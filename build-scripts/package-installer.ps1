param(
    [string]$Version = "0.1.0",
    [string]$PublishedDirectory = "artifacts\QpdfDecryptor",
    [string]$OutputDirectory = "artifacts\release",
    [string]$QpdfRuntimeDirectory = "",
    [string]$StripExecutable = "",
    [string]$IsccPath = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishedPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $PublishedDirectory))
$outputPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$outputParent = Split-Path $outputPath -Parent
$outputName = Split-Path $outputPath -Leaf
$packageId = [Guid]::NewGuid().ToString("N")
$stagingDirectory = Join-Path $outputParent ".$outputName.staging-$packageId"
$backupDirectory = Join-Path $outputParent ".$outputName.backup-$packageId"
$installerDefinition = Join-Path $repositoryRoot "installer\QpdfDecryptor.iss"

$parsedVersion = [Version]$null
if (-not [Version]::TryParse($Version, [ref]$parsedVersion)) {
    throw "Version must be a numeric version such as 0.1.0."
}

if (-not $SkipPublish) {
    $publishArguments = @{
        OutputDirectory = $publishedPath
        Version = $Version
    }
    if ($QpdfRuntimeDirectory) {
        $publishArguments.QpdfRuntimeDirectory = $QpdfRuntimeDirectory
    }
    if ($StripExecutable) {
        $publishArguments.StripExecutable = $StripExecutable
    }
    & (Join-Path $PSScriptRoot "publish-gui.ps1") @publishArguments
}

$publishedExecutable = Join-Path $publishedPath "QpdfDecryptor.exe"
if (-not (Test-Path $publishedExecutable -PathType Leaf)) {
    throw "Published application was not found at '$publishedExecutable'."
}

$compiler = if ($IsccPath) {
    $IsccPath
} else {
    @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_ -PathType Leaf) } | Select-Object -First 1
}

if (-not $compiler -or -not (Test-Path $compiler -PathType Leaf)) {
    throw "Inno Setup Compiler was not found. Install JRSoftware.InnoSetup or pass -IsccPath."
}

New-Item -ItemType Directory -Force -Path $outputParent | Out-Null

try {
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    & $compiler `
        "/DAppVersion=$Version" `
        "/DSourceDir=$publishedPath" `
        "/DOutputDir=$stagingDirectory" `
        $installerDefinition
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }

    $stagedInstaller = Join-Path $stagingDirectory "QpdfDecryptor-Setup.exe"
    if (-not (Test-Path $stagedInstaller -PathType Leaf) -or
        (Get-Item $stagedInstaller).Length -eq 0) {
        throw "Inno Setup did not create a valid installer at '$stagedInstaller'."
    }

    $hash = (Get-FileHash $stagedInstaller -Algorithm SHA256).Hash
    "$hash *QpdfDecryptor-Setup.exe" |
        Set-Content (Join-Path $stagingDirectory "SHA256SUMS.txt") -Encoding ascii

    if (Test-Path -LiteralPath $outputPath) {
        Move-Item -LiteralPath $outputPath -Destination $backupDirectory
    }

    try {
        Move-Item -LiteralPath $stagingDirectory -Destination $outputPath
    } catch {
        if ((Test-Path -LiteralPath $backupDirectory) -and
            -not (Test-Path -LiteralPath $outputPath)) {
            Move-Item -LiteralPath $backupDirectory -Destination $outputPath
        }
        throw
    }

    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }

    Write-Host "Release package created at $outputPath"
} finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $backupDirectory) {
        if (-not (Test-Path -LiteralPath $outputPath)) {
            Move-Item -LiteralPath $backupDirectory -Destination $outputPath
        } else {
            Remove-Item -LiteralPath $backupDirectory -Recurse -Force
        }
    }
}
