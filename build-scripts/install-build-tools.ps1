[CmdletBinding()]
param(
    [switch]$ResolveOnly
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$installRoot = Join-Path $env:USERPROFILE "msys64"
$bash = Join-Path $installRoot "usr\bin\bash.exe"
$installer = Join-Path $env:TEMP "msys2-base-x86_64.sfx.exe"

function Get-Msys2ReleaseAssets {
    $headers = @{
        Accept = "application/vnd.github+json"
        "User-Agent" = "qpdf-gui-build-tools"
    }
    $release = Invoke-RestMethod `
        -Uri "https://api.github.com/repos/msys2/msys2-installer/releases/latest" `
        -Headers $headers

    $installerAsset = $release.assets |
        Where-Object { $_.name -match '^msys2-base-x86_64-(latest|\d+)\.sfx\.exe$' } |
        Select-Object -First 1
    if (-not $installerAsset) {
        throw "The latest MSYS2 release does not contain an x86-64 base SFX installer."
    }

    if ($installerAsset.digest -notmatch '^sha256:([0-9a-fA-F]{64})$') {
        throw "The official MSYS2 release metadata does not contain a SHA-256 digest for $($installerAsset.name)."
    }

    [pscustomobject]@{
        InstallerUrl = $installerAsset.browser_download_url
        Sha256 = $Matches[1]
    }
}

function Invoke-MsysCommand {
    param([Parameter(Mandatory)][string]$Command)

    & $bash -lc $Command
    if ($LASTEXITCODE -ne 0) {
        throw "MSYS2 command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

if ($ResolveOnly) {
    $assets = Get-Msys2ReleaseAssets
    Write-Host "Installer: $($assets.InstallerUrl)"
    Write-Host "SHA-256:  $($assets.Sha256)"
    return
}

if (-not (Test-Path -LiteralPath $bash)) {
    Write-Host "Downloading the MSYS2 base environment..."
    $assets = Get-Msys2ReleaseAssets
    Invoke-WebRequest -Uri $assets.InstallerUrl -OutFile $installer

    $actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    if (-not $actualHash.Equals($assets.Sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The MSYS2 installer checksum does not match the digest in the official release metadata."
    }

    Write-Host "Installing MSYS2 in $installRoot..."
    & $installer -y "-o$env:USERPROFILE"
    if ($LASTEXITCODE -ne 0) {
        throw "The MSYS2 base installer failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $bash)) {
        throw "MSYS2 extraction completed, but $bash was not found."
    }
}
else {
    Write-Host "Using the existing MSYS2 installation at $installRoot."
}

Write-Host "Updating the MSYS2 base environment..."
Invoke-MsysCommand "pacman -Syu --noconfirm"
Invoke-MsysCommand "pacman -Syu --noconfirm"

$packages = @(
    "mingw-w64-ucrt-x86_64-gcc"
    "mingw-w64-ucrt-x86_64-cmake"
    "mingw-w64-ucrt-x86_64-ninja"
    "mingw-w64-ucrt-x86_64-zlib"
    "mingw-w64-ucrt-x86_64-libjpeg-turbo"
    "mingw-w64-ucrt-x86_64-openssl"
)

Write-Host "Installing the minimal qpdf build toolchain..."
Invoke-MsysCommand "pacman -S --needed --noconfirm $($packages -join ' ')"

Write-Host "Verifying installed tools..."
Invoke-MsysCommand 'export PATH=/ucrt64/bin:$PATH; gcc --version; cmake --version; ninja --version'

Write-Host ""
Write-Host "qpdf build tools are ready."
Write-Host "MSYS2 root: $installRoot"
