using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace SourceCodeEditor;

/// <summary>
/// Bounded, VT100/xterm-compatible text grid for the ConPTY output stream.
/// Supports cursor movement, erase, SGR 16/256/RGB colors, alternate screen,
/// scrollback, CR/LF, and the escape sequences emitted by cmd, PowerShell,
/// Git, and common command-line tools. No dependency on an external terminal.
/// </summary>
internal sealed class TerminalScreen
{
    internal readonly record struct Cell(char Char, Color Foreground, Color Background);
    internal sealed class Line
    {
        internal Cell[] Cells;
        internal Line(int columns, Cell blank)
        {
            Cells = Enumerable.Repeat(blank, columns).ToArray();
        }
        internal void Resize(int columns, Cell blank)
        {
            if (Cells.Length == columns) return;
            int old = Cells.Length;
            Array.Resize(ref Cells, columns);
            if (columns > old) Array.Fill(Cells, blank, old, columns - old);
        }
    }

    private static readonly Color[] StandardColors =
    {
        Color.FromArgb(12, 12, 12), Color.FromArgb(197, 15, 31),
        Color.FromArgb(19, 161, 14), Color.FromArgb(193, 156, 0),
        Color.FromArgb(0, 55, 218), Color.FromArgb(136, 23, 152),
        Color.FromArgb(58, 150, 221), Color.FromArgb(204, 204, 204),
        Color.FromArgb(118, 118, 118), Color.FromArgb(231, 72, 86),
        Color.FromArgb(22, 198, 12), Color.FromArgb(249, 241, 165),
        Color.FromArgb(59, 120, 255), Color.FromArgb(180, 0, 158),
        Color.FromArgb(97, 214, 214), Color.FromArgb(242, 242, 242)
    };

    internal static readonly Color DefaultForeground = Color.FromArgb(222, 228, 235);
    internal static readonly Color DefaultBackground = Color.FromArgb(15, 18, 24);
    private enum ParseState { Text, Escape, Csi, Osc, OscEscape, Charset }
    private ParseState state;
    private readonly StringBuilder code = new();
    private List<Line> display = new();
    private readonly List<Line> history = new();
    private List<Line>? previousDisplay;
    private int previousRow, previousColumn;
    private Color fg = DefaultForeground, bg = DefaultBackground;
    private bool bold, inverse, alternateScreen, pendingWrap, showCursor = true;
    private int savedRow, savedColumn, row, column;
    private int topMargin, bottomMargin;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int ScrollOffset { get; private set; }
    public int CursorRow => row;
    public int CursorColumn => column;
    public bool ShowCursor => showCursor;
    public int HistoryCount => history.Count;

    private Cell Blank => new(' ', fg, bg);
    public TerminalScreen(int columns, int rows)
    {
        Columns = Math.Max(20, columns);
        Rows = Math.Max(4, rows);
        bottomMargin = Rows - 1;
        for (int i = 0; i < Rows; i++) display.Add(new Line(Columns, Blank));
    }

    public void Resize(int columns, int rows)
    {
        columns = Math.Max(20, columns);
        rows = Math.Max(4, rows);
        if (columns == Columns && rows == Rows) return;
        Columns = columns;
        foreach (var line in display) line.Resize(columns, Blank);
        foreach (var line in history) line.Resize(columns, Blank);
        if (previousDisplay != null) foreach (var line in previousDisplay) line.Resize(columns, Blank);
        if (rows > Rows)
            for (int i = Rows; i < rows; i++) display.Add(new Line(columns, Blank));
        else if (rows < Rows)
        {
            int count = Rows - rows;
            for (int i = 0; i < count; i++)
            {
                if (!alternateScreen) AppendHistory(display[0]);
                display.RemoveAt(0);
            }
            row = Math.Max(0, row - count);
        }
        Rows = rows;
        row = Math.Clamp(row, 0, Rows - 1);
        column = Math.Clamp(column, 0, Columns - 1);
        topMargin = 0;
        bottomMargin = Rows - 1;
        ScrollOffset = Math.Min(ScrollOffset, history.Count);
    }

    public Line GetRenderedLine(int displayRow)
    {
        int index = history.Count - ScrollOffset + displayRow;
        return index < history.Count && index >= 0 ? history[index]
            : display[Math.Clamp(index - history.Count, 0, Rows - 1)];
    }

