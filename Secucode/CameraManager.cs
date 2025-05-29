using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Drawing;
using System.IO;

namespace Secucode
{
    public class CameraManager : IDisposable
    {
        private VideoCapture capture;
        private CascadeClassifier faceDetector;

        public CameraManager()
        {
            capture = new VideoCapture(0); // Initialize webcam
            string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
            faceDetector = new CascadeClassifier(cascadePath);
        }

        public Bitmap CaptureFace()
        {
            using Mat frame = capture.QueryFrame();
            if (frame == null) return null;

            using var image = frame.ToImage<Bgr, byte>();
            var gray = image.Convert<Gray, byte>();
            var faces = faceDetector.DetectMultiScale(gray);

            if (faces.Length > 0)
            {
                var faceRect = faces[0];
                return image.Copy(faceRect).ToBitmap();
            }

            return null;
        }

        public void SaveFaceToDisk(Bitmap faceImage, string filePath)
        {
            faceImage.Save(filePath, System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        public void Dispose()
        {
            capture?.Dispose();
        }
    }
}
