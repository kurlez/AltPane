using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public SwitcherRow(WindowEntry entry, ImageSource icon)
    {
        Entry = entry;
        _icon = icon;
    }

    public WindowEntry Entry { get; }
    public string Title => Entry.Title;
    public string Subtitle => string.IsNullOrWhiteSpace(Entry.DisplayProcessName) ? Entry.ProcessName : Entry.DisplayProcessName;
    public string LoadText { get; private set; } = "";
    public double BarWidth { get; private set; }
    public int Heat { get; private set; }
    public bool ShowLoad { get; private set; }
    public string ToolTipText { get; private set; } = "";

    public void ApplyLoad(bool show, in TaskLoad load)
    {
        ShowLoad = show;
        if (!show)
        {
            LoadText = "";
            BarWidth = 0;
            Heat = 0;
        }
        else if (load.ProcessCount <= 0)
        {
            LoadText = "—";
            BarWidth = 0;
            Heat = 0;
        }
        else
        {
            LoadText = LoadFormat.Row(load);
            var cpu = load.CpuPercent ?? 0;
            BarWidth = Math.Clamp(cpu, 0, 100) / 100.0 * 40.0;
            Heat = (int)LoadHeatMap.Classify(load.CpuPercent, load.WorkingSetBytes);
        }
        ToolTipText = show && load.ProcessCount > 0
            ? $"{Entry.Title}\n{Subtitle}  ·  pid {Entry.KillProcessId}\n{LoadFormat.Cpu(load.CpuPercent)}  ·  {LoadFormat.Bytes(load.WorkingSetBytes)}  ·  {LoadFormat.Bytes(load.PrivateBytes)}"
            : $"{Entry.Title}\n{Subtitle}  ·  pid {Entry.KillProcessId}";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BarWidth)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Heat)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLoad)));
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
}

public partial class SwitcherWindow : Window
{
    readonly List<SwitcherRow> _rows = new();
    readonly ContextMenu _menu;
    readonly MenuItem _menuClose;
    readonly MenuItem _menuKill;
    readonly MenuItem _menuMin;
    readonly MenuItem _menuMax;
    readonly MenuItem _menuRestore;
    readonly MenuItem _menuKillTree;
    readonly MenuItem _menuPreview;
    Point _lastScreen = new(double.NaN, double.NaN);
    int _renderedSelection = -2;
    bool _internalEdit;
    bool _composing;
    bool _searchMode;
    bool _guard;
    DateTime _shownAt;
    DateTime _suppressCancelUntil;

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
        _menuClose.Click += (_, _) => CloseRequested?.Invoke();
        _menuKill.Click += (_, _) => KillRequested?.Invoke();
        _menuKillTree.Click += (_, _) => KillTreeRequested?.Invoke();
        _menuPreview.Click += (_, _) => PreviewRequested?.Invoke();
        _menuMin.Click += (_, _) => MinimizeRequested?.Invoke();
        _menuMax.Click += (_, _) => MaximizeRequested?.Invoke();
        _menuRestore.Click += (_, _) => RestoreRequested?.Invoke();
        _menu = new ContextMenu { Items = { _menuClose, _menuKill, _menuKillTree, _menuPreview, new Separator(), _menuMin, _menuMax, _menuRestore } };
        _menu.Opened += (_, _) => LocalizeMenu();
        WindowList.ContextMenu = _menu;
        TextCompositionManager.AddTextInputStartHandler(SearchBox, (_, _) => _composing = true);
        TextCompositionManager.AddTextInputHandler(SearchBox, (_, _) => _composing = false);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowChrome.ApplyToolWindow(hwnd);
            WindowChrome.ApplyBackdrop(hwnd, ThemeService.IsDark);
        };
        ThemeService.Changed += dark => Dispatcher.BeginInvoke(() =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, dark);
        });
        ApplyStaticText();
        Localizer.Current.LanguageChanged += (_, _) => ApplyStaticText();
    }

    public nint Handle => new WindowInteropHelper(this).Handle;
    public event Action<long>? Hovered;
    public event Action<long>? Invoked;
    public event Action? CommitRequested;
    public event Action? CancelRequested;
    public event Action? SearchRequested;
    public event Action<string>? QueryEdited;
    public event Action<int>? NavigateRequested;
    public event Action? CloseRequested;
    public event Action? KillRequested;
    public event Action? KillTreeRequested;
    public event Action? SortCycleRequested;
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
        StatusText.Text = state.Status ?? "";
        EmptyText.Text = state.EmptyText;
        EmptyText.Visibility = string.IsNullOrEmpty(state.EmptyText) ? Visibility.Collapsed : Visibility.Visible;

        PreviewColumn.Width = state.ShowPreview ? new GridLength(0.58, GridUnitType.Star) : new GridLength(0);
        ListColumn.Width = state.ShowPreview ? new GridLength(0.42, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        PreviewRoot.Visibility = state.ShowPreview ? Visibility.Visible : Visibility.Collapsed;

        if (!SameRows(state.Filtered))
        {
            _rows.Clear();
            foreach (var entry in state.Filtered)
            {
                state.Icons.TryGetValue(entry.Handle, out var icon);
                var row = new SwitcherRow(entry, icon ?? IconLoader.Letter(entry.Title));
                row.ApplyLoad(state.ShowLoad, default);
                _rows.Add(row);
            }
            WindowList.ItemsSource = null;
            WindowList.ItemsSource = _rows;
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (state.Icons.TryGetValue(row.Entry.Handle, out var icon) && !ReferenceEquals(icon, row.Icon))
                row.Icon = icon;
            var load = state.Loads.TryGetValue(row.Entry.Handle, out var found) ? found : default;
            row.ApplyLoad(state.ShowLoad, load);
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
        PreviewTitle.Text = selected?.Title ?? "";
        PreviewProcess.Text = selected is null ? "" : $"{selected.DisplayProcessName}  ·  pid {selected.KillProcessId}";
        var enabled = selected is not null;
        MinButton.IsEnabled = enabled;
        MaxButton.IsEnabled = enabled;
        RestoreButton.IsEnabled = enabled;
        CloseButton.IsEnabled = enabled;
        KillButton.IsEnabled = enabled;
        _menuPreview.IsEnabled = selected?.IsIconic == true;
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
        Activate();
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
        SearchBox.CaretIndex = SearchBox.Text.Length;
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
        if (!_searchMode || _composing || e.ImeProcessedKey != Key.None) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
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
            case Key.Enter:
                CommitRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelRequested?.Invoke();
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
            case Key.Delete when mods.HasFlag(WpfMods.Shift):
                KillRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_guard || !IsVisible) return;
        if (DateTime.UtcNow < _suppressCancelUntil) return;
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 250) return;
        CancelRequested?.Invoke();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_guard)
        {
            e.Cancel = true;
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
        LocalizeMenu();
    }

    void Sort_Click(object sender, RoutedEventArgs e) => SortCycleRequested?.Invoke();

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
    }

    bool SameRows(IReadOnlyList<WindowEntry> filtered)
    {
        if (_rows.Count != filtered.Count) return false;
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Entry.Handle != filtered[i].Handle) return false;
        }
        return true;
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
        if (RowFrom(sender) is { } row) Hovered?.Invoke(row.Entry.Handle);
    }

    void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (RowFrom(sender) is not { } row) return;
        Invoked?.Invoke(row.Entry.Handle);
        e.Handled = true;
    }

    void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e) is { } row) Hovered?.Invoke(row.Entry.Handle);
    }

    void List_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if (RowAt(e) is not { } row) return;
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
