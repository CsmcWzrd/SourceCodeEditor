# SourceCodeEditor 0.5.2 — destructive/negative test matrix

## Test execution status and interpretation

- **Native .NET headless suite:** `NegativeSelfTest.cs` defines 91 test call sites (more individual assertions due to UTF encoding and EOL loops); it uses real `DocumentSafety`, `TextFiles`, `SessionFileStore`, `TextSourceFileFilter`. **Execution pending on Windows x64** because the authoring environment cannot compile or execute the WinForms application. Invoked by `negative-smoke.ps1`, by `build-x64.cmd` before installer generation, and by the included Windows CI workflow. Every test uses a unique `%TEMP%/SourceCodeEditorNegative_<GUID>` fixture, never production Documents or session data.
- **Offline source/fixture validation:** `python tests/offline_source_checks.py` ran here and produced `tests/OFFLINE-CHECK-RESULTS.json`. It validates source structure/guards and creates harmless temporary filesystem fixtures to exercise deletion, rename, same-metadata rewrites and hidden filenames. **It does not execute the C# application.**
- **Interactive UI / Windows OS compatibility cases:** remain **not executed** in this environment. A WinForms/ConPTY desktop session and actual Visual Studio 2022 build are required. User-visible exception paths must be manually verified.

Passing source checks or the headless suite is **not** proof that the GUI and installer work. There are infinitely many possible environmental combinations; this is a systematic risk-based matrix, not a claim of exhaustive coverage.

## What must happen when the folder of a Closed Files entry is removed?

1. The entry remains in **Closed Files** and is labelled `[missing]` after the status check.
2. Double-clicking the entry offers to open the last cached content **as an unsaved recovered file**; rejecting that prompt leaves the closed entry unchanged.
3. Accepting creates a normal document tab named `file.cs (Recovered)` with the original buffer intact. The old path is stored as recovery metadata but is no longer considered a save target.
4. Ctrl+S opens **Save As**, initially in Documents (or another existing parent), not the removed folder.
5. Cancel Save As: tab and contents remain intact and unsaved. Save to an existing folder: new file is created only at the selected path. The old missing folder is never silently recreated.
6. If SourceCodeEditor is restarted before that Save As, the recovered buffer remains recoverable in the session snapshot.

## Manual UI matrix (all statuses: pending Windows execution)

