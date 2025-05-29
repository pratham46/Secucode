using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Amazon.S3;
using Amazon.S3.Transfer;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Microsoft.Win32;
using MySql.Data.MySqlClient;
using NAudio.Wave;
using Python.Runtime;
using Microsoft.Win32;


namespace Secucode
{
    public partial class TestPage : Window
    {
#if DEBUG
        private readonly bool isDevelopmentMode = true;
#else
        private readonly bool isDevelopmentMode = false;
#endif

        private int windowFocusMissCount = 0;
        private const int MaxFocusMissesAllowed = 3;

        private int studentUserId;
        private Exam currentExam;
        private Random random = new Random();
        private readonly string connectionString = "Server=localhost;Database=secucode;User Id=root;Password=root;";
        private List<Question> testQuestions;
        private DispatcherTimer testTimer;
        private DispatcherTimer autoSaveTimer;
        private TimeSpan timeLeft;

        private WaveInEvent waveIn;
        private WaveFileWriter writer;
        private DispatcherTimer audioTimer;

        private VideoCapture faceCapture;
        private CascadeClassifier faceDetector;
        private DispatcherTimer faceMonitorTimer;
        private int faceMissCount = 0;
        private const int MaxAllowedMisses = 3;
        private const double FaceMatchThreshold = 60;
        private Image<Gray, byte> referenceFace;
        private string malpracticeLogDir;
        private string tempSavePath;
        private string audioLogDir;
        private bool isAutoSubmitted = false;
        private const int TimeoutInMilliseconds = 5000;


        private IntPtr _hookID = IntPtr.Zero;

        private string GetStudentNameById(int userId)
        {
            try
            {
                using var conn = new MySqlConnection("Server=localhost;Database=secucode;User Id=root;Password=root;");
                conn.Open();
                string query = "SELECT username FROM users WHERE id = @userId";
                using var cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@userId", userId);
                object result = cmd.ExecuteScalar();
                if (result != null)
                    return result.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error fetching username: " + ex.Message);
            }
            return "Unknown";
        }

        private string SanitizeForFolder(string input)
        {
            return Regex.Replace(input, "[^a-zA-Z0-9_-]", "_");
        }

        public TestPage(Exam exam, int userId)
        {
            InitializeComponent();
            currentExam = exam;
            studentUserId = userId;

            if (currentExam.TimeLimit <= 0)
            {
                MessageBox.Show("Test time limit is invalid or expired. Cannot start test.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
                return;
            }

            UserIdTextBlock.Text = studentUserId == -1 ? "Teacher Preview Mode" : $"Student ID: {studentUserId}";
            testQuestions = new List<Question>();

            InitializePython();
            LoadQuestionsFromDatabase();

            timeLeft = TimeSpan.FromMinutes(currentExam.TimeLimit);
            StartTimer();

            if (studentUserId != -1)
            {
                StartFaceMonitoring();
                SetupLockdown();
                StartAutoSave();
                StartAudioMonitoring();

                this.Topmost = true;
                this.Deactivated += Window_Deactivated;
                this.StateChanged += Window_StateChanged;

                CodeEditor.PreviewKeyDown += CodeEditor_PreviewKeyDown;
                CodeEditor.PreviewMouseRightButtonDown += (s, e) => { e.Handled = true; };
            }

            this.WindowState = WindowState.Maximized;
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
        }



        // Disable Copy Paste Cut
        private void CodeEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (studentUserId == -1) return;
            if ((Keyboard.Modifiers == ModifierKeys.Control) && (e.Key == Key.C || e.Key == Key.V || e.Key == Key.X))
            {
                e.Handled = true;
            }
        }

        // Lockdown keyboard
        private void SetupLockdown()
        {
            _hookID = SetHook(HookCallback);
        }

