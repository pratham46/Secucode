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

    [Fact]
    public void Button_and_toggle_templates_apply_in_every_state() => Sta.Run(() =>
    {
        var res = Load("Controls.xaml");
        res.MergedDictionaries.Add(Load("Colors.Light.xaml"));
        var button = new System.Windows.Controls.Button { Style = (Style)res["PrimaryButton"], Content = "x", IsEnabled = false };
        var toggle = new System.Windows.Controls.CheckBox { Style = (Style)res["ToggleSwitch"], IsChecked = true };
        foreach (var c in new System.Windows.Controls.Control[] { button, toggle })
        {
            c.Resources = res;
            c.ApplyTemplate();
            c.Measure(new Size(200, 100));
        }
        Assert.NotNull(button.Template.FindName("Overlay", button));
    });

    [Fact]
    public void SlideTo_without_an_app_sets_the_value_and_leaves_it_settable() => Sta.Run(() =>
    {
        var t = new System.Windows.Media.TranslateTransform();
        Theme.Transitions.SlideTo(t, 40);
        Assert.Equal(40, t.Y);
        t.Y = 5;
        Assert.Equal(5, t.Y);
    });
}
