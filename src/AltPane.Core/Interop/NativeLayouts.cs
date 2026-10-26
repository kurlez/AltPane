using System.Runtime.InteropServices;

namespace AltPane.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
public struct NativePoint
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeSize
{
    public int Cx;
    public int Cy;
}

/// <summary>
/// DWM_THUMBNAIL_PROPERTIES. Explicit padding keeps the BOOL fields 4-byte aligned
/// after the opacity byte. Native size on Windows x64 is 48.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DwmThumbnailProperties
{
    public uint dwFlags;
    public NativeRect rcDestination;
    public NativeRect rcSource;
    public byte opacity;
    public byte pad1;
    public byte pad2;
    public byte pad3;
    public int fVisible;
    public int fSourceClientAreaOnly;
}

public static class DwmThumbnailFlags
{
    public const uint RectDestination = 0x00000001;
    public const uint RectSource = 0x00000002;
    public const uint Opacity = 0x00000004;
    public const uint Visible = 0x00000008;
    public const uint SourceClientAreaOnly = 0x00000010;
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeKeyboardInput
{
    public ushort VirtualKey;
    public ushort Scan;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeMouseInput
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeHardwareInput
{
    public uint Message;
    public ushort ParamL;
    public ushort ParamH;
}

[StructLayout(LayoutKind.Explicit)]
public struct NativeInputUnion
{
    [FieldOffset(0)] public NativeMouseInput Mouse;
    [FieldOffset(0)] public NativeKeyboardInput Keyboard;
    [FieldOffset(0)] public NativeHardwareInput Hardware;
}

/// <summary>
/// Win32 INPUT. On x64 the type field is padded so the union is 8-aligned and the
/// whole struct is 40 bytes. SendInput rejects a wrong size.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NativeInput
{
    public uint Type;
    public NativeInputUnion Data;
}
