using System.Windows.Threading;
using AltPane.Core.Layout;
using AltPane.Core.Localization;
using Microsoft.Win32;

namespace AltPane;

/// <summary>
/// One non-activating reminder per monitor that is not hosting the switcher.
/// </summary>
internal sealed class HintOverlay : IDisposable
{
    readonly List<HintWindow> _windows = new();
    bool _disposed;

    public HintOverlay()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplay;
        ThemeService.Changed += OnTheme;
    }

    public event Action? DisplayChanged;

    public bool IsOurWindow(nint hwnd)
    {
        foreach (var window in _windows)
        {
            if (window.IsVisible && window.Handle == hwnd) return true;
        }
        return false;
    }

    public void Show(IReadOnlyList<HintPlan> plans, IReadOnlyList<DisplayBounds> monitors, Action<int> moveTo)
    {
        if (_disposed) return;
        var loc = Localizer.Current;
        var move = loc["hint.move"];
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            if (plan.HintMonitorIndex < 0 || plan.HintMonitorIndex >= monitors.Count) continue;
            var window = WindowAt(i);
            var title = plan.HostNumber > 0
                ? loc.Format("hint.onDisplay", plan.HostNumber)
                : loc["hint.other"];
            var target = plan.HintMonitorIndex;
            window.Clicked = () => moveTo(target);
            window.Place(plan, monitors[plan.HintMonitorIndex], title, move);
        }

        for (var i = plans.Count; i < _windows.Count; i++)
        {
            if (_windows[i].IsVisible) _windows[i].Hide();
        }
    }

    public void Hide()
    {
        foreach (var window in _windows)
        {
            if (window.IsVisible) window.Hide();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { SystemEvents.DisplaySettingsChanged -= OnDisplay; } catch { }
        ThemeService.Changed -= OnTheme;
        foreach (var window in _windows)
        {
            try { window.Close(); } catch { }
        }
        _windows.Clear();
    }

    HintWindow WindowAt(int index)
    {
        while (_windows.Count <= index)
            _windows.Add(new HintWindow());
        return _windows[index];
    }

    void OnDisplay(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        var handler = DisplayChanged;
        if (dispatcher is null || handler is null) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, handler);
    }

    void OnTheme(bool dark)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            foreach (var window in _windows)
                window.ApplyChrome(dark);
        });
    }
}
