using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Api;
using Secucode.Desktop.Shell;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Staff;

public partial class ExamEditorPage : UserControl
{
    private readonly ExamEditorViewModel _vm;
    private bool _saving;

    public ExamEditorPage(int? examId)
    {
        InitializeComponent();
        _vm = new ExamEditorViewModel(App.Api, examId);
        PageTitle.Text = _vm.IsNew ? "New exam" : "Edit exam";
        Form.IsEnabled = SaveButton.IsEnabled = false;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _vm.LoadAsync();
            DataContext = _vm; // once, after loading: the view model doesn't raise change notifications
            Form.IsEnabled = SaveButton.IsEnabled = true;
        }
        catch (ApiError error)
        {
            Info.ShowError(error, () => _ = LoadAsync());
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        Info.Hide();
        var (request, problem) = _vm.BuildRequest();
        if (request is null)
        {
            Info.Show(InfoSeverity.Error, problem!);
            return;
        }
        _saving = true;
        SaveButton.IsEnabled = false;
        try
        {
            await _vm.SaveAsync(request);
            ShellWindow.Current?.Navigate(new StaffExamsPage(App.Session!));
        }
        catch (ApiError error)
        {
            Info.ShowError(error);
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = true;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShellWindow.Current?.Navigate(new StaffExamsPage(App.Session!));
    private void AddQuestion_Click(object sender, RoutedEventArgs e) => _vm.Questions.Add(new QuestionRow());
    private static QuestionRow Row(object sender) => (QuestionRow)((FrameworkElement)sender).DataContext;
    private void Up_Click(object sender, RoutedEventArgs e) => _vm.MoveQuestion(Row(sender), -1);
    private void Down_Click(object sender, RoutedEventArgs e) => _vm.MoveQuestion(Row(sender), 1);
    private void Remove_Click(object sender, RoutedEventArgs e) => _vm.Questions.Remove(Row(sender));
}
