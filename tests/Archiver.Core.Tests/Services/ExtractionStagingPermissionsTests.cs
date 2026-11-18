using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F532: merging into a folder that exists, each file is renamed in from staging, and a rename keeps
/// the file's DACL: it carried what staging's folder passes down, not what its own folder in the
/// destination does. A file whose folder passes down other entries (its own, one a folder above it
/// gave, one its parent gives its children only) has its inheritance written again; a file whose
/// folder passes down the same keeps its DACL untouched.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExtractionStagingPermissionsTests : IDisposable
{
    private const string Mark = "(A;ID;FR;;;BG)";
    private static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Merge_EachFileTakesWhatItsOwnFolderGives()
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(Path.Combine(dest, "s", "t"));
        Give(Path.Combine(dest, "s"), PropagationFlags.None);

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, "a.txt");
            Stage(staging, Path.Combine("s", "b.txt"));
            Stage(staging, Path.Combine("s", "t", "c.txt"));
            Stage(staging, Path.Combine("s", "new", "d.txt"));
            staging.CommitInto(dest).Should().BeEmpty();
            staging.PermissionsNotApplied.Should().BeEmpty();
        }

        foreach (string name in new[] { "a.txt", Path.Combine("s", "b.txt"), Path.Combine("s", "t", "c.txt"), Path.Combine("s", "new", "d.txt") })
            Rules(Path.Combine(dest, name)).Should().Equal(RulesOfAFileMadeBeside(Path.Combine(dest, name)), name);
        Rules(Path.Combine(dest, "s", "t", "c.txt")).Should().Contain(r => r.StartsWith(Everyone.Value, StringComparison.Ordinal),
            "a folder with nothing of its own still passes down what the folder above it gave");
    }

    [Fact]
    public void Merge_AnEntryTheDestinationGivesItsChildrenOnly_ReachesItsFiles()
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        Give(dest, PropagationFlags.NoPropagateInherit);

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, "a.txt");
            Stage(staging, Path.Combine("sub", "b.txt"));
            staging.CommitInto(dest).Should().BeEmpty();
        }

        Rules(Path.Combine(dest, "a.txt")).Should().Equal(RulesOfAFileMadeBeside(Path.Combine(dest, "a.txt")));
        Rules(Path.Combine(dest, "a.txt")).Should().Contain(r => r.StartsWith(Everyone.Value, StringComparison.Ordinal));
        Rules(Path.Combine(dest, "sub", "b.txt")).Should().Equal(RulesOfAFileMadeBeside(Path.Combine(dest, "sub", "b.txt")));
    }

    // T-F555: a folder that passes nothing to its files (its entries reach subfolders only, or this
    // folder only): a file made there gets the creator's default DACL (measured: SYSTEM, the logon
    // session, the user), not none. A merged file gets the same, never an empty DACL no one can open.
    [Fact]
    public void Merge_AFolderThatGivesItsFilesNothing_FilesGetWhatAFileMadeThereGets()
    {
        string dest = Path.Combine(_temp.Path, "dest");
        string sub = Path.Combine(dest, "s");
        Directory.CreateDirectory(sub);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(sub).SetAccessControl(security);
        string b = Path.Combine(sub, "b.txt");

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, Path.Combine("s", "b.txt"));
            staging.CommitInto(dest).Should().BeEmpty();
            staging.PermissionsNotApplied.Should().BeEmpty();
        }

        Rules(b).Should().NotBeEmpty().And.Equal(RulesOfAFileMadeBeside(b));
        File.ReadAllText(b).Should().Be(Path.Combine("s", "b.txt"));
    }

    // The usual merge: the destination's folders pass down what staging's did, and no file is rewritten.
    [Fact]
    public void Merge_FoldersThatGiveWhatStagingGave_LeaveTheFilesAlone()
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(Path.Combine(dest, "s"));

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, "a.txt", mark: true);
            Stage(staging, Path.Combine("s", "b.txt"), mark: true);
            staging.CommitInto(dest).Should().BeEmpty();
        }

        RawDacl.Sddl(Path.Combine(dest, "a.txt")).Should().EndWith(Mark);
        RawDacl.Sddl(Path.Combine(dest, "s", "b.txt")).Should().EndWith(Mark);
    }

    // A folder whose entries cannot be read by its plain path (deeper than MAX_PATH) is not taken for one
    // that gives what staging gave: its files take its entries all the same.
    [Fact]
    public void Merge_AFolderDeeperThanMaxPath_StillGivesItsEntries()
    {
        string dest = Path.Combine(_temp.Path, "dest");
        string relativeFolder = Path.Combine(new string('d', 100), new string('e', 100), new string('f', 100), "s");
        Directory.CreateDirectory(Path.Combine(dest, relativeFolder));
        Give(Path.Combine(dest, relativeFolder), PropagationFlags.None);
        string relative = Path.Combine(relativeFolder, "b.txt");

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, relative);
            staging.CommitInto(dest).Should().BeEmpty();
            staging.PermissionsNotApplied.Should().BeEmpty();
        }

        Rules(Path.Combine(dest, relative)).Should().Contain(r => r.StartsWith(Everyone.Value, StringComparison.Ordinal));
    }

    // ZIP extraction commits through the same staging.
    [Fact]
    public async Task ZipExtraction_IntoAFolderWithEntriesOfItsOwn_FilesTakeThem()
    {
        string zip = Path.Combine(_temp.Path, "in.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (string name in new[] { "a.txt", "s/b.txt" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(name);
            }
        }
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(Path.Combine(dest, "s"));
        Give(Path.Combine(dest, "s"), PropagationFlags.None);

        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions()).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, OnConflict = ConflictBehavior.Overwrite,
        });

        result.Errors.Should().BeEmpty();
        string b = Path.Combine(dest, "s", "b.txt");
        File.Exists(b).Should().BeTrue();
        Rules(b).Should().Equal(RulesOfAFileMadeBeside(b));
        Rules(b).Should().Contain(r => r.StartsWith(Everyone.Value, StringComparison.Ordinal));
    }

    private static void Stage(ExtractionStaging staging, string relative, bool mark = false)
    {
        string file = Path.Combine(staging.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, relative);
        if (mark)
            RawDacl.Write(file, RawDacl.Sddl(file) + Mark);
    }

    private static void Give(string folder, PropagationFlags propagation)
    {
        var info = new DirectoryInfo(folder);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(Everyone, FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, propagation, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    private static List<string> RulesOfAFileMadeBeside(string file)
    {
        string probe = Path.Combine(Path.GetDirectoryName(file)!, "probe-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(probe, "p");
        try
        {
            return Rules(probe);
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private static List<string> Rules(string file) =>
        [.. new FileInfo(file).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(r => $"{r.IdentityReference.Value} {r.FileSystemRights} {r.AccessControlType} {r.IsInherited}")
            .Order(StringComparer.Ordinal)];
}
