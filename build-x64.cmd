@echo off
setlocal enableextensions
cd /d "%~dp0"
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"

echo === Publishing SourceCodeEditor x64 ===
dotnet build "SourceCodeEditor.sln" -c Release -p:Platform=x64 -p:EnableWindowsTargeting=true
if errorlevel 1 exit /b 1
dotnet publish "SourceCodeEditor.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o "dist\win-x64"
if errorlevel 1 exit /b 1

set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ISCC_PATH%" goto found_inno
where iscc >nul 2>nul
if errorlevel 1 (
    echo ERROR: Inno Setup 6 not found. Application published, installer not generated.
    echo Install Inno Setup 6 or add ISCC.exe to PATH, then rerun this script.
    exit /b 2
)
set "ISCC_PATH=iscc"
:found_inno
echo === Building SourceCodeEditor x64 installer ===
"%ISCC_PATH%" "installer.iss"
if errorlevel 1 exit /b 1
echo Installer: dist\SourceCodeEditor-Setup-x64.exe
exit /b 0
