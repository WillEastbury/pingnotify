[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$App,
    [Parameter(Mandatory)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$Seq,
    [string]$StoragePath = '',
    [datetimeoffset]$Last = [datetimeoffset]::UtcNow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($StoragePath)) {
    $configuredLocation = [Environment]::GetEnvironmentVariable('notificationShare', 'User')
    if ([string]::IsNullOrWhiteSpace($configuredLocation)) {
        $localPath = 'C:\PingNotify'
        $redirectedPath = '\\tsclient\c\PingNotify'
        New-Item -ItemType Directory -Path $localPath -Force | Out-Null
        try {
            New-Item -ItemType Directory -Path $redirectedPath -Force | Out-Null
            $StoragePath = $redirectedPath
        }
        catch [System.IO.IOException] {
            $StoragePath = $localPath
        }
        catch [System.UnauthorizedAccessException] {
            $StoragePath = $localPath
        }
    }
    else {
        $StoragePath = $configuredLocation
    }
}
$machineName = $env:COMPUTERNAME
if ([string]::IsNullOrWhiteSpace($machineName)) { throw 'COMPUTERNAME is not available.' }
$machineName = $machineName -replace '[^A-Za-z0-9._-]', '_'
$blobName = '{0}.json' -f $machineName
$isBlobContainer = $StoragePath -match '^https://[^/]+/[^/?]+(?:\?.*)?$'
$stateFile = if ($isBlobContainer) { $null } else { Join-Path $StoragePath $blobName }
$state = [ordered]@{}

if (($isBlobContainer) -or (Test-Path -LiteralPath $stateFile -PathType Leaf)) {
    try {
        if ($isBlobContainer) {
            $parts = $StoragePath -split '\?', 2
            $sasQuery = if ($parts.Count -gt 1) { '?' + $parts[1] } else { '' }
            $blobUri = '{0}/{1}{2}' -f $parts[0].TrimEnd('/'), $blobName, $sasQuery
            $existing = Invoke-RestMethod -Uri $blobUri -Method Get -Headers @{ 'x-ms-version' = '2021-12-02' } | ConvertFrom-Json
        }
        else {
            $existing = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        }
    }
    catch { throw "The existing notification metadata file is invalid: $($_.Exception.Message)" }
    foreach ($property in $existing.PSObject.Properties) {
        $seqProperty = $property.Value.PSObject.Properties['seq']
        $lastProperty = $property.Value.PSObject.Properties['last']
        $existingSeq = 0L
        if ($null -ne $seqProperty -and $null -ne $lastProperty -and [long]::TryParse([string]$seqProperty.Value, [ref]$existingSeq) -and $existingSeq -gt 0) {
            $state[$property.Name] = [ordered]@{ seq = $existingSeq; last = [string]$lastProperty.Value }
        }
    }
}

$state[$App] = [ordered]@{
    seq = $Seq
    last = $Last.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
}
$json = $state | ConvertTo-Json -Depth 3
if ($isBlobContainer) {
    $parts = $StoragePath -split '\?', 2
    $sasQuery = if ($parts.Count -gt 1) { '?' + $parts[1] } else { '' }
    $blobUri = '{0}/{1}{2}' -f $parts[0].TrimEnd('/'), $blobName, $sasQuery
    $body = [Text.Encoding]::UTF8.GetBytes($json)
    Invoke-RestMethod -Uri $blobUri -Method Put -Headers @{
        'x-ms-version' = '2021-12-02'
        'x-ms-blob-type' = 'BlockBlob'
        'Content-Type' = 'application/json; charset=utf-8'
    } -Body $body | Out-Null
}
else {
    $tempFile = Join-Path $StoragePath ('notifications.{0}.tmp' -f [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $StoragePath -Force | Out-Null
    $json | Set-Content -LiteralPath $tempFile -Encoding UTF8
    Move-Item -LiteralPath $tempFile -Destination $stateFile -Force
}
