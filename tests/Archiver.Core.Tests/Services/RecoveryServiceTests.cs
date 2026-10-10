using System.Runtime.Versioning;
using Archiver.Core.Models;
using Archiver.Core.Recovery;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Recovery;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F275 step 4: repair from PAR2 recovery data through the service the frontends call. The
// archives are the golden content under the right magic; the ZIP engine is a fake whose test
// passes unless told to fail, and no test here starts tar.exe.
[SupportedOSPlatform("windows")]
public sealed class RecoveryServiceTests : IDisposable
{
    private const int Length = 40_000;
    private const string Mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
    private readonly TempDirectory _temp = new();
    private readonly TestingZipEngine _zip = new();

    public void Dispose() => _temp.Dispose();

    private ExtractionRouter Router(GroupPolicyOptions policy) =>
        new(_zip, new UnusedTarEngine(), new TarCapabilities(), policy);

    private RecoveryService Service(GroupPolicyOptions? policy = null)
    {
        policy ??= new GroupPolicyOptions();
        return new RecoveryService(policy, () => Task.FromResult<Interfaces.IExtractionRouter>(Router(policy)));
    }

    private Task<ArchiveResult> Repair(params string[] paths) =>
        Service().RepairAsync(new RepairOptions { Paths = paths });

    private Task<ArchiveResult> Test(params string[] paths) =>
        Router(new GroupPolicyOptions()).TestAsync(paths, verifyRecoveryData: true);

    private static byte[] Bytes(string name, int length, int seed)
    {
        byte[] content = Par2TestData.Content(length);
        for (int i = 0; i < content.Length; i++)
            content[i] ^= (byte)seed;
        // The detector reads the magic: these make "a.zip" a ZIP and "a.tar.gz" a tar-family archive.
        if (name.EndsWith(".zip", StringComparison.Ordinal))
            new byte[] { 0x50, 0x4B, 0x03, 0x04 }.CopyTo(content, 0);
        else if (name.EndsWith(".gz", StringComparison.Ordinal))
            new byte[] { 0x1F, 0x8B, 0x08 }.CopyTo(content, 0);
        return content;
    }

