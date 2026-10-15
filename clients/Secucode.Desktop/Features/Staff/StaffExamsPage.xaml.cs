using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Secucode.Desktop.Api;
using Secucode.Desktop.Shell;

namespace Secucode.Desktop.Features.Staff;

public partial class StaffExamsPage : UserControl
{
    private readonly StaffExamsViewModel _vm;

    public StaffExamsPage(Session session)
    {
        InitializeComponent();
        _vm = new StaffExamsViewModel(App.Api, session);
        FromDate.SelectedDate = _vm.From;
        ToDate.SelectedDate = _vm.To;
        NewButton.Visibility = _vm.CanCreate ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
        Loaded += async (_, _) => await LoadAsync();
    }

    private StaffExamRow? Selected => Grid.SelectedItem as StaffExamRow;

    private async Task LoadAsync()
    {
        Info.Hide();
        _vm.From = FromDate.SelectedDate ?? _vm.From;
        _vm.To = ToDate.SelectedDate ?? _vm.To;
        await RunAsync(_vm.LoadAsync);
    }

    // Runs an action, then refreshes the grid; errors go to the InfoBar.
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            Grid.ItemsSource = _vm.Rows;
        }
        catch (ApiError error)
        {
            Info.ShowError(error, () => _ = LoadAsync());
        }
        UpdateActions();
    }

    private void UpdateActions()
    {
        var row = Selected;
        AttemptsButton.IsEnabled = row?.CanViewResults == true;
        EditButton.IsEnabled = ToggleButton.IsEnabled = DeleteButton.IsEnabled = row?.CanManage == true;
        ToggleButton.Content = row?.Exam.IsActive == false ? "Activate" : "Deactivate";
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected?.CanViewResults == true) Attempts_Click(sender, e);
    }

    private void Attempts_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row) ShellWindow.Current?.Navigate(new AttemptsPage(row.Exam));
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row) _ = RunAsync(() => _vm.ToggleActiveAsync(row));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (MessageBox.Show(Window.GetWindow(this)!, $"Delete \"{row.Title}\"? This can't be undone.", "Delete exam",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            _ = RunAsync(() => _vm.DeleteAsync(row));
    }

    private void New_Click(object sender, RoutedEventArgs e) { } // wired to ExamEditorPage in Task 13
    private void Edit_Click(object sender, RoutedEventArgs e) { } // wired to ExamEditorPage in Task 13
}
