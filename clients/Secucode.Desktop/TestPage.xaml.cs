using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using MySql.Data.MySqlClient;
using NAudio.Wave;

namespace Secucode
{
    // Exam window. Every way out (submit button, timer, strikes, focus loss, minimize, any other close)
    // ends in Window_Closing, which saves the submission exactly once.
    public partial class TestPage : Window
    {
#if DEBUG
        private const bool IsDevelopmentMode = true;
#else
        private const bool IsDevelopmentMode = false;
#endif

        private const int MaxFocusMissesAllowed = 3;
        private const int MaxAllowedFaceStrikes = 3;
        private const int FaceCheckSeconds = 3;
        private const int FaceDetectMinNeighbors = 5;
        // ponytail: LBPH distance cutoff (lower = same person). Calibrate on real webcams/lighting; ~60-100 is typical.
        private const double FaceMatchMaxDistance = 90;

        private readonly Exam currentExam;
        private readonly int studentUserId;
        private bool IsStudent => studentUserId != -1;

        private string questionText = "";
        private string studentFolder = "";

        private DispatcherTimer? testTimer;
        private DispatcherTimer? autoSaveTimer;
        private DispatcherTimer? faceMonitorTimer;
        private DispatcherTimer? audioTimer;
        private TimeSpan timeLeft;

        private VideoCapture? faceCapture;
        private CascadeClassifier? faceDetector;
        private LBPHFaceRecognizer? faceRecognizer;
        private int faceStrikes;
        private int windowFocusMissCount;

        private WaveInEvent? waveIn;
        private WaveFileWriter? audioWriter;
        private readonly object audioLock = new();

        private string? submitReason; // set once a submission has been triggered
        private bool resourcesReleased;

        public TestPage(Exam exam, int userId)
        {
            InitializeComponent();
            currentExam = exam;
            studentUserId = userId;
            Closing += Window_Closing;

            UserIdTextBlock.Text = IsStudent ? $"Student ID: {studentUserId}" : "Teacher Preview Mode";

            if (!LoadRandomQuestion())
            {
                Abort("No questions available for this exam.");
                return;
            }

            if (IsStudent)
            {
                studentFolder = BuildStudentFolder();
                if (!StartFaceMonitoring())
                {
                    Abort("Webcam is not available. The exam cannot start without it.");
                    return;
                }
                StartAudioMonitoring();
                StartAutoSave();
                SetupLockdown();

                Topmost = true;
                Deactivated += Window_Deactivated;
                StateChanged += Window_StateChanged;

                // Block copy/cut/paste everywhere in the window (all shortcuts and context menus).
                CommandManager.AddPreviewExecutedHandler(this, (s, e) =>
                {
                    if (e.Command == ApplicationCommands.Copy || e.Command == ApplicationCommands.Cut || e.Command == ApplicationCommands.Paste)
                        e.Handled = true;
                });
                CodeEditor.AllowDrop = false;
                StdinInputBox.AllowDrop = false;
            }

            timeLeft = TimeSpan.FromMinutes(currentExam.TimeLimit);
            TimerTextBlock.Text = timeLeft.ToString("c");
            StartTimer();
        }

        // Refuse to run; closes as soon as the window has loaded (Close() isn't allowed inside the constructor).
        private void Abort(string message)
        {
            submitReason = "aborted"; // nothing to submit
            MessageBox.Show(message, "Cannot Start Test", MessageBoxButton.OK, MessageBoxImage.Error);
            Loaded += (_, _) => Close();
        }

