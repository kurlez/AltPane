# AltPane 0.4.16

## 中文

- 新增本地 Codex bridge：选中文字后按 Ctrl+Alt+J，选择翻译（简体中文、英语或日语）或格式整理，确认后发送到本机 Codex CLI 配置的 AI 服务。
- 托盘增加 Codex AI 入口。无法通过 Windows UI Automation 读取选区时，可手动粘贴文字；结果可复制，不会自动覆盖原应用内容。
- AI 窗口提供“登录 Codex”和“检查登录”。调用官方登录流程并检查状态，AltPane 不读取或保存登录令牌。需先安装官方 Codex CLI；可选择并保存原生 codex.exe 路径，不支持 .cmd/.bat 包装脚本。
- 支持取消、关闭窗口时停止请求、处理两分钟超时、登录十分钟超时和状态检查十五秒超时。处理请求使用只读沙箱，AltPane 不记录原文或回复。
- Linux 上 359 项自动测试通过，Windows 目标编译通过。真实 Windows 选区、浏览器登录及线上 AI 调用仍需实机验证。

## English

- Add a local Codex bridge for selected-text translation and formatting. Press Ctrl+Alt+J or open Codex AI from the tray. Review the input before sending it to the AI service configured by the local Codex CLI.
- Translate into Simplified Chinese, English or Japanese, or clean up formatting. Paste manually when selection capture is unsupported; copy results without modifying the source application.
- Sign in and check login status from the AI window using the official Codex CLI flow. AltPane does not read or store login tokens. Install Codex CLI separately and select its native codex.exe if necessary; .cmd/.bat wrappers are unsupported.
- Cancel requests or close the window to stop their processes. Text processing times out after two minutes, login after ten minutes, and status checks after fifteen seconds. Text requests use a read-only sandbox; AltPane does not log input or replies.
- Validated with 359 automated tests on Linux and a Windows-target build. Windows selection capture, browser authentication and live AI calls still require desktop validation.
