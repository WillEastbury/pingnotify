using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace PingNotify;

internal sealed class UpdateService : IDisposable
{
    private const string Repository = "WillEastbury/pingnotify";
    private readonly HttpClient _http = new();
    private int _checking;

    public async Task<AvailableUpdate?> GetAvailableUpdateAsync()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1)
            return null;
        try
        {
            using var response = await _http.GetAsync(
                $"https://api.github.com/repos/{Repository}/releases/latest");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v');
            if (!Version.TryParse(tag, out var latest))
                return null;

            var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
            if (latest <= current)
                return null;

            var asset = root.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(item => item.GetProperty("name").GetString() == "PingNotify-win-x64.zip");
            if (asset.ValueKind == JsonValueKind.Undefined)
                return null;

            return new AvailableUpdate(
                latest,
                asset.GetProperty("browser_download_url").GetString()
                    ?? throw new InvalidOperationException("The update asset URL is missing."));
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
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
