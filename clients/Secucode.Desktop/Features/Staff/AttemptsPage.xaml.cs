using System.Windows;
using System.Windows.Controls;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Shell;

namespace Secucode.Desktop.Features.Staff;

// Read-only results. Evidence viewing and corrections come in sub-project 5 (web-first).
public partial class AttemptsPage : UserControl
{
    private readonly AttemptsViewModel _vm;

    public AttemptsPage(ExamDto exam)
    {
        InitializeComponent();
        _vm = new AttemptsViewModel(App.Api, exam);
        ExamTitle.Text = exam.Title;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Info.Hide();
        try
        {
            await _vm.LoadAsync();
            Grid.ItemsSource = _vm.Rows;
            Empty.Visibility = _vm.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiError error)
        {
            Info.ShowError(error, () => _ = LoadAsync());
        }
    }

    private async void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not AttemptRow row) return;
        try
        {
            var detail = await _vm.DetailAsync(row.Attempt.Id);
            DetailStudent.Text = detail.StudentName;
            DetailReason.Text = $"{row.Status} · attempt {detail.Attempt.AttemptNumber}" +
                                (detail.Attempt.SubmitReason is { } reason ? $" · {reason}" : "");
            DetailQuestion.Text = detail.Attempt.QuestionText;
            DetailEvents.ItemsSource = detail.Events.Count == 0
                ? new List<string> { "None" }
                : detail.Events.Select(ev => $"{ev.OccurredAt.ToLocalTime():HH:mm:ss}  {ev.Type}{(ev.Detail is null ? "" : $" ({ev.Detail})")}").ToList();
            DetailLanguage.Text = $"Code · {detail.Attempt.Language ?? "not saved yet"}";
            DetailCode.Text = detail.Attempt.Code;
            DetailCard.Visibility = Visibility.Visible;
        }
        catch (ApiError error)
        {
            Info.ShowError(error);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        ShellWindow.Current?.Navigate(new StaffExamsPage(App.Session!));
}
