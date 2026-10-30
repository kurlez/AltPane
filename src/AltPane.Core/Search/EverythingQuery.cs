namespace AltPane.Core.Search;

public enum EverythingLink
{
    None = 0,
    Sdk,
    Ipc,
    IpcTaskbar,
    IpcWindow,
    Es,
    Http
}

public enum EverythingReach
{
    NotRunning = 0,
    IpcFailed,
    Connected,
    ConnectedHttp
}

public readonly record struct EverythingAttempt(EverythingLink Link, int? Port);

/// <summary>
/// Which Everything transport to try, in order. The SDK is first when its DLL is
/// present. IPC uses <c>EVERYTHING_IPC_WNDCLASS</c>, then the tray class
/// <c>EVERYTHING_TASKBAR_NOTIFICATION</c>, then the main <c>EVERYTHING</c> window
/// when the IPC class is missing or that window does not answer. <c>es.exe</c>
/// follows. HTTP is last, on the configured port and then 8080.
/// A null reply means that hop did not answer. An empty list is an answer.
/// </summary>
public static class EverythingQuery
{
    public const string IpcClassName = "EVERYTHING_IPC_WNDCLASS";
    public const string TaskbarClassName = "EVERYTHING_TASKBAR_NOTIFICATION";
    public const string MainClassName = "EVERYTHING";

    public static readonly EverythingLink[] IpcOrder =
    {
        EverythingLink.Ipc,
        EverythingLink.IpcTaskbar,
        EverythingLink.IpcWindow
    };

    public readonly record struct Result(
        IReadOnlyList<string> Paths,
        EverythingLink Link,
        int? Port,
        EverythingReach Reach,
        IReadOnlyList<EverythingAttempt> Attempted);

    public delegate IReadOnlyList<string>? Send(EverythingLink link, int? port);

    public static Result Run(
        bool running,
        Func<EverythingLink, bool> available,
        Send send,
        int httpPort,
        bool tryPort8080,
        EverythingLink prefer = EverythingLink.None,
        int? preferPort = null)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(send);
        var attempted = new List<EverythingAttempt>();
        var skipped = new List<EverythingAttempt>();

        if (running && prefer != EverythingLink.None && PreferIsOpen(prefer, preferPort, httpPort, tryPort8080, available))
        {
            var port = prefer == EverythingLink.Http ? preferPort : null;
            attempted.Add(new EverythingAttempt(prefer, port));
            var reply = send(prefer, port);
            if (reply is not null)
            {
                var reach = prefer == EverythingLink.Http ? EverythingReach.ConnectedHttp : EverythingReach.Connected;
                return Finish(reply, prefer, port, reach, attempted);
            }
            skipped.Add(new EverythingAttempt(prefer, port));
        }

        var sdk = Try(EverythingLink.Sdk, null, EverythingReach.Connected, available, send, attempted, skipped);
        if (sdk is not null) return sdk.Value;
        foreach (var link in IpcOrder)
        {
            var ipc = Try(link, null, EverythingReach.Connected, available, send, attempted, skipped);
            if (ipc is not null) return ipc.Value;
        }
        var es = Try(EverythingLink.Es, null, EverythingReach.Connected, available, send, attempted, skipped);
        if (es is not null) return es.Value;

        if (!running)
            return Finish(Array.Empty<string>(), EverythingLink.None, null, EverythingReach.NotRunning, attempted);

        foreach (var port in EverythingHttp.Ports(httpPort, tryPort8080))
        {
            var http = Try(EverythingLink.Http, port, EverythingReach.ConnectedHttp, available, send, attempted, skipped);
            if (http is not null) return http.Value;
        }
        return Finish(Array.Empty<string>(), EverythingLink.None, null, EverythingReach.IpcFailed, attempted);
    }

    public static string Name(EverythingLink link, int? port) => link switch
    {
        EverythingLink.Sdk => "sdk",
        EverythingLink.Ipc => "ipc",
        EverythingLink.IpcTaskbar => "ipc-taskbar",
        EverythingLink.IpcWindow => "ipc-window",
        EverythingLink.Es => "es",
        EverythingLink.Http => "http" + (port is int value ? ":" + value : ""),
        _ => "none"
    };

    static bool PreferIsOpen(
        EverythingLink prefer,
        int? preferPort,
        int httpPort,
        bool tryPort8080,
        Func<EverythingLink, bool> available)
    {
        if (prefer == EverythingLink.Http)
            return preferPort is int port && EverythingHttp.Ports(httpPort, tryPort8080).Contains(port);
        return available(prefer);
    }

    static Result? Try(
        EverythingLink link,
        int? port,
        EverythingReach reach,
        Func<EverythingLink, bool> available,
        Send send,
        List<EverythingAttempt> attempted,
        List<EverythingAttempt> skipped)
    {
        if (skipped.Contains(new EverythingAttempt(link, port))) return null;
        if (link != EverythingLink.Http && !available(link)) return null;
        attempted.Add(new EverythingAttempt(link, port));
        var reply = send(link, port);
        if (reply is null) return null;
        return Finish(reply, link, port, reach, attempted);
    }

    static Result Finish(
        IReadOnlyList<string> paths,
        EverythingLink link,
        int? port,
        EverythingReach reach,
        List<EverythingAttempt> attempted) =>
        new(paths, link, port, reach, attempted.ToArray());
}
