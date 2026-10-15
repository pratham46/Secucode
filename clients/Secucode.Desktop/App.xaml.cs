using System.Net.Http;
using System.Windows;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Login;
using Secucode.Desktop.Shell;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = null!;
    public static ApiClient Api { get; private set; } = null!;
    public static Session? Session { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "SecuCode", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        Settings = AppSettings.Load();
        ThemeManager.Apply(Settings.Theme);
        ThemeManager.FollowSystem();
        UseServer(Settings.ApiBaseUrl);
        new LoginWindow(null).Show();
    }

    // A fresh client for a (new) server address. Only called while nobody is signed in.
    public static void UseServer(string baseUrl)
    {
        Settings.ApiBaseUrl = baseUrl;
        Api = new ApiClient(new HttpClient { BaseAddress = Settings.BaseUri, Timeout = TimeSpan.FromSeconds(30) });
        Api.SessionExpired += (_, _) => Current.Dispatcher.BeginInvoke(() =>
            SignOut("Your session expired. Please sign in again.", callServer: false));
    }

    public static void SignedIn(MeResponse me)
    {
        Session = new Session(me);
        new ShellWindow(Session).Show();
    }

    // Shows the sign-in window and closes every other window except the exam, which leaves on its own without submitting.
    public static async void SignOut(string? message, bool callServer = true)
    {
        if (callServer)
        {
            try { await Api.LogoutAsync(); } catch (Exception) { } // local sign-out must always proceed
        }
        Session = null;
        if (Current.Windows.OfType<LoginWindow>().Any()) return;
        var login = new LoginWindow(message);
        login.Show();
        foreach (var window in Current.Windows.OfType<Window>().Where(w => w != login && w is not Features.Student.ExamWindow).ToList())
            window.Close();
    }
}
