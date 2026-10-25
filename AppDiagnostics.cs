using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SourceCodeEditor;

/// <summary>Local crash log. Does not send telemetry or user documents anywhere.</summary>
internal static class AppDiagnostics
{
    private static readonly object Gate = new();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SourceCodeEditor");
    private static readonly string LogPath = Path.Combine(Folder, "errors.log");

    public static void Record(Exception exception, string category)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 4 * 1024 * 1024)
                {
                    string rotated = LogPath + ".old";
                    File.Move(LogPath, rotated, true);
                }
                File.AppendAllText(LogPath,
                    $"[{DateTimeOffset.Now:O}] {category}{Environment.NewLine}" +
                    exception + Environment.NewLine + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // A broken log path must not cause a recursive exception.
        }
    }

    public static void ReportUiException(Exception exception)
    {
        Record(exception, "winforms-ui");
        try
        {
            MessageBox.Show(
                "SourceCodeEditor encountered an unexpected UI error.\n\n" +
                exception.Message + "\n\nThe error has been logged locally in:\n" + LogPath +
                "\n\nSave or recover your changes and restart the editor if it behaves unexpectedly.",
                "SourceCodeEditor error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
