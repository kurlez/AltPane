# AltPane

AltPane is a Windows desktop task switcher that replaces the built-in Alt+Tab UI while it is running. It lists the windows that would normally appear in Alt+Tab, shows a live DWM thumbnail of the selection, and stays open when you release the trigger key. Enter, or a click, switches to the selected window.

The interface follows the Windows display language when it is Chinese, and can be switched between Simplified Chinese and English in Settings.

This is an independent project. It is not affiliated with NTWind or the commercial product Alt-Tab Terminator.

## Features

- Low-level keyboard hook overrides Alt+Tab and Alt+Shift+Tab. By default, releasing the trigger key leaves the switcher open (sticky mode). Enter or a click activates the selected window. Esc, or a click outside the switcher, cancels and restores the previous foreground window. Classic release-to-switch is a setting.
- The hotkey is configurable. Win+Tab opens AltPane as well, unless that setting is turned off.
- The switcher lists visible top-level windows in Z-order, with icon, title, and process name. Tool windows, cloaked UWP ghosts, owned popups, and a small shell-class denylist are filtered out. ApplicationFrameHost windows use the hosted CoreWindow for the real process name, icon, and “end task” target.
- Live preview uses `DwmRegisterThumbnail`.
- Type-to-search filters by window title or process name (substring, acronym, and pinyin). After the trigger key is released, the search box takes keyboard focus, so an IME can compose without Ctrl+F.
- The same box also launches installed apps, in the style of Listary. Open windows stay first. A separate Launch section lists matching Start Menu shortcuts, Microsoft Store apps, and desktop shortcuts (PATH executables are optional and off by default). `vsc` matches Visual Studio Code; `wx` or `weixin` matches 微信. Enter launches the app and closes the switcher, or switches to it when a window is already open (shown with an Open badge). Ctrl+Enter runs it as administrator. The context menu can open the file location. The index is built in the background at startup, cached in `%APPDATA%\AltPane\apps.json`, and refreshed by a shortcut watcher plus a full scan every 10 minutes, so opening the switcher never waits on it. How often you launch or switch to an app is stored in `launches.json` and nudges ranking when match quality is close. An empty query lists windows only.
- While the switcher is open it shows machine CPU and memory, and each task’s CPU and working set. Helper processes are rolled into the window that owns them (browsers, and UWP apps hosted by ApplicationFrameHost). Heavy tasks get a warm or hot bar. Sort by recent use, CPU, or memory. Sampling runs on a background thread only while the switcher is visible.
- Minimized windows can be restored off-screen for a live thumbnail (Space, the context menu, or a setting that does it automatically). If you do not switch to that window it is minimized again, including a window that was maximized before it was minimized. A previous snapshot is shown when one was captured.
- Keyboard and mouse navigation, plus close, end task, minimize, maximize, and restore.
- The window opens on the monitor under the cursor or the monitor of the active window. The process is per-monitor DPI aware (v2). On every other connected monitor, a small reminder points back at the switcher. It does not take focus. Clicking it moves the switcher to that monitor. A single monitor shows nothing extra. The reminder can be turned off in Settings.
- With more than one monitor, each row shows which display that window is on: a small screen icon and a number, in a color shared with the reminder that points at that display. The number is the Windows `DISPLAY` index when those indexes are unique, and left-to-right otherwise. A window that spans displays uses the one with the largest overlap. A minimized window uses its restore position. The preview reads like “Display 2 · 2560×1440 · Primary”. Ctrl+Shift+D, or the This display button, lists only the windows on the switcher’s display. One monitor, or the setting, hides the badges.
- System tray menu: open the switcher, Settings, pause/resume the override, start with Windows, exit. A second launch asks the running instance to open Settings.
- Settings are stored as JSON in `%APPDATA%\AltPane\settings.json`. A corrupt file is copied to `settings.json.bak` and defaults are loaded.
- Pausing, exiting, or an unhandled exception removes the hook. If the process is killed, Windows drops the hook as well, so the native switcher works again.

## Keyboard shortcuts

