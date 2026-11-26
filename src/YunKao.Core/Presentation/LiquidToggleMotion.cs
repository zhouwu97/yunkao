namespace YunKao.Core.Presentation;

/// <summary>Preview follows the thumb; semantic state changes once, only on release.</summary>
public sealed class LiquidToggleMotion
{
    public const double Travel = 20;
    private double _position, _velocity, _target, _press, _pressVelocity, _pressTarget, _start;
    private bool _restOn, _dragged;
    private double _color, _colorVelocity, _colorTarget;
    private Spring _strain, _bendX, _bendY;
    private double _startY, _lastX;
    private const double TapLiftSeconds = .14, TapTravelSeconds = .28;
    private double _heldSeconds, _tapSeconds, _tapLift, _tapFrom, _tapTo;
    private bool _tapActive, _tapPosition, _tapInterrupted;
    public bool Elastic { get; set; }
    public bool Held { get; private set; }
    public double Position => Math.Clamp(_position, 0, Travel);
    public double Progress => Position / Travel;
    public double TrackProgress => Math.Clamp(_color, 0, 1);
    public double Pressure => Math.Clamp(_press, 0, 1.16);
    public bool Moving => _tapActive || Math.Abs(_target-_position) > .005 || Math.Abs(_velocity) > .025
        || Math.Abs(_pressTarget-_press) > .005 || Math.Abs(_pressVelocity) > .025
        || Math.Abs(_colorTarget-_color) > .001 || Math.Abs(_colorVelocity) > .005
        || _strain.Moving || _bendX.Moving || _bendY.Moving;
    public double Stretch => Elastic ? Math.Clamp(_strain.Value, -.025, .04) : 0;
    public double BendX => Elastic ? _bendX.Value : 0;
    public double BendY => Elastic ? _bendY.Value : 0;
    public double Energy => Math.Abs(Stretch) + Math.Sqrt(BendX * BendX + BendY * BendY) * .08;
    public double ScaleX => Elastic ? (1 + Pressure * (22d / 30)) * (1 + Stretch)
        : 1 + Pressure * (22d / 30) + Math.Min(Math.Abs(_velocity) / 1800, .07);
    public double ScaleY => Elastic ? (1 + Pressure * (8d / 24)) / (1 + Stretch)
        : 1 + Pressure * (8d / 24) - Math.Min(Math.Abs(_velocity) / 3600, .035);

