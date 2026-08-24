using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace YunKao.Services;

/// <summary>
/// 统一管理轻量交互动效，避免每个控件各自创建不可中断的动画队列。
/// </summary>
public static class MotionService
{
    public static bool ReduceMotion { get; private set; } = DetectReducedMotion();

    public static void SetReduceMotion(bool reduceMotion) => ReduceMotion = reduceMotion;

    public static void AnimateScale(FrameworkElement element, double target, double durationMs = 120)
    {
        ScaleTransform transform = EnsureScaleTransform(element);
        if (ReduceMotion)
        {
            transform.ScaleX = target;
            transform.ScaleY = target;
            return;
        }

        var storyboard = new Storyboard();
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var scaleX = new DoubleAnimation { To = target, Duration = duration, EasingFunction = easing };
        var scaleY = new DoubleAnimation { To = target, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(scaleX, transform);
        Storyboard.SetTarget(scaleY, transform);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Begin();
    }

    public static void AnimatePage(FrameworkElement element)
    {
        if (ReduceMotion)
        {
            element.Opacity = 1;
            return;
        }

        var transform = new TranslateTransform { Y = 4 };
        element.RenderTransform = transform;
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        element.Opacity = 0;

        var storyboard = new Storyboard();
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opacity = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = easing,
        };
        var translate = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = easing,
        };
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTarget(translate, transform);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        Storyboard.SetTargetProperty(translate, "Y");
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translate);
        storyboard.Begin();
    }

    public static void AnimateTranslateX(FrameworkElement element, double target, double durationMs = 220)
    {
        TranslateTransform transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        if (ReduceMotion)
        {
            transform.X = target;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, "X");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private static ScaleTransform EnsureScaleTransform(FrameworkElement element)
    {
        if (element.RenderTransform is ScaleTransform transform)
        {
            return transform;
        }

        transform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        element.RenderTransform = transform;
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        return transform;
    }

    private static bool DetectReducedMotion()
    {
        try
        {
            return !new UISettings().AnimationsEnabled;
        }
        catch
        {
            return false;
        }
    }
}
