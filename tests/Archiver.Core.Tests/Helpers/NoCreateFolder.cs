using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Archiver.Core.Tests.Helpers;

/// <summary>Denies creating files (or the given rights) in a folder for the current user; restored on Dispose.</summary>
[SupportedOSPlatform("windows")]
internal sealed class NoCreateFolder : IDisposable
{
    private readonly DirectoryInfo _folder;
    private readonly FileSystemAccessRule _rule;

    public NoCreateFolder(string path, FileSystemRights denied = FileSystemRights.CreateFiles)
    {
        _folder = new DirectoryInfo(path);
        _rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, denied,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
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
