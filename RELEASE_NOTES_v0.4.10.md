# AltPane 0.4.10

## 中文

标准的打开 / 另存为对话框在前台时，AltPane 会在对话框边缘放一个小面板，用来快速跳到文件夹。窗口切换本身不变。

- **哪些对话框。** 识别公共项对话框（窗口类 `#32770`，里面有 `DUIViewWndClassName` / `DirectUIHWND` 和地址栏）以及旧的 `GetOpenFileName` 对话框。普通的 `#32770` 消息框不会弹出面板。用 `SetWinEventHook` 的 `EVENT_SYSTEM_FOREGROUND` 和 `EVENT_OBJECT_SHOW`（进程外回调）。没有对话框时不枚举资源管理器，也不查 Everything。
- **面板。** 不激活（`WS_EX_NOACTIVATE`），叠在所属对话框之上，而不是全局置顶。默认贴在对话框底部，也可以改到右侧。对话框移动或改变大小时跟着走，按所在显示器的 DPI 定位。对话框关闭或不再是前台窗口时隐藏。
- **列表。** 先列出当前在文件资源管理器里打开的文件夹（`IShellWindows`，面板出现时才枚举），然后是 AltPane 自己记住的最近文件夹，以及 Windows「最近使用的项目」里指向目录的快捷方式。输入后按子串、空格 AND 和拼音首字母过滤，优先问 Everything 的 `folder:`，否则用内置索引里的目录。每一行有真实文件夹图标和完整路径。
- **操作。** 对话框在前台时，默认 Ctrl+G（可在设置里改）聚焦面板的搜索框。上下键选择，Enter 或单击跳转，Esc 把焦点还给对话框。提示行写着这些键。跳转时把路径写入文件名编辑框（控件 id `0x47C` / 1148，或界面自动化），相当于按 Enter；另存为会把原来输入的文件名放回去。若文件夹没有变化，再改地址栏。
- **设置。** 「文件对话框快速跳转」默认开启。还可以改热键和面板位置（底部 / 右侧）。设置架构 9，旧文件会补上这三项默认值；已经写过的选择会保留。

版本号 0.4.10 的补丁是两位数。关于页和设置页按 Major.Minor.Patch 显示，发布流程把标签解析成 `0.4.10` 和程序集版本 `0.4.10.0`，比较时按数字而不是字符串，所以 0.4.10 排在 0.4.9 后面。

## English

A standard Open or Save As dialog gets a small folder panel on its edge. The window switcher is unchanged.

- **Which dialogs.** Common item dialogs (`#32770` hosting `DUIViewWndClassName` / `DirectUIHWND` and an address bar) and legacy `GetOpenFileName` dialogs. Other `#32770` message boxes are ignored. Detection uses `SetWinEventHook` for `EVENT_SYSTEM_FOREGROUND` and `EVENT_OBJECT_SHOW`, out of context. Explorer windows and Everything are not touched until a dialog is showing.
- **Panel.** `WS_EX_NOACTIVATE`, owned by the dialog so it stays above that window rather than above everything. Bottom edge by default, or the right edge. It follows move and resize and uses that monitor's DPI. It hides when the dialog closes or is no longer in front.
- **List.** Folders open in File Explorer (`IShellWindows`, enumerated when the panel appears), then AltPane's own recent folders and directory targets from the Windows Recent folder. Typing uses substring, AND, and pinyin initials, asking Everything for `folder:` and otherwise the built-in index. Each row has the real folder icon and the full path.
- **Keys.** Ctrl+G by default, while the dialog is in front, focuses the search box. Up and Down select. Enter or a click navigates. Esc returns focus to the dialog. The hint line shows those keys. Navigation writes the path into the filename edit (control id `0x47C` / 1148, or UI Automation) and submits it, then puts a Save dialog's previous filename back. If the folder did not change, the address bar is tried.
- **Settings.** 文件对话框快速跳转 is on by default, with the hotkey and the bottom/right position. Schema 9 fills those defaults into older files and keeps an explicit choice.

The 0.4.10 patch is two digits. About and Settings show Major.Minor.Patch. The release workflow accepts `v0.4.10` and sets the assembly version to `0.4.10.0`. Ordering is numeric, so 0.4.10 comes after 0.4.9.
