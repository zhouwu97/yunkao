using System.Reflection;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using YunKao.Controls;
using YunKao.Core.Presentation;

namespace LiquidGlassDemo;

// Optional integration capture, not OS pointer injection. The lab inspects the actual
// surface/controller/renderer without exposing debug APIs in the shipping controls.
internal static class NativeCapture
{
    internal static async Task RunAsync(FrameworkElement root, string directory)
    {
        var nodes = Descendants(root).ToArray();
        var heading = nodes.OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "AnchoredHeading");
        var surfaces = nodes.OfType<LiquidGlassSurface>().OrderByDescending(s => s.ActualHeight).ToArray();
        var trace = new List<object>();
        foreach (var (surface, index) in surfaces.Select((s, i) => (s, i)))
        {
            var renderer = Read<LiquidGlassRenderer>(surface, "_renderer");
            var controller = Read<LiquidGlassInteraction>(surface, "_interaction");
            var motion = Read<LiquidSurfaceMotion>(controller, "_motion");
            if (renderer.HasFailed || surface.FallbackOpacity != 0) throw new InvalidOperationException("Native glass is using a fallback.");
            string prefix = index == 0 ? "native-card" : "native-button";
            Point initial = heading.TransformToVisual(root).TransformPoint(new Point());

            async Task Frame(string phase)
            {
                renderer.SetInteraction(motion.Pressure, .7, .3);
                renderer.SetPull(motion.PullX, motion.PullY, motion.GrabX, motion.GrabY, motion.Energy);
                await Task.Delay(120);
                var target = Read<CanvasRenderTarget>(renderer, "_target");
                await target.SaveAsync(Path.Combine(directory, $"{prefix}-{phase}.png"), CanvasBitmapFileFormat.Png);
                Point current = heading.TransformToVisual(root).TransformPoint(new Point());
                if (current != initial) throw new InvalidOperationException("Surface deformation moved the content.");
                trace.Add(new { surface = prefix, phase, motion.Held, motion.PullX, motion.PullY, motion.GrabX, motion.GrabY,
                    motion.Moving, centerX = surface.ActualWidth / 2, centerY = surface.ActualHeight / 2,
                    headingX = current.X, headingY = current.Y, surface.FallbackOpacity });
            }
            void Settle() { for (int f = 0; f < 720; f++) motion.Advance(1.0 / 120); }

            motion.Reset(); await Frame("rest");
            double x = surface.ActualWidth / 2;
            motion.Press(x, 5, surface.ActualWidth, surface.ActualHeight);
            motion.Pull(x + 30, -95); Settle(); await Frame("held");
            motion.Pull(x - 30, 105); Settle(); await Frame("reverse");
            motion.Release();
            motion.Advance(1.0 / 60); await Frame("release");
            Settle(); await Frame("settled");
            // Reset the same interaction owner through its normal reduced-motion path.
            motion.Press(30, 20, surface.ActualWidth, surface.ActualHeight);
            motion.Pull(180, 50); Settle();
            YunKao.Services.MotionService.SetReduceMotion(true);
            await Frame("reduced");
            if (motion.Held || motion.Energy != 0 || motion.Moving) throw new InvalidOperationException("Reduced motion left an active deformation.");
            YunKao.Services.MotionService.SetReduceMotion(false);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-motion.json"), JsonSerializer.Serialize(trace));
    }

    private static T Read<T>(object owner, string field) where T : class
        => owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner) as T
            ?? throw new InvalidOperationException($"Native capture cannot inspect {owner.GetType().Name}.{field}.");

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var node in Descendants(child)) yield return node;
        }
    }
}
