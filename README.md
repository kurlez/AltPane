# AltPane

AltPane is a Windows desktop task switcher that replaces the built-in Alt+Tab UI while it is running. It lists the windows that would normally appear in Alt+Tab, shows a live DWM thumbnail of the selection, and switches to that window when you release the trigger key.

The interface follows the Windows display language when it is Chinese, and can be switched between Simplified Chinese and English in Settings.

This is an independent project. It is not affiliated with NTWind or the commercial product Alt-Tab Terminator.

## Features

- Low-level keyboard hook overrides Alt+Tab and Alt+Shift+Tab. Releasing the trigger key activates the selected window. Esc cancels and restores the previous foreground window.
- The hotkey is configurable. An optional setting also tries to take over Win+Tab.
- The switcher lists visible top-level windows in Z-order, with icon, title, and process name. Tool windows, cloaked UWP ghosts, owned popups, and a small shell-class denylist are filtered out. ApplicationFrameHost windows use the hosted CoreWindow for the real process name, icon, and “end task” target.
- Live preview uses `DwmRegisterThumbnail`.
- Type-to-search filters by window title or process name (substring, plus a short acronym/subsequence match).
- Keyboard and mouse navigation, plus close, end task, minimize, maximize, and restore.
- The window opens on the monitor under the cursor or the monitor of the active window. The process is per-monitor DPI aware (v2).
- System tray menu: open the switcher, Settings, pause/resume the override, start with Windows, exit. A second launch asks the running instance to open Settings.
- Settings are stored as JSON in `%APPDATA%\AltPane\settings.json`. A corrupt file is copied to `settings.json.bak` and defaults are loaded.
- Pausing, exiting, or an unhandled exception removes the hook. If the process is killed, Windows drops the hook as well, so the native switcher works again.

## Keyboard shortcuts

| Action | Gesture |
| --- | --- |
| Open and move to the previous window | Alt+Tab (or your hotkey) |
| Open and move the other way | Alt+Shift+Tab, when Shift is not part of the hotkey |
| Activate the selection | Release the trigger modifier, or Enter |
| Cancel | Esc |
| Move the selection | Tab, Shift+Tab, arrow keys, Home, End, Page Up, Page Down, mouse wheel |
| Filter | Type letters or digits while holding the trigger |
| Search with an IME | Ctrl+F, or click the search box, then type. Enter confirms, Esc cancels |
| Close the window | Delete, the Close button, the context menu, or middle-click a row |
| End the process | Shift+Delete, or the context menu |
| Minimize / maximize / restore | Ctrl+M, Ctrl+Shift+M, Ctrl+R, the buttons, or the context menu |
| Select under the pointer | Hover a row |
| Activate with the mouse | Click a row |

A very fast Alt+Tab, released before the window is drawn and with no search text, activates the previous window without flashing the switcher.

While the trigger is held, typed characters are read with `ToUnicode` and the Alt bits cleared. That path is for Latin letters, digits, and symbols (process names and English titles). Chinese and other IME composition uses Ctrl+F or a click on the search box, which leaves the hold-gesture and focuses a real text box. In that mode, releasing Alt does not activate a window; press Enter.

If the hotkey itself includes Shift, Shift does not reverse direction. Use the arrow keys.

## Run

The published build is a self-contained 64-bit executable. Copy `AltPane.exe` to a Windows 10 or Windows 11 x64 machine and start it. No separate .NET install is required. The first launch unpacks native libraries and is slower than later launches.

Windows SmartScreen may warn because the executable is unsigned. Choose “More info”, then “Run anyway”, if you trust the binary you built.

AltPane starts in the notification area. The first launch shows a balloon. Right-click the icon for Settings, pause, startup, and exit. Left-click opens the switcher without stealing Alt+Tab (Enter confirms, Esc cancels).

A normal (non-admin) process cannot activate or thumbnail some elevated windows. Settings has **Restart as administrator**. The manifest stays `asInvoker`, so everyday use does not require elevation.

Startup registration writes the quoted path of the executable to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` under the value `AltPane`.

Logs: `%APPDATA%\AltPane\altpane.log` (rotated to `altpane.log.old` around 1 MB).

## Build

Requires the .NET 8 SDK. On Windows:

```powershell
dotnet test tests/AltPane.Tests/AltPane.Tests.csproj -c Release
dotnet publish src/AltPane/AltPane.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

`publish\AltPane.exe` is the app. `build.ps1` runs the same two commands.

The WPF project sets `EnableWindowsTargeting`, so the same publish command also works on Linux when the Windows desktop targeting pack can be downloaded. The UI cannot be executed there.

## Layout

