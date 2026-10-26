namespace AltPane.Core.Preview;

public readonly record struct PlacementSnapshot(
    int Flags,
    int ShowCmd,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    int NormalLeft,
    int NormalTop,
    int NormalRight,
    int NormalBottom);

public readonly record struct PreviewStep(
    long ReturnHandle,
    PlacementSnapshot? ReturnPlacement,
    long BorrowHandle,
    long ReleaseHandle,
    PlacementSnapshot? ReleasePlacement);

public static class PlacementEdits
{
    public const int ShowMinimized = 2;
    public const int ShowMaximized = 3;
    public const int ShowNoActivate = 4;
    public const int RestoreToMaximized = 0x0002;

    public static PlacementSnapshot OffScreen(PlacementSnapshot original, int virtualLeft, int virtualTop)
    {
        var width = original.NormalRight - original.NormalLeft;
        var height = original.NormalBottom - original.NormalTop;
        if (width < 80) width = 800;
        if (height < 60) height = 600;
        var left = virtualLeft - width - 80;
        var top = virtualTop;
        return original with
        {
            Flags = original.Flags & ~RestoreToMaximized,
            ShowCmd = ShowNoActivate,
            NormalLeft = left,
            NormalTop = top,
            NormalRight = left + width,
            NormalBottom = top + height
        };
    }

    public static PlacementSnapshot ForActivate(PlacementSnapshot original)
    {
        var maximized = (original.Flags & RestoreToMaximized) != 0 || original.ShowCmd == ShowMaximized;
        return original with { ShowCmd = maximized ? ShowMaximized : ShowNoActivate };
    }

    public static bool WasMaximized(PlacementSnapshot original) =>
        (original.Flags & RestoreToMaximized) != 0 || original.ShowCmd == ShowMaximized;
}

/// <summary>
/// Tracks a minimized window that was temporarily restored for a thumbnail.
/// </summary>
public sealed class MinimizedPreviewSession
{
    public long Handle { get; private set; }
    public PlacementSnapshot? Original { get; private set; }
    public bool IsBorrowing => Handle != 0 && Original is not null;

    public PreviewStep Consider(long handle, bool iconic, bool auto, bool manual)
    {
        if (IsBorrowing && Handle == handle)
            return default;

        var returning = TakeReturn();
        var borrow = iconic && (auto || manual) ? handle : 0;
        return returning with { BorrowHandle = borrow };
    }

    public void NoteBorrowed(long handle, PlacementSnapshot original)
    {
        Handle = handle;
        Original = original;
    }

    public void NoteFailed()
    {
        Handle = 0;
        Original = null;
    }

    public PreviewStep Commit()
    {
        if (!IsBorrowing || Original is null)
            return default;
        var step = new PreviewStep(0, null, 0, Handle, PlacementEdits.ForActivate(Original.Value));
        NoteFailed();
        return step;
    }

    public PreviewStep Cancel() => TakeReturn();

    PreviewStep TakeReturn()
    {
        if (!IsBorrowing || Original is null)
            return default;
        var step = new PreviewStep(Handle, Original, 0, 0, null);
        NoteFailed();
        return step;
    }
}
