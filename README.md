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

```powershell
dotnet restore .\PingNotify.csproj
dotnet build .\PingNotify.csproj
```

`Package.appxmanifest` declares the `userNotificationListener` capability. Deploy the executable as an MSIX/package using that manifest; an unpackaged WinForms executable cannot receive notification-listener access. Windows requests listener consent on first use.

## Tray features

The tray icon changes when another machine has pending notifications. Hovering over it opens a borderless flyout containing the full `Machine: Application (Quantity)` list. The context menu provides the same list plus:

- **Remote Copy**: requires confirmation that the entered string leaves the current trust boundary. Never enter sensitive data, credentials, secrets, or regulated content.
- **Remote Paste**: displays `remote-copy.txt` in a read-only text box for manual copying, then clears the shared blob when the dialog closes. It is a one-shot shared value.

The PowerShell scripts remain available as a legacy metadata-only fallback for environments that cannot run the packaged .NET agent.
