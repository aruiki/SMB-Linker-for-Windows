using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SmbAutoMount.Core;

namespace SmbAutoMount.UI;

/// <summary>Notification-area icon with a native popup menu and Windows notifications.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly TaskbarIcon _icon;
    private readonly HashSet<Guid> _lost = new();

    public TrayIcon(App app)
    {
        _app = app;
        _icon = new TaskbarIcon
        {
            ToolTipText = "SmbAutoMount",
            IconSource = new BitmapImage(new Uri(Paths.AppIcon)),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(_app.ShowMainWindow),
            ContextFlyout = new MenuFlyout(),
        };
        Rebuild();
        _icon.ForceCreate(false);
    }

    public void Rebuild()
    {
        var menu = (MenuFlyout)_icon.ContextFlyout;
        menu.Items.Clear();
        menu.Items.Add(Item(Strings.T("tray.open"), _app.ShowMainWindow));
        menu.Items.Add(Item(Strings.T("tray.reconnectAll"), () => _ = _app.Service.RefreshAllAsync()));
        if (_app.Config.Profiles.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var p in _app.Config.Profiles)
        {
            var (state, _) = _app.Service.GetState(p.Id);
            var name = string.IsNullOrWhiteSpace(p.Label) ? p.RemotePath : p.Label;
            var item = Item($"{name} ({p.LocalName}) — {Strings.StateName(state)}", () => OpenDrive(p));
            item.IsEnabled = state == MountState.Connected;
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(Strings.T("tray.exit"), _app.ExitApp));
        UpdateTooltip();
    }

    private static MenuFlyoutItem Item(string text, Action action) =>
        new() { Text = text, Command = new RelayCommand(action) };

    internal static void OpenDrive(MountProfile p)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", p.LocalName + "\\") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Open drive failed", ex); }
    }

    public void OnStateChanged(StateChangedEventArgs e)
    {
        Rebuild();
        if (!_app.Config.Settings.Notifications) return;

        var name = $"{(string.IsNullOrWhiteSpace(e.Profile.Label) ? e.Profile.RemotePath : e.Profile.Label)} ({e.Profile.LocalName})";
        switch (e.State)
        {
            case MountState.Connected when _lost.Remove(e.Profile.Id):
                Notify(Strings.F("notify.reconnected", name), e.Message, NotificationIcon.Info);
                break;
            case MountState.AuthFailed:
                Notify(Strings.F("notify.authFailed", name), e.Message, NotificationIcon.Error);
                break;
            case MountState.Error:
                Notify(Strings.F("notify.error", name), e.Message, NotificationIcon.Warning);
                break;
            case MountState.Unreachable or MountState.WaitingForNetwork when e.Previous == MountState.Connected:
                _lost.Add(e.Profile.Id);
                Notify(Strings.F("notify.lost", name), e.Message, NotificationIcon.Warning);
                break;
        }
    }

    public void Notify(string title, string message, NotificationIcon icon)
    {
        try { _icon.ShowNotification(title, string.IsNullOrWhiteSpace(message) ? " " : message, icon); }
        catch (Exception ex) { Log.Error("Notification failed", ex); }
    }

    private void UpdateTooltip()
    {
        var profiles = _app.Config.Profiles;
        int ok = profiles.Count(p => _app.Service.GetState(p.Id).state == MountState.Connected);
        var text = profiles.Count == 0 ? "SmbAutoMount" : $"SmbAutoMount — {Strings.F("tray.summary", ok, profiles.Count)}";
        _icon.ToolTipText = text.Length > 120 ? text[..120] : text;
    }

    public void Dispose() => _icon.Dispose();
}
