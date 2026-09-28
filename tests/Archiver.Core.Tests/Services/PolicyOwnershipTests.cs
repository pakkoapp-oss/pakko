using System.Reflection;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F261: a ZIP engine that records every call, so a test can prove which operations reached it.
file sealed class RecordingZipService : IArchiveService
{
    public int Calls;

    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new ArchiveResult { Success = true });
    }

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new ArchiveResult { Success = true });
    }

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new ArchiveResult { Success = true });
    }

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new ArchiveListResult { Success = true });
    }
}

// T-F261: every tar.exe-starting call is counted and fails loudly. The probe answers "everything
// supported", so only Group Policy can keep an archive away from tar.exe.
file sealed class TarMustNotRunService : ITarService
{
    public int ProbeCalls;
    public int TarCalls;

    public Task<TarCapabilities> DetectCapabilitiesAsync()
    {
        ProbeCalls++;
        return Task.FromResult(new TarCapabilities
        {
            SupportsRar = true, Supports7z = true, SupportsZstd = true, SupportsXz = true,
            SupportsLzma = true, SupportsBz2 = true, Version = "bsdtar 3.8.4",
        });
    }

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        TarCalls++;
        throw new InvalidOperationException("tar.exe must not be started for extraction here.");
    }

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        TarCalls++;
        throw new InvalidOperationException("tar.exe must not be started for listing here.");
    }

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        TarCalls++;
        throw new InvalidOperationException("tar.exe must not be started for creation here.");
    }
}

/// <summary>
/// T-F261: Group Policy has one owner — every engine and router requires it, the tar engine
/// refuses on its own when tar.exe is disabled (also for the unsandboxed version probe), and the
/// PakkoServices factory hands the one policy to everything it builds.
/// </summary>
public sealed class PolicyOwnershipTests : IDisposable
{
    private const string TarDisabledMessage = "tar.exe-based extraction is disabled by Group Policy.";
    private static readonly GroupPolicyOptions TarDisabled = new() { DisableTarExtraction = true };

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Missing(string name) => Path.Combine(_temp.Path, "missing", name);

