# AltPane 0.4.18

## 中文

- 新增托盘菜单“改进 AltPane…”：在应用内用一句话描述想改的地方，改动经由 GitHub 完成，而不是直接改你本地的源码目录。
- 流程：AltPane 在 `%LOCALAPPDATA%\AltPane\improve\repo` 维护一份私有克隆 → 本机 `codex app-server` 常驻会话在克隆里改代码 → AltPane（而非代理）提交并推送分支 `improve/<日期>-<摘要>` → 现有 `build.yml` 在 GitHub 上构建并测试 → AltPane 等待构建完成，用 `gh run download` 下载产物 → 你点“安装并重启”，由隐藏的 PowerShell 脚本在 AltPane 退出后替换 exe（旧文件保留为 `.old`）并重新启动。
- 需要：PATH 中有 `git` 和 GitHub CLI（先执行一次 `gh auth login`，令牌需有推送权限），以及原生 Codex CLI（`codex login`）。构建失败的分支只会报告运行链接，不会安装；任何分支都不会被自动合并。
- 与 0.4.16 的一次性 `codex exec` 不同：只启动一个 `codex app-server`，只开一个会话线程，每个请求一个回合，空闲十分钟后停止；取消会发送 `turn/interrupt`。
- 安全：审批策略 never、沙箱 workspace-write，所有审批请求一律拒绝；提示词禁止代理运行 git、发布或改动克隆之外的内容。若 Codex 报告无法启动命令（Windows 沙箱未就绪），可在 settings.json 里把 `improvementsFullAccess` 设为 true。命令失败、沙箱警告会直接显示在日志里。
- 新增设置：`improvementsRemote`（默认本仓库，可改成你的 fork）、`improvementsBaseBranch`（默认 main）、`codexExecutable`、`improvementsFullAccess`。
- 安装前确认：点“安装并重启”会弹出中文确认框，显示分支、提交、SHA256 和构建链接，并提示这些代码由 AI 编写、尚未合并；默认按钮是“否”。
- SHA256 校验：`build.yml` 现在在构建产物里附带 `AltPane.exe.sha256`，AltPane 下载后先校验，不一致则删除文件、不安装；旧构建没有校验文件时会在日志中注明跳过校验。
- 推送权限：如果推送到 `improvementsRemote` 因权限失败（403、Permission denied 等），会提示先 fork AltPane，再把 `improvementsRemote` 改成你的 fork；修改后私有克隆的 origin 会自动更新。
- 退出更稳：退出 AltPane 时同步结束 `codex app-server` 及其整个子进程树，不再在界面线程上等待异步任务（之前最多会卡 2 秒并可能留下 codex.exe）；处理超时或会话失效时同样结束整个进程树。
- `improvementsFullAccess` 默认仍为 false（沙箱模式）。
- 验证情况：在 Linux 上 404 项测试通过；用真实 git 和本地裸仓库验证了克隆、建分支、提交、推送与二次重置；WPF 代码已用 Windows 引用程序集编译检查；用真实 codex 验证了 `initialize`/`thread/start`/`turn/start` 握手。**尚未验证**：真实的 Codex 完整回合、GitHub Actions 构建与 `gh run download`、窗口显示、自更新脚本、确认框和 SHA256 校验的实际下载路径，需在 Windows 上实机确认。

## English

- Add **Improve AltPane…** to the tray menu. Describe a change in the app; it goes through GitHub instead of editing your own source checkout.
- Flow: AltPane keeps a private clone in `%LOCALAPPDATA%\AltPane\improve\repo` → a long-lived local `codex app-server` session edits it → AltPane (not the agent) commits and pushes a branch `improve/<date>-<slug>` → the existing `build.yml` builds and tests it on GitHub → AltPane waits for the run and downloads the artifact with `gh run download` → you click **Install and restart**, and a hidden PowerShell script swaps the exe after AltPane exits (the old file is kept as `.old`) and starts it.
- Needs `git` and the GitHub CLI on PATH (`gh auth login` once, with push rights) and the native Codex CLI (`codex login`). A branch whose build fails is only reported with its run link; nothing is merged automatically.
- Unlike 0.4.16's one-shot `codex exec`, one `codex app-server` process and one thread are reused, one turn per request, stopped after ten idle minutes; cancel sends `turn/interrupt`.
- Safety: approval policy `never`, `workspace-write` sandbox, every approval declined, and a prompt forbidding git, publishing and edits outside the clone. If Codex reports it cannot launch commands (Windows sandbox not ready), set `improvementsFullAccess` to true in settings.json. Failed commands and sandbox warnings appear in the log.
- New settings: `improvementsRemote` (default this repository; use your fork), `improvementsBaseBranch` (default main), `codexExecutable`, `improvementsFullAccess`.
- Install confirmation: **Install and restart** first shows a confirmation (Chinese UI) with the branch, commit, SHA256 and build link, and a warning that the code is AI-written and unmerged. The default button is No.
- SHA256 check: `build.yml` now ships `AltPane.exe.sha256` in the artifact. AltPane verifies the download before offering it; on a mismatch the file is deleted and nothing is installed. A build without the file is noted in the log as unverified.
- Push permission: when pushing to `improvementsRemote` fails for lack of rights (403, Permission denied, …), the error says to fork AltPane and set `improvementsRemote` to the fork. The private clone's origin follows the setting.
- Exit: quitting AltPane kills the `codex app-server` process tree synchronously instead of waiting on async work on the UI thread (previously up to a 2-second stall that could leave codex.exe running). A turn timeout or broken session also kills the whole tree.
- `improvementsFullAccess` still defaults to false (sandboxed).
- Validation: 404 tests pass on Linux; clone, branch, commit, push and re-reset were run with real git against a local bare repository; the WPF code compiles against the Windows reference assemblies; the real `codex` accepted the `initialize`, `thread/start` and `turn/start` handshake. **Not yet verified**: a full real Codex turn, the GitHub Actions build and `gh run download`, the window's appearance, the self-update script, the confirmation dialog, and checksum verification on a real download. Check these on Windows.
