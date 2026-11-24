namespace YunKao.Core.Presentation;

/// <summary>A local elastic pull at the grabbed edge. The panel has no translation, rotation or scale state.</summary>
public sealed class LiquidSurfaceMotion
{
    private Spring _pullX, _pullY, _grabX, _grabY, _pressure, _bulge;
    private double _width = 1, _height = 1, _startX, _startY;
    public bool Held { get; private set; }
    public double Pressure => Math.Clamp(_pressure.Value, 0, 1.12);
    private double RawX => _pullX.Value + (GrabX - .5) * 2 * _bulge.Value;
    private double RawY => _pullY.Value + (GrabY - .5) * 2 * _bulge.Value;
    private double PullScale => Math.Min(1, 25 / Math.Max(.001, Math.Sqrt(RawX * RawX + RawY * RawY)));
    public double PullX => RawX * PullScale;
    public double PullY => RawY * PullScale;
    public double GrabX => Math.Clamp(_grabX.Value, 0, 1);
    public double GrabY => Math.Clamp(_grabY.Value, 0, 1);
    public double Energy => Math.Sqrt(PullX * PullX + PullY * PullY) * .015;
    public bool Moving => _pullX.Moving || _pullY.Moving || _grabX.Moving || _grabY.Moving || _pressure.Moving || _bulge.Moving;

    public void Press(double x, double y, double width, double height, double pressure = 1)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            || width <= 0 || height <= 0) return;
        _startX = x; _startY = y; _width = width; _height = height;
        double ax = Math.Clamp(x, 0, width), ay = Math.Clamp(y, 0, height);
        double edge = Math.Min(Math.Min(ax, width - ax), Math.Min(ay, height - ay));
        if (edge == ax) ax = 0;
        else if (edge == width - ax) ax = width;
        else if (edge == ay) ay = 0;
        else ay = height;
        // Re-grabbing during recoil moves the influence point continuously instead of jumping it.
        if (!Moving && Math.Abs(PullX) + Math.Abs(PullY) < .01)
        {
            _grabX.Set(ax / width); _grabY.Set(ay / height);
        }
        else { _grabX.Target = ax / width; _grabY.Target = ay / height; }
        Held = true;
        Pull(x, y, pressure);
    }

    public void Pull(double x, double y, double pressure = 1)
    {
        if (!Held || !double.IsFinite(x) || !double.IsFinite(y)) return;
        double dx = x - _startX, dy = y - _startY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double response = 1 - Math.Exp(-length / Math.Clamp(Math.Min(_width, _height) * .32, 24, 72));
        double amount = response * Math.Min(19, Math.Min(_width, _height) * .42);
        _pullX.Target = length > .001 ? amount * dx / length : 0;
        _pullY.Target = length > .001 ? amount * dy / length : 0;
        _pressure.Target = double.IsFinite(pressure) ? Math.Clamp(pressure, 0, 1) : 1;
        _bulge.Target = Math.Min(5, Math.Min(_width, _height) * .14) * _pressure.Target;
    }

    public void Release()
    {
        Held = false;
        _pullX.Target = _pullY.Target = _pressure.Target = _bulge.Target = 0;
    }

    public void Reset()
    {
        Held = false;
        _pullX = _pullY = _grabX = _grabY = _pressure = _bulge = default;
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        double remaining = Math.Min(seconds, .08);
        while (remaining > 1e-9)
        {
            double dt = Math.Min(remaining, 1.0 / 180);
            _pullX.Step(dt, 240, 10); _pullY.Step(dt, 240, 10);
            _grabX.Step(dt, 260, 30); _grabY.Step(dt, 260, 30);
            _pressure.Step(dt, 310, 20);
            _bulge.Step(dt, 280, 10);
            remaining -= dt;
        }
    }

    private struct Spring
    {
        public double Value, Target, Velocity;
        public readonly bool Moving => Math.Abs(Velocity) > .0005 || Math.Abs(Target - Value) > .0001;
        public void Set(double value) { Value = Target = value; Velocity = 0; }
        public void Step(double dt, double stiffness, double damping)
        {
            Velocity += ((Target - Value) * stiffness - Velocity * damping) * dt;
            Value += Velocity * dt;
            if (!Moving) { Value = Target; Velocity = 0; }
        }
    }
}
