using System.Windows.Threading;
using AltPane.Core.Actions;
using AltPane.Core.Input;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Switcher;

namespace AltPane;

internal sealed class SwitcherPresenter
{
    readonly Func<AppSettings> _settings;
    readonly SwitcherWindow _window;
    readonly ThumbnailController _thumbs = new();
    readonly Dictionary<long, System.Windows.Media.ImageSource> _icons = new();
    readonly DispatcherTimer _statusTimer;
    SwitcherSession? _session;
    nint _restore;
    string? _status;
    int _iconGeneration;
    bool _searchMode;

    public SwitcherPresenter(Func<AppSettings> settings)
    {
        _settings = settings;
        _window = new SwitcherWindow();
        _window.Hovered += handle =>
        {
            if (_session?.SelectHandle(handle) == true)
                Refresh(updateThumb: true);
        };
        _window.Invoked += handle =>
        {
            _session?.SelectHandle(handle);
            Hotkeys?.RequestCommit();
        };
        _window.CommitRequested += () => Hotkeys?.RequestCommit();
        _window.CancelRequested += () => Hotkeys?.RequestCancel();
        _window.SearchRequested += () => Hotkeys?.RequestSearchMode();
        _window.QueryEdited += text =>
        {
            _session?.SetQuery(text);
            Hotkeys?.SyncQueryLength(text?.Length ?? 0);
            Refresh(updateThumb: true);
        };
        _window.NavigateRequested += Nudge;
        _window.CloseRequested += CloseSelected;
        _window.KillRequested += KillSelected;
        _window.MinimizeRequested += () => RunAction(static hwnd => WindowActions.Minimize(hwnd), iconic: true, zoomed: false);
        _window.MaximizeRequested += () => RunAction(static hwnd => WindowActions.Maximize(hwnd), iconic: false, zoomed: true);
        _window.RestoreRequested += () => RunAction(static hwnd => WindowActions.Restore(hwnd), iconic: false, zoomed: false);
        _window.LayoutChanged += () => UpdateThumb();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _status = null;
            if (_window.IsVisible) Refresh(updateThumb: false);
        };
    }

    public HotkeyService? Hotkeys { get; set; }
    public bool PendingShow { get; private set; }
    public event Action<string>? Notify;

    public void Apply(UiOp op)
    {
        switch (op)
        {
            case UiOp.Open open:
                Build(open.Delta);
                break;
            case UiOp.Cycle cycle:
                _session?.Move(cycle.Delta);
                if (_window.IsVisible) Refresh(updateThumb: true);
                break;
            case UiOp.AppendQuery append:
                _session?.AppendQuery(append.Text);
                if (_window.IsVisible) Refresh(updateThumb: true);
                break;
            case UiOp.Backspace:
                _session?.Backspace();
                if (_window.IsVisible) Refresh(updateThumb: true);
                break;
            case UiOp.Home:
                _session?.SelectFirst();
                if (_window.IsVisible) Refresh(updateThumb: true);
                break;
            case UiOp.End:
                _session?.SelectLast();
                if (_window.IsVisible) Refresh(updateThumb: true);
                break;
            case UiOp.Page page:
                _session?.Page(page.Direction);
                if (_window.IsVisible) Refresh(updateThumb: true);
                break;
            case UiOp.Commit:
                Commit();
                break;
            case UiOp.Cancel:
                Cancel();
                break;
            case UiOp.EnterSearch:
                _searchMode = true;
                if (!_window.IsVisible) PendingShow = true;
                if (_window.IsVisible)
                {
                    Refresh(updateThumb: false);
                    _window.FocusSearch();
                }
                break;
            case UiOp.CloseWindow:
                CloseSelected();
                break;
            case UiOp.KillProcess:
                KillSelected();
                break;
            case UiOp.Minimize:
                RunAction(static hwnd => WindowActions.Minimize(hwnd), iconic: true, zoomed: false);
                break;
            case UiOp.Maximize:
                RunAction(static hwnd => WindowActions.Maximize(hwnd), iconic: false, zoomed: true);
                break;
            case UiOp.Restore:
                RunAction(static hwnd => WindowActions.Restore(hwnd), iconic: false, zoomed: false);
                break;
            case UiOp.BringToFront:
                if (_session is null) Build(0);
                else if (_window.IsVisible) _window.Activate();
                else PendingShow = true;
                break;
        }
    }

    public void FinishFrame(GestureSnapshot snap)
    {
        if (!PendingShow || !snap.SwitcherOpen) return;
        PendingShow = false;
        _searchMode = snap.SearchMode;
        _window.Render(BuildState());
        _window.ShowFor(_settings(), _restore);
        if (snap.SearchMode) _window.FocusSearch();
        QueueIcons();
        UpdateThumb();
    }

    public WindowEntry? TakeForQuickCommit()
    {
        var selected = _session?.Selected;
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _thumbs.Clear();
        return selected;
    }

    public void ActivateEntry(WindowEntry entry)
    {
        if (!NativeMethods.IsWindow((nint)entry.Handle))
        {
            Notify?.Invoke(Localizer.Current["activate.missing"]);
            return;
        }
        if (!WindowActions.TryActivate((nint)entry.Handle))
            Notify?.Invoke(Localizer.Current["activate.failed"]);
    }

    public void ForceClose()
    {
        _thumbs.Clear();
        _window.ForceClose();
    }

    void Build(int delta)
    {
        _restore = NativeMethods.GetForegroundWindow();
        List<WindowEntry> entries;
        try
        {
            entries = WindowEnumerator.Enumerate(_settings());
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            entries = new List<WindowEntry>();
        }

        _session = new SwitcherSession(entries);
        if (delta != 0) _session.Move(delta);
        _searchMode = false;
        _status = null;
        _icons.Clear();
        foreach (var entry in entries)
            _icons[entry.Handle] = IconLoader.Letter(entry.Title);
        PendingShow = true;
        _iconGeneration++;
    }

    void Commit()
    {
        var selected = _session?.Selected;
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _thumbs.Clear();
        _window.RunGuarded(() =>
        {
            if (_window.IsVisible) _window.HideSwitcher();
        });
        if (selected is null) return;
        ActivateEntry(selected);
    }

    void Cancel()
    {
        var restore = _restore;
        var wasVisible = _window.IsVisible;
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _thumbs.Clear();
        _window.RunGuarded(() =>
        {
            if (wasVisible) _window.HideSwitcher();
        });
        if (wasVisible && restore != 0)
            WindowActions.TryActivate(restore);
    }

    void Nudge(int delta)
    {
        if (_session is null) return;
        if (delta == int.MinValue) _session.SelectFirst();
        else if (delta == int.MaxValue) _session.SelectLast();
        else if (Math.Abs(delta) >= SwitcherSession.PageSize) _session.Page(Math.Sign(delta));
        else _session.Move(delta);
        if (_window.IsVisible) Refresh(updateThumb: true);
    }

    void CloseSelected()
    {
        var session = _session;
        var selected = session?.Selected;
        if (session is null || selected is null) return;
        var hwnd = (nint)selected.Handle;
        if (!NativeMethods.IsWindow(hwnd))
        {
            session.RemoveHandle(selected.Handle);
            ShowStatus(Localizer.Current["close.gone"]);
            return;
        }
        WindowActions.RequestClose(hwnd);
        ShowStatus(Localizer.Current["close.sent"]);
        SchedulePrune(selected.Handle);
    }

    void KillSelected()
    {
        var selected = _session?.Selected;
        if (selected is null) return;
        var decision = ProcessActionPolicy.Evaluate(selected.KillProcessName, selected.KillProcessId);
        if (!decision.Allowed)
        {
            ShowStatus(Localizer.Current[decision.ReasonKey]);
            return;
        }

        var result = WindowActions.Kill(selected.KillProcessId);
        ShowStatus(Localizer.Current[result switch
        {
            KillAttempt.Success => "kill.done",
            KillAttempt.AccessDenied => "kill.denied",
            _ => "kill.failed"
        }]);
        if (result == KillAttempt.Success)
            SchedulePrune(selected.Handle);
    }

    void RunAction(Action<nint> action, bool iconic, bool zoomed)
    {
        var selected = _session?.Selected;
        if (selected is null || !NativeMethods.IsWindow((nint)selected.Handle)) return;
        action((nint)selected.Handle);
        selected.IsIconic = iconic;
        selected.IsZoomed = zoomed;
        Refresh(updateThumb: true);
    }

    void SchedulePrune(long handle)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_session is null) return;
            if (!NativeMethods.IsWindow((nint)handle))
                _session.RemoveHandle(handle);
            if (_window.IsVisible) Refresh(updateThumb: true);
        };
        timer.Start();
    }

    void ShowStatus(string text)
    {
        _status = text;
        _statusTimer.Stop();
        _statusTimer.Start();
        if (_window.IsVisible) Refresh(updateThumb: false);
    }

    void Refresh(bool updateThumb)
    {
        _window.Render(BuildState());
        if (updateThumb && _window.IsVisible) UpdateThumb();
    }

    SwitcherViewState BuildState()
    {
        var settings = _settings();
        var filtered = _session?.Filtered ?? Array.Empty<WindowEntry>();
        var allCount = _session?.All.Count ?? 0;
        var loc = Localizer.Current;
        var count = _session is null
            ? ""
            : _session.Query.Length == 0
                ? loc.Format("count.windows", filtered.Count)
                : loc.Format("count.filtered", filtered.Count, allCount);
        var trigger = HotkeyLogic.TriggerDisplayName(HotkeyLogic.TriggerModifier(settings.Hotkey ?? HotkeyChord.AltTab()));
        var hint = _searchMode ? loc["hint.sticky"] : loc.Format("hint.gesture", trigger);
        return new SwitcherViewState
        {
            Filtered = filtered,
            SelectedIndex = _session?.SelectedIndex ?? -1,
            Query = _session?.Query ?? "",
            Hint = hint,
            CountText = count,
            Status = _status,
            ShowPreview = settings.ShowPreview,
            SearchMode = _searchMode,
            Icons = _icons,
            EmptyText = _session is null || allCount == 0
                ? loc["list.empty"]
                : filtered.Count == 0 ? loc["list.nomatch"] : ""
        };
    }

    void QueueIcons()
    {
        var generation = ++_iconGeneration;
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (generation != _iconGeneration || _session is null) return;
            foreach (var entry in _session.All.ToList())
            {
                if (generation != _iconGeneration) return;
                try
                {
                    var icon = IconLoader.TryGetIcon(entry);
                    if (icon is not null) _icons[entry.Handle] = icon;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex);
                }
            }
            if (_window.IsVisible) Refresh(updateThumb: false);
        }, DispatcherPriority.Background);
    }

    void UpdateThumb()
    {
        var settings = _settings();
        var selected = _session?.Selected;
        if (!settings.ShowPreview || selected is null || !_window.IsVisible || selected.IsIconic)
        {
            _thumbs.Clear();
            _window.SetPreviewMessage(selected is null
                ? Localizer.Current["preview.none"]
                : selected.IsIconic
                    ? Localizer.Current["preview.minimized"]
                    : "");
            return;
        }

        if (!_window.TryGetThumbBounds(out var bounds))
        {
            _thumbs.Clear();
            return;
        }

        var hr = _thumbs.Show(_window.Handle, (nint)selected.Handle, bounds);
        if (hr == 0)
        {
            _window.SetPreviewMessage("");
            return;
        }

        _thumbs.Clear();
        var denied = unchecked((int)hr) == unchecked((int)0x80070005);
        _window.SetPreviewMessage(Localizer.Current[denied ? "preview.denied" : "preview.unavailable"]);
    }
}
