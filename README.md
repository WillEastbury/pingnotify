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

## Tray features

The tray icon changes when another machine has pending notifications. Hovering over it opens a borderless flyout containing the full `Machine: Application (Quantity)` list. The context menu provides the same list plus:

- **Remote Copy**: requires confirmation that the entered string leaves the current trust boundary. Never enter sensitive data, credentials, secrets, or regulated content.
- **Remote Paste**: displays `remote-copy.txt` in a read-only text box for manual copying, then clears the shared blob when the dialog closes. It is a one-shot shared value.

The PowerShell scripts remain available as a legacy metadata-only fallback for environments that cannot run the packaged .NET agent.
