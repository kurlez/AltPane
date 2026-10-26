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

    public SwitcherRow(WindowEntry entry, ImageSource icon, string index)
    {
        Entry = entry;
        _icon = icon;
        Title = entry.Title;
        Subtitle = string.IsNullOrWhiteSpace(entry.DisplayProcessName) ? entry.ProcessName : entry.DisplayProcessName;
        IndexLabel = index;
    }

    public SwitcherRow(AppHit hit, ImageSource icon, string section, string badge, string subtitle, string index)
    {
        AppHit = hit;
        _icon = icon;
        Title = hit.App.Name;
        Subtitle = subtitle;
        Section = section;
        Badge = badge;
        IndexLabel = index;
    }

    public WindowEntry? Entry { get; }
    public AppHit? AppHit { get; }
    public bool IsApp => AppHit is not null;
    public string? AppId => AppHit?.App.Id;
    public string Title { get; }
    public string Subtitle { get; }
    public string Section { get; } = "";
    public string Badge { get; } = "";
    public string IndexLabel { get; private set; } = "";

    public void SetIndex(string index)
    {
        if (IndexLabel == index) return;
        IndexLabel = index ?? "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IndexLabel)));
    }
    public string LoadText { get; private set; } = "";
    public double BarWidth { get; private set; }
    public int Heat { get; private set; }
    public bool ShowLoad { get; private set; }
    public string ToolTipText { get; private set; } = "";
    public string MonitorLabel { get; private set; } = "";
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

    public void ApplyMonitor(bool show, WindowEntry entry)
    {
        var visible = show && entry.MonitorNumber > 0;
        var label = visible ? entry.MonitorNumber.ToString() : "";
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
    public bool ShowPreview { get; init; }
    public bool SearchMode { get; init; }
    public required string EmptyText { get; init; }
    public required IReadOnlyDictionary<long, ImageSource> Icons { get; init; }
    public IReadOnlyDictionary<long, TaskLoad> Loads { get; init; } = new Dictionary<long, TaskLoad>();
    public bool ShowLoad { get; init; }
    public string SystemText { get; init; } = "";
    public string SortText { get; init; } = "";
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
    readonly MenuItem _menuPreview;
    readonly MenuItem _menuLaunch;
    readonly MenuItem _menuAdmin;
    readonly MenuItem _menuLocation;
    readonly MenuItem _menuMove;
    readonly MenuItem _menuStay;
    bool _menuForApp;
    Point _lastScreen = new(double.NaN, double.NaN);
    int _renderedSelection = -2;
    bool _internalEdit;
    bool _composing;
    bool _searchMode;
    bool _guard;
    int _holdingActivation;
    nint _previousForeground;
    DateTime _shownAt;
    DateTime _suppressCancelUntil;
    readonly HintOverlay _hints = new();
    bool _showHints = true;
    bool _showBadges = true;

    public SwitcherWindow()
    {
        InitializeComponent();
        _menuClose = new MenuItem();
        _menuKill = new MenuItem();
        _menuMin = new MenuItem();
        _menuMax = new MenuItem();
        _menuRestore = new MenuItem();
        _menuKillTree = new MenuItem();
        _menuPreview = new MenuItem();
        _menuLaunch = new MenuItem();
        _menuAdmin = new MenuItem();
        _menuLocation = new MenuItem();
        _menuMove = new MenuItem();
        _menuStay = new MenuItem();
        _menuClose.Click += (_, _) => CloseRequested?.Invoke();
        _menuLaunch.Click += (_, _) => CommitRequested?.Invoke();
        _menuAdmin.Click += (_, _) => AdminLaunchRequested?.Invoke();
        _menuLocation.Click += (_, _) => OpenLocationRequested?.Invoke();
        _menuKill.Click += (_, _) => KillRequested?.Invoke();
        _menuKillTree.Click += (_, _) => KillTreeRequested?.Invoke();
        _menuPreview.Click += (_, _) => PreviewRequested?.Invoke();
        _menuMin.Click += (_, _) => MinimizeRequested?.Invoke();
        _menuMax.Click += (_, _) => MaximizeRequested?.Invoke();
        _menuRestore.Click += (_, _) => RestoreRequested?.Invoke();
        _menu = new ContextMenu { Items = { _menuLaunch, _menuAdmin, _menuLocation, _menuMove, _menuStay, _menuClose, _menuKill, _menuKillTree, _menuPreview, new Separator(), _menuMin, _menuMax, _menuRestore } };
        _menuMove.Click += (_, _) => ForceMoveRequested?.Invoke();
        _menuStay.Click += (_, _) => CommitInPlaceRequested?.Invoke();
        _menu.Opened += (_, _) => LocalizeMenu();
        WindowList.ItemsSource = _rows;
        WindowList.ContextMenu = _menu;
        TextCompositionManager.AddTextInputStartHandler(SearchBox, (_, _) => _composing = true);
        TextCompositionManager.AddTextInputHandler(SearchBox, (_, _) => _composing = false);
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
            SyncHints();
            LabelsRequested?.Invoke();
        };
        _hints.DisplayChanged += () =>
        {
            if (IsVisible) SyncHints(allowRelocate: true);
            DisplaysChanged?.Invoke();
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
    /// <summary>Runs on the UI thread before the switcher hides, so thumbnails can be released first.</summary>
    public event Action? Releasing;
    public bool IsImeComposing => _composing;
    public event Action? SearchRequested;
    public event Action<string>? QueryEdited;
    public event Action<int>? NavigateRequested;
    public event Action? SpaceRequested;
    public event Action? CloseRequested;
    public event Action? KillRequested;
    public event Action? KillTreeRequested;
    public event Action? SortCycleRequested;
    public event Action? MonitorFilterRequested;
    public event Action? DisplaysChanged;
    public event Action? HostMonitorChanged;
    public event Action? LabelsRequested;
    public event Action? PreviewRequested;
    public event Action? MinimizeRequested;
    public event Action? MaximizeRequested;
    public event Action? RestoreRequested;
    public event Action? LayoutChanged;

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

        PreviewColumn.Width = state.ShowPreview ? new GridLength(0.58, GridUnitType.Star) : new GridLength(0);
        ListColumn.Width = state.ShowPreview ? new GridLength(0.42, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        PreviewRoot.Visibility = state.ShowPreview ? Visibility.Visible : Visibility.Collapsed;

        SyncRows(state);

        for (var i = 0; i < _rows.Count && i < state.Filtered.Count; i++)
        {
            var row = _rows[i];
            if (row.Entry is not { } entry) continue;
            if (state.Indexes.TryGetValue(entry.Handle, out var index))
                row.SetIndex(index);
            if (state.Icons.TryGetValue(entry.Handle, out var icon) && !ReferenceEquals(icon, row.Icon))
                row.Icon = icon;
            var load = state.Loads.TryGetValue(entry.Handle, out var found) ? found : default;
            row.ApplyLoad(state.ShowLoad, load);
            row.ApplyMonitor(state.ShowMonitorBadges, entry);
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

        var selected = state.SelectedIndex >= 0 && state.SelectedIndex < state.Filtered.Count
            ? state.Filtered[state.SelectedIndex]
            : null;
        var selectedApp = state.SelectedIndex >= state.Filtered.Count
            && state.SelectedIndex < state.Filtered.Count + state.Apps.Count
            ? state.Apps[state.SelectedIndex - state.Filtered.Count]
            : (AppHit?)null;
        PreviewTitle.Text = selected?.Title ?? selectedApp?.App.Name ?? "";
        PreviewProcess.Text = selected is not null
            ? $"{selected.DisplayProcessName}  ·  pid {selected.KillProcessId}"
            : selectedApp is { } app
                ? (app.SwitchHandle != 0 ? state.SwitchSubtitle : app.App.ProcessName)
                : "";
        var monitorLine = selected is not null && state.ShowMonitorBadges
            ? MonitorChrome.Format(selected.MonitorNumber, selected.MonitorWidth, selected.MonitorHeight, selected.MonitorPrimary, selected.MonitorFriendlyName)
            : "";
        PreviewMonitor.Text = monitorLine;
        PreviewMonitor.Visibility = string.IsNullOrEmpty(monitorLine) ? Visibility.Collapsed : Visibility.Visible;
        var enabled = selected is not null;
        MinButton.IsEnabled = enabled;
        MaxButton.IsEnabled = enabled;
        RestoreButton.IsEnabled = enabled;
        CloseButton.IsEnabled = enabled;
        KillButton.IsEnabled = enabled;
        _menuPreview.IsEnabled = selected?.IsIconic == true;
        _menuAdmin.IsEnabled = selectedApp is { } launch && launch.SwitchHandle == 0 && launch.App.Kind != AppKind.Store;
    }

    public void SetPreviewMessage(string message)
    {
        PreviewPlaceholder.Text = message;
        PreviewPlaceholder.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowStillPreview(ImageSource? image, string? badge)
    {
        CachedPreview.Source = image;
        CachedPreview.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        MinimizedBadgeText.Text = badge ?? "";
        MinimizedBadge.Visibility = image is null || string.IsNullOrEmpty(badge) ? Visibility.Collapsed : Visibility.Visible;
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

    public void ShowFor(AppSettings settings, nint activeWindow)
    {
        _shownAt = DateTime.UtcNow;
        var (width, height) = SwitcherMetrics.GetSize(settings.Size);
        Width = width;
        Height = height;
        var reveal = !IsVisible;
        if (reveal) Opacity = 0;
        if (!IsVisible) Show();
        UpdateLayout();
        var area = MonitorUtil.GetWorkArea(settings.Placement, activeWindow);
        var scaleX = area.DpiX / 96.0;
        var scaleY = area.DpiY / 96.0;
        var physicalW = Math.Max(1, (int)Math.Round(width * scaleX));
        var physicalH = Math.Max(1, (int)Math.Round(height * scaleY));
        var centered = RectangleMath.Center(new RectangleMath.RectI(area.X, area.Y, area.Width, area.Height), physicalW, physicalH);
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, new nint(-1), centered.X, centered.Y, centered.Width, centered.Height, 0x0040);
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var origin = target.TransformFromDevice.Transform(new Point(centered.X, centered.Y));
            Left = origin.X;
            Top = origin.Y;
        }
        if (reveal) Opacity = 1;
        _previousForeground = NativeMethods.GetForegroundWindow();
        HoldActivation();
        try
        {
            Activate();
            if (NativeMethods.GetForegroundWindow() != hwnd)
                WindowActions.TryActivate(hwnd);
        }
        finally
        {
            ReleaseActivation();
        }
        _showHints = settings.ShowOtherMonitorHints;
        _showBadges = settings.ShowMonitorBadges;
        SyncHints(centered, allowRelocate: false);
    }

    public void HideSwitcher()
    {
        _hints.Hide();
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
    /// A second attempt is posted so the search box still receives IME keys if the
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
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsVisible || !_searchMode) return;
            HoldActivation();
            try
            {
                var current = Handle;
                if (current != 0 && NativeMethods.GetForegroundWindow() != current)
                    WindowActions.TryActivate(current);
                FocusSearch();
            }
            finally
            {
                ReleaseActivation();
            }
        }));
    }

    public bool TryGetThumbBounds(out RectangleMath.RectI bounds)
    {
        bounds = default;
        if (!IsVisible || ThumbHost.ActualWidth < 4 || ThumbHost.ActualHeight < 4) return false;
        var origin = ThumbHost.TranslatePoint(new Point(1, 1), this);
        var dpi = VisualTreeHelper.GetDpi(this);
        bounds = RectangleMath.DipToPhysical(
            (int)Math.Round(origin.X),
            (int)Math.Round(origin.Y),
            Math.Max(1, (int)Math.Round(ThumbHost.ActualWidth - 2)),
            Math.Max(1, (int)Math.Round(ThumbHost.ActualHeight - 2)),
            dpi.DpiScaleX,
            dpi.DpiScaleY);
        return bounds.Width > 2 && bounds.Height > 2;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // A composition belongs to the IME, including its Esc. Anything else is handled
        // here, before a focused child (the search box, a button) can drop the key.
        if (_composing) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape || e.ImeProcessedKey == Key.Escape)
        {
            if (EscAction.Decide(imeComposing: false, hasQuery: !string.IsNullOrEmpty(SearchBox.Text)) == EscDecision.ClearQuery)
                ClearQueryRequested?.Invoke();
            else
                CancelRequested?.Invoke();
            e.Handled = true;
            return;
        }
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
                _hints.IsOurWindow(foreground),
                _holdingActivation > 0,
                (long)_previousForeground,
                age))
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

    protected override void OnClosed(EventArgs e)
    {
        _hints.Dispose();
        base.OnClosed(e);
    }

    void SyncHints(RectangleMath.RectI? known = null, bool allowRelocate = true)
    {
        if (!_showHints || !IsVisible)
        {
            _hints.Hide();
            return;
        }

        var monitors = MonitorUtil.All();
        int left, top, width, height;
        if (known is { } placed)
        {
            left = placed.X;
            top = placed.Y;
            width = placed.Width;
            height = placed.Height;
        }
        else if (NativeMethods.GetWindowRect(Handle, out var rect))
        {
            left = rect.Left;
            top = rect.Top;
            width = Math.Max(1, rect.Right - rect.Left);
            height = Math.Max(1, rect.Bottom - rect.Top);
        }
        else
        {
            _hints.Hide();
            return;
        }

        var host = OtherMonitorHints.HostIndex(monitors, left, top, width, height);
        if (host < 0 && allowRelocate && monitors.Count > 0)
        {
            MoveToMonitor(monitors[0]);
            return;
        }

        var plans = OtherMonitorHints.Plan(monitors, host);
        _hints.Show(plans, monitors, MoveToMonitor, colorCode: _showBadges && monitors.Count >= 2);
    }

    public bool TryGetHostMonitor(out DisplayBounds monitor)
    {
        monitor = default;
        var monitors = MonitorUtil.All();
        if (monitors.Count == 0 || Handle == 0) return false;
        if (!NativeMethods.GetWindowRect(Handle, out var rect)) return false;
        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var host = OtherMonitorHints.HostIndex(monitors, rect.Left, rect.Top, width, height);
        if (host < 0 || host >= monitors.Count) return false;
        monitor = monitors[host];
        return true;
    }

    public int HostMonitorNumber()
    {
        var monitors = MonitorUtil.All();
        if (monitors.Count == 0 || Handle == 0) return 0;
        if (!NativeMethods.GetWindowRect(Handle, out var rect)) return 0;
        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var host = OtherMonitorHints.HostIndex(monitors, rect.Left, rect.Top, width, height);
        return host >= 0 && host < monitors.Count ? monitors[host].Number : 0;
    }

    void MoveToMonitor(int index)
    {
        var monitors = MonitorUtil.All();
        if (index < 0 || index >= monitors.Count) return;
        MoveToMonitor(monitors[index]);
    }

    void MoveToMonitor(DisplayBounds monitor)
    {
        HoldActivation();
        var scaleX = monitor.DpiX <= 0 ? 1d : monitor.DpiX / 96d;
        var scaleY = monitor.DpiY <= 0 ? 1d : monitor.DpiY / 96d;
        var physicalW = Math.Max(1, (int)Math.Round(Width * scaleX));
        var physicalH = Math.Max(1, (int)Math.Round(Height * scaleY));
        var centered = RectangleMath.Center(
            new RectangleMath.RectI(monitor.X, monitor.Y, monitor.Width, monitor.Height),
            physicalW,
            physicalH);
        var hwnd = Handle;
        NativeMethods.SetWindowPos(hwnd, new nint(-1), centered.X, centered.Y, centered.Width, centered.Height, 0x0010 | 0x0040);
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var origin = target.TransformFromDevice.Transform(new Point(centered.X, centered.Y));
            Left = origin.X;
            Top = origin.Y;
        }
        try
        {
            if (NativeMethods.GetForegroundWindow() != hwnd)
                Activate();
            if (_searchMode) FocusSearch();
        }
        finally
        {
            ReleaseActivation();
        }
        SyncHints(centered, allowRelocate: false);
        HostMonitorChanged?.Invoke();
    }

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

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        LayoutChanged?.Invoke();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        LayoutChanged?.Invoke();
    }

    void ApplyStaticText()
    {
        var loc = Localizer.Current;
        SearchPlaceholder.Text = loc["search.placeholder"];
        MinButton.Content = loc["action.minimize"];
        MaxButton.Content = loc["action.maximize"];
        RestoreButton.Content = loc["action.restore"];
        CloseButton.Content = loc["action.close"];
        KillButton.Content = loc["action.kill"];
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
        _menuPreview.Header = loc["menu.preview"];
        _menuMin.Header = loc["menu.minimize"];
        _menuMax.Header = loc["menu.maximize"];
        _menuRestore.Header = loc["menu.restore"];
        var switching = WindowList.SelectedItem is SwitcherRow { IsApp: true, Badge.Length: > 0 };
        _menuLaunch.Header = loc[switching ? "launch.switch" : "launch.open"];
        _menuAdmin.Header = loc["launch.admin"];
        _menuLocation.Header = loc["launch.location"];
        _menuMove.Header = loc["menu.moveHere"];
        _menuStay.Header = loc["menu.stay"];
        var app = _menuForApp ? Visibility.Visible : Visibility.Collapsed;
        var window = _menuForApp ? Visibility.Collapsed : Visibility.Visible;
        _menuLaunch.Visibility = app;
        _menuAdmin.Visibility = app;
        _menuLocation.Visibility = app;
        _menuClose.Visibility = window;
        _menuKill.Visibility = window;
        _menuKillTree.Visibility = window;
        _menuPreview.Visibility = window;
        _menuMin.Visibility = window;
        _menuMax.Visibility = window;
        _menuRestore.Visibility = window;
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
        var row = new SwitcherRow(entry, icon ?? IconLoader.Letter(entry.Title), index ?? "");
        row.ApplyLoad(state.ShowLoad, default);
        row.ApplyMonitor(state.ShowMonitorBadges, entry);
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
        if (row.Entry is { } entry) Invoked?.Invoke(entry.Handle);
        else if (row.AppId is { } id) AppInvoked?.Invoke(id);
        e.Handled = true;
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
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_internalEdit) return;
        QueryEdited?.Invoke(SearchBox.Text);
    }

    void MenuClose_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    void MenuKill_Click(object sender, RoutedEventArgs e) => KillRequested?.Invoke();
    void MenuMin_Click(object sender, RoutedEventArgs e) => MinimizeRequested?.Invoke();
    void MenuMax_Click(object sender, RoutedEventArgs e) => MaximizeRequested?.Invoke();
    void MenuRestore_Click(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke();
}
