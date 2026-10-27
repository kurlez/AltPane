namespace AltPane.Core.Input;

/// <summary>
/// Whether a switcher row may show a mouse-hover highlight and take the selection.
/// Keyboard navigation and the mouse wheel suppress hover until that input has been
/// idle for <see cref="IdleMilliseconds"/> and the cursor has actually moved.
/// A stationary cursor when the switcher opens does neither.
/// </summary>
public sealed class HoverGate
{
    public const int IdleMilliseconds = 600;
    public const double MoveEpsilon = 1;

    bool _navigation;
    long _navigationAt;
    double _anchorX = double.NaN;
    double _anchorY = double.NaN;
    bool _hover;

    public bool HoverHighlight => _hover;

    /// <summary>
    /// The switcher became visible. Hover stays off until the cursor leaves
    /// <paramref name="cursorX"/>, <paramref name="cursorY"/>. Pass NaN when the
    /// position is unknown; the first report is then recorded and still ignored.
    /// </summary>
    public void NoteOpened(double cursorX, double cursorY)
    {
        _navigation = false;
        _navigationAt = 0;
        _hover = false;
        _anchorX = cursorX;
        _anchorY = cursorY;
    }

    /// <summary>
    /// Keyboard navigation or a wheel notch. Records the cursor so a later
    /// move event at the same point, including one caused by scrolling the
    /// list under a stationary pointer, does not select.
    /// </summary>
    public void NoteNavigation(long nowMs, double cursorX, double cursorY)
    {
        _navigation = true;
        _navigationAt = nowMs;
        _hover = false;
        _anchorX = cursorX;
        _anchorY = cursorY;
    }

    /// <summary>
    /// A mouse-move report, in the same coordinates as <see cref="NoteNavigation"/>.
    /// True only when hover may highlight and change the selection.
    /// </summary>
    public bool NoteMouseMove(long nowMs, double x, double y)
    {
        if (_hover) return true;

        if (double.IsNaN(_anchorX) || double.IsNaN(_anchorY))
        {
            _anchorX = x;
            _anchorY = y;
            return false;
        }

        var moved = Math.Abs(x - _anchorX) >= MoveEpsilon || Math.Abs(y - _anchorY) >= MoveEpsilon;
        if (_navigation && nowMs - _navigationAt < IdleMilliseconds)
        {
            if (moved)
            {
                _anchorX = x;
                _anchorY = y;
            }
            return false;
        }

        if (!moved) return false;
        _hover = true;
        _navigation = false;
        _anchorX = x;
        _anchorY = y;
        return true;
    }
}
