using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using SmbAutoMount.Native;

namespace SmbAutoMount.Core;

public enum DiagLevel { Ok, Info, Warn, Fail }

public sealed record DiagItem(DiagLevel Level, string Title, string Detail);

public sealed class DiagReport
{
    public List<DiagItem> Items { get; } = new();
    public bool HasConflicts { get; set; }
    public bool HasWindowsPersistentMapping { get; set; }

    public string ToText(MountProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"SmbAutoMount {typeof(DiagReport).Assembly.GetName().Version} diagnostics");
        sb.AppendLine($"Windows {Environment.OSVersion.Version}");
        sb.AppendLine($"Target: {p.LocalName} -> {p.RemotePath} (user: {(string.IsNullOrEmpty(p.UserName) ? "-" : "set")})");
        sb.AppendLine();
        foreach (var i in Items)
            sb.AppendLine($"[{i.Level.ToString().ToUpperInvariant()}] {i.Title}{(string.IsNullOrEmpty(i.Detail) ? "" : " - " + i.Detail)}");
        return sb.ToString();
    }
}

/// <summary>Step-by-step checks that explain *why* a share does not connect.</summary>
internal static class Diagnostics
{
    public static async Task<DiagReport> RunAsync(MountProfile p)
    {
        var r = new DiagReport();
        void Add(DiagLevel l, string titleKey, string detail = "") => r.Items.Add(new DiagItem(l, Strings.T(titleKey), detail));

        // 1. Settings
        if (string.IsNullOrWhiteSpace(p.Server) || string.IsNullOrWhiteSpace(p.Share))
        {
            Add(DiagLevel.Fail, "diag.config", Strings.T("diag.configMissing"));
            return r;
        }
        Add(DiagLevel.Ok, "diag.config", $"{p.LocalName} -> {p.RemotePath}");

        // 2. Name resolution
        var ips = await DriveMapper.ResolveAsync(p.Server);
        if (ips.Length == 0)
        {
            Add(DiagLevel.Fail, "diag.dns", Strings.F("diag.dnsFail", p.Server));
            return r;
        }
        Add(DiagLevel.Ok, "diag.dns", string.Join(", ", ips.Select(i => i.ToString())));

        // 3. SMB port
        if (!await DriveMapper.IsReachableAsync(p.Server, 3000))
        {
            Add(DiagLevel.Fail, "diag.port", Strings.T("diag.portFail"));
            return r;
        }
        Add(DiagLevel.Ok, "diag.port", Strings.T("diag.portOk"));

        // 4. Stored password
        if (!string.IsNullOrWhiteSpace(p.UserName))
        {
            bool has = CredMan.Exists(CredMan.TargetFor(p.Id));
            Add(has ? DiagLevel.Ok : DiagLevel.Warn, "diag.cred", Strings.T(has ? "diag.credOk" : "diag.credMissing"));
        }

        // 5. Drive letter
        var (code, current) = WNet.GetConnection(p.LocalName);
        if (DriveMapper.IsLocalVolume(p.LocalName))
            Add(DiagLevel.Fail, "diag.letter", Strings.F("err.letterLocal", p.LocalName));
        else if (code == WNet.NO_ERROR && DriveMapper.SameRemote(current, p.RemotePath))
            Add(DiagLevel.Ok, "diag.letter", Strings.T("diag.letterMapped"));
        else if (code == WNet.ERROR_CONNECTION_UNAVAIL && DriveMapper.SameRemote(current, p.RemotePath))
            Add(DiagLevel.Warn, "diag.letter", Strings.T("diag.letterStale"));
        else if (current != null && !DriveMapper.SameRemote(current, p.RemotePath))
            Add(DiagLevel.Fail, "diag.letter", Strings.F("err.letterUsed", p.LocalName, current));
        else
            Add(DiagLevel.Ok, "diag.letter", Strings.T("diag.letterFree"));

        // 6. Windows' own persistent mapping (the one that races the network at logon)
        using (var net = Registry.CurrentUser.OpenSubKey($@"Network\{p.LocalName.TrimEnd(':')}"))
        {
            if (net != null)
            {
                r.HasWindowsPersistentMapping = true;
                Add(DiagLevel.Warn, "diag.persistent", Strings.T("diag.persistentDetail"));
            }
        }

        // 7. Other sessions to the same server (cause of error 1219)
        var others = DriveMapper.ConflictingConnections(p);
        if (others.Count > 0)
        {
            r.HasConflicts = true;
            Add(DiagLevel.Warn, "diag.conflicts",
                Strings.F("diag.conflictsDetail", string.Join(", ", others.Select(o =>
                    $"{o.LocalName ?? ""} {o.RemoteName} ({WNet.GetUser(o.LocalName ?? o.RemoteName) ?? "?"})".Trim()))));
        }
        else
        {
            Add(DiagLevel.Ok, "diag.conflicts", Strings.T("diag.conflictsNone"));
        }

        // 8. Real login test (only if the drive is not already happily connected)
        if (!(code == WNet.NO_ERROR && DriveMapper.SameRemote(current, p.RemotePath)))
        {
            var res = await Task.Run(() => DriveMapper.TestLogin(p, null));
            Add(res.State == MountState.Connected ? DiagLevel.Ok : DiagLevel.Fail, "diag.login",
                res.State == MountState.Connected ? Strings.T("diag.loginOk") : $"{res.Message} (code {res.Code})");
        }

        return r;
    }

    /// <summary>Applies the fixes the report suggested.</summary>
    public static List<string> Repair(MountProfile p, DiagReport r)
    {
        var done = new List<string>();
        if (r.HasConflicts)
            foreach (var x in DriveMapper.CleanupConflicts(p)) done.Add(Strings.F("diag.fixedConflict", x));
        if (r.HasWindowsPersistentMapping)
        {
            WNet.CancelConnection(p.LocalName, forgetPersistent: true, force: true);
            Registry.CurrentUser.DeleteSubKeyTree($@"Network\{p.LocalName.TrimEnd(':')}", false);
            done.Add(Strings.T("diag.fixedPersistent"));
        }
        return done;
    }
}
