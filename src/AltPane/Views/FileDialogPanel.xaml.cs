using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AltPane.Core.Localization;

namespace AltPane;

public sealed class DialogFolderRow
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public ImageSource? Icon { get; set; }
}

public partial class FileDialogPanel : Window
{
    public FileDialogPanel()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ApplyChrome(hwnd);
            ApplyExtended(hwnd, noActivate: true);
        };
        ThemeService.Changed += dark => Dispatcher.BeginInvoke(() =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, dark);
        });
    }

    public event Action<string>? QueryChanged;
    public event Action<string>? NavigateRequested;
    public event Action? DismissRequested;

    public string Query => SearchBox.Text ?? "";

    public void SetHint(string hotkeyText)
    {
        HintText.Text = Localizer.Current.Format("dialog.hint", hotkeyText);
        Placeholder.Text = Localizer.Current["dialog.placeholder"];
        EmptyText.Text = Localizer.Current["dialog.empty"];
    }

    public void SetRows(IReadOnlyList<DialogFolderRow> rows, string? keepPath)
    {
        var selected = keepPath;
        if (string.IsNullOrEmpty(selected) && Rows.SelectedItem is DialogFolderRow current)
            selected = current.Path;
        Rows.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var index = 0;
        if (!string.IsNullOrEmpty(selected))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].Path, selected, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
        }
        if (rows.Count > 0)
        {
            Rows.SelectedIndex = index;
            Rows.ScrollIntoView(Rows.SelectedItem);
        }
    }

    public void RefreshRows() => Rows.Items.Refresh();

    public string? SelectedPath() => Rows.SelectedItem is DialogFolderRow row ? row.Path : null;

    public void MoveTo(int x, int y, int width, int height, double scaleX, double scaleY)
    {
        scaleX = scaleX <= 0 ? 1 : scaleX;
        scaleY = scaleY <= 0 ? 1 : scaleY;
        Width = Math.Max(160, width / scaleX);
        Height = Math.Max(120, height / scaleY);
        ShowActivated = false;
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, 0, x, y, Math.Max(1, width), Math.Max(1, height), NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    public void AttachTo(nint owner)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        ApplyExtended(hwnd, noActivate: true);
        new WindowInteropHelper(this).Owner = owner;
    }

    public void FocusSearch()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        ApplyExtended(hwnd, noActivate: false);
        ShowActivated = true;
        if (!IsVisible) Show();
        WindowActions.TryActivate(hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    public void ReturnFocus(nint dialog)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) ApplyExtended(hwnd, noActivate: true);
        ShowActivated = false;
        if (dialog != 0) WindowActions.TryActivate(dialog);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (SearchBox.IsKeyboardFocusWithin) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) ApplyExtended(hwnd, noActivate: true);
    }

    public void ClearQuery()
    {
        if (SearchBox.Text.Length == 0) return;
        SearchBox.Text = "";
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DismissRequested?.Invoke();
            return;
        }
        if (e.Key == Key.Down)
        {
            Move(1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Up)
        {
            Move(-1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (SelectedPath() is { Length: > 0 } path)
                NavigateRequested?.Invoke(path);
        }
    }

    void Move(int delta)
    {
        if (Rows.Items.Count == 0) return;
        var next = Rows.SelectedIndex < 0 ? 0 : Rows.SelectedIndex + delta;
        if (next < 0) next = 0;
        if (next >= Rows.Items.Count) next = Rows.Items.Count - 1;
        Rows.SelectedIndex = next;
        Rows.ScrollIntoView(Rows.SelectedItem);
    }

    void Rows_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(Rows, source) is not ListBoxItem item) return;
        if (item.Content is DialogFolderRow row && row.Path.Length > 0)
            NavigateRequested?.Invoke(row.Path);
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        QueryChanged?.Invoke(SearchBox.Text ?? "");
    }

    void ApplyChrome(nint hwnd)
    {
        if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, ThemeService.IsDark);
    }

    static void ApplyExtended(nint hwnd, bool noActivate)
    {
        if (hwnd == 0) return;
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        var tool = (nint)FileDialogNative.WsExToolWindow;
        var quiet = (nint)FileDialogNative.WsExNoActivate;
        ex |= tool;
        if (noActivate) ex |= quiet;
        else ex &= ~quiet;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, ex);
    }
}
