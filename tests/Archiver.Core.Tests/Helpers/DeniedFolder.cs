using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Archiver.Core.Tests.Helpers;

/// <summary>
/// T-F236: makes a folder unlistable for the current user (a Deny ACE for ListDirectory on the
/// folder itself) and restores it on Dispose — the owner keeps WRITE_DAC, so the restore always
/// works, and it must run before the surrounding TempDirectory deletes the tree.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DeniedFolder : IDisposable
{
    private readonly DirectoryInfo _folder;
    private readonly FileSystemAccessRule _rule;

    public DeniedFolder(string path)
    {
        _folder = new DirectoryInfo(path);
        _rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);
        DirectorySecurity security = _folder.GetAccessControl();
        security.AddAccessRule(_rule);
        _folder.SetAccessControl(security);
    }

    public void Dispose()
    {
        DirectorySecurity security = _folder.GetAccessControl();
        security.RemoveAccessRule(_rule);
        _folder.SetAccessControl(security);
    }
}
