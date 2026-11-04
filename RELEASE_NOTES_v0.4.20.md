# AltPane 0.4.20

## 中文

- 切换窗口改为暗色风格（issue #12）：切换器窗口默认使用深色配色——深色背景、浅色文字、蓝色选中高亮、浅蓝色边框，左侧常用应用侧栏、搜索行、窗口列表、操作提示和提示条一并变暗；窗口边框和 DWM 深色模式也随之切换。无论「主题」选浅色、深色还是跟随系统，切换器都保持深色；设置、关于窗口和文件对话框跳转条仍跟随「主题」。
- 设置新增「切换窗口风格」：「深色（默认）」或「跟随主题」。选「跟随主题」后切换器与以前一样跟随「主题」。
- 设置文件升级到第 11 版，新增 `switcherStyle`。旧文件缺少该项时使用深色切换器；已保存的 `FollowTheme` 保持不变；无效值按深色处理。
- 验证情况：在 Linux 上 381 项测试通过（新增风格判定、默认值、迁移、保存往返和无效值的测试），Windows 目标编译通过。**尚未在 Windows 实机验证**切换器的实际深色外观、浅色系统主题下的窗口边框，以及右键菜单（仍为系统样式）。

## English

- Dark switcher (issue #12): the switcher window now uses the dark palette by default — a dark background, light text, a blue selection highlight and a light-blue edge — across the frequent-apps sidebar, search line, window list, action hint and status line. The DWM frame (dark mode, border and caption colors) follows it. The switcher stays dark whether **主题 / Theme** is light, dark or follow system; Settings, About and the file-dialog bar still follow the theme.
- New setting **切换窗口风格 / Switcher style**: **深色（默认） / Dark (default)** or **跟随主题 / Follow the theme**. Follow the theme restores the previous behavior.
- Settings schema 11 adds `switcherStyle`. A file without it gets the dark switcher, a saved `FollowTheme` is kept, and an invalid value falls back to dark.
- Validation: 381 tests pass on Linux (new tests cover the style decision, defaults, migration, save round trip and invalid values); the Windows targets compile. **Not yet verified on Windows**: the actual dark look, the frame colors under a light system theme, and the right-click menus (still system-styled).
