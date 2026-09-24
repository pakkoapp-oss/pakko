using System.Runtime.InteropServices;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Grants the AppContainer SID access to a quarantine folder via SetEntriesInAclW/
/// SetNamedSecurityInfoW — the raw production equivalent of Phase 0's icacls-based spike (see
/// DECISIONS.md's T-F52 Phase 0 entry, which confirmed these three access levels work: "in\" =
/// Read &amp; Execute, "out\" = Modify, quarantine-root = traverse-only (T-F196: plus list/
/// read-attributes, see <see cref="GrantTraverseListReadAttributes"/>), all as an ADD to the
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
    // T-F196: Traverse | List Folder | Read Attributes | SYNCHRONIZE — found by elimination (every
    // subset failed) as the minimum libarchive needs on the PARENT of tar.exe's -C directory to
    // stat a bare "./" entry, the first entry of any archive made with `tar -C dir .`.
    private const uint FileTraverseListReadAttributes = 0x1000A1;

    private const int SubContainersAndObjectsInherit = 0x3;
    private const int NoInheritance = 0x0;

    public static void GrantReadExecute(string path, SafeSidHandle sid)
        => Grant(path, sid, FileGenericReadExecute, SubContainersAndObjectsInherit, sharedParent: false);

    public static void GrantModify(string path, SafeSidHandle sid)
        => Grant(path, sid, FileGenericModify, SubContainersAndObjectsInherit, sharedParent: false);

    public static void GrantTraverseOnly(string path, SafeSidHandle sid)
        => Grant(path, sid, FileTraverse, NoInheritance, sharedParent: false);

    /// <summary>T-F196: for a per-scope quarantine root only (it holds nothing but that scope's
    /// own in\ and out\), never a shared or user-owned directory.</summary>
    public static void GrantTraverseListReadAttributes(string path, SafeSidHandle sid)
        => Grant(path, sid, FileTraverseListReadAttributes, NoInheritance, sharedParent: false);

    /// <summary>
    /// T-F195: traverse grant for the ONE directory every scope in every Pakko process shares
    /// (%TEMP%\PakkoTarSandbox). SetNamedSecurityInfoW re-propagates inheritable ACEs to every
    /// existing child on each call — a read-recompute-write of other live scopes' in\/out\ DACLs
    /// that races their own grants and was confirmed to drop a live out\ Modify grant
    /// (QuarantineAclParentRaceTests). Here: no write at all once the ACE is present (the steady
    /// state), and the one-time first write goes through SetFileSecurityW, which touches only this
    /// directory's own DACL, never its children.
    /// </summary>
    public static void EnsureSharedParentTraverse(string path, SafeSidHandle sid)
        => Grant(path, sid, FileTraverse, NoInheritance, sharedParent: true);

    private static void Grant(string path, SafeSidHandle sid, uint accessMask, int inheritance, bool sharedParent)
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
            // DangerousAddRef/Release pins sid alive across SetEntriesInAclW, the call that
            // actually dereferences the raw PSID from DangerousGetHandle() — without it, nothing
            // keeps sid reachable for the GC between the two calls (S3869).
            bool sidRefAdded = false;
            IntPtr newAcl;
            try
            {
                sid.DangerousAddRef(ref sidRefAdded);
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
                        ptstrName = sid.DangerousGetHandle(), // NOSONAR: S3869 — PSID struct field (TRUSTEE_W), not a P/Invoke parameter the marshaller can intercept, and not a kernel handle at all (freed via FreeSid, not CloseHandle) — SafeHandle typing doesn't apply here; DangerousAddRef/Release above is the real protection (T-F138)
                    },
                };

                // Passing the folder's existing DACL as OldAcl merges this grant in — it does not
                // replace owner/SYSTEM/Administrators entries, which the previous naive draft of
                // this method would have wiped out (see this method's own doc comment).
                uint setEntriesResult = NativeMethods.SetEntriesInAclW(1, ref explicitAccess, existingAcl, out newAcl);
                if (setEntriesResult != 0)
                    throw new InvalidOperationException($"SetEntriesInAclW('{path}') failed (Win32 error {setEntriesResult}).");
            }
            finally
            {
                if (sidRefAdded)
                    sid.DangerousRelease();
            }

            try
            {
                if (sharedParent)
                {
                    if (!AclBytesEqual(existingAcl, newAcl))
                        SetDaclWithoutPropagation(path, newAcl);
                    return;
                }

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

    // SetEntriesInAclW merges a GRANT into an existing identical ACE, so an unchanged result means
    // the grant is already there. ACL header: AclSize is the ushort at byte offset 2.
    private static bool AclBytesEqual(IntPtr a, IntPtr b)
    {
        if (a == IntPtr.Zero || b == IntPtr.Zero)
            return false;
        int sizeA = (ushort)Marshal.ReadInt16(a, 2);
        int sizeB = (ushort)Marshal.ReadInt16(b, 2);
        if (sizeA != sizeB)
            return false;
        byte[] bytesA = new byte[sizeA];
        byte[] bytesB = new byte[sizeB];
        Marshal.Copy(a, bytesA, 0, sizeA);
        Marshal.Copy(b, bytesB, 0, sizeB);
        return bytesA.AsSpan().SequenceEqual(bytesB);
    }

    private static void SetDaclWithoutPropagation(string path, IntPtr dacl)
    {
        const uint SECURITY_DESCRIPTOR_REVISION = 1;
        const uint DACL_SECURITY_INFORMATION = 0x00000004;
        const ushort SE_DACL_AUTO_INHERITED = 0x0400;
        const int SecurityDescriptorMinLength = 40; // sizeof(SECURITY_DESCRIPTOR) on 64-bit

        IntPtr sd = Marshal.AllocHGlobal(SecurityDescriptorMinLength);
        try
        {
            if (!NativeMethods.InitializeSecurityDescriptor(sd, SECURITY_DESCRIPTOR_REVISION)
                || !NativeMethods.SetSecurityDescriptorDacl(sd, true, dacl, false)
                || !NativeMethods.SetSecurityDescriptorControl(sd, SE_DACL_AUTO_INHERITED, SE_DACL_AUTO_INHERITED)
                || !NativeMethods.SetFileSecurityW(path, DACL_SECURITY_INFORMATION, sd))
            {
                throw new InvalidOperationException(
                    $"SetFileSecurityW('{path}') failed (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(sd);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRUSTEE_W // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
    {
        public IntPtr pMultipleTrustee;
        public int MultipleTrusteeOperation;
        public int TrusteeForm;
        public int TrusteeType;
        public IntPtr ptstrName; // holds a raw PSID when TrusteeForm = TRUSTEE_IS_SID
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXPLICIT_ACCESS_W // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
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

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeSecurityDescriptor(IntPtr pSecurityDescriptor, uint dwRevision);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetSecurityDescriptorDacl(
            IntPtr pSecurityDescriptor,
            [MarshalAs(UnmanagedType.Bool)] bool bDaclPresent,
            IntPtr pDacl,
            [MarshalAs(UnmanagedType.Bool)] bool bDaclDefaulted);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetSecurityDescriptorControl(
            IntPtr pSecurityDescriptor, ushort controlBitsOfInterest, ushort controlBitsToSet);

        // Obsolete per its docs in favor of SetNamedSecurityInfoW — deliberately used here for the
        // one property that makes it the right call: it never propagates to children (T-F195).
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetFileSecurityW(
            string lpFileName, uint securityInformation, IntPtr pSecurityDescriptor);
    }
}
