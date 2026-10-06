using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Secucode
{
    public partial class StudentTestDetailsPage : Page
    {
        private readonly Exam currentExam;
        private readonly int studentUserId;
        private bool isFaceCaptured = false;

        public StudentTestDetailsPage(Exam exam, int userId)
        {
            InitializeComponent();
            currentExam = exam;
            studentUserId = userId;
            this.DataContext = currentExam;
        }

        private void CaptureFace_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
                if (!File.Exists(cascadePath))
                {
                    MessageBox.Show("Face detection model not found.");
                    return;
                }
                using var faceDetector = new CascadeClassifier(cascadePath);

                using var capture = new VideoCapture(0, VideoCapture.API.DShow);
                if (!capture.IsOpened)
                {
                    MessageBox.Show("Could not open the webcam.");
                    return;
                }

                // First frames from a webcam are often dark while it adjusts; try for ~3 seconds.
                Image<Bgr, byte>? image = null;
                Rectangle[] faces = Array.Empty<Rectangle>();
                for (int i = 0; i < 30 && faces.Length == 0; i++)
                {
                    using var frame = capture.QueryFrame();
                    if (frame == null || frame.IsEmpty)
                    {
                        System.Threading.Thread.Sleep(100);
                        continue;
                    }
                    image?.Dispose();
                    image = frame.ToImage<Bgr, byte>();
                    using var gray = image.Convert<Gray, byte>();
                    faces = faceDetector.DetectMultiScale(gray, 1.1, 10, System.Drawing.Size.Empty);
                }

                if (image == null)
                {
                    MessageBox.Show("No frame captured from webcam.");
                    return;
                }
                if (faces.Length == 0)
                {
                    image.Dispose();
                    MessageBox.Show("No face detected. Face the camera in good light and try again.");
                    return;
                }

                var face = image.Copy(faces[0]);

                // Save face image
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CapturedFaces");
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, $"student_{studentUserId}.png");
                face.ToBitmap().Save(path, System.Drawing.Imaging.ImageFormat.Png);

                // Display image preview
                CameraPreview.Source = ConvertToBitmapImage(face.ToBitmap());
                isFaceCaptured = true;

                MessageBox.Show("Face captured successfully!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error capturing face: " + ex.Message);
            }
        }

        private BitmapImage ConvertToBitmapImage(Bitmap bitmap)
        {
            using MemoryStream memory = new();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
            memory.Position = 0;

            BitmapImage bmpImage = new();
            bmpImage.BeginInit();
            bmpImage.StreamSource = memory;
            bmpImage.CacheOption = BitmapCacheOption.OnLoad;
            bmpImage.EndInit();
            return bmpImage;
        }

        private void GiveTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isFaceCaptured)
            {
                MessageBox.Show("Please capture your face before starting the test.");
                return;
            }

            var confirm = MessageBox.Show("Are you sure you want to begin the test now?", "Start Test", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            var currentWindow = Window.GetWindow(this);
            new TestPage(currentExam, studentUserId).ShowDialog();
            currentWindow?.Close();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var currentWindow = Window.GetWindow(this);
            if (currentWindow != null)
            {
                currentWindow.Close();
            }
        }
    }
}
