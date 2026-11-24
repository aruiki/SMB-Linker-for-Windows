using System;
using System.IO;
using System.Text.Json;

namespace SMBLinker.Core;

internal static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile))
            {
                var json = File.ReadAllText(Paths.ConfigFile);
                return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to read config, starting with an empty one", ex);
            try { File.Copy(Paths.ConfigFile, Paths.ConfigFile + ".broken", overwrite: true); } catch { }
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        var tmp = Paths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, Paths.ConfigFile, overwrite: true);
    }
}
