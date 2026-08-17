[CmdletBinding()]
param(
    [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'PingNotify')
)

$ErrorActionPreference = 'Stop'
$repository = 'WillEastbury/pingnotify'
$temporaryRoot = Join-Path $env:TEMP ('PingNotify-install-' + [guid]::NewGuid().ToString('N'))
$archive = Join-Path $env:TEMP 'PingNotify-latest.zip'

try {
    Write-Host 'Finding the latest PingNotify release...'
    $release = Invoke-RestMethod "https://api.github.com/repos/$repository/releases/latest"
    $asset = @($release.assets | Where-Object { $_.name -eq 'PingNotify-win-x64.zip' }) | Select-Object -First 1
    $downloadUri = if ($null -ne $asset) { $asset.browser_download_url } else { $release.zipball_url }
    if ([string]::IsNullOrWhiteSpace($downloadUri)) {
        throw 'The latest release does not provide a downloadable build or source archive.'
    }

    Write-Host "Downloading $($release.tag_name)..."
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
    Write-Host "Installed $($release.tag_name) to $InstallPath"
    Start-Process -FilePath (Join-Path $InstallPath 'PingNotify.exe')
}
finally {
    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}
