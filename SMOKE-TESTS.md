
## v0.5.4: Monospaced font menu and locate-current-file

1. Open **View → Monospaced Fonts**. Confirm two submenus: **RTF Source Editor**
   and **Application Interface**. Each should enumerate installed regular
   fixed-width font families and show a check mark for the active choice when
   that choice is part of the monospaced list.
2. Select two different installed monospaced families in the editor submenu,
   with `.cs` and `.py` files open. All RTF editor tabs (including gutter)
   should use the chosen font; font weight should be **Regular**, not Bold.
   Syntax token colors, caret selection, line numbers, and zoom should remain.
3. Choose a monospaced family for the application UI; verify left navigator,
   tabs, menu strip, explorer tree, status and shell tab UI. Resize/zoom afterward
   and check there are no splitter exceptions or clipped navigation captions.
4. Set editor font to 15 pt and application to 13 pt, close and reopen the app.
   Both font family choices and both sizes should be restored from `session.json`.
   Remove one installed font and restart: the unavailable family should fall
   back to Consolas/Segoe UI, not crash or damage recovered documents.
5. Open an existing `.cs` file, choose **Tools → Locate Current File in File
   Explorer**: the File Explorer navigation tab must become active and the file
   must be selected in the tree. Repeat after navigating elsewhere in the tree,
   after adding a file externally, and for hidden files.
6. Delete or rename a previously opened file/folder externally, then choose
   Locate: the closest existing parent folder should be selected and a clear
   warning shown, without losing the editor's cached content. Test disconnected
   drives and protected directories as well.
7. With an unsaved `Untitled` document selected, choose Locate: the built-in
   explorer opens at Documents and explains that Save As is required. With a
   CMD shell tab selected, Locate should explain that there is no source file.
8. Right-click a file in **Opened Files** or **Closed Files** and choose Locate:
   verify it uses the same internal navigation logic, including missing paths.

# SourceCodeEditor 0.5.3 — Windows x64 smoke-test checklist

**Not yet executed** in a Windows test environment. These checks are intended
for Visual Studio 2022 / Windows 10 or Windows 11 x64.

## Startup and explorer

1. Start app; four left tabs are visible: File Explorer, Opened Files, Closed Files,
   Unsaved Files. Editor occupies most of the window width.
2. File Explorer lists **all local/mapped drives at the top level**. The selected
   folder and address begin at the user's `Documents` directory on EVERY launch,
   including after previously selecting another folder and closing the app.