        private void RemoveLockdown()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    if ((vkCode == 0x09 && (GetAsyncKeyState(0x12) & 0x8000) != 0) ||
                        (vkCode == 0x1B && (GetAsyncKeyState(0x11) & 0x8000) != 0) ||
                        (vkCode == 0x5B) || (vkCode == 0x5C) ||
                        (vkCode == 0x73 && (GetAsyncKeyState(0x12) & 0x8000) != 0) ||
                        (vkCode == 0x2E && (GetAsyncKeyState(0x11) & 0x8000) != 0)) // Ctrl+Alt+Del simulated detection
                    {
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Detect loss of focus
        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (isDevelopmentMode || studentUserId == -1) return;

            windowFocusMissCount++;

            if (windowFocusMissCount == 1)
                MessageBox.Show("Warning: You switched away from the test window! (1/3)", "Security Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            else if (windowFocusMissCount == 2)
                MessageBox.Show("Final Warning: If you switch again, your test will be auto-submitted! (2/3)", "Security Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            else if (windowFocusMissCount >= MaxFocusMissesAllowed)
            {
                MessageBox.Show("Window lost focus multiple times. Test auto-submitted.", "Security Alert", MessageBoxButton.OK, MessageBoxImage.Error);
                SubmitCodeButton_Click(null, null);
                this.Close();
            }
        }

        // Detect minimized
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (studentUserId == -1) return;
            if (WindowState == WindowState.Minimized)
            {
                MessageBox.Show("Minimized window! Test auto-submitted.", "Security Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                SubmitCodeButton_Click(null, null);
            }
        }


        private void CaptureAndSaveScreenshot(string path)
        {
            try
            {
                using (Bitmap bitmap = new Bitmap((int)SystemParameters.VirtualScreenWidth, (int)SystemParameters.VirtualScreenHeight))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen((int)SystemParameters.VirtualScreenLeft, (int)SystemParameters.VirtualScreenTop, 0, 0, bitmap.Size);
                    }
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Screenshot capture failed: " + ex.Message);
            }
        }

        private void InitializePython()
        {
            string pythonHome = GetPythonInstallPath();
            if (string.IsNullOrEmpty(pythonHome))
            {
                MessageBox.Show("Python installation not found.");
                return;
            }

            string pythonDll = Path.Combine(pythonHome, "python39.dll"); // Update if using different version

            Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);
            Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", pythonDll);

            PythonEngine.Initialize();
        }

        private string GetPythonInstallPath()
        {
            string installPath = null;

            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Python\PythonCore\3.9\InstallPath") ??
                                      Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Python\PythonCore\3.9\InstallPath"))
            {
                if (key != null)
                {
                    installPath = key.GetValue("") as string;
                }
            }

            return installPath;
        }

        private void StartTimer()
        {
            testTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            testTimer.Tick += OnTimedEvent;
            testTimer.Start();
        }

        private void OnTimedEvent(object sender, EventArgs e)
        {
            timeLeft = timeLeft.Subtract(TimeSpan.FromSeconds(1));
            TimerTextBlock.Text = timeLeft.ToString("c");

            if (timeLeft.TotalSeconds <= 0)
            {
                testTimer.Stop();
                SubmitCodeButton_Click(null, null);
            }
        }

