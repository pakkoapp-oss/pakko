using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Recovery;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Recovery;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F275 step 2: an engine that writes real archive files, so the router's PAR2 step runs for real.
internal sealed class RecoveryWritingEngine : IArchiveService, ITarService
{
    public Func<ArchiveOptions, IProgress<ProgressReport>?, ArchiveResult> Produce = (_, _) => new ArchiveResult();
    public ArchiveOptions? LastOptions;
    public int Calls;

    private Task<ArchiveResult> Run(ArchiveOptions options, IProgress<ProgressReport>? progress)
    {
        Calls++;
        LastOptions = options;
        return Task.FromResult(Produce(options, progress));
    }

    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) => Run(options, progress);

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) => Run(options, progress);

    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

// Synchronous, unlike Progress<T>, so a test sees every report in order and on the reporting thread.
internal sealed class RecordingProgress(Action<ProgressReport>? onReport = null) : IProgress<ProgressReport>
{
    public List<ProgressReport> Reports { get; } = [];
    public List<bool> OnPoolThread { get; } = [];

    public void Report(ProgressReport value)
    {
        lock (Reports)
        {
            Reports.Add(value);
            OnPoolThread.Add(Thread.CurrentThread.IsThreadPoolThread);
        }
        onReport?.Invoke(value);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ArchiveCreationRouterRecoveryTests
{
    private static ArchiveCreationRouter Router(RecoveryWritingEngine engine, GroupPolicyOptions? policy = null) =>
        new(engine, engine, policy ?? new GroupPolicyOptions());

    private static ArchiveOptions Options(TempDirectory temp, int percent, ArchiveContainerFormat format = ArchiveContainerFormat.Zip) => new()
    {
        SourcePaths = [Path.Combine(temp.Path, "src")],
        DestinationFolder = temp.Path,
        Format = format,
        RecoveryPercent = percent,
    };

    // The engine's result for one written archive whose single source completed.
    private static ArchiveResult Written(string archivePath, string source, int length = 300_001)
    {
        File.WriteAllBytes(archivePath, Par2TestData.Content(length));
        return new ArchiveResult
        {
            CreatedFiles = [archivePath],
            Sources = [new SourceResult { Path = source, Outcome = SourceOutcome.Completed }],
        };
    }

    private static Par2VerifyStatus VerifyStatus(string archivePath)
    {
        Par2ReadResult read = Par2PacketReader.Read(Par2SetLocator.SetFilesForTarget(archivePath), CancellationToken.None);
        Par2Match match = Par2SetLocator.Select(read.Sets, archivePath)!;
        return Par2Verifier.Verify(archivePath, match.Set, null, CancellationToken.None).Status;
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip, "a.zip")]
    [InlineData(ArchiveContainerFormat.TarGz, "a.tar.gz")]
    public async Task RecoveryPercent_WritesASetNextToTheArchiveThatVerifiesIntact(ArchiveContainerFormat format, string name)
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, name);
        var engine = new RecoveryWritingEngine { Produce = (o, _) => Written(archive, o.SourcePaths[0]) };

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 5, format));

        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(archive);
        int recoveryCount = Par2Creator.ChooseParameters(300_001, 5)!.Value.RecoveryCount;
        result.RecoveryFiles.Should().Equal(Par2Creator.IndexPath(archive), Par2Creator.VolumePath(archive, recoveryCount));
        result.RecoveryFiles.Should().OnlyContain(f => File.Exists(f));
        VerifyStatus(archive).Should().Be(Par2VerifyStatus.Intact);
        result.FullyProcessedSources.Should().ContainSingle();
    }

    [Fact]
    public async Task RecoveryPercent_RecoveryBlockCountFollowsThePercent()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        var engine = new RecoveryWritingEngine { Produce = (o, _) => Written(archive, o.SourcePaths[0]) };

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 20));

        Par2Parameters expected = Par2Creator.ChooseParameters(300_001, 20)!.Value;
        result.RecoveryFiles[1].Should().Be(Par2Creator.VolumePath(archive, expected.RecoveryCount));
    }

    [Fact]
    public async Task ZeroPercent_PassesTheEngineResultAndOptionsThroughUntouched()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        ArchiveResult engineResult = null!;
        var engine = new RecoveryWritingEngine { Produce = (o, _) => engineResult = Written(archive, o.SourcePaths[0]) };
        ArchiveOptions options = Options(temp, 0) with { OpenDestinationFolder = true };

        ArchiveResult result = await Router(engine).ArchiveAsync(options);

        result.Should().BeSameAs(engineResult);
        engine.LastOptions.Should().BeSameAs(options);
        Directory.GetFiles(temp.Path).Should().Equal(archive);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public async Task PercentOutOfRange_RefusedBeforeTheEngineRuns(int percent)
    {
        using var temp = new TempDirectory();
        var engine = new RecoveryWritingEngine();

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, percent));

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryPercentInvalid);
        engine.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DisableRecoveryDataPolicy_RefusesARequestBeforeTheEngineRuns()
    {
        using var temp = new TempDirectory();
        var engine = new RecoveryWritingEngine();

        ArchiveResult result = await Router(engine, new GroupPolicyOptions { DisableRecoveryData = true }).ArchiveAsync(Options(temp, 5));

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataDisabled);
        engine.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DisableRecoveryDataPolicy_LeavesAnArchiveWithoutRecoveryDataAlone()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        var engine = new RecoveryWritingEngine { Produce = (o, _) => Written(archive, o.SourcePaths[0]) };

        ArchiveResult result = await Router(engine, new GroupPolicyOptions { DisableRecoveryData = true }).ArchiveAsync(Options(temp, 0));

        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(archive);
    }

    [Fact]
    public async Task ArchiveWrittenWithAnError_StillGetsItsSet()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        ArchiveError unreadable = new() { SourcePath = "x", Message = "unreadable" };
        var engine = new RecoveryWritingEngine { Produce = (o, _) => Written(archive, o.SourcePaths[0]) with { Errors = [unreadable] } };

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 5));

        result.Errors.Should().Equal(unreadable);
        result.RecoveryFiles.Should().HaveCount(2);
        VerifyStatus(archive).Should().Be(Par2VerifyStatus.Intact);
    }

    [Fact]
    public async Task SeparateArchives_EachArchiveGetsItsOwnSet()
    {
        using var temp = new TempDirectory();
        string first = Path.Combine(temp.Path, "one.zip");
        string second = Path.Combine(temp.Path, "two.zip");
        var engine = new RecoveryWritingEngine
        {
            Produce = (_, _) =>
            {
                File.WriteAllBytes(first, Par2TestData.Content(5000));
                File.WriteAllBytes(second, Par2TestData.Content(70_000));
                return new ArchiveResult { CreatedFiles = [first, second] };
            },
        };

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 10) with { Mode = ArchiveMode.SeparateArchives });

        result.Errors.Should().BeEmpty();
        result.RecoveryFiles.Should().HaveCount(4);
        VerifyStatus(first).Should().Be(Par2VerifyStatus.Intact);
        VerifyStatus(second).Should().Be(Par2VerifyStatus.Intact);
    }

    [Fact]
    public async Task NothingCreated_NoRecoveryData()
    {
        using var temp = new TempDirectory();
        string existing = temp.CreateFile("a.zip");
        var engine = new RecoveryWritingEngine
        {
            Produce = (o, _) => new ArchiveResult { SkippedFiles = [new SkippedFile { Path = o.SourcePaths[0], Reason = "exists" }] },
        };

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 5));

        result.RecoveryFiles.Should().BeEmpty();
        Directory.GetFiles(temp.Path).Should().Equal(existing);
    }

    [Fact]
    public async Task RecoveryDataFails_ErrorNamesTheArchive_ArchiveKept_SourcesNotDeletable()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        string vanished = Path.Combine(temp.Path, "gone.zip");
        var engine = new RecoveryWritingEngine
        {
            Produce = (o, _) => Written(archive, o.SourcePaths[0]) with { CreatedFiles = [archive, vanished] },
        };

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 5));

        ArchiveError error = result.Errors.Should().ContainSingle().Subject;
        error.SourcePath.Should().Be(vanished);
        error.Text!.Code.Should().Be(MessageCode.RecoveryDataNotCreated);
        result.CreatedFiles.Should().Equal(archive, vanished);
        result.RecoveryFiles.Should().HaveCount(2, "the archive that exists is still protected");
        result.FullyProcessedSources.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
    }

    [Fact]
    public async Task EmptyArchive_ErrorWithItsOwnCode()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        var engine = new RecoveryWritingEngine { Produce = (o, _) => Written(archive, o.SourcePaths[0], length: 0) };

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 5));

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.RecoveryDataFileTooLarge);
        File.Exists(archive).Should().BeTrue();
        result.RecoveryFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelledWhileCreatingRecoveryData_Throws_ArchiveKept_NoPartialFiles()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        var engine = new RecoveryWritingEngine { Produce = (o, _) => Written(archive, o.SourcePaths[0], length: 2_000_000) };
        using var cts = new CancellationTokenSource();
        var progress = new RecordingProgress(r =>
        {
            if (r.Phase == ProgressPhase.CreatingRecoveryData)
                cts.Cancel();
        });

        Func<Task> act = () => Router(engine).ArchiveAsync(Options(temp, 20), progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(temp.Path).Should().Equal(archive);
    }

    [Fact]
    public async Task Progress_RisesMonotonicallyTo100_WhichComesOnlyAfterTheSet()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        var engine = new RecoveryWritingEngine
        {
            Produce = (o, p) =>
            {
                foreach (int percent in new[] { 0, 50, 100 })
                    p?.Report(new ProgressReport { Percent = percent, BytesTransferred = percent * 10, TotalBytes = 1000 });
                return Written(archive, o.SourcePaths[0], length: 1_000_000);
            },
        };
        var progress = new RecordingProgress(r =>
        {
            if (r.Percent == 100)
                File.Exists(Par2Creator.IndexPath(archive)).Should().BeTrue("100 % is reported after the set is written");
        });

        await Router(engine).ArchiveAsync(Options(temp, 5), progress);

        List<int> percents = [.. progress.Reports.Select(r => r.Percent)];
        percents.Should().BeInAscendingOrder();
        percents[^1].Should().Be(100);
        percents.Count(p => p == 100).Should().Be(1);
        progress.Reports.Take(3).Should().OnlyContain(r => r.Phase == ProgressPhase.Transferring && r.Percent < 100);
        ProgressReport[] recovery = [.. progress.Reports.Where(r => r.Phase == ProgressPhase.CreatingRecoveryData)];
        recovery.Should().NotBeEmpty().And.OnlyContain(r => r.TotalBytes == 0);
        recovery.Select(r => r.Percent).Should().OnlyHaveUniqueItems("a report is passed on only when the percent changes");
    }

    [Fact]
    public void RecoveryData_RunsOffTheCallersThread()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        var engine = new RecoveryWritingEngine { Produce = (o, _) => Written(archive, o.SourcePaths[0]) };
        var progress = new RecordingProgress();
        Exception? failure = null;

        var caller = new Thread(() =>
        {
            try { Router(engine).ArchiveAsync(Options(temp, 5), progress).GetAwaiter().GetResult(); }
            catch (Exception ex) { failure = ex; }
        });
        caller.Start();
        caller.Join();

        failure.Should().BeNull();
        progress.Reports.Select((r, i) => (r, i))
            .Where(x => x.r.Phase == ProgressPhase.CreatingRecoveryData)
            .Should().NotBeEmpty()
            .And.OnlyContain(x => progress.OnPoolThread[x.i], "the UI thread awaits the router");
    }

    // The run fails (the second archive does not exist), so the router itself opens nothing either
    // and the test run starts no Explorer window.
    [Fact]
    public async Task RecoveryPercent_TheEngineNeverOpensTheFolder()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        string vanished = Path.Combine(temp.Path, "gone.zip");
        var engine = new RecoveryWritingEngine
        {
            Produce = (o, _) => Written(archive, o.SourcePaths[0]) with { CreatedFiles = [archive, vanished] },
        };

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 5) with { OpenDestinationFolder = true });

        result.Success.Should().BeFalse();
        engine.LastOptions!.OpenDestinationFolder.Should().BeFalse();
        engine.LastOptions.RecoveryPercent.Should().Be(5);
    }

    [Fact]
    public async Task StaleVolumeOfAnEarlierSet_Removed_OtherFilesKept()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        File.WriteAllBytes(archive, Par2TestData.Content(9000));
        Par2CreateResult old = Par2Creator.Create(archive, Par2Creator.ChooseParameters(9000, 100)!.Value, null, CancellationToken.None);
        string garbageVolume = temp.CreateFile("a.zip.vol7+7.par2", "not a PAR2 file");
        string notes = temp.CreateFile("a.zip.notes.par2", "someone else's");
        string otherArchiveVolume = temp.CreateFile("b.zip.vol0+1.par2", "another archive's");
        var engine = new RecoveryWritingEngine { Produce = (o, _) => Written(archive, o.SourcePaths[0]) };

        ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 5));

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        File.Exists(old.VolumePath).Should().BeFalse();
        Directory.GetFiles(temp.Path).Should().BeEquivalentTo(
            [archive, .. result.RecoveryFiles, garbageVolume, notes, otherArchiveVolume]);
        VerifyStatus(archive).Should().Be(Par2VerifyStatus.Intact);
    }

    // The set is written before the stale-volume step: a folder that cannot be listed then must not
    // turn a written set into "not created" (nor take the sources' Completed away).
    [Fact]
    public async Task FolderCannotBeListed_SetStillWritten_NoErrorNoWarning()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        DeniedFolder? denied = null;
        var engine = new RecoveryWritingEngine
        {
            Produce = (o, _) =>
            {
                ArchiveResult written = Written(archive, o.SourcePaths[0]);
                denied = new DeniedFolder(temp.Path);
                return written;
            },
        };
        ArchiveResult result;
        try
        {
            result = await Router(engine).ArchiveAsync(Options(temp, 5));
        }
        finally
        {
            denied?.Dispose();
        }

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.RecoveryFiles.Should().HaveCount(2).And.OnlyContain(f => File.Exists(f));
        result.FullyProcessedSources.Should().ContainSingle();
    }

    [Fact]
    public async Task StaleVolumeThatCannotBeDeleted_IsAWarning()
    {
        using var temp = new TempDirectory();
        string archive = Path.Combine(temp.Path, "a.zip");
        File.WriteAllBytes(archive, Par2TestData.Content(9000));
        Par2CreateResult old = Par2Creator.Create(archive, Par2Creator.ChooseParameters(9000, 100)!.Value, null, CancellationToken.None);
        File.SetAttributes(old.VolumePath, FileAttributes.ReadOnly);
        var engine = new RecoveryWritingEngine { Produce = (o, _) => Written(archive, o.SourcePaths[0]) };
        try
        {
            ArchiveResult result = await Router(engine).ArchiveAsync(Options(temp, 5));

            result.Errors.Should().BeEmpty();
            ArchiveWarning warning = result.Warnings.Should().ContainSingle().Subject;
            warning.SourcePath.Should().Be(old.VolumePath);
            warning.Text!.Code.Should().Be(MessageCode.RecoveryOldVolumeNotDeleted);
            result.FullyProcessedSources.Should().ContainSingle("a warning never makes a source undeletable");
        }
        finally
        {
            File.SetAttributes(old.VolumePath, FileAttributes.Normal);
        }
    }
}
