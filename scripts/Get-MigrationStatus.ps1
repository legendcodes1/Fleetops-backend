# Read-only: refresh this PowerShell process from the local file, then query EF status.
# No database initialization, migration application, or credential output.
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Use-LocalDatabase.ps1')
Write-Output 'Loaded database settings from the repository .env into this PowerShell process.'

$efArguments = @(
    'ef', 'migrations', 'list',
    '--project', (Join-Path $repositoryRoot 'src/FleetOps.Infrastructure'),
    '--startup-project', (Join-Path $repositoryRoot 'src/FleetOps.Api'),
    '--context', 'FleetOpsDbContext', '--json', '--', '--connection-diagnostics'
)
$previousErrorAction = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $output = @(& dotnet @efArguments 2>&1 | ForEach-Object { $_.ToString() })
    $efExitCode = $LASTEXITCODE
}
finally { $ErrorActionPreference = $previousErrorAction }

# Only show known-safe factory fields; do not echo arbitrary provider error messages.
$output | Where-Object { $_ -match '^Factory(:| settings:)' } | Write-Output
if ($efExitCode -ne 0) { throw 'EF tooling failed. Run the credential-safe diagnostic for details.' }
$jsonStart = [Array]::IndexOf($output, '[')
$jsonEnd = [Array]::LastIndexOf($output, ']')
if ($jsonStart -lt 0 -or $jsonEnd -lt $jsonStart) { throw 'EF did not return migration-status JSON.' }
$migrations = @(($output[$jsonStart..$jsonEnd] -join [Environment]::NewLine) | ConvertFrom-Json)
if (@($migrations | Where-Object { $null -eq $_.applied }).Count -gt 0) {
    throw 'Migration status is unknown: EF could not read the database. No migration was applied.'
}
foreach ($migration in $migrations) {
    $status = if ($migration.applied) { 'Applied' } else { 'Pending' }
    Write-Output ($migration.id + ': ' + $status)
}
