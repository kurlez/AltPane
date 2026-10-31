namespace AltPane.Core.Input;

public enum HintRow
{
    Window,
    App,
    File,
    Calculator,
    AppsMode,
    TilePair
}

/// <summary>
/// Compact Chinese hint for the row that is selected. The setting hides every line.
/// </summary>
public static class RowHints
{
    public const string File = "Enter 打开 · Ctrl+Enter 打开所在目录 · Ctrl+C 复制路径 · Ctrl+Shift+C 复制目录路径 · → 更多";
    public const string Window = "Enter 切换 · Ctrl+Enter 打开所在目录 · Ctrl+C 复制路径 · Ctrl+Shift+C 复制目录路径 · Delete 关闭 · → 更多";
    public const string WindowNoPath = "Enter 切换 · Delete 关闭 · → 更多";
    public const string App = "Enter 启动 · Ctrl+Enter 打开所在目录 · Ctrl+C 复制路径 · Ctrl+Shift+C 复制目录路径 · → 更多";
    public const string AppSwitch = "Enter 切换 · Ctrl+Enter 打开所在目录 · Ctrl+C 复制路径 · Ctrl+Shift+C 复制目录路径 · → 更多";
    public const string AppNoPath = "Enter 启动 · → 更多";
    public const string Calculator = "Enter/Ctrl+C 复制结果";
    public const string AppsMode = "Enter 启动 · Ctrl+Shift+Enter 以管理员运行 · Esc 退出";
    public const string TilePair = "Enter 同时切换两个窗口 · 焦点留给最近使用的那个";

    public static string Text(HintRow? kind, bool show, bool hasPath = true, bool switching = false)
    {
        if (!show || kind is null) return "";
        return kind switch
        {
            HintRow.File => File,
            HintRow.Calculator => Calculator,
            HintRow.AppsMode => AppsMode,
            HintRow.TilePair => TilePair,
            HintRow.Window => hasPath ? Window : WindowNoPath,
            HintRow.App when !hasPath => AppNoPath,
            HintRow.App when switching => AppSwitch,
            HintRow.App => App,
            _ => ""
        };
    }
}
