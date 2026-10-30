# AltPane 0.4.8

## 中文

切换器左侧增加一列常用应用。每台显示器上的切换器都有这一列。排序用的是最近 7 天里真正在前台的时间，不是半衰期衰减后的分数。降低优先级的应用、内置的 VPN 和托盘工具、AltPane 自己，以及自己抢到前台的窗口，都不进这一列。

- **侧栏。** 默认显示 8 个，设置里可以改成 6、7 或 8。每一项是图标和短名称，应用当前有窗口时右下角有一个小圆点。单击会激活该应用最近用过的窗口；没有窗口时，用统计里记下的 exe 或快捷方式启动。
- **键盘。** 切换器打开时，F1 到 F8 对应侧栏从上到下的位置。按住 Alt 时就是 Alt+F1 到 Alt+F8。Win+Tab 打开且 Win 仍按着时，Win+F1 到 Win+F8 是同一组键。这些键写在项目上，也写在底部提示条里。Ctrl+1 到 Ctrl+0、Ctrl+Enter、Ctrl+Shift+Enter、Ctrl+C 不变。切换器没打开时不接管这些键，Alt+F4 仍然关闭窗口。
- **固定和移除。** 右键可以「固定到侧栏」「取消固定」或「从侧栏移除」。固定项按你排的顺序在最前，其余按使用时长补足。设置里可以开关侧栏、改数量，并编辑这两份名单。
- **打开速度。** 名次在后台算好并缓存，图标也提前解码。切换器打开期间侧栏顺序保持不变，和主列表一样。方向键仍然只移动主列表。键盘操作期间，鼠标停在某一项上不会抢高亮。

设置架构升到 8。旧文件默认打开侧栏、数量为 8，固定和隐藏名单为空。已经选过的排序方式不变。

## English

The switcher gains a narrow column of frequent apps, on every monitor. Rank is accumulated real foreground time over a rolling 7 days, not the decayed score used for the main list. Demoted apps, the built-in VPN and tray list, AltPane itself, and focus-stealing windows are left out.

- **Sidebar.** Eight items by default; Settings can choose 6, 7, or 8. Each item is an icon and a short name, with a dot when the app has a window open. A click activates that app's most recent window, or launches the exe or shortcut stored with the stats.
- **Keys.** While the switcher is open, F1–F8 match the sidebar from top to bottom. With Alt still held that is Alt+F1–F8. A Win+Tab session that is still holding Win uses Win+F1–F8. The keys are drawn on the items and in the hint bar. Ctrl+1–0, Ctrl+Enter, Ctrl+Shift+Enter, and Ctrl+C are unchanged. These keys are not taken while the switcher is closed, so Alt+F4 still closes a window.
- **Pin and hide.** The right-click menu can pin, unpin, or remove an item. Pins stay first in the saved order. Settings can hide the column, change the count, and edit both lists.
- **Open latency.** Ranking runs in the background and icons are decoded ahead of time. The sidebar order stays fixed while the switcher is open, as the main list does. Arrow keys still move only the main list. A pointer resting on an item does not highlight it during keyboard navigation.

Settings schema is 8. Older files show the sidebar, with 8 slots and empty pin and hide lists. An explicit sort choice is kept.
