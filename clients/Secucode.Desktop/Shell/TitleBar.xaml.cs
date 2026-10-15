using System.Windows;
using System.Windows.Controls;

namespace Secucode.Desktop.Shell;

// Custom caption for WindowChrome windows: min / max-restore / close.
public partial class TitleBar : UserControl
{
    private Window? _window;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this)!;
            if (_window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
                MaximizeButton.Visibility = Visibility.Collapsed;
            _window.StateChanged += (_, _) => Sync();
            Sync();
        };
    }

    private void Sync()
    {
        var maximized = _window!.WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        // A maximised WindowChrome window overhangs the screen by its resize border; pad it back in.
        _window.BorderThickness = maximized ? new Thickness(7) : new Thickness(0);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(_window!);

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (_window!.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(_window);
        else SystemCommands.MaximizeWindow(_window);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(_window!);
}
