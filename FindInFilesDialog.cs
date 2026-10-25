using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SourceCodeEditor;

internal sealed class FindInFilesDialog : Form
{
    private readonly Action<string, int> openResult;
    private readonly TextBox pattern = new() { Dock = DockStyle.Fill };
    private readonly TextBox directory = new() { Dock = DockStyle.Fill };
    private readonly TextBox masks = new() { Dock = DockStyle.Fill, Text = "*.cs;*.cpp;*.c;*.h;*.hpp;*.py;*.js;*.ts;*.json;*.xml;*.txt;*.md;*.cmot;*.hmot" };
    private readonly CheckBox recurse = new() { Text = "Include subfolders", AutoSize = true, Checked = true };
    private readonly CheckBox useRegex = new() { Text = "Regular expression", AutoSize = true };
    private readonly CheckBox matchCase = new() { Text = "Match case", AutoSize = true };
    private readonly ListView results = new() { View = View.Details, Dock = DockStyle.Fill, FullRowSelect = true, GridLines = true };
    private readonly Label progress = new() { AutoSize = true, Text = "Enter a search expression and choose Search." };
    private readonly Button start = new() { Text = "Search", Width = 92 };
    private readonly Button stop = new() { Text = "Cancel", Width = 92, Enabled = false };
    private CancellationTokenSource? cancellation;
    private TableLayoutPanel? fontLayout;

    public FindInFilesDialog(string initialDirectory, Action<string, int> onResult)
    {
        openResult = onResult;
        Text = "Find in Files"; Width = 1060; Height = 650;
        MinimumSize = new Size(650, 430); StartPosition = FormStartPosition.CenterParent;
        directory.Text = initialDirectory;
        Font = new Font("Segoe UI", 12f);
        AutoScaleMode = AutoScaleMode.Font;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(9) };
        fontLayout = panel;
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 29, 29, 29, 35, 37 })
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = "Find", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        panel.Controls.Add(pattern, 1, 0);
        panel.Controls.Add(new Label { Text = "Folder", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 83));
        var browse = new Button { Text = "Browse…", Dock = DockStyle.Fill };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(directory.Text) ? directory.Text : "" };
            if (dialog.ShowDialog(this) == DialogResult.OK) directory.Text = dialog.SelectedPath;
        };
        folderRow.Controls.Add(directory, 0, 0); folderRow.Controls.Add(browse, 1, 0);
        panel.Controls.Add(folderRow, 1, 1);
        panel.Controls.Add(new Label { Text = "File masks", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        panel.Controls.Add(masks, 1, 2);
        var flags = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        flags.Controls.AddRange(new Control[] { recurse, useRegex, matchCase });
        panel.Controls.Add(flags, 1, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        buttons.Controls.Add(start); buttons.Controls.Add(stop); buttons.Controls.Add(progress);
        start.Click += async (_, _) => await RunSearchAsync();
        stop.Click += (_, _) => cancellation?.Cancel();
        panel.Controls.Add(buttons, 1, 4);
        results.Columns.Add("File", 380);
        results.Columns.Add("Line", 68);
        results.Columns.Add("Preview", 550);
        results.DoubleClick += (_, _) =>
        {
            if (results.SelectedItems.Count == 0) return;
            if (results.SelectedItems[0].Tag is FileHit hit) openResult(hit.Path, hit.Line);
        };
        panel.Controls.Add(results, 0, 5); panel.SetColumnSpan(results, 2);
        Controls.Add(panel);
        FormClosing += (_, _) => cancellation?.Cancel();
    }

    public void SetInterfaceFont(Font uiFont)
    {
        Font = uiFont;
        if (fontLayout != null)
        {
            fontLayout.ColumnStyles[0].Width = Math.Max(98, uiFont.Height * 5);
            int row = Math.Max(29, uiFont.Height + 11);
            for (int i = 0; i < 3; i++) fontLayout.RowStyles[i].Height = row;
            fontLayout.RowStyles[3].Height = Math.Max(35, uiFont.Height + 13);
            fontLayout.RowStyles[4].Height = Math.Max(37, uiFont.Height + 15);
            fontLayout.PerformLayout();
        }
    }

    private sealed record FileHit(string Path, int Line, string Preview);

    private async Task RunSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(pattern.Text) || !Directory.Exists(directory.Text))
        {
            MessageBox.Show(this, "Specify a non-empty expression and a valid folder.", "Find in Files");
            return;
        }
        Regex regex;
        try
        {
            regex = new Regex(useRegex.Checked ? pattern.Text : Regex.Escape(pattern.Text),
                RegexOptions.Multiline | (matchCase.Checked ? RegexOptions.None : RegexOptions.IgnoreCase),
                TimeSpan.FromMilliseconds(500));
        }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Invalid expression"); return; }
        var filter = masks.Text.Split(new[] { ';', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (filter.Length == 0) filter = new[] { "*" };
        string folder = directory.Text;
        bool recursive = recurse.Checked;
        using var tokenSource = new CancellationTokenSource();
        cancellation = tokenSource;
        start.Enabled = false; stop.Enabled = true;
        progress.Text = "Searching...";
        results.Items.Clear();
        try
        {
            var hits = await Task.Run(() => SearchFiles(folder, regex, filter, recursive, tokenSource.Token), tokenSource.Token);
            results.BeginUpdate();
            try
            {
                foreach (var hit in hits)
                {
                    var item = new ListViewItem(new[] { hit.Path, hit.Line.ToString(), hit.Preview }) { Tag = hit };
                    results.Items.Add(item);
                }
            }
            finally { results.EndUpdate(); }
            progress.Text = hits.Count >= 5000 ? "Showing the first 5,000 matches" : $"{hits.Count:N0} match(es)";
        }
        catch (OperationCanceledException) { progress.Text = "Search cancelled"; }
        catch (Exception ex) { progress.Text = "Search failed: " + ex.Message; }
        finally { cancellation = null; start.Enabled = true; stop.Enabled = false; }
    }

    private static List<FileHit> SearchFiles(string folder, Regex expression, string[] masks, bool recursive, CancellationToken token)
    {
        var hits = new List<FileHit>();
        var pending = new Stack<string>(); pending.Push(folder);
        while (pending.Count > 0 && hits.Count < 5000)
        {
            token.ThrowIfCancellationRequested();
            string dir = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                if (!masks.Any(mask => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(mask, Path.GetFileName(file), true))) continue;
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length > 12 * 1024 * 1024) continue; // avoid loading binary or very large files
                    var text = TextFiles.Read(file).text;
                    int line = 1;
                    int next = 0;
                    foreach (Match match in expression.Matches(text))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!match.Success) continue;
                        while (next < match.Index)
                        {
                            if (text[next] == '\n') line++;
                            next++;
                        }
                        int lineStart = text.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
                        int end = text.IndexOf('\n', match.Index);
                        if (end < 0) end = text.Length;
                        string preview = text.Substring(lineStart, Math.Min(end - lineStart, 220)).Trim();
                        hits.Add(new FileHit(file, line, preview));
                        if (hits.Count >= 5000) return hits;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException or RegexMatchTimeoutException)
                {
                    // Ignore unreadable, binary, and pathological input files.
                }
            }
            if (!recursive) continue;
            string[] children;
            try { children = Directory.GetDirectories(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return hits;
    }
}
