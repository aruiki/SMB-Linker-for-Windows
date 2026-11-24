using System;
using Microsoft.Win32;

namespace SMBLinker.Core;

/// <summary>"Start with Windows" via HKCU\...\Run (per user, no admin rights, no console window).</summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SMBLinker";
    private const string LegacyValueName = "SmbAutoMount";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Paths.ExePath}\" --background");
        else key.DeleteValue(ValueName, false);
        key.DeleteValue(LegacyValueName, false);
    }

    /// <summary>Moves the 1.0.x "SmbAutoMount" startup entry to the new name. Returns true if one was found.</summary>
    public static bool MigrateLegacy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(LegacyValueName) is not string) return false;
        key.DeleteValue(LegacyValueName, false);
        key.SetValue(ValueName, $"\"{Paths.ExePath}\" --background");
        return true;
    }
}
