# AltPane 0.4.9

## 中文

列表里的图标改为系统里的真实图标。文件搜索（Everything 或内置索引）不再用蓝底字母方块占位；窗口、常用应用侧栏和 Win 键应用列表也按同一套来源取图标。

- **文件。** 每一行用 `IShellItemImageFactory`（只要图标，不要缩略图）或系统大号/超大号图像列表取出该文件的外壳图标。普通类型按扩展名缓存，`.exe`、`.lnk`、`.ico`、`.url` 按完整路径缓存。列表先画出来，图标在后台补上。取不到才退回字母方块。
- **窗口和应用。** 优先用窗口自己的大图标（`WM_GETICON` / `GCLP_HICON`），然后是主程序的大号图标，最后才是快捷方式。UWP 和带包标识的进程通过 AUMID 走 `shell:AppsFolder`，不行再读清单里的 `Square44x44Logo`（按缩放选尺寸，无底板的图标会铺上清单里的背景色）。Chrome、Edge 这类共用宿主的窗口优先用窗口自己的图标或重新启动图标，避免所有窗口都变成浏览器图标。
- **清晰度。** 按当前 DPI 至少请求 32 像素乘以缩放倍数，需要时用超大号图像列表再缩小，不再把 16 像素图标拉大。可执行文件路径变了就丢掉旧图标。缓存有上限，最近不用的会释放，位图在放进界面前冻结。

## English

List icons are the real shell icons. File search (Everything or the built-in index) no longer uses a letter tile, and the same sources feed the window list, the frequent-apps sidebar, and the Win-key app list.

- **Files.** Each row asks `IShellItemImageFactory` for the icon only, or the large/jumbo system image list. Ordinary types are cached by extension. `.exe`, `.lnk`, `.ico`, and `.url` are cached by full path. The list paints immediately and the icons fill in on a background thread. The letter tile is only the failure case.
- **Windows and apps.** Order is the window's big icon (`WM_GETICON` / `GCLP_HICON`), then the main executable at a large size, then the shortcut. UWP and packaged processes resolve the AUMID through `shell:AppsFolder`, then the manifest `Square44x44Logo` (the scale or target size that fits, plated with the manifest background when the asset is unplated). Chrome, Edge, and the other shared hosts prefer the window icon or the relaunch icon so every window is not the browser icon.
- **Sharpness.** The request is at least 32 device pixels times the DPI scale, using the jumbo image list and scaling down when that is larger. A 16px icon is not stretched up. Changing an executable path drops the old icon. The cache is bounded, and bitmaps are frozen before they are shown.
