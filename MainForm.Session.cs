using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace SourceCodeEditor;

public sealed partial class MainForm
{
    private bool sessionLoading;
    private void PersistSession()
    {
        if (sessionLoading || isClosing || IsDisposed) return;
        try
        {
            foreach (var doc in documents.Where(x => x.View != null))
            {
                doc.Buffer = doc.View!.Editor.Text;
                doc.Caret = doc.View.Editor.SelectionStart;
                doc.Zoom = doc.View.Editor.ZoomFactor;
            }
            var state = new EditorSession
            {
                Documents = documents,
                ActiveDocument = Math.Max(0, documents.IndexOf(CurrentDocument()!)),
                ExplorerRoot = explorerRoot,
                NavigationIndex = navigation.SelectedIndex,
                LeftWidth = split.Panel1Collapsed ? 216 : split.SplitterDistance,
                RecentFiles = recentFiles.ToList()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);
            var temporary = sessionPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, sessionPath, true);
        }
        catch (Exception)
        {
            // Session recovery must never block normal editing or saving.
        }
    }
    private void RestoreSession()
    {
        if (!File.Exists(sessionPath)) return;
        sessionLoading = true;
        try
        {
            var json = File.ReadAllText(sessionPath);
            using var parsed = JsonDocument.Parse(json);
            EditorSession? state;
            // Compatibility with the original SourceCodeEditor session file, which was a JSON array.
            if (parsed.RootElement.ValueKind == JsonValueKind.Array)
                state = new EditorSession { Documents = JsonSerializer.Deserialize<List<EditorDocument>>(json) ?? new() };
            else
                state = JsonSerializer.Deserialize<EditorSession>(json);
            if (state == null) return;
            // Do not restore the old explorer path: every launch starts in Documents.
            preferredLeftWidth = Math.Clamp(state.LeftWidth, 160, 320);
            foreach (var recent in state.RecentFiles ?? new List<string>())
                if (File.Exists(recent)) recentFiles.Add(recent);
            RefreshRecentMenu();
            foreach (var doc in state.Documents)
            {
                doc.View = null; doc.Tab = null;
                doc.Bookmarks ??= new HashSet<int>();
                if (doc.Path != null && File.Exists(doc.Path) && !doc.Dirty)
                {
                    try { LoadFromDisk(doc); }
                    catch (Exception) { doc.Dirty = true; }
                }
                else if (doc.Path != null && !File.Exists(doc.Path)) doc.Dirty = true;
                documents.Add(doc);
                if (!doc.Closed) Activate(doc);
                if (doc.Name.StartsWith("Untitled ", StringComparison.Ordinal) &&
                    int.TryParse(doc.Name.Substring(9), out int number))
                    untitledCounter = Math.Max(untitledCounter, number + 1);
            }
            navigation.SelectedIndex = Math.Clamp(state.NavigationIndex, 0, navigation.TabPages.Count - 1);
            if (state.ActiveDocument >= 0 && state.ActiveDocument < documents.Count)
            {
                var active = documents[state.ActiveDocument];
                if (!active.Closed && active.Tab != null) editors.SelectedTab = active.Tab;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The saved session could not be restored: " + ex.Message,
                "Session recovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { sessionLoading = false; }
    }
    private void CheckExternalChanges()
    {
        if (isClosing || !Visible || !Enabled) return;
        foreach (var doc in documents.Where(x => !x.Closed && x.Path != null).ToArray())
        {
            try
            {
                if (doc.Path == null || !File.Exists(doc.Path)) continue;
                var info = new FileInfo(doc.Path);
                if (info.LastWriteTimeUtc <= doc.WriteUtc && info.Length == doc.LastLength) continue;
                // Remember the changed version if the user decides not to reload it.
                doc.WriteUtc = info.LastWriteTimeUtc;
                doc.LastLength = info.Length;
                bool reload = MessageBox.Show(this,
                    $"'{doc.Name}' was modified outside SourceCodeEditor.\n\n" +
                    (doc.Dirty ? "Reloading will discard UNSAVED editor changes.\n\n" : "") +
                    "Reload from disk now?", "External file change",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                if (!reload) continue;
                int at = doc.View?.Editor.SelectionStart ?? doc.Caret;
                LoadFromDisk(doc);
                if (doc.View != null)
                {
                    ignoreTextChanged = true;
                    try
                    {
                        doc.View.LoadText(doc.Buffer, doc.Path);
                        doc.View.Editor.SelectionStart = Math.Clamp(at, 0, doc.View.Editor.TextLength);
                    }
                    finally { ignoreTextChanged = false; }
                }
                if (doc.Tab != null) doc.Tab.Text = doc.ToString();
                RefreshDocumentTrees(); UpdateStatus();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private void OnMainFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (isClosing) return;
        sessionTimer.Stop(); diskTimer.Stop();
        // Closing is cancelled if any Save As operation is cancelled.
        foreach (var doc in documents.Where(x => !x.Closed && x.Dirty).ToArray())
        {
            if (!ConfirmCanClose(doc))
            {
                args.Cancel = true;
                sessionTimer.Start(); diskTimer.Start();
                return;
            }
        }
        PersistSession();
        isClosing = true;
        CloseAllShellTabs();
    }
}
