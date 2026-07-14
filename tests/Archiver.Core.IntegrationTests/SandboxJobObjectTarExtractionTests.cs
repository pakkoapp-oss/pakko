using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Step 5 of T-F52's build order: re-confirms Phase 0's spike 0a finding (see DECISIONS.md) —
/// that Windows' built-in bsdtar keeps its .tar.xz/.tar.zst compression filters statically linked
/// and in-process, so a Job Object with ActiveProcessLimit = 1 does not break their extraction —
/// through the real production SandboxJobObject + SandboxedProcessLauncher classes instead of the
/// throwaway spike script. No AppContainer here yet (that combination, with the quarantine ACLs
/// that make it work against a real destination folder, arrives in step 6's TarSandboxScope) —
/// this test isolates the Job Object dimension only.
/// </summary>
public sealed class SandboxJobObjectTarExtractionTests : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [SkipIfFormatUnsupported("xz")]
    public async Task ExtractAsync_TarXz_UnderJobObjectWithActiveProcessLimitOne_Succeeds()
        => await ExtractAndVerify("valid.tar.xz", "-cJf");

    [SkipIfFormatUnsupported("zst")]
    public async Task ExtractAsync_TarZst_UnderJobObjectWithActiveProcessLimitOne_Succeeds()
        => await ExtractAndVerify("valid.tar.zst", "--zstd -cf");

    private async Task ExtractAndVerify(string fileName, string compressionFlag)
    {
        string archivePath = Path.Combine(_temp.Path, fileName);
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, compressionFlag, [("a.txt", "hello sandboxed job object")]);

        string destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);

        using var job = SandboxJobObject.Create(ramLimitBytes: 512 * 1024 * 1024, cpuTimeLimit: TimeSpan.FromMinutes(2));

        var (exitCode, _, stdErr) = await SandboxedProcessLauncher.RunAsync(
            TarExecutablePath,
            ["-xf", archivePath, "-C", destDir],
            attributeList: null,
            job.Handle,
            CancellationToken.None);

        exitCode.Should().Be(0, because: stdErr);
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello sandboxed job object");
    }
}
