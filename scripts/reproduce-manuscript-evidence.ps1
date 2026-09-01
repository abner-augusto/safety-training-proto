param(
    [string]$OutputDirectory = "artifacts/reproduction",
    # The manuscript evidence is bound to this commit. Everything below runs
    # against a throwaway worktree checked out at this ref, not the current
    # branch, so the asserted figures stay valid as `main` moves on.
    [string]$EvidenceRef = "v1.0.0"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$testProject = "Tools/SafetyProto.Tests/SafetyProto.Tests.csproj"
$cliProject = "Tools/CliHarness"
$canonicalScenario = "Assets/_SafetyProto/Resources/Scenarios/default.json"
$ppeScenario = "Tools/CliHarness/scenarios/ppe_equip.json"

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$evidenceCommit = (& git -C $repoRoot rev-parse "$EvidenceRef^{commit}" 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($evidenceCommit)) {
    throw "Evidence ref '$EvidenceRef' not found. Run 'git fetch --tags', or pass -EvidenceRef <commit>. The pinned figures below only reproduce at that commit."
}
$evidenceCommit = $evidenceCommit.Trim()
$repoHead = (& git -C $repoRoot rev-parse HEAD).Trim()

$worktree = Join-Path ([System.IO.Path]::GetTempPath()) "svr-evidence-$($evidenceCommit.Substring(0, 12))"
if (Test-Path $worktree) {
    & git -C $repoRoot worktree remove --force $worktree 2>$null
    if (Test-Path $worktree) { Remove-Item -Recurse -Force $worktree }
}

function Invoke-DotNetStep {
    param(
        [string]$Name,
        [string[]]$Arguments
    )

    $logPath = Join-Path $outputRoot "$Name.log"
    Write-Host "`n=== $Name ===" -ForegroundColor Cyan
    $output = @(& dotnet @Arguments 2>&1 | Tee-Object -FilePath $logPath)
    if ($LASTEXITCODE -ne 0) {
        throw "Step '$Name' failed with exit code $LASTEXITCODE. See $logPath"
    }

    return ($output -join "`n")
}

Write-Host "Pinned evidence ref: $EvidenceRef ($evidenceCommit)" -ForegroundColor Cyan
Write-Host "Repo HEAD:           $repoHead" -ForegroundColor Cyan
& git -C $repoRoot worktree add --detach $worktree $evidenceCommit
if ($LASTEXITCODE -ne 0) { throw "Failed to create evidence worktree at $worktree." }

Push-Location $worktree
try {
    $tests = Invoke-DotNetStep "headless-tests" @(
        "test", $testProject,
        "--results-directory", (Join-Path $outputRoot "test-results")
    )
    if ($tests -notmatch "Total:\s+46") {
        throw "Expected 46 headless tests at $EvidenceRef."
    }

    $integration = Invoke-DotNetStep "integration-tests" @(
        "test", $testProject,
        "--filter", "FullyQualifiedName~SessionIntegrationTests",
        "--results-directory", (Join-Path $outputRoot "integration-results")
    )
    if ($integration -notmatch "Total:\s+8") {
        throw "Expected 8 integration tests at $EvidenceRef."
    }

    $coverageDirectory = Join-Path $outputRoot "coverage"
    Invoke-DotNetStep "coverage" @(
        "test", $testProject,
        '--collect:XPlat Code Coverage',
        "--settings", "Tools/SafetyProto.Tests/coverlet.runsettings",
        "--results-directory", $coverageDirectory
    ) | Out-Null

    $coverageFile = Get-ChildItem -Path $coverageDirectory -Recurse -Filter "coverage.cobertura.xml" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $coverageFile) {
        throw "Cobertura output was not generated."
    }

    [xml]$coverageXml = Get-Content -Raw -Path $coverageFile.FullName
    $coverageNode = $coverageXml.coverage
    $lineRate = [double]::Parse($coverageNode.'line-rate', [Globalization.CultureInfo]::InvariantCulture)
    $branchRate = [double]::Parse($coverageNode.'branch-rate', [Globalization.CultureInfo]::InvariantCulture)
    $linePercent = [Math]::Round($lineRate * 100, 1)
    $branchPercent = [Math]::Round($branchRate * 100, 1)
    if ($linePercent -ne 70.3 -or $branchPercent -ne 57.3) {
        throw "Coverage changed: line=$linePercent%, branch=$branchPercent%. Update the manuscript evidence."
    }

    $ppeOutput = Join-Path $outputRoot "harness-ppe"
    $ppe = Invoke-DotNetStep "cli-ppe" @(
        "run", "--project", $cliProject, "--", $ppeScenario, $ppeOutput
    )
    if ($ppe -notmatch "Session summary: 5/5 tasks, score 750") {
        throw "PPE CLI result did not match 5/5 tasks and 750 points."
    }

    $canonicalOutput = Join-Path $outputRoot "harness-canonical"
    $canonical = Invoke-DotNetStep "cli-canonical" @(
        "run", "--project", $cliProject, "--", $canonicalScenario, $canonicalOutput
    )
    if ($canonical -notmatch "Session summary: 9/9 tasks, score 1400") {
        throw "Canonical CLI result did not match 9/9 tasks and 1,400 points."
    }

    $summary = @(
        "# Manuscript evidence reproduction",
        "",
        "- Evidence ref: ``$EvidenceRef`` (``$evidenceCommit``)",
        "- Reproduced from repo HEAD: ``$repoHead``",
        "- .NET SDK: ``$(& dotnet --version)``",
        "- Headless tests: 46/46",
        "- Integration tests: 8/8",
        "- Coverage: $linePercent% line ($($coverageNode.'lines-covered')/$($coverageNode.'lines-valid')), $branchPercent% branch ($($coverageNode.'branches-covered')/$($coverageNode.'branches-valid'))",
        "- PPE CLI: 5/5 tasks, 750 points",
        "- Canonical CLI: 9/9 tasks, 1,400 points",
        "- Canonical scenario: ``$canonicalScenario``"
    )
    $summaryPath = Join-Path $outputRoot "summary.md"
    Set-Content -Path $summaryPath -Value $summary -Encoding utf8

    Write-Host "`nAll reproducible manuscript checks passed (pinned to $EvidenceRef)." -ForegroundColor Green
    Write-Host "Summary: $summaryPath"
}
finally {
    Pop-Location
    & git -C $repoRoot worktree remove --force $worktree 2>$null
    if (Test-Path $worktree) { Remove-Item -Recurse -Force $worktree -ErrorAction SilentlyContinue }
}
