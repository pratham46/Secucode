using System.Drawing;
using System.IO;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace Secucode.Desktop.Proctoring;

public enum FaceCheck { Ok, NoFrame, Missing, Multiple, Mismatch }

// Webcam presence and identity checks, one frame per call. No UI and no network.
public sealed class FaceMonitor : IDisposable
{
    public const int CheckSeconds = 3;
    private const int DetectMinNeighbors = 5;
    // ponytail: LBPH distance cutoff (lower = same person). Calibrate on real webcams/lighting; ~60-100 is typical.
    private const double MatchMaxDistance = 90;

    private readonly VideoCapture _capture;
    private readonly CascadeClassifier _detector;
    private readonly LBPHFaceRecognizer? _recognizer;

    private FaceMonitor(VideoCapture capture, CascadeClassifier detector, LBPHFaceRecognizer? recognizer)
    {
        _capture = capture;
        _detector = detector;
        _recognizer = recognizer;
    }

    public static CascadeClassifier? LoadDetector()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "haarcascade_frontalface_default.xml");
        return File.Exists(path) ? new CascadeClassifier(path) : null;
    }

    // Null when the webcam or the detection model is unavailable: the exam must not start then.
    public static FaceMonitor? TryStart(byte[]? referencePng)
    {
        var detector = LoadDetector();
        if (detector is null) return null;
        var capture = new VideoCapture(0, VideoCapture.API.DShow);
        if (!capture.IsOpened)
        {
            capture.Dispose();
            detector.Dispose();
            return null;
        }
        capture.Set(CapProp.FrameWidth, 320);
        capture.Set(CapProp.FrameHeight, 240);
        return new FaceMonitor(capture, detector, referencePng is null ? null : Train(referencePng));
    }

    private static LBPHFaceRecognizer Train(byte[] referencePng)
    {
        using var mat = new Mat();
        CvInvoke.Imdecode(referencePng, ImreadModes.Grayscale, mat);
        using var gray = mat.ToImage<Gray, byte>();
        using var face = Prepare(gray); // the reference is already a face crop
        var recognizer = new LBPHFaceRecognizer();
        using var images = new VectorOfMat(face.Mat);
        using var labels = new VectorOfInt([1]);
        recognizer.Train(images, labels);
        return recognizer;
    }

    private static Image<Gray, byte> Prepare(Image<Gray, byte> face)
    {
        var resized = face.Resize(100, 100, Inter.Linear);
        resized._EqualizeHist(); // less sensitive to lighting
        return resized;
    }

    // One check. The frame comes back as JPEG evidence when the check fails.
    public (FaceCheck Result, byte[]? Jpeg) Check()
    {
        using var frame = new Mat();
        if (!_capture.Read(frame) || frame.IsEmpty) return (FaceCheck.NoFrame, null); // a dropped frame is not the student's fault

        using var image = frame.ToImage<Bgr, byte>();
        using var gray = image.Convert<Gray, byte>();
        var faces = _detector.DetectMultiScale(gray, 1.1, DetectMinNeighbors);
        var result = faces.Length switch
        {
            0 => FaceCheck.Missing,
            > 1 => FaceCheck.Multiple,
            _ => Matches(gray, faces[0]) ? FaceCheck.Ok : FaceCheck.Mismatch,
        };
        return (result, result == FaceCheck.Ok ? null : Images.Encode(image, ".jpg"));
    }

    private bool Matches(Image<Gray, byte> gray, Rectangle face)
    {
        if (_recognizer is null) return true; // no reference: presence-only monitoring
        using var crop = gray.Copy(face);
        using var live = Prepare(crop);
        return _recognizer.Predict(live).Distance <= MatchMaxDistance;
    }

    public byte[]? CaptureJpeg()
    {
        using var frame = new Mat();
        return _capture.Read(frame) && !frame.IsEmpty ? Images.Encode(frame, ".jpg") : null;
    }

    public void Dispose()
    {
        _capture.Dispose();
        _detector.Dispose();
        _recognizer?.Dispose();
    }
}
