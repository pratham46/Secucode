using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Api;
using Secucode.Desktop.Shell;

namespace Secucode.Desktop.Features.Student;

public partial class StudentExamsPage : UserControl
{
    public StudentExamsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Info.Hide();
        try
        {
            var now = DateTime.UtcNow;
            var rows = (await App.Api.StudentExamsAsync()).OrderBy(e => e.StartsAt).Select(e => new StudentExamRow(e, now)).ToList();
            List.ItemsSource = rows;
            Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
