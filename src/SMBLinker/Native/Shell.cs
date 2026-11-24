using System;
using System.Runtime.InteropServices;

namespace SMBLinker.Native;

internal static class Shell
{
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNE_DRIVEADD = 0x00000100;
    private const int SHCNE_DRIVEREMOVED = 0x00000080;
    private const int SHCNE_UPDATEITEM = 0x00002000;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint SHCNF_PATHW = 0x0005;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHChangeNotify")]
    private static extern void SHChangeNotifyPath(int eventId, uint flags, string item1, IntPtr item2);

    /// <summary>Asks Explorer to refresh icons / labels.</summary>
    public static void NotifyAssociationsChanged() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    public static void NotifyDriveUpdated(string driveRoot) => SHChangeNotifyPath(SHCNE_UPDATEITEM, SHCNF_PATHW, driveRoot, IntPtr.Zero);
}
