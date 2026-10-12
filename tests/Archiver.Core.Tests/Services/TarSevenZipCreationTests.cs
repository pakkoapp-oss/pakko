using System.IO.Compression;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

file sealed class ZipMustNotRunService : IArchiveService
{
    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("the ZIP engine must not run");

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("the ZIP engine must not run");

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("the ZIP engine must not run");

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("the ZIP engine must not run");
}

file sealed class TarMustNotCreateService : ITarService
{
    public Task<bool> CanCreateSevenZipAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("the 7z writer must not be asked for");

    public Task<TarCapabilities> DetectCapabilitiesAsync()
        => throw new InvalidOperationException("tar.exe must not be probed");

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("tar.exe must not run");

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("tar.exe must not run");

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("tar.exe must not run");
}

/// <summary>
/// T-F342 (Core): the parts of 7z creation that need no tar.exe run - the arguments, the answer to
/// "can this system create 7z", and every refusal that comes before anything is written. Creation
/// itself is checked against the real tar.exe in Archiver.Core.IntegrationTests and against 7-Zip
/// in Archiver.Core.PerformanceTests.
/// </summary>
public sealed class TarSevenZipCreationTests : IDisposable
{
    private static readonly TarCapabilities Reads7z = new() { Supports7z = true };
    private readonly TempDirectory _temp = new();
    private int _versionProbes;
    private int _writerProbes;

    public void Dispose() => _temp.Dispose();

    private TarSandboxedService Service(GroupPolicyOptions? policy = null, bool reads7z = true, bool writes7z = true) => new(
        policy ?? new GroupPolicyOptions(),
        () => { _versionProbes++; return Task.FromResult(reads7z ? Reads7z : new TarCapabilities()); },
        sevenZipWriterProbe: () => { _writerProbes++; return Task.FromResult(writes7z); });

