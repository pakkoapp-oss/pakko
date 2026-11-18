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
        const uint SddlRevision1 = 1;
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                OwnersOnly(), SddlRevision1, out IntPtr securityDescriptor, 0))
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

    // The user, SYSTEM and Administrators only, protected.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string OwnersOnly()
    {
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current user's SID cannot be read.");
        return $"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{user})";
    }

    /// <summary>
    /// T-F527: when the user owns the file or folder <paramref name="path"/> (the user, or the token's default
    /// owner), writes the DACL of a private folder (<see cref="CreatePrivateFolder"/>) to it, itself (a
    /// junction's own DACL, never its target's): the owner may write a DACL that lets it nothing else.
    /// False when another principal owns it, or its owner cannot be read (an OWNER RIGHTS entry took the
    /// owner's rights away); <see cref="IOException"/> when the write fails.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static bool GiveBackIfOwned(string path)
    {
        const uint DACL_SECURITY_INFORMATION = 0x00000004;
        const uint SddlRevision1 = 1;
        if (!OwnedByTheUser(path))
            return false;
        string ownersOnly;
        try
        {
            ownersOnly = OwnersOnly();
        }
        catch (InvalidOperationException ex)
        {
            throw new IOException(ex.Message, ex);
        }
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptorW(ownersOnly, SddlRevision1, out IntPtr securityDescriptor, 0))
            throw new IOException($"ConvertStringSecurityDescriptorToSecurityDescriptorW failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        try
        {
            byte[] descriptor = new byte[NativeMethods.GetSecurityDescriptorLength(securityDescriptor)];
            Marshal.Copy(securityDescriptor, descriptor, 0, descriptor.Length);
            // Not through CreateFileW: it asks SYNCHRONIZE and FILE_READ_ATTRIBUTES too, which such a DACL
            // denies; SetFileSecurityW asks WRITE_DAC alone, and writes a junction's own DACL, never its
            // target's (measured).
            if (!NativeMethods.SetFileSecurityW(path, DACL_SECURITY_INFORMATION, descriptor))
                throw ErrorFor(Marshal.GetLastPInvokeError(), path);
            return true;
        }
        finally
        {
            NativeMethods.LocalFree(securityDescriptor);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool OwnedByTheUser(string path)
    {
        const int SE_FILE_OBJECT = 1;
        const uint OWNER_SECURITY_INFORMATION = 0x00000001;
        if (NativeMethods.GetNamedSecurityInfoW(path, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, out _, IntPtr.Zero, out IntPtr securityDescriptor) != 0)
            return false;
        try
        {
            byte[] bytes = new byte[NativeMethods.GetSecurityDescriptorLength(securityDescriptor)];
            Marshal.Copy(securityDescriptor, bytes, 0, bytes.Length);
            System.Security.Principal.SecurityIdentifier? owner = new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0).Owner;
            using var token = System.Security.Principal.WindowsIdentity.GetCurrent();
            return owner is not null && (owner == token.User || owner == token.Owner);
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
    /// T-F528: true when <paramref name="folder"/>'s DACL has an entry it passes to its children only
    /// (NO_PROPAGATE). False when the DACL cannot be read: a volume without permissions has none.
    /// </summary>
    public static bool GivesChildrenOnly(string folder)
    {
        const int SE_FILE_OBJECT = 1;
        const uint DACL_SECURITY_INFORMATION = 0x00000004;
        const uint SDDL_REVISION_1 = 1;

        if (!OperatingSystem.IsWindows()
            || NativeMethods.GetNamedSecurityInfoW(folder, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, out _, IntPtr.Zero, out IntPtr securityDescriptor) != 0)
            return false;
        try
        {
            if (!NativeMethods.ConvertSecurityDescriptorToStringSecurityDescriptorW(
                    securityDescriptor, SDDL_REVISION_1, DACL_SECURITY_INFORMATION, out IntPtr text, out _))
                return false;
            try
            {
                return HasNoPropagateEntry(Marshal.PtrToStringUni(text) ?? string.Empty);
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

    // "D:(A;OICINP;FR;;;WD)...": an entry's flags are its second field, two letters each.
    internal static bool HasNoPropagateEntry(string sddl) =>
        sddl.Split('(').Skip(1).Select(entry => entry.Split(';')).Any(fields => fields.Length >= 2 && HasFlag(fields[1], "NP"));

    private static bool HasFlag(string flags, string flag)
    {
        for (int i = 0; i + flag.Length <= flags.Length; i += 2)
        {
            if (string.CompareOrdinal(flags, i, flag, 0, flag.Length) == 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// T-F528: drops <paramref name="path"/>'s own entries and has it inherit from its parent again;
    /// for a folder, Windows writes the inheritance of everything below it too. Only for a file or
    /// folder Pakko made, whose entries are all inherited. Throws <see cref="IOException"/> when the
    /// DACL cannot be written.
    /// </summary>
    public static void InheritOnly(string path) => InheritOnly(path, parentGivesFilesNothing: false);

    /// <summary>
    /// T-F555: <see cref="InheritOnly(string)"/> for a file whose folder passes nothing to its files: the
    /// file inherits nothing, so it takes the entries a file made there gets - the creator's default
    /// DACL (measured: SYSTEM, the logon session, the user) - never an empty DACL no one can open.
    /// </summary>
    public static void InheritOnly(string path, bool parentGivesFilesNothing)
    {
        const int SE_FILE_OBJECT = 1;
        const uint DACL_SECURITY_INFORMATION = 0x00000004;
        const uint UNPROTECTED_DACL_SECURITY_INFORMATION = 0x20000000;
        const int AclHeaderLength = 8;
        const byte AclRevision = 2;

        byte[]? defaultDacl = parentGivesFilesNothing ? TokenDefaultDacl() : null;
        IntPtr acl = Marshal.AllocHGlobal(defaultDacl?.Length ?? AclHeaderLength);
        try
        {
            if (defaultDacl is not null)
            {
                Marshal.Copy(defaultDacl, 0, acl, defaultDacl.Length);
            }
            else
            {
                Marshal.WriteInt64(acl, 0);
                Marshal.WriteByte(acl, 0, AclRevision);
                Marshal.WriteInt16(acl, 2, AclHeaderLength);
            }
            uint result = NativeMethods.SetNamedSecurityInfoW(
                path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION | UNPROTECTED_DACL_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, acl, IntPtr.Zero);
            if (result != 0)
                throw new IOException($"SetNamedSecurityInfoW('{path}') failed (Win32 error {result}).");
        }
        finally
        {
            Marshal.FreeHGlobal(acl);
        }
    }

    // T-F555: the process token's default DACL, the one Windows gives an object whose parent passes it
    // nothing, as a self-contained ACL. Throws IOException when it cannot be read, or the token has none.
    private static byte[] TokenDefaultDacl()
    {
        const uint TokenQuery = 0x0008;
        const int TokenDefaultDaclClass = 6;
        const int AclSizeOffset = 2;

        if (!NativeMethods.OpenProcessToken(new IntPtr(-1), TokenQuery, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token))
            throw new IOException($"OpenProcessToken failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        using (token)
        {
            _ = NativeMethods.GetTokenInformation(token, TokenDefaultDaclClass, IntPtr.Zero, 0, out uint needed);
            if (needed == 0)
                throw new IOException($"GetTokenInformation(TokenDefaultDacl) failed (Win32 error {Marshal.GetLastPInvokeError()}).");
            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!NativeMethods.GetTokenInformation(token, TokenDefaultDaclClass, buffer, needed, out _))
                    throw new IOException($"GetTokenInformation(TokenDefaultDacl) failed (Win32 error {Marshal.GetLastPInvokeError()}).");
                IntPtr dacl = Marshal.ReadIntPtr(buffer);
                if (dacl == IntPtr.Zero)
                    throw new IOException("The process token has no default DACL.");
                byte[] bytes = new byte[(ushort)Marshal.ReadInt16(dacl, AclSizeOffset)];
                Marshal.Copy(dacl, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>
    /// T-F532: the entries <paramref name="folder"/> passes down to a file made in it, in their order, each
    /// without its flags (a file's copy is only "inherited"), as one string to compare. Each entry is read
    /// as an entry, not as SDDL text: a conditional entry holds parentheses of its own. Null when the DACL
    /// cannot be read: a volume without permissions has none.
    /// </summary>
    public static string? FileInheritableEntries(string folder)
    {
        const int SE_FILE_OBJECT = 1;
        const uint DACL_SECURITY_INFORMATION = 0x00000004;

        if (!OperatingSystem.IsWindows()
            || NativeMethods.GetNamedSecurityInfoW(folder, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, out _, IntPtr.Zero, out IntPtr securityDescriptor) != 0)
            return null;
        try
        {
            byte[] bytes = new byte[NativeMethods.GetSecurityDescriptorLength(securityDescriptor)];
            Marshal.Copy(securityDescriptor, bytes, 0, bytes.Length);
            return PassedToFiles(new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0).DiscretionaryAcl);
        }
        finally
        {
            NativeMethods.LocalFree(securityDescriptor);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string PassedToFiles(System.Security.AccessControl.RawAcl? dacl)
    {
        var text = new System.Text.StringBuilder();
        foreach (System.Security.AccessControl.GenericAce ace in dacl ?? new System.Security.AccessControl.RawAcl(2, 0))
        {
            if ((ace.AceFlags & System.Security.AccessControl.AceFlags.ObjectInherit) == 0)
                continue;
            System.Security.AccessControl.GenericAce copy = ace.Copy();
            copy.AceFlags = System.Security.AccessControl.AceFlags.None;
            byte[] form = new byte[copy.BinaryLength];
            copy.GetBinaryForm(form, 0);
            text.Append(Convert.ToHexString(form)).Append(';');
        }
        return text.ToString();
    }

    /// <summary>
    /// T-F534: gives the folder <paramref name="to"/> the DACL of the folder <paramref name="from"/>, which
    /// must be a folder made in the same parent, so that <paramref name="to"/> carries what a folder made
    /// there carries, its auto-inherited flag included. Written to the name itself (a link is not
    /// followed) by the low-level call, which never walks what is below it: what a folder passes down is
    /// the same either way. Throws <see cref="IOException"/> when a DACL cannot be read or written.
    /// T-F555: <paramref name="from"/> is read by its name, which someone with Modify on the parent could
    /// have given a folder of their own; the folder opened must be owned by the owner this process gives
    /// what it makes, checked through the handle the DACL is read from, or nothing is written.
    /// </summary>
    public static void CopyFolderDacl(string from, string to)
    {
        if (!OperatingSystem.IsWindows())
            throw new IOException("A folder's DACL is copied on Windows only.");
        CopyFolderDacl(from, to, System.Security.Principal.WindowsIdentity.GetCurrent().Owner
            ?? throw new IOException("The process token names no owner for what it creates."));
    }

    internal static void CopyFolderDacl(string from, string to, System.Security.Principal.SecurityIdentifier expectedOwner)
    {
        if (!OperatingSystem.IsWindows())
            throw new IOException("A folder's DACL is copied on Windows only.");
        const uint OWNER_SECURITY_INFORMATION = 0x00000001;
        const uint DACL_SECURITY_INFORMATION = 0x00000004;
        const uint ReadControl = 0x00020000;
        const uint WriteDac = 0x00040000;
        // SE_DACL_AUTO_INHERIT_REQ: without it the low-level write drops the auto-inherited flag
        // (measured), and the next change of the parent's entries would treat this folder's as its own.
        const ushort DaclAutoInheritRequest = 0x0100;
        const uint SecurityDescriptorMinLength = 20;
        const uint OwnerAndDacl = OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION;

        byte[] descriptor;
        using (Microsoft.Win32.SafeHandles.SafeFileHandle source = OpenFolderItself(from, ReadControl))
        {
            _ = NativeMethods.GetKernelObjectSecurity(source, OwnerAndDacl, null, 0, out uint needed);
            descriptor = new byte[needed];
            if (needed < SecurityDescriptorMinLength || !NativeMethods.GetKernelObjectSecurity(source, OwnerAndDacl, descriptor, needed, out _))
                throw ErrorFor(Marshal.GetLastPInvokeError(), from);
        }
        System.Security.Principal.SecurityIdentifier? owner = new System.Security.AccessControl.RawSecurityDescriptor(descriptor, 0).Owner;
        if (owner != expectedOwner)
            throw new IOException($"'{from}' is owned by {owner?.Value ?? "no one"}, not by {expectedOwner.Value}: not the folder Pakko made.");
        // A self-relative descriptor: Revision, Sbz1, then Control (little-endian).
        ushort control = (ushort)(descriptor[2] | (descriptor[3] << 8) | DaclAutoInheritRequest);
        descriptor[2] = (byte)control;
        descriptor[3] = (byte)(control >> 8);
        using Microsoft.Win32.SafeHandles.SafeFileHandle target = OpenFolderItself(to, WriteDac);
        if (!NativeMethods.SetKernelObjectSecurity(target, DACL_SECURITY_INFORMATION, descriptor))
            throw ErrorFor(Marshal.GetLastPInvokeError(), to);
    }

    private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenFolderItself(string path, uint access)
    {
        const uint FileShareAll = 7;
        const uint OpenExisting = 3;
        const uint BackupSemantics = 0x02000000;
        const uint OpenReparsePoint = 0x00200000;
        Microsoft.Win32.SafeHandles.SafeFileHandle handle = NativeMethods.CreateFileW(
            path, access, FileShareAll, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        if (!handle.IsInvalid)
            return handle;
        int error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw ErrorFor(error, path);
    }

    private static IOException ErrorFor(int error, string path) =>
        new($"{Marshal.GetPInvokeErrorMessage(error)} : '{path}'", unchecked((int)0x80070000) | error);

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
    /// T-F474: true when the DACL of the file or folder open as <paramref name="file"/> (opened with
    /// READ_CONTROL) lets an AppContainer in or could: an entry for an AppContainer or capability SID
    /// (S-1-15-2-*, S-1-15-3-*, ALL APPLICATION PACKAGES included), conditional ones too, an entry this
    /// parser does not know, or no DACL at all. True too when the DACL cannot be read, so what cannot be
    /// shown sealed is never taken out. Each entry's SID is read, not its SDDL text (batch 5a's review:
    /// a substring missed capability SIDs and conditional entries).
    /// </summary>
    public static bool HasAppContainerEntry(Microsoft.Win32.SafeHandles.SafeFileHandle file)
    {
        const int SE_FILE_OBJECT = 1;
        const uint DACL_SECURITY_INFORMATION = 0x00000004;

        if (!OperatingSystem.IsWindows()
            || NativeMethods.GetSecurityInfo(file, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, out _, IntPtr.Zero, out IntPtr securityDescriptor) != 0)
            return true;
        try
        {
            uint length = NativeMethods.GetSecurityDescriptorLength(securityDescriptor);
            byte[] bytes = new byte[length];
            Marshal.Copy(securityDescriptor, bytes, 0, bytes.Length);
            System.Security.AccessControl.RawAcl? dacl = new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0).DiscretionaryAcl;
            return dacl is null || dacl.Cast<System.Security.AccessControl.GenericAce>().Any(LetsAnAppContainerIn);
        }
        finally
        {
            NativeMethods.LocalFree(securityDescriptor);
        }
    }

    // An entry this parser does not know counts too: what cannot be shown sealed is never taken out.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool LetsAnAppContainerIn(System.Security.AccessControl.GenericAce ace) =>
        ace is not System.Security.AccessControl.KnownAce known
        || known.SecurityIdentifier.Value.StartsWith("S-1-15-2-", StringComparison.Ordinal)
        || known.SecurityIdentifier.Value.StartsWith("S-1-15-3-", StringComparison.Ordinal);

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

        [LibraryImport("advapi32.dll")]
        public static partial uint GetSecurityDescriptorLength(IntPtr securityDescriptor);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle tokenHandle);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetTokenInformation(
            Microsoft.Win32.SafeHandles.SafeAccessTokenHandle tokenHandle, int tokenInformationClass, IntPtr tokenInformation, uint length, out uint returnLength);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        public static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
            string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetKernelObjectSecurity(
            Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint securityInformation, [Out] byte[]? securityDescriptor, uint length, out uint lengthNeeded);

        [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetFileSecurityW(string fileName, uint securityInformation, [In] byte[] securityDescriptor);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetKernelObjectSecurity(
            Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint securityInformation, [In] byte[] securityDescriptor);
    }
}
