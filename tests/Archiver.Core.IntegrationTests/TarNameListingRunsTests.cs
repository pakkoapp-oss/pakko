using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F550: T-F465's check reads the lines the scope's first "-t" printed; a second "-t" decompressed the
// whole archive again (a filtered pakko t of a 300 MB .tar.xz: 16.2 s, 12.2 s without it; 2026-11-17).
[Collection("TarSandbox")]
public sealed class TarNameListingRunsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Archive()
    {
        string archivePath = Path.Combine(_temp.Path, "three.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c")]);
        return archivePath;
    }

    [Integration]
    public async Task SelectiveExtraction_OneNameListing()
    {
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(Archive(), needsOutputDir: true, CancellationToken.None);
        ISandboxedArchiveReader reader = TarExeReader.On(scope);
        await reader.ScanAsync(sendsNamesBack: true, CancellationToken.None);
        (await reader.SelectForExtractAsync(["b.txt"], null, CancellationToken.None)).Kind.Should().NotBe(SelectionKind.EmptySelection);

        await reader.ExtractAsync(null, [], CancellationToken.None);

        scope.NameListingRuns.Should().Be(1);
    }

    // A scan whose reader sends no names back (an extraction without a selection, a filter's included,
    // or a list) keeps nothing; what each service passes is TarSandboxedService's and the scan's call.
    [Integration]
    public async Task ScanThatSendsNoNamesBack_KeepsNoLines()
    {
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(Archive(), needsOutputDir: true, CancellationToken.None);
        ISandboxedArchiveReader reader = TarExeReader.On(scope);

        await reader.ScanAsync(CancellationToken.None);

        scope.HoldsPrintedNames.Should().BeFalse();
    }

    [Integration]
    public async Task FilteredTest_OneNameListing()
    {
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(Archive(), needsOutputDir: false, CancellationToken.None);
        ISandboxedArchiveReader reader = TarExeReader.On(scope);

        TestOutcome outcome = await reader.TestAsync(null, (name, _) => name == "b.txt", CancellationToken.None);

        outcome.Failure.Should().BeNull();
        scope.NameListingRuns.Should().Be(1);
    }

    // A list never sends a name back: it keeps no lines (T-F539's reason).
    [Integration]
    public async Task List_KeepsNoLines()
    {
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(Archive(), needsOutputDir: false, CancellationToken.None);

        (await scope.ListNamesAsync(CancellationToken.None)).ExitCode.Should().Be(0);

        scope.HoldsPrintedNames.Should().BeFalse();
        scope.NameListingRuns.Should().Be(1);
    }

    // A scope that kept nothing reads the lines when a name is first sent back, and only once.
    [Integration]
    public async Task NamesSentWithoutKeptLines_ReadOnce_AndChecked()
    {
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(Archive(), needsOutputDir: false, CancellationToken.None);
        (await scope.ListNamesAsync(CancellationToken.None)).ExitCode.Should().Be(0);

        (await scope.TestAsync(_ => { }, ["a.txt"], null, long.MaxValue, CancellationToken.None)).ExitCode.Should().Be(0);
        (await scope.TestAsync(_ => { }, ["b.txt"], null, long.MaxValue, CancellationToken.None)).ExitCode.Should().Be(0);
        Func<Task> missing = () => scope.TestAsync(_ => { }, ["d.txt"], null, long.MaxValue, CancellationToken.None);

        await missing.Should().ThrowAsync<TarArgumentEncodingException>();
        scope.NameListingRuns.Should().Be(2);
    }
}
