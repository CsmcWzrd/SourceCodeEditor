using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SourceCodeEditor;

// Windows Explorer is deliberately NOT embedded or launched here. The tree is an
// ordinary WinForms TreeView populated from System.IO, with one node per drive.
public sealed partial class MainForm
{
    private sealed class ExplorerItem
    {
        public string Path { get; }
        public bool IsFolder { get; }
        public bool Loaded { get; set; }
        public ExplorerItem(string path, bool isFolder)
        {
            Path = path;
            IsFolder = isFolder;
        }
    }

    private static string GetUserHome()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(home) ? home : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string GetDocumentsDirectory()
    {
        // Prefer the literal Documents folder in the user's home, as requested.
        // Fall back to the user's configured Documents known folder if it has
        // been redirected to another disk or OneDrive.
        string homeDocs = Path.Combine(GetUserHome(), "Documents");
        if (Directory.Exists(homeDocs)) return homeDocs;
        string configured = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(configured) ? configured : GetUserHome();
    }

    private TabPage BuildExplorerPage()
    {
        var page = new TabPage("File Explorer") { Padding = Padding.Empty };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = Padding.Empty, Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden,
            AutoSize = false, RenderMode = ToolStripRenderMode.System,
            Padding = Padding.Empty
        };
        var documentsButton = new ToolStripButton("Documents") { ToolTipText = "Go to your Documents folder" };
        documentsButton.Click += (_, _) => NavigateExplorer(GetDocumentsDirectory());
        var drivesButton = new ToolStripButton("Drives") { ToolTipText = "Show all available Windows drives" };
        drivesButton.Click += (_, _) =>
        {
            RefreshDriveTree();
            explorerAddress.Text = GetDocumentsDirectory();
        };
        var refreshButton = new ToolStripButton("Refresh") { ToolTipText = "Refresh drives and selected folder" };
        refreshButton.Click += (_, _) => RefreshExplorer();
        toolbar.Items.AddRange(new ToolStripItem[] { documentsButton, drivesButton, refreshButton });

