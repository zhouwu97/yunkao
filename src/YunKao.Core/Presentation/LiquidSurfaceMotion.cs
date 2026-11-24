using System.Numerics;

namespace YunKao.Core.Presentation;

/// <summary>Design-systems' tethered jelly motion (7a424c93): temporary travel, lift, velocity strain and lag-driven bend.</summary>
public sealed class LiquidSurfaceMotion(double maxTravel = 75, double intensity = .7, double dragGain = 1)
{
    private Spring _x, _y, _press, _stretch, _bendX, _bendY;
    private double _startX, _startY, _baseX, _baseY, _angle;
    private double _minX = -Math.Abs(maxTravel), _maxX = Math.Abs(maxTravel);
    private double _minY = -Math.Abs(maxTravel), _maxY = Math.Abs(maxTravel);
    private readonly double _amount = double.IsFinite(intensity) ? Math.Clamp(intensity, 0, 1) : .7;
    public bool Held { get; private set; }
    public double PositionX => _x.Value;
    public double PositionY => _y.Value;
    public double Pressure => Math.Clamp(_press.Value, 0, 1.16);
    public double Stretch => Math.Clamp(_stretch.Value, -.20, .34);
    public double Angle => _angle;
    public double BendX => _bendX.Value;
    public double BendY => _bendY.Value;
    public double Lift => 1 + Pressure * _amount * .035;
    public double Energy => Math.Abs(Stretch) + Math.Sqrt(BendX * BendX + BendY * BendY) * .015;
    public bool Moving => _x.Moving || _y.Moving || _press.Moving || _stretch.Moving || _bendX.Moving || _bendY.Moving;

    public void SetBounds(double minX, double maxX, double minY, double maxY)
    {
        if (!double.IsFinite(minX) || !double.IsFinite(maxX) || !double.IsFinite(minY) || !double.IsFinite(maxY)) return;
        double limit = Math.Abs(maxTravel);
        // Include the original resting location even when the host has less than the reference's 22px inset.
        _minX = Math.Max(-limit, Math.Min(0, minX)); _maxX = Math.Min(limit, Math.Max(0, maxX));
        _minY = Math.Max(-limit, Math.Min(0, minY)); _maxY = Math.Min(limit, Math.Max(0, maxY));
    }

    public void Press(double x, double y, double width, double height, double pressure = 1)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            || width <= 0 || height <= 0) return;
        _startX = x; _startY = y;
        _baseX = _x.Target; _baseY = _y.Target;
        Held = true;
        SetPressure(pressure);
    }

    public void Pull(double x, double y, double pressure = 1)
    {
        if (!Held || !double.IsFinite(x) || !double.IsFinite(y)) return;
        _x.Target = Math.Clamp(_baseX + (x - _startX) * dragGain, _minX, _maxX);
        _y.Target = Math.Clamp(_baseY + (y - _startY) * dragGain, _minY, _maxY);
        SetPressure(pressure);
    }

    private void SetPressure(double value) => _press.Target = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;

    public void Release()
    {
        Held = false;
        _x.Target = _y.Target = _press.Target = 0;
    }

    public void Reset()
    {
        Held = false;
        _x = _y = _press = _stretch = _bendX = _bendY = default;
        _angle = 0;
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        double dt = Math.Min(seconds, .05);
        _x.Step(dt, 260, Held ? 22 : 17); _y.Step(dt, 260, Held ? 22 : 17);
        _press.Step(dt, 310, 18);
        double speed = Math.Sqrt(_x.Velocity * _x.Velocity + _y.Velocity * _y.Velocity);
        if (speed > 16) _angle = Math.Atan2(_y.Velocity, _x.Velocity);
        _stretch.Target = Math.Min(speed / 2400, .25) * _amount;
        _stretch.Step(dt, 230, 15);
        _bendX.Target = Math.Clamp((_x.Target - PositionX) * .12, -11, 11) * _amount;
        _bendY.Target = Math.Clamp((_y.Target - PositionY) * .12, -11, 11) * _amount;
        _bendX.Step(dt, 190, 14); _bendY.Step(dt, 190, 14);
    }

    public Matrix3x2 CreatePose(double width, double height, double originX = 0, double originY = 0)
    {
        double along = 1 + Stretch, across = 1 / along, c = Math.Cos(Angle), s = Math.Sin(Angle);
        double a = (along * c * c + across * s * s) * Lift;
        double b = (along - across) * c * s * Lift;
        double d = (along * s * s + across * c * c) * Lift;
        if (Stretch == 0) { a = d = Lift; b = 0; }
        double cx = width * (.5 - originX), cy = height * (.5 - originY);
        return new((float)a, (float)b, (float)b, (float)d,
            (float)(cx * (1 - a) - cy * b + PositionX), (float)(cy * (1 - d) - cx * b + PositionY));
    }

    private struct Spring
    {
        public double Value, Target, Velocity;
        public readonly bool Moving => Math.Abs(Velocity) > .025 || Math.Abs(Target - Value) > .005;
        public void Step(double seconds, double stiffness, double damping)
        {
            double remaining = Math.Min(seconds, .08);
            while (remaining > 1e-12)
            {
                double dt = Math.Min(remaining, 1d / 180);
                Velocity += ((Target - Value) * stiffness - Velocity * damping) * dt;
                Value += Velocity * dt;
                remaining -= dt;
            }
            if (!Moving) { Value = Target; Velocity = 0; }
        }
    }
}
