namespace PingNotify;

internal static class StorageSetup
{
    public static bool EnsureConfigured()
    {
        if (HasConfiguredLocation())
            return true;

        var result = StorageSetupWizard.Show();
        if (result is null)
            return false;
        if (result.SasUri is null)
            return true;

        var sasUri = result.SasUri;
        if (string.IsNullOrWhiteSpace(sasUri) ||
            !Uri.TryCreate(sasUri.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Query))
        {
            MessageBox.Show(
                "A valid HTTPS container SAS URI is required. No environment variable was changed.",
                "PingNotify storage setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        Environment.SetEnvironmentVariable("notificationShare", sasUri.Trim(), EnvironmentVariableTarget.User);
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("The PingNotify executable path is unavailable for restart.");

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true
        });
        return false;
    }

    private static bool HasConfiguredLocation() =>
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("notificationShare", EnvironmentVariableTarget.User)) ||
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("notificationShare", EnvironmentVariableTarget.Process));
}

internal sealed record StorageSetupResult(string? SasUri);

internal static class StorageSetupWizard
{
    public static StorageSetupResult? Show()
    {
        using var form = new Form
        {
            Text = "PingNotify first-run setup",
            Width = 620,
            Height = 360,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true
        };
        var pages = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28) };
        var choicePage = new Panel { Dock = DockStyle.Fill };
        var sasPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        var title = new Label { AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
        var description = new Label { AutoSize = true, MaximumSize = new Size(540, 0), Top = 42 };
        var cloud = new RadioButton { AutoSize = true, Text = "Use shared Azure Blob storage", Top = 112, Checked = true };
        var redirected = new RadioButton { AutoSize = true, Text = "Use TSClient drive redirection", Top = 148 };
        var sasTitle = new Label { AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
        var sasDescription = new Label { AutoSize = true, MaximumSize = new Size(540, 0), Top = 42, Text = "Paste the complete HTTPS container SAS URI. It is stored only as a user-scoped environment variable." };
        var sasBox = new TextBox { Left = 0, Top = 110, Width = 540, UseSystemPasswordChar = true };
        var back = new Button { Text = "Back", Width = 90, DialogResult = DialogResult.None };
        var next = new Button { Text = "Next", Width = 90, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };

        title.Text = "Choose your storage";
        description.Text = "PingNotify needs a place to keep notification metadata. Blob storage lets multiple machines see one another; TSClient keeps data in the redirected session drive.";
        choicePage.Controls.AddRange([title, description, cloud, redirected]);
        sasTitle.Text = "Connect shared storage";
        sasPage.Controls.AddRange([sasTitle, sasDescription, sasBox]);
        pages.Controls.Add(choicePage);
        pages.Controls.Add(sasPage);
        form.Controls.Add(pages);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 16, 8)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(next);
        buttons.Controls.Add(back);
        form.Controls.Add(buttons);
        form.AcceptButton = next;
        form.CancelButton = cancel;

        StorageSetupResult? result = null;
        back.Visible = false;
        next.Click += (_, _) =>
        {
            if (choicePage.Visible)
            {
                if (redirected.Checked)
                {
                    result = new StorageSetupResult(null);
                    form.DialogResult = DialogResult.OK;
                    return;
                }
                choicePage.Visible = false;
                sasPage.Visible = true;
                back.Visible = true;
                next.Text = "Save and restart";
                sasBox.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(sasBox.Text))
            {
                MessageBox.Show("Enter a container SAS URI or use Back to choose TSClient.", "PingNotify", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            result = new StorageSetupResult(sasBox.Text.Trim());
            form.DialogResult = DialogResult.OK;
        };
        back.Click += (_, _) =>
        {
            sasPage.Visible = false;
            choicePage.Visible = true;
            back.Visible = false;
            next.Text = "Next";
        };
        form.ShowDialog();
        return result;
    }
}
