using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F389: tar.exe writes inside the AppContainer, so a file it creates carries the quarantine
/// folder's inherited entries (the sandbox SID) and a Low integrity label; a rename out of the
/// quarantine keeps that security descriptor. An extracted file must have the permissions of the
/// folder it lands in, as a ZIP entry does.
/// </summary>
[Collection("TarSandbox")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed partial class TarSandboxedServiceExtractPermissionsTests : IDisposable
{
    private const string AppContainerSidPrefix = "S-1-15-2-";
    private const string MandatoryLabelAce = "(ML;";
    private const string InheritedAuthenticatedUsersAce = ";;;AU)";

    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Integration]
    public async Task ExtractAsync_File_HasTheDestinationFoldersPermissionsAndNothingFromTheSandbox()
    {
        string destDir = CreateFolderGrantingAuthenticatedUsers("shared");

        ArchiveResult result = await ExtractAsync(destDir);

        result.Success.Should().BeTrue();
        foreach (string file in new[] { Path.Combine(destDir, "a.txt"), Path.Combine(destDir, "sub", "b.txt") })
        {
            string sddl = ReadDaclAndLabel(file);
            sddl.Should().NotContain(AppContainerSidPrefix, $"{file} must not keep the sandbox's entry");
            sddl.Should().NotContain(MandatoryLabelAce, $"{file} must not keep the sandbox's integrity label");
            sddl.Should().Contain(InheritedAuthenticatedUsersAce, $"{file} must inherit from the folder it was extracted into");
        }
    }

    [Integration]
    public async Task ExtractAsync_FileReplacingAnExistingOne_HasTheDestinationFoldersPermissions()
    {
        string destDir = CreateFolderGrantingAuthenticatedUsers("shared");
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "old");

        ArchiveResult result = await ExtractAsync(destDir, ConflictBehavior.Overwrite);

        result.Success.Should().BeTrue();
        string file = Path.Combine(destDir, "a.txt");
        File.ReadAllText(file).Should().Be("hello");
        string sddl = ReadDaclAndLabel(file);
        sddl.Should().NotContain(AppContainerSidPrefix);
        sddl.Should().NotContain(MandatoryLabelAce);
        sddl.Should().Contain(InheritedAuthenticatedUsersAce);
    }

    [Fact]
    public void InheritFromParent_MissingFile_ThrowsIOException()
    {
        Action act = () => Archiver.Core.Services.Sandbox.QuarantineAcl.InheritFromParent(Path.Combine(_temp.Path, "missing.txt"));

        act.Should().Throw<IOException>();
    }

    [Fact]
    public void InheritFromParent_FileWithItsOwnEntry_KeepsOnlyWhatTheFolderGrants()
    {
        string folder = CreateFolderGrantingAuthenticatedUsers("shared");
        string file = Path.Combine(folder, "own.txt");
        File.WriteAllText(file, "x");
        var info = new FileInfo(file);
        FileSecurity security = info.GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        info.SetAccessControl(security);
        ReadDaclAndLabel(file).Should().NotContain(InheritedAuthenticatedUsersAce);

        Archiver.Core.Services.Sandbox.QuarantineAcl.InheritFromParent(file);

        string sddl = ReadDaclAndLabel(file);
        sddl.Should().Contain(InheritedAuthenticatedUsersAce);
        sddl.Should().NotContain("(A;;", "no explicit entry is left");
    }

    private Task<ArchiveResult> ExtractAsync(string destDir, ConflictBehavior onConflict = ConflictBehavior.Skip)
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("hello") },
            new TarBuilder.Entry { Name = "sub/b.txt", Content = Encoding.ASCII.GetBytes("world") },
        ]);

        return _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = onConflict,
        });
    }

    // An entry the temporary folder does not have, so "inherits from the destination" is visible.
    private string CreateFolderGrantingAuthenticatedUsers(string name)
    {
        var folder = new DirectoryInfo(Path.Combine(_temp.Path, name));
        folder.Create();
        DirectorySecurity security = folder.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        folder.SetAccessControl(security);
        return folder.FullName;
    }

    private static string ReadDaclAndLabel(string path)
    {
        const int SeFileObject = 1;
        const uint DaclAndLabel = 0x00000004 | 0x00000010;
        const uint SddlRevision1 = 1;

        uint result = GetNamedSecurityInfoW(path, SeFileObject, DaclAndLabel, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out IntPtr descriptor);
        result.Should().Be(0u, $"GetNamedSecurityInfoW('{path}')");
        try
        {
            ConvertSecurityDescriptorToStringSecurityDescriptorW(descriptor, SddlRevision1, DaclAndLabel, out IntPtr text, out _)
                .Should().BeTrue();
            try
            {
                return Marshal.PtrToStringUni(text)!;
            }
            finally
            {
                LocalFree(text);
            }
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetNamedSecurityInfoW(
        string objectName, int objectType, uint securityInfo,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr securityDescriptor);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSecurityDescriptorToStringSecurityDescriptorW(
        IntPtr securityDescriptor, uint revision, uint securityInfo, out IntPtr text, out uint length);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
