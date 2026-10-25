using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SourceCodeEditor;

/// <summary>
/// A persistent Windows cmd.exe subprocess presented inside an editor tab.
/// It uses redirected standard streams, not an external console window.
/// Ordinary shell commands work; full-screen/TUI console programs requiring
/// a Win32 console or ConPTY are intentionally not supported.
/// </summary>
public sealed class EmbeddedShellControl : UserControl
{
    private readonly RichTextBox output = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false,
        Font = new Font("Consolas", 10f), BackColor = Color.FromArgb(19, 22, 28),
        ForeColor = Color.FromArgb(220, 226, 232), BorderStyle = BorderStyle.None,
        DetectUrls = false, HideSelection = false
    };
    private readonly TextBox input = new()
    {
        Dock = DockStyle.Fill, Font = new Font("Consolas", 10f),
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly ToolStripStatusLabel shellState = new("Starting...");
    private TableLayoutPanel? shellLayout;
    private ToolStrip? shellToolbar;
    private readonly List<string> commandHistory = new();
    private readonly ConcurrentQueue<string> pendingOutput = new();
    private readonly System.Windows.Forms.Timer outputDrainTimer = new() { Interval = 75 };
    private int pendingCharacters;
    private int historyPosition;
    private Process? process;
    private bool stopping;
    private bool started;
    private const int OutputLimit = 1_500_000;

    public string InitialDirectory { get; }
    public bool IsRunning => process is { HasExited: false };

    public EmbeddedShellControl(string directory)
    {
        InitialDirectory = Path.GetFullPath(directory);
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(19, 22, 28);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        var header = new ToolStrip
        {
            Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden,
            AutoSize = false, RenderMode = ToolStripRenderMode.System
        };
        header.Items.Add(new ToolStripLabel("CMD: " + InitialDirectory) { AutoToolTip = true });
        header.Items.Add(new ToolStripSeparator());
        var clear = new ToolStripButton("Clear");
        clear.Click += (_, _) => output.Clear();
        var stop = new ToolStripButton("Stop");
        stop.Click += (_, _) => StopShell();
        header.Items.Add(clear);
        header.Items.Add(stop);
        header.Items.Add(new ToolStripSeparator());
        header.Items.Add(new ToolStripLabel("Status:"));
        header.Items.Add(shellState);
        var inputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = Color.FromArgb(26, 31, 38), Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 23));
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, Text = ">", TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.LightGreen
        }, 0, 0);
        inputPanel.Controls.Add(input, 1, 0);
        input.KeyDown += InputKeyDown;
        outputDrainTimer.Tick += (_, _) => DrainOutput();
        outputDrainTimer.Start();
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(output, 0, 1);
        root.Controls.Add(inputPanel, 0, 2);
        Controls.Add(root);
        shellLayout = root;
        shellToolbar = header;
        ApplyInterfaceFont(12f);
        AppendOutput("Embedded Windows Command Prompt — " + InitialDirectory + Environment.NewLine);
        AppendOutput("Commands run in a persistent cmd.exe process. Type 'exit' to end it." + Environment.NewLine);
        AppendOutput("Note: full-screen console applications need a ConPTY terminal and are not supported here." + Environment.NewLine + Environment.NewLine);
    }

    /// <summary>Apply the application UI font size to the embedded shell tab.</summary>
    public void ApplyInterfaceFont(float points)
    {
        points = Math.Clamp(points, 8f, 32f);
        Font = new Font("Segoe UI", points, FontStyle.Regular, GraphicsUnit.Point);
        if (shellToolbar != null) shellToolbar.Font = Font;
        output.Font = new Font("Consolas", points, FontStyle.Regular, GraphicsUnit.Point);
        input.Font = new Font("Consolas", points, FontStyle.Regular, GraphicsUnit.Point);
        if (shellLayout != null)
        {
            shellLayout.RowStyles[0].Height = Math.Max(27, Font.Height + 10);
            shellLayout.RowStyles[2].Height = Math.Max(31, input.Font.Height + 12);
        }
        PerformLayout();
    }

    public void StartShell()
    {
        if (started || IsDisposed) return;
        started = true;
        try
        {
            // Windows commonly runs cmd.exe using the active OEM code page.
            // Register code-page encodings so output isn't forcibly interpreted as UTF-8.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/Q /D",
                WorkingDirectory = InitialDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = oem,
                StandardErrorEncoding = oem,
                StandardInputEncoding = oem
            };
            process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += ProcessOutput;
            process.ErrorDataReceived += ProcessOutput;
            process.Exited += ProcessExited;
            if (!process.Start()) throw new InvalidOperationException("Windows Command Prompt did not start.");
            process.StandardInput.AutoFlush = true;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            shellState.Text = "Running";
            input.Enabled = true;
            input.Focus();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            shellState.Text = "Failed";
            input.Enabled = false;
            AppendOutput("\r\nCannot start cmd.exe: " + ex.Message + "\r\n");
            StopShell();
        }
    }

    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            SendCommand();
        }
        else if (e.KeyCode == Keys.Up)
        {
            e.SuppressKeyPress = true;
            if (commandHistory.Count > 0)
            {
                historyPosition = Math.Max(0, historyPosition - 1);
                input.Text = commandHistory[historyPosition];
                input.SelectionStart = input.TextLength;
            }
        }
        else if (e.KeyCode == Keys.Down)
        {
            e.SuppressKeyPress = true;
            if (commandHistory.Count > 0)
            {
                historyPosition = Math.Min(commandHistory.Count, historyPosition + 1);
                input.Text = historyPosition == commandHistory.Count ? "" : commandHistory[historyPosition];
                input.SelectionStart = input.TextLength;
            }
        }
    }

    private void SendCommand()
    {
        string command = input.Text;
        input.Clear();
        if (!IsRunning) { AppendOutput("[The shell is not running]\r\n"); return; }
        if (!string.IsNullOrWhiteSpace(command))
        {
            commandHistory.Add(command);
            if (commandHistory.Count > 100) commandHistory.RemoveAt(0);
        }
        historyPosition = commandHistory.Count;
        AppendOutput("> " + command + Environment.NewLine);
        try { process!.StandardInput.WriteLine(command); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            AppendOutput("[Cannot send command: " + ex.Message + "]" + Environment.NewLine);
        }
    }

    private void ProcessOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null) PostOutput(e.Data + Environment.NewLine);
    }

    private void ProcessExited(object? sender, EventArgs e)
    {
        if (stopping) return;
        PostOutput(Environment.NewLine + "[Shell exited]" + Environment.NewLine);
        PostToUI(() => { shellState.Text = "Exited"; input.Enabled = false; });
    }

    private void PostOutput(string text)
    {
        if (stopping || IsDisposed) return;
        // Throttle high-volume output without posting one WinForms message per line.
        int count = Interlocked.Add(ref pendingCharacters, text.Length);
        if (count > 1_000_000)
        {
            Interlocked.Add(ref pendingCharacters, -text.Length);
            return;
        }
        pendingOutput.Enqueue(text);
    }

    private void DrainOutput()
    {
        if (IsDisposed) return;
        var batch = new StringBuilder(65536);
        int lines = 0;
        while (lines++ < 400 && batch.Length < 65536 && pendingOutput.TryDequeue(out var chunk))
        {
            Interlocked.Add(ref pendingCharacters, -chunk.Length);
            batch.Append(chunk);
        }
        if (batch.Length > 0) AppendOutput(batch.ToString());
    }

    private void PostToUI(Action action)
    {
        if (stopping || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed && !stopping) action();
            }));
        }
        catch (InvalidOperationException) { }
        catch (ObjectDisposedException) { }
    }

    private void AppendOutput(string text)
    {
        if (output.IsDisposed) return;
        // Keep memory bounded when commands produce large streams of text.
        if (output.TextLength + text.Length > OutputLimit)
        {
            int drop = Math.Min(output.TextLength, Math.Max(100_000, text.Length));
            output.Select(0, drop);
            output.SelectedText = "";
        }
        output.AppendText(text);
        output.SelectionStart = output.TextLength;
        output.ScrollToCaret();
    }

    public void FocusCommand() { if (input.Enabled) input.Focus(); }

    public void StopShell()
    {
        if (stopping) return;
        stopping = true;
        outputDrainTimer.Stop();
        DrainOutput();
        input.Enabled = false;
        shellState.Text = "Stopped";
        if (process == null) return;
        try
        {
            if (!process.HasExited)
            {
                try { process.StandardInput.WriteLine("exit"); } catch (IOException) { }
                // Do not wait on the UI thread. Kill child processes on disposal.
                if (!process.WaitForExit(150)) process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException) { }
        finally { process.Dispose(); process = null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopShell();
        base.Dispose(disposing);
    }
}
