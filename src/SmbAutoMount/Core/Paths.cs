using System;
using System.IO;

namespace SmbAutoMount.Core;

internal static class Paths
{
    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmbAutoMount"));

    public static string LogDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmbAutoMount", "logs"));

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string LogFile => Path.Combine(LogDir, "SmbAutoMount.log");

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SmbAutoMount.exe");
    public static string DriveIconDir => Path.Combine(AppContext.BaseDirectory, "Assets", "DriveIcons");
    public static string DefaultDriveIcon => Path.Combine(DriveIconDir, "drive-green.ico");
    public static string AppIcon => Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
