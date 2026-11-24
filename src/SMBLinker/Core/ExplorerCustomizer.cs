using System;
using Microsoft.Win32;
using SMBLinker.Native;

namespace SMBLinker.Core;

/// <summary>
/// Per-user Explorer customisations that Windows has no UI for:
/// a custom drive name and a custom drive icon. Uses HKCU only (no admin rights).
/// </summary>
internal static class ExplorerCustomizer
{
    private const string MountPoints = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2";
    private const string DriveIcons = @"Software\Classes\Applications\Explorer.exe\Drives";

    /// <summary>MountPoints2 key name for a UNC path: \\server\share\sub -> ##server#share#sub</summary>
    private static string MountKey(MountProfile p) => p.RemotePath.Replace('\\', '#');

    public static void Apply(MountProfile p)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey($@"{MountPoints}\{MountKey(p)}"))
            {
                if (string.IsNullOrWhiteSpace(p.Label)) key.DeleteValue("_LabelFromReg", false);
                else key.SetValue("_LabelFromReg", p.Label.Trim(), RegistryValueKind.String);
            }

            var letter = p.LocalName.TrimEnd(':');
            if (string.IsNullOrWhiteSpace(p.Icon))
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{DriveIcons}\{letter}", false);
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"{DriveIcons}\{letter}\DefaultIcon");
                key.SetValue("", p.Icon.Trim(), RegistryValueKind.String);
            }

            Shell.NotifyAssociationsChanged();
            Shell.NotifyDriveUpdated(p.LocalName + "\\");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not apply Explorer customisation for {p.LocalName}", ex);
        }
    }

    /// <summary>Undo everything Apply() wrote for this profile.</summary>
    public static void Remove(MountProfile p)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey($@"{MountPoints}\{MountKey(p)}", writable: true))
                key?.DeleteValue("_LabelFromReg", false);
            if (!string.IsNullOrWhiteSpace(p.Icon))
                Registry.CurrentUser.DeleteSubKeyTree($@"{DriveIcons}\{p.LocalName.TrimEnd(':')}", false);
            Shell.NotifyAssociationsChanged();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not remove Explorer customisation for {p.LocalName}", ex);
        }
    }
}