| Action | Gesture |
| --- | --- |
| Open and move to the previous window | Alt+Tab (or your hotkey). Win+Tab does this too, and is on by default |
| Open and move the other way | Alt+Shift+Tab, when Shift is not part of the hotkey. Win+Shift+Tab reverses a Win+Tab session |
| Win+Tab | Opens the switcher and leaves it open after both keys are released. Enter switches, Esc cancels. Tab or Win+Tab cycles. Task View does not open. A Windows key on its own still opens Start. Win+E, Win+D, and Win+Shift+S are unchanged |
| Other displays | A reminder on each other monitor points at the switcher. Click it to move the switcher there. Nothing appears when only one monitor is connected |
| Window’s display | Each row shows the display number. The preview adds the resolution and whether it is the main display. Ctrl+Shift+D shows only windows on the switcher’s display |
| Activate the selection | Enter, or click a row. Space does this when the window is not minimized and the search box is empty. In classic mode (Settings), releasing the trigger modifier also activates |
| Launch an app | Type part of its name, move to the Launch row, and press Enter. If that app already has a window, Enter switches to it instead |
| Run as administrator | Ctrl+Enter on a Launch row, or the context menu. Store apps cannot be elevated. Cancelling the UAC prompt does not count as a launch |
| Open the app’s file location | Context menu on a Launch row |
| Keep the switcher open | Release the trigger modifier. This is the default. Alt+Tab while it is open keeps cycling |
| Preview a minimized window | Space, or the context menu item “Restore to preview”. The window is restored off-screen without taking focus. Moving away or cancelling minimizes it again. Switching to it leaves it restored. A saved snapshot, when one exists, shows with a Minimized badge |
| Cancel | Esc, or click outside the switcher |
| Move the selection | Tab, Shift+Tab, arrow keys, Home, End, Page Up, Page Down, mouse wheel. These work after the trigger key is released |
| Filter or find an app | Type, with or without holding the trigger. Backspace edits the query. Initials and pinyin match Chinese names (`wx`, `weixin`, and `yinyue` for 音乐). An empty box does not list the whole app catalog |
| Search with an IME | Release the trigger key. The search box is focused, so a Chinese IME can compose. Ctrl+F, or a click on the search box, does the same while the key is still held. Enter confirms |
| Close the window | Delete, the Close button, the context menu, or middle-click a row |
| End the task | Shift+Delete, the End task button, or the context menu. When confirmation is on, press it again. Child processes are included when that setting is on |
| End the process tree | Context menu, even if the child-process setting is off |
| Minimize / maximize / restore | Ctrl+M, Ctrl+Shift+M, Ctrl+R, the buttons, or the context menu |
| Select under the pointer | Hover a row |
| Activate with the mouse | Click a row |

A very fast Alt+Tab, released before the window is drawn, leaves the switcher open in the default sticky mode. Settings has “Still switch on a very quick tap” for the old behavior: that tap activates the previous window without flashing the switcher. Classic mode (sticky off) always does that.

While the trigger is held, typed characters are read with `ToUnicode` and the Alt bits cleared. That path is for Latin letters, digits, and symbols (process names and English titles). Releasing the trigger focuses the real search box and passes keys through, including IME composition. Ctrl+F or a click on the search box does the same thing before the key is released. Press Enter to switch. The hook swallows the activation chord, and it does not swallow the keys the search box needs.

If the hotkey itself includes Shift, Shift does not reverse direction. Use the arrow keys.

## Run

Two win-x64 builds are published.

The smaller one is framework-dependent. Install the .NET 8 Desktop Runtime (x64) first, then run `AltPane.exe`. Download it from [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0) (Desktop Runtime, Windows x64) or the direct installer: <https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe>.

The self-contained build includes the runtime. Copy `AltPane.exe` to a Windows 10 or Windows 11 x64 machine and start it. No separate .NET install is required. The first launch unpacks native libraries and is slower than later launches.

Windows SmartScreen may warn because the executable is unsigned. Choose “More info”, then “Run anyway”, if you trust the binary you built.

AltPane starts in the notification area. The first launch shows a balloon. Right-click the icon for Settings, pause, startup, and exit. Left-click opens the switcher without stealing Alt+Tab (Enter confirms, Esc cancels).

A normal (non-admin) process cannot activate or thumbnail some elevated windows. Settings has **Restart as administrator**. The manifest stays `asInvoker`, so everyday use does not require elevation.

