param([Uri]$BaseUri = 'http://localhost:5252')
$ErrorActionPreference = 'Stop'
if ($BaseUri.Scheme -ne 'https' -and -not ($BaseUri.Scheme -eq 'http' -and $BaseUri.IsLoopback)) {
    throw 'Use HTTPS for remote authentication. HTTP is allowed only for local development.'
}
$email = Read-Host 'Account email'
$global:FleetOpsHeaders = $null
$global:FleetOpsRefreshToken = $null
$securePassword = Read-Host 'Password' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $body = @{ email = $email; password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer) } | ConvertTo-Json -Compress
    $session = Invoke-RestMethod -Uri ($BaseUri.AbsoluteUri.TrimEnd('/') + '/api/auth/login') -Method Post -ContentType 'application/json' -Body $body
    $global:FleetOpsHeaders = @{ Authorization = 'Bearer ' + $session.accessToken }
    $global:FleetOpsRefreshToken = $session.refreshToken
    Write-Output 'Authenticated. Use -Headers $FleetOpsHeaders for API requests. Tokens are held in this process and were not printed.'
}
catch { throw 'Login failed. Check account credentials, account status, lockout, and the API address.' }
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    $securePassword.Dispose()
    $body = $null
    $session = $null
}
