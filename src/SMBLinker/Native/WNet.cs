using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SMBLinker.Native;

/// <summary>P/Invoke wrappers for the Windows Networking (WNet) API in mpr.dll.</summary>
internal static class WNet
{
    public const int NO_ERROR = 0;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_BAD_NETPATH = 53;
    public const int ERROR_NETWORK_ACCESS_DENIED = 65;
    public const int ERROR_BAD_NET_NAME = 67;
    public const int ERROR_ALREADY_ASSIGNED = 85;
    public const int ERROR_INVALID_PASSWORD = 86;
    public const int ERROR_MORE_DATA = 234;
    public const int ERROR_NO_MORE_ITEMS = 259;
    public const int ERROR_DEVICE_ALREADY_REMEMBERED = 1202;
    public const int ERROR_BAD_DEVICE = 1200;
    public const int ERROR_CONNECTION_UNAVAIL = 1201;
    public const int ERROR_NO_NET_OR_BAD_PATH = 1203;
    public const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;
    public const int ERROR_NO_NETWORK = 1222;
    public const int ERROR_NOT_CONNECTED = 2250;
    public const int ERROR_LOGON_FAILURE = 1326;
    public const int ERROR_ACCOUNT_RESTRICTION = 1327;
    public const int ERROR_ACCOUNT_DISABLED = 1331;
    public const int ERROR_ACCOUNT_LOCKED_OUT = 1909;
    public const int ERROR_PASSWORD_EXPIRED = 1330;
    public const int ERROR_BAD_USERNAME = 2202;
    public const int ERROR_GUEST_BLOCKED = 1272; // insecure guest logon blocked by policy
    public const int ERROR_SEM_TIMEOUT = 121;
    public const int ERROR_NETNAME_DELETED = 64;
    public const int ERROR_UNEXP_NET_ERR = 59;

    private const int RESOURCE_CONNECTED = 1;
    private const int RESOURCE_REMEMBERED = 3;
    private const int RESOURCETYPE_DISK = 1;
    private const int CONNECT_UPDATE_PROFILE = 0x1;
    private const int CONNECT_TEMPORARY = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string? lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE_OUT
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public IntPtr lpLocalName;
        public IntPtr lpRemoteName;
        public IntPtr lpComment;
        public IntPtr lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(NETRESOURCE netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetUser(string? name, StringBuilder userName, ref int length);

    /// <summary>User name a connection (drive letter or UNC path) was made with, or null if unknown.</summary>
    public static string? GetUser(string name)
    {
        int len = 256;
        var sb = new StringBuilder(len);
        int rc = WNetGetUser(name, sb, ref len);
        if (rc == ERROR_MORE_DATA)
        {
            sb = new StringBuilder(len);
            rc = WNetGetUser(name, sb, ref len);
        }
        return rc == NO_ERROR && sb.Length > 0 ? sb.ToString() : null;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetOpenEnum(int scope, int type, int usage, IntPtr netResource, out IntPtr handle);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetEnumResource(IntPtr handle, ref int count, IntPtr buffer, ref int bufferSize);

    [DllImport("mpr.dll")]
    private static extern int WNetCloseEnum(IntPtr handle);

    public record Connection(string? LocalName, string RemoteName);

    /// <summary>Connects a drive letter (e.g. "Y:") or, if localName is null, a deviceless UNC session.</summary>
    /// <param name="remember">If true, Windows also restores the mapping at logon (we normally do NOT want this).</param>
    public static int AddConnection(string? localName, string remoteName, string? userName, string? password, bool remember)
    {
        var nr = new NETRESOURCE { dwType = RESOURCETYPE_DISK, lpLocalName = localName, lpRemoteName = remoteName };
        int flags = remember ? CONNECT_UPDATE_PROFILE : CONNECT_TEMPORARY;
        return WNetAddConnection2(nr, password, string.IsNullOrWhiteSpace(userName) ? null : userName, flags);
    }

    /// <param name="forgetPersistent">Also remove the mapping from the user profile (HKCU\Network).</param>
    public static int CancelConnection(string name, bool forgetPersistent, bool force = true)
        => WNetCancelConnection2(name, forgetPersistent ? CONNECT_UPDATE_PROFILE : 0, force);

    /// <summary>Returns the remote name a local drive is mapped to, plus the WNet status code.</summary>
    public static (int code, string? remote) GetConnection(string localName)
    {
        int len = 512;
        var sb = new StringBuilder(len);
        int rc = WNetGetConnection(localName, sb, ref len);
        if (rc == ERROR_MORE_DATA)
        {
            sb = new StringBuilder(len);
            rc = WNetGetConnection(localName, sb, ref len);
        }
        // ERROR_CONNECTION_UNAVAIL still fills the remote name (remembered but not connected)
        return (rc, rc is NO_ERROR or ERROR_CONNECTION_UNAVAIL ? sb.ToString() : null);
    }

    public static List<Connection> EnumConnected() => Enum(RESOURCE_CONNECTED);
    public static List<Connection> EnumRemembered() => Enum(RESOURCE_REMEMBERED);

    private static List<Connection> Enum(int scope)
    {
        var list = new List<Connection>();
        if (WNetOpenEnum(scope, RESOURCETYPE_DISK, 0, IntPtr.Zero, out var handle) != NO_ERROR) return list;
        int bufSize = 16384;
        IntPtr buffer = Marshal.AllocHGlobal(bufSize);
        try
        {
            while (true)
            {
                int count = -1;
                int size = bufSize;
                int rc = WNetEnumResource(handle, ref count, buffer, ref size);
                if (rc == ERROR_MORE_DATA)
                {
                    Marshal.FreeHGlobal(buffer);
                    bufSize = size;
                    buffer = Marshal.AllocHGlobal(bufSize);
                    continue;
                }
                if (rc != NO_ERROR) break;
                int structSize = Marshal.SizeOf<NETRESOURCE_OUT>();
                for (int i = 0; i < count; i++)
                {
                    var r = Marshal.PtrToStructure<NETRESOURCE_OUT>(buffer + i * structSize);
                    string? local = r.lpLocalName == IntPtr.Zero ? null : Marshal.PtrToStringUni(r.lpLocalName);
                    string? remote = r.lpRemoteName == IntPtr.Zero ? null : Marshal.PtrToStringUni(r.lpRemoteName);
                    if (remote != null) list.Add(new Connection(string.IsNullOrEmpty(local) ? null : local, remote));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            WNetCloseEnum(handle);
        }
        return list;
    }
}
