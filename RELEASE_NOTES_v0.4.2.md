# AltPane 0.4.2

One release. Opening the switcher does less work, and the default order is focus time.

## Opening faster, and a smaller working set

These are in this tag:

- Switcher windows are created at startup, one hidden window per monitor, and reused. A later Alt+Tab calls Show and SetWindowPos on those windows. A window object is replaced only when the monitor set changes.
- The list is shown first. Icons are decoded afterward, and CPU and memory sampling starts only after the switcher is visible. Process names and icons for the list are maintained in the background, so a warm open does not walk every process on the UI thread.
- A background STA thread keeps the window list with `SetWinEventHook` (foreground, create, hide, and name change). The hook only enqueues a handle. Opening clones that list. A full `EnumWindows` runs when the queue overflows or the list is older than 15 seconds.
- Icons are 32×32, cached per executable, and decoded on a thread-pool thread. The UI thread only freezes the bitmap and paints it.
- The Microsoft Store scan releases each Shell item and the folder COM objects when it finishes, instead of holding them for the life of the process.
- After startup, one off-screen pass at (-32000, -32000) shows, lays out, and decodes icons without taking focus, so the first real Alt+Tab is not the first time that code runs. TieredPGO and ReadyToRun are on. Invariant globalization stays off.
- The working set is trimmed once after that warmup (`SetProcessWorkingSetSize` -1, -1) and logged as `working set startup {n} MB`. The same figure is logged every 30 minutes. The target is under 60 MB.
- Each open writes one INFO line: `open hotkey→visible {ms} ms, enumeration {ms} ms, rows {n}`. A warm open reports enumeration 0 because it cloned the cache. Esc behavior is unchanged.

## Focus time (按使用时长)

The default sort is no longer stable grouped by name. Windows you actually use come first, in the order you used them. Windows you barely use, and tools that take the foreground by themselves, stay last.

- A foreground change counts toward the previous-window row and toward focus time only when a key or click happened within about half a second before it, or AltPane activated the window, and the window stays in front for at least a second. 飞连 (CorpLink) and similar popups no longer become the window Alt+Tab returns to.
- Focus time decays with a 7-day half-life and is stored in `%APPDATA%\AltPane\usage.json`.
- The main list is apps at or above the threshold, plus pinned apps, ordered by most recent use. The default threshold is 10 minutes. Settings offers 1, 5, 10, and 30. An app leaves the main list only after it falls below 80 percent of the threshold.
- Below a “不常用 / Rarely used” separator: apps under the threshold, apps you demote, and a built-in list of VPN and tray processes (CorpLink, Feilian, Clash, v2rayN, OpenVPN, GlobalProtect, Cisco AnyConnect, and a few similar names). They follow recent use too, and they do not take the first row while a main-list window exists.
- A pin keeps an app in the main list. It does not jump ahead of a window you used more recently. Ctrl+Shift+P pins, Ctrl+Shift+B demotes, Ctrl+Shift+U restores the default. The row menu and Settings (`pinnedApps`, `demotedApps`) do the same.
- For the first day, and when an app has no usage yet, it stays in the main list. With no history, the list follows Z-order.
- The order is fixed when the switcher opens. Search still finds every window. Index badges stay based on the application name. Recent, app name, CPU, and memory are unchanged.

Settings schema 7. A file that was on initial letter (the old default) or stable grouped moves to focus time. An explicit Recent, app name, CPU, or memory choice stays. `focusThresholdMinutes` defaults to 10.
