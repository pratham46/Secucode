using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Secucode.Desktop.Theme;

// Code-driven motion. Timings come from Motion.xaml; nothing animates when Windows animations are turned off.
public static class Transitions
{
    private static bool Enabled => SystemParameters.ClientAreaAnimation && Application.Current is not null;
    private static Duration Get(string key) => (Duration)Application.Current.FindResource(key);
    private static IEasingFunction Ease => (IEasingFunction)Application.Current.FindResource("Motion.EaseOut");

    // Final values are set locally and animations use FillBehavior.Stop, so nothing stays held
    // and later local sets (page.Opacity = 0, transform.Y = ...) still take effect.
    private static DoubleAnimation Anim(double from, double to, Duration d) =>
        new(from, to, d) { EasingFunction = Ease, FillBehavior = FillBehavior.Stop };

    // New page: fade in while sliding up 12 px.
    public static void PageIn(FrameworkElement page)
    {
        if (!Enabled) return;
        var move = new TranslateTransform();
        page.RenderTransform = move;
        page.Opacity = 1;
        var d = Get("Motion.Page");
        page.BeginAnimation(UIElement.OpacityProperty, Anim(0, 1, d));
        move.BeginAnimation(TranslateTransform.YProperty, Anim(12, 0, d));
    }

    // InfoBars and overlays: fade in while scaling up from 96 %.
    public static void PopIn(FrameworkElement element)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        element.Opacity = 1;
        var d = Get("Motion.Pop");
        element.BeginAnimation(UIElement.OpacityProperty, Anim(0, 1, d));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.96, 1, d));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.96, 1, d));
    }

    // Sidebar selection indicator.
    public static void SlideTo(TranslateTransform transform, double y)
    {
        var from = transform.Y;
        transform.BeginAnimation(TranslateTransform.YProperty, null); // drop any running animation
        if (!Enabled)
        {
            transform.Y = y;
            return;
        }
        transform.Y = y;
        transform.BeginAnimation(TranslateTransform.YProperty, Anim(from, y, Get("Motion.Page")));
    }
}
