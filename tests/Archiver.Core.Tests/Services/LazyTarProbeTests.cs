using System.Text;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

file sealed class QuietZipService : IArchiveService
{
    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ArchiveResult());

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ArchiveResult());

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ArchiveResult());

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => Task.FromResult(new ArchiveListResult { Success = true });
}

// Counts the tar.exe --version probe; the engine calls succeed without starting anything.
file sealed class CountingTarService : ITarService
{
    public int ProbeCalls;

    public Task<TarCapabilities> DetectCapabilitiesAsync()
    {
        ProbeCalls++;
        return Task.FromResult(LazyTarProbeTests.EverythingSupported);
    }

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ArchiveResult());

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => Task.FromResult(new ArchiveListResult { Success = true });

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

/// <summary>
/// T-F350: Shell and the CLI probe tar.exe only when the selection holds an archive whose verdict
/// depends on what tar.exe can read — never for ZIP, and never for a format Group Policy already
/// refuses. What is accepted or refused, and with which text, stays as it was.
/// </summary>
public sealed class LazyTarProbeTests : IDisposable
{
    internal static readonly TarCapabilities EverythingSupported = new()
    {
        SupportsRar = true, Supports7z = true, SupportsZstd = true, SupportsXz = true,
        SupportsLzma = true, SupportsBz2 = true, Version = "bsdtar 3.8.4",
    };

