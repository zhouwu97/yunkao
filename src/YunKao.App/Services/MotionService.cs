using System.Runtime.CompilerServices;
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
    private const string ScaleChannel = "scale";
    private const string TranslateChannel = "translate";
    private const string DrawerChannel = "drawer";
    private static readonly ConditionalWeakTable<FrameworkElement, MotionState> States = new();

    private sealed class MotionState
    {
        public Dictionary<string, Storyboard> Storyboards { get; } = [];
    }

    public static bool ReduceMotion { get; private set; } = DetectReducedMotion();

    public static void SetReduceMotion(bool reduceMotion) => ReduceMotion = reduceMotion;

    public static void AnimateScale(FrameworkElement element, double target, double durationMs = 120)
        => AnimateScaleXY(element, target, target, durationMs);

    public static void AnimateScaleXY(
        FrameworkElement element,
        double targetX,
        double targetY,
        double durationMs = 120,
        Action? completed = null)
    {
        ScaleTransform transform = EnsureScaleTransform(element);
        if (ReduceMotion)
        {
            transform.ScaleX = targetX;
            transform.ScaleY = targetY;
            completed?.Invoke();
            return;
        }

        var storyboard = new Storyboard();
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var scaleX = new DoubleAnimation { To = targetX, Duration = duration, EasingFunction = easing };
        var scaleY = new DoubleAnimation { To = targetY, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(scaleX, transform);
        Storyboard.SetTarget(scaleY, transform);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        Run(element, ScaleChannel, storyboard, completed);
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
        Run(element, "page", storyboard);
    }

    public static void AnimateTranslateX(FrameworkElement element, double target, double durationMs = 220)
        => AnimateTranslateX(element, target, durationMs, null);

    public static void AnimateTranslateX(
        FrameworkElement element,
        double target,
        double durationMs,
        Action? completed)
    {
        TranslateTransform transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        if (ReduceMotion)
        {
            transform.X = target;
            completed?.Invoke();
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
        Run(element, TranslateChannel, storyboard, completed);
    }

    public static void AnimateOpacity(
        FrameworkElement element,
        double target,
        double durationMs = 160,
        string channel = "opacity",
        Action? completed = null)
    {
        if (ReduceMotion)
        {
            element.Opacity = target;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Run(element, channel, storyboard, completed);
    }

    public static void AnimateTranslateY(FrameworkElement element, double target, double durationMs = 180, Action? completed = null)
    {
        TranslateTransform transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        if (ReduceMotion)
        {
            transform.Y = target;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, "Y");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Run(element, "translate-y", storyboard, completed);
    }

    public static void AnimateTranslateAndOpacity(
        FrameworkElement element,
        double translateX,
        double opacity,
        double durationMs = 180,
        Action? completed = null)
    {
        TranslateTransform transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        if (ReduceMotion)
        {
            transform.X = translateX;
            element.Opacity = opacity;
            completed?.Invoke();
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var translate = new DoubleAnimation { To = translateX, Duration = duration, EasingFunction = easing };
        var fade = new DoubleAnimation { To = opacity, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(translate, transform);
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(translate, "X");
        Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(translate);
        storyboard.Children.Add(fade);
        Run(element, DrawerChannel, storyboard, completed);
    }

    public static void SetTranslateX(FrameworkElement element, double value)
    {
        TranslateTransform transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        transform.X = value;
    }

    public static void SetSoftShadow(FrameworkElement element, bool enabled)
    {
        // WinUI 3 2.3.1 暴露的是 UIElement.Shadow；ThemeShadow 会随系统主题和环境自动调整。
        element.Shadow = enabled ? new ThemeShadow() : null;
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

    private static void Run(FrameworkElement element, string channel, Storyboard storyboard, Action? completed = null)
    {
        MotionState state = States.GetOrCreateValue(element);
        Stop(element, channel);
        state.Storyboards[channel] = storyboard;

        storyboard.Completed += (_, _) =>
        {
            if (state.Storyboards.TryGetValue(channel, out Storyboard? current)
                && ReferenceEquals(current, storyboard))
            {
                state.Storyboards.Remove(channel);
                completed?.Invoke();
            }
        };

        storyboard.Begin();
    }

    private static void Stop(FrameworkElement element, string channel)
    {
        if (!States.TryGetValue(element, out MotionState? state)
            || !state.Storyboards.TryGetValue(channel, out Storyboard? storyboard))
        {
            return;
        }

        try { storyboard.Stop(); } catch (InvalidOperationException) { }
        state.Storyboards.Remove(channel);
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
