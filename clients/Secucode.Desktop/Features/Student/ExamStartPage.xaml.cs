using System.Windows;
using System.Windows.Controls;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Proctoring;
using Secucode.Desktop.Shell;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Student;

public partial class ExamStartPage : UserControl
{
    private readonly ExamStartViewModel _vm;
    private byte[]? _referencePng;

    public ExamStartPage(StudentExamDto exam)
    {
        InitializeComponent();
        _vm = new ExamStartViewModel(App.Api, exam);
        var row = new StudentExamRow(exam, DateTime.UtcNow);
        ExamTitle.Text = exam.Title;
        WhenText.Text = row.When;
        ModeText.Text = row.ModeText;
        PinPanel.Visibility = _vm.IsResume ? Visibility.Collapsed : Visibility.Visible;
        BeginButton.Content = _vm.IsResume ? "Resume exam" : "Start exam";
        UpdateBegin();
    }

    private bool _busy; // true while Begin_Click runs: the camera belongs to the monitor and the form is frozen

    private void UpdateBegin()
    {
        BeginButton.IsEnabled = !_busy && _referencePng is not null && (_vm.IsResume || Pin.Password.Length > 0);
        CaptureButton.IsEnabled = !_busy;
        Pin.IsEnabled = !_busy;
    }

    private void Pin_PasswordChanged(object sender, RoutedEventArgs e) => UpdateBegin();

    private void Back_Click(object sender, RoutedEventArgs e) => ShellWindow.Current?.Navigate(new StudentExamsPage());

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        CaptureButton.IsEnabled = false;
        BeginButton.IsEnabled = false;
        Info.Hide();
        try
        {
            var face = await Task.Run(() => FaceReference.Capture()); // up to ~3 s of camera warm-up
            _referencePng = face.Png;
            Preview.Source = face.Preview;
            PreviewHint.Visibility = Visibility.Collapsed;
            CaptureButton.Content = "Retake";
        }
        catch (InvalidOperationException error)
        {
            Info.Show(InfoSeverity.Error, error.Message);
        }
        catch (Exception) // native camera/OpenCV failures
        {
            Info.Show(InfoSeverity.Error, "The camera couldn't be used. Close other apps that use it and try again.");
        }
        finally
        {
            UpdateBegin();
        }
    }

    private async void Begin_Click(object sender, RoutedEventArgs e)
    {
        _busy = true;
        UpdateBegin();
        Info.Hide();
        FaceMonitor? monitor = null; // ours until the exam window takes it over
        try
        {
            // A signed-out exam window can linger (open dialog) holding the webcam and outbox; never run two at once.
            if (Application.Current.Windows.OfType<ExamWindow>().Any())
            {
                Info.Show(InfoSeverity.Warning, "An exam window is still open. Close it, then try again.");
                return;
            }
            // Proctoring must work before the server starts the clock (spec §9). Created on the UI thread on purpose.
            monitor = FaceMonitor.TryStart(_referencePng);
            if (monitor is null)
            {
                Info.Show(InfoSeverity.Error, "The webcam isn't available, so the exam can't start. Close other apps that use the camera and try again.");
                return;
            }
            var attempt = await _vm.BeginAsync(Pin.Password);
            var window = new ExamWindow(App.Api, _vm.Exam.Title, attempt, monitor, _referencePng!);
            monitor = null; // the window owns it now
            window.Closed += (_, _) => ShellWindow.Current?.Navigate(new StudentExamsPage());
            window.Show();
            ShellWindow.Current?.Navigate(new StudentExamsPage());
        }
        catch (ApiError error)
        {
            Info.ShowError(error);
        }
        catch (Exception error)
        {
            Info.Show(InfoSeverity.Error, error.Message);
        }
        finally
        {
            monitor?.Dispose();
            _busy = false;
            UpdateBegin();
        }
    }
}
