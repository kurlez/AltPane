using System.Windows.Threading;
using AltPane.Core.Layout;
using AltPane.Core.Localization;
using AltPane.Core.Models;

namespace AltPane;

/// <summary>
/// One full switcher window per monitor. They share the presenter's session:
/// rendering the same <see cref="SwitcherViewState"/> updates every instance.
/// Keyboard focus stays on one window. A click arms that window's monitor as the move target.
/// </summary>
internal sealed class SwitcherDeck
{
    readonly Dictionary<string, SwitcherWindow> _byName = new(StringComparer.OrdinalIgnoreCase);
    readonly List<SwitcherWindow> _spare = new();
    readonly List<SwitcherWindow> _shown = new();
    Action<SwitcherWindow>? _bind;
    SwitcherWindow? _dispatcherWindow;
    SwitcherWindow? _keyboard;
    DisplayBounds? _click;
    SwitcherSize _size = SwitcherSize.Comfortable;
    int _anchorIndex;
    nint _previousForeground;

    public event Action? DisplaysChanged;

    public Dispatcher Dispatcher => EnsureDispatcher().Dispatcher;
    public bool IsVisible => All().Any(window => window.IsVisible);
    public bool IsImeComposing => Keyboard()?.IsImeComposing == true;
    public int QueryCharCount => Keyboard()?.QueryCharCount ?? VisibleQuery();
    public bool IsSearchMode => Keyboard()?.IsSearchMode == true || _shown.Exists(window => window.IsVisible && window.IsSearchMode);
    public bool EscapeBelongsToIme() => All().Any(window => window.IsVisible && window.EscapeBelongsToIme());

