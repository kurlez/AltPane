using System.Windows.Threading;
using AltPane.Core.Actions;
using AltPane.Core.Apps;
using AltPane.Core.Input;
using AltPane.Core.Load;
using AltPane.Core.Layout;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Switcher;

namespace AltPane;

internal sealed class SwitcherPresenter
{
    readonly Func<AppSettings> _settings;
    readonly SwitcherDeck _deck;
    readonly Dictionary<long, System.Windows.Media.ImageSource> _icons = new();
    readonly Dictionary<string, System.Windows.Media.ImageSource> _appIcons = new(StringComparer.OrdinalIgnoreCase);
    AppIndexService? _apps;
    readonly DispatcherTimer _statusTimer;
    readonly LoadSampler _sampler = new();
    readonly KillArm _arm = new();
    readonly ActivationMru _activations = new();
    readonly WindowListCache _windows;
    int _displayCount = -1;
    DispatcherTimer? _prune;
    SwitcherSession? _session;
    nint _restore;
    string? _status;
    int _iconGeneration;
    long _enumerationMs;
    bool _warmed;
    bool _suspendOpen;
    UiOp.Open? _deferredOpen;
    GestureSnapshot _deferredSnap;
    bool _deferredFinish;
    bool _searchMode;
    bool _statusSticky;
    SystemLoad _system;
    readonly HoverGate _hover = new();
    readonly WheelNavigator _wheel = new();
    bool _hoverHighlight;

