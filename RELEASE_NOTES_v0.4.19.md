# AltPane 0.4.19

## 中文

- 移除 Codex：删除 0.4.18 的 `codex app-server` 会话、私有克隆、`improve/*` 分支推送、`gh run download` 下载分支构建以及对应的自替换流程。旧设置项 `improvementsRemote`、`improvementsBaseBranch`、`codexExecutable`、`improvementsFullAccess` 读取时会被忽略，下次保存时自动移除。
- 新增托盘菜单「提需求…」：填写标题和描述后点「提交」，AltPane 用 GitHub CLI 在 kurlez/AltPane 创建 issue（标签 `altpane-request`，指派给当前 gh 登录账号），正文末尾附 AltPane 版本和系统版本。参数逐个传给 gh、不经过 shell，正文通过临时文件（`--body-file`）传递，提交后删除。标签不存在时先尝试创建（失败忽略）。成功后显示 issue 链接和「打开」。未安装 gh、未登录、没有权限时给出中文提示。issue 被指派后由维护流程实现并自动发布新版本。
- 新增自动更新：启动约 30 秒后及每 6 小时检查一次 GitHub 最新发布，有新版本时询问「发现新版本 X，是否安装？」；托盘新增「检查更新」可随时手动检查。确认后下载发布 zip 和 `.sha256`，校验不通过或缺少校验文件则不安装；通过后由隐藏的 PowerShell 脚本在 AltPane 退出后替换 exe（旧文件保留为 `AltPane.exe.old`）并重启。若安装目录需要管理员权限，保留旧版本、把原因写入 `%TEMP%\altpane-update-error.txt`，并在下次启动时通过托盘通知提示。
- 验证情况：在 Linux 上 372 项测试通过（Codex 相关测试已删除，新增参数构建、错误分类、SHA256 校验、下载解压和旧设置兼容等测试），Windows 目标编译通过。**尚未在 Windows 实机验证**：提需求窗口外观、真实 `gh issue create`、更新提示框、下载后替换并重启的脚本，以及需要管理员权限时的回退提示。

## English

- Codex removed: 0.4.18's `codex app-server` session, private clone, `improve/*` branch pushes, `gh run download` of branch builds and the matching self-replace flow are gone. The old keys `improvementsRemote`, `improvementsBaseBranch`, `codexExecutable` and `improvementsFullAccess` are ignored when read and dropped on the next save.
- New tray item **提需求… / Submit a request…**: a title, a description and **提交**. AltPane files a GitHub issue in kurlez/AltPane with the GitHub CLI (label `altpane-request`, assigned to the signed-in gh account) with a footer giving the AltPane and OS versions. Arguments go to gh one by one with no shell; the body travels in a temporary `--body-file` that is deleted afterwards. The label is created first if needed (failure ignored). The issue link is shown with **打开**. Missing gh, no sign-in and missing permission each get a clear Chinese message. Assigned issues are implemented and released by the maintainer's automation.
- New update check: about 30 seconds after startup and every six hours AltPane reads the latest GitHub release and, when it is newer, asks 「发现新版本 X，是否安装？」. **检查更新 / Check for updates** in the tray checks on demand. On yes it downloads the release zip and its `.sha256`, refuses to install when the checksum is missing or wrong, and a hidden PowerShell script swaps the exe after AltPane exits (keeping `AltPane.exe.old`) and restarts it. If the folder needs administrator rights the old version stays, the reason goes to `%TEMP%\altpane-update-error.txt`, and the next start shows it in a tray notification.
- Validation: 372 tests pass on Linux (Codex tests removed; new tests cover argument building, error classification, SHA256 verification, download and extraction, and legacy settings); the Windows targets compile. **Not yet verified on Windows**: the request window's appearance, a real `gh issue create`, the update prompt, the swap-and-restart script, and the admin-rights fallback notice.
