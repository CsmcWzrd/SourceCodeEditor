> **v0.5.5 build hotfix:** Corrects the invalid `MonospacedFontCatalog.cs` initializer which produced CS1003, CS1002, and CS0103. See `RELEASE-TEST-REPORT-v055.md`.

# SourceCodeEditor v0.5.4 — Windows x64

SourceCodeEditor is a C# WinForms editor using the **CSharpCodeDetective**
`LineNumberedRichTextBox` RichTextBox/RTF-based syntax highlighting implementation.
The upstream implementation, as provided by the user, remains under
`third_party/CSharpCodeDetective/`; the adapted control is included at the project
root and retains RTF-backed syntax coloring.

One Visual Studio 2022 solution (`SourceCodeEditor.sln`), one project
(`SourceCodeEditor.csproj`), **all source inside the top-level `SourceCodeEditor/`
folder**, and Windows x64 / .NET 8 target. No plugin system.

## New in 0.5.4: Installed monospaced fonts and file location

- **View → Monospaced Fonts → RTF Source Editor** lists the installed font
  families that pass a regular-weight fixed-width glyph check. Choosing one
  changes all editor tabs, including the line-number gutter. Colors and
  per-document zoom remain intact. Size is independently controlled by
  View → RTF Editor Font (8–32 pt).
- **View → Monospaced Fonts → Application Interface** provides the same font
  list for menus, tabs, navigation trees and the general WinForms interface.
  Application font size remains independently adjustable (8–32 pt).
- The font menus are populated from fonts *actually installed on the user's
  Windows machine*; the list is not a fixed set of names. Windows generally
  includes Consolas and Courier New; additional fonts appear only if installed
  and classified as fixed-width using representative Latin glyphs. Fonts with
  variable Latin character widths are omitted. Only Regular styles are used;
  editors do **not** become bold by default.
- Application and editor family names and sizes are persisted in
  `%LOCALAPPDATA%\SourceCodeEditor\session.json`. Older sessions without family
  properties retain the defaults Segoe UI (application) and Consolas (editor).
  Invalid or uninstalled saved font families fall back to defaults.
- **Tools → Locate Current File in File Explorer** explicitly switches to the
  built-in File Explorer navigation tab, refreshes its drive tree and selects
  the saved file. No Windows `explorer.exe` process is launched. With a deleted
  file or renamed folder it selects the nearest existing parent and warns;
  with an untitled document or active shell tab it explains why no file can
  be selected. Right-click → Locate in File Explorer uses the same routine.
- Source-only regression cases are included for existing/missing files, renamed
  directories, Unicode paths, and persisted font settings. Windows native
  font checks run via `negative-smoke.ps1`; interactive acceptance steps are
  documented in `SMOKE-TESTS.md`.

**Validation distinction:** This is source code and installer configuration,
not a precompiled Windows installer. Static/fixture tests can run in Linux;
WinForms rendering and Visual Studio compilation require Windows.

## New in 0.5.3: startup recovery, 12 pt regular font and UI exception fix

The source continues to use the original CSharpCodeDetective-derived WinForms
RTF/RichTextBox editor, and there is still **one solution and one project**.

- **Regular font:** The editor starts at **Consolas 12 pt, Regular**; its
  RichEdit text runs and empty-document insertion format are normalized to
  Regular. Syntax highlighting continues to use colored RTF runs, and View's
  editor-font setting remains separate from application UI fonts.
- **Always one editor document:** When the last editor document closes, an
  empty `Untitled N` tab is automatically created. This also holds after
  `Close All`, after startup with no restorable documents, and after closing
  a shell when there are no source documents. It is not saved to disk until
  the user chooses Save/Save As.
- **Reopen on startup:** `session.json` stores open saved documents, unsaved
  untitled and modified documents, dirty closed documents, clean closed
  documents, tab ordering/caret/zoom, recent files, encoding and EOL. On
  normal application exit, unsaved text is checkpointed **without prompting
  for Save or discarding the editor buffer**. Auto-snapshots occur every
  three seconds while the application is running. After an abnormal OS/process
  termination, edits made after the latest snapshot may be missing.
- **Closed-file startup preference:** `File -> Startup Session -> Reopen closed
  files at startup` is **enabled by default**. Disabling it retains saved
  closed entries in the Closed Files navigator, where they can be opened
  individually. `Reopen all files in Closed Files now` is also available.
