using System.Text;
using AltPane.Core.Dialogs;

namespace AltPane;

internal static class FileDialogProbe
{
    public static bool TryClassify(nint hwnd, out FileDialogKind kind, out bool save)
    {
        kind = FileDialogKind.None;
        save = false;
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd)) return false;
        if (!string.Equals(ClassName(hwnd), FileDialogClassifier.DialogClass, StringComparison.Ordinal))
            return false;
        var snapshot = Capture(hwnd);
        kind = FileDialogClassifier.Classify(snapshot);
        if (kind == FileDialogKind.None) return false;
        save = FileDialogClassifier.IsSaveDialog(snapshot.Title);
        return true;
    }

    public static DialogSnapshot Capture(nint hwnd)
    {
        var controls = new List<DialogControl>();
        EnumWindowsProc callback = (child, _) =>
        {
            controls.Add(new DialogControl(ClassName(child), FileDialogNative.GetDlgCtrlID(child)));
            return true;
        };
        NativeMethods.EnumChildWindows(hwnd, callback, 0);
        GC.KeepAlive(callback);
        return new DialogSnapshot
        {
            ClassName = ClassName(hwnd),
            Title = WindowText(hwnd),
            Descendants = controls
        };
    }

    static string ClassName(nint hwnd)
    {
        var buffer = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }

    static string WindowText(nint hwnd)
    {
        var buffer = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
