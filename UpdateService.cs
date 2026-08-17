using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace PingNotify;

internal sealed class UpdateService : IDisposable
{
    private const string ManifestUrl = "https://notificationstatus.blob.core.windows.net/public/latest.json";
    private const string AssetUrl = "https://notificationstatus.blob.core.windows.net/public/PingNotify-latest.zip";
    private readonly HttpClient _http = new();
    private readonly string _checkStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PingNotify",
        "update-check.json");
    private int _checking;

    public async Task<AvailableUpdate?> GetAvailableUpdateAsync()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1)
            return null;
        try
        {
            if (File.Exists(_checkStatePath))
            {
                var state = JsonSerializer.Deserialize<UpdateCheckState>(
                    await File.ReadAllTextAsync(_checkStatePath));
                if (state?.NextCheckUtc > DateTimeOffset.UtcNow)
                    return null;
            }

            using var head = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, ManifestUrl));
            if (head.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            head.EnsureSuccessStatusCode();
            var etag = head.Headers.ETag?.Tag;
            var previousState = File.Exists(_checkStatePath)
                ? JsonSerializer.Deserialize<UpdateCheckState>(await File.ReadAllTextAsync(_checkStatePath))
                : null;
            await SaveStateAsync(DateTimeOffset.UtcNow.AddHours(12), etag);
            if (!string.IsNullOrWhiteSpace(etag) && etag == previousState?.ETag)
                return null;

            using var manifestResponse = await _http.GetAsync(ManifestUrl);
            manifestResponse.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await manifestResponse.Content.ReadAsStreamAsync());
            var root = document.RootElement;
            var tag = root.GetProperty("version").GetString()?.TrimStart('v');
            if (!Version.TryParse(tag, out var latest))
                return null;

            var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
            if (latest <= current)
                return null;

            return new AvailableUpdate(latest, AssetUrl);
        }

        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    private async Task SaveStateAsync(DateTimeOffset nextCheckUtc, string? etag)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_checkStatePath)!);
        await File.WriteAllTextAsync(
            _checkStatePath,
            JsonSerializer.Serialize(new UpdateCheckState(nextCheckUtc, etag)));
    }

    public async Task InstallAsync(AvailableUpdate update, int currentProcessId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"PingNotify-update-{Guid.NewGuid():N}");
        var archive = Path.Combine(root, "PingNotify.zip");
        var extracted = Path.Combine(root, "extracted");
        Directory.CreateDirectory(root);
        try
        {
            using (var response = await _http.GetAsync(update.AssetUrl))
            {
                response.EnsureSuccessStatusCode();
                await using var output = File.Create(archive);
                await response.Content.CopyToAsync(output);
            }
            ZipFile.ExtractToDirectory(archive, extracted);
            var source = Directory.EnumerateFiles(extracted, "PingNotify.exe", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(path => path is not null)
                ?? throw new InvalidOperationException("The update archive does not contain PingNotify.exe.");
            var target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var script = Path.Combine(root, "apply-update.ps1");
            File.WriteAllText(script, $"""
                $ErrorActionPreference = 'Stop'
                Wait-Process -Id {currentProcessId} -Timeout 60 -ErrorAction SilentlyContinue
                Get-ChildItem -LiteralPath '{EscapePowerShell(source)}' | Copy-Item -Destination '{EscapePowerShell(target)}' -Recurse -Force
                Start-Process -FilePath '{EscapePowerShell(Path.Combine(target, "PingNotify.exe"))}'
                Remove-Item -LiteralPath '{EscapePowerShell(root)}' -Recurse -Force -ErrorAction SilentlyContinue
                """);
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            throw;
        }
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''");

    public void Dispose() => _http.Dispose();
}

internal sealed record AvailableUpdate(Version Version, string AssetUrl);
internal sealed record UpdateCheckState(DateTimeOffset NextCheckUtc, string? ETag);
