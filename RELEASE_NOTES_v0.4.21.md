# AltPane 0.4.21

## 中文

- **拖动悬停激活窗口。** 从资源管理器或桌面拖着文件时，把文件停在切换器里的某个窗口、并排组合，或已经有窗口的常用应用上约 700 毫秒（可在设置里改成 400–1200 毫秒，也可自定义，范围 300–2000），就会切到那个窗口并收起切换器。随后可以把文件放进该窗口。悬停过程中，对应行会有进度填充作为提示。AltPane 本身不接收放下的文件（报告 `DragDropEffects.None`，也不读取拖动数据）。激活走现有的安全路径：不移动、不改大小（`SWP_NOSIZE | SWP_NOMOVE`），只有最小化窗口才还原，并使用已经在用的 `AttachThreadInput` 和前台变通。
- **拖动时仍可用热键打开切换器。** 低级键盘钩子本来就不看鼠标按键，Alt+Tab（以及设置的热键、Win+Tab）在按住鼠标拖文件时仍然有效。这一版在检测到鼠标键按住时，打开切换器不再抢前台、也不把键盘交给搜索框，以免 OLE 拖动被取消。不需要把文件拖到任务栏或屏幕边缘。
- 设置新增「拖动悬停激活窗口」（默认开启）和「悬停时间」。设置文件升级到第 12 版，新增 `dragHoverActivate` 和 `dragHoverDwellMilliseconds`。旧文件缺少这两项时使用默认；已经保存的关闭或时间保持不变（时间会限制在 300–2000 毫秒）。
- 验证情况：在 Linux 上测试全部通过（新增悬停计时、进度、文件格式判定、拖动时不抢前台、默认值、迁移和保存往返的测试），Windows 目标编译通过。**尚未在 Windows 实机验证**完整拖动流程：拖着文件按 Alt+Tab、悬停后目标窗口被激活、随后把文件放进去；也未验证高完整性（管理员）目标窗口因 UIPI 无法从普通权限的资源管理器接收拖放。

## English

- **Activate a window by hovering during a file drag.** While dragging files from Explorer or the desktop, hover over a window row, a side-by-side pair, or a frequent app that already has a window for about 700 ms (400–1200 ms in Settings, or a custom value clamped to 300–2000). That window is raised, the switcher hides, and the drag continues so you can drop into it. A progress fill is shown on the hovered row during the dwell. AltPane itself never takes the drop (`DragDropEffects.None`; the data is never read). Activation uses the existing safe path: no move or resize (`SWP_NOSIZE | SWP_NOMOVE`), restore only if minimized, and the same `AttachThreadInput` and foreground workarounds.
- **The hotkey still opens the switcher during a drag.** The low-level keyboard hook already ignores mouse buttons, so Alt+Tab (and the configured hotkey, and Win+Tab) works while the button is held. This release, when a mouse button is down, shows the switcher without taking the foreground and without focusing the search box, so the OLE drag is not cancelled. Dragging to the taskbar or a screen edge is not required.
- New settings **拖动悬停激活窗口 / Activate a window by hovering during a file drag** (on by default) and **悬停时间 / Hover time**. Settings schema 12 adds `dragHoverActivate` and `dragHoverDwellMilliseconds`. A file without them gets the defaults; a saved off or dwell is kept (the dwell is clamped to 300–2000 ms).
- Validation: all tests pass on Linux (new tests cover dwell timing, progress, file-format detection, not taking the foreground during a drag, defaults, migration and a save round trip); the Windows targets compile. **Not yet verified on Windows**: the full drag path (Alt+Tab while dragging, hover-to-activate, then dropping into the target), and that an elevated target cannot receive a drop from a non-elevated Explorer because of UIPI.
