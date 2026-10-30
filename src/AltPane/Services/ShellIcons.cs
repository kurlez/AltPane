using System.Runtime.InteropServices;
using System.Text;
using AltPane.Core.Icons;

namespace AltPane;

/// <summary>
/// Shell icon retrieval. File icons prefer IShellItemImageFactory with SIIGBF_ICONONLY
/// and fall back to the system image list (jumbo when the requested edge is above 48).
/// Callers run this off the UI thread.
/// </summary>
internal static class ShellIcons
{
    const int CoInitApartment = 2;
    const uint SiigbfIconOnly = 0x4;
    const uint SiigbfBiggerOk = 0x1;
    const uint ShgfiSysIconIndex = 0x4000;
    const uint ShgfiUseFileAttributes = 0x10;
    const uint FileAttributeNormal = 0x80;
    const int ShilLarge = 0;
    const int ShilExtraLarge = 2;
    const int ShilJumbo = 4;
    const int IldTransparent = 1;
    const ushort VtLpWstr = 31;
    const ushort VtBstr = 8;
    const int ErrorInsufficientBuffer = 122;

    static readonly Guid ImageFactoryId = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    static readonly Guid ImageListId = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
    static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    static readonly PropertyKey AppUserModelId = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    static readonly PropertyKey RelaunchIconResource = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 3);

    internal readonly struct Identity
    {
        public Identity(string aumid, string relaunch, bool packaged, string packagePath)
        {
            Aumid = aumid ?? "";
            Relaunch = relaunch ?? "";
            Packaged = packaged;
            PackagePath = packagePath ?? "";
        }

        public string Aumid { get; }
        public string Relaunch { get; }
        public bool Packaged { get; }
        public string PackagePath { get; }
    }

    public static bool TryFile(string? path, int index, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (string.IsNullOrWhiteSpace(path)) return false;
        var com = BeginCom();
        try
        {
            return TryFileCore(path.Trim(), index, Normalize(edge), out pixels);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
        finally
        {
            EndCom(com);
        }
    }

    public static bool TryWindowBig(nint hwnd, out IconLoader.IconPixels pixels) =>
        TryCopiedIcon(hwnd, classIcon: false, out pixels);

    public static bool TryClassIcon(nint hwnd, out IconLoader.IconPixels pixels) =>
        TryCopiedIcon(hwnd, classIcon: true, out pixels);

    public static Identity ReadIdentity(nint hwnd, int processId, int hostedProcessId, string? processName)
    {
        var com = BeginCom();
        try
        {
            return ReadIdentityCore(hwnd, processId, hostedProcessId, processName);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return new Identity("", "", HostExecutables.IsFrameHost(processName), "");
        }
        finally
        {
            EndCom(com);
        }
    }

    public static bool TryPackage(in Identity identity, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        var com = BeginCom();
        try
        {
            return TryPackageCore(identity, Normalize(edge), out pixels);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
        finally
        {
            EndCom(com);
        }
    }

    public static bool TryRelaunch(string? resource, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (!RelaunchIcon.TryParse(resource, out var parsed)) return false;
        var com = BeginCom();
        try
        {
            var path = Environment.ExpandEnvironmentVariables(parsed.Path);
            return TryExtract(path, parsed.Index, Normalize(edge), out pixels);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
        finally
        {
            EndCom(com);
        }
    }

    static bool TryCopiedIcon(nint hwnd, bool classIcon, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (hwnd == 0) return false;
        var com = BeginCom();
        var icon = nint.Zero;
        try
        {
            if (classIcon)
            {
                var cls = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GclHIcon);
                if (cls != 0) icon = NativeMethods.CopyIcon(cls);
            }
            else
            {
                NativeMethods.SendMessageTimeout(
                    hwnd,
                    NativeMethods.WmGetIcon,
                    NativeMethods.IconBig,
                    0,
                    NativeMethods.SmtoAbortIfHung,
                    80,
                    out var result);
                if (result != 0) icon = NativeMethods.CopyIcon(result);
            }

            return icon != 0 && IconBits.FromIcon(icon, out pixels);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
        finally
        {
            if (icon != 0) NativeMethods.DestroyIcon(icon);
            EndCom(com);
        }
    }

    static bool TryFileCore(string path, int index, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (!File.Exists(path) && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && path.IndexOf(',') > 0
            && RelaunchIcon.TryParse(path, out var embedded))
        {
            var embeddedPath = Environment.ExpandEnvironmentVariables(embedded.Path);
            if (TryExtract(embeddedPath, embedded.Index, edge, out pixels)) return true;
        }

        if (IsShellParsingName(path))
        {
            var parsing = path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                ? path
                : "shell:AppsFolder\\" + path;
            return TryImageFactory(parsing, edge, out pixels);
        }

        if (index != 0 && TryExtract(path, index, edge, out pixels))
            return true;
        if (TryImageFactory(path, edge, out pixels)) return true;
        if (TryImageList(path, edge, out pixels)) return true;
        return index == 0 && TryExtract(path, 0, edge, out pixels);
    }

    static bool IsShellParsingName(string path) =>
        path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
        || (path.IndexOf('!') >= 0 && path.IndexOf('\\') < 0 && path.IndexOf(':') < 0);

    static bool TryPackageCore(in Identity identity, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        var aumid = StripAppsFolder(identity.Aumid);
        if (aumid.Length > 0 && TryImageFactory("shell:AppsFolder\\" + aumid, edge, out pixels))
            return true;
        return TryManifestLogo(identity, aumid, edge, out pixels);
    }

    static bool TryManifestLogo(in Identity identity, string aumid, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (identity.PackagePath.Length == 0 || !Directory.Exists(identity.PackagePath)) return false;
        var manifestPath = Path.Combine(identity.PackagePath, "AppxManifest.xml");
        if (!File.Exists(manifestPath)) return false;
        string xml;
        try
        {
            xml = File.ReadAllText(manifestPath);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }

        var selected = PackageManifest.Select(PackageManifest.Read(xml), PackageManifest.ApplicationId(aumid));
        if (selected is not { Square44: { } logo }) return false;
        var files = LogoFiles(identity.PackagePath, logo);
        var choice = PackageLogoPicker.Choose(logo, selected.Value.Background, files, edge);
        if (choice is null) return false;
        var full = Path.Combine(identity.PackagePath, choice.Value.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!TryPng(full, out var raw)) return false;
        raw = Fit(raw, edge);
        if (choice.Value.NeedsPlate && IconColor.TryParse(choice.Value.Background, out var r, out var g, out var b))
        {
            var plated = IconPlate.Over(raw.Bgra, raw.Width, raw.Height, b, g, r, raw.Premultiplied);
            pixels = new IconLoader.IconPixels(raw.Width, raw.Height, plated, premultiplied: false);
            return true;
        }

        pixels = raw;
        return true;
    }

    static List<string> LogoFiles(string root, string logo)
    {
        var list = new List<string>();
        var relativeDir = Path.GetDirectoryName(logo) ?? "";
        var fullDir = relativeDir.Length == 0 ? root : Path.Combine(root, relativeDir);
        if (!Directory.Exists(fullDir)) return list;
        var stem = Path.GetFileNameWithoutExtension(logo);
        if (stem.Length == 0) return list;
        try
        {
            foreach (var file in Directory.EnumerateFiles(fullDir))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!name.Equals(stem, StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase))
                    continue;
                var relative = relativeDir.Length == 0
                    ? Path.GetFileName(file)
                    : Path.Combine(relativeDir, Path.GetFileName(file));
                list.Add(relative);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }

        return list;
    }

    static bool TryPng(string path, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        try
        {
            using var image = new System.Drawing.Bitmap(path);
            var width = image.Width;
            var height = image.Height;
            if (width <= 0 || height <= 0 || width > 1024 || height > 1024) return false;
            var rect = new System.Drawing.Rectangle(0, 0, width, height);
            var data = image.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var stride = data.Stride;
                var abs = Math.Abs(stride);
                var bgra = new byte[width * height * 4];
                var row = new byte[abs];
                for (var y = 0; y < height; y++)
                {
                    var sourceY = stride >= 0 ? y : height - 1 - y;
                    Marshal.Copy(data.Scan0 + sourceY * stride, row, 0, abs);
                    Buffer.BlockCopy(row, 0, bgra, y * width * 4, width * 4);
                }

                pixels = new IconLoader.IconPixels(width, height, bgra, premultiplied: false);
                return true;
            }
            finally
            {
                image.UnlockBits(data);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }

    static Identity ReadIdentityCore(nint hwnd, int processId, int hostedProcessId, string? processName)
    {
        var aumid = "";
        var relaunch = "";
        if (hwnd != 0)
            ReadWindowProperties(hwnd, ref aumid, ref relaunch);

        var pid = HostExecutables.IsFrameHost(processName) && hostedProcessId > 0
            ? hostedProcessId
            : processId;
        var packaged = aumid.Length > 0 || HostExecutables.IsFrameHost(processName);
        var packagePath = "";
        if (pid > 0)
        {
            var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
            if (process != 0)
            {
                try
                {
                    if (aumid.Length == 0) aumid = QueryUserModelId(process);
                    var fullName = QueryPackageFullName(process);
                    if (fullName.Length > 0)
                    {
                        packaged = true;
                        packagePath = QueryPackagePath(fullName);
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(process);
                }
            }
        }

        if (aumid.Length > 0) packaged = true;
        return new Identity(aumid, relaunch, packaged, packagePath);
    }

    static void ReadWindowProperties(nint hwnd, ref string aumid, ref string relaunch)
    {
        var iid = PropertyStoreId;
        var hr = SHGetPropertyStoreForWindow(hwnd, ref iid, out var store);
        if (hr != 0 || store is null) return;
        try
        {
            var idKey = AppUserModelId;
            if (store.GetValue(ref idKey, out var idValue) == 0)
                aumid = ReadString(ref idValue);
            var iconKey = RelaunchIconResource;
            if (store.GetValue(ref iconKey, out var iconValue) == 0)
                relaunch = ReadString(ref iconValue);
        }
        finally
        {
            if (Marshal.IsComObject(store)) Marshal.ReleaseComObject(store);
        }
    }

    static string ReadString(ref PropVariant value)
    {
        try
        {
            if (value.Pointer == 0 || value.Vt is not (VtLpWstr or VtBstr)) return "";
            return Marshal.PtrToStringUni(value.Pointer) ?? "";
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    static string QueryUserModelId(nint process) => Query(process, GetApplicationUserModelId);

    static string QueryPackageFullName(nint process) => Query(process, GetPackageFullName);

    static string Query(nint process, PackageQuery query)
    {
        var buffer = new StringBuilder(260);
        var length = buffer.Capacity;
        var rc = query(process, ref length, buffer);
        if (rc == ErrorInsufficientBuffer && length > 1)
        {
            buffer = new StringBuilder(length);
            rc = query(process, ref length, buffer);
        }

        return rc == 0 ? buffer.ToString() : "";
    }

    static string QueryPackagePath(string fullName)
    {
        if (fullName.Length == 0) return "";
        var buffer = new StringBuilder(260);
        var length = buffer.Capacity;
        var rc = GetPackagePathByFullName(fullName, ref length, buffer);
        if (rc == ErrorInsufficientBuffer && length > 1)
        {
            buffer = new StringBuilder(length);
            rc = GetPackagePathByFullName(fullName, ref length, buffer);
        }

        return rc == 0 ? buffer.ToString() : "";
    }

    static bool TryImageFactory(string path, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (string.IsNullOrWhiteSpace(path)) return false;
        var iid = ImageFactoryId;
        var hr = SHCreateItemFromParsingName(path, 0, ref iid, out var factory);
        if (hr != 0 || factory is null) return false;
        var bitmap = nint.Zero;
        try
        {
            hr = factory.GetImage(new IconSize(edge, edge), SiigbfIconOnly | SiigbfBiggerOk, out bitmap);
            if (hr != 0 || bitmap == 0) return false;
            if (!IconBits.FromBitmap(bitmap, premultiplied: true, out pixels)) return false;
            pixels = Fit(pixels, edge);
            return pixels.Width > 0;
        }
        finally
        {
            if (bitmap != 0) NativeMethods.DeleteObject(bitmap);
            if (Marshal.IsComObject(factory)) Marshal.ReleaseComObject(factory);
        }
    }

    static bool TryImageList(string path, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        var info = new ShFileInfo();
        var flags = ShgfiSysIconIndex;
        if (!File.Exists(path))
        {
            if (IconCacheKey.IsPathSpecific(path, 0)) return false;
            flags |= ShgfiUseFileAttributes;
        }

        var list = SHGetFileInfo(path, FileAttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
        if (list == 0) return false;
        var kind = IconPixelSize.UseJumbo(edge) ? ShilJumbo : edge > 32 ? ShilExtraLarge : ShilLarge;
        if (TryListIcon(kind, info.IconIndex, edge, out pixels)) return true;
        if (kind != ShilExtraLarge && TryListIcon(ShilExtraLarge, info.IconIndex, edge, out pixels)) return true;
        return kind != ShilLarge && TryListIcon(ShilLarge, info.IconIndex, edge, out pixels);
    }

    static bool TryListIcon(int kind, int index, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        var iid = ImageListId;
        var hr = SHGetImageList(kind, ref iid, out var images);
        if (hr != 0 || images is null) return false;
        var icon = nint.Zero;
        try
        {
            hr = images.GetIcon(index, IldTransparent, out icon);
            if (hr != 0 || icon == 0) return false;
            if (!IconBits.FromIcon(icon, out pixels)) return false;
            pixels = Fit(pixels, edge);
            return pixels.Width > 0;
        }
        finally
        {
            if (icon != 0) NativeMethods.DestroyIcon(icon);
            if (Marshal.IsComObject(images)) Marshal.ReleaseComObject(images);
        }
    }

    static bool TryExtract(string path, int index, int edge, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return false;
        var large = nint.Zero;
        var small = nint.Zero;
        try
        {
            var hr = SHDefExtractIcon(path, index, 0, out large, out small, (uint)edge);
            if (hr == 0 && large != 0 && IconBits.FromIcon(large, out pixels))
            {
                pixels = Fit(pixels, edge);
                return pixels.Width > 0;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        finally
        {
            if (large != 0) NativeMethods.DestroyIcon(large);
            if (small != 0) NativeMethods.DestroyIcon(small);
        }

        var extracted = nint.Zero;
        try
        {
            var count = NativeMethods.PrivateExtractIcons(path, index, edge, edge, out extracted, out _, 1, 0);
            if (count <= 0 || extracted == 0) return false;
            if (!IconBits.FromIcon(extracted, out pixels)) return false;
            pixels = Fit(pixels, edge);
            return pixels.Width > 0;
        }
        finally
        {
            if (extracted != 0) NativeMethods.DestroyIcon(extracted);
        }
    }

    static IconLoader.IconPixels Fit(in IconLoader.IconPixels pixels, int edge)
    {
        if (pixels.Width <= edge && pixels.Height <= edge) return pixels;
        var bytes = IconScale.Box(pixels.Bgra, pixels.Width, pixels.Height, edge);
        if (ReferenceEquals(bytes, pixels.Bgra) || bytes.Length < edge * edge * 4) return pixels;
        return new IconLoader.IconPixels(edge, edge, bytes, pixels.Premultiplied);
    }

    static int Normalize(int edge) => edge < IconPixelSize.Minimum ? IconPixelSize.Minimum : edge;

    static string StripAppsFolder(string aumid)
    {
        const string prefix = "shell:AppsFolder\\";
        if (aumid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return aumid[prefix.Length..];
        return aumid.Trim();
    }

    static int BeginCom()
    {
        var hr = CoInitializeEx(0, CoInitApartment);
        return hr is 0 or 1 ? 1 : 0;
    }

    static void EndCom(int cookie)
    {
        if (cookie != 0) CoUninitialize();
    }

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(nint reserved, int coInit);

    [DllImport("ole32.dll")]
    static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(
        string path,
        nint bind,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    static extern int SHGetImageList(
        int imageList,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IImageList list);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern nint SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint cb, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHDefExtractIconW")]
    static extern int SHDefExtractIcon(string path, int index, uint flags, out nint large, out nint small, uint size);

    [DllImport("shell32.dll")]
    static extern int SHGetPropertyStoreForWindow(
        nint hwnd,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetApplicationUserModelId(nint process, ref int length, StringBuilder? id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackageFullName(nint process, ref int length, StringBuilder? fullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackagePathByFullName(string fullName, ref int length, StringBuilder? path);

    delegate int PackageQuery(nint process, ref int length, StringBuilder? buffer);

    [StructLayout(LayoutKind.Sequential)]
    struct IconSize
    {
        public IconSize(int edgeX, int edgeY)
        {
            Cx = edgeX;
            Cy = edgeY;
        }

        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public PropertyKey(Guid format, uint id)
        {
            Format = format;
            Id = id;
        }

        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public nint Pointer;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage([In, MarshalAs(UnmanagedType.Struct)] IconSize size, uint flags, out nint bitmap);
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IImageList
    {
        [PreserveSig] int Add(nint image, nint mask, out int index);
        [PreserveSig] int ReplaceIcon(int index, nint icon, out int replaced);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int index, nint image, nint mask);
        [PreserveSig] int AddMasked(nint image, int mask, out int index);
        [PreserveSig] int Draw(nint parameters);
        [PreserveSig] int Remove(int index);
        [PreserveSig] int GetIcon(int index, int flags, out nint icon);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
