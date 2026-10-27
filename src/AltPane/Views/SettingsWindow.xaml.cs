using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AltPane.Core.Input;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using WpfMods = System.Windows.Input.ModifierKeys;
using AppMods = AltPane.Core.Models.ModifierKeys;

namespace AltPane;

public partial class SettingsWindow : Window
{
    readonly AppSettings _original;
    readonly Action<AppSettings> _save;
    readonly Action<bool> _recordingChanged;
    AppSettings _draft;
    bool _loading;
    bool _recording;
    bool _saved;

    public SettingsWindow(AppSettings current, Action<AppSettings> save, Action<bool> recordingChanged)
    {
        _original = Copy(current);
        _draft = Copy(current);
        _save = save;
        _recordingChanged = recordingChanged;
        InitializeComponent();
        ThemeBox.SelectionChanged += ThemeBox_SelectionChanged;
        LanguageBox.SelectionChanged += LanguageBox_SelectionChanged;
        SourceInitialized += (_, _) => ApplyChrome();
        ThemeService.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeService.Changed -= OnThemeChanged;
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;
        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = "AltPane " + (version?.ToString(3) ?? "0.4.2");
        LoadDraft();
        Localizer.Current.LanguageChanged += (_, _) =>
        {
            if (IsLoaded) ApplyLabels();
        };
    }

    void LoadDraft()
    {
        _loading = true;
        ApplyLabels();
        HotkeyText.Text = HotkeyLogic.Format(_draft.Hotkey ?? HotkeyChord.AltTab());
        WinTabBox.IsChecked = _draft.CaptureWinTab;
        UpdateWinTabState();
        StickyBox.IsChecked = _draft.StickySwitcher;
        QuickTapBox.IsChecked = _draft.QuickTapSwitches;
        UpdateQuickTapEnabled();
        LoadBox.IsChecked = _draft.ShowLoad;
        ConfirmKillBox.IsChecked = _draft.ConfirmEndTask;
        KillTreeBox.IsChecked = _draft.EndTaskKillsTree;
        DesktopBox.IsChecked = _draft.IncludeOtherVirtualDesktops;
        StartupBox.IsChecked = _draft.StartWithWindows;
        PauseBox.IsChecked = _draft.OverridePaused;
        LaunchBox.IsChecked = _draft.LaunchApps;
        IndexStartBox.IsChecked = _draft.IndexStartMenu;
        IndexStoreBox.IsChecked = _draft.IndexStoreApps;
        IndexDesktopBox.IsChecked = _draft.IndexDesktop;
        IndexPathBox.IsChecked = _draft.IndexPath;
        ExclusionBox.Text = string.Join(Environment.NewLine, _draft.ExcludedProcesses);
        ExcludedAppsBox.Text = string.Join(Environment.NewLine, _draft.ExcludedApps);
        PinnedBox.Text = string.Join(Environment.NewLine, _draft.PinnedApps ?? new List<string>());
        DemotedBox.Text = string.Join(Environment.NewLine, _draft.DemotedApps ?? new List<string>());
        Select(ThemeBox, _draft.Theme);
        Select(LanguageBox, _draft.Language);
        Select(SizeBox, _draft.Size);
        Select(PlacementBox, _draft.Placement);
        MonitorBadgesBox.IsChecked = _draft.ShowMonitorBadges;
        MoveBox.IsChecked = _draft.MoveToSwitcherMonitor;
        VerboseBox.IsChecked = _draft.VerboseLog;
        EverythingBox.IsChecked = _draft.UseEverything;
        EverythingExtBox.Text = _draft.EverythingExtensions;
        EverythingExcludeBox.Text = string.Join(Environment.NewLine, _draft.EverythingExcludedPaths ?? new List<string>());
        UpdateEverythingStatus();
        Select(SortBox, _draft.SortMode);
        Select(ThresholdBox, _draft.FocusThresholdMinutes);
        var admin = Admin.IsRunningAsAdmin();
        AdminButton.IsEnabled = !admin;
        AdminButton.Content = admin ? Localizer.Current["settings.alreadyAdmin"] : Localizer.Current["settings.restartAdmin"];
        _loading = false;
    }

