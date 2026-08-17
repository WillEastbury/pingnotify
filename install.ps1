[CmdletBinding()]
param(
    [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'PingNotify'),
    [switch]$ForceUpdate,
    [string]$DownloadBaseUri = 'https://notificationstatus.blob.core.windows.net/downloads'
)

$ErrorActionPreference = 'Stop'
$installedExecutable = Join-Path $InstallPath 'PingNotify.exe'

if ((Test-Path -LiteralPath $installedExecutable -PathType Leaf) -and -not $ForceUpdate) {
    Write-Host "PingNotify is already installed at $InstallPath."
    Write-Host 'Starting the local installation without contacting GitHub.'
    Start-Process -FilePath $installedExecutable
    return
}

$temporaryRoot = Join-Path $env:TEMP ('PingNotify-install-' + [guid]::NewGuid().ToString('N'))
$archive = Join-Path $env:TEMP 'PingNotify-latest.zip'

try {
    Write-Host 'Finding the latest PingNotify build...'
    $manifest = Invoke-RestMethod "$($DownloadBaseUri.TrimEnd('/'))/latest.json"
    $downloadUri = "$($DownloadBaseUri.TrimEnd('/'))/PingNotify-latest.zip"
    if ([string]::IsNullOrWhiteSpace($manifest.version)) {
        throw 'The download manifest is missing a version.'
    }

    Write-Host "Downloading $($manifest.version)..."
    Invoke-WebRequest -Uri $downloadUri -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $temporaryRoot -Force

    $executable = Get-ChildItem -LiteralPath $temporaryRoot -Filter 'PingNotify.exe' -File -Recurse |
        Select-Object -First 1
    if ($null -eq $executable) {
        throw 'PingNotify.exe was not found in the release archive.'
    }

    $buildPath = $executable.Directory.FullName
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    Get-ChildItem -LiteralPath $buildPath | Copy-Item -Destination $InstallPath -Recurse -Force
    Write-Host "Installed $($manifest.version) to $InstallPath"
    Start-Process -FilePath (Join-Path $InstallPath 'PingNotify.exe')
}
finally {
    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}
