param([string]$BaseUrl = 'http://localhost:5252', [hashtable]$Headers = $global:FleetOpsHeaders)
$ErrorActionPreference = 'Stop'
if (-not $Headers -or -not $Headers.Authorization) { throw 'Authenticate with ./scripts/Connect-FleetOps.ps1 first.' }
$targetUri = [Uri]$BaseUrl
if ($targetUri.Scheme -ne 'https' -and -not ($targetUri.Scheme -eq 'http' -and $targetUri.IsLoopback)) {
    throw 'Use HTTPS for remote requests.'
}
function Assert-Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
function Send-Request($method, $path, $body = $null, $etag = $null) {
    $parameters = @{ UseBasicParsing = $true; Uri = "$BaseUrl$path"; Method = $method }
    $parameters.Headers = @{}
    foreach ($key in $Headers.Keys) { $parameters.Headers[$key] = $Headers[$key] }
    if ($null -ne $body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $body | ConvertTo-Json -Compress
    }
    if ($null -ne $etag) { $parameters.Headers['If-Match'] = $etag }
    try {
        $response = Invoke-WebRequest @parameters
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $response.Content; ETag = $response.Headers.ETag }
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        $errorBody = $_.ErrorDetails.Message
        if ([string]::IsNullOrEmpty($errorBody)) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            try { $errorBody = $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        return [pscustomobject]@{ Status = [int]$_.Exception.Response.StatusCode; Body = $errorBody; ETag = $null }
    }
}
$prefix = 'HTTP-' + [guid]::NewGuid().ToString('N')
$createdIds = @()
try {
    $first = Send-Request Post '/api/assets' @{ name = "$prefix Alpha"; serialNumber = "$prefix-A" }
    Assert-Check ($first.Status -eq 201) 'Create returns 201'
    $asset = $first.Body | ConvertFrom-Json
    $createdIds += $asset.id
    Assert-Check ($first.ETag -eq ('"' + $asset.rowVersion + '"')) 'Create returns ETag matching rowVersion'
    $second = Send-Request Post '/api/assets' @{ name = "$prefix Beta"; serialNumber = "$prefix-B" }
    Assert-Check ($second.Status -eq 201) 'Create second query fixture'
    $createdIds += ($second.Body | ConvertFrom-Json).id
    $duplicate = Send-Request Post '/api/assets' @{ name = 'Duplicate'; serialNumber = "$prefix-a" }
    Assert-Check ($duplicate.Status -eq 409) 'Case-insensitive duplicate serial returns 409'
    Assert-Check (($duplicate.Body | ConvertFrom-Json).status -eq 409) 'Conflict response uses ProblemDetails'
    $page = Send-Request Get "/api/assets?search=$prefix&pageSize=1&sortBy=name&descending=true"
    $data = $page.Body | ConvertFrom-Json
    Assert-Check ($page.Status -eq 200 -and $data.totalCount -eq 2 -and $data.items.Count -eq 1 -and $data.items[0].name -eq "$prefix Beta") 'SQL filtering, descending sort, and pagination'
    $invalid = Send-Request Get '/api/assets?pageSize=101'
    Assert-Check ($invalid.Status -eq 400) 'Page-size bound returns 400'
    $invalid = Send-Request Get '/api/assets?sortBy=unsupported'
    Assert-Check ($invalid.Status -eq 400) 'Unsupported sort returns 400'
    $invalid = Send-Request Get '/api/assets?status=99'
    Assert-Check ($invalid.Status -eq 400) 'Undefined status returns 400'
    $read = Send-Request Get "/api/assets/$($asset.id)"
    $originalETag = $read.ETag
    $body = @{ name = "$prefix Updated"; serialNumber = "$prefix-A" }
    $missing = Send-Request Put "/api/assets/$($asset.id)" $body
    Assert-Check ($missing.Status -eq 428) 'Missing If-Match returns 428'
    $malformed = Send-Request Put "/api/assets/$($asset.id)" $body 'invalid'
    Assert-Check ($malformed.Status -eq 400) 'Malformed If-Match returns 400'
    $weak = Send-Request Put "/api/assets/$($asset.id)" $body ('W/' + $originalETag)
    Assert-Check ($weak.Status -eq 400) 'Weak If-Match is rejected'
    $updated = Send-Request Put "/api/assets/$($asset.id)" $body $originalETag
    Assert-Check ($updated.Status -eq 204) 'Current If-Match updates resource'
    $stale = Send-Request Put "/api/assets/$($asset.id)" $body $originalETag
    Assert-Check ($stale.Status -eq 412) 'Stale If-Match update returns 412'
    $staleDelete = Send-Request Delete "/api/assets/$($asset.id)" $null $originalETag
    Assert-Check ($staleDelete.Status -eq 412) 'Stale If-Match delete returns 412'
    $current = Send-Request Get "/api/assets/$($asset.id)"
    Assert-Check ($current.ETag -ne $originalETag -and ($current.Body | ConvertFrom-Json).name -eq "$prefix Updated") 'Version changes and new details persist'
    $deleted = Send-Request Delete "/api/assets/$($asset.id)" $null $current.ETag
    Assert-Check ($deleted.Status -eq 204) 'Current If-Match deletes resource'
    $absent = Send-Request Get "/api/assets/$($asset.id)"
    Assert-Check ($absent.Status -eq 404) 'Deleted resource returns 404'
    $invalid = Send-Request Post '/api/assets' @{ name = ' '; serialNumber = '' }
    Assert-Check ($invalid.Status -eq 400) 'Invalid create returns 400'
}
finally {
    foreach ($id in $createdIds) {
        $current = Send-Request Get "/api/assets/$id"
        if ($current.Status -eq 200) {
            $cleanup = Send-Request Delete "/api/assets/$id" $null $current.ETag
            if ($cleanup.Status -ne 204) { Write-Warning "Could not clean up smoke-test asset $id." }
        }
    }
}
