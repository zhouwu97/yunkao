using System.Numerics;
using System.Runtime.CompilerServices;
using ComputeSharp;
using ComputeSharp.D2D1.Interop;
using ComputeSharp.D2D1.WinUI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using YunKao.Core.Presentation;

namespace YunKao.Controls;

internal readonly record struct LiquidGlassMaterial(
    GlassCorners Corners, float Blur, float Refraction, float Saturation, Color Tint, float TintOpacity,
    bool ShowSheen, float Padding = 0, float Dispersion = .35f, float InteriorEffects = 1);

/// <summary>GPU surface optics, ported from FlutterLiquidglass. No CPU normal maps or geometry-only deformation.</summary>
internal sealed class LiquidGlassRenderer : IDisposable
{
    private static readonly ConditionalWeakTable<LiquidGlassScene, SceneBitmaps> Bitmaps = new();
    private readonly Grid _host;
    private readonly LiquidGlassScene _scene;
    private readonly LiquidGlassScene? _labels;
    private readonly Border? _fallback;
    private readonly Action<bool>? _materialReady;
    private readonly FrameworkElement? _switchTrack;
    private readonly float _opticalScale;
    private readonly PixelShaderEffect<SwitchTrackShader>? _trackEffect;
    private Color _trackColor;
    private readonly CanvasControl _canvas = new() { IsHitTestVisible = false };
    private readonly CropEffect _sceneCrop = new();
    private readonly BorderEffect _sceneBorder;
    private readonly Transform2DEffect _sceneEffect = new() { BorderMode = EffectBorderMode.Hard };
    private readonly GaussianBlurEffect _blur;
    private readonly SaturationEffect _saturation;
    private readonly Transform2DEffect _glyphs = new();
    private readonly ColorSourceEffect _transparent = new() { Color = Color.FromArgb(0, 0, 0, 0) };
    private readonly PixelShaderEffect<LiquidGlassShader> _shader;
    private CanvasBitmap? _backdrop, _labelBitmap;
    private CanvasRenderTarget? _target;
    private (int Width, int Height, float Scale) _targetSize;
    private int _version = -1, _labelVersion = -1;
    private LiquidGlassMaterial _material;
    private float _pressure, _stretch, _angle, _energy, _lift = 1;
    private bool _externalPose;
    private Vector2 _bend;
    private Vector2 _grab = new(.5f, .5f);
    private Vector2 _light = new(.22f, .05f);
    private bool _disposed, _failed, _ready;

    public bool HasLabels => !_failed && _labelBitmap is not null;
    public bool HasFailed => _failed;

    public LiquidGlassRenderer(Grid host, LiquidGlassScene scene, float cornerRadius,
        Border? fallback = null, LiquidGlassScene? labels = null, Action<bool>? materialReady = null,
        FrameworkElement? switchTrack = null, float opticalScale = 1)
    {
        _host = host;
        _scene = scene;
        _labels = labels;
        _fallback = fallback;
        _materialReady = materialReady;
        _switchTrack = switchTrack;
        _opticalScale = opticalScale;
        _sceneBorder = new BorderEffect { Source = _sceneCrop, ExtendX = CanvasEdgeBehavior.Clamp, ExtendY = CanvasEdgeBehavior.Clamp };
        _sceneEffect.Source = _sceneBorder;
        _material = new(new GlassCorners(cornerRadius), 6, 24, 1.12f, Color.FromArgb(255, 232, 223, 252), .24f, true);
        _blur = new GaussianBlurEffect { Source = _sceneEffect, BorderMode = EffectBorderMode.Hard, Optimization = EffectOptimization.Balanced };
        _saturation = new SaturationEffect { Source = _blur, Saturation = 1.12f };
        _shader = new PixelShaderEffect<LiquidGlassShader>
        {
            // Include all displaced samples in the upstream input rectangles.
            TransformMapper = D2D1DrawTransformMapper<LiquidGlassShader>.Inflate(96),
        };
        if (switchTrack is not null)
        {
            _trackEffect = new PixelShaderEffect<SwitchTrackShader>
            {
                TransformMapper = D2D1DrawTransformMapper<SwitchTrackShader>.Inflate(96),
            };
            _trackEffect.Sources[0] = _saturation;
            _shader.Sources[0] = _trackEffect;
        }
        else _shader.Sources[0] = _saturation;
        _shader.Sources[1] = _transparent;
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        _scene.Changed += Invalidate;
        if (_labels is not null) { _labels.Changed += Invalidate; _labels.Invalidate(); }
        _scene.Invalidate();
        host.Children.Add(_canvas);
    }

