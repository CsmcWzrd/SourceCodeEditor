# SourceCodeEditor 0.4.0 — Windows x64 smoke-test checklist

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
    Full-screen console tools are not supported by the stream-based terminal.

## Existing editor regression

13. Open two text/source files; verify separate closeable editor tabs and
    source syntax formatting. Ctrl+mouse wheel scales line numbers and editor.
14. Verify Opened/Closed/Unsaved left tabs and unsaved close prompt.
15. Find/Replace, Find in Files, line editing, bookmarks, encodings, EOLs and
    session recovery continue working.
16. Build `Release | x64` in the **single** `SourceCodeEditor.sln` and run
    `build-x64.cmd` with Inno Setup installed. Check generated installer and
    clean install/uninstall on Windows x64.
