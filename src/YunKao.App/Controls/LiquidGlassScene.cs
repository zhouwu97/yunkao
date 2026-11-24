using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using YunKao.Services;

namespace YunKao.Controls;

/// <summary>Shares one feedback-free capture of a decorative layer among its glass surfaces.</summary>
internal sealed class LiquidGlassScene : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(65) };
    private int _revision;
    private bool _capturing, _disposed;
    private XamlRoot? _root;
    public FrameworkElement Element { get; }
    public byte[]? Pixels { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public double PixelsPerDip { get; private set; } = 1;
    public int Version { get; private set; }
    public bool IsDisposed => _disposed;
    public event Action? Changed;
    public event Action? Disposed;

    public LiquidGlassScene(FrameworkElement element)
    {
        Element = element;
        element.SizeChanged += OnSizeChanged;
        element.ActualThemeChanged += OnThemeChanged;
        element.Loaded += OnLoaded;
        element.Unloaded += OnUnloaded;
        _timer.Tick += Capture;
        GlassSettings.Changed += OnMaterialChanged;
        if (element.IsLoaded) OnLoaded(element, new RoutedEventArgs());
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Invalidate();
    private void OnThemeChanged(FrameworkElement sender, object args) => Invalidate();
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_root is not null) _root.Changed -= OnRootChanged;
        _root = Element.XamlRoot;
        if (_root is not null) _root.Changed += OnRootChanged;
        Invalidate();
    }
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _revision++;
        _timer.Stop();
        if (_root is not null) _root.Changed -= OnRootChanged;
        _root = null;
    }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Invalidate();
    private void OnMaterialChanged(object? sender, EventArgs args)
    {
        if (Element.DispatcherQueue.HasThreadAccess) Invalidate();
        else Element.DispatcherQueue.TryEnqueue(Invalidate);
    }

    public void Invalidate()
    {
        if (_disposed) return;
        _revision++;
        _timer.Stop();
        if (Element.IsLoaded && Changed is not null && !GlassSettings.IsSolid) _timer.Start();
    }

    private async void Capture(object? sender, object args)
    {
        _timer.Stop();
        if (_capturing || _disposed || Changed is null || GlassSettings.IsSolid || !Element.IsLoaded
            || Element.XamlRoot is null || Element.ActualWidth < 1 || Element.ActualHeight < 1) return;
        _capturing = true;
        int revision = _revision;
        try
        {
            var image = new RenderTargetBitmap();
            double pixelsPerDip = Element.XamlRoot.RasterizationScale;
            await image.RenderAsync(Element);
            var buffer = await image.GetPixelsAsync();
            if (_disposed || revision != _revision) return;
            Width = image.PixelWidth;
            Height = image.PixelHeight;
            PixelsPerDip = pixelsPerDip;
            Pixels = buffer.ToArray();
            Version++;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            if (!_disposed) App.Services.Diagnostics.Warning("玻璃背景取样失败：" + ex.Message);
        }
        finally
        {
            _capturing = false;
            if (!_disposed && revision != _revision && Changed is not null && Element.IsLoaded && !GlassSettings.IsSolid) _timer.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Capture;
        Element.SizeChanged -= OnSizeChanged;
        Element.ActualThemeChanged -= OnThemeChanged;
        Element.Loaded -= OnLoaded;
        Element.Unloaded -= OnUnloaded;
        GlassSettings.Changed -= OnMaterialChanged;
        if (_root is not null) _root.Changed -= OnRootChanged;
        _root = null;
        Pixels = null;
        Changed = null;
        Disposed?.Invoke();
        Disposed = null;
    }
}
