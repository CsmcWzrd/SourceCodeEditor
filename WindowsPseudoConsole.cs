using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace SourceCodeEditor;

/// <summary>
/// A real Windows ConPTY session, rather than redirected, line-buffered cmd.exe.
/// Supported by Windows 10 build 17763+ and Windows 11. VT/UTF-8 comes directly
/// from the pseudo console; the shell remains interactive (including Ctrl+C,
/// prompt redraw, arrows, and full-screen programs).
/// </summary>
internal sealed class WindowsPseudoConsole : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X, Y;
        public Coord(int x, int y) { X = checked((short)x); Y = checked((short)y); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    private const int ExtendedStartupInfoPresent = 0x00080000;
    private const int CreateUnicodeEnvironment = 0x00000400;
    private const int ProcThreadAttributePseudoConsole = 0x00020016;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe,
        IntPtr pipeAttributes, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output,
        uint flags, out IntPtr pseudoConsole);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr pseudoConsole, Coord size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr pseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr attributeList,
        int attributeCount, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr attributeList, uint flags,
        IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string? applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
        int creationFlags, IntPtr environment, string currentDirectory,
        ref StartupInfoEx startupInfo, out ProcessInformation processInformation);

    private readonly Channel<byte[]> inputQueue = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly object sizeLock = new();
    private IntPtr pseudoConsole;
    private FileStream? input;
    private FileStream? output;
    private int processId;
    private int disposed;
    private volatile bool running;
    private int lastColumns, lastRows;

    public event Action<string>? OutputReceived;
    public event Action? ShellExited;
    public bool IsRunning => running && Volatile.Read(ref disposed) == 0;
    public int ProcessId => processId;

    public WindowsPseudoConsole(string folder, int columns, int rows)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            throw new PlatformNotSupportedException("Embedded ConPTY terminals require Windows 10 version 1809 or later.");
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        columns = Math.Clamp(columns, 20, short.MaxValue);
        rows = Math.Clamp(rows, 4, short.MaxValue);
        lastColumns = columns;
        lastRows = rows;

        IntPtr ptyInputRead = IntPtr.Zero, hostInputWrite = IntPtr.Zero;
        IntPtr hostOutputRead = IntPtr.Zero, ptyOutputWrite = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        bool initialized = false;
        try
        {
            if (!CreatePipe(out ptyInputRead, out hostInputWrite, IntPtr.Zero, 0)) ThrowWin32("CreatePipe (input)");
            if (!CreatePipe(out hostOutputRead, out ptyOutputWrite, IntPtr.Zero, 0)) ThrowWin32("CreatePipe (output)");
            int hr = CreatePseudoConsole(new Coord(columns, rows), ptyInputRead, ptyOutputWrite, 0, out pseudoConsole);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            // Keep both pseudoconsole endpoints alive until CreateProcess has
            // attached the child. Closing them earlier can race child startup.

            IntPtr listSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
            if (listSize == IntPtr.Zero) ThrowWin32("InitializeProcThreadAttributeList (size)");
            attributeList = Marshal.AllocHGlobal(listSize);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref listSize))
                ThrowWin32("InitializeProcThreadAttributeList");
            initialized = true;
            // PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE is special: lpValue must be
            // the HPCON value itself. Passing ref pseudoConsole here yields
            // HPCON* (one extra level of indirection), which can cause cmd.exe
            // to detach into the user's default terminal (Windows Terminal)
            // while our embedded tab remains blank. See Microsoft's ConPTY
            // sample: UpdateProcThreadAttribute(..., hpc, sizeof(hpc), ...).
            if (!UpdateProcThreadAttribute(attributeList, 0, (IntPtr)ProcThreadAttributePseudoConsole,
                pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                ThrowWin32("UpdateProcThreadAttribute");

            string exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var commandLine = new StringBuilder("\"" + exe + "\" /D");
            var si = new StartupInfoEx
            {
                StartupInfo = new StartupInfo { cb = Marshal.SizeOf<StartupInfoEx>() },
                lpAttributeList = attributeList
            };
            if (!CreateProcess(exe, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                IntPtr.Zero, folder, ref si, out var created))
                ThrowWin32("CreateProcess (cmd.exe)");
            processId = created.dwProcessId;
            CloseHandle(created.hThread);
            CloseHandle(created.hProcess);
            // Microsoft recommends releasing the PTY-side pipe handles only
            // after creating the child process to prevent startup races.
            CloseHandle(ptyInputRead); ptyInputRead = IntPtr.Zero;
            CloseHandle(ptyOutputWrite); ptyOutputWrite = IntPtr.Zero;

            input = new FileStream(new SafeFileHandle(hostInputWrite, ownsHandle: true), FileAccess.Write, 4096, false);
            hostInputWrite = IntPtr.Zero;
            output = new FileStream(new SafeFileHandle(hostOutputRead, ownsHandle: true), FileAccess.Read, 4096, false);
            hostOutputRead = IntPtr.Zero;
            running = true;
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributeList);
            if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList);
            CloseIfValid(ptyInputRead);
            CloseIfValid(ptyOutputWrite);
            CloseIfValid(hostInputWrite);
            CloseIfValid(hostOutputRead);
        }
    }

    /// <summary>Start reading only after listeners are attached (do not lose the first CMD prompt).</summary>
    public void BeginStreaming()
    {
        if (!IsRunning) return;
        _ = Task.Run(ReadOutput);
        _ = Task.Run(WriteInput);
    }

    private static void ThrowWin32(string operation) =>
        throw new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed");

    private static void CloseIfValid(IntPtr handle)
    {
        if (handle != IntPtr.Zero && handle != new IntPtr(-1)) CloseHandle(handle);
    }

    private void ReadOutput()
    {
        var bytes = new byte[16_384];
        var chars = new char[32_768];
        var decoder = new UTF8Encoding(false, false).GetDecoder();
        try
        {
            while (IsRunning && output != null)
            {
                int count = output.Read(bytes, 0, bytes.Length);
                if (count == 0) break;
                int written = decoder.GetChars(bytes, 0, count, chars, 0, false);
                if (written > 0) OutputReceived?.Invoke(new string(chars, 0, written));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Closing the tab tears down the pipe while this worker is reading.
        }
        finally
        {
            running = false;
            if (Volatile.Read(ref disposed) == 0) ShellExited?.Invoke();
        }
    }

    private async Task WriteInput()
    {
        try
        {
            await foreach (var bytes in inputQueue.Reader.ReadAllAsync())
            {
                if (Volatile.Read(ref disposed) != 0 || input == null) break;
                await input.WriteAsync(bytes.AsMemory());
                await input.FlushAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Pipe has closed (for example, after 'exit').
        }
    }

    public void Send(string text)
    {
        if (IsRunning && text.Length > 0)
            inputQueue.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
    }

    public void Resize(int columns, int rows)
    {
        if (!IsRunning) return;
        columns = Math.Clamp(columns, 20, short.MaxValue);
        rows = Math.Clamp(rows, 4, short.MaxValue);
        lock (sizeLock)
        {
            if (!IsRunning || (columns == lastColumns && rows == lastRows)) return;
            if (ResizePseudoConsole(pseudoConsole, new Coord(columns, rows)) == 0)
            {
                lastColumns = columns;
                lastRows = rows;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        running = false;
        inputQueue.Writer.TryComplete();
        // Stop the process tree before releasing the pseudo console so the
        // host's close operation cannot wait on a still-running child shell.
        if (processId != 0)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }
        }
        try { input?.Dispose(); } catch (IOException) { }
        lock (sizeLock)
        {
            if (pseudoConsole != IntPtr.Zero)
            {
                ClosePseudoConsole(pseudoConsole);
                pseudoConsole = IntPtr.Zero;
            }
        }
        try { output?.Dispose(); } catch (IOException) { }
    }
}
