using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SourceCodeEditor;

/// <summary>
/// Interactive, integrated ConPTY terminal. Unlike the former implementation,
/// there is NO separate command-entry box or output-only RichTextBox. The cursor,
/// prompt, keyboard echo, colors and screen updates come from a real console.
/// </summary>
public sealed class EmbeddedShellControl : UserControl
{
    private sealed class TerminalSurface : Control
    {
        private readonly TerminalScreen screen = new(80, 24);
        private int cellWidth = 9, cellHeight = 19;
        private float fontPoints = 12f, zoomFactor = 1f;
        private bool selecting;
        private (int row, int col)? startSelection, endSelection;
        private bool cursorVisible = true;
        private readonly System.Windows.Forms.Timer cursorTimer = new() { Interval = 550 };
        internal event Action<string>? SendInput;
        internal event Action<int, int>? TerminalSizeChanged;
        internal int Columns => screen.Columns;
        internal int Rows => screen.Rows;
        internal bool ShowCaret { get; set; } = true;

        public TerminalSurface()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Dock = DockStyle.Fill;
            TabStop = true;
            BackColor = TerminalScreen.DefaultBackground;
            ForeColor = TerminalScreen.DefaultForeground;
            Font = new Font("Consolas", 12f, FontStyle.Regular, GraphicsUnit.Point);
            MeasureCells();
            cursorTimer.Tick += (_, _) =>
            {
                cursorVisible = !cursorVisible;
                if (Focused && screen.ScrollOffset == 0 && ShowCaret) Invalidate();
            };
            cursorTimer.Start();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy selection", null, (_, _) => CopySelection());
            menu.Items.Add("Paste", null, (_, _) => PasteClipboard());
            menu.Items.Add("Copy visible screen", null, (_, _) => CopyVisible());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Clear scrollback and screen", null, (_, _) =>
            {
                // 'cls' is issued to the actual shell; it can redraw its prompt.
                SendInput?.Invoke("cls\r");
                screen.ClearScreen(); Invalidate();
            });
            ContextMenuStrip = menu;
        }

        public void SetBaseFont(float points)
        {
            fontPoints = Math.Clamp(points, 8f, 32f);
            UpdateFont();
        }

        private void UpdateFont()
        {
            var size = Math.Clamp(fontPoints * zoomFactor, 7f, 48f);
            var old = Font;
            Font = new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
            old.Dispose();
            MeasureCells();
            RecalculateGrid();
            Invalidate();
        }

        private void MeasureCells()
        {
            using var g = CreateGraphics();
            var measured = TextRenderer.MeasureText(g, "W", Font,
                Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            cellWidth = Math.Max(5, measured.Width);
            cellHeight = Math.Max(9, Font.Height + 2);
        }

        internal void Output(string text)
        {
            screen.Write(text);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecalculateGrid();
        }

        private void RecalculateGrid()
        {
            if (cellWidth <= 0 || cellHeight <= 0) return;
            int columns = Math.Clamp(ClientSize.Width / cellWidth, 20, 300);
            int rows = Math.Clamp(ClientSize.Height / cellHeight, 4, 150);
            if (screen.Columns == columns && screen.Rows == rows) return;
            screen.Resize(columns, rows);
            TerminalSizeChanged?.Invoke(columns, rows);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;
            for (int r = 0; r < screen.Rows; r++)
            {
                var cells = screen.GetRenderedLine(r).Cells;
                int y = r * cellHeight;
                int col = 0;
                while (col < cells.Length)
                {
                    int begin = col;
                    var fg = cells[col].Foreground;
                    var bg = cells[col].Background;
                    while (col < cells.Length && cells[col].Foreground == fg && cells[col].Background == bg)
                        col++;
                    int width = (col - begin) * cellWidth;
                    if (bg != BackColor)
                    {
                        using var brush = new SolidBrush(bg);
                        graphics.FillRectangle(brush, begin * cellWidth, y, width, cellHeight);
                    }
                    var characters = new char[col - begin];
                    for (int i = 0; i < characters.Length; i++) characters[i] = cells[begin + i].Char;
                    string text = new string(characters);
                    if (!string.IsNullOrWhiteSpace(text))
                        TextRenderer.DrawText(graphics, text, Font,
                            new Rectangle(begin * cellWidth, y, width + cellWidth, cellHeight),
                            fg, flags);
                }
            }
            // Selection is an overlay and does not modify the VT screen cells.
            if (startSelection is { } s && endSelection is { } t)
            {
                if (s.row > t.row || (s.row == t.row && s.col > t.col)) (s, t) = (t, s);
                using var brush = new SolidBrush(Color.FromArgb(85, 104, 166, 230));
                for (int r = 0; r < screen.Rows; r++)
                {
                    int absolute = screen.GetAbsoluteRow(r);
                    if (absolute < s.row || absolute > t.row) continue;
                    int from = absolute == s.row ? s.col : 0;
                    int to = absolute == t.row ? t.col + 1 : screen.Columns;
                    graphics.FillRectangle(brush, from * cellWidth, r * cellHeight,
                        Math.Max(0, to - from) * cellWidth, cellHeight);
                }
            }
            if (Focused && ShowCaret && cursorVisible && screen.ScrollOffset == 0 && screen.ShowCursor)
            {
                var rect = new Rectangle(screen.CursorColumn * cellWidth, screen.CursorRow * cellHeight,
                    Math.Max(2, cellWidth / 5), cellHeight - 1);
                using var cursor = new SolidBrush(Color.FromArgb(197, 224, 240));
                graphics.FillRectangle(cursor, rect);
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            if (key is Keys.Tab or Keys.Enter or Keys.Escape or Keys.Up or Keys.Down or
                Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            string? sequence = null;
            if (e.Control && e.Shift && e.KeyCode == Keys.C) { CopySelection(); e.SuppressKeyPress = true; return; }
            if ((e.Control && e.KeyCode == Keys.V) || (e.Control && e.Shift && e.KeyCode == Keys.V))
            {
                PasteClipboard(); e.SuppressKeyPress = true; return;
            }
            if (e.Shift && e.KeyCode == Keys.Insert) { PasteClipboard(); e.SuppressKeyPress = true; return; }
            if (e.Control && e.KeyCode == Keys.C && HasSelection)
            {
                CopySelection(); e.SuppressKeyPress = true; return;
            }
            // Ctrl+C without a selection sends an interrupt to the running command.
            if (e.Control && e.KeyCode == Keys.C) sequence = "\x03";
            else if (e.Control && e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
                sequence = ((char)(e.KeyCode - Keys.A + 1)).ToString();
            else if (e.KeyCode == Keys.Enter) sequence = "\r";
            else if (e.KeyCode == Keys.Back) sequence = "\x7f";
            else if (e.KeyCode == Keys.Tab) sequence = e.Shift ? "\x1b[Z" : "\t";
            else if (e.KeyCode == Keys.Escape) sequence = "\x1b";
            else if (e.KeyCode == Keys.Up) sequence = "\x1b[A";
            else if (e.KeyCode == Keys.Down) sequence = "\x1b[B";
            else if (e.KeyCode == Keys.Right) sequence = "\x1b[C";
            else if (e.KeyCode == Keys.Left) sequence = "\x1b[D";
            else if (e.KeyCode == Keys.Home) sequence = "\x1b[H";
            else if (e.KeyCode == Keys.End) sequence = "\x1b[F";
            else if (e.KeyCode == Keys.Delete) sequence = "\x1b[3~";
            else if (e.KeyCode == Keys.Insert) sequence = "\x1b[2~";
            else if (e.KeyCode == Keys.PageUp) sequence = "\x1b[5~";
            else if (e.KeyCode == Keys.PageDown) sequence = "\x1b[6~";
            else if (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F4)
                sequence = new[] { "\x1bOP", "\x1bOQ", "\x1bOR", "\x1bOS" }[e.KeyCode - Keys.F1];
            else if (e.KeyCode >= Keys.F5 && e.KeyCode <= Keys.F12)
                sequence = "\x1b[" + new[] { 15, 17, 18, 19, 20, 21, 23, 24 }[e.KeyCode - Keys.F5] + "~";
            if (sequence == null) return;
            screen.ScrollToBottom();
            startSelection = endSelection = null;
            SendInput?.Invoke(sequence);
            e.Handled = true; e.SuppressKeyPress = true;
            cursorVisible = true; Invalidate();
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (e.Handled || char.IsControl(e.KeyChar)) return;
            if ((ModifierKeys & Keys.Control) != 0 && (ModifierKeys & Keys.Alt) == 0) return;
            screen.ScrollToBottom();
            startSelection = endSelection = null;
            SendInput?.Invoke(e.KeyChar.ToString());
            e.Handled = true;
            cursorVisible = true; Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                zoomFactor = Math.Clamp(zoomFactor + (e.Delta > 0 ? .1f : -.1f), .5f, 3f);
                UpdateFont();
            }
            else
            {
                screen.Scroll(e.Delta > 0 ? 3 : -3);
                Invalidate();
            }
        }

        private (int row, int col) PointToCell(Point point)
        {
            int r = Math.Clamp(point.Y / cellHeight, 0, screen.Rows - 1);
            int c = Math.Clamp(point.X / cellWidth, 0, screen.Columns - 1);
            return (screen.GetAbsoluteRow(r), c);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            selecting = true;
            startSelection = endSelection = PointToCell(e.Location);
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!selecting) return;
            endSelection = PointToCell(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            selecting = false;
            Capture = false;
            if (startSelection == endSelection) startSelection = endSelection = null;
            Invalidate();
        }

        private bool HasSelection => startSelection is not null && endSelection is not null;
        internal void CopySelection()
        {
            if (!HasSelection) return;
            var a = startSelection!.Value;
            var b = endSelection!.Value;
            var text = screen.ReadSelection(a.row, a.col, b.row, b.col);
            if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        }
        internal void CopyVisible()
        {
            string text = screen.ReadVisible();
            if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        }
        internal void PasteClipboard()
        {
            if (!Clipboard.ContainsText()) return;
            string value = Clipboard.GetText().Replace("\r\n", "\r").Replace('\n', '\r');
            SendInput?.Invoke(value);
            screen.ScrollToBottom(); Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e); cursorVisible = true; Invalidate();
        }
        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e); Invalidate();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) cursorTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    private readonly TerminalSurface terminal = new();
    private readonly ConcurrentQueue<string> outputQueue = new();
    private readonly System.Windows.Forms.Timer flushTimer = new() { Interval = 35 };
    private WindowsPseudoConsole? session;
    private bool isDisposed;
    private int queuedCharacters;
    private float baseFontPoints = 12f;
    public string InitialDirectory { get; }
    public bool IsRunning => session?.IsRunning == true;

    public EmbeddedShellControl(string directory)
    {
        InitialDirectory = Path.GetFullPath(directory);
        Dock = DockStyle.Fill;
        Padding = Padding.Empty;
        BackColor = TerminalScreen.DefaultBackground;
        terminal.SendInput += text => session?.Send(text);
        terminal.TerminalSizeChanged += (columns, rows) => session?.Resize(columns, rows);
        Controls.Add(terminal);
        flushTimer.Tick += (_, _) => FlushOutput();
        flushTimer.Start();
    }

    /// <summary>Application font preference. Terminal keeps independent Ctrl+wheel zoom.</summary>
    public void ApplyInterfaceFont(float points)
    {
        baseFontPoints = Math.Clamp(points, 8f, 32f);
        terminal.SetBaseFont(baseFontPoints);
    }

    public void StartShell()
    {
        if (isDisposed || IsRunning) return;
        try
        {
            // No banner, top toolbar, or fake prompt: cmd.exe paints its own prompt.
            terminal.Output("\x1b[0m");
            session = new WindowsPseudoConsole(InitialDirectory, terminal.Columns, terminal.Rows);
            session.OutputReceived += text =>
            {
                if (isDisposed) return;
                int pending = Interlocked.Add(ref queuedCharacters, text.Length);
                if (pending > 2_000_000)
                {
                    Interlocked.Add(ref queuedCharacters, -text.Length);
                    return;
                }
                outputQueue.Enqueue(text);
            };
            session.ShellExited += () =>
            {
                if (!isDisposed)
                {
                    const string notice = "\r\n\x1b[90m[Shell session ended. Close this tab to release it.]\x1b[0m\r\n";
                    Interlocked.Add(ref queuedCharacters, notice.Length);
                    outputQueue.Enqueue(notice);
                }
            };
            session.BeginStreaming();
            terminal.ShowCaret = true;
            FocusCommand();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or ArgumentException or
            UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException or
            EntryPointNotFoundException or System.Runtime.InteropServices.COMException)
        {
            terminal.Output("\r\n\x1b[91mCould not start Windows ConPTY shell:\x1b[0m\r\n" + ex.Message + "\r\n");
            terminal.ShowCaret = false;
        }
    }

    private void FlushOutput()
    {
        if (isDisposed) return;
        var batch = new StringBuilder(65536);
        int count = 0;
        while (count++ < 256 && batch.Length < 65536 && outputQueue.TryDequeue(out string? chunk))
        {
            Interlocked.Add(ref queuedCharacters, -chunk.Length);
            batch.Append(chunk);
        }
        if (batch.Length > 0) terminal.Output(batch.ToString());
    }

    public void FocusCommand() => terminal.Focus();
    public void StopShell()
    {
        var previous = session;
        session = null;
        previous?.Dispose();
        terminal.ShowCaret = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            isDisposed = true;
            flushTimer.Stop();
            StopShell();
            flushTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
