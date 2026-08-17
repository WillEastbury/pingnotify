namespace PingNotify;

internal static class TextDialog
{
    public static string? Show(string title, string initialText, bool readOnly)
    {
        using var dialog = new Form
        {
            Text = title,
            Width = 720,
            Height = 440,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
        using var textBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Text = initialText,
            ReadOnly = readOnly,
            Font = new Font("Segoe UI", 11)
        };
        using var buttons = new FlowLayoutPanel
        {
            Height = 78,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12),
            WrapContents = false
        };
        using var done = new Button
        {
            Text = readOnly ? "Done and clear shared data" : "Remote Copy",
            DialogResult = DialogResult.OK,
            Width = readOnly ? 230 : 150,
            Height = 42,
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            Margin = new Padding(8, 4, 0, 4),
            AutoSize = false
        };
        using var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 120,
            Height = 42,
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            Margin = new Padding(8, 4, 0, 4),
            AutoSize = false
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(done);
        dialog.Controls.Add(textBox);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = done;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => textBox.Focus();

        return dialog.ShowDialog() == DialogResult.OK ? textBox.Text : null;
    }
}
