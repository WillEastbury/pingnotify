[CmdletBinding()]
param(
    [string]$StoragePath = '',
    [ValidateRange(2, 300)]
    [int]$PollSeconds = 5
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
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$machineName = $env:COMPUTERNAME -replace '[^A-Za-z0-9._-]', '_'
$isBlobContainer = $StoragePath -match '^https://[^/]+/[^/?]+(?:\?.*)?$'

function Get-BlobUri([string]$blobName) {
    $parts = $StoragePath -split '\?', 2
    $sasQuery = if ($parts.Count -gt 1) { '?' + $parts[1] } else { '' }
    return '{0}/{1}{2}' -f $parts[0].TrimEnd('/'), $blobName, $sasQuery
}

function Get-ContainerBlobs {
    if (-not $isBlobContainer) {
        return @(Get-ChildItem -LiteralPath $StoragePath -Filter '*.json' -File -ErrorAction SilentlyContinue)
    }

    $parts = $StoragePath -split '\?', 2
    if ($parts.Count -lt 2) { throw 'notificationShare must include a SAS query string.' }
    $listUri = '{0}?restype=container&comp=list&{1}' -f $parts[0].TrimEnd('/'), $parts[1]
    $result = Invoke-RestMethod -Uri $listUri -Method Get -Headers @{ 'x-ms-version' = '2021-12-02' }
    return @($result.EnumerationResults.Blobs.Blob | Where-Object { $null -ne $_.Name })
}

function Read-BlobMetadata($blob) {
    try {
        if ($isBlobContainer) {
            $document = Invoke-RestMethod -Uri (Get-BlobUri $blob.Name) -Method Get -Headers @{ 'x-ms-version' = '2021-12-02' } | ConvertFrom-Json
            $sourceName = [IO.Path]::GetFileNameWithoutExtension($blob.Name)
        }
        else {
            $document = Get-Content -LiteralPath $blob.FullName -Raw | ConvertFrom-Json
            $sourceName = [IO.Path]::GetFileNameWithoutExtension($blob.Name)
        }
    }
    catch {
        return @()
    }

    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($property in $document.PSObject.Properties) {
        $seqProperty = $property.Value.PSObject.Properties['seq']
        $lastProperty = $property.Value.PSObject.Properties['last']
        $sequence = 0L
        $last = [datetimeoffset]::MinValue
        if ($null -eq $seqProperty -or $null -eq $lastProperty) { continue }
        if (-not [long]::TryParse([string]$seqProperty.Value, [ref]$sequence) -or $sequence -lt 1) { continue }
        if (-not [datetimeoffset]::TryParse([string]$lastProperty.Value, [ref]$last)) { continue }
        $items.Add([pscustomobject]@{
            Machine = $sourceName
            Application = $property.Name
            Quantity = $sequence
            Last = $last
        })
    }
    return $items
}

function Read-OtherMachineNotifications {
    $items = [System.Collections.Generic.List[object]]::new()
    try {
        foreach ($blob in @(Get-ContainerBlobs)) {
            $name = if ($isBlobContainer) { $blob.Name } else { $blob.Name }
            if ($name -notlike '*.json' -or [IO.Path]::GetFileNameWithoutExtension($name) -eq $machineName) { continue }
            foreach ($item in @(Read-BlobMetadata $blob)) { $items.Add($item) }
        }
    }
    catch {
        return $items
    }
    return $items | Sort-Object Machine, Application
}

function Get-SharedText {
    $blobName = 'remote-copy.txt'
    if ($isBlobContainer) {
        return Invoke-RestMethod -Uri (Get-BlobUri $blobName) -Method Get -Headers @{ 'x-ms-version' = '2021-12-02' }
    }
    $path = Join-Path $StoragePath $blobName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return '' }
    return Get-Content -LiteralPath $path -Raw
}

function Set-SharedText([string]$text) {
    $blobName = 'remote-copy.txt'
    if ($isBlobContainer) {
        $body = [Text.Encoding]::UTF8.GetBytes($text)
        Invoke-RestMethod -Uri (Get-BlobUri $blobName) -Method Put -Headers @{
            'x-ms-version' = '2021-12-02'
            'x-ms-blob-type' = 'BlockBlob'
            'Content-Type' = 'text/plain; charset=utf-8'
        } -Body $body | Out-Null
        return
    }
    New-Item -ItemType Directory -Path $StoragePath -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $StoragePath $blobName) -Value $text -Encoding UTF8
}