    void ApplyLabels()
    {
        var loc = Localizer.Current;
        Title = loc["settings.title"];
        TitleText.Text = loc["settings.title"];
        HotkeyLabel.Text = loc["settings.hotkey"];
        RecordButton.Content = _recording ? loc["settings.recording"] : loc["settings.record"];
        WinTabBox.Content = loc["settings.winTab"];
        WinTabNote.Text = loc["settings.winTabNote"];
        UpdateWinTabState();
        StickyBox.Content = loc["settings.sticky"];
        StickyNote.Text = loc["settings.stickyNote"];
        QuickTapBox.Content = loc["settings.quickTap"];
        QuickTapNote.Text = loc["settings.quickTapNote"];
        ThemeLabel.Text = loc["settings.theme"];
        LanguageLabel.Text = loc["settings.language"];
        SizeLabel.Text = loc["settings.size"];
        PlacementLabel.Text = loc["settings.placement"];
        MonitorBadgesBox.Content = loc["settings.monitorBadges"];
        MonitorBadgesNote.Text = loc["settings.monitorBadgesNote"];
        MoveBox.Content = loc["settings.move"];
        MoveNote.Text = loc["settings.moveNote"];
        VerboseBox.Content = loc["settings.verbose"];
        VerboseNote.Text = loc["settings.verboseNote"];
        EverythingBox.Content = loc["settings.everything"];
        EverythingNote.Text = loc["settings.everythingNote"];
        EverythingExtLabel.Text = loc["settings.everythingExt"];
        EverythingExcludeLabel.Text = loc["settings.everythingExclude"];
        EverythingExcludeHint.Text = loc["settings.everythingExcludeHint"];
        EverythingStartButton.Content = loc["settings.everythingStart"];
        EverythingSiteButton.Content = loc["settings.everythingSite"];
        LoadBox.Content = loc["settings.showLoad"];
        SortLabel.Text = loc["settings.sort"];
        ThresholdLabel.Text = loc["settings.focusThreshold"];
        ThresholdHint.Text = loc["settings.focusThresholdHint"];
        PinnedLabel.Text = loc["settings.pinnedApps"];
        PinnedHint.Text = loc["settings.pinnedAppsHint"];
        DemotedLabel.Text = loc["settings.demotedApps"];
        DemotedHint.Text = loc["settings.demotedAppsHint"];
        ConfirmKillBox.Content = loc["settings.confirmKill"];
        KillTreeBox.Content = loc["settings.killTree"];
        DesktopBox.Content = loc["settings.desktops"];
        StartupBox.Content = loc["settings.startup"];
        PauseBox.Content = loc["settings.pause"];
        LaunchBox.Content = loc["settings.launch"];
        LaunchNote.Text = loc["settings.launchNote"];
        IndexStartBox.Content = loc["settings.indexStart"];
        IndexStoreBox.Content = loc["settings.indexStore"];
        IndexDesktopBox.Content = loc["settings.indexDesktop"];
        IndexPathBox.Content = loc["settings.indexPath"];
        IndexPathNote.Text = loc["settings.indexPathNote"];
        ExcludedAppsLabel.Text = loc["settings.excludedApps"];
        ExcludedAppsHint.Text = loc["settings.excludedAppsHint"];
        ExclusionLabel.Text = loc["settings.exclusions"];
        ExclusionHint.Text = loc["settings.exclusionsHint"];
        ShortcutTitle.Text = loc["settings.shortcuts"];
        ShortcutBody.Text = loc["settings.shortcutBody"];
        AdminNote.Text = loc["settings.admin"];
        SaveButton.Content = loc["settings.save"];
        CancelButton.Content = loc["settings.cancel"];
        if (!_recording)
            HotkeyError.Text = loc["settings.hotkeyInvalid"];
        Fill(ThemeBox, new (AppTheme, string)[]
        {
            (AppTheme.System, loc["settings.theme.system"]),
            (AppTheme.Light, loc["settings.theme.light"]),
            (AppTheme.Dark, loc["settings.theme.dark"])
        }, _draft.Theme);
        Fill(LanguageBox, new (AppLanguage, string)[]
        {
            (AppLanguage.System, loc["settings.language.system"]),
            (AppLanguage.ZhCn, loc["settings.language.zh"]),
            (AppLanguage.En, loc["settings.language.en"])
        }, _draft.Language);
        Fill(SizeBox, new (SwitcherSize, string)[]
        {
            (SwitcherSize.Compact, loc["settings.size.compact"]),
            (SwitcherSize.Comfortable, loc["settings.size.comfortable"]),
            (SwitcherSize.Large, loc["settings.size.large"])
        }, _draft.Size);
        Fill(PlacementBox, new (SwitcherPlacement, string)[]
        {
            (SwitcherPlacement.MouseCursor, loc["settings.placement.mouse"]),
            (SwitcherPlacement.ActiveWindow, loc["settings.placement.window"])
        }, _draft.Placement);
        Fill(SortBox, new (LoadSortMode, string)[]
        {
            (LoadSortMode.FocusTime, loc["settings.sort.stable"]),
            (LoadSortMode.Recent, loc["settings.sort.recent"]),
            (LoadSortMode.Initial, loc["settings.sort.initial"]),
            (LoadSortMode.Cpu, loc["settings.sort.cpu"]),
            (LoadSortMode.Memory, loc["settings.sort.memory"])
        }, _draft.SortMode);
        var threshold = _draft.FocusThresholdMinutes;
        var presets = new List<(int Value, string Label)>
        {
            (1, loc["settings.threshold.1"]),
            (5, loc["settings.threshold.5"]),
            (10, loc["settings.threshold.10"]),
            (30, loc["settings.threshold.30"])
        };
        if (presets.TrueForAll(item => item.Value != threshold))
            presets.Add((threshold, loc.Format("settings.threshold.custom", threshold)));
        Fill(ThresholdBox, presets, threshold);
    }

