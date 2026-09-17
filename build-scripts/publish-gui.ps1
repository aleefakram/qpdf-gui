param(
    [string]$QpdfRuntimeDirectory = "",
    [string]$OutputDirectory = "artifacts\QpdfDecryptor",
    [string]$StripExecutable = "",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$projectDirectory = Join-Path $PSScriptRoot "..\src\QPdfDecryptor"
$nativeDirectory = Join-Path $projectDirectory "Native"
$runtimeSource = if ($QpdfRuntimeDirectory) {
    [IO.Path]::GetFullPath($QpdfRuntimeDirectory)
} else {
    [IO.Path]::GetFullPath($nativeDirectory)
}
$outputPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputDirectory))
}
$outputParent = Split-Path $outputPath -Parent
$outputName = Split-Path $outputPath -Leaf

if (-not $outputParent -or -not $outputName) {
    throw "OutputDirectory must identify a directory below a filesystem root."
}

$publishId = [Guid]::NewGuid().ToString("N")
$stagingDirectory = Join-Path $outputParent ".$outputName.staging-$publishId"
$backupDirectory = Join-Path $outputParent ".$outputName.backup-$publishId"
$preparedRuntimeDirectory = Join-Path $outputParent ".$outputName.native-$publishId"

if (-not [Version]::TryParse($Version, [ref]([Version]$null))) {
    throw "Version must be a numeric version such as 0.1.0."
}

if (-not (Test-Path (Join-Path $runtimeSource "qpdf.exe") -PathType Leaf)) {
    throw "Native qpdf runtime is missing. Pass -QpdfRuntimeDirectory with qpdf.exe and its required DLLs."
}

$stripPath = if ($StripExecutable) {
    $StripExecutable
} elseif ($env:QPDF_STRIP_EXECUTABLE) {
    $env:QPDF_STRIP_EXECUTABLE
} elseif (Test-Path "$env:USERPROFILE\msys64\ucrt64\bin\strip.exe") {
    "$env:USERPROFILE\msys64\ucrt64\bin\strip.exe"
} else {
    (Get-Command strip.exe -ErrorAction SilentlyContinue).Source
}

if (-not $stripPath -or -not (Test-Path $stripPath -PathType Leaf)) {
    throw "GNU strip was not found. Pass -StripExecutable or run install-build-tools.ps1."
}

New-Item -ItemType Directory -Force -Path $outputParent | Out-Null

try {
    New-Item -ItemType Directory -Path $preparedRuntimeDirectory | Out-Null
    Copy-Item -Path (Join-Path $runtimeSource "*") -Destination $preparedRuntimeDirectory -Recurse -Force

    Get-ChildItem $preparedRuntimeDirectory -File -Recurse |
        Where-Object Extension -in ".exe", ".dll" |
        ForEach-Object {
            & $stripPath --strip-unneeded $_.FullName
            if ($LASTEXITCODE -ne 0) {
                throw "Native symbol stripping failed for '$($_.Name)' with exit code $LASTEXITCODE."
            }
        }

    $preparedQpdf = Join-Path $preparedRuntimeDirectory "qpdf.exe"
    & $preparedQpdf --version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The prepared qpdf runtime failed its startup check with exit code $LASTEXITCODE."
    }

    & dotnet publish (Join-Path $projectDirectory "QPdfDecryptor.csproj") `
        -c Release `
        -r win-x64 `
        --self-contained true `
        "-p:Version=$Version" `
        "-p:QpdfRuntimeDirectory=$preparedRuntimeDirectory" `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=false `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishReadyToRun=false `
        -o $stagingDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $stagedExecutable = Join-Path $stagingDirectory "QpdfDecryptor.exe"
    if (-not (Test-Path $stagedExecutable -PathType Leaf)) {
        throw "dotnet publish completed without creating '$stagedExecutable'."
    }

    if ((Get-Item $stagedExecutable).Length -eq 0) {
        throw "dotnet publish created an empty executable at '$stagedExecutable'."
    }

    # qpdf ships beside the exe (not self-extracted to %TEMP%, which AppLocker/WDAC policies often block).
    if (-not (Test-Path (Join-Path $stagingDirectory "Native\qpdf.exe") -PathType Leaf)) {
        throw "dotnet publish did not place Native\qpdf.exe beside the executable."
    }

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

    $publishedExecutable = Join-Path $outputPath "QpdfDecryptor.exe"
    Write-Host "Published single-file app to $publishedExecutable"
} finally {
    if (Test-Path -LiteralPath $preparedRuntimeDirectory) {
        Remove-Item -LiteralPath $preparedRuntimeDirectory -Recurse -Force
    }

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
