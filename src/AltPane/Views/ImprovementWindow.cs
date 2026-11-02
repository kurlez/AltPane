using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AltPane.Core.Improvements;
using AltPane.Core.Localization;
using WinForms = System.Windows.Forms;

namespace AltPane;

/// <summary>
/// "What should change?" box. The text goes to a local Codex app-server session that edits the AltPane
/// source checkout. Progress streams into the log; past requests and the agent's summaries stay in a list.
/// </summary>
internal sealed class ImprovementWindow : Window
{
    readonly ImprovementService _service;
    readonly Func<ImprovementContext> _context;
    readonly Action<string> _saveRepository;
    readonly string _historyPath = ImprovementHistory.DefaultPath();
    readonly List<ImprovementEntry> _history;
    readonly TextBox _input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6, 4, 6, 4) };
    readonly TextBlock _hint = new() { IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(11, 8, 11, 8) };
    readonly TextBox _repo = new();
    readonly Button _browse = new();
    readonly Button _send = new() { IsDefault = false };
    readonly Button _cancel = new() { IsEnabled = false };
    readonly Button _close = new();
    readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly ListBox _list = new();
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    CancellationTokenSource? _request;
    bool _closed;

    public ImprovementWindow(ImprovementService service, Func<ImprovementContext> context, string repository, Action<string> saveRepository)
    {
        _service = service;
        _context = context;
        _saveRepository = saveRepository;
        _history = ImprovementHistory.Load(_historyPath);
        var loc = Localizer.Current;
        Title = "AltPane · " + loc["improve.title"];
        Width = 680; Height = 760; MinWidth = 480; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SettingsBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _hint.Text = loc["improve.hint"];
        _repo.Text = repository;
        _browse.Content = loc["improve.browse"];
        _send.Content = loc["improve.send"];
        _cancel.Content = loc["improve.cancel"];
        _close.Content = loc["improve.close"];

        var grid = new Grid { Margin = new Thickness(18) };
        foreach (var height in new[]
        {
            GridLength.Auto, new GridLength(2, GridUnitType.Star), GridLength.Auto, GridLength.Auto,
            GridLength.Auto, new GridLength(2, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star)
        })
            grid.RowDefinitions.Add(new RowDefinition { Height = height });

        Add(grid, new TextBlock { Text = loc["improve.note"], TextWrapping = TextWrapping.Wrap }, 0);
        var inputHost = new Grid();
        inputHost.Children.Add(_input);
        inputHost.Children.Add(_hint);
        Add(grid, inputHost, 1);

        var repoRow = new DockPanel();
        DockPanel.SetDock(_browse, Dock.Right);
        repoRow.Children.Add(_browse);
        repoRow.Children.Add(_repo);
        var repoBlock = new StackPanel();
        repoBlock.Children.Add(new TextBlock { Text = loc["improve.repo"] });
        repoBlock.Children.Add(repoRow);
        Add(grid, repoBlock, 2);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { _send, _cancel, _close })
        {
            button.Padding = new Thickness(14, 4, 14, 4);
            button.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(button);
        }
        Add(grid, buttons, 3);
        Add(grid, _status, 4);
        Add(grid, _log, 5);
        Add(grid, new TextBlock { Text = loc["improve.history"] }, 6);
        Add(grid, _list, 7);
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
        _browse.Click += (_, _) =>
        {
            using var dialog = new WinForms.FolderBrowserDialog { SelectedPath = _repo.Text, ShowNewFolderButton = false };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK) _repo.Text = dialog.SelectedPath;
        };
        _send.Click += Send;
        _cancel.Click += (_, _) => _request?.Cancel();
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
        _repo.IsEnabled = !busy;
        _browse.IsEnabled = !busy;
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
        var repository = _repo.Text.Trim();
        if (repository.Length > 0 && !CodexLocator.IsRepository(repository))
        {
            _status.Text = loc["improve.noRepo"];
            return;
        }
        if (repository.Length > 0) _saveRepository(repository);

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
            switch (result.Outcome)
            {
                case ImprovementOutcome.Completed:
                    entry.Status = "done";
                    _status.Text = loc["improve.done"];
                    _input.Clear();
                    break;
                case ImprovementOutcome.Interrupted:
                    entry.Status = "cancelled";
                    _status.Text = loc["improve.cancelled"];
                    break;
                default:
                    entry.Status = "failed";
                    _status.Text = (loc["improve.failed"] + " " + result.Summary).Trim();
                    break;
            }
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
