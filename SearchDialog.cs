using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SourceCodeEditor;

internal sealed class SearchDialog : Form
{
    private readonly Func<RichTextBox?> activeEditor;
    private readonly TextBox find = new() { Dock = DockStyle.Fill };
    private readonly TextBox replace = new() { Dock = DockStyle.Fill };
    private readonly CheckBox regex = new() { Text = "Regular expression", AutoSize = true };
    private readonly CheckBox matchCase = new() { Text = "Match case", AutoSize = true };
    private readonly CheckBox wholeWord = new() { Text = "Whole word", AutoSize = true };
    private readonly CheckBox backwards = new() { Text = "Backward", AutoSize = true };
    private readonly CheckBox wrap = new() { Text = "Wrap around", Checked = true, AutoSize = true };
    private readonly ListView results = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        GridLines = true, HideSelection = false
    };
    private readonly Label feedback = new() { Text = "", AutoSize = true };

    public SearchDialog(Func<RichTextBox?> getActive)
    {
        activeEditor = getActive;
        Text = "Find and Replace";
        Width = 740; Height = 450;
        MinimumSize = new Size(550, 340);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.Controls.Add(new Label { Text = "Find:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(find, 1, 0);
        layout.Controls.Add(new Label { Text = "Replace:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(replace, 1, 1);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        options.Controls.AddRange(new Control[] { regex, matchCase, wholeWord, backwards, wrap });
        layout.Controls.Add(options, 1, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        AddButton(buttons, "Find Next", FindNext);
        AddButton(buttons, "Replace", ReplaceOne);
        AddButton(buttons, "Replace All", ReplaceAll);
        AddButton(buttons, "Find All", FindAll);
        layout.Controls.Add(buttons, 1, 3);
        results.Columns.Add("Line", 80);
        results.Columns.Add("Column", 80);
        results.Columns.Add("Preview", 520);
        results.DoubleClick += (_, _) =>
        {
            if (results.SelectedItems.Count == 0) return;
            if (results.SelectedItems[0].Tag is not ValueTuple<int, int> location) return;
            var editor = activeEditor(); if (editor == null) return;
            editor.Select(location.Item1, location.Item2); editor.ScrollToCaret(); editor.Focus();
        };
        layout.Controls.Add(results, 0, 4); layout.SetColumnSpan(results, 2);
        layout.Controls.Add(feedback, 0, 5); layout.SetColumnSpan(feedback, 2);
        Controls.Add(layout);
        find.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };
    }
    private static void AddButton(FlowLayoutPanel owner, string title, Action action)
    {
        var button = new Button { Text = title, AutoSize = true, Height = 28, Margin = new Padding(0, 0, 6, 0) };
        button.Click += (_, _) => action(); owner.Controls.Add(button);
    }
    private Regex? MakeRegex()
    {
        if (find.Text.Length == 0) return null;
        try
        {
            string pattern = regex.Checked ? find.Text : Regex.Escape(find.Text);
            if (wholeWord.Checked) pattern = @"\b(?:" + pattern + @")\b";
            var options = RegexOptions.Multiline | (matchCase.Checked ? RegexOptions.None : RegexOptions.IgnoreCase);
            return new Regex(pattern, options, TimeSpan.FromMilliseconds(750));
        }
        catch (ArgumentException ex)
        {
            feedback.Text = "Invalid expression: " + ex.Message; return null;
        }
    }
    private void FindNext()
    {
        var editor = activeEditor(); var pattern = MakeRegex();
        if (editor == null || pattern == null) return;
        try
        {
            Match? match;
            if (backwards.Checked)
            {
                int at = Math.Max(0, editor.SelectionStart - 1);
                match = pattern.Matches(editor.Text).Cast<Match>().LastOrDefault(m => m.Index <= at);
                if (match == null && wrap.Checked)
                    match = pattern.Matches(editor.Text).Cast<Match>().LastOrDefault();
            }
            else
            {
                int start = Math.Min(editor.TextLength, editor.SelectionStart + Math.Max(1, editor.SelectionLength));
                match = pattern.Match(editor.Text, start);
                if (!match.Success && wrap.Checked) match = pattern.Match(editor.Text);
            }
            if (match == null || !match.Success) { feedback.Text = "No match found"; return; }
            editor.Select(match.Index, match.Length);
            editor.ScrollToCaret(); editor.Focus();
            feedback.Text = $"Match at index {match.Index + 1}";
        }
        catch (RegexMatchTimeoutException) { feedback.Text = "The regular expression timed out"; }
    }
    private void ReplaceOne()
    {
        var editor = activeEditor(); var pattern = MakeRegex();
        if (editor == null || pattern == null) return;
        try
        {
            var selectedStart = editor.SelectionStart;
            var selectedLength = editor.SelectionLength;
            var match = pattern.Match(editor.Text, selectedStart);
            if (match.Success && match.Index == selectedStart && match.Length == selectedLength && selectedLength > 0)
            {
                string replacement = regex.Checked ? match.Result(replace.Text) : replace.Text;
                editor.SelectedText = replacement;
                editor.SelectionStart = Math.Min(editor.TextLength, selectedStart + replacement.Length);
                editor.SelectionLength = 0;
            }
            FindNext();
        }
        catch (RegexMatchTimeoutException) { feedback.Text = "Replace timed out"; }
        catch (ArgumentException ex) { feedback.Text = "Invalid replacement: " + ex.Message; }
    }
    private void ReplaceAll()
    {
        var editor = activeEditor(); var pattern = MakeRegex();
        if (editor == null || pattern == null) return;
        try
        {
            string source = editor.Text;
            int count = 0;
            string output = pattern.Replace(source, match =>
            {
                count++;
                return regex.Checked ? match.Result(replace.Text) : replace.Text;
            });
            if (output != source)
            {
                int caret = editor.SelectionStart;
                editor.Text = output;
                editor.SelectionStart = Math.Clamp(caret, 0, output.Length);
            }
            feedback.Text = $"Replaced {count:N0} occurrence(s)";
        }
        catch (RegexMatchTimeoutException) { feedback.Text = "Replace timed out"; }
        catch (ArgumentException ex) { feedback.Text = "Invalid replacement: " + ex.Message; }
    }
    private void FindAll()
    {
        var editor = activeEditor(); var pattern = MakeRegex();
        if (editor == null || pattern == null) return;
        try
        {
            results.BeginUpdate();
            results.Items.Clear();
            int total = 0;
            foreach (Match match in pattern.Matches(editor.Text))
            {
                if (!match.Success) continue;
                total++;
                if (total > 10000) break;
                int line = editor.GetLineFromCharIndex(match.Index);
                int lineStart = editor.GetFirstCharIndexFromLine(line);
                int col = match.Index - Math.Max(0, lineStart) + 1;
                string preview = line >= 0 && line < editor.Lines.Length ? editor.Lines[line].Trim() : "";
                if (preview.Length > 180) preview = preview.Substring(0, 180);
                var item = new ListViewItem(new[] { (line + 1).ToString(), col.ToString(), preview })
                {
                    Tag = (match.Index, match.Length)
                };
                results.Items.Add(item);
            }
            results.EndUpdate();
            feedback.Text = total > 10000 ? "Showing the first 10,000 matches" : $"{total:N0} match(es)";
        }
        catch (RegexMatchTimeoutException) { results.EndUpdate(); feedback.Text = "Find All timed out"; }
    }
}
