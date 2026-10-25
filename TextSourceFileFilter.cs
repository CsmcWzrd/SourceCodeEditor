using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SourceCodeEditor;

/// <summary>
/// Explorer file visibility: known source/text extensions, dotfiles, and other
/// files that pass a short text-content probe. Hidden attributes never exclude
/// a file. A probe reads no more than 4096 bytes and does not modify files.
/// </summary>
internal static class TextSourceFileFilter
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".log", ".md", ".markdown", ".rst", ".csv", ".tsv",
        ".json", ".jsonl", ".xml", ".xsl", ".xslt", ".xsd", ".yaml", ".yml", ".toml",
        ".ini", ".cfg", ".config", ".conf", ".properties", ".env", ".editorconfig",
        ".gitignore", ".gitattributes", ".gitmodules", ".dockerignore", ".htaccess",
        ".cs", ".csx", ".csproj", ".sln", ".slnx", ".props", ".targets", ".resx",
        ".c", ".h", ".cpp", ".hpp", ".cc", ".cxx", ".hxx", ".inl", ".ipp",
        ".rs", ".go", ".zig", ".asm", ".s", ".S", ".inc", ".nasm",
        ".py", ".pyi", ".pyx", ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx",
        ".java", ".kt", ".kts", ".scala", ".sc", ".swift", ".m", ".mm",
        ".rb", ".rake", ".php", ".pl", ".pm", ".lua", ".sh", ".bash", ".zsh",
        ".fish", ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".vbs", ".wsf",
        ".html", ".htm", ".css", ".scss", ".sass", ".less", ".vue", ".svelte",
        ".sql", ".graphql", ".gql", ".proto", ".idl", ".f", ".f90", ".f95",
        ".pas", ".pp", ".d", ".dart", ".erl", ".ex", ".exs", ".hs", ".elm",
        ".r", ".R", ".jl", ".ipynb", ".tex", ".bib", ".cmake", ".gradle",
        ".mk", ".make", ".mak", ".ac", ".am", ".m4", ".def", ".rc",
        ".CMOT", ".CMTV", ".HMOT", ".HMTV", ".cmot", ".cmtv", ".hmot", ".hmtv"
    };

    private static readonly HashSet<string> KnownTextNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Makefile", "Dockerfile", "Containerfile", "CMakeLists.txt", "README",
        "LICENSE", "COPYING", "NOTICE", "AUTHORS", "CHANGELOG", "Gemfile",
        "Rakefile", "Vagrantfile", "BUILD", "WORKSPACE", ".gitignore", ".env"
    };

    public static bool IsTextOrSource(string path)
    {
        try
        {
            string name = Path.GetFileName(path);
            if (Extensions.Contains(Path.GetExtension(name)) || KnownTextNames.Contains(name)) return true;
            // Dotfiles and unknown extensions can also be ordinary text. Avoid
            // hiding those by inspecting a small prefix rather than blacklisting.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.SequentialScan);
            Span<byte> bytes = stackalloc byte[4096];
            int length = stream.Read(bytes);
            if (length == 0) return true;
            var sample = bytes[..length];
            if (length >= 4 && (sample[..4].SequenceEqual(new byte[] { 0xFF, 0xFE, 0, 0 }) ||
                                sample[..4].SequenceEqual(new byte[] { 0, 0, 0xFE, 0xFF }))) return true;
            if (length >= 2 && (sample[..2].SequenceEqual(new byte[] { 0xFF, 0xFE }) ||
                                sample[..2].SequenceEqual(new byte[] { 0xFE, 0xFF }))) return true;
            if (length >= 3 && sample[..3].SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })) return true;
            // Known binary formats, regardless of extension.
            if (length >= 4 &&
                (sample[..4].SequenceEqual(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }) ||
                 sample[..4].SequenceEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 }) ||
                 sample[..4].SequenceEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }))) return false;
            if (length >= 2 && sample[0] == (byte)'M' && sample[1] == (byte)'Z') return false;
            int unusual = 0;
            foreach (byte b in sample)
            {
                if (b == 0) return false;
                if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D && b != 0x0C && b != 0x08)
                    unusual++;
            }
            return unusual * 100 < length * 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
