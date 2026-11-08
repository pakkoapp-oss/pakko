using System.Text;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F470: TarSandboxScope launches the archive helper as it launches tar.exe - from a copy in bin\
/// of its own quarantine, in the production AppContainer, under a Job Object, the archive as stdin -
/// and reads its stdout through the strict protocol reader.
/// </summary>
[Collection("TarSandbox")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class TarSandboxScopeHelperTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // --- Happy path ---

    [Fact]
    public async Task List_GivesTheEntriesUnderTheirExactNames()
    {
        string archive = Tar(("файл.txt", "A"), ("日本語.txt", "BB"));
        using TarSandboxScope scope = await Scope(archive, needsOutputDir: false);
        var output = new ArchiveHelperOutput(ArchiveHelperMode.List);

        (ArchiveHelperExit exit, string limitHit) = await scope.RunHelperAsync(ArchiveHelperMode.List, ["--format", "tar"], null, output, CancellationToken.None);

        exit.Should().Be(ArchiveHelperExit.Done);
        limitHit.Should().BeEmpty();
        output.Entries.Select(e => (e.Name, e.Size)).Should().Equal(("файл.txt", 1L), ("日本語.txt", 2L));
    }

    [Fact]
    public async Task Extract_WritesTheSelectionAsIndices_AndTheCopyIsReusedAcrossRuns()
    {
        string archive = Tar(("a.txt", "A"), ("b.txt", "BB"), ("c.txt", "CCC"));
        using TarSandboxScope scope = await Scope(archive, needsOutputDir: true);
        await scope.RunHelperAsync(ArchiveHelperMode.List, ["--format", "tar"], null, new ArchiveHelperOutput(ArchiveHelperMode.List), CancellationToken.None);
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Extract);

        (ArchiveHelperExit exit, _) = await scope.RunHelperAsync(
            ArchiveHelperMode.Extract, ["--format", "tar", "--max-total", "100"], [0, 2], output, CancellationToken.None);

        exit.Should().Be(ArchiveHelperExit.Done);
        output.Read.Should().Equal((0L, 1L), (2L, 3L));
        Directory.GetFiles(scope.OutputDirectory!).Select(Path.GetFileName).Should().BeEquivalentTo(["0", "2"]);
        File.ReadAllText(Path.Combine(scope.OutputDirectory!, "2")).Should().Be("CCC");
    }

    [Fact]
    public void SelectionBytes_AreTheHelpersFormat()
        => Encoding.ASCII.GetString(TarSandboxScope.SelectionBytes([0, 7, 4000000])).Should().Be("0\n7\n4000000\n");

    // --- Security & boundary ---

    [Fact]
    public async Task ACopyChangedBetweenRuns_IsNotRun()
    {
        string archive = Tar(("a.txt", "A"));
        using TarSandboxScope scope = await Scope(archive, needsOutputDir: false);
        await scope.RunHelperAsync(ArchiveHelperMode.List, ["--format", "tar"], null, new ArchiveHelperOutput(ArchiveHelperMode.List), CancellationToken.None);
        string copy = Path.Combine(scope.QuarantineRoot, "bin", "Archiver.ArchiveHelper.exe");
        File.AppendAllText(copy, "x");

        Func<Task> run = () => scope.RunHelperAsync(ArchiveHelperMode.List, ["--format", "tar"], null, new ArchiveHelperOutput(ArchiveHelperMode.List), CancellationToken.None);

        (await run.Should().ThrowAsync<IOException>()).WithMessage("*copy changed*");
    }

    [Fact]
    public async Task TheSandboxGetsReadExecuteOnBinAndList_NotWrite()
    {
        string archive = Tar(("a.txt", "A"));
        using TarSandboxScope scope = await Scope(archive, needsOutputDir: true);

        await scope.RunHelperAsync(ArchiveHelperMode.Test, ["--format", "tar"], [0], new ArchiveHelperOutput(ArchiveHelperMode.Test), CancellationToken.None);

        foreach (string folder in new[] { "bin", "list" })
        {
            string rights = string.Join(" ", new DirectoryInfo(Path.Combine(scope.QuarantineRoot, folder)).GetAccessControl()
                .GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier))
                .Cast<System.Security.AccessControl.FileSystemAccessRule>()
                .Where(r => r.IdentityReference.Value.StartsWith("S-1-15-2-", StringComparison.Ordinal))
                .Select(r => r.FileSystemRights.ToString()));
            rights.Should().Contain("ReadAndExecute").And.NotContain("Write").And.NotContain("Modify");
        }
    }

    // --- Misuse ---

    [Fact]
    public async Task AScopeWithoutTheHelper_RefusesToRunIt()
    {
        string archive = Tar(("a.txt", "A"));
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archive, needsOutputDir: false, CancellationToken.None);

        Func<Task> run = () => scope.RunHelperAsync(ArchiveHelperMode.List, ["--format", "tar"], null, new ArchiveHelperOutput(ArchiveHelperMode.List), CancellationToken.None);

        await run.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExtractInAListingScope_IsRefused()
    {
        string archive = Tar(("a.txt", "A"));
        using TarSandboxScope scope = await Scope(archive, needsOutputDir: false);

        Func<Task> run = () => scope.RunHelperAsync(ArchiveHelperMode.Extract, ["--format", "tar", "--max-total", "9"], [0], new ArchiveHelperOutput(ArchiveHelperMode.Extract), CancellationToken.None);

        await run.Should().ThrowAsync<InvalidOperationException>();
    }

    // --- Error path ---

    [Fact]
    public async Task AMissingHelper_FailsAsIO()
    {
        string archive = Tar(("a.txt", "A"));
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archive, needsOutputDir: false, CancellationToken.None,
            helperSource: Path.Combine(_temp.Path, "no-such-helper.exe"));

        Func<Task> run = () => scope.RunHelperAsync(ArchiveHelperMode.List, ["--format", "tar"], null, new ArchiveHelperOutput(ArchiveHelperMode.List), CancellationToken.None);

        await run.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task ACutArchive_EndsInTheHelpersFailure_NotAnException()
    {
        string whole = Tar(("a.txt", new string('x', 4000)));
        string cut = Path.Combine(_temp.Path, "cut.tar");
        File.WriteAllBytes(cut, File.ReadAllBytes(whole)[..1500]);
        using TarSandboxScope scope = await Scope(cut, needsOutputDir: true);
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Extract);

        (ArchiveHelperExit exit, _) = await scope.RunHelperAsync(ArchiveHelperMode.Extract, ["--format", "tar", "--max-total", "100000"], [0], output, CancellationToken.None);

        exit.Should().Be(ArchiveHelperExit.Archive);
        output.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task ACancelledRun_LeavesNoHelperRunning()
    {
        string archive = Tar(("a.txt", "A"));
        using TarSandboxScope scope = await Scope(archive, needsOutputDir: false);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> run = () => scope.RunHelperAsync(ArchiveHelperMode.List, ["--format", "tar"], null, new ArchiveHelperOutput(ArchiveHelperMode.List), cancelled.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        System.Diagnostics.Process.GetProcessesByName("Archiver.ArchiveHelper")
            .Where(p => { try { return p.MainModule?.FileName?.StartsWith(scope.QuarantineRoot, StringComparison.OrdinalIgnoreCase) == true; } catch (System.ComponentModel.Win32Exception) { return false; } })
            .Should().BeEmpty();
    }

    // --- Harness ---

    private static Task<TarSandboxScope> Scope(string archive, bool needsOutputDir) =>
        TarSandboxScope.CreateAsync(archive, needsOutputDir, CancellationToken.None, helperSource: ArchiveHelperBuild.ExePath);

    private string Tar(params (string Name, string Content)[] files)
    {
        string path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N")[..8] + ".tar");
        TarBuilder.WriteTar(path, files.SelectMany(f => new[]
        {
            TarBuilder.PaxPath(f.Name),
            new TarBuilder.Entry { Name = "x", Content = Encoding.UTF8.GetBytes(f.Content), ModifiedUnixSeconds = 1700000000 },
        }));
        return path;
    }
}
