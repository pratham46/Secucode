using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.Structure;

namespace Secucode.Desktop.Proctoring;

public sealed record FaceCapture(byte[] Png, BitmapImage Preview);

// Captures the student's reference face before the exam. Opens and closes its own camera.
public static class FaceReference
{
    public static FaceCapture Capture()
    {
        using var detector = FaceMonitor.LoadDetector()
            ?? throw new InvalidOperationException("The face detection model is missing. Reinstall SecuCode.");
        using var capture = new VideoCapture(0, VideoCapture.API.DShow);
        if (!capture.IsOpened)
            throw new InvalidOperationException("Couldn't open the webcam. Close other apps that use the camera and try again.");

        // Cameras start dark while they adjust: try for about 3 seconds.
        for (var i = 0; i < 30; i++)
        {
            using var frame = capture.QueryFrame();
            if (frame is null || frame.IsEmpty)
            {
                Thread.Sleep(100);
                continue;
            }
            using var image = frame.ToImage<Bgr, byte>();
            using var gray = image.Convert<Gray, byte>();
            var faces = detector.DetectMultiScale(gray, 1.1, 10, System.Drawing.Size.Empty);
            if (faces.Length == 0) continue;

            using var face = image.Copy(faces[0]);
            var png = Images.Encode(face, ".png");
            return new FaceCapture(png, Images.ToBitmap(png));
        }
        throw new InvalidOperationException("No face detected. Face the camera in good light and try again.");
    }
}
