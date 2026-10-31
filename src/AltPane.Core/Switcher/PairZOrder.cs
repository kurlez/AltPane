namespace AltPane.Core.Switcher;

/// <summary>
/// Whether a side-by-side pair is actually in front. The list is top to bottom
/// and already omits hidden, cloaked, owned popups, and other monitors.
/// </summary>
public static class PairZOrder
{
    public static bool IsRaised(IReadOnlyList<long>? topToBottom, long focus, long other)
    {
        if (topToBottom is null || focus == 0 || other == 0 || focus == other) return false;
        var focusAt = IndexOf(topToBottom, focus);
        var otherAt = IndexOf(topToBottom, other);
        if (focusAt < 0 || otherAt < 0) return false;
        if (focusAt > otherAt) return false;
        for (var i = 0; i < otherAt; i++)
        {
            var handle = topToBottom[i];
            if (handle != focus && handle != other) return false;
        }
        return true;
    }

    static int IndexOf(IReadOnlyList<long> list, long handle)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == handle) return i;
        }
        return -1;
    }
}
