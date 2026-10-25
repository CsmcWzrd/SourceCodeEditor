# SourceCodeEditor 0.5.3 — test execution report

Date: 2026-10-25

## What was executed in this environment

| Check | Result | Evidence |
|---|---|---|
| Exactly one solution, one project, explicit source references | PASS | `tests/OFFLINE-CHECK-RESULTS.json` |
| Offline source, syntax-delimiter, feature wiring and project checks | PASS | `tests/OFFLINE-CHECK-RESULTS.json` |
| Linux filesystem fixtures: deleted directory, renamed directory, same-size/timestamp write, hidden file | PASS | `tests/OFFLINE-CHECK-RESULTS.json` |
| Offline aggregate | **79 / 79 PASS** | `tests/OFFLINE-CHECK-RESULTS.json` |
| Native C# headless test definitions | **110 distinct cases included; not executed** | `NegativeSelfTest.cs` |

**Important:** Source/delimiter checks and Linux filesystem fixtures are not a C# compilation or Windows Forms runtime tests. The Linux environment has no installed .NET SDK, Windows Forms runtime or GUI host. Consequently native C# tests, installer creation, and the WinForms/ConPTY UI have not been run here.

## Corrected from supplied exception log

The uploaded log (`Pasted text(1).txt`) identifies an
`ObjectDisposedException` with `Object name: 'System.Windows.Forms.ContextMenuStrip'` from `ToolStripDropDown.OnItemClicked`.
The previous release registered `menu.Closed += (_, _) => menu.Dispose()` for document and shell context menus. WinForms could still be processing the menu click when the menu was destroyed. Version 0.5.3 changes both to form-owned reusable context menus. They are not disposed on the `Closed` event. This is a source-level correction, **not a verified Windows UI test pass**.

## Startup and data-preservation changes

- Every empty editor state creates an unsaved `Untitled N` document; `Close All` cannot leave the source editor without a document.
- `LineNumberedRichTextBox` explicitly sets Consolas 12 pt `FontStyle.Regular` on the entire loaded RTF text and at the empty caret; RTF token colors are retained.
- Session restoration includes opened clean/dirty documents, closed clean/dirty documents, untitled buffers, caret/zoom/encoding/EOL/bookmarks, and recent files.
- "Reopen closed files at startup" defaults ON; it can be disabled under `File -> Startup Session` to preserve entries in Closed Files instead of opening them all.
- Closing a dirty document: **No** retains its unsaved buffer in the Closed Files history, rather than discarding it. The user may remove that entry explicitly.
- Exiting the application saves the recovery session before destroying editor tabs. It does **not** automatically write unsaved buffers to the original files. If saving the session fails, default is to cancel the exit.
- Session writes are atomic and rejected if the resulting UTF-8 JSON exceeds the 128 MiB load limit, rather than claiming to have created an unrestorable snapshot.
- Unexpected UI exceptions are logged to `%LOCALAPPDATA%\SourceCodeEditor\errors.log` for diagnosis.

## Native Windows tests included but not executed

| Test group | Status | Command / location |
|---|---|---|
| Windows compilation Release x64 | NOT RUN | Visual Studio 2022 -> `SourceCodeEditor.sln` or `dotnet build -c Release -p:Platform=x64` |
| 110 C# negative regression cases | NOT RUN | `negative-smoke.ps1` (after publish); JSON report in `%TEMP%` |
| Embedded ConPTY self-test | NOT RUN | `conpty-smoke.ps1` |
| WinForms context menu disposal regression | NOT RUN | `SMOKE-TESTS.md` interactive checklist |
| Regular 12 pt RTF font and zoom | NOT RUN | `SMOKE-TESTS.md` interactive checklist |
| Restart/exit and all four file states | NOT RUN | `SMOKE-TESTS.md` interactive checklist |
| Installer compile/install/uninstall | NOT RUN | `build-x64.cmd`, Inno Setup 6, Windows x64 QA |

## Remaining risks

- Windows UI and ConPTY require actual execution on Windows; no Windows installer binary is included.
- A crash can lose edits made after the latest three-second session snapshot.
- A 128 MiB session limit requires very large/voluminous dirty documents to be saved individually.
- Filesystem changes between last disk preflight and write are not serialized across processes.
- Very large files, transient network drives and reparse-point-heavy folders still need performance testing.
- Fatal background-process failures are logged, but cannot safely be continued after the runtime has deemed them fatal.

**Release status:** source development release, not Windows-validated production build.
