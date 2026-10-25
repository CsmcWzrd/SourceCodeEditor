# Windows x64 build — SourceCodeEditor 0.4.1

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
