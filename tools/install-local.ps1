<#
.SYNOPSIS
    Build ShrinkRay from this checkout and install it for the current user.

.DESCRIPTION
    Publishes the app in Release, mirrors it into %LOCALAPPDATA%\Programs\ShrinkRay
    and adds a Start menu shortcut. Run it again after pulling to pick up new
    changes; no version bump or release is involved.

    The install folder holds program files only. Settings, the swap journal, the
    probe cache and the ledger live in %LOCALAPPDATA%\ShrinkRay, which this script
    never touches, so a reinstall keeps them.

    The script refuses to run while ShrinkRay is open, because replacing the app
    mid-encode would kill a run that may be swapping files on the library.

.PARAMETER Launch
    Start ShrinkRay once the install finishes.
#>
param([switch]$Launch)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\ShrinkRay.App\ShrinkRay.App.csproj"
$staging = Join-Path $root "artifacts\local-install"
$target = Join-Path $env:LOCALAPPDATA "Programs\ShrinkRay"
$exe = Join-Path $target "ShrinkRay.exe"

$running = Get-Process -Name ShrinkRay -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "ShrinkRay is running (PID $($running.Id -join ', ')). Close it first; it may be in the middle of an encode." -ForegroundColor Yellow
    exit 1
}

$commit = (git -C $root rev-parse --short HEAD).Trim()
$branch = (git -C $root branch --show-current).Trim()
$dirty = [bool](git -C $root status --porcelain)
$label = if ($dirty) { "$commit with uncommitted changes" } else { $commit }

Write-Host "Publishing ShrinkRay from $branch at $label..." -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
# SourceRevisionId lands in the assembly's informational version, so the installed
# exe records which commit it came from.
dotnet publish $project -c Release -o $staging --nologo -p:SourceRevisionId=$commit
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# /MIR removes files a newer build no longer ships. Robocopy exit codes below 8 mean success.
robocopy $staging $target /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    Write-Error "Copying into $target failed (robocopy exit $LASTEXITCODE)."
    exit 1
}

Set-Content -Path (Join-Path $target "install.txt") -Encoding ASCII -Value @(
    "branch: $branch"
    "commit: $label"
    "installed: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
)

$shortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "ShrinkRay.lnk"
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.Description = "Re-encode a Plex TV library to H.265"
$link.Save()

Write-Host "Installed $label to $target. Start menu: ShrinkRay." -ForegroundColor Green

if ($Launch) { Start-Process $exe -WorkingDirectory $target }
exit 0
