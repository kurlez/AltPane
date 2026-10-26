using System.Reflection;
using WinForms = System.Windows.Forms;

namespace AltPane;

internal sealed class TrayController : IDisposable
{
    readonly AppHost _host;
    readonly WinForms.NotifyIcon _icon;
    readonly WinForms.ContextMenuStrip _menu = new();

    public TrayController(AppHost host)
    {
        _host = host;
        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = false,
            Text = "AltPane"
        };
        _icon.ContextMenuStrip = _menu;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                _host.OpenSwitcher();
        };
        Rebuild();
    }

    public void Show() => _icon.Visible = true;

    public void Rebuild()
    {
        var loc = Core.Localization.Localizer.Current;
        var settings = _host.Settings;
        _icon.Text = settings.OverridePaused ? loc["tray.tipPaused"] : loc["tray.tip"];
        _menu.Items.Clear();
        Add(loc["tray.open"], () => _host.OpenSwitcher());
        Add(loc["tray.settings"], () => _host.ShowSettings());
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        Add(settings.OverridePaused ? loc["tray.resume"] : loc["tray.pause"], () => _host.TogglePause());
        var startup = new WinForms.ToolStripMenuItem(loc["tray.startup"])
        {
            Checked = settings.StartWithWindows,
            CheckOnClick = false
        };
        startup.Click += (_, _) => _host.ToggleStartup();
        _menu.Items.Add(startup);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        Add(loc["tray.log"], () => _host.OpenLogFolder());
        Add(loc["tray.exit"], () => _host.Exit());
    }

    public void Balloon(string body, string? title = null)
    {
        _icon.BalloonTipTitle = title ?? "AltPane";
        _icon.BalloonTipText = body;
        _icon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    void Add(string text, Action action)
    {
        var item = new WinForms.ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        _menu.Items.Add(item);
    }

    static System.Drawing.Icon LoadIcon()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AltPane.app.ico");
        if (stream is null) return System.Drawing.SystemIcons.Application;
        return new System.Drawing.Icon(stream);
    }
}