    public int GetAbsoluteRow(int displayedRow) => history.Count - ScrollOffset + displayedRow;

    public string ReadSelection(int startAbsoluteRow, int startColumn, int endAbsoluteRow, int endColumn)
    {
        if (startAbsoluteRow > endAbsoluteRow ||
            (startAbsoluteRow == endAbsoluteRow && startColumn > endColumn))
            (startAbsoluteRow, endAbsoluteRow, startColumn, endColumn) =
                (endAbsoluteRow, startAbsoluteRow, endColumn, startColumn);
        var text = new StringBuilder();
        for (int i = Math.Max(0, startAbsoluteRow); i <= Math.Min(history.Count + Rows - 1, endAbsoluteRow); i++)
        {
            var line = i < history.Count ? history[i] : display[i - history.Count];
            int from = Math.Clamp(i == startAbsoluteRow ? startColumn : 0, 0, Columns);
            int to = Math.Clamp(i == endAbsoluteRow ? endColumn + 1 : Columns, from, Columns);
            var part = new string(line.Cells.Skip(from).Take(to - from).Select(c => c.Char).ToArray()).TrimEnd();
            text.Append(part);
            if (i != endAbsoluteRow) text.AppendLine();
        }
        return text.ToString();
    }

    public string ReadVisible() => ReadSelection(GetAbsoluteRow(0), 0, GetAbsoluteRow(Rows - 1), Columns - 1);

    public void Scroll(int lines) => ScrollOffset = Math.Clamp(ScrollOffset + lines, 0, history.Count);
    public void ScrollToBottom() => ScrollOffset = 0;

    public void ClearScreen()
    {
        history.Clear(); display.Clear();
        for (int i = 0; i < Rows; i++) display.Add(new Line(Columns, Blank));
        row = 0; column = 0; ScrollOffset = 0; pendingWrap = false;
    }

    public void Write(string text)
    {
        foreach (char ch in text)
        {
            switch (state)
            {
                case ParseState.Text:
                    switch (ch)
                    {
                        case '\u001b': state = ParseState.Escape; break;
                        case '\r': column = 0; pendingWrap = false; break;
                        case '\n': NewLine(); break;
                        case '\b': column = Math.Max(0, column - 1); pendingWrap = false; break;
                        case '\t':
                            int count = 8 - column % 8;
                            for (int i = 0; i < count; i++) Print(' ');
                            break;
                        case '\a': break;
                        default:
                            if (ch >= ' ' && ch != '\u007f') Print(ch);
                            break;
                    }
                    break;
                case ParseState.Escape:
                    state = ParseState.Text;
                    if (ch == '[') { code.Clear(); state = ParseState.Csi; }
                    else if (ch == ']') { code.Clear(); state = ParseState.Osc; }
                    else if (ch is '(' or ')' or '*' or '+') state = ParseState.Charset;
                    else if (ch == '7') { savedRow = row; savedColumn = column; }
                    else if (ch == '8') { row = savedRow; column = savedColumn; }
                    else if (ch == 'D') NewLine();
                    else if (ch == 'E') { column = 0; NewLine(); }
                    else if (ch == 'M') ReverseIndex();
                    else if (ch == 'c') Reset();
                    break;
                case ParseState.Csi:
                    if (ch is >= '@' and <= '~')
                    {
                        HandleCsi(ch, code.ToString());
                        code.Clear(); state = ParseState.Text;
                    }
                    else if (code.Length < 256) code.Append(ch);
                    else { code.Clear(); state = ParseState.Text; }
                    break;
                case ParseState.Osc:
                    if (ch == '\a') state = ParseState.Text;
                    else if (ch == '\u001b') state = ParseState.OscEscape;
                    break;
                case ParseState.OscEscape:
                    state = ch == '\\' ? ParseState.Text : ParseState.Osc;
                    break;
                case ParseState.Charset:
                    state = ParseState.Text;
                    break;
            }
        }
        if (!alternateScreen && ScrollOffset == 0) ScrollToBottom();
    }

    private void Print(char ch)
    {
        if (pendingWrap)
        {
            column = 0;
            NewLine();
        }
        if (column < Columns && row < Rows)
            display[row].Cells[column] = new Cell(ch, inverse ? bg : fg, inverse ? fg : bg);
        if (column == Columns - 1) pendingWrap = true;
        else column++;
    }

