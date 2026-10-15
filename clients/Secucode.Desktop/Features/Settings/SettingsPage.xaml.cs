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
        App.Settings.Save();
        ThemeManager.Apply(choice);
    }

    private void SignOut_Click(object sender, RoutedEventArgs e) => App.SignOut(null);
}
