# Written with Claude Code (AI), directed and tested by team MARSXR
# Checks an APK before a release (audit R20, F2 step 5). Prints no token and no IP address:
# the DevAgent token is only fingerprinted (first 10 hex of its SHA-256) and counted inside the APK.
# Usage (from anywhere):
#   powershell -NoProfile -ExecutionPolicy Bypass -File <project>\Tools\check-apk.ps1 [-Apk <path>] [-Aapt2 <path>] [-Unity <path>]
# -Apk: default Builds\walkback.apk; a relative path is read from the current folder, then from the project folder.
# -Aapt2: default the first aapt2.exe under Unity's Android SDK build-tools, then under $env:ANDROID_HOME / $env:ANDROID_SDK_ROOT.
# Reads Assets\Resources\DevAgentSettings.asset (not committed: the SDK writes the token into it) and the old
# public token from commit 740d2fe; it never writes anything.
param(
  [string]$Apk = 'Builds\walkback.apk',
  [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe',
  [string]$Aapt2 = '',
  [string]$ExpectedNew = '143594aa64',
  [string]$ExpectedOld = '3c8d886694'
)

$proj = Split-Path -Parent $PSScriptRoot
function Fp([string]$s) { if (-not $s) { return '(empty)' }; ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($s))) -replace '-','').Substring(0,10).ToLower() }

if (-not [IO.Path]::IsPathRooted($Apk) -and -not (Test-Path $Apk)) { $Apk = Join-Path $proj $Apk }
if (-not (Test-Path $Apk)) { "STOP: APK not found: $Apk"; exit 2 }
$Apk = (Resolve-Path $Apk).Path
$item = Get-Item $Apk
"APK: $Apk"
"  written $($item.LastWriteTime.ToString('dd/MM HH:mm:ss')), {0:N1} MB" -f ($item.Length / 1MB)

# 5a. Token: the new one in 1 entry, the old public one in 0 (matched case-sensitively; never printed)
$asset = Join-Path $proj 'Assets\Resources\DevAgentSettings.asset'
$new = ''; $ip = ''; $on = '(no asset)'
if (Test-Path $asset) {
  $tx  = Get-Content $asset -Raw
  $new = [regex]::Match($tx, '(?m)^\s*accessToken:[ \t]*(\S*)').Groups[1].Value
  $ip  = [regex]::Match($tx, '(?m)^\s*serverAddress:[ \t]*(\S*)').Groups[1].Value
  $on  = [regex]::Match($tx, '(?m)^\s*enabled:[ \t]*(\S*)').Groups[1].Value
  Remove-Variable tx
} else { "  $asset not found: the new token cannot be checked" }
$old = [regex]::Match(((git -C $proj show 740d2fe:Assets/Resources/DevAgentSettings.asset 2>$null) -join "`n"), '(?m)^\s*accessToken:[ \t]*(\S*)').Groups[1].Value
"  local token fingerprint $(Fp $new) (expected $ExpectedNew) | old public one $(Fp $old) (expected $ExpectedOld) | agent enabled: $on (expected 0)"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($Apk); $enc = [Text.Encoding]::GetEncoding(28591); $nNew = 0; $nOld = 0; $nIp = 0
foreach ($e in $z.Entries) {
  if ($e.Length -eq 0) { continue }
  $s = $e.Open(); $ms = New-Object IO.MemoryStream; $s.CopyTo($ms); $s.Dispose(); $b = $enc.GetString($ms.ToArray()); $ms.Dispose()
  if ($new -and $b.Contains($new)) { $nNew++ }; if ($old -and $b.Contains($old)) { $nOld++ }; if ($ip -and $b.Contains($ip)) { $nIp++ }
}
$z.Dispose()
"  in the APK -> new token: $nNew entries (expected 1) | old token: $nOld (expected 0) | laptop IP: $nIp"
Remove-Variable new, old, ip, b -ErrorAction SilentlyContinue

# 5b. Name, package, INTERNET permission
if (-not $Aapt2) {
  $roots = @((Join-Path (Split-Path $Unity) 'Data\PlaybackEngines\AndroidPlayer\SDK\build-tools'))
  foreach ($v in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)) { if ($v) { $roots += (Join-Path $v 'build-tools') } }
  foreach ($r in $roots) {
    if (-not (Test-Path $r)) { continue }
    $Aapt2 = Get-ChildItem $r -Recurse -Filter aapt2.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    if ($Aapt2) { break }
  }
}
if ($Aapt2 -and (Test-Path $Aapt2)) {
  "  aapt2: build-tools $(Split-Path (Split-Path $Aapt2 -Parent) -Leaf)"
  & $Aapt2 dump badging $Apk 2>$null | Select-String -Pattern "^package:|^application-label:|android.permission.INTERNET|debuggable" | ForEach-Object { "    " + $_.Line }
} else { "  aapt2 not found (pass -Aapt2): check INTERNET with adb dumpsys after installing" }

# 5c. SHA-256
"  SHA-256: " + (Get-FileHash $Apk -Algorithm SHA256).Hash.ToLower()