Startup registration writes the quoted path of the executable to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` under the value `AltPane`.

Logs: `%APPDATA%\AltPane\altpane.log` (rotated to `altpane.log.old` around 1 MB).

The app index is `%APPDATA%\AltPane\apps.json`. Launch counts and last-used times are `%APPDATA%\AltPane\launches.json`. Both are rewritten only after the background scan changes, so the first Alt+Tab after startup reads whatever is already in memory. Settings choose the sources (Start Menu, Store, Desktop, PATH) and an exclusion list. A name matches the display name, process name, or file name. A token that contains `\` or `/` hides every app under that folder. PATH indexing skips `Windows\System32`, `SysWOW64`, and `WinSxS`. Duplicate targets keep the Start Menu or Store name over a desktop shortcut.

## Build

Requires the .NET 8 SDK. On Windows:

```powershell
dotnet test tests/AltPane.Tests/AltPane.Tests.csproj -c Release
dotnet publish src/AltPane/AltPane.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

`publish\AltPane.exe` is the self-contained app. `build.ps1` runs the same two commands.

Framework-dependent single file (requires the .NET 8 Desktop Runtime linked above):

```powershell
dotnet publish src/AltPane/AltPane.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish-fx
```

The WPF project sets `EnableWindowsTargeting`, so the same publish command also works on Linux when the Windows desktop targeting pack can be downloaded. The UI cannot be executed there.

## Layout

- `src/AltPane.Core` — net8.0 library with no UI: window-filter rules, search (including the embedded pinyin table), the installed-app catalog and shell-link parser, the hook gesture state machine, settings load/save, switcher session, localization tables, and native struct sizes.
- `src/AltPane` — WPF app (plus a WinForms tray icon): the low-level hook, window enumeration, DWM thumbnails, switcher and settings windows.
- `tests/AltPane.Tests` — xUnit tests for the core library.

The hook procedure only classifies the key and queues a small operation. Enumeration and UI work run on the dispatcher, so the hook stays inside Windows’ low-level hook timeout. The delegate is rooted for the lifetime of the service. Pause and exit call `UnhookWindowsHookEx`.

## Tests

```powershell
dotnet test tests/AltPane.Tests/AltPane.Tests.csproj -c Release
```

Covered without a Windows desktop session: inclusion rules (visibility, tool windows, cloak, owner, empty title, exclusion list, ApplicationFrameHost), search ranking, pinyin and acronym matching, app ranking against launch frequency, shell-link (`.lnk`) parsing, session movement across windows and launch rows, hook interpretation (including sticky release, Win+Tab staying open, search mode, Ctrl+Enter, and the activation chord), the Win-key suppression state machine, other-monitor hint direction and placement, which display a window rect belongs to (largest overlap, minimized restore offset, GDI display numbers), settings JSON round-trip and corrupt-file recovery, kill-process policy, hotkey formatting, localization fallback, layout math, the opaque window-frame colors (no full-window glass margin), and the 48-byte DWM thumbnail property / 40-byte `INPUT` layouts.

