# AltPane 0.4.2

One release. The default order is now focus time, and opening the switcher does less work.

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

## Opening faster, and a smaller working set

- Switcher windows are created once per monitor and reused.
- The window list is kept warm on a background thread, so an open paints the cached list.
- Icons are cached. The log records `open hotkey→visible`, enumeration time, and the row count.
- The working set is trimmed once after startup warmup and logged at startup and every 30 minutes.
