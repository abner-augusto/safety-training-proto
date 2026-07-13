#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_base="${1:-artifacts/reproduction}"
run_id="$(date -u +%Y%m%dT%H%M%SZ)"
output_root="$repo_root/$output_base/$run_id"
test_project="Tools/SafetyProto.Tests/SafetyProto.Tests.csproj"
cli_project="Tools/CliHarness"
canonical_scenario="Assets/_SafetyProto/Resources/Scenarios/default.json"
ppe_scenario="Tools/CliHarness/scenarios/ppe_equip.json"

mkdir -p "$output_root"
cd "$repo_root"

run_step() {
    local name="$1"
    shift
    printf '\n=== %s ===\n' "$name"
    "$@" 2>&1 | tee "$output_root/$name.log"
}

read_trx_count() {
    local trx="$1"
    local attribute="$2"
    sed -n "s/.*$attribute=\"\([0-9][0-9]*\)\".*/\1/p" "$trx" | head -n 1
}

headless_dir="$output_root/test-results"
run_step headless-tests dotnet test "$test_project" \
    --logger "trx;LogFileName=headless.trx" \
    --results-directory "$headless_dir"
headless_trx="$headless_dir/headless.trx"
[[ "$(read_trx_count "$headless_trx" total)" == "46" ]]
[[ "$(read_trx_count "$headless_trx" passed)" == "46" ]]

integration_dir="$output_root/integration-results"
run_step integration-tests dotnet test "$test_project" \
    --filter "FullyQualifiedName~SessionIntegrationTests" \
    --logger "trx;LogFileName=integration.trx" \
    --results-directory "$integration_dir"
integration_trx="$integration_dir/integration.trx"
[[ "$(read_trx_count "$integration_trx" total)" == "8" ]]
[[ "$(read_trx_count "$integration_trx" passed)" == "8" ]]

coverage_dir="$output_root/coverage"
run_step coverage dotnet test "$test_project" \
    --collect:"XPlat Code Coverage" \
    --settings "Tools/SafetyProto.Tests/coverlet.runsettings" \
    --results-directory "$coverage_dir"
coverage_file="$(find "$coverage_dir" -name coverage.cobertura.xml -type f | head -n 1)"
[[ -n "$coverage_file" ]]
coverage_line="$(grep -m 1 '<coverage ' "$coverage_file")"
line_rate="$(printf '%s\n' "$coverage_line" | sed -n 's/.*line-rate="\([^"]*\)".*/\1/p')"
branch_rate="$(printf '%s\n' "$coverage_line" | sed -n 's/.*branch-rate="\([^"]*\)".*/\1/p')"
lines_covered="$(printf '%s\n' "$coverage_line" | sed -n 's/.*lines-covered="\([^"]*\)".*/\1/p')"
lines_valid="$(printf '%s\n' "$coverage_line" | sed -n 's/.*lines-valid="\([^"]*\)".*/\1/p')"
branches_covered="$(printf '%s\n' "$coverage_line" | sed -n 's/.*branches-covered="\([^"]*\)".*/\1/p')"
branches_valid="$(printf '%s\n' "$coverage_line" | sed -n 's/.*branches-valid="\([^"]*\)".*/\1/p')"
[[ "$line_rate" == "0.7034" ]]
[[ "$branch_rate" == "0.5731" ]]

ppe_output="$output_root/harness-ppe"
run_step cli-ppe dotnet run --project "$cli_project" -- "$ppe_scenario" "$ppe_output"
grep -q "Session summary: 5/5 tasks, score 750" "$output_root/cli-ppe.log"

canonical_output="$output_root/harness-canonical"
run_step cli-canonical dotnet run --project "$cli_project" -- "$canonical_scenario" "$canonical_output"
grep -q "Session summary: 9/9 tasks, score 1400" "$output_root/cli-canonical.log"

commit="$(git rev-parse HEAD)"
if [[ -n "$(git status --porcelain)" ]]; then
    working_tree="dirty"
else
    working_tree="clean"
fi

cat > "$output_root/summary.md" <<EOF
# Manuscript evidence reproduction

- Commit: \`$commit\`
- Working tree: $working_tree
- .NET SDK: \`$(dotnet --version)\`
- Headless tests: 46/46
- Integration tests: 8/8
- Coverage: 70.3% line ($lines_covered/$lines_valid), 57.3% branch ($branches_covered/$branches_valid)
- PPE CLI: 5/5 tasks, 750 points
- Canonical CLI: 9/9 tasks, 1,400 points
- Canonical scenario: \`$canonical_scenario\`
EOF

printf '\nAll reproducible manuscript checks passed.\n'
if [[ "$working_tree" != "clean" ]]; then
    printf 'WARNING: The working tree is dirty. Re-run after committing to bind evidence to one commit.\n' >&2
fi
printf 'Summary: %s\n' "$output_root/summary.md"
