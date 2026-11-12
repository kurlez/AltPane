namespace AltPane.Core.Input;

/// <summary>
/// Whether the switcher may take the foreground while a mouse button is held.
/// The low-level keyboard hook already opens on Alt+Tab during an OLE drag;
/// taking the foreground, or focusing the search box, would cancel that drag.
/// </summary>
public static class DragSwitcherPolicy
{
    /// <summary>
    /// False when a mouse button is down: show the switcher with SWP_NOACTIVATE
    /// and do not call SetForegroundWindow.
    /// </summary>
    public static bool TakeForeground(bool mouseButtonDown) => !mouseButtonDown;

    /// <summary>
    /// False when a mouse button is down: do not ClaimKeyboard / FocusSearch.
    /// </summary>
    public static bool ClaimKeyboard(bool mouseButtonDown) => !mouseButtonDown;
}
