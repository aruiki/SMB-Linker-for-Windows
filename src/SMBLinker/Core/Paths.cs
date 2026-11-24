using System;
using System.IO;

namespace SMBLinker.Core;

internal static class Paths
{
    public const string AppFolderName = "SMBLinker";
    /// <summary>Folder name used before the app was renamed (versions 1.0.x were called SmbAutoMount).</summary>
    public const string LegacyFolderName = "SmbAutoMount";

    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName));

    public static string LogDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, "logs"));

    public static string LegacyDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyFolderName);

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string LogFile => Path.Combine(LogDir, "SMBLinker.log");

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SMBLinker.exe");
    public static string DriveIconDir => Path.Combine(AppContext.BaseDirectory, "Assets", "DriveIcons");
    public static string DefaultDriveIcon => Path.Combine(DriveIconDir, "drive-green.ico");
    public static string AppIcon => Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
