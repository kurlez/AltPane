namespace AltPane.Core.Icons;

/// <summary>
/// Remembers which executable an app or window was drawn from.
/// When that path changes, the previous path is returned so its icon can be dropped.
/// </summary>
public sealed class IconPathBindings
{
    readonly object _gate = new();
    readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

    public string? Rebind(string? appKey, string? path)
    {
        if (string.IsNullOrWhiteSpace(appKey)) return null;
        path ??= "";
        lock (_gate)
        {
            if (_paths.TryGetValue(appKey, out var previous))
            {
                if (string.Equals(previous, path, StringComparison.OrdinalIgnoreCase)) return null;
                _paths[appKey] = path;
                return previous;
            }

            _paths[appKey] = path;
            return null;
        }
    }
}
