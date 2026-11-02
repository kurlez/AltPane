# AltPane 0.4.18

## 中文

- 新增托盘菜单“改进 AltPane…”：在应用内用一句话描述想改的地方，由本机 `codex app-server` 常驻会话直接修改 AltPane 源码、运行测试并汇报结果（灵感来自把“反馈框”直接接到实现代理上的做法）。
- 与 0.4.16 的一次性 `codex exec` 不同：AltPane 启动一个 `codex app-server`，初始化后只开一个会话线程，每个请求作为一个回合，后续请求复用进程与上下文；空闲十分钟后自动停止进程。取消会发送 `turn/interrupt`。
- 默认安全边界：审批策略为 never，沙箱为 workspace-write（只能写源码目录）；Codex 提出的任何审批都会被拒绝；提示词要求不提交、不推送、不发布、不改动目录外内容。可用 `improvementsFullAccess` 关闭沙箱（默认关闭）。
- 进度（命令、改动文件、代理总结）实时显示；历史请求与总结保存在 `%APPDATA%\AltPane\improvements.json`（最近 100 条）。改动不会自动生效：请用 `git diff` 检查，再重新编译运行。
- 新增设置项：`improvementsRepository`、`codexExecutable`、`improvementsFullAccess`。
- 本版本在没有 .NET SDK 的 Linux 环境中编写，**尚未编译或运行测试**，也未用真实 Codex 联调；发布前请在 Windows 上执行 `build.ps1` 并实机验证。

## English

- Add **Improve AltPane…** to the tray menu: describe a change in the app and a long-lived local `codex app-server` session edits the AltPane source, runs the tests and reports back.
- Unlike 0.4.16's one-shot `codex exec`, AltPane starts one `codex app-server`, initializes it once, keeps a single thread, and runs each request as a turn, so later requests reuse the process and context. The process stops after ten idle minutes. Cancel sends `turn/interrupt`.
- Safety defaults: approval policy `never`, `workspace-write` sandbox, every approval request declined, and a prompt that forbids committing, pushing, publishing or touching files outside the checkout. `improvementsFullAccess` (off by default) removes the sandbox.
- Progress streams into the window; past requests and summaries are kept in `%APPDATA%\AltPane\improvements.json` (last 100). Changes never apply on their own: review with `git diff`, then rebuild.
- New settings: `improvementsRepository`, `codexExecutable`, `improvementsFullAccess`.
- This version was written in a Linux environment without the .NET SDK. It has **not been compiled, unit-tested or tried against a real Codex**. Run `build.ps1` and check on Windows before releasing.
