using Emgu.CV;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using Emgu.CV.Util;

// Hardware and native-library smoke checks for the desktop app. Run: dotnet run --project clients/Secucode.Desktop.Checks
int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok ? "" : "  -> " + detail)}");
    if (!ok) failures++;
}

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

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