    public void Attach(Action<SwitcherWindow> bind)
    {
        _bind = bind;
        EnsureDispatcher();
        try
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    public void Prepare(AppSettings settings, nint activeWindow, bool captureForeground = true)
    {
        _size = settings.Size;
        var monitors = MonitorUtil.All();
        if (monitors.Count == 0)
            monitors = new[] { Fallback() };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _shown.Clear();
        for (var i = 0; i < monitors.Count; i++)
        {
            var name = MonitorKey(monitors[i], i);
            seen.Add(name);
            var window = Take(name);
            window.BindMonitor(monitors[i], Localizer.Current["monitor.thisScreen"]);
            window.RememberForeground(0);
            _shown.Add(window);
        }

        var retired = new List<string>();
        foreach (var pair in _byName)
        {
            if (seen.Contains(pair.Key)) continue;
            retired.Add(pair.Key);
            if (pair.Value.IsVisible)
                pair.Value.RunGuarded(pair.Value.HideSwitcher);
            if (!_spare.Contains(pair.Value))
                _spare.Add(pair.Value);
        }
        foreach (var key in retired)
            _byName.Remove(key);

        _anchorIndex = AnchorIndex(settings.Placement, monitors, activeWindow);
        if (_anchorIndex < 0 || _anchorIndex >= _shown.Count) _anchorIndex = 0;
        _keyboard = _shown[_anchorIndex];
        _click = null;
        if (captureForeground)
            _previousForeground = NativeMethods.GetForegroundWindow();
        foreach (var window in _shown)
            window.RememberForeground(_previousForeground);
    }

    public void Render(SwitcherViewState state)
    {
        var targets = _shown.Count > 0 ? _shown : All().ToList();
        foreach (var window in targets)
            window.Render(state);
    }

    public void Reveal()
    {
        if (_shown.Count == 0) return;
        foreach (var window in _shown)
            window.HoldActivation();
        try
        {
            for (var i = 0; i < _shown.Count; i++)
            {
                if (i == _anchorIndex) continue;
                _shown[i].Place(_size, activate: false);
            }
            _shown[_anchorIndex].Place(_size, activate: true);
            _keyboard = _shown[_anchorIndex];
        }
        finally
        {
            foreach (var window in _shown)
                window.ReleaseActivation();
        }
    }

    public void HideAll()
    {
        var windows = All().ToList();
        foreach (var window in windows)
            window.PushGuard();
        try
        {
            foreach (var window in windows)
            {
                if (window.IsVisible) window.HideSwitcher();
            }
        }
        finally
        {
            foreach (var window in windows)
                window.PopGuard();
        }
        _click = null;
    }

    public void ForceClose()
    {
        foreach (var window in All().ToList())
            window.ForceClose();
    }

    public void RunGuarded(Action action)
    {
        var windows = All().ToList();
        foreach (var window in windows)
            window.PushGuard();
        try
        {
            action();
        }
        finally
        {
            foreach (var window in windows)
                window.PopGuard();
        }
    }

    public void FocusSearch() => Keyboard()?.FocusSearch();

    public void ClaimKeyboard()
    {
        var window = Keyboard();
        if (window is null) return;
        HoldAll();
        try
        {
            window.ClaimKeyboard();
        }
        finally
        {
            ReleaseAll();
        }
        window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var current = Keyboard();
            if (current is null || !current.IsVisible || !current.IsSearchMode) return;
            HoldAll();
            try
            {
                current.ClaimKeyboard();
            }
            finally
            {
                ReleaseAll();
            }
        });
    }

    public void ActivateFocused()
    {
        var window = Keyboard();
        if (window is null) return;
        HoldAll();
        try
        {
            window.Activate();
        }
        finally
        {
            ReleaseAll();
        }
    }

    void HoldAll()
    {
        foreach (var window in All())
            window.HoldActivation();
    }

    void ReleaseAll()
    {
        foreach (var window in All())
            window.ReleaseActivation();
    }

    public void NoteClick(SwitcherWindow window)
    {
        _keyboard = window;
        _click = window.Host;
    }

    public void NoteKeyboard(SwitcherWindow window) => _keyboard = window;

    public void ClearClick() => _click = null;

    public DisplayBounds? TakeMoveTarget()
    {
        var target = _click ?? CursorMonitor();
        _click = null;
        return target;
    }

    public int HostMonitorNumber()
    {
        var window = Keyboard();
        if (window is not null && window.Host.Number > 0)
            return window.Host.Number;
        return CursorMonitor()?.Number ?? 0;
    }

    public bool Owns(nint hwnd)
    {
        if (hwnd == 0) return false;
        foreach (var window in All())
        {
            if (window.Handle == hwnd) return true;
        }
        return false;
    }

    SwitcherWindow EnsureDispatcher()
    {
        if (_dispatcherWindow is not null) return _dispatcherWindow;
        _dispatcherWindow = new SwitcherWindow();
        _bind?.Invoke(_dispatcherWindow);
        _spare.Add(_dispatcherWindow);
        return _dispatcherWindow;
    }

    SwitcherWindow Take(string name)
    {
        if (_byName.TryGetValue(name, out var existing))
            return existing;
        SwitcherWindow window;
        if (_spare.Count > 0)
        {
            window = _spare[^1];
            _spare.RemoveAt(_spare.Count - 1);
        }
        else
        {
            window = new SwitcherWindow();
            _bind?.Invoke(window);
        }
        _byName[name] = window;
        return window;
    }

    IEnumerable<SwitcherWindow> All()
    {
        foreach (var window in _byName.Values)
            yield return window;
        foreach (var window in _spare)
        {
            if (!_byName.ContainsValue(window))
                yield return window;
        }
    }

    int VisibleQuery()
    {
        foreach (var window in _shown)
        {
            if (window.IsVisible) return window.QueryCharCount;
        }
        return 0;
    }

    SwitcherWindow? Keyboard() =>
        _keyboard is { IsVisible: true } ? _keyboard : _shown.FirstOrDefault(window => window.IsVisible) ?? _keyboard;

    void OnDisplaysChanged(object? sender, EventArgs e)
    {
        var window = _dispatcherWindow;
        if (window is null) return;
        window.Dispatcher.BeginInvoke(() => DisplaysChanged?.Invoke());
    }

    static int AnchorIndex(SwitcherPlacement placement, IReadOnlyList<DisplayBounds> monitors, nint activeWindow)
    {
        if (placement == SwitcherPlacement.ActiveWindow && activeWindow != 0)
        {
            var monitor = NativeMethods.MonitorFromWindow(activeWindow, NativeMethods.MonitorDefaultToNearest);
            var index = IndexOfHandle(monitors, monitor);
            if (index >= 0) return index;
        }
        if (NativeMethods.GetCursorPos(out var point))
        {
            var cursor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
            var index = IndexOfHandle(monitors, cursor);
            if (index >= 0) return index;
            index = MonitorLayout.HostIndex(monitors, point.X, point.Y, 1, 1);
            if (index >= 0) return index;
        }
        return 0;
    }

    static int IndexOfHandle(IReadOnlyList<DisplayBounds> monitors, nint handle)
    {
        if (handle == 0) return -1;
        var info = new MonitorInfo { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(handle, ref info)) return -1;
        var width = Math.Max(1, info.rcWork.Right - info.rcWork.Left);
        var height = Math.Max(1, info.rcWork.Bottom - info.rcWork.Top);
        return MonitorLayout.HostIndex(monitors, info.rcWork.Left, info.rcWork.Top, width, height);
    }

    static DisplayBounds? CursorMonitor()
    {
        var monitors = MonitorUtil.All();
        if (monitors.Count == 0) return null;
        if (!NativeMethods.GetCursorPos(out var point)) return monitors[0];
        var index = MonitorLayout.HostIndex(monitors, point.X, point.Y, 1, 1);
        if (index < 0 || index >= monitors.Count) return monitors[0];
        return monitors[index];
    }

    static string MonitorKey(DisplayBounds monitor, int index) =>
        string.IsNullOrEmpty(monitor.Name) ? "monitor-" + index : monitor.Name;

    static DisplayBounds Fallback() => new(0, 0, 1280, 800, 96, 96, 1, "DISPLAY1");
}
