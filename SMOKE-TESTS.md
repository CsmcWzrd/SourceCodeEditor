# Manual smoke test checklist (Windows x64)

Build or run an installer produced by the provided build script / Windows CI.

1. Launch the application. Confirm **File Explorer | Opened Files | Closed Files | Unsaved Files** all display in the LEFT navigator. Click each tab, confirm only one level in the three document lists.
2. Ensure explorer tree starts below tabs and below compact Browse/Refresh toolbar. No large Up/Shell buttons.
3. Open two `.cs` files. Confirm TWO distinct, clickable editor tabs on the RIGHT, with visible × close affordances.
4. Double-click a file in Opened Files; verify its tab activates. Close it; verify it appears under Closed Files. Double-click Closed Files; verify reopen.
5. Create new document, type unsaved text, confirm under Unsaved Files. Save As; ensure it disappears from Unsaved Files. Modify; confirm it reappears.
6. Open a 10,000-line file. Use Ctrl+mouse-wheel up/down. Confirm left line numbers change size and remain aligned with code text. Test 50%, 100%, 250%, 400% zoom and Ctrl+0 reset via menu.
7. Expand explorer directory tree; right-click a file or folder, choose `Open shell here`; verify cmd.exe's working directory. No permanent Shell toolbar button.
8. Open a Python file with `#` comments, a C# file with `//` comments and string with URL, SQL, and CMotive files. Verify basic syntax colors.
9. Search > Find, Replace, Find All with case, whole-word, regex and backward. Find in Files with masks and recursion. Double-click results.
10. Change EOL to LF and encoding to UTF-8 BOM. Save and reopen; verify EOL/BOM. Test UTF-16 LE/BE and UTF-32 LE/BE round trips with non-ASCII characters.
11. Modify a file externally (Notepad). Verify change detection prompts within ~5 seconds and editor reloads when accepted.
12. Create dirty and untitled buffers; keep the app open for >10s; force terminate in Task Manager; restart; verify recovery of unsaved buffers.
13. Build x64 Release using `SourceCodeEditor.sln`; run `build-x64.cmd` with Inno Setup installed; install/uninstall the generated x64 installer.

These are **test instructions**, not claims that the checks have passed.
