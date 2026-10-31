using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace AltPane;

public partial class App : Application
{
    Mutex? _mutex;
    EventWaitHandle? _activate;
    RegisteredWaitHandle? _wait;
    AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThreadPool.SetMinThreads(4, 4);
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogUnhandled(args.ExceptionObject, args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogUnhandled(args.Exception, terminating: false);
            args.SetObserved();
        };

        try
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }

        _mutex = new Mutex(false, @"Local\AltPane.SingleInstance");
        var ownsMutex = false;
        try
        {
            ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(@"Local\AltPane.Activate");
                ev.Set();
            }
            catch
            {
                // The first instance may already be exiting.
            }
            Shutdown();
            return;
        }

        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AltPane.Activate");
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) =>
        {
            Dispatcher.BeginInvoke(() => _host?.ShowSettings());
        }, null, Timeout.Infinite, false);

        try
        {
            _host = new AppHost();
            _host.Start();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            try { _host?.EmergencyUnhook(); } catch { }
            MessageBox.Show(ex.Message, "AltPane", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        try { _host?.EmergencyUnhook(); } catch { }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _host?.Dispose(); } catch { }
        try { _wait?.Unregister(null); } catch { }
        try { _activate?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    void LogUnhandled(object? error, bool terminating)
    {
        try
        {
            var text = error is Exception ex ? ex.ToString() : error?.ToString() ?? "(null)";
            AppLog.Error($"unhandled exception terminating={terminating}{Environment.NewLine}{text}");
        }
        catch
        {
            // Logging must not throw from the crash path.
        }
        try { _host?.EmergencyUnhook(); } catch { }
    }

    void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogUnhandled(e.Exception, terminating: true);
        e.Handled = true;
        MessageBox.Show(
            Core.Localization.Localizer.Current["crash.body"] + Environment.NewLine + Environment.NewLine + e.Exception.Message,
            Core.Localization.Localizer.Current["crash.title"],
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }
}
