using System.Runtime.InteropServices;
using System.Text;

namespace AltPane;

internal static class FileDialogNative
{
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;
    public const int WmSetText = 0x000C;
    public const int WmGetText = 0x000D;
    public const int WmGetTextLength = 0x000E;
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmCommand = 0x0111;
    public const int DmGetDefId = 0x0400;
    public const int DcHasDefId = 0x534B;
    public const int VkReturn = 0x0D;
    public const int EnChange = 0x0300;
    public const int CbnEditChange = 5;
    public const int WmUser = 0x0400;
    public const int WmNextDlgCtl = 0x0028;
    public const int CdmFirst = WmUser + 100;
    public const int CdmSetControlText = CdmFirst + 4;

    [DllImport("user32.dll")]
    public static extern int GetDlgCtrlID(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint GetParent(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint GetDlgItem(nint hDlg, int nIDDlgItem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowEx(nint parent, nint child, string? className, string? window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern nint SendMessage(nint hWnd, int msg, nint wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern nint SendMessage(nint hWnd, int msg, nint wParam, StringBuilder lParam);
}
