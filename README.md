# PingNotify

PingNotify is a Windows .NET system-tray agent for RDP, devbox, SSD, and SAW sessions. It uses the Windows notification listener to detect pending notifications, but never stores, logs, forwards, or displays notification content.

## Privacy boundary

The listener processes notifications in memory and reads only:

- Application display name
- Notification count
- Latest notification timestamp

The resulting machine file contains metadata only:

```json
{
  "teams":   { "seq": 12, "last": "2026-08-17T10:52:00Z" },
  "outlook": { "seq": 4, "last": "2026-08-17T10:40:10Z" }
}
```

## Azure storage

Set the user environment variable `notificationShare` to a container SAS URI. The SAS must allow:

- Read (`r`)
- Write (`w`)
- Create (`c`)
- List (`l`)
- Delete (`d`) for one-shot Remote Paste cleanup

Each machine writes `<machine-name>.json` to the container. Other machines list and read the files, excluding their own machine file.

Each machine file also includes a reserved `_pingnotify` block so readers can determine freshness:

```json
"_pingnotify": {
  "lastPublishedUtc": "2026-08-17T14:40:00Z",
  "nextPublishUtc": "2026-08-17T14:50:00Z"
}
```

If `notificationShare` is not set, the app creates both `C:\PingNotify` and `\\tsclient\c\PingNotify` when possible. It uses the redirected path when available and falls back to `C:\PingNotify` when the RDP drive is unavailable.

## .NET build and deployment

### Download the repository

From GitHub, select **Code → Download ZIP**, extract it, and open a PowerShell terminal in the extracted folder. Or clone it:

```powershell
git clone https://github.com/WillEastbury/pingnotify.git
cd pingnotify
```

The self-contained release executable and dependencies are distributed in the latest GitHub release. A framework-dependent debug build, when present, is under:

`bin\Debug\net9.0-windows10.0.19041.0\`

The installer is idempotent: after the first install, running it again starts the local copy without contacting GitHub. It installs to `%LOCALAPPDATA%\PingNotify`, creates `PingNotify.lnk` in the user Start Menu, and creates a Startup shortcut so the tray agent starts when the user signs in. Use `.\install.ps1 -ForceUpdate` when you explicitly want to check for and install the latest release. PingNotify itself performs its separate, rate-limited update check every 12 hours.

Expected paths:

- Executable: `%LOCALAPPDATA%\PingNotify\PingNotify.exe`
- Start Menu shortcut: `%APPDATA%\Microsoft\Windows\Start Menu\Programs\PingNotify.lnk`
- Startup shortcut: `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\PingNotify.lnk`

Run the installer directly from the anonymous Blob endpoint:

```powershell
irm https://notificationstatus.blob.core.windows.net/public/install.ps1 | iex
```

The installer downloads `latest.json` and `PingNotify-latest.zip` from Blob Storage; it does not contact GitHub. A GitHub Action publishes the installer script, manifest, and ZIP to the public container addressed by `AZURE_CREDENTIALS_SAS_BLOB` whenever a GitHub release is published. The secret must contain the complete container SAS URI with write/create permissions.

### Configure storage

Set `notificationShare` as a **user** environment variable using the complete container SAS URI. Do not commit the SAS URI or place it in a public script. Sign out and back in, or restart the agent, after changing the variable.

If the variable is omitted, the app creates and uses the local fallback directories described above.

On first run without `notificationShare`, PingNotify asks how to proceed. Choose **Yes** to enter the SAS URI; PingNotify saves it as a user-scoped environment variable and restarts. Choose **No** to use TSClient drive redirection instead, or **Cancel** to exit.

### Build from source

Install the .NET 9 SDK, then run:

```powershell
dotnet restore .\PingNotify.csproj
dotnet build .\PingNotify.csproj
```

`Package.appxmanifest` declares the `userNotificationListener` capability. Deploy the executable as an MSIX/package using that manifest; an unpackaged WinForms executable cannot receive notification-listener access. Windows requests listener consent on first use.

The repository currently contains the compiled executable, but not a signed MSIX installer. Running `PingNotify.exe` directly is useful for checking the tray UI and storage connectivity; package it with the manifest before relying on Windows notification-listener access.

PingNotify checks the latest GitHub release when it starts and every 12 hours afterward. The next-check time is persisted under `%LOCALAPPDATA%\PingNotify`, so restarting the app does not bypass the interval. If GitHub returns HTTP 429, checking is paused for one hour. If a newer self-contained `PingNotify-win-x64*.zip` release is available, it asks for confirmation, replaces the installed files, and restarts.

Local notification metadata is published every 10 minutes. Remote machine metadata is scanned every 2.5 minutes.

### Slack notifications

Slack must be configured to deliver notifications through **Windows Action Center/native notifications**. Slack’s own in-app popups are not Windows toast notifications and cannot be observed by `UserNotificationListener`. Also verify that Windows notifications are enabled for Slack and that Slack is installed and launched from its registered Start menu shortcut.

The app also includes an opt-in best-effort Slack fallback using Windows UI Automation. It counts visible Slack notification entries in memory and writes only `Slack`, quantity, and the current timestamp; UI text is discarded immediately. Because Windows does not expose a stable notification-center API for every Slack build, this fallback may require Notification Center entries to be visible and is not guaranteed on every Windows version.

## Tray features

The tray icon changes when another machine has pending notifications. Hovering over it opens a borderless flyout containing the full `Machine: Application (Quantity)` list. The context menu provides the same list plus:

- **Remote Copy**: requires confirmation that the entered string leaves the current trust boundary. Never enter sensitive data, credentials, secrets, or regulated content.
- **Remote Paste**: displays `remote-copy.txt` in a read-only text box for manual copying, then clears the shared blob when the dialog closes. It is a one-shot shared value.

The PowerShell scripts remain available as a legacy metadata-only fallback for environments that cannot run the packaged .NET agent.
