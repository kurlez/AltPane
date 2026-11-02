namespace AltPane.Core.Improvements;

/// <summary>Line-oriented channel to an app-server. Tests supply an in-memory one.</summary>
public interface IRpcTransport : IAsyncDisposable
{
    /// <summary>Next line, or null when the server closed its output.</summary>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);

    Task WriteLineAsync(string line, CancellationToken cancellationToken);
}
