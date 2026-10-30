# AltPane 0.4.6

## 中文

0.4.5 的文件搜索在 Everything 没有运行、或 IPC 和 HTTP 都没有回应时，结果是空的。0.4.6 这时改用内置索引。窗口切换本身不变。

- **内置文件索引。** 默认开启，不需要管理员。后台线程以低优先级扫描 `%USERPROFILE%`，跳过隐藏目录、系统目录、`AppData`，以及原来的代理目录（`.gemini`、`.claude`、`.cursor`、`node_modules` 等）。桌面、文档、下载、图片、视频、音乐、OneDrive 和开始菜单如果不在这次扫描里面，会另外加进去。设置里可以再加文件夹。排除列表还是原来的那一份。
- **体积和更新。** 每条只存文件名和父目录编号，不给每个文件存一份完整路径。快照写在 `%LOCALAPPDATA%\AltPane\file-index.bin`，下次启动可以先搜再扫。`FileSystemWatcher` 防抖后更新；缓冲区溢出就整棵重扫。日志用 INFO 记下耗时、条数和内存。
- **搜索。** 不区分大小写的子串，多个词是 AND，中文名可以按拼音首字母。文档类型和 `f ` / `file:` 跟以前一样。Everything 连得上就用 Everything，否则自动用内置索引。分组标题是「内置索引」或「Everything」。
- **单按 Win 键。** 设置项「单按 Win 键打开 AltPane」默认关闭。打开后，只按下再松开左 Win 或右 Win、中间没有其他键，会打开应用列表，而不是开始菜单。钩子吃掉 Win 的松开，并注入 VK_E8，避免开始菜单弹出。Win+E、Win+R、Win+D、Win+Shift+S 以及其他 Win 组合键保持原样。列表来自当前用户和公共开始菜单的 `.lnk`，以及 `shell:AppsFolder` 里的商店应用，带图标。名称和拼音首字母都能过滤。Enter 启动，Ctrl+Shift+Enter 以管理员运行，底部提示条写出这些键。Alt+Tab 不受影响。

## English

File search in 0.4.5 returned nothing when Everything was stopped or neither IPC nor HTTP answered. 0.4.6 uses a built-in index in that case. The window switcher itself is unchanged.

- **Built-in file index.** On by default, and it does not need administrator rights. A low-priority background thread scans `%USERPROFILE%`, skipping hidden directories, system directories, `AppData`, and the existing agent directories (`.gemini`, `.claude`, `.cursor`, `node_modules`, and the rest). Desktop, Documents, Downloads, Pictures, Videos, Music, OneDrive, and the Start Menu are added when that walk will not reach them. Settings can add more folders. The exclusion list is the one already used for Everything.
- **Size and updates.** Each entry stores a file name and a parent-directory id, not a full path string. A snapshot in `%LOCALAPPDATA%\AltPane\file-index.bin` lets the next start search before the scan finishes. `FileSystemWatcher` applies changes after a debounce, and a buffer overflow triggers a full rescan. Duration, entry count, and memory are logged at INFO.
- **Search.** Case-insensitive substring, AND across words, and pinyin initials for Chinese names. Document types and the `f ` / `file:` prefix are unchanged. Everything is used when it answers; otherwise the built-in index is used. The group title reads 内置索引 or Everything.
- **Single Win key.** The setting 单按 Win 键打开 AltPane is off by default. When it is on, a left or right Win press and release with no other key in between opens the app list instead of the Start menu. The hook swallows the Win key-up and injects VK_E8 so Start does not open. Win+E, Win+R, Win+D, Win+Shift+S, and every other Win shortcut still work. The list is Start Menu `.lnk` files (user and common) plus Store apps from `shell:AppsFolder`, with icons. Names and pinyin initials both filter. Enter launches, Ctrl+Shift+Enter runs as administrator, and the hint bar shows those keys. Alt+Tab is unchanged.
