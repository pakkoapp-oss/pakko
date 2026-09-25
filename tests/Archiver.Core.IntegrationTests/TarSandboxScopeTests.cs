using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Step 6 of T-F52's build order: end-to-end proof that TarSandboxScope ties profile + ACL +
/// staging + Job Object together correctly — the same shape TarSandboxedService will use for
/// both the pre-scan and extraction within one scope. The quarantine is rooted under
/// %TEMP%\PakkoTarSandbox\, not next to the (here, arbitrary TempDirectory-based) destination —
/// see DECISIONS.md's T-F52 entry for why "same disk as destination" was dropped after an
/// AppContainer ancestor-traversal failure was found empirically.
/// </summary>
[Collection("TarSandbox")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class TarSandboxScopeTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task RunAsync_PreScanThenExtractionWithinOneScope_BothSucceed()
    {
        string archivePath = Path.Combine(_temp.Path, "fixture.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "scope test content")]);

        using var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, CancellationToken.None);

        var (preScanExit, preScanStdOut, preScanStdErr) = await scope.ListAsync(verbose: false, CancellationToken.None);
        preScanExit.Should().Be(0, because: preScanStdErr);
        preScanStdOut.Should().Contain("a.txt");

        var (extractExit, _, extractStdErr) = await scope.ExtractAsync(null, CancellationToken.None);
        extractExit.Should().Be(0, because: extractStdErr);

        File.ReadAllText(Path.Combine(scope.OutputDirectory!, "a.txt")).Should().Be("scope test content");
    }

    // Found 2026-09-24: every full test run left one empty "<guid>\in\" under %TEMP%\PakkoTarSandbox
    // — CreateAsync created the quarantine folders, then staging threw (here: the archive is gone),
    // and nothing owned the half-built folder yet, since the scope object is only constructed at
    // the very end. Any setup failure must clean up what setup already created.
    [Fact]
    public async Task CreateAsync_StagingFails_LeavesNoQuarantineFolderBehind()
    {
        string parent = Path.Combine(Path.GetTempPath(), "PakkoTarSandbox");
        Directory.CreateDirectory(parent);
        var before = Directory.GetDirectories(parent).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string missingArchive = Path.Combine(_temp.Path, "does-not-exist.tar");

        Func<Task> act = () => TarSandboxScope.CreateAsync(missingArchive, needsOutputDir: true, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        Directory.GetDirectories(parent).Where(d => !before.Contains(d)).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_NeedsOutputDirFalse_NoOutFolderCreated()
    {
        string archivePath = Path.Combine(_temp.Path, "fixture.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "listing only")]);

        using var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, CancellationToken.None);

        scope.OutputDirectory.Should().BeNull();
        Directory.Exists(Path.Combine(scope.QuarantineRoot, "out")).Should().BeFalse();

        var (exitCode, stdOut, stdErr) = await scope.ListAsync(verbose: false, CancellationToken.None);
        exitCode.Should().Be(0, because: stdErr);
        stdOut.Should().Contain("a.txt");
    }

    [Fact]
    public async Task Dispose_DeletesQuarantineDirectoryButNotAppContainerProfile()
    {
        string archivePath = Path.Combine(_temp.Path, "fixture.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "dispose test")]);

        var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, CancellationToken.None);
        string quarantineRoot = scope.QuarantineRoot;
        Directory.Exists(quarantineRoot).Should().BeTrue();

        scope.Dispose();

        Directory.Exists(quarantineRoot).Should().BeFalse();

        // The shared production profile must still exist and be reusable — Dispose() must never
        // delete it (see DECISIONS.md's T-F52 follow-up entry: create-once-reuse-forever).
        var profile = new AppContainerProfile(AppContainerProfile.ProductionProfileName);
        Action reuseProfile = () => profile.EnsureExists();
        reuseProfile.Should().NotThrow();
    }

    // T-F233: hardlink staging made quarantine\in\ a second name for the user's own file, and the
    // per-file grant then rewrote that file's DACL — an AppContainer ACE added, the ACEs it
    // inherited from its real folder recomputed from quarantine\in\ and lost. Nothing about the
    // original's security descriptor may change, for listing or extraction.
    [Fact]
    public async Task Scope_ListAndExtract_OriginalSecurityDescriptorUnchanged()
    {
        string folder = Path.Combine(_temp.Path, "shared");
        Directory.CreateDirectory(folder);
        var folderSecurity = new DirectoryInfo(folder).GetAccessControl();
        folderSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier("S-1-5-32-545"),
            System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.InheritanceFlags.ObjectInherit | System.Security.AccessControl.InheritanceFlags.ContainerInherit,
            System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(folder).SetAccessControl(folderSecurity);

        string archivePath = Path.Combine(folder, "b.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "acl")]);
        string before = Sddl(archivePath);

        using (var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, CancellationToken.None))
        {
            var (listExit, _, listErr) = await scope.ListAsync(verbose: false, CancellationToken.None);
            listExit.Should().Be(0, because: listErr);
            var (extractExit, _, extractErr) = await scope.ExtractAsync(null, CancellationToken.None);
            extractExit.Should().Be(0, because: extractErr);
            Sddl(archivePath).Should().Be(before, "the scope must never touch the user's file");
        }

        Sddl(archivePath).Should().Be(before);
    }

    // T-F233 second failure: an archive the user may read but not re-ACL (an OWNER RIGHTS ACE
    // takes the owner's implicit WRITE_DAC away) could not be opened at all.
    [Fact]
    public async Task Scope_ArchiveWithoutWriteDac_Opens()
    {
        string archivePath = Path.Combine(_temp.Path, "c.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "no write dac")]);
        var security = new System.Security.AccessControl.FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier("S-1-3-4"),
            System.Security.AccessControl.FileSystemRights.ReadAndExecute | System.Security.AccessControl.FileSystemRights.WriteAttributes,
            System.Security.AccessControl.AccessControlType.Allow));
        new FileInfo(archivePath).SetAccessControl(security);

        using var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, CancellationToken.None);
        var (exitCode, stdOut, stdErr) = await scope.ListAsync(verbose: false, CancellationToken.None);

        exitCode.Should().Be(0, because: stdErr);
        stdOut.Should().Contain("a.txt");
    }

    // T-F248: a read-only archive's hardlink could not be deleted by the recursive cleanup, so
    // every operation left one more link to the user's file in %TEMP%.
    [Fact]
    public async Task Scope_ReadOnlyArchive_NoLinkLeftAndAttributeKept()
    {
        string archivePath = Path.Combine(_temp.Path, "r.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "read only")]);
        File.SetAttributes(archivePath, FileAttributes.ReadOnly);
        try
        {
            string quarantineRoot;
            using (var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, CancellationToken.None))
            {
                quarantineRoot = scope.QuarantineRoot;
                var (exitCode, _, stdErr) = await scope.ListAsync(verbose: false, CancellationToken.None);
                exitCode.Should().Be(0, because: stdErr);
            }

            Directory.Exists(quarantineRoot).Should().BeFalse();
            LinkCount(archivePath).Should().Be(1);
            File.GetAttributes(archivePath).Should().HaveFlag(FileAttributes.ReadOnly);
        }
        finally
        {
            File.SetAttributes(archivePath, FileAttributes.Normal);
        }
    }

    // T-F233: the pre-scan's verdict is only valid for the bytes extraction reads — the archive
    // cannot be changed while the scope is open.
    [Fact]
    public async Task Scope_Open_ArchiveCannotBeWritten()
    {
        string archivePath = Path.Combine(_temp.Path, "w.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "locked")]);

        using var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, CancellationToken.None);

        Action write = () => new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite).Dispose();
        write.Should().Throw<IOException>();
    }

    // Misuse: an archive another program still has open for writing (a download in progress) is
    // refused with a clear message instead of being read while it changes.
    [Fact]
    public async Task CreateAsync_ArchiveOpenForWriting_RefusedAsInUse()
    {
        string archivePath = Path.Combine(_temp.Path, "busy.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "busy")]);
        using var writer = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Func<Task> act = () => TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, CancellationToken.None);

        (await act.Should().ThrowAsync<IOException>()).Which.Message.Should().Contain("in use");
    }

    private static string Sddl(string path) =>
        new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(
            System.Security.AccessControl.AccessControlSections.Access | System.Security.AccessControl.AccessControlSections.Owner);

    private static uint LinkCount(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return GetFileInformationByHandle(handle, out var info) ? info.NumberOfLinks : 0;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        // FILETIME is two DWORDs (4-byte aligned) — a long here would add padding.
        public uint FileAttributes;
        public uint CreationTimeLow, CreationTimeHigh;
        public uint LastAccessTimeLow, LastAccessTimeHigh;
        public uint LastWriteTimeLow, LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out ByHandleFileInformation info);

    [Fact]
    public async Task RunAsync_TwoConcurrentScopes_BothSucceedWithDistinctQuarantineRoots()
    {
        // T-F175 (test-coverage audit): the shared AppContainer profile/ACL is deliberately
        // create-once-reuse-forever (see Dispose_DeletesQuarantineDirectoryButNotAppContainerProfile
        // above and DECISIONS.md's T-F52 entry), and each CreateAsync mints its own
        // Guid.NewGuid()-based quarantineRoot — this proves two concurrent scopes genuinely don't
        // cross-contaminate each other's quarantine directory or content under real parallel use
        // (e.g. two extractions started back-to-back), not just that the shared profile is inert.
        string archivePathA = Path.Combine(_temp.Path, "fixture-a.tar");
        string archivePathB = Path.Combine(_temp.Path, "fixture-b.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePathA, "-cf", [("a.txt", "scope A content")]);
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePathB, "-cf", [("b.txt", "scope B content")]);

        var scopeATask = TarSandboxScope.CreateAsync(archivePathA, needsOutputDir: true, CancellationToken.None);
        var scopeBTask = TarSandboxScope.CreateAsync(archivePathB, needsOutputDir: true, CancellationToken.None);
        await Task.WhenAll(scopeATask, scopeBTask);
        using var scopeA = await scopeATask;
        using var scopeB = await scopeBTask;

        scopeA.QuarantineRoot.Should().NotBe(scopeB.QuarantineRoot);

        var extractATask = scopeA.ExtractAsync(null, CancellationToken.None);
        var extractBTask = scopeB.ExtractAsync(null, CancellationToken.None);
        await Task.WhenAll(extractATask, extractBTask);
        var (exitA, _, stdErrA) = await extractATask;
        var (exitB, _, stdErrB) = await extractBTask;

        exitA.Should().Be(0, because: stdErrA);
        exitB.Should().Be(0, because: stdErrB);
        File.ReadAllText(Path.Combine(scopeA.OutputDirectory!, "a.txt")).Should().Be("scope A content");
        File.ReadAllText(Path.Combine(scopeB.OutputDirectory!, "b.txt")).Should().Be("scope B content");
        File.Exists(Path.Combine(scopeA.OutputDirectory!, "b.txt")).Should().BeFalse();
        File.Exists(Path.Combine(scopeB.OutputDirectory!, "a.txt")).Should().BeFalse();
    }
}
