using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F421, the tar route: as ZipArchiveServiceDestinationIsASourceTests - in separate mode "a.txt"
/// and "a.tar" both map to "a.tar", "a.tar" alone to itself, and a single archive can be named after
/// a source. A source of the run is never replaced: numbered under Overwrite, skipped under Skip.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceDestinationIsASourceTests : IDisposable
{
    private static readonly byte[] SourceTarBytes = [.. "not a real tar, a source file of the run"u8];
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private (string Txt, string Tar) CreateSources()
    {
        string txt = Path.Combine(_temp.Path, "a.txt");
        string tar = Path.Combine(_temp.Path, "a.tar");
        File.WriteAllText(txt, "hello");
        File.WriteAllBytes(tar, SourceTarBytes);
        return (txt, tar);
    }

    private Task<ArchiveResult> CompressAsync(ArchiveMode mode, ConflictBehavior onConflict, params string[] sources) =>
        _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            DestinationFolder = _temp.Path,
            ArchiveName = mode == ArchiveMode.SingleArchive ? "a" : null,
            Mode = mode,
            Format = ArchiveContainerFormat.Tar,
            OnConflict = onConflict,
        });

    private static async Task<string[]> ListAsync(string archivePath)
    {
        System.Diagnostics.ProcessStartInfo tar = new(@"C:\Windows\System32\tar.exe") { RedirectStandardOutput = true, UseShellExecute = false };
        tar.ArgumentList.Add("-tf");
        tar.ArgumentList.Add(archivePath);
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(tar)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [Integration]
    public async Task CompressAsync_SeparateTwoSourcesMapToOneName_Overwrite_NeitherSourceIsReplaced()
    {
        (string txt, string tar) = CreateSources();

        ArchiveResult result = await CompressAsync(ArchiveMode.SeparateArchives, ConflictBehavior.Overwrite, txt, tar);

        File.ReadAllBytes(tar).Should().Equal(SourceTarBytes, "a source of the run is never replaced");
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().BeEquivalentTo([Path.Combine(_temp.Path, "a (1).tar"), Path.Combine(_temp.Path, "a (2).tar")]);
        List<string[]> listings = [];
        foreach (string created in result.CreatedFiles)
            listings.Add(await ListAsync(created));
        listings.Should().ContainSingle(l => l.SequenceEqual(new[] { "a.txt" }));
        listings.Should().ContainSingle(l => l.SequenceEqual(new[] { "a.tar" }));
    }

    [Integration]
    public async Task CompressAsync_SeparateSourceMapsToItsOwnName_Overwrite_SourceIsKeptAndTheArchiveIsNumbered()
    {
        (_, string tar) = CreateSources();

        ArchiveResult result = await CompressAsync(ArchiveMode.SeparateArchives, ConflictBehavior.Overwrite, tar);

        File.ReadAllBytes(tar).Should().Equal(SourceTarBytes);
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "a (1).tar"));
        (await ListAsync(result.CreatedFiles[0])).Should().Equal("a.tar");
    }

    [Integration]
    public async Task CompressAsync_SingleArchiveNamedAfterASource_Overwrite_SourceIsKeptAndTheArchiveIsNumbered()
    {
        (string txt, string tar) = CreateSources();

        ArchiveResult result = await CompressAsync(ArchiveMode.SingleArchive, ConflictBehavior.Overwrite, txt, tar);

        File.ReadAllBytes(tar).Should().Equal(SourceTarBytes);
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "a (1).tar"));
        (await ListAsync(result.CreatedFiles[0])).Should().BeEquivalentTo(["a.txt", "a.tar"]);
    }

    [Integration]
    public async Task CompressAsync_SeparateTwoSourcesMapToOneName_Skip_SkipsBothAndKeepsTheSource()
    {
        (string txt, string tar) = CreateSources();

        ArchiveResult result = await CompressAsync(ArchiveMode.SeparateArchives, ConflictBehavior.Skip, txt, tar);

        File.ReadAllBytes(tar).Should().Equal(SourceTarBytes);
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Select(s => s.Path).Should().BeEquivalentTo([txt, tar]);
    }
}
