using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Interop;

namespace AltPane;

/// <summary>Reads the selection without overwriting the clipboard or injecting keys.</summary>
internal sealed class AiSelectionHotkey : IDisposable
{
    readonly HwndSource _source;
    readonly Action<string> _open;
    bool _reading;
    const int Id = 0x4149;

    public AiSelectionHotkey(Action<string> open)
    {
        _open = open;
        _source = new HwndSource(new HwndSourceParameters("AltPane AI hotkey") { ParentWindow = new nint(-3) });
        _source.AddHook(Hook);
        Registered = RegisterHotKey(_source.Handle, Id, 0x4000 | 0x0001 | 0x0002, 0x4A);
    }

    public bool Registered { get; }

    nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == Id)
        {
            handled = true;
            if (!_reading) ReadSelection();
        }
        return 0;
    }

    async void ReadSelection()
    {
        _reading = true;
        try
        {
            var text = "";
            try
            {
                text = await Task.Run(() =>
                {
                    var element = AutomationElement.FocusedElement;
                    if (element is null || element.Current.IsPassword) return "";
                    if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return "";
                    return string.Join(Environment.NewLine, ((TextPattern)pattern).GetSelection().Select(range => range.GetText(100_001)));
                }).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch { /* Unsupported UIA providers: offer manual paste. */ }
            if (!_disposed) _open(text);
        }
        finally { _reading = false; }
    }

    bool _disposed;
    public void Dispose()
    {
        _disposed = true;
        if (Registered) UnregisterHotKey(_source.Handle, Id);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(nint hwnd, int id);
}