    private ArchiveOptions SevenZipOptions(Func<PasswordPromptInfo, Task<PasswordDecision>>? password = null)
    {
        string source = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(source, "a");
        return new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = "out",
            Format = ArchiveContainerFormat.SevenZip,
            ResolvePasswordAsync = password,
        };
    }

    private static List<string> Args(SevenZipMethod method, CompressionLevel level)
    {
        var args = new List<string>();
        TarSandboxedService.AppendSevenZipArgs(args, method, level);
        return args;
    }

    // --- Happy path ---

    [Theory]
    [InlineData(SevenZipMethod.Lzma2, CompressionLevel.Optimal, "7zip:compression=lzma2,compression-level=6")]
    [InlineData(SevenZipMethod.Lzma2, CompressionLevel.Fastest, "7zip:compression=lzma2,compression-level=1")]
    [InlineData(SevenZipMethod.Lzma2, CompressionLevel.SmallestSize, "7zip:compression=lzma2,compression-level=9")]
    [InlineData(SevenZipMethod.Lzma, CompressionLevel.Optimal, "7zip:compression=lzma1,compression-level=6")]
    [InlineData(SevenZipMethod.BZip2, CompressionLevel.Fastest, "7zip:compression=bzip2,compression-level=1")]
    [InlineData(SevenZipMethod.Deflate, CompressionLevel.SmallestSize, "7zip:compression=deflate,compression-level=9")]
    public void AppendSevenZipArgs_MethodAndLevel_NameTheFormatAndBothOptions(SevenZipMethod method, CompressionLevel level, string options) =>
        Args(method, level).Should().Equal("--format=7zip", "--options", options);

    [Fact]
    public async Task CanCreateSevenZipAsync_ReaderAndWriterPresent_IsTrueAndAskedOnce()
    {
        TarSandboxedService sut = Service();

        (await sut.CanCreateSevenZipAsync()).Should().BeTrue();
        (await sut.CanCreateSevenZipAsync()).Should().BeTrue();

        _writerProbes.Should().Be(1);
        _versionProbes.Should().Be(1);
    }

    [Fact]
    public void NamingAndPolicyName_AreTheOnesReadingUses()
    {
        ArchiveNaming.GetExtension(ArchiveContainerFormat.SevenZip).Should().Be(".7z");
        ArchiveFormatRegistryNames.ToRegistryName(ArchiveContainerFormat.SevenZip)
            .Should().Be(ArchiveFormatRegistryNames.ToRegistryName(ArchiveFormat.SevenZip));
        new ArchiveOptions().SevenZipMethod.Should().Be(SevenZipMethod.Lzma2);
    }

    // --- Security & Boundary: policy stops 7z creation before tar.exe is asked anything ---

    [Theory]
    [InlineData("blocked")]
    [InlineData("not allowed")]
    [InlineData("tar disabled")]
    public async Task Router_PolicyRefusesSevenZip_BeforeAnyEngineOrProbe(string policyName)
    {
        GroupPolicyOptions policy = policyName switch
        {
            "blocked" => new GroupPolicyOptions { BlockedFormats = ["sevenzip"] },
            "not allowed" => new GroupPolicyOptions { AllowedFormats = ["zip", "tar"] },
            _ => new GroupPolicyOptions { DisableTarExtraction = true },
        };
        var router = new ArchiveCreationRouter(new ZipMustNotRunService(), new TarMustNotCreateService(), policy);

        ArchiveResult result = await router.ArchiveAsync(SevenZipOptions());

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(
            policyName == "tar disabled" ? MessageCode.TarCreationDisabled : MessageCode.CreationFormatBlocked);
        Directory.Exists(Path.Combine(_temp.Path, "out")).Should().BeFalse();
    }

    [Fact]
    public async Task CanCreateSevenZipAsync_TarDisabledByPolicy_IsFalseAndStartsNothing()
    {
        TarSandboxedService sut = Service(new GroupPolicyOptions { DisableTarExtraction = true });

        (await sut.CanCreateSevenZipAsync()).Should().BeFalse();

        (_versionProbes, _writerProbes).Should().Be((0, 0));
    }

    [Fact]
    public async Task CanCreateSevenZipAsync_TarCannotReadSevenZip_IsFalseAndTheWriterIsNotRun()
    {
        // A 7z Pakko could not open again is not offered.
        TarSandboxedService sut = Service(reads7z: false);

        (await sut.CanCreateSevenZipAsync()).Should().BeFalse();

        _writerProbes.Should().Be(0);
    }

    [Theory]
    [InlineData(0, "7z\u00BC\u00AF'\u001C" + "rest of the header", true)]
    [InlineData(1, "7z\u00BC\u00AF'\u001C", false)]
    [InlineData(0, "", false)]
    [InlineData(0, "ustar", false)]
    [InlineData(0, "7z", false)]
    [InlineData(0, " 7z\u00BC\u00AF'\u001C", false)]
    public void IsSevenZipWriterOutput_NeedsExitZeroAndTheSevenZipSignatureFirst(int exitCode, string output, bool expected) =>
        TarSandboxedService.IsSevenZipWriterOutput(exitCode, output).Should().Be(expected);

    // --- Misuse ---

    [Theory]
    [InlineData(SevenZipMethod.Lzma2)]
    [InlineData(SevenZipMethod.Lzma)]
    [InlineData(SevenZipMethod.BZip2)]
    [InlineData(SevenZipMethod.Deflate)]
    [InlineData(SevenZipMethod.Copy)]
    public void AppendSevenZipArgs_NoCompression_IsTheCopyMethodWhateverTheMethod(SevenZipMethod method) =>
        Args(method, CompressionLevel.NoCompression).Should().Equal("--format=7zip", "--options", "7zip:compression=copy");

    [Theory]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void AppendSevenZipArgs_CopyMethod_HasNoLevel(CompressionLevel level) =>
        Args(SevenZipMethod.Copy, level).Should().Equal("--format=7zip", "--options", "7zip:compression=copy");

    [Fact]
    public void AppendSevenZipArgs_MethodOutsideTheEnum_Throws()
    {
        Action act = () => Args((SevenZipMethod)99, CompressionLevel.Optimal);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task CompressAsync_SevenZipWithPassword_IsRefusedWithItsOwnMessageAndNothingStarts()
    {
        TarSandboxedService sut = Service();
        bool asked = false;

        ArchiveResult result = await sut.CompressAsync(SevenZipOptions(_ =>
        {
            asked = true;
            return Task.FromResult(new PasswordDecision());
        }));

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SevenZipEncryptionNotSupported);
        result.CreatedFiles.Should().BeEmpty();
        asked.Should().BeFalse();
        (_versionProbes, _writerProbes).Should().Be((0, 0));
        Directory.Exists(Path.Combine(_temp.Path, "out")).Should().BeFalse();
    }

    [Fact]
    public async Task CompressAsync_TarFormatWithPassword_KeepsTheZipOnlyMessage()
    {
        TarSandboxedService sut = Service();

        ArchiveResult result = await sut.CompressAsync(
            SevenZipOptions(_ => Task.FromResult(new PasswordDecision())) with { Format = ArchiveContainerFormat.TarGz });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.PasswordOnlyForZip);
    }

    // --- Error path ---

    [Fact]
    public async Task CompressAsync_NoSevenZipWriter_IsRefusedBeforeAnythingIsWritten()
    {
        TarSandboxedService sut = Service(writes7z: false);

        ArchiveResult result = await sut.CompressAsync(SevenZipOptions());

        result.Errors.Should().ContainSingle().Which.Should().Match<ArchiveError>(e =>
            e.Text!.Code == MessageCode.SevenZipWriterUnavailable && e.SourcePath == Path.Combine(_temp.Path, "out", "out.7z"));
        result.CreatedFiles.Should().BeEmpty();
        Directory.Exists(Path.Combine(_temp.Path, "out")).Should().BeFalse();
    }

    [Fact]
    public async Task CanCreateSevenZipAsync_CancelledWhileTheProbeRuns_Throws()
    {
        var pending = new TaskCompletionSource<bool>();
        var sut = new TarSandboxedService(new GroupPolicyOptions(), () => Task.FromResult(Reads7z), sevenZipWriterProbe: () => pending.Task);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => sut.CanCreateSevenZipAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Recovery: what a killed run leaves is swept by the next creation in that folder ---

    [Fact]
    public void ArchiveTempFileCreate_WriterFolderOfADeadRun_IsRemoved()
    {
        string stale = Path.Combine(_temp.Path, DeadOwner.Name(ArchiveTempFile.WriterFolderPrefix));
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "libarchive_0000000000"), "left by a killed tar.exe");

        ArchiveTempFile.Create(Path.Combine(_temp.Path, "new.7z"));

        Directory.Exists(stale).Should().BeFalse();
    }

    [Fact]
    public void ArchiveTempFileCreate_WriterFolderOfThisProcess_IsKept()
    {
        string own = ArchiveTempFile.NewWriterFolder(Path.Combine(_temp.Path, "x.tmp"));
        Directory.CreateDirectory(own);

        ArchiveTempFile.Create(Path.Combine(_temp.Path, "new.7z"));

        Directory.Exists(own).Should().BeTrue();
    }
}
