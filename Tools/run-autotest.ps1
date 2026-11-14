# Written with Claude Code (AI), directed and tested by team MARSXR
# Runs the automatic test inside the OPEN Unity editor, one caller at a time (a lock folder in Temp/).
# Usage (from anywhere):
#   powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\kevin\walkback-NASA-Space-Apps\Tools\run-autotest.ps1
# Prints compile errors that appeared during the run (if any), then TestOutput/report.txt.
# Exit code: 0 = PASS, 1 = FAIL, 2 = busy, timeout or Unity not open.
# With Unity closed use the batchmode command in CLAUDE.md instead.
param([int]$TimeoutSeconds = 360)

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$lock = Join-Path $root 'Temp/walkback-test-lock'
$log = Join-Path $env:LOCALAPPDATA 'Unity/Editor/Editor.log'
$report = Join-Path $root 'TestOutput/report.txt'
$start = Get-Date

# 1. Take the lock (a folder: creating it is atomic). A lock older than 6 min is stale.
while ($true) {
    try { New-Item -ItemType Directory -Path $lock -ErrorAction Stop | Out-Null; break }
    catch {
        $item = Get-Item $lock -ErrorAction SilentlyContinue
        if ($item -and ((Get-Date) - $item.CreationTime).TotalSeconds -gt 360) { Remove-Item $lock -Recurse -Force -ErrorAction SilentlyContinue; continue }
        if (((Get-Date) - $start).TotalSeconds -gt $TimeoutSeconds) { 'BUSY: another agent held the test lock for too long'; exit 2 }
        Start-Sleep -Seconds 3
    }
}

try {
    $unity = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like '*walkback-NASA-Space-Apps*' } | Select-Object -First 1
    if (-not $unity) { 'NO UNITY: the editor with this project is not open'; exit 2 }
    $logStart = (Get-Item $log).Length
    $t0 = Get-Date

    # 2. Ask the editor to run (AutoTest watches this file), and give it focus so it refreshes.
    New-Item -ItemType File -Force (Join-Path $root 'Temp/walkback-run-test') | Out-Null
    $null = (New-Object -ComObject WScript.Shell).AppActivate($unity.Id)

    # 3. Wait for a report newer than the request.
    while ($true) {
        $r = Get-Item $report -ErrorAction SilentlyContinue
        if ($r -and $r.LastWriteTime -gt $t0) { break }
        if (((Get-Date) - $t0).TotalSeconds -gt $TimeoutSeconds) { "TIMEOUT: no report after $TimeoutSeconds s (guion trap 21: the editor may be stuck)"; exit 2 }
        Start-Sleep -Seconds 2
    }
    Start-Sleep -Milliseconds 300

    # 4. Compile errors written to the editor log during this run.
    $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    $null = $fs.Seek($logStart, 'Begin')
    $reader = New-Object IO.StreamReader($fs)
    $tail = $reader.ReadToEnd()
    $reader.Close()
    $errors = $tail -split "`n" | Where-Object { $_ -match 'error CS\d+' } | ForEach-Object { $_.Trim() } | Sort-Object -Unique
    if ($errors) { 'COMPILE ERRORS (the report below then tests the OLD scripts):'; $errors | Select-Object -First 30; '' }

    Get-Content $report
    if ((Get-Content $report -TotalCount 2)[1] -like 'RESULT: PASS*') { exit 0 } else { exit 1 }
}
finally {
    Remove-Item $lock -Recurse -Force -ErrorAction SilentlyContinue
}
