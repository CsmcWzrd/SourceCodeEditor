using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace SourceCodeEditor;

/// <summary>
/// Headless destructive-fixture regression tests. Every test runs in a unique
/// temporary folder; no user files, Documents folder, or application sessions
/// are modified. Invoke: SourceCodeEditor.exe --self-test-negative REPORT.json
/// </summary>
internal static class NegativeSelfTest
{
    private sealed record Result(string Name, bool Passed, string Message);

    private static void Require(bool success, string message = "assertion failed")
    {
        if (!success) throw new InvalidOperationException(message);
    }

    private static void ExpectThrows<T>(Action operation) where T : Exception
    {
        try { operation(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " but no exception was thrown");
    }

    private static EditorDocument Snapshot(string path)
    {
        var file = new FileInfo(path);
        return new EditorDocument
        {
            Path = path, Name = file.Name, Buffer = "cached\noriginal",
            WriteUtc = file.LastWriteTimeUtc, LastLength = file.Length,
            Newline = "\n", EncodingName = "utf-8", Closed = true,
            DiskHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
        };
    }

    public static int Run(string? reportPath)
    {
        reportPath = string.IsNullOrWhiteSpace(reportPath)
            ? Path.Combine(Path.GetTempPath(), "SourceCodeEditor-negative-self-test.json")
            : Path.GetFullPath(reportPath);
        var results = new List<Result>();
        string root = Path.Combine(Path.GetTempPath(), "SourceCodeEditorNegative_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        void Test(string name, Action<string> action)
        {
            string folder = Path.Combine(root, results.Count.ToString("D3"));
            Directory.CreateDirectory(folder);
            try
            {
                action(folder);
                results.Add(new Result(name, true, "PASS"));
            }
            catch (Exception ex)
            {
                results.Add(new Result(name, false, ex.GetType().Name + ": " + ex.Message));
            }
        }

        string Write(string folder, string name = "example.cs", string content = "initial")
        {
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, content);
            return path;
        }

        try
        {
            // Missing, moved, inaccessible, locked and conflicting files.
            Test("missing:null-path", f => Require(DocumentSafety.Inspect(null) == FileAvailability.Missing));
            Test("missing:empty-path", f => Require(DocumentSafety.Inspect("") == FileAvailability.Missing));
            Test("missing:never-created", f => Require(DocumentSafety.Inspect(Path.Combine(f, "gone.txt")) == FileAvailability.Missing));
            Test("present:ordinary-source", f => Require(DocumentSafety.Inspect(Write(f)) == FileAvailability.Present));
            Test("missing:file-deleted-after-close", f =>
            {
                string path = Write(f); var d = Snapshot(path); File.Delete(path);
                Require(DocumentSafety.Inspect(d.Path) == FileAvailability.Missing);
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.Missing);
                Require(d.Buffer == "cached\noriginal", "cached content was modified");
            });
            Test("missing:entire-parent-folder-deleted", f =>
            {
                string child = Path.Combine(f, "child"); Directory.CreateDirectory(child);
                var d = Snapshot(Write(child)); Directory.Delete(child, true);
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.Missing);
            });
            Test("missing:parent-folder-renamed", f =>
            {
                string child = Path.Combine(f, "child"); Directory.CreateDirectory(child);
                var d = Snapshot(Write(child)); Directory.Move(child, Path.Combine(f, "renamed"));
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.Missing);
            });
            Test("missing:file-moved-away", f =>
            {
                var d = Snapshot(Write(f)); File.Move(d.Path!, Path.Combine(f, "moved.cs"));
                Require(DocumentSafety.Inspect(d.Path) == FileAvailability.Missing);
            });
            Test("invalid:directory-instead-of-file", f =>
            {
                string path = Path.Combine(f, "folder.cs"); Directory.CreateDirectory(path);
                Require(DocumentSafety.Inspect(path) == FileAvailability.Directory);
            });
            Test("invalid:embedded-NUL-path", f => Require(DocumentSafety.Inspect("a\0b") == FileAvailability.Inaccessible));
            Test("recovery:severs-destroyed-disk-path", f =>
            {
                var d = Snapshot(Write(f)); string old = d.Path!; File.Delete(old);
                DocumentSafety.DetachToRecoveredBuffer(d);
                Require(d.Path == null && d.RecoverySourcePath == old && d.Dirty);
                Require(!File.Exists(old), "file recreated without Save As");
            });
            Test("recovery:old-folder-is-not-recreated", f =>
            {
                string folder = Path.Combine(f, "removed"); Directory.CreateDirectory(folder);
                var d = Snapshot(Write(folder)); Directory.Delete(folder, true);
                DocumentSafety.DetachToRecoveredBuffer(d);
                Require(!Directory.Exists(folder));
            });
            Test("recovery:unicode-and-newline-retained", f =>
            {
                var d = Snapshot(Write(f)); d.Buffer = "你好🚀\r\n🌐"; d.Newline = "\r\n";
                File.Delete(d.Path!); DocumentSafety.DetachToRecoveredBuffer(d);
                Require(d.Buffer == "你好🚀\r\n🌐" && d.Newline == "\r\n");
            });
            Test("recovery:encoding-and-BOM-retained", f =>
            {
                var d = Snapshot(Write(f)); d.EncodingName = "utf-16be"; d.HasBom = true;
                DocumentSafety.DetachToRecoveredBuffer(d);
                Require(d.HasBom && d.EncodingName == "utf-16be");
            });
            Test("recovery:bookmarks-and-caret-retained", f =>
            {
                var d = Snapshot(Write(f)); d.Bookmarks.Add(10); d.Caret = 27;
                DocumentSafety.DetachToRecoveredBuffer(d);
                Require(d.Bookmarks.Contains(10) && d.Caret == 27);
            });
            Test("recovery:clean-document-marked-dirty", f =>
            {
                var d = Snapshot(Write(f)); Require(!d.Dirty);
                DocumentSafety.DetachToRecoveredBuffer(d);
                Require(d.Dirty && d.Name.EndsWith(" (Recovered)", StringComparison.Ordinal));
            });
            Test("recovery:double-detach-idempotent", f =>
            {
                var d = Snapshot(Write(f)); DocumentSafety.DetachToRecoveredBuffer(d);
                string title = d.Name; string? origin = d.RecoverySourcePath;
                DocumentSafety.DetachToRecoveredBuffer(d);
                Require(title == d.Name && origin == d.RecoverySourcePath);
            });
            Test("recovery:recreated-original-does-not-reattach", f =>
            {
                var d = Snapshot(Write(f)); string path = d.Path!;
                File.Delete(path); DocumentSafety.DetachToRecoveredBuffer(d);
                Write(f, "example.cs", "unrelated new file");
                Require(d.Path == null && d.RecoverySourcePath == path);
            });
            Test("session:duplicate-file-path-detaches-second-copy", f =>
            {
                string path = Write(f);
                var first = Snapshot(path);
                var second = Snapshot(path); second.Buffer = "different unsaved text";
                bool detached = DocumentSafety.DetachIfDuplicate(second, new[] { first });
                Require(detached && second.Path == null && second.RecoverySourcePath == path);
                Require(second.Buffer == "different unsaved text" && first.Path == path);
            });
            Test("session:distinct-file-path-does-not-detach", f =>
            {
                var first = Snapshot(Write(f, "one.txt"));
                var second = Snapshot(Write(f, "two.txt"));
                Require(!DocumentSafety.DetachIfDuplicate(second, new[] { first }) && second.Path != null);
            });
            Test("recovery:empty-file-cache-can-be-recovered", f =>
            {
                var d = Snapshot(Write(f, "empty.cs", "")); d.Buffer = "";
                File.Delete(d.Path!); DocumentSafety.DetachToRecoveredBuffer(d);
                Require(d.Dirty && d.Path == null);
            });
            Test("save:original-unchanged", f =>
            {
                var d = Snapshot(Write(f));
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.Unmodified);
            });
            Test("save:external-length-change", f =>
            {
                var d = Snapshot(Write(f)); File.AppendAllText(d.Path!, "externally modified");
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.ModifiedExternally);
            });
            Test("save:external-timestamp-change", f =>
            {
                var d = Snapshot(Write(f)); File.SetLastWriteTimeUtc(d.Path!, d.WriteUtc.AddMinutes(-5));
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.ModifiedExternally);
            });
            Test("save:external-rewrite-same-length-same-timestamp", f =>
            {
                var d = Snapshot(Write(f, "same.cs", "AAAAA"));
                File.WriteAllText(d.Path!, "BBBBB");
                File.SetLastWriteTimeUtc(d.Path!, d.WriteUtc);
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.ModifiedExternally,
                    "SHA-256 check failed to detect metadata-preserving rewrite");
            });
            Test("save:previous-conflict-even-when-time-unchanged", f =>
            {
                var d = Snapshot(Write(f)); d.ExternalConflict = true;
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.ModifiedExternally);
            });
            Test("save:deleted-then-reappeared-requires-review", f =>
            {
                var d = Snapshot(Write(f)); File.Delete(d.Path!); Write(f, "example.cs", "brand new");
                d.MissingOnDisk = true;
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.ModifiedExternally);
            });
            Test("save:unavailable-directory-target", f =>
            {
                var d = Snapshot(Write(f)); File.Delete(d.Path!); Directory.CreateDirectory(d.Path!);
                Require(DocumentSafety.AssessSaveTarget(d) == SaveTargetState.Unavailable);
            });
            Test("save:missing-parent-does-not-create-folders", f =>
            {
                string nested = Path.Combine(f, "no-parent");
                ExpectThrows<DirectoryNotFoundException>(() => TextFiles.WriteAtomic(Path.Combine(nested, "file.txt"), new byte[] { 65 }));
                Require(!Directory.Exists(nested));
            });
            Test("save:atomic-overwrite-existing", f =>
            {
                string p = Write(f, "file.txt", "old");
                TextFiles.WriteAtomic(p, Encoding.UTF8.GetBytes("new content"));
                Require(File.ReadAllText(p) == "new content");
            });
            Test("save:atomic-new-file", f =>
            {
                string p = Path.Combine(f, "new.txt");
                TextFiles.WriteAtomic(p, Encoding.UTF8.GetBytes("created"));
                Require(File.ReadAllText(p) == "created");
            });
            Test("save:no-orphan-temp-files", f =>
            {
                string p = Write(f);
                TextFiles.WriteAtomic(p, Encoding.UTF8.GetBytes("after"));
                Require(!Directory.EnumerateFiles(f, ".sourcecodeeditor_*.tmp").Any());
            });
            Test("save:original-preserved-if-encoding-fails", f =>
            {
                string p = Write(f, "ascii.txt", "BEFORE");
                ExpectThrows<EncoderFallbackException>(() => TextFiles.Encode("漢字", "iso-8859-1", false, "\n"));
                Require(File.ReadAllText(p) == "BEFORE");
            });
            Test("save-as:preferred-dir-still-exists", f =>
            {
                Require(DocumentSafety.SafeSaveDirectory(Path.Combine(f, "a.txt"), "fallback") == f);
            });
            Test("save-as:missing-original-dir-falls-back", f =>
            {
                Require(DocumentSafety.SafeSaveDirectory(Path.Combine(f, "gone", "a.txt"), f) == f);
            });
            Test("save-as:invalid-original-path-falls-back", f =>
            {
                Require(DocumentSafety.SafeSaveDirectory("a\0b", f) == f);
            });
            Test("read:exclusive-lock-does-not-return-corrupt-data", f =>
            {
                if (!OperatingSystem.IsWindows()) return;
                string p = Write(f, "locked.txt", "sensitive original");
                using var hold = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                bool blocked = false;
                try { _ = TextFiles.Read(p); }
                catch (IOException) { blocked = true; }
                catch (UnauthorizedAccessException) { blocked = true; }
                Require(blocked, "read bypassed exclusive Windows lock");
            });
            Test("save:exclusive-lock-does-not-corrupt-original", f =>
            {
                if (!OperatingSystem.IsWindows()) return;
                string p = Write(f, "locked.txt", "ORIGINAL");
                using (var hold = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    bool blocked = false;
                    try { TextFiles.WriteAtomic(p, Encoding.UTF8.GetBytes("NEW DATA")); }
                    catch (IOException) { blocked = true; }
                    catch (UnauthorizedAccessException) { blocked = true; }
                    Require(blocked, "exclusive target was overwritten");
                }
                Require(File.ReadAllText(p) == "ORIGINAL");
            });
            Test("save:readonly-original-preserved", f =>
            {
                if (!OperatingSystem.IsWindows()) return;
                string p = Write(f, "readonly.cs", "ORIGINAL");
                File.SetAttributes(p, File.GetAttributes(p) | FileAttributes.ReadOnly);
                try
                {
                    bool blocked = false;
                    try { TextFiles.WriteAtomic(p, Encoding.UTF8.GetBytes("NEW DATA")); }
                    catch (IOException) { blocked = true; }
                    catch (UnauthorizedAccessException) { blocked = true; }
                    Require(blocked, "read-only original was overwritten");
                }
                finally { File.SetAttributes(p, FileAttributes.Normal); }
                Require(File.ReadAllText(p) == "ORIGINAL");
            });
            Test("save:target-has-become-directory", f =>
            {
                string p = Path.Combine(f, "turned-to-directory.txt");
                Directory.CreateDirectory(p);
                bool blocked = false;
                try { TextFiles.WriteAtomic(p, Encoding.UTF8.GetBytes("some data")); }
                catch (IOException) { blocked = true; }
                catch (UnauthorizedAccessException) { blocked = true; }
                Require(blocked && Directory.Exists(p));
                Require(!Directory.EnumerateFiles(f, ".sourcecodeeditor_*.tmp").Any());
            });
            Test("session:exclusive-lock-does-not-overwrite-session", f =>
            {
                if (!OperatingSystem.IsWindows()) return;
                string p = Write(f, "session.json", "{\"Documents\":[]}");
                using (var hold = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    bool blocked = false;
                    try { SessionFileStore.Save(p, new EditorSession()); }
                    catch (IOException) { blocked = true; }
                    catch (UnauthorizedAccessException) { blocked = true; }
                    Require(blocked);
                }
                Require(File.ReadAllText(p) == "{\"Documents\":[]}");
            });
            Test("read:empty-text-file", f => Require(TextFiles.Read(Write(f, "empty.txt", "")).text.Length == 0));
            Test("read:UTF8-without-BOM", f =>
            {
                var data = TextFiles.Read(Write(f, "utf.txt", "你好"));
                Require(data.encodingName == "utf-8" && !data.bom && data.text == "你好");
            });
            Test("read:UTF8-with-BOM", f =>
            {
                var p = Path.Combine(f, "bom.txt"); File.WriteAllBytes(p, TextFiles.Encode("hello", "utf-8", true, "\n"));
                var data = TextFiles.Read(p); Require(data.text == "hello" && data.bom);
            });
            foreach (string encoding in new[] { "utf-16", "utf-16be", "utf-32", "utf-32be" })
            {
                string which = encoding;
                Test("read:roundtrip-" + which, f =>
                {
                    var p = Path.Combine(f, "unicode.txt");
                    File.WriteAllBytes(p, TextFiles.Encode("文字😊", which, true, "\n"));
                    var data = TextFiles.Read(p);
                    Require(data.text == "文字😊" && data.encodingName == which && data.bom);
                });
            }
            Test("read:Latin1-byte-fallback", f =>
            {
                string p = Path.Combine(f, "latin.txt"); File.WriteAllBytes(p, new byte[] { 0xE9 });
                var data = TextFiles.Read(p);
                Require(data.encodingName == "iso-8859-1" && data.text == "é");
            });
            Test("read:reject-NUL-binary", f =>
            {
                string p = Path.Combine(f, "binary.txt"); File.WriteAllBytes(p, new byte[] { 1, 2, 0, 3 });
                ExpectThrows<InvalidDataException>(() => TextFiles.Read(p));
            });
            Test("read:reject-control-heavy-binary", f =>
            {
                string p = Path.Combine(f, "bin.cs"); File.WriteAllBytes(p, Enumerable.Repeat((byte)0x01, 150).ToArray());
                ExpectThrows<InvalidDataException>(() => TextFiles.Read(p));
            });
            Test("read:missing-file-does-not-return-empty-string", f =>
                ExpectThrows<FileNotFoundException>(() => TextFiles.Read(Path.Combine(f, "not-there.cs"))));
            foreach (string eol in new[] { "\r\n", "\n", "\r" })
            {
                string actual = eol;
                Test("encoding:EOL-roundtrip-" + (actual == "\r\n" ? "CRLF" : actual == "\n" ? "LF" : "CR"), f =>
                {
                    string p = Path.Combine(f, "eol.txt");
                    File.WriteAllBytes(p, TextFiles.Encode("one\r\ntwo\nthree\rfour", "utf-8", false, actual));
                    var data = TextFiles.Read(p);
                    Require(data.newline == actual);
                    Require(data.text == "one" + actual + "two" + actual + "three" + actual + "four");
                });
            }
            Test("encoding:invalid-Latin1-codepoint-rejected", f =>
                ExpectThrows<EncoderFallbackException>(() => TextFiles.Encode("€", "iso-8859-1", false, "\n")));
            Test("encoding:UTF8-no-BOM-actually-no-BOM", f =>
            {
                var b = TextFiles.Encode("hello", "utf-8", false, "\n");
                Require(b[0] == (byte)'h');
            });
            Test("encoding:UTF8-with-BOM-exactly-one", f =>
            {
                var b = TextFiles.Encode("hello", "utf-8", true, "\n");
                Require(b.Length == 8 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF);
            });
            Test("encoding:UTF16-byte-order", f =>
            {
                var le = TextFiles.Encode("A", "utf-16", true, "\n");
                var be = TextFiles.Encode("A", "utf-16be", true, "\n");
                Require(le[0] == 0xFF && le[1] == 0xFE && be[0] == 0xFE && be[1] == 0xFF);
            });
            Test("encoding:CRLF-not-doubled", f =>
            {
                byte[] b = TextFiles.Encode("a\r\nb", "utf-8", false, "\r\n");
                Require(Encoding.UTF8.GetString(b) == "a\r\nb");
            });
            Test("encoding:source-unicode-survives", f =>
            {
                const string text = "\u03bb \u4f60\u597d 🚀";
                Require(Encoding.UTF8.GetString(TextFiles.Encode(text, "utf-8", false, "\n")) == text);
            });

            // Session storage, legacy formats and hostile/corrupt inputs.
            Test("session:JSON-roundtrip-active-and-closed", f =>
            {
                string p = Path.Combine(f, "session.json");
                var closed = new EditorDocument { Closed = true, Name = "closed.cs", Buffer = "cached", Path = "Z:\\removed\\closed.cs" };
                var active = new EditorDocument { Dirty = true, Name = "Untitled 1", Buffer = "unsaved text" };
                SessionFileStore.Save(p, new EditorSession { Documents = new() { closed, active }, ActiveDocument = 1 });
                var read = SessionFileStore.Load(p);
                Require(read.Documents.Count == 2 && read.Documents[0].Closed && read.Documents[1].Dirty);
                Require(read.Documents[1].Buffer == "unsaved text" && read.ActiveDocument == 1);
            });
            Test("session:recovery-original-path-serialized", f =>
            {
                string p = Path.Combine(f, "s.json");
                var doc = new EditorDocument { RecoverySourcePath = "Z:\\lost\\old.cs", Dirty = true, Buffer = "recover" };
                SessionFileStore.Save(p, new EditorSession { Documents = new() { doc } });
                Require(SessionFileStore.Load(p).Documents[0].RecoverySourcePath == doc.RecoverySourcePath);
            });
            Test("session:font-sizes-persist", f =>
            {
                string p = Path.Combine(f, "s.json");
                SessionFileStore.Save(p, new EditorSession { ApplicationFontPoints = 22, EditorFontPoints = 14 });
                var data = SessionFileStore.Load(p);
                Require(data.ApplicationFontPoints == 22 && data.EditorFontPoints == 14);
            });
            Test("session:bookmarks-persist", f =>
            {
                string p = Path.Combine(f, "s.json");
                var doc = new EditorDocument { Name = "one.cs" }; doc.Bookmarks.Add(7); doc.Bookmarks.Add(99);
                SessionFileStore.Save(p, new EditorSession { Documents = new() { doc } });
                Require(SessionFileStore.Load(p).Documents[0].Bookmarks.SetEquals(new[] { 7, 99 }));
            });
            Test("session:legacy-array-supported", f =>
            {
                string p = Write(f, "legacy.json", "[{\"Name\":\"legacy.cs\",\"Buffer\":\"old\"}]");
                var data = SessionFileStore.Load(p);
                Require(data.Documents.Count == 1 && data.Documents[0].Buffer == "old");
            });
            Test("session:invalid-JSON-rejected", f =>
            {
                string p = Write(f, "bad.json", "{ invalid JSON");
                ExpectThrows<JsonException>(() => SessionFileStore.Load(p));
            });
            Test("session:null-document-array-rejected", f =>
            {
                string p = Write(f, "bad.json", "{\"Documents\":null}");
                ExpectThrows<InvalidDataException>(() => SessionFileStore.Load(p));
            });
            Test("session:null-document-item-rejected", f =>
            {
                string p = Write(f, "bad.json", "{\"Documents\":[null]}");
                ExpectThrows<InvalidDataException>(() => SessionFileStore.Load(p));
            });
            Test("session:missing-session-is-error-not-data-loss", f =>
                ExpectThrows<FileNotFoundException>(() => SessionFileStore.Load(Path.Combine(f, "missing.json"))));
            Test("session:corrupt-raw-bytes-preserved", f =>
            {
                string p = Write(f, "bad.json", "{BAD DATA\0\u00a9}");
                string original = File.ReadAllText(p);
                string? rescued = SessionFileStore.RescueCorruptSession(p);
                Require(rescued != null && File.ReadAllText(rescued) == original && File.ReadAllText(p) == original);
            });
            Test("session:no-overwrite-corrupt-backup", f =>
            {
                string p = Write(f, "bad.json", "broken-json");
                string copy = SessionFileStore.RescueCorruptSession(p)!;
                SessionFileStore.Save(p, new EditorSession());
                Require(File.ReadAllText(copy) == "broken-json");
            });
            Test("session:missing-backup-source-returns-null", f =>
                Require(SessionFileStore.RescueCorruptSession(Path.Combine(f, "missing.json")) == null));
            Test("session:save-creates-only-configured-parent", f =>
            {
                string target = Path.Combine(f, "application", "session.json");
                SessionFileStore.Save(target, new EditorSession());
                Require(File.Exists(target));
            });
            Test("session:save-does-not-leave-temporary-files", f =>
            {
                string target = Path.Combine(f, "session.json");
                SessionFileStore.Save(target, new EditorSession());
                SessionFileStore.Save(target, new EditorSession());
                Require(Directory.GetFiles(f, "*.tmp").Length == 0);
            });
            Test("session:missing-closed-path-remains-in-history", f =>
            {
                string p = Path.Combine(f, "session.json");
                var doc = new EditorDocument { Closed = true, Name = "gone.cs", Buffer = "cached", Path = Path.Combine(f, "removed", "gone.cs") };
                SessionFileStore.Save(p, new EditorSession { Documents = new() { doc } });
                var read = SessionFileStore.Load(p).Documents[0];
                Require(read.Closed && read.Buffer == "cached" && DocumentSafety.Inspect(read.Path) == FileAvailability.Missing);
            });
            Test("session:recovery-marker-stays-unsaved", f =>
            {
                string p = Path.Combine(f, "session.json");
                var doc = new EditorDocument { Dirty = true, Name = "lost.cs (Recovered)", Path = null, Buffer = "saved in recovery" };
                SessionFileStore.Save(p, new EditorSession { Documents = new() { doc } });
                var d = SessionFileStore.Load(p).Documents[0];
                Require(d.Dirty && d.Path == null && d.Buffer == "saved in recovery");
            });
            Test("session:closed-and-open-mixed-order", f =>
            {
                string p = Path.Combine(f, "session.json");
                var docs = Enumerable.Range(0, 20).Select(i => new EditorDocument { Name = i.ToString(), Closed = (i % 2 == 0) }).ToList();
                SessionFileStore.Save(p, new EditorSession { Documents = docs });
                var again = SessionFileStore.Load(p).Documents;
                Require(again.Count == 20 && again[0].Closed && !again[1].Closed && again[19].Name == "19");
            });

            // Startup and closed-file recovery invariants (new in v0.5.3).
            Test("startup:zero-documents-requires-untitled", f =>
                Require(SessionRestorePolicy.NeedsUntitledDocument(Array.Empty<EditorDocument>())));
            Test("startup:all-closed-requires-untitled", f =>
                Require(SessionRestorePolicy.NeedsUntitledDocument(new[]
                { new EditorDocument { Closed = true, Path = "closed.cs" } })));
            Test("startup:open-blank-does-not-duplicate", f =>
                Require(!SessionRestorePolicy.NeedsUntitledDocument(new[]
                { new EditorDocument { Name = "Untitled 1", Closed = false, Buffer = "" } })));
            Test("startup:open-saved-does-not-duplicate", f =>
                Require(!SessionRestorePolicy.NeedsUntitledDocument(new[]
                { new EditorDocument { Closed = false, Path = "open.cs" } })));
            Test("startup:dirty-open-buffer-does-not-duplicate", f =>
                Require(!SessionRestorePolicy.NeedsUntitledDocument(new[]
                { new EditorDocument { Closed = false, Dirty = true, Buffer = "not saved" } })));
            Test("startup:open-plus-closed-no-extra-blank", f =>
                Require(!SessionRestorePolicy.NeedsUntitledDocument(new[]
                { new EditorDocument { Closed = true }, new EditorDocument { Closed = false } })));
            Test("startup:reopen-closed-enabled", f =>
                Require(SessionRestorePolicy.ShouldReopen(new EditorDocument { Closed = true }, true)));
            Test("startup:reopen-closed-disabled", f =>
                Require(!SessionRestorePolicy.ShouldReopen(new EditorDocument { Closed = true }, false)));
            Test("startup:open-always-reopened", f =>
                Require(SessionRestorePolicy.ShouldReopen(new EditorDocument { Closed = false }, false)));
            Test("session:closed-reopen-preference-persists", f =>
            {
                string path = Path.Combine(f, "session.json");
                SessionFileStore.Save(path, new EditorSession { ReopenClosedOnStartup = false });
                Require(!SessionFileStore.Load(path).ReopenClosedOnStartup);
            });
            Test("session:closed-reopen-enabled-by-default", f =>
            {
                string path = Path.Combine(f, "session.json");
                SessionFileStore.Save(path, new EditorSession());
                Require(SessionFileStore.Load(path).ReopenClosedOnStartup);
            });
            Test("session:old-format-defaults-to-reopen", f =>
            {
                string path = Write(f, "old-session.json", "{\"Documents\":[]}");
                Require(SessionFileStore.Load(path).ReopenClosedOnStartup);
            });
            Test("session:dirty-open-buffer-restored-exactly", f =>
            {
                var d = new EditorDocument { Name = "Unsaved C#", Dirty = true,
                    Buffer = "class A { }\n你好 🐱", Closed = false };
                string path = Path.Combine(f, "session.json");
                SessionFileStore.Save(path, new EditorSession { Documents = new() { d } });
                var restored = SessionFileStore.Load(path).Documents.Single();
                Require(restored.Dirty && !restored.Closed && restored.Path == null &&
                    restored.Buffer == d.Buffer);
            });
            Test("session:dirty-closed-buffer-restored-exactly", f =>
            {
                var d = new EditorDocument { Name = "Closed unsaved", Dirty = true,
                    Buffer = "not saved but retained", Closed = true };
                string path = Path.Combine(f, "session.json");
                SessionFileStore.Save(path, new EditorSession { Documents = new() { d } });
                var restored = SessionFileStore.Load(path).Documents.Single();
                Require(restored.Dirty && restored.Closed && restored.Buffer == d.Buffer);
            });
            Test("session:saved-closed-reopen-from-disk", f =>
            {
                string source = Write(f, "closed.cs", "saved contents");
                var d = Snapshot(source); // Closed is true, as in the Closed Files tree.
                var state = new EditorSession { Documents = new() { d } };
                string path = Path.Combine(f, "session.json");
                SessionFileStore.Save(path, state);
                var restored = SessionFileStore.Load(path).Documents.Single();
                Require(restored.Closed && SessionRestorePolicy.ShouldReopen(restored, true));
                Require(DocumentSafety.Inspect(restored.Path) == FileAvailability.Present);
            });
            Test("session:saved-closed-missing-source-recovered", f =>
            {
                string source = Write(f, "closed.cs", "saved contents");
                var d = Snapshot(source); d.Buffer = "precious cached text";
                string path = Path.Combine(f, "session.json");
                SessionFileStore.Save(path, new EditorSession { Documents = new() { d } });
                File.Delete(source);
                var restored = SessionFileStore.Load(path).Documents.Single();
                if (SessionRestorePolicy.ShouldReopen(restored, true) &&
                    DocumentSafety.Inspect(restored.Path) == FileAvailability.Missing)
                    DocumentSafety.DetachToRecoveredBuffer(restored);
                Require(restored.Path == null && restored.Dirty &&
                    restored.Buffer == "precious cached text" && !File.Exists(source));
            });
            Test("session:empty-untitled-persists-as-untitled", f =>
            {
                string path = Path.Combine(f, "session.json");
                SessionFileStore.Save(path, new EditorSession { Documents = new()
                { new EditorDocument { Name = "Untitled 1", Buffer = "", Closed = false } } });
                var docs = SessionFileStore.Load(path).Documents;
                Require(!SessionRestorePolicy.NeedsUntitledDocument(docs) &&
                    docs[0].Name == "Untitled 1" && docs[0].Buffer == "");
            });
            Test("recovery:invalid-source-path-does-not-crash", f =>
            {
                var d = new EditorDocument { Path = "invalid\0path", Buffer = "keep this", Dirty = true };
                DocumentSafety.DetachToRecoveredBuffer(d);
                Require(d.Path == null && d.Dirty && d.Buffer == "keep this" &&
                    d.RecoverySourcePath == "invalid\0path");
            });
            Test("session:dirty-and-clean-mixed-persist", f =>
            {
                string path = Path.Combine(f, "session.json");
                SessionFileStore.Save(path, new EditorSession { Documents = new()
                {
                    new EditorDocument { Closed = false, Dirty = true, Buffer = "unsaved" },
                    new EditorDocument { Closed = true, Dirty = false, Buffer = "saved" },
                    new EditorDocument { Closed = true, Dirty = true, Buffer = "closed dirty" }
                } });
                var docs = SessionFileStore.Load(path).Documents;
                Require(docs.Count == 3 && docs[0].Dirty && !docs[1].Dirty && docs[2].Dirty &&
                    docs[2].Closed && docs[2].Buffer == "closed dirty");
            });

            // Explorer filtering: files must be readable text, even with source extension.
            Test("explorer:source-code-visible", f => Require(TextSourceFileFilter.IsTextOrSource(Write(f, "code.cs", "class X {}"))));
            Test("explorer:plain-text-visible", f => Require(TextSourceFileFilter.IsTextOrSource(Write(f, "notes.txt", "hello"))));
            Test("explorer:extensionless-text-visible", f => Require(TextSourceFileFilter.IsTextOrSource(Write(f, "Makefile", "all:\n\techo ok"))));
            Test("explorer:unknown-extension-text-visible", f => Require(TextSourceFileFilter.IsTextOrSource(Write(f, "data.xyz", "readable text"))));
            Test("explorer:dotfile-visible", f => Require(TextSourceFileFilter.IsTextOrSource(Write(f, ".editorconfig", "root=true"))));
            Test("explorer:CMotive-source-visible", f => Require(TextSourceFileFilter.IsTextOrSource(Write(f, "class.CMOT", "Class Example {}"))));
            Test("explorer:unknown-empty-file-visible", f => Require(TextSourceFileFilter.IsTextOrSource(Write(f, "empty.xyz", ""))));
            Test("explorer:unknown-NUL-binary-hidden", f =>
            {
                string p = Path.Combine(f, "image.dat"); File.WriteAllBytes(p, new byte[] { 0, 1, 2 });
                Require(!TextSourceFileFilter.IsTextOrSource(p));
            });
            Test("explorer:binary-disguised-as-source-hidden", f =>
            {
                string p = Path.Combine(f, "fake.cs"); File.WriteAllBytes(p, new byte[] { 0, 1, 2, 0 });
                Require(!TextSourceFileFilter.IsTextOrSource(p));
            });
            Test("explorer:ZIP-file-hidden", f =>
            {
                string p = Path.Combine(f, "archive.dat"); File.WriteAllBytes(p, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0 });
                Require(!TextSourceFileFilter.IsTextOrSource(p));
            });
            Test("explorer:PNG-file-hidden", f =>
            {
                string p = Path.Combine(f, "png.cs"); File.WriteAllBytes(p, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
                Require(!TextSourceFileFilter.IsTextOrSource(p));
            });
            Test("explorer:MZ-executable-hidden", f =>
            {
                string p = Path.Combine(f, "exe.txt"); File.WriteAllBytes(p, new byte[] { (byte)'M', (byte)'Z', 0x20, 0x20 });
                Require(!TextSourceFileFilter.IsTextOrSource(p));
            });
            Test("explorer:ELF-executable-hidden", f =>
            {
                string p = Path.Combine(f, "elf.txt"); File.WriteAllBytes(p, new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' });
                Require(!TextSourceFileFilter.IsTextOrSource(p));
            });
            Test("explorer:missing-file-hidden-without-throw", f =>
                Require(!TextSourceFileFilter.IsTextOrSource(Path.Combine(f, "missing.cs"))));
            Test("explorer:directory-is-not-a-file", f =>
            {
                string p = Path.Combine(f, "folder.txt"); Directory.CreateDirectory(p);
                Require(!TextSourceFileFilter.IsTextOrSource(p));
            });
            Test("explorer:UTF16-source-visible", f =>
            {
                string p = Path.Combine(f, "encoded.xml");
                File.WriteAllBytes(p, TextFiles.Encode("<x>文</x>", "utf-16", true, "\n"));
                Require(TextSourceFileFilter.IsTextOrSource(p));
            });
            Test("explorer:hidden-attribute-is-not-filtered", f =>
            {
                string p = Write(f, ".hidden.txt", "visible despite hidden attribute");
                if (OperatingSystem.IsWindows()) File.SetAttributes(p, File.GetAttributes(p) | FileAttributes.Hidden);
                Require(TextSourceFileFilter.IsTextOrSource(p));
            });
            Test("explorer:all-control-byte-file-hidden", f =>
            {
                string p = Path.Combine(f, "fake.py"); File.WriteAllBytes(p, Enumerable.Repeat((byte)0x01, 50).ToArray());
                Require(!TextSourceFileFilter.IsTextOrSource(p));
            });

            // v0.5.4: Find the current file in the custom explorer, not explorer.exe.
            Test("locate:existing-saved-file", f =>
            {
                string path = Write(f, "main.cs", "class Main { }");
                var route = ExplorerNavigationPlan.ForFile(path, f);
                Require(route.FileExists && route.OriginalDirectoryExists);
                Require(string.Equals(route.TargetFile, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
                Require(string.Equals(route.DirectoryToShow, f, StringComparison.OrdinalIgnoreCase));
            });
            Test("locate:missing-file-in-existing-folder", f =>
            {
                var route = ExplorerNavigationPlan.ForFile(Path.Combine(f, "deleted.py"), f);
                Require(!route.FileExists && route.OriginalDirectoryExists);
                Require(route.DirectoryToShow == f);
            });
            Test("locate:deleted-parent-folder", f =>
            {
                string child = Path.Combine(f, "deleted"); Directory.CreateDirectory(child);
                var target = Path.Combine(child, "a.cs"); File.WriteAllText(target, "text");
                Directory.Delete(child, true);
                var route = ExplorerNavigationPlan.ForFile(target, f);
                Require(!route.FileExists && !route.OriginalDirectoryExists);
                Require(route.DirectoryToShow == f);
            });
            Test("locate:renamed-parent-folder", f =>
            {
                string child = Path.Combine(f, "before"); Directory.CreateDirectory(child);
                string file = Write(child, "unit.cs");
                Directory.Move(child, Path.Combine(f, "after"));
                var route = ExplorerNavigationPlan.ForFile(file, f);
                Require(!route.FileExists && !route.OriginalDirectoryExists && route.DirectoryToShow == f);
            });
            Test("locate:deepest-existing-parent", f =>
            {
                string a = Path.Combine(f, "a"); Directory.CreateDirectory(a);
                var route = ExplorerNavigationPlan.ForFile(Path.Combine(a, "missing", "deeper", "x.cs"), f);
                Require(!route.FileExists && route.DirectoryToShow == a);
            });
            Test("locate:unicode-file-name", f =>
            {
                string file = Write(f, "こんにちは_源コード.cs", "text");
                var route = ExplorerNavigationPlan.ForFile(file, f);
                Require(route.FileExists && route.TargetFile == Path.GetFullPath(file));
            });
            Test("locate:blank-document-path-rejected", f =>
                ExpectThrows<ArgumentException>(() => ExplorerNavigationPlan.ForFile("", f)));
            Test("locate:invalid-NUL-filename", f =>
                ExpectThrows<ArgumentException>(() => ExplorerNavigationPlan.ForFile("bad\0file", f)));
            Test("fonts:chosen-families-survive-session-restart", f =>
            {
                string path = Path.Combine(f, "session.json");
                var state = new EditorSession
                {
                    ApplicationFontFamily = "Courier New", EditorFontFamily = "Consolas",
                    ApplicationFontPoints = 13f, EditorFontPoints = 15f
                };
                SessionFileStore.Save(path, state);
                var restored = SessionFileStore.Load(path);
                Require(restored.ApplicationFontFamily == "Courier New" && restored.EditorFontFamily == "Consolas");
                Require(restored.ApplicationFontPoints == 13f && restored.EditorFontPoints == 15f);
            });
            Test("fonts:old-session-default-families", f =>
            {
                string path = Path.Combine(f, "session.json");
                File.WriteAllText(path, "{\"Documents\":[],\"ApplicationFontPoints\":12,\"EditorFontPoints\":12}");
                var restored = SessionFileStore.Load(path);
                Require(restored.ApplicationFontFamily == "Segoe UI" && restored.EditorFontFamily == "Consolas");
            });
            if (OperatingSystem.IsWindows())
            {
                Test("fonts:installed-fixed-pitch-list", f =>
                {
                    var available = MonospacedFontCatalog.ListInstalled();
                    Require(available.Count > 0, "No installed monospaced fonts detected");
                    Require(available.Distinct(StringComparer.OrdinalIgnoreCase).Count() == available.Count,
                        "Duplicate font name");
                    foreach (var name in available)
                    {
                        using var font = new System.Drawing.Font(name, 12f, System.Drawing.FontStyle.Regular);
                        Require(MonospacedFontCatalog.IsMonospaced(font), "Non-monospaced font: " + name);
                    }
                });
                Test("fonts:unknown-font-restores-default", f =>
                    Require(MonospacedFontCatalog.InstalledOrDefault("MissingFont_" + Guid.NewGuid(), "Consolas") == "Consolas"));
            }
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch (IOException) { /* test fixture left in OS temp, not Documents */ }
            catch (UnauthorizedAccessException) { }
        }

        int failed = results.Count(r => !r.Passed);
        var report = new
        {
            Suite = "SourceCodeEditor file/encoding/recovery negative tests",
            TimestampUtc = DateTime.UtcNow,
            System = Environment.OSVersion.ToString(),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Total = results.Count,
            Passed = results.Count - failed,
            Failed = failed,
            Tests = results
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "SourceCodeEditor-negative-test-report-error.txt"), ex.ToString()); }
            catch { }
            return 2;
        }
        return failed == 0 ? 0 : 1;
    }
}
