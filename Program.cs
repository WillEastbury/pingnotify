using System.Drawing;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace PingNotify;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly BlobStore _store;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Form _flyout;
    private readonly Label _flyoutLabel;
    private readonly System.Windows.Forms.Timer _flyoutTimer;
    private readonly string _machineName = SanitizeMachineName(Environment.MachineName);
    private IReadOnlyList<RemoteNotification> _remote = [];
    private string _signature = string.Empty;
    private DateTime _lastHoverUtc;

    public TrayContext()
    {
        _store = new BlobStore();
        _flyout = CreateFlyout(out _flyoutLabel);
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Visible = true,
            Text = "PingNotify",
            ContextMenuStrip = _menu
        };
        _icon.MouseMove += (_, _) => ShowFlyout();
        _flyoutTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _flyoutTimer.Tick += (_, _) =>
        {
            if (_flyout.Visible && (DateTime.UtcNow - _lastHoverUtc).TotalSeconds > 5)
                _flyout.Hide();
        };
        _flyoutTimer.Start();
        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        RebuildMenu();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                ShowError($"Notification listener access is {access}. Enable access in Windows Settings.");
                return;
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Notification listener unavailable: {ex.Message}");
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var local = notifications
                .GroupBy(notification => notification.AppInfo?.DisplayInfo?.DisplayName ?? "Unknown app")
                .ToDictionary(
                    group => group.Key,
                    group => new NotificationMetadata(
                        group.LongCount(),
                        group.Max(notification => notification.CreationTime)));
            await _store.WriteMachineMetadataAsync(_machineName, local);

            _remote = await _store.ReadOtherMachinesAsync(_machineName);
            var nextSignature = string.Join(';', _remote.Select(item =>
                $"{item.Machine}|{item.Application}|{item.Quantity}|{item.Last:O}"));
            if (nextSignature != _signature)
            {
                _signature = nextSignature;
                _icon.Icon = _remote.Count > 0 ? SystemIcons.Warning : SystemIcons.Information;
                if (_flyout.Visible)
                    _flyoutLabel.Text = FormatRemoteList(_remote);
                RebuildMenu();
            }
        }
        catch (Exception ex)
        {
            ShowError($"PingNotify refresh failed: {ex.Message}");
        }
    }

    private void ShowFlyout()
    {
        _lastHoverUtc = DateTime.UtcNow;
        if (_flyout.Visible) return;
        _flyoutLabel.Text = FormatRemoteList(_remote);
        _flyout.PerformLayout();
        var cursor = Cursor.Position;
        _flyout.Location = new Point(
            Math.Max(0, cursor.X - _flyout.Width + 20),
            Math.Max(0, cursor.Y - _flyout.Height - 8));
        _flyout.Show();
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add($"Other machines: {_remote.Count}").Enabled = false;
        _menu.Items.Add(FormatRemoteList(_remote)).Enabled = false;
        _menu.Items.Add(new ToolStripSeparator());
        var copy = _menu.Items.Add("Remote Copy...");
        copy.Click += async (_, _) => await RemoteCopyAsync();
        var paste = _menu.Items.Add("Remote Paste...");
        paste.Click += async (_, _) => await RemotePasteAsync();
        _menu.Items.Add(new ToolStripSeparator());
        var exit = _menu.Items.Add("Exit");
        exit.Click += (_, _) => ExitThread();
    }

    private async Task RemoteCopyAsync()
    {
        const string warning =
            "Remote Copy sends the entered string outside this machine and current trust boundary.\n\n" +
            "Do not copy sensitive data, credentials, secrets, or regulated content.\n\nContinue?";
        if (MessageBox.Show(warning, "Remote Copy warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var text = TextDialog.Show("Remote Copy", "Remote Copy", readOnly: false);
        if (text is not null)
            await _store.WriteSharedTextAsync(text);
    }

    private async Task RemotePasteAsync()
    {
        var text = await _store.ReadSharedTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("The remote copy area is empty.", "Remote Paste",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        TextDialog.Show("Remote Paste", text, readOnly: true);
        await _store.WriteSharedTextAsync(string.Empty);
    }

    private void ShowError(string message)
    {
        _icon.Icon = SystemIcons.Error;
        _icon.Text = "PingNotify: error";
        _menu.Items.Clear();
        _menu.Items.Add(message).Enabled = false;
        var exit = _menu.Items.Add("Exit");
        exit.Click += (_, _) => ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _flyoutTimer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _flyout.Dispose();
        _menu.Dispose();
        _store.Dispose();
        base.ExitThreadCore();
    }

    private static Form CreateFlyout(out Label label)
    {
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            TopMost = true,
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(10),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        label = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            MaximumSize = new Size(460, 0),
            ForeColor = Color.Black,
            Text = "No pending notifications on other machines."
        };
        form.Controls.Add(label);
        return form;
    }

    private static string FormatRemoteList(IEnumerable<RemoteNotification> items) =>
        items.Any()
            ? string.Join(Environment.NewLine, items.Select(item =>
                $"{item.Machine}: {item.Application} ({item.Quantity})"))
            : "No pending notifications on other machines.";

    private static string SanitizeMachineName(string value) =>
        string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '_'));
}

