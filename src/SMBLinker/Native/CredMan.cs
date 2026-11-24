using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SMBLinker.Native;

/// <summary>
/// Stores passwords in Windows Credential Manager (DPAPI-protected per Windows user).
/// Passwords are never written to config files.
/// </summary>
internal static class CredMan
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static string TargetFor(Guid profileId) => $"SMBLinker:{profileId:N}";

    /// <summary>Credential name used by 1.0.x (the app was called SmbAutoMount).</summary>
    private static string LegacyTargetFor(Guid profileId) => $"SmbAutoMount:{profileId:N}";

    /// <summary>Moves a password saved by 1.0.x to the new name. Returns true if one was moved.</summary>
    public static bool MigrateLegacy(Guid profileId)
    {
        var legacy = LegacyTargetFor(profileId);
        var old = Read(legacy);
        if (old == null) return false;
        if (!Exists(TargetFor(profileId))) Save(TargetFor(profileId), old.Value.userName, old.Value.password);
        Delete(legacy);
        return true;
    }

    /// <summary>Removes the password for a profile under both the current and the 1.0.x name.</summary>
    public static void DeleteAll(Guid profileId)
    {
        Delete(TargetFor(profileId));
        Delete(LegacyTargetFor(profileId));
    }

    public static void Save(string target, string userName, string password)
    {
        byte[] blob = Encoding.Unicode.GetBytes(password);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                Comment = "SMB Linker network drive password",
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = userName,
            };
            if (!CredWrite(ref cred, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            // wipe the unmanaged copy and the managed byte array
            for (int i = 0; i < blob.Length; i++) Marshal.WriteByte(blobPtr, i, 0);
            Marshal.FreeHGlobal(blobPtr);
            Array.Clear(blob);
        }
    }

    public static (string userName, string password)? Read(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr))
        {
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND) return null;
            throw new Win32Exception(err);
        }
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            string pw = cred.CredentialBlobSize > 0
                ? Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2)
                : string.Empty;
            return (cred.UserName ?? string.Empty, pw);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public static bool Exists(string target) => CredRead(target, CRED_TYPE_GENERIC, 0, out var p) && Free(p);

    private static bool Free(IntPtr p) { CredFree(p); return true; }

    public static void Delete(string target) => CredDelete(target, CRED_TYPE_GENERIC, 0);
}
