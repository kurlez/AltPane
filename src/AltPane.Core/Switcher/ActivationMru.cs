namespace AltPane.Core.Switcher;

/// <summary>
/// Most-recently-activated window handles. Index 0 is the newest. The list is
/// bounded and safe to update from a foreground hook while the UI reads it.
/// </summary>
public sealed class ActivationMru
{
    public const int DefaultCapacity = 64;
    readonly object _gate = new();
    readonly List<long> _recent = new();
    readonly int _capacity;

    public ActivationMru(int capacity = DefaultCapacity)
    {
        _capacity = capacity < 1 ? 1 : capacity;
    }

    public void Note(long handle)
    {
        if (handle == 0) return;
        lock (_gate)
        {
            var existing = _recent.IndexOf(handle);
            if (existing == 0) return;
            if (existing > 0) _recent.RemoveAt(existing);
            _recent.Insert(0, handle);
            if (_recent.Count > _capacity) _recent.RemoveAt(_recent.Count - 1);
        }
    }

    /// <summary>Lower is more recent. A handle that was never recorded is <see cref="int.MaxValue"/>.</summary>
    public int Rank(long handle)
    {
        lock (_gate)
        {
            var index = _recent.IndexOf(handle);
            return index < 0 ? int.MaxValue : index;
        }
    }
}