    private void NewLine()
    {
        pendingWrap = false;
        if (row == bottomMargin) ScrollRegionUp(topMargin, bottomMargin);
        else row = Math.Min(Rows - 1, row + 1);
    }

    private void ReverseIndex()
    {
        if (row == topMargin)
        {
            display.RemoveAt(bottomMargin);
            display.Insert(topMargin, new Line(Columns, Blank));
        }
        else row = Math.Max(0, row - 1);
    }

    private void AppendHistory(Line line)
    {
        history.Add(line);
        if (history.Count > 5000) { history.RemoveAt(0); }
        if (ScrollOffset > 0) ScrollOffset = Math.Min(history.Count, ScrollOffset + 1);
    }

    private void ScrollRegionUp(int top, int bottom)
    {
        var removed = display[top];
        display.RemoveAt(top);
        display.Insert(bottom, new Line(Columns, Blank));
        if (top == 0 && bottom == Rows - 1 && !alternateScreen) AppendHistory(removed);
    }

    private void ScrollRegionDown(int top, int bottom)
    {
        display.RemoveAt(bottom);
        display.Insert(top, new Line(Columns, Blank));
    }

    private static int[] Args(string parameter)
    {
        parameter = parameter.TrimStart('?', '>', '!');
        return parameter.Split(';').Select(p => int.TryParse(p, out int x) ? x : 0).ToArray();
    }
    private static int Arg(int[] args, int index, int fallback = 1) =>
        index >= args.Length || args[index] == 0 ? fallback : args[index];

    private void HandleCsi(char suffix, string parameter)
    {
        var p = Args(parameter);
        bool dec = parameter.StartsWith('?');
        pendingWrap = false;
        switch (suffix)
        {
            case 'A': row = Math.Max(0, row - Arg(p, 0)); break;
            case 'B': row = Math.Min(Rows - 1, row + Arg(p, 0)); break;
            case 'C': column = Math.Min(Columns - 1, column + Arg(p, 0)); break;
            case 'D': column = Math.Max(0, column - Arg(p, 0)); break;
            case 'E': row = Math.Min(Rows - 1, row + Arg(p, 0)); column = 0; break;
            case 'F': row = Math.Max(0, row - Arg(p, 0)); column = 0; break;
            case 'G': column = Math.Clamp(Arg(p, 0) - 1, 0, Columns - 1); break;
            case 'd': row = Math.Clamp(Arg(p, 0) - 1, 0, Rows - 1); break;
            case 'H':
            case 'f':
                row = Math.Clamp(Arg(p, 0) - 1, 0, Rows - 1);
                column = Math.Clamp(Arg(p, 1) - 1, 0, Columns - 1);
                break;
            case 's': savedRow = row; savedColumn = column; break;
            case 'u': row = savedRow; column = savedColumn; break;
            case 'm': SetGraphics(p); break;
            case 'J':
                if (p[0] == 3) { history.Clear(); ScrollOffset = 0; }
                else if (p[0] == 2) for (int r = 0; r < Rows; r++) Erase(r, 0, Columns);
                else if (p[0] == 1)
                {
                    for (int r = 0; r < row; r++) Erase(r, 0, Columns);
                    Erase(row, 0, column + 1);
                }
                else
                {
                    Erase(row, column, Columns);
                    for (int r = row + 1; r < Rows; r++) Erase(r, 0, Columns);
                }
                break;
            case 'K':
                if (p[0] == 2) Erase(row, 0, Columns);
                else if (p[0] == 1) Erase(row, 0, column + 1);
                else Erase(row, column, Columns);
                break;
            case 'X': Erase(row, column, Math.Min(Columns, column + Arg(p, 0))); break;
            case 'P':
                int n = Math.Min(Columns - column, Arg(p, 0));
                Array.Copy(display[row].Cells, column + n, display[row].Cells, column, Columns - column - n);
                Erase(row, Columns - n, Columns);
                break;
            case '@':
                int c = Math.Min(Columns - column, Arg(p, 0));
                Array.Copy(display[row].Cells, column, display[row].Cells, column + c, Columns - column - c);
                Erase(row, column, column + c);
                break;
            case 'L':
                for (int i = 0; i < Math.Min(Arg(p, 0), Rows); i++)
                    if (row >= topMargin && row <= bottomMargin) ScrollRegionDown(row, bottomMargin);
                break;
            case 'M':
                for (int i = 0; i < Math.Min(Arg(p, 0), Rows); i++)
                    if (row >= topMargin && row <= bottomMargin) ScrollRegionUp(row, bottomMargin);
                break;
            case 'S': for (int i = 0; i < Math.Min(Arg(p, 0), Rows); i++) ScrollRegionUp(topMargin, bottomMargin); break;
            case 'T': for (int i = 0; i < Math.Min(Arg(p, 0), Rows); i++) ScrollRegionDown(topMargin, bottomMargin); break;
            case 'r':
                topMargin = Math.Clamp(Arg(p, 0) - 1, 0, Rows - 1);
                bottomMargin = Math.Clamp(Arg(p, 1, Rows) - 1, topMargin, Rows - 1);
                row = topMargin; column = 0;
                break;
            case 'h':
            case 'l':
                if (dec)
                {
                    bool set = suffix == 'h';
                    foreach (int arg in p)
                    {
                        if (arg == 25) showCursor = set;
                        if (arg is 1049 or 1047 or 47) SwitchAlternate(set);
                    }
                }
                break;
        }
    }

