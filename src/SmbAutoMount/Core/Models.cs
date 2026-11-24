using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SmbAutoMount.Core;

/// <summary>One network drive the user wants mounted. Contains NO password.</summary>
public sealed class MountProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;

    /// <summary>Host name or IP, e.g. "192.168.0.10" or "truenas.local".</summary>
    public string Server { get; set; } = "";

    /// <summary>Share path below the server, e.g. "home" or "media\\movies".</summary>
    public string Share { get; set; } = "";

    /// <summary>Drive letter without colon, e.g. "Y".</summary>
    public string DriveLetter { get; set; } = "Z";

    /// <summary>User name sent to the server. Empty = use current Windows credentials.</summary>
    public string UserName { get; set; } = "";

    /// <summary>Custom name shown in Explorer instead of "share (\\server)". Empty = Windows default.</summary>
    public string Label { get; set; } = "";

    /// <summary>Icon resource "path,index" or path to .ico. Empty = Windows default.</summary>
    public string Icon { get; set; } = "";

    [JsonIgnore]
    public string RemotePath => $@"\\{Server.Trim().TrimStart('\\')}\{Share.Trim().Trim('\\')}";
    [JsonIgnore]
    public string LocalName => $"{DriveLetter.Trim().TrimEnd(':').ToUpperInvariant()}:";

    /// <summary>User name in the form the server expects. A bare name is qualified with the server
    /// so Windows does not send the local PC / Microsoft account domain.</summary>
    [JsonIgnore]
    public string? QualifiedUserName
    {
        get
        {
            var u = UserName.Trim();
            if (u.Length == 0) return null;
            if (u.Contains('\\') || u.Contains('@')) return u;
            return $@"{Server.Trim()}\{u}";
        }
    }

    public MountProfile Clone() => (MountProfile)MemberwiseClone();
}

public sealed class AppSettings
{
    public bool Notifications { get; set; } = true;
    public bool AutoFixConflicts { get; set; } = true;
    public int HealthCheckSeconds { get; set; } = 60;
    /// <summary>"auto", "ja" or "en".</summary>
    public string Language { get; set; } = "auto";
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public bool TrayHintShown { get; set; }
    public AppSettings Settings { get; set; } = new();
    public List<MountProfile> Profiles { get; set; } = new();
}

public enum MountState
{
    Unknown,
    Disabled,
    WaitingForNetwork,
    Connecting,
    Connected,
    Unreachable,
    AuthFailed,
    Error,
}
