namespace PingNotify;

internal static class TextDialog
{
    public static string? Show(string title, string initialText, bool readOnly)
    {
        using var dialog = new Form
        {
            Text = title,
            Width = 620,
            Height = 360,
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
            ReadOnly = readOnly
        };
        using var buttons = new FlowLayoutPanel
        {
            Height = 42,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft
        };
        using var done = new Button
        {
            Text = readOnly ? "Done and clear shared data" : "Remote Copy",
            DialogResult = DialogResult.OK
        };
        using var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
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
