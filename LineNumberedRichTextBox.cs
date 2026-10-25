using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SourceCodeEditor;

public sealed class LineNumberedRichTextBox : UserControl
{
    private const int WmSetRedraw = 0x000B;
    private const int EmGetFirstVisibleLine = 0x00CE;
    private const int SyntaxHighlightMaxChars = 1_250_000;

    private readonly LineNumberGutter _gutter = new() { Dock = DockStyle.Left, Width = 45 };
    public HashSet<int> Bookmarks { get; set; } = new();
    private bool _showLineNumbers = true;
    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set { _showLineNumbers = value; _gutter.Visible = value; QueueGutterRefresh(); }
    }
    public void RefreshLineNumbers() => QueueGutterRefresh();
    private readonly System.Windows.Forms.Timer _gutterRefreshTimer = new() { Interval = 20 };
    private readonly System.Windows.Forms.Timer _highlightTimer = new() { Interval = 550 };
    private string _filePath = string.Empty;
    private bool _isHighlighting;
    private bool _highlightQueued;

    public string DocumentFilePath
    {
        get => _filePath;
        set => _filePath = value ?? string.Empty;
    }

    public ZoomRichTextBox Editor { get; } = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 10.5f),
        WordWrap = false,
        AcceptsTab = true,
        HideSelection = false,
        DetectUrls = false,
        BorderStyle = BorderStyle.FixedSingle
    };

    public LineNumberedRichTextBox()
    {
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Window;
        DoubleBuffered = true;

        Controls.Add(Editor);
        Controls.Add(_gutter);
        _gutter.Attach(this);

        _gutterRefreshTimer.Tick += (_, _) =>
        {
            _gutterRefreshTimer.Stop();
            if (!IsDisposed && !_gutter.IsDisposed)
            {
                ResizeGutterToFont();
                _gutter.Invalidate();
            }
        };

        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            ApplySyntaxHighlighting();
        };

        Editor.TextChanged += (_, _) =>
        {
            QueueGutterRefresh();
            QueueSyntaxHighlighting();
        };
        Editor.Resize += (_, _) => QueueGutterRefresh();
        Editor.VScroll += (_, _) => QueueGutterRefresh();
        Editor.MouseWheel += (_, _) => QueueGutterRefresh();
        Editor.ZoomChanged += (_, _) => QueueGutterRefresh();
        Editor.FontChanged += (_, _) => QueueGutterRefresh();
        Editor.KeyUp += (_, _) => QueueGutterRefresh();
        Editor.MouseUp += (_, _) => QueueGutterRefresh();
    }

    public void LoadText(string text, string filePath)
    {
        _filePath = filePath;
        using (new RedrawScope(Editor))
        {
            Editor.Text = text;
            Editor.Select(0, 0);
        }
        ApplySyntaxHighlighting();
        QueueGutterRefresh();
    }

    public void ApplySyntaxHighlighting()
    {
        if (_isHighlighting) return;
        if (Editor.IsDisposed || Editor.TextLength > SyntaxHighlightMaxChars) return;

        _isHighlighting = true;
        try
        {
            var selectionStart = Editor.SelectionStart;
            var selectionLength = Editor.SelectionLength;
            var scrollLine = SendMessage(Editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
            var text = Editor.Text;
            var tokens = SourceHighlighter.BuildHighlights(text, _filePath);
            // Clear stale colors even when a document has no recognized tokens.
            // The upstream RTF-backed formatting is kept, not replaced with a plain textbox.

            using (new RedrawScope(Editor))
            {
                Editor.SelectAll();
                Editor.SelectionColor = Editor.ForeColor;
                Editor.SelectionBackColor = Editor.BackColor;

                foreach (var token in tokens)
                {
                    if (token.Start < 0 || token.Start >= Editor.TextLength) continue;
                    var length = Math.Min(token.Length, Editor.TextLength - token.Start);
                    if (length <= 0) continue;
                    Editor.Select(token.Start, length);
                    Editor.SelectionColor = token.Color;
                }

                Editor.Select(Math.Min(selectionStart, Editor.TextLength), Math.Min(selectionLength, Math.Max(0, Editor.TextLength - selectionStart)));
            }

            // Restore the visible area as closely as WinForms RichTextBox permits.
            if (scrollLine > 0)
            {
                var firstChar = Editor.GetFirstCharIndexFromLine(Math.Min(scrollLine, Math.Max(0, Editor.Lines.Length - 1)));
                if (firstChar >= 0)
                {
                    var restoreStart = Editor.SelectionStart;
                    var restoreLength = Editor.SelectionLength;
                    Editor.Select(firstChar, 0);
                    Editor.ScrollToCaret();
                    Editor.Select(Math.Min(restoreStart, Editor.TextLength), Math.Min(restoreLength, Math.Max(0, Editor.TextLength - restoreStart)));
                }
            }
        }
        finally
        {
            _isHighlighting = false;
            _highlightQueued = false;
            QueueGutterRefresh();
        }
    }

    private void QueueSyntaxHighlighting()
    {
        if (_isHighlighting) return;
        if (Editor.TextLength > SyntaxHighlightMaxChars) return;
        _highlightQueued = true;
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private void QueueGutterRefresh()
    {
        if (IsDisposed) return;
        _gutterRefreshTimer.Stop();
        _gutterRefreshTimer.Start();
    }

    private Font ScaledLineFont()
    {
        float scale = Math.Clamp(Editor.ZoomFactor, 0.3f, 5f);
        return new Font(Editor.Font.FontFamily, Math.Clamp(Editor.Font.SizeInPoints * scale, 5f, 64f), FontStyle.Regular, GraphicsUnit.Point);
    }

    private void ResizeGutterToFont()
    {
        if (!_showLineNumbers || Editor.IsDisposed || _gutter.IsDisposed) return;
        using var font = ScaledLineFont();
        int digits = Math.Max(2, Math.Max(1, Editor.Lines.Length).ToString().Length);
        int pixels = TextRenderer.MeasureText(new string('8', digits), font, new Size(1000, 1000), TextFormatFlags.NoPadding).Width;
        int ideal = Math.Clamp(pixels + 18, 38, 190);
        if (_gutter.Width != ideal) _gutter.Width = ideal;
    }

    private void PaintLineNumbers(Graphics g)
    {
        g.Clear(Color.FromArgb(245, 247, 250));
        if (!_showLineNumbers || Editor.IsDisposed) return;
        using var font = ScaledLineFont();
        var normal = Color.FromArgb(105, 111, 122);
        var active = Color.FromArgb(30, 75, 150);
        int firstVisibleChar = Editor.GetCharIndexFromPosition(new Point(1, 1));
        int firstVisibleLine = Math.Max(0, Editor.GetLineFromCharIndex(firstVisibleChar) - 1);
        int currentLine = Editor.GetLineFromCharIndex(Editor.SelectionStart);
        int maxLine = Math.Max(1, Editor.Lines.Length);
        int minHeight = Math.Max(8, font.Height);
        int end = Math.Min(maxLine, firstVisibleLine + Math.Max(8, Editor.ClientSize.Height / Math.Max(1, minHeight) + 8));
        for (int line = firstVisibleLine; line < end; line++)
        {
            int index = Editor.GetFirstCharIndexFromLine(line);
            if (index < 0) continue;
            var position = Editor.GetPositionFromCharIndex(index);
            if (position.Y < -minHeight || position.Y > Editor.ClientSize.Height + 4) continue;
            var rectangle = new Rectangle(1, position.Y, Math.Max(0, _gutter.Width - 11), minHeight + 6);
            if (Bookmarks.Contains(line + 1))
            {
                using var marker = new SolidBrush(Color.FromArgb(250, 165, 38));
                g.FillEllipse(marker, new Rectangle(2, position.Y + Math.Max(0, minHeight / 2 - 3), 7, 7));
            }
            TextRenderer.DrawText(g, (line + 1).ToString(), font, rectangle,
                line == currentLine ? active : normal,
                TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        using var pen = new Pen(Color.FromArgb(215, 220, 227));
        g.DrawLine(pen, _gutter.Width - 1, 0, _gutter.Width - 1, _gutter.Height);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private sealed class LineNumberGutter : Panel
    {
        private LineNumberedRichTextBox? _owner;

        public LineNumberGutter()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
        }

        public void Attach(LineNumberedRichTextBox owner) => _owner = owner;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Paint the full gutter in OnPaint to avoid erase-background flicker during editor scrolling.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_owner == null) base.OnPaint(e);
            else _owner.PaintLineNumbers(e.Graphics);
        }
    }

    private sealed class RedrawScope : IDisposable
    {
        private readonly Control _control;

        public RedrawScope(Control control)
        {
            _control = control;
            if (_control.IsHandleCreated) SendMessage(_control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (_control.IsHandleCreated)
            {
                SendMessage(_control.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                _control.Invalidate();
            }
        }
    }

    // Intercept WM_MOUSEWHEEL before RichEdit handles Ctrl+wheel itself.
    // This keeps the text zoom and the gutter zoom precisely in sync.
    public sealed class ZoomRichTextBox : RichTextBox
    {
        private const int WmMouseWheel = 0x020A;
        public event EventHandler? ZoomChanged;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmMouseWheel && (ModifierKeys & Keys.Control) == Keys.Control)
            {
                int delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xffff));
                ZoomFactor = Math.Clamp(ZoomFactor + (delta >= 0 ? 0.10f : -0.10f), 0.50f, 4.0f);
                ZoomChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }
    }

    private readonly record struct HighlightToken(int Start, int Length, Color Color);

    private static class SourceHighlighter
    {
        private static readonly Color KeywordColor = Color.FromArgb(0, 0, 180);
        private static readonly Color StringColor = Color.FromArgb(150, 70, 0);
        private static readonly Color CommentColor = Color.FromArgb(0, 128, 0);
        private static readonly Color NumberColor = Color.FromArgb(120, 0, 150);
        private static readonly Color PreprocessorColor = Color.FromArgb(120, 80, 0);
        private static readonly Color TypeColor = Color.FromArgb(43, 145, 175);

        private static readonly string[] CommonKeywords =
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
            "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
            "ref", "return", "sbyte", "sealed", "short", "sizeof", "static", "string", "struct", "switch", "this", "throw", "true", "try",
            "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
            "auto", "constexpr", "delete", "friend", "inline", "mutable", "nullptr", "template", "typename", "include", "define", "ifdef", "ifndef", "endif",
            "def", "elif", "except", "from", "global", "import", "lambda", "nonlocal", "pass", "raise", "with", "yield", "async", "await",
            "function", "let", "var", "package", "func", "defer", "go", "map", "chan", "range", "select", "type", "impl", "trait", "match", "mod", "mut", "pub", "use"
        };

        private static readonly Regex KeywordRegex = new($"\\b({string.Join("|", CommonKeywords.Select(Regex.Escape))})\\b", RegexOptions.Compiled);
        private static readonly Regex TypeRegex = new(@"\b([A-Z][A-Za-z0-9_]*)(?=\s*[<\(\{;\[]?)", RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new(@"\b(0x[0-9A-Fa-f]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)(?:[uUlLfFdDmM]*)\b", RegexOptions.Compiled);
        private static readonly Regex PreprocessorRegex = new(@"(?m)^\s*#\s*[A-Za-z_][A-Za-z0-9_]*.*$", RegexOptions.Compiled);
        private static readonly Regex CStyleCommentRegex = new(@"//[^\r\n]*|/\*[\s\S]*?\*/", RegexOptions.Compiled);
        private static readonly Regex HashCommentRegex = new(@"(?m)#[^\r\n]*", RegexOptions.Compiled);
        // Ordered alternatives tokenize comments and quoted strings without coloring
        // a URL's // or a quoted Python '#' as a comment.
        private static readonly Regex CStyleLexRegex = new(@"/\*[\s\S]*?\*/|//[^\r\n]*|@?""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'", RegexOptions.Compiled);
        private static readonly Regex HashLexRegex = new(@"#[^\r\n]*|@?""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'", RegexOptions.Compiled);
        private static readonly Regex SqlLexRegex = new(@"--[^\r\n]*|/\*[\s\S]*?\*/|""(?:\\.|[^""\\])*""|'(?:''|[^'])*'", RegexOptions.Compiled);
        private static readonly Regex XmlLexRegex = new(@"<!--[\s\S]*?-->|""[^""]*""|'[^']*'", RegexOptions.Compiled);
        private static readonly Regex SqlKeywordRegex = new(@"\b(select|insert|update|delete|create|alter|drop|table|view|where|from|join|left|right|inner|outer|group|order|by|having|limit|offset|values|distinct|into|as|on|and|or|not|null|is|like|union|case|when|then|else|end|begin|commit|rollback|primary|key|foreign|references|index)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CMotiveKeywordRegex = new(@"\b(Class|Overridable|Operation|Global|Fptr|Target|Hit|New|Delete|Struct|Include|Package|Public|Private|Protected|Return|If|Else|While|For|Try|Catch|Throw|Int|Uint|Int16|Int32|I16|I32|I64|U16|U32|U64)\b", RegexOptions.Compiled);
        private static readonly Regex XmlTagRegex = new(@"</?[A-Za-z][A-Za-z0-9:_.-]*(?=\s|/?>)", RegexOptions.Compiled);
        private static readonly Regex MarkdownHeadingRegex = new(@"(?m)^\s{0,3}#{1,6}[^\r\n]*", RegexOptions.Compiled);
        private static readonly Regex StringRegex = new("@?\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*'", RegexOptions.Compiled | RegexOptions.Singleline);

        public static List<HighlightToken> BuildHighlights(string text, string filePath)
        {
            var tokens = new List<HighlightToken>(capacity: Math.Min(4096, Math.Max(64, text.Length / 48)));
            var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".txt" or ".log" or ".csv") return tokens;
            if (ext is ".md" or ".markdown")
            {
                AddMatches(tokens, MarkdownHeadingRegex.Matches(text), KeywordColor);
                return tokens;
            }
            bool hashComments = ext is ".py" or ".sh" or ".ps1" or ".rb" or ".yaml" or ".yml";
            bool xml = ext is ".xml" or ".html" or ".htm" or ".xaml" or ".svg";
            bool cmotive = ext is ".cmot" or ".cmtv" or ".hmot" or ".hmtv";
            bool sql = ext is ".sql";
            if (sql) AddMatches(tokens, SqlKeywordRegex.Matches(text), KeywordColor);
            else if (cmotive) AddMatches(tokens, CMotiveKeywordRegex.Matches(text), KeywordColor);
            else AddMatches(tokens, KeywordRegex.Matches(text), KeywordColor);
            if (xml) AddMatches(tokens, XmlTagRegex.Matches(text), TypeColor);
            else if (!sql) AddMatches(tokens, TypeRegex.Matches(text), TypeColor);
            AddMatches(tokens, NumberRegex.Matches(text), NumberColor);
            if (!hashComments && !xml) AddMatches(tokens, PreprocessorRegex.Matches(text), PreprocessorColor);
            // A single ordered scan determines whether each token is a comment or
            // a quoted literal, preventing overlapping/comment-in-string artifacts.
            var lexical = hashComments ? HashLexRegex : sql ? SqlLexRegex : xml ? XmlLexRegex : CStyleLexRegex;
            foreach (Match match in lexical.Matches(text))
            {
                if (!match.Success || match.Length == 0) continue;
                bool comment = match.Value.StartsWith("//", StringComparison.Ordinal) ||
                    match.Value.StartsWith("/*", StringComparison.Ordinal) ||
                    match.Value.StartsWith("#", StringComparison.Ordinal) ||
                    match.Value.StartsWith("--", StringComparison.Ordinal) ||
                    match.Value.StartsWith("<!--", StringComparison.Ordinal);
                tokens.Add(new HighlightToken(match.Index, match.Length, comment ? CommentColor : StringColor));
            }
            return tokens;
        }

        private static void AddMatches(List<HighlightToken> tokens, MatchCollection matches, Color color)
        {
            foreach (Match match in matches)
            {
                if (match.Success && match.Length > 0) tokens.Add(new HighlightToken(match.Index, match.Length, color));
            }
        }
    }
}
