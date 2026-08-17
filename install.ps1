[CmdletBinding()]
param(
    [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'PingNotify'),
    [switch]$ForceUpdate,
    [string]$DownloadBaseUri = 'https://notificationstatus.blob.core.windows.net/public'
)

$ErrorActionPreference = 'Stop'
$installedExecutable = Join-Path $InstallPath 'PingNotify.exe'
$manifestUrl = "$($DownloadBaseUri.TrimEnd('/'))/latest.json"

$installedVersion = if (Test-Path -LiteralPath $installedExecutable -PathType Leaf) {
    (Get-Item -LiteralPath $installedExecutable).VersionInfo.ProductVersion
} else {
    $null
}

try {
    $manifest = Invoke-RestMethod $manifestUrl
}
catch {
    if ($null -ne $installedVersion -and -not $ForceUpdate) {
        Write-Host "Could not check the latest version. Starting installed version $installedVersion."
        Start-Process -FilePath $installedExecutable
        return
    }
    throw
}

$remoteVersion = $null
$localVersion = $null
[void][Version]::TryParse(([string]$manifest.version).TrimStart('v'), [ref]$remoteVersion)
[void][Version]::TryParse(([string]$installedVersion).TrimStart('v'), [ref]$localVersion)
if ($null -ne $installedVersion -and -not $ForceUpdate -and $null -ne $remoteVersion -and $null -ne $localVersion -and $localVersion -ge $remoteVersion) {
    Write-Host "PingNotify is already current at version $installedVersion."
    Start-Process -FilePath $installedExecutable
    return
}

$temporaryRoot = Join-Path $env:TEMP ('PingNotify-install-' + [guid]::NewGuid().ToString('N'))
$archive = Join-Path $env:TEMP 'PingNotify-latest.zip'

try {
    Write-Host 'Finding the latest PingNotify build...'
    $downloadUri = "$($DownloadBaseUri.TrimEnd('/'))/PingNotify-latest.zip"
    if ([string]::IsNullOrWhiteSpace($manifest.version)) {
        throw 'The download manifest is missing a version.'
    }

    Write-Host "Version to install: $($manifest.version)"
    Write-Host "Downloading PingNotify $($manifest.version)..."
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
    $shell = New-Object -ComObject WScript.Shell
    $startMenuPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PingNotify.lnk'
    $startupPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\PingNotify.lnk'
    foreach ($shortcutPath in @($startMenuPath, $startupPath)) {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $installedExecutable
        $shortcut.WorkingDirectory = $InstallPath
        $shortcut.Description = 'PingNotify notification status tray agent'
        $shortcut.Save()
    }
    Write-Host "Installed $($manifest.version) to $InstallPath"
    Start-Process -FilePath (Join-Path $InstallPath 'PingNotify.exe')
}
finally {
    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}
