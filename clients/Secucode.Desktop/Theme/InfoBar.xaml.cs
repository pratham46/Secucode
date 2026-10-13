using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Theme;

public enum InfoSeverity { Info, Success, Warning, Error }

// The one way screens show status and errors: icon + message (+ optional action), coloured by severity.
public partial class InfoBar : UserControl
{
    private Action? _action;

    public InfoBar() => InitializeComponent();

    public void Show(InfoSeverity severity, string message, string? actionLabel = null, Action? action = null)
    {
        var (fill, edge, glyph) = severity switch
        {
            InfoSeverity.Success => ("SuccessSoft", "Success", ""),
            InfoSeverity.Warning => ("WarningSoft", "Warning", ""),
            InfoSeverity.Error => ("DangerSoft", "Danger", ""),
            _ => ("AccentSoft", "Accent", ""),
        };
        Root.SetResourceReference(Border.BackgroundProperty, fill);
        Root.SetResourceReference(Border.BorderBrushProperty, edge);
        Glyph.SetResourceReference(TextBlock.ForegroundProperty, edge);
        Glyph.Text = glyph;
        MessageText.Text = message;
        _action = action;
        ActionButton.Content = actionLabel;
        ActionButton.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;

        var wasHidden = Visibility != Visibility.Visible;
        Visibility = Visibility.Visible;
        if (wasHidden) Transitions.PopIn(Root);
    }

    // Network failures offer Retry; anything else shows the server's message.
    public void ShowError(ApiError error, Action? retry = null) =>
        Show(InfoSeverity.Error, error.Display, error.IsNetwork && retry is not null ? "Retry" : null, error.IsNetwork ? retry : null);

    public void Hide() => Visibility = Visibility.Collapsed;

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _action;
        Hide();
        action?.Invoke();
    }
}
