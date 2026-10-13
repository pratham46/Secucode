using System.Windows;
using System.Windows.Media;

namespace Secucode.Desktop.Tests;

public sealed class ThemeTests
{
    private static ResourceDictionary Load(string file)
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack; // registers the pack:// scheme outside a running app
        return (ResourceDictionary)Application.LoadComponent(new Uri($"/Secucode;component/Theme/{file}", UriKind.Relative));
    }

    [Fact]
    public void Light_and_dark_define_the_same_brush_tokens() => Sta.Run(() =>
    {
        var light = Load("Colors.Light.xaml");
        var dark = Load("Colors.Dark.xaml");
        var keys = light.Keys.Cast<string>().Order().ToList();

        Assert.Equal(keys, dark.Keys.Cast<string>().Order().ToList());
        Assert.All(keys, k =>
        {
            Assert.IsType<SolidColorBrush>(light[k]);
            Assert.IsType<SolidColorBrush>(dark[k]);
        });
        Assert.Equal(22, keys.Count);
    });

    [Fact]
    public void Controls_load_and_expose_the_named_resources() => Sta.Run(() =>
    {
        var controls = Load("Controls.xaml");
        foreach (var key in new[]
                 {
                     "AppWindow", "FocusRing", "PrimaryButton", "SubtleButton", "IconButton", "CaptionButton", "CloseCaptionButton",
                     "ToggleSwitch", "Card", "NavList", "CodeEditor", "Caption", "Body", "Subtitle", "Title", "Display", "Icon",
                     "UiFont", "CodeFont", "IconFont", "ControlRadius", "Motion.Fast", "Motion.Page", "Motion.EaseOut",
                 })
            Assert.True(controls.Contains(key) || controls.MergedDictionaries.Any(d => d.Contains(key)), $"missing {key}");
    });
}
