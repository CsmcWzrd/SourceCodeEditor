# Windows x64 release procedure

Build machine: Windows 10/11 x64, Visual Studio 2022, .NET 8 SDK, Inno Setup 6.

1. Open SourceCodeEditor.sln, choose Release | x64, then Build > Rebuild Solution.
2. Run build-x64.cmd from a Developer Command Prompt or regular terminal where
   dotnet is on PATH. Inno Setup 6 may be installed normally at
   %ProgramFiles(x86)%\Inno Setup 6\ISCC.exe or its ISCC.exe added to PATH.
3. Check dist/win-x64/SourceCodeEditor.exe.
4. Check dist/SourceCodeEditor-Setup-x64.exe.
5. Test on Windows x64: opening multiple files, navigation headers, tabs, zoom
   synchronization, search, encoding round trips, session restore and installer
   uninstall/reinstall. Windows validation has NOT been performed in this runtime.

Do not run two SourceCodeEditor instances against the same session.json: the
most recent writer wins. Check that backups and session files are handled
appropriately for confidential source files.
