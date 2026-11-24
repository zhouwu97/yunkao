using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using YunKao.Core.Presentation;
using YunKao.Services;

namespace YunKao.Controls;

/// <summary>Observes native input and deforms only the glass field, never the control's layout or content.</summary>
internal sealed class LiquidGlassInteraction : IDisposable
{
    private static readonly ConditionalWeakTable<UIElement, LiquidGlassInteraction> Owners = new();
    private readonly LiquidGlassSurface _surface;
    private readonly Action<LiquidSurfaceMotion, double, double> _paint;
    private readonly Func<bool> _available;
    private readonly LiquidSurfaceMotion _motion = new();
    private UIElement? _trackingRoot;
    private uint? _pointer;
    private bool _captured, _animating, _disposed;
    private long _lastTick;
    private double _lightX = .22, _lightY = .05;
    public UIElement Input { get; }

    public LiquidGlassInteraction(LiquidGlassSurface surface, UIElement input,
        Func<bool> available, Action<LiquidSurfaceMotion, double, double> paint)
    {
        _surface = surface; Input = input; _available = available; _paint = paint;
        // One optical layer owns each native control, including compact buttons with a nested panel.
        Owners.Remove(input);
        Owners.Add(input, this);
        input.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        input.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        input.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        input.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnReleased), true);
        input.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnReleased), true);
        input.PointerExited += OnExited;
        MotionService.Changed += OnMotionChanged;
    }

    internal static bool Owns(UIElement input) => Owners.TryGetValue(input, out var owner) && owner._available();

    private bool IsOwnInput(DependencyObject? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, Input); node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement element && Owners.TryGetValue(element, out var owner) && !ReferenceEquals(owner, this)) return false;
            if (node is ButtonBase or TextBox or PasswordBox or ComboBox or Slider or ScrollBar) return false;
        }
        return true;
    }

    private bool CanCapture(DependencyObject? source)
    {
        if (Input is ButtonBase) return false;
        for (var node = source; node is not null && !ReferenceEquals(node, Input); node = VisualTreeHelper.GetParent(node))
            if (node is SelectorItem || node is TextBlock { IsTextSelectionEnabled: true }) return false;
        return true;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_disposed || _pointer.HasValue || !_available() || MotionService.ReduceMotion
            || !IsOwnInput(args.OriginalSource as DependencyObject)) return;
        var point = args.GetCurrentPoint(_surface);
        if (!IsDown(point)) return;
        _pointer = point.PointerId;
        _motion.Press(point.Position.X, point.Position.Y, _surface.ActualWidth, _surface.ActualHeight, Pressure(point));
        // Only empty mouse-driven panel space captures here. Buttons, text selection and touch
        // scrolling retain their native capture; observing handled events doesn't consume them.
        _captured = !args.Handled && point.PointerDeviceType == PointerDeviceType.Mouse
            && CanCapture(args.OriginalSource as DependencyObject) && Input.CapturePointer(args.Pointer);
        TrackRoot();
        UpdateLight(point);
        Wake();
    }

    private void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_disposed || !_available() || MotionService.ReduceMotion) return;
        var point = args.GetCurrentPoint(_surface);
        if (_pointer is null)
        {
            if (!ReferenceEquals(sender, Input) || !IsOwnInput(args.OriginalSource as DependencyObject)) return;
            UpdateLight(point);
            Paint();
            return;
        }
        if (_pointer != point.PointerId) return;
        if (!IsDown(point)) { Release(); return; }
        _motion.Pull(point.Position.X, point.Position.Y, Pressure(point));
        UpdateLight(point);
        Wake();
    }

    private static double Pressure(PointerPoint point)
        => point.PointerDeviceType == PointerDeviceType.Pen ? Math.Clamp(point.Properties.Pressure, .15, 1) : 1;
    private static bool IsDown(PointerPoint point)
        => point.PointerDeviceType == PointerDeviceType.Mouse ? point.Properties.IsLeftButtonPressed : point.IsInContact;

    private void UpdateLight(PointerPoint point)
    {
        _lightX = Math.Clamp(point.Position.X / Math.Max(1, _surface.ActualWidth), 0, 1);
        _lightY = Math.Clamp(point.Position.Y / Math.Max(1, _surface.ActualHeight), 0, 1);
    }

    private void OnReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_pointer == args.Pointer.PointerId) Release();
    }

    private void OnExited(object sender, PointerRoutedEventArgs args)
    {
        if (_pointer.HasValue) return;
        _lightX = .22; _lightY = .05;
        Paint();
    }

    private void TrackRoot()
    {
        _trackingRoot = _surface.XamlRoot?.Content;
        if (_trackingRoot is null || ReferenceEquals(_trackingRoot, Input)) return;
        _trackingRoot.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        _trackingRoot.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        _trackingRoot.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnReleased), true);
        _trackingRoot.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnReleased), true);
    }

    private void UntrackRoot()
    {
        if (_trackingRoot is not null && !ReferenceEquals(_trackingRoot, Input))
        {
            _trackingRoot.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved));
            _trackingRoot.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased));
            _trackingRoot.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnReleased));
            _trackingRoot.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnReleased));
        }
        _trackingRoot = null;
    }

    private void Release()
    {
        _pointer = null;
        UntrackRoot();
        if (_captured) { _captured = false; Input.ReleasePointerCaptures(); }
        _motion.Release();
        Wake();
    }

    private void Wake()
    {
        if (_animating || _disposed) return;
        _animating = true;
        _lastTick = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, object args)
    {
        long now = Stopwatch.GetTimestamp();
        _motion.Advance(Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds);
        _lastTick = now;
        Paint();
        if (!_motion.Moving) Stop();
    }

    private void Paint() => _paint(_motion, _lightX, _lightY);
    private void Stop()
    {
        if (_animating) CompositionTarget.Rendering -= OnFrame;
        _animating = false;
    }

    public void Reset()
    {
        _pointer = null;
        UntrackRoot();
        if (_captured) { _captured = false; Input.ReleasePointerCaptures(); }
        Stop();
        _motion.Reset();
        _lightX = .22; _lightY = .05;
        Paint();
    }

    private void OnMotionChanged(object? sender, EventArgs args)
    {
        if (!MotionService.ReduceMotion) return;
        if (_surface.DispatcherQueue.HasThreadAccess) Reset(); else _surface.DispatcherQueue.TryEnqueue(Reset);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
        MotionService.Changed -= OnMotionChanged;
        if (Owners.TryGetValue(Input, out var owner) && ReferenceEquals(owner, this)) Owners.Remove(Input);
        Input.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed));
        Input.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved));
        Input.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased));
        Input.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnReleased));
        Input.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnReleased));
        Input.PointerExited -= OnExited;
    }
}
