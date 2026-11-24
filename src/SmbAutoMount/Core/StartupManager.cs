using System;
using Microsoft.Win32;

namespace SmbAutoMount.Core;

/// <summary>"Start with Windows" via HKCU\...\Run (per user, no admin rights, no console window).</summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SmbAutoMount";

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
    }
}