- **Explicitly closing a dirty document:** `Yes` saves now; `No` retains
  unsaved text in Closed Files for recovery; `Cancel` leaves the tab open.
  `Remove from Closed Files` removes the retained entry. Exit itself no
  longer discards text. Changes to existing files are not auto-written to disk.
- **Deleted/renamed originals:** missing or inaccessible files are recovered
  from session snapshots into unsaved tabs requiring Save As. Existing disk
  conflict detection is still enforced.
- **Context-menu fix:** Removed disposal of newly created `ContextMenuStrip`
  objects from the `Closed` callback (the precise failure in the uploaded
  exception log). The two dynamic menus are owned by the main form and
  disposed after its lifetime, not while WinForms processes clicks.
- **Unexpected errors:** An exception log is kept locally at
  `%LOCALAPPDATA%\SourceCodeEditor\errors.log`. UI errors are reported with
  an actionable message; fatal background failures remain fatal and are logged.
  No telemetry is sent by this logging mechanism.
- **Safeguarded exit:** If the session backup cannot be written, exit warns
  and offers to *cancel* (default) to avoid unintentionally losing buffers.

The Windows native test runner gains 19 additional session/restore scenarios.

**Validation:** Offline structural/filesystem checks are executed from
`tests/offline_source_checks.py`. Native .NET and WinForms/ConPTY checks require
Windows; this source package does not claim to include a compiled installer or
successful Windows runtime qualification.

## New in 0.5.2: negative-testing and data-loss safeguards

This release hardens the **existing** one-project application and retains the
CSharpCodeDetective RTF-backed RichTextBox editor. It does not introduce a new
solution, plugin system, or alternative editor backend.

- **Closed Files entries are safe even if their file or parent directory has
  been deleted, moved or made inaccessible.** The entry remains visible and is
  labelled `[missing]` / `[inaccessible]` when detected. Double-click offers
  to reopen its last cached text as `filename (Recovered)` in an **unsaved**
  tab; the original path is removed from that document, so Save requires Save
  As. Declining leaves the entry in Closed Files.
- **Saving does not silently recreate deleted original files.** Active files
  whose original has vanished also require Save As. File creation in a removed
  parent directory is never attempted automatically.
- **External overwrite conflicts:** saving checks modification time, length,
  and SHA-256 of the original bytes. Even a change with the same timestamp and
  length is detected. After a change, explicit Yes/No/Cancel is required to
  overwrite, Save As, or abort. The file is checked again before the atomic
  move. An unavoidable narrow race remains if another process replaces the
  file after this check; concurrent multi-process transactions are not yet
  implemented.
- **Session recovery:** malformed JSON is copied to a dated `.corrupt_*.json`
  file before the editor could replace it. If copying fails, automatic session
  writes are disabled to protect the original. Dirty buffers, bookmarks,
  encodings, source path of recovered files, and duplicate-path entries are
  preserved. Duplicated paths are detached to recovered tabs.
- **Explorer:** binary content is rejected even when renamed with `.txt` or
  `.cs` extensions; hidden text files are still included. Missing directories,
  protected folders, and deep symlink/junction chains are handled safely.
- **Regression runner:** `--self-test-negative [REPORT.json]` executes **91
  defined headless tests** in its own isolated temporary directories, using
  the application's actual filesystem and serialization code. Run
  `negative-smoke.ps1` after publishing; the Windows build script and CI will
  block installer generation when these tests fail. See `NEGATIVE-TEST-MATRIX.md`.

**Verification distinction:** The authoring environment is Linux without a
.NET SDK or Windows. Offline source/fixture tests passed (see
`tests/OFFLINE-CHECK-RESULTS.json`); the 91 native .NET scenarios, ConPTY test,
WinForms UI, VS2022 compilation and installer still require execution on
Windows. No verified installer binary is supplied in this source archive.

## Introduced in 0.5.1: fix detached CMD and empty shell tab

**Root cause fixed:** The ConPTY process attribute was created using
`ref pseudoConsole` rather than the HPCON handle value. Unlike typical pointer
attributes, `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` requires the raw handle as
`lpValue` (`UpdateProcThreadAttribute(..., hpc, sizeof(hpc), ...)`). Passing a
pointer to that handle broke attachment and could launch a separate Windows
Terminal window while leaving the built-in tab blank. The P/Invoke signature
and call now pass `pseudoConsole` directly, following Microsoft's ConPTY code.