internal sealed class BlobStore : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly string _location;
    private readonly bool _remote;

    public BlobStore()
    {
        var configuredLocation =
            Environment.GetEnvironmentVariable("notificationShare", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("notificationShare", EnvironmentVariableTarget.Process);
        if (string.IsNullOrWhiteSpace(configuredLocation))
        {
            var redirectedPath = @"\\tsclient\c\PingNotify";
            var localPath = @"C:\PingNotify";
            Directory.CreateDirectory(localPath);
            try
            {
                Directory.CreateDirectory(redirectedPath);
                _location = redirectedPath;
            }
            catch (IOException)
            {
                _location = localPath;
            }
            catch (UnauthorizedAccessException)
            {
                _location = localPath;
            }
        }
        else
        {
            _location = configuredLocation;
        }
        _remote = Uri.TryCreate(_location, UriKind.Absolute, out var uri) &&
                  uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    public async Task WriteMachineMetadataAsync(
        string machine,
        IReadOnlyDictionary<string, NotificationMetadata> metadata)
    {
        var json = JsonSerializer.Serialize(metadata);
        await PutAsync($"{machine}.json", json, "application/json");
    }

    public async Task<IReadOnlyList<RemoteNotification>> ReadOtherMachinesAsync(string currentMachine)
    {
        var result = new List<RemoteNotification>();
        foreach (var blobName in await ListBlobsAsync())
        {
            var machine = Path.GetFileNameWithoutExtension(blobName);
            if (!blobName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                machine.Equals(currentMachine, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var json = await GetAsync(blobName);
                var metadata = JsonSerializer.Deserialize<Dictionary<string, NotificationMetadata>>(json) ?? [];
                result.AddRange(metadata.Select(item => new RemoteNotification(
                    machine, item.Key, item.Value.Seq, item.Value.Last)));
            }
            catch (JsonException)
            {
                // Ignore malformed metadata files; notification payloads are never inspected.
            }
        }
        return result.OrderBy(item => item.Machine).ThenBy(item => item.Application).ToArray();
    }

    public Task<string> ReadSharedTextAsync() => GetAsync("remote-copy.txt", missingAsEmpty: true);

    public Task WriteSharedTextAsync(string text) =>
        PutAsync("remote-copy.txt", text, "text/plain; charset=utf-8");

    private async Task<IReadOnlyList<string>> ListBlobsAsync()
    {
        if (!_remote)
            return Directory.Exists(_location)
                ? Directory.EnumerateFiles(_location, "*.json").Select(Path.GetFileName).Where(name => name is not null).Cast<string>().ToArray()
                : [];

        var uri = ContainerUri("restype=container&comp=list");
        var xml = XDocument.Parse(await _http.GetStringAsync(uri));
        return xml.Descendants("Blob")
            .Elements("Name")
            .Select(element => element.Value)
            .ToArray();
    }

    private async Task<string> GetAsync(string blobName, bool missingAsEmpty = false)
    {
        if (!_remote)
        {
            var path = Path.Combine(_location, blobName);
            if (missingAsEmpty && !File.Exists(path)) return string.Empty;
            return await File.ReadAllTextAsync(path);
        }

        using var response = await _http.GetAsync(BlobUri(blobName));
        if (missingAsEmpty && response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return string.Empty;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task PutAsync(string blobName, string content, string mediaType)
    {
        if (!_remote)
        {
            Directory.CreateDirectory(_location);
            await File.WriteAllTextAsync(Path.Combine(_location, blobName), content, Encoding.UTF8);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, BlobUri(blobName));
        request.Headers.TryAddWithoutValidation("x-ms-version", "2021-12-02");
        request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        request.Content = new StringContent(content, Encoding.UTF8, mediaType);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private string BlobUri(string blobName) =>
        $"{BaseUri().TrimEnd('/')}/{Uri.EscapeDataString(blobName)}{SasSuffix()}";

    private string ContainerUri(string query) =>
        $"{BaseUri().TrimEnd('/')}?{query}&{SasQuery()}";

    private string BaseUri() => _location.Split('?', 2)[0];

    private string SasSuffix() =>
        _location.Contains('?') ? "?" + _location.Split('?', 2)[1] : string.Empty;

    private string SasQuery() =>
        _location.Split('?', 2).ElementAtOrDefault(1)
        ?? throw new InvalidOperationException("notificationShare must include a SAS query.");

    public void Dispose() => _http.Dispose();
}

internal sealed record NotificationMetadata(long Seq, DateTimeOffset Last);
internal sealed record RemoteNotification(string Machine, string Application, long Quantity, DateTimeOffset Last);
