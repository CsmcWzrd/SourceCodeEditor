using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CodeDetective.WinForms;

public sealed class LineNumberedRichTextBox : UserControl
{
    private const int WmSetRedraw = 0x000B;
    private const int EmGetFirstVisibleLine = 0x00CE;
    private const int SyntaxHighlightMaxChars = 1_250_000;

    private readonly LineNumberGutter _gutter = new() { Dock = DockStyle.Left, Width = 64 };
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

    public RichTextBox Editor { get; } = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9f),
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
            if (!IsDisposed && !_gutter.IsDisposed) _gutter.Invalidate();
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
        if (Editor.IsDisposed || Editor.TextLength == 0 || Editor.TextLength > SyntaxHighlightMaxChars) return;

        _isHighlighting = true;
        try
        {
            var selectionStart = Editor.SelectionStart;
            var selectionLength = Editor.SelectionLength;
            var scrollLine = SendMessage(Editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
            var text = Editor.Text;
            var tokens = SourceHighlighter.BuildHighlights(text, _filePath);
            if (tokens.Count == 0) return;

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
        if (_isHighlighting || _highlightQueued) return;
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

    private void PaintLineNumbers(Graphics g)
    {
        g.Clear(SystemColors.ControlLight);
        using var brush = new SolidBrush(SystemColors.ControlDarkDark);
        using var activeBrush = new SolidBrush(Color.FromArgb(35, 75, 145));
        using var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };

        var lineCount = Math.Max(1, Editor.Lines.Length);
        var firstLine = Math.Max(0, SendMessage(Editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32());
        var currentLine = Editor.GetLineFromCharIndex(Editor.SelectionStart);
        var visibleLines = Math.Max(1, (int)Math.Ceiling(Editor.ClientSize.Height / (double)Editor.Font.Height) + 2);
        var lastLine = Math.Min(lineCount - 1, firstLine + visibleLines);

        for (var line = firstLine; line <= lastLine; line++)
        {
            var charIndex = Editor.GetFirstCharIndexFromLine(line);
            if (charIndex < 0 && line == 0) charIndex = 0;
            if (charIndex < 0) continue;
            var pos = Editor.GetPositionFromCharIndex(charIndex);
            if (pos.Y < -Editor.Font.Height || pos.Y > Editor.ClientSize.Height) continue;
            var rect = new RectangleF(0, pos.Y + 1, _gutter.Width - 8, Editor.Font.Height + 2);
            g.DrawString((line + 1).ToString(), Editor.Font, line == currentLine ? activeBrush : brush, rect, format);
        }

        using var pen = new Pen(SystemColors.ControlDark);
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
        private static readonly Regex CStyleCommentRegex = new(@"//.*?$|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline);
        private static readonly Regex HashCommentRegex = new(@"(?m)#.*$", RegexOptions.Compiled);
        private static readonly Regex StringRegex = new("@?\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*'", RegexOptions.Compiled | RegexOptions.Singleline);

        public static List<HighlightToken> BuildHighlights(string text, string filePath)
        {
            var tokens = new List<HighlightToken>(capacity: Math.Min(4096, Math.Max(64, text.Length / 48)));
            var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            var hashComments = ext is ".py" or ".sh" or ".ps1" or ".rb";

            AddMatches(tokens, KeywordRegex.Matches(text), KeywordColor);
            AddMatches(tokens, TypeRegex.Matches(text), TypeColor);
            AddMatches(tokens, NumberRegex.Matches(text), NumberColor);
            AddMatches(tokens, StringRegex.Matches(text), StringColor);
            if (!hashComments) AddMatches(tokens, PreprocessorRegex.Matches(text), PreprocessorColor);
            AddMatches(tokens, hashComments ? HashCommentRegex.Matches(text) : CStyleCommentRegex.Matches(text), CommentColor);

            // Later token groups intentionally override earlier groups, so comments and strings win over keywords inside them.
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
