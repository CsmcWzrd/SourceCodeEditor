using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace SourceCodeEditor;

public sealed partial class MainForm : Form
{
    private readonly List<EditorDocument> documents = new();
    private readonly TabControl editors = new()
    {
        Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed,
        SizeMode = TabSizeMode.Normal, Padding = new Point(20, 7), HotTrack = true
    };
    private readonly TabControl navigation = new()
    {
        Dock = DockStyle.Fill, Multiline = true, SizeMode = TabSizeMode.Fixed,
        ItemSize = new Size(116, 31), Padding = new Point(2, 4),
        Font = new Font("Segoe UI", 12f)
    };
    private readonly TreeView opened = MakeDocumentTree();
    private readonly TreeView closed = MakeDocumentTree();
    private readonly TreeView unsaved = MakeDocumentTree();
    private readonly TreeView explorer = new()
    {
        Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true,
        BorderStyle = BorderStyle.None
    };
    private readonly TextBox explorerAddress = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly ContextMenuStrip explorerMenu = new();
    private string explorerRoot = GetDocumentsDirectory();
    private readonly SplitContainer split = new()
    {
        Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
        FixedPanel = FixedPanel.Panel1, SplitterWidth = 5
    };
    private readonly ToolStripStatusLabel caretStatus = new("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel encodingStatus = new("UTF-8");
    private readonly ToolStripStatusLabel eolStatus = new("CRLF");
    private readonly ToolStripStatusLabel zoomStatus = new("100%");
    private readonly System.Windows.Forms.Timer sessionTimer = new() { Interval = 10000 };
    private readonly System.Windows.Forms.Timer diskTimer = new() { Interval = 4500 };
    private readonly string sessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SourceCodeEditor", "session.json");
    private int untitledCounter = 1;
    private bool ignoreTextChanged;
    private bool isClosing;
    private bool autoIndent = true;
    private bool showLineNumbers = true;
    private bool defaultWordWrap;
    private bool fullscreen;
    private FormBorderStyle normalBorder;
    private FormWindowState normalState;
    private SearchDialog? searchWindow;
    private readonly List<string> recentFiles = new();
    private int preferredLeftWidth = 252;
    private ToolStripMenuItem? recentMenu;

    public MainForm(string[] args)
    {
        Text = "SourceCodeEditor";
        Width = 1380; Height = 860;
        MinimumSize = new Size(820, 500);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", DefaultFontPoints, FontStyle.Regular, GraphicsUnit.Point);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        AllowDrop = true;
        editors.AllowDrop = true;
        var menu = BuildMenu();
        MainMenuStrip = menu;
        navigation.TabPages.Add(BuildExplorerPage());
        foreach (var (caption, tree) in new[]
        {
            ("Opened Files", opened), ("Closed Files", closed), ("Unsaved Files", unsaved)
        })
        {
            var page = new TabPage(caption);
            page.Controls.Add(tree);
            navigation.TabPages.Add(page);
            tree.NodeMouseDoubleClick += (_, e) =>
            {
                if (e.Node.Tag is EditorDocument d) Activate(d);
            };
            tree.NodeMouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    tree.SelectedNode = e.Node;
                    if (e.Node.Tag is EditorDocument document) ShowDocumentContext(tree, e.Location, document);
                }
            };
        }
        split.Panel1.Controls.Add(navigation);
        split.Panel2.Controls.Add(editors);

