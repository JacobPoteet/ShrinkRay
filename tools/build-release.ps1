<#
.SYNOPSIS
    Build the release downloads: a zip and, with -Installer, the Inno Setup installer.

.DESCRIPTION
    Publishes ShrinkRay for win-x64 with the .NET runtime included, so users install no
    runtime, and writes the downloads to artifacts\release. The release workflow runs this
    script; run it locally to check what a release will hold.

.PARAMETER Version
    Defaults to <Version> in Directory.Build.props.

.PARAMETER Installer
    Also compile installer\ShrinkRay.iss. Needs Inno Setup 6.
#>
param(
    [string]$Version,
    [switch]$Installer
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
    $Version = "$($props.Project.PropertyGroup.Version)".Trim()
}
if (-not $Version) { throw "No <Version> in Directory.Build.props, and no -Version given." }

$out = Join-Path $root "artifacts\release"
$publish = Join-Path $out "publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root "src\ShrinkRay.App\ShrinkRay.App.csproj") -c Release -r win-x64 `
    --self-contained --nologo -o $publish "-p:Version=$Version" -p:DebugType=none -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The libraries' XML documentation is for editors, not for users.
Remove-Item (Join-Path $publish "ShrinkRay*.xml")
Copy-Item (Join-Path $root "LICENSE") (Join-Path $publish "LICENSE.txt")

$zip = Join-Path $out "ShrinkRay-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip
Write-Host "Zip: $zip"

if ($Installer) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }

    & $iscc "/DAppVersion=$Version" /Qp (Join-Path $root "installer\ShrinkRay.iss")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Installer: $(Join-Path $out "ShrinkRay-Setup-$Version.exe")"
}
exit 0
