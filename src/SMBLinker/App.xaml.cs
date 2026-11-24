using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SMBLinker.Core;
using SMBLinker.UI;

namespace SMBLinker;

public partial class App : Application
{
    private const string MutexName = @"Local\SMBLinker.SingleInstance";
    private const string ShowEventName = @"Local\SMBLinker.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private bool _exiting;

    internal static App Instance => (App)Current;
    internal AppConfig Config { get; private set; } = new();
    internal ReconnectService Service { get; private set; } = null!;
    internal DispatcherQueue Dispatcher { get; private set; } = null!;
    internal bool IsExiting => _exiting;
    internal bool IsDemo { get; }
    internal string? DemoOpen { get; }

    public App()
    {
        var args = Environment.GetCommandLineArgs();
        IsDemo = DemoData.IsDemo(args);
        if (IsDemo)
        {
            DemoOpen = DemoData.Option(args, "demo-open");
            var theme = DemoData.Option(args, "demo-theme");
            if (theme == "dark") RequestedTheme = ApplicationTheme.Dark;
            else if (theme == "light") RequestedTheme = ApplicationTheme.Light;
        }
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cmd = Environment.GetCommandLineArgs();
        bool background = cmd.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

        if (cmd.Any(a => a.Equals("--cleanup", StringComparison.OrdinalIgnoreCase)))
        {
            // Called by the uninstaller: undo everything we changed outside our own folders.
            Cleanup();
            Exit();
            return;
        }

        if (IsDemo)
        {
            // Screenshot mode: sample data only, no network, no tray, nothing saved.
            Dispatcher = DispatcherQueue.GetForCurrentThread();
            Config = DemoData.Config();
            Strings.SetLanguage(DemoData.Option(cmd, "demo-lang") ?? "auto");
            Service = new ReconnectService(() => Config);
            for (int i = 0; i < Config.Profiles.Count; i++)
            {
                var (state, message) = DemoData.StateFor(i, Config.Profiles[i]);
                Service.SetStateForDemo(Config.Profiles[i], state, message);
            }
            ShowMainWindow();
            return;
        }

        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask that instance to show its window, then quit.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Exit();
            return;
        }

        Dispatcher = DispatcherQueue.GetForCurrentThread();
        Migration.Run();
        Config = ConfigStore.Load();
        if (Migration.MigrateProfiles(Config)) SaveConfig();
        Strings.SetLanguage(Config.Settings.Language);
        Log.Info($"Starting {(background ? "in background" : "with window")}, {Config.Profiles.Count} profile(s)");

        Service = new ReconnectService(() => Config);
        _tray = new TrayIcon(this);
        Service.StateChanged += (_, e) => Dispatcher.TryEnqueue(() =>
        {
            _tray?.OnStateChanged(e);
            _main?.OnStateChanged(e);
        });
        Service.Start();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.TryEnqueue(ShowMainWindow);
        }) { IsBackground = true, Name = "ShowEventListener" }.Start();

        if (!background || Config.Profiles.Count == 0) ShowMainWindow();
    }

    internal void ShowMainWindow()
    {
        if (_main == null && Strings.CultureName != "en-US")
            Resources["PathFontFamily"] = Resources["ContentControlThemeFontFamily"];
        if (_main == null && Strings.CultureName == "en-US")
        {
            // On a Japanese/Korean Windows the default UI font draws "\" as a currency sign.
            // When the app itself is in English, use the Latin UI font so UNC paths read \\server\share.
            var latin = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
            Resources["ContentControlThemeFontFamily"] = latin;
            // Plain TextBlocks (e.g. inside SettingsCard descriptions) don't inherit from controls.
            var style = new Style(typeof(Microsoft.UI.Xaml.Controls.TextBlock));
            style.Setters.Add(new Setter(Microsoft.UI.Xaml.Controls.TextBlock.FontFamilyProperty, latin));
            Resources[typeof(Microsoft.UI.Xaml.Controls.TextBlock)] = style;
        }
        _main ??= new MainWindow();
        _main.ShowAndActivate();
    }

    internal void OnMainWindowHidden()
    {
        if (Config.Settings.Notifications && !Config.TrayHintShown)
        {
            Config.TrayHintShown = true;
            SaveConfig();
            _tray?.Notify(Strings.T("tray.hintTitle"), Strings.T("tray.hintBody"), H.NotifyIcon.Core.NotificationIcon.Info);
        }
    }

    internal void RefreshTray() => _tray?.Rebuild();

    internal void SaveConfig()
    {
        if (IsDemo) return;
        try { ConfigStore.Save(Config); }
        catch (Exception ex) { Log.Error("Failed to save config", ex); }
    }

    private static void Cleanup()
    {
        Migration.Run();
        var cfg = ConfigStore.Load();
        foreach (var p in cfg.Profiles)
        {
            try
            {
                DriveMapper.Unmount(p);
                ExplorerCustomizer.Remove(p);
                Native.CredMan.DeleteAll(p.Id);
            }
            catch (Exception ex)
            {
                Log.Error($"Cleanup failed for {p.LocalName}", ex);
            }
        }
        try { StartupManager.SetEnabled(false); } catch { }
        Log.Info($"Uninstall cleanup done for {cfg.Profiles.Count} profile(s)");
    }

    internal void ExitApp()
    {
        Log.Info("Exiting");
        _exiting = true;
        Service.Dispose();
        _tray?.Dispose();
        _main?.Close();
        try { _mutex?.ReleaseMutex(); } catch { }
        Exit();
    }
}