        explorerAddress.Text = GetDocumentsDirectory();
        explorerAddress.Margin = new Padding(2, 1, 2, 1);
        explorerAddress.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            NavigateExplorer(explorerAddress.Text);
        };
        explorer.ShowPlusMinus = true;
        explorer.ShowRootLines = true;
        explorer.HideSelection = false;
        explorer.NodeMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) explorer.SelectedNode = e.Node;
        };
        explorer.AfterSelect += (_, e) =>
        {
            if (e.Node.Tag is not ExplorerItem item) return;
            explorerRoot = item.IsFolder ? item.Path : Path.GetDirectoryName(item.Path) ?? GetDocumentsDirectory();
            explorerAddress.Text = explorerRoot;
        };
        explorer.BeforeExpand += (_, e) =>
        {
            if (e.Node.Tag is ExplorerItem { IsFolder: true, Loaded: false })
                PopulateExplorerNode(e.Node);
        };
        explorer.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node.Tag is not ExplorerItem item) return;
            if (item.IsFolder) e.Node.Expand();
            else OpenPath(item.Path);
        };

        explorerMenu.Items.Add("Open", null, (_, _) => OpenExplorerSelected());
        explorerMenu.Items.Add("Open shell here (embedded tab)", null, (_, _) => OpenEmbeddedShell(GetExplorerSelectedFolder()));
        explorerMenu.Items.Add("Copy path", null, (_, _) =>
        {
            string? selected = GetExplorerSelectedPath();
            if (selected != null) Clipboard.SetText(selected);
        });
        explorerMenu.Items.Add(new ToolStripSeparator());
        explorerMenu.Items.Add("Go to Documents", null, (_, _) => NavigateExplorer(GetDocumentsDirectory()));
        explorerMenu.Items.Add("Go to parent folder", null, (_, _) =>
        {
            string current = GetExplorerSelectedFolder();
            try { NavigateExplorer(Directory.GetParent(current)?.FullName ?? current); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                MessageBox.Show(this, ex.Message, "Go to parent folder");
            }
        });
        explorer.ContextMenuStrip = explorerMenu;

        explorerLayout = layout;
        explorerToolbar = toolbar;
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(explorerAddress, 0, 1);
        layout.Controls.Add(explorer, 0, 2);
        page.Controls.Add(layout);
        RefreshDriveTree();
        return page;
    }

    // Folder navigation uses the address bar, not the Windows FolderBrowserDialog.
    private void ChooseExplorerFolder()
    {
        navigation.SelectedIndex = 0;
        explorerAddress.Focus();
        explorerAddress.SelectAll();
    }

    private void RefreshExplorer()
    {
        string requested = explorer.SelectedNode?.Tag is ExplorerItem item && item.IsFolder
            ? item.Path : explorerRoot;
        RefreshDriveTree();
        NavigateExplorer(Directory.Exists(requested) ? requested : GetDocumentsDirectory());
    }

    private void RefreshDriveTree()
    {
        explorer.BeginUpdate();
        try
        {
            explorer.Nodes.Clear();
            foreach (var drive in DriveInfo.GetDrives().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                string label = drive.Name;
                try
                {
                    if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
                        label += " (" + drive.VolumeLabel + ")";
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                var node = MakeExplorerNode(drive.Name, true, label);
                explorer.Nodes.Add(node);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Reading drives", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { explorer.EndUpdate(); }
    }

    private static TreeNode MakeExplorerNode(string path, bool isFolder, string? caption = null)
    {
        var name = caption ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        var node = new TreeNode(string.IsNullOrEmpty(name) ? path : name)
        {
            Tag = new ExplorerItem(path, isFolder),
            ToolTipText = path,
            ForeColor = isFolder ? Color.FromArgb(40, 73, 118) : SystemColors.WindowText
        };
        // Dummy node delays enumeration until a directory is actually expanded.
        if (isFolder) node.Nodes.Add(new TreeNode("Loading..."));
        return node;
    }

    private void PopulateExplorerNode(TreeNode node)
    {
        if (node.Tag is not ExplorerItem { IsFolder: true } folder || folder.Loaded) return;
        folder.Loaded = true;
        node.Nodes.Clear();
        // Junctions and symlinks may form cyclic directory graphs. Restrict
        // manual expansion depth instead of recursively following indefinitely.
        int depth = 0;
        for (TreeNode? parent = node.Parent; parent != null; parent = parent.Parent) depth++;
        if (depth >= 80)
        {
            node.Nodes.Add(new TreeNode("[Maximum explorer depth reached]"));
            return;
        }
        try
        {
            var options = new EnumerationOptions
            {
                AttributesToSkip = 0, // Hidden and System entries MUST be visible.
                IgnoreInaccessible = true, RecurseSubdirectories = false,
                ReturnSpecialDirectories = false
            };
            var entries = new DirectoryInfo(folder.Path).EnumerateFileSystemInfos("*", options).ToArray();
            foreach (var info in entries.Where(f => IsDirectoryEntry(f))
                     .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                node.Nodes.Add(MakeExplorerNode(info.FullName, true));
            foreach (var info in entries.Where(f => !IsDirectoryEntry(f) && TextSourceFileFilter.IsTextOrSource(f.FullName))
                     .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                node.Nodes.Add(MakeExplorerNode(info.FullName, false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            node.Nodes.Add(new TreeNode("[Unable to read folder]") { ToolTipText = ex.Message });
        }
    }

    private static bool IsDirectoryEntry(FileSystemInfo entry)
    {
        try { return (entry.Attributes & FileAttributes.Directory) != 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // Navigates the drive-rooted tree to a path; all drive roots remain visible.
    // At startup this is called with the user's Documents path, regardless of
    // the directory saved in an earlier session.
    private void NavigateExplorer(string path)
    {
        try
        {
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            bool isFile = File.Exists(full);
            string folder = isFile ? Path.GetDirectoryName(full)! : full;
            if (!Directory.Exists(folder))
            {
                MessageBox.Show(this, "Folder does not exist: " + folder, "File Explorer");
                explorerAddress.Text = explorerRoot;
                return;
            }
            if (explorer.Nodes.Count == 0) RefreshDriveTree();
            string? driveRoot = Path.GetPathRoot(folder);
            var drive = explorer.Nodes.Cast<TreeNode>().FirstOrDefault(n =>
                n.Tag is ExplorerItem item && string.Equals(
                    Path.TrimEndingDirectorySeparator(item.Path),
                    Path.TrimEndingDirectorySeparator(driveRoot ?? ""), StringComparison.OrdinalIgnoreCase));
            if (drive == null)
            {
                MessageBox.Show(this, "Drive not available in the explorer: " + driveRoot, "File Explorer");
                return;
            }
            TreeNode current = drive;
            bool reached = true;
            string relative = Path.GetRelativePath(driveRoot!, folder);
            var parts = relative == "." ? Array.Empty<string>() :
                relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                PopulateExplorerNode(current);
                current.Expand();
                var next = current.Nodes.Cast<TreeNode>().FirstOrDefault(n =>
                    n.Tag is ExplorerItem entry && entry.IsFolder &&
                    string.Equals(n.Text, part, StringComparison.OrdinalIgnoreCase));
                if (next == null)
                {
                    reached = false; // Folder disappeared or access was denied.
                    break;
                }
                current = next;
            }
            if (isFile)
            {
                PopulateExplorerNode(current);
                current.Expand();
                var match = current.Nodes.Cast<TreeNode>().FirstOrDefault(n =>
                    n.Tag is ExplorerItem entry && !entry.IsFolder &&
                    string.Equals(entry.Path, full, StringComparison.OrdinalIgnoreCase));
                if (match != null) current = match;
            }
            explorer.SelectedNode = current;
            current.EnsureVisible();
            explorerRoot = reached ? folder :
                ((current.Tag as ExplorerItem)?.IsFolder == true ?
                    ((ExplorerItem)current.Tag!).Path : GetDocumentsDirectory());
            explorerAddress.Text = explorerRoot;
            navigation.SelectedIndex = 0;
            if (!reached)
                MessageBox.Show(this, "Navigation stopped at the nearest accessible folder. " +
                    "A component of the selected path was removed, protected, or no longer available.",
                    "File Explorer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "File Explorer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string? GetExplorerSelectedPath() => (explorer.SelectedNode?.Tag as ExplorerItem)?.Path;

    private string GetExplorerSelectedFolder()
    {
        var item = explorer.SelectedNode?.Tag as ExplorerItem;
        string candidate = item == null ? explorerRoot :
            item.IsFolder ? item.Path : Path.GetDirectoryName(item.Path) ?? explorerRoot;
        return Directory.Exists(candidate) ? candidate : GetDocumentsDirectory();
    }

    private void OpenExplorerSelected()
    {
        if (explorer.SelectedNode?.Tag is not ExplorerItem item) return;
        if (item.IsFolder) NavigateExplorer(item.Path);
        else OpenPath(item.Path);
    }

    private void OpenFileLocation()
    {
        string? file = CurrentDocument()?.Path;
        if (file != null) ShowFileInExplorer(file);
    }

    private void ShowFileInExplorer(string? path)
    {
        // Locate in OUR explorer instead of starting Windows explorer.exe.
        if (!string.IsNullOrWhiteSpace(path)) NavigateExplorer(path);
    }
}