    private string WriteTar(string name)
    {
        byte[] header = new byte[512];
        "ustar"u8.CopyTo(header.AsSpan(257));
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, header);
        return path;
    }

    private string WriteZip(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);
        return path;
    }

    // --- Security & Boundary: the policy is never optional ---

    [Theory]
    [InlineData(typeof(ZipArchiveService))]
    [InlineData(typeof(TarSandboxedService))]
    [InlineData(typeof(ExtractionRouter))]
    [InlineData(typeof(ArchiveCreationRouter))]
    [InlineData(typeof(ArchiveListingRouter))]
    [InlineData(typeof(AntivirusScanService))]
    public void EveryPublicConstructor_RequiresANonNullPolicy(Type type)
    {
        var nullability = new NullabilityInfoContext();
        foreach (ConstructorInfo constructor in type.GetConstructors())
        {
            ParameterInfo policy = constructor.GetParameters().Should()
                .ContainSingle(p => p.ParameterType == typeof(GroupPolicyOptions), $"{type.Name} must take the policy").Subject;
            policy.IsOptional.Should().BeFalse($"{type.Name} must not default to 'allow everything'");
            nullability.Create(policy).WriteState.Should().Be(NullabilityState.NotNull);
        }
    }

    [Fact]
    public void TarEngine_NullPolicy_Throws()
    {
        Action act = () => _ = new TarSandboxedService(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- The tar engine gates itself (covers the App's DI, which never uses the factory) ---

    [Fact]
    public async Task DetectCapabilities_TarDisabled_NeverRunsTheProbe()
    {
        int probes = 0;
        var sut = new TarSandboxedService(TarDisabled, () => { probes++; return Task.FromResult(new TarCapabilities { Version = "x" }); });

        TarCapabilities result = await sut.DetectCapabilitiesAsync();

        probes.Should().Be(0);
        result.Should().Be(new TarCapabilities());
    }

    [Fact]
    public async Task DetectCapabilities_TarAllowed_RunsTheProbe()
    {
        int probes = 0;
        var expected = new TarCapabilities { Version = "bsdtar 3.8.4" };
        var sut = new TarSandboxedService(new GroupPolicyOptions(), () => { probes++; return Task.FromResult(expected); });

        TarCapabilities result = await sut.DetectCapabilitiesAsync();

        probes.Should().Be(1);
        result.Should().Be(expected);
    }

    [Fact]
    public async Task TarList_TarDisabled_RefusesWithThePolicyReason()
    {
        var sut = new TarSandboxedService(TarDisabled);

        ArchiveListResult result = await sut.ListEntriesAsync(Missing("a.7z"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(TarDisabledMessage);
    }

    [Fact]
    public async Task TarExtract_TarDisabled_RefusesEveryArchiveAndCreatesNoDestination()
    {
        var sut = new TarSandboxedService(TarDisabled);
        string dest = Path.Combine(_temp.Path, "out");
        string first = Missing("a.7z"), second = Missing("b.rar");

        ArchiveResult result = await sut.ExtractAsync(new ExtractOptions { ArchivePaths = [first, second], DestinationFolder = dest });

        result.Success.Should().BeFalse();
        result.Errors.Select(e => (e.SourcePath, e.Message)).Should().Equal((first, TarDisabledMessage), (second, TarDisabledMessage));
        result.Sources.Should().OnlyContain(s => s.Outcome == SourceOutcome.NotProcessed);
        Directory.Exists(dest).Should().BeFalse();
    }

    [Fact]
    public async Task TarCompress_TarDisabled_RefusesAndWritesNothing()
    {
        var sut = new TarSandboxedService(TarDisabled);
        string source = Path.Combine(_temp.Path, "in.txt");
        File.WriteAllText(source, "data");
        string dest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(dest);

        ArchiveResult result = await sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [source], DestinationFolder = dest, ArchiveName = "x", Format = ArchiveContainerFormat.Tar,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Be("tar.exe-based archive creation is disabled by Group Policy.");
        Directory.EnumerateFileSystemEntries(dest).Should().BeEmpty();
    }

    // --- The factory ---

    [Fact]
    public async Task Factory_RealEnginesUnderTarDisabled_NeitherProbesNorListsWithTar()
    {
        var services = PakkoServices.Create(TarDisabled);

        TarCapabilities capabilities = await services.GetTarCapabilitiesAsync();
        ArchiveListResult list = await services.TarService.ListEntriesAsync(Missing("a.tar"));

        services.Policy.Should().BeSameAs(TarDisabled);
        capabilities.Should().Be(new TarCapabilities());
        list.ErrorMessage.Should().Be(TarDisabledMessage);
    }

    [Fact]
    public async Task Factory_ProbesTarAtMostOnceAcrossRouters()
    {
        var tar = new TarMustNotRunService();
        var services = PakkoServices.Create(new GroupPolicyOptions(), new RecordingZipService(), tar);

        await services.CreateExtractionRouterAsync();
        await services.CreateListingRouterAsync();
        await services.GetTarCapabilitiesAsync();

        tar.ProbeCalls.Should().Be(1);
    }

    [Fact]
    public void Factory_CreationDoesNotProbe()
    {
        var tar = new TarMustNotRunService();
        var services = PakkoServices.Create(new GroupPolicyOptions(), new RecordingZipService(), tar);

        _ = services.CreationRouter;

        tar.ProbeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Factory_TarDisabled_ExtractTestAndCreateNeverReachTar()
    {
        var zip = new RecordingZipService();
        var tar = new TarMustNotRunService();
        var services = PakkoServices.Create(TarDisabled, zip, tar);
        string archive = WriteTar("a.tar");
        IExtractionRouter router = await services.CreateExtractionRouterAsync();

        ArchiveResult extracted = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [archive], DestinationFolder = _temp.Path });
        ArchiveResult tested = await router.TestAsync([archive]);
        ArchiveResult created = await services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [archive], DestinationFolder = _temp.Path, ArchiveName = "x", Format = ArchiveContainerFormat.TarGz,
        });

        tar.TarCalls.Should().Be(0);
        zip.Calls.Should().Be(0);
        extracted.SkippedFiles.Should().ContainSingle().Which.Reason.Should().Be(TarDisabledMessage);
        tested.SkippedFiles.Should().ContainSingle().Which.Reason.Should().Be(TarDisabledMessage);
        created.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Factory_ZipBlocked_ExtractTestAndCreateNeverReachTheZipEngine()
    {
        var zip = new RecordingZipService();
        var services = PakkoServices.Create(new GroupPolicyOptions { BlockedFormats = ["zip"] }, zip, new TarMustNotRunService());
        string archive = WriteZip("a.zip");
        IExtractionRouter router = await services.CreateExtractionRouterAsync();

        ArchiveResult extracted = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [archive], DestinationFolder = _temp.Path });
        ArchiveResult tested = await router.TestAsync([archive]);
        ArchiveResult created = await services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [archive], DestinationFolder = _temp.Path, ArchiveName = "x", Format = ArchiveContainerFormat.Zip,
        });

        zip.Calls.Should().Be(0);
        extracted.SkippedFiles.Should().ContainSingle().Which.Reason.Should().Contain("blocked by Group Policy");
        tested.SkippedFiles.Should().ContainSingle().Which.Reason.Should().Contain("blocked by Group Policy");
        created.Success.Should().BeFalse();
    }

    // --- T-F250: every operation, under both tar-refusing policies, never reaches tar.exe ---

    public static TheoryData<string, string> TarRefusingPolicies => new()
    {
        { "BlockedFormats=tar", "extract" }, { "BlockedFormats=tar", "create" },
        { "BlockedFormats=tar", "list" }, { "BlockedFormats=tar", "test" },
        { "DisableTarExtraction", "extract" }, { "DisableTarExtraction", "create" },
        { "DisableTarExtraction", "list" }, { "DisableTarExtraction", "test" },
    };

    private static GroupPolicyOptions TarRefusing(string policy) => policy == "DisableTarExtraction"
        ? TarDisabled
        : new GroupPolicyOptions { BlockedFormats = ["tar"] };

    [Theory]
    [MemberData(nameof(TarRefusingPolicies))]
    public async Task Factory_TarRefusedByPolicy_OperationNeverReachesTar(string policy, string operation)
    {
        var zip = new RecordingZipService();
        var tar = new TarMustNotRunService();
        var services = PakkoServices.Create(TarRefusing(policy), zip, tar);
        string archive = WriteTar("a.tar");

        bool refused = operation switch
        {
            "extract" => (await (await services.CreateExtractionRouterAsync())
                .ExtractAsync(new ExtractOptions { ArchivePaths = [archive], DestinationFolder = _temp.Path })).SkippedFiles.Count == 1,
            "test" => (await (await services.CreateExtractionRouterAsync()).TestAsync([archive])).SkippedFiles
                .Single().Reason.Contains("Group Policy"),
            "list" => (await (await services.CreateListingRouterAsync()).ListEntriesAsync(archive)).ErrorMessage!
                .Contains("Group Policy"),
            _ => !(await services.CreationRouter.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [archive], DestinationFolder = _temp.Path, ArchiveName = "x", Format = ArchiveContainerFormat.Tar,
            })).Success,
        };

        refused.Should().BeTrue();
        tar.TarCalls.Should().Be(0);
        zip.Calls.Should().Be(0);
    }

    // Scan opens the sandbox directly, not through ITarService, so the shared classifier is its
    // only tar gate. With no AV provider registered, a path that got past the classifier would be
    // reported as "No antivirus is registered" instead — without starting tar.exe.
    [Theory]
    [InlineData("BlockedFormats=tar")]
    [InlineData("DisableTarExtraction")]
    public async Task Scan_TarRefusedByPolicy_ReportsThePolicyAndOpensNoScanner(string policy)
    {
        string archive = WriteTar("a.tar");
        var scanner = new AntivirusScanService(new TarCapabilities(), TarRefusing(policy),
            () => throw new InvalidOperationException("no scanner may be opened"), () => false);

        ThreatScanResult result = await scanner.ScanAsync(new AntivirusScanOptions { ArchivePaths = [archive] });

        result.Findings.Should().ContainSingle().Which.Reason.Should().Contain("Group Policy");
    }

    [Fact]
    public async Task Factory_ListingRouter_GetsThePolicy()
    {
        var tar = new TarMustNotRunService();
        var services = PakkoServices.Create(TarDisabled, new RecordingZipService(), tar);

        ArchiveListResult result = await (await services.CreateListingRouterAsync()).ListEntriesAsync(WriteTar("a.tar"));

        result.ErrorMessage.Should().Be(TarDisabledMessage);
        tar.TarCalls.Should().Be(0);
    }

    // --- The shared classifier ---

    [Fact]
    public void GetRefusalReason_TarDisabledAndNeverProbed_NamesThePolicyNotTheCapability()
    {
        CoreText? reason = ArchiveFormatPolicy.GetRefusalReason(ArchiveFormat.SevenZip, new TarCapabilities(), TarDisabled);

        reason!.Code.Should().Be(MessageCode.TarExtractionDisabled);
        reason.English.Should().Be(TarDisabledMessage);
    }

    [Fact]
    public void GetRefusalReason_UnknownFormat_IsLeftToTheZipEngine()
    {
        ArchiveFormatPolicy.GetRefusalReason(ArchiveFormat.Unknown, new TarCapabilities(), new GroupPolicyOptions { AllowedFormats = ["tar"] })
            .Should().BeNull();
    }

    [Theory]
    [InlineData(ArchiveFormat.Zip, false)]
    [InlineData(ArchiveFormat.Rar, true)]
    [InlineData(ArchiveFormat.Unknown, false)]
    public void IsBlockedByPolicy_TarDisabled_BlocksOnlyTarFamily(ArchiveFormat format, bool blocked)
    {
        ArchiveFormatPolicy.IsBlockedByPolicy(format, TarDisabled).Should().Be(blocked);
    }
}