    public SwitcherPresenter(Func<AppSettings> settings)
    {
        _settings = settings;
        _deck = new SwitcherDeck();
        _deck.Attach(Bind);
        _deck.DisplaysChanged += OnDisplaysChanged;
        _windows = new WindowListCache(_activations, _settings);
        _sampler.Updated += update => _deck.Dispatcher.BeginInvoke(() => ApplyLoad(update));
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            if (_statusSticky) return;
            _status = null;
            if (_deck.IsVisible) Refresh();
        };
    }

    public HotkeyService? Hotkeys { get; set; }
    public ActivationMru Activations => _activations;

    public void StartWindowCache() => _windows.Start();

    public void InvalidateWindowCache() => _windows.Invalidate();

    public void DisposeWindowCache() => _windows.Dispose();

    /// <summary>
    /// Build one hidden switcher per monitor before the first hotkey.
    /// A later open calls Show and SetWindowPos on these windows.
    /// Prepare rebinds each monitor's DPI, including after a DPI change.
    /// The window objects themselves are replaced only when the monitor set changes.
    /// </summary>
    public void CreateWindows()
    {
        _deck.Prepare(_settings(), 0, captureForeground: false);
        _deck.EnsureCreated();
    }
    public bool PendingShow { get; private set; }
    public bool IsSwitcherVisible => _deck.IsVisible;
    public bool IsImeComposing => _deck.IsImeComposing;
    public int VisibleQueryLength => _deck.QueryCharCount;
    public bool VisibleSearchMode => _deck.IsSearchMode;
    public bool EscapeBelongsToIme() => _deck.EscapeBelongsToIme();

    void Bind(SwitcherWindow window)
    {
        window.IsCompanion = _deck.Owns;
        window.Hovered += handle =>
        {
            if (_session?.SelectHandle(handle) == true)
                Refresh();
        };
        window.Invoked += handle =>
        {
            _deck.NoteClick(window);
            _session?.SelectHandle(handle);
            Hotkeys?.RequestCommit();
        };
        window.AppHovered += id =>
        {
            if (_session?.SelectApp(id) == true)
                Refresh();
        };
        window.AppInvoked += id =>
        {
            _deck.NoteClick(window);
            _session?.SelectApp(id);
            Hotkeys?.RequestCommit();
        };
        window.AdminLaunchRequested += () =>
        {
            if (window.TakeMouse()) _deck.NoteClick(window);
            else _deck.ClearClick();
            LaunchAdmin();
        };
        window.OpenLocationRequested += RevealSelected;
        window.CommitRequested += () =>
        {
            if (window.TakeMouse()) _deck.NoteClick(window);
            else _deck.ClearClick();
            Hotkeys?.RequestCommit();
        };
        window.CommitInPlaceRequested += () =>
        {
            window.TakeMouse();
            _deck.ClearClick();
            Hotkeys?.RequestCommit(moveChoice: 0);
        };
        window.ForceMoveRequested += () =>
        {
            if (window.TakeMouse()) _deck.NoteClick(window);
            Hotkeys?.RequestCommit(moveChoice: 1);
        };
        window.ClearQueryRequested += () => Hotkeys?.RequestClearQuery();
        window.CancelRequested += () => Hotkeys?.RequestCancel();
        window.Releasing += () => ReleaseLiveSurface();
        window.SearchRequested += () =>
        {
            _deck.NoteKeyboard(window);
            Hotkeys?.RequestSearchMode();
        };
        window.QueryEdited += text =>
        {
            _deck.NoteKeyboard(window);
            NoteNavigation();
            _session?.SetQuery(text);
            Hotkeys?.SyncQueryLength(text?.Length ?? 0);
            SyncSearch();
            Refresh();
        };
        window.AllowHover = AllowHover;
        window.WheelRequested += OnWheel;
        window.NavigateRequested += Nudge;
        window.SpaceRequested += OnSpace;
        window.CloseRequested += CloseSelected;
        window.KillRequested += () => KillSelected(null);
        window.KillTreeRequested += () => KillSelected(true);
        window.SortCycleRequested += CycleSort;
        window.MonitorFilterRequested += ToggleMonitorFilter;
        window.PinRequested += () => SetAppPriority(AppPriorityLists.Pin);
        window.DemoteRequested += () => SetAppPriority(AppPriorityLists.Demote);
        window.RestorePriorityRequested += () => SetAppPriority(AppPriorityLists.Restore);
        window.LabelsRequested += () =>
        {
            if (_deck.IsVisible) Refresh();
        };
        window.MinimizeRequested += () => RunAction(static hwnd => WindowActions.Minimize(hwnd), iconic: true, zoomed: false);
        window.MaximizeRequested += () => RunAction(static hwnd => WindowActions.Maximize(hwnd), iconic: false, zoomed: true);
        window.RestoreRequested += () => RunAction(static hwnd => WindowActions.Restore(hwnd), iconic: false, zoomed: false);
    }

    public void UseApps(AppIndexService apps)
    {
        _apps = apps;
        apps.Updated += () => _deck.Dispatcher.BeginInvoke(ReloadApps);
    }
    public event Action<string>? Notify;
    public event Action? PersistRequested;

    public void Apply(UiOp op)
    {
        if (_suspendOpen && op is UiOp.Open deferred)
        {
            _deferredOpen = deferred;
            return;
        }

        switch (op)
        {
            case UiOp.Open open:
                Build(open.Delta);
                break;
            case UiOp.Cycle cycle:
                NoteNavigation();
                _session?.Move(cycle.Delta);
                if (_deck.IsVisible) Refresh();
                break;
            case UiOp.AppendQuery append:
                NoteNavigation();
                _session?.AppendQuery(append.Text);
                SyncSearch();
                if (_deck.IsVisible) Refresh();
                break;
            case UiOp.Backspace:
                NoteNavigation();
                _session?.Backspace();
                SyncSearch();
                if (_deck.IsVisible) Refresh();
                break;
            case UiOp.ClearQuery:
                if (_session?.ClearQuery() == true)
                {
                    NoteNavigation();
                    Hotkeys?.SyncQueryLength(0);
                    SyncSearch();
                    if (_deck.IsVisible)
                    {
                        Refresh();
                        if (_searchMode) _deck.FocusSearch();
                    }
                }
                break;
            case UiOp.Home:
                NoteNavigation();
                _session?.SelectFirst();
                if (_deck.IsVisible) Refresh();
                break;
            case UiOp.End:
                NoteNavigation();
                _session?.SelectLast();
                if (_deck.IsVisible) Refresh();
                break;
            case UiOp.Page page:
                NoteNavigation();
                _session?.Page(page.Direction);
                if (_deck.IsVisible) Refresh();
                break;
            case UiOp.Commit commit:
                Commit(commit.MoveToMonitor);
                break;
            case UiOp.Cancel:
                Cancel();
                break;
            case UiOp.EnterSearch:
            case UiOp.ReleaseHold:
                _searchMode = true;
                if (!_deck.IsVisible) PendingShow = true;
                else
                {
                    Refresh();
                    _deck.ClaimKeyboard();
                }
                break;
            case UiOp.CloseWindow:
                CloseSelected();
                break;
            case UiOp.KillProcess:
                KillSelected(null);
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
            case UiOp.SpaceAction:
                OnSpace();
                break;
            case UiOp.PinApp:
                SetAppPriority(AppPriorityLists.Pin);
                break;
            case UiOp.DemoteApp:
                SetAppPriority(AppPriorityLists.Demote);
                break;
            case UiOp.RestorePriority:
                SetAppPriority(AppPriorityLists.Restore);
                break;
            case UiOp.AdminLaunch:
                LaunchAdmin();
                break;
            case UiOp.BringToFront:
                if (_session is null) Build(0);
                else if (_deck.IsVisible) _deck.ActivateFocused();
                else PendingShow = true;
                break;
        }
    }

    public void FinishFrame(GestureSnapshot snap)
    {
        if (_suspendOpen)
        {
            _deferredSnap = snap;
            _deferredFinish = true;
            return;
        }

        if (!PendingShow || !snap.SwitcherOpen)
        {
            OpenTiming.Clear();
            return;
        }

        PendingShow = false;
        _searchMode = snap.SearchMode;
        var settings = _settings();
        _deck.Prepare(settings, _restore);
        NoteOpened();
        _deck.Render(BuildState());
        _deck.Reveal();
        var rows = _session?.All.Count ?? 0;
        if (OpenTiming.TryConsume(out var visibleMs))
            AppLog.Info($"open hotkey→visible {visibleMs} ms, enumeration {_enumerationMs} ms, rows {rows}");
        if (snap.SearchMode) _deck.ClaimKeyboard();
        QueueIcons();
        SyncSampler();
    }

    public WindowEntry? TakeForQuickCommit()
    {
        OpenTiming.Clear();
        var selected = _session?.Selected;
        ReleaseLiveSurface();
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _arm.Clear();
        _deck.HideAll();
        return selected;
    }

    public void ActivateEntry(WindowEntry entry) => ActivateEntry(entry, null);

    void ActivateEntry(WindowEntry entry, DisplayBounds? moveTo)
    {
        if (!NativeMethods.IsWindow((nint)entry.Handle))
        {
            Notify?.Invoke(Localizer.Current["activate.missing"]);
            return;
        }
        if (moveTo is { } host
            && !WindowMover.TryMoveTo((nint)entry.Handle, host, MonitorUtil.All()))
            AppLog.Info($"Could not move 0x{entry.Handle:X} onto the switcher's display. Activating it where it is.");
        _windows.ArmActivation(entry.Handle);
        if (!WindowActions.TryActivate((nint)entry.Handle))
            Notify?.Invoke(Localizer.Current["activate.failed"]);
    }

    public void ForceClose()
    {
        ReleaseLiveSurface();
        _deck.ForceClose();
    }

    void Build(int delta, bool captureForeground = true)
    {
        ReleaseLiveSurface();
        if (captureForeground)
            _restore = NativeMethods.GetForegroundWindow();
        List<WindowEntry> entries;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var captured = _windows.Capture(_settings());
            entries = captured.Entries;
            _enumerationMs = captured.EnumerationMilliseconds;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            entries = new List<WindowEntry>();
            _enumerationMs = clock.ElapsedMilliseconds;
        }

        _session = new SwitcherSession(entries);
        var priority = _windows.Priority(_settings());
        _session.SetOrderContext((long)_restore, Activations.Rank, priority.Tier, priority.IsShort);
        _session.SetSortMode(_settings().SortMode);
        var opening = StableGroupedOrder.OpeningIndex(_session.SortMode, _session.ResultCount, delta);
        if (opening is int index)
        {
            if (index <= 0) _session.SelectFirst();
            else _session.SelectLast();
        }
        else if (delta != 0)
            _session.Move(delta);
        ApplyCatalog(resetClock: true);
        _searchMode = false;
        _status = null;
        _arm.Clear();
        _statusSticky = false;
        var live = new HashSet<long>(entries.Count);
        foreach (var entry in entries)
        {
            live.Add(entry.Handle);
            if (_icons.ContainsKey(entry.Handle)) continue;
            var cached = IconLoader.Cached(entry.IconPath, 0);
            _icons[entry.Handle] = cached ?? IconLoader.Letter(entry.Title);
        }
        if (_icons.Count != live.Count)
        {
            var stale = new List<long>();
            foreach (var handle in _icons.Keys)
            {
                if (!live.Contains(handle)) stale.Add(handle);
            }
            foreach (var handle in stale) _icons.Remove(handle);
        }
        _displayCount = -1;
        PendingShow = true;
        _iconGeneration++;
    }

    /// <summary>
    /// Exercise Show, layout, and icon decode once while the process is idle.
    /// The window is placed off-screen and does not take focus. A real Alt+Tab
    /// that arrives while this is showing is replayed afterwards.
    /// </summary>
    public void Warm()
    {
        if (_warmed) return;
        _warmed = true;
        if (_deck.IsVisible || PendingShow || _session is not null)
        {
            AppLog.Info("warmup skipped because the switcher is already open");
            return;
        }

        _suspendOpen = true;
        var savedRestore = _restore;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var rows = 0;
        try
        {
            Build(0, captureForeground: false);
            rows = _session?.All.Count ?? 0;
            _deck.Prepare(_settings(), 0, captureForeground: false);
            if (_session is not null)
                _deck.Render(BuildState());
            _deck.RevealOffscreen();
            QueueIcons();
            _deck.HideAll();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        finally
        {
            ReleaseLiveSurface();
            _session = null;
            PendingShow = false;
            _searchMode = false;
            _status = null;
            _arm.Clear();
            _restore = savedRestore;
            _suspendOpen = false;
        }

        AppLog.Info($"warmup off-screen {clock.ElapsedMilliseconds} ms, rows {rows}");
        var open = _deferredOpen;
        var finish = _deferredFinish;
        var snap = _deferredSnap;
        _deferredOpen = null;
        _deferredFinish = false;
        if (open is not null)
            Build(open.Delta);
        if (finish)
            FinishFrame(snap);
    }

    void ReleaseLiveSurface()
    {
        _sampler.Stop();
        _apps?.CancelSearch();
        _prune?.Stop();
    }

    void DismissSwitcher()
    {
        OpenTiming.Clear();
        ReleaseLiveSurface();
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _sampler.Stop();
        _arm.Clear();
        _deck.HideAll();
    }

    void LaunchAdmin()
    {
        var hit = _session?.SelectedApp;
        if (hit is not { } appHit)
            return;
        if (appHit.SwitchHandle != 0)
        {
            Commit(null);
            return;
        }
        var app = appHit.App;
        var host = MoveDestination(null);
        DismissSwitcher();
        if (!AppLauncher.TryLaunch(app, admin: true, out var error) && error is not null)
            Notify?.Invoke(Localizer.Current.Format(error, app.Name));
        else
        {
            _apps?.Remember(app.Id);
            if (host is { } bounds) WatchLaunch(app, bounds);
        }
    }

    void RevealSelected()
    {
        var hit = _session?.SelectedApp;
        if (hit is not { } appHit) return;
        if (!AppLauncher.TryReveal(appHit.App, out var error) && error is not null)
            ShowStatus(Localizer.Current[error]);
    }

    void ReloadApps()
    {
        _windows.RestampNames();
        if (_session is null) return;
        foreach (var entry in _session.All)
        {
            var process = string.IsNullOrWhiteSpace(entry.HostedProcessName) ? entry.ProcessName : entry.HostedProcessName;
            var name = AppDisplayNames.Resolve(process, entry.IconPath);
            if (!string.Equals(entry.DisplayProcessName, name, StringComparison.Ordinal))
                entry.DisplayProcessName = name;
        }
        ApplyCatalog(resetClock: false);
        if (_deck.IsVisible) Refresh();
    }

    void ApplyCatalog(bool resetClock)
    {
        if (_session is null) return;
        var settings = _settings();
        _session.SetCatalog(
            _apps?.ForQuery(_session.Query),
            _apps?.UsageSnapshot(),
            settings.ExcludedApps,
            settings.LaunchApps,
            resetClock ? DateTime.UtcNow : null);
    }

    void Commit(bool? moveOverride)
    {
        var hit = _session?.SelectedApp;
        if (hit is { } appHit)
        {
            var app = appHit.App;
            var window = appHit.SwitchHandle == 0
                ? null
                : _session?.All.FirstOrDefault(entry => entry.Handle == appHit.SwitchHandle);
            var host = MoveDestination(moveOverride);
            DismissSwitcher();
            if (window is not null)
            {
                _apps?.Remember(app.Id);
                ActivateEntry(window, host);
                return;
            }
            if (!AppLauncher.TryLaunch(app, admin: false, out var error) && error is not null)
                Notify?.Invoke(Localizer.Current.Format(error, app.Name));
            else
            {
                _apps?.Remember(app.Id);
                if (host is { } bounds) WatchLaunch(app, bounds);
            }
            return;
        }

        var selected = _session?.Selected;
        var destination = MoveDestination(moveOverride);
        ReleaseLiveSurface();
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _arm.Clear();
        _deck.HideAll();
        if (selected is null) return;
        ActivateEntry(selected, destination);
    }

    bool WantsMove(bool? moveOverride) => moveOverride ?? _settings().MoveToSwitcherMonitor;

    DisplayBounds? MoveDestination(bool? moveOverride) =>
        WantsMove(moveOverride) ? _deck.TakeMoveTarget() : null;

    void SyncSearch() => _apps?.ScheduleSearch(_session?.Query);

    void WatchLaunch(AppEntry app, DisplayBounds host)
    {
        if (string.IsNullOrWhiteSpace(app.ProcessName)) return;
        var seen = new HashSet<long>();
        try
        {
            foreach (var entry in WindowEnumerator.Enumerate(_settings()))
                seen.Add(entry.Handle);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return;
        }
        var name = app.ProcessName;
        var ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            if (++ticks > 15)
            {
                timer.Stop();
                return;
            }
            try
            {
                foreach (var entry in WindowEnumerator.Enumerate(_settings()))
                {
                    if (!seen.Add(entry.Handle)) continue;
                    if (!string.Equals(entry.ProcessName, name, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(entry.DisplayProcessName, name, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(entry.KillProcessName, name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    WindowMover.TryMoveTo((nint)entry.Handle, host, MonitorUtil.All());
                    timer.Stop();
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
                timer.Stop();
            }
        };
        timer.Start();
    }

    void Cancel()
    {
        OpenTiming.Clear();
        ReleaseLiveSurface();
        var restore = _restore;
        var wasVisible = _deck.IsVisible;
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _arm.Clear();
        if (wasVisible) _deck.HideAll();
        if (wasVisible && restore != 0)
            WindowActions.TryActivate(restore);
    }

    void Nudge(int delta)
    {
        if (_session is null) return;
        NoteNavigation();
        if (delta == int.MinValue) _session.SelectFirst();
        else if (delta == int.MaxValue) _session.SelectLast();
        else if (Math.Abs(delta) >= SwitcherSession.PageSize) _session.Page(Math.Sign(delta));
        else _session.Move(delta);
        DisarmIfSelectionChanged();
        if (_deck.IsVisible) Refresh();
    }

    /// <summary>
    /// One accumulator for every monitor. A partial touchpad delta still counts
    /// as selection input, so a scroll under a stationary cursor cannot reselect.
    /// </summary>
    void OnWheel(int delta)
    {
        var steps = _wheel.Consume(delta);
        if (steps == 0)
        {
            if (delta != 0) NoteNavigation();
            return;
        }
        if (_session is null) return;
        NoteNavigation();
        _session.Move(steps);
        DisarmIfSelectionChanged();
        if (_deck.IsVisible) Refresh();
    }

    bool AllowHover(double x, double y)
    {
        // GetCursorPos, not the event point. A scroll or redraw reports MouseMove
        // while the pointer is still, and the event point can disagree with the
        // cursor by a DPI scale.
        var (cx, cy) = CursorPoint();
        if (double.IsNaN(cx))
        {
            cx = x;
            cy = y;
        }
        var allow = _hover.NoteMouseMove(Environment.TickCount64, cx, cy);
        if (allow && !_hoverHighlight) ShowHover(true);
        return allow;
    }

    void NoteOpened()
    {
        _wheel.Reset();
        var (x, y) = CursorPoint();
        _hover.NoteOpened(x, y);
        ShowHover(false);
    }

    void NoteNavigation()
    {
        var (x, y) = CursorPoint();
        _hover.NoteNavigation(Environment.TickCount64, x, y);
        ShowHover(false);
    }

    void ShowHover(bool enabled)
    {
        _hoverHighlight = enabled;
        _deck.SetHoverHighlight(enabled);
    }

    static (double X, double Y) CursorPoint()
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return (double.NaN, double.NaN);
        return (point.X, point.Y);
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

    void KillSelected(bool? forceTree)
    {
        var selected = _session?.Selected;
        if (selected is null) return;
        var includeTree = forceTree ?? _settings().EndTaskKillsTree;
        var decision = ProcessActionPolicy.Evaluate(selected.KillProcessName, selected.KillProcessId);
        if (!decision.Allowed)
        {
            _arm.Clear();
            ShowStatus(Localizer.Current[decision.ReasonKey]);
            return;
        }

        if (!_arm.Ready(selected.KillProcessId, includeTree, _settings().ConfirmEndTask))
        {
            var key = includeTree ? "kill.confirmTree" : "kill.confirm";
            ShowStatus(Localizer.Current.Format(key, selected.DisplayProcessName), sticky: true);
            return;
        }

        IReadOnlyList<int> targets;
        try
        {
            var nodes = LoadSampler.CaptureProcesses()
                .Select(sample => new ProcessNode(sample.ProcessId, sample.ParentProcessId, sample.Name))
                .ToList();
            targets = ProcessTree.SelectTargets(selected.KillProcessId, selected.KillProcessName, nodes, includeTree);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            targets = includeTree ? Array.Empty<int>() : new[] { selected.KillProcessId };
        }

        if (targets.Count == 0)
        {
            ShowStatus(Localizer.Current["kill.failed"]);
            return;
        }

        var result = WindowActions.KillAll(targets);
        ShowStatus(Localizer.Current[result switch
        {
            KillAttempt.Success => "kill.done",
            KillAttempt.AccessDenied => "kill.denied",
            _ => "kill.failed"
        }]);
        if (result == KillAttempt.Success)
            SchedulePrune(selected.Handle);
    }

    void SetAppPriority(Action<AppSettings, string> apply)
    {
        var selected = _session?.Selected;
        if (selected is null) return;
        var key = AppIdentity.Key(selected);
        if (key.Length == 0) return;
        var settings = _settings();
        var pinnedBefore = AppPriorityLists.IsPinned(settings.PinnedApps, key);
        var demotedBefore = AppPriorityLists.IsDemoted(settings.PinnedApps, settings.DemotedApps, key);
        apply(settings, key);
        try { PersistRequested?.Invoke(); }
        catch (Exception ex) { AppLog.Error(ex); }
        var priority = _windows.Priority(settings);
        if (_session is null) return;
        _session.SetPriority(priority.Tier, priority.IsShort);
        var loc = Localizer.Current;
        var message = AppPriorityLists.IsPinned(settings.PinnedApps, key)
            ? loc["priority.pinned"]
            : AppPriorityLists.IsUserDemoted(settings.DemotedApps, key)
                ? loc["priority.demoted"]
                : loc["priority.restored"];
        if (pinnedBefore || demotedBefore || message.Length > 0)
            ShowStatus(message);
        if (_deck.IsVisible) Refresh();
    }

    void CycleSort()
    {
        var settings = _settings();
        settings.SortMode = settings.SortMode switch
        {
            LoadSortMode.FocusTime => LoadSortMode.Recent,
            LoadSortMode.Recent => LoadSortMode.Initial,
            LoadSortMode.Initial => LoadSortMode.Cpu,
            LoadSortMode.Cpu => LoadSortMode.Memory,
            _ => LoadSortMode.FocusTime
        };
        _session?.SetSortMode(settings.SortMode);
        try { PersistRequested?.Invoke(); }
        catch (Exception ex) { AppLog.Error(ex); }
        if (_deck.IsVisible) Refresh();
    }

    void ApplyLoad(LoadUpdate update)
    {
        if (_session is null || !_deck.IsVisible || !_settings().ShowLoad) return;
        _system = update.System;
        var byHandle = new Dictionary<long, TaskLoad>();
        foreach (var entry in _session.All)
        {
            if (update.ByProcessId.TryGetValue(entry.KillProcessId, out var load))
                byHandle[entry.Handle] = load;
        }
        _session.UpdateLoads(byHandle);
        DisarmIfSelectionChanged();
        Refresh();
    }

    void SyncSampler()
    {
        if (_session is null || !_deck.IsVisible || !_settings().ShowLoad)
        {
            _sampler.Stop();
            return;
        }
        _sampler.Watch(_session.All.Select(entry => entry.KillProcessId).ToArray());
    }

    void DisarmIfSelectionChanged()
    {
        var pid = _session?.Selected?.KillProcessId ?? 0;
        if (!_arm.IsArmed || pid == _arm.ProcessId) return;
        _arm.Clear();
        if (_statusSticky)
        {
            _statusSticky = false;
            _status = null;
        }
    }

    void RunAction(Action<nint> action, bool iconic, bool zoomed)
    {
        var selected = _session?.Selected;
        if (selected is null || !NativeMethods.IsWindow((nint)selected.Handle)) return;
        action((nint)selected.Handle);
        selected.IsIconic = iconic;
        selected.IsZoomed = zoomed;
        Refresh();
    }

    void SchedulePrune(long handle)
    {
        _prune?.Stop();
        _prune = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _prune.Tick += (_, _) =>
        {
            _prune.Stop();
            if (_session is null) return;
            if (!NativeMethods.IsWindow((nint)handle))
                _session.RemoveHandle(handle);
            if (_deck.IsVisible) Refresh();
            SyncSampler();
        };
        _prune.Start();
    }

    void ShowStatus(string text, bool sticky = false)
    {
        _status = text;
        _statusSticky = sticky;
        _statusTimer.Stop();
        if (!sticky) _statusTimer.Start();
        if (_deck.IsVisible) Refresh();
    }

    void Refresh()
    {
        DisarmIfSelectionChanged();
        _deck.Render(BuildState());
    }

    SwitcherViewState BuildState()
    {
        var settings = _settings();
        if (_displayCount < 0) _displayCount = MonitorUtil.All().Count;
        var showBadges = settings.ShowMonitorBadges && _displayCount >= 2;
        if (!showBadges && _session is { MonitorFilter: > 0 })
            _session.SetMonitorFilter(0);
        var filtered = _session?.Filtered ?? Array.Empty<WindowEntry>();
        var allCount = _session?.All.Count ?? 0;
        var loc = Localizer.Current;
        var appCount = _session?.AppHits.Count ?? 0;
        var count = _session is null
            ? ""
            : appCount > 0
                ? loc.Format("count.apps", filtered.Count, appCount)
                : _session.Query.Length == 0 && _session.MonitorFilter == 0
                    ? loc.Format("count.windows", filtered.Count)
                    : loc.Format("count.filtered", filtered.Count, allCount);
        var trigger = HotkeyLogic.TriggerDisplayName(HotkeyLogic.TriggerModifier(settings.Hotkey ?? HotkeyChord.AltTab()));
        var hint = settings.StickySwitcher || _searchMode
            ? loc.Format("hint.sticky", trigger)
            : loc.Format("hint.gesture", trigger);
        var showLoad = settings.ShowLoad;
        var systemText = !showLoad
            ? ""
            : _system.MemoryTotalBytes <= 0
                ? loc.Format("load.summary", "…", "…")
                : loc.Format("load.summary", LoadFormat.Cpu(_system.CpuPercent), LoadFormat.MemoryPair(_system.MemoryUsedBytes, _system.MemoryTotalBytes));
        return new SwitcherViewState
        {
            Filtered = filtered,
            SelectedIndex = _session?.SelectedIndex ?? -1,
            Query = _session?.Query ?? "",
            Hint = hint,
            CountText = count,
            Status = _status,
            SearchMode = _searchMode,
            Icons = _icons,
            Loads = showLoad && _session is not null
                ? _session.All.ToDictionary(entry => entry.Handle, entry => _session.LoadFor(entry.Handle))
                : new Dictionary<long, TaskLoad>(),
            ShowLoad = showLoad,
            SystemText = systemText,
            SortText = loc[settings.SortMode switch
            {
                LoadSortMode.FocusTime => "sort.stable",
                LoadSortMode.Initial => "sort.initial",
                LoadSortMode.Cpu => "sort.cpu",
                LoadSortMode.Memory => "sort.memory",
                _ => "sort.recent"
            }],
            GroupSections = _session?.Sections(loc["tier.infrequent"]) ?? Array.Empty<string>(),
            Indexes = _session?.Indexes ?? new Dictionary<long, string>(),
            AppIndexes = AppIndexLabels(appCount),
            EmptyText = EmptyMessage(loc, settings, filtered.Count, allCount),
            Apps = _session?.AppHits ?? Array.Empty<AppHit>(),
            AppIcons = _appIcons,
            LaunchSection = _apps is { UsingBuiltInIndex: true }
                ? loc["launch.section"] + "  ·  " + loc["launch.everythingOff"]
                : loc["launch.section"],
            OpenBadge = loc["launch.openBadge"],
            SwitchSubtitle = loc["launch.switchSubtitle"],
            ShowMonitorBadges = showBadges,
            MonitorFilterOn = _session?.MonitorFilter > 0,
            MonitorFilterText = loc["monitor.only"]
        };
    }

    string EmptyMessage(Localizer loc, AppSettings settings, int filteredCount, int allCount)
    {
        var apps = _session?.AppHits.Count ?? 0;
        if (filteredCount + apps > 0) return "";
        if (_session is null || (allCount == 0 && _session.Query.Length == 0)) return loc["list.empty"];
        if (_session.MonitorFilter > 0 && _session.Query.Length == 0) return loc["list.monitorEmpty"];
        return loc[settings.LaunchApps ? "list.nomatchApps" : "list.nomatch"];
    }

    void ToggleMonitorFilter()
    {
        if (_session is null || !_settings().ShowMonitorBadges) return;
        if (_session.MonitorFilter > 0)
        {
            _session.SetMonitorFilter(0);
        }
        else
        {
            var number = _deck.HostMonitorNumber();
            if (number <= 0 || MonitorUtil.All().Count < 2) return;
            _session.SetMonitorFilter(number);
        }
        if (_deck.IsVisible) Refresh();
    }

    string[] AppIndexLabels(int count)
    {
        if (count <= 0 || _session is null) return Array.Empty<string>();
        var labels = new string[count];
        for (var i = 0; i < count; i++)
            labels[i] = _session.IndexForApp(i);
        return labels;
    }

    void OnDisplaysChanged()
    {
        _windows.Invalidate();
        var wasVisible = _deck.IsVisible;
        try
        {
            _deck.Prepare(_settings(), _restore, captureForeground: false);
            _deck.EnsureCreated();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }

        if (_session is null) return;
        var live = MonitorUtil.Live();
        _displayCount = live.Count;
        foreach (var entry in _session.All)
            MonitorLocator.CopyTo(entry, MonitorLocator.Locate((nint)entry.Handle, live));
        if (!_settings().ShowMonitorBadges || live.Count < 2)
            _session.SetMonitorFilter(0);
        else if (_session.MonitorFilter > 0)
        {
            var number = _deck.HostMonitorNumber();
            if (number > 0) _session.SetMonitorFilter(number);
        }
        if (!wasVisible) return;
        Refresh();
        _deck.Reveal();
        _deck.SetHoverHighlight(_hoverHighlight);
    }

    void QueueIcons()
    {
        var generation = ++_iconGeneration;
        if (_session is null) return;
        var windows = new List<(long Handle, string Path)>(_session.All.Count);
        foreach (var entry in _session.All)
            windows.Add((entry.Handle, entry.IconPath ?? ""));
        var apps = new List<(string Id, string Path, int Index, string Launch)>(_session.AppHits.Count);
        foreach (var hit in _session.AppHits)
        {
            var path = string.IsNullOrWhiteSpace(hit.App.IconLocation) ? hit.App.ResolvedPath : hit.App.IconLocation;
            apps.Add((hit.App.Id, path ?? "", hit.App.IconIndex, hit.App.LaunchPath ?? ""));
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            var memo = new Dictionary<string, IconLoader.IconPixels?>(StringComparer.OrdinalIgnoreCase);
            var windowPixels = new List<(long Handle, string Path, int Index, IconLoader.IconPixels Pixels)>();
            var windowReady = new List<(long Handle, System.Windows.Media.ImageSource Icon)>();
            foreach (var item in windows)
                TakeIcon(item.Path, 0, memo, icon => windowReady.Add((item.Handle, icon)), pixels => windowPixels.Add((item.Handle, item.Path, 0, pixels)));

            var appPixels = new List<(string Id, string Path, int Index, IconLoader.IconPixels Pixels)>();
            var appReady = new List<(string Id, System.Windows.Media.ImageSource Icon)>();
            foreach (var item in apps)
            {
                var cached = IconLoader.Cached(item.Path, item.Index) ?? IconLoader.Cached(item.Launch, 0);
                if (cached is not null)
                {
                    appReady.Add((item.Id, cached));
                    continue;
                }
                if (DecodeOnce(item.Path, item.Index, memo, out var pixels))
                    appPixels.Add((item.Id, item.Path, item.Index, pixels));
                else if (DecodeOnce(item.Launch, 0, memo, out var fallback))
                    appPixels.Add((item.Id, item.Launch, 0, fallback));
            }

            _deck.Dispatcher.BeginInvoke(() =>
            {
                ApplyDecoded(generation, windowPixels, windowReady, (handle, source) => _icons[handle] = source);
                foreach (var item in appPixels)
                {
                    var source = IconLoader.Freeze(item.Pixels);
                    if (source is null) continue;
                    IconLoader.Store(item.Path, item.Index, source);
                    if (generation == _iconGeneration)
                        _appIcons[item.Id] = source;
                }
                if (generation == _iconGeneration)
                {
                    foreach (var item in appReady)
                        _appIcons[item.Id] = item.Icon;
                }
                if (generation == _iconGeneration && _deck.IsVisible)
                    Refresh();
            }, DispatcherPriority.Background);
        });
    }

    static void TakeIcon(
        string path,
        int index,
        Dictionary<string, IconLoader.IconPixels?> memo,
        Action<System.Windows.Media.ImageSource> addReady,
        Action<IconLoader.IconPixels> addPixels)
    {
        var cached = IconLoader.Cached(path, index);
        if (cached is not null)
        {
            addReady(cached);
            return;
        }
        if (DecodeOnce(path, index, memo, out var pixels))
            addPixels(pixels);
    }

    static bool DecodeOnce(string path, int index, Dictionary<string, IconLoader.IconPixels?> memo, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (string.IsNullOrWhiteSpace(path)) return false;
        var key = index == 0 ? path : path + "\n" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (memo.TryGetValue(key, out var existing))
        {
            if (existing is not { } hit) return false;
            pixels = hit;
            return true;
        }
        if (!IconLoader.TryDecode(path, index, out pixels))
        {
            memo[key] = null;
            return false;
        }
        memo[key] = pixels;
        return true;
    }

    void ApplyDecoded(
        int generation,
        List<(long Handle, string Path, int Index, IconLoader.IconPixels Pixels)> decoded,
        List<(long Handle, System.Windows.Media.ImageSource Icon)> ready,
        Action<long, System.Windows.Media.ImageSource> assign)
    {
        foreach (var item in decoded)
        {
            var source = IconLoader.Freeze(item.Pixels);
            if (source is null) continue;
            IconLoader.Store(item.Path, item.Index, source);
            if (generation == _iconGeneration)
                assign(item.Handle, source);
        }
        if (generation != _iconGeneration) return;
        foreach (var item in ready)
            assign(item.Handle, item.Icon);
    }

    void OnSpace() => Hotkeys?.RequestCommit();
}
