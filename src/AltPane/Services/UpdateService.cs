using System.Net.Http;
using System.Windows;
using AltPane.Core.Localization;
using AltPane.Core.Updates;

namespace AltPane;

/// <summary>
/// Looks for a newer GitHub release at startup, every six hours and on demand. When one exists it asks
/// 「发现新版本 X，是否安装？」; on yes it downloads the zip, verifies it against the release's .sha256,
/// and hands the executable to <see cref="SelfUpdater"/>, which swaps it in after AltPane exits.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    readonly string _currentVersion;
    readonly Func<bool> _install;
    readonly Action<string> _notify;
    readonly UpdateClient _client;
    readonly SemaphoreSlim _gate = new(1, 1);
    Timer? _timer;
    string? _declined;
    bool _disposed;

    /// <param name="exitForUpdate">Exits AltPane after the swap script has started.</param>
    public UpdateService(string currentVersion, Func<bool> exitForUpdate, Action<string> notify)
    {
        _currentVersion = currentVersion;
        _install = exitForUpdate;
        _notify = notify;
        _client = new UpdateClient(Http, currentVersion);
    }

    public static string DownloadDirectory() => Path.Combine(Path.GetTempPath(), "AltPane", "update");

    public void Start()
    {
        ReportLastFailure();
        _timer = new Timer(_ => Dispatch(manual: false), null, TimeSpan.FromSeconds(30), UpdateCheck.Interval);
    }

    /// <summary>The swap script writes its error here when it could not replace the file (for example, admin rights needed).</summary>
    void ReportLastFailure()
    {
        try
        {
            if (!File.Exists(SelfUpdater.ErrorFile)) return;
            var reason = File.ReadAllText(SelfUpdater.ErrorFile).Trim();
            File.Delete(SelfUpdater.ErrorFile);
            AppLog.Info("Last update failed: " + reason);
            _notify(Localizer.Current.Format("update.lastFailed", reason));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void CheckNow() => Dispatch(manual: true);

    void Dispatch(bool manual)
    {
        var app = Application.Current;
        if (app is null || _disposed) return;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try { await CheckAsync(manual); }
            catch (Exception ex) { AppLog.Error(ex); }
        });
    }

    async Task CheckAsync(bool manual)
    {
        if (!await _gate.WaitAsync(0))
        {
            if (manual) _notify(Localizer.Current["update.busy"]);
            return;
        }
        try
        {
            var loc = Localizer.Current;
            ReleaseInfo? release;
            try
            {
                release = await _client.GetLatestAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                AppLog.Info("Update check failed: " + ex.Message);
                if (manual) MessageBox.Show(loc.Format("update.checkFailed", ex.Message), "AltPane", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AppLog.Info($"Update check: current {_currentVersion}, latest {release?.Version ?? "none"}.");
            if (release is null || !UpdateCheck.IsNewer(_currentVersion, release))
            {
                if (manual) MessageBox.Show(loc.Format("update.upToDate", _currentVersion), "AltPane", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // An automatic check asks once per version per session; the tray item always asks.
            if (!manual && _declined == release.Version) return;
            var answer = MessageBox.Show(loc.Format("update.prompt", release.Version, _currentVersion, release.HtmlUrl),
                "AltPane", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes)
            {
                _declined = release.Version;
                return;
            }
            _notify(loc.Format("update.downloading", release.Version));
            string exe;
            try
            {
                exe = await Task.Run(() => _client.DownloadAsync(release, Path.Combine(DownloadDirectory(), release.Version), CancellationToken.None));
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
                MessageBox.Show(loc.Format("update.downloadFailed", ex.Message), "AltPane", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AppLog.Info($"Update {release.Version} downloaded and verified; installing.");
            if (!SelfUpdater.Start(exe))
            {
                MessageBox.Show(loc["update.installFailed"], "AltPane", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _install();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _timer?.Dispose(); } catch (ObjectDisposedException) { }
    }
}