    public void SetState(bool on, bool snap = false)
    {
        _restOn = on;
        if (Held) return;
        if (!snap && _tapActive && _tapTo == (on ? Travel : 0)) return;
        _tapActive = _tapPosition = false;
        _target = on ? Travel : 0;
        if (snap)
        {
            _position = _target;
            _velocity = _press = _pressVelocity = _pressTarget = 0;
            _color = _colorTarget = on ? 1 : 0;
            _colorVelocity = 0;
            _strain = _bendX = _bendY = default;
        }
    }
    public void Press(double x, double y = 0)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        _tapInterrupted = Moving;
        _heldSeconds = 0;
        // A new press can still be a tap; keep its current journey until drag takeover.
        if (!_tapActive) { _tapPosition = false; _target = Position; }
        _start = _lastX = x; _startY = y; Held = true; _dragged = false; _pressTarget = 1;
    }
    public void Pull(double x, double y = 0)
    {
        if (!Held || !double.IsFinite(x) || !double.IsFinite(y)) return;
        _dragged |= Math.Abs(x-_start) > 5;
        double delta = x - _lastX;
        _lastX = x;
        if (_tapPosition)
        {
            if (!_dragged) return;
            _tapActive = _tapPosition = false;
            _target = Position;
            delta = x - _start;
        }
        // Discard off-rail travel on each event so reversal has no dead zone.
        _target = Math.Clamp(_target + delta, 0, Travel);
        _bendY.Target = Elastic ? Math.Clamp((y - _startY) * .14, -2.4, 2.4) : 0;
    }
    public bool Release(bool cancel = false)
    {
        if (!Held)
        {
            if (cancel) { _tapActive = _tapPosition = false; _pressTarget = 0; _bendY.Target = 0; SetState(_restOn); }
            return _restOn;
        }
        bool on = cancel ? _restOn : _dragged ? _target >= Travel/2 : !_restOn;
        Held = false; _pressTarget = 0; _bendY.Target = 0; SetState(on);
        if (!cancel && !_dragged)
        {
            // A quick click keeps the same press -> drag -> release as a held
            // gesture. Commit on pointer-up; complete the optical gesture separately.
            _tapFrom = Position; _tapTo = on ? Travel : 0; _target = _tapFrom;
            _tapSeconds = _tapInterrupted ? TapTravelSeconds : 0;
            _tapLift = _tapInterrupted ? 0 : Math.Max(0, TapLiftSeconds - _heldSeconds);
            _tapActive = _tapPosition = true; _pressTarget = 1;
        }
        return on;
    }
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        double remaining = Math.Min(seconds, .05);
        while (remaining > 1e-12)
        {
            double realStep = Math.Min(remaining, 1d/180);
            double dt = realStep * (_tapPosition ? 1.15 : 1);
            if (Held) _heldSeconds += realStep;
            if (_tapActive)
            {
                _tapSeconds += dt;
                double t = Math.Clamp((_tapSeconds - _tapLift) / TapTravelSeconds, 0, 1);
                _target = _tapFrom + (_tapTo - _tapFrom) * t * t * (3 - 2 * t);
                if (t == 1 && Math.Abs(_tapTo - _position) < .4 && Math.Abs(_velocity) < 12)
                {
                    _tapActive = false;
                    if (!Held) _pressTarget = 0;
                }
            }
            // The guided tap has a smooth arrival; the real drag retains its
            // original inertia. Shape springs still recoil on either release.
            double stiffness = _tapPosition ? 700 : 260;
            double damping = _tapPosition ? 54 : Elastic ? Held ? 22 : 17 : 26;
            _velocity += ((_target-_position)*stiffness-_velocity*damping)*dt;
            _position += _velocity*dt;
            _pressVelocity += ((_pressTarget-_press)*310-_pressVelocity*(_tapPosition && !_tapActive ? 24 : 18))*dt;
            _press += _pressVelocity*dt;
            // Like the moving lens, strain and lag bend have their own inertia.
            // Reversal/release changes targets without discarding their velocity.
            _strain.Target = Elastic ? Math.Min(Math.Abs(_velocity) / 2400, .04) : 0;
            _bendX.Target = Elastic ? Math.Clamp((_target - _position) * .20, -3, 3) : 0;
            _strain.Step(dt, _tapPosition ? 300 : 230, _tapPosition ? 24 : 15);
            _bendX.Step(dt, _tapPosition ? 300 : 190, _tapPosition ? 24 : 14);
            _bendY.Step(dt, 190, 14);
            // Color follows the visible thumb crossing the middle, including taps
            // and reversal. Merely changing the drag target must not change color.
            _colorTarget = Position >= Travel / 2 ? 1 : 0;
            _colorVelocity += ((_colorTarget - _color) * 420 - _colorVelocity * 38) * dt;
            _color += _colorVelocity * dt;
            remaining -= realStep;
        }
        if (Math.Abs(_target-_position)<.005 && Math.Abs(_velocity)<.025) { _position=_target; _velocity=0; }
        if (Math.Abs(_colorTarget-_color)<.001 && Math.Abs(_colorVelocity)<.005) { _color=_colorTarget; _colorVelocity=0; }
        if (Math.Abs(_pressTarget-_press)<.005 && Math.Abs(_pressVelocity)<.025) { _press=_pressTarget; _pressVelocity=0; }
    }

    private struct Spring
    {
        public double Value, Target, Velocity;
        public readonly bool Moving => Math.Abs(Target - Value) > .0001 || Math.Abs(Velocity) > .001;
        public void Step(double dt, double stiffness, double damping)
        {
            Velocity += ((Target - Value) * stiffness - Velocity * damping) * dt;
            Value += Velocity * dt;
            if (!Moving) { Value = Target; Velocity = 0; }
        }
    }
}
