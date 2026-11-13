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
internal static partial class QuarantineAcl
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
    private const int GrantAccess = 1;
    private const int RevokeAccess = 4;

    public static void GrantReadExecute(string path, SafeSidHandle sid)
        => Grant(path, sid, FileGenericReadExecute, SubContainersAndObjectsInherit);

    public static void GrantModify(string path, SafeSidHandle sid)
        => Grant(path, sid, FileGenericModify, SubContainersAndObjectsInherit);

    public static void GrantTraverseOnly(string path, SafeSidHandle sid)
        => Grant(path, sid, FileTraverse, NoInheritance);

    /// <summary>
    /// T-F474: takes every entry of <paramref name="sid"/> off <paramref name="path"/>; Windows takes
    /// the ones its files and folders inherited off them too. A file whose own DACL names the SID keeps
    /// that entry: <see cref="HasAppContainerEntry"/> finds it.
    /// </summary>
    public static void Revoke(string path, SafeSidHandle sid)
        => Grant(path, sid, 0, NoInheritance, RevokeAccess);

    /// <summary>T-F196: for a per-scope quarantine root only (it holds nothing but that scope's
    /// own in\ and out\), never a shared or user-owned directory.</summary>
    public static void GrantTraverseListReadAttributes(string path, SafeSidHandle sid)
        => Grant(path, sid, FileTraverseListReadAttributes, NoInheritance);

    /// <summary>
    /// T-F524: creates a quarantine root next to a destination: a new folder (an existing name throws
    /// <see cref="IOException"/>, a folder the user may not write <see cref="UnauthorizedAccessException"/>),
    /// born with a protected DACL of the user, SYSTEM and Administrators only. Nothing comes from the
    /// destination's folder: on a drive root that is Authenticated Users Modify, so other accounts, or
    /// any AppContainer a folder lets in, could otherwise write into the quarantine. Created with its
    /// DACL in one call, so there is no moment when the folder carries the inherited entries.
    /// </summary>
    public static void CreatePrivateFolder(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current user's SID cannot be read.");
        const uint SddlRevision1 = 1;
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                $"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{user})", SddlRevision1, out IntPtr securityDescriptor, 0))
            throw new InvalidOperationException($"ConvertStringSecurityDescriptorToSecurityDescriptorW failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = securityDescriptor,
            };
            // A root next to a destination is a plain path under MAX_PATH (QuarantineLocation.RootFits).
            if (!NativeMethods.CreateDirectoryW(path, ref attributes))
            {
                const int ErrorAccessDenied = 5;
                int error = Marshal.GetLastPInvokeError();
                string message = $"{Marshal.GetPInvokeErrorMessage(error)} : '{path}'";
                if (error == ErrorAccessDenied)
                    throw new UnauthorizedAccessException(message);
                throw new IOException(message, unchecked((int)0x80070000) | error);
            }
        }
        finally
        {
            NativeMethods.LocalFree(securityDescriptor);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    /// <summary>
    /// T-F389: gives a file that tar.exe wrote inside the AppContainer the permissions of the
    /// folder it is in now. Such a file carries the quarantine folder's inherited entries (the
    /// sandbox SID among them) and a Low integrity label, and a rename keeps both. An empty,
    /// unprotected DACL leaves only what the current parent grants; an empty label list removes
    /// the label. Throws <see cref="IOException"/> when the file's security cannot be written.
    /// </summary>
    public static void InheritFromParent(string filePath)
    {
        const int SE_FILE_OBJECT = 1;
        const uint DACL_SECURITY_INFORMATION = 0x00000004;
        const uint LABEL_SECURITY_INFORMATION = 0x00000010;
        const uint UNPROTECTED_DACL_SECURITY_INFORMATION = 0x20000000;
        const int AclHeaderLength = 8;
        const byte AclRevision = 2;

        // ACL header with no entries: AclRevision, Sbz1, AclSize (ushort), AceCount (ushort), Sbz2 (ushort).
        IntPtr emptyAcl = Marshal.AllocHGlobal(AclHeaderLength);
        try
        {
            Marshal.WriteInt64(emptyAcl, 0);
            Marshal.WriteByte(emptyAcl, 0, AclRevision);
            Marshal.WriteInt16(emptyAcl, 2, AclHeaderLength);

            uint result = NativeMethods.SetNamedSecurityInfoW(
                filePath, SE_FILE_OBJECT,
                DACL_SECURITY_INFORMATION | UNPROTECTED_DACL_SECURITY_INFORMATION | LABEL_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, emptyAcl, emptyAcl);
            if (result != 0)
                throw new IOException($"SetNamedSecurityInfoW('{filePath}') failed (Win32 error {result}).");
        }
        finally
        {
            Marshal.FreeHGlobal(emptyAcl);
        }
    }

    /// <summary>
    /// T-F389: true when <paramref name="filePath"/> has an entry for an AppContainer SID or an
    /// integrity label - what a file created by the sandboxed tar.exe carries. False when the file's
    /// security cannot be read: a volume without permissions (FAT, exFAT) has none to carry, and a
    /// file that reached its volume by copy was created by Pakko, not by the sandbox.
    /// </summary>
    public static bool CarriesSandboxSecurity(string filePath)
    {
        const int SE_FILE_OBJECT = 1;
        const uint DaclAndLabel = 0x00000004 | 0x00000010;
        const uint SDDL_REVISION_1 = 1;
        const string AppContainerSidPrefix = "S-1-15-2-";
        const string MandatoryLabelAce = "(ML;";

        uint getResult = NativeMethods.GetNamedSecurityInfoW(
            filePath, SE_FILE_OBJECT, DaclAndLabel,
            IntPtr.Zero, IntPtr.Zero, out _, IntPtr.Zero, out IntPtr securityDescriptor);
        if (getResult != 0)
            return false;

        try
        {
            if (!NativeMethods.ConvertSecurityDescriptorToStringSecurityDescriptorW(
                    securityDescriptor, SDDL_REVISION_1, DaclAndLabel, out IntPtr text, out _))
                return false;

            try
            {
                string sddl = Marshal.PtrToStringUni(text) ?? string.Empty;
                return sddl.Contains(AppContainerSidPrefix, StringComparison.Ordinal)
                    || sddl.Contains(MandatoryLabelAce, StringComparison.Ordinal);
            }
            finally
            {
                NativeMethods.LocalFree(text);
            }
        }
        finally
        {
            NativeMethods.LocalFree(securityDescriptor);
        }
    }

    /// <summary>
    /// T-F474: true when the DACL of the file open as <paramref name="file"/> (opened with READ_CONTROL)
    /// lets any AppContainer in: an AppContainer SID or ALL APPLICATION PACKAGES. True too when the
    /// DACL cannot be read, so a file that cannot be shown sealed is never taken out.
    /// </summary>
    public static bool HasAppContainerEntry(Microsoft.Win32.SafeHandles.SafeFileHandle file)
    {
        const int SE_FILE_OBJECT = 1;
        const uint DACL_SECURITY_INFORMATION = 0x00000004;
        const uint SDDL_REVISION_1 = 1;
        const string AppContainerSidPrefix = "S-1-15-2-";
        const string AllApplicationPackages = ";AC)";

        if (NativeMethods.GetSecurityInfo(file, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, out _, IntPtr.Zero, out IntPtr securityDescriptor) != 0)
            return true;
        try
        {
            if (!NativeMethods.ConvertSecurityDescriptorToStringSecurityDescriptorW(
                    securityDescriptor, SDDL_REVISION_1, DACL_SECURITY_INFORMATION, out IntPtr text, out _))
                return true;
            try
            {
                string sddl = Marshal.PtrToStringUni(text) ?? string.Empty;
                return sddl.Contains(AppContainerSidPrefix, StringComparison.Ordinal)
                    || sddl.Contains(AllApplicationPackages, StringComparison.Ordinal);
            }
            finally
            {
                NativeMethods.LocalFree(text);
            }
        }
        finally
        {
            NativeMethods.LocalFree(securityDescriptor);
        }
    }

    private static void Grant(string path, SafeSidHandle sid, uint accessMask, int inheritance, int accessMode = GrantAccess)
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
                    grfAccessMode = accessMode, // GRANT_ACCESS, or REVOKE_ACCESS (T-F474)
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

    private static partial class NativeMethods
    {
        [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = false)]
        public static partial uint GetNamedSecurityInfoW(
            string pObjectName,
            int objectType,
            uint securityInfo,
            IntPtr ppsidOwner,
            IntPtr ppsidGroup,
            out IntPtr ppDacl,
            IntPtr ppSacl,
            out IntPtr ppSecurityDescriptor);

        [LibraryImport("advapi32.dll", SetLastError = false)]
        public static partial uint SetEntriesInAclW(
            int cCountOfExplicitEntries,
            ref EXPLICIT_ACCESS_W pListOfExplicitEntries,
            IntPtr oldAcl,
            out IntPtr newAcl);

        [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = false)]
        public static partial uint SetNamedSecurityInfoW(
            string pObjectName,
            int objectType,
            uint securityInfo,
            IntPtr psidOwner,
            IntPtr psidGroup,
            IntPtr dacl,
            IntPtr sacl);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ConvertSecurityDescriptorToStringSecurityDescriptorW(
            IntPtr securityDescriptor, uint requestedStringSDRevision, uint securityInformation,
            out IntPtr stringSecurityDescriptor, out uint stringSecurityDescriptorLen);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr LocalFree(IntPtr hMem);

        [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
            string stringSecurityDescriptor, uint stringSDRevision, out IntPtr securityDescriptor, IntPtr securityDescriptorSize);

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CreateDirectoryW(string pathName, ref SecurityAttributes securityAttributes);

        [LibraryImport("advapi32.dll", SetLastError = false)]
        public static partial uint GetSecurityInfo(
            Microsoft.Win32.SafeHandles.SafeFileHandle handle, int objectType, uint securityInfo,
            IntPtr ppsidOwner, IntPtr ppsidGroup, out IntPtr ppDacl, IntPtr ppSacl, out IntPtr ppSecurityDescriptor);
    }
}
