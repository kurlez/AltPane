using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AltPane.Core.Improvements;
using AltPane.Core.Localization;

namespace AltPane;

/// <summary>
/// "What should change?" box. The text goes to a local Codex app-server session that edits a private clone;
/// AltPane pushes a branch, GitHub builds it, and the finished build can be installed from here.
/// Progress streams into the log; past requests and the agent's summaries stay in a list.
/// </summary>
internal sealed class ImprovementWindow : Window
{
    readonly ImprovementService _service;
    readonly Func<ImprovementContext> _context;
    readonly Func<string, bool> _install;
    string? _builtExecutable;
    PipelineResult? _built;
    readonly string _historyPath = ImprovementHistory.DefaultPath();
    readonly List<ImprovementEntry> _history;
    readonly TextBox _input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6, 4, 6, 4) };
    readonly TextBlock _hint = new() { IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(11, 8, 11, 8) };
    readonly Button _send = new() { IsDefault = false };
    readonly Button _cancel = new() { IsEnabled = false };
    readonly Button _installButton = new() { IsEnabled = false };
    readonly Button _close = new();
    readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly ListBox _list = new();
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    CancellationTokenSource? _request;
    bool _closed;

    public ImprovementWindow(ImprovementService service, Func<ImprovementContext> context, Func<string, bool> install)
    {
        _service = service;
        _context = context;
        _install = install;
        _history = ImprovementHistory.Load(_historyPath);
        var loc = Localizer.Current;
        Title = "AltPane · " + loc["improve.title"];
        Width = 680; Height = 760; MinWidth = 480; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SettingsBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _hint.Text = loc["improve.hint"];
        _installButton.Content = loc["improve.install"];
        _send.Content = loc["improve.send"];
        _cancel.Content = loc["improve.cancel"];
        _close.Content = loc["improve.close"];

        var grid = new Grid { Margin = new Thickness(18) };
        foreach (var height in new[]
        {
            GridLength.Auto, new GridLength(2, GridUnitType.Star), GridLength.Auto,
            GridLength.Auto, new GridLength(2, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star)
        })
            grid.RowDefinitions.Add(new RowDefinition { Height = height });

        Add(grid, new TextBlock { Text = loc["improve.note"], TextWrapping = TextWrapping.Wrap }, 0);
        var inputHost = new Grid();
        inputHost.Children.Add(_input);
        inputHost.Children.Add(_hint);
        Add(grid, inputHost, 1);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { _send, _cancel, _installButton, _close })
        {
            button.Padding = new Thickness(14, 4, 14, 4);
            button.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(button);
        }
        Add(grid, buttons, 2);
        Add(grid, _status, 3);
        Add(grid, _log, 4);
        Add(grid, new TextBlock { Text = loc["improve.history"] }, 5);
        Add(grid, _list, 6);
        Content = grid;

        _input.TextChanged += (_, _) => _hint.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                Send(this, new RoutedEventArgs());
            }
        };
        _send.Click += Send;
        _cancel.Click += (_, _) => _request?.Cancel();
        _installButton.Click += (_, _) =>
        {
            if (_builtExecutable is null || _built is null) return;
            var message = string.Format(loc["improve.installConfirm"],
                _built.Branch ?? "?",
                _built.Commit is { Length: >= 7 } commit ? commit[..7] : _built.Commit ?? "?",
                _built.Sha256 is null ? loc["improve.noChecksum"] : _built.Sha256,
                _built.RunUrl ?? "");
            if (MessageBox.Show(this, message, Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (!_install(_builtExecutable)) _status.Text = loc["improve.installFailed"];
        };
        _close.Click += (_, _) => Close();
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _history.Count)
                _log.Text = _history[_history.Count - 1 - _list.SelectedIndex].Summary;
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, ThemeService.IsDark);
        };
        ThemeService.Changed += OnThemeChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            ThemeService.Changed -= OnThemeChanged;
            _request?.Cancel();
        };
        RefreshHistory();
        Loaded += (_, _) => _input.Focus();
    }

    void OnThemeChanged(bool dark) => Dispatcher.BeginInvoke(() =>
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, dark);
    });

    static void Add(Grid grid, FrameworkElement child, int row)
    {
        child.Margin = new Thickness(0, 5, 0, 5);
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    void SetBusy(bool busy)
    {
        _send.IsEnabled = !busy;
        _cancel.IsEnabled = busy;
        _input.IsReadOnly = busy;
        if (busy) _installButton.IsEnabled = false;
    }

    void Append(ImprovementEvent e) => Dispatcher.BeginInvoke(() =>
    {
        if (_closed) return;
        _log.AppendText(e.Text + Environment.NewLine);
        _log.ScrollToEnd();
    });

    void RefreshHistory()
    {
        _list.Items.Clear();
        for (var i = _history.Count - 1; i >= 0; i--)
        {
            var entry = _history[i];
            var mark = entry.Status switch { "done" => "✓", "failed" => "✗", "cancelled" => "–", _ => "…" };
            var firstLine = entry.Request.Split('\n')[0].Trim();
            _list.Items.Add($"{mark} {entry.At.LocalDateTime:MM-dd HH:mm}  {firstLine}");
        }
    }

    async void Send(object sender, RoutedEventArgs e)
    {
        if (_request is not null || _closed) return;
        var loc = Localizer.Current;
        string text;
        try { text = ImprovementPrompt.Normalize(_input.Text); }
        catch (ArgumentException ex) { _status.Text = ex.Message; return; }
        var entry = new ImprovementEntry { Request = text };
        using var request = new CancellationTokenSource();
        _request = request;
        SetBusy(true);
        _log.Clear();
        _status.Text = loc["improve.running"];
        try
        {
            var result = await _service.RunAsync(text, _context(), Append, request.Token);
            entry.Summary = result.Summary;
            _builtExecutable = null;
            _built = null;
            switch (result.Outcome)
            {
                case PipelineOutcome.Built:
                    entry.Status = "done";
                    _builtExecutable = result.ExePath;
                    _built = result;
                    _installButton.IsEnabled = !_closed;
                    _status.Text = loc["improve.done"];
                    _input.Clear();
                    break;
                case PipelineOutcome.NoChanges:
                    entry.Status = "failed";
                    _status.Text = loc["improve.noChanges"] + " " + result.Summary;
                    break;
                case PipelineOutcome.BuildFailed:
                    entry.Status = "failed";
                    _status.Text = loc["improve.buildFailed"] + " " + result.RunUrl;
                    break;
                default:
                    entry.Status = "failed";
                    _status.Text = (loc["improve.failed"] + " " + result.Summary).Trim();
                    break;
            }
            if (result.RunUrl is not null) entry.Summary = (entry.Summary + Environment.NewLine + result.RunUrl).Trim();
        }
        catch (OperationCanceledException)
        {
            entry.Status = "cancelled";
            _status.Text = loc["improve.cancelled"];
        }
        catch (Exception ex)
        {
            entry.Status = "failed";
            entry.Summary = ex.Message;
            _status.Text = ex.Message;
            AppLog.Error(ex);
        }
        finally
        {
            _request = null;
            _history.Add(entry);
            try { ImprovementHistory.Save(_historyPath, _history); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!_closed)
            {
                RefreshHistory();
                SetBusy(false);
            }
        }
    }
}
