using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class ScreenCaptureService
{
    public string CaptureAsBase64()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Jpeg);
        return Convert.ToBase64String(ms.ToArray());
    }
}
