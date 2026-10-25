# SourceCodeEditor v0.4.0 — Windows x64

SourceCodeEditor is a C# WinForms editor using the **CSharpCodeDetective**
`LineNumberedRichTextBox` RichTextBox/RTF-based syntax highlighting implementation.
The upstream implementation, as provided by the user, remains under
`third_party/CSharpCodeDetective/`; the adapted control is included at the project
root and retains RTF-backed syntax coloring.

One Visual Studio 2022 solution (`SourceCodeEditor.sln`), one project
(`SourceCodeEditor.csproj`), **all source inside the top-level `SourceCodeEditor/`
folder**, and Windows x64 / .NET 8 target. No plugin system.

## New in 0.4.0: custom drive explorer

The **File Explorer** navigation tab is an **in-process TreeView** backed by
`System.IO`; it does **not** embed or launch Windows Explorer. It always shows
all enumerated drives as its top-level nodes (including removable drives).
Subfolders expand on demand. Folder contents display **all directories** and
**text/source files only**. Hidden/system files and directories are included
(`EnumerationOptions.AttributesToSkip = 0`). Files with recognized text/source
extensions show immediately; unknown extensions and extensionless files receive
a small content probe to exclude obvious binary data. Inaccessible folders are
reported in place rather than crashing the editor.

On **every launch**, the explorer opens at `<UserProfile>\\Documents` when it
exists, then falls back to the Windows-configured Documents known folder, then
the user's home folder. The last-visited session path is intentionally ignored.
Navigation to a different folder during the session is supported through the
address bar, drive tree, and Documents toolbar button. **Drives**, **Documents**
and **Refresh** are small explorer toolbar commands; the old large **Up** and
**Shell** buttons and native FolderBrowserDialog have been removed. `File > Open
Folder` now focuses the path field to allow typing any folder path.

The full navigation bar still contains these four tabs, in order:

1. File Explorer
2. Opened Files
3. Closed Files
4. Unsaved Files

The right pane still has closable tabs for individual source/text documents.

## New in 0.4.0: shell tabs

Use the **Shell** top-level menu:

- **Open shell here**: in the selected folder in the built-in explorer (or the
  parent directory when a file is selected).
- **Open shell in current file's location**: uses the directory of the active,
  saved text document; disabled for untitled buffers and when a shell tab is active.
- **Open shell in user's home directory**.
- **Open shell in user's Documents directory**.
- **Open shell in user's drive > [each available drive]**: generated dynamically
  from `DriveInfo.GetDrives()`; unavailable drives are disabled.

The file explorer right-click menu also contains **Open shell here (embedded tab)**.
Every action opens an independent, closable **CMD** tab **within the same right
editor tab control**, not a separate console window. Multiple shell tabs are
supported. The embedded shell uses a persistent `cmd.exe /Q /D` subprocess with
redirected standard input, output and error; its starting working directory is
the selected path, and `cd` commands keep their context within that process.
The bottom input field accepts commands, with Enter to execute and Up/Down for
command history. Output can be selected/copied. Tabs are closed using ×, middle
click, Ctrl+W or the context menu. Closing a shell disposes its subprocess.

**Terminal limitations:** This is a stream-based embedded command shell, not a
Win32 ConPTY/VT terminal emulator. Shell built-ins and ordinary noninteractive
CLI programs work, but console-dependent full-screen tools, advanced ANSI/VT
rendering, signal handling and interactive password prompts are not supported.
Shell tabs are not restored after restart, and commands are not recorded in the
session file. Do not paste commands you do not trust.

## Existing editing and navigation

- Source/document tabs, Opened/Closed/Unsaved one-level file trees, session
  recovery, unsaved buffers, close/reopen files, clipboard and editing shortcuts.
- RTF-backed syntax coloring, scalable line-number gutter, zoom, word wrap,
  bookmarks, auto-indent, line operations and comment toggling.
- Find/Replace and Find in Files with regular expression/whole-word/case options.
- Unicode encoding and newline options, backup saves, external modification
  prompts, recent files, and status bar.

`File > Open...` still uses Windows' standard file-open dialog; only the built-in
explorer **pane** was reimplemented. The "Locate Current File in File Explorer"
command navigates the built-in tree; it does not open `explorer.exe`.

## Build / installer (Windows x64)

Requires Visual Studio 2022 with .NET desktop workload and .NET 8 SDK:

1. Extract ZIP; open `SourceCodeEditor/SourceCodeEditor.sln`.
2. Select `Release | x64`, then **Build > Rebuild Solution**.
3. Run `SourceCodeEditor/build-x64.cmd` to publish a self-contained x64 EXE.
4. Install **Inno Setup 6** and rerun the script to also generate the x64
   installer `SourceCodeEditor/dist/SourceCodeEditor-Setup-x64.exe`.
5. The included Windows GitHub Actions workflow can also build/install-package
   on a Windows runner.

**Verification:** The archive and source-level references are checked during
packaging. A Windows .NET SDK/Visual Studio installation is not available in
this environment; an actual compilation, UI run, shell execution, and installer
run have **not** been verified. The ZIP contains source and an installer
**recipe**, not a precompiled installer binary.

## Remaining Notepad++ differences

No code folding, multi-caret/column editing, fully interactive terminal/ConPTY,
macros, or large-file incremental engine. This project does **not** include
plugins, as requested. Syntax formatting is performed in an RTF-backed
RichTextBox control; text files are saved as source text rather than RTF.

Recovery snapshots are stored as plaintext at
`%LOCALAPPDATA%\\SourceCodeEditor\\session.json`; avoid storing secrets in
unsaved buffers unless your Windows profile/storage is appropriately secured.
