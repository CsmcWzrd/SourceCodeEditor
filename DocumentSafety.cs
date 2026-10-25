using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;

namespace SourceCodeEditor;

/// <summary>
/// Centralized filesystem state checks. File.Exists() is deliberately not used
/// to decide whether a saved document is safe to overwrite: it reports false
/// for both missing paths and several access failures.
/// </summary>
internal enum FileAvailability { Present, Missing, Directory, Inaccessible }
internal enum SaveTargetState { Unmodified, ModifiedExternally, Missing, Unavailable }

internal static class DocumentSafety
{
    public static FileAvailability Inspect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return FileAvailability.Missing;
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0
                ? FileAvailability.Directory : FileAvailability.Present;
        }
        catch (FileNotFoundException) { return FileAvailability.Missing; }
        catch (DirectoryNotFoundException) { return FileAvailability.Missing; }
        catch (UnauthorizedAccessException) { return FileAvailability.Inaccessible; }
        catch (IOException) { return FileAvailability.Inaccessible; }
        catch (ArgumentException) { return FileAvailability.Inaccessible; }
        catch (NotSupportedException) { return FileAvailability.Inaccessible; }
    }

    public static string HashDiskFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static (DateTime stamp, long length) Stamp(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        return (info.LastWriteTimeUtc, info.Length);
    }

    public static SaveTargetState AssessSaveTarget(EditorDocument doc)
    {
        FileAvailability state = Inspect(doc.Path);
        if (state == FileAvailability.Missing) return SaveTargetState.Missing;
        if (state != FileAvailability.Present) return SaveTargetState.Unavailable;
        if (doc.ExternalConflict || doc.MissingOnDisk) return SaveTargetState.ModifiedExternally;
        try
        {
            var (stamp, length) = Stamp(doc.Path!);
            if (stamp != doc.WriteUtc || length != doc.LastLength)
                return SaveTargetState.ModifiedExternally;
            if (!string.IsNullOrEmpty(doc.DiskHash))
            {
                string nowHash = HashDiskFile(doc.Path!);
                if (!string.Equals(nowHash, doc.DiskHash, StringComparison.OrdinalIgnoreCase))
                    return SaveTargetState.ModifiedExternally;
            }
            return SaveTargetState.Unmodified;
        }
        catch (IOException) { return SaveTargetState.Unavailable; }
        catch (UnauthorizedAccessException) { return SaveTargetState.Unavailable; }
    }

    /// <summary>
    /// Recover the cached text into an untitled document so ordinary Save always
    /// invokes Save As. Never fabricate the removed directory or old file.
    /// </summary>
    public static void DetachToRecoveredBuffer(EditorDocument doc)
    {
        if (doc.Path == null) return;
        string oldPath = doc.Path;
        doc.RecoverySourcePath = oldPath;
        doc.Path = null;
        doc.Name = Path.GetFileName(oldPath) + " (Recovered)";
        doc.Dirty = true;
        doc.MissingOnDisk = false;
        doc.ExternalConflict = false;
        doc.UnavailableOnDisk = false;
        doc.WriteUtc = DateTime.MinValue;
        doc.LastLength = 0;
        doc.DiskHash = "";
    }

    public static bool DetachIfDuplicate(EditorDocument candidate, IEnumerable<EditorDocument> priorDocuments)
    {
        if (candidate.Path == null) return false;
        string originalPath = candidate.Path;
        try { originalPath = Path.GetFullPath(originalPath); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { }
        foreach (var existing in priorDocuments)
        {
            if (existing.Path == null) continue;
            string existingPath = existing.Path;
            try { existingPath = Path.GetFullPath(existingPath); }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { }
            if (!string.Equals(originalPath, existingPath, StringComparison.OrdinalIgnoreCase)) continue;
            DetachToRecoveredBuffer(candidate);
            return true;
        }
        return false;
    }

    public static string SafeSaveDirectory(string? existingPath, string documents)
    {
        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            try
            {
                string? parent = Path.GetDirectoryName(Path.GetFullPath(existingPath));
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) return parent;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        return documents;
    }
}

/// <summary>
/// Atomic session storage and corrupt-session rescue. A damaged session is
/// copied aside before UI startup has a chance to replace it with new data.
/// </summary>
internal static class SessionFileStore
{
    // A damaged/malicious session must not allocate unbounded process memory.
    private const long MaxSessionBytes = 128L * 1024L * 1024L;
    private const int MaxDocuments = 10000;

    public static EditorSession Load(string path)
    {
        if (new FileInfo(path).Length > MaxSessionBytes)
            throw new InvalidDataException("Session file exceeds the 128 MiB recovery limit.");
        string json = File.ReadAllText(path);
        using var parsed = JsonDocument.Parse(json);
        EditorSession? result = parsed.RootElement.ValueKind == JsonValueKind.Array
            ? new EditorSession { Documents = JsonSerializer.Deserialize<System.Collections.Generic.List<EditorDocument>>(json) ?? new() }
            : JsonSerializer.Deserialize<EditorSession>(json);
        if (result?.Documents == null) throw new InvalidDataException("Session document list is invalid.");
        if (result.Documents.Count > MaxDocuments)
            throw new InvalidDataException("Session has too many document entries.");
        if (result.Documents.Exists(d => d == null))
            throw new InvalidDataException("Session contains a null document.");
        return result;
    }

    public static void Save(string path, EditorSession state)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string? RescueCorruptSession(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string target = path + ".corrupt_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff") + ".json";
            File.Copy(path, target, false);
            return target;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
