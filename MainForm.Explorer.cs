using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SourceCodeEditor;

public sealed partial class MainForm
{
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System, AutoSize = false,
            Padding = Padding.Empty
        };
        var browse = new ToolStripButton("Browse…") { ToolTipText = "Choose a folder to browse" };
        browse.Click += (_, _) => ChooseExplorerFolder();
        var refresh = new ToolStripButton("Refresh") { ToolTipText = "Refresh current folder" };
        refresh.Click += (_, _) => NavigateExplorer(explorerRoot);
        toolbar.Items.AddRange(new ToolStripItem[] { browse, refresh });
        explorerAddress.Text = explorerRoot;
        explorerAddress.Margin = new Padding(2, 1, 2, 2);
        explorerAddress.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                NavigateExplorer(explorerAddress.Text);
            }
        };
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(explorerAddress, 0, 1);
        layout.Controls.Add(explorer, 0, 2);
        page.Controls.Add(layout);

        explorerMenu.Items.Add("Open", null, (_, _) => OpenExplorerSelected());
        explorerMenu.Items.Add("Open shell here (cmd.exe)", null, (_, _) => OpenShell(GetExplorerSelectedPath()));
        explorerMenu.Items.Add("Open PowerShell here", null, (_, _) => OpenShell(GetExplorerSelectedPath(), true));
        explorerMenu.Items.Add(new ToolStripSeparator());
        explorerMenu.Items.Add("Set folder as explorer root", null, (_, _) =>
        {
            string? selected = GetExplorerSelectedPath();
            if (selected != null)
                NavigateExplorer(Directory.Exists(selected) ? selected : Path.GetDirectoryName(selected) ?? explorerRoot);
        });
        explorerMenu.Items.Add("Go to parent folder", null, (_, _) =>
            NavigateExplorer(Directory.GetParent(explorerRoot)?.FullName ?? explorerRoot));
        explorer.ContextMenuStrip = explorerMenu;
        explorer.NodeMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) explorer.SelectedNode = e.Node;
        };
        explorer.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node.Tag is not string path) return;
            if (File.Exists(path)) OpenPath(path);
            else if (Directory.Exists(path) && !e.Node.IsExpanded) e.Node.Expand();
        };
        explorer.BeforeExpand += (_, e) =>
        {
            if (e.Node.Tag is string path && Directory.Exists(path)) PopulateExplorerNode(e.Node, path);
        };
        NavigateExplorer(explorerRoot);
        return page;
    }

    private void ChooseExplorerFolder()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = "Select the folder to show in File Explorer",
            SelectedPath = Directory.Exists(explorerRoot) ? explorerRoot : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ShowNewFolderButton = true
        };
        if (picker.ShowDialog(this) == DialogResult.OK) NavigateExplorer(picker.SelectedPath);
    }
    private void NavigateExplorer(string path)
    {
        try
        {
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            if (!Directory.Exists(full))
            {
                MessageBox.Show(this, "Folder does not exist: " + full, "File Explorer");
                explorerAddress.Text = explorerRoot;
                return;
            }
            explorerRoot = full;
            explorerAddress.Text = full;
            explorer.BeginUpdate();
            try
            {
                explorer.Nodes.Clear();
                var root = ExplorerNode(full, true);
                explorer.Nodes.Add(root);
                PopulateExplorerNode(root, full);
                root.Expand();
            }
            finally { explorer.EndUpdate(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "File Explorer");
        }
    }
    private static TreeNode ExplorerNode(string full, bool isFolder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
        var node = new TreeNode(string.IsNullOrWhiteSpace(name) ? full : name)
        {
            Tag = full, ToolTipText = full,
            ForeColor = isFolder ? Color.FromArgb(33, 76, 135) : SystemColors.WindowText
        };
        if (isFolder) node.Nodes.Add(new TreeNode("Loading..."));
        return node;
    }
    private void PopulateExplorerNode(TreeNode parent, string folder)
    {
        parent.Nodes.Clear();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(folder).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                parent.Nodes.Add(ExplorerNode(dir, true));
            foreach (var file in Directory.EnumerateFiles(folder).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                parent.Nodes.Add(ExplorerNode(file, false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            parent.Nodes.Add(new TreeNode("[Folder unavailable]") { ToolTipText = ex.Message });
        }
    }
    private string? GetExplorerSelectedPath() => explorer.SelectedNode?.Tag as string;
    private void OpenExplorerSelected()
    {
        string? selected = GetExplorerSelectedPath();
        if (selected == null) return;
        if (File.Exists(selected)) OpenPath(selected);
        else if (Directory.Exists(selected)) NavigateExplorer(selected);
    }
    private void OpenShell(string? requestedPath, bool powershell = false)
    {
        string folder = requestedPath == null ? explorerRoot :
            Directory.Exists(requestedPath) ? requestedPath : Path.GetDirectoryName(requestedPath) ?? explorerRoot;
        if (!Directory.Exists(folder)) return;
        try
        {
            Process.Start(new ProcessStartInfo(powershell ? "powershell.exe" : "cmd.exe")
            {
                WorkingDirectory = folder, UseShellExecute = true
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open shell failed"); }
    }
    private void OpenFileLocation()
    {
        var path = CurrentDocument()?.Path;
        if (path == null) return;
        ShowFileInExplorer(path);
    }
    private void ShowFileInExplorer(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = "/select,\"" + path.Replace("\"", "", StringComparison.Ordinal) + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Explorer failed"); }
    }
}
