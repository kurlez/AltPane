# AltPane 0.4.5

## 中文

0.4.4 的文件搜索在一些 Everything 1.4 安装上会得到 0 条结果：服务和托盘进程都在运行，`ipc=1`，但没有 `EVERYTHING_IPC_WNDCLASS` 窗口（包括 message-only 窗口），本机也没有 `Everything64.dll` 或 `es.exe`。0.4.5 在这种情况下仍能查到文件。窗口顺序、Esc、悬停和计算器的行为不变。

- **IPC 回退。** 先问 `EVERYTHING_IPC_WNDCLASS`。这个类不存在，或者发出 `WM_COPYDATA` 后在超时内没有回应，就用同一条查询再问托盘窗口 `EVERYTHING_TASKBAR_NOTIFICATION`，然后是主窗口 `EVERYTHING`（主窗口只在 Everything 打开时存在）。空的结果列表算作已经连上；一直没有数据包才算失败。
- **HTTP 回退。** IPC 都没有回应时，查询本机 Everything HTTP 服务器：`/?search=…&json=1&path_column=1&count=N`。设置里的端口默认 80，连不上时再试 8080（可以关掉）。超时很短，而且只在后台线程上等待，不会卡住界面。JSON 映射成和 IPC 相同的文件行。
- **状态。** 搜索和设置里分开显示「Everything 未运行」、「Everything 在运行但 IPC 连接失败」（并提示到 Everything 选项 → HTTP 服务器 里启用），以及「已通过 HTTP 连接」。日志用 INFO 记下实际用的传输（`ipc`、`ipc-taskbar`、`ipc-window`、`http:80` 等）。
- **内置排除。** 文件搜索始终加上这些 `!path:`，并在结果里再滤一次：`.gemini`、`.claude`、`.cursor`、`.codex`、`.copilot`、`.continue`、`.aider*`、`.ollama`、`.lmstudio`、`.windsurf`、`.codeium`、`.qwen`、`.kimi`、`.trae`、`.vscode\extensions`，以及 `node_modules`。设置里原有的「要跳过的文件夹」仍然追加在后面。

在 Everything 里打开 HTTP 服务器：工具 → 选项 → HTTP 服务器 → 启用 HTTP 服务器。端口 80 往往需要管理员权限；若 80 起不来，把端口改成 8080，AltPane 会在 80 失败后自动再试 8080，也可以在 AltPane 设置里把 HTTP 端口直接写成 8080。

## English

File search in 0.4.4 returned no rows on some Everything 1.4 installs: the service and the tray process were running, `ipc=1` was set, and there was still no `EVERYTHING_IPC_WNDCLASS` window, including among message-only windows. `Everything64.dll` and `es.exe` were absent too. 0.4.5 still searches in that case. Window order, Esc, hover, and the calculator are unchanged.

- **IPC fallback.** Ask `EVERYTHING_IPC_WNDCLASS` first. When that class is missing, or the `WM_COPYDATA` query gets no reply before the timeout, send the same query to the tray window `EVERYTHING_TASKBAR_NOTIFICATION`, then to the main `EVERYTHING` window (present only while Everything is open). An empty result list counts as a connection. No packet does not.
- **HTTP fallback.** If IPC never answers, query Everything's HTTP server on localhost: `/?search=…&json=1&path_column=1&count=N`. The port in Settings defaults to 80, and 8080 is tried next (that second try can be turned off). The wait is short and stays off the UI thread. JSON becomes the same file rows IPC would have produced.
- **Status.** Search and Settings distinguish “Everything is not running”, “Everything is running, but IPC failed” (with a pointer to Everything Options → HTTP Server), and “Connected over HTTP”. The transport (`ipc`, `ipc-taskbar`, `ipc-window`, `http:80`, and so on) is written at INFO.
- **Built-in exclusions.** File search always adds these `!path:` terms and filters the rows again: `.gemini`, `.claude`, `.cursor`, `.codex`, `.copilot`, `.continue`, `.aider*`, `.ollama`, `.lmstudio`, `.windsurf`, `.codeium`, `.qwen`, `.kimi`, `.trae`, `.vscode\extensions`, and `node_modules`. Folders already listed in Settings are still added on top.

Turn on the HTTP server in Everything with Tools → Options → HTTP Server → Enable HTTP server. Port 80 often needs an administrator. If it will not listen there, set the port to 8080. AltPane tries 8080 after 80 fails, and Settings can point the HTTP port at 8080 directly.
