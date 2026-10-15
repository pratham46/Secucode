using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace Secucode.Desktop.Proctoring;

public static class ScreenCapture
{
    // ponytail: SystemParameters are in DIPs, so on a scaled display this captures the top-left part of the desktop
    // (same as the old app). Switch to per-monitor pixel bounds if staff need the full screen.
    public static byte[]? Jpeg()
    {
        try
        {
            var left = (int)SystemParameters.VirtualScreenLeft;
            var top = (int)SystemParameters.VirtualScreenTop;
            using var bitmap = new Bitmap((int)SystemParameters.VirtualScreenWidth, (int)SystemParameters.VirtualScreenHeight);
            using (var g = Graphics.FromImage(bitmap))
                g.CopyFromScreen(left, top, 0, 0, bitmap.Size);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Jpeg);
            return stream.ToArray();
        }
        catch (Exception)
        {
            return null; // evidence is best-effort; the submission must not fail because of it
        }
    }
}
