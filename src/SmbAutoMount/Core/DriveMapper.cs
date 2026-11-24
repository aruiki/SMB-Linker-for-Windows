using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SmbAutoMount.Native;

namespace SmbAutoMount.Core;

public sealed record MountResult(MountState State, int Code, string Message);

/// <summary>Low-level mount / unmount / probe operations. Stateless and thread-safe.</summary>
internal static class DriveMapper
{
    public const int SmbPort = 445;

    /// <summary>True if TCP 445 on the server accepts a connection within the timeout.</summary>
    public static async Task<bool> IsReachableAsync(string server, int timeoutMs = 1500, CancellationToken ct = default)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await client.ConnectAsync(server.Trim(), SmbPort, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Directory.Exists on a dead network share can block for a long time; bound it.</summary>
    public static async Task<bool> IsAccessibleAsync(string path, int timeoutMs = 5000)
    {
        var probe = Task.Run(() => Directory.Exists(path));
        var done = await Task.WhenAny(probe, Task.Delay(timeoutMs)).ConfigureAwait(false);
        return done == probe && probe.Result;
    }

    public static bool SameRemote(string? a, string? b) =>
        a != null && b != null &&
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Is the letter a local (non-network) volume such as a USB stick or DVD drive?</summary>
    public static bool IsLocalVolume(string localName)
    {
        try
        {
            var d = new DriveInfo(localName);
            return d.DriveType != DriveType.Network && d.DriveType != DriveType.NoRootDirectory;
        }
        catch
        {
            return false;
        }
    }

    public static List<WNet.Connection> ConnectionsToServer(string server)
    {
        var prefix = $@"\\{server.Trim()}\";
        return WNet.EnumConnected()
            .Where(c => c.RemoteName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>"SERVER\user", "user@server" and "user" all compare by the account part.</summary>
    public static bool SameUser(string? a, string? b)
    {
        static string Account(string s)
        {
            s = s.Trim();
            int i = s.LastIndexOf('\\');
            if (i >= 0) s = s[(i + 1)..];
            int at = s.IndexOf('@');
            if (at >= 0) s = s[..at];
            return s;
        }
        return a != null && b != null && string.Equals(Account(a), Account(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The user name this profile connects (or is connected) as, if it can be determined.</summary>
    public static string? EffectiveUser(MountProfile p) =>
        p.QualifiedUserName ?? WNet.GetUser(p.LocalName);

    /// <summary>
    /// Connections to the same server made with a DIFFERENT user name. Only these cause ERROR 1219
    /// ("multiple connections ... by the same user, using more than one user name").
    /// If our own user name is unknown, every other connection is a suspect.
    /// </summary>
    public static List<WNet.Connection> ConflictingConnections(MountProfile p)
    {
        var me = EffectiveUser(p);
        return ConnectionsToServer(p.Server)
            .Where(c => !(c.LocalName != null && string.Equals(c.LocalName, p.LocalName, StringComparison.OrdinalIgnoreCase)))
            .Where(c => me == null || !SameUser(WNet.GetUser(c.LocalName ?? c.RemoteName), me))
            .ToList();
    }

    /// <summary>Removes connections to the same server that use a different user name (cause of error 1219).</summary>
    public static List<string> CleanupConflicts(MountProfile p)
    {
        var removed = new List<string>();
        foreach (var c in ConflictingConnections(p))
        {
            var name = c.LocalName ?? c.RemoteName;
            int rc = WNet.CancelConnection(name, forgetPersistent: false, force: true);
            Log.Info($"Conflict cleanup: cancel {name} -> {c.RemoteName}: {rc}");
            if (rc == WNet.NO_ERROR) removed.Add(c.LocalName != null ? $"{c.LocalName} {c.RemoteName}" : c.RemoteName);
        }
        // IPC$ sessions are not always enumerated but still count as a connection
        WNet.CancelConnection($@"\\{p.Server.Trim()}\IPC$", forgetPersistent: false, force: true);
        return removed;
    }

    /// <summary>Maps the drive. Assumes the server is reachable.</summary>
    public static MountResult Mount(MountProfile p, AppSettings settings)
    {
        var local = p.LocalName;
        var remote = p.RemotePath;

        if (IsLocalVolume(local))
            return new MountResult(MountState.Error, WNet.ERROR_ALREADY_ASSIGNED, Strings.F("err.letterLocal", local));

        // What does Windows currently have on this letter?
        var (code, current) = WNet.GetConnection(local);
        if (code == WNet.NO_ERROR || code == WNet.ERROR_CONNECTION_UNAVAIL)
        {
            if (current != null && !SameRemote(current, remote))
                return new MountResult(MountState.Error, WNet.ERROR_ALREADY_ASSIGNED, Strings.F("err.letterUsed", local, current));

            // Same share but stale/remembered (e.g. Windows' own failed logon restore): drop it,
            // including the persistent HKCU\Network entry so Windows stops racing us at logon.
            int rc0 = WNet.CancelConnection(local, forgetPersistent: true, force: true);
            Log.Info($"Removed existing mapping {local} -> {current} (status {code}): {rc0}");
        }

        string? user = p.QualifiedUserName;
        string? password = null;
        try
        {
            var cred = CredMan.Read(CredMan.TargetFor(p.Id));
            if (cred != null)
            {
                password = cred.Value.password;
                if (user == null && cred.Value.userName.Length > 0) user = cred.Value.userName;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read stored credential", ex);
        }

        int rc = WNet.AddConnection(local, remote, user, password, remember: false);
        Log.Info($"Mount {local} -> {remote} as '{user ?? "(current user)"}': {rc}");

        if (rc == WNet.ERROR_SESSION_CREDENTIAL_CONFLICT && settings.AutoFixConflicts)
        {
            var removed = CleanupConflicts(p);
            Log.Info($"1219 auto-fix removed {removed.Count} connection(s), retrying");
            rc = WNet.AddConnection(local, remote, user, password, remember: false);
            Log.Info($"Retry mount {local}: {rc}");
        }
        else if (rc is WNet.ERROR_ALREADY_ASSIGNED or WNet.ERROR_DEVICE_ALREADY_REMEMBERED)
        {
            WNet.CancelConnection(local, forgetPersistent: true, force: true);
            rc = WNet.AddConnection(local, remote, user, password, remember: false);
            Log.Info($"Retry mount after releasing letter {local}: {rc}");
        }

        password = null;
        return Interpret(rc);
    }

    /// <summary>
    /// Removes Windows' own "reconnect at sign-in" entry (HKCU\Network\X) without disconnecting
    /// the drive, so only SmbAutoMount restores it - after the network is ready.
    /// </summary>
    public static void ForgetWindowsPersistence(MountProfile p)
    {
        try
        {
            var key = $@"Network\{p.LocalName.TrimEnd(':')}";
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key);
            if (k == null) return;
            var remote = k.GetValue("RemotePath") as string;
            if (remote != null && !SameRemote(remote, p.RemotePath)) return; // someone else's mapping
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false);
            Log.Info($"Removed Windows' remembered mapping for {p.LocalName}");
        }
        catch (Exception ex)
        {
            Log.Error("Could not remove Windows' remembered mapping", ex);
        }
    }

    public static int Unmount(MountProfile p) => WNet.CancelConnection(p.LocalName, forgetPersistent: true, force: true);

    /// <summary>Opens and closes a deviceless session to test credentials without touching drive letters.</summary>
    public static MountResult TestLogin(MountProfile p, string? passwordOverride)
    {
        string? user = p.QualifiedUserName;
        string? password = passwordOverride;
        if (password == null)
        {
            var cred = CredMan.Read(CredMan.TargetFor(p.Id));
            password = cred?.password;
        }
        int rc = WNet.AddConnection(null, p.RemotePath, user, password, remember: false);
        if (rc == WNet.NO_ERROR) WNet.CancelConnection(p.RemotePath, forgetPersistent: false, force: true);
        return Interpret(rc);
    }

    public static MountResult Interpret(int rc) => rc switch
    {
        WNet.NO_ERROR => new(MountState.Connected, rc, Strings.T("res.ok")),
        WNet.ERROR_LOGON_FAILURE or WNet.ERROR_INVALID_PASSWORD or WNet.ERROR_BAD_USERNAME
            => new(MountState.AuthFailed, rc, Strings.T("err.logon")),
        WNet.ERROR_ACCOUNT_LOCKED_OUT => new(MountState.AuthFailed, rc, Strings.T("err.locked")),
        WNet.ERROR_ACCOUNT_DISABLED or WNet.ERROR_ACCOUNT_RESTRICTION or WNet.ERROR_PASSWORD_EXPIRED
            => new(MountState.AuthFailed, rc, Strings.T("err.account")),
        WNet.ERROR_SESSION_CREDENTIAL_CONFLICT => new(MountState.Error, rc, Strings.T("err.1219")),
        WNet.ERROR_ACCESS_DENIED or WNet.ERROR_NETWORK_ACCESS_DENIED => new(MountState.AuthFailed, rc, Strings.T("err.denied")),
        WNet.ERROR_BAD_NET_NAME => new(MountState.Error, rc, Strings.T("err.badShare")),
        WNet.ERROR_GUEST_BLOCKED => new(MountState.Error, rc, Strings.T("err.guest")),
        WNet.ERROR_BAD_NETPATH or WNet.ERROR_NO_NET_OR_BAD_PATH or WNet.ERROR_NO_NETWORK
            or WNet.ERROR_SEM_TIMEOUT or WNet.ERROR_NETNAME_DELETED or WNet.ERROR_UNEXP_NET_ERR
            => new(MountState.Unreachable, rc, Strings.T("err.unreachable")),
        _ => new(MountState.Error, rc, Strings.F("err.other", rc, new System.ComponentModel.Win32Exception(rc).Message)),
    };

    public static async Task<IPAddress[]> ResolveAsync(string server)
    {
        if (IPAddress.TryParse(server.Trim(), out var ip)) return new[] { ip };
        try { return await Dns.GetHostAddressesAsync(server.Trim()).ConfigureAwait(false); }
        catch { return Array.Empty<IPAddress>(); }
    }
}
