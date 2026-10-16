using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Secucode.Desktop.Proctoring;

public static class ScreenCapture
{
    // Pixel bounds of the whole virtual desktop. The app manifest is system-DPI-aware, so these are real pixels.
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    public static byte[]? Jpeg()
    {
        try
        {
            var left = GetSystemMetrics(76);
            var top = GetSystemMetrics(77);
            using var bitmap = new Bitmap(GetSystemMetrics(78), GetSystemMetrics(79));
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
