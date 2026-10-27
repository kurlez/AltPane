namespace AltPane.Core.Input;

/// <summary>
/// Turns wheel deltas into row steps. One notch is <see cref="Notch"/>.
/// Precision-touchpad deltas are smaller and accumulate until they add up
/// to a notch. A positive delta (the wheel rolled away from the user) moves
/// toward the first row. The step count does not wrap; the session clamps it.
/// </summary>
public sealed class WheelNavigator
{
    public const int Notch = 120;
    long _remainder;

    public int Consume(int delta)
    {
        if (delta == 0) return 0;
        if (_remainder != 0 && Math.Sign(delta) != Math.Sign(_remainder))
            _remainder = 0;
        _remainder += delta;
        var notches = (int)(_remainder / Notch);
        _remainder -= (long)notches * Notch;
        return -notches;
    }

    public void Reset() => _remainder = 0;
}
