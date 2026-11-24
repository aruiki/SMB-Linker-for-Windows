using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SMBLinker.Core;
using SMBLinker.Native;
using Windows.Graphics;

namespace SMBLinker.UI;

public sealed partial class DriveItem : INotifyPropertyChanged
{
    public DriveItem(MountProfile p) => Profile = p;
    public MountProfile Profile { get; private set; }

    public string Title => string.IsNullOrWhiteSpace(Profile.Label)
        ? $"{Profile.Share} ({Profile.LocalName})"
        : $"{Profile.Label} ({Profile.LocalName})";

    public string Subtitle => string.IsNullOrWhiteSpace(Profile.UserName)
        ? Profile.RemotePath
        : $"{Profile.RemotePath}  ·  {Profile.UserName}";

    private MountState _state = MountState.Unknown;
    private string _message = "";

    public MountState State
    {
        get => _state;
        set { _state = value; Raise(); Raise(nameof(StateName)); Raise(nameof(StateBrush)); Raise(nameof(IsConnected)); Raise(nameof(ShowMessage)); }
    }

    public string Message { get => _message; set { _message = value; Raise(); Raise(nameof(ShowMessage)); } }

    /// <summary>Only show the detail line when it adds information (problems, waiting).</summary>
    public Visibility ShowMessage => State is MountState.Connected or MountState.Unknown || string.IsNullOrWhiteSpace(Message)
        ? Visibility.Collapsed : Visibility.Visible;

    public string StateName => Strings.StateName(State);
    public bool IsConnected => State == MountState.Connected;

    public Brush StateBrush => (Brush)Application.Current.Resources[State switch
    {
        MountState.Connected => "SystemFillColorSuccessBrush",
        MountState.WaitingForNetwork or MountState.Connecting or MountState.Unreachable => "SystemFillColorCautionBrush",
        MountState.AuthFailed or MountState.Error => "SystemFillColorCriticalBrush",
        _ => "SystemFillColorNeutralBrush",
    }];

