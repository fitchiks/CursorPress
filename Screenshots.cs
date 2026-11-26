using System.Drawing.Imaging;

namespace CursorPress;

internal static class Screenshots
{
    internal static int Save(string path, string? language)
    {
        using var form = new MainForm(smoke: true, autoClose: false, language: language)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();
        var surface = form.Controls[0];
        using var bitmap = new Bitmap(surface.Width, surface.Height);
        surface.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bitmap.Save(path, ImageFormat.Png);
        form.Hide();
        return 0;
    }
}
