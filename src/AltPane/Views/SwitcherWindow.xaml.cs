using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AltPane.Core.Apps;
using AltPane.Core.Input;
using AltPane.Core.Layout;
using AltPane.Core.Load;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Switcher;
using WpfMods = System.Windows.Input.ModifierKeys;

namespace AltPane;

internal sealed class SwitcherRow : INotifyPropertyChanged
{
    ImageSource _icon;

    public SwitcherRow(WindowEntry entry, ImageSource icon, string index, string section = "")
    {
        Entry = entry;
        _icon = icon;
        Title = entry.Title;
        _subtitle = string.IsNullOrWhiteSpace(entry.DisplayProcessName) ? entry.ProcessName : entry.DisplayProcessName;
        IndexLabel = index;
        _section = section ?? "";
    }

    public SwitcherRow(AppHit hit, ImageSource icon, string section, string badge, string subtitle, string index)
    {
        AppHit = hit;
        _icon = icon;
        Title = hit.App.Name;
        _subtitle = subtitle;
        _section = section ?? "";
        Badge = badge;
        IndexLabel = index;
    }

    public WindowEntry? Entry { get; }
    public AppHit? AppHit { get; }
    public bool IsApp => AppHit is not null;
    public string? AppId => AppHit?.App.Id;
    public string Title { get; }
    string _subtitle = "";
    public string Subtitle => _subtitle;

    public void SetSubtitle(string? subtitle)
    {
        subtitle ??= "";
        if (_subtitle == subtitle) return;
        _subtitle = subtitle;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
    }
    string _section = "";
    public string Section => _section;
    public string Badge { get; } = "";
    public string IndexLabel { get; private set; } = "";

    public void SetIndex(string index)
    {
        if (IndexLabel == index) return;
        IndexLabel = index ?? "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IndexLabel)));
    }

