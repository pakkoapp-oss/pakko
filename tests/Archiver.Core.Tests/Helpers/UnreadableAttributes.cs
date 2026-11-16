using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Archiver.Core.Tests.Helpers;

/// <summary>
/// T-F522: a folder whose attributes the current user cannot read. A Deny ACE for ReadAttributes on
/// the folder alone is not enough - Windows then reads them from its parent's listing - so the parent
/// is made unlistable too (measured 2026-11-16: only both together fail File.GetAttributes with
/// access denied). Both are restored on Dispose, before the surrounding TempDirectory deletes the tree.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UnreadableAttributes : IDisposable
{
    private readonly DirectoryInfo _folder;
    private readonly FileSystemAccessRule _rule;
    private readonly DeniedFolder _parent;

    private UnreadableAttributes(string path)
    {
        _folder = new DirectoryInfo(path);
        _rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ReadAttributes,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);
        DirectorySecurity security = _folder.GetAccessControl();
        security.AddAccessRule(_rule);
        _folder.SetAccessControl(security);
        _parent = new DeniedFolder(_folder.Parent!.FullName);
    }

    public static UnreadableAttributes Of(string path) => new(path);

    public void Dispose()
    {
        _parent.Dispose();
        DirectorySecurity security = _folder.GetAccessControl();
        security.RemoveAccessRule(_rule);
        _folder.SetAccessControl(security);
    }
}
