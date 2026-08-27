function Import-DotEnv {
    <#
      Loads KEY=VALUE pairs from a .env file into process environment variables.
      Existing environment variables are never overwritten, so CI/shell exports
      still take precedence over the file. Missing files are silently skipped.
    #>
    param(
        [Parameter(Mandatory)][string]$Path
    )

    if (-not (Test-Path $Path)) {
        return
    }

    foreach ($line in Get-Content -Path $Path) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf('=')
        if ($separatorIndex -lt 1) {
            continue
        }

        $key = $trimmed.Substring(0, $separatorIndex).Trim()
        $value = $trimmed.Substring($separatorIndex + 1).Trim().Trim('"').Trim("'")

        if (-not (Test-Path "env:$key")) {
            Set-Item -Path "env:$key" -Value $value
        }
    }
}