    public void SetSection(string? section)
    {
        section ??= "";
        if (_section == section) return;
        _section = section;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Section)));
    }
    public string LoadText { get; private set; } = "";
    public double BarWidth { get; private set; }
    public int Heat { get; private set; }
    public bool ShowLoad { get; private set; }
    public string ToolTipText { get; private set; } = "";
    public string MonitorLabel { get; private set; } = "";
    public string KillLabel { get; private set; } = "";
    public System.Windows.Media.Brush MonitorBrush { get; private set; } = System.Windows.Media.Brushes.Transparent;
    public System.Windows.Media.Brush MonitorTint { get; private set; } = System.Windows.Media.Brushes.Transparent;
    string _baseTip = "";
    string _monitorTip = "";

    public void ApplyLoad(bool show, in TaskLoad load)
    {
        string text;
        double bar;
        int heat;
        if (!show)
        {
            text = "";
            bar = 0;
            heat = 0;
        }
        else if (load.ProcessCount <= 0)
        {
            text = "—";
            bar = 0;
            heat = 0;
        }
        else
        {
            text = LoadFormat.Row(load);
            var cpu = load.CpuPercent ?? 0;
            bar = Math.Clamp(cpu, 0, 100) / 100.0 * 40.0;
            heat = (int)LoadHeatMap.Classify(load.CpuPercent, load.WorkingSetBytes);
        }
        var tip = show && load.ProcessCount > 0
            ? $"{Title}\n{Subtitle}  ·  pid {Entry?.KillProcessId}\n{LoadFormat.Cpu(load.CpuPercent)}  ·  {LoadFormat.Bytes(load.WorkingSetBytes)}  ·  {LoadFormat.Bytes(load.PrivateBytes)}"
            : $"{Title}\n{Subtitle}";
        if (ShowLoad != show)
        {
            ShowLoad = show;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLoad)));
        }
        if (LoadText != text)
        {
            LoadText = text;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadText)));
        }
        if (Math.Abs(BarWidth - bar) > 0.01)
        {
            BarWidth = bar;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BarWidth)));
        }
        if (Heat != heat)
        {
            Heat = heat;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Heat)));
        }
        if (_baseTip != tip)
        {
            _baseTip = tip;
            PublishTip();
        }
    }

    public void SetKillLabel(string label)
    {
        if (KillLabel == label) return;
        KillLabel = label;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KillLabel)));
    }

    public void ApplyMonitor(bool show, WindowEntry entry, int hostMonitor, string thisScreen)
    {
        var label = show ? MonitorBadge.Label(entry.MonitorNumber, hostMonitor, thisScreen) : "";
        var visible = label.Length > 0;
        var tip = visible
            ? MonitorChrome.Format(entry.MonitorNumber, entry.MonitorWidth, entry.MonitorHeight, entry.MonitorPrimary, entry.MonitorFriendlyName)
            : "";
        if (MonitorLabel == label && _monitorTip == tip) return;
        var labelChanged = MonitorLabel != label;
        MonitorLabel = label;
        _monitorTip = tip;
        if (labelChanged)
        {
            if (visible)
            {
                MonitorBrush = MonitorChrome.Brush(entry.MonitorNumber, 0xFF);
                MonitorTint = MonitorChrome.Brush(entry.MonitorNumber, 0x28);
            }
            else
            {
                MonitorBrush = System.Windows.Media.Brushes.Transparent;
                MonitorTint = System.Windows.Media.Brushes.Transparent;
            }
        }
        PublishTip();
        if (!labelChanged) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MonitorLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MonitorBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MonitorTint)));
    }

    void PublishTip()
    {
        ToolTipText = string.IsNullOrEmpty(_monitorTip)
            ? _baseTip
            : string.IsNullOrEmpty(_baseTip) ? _monitorTip : _baseTip + "\n" + _monitorTip;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTipText)));
    }

    public ImageSource Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SwitcherViewState
{
    public required IReadOnlyList<WindowEntry> Filtered { get; init; }
    public int SelectedIndex { get; init; }
    public required string Query { get; init; }
    public required string Hint { get; init; }
    public required string CountText { get; init; }
    public string? Status { get; init; }
    public bool SearchMode { get; init; }
    public required string EmptyText { get; init; }
    public required IReadOnlyDictionary<long, ImageSource> Icons { get; init; }
    public IReadOnlyDictionary<long, TaskLoad> Loads { get; init; } = new Dictionary<long, TaskLoad>();
    public bool ShowLoad { get; init; }
    public string SystemText { get; init; } = "";
    public string SortText { get; init; } = "";
    public IReadOnlyList<string> GroupSections { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AppHit> Apps { get; init; } = Array.Empty<AppHit>();
    public IReadOnlyDictionary<long, string> Indexes { get; init; } = new Dictionary<long, string>();
    public IReadOnlyList<string> AppIndexes { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, ImageSource> AppIcons { get; init; } = new Dictionary<string, ImageSource>();
    public string LaunchSection { get; init; } = "";
    public string OpenBadge { get; init; } = "";
    public string SwitchSubtitle { get; init; } = "";
    public bool ShowMonitorBadges { get; init; }
    public bool MonitorFilterOn { get; init; }
    public string MonitorFilterText { get; init; } = "";
}

public partial class SwitcherWindow : Window
{
    readonly ObservableCollection<SwitcherRow> _rows = new();
    readonly ContextMenu _menu;
    readonly MenuItem _menuClose;
    readonly MenuItem _menuKill;
    readonly MenuItem _menuMin;
    readonly MenuItem _menuMax;
    readonly MenuItem _menuRestore;
    readonly MenuItem _menuKillTree;
    readonly MenuItem _menuLaunch;
    readonly MenuItem _menuAdmin;
    readonly MenuItem _menuLocation;
    readonly MenuItem _menuMove;
    readonly MenuItem _menuStay;
    readonly MenuItem _menuPin;
    readonly MenuItem _menuDemote;
    readonly MenuItem _menuRestorePriority;
    bool _menuForApp;
    Point _lastScreen = new(double.NaN, double.NaN);
    int _renderedSelection = -2;
    bool _internalEdit;
    int _compositionChars;
    int _queryChars;
    bool _searchMode;
    bool _guard;
    int _holdingActivation;
    nint _previousForeground;
    DateTime _shownAt;
    DateTime _suppressCancelUntil;
    DisplayBounds _host;
    string _thisScreen = "";
    bool _mouseGesture;

    public SwitcherWindow()
    {
        InitializeComponent();
        _menuClose = new MenuItem();
        _menuKill = new MenuItem();
        _menuMin = new MenuItem();
        _menuMax = new MenuItem();
        _menuRestore = new MenuItem();
        _menuKillTree = new MenuItem();
        _menuLaunch = new MenuItem();
        _menuAdmin = new MenuItem();
        _menuLocation = new MenuItem();
        _menuMove = new MenuItem();
        _menuStay = new MenuItem();
        _menuPin = new MenuItem();
        _menuDemote = new MenuItem();
        _menuRestorePriority = new MenuItem();
        _menuClose.Click += (_, _) => CloseRequested?.Invoke();
        _menuPin.Click += (_, _) => PinRequested?.Invoke();
        _menuDemote.Click += (_, _) => DemoteRequested?.Invoke();
        _menuRestorePriority.Click += (_, _) => RestorePriorityRequested?.Invoke();
        _menuLaunch.Click += (_, _) => { _mouseGesture = true; CommitRequested?.Invoke(); };
        _menuAdmin.Click += (_, _) => { _mouseGesture = true; AdminLaunchRequested?.Invoke(); };
        _menuLocation.Click += (_, _) => OpenLocationRequested?.Invoke();
        _menuKill.Click += (_, _) => KillRequested?.Invoke();
        _menuKillTree.Click += (_, _) => KillTreeRequested?.Invoke();
        _menuMin.Click += (_, _) => MinimizeRequested?.Invoke();
        _menuMax.Click += (_, _) => MaximizeRequested?.Invoke();
        _menuRestore.Click += (_, _) => RestoreRequested?.Invoke();
        _menu = new ContextMenu
        {
            Items =
            {
                _menuPin, _menuDemote, _menuRestorePriority, new Separator(),
                _menuLaunch, _menuAdmin, _menuLocation, _menuMove, _menuStay,
                _menuClose, _menuKill, _menuKillTree, new Separator(),
                _menuMin, _menuMax, _menuRestore
            }
        };
        _menuMove.Click += (_, _) => { _mouseGesture = true; ForceMoveRequested?.Invoke(); };
        _menuStay.Click += (_, _) => { _mouseGesture = true; CommitInPlaceRequested?.Invoke(); };
        _menu.Opened += (_, _) => LocalizeMenu();
        WindowList.ItemsSource = _rows;
        WindowList.ContextMenu = _menu;
        TextCompositionManager.AddPreviewTextInputStartHandler(SearchBox, (_, e) => NoteComposition(e));
        TextCompositionManager.AddPreviewTextInputUpdateHandler(SearchBox, (_, e) => NoteComposition(e));
        TextCompositionManager.AddPreviewTextInputHandler(SearchBox, (_, _) => ClearComposition());
        TextCompositionManager.AddTextInputStartHandler(SearchBox, (_, e) => NoteComposition(e));
        TextCompositionManager.AddTextInputUpdateHandler(SearchBox, (_, e) => NoteComposition(e));
        TextCompositionManager.AddTextInputHandler(SearchBox, (_, _) => ClearComposition());
        SearchBox.LostFocus += (_, _) => ClearComposition();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowChrome.ApplyToolWindow(hwnd);
            WindowChrome.ApplyBackdrop(hwnd, ThemeService.IsDark);
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(PreventBlackFrame);
        };
        ThemeService.Changed += dark => Dispatcher.BeginInvoke(() =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, dark);
        });
        ApplyStaticText();
        Localizer.Current.LanguageChanged += (_, _) =>
        {
            ApplyStaticText();
            if (!IsVisible) return;
            LabelsRequested?.Invoke();
        };
    }

    public nint Handle => new WindowInteropHelper(this).Handle;
    public event Action<long>? Hovered;
    public event Action<long>? Invoked;
    public event Action<string>? AppHovered;
    public event Action<string>? AppInvoked;
    public event Action? AdminLaunchRequested;
    public event Action? OpenLocationRequested;
    public event Action? CommitRequested;
    public event Action? CommitInPlaceRequested;
    public event Action? ForceMoveRequested;
    public event Action? ClearQueryRequested;
    public event Action? CancelRequested;
    /// <summary>Runs on the UI thread before this copy hides.</summary>
    public event Action? Releasing;
    public bool IsSearchMode => _searchMode;
    public int QueryCharCount => Volatile.Read(ref _queryChars);
    /// <summary>WPF composition text is non-empty. An idle IME, or TextInputStart with no string, is not composing.</summary>
    public bool IsImeComposing => Volatile.Read(ref _compositionChars) > 0;
    /// <summary>
    /// Esc goes to the IME only when a composition string exists.
    /// IMM with no context, and ImeProcessedKey on its own, do not count.
    /// </summary>
    public bool EscapeBelongsToIme() => IsImeComposing || ImeComposition.HasComposition(Handle);
    public event Action? SearchRequested;
    public event Action<string>? QueryEdited;
    public event Action<int>? NavigateRequested;
    public event Action? SpaceRequested;
    public event Action? CloseRequested;
    public event Action? KillRequested;
    public event Action? KillTreeRequested;
    public event Action? SortCycleRequested;
    public event Action? MonitorFilterRequested;
    public event Action? LabelsRequested;
    public event Action? MinimizeRequested;
    public event Action? MaximizeRequested;
    public event Action? RestoreRequested;
    public event Action? PinRequested;
    public event Action? DemoteRequested;
    public event Action? RestorePriorityRequested;

    public DisplayBounds Host => _host;
    public Func<nint, bool>? IsCompanion { get; set; }

    public void BindMonitor(DisplayBounds monitor, string thisScreen)
    {
        _host = monitor;
        if (!string.IsNullOrEmpty(thisScreen)) _thisScreen = thisScreen;
    }

    public void RememberForeground(nint hwnd) => _previousForeground = hwnd;

    public bool TakeMouse()
    {
        var mouse = _mouseGesture;
        _mouseGesture = false;
        return mouse;
    }

    public void PushGuard() => _guard = true;

    public void PopGuard() => _guard = false;

    public void Render(SwitcherViewState state)
    {
        _searchMode = state.SearchMode;
        SearchBox.IsReadOnly = !state.SearchMode;
        SearchBox.Focusable = state.SearchMode;
        if (SearchBox.Text != state.Query)
        {
            _internalEdit = true;
            SearchBox.Text = state.Query;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            _internalEdit = false;
        }
        Volatile.Write(ref _queryChars, SearchBox.Text?.Length ?? 0);
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        HintText.Text = state.Hint;
        CountText.Text = state.CountText;
        SystemText.Text = state.SystemText;
        SystemText.Visibility = state.ShowLoad ? Visibility.Visible : Visibility.Collapsed;
        SortButton.Visibility = state.ShowLoad ? Visibility.Visible : Visibility.Collapsed;
        SortButton.Content = state.SortText;
        DisplayFilterButton.Visibility = state.ShowMonitorBadges ? Visibility.Visible : Visibility.Collapsed;
        DisplayFilterButton.Content = state.MonitorFilterText;
        DisplayFilterButton.Background = state.MonitorFilterOn
            ? BrushOf("SelectedBrush")
            : BrushOf("ButtonBgBrush");
        DisplayFilterButton.BorderBrush = state.MonitorFilterOn
            ? BrushOf("AccentBrush")
            : BrushOf("ButtonBorderBrush");
        StatusText.Text = state.Status ?? "";
        EmptyText.Text = state.EmptyText;
        EmptyText.Visibility = string.IsNullOrEmpty(state.EmptyText) ? Visibility.Collapsed : Visibility.Visible;

        SyncRows(state);
        var kill = Localizer.Current["action.kill"];

        for (var i = 0; i < _rows.Count && i < state.Filtered.Count; i++)
        {
            var row = _rows[i];
            if (row.Entry is not { } entry) continue;
            row.SetKillLabel(kill);
            if (state.Indexes.TryGetValue(entry.Handle, out var index))
                row.SetIndex(index);
            row.SetSection(i < state.GroupSections.Count ? state.GroupSections[i] : "");
            row.SetSubtitle(string.IsNullOrWhiteSpace(entry.DisplayProcessName) ? entry.ProcessName : entry.DisplayProcessName);
            if (state.Icons.TryGetValue(entry.Handle, out var icon) && !ReferenceEquals(icon, row.Icon))
                row.Icon = icon;
            var load = state.Loads.TryGetValue(entry.Handle, out var found) ? found : default;
            row.ApplyLoad(state.ShowLoad, load);
            row.ApplyMonitor(state.ShowMonitorBadges, entry, _host.Number, _thisScreen);
        }
        for (var i = 0; i < state.Apps.Count; i++)
        {
            var rowIndex = state.Filtered.Count + i;
            if (rowIndex < 0 || rowIndex >= _rows.Count) break;
            var hit = state.Apps[i];
            if (state.AppIcons.TryGetValue(hit.App.Id, out var icon) && !ReferenceEquals(icon, _rows[rowIndex].Icon))
                _rows[rowIndex].Icon = icon;
        }

        var selectionChanged = _renderedSelection != state.SelectedIndex;
        if (WindowList.SelectedIndex != state.SelectedIndex)
            WindowList.SelectedIndex = state.SelectedIndex;
        if (selectionChanged && state.SelectedIndex >= 0 && state.SelectedIndex < _rows.Count)
            WindowList.ScrollIntoView(_rows[state.SelectedIndex]);
        _renderedSelection = state.SelectedIndex;

        var selectedApp = state.SelectedIndex >= state.Filtered.Count
            && state.SelectedIndex < state.Filtered.Count + state.Apps.Count
            ? state.Apps[state.SelectedIndex - state.Filtered.Count]
            : (AppHit?)null;
        _menuAdmin.IsEnabled = selectedApp is { } launch && launch.SwitchHandle == 0 && launch.App.Kind != AppKind.Store;
    }

    public void SuppressCancel(int milliseconds) =>
        _suppressCancelUntil = DateTime.UtcNow.AddMilliseconds(Math.Max(0, milliseconds));

    /// <summary>
    /// Ignore the deactivate that our own Activate / SetForegroundWindow raises.
    /// A later click on another window still closes the switcher.
    /// </summary>
    public void HoldActivation() => _holdingActivation++;

    public void ReleaseActivation()
    {
        if (_holdingActivation > 0) _holdingActivation--;
    }

    /// <summary>
    /// Place this instance on <see cref="Host"/> using that monitor's DPI.
    /// Only the anchor activates. The others use SWP_NOACTIVATE so they do not steal the keyboard.
    /// </summary>
    public void EnsureHwnd() => _ = new WindowInteropHelper(this).EnsureHandle();

    public void PlaceOffscreen(SwitcherSize size) => Place(size, activate: false, offscreen: true);

    public void Place(SwitcherSize size, bool activate) => Place(size, activate, offscreen: false);

    void Place(SwitcherSize size, bool activate, bool offscreen)
    {
        _shownAt = DateTime.UtcNow;
        var (width, height) = SwitcherMetrics.GetSize(size);
        Width = width;
        Height = height;
        var reveal = !IsVisible;
        if (reveal) Opacity = 0;
        ShowActivated = false;
        if (!IsVisible) Show();
        UpdateLayout();
        var monitor = _host;
        var scaleX = monitor.DpiX <= 0 ? 1d : monitor.DpiX / 96d;
        var scaleY = monitor.DpiY <= 0 ? 1d : monitor.DpiY / 96d;
        var physicalW = Math.Max(1, (int)Math.Round(width * scaleX));
        var physicalH = Math.Max(1, (int)Math.Round(height * scaleY));
        var hwnd = new WindowInteropHelper(this).Handle;
        if (offscreen)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndBottom, -32000, -32000, physicalW, physicalH, NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
            return;
        }

        ShowActivated = activate;
        var centered = RectangleMath.Center(
            new RectangleMath.RectI(monitor.X, monitor.Y, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height)),
            physicalW,
            physicalH);
        var flags = activate ? NativeMethods.SwpShowWindow : NativeMethods.SwpShowWindow | NativeMethods.SwpNoActivate;
        NativeMethods.SetWindowPos(hwnd, new nint(-1), centered.X, centered.Y, centered.Width, centered.Height, flags);
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var origin = target.TransformFromDevice.Transform(new Point(centered.X, centered.Y));
            Left = origin.X;
            Top = origin.Y;
        }
        if (reveal) Opacity = 1;
        if (!activate) return;
        Activate();
        if (NativeMethods.GetForegroundWindow() != hwnd)
            WindowActions.TryActivate(hwnd);
    }

    public void HideSwitcher()
    {
        if (IsVisible) Hide();
    }

    public void ForceClose()
    {
        _guard = true;
        Close();
    }

    public void RunGuarded(Action action)
    {
        _guard = true;
        try { action(); }
        finally { _guard = false; }
    }

    public void FocusSearch()
    {
        _searchMode = true;
        SearchBox.IsReadOnly = false;
        SearchBox.Focusable = true;
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    /// <summary>
    /// Take the foreground after the trigger key is released. SendInput has already run,
    /// which is what lets SetForegroundWindow succeed under the foreground lock.
    /// The deck posts a second attempt so the search box still receives IME keys if the
    /// first activation lands before the window is ready.
    /// </summary>
    public void ClaimKeyboard()
    {
        var hwnd = Handle;
        HoldActivation();
        try
        {
            if (hwnd != 0)
                WindowActions.TryActivate(hwnd);
            FocusSearch();
        }
        finally
        {
            ReleaseActivation();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Esc is handled here unless a composition string is actually present.
        // ImeProcessedKey with an empty composition is not composition: Chinese IMEs
        // raise it while the search box is merely focused.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape || e.ImeProcessedKey == Key.Escape)
        {
            if (EscapeBelongsToIme()) return;
            if (EscAction.Decide(imeComposing: false, hasQuery: !string.IsNullOrEmpty(SearchBox.Text)) == EscDecision.ClearQuery)
                ClearQueryRequested?.Invoke();
            else
                CancelRequested?.Invoke();
            e.Handled = true;
            return;
        }
        if (IsImeComposing) return;
        if (!_searchMode || e.ImeProcessedKey != Key.None) return;
        var mods = Keyboard.Modifiers;
        if (key is Key.LeftAlt or Key.RightAlt
            && !mods.HasFlag(WpfMods.Control)
            && !mods.HasFlag(WpfMods.Shift)
            && !mods.HasFlag(WpfMods.Windows))
        {
            // A bare Alt would open the window menu and steal the next character from the IME.
            // Alt combined with Shift or Ctrl (IME switching, AltGr) is left for the text box.
            e.Handled = true;
            return;
        }
        if (mods.HasFlag(WpfMods.Control))
        {
            switch (key)
            {
                case Key.W:
                    CloseRequested?.Invoke();
                    e.Handled = true;
                    return;
                case Key.M when mods.HasFlag(WpfMods.Shift):
                    MaximizeRequested?.Invoke();
                    e.Handled = true;
                    return;
                case Key.M:
                    MinimizeRequested?.Invoke();
                    e.Handled = true;
                    return;
                case Key.R:
                    RestoreRequested?.Invoke();
                    e.Handled = true;
                    return;
            }
        }

        switch (key)
        {
            case Key.Enter when mods.HasFlag(WpfMods.Control):
                AdminLaunchRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Enter when mods.HasFlag(WpfMods.Shift):
                CommitInPlaceRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Enter:
                CommitRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Up:
            case Key.Left:
                NavigateRequested?.Invoke(-1);
                e.Handled = true;
                break;
            case Key.Down:
            case Key.Right:
                NavigateRequested?.Invoke(1);
                e.Handled = true;
                break;
            case Key.Home:
                NavigateRequested?.Invoke(int.MinValue);
                e.Handled = true;
                break;
            case Key.End:
                NavigateRequested?.Invoke(int.MaxValue);
                e.Handled = true;
                break;
            case Key.Prior:
                NavigateRequested?.Invoke(-SwitcherSession.PageSize);
                e.Handled = true;
                break;
            case Key.Next:
                NavigateRequested?.Invoke(SwitcherSession.PageSize);
                e.Handled = true;
                break;
            case Key.Tab:
                NavigateRequested?.Invoke(mods.HasFlag(WpfMods.Shift) ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Space when mods == WpfMods.None && string.IsNullOrEmpty(SearchBox.Text):
                SpaceRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Delete when mods.HasFlag(WpfMods.Shift) && !mods.HasFlag(WpfMods.Control):
                KillRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Delete when !mods.HasFlag(WpfMods.Control) && !mods.HasFlag(WpfMods.Alt):
                CloseRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.D when mods.HasFlag(WpfMods.Control) && mods.HasFlag(WpfMods.Shift) && !mods.HasFlag(WpfMods.Alt):
                MonitorFilterRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_guard || !IsVisible) return;
        var foreground = NativeMethods.GetForegroundWindow();
        var age = (int)(DateTime.UtcNow - _shownAt).TotalMilliseconds;
        if (!SwitcherCleanup.ShouldDismiss(
                (long)foreground,
                (long)Handle,
                IsCompanion?.Invoke(foreground) == true,
                _holdingActivation > 0,
                (long)_previousForeground,
                age,
                SwitcherCleanup.IsImeUiClass(ClassNameOf(foreground))))
            return;
        Releasing?.Invoke();
        CancelRequested?.Invoke();
    }

    /// <summary>
    /// A borderless window's default non-client paint fills the client black when it deactivates.
    /// </summary>
    static nint PreventBlackFrame(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != 0x0086) return 0;
        handled = true;
        return 1;
    }

    protected override void OnClosed(EventArgs e) => base.OnClosed(e);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_guard)
        {
            e.Cancel = true;
            Releasing?.Invoke();
            CancelRequested?.Invoke();
            return;
        }
        base.OnClosing(e);
    }

    void ApplyStaticText()
    {
        var loc = Localizer.Current;
        SearchPlaceholder.Text = loc["search.placeholder"];
        _thisScreen = loc["monitor.thisScreen"];
        var kill = loc["action.kill"];
        foreach (var row in _rows)
            row.SetKillLabel(row.IsApp ? "" : kill);
        DisplayFilterButton.Content = loc["monitor.only"];
        DisplayFilterButton.ToolTip = loc["monitor.onlyTip"];
        LocalizeMenu();
    }

    void Sort_Click(object sender, RoutedEventArgs e) => SortCycleRequested?.Invoke();

    void DisplayFilter_Click(object sender, RoutedEventArgs e) => MonitorFilterRequested?.Invoke();

    System.Windows.Media.Brush BrushOf(string key) =>
        TryFindResource(key) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Transparent;

    void LocalizeMenu()
    {
        var loc = Localizer.Current;
        _menuClose.Header = loc["menu.close"];
        _menuKill.Header = loc["menu.kill"];
        _menuKillTree.Header = loc["menu.killTree"];
        _menuMin.Header = loc["menu.minimize"];
        _menuMax.Header = loc["menu.maximize"];
        _menuRestore.Header = loc["menu.restore"];
        var switching = WindowList.SelectedItem is SwitcherRow { IsApp: true, Badge.Length: > 0 };
        _menuLaunch.Header = loc[switching ? "launch.switch" : "launch.open"];
        _menuAdmin.Header = loc["launch.admin"];
        _menuLocation.Header = loc["launch.location"];
        _menuMove.Header = loc["menu.moveHere"];
        _menuStay.Header = loc["menu.stay"];
        _menuPin.Header = loc["menu.pin"];
        _menuDemote.Header = loc["menu.demote"];
        _menuRestorePriority.Header = loc["menu.restorePriority"];
        var app = _menuForApp ? Visibility.Visible : Visibility.Collapsed;
        var window = _menuForApp ? Visibility.Collapsed : Visibility.Visible;
        _menuLaunch.Visibility = app;
        _menuAdmin.Visibility = app;
        _menuLocation.Visibility = app;
        _menuClose.Visibility = window;
        _menuKill.Visibility = window;
        _menuKillTree.Visibility = window;
        _menuMin.Visibility = window;
        _menuMax.Visibility = window;
        _menuRestore.Visibility = window;
        _menuPin.Visibility = window;
        _menuDemote.Visibility = window;
        _menuRestorePriority.Visibility = window;
    }

    void SyncRows(SwitcherViewState state)
    {
        var windowCount = WindowRowCount();
        if (!SameWindowOrder(state, windowCount))
        {
            if (SharesRelativeOrder(state, windowCount))
                EditWindowRows(state, windowCount);
            else
                ReplaceWindowRows(state);
        }

        SyncAppRows(state, WindowRowCount());
    }

    int WindowRowCount()
    {
        var count = 0;
        while (count < _rows.Count && !_rows[count].IsApp)
            count++;
        return count;
    }

    bool SameWindowOrder(SwitcherViewState state, int windowCount)
    {
        if (windowCount != state.Filtered.Count) return false;
        for (var i = 0; i < windowCount; i++)
        {
            if (_rows[i].Entry?.Handle != state.Filtered[i].Handle) return false;
        }
        return true;
    }

    bool SharesRelativeOrder(SwitcherViewState state, int windowCount)
    {
        var want = new HashSet<long>();
        foreach (var entry in state.Filtered)
            want.Add(entry.Handle);
        var current = new HashSet<long>();
        var kept = new List<long>();
        for (var i = 0; i < windowCount; i++)
        {
            var handle = _rows[i].Entry?.Handle ?? 0;
            current.Add(handle);
            if (want.Contains(handle)) kept.Add(handle);
        }
        var fromDesired = new List<long>();
        foreach (var entry in state.Filtered)
        {
            if (current.Contains(entry.Handle)) fromDesired.Add(entry.Handle);
        }
        if (kept.Count != fromDesired.Count) return false;
        for (var i = 0; i < kept.Count; i++)
        {
            if (kept[i] != fromDesired[i]) return false;
        }
        return true;
    }

    void EditWindowRows(SwitcherViewState state, int windowCount)
    {
        var wanted = new HashSet<long>(state.Filtered.Select(entry => entry.Handle));
        var desired = 0;
        var row = 0;
        while (desired < state.Filtered.Count || row < windowCount)
        {
            var current = row < windowCount ? _rows[row].Entry?.Handle ?? 0 : 0;
            var matches = row < windowCount
                && desired < state.Filtered.Count
                && current == state.Filtered[desired].Handle;
            if (matches)
            {
                desired++;
                row++;
                continue;
            }
            if (row < windowCount && (desired >= state.Filtered.Count || !wanted.Contains(current)))
            {
                _rows.RemoveAt(row);
                windowCount--;
                continue;
            }
            InsertWindowRow(state, desired, row);
            desired++;
            row++;
            windowCount++;
        }
    }

    void ReplaceWindowRows(SwitcherViewState state)
    {
        var windowCount = WindowRowCount();
        for (var i = windowCount - 1; i >= 0; i--)
            _rows.RemoveAt(i);
        for (var i = 0; i < state.Filtered.Count; i++)
            InsertWindowRow(state, i, i);
    }

    void InsertWindowRow(SwitcherViewState state, int filteredIndex, int rowIndex)
    {
        var entry = state.Filtered[filteredIndex];
        state.Icons.TryGetValue(entry.Handle, out var icon);
        state.Indexes.TryGetValue(entry.Handle, out var index);
        var section = filteredIndex < state.GroupSections.Count ? state.GroupSections[filteredIndex] : "";
        var row = new SwitcherRow(entry, icon ?? IconLoader.Letter(entry.Title), index ?? "", section);
        row.SetKillLabel(Localizer.Current["action.kill"]);
        row.ApplyLoad(state.ShowLoad, default);
        row.ApplyMonitor(state.ShowMonitorBadges, entry, _host.Number, _thisScreen);
        _rows.Insert(rowIndex, row);
    }

    void SyncAppRows(SwitcherViewState state, int windowCount)
    {
        var same = _rows.Count - windowCount == state.Apps.Count;
        if (same)
        {
            for (var i = 0; i < state.Apps.Count; i++)
            {
                var row = _rows[windowCount + i];
                if (!string.Equals(row.AppId, state.Apps[i].App.Id, StringComparison.OrdinalIgnoreCase))
                {
                    same = false;
                    break;
                }
                if ((row.Badge.Length > 0) != (state.Apps[i].SwitchHandle != 0))
                {
                    same = false;
                    break;
                }
                var wantIndex = i < state.AppIndexes.Count ? state.AppIndexes[i] : "";
                if (!string.Equals(row.IndexLabel, wantIndex, StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }
        }
        if (same) return;
        while (_rows.Count > windowCount)
            _rows.RemoveAt(_rows.Count - 1);
        for (var i = 0; i < state.Apps.Count; i++)
        {
            var hit = state.Apps[i];
            state.AppIcons.TryGetValue(hit.App.Id, out var icon);
            var subtitle = hit.SwitchHandle != 0
                ? state.SwitchSubtitle
                : string.IsNullOrWhiteSpace(hit.App.ProcessName) ? hit.App.Name : hit.App.ProcessName;
            _rows.Add(new SwitcherRow(
                hit,
                icon ?? IconLoader.Letter(hit.App.Name),
                i == 0 ? state.LaunchSection : "",
                hit.SwitchHandle != 0 ? state.OpenBadge : "",
                subtitle,
                i < state.AppIndexes.Count ? state.AppIndexes[i] : ""));
        }
    }

    SwitcherRow? RowFrom(object sender)
    {
        if (sender is ListBoxItem item && item.DataContext is SwitcherRow row) return row;
        return null;
    }

    SwitcherRow? RowAt(MouseEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);
        return element is ListBoxItem item ? item.DataContext as SwitcherRow : null;
    }

    void Item_MouseMove(object sender, MouseEventArgs e)
    {
        var screen = PointToScreen(e.GetPosition(this));
        if (Math.Abs(screen.X - _lastScreen.X) < 1 && Math.Abs(screen.Y - _lastScreen.Y) < 1) return;
        _lastScreen = screen;
        if (RowFrom(sender) is not { } row) return;
        if (row.Entry is { } entry) Hovered?.Invoke(entry.Handle);
        else if (row.AppId is { } id) AppHovered?.Invoke(id);
    }

    void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (RowFrom(sender) is not { } row) return;
        if (IsKillClick(e.OriginalSource))
        {
            if (row.Entry is { } kill)
            {
                Hovered?.Invoke(kill.Handle);
                KillRequested?.Invoke();
            }
            e.Handled = true;
            return;
        }
        if (row.Entry is { } entry) Invoked?.Invoke(entry.Handle);
        else if (row.AppId is { } id) AppInvoked?.Invoke(id);
        e.Handled = true;
    }

    static bool IsKillClick(object? source)
    {
        var element = source as DependencyObject;
        while (element is not null)
        {
            if (element is FrameworkElement { Tag: "kill" }) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e) is not { } row) return;
        _menuForApp = row.IsApp;
        if (row.Entry is { } entry) Hovered?.Invoke(entry.Handle);
        else if (row.AppId is { } id) AppHovered?.Invoke(id);
    }

    void List_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if (RowAt(e) is not { } row || row.Entry is null) return;
        Hovered?.Invoke(row.Entry.Handle);
        CloseRequested?.Invoke();
        e.Handled = true;
    }

    void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        NavigateRequested?.Invoke(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    void SearchBox_PreviewMouseDown(object sender, MouseButtonEventArgs e) => SearchRequested?.Invoke();

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text ?? "";
        Volatile.Write(ref _queryChars, text.Length);
        SearchPlaceholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_internalEdit) return;
        QueryEdited?.Invoke(text);
    }

    void NoteComposition(TextCompositionEventArgs e)
    {
        var text = e.TextComposition?.CompositionText;
        Volatile.Write(ref _compositionChars, string.IsNullOrEmpty(text) ? 0 : text.Length);
    }

    void ClearComposition() => Volatile.Write(ref _compositionChars, 0);

    static string ClassNameOf(nint hwnd)
    {
        if (hwnd == 0) return "";
        var buffer = new System.Text.StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }

    void MenuClose_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    void MenuKill_Click(object sender, RoutedEventArgs e) => KillRequested?.Invoke();
    void MenuMin_Click(object sender, RoutedEventArgs e) => MinimizeRequested?.Invoke();
    void MenuMax_Click(object sender, RoutedEventArgs e) => MaximizeRequested?.Invoke();
    void MenuRestore_Click(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke();
}
