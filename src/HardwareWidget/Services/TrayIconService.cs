using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace HardwareWidget.Services;

/// <summary>
/// Notification-area icon, built the same way as the AI Usage Monitor's: a WinForms NotifyIcon with
/// a ContextMenuStrip, the menu labels refreshed on open, and double-click showing the widget.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly IApplicationController _controller;

    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _widgetVisibilityItem;
    private Drawing.Icon? _icon;

    public TrayIconService(IApplicationController controller) => _controller = controller;

    public void Initialize()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        _widgetVisibilityItem = new Forms.ToolStripMenuItem();
        _widgetVisibilityItem.Click += (_, _) => ToggleWidget();

        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => RefreshMenuLabels();
        menu.Items.Add(_widgetVisibilityItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Refresh", null, (_, _) => _controller.RefreshNow());
        menu.Items.Add("Settings", null, (_, _) => _controller.ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _controller.ExitApplication());

        _icon = LoadIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Hardware Widget",
            Icon = _icon ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };

        _notifyIcon.DoubleClick += (_, _) => _controller.ShowWidget();

        AppLog.Info("Tray icon initialised.");
    }

    public void Dispose()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _notifyIcon = null;
        _widgetVisibilityItem = null;

        _icon?.Dispose();
        _icon = null;
    }

    private void ToggleWidget()
    {
        if (_controller.IsWidgetVisible())
        {
            _controller.HideWidget();
            return;
        }

        _controller.ShowWidget();
    }

    private void RefreshMenuLabels()
    {
        if (_widgetVisibilityItem is not null)
        {
            _widgetVisibilityItem.Text = _controller.IsWidgetVisible() ? "Hide Widget" : "Show Widget";
        }
    }

    /// <summary>
    /// Reads the icon from the embedded resource so it is crisp at tray size, falling back to the
    /// executable's own icon and then to the system default.
    /// </summary>
    private static Drawing.Icon? LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("/HardwareWidget;component/Assets/HardwareWidget.ico", UriKind.Relative));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Tray icon resource could not be loaded: {exception.Message}");
        }

        try
        {
            return Environment.ProcessPath is { } executablePath
                ? Drawing.Icon.ExtractAssociatedIcon(executablePath)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
