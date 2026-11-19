# Dot-source this script to load ignored local settings into your current PowerShell session.
$settingsPath = Join-Path $PSScriptRoot '../.env'
if (-not (Test-Path -LiteralPath $settingsPath)) { throw 'Copy .env.example to .env and fill in local credentials first.' }
foreach ($line in Get-Content -LiteralPath $settingsPath) {
    if ($line.Trim() -eq '' -or $line.TrimStart().StartsWith('#')) { continue }
    $parts = $line -split '=', 2
    if ($parts.Count -ne 2 -or $parts[0] -notin @('MSSQL_SA_PASSWORD', 'ConnectionStrings__FleetOps')) {
        throw 'Unexpected local configuration key.'
    }
    [Environment]::SetEnvironmentVariable($parts[0], $parts[1], 'Process')
}
