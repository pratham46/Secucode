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

    // New page: fade in while sliding up 12 px.
    public static void PageIn(FrameworkElement page)
    {
        if (!Enabled) return;
        var move = new TranslateTransform(0, 12);
        page.RenderTransform = move;
        var d = Get("Motion.Page");
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = Ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, d) { EasingFunction = Ease });
    }

    // InfoBars and overlays: fade in while scaling up from 96 %.
    public static void PopIn(FrameworkElement element)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform(0.96, 0.96);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var d = Get("Motion.Pop");
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = Ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, d) { EasingFunction = Ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, d) { EasingFunction = Ease });
    }

    // Sidebar selection indicator.
    public static void SlideTo(TranslateTransform transform, double y)
    {
        if (!Enabled)
        {
            transform.Y = y;
            return;
        }
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, Get("Motion.Page")) { EasingFunction = Ease });
    }
}
