using System.Windows.Automation;
using AltPane.Core.Dialogs;

namespace AltPane;

/// <summary>
/// Filename edit of a file dialog, then the address bar. Enter in a dialog is the
/// default button (<c>DM_GETDEFID</c> / <c>WM_COMMAND</c>), which navigates when
/// the text is a folder and would otherwise dismiss the dialog.
/// </summary>
internal static class DialogEdit
{
    public static string Read(nint dialog)
    {
        var edit = Find(dialog);
        if (edit == 0) return "";
        var length = (int)FileDialogNative.SendMessage(edit, FileDialogNative.WmGetTextLength, 0, 0);
        if (length <= 0) return "";
        if (length > 4096) length = 4096;
        var buffer = new System.Text.StringBuilder(length + 1);
        FileDialogNative.SendMessage(edit, FileDialogNative.WmGetText, length + 1, buffer);
        return buffer.ToString();
    }

    public static void Write(nint dialog, string? text)
    {
        var edit = Find(dialog);
        if (edit == 0)
        {
            AppLog.Info($"file dialog filename edit missing on 0x{dialog:X}.");
            return;
        }
        var value = text ?? "";
        FileDialogNative.SendMessage(dialog, FileDialogNative.CdmSetControlText, FileDialogClassifier.FileNameControlId, value);
        FileDialogNative.SendMessage(edit, FileDialogNative.WmSetText, 0, value);
        var parent = FileDialogNative.GetParent(edit);
        if (parent != 0 && parent != edit)
        {
            var editId = FileDialogNative.GetDlgCtrlID(edit);
            FileDialogNative.SendMessage(parent, FileDialogNative.WmCommand, (FileDialogNative.EnChange << 16) | (editId & 0xFFFF), edit);
        }
        var combo = parent != 0 && parent != dialog ? parent : edit;
        var comboId = FileDialogNative.GetDlgCtrlID(combo);
        if (comboId == 0) comboId = FileDialogClassifier.FileNameControlId;
        FileDialogNative.SendMessage(dialog, FileDialogNative.WmCommand, (FileDialogNative.CbnEditChange << 16) | (comboId & 0xFFFF), combo);
    }

    public static void Submit(nint dialog)
    {
        // Enter in the filename box navigates when the text is a folder (trailing
        // slash) and would otherwise dismiss the dialog. Do not press OK when the
        // edit is missing, or the dialog can close on the old name.
        var edit = Find(dialog);
        if (edit == 0) return;
        var packed = FileDialogNative.SendMessage(dialog, FileDialogNative.DmGetDefId, 0, 0).ToInt64();
        var id = (int)(packed & 0xFFFF);
        var hasDefault = ((packed >> 16) & 0xFFFF) == FileDialogNative.DcHasDefId;
        if (!hasDefault || id == 0) id = 1;
        var button = FileDialogNative.GetDlgItem(dialog, id);
        FileDialogNative.SendMessage(dialog, FileDialogNative.WmCommand, id, button);
    }

    static nint Find(nint dialog)
    {
        nint combo = 0;
        nint legacy = 0;
        EnumWindowsProc callback = (child, _) =>
        {
            var id = FileDialogNative.GetDlgCtrlID(child);
            if (id == FileDialogClassifier.FileNameControlId) combo = child;
            else if (id == FileDialogClassifier.LegacyFileNameEditId && legacy == 0) legacy = child;
            return true;
        };
        NativeMethods.EnumChildWindows(dialog, callback, 0);
        GC.KeepAlive(callback);
        if (combo != 0)
        {
            var edit = FileDialogNative.FindWindowEx(combo, 0, "Edit", null);
            return edit != 0 ? edit : combo;
        }
        return legacy;
    }
}

internal static class DialogAddress
{
    public static string Read(nint dialog)
    {
        try
        {
            var root = AutomationElement.FromHandle(dialog);
            var match = root is null ? null : Find(root, 80);
            return match?.Current.Name ?? "";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return "";
        }
    }

    public static bool TrySet(nint dialog, string folder)
    {
        try
        {
            var root = AutomationElement.FromHandle(dialog);
            var match = root is null ? null : Find(root, 80);
            if (match is null) return false;
            var target = match;
            if (!target.TryGetCurrentPattern(ValuePattern.Pattern, out _))
            {
                var edit = target.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                if (edit is not null) target = edit;
            }
            if (!target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) || pattern is not ValuePattern)
            {
                if (target.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke) && invoke is InvokePattern invoker)
                {
                    invoker.Invoke();
                    var opened = target.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                    if (opened is not null) target = opened;
                }
            }
            if (target.TryGetCurrentPattern(ValuePattern.Pattern, out var ready) && ready is ValuePattern typed)
                typed.SetValue(folder);
            else
            {
                var hwndOnly = target.Current.NativeWindowHandle;
                if (hwndOnly == 0) return false;
                FileDialogNative.SendMessage(hwndOnly, FileDialogNative.WmSetText, 0, folder);
            }
            var hwnd = target.Current.NativeWindowHandle;
            if (hwnd != 0)
            {
                FileDialogNative.SendMessage(hwnd, FileDialogNative.WmKeyDown, FileDialogNative.VkReturn, 0x001C0001);
                FileDialogNative.SendMessage(hwnd, FileDialogNative.WmKeyUp, FileDialogNative.VkReturn, unchecked((nint)0xC01C0001));
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }

