namespace YunKao.Core.Presentation;

/// <summary>Preview follows the thumb; semantic state changes once, only on release.</summary>
public sealed class LiquidToggleMotion
{
    public const double Travel = 22;
    private double _position, _velocity, _target, _press, _pressVelocity, _pressTarget, _start, _base;
    private bool _restOn, _dragged;
    public bool Held { get; private set; }
    public double Position => Math.Clamp(_position, 0, Travel);
    public double Progress => Position / Travel;
    public double Pressure => Math.Clamp(_press, 0, 1.16);
    public bool Moving => Math.Abs(_target-_position) > .005 || Math.Abs(_velocity) > .025
        || Math.Abs(_pressTarget-_press) > .005 || Math.Abs(_pressVelocity) > .025;
    public double ScaleX => 1 + Math.Clamp(Pressure * .10 + Math.Abs(_velocity) / 2400, 0, .18);
    public double ScaleY => 1 / ScaleX;

    public void SetState(bool on, bool snap = false)
    {
        _restOn = on;
        if (Held) return;
        _target = on ? Travel : 0;
        if (snap) { _position = _target; _velocity = _press = _pressVelocity = _pressTarget = 0; }
    }
    public void Press(double x)
    {
        if (!double.IsFinite(x)) return;
        _start = x; _base = Position; Held = true; _dragged = false; _pressTarget = 1;
    }
    public void Pull(double x)
    {
        if (!Held || !double.IsFinite(x)) return;
        _dragged |= Math.Abs(x-_start) > 5;
        _target = Math.Clamp(_base + x-_start, 0, Travel);
    }
    public bool Release(bool cancel = false)
    {
        if (!Held) return _restOn;
        bool on = cancel ? _restOn : _dragged ? _target >= Travel/2 : !_restOn;
        Held = false; _pressTarget = 0; SetState(on);
        return on;
    }
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        double remaining = Math.Min(seconds, .05);
        while (remaining > 1e-12)
        {
            double dt = Math.Min(remaining, 1d/180);
            _velocity += ((_target-_position)*260-_velocity*26)*dt;
            _position += _velocity*dt;
            _pressVelocity += ((_pressTarget-_press)*310-_pressVelocity*18)*dt;
            _press += _pressVelocity*dt;
            remaining -= dt;
        }
        if (Math.Abs(_target-_position)<.005 && Math.Abs(_velocity)<.025) { _position=_target; _velocity=0; }
        if (Math.Abs(_pressTarget-_press)<.005 && Math.Abs(_pressVelocity)<.025) { _press=_pressTarget; _pressVelocity=0; }
    }
}
