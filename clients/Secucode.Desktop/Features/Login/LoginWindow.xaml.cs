using System.IO;
using System.Windows;
using Secucode.Desktop.Api;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Login;

// One sign-in for everyone; the server's answer decides between the student and staff experience.
public partial class LoginWindow : Window
{
    public LoginWindow(string? message)
    {
        InitializeComponent();
        ServerAddress.Text = App.Settings.ApiBaseUrl;
        if (message is not null) Info.Show(InfoSeverity.Warning, message);
        Loaded += (_, _) =>
        {
            Transitions.PopIn(Card);
            Username.Focus();
        };
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        Info.Hide();
        var address = ServerAddress.Text.Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ServerPanel.Visibility = Visibility.Visible;
            Info.Show(InfoSeverity.Error, "Enter the server address as http://host:port.");
            return;
        }
        if (address != App.Settings.ApiBaseUrl)
        {
            App.UseServer(address);
            try { App.Settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // ponytail: address just isn't remembered
        }

        SignInButton.IsEnabled = false;
        try
        {
            var me = await App.Api.LoginAsync(Username.Text.Trim(), Password.Password);
            App.SignedIn(me);
            Close();
        }
        catch (ApiError error)
        {
            Info.ShowError(error);
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void ToggleServer_Click(object sender, RoutedEventArgs e) =>
        ServerPanel.Visibility = ServerPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
}
