using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using YunKao.Services;
using Windows.Foundation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Windows.System;
using YunKao.Core.Presentation;

namespace YunKao.Controls;

public enum NavigationTarget
{
    Workspace,
    History,
    Export,
    Diagnostics,
    Settings
}

public sealed class NavigationRequestEventArgs(NavigationTarget target) : EventArgs
{
    public NavigationTarget Target { get; } = target;
}

/// <summary>
/// 侧边导航。选中项的标记跟随当前页面，紧凑时保留短标签。
/// </summary>
public sealed partial class NavigationRail : UserControl
{
    private NavigationTarget _activeTarget = NavigationTarget.Workspace;
    private bool _isLoaded;
    private bool _compact;
    private LiquidGlassScene? _scene;
    private LiquidGlassScene? _labels;
    private LiquidGlassRenderer? _lens;
    private readonly LiquidRailMotion _motion = new();
    private long _lastTick;
    private uint? _pointer;
    private double _lastY, _speed, _grabX, _grabOffsetY, _lightX = .22, _lightY = .1, _compression;
    private ulong _lastSample;
    private bool _animating;
    private bool _keyboardSelection;

    public NavigationRail()
    {
        InitializeComponent();
        GestureSurface.AddHandler(PointerPressedEvent, new PointerEventHandler(OnGlassPressed), true);
        GestureSurface.AddHandler(PointerMovedEvent, new PointerEventHandler(OnGlassMoved), true);
        GestureSurface.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnGlassReleased), true);
        GestureSurface.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnGlassCancelled), true);
        GestureSurface.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnGlassCancelled), true);
        Loaded += (_, _) =>
        {
            _isLoaded = true;
            _labels = new LiquidGlassScene(NavigationItems);
            ApplyStyles();
            GlassSettings.Changed += OnGlassChanged;
            MotionService.Changed += OnMotionChanged;
            RefreshLens();
            MoveSelection(animate: false);
        };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            GlassSettings.Changed -= OnGlassChanged;
            MotionService.Changed -= OnMotionChanged;
            _pointer = null;
            GestureSurface.ReleasePointerCaptures();
            StopMotion();
            _lens?.Dispose();
            _lens = null;
            _labels?.Dispose();
            _labels = null;
        };
        SizeChanged += OnLayoutChanged;
        KeyDown += OnRailKeyDown;
    }

    public event EventHandler<NavigationRequestEventArgs>? NavigationRequested;

    internal void SetScene(LiquidGlassScene scene) => _scene = scene;

    private void OnGlassChanged(object? sender, EventArgs args)
    {
        if (_lens?.HasFailed == true) { _lens.Dispose(); _lens = null; }
        RefreshLens();
    }

    private void OnMotionChanged(object? sender, EventArgs args)
    {
        if (!MotionService.ReduceMotion) return;
        StopMotion();
        _motion.Snap((int)_activeTarget * Step);
        PaintMotion();
    }

    public void RefreshLens()
    {
        if (!_isLoaded || _scene is null) return;
        if (GlassSettings.IsSolid || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _lens?.Dispose();
            _lens = null;
            App.Services.Workspace.NavigationMaterial = "实体选中态";
            return;
        }
        try
        {
            _lens ??= new LiquidGlassRenderer(LensHost, _scene, 32, LensFallback, _labels,
                materialReady: ready => Canvas.SetZIndex(SelectionBlob, ready ? 1 : 0));
            // Preserve a clear center on the dark rail. Keep rim refraction, but remove
            // blur, tint and the pressed interior glint/shadow that reads as an oval wash.
            _lens.SetMaterial(new(new GlassCorners(_compact ? 25 : 22), 0,
                LiquidLensPreset.Refraction, LiquidLensPreset.Saturation,
                Windows.UI.Color.FromArgb(255, 255, 255, 255), 0, true, 24, LiquidLensPreset.Dispersion, InteriorEffects: 0));
            _lens.Invalidate();
        }
        catch (Exception ex)
        {
            App.Services.Workspace.NavigationMaterial = "基础材质";
            App.Services.Diagnostics.Warning("导航玻璃不可用：" + ex.Message);
        }
    }

    public void SetActive(NavigationTarget target)
    {
        if (_pointer.HasValue)
        {
            _pointer = null;
            GestureSurface.ReleasePointerCaptures();
        }
        bool changed = _activeTarget != target || _motion.Held;
        _activeTarget = target;
        ApplyStyles();
        if (changed) _labels?.Invalidate();
        if (_isLoaded) MoveSelection(changed && !_keyboardSelection);
    }

    /// <summary>紧凑模式在图标下显示页面短名。</summary>
    public void SetCompact(bool compact)
    {
        _compact = compact;
        ApplyStyles();
        SelectionBlob.Width = compact ? 54 : double.NaN;
        SelectionBlob.Height = compact ? 50 : 44;
        SelectionBlob.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        _labels?.Invalidate();
        RefreshLens();
        if (_isLoaded) DispatcherQueue.TryEnqueue(() => MoveSelection(animate: false));
    }

    private void OnLayoutChanged(object sender, SizeChangedEventArgs args)
    {
        if (_isLoaded) MoveSelection(animate: false);
    }

    private void ApplyStyles()
    {
        var normal = (Style)Application.Current.Resources["SidebarNavItemStyle"];
        var selected = (Style)Application.Current.Resources["SidebarNavItemSelectedStyle"];
        foreach (NavigationTarget target in Enum.GetValues<NavigationTarget>())
        {
            Button button = GetButton(target);
            Style style = target == _activeTarget ? selected : normal;
            if (!ReferenceEquals(button.Style, style)) button.Style = style;

            // 样式切换会重置本地值，紧凑尺寸在样式之后应用。
            button.Width = _compact ? 54 : double.NaN;
            button.Height = _compact ? 50 : 44;
            button.HorizontalAlignment = _compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = _compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            button.Padding = _compact ? new Thickness(0) : new Thickness(12, 0, 12, 0);
            if (button.Content is StackPanel content && content.Children[1] is TextBlock label)
            {
                content.Orientation = _compact ? Orientation.Vertical : Orientation.Horizontal;
                content.Spacing = _compact ? 3 : 12;
                label.FontSize = _compact ? 11 : 14;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.Text = (_compact, target) switch
                {
                    (true, NavigationTarget.History) => "历史",
                    (true, NavigationTarget.Export) => "导出",
                    _ => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button),
                };
            }
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(button, target == _activeTarget ? "当前页" : "");
        }
    }

    private void MoveSelection(bool animate)
    {
        Button button = GetButton(_activeTarget);
        if (button.ActualHeight <= 0) return;
        double translation = button.TransformToVisual(NavigationItems).TransformPoint(new Point(0, 0)).Y;

        if (_pointer.HasValue) return;
        if (!animate || MotionService.ReduceMotion)
        {
            StopMotion();
            _motion.Snap(translation);
            PaintMotion();
        }
        else Aim(translation);
    }

    private double Step => WorkspaceButton.ActualHeight + NavigationItems.Spacing;
    private double PositionAt(double y) => (y - WorkspaceButton.ActualHeight / 2) / Math.Max(1, Step);

    private void OnGlassPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(GestureSurface);
        if (_pointer.HasValue || !pt.Properties.IsLeftButtonPressed) return;
        // Button captures during its class handler, before this routed handler. Transfer it to the rail
        // so a drag can cross several buttons; release still commits through their native focus/automation.
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node is not null && node != GestureSurface;
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is not Button button) continue;
            button.ReleasePointerCapture(e.Pointer);
            break;
        }
        if (!GestureSurface.CapturePointer(e.Pointer)) return;
        _pointer = pt.PointerId;
        _grabX = pt.Position.X;
        double center = pt.Position.Y - WorkspaceButton.ActualHeight / 2;
        double grabbed = Math.Abs(center - _motion.Position) <= WorkspaceButton.ActualHeight / 2
            ? _motion.Position : Math.Round(PositionAt(pt.Position.Y)) * Step;
        _grabOffsetY = center - grabbed;
        _lastY = pt.Position.Y;
        _lastSample = pt.Timestamp;
        _speed = 0;
        AimAtPointer(pt);
        e.Handled = true;
    }

    private void OnGlassMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(GestureSurface);
        if (_pointer is null)
        {
            return;
        }
        if (pt.PointerId != _pointer) return;
        if (pt.Timestamp > _lastSample)
        {
            double dt = (pt.Timestamp - _lastSample) / 1_000_000d;
            _speed += (1 - Math.Exp(-dt * 30)) * ((pt.Position.Y - _lastY) / dt - _speed);
        }
        _lastSample = pt.Timestamp;
        _lastY = pt.Position.Y;
        AimAtPointer(pt);
        e.Handled = true;
    }

    private void AimAtPointer(PointerPoint pt)
    {
        double raw = PositionAt(pt.Position.Y - _grabOffsetY);
        double index = Math.Clamp(raw, 0, NavigationItems.Children.Count - 1);
        _compression = Math.Min(1, Math.Abs(raw - index) / .72);
        _lightX = Math.Clamp(pt.Position.X / Math.Max(1, GestureSurface.ActualWidth), 0, 1);
        _lightY = Math.Clamp(.5 + (pt.Position.Y - index * Step - WorkspaceButton.ActualHeight / 2) / WorkspaceButton.ActualHeight, 0, 1);
        double pressure = pt.PointerDeviceType == PointerDeviceType.Pen ? Math.Clamp(pt.Properties.Pressure, .15, 1) : 1;
        Aim(index * Step, true, pressure, (pt.Position.X - _grabX) * .12);
    }

    private void OnGlassReleased(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(GestureSurface);
        if (pt.PointerId != _pointer) return;
        _pointer = null;
        GestureSurface.ReleasePointerCapture(e.Pointer);
        _compression = 0;
        if (pt.Position.X < -8 || pt.Position.X > GestureSurface.ActualWidth + 8 ||
            pt.Position.Y < -32 || pt.Position.Y > GestureSurface.ActualHeight + 32)
        {
            Aim((int)_activeTarget * Step);
        }
        else
        {
            double speed = pt.Timestamp - _lastSample > 100_000 ? 0 : _speed;
            int index = LiquidRailMotion.ReleaseIndex(PositionAt(pt.Position.Y - _grabOffsetY), speed, Step, NavigationItems.Children.Count);
            Commit((NavigationTarget)index, FocusState.Pointer);
        }
        e.Handled = true;
    }

    private void OnGlassCancelled(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, GestureSurface)) return;
        if (_pointer != e.Pointer.PointerId) return;
        _pointer = null;
        _compression = 0;
        Aim((int)_activeTarget * Step);
    }

    private void OnRailKeyDown(object sender, KeyRoutedEventArgs e)
    {
        int current = e.OriginalSource is Button b && Enum.TryParse<NavigationTarget>(b.Tag?.ToString(), out var focused)
            ? (int)focused : (int)_activeTarget;
        int last = NavigationItems.Children.Count - 1;
        int index = e.Key switch
        {
            VirtualKey.Home => 0, VirtualKey.End => last,
            VirtualKey.Up => Math.Max(0, current - 1), VirtualKey.Down => Math.Min(last, current + 1),
            _ => -1,
        };
        if (index < 0) return;
        _keyboardSelection = true;
        try { Commit((NavigationTarget)index, FocusState.Keyboard); }
        finally { _keyboardSelection = false; }
        e.Handled = true;
    }

    private void Commit(NavigationTarget target, FocusState focus)
    {
        GetButton(target).Focus(focus);
        if (target != _activeTarget) NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(target));
        else MoveSelection(animate: focus != FocusState.Keyboard);
    }

    private void Aim(double y, bool held = false, double pressure = 0, double cross = 0)
    {
        if (MotionService.ReduceMotion)
        {
            StopMotion();
            _motion.Snap(y);
            PaintMotion();
            return;
        }
        _motion.Aim(y, held, pressure, cross);
        if (_animating) return;
        _animating = true;
        _lastTick = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnMotionFrame;
    }

    private void OnMotionFrame(object? sender, object args)
    {
        long now = Stopwatch.GetTimestamp();
        _motion.Advance(Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds);
        _lastTick = now;
        if (MotionService.ReduceMotion) _motion.Snap((int)_activeTarget * Step);
        PaintMotion();
        if (!_motion.Moving) StopMotion();
    }

    private void PaintMotion()
    {
        double press = _motion.Pressure;
        SelectionTransform.TranslateY = _motion.Position;
        SelectionTransform.TranslateX = _motion.Cross;
        SelectionTransform.ScaleX = SelectionTransform.ScaleY = 1;
        SelectionTransform.SkewX = 0;
        _lens?.SetInteraction(press, _lightX, _lightY);
        _lens?.SetDeformation(_motion.Stretch, _motion.Angle, _motion.CrossBend,
            _motion.Bend - press * _compression * 4, _motion.Energy);
        // Position changes need fresh sampling even after pressure and light have settled.
        _lens?.Invalidate();
    }

    private void StopMotion()
    {
        if (_animating) CompositionTarget.Rendering -= OnMotionFrame;
        _animating = false;
    }

    private Button GetButton(NavigationTarget target) => target switch
    {
        NavigationTarget.History => HistoryButton,
        NavigationTarget.Export => ExportButton,
        NavigationTarget.Diagnostics => DiagnosticsButton,
        NavigationTarget.Settings => SettingsButton,
        _ => WorkspaceButton,
    };

    private void OnItemClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse(tag, out NavigationTarget target))
        {
            NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(target));
        }
    }
}