        var status = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = false };
        status.Items.AddRange(new ToolStripItem[] { caretStatus, encodingStatus, eolStatus, zoomStatus });
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        menu.Dock = DockStyle.Fill;
        root.Controls.Add(menu, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(status, 0, 2);
        Controls.Add(root);

        // The menu and tab strips now have separate layout rows; neither can obscure tabs.
        Shown += (_, _) => BeginInvoke(new Action(() =>
        {
            InitializePaneDimensions();
            // The explorer always starts at Documents, never the last session folder.
            NavigateExplorer(GetDocumentsDirectory());
        }));
        editors.DrawItem += DrawEditorTab;
        editors.MouseDown += EditorTabMouseDown;
        editors.SelectedIndexChanged += (_, _) => { UpdateStatus(); PersistSession(); };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Tab)
            {
                if (editors.TabCount > 1)
                {
                    int offset = e.Shift ? -1 : 1;
                    editors.SelectedIndex = (editors.SelectedIndex + offset + editors.TabCount) % editors.TabCount;
                }
                e.Handled = true; e.SuppressKeyPress = true;
            }
        };
        DragEnter += FileDragEnter;
        DragDrop += FileDragDrop;
        editors.DragEnter += FileDragEnter;
        editors.DragDrop += FileDragDrop;
        split.SplitterMoved += (_, _) =>
        {
            if (!split.Panel1Collapsed && split.SplitterDistance >= 160 && split.SplitterDistance <= 360)
                preferredLeftWidth = split.SplitterDistance;
        };
        FormClosing += OnMainFormClosing;
        sessionTimer.Tick += (_, _) => PersistSession();
        diskTimer.Tick += (_, _) => CheckExternalChanges();

        SetApplicationFontSize(applicationFontPoints, false);
        SetEditorFontSize(editorFontPoints, false);
        RestoreSession();
        foreach (string path in args.Where(File.Exists)) OpenPath(path);
        if (!documents.Any(x => !x.Closed)) NewDocument();
        RefreshDocumentTrees();
        sessionTimer.Start(); diskTimer.Start();
        UpdateStatus();
    }

    private static TreeView MakeDocumentTree() => new()
    {
        Dock = DockStyle.Fill, HideSelection = false, ShowRootLines = false,
        ShowLines = false, BorderStyle = BorderStyle.None, ShowNodeToolTips = true
    };

    private void InitializePaneDimensions()
    {
        if (split.IsDisposed) return;
        int available = Math.Max(0, split.ClientSize.Width - split.SplitterWidth);
        if (available < 500) return;
        // Set a safe distance BEFORE applying minimums, so each subsequent
        // WinForms validation sees a legal interval (including on HiDPI).
        int target = Math.Clamp(preferredLeftWidth, 242, available - 340);
        split.SplitterDistance = target;
        split.Panel1MinSize = 242;
        split.Panel2MinSize = 340;
        navigation.SelectedIndex = Math.Clamp(navigation.SelectedIndex, 0, navigation.TabPages.Count - 1);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Font = new Font("Segoe UI", DefaultFontPoints, FontStyle.Regular, GraphicsUnit.Point) };
        var file = new ToolStripMenuItem("&File");
        AddMenu(file, "&New", Keys.Control | Keys.N, NewDocument);
        AddMenu(file, "&Open...", Keys.Control | Keys.O, OpenFilesFromDialog);
        AddMenu(file, "Open &Folder...", Keys.Control | Keys.Shift | Keys.O, ChooseExplorerFolder);
        AddMenu(file, "Save", Keys.Control | Keys.S, () => SaveDocument(CurrentDocument(), false));
        AddMenu(file, "Save &As...", Keys.Control | Keys.Shift | Keys.S, () => SaveDocument(CurrentDocument(), true));
        AddMenu(file, "Save A&ll", Keys.Control | Keys.Alt | Keys.S, SaveAll);
        AddMenu(file, "Save &Backup Copy...", Keys.None, SaveBackup);
        file.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(file, "&Reload from Disk", Keys.None, ReloadCurrent);
        AddMenu(file, "Close &Tab", Keys.Control | Keys.W, CloseCurrent);
        AddMenu(file, "Close All", Keys.Control | Keys.Shift | Keys.W, CloseAll);
        file.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(file, "&Print...", Keys.Control | Keys.P, PrintCurrent);
        recentMenu = new ToolStripMenuItem("Recent Files");
        file.DropDownItems.Add(recentMenu);
        AddMenu(file, "&Exit", Keys.None, Close);

        var edit = new ToolStripMenuItem("&Edit");
        AddMenu(edit, "Undo", Keys.Control | Keys.Z, () => Edit(r => { if (r.CanUndo) r.Undo(); }));
        AddMenu(edit, "Redo", Keys.Control | Keys.Y, () => Edit(r => { if (r.CanRedo) r.Redo(); }));
        edit.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(edit, "Cut", Keys.Control | Keys.X, () => Edit(r => r.Cut()));
        AddMenu(edit, "Copy", Keys.Control | Keys.C, () => Edit(r => r.Copy()));
        AddMenu(edit, "Paste", Keys.Control | Keys.V, () => Edit(r =>
        {
            if (Clipboard.ContainsText()) r.SelectedText = Clipboard.GetText();
        }));
        AddMenu(edit, "Select All", Keys.Control | Keys.A, () => Edit(r => r.SelectAll()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(edit, "Duplicate Line", Keys.Control | Keys.D, DuplicateLine);
        AddMenu(edit, "Delete Line", Keys.Control | Keys.L, DeleteLine);
        AddMenu(edit, "Move Line Up", Keys.Alt | Keys.Up, () => MoveLine(-1));
        AddMenu(edit, "Move Line Down", Keys.Alt | Keys.Down, () => MoveLine(1));
        AddMenu(edit, "Indent Selection", Keys.Control | Keys.OemCloseBrackets, () => IndentSelection(false));
        AddMenu(edit, "Unindent Selection", Keys.Control | Keys.OemOpenBrackets, () => IndentSelection(true));
        AddMenu(edit, "Toggle Comment", Keys.Control | Keys.OemQuestion, ToggleComment);
        AddMenu(edit, "UPPERCASE", Keys.Control | Keys.Shift | Keys.U, () => TransformSelection(x => x.ToUpperInvariant()));
        AddMenu(edit, "lowercase", Keys.Control | Keys.U, () => TransformSelection(x => x.ToLowerInvariant()));
        AddMenu(edit, "Trim Trailing Whitespace", Keys.None, TrimTrailingWhitespace);
        AddMenu(edit, "Sort Selected Lines A-Z", Keys.None, () => SortSelection(false));
        AddMenu(edit, "Sort Selected Lines Z-A", Keys.None, () => SortSelection(true));

        var search = new ToolStripMenuItem("&Search");
        AddMenu(search, "Find...", Keys.Control | Keys.F, ShowSearch);
        AddMenu(search, "Replace...", Keys.Control | Keys.H, ShowSearch);
        AddMenu(search, "Find in Files...", Keys.Control | Keys.Shift | Keys.F, OpenFindInFiles);
        AddMenu(search, "Go to Line...", Keys.Control | Keys.G, GoToLine);
        search.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(search, "Toggle Bookmark", Keys.Control | Keys.F2, ToggleBookmark);
        AddMenu(search, "Next Bookmark", Keys.F2, () => NavigateBookmark(false));
        AddMenu(search, "Previous Bookmark", Keys.Shift | Keys.F2, () => NavigateBookmark(true));

        var view = new ToolStripMenuItem("&View");
        var showNums = new ToolStripMenuItem("Show Line Numbers") { Checked = true, CheckOnClick = true };
        showNums.CheckedChanged += (_, _) =>
        {
            showLineNumbers = showNums.Checked;
            foreach (var d in documents) if (d.View != null) d.View.ShowLineNumbers = showLineNumbers;
        };
        view.DropDownItems.Add(showNums);
        var wrap = new ToolStripMenuItem("Word Wrap") { CheckOnClick = true };
        wrap.CheckedChanged += (_, _) =>
        {
            defaultWordWrap = wrap.Checked;
            foreach (var d in documents.Where(x => !x.Closed))
                if (d.View != null) { d.WordWrap = wrap.Checked; d.View.Editor.WordWrap = wrap.Checked; d.View.RefreshLineNumbers(); }
        };
        view.DropDownItems.Add(wrap);
        var indent = new ToolStripMenuItem("Auto Indent") { Checked = true, CheckOnClick = true };
        indent.CheckedChanged += (_, _) => autoIndent = indent.Checked;
        view.DropDownItems.Add(indent);
        view.DropDownItems.Add(new ToolStripSeparator());
        applicationFontMenu = new ToolStripMenuItem("Application Font (12 pt)");
        AddMenu(applicationFontMenu, "Increase Application Font (+1 pt)", Keys.None, () => SetApplicationFontSize(applicationFontPoints + 1));
        AddMenu(applicationFontMenu, "Decrease Application Font (-1 pt)", Keys.None, () => SetApplicationFontSize(applicationFontPoints - 1));
        AddMenu(applicationFontMenu, "Reset Application Font (12 pt)", Keys.None, () => SetApplicationFontSize(DefaultFontPoints));
        view.DropDownItems.Add(applicationFontMenu);
        editorFontMenu = new ToolStripMenuItem("RTF Editor Font (12 pt)");
        AddMenu(editorFontMenu, "Increase Editor Font (+1 pt)", Keys.None, () => SetEditorFontSize(editorFontPoints + 1));
        AddMenu(editorFontMenu, "Decrease Editor Font (-1 pt)", Keys.None, () => SetEditorFontSize(editorFontPoints - 1));
        AddMenu(editorFontMenu, "Reset Editor Font (12 pt)", Keys.None, () => SetEditorFontSize(DefaultFontPoints));
        view.DropDownItems.Add(editorFontMenu);
        view.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(view, "Zoom In", Keys.Control | Keys.Oemplus, () => ChangeZoom(0.10f));
        AddMenu(view, "Zoom Out", Keys.Control | Keys.OemMinus, () => ChangeZoom(-0.10f));
        AddMenu(view, "Reset Zoom", Keys.Control | Keys.NumPad0, () => SetZoom(1f));
        AddMenu(view, "Full Screen", Keys.F11, ToggleFullScreen);
        AddMenu(view, "Hide/Show File Navigator", Keys.F12, () => split.Panel1Collapsed = !split.Panel1Collapsed);

        var format = new ToolStripMenuItem("F&ormat");
        var encoding = new ToolStripMenuItem("Character Encoding");
        foreach (var choice in new (string label, string name, bool bom)[]
        {
            ("UTF-8", "utf-8", false), ("UTF-8 with BOM", "utf-8", true),
            ("UTF-16 Little Endian", "utf-16", true), ("UTF-16 Big Endian", "utf-16be", true),
            ("UTF-32 Little Endian", "utf-32", true), ("UTF-32 Big Endian", "utf-32be", true),
            ("Latin-1", "iso-8859-1", false)
        })
            AddMenu(encoding, choice.label, Keys.None, () => ConvertEncoding(choice.name, choice.bom));
        format.DropDownItems.Add(encoding);
        var lineEndings = new ToolStripMenuItem("Line Endings");
        AddMenu(lineEndings, "Windows (CRLF)", Keys.None, () => ConvertLineEndings("\r\n"));
        AddMenu(lineEndings, "Unix (LF)", Keys.None, () => ConvertLineEndings("\n"));
        AddMenu(lineEndings, "Classic Mac (CR)", Keys.None, () => ConvertLineEndings("\r"));
        format.DropDownItems.Add(lineEndings);

        var tools = new ToolStripMenuItem("&Tools");
        AddMenu(tools, "Locate Current File in File Explorer", Keys.None, OpenFileLocation);
        AddMenu(tools, "Save Session Now", Keys.None, PersistSession);

        var help = new ToolStripMenuItem("&Help");
        AddMenu(help, "About SourceCodeEditor", Keys.None, () => MessageBox.Show(this,
            "SourceCodeEditor - native Windows x64 RTF-based source editor.\n\n" +
            "Original LineNumberedRichTextBox derived from CSharpCodeDetective (MIT).\n" +
            "No plugin system. Developed for Visual Studio 2022 and .NET 8.",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange(new ToolStripItem[] { file, edit, search, view, format, BuildShellMenu(), tools, help });
        return menu;
    }

    private static void AddMenu(ToolStripMenuItem parent, string label, Keys shortcut, Action action)
    {
        var item = new ToolStripMenuItem(label) { ShortcutKeys = shortcut };
        item.Click += (_, _) => action();
        parent.DropDownItems.Add(item);
    }

    private void FileDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }
    private void FileDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths)
        {
            if (File.Exists(path)) OpenPath(path);
            else if (Directory.Exists(path)) NavigateExplorer(path);
        }
    }
    private EditorDocument? CurrentDocument() => editors.SelectedTab?.Tag as EditorDocument;
    private void Edit(Action<RichTextBox> action)
    {
        var editor = CurrentDocument()?.View?.Editor;
        if (editor != null) action(editor);
    }
    private void NewDocument()
    {
        var d = new EditorDocument { Name = "Untitled " + untitledCounter++, WordWrap = defaultWordWrap };
        documents.Add(d);
        Activate(d);
    }
    private void OpenFilesFromDialog()
    {
        using var picker = new OpenFileDialog { Filter = "All files (*.*)|*.*", Multiselect = true };
        if (picker.ShowDialog(this) == DialogResult.OK)
            foreach (var path in picker.FileNames) OpenPath(path);
    }
    private void OpenPath(string path)
    {
        try { path = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            MessageBox.Show(this, ex.Message, "Invalid file path"); return;
        }
        var existing = documents.FirstOrDefault(d => d.Path != null &&
            string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { Activate(existing); return; }
        try
        {
            var d = new EditorDocument { Path = path, Name = Path.GetFileName(path), WordWrap = defaultWordWrap };
            LoadFromDisk(d);
            documents.Add(d);
            AddRecentFile(path);
            Activate(d);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "Unable to open file", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private static void LoadFromDisk(EditorDocument d)
    {
        if (d.Path == null) return;
        var content = TextFiles.Read(d.Path);
        d.Buffer = content.text;
        d.EncodingName = content.encodingName;
        d.HasBom = content.bom;
        d.Newline = content.newline;
        var info = new FileInfo(d.Path);
        d.WriteUtc = info.LastWriteTimeUtc;
        d.LastLength = info.Length;
        d.DiskHash = content.hash;
        d.LastNotifiedWriteUtc = DateTime.MinValue;
        d.LastNotifiedLength = 0;
        d.Dirty = false;
        d.MissingOnDisk = false;
        d.UnavailableOnDisk = false;
        d.ExternalConflict = false;
    }
    // A closed entry is a historical snapshot, not proof that the disk path
    // still exists. Revalidate it whenever the user opens the entry.
    private bool PrepareClosedDocument(EditorDocument d)
    {
        if (d.Path == null) return true;
        var availability = DocumentSafety.Inspect(d.Path);
        if (availability != FileAvailability.Present)
        {
            string original = d.Path;
            string reason = availability == FileAvailability.Missing
                ? "The file or one of its parent folders no longer exists."
                : "The original file cannot be accessed as a regular file.";
            if (MessageBox.Show(this, reason + "\n\n" + original +
                "\n\nOpen the cached contents as an UNSAVED recovered document? " +
                "Saving it will require Save As and will NOT recreate the removed folder automatically.",
                "Reopen closed document", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;
            DocumentSafety.DetachToRecoveredBuffer(d);
            return true;
        }
        if (!d.Dirty)
        {
            try { LoadFromDisk(d); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                if (MessageBox.Show(this, "The file could not be reloaded: " + ex.Message +
                    "\n\nOpen its cached text as a recovered unsaved document?",
                    "Reopen closed document", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return false;
                DocumentSafety.DetachToRecoveredBuffer(d);
            }
        }
        else if (DocumentSafety.AssessSaveTarget(d) == SaveTargetState.ModifiedExternally)
            d.ExternalConflict = true; // Keep the recovered/unsaved buffer.
        d.MissingOnDisk = false;
        d.UnavailableOnDisk = false;
        return true;
    }

    private void Activate(EditorDocument d)
    {
        if (d.Closed && !PrepareClosedDocument(d)) return;
        if (d.Closed) d.Closed = false;
        if (d.Tab == null)
        {
            var tab = new TabPage(d.ToString()) { Tag = d, ToolTipText = d.Path ?? d.Name, Padding = Padding.Empty };
            var control = new LineNumberedRichTextBox
            {
                DocumentFilePath = d.Path ?? d.Name, ShowLineNumbers = showLineNumbers, Bookmarks = d.Bookmarks
            };
            control.Editor.WordWrap = d.WordWrap;
            control.SetBaseFontSize(editorFontPoints);
            control.Editor.ZoomFactor = Math.Clamp(d.Zoom, 0.5f, 4f);
            ignoreTextChanged = true;
            control.LoadText(d.Buffer, d.Path ?? d.Name);
            control.Editor.SelectionStart = Math.Clamp(d.Caret, 0, control.Editor.TextLength);
            ignoreTextChanged = false;
            control.Editor.TextChanged += (_, _) =>
            {
                if (ignoreTextChanged) return;
                d.Buffer = control.Editor.Text;
                d.Dirty = true;
                tab.Text = d.ToString();
                RefreshDocumentTrees(); UpdateStatus();
            };
            control.Editor.SelectionChanged += (_, _) =>
            {
                d.Caret = control.Editor.SelectionStart;
                control.RefreshLineNumbers();
                UpdateStatus();
            };
            control.Editor.ZoomChanged += (_, _) =>
            {
                d.Zoom = control.Editor.ZoomFactor;
                control.RefreshLineNumbers(); UpdateStatus();
            };
            control.Editor.KeyDown += (_, e) => EditorKeyDown(control.Editor, e);
            tab.Controls.Add(control);
            d.Tab = tab; d.View = control;
            editors.TabPages.Add(tab);
        }
        editors.SelectedTab = d.Tab;
        RefreshDocumentTrees(); UpdateStatus();
        d.View?.Editor.Focus();
    }
    private bool SaveDocument(EditorDocument? d, bool saveAs)
    {
        if (d == null) return false;
        bool overwriteConfirmed = false;
        (DateTime stamp, long length)? approvedVersion = null;
        string? approvedHash = null;
        string? path = d.Path;
        if (!saveAs && path != null)
        {
            var target = DocumentSafety.AssessSaveTarget(d);
            if (target == SaveTargetState.Missing)
            {
                MessageBox.Show(this,
                    "The original file or its folder has been removed.\n\n" + path +
                    "\n\nSave As is required; the old path will not be silently recreated.",
                    "Original file missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                saveAs = true;
            }
            else if (target == SaveTargetState.Unavailable)
            {
                MessageBox.Show(this, "The original path is inaccessible or no longer a file. " +
                    "Choose Save As to store your text elsewhere.", "Save blocked",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            else if (target == SaveTargetState.ModifiedExternally)
            {
                var answer = MessageBox.Show(this,
                    "This file has changed on disk since the current editor contents were loaded.\n\n" +
                    path + "\n\nYes: explicitly overwrite the external version.\n" +
                    "No: Save As another file.\nCancel: keep editing without saving.",
                    "External file conflict", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (answer == DialogResult.Cancel) return false;
                if (answer == DialogResult.No) saveAs = true;
                else
                {
                    try
                    {
                        approvedVersion = DocumentSafety.Stamp(path);
                        approvedHash = DocumentSafety.HashDiskFile(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        MessageBox.Show(this, ex.Message, "File changed during save");
                        return false;
                    }
                    overwriteConfirmed = true;
                }
            }
        }
        if (saveAs || path == null)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "All files (*.*)|*.*",
                FileName = path == null ? d.Name.Replace(" (Recovered)", "") : Path.GetFileName(path),
                InitialDirectory = DocumentSafety.SafeSaveDirectory(path ?? d.RecoverySourcePath, GetDocumentsDirectory()),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
            saveAs = true;
            if (documents.Any(other => !ReferenceEquals(other, d) && other.Path != null &&
                string.Equals(other.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "This path already belongs to another editor document.", "Save As");
                return false;
            }
        }
        try
        {
            // Repeat the availability/stamp check immediately before committing.
            // A late change fails closed rather than deleting the other version.
            if (!saveAs && d.Path != null)
            {
                var current = DocumentSafety.AssessSaveTarget(d);
                if (current == SaveTargetState.Missing || current == SaveTargetState.Unavailable)
                {
                    MessageBox.Show(this, "The file disappeared or became inaccessible during Save. " +
                        "The editor buffer is unchanged; use Save As.", "Save interrupted");
                    return false;
                }
                if (current == SaveTargetState.ModifiedExternally && !overwriteConfirmed)
                {
                    MessageBox.Show(this, "The file changed again before it could be saved. " +
                        "No data was overwritten.", "Save interrupted");
                    return false;
                }
                if (overwriteConfirmed && approvedVersion != null &&
                    (DocumentSafety.Stamp(path!) != approvedVersion.Value ||
                     !string.Equals(DocumentSafety.HashDiskFile(path!), approvedHash, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(this, "Another external change occurred after you approved overwrite. " +
                        "No data was overwritten.", "Save interrupted");
                    return false;
                }
            }
            string content = d.View?.Editor.Text ?? d.Buffer;
            var encoded = TextFiles.Encode(content, d.EncodingName, d.HasBom, d.Newline);
            TextFiles.WriteAtomic(path!, encoded);
            d.Path = Path.GetFullPath(path!);
            d.Name = Path.GetFileName(path!);
            d.Dirty = false;
            d.Buffer = content;
            d.MissingOnDisk = false;
            d.UnavailableOnDisk = false;
            d.ExternalConflict = false;
            d.RecoverySourcePath = null;
            var info = new FileInfo(path!);
            d.WriteUtc = info.LastWriteTimeUtc;
            d.LastLength = info.Length;
            d.DiskHash = Convert.ToHexString(SHA256.HashData(encoded));
            d.LastNotifiedWriteUtc = DateTime.MinValue;
            d.LastNotifiedLength = 0;
            if (d.View != null) d.View.DocumentFilePath = path!;
            if (d.Tab != null) { d.Tab.Text = d.ToString(); d.Tab.ToolTipText = path; }
            AddRecentFile(path!);
            RefreshDocumentTrees(); UpdateStatus(); PersistSession();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EncoderFallbackException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Unable to save file", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
    private void SaveAll()
    {
        foreach (var d in documents.Where(x => !x.Closed && x.Dirty).ToArray())
            if (!SaveDocument(d, false)) break;
    }
    private void SaveBackup()
    {
        var d = CurrentDocument(); if (d == null) return;
        if (d.Path == null && !SaveDocument(d, true)) return;
        try
        {
            var suffix = "_bkp_" + DateTime.Now.ToString("yyyy_MM_dd__HH_mm_ss");
            string filename = d.Path! + suffix;
            TextFiles.WriteAtomic(filename, TextFiles.Encode(d.View?.Editor.Text ?? d.Buffer, d.EncodingName, d.HasBom, d.Newline));
            MessageBox.Show(this, filename, "Backup saved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EncoderFallbackException or NotSupportedException)
        { MessageBox.Show(this, ex.Message, "Backup failed"); }
    }
    private bool ConfirmCanClose(EditorDocument d)
    {
        if (!d.Dirty) return true;
        if (d.Tab != null) editors.SelectedTab = d.Tab;
        var result = MessageBox.Show(this, $"Save changes to '{d.Name}'?", "SourceCodeEditor",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.Yes) return SaveDocument(d, false);
        // User selected "No": keep the on-disk version, not the discarded edits.
        if (d.Path != null && DocumentSafety.Inspect(d.Path) == FileAvailability.Present)
        {
            try { LoadFromDisk(d); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                // The user explicitly chose Discard. Never let a deleted or
                // unreadable disk file crash the tab-close / app-exit flow.
                d.Dirty = false;
                d.Buffer = string.Empty;
            }
        }
        else { d.Dirty = false; d.Buffer = string.Empty; }
        return true;
    }
    private void CloseCurrent()
    {
        if (editors.SelectedTab is TabPage tab && tab.Tag is EmbeddedShellControl)
        {
            CloseShellTab(tab);
            return;
        }
        var d = CurrentDocument(); if (d != null) CloseDocument(d);
    }
    private bool CloseDocument(EditorDocument d)
    {
        if (!ConfirmCanClose(d)) return false;
        if (d.View != null) { d.Caret = d.View.Editor.SelectionStart; d.Zoom = d.View.Editor.ZoomFactor; }
        if (d.Tab != null) { editors.TabPages.Remove(d.Tab); d.Tab.Dispose(); }
        d.Tab = null; d.View = null;
        if (d.Path == null && string.IsNullOrEmpty(d.Buffer)) documents.Remove(d);
        else d.Closed = true;
        RefreshDocumentTrees(); PersistSession(); UpdateStatus();
        return true;
    }
    private void CloseAll()
    {
        foreach (var d in documents.Where(x => !x.Closed).ToArray())
            if (!CloseDocument(d)) return;
        CloseAllShellTabs();
    }
    private void ReloadCurrent()
    {
        var d = CurrentDocument(); if (d?.Path == null) return;
        if (d.Dirty && MessageBox.Show(this, "Discard unsaved changes and reload from disk?", "Reload",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            LoadFromDisk(d);
            if (d.View != null)
            {
                ignoreTextChanged = true;
                try { d.View.LoadText(d.Buffer, d.Path); }
                finally { ignoreTextChanged = false; }
            }
            if (d.Tab != null) d.Tab.Text = d.ToString();
            RefreshDocumentTrees(); UpdateStatus();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Reload failed"); }
    }
    private static void FillTree(TreeView tree, IEnumerable<EditorDocument> items)
    {
        tree.BeginUpdate();
        tree.Nodes.Clear();
        foreach (var d in items) tree.Nodes.Add(new TreeNode(d.ToString()) { Tag = d, ToolTipText = d.Path ?? d.Name });
        tree.EndUpdate();
    }
    private void RefreshDocumentTrees()
    {
        FillTree(opened, documents.Where(d => !d.Closed));
        FillTree(closed, documents.Where(d => d.Closed));
        FillTree(unsaved, documents.Where(d => !d.Closed && (d.Dirty || d.Path == null)));
    }
    private void ShowDocumentContext(Control source, Point point, EditorDocument d)
    {
        var menu = new ContextMenuStrip { Font = Font };
        menu.Items.Add("Open / Activate", null, (_, _) => Activate(d));
        if (!d.Closed)
        {
            menu.Items.Add("Close", null, (_, _) => CloseDocument(d));
            menu.Items.Add("Close other tabs", null, (_, _) =>
            {
                foreach (var other in documents.Where(x => !x.Closed && !ReferenceEquals(x, d)).ToArray())
                    if (!CloseDocument(other)) break;
            });
        }
        if (d.Path != null)
        {
            menu.Items.Add("Locate in File Explorer", null, (_, _) => ShowFileInExplorer(d.Path));
            menu.Items.Add("Open shell in file's folder", null, (_, _) => OpenEmbeddedShell(Path.GetDirectoryName(d.Path)));
            menu.Items.Add("Copy full path", null, (_, _) => Clipboard.SetText(d.Path));
        }
        if (d.Closed) menu.Items.Add("Remove from Closed Files", null, (_, _) =>
        {
            documents.Remove(d); RefreshDocumentTrees(); PersistSession();
        });
        // ContextMenuStrip must remain alive while displayed.
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(source, point);
    }
    private void AddRecentFile(string path)
    {
        recentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        recentFiles.Insert(0, path);
        if (recentFiles.Count > 20) recentFiles.RemoveAt(recentFiles.Count - 1);
        RefreshRecentMenu();
    }
    private void RefreshRecentMenu()
    {
        if (recentMenu == null) return;
        recentMenu.DropDownItems.Clear();
        foreach (var file in recentFiles.ToArray())
        {
            string path = file;
            recentMenu.DropDownItems.Add(new ToolStripMenuItem(path, null, (_, _) => OpenPath(path)));
        }
        recentMenu.Enabled = recentFiles.Count != 0;
    }
    private void DrawEditorTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= editors.TabCount) return;
        var rect = editors.GetTabRect(e.Index);
        bool selected = e.Index == editors.SelectedIndex;
        using var bg = new SolidBrush(selected ? Color.White : Color.FromArgb(235, 239, 245));
        e.Graphics.FillRectangle(bg, rect);
        using var line = new Pen(Color.FromArgb(202, 208, 216));
        e.Graphics.DrawRectangle(line, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        var closeRect = GetEditorTabCloseRect(rect);
        var textRect = new Rectangle(rect.Left + 9, rect.Top + 2,
            Math.Max(0, closeRect.Left - rect.Left - 14), rect.Height - 4);
        TextRenderer.DrawText(e.Graphics, editors.TabPages[e.Index].Text,
            editors.Font, textRect, Color.FromArgb(40, 44, 50),
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics, "×", editors.Font, closeRect, Color.FromArgb(92, 96, 107),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
    private Rectangle GetEditorTabCloseRect(Rectangle tabRect)
    {
        int closeSize = Math.Max(18, editors.Font.Height);
        int rightPadding = Math.Max(5, editors.Font.Height / 5);
        return new Rectangle(tabRect.Right - closeSize - rightPadding,
            tabRect.Top + 2, closeSize, Math.Max(0, tabRect.Height - 4));
    }

    private void EditorTabMouseDown(object? sender, MouseEventArgs e)
    {
        for (int i = 0; i < editors.TabCount; i++)
        {
            Rectangle r = editors.GetTabRect(i);
            if (!r.Contains(e.Location)) continue;
            var page = editors.TabPages[i];
            if (page.Tag is EmbeddedShellControl)
            {
                if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && GetEditorTabCloseRect(r).Contains(e.Location)))
                    CloseShellTab(page);
                else if (e.Button == MouseButtons.Right)
                    ShowShellContext(e.Location, page);
                return;
            }
            if (page.Tag is not EditorDocument doc) return;
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && GetEditorTabCloseRect(r).Contains(e.Location)))
                CloseDocument(doc);
            else if (e.Button == MouseButtons.Right)
                ShowDocumentContext(editors, e.Location, doc);
            return;
        }
    }
    private void UpdateStatus()
    {
        var d = CurrentDocument();
        var editor = d?.View?.Editor;
        if (editor == null)
        {
            if (editors.SelectedTab?.Tag is EmbeddedShellControl shell)
            {
                caretStatus.Text = "CMD shell: " + shell.InitialDirectory;
                encodingStatus.Text = ""; eolStatus.Text = ""; zoomStatus.Text = "";
                Text = "CMD - SourceCodeEditor";
            }
            else
            {
                caretStatus.Text = "Ready"; encodingStatus.Text = ""; eolStatus.Text = ""; zoomStatus.Text = "";
                Text = "SourceCodeEditor";
            }
            return;
        }
        int line = editor.GetLineFromCharIndex(editor.SelectionStart);
        int beginning = editor.GetFirstCharIndexFromLine(line);
        int col = editor.SelectionStart - Math.Max(0, beginning) + 1;
        caretStatus.Text = $"Ln {line + 1}, Col {col} | {editor.TextLength:N0} characters | {editor.Lines.Length:N0} lines" +
            (d!.MissingOnDisk ? " | ORIGINAL MISSING - SAVE AS" :
             d.UnavailableOnDisk ? " | FILE INACCESSIBLE" :
             d.ExternalConflict ? " | DISK CONFLICT - REVIEW BEFORE SAVING" : "");
        encodingStatus.Text = d!.EncodingName.ToUpperInvariant() + (d.HasBom ? " BOM" : "");
        eolStatus.Text = d.Newline == "\r\n" ? "CRLF" : d.Newline == "\n" ? "LF" : "CR";
        zoomStatus.Text = $"{editor.ZoomFactor:P0}";
        Text = $"{d.Name}{(d.Dirty ? " *" : "")} - SourceCodeEditor";
    }
    private void ShowSearch()
    {
        if (searchWindow == null || searchWindow.IsDisposed)
        {
            searchWindow = new SearchDialog(() => CurrentDocument()?.View?.Editor);
            searchWindow.SetInterfaceFont(Font);
            searchWindow.FormClosed += (_, _) => searchWindow = null;
        }
        searchWindow.Show(this);
        searchWindow.Activate();
    }
    private void OpenFindInFiles()
    {
        var dialog = new FindInFilesDialog(
            CurrentDocument()?.Path is string p ? Path.GetDirectoryName(p)! : explorerRoot,
            (path, line) =>
            {
                OpenPath(path);
                var editor = CurrentDocument()?.View?.Editor;
                if (editor == null) return;
                int idx = editor.GetFirstCharIndexFromLine(Math.Clamp(line - 1, 0, Math.Max(0, editor.Lines.Length - 1)));
                if (idx >= 0) { editor.Select(idx, 0); editor.ScrollToCaret(); editor.Focus(); }
            });
        dialog.SetInterfaceFont(Font);
        dialog.Show(this);
    }
    private void ChangeZoom(float delta)
    {
        var ed = CurrentDocument()?.View?.Editor;
        if (ed != null) SetZoom(ed.ZoomFactor + delta);
    }
    private void SetZoom(float scale)
    {
        var d = CurrentDocument();
        if (d?.View == null) return;
        d.Zoom = Math.Clamp(scale, 0.5f, 4f);
        d.View.Editor.ZoomFactor = d.Zoom;
        d.View.RefreshLineNumbers(); UpdateStatus();
    }
    private void ToggleFullScreen()
    {
        if (!fullscreen)
        {
            normalBorder = FormBorderStyle; normalState = WindowState;
            FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Maximized;
        }
        else
        {
            WindowState = normalState; FormBorderStyle = normalBorder;
        }
        fullscreen = !fullscreen;
    }
    private void ConvertEncoding(string name, bool bom)
    {
        var d = CurrentDocument(); if (d == null) return;
        if (d.EncodingName == name && d.HasBom == bom) return;
        d.EncodingName = name; d.HasBom = bom; d.Dirty = true;
        if (d.Tab != null) d.Tab.Text = d.ToString();
        RefreshDocumentTrees(); UpdateStatus();
    }
    private void ConvertLineEndings(string eol)
    {
        var d = CurrentDocument(); if (d == null) return;
        if (d.Newline == eol) return;
        d.Newline = eol; d.Dirty = true;
        if (d.Tab != null) d.Tab.Text = d.ToString();
        RefreshDocumentTrees(); UpdateStatus();
    }
}