    static void Fill<T>(System.Windows.Controls.ComboBox box, IEnumerable<(T Value, string Label)> items, T selected) where T : struct
    {
        var current = box.SelectedValue;
        box.Items.Clear();
        foreach (var item in items)
            box.Items.Add(new Choice<T>(item.Value, item.Label));
        Select(box, selected);
        if (box.SelectedIndex < 0 && current is T kept)
            Select(box, kept);
    }

    static void Select<T>(System.Windows.Controls.ComboBox box, T value)
    {
        foreach (Choice<T> choice in box.Items)
        {
            if (EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                box.SelectedItem = choice;
                return;
            }
        }
    }

    static T Read<T>(System.Windows.Controls.ComboBox box, T fallback) =>
        box.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    void OnThemeChanged(bool dark) => Dispatcher.BeginInvoke(() =>
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, dark);
    });

    void ApplyChrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, ThemeService.IsDark);
    }

    void ThemeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var theme = Read(ThemeBox, _draft.Theme);
        if (theme == _draft.Theme) return;
        _draft.Theme = theme;
        ThemeService.Apply(_draft.Theme);
    }

    void LanguageBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var language = Read(LanguageBox, _draft.Language);
        if (language == _draft.Language) return;
        _draft.Language = language;
        Localizer.Current.Apply(_draft.Language, System.Globalization.CultureInfo.CurrentUICulture.Name);
    }

    void StickyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateQuickTapEnabled();
    }

    void UpdateQuickTapEnabled()
    {
        var on = StickyBox.IsChecked == true;
        QuickTapBox.IsEnabled = on;
        QuickTapNote.Opacity = on ? 1 : 0.55;
    }

    void Record_Click(object sender, RoutedEventArgs e)
    {
        _recording = true;
        HotkeyError.Visibility = Visibility.Collapsed;
        RecordButton.Content = Localizer.Current["settings.recording"];
        _recordingChanged(true);
        Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_recording) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }
        if (key == Key.Escape && Keyboard.Modifiers == WpfMods.None)
        {
            StopRecording();
            e.Handled = true;
            return;
        }

        var mods = AppMods.None;
        if (Keyboard.Modifiers.HasFlag(WpfMods.Control)) mods |= AppMods.Control;
        if (Keyboard.Modifiers.HasFlag(WpfMods.Alt)) mods |= AppMods.Alt;
        if (Keyboard.Modifiers.HasFlag(WpfMods.Shift)) mods |= AppMods.Shift;
        if (Keyboard.Modifiers.HasFlag(WpfMods.Windows)) mods |= AppMods.Win;
        var chord = new HotkeyChord { Modifiers = mods, VirtualKey = KeyInterop.VirtualKeyFromKey(key) };
        StopRecording();
        if (!HotkeyLogic.IsValid(chord))
        {
            HotkeyError.Visibility = Visibility.Visible;
            HotkeyError.Text = Localizer.Current["settings.hotkeyInvalid"];
        }
        else
        {
            _draft.Hotkey = chord;
            HotkeyText.Text = HotkeyLogic.Format(chord);
            HotkeyError.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }

    void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        _recordingChanged(false);
        RecordButton.Content = Localizer.Current["settings.record"];
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        _draft.CaptureWinTab = WinTabBox.IsChecked == true;
        _draft.StickySwitcher = StickyBox.IsChecked == true;
        _draft.QuickTapSwitches = QuickTapBox.IsChecked == true;
        _draft.ShowLoad = LoadBox.IsChecked == true;
        _draft.ConfirmEndTask = ConfirmKillBox.IsChecked == true;
        _draft.EndTaskKillsTree = KillTreeBox.IsChecked == true;
        _draft.SortMode = Read(SortBox, _draft.SortMode);
        _draft.FocusThresholdMinutes = Read(ThresholdBox, _draft.FocusThresholdMinutes);
        _draft.IncludeOtherVirtualDesktops = DesktopBox.IsChecked == true;
        _draft.StartWithWindows = StartupBox.IsChecked == true;
        _draft.OverridePaused = PauseBox.IsChecked == true;
        _draft.LaunchApps = LaunchBox.IsChecked == true;
        _draft.IndexStartMenu = IndexStartBox.IsChecked == true;
        _draft.IndexStoreApps = IndexStoreBox.IsChecked == true;
        _draft.IndexDesktop = IndexDesktopBox.IsChecked == true;
        _draft.IndexPath = IndexPathBox.IsChecked == true;
        _draft.Theme = Read(ThemeBox, _draft.Theme);
        _draft.Language = Read(LanguageBox, _draft.Language);
        _draft.Size = Read(SizeBox, _draft.Size);
        _draft.Placement = Read(PlacementBox, _draft.Placement);
        _draft.ShowMonitorBadges = MonitorBadgesBox.IsChecked == true;
        _draft.MoveToSwitcherMonitor = MoveBox.IsChecked == true;
        _draft.VerboseLog = VerboseBox.IsChecked == true;
        _draft.UseEverything = EverythingBox.IsChecked == true;
        _draft.EverythingExtensions = EverythingExtBox.Text.Trim();
        _draft.EverythingExcludedPaths = EverythingExcludeBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        _draft.ExcludedProcesses = ExclusionBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        _draft.ExcludedApps = ExcludedAppsBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        _draft.PinnedApps = PinnedBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        _draft.DemotedApps = DemotedBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        _draft.WelcomeShown = _original.WelcomeShown;
        try
        {
            _save(_draft);
            _saved = true;
            Close();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            MessageBox.Show(this, Localizer.Current["settings.saveFailed"] + Environment.NewLine + ex.Message, "AltPane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        Close();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Cancel_Click(sender, e);

    void Admin_Click(object sender, RoutedEventArgs e)
    {
        if (Admin.TryRestartAsAdmin())
            System.Windows.Application.Current.Shutdown();
    }

    void WinTabBox_Changed(object sender, RoutedEventArgs e) => UpdateWinTabState();

    void UpdateWinTabState()
    {
        if (WinTabState is null) return;
        WinTabState.Text = Localizer.Current[WinTabBox.IsChecked == true ? "settings.winTabOn" : "settings.winTabOff"];
    }

    void UpdateEverythingStatus()
    {
        if (EverythingStatus is null) return;
        try
        {
            var status = new EverythingClient().Probe();
            EverythingStatus.Text = Localizer.Current[status.Running
                ? "settings.everythingRunning"
                : status.Installed
                    ? "settings.everythingStopped"
                    : "settings.everythingMissing"];
            EverythingStartButton.IsEnabled = status.Installed && !status.Running;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            EverythingStartButton.IsEnabled = false;
        }
    }

    void EverythingStart_Click(object sender, RoutedEventArgs e)
    {
        if (new EverythingClient().TryStart())
            Dispatcher.BeginInvoke(UpdateEverythingStatus, System.Windows.Threading.DispatcherPriority.Background);
    }

    void EverythingSite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.voidtools.com/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    void Title_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopRecording();
        if (!_saved)
        {
            Localizer.Current.Apply(_original.Language, System.Globalization.CultureInfo.CurrentUICulture.Name);
            ThemeService.Apply(_original.Theme);
        }
        base.OnClosed(e);
    }

    static AppSettings Copy(AppSettings source) => new()
    {
        Version = source.Version,
        Hotkey = new HotkeyChord
        {
            Modifiers = source.Hotkey?.Modifiers ?? AppMods.Alt,
            VirtualKey = source.Hotkey?.VirtualKey ?? VirtualKeys.Tab
        },
        CaptureWinTab = source.CaptureWinTab,
        StickySwitcher = source.StickySwitcher,
        QuickTapSwitches = source.QuickTapSwitches,
        Theme = source.Theme,
        Size = source.Size,
        Placement = source.Placement,
        ShowMonitorBadges = source.ShowMonitorBadges,
        MoveToSwitcherMonitor = source.MoveToSwitcherMonitor,
        UseEverything = source.UseEverything,
        EverythingExtensions = source.EverythingExtensions,
        EverythingExcludedPaths = new List<string>(source.EverythingExcludedPaths ?? new List<string>()),
        VerboseLog = source.VerboseLog,
        Language = source.Language,
        ExcludedProcesses = new List<string>(source.ExcludedProcesses ?? new List<string>()),
        StartWithWindows = source.StartWithWindows,
        OverridePaused = source.OverridePaused,
        IncludeOtherVirtualDesktops = source.IncludeOtherVirtualDesktops,
        WelcomeShown = source.WelcomeShown,
        ShowLoad = source.ShowLoad,
        SortMode = source.SortMode,
        FocusThresholdMinutes = source.FocusThresholdMinutes,
        ConfirmEndTask = source.ConfirmEndTask,
        EndTaskKillsTree = source.EndTaskKillsTree,
        LaunchApps = source.LaunchApps,
        IndexStartMenu = source.IndexStartMenu,
        IndexStoreApps = source.IndexStoreApps,
        IndexDesktop = source.IndexDesktop,
        IndexPath = source.IndexPath,
        ExcludedApps = new List<string>(source.ExcludedApps ?? new List<string>()),
        PinnedApps = new List<string>(source.PinnedApps ?? new List<string>()),
        DemotedApps = new List<string>(source.DemotedApps ?? new List<string>())
    };

    sealed class Choice<T>
    {
        public Choice(T value, string label)
        {
            Value = value;
            Label = label;
        }

        public T Value { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }
}