    public void SetTrackColor(Color color)
    {
        if (_trackColor == color) return;
        _trackColor = color;
        Invalidate();
    }

    public void SetMaterial(LiquidGlassMaterial material)
    {
        if (_material == material) return;
        _material = material;
        Invalidate();
    }

    public void Invalidate()
    {
        if (!_disposed && !_failed) _canvas.Invalidate();
    }

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _backdrop = _labelBitmap = null;
        _version = _labelVersion = -1;
        _sceneCrop.Source = _glyphs.Source = null;
        _target?.Dispose();
        _target = null;
        _targetSize = default;
        _failed = false;
        _canvas.Opacity = 1;
        SetReady(false);
    }

    public void SetInteraction(double pressure, double x, double y)
    {
        _pressure = (float)Math.Clamp(pressure, 0, 1.16);
        _light = new((float)Math.Clamp(x, 0, 1), (float)Math.Clamp(y, 0, 1));
        Invalidate();
    }

    public void SetDeformation(double stretch, double angle, double bendX, double bendY, double energy)
    {
        _stretch = (float)Math.Clamp(stretch, -.20, .34);
        _angle = (float)angle;
        _bend = new((float)Math.Clamp(bendX, -16, 16), (float)Math.Clamp(bendY, -16, 16));
        _energy = (float)Math.Clamp(energy, 0, 1);
        Invalidate();
    }

    public void SetPull(double x, double y, double grabX, double grabY, double energy)
    {
        _stretch = _angle = 0;
        _bend = new((float)Math.Clamp(x, -24, 24), (float)Math.Clamp(y, -24, 24));
        _grab = new((float)Math.Clamp(grabX, 0, 1), (float)Math.Clamp(grabY, 0, 1));
        _energy = (float)Math.Clamp(energy, 0, 1);
        Invalidate();
    }

    // The native visual carries the affine pose, including crisp foreground content.
    // Supply its inverse to the optics so normals/refraction remain in scene coordinates.
    public void SetJellyPose(double stretch, double angle, double lift, double bendX, double bendY, double energy)
    {
        _externalPose = true;
        _lift = (float)lift;
        SetDeformation(stretch, angle, bendX, bendY, energy);
    }

    private void OnDraw(CanvasControl canvas, CanvasDrawEventArgs args)
    {
        if (_disposed || _failed || _scene.IsDisposed || _scene.Pixels is null) return;
        float w = (float)canvas.ActualWidth, h = (float)canvas.ActualHeight;
        if (w < 2 || h < 2) return;
        try
        {
            // The switch expands through a native transform. Keep its small texture
            // sharp at the maximum pressed scale, without reallocating on every frame.
            float scale = LiquidGlassOptics.TextureScale(w, h, canvas.Dpi * (_switchTrack is null ? 1 : 2));
            int width = Math.Max(1, (int)Math.Ceiling(w * scale)), height = Math.Max(1, (int)Math.Ceiling(h * scale));
            if (_targetSize != (width, height, scale))
            {
                _target?.Dispose();
                _target = new CanvasRenderTarget(canvas, width, height, 96);
                _targetSize = (width, height, scale);
            }
            if (_version != _scene.Version)
            {
                _backdrop = GetBitmap(_scene, canvas);
                _sceneCrop.Source = _backdrop;
                _version = _scene.Version;
            }
            if (_backdrop is null) return;
            // A tall/scrolled card can refract past the captured window. Clamp the
            // decorative scene's layout extent, excluding transparent capture overflow.
            _sceneCrop.SourceRectangle = new Rect(0, 0,
                Math.Min(_backdrop.Size.Width, _scene.Element.ActualWidth * _scene.PixelsPerDip * _backdrop.Size.Width / _scene.Width),
                Math.Min(_backdrop.Size.Height, _scene.Element.ActualHeight * _scene.PixelsPerDip * _backdrop.Size.Height / _scene.Height));
            if (_labels?.Pixels is not null && _labelVersion != _labels.Version)
            {
                _labelBitmap = GetBitmap(_labels, canvas);
                _glyphs.Source = _labelBitmap;
                _labelVersion = _labels.Version;
                _shader.Sources[1] = _glyphs;
            }
            _sceneEffect.TransformMatrix = SourceTransform(canvas, _scene, _backdrop) * Matrix3x2.CreateScale(scale);
            if (_labels is not null && _labelBitmap is not null)
                _glyphs.TransformMatrix = SourceTransform(canvas, _labels, _labelBitmap) * Matrix3x2.CreateScale(scale);
            if (_switchTrack is not null && _trackEffect is not null)
            {
                var toTrack = canvas.TransformToVisual(_switchTrack);
                Point origin = toTrack.TransformPoint(new Point());
                Point axisX = toTrack.TransformPoint(new Point(1, 0));
                Point axisY = toTrack.TransformPoint(new Point(0, 1));
                _trackEffect.ConstantBuffer = new(
                    new Float4((float)(axisX.X - origin.X), (float)(axisY.X - origin.X),
                        (float)(axisX.Y - origin.Y), (float)(axisY.Y - origin.Y)),
                    new Float2((float)origin.X, (float)origin.Y),
                    new Float2((float)_switchTrack.ActualWidth, (float)_switchTrack.ActualHeight),
                    new Float4(_trackColor.R / 255f, _trackColor.G / 255f, _trackColor.B / 255f, 1), scale);
            }
            bool lens = _labels is not null;
            float activation = Math.Clamp(_pressure, 0, 1);
            _blur.BlurAmount = _material.Blur * scale * (lens ? 1 - .8f * activation : 1);
            _saturation.Saturation = _material.Saturation;
            var size = new Vector2(Math.Max(1, w - 2 * _material.Padding), Math.Max(1, h - 2 * _material.Padding));
            var pointer = new Float2(_material.Padding + size.X * _light.X, _material.Padding + size.Y * _light.Y);
            if (lens) size *= new Vector2(1 + .025f * activation, 1 + .39f * activation);
            GlassCorners corners = _material.Corners.Fit(size.X, size.Y);
            if (lens) corners = new GlassCorners(Math.Min(size.X, size.Y) / 2);
            float along = 1 + _stretch, across = 1 / along;
            float c = MathF.Cos(_angle), s = MathF.Sin(_angle);
            // Invert the volume-preserving stretch along the actual velocity vector.
            float lift = _externalPose ? _lift : 1;
            // ComputeSharp vector operators are shader intrinsics, not CPU arithmetic.
            // Do scalar arithmetic before constructing the uniform (vector division returns default on the CPU).
            var inverse = new Float4((across * c * c + along * s * s) / lift, (across - along) * c * s / lift,
                (across - along) * c * s / lift, (across * s * s + along * c * c) / lift);
            Color tint = _material.Tint;
            float opacity = _material.TintOpacity * tint.A / 255f * (lens ? 1 : 1 - .12f * activation);
            _shader.ConstantBuffer = new LiquidGlassShader(
                new Float2(size.X, size.Y), new Float2(w / 2, h / 2),
                new Float4(corners.TopLeft, corners.TopRight, corners.BottomRight, corners.BottomLeft),
                pointer, inverse, new Float2(_bend.X, _bend.Y),
                new Float4(tint.R / 255f, tint.G / 255f, tint.B / 255f, opacity),
                activation, _material.Refraction * (lens ? 1 : 1 + .25f * activation),
                _material.Dispersion, _energy, scale, _material.ShowSheen ? .85f : 0,
                lens && _labelBitmap is not null ? 1 : 0, 0, lens ? 0 : _externalPose ? 2 : 1,
                new Float2(_grab.X, _grab.Y), _material.InteriorEffects, _switchTrack is null ? 0 : 1, _opticalScale);
            // Complex samplers stay in one physical-pixel space. Final presentation alone uses the XAML DPI.
            using (var draw = _target!.CreateDrawingSession())
            {
                draw.Clear(Color.FromArgb(0, 0, 0, 0));
                draw.DrawImage(_shader, Vector2.Zero, new Rect(0, 0, w * scale, h * scale));
            }
            args.DrawingSession.DrawImage(_target, new Rect(0, 0, w, h), new Rect(0, 0, w * scale, h * scale));
            SetReady(true);
        }
        catch (Exception ex)
        {
            if (canvas.Device.IsDeviceLost(ex.HResult)) throw;
            _failed = true;
            SetReady(false);
            _canvas.Opacity = 0;
            App.Services.Diagnostics.Warning("玻璃像素着色器已降级：" + ex.Message);
        }
    }

    private static Matrix3x2 SourceTransform(CanvasControl canvas, LiquidGlassScene scene, CanvasBitmap bitmap)
    {
        var transform = canvas.TransformToVisual(scene.Element);
        Point p = transform.TransformPoint(new Point());
        Point x = transform.TransformPoint(new Point(1, 0));
        Point y = transform.TransformPoint(new Point(0, 1));
        var toSource = new Matrix3x2((float)(x.X - p.X), (float)(x.Y - p.Y),
            (float)(y.X - p.X), (float)(y.Y - p.Y), (float)p.X, (float)p.Y);
        if (!Matrix3x2.Invert(toSource, out var inverse)) return Matrix3x2.Identity;
        // RenderTargetBitmap includes overflowing descendants in its pixel extent. Its width
        // is not the layout width: fitting the whole bitmap would squeeze transparent padding
        // into every lens. Preserve the capture's native pixel-to-DIP scale instead.
        return Matrix3x2.CreateScale((float)(scene.Width / bitmap.Size.Width / scene.PixelsPerDip),
            (float)(scene.Height / bitmap.Size.Height / scene.PixelsPerDip)) * inverse;
    }

    private void SetReady(bool ready)
    {
        if (_fallback is not null)
        {
            _fallback.Opacity = ready ? 0 : 1;
            App.Services.Workspace.NavigationMaterial = ready ? "GPU · 曲面液态玻璃" : "基础材质";
        }
        if (_ready == ready) return;
        _ready = ready;
        _materialReady?.Invoke(ready);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scene.Changed -= Invalidate;
        if (_labels is not null) _labels.Changed -= Invalidate;
        _canvas.CreateResources -= OnCreateResources;
        _canvas.Draw -= OnDraw;
        _host.Children.Remove(_canvas);
        _canvas.RemoveFromVisualTree();
        _shader.Dispose();
        _trackEffect?.Dispose();
        _target?.Dispose();
        _target = null;
        _sceneCrop.Source = _glyphs.Source = null;
        _glyphs.Dispose(); _saturation.Dispose(); _blur.Dispose(); _sceneEffect.Dispose(); _sceneBorder.Dispose(); _sceneCrop.Dispose(); _transparent.Dispose();
        _backdrop = _labelBitmap = null;
        SetReady(false);
    }

    private static CanvasBitmap GetBitmap(LiquidGlassScene scene, CanvasControl canvas)
        => Bitmaps.GetValue(scene, source => new SceneBitmaps(source)).Get(canvas);

    // Shared devices upload a decorative scene once per revision, rather than once per control.
    private sealed class SceneBitmaps : IDisposable
    {
        private readonly LiquidGlassScene _scene;
        private readonly Dictionary<CanvasDevice, (CanvasBitmap Bitmap, int Version)> _devices = new();

        public SceneBitmaps(LiquidGlassScene scene) { _scene = scene; scene.Disposed += Dispose; }

        public CanvasBitmap Get(CanvasControl canvas)
        {
            CanvasDevice device = canvas.Device;
            if (_devices.TryGetValue(device, out var entry) && entry.Version == _scene.Version) return entry.Bitmap;
            CanvasBitmap bitmap = CanvasBitmap.CreateFromBytes(canvas, _scene.Pixels!, _scene.Width, _scene.Height,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
            float scale = Math.Min(1, LiquidGlassOptics.TextureScale(_scene.Width, _scene.Height, 96));
            if (scale < 1)
            {
                var reduced = new CanvasRenderTarget(canvas, Math.Max(1, MathF.Floor(_scene.Width * scale)),
                    Math.Max(1, MathF.Floor(_scene.Height * scale)), 96);
                using (var draw = reduced.CreateDrawingSession())
                {
                    draw.Clear(Color.FromArgb(0, 0, 0, 0));
                    draw.DrawImage(bitmap, new Rect(0, 0, reduced.Size.Width, reduced.Size.Height),
                        new Rect(0, 0, _scene.Width, _scene.Height));
                }
                bitmap.Dispose();
                bitmap = reduced;
            }
            if (_devices.ContainsKey(device)) entry.Bitmap.Dispose();
            _devices[device] = (bitmap, _scene.Version);
            return bitmap;
        }

        public void Dispose()
        {
            _scene.Disposed -= Dispose;
            foreach (var entry in _devices.Values) entry.Bitmap.Dispose();
            _devices.Clear();
        }
    }
}
