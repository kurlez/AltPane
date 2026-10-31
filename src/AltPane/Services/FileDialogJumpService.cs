using System.Windows.Interop;
using System.Windows.Threading;
using AltPane.Core.Dialogs;
using AltPane.Core.Input;
using AltPane.Core.Interop;
using AltPane.Core.Layout;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Search;

namespace AltPane;

/// <summary>
/// Shows a folder panel while a file dialog is in front. The WinEvent hooks are the
/// only always-on cost. Explorer windows and Everything run when the panel is shown
/// or the user types.
/// </summary>
internal sealed class FileDialogJumpService : IDisposable
{
    const uint EventForeground = 0x0003;
    const uint EventObjectShow = 0x8002;
    const uint OutOfContext = 0x0000;
    const uint SkipOwnProcess = 0x0002;

    readonly Func<AppSettings> _settings;
    readonly FileIndexService _files;
    readonly Dispatcher _dispatcher;
    readonly FolderMru _mru;
    readonly string _mruPath;
    readonly nint[] _hooks = new nint[2];
    WinEventProc? _callback;
    FileDialogPanel? _panel;
    nint _dialog;
    bool _save;
    NativeRect _lastRect;
    bool _haveRect;
    DispatcherTimer? _follow;
    DispatcherTimer? _searchTimer;
    string _pendingQuery = "";
    int _searchGen;
    int _iconGen;
    bool _localsReady;
    IReadOnlyList<string> _explorer = Array.Empty<string>();
    IReadOnlyList<string> _recent = Array.Empty<string>();
    int _armedVk;
    bool _started;
    bool _suggestionsOpen;
    bool _navigating;
    bool _disposed;

    public FileDialogJumpService(Func<AppSettings> settings, FileIndexService files, Dispatcher dispatcher)
    {
        _settings = settings;
        _files = files;
        _dispatcher = dispatcher;
        _mruPath = FolderMruStore.DefaultPath();
        _mru = FolderMruStore.Load(_mruPath);
    }

    public void Start()
    {
        if (_disposed || _started) return;
        if (!_settings().FileDialogJump) return;
        _callback = OnEvent;
        var flags = OutOfContext | SkipOwnProcess;
        _hooks[0] = NativeMethods.SetWinEventHook(EventForeground, EventForeground, 0, _callback, 0, 0, flags);
        _hooks[1] = NativeMethods.SetWinEventHook(EventObjectShow, EventObjectShow, 0, _callback, 0, 0, flags);
        _started = _hooks[0] != 0 || _hooks[1] != 0;
        if (!_started)
            AppLog.Info("File dialog hook was not installed.");
        else
            AppLog.Info("File dialog quick jump is on.");
    }

    public void ApplySettings()
    {
        if (_settings().FileDialogJump)
        {
            Start();
            if (_panel is { IsVisible: true })
            {
                _panel.SetHint(HotkeyLogic.Format(_settings().FileDialogHotkey ?? HotkeyChord.CtrlG()));
                if (_dialog != 0) Place();
            }
        }
        else
        {
            Detach();
            Stop();
        }
    }

    public void Stop()
    {
        for (var i = 0; i < _hooks.Length; i++)
        {
            if (_hooks[i] == 0) continue;
            NativeMethods.UnhookWinEvent(_hooks[i]);
            _hooks[i] = 0;
        }
        _started = false;
        _callback = null;
    }

    public bool TakeChord(int virtualKey, bool down, ModifierState mods)
    {
        if (!down)
        {
            if (virtualKey != _armedVk) return false;
            _armedVk = 0;
            return true;
        }
        if (_disposed || !_settings().FileDialogJump || _dialog == 0) return false;
        if (!FileDialogHotkey.Matches(_settings().FileDialogHotkey ?? HotkeyChord.CtrlG(), virtualKey, mods))
            return false;
        _armedVk = virtualKey;
        _dispatcher.BeginInvoke(FocusSearch);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        Stop();
        try { _panel?.Close(); } catch { }
        _panel = null;
    }