3. Expand `C:\`, another drive, and nested folders. Confirm directories always
   show, but `.png`, `.exe`, `.dll`, `.zip` and other known binary files do not;
   `.cs`, `.cpp`, `.py`, `.md`, `.txt`, `.json`, `.CMOT`, extensionless text files
   do appear. Create a hidden `.env`, hidden `.py` and hidden directory and verify
   they appear without changing Windows' Explorer hidden-file setting.
4. Try an inaccessible folder/removable drive. It must not crash. Press Refresh.
5. File Explorer toolbar has compact Documents/Drives/Refresh commands; no large
   Up/Shell buttons. `File > Open Folder` focuses the address field, rather than
   launching Windows FolderBrowserDialog. Double-click a source file and check
   that it opens in a right-hand document tab.
6. `Tools > Locate Current File in File Explorer` highlights the file in the
   built-in explorer (no Windows `explorer.exe` process is spawned).

## Embedded shell tabs

7. Expand/select `C:\Temp` (create if needed). Choose **Shell > Open shell here**.
   Verify a new CMD tab appears alongside source tabs and executes `cd`, `dir`,
   `echo hello` and `cd ..` persistently within the same shell process.
8. With a saved source file active, choose **Shell > Open shell in current file's
   location**; run `cd` to verify the starting folder. With untitled/terminal
   tab active, the option must be disabled.
9. Create shell tabs for user's home, Documents, and every available drive via
   **Shell > Open shell in user's drive** subitems. Verify separate tab titles,
   independent working directories and command histories.
10. Right-click a file and a folder in File Explorer; **Open shell here (embedded
    tab)** must start in the correct folder (file uses parent folder).
11. Close a CMD tab using ×, middle click, Ctrl+W and tab context menu. Confirm
    the associated subprocess is terminated. Verify it does not close editor
    document tabs or discard their unsaved edits. Try two CMD tabs concurrently.
12. Try a command producing many output lines; the UI must remain responsive.
    The terminal must retain correct prompt placement and scrollback. Test
    Ctrl+C interrupt and CMD keyboard navigation. Complex TUIs need separate
    acceptance tests because the VT renderer is not yet feature complete.

## ConPTY visual and interactivity regression

12a. The CMD tab must have **no separate bottom input box** and **no embedded
    top toolbar**. The terminal prompt and typed text must share one continuous
    black console surface; tabs and application status bar remain outside it.
12b. Run `echo hello`, `dir`, `cd`, `cls`, `set /p name=Name:`, then type a value.
    Confirm echo, line editing, prompt rendering and clear-screen semantics.
12c. Run `ping -t 127.0.0.1` and press Ctrl+C; verify it interrupts. Use Up
    arrow to recall commands, then type Backspace and execute the edited command.
12d. Paste a command using Ctrl+V, select text with mouse, copy with
    Ctrl+Shift+C, and scroll backwards with mouse wheel.
12e. Resize the main window and splitter and change the application font.
    Check the console automatically reflows and the cursor remains visible.
12f. Run `powershell -NoLogo`, execute `Write-Host Test -ForegroundColor Cyan`,
    exit and confirm colors and prompt are rendered. Repeat with two tabs.
12g. Close a running `ping -t` shell tab. Confirm child processes and pipe
    handles are released and the UI doesn't hang.

## ConPTY attachment regression (0.5.2)

The v0.5.0 implementation mistakenly passed `ref pseudoConsole` to
`UpdateProcThreadAttribute` and thereby passed an HPCON pointer instead of the
HPCON handle value. Windows could open CMD in a separate Windows Terminal
window and leave SourceCodeEditor with a blank shell tab. The C# P/Invoke now
accepts `IntPtr` for `lpValue`, and receives the pseudoconsole handle directly.

- Build/publish `Release|x64` and run the `conpty-smoke.ps1` script. Its
  headless self-test creates CMD using the exact application ConPTY backend,
  writes an `echo` command, and checks that the result is received on the
  application's own output pipe. No external Windows Terminal should appear.
- Open **Shell > Open shell in user's Documents directory** in the GUI.
  Verify that CMD's version banner and `C:\...>` prompt are displayed in the
  **same dark editor tab**, not in any external console window.
- Type `echo hello`, `dir`, `cd`, `cls`, arrow up/down, and `exit` directly in
  the editor tab. Try multiple shell tabs and close them using the tab X.
- Temporarily test a failure to attach: the shell must display an error inside
  the tab rather than remain black for more than ten seconds. Restore the
  correct ConPTY implementation before shipping.

## Existing editor regression

13. Open two text/source files; verify separate closeable editor tabs and
    source syntax formatting. Ctrl+mouse wheel scales line numbers and editor.
14. Verify Opened/Closed/Unsaved left tabs and unsaved close prompt.
15. Find/Replace, Find in Files, line editing, bookmarks, encodings, EOLs and
    session recovery continue working.
16. Build `Release | x64` in the **single** `SourceCodeEditor.sln` and run
    `build-x64.cmd` with Inno Setup installed. Check generated installer and
    clean install/uninstall on Windows x64.

## Application/RTF editor font sizing (0.4.1)

17. On a fresh installation (or session with no saved font settings), verify
    the File Explorer tree/address and source editor use 12 pt fonts.
18. Choose **View > Application Font > Increase Application Font** three times.
    Verify the menu, navigation tabs, file lists, status bar, editor tab headers
    and CMD shell UI grow without clipping; restore to 12 pt.
19. Choose **View > RTF Editor Font > Increase Editor Font**. Verify all existing
    source tabs change together, the line-number gutter stays aligned, and syntax
    highlight colors remain unchanged. Decrease, then reset to 12 pt.
20. Ctrl+mouse-wheel zooms one document independently of its base font size;
    switching documents must not change the base font. With a large font,
    close a tab using × and make sure text in the tab header remains clickable.
21. Save an unsaved document, adjust both font sizes, close and restart.
    Verify restored application/editor font sizes and 12 pt reset commands.
22. Open Find and Replace/Find in Files dialogs; verify application font scaling
    while dialog controls remain usable. Verify no resize or startup exception.

## New in 0.5.2 — file-loss negative paths

See `NEGATIVE-TEST-MATRIX.md` for the extensive matrix. Mandatory targeted
manual checks: close a cached file and delete its parent folder; re-open via
Closed Files, choose No (must remain listed), choose Yes (must open as Unsaved
Recovered), then cancel Save As (buffer must remain) and save to an existing
folder (must not recreate the removed directory). Repeat with an empty file,
missing drive, moved file, file replaced by directory, an exclusive-lock file,
changes with identical size/timestamps, malformed session JSON, and multiple
simultaneous shells. Run `negative-smoke.ps1` before each installer build.

## New in 0.5.3 — WinForms UI and session regression cases

- [ ] [Font] Start a blank editor: `Editor.Font.Style == FontStyle.Regular`
  and `SelectionFont.Style == FontStyle.Regular`, 12 pt. Type text, open a
  source file, syntax-highlight, increase/decrease font, reload, verify normal.
- [ ] [ContextMenuStrip] From Opened/Closed/Unsaved tree, right-click Close,
  Open/Activate, Close other tabs, and Remove. No ObjectDisposedException
  from `ToolStripDropDown.OnItemClicked`.
- [ ] [ContextMenuStrip] Right-click embedded CMD tab, close it. No disposed
  ContextMenuStrip or additional CMD window.
- [ ] [Zero-doc] Close sole file tab. A fresh empty unsaved Untitled tab opens.
  File -> Close All has the same guarantee; no infinite close loop.
- [ ] [Exit/restart] Type unsaved content in blank Untitled, exit using X,
  reopen SourceCodeEditor: text, dirty marker, caret, and tab recovered.
- [ ] [Exit/restart] Modify already-saved code file, exit without saving,
  restart. Content is recovered but the original disk file is NOT overwritten.
- [ ] [Close/reopen] Close a dirty tab, select No (retain for recovery), exit,
  restart. It appears reopened automatically (default setting).
- [ ] [Save/close/reopen] Close a clean saved file, exit, restart: it reopens.
  Repeat with startup option disabled: entry stays in Closed Files list.
- [ ] [Missing file] Close a saved file, remove/move parent folder, restart:
  cached text is in an unsaved recovered tab and original folder stays gone.
- [ ] [Duplicate protection] Corrupted session listing same path twice does
  not give two write-capable editor tabs for the same source path.
- [ ] [Session error] Simulate denied session write in isolated test account;
  exiting with unsaved data displays warning and defaults to cancel exit.
- [ ] [Shell-only] With CMD tabs open, close last document: a blank editor
  tab exists in addition to the shell. Close shell: blank editor still exists.
- [ ] [Crash diagnostics] A triggered unexpected WinForms exception records
  `%LOCALAPPDATA%\SourceCodeEditor\errors.log` without recursive failures.

This checklist requires interactive Windows execution; the Linux authoring
environment does not execute the GUI or ConPTY.
