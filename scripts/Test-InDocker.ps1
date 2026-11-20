param(
    [string]$Image = 'fleetops-verify:phase5',
    [switch]$Coverage,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    docker build -f (Join-Path $repositoryRoot 'Dockerfile.verify') -t $Image $repositoryRoot
    if ($LASTEXITCODE -ne 0) { throw 'Verification image build failed.' }
}
$resultsDirectory = Join-Path $repositoryRoot ('artifacts/test-results/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null
$dockerArguments = @(
    'run', '--rm',
    '--mount', 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock',
    '--mount', "type=bind,source=$resultsDirectory,target=/test-results",
    '-e', 'TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal',
    $Image, 'dotnet', 'test', 'FleetOps.slnx', '--no-build', '--no-restore', '-m:1',
    '--settings', 'tests.runsettings',
    '--logger', 'console;verbosity=minimal', '--logger', 'trx',
    '--results-directory', '/test-results'
)
if ($Coverage) { $dockerArguments += @('--collect', 'XPlat Code Coverage') }
& docker @dockerArguments
$testExitCode = $LASTEXITCODE
Write-Output "Test artifacts: $resultsDirectory"
if ($testExitCode -ne 0) { throw 'Verification tests failed; retained artifacts contain the results.' }
