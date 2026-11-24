using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ComputeSharp;
using ComputeSharp.D2D1.WinUI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using YunKao.Controls;
using YunKao.Core.Presentation;

namespace LiquidGlassDemo;

// Prototype: does one deformed optical field remain convincing across text, a grid and artwork?
// Uses the production shader and spring model. All demo preferences remain in memory.
public sealed partial class MainWindow : Window
{
    private readonly Grid Root = new() { Padding = new Thickness(24), RowSpacing = 14 };
    private readonly CanvasControl Canvas = new() { ClearColor = Color.FromArgb(0, 0, 0, 0), ManipulationMode = ManipulationModes.None };
    private readonly TextBlock State = new() { FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox Frost = new() { Content = "磨砂" };
    private readonly CheckBox Compare = new() { Content = "仅模糊对照" };
    private readonly CheckBox Dark = new() { Content = "深色" };
    private readonly CheckBox Reduce = new() { Content = "减少动画" };
    private readonly LiquidRailMotion _motion = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _replay = new();
    private readonly List<object> _trace = [];
    private readonly HashSet<string> _captures = [];
    private CanvasRenderTarget? _scene, _ink, _frame;
    private CanvasBitmap? _art;
    private readonly GaussianBlurEffect _blur = new() { BorderMode = EffectBorderMode.Hard };
    private readonly PixelShaderEffect<LiquidGlassShader> _glass = new();
    private readonly PixelShaderEffect<LiquidGlassShader> _card = new();
    private (int Width, int Height, float Scale) _size;
    private uint? _pointer;
    private double _grabX, _target, _pointerX = 130, _pointerY = 60;
    private long _lastTick;
    private bool _dirty = true, _running, _saving, _capturePending;
    private readonly string? _captureDirectory;
    private LiquidGlassScene? _nativeScene;

    public MainWindow()
    {
        BuildUI();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 820));
        _timer.Tick += OnTick;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _nativeScene?.Dispose();
            _scene?.Dispose(); _ink?.Dispose(); _frame?.Dispose(); _art?.Dispose();
            _glass.Dispose(); _card.Dispose(); _blur.Dispose();
            Canvas.RemoveFromVisualTree();
        };
        const string capture = "--capture-demo=";
        string? argument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(capture, StringComparison.Ordinal));
        if (argument is not null) _captureDirectory = Path.GetFullPath(argument[capture.Length..]);
        // Explorer activation isolates WinUI resource lookup from a packaged caller. A one-shot
        // request also lets the capture script use that activation path without shell arguments.
        string request = Path.Combine(AppContext.BaseDirectory, "capture-request.txt");
        if (File.Exists(request))
        {
            _captureDirectory ??= Path.GetFullPath(File.ReadAllText(request).Trim());
            File.Delete(request);
        }
    }

    private void BuildUI()
    {
        Title = "YunKao · Liquid Glass Lab";
        Root.Background = new SolidColorBrush(Rgb(248, 249, 252));
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            Root.RowDefinitions.Add(new RowDefinition { Height = height });
        var header = new StackPanel { Spacing = 5 };
        header.Children.Add(new TextBlock { Text = "液态玻璃 · 独立实验", FontSize = 27 });
        header.Children.Add(new TextBlock { Text = "左侧透镜演示移动折射；右侧应用卡片可以原地拉扯、松手回弹，内部按钮和输入框保持正常。", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        Root.Children.Add(header);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        var replay = new Button { Content = "自动演示" };
        replay.Click += OnReplay;
        toolbar.Children.Add(replay);
        foreach (var check in new[] { Frost, Compare, Dark, Reduce })
        {
            toolbar.Children.Add(check);
            check.Checked += OnMaterialChanged;
            check.Unchecked += OnMaterialChanged;
        }
        Grid.SetRow(toolbar, 1); Root.Children.Add(toolbar);
        Canvas.CreateResources += OnCreateResources;
        Canvas.Draw += OnDraw;
        Canvas.SizeChanged += OnSizeChanged;
        Canvas.PointerPressed += OnPressed;
        Canvas.PointerMoved += OnMoved;
        Canvas.PointerReleased += OnReleased;
        Canvas.PointerCanceled += OnCancelled;
        Canvas.PointerCaptureLost += OnCancelled;
        var experiments = new Grid { ColumnSpacing = 20 };
        experiments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        experiments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        experiments.Children.Add(Canvas);
        var native = BuildNativeExample();
        Grid.SetColumn(native, 1); experiments.Children.Add(native);
        Grid.SetRow(experiments, 2); Root.Children.Add(experiments);
        Grid.SetRow(State, 3); Root.Children.Add(State);
        Root.Loaded += OnLoaded;
        Content = Root;
    }

    private Grid BuildNativeExample()
    {
        var host = new Grid { RequestedTheme = ElementTheme.Dark };
        var backdrop = new Grid
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Rgb(32, 48, 76), Offset = 0 },
                    new GradientStop { Color = Rgb(112, 76, 126), Offset = 1 },
                },
            },
            IsHitTestVisible = false,
        };
        var grid = new Microsoft.UI.Xaml.Controls.Canvas();
        for (int x = 0; x < 360; x += 22)
            grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Line { X1 = x, X2 = x, Y1 = 0, Y2 = 900, StrokeThickness = 1,
                Stroke = new SolidColorBrush(Color.FromArgb(55, 225, 234, 255)) });
        for (int y = 0; y < 900; y += 22)
            grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Line { X1 = 0, X2 = 350, Y1 = y, Y2 = y, StrokeThickness = 1,
                Stroke = new SolidColorBrush(Color.FromArgb(55, 225, 234, 255)) });
        backdrop.Children.Add(grid);
        var artwork = new Microsoft.UI.Xaml.Controls.Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Luna.png")),
            Width = 260, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right,
        };
        backdrop.Children.Add(artwork);
        host.Children.Add(backdrop);
        _nativeScene = new LiquidGlassScene(backdrop);
        artwork.ImageOpened += (_, _) => _nativeScene?.Invalidate();
        LiquidGlassScope.SetScene(host, _nativeScene);
        host.Children.Add(new TextBlock { Text = "固定位置 · 真实应用组件", FontSize = 19, Margin = new Thickness(22),
            VerticalAlignment = VerticalAlignment.Top, Foreground = new SolidColorBrush(Rgb(255, 255, 255)) });
        var content = new StackPanel { Spacing = 14 };
        var heading = new TextBlock { Text = "按住空白处拉扯", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Rgb(255, 255, 255)) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(heading, "AnchoredHeading");
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = "只有抓取点附近的边缘形变。\n主体、远侧边缘和文字位置不变。", TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Rgb(239, 239, 248)) });
        var test = new Button { Content = "测试按钮 · 0", HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(test, "AnchoredButton");
        int clicks = 0;
        test.Click += (_, _) => test.Content = $"测试按钮 · {++clicks}";
        content.Children.Add(test);
        var entry = new TextBox { PlaceholderText = "输入与选择文字保持正常" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(entry, "AnchoredEntry");
        content.Children.Add(entry);
        var panel = new GlassPanel { Content = content, Margin = new Thickness(24, 0, 24, 0), Padding = new Thickness(20),
            VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(panel, "AnchoredPanel");
        host.Children.Add(panel);
        return host;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_captureDirectory is not null)
        {
            Directory.CreateDirectory(_captureDirectory);
            _capturePending = true;
            Canvas.Invalidate();
        }
    }

    private async void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _scene?.Dispose(); _ink?.Dispose(); _frame?.Dispose(); _art?.Dispose();
        _scene = _ink = _frame = null;
        _size = default;
        _dirty = true;
        try
        {
            _art = await CanvasBitmap.LoadAsync(sender, Path.Combine(AppContext.BaseDirectory, "Assets", "Luna.png"));
            _dirty = true;
            sender.Invalidate();
        }
        catch (Exception ex) { State.Text = ex.Message; }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) { _dirty = true; Canvas.Invalidate(); }
    private void OnMaterialChanged(object sender, RoutedEventArgs args)
    {
        if (Canvas is null) return;
        Root.RequestedTheme = Dark.IsChecked == true ? ElementTheme.Dark : ElementTheme.Light;
        Root.Background = new SolidColorBrush(Dark.IsChecked == true ? Rgb(24, 26, 34) : Rgb(248, 249, 252));
        _dirty = true;
        if (Reduce.IsChecked == true) _motion.Snap(_target);
        YunKao.Services.MotionService.SetReduceMotion(Reduce.IsChecked == true);
        Canvas.Invalidate();
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    private void EnsureScene(CanvasControl canvas, float w, float h, float scale)
    {
        int width = Math.Max(1, (int)Math.Ceiling(w * scale)), height = Math.Max(1, (int)Math.Ceiling(h * scale));
        if (_size != (width, height, scale))
        {
            _scene?.Dispose(); _ink?.Dispose(); _frame?.Dispose();
            _scene = new CanvasRenderTarget(canvas, width, height, 96);
            _ink = new CanvasRenderTarget(canvas, width, height, 96);
            _frame = new CanvasRenderTarget(canvas, width, height, 96);
            _size = (width, height, scale);
            _dirty = true;
        }
        if (!_dirty) return;
        bool dark = Dark.IsChecked == true;
        using (var ds = _scene!.CreateDrawingSession())
        {
            ds.Transform = Matrix3x2.CreateScale(scale);
            using var background = new CanvasLinearGradientBrush(canvas,
                dark ? Rgb(28, 37, 60) : Rgb(213, 237, 242), dark ? Rgb(74, 47, 78) : Rgb(239, 215, 230))
            { StartPoint = Vector2.Zero, EndPoint = new(w, h) };
            ds.FillRectangle(0, 0, w, h, background);
            ds.FillEllipse(w * .25f, h * .4f, w * .42f, 75, dark ? Rgb(37, 93, 115) : Rgb(123, 195, 219));
            ds.FillEllipse(w * .75f, h * .8f, w * .6f, 60, dark ? Rgb(104, 69, 111) : Rgb(207, 173, 205));
            var line = dark ? Color.FromArgb(68, 205, 222, 245) : Color.FromArgb(68, 43, 70, 103);
            for (int x = 0; x < w; x += 22) ds.DrawLine(x, 0, x, h, line, 1);
            for (int y = 0; y < h; y += 22) ds.DrawLine(0, y, w, y, line, 1);
            Color text = dark ? Rgb(231, 237, 247) : Rgb(30, 51, 74);
            ds.DrawText("REFRACTION / 折射", 32, 26, text);
            ds.DrawText("0123456789   ABCDEFG   真实纹理", 32, h * .47f, text);
            ds.DrawText("同一曲面：轮廓 · 法线 · 折射 · 方向光", 32, h - 58, text);
            if (_art is not null)
            {
                float artH = Math.Min(h - 20, 400), artW = (float)(_art.Size.Width / _art.Size.Height * artH);
                ds.DrawImage(_art, new Windows.Foundation.Rect(w - artW - 24, h - artH, artW, artH));
            }
        }
        using (var ds = _ink!.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(0, 0, 0, 0));
            ds.Transform = Matrix3x2.CreateScale(scale);
            ds.DrawText("液态玻璃  /  GPU", 68, h * .55f - 10, dark ? Rgb(223, 234, 255) : Rgb(28, 72, 142));
            ds.DrawText("GLASS", w - 200, h - 100, dark ? Rgb(223, 234, 255) : Rgb(28, 72, 142));
        }
        _blur.Source = _scene;
        _glass.Sources[0] = _blur; _glass.Sources[1] = _ink;
        _card.Sources[0] = _blur; _card.Sources[1] = _ink;
        _dirty = false;
    }

    private async void OnDraw(CanvasControl canvas, CanvasDrawEventArgs args)
    {
        float w = (float)canvas.ActualWidth, h = (float)canvas.ActualHeight;
        if (w < 100 || h < 100) return;
        try
        {
            float scale = LiquidGlassOptics.TextureScale(w, h, canvas.Dpi);
            EnsureScene(canvas, w, h, scale);
            bool reduced = Reduce.IsChecked == true;
            float press = reduced ? 0 : (float)_motion.Pressure;
            float stretch = reduced ? 0 : (float)_motion.Stretch;
            float angle = (float)(Math.PI / 2 - _motion.Angle);
            float along = 1 + stretch, across = 1 / along, c = MathF.Cos(angle), s = MathF.Sin(angle);
            var inverse = new Float4(across * c * c + along * s * s, (across - along) * c * s,
                (across - along) * c * s, across * s * s + along * c * c);
            bool dark = Dark.IsChecked == true;
            float frost = Frost.IsChecked == true ? 6 : .6f;
            _blur.BlurAmount = frost * scale * (1 - .8f * Math.Min(press, 1));
            var tint = dark ? new Float4(.08f, .09f, .14f, .04f) : new Float4(1, 1, 1, .03f);
            var center = new Float2(135 + (float)_motion.Position, h * .55f + (float)_motion.Cross);
            _glass.ConstantBuffer = new(new Float2(190 * (1 + .025f * press), 76 * (1 + .39f * press)), center,
                new Float4(60, 60, 60, 60), new Float2((float)_pointerX, (float)_pointerY), inverse,
                new Float2((float)_motion.Bend, (float)_motion.CrossBend), tint, press, 14, .5f,
                (float)_motion.Energy, scale, .85f, 1, Compare.IsChecked == true ? 1 : 0);
            _card.ConstantBuffer = new(new Float2(168, 54), new Float2(w - 140, h - 91),
                new Float4(16, 16, 16, 16), new Float2((float)_pointerX, (float)_pointerY),
                new Float4(1, 0, 0, 1), new Float2(0, 0), tint, 0, 18, .3f, 0, scale, .65f, 1,
                Compare.IsChecked == true ? 1 : 0);
            using (var ds = _frame!.CreateDrawingSession())
            {
                ds.DrawImage(_scene);
                ds.DrawImage(_card, Vector2.Zero, new Windows.Foundation.Rect(0, 0, w * scale, h * scale));
                ds.DrawImage(_glass, Vector2.Zero, new Windows.Foundation.Rect(0, 0, w * scale, h * scale));
            }
            args.DrawingSession.DrawImage(_frame, new Windows.Foundation.Rect(0, 0, w, h),
                new Windows.Foundation.Rect(0, 0, w * scale, h * scale));
            State.Text = $"GPU / Direct2D   x {_motion.Position,6:F1}   press {press:F2}   stretch {stretch:F3}   bend {_motion.Bend,6:F2}   {(_timer.IsEnabled ? "active" : "idle")}";
            if (_captureDirectory is not null && !_saving)
            {
                if (_capturePending && _art is not null)
                {
                    // Warm the native effect graph and load the artwork before starting the clock.
                    // Otherwise a cold first frame can skip the short resting phase entirely.
                    _capturePending = false;
                    _saving = true;
                    await _frame.SaveAsync(Path.Combine(_captureDirectory, "01-rest.png"), CanvasBitmapFileFormat.Png);
                    _saving = false;
                    StartReplay();
                    _captures.Add("01-rest");
                    return;
                }
                string? phase = Phase(_replay.Elapsed.TotalSeconds);
                if (phase is not null && _captures.Add(phase))
                {
                    _saving = true;
                    await _frame.SaveAsync(Path.Combine(_captureDirectory, phase + ".png"), CanvasBitmapFileFormat.Png);
                    _saving = false;
                    if (phase == "07-settled")
                    {
                        _saving = true;
                        await NativeCapture.RunAsync(Root, _captureDirectory);
                        await File.WriteAllTextAsync(Path.Combine(_captureDirectory, "motion.json"), JsonSerializer.Serialize(_trace));
                        Close();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            State.Text = $"渲染失败 0x{ex.HResult:X8}: {ex.Message}";
            _timer.Stop();
            if (_captureDirectory is not null) File.WriteAllText(Path.Combine(_captureDirectory, "capture-error.log"), ex.ToString());
        }
    }

    private static string? Phase(double seconds) => seconds switch
    {
        < .08 => null, < .3 => "01-rest", < .65 => null, < .8 => "02-pressed",
        < 1.5 => null, < 1.8 => "03-slow", < 1.99 => null, < 2.1 => "04-fast",
        < 2.3 => null, < 2.5 => "05-reversal", < 2.7 => null, < 3.1 => "06-release",
        < 3.8 => null, _ => "07-settled",
    };

    private void Wake()
    {
        if (_timer.IsEnabled) return;
        _lastTick = Stopwatch.GetTimestamp();
        _timer.Start();
    }
    private void OnReplay(object sender, RoutedEventArgs args) => StartReplay();
    private void StartReplay()
    {
        _pointer = null;
        Canvas.ReleasePointerCaptures();
        _motion.Snap(0);
        _trace.Clear(); _captures.Clear();
        _replay.Restart(); _running = true;
        Wake();
    }
    private void OnTick(object? sender, object args)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds;
        _lastTick = now;
        if (_running)
        {
            double t = _replay.Elapsed.TotalSeconds, range = Math.Max(1, Canvas.ActualWidth - 300);
            double target = t switch
            {
                < .8 => 0, < 1.8 => range * .25 * (t - .8),
                < 2.1 => range * (.25 + .6 * (t - 1.8) / .3),
                < 2.5 => range * (.85 - .7 * (t - 2.1) / .4),
                _ => range * .15,
            };
            _target = target;
            _motion.Aim(target, t is >= .3 and < 2.5, t is >= .3 and < 2.5 ? 1 : 0, 0);
            _pointerX = target + 135; _pointerY = Canvas.ActualHeight * .55;
            if (t > 4 && !_motion.Moving) _running = false;
            _trace.Add(new { t, x = _motion.Position, pressure = _motion.Pressure, stretch = _motion.Stretch, bend = _motion.Bend });
        }
        if (Reduce.IsChecked == true) _motion.Snap(_target); else _motion.Advance(dt);
        Canvas.Invalidate();
        if (!_running && !_motion.Moving) _timer.Stop();
    }

    private void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        var p = args.GetCurrentPoint(Canvas);
        if (_pointer.HasValue || !p.Properties.IsLeftButtonPressed || !Canvas.CapturePointer(args.Pointer)) return;
        _running = false; _replay.Stop();
        _pointer = p.PointerId;
        _grabX = p.Position.X - 135 - _motion.Position;
        Aim(p.Position.X, p.Position.Y, true);
        args.Handled = true;
    }
    private void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        var p = args.GetCurrentPoint(Canvas);
        _pointerX = p.Position.X; _pointerY = p.Position.Y;
        if (_pointer != p.PointerId) { Canvas.Invalidate(); return; }
        Aim(p.Position.X, p.Position.Y, true);
    }
    private void Aim(double x, double y, bool held)
    {
        _pointerX = x; _pointerY = y;
        _target = Math.Clamp(x - 135 - _grabX, 0, Math.Max(1, Canvas.ActualWidth - 300));
        _motion.Aim(_target, held, held ? 1 : 0, held ? (y - Canvas.ActualHeight * .55) * .15 : 0);
        Wake();
    }
    private void OnReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_pointer != args.Pointer.PointerId) return;
        _pointer = null; Canvas.ReleasePointerCapture(args.Pointer);
        _motion.Aim(_target); Wake();
    }
    private void OnCancelled(object sender, PointerRoutedEventArgs args)
    {
        if (_pointer != args.Pointer.PointerId) return;
        _pointer = null; _motion.Aim(_target); Wake();
    }
}
