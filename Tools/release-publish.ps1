# Written with Claude Code (AI), directed and tested by team MARSXR
# Walkback release, steps 7-10 of audit R20 (F2/RELEASE-COMANDOS.ps1.md): notes, annotated tag, push of the tag,
# GitHub release with the APK, and a check as someone without a gh session sees it.
# It PUBLISHES: run it only after the APK check (Tools\check-apk.ps1), a pass in the headset and the team's yes
# for the tag and the release. The tag is never moved: the script stops if it already exists on GitHub.
# Usage (from anywhere):
#   powershell -NoProfile -ExecutionPolicy Bypass -File <project>\Tools\release-publish.ps1 -Commit <sha> -Checks <n> -NotesTemplate <path>
# -NotesTemplate: the release notes in Markdown with the placeholders {{SHA256}}, {{COMMIT}}, {{SIZE}} and {{CHECKS}}.
# -Repo: owner/name on GitHub; default read from the origin remote.
param(
  [Parameter(Mandatory = $true)][string]$Commit,
  [Parameter(Mandatory = $true)][string]$Checks,
  [Parameter(Mandatory = $true)][string]$NotesTemplate,
  [string]$Repo = '',
  [string]$Tag = 'v1.0-submission',
  [string]$Asset = 'Builds\Walkback-v1.0-submission.apk'
)
$ErrorActionPreference = 'Continue'
$proj = Split-Path -Parent $PSScriptRoot
function Now { Get-Date -Format 'HH:mm:ss' }

$NotesTemplate = if (Test-Path $NotesTemplate) { (Resolve-Path $NotesTemplate).Path } else { '' }
Set-Location $proj
if (-not $NotesTemplate) { "[$(Now)] STOP: the notes template does not exist"; exit 5 }
if (-not (Test-Path $Asset)) { "[$(Now)] STOP: $Asset does not exist"; exit 2 }
if (-not $Repo) {
  $url = (git remote get-url origin).Trim()
  $m = [regex]::Match($url, 'github\.com[:/]+([^/]+/[^/]+?)(\.git)?/?$')
  if (-not $m.Success) { "[$(Now)] STOP: the origin remote is not a GitHub repository (pass -Repo owner/name)"; exit 6 }
  $Repo = $m.Groups[1].Value
}
if ((git ls-remote --tags origin $Tag)) { "[$(Now)] STOP: the tag already exists on GitHub (it is never moved)"; exit 3 }
$full = (git rev-parse $Commit).Trim()
$sha  = (Get-FileHash $Asset -Algorithm SHA256).Hash.ToLower()
$size = '{0:N1} MB' -f ((Get-Item $Asset).Length / 1MB)
$name = Split-Path $Asset -Leaf

# 7. Notes
$notes = Join-Path $env:TEMP 'walkback-release-notes.md'
(Get-Content $NotesTemplate -Raw -Encoding UTF8).
  Replace('{{SHA256}}', $sha).Replace('{{COMMIT}}', $full.Substring(0, 7)).Replace('{{SIZE}}', $size).Replace('{{CHECKS}}', $Checks) |
  Set-Content $notes -Encoding utf8
if (Select-String $notes -Pattern '\{\{') { "[$(Now)] STOP: a placeholder is left in the notes"; exit 4 }
"[$(Now)] notes ready: $Repo | commit $($full.Substring(0,7)) | $size | sha256 $sha | $Checks checks"

# 8. Annotated tag on the commit the APK was built from, then push
git tag -a $Tag $full -m "Walkback v1.0: NASA Space Apps 2026 submission (team MARSXR)"
"[$(Now)] tag type: $((git cat-file -t $Tag).Trim()) -> $((git rev-parse "$Tag^{commit}").Trim().Substring(0,7))"
git push origin $Tag 2>&1 | ForEach-Object { "  $_" }
"[$(Now)] tags on GitHub: " + ((git ls-remote --tags origin) -join ' | ')

# 9. Release with the APK
gh release create $Tag $Asset --repo $Repo --verify-tag --latest --title 'Walkback v1.0 (NASA Space Apps 2026 submission)' --notes-file $notes 2>&1 | ForEach-Object { "  $_" }

# 10. Check, as someone without an account sees it
gh release view $Tag --repo $Repo --json tagName,isDraft,isPrerelease,assets --jq '{tagName, isDraft, isPrerelease, assets: [.assets[] | {name, size}]}'
"[$(Now)] release page: " + (curl.exe -s -o NUL -w '%{http_code}' "https://github.com/$Repo/releases/tag/$Tag")
$chk = Join-Path $env:TEMP 'walkback-check.apk'
curl.exe -sL -o $chk "https://github.com/$Repo/releases/download/$Tag/$name"
$dl = (Get-FileHash $chk -Algorithm SHA256).Hash.ToLower()
"[$(Now)] downloaded without a session: sha256 matches = $($dl -eq $sha)"
"[$(Now)] RELEASE: tag $Tag -> $($full.Substring(0,7)) | $name | $size | sha256 $sha | test $Checks of $Checks"
