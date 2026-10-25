using System;
using System.IO;

namespace SourceCodeEditor;

/// <summary>
/// Resolve an editor document path to the closest navigable directory.
/// This is deliberately independent of TreeView so missing/renamed paths
/// can be tested without creating a window.
/// </summary>
internal readonly record struct ExplorerNavigationPlan(
    string TargetFile, string DirectoryToShow, bool FileExists, bool OriginalDirectoryExists)
{
    public static ExplorerNavigationPlan ForFile(string filePath, string fallbackDirectory)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A saved filename is required.", nameof(filePath));
        var normalizedFile = Path.GetFullPath(filePath);
        var originalDirectory = Path.GetDirectoryName(normalizedFile)
            ?? throw new ArgumentException("The file has no parent directory.", nameof(filePath));
        bool originalExists = Directory.Exists(originalDirectory);
        string? candidate = originalDirectory;
        while (candidate != null && !Directory.Exists(candidate))
        {
            try { candidate = Directory.GetParent(candidate)?.FullName; }
            catch (IOException) { candidate = null; }
            catch (UnauthorizedAccessException) { candidate = null; }
        }
        string navigable = candidate ?? fallbackDirectory;
        if (!Directory.Exists(navigable))
            navigable = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new ExplorerNavigationPlan(normalizedFile, navigable,
            File.Exists(normalizedFile), originalExists);
    }
}
