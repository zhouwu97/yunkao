using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace YunKao.Services;

/// <summary>
/// 为小面积玻璃岛提供可降级的 Composition backdrop effect。
/// 只使用 WinUI 支持的内置效果，不引入自定义像素着色器。
/// </summary>
public static class CompositionBackdropService
{
    private const string BackdropParameter = "backdrop";
    private static readonly ConditionalWeakTable<FrameworkElement, GlassState> States = new();

    public static bool TryAttach(
        FrameworkElement host,
        float blurAmount = 7.0f,
        float saturation = 1.03f,
        float cornerRadius = 18.0f)
    {
        ArgumentNullException.ThrowIfNull(host);

        // Win2D 1.4 的互操作程序集以 19041 为目标；更低系统直接使用 Acrylic 降级。
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return false;
        return TryAttachWithWin2D(host, blurAmount, saturation, cornerRadius);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryAttachWithWin2D(
        FrameworkElement host,
        float blurAmount,
        float saturation,
        float cornerRadius)
    {
        if (States.TryGetValue(host, out GlassState? existing))
        {
            existing.Visual.Opacity = 1.0f;
            existing.Visual.IsVisible = true;
            Update(host, cornerRadius);
            return true;
        }

        try
        {
            Visual hostVisual = ElementCompositionPreview.GetElementVisual(host);
            Compositor compositor = hostVisual.Compositor;

            var blur = new GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = blurAmount,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter(BackdropParameter),
            };
            var saturationEffect = new SaturationEffect
            {
                Name = "Saturation",
                Saturation = saturation,
                Source = blur,
            };

            CompositionEffectFactory factory = compositor.CreateEffectFactory(
                saturationEffect,
                new[] { "Blur.BlurAmount", "Saturation.Saturation" });
            CompositionEffectBrush effectBrush = factory.CreateBrush();
            effectBrush.SetSourceParameter(BackdropParameter, compositor.CreateBackdropBrush());

            SpriteVisual visual = compositor.CreateSpriteVisual();
            visual.Brush = effectBrush;
            visual.Opacity = 1.0f;
            visual.IsVisible = true;

            var geometry = compositor.CreateRoundedRectangleGeometry();
            var clip = compositor.CreateGeometricClip(geometry);
            visual.Clip = clip;
            ElementCompositionPreview.SetElementChildVisual(host, visual);

            var state = new GlassState(visual, geometry);
            States.Add(host, state);
            host.SizeChanged += (_, _) => Update(host, cornerRadius);
            Update(host, cornerRadius);
            return true;
        }
        catch (Exception)
        {
            // 老系统、禁用合成或图形驱动不支持时由调用方切回 SystemBackdropElement。
            return false;
        }
    }

    public static void SetVisible(FrameworkElement host, bool visible)
    {
        if (!States.TryGetValue(host, out GlassState? state)) return;
        state.Visual.IsVisible = visible;
        state.Visual.Opacity = visible ? 1.0f : 0.0f;
    }

    private static void Update(FrameworkElement host, float cornerRadius)
    {
        if (!States.TryGetValue(host, out GlassState? state)) return;

        float width = Math.Max(0, (float)host.ActualWidth);
        float height = Math.Max(0, (float)host.ActualHeight);
        state.Visual.Size = new Vector2(width, height);
        state.Geometry.Size = new Vector2(width, height);
        state.Geometry.CornerRadius = new Vector2(cornerRadius, cornerRadius);
    }

    private sealed record GlassState(SpriteVisual Visual, CompositionRoundedRectangleGeometry Geometry);
}
