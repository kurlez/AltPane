# AltPane 0.4.13

## 中文

打开 / 另存为对话框上的快速跳转改成 Listary 那种细搜索条，不再铺一块大面板。

- **平时。** 只有一行搜索框，高度大约是一个普通输入框，宽度大约 280 像素（按 DPI 缩放）。默认贴在对话框底边的左下角，设置里可以改到右下角。有空间时挂在对话框外面，不挡住文件名和「打开 / 保存」按钮。屏幕底下放不下时，才缩进对话框底边的角落里；贴右边时会让开按钮那一截。
- **展开。** 默认不显示文件夹列表。Ctrl+G，或点击搜索条，列表才向上弹出（资源管理器里打开的文件夹、最近用过的、索引到的）。输入后按子串、空格 AND 和拼音过滤。上下键选择，Enter 或单击跳转。另存为仍把原来的文件名放回去。
- **收起。** Esc、跳转成功，或焦点回到对话框时，列表收起，搜索条还留在底边上。对话框移动或改变大小时，搜索条跟着走。
- **设置。** 开关、热键、位置都还在。位置从「底部 / 右侧」改成「左下角 / 右下角」，原来保存的选择仍然对应这两项。

## English

The file-dialog jump is a Listary-style slim bar instead of a large panel.

- **At rest.** One search line, about a normal text box tall and about 280 DIP wide. Bottom-left of the dialog by default, or bottom-right from Settings. When the screen has room it hangs just outside the dialog, so it does not cover the filename or the Open/Save buttons. If there is no room below, it tucks into that bottom corner, and the right-hand placement stays clear of the buttons.
- **Open.** The folder list stays hidden until Ctrl+G or a click. It then pops upward (Explorer folders, recent folders, then the index). Typing still uses substring, AND, and pinyin. Up and Down select. Enter or a click jumps. A Save dialog still keeps the filename that was already typed.
- **Close.** Esc, a successful jump, or focus returning to the dialog hides the list. The bar stays on the bottom edge and follows the dialog when it moves or resizes.
- **Settings.** The enable switch, the hotkey, and the position remain. Position is now the two bottom corners. A saved Bottom or Right choice maps to bottom-left or bottom-right.