    private void Erase(int r, int start, int end)
    {
        start = Math.Clamp(start, 0, Columns);
        end = Math.Clamp(end, start, Columns);
        Array.Fill(display[r].Cells, Blank, start, end - start);
    }

    private void SetGraphics(int[] parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            int p = parameters[i];
            if (p == 0) { fg = DefaultForeground; bg = DefaultBackground; bold = inverse = false; }
            else if (p == 1) bold = true;
            else if (p == 22) bold = false;
            else if (p == 7) inverse = true;
            else if (p == 27) inverse = false;
            else if (p == 39) fg = DefaultForeground;
            else if (p == 49) bg = DefaultBackground;
            else if (p is >= 30 and <= 37) fg = StandardColors[(p - 30) + (bold ? 8 : 0)];
            else if (p is >= 40 and <= 47) bg = StandardColors[p - 40];
            else if (p is >= 90 and <= 97) fg = StandardColors[p - 90 + 8];
            else if (p is >= 100 and <= 107) bg = StandardColors[p - 100 + 8];
            else if (p is 38 or 48)
            {
                bool foreground = p == 38;
                Color color;
                if (i + 2 < parameters.Length && parameters[i + 1] == 5)
                {
                    color = Color256(parameters[i + 2]); i += 2;
                }
                else if (i + 4 < parameters.Length && parameters[i + 1] == 2)
                {
                    color = Color.FromArgb(
                        Math.Clamp(parameters[i + 2], 0, 255),
                        Math.Clamp(parameters[i + 3], 0, 255),
                        Math.Clamp(parameters[i + 4], 0, 255));
                    i += 4;
                }
                else continue;
                if (foreground) fg = color; else bg = color;
            }
        }
    }

    private static Color Color256(int n)
    {
        if (n < 0) return DefaultForeground;
        if (n < 16) return StandardColors[n];
        if (n < 232)
        {
            int value = n - 16;
            static int Six(int x) => x == 0 ? 0 : x * 40 + 55;
            return Color.FromArgb(Six(value / 36), Six((value / 6) % 6), Six(value % 6));
        }
        return Color.FromArgb(Math.Clamp(8 + (n - 232) * 10, 0, 255),
            Math.Clamp(8 + (n - 232) * 10, 0, 255), Math.Clamp(8 + (n - 232) * 10, 0, 255));
    }

    private void SwitchAlternate(bool enable)
    {
        if (enable == alternateScreen) return;
        alternateScreen = enable;
        if (enable)
        {
            previousDisplay = display;
            previousRow = row; previousColumn = column;
            display = new List<Line>();
            for (int i = 0; i < Rows; i++) display.Add(new Line(Columns, Blank));
            row = 0; column = 0; ScrollOffset = 0;
        }
        else if (previousDisplay != null)
        {
            display = previousDisplay;
            previousDisplay = null;
            row = Math.Clamp(previousRow, 0, Rows - 1);
            column = Math.Clamp(previousColumn, 0, Columns - 1);
        }
    }

    private void Reset()
    {
        fg = DefaultForeground; bg = DefaultBackground;
        bold = inverse = pendingWrap = false;
        showCursor = true; topMargin = 0; bottomMargin = Rows - 1;
        SwitchAlternate(false);
        ClearScreen();
    }
}