Pinyin readings come from [mozillazg/pinyin-data](https://github.com/mozillazg/pinyin-data) (`pinyin.txt`, MIT). The compact table is `src/AltPane.Core/Search/pinyin.bin`. `ü` is stored as `v`, and a query may use `u` or `v` (`nu` and `nv` both match 女). Polyphonic characters keep their readings (乐 is `le` and `yue`).

Not covered here: the real hook, DWM thumbnails, tray icon, monitor placement, and activation. Those need a Windows machine. See the checklist below.

## Known limitations

- Win+Tab is taken by the low-level hook: Tab is not delivered, and an unassigned key (VK_E8) is injected while Win is still down so the later Win key-up does not open Start. Both Win keys are then released, so the Windows key is not left stuck. A low-level hook in a process that is not elevated does not see keystrokes while an elevated window is in front. Win+Tab then falls through to Task View until AltPane is restarted as administrator.
- Exclusive fullscreen, DRM, and some elevated windows produce a blank thumbnail. Minimized windows have no DWM thumbnail until they are restored for preview. Some apps ignore a no-activate restore or animate back to the taskbar; AltPane then puts the saved placement back and shows a failure message. Snapshot capture uses `PrintWindow` and is blank for many GPU-rendered windows.
- Switching to an elevated window, closing it, or thumbnailing it can fail with UIPI unless AltPane is running as administrator.
- “Other virtual desktops” is approximate. The shell cloak bit and `IVirtualDesktopManager` are used when available. The application cloak bit (UWP ghost windows) is always hidden.
- Protected processes (`explorer`, `csrss`, `lsass`, `svchost`, `dwm`, and similar) and an unresolved ApplicationFrameHost window cannot be killed from the switcher. Ending an elevated process needs AltPane to be running as administrator; the switcher says so when access is denied. CPU percent needs two samples, so the first fraction of a second shows memory and then CPU. GPU and disk load are not shown. Per-task CPU is a share of the whole machine. Memory is the working set, with private bytes in the tooltip, summed across helper processes.
- The switcher keeps `AllowsTransparency` off so DWM thumbnails work. The window fill is opaque. Windows 11 rounds it with `DWMWA_WINDOW_CORNER_PREFERENCE`; Windows 10 keeps square corners and the same 1px edge. A one-pixel frame extension is only there so DWM can draw a shadow. Mica and acrylic are not used, because a transparent client paints black when those effects are unavailable.
- The single-file executable is unsigned.
- Store apps are discovered through `shell:AppsFolder` on a background thread and that scan is best-effort. If it fails, Start Menu and desktop shortcuts are still indexed. A Store app is launched with `IApplicationActivationManager`, then `explorer.exe shell:AppsFolder\<aumid>` if that fails. Store apps have no “run as administrator” and no file location.
- Match quality outweighs habit when the gap is large: an exact name beats a heavily used loose match. Frequent use can lift a prefix above a never-used exact name. Pinyin covers the common readings in the embedded table, not every rare character.

## Manual test checklist

1. Start AltPane. Alt+Tab shows AltPane, not the Windows switcher. Release Alt: the switcher stays open, the search box has a caret, and Alt is not stuck and does not open a menu. Enter switches. Esc returns to the previous window. Clicking outside cancels. A quick tap also leaves the switcher open.
2. Hold Alt and press Tab / Shift+Tab. Arrows, Home, End, Page Up, and Page Down move the selection. After releasing Alt, the same keys still move it, and Alt+Tab cycles again. Turn off “Keep the switcher open…” and confirm releasing Alt switches immediately.
3. Type a process name while holding Alt and confirm the list filters. Release Alt and enter Chinese with an IME, then confirm with Enter. Ctrl+F still focuses search while Alt is held. Type `vsc` (or another installed app’s initials) and confirm a Launch row appears under the windows without a delay on open. Enter starts it and closes the switcher. If that app is already open, the row says so and Enter switches to it. Ctrl+Enter shows a UAC prompt for a normal shortcut and does nothing harmful if you cancel. The context menu opens the shortcut’s folder. `wx` or `weixin` finds 微信 when that shortcut is installed. Uncheck a source or add an exclusion in Settings and confirm the row disappears. Turn off “Suggest installed apps” and confirm only windows remain.
4. Delete sends close. Shift+Delete ends a normal task; with confirmation on, the second press ends it, including child processes when that setting is on. Explorer is refused. An elevated process reports access denied. Minimize, maximize, restore, right-click, and middle-click match their labels. CPU and memory update while the switcher stays open, and sorting by CPU or memory moves heavy tasks up.
5. Hover and the wheel change the selection without fighting the keyboard. The thumbnail tracks the selection, including a minimized placeholder.
6. Pause, then exit. Native Alt+Tab works in both cases. Kill `AltPane.exe` from Task Manager and confirm native Alt+Tab still works.
7. Change hotkey, theme, language, size, preview, placement, and exclusions. Restart and confirm `%APPDATA%\AltPane\settings.json` was applied. Toggle start with Windows and check the HKCU Run key.
8. Launch a second copy and confirm it focuses Settings on the first instance.
9. Move the cursor to another monitor and another DPI scale. Confirm the switcher opens there and is sharp.
10. Win+Tab opens AltPane, not Task View, and releasing Win does not open Start or leave Win stuck. The switcher stays open; Tab cycles; Win+Shift+Tab reverses; Enter switches; Esc cancels. A Windows key alone still opens Start. Win+E, Win+D, and Win+Shift+S still work. Turn the setting off and confirm Task View is back. With an elevated window in front, such as Task Manager, Win+Tab reaches AltPane only after Restart as administrator.
11. In light and dark themes, at 100%, 125%, 150%, and 200% scaling, the switcher and Settings have a solid fill and a thin edge. Windows 11 corners are rounded; Windows 10 corners are square. There is no black band in the corners or along the frame, and the shadow is soft. A window whose shape differs from the preview pane is centered; the bars around it are the pane color.
12. With two or more monitors, opening the switcher by Alt+Tab or Win+Tab shows a reminder centered on each other monitor. The arrow points at the monitor that has the switcher, in that monitor's language and scale. The reminder does not take focus, and typing still filters the switcher. Clicking a reminder moves the switcher there and the arrows follow. Unplug a monitor while it is open and the reminder disappears. One monitor shows no reminder. Turn the setting off and confirm the reminders stay hidden.
13. With two or more monitors, each row shows a colored display number. The preview names that display, its resolution, and Primary for the main one. The number’s color matches the reminder that points at the same display. A window dragged across the boundary is listed on the display that contains more of it. A minimized window is listed where it will restore, not at the off-screen icon position. Ctrl+Shift+D, and the This display button, hide windows on the other displays; press it again to show them all. One monitor, or the setting turned off, hides the badges.

## 中文说明

AltPane 是一个 Windows 桌面窗口切换器。运行期间它会用低级键盘钩子接管 Alt+Tab（以及 Alt+Shift+Tab），系统自带的切换界面不会出现。Win+Tab 默认也会打开 AltPane，而不是任务视图；松开 Win 不会弹出开始菜单，也不会让 Windows 键卡住。单独按 Windows 键仍打开开始菜单，Win+E、Win+D、Win+Shift+S 保持原样。默认松开触发键后切换器保持打开，Enter 或单击才切换到所选窗口，Esc 或点击外部取消。设置里可以改回松开即切换，也可以关闭 Win+Tab 接管。当前窗口以管理员身份运行时，需要同样以管理员身份启动 AltPane，钩子才能看到这个组合键。接了多台显示器时，没有显示切换器的屏幕中央会出现提示，箭头指向切换器所在的那一台，单击提示可把切换器移过去。提示不抢焦点。只有一台显示器时不会出现。可在设置中关闭。每一行会用编号和颜色标出窗口所在的显示器，预览写明分辨率，主显示器会标明。编号的颜色与指向该显示器的提示相同。Ctrl+Shift+D 或“本显示器”按钮只列出切换器所在屏幕上的窗口。只有一台显示器，或关闭该项时，不显示编号。

界面默认跟随系统语言：中文系统使用简体中文，也可在设置里改为 English。设置保存在 `%APPDATA%\AltPane\settings.json`。托盘图标可以打开切换器、设置、暂停接管、开机启动和退出。程序崩溃、暂停或结束后会卸下钩子，原生 Alt+Tab 恢复可用。

按住 Alt 时直接打字适合英文和进程名。松开 Alt 后搜索框会获得焦点，可以直接用中文输入法，再按 Enter 确认；按住时仍可用 Ctrl+F。搜索框同时可以启动已安装的应用：窗口仍排在前面，下面是“启动”分组。`vsc` 能找到 Visual Studio Code，`wx` 或 `weixin` 能找到微信。Enter 启动应用并关闭切换器；若该应用已有窗口，会标明“已打开”，Enter 改为切换过去。Ctrl+Enter 以管理员身份运行（商店应用不可以）。右键可打开文件所在位置。索引在后台建立，缓存在 `%APPDATA%\AltPane\apps.json`，使用次数在 `launches.json`，打开切换器不会等待扫描。默认索引开始菜单、商店应用和桌面快捷方式，PATH 默认关闭。搜索框为空时不列出全部应用。极快地按一下 Alt+Tab 默认也会保持打开，设置中可改为立即切换。切换器打开时会显示整机和每个任务的 CPU、内存，可按 CPU 或内存排序。Shift+Delete 结束任务（可在设置中要求再按一次确认，并结束子进程）。最小化窗口按空格可在不抢焦点的情况下移到屏幕外预览，不切换过去则会重新最小化。窗口填充是不透明的主题色，边缘是 1 像素描边。Windows 11 由系统裁成圆角，Windows 10 保持直角。阴影来自 DWM，不再把整窗做成透明玻璃，因此边角不会出现黑块。预览会按比例居中，周围是预览区的底色。普通权限无法切换或结束以管理员身份运行的窗口，需要在设置中“以管理员身份重新启动”。

较小的发布包依赖本机已安装的 .NET 8 桌面运行时（x64）：<https://dotnet.microsoft.com/download/dotnet/8.0> ，安装包直链 <https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe>。自包含的 `AltPane.exe` 不需要另外安装运行时；首次启动会解压本机库，会稍慢。文件未签名，SmartScreen 可能提示，确认来源后选择仍要运行。