    public void Replace(MountProfile p)
    {
        Profile = p;
        Raise(nameof(Title));
        Raise(nameof(Subtitle));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<DriveItem> _items = new();
    private bool _loading;
    private bool _dialogOpen;

    private static App AppInstance => App.Instance;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        AppWindow.Title = "SMB Linker for Windows";
        AppWindow.SetIcon(Paths.AppIcon);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(980 * scale), (int)(760 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.PreferredMinimumWidth = (int)(560 * scale);
            op.PreferredMinimumHeight = (int)(420 * scale);
        }
        CenterOnScreen();

        // Closing the window keeps the app running in the notification area.
        AppWindow.Closing += (_, e) =>
        {
            if (AppInstance.IsExiting) return;
            e.Cancel = true;
            AppWindow.Hide();
            AppInstance.OnMainWindowHidden();
        };

        DriveList.ItemsSource = _items;
        foreach (var p in AppInstance.Config.Profiles) _items.Add(ToItem(p));

        _loading = true;
        StartupToggle.IsOn = StartupManager.IsEnabled();
        NotifyToggle.IsOn = AppInstance.Config.Settings.Notifications;
        AutoFixToggle.IsOn = AppInstance.Config.Settings.AutoFixConflicts;
        LanguageBox.ItemsSource = new[] { Strings.T("lang.auto"), "日本語", "English" };
        LanguageBox.SelectedIndex = AppInstance.Config.Settings.Language switch { "ja" => 1, "en" => 2, _ => 0 };
        _loading = false;

        AboutCard.Description = Strings.F("main.version", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "");
        UpdateEmpty();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2));
    }

    internal void ShowAndActivate()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } op) op.Restore();
        Activate();
    }

    private DriveItem ToItem(MountProfile p)
    {
        var (state, msg) = AppInstance.Service.GetState(p.Id);
        return new DriveItem(p) { State = state, Message = msg };
    }

    private void UpdateEmpty() => EmptyCard.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    internal void OnStateChanged(StateChangedEventArgs e)
    {
        var item = _items.FirstOrDefault(i => i.Profile.Id == e.Profile.Id);
        if (item == null) return;
        item.State = e.State;
        item.Message = e.Message;
    }

    private static DriveItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as DriveItem;

    // ---------- drive actions ----------

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var p = await ShowProfileDialogAsync(null);
        if (p == null) return;
        AppInstance.Config.Profiles.Add(p);
        AppInstance.SaveConfig();
        _items.Add(ToItem(p));
        UpdateEmpty();
        AppInstance.RefreshTray();
        await AppInstance.Service.ConnectNowAsync(p);
    }

    private async void Card_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) await EditAsync(item);
    }

    private async Task EditAsync(DriveItem item)
    {
        var old = item.Profile;
        var dlg = new ProfileDialog(old) { XamlRoot = Root.XamlRoot };
        var p = await ShowDialogAsync(dlg) == ContentDialogResult.Primary ? dlg.Result : null;
        if (p == null) return;

        bool moved = !string.Equals(old.LocalName, p.LocalName, StringComparison.OrdinalIgnoreCase)
                     || !DriveMapper.SameRemote(old.RemotePath, p.RemotePath)
                     || old.UserName != p.UserName || dlg.PasswordChanged;
        if (moved) DriveMapper.Unmount(old);
        ExplorerCustomizer.Remove(old);

        var idx = AppInstance.Config.Profiles.FindIndex(x => x.Id == old.Id);
        AppInstance.Config.Profiles[idx] = p;
        AppInstance.SaveConfig();
        item.Replace(p);
        AppInstance.Service.Forget(p); // clears "sign-in failed" so the new password gets tried
        if (!moved && item.State == MountState.Connected) ExplorerCustomizer.Apply(p);
        AppInstance.RefreshTray();
        await AppInstance.Service.ConnectNowAsync(p);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) TrayIcon.OpenDrive(item.Profile);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(MenuItem(item.IsConnected ? "main.disconnect" : "main.connect", item.IsConnected ? "" : "",
            () =>
            {
                if (item.IsConnected) AppInstance.Service.Disconnect(item.Profile);
                else _ = AppInstance.Service.ConnectNowAsync(item.Profile);
            }));
        menu.Items.Add(MenuItem("main.edit", "", () => _ = EditAsync(item)));
        menu.Items.Add(MenuItem("main.diagnose", "", () => _ = DiagnoseAsync(item)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("main.remove", "", () => _ = RemoveAsync(item)));
        menu.ShowAt((FrameworkElement)sender);
    }

    private static MenuFlyoutItem MenuItem(string key, string glyph, Action action)
    {
        var mi = new MenuFlyoutItem { Text = Strings.T(key), Icon = new FontIcon { Glyph = glyph } };
        mi.Click += (_, _) => action();
        return mi;
    }

    private async Task DiagnoseAsync(DriveItem item)
    {
        await ShowDialogAsync(new DiagnosticsDialog(item.Profile) { XamlRoot = Root.XamlRoot });
    }

    private async Task RemoveAsync(DriveItem item)
    {
        var p = item.Profile;
        var confirm = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.F("main.removeTitle", p.LocalName),
            Content = Strings.F("main.removeConfirm", p.RemotePath),
            PrimaryButtonText = Strings.T("main.remove"),
            CloseButtonText = Strings.T("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(confirm) != ContentDialogResult.Primary) return;

        DriveMapper.Unmount(p);
        ExplorerCustomizer.Remove(p);
        CredMan.DeleteAll(p.Id);
        AppInstance.Service.Forget(p);
        AppInstance.Config.Profiles.RemoveAll(x => x.Id == p.Id);
        AppInstance.SaveConfig();
        _items.Remove(item);
        UpdateEmpty();
        AppInstance.RefreshTray();
    }

    private async Task<MountProfile?> ShowProfileDialogAsync(MountProfile? existing)
    {
        var dlg = new ProfileDialog(existing) { XamlRoot = Root.XamlRoot };
        return await ShowDialogAsync(dlg) == ContentDialogResult.Primary ? dlg.Result : null;
    }

    /// <summary>Only one ContentDialog may be open at a time.</summary>
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialogOpen) return ContentDialogResult.None;
        _dialogOpen = true;
        try
        {
            dialog.Style ??= (Style)Application.Current.Resources["DefaultContentDialogStyle"];
            dialog.RequestedTheme = Root.ActualTheme;
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void ReconnectAll_Click(object sender, RoutedEventArgs e) => await AppInstance.Service.RefreshAllAsync();

    // ---------- settings ----------

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = AppInstance.Config.Settings;
        s.Notifications = NotifyToggle.IsOn;
        s.AutoFixConflicts = AutoFixToggle.IsOn;
        try { StartupManager.SetEnabled(StartupToggle.IsOn); }
        catch (Exception ex) { Log.Error("Startup toggle failed", ex); }
        AppInstance.SaveConfig();
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        AppInstance.Config.Settings.Language = LanguageBox.SelectedIndex switch { 1 => "ja", 2 => "en", _ => "auto" };
        AppInstance.SaveConfig();
    }

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", Paths.LogDir) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Open logs failed", ex); }
    }
}
