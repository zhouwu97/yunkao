using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using YunKao.Core.Presentation;
using YunKao.Services;

namespace YunKao.Controls;

/// <summary>Tethers a native component to its resting location using the reference jelly solver.</summary>
internal sealed class LiquidGlassInteraction : IDisposable
{
    private static readonly ConditionalWeakTable<UIElement, LiquidGlassInteraction> Owners = new();
    private readonly FrameworkElement _geometry;
    private readonly Action<LiquidSurfaceMotion, double, double>? _paint;
    private readonly Func<bool> _available;
    private readonly LiquidSurfaceMotion _motion;
    private readonly MatrixTransform _pose = new();
    private readonly TransformGroup _transform = new();
    private readonly Transform? _originalTransform;
    private UIElement? _trackingRoot, _captureOwner;
    private ButtonBase? _suppressedRelease;
    private uint? _pointer;
    private bool _animating, _disposed, _transferring, _dragged;
    private long _lastTick;
    private Point _pressWorld;
    private double _lightX = .22, _lightY = .05;
    public UIElement Input { get; }

    public LiquidGlassInteraction(FrameworkElement geometry, UIElement input,
        Func<bool> available, Action<LiquidSurfaceMotion, double, double>? paint = null)
    {
        _geometry = geometry; Input = input; _available = available; _paint = paint;
        _motion = new LiquidSurfaceMotion(input is ButtonBase ? 4 : 12, dragGain: .18);
        if (Owners.TryGetValue(input, out var previous)) previous.Dispose();
        Owners.Add(input, this);
        _originalTransform = input.RenderTransform;
        if (_originalTransform is not null) _transform.Children.Add(_originalTransform);
        _transform.Children.Add(_pose);
        input.RenderTransform = _transform;
        input.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        input.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        input.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        input.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnReleased), true);
        input.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnCaptureLost), true);
        input.PointerExited += OnExited;
        MotionService.Changed += OnMotionChanged;
    }

    internal static bool Owns(UIElement input) => Owners.TryGetValue(input, out _);

    private bool IsOwnInput(DependencyObject? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, Input); node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement element && Owners.TryGetValue(element, out var owner) && !ReferenceEquals(owner, this)) return false;
            if (node is ButtonBase or ToggleSwitch or LiquidGlassToggleSwitch or TextBox or PasswordBox or ComboBox or Slider or ScrollBar
                or TextBlock { IsTextSelectionEnabled: true }) return false;
            if (node is SelectorItem item && HasSelection(item)) return false;
        }
        return true;
    }

    private static bool HasSelection(SelectorItem item)
    {
        for(var node=VisualTreeHelper.GetParent(item);node is not null;node=VisualTreeHelper.GetParent(node))
            if(node is ListViewBase list)return list.SelectionMode!=ListViewSelectionMode.None;
        return true;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs args) => Begin(args);

    internal void Begin(PointerRoutedEventArgs args)
    {
        if (_disposed || _pointer.HasValue || !_available() || MotionService.ReduceMotion
            || !IsOwnInput(args.OriginalSource as DependencyObject)) return;
        var root = _geometry.XamlRoot?.Content;
        if (root is null) return;
        var world = args.GetCurrentPoint(root);
        if (!IsDown(world)) return;
        _pointer = world.PointerId;
        _pressWorld = world.Position;
        _dragged = false;
        if (root is FrameworkElement viewport && Input is FrameworkElement host)
        {
            var rect = host.TransformToVisual(root).TransformBounds(new Rect(0, 0, host.ActualWidth, host.ActualHeight));
            _motion.SetBounds(22 - rect.Left + _motion.PositionX, viewport.ActualWidth - 22 - rect.Right + _motion.PositionX,
                22 - rect.Top + _motion.PositionY, viewport.ActualHeight - 22 - rect.Bottom + _motion.PositionY);
        }
        _motion.Press(world.Position.X, world.Position.Y, _geometry.ActualWidth, _geometry.ActualHeight, Pressure(world));
        TrackRoot(root);
        // Buttons keep native click/capture until a real drag is detected. Touch retains native pan ownership.
        // A nonselecting ListView can mark the down event handled before the panel
        // sees it. Child editing/selection owners were already excluded above.
        if (Input is not ButtonBase && world.PointerDeviceType == PointerDeviceType.Mouse
            && Input.CapturePointer(args.Pointer)) _captureOwner = Input;
        UpdateLight(args.GetCurrentPoint(_geometry));
        Wake();
    }

    private void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_disposed || !_available() || MotionService.ReduceMotion) return;
        if (_pointer is null)
        {
            if (!ReferenceEquals(sender, Input) || !IsOwnInput(args.OriginalSource as DependencyObject)) return;
            UpdateLight(args.GetCurrentPoint(_geometry)); Paint(); return;
        }
        var world = args.GetCurrentPoint(_trackingRoot);
        if (_pointer != world.PointerId) return;
        if (!IsDown(world)) { Release(); return; }
        bool moved = Math.Sqrt(Math.Pow(world.Position.X - _pressWorld.X, 2) + Math.Pow(world.Position.Y - _pressWorld.Y, 2)) > 5;
        if (moved && !_dragged && Input is ButtonBase && world.PointerDeviceType == PointerDeviceType.Mouse && _trackingRoot is { } root)
        {
            // Some native Button templates still invoke on release after capture is transferred.
            // Changing Release to Press only after the down event suppresses that release path.
            if (Input is ButtonBase { ClickMode: ClickMode.Release } button)
            {
                _suppressedRelease = button;
                button.ClickMode = ClickMode.Press;
            }
            // Transfer capture to cancel the native pressed state/command after a true drag.
            // A tap retains native Click, keyboard activation and automation semantics.
            _transferring = true;
            try { if (root.CapturePointer(args.Pointer)) _captureOwner = root; }
            finally { _transferring = false; }
        }
        _dragged |= moved;
        _motion.Pull(world.Position.X, world.Position.Y, Pressure(world));
        UpdateLight(args.GetCurrentPoint(_geometry));
        Wake();
    }

    private static double Pressure(PointerPoint point)
        => point.PointerDeviceType == PointerDeviceType.Pen ? Math.Clamp(point.Properties.Pressure, .15, 1) : 1;
    private static bool IsDown(PointerPoint point)
        => point.PointerDeviceType == PointerDeviceType.Mouse ? point.Properties.IsLeftButtonPressed : point.IsInContact;
    private void UpdateLight(PointerPoint point)
    {
        _lightX = Math.Clamp(point.Position.X / Math.Max(1, _geometry.ActualWidth), 0, 1);
        _lightY = Math.Clamp(point.Position.Y / Math.Max(1, _geometry.ActualHeight), 0, 1);
    }
    private void OnReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_transferring && _pointer == args.Pointer.PointerId) Release();
    }
    private void OnCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (_transferring || _pointer != args.Pointer.PointerId) return;
        // Capture-loss bubbles from descendants and the former native button owner.
        if (ReferenceEquals(args.OriginalSource, _captureOwner)) Release();
    }
    private void OnExited(object sender, PointerRoutedEventArgs args)
    {
        if (_pointer.HasValue) return;
        _lightX = .22; _lightY = .05; Paint();
    }
    private void TrackRoot(UIElement root)
    {
        _trackingRoot = root;
        if (ReferenceEquals(root, Input)) return;
        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        root.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnReleased), true);
        root.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnCaptureLost), true);
    }
    private void UntrackRoot()
    {
        if (_trackingRoot is { } root && !ReferenceEquals(root, Input))
        {
            root.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved));
            root.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased));
            root.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnReleased));
            root.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnCaptureLost));
        }
        _trackingRoot = null;
    }
    private void ReleaseCapture()
    {
        var owner = _captureOwner; _captureOwner = null;
        owner?.ReleasePointerCaptures();
    }
    private void Release()
    {
        _pointer = null; UntrackRoot(); ReleaseCapture(); _motion.Release(); RestoreClickMode(); Wake();
    }
    private void RestoreClickMode()
    {
        var button = _suppressedRelease; _suppressedRelease = null;
        if (button is null) return;
        // Restore after the current routed native release event has finished.
        button.DispatcherQueue.TryEnqueue(() => { if (button.ClickMode == ClickMode.Press) button.ClickMode = ClickMode.Release; });
    }
    private void Wake()
    {
        if (_animating || _disposed) return;
        _animating = true; _lastTick = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnFrame;
    }
    private void OnFrame(object? sender, object args)
    {
        long now = Stopwatch.GetTimestamp();
        _motion.Advance(Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds); _lastTick = now;
        Paint();
        if (!_motion.Moving) Stop();
    }
    internal void Paint()
    {
        if (Input is FrameworkElement host)
        {
            Point origin = Input.RenderTransformOrigin;
            var m = _motion.CreatePose(host.ActualWidth, host.ActualHeight, origin.X, origin.Y);
            _pose.Matrix = new Matrix { M11 = m.M11, M12 = m.M12, M21 = m.M21, M22 = m.M22, OffsetX = m.M31, OffsetY = m.M32 };
        }
        _paint?.Invoke(_motion, _lightX, _lightY);
    }
    private void Stop()
    {
        if (_animating) CompositionTarget.Rendering -= OnFrame;
        _animating = false;
    }
    public void Reset()
    {
        _pointer = null; UntrackRoot(); ReleaseCapture(); RestoreClickMode(); Stop(); _motion.Reset();
        _lightX = .22; _lightY = .05; Paint();
    }
    private void OnMotionChanged(object? sender, EventArgs args)
    {
        if (!MotionService.ReduceMotion) return;
        if (_geometry.DispatcherQueue.HasThreadAccess) Reset(); else _geometry.DispatcherQueue.TryEnqueue(Reset);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Reset();
        MotionService.Changed -= OnMotionChanged;
        if (Owners.TryGetValue(Input, out var owner) && ReferenceEquals(owner, this)) Owners.Remove(Input);
        Input.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed));
        Input.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved));
        Input.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased));
        Input.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnReleased));
        Input.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnCaptureLost));
        Input.PointerExited -= OnExited;
        if (ReferenceEquals(Input.RenderTransform, _transform)) Input.RenderTransform = _originalTransform;
    }
}
