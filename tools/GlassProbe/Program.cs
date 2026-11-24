using System.Numerics;
using System.Text.Json;
using ComputeSharp;
using ComputeSharp.D2D1.Interop;
using ComputeSharp.D2D1.WinUI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.UI;
using YunKao.Controls;

string output = Path.GetFullPath(args.FirstOrDefault() ?? "build/glass-probe");
Directory.CreateDirectory(output);
using var device = new CanvasDevice();
using var scene = new CanvasRenderTarget(device, 320, 180, 96);
using (var draw = scene.CreateDrawingSession())
{
    draw.Clear(Color.FromArgb(255, 218, 230, 241));
    for (int x = 0; x < 320; x += 11) draw.FillRectangle(x, 0, 2, 180, Color.FromArgb(255, 35, 67, 104));
    for (int y = 0; y < 180; y += 11) draw.FillRectangle(0, y, 320, 2, Color.FromArgb(255, 35, 67, 104));
    draw.DrawText("GLASS / 0123", 102, 65, Color.FromArgb(255, 26, 32, 45));
}
using var ink = new CanvasRenderTarget(device, 320, 180, 96);
using (var draw = ink.CreateDrawingSession())
{
    draw.Clear(Color.FromArgb(0, 0, 0, 0));
    draw.FillRectangle(243, 70, 4, 38, Color.FromArgb(255, 25, 80, 220));
}
var results = new List<object>();
int failures = 0;
void Check(string name, bool passed, double measured)
{
    results.Add(new { name, passed, measured });
    if (!passed) failures++;
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {measured:F3}");
}
CanvasRenderTarget Render(float compare = 0, float bend = 0, float dpi = 96, float foreground = 1, float sheen = 0,
    bool translated = false, bool anchored = false, float stretch = 0, float pullY = 0)
{
    float scale = dpi / 96;
    // Keep the effect graph in physical pixels at 96 DPI. Win2D then presents this texture
    // in logical coordinates, avoiding implicit DPI-compensation nodes on complex samplers.
    var target = new CanvasRenderTarget(device, 320 * scale, 180 * scale, 96);
    using var atlas = new CanvasRenderTarget(device, 960, 700, 96);
    using (var draw = atlas.CreateDrawingSession())
    {
        draw.Clear(Color.FromArgb(255, 218, 230, 241));
        draw.DrawImage(scene, 400, 200);
    }
    using var backdrop = new Transform2DEffect { Source = translated ? atlas : scene,
        BorderMode = EffectBorderMode.Hard,
        TransformMatrix = (translated ? Matrix3x2.CreateTranslation(-400, -200) : Matrix3x2.Identity) * Matrix3x2.CreateScale(scale) };
    using var blur = new GaussianBlurEffect { Source = backdrop, BlurAmount = 0, BorderMode = EffectBorderMode.Hard };
    using var saturation = new SaturationEffect { Source = blur, Saturation = 1 };
    using var lettering = new Transform2DEffect { Source = ink, TransformMatrix = Matrix3x2.CreateScale(scale) };
    using var effect = new PixelShaderEffect<LiquidGlassShader>
    {
        ConstantBuffer = new(new Float2(200, 80), new Float2(160, 90), new Float4(40, 40, 40, 40),
            new Float2(90, 20), new Float4(1 / (1 + stretch), 0, 0, 1 + stretch), new Float2(bend, pullY), new Float4(1, 1, 1, .06f),
            1, 22, .5f, .2f, dpi / 96, sheen, foreground, compare, anchored ? 1 : 0, new Float2(.5f, 0)),
        TransformMapper = D2D1DrawTransformMapper<LiquidGlassShader>.Inflate(96),
    };
    effect.Sources[0] = saturation;
    effect.Sources[1] = lettering;
    using (var draw = target.CreateDrawingSession())
    {
        draw.Clear(Color.FromArgb(0, 0, 0, 0));
        draw.DrawImage(effect, Vector2.Zero, new Windows.Foundation.Rect(0, 0, 320 * scale, 180 * scale));
    }
    return target;
}
static double Difference(byte[] a, byte[] b, int left, int top, int right, int bottom, int channel = -1)
{
    double sum = 0;
    int count = 0;
    for (int y = top; y < bottom; y++)
        for (int x = left; x < right; x++)
            for (int c = channel < 0 ? 0 : channel; c < (channel < 0 ? 3 : channel + 1); c++)
            {
                sum += Math.Abs(a[(y * 320 + x) * 4 + c] - b[(y * 320 + x) * 4 + c]);
                count++;
            }
    return sum / Math.Max(1, count);
}
using var glass = Render();
using var control = Render(compare: 1);
using var right = Render(bend: 10);
using var left = Render(bend: -10);
using var retina = Render(dpi: 144);
using var lit = Render(sheen: .8f);
using var translated = Render(translated: true);
using var translatedRetina = Render(translated: true, dpi: 144);
using var anchored = Render(anchored: true, pullY: -18);
byte[] a = glass.GetPixelBytes(), b = control.GetPixelBytes(), rp = right.GetPixelBytes(), lp = left.GetPixelBytes();
Check("curved edge displaces scene", Difference(a, b, 65, 65, 84, 115) > 7, Difference(a, b, 65, 65, 84, 115));
Check("flat center preserves detail", Difference(a, b, 135, 75, 185, 105) < 1, Difference(a, b, 135, 75, 185, 105));
Check("foreground is optically sampled", Difference(a, b, 230, 70, 260, 110) > 5, Difference(a, b, 230, 70, 260, 110));
Check("outside stays transparent", a[3] == 0, a[3]);
double shapeChange = Difference(a, rp, 45, 45, 275, 135, 3);
Check("bend changes the silhouette", shapeChange > 1, shapeChange);
double mirrorError = 0;
for (int y = 30; y < 150; y++)
    for (int x = 30; x < 290; x++) mirrorError += Math.Abs(rp[(y * 320 + x) * 4 + 3] - lp[(y * 320 + 319 - x) * 4 + 3]);
