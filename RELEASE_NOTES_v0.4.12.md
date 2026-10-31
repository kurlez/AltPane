# AltPane 0.4.12

## 中文

选中并排的那一行时，两个窗口都会到最前面。0.4.11 只把获得焦点的那个带上来。

- **原因。** 切换器先隐藏，本进程不再是前台。接着对另一个窗口调用 `SetWindowPos(HWND_TOP)`。Windows 不允许一个已经不在前台的进程用这种方式把别的进程的窗口抬到其他窗口之上，这次调用等于没做。随后 `SetForegroundWindow` 只作用于要获得焦点的那个窗口，所以旁边那个仍留在原来的位置，被别的窗口挡住。
- **现在。** 先把当前前台线程和两个目标窗口的线程挂在一起，并允许设置前台。先把另一个窗口插到焦点窗口的正下方，再把焦点交给最近使用的那个。位置和大小都不改，最小化的窗口也不会被还原（避免把它挪走）。如果检查 Z 序发现旁边那个仍被挡住，就让两个窗口短暂进入最顶层，再按「先另一个、后焦点」的顺序退出最顶层，这样它们留在普通窗口的最上面，又不会变成永远置顶。切换器隐藏消息处理完之后会再查一次，仍被挡住就再抬一次。
- **日志。** `%APPDATA%\AltPane\altpane.log` 会记下这一行的两个标题和句柄、抬之前的前台窗口、每次 `SetWindowPos` 失败的错误码，以及抬完之后从顶向下的几个窗口（句柄、进程号、标题）。用来确认配到的是不是你看到的那两个窗口。

检测规则没有改：仍然只配同一屏幕上贴边、并且一起大致铺满工作区的窗口。

## English

Choosing a combined row brings both windows to the front. 0.4.11 only raised the one that took focus.

- **Cause.** The switcher hides first, so this process is no longer foreground. `SetWindowPos(HWND_TOP)` on the other window is then ignored for a window that belongs to another process. `SetForegroundWindow` runs only for the window that should keep focus, and the partner stays buried.
- **Now.** The foreground thread and both target threads are attached, and foreground changes are allowed. The partner is inserted directly under the focused window, then focus is given to the one used more recently. Nothing is moved or resized, and a minimized window is not restored. If the partner is still covered, both windows are placed in the topmost band and then taken out of it — partner first, focus second — so they sit at the top of the normal z-order without staying always-on-top. After the hide message has been processed, the z-order is checked once more and the raise is repeated if the partner is still behind.
- **Log.** `%APPDATA%\AltPane\altpane.log` records both titles and handles, the foreground window before the raise, any `SetWindowPos` error, and the windows that were actually in front afterwards (handle, process id, title). That shows whether the paired windows are the ones on screen.

Detection is unchanged: same monitor, shared edge, together filling the work area.
