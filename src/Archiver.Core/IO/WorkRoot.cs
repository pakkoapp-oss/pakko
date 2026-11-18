using System.Runtime.InteropServices;
using Archiver.Core.Services.Sandbox;

namespace Archiver.Core.IO;

/// <summary>
/// T-F528: the one owner of where an operation's work folders go and how they are made. An operation
/// gets one hidden root, "<c>.pakko-w-</c>&lt;owner tag&gt;-&lt;guid&gt;", in the nearest folder of its
/// destination that exists (a folder not there yet is never created for work that may be refused; a
/// folder that does not exist yet cannot be a mount point, so the root shares the destination's volume
/// and the commit stays a rename). The root is made on the first role asked of it, so an operation
/// that needs none leaves nothing. Each role is a new folder, "&lt;role&gt;-&lt;guid&gt;", made by a
/// call that fails on any name already there (a folder, a file or a planted link). The root goes with
/// <see cref="Dispose"/>, each link in it on its own (<see cref="TempOwner.TryRemove"/>). Making a root
/// sweeps the roots, and the staging folders of older versions, that killed runs left there.
/// The quarantine of a sandboxed extraction is not a role: it stays a folder of its own with a
/// protected DACL (<see cref="QuarantineLocation"/>), since this root inherits the destination's, and
/// whoever may change that folder could rename the root. Public for the CLI's <c>-si</c> (T-F525).
/// </summary>
public sealed partial class WorkRoot : IDisposable
{
    /// <summary>The prefix of a root's name.</summary>
    public const string Prefix = ".pakko-w-";

    // The roots, and what older versions left next to a destination: extraction staging, creation's
    // temporary file, chunk folder and 7z writer folder (T-F263, T-F312, T-F342). One listing for all.
    private static readonly (string Prefix, string Suffix)[] SweptNames =
        [(Prefix, ""), (".pakko-x-", ""), (".pakko-a-", ".tmp"), (".pakko-tmp-", ""), (".pakko-7z-", "")];

    private readonly string _destination;
    private readonly Lock _gate = new();
    private string? _root;

    private WorkRoot(string destination) => _destination = destination;

    /// <summary>The work root of an operation whose output goes into <paramref name="destination"/>
    /// (which need not exist yet). Nothing is created before the first role.</summary>
    public static WorkRoot Near(string destination) => new(destination);

    /// <summary>A new folder for <paramref name="role"/> in the root; files made in it inherit the
    /// destination folder's permissions. Thread-safe.</summary>
    public string Folder(string role)
    {
        string path = Path.Combine(EnsureRoot(), $"{role}-{Guid.NewGuid():N}");
        CreateNewFolder(path);
        return path;
    }

    /// <summary>A new file path for <paramref name="role"/> in the root; the file itself is not
    /// created. Thread-safe.</summary>
    public string NewFile(string role, string suffix) =>
        Path.Combine(EnsureRoot(), $"{role}-{Guid.NewGuid():N}{suffix}");

    private string EnsureRoot()
    {
        lock (_gate)
        {
            if (_root is not null)
                return _root;
            string parent = NearestExisting(_destination)
                ?? throw new DirectoryNotFoundException($"No folder of '{_destination}' exists.");
            TempOwner.SweepStale(parent, SweptNames, TempScope.Destination);
            string root = Path.Combine(parent, TempOwner.NewName(Prefix));
            CreateNewFolder(root);
            _root = root;
            new DirectoryInfo(root).Attributes |= FileAttributes.Hidden;
            return root;
        }
    }

    /// <summary>The nearest folder of <paramref name="destination"/> that exists, or null.</summary>
    internal static string? NearestExisting(string destination)
    {
        string? folder = Path.GetFullPath(destination);
        while (folder is not null && !Directory.Exists(folder))
            folder = Path.GetDirectoryName(folder);
        return folder;
    }

    /// <summary>Creates <paramref name="path"/> as a new folder; throws <see cref="IOException"/> when
    /// anything has that name, a link included, and <see cref="UnauthorizedAccessException"/> when the
    /// folder above may not be written.</summary>
    internal static void CreateNewFolder(string path)
    {
        string full = Path.GetFullPath(path);
        if (CreateDirectoryW(Archiver.Core.Services.InputPaths.Literal(full), IntPtr.Zero))
            return;
        const int ErrorAccessDenied = 5;
        int error = Marshal.GetLastPInvokeError();
        string message = $"{Marshal.GetPInvokeErrorMessage(error)} : '{full}'";
        if (error == ErrorAccessDenied)
            throw new UnauthorizedAccessException(message);
        throw new IOException(message, unchecked((int)0x80070000) | error);
    }

    /// <summary>
    /// After a file made in the root was renamed into place as <paramref name="moved"/> (a folder:
    /// <see cref="TakeParentEntriesOfFolder"/>, which needs no walk): gives
    /// it what one made there gets. A rename keeps permissions, and an entry its new parent gives its
    /// children only (NO_PROPAGATE) never reached one made a level deeper (measured, T-F528). Only then is
    /// the inheritance written again, which for a folder walks its whole tree (0.9 s for 20,000 files).
    /// Best-effort: without it, it keeps the rest of what the destination grants.
    /// </summary>
    internal static void TakeParentEntries(string moved)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(moved));
        try
        {
            if (parent is not null && QuarantineAcl.GivesChildrenOnly(parent))
                QuarantineAcl.InheritOnly(moved);
        }
        catch (IOException)
        {
            /* best-effort: the folder keeps every entry but the one-level one */
        }
    }

    /// <summary>
    /// T-F534: <see cref="TakeParentEntries"/> for a folder renamed from this root into place as
    /// <paramref name="moved"/>. The entry its parent gives its children only reaches the folder alone,
    /// and what the folder passes down equals what it got in the root, so only the folder itself is
    /// written: it takes the root's DACL, a folder made in that same parent (<see cref="QuarantineAcl.CopyFolderDacl(string, string)"/>),
    /// with no walk of its tree. A folder in another parent has its inheritance written again, as before.
    /// Best-effort, as <see cref="TakeParentEntries"/>.
    /// </summary>
    internal void TakeParentEntriesOfFolder(string moved)
    {
        string full = Path.GetFullPath(moved);
        string? parent = Path.GetDirectoryName(full);
        string? root;
        lock (_gate)
            root = _root;
        try
        {
            if (parent is null || !QuarantineAcl.GivesChildrenOnly(parent))
                return;
            if (root is not null && string.Equals(Path.GetDirectoryName(root), parent, StringComparison.OrdinalIgnoreCase))
                QuarantineAcl.CopyFolderDacl(Archiver.Core.Services.InputPaths.Literal(root), Archiver.Core.Services.InputPaths.Literal(full));
            else
                QuarantineAcl.InheritOnly(full);
        }
        catch (IOException)
        {
            /* best-effort: the folder keeps every entry but the one-level one */
        }
    }

    /// <summary>Removes the root and everything in it, best-effort: what is held stays for a later sweep.</summary>
    public void Dispose()
    {
        string? root;
        lock (_gate)
        {
            root = _root;
            _root = null;
        }
        if (root is not null)
            _ = TempOwner.TryRemove(root); // false: held, swept by the next root made here
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryW(string pathName, IntPtr securityAttributes);
}
