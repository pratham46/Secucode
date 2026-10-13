using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using MySql.Data.MySqlClient;
using Secucode;

int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok ? "" : "  -> " + detail)}");
    if (!ok) failures++;
}
void Skip(string name, string why) => Console.WriteLine($"SKIP  {name} ({why})");

// --- Code runner ---
string r = await CodeExecutor.RunAsync("Python", "print('hi')", "");
if (r.Contains("not found")) Skip("python", r);
else
{
    Check("python prints", r.Trim() == "hi", r);
    r = await CodeExecutor.RunAsync("Python", "n=int(input()); print(sum(range(1,n+1)))", "10");
    Check("python reads stdin", r.Trim() == "55", r);
    r = await CodeExecutor.RunAsync("Python", "input()", "");
    Check("missing input gets EOF, not a hang", r.Contains("EOFError") && !r.Contains("timed out"), r);
    r = await CodeExecutor.RunAsync("Python", "while True: pass", "");
    Check("infinite loop times out", r.Contains("timed out"), r);
    r = await CodeExecutor.RunAsync("Python", "print('x'*200000)", "");
    Check("large output doesn't deadlock", r.Trim().Length == 200000, $"len={r.Length}");
    r = await CodeExecutor.RunAsync("Python", "raise ValueError('boom')", "");
    Check("runtime error reported", r.Contains("ValueError") && r.Contains("exit code"), r);
}

r = await CodeExecutor.RunAsync("JavaScript", "console.log(6*7)", "");
if (r.Contains("not found")) Skip("javascript", r); else Check("javascript", r.Trim() == "42", r);

r = await CodeExecutor.RunAsync("Java", "class A { public static void main(String[] a) { System.out.println(\"j\"); } }", "");
if (r.Contains("not found")) Skip("java", r); else Check("java", r.Trim() == "j", r);

r = await CodeExecutor.RunAsync("C++", "#include <iostream>\nint main(){std::cout<<\"c\";}", "");
if (r.Contains("not found")) Skip("c++", r); else Check("c++", r.Trim() == "c", r);

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

// --- Database ---
try
{
    using var conn = Db.Open();
    var tables = new List<string>();
    using (var cmd = new MySqlCommand("SHOW TABLES", conn))
    using (var rd = cmd.ExecuteReader()) while (rd.Read()) tables.Add(rd.GetString(0));
    foreach (var t in new[] { "users", "branches", "classes", "batches", "examinations", "questions", "submissions" })
        Check($"table {t}", tables.Contains(t), "missing - run Database/setup.sql");

    using var pinCmd = new MySqlCommand("SELECT COUNT(*) FROM users WHERE pin IS NOT NULL AND pin NOT LIKE '$2%'", conn);
    Check("all PINs are bcrypt hashes", Convert.ToInt32(pinCmd.ExecuteScalar()) == 0, "plaintext PINs found");
}
catch (Exception ex) { Check("database reachable", false, ex.Message); }

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
