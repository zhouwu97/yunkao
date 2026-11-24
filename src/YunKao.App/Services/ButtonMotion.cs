using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace YunKao.Services;

internal static class ButtonMotion
{
    private static readonly ConditionalWeakTable<FrameworkElement, object> Attached = new();
    // Listen above the controls so native Click, Command, focus and pointer capture stay intact.
    internal static void Attach(FrameworkElement root)
    {
        if (Attached.TryGetValue(root, out _)) return;
        Attached.Add(root, new object());
        var held = new Dictionary<uint, Button>();
        bool subscribed = false;
        void Reset()
        {
            foreach (Button button in held.Values.Distinct())
            {
                var visual = ElementCompositionPreview.GetElementVisual(button);
                visual.StopAnimation("Scale");
                visual.Scale = Vector3.One;
            }
            held.Clear();
        }
        void OnMotionChanged(object? sender, EventArgs args)
        {
            if (!MotionService.ReduceMotion) return;
            if (root.DispatcherQueue.HasThreadAccess) Reset();
            else root.DispatcherQueue.TryEnqueue(Reset);
        }
        void Subscribe()
        {
            if (subscribed) return;
            MotionService.Changed += OnMotionChanged;
            subscribed = true;
        }
        root.Loaded += (_, _) => Subscribe();
        root.Unloaded += (_, _) =>
        {
            MotionService.Changed -= OnMotionChanged;
            subscribed = false;
            Reset();
        };
        if (root.IsLoaded) Subscribe();
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (MotionService.ReduceMotion || !e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) return;
            DependencyObject? node = e.OriginalSource as DependencyObject;
            while (node is not null && node is not Button) node = VisualTreeHelper.GetParent(node);
            if (node is not Button button || !button.IsEnabled || YunKao.Controls.LiquidGlassInteraction.Owns(button)) return;
            held[e.Pointer.PointerId] = button;
            Animate(button, .956f, .72f);
        }), true);
        PointerEventHandler release = (_, e) =>
        {
            if (held.Remove(e.Pointer.PointerId, out var button)) Animate(button, 1, .54f);
        };
        root.AddHandler(UIElement.PointerReleasedEvent, release, true);
        root.AddHandler(UIElement.PointerCanceledEvent, release, true);
        root.AddHandler(UIElement.PointerCaptureLostEvent, release, true);
    }

    private static void Animate(Button button, float scale, float damping)
    {
        var visual = ElementCompositionPreview.GetElementVisual(button);
        visual.CenterPoint = new Vector3((float)button.ActualWidth / 2, (float)button.ActualHeight / 2, 0);
        if (MotionService.ReduceMotion)
        {
            visual.StopAnimation("Scale");
            visual.Scale = Vector3.One;
            return;
        }
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(scale, scale, 1);
        spring.Period = TimeSpan.FromMilliseconds(180);
        spring.DampingRatio = damping;
        visual.StartAnimation("Scale", spring);
    }
}