    private string Archive(string name, int length = Length, int seed = 0)
    {
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, Bytes(name, length, seed));
        return path;
    }

    // The archive with a set next to it, as `pakko a -rr` writes it.
    private string Protected(string name, int percent = 5, int length = Length, int seed = 0)
    {
        string path = Archive(name, length, seed);
        Par2Creator.Create(path, Par2Creator.ChooseParameters(length, percent)!.Value, null, CancellationToken.None);
        return path;
    }

    private static void Damage(string path, int offset, int count)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = offset;
        stream.Write(Enumerable.Repeat((byte)0xA5, count).ToArray());
    }

    private static MessageCode? Code(ArchiveError error) => error.Text?.Code;

    private string[] Files() => [.. Directory.GetFiles(_temp.Path, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(_temp.Path, p)).Order(StringComparer.Ordinal)];

    // --- Happy path ---

    [Theory]
    [InlineData("a.tar.gz", "a.repaired.tar.gz")]
    [InlineData("a.zip", "a.repaired.zip")]
    [InlineData("notes", "notes.repaired")]
    [InlineData("a.b.7z", "a.b.repaired.7z")]
    public async Task DamagedArchive_IsRebuiltNextToIt_AndTheOriginalIsOnlyRead(string name, string repairedName)
    {
        string archive = Protected(name);
        byte[] good = File.ReadAllBytes(archive);
        Damage(archive, 5_000, 16);
        _zip.Failing.Add(archive);
        byte[] damaged = File.ReadAllBytes(archive);
        DateTime modified = File.GetLastWriteTimeUtc(archive);
        string[] before = Files();

        ArchiveResult result = await Repair(archive);

        string repaired = Path.Combine(_temp.Path, repairedName);
        result.Errors.Should().BeEmpty();
        result.Outcome.Should().Be(OperationOutcome.Completed);
        result.CreatedFiles.Should().Equal(repaired);
        File.ReadAllBytes(repaired).Should().Equal(good);
        File.ReadAllBytes(archive).Should().Equal(damaged);
        File.GetLastWriteTimeUtc(archive).Should().Be(modified);
        Files().Should().BeEquivalentTo([.. before, repairedName]);
        RecoveryCheck check = result.RecoveryChecks.Should().ContainSingle().Subject;
        check.State.Should().Be(RecoveryState.Repaired);
        check.ArchivePath.Should().Be(archive);
        check.RepairedPath.Should().Be(repaired);
        check.DamagedBlocks.Should().Be(1);
        check.Text!.Code.Should().Be(MessageCode.RecoveryDataRepaired);
        check.Text.English.Should().Be($"The archive was repaired (1 of 2000 blocks). The repaired copy is {repairedName}; the original was not changed.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Par2Path_RepairsTheArchiveItProtects(bool index)
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);

        ArchiveResult result = await Repair(index ? Par2Creator.IndexPath(archive) : Directory.GetFiles(_temp.Path, "*.vol*.par2").Single());

        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "a.repaired.tar.gz"));
        result.RecoveryChecks.Should().ContainSingle().Which.ArchivePath.Should().Be(archive);
    }

    [Fact]
    public async Task ArchiveAndItsPar2FilesTogether_AreRepairedOnce()
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);

        ArchiveResult result = await Repair([.. Directory.GetFiles(_temp.Path, "*.par2"), archive, archive]);

        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().ContainSingle();
        result.RecoveryChecks.Should().ContainSingle();
        Directory.GetFiles(_temp.Path, "*repaired*").Should().ContainSingle();
    }

    [Fact]
    public async Task IntactArchive_WritesNothingAndSaysItMatches()
    {
        string archive = Protected("a.zip");
        string[] before = Files();

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Intact);
        result.RecoveryChecks[0].Text!.Code.Should().Be(MessageCode.RecoveryDataIntact);
        Files().Should().Equal(before);
        _zip.Tested.Should().BeEmpty("a good archive is read once, by the set check");
    }

    [Fact]
    public async Task OutputDirectory_IsCreatedAndTakesTheCopy()
    {
        string archive = Protected("a.tar.gz");
        byte[] good = File.ReadAllBytes(archive);
        Damage(archive, 5_000, 16);
        string[] before = Files();
        string folder = Path.Combine(_temp.Path, "out", "deeper");

        ArchiveResult result = await Service().RepairAsync(new RepairOptions { Paths = [archive], OutputDirectory = folder });

        string repaired = Path.Combine(folder, "a.repaired.tar.gz");
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(repaired);
        File.ReadAllBytes(repaired).Should().Equal(good);
        Files().Should().BeEquivalentTo([.. before, Path.GetRelativePath(_temp.Path, repaired)]);
    }

    // The set alone is enough when it holds as many recovery blocks as the archive has blocks.
    [Fact]
    public async Task ArchiveGone_IsRebuiltFromAFullSet()
    {
        string archive = Protected("a.tar.gz", percent: 100, length: 2_000);
        byte[] good = File.ReadAllBytes(archive);
        File.Delete(archive);

        ArchiveResult result = await Repair(Par2Creator.IndexPath(archive));

        string repaired = Path.Combine(_temp.Path, "a.repaired.tar.gz");
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(repaired);
        File.ReadAllBytes(repaired).Should().Equal(good);
        File.Exists(archive).Should().BeFalse("the original name is never written");
    }

    [Fact]
    public async Task SeveralArchives_EachGetsItsOwnVerdict()
    {
        string repairable = Protected("a.tar.gz");
        Damage(repairable, 5_000, 16);
        string beyond = Protected("b.tar.gz", seed: 1);
        Damage(beyond, 1_000, 30_000);
        string intact = Protected("c.tar.gz", seed: 2);
        string bare = Archive("d.tar.gz", seed: 3);

        ArchiveResult result = await Repair(repairable, beyond, intact, bare);

        result.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "a.repaired.tar.gz"));
        result.Errors.Select(e => (e.SourcePath, Code(e))).Should().BeEquivalentTo(new[]
        {
            (beyond, (MessageCode?)MessageCode.RecoveryDataDamagedNotRepairable),
            (bare, (MessageCode?)MessageCode.RecoveryDataNotFound),
        });
        result.RecoveryChecks.Select(c => (c.ArchivePath, c.State)).Should().BeEquivalentTo(new[]
        {
            (repairable, RecoveryState.Repaired), (beyond, RecoveryState.NotRepairable), (intact, RecoveryState.Intact),
        });
        Directory.GetFiles(_temp.Path, "*repaired*").Should().ContainSingle();
    }

    [Fact]
    public async Task Progress_ClimbsToOneHundredOnce_AndNamesBothPhases()
    {
        string archive = Protected("a.tar.gz", length: 300_001);
        Damage(archive, 5_000, 16);
        var reports = new List<ProgressReport>();

        await Service().RepairAsync(new RepairOptions { Paths = [archive] }, new SyncProgress(reports.Add));

        reports.Select(r => r.Percent).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        reports[^1].Percent.Should().Be(100);
        reports.Select(r => r.Phase).Distinct().Should().Equal(ProgressPhase.VerifyingRecoveryData, ProgressPhase.RepairingArchive);
    }

    // --- Repair rebuilds exactly what the test calls damaged and repairable ---

    public static TheoryData<string, bool, int, bool> Scenarios => new()
    {
        // name, the ZIP engine fails it, bytes damaged, rewritten beside its old set
        { "a.tar.gz", false, 0, false },
        { "a.tar.gz", false, 16, false },
        { "a.tar.gz", false, 30_000, false },
        { "a.tar.gz", false, 0, true },
        { "a.zip", false, 0, false },
        { "a.zip", true, 16, false },
        { "a.zip", true, 30_000, false },
        // The set disagrees in a block it could rebuild, and the ZIP tests as intact: not damage.
        { "a.zip", false, 16, false },
        { "a.zip", false, 0, true },
        { "a.zip", true, 0, true },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Repair_WritesACopy_ExactlyWhenTheTestSaysRepairable(string name, bool zipFails, int damagedBytes, bool rewritten)
    {
        string archive = Protected(name);
        if (rewritten)
            File.WriteAllBytes(archive, Bytes(name, Length, seed: 9));
        if (damagedBytes > 0)
            Damage(archive, 1_000, damagedBytes);
        if (zipFails)
            _zip.Failing.Add(archive);

        ArchiveResult tested = await Test(archive);
        ArchiveResult repaired = await Repair(archive);

        RecoveryState said = tested.RecoveryChecks.Single().State;
        RecoveryState did = repaired.RecoveryChecks.Single().State;
        did.Should().Be(said == RecoveryState.Repairable ? RecoveryState.Repaired : said);
        repaired.CreatedFiles.Should().HaveCount(said == RecoveryState.Repairable ? 1 : 0);
        Directory.GetFiles(_temp.Path, "*repaired*").Should().HaveCount(said == RecoveryState.Repairable ? 1 : 0);
        repaired.Warnings.Select(w => w.Text?.Code).Should().Equal(tested.Warnings.Select(w => w.Text?.Code));
    }

    // A ZIP that tests as intact while its set disagrees in blocks the set could rebuild: a newer
    // archive beside an older set. Rebuilding would hand back the old version as a "repair".
    [Fact]
    public async Task ZipThatTestsIntactWhileItsSetDisagrees_IsWarnedAbout_AndNothingIsRebuilt()
    {
        string archive = Protected("a.zip");
        Damage(archive, 5_000, 16);
        string[] before = Files();

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataDoesNotMatch);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.DoesNotMatch);
        Files().Should().Equal(before);
        _zip.Tested.Should().Equal(archive);
    }

    // --- No usable recovery data: an error, since repair was asked for ---

    [Fact]
    public async Task ArchiveWithoutASet_IsAnError()
    {
        string archive = Archive("a.zip");

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().ContainSingle().Which.Should().Match<ArchiveError>(e => e.SourcePath == archive && Code(e) == MessageCode.RecoveryDataNotFound);
        result.Warnings.Should().BeEmpty();
        result.Outcome.Should().Be(OperationOutcome.Failed);
    }

    // What a cancelled or killed `pakko a -rr` leaves: temporary files, never a set.
    [Fact]
    public async Task OnlyTheWritersTemporaryFiles_IsNoSet()
    {
        string archive = Archive("a.zip");
        File.WriteAllBytes(Path.Combine(_temp.Path, ".pakko-a-123-abc.tmp"), Par2TestData.Content(2_000));
        File.WriteAllBytes(archive + ".par2.tmp", Par2TestData.Content(2_000));
        string[] before = Files();

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataNotFound);
        Files().Should().Equal(before);
    }

    [Fact]
    public async Task UnreadableSetNextToTheArchive_IsAnErrorNotAWarning()
    {
        string archive = Archive("a.zip");
        File.WriteAllBytes(archive + ".par2", Par2TestData.Content(3_000));

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().ContainSingle().Which.Should().Match<ArchiveError>(e => e.SourcePath == archive && Code(e) == MessageCode.RecoveryDataUnusable);
        result.Warnings.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
    }

    // A set put under this archive's name by hand or by an attacker: nothing is built from it.
    [Fact]
    public async Task AnotherFilesSetUnderThisName_IsAnErrorAndNothingIsBuilt()
    {
        string other = Protected("other.zip", seed: 5);
        string archive = Archive("a.zip");
        File.Move(Par2Creator.IndexPath(other), archive + ".par2");
        File.Move(Directory.GetFiles(_temp.Path, "other.zip.vol*.par2").Single(), Path.Combine(_temp.Path, "a.zip.vol000+100.par2"));
        File.Delete(other);
        string[] before = Files();

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataForAnotherFile);
        result.CreatedFiles.Should().BeEmpty();
        Files().Should().Equal(before);
    }

    [Theory]
    [InlineData("missing.zip")]
    [InlineData("missing.zip.par2")]
    [InlineData("")]
    [InlineData("a|b<c>.zip")]
    [InlineData("\0.par2")]
    public async Task PathThatIsNotThere_IsAnErrorNeverAThrow(string name)
    {
        string path = name.Length == 0 || name.Contains('|') || name.Contains('\0') ? name : Path.Combine(_temp.Path, name);

        ArchiveResult result = await Repair(path);

        result.Errors.Should().ContainSingle();
        result.CreatedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Par2ThatCannotBeRead_IsAnError()
    {
        Archive("a.zip");
        string par2 = Path.Combine(_temp.Path, "a.zip.par2");
        File.WriteAllBytes(par2, Par2TestData.Content(3_000));

        ArchiveResult result = await Repair(par2);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataUnusable);
    }

    // --- Damage the set cannot undo, and sets that are damaged themselves ---

    [Fact]
    public async Task DamagedBeyondTheSet_IsAnErrorAndNothingIsWritten()
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 1_000, 30_000);
        string[] before = Files();

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataDamagedNotRepairable);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.NotRepairable);
        result.CreatedFiles.Should().BeEmpty();
        Files().Should().Equal(before);
    }

    // Recovery data changed with its packet hash recomputed reads as valid; the copy built from it
    // fails the final check against the set and is not kept.
    [Fact]
    public async Task ForgedRecoveryBlock_IsCaughtByTheFinalCheck_AndNoCopyIsKept()
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);
        Par2RepairTests.ForgeRecoveryBlock(Directory.GetFiles(_temp.Path, "*.vol*.par2").Single(), exponent: 0);
        string[] before = Files();

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryRepairCheckFailed);
        result.CreatedFiles.Should().BeEmpty();
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Repairable);
        Files().Should().Equal(before);
    }

    // A volume cut short (a copy that stopped, a kill while it was written by another tool): the
    // blocks that are whole are used, and when they are not enough nothing is written.
    [Theory]
    [InlineData(16, true)]
    [InlineData(6_000, false)]
    public async Task VolumeCutShort_UsesTheBlocksThatAreWhole(int damagedBytes, bool enough)
    {
        string archive = Protected("a.tar.gz", percent: 10);
        byte[] good = File.ReadAllBytes(archive);
        Damage(archive, 5_000, damagedBytes);
        string volume = Directory.GetFiles(_temp.Path, "*.vol*.par2").Single();
        using (FileStream stream = File.Open(volume, FileMode.Open, FileAccess.Write))
            stream.SetLength(stream.Length * 55 / 100); // past the leading copy of the critical packets, into the blocks

        ArchiveResult result = await Repair(archive);

        if (enough)
        {
            result.Errors.Should().BeEmpty();
            File.ReadAllBytes(result.CreatedFiles.Should().ContainSingle().Subject).Should().Equal(good);
        }
        else
        {
            result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataDamagedNotRepairable);
            Directory.GetFiles(_temp.Path, "*repaired*").Should().BeEmpty();
        }
    }

    [Fact]
    public async Task IndexGone_TheVolumeAloneRepairs()
    {
        string archive = Protected("a.tar.gz");
        byte[] good = File.ReadAllBytes(archive);
        Damage(archive, 5_000, 16);
        File.Delete(Par2Creator.IndexPath(archive));

        ArchiveResult result = await Repair(archive);

        File.ReadAllBytes(result.CreatedFiles.Should().ContainSingle().Subject).Should().Equal(good);
    }

    [Fact]
    public async Task VolumeGone_TheIndexAloneCannotRepair()
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);
        File.Delete(Directory.GetFiles(_temp.Path, "*.vol*.par2").Single());

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataDamagedNotRepairable);
        result.CreatedFiles.Should().BeEmpty();
    }

    // A set renamed with its archive still matches by content: it repairs, and says the name differs.
    [Fact]
    public async Task ArchiveAndSetRenamed_RepairWithTheNameWarning()
    {
        string archive = Protected("a.tar.gz");
        string renamed = Path.Combine(_temp.Path, "b.tar.gz");
        File.Move(archive, renamed);
        foreach (string par2 in Directory.GetFiles(_temp.Path, "a.tar.gz*.par2"))
            File.Move(par2, Path.Combine(_temp.Path, "b" + Path.GetFileName(par2)[1..]));
        Damage(renamed, 30_000, 16);

        ArchiveResult result = await Repair(renamed);

        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "b.repaired.tar.gz"));
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataNameMismatch);
    }

    // --- Where the copy goes ---

    [Fact]
    public async Task RepairedNameTaken_TheNextNumberIsUsedAndTheExistingFileStays()
    {
        string archive = Protected("a.tar.gz");
        byte[] good = File.ReadAllBytes(archive);
        Damage(archive, 5_000, 16);
        string taken = Path.Combine(_temp.Path, "a.repaired.tar.gz");
        File.WriteAllText(taken, "keep");
        File.WriteAllText(Path.Combine(_temp.Path, "a.repaired (1).tar.gz"), "keep too");

        ArchiveResult result = await Repair(archive);

        string repaired = Path.Combine(_temp.Path, "a.repaired (2).tar.gz");
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(repaired);
        File.ReadAllBytes(repaired).Should().Equal(good);
        File.ReadAllText(taken).Should().Be("keep");
    }

    [Fact]
    public async Task FolderThatCannotBeWrittenTo_IsAnError_AndAnotherFolderWorks()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "locked")).FullName;
        string archive = Path.Combine(folder, "a.tar.gz");
        File.Move(Protected("a.tar.gz"), archive);
        foreach (string par2 in Directory.GetFiles(_temp.Path, "*.par2"))
            File.Move(par2, Path.Combine(folder, Path.GetFileName(par2)));
        Damage(archive, 5_000, 16);
        byte[] damaged = File.ReadAllBytes(archive);
        string[] before = Files();
        ArchiveResult refused;
        using (new NoCreateFolder(folder))
        {
            refused = await Repair(archive);
            Files().Should().Equal(before);
        }

        ArchiveResult elsewhere = await Service().RepairAsync(new RepairOptions { Paths = [archive], OutputDirectory = Path.Combine(_temp.Path, "out") });

        ArchiveError error = refused.Errors.Should().ContainSingle().Subject;
        error.SourcePath.Should().Be(archive);
        Code(error).Should().Be(MessageCode.RecoveryRepairNotWritten);
        refused.CreatedFiles.Should().BeEmpty();
        File.ReadAllBytes(archive).Should().Equal(damaged);
        elsewhere.Errors.Should().BeEmpty();
        elsewhere.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "out", "a.repaired.tar.gz"));
    }

    [Fact]
    public async Task OutputDirectoryThatIsAFile_IsAnError()
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);
        string notAFolder = Path.Combine(_temp.Path, "file.txt");
        File.WriteAllText(notAFolder, "x");

        ArchiveResult result = await Service().RepairAsync(new RepairOptions { Paths = [archive], OutputDirectory = notAFolder });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryRepairNotWritten);
        result.CreatedFiles.Should().BeEmpty();
    }

    // --- Cancel ---

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(70)]
    [InlineData(95)]
    public async Task CancelledAtAnyPoint_ThrowsAndLeavesNoFile(int atPercent)
    {
        string archive = Protected("a.tar.gz", percent: 10, length: 300_001);
        Damage(archive, 1_000, 5_000);
        string[] before = Files();
        using var cts = new CancellationTokenSource();
        if (atPercent == 0)
            cts.Cancel();
        var progress = new SyncProgress(r =>
        {
            if (r.Percent >= atPercent)
                cts.Cancel();
        });

        Func<Task> repair = () => Service().RepairAsync(new RepairOptions { Paths = [archive] }, progress, cts.Token);

        await repair.Should().ThrowAsync<OperationCanceledException>();
        Files().Should().Equal(before);
    }

    // --- Policy ---

    [Fact]
    public async Task UnderDisableRecoveryData_EveryPathIsRefusedAndNothingIsRead()
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);
        string index = Par2Creator.IndexPath(archive);
        string[] before = Files();

        ArchiveResult result = await Service(new GroupPolicyOptions { DisableRecoveryData = true }).RepairAsync(new RepairOptions { Paths = [archive, index] });

        result.Errors.Select(e => (e.SourcePath, Code(e))).Should().Equal(
            (index, (MessageCode?)MessageCode.RecoveryDataDisabled), (archive, (MessageCode?)MessageCode.RecoveryDataDisabled));
        result.RecoveryChecks.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        Files().Should().Equal(before);
    }

    // BlockedFormats stops Pakko from opening a format. A repair copies bytes and hashes them, so
    // Core still does it; the App and Explorer do not offer it on a blocked archive.
    [Fact]
    public async Task UnderBlockedFormats_TheBytesAreStillRepaired()
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);

        ArchiveResult result = await Service(new GroupPolicyOptions { BlockedFormats = ["gzip"] }).RepairAsync(new RepairOptions { Paths = [archive] });

        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().ContainSingle();
    }

    // --- The "downloaded from the internet" mark ---

    [Fact]
    public async Task MarkedArchive_GivesAMarkedCopy()
    {
        string archive = Protected("a.zip");
        Damage(archive, 5_000, 16);
        _zip.Failing.Add(archive);
        File.WriteAllText(archive + ":Zone.Identifier", Mark);

        ArchiveResult result = await Repair(archive);

        File.ReadAllText(result.CreatedFiles.Should().ContainSingle().Subject + ":Zone.Identifier").Should().Be(Mark);
    }

    // With the archive gone the copy is made of the PAR2 files alone, and is marked as they are.
    [Fact]
    public async Task ArchiveGoneAndMarkedSet_GivesAMarkedCopy()
    {
        string archive = Protected("a.tar.gz", percent: 100, length: 2_000);
        File.Delete(archive);
        File.WriteAllText(Directory.GetFiles(_temp.Path, "*.vol*.par2").Single() + ":Zone.Identifier", Mark);

        ArchiveResult result = await Repair(Par2Creator.IndexPath(archive));

        File.ReadAllText(result.CreatedFiles.Should().ContainSingle().Subject + ":Zone.Identifier").Should().Be(Mark);
    }

    [Fact]
    public async Task UnmarkedArchive_GivesAnUnmarkedCopy()
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);

        ArchiveResult result = await Repair(archive);

        File.Exists(result.CreatedFiles.Should().ContainSingle().Subject + ":Zone.Identifier").Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task MarkLeftOffByTheUser_IsLeftOff_UnlessPolicyEnforcesIt(bool enforced, bool marked)
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);
        File.WriteAllText(archive + ":Zone.Identifier", Mark);
        var policy = new GroupPolicyOptions { MotwModeSetByPolicy = enforced };

        ArchiveResult result = await Service(policy).RepairAsync(new RepairOptions { Paths = [archive], ApplyDownloadMark = false });

        File.Exists(result.CreatedFiles.Should().ContainSingle().Subject + ":Zone.Identifier").Should().Be(marked);
    }

    // A policy narrower than "all files" is about what an archive unpacks to. The copy is the
    // archive itself, so it keeps the archive's mark whatever its extension.
    [Fact]
    public async Task NarrowMarkPolicy_StillMarksTheCopyOfAMarkedArchive()
    {
        string archive = Protected("a.tar.gz");
        Damage(archive, 5_000, 16);
        File.WriteAllText(archive + ":Zone.Identifier", Mark);
        var policy = new GroupPolicyOptions { MotwMode = MotwMode.UnsafeExtensionsOnly, MotwModeSetByPolicy = true };

        ArchiveResult result = await Service(policy).RepairAsync(new RepairOptions { Paths = [archive] });

        File.ReadAllText(result.CreatedFiles.Should().ContainSingle().Subject + ":Zone.Identifier").Should().Be(Mark);
    }

    // --- Sets made by other tools ---

    // par2cmdline's own files, under the name it gives them and under the short base QuickPar and
    // MultiPar use ("data.par2" for "data.bin").
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Par2cmdlineSet_UnderEitherName_Repairs(bool shortBase)
    {
        const int length = 300001;
        string archive = Par2TestData.WriteContent(_temp.Path, "data.bin", length);
        string golden = Path.Combine(Par2TestData.GoldenDir, "data.bin");
        string baseName = shortBase ? "data" : "data.bin";
        File.Copy(golden + ".par2", Path.Combine(_temp.Path, baseName + ".par2"));
        File.Copy(golden + ".vol0+4.par2", Path.Combine(_temp.Path, baseName + ".vol0+4.par2"));
        Damage(archive, 100_000, 16);

        ArchiveResult result = await Repair(archive);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        string repaired = result.CreatedFiles.Should().ContainSingle().Subject;
        repaired.Should().Be(Path.Combine(_temp.Path, "data.repaired.bin"));
        File.ReadAllBytes(repaired).Should().Equal(Par2TestData.Content(length));
    }

    // --- Names ---

    [Theory]
    [InlineData(@"C:\a\photos.zip", "photos.repaired.zip")]
    [InlineData(@"C:\a\src.tar.gz", "src.repaired.tar.gz")]
    [InlineData(@"C:\a\SRC.TAR.ZST", "SRC.repaired.TAR.ZST")]
    [InlineData(@"C:\a\noextension", "noextension.repaired")]
    [InlineData(@"C:\a\.hidden", ".hidden.repaired")]
    [InlineData(@"C:\a\a.b.c.7z", "a.b.c.repaired.7z")]
    public void RepairedName_KeepsTheExtensionEveryToolRecognizes(string archivePath, string expected) =>
        RecoveryService.RepairedName(archivePath).Should().Be(expected);

    private sealed class SyncProgress(Action<ProgressReport> onReport) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) => onReport(value);
    }
}
