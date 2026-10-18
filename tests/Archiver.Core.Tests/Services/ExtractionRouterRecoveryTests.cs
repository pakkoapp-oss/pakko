using System.Runtime.Versioning;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Recovery;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Recovery;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F275 step 3: a ZIP engine whose test passes every path it is given, or fails the ones named.
internal sealed class TestingZipEngine : IArchiveService
{
    public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Tested { get; } = [];
    public Func<string, bool, bool>? Filter { get; private set; }

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, Func<string, bool, bool>? entryFilter = null, CancellationToken cancellationToken = default)
    {
        Tested.AddRange(archivePaths);
        Filter = entryFilter;
        progress?.Report(new ProgressReport { Percent = 50 });
        progress?.Report(new ProgressReport { Percent = 100 });
        return Task.FromResult(new ArchiveResult
        {
            Errors = [.. archivePaths.Where(Failing.Contains).Select(p => CoreMessages.Error(p, MessageCode.ZipCorrupted))],
            Sources = [.. archivePaths.Select(p => new SourceResult { Path = p, Outcome = Failing.Contains(p) ? SourceOutcome.NotProcessed : SourceOutcome.Completed })],
        });
    }

    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

// T-F397: a tar engine whose test reads every path it is given to its end and passes, or fails
// the paths named. As the real engine, it says that only the structure was checked where the
// format gives tar.exe no checksum to compare (a tar.gz), and says nothing of a 7z.
internal sealed class TestingTarEngine : ITarService
{
    public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Tested { get; } = [];
    public Func<string, bool, bool>? Filter { get; private set; }

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<string, bool, bool>? entryFilter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tested.AddRange(archivePaths);
        Filter = entryFilter;
        progress?.Report(new ProgressReport { Percent = 50 });
        progress?.Report(new ProgressReport { Percent = 100 });
        return Task.FromResult(new ArchiveResult
        {
            Errors = [.. archivePaths.Where(Failing.Contains).Select(p => CoreMessages.Error(p, MessageCode.CannotReadArchive, "damaged"))],
            Warnings = [.. archivePaths.Where(p => !Failing.Contains(p) && !TarTestCoverage.VerifiesContents(p)).Select(p => CoreMessages.Warning(p, CoreMessages.Text(MessageCode.TestedStructureOnly)))],
            Sources = [.. archivePaths.Select(p => new SourceResult { Path = p, Outcome = Failing.Contains(p) ? SourceOutcome.Partial : SourceOutcome.Completed })],
        });
    }

    public Task<bool> CanCreateSevenZipAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());
    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

[SupportedOSPlatform("windows")]
public sealed class ExtractionRouterRecoveryTests : IDisposable
{
    private const int Length = 10_000;
    private readonly TempDirectory _temp = new();
    private readonly TestingZipEngine _zip = new();
    private readonly TestingTarEngine _tar = new();

    public void Dispose() => _temp.Dispose();

    private ExtractionRouter Router(GroupPolicyOptions? policy = null) =>
        new(_zip, _tar, new TarCapabilities(), policy ?? new GroupPolicyOptions());

    private Task<ArchiveResult> Test(params string[] paths) =>
        Router().TestAsync(paths, verifyRecoveryData: true);

