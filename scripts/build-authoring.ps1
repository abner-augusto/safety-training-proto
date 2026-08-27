param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = "dist/AuthoringApp",
    [string]$CopyToDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = "Tools/AuthoringApp.Gui"

. (Join-Path $PSScriptRoot "lib/Load-DotEnv.ps1")
Import-DotEnv -Path (Join-Path $PSScriptRoot ".env")

if (-not $CopyToDirectory -and $env:SAFETY_AUTHORING_OUTPUT_DIR) {
    $CopyToDirectory = $env:SAFETY_AUTHORING_OUTPUT_DIR
}

Push-Location $repoRoot
try {
    Write-Host "=== Publishing Authoring GUI ($RuntimeIdentifier) ===" -ForegroundColor Cyan
    & dotnet publish $projectPath -c Release -r $RuntimeIdentifier --self-contained -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    $exeName = "SafetyProto.AuthoringApp.Gui.exe"
    $publishedExe = Join-Path $OutputDirectory $exeName
    if (-not (Test-Path $publishedExe)) {
        throw "Expected executable not found at $publishedExe"
    }

    Write-Host "=== Published to $publishedExe ===" -ForegroundColor Green
    Get-Item $publishedExe | Select-Object Name, Length, LastWriteTime

    if ($CopyToDirectory) {
        New-Item -ItemType Directory -Force -Path $CopyToDirectory | Out-Null
        Copy-Item -Path (Join-Path $OutputDirectory "*") -Destination $CopyToDirectory -Recurse -Force
        Write-Host "=== Copied to $CopyToDirectory ===" -ForegroundColor Green
    }
} finally {
    Pop-Location
}
