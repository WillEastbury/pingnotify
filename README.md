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

### Private notification storage

Create a private container and set the user environment variable `notificationShare` to its complete container SAS URI. The SAS must allow:

- Read (`r`)
- Write (`w`)
- Create (`c`)
- List (`l`)
- Delete (`d`) for one-shot Remote Paste cleanup

Azure setup guides:

- [Create a user delegation SAS](https://learn.microsoft.com/azure/storage/blobs/storage-blob-user-delegation-sas-create-cli)
- [Create and manage containers](https://learn.microsoft.com/azure/storage/blobs/storage-blob-containers-portal)
- [SAS overview and security guidance](https://learn.microsoft.com/azure/storage/blobs/sas-overview)

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
irm https://tinyurl.com/pingnotify | iex
```

This TinyURL currently redirects directly to the Blob-hosted installer. The installer then downloads `latest.json` and `PingNotify-latest.zip` from Blob Storage; it does not contact GitHub.

### Public release downloads

The public release container is separate from private notification storage and contains only the installer, manifest, and application ZIP. Maintainers configure the GitHub Actions secret `AZURE_CREDENTIALS_SAS_BLOB` with a complete container SAS URI granting write (`w`) and create (`c`) permissions. Anonymous blob read access must be enabled on that container.

Azure guide: [Configure anonymous read access for blob data](https://learn.microsoft.com/azure/storage/blobs/anonymous-read-access-configure)

### Configure storage

Do not reuse the public download SAS for `notificationShare`. Use a separate private container SAS, and never commit either SAS or place it in a public script. Sign out and back in, or restart the agent, after changing the variable.

If the variable is omitted, the app creates and uses the local fallback directories described above.

On first run without `notificationShare`, PingNotify asks how to proceed. Choose **Yes** to enter the SAS URI; PingNotify saves it as a user-scoped environment variable and restarts. Choose **No** to use TSClient drive redirection instead, or **Cancel** to exit.

### Build from source

Install the .NET 9 SDK, then run:

```powershell
dotnet restore .\PingNotify.csproj
dotnet build .\PingNotify.csproj
```

## Android read-only app

`PingNotify.Android.csproj` is a separate Android app. It reads the private `notificationShare` container using only HTTPS `GET` requests, then displays pending entries as `Machine · Application · Quantity`. It has no upload, update, notification-listener, or Blob write code, so the phone cannot publish or send notification data.

Build it with the .NET 10 SDK and Android workload:

```powershell
dotnet build .\PingNotify.Android.csproj
```

On first launch, enter the private container SAS URI. The value is stored in Android app-private preferences; use a read/list-only SAS for the phone where possible. The app refreshes automatically every 2.5 minutes and has a manual Refresh button.

The repository’s **Build Android app** workflow also produces an APK artifact in GitHub Actions for sideloading.

`Package.appxmanifest` declares the `userNotificationListener` capability. Deploy the executable as an MSIX/package using that manifest; an unpackaged WinForms executable cannot receive notification-listener access. Windows requests listener consent on first use.

The repository currently contains the compiled executable, but not a signed MSIX installer. Running `PingNotify.exe` directly is useful for checking the tray UI and storage connectivity; package it with the manifest before relying on Windows notification-listener access.

PingNotify checks the public Blob manifest when it starts and every 12 hours afterward. It sends a lightweight `HEAD` request and compares the manifest ETag with the locally persisted ETag under `%LOCALAPPDATA%\PingNotify`, so it does not call GitHub or download the ZIP unless the manifest changes. If a newer self-contained build is available, it asks for confirmation, replaces the installed files, and restarts.

Local notification metadata is published every 10 minutes. Remote machine metadata is scanned every 2.5 minutes.

### Slack notifications

Slack must be configured to deliver notifications through **Windows Action Center/native notifications**. Slack’s own in-app popups are not Windows toast notifications and cannot be observed by `UserNotificationListener`. Also verify that Windows notifications are enabled for Slack and that Slack is installed and launched from its registered Start menu shortcut.

The app also includes an opt-in best-effort Slack fallback using Windows UI Automation. It counts visible Slack notification entries in memory and writes only `Slack`, quantity, and the current timestamp; UI text is discarded immediately. Because Windows does not expose a stable notification-center API for every Slack build, this fallback may require Notification Center entries to be visible and is not guaranteed on every Windows version.

## Tray features

The tray icon changes when another machine has pending notifications. Hovering over it opens a borderless flyout containing the full `Machine: Application (Quantity)` list. The context menu provides the same list plus:

The neutral application icon means there is nothing to do. The blue information icon means notifications are waiting. An error icon is reserved for communication, authentication, or other operational failures.

- **Remote Copy**: requires confirmation that the entered string leaves the current trust boundary. Never enter sensitive data, credentials, secrets, or regulated content.
- **Remote Paste**: displays `remote-copy.txt` in a read-only text box for manual copying, then clears the shared blob when the dialog closes. It is a one-shot shared value.

The PowerShell scripts remain available as a legacy metadata-only fallback for environments that cannot run the packaged .NET agent.
