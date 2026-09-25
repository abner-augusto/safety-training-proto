param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = "dist/AuthoringApp",
    [string]$CopyToDirectory,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Show-Help {
    @'
Publish the Avalonia Authoring GUI with dotnet publish.

Usage:
  scripts/build-authoring.ps1 [options]

Options:
  -RuntimeIdentifier <rid>   Target runtime identifier (default: win-x64)
  -OutputDirectory <path>    Publish output directory (default: dist/AuthoringApp)
  -CopyToDirectory <path>    Also copy the published output here. Default:
                             $env:SAFETY_AUTHORING_OUTPUT_DIR when set
  -Help, --help              Show this help and exit

Examples:
  scripts/build-authoring.ps1
  scripts/build-authoring.ps1 -RuntimeIdentifier linux-x64
  scripts/build-authoring.ps1 -CopyToDirectory D:\Apps\Authoring
'@
}

# PowerShell binds --help to the first string parameter instead of -Help, so
# scan the bound values for it.
if ($Help -or $PSBoundParameters.Values -contains '--help') {
    Show-Help
    return
}

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
