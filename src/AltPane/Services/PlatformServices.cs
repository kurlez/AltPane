using System.Diagnostics;
using System.Security.Principal;
using AltPane.Core.Settings;
using Microsoft.Win32;

namespace AltPane;

internal static class StartupRegistration
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "AltPane";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("HKCU Run key is unavailable.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exe = AppPaths.ResolveExecutable(Environment.ProcessPath);
        key.SetValue(ValueName, ExecutablePath.Quote(exe));
    }
}

internal static class Admin
{
    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryRestartAsAdmin()
    {
        var exe = AppPaths.ResolveExecutable(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(exe)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }
}

internal static class SystemTheme
{
    public static bool IsLight()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int light && light != 0;
        }
        catch
        {
            return false;
        }
    }
}

internal static class ThemeService
{
    public static event Action<bool>? Changed;
    public static bool IsDark { get; private set; } = true;

    public static void Apply(Core.Models.AppTheme theme)
    {
        IsDark = theme switch
        {
            Core.Models.AppTheme.Light => false,
            Core.Models.AppTheme.Dark => true,
            _ => !SystemTheme.IsLight()
        };

        var name = IsDark ? "Dark" : "Light";
        var dict = new System.Windows.ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{name}.xaml", UriKind.Absolute)
        };
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(dict);
        Changed?.Invoke(IsDark);
    }
}
