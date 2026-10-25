# SourceCodeEditor 0.5.2 — test execution report

Date: 2026-10-25

## Executed in this authoring environment

| Check | Result | Evidence |
|---|---|---|
| Source/project layout | PASS | Exactly one `.sln`, one `.csproj`, 18 explicitly compiled C# files |
| Offline structural checks | PASS — 65 / 65 | `tests/OFFLINE-CHECK-RESULTS.json` |
| Temporary fixture: cached closed file after directory deletion | PASS | Same offline JSON; verifies fixture state, not WinForms |
| Temporary fixture: parent directory renamed | PASS | Same offline JSON; verifies fixture state, not WinForms |
| Temporary fixture: same-size/same-timestamp rewrite | PASS | Same offline JSON; verifies fixture state, not C# hash implementation |
| Temporary fixture: hidden source file | PASS | Same offline JSON; verifies fixture state, not WinForms |
| C# source delimiter structure | PASS | Offline checks lexical analysis with Python Pygments |

These **65 checks include source structure, guards, C# delimiter balance and four temporary filesystem fixtures**. They do not compile C# or execute native Windows code.

## Implemented but NOT executable in this environment

| Test group | Status | How to run |
|---|---|---|
| Native .NET negative self-tests | NOT RUN; 91 declared call sites, ~96 runtime cases including encoding/EOL loops | `powershell -NoProfile -ExecutionPolicy Bypass -File negative-smoke.ps1` on Windows after publishing |
| ConPTY attachment self-test | NOT RUN | `powershell -ExecutionPolicy Bypass -File conpty-smoke.ps1` |
| Visual Studio 2022 compilation, Release x64 | NOT RUN | Build `SourceCodeEditor.sln` |
| Interactive WinForms GUI manual matrix | NOT RUN | `NEGATIVE-TEST-MATRIX.md` |
| Inno Setup installer build/install/uninstall | NOT RUN | `build-x64.cmd` and installer QA on Windows x64 |

## Source-review defects corrected in 0.5.2 (not claimed runtime-verified)

1. Closed Files could reopen cached text with a stale original filename, allowing Save to recreate a deleted file when the directory still existed. Now prompts to recover to a detached unsaved document instead.
2. The original save path could be removed or become inaccessible while editing, then fail or overwrite data unexpectedly. Now preflight checks and explicit Save As behavior protect the buffer.
3. External change tracking previously updated the document's baseline after a declined reload, allowing subsequent saves to overwrite the external change without another warning. Now a conflict flag persists until deliberate resolution.
4. Files rewritten with unchanged length/time were undetected. Now SHA-256 is tracked and checked before save.
5. Malformed session JSON was at risk of being overwritten by the next autosave. Now it is copied to a timestamped rescue file first; session writes are disabled if rescue fails.
6. Duplicate file paths injected through a restored session could cause two editor documents to own the same file. Now duplicate entries are detached to recovered buffers.
7. Binary files renamed with source extensions could appear in the custom explorer. Now their content is probed before display.
8. An explicitly discarded dirty document could encounter an unhandled read error when the disk path had become unreadable. The close flow now catches the failure.

## Release gate

The build/publish script and Windows CI workflow are configured so a failing native negative test **prevents creation of an installer**. Because Windows testing has not run here, **the package is a development source release, not a production-qualified or precompiled installer**.

## Residual unresolved scenarios

- Small race between final hash check and replace if another process writes concurrently.
- Session snapshots every 10 seconds leave a possible crash-recovery gap.
- Large files, huge directories, offline network shares and junction-heavy trees can still impact UI responsiveness.
- Complex terminal VT/TUI programs and actual Windows ConPTY process cleanup remain unqualified on this host.
- Source-code lexical checks are not a substitute for an actual C# compiler and Windows integration tests.
