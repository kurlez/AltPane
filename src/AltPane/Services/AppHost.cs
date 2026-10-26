using System.Globalization;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using Microsoft.Win32;

namespace AltPane;

internal sealed class AppHost : IDisposable
{
    readonly string _settingsPath = SettingsStore.DefaultPath();
    readonly SwitcherPresenter _presenter;
    readonly HotkeyService _hotkeys;
    readonly TrayController _tray;
    SettingsWindow? _settingsWindow;
    bool _recording;
    bool _disposed;

    public AppHost()
    {
        var loaded = SettingsStore.Load(_settingsPath);
        if (loaded.RecoveredFromError)
            AppLog.Error("Settings were reset: " + loaded.Error);
        Settings = loaded.Settings;
        Localizer.Current.Apply(Settings.Language, CultureInfo.CurrentUICulture.Name);
        ThemeService.Apply(Settings.Theme);
        _presenter = new SwitcherPresenter(() => Settings);
        _hotkeys = new HotkeyService(_presenter, () => Settings, () => _recording, System.Windows.Application.Current.Dispatcher);
        _presenter.Hotkeys = _hotkeys;
        _presenter.Notify += message => _tray?.Balloon(message);
        _tray = new TrayController(this);
        Localizer.Current.LanguageChanged += (_, _) => _tray.Rebuild();
        SystemEvents.UserPreferenceChanged += OnPreference;
        AppLog.Info("AltPane started.");
    }

    public AppSettings Settings { get; private set; }

    public void Start()
    {
        _tray.Show();
        if (!Settings.OverridePaused && !_hotkeys.Install())
            _tray.Balloon(Localizer.Current["settings.hookFailed"]);
        if (!Settings.WelcomeShown)
        {
            _tray.Balloon(Localizer.Current["tray.welcome.body"], Localizer.Current["tray.welcome.title"]);
            Settings.WelcomeShown = true;
            TrySave();
        }
    }

    public void OpenSwitcher() => _hotkeys.RequestOpenFromTray();

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _hotkeys.RequestCancel();
        _settingsWindow = new SettingsWindow(Settings, UpdateSettings, recording => _recording = recording);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void TogglePause()
    {
        Settings.OverridePaused = !Settings.OverridePaused;
        if (Settings.OverridePaused) _hotkeys.Uninstall();
        else if (!_hotkeys.Install()) _tray.Balloon(Localizer.Current["settings.hookFailed"]);
        _tray.Rebuild();
        TrySave();
    }

    public void ToggleStartup()
    {
        Settings.StartWithWindows = !Settings.StartWithWindows;
        try
        {
            StartupRegistration.Apply(Settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            Settings.StartWithWindows = !Settings.StartWithWindows;
            _tray.Balloon(Localizer.Current["startup.failed"]);
        }
        _tray.Rebuild();
        TrySave();
    }

    public void Exit()
    {
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public void EmergencyUnhook() => _hotkeys.Uninstall();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { SystemEvents.UserPreferenceChanged -= OnPreference; } catch { }
        try { _hotkeys.Uninstall(); } catch (Exception ex) { AppLog.Error(ex); }
        try { _presenter.ForceClose(); } catch { }
        try { _settingsWindow?.Close(); } catch { }
        try { _tray.Dispose(); } catch { }
        AppLog.Info("AltPane stopped.");
    }

    void UpdateSettings(AppSettings draft)
    {
        draft.WelcomeShown = Settings.WelcomeShown;
        Settings = draft;
        Localizer.Current.Apply(Settings.Language, CultureInfo.CurrentUICulture.Name);
        ThemeService.Apply(Settings.Theme);
        try
        {
            StartupRegistration.Apply(Settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            _tray.Balloon(Localizer.Current["startup.failed"]);
        }

        if (Settings.OverridePaused) _hotkeys.Uninstall();
        else if (!_hotkeys.Install()) _tray.Balloon(Localizer.Current["settings.hookFailed"]);
        SettingsStore.Save(_settingsPath, Settings);
        _tray.Rebuild();
    }

    void TrySave()
    {
        try { SettingsStore.Save(_settingsPath, Settings); }
        catch (Exception ex) { AppLog.Error(ex); }
    }

    void OnPreference(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (Settings.Theme != AppTheme.System) return;
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ThemeService.Apply(AppTheme.System));
    }
}
