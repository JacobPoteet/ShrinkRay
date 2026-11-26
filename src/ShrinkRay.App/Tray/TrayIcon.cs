using System.Windows;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ShrinkRay.App.Tray;

/// <summary>
/// The notification-area icon. Thin glue: it paints <see cref="TrayModel"/> onto a NotifyIcon,
/// opens the window on double-click or Open, and sends Exit through the window's own Closing
/// event, so a close during an encode asks first. Dispose removes the icon from the tray.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Window _window;
    private readonly TrayModel _model;
    private readonly WinForms.NotifyIcon _notify;
    private readonly Dictionary<TrayState, Drawing.Icon> _icons = [];
    private readonly WinForms.ToolStripMenuItem _stopAfter;

    public TrayIcon(Window window, TrayModel model)
    {
        _window = window;
        _model = model;

        // The size the shell draws the tray at on this display and scale, picked from the .ico.
        var size = WinForms.SystemInformation.SmallIconSize;
        foreach (var (state, name) in new[]
        {
            (TrayState.Idle, "tray-idle"),
            (TrayState.Encoding, "tray-encoding"),
            (TrayState.Paused, "tray-paused"),
            (TrayState.Failed, "tray-failed"),
        })
        {
            var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Icon/{name}.ico"))!;
            using var stream = info.Stream;
            _icons[state] = new Drawing.Icon(stream, size);
        }

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Open());
        _stopAfter = new WinForms.ToolStripMenuItem(_model.StopAfterText, null, (_, _) => _model.ToggleStopAfter())
        {
            Enabled = _model.CanStopAfter,
        };
        menu.Items.Add(_stopAfter);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());

        _notify = new WinForms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _icons[_model.State],
            Text = _model.Tooltip,
            Visible = true,
        };
        _notify.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                Open();
            }
        };

        _notify.BalloonTipClicked += (_, _) => Open();

        _model.Changed += OnModelChanged;
        _model.Notified += OnNotified;
    }

    /// <summary>Shows the deck, restoring it if it is minimized, and brings it forward.</summary>
    public void Open()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    /// <summary>
    /// Closes the window, which asks before ending a run. The window comes forward first, so
    /// the confirm card is in sight.
    /// </summary>
    public void Exit()
    {
        Open();
        _window.Close();
    }

    public void Dispose()
    {
        _model.Changed -= OnModelChanged;
        _model.Notified -= OnNotified;
        _notify.Visible = false;
        _notify.ContextMenuStrip?.Dispose();
        _notify.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }

    // Windows shows this as a notification. Clicking it opens the deck.
    private void OnNotified(object? sender, ViewModels.RunReport report)
    {
        // A notification body holds 255 characters.
        var text = report.Text.Length <= 255 ? report.Text : report.Text[..252] + "...";
        _notify.ShowBalloonTip(10_000, report.Title, text, report.NeedsAttention ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        _notify.Icon = _icons[_model.State];
        _notify.Text = _model.Tooltip;
        _stopAfter.Text = _model.StopAfterText;
        _stopAfter.Enabled = _model.CanStopAfter;
    }
}
