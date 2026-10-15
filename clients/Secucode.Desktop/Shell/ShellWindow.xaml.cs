using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Features.Settings;
using Secucode.Desktop.Features.Student;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Shell;

// Main window after sign-in: sidebar navigation on the left, the current page on the right.
public partial class ShellWindow : Window
{
    public ShellWindow(Session session)
    {
        InitializeComponent();
        UserName.Text = session.User.FullName;
        UserRoles.Text = session.RoleNames;
        Nav.ItemsSource = new[]
        {
            new NavItem("", "Exams", () => session.IsStudent
                ? new StudentExamsPage()
                : new TextBlock { Text = "Exams", Style = (Style)FindResource("Title") }),
            new NavItem("", "Settings", () => new SettingsPage()),
        };
        Loaded += (_, _) => Nav.SelectedIndex = 0;
    }

    public static ShellWindow? Current => Application.Current.Windows.OfType<ShellWindow>().FirstOrDefault();

    public void Navigate(FrameworkElement page)
    {
        Host.Content = page;
        Transitions.PageIn(page);
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item) return;
        Navigate(item.Create());
        if (Nav.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement row)
            Transitions.SlideTo(IndicatorMove, row.TranslatePoint(new Point(0, 0), Nav).Y);
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        var collapse = Sidebar.Width > 100;
        Sidebar.Width = collapse ? 60 : 232;
        UserCard.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
    }
}
