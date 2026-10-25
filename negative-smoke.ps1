# Run in Windows PowerShell 5.1+ after build-x64.cmd or dotnet publish.
# The executable performs destructive tests ONLY inside its own unique %TEMP% folder.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $here 'dist\win-x64\SourceCodeEditor.exe'
if (-not (Test-Path $exe)) {
    $exe = Join-Path $here 'bin\Release\net8.0-windows\win-x64\SourceCodeEditor.exe'
}
if (-not (Test-Path $exe)) {
    throw "SourceCodeEditor.exe not found. Run build-x64.cmd first."
}
$report = Join-Path $env:TEMP 'SourceCodeEditor-negative-tests.json'
if (Test-Path $report) { Remove-Item $report -Force }
Write-Host "Running negative tests using $exe"
$p = Start-Process -FilePath $exe -ArgumentList @('--self-test-negative', ('"' + $report + '"')) -PassThru -Wait
if (-not (Test-Path $report)) { throw "Self-test did not write a report; exit code $($p.ExitCode)" }
$data = Get-Content $report -Raw | ConvertFrom-Json
Write-Host "Completed: $($data.Passed)/$($data.Total) passed; $($data.Failed) failed."
$data.Tests | Where-Object { -not $_.Passed } | ForEach-Object { Write-Host "FAIL $($_.Name): $($_.Message)" }
Write-Host "JSON report: $report"
if ($p.ExitCode -ne 0 -or $data.Failed -ne 0) {
    throw "SourceCodeEditor negative suite failed. Inspect $report"
}