The shell tab also includes a startup watchdog: if its output pipe stays silent
for ten seconds, it reports a connection failure **inside the tab** and stops
the process. This prevents silently persisting a black editor panel.

A Windows-only self-test is available:

```
powershell -ExecutionPolicy Bypass -File conpty-smoke.ps1
```

The script runs the published EXE with `--self-test-conpty`, which launches CMD
on the real ConPTY backend, sends `echo`, and checks for command output returned
through SourceCodeEditor's own pipe. The self-test is integrated into the
Windows x64 CI workflow. **This environment has no Windows/.NET SDK**, so the
Windows build, self-test and GUI scenario still require execution on Windows.

## New in 0.5.0: interactive Windows ConPTY terminal tabs

The old shell widget used redirected standard input/output, a large toolbar,
readonly transcript and a separate command-entry box. This was **not a terminal**
and did not even display CMD's interactive prompt correctly. The code has been
replaced with three self-contained C# files:

- `WindowsPseudoConsole.cs` starts `cmd.exe /D` using the Windows ConPTY API,
  attaches it via STARTUPINFOEX, and communicates using dedicated input/output
  worker threads, with UTF-8/VT output and dynamic console resizing.
- `TerminalScreen.cs` parses output into a terminal character-cell grid,
  including cursor positioning, clear commands, scrollback, ANSI 16/256/true
  color, and alternate-screen mode.
- `EmbeddedShellControl.cs` owns a single keyboard-enabled terminal canvas;
  text and cursor appear at the same location, as in a terminal. There is no
  separate output panel, prompt field, or oversized shell toolbar.

Features: native CMD prompt and echo, `dir`, `cd`, `cls`, interactive console
input, Ctrl+C interrupts, arrow keys, scrolling, mouse text selection, copy
(Ctrl+Shift+C), paste (Ctrl+Shift+V or Ctrl+V), terminal zoom with Ctrl+wheel,
multiple independent tabs and x64 Windows support. Right-click in the terminal
to copy, paste, copy the visible screen, or clear it. Closing a shell tab
releases the pseudo console and ends its process tree.

**Compatibility:** ConPTY requires Windows 10 version 1809+ / Windows 11.
This compact terminal implements common VT/xterm sequences, but is **not** a
complete Windows Terminal replacement. Complex TUIs requiring advanced mouse
protocols, Unicode grapheme-width processing, or all ANSI extensions may not
render perfectly. Windows runtime testing is still pending in this environment.

## Introduced in 0.4.1: application and RTF editor fonts

Both the **File Explorer** and the **RTF source editor** start at **12 pt** on a
fresh installation. Under **View** there are two independent submenus:

- **Application Font (12 pt)** > Increase Application Font (+1 pt), Decrease
  Application Font (-1 pt), Reset Application Font (12 pt). Applies to menus,
  four navigator tabs, explorer and file trees, document tabs, status-bar UI,
  search dialogs, and the embedded CMD user interface.
- **RTF Editor Font (12 pt)** > Increase Editor Font (+1 pt), Decrease Editor
  Font (-1 pt), Reset Editor Font (12 pt). Applies to all open RTF editor
  documents, preserving selection, syntax colors, and the line-number gutter.
  Newly opened/recovered documents use the same base font.

Font sizes change in whole 1 pt steps, bounded to 8–32 pt. The two selected
font sizes are saved in the existing session file and restored after relaunch.
Previously saved sessions without these properties default to 12 pt.
Ctrl+mouse-wheel and **View > Zoom** continue to act on the active document
and do not change the base font settings. The gutter automatically measures
its width and font against the effective (base x zoom) text size.

## Introduced in 0.4.0: custom drive explorer

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

## Introduced in 0.4.0: shell tabs

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
supported, each with its own real CMD working directory and interactive prompt.
The replacement shell uses ConPTY rather than redirected, line-based stdio,
and inputs are typed directly into the terminal view at the cursor. Close tabs
using ×, middle click, Ctrl+W or the tab context menu.

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

No code folding, multi-caret/column editing, complete Windows Terminal VT protocol
coverage, macros, or large-file incremental engine. This project does **not** include
plugins, as requested. Syntax formatting is performed in an RTF-backed
RichTextBox control; text files are saved as source text rather than RTF.

Recovery snapshots are stored as plaintext at
`%LOCALAPPDATA%\\SourceCodeEditor\\session.json`; avoid storing secrets in
unsaved buffers unless your Windows profile/storage is appropriately secured.
