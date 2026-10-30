namespace AltPane.Core.Icons;

/// <summary>
/// A fixed-capacity map. Reading or replacing an entry makes it the newest.
/// Inserting past the capacity drops the oldest key.
/// </summary>
public sealed class LruCache<T>
{
    readonly int _capacity;
    readonly Dictionary<string, LinkedListNode<Entry>> _map;
    readonly LinkedList<Entry> _order;

    sealed class Entry
    {
        public string Key = "";
        public T Value = default!;
    }

    public LruCache(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _map = new Dictionary<string, LinkedListNode<Entry>>(capacity, StringComparer.OrdinalIgnoreCase);
        _order = new LinkedList<Entry>();
    }

    public int Count => _map.Count;
    public int Capacity => _capacity;

    public bool TryGet(string key, out T value)
    {
        if (key is not null && _map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public string? Set(string key, T value)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_map.TryGetValue(key, out var node))
        {
            node.Value.Value = value;
            _order.Remove(node);
            _order.AddFirst(node);
            return null;
        }

        var added = new LinkedListNode<Entry>(new Entry { Key = key, Value = value });
        _order.AddFirst(added);
        _map[key] = added;
        if (_map.Count <= _capacity) return null;
        var last = _order.Last!;
        _order.RemoveLast();
        _map.Remove(last.Value.Key);
        return last.Value.Key;
    }

    public bool Remove(string key)
    {
        if (key is null || !_map.Remove(key, out var node)) return false;
        _order.Remove(node);
        return true;
    }

    public int RemoveWhere(Func<string, bool> match)
    {
        var doomed = new List<string>();
        foreach (var key in _map.Keys)
        {
            if (match(key)) doomed.Add(key);
        }

        foreach (var key in doomed) Remove(key);
        return doomed.Count;
    }
}
