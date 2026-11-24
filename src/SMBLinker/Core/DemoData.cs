using System;
using System.Collections.Generic;
using System.IO;

namespace SMBLinker.Core;

/// <summary>
/// Sample data for "--demo" mode, used to take README screenshots without exposing real
/// servers or user names. Demo mode never touches the network, Credential Manager, the registry
/// or the config file.
///   SMBLinker.exe --demo [--demo-lang=en|ja] [--demo-theme=light|dark] [--demo-open=edit|diag]
/// </summary>
internal static class DemoData
{
    public static bool IsDemo(string[] args) => Has(args, "--demo");
    public static string? Option(string[] args, string name)
    {
        foreach (var a in args)
            if (a.StartsWith($"--{name}=", StringComparison.OrdinalIgnoreCase)) return a[(name.Length + 3)..];
        return null;
    }
    private static bool Has(string[] args, string flag) => Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string Icon(string name) => Path.Combine(Paths.DriveIconDir, name + ".ico");

    public static AppConfig Config() => new()
    {
        TrayHintShown = true,
        Profiles = new List<MountProfile>
        {
            new() { Server = "nas.local", Share = "media", DriveLetter = "M", UserName = "alex", Label = "Media", Icon = Icon("drive-media") },
            new() { Server = "nas.local", Share = "photos", DriveLetter = "P", UserName = "alex", Label = "Photos", Icon = Icon("drive-photo") },
            new() { Server = "nas.local", Share = "home\\alex", DriveLetter = "H", UserName = "alex", Label = "Home", Icon = Icon("drive-home") },
            new() { Server = "192.168.1.20", Share = "backup", DriveLetter = "B", UserName = "backup", Label = "Backup", Icon = Icon("drive-backup") },
        },
    };

    /// <summary>State shown for each sample drive (by index).</summary>
    public static (MountState state, string message) StateFor(int index, MountProfile p) => index switch
    {
        3 => (MountState.WaitingForNetwork, Strings.F("state.waiting", p.Server)),
        _ => (MountState.Connected, Strings.T("state.connected")),
    };

    public static DiagReport Report(MountProfile p)
    {
        var r = new DiagReport { HasConflicts = true, HasWindowsPersistentMapping = true };
        void Add(DiagLevel l, string key, string detail) => r.Items.Add(new DiagItem(l, Strings.T(key), detail));
        Add(DiagLevel.Ok, "diag.config", $"{p.LocalName} -> {p.RemotePath}");
        Add(DiagLevel.Ok, "diag.dns", "192.168.1.20");
        Add(DiagLevel.Ok, "diag.port", Strings.T("diag.portOk"));
        Add(DiagLevel.Ok, "diag.cred", Strings.T("diag.credOk"));
        Add(DiagLevel.Warn, "diag.letter", Strings.T("diag.letterStale"));
        Add(DiagLevel.Warn, "diag.persistent", Strings.T("diag.persistentDetail"));
        Add(DiagLevel.Warn, "diag.conflicts", Strings.F("diag.conflictsDetail", @"\\192.168.1.20\IPC$ (OLD-PC\guest)"));
        Add(DiagLevel.Ok, "diag.login", Strings.T("diag.loginOk"));
        return r;
    }
}
