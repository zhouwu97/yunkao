namespace YunKao.Core.Presentation;

/// <summary>Vertical adaptation of FlutterLiquidglass's position, pressure, bend and stretch springs.</summary>
public sealed class LiquidRailMotion
{
    private Spring _position, _pressure, _stretch, _cross, _bend, _crossBend;
    private double _angle = Math.PI / 2;
    public double Position => _position.Value;
    public double Pressure => Math.Clamp(_pressure.Value, 0, 1.16);
    public double Stretch => Math.Clamp(_stretch.Value, -.20, .34);
    public double Cross => _cross.Value;
    public double Bend => _bend.Value;
    public double CrossBend => _crossBend.Value;
    public double Angle => _angle;
    public double Energy => Math.Abs(Stretch) + Math.Sqrt(Bend * Bend + CrossBend * CrossBend) * .015;
    public bool Held { get; private set; }
    public bool Moving => _position.Moving || _pressure.Moving || _stretch.Moving || _cross.Moving || _bend.Moving || _crossBend.Moving;

    public void Aim(double position, bool held = false, double pressure = 0, double cross = 0)
    {
        Held = held;
        _position.Target = position;
        _pressure.Target = pressure;
        _cross.Target = Math.Clamp(cross, -9, 9);
    }

    public void Snap(double position)
    {
        _position = new Spring { Value = position, Target = position };
        _pressure = _stretch = _cross = _bend = _crossBend = default;
        _angle = Math.PI / 2;
        Held = false;
    }

    public void Advance(double seconds)
    {
        double remaining = Math.Clamp(seconds, 0, .08);
        while (remaining > 1e-9)
        {
            double dt = Math.Min(remaining, 1.0 / 180);
            _position.Step(dt, 260, Held ? 22 : 17);
            _cross.Step(dt, 260, Held ? 22 : 17);
            _pressure.Step(dt, 310, 18);
            double speed = Math.Sqrt(_position.Velocity * _position.Velocity + _cross.Velocity * _cross.Velocity);
            if (speed > 16) _angle = Math.Atan2(_position.Velocity, _cross.Velocity);
            _stretch.Target = Math.Min(speed / 2400, .25);
            _stretch.Step(dt, 230, 15);
            _bend.Target = Math.Clamp((_position.Target - Position) * .12, -11, 11);
            _bend.Step(dt, 190, 14);
            _crossBend.Target = Math.Clamp((_cross.Target - Cross) * .12, -11, 11);
            _crossBend.Step(dt, 190, 14);
            remaining -= dt;
        }
    }

    public static int ReleaseIndex(double index, double speed, double step, int count)
    {
        int nearest = (int)Math.Floor(index + .5);
        if (step > 0 && Math.Abs(speed) >= 450)
        {
            int low = speed > 0 ? (int)Math.Floor(index) : (int)Math.Ceiling(index) - 1;
            nearest = Math.Clamp((int)Math.Floor(index + speed / step * .10 + .5), low, low + 1);
        }
        return Math.Clamp(nearest, 0, count - 1);
    }

    private struct Spring
    {
        public double Value, Target, Velocity;
        public readonly bool Moving => Math.Abs(Velocity) > .025 || Math.Abs(Target - Value) > .005;
        public void Step(double dt, double stiffness, double damping)
        {
            Velocity += ((Target - Value) * stiffness - Velocity * damping) * dt;
            Value += Velocity * dt;
            if (!Moving) { Value = Target; Velocity = 0; }
        }
    }
}
