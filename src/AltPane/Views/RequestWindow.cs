using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using AltPane.Core.Localization;
using AltPane.Core.Requests;

namespace AltPane;

/// <summary>
/// 「提需求…」: a title, a description and a submit button. The request is filed as a GitHub issue in
/// kurlez/AltPane with the gh CLI, labeled altpane-request and assigned to the signed-in account.
/// </summary>
internal sealed class RequestWindow : Window
{
    readonly FeatureRequestSubmitter _submitter = new(new ProcessCommandRunner());
    readonly Func<RequestContext> _context;
    readonly TextBox _title = new() { Padding = new Thickness(6, 4, 6, 4), MaxLength = FeatureRequest.MaxTitleLength };
    readonly TextBox _description = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(6, 4, 6, 4), MaxLength = FeatureRequest.MaxDescriptionLength
    };
    readonly Button _submit = new() { Padding = new Thickness(16, 4, 16, 4) };
    readonly Button _close = new() { Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    CancellationTokenSource? _request;
    bool _closed;

    public RequestWindow(Func<RequestContext> context)
    {
        _context = context;
        var loc = Localizer.Current;
        Title = "AltPane · " + loc["request.title"];
        Width = 560; Height = 480; MinWidth = 420; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SettingsBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        _submit.Content = loc["request.submit"];
        _close.Content = loc["request.close"];

        var grid = new Grid { Margin = new Thickness(18) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = height });
        Add(grid, new TextBlock { Text = loc["request.note"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }, 0);
        Add(grid, new TextBlock { Text = loc["request.titleLabel"], Margin = new Thickness(0, 0, 0, 4) }, 1);
        Add(grid, _title, 2);
        Add(grid, new TextBlock { Text = loc["request.descriptionLabel"], Margin = new Thickness(0, 10, 0, 4) }, 3);
        Add(grid, _description, 4);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(_submit);
        buttons.Children.Add(_close);
        Add(grid, buttons, 5);
        Add(grid, _status, 6);
        Content = grid;

        _submit.Click += Submit;
        _close.Click += (_, _) => Close();
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
        Loaded += (_, _) => _title.Focus();
    }

    void OnThemeChanged(bool dark) => Dispatcher.BeginInvoke(() =>
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, dark);
    });

    static void Add(Grid grid, UIElement child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    async void Submit(object sender, RoutedEventArgs e)
    {
        if (_request is not null || _closed) return;
        var loc = Localizer.Current;
        try { FeatureRequest.Normalize(_title.Text, _description.Text); }
        catch (ArgumentException ex) { _status.Text = ex.Message; return; }

        using var request = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _request = request;
        _submit.IsEnabled = false;
        _title.IsReadOnly = true;
        _description.IsReadOnly = true;
        _status.Text = loc["request.sending"];
        try
        {
            var url = await _submitter.SubmitAsync(_title.Text, _description.Text, _context(), request.Token);
            AppLog.Info("Request filed: " + url);
            if (_closed) return;
            ShowSuccess(url);
            _title.Clear();
            _description.Clear();
        }
        catch (RequestException ex)
        {
            AppLog.Info($"Request failed ({ex.Failure}): {ex.Message}");
            if (!_closed) _status.Text = ex.Message;
        }
        catch (OperationCanceledException)
        {
            if (!_closed) _status.Text = loc["request.timeout"];
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            if (!_closed) _status.Text = FeatureRequest.Message(RequestFailure.Other, ex.Message);
        }
        finally
        {
            _request = null;
            if (!_closed)
            {
                _submit.IsEnabled = true;
                _title.IsReadOnly = false;
                _description.IsReadOnly = false;
            }
        }
    }

    void ShowSuccess(string url)
    {
        var loc = Localizer.Current;
        _status.Inlines.Clear();
        _status.Inlines.Add(new Run(loc["request.done"] + " " + url + "  "));
        var link = new Hyperlink(new Run(loc["request.open"])) { NavigateUri = new Uri(url) };
        link.RequestNavigate += (_, args) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Error(ex); }
            args.Handled = true;
        };
        _status.Inlines.Add(link);
    }
}
