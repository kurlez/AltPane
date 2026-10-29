# AltPane 0.4.4

## 中文

在 0.4.3 的行为之上增加四件事。窗口顺序在打开期间仍然固定，Esc 先清搜索再退出，悬停门闩、滚轮一齿一行且不循环、每台显示器的切换器和焦点时长排序都保持不变。界面仍是中文，没有新增语言设置。

- **Ctrl+数字直达。** Ctrl+1 到 Ctrl+9 激活前九个可见行，Ctrl+0 激活第十行，然后关闭切换器。数字显示在原来的应用名/拼音索引旁边。搜索框有焦点时可用，按住 Alt 时也可用，不会打断 Alt+Tab。
- **文件搜索。** 搜索框同时向正在运行的 Everything 1.4 查询文档。默认类型：doc、docx、xls、xlsx、ppt、pptx、pdf、txt、md、csv、rtf、odt、ods、odp、wps、et、dps、ofd、epub，可在设置里改。`f ` 或 `file:` 搜索所有类型。结果单独成组，排在窗口和应用下面，最多 20 条，由 Everything 按运行次数排序。没有安装或没有运行时，只显示一行「未检测到 Everything，文件搜索不可用」，其余切换器照常工作。查询防抖，过期的结果会被丢掉，延迟写入 INFO 日志。
- **文件操作。** Enter 打开文件。Ctrl+Enter 用 `explorer /select` 打开所在目录并选中文件。Ctrl+C 复制完整路径；搜索框里有选中文字时，Ctrl+C 仍复制那段文字。Ctrl+Shift+C 复制所在目录的完整路径。菜单里的「复制目录名」只复制文件夹名。复制后短暂显示「已复制」。窗口行使用进程可执行文件路径，应用行使用启动路径。右方向键或 Ctrl+O 或右键打开操作菜单，每一项右侧标着快捷键。
- **计算器。** 搜索文字能算出来时，最上面出现一行「= 结果」。支持 + − * / % ^、括号、小数、一元负号、sqrt、abs、round、sin、cos、tan、log、ln、pi、e，以及全角 ＋－×÷（）。可以以 `=` 开头。Enter 或 Ctrl+C 复制结果的纯文本（没有千分位），切换器保持打开。除以零或无法解析时不显示这一行，也不抛异常。
- **快捷键提示。** 选中文件、窗口、应用或计算结果时，底部用中文列出该行的操作。设置里可以关掉，默认开着。

以管理员身份运行改为 Ctrl+Shift+Enter，避免和「打开所在目录」抢 Ctrl+Enter。右方向键打开操作菜单；向下键仍然移动选择。搜索框光标还没到行尾时，右方向键仍移动光标。

## English

Four additions on top of 0.4.3. Order stays fixed while the switcher is open, Esc still clears and then closes, and the hover gate, one-row wheel steps, per-monitor switcher, and focus-time order are unchanged. The UI stays Chinese. There is no new language setting.

- **Ctrl+number.** Ctrl+1 through Ctrl+9 activate the first nine visible rows. Ctrl+0 activates the tenth. The switcher then closes. The digit sits beside the existing name or pinyin index. It works while the search box is focused and while Alt is held, and it does not take over Alt+Tab.
- **File search.** Typing also asks a running Everything 1.4 instance for documents. The default types are doc, docx, xls, xlsx, ppt, pptx, pdf, txt, md, csv, rtf, odt, ods, odp, wps, et, dps, ofd, and epub, and Settings can change that list. `f ` or `file:` searches every type. Results are their own group under windows and apps, capped at 20, sorted by Everything's run count. If Everything is missing or not running, one row says file search is unavailable and the rest of the switcher keeps working. Queries are debounced, stale replies are dropped, and the latency is logged at INFO.
- **File actions.** Enter opens the file. Ctrl+Enter runs `explorer /select` on it. Ctrl+C copies the full path, unless the search box has a text selection. Ctrl+Shift+C copies the directory path. The menu action “复制目录名” copies only the folder name. A short “已复制” toast follows a copy. Window rows use the process executable. App rows use the launch path. Right arrow, Ctrl+O, or a right-click opens the action menu, and each item shows its shortcut on the right.
- **Calculator.** A query that parses as arithmetic shows a top row `= result`. The parser accepts + − * / % ^, parentheses, decimals, unary minus, sqrt, abs, round, sin, cos, tan, log, ln, pi, and e, plus the full-width operators. A leading `=` is allowed. Enter or Ctrl+C copies the plain result, without grouping separators, and leaves the switcher open. Division by zero or an invalid expression shows no row and throws nothing.
- **Shortcut hint.** The bottom bar names the actions for a file, window, app, or calculator row. Settings can hide it. It is on by default.

Run as administrator moved to Ctrl+Shift+Enter so Ctrl+Enter can open the parent folder. Right arrow opens the action menu; Down still moves the selection. While the search caret is not at the end of the query, Right still moves the caret.