mirrorError /= 120 * 260;
Check("reversal mirrors deformation", mirrorError < 1, mirrorError);
byte[] dp = retina.GetPixelBytes();
double dpiError = 0;
for (int y = 60; y < 120; y++)
    for (int x = 75; x < 245; x++)
        dpiError += Math.Abs(a[(y * 320 + x) * 4 + 3] - dp[((int)((y + .5) * 1.5) * 480 + (int)((x + .5) * 1.5)) * 4 + 3]);
dpiError /= 60 * 170;
Check("logical geometry survives 150 percent DPI", dpiError < 1, dpiError);
double lightDifference = Difference(a, lit.GetPixelBytes(), 65, 53, 255, 65);
Check("directional lighting affects rim", lightDifference > .5, lightDifference);
double translationError = Difference(a, translated.GetPixelBytes(), 70, 60, 250, 120);
Check("translated scene keeps sampling coordinates", translationError < 1, translationError);
byte[] tr = translatedRetina.GetPixelBytes();
double translatedDpiError = dp.Zip(tr, (one, two) => Math.Abs(one - two)).Average();
Check("translated scene remains aligned at 150 percent DPI", translatedDpiError < 1, translatedDpiError);
byte[] ap = anchored.GetPixelBytes();
double oppositeError = Difference(a, ap, 35, 92, 285, 145, 3);
Check("local pull leaves the opposite edge fixed", oppositeError == 0, oppositeError);
// Leave the support transition outside the stable-corner regions (52 DIP from the anchor).
double sidesError = Difference(a, ap, 35, 35, 105, 145, 3) + Difference(a, ap, 215, 35, 285, 145, 3);
Check("local pull leaves distant corners fixed", sidesError == 0, sidesError);
double bodyError = Difference(a, ap, 140, 80, 180, 100);
Check("local pull preserves the body texture", bodyError < .01, bodyError);
double anchoredChange = Difference(a, ap, 120, 25, 200, 70, 3);
Check("grabbed edge bulges locally", anchoredChange > 3, anchoredChange);
await scene.SaveAsync(Path.Combine(output, "scene.png"), CanvasBitmapFileFormat.Png);
await glass.SaveAsync(Path.Combine(output, "refracted.png"), CanvasBitmapFileFormat.Png);
await control.SaveAsync(Path.Combine(output, "blur-only.png"), CanvasBitmapFileFormat.Png);
await left.SaveAsync(Path.Combine(output, "bend-left.png"), CanvasBitmapFileFormat.Png);
await right.SaveAsync(Path.Combine(output, "bend-right.png"), CanvasBitmapFileFormat.Png);
await lit.SaveAsync(Path.Combine(output, "directional-light.png"), CanvasBitmapFileFormat.Png);
await retina.SaveAsync(Path.Combine(output, "dpi-150.png"), CanvasBitmapFileFormat.Png);
await translated.SaveAsync(Path.Combine(output, "translated.png"), CanvasBitmapFileFormat.Png);
await anchored.SaveAsync(Path.Combine(output, "anchored-pull.png"), CanvasBitmapFileFormat.Png);
await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
{
    backend = "Win2D / Direct2D / ComputeSharp pixel shader",
    forceSoftwareRenderer = device.ForceSoftwareRenderer,
    failures, checks = results,
}, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;