- `src/AltPane.Core` — net8.0 library with no UI: window-filter rules, search, the hook gesture state machine, settings load/save, switcher session, localization tables, and native struct sizes.
- `src/AltPane` — WPF app (plus a WinForms tray icon): the low-level hook, window enumeration, DWM thumbnails, switcher and settings windows.
- `tests/AltPane.Tests` — xUnit tests for the core library.

The hook procedure only classifies the key and queues a small operation. Enumeration and UI work run on the dispatcher, so the hook stays inside Windows’ low-level hook timeout. The delegate is rooted for the lifetime of the service. Pause and exit call `UnhookWindowsHookEx`.

## Tests

```powershell
dotnet test tests/AltPane.Tests/AltPane.Tests.csproj -c Release
```

Covered without a Windows desktop session: inclusion rules (visibility, tool windows, cloak, owner, empty title, exclusion list, ApplicationFrameHost), search ranking, session movement, hook interpretation (including search mode and the activation chord), settings JSON round-trip and corrupt-file recovery, kill-process policy, hotkey formatting, localization fallback, layout math, and the 48-byte DWM thumbnail property / 40-byte `INPUT` layouts.

Not covered here: the real hook, DWM thumbnails, tray icon, monitor placement, and activation. Those need a Windows machine. See the checklist below.

## Known limitations

- Win+Tab capture is best-effort. Task View or the Start menu can still appear, because the shell handles that chord outside a normal keystroke.
- Exclusive fullscreen, DRM, and some elevated windows produce a blank thumbnail. Minimized windows show a placeholder instead of a thumbnail.
- Switching to an elevated window, closing it, or thumbnailing it can fail with UIPI unless AltPane is running as administrator.
- “Other virtual desktops” is approximate. The shell cloak bit and `IVirtualDesktopManager` are used when available. The application cloak bit (UWP ghost windows) is always hidden.
- Protected processes (`explorer`, `csrss`, `lsass`, `svchost`, `dwm`, and similar) and an unresolved ApplicationFrameHost window cannot be killed from the switcher.
- The switcher keeps `AllowsTransparency` off so DWM thumbnails and the system backdrop work. Windows 11 rounds the window. On older builds the corners are square and, if Mica is unavailable, a legacy acrylic accent is requested.
- The single-file executable is unsigned.

## Manual test checklist

1. Start AltPane. Alt+Tab shows AltPane, not the Windows switcher. A quick tap switches to the previous window and does not leave Alt stuck or open a menu.
2. Hold Alt and press Tab / Shift+Tab. Arrows, Home, End, Page Up, and Page Down move the selection. Releasing Alt activates it. Esc cancels and returns focus to the previous window.
3. Type a process name while holding Alt and confirm the list filters. Press Ctrl+F (or click the search box), enter Chinese with an IME, and confirm with Enter.
4. Delete sends close. Shift+Delete ends a normal process and refuses Explorer. Minimize, maximize, restore, right-click, and middle-click behave as labeled.
5. Hover and the wheel change the selection without fighting the keyboard. The thumbnail tracks the selection, including a minimized placeholder.
6. Pause, then exit. Native Alt+Tab works in both cases. Kill `AltPane.exe` from Task Manager and confirm native Alt+Tab still works.
7. Change hotkey, theme, language, size, preview, placement, and exclusions. Restart and confirm `%APPDATA%\AltPane\settings.json` was applied. Toggle start with Windows and check the HKCU Run key.
8. Launch a second copy and confirm it focuses Settings on the first instance.
9. Move the cursor to another monitor and another DPI scale. Confirm the switcher opens there and is sharp.
10. Optional: enable Win+Tab capture and note whether Task View still appears. Restart as administrator and switch to an elevated window such as Task Manager.

## 中文说明

AltPane 是一个 Windows 桌面窗口切换器。运行期间它会用低级键盘钩子接管 Alt+Tab（以及 Alt+Shift+Tab），系统自带的切换界面不会出现。松开触发键即切换到所选窗口，Esc 取消。

界面默认跟随系统语言：中文系统使用简体中文，也可在设置里改为 English。设置保存在 `%APPDATA%\AltPane\settings.json`。托盘图标可以打开切换器、设置、暂停接管、开机启动和退出。程序崩溃、暂停或结束后会卸下钩子，原生 Alt+Tab 恢复可用。

按住 Alt 时直接打字适合英文和进程名。中文输入请按 Ctrl+F 或点击搜索框，再用 Enter 确认。普通权限无法切换到以管理员运行的窗口，需要在设置中“以管理员身份重新启动”。

发布包是自包含的 `AltPane.exe`（win-x64）。首次启动会解压本机库，会稍慢。文件未签名，SmartScreen 可能提示，确认来源后选择仍要运行。