        private void LoadQuestionsFromDatabase()
        {
            testQuestions.Clear();
            using (MySqlConnection conn = new MySqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT question_text FROM questions WHERE examination_id = @examId";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@examId", currentExam.Id);
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            testQuestions.Add(new Question { Text = reader.GetString("question_text") });
                        }
                    }
                }
            }
            LoadRandomQuestion();
        }

        private void LoadRandomQuestion()
        {
            if (testQuestions.Count > 0)
            {
                int index = random.Next(testQuestions.Count);
                QuestionTextBlock.Text = testQuestions[index].Text;
            }
            else
            {
                MessageBox.Show("No questions available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TestCodeButton_Click(object sender, RoutedEventArgs e)
        {
            string code = CodeEditor.Text;
            string input = StdinInputBox.Text;
            string selectedCompiler = ((ComboBoxItem)CompilerSelector.SelectedItem).Content.ToString();

            var executor = new Secucode.CodeExecutor(); // ✅ Reference your CodeExecutor class

            string output = selectedCompiler switch
            {
                "Python" => await executor.ExecutePythonCodeAsync(code, input),
                "Java" => executor.ExecuteJavaCode(code, input),
                "C++" => executor.ExecuteCppCode(code, input),
                "JavaScript" => executor.ExecuteJavaScriptCode(code, input),
                _ => "Unknown compiler selected."
            };

            OutputBox.Text = output; // ✅ This must match the name in your XAML
        }


        public async Task<string> ExecutePythonCodeAsync(string code, string input = "")
        {
            try
            {
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    dynamic io = Py.Import("io");

                    sys.stdin = io.StringIO(input);
                    sys.stdout = io.StringIO();
                    sys.stderr = io.StringIO();
                    var scope = Py.CreateScope();

                    var task = Task.Run(() =>
                    {
                        try
                        {
                            scope.Exec(code);
                            return sys.stdout.getvalue().ToString();
                        }
                        catch (PythonException ex)
                        {
                            return $"Python Error: {ex.Message}\n{sys.stderr.getvalue()}";
                        }
                    });

                    if (await Task.WhenAny(task, Task.Delay(TimeoutInMilliseconds)) == task)
                        return task.Result;

                    return "Execution Timeout: Python code took too long.";
                }
            }
            catch (Exception ex)
            {
                return $"Execution error: {ex.Message}";
            }
        }

        public string ExecuteJavaCode(string code, string input = "")
        {
            string javaFile = "UserCode.java";
            File.WriteAllText(javaFile, code);

            string compileOutput = ExecuteCommand("javac", javaFile);
            if (!string.IsNullOrWhiteSpace(compileOutput))
                return $"Java Compilation Error:\n{compileOutput}";

            return ExecuteCommand("java", "-cp . UserCode", input);
        }

        public string ExecuteCppCode(string code, string input = "")
        {
            string cppFile = "UserCode.cpp";
            string exeFile = "UserCode.exe";
            File.WriteAllText(cppFile, code);

            string compileOutput = ExecuteCommand("g++", $"{cppFile} -o {exeFile}");
            if (!string.IsNullOrWhiteSpace(compileOutput))
                return $"C++ Compilation Error:\n{compileOutput}";

            return ExecuteCommand(exeFile, "", input);
        }

        public string ExecuteJavaScriptCode(string code, string input = "")
        {
            string jsFile = "UserCode.js";
            File.WriteAllText(jsFile, code);
            return ExecuteCommand("node", jsFile, input);
        }



        private string ExecuteCommand(string command, string args, string input = "")
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process proc = new Process { StartInfo = psi };
                proc.Start();

                // Feed user input
                if (!string.IsNullOrWhiteSpace(input))
                {
                    using StreamWriter writer = proc.StandardInput;
                    writer.Write(input);
                }

                // Wait with timeout
                if (!proc.WaitForExit(TimeoutInMilliseconds))
                {
                    proc.Kill();
                    return "Error: Execution timed out. Possible missing input or infinite loop.";
                }

                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();

                return string.IsNullOrWhiteSpace(error) ? output : $"Error:\n{error}";
            }
            catch (Exception ex)
            {
                return $"Execution failed: {ex.Message}";
            }
        }

        private void SubmitCodeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (studentUserId == -1)
                {
                    MessageBox.Show("Submission skipped for teacher preview mode.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                    return;
                }
                if (studentUserId <= 0)
                {
                    MessageBox.Show("Invalid student user ID.");
                    return;
                }

                string code = CodeEditor.Text;

                string selectedCompiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLower();
                string extension = selectedCompiler switch
                {
                    "python" => ".py",
                    "java" => ".java",
                    "c++" => ".cpp",
                    "javascript" => ".js",
                    _ => ".txt"
                };

                // Fetch student name and sanitize folder names
                string studentName = GetStudentNameById(studentUserId);
                string examNameSanitized = SanitizeForFolder(currentExam.Name);
                string studentNameSanitized = SanitizeForFolder(studentName);

                // Folder Structure: /SubmittedCodes/Exam_{examId}_{examName}/Student_{userId}_{studentName}/Submissions & Malpractice
                string baseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SubmittedCodes");
                string examFolder = Path.Combine(baseFolder, $"Exam_{currentExam.Id}_{examNameSanitized}");
                string studentFolder = Path.Combine(examFolder, $"Student_{studentUserId}_{studentNameSanitized}");
                string submissionFolder = Path.Combine(studentFolder, "Submissions");
                string malpracticeFolder = Path.Combine(studentFolder, "Malpractice");

                Directory.CreateDirectory(submissionFolder);
                Directory.CreateDirectory(malpracticeFolder);

                // Save Code
                string codeFileName = $"student_{studentUserId}_{currentExam.Id}{extension}";
                string codeFilePath = Path.Combine(submissionFolder, codeFileName);
                File.WriteAllText(codeFilePath, code);

                // Save Webcam Capture
                if (faceCapture != null && faceCapture.Ptr != IntPtr.Zero)
                {
                    using Mat frame = faceCapture.QueryFrame();
                    if (frame != null && !frame.IsEmpty)
                    {
                        var img = frame.ToImage<Bgr, byte>();
                        string facePath = Path.Combine(submissionFolder, "final_face.jpg");
                        img.ToBitmap().Save(facePath);
                    }
                }

                // Save Desktop Screenshot
                try
                {
                    var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                    using Bitmap bmp = new Bitmap(bounds.Width, bounds.Height);
                    using Graphics g = Graphics.FromImage(bmp);
                    g.CopyFromScreen(System.Drawing.Point.Empty, System.Drawing.Point.Empty, bounds.Size);
                    string screenshotPath = Path.Combine(submissionFolder, "desktop_screenshot.jpg");
                    bmp.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Screenshot capture failed: " + ex.Message);
                }

                // Save Reason (why submission happened)
                string reasonPath = Path.Combine(submissionFolder, "reason.txt");
                string reasonText = isAutoSubmitted
                    ? $"Test auto-submitted at {DateTime.Now:G} due to malpractice or window focus loss."
                    : $"Test manually submitted by student at {DateTime.Now:G}.";
                File.WriteAllText(reasonPath, reasonText);

                MessageBox.Show($"✅ Submission Saved Successfully!\n\nSaved as: {codeFileName}");

                // Clean shutdown (stop all monitoring systems)
                StopEverything();
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving submission: {ex.Message}");
            }
        }


        private void StartAutoSave()
        {
            tempSavePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempCodes", $"temp_{studentUserId}_{currentExam.Id}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(tempSavePath));
            autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            autoSaveTimer.Tick += (s, e) => File.WriteAllText(tempSavePath, CodeEditor.Text);
            autoSaveTimer.Start();
        }

        private void StartAudioMonitoring()
        {
            // Organize audio files under the student's "Submissions" folder
            string studentName = GetStudentNameById(studentUserId);
            string examNameSanitized = SanitizeForFolder(currentExam.Name);
            string studentNameSanitized = SanitizeForFolder(studentName);

            string baseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SubmittedCodes");
            string examFolder = Path.Combine(baseFolder, $"Exam_{currentExam.Id}_{examNameSanitized}");
            string studentFolder = Path.Combine(examFolder, $"Student_{studentUserId}_{studentNameSanitized}");
            string submissionFolder = Path.Combine(studentFolder, "Submissions");

            Directory.CreateDirectory(submissionFolder);

            // Set audio recording path inside Submissions
            audioLogDir = submissionFolder;

            waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 1)
            };

            waveIn.DataAvailable += (s, a) =>
            {
                if (writer != null)
                    writer.Write(a.Buffer, 0, a.BytesRecorded);
            };

            waveIn.StartRecording();

            audioTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
            audioTimer.Tick += (s, e) =>
            {
                if (writer != null)
                {
                    writer.Dispose();
                    writer = null;
                }

                string newFile = Path.Combine(audioLogDir, $"audio_{DateTime.Now:HHmmss}.wav");
                writer = new WaveFileWriter(newFile, waveIn.WaveFormat);
            };
            audioTimer.Start();

            // Start first audio file immediately
            string firstFile = Path.Combine(audioLogDir, $"audio_{DateTime.Now:HHmmss}.wav");
            writer = new WaveFileWriter(firstFile, waveIn.WaveFormat);
        }


        private void StopEverything()
        {
            testTimer?.Stop();
            autoSaveTimer?.Stop();
            faceMonitorTimer?.Stop();
            faceCapture?.Dispose();
            waveIn?.StopRecording();
            writer?.Dispose();
            audioTimer?.Stop();
            ShutdownPython();
        }

        private void StartFaceMonitoring()
        {
            try
            {
                faceCapture = new VideoCapture(0, VideoCapture.API.DShow);
                faceCapture.Set(CapProp.FrameWidth, 320);
                faceCapture.Set(CapProp.FrameHeight, 240);

                if (!faceCapture.IsOpened)
                    return;

                string cascadeFileName = "haarcascade_frontalface_default.xml";
                string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cascadeFileName);

                if (!File.Exists(cascadePath))
                    return;

                faceDetector = new CascadeClassifier(cascadePath);

                string refPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CapturedFaces", $"student_{studentUserId}.png");

                if (File.Exists(refPath))
                {
                    var img = new Image<Bgr, byte>(refPath).Convert<Gray, byte>();
                    var faces = faceDetector.DetectMultiScale(img);
                    if (faces.Length > 0)
                        referenceFace = img.Copy(faces[0]).Resize(100, 100, Inter.Linear);
                }

                faceMonitorTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                faceMonitorTimer.Tick += MonitorFacePresence;
                faceMonitorTimer.Start();
            }
            catch
            {
                // Silent fail to avoid runtime crash if any unexpected error occurs
            }
        }

        private void MonitorFacePresence(object sender, EventArgs e)
        {
            try
            {
                if (faceCapture == null || faceCapture.Ptr == IntPtr.Zero || !faceCapture.IsOpened)
                    return;

                using Mat frame = new Mat();
                if (!faceCapture.Read(frame) || frame.IsEmpty)
                    return;

                var image = frame.ToImage<Bgr, byte>();
                var grayLiveImage = image.Convert<Gray, byte>();

                if (referenceFace == null)
                    return;

                var liveFaces = faceDetector.DetectMultiScale(grayLiveImage, 1.1, 10);

                if (liveFaces.Length == 0)
                {
                    faceMissCount++;
                    SaveMalpracticeImage(image, "missing_face");
                    UpdateStrikeDisplay(faceMissCount);
                }
                else
                {
                    var liveFace = grayLiveImage.Copy(liveFaces[0]).Resize(100, 100, Inter.Linear);
                    var diff = referenceFace.AbsDiff(liveFace);
                    double similarity = CvInvoke.Mean(diff).V0;

                    if (similarity <= FaceMatchThreshold)
                    {
                        faceMissCount = 0;
                    }
                    else
                    {
                        faceMissCount++;
                        SaveMalpracticeImage(image, "unauthorized_face");
                    }

                    UpdateStrikeDisplay(faceMissCount);
                }

                if (faceMissCount >= MaxAllowedMisses)
                {
                    faceMonitorTimer?.Stop();
                    MessageBox.Show("❌ Auto-submitting test due to multiple face detection violations.", "Security Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                    isAutoSubmitted = true;
                    SubmitCodeButton_Click(null, null);
                    this.Close();
                }
            }
            catch
            {
                // Silent fail-safe
            }
        }



        private void SaveMalpracticeImage(Image<Bgr, byte> image, string reason)
        {
            try
            {
                // Organized malpractice path
                string studentName = GetStudentNameById(studentUserId);
                string examNameSanitized = SanitizeForFolder(currentExam.Name);
                string studentNameSanitized = SanitizeForFolder(studentName);
                string baseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SubmittedCodes");
                string examFolder = Path.Combine(baseFolder, $"Exam_{currentExam.Id}_{examNameSanitized}");
                string studentFolder = Path.Combine(examFolder, $"Student_{studentUserId}_{studentNameSanitized}");
                string malpracticeFolder = Path.Combine(studentFolder, "Malpractice");

                Directory.CreateDirectory(malpracticeFolder);

                string timestamp = DateTime.Now.ToString("HHmmss");
                string filename = Path.Combine(malpracticeFolder, $"{reason}_{timestamp}.jpg");
                image.ToBitmap().Save(filename);
                Debug.WriteLine($"Malpractice image saved: {filename}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error saving malpractice image: " + ex.Message);
            }
        }


        private void UpdateStrikeDisplay(int count)
        {
            Debug.WriteLine($"➡ Updating strike display with count: {count}");

            Dispatcher.Invoke(() =>
            {
                try
                {
                    var active = System.Windows.Media.Brushes.Red;
                    var inactive = System.Windows.Media.Brushes.Gray;

                    if (Strike1 == null || Strike2 == null || Strike3 == null)
                    {
                        Debug.WriteLine("❌ One or more Strike elements are null. Cannot update.");
                        return;
                    }

                    Debug.WriteLine("✅ Strike elements found. Applying colors...");

                    Strike1.Foreground = count >= 1 ? active : inactive;
                    Strike2.Foreground = count >= 2 ? active : inactive;
                    Strike3.Foreground = count >= 3 ? active : inactive;

                    Debug.WriteLine("✅ Strike display updated.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠ UI update failed: {ex.Message}");
                }
            });
        }


        private void Window_Closing(object sender, CancelEventArgs e)
        {
            ShutdownPython();
            faceMonitorTimer?.Stop();
            faceCapture?.Dispose();
        }

        private void ShutdownPython()
        {
            PythonEngine.Shutdown();
        }

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                this.Close();
        }
    }
}
