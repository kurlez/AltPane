using System.Windows.Threading;
using AltPane.Core.Actions;
using AltPane.Core.Apps;
using AltPane.Core.Input;
using AltPane.Core.Load;
using AltPane.Core.Layout;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Preview;
using AltPane.Core.Switcher;

namespace AltPane;

internal sealed class SwitcherPresenter
{
    readonly Func<AppSettings> _settings;
    readonly SwitcherWindow _window;
    readonly ThumbnailController _thumbs = new();
    readonly Dictionary<long, System.Windows.Media.ImageSource> _icons = new();
    readonly Dictionary<string, System.Windows.Media.ImageSource> _appIcons = new(StringComparer.OrdinalIgnoreCase);
    AppIndexService? _apps;
    readonly DispatcherTimer _statusTimer;
    readonly LoadSampler _sampler = new();
    readonly KillArm _arm = new();
    readonly MinimizedPreviewSession _preview = new();
    readonly Dictionary<long, System.Windows.Media.ImageSource> _stills = new();
    int _captureGeneration;
    long _giveUpHandle;
    SwitcherSession? _session;
    nint _restore;
    string? _status;
    int _iconGeneration;
    bool _searchMode;
    bool _statusSticky;
    SystemLoad _system;

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
        _window.AppHovered += id =>
        {
            if (_session?.SelectApp(id) == true)
                Refresh(updateThumb: true);
        };
        _window.AppInvoked += id =>
        {
            _session?.SelectApp(id);
            Hotkeys?.RequestCommit();
        };
        _window.AdminLaunchRequested += LaunchAdmin;
        _window.OpenLocationRequested += RevealSelected;
        _window.CommitRequested += () => Hotkeys?.RequestCommit();
        _window.CommitInPlaceRequested += () => Hotkeys?.RequestCommit(moveChoice: 0);
        _window.ForceMoveRequested += () => Hotkeys?.RequestCommit(moveChoice: 1);
        _window.ClearQueryRequested += () => Hotkeys?.RequestClearQuery();
        _window.CancelRequested += () => Hotkeys?.RequestCancel();
        _window.SearchRequested += () => Hotkeys?.RequestSearchMode();
        _window.QueryEdited += text =>
        {
            _session?.SetQuery(text);
            Hotkeys?.SyncQueryLength(text?.Length ?? 0);
            SyncSearch();
            Refresh(updateThumb: true);
        };
        _window.NavigateRequested += Nudge;
        _window.SpaceRequested += OnSpace;
        _window.CloseRequested += CloseSelected;
        _window.KillRequested += () => KillSelected(null);
        _window.KillTreeRequested += () => KillSelected(true);
        _window.SortCycleRequested += CycleSort;
        _window.MonitorFilterRequested += ToggleMonitorFilter;
        _window.DisplaysChanged += OnDisplaysChanged;
        _window.HostMonitorChanged += FollowHostMonitor;
        _window.LabelsRequested += () =>
        {
            if (_window.IsVisible) Refresh(updateThumb: false);
        };
        _window.PreviewRequested += PreviewSelected;
        _sampler.Updated += update => _window.Dispatcher.BeginInvoke(() => ApplyLoad(update));
        _window.MinimizeRequested += () => RunAction(static hwnd => WindowActions.Minimize(hwnd), iconic: true, zoomed: false);
        _window.MaximizeRequested += () => RunAction(static hwnd => WindowActions.Maximize(hwnd), iconic: false, zoomed: true);
        _window.RestoreRequested += () => RunAction(static hwnd => WindowActions.Restore(hwnd), iconic: false, zoomed: false);
        _window.LayoutChanged += () => UpdateThumb();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            if (_statusSticky) return;
            _status = null;
            if (_window.IsVisible) Refresh(updateThumb: false);
        };
    }

    public HotkeyService? Hotkeys { get; set; }
    public bool PendingShow { get; private set; }
    public bool IsSwitcherVisible => _window.IsVisible;
    public bool IsImeComposing => _window.IsImeComposing;

    public void UseApps(AppIndexService apps)
    {
        _apps = apps;
        apps.Updated += () => _window.Dispatcher.BeginInvoke(ReloadApps);
    }
    public event Action<string>? Notify;
    public event Action? PersistRequested;

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
                SyncSearch();
                if (_window.IsVisible) Refresh(updateThumb: true);
                break;
            case UiOp.Backspace:
                _session?.Backspace();
                SyncSearch();
                if (_window.IsVisible) Refresh(updateThumb: true);
                break;
            case UiOp.ClearQuery:
                if (_session?.ClearQuery() == true)
                {
                    Hotkeys?.SyncQueryLength(0);
                    SyncSearch();
                    if (_window.IsVisible)
                    {
                        Refresh(updateThumb: true);
                        if (_searchMode) _window.FocusSearch();
                    }
                }
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
            case UiOp.Commit commit:
                Commit(commit.MoveToMonitor);
                break;
            case UiOp.Cancel:
                Cancel();
                break;
            case UiOp.EnterSearch:
            case UiOp.ReleaseHold:
                _searchMode = true;
                if (!_window.IsVisible) PendingShow = true;
                else
                {
                    Refresh(updateThumb: false);
                    _window.ClaimKeyboard();
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
            case UiOp.AdminLaunch:
                LaunchAdmin();
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
        if (snap.SearchMode) _window.ClaimKeyboard();
        QueueIcons();
        UpdateThumb();
        SyncSampler();
    }

    public WindowEntry? TakeForQuickCommit()
    {
        var selected = _session?.Selected;
        ApplyPreview(_preview.Cancel());
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _thumbs.Clear();
        _sampler.Stop();
        _arm.Clear();
        return selected;
    }

    public void ActivateEntry(WindowEntry entry) => ActivateEntry(entry, null);

    void ActivateEntry(WindowEntry entry, bool? moveOverride)
    {
        if (!NativeMethods.IsWindow((nint)entry.Handle))
        {
            Notify?.Invoke(Localizer.Current["activate.missing"]);
            return;
        }
        if (WantsMove(moveOverride) && _window.TryGetHostMonitor(out var host)
            && !WindowMover.TryMoveTo((nint)entry.Handle, host, MonitorUtil.All()))
            AppLog.Info($"Could not move 0x{entry.Handle:X} onto the switcher's display. Activating it where it is.");
        if (!WindowActions.TryActivate((nint)entry.Handle))
            Notify?.Invoke(Localizer.Current["activate.failed"]);
    }

    public void ForceClose()
    {
        ApplyPreview(_preview.Cancel());
        _sampler.Stop();
        _thumbs.Clear();
        _window.ForceClose();
    }

    void Build(int delta)
    {
        ApplyPreview(_preview.Cancel());
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
        if (_settings().ShowLoad)
            _session.SetSortMode(_settings().SortMode);
        if (delta != 0) _session.Move(delta);
        ApplyCatalog(resetClock: true);
        _searchMode = false;
        _status = null;
        _arm.Clear();
        _statusSticky = false;
        _icons.Clear();
        foreach (var entry in entries)
            _icons[entry.Handle] = IconLoader.Letter(entry.Title);
        PendingShow = true;
        _iconGeneration++;
    }

    void DismissSwitcher(bool keepBorrowed)
    {
        ApplyPreview(keepBorrowed ? _preview.Commit() : _preview.Cancel());
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _thumbs.Clear();
        _sampler.Stop();
        _arm.Clear();
        _window.RunGuarded(() =>
        {
            if (_window.IsVisible) _window.HideSwitcher();
        });
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
        var host = HostForLaunch(null);
        DismissSwitcher(keepBorrowed: false);
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
        if (_session is null) return;
        ApplyCatalog(resetClock: false);
        if (_window.IsVisible) Refresh(updateThumb: false);
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
            var keepBorrowed = window is not null && _preview.IsBorrowing && _preview.Handle == window.Handle;
            var host = HostForLaunch(moveOverride);
            DismissSwitcher(keepBorrowed);
            if (window is not null)
            {
                _apps?.Remember(app.Id);
                ActivateEntry(window, moveOverride);
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
        ApplyPreview(_preview.Commit());
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _thumbs.Clear();
        _sampler.Stop();
        _arm.Clear();
        _window.RunGuarded(() =>
        {
            if (_window.IsVisible) _window.HideSwitcher();
        });
        if (selected is null) return;
        ActivateEntry(selected, moveOverride);
    }

    bool WantsMove(bool? moveOverride) => moveOverride ?? _settings().MoveToSwitcherMonitor;

    DisplayBounds? HostForLaunch(bool? moveOverride)
    {
        if (!WantsMove(moveOverride)) return null;
        return _window.TryGetHostMonitor(out var host) ? host : null;
    }

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
        ApplyPreview(_preview.Cancel());
        var restore = _restore;
        var wasVisible = _window.IsVisible;
        PendingShow = false;
        _searchMode = false;
        _session = null;
        _thumbs.Clear();
        _sampler.Stop();
        _arm.Clear();
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
        DisarmIfSelectionChanged();
        if (_window.IsVisible) Refresh(updateThumb: true);
    }

    void CloseSelected()
    {
        var session = _session;
        var selected = session?.Selected;
        if (session is null || selected is null) return;
        ReturnBorrowed(selected.Handle);
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
        ReturnBorrowed(selected.Handle);
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

    void CycleSort()
    {
        var settings = _settings();
        settings.SortMode = settings.SortMode switch
        {
            LoadSortMode.Recent => LoadSortMode.Cpu,
            LoadSortMode.Cpu => LoadSortMode.Memory,
            _ => LoadSortMode.Recent
        };
        _session?.SetSortMode(settings.ShowLoad ? settings.SortMode : LoadSortMode.Recent);
        try { PersistRequested?.Invoke(); }
        catch (Exception ex) { AppLog.Error(ex); }
        if (_window.IsVisible) Refresh(updateThumb: false);
    }

    void ApplyLoad(LoadUpdate update)
    {
        if (_session is null || !_window.IsVisible || !_settings().ShowLoad) return;
        _system = update.System;
        var byHandle = new Dictionary<long, TaskLoad>();
        foreach (var entry in _session.All)
        {
            if (update.ByProcessId.TryGetValue(entry.KillProcessId, out var load))
                byHandle[entry.Handle] = load;
        }
        _session.UpdateLoads(byHandle);
        DisarmIfSelectionChanged();
        Refresh(updateThumb: false);
    }

    void SyncSampler()
    {
        if (_session is null || !_window.IsVisible || !_settings().ShowLoad)
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
        if (iconic) ReturnBorrowed(selected.Handle);
        else ReleaseBorrowed(selected.Handle);
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
            SyncSampler();
        };
        timer.Start();
    }

    void ShowStatus(string text, bool sticky = false)
    {
        _status = text;
        _statusSticky = sticky;
        _statusTimer.Stop();
        if (!sticky) _statusTimer.Start();
        if (_window.IsVisible) Refresh(updateThumb: false);
    }

    void Refresh(bool updateThumb)
    {
        DisarmIfSelectionChanged();
        _window.Render(BuildState());
        if (updateThumb && _window.IsVisible) UpdateThumb();
    }

    SwitcherViewState BuildState()
    {
        var settings = _settings();
        var showBadges = settings.ShowMonitorBadges && MonitorUtil.All().Count >= 2;
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
            ShowPreview = settings.ShowPreview,
            SearchMode = _searchMode,
            Icons = _icons,
            Loads = showLoad && _session is not null
                ? _session.All.ToDictionary(entry => entry.Handle, entry => _session.LoadFor(entry.Handle))
                : new Dictionary<long, TaskLoad>(),
            ShowLoad = showLoad,
            SystemText = systemText,
            SortText = loc[settings.SortMode switch
            {
                LoadSortMode.Cpu => "sort.cpu",
                LoadSortMode.Memory => "sort.memory",
                _ => "sort.recent"
            }],
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
            var number = _window.HostMonitorNumber();
            if (number <= 0 || MonitorUtil.All().Count < 2) return;
            _session.SetMonitorFilter(number);
        }
        if (_window.IsVisible) Refresh(updateThumb: true);
    }

    void FollowHostMonitor()
    {
        if (_session is null || _session.MonitorFilter == 0) return;
        var number = _window.HostMonitorNumber();
        if (number > 0) _session.SetMonitorFilter(number);
        if (_window.IsVisible) Refresh(updateThumb: true);
    }

    void OnDisplaysChanged()
    {
        if (_session is null) return;
        var live = MonitorUtil.Live();
        foreach (var entry in _session.All)
            MonitorLocator.CopyTo(entry, MonitorLocator.Locate((nint)entry.Handle, live));
        if (!_settings().ShowMonitorBadges || live.Count < 2)
            _session.SetMonitorFilter(0);
        else if (_session.MonitorFilter > 0)
        {
            var number = _window.HostMonitorNumber();
            if (number > 0) _session.SetMonitorFilter(number);
        }
        if (_window.IsVisible) Refresh(updateThumb: false);
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
            foreach (var hit in _session.AppHits.ToList())
            {
                if (generation != _iconGeneration) return;
                var path = string.IsNullOrWhiteSpace(hit.App.IconLocation) ? hit.App.ResolvedPath : hit.App.IconLocation;
                var icon = IconLoader.TryGetFileIcon(path, hit.App.IconIndex) ?? IconLoader.TryGetFileIcon(hit.App.LaunchPath, 0);
                if (icon is not null) _appIcons[hit.App.Id] = icon;
            }
            if (_window.IsVisible) Refresh(updateThumb: false);
        }, DispatcherPriority.Background);
    }

    void UpdateThumb()
    {
        var settings = _settings();
        if (_session?.SelectedApp is { } appHit)
        {
            _thumbs.Clear();
            _window.ShowStillPreview(null, null);
            var path = string.IsNullOrWhiteSpace(appHit.App.ResolvedPath) ? appHit.App.LaunchPath : appHit.App.ResolvedPath;
            _window.SetPreviewMessage(appHit.SwitchHandle != 0
                ? Localizer.Current["launch.switchSubtitle"]
                : path);
            return;
        }
        FollowMinimizedPreview();
        var selected = _session?.Selected;
        if (!settings.ShowPreview || selected is null || !_window.IsVisible || selected.IsIconic)
        {
            _thumbs.Clear();
            if (selected is { IsIconic: true } && _stills.TryGetValue(selected.Handle, out var still))
            {
                _window.ShowStillPreview(still, Localizer.Current["preview.minimizedBadge"]);
                _window.SetPreviewMessage("");
            }
            else
            {
                _window.ShowStillPreview(null, null);
                _window.SetPreviewMessage(selected is null
                    ? Localizer.Current["preview.none"]
                    : selected.IsIconic
                        ? Localizer.Current["preview.minimized"]
                        : "");
            }
            return;
        }

        _window.ShowStillPreview(null, null);

        if (!_window.TryGetThumbBounds(out var bounds))
        {
            _thumbs.Clear();
            return;
        }

        var hr = _thumbs.Show(_window.Handle, (nint)selected.Handle, bounds);
        if (hr == 0)
        {
            _window.SetPreviewMessage("");
            QueueStill(selected.Handle, (nint)selected.Handle);
            return;
        }

        _thumbs.Clear();
        var denied = unchecked((int)hr) == unchecked((int)0x80070005);
        _window.SetPreviewMessage(Localizer.Current[denied ? "preview.denied" : "preview.unavailable"]);
    }

    void FollowMinimizedPreview()
    {
        if (!_settings().ShowPreview || _session?.Selected is not { } selected)
        {
            ApplyPreview(_preview.Cancel());
            return;
        }
        if (_giveUpHandle != 0 && _giveUpHandle != selected.Handle)
            _giveUpHandle = 0;
        if (_preview.IsBorrowing && _preview.Handle == selected.Handle)
            return;
        var auto = _settings().PreviewMinimized && _giveUpHandle != selected.Handle;
        ApplyPreview(_preview.Consider(selected.Handle, selected.IsIconic, auto, manual: false));
    }

    void OnSpace()
    {
        var selected = _session?.Selected;
        if (selected is null || !selected.IsIconic)
        {
            Hotkeys?.RequestCommit();
            return;
        }
        _giveUpHandle = 0;
        ApplyPreview(_preview.Consider(selected.Handle, true, auto: false, manual: true), manualBorrow: true);
        if (_window.IsVisible) UpdateThumb();
    }

    void PreviewSelected()
    {
        var selected = _session?.Selected;
        if (selected is null || !selected.IsIconic) return;
        _giveUpHandle = 0;
        ApplyPreview(_preview.Consider(selected.Handle, true, auto: false, manual: true), manualBorrow: true);
        if (_window.IsVisible) UpdateThumb();
    }

    void ReturnBorrowed(long handle)
    {
        if (!_preview.IsBorrowing || _preview.Handle != handle) return;
        ApplyPreview(_preview.Cancel());
    }

    void ReleaseBorrowed(long handle)
    {
        if (!_preview.IsBorrowing || _preview.Handle != handle) return;
        ApplyPreview(_preview.Commit());
    }

    void ApplyPreview(PreviewStep step, bool manualBorrow = false)
    {
        if (step.ReturnHandle != 0 && step.ReturnPlacement is { } back)
        {
            WindowPlacementStore.TryApply((nint)step.ReturnHandle, back);
            MarkIconic(step.ReturnHandle, true);
        }
        if (step.ReleaseHandle != 0 && step.ReleasePlacement is { } forward)
        {
            _window.SuppressCancel(600);
            WindowPlacementStore.TryApply((nint)step.ReleaseHandle, forward);
            MarkIconic(step.ReleaseHandle, false);
        }
        if (step.BorrowHandle != 0)
            BeginBorrow(step.BorrowHandle, manualBorrow);
    }

    void BeginBorrow(long handle, bool manual)
    {
        if (!manual && _giveUpHandle == handle) return;
        var hwnd = (nint)handle;
        if (!NativeMethods.IsWindow(hwnd) || !WindowPlacementStore.TryGet(hwnd, out var original))
        {
            _giveUpHandle = handle;
            ShowStatus(Localizer.Current["preview.restoreFailed"]);
            return;
        }

        var off = PlacementEdits.OffScreen(
            original,
            NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen));
        _window.SuppressCancel(700);
        var applied = false;
        _window.RunGuarded(() => applied = WindowPlacementStore.TryApply(hwnd, off));
        if (!applied)
        {
            _giveUpHandle = handle;
            ShowStatus(Localizer.Current["preview.restoreFailed"]);
            return;
        }

        _preview.NoteBorrowed(handle, original);
        if (!NativeMethods.IsIconic(hwnd))
        {
            MarkIconic(handle, false);
            KeepSwitcherForeground();
            return;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_preview.IsBorrowing || _preview.Handle != handle) return;
            _window.SuppressCancel(400);
            if (NativeMethods.IsWindow(hwnd) && !NativeMethods.IsIconic(hwnd))
            {
                MarkIconic(handle, false);
                KeepSwitcherForeground();
                if (_window.IsVisible) UpdateThumb();
                return;
            }
            if (NativeMethods.IsWindow(hwnd))
                WindowPlacementStore.TryApply(hwnd, original);
            _preview.NoteFailed();
            _giveUpHandle = handle;
            MarkIconic(handle, true);
            ShowStatus(Localizer.Current["preview.restoreFailed"]);
            if (_window.IsVisible) UpdateThumb();
        };
        timer.Start();
    }

    void KeepSwitcherForeground()
    {
        var hwnd = _window.Handle;
        if (hwnd == 0) return;
        NativeMethods.SetWindowPos(hwnd, new nint(-1), 0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        if (NativeMethods.GetForegroundWindow() != hwnd)
            NativeMethods.SetForegroundWindow(hwnd);
    }

    void MarkIconic(long handle, bool iconic)
    {
        var entry = _session?.All.FirstOrDefault(item => item.Handle == handle);
        if (entry is not null) entry.IsIconic = iconic;
    }

    void QueueStill(long handle, nint hwnd)
    {
        if (_stills.ContainsKey(handle)) return;
        var generation = ++_captureGeneration;
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (generation != _captureGeneration || _stills.ContainsKey(handle)) return;
            var image = PreviewCapture.TryCapture(hwnd);
            if (image is null) return;
            _stills[handle] = image;
            while (_stills.Count > 16)
            {
                long drop = 0;
                foreach (var key in _stills.Keys)
                {
                    drop = key;
                    break;
                }
                _stills.Remove(drop);
            }
        }, DispatcherPriority.Background);
    }
}
