param([string]$Image = 'fleetops-verify:phase3')
$ErrorActionPreference = 'Stop'
docker build -f Dockerfile.verify -t $Image .
if ($LASTEXITCODE -ne 0) { throw 'Verification image build failed.' }
docker run --rm --mount type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal $Image
if ($LASTEXITCODE -ne 0) { throw 'Verification tests failed.' }
