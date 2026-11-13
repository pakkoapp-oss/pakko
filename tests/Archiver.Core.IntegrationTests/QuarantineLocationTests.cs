using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Archiver.Core.IO;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F524: an extraction's quarantine goes next to its destination, in the nearest folder of it
/// that exists, on a fixed local volume that keeps permissions; its root is a new folder of the
/// user's own, cut off from what the destination's folder would pass down.
/// </summary>
[Collection("TarSandbox")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class QuarantineLocationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private string? _other;

    public void Dispose()
    {
        _temp.Dispose();
        if (_other is not null)
            Directory.Delete(_other, recursive: true);
    }

    // --- The decision ---

    [Theory]
    [InlineData(true, 0x8u, 3u, true)]   // a fixed volume that keeps permissions (NTFS, ReFS)
    [InlineData(true, 0x0u, 3u, false)]  // FAT or exFAT: no permissions to protect the quarantine
    [InlineData(true, 0x8u, 2u, false)]  // a removable drive: not measured
    [InlineData(true, 0x8u, 4u, false)]  // a network drive: the sandbox has no network
    [InlineData(false, 0x8u, 3u, false)] // no volume GUID path: a network folder
    public void Accepts_OnlyAFixedVolumeThatKeepsPermissions(bool hasVolume, uint flags, uint driveType, bool expected) =>
        QuarantineLocation.Accepts(hasVolume ? @"\\?\Volume{00000000-0000-0000-0000-000000000000}\" : null, flags, _ => driveType)
            .Should().Be(expected);

    [Fact]
    public void NearDestination_AFolderNotYetThere_IsItsNearestFolderThatIs()
    {
        string destination = Path.Combine(_temp.Path, "x", "y");

        Near(destination).Should().Be(_temp.Path);
        Directory.Exists(Path.Combine(_temp.Path, "x")).Should().BeFalse();
    }

    // Every sandboxed run gets the root as its working folder by its plain path: a \\?\ or \\.\ spelling
    // reached tar.exe as \\?\UNC\.\C:\... (found by TrimmedSourceNameTests).
    [Fact]
    public void NearDestination_ALongPathSpelling_IsThePlainFinalPath() =>
        Near(@"\\?\" + Path.Combine(_temp.Path, "x")).Should().Be(_temp.Path);

    // A plain path to "trail." opens "trail", here a folder of its own: the quarantine would land in
    // that other folder (T-F509).
    [Fact]
    public void NearDestination_AFolderWindowsTrims_IsNone()
    {
        string trimmed = @"\\?\" + Path.Combine(_temp.Path, "trail.");
        Directory.CreateDirectory(trimmed);
        Directory.CreateDirectory(Path.Combine(_temp.Path, "trail"));

        Near(trimmed + @"\x").Should().BeNull();
    }

    // A junction is judged by where it leads, and the quarantine goes there by its final path.
    [Fact]
    public void NearDestination_AJunctionToAnotherVolume_IsTheFolderItLeadsTo()
    {
        string? target = OtherFixedVolumeFolder();
        if (target is null)
            return; // one volume only
        try
        {
            string junction = Path.Combine(_temp.Path, "j");
            Directory.CreateSymbolicLink(junction, target);

            Near(Path.Combine(junction, "x")).Should().Be(target);
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public void NearDestination_ANetworkFolder_IsNone() =>
        Near(@"\\127.0.0.1\pakko-no-such-share\x").Should().BeNull();

    // --- The root ---

    // A drive root gives Authenticated Users Modify to everything below it; the quarantine takes none
    // of it, so neither another account nor another operation's sandbox can write into it.
    [Fact]
    public async Task Scope_NearAFolderThatLetsEveryoneIn_ItsRootTakesNothingFromIt()
    {
        string open = Path.Combine(_temp.Path, "open");
        Directory.CreateDirectory(open);
        DirectorySecurity openSecurity = new DirectoryInfo(open).GetAccessControl();
        openSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        openSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-15-2-1"),
            FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(open).SetAccessControl(openSecurity);
        string archive = Archive(("a.txt", "a"));

        using TarSandboxScope mine = await TarSandboxScope.CreateAsync(archive, needsOutputDir: true, CancellationToken.None, nearFolder: open);
        using TarSandboxScope other = await TarSandboxScope.CreateAsync(archive, needsOutputDir: true, CancellationToken.None);

        Path.GetDirectoryName(mine.QuarantineRoot).Should().Be(open);
        Path.GetFileName(mine.QuarantineRoot).Should().StartWith(QuarantineLocation.RootPrefix);
        new DirectoryInfo(mine.QuarantineRoot).Attributes.Should().HaveFlag(FileAttributes.Hidden);
        DirectorySecurity root = new DirectoryInfo(mine.QuarantineRoot).GetAccessControl();
        root.AreAccessRulesProtected.Should().BeTrue();
        root.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Select(r => r.IdentityReference.Value).Should().NotContain(["S-1-1-0", "S-1-15-2-1"]);
        string planted = Path.Combine(mine.OutputDirectory!, "planted.txt");
        (await CmdAsync(other, @"echo x> " + planted)).Should().NotBe(0, "another operation's sandbox is not let in");
        File.Exists(planted).Should().BeFalse();
        (await CmdAsync(mine, @"echo x> out\own.txt")).Should().Be(0, "the scope's own sandbox writes its out\\");
    }

    [Fact]
    public void CreatePrivateFolder_AnExistingName_Throws()
    {
        string folder = Path.Combine(_temp.Path, "there");
        Directory.CreateDirectory(folder);

        Action again = () => QuarantineAcl.CreatePrivateFolder(folder);

        again.Should().Throw<IOException>();
    }

    // A killed run's quarantine next to a destination holds the archive's plaintext; the next scope
    // there removes it, and leaves one whose run is still going.
    [Fact]
    public async Task Scope_NearAFolder_RemovesAKilledRunsQuarantine_KeepsALiveOnes()
    {
        string machine = TempOwner.CurrentTag.Split('-')[0];
        string dead = Path.Combine(_temp.Path, $"{QuarantineLocation.RootPrefix}{machine}-999999-1-{Guid.NewGuid():N}");
        string live = Path.Combine(_temp.Path, TempOwner.NewName(QuarantineLocation.RootPrefix));
        Directory.CreateDirectory(dead);
        Directory.CreateDirectory(live);

        using (await TarSandboxScope.CreateAsync(Archive(("a.txt", "a")), needsOutputDir: true, CancellationToken.None, nearFolder: _temp.Path))
        {
        }

        Directory.Exists(dead).Should().BeFalse();
        Directory.Exists(live).Should().BeTrue();
    }

    // --- Through the readers and the service ---

    [Theory]
    [MemberData(nameof(HelperReaderTests.Backends), MemberType = typeof(HelperReaderTests))]
    public async Task Reader_WithADestination_QuarantinesNextToIt_AndLeavesNothing(string backend)
    {
        string? folder = OtherFixedVolumeFolder();
        if (folder is null)
            return; // one volume only: the quarantine stays in %TEMP%
        try
        {
            string destination = Path.Combine(folder, "dest");
            string[] quarantined;
            using (ISandboxedArchiveReader reader = await ISandboxedArchiveReader.OpenAsync(
                       Archive(("a.txt", "a")), true, null, CancellationToken.None, backend == HelperReaderTests.Helper ? HelperSetupFor() : null,
                       destination: destination))
            {
                await reader.ScanAsync(CancellationToken.None);
                await reader.SelectForExtractAsync(null, null, CancellationToken.None);
                IQuarantinedEntries quarantine = await reader.ExtractAsync(null, [], CancellationToken.None);
                quarantined = [.. quarantine.Files().Select(f => f.Path)];
                // The helper's copy runs from %TEMP%, never from the user's folder.
                Directory.GetDirectories(folder, QuarantineLocation.RootPrefix + "*")
                    .Should().ContainSingle().Which.Should().Match(root => !Directory.Exists(Path.Combine(root, "bin")));
            }

            quarantined.Should().ContainSingle().Which.Should().StartWith(Path.Combine(folder, QuarantineLocation.RootPrefix));
            Directory.GetDirectories(folder, QuarantineLocation.RootPrefix + "*").Should().BeEmpty();
        }
        finally
        {
            if (folder is not null)
                Directory.Delete(folder, recursive: true);
        }
    }

    // A destination on the volume of %TEMP% (Documents, the Desktop) quarantines in %TEMP%, as before.
    [Theory]
    [MemberData(nameof(HelperReaderTests.Backends), MemberType = typeof(HelperReaderTests))]
    public async Task Reader_WithADestinationOnTheVolumeOfTemp_QuarantinesInTemp(string backend)
    {
        string destination = Path.Combine(_temp.Path, "dest");
        using ISandboxedArchiveReader reader = await ISandboxedArchiveReader.OpenAsync(
            Archive(("a.txt", "a")), true, null, CancellationToken.None, backend == HelperReaderTests.Helper ? HelperSetupFor() : null,
            destination: destination);
        await reader.ScanAsync(CancellationToken.None);
        await reader.SelectForExtractAsync(null, null, CancellationToken.None);
        IQuarantinedEntries quarantine = await reader.ExtractAsync(null, [], CancellationToken.None);

        quarantine.Files().Should().ContainSingle().Which.Path.Should().StartWith(Path.Combine(Path.GetTempPath(), "PakkoTarSandbox"));
        Directory.GetDirectories(_temp.Path, QuarantineLocation.RootPrefix + "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    // The service makes the destination's own parent folder for a refused archive too, as it did
    // before (measured with the quarantine in %TEMP%); the quarantine leaves nothing.
    [Theory]
    [MemberData(nameof(HelperReaderTests.Backends), MemberType = typeof(HelperReaderTests))]
    public async Task Extract_ARefusedArchive_IntoAFolderNotYetThere_LeavesNoQuarantine(string backend)
    {
        string archive = Path.Combine(_temp.Path, "escape.tar");
        TarBuilder.WriteTar(archive, [new TarBuilder.Entry { Name = "../escape.txt", Content = "x"u8.ToArray() }]);
        string destination = Path.Combine(ServiceFolder(), "x", "y");

        ArchiveResult result = await Service(backend).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = destination, Mode = ExtractMode.SingleFolder,
        });

        result.Errors.Should().NotBeEmpty();
        Directory.Exists(destination).Should().BeFalse();
        Directory.GetDirectories(ServiceFolder(), QuarantineLocation.RootPrefix + "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    // The quarantine sits in the destination while the archive is placed: in either mode, with a
    // separate folder, a dropped root, or a merge into what is there, nothing of it shows in the
    // result and nothing is left.
    [Theory]
    [InlineData(HelperReaderTests.TarExe, ExtractMode.SeparateFolders)]
    [InlineData(HelperReaderTests.Helper, ExtractMode.SeparateFolders)]
    [InlineData(HelperReaderTests.TarExe, ExtractMode.SingleFolder)]
    [InlineData(HelperReaderTests.Helper, ExtractMode.SingleFolder)]
    public async Task Extract_SeveralArchives_InEitherMode_LeaveOnlyTheirFiles(string backend, ExtractMode mode)
    {
        string destination = Path.Combine(ServiceFolder(), "dest");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "there.txt"), "there");
        string rooted = Path.Combine(_temp.Path, "rooted.tar");
        TarBuilder.WriteTar(rooted, [new TarBuilder.Entry { Name = "rooted/a.txt", Content = "a"u8.ToArray() }]);
        string flat = Path.Combine(_temp.Path, "flat.tar");
        TarBuilder.WriteTar(flat, [new TarBuilder.Entry { Name = "b.txt", Content = "b"u8.ToArray() }, new TarBuilder.Entry { Name = "c.txt", Content = "c"u8.ToArray() }]);

        ArchiveResult result = await Service(backend).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [rooted, flat], DestinationFolder = destination, Mode = mode, EliminateDuplicateRootFolder = true,
        });

        result.Errors.Should().BeEmpty();
        string[] files = [.. Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(destination, f)).Order(StringComparer.Ordinal)];
        files.Should().Equal(mode == ExtractMode.SeparateFolders
            ? [@"flat\b.txt", @"flat\c.txt", @"rooted\a.txt", "there.txt"]
            : ["a.txt", "b.txt", "c.txt", "there.txt"]);
        Directory.GetDirectories(ServiceFolder(), QuarantineLocation.RootPrefix + "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    // A folder the user may not create in fails the archive as it failed before, never as a sandbox failure.
    [Theory]
    [MemberData(nameof(HelperReaderTests.Backends), MemberType = typeof(HelperReaderTests))]
    public async Task Extract_IntoAFolderTheUserMayNotCreateIn_IsAnError_NotASandboxFailure(string backend)
    {
        string closed = Path.Combine(ServiceFolder(), "closed");
        Directory.CreateDirectory(closed);
        DirectorySecurity security = new DirectoryInfo(closed).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles, AccessControlType.Deny));
        new DirectoryInfo(closed).SetAccessControl(security);
        try
        {
            ArchiveResult result = await Service(backend).ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [Archive(("a.txt", "a"))], DestinationFolder = Path.Combine(closed, "dest"), Mode = ExtractMode.SingleFolder,
            });

            result.Errors.Should().ContainSingle().Which.Message.Should().NotContainEquivalentOf("sandbox");
        }
        finally
        {
            security.RemoveAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles, AccessControlType.Deny));
            new DirectoryInfo(closed).SetAccessControl(security);
        }
    }

    // The quarantine's root is every run's working folder, which CreateProcess caps below MAX_PATH:
    // a destination too deep for it quarantines in %TEMP%; one at the edge, next to itself. A deep
    // entry lands either way (both tar.exe and the helper see these paths).
    [Theory]
    [InlineData(HelperReaderTests.TarExe, false)]
    [InlineData(HelperReaderTests.Helper, false)]
    [InlineData(HelperReaderTests.TarExe, true)]
    [InlineData(HelperReaderTests.Helper, true)]
    public async Task Extract_ADeepEntry_IntoADeepDestination_Lands(string backend, bool atTheEdge)
    {
        string deepFolder = ServiceFolder();
        while (deepFolder.Length < 200)
            deepFolder = Path.Combine(deepFolder, new string('f', 20));
        if (atTheEdge)
        {
            deepFolder = ServiceFolder();
            int nameLength = TempOwner.NewName(QuarantineLocation.RootPrefix).Length;
            int target = QuarantineLocation.MaxRootLength - 16 - 1 - nameLength;
            deepFolder = Path.Combine(deepFolder, new string('e', target - deepFolder.Length - 1));
        }
        Directory.CreateDirectory(deepFolder);
        (Near(Path.Combine(deepFolder, "dest")) is not null).Should().Be(atTheEdge);
        string entry = string.Join('/', Enumerable.Repeat(new string('d', 60), 5)) + "/a.txt";
        string archive = Path.Combine(_temp.Path, "deep.tar");
        TarBuilder.WriteTar(archive, [TarBuilder.PaxPath(entry), new TarBuilder.Entry { Name = "x", Content = "deep"u8.ToArray() }]);
        string destination = Path.Combine(deepFolder, "dest");

        ArchiveResult result = await Service(backend).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = destination, Mode = ExtractMode.SingleFolder,
        });

        result.Errors.Should().BeEmpty();
        File.ReadAllText(@"\\?\" + Path.Combine(destination, entry.Replace('/', '\\'))).Should().Be("deep");
    }

    // --- Controlled Folder Access ---

    // Measured on PakkoW10 (2026-11-13): in block mode CFA refused the helper's copy writing its out\
    // into Documents (event 1123) and let tar.exe through; so the helper keeps %TEMP% then.
    [Theory]
    [InlineData("local", 1, true)]
    [InlineData("policy", 1, true)]
    [InlineData("local", 0, false)]
    [InlineData("local", 2, false)]
    [InlineData("local", 3, false)]
    [InlineData("none", 0, false)]
    public void ControlledFolderAccess_OnlyBlockModeBlocks(string where, int value, bool blocks)
    {
        FakeRegistry registry = where switch
        {
            "local" => new FakeRegistry().With(CfaLocalKey, value),
            "policy" => new FakeRegistry().With(CfaPolicyKey, value),
            _ => new FakeRegistry(),
        };

        QuarantineLocation.ControlledFolderAccessBlocks(registry).Should().Be(blocks);
    }

    // MDM (measured through the WMI bridge on PakkoW10): the winning provider's value under
    // PolicyManager, the one copy a standard user can read.
    [Theory]
    [InlineData("81B3FFCA-E934-433E-97FB-46C8A582F52E", 1, true)]
    [InlineData("81B3FFCA-E934-433E-97FB-46C8A582F52E", 2, false)]
    [InlineData("not a provider", 1, false)]
    public void ControlledFolderAccess_ByMdm_IsTheWinningProvidersValue(string provider, int value, bool blocks)
    {
        FakeRegistry registry = new FakeRegistry()
            .WithString(@"SOFTWARE\Microsoft\PolicyManager\current\device\Defender", "EnableControlledFolderAccess_WinningProvider", provider)
            .With(@"SOFTWARE\Microsoft\PolicyManager\Providers\81B3FFCA-E934-433E-97FB-46C8A582F52E\default\Device\Defender", value);

        QuarantineLocation.ControlledFolderAccessBlocks(registry).Should().Be(blocks);
    }

    // On the volume of %TEMP% the quarantine stays there: a rename out of it works already, and the
    // profile's protected and synced folders never get a hidden quarantine.
    [Fact]
    public void NearDestination_OnTheVolumeOfTemp_IsNone()
    {
        string tempVolume = PathIdentity.VolumeOf(_temp.Path)!.Value.VolumeRoot;
        string profileVolume = PathIdentity.VolumeOf(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!.Value.VolumeRoot;

        QuarantineLocation.NearDestination(Path.Combine(_temp.Path, "x"), byHelper: false, new FakeRegistry(), [tempVolume]).Should().BeNull();
        QuarantineLocation.VolumesKeptInTemp.Should().Equal(tempVolume, profileVolume);
    }

    [Fact]
    public void NearDestination_AnotherVolume_IsTheDestinationsFolder()
    {
        string? other = OtherFixedVolumeFolder();
        if (other is null)
            return; // one volume only
        try
        {
            QuarantineLocation.NearDestination(Path.Combine(other, "x"), byHelper: false, new FakeRegistry(), QuarantineLocation.VolumesKeptInTemp!)
                .Should().Be(other);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void NearDestination_ControlledFolderAccessBlocking_KeepsTheHelperInTemp_NotTarExe()
    {
        FakeRegistry blocking = new FakeRegistry().With(CfaLocalKey, 1);

        QuarantineLocation.NearDestination(Path.Combine(_temp.Path, "x"), byHelper: true, blocking, keptInTemp: []).Should().BeNull();
        QuarantineLocation.NearDestination(Path.Combine(_temp.Path, "x"), byHelper: false, blocking, keptInTemp: []).Should().Be(_temp.Path);
        QuarantineLocation.NearDestination(Path.Combine(_temp.Path, "x"), byHelper: true, new FakeRegistry(), keptInTemp: []).Should().Be(_temp.Path);
    }

    private const string CfaLocalKey = @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";
    private const string CfaPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";

    private sealed class FakeRegistry : IRegistryReader
    {
        // Registry names are case-insensitive, as Windows reads them.
        private readonly Dictionary<string, object> _values = [with(StringComparer.OrdinalIgnoreCase)];

        public FakeRegistry With(string key, int value, string name = "EnableControlledFolderAccess")
        {
            _values[key + "|" + name] = value;
            return this;
        }

        public FakeRegistry WithString(string key, string name, string value)
        {
            _values[key + "|" + name] = value;
            return this;
        }

        public int? GetDword(string keyPath, string valueName) =>
            _values.TryGetValue(keyPath + "|" + valueName, out object? value) && value is int i ? i : null;

        public string[]? GetMultiString(string keyPath, string valueName) => null;

        public string? GetString(string keyPath, string valueName) =>
            _values.TryGetValue(keyPath + "|" + valueName, out object? value) ? value as string : null;
    }

    // --- Harness ---

    // No volume kept in %TEMP%: these tests run under %TEMP%, and judge the rest of the rule there.
    private static string? Near(string destination) =>
        QuarantineLocation.NearDestination(destination, byHelper: false, new FakeRegistry(), keptInTemp: []);

    // A folder on another fixed volume than %TEMP%, where an extraction quarantines next to its
    // destination; %TEMP% on a machine with one volume.
    private string ServiceFolder() => (_other ??= OtherFixedVolumeFolder()) ?? _temp.Path;

    private string Archive(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N")[..8] + ".tar");
        TarBuilder.WriteTar(path, entries.Select(e => new TarBuilder.Entry { Name = e.Name, Content = Encoding.UTF8.GetBytes(e.Content) }));
        return path;
    }

    private static TarSandboxedService Service(string backend) =>
        new(new GroupPolicyOptions()) { Helper = backend == HelperReaderTests.Helper ? HelperSetupFor() : null };

    private static HelperSetup HelperSetupFor() => HelperReaderTests.SharedSetup;

    private static async Task<int> CmdAsync(TarSandboxScope scope, string command)
    {
        using SafeSidHandle sid = new AppContainerProfile(TarSandboxScope.SandboxNameFor(scope.QuarantineRoot)).GetSid();
        (int exitCode, _, _) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe", ["/d", "/c", command],
            new ProcessLaunchOptions(AppContainerSid: sid, WorkingDirectory: scope.QuarantineRoot), CancellationToken.None);
        return exitCode;
    }

    private static string? OtherFixedVolumeFolder()
    {
        string tempRoot = Path.GetPathRoot(Path.GetTempPath())!;
        string? otherRoot = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady && !string.Equals(d.RootDirectory.FullName, tempRoot, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.RootDirectory.FullName).FirstOrDefault();
        if (otherRoot is null)
            return null;
        string folder = Path.Combine(otherRoot, "pakko-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
