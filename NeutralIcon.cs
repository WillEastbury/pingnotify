using System.Drawing.Drawing2D;

namespace PingNotify;

internal static class NeutralIcon
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var face = new SolidBrush(Color.FromArgb(150, 158, 168));
        using var features = new Pen(Color.White, 2.5f);
        graphics.FillEllipse(face, 2, 2, 28, 28);
        graphics.DrawLine(features, 10, 13, 12, 13);
        graphics.DrawLine(features, 20, 13, 22, 13);
        graphics.DrawLine(features, 10, 21, 22, 21);
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
