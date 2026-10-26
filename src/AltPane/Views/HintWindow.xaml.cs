using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AltPane.Core.Layout;

namespace AltPane;

public partial class HintWindow : Window
{
    public HintWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowChrome.ApplyNoActivate(hwnd, ThemeService.IsDark);
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(NoActivate);
        };
    }

    public nint Handle => new WindowInteropHelper(this).Handle;

    public Action? Clicked { get; set; }

    public void Place(HintPlan plan, DisplayBounds monitor, string title, string move)
    {
        ArrowText.Text = OtherMonitorHints.Arrow(plan.Direction);
        TitleText.Text = title;
        MoveText.Text = move;
        var scaleX = monitor.DpiX <= 0 ? 1d : monitor.DpiX / 96d;
        var scaleY = monitor.DpiY <= 0 ? 1d : monitor.DpiY / 96d;
        Width = plan.Width / scaleX;
        Height = plan.Height / scaleY;
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, new nint(-1), plan.X, plan.Y, plan.Width, plan.Height, 0x0010 | 0x0040);
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var origin = target.TransformFromDevice.Transform(new Point(plan.X, plan.Y));
            Left = origin.X;
            Top = origin.Y;
        }
    }

    public void ApplyChrome(bool dark)
    {
        var hwnd = Handle;
        if (hwnd != 0) WindowChrome.ApplyBackdrop(hwnd, dark);
    }

    void Surface_Click(object sender, MouseButtonEventArgs e)
    {
        Clicked?.Invoke();
        e.Handled = true;
    }

    static nint NoActivate(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != 0x0021) return 0;
        handled = true;
        return 3;
    }
}
