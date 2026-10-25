using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SourceCodeEditor;

public sealed partial class MainForm
{
    private void EditorKeyDown(RichTextBox editor, KeyEventArgs e)
    {
        if (!autoIndent || e.KeyCode != Keys.Enter || e.Control || e.Alt || e.Shift) return;
        int line = editor.GetLineFromCharIndex(editor.SelectionStart);
        int begin = editor.GetFirstCharIndexFromLine(line);
        if (begin < 0) return;
        string prefix = editor.Text.Substring(begin, Math.Max(0, editor.SelectionStart - begin));
        string whitespace = new string(prefix.TakeWhile(c => c == ' ' || c == '\t').ToArray());
        editor.SelectedText = Environment.NewLine + whitespace;
        e.SuppressKeyPress = true;
    }

    private void DuplicateLine()
    {
        var e = CurrentDocument()?.View?.Editor; if (e == null) return;
        int line = e.GetLineFromCharIndex(e.SelectionStart);
        int start = e.GetFirstCharIndexFromLine(line);
        if (start < 0 || line >= e.Lines.Length) return;
        int end = line + 1 < e.Lines.Length ? e.GetFirstCharIndexFromLine(line + 1) : e.TextLength;
        string source = e.Text.Substring(start, Math.Max(0, end - start));
        if (!source.EndsWith("\n", StringComparison.Ordinal)) source = Environment.NewLine + source;
        e.Select(end, 0);
        e.SelectedText = source;
    }
    private void DeleteLine()
    {
        var e = CurrentDocument()?.View?.Editor; if (e == null) return;
        int line = e.GetLineFromCharIndex(e.SelectionStart);
        int start = e.GetFirstCharIndexFromLine(line);
        if (start < 0) return;
        int end = line + 1 < e.Lines.Length ? e.GetFirstCharIndexFromLine(line + 1) : e.TextLength;
        if (end < start) return;
        e.Select(start, end - start);
        e.SelectedText = "";
    }
    private void MoveLine(int direction)
    {
        var e = CurrentDocument()?.View?.Editor; if (e == null) return;
        int line = e.GetLineFromCharIndex(e.SelectionStart);
        var lines = e.Lines.ToArray();
        int target = line + direction;
        if (line < 0 || target < 0 || line >= lines.Length || target >= lines.Length) return;
        (lines[line], lines[target]) = (lines[target], lines[line]);
        int col = Math.Max(0, e.SelectionStart - e.GetFirstCharIndexFromLine(line));
        e.Text = string.Join(Environment.NewLine, lines);
        int newStart = e.GetFirstCharIndexFromLine(target);
        if (newStart >= 0) { e.SelectionStart = Math.Min(newStart + Math.Min(col, lines[target].Length), e.TextLength); e.ScrollToCaret(); }
    }
    private void IndentSelection(bool unindent)
    {
        var e = CurrentDocument()?.View?.Editor; if (e == null) return;
        int first = e.GetLineFromCharIndex(e.SelectionStart);
        int last = e.GetLineFromCharIndex(Math.Max(0, e.SelectionStart + e.SelectionLength - 1));
        if (e.SelectionLength == 0) last = first;
        // Replace backwards so offsets of earlier lines remain valid.
        for (int line = last; line >= first; line--)
        {
            int start = e.GetFirstCharIndexFromLine(line);
            if (start < 0) continue;
            if (unindent)
            {
                var text = e.Text;
                int remove = start < text.Length && text[start] == '\t' ? 1 :
                    text.Substring(start).TakeWhile(c => c == ' ').Take(4).Count();
                if (remove > 0) { e.Select(start, remove); e.SelectedText = ""; }
            }
            else { e.Select(start, 0); e.SelectedText = "    "; }
        }
    }
    private void ToggleComment()
    {
        var d = CurrentDocument(); var e = d?.View?.Editor; if (e == null) return;
        string ext = System.IO.Path.GetExtension(d!.Path ?? d.Name).ToLowerInvariant();
        string marker = ext is ".py" or ".rb" or ".sh" or ".ps1" or ".yaml" or ".yml" ? "#" :
            ext is ".sql" or ".lua" ? "--" : "//";
        int first = e.GetLineFromCharIndex(e.SelectionStart);
        int last = e.GetLineFromCharIndex(Math.Max(0, e.SelectionStart + e.SelectionLength - 1));
        if (e.SelectionLength == 0) last = first;
        for (int line = last; line >= first; line--)
        {
            int start = e.GetFirstCharIndexFromLine(line);
            if (start < 0) continue;
            int end = line + 1 < e.Lines.Length ? e.GetFirstCharIndexFromLine(line + 1) : e.TextLength;
            string content = e.Text.Substring(start, Math.Max(0, end - start));
            int indent = content.TakeWhile(c => c == ' ' || c == '\t').Count();
            int markerPos = start + indent;
            if (content.Substring(indent).StartsWith(marker, StringComparison.Ordinal))
            {
                e.Select(markerPos, marker.Length);
                e.SelectedText = "";
            }
            else { e.Select(markerPos, 0); e.SelectedText = marker; }
        }
    }
    private void TransformSelection(Func<string, string> transform)
    {
        var e = CurrentDocument()?.View?.Editor;
        if (e == null || e.SelectionLength == 0) return;
        e.SelectedText = transform(e.SelectedText);
    }
    private void TrimTrailingWhitespace()
    {
        var e = CurrentDocument()?.View?.Editor; if (e == null) return;
        string cleaned = Regex.Replace(e.Text, @"(?m)[ \t]+(?=\r?$)", "");
        if (cleaned == e.Text) return;
        int at = e.SelectionStart;
        e.Text = cleaned;
        e.SelectionStart = Math.Clamp(at, 0, e.TextLength);
    }
    private void SortSelection(bool reverse)
    {
        var e = CurrentDocument()?.View?.Editor;
        if (e == null || e.SelectionLength == 0) return;
        var lines = e.SelectedText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();
        bool trailingNewline = lines.Count > 1 && lines[^1].Length == 0;
        if (trailingNewline) lines.RemoveAt(lines.Count - 1);
        lines.Sort(StringComparer.OrdinalIgnoreCase);
        if (reverse) lines.Reverse();
        e.SelectedText = string.Join(Environment.NewLine, lines) + (trailingNewline ? Environment.NewLine : "");
    }
    private void GoToLine()
    {
        var e = CurrentDocument()?.View?.Editor; if (e == null) return;
        using var dlg = new Form
        {
            Text = "Go to Line", Width = 330, Height = 155, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        layout.Controls.Add(new Label { Text = $"Line (1 to {e.Lines.Length:N0})", AutoSize = true }, 0, 0);
        var field = new TextBox { Dock = DockStyle.Fill, Text = (e.GetLineFromCharIndex(e.SelectionStart) + 1).ToString() };
        layout.Controls.Add(field, 0, 1);
        var button = new Button { Text = "Go", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        layout.Controls.Add(button, 0, 2);
        dlg.Controls.Add(layout); dlg.AcceptButton = button;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (!int.TryParse(field.Text, out int line) || line < 1 || line > e.Lines.Length) return;
        int index = e.GetFirstCharIndexFromLine(line - 1);
        if (index >= 0) { e.Select(index, 0); e.ScrollToCaret(); e.Focus(); }
    }
    private void ToggleBookmark()
    {
        var d = CurrentDocument(); if (d?.View == null) return;
        int line = d.View.Editor.GetLineFromCharIndex(d.View.Editor.SelectionStart) + 1;
        if (!d.Bookmarks.Add(line)) d.Bookmarks.Remove(line);
        d.View.RefreshLineNumbers(); PersistSession();
    }
    private void NavigateBookmark(bool previous)
    {
        var d = CurrentDocument(); if (d?.View == null || d.Bookmarks.Count == 0) return;
        var e = d.View.Editor;
        int current = e.GetLineFromCharIndex(e.SelectionStart) + 1;
        var ordered = d.Bookmarks.OrderBy(x => x).ToArray();
        int next = previous ? ordered.LastOrDefault(x => x < current) : ordered.FirstOrDefault(x => x > current);
        if (next == 0) next = previous ? ordered[^1] : ordered[0];
        int pos = e.GetFirstCharIndexFromLine(next - 1);
        if (pos >= 0) { e.Select(pos, 0); e.ScrollToCaret(); e.Focus(); }
    }
    private void PrintCurrent()
    {
        var d = CurrentDocument(); if (d?.View == null) return;
        string content = d.View.Editor.Text;
        using var print = new PrintDocument { DocumentName = d.Name };
        using var font = new Font("Consolas", 9.5f);
        int start = 0;
        print.BeginPrint += (_, _) => start = 0;
        print.PrintPage += (_, e) =>
        {
            if (e.Graphics == null) return;
            using var format = new StringFormat { Trimming = StringTrimming.Word, FormatFlags = 0 };
            var region = e.MarginBounds;
            if (start >= content.Length) { e.HasMorePages = false; return; }
            string remaining = content.Substring(start);
            e.Graphics.MeasureString(remaining, font, region.Size, format, out int fitted, out _);
            if (fitted <= 0) fitted = Math.Min(remaining.Length, 100);
            e.Graphics.DrawString(remaining.Substring(0, fitted), font, Brushes.Black, region, format);
            start += fitted;
            e.HasMorePages = start < content.Length;
        };
        using var dialog = new PrintDialog { Document = print, UseEXDialog = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try { print.Print(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Print failed"); }
        }
    }
}