    private static readonly (string Name, byte[] Magic)[] Formats =
    [
        ("zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]),
        ("tar", TarHeader()),
        ("gz", [0x1F, 0x8B, 8, 0]),
        ("bz2", [0x42, 0x5A, 0x68, 0x39]),
        ("xz", [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00]),
        ("zst", [0x28, 0xB5, 0x2F, 0xFD]),
        ("rar", [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]),
        ("7z", [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]),
        ("unknown", [1, 2, 3, 4, 5, 6, 7, 8]),
    ];

    private static readonly (string Name, GroupPolicyOptions Policy)[] Policies =
    [
        ("no policy", new GroupPolicyOptions()),
        ("blocked rar+zip", new GroupPolicyOptions { BlockedFormats = ["rar", "zip"] }),
        ("allowed zip+tar+7z", new GroupPolicyOptions { AllowedFormats = ["zip", "tar", "7z"] }),
        ("tar disabled", new GroupPolicyOptions { DisableTarExtraction = true }),
    ];

    private static readonly (string Name, TarCapabilities Capabilities)[] CapabilitySets =
    [
        ("new tar", EverythingSupported),
        ("no tar", new TarCapabilities()),
    ];

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static byte[] TarHeader()
    {
        byte[] header = new byte[512];
        "ustar"u8.CopyTo(header.AsSpan(257));
        return header;
    }

    private string Write(string format)
    {
        string path = Path.Combine(_temp.Path, "a." + format);
        File.WriteAllBytes(path, Formats.Single(f => f.Name == format).Magic);
        return path;
    }

    private async Task RunAsync(PakkoServices services, string operation, params string[] paths)
    {
        switch (operation)
        {
            case "extract":
                await (await services.CreateExtractionRouterAsync()).ExtractAsync(new ExtractOptions
                {
                    ArchivePaths = paths, DestinationFolder = _temp.Path, OpenDestinationFolder = false,
                });
                break;
            case "test":
                await (await services.CreateExtractionRouterAsync()).TestAsync(paths);
                break;
            default:
                IArchiveListingRouter listing = await services.CreateListingRouterAsync();
                foreach (string path in paths)
                    await listing.ListEntriesAsync(path);
                break;
        }
    }

    // --- Happy path: ZIP never probes ---

    [Theory]
    [InlineData("extract")]
    [InlineData("test")]
    [InlineData("list")]
    public async Task ZipOnly_NeverProbesTar(string operation)
    {
        var tar = new CountingTarService();
        var services = PakkoServices.Create(new GroupPolicyOptions(), new QuietZipService(), tar);

        await RunAsync(services, operation, Write("zip"), Write("unknown"));

        tar.ProbeCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("extract")]
    [InlineData("test")]
    [InlineData("list")]
    public async Task TarFamily_ProbesOnce(string operation)
    {
        var tar = new CountingTarService();
        var services = PakkoServices.Create(new GroupPolicyOptions(), new QuietZipService(), tar);

        await RunAsync(services, operation, Write("zip"), Write("7z"), Write("gz"));
        await RunAsync(services, operation, Write("rar"));

        tar.ProbeCalls.Should().Be(1);
    }

    // --- Security & Boundary: a format policy refuses needs no probe ---

    [Theory]
    [InlineData("tar disabled", "extract")]
    [InlineData("tar disabled", "test")]
    [InlineData("tar disabled", "list")]
    [InlineData("blocked rar+zip", "extract")]
    [InlineData("blocked rar+zip", "test")]
    [InlineData("blocked rar+zip", "list")]
    public async Task RefusedByPolicy_NeverProbesTar(string policyName, string operation)
    {
        var tar = new CountingTarService();
        var services = PakkoServices.Create(Policies.Single(p => p.Name == policyName).Policy, new QuietZipService(), tar);

        await RunAsync(services, operation, Write("rar"));

        tar.ProbeCalls.Should().Be(0);
    }

    // With no AV provider registered a scan opens neither AMSI nor the sandbox, so only the probe is observed.
    [Theory]
    [InlineData("zip", "no policy", 0)]
    [InlineData("7z", "no policy", 1)]
    [InlineData("7z", "tar disabled", 0)]
    public async Task Scan_ProbesOnlyForATarFamilyArchivePolicyAllows(string format, string policyName, int expectedProbes)
    {
        int probes = 0;
        var scanner = new AntivirusScanService(
            () => { probes++; return Task.FromResult(EverythingSupported); },
            Policies.Single(p => p.Name == policyName).Policy,
            () => throw new InvalidOperationException("no scanner may be opened"),
            () => false);

        await scanner.ScanAsync(new AntivirusScanOptions { ArchivePaths = [Write(format)] });

        probes.Should().Be(expectedProbes);
    }

    // --- Error path: the wait for the probe is inside the operation, so Cancel ends it ---

    [Theory]
    [InlineData("extract")]
    [InlineData("test")]
    [InlineData("list")]
    public async Task ProbeStillRunning_Cancelled_Throws(string operation)
    {
        var probe = new TaskCompletionSource<TarCapabilities>();
        var policy = new GroupPolicyOptions();
        var extraction = new ExtractionRouter(new QuietZipService(), new CountingTarService(), () => probe.Task, policy);
        var listing = new ArchiveListingRouter(new QuietZipService(), new CountingTarService(), () => probe.Task, policy);
        string archive = Write("7z");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = operation switch
        {
            "extract" => () => extraction.ExtractAsync(
                new ExtractOptions { ArchivePaths = [archive], DestinationFolder = _temp.Path, OpenDestinationFolder = false },
                null, cancellation.Token),
            "test" => () => extraction.TestAsync([archive], null, null, cancellationToken: cancellation.Token),
            _ => () => listing.ListEntriesAsync(archive, cancellation.Token),
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Misuse: a ZIP-only selection never touches the source, even one that would fail ---

    [Fact]
    public async Task ClassifyAsync_NothingNeedsTar_NeverCallsTheSource()
    {
        ArchiveFormatPolicy.Classification result = await ArchiveFormatPolicy.ClassifyAsync(
            [Write("zip"), Write("unknown"), Path.Combine(_temp.Path, "missing.7z")],
            () => throw new InvalidOperationException("the probe must not run"),
            new GroupPolicyOptions(), CancellationToken.None);

        result.ZipPaths.Should().HaveCount(3);
    }

    [Fact]
    public async Task ClassifyAsync_EveryFormatPolicyAndCapability_MatchesThePinnedVerdicts()
    {
        string verdicts = await RenderAsync((paths, capabilities, policy) => ArchiveFormatPolicy
            .ClassifyAsync(paths, () => Task.FromResult(capabilities), policy, CancellationToken.None));

        verdicts.Should().Be(Pinned);
    }

    // --- Characterization: every verdict and its text, pinned before the change ---

    private async Task<string> RenderAsync(Func<IReadOnlyList<string>, TarCapabilities, GroupPolicyOptions, Task<ArchiveFormatPolicy.Classification>> classify)
    {
        var text = new StringBuilder();
        foreach ((string policyName, GroupPolicyOptions policy) in Policies)
        {
            foreach ((string capabilitiesName, TarCapabilities capabilities) in CapabilitySets)
            {
                foreach ((string format, _) in Formats)
                {
                    string path = Write(format);
                    ArchiveFormatPolicy.Classification result = await classify([path], capabilities, policy);
                    string verdict = result.ZipPaths.Count == 1 ? "zip engine"
                        : result.TarPaths.Count == 1 ? "tar engine"
                        : "refused: " + result.Unsupported.Single().Reason;
                    text.Append(policyName).Append(" | ").Append(capabilitiesName).Append(" | ").Append(format)
                        .Append(" -> ").Append(verdict).Append('\n');
                }
            }
        }
        return text.ToString();
    }

    [Fact]
    public async Task Classify_EveryFormatPolicyAndCapability_MatchesThePinnedVerdicts()
    {
        string verdicts = await RenderAsync((paths, capabilities, policy) =>
            Task.FromResult(ArchiveFormatPolicy.Classify(paths, capabilities, policy)));

        verdicts.Should().Be(Pinned);
    }

    private static string Pinned => PinnedVerdicts.ReplaceLineEndings("\n") + "\n";

    private const string PinnedVerdicts = """
        no policy | new tar | zip -> zip engine
        no policy | new tar | tar -> tar engine
        no policy | new tar | gz -> tar engine
        no policy | new tar | bz2 -> tar engine
        no policy | new tar | xz -> tar engine
        no policy | new tar | zst -> tar engine
        no policy | new tar | rar -> tar engine
        no policy | new tar | 7z -> tar engine
        no policy | new tar | unknown -> zip engine
        no policy | no tar | zip -> zip engine
        no policy | no tar | tar -> tar engine
        no policy | no tar | gz -> tar engine
        no policy | no tar | bz2 -> refused: This archive format is not supported by this system's tar.exe (version ).
        no policy | no tar | xz -> refused: XZ is not supported by this system's tar.exe (version ).
        no policy | no tar | zst -> refused: Zstandard requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version ) does not support it.
        no policy | no tar | rar -> refused: RAR requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version ) does not support it.
        no policy | no tar | 7z -> refused: 7-Zip requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version ) does not support it.
        no policy | no tar | unknown -> zip engine
        blocked rar+zip | new tar | zip -> refused: This archive format (zip) is blocked by Group Policy.
        blocked rar+zip | new tar | tar -> tar engine
        blocked rar+zip | new tar | gz -> tar engine
        blocked rar+zip | new tar | bz2 -> tar engine
        blocked rar+zip | new tar | xz -> tar engine
        blocked rar+zip | new tar | zst -> tar engine
        blocked rar+zip | new tar | rar -> refused: This archive format (rar) is blocked by Group Policy.
        blocked rar+zip | new tar | 7z -> tar engine
        blocked rar+zip | new tar | unknown -> zip engine
        blocked rar+zip | no tar | zip -> refused: This archive format (zip) is blocked by Group Policy.
        blocked rar+zip | no tar | tar -> tar engine
        blocked rar+zip | no tar | gz -> tar engine
        blocked rar+zip | no tar | bz2 -> refused: This archive format is not supported by this system's tar.exe (version ).
        blocked rar+zip | no tar | xz -> refused: XZ is not supported by this system's tar.exe (version ).
        blocked rar+zip | no tar | zst -> refused: Zstandard requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version ) does not support it.
        blocked rar+zip | no tar | rar -> refused: This archive format (rar) is blocked by Group Policy.
        blocked rar+zip | no tar | 7z -> refused: 7-Zip requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version ) does not support it.
        blocked rar+zip | no tar | unknown -> zip engine
        allowed zip+tar+7z | new tar | zip -> zip engine
        allowed zip+tar+7z | new tar | tar -> tar engine
        allowed zip+tar+7z | new tar | gz -> refused: This archive format (gzip) is blocked by Group Policy.
        allowed zip+tar+7z | new tar | bz2 -> refused: This archive format (bz2) is blocked by Group Policy.
        allowed zip+tar+7z | new tar | xz -> refused: This archive format (xz) is blocked by Group Policy.
        allowed zip+tar+7z | new tar | zst -> refused: This archive format (zstd) is blocked by Group Policy.
        allowed zip+tar+7z | new tar | rar -> refused: This archive format (rar) is blocked by Group Policy.
        allowed zip+tar+7z | new tar | 7z -> refused: This archive format (sevenzip) is blocked by Group Policy.
        allowed zip+tar+7z | new tar | unknown -> zip engine
        allowed zip+tar+7z | no tar | zip -> zip engine
        allowed zip+tar+7z | no tar | tar -> tar engine
        allowed zip+tar+7z | no tar | gz -> refused: This archive format (gzip) is blocked by Group Policy.
        allowed zip+tar+7z | no tar | bz2 -> refused: This archive format (bz2) is blocked by Group Policy.
        allowed zip+tar+7z | no tar | xz -> refused: This archive format (xz) is blocked by Group Policy.
        allowed zip+tar+7z | no tar | zst -> refused: This archive format (zstd) is blocked by Group Policy.
        allowed zip+tar+7z | no tar | rar -> refused: This archive format (rar) is blocked by Group Policy.
        allowed zip+tar+7z | no tar | 7z -> refused: This archive format (sevenzip) is blocked by Group Policy.
        allowed zip+tar+7z | no tar | unknown -> zip engine
        tar disabled | new tar | zip -> zip engine
        tar disabled | new tar | tar -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | new tar | gz -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | new tar | bz2 -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | new tar | xz -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | new tar | zst -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | new tar | rar -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | new tar | 7z -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | new tar | unknown -> zip engine
        tar disabled | no tar | zip -> zip engine
        tar disabled | no tar | tar -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | no tar | gz -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | no tar | bz2 -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | no tar | xz -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | no tar | zst -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | no tar | rar -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | no tar | 7z -> refused: tar.exe-based extraction is disabled by Group Policy.
        tar disabled | no tar | unknown -> zip engine
        """;
}
