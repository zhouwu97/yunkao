using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using ToggleState = Microsoft.UI.Xaml.Automation.ToggleState;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using YunKao.Core.Presentation;
using YunKao.Services;

namespace YunKao.Controls;

/// <summary>Accessible toggle semantics with a glass thumb only during pointer interaction.</summary>
public sealed partial class LiquidGlassToggleSwitch : Control
{
    public static readonly DependencyProperty IsOnProperty=DependencyProperty.Register(nameof(IsOn),typeof(bool),typeof(LiquidGlassToggleSwitch),new PropertyMetadata(false,OnIsOnChanged));
    public bool IsOn { get=>(bool)GetValue(IsOnProperty); set=>SetValue(IsOnProperty,value); }
    public event RoutedEventHandler? Toggled;
    // The isolated preview renders the same control at native resolution.
    internal double PreviewScale { get; set; } = 1;
    private static void OnIsOnChanged(DependencyObject sender,DependencyPropertyChangedEventArgs args)
    {
        var toggle=(LiquidGlassToggleSwitch)sender;
        toggle._motion.SetState((bool)args.NewValue);toggle.Wake();
        if(FrameworkElementAutomationPeer.FromElement(toggle) is TogglePeer peer)
            peer.RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty,
                (bool)args.OldValue?ToggleState.On:ToggleState.Off,(bool)args.NewValue?ToggleState.On:ToggleState.Off);
        toggle.Toggled?.Invoke(toggle,new RoutedEventArgs());
    }
    private readonly LiquidToggleMotion _motion = new();
    private Grid? _track, _thumb;
    private Border? _fill, _solid;
    private Grid? _glass;
    private readonly CompositeTransform _pose = new();
    private LiquidGlassRenderer? _renderer;
    private LiquidGlassScene? _scene;
    private uint? _pointer;
    private long _lastTick;
    private bool _animating, _keyHeld, _subscribed, _opticalReady;

    public LiquidGlassToggleSwitch()
    {
        DefaultStyleKey = typeof(LiquidGlassToggleSwitch);
        IsTabStop=true;
        IsEnabledChanged += (_,_)=>{if(!IsEnabled)Cancel();Paint();};
        Loaded += (_, _) =>
        {
            if (!_subscribed) { MotionService.Changed += OnMotionChanged; GlassSettings.Changed += OnGlassChanged; _subscribed=true; }
            _motion.SetState(IsOn, true); EnsureRenderer(); Paint();
        };
        Unloaded += (_, _) =>
        {
            Cancel(); Stop(); DisposeRenderer();
            MotionService.Changed -= OnMotionChanged; GlassSettings.Changed -= OnGlassChanged; _subscribed=false;
        };
        ActualThemeChanged += (_, _) => Paint();
    }

    protected override void OnApplyTemplate()
    {
        DisposeRenderer(); base.OnApplyTemplate();
        _track=GetTemplateChild("PART_Track") as Grid; _thumb=GetTemplateChild("PART_Thumb") as Grid;
        _fill=GetTemplateChild("PART_Fill") as Border; _solid=GetTemplateChild("PART_Solid") as Border;
        _glass=GetTemplateChild("PART_Glass") as Grid;
        double scale = PreviewScale;
        MinWidth = 56 * scale;
        MinHeight = 44 * scale;
        if (_track is not null) { _track.Width = 56 * scale; _track.Height = 44 * scale; }
        if (_thumb is not null)
        {
            _thumb.Width = 30 * scale; _thumb.Height = 24 * scale;
            _thumb.Margin = new Thickness(3 * scale, 0, 0, 0); _thumb.RenderTransform = _pose;
        }
        if (_fill is not null) { _fill.Height = 30 * scale; _fill.CornerRadius = new CornerRadius(15 * scale); }
        if (_solid is not null) _solid.CornerRadius = new CornerRadius(12 * scale);
        if (_glass is not null) _glass.Margin = new Thickness(-10 * scale);
        _motion.SetState(IsOn,true); Paint();
    }
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (!IsEnabled || _pointer.HasValue || _track is null) return;
        var p=e.GetCurrentPoint(_track);
        if (p.PointerDeviceType==Microsoft.UI.Input.PointerDeviceType.Mouse && !p.Properties.IsLeftButtonPressed) return;
        if (!CapturePointer(e.Pointer)) return;
        Focus(FocusState.Pointer); _pointer=p.PointerId; _motion.Press(p.Position.X / PreviewScale);
        EnsureRenderer(); Wake(); e.Handled=true;
    }
    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (_pointer!=e.Pointer.PointerId || _track is null) return;
        _motion.Pull(e.GetCurrentPoint(_track).Position.X / PreviewScale); Wake(); e.Handled=true;
    }
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (_pointer!=e.Pointer.PointerId) return;
        _pointer=null; bool on=_motion.Release(); ReleasePointerCapture(e.Pointer);
        IsOn=on; Wake(); e.Handled=true;
    }
    protected override void OnPointerCanceled(PointerRoutedEventArgs e) { Cancel(); e.Handled=true; }
    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e) { if(_pointer==e.Pointer.PointerId) Cancel(); }
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Space or VirtualKey.Left or VirtualKey.Right or VirtualKey.Home or VirtualKey.End)
        {
            if (!_keyHeld) { Cancel(); IsOn=e.Key is VirtualKey.Right or VirtualKey.End || e.Key==VirtualKey.Space&&!IsOn; _keyHeld=true; }
            e.Handled=true; return;
        }
        if (e.Key==VirtualKey.Escape && _pointer.HasValue) {Cancel();e.Handled=true;return;}
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyRoutedEventArgs e) { _keyHeld=false; base.OnKeyUp(e); }
    private void Cancel()
    {
        _pointer=null; _motion.Release(true); ReleasePointerCaptures(); Wake();
    }
    private void EnsureRenderer()
    {
        if (_renderer is not null || _glass is null || _track is null || GlassSettings.IsSolid || MotionService.ReduceMotion) return;
        var scene=LiquidGlassScope.Find(this);
        if (scene is null) return;
        try
        {
            _scene=scene;
            _renderer=new LiquidGlassRenderer(_glass,scene,12,materialReady: ready=>{_opticalReady=ready;Paint();},
                switchTrack: _track, opticalScale: (float)PreviewScale);
            _renderer.SetMaterial(new(new GlassCorners((float)(12 * PreviewScale)),0,(float)(8 * PreviewScale),1,
                Color.FromArgb(255,255,255,255),.012f,true,(float)(10 * PreviewScale),.5f,0));
        }
        catch { DisposeRenderer(); }
    }
    private void Paint()
    {
        if (_track is null || _thumb is null || _fill is null || _solid is null || _glass is null) return;
        double progress=_motion.TrackProgress, pressure=MotionService.ReduceMotion?0:Math.Min(1,_motion.Pressure);
        bool dark=ActualTheme==ElementTheme.Dark;
        Color off=dark?Color.FromArgb(255,91,88,106):Color.FromArgb(255,207,203,211);
        Color on=Color.FromArgb(255,42,211,77);
        byte Blend(byte a,byte b)=>(byte)Math.Round(a+(b-a)*progress);
        _fill.Background=new SolidColorBrush(Color.FromArgb(255,Blend(off.R,on.R),Blend(off.G,on.G),Blend(off.B,on.B)));
        double sx=MotionService.ReduceMotion?1:_motion.ScaleX, sy=MotionService.ReduceMotion?1:_motion.ScaleY;
        _pose.TranslateX=_motion.Position * PreviewScale; _pose.ScaleX=sx; _pose.ScaleY=sy;
        bool optical=_opticalReady && _renderer is {HasFailed:false} && !GlassSettings.IsSolid;
        _glass.Opacity=optical?pressure:.001; _solid.Opacity=1-(optical?pressure:0);
        _renderer?.SetInteraction(pressure,.3,.1);
        double lift=Math.Sqrt(sx*sy), stretch=Math.Sqrt(sx/sy)-1;
        _renderer?.SetTrackColor(((SolidColorBrush)_fill.Background).Color);
        _renderer?.SetJellyPose(stretch,0,lift,0,0,Math.Abs(sx-sy));
        Opacity=IsEnabled?1:.4;
    }
    internal async Task ReplayAsync()
    {
        if (_pointer.HasValue) return;
        bool start=IsOn;
        _motion.Press(0); EnsureRenderer(); Wake(); await Task.Delay(360);
        for (int i=1; i<=25; i++)
        {
            _motion.Pull((start?-1:1)*LiquidToggleMotion.Travel*i/25); Wake(); await Task.Delay(16);
        }
        await Task.Delay(350); IsOn=_motion.Release(); Wake();
    }
    private void Wake()
    {
        if (!IsLoaded) return;
        if (MotionService.ReduceMotion) { _motion.SetState(IsOn,true); Paint(); return; }
        if (_animating) return;
        _lastTick=Stopwatch.GetTimestamp(); _animating=true; CompositionTarget.Rendering+=OnFrame;
    }
    private void OnFrame(object? sender,object args)
    {
        long now=Stopwatch.GetTimestamp(); _motion.Advance(Stopwatch.GetElapsedTime(_lastTick,now).TotalSeconds);_lastTick=now;
        Paint(); if(!_motion.Moving)Stop();
    }
    private void Stop() {if(_animating)CompositionTarget.Rendering-=OnFrame;_animating=false;}
    private void OnMotionChanged(object? s,EventArgs e) => DispatcherQueue.TryEnqueue(()=>{if(MotionService.ReduceMotion){Cancel();_motion.SetState(IsOn,true);Stop();Paint();}});
    private void OnGlassChanged(object? s,EventArgs e) => DispatcherQueue.TryEnqueue(()=>{DisposeRenderer();if(_motion.Held)EnsureRenderer();Paint();});
    private void DisposeRenderer() {_renderer?.Dispose();_renderer=null;_scene=null;_opticalReady=false;}
    protected override AutomationPeer OnCreateAutomationPeer()=>new TogglePeer(this);
    private sealed class TogglePeer(LiquidGlassToggleSwitch owner):FrameworkElementAutomationPeer(owner),IToggleProvider
    {
        protected override string GetClassNameCore()=>"ToggleSwitch";
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Button;
        protected override object GetPatternCore(PatternInterface pattern)=>pattern==PatternInterface.Toggle?this:base.GetPatternCore(pattern);
        public ToggleState ToggleState=>owner.IsOn?ToggleState.On:ToggleState.Off;
        public void Toggle() {if(!owner.IsEnabled)throw new ElementNotEnabledException();owner.IsOn=!owner.IsOn;}
    }
}

