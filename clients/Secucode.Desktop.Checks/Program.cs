using System.IO;
using Emgu.CV;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Secucode.Desktop.Proctoring;

// Hardware and native-library smoke checks for the desktop app. Run: dotnet run --project clients/Secucode.Desktop.Checks
int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok ? "" : "  -> " + detail)}");
    if (!ok) failures++;
}

void Skip(string name, string why) => Console.WriteLine($"SKIP  {name} ({why})");

// --- Face recognizer (native contrib module must be present) ---
try
{
    var rnd = new Random(1);
    Image<Gray, byte> Noise() { var i = new Image<Gray, byte>(100, 100); for (int y = 0; y < 100; y++) for (int x = 0; x < 100; x++) i.Data[y, x, 0] = (byte)rnd.Next(256); return i; }
    using var a = Noise();
    using var b = Noise();
    using var rec = new LBPHFaceRecognizer();
    using var imgs = new VectorOfMat(a.Mat);
    using var labels = new VectorOfInt(new[] { 1 });
    rec.Train(imgs, labels);
    double same = rec.Predict(a).Distance, other = rec.Predict(b).Distance;
    Check("LBPH same image closer than different", same < other, $"same={same:F1} other={other:F1}");
}
catch (Exception ex) { Check("LBPH loads", false, ex.Message); }

string cascade = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Secucode.Desktop", "haarcascade_frontalface_default.xml");
try { using var c = new CascadeClassifier(cascade); Check("haar cascade loads", true); }
catch (Exception ex) { Check("haar cascade loads", false, ex.Message); }

// --- Image encoding ---
using (var img = new Image<Bgr, byte>(64, 64, new Bgr(40, 80, 120)))
{
    var jpg = Images.Encode(img, ".jpg");
    var png = Images.Encode(img, ".png");
    Check("jpeg encodes", jpg.Length > 2 && jpg[0] == 0xFF && jpg[1] == 0xD8);
    Check("png encodes", png.Length > 4 && png[0] == 0x89 && png[1] == 0x50);
}

// --- Webcam (needs a camera; run on a real PC) ---
var monitor = FaceMonitor.TryStart(null);
if (monitor is null) Skip("webcam", "no camera or model");
else
    using (monitor)
    {
        var (result, _) = monitor.Check();
        Check("webcam check runs", Enum.IsDefined(result), result.ToString());
        Check("webcam frame captures", monitor.CaptureJpeg() is { Length: > 2 } f && f[0] == 0xFF);
    }

// --- Microphone (needs a mic) ---
using (var audio = new AudioRecorder())
{
    byte[]? chunk = null;
    audio.ChunkReady += c => chunk = c;
    if (!audio.Start(out var audioError)) Skip("microphone", audioError ?? "unavailable");
    else
    {
        Thread.Sleep(1500);
        audio.Stop();
        Check("audio chunk is a WAV", chunk is { Length: > 44 } && chunk[0] == (byte)'R' && chunk[1] == (byte)'I', $"len={chunk?.Length}");
    }
}

// --- Screenshot ---
Check("screenshot is a JPEG", ScreenCapture.Jpeg() is { Length: > 2 } s && s[0] == 0xFF && s[1] == 0xD8);

// --- Keyboard hook (installed then removed at once; never left on) ---
try { Lockdown.Enable(); Check("lockdown hook installs", true); }
catch (Exception ex) { Check("lockdown hook installs", false, ex.Message); }
finally { Lockdown.Disable(); }

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