        private void AutoSubmit(string reason)
        {
            if (submitReason != null) return;
            submitReason = reason;
            MessageBox.Show(this, reason + "\nYour test is being submitted.", "Security Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }

        // ---------- Question / timer ----------

        private bool LoadRandomQuestion()
        {
            try
            {
                using MySqlConnection conn = Db.Open();
                using MySqlCommand cmd = new MySqlCommand(
                    "SELECT question_text FROM questions WHERE examination_id = @examId ORDER BY RAND() LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@examId", currentExam.Id);
                questionText = cmd.ExecuteScalar() as string ?? "";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading question: " + ex.Message);
            }
            QuestionTextBlock.Text = questionText;
            return questionText.Length > 0;
        }

        private void StartTimer()
        {
            testTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            testTimer.Tick += (s, e) =>
            {
                timeLeft = timeLeft.Subtract(TimeSpan.FromSeconds(1));
                TimerTextBlock.Text = timeLeft.ToString("c");
                if (timeLeft <= TimeSpan.Zero)
                {
                    testTimer.Stop();
                    if (IsStudent) AutoSubmit("Time limit reached.");
                    else Close();
                }
            };
            testTimer.Start();
        }

        // ---------- Running and submitting code ----------

        private string SelectedLanguage => (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Python";

        private async void TestCodeButton_Click(object sender, RoutedEventArgs e)
        {
            TestCodeButton.IsEnabled = false;
            OutputBox.Text = "Running...";
            try
            {
                OutputBox.Text = await CodeExecutor.RunAsync(SelectedLanguage, CodeEditor.Text, StdinInputBox.Text);
            }
            finally
            {
                TestCodeButton.IsEnabled = true;
            }
        }

        private void SubmitCodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsStudent)
            {
                if (MessageBox.Show(this, "Submit your code now? You cannot change it afterwards.", "Submit",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                submitReason ??= "Manually submitted by student.";
            }
            Close();
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (IsStudent && submitReason != "aborted")
            {
                // Any close we didn't initiate (e.g. a key combo the hook missed) still submits.
                submitReason ??= "Test window was closed.";
                StopTimers();
                SaveSubmission(submitReason);
            }
            ReleaseResources();
        }

        private void SaveSubmission(string reason)
        {
            string extension = SelectedLanguage switch
            {
                "Python" => ".py",
                "Java" => ".java",
                "C++" => ".cpp",
                "JavaScript" => ".js",
                _ => ".txt"
            };
            string code = CodeEditor.Text;
            string submissionFolder = Path.Combine(studentFolder, "Submissions");
            string codeFileName = $"student_{studentUserId}_{currentExam.Id}{extension}";

            // Disk first: it holds the evidence and doesn't depend on the DB being reachable.
            try
            {
                Directory.CreateDirectory(submissionFolder);
                File.WriteAllText(Path.Combine(submissionFolder, codeFileName), code);
                File.WriteAllText(Path.Combine(submissionFolder, "reason.txt"), $"{DateTime.Now:G}: {reason}");
                SaveWebcamFrame(Path.Combine(submissionFolder, "final_face.jpg"));
                CaptureScreenshot(Path.Combine(submissionFolder, "desktop_screenshot.jpg"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error saving submission files: " + ex.Message);
            }

            try
            {
                using MySqlConnection conn = Db.Open();
                using MySqlCommand cmd = new MySqlCommand(@"
                    INSERT INTO submissions (examination_id, user_id, question_text, language, code, reason)
                    VALUES (@examId, @userId, @question, @language, @code, @reason)", conn);
                cmd.Parameters.AddWithValue("@examId", currentExam.Id);
                cmd.Parameters.AddWithValue("@userId", studentUserId);
                cmd.Parameters.AddWithValue("@question", questionText);
                cmd.Parameters.AddWithValue("@language", SelectedLanguage);
                cmd.Parameters.AddWithValue("@code", code);
                cmd.Parameters.AddWithValue("@reason", reason.Length > 255 ? reason[..255] : reason);
                cmd.ExecuteNonQuery();
                MessageBox.Show(this, $"Submission saved.\n\nReason: {reason}", "Submitted", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Your code was saved locally as {codeFileName}, but recording it in the database failed:\n{ex.Message}",
                    "Submission Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StartAutoSave()
        {
            string tempSavePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempCodes", $"temp_{studentUserId}_{currentExam.Id}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(tempSavePath)!);
            autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            autoSaveTimer.Tick += (s, e) =>
            {
                try { File.WriteAllText(tempSavePath, CodeEditor.Text); }
                catch (IOException ex) { Debug.WriteLine("Auto-save failed: " + ex.Message); }
            };
            autoSaveTimer.Start();
        }

        // ---------- Evidence folder ----------

        // SubmittedCodes/Exam_{id}_{name}/Student_{id}_{name}/{Submissions,Malpractice}
        private string BuildStudentFolder()
        {
            string studentName = "Unknown";
            try
            {
                using MySqlConnection conn = Db.Open();
                using MySqlCommand cmd = new MySqlCommand("SELECT username FROM users WHERE id = @userId", conn);
                cmd.Parameters.AddWithValue("@userId", studentUserId);
                studentName = cmd.ExecuteScalar() as string ?? studentName;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error fetching username: " + ex.Message);
            }

            static string Sanitize(string s) => Regex.Replace(s, "[^a-zA-Z0-9_-]", "_");
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SubmittedCodes",
                $"Exam_{currentExam.Id}_{Sanitize(currentExam.Name)}",
                $"Student_{studentUserId}_{Sanitize(studentName)}");
        }

        private void SaveMalpracticeImage(Image<Bgr, byte> image, string reason)
        {
            try
            {
                string dir = Path.Combine(studentFolder, "Malpractice");
                Directory.CreateDirectory(dir);
                image.ToBitmap().Save(Path.Combine(dir, $"{reason}_{DateTime.Now:HHmmss}.jpg"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error saving malpractice image: " + ex.Message);
            }
        }

        private void SaveWebcamFrame(string path)
        {
            if (faceCapture == null || !faceCapture.IsOpened) return;
            using Mat frame = new Mat();
            if (faceCapture.Read(frame) && !frame.IsEmpty)
                frame.ToImage<Bgr, byte>().ToBitmap().Save(path);
        }

        private static void CaptureScreenshot(string path)
        {
            try
            {
                using var bitmap = new Bitmap((int)SystemParameters.VirtualScreenWidth, (int)SystemParameters.VirtualScreenHeight);
                using (Graphics g = Graphics.FromImage(bitmap))
                    g.CopyFromScreen((int)SystemParameters.VirtualScreenLeft, (int)SystemParameters.VirtualScreenTop, 0, 0, bitmap.Size);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Screenshot capture failed: " + ex.Message);
            }
        }

        // ---------- Face monitoring ----------

        private bool StartFaceMonitoring()
        {
            try
            {
                string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
                if (!File.Exists(cascadePath))
                    return false;
                faceDetector = new CascadeClassifier(cascadePath);

                faceCapture = new VideoCapture(0, VideoCapture.API.DShow);
                if (!faceCapture.IsOpened)
                    return false;
                faceCapture.Set(CapProp.FrameWidth, 320);
                faceCapture.Set(CapProp.FrameHeight, 240);

                // The saved reference is already a face crop (StudentTestDetailsPage), so use it as-is.
                string refPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CapturedFaces", $"student_{studentUserId}.png");
                if (File.Exists(refPath))
                {
                    using var reference = PrepareFace(new Image<Bgr, byte>(refPath).Convert<Gray, byte>());
                    faceRecognizer = new LBPHFaceRecognizer();
                    using var images = new VectorOfMat(reference.Mat);
                    using var labels = new VectorOfInt(new[] { 1 });
                    faceRecognizer.Train(images, labels);
                }
                // else: presence-only monitoring

                faceMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(FaceCheckSeconds) };
                faceMonitorTimer.Tick += MonitorFacePresence;
                faceMonitorTimer.Start();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Face monitoring failed to start: " + ex.Message);
                return false;
            }
        }

        private static Image<Gray, byte> PrepareFace(Image<Gray, byte> face)
        {
            var resized = face.Resize(100, 100, Inter.Linear);
            resized._EqualizeHist(); // reduces sensitivity to lighting changes
            return resized;
        }

        private void MonitorFacePresence(object? sender, EventArgs e)
        {
            if (submitReason != null || faceCapture == null || faceDetector == null)
                return;

            try
            {
                using Mat frame = new Mat();
                if (!faceCapture.Read(frame) || frame.IsEmpty)
                    return;

                using var image = frame.ToImage<Bgr, byte>();
                using var gray = image.Convert<Gray, byte>();
                var faces = faceDetector.DetectMultiScale(gray, 1.1, FaceDetectMinNeighbors);

                string? violation = null;
                if (faces.Length == 0)
                    violation = "missing_face";
                else if (faces.Length > 1)
                    violation = "multiple_faces";
                else if (faceRecognizer != null)
                {
                    using var live = PrepareFace(gray.Copy(faces[0]));
                    if (faceRecognizer.Predict(live).Distance > FaceMatchMaxDistance)
                        violation = "unauthorized_face";
                }

                if (violation == null)
                {
                    faceStrikes = 0; // strikes count consecutive failed checks
                }
                else
                {
                    faceStrikes++;
                    SaveMalpracticeImage(image, violation);
                }
                UpdateStrikeDisplay(faceStrikes);

                if (faceStrikes >= MaxAllowedFaceStrikes)
                    AutoSubmit($"Face check failed {MaxAllowedFaceStrikes} times in a row ({violation}).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Face check error: " + ex.Message);
            }
        }

        private void UpdateStrikeDisplay(int count)
        {
            var active = System.Windows.Media.Brushes.Red;
            var inactive = System.Windows.Media.Brushes.Gray;
            Strike1.Foreground = count >= 1 ? active : inactive;
            Strike2.Foreground = count >= 2 ? active : inactive;
            Strike3.Foreground = count >= 3 ? active : inactive;
        }

        // ---------- Audio ----------

        private void StartAudioMonitoring()
        {
            string dir = Path.Combine(studentFolder, "Submissions");
            Directory.CreateDirectory(dir);

            try
            {
                waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1) };
                waveIn.DataAvailable += (s, a) =>
                {
                    lock (audioLock) audioWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                };

                void Rotate()
                {
                    lock (audioLock)
                    {
                        audioWriter?.Dispose();
                        audioWriter = new WaveFileWriter(Path.Combine(dir, $"audio_{DateTime.Now:HHmmss}.wav"), waveIn.WaveFormat);
                    }
                }

                Rotate();
                waveIn.StartRecording();

                // New file every 3 minutes so a crash loses at most one chunk.
                audioTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
                audioTimer.Tick += (s, e) => Rotate();
                audioTimer.Start();
            }
            catch (Exception ex)
            {
                // No microphone: record that fact instead of blocking the exam.
                Directory.CreateDirectory(Path.Combine(studentFolder, "Malpractice"));
                File.WriteAllText(Path.Combine(studentFolder, "Malpractice", "audio_unavailable.txt"), $"{DateTime.Now:G}: {ex.Message}");
                waveIn?.Dispose();
                waveIn = null;
            }
        }

        // ---------- Window focus / state ----------

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (IsDevelopmentMode || submitReason != null) return;

            // Let activation settle, then only count it if focus went to another process
            // (our own dialogs, like the submit confirmation, also deactivate this window).
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (submitReason != null || IsActive || ForegroundIsOurProcess()) return;
                RegisterFocusMiss();
            });
        }

        private static bool ForegroundIsOurProcess()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        private void RegisterFocusMiss()
        {
            windowFocusMissCount++;
            if (windowFocusMissCount >= MaxFocusMissesAllowed)
                AutoSubmit("Test window lost focus too many times.");
            else
                MessageBox.Show(this, $"Warning: you switched away from the test window! ({windowFocusMissCount}/{MaxFocusMissesAllowed})\n" +
                                      "Reaching the limit submits your test.", "Security Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                AutoSubmit("Test window was minimized.");
        }

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !IsStudent) // Escape only exits teacher preview
                Close();
        }

        // ---------- Cleanup ----------

        private void StopTimers()
        {
            testTimer?.Stop();
            autoSaveTimer?.Stop();
            faceMonitorTimer?.Stop();
            audioTimer?.Stop();
        }

        private void ReleaseResources()
        {
            if (resourcesReleased) return;
            resourcesReleased = true;

            StopTimers();
            RemoveLockdown();
            faceCapture?.Dispose();
            faceDetector?.Dispose();
            faceRecognizer?.Dispose();
            waveIn?.StopRecording();
            waveIn?.Dispose();
            lock (audioLock)
            {
                audioWriter?.Dispose();
                audioWriter = null;
            }
        }

        // ---------- Keyboard lockdown (low-level hook) ----------

        // Kept in a static field so the GC can't collect the delegate while Windows still calls it.
        private static readonly LowLevelKeyboardProc HookProc = HookCallback;
        private IntPtr hookId = IntPtr.Zero;

        private void SetupLockdown()
        {
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule!;
            hookId = SetWindowsHookEx(WH_KEYBOARD_LL, HookProc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private void RemoveLockdown()
        {
            if (hookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
        }

        // Swallows Alt+Tab, Alt+Esc, Ctrl+Esc, Win keys, Alt+F4.
        // Ctrl+Alt+Del cannot be blocked by any app; focus loss from it is caught by Window_Deactivated.
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

                if ((vkCode == VK_TAB && alt) ||
                    (vkCode == VK_ESCAPE && (alt || ctrl)) ||
                    vkCode == VK_LWIN || vkCode == VK_RWIN ||
                    (vkCode == VK_F4 && alt))
                {
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_F4 = 0x73, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_MENU = 0x12, VK_CONTROL = 0x11;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
