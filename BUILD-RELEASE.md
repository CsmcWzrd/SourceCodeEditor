# Windows x64 build — SourceCodeEditor 0.5.1

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
