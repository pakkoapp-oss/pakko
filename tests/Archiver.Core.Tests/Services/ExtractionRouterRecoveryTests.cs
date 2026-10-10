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

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, CancellationToken cancellationToken = default)
    {
        Tested.AddRange(archivePaths);
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

internal sealed class UnusedTarEngine : ITarService
{
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

    public void Dispose() => _temp.Dispose();

    private ExtractionRouter Router(GroupPolicyOptions? policy = null) =>
        new(_zip, new UnusedTarEngine(), new TarCapabilities(), policy ?? new GroupPolicyOptions());

    private Task<ArchiveResult> Test(params string[] paths) =>
        Router().TestAsync(paths, verifyRecoveryData: true);

    private string Archive(string name, int length = Length)
    {
        string path = Path.Combine(_temp.Path, name);
        byte[] content = Par2TestData.Content(length);
        // The detector reads the magic: a gzip header makes "a.tar.gz" a tar-family archive.
        if (name.EndsWith(".gz", StringComparison.Ordinal))
            new byte[] { 0x1F, 0x8B, 0x08 }.CopyTo(content, 0);
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
    public async Task WithoutTheFlag_NoSetIsLookedForAndTarIsStillSkipped()
    {
        string tar = Protected("a.tar.gz");

        ArchiveResult result = await Router().TestAsync([tar]);

        result.RecoveryChecks.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.NoTestCapability);
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
        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.NoTestCapability);
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
        result.Warnings.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle().Which.Path.Should().Be(tar);
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
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataForAnotherFile);
        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.NoTestCapability);
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
    public async Task DamagedSetFiles_AreAWarningAndTarIsStillSkipped()
    {
        string tar = Protected("a.tar.gz");
        foreach (string file in Directory.GetFiles(_temp.Path, "*.par2"))
            File.WriteAllBytes(file, Noise((int)new FileInfo(file).Length, 3));

        ArchiveResult result = await Test(tar);

        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataUnusable);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Unusable);
        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.NoTestCapability);
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
        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.NoTestCapability);
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
