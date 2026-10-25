using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceCodeEditor;

/// <summary>
/// Headless Windows-only integration check for ConPTY attachment. It exercises
/// the same native interop as the embedded shell without opening the editor.
/// Run SourceCodeEditor.exe --self-test-conpty [report-path] on Windows x64.
/// Requires the child prompt and the result of an 'echo' command to arrive
/// through our own ConPTY output pipe; an externally opened Windows Terminal
/// cannot satisfy this test.
/// </summary>
internal static class ConPtySelfTest
{
    public static int Run(string? reportPath)
    {
        reportPath = string.IsNullOrWhiteSpace(reportPath)
            ? Path.Combine(Path.GetTempPath(), "SourceCodeEditor-ConPTY-self-test.txt")
            : Path.GetFullPath(reportPath);
        var transcript = new StringBuilder();
        var sync = new object();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string marker = "__SOURCE_CODE_EDITOR_CONPTY_RESULT_01961__";
        bool passed = false;
        string summary;
        try
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            using var terminal = new WindowsPseudoConsole(folder, 100, 30);
            terminal.OutputReceived += chunk =>
            {
                lock (sync)
                {
                    transcript.Append(chunk);
                    // An interactive CMD echoes 'echo TOKEN' and then prints
                    // 'TOKEN' on its own line. Count both to distinguish a
                    // real command result from mere input echoing.
                    var text = transcript.ToString();
                    if (text.Split(marker, StringSplitOptions.None).Length >= 3)
                        completed.TrySetResult(true);
                }
            };
            terminal.BeginStreaming();
            terminal.Send("echo " + marker + "\r");
            passed = completed.Task.Wait(TimeSpan.FromSeconds(15));
            summary = passed
                ? "PASS: CMD is attached to SourceCodeEditor's ConPTY output pipe."
                : "FAIL: CMD produced no verified echo result through the ConPTY pipe within 15 seconds.";
        }
        catch (Exception ex)
        {
            summary = "FAIL: " + ex;
        }
        try
        {
            var parent = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            lock (sync)
            {
                File.WriteAllText(reportPath, summary + Environment.NewLine + Environment.NewLine +
                    "Captured ConPTY output:" + Environment.NewLine + transcript, new UTF8Encoding(false));
            }
        }
        catch
        {
            // The exit code still reports the ConPTY result if the log cannot be saved.
        }
        return passed ? 0 : 1;
    }
}
