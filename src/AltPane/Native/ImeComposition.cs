using System.Runtime.InteropServices;

namespace AltPane;

/// <summary>
/// True only when the window has a non-empty IMM composition string.
/// A missing imm32, or a window with no input context, is not composition.
/// WPF/TSF often has no IMM context; the text box reports its own composition
/// text separately, and an empty TextInputStart is not enough.
/// </summary>
static class ImeComposition
{
    const int GcsCompStr = 0x0008;

    public static bool HasComposition(nint hwnd)
    {
        if (hwnd == 0) return false;
        try
        {
            var context = ImmGetContext(hwnd);
            if (context == 0) return false;
            try
            {
                return ImmGetCompositionString(context, GcsCompStr, null, 0) > 0;
            }
            finally
            {
                ImmReleaseContext(hwnd, context);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("imm32.dll")]
    static extern nint ImmGetContext(nint hWnd);

    [DllImport("imm32.dll")]
    static extern bool ImmReleaseContext(nint hWnd, nint hImc);

    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    static extern int ImmGetCompositionString(nint hImc, int dwIndex, byte[]? lpBuf, int dwBufLen);
}
