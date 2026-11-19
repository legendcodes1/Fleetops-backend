param([switch]$InDocker)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Use-LocalDatabase.ps1')
$originalConnection = $env:ConnectionStrings__FleetOps
$previousEmail = $env:BootstrapAdmin__Email
$previousPassword = $env:BootstrapAdmin__Password
$email = Read-Host 'Administrator email'
$securePassword = Read-Host 'Administrator password (12+ characters, upper/lower/digit/symbol)' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    if ($plainPassword.Length -lt 12 -or $plainPassword.Length -gt 128 -or
        $plainPassword -cnotmatch '[A-Z]' -or $plainPassword -cnotmatch '[a-z]' -or
        $plainPassword -notmatch '[0-9]' -or $plainPassword -notmatch '[^a-zA-Z0-9]') {
        throw 'Password must contain 12-128 characters, including uppercase, lowercase, a digit, and a symbol. No account was created.'
    }
    $env:BootstrapAdmin__Email = $email
    $env:BootstrapAdmin__Password = $plainPassword
    if ($InDocker) {
        $env:ConnectionStrings__FleetOps = $originalConnection.Replace('tcp:127.0.0.1,14333', 'tcp:sqlserver,1433')
        docker run --rm --network fleetops_default --mount type=volume,source=fleetops-auth-keys,target=/keys -e DataProtection__KeysPath=/keys -e ConnectionStrings__FleetOps -e BootstrapAdmin__Email -e BootstrapAdmin__Password -e Logging__LogLevel__Default=Warning fleetops-verify:phase4 dotnet src/FleetOps.Api/bin/Debug/net10.0/FleetOps.Api.dll --environment Development --bootstrap-admin
    }
    else {
        dotnet (Join-Path $repositoryRoot 'src/FleetOps.Api/bin/Debug/net10.0/FleetOps.Api.dll') --environment Development --bootstrap-admin
    }
    if ($LASTEXITCODE -ne 0) { throw 'Administrator initialization failed. Ensure the Identity migration is applied and the password meets policy.' }
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    $securePassword.Dispose()
    $plainPassword = $null
    $env:BootstrapAdmin__Email = $previousEmail
    $env:BootstrapAdmin__Password = $previousPassword
    $env:ConnectionStrings__FleetOps = $originalConnection
}
