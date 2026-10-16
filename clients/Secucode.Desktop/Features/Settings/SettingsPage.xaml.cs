using System.IO;
using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Settings;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        ThemeChoiceBox.ItemsSource = Enum.GetValues<ThemeChoice>();
        ThemeChoiceBox.SelectedItem = App.Settings.Theme;
        ServerText.Text = App.Settings.ApiBaseUrl;
        SignedInAs.Text = $"Signed in as {App.Session?.User.FullName} ({App.Session?.User.Username})";
        VersionText.Text = $"SecuCode {typeof(App).Assembly.GetName().Version}";
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeChoiceBox.SelectedItem is not ThemeChoice choice || choice == App.Settings.Theme) return;
        App.Settings.Theme = choice;
        ThemeManager.Apply(choice);
        try { App.Settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // ponytail: theme applies this session, just not remembered
    }

    private void SignOut_Click(object sender, RoutedEventArgs e) => App.SignOut(null);
}
