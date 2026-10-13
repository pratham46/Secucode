using System.Windows;
using Microsoft.Win32;

namespace Secucode.Desktop.Theme;

// Swaps the colour dictionary at runtime. "System" follows Windows' app theme, including later changes.
public static class ThemeManager
{
    private static bool _following;

    public static ThemeChoice Current { get; private set; }
    public static bool IsDark { get; private set; }

    public static void Apply(ThemeChoice choice)
    {
        Current = choice;
        IsDark = choice == ThemeChoice.Dark || (choice == ThemeChoice.System && WindowsUsesDarkTheme());
        var fresh = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Secucode;component/Theme/Colors.{(IsDark ? "Dark" : "Light")}.xaml"),
        };
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var old = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Colors.") == true);
        if (old is null) dictionaries.Insert(0, fresh);
        else dictionaries[dictionaries.IndexOf(old)] = fresh;
    }

    public static void FollowSystem()
    {
        if (_following) return;
        _following = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (Current == ThemeChoice.System && e.Category == UserPreferenceCategory.General)
                Application.Current.Dispatcher.BeginInvoke(() => Apply(ThemeChoice.System));
        };
    }

    public static bool WindowsUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
