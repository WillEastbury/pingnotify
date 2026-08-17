using System.Text.Json;
using System.IO;

namespace PingNotify;

internal sealed class PollingSettings
{
    public int WriteMinutes { get; set; } = 2;
    public int ReadMinutes { get; set; } = 2;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PingNotify",
        "settings.json");

    public static PollingSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<PollingSettings>(File.ReadAllText(FilePath));
                if (settings is not null && settings.WriteMinutes is >= 1 and <= 1440 && settings.ReadMinutes is >= 1 and <= 1440)
                    return settings;
            }
        }
        catch (JsonException)
        {
        }
        return new PollingSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}

internal static class PollingSettingsDialog
{
    public static bool Show(PollingSettings settings)
    {
        using var dialog = new Form
        {
            Text = "PingNotify polling",
            Width = 430,
            Height = 270,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true
        };
        using var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 2,
            RowCount = 4
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        panel.Controls.Add(new Label { Text = "Publish local metadata (minutes)", AutoSize = true }, 0, 0);
        panel.Controls.Add(new Label { Text = "Read remote metadata (minutes)", AutoSize = true }, 0, 1);
        using var write = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = settings.WriteMinutes, Dock = DockStyle.Fill };
        using var read = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = settings.ReadMinutes, Dock = DockStyle.Fill };
        panel.Controls.Add(write, 1, 0);
        panel.Controls.Add(read, 1, 1);
        using var help = new Label
        {
            Text = "Changes are stored locally and apply immediately.",
            AutoSize = true
        };
        panel.Controls.Add(help, 0, 2);
        panel.SetColumnSpan(help, 2);
        using var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12)
        };
        using var save = new Button { Text = "Save", Width = 100, Height = 34, DialogResult = DialogResult.OK };
        using var cancel = new Button { Text = "Cancel", Width = 100, Height = 34, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        dialog.Controls.Add(panel);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog() != DialogResult.OK)
            return false;
        settings.WriteMinutes = (int)write.Value;
        settings.ReadMinutes = (int)read.Value;
        settings.Save();
        return true;
    }
}
