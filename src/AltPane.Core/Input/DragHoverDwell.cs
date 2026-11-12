namespace AltPane.Core.Input;

/// <summary>
/// How long a file drag must stay on one switcher target before that
/// window is activated. Pure timing: the UI supplies the clock and the
/// current target id, and never lets this type talk to Win32.
/// </summary>
public sealed class DragHoverDwell
{
    public const int DefaultMilliseconds = 700;
    public const int MinMilliseconds = 300;
    public const int MaxMilliseconds = 2000;

    string? _id;
    long _startedAt;
    bool _fired;

    public string? TargetId => _id;
    public bool IsArmed => _id is not null && !_fired;
    public bool HasFired => _fired;

    public static int Clamp(int milliseconds)
    {
        if (milliseconds < MinMilliseconds) return MinMilliseconds;
        if (milliseconds > MaxMilliseconds) return MaxMilliseconds;
        return milliseconds;
    }

    /// <summary>
    /// Report the target under the cursor. An empty id cancels. A different
    /// id restarts the timer. True only on the tick that crosses the dwell.
    /// </summary>
    public bool Note(string? targetId, long nowMs, int dwellMilliseconds)
    {
        if (string.IsNullOrEmpty(targetId))
        {
            Reset();
            return false;
        }

        var dwell = Clamp(dwellMilliseconds);
        if (!string.Equals(_id, targetId, StringComparison.Ordinal))
        {
            _id = targetId;
            _startedAt = nowMs;
            _fired = false;
            return false;
        }

        if (_fired) return false;
        if (nowMs - _startedAt < dwell) return false;
        _fired = true;
        return true;
    }

    /// <summary>0 to 1, for a progress fill. 1 after the dwell has fired, 0 when idle.</summary>
    public double Progress(long nowMs, int dwellMilliseconds)
    {
        if (_id is null) return 0;
        if (_fired) return 1;
        var dwell = Clamp(dwellMilliseconds);
        if (dwell <= 0) return 1;
        var fraction = (nowMs - _startedAt) / (double)dwell;
        if (fraction <= 0) return 0;
        if (fraction >= 1) return 1;
        return fraction;
    }

    public void Reset()
    {
        _id = null;
        _startedAt = 0;
        _fired = false;
    }
}
