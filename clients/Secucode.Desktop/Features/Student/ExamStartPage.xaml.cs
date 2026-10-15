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

    private void UpdateBegin() =>
        BeginButton.IsEnabled = _referencePng is not null && (_vm.IsResume || Pin.Password.Length > 0);

    private void Pin_PasswordChanged(object sender, RoutedEventArgs e) => UpdateBegin();

    private void Back_Click(object sender, RoutedEventArgs e) => ShellWindow.Current?.Navigate(new StudentExamsPage());

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        CaptureButton.IsEnabled = false;
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
        finally
        {
            CaptureButton.IsEnabled = true;
            UpdateBegin();
        }
    }

    private async void Begin_Click(object sender, RoutedEventArgs e)
    {
        BeginButton.IsEnabled = false;
        Info.Hide();
        // A signed-out exam window can linger (open dialog) holding the webcam and outbox; never run two at once.
        if (Application.Current.Windows.OfType<ExamWindow>().Any())
        {
            Info.Show(InfoSeverity.Warning, "An exam window is still open. Close it, then try again.");
            UpdateBegin();
            return;
        }
        // Proctoring must work before the server starts the clock (spec §9). Created on the UI thread on purpose.
        var monitor = FaceMonitor.TryStart(_referencePng);
        if (monitor is null)
        {
            Info.Show(InfoSeverity.Error, "The webcam isn't available, so the exam can't start. Close other apps that use the camera and try again.");
            UpdateBegin();
            return;
        }
        try
        {
            var attempt = await _vm.BeginAsync(Pin.Password);
            var window = new ExamWindow(App.Api, _vm.Exam.Title, attempt, monitor, _referencePng!);
            window.Closed += (_, _) => ShellWindow.Current?.Navigate(new StudentExamsPage());
            window.Show();
            ShellWindow.Current?.Navigate(new StudentExamsPage());
        }
        catch (ApiError error)
        {
            monitor.Dispose();
            Info.ShowError(error);
            UpdateBegin();
        }
    }
}
