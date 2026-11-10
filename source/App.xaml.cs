using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Yuhui;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _tray;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Show();
        _tray = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "余晖 · 日落进度",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) _mainWindow.ToggleVisibility();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => _mainWindow?.ToggleVisibility());
        menu.Items.Add("固定到桌面边缘", null, (_, _) => _mainWindow?.PinToEdge());
        var mouseThrough = new Forms.ToolStripMenuItem("鼠标穿透") { CheckOnClick = true };
        mouseThrough.Click += (_, _) => _mainWindow?.SetMouseThrough(mouseThrough.Checked);
        menu.Opening += (_, _) => mouseThrough.Checked = _mainWindow?.IsMouseThrough == true;
        menu.Items.Add(mouseThrough);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出余晖", null, (_, _) => Shutdown());
        return menu;
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var horizon = new Pen(Color.FromArgb(44, 68, 61), 2);
        using var sun = new SolidBrush(Color.FromArgb(242, 160, 77));
        graphics.DrawLine(horizon, 4, 23, 28, 23);
        graphics.FillEllipse(sun, 10, 9, 12, 12);
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
