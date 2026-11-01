using System.Windows;
using System.Windows.Controls;
using AltPane.Core.AI;
using Microsoft.Win32;

namespace AltPane;

internal sealed class AiTextWindow : Window
{
    readonly TextBox _input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBox _executable = new();
    readonly ComboBox _action = new() { ItemsSource = new[] { "翻译 / Translate", "格式整理 / Format" }, SelectedIndex = 0, MinWidth = 150 };
    readonly ComboBox _language = new() { ItemsSource = new[] { "简体中文", "English", "日本語" }, SelectedIndex = 0, MinWidth = 110 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _run = new() { Content = "开始处理 / Run" };
    readonly Button _cancel = new() { Content = "取消 / Cancel", IsEnabled = false };
    readonly Button _copy = new() { Content = "复制结果 / Copy result", IsEnabled = false };
    readonly Button _login = new() { Content = "登录 Codex / Sign in" };
    readonly Button _checkLogin = new() { Content = "检查登录 / Check login" };
    readonly TextBlock _loginStatus = new() { Text = "尚未检查登录状态", TextWrapping = TextWrapping.Wrap };
    readonly DockPanel _path = new();
    readonly Action<string> _saveExecutable;
    CancellationTokenSource? _request;
    CancellationTokenSource? _authRequest;
    bool _closed;

    public AiTextWindow(string text, string executable, Action<string> saveExecutable)
    {
        _saveExecutable = saveExecutable;
        Title = "AltPane · Codex AI";
        Width = 740; Height = 720; MinWidth = 520; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SettingsBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        _input.Text = text;
        _executable.Text = executable;
        var grid = new Grid { Margin = new Thickness(18) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = height });
        Add(grid, new TextBlock { Text = "选中文字后按 Ctrl+Alt+J；也可复制后从托盘打开并粘贴。\n通过本机 Codex 登录调用 AI。点击开始后，输入内容将发送到 Codex 配置的 AI 服务。", TextWrapping = TextWrapping.Wrap }, 0);
        var path = _path;
        var browse = new Button { Content = "选择 codex.exe…" };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Codex executable|codex.exe|Executable|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) _executable.Text = dialog.FileName;
        };
        DockPanel.SetDock(browse, Dock.Right); path.Children.Add(browse); path.Children.Add(_executable);
        Add(grid, path, 1);
        var auth = new WrapPanel();
        auth.Children.Add(_login); auth.Children.Add(_checkLogin);
        _loginStatus.Margin = new Thickness(10, 5, 0, 0);
        auth.Children.Add(_loginStatus); Add(grid, auth, 2);
        var options = new StackPanel { Orientation = Orientation.Horizontal };
        options.Children.Add(_action); options.Children.Add(_language);
        var paste = new Button { Content = "粘贴 / Paste" };
        paste.Click += (_, _) => { try { if (Clipboard.ContainsText()) _input.Text = Clipboard.GetText(); } catch { _status.Text = "剪贴板正忙，请重试。"; } };
        options.Children.Add(paste); Add(grid, options, 3);
        Add(grid, _input, 4);
        Add(grid, new TextBlock { Text = "结果 / Result" }, 5); Add(grid, _output, 6);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_run); buttons.Children.Add(_cancel); buttons.Children.Add(_copy); Add(grid, buttons, 7);
        Add(grid, _status, 8); Content = grid;
        _run.Click += Run;
        _cancel.Click += (_, _) => { _request?.Cancel(); _authRequest?.Cancel(); };
        _login.Click += async (_, _) => await AuthenticateAsync(signIn: true);
        _checkLogin.Click += async (_, _) => await AuthenticateAsync(signIn: false);
        _executable.TextChanged += (_, _) => _loginStatus.Text = "路径已更改，请重新检查登录状态。";
        _copy.Click += (_, _) => { try { Clipboard.SetText(_output.Text); _status.Text = "已复制 / Copied"; } catch { _status.Text = "剪贴板正忙，请重试。"; } };
        Closed += (_, _) => { _closed = true; _request?.Cancel(); _authRequest?.Cancel(); };
        _action.SelectionChanged += (_, _) => _language.IsEnabled = _action.SelectedIndex == 0;
        _status.Text = string.IsNullOrWhiteSpace(text) ? "未读取到选区，请粘贴文字。首次使用请先安装 Codex CLI，再点击登录 Codex。" : "已读取选中文字；确认内容后点击开始。";
        Loaded += async (_, _) => await AuthenticateAsync(signIn: false);
    }

    static void Add(Grid grid, FrameworkElement child, int row)
    {
        child.Margin = new Thickness(0, 5, 0, 5); Grid.SetRow(child, row); grid.Children.Add(child);
    }

    async void Run(object sender, RoutedEventArgs e)
    {
        if (_request is not null || _authRequest is not null || _closed) return;
        using var request = new CancellationTokenSource(); _request = request;
        _run.IsEnabled = false; _cancel.IsEnabled = true; _copy.IsEnabled = false; _output.Clear();
        _input.IsReadOnly = true; _action.IsEnabled = false; _language.IsEnabled = false;
        _login.IsEnabled = false; _checkLogin.IsEnabled = false; _path.IsEnabled = false;
        _status.Text = "Codex 正在处理… / Processing…";
        try
        {
            var executable = _executable.Text.Trim();
            _saveExecutable(executable);
            _output.Text = await new CodexBridge().TransformAsync(executable, _input.Text, (TextAction)_action.SelectedIndex, (string)_language.SelectedItem, request.Token);
            _copy.IsEnabled = true; _status.Text = "处理完成 / Done";
        }
        catch (OperationCanceledException) { _status.Text = "已取消 / Cancelled"; }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _request = null; _run.IsEnabled = true; _cancel.IsEnabled = false;
            _input.IsReadOnly = false; _action.IsEnabled = true; _language.IsEnabled = _action.SelectedIndex == 0;
            _login.IsEnabled = true; _checkLogin.IsEnabled = true; _path.IsEnabled = true;
        }
    }

    async Task AuthenticateAsync(bool signIn)
    {
        if (_authRequest is not null || _request is not null || _closed) return;
        using var request = new CancellationTokenSource(); _authRequest = request;
        _login.IsEnabled = false; _checkLogin.IsEnabled = false; _path.IsEnabled = false;
        _run.IsEnabled = false; _cancel.IsEnabled = true;
        _loginStatus.Text = signIn ? "请在 Codex 打开的浏览器或登录窗口完成登录…" : "正在检查登录状态…";
        try
        {
            var executable = _executable.Text.Trim();
            CodexLogin.CreateStartInfo(executable, interactive: signIn);
            _saveExecutable(executable);
            var login = new CodexLogin();
            if (signIn) await login.LoginAsync(executable, request.Token);
            var authenticated = await login.CheckAsync(executable, request.Token);
            _loginStatus.Text = authenticated ? "已登录 Codex / Signed in" : "未登录或无法确认状态，请点击登录或重试检查。";
        }
        catch (OperationCanceledException) { _loginStatus.Text = "已取消；如已在浏览器授权，可重新检查登录状态。"; }
        catch (Exception ex) { _loginStatus.Text = ex.Message; }
        finally
        {
            _authRequest = null;
            _login.IsEnabled = true; _checkLogin.IsEnabled = true; _path.IsEnabled = true;
            _run.IsEnabled = true; _cancel.IsEnabled = false;
        }
    }
}
