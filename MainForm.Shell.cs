using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SourceCodeEditor;

public sealed partial class MainForm
{
    private ToolStripMenuItem BuildShellMenu()
    {
        var menu = new ToolStripMenuItem("S&hell");
        AddMenu(menu, "Open shell here", Keys.None, () => OpenEmbeddedShell(GetExplorerSelectedFolder()));
        var current = new ToolStripMenuItem("Open shell in current file's location");
        current.Click += (_, _) =>
        {
            string? file = CurrentDocument()?.Path;
            if (file != null) OpenEmbeddedShell(Path.GetDirectoryName(file));
        };
        menu.DropDownItems.Add(current);
        AddMenu(menu, "Open shell in user's home directory", Keys.None, () => OpenEmbeddedShell(GetUserHome()));
        AddMenu(menu, "Open shell in user's Documents directory", Keys.None, () => OpenEmbeddedShell(GetDocumentsDirectory()));
        var drives = new ToolStripMenuItem("Open shell in user's drive");
        drives.DropDownOpening += (_, _) =>
        {
            drives.DropDownItems.Clear();
            foreach (var drive in DriveInfo.GetDrives().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                string folder = drive.Name;
                var item = new ToolStripMenuItem(folder) { Enabled = Directory.Exists(folder) };
                item.Click += (_, _) => OpenEmbeddedShell(folder);
                drives.DropDownItems.Add(item);
            }
            if (drives.DropDownItems.Count == 0)
                drives.DropDownItems.Add(new ToolStripMenuItem("No drives found") { Enabled = false });
        };
        menu.DropDownItems.Add(drives);
        menu.DropDownOpening += (_, _) =>
        {
            string? path = CurrentDocument()?.Path;
            current.Enabled = path != null && Directory.Exists(Path.GetDirectoryName(path));
        };
        return menu;
    }

    private void OpenEmbeddedShell(string? path)
    {
        string folder = string.IsNullOrWhiteSpace(path) ? GetDocumentsDirectory() : path;
        if (File.Exists(folder)) folder = Path.GetDirectoryName(folder) ?? GetDocumentsDirectory();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "Cannot open shell: folder does not exist.\n" + folder,
                "SourceCodeEditor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            string name = Path.TrimEndingDirectorySeparator(folder);
            var leaf = Path.GetFileName(name);
            string title = "CMD: " + (string.IsNullOrWhiteSpace(leaf) ? folder : leaf);
            var tab = new TabPage(title)
            {
                Padding = Padding.Empty, ToolTipText = "Interactive Windows ConPTY terminal: " + folder
            };
            var shell = new EmbeddedShellControl(folder);
            shell.ApplyInterfaceFont(applicationFontPoints);
            tab.Tag = shell;
            tab.Controls.Add(shell);
            editors.TabPages.Add(tab);
            editors.SelectedTab = tab;
            shell.StartShell();
            shell.FocusCommand();
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Open shell failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseShellTab(TabPage tab)
    {
        if (tab.Tag is not EmbeddedShellControl) return;
        editors.TabPages.Remove(tab);
        tab.Dispose(); // Disposes the embedded shell and terminates cmd.exe.
        UpdateStatus();
    }

    private void CloseAllShellTabs()
    {
        foreach (var tab in editors.TabPages.Cast<TabPage>().ToArray())
            if (tab.Tag is EmbeddedShellControl) CloseShellTab(tab);
    }

    private void ShowShellContext(Point point, TabPage tab)
    {
        var menu = new ContextMenuStrip { Font = Font };
        menu.Items.Add("Close shell tab", null, (_, _) => CloseShellTab(tab));
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(editors, point);
    }
}