function Show-TextEntry {
    param([string]$Title, [string]$InitialText = '', [switch]$ReadOnly)
    $dialog = [Windows.Forms.Form]::new()
    $dialog.Text = $Title
    $dialog.Width = 620
    $dialog.Height = 360
    $dialog.StartPosition = [Windows.Forms.FormStartPosition]::CenterScreen
    $dialog.TopMost = $true
    $textBox = [Windows.Forms.TextBox]::new()
    $textBox.Multiline = $true
    $textBox.ScrollBars = [Windows.Forms.ScrollBars]::Both
    $textBox.WordWrap = $false
    $textBox.Dock = [Windows.Forms.DockStyle]::Fill
    $textBox.Text = $InitialText
    $textBox.ReadOnly = $ReadOnly
    $buttons = [Windows.Forms.FlowLayoutPanel]::new()
    $buttons.Height = 42
    $buttons.Dock = [Windows.Forms.DockStyle]::Bottom
    $buttons.FlowDirection = [Windows.Forms.FlowDirection]::RightToLeft
    $ok = [Windows.Forms.Button]::new()
    $ok.Text = if ($ReadOnly) { 'Done and clear shared data' } else { 'Remote Copy' }
    $ok.DialogResult = [Windows.Forms.DialogResult]::OK
    $cancel = [Windows.Forms.Button]::new()
    $cancel.Text = 'Cancel'
    $cancel.DialogResult = [Windows.Forms.DialogResult]::Cancel
    $buttons.Controls.Add($cancel) | Out-Null
    $buttons.Controls.Add($ok) | Out-Null
    $dialog.Controls.Add($textBox)
    $dialog.Controls.Add($buttons)
    $dialog.AcceptButton = $ok
    $dialog.CancelButton = $cancel
    $dialog.Add_Shown({ $textBox.Focus() | Out-Null })
    $result = $dialog.ShowDialog()
    $value = $textBox.Text
    $dialog.Dispose()
    if ($result -eq [Windows.Forms.DialogResult]::OK) { return $value }
    return $null
}

function Invoke-RemoteCopy {
    $warning = 'Remote Copy sends the entered string outside this machine and current trust boundary. Do not copy sensitive data, credentials, secrets, or regulated content. Continue?'
    if ([Windows.Forms.MessageBox]::Show($warning, 'Remote Copy warning', [Windows.Forms.MessageBoxButtons]::YesNo, [Windows.Forms.MessageBoxIcon]::Warning) -ne [Windows.Forms.DialogResult]::Yes) { return }
    $text = Show-TextEntry -Title 'Remote Copy' 
    if ($null -ne $text) { Set-SharedText $text }
}

