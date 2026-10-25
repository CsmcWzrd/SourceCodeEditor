using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SourceCodeEditor;

public sealed partial class MainForm
{
    private const float DefaultFontPoints = 12f;
    private const float MinimumFontPoints = 8f;
    private const float MaximumFontPoints = 32f;
    private float applicationFontPoints = DefaultFontPoints;
    private float editorFontPoints = DefaultFontPoints;
    private string applicationFontFamily = "Segoe UI";
    private string editorFontFamily = "Consolas";
    private ToolStripMenuItem? applicationMonospacedFonts;
    private ToolStripMenuItem? editorMonospacedFonts;
    private bool fontChoicesLoaded;
    private ToolStripMenuItem? applicationFontMenu;
    private ToolStripMenuItem? editorFontMenu;
    private TableLayoutPanel? explorerLayout;
    private ToolStrip? explorerToolbar;

    private static float NormalizeFontPoints(float points) =>
        Math.Clamp(float.IsFinite(points) ? points : DefaultFontPoints, MinimumFontPoints, MaximumFontPoints);

    // Application font and editor font are deliberately INDEPENDENT. The former
    // affects navigation, menus, tabs, status bar, and shell interface. The latter
    // adjusts the RichEdit base font of all open documents, retaining individual
    // document zoom factors and RTF syntax token colors.
    private void SetApplicationFontSize(float points, bool persist = true)
    {
        points = NormalizeFontPoints(points);
        applicationFontPoints = points;
        var uiFont = new Font(applicationFontFamily, points, FontStyle.Regular, GraphicsUnit.Point);
        SuspendLayout();
        try
        {
            Font = uiFont;
            navigation.Font = uiFont;
            // Four tabs in a predictable two-by-two layout at the default font.
            navigation.ItemSize = new Size(116, Math.Max(30, uiFont.Height + 10));
            editors.Font = uiFont;
            editors.Padding = new Point(Math.Max(20, uiFont.Height + 2), Math.Max(7, uiFont.Height / 4));
            if (MainMenuStrip != null) MainMenuStrip.Font = uiFont;
            explorerMenu.Font = uiFont;
            documentContextMenu.Font = uiFont;
            shellContextMenu.Font = uiFont;
            if (explorerToolbar != null) explorerToolbar.Font = uiFont;
            explorerAddress.Font = uiFont;
            foreach (TreeView tree in new[] { explorer, opened, closed, unsaved })
            {
                tree.Font = uiFont;
                tree.ItemHeight = Math.Max(22, uiFont.Height + 5);
            }
            if (explorerLayout != null)
            {
                // Explorer toolbar and path entry must not clip when font grows.
                int height = Math.Max(27, uiFont.Height + 12);
                explorerLayout.RowStyles[0].Height = height;
                explorerLayout.RowStyles[1].Height = height;
                explorerLayout.PerformLayout();
            }
            foreach (TabPage page in editors.TabPages)
                if (page.Tag is EmbeddedShellControl shell) shell.ApplyInterfaceFont(points);
            foreach (Form child in OwnedForms)
            {
                if (child is SearchDialog search) search.SetInterfaceFont(uiFont);
                else if (child is FindInFilesDialog findFiles) findFiles.SetInterfaceFont(uiFont);
            }
            if (applicationFontMenu != null) applicationFontMenu.Text = $"Application Font ({points:0} pt)";
            navigation.Invalidate(true);
            editors.Invalidate(true);
            MainMenuStrip?.PerformLayout();
        }
        finally { ResumeLayout(true); }
        if (persist) PersistSession();
    }

    private void SetEditorFontSize(float points, bool persist = true)
    {
        points = NormalizeFontPoints(points);
        editorFontPoints = points;
        bool oldIgnore = ignoreTextChanged;
        ignoreTextChanged = true;
        try
        {
            foreach (var doc in documents.Where(d => d.View != null))
                doc.View!.SetBaseFont(editorFontFamily, points);
        }
        finally { ignoreTextChanged = oldIgnore; }
        if (editorFontMenu != null) editorFontMenu.Text = $"RTF Editor Font ({points:0} pt)";
        UpdateStatus();
        if (persist) PersistSession();
    }

    private void SetApplicationFontFamily(string familyName)
    {
        // Only families from the installed monospaced-font list may be chosen.
        if (!MonospacedFontCatalog.ListInstalled().Contains(familyName, StringComparer.OrdinalIgnoreCase)) return;
        applicationFontFamily = familyName;
        SetApplicationFontSize(applicationFontPoints);
        UpdateFontChoiceChecks();
    }

    private void SetEditorFontFamily(string familyName)
    {
        if (!MonospacedFontCatalog.ListInstalled().Contains(familyName, StringComparer.OrdinalIgnoreCase)) return;
        editorFontFamily = familyName;
        SetEditorFontSize(editorFontPoints);
        UpdateFontChoiceChecks();
    }

    // Both submenus display the complete installed fixed-width font list.
    // Items are created once, not while WinForms dispatches a click, to avoid
    // the ContextMenuStrip disposal bug fixed in v0.5.3.
    private void EnsureFontChoices()
    {
        if (fontChoicesLoaded) return;
        var available = MonospacedFontCatalog.ListInstalled();
        foreach (var menu in new[] { applicationMonospacedFonts, editorMonospacedFonts })
        {
            if (menu == null) continue;
            if (available.Count == 0)
            {
                menu.DropDownItems.Add(new ToolStripMenuItem("No installed monospaced fonts detected") { Enabled = false });
                continue;
            }
            foreach (string name in available)
            {
                string selected = name;
                var item = new ToolStripMenuItem(name) { Tag = name };
                item.Click += (_, _) =>
                {
                    if (ReferenceEquals(menu, applicationMonospacedFonts)) SetApplicationFontFamily(selected);
                    else SetEditorFontFamily(selected);
                };
                menu.DropDownItems.Add(item);
            }
        }
        fontChoicesLoaded = true;
        UpdateFontChoiceChecks();
    }

    private void UpdateFontChoiceChecks()
    {
        foreach (var (menu, selected) in new[]
        {
            (applicationMonospacedFonts, applicationFontFamily),
            (editorMonospacedFonts, editorFontFamily)
        })
        {
            if (menu == null) continue;
            foreach (ToolStripItem item in menu.DropDownItems)
                if (item is ToolStripMenuItem option && option.Tag is string name)
                    option.Checked = string.Equals(name, selected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
