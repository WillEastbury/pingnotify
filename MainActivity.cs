using System.Text.Json;
using System.Xml.Linq;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace PingNotify.Android;

[Activity(Label = "PingNotify", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private readonly HttpClient _http = new();
    private EditText _sasInput = null!;
    private TextView _status = null!;
    private TextView _results = null!;
    private Button _refresh = null!;
    private string? _sasUri;
    private int _readMinutes = 2;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Action _scheduledRefresh;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _http.Timeout = TimeSpan.FromSeconds(20);
        var preferences = GetPreferences(global::Android.Content.FileCreationMode.Private);
        _sasUri = preferences.GetString("notificationShare", null);
        _readMinutes = preferences.GetInt("readMinutes", 2);
        _scheduledRefresh = () =>
        {
            _ = RefreshAsync();
            _handler.PostDelayed(_scheduledRefresh, _readMinutes * 60 * 1000L);
        };
        BuildView();
        _ = RefreshAsync();
    }

    private void BuildView()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(28, 30, 28, 20);

        var title = new TextView(this) { Text = "PingNotify", TextSize = 28 };
        title.SetTextColor(Color.Rgb(23, 35, 60));
        root.AddView(title);
        root.AddView(new TextView(this) { Text = "Read-only view across all machines", TextSize = 15 });

        _sasInput = new EditText(this)
        {
            Hint = "Private container SAS URI",
            Text = _sasUri,
            InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextVariationUri
        };
        root.AddView(_sasInput);
        var readInput = new EditText(this)
        {
            Hint = "Remote read interval in minutes",
            Text = _readMinutes.ToString(),
            InputType = global::Android.Text.InputTypes.ClassNumber
        };
        root.AddView(readInput);
        var save = new Button(this) { Text = "Save private read access" };
        save.Click += (_, _) =>
        {
            _sasUri = _sasInput.Text?.Trim();
            if (int.TryParse(readInput.Text, out var minutes) && minutes is >= 1 and <= 1440)
                _readMinutes = minutes;
            GetPreferences(global::Android.Content.FileCreationMode.Private).Edit()?
                .PutString("notificationShare", _sasUri)?
                .PutInt("readMinutes", _readMinutes)?
                .Apply();
            _ = RefreshAsync();
        };
        root.AddView(save);

        _refresh = new Button(this) { Text = "Refresh" };
        _refresh.Click += (_, _) => _ = RefreshAsync();
        root.AddView(_refresh);
        _status = new TextView(this) { Text = "Ready", TextSize = 14 };
        root.AddView(_status);
        _results = new TextView(this) { TextSize = 16 };
        _results.SetTextColor(Color.Rgb(23, 35, 60));
        var scroll = new ScrollView(this);
        scroll.AddView(_results);
        root.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        SetContentView(root);
    }

    private async Task RefreshAsync()
    {
        if (!Uri.TryCreate(_sasUri, UriKind.Absolute, out var container) ||
            !container.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(container.Query))
        {
            SetStatus("Enter a complete private container SAS URI.");
            return;
        }

        SetBusy(true);
        try
        {
            var blobs = await ListBlobsAsync(container);
            var rows = new List<string>();
            foreach (var blob in blobs.Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var machine = System.IO.Path.GetFileNameWithoutExtension(blob);
                var document = JsonDocument.Parse(await GetTextAsync(
                    BlobUri(container, blob),
                    $"reading {blob}"));
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.NameEquals("_pingnotify")) continue;
                    if (!property.Value.TryGetProperty("seq", out var seq) ||
                        !property.Value.TryGetProperty("last", out _)) continue;
                    if (seq.TryGetInt64(out var quantity) && quantity > 0)
                        rows.Add($"{machine}  ·  {property.Name}  ·  {quantity}");
                }
            }
            SetResults(rows);
        }
        catch (HttpRequestException ex)
        {
            SetStatus($"Azure read failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            SetStatus($"Read failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<IReadOnlyList<string>> ListBlobsAsync(Uri container)
    {
        var parts = container.AbsoluteUri.Split('?', 2);
        var uri = $"{parts[0].TrimEnd('/')}?restype=container&comp=list&{parts[1]}";
        var xml = XDocument.Parse(await GetTextAsync(uri, "listing the notification container"));
        return xml.Descendants()
            .Where(element => element.Name.LocalName == "Blob")
            .Elements()
            .Where(element => element.Name.LocalName == "Name")
            .Select(element => element.Value)
            .ToArray();
    }

    private async Task<string> GetTextAsync(string uri, string operation)
    {
        using var response = await _http.GetAsync(uri);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"{operation} returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). " +
                "The SAS must be for the private notification container and include read (r) and list (l) permissions.");
        return await response.Content.ReadAsStringAsync();
    }

    private static string BlobUri(Uri container, string blob)
    {
        var parts = container.AbsoluteUri.Split('?', 2);
        return $"{parts[0].TrimEnd('/')}/{Uri.EscapeDataString(blob)}?{parts[1]}";
    }

    private void SetResults(IReadOnlyList<string> rows)
    {
        RunOnUiThread(() =>
        {
            _results.Text = rows.Count == 0
                ? "No pending notifications."
                : string.Join(System.Environment.NewLine + System.Environment.NewLine, rows);
            _status.Text = $"Read {System.DateTime.Now:t}";
        });
    }

    private void SetStatus(string message) => RunOnUiThread(() => _status.Text = message);
    private void SetBusy(bool busy) => RunOnUiThread(() => _refresh.Enabled = !busy);

    protected override void OnResume()
    {
        base.OnResume();
        _handler.RemoveCallbacksAndMessages(null);
        _handler.PostDelayed(_scheduledRefresh, _readMinutes * 60 * 1000L);
    }

    protected override void OnDestroy()
    {
        _handler.RemoveCallbacksAndMessages(null);
        _http.Dispose();
        base.OnDestroy();
    }
}