    void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        if (_disposed || hwnd == 0 || idObject != 0 || idChild != 0) return;
        try
        {
            if (eventType == EventObjectShow)
            {
                var foreground = NativeMethods.GetForegroundWindow();
                if (foreground == 0) return;
                // Children are created after the dialog is already in front. Recheck that dialog.
                if (hwnd != foreground && NativeMethods.GetAncestor(hwnd, 2u) != foreground)
                    return;
                hwnd = foreground;
            }
            _dispatcher.BeginInvoke(() => OnForeground(hwnd));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    void OnForeground(nint hwnd)
    {
        if (_disposed || !_settings().FileDialogJump)
        {
            Detach();
            return;
        }
        if (IsPanel(hwnd)) return;
        if (hwnd == _dialog && NativeMethods.IsWindow(_dialog))
        {
            EnsureShown();
            return;
        }
        if (FileDialogProbe.TryClassify(hwnd, out var kind, out var save))
        {
            var changed = hwnd != _dialog;
            Attach(hwnd, save);
            if (changed)
                AppLog.Info($"file dialog {kind} {(save ? "save" : "open")}");
            return;
        }
        if (_dialog != 0) Detach();
    }

    void Attach(nint hwnd, bool save)
    {
        var changed = hwnd != _dialog;
        _dialog = hwnd;
        _save = save;
        var panel = EnsurePanel();
        panel.AttachTo(hwnd);
        panel.SetHint(HotkeyLogic.Format(_settings().FileDialogHotkey ?? HotkeyChord.CtrlG()));
        if (changed)
            panel.Collapse(clearQuery: true);
        Place();
        StartFollow();
    }

    void EnsureShown()
    {
        if (_dialog == 0) return;
        Place();
        StartFollow();
    }

    void Detach()
    {
        RememberLocation();
        _dialog = 0;
        _haveRect = false;
        _armedVk = 0;
        _localsReady = false;
        _searchGen++;
        _follow?.Stop();
        _searchTimer?.Stop();
        if (_panel is null) return;
        try
        {
            new WindowInteropHelper(_panel).Owner = 0;
            if (_panel.IsVisible) _panel.Hide();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    void FocusSearch()
    {
        if (_dialog == 0 || !NativeMethods.IsWindow(_dialog)) return;
        var panel = EnsurePanel();
        if (!panel.IsVisible) EnsureShown();
        panel.FocusSearch();
    }

    void Place()
    {
        if (_panel is null || _dialog == 0 || !NativeMethods.GetWindowRect(_dialog, out var rect)) return;
        var work = MonitorUtil.GetWorkArea(SwitcherPlacement.ActiveWindow, _dialog);
        var scaleX = work.DpiX <= 0 ? 1d : work.DpiX / 96d;
        var scaleY = work.DpiY <= 0 ? 1d : work.DpiY / 96d;
        var edge = _settings().FileDialogPanelEdge;
        var dialog = new RectangleMath.RectI(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
        var area = new RectangleMath.RectI(work.X, work.Y, work.Width, work.Height);
        var bar = DialogPanelLayout.Dock(
            dialog,
            area,
            edge,
            Dip(DialogPanelLayout.BarWidthDip, scaleX),
            Dip(DialogPanelLayout.BarHeightDip, scaleY),
            gap: 0,
            inset: Dip(DialogPanelLayout.CornerInsetDip, scaleX),
            buttonReserve: Dip(DialogPanelLayout.ButtonReserveDip, scaleX));
        var listDip = 0;
        if (_panel.IsExpanded)
        {
            var rows = _panel.RowCount;
            listDip = rows <= 0
                ? DialogPanelLayout.EmptyListDip
                : Math.Min(rows, DialogPanelLayout.MaxVisibleRows) * DialogPanelLayout.RowHeightDip;
        }
        var slot = listDip > 0 ? DialogPanelLayout.ExpandUp(bar, area, Dip(listDip, scaleY)) : bar;
        _panel.MoveTo(slot.X, slot.Y, slot.Width, slot.Height, scaleX, scaleY);
        _lastRect = rect;
        _haveRect = true;
    }

    void StartFollow()
    {
        _follow ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _follow.Tick -= OnFollow;
        _follow.Tick += OnFollow;
        _follow.Start();
    }

    void OnFollow(object? sender, EventArgs e)
    {
        if (_dialog == 0 || !NativeMethods.IsWindow(_dialog))
        {
            Detach();
            return;
        }
        var foreground = NativeMethods.GetForegroundWindow();
        if (_navigating) return;
        if (foreground != 0 && foreground != _dialog && !IsPanel(foreground))
        {
            Detach();
            return;
        }
        if (!NativeMethods.GetWindowRect(_dialog, out var rect)) return;
        if (_haveRect && rect.Left == _lastRect.Left && rect.Top == _lastRect.Top && rect.Right == _lastRect.Right && rect.Bottom == _lastRect.Bottom)
            return;
        Place();
    }

    void OnQuery(string text)
    {
        _pendingQuery = text ?? "";
        if (_panel is not { IsExpanded: true })
        {
            _searchTimer?.Stop();
            _searchGen++;
            return;
        }
        if (string.IsNullOrWhiteSpace(_pendingQuery))
        {
            _searchTimer?.Stop();
            _searchGen++;
            ShowSuggestions("");
            return;
        }
        _searchTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _searchTimer.Stop();
        _searchTimer.Tick -= OnSearch;
        _searchTimer.Tick += OnSearch;
        _searchTimer.Start();
    }

    void OnSearch(object? sender, EventArgs e)
    {
        _searchTimer?.Stop();
        ShowSuggestions(_pendingQuery);
    }

    void ShowSuggestions(string query)
    {
        if (_dialog == 0) return;
        if (!_localsReady || string.IsNullOrWhiteSpace(query))
            LoadLocals();
        var explorer = _explorer;
        var recent = _recent;

        if (string.IsNullOrWhiteSpace(query))
        {
            Publish(FolderMatch.Compose("", explorer, recent, null), "");
            return;
        }

        var generation = ++_searchGen;
        var settings = _settings();
        var index = _files.Current;
        var text = query;
        Task.Run(() => Search(settings, index, text)).ContinueWith(task => _dispatcher.BeginInvoke(() =>
        {
            if (generation != _searchGen || _dialog == 0) return;
            var matches = task.Status == TaskStatus.RanToCompletion ? task.Result : Array.Empty<string>();
            Publish(FolderMatch.Compose(text, explorer, recent, matches), text);
        }));
    }

    void LoadLocals()
    {
        try
        {
            _explorer = ExplorerFolders.Open();
            _recent = RecentFolders.Read(_mru.Entries);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            _explorer = Array.Empty<string>();
            _recent = _mru.Entries;
        }
        _localsReady = true;
    }

    static IReadOnlyList<string> Search(AppSettings settings, FileIndex index, string text)
    {
        try
        {
            IReadOnlyList<string> everything = Array.Empty<string>();
            if (settings.UseEverything)
            {
                var query = FolderMatch.EverythingQuery(text);
                if (query.Length > 0)
                {
                    var result = new EverythingClient().Query(query, 30, 350, settings.EverythingHttpPort, settings.EverythingHttpFallback);
                    everything = result.Paths;
                }
            }
            var builtin = settings.UseBuiltinFileIndex
                ? FolderMatch.SearchIndex(index, text, settings.EverythingExcludedPaths, 30)
                : Array.Empty<string>();
            var primary = everything.Count > 0 ? everything : builtin;
            var secondary = everything.Count > 0 ? builtin : Array.Empty<string>();
            return FolderMatch.Merge(primary, secondary, 30);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return Array.Empty<string>();
        }
    }

    void Publish(IReadOnlyList<FolderSuggestion> suggestions, string query)
    {
        if (_panel is null) return;
        var rows = new List<DialogFolderRow>(suggestions.Count);
        foreach (var item in suggestions)
        {
            rows.Add(new DialogFolderRow
            {
                Path = item.Path,
                Name = item.Name,
                Kind = Kind(item.Origin),
                Icon = IconLoader.Cached(item.Path, 0) ?? IconLoader.Letter(item.Name)
            });
        }
        _panel.SetRows(rows, null);
        LoadIcons(rows, ++_iconGen);
        if (!string.Equals(query, _panel.Query, StringComparison.Ordinal) && string.IsNullOrEmpty(query))
            _panel.ClearQuery();
    }

    void LoadIcons(List<DialogFolderRow> rows, int generation)
    {
        var paths = new string[rows.Count];
        for (var i = 0; i < rows.Count; i++) paths[i] = rows[i].Path;
        Task.Run(() =>
        {
            foreach (var path in paths)
            {
                try
                {
                    if (IconLoader.Cached(path, 0) is not null) continue;
                    if (ShellIcons.TryFile(path, 0, IconLoader.RequestEdge(), out var pixels))
                        IconLoader.Keep(path, 0, pixels);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex);
                }
            }
        }).ContinueWith(_ => _dispatcher.BeginInvoke(() =>
        {
            if (generation != _iconGen || _panel is null) return;
            foreach (var row in rows)
                row.Icon = IconLoader.Materialize(row.Path, 0) ?? row.Icon ?? IconLoader.Letter(row.Name);
            _panel.RefreshRows();
        }));
    }

    void Navigate(string path)
    {
        _navigating = true;
        try
        {
            if (_dialog == 0 || !NativeMethods.IsWindow(_dialog))
            {
                AppLog.Info($"file dialog jump class= method=none ok=false path={path} detail=dialog-gone");
                Detach();
                return;
            }
            var cls = ClassOf(_dialog);
            var surface = new LiveDialogSurface(_dialog);
            var result = DialogNavigation.Go(surface, path, _save, System.IO.Directory.Exists, step =>
                AppLog.Info($"file dialog jump class={cls} kind={surface.Kind} method={step.Method} ok={step.Succeeded} path={path} detail={step.Detail}"));
            if (result.Succeeded)
                Remember(path);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            AppLog.Info($"file dialog jump class= method=none ok=false path={path} detail=exception");
        }
        finally
        {
            _navigating = false;
            if (_dialog != 0)
                _panel?.ReturnFocus(_dialog);
        }
    }

    static string ClassOf(nint hwnd)
    {
        var buffer = new System.Text.StringBuilder(64);
        NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    void Dismiss()
    {
        _panel?.ReturnFocus(_dialog);
    }

    void RememberLocation()
    {
        if (_dialog == 0 || !NativeMethods.IsWindow(_dialog)) return;
        try
        {
            var address = DialogAddress.Read(_dialog);
            if (FolderPath.IsAbsolute(address)) Remember(address);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    void Remember(string path)
    {
        var before = _mru.Entries.Count == 0 ? "" : _mru.Entries[0];
        _mru.Remember(path);
        if (_mru.Entries.Count == 0 || string.Equals(before, _mru.Entries[0], StringComparison.OrdinalIgnoreCase))
            return;
        try { FolderMruStore.Save(_mruPath, _mru); }
        catch (Exception ex) { AppLog.Error(ex); }
    }

    FileDialogPanel EnsurePanel()
    {
        if (_panel is { IsLoaded: true }) return _panel;
        _panel = new FileDialogPanel();
        _panel.QueryChanged += OnQuery;
        _panel.NavigateRequested += Navigate;
        _panel.DismissRequested += Dismiss;
        _panel.LayoutChanged += OnPanelLayout;
        _panel.Closed += (_, _) => _panel = null;
        _panel.SetHint(HotkeyLogic.Format(_settings().FileDialogHotkey ?? HotkeyChord.CtrlG()));
        return _panel;
    }

    void OnPanelLayout()
    {
        if (_dialog == 0 || _panel is null) return;
        Place();
        if (!_panel.IsExpanded)
        {
            _suggestionsOpen = false;
            return;
        }
        if (_suggestionsOpen) return;
        _suggestionsOpen = true;
        ShowSuggestions(_panel.Query);
    }

    static int Dip(int dip, double scale) => Math.Max(1, (int)Math.Round(dip * (scale <= 0 ? 1 : scale)));

    bool IsPanel(nint hwnd)
    {
        if (_panel is null || hwnd == 0) return false;
        return new WindowInteropHelper(_panel).Handle == hwnd;
    }

    static string Kind(FolderOrigin origin) => origin switch
    {
        FolderOrigin.Explorer => Localizer.Current["dialog.explorer"],
        FolderOrigin.Recent => Localizer.Current["dialog.recent"],
        _ => Localizer.Current["dialog.search"]
    };
}
