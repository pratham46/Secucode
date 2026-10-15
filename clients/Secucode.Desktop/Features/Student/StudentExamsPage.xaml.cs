using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Outbox;
using Secucode.Desktop.Shell;

namespace Secucode.Desktop.Features.Student;

public partial class StudentExamsPage : UserControl
{
    // Rows re-check the time so Start enables when the window opens without a manual refresh.
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private List<StudentExamRow> _rows = [];

    public StudentExamsPage()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => List.ItemsSource = _rows = _rows.Select(r => r with { NowUtc = DateTime.UtcNow }).ToList();
        Loaded += async (_, _) => { _clock.Start(); await LoadAsync(); };
        Unloaded += (_, _) => _clock.Stop();
    }

    private async Task LoadAsync()
    {
        Info.Hide();
        try
        {
            var now = DateTime.UtcNow;
            var rows = (await App.Api.StudentExamsAsync()).OrderBy(e => e.StartsAt).Select(e => new StudentExamRow(e, now)).ToList();
            List.ItemsSource = _rows = rows;
            Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // An open exam window owns its outbox folder, so sweep only when none is open.
            if (!Application.Current.Windows.OfType<ExamWindow>().Any())
                await AttemptOutbox.SweepAsync(AttemptOutbox.DefaultRoot,
                    rows.Select(r => r.Exam.InProgressAttemptId).OfType<int>().ToHashSet(),
                    async id => (await App.Api.AttemptAsync(id)).Attempt.Status != AttemptStatus.InProgress);
        }
        catch (ApiError error)
        {
            Info.ShowError(error, () => _ = LoadAsync());
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is StudentExamRow row)
            ShellWindow.Current?.Navigate(new ExamStartPage(row.Exam));
    }
}