function Invoke-RemotePaste {
    $text = Get-SharedText
    if ([string]::IsNullOrEmpty($text)) {
        [Windows.Forms.MessageBox]::Show('The remote copy area is empty.', 'Remote Paste', [Windows.Forms.MessageBoxButtons]::OK, [Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        return
    }
    [void](Show-TextEntry -Title 'Remote Paste' -InitialText $text -ReadOnly)
    Set-SharedText ''
}

$notifyIcon = [Windows.Forms.NotifyIcon]::new()
$notifyIcon.Icon = [Drawing.SystemIcons]::Information
$notifyIcon.Visible = $true
$notifyIcon.Text = 'PingNotify'
$flyout = [Windows.Forms.Form]::new()
$flyout.FormBorderStyle = [Windows.Forms.FormBorderStyle]::None
$flyout.ShowInTaskbar = $false
$flyout.TopMost = $true
$flyout.StartPosition = [Windows.Forms.FormStartPosition]::Manual
$flyout.BackColor = [Drawing.Color]::WhiteSmoke
$flyout.Padding = [Windows.Forms.Padding]::new(10)
$flyout.AutoSize = $true
$flyout.AutoSizeMode = [Windows.Forms.AutoSizeMode]::GrowAndShrink
$flyoutLabel = [Windows.Forms.Label]::new()
$flyoutLabel.AutoSize = $true
$flyoutLabel.Font = [Drawing.Font]::new('Segoe UI', 9)
$flyoutLabel.MaximumSize = [Drawing.Size]::new(460, 0)
$flyoutLabel.ForeColor = [Drawing.Color]::Black
$flyout.Controls.Add($flyoutLabel)
$menu = [Windows.Forms.ContextMenuStrip]::new()
$exitItem = $menu.Items.Add('Exit')
$exitItem.Add_Click({ $notifyIcon.Visible = $false; [Windows.Forms.Application]::Exit() })
$menu.Items.Add('-') | Out-Null
$copyItem = $menu.Items.Add('Remote Copy...')
$copyItem.Add_Click({ Invoke-RemoteCopy })
$pasteItem = $menu.Items.Add('Remote Paste...')
$pasteItem.Add_Click({ Invoke-RemotePaste })
$notifyIcon.ContextMenuStrip = $menu

$currentItems = @()
$lastSignature = ''
$lastHoverSignature = ''
$lastHover = [datetime]::MinValue

function Format-NotificationList($items) {
    if ($items.Count -eq 0) { return 'No pending notifications on other machines.' }
    return ($items | ForEach-Object { '{0}: {1} ({2})' -f $_.Machine, $_.Application, $_.Quantity }) -join "`n"
}

function Update-Tray {
    $script:currentItems = @(Read-OtherMachineNotifications)
    $signature = ($script:currentItems | ForEach-Object { '{0}|{1}|{2}|{3}' -f $_.Machine, $_.Application, $_.Quantity, $_.Last }) -join ';'
    if ($signature -ne $script:lastSignature) {
        $script:lastSignature = $signature
        $script:lastHoverSignature = ''
        $notifyIcon.Icon = if ($script:currentItems.Count -gt 0) { [Drawing.SystemIcons]::Warning } else { [Drawing.SystemIcons]::Information }
        $notifyIcon.Text = ('PingNotify: {0}' -f (($script:currentItems | ForEach-Object { '{0}/{1}={2}' -f $_.Machine, $_.Application, $_.Quantity }) -join ', ')).Substring(0, [Math]::Min(63, ('PingNotify: {0}' -f (($script:currentItems | ForEach-Object { '{0}/{1}={2}' -f $_.Machine, $_.Application, $_.Quantity }) -join ', ')).Length))
        $menu.Items.Clear()
        $menu.Items.Add(('Other machines: {0}' -f $script:currentItems.Count)) | Out-Null
        $menu.Items.Add((Format-NotificationList $script:currentItems)) | Out-Null
        $menu.Items.Add('-') | Out-Null
        $newCopy = $menu.Items.Add('Remote Copy...')
        $newCopy.Add_Click({ Invoke-RemoteCopy })
        $newPaste = $menu.Items.Add('Remote Paste...')
        $newPaste.Add_Click({ Invoke-RemotePaste })
        $menu.Items.Add('-') | Out-Null
        $newExit = $menu.Items.Add('Exit')
        $newExit.Add_Click({ $notifyIcon.Visible = $false; [Windows.Forms.Application]::Exit() })
    }
}

$notifyIcon.Add_MouseMove({
    $script:lastHover = [datetime]::UtcNow
    if (-not $flyout.Visible -or $script:lastHoverSignature -ne $script:lastSignature) {
        $flyoutLabel.Text = Format-NotificationList $script:currentItems
        $flyout.Show()
        $flyout.Location = [Drawing.Point]::new(
            [Math]::Max(0, [Windows.Forms.Cursor]::Position.X - $flyout.Width + 20),
            [Math]::Max(0, [Windows.Forms.Cursor]::Position.Y - $flyout.Height - 8))
        $script:lastHoverSignature = $script:lastSignature
    }
})
$flyout.Add_MouseMove({ $script:lastHover = [datetime]::UtcNow })

$timer = [Windows.Forms.Timer]::new()
$timer.Interval = $PollSeconds * 1000
$timer.Add_Tick({ Update-Tray })
$timer.Start()
$flyoutTimer = [Windows.Forms.Timer]::new()
$flyoutTimer.Interval = 250
$flyoutTimer.Add_Tick({
    if ($flyout.Visible -and ([datetime]::UtcNow - $script:lastHover).TotalSeconds -gt 5) {
        $flyout.Hide()
    }
})
$flyoutTimer.Start()
Update-Tray
[Windows.Forms.Application]::Run()
$timer.Stop()
$flyoutTimer.Stop()
$flyout.Dispose()
$notifyIcon.Dispose()
