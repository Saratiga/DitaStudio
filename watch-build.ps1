# Build watcher. ASCII only: PowerShell 5.1 reads UTF-8 files as CP1251.
# Start once, keep the window open:
#   powershell -ExecutionPolicy Bypass -File C:\Users\vova1\source\DitaStudio\watch-build.ps1

$root = $PSScriptRoot
$sln  = Join-Path $root 'DitaStudio.sln'
$req  = Join-Path $root 'build.request'
$log  = Join-Path $root 'build.log'
$tlog = Join-Path $root 'test.log'
$done = Join-Path $root 'build.done'

Write-Host "Build watcher: $root"
Write-Host "Waiting for build.request. Ctrl+C to stop."

while ($true) {
    if (Test-Path $req) {
        Remove-Item $req -Force -ErrorAction SilentlyContinue
        Remove-Item $done -Force -ErrorAction SilentlyContinue

        Write-Host ("[{0}] building..." -f (Get-Date -Format 'HH:mm:ss'))

        & dotnet build $sln -c Debug --nologo -v minimal 2>&1 | Out-File $log -Encoding utf8
        $code = $LASTEXITCODE
        "EXITCODE=$code" | Out-File $log -Append -Encoding utf8

        if ($code -eq 0) {
            & dotnet run --project (Join-Path $root 'tests\DitaStudio.Tests') --nologo 2>&1 | Out-File $tlog -Encoding utf8
            "EXITCODE=$LASTEXITCODE" | Out-File $tlog -Append -Encoding utf8
            Write-Host "  OK, tests done"
        } else {
            Write-Host "  BUILD FAILED, see build.log"
        }

        "$code" | Out-File $done -Encoding utf8
    }

    Start-Sleep -Milliseconds 1500
}
