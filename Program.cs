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
        if (!StorageSetup.EnsureConfigured())
            return;
        Application.Run(new TrayContext());
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly BlobStore _store;
    private readonly Icon _neutralIcon = NeutralIcon.Create();
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _localTimer;
    private readonly System.Windows.Forms.Timer _remoteTimer;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly UpdateService _updates = new();
    private readonly Form _flyout;
    private readonly Label _flyoutLabel;
    private readonly System.Windows.Forms.Timer _flyoutTimer;
    private readonly string _machineName = SanitizeMachineName(Environment.MachineName);
    private readonly string _version = $"Version {typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
    private IReadOnlyList<RemoteNotification> _remote = [];
    private string _signature = string.Empty;
    private DateTime _lastHoverUtc;

    public TrayContext()
    {
        _store = new BlobStore();
        _flyout = CreateFlyout(out _flyoutLabel);
        _icon = new NotifyIcon
        {
            Icon = _neutralIcon,
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
        _localTimer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
        _localTimer.Tick += async (_, _) => await PublishLocalMetadataAsync();
        _localTimer.Start();
        _remoteTimer = new System.Windows.Forms.Timer { Interval = 150 * 1000 };
        _remoteTimer.Tick += async (_, _) => await RefreshRemoteMetadataAsync();
        _remoteTimer.Start();
        _updateTimer = new System.Windows.Forms.Timer { Interval = 12 * 60 * 60 * 1000 };
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateTimer.Start();
        RebuildMenu();
        _ = InitializeAsync();
        _ = CheckForUpdateAsync();
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
            await PublishLocalMetadataAsync();
            await RefreshRemoteMetadataAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Notification listener unavailable: {ex.Message}");
        }
    }

    private async Task PublishLocalMetadataAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var local = notifications
                .GroupBy(GetApplicationName)
                .ToDictionary(
                    group => group.Key,
                    group => new NotificationMetadata(
                        group.LongCount(),
                        group.Max(notification => notification.CreationTime)));
            var slackFallback = UiAutomationNotificationSource.GetSlackMetadata();
            if (slackFallback is not null)
                local["Slack"] = slackFallback;
            await _store.WriteMachineMetadataAsync(_machineName, local);
        }
        catch (Exception ex)
        {
            ShowError($"PingNotify local metadata refresh failed: {ex.Message}");
        }
    }

    private async Task RefreshRemoteMetadataAsync()
    {
        try
        {
            _remote = await _store.ReadOtherMachinesAsync(_machineName);
            var nextSignature = string.Join(';', _remote.Select(item =>
                $"{item.Machine}|{item.Application}|{item.Quantity}|{item.Last:O}"));
            if (nextSignature != _signature)
            {
                _signature = nextSignature;
                _icon.Icon = _remote.Count > 0 ? SystemIcons.Information : _neutralIcon;
                if (_flyout.Visible)
                    _flyoutLabel.Text = FormatRemoteList(_remote);
                RebuildMenu();
            }

        }
        catch (Exception ex)
        {
            ShowError($"PingNotify remote metadata refresh failed: {ex.Message}");
        }
    }

    private static string GetApplicationName(UserNotification notification)
    {
        var displayName = notification.AppInfo?.DisplayInfo?.DisplayName;
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName;

        var appUserModelId = notification.AppInfo?.AppUserModelId;
        if (!string.IsNullOrWhiteSpace(appUserModelId) &&
            appUserModelId.Contains("slack", StringComparison.OrdinalIgnoreCase))
            return "Slack";

        return string.IsNullOrWhiteSpace(appUserModelId) ? "Unknown app" : appUserModelId;
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var update = await _updates.GetAvailableUpdateAsync();
            if (update is null)
                return;

            var choice = MessageBox.Show(
                $"PingNotify {update.Version} is available. Install it now and restart?",
                "PingNotify update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (choice != DialogResult.Yes)
                return;

            await _updates.InstallAsync(update, Environment.ProcessId);
            Application.Exit();
        }
        catch (Exception ex)
        {
            ShowError($"PingNotify update failed: {ex.Message}");
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
            Math.Max(0, cursor.Y - _flyout.Height - 36));
        _flyout.Show();
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(_version).Enabled = false;
        _menu.Items.Add(new ToolStripSeparator());
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
        _menu.Items.Add(_version).Enabled = false;
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(message).Enabled = false;
        var exit = _menu.Items.Add("Exit");
        exit.Click += (_, _) => ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _localTimer.Stop();
        _remoteTimer.Stop();
        _updateTimer.Stop();
        _flyoutTimer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _neutralIcon.Dispose();
        _flyout.Dispose();
        _menu.Dispose();
        _store.Dispose();
        _updates.Dispose();
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
        var published = DateTimeOffset.UtcNow;
        var document = new Dictionary<string, object>
        {
            ["_pingnotify"] = new MachineSchedule(published, published.AddMinutes(10))
        };
        foreach (var item in metadata)
            document[item.Key] = item.Value;
        var json = JsonSerializer.Serialize(document);
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
                using var document = JsonDocument.Parse(json);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.NameEquals("_pingnotify"))
                        continue;
                    var metadata = property.Value.Deserialize<NotificationMetadata>();
                    if (metadata is not null)
                        result.Add(new RemoteNotification(machine, property.Name, metadata.Seq, metadata.Last));
                }
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
        PutAsync("remote-copy.txt", text, "text/plain");

    private async Task<IReadOnlyList<string>> ListBlobsAsync()
    {
        if (!_remote)
            return Directory.Exists(_location)
                ? Directory.EnumerateFiles(_location, "*.json").Select(Path.GetFileName).Where(name => name is not null).Cast<string>().ToArray()
                : [];

        var uri = ContainerUri("restype=container&comp=list");
        var xml = XDocument.Parse(await _http.GetStringAsync(uri));
        return xml.Descendants()
            .Where(element => element.Name.LocalName == "Blob")
            .Elements()
            .Where(element => element.Name.LocalName == "Name")
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
internal sealed record MachineSchedule(DateTimeOffset LastPublishedUtc, DateTimeOffset NextPublishUtc);
internal sealed record RemoteNotification(string Machine, string Application, long Quantity, DateTimeOffset Last);
