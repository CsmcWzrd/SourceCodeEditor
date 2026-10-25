# Windows x64 build — SourceCodeEditor 0.5.3

This project contains exactly one solution (`SourceCodeEditor.sln`) and one
project (`SourceCodeEditor.csproj`). It explicitly lists compiled source files
to avoid duplicate generated assembly attributes when copying folders.

1. Visual Studio 2022 / .NET 8 Desktop SDK: open `SourceCodeEditor.sln`, build
   `Release|x64`. Run the built `SourceCodeEditor.exe`.
2. Run `build-x64.cmd` to publish a self-contained `win-x64` executable in
   `dist/win-x64`.
3. Install Inno Setup 6 (ISCC.exe) to generate
   `dist/SourceCodeEditor-Setup-x64.exe` from `installer.iss`.
4. Windows runner GitHub Actions workflow is under `.github/workflows/`.

No Windows compilation or GUI test was available in the authoring environment.
The package contains source plus build/installer scripts, not a compiled EXE.

## Terminal requirement

The interactive shell uses native Windows ConPTY (Windows 10 version 1809+
and Windows 11). The shell is part of the WinForms project, not a NuGet
terminal package. Test the ConPTY interop and key bindings on Windows with
`SMOKE-TESTS.md` before distributing the installer.

## ConPTY attachment verification (important on Windows 11)

After publishing, run `powershell -ExecutionPolicy Bypass -File conpty-smoke.ps1`.
The test invokes the same ConPTY backend as the embedded shell without a GUI,
reads its own output pipe, and fails if CMD has not attached properly.
It also runs in the supplied Windows x64 GitHub Actions workflow before building
the installer. The Windows GUI test is still required afterward.

## Mandatory negative-test gate (0.5.2)

`build-x64.cmd` now runs `negative-smoke.ps1` after a successful publish and
BEFORE Inno Setup. A failure aborts the build without generating an installer.
Run the test separately on Windows with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File negative-smoke.ps1
```

The command invokes `SourceCodeEditor.exe --self-test-negative` and writes a
machine-readable report to `%TEMP%\SourceCodeEditor-negative-tests.json`.
It creates and destroys only new test fixtures under `%TEMP%`; it never deletes
real user's files or modifies `%LOCALAPPDATA%\SourceCodeEditor\session.json`.

The supplied Windows GitHub Actions workflow runs the negative suite and the
ConPTY self-test and archives their output. An interactive Windows GUI manual
pass is still required, as CI does not provide a desktop user session.
See `NEGATIVE-TEST-MATRIX.md` for cases and expected outcomes.

## SourceCodeEditor v0.5.3 functional regression checklist

After compiling, execute `negative-smoke.ps1` to run the extended headless
file/session tests. Before releasing an installer, also test on Windows:

1. With only a blank `Untitled 1`, type a few characters and check that the
   text is Consolas **Regular** (not Bold) at 12 pt; repeat on a `.cs` file
   with syntax coloring and after View -> change editor font.
2. Right-click a document entry, select Close, Open/Activate, Close others,
   and Remove from Closed Files. No ObjectDisposedException/JIT dialog should
   appear. Repeat with a CMD editor tab context menu.
3. Close the last document tab, including from a right-click: a new empty
   Untitled tab must appear. Repeat after File -> Close All.
4. Type unsaved text, exit normally without Save, restart: the original
   untitled/dirty buffer is restored, with its contents. Verify saved open
   documents, and clean/dirty files that were closed before exit.
5. In `File -> Startup Session`, turn OFF automatic reopening of closed files,
   restart: Closed Files entries stay in the navigation list. Double-click
   one to reopen it. Turn ON and restart to have them reopen automatically.
6. Delete the parent folder of a saved closed file. Restart: recovery must
   retain the cached data in an untitled recovered buffer and Save must
   require a new path, not recreate the old folder.
7. Make `%LOCALAPPDATA%\SourceCodeEditor` inaccessible in a **test-only**
   Windows account. Attempt to exit with dirty text: the editor must warn and
   allow cancelling exit. Restore the directory before exiting.

The test report must distinguish source checks from an actually compiled and
executed Windows GUI/ConPTY test.
