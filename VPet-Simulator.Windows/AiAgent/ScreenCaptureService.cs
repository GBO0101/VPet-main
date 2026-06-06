using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class ScreenCaptureService
{
    public int CaptureWidth { get; set; } = 1000;

    public int CaptureHeight { get; set; } = 800;
    public string CaptureAsBase64()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

        var x = Math.Max(0, Math.Min(cursor.X - CaptureWidth / 2, screen.Width - CaptureWidth));
        var y = Math.Max(0, Math.Min(cursor.Y - CaptureHeight / 2, screen.Height - CaptureHeight));
        var width = Math.Min(CaptureWidth, screen.Width - x);
        var height = Math.Min(CaptureHeight, screen.Height - y);

        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height));
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Jpeg);
        return Convert.ToBase64String(ms.ToArray());
    }
}
