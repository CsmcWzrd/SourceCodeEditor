# SourceCodeEditor — Windows x64 source editor

SourceCodeEditor is a C# WinForms source editor, built around the original
`LineNumberedRichTextBox` RTF-backed control from the **uploaded CSharpCodeDetective**
repository. Its original MIT license is included as `LICENSE-upstream.txt`.

**One solution** (`SourceCodeEditor.sln`), **one project** (`SourceCodeEditor.csproj`),
all source in the top-level `SourceCodeEditor` directory, targeting Visual Studio 2022,
.NET 8 and Windows x64. There is **no plugin system**.

## Corrected UI layout

- Main window uses a 3-row `TableLayoutPanel`: menu, editor/navigation, status bar.
  This fixes the previous docking order that obscured BOTH tab-control headers.
- Left navigator defaults to **216 pixels**, has a draggable splitter and visible,
  multiline headers for **File Explorer**, **Opened Files**, **Closed Files**, and
  **Unsaved Files**, in that order.
- The File Explorer tab has a compact `Browse...` / `Refresh` toolbar and path textbox.
  **Up** and **Shell** large buttons have been removed. Context-click a folder or file
  to use **Open shell here (cmd.exe)**, **Open PowerShell here**, **Set folder as root**,
  or **Go to parent folder**.
- The right editor pane has actual **per-document tab headers**, each with an **×**
  close affordance, plus middle-click close, a context menu, and Ctrl+Tab switching.
- The left three document trees have one-level lists; double-click an entry to
  open/activate it. File Explorer has a full expandable folder tree.

## Included editing capabilities

- New, multi-file open, Save, Save As, Save All, Save Backup Copy, close, close all,
  reload, recent files, drag-and-drop files and folders.
- Reuse of CSharpCodeDetective's RichTextBox/RTF syntax formatting and numbered gutter.
  Highlights many C-style languages and Python, SQL, markup, Markdown and CMotive
  source extensions (basic tokenization, not a full language parser).
- **Ctrl+mouse-wheel zoom scales the source and the line-number font together**.
  Gutter width recalculates with digit count and font zoom. Zoom in/out/reset menu,
  Word Wrap, Show Line Numbers, full-screen and optional auto-indent.
- Undo/redo, cut/copy/paste (plain text), duplicate/delete/move line,
  indent/unindent, toggle line comments, case transforms, trim trailing spaces,
  sort selected lines, select all.
- Find/replace: regex with timeouts, match case, whole words, backward, wrap,
  Find All (click results to navigate), Replace and Replace All.
- Find in Files: directory search, filename masks, optional recursion, regex,
  case sensitivity and clickable results; asynchronously searches files <= 12 MB;
  max 5,000 results per run.
- Bookmarks (Ctrl+F2, F2, Shift+F2), Go to Line (Ctrl+G), document status line,
  line/column, character count, encoding, EOL type and zoom level.
- File change monitoring polls file modification time and size about every 4.5s;
  confirms before reloading. Session snapshots every 10s include unsaved buffers,
  closed-file list, caret positions, zoom, bookmarks, recent file list and explorer root.
- Unicode file detection and save encoding selection: UTF-8 with/without BOM,
  UTF-16 LE/BE, UTF-32 LE/BE and Latin-1 fallback. Supports CRLF, LF and CR
  output selection. Atomic save via a temporary file in the same folder.
- Print selected document as text (source highlighting is not printed).

## Build for Windows x64

Prerequisites: **Visual Studio 2022 (17.x)** with .NET desktop development workload,
**.NET 8 SDK**, and optionally **Inno Setup 6** to produce the installer.

1. Extract the ZIP and open `SourceCodeEditor/SourceCodeEditor.sln`.
2. Select `Release | x64` and use **Build > Rebuild Solution**.
3. For a self-contained x64 publish and a setup executable, install Inno Setup 6
   and run `SourceCodeEditor/build-x64.cmd`. The script builds
   `dist/win-x64/SourceCodeEditor.exe` and
   `dist/SourceCodeEditor-Setup-x64.exe`.
4. Alternatively, the included `.github/workflows/windows-x64-installer.yml`
   can be used on Windows GitHub Actions runners to compile and generate the installer.

The app and Inno installer target **64-bit x86 Windows only**.

**Verification note:** The ZIP and C# lexical structure were checked in a Linux
runtime. The environment did NOT have the Windows .NET SDK / Visual Studio / Inno
Setup, so compilation, installer execution and UI tests **could not be performed**.
The included Windows CI workflow is provided to perform those checks on a Windows runner.

## Remaining differences from Notepad++

This is an expanded RTF-backed source editor, **not yet complete Notepad++ parity**.
Not present: Scintilla's incremental large-file engine, multi-selection/column mode,
code folding and function list, code minimap, macros, advanced lexer/parser coverage,
project workspaces, diff tools, FTP/SFTP, and some sophisticated search and encoding
workflows. No third-party plugins are added by design.

`RichTextBox` is an RTF-capable *editor control*, but ordinary source documents are
saved as **plain text**, not as `.rtf` files. Syntax highlighting is in-memory only.
BOM-less legacy code pages cannot be reliably autodetected; non-UTF8 bytes fall back
to Latin-1. Mixed line endings are normalized to the selected EOL on saving edited
text. Large text files can exceed the practical limits of a RichTextBox.

Session recovery JSON is at `%LOCALAPPDATA%\SourceCodeEditor\session.json` and
contains plain-text document buffers, including unsaved data. It is not encrypted.
Keep highly sensitive documents out of autosave sessions or protect that profile.
