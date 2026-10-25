using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace SourceCodeEditor;

internal sealed class EditorDocument
{
    public string? Path { get; set; }
    public string Name { get; set; } = "Untitled";
    public string Buffer { get; set; } = "";
    public bool Dirty { get; set; }
    public bool Closed { get; set; }
    // Persist the recovery source without retaining a writable path.
    public string? RecoverySourcePath { get; set; }
    public bool MissingOnDisk { get; set; }
    public bool UnavailableOnDisk { get; set; }
    public bool ExternalConflict { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime LastNotifiedWriteUtc { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public long LastNotifiedLength { get; set; }
    public int Caret { get; set; }
    public string EncodingName { get; set; } = "utf-8";
    public bool HasBom { get; set; }
    public string Newline { get; set; } = "\r\n";
    public long LastLength { get; set; }
    // SHA-256 of exact disk bytes, to detect same-size/same-timestamp rewrites.
    public string DiskHash { get; set; } = "";
    public DateTime WriteUtc { get; set; }
    public float Zoom { get; set; } = 1f;
    public bool WordWrap { get; set; }
    public HashSet<int> Bookmarks { get; set; } = new();
    public TabPage? Tab;
    public LineNumberedRichTextBox? View;
    public override string ToString() => Name +
        (MissingOnDisk ? " [missing]" : UnavailableOnDisk ? " [inaccessible]" :
         ExternalConflict ? " [changed on disk]" : "") + (Dirty ? " *" : "");
}

internal sealed class EditorSession
{
    public List<EditorDocument> Documents { get; set; } = new();
    public int ActiveDocument { get; set; }
    public string ExplorerRoot { get; set; } = "";
    public int NavigationIndex { get; set; }
    public int LeftWidth { get; set; } = 216;
    public float ApplicationFontPoints { get; set; } = 12f;
    public float EditorFontPoints { get; set; } = 12f;
    public List<string> RecentFiles { get; set; } = new();
}

internal static class TextFiles
{
    public static (string text, string encodingName, bool bom, string newline, string hash) Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        // Reject obviously binary files instead of silently destroying their contents.
        int sample = Math.Min(bytes.Length, 32768);
        bool hasUnicodeBom = sample >= 2 && ((bytes[0] == 0xff && bytes[1] == 0xfe) || (bytes[0] == 0xfe && bytes[1] == 0xff));
        bool hasUtf8Bom = sample >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
        bool hasUtf32BeBom = sample >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff;
        if (!hasUnicodeBom && !hasUtf8Bom && !hasUtf32BeBom)
        {
            int unexpected = 0;
            for (int i = 0; i < sample; i++)
            {
                byte b = bytes[i];
                if (b == 0) throw new InvalidDataException("This file contains binary NUL bytes.");
                if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D or 0x0C or 0x08)) unexpected++;
            }
            if (sample > 0 && unexpected * 100 >= sample * 2)
                throw new InvalidDataException("This file contains non-text control bytes.");
        }
        Encoding encoding;
        int skip;
        if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0)
            { encoding = new UTF32Encoding(false, true, true); skip = 4; }
        else if (hasUtf32BeBom)
            { encoding = new UTF32Encoding(true, true, true); skip = 4; }
        else if (hasUtf8Bom)
            { encoding = new UTF8Encoding(true, true); skip = 3; }
        else if (hasUnicodeBom && bytes[0] == 0xff)
            { encoding = new UnicodeEncoding(false, true, true); skip = 2; }
        else if (hasUnicodeBom)
            { encoding = new UnicodeEncoding(true, true, true); skip = 2; }
        else
        {
            skip = 0;
            try { _ = new UTF8Encoding(false, true).GetString(bytes); encoding = new UTF8Encoding(false, true); }
            catch (DecoderFallbackException) { encoding = Encoding.Latin1; }
        }
        string text = encoding.GetString(bytes, skip, bytes.Length - skip);
        int crlf = Count(text, "\r\n");
        int lf = Count(text, "\n") - crlf;
        int cr = Count(text, "\r") - crlf;
        string newline = crlf >= lf && crlf >= cr ? "\r\n" : lf >= cr ? "\n" : "\r";
        string encodingName = encoding.CodePage switch
        {
            1200 => "utf-16", 1201 => "utf-16be",
            12000 => "utf-32", 12001 => "utf-32be",
            65001 => "utf-8", _ => "iso-8859-1"
        };
        return (text, encodingName, skip != 0, newline, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static int Count(string input, string needle)
    {
        int count = 0, i = 0;
        while ((i = input.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }

    public static byte[] Encode(string input, string encodingName, bool bom, string newline)
    {
        Encoding encoding = encodingName.ToLowerInvariant() switch
        {
            "utf-16" => new UnicodeEncoding(false, bom, true),
            "utf-16be" => new UnicodeEncoding(true, bom, true),
            "utf-32" => new UTF32Encoding(false, bom, true),
            "utf-32be" => new UTF32Encoding(true, bom, true),
            "iso-8859-1" => Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            _ => new UTF8Encoding(bom, true)
        };
        // RichTextBox normalizes newlines while editing. Normalize once on write.
        string normalized = input.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Replace("\n", newline, StringComparison.Ordinal);
        byte[] body = encoding.GetBytes(normalized);
        byte[] prefix = bom ? encoding.GetPreamble() : Array.Empty<byte>();
        if (prefix.Length == 0) return body;
        byte[] result = new byte[body.Length + prefix.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, result, prefix.Length, body.Length);
        return result;
    }

    public static void WriteAtomic(string path, byte[] bytes)
    {
        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        var temp = System.IO.Path.Combine(folder, ".sourcecodeeditor_" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
