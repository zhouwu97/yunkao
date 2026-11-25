namespace YunKao.Core.Presentation;

/// <summary>Preview follows the thumb; semantic state changes once, only on release.</summary>
public sealed class LiquidToggleMotion
{
    public const double Travel = 20;
    private double _position, _velocity, _target, _press, _pressVelocity, _pressTarget, _start, _base;
    private bool _restOn, _dragged;
    private double _color, _colorVelocity, _colorTarget;
    public bool Held { get; private set; }
    public double Position => Math.Clamp(_position, 0, Travel);
    public double Progress => Position / Travel;
    public double TrackProgress => Math.Clamp(_color, 0, 1);
    public double Pressure => Math.Clamp(_press, 0, 1.16);
    public bool Moving => Math.Abs(_target-_position) > .005 || Math.Abs(_velocity) > .025
        || Math.Abs(_pressTarget-_press) > .005 || Math.Abs(_pressVelocity) > .025
        || Math.Abs(_colorTarget-_color) > .001 || Math.Abs(_colorVelocity) > .005;
    public double ScaleX => 1 + Pressure * (22d / 30) + Math.Min(Math.Abs(_velocity) / 1800, .07);
    public double ScaleY => 1 + Pressure * (8d / 24) - Math.Min(Math.Abs(_velocity) / 3600, .035);

    public void SetState(bool on, bool snap = false)
    {
        _restOn = on;
        if (Held) return;
        _target = on ? Travel : 0;
        if (snap)
        {
            _position = _target;
            _velocity = _press = _pressVelocity = _pressTarget = 0;
            _color = _colorTarget = on ? 1 : 0;
            _colorVelocity = 0;
        }
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
            // Color follows the visible thumb crossing the middle, including taps
            // and reversal. Merely changing the drag target must not change color.
            _colorTarget = Position >= Travel / 2 ? 1 : 0;
            _colorVelocity += ((_colorTarget - _color) * 420 - _colorVelocity * 38) * dt;
            _color += _colorVelocity * dt;
            remaining -= dt;
        }
        if (Math.Abs(_target-_position)<.005 && Math.Abs(_velocity)<.025) { _position=_target; _velocity=0; }
        if (Math.Abs(_colorTarget-_color)<.001 && Math.Abs(_colorVelocity)<.005) { _color=_colorTarget; _colorVelocity=0; }
        if (Math.Abs(_pressTarget-_press)<.005 && Math.Abs(_pressVelocity)<.025) { _press=_pressTarget; _pressVelocity=0; }
    }
}
