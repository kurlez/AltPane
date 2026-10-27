namespace AltPane.Core.Input;

public static class VirtualKeys
{
    public const int Tab = 0x09;
    public const int Escape = 0x1B;
    public const int Return = 0x0D;
    public const int Space = 0x20;
    public const int Back = 0x08;
    public const int Delete = 0x2E;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Home = 0x24;
    public const int End = 0x23;
    public const int Prior = 0x21;
    public const int Next = 0x22;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    /// <summary>Unassigned. Injected while Win is down so a swallowed chord is not a lone Win tap.</summary>
    public const int Unused = WinChordGuard.DummyVirtualKey;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;
    public const int E = 0x45;
    public const int F = 0x46;
    public const int S = 0x53;
    public const int W = 0x57;
    public const int M = 0x4D;
    public const int R = 0x52;
    public const int A = 0x41;
    public const int B = 0x42;
    public const int P = 0x50;
    public const int U = 0x55;
}