    static AutomationElement? Find(AutomationElement root, int budget)
    {
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        var seen = 0;
        while (queue.Count > 0 && seen < budget)
        {
            var node = queue.Dequeue();
            seen++;
            if (IsAddress(node)) return node;
            AutomationElement? child = null;
            try { child = TreeWalker.ControlViewWalker.GetFirstChild(node); }
            catch { child = null; }
            while (child is not null && seen < budget)
            {
                queue.Enqueue(child);
                try { child = TreeWalker.ControlViewWalker.GetNextSibling(child); }
                catch { break; }
            }
        }
        return null;
    }

    static bool IsAddress(AutomationElement node)
    {
        string cls;
        string name;
        try
        {
            cls = node.Current.ClassName ?? "";
            name = node.Current.Name ?? "";
        }
        catch
        {
            return false;
        }
        if (cls is "Breadcrumb Parent" or "Address Band Root" or "TravelBand" or "UniversalSearchBand" or "ComboBoxEx32")
            return true;
        if (name.Contains("Address", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains('地') && name.Contains('址')) return true;
        return false;
    }
}

internal static class ShellDialogBrowse
{
    const int BrowseSlot = 11;

    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    delegate int BrowseObjectDelegate(nint self, nint pidl, uint flags);

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint fetched);

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    static extern void ILFree(nint pidl);

    public static bool TryBrowse(nint dialog, string folder)
    {
        nint pidl = 0;
        try
        {
            var parsed = SHParseDisplayName(folder, 0, out pidl, 0, out _);
            if (parsed != 0 || pidl == 0)
            {
                AppLog.Info($"file dialog shell browse parse failed hr=0x{parsed:X8} path={folder}");
                return false;
            }
            var view = FindShellView(dialog);
            var browser = view == 0 ? 0 : FileDialogNative.SendMessage(view, FileDialogNative.WmGetIShellBrowser, 0, 0);
            if (!LooksLikePointer(browser))
                browser = FileDialogNative.SendMessage(dialog, FileDialogNative.WmGetIShellBrowser, 0, 0);
            if (!LooksLikePointer(browser))
            {
                AppLog.Info($"file dialog shell browse no view path={folder}");
                return false;
            }
            var hr = CallBrowse(browser, pidl);
            var ok = hr == 0;
            AppLog.Info($"file dialog shell browse ok={ok} hr=0x{hr:X8} view=0x{view:X} path={folder}");
            return ok;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
        finally
        {
            if (pidl != 0) ILFree(pidl);
        }
    }

    static bool LooksLikePointer(nint value) => value.ToInt64() > 0x10000;

    static int CallBrowse(nint browser, nint pidl)
    {
        var vtable = System.Runtime.InteropServices.Marshal.ReadIntPtr(browser);
        var slot = System.Runtime.InteropServices.Marshal.ReadIntPtr(vtable, BrowseSlot * IntPtr.Size);
        var browse = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<BrowseObjectDelegate>(slot);
        return browse(browser, pidl, FileDialogNative.SbspAbsolute | FileDialogNative.SbspSameBrowser);
    }

    static nint FindShellView(nint dialog)
    {
        nint found = 0;
        EnumWindowsProc callback = (child, _) =>
        {
            var buffer = new System.Text.StringBuilder(64);
            NativeMethods.GetClassName(child, buffer, buffer.Capacity);
            if (buffer.ToString() == "SHELLDLL_DefView")
            {
                found = child;
                return false;
            }
            return true;
        };
        NativeMethods.EnumChildWindows(dialog, callback, 0);
        GC.KeepAlive(callback);
        return found;
    }
}

internal sealed class LiveDialogSurface : IDialogSurface
{
    readonly nint _dialog;
    string _location;

    public LiveDialogSurface(nint dialog)
    {
        _dialog = dialog;
        _location = DialogAddress.Read(dialog);
        Kind = FileDialogProbe.TryClassify(dialog, out var kind, out _) ? kind : FileDialogKind.None;
    }

    public FileDialogKind Kind { get; }

    public string FileName
    {
        get => DialogEdit.Read(_dialog);
        set => DialogEdit.Write(_dialog, value);
    }

    public string CurrentLocation => _location;

    public void PrepareForJump()
    {
        if (_dialog != 0) WindowActions.TryActivate(_dialog);
    }

    public bool TryBrowseShell(string folder)
    {
        if (!ShellDialogBrowse.TryBrowse(_dialog, folder)) return false;
        var address = DialogAddress.Read(_dialog);
        _location = FolderPath.Same(address, folder) || FolderPath.Landed("", address, folder)
            ? address
            : folder;
        return true;
    }

    public void SubmitFileName()
    {
        var submitted = DialogEdit.Read(_dialog);
        DialogEdit.Submit(_dialog);
        var edit = DialogEdit.Read(_dialog);
        var address = DialogAddress.Read(_dialog);
        if (FolderPath.Landed(_location, address, submitted) || FolderPath.Same(address, submitted))
            _location = string.IsNullOrWhiteSpace(address) ? submitted : address;
        else if (!string.Equals(edit, submitted, StringComparison.OrdinalIgnoreCase))
            _location = string.IsNullOrWhiteSpace(address) ? submitted : address;
        else if (!string.IsNullOrWhiteSpace(address))
            _location = address;
    }

    public bool TryNavigateAddress(string folder)
    {
        if (!DialogAddress.TrySet(_dialog, folder)) return false;
        var address = DialogAddress.Read(_dialog);
        if (string.IsNullOrWhiteSpace(address)
            || FolderPath.Landed(_location, address, folder)
            || FolderPath.Same(address, folder))
        {
            _location = string.IsNullOrWhiteSpace(address) ? folder : address;
            return true;
        }
        _location = address;
        return FolderPath.Landed("", address, folder);
    }
}
