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
    // Never overwrite an unreadable session unless its original was rescued.
    private bool disableSessionWrites;
    private void PersistSession()
    {
        TryPersistSession(out _);
    }

    private bool TryPersistSession(out string? failure)
    {
        failure = null;
        if (sessionLoading || isClosing || IsDisposed || disableSessionWrites)
        {
            failure = "Session writes are disabled or the editor is closing.";
            return false;
        }
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
                LeftWidth = split.Panel1Collapsed ? 252 : split.SplitterDistance,
                ApplicationFontPoints = applicationFontPoints,
                EditorFontPoints = editorFontPoints,
                RecentFiles = recentFiles.ToList(),
                ReopenClosedOnStartup = reopenClosedOnStartup
            };
            SessionFileStore.Save(sessionPath, state);
            return true;
        }
        catch (Exception ex)
        {
            // Automatic backups never interrupt editing. App exit uses the
            // returned failure to protect unsaved documents from silent loss.
            failure = ex.Message;
            return false;
        }
    }
    private void RestoreSession()
    {
        if (!File.Exists(sessionPath)) return;
        sessionLoading = true;
        try
        {
            EditorSession state = SessionFileStore.Load(sessionPath);
            reopenClosedOnStartup = state.ReopenClosedOnStartup;
            if (reopenClosedOnStartupMenu != null)
                reopenClosedOnStartupMenu.Checked = reopenClosedOnStartup;
            // Do not restore the old explorer path: every launch starts in Documents.
            preferredLeftWidth = Math.Clamp(state.LeftWidth, 242, 320);
            SetApplicationFontSize(state.ApplicationFontPoints, false);
            SetEditorFontSize(state.EditorFontPoints, false);
            foreach (var recent in state.RecentFiles ?? new List<string>())
            {
                if (recentFiles.Count >= 20) break;
                if (!string.IsNullOrWhiteSpace(recent) && File.Exists(recent) &&
                    !recentFiles.Contains(recent, StringComparer.OrdinalIgnoreCase))
                    recentFiles.Add(recent);
            }
            RefreshRecentMenu();
            foreach (var doc in state.Documents)
            {
                doc.View = null; doc.Tab = null;
                doc.Bookmarks ??= new HashSet<int>();
                doc.Buffer ??= string.Empty;
                doc.Name ??= "Untitled";
                doc.EncodingName ??= "utf-8";
                if (doc.Newline != "\r\n" && doc.Newline != "\n" && doc.Newline != "\r") doc.Newline = "\r\n";
                if (!float.IsFinite(doc.Zoom)) doc.Zoom = 1f;
                doc.Zoom = Math.Clamp(doc.Zoom, 0.5f, 4f);
                bool reopen = SessionRestorePolicy.ShouldReopen(doc, reopenClosedOnStartup);
                if (doc.Path != null)
                {
                    FileAvailability availability = DocumentSafety.Inspect(doc.Path);
                    if (availability == FileAvailability.Present && !doc.Dirty && reopen)
                    {
                        try { LoadFromDisk(doc); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                        {
                            // No on-disk read should ever destroy the session snapshot.
                            DocumentSafety.DetachToRecoveredBuffer(doc);
                        }
                    }
                    else if (availability == FileAvailability.Missing)
                    {
                        if (!reopen) doc.MissingOnDisk = true;
                        else DocumentSafety.DetachToRecoveredBuffer(doc);
                    }
                    else if (availability != FileAvailability.Present)
                    {
                        if (!reopen) doc.UnavailableOnDisk = true;
                        else DocumentSafety.DetachToRecoveredBuffer(doc);
                    }
                    else if (reopen && doc.Dirty &&
                             DocumentSafety.AssessSaveTarget(doc) == SaveTargetState.ModifiedExternally)
                    {
                        doc.ExternalConflict = true;
                    }
                }
                if (doc.Path == null && doc.Buffer.Length > 0) doc.Dirty = true;
                // A damaged session may refer to the same physical file in
                // multiple document entries. Preserve every buffer, but never
                // allow two tabs to silently save over the same path.
                DocumentSafety.DetachIfDuplicate(doc, documents);
                documents.Add(doc);
                if (reopen)
                {
                    // Restore performed the disk checks, so avoid a second
                    // prompt/loading pass through PrepareClosedDocument.
                    doc.Closed = false;
                    Activate(doc);
                }
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
            string? backup = SessionFileStore.RescueCorruptSession(sessionPath);
            if (backup == null) disableSessionWrites = true;
            string note = backup == null
                ? "A backup could not be made. Automatic session writes are disabled to protect the original file."
                : "The original session was copied to:\n" + backup;
            MessageBox.Show(this, "The saved session could not be restored: " + ex.Message +
                "\n\n" + note, "Session recovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { sessionLoading = false; }
    }

    private void CheckExternalChanges()
    {
        if (isClosing || !Visible || !Enabled) return;
        bool changes = false;
        foreach (var doc in documents.Where(x => x.Path != null).ToArray())
        {
            var availability = DocumentSafety.Inspect(doc.Path);
            if (doc.Closed)
            {
                bool missing = availability == FileAvailability.Missing;
                bool inaccessible = availability is FileAvailability.Directory or FileAvailability.Inaccessible;
                if (doc.MissingOnDisk != missing || doc.UnavailableOnDisk != inaccessible)
                {
                    doc.MissingOnDisk = missing;
                    doc.UnavailableOnDisk = inaccessible;
                    changes = true;
                }
                continue;
            }
            if (availability == FileAvailability.Missing)
            {
                if (!doc.MissingOnDisk)
                {
                    doc.MissingOnDisk = true;
                    doc.UnavailableOnDisk = false;
                    doc.Dirty = true;  // Protect cached text from being silently discarded.
                    changes = true;
                }
                continue;
            }
            if (availability != FileAvailability.Present)
            {
                if (!doc.UnavailableOnDisk)
                {
                    doc.UnavailableOnDisk = true;
                    changes = true;
                }
                continue;
            }
            bool reappeared = doc.MissingOnDisk;
            if (doc.UnavailableOnDisk || reappeared)
            {
                doc.UnavailableOnDisk = false;
                doc.MissingOnDisk = false;
                doc.ExternalConflict = true;
                doc.Dirty = true;
                changes = true;
            }
            try
            {
                var (stamp, length) = DocumentSafety.Stamp(doc.Path!);
                if (!reappeared && stamp == doc.WriteUtc && length == doc.LastLength)
                {
                    // Detect replacements that deliberately keep their length
                    // and timestamp, without repeatedly hashing huge files.
                    if (length > 8 * 1024 * 1024 ||
                        DocumentSafety.AssessSaveTarget(doc) == SaveTargetState.Unmodified)
                        continue;
                }
                // Do not show the same conflict message every four seconds.
                if (!reappeared && stamp == doc.LastNotifiedWriteUtc &&
                    length == doc.LastNotifiedLength) continue;
                doc.LastNotifiedWriteUtc = stamp;
                doc.LastNotifiedLength = length;
                bool reload = MessageBox.Show(this,
                    $"'{doc.Name}' {(reappeared ? "reappeared after being removed" : "changed outside SourceCodeEditor")}.\n\n" +
                    "Reloading will replace the cached editor text.\n\nReload from disk now?",
                    "External file change", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                if (!reload)
                {
                    doc.ExternalConflict = true;
                    doc.Dirty = true;
                    changes = true;
                    continue;
                }
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
                changes = true;
            }
            catch (IOException) { doc.UnavailableOnDisk = true; changes = true; }
            catch (UnauthorizedAccessException) { doc.UnavailableOnDisk = true; changes = true; }
        }
        if (changes)
        {
            foreach (var doc in documents.Where(x => x.Tab != null))
                doc.Tab!.Text = doc.ToString();
            RefreshDocumentTrees(); UpdateStatus();
        }
    }
    private void OnMainFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (isClosing) return;
        sessionTimer.Stop(); diskTimer.Stop();
        // Exiting is not the same as explicitly closing/discarding a tab.
        // Persist all dirty/untitled/closed buffers *before* any disposal.
        // Session recovery is the default startup behavior.
        if (!TryPersistSession(out string? failure))
        {
            var answer = MessageBox.Show(this,
                "The editor could not save its recovery session.\n\n" + failure +
                "\n\nExiting now can lose unsaved work.\n\n" +
                "Yes = exit without a recovery snapshot.\nNo = cancel exit and continue editing.",
                "Session backup failed", MessageBoxButtons.YesNo, MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                args.Cancel = true;
                sessionTimer.Start(); diskTimer.Start();
                return;
            }
        }
        isClosing = true;
        CloseAllShellTabs();
    }
}
