using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SmbAutoMount.Core;
using SmbAutoMount.UI;

namespace SmbAutoMount;

public partial class App : Application
{
    private const string MutexName = @"Local\SmbAutoMount.SingleInstance";
    private const string ShowEventName = @"Local\SmbAutoMount.Show";

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

    public App()
    {
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

        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask that instance to show its window, then quit.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Exit();
            return;
        }

        Dispatcher = DispatcherQueue.GetForCurrentThread();
        Config = ConfigStore.Load();
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
        try { ConfigStore.Save(Config); }
        catch (Exception ex) { Log.Error("Failed to save config", ex); }
    }

    private static void Cleanup()
    {
        var cfg = ConfigStore.Load();
        foreach (var p in cfg.Profiles)
        {
            try
            {
                DriveMapper.Unmount(p);
                ExplorerCustomizer.Remove(p);
                Native.CredMan.Delete(Native.CredMan.TargetFor(p.Id));
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
