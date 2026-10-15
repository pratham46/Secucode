using System.IO;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.Util;

namespace Secucode.Desktop.Proctoring;

public static class Images
{
    public static byte[] Encode(IInputArray image, string extension)
    {
        using var buffer = new VectorOfByte();
        CvInvoke.Imencode(extension, image, buffer);
        return buffer.ToArray();
    }

    public static BitmapImage ToBitmap(byte[] encoded)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(encoded);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze(); // usable from any thread
        return bitmap;
    }
}
