# Run after build-x64.cmd (or a Release x64 publish).
# Ensures the CMD process is genuinely attached to SourceCodeEditor's own
# ConPTY pipes rather than an external Windows Terminal window.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'dist\win-x64\SourceCodeEditor.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Published x64 application not found: $exe. Run build-x64.cmd first."
}
$report = Join-Path ([System.IO.Path]::GetTempPath()) 'SourceCodeEditor-ConPTY-smoke.txt'
Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue
$arguments = '--self-test-conpty "' + $report + '"'
$process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -Wait
if (Test-Path -LiteralPath $report) {
    Get-Content -LiteralPath $report
} else {
    Write-Warning "Self-test report was not created: $report"
}
if ($process.ExitCode -ne 0) {
    throw "ConPTY did not attach correctly (exit code $($process.ExitCode)). Check the report and terminal startup code."
}
Write-Host 'ConPTY attachment smoke test passed.' -ForegroundColor Green
