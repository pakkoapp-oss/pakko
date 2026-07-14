using System.Runtime.InteropServices;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Grants the AppContainer SID access to a quarantine folder via SetEntriesInAclW/
/// SetNamedSecurityInfoW — the raw production equivalent of Phase 0's icacls-based spike (see
/// DECISIONS.md's T-F52 Phase 0 entry, which confirmed these three access levels work: "in\" =
/// Read &amp; Execute, "out\" = Modify, quarantine-root = traverse-only, all as an ADD to the
/// folder's existing DACL, never a full replace — an AppContainer process has zero filesystem
/// access outside paths explicitly ACL'd to its SID, but the folder's owner/SYSTEM/Administrators
/// entries must still be preserved so Pakko's own (non-sandboxed) process identity keeps its
/// normal access to stage/validate/move files.
/// </summary>
internal static class QuarantineAcl
{
    // Standard, documented NTFS "simple permission" masks (the same values Windows' own ACL UI
    // and icacls use under these names) — not invented constants.
    private const uint FileGenericReadExecute = 0x1200A9; // "Read & Execute"
    private const uint FileGenericModify = 0x1301BF;       // "Modify"
    private const uint FileTraverse = 0x0020;              // "Traverse Folder" only — no read/write/execute of contents

    private const int SubContainersAndObjectsInherit = 0x3;
    private const int NoInheritance = 0x0;

    public static void GrantReadExecute(string path, SafeSidHandle sid)
        => Grant(path, sid, FileGenericReadExecute, SubContainersAndObjectsInherit);

    public static void GrantModify(string path, SafeSidHandle sid)
        => Grant(path, sid, FileGenericModify, SubContainersAndObjectsInherit);

    public static void GrantTraverseOnly(string path, SafeSidHandle sid)
        => Grant(path, sid, FileTraverse, NoInheritance);

    private static void Grant(string path, SafeSidHandle sid, uint accessMask, int inheritance)
    {
        const int SE_FILE_OBJECT = 1;
        const uint DACL_SECURITY_INFORMATION = 0x00000004;

        uint getResult = NativeMethods.GetNamedSecurityInfoW(
            path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
            IntPtr.Zero, IntPtr.Zero, out IntPtr existingAcl, IntPtr.Zero, out IntPtr securityDescriptor);
        if (getResult != 0)
            throw new InvalidOperationException($"GetNamedSecurityInfoW('{path}') failed (Win32 error {getResult}).");

        try
        {
            var explicitAccess = new EXPLICIT_ACCESS_W
            {
                grfAccessPermissions = accessMask,
                grfAccessMode = 1, // GRANT_ACCESS
                grfInheritance = (uint)inheritance,
                Trustee = new TRUSTEE_W
                {
                    pMultipleTrustee = IntPtr.Zero,
                    MultipleTrusteeOperation = 0,
                    TrusteeForm = 0, // TRUSTEE_IS_SID
                    TrusteeType = 0, // TRUSTEE_IS_UNKNOWN
                    ptstrName = sid.DangerousGetHandle(),
                },
            };

            // Passing the folder's existing DACL as OldAcl merges this grant in — it does not
            // replace owner/SYSTEM/Administrators entries, which the previous naive draft of
            // this method would have wiped out (see this method's own doc comment).
            uint setEntriesResult = NativeMethods.SetEntriesInAclW(1, ref explicitAccess, existingAcl, out IntPtr newAcl);
            if (setEntriesResult != 0)
                throw new InvalidOperationException($"SetEntriesInAclW('{path}') failed (Win32 error {setEntriesResult}).");

            try
            {
                uint setResult = NativeMethods.SetNamedSecurityInfoW(
                    path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
                    IntPtr.Zero, IntPtr.Zero, newAcl, IntPtr.Zero);
                if (setResult != 0)
                    throw new InvalidOperationException($"SetNamedSecurityInfoW('{path}') failed (Win32 error {setResult}).");
            }
            finally
            {
                NativeMethods.LocalFree(newAcl);
            }
        }
        finally
        {
            NativeMethods.LocalFree(securityDescriptor);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRUSTEE_W
    {
        public IntPtr pMultipleTrustee;
        public int MultipleTrusteeOperation;
        public int TrusteeForm;
        public int TrusteeType;
        public IntPtr ptstrName; // holds a raw PSID when TrusteeForm = TRUSTEE_IS_SID
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXPLICIT_ACCESS_W
    {
        public uint grfAccessPermissions;
        public int grfAccessMode;
        public uint grfInheritance;
        public TRUSTEE_W Trustee;
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern uint GetNamedSecurityInfoW(
            string pObjectName,
            int objectType,
            uint securityInfo,
            IntPtr ppsidOwner,
            IntPtr ppsidGroup,
            out IntPtr ppDacl,
            IntPtr ppSacl,
            out IntPtr ppSecurityDescriptor);

        [DllImport("advapi32.dll", SetLastError = false)]
        public static extern uint SetEntriesInAclW(
            int cCountOfExplicitEntries,
            ref EXPLICIT_ACCESS_W pListOfExplicitEntries,
            IntPtr oldAcl,
            out IntPtr newAcl);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern uint SetNamedSecurityInfoW(
            string pObjectName,
            int objectType,
            uint securityInfo,
            IntPtr psidOwner,
            IntPtr psidGroup,
            IntPtr dacl,
            IntPtr sacl);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);
    }
}
