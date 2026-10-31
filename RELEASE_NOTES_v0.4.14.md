# AltPane 0.4.14

## 中文

文件对话框的快速跳转选中文件夹之后，打开 / 另存为对话框会真的进去。

- **为什么以前没动。** 搜索条还占着焦点时，对对话框发送确定往往被忽略。现代的公共项对话框（`#32770` 里的 DirectUI）尤其如此。紧凑列表上的点击还可能先让对话框抢回焦点，列表先收起，跳转根本没发出去。
- **现在。** Enter、单击下拉项、搜索条右侧的「跳转」按钮走同一条路。按钮不拿键盘焦点，列表在跳转发出去之前不会收起。目标优先是当前选中的文件夹，否则是第一条匹配，再否则是输入框里的完整路径。没有目标时按钮变灰，提示先选择或输入完整路径。
- **怎么跳。** 先确认路径存在。然后激活对话框，并按对话框种类换顺序尝试：公共项对话框先走 shell 视图（`IShellBrowser::BrowseObject`），再试文件名框写入路径并回车，最后试地址栏。旧式 `#32770` 先试文件名框。路径以 `\` 结尾，避免确定键把对话框关掉。另存为仍把原来的文件名写回去。
- **日志。** 每一次尝试都在 `%APPDATA%\AltPane\altpane.log` 记一行：窗口类、种类、方法、路径、成败。

## English

Choosing a folder in the quick-jump bar now moves the Open or Save dialog there.

- **What failed.** Sending OK while the search bar still had focus was ignored, especially by the common item dialog. A click could also activate the dialog first, close the list, and never send the jump.
- **Now.** Enter, a click on a row, and the 跳转 button share one path. The button does not take keyboard focus, and the list stays open until the jump has been sent. The target is the highlighted folder, otherwise the first match, otherwise a typed full path. With no target the button is disabled and the tooltip says to pick a folder or type a full path.
- **How.** The path has to exist. The dialog is activated, then tried in an order that depends on its kind: a common item dialog uses the shell view (`IShellBrowser::BrowseObject`) first, then the filename box plus Enter, then the address bar. A legacy `#32770` tries the filename box first. The path ends with `\`, so OK navigates instead of closing the dialog. A Save dialog still gets its previous filename back.
- **Log.** Each attempt is one line in `%APPDATA%\AltPane\altpane.log`: window class, kind, method, path, and success or failure.
