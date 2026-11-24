using System;
using System.IO;
using System.Linq;
using SMBLinker.Native;

namespace SMBLinker.Core;

/// <summary>
/// Carries settings over from version 1.0.x, when the app was called "SmbAutoMount":
/// config file, saved passwords, startup entry, and icon paths that point at an old install folder.
/// Safe to run on every start; each step does nothing once migrated.
/// </summary>
internal static class Migration
{
    public static void Run()
    {
        try
        {
            MigrateConfigFile();
            if (StartupManager.MigrateLegacy()) Log.Info("Migrated startup entry from SmbAutoMount");
        }
        catch (Exception ex)
        {
            Log.Error("Migration from SmbAutoMount failed", ex);
        }
    }

    private static void MigrateConfigFile()
    {
        var legacy = Path.Combine(Paths.LegacyDataDir, "config.json");
        if (File.Exists(Paths.ConfigFile) || !File.Exists(legacy)) return;
        File.Copy(legacy, Paths.ConfigFile);
        Log.Info("Migrated settings from SmbAutoMount");
        try { Directory.Delete(Paths.LegacyDataDir, recursive: true); } catch { }
    }

    /// <summary>Per-profile fixes that need the loaded config. Returns true if the config changed.</summary>
    public static bool MigrateProfiles(AppConfig config)
    {
        bool changed = false;
        foreach (var p in config.Profiles)
        {
            try
            {
                if (CredMan.MigrateLegacy(p.Id)) Log.Info($"Migrated saved password for {p.LocalName}");
            }
            catch (Exception ex)
            {
                Log.Error($"Password migration failed for {p.LocalName}", ex);
            }

            // Built-in icon referenced from an old install folder -> point at this install.
            if (!string.IsNullOrWhiteSpace(p.Icon) && !File.Exists(p.Icon.Split(',')[0].Trim('"')))
            {
                var candidate = Path.Combine(Paths.DriveIconDir, Path.GetFileName(p.Icon.Split(',')[0].Trim('"')));
                if (File.Exists(candidate))
                {
                    p.Icon = candidate;
                    changed = true;
                    Log.Info($"Updated icon path for {p.LocalName}");
                }
            }
        }
        return changed;
    }
}
