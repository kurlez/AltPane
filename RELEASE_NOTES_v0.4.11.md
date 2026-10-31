# AltPane 0.4.11

## 中文

同一屏幕上左右或上下拼在一起的两个窗口，可以当成一个切换目标。窗口切换的其余行为不变。

- **什么时候出现。** 两个窗口都可见、没有最小化、没有被 Cloak，并且在同一台显示器上。它们贴着一条边（左右或上下），合起来大致铺满这块屏幕的工作区。Windows 贴靠的左右分屏、上下分屏，以及手动摆到差不多贴齐的，都算。位置用 `DWMWA_EXTENDED_FRAME_BOUNDS`，不算 DWM 那圈看不见的边框；允许的缝隙按工作区的短边放大，所以 100%、150%、200% 缩放都用同一套规则。互相叠在一起的、很小的、几乎最大化的，不会配成一组。
- **三个及以上。** 不会合成一行。铺满工作区时，只把共享一条边的相邻窗口两两配成一组。三列是左中、中右两行；四宫格是四条边，不包括对角。左半边加右侧上下两块，是三条相邻的组。
- **列表里怎么显示。** 这是额外的一行。两个窗口仍各自留在列表中。这一行同时显示两个图标，标题形如 Chrome ｜ VS Code。序号、Ctrl+数字、屏幕标记（当前屏幕 / 显示器序号）、搜索、拼音和提示行都按现有列表来。排序用两个窗口里最近使用的那个，并且不会抢走第一行的“上一个窗口”。
- **选中之后。** 两个窗口都置前，不移动、不改变大小。焦点留在最近使用的那个上。关闭、结束任务、最小化这些操作不属于这一行。
- **设置。** 「并排窗口作为额外的一组切换」默认开启。设置架构 10。旧文件会补上这项并打开；已经写成关闭的会保留。

## English

Two windows tiled on the same monitor can be switched together. The rest of the switcher is unchanged.

- **When.** Both windows are visible, not minimized, and not cloaked, on the same monitor. They share an edge (left/right or top/bottom) and together fill that work area. Snap layouts and a manual tile that is only roughly aligned both count. Bounds come from `DWMWA_EXTENDED_FRAME_BOUNDS`, so the invisible DWM border is not a gap. The slack scales with the shorter side of the work area, so 100%, 150%, and 200% scaling use the same rule. Overlapping, tiny, and nearly maximized windows are not combined.
- **Three or more.** There is no single row for the whole group. When the tiles fill the work area, each pair that shares an edge is an extra row: left-middle and middle-right for three columns, the four edges of a 2×2 grid and not the diagonals, and three pairs for a half plus two stacked quarters.
- **The row.** It is an extra item. Each window stays in the list. The row shows both icons and a title such as Chrome ｜ VS Code. Number, Ctrl+number, the monitor badge (当前屏幕 / display number), search, pinyin, and the hint line follow the existing list. Order uses the more recently used of the two, and the row does not take the previous-window slot.
- **Activating.** Both windows come forward. Positions and sizes stay. Focus remains on the one used more recently. Close, end task, and minimize do not apply to this row.
- **Settings.** 并排窗口作为额外的一组切换 is on by default. Schema 10 fills that default into older files and keeps an explicit off.