    private string Archive(string name, int length = Length)
    {
        string path = Path.Combine(_temp.Path, name);
        byte[] content = Par2TestData.Content(length);
        // The detector reads the magic: a gzip header makes "a.tar.gz" a tar-family archive.
        if (name.EndsWith(".gz", StringComparison.Ordinal))
            new byte[] { 0x1F, 0x8B, 0x08 }.CopyTo(content, 0);
        else if (name.EndsWith(".7z", StringComparison.Ordinal))
            new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }.CopyTo(content, 0);
        File.WriteAllBytes(path, content);
        return path;
    }

    // The archive with a 5 % set next to it, as `pakko a -rr` writes it.
    private string Protected(string name, int length = Length)
    {
        string path = Archive(name, length);
        Par2Creator.Create(path, Par2Creator.ChooseParameters(length, 5)!.Value, null, CancellationToken.None);
        return path;
    }

    private static void Damage(string path, int offset, int count)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = offset;
        stream.Write(Enumerable.Repeat((byte)0xA5, count).ToArray());
    }

    private static MessageCode? Code(ArchiveError error) => error.Text?.Code;

    private static byte[] Noise(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    // --- Happy path ---

    [Fact]
    public async Task ZipWithIntactSet_IsIntactAndClean()
    {
        string zip = Protected("a.zip");

        ArchiveResult result = await Test(zip);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.Outcome.Should().Be(OperationOutcome.Completed);
        RecoveryCheck check = result.RecoveryChecks.Should().ContainSingle().Subject;
        check.ArchivePath.Should().Be(zip);
        check.State.Should().Be(RecoveryState.Intact);
        check.Blocks.Should().Be(Par2Creator.ChooseParameters(Length, 5)!.Value.SliceCount);
        check.RecoveryBlocks.Should().Be(Par2Creator.ChooseParameters(Length, 5)!.Value.RecoveryCount);
        check.SetFiles.Should().HaveCount(2);
        _zip.Tested.Should().Equal(zip);
    }

    [Fact]
    public async Task TarWithIntactSet_IsCheckedByTheSetInsteadOfSkipped()
    {
        string tar = Protected("a.tar.gz");

        ArchiveResult result = await Test(tar);

        result.SkippedFiles.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        result.Outcome.Should().Be(OperationOutcome.Completed);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Intact);
        _zip.Tested.Should().BeEmpty();
    }

    [Fact]
    public async Task Par2Path_ChecksTheArchiveItProtectsAndIsNotSentToTheZipEngine()
    {
        string tar = Protected("a.tar.gz");

        ArchiveResult result = await Test(Par2Creator.IndexPath(tar));

        result.Errors.Should().BeEmpty();
        result.RecoveryChecks.Should().ContainSingle().Which.ArchivePath.Should().Be(tar);
        _zip.Tested.Should().BeEmpty();
    }

    [Fact]
    public async Task VolumePath_ChecksTheArchiveItProtects()
    {
        string tar = Protected("a.tar.gz");
        string volume = Par2Creator.VolumePath(tar, Par2Creator.ChooseParameters(Length, 5)!.Value.RecoveryCount);

        ArchiveResult result = await Test(volume);

        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Intact);
    }

    [Fact]
    public async Task SetNamedWithoutTheLastExtension_IsFound()
    {
        // QuickPar and MultiPar name the set for a.zip "a.par2".
        string zip = Protected("a.zip");
        File.Move(Par2Creator.IndexPath(zip), Path.Combine(_temp.Path, "a.par2"));
        string volume = Par2Creator.VolumePath(zip, Par2Creator.ChooseParameters(Length, 5)!.Value.RecoveryCount);
        File.Move(volume, Path.Combine(_temp.Path, "a" + Path.GetFileName(volume)[5..]));

        ArchiveResult result = await Test(zip);

        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Intact);
        result.Warnings.Should().BeEmpty();
    }

    // --- Flag off and policy ---

    [Fact]
    public async Task WithoutTheFlag_NoSetIsLookedForAndTarIsTestedByItsEngine()
    {
        string tar = Protected("a.tar.gz");

        ArchiveResult result = await Router().TestAsync([tar]);

        result.RecoveryChecks.Should().BeEmpty();
        _tar.Tested.Should().Equal(tar);
        result.SkippedFiles.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TestedStructureOnly);
    }

    [Fact]
    public async Task WithoutTheFlag_Par2PathGoesToTheZipEngineAsBefore()
    {
        string index = Par2Creator.IndexPath(Protected("a.tar.gz"));

        await Router().TestAsync([index]);

        _zip.Tested.Should().Equal(index);
    }

    [Fact]
    public async Task DisabledByPolicy_ArchiveIsTestedWithoutItsSet()
    {
        string tar = Protected("a.tar.gz");
        Damage(tar, 100, 10);

        ArchiveResult result = await Router(new GroupPolicyOptions { DisableRecoveryData = true }).TestAsync([tar], verifyRecoveryData: true);

        result.RecoveryChecks.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TestedStructureOnly);
    }

    [Fact]
    public async Task DisabledByPolicy_Par2PathIsRefusedWithThePolicyCode()
    {
        string index = Par2Creator.IndexPath(Protected("a.zip"));

        ArchiveResult result = await Router(new GroupPolicyOptions { DisableRecoveryData = true }).TestAsync([index], verifyRecoveryData: true);

        Code(result.Errors.Should().ContainSingle().Subject).Should().Be(MessageCode.RecoveryDataDisabled);
        result.Errors[0].SourcePath.Should().Be(index);
        _zip.Tested.Should().BeEmpty();
    }

    // --- Misuse ---

    [Fact]
    public async Task ArchiveAndItsPar2_AreCheckedOnce()
    {
        string zip = Protected("a.zip");

        ArchiveResult result = await Test(zip, Par2Creator.IndexPath(zip));

        result.RecoveryChecks.Should().ContainSingle();
        _zip.Tested.Should().Equal(zip);
    }

    [Fact]
    public async Task Par2FirstThenTheArchiveSpelledDifferently_IsOneCheckAndNoSkip()
    {
        string tar = Protected("a.tar.gz");
        string spelled = Path.Combine(_temp.Path, ".", "a.tar.gz");

        ArchiveResult result = await Test(Par2Creator.IndexPath(tar), spelled);

        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Intact);
        result.SkippedFiles.Should().BeEmpty();
        result.Outcome.Should().Be(OperationOutcome.Completed);
    }

    [Fact]
    public async Task Par2FirstThenARewrittenZipSpelledDifferently_IsStillAWarning()
    {
        string zip = Protected("a.zip");
        File.WriteAllBytes(zip, Noise(Length + 500, 7));
        string spelled = Path.Combine(_temp.Path, ".", "a.zip");

        ArchiveResult result = await Test(Par2Creator.IndexPath(zip), spelled);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataDoesNotMatch);
    }

    [Fact]
    public async Task NoSet_ResultIsTheTestAlone()
    {
        string zip = Archive("a.zip");
        string tar = Archive("b.tar.gz");

        ArchiveResult result = await Test(zip, tar);

        result.RecoveryChecks.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Match<ArchiveWarning>(
            w => w.SourcePath == tar && w.Text!.Code == MessageCode.TestedStructureOnly);
    }

    [Fact]
    public async Task RenamedArchiveAndSet_MatchByContentWithAWarning()
    {
        string original = Protected("a.zip");
        string renamed = Path.Combine(_temp.Path, "b.zip");
        File.Move(original, renamed);
        foreach (string file in Directory.GetFiles(_temp.Path, "a.zip*.par2"))
            File.Move(file, Path.Combine(_temp.Path, "b.zip" + Path.GetFileName(file)[5..]));

        ArchiveResult result = await Test(renamed);

        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Intact);
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataNameMismatch);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task SetMadeForAnotherFile_IsAWarningAndNoCheck()
    {
        string other = Protected("other.zip");
        string tar = Archive("a.tar.gz", 7000);
        foreach (string file in Directory.GetFiles(_temp.Path, "other.zip*.par2"))
            File.Move(file, Path.Combine(_temp.Path, "a.tar.gz" + Path.GetFileName(file)[9..]));
        File.Delete(other);

        ArchiveResult result = await Test(tar);

        result.RecoveryChecks.Should().BeEmpty();
        result.Warnings.Select(w => w.Text!.Code).Should().BeEquivalentTo([MessageCode.TestedStructureOnly, MessageCode.RecoveryDataForAnotherFile]);
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task ZipRewrittenWithoutASet_OldSetIsAWarningNotDamage()
    {
        // `pakko a -y a.zip` without -rr over an archive that had a set leaves the old set.
        string zip = Protected("a.zip");
        File.WriteAllBytes(zip, Noise(Length + 500, 7));

        ArchiveResult result = await Test(zip);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataDoesNotMatch);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.DoesNotMatch);
        result.Outcome.Should().Be(OperationOutcome.CompletedWithWarnings);
    }

    // --- Error path ---

    [Fact]
    public async Task TarDamagedWithinTheRecoveryData_IsARepairableError()
    {
        string tar = Protected("a.tar.gz");
        Damage(tar, 100, 10);

        ArchiveResult result = await Test(tar);

        Code(result.Errors.Should().ContainSingle().Subject).Should().Be(MessageCode.RecoveryDataDamagedRepairable);
        result.Errors[0].SourcePath.Should().Be(tar);
        RecoveryCheck check = result.RecoveryChecks.Should().ContainSingle().Subject;
        check.State.Should().Be(RecoveryState.Repairable);
        check.DamagedBlocks.Should().BeInRange(1, 3);
        result.SkippedFiles.Should().BeEmpty();
        result.Outcome.Should().Be(OperationOutcome.Failed);
    }

    [Fact]
    public async Task TarDamagedBeyondTheRecoveryData_IsANotRepairableError()
    {
        string tar = Protected("a.tar.gz");
        Damage(tar, 16, 4000);

        ArchiveResult result = await Test(tar);

        Code(result.Errors.Should().ContainSingle().Subject).Should().Be(MessageCode.RecoveryDataDamagedNotRepairable);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.NotRepairable);
    }

    [Fact]
    public async Task ZipFailingItsTestAndDamaged_ReportsBoth()
    {
        string zip = Protected("a.zip");
        Damage(zip, 100, 10);
        _zip.Failing.Add(zip);

        ArchiveResult result = await Test(zip);

        result.Errors.Select(Code).Should().BeEquivalentTo([MessageCode.ZipCorrupted, MessageCode.RecoveryDataDamagedRepairable]);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Repairable);
    }

    [Fact]
    public async Task Par2PathWhoseArchiveIsMissing_IsNotRepairable()
    {
        string tar = Protected("a.tar.gz");
        File.Delete(tar);

        ArchiveResult result = await Test(Par2Creator.IndexPath(tar));

        Code(result.Errors.Should().ContainSingle().Subject).Should().Be(MessageCode.RecoveryDataDamagedNotRepairable);
        RecoveryCheck check = result.RecoveryChecks.Should().ContainSingle().Subject;
        check.ArchivePath.Should().Be(tar);
        check.DamagedBlocks.Should().Be(check.Blocks);
    }

    [Fact]
    public async Task Par2PathWhoseArchiveCannotBeFound_IsAnError()
    {
        string other = Protected("other.zip");
        string index = Path.Combine(_temp.Path, "x.zip.par2");
        File.Move(Par2Creator.IndexPath(other), index);
        File.Delete(other);

        ArchiveResult result = await Test(index);

        Code(result.Errors.Should().ContainSingle().Subject).Should().Be(MessageCode.RecoveryDataTargetNotFound);
        result.Errors[0].SourcePath.Should().Be(index);
        result.RecoveryChecks.Should().BeEmpty();
    }

    [Fact]
    public async Task DamagedSetFiles_AreAWarningAndTheTarKeepsItsStructureOnlyWarning()
    {
        string tar = Protected("a.tar.gz");
        foreach (string file in Directory.GetFiles(_temp.Path, "*.par2"))
            File.WriteAllBytes(file, Noise((int)new FileInfo(file).Length, 3));

        ArchiveResult result = await Test(tar);

        result.Warnings.Select(w => w.Text!.Code).Should().BeEquivalentTo([MessageCode.TestedStructureOnly, MessageCode.RecoveryDataUnusable]);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Unusable);
        result.SkippedFiles.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task DamagedSetFilesGivenByPath_AreAnError()
    {
        string tar = Protected("a.tar.gz");
        string index = Par2Creator.IndexPath(tar);
        foreach (string file in Directory.GetFiles(_temp.Path, "*.par2"))
            File.WriteAllBytes(file, Noise((int)new FileInfo(file).Length, 3));

        ArchiveResult result = await Test(index);

        Code(result.Errors.Should().ContainSingle().Subject).Should().Be(MessageCode.RecoveryDataUnusable);
        result.Errors[0].SourcePath.Should().Be(index);
    }

    // --- A set left incomplete: its creation was killed, or a copy of it was cut short ---

    // Killed between the two renames: the volume is in place, the index is not. The volume carries
    // the critical packets too, so the set still works.
    [Theory]
    [InlineData(false, RecoveryState.Intact)]
    [InlineData(true, RecoveryState.Repairable)]
    public async Task SetWithoutItsIndex_StillChecksTheArchive(bool damaged, RecoveryState expected)
    {
        string tar = Protected("a.tar.gz");
        File.Delete(Par2Creator.IndexPath(tar));
        if (damaged)
            Damage(tar, 100, 10);

        ArchiveResult result = await Test(tar);

        RecoveryCheck check = result.RecoveryChecks.Should().ContainSingle().Subject;
        check.State.Should().Be(expected);
        check.RecoveryBlocks.Should().Be(Par2Creator.ChooseParameters(Length, 5)!.Value.RecoveryCount);
        result.SkippedFiles.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, RecoveryState.Intact)]
    [InlineData(true, RecoveryState.NotRepairable)]
    public async Task SetWithoutItsVolume_ChecksButCannotRepair(bool damaged, RecoveryState expected)
    {
        string tar = Protected("a.tar.gz");
        File.Delete(Par2Creator.VolumePath(tar, Par2Creator.ChooseParameters(Length, 5)!.Value.RecoveryCount));
        if (damaged)
            Damage(tar, 100, 10);

        ArchiveResult result = await Test(tar);

        RecoveryCheck check = result.RecoveryChecks.Should().ContainSingle().Subject;
        check.State.Should().Be(expected);
        check.RecoveryBlocks.Should().Be(0);
    }

    // Every length a half-copied volume can have: the check never throws, an intact archive stays
    // intact, and a damaged one is never promised more recovery blocks than survived.
    [Fact]
    public async Task VolumeCutShortAtAnyLength_NeverThrowsAndNeverOverpromises()
    {
        string tar = Protected("a.tar.gz");
        int recoveryCount = Par2Creator.ChooseParameters(Length, 5)!.Value.RecoveryCount;
        string volume = Par2Creator.VolumePath(tar, recoveryCount);
        byte[] whole = File.ReadAllBytes(volume);
        byte[] intact = File.ReadAllBytes(tar);

        for (int length = 0; length < whole.Length; length += 97)
        {
            File.WriteAllBytes(volume, whole.AsSpan(0, length).ToArray());
            File.WriteAllBytes(tar, intact);
            (await Test(tar)).RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Intact, "length {0}", length);

            Damage(tar, 100, 10);
            RecoveryCheck check = (await Test(tar)).RecoveryChecks.Should().ContainSingle().Subject;
            check.State.Should().BeOneOf(RecoveryState.Repairable, RecoveryState.NotRepairable);
            check.RecoveryBlocks.Should().BeLessThanOrEqualTo(recoveryCount);
            if (check.State == RecoveryState.Repairable)
                check.RecoveryBlocks.Should().BeGreaterThanOrEqualTo(check.DamagedBlocks);
        }
    }

    // Killed while the set was being written: only the writer's temporary files exist. They are
    // whole PAR2 files under another name, and no rule finds them.
    [Fact]
    public async Task TemporaryFilesOfAnInterruptedRun_AreNotASet()
    {
        string tar = Protected("a.tar.gz");
        int n = 0;
        foreach (string file in Directory.GetFiles(_temp.Path, "*.par2"))
            File.Move(file, Path.Combine(_temp.Path, $".pakko-a-{n++:x8}.tmp"));
        Damage(tar, 100, 10);

        ArchiveResult result = await Test(tar);

        result.RecoveryChecks.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TestedStructureOnly);
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveLockedByAnotherProgram_IsAnErrorNotAThrow()
    {
        string tar = Protected("a.tar.gz");
        using FileStream locked = File.Open(tar, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ArchiveResult result = await Test(tar);

        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(tar);
    }

    [Fact]
    public async Task FolderThatCannotBeListed_IsNoSetNotAThrow()
    {
        string tar = Protected("a.tar.gz");
        ArchiveResult result;
        using (new DeniedFolder(_temp.Path))
            result = await Test(tar);

        result.RecoveryChecks.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TestedStructureOnly);
    }

    // --- T-F397: the tar engine's own test beside the set ---

    // The set compares every byte, so "only the structure was checked" would no longer be true.
    [Fact]
    public async Task TarWithIntactSet_HasNoStructureOnlyWarning()
    {
        string tar = Protected("a.tar.gz");

        ArchiveResult result = await Test(tar);

        _tar.Tested.Should().Equal(tar);
        result.Warnings.Should().BeEmpty();
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Intact);
        result.Outcome.Should().Be(OperationOutcome.Completed);
    }

    // tar.exe reads a tar.gz with changed bytes to its end and reports nothing. That pass must not
    // count as "the archive is fine and the set is merely older": the set is right, it is damage.
    [Fact]
    public async Task TarPassingAStructureOnlyTestWhileTheSetDisagrees_IsDamageNotAnOlderSet()
    {
        string tar = Protected("a.tar.gz");
        Damage(tar, 100, 10);

        ArchiveResult result = await Test(tar);

        _tar.Tested.Should().Equal(tar);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Repairable);
        result.Errors.Should().ContainSingle();
    }

    // A 7z passes a test that does verify its contents: the same rule as for a ZIP then holds.
    [Fact]
    public async Task VerifiedTarFamilyArchiveRewrittenWithoutASet_OldSetIsAWarningNotDamage()
    {
        var router = new ExtractionRouter(_zip, _tar, new TarCapabilities { Supports7z = true }, new GroupPolicyOptions());
        string sevenZip = Protected("a.7z");
        Damage(sevenZip, 100, 10);

        ArchiveResult result = await router.TestAsync([sevenZip], verifyRecoveryData: true);

        _tar.Tested.Should().Equal(sevenZip);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataDoesNotMatch);
    }

    // T-F410: a test of only the entries a filter passed did not read the rest, so the set
    // disagreeing with a ZIP that passed it is damage, not an older set.
    [Fact]
    public async Task ZipPassingAFilteredTestWhileTheSetDisagrees_IsDamageNotAnOlderSet()
    {
        string zip = Protected("a.zip");
        Damage(zip, 100, 10);
        Func<string, bool, bool> filter = (_, _) => true;

        ArchiveResult result = await Router().TestAsync([zip], verifyRecoveryData: true, entryFilter: filter);

        _zip.Filter.Should().BeSameAs(filter);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Repairable);
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Filter_ReachesBothEngines()
    {
        string zip = Archive("a.zip");
        string tar = Archive("a.tar.gz");
        Func<string, bool, bool> filter = (_, _) => true;

        await Router().TestAsync([zip, tar], entryFilter: filter);

        _zip.Filter.Should().BeSameAs(filter);
        _tar.Filter.Should().BeSameAs(filter);
    }

    [Fact]
    public async Task TarFailingItsTestAndDamaged_ReportsBoth()
    {
        string tar = Protected("a.tar.gz");
        Damage(tar, 100, 10);
        _tar.Failing.Add(tar);

        ArchiveResult result = await Test(tar);

        result.Errors.Select(Code).Should().Contain(MessageCode.CannotReadArchive);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Repairable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\0b.zip")]
    public async Task UnusablePath_GoesToTheEngineWithoutAThrow(string path)
    {
        ArchiveResult result = await Test(path);

        result.RecoveryChecks.Should().BeEmpty();
        _zip.Tested.Should().Equal(path);
    }

    [Fact]
    public async Task Par2PathInAFolderThatCannotBeListed_IsAnErrorNotAThrow()
    {
        string index = Par2Creator.IndexPath(Protected("a.tar.gz"));
        ArchiveResult result;
        using (new DeniedFolder(_temp.Path))
            result = await Test(index);

        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(index);
    }

    // --- Concurrency: progress and cancellation ---

    [Fact]
    public async Task Progress_ClimbsOnceToOneHundredOverTheTestAndTheCheck()
    {
        string zip = Protected("a.zip");
        string tar = Protected("b.tar.gz");
        var progress = new RecordingProgress();

        await Router().TestAsync([zip, tar], progress, verifyRecoveryData: true);

        int[] percents = [.. progress.Reports.Select(r => r.Percent)];
        percents.Should().BeInAscendingOrder();
        percents[^1].Should().Be(100);
        progress.Reports.Should().Contain(r => r.Phase == ProgressPhase.VerifyingRecoveryData && r.Percent < 100);
        progress.Reports.Where(r => r.Phase == ProgressPhase.VerifyingRecoveryData).Should().OnlyContain(r => r.TotalBytes == 0);
    }

    [Fact]
    public async Task Cancellation_Throws()
    {
        string tar = Protected("a.tar.gz");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => Router().TestAsync([tar], verifyRecoveryData: true, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