| ID | Negative scenario | Required behavior |
|---|---|---|
| F01 | Closed file deleted, directory exists | Cached text offered as recovered; Ctrl+S requires Save As |
| F02 | Closed file's parent folder deleted | Same as F01, no directory auto-creation |
| F03 | Closed file's grandparent removed | Same as F01; no startup crash |
| F04 | Closed file renamed | Closed old entry safely recovered; renamed path remains separate |
| F05 | Closed parent folder renamed | Closed history preserved, path marked missing |
| F06 | Closed path becomes a directory | Treat as inaccessible, never overwrite a directory |
| F07 | Closed file is replaced with new content | Clean closed entry reloads latest disk version |
| F08 | Dirty closed entry restored from session | Preserve unsaved snapshot, inspect disk conflicts |
| F09 | Closed inaccessible file | Offer cached content, do not assume file was deleted |
| F10 | Closed file on unplugged USB drive | No crash; history retained, recovery offered if cached |
| F11 | User rejects recovery | Closed entry remains selectable |
| F12 | User accepts recovery | Unsaved tab with complete old buffer appears |
| F13 | Recovered tab Save As cancelled | Buffer remains dirty, no file created |
| F14 | Recovered file saved to Documents | Saved tab receives new path and normal status |
| F15 | Empty closed file removed | Recovery still possible with empty contents |
| F16 | Unicode filename + emoji content removed | Filename and text preserved, Save As usable |
| F17 | Open file deleted during editing | Dirty/missing marker; Ctrl+S requires Save As |
| F18 | Open file's folder removed during editing | Same as F17, no directory recreation |
| F19 | Folder removed during Save As dialog | Save fails with error, buffer remains intact |
| F20 | External change between check and Save | Conflict detected or safe failure; no silent overwrite |
| F21 | External change, user selects No to reload | Buffer remains; dirty/conflict marker shown |
| F22 | External change, user selects Yes to reload | Disk text replaces editor only after explicit approval |
| F23 | External modification same size and timestamp | SHA-256 detects change on Save |
| F24 | External change then Save: choose Yes | Explicit overwrite, metadata refreshed |
| F25 | External change then Save: choose No | Save As, original version untouched |
| F26 | External change then Save: choose Cancel | Both files and buffer remain unchanged |
| F27 | Another external rewrite after overwrite approval | Save fails closed if bytes/stamp changed again |
| F28 | File removed and subsequently recreated elsewhere at same path | User must review conflict, no automatic merge |
| F29 | File locked exclusively by another program | Read/save safely fail; editor remains responsive |
| F30 | Read-only file Save | Error and retained buffer; no partial temp files |
| F31 | Invalid or too-long filename | Friendly error, no application termination |
| F32 | UNC path becomes offline | Failed operation is non-destructive |
| F33 | Two SourceCodeEditor instances editing the same file | Conflict identified before Save; concurrency race caveat documented |
| F34 | Two tabs with the same source path in corrupted session | Second is detached to recovered unsaved buffer |
| F35 | Save As targets path held by another tab | Refuse duplicate disk ownership |
| F36 | Save All, first file unavailable | Stops on failure without discarding following buffers |
| F37 | Backup save path unavailable | Warn without destroying original/dirty buffer |
| F38 | Reload when file is missing | Error; current text remains intact |
| F39 | Close dirty document then Cancel | Tab and edits remain |
| F40 | Close dirty document then Don't Save | Explicitly discard; no accidental reload crash |
| S01 | Session file contains malformed JSON | Preserve `.corrupt_*.json` copy; start new session safely |
| S02 | Session file contains `Documents:null` | Error reported; original safely backed up |
| S03 | Session file contains `[null]` document | Error reported; original safely backed up |
| S04 | Session file too large | Refuse unsafe restore, preserve original copy |
| S05 | Session file locked/unwritable | Normal text editing continues; no crash |
| S06 | Missing file listed in restored closed documents | History preserved, safe deferred recovery |
| S07 | Missing file listed in restored open documents | Detached recovered unsaved tab opens |
| S08 | Unsaved editor buffer on forced termination | Most recent session snapshot restored (10 s interval) |
| S09 | Rapid process crash before autosave tick | Data since last snapshot may be lost; known limitation |
| S10 | Session duplicate filenames with different cached text | Both buffers preserved, one detached |
| S11 | Session font size invalid/NaN | Defaults/clamps safely |
| S12 | Session navigation index out of range | Clamped to a valid navigation tab |
| S13 | Session relative/malformed path | No fatal startup exception, backup on corrupt record |
| S14 | Session file backup itself cannot be written | Disable subsequent session overwrite to protect original |
| E01 | `.cs` file actually binary bytes | Not shown in custom explorer; Open fails safely |
| E02 | Hidden/system text and source files | Listed when OS permits reading them |
| E03 | Invalid UTF-8 with BOM | Read fails rather than altering bytes silently |
| E04 | UTF-16 LE/BE, UTF-32 LE/BE, UTF-8 BOM | Correct round-trip and BOM preservation |
| E05 | Latin-1 document contains new unrepresentable Unicode | Save refuses instead of replacing with `?` |
| E06 | Mixed newline types converted to CRLF/LF/CR | Correct chosen line ending on disk |
| E07 | Save and reopen zero-byte file | Empty buffer, no crash |
| E08 | Binary filename with known text extension | Recognized as binary after probe |
| X01 | Remove selected folder before explorer expand | Placeholder/warning, no crash |
| X02 | Disconnect removable drive while explorer open | Refresh recovers gracefully |
| X03 | Expand junction/symlink loop | Navigation depth guard prevents unbounded expansion |
| X04 | Directory with 100,000+ entries | May be slow; responsiveness not yet qualified |
| X05 | Invalid address in explorer path field | Warning, previous navigation retained |
| X06 | Folder used for Shell disappears before launch | Warning, no blank shell tab |
| X07 | Shell starts in offline drive | No crash; shell not started |
| X08 | Multiple shells, close one while command running | One child process cleaned up, other tabs unaffected |
| X09 | ConPTY fails to attach | Error appears in shell tab; no detached Windows Terminal window |
| X10 | Exit CMD from inside terminal | End-of-session notice, no process leak |
| X11 | Terminal floods output then tab closes | UI remains responsive, worker threads stop |
| X12 | Shell starts from missing current-file location | Menu disabled/warning; no unexpected directory |
| U01 | Application font increased to max then resized small | Menus and navigation remain accessible |
| U02 | Ctrl+wheel zoom many times | Line-number gutter follows text baseline |
| U03 | Tab close when search window open | Search operations handle missing editor |
| U04 | Find-in-files folder deleted mid-search | Search cancels/skips unavailable directory |
| U05 | Regex pathological/catastrophic input | Timeout and error instead of GUI hang |
| U06 | Many opened/closed/unsaved tabs | No duplicate tree entries or tab crashes |
| U07 | Installer running on 32-bit Windows | x64-only installer must refuse installation |
| U08 | Installer interrupted/rollback | Existing user data and sessions not deleted |

## Native headless suite: how to run

1. Build `Release|x64` in Visual Studio 2022 and publish (`build-x64.cmd` automatically runs tests before producing the installer).
2. For a standalone test invocation: `powershell -NoProfile -ExecutionPolicy Bypass -File negative-smoke.ps1`.
3. Inspect `%TEMP%\SourceCodeEditor-negative-tests.json`. Every test includes a name and PASS/FAIL with a failure reason. The process exits nonzero on any failure.
4. For ConPTY separately run `powershell -ExecutionPolicy Bypass -File conpty-smoke.ps1`.
5. For manual GUI cases use a new non-critical test folder and test files, not irreplaceable source files. Only mark cases complete after observing the expected UI result on Windows.

## Known residual risks

- There is an unavoidable gap between the final on-disk checksum check and atomic replace when multiple processes can edit the same path concurrently; a true shared-lock or transactional scheme is needed for a strict no-overwrite guarantee.
- Normal session snapshots are periodic (10 seconds). Forced termination or power loss before the next snapshot can lose the most recent keystrokes.
- Very large files, offline network drives, huge directory listings and complex terminal applications are not fully qualified. Text files are currently read in full into memory.
- Windows GUI, terminal rendering and installer functionality **have not been executed here**; CI/self-tests must be run on Windows before shipping the application as production-ready.

## v0.5.3: new startup and UI safety cases

The native headless suite adds 19 cases for zero-document startup,
no-duplicate blank documents, opened/closed document distinctions, reopening
closed files (on/off), session preference JSON backward compatibility, exact
UTF-8/Unicode unsaved-buffer restoration, clean/dirty closed-file restoration,
missing closed-file recovery, and mixed-state session persistence. These
cases exercise the real C# session and filesystem model on Windows.

Interactive Windows-only tests additionally cover the toolbar/context-menu
`ObjectDisposedException`, 12 pt Regular RTF font, main-window exit recovery
without Save prompts, shell-tab closure, and blocked session writes. See
`SMOKE-TESTS.md` for step-by-step acceptance criteria.
