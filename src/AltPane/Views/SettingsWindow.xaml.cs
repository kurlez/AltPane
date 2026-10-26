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
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowChrome.ApplyBackdrop(hwnd, ThemeService.IsDark);
        };
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;
        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = "AltPane " + (version?.ToString(3) ?? "0.2.0");
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
        StickyBox.IsChecked = _draft.StickySwitcher;
        QuickTapBox.IsChecked = _draft.QuickTapSwitches;
        UpdateQuickTapEnabled();
        PreviewBox.IsChecked = _draft.ShowPreview;
        PreviewMinimizedBox.IsChecked = _draft.PreviewMinimized;
        LoadBox.IsChecked = _draft.ShowLoad;
        ConfirmKillBox.IsChecked = _draft.ConfirmEndTask;
        KillTreeBox.IsChecked = _draft.EndTaskKillsTree;
        DesktopBox.IsChecked = _draft.IncludeOtherVirtualDesktops;
        StartupBox.IsChecked = _draft.StartWithWindows;
        PauseBox.IsChecked = _draft.OverridePaused;
        ExclusionBox.Text = string.Join(Environment.NewLine, _draft.ExcludedProcesses);
        Select(ThemeBox, _draft.Theme);
        Select(LanguageBox, _draft.Language);
        Select(SizeBox, _draft.Size);
        Select(PlacementBox, _draft.Placement);
        Select(SortBox, _draft.SortMode);
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
        StickyBox.Content = loc["settings.sticky"];
        StickyNote.Text = loc["settings.stickyNote"];
        QuickTapBox.Content = loc["settings.quickTap"];
        QuickTapNote.Text = loc["settings.quickTapNote"];
        ThemeLabel.Text = loc["settings.theme"];
        LanguageLabel.Text = loc["settings.language"];
        SizeLabel.Text = loc["settings.size"];
        PlacementLabel.Text = loc["settings.placement"];
        PreviewBox.Content = loc["settings.preview"];
        PreviewMinimizedBox.Content = loc["settings.previewMinimized"];
        PreviewMinimizedNote.Text = loc["settings.previewMinimizedNote"];
        LoadBox.Content = loc["settings.showLoad"];
        SortLabel.Text = loc["settings.sort"];
        ConfirmKillBox.Content = loc["settings.confirmKill"];
        KillTreeBox.Content = loc["settings.killTree"];
        DesktopBox.Content = loc["settings.desktops"];
        StartupBox.Content = loc["settings.startup"];
        PauseBox.Content = loc["settings.pause"];
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
            (LoadSortMode.Recent, loc["settings.sort.recent"]),
            (LoadSortMode.Cpu, loc["settings.sort.cpu"]),
            (LoadSortMode.Memory, loc["settings.sort.memory"])
        }, _draft.SortMode);
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
        _draft.ShowPreview = PreviewBox.IsChecked == true;
        _draft.PreviewMinimized = PreviewMinimizedBox.IsChecked == true;
        _draft.ShowLoad = LoadBox.IsChecked == true;
        _draft.ConfirmEndTask = ConfirmKillBox.IsChecked == true;
        _draft.EndTaskKillsTree = KillTreeBox.IsChecked == true;
        _draft.SortMode = Read(SortBox, _draft.SortMode);
        _draft.IncludeOtherVirtualDesktops = DesktopBox.IsChecked == true;
        _draft.StartWithWindows = StartupBox.IsChecked == true;
        _draft.OverridePaused = PauseBox.IsChecked == true;
        _draft.Theme = Read(ThemeBox, _draft.Theme);
        _draft.Language = Read(LanguageBox, _draft.Language);
        _draft.Size = Read(SizeBox, _draft.Size);
        _draft.Placement = Read(PlacementBox, _draft.Placement);
        _draft.ExcludedProcesses = ExclusionBox.Text
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
        ShowPreview = source.ShowPreview,
        PreviewMinimized = source.PreviewMinimized,
        Placement = source.Placement,
        Language = source.Language,
        ExcludedProcesses = new List<string>(source.ExcludedProcesses ?? new List<string>()),
        StartWithWindows = source.StartWithWindows,
        OverridePaused = source.OverridePaused,
        IncludeOtherVirtualDesktops = source.IncludeOtherVirtualDesktops,
        WelcomeShown = source.WelcomeShown,
        ShowLoad = source.ShowLoad,
        SortMode = source.SortMode,
        ConfirmEndTask = source.ConfirmEndTask,
        EndTaskKillsTree = source.EndTaskKillsTree
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
