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

public static class PlacementEdits
{
    public const int ShowMinimized = 2;
    public const int ShowMaximized = 3;
    public const int ShowNoActivate = 4;
    public const int RestoreToMaximized = 0x0002;

    public static bool WasMaximized(PlacementSnapshot original) =>
        (original.Flags & RestoreToMaximized) != 0 || original.ShowCmd == ShowMaximized;
}
