using System.Diagnostics;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F410: "Test" with an entry filter through the real tar.exe. tar.exe gets the shorter list:
/// "-T" with the files that passed, or "-X" with the files left out. Measured first (DECISIONS,
/// "T-F410, wave B"): a damaged file of a solid 7z that "-X" leaves out is not reported (7-Zip
/// reports it), and an xz stream fails wherever it is damaged.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceTestFilterTests : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";
    // The first file of the 7z, so a flipped byte near the start of its data damages only it.
    private const string Damaged = "k[1].bin";

    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [SevenZipWriterFact]
    public async Task TestAsync_ExcludeListLeavesOutTheDamagedFile_Passes()
    {
        // Three of four pass, so "-X" carries the one left out, a name with pattern syntax in it.
        string archive = DamagedSevenZip();

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (name, _) => name != Damaged);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
    }

    [SevenZipWriterFact]
    public async Task TestAsync_MemberListWithANameStartingWithADash_ReadsOnlyIt()
    {
        // One of four passes, so "-T" carries it; a name tar.exe did not find would be an error.
        string archive = DamagedSevenZip();

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (name, _) => name == "-d.bin");

        result.Errors.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
    }

    [SevenZipWriterFact]
    public async Task TestAsync_MemberListTakesTheDamagedFile_Fails()
    {
        string archive = DamagedSevenZip();

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (name, _) => name == Damaged);

        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(archive);
    }

    [SevenZipWriterFact]
    public async Task TestAsync_NoFilter_StillFailsOnTheDamagedFile()
    {
        string archive = DamagedSevenZip();

        ArchiveResult result = await _sut.TestAsync([archive]);

        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task TestAsync_FilterPassesNothing_CleanWithoutTheStructureOnlyWarning()
    {
        // tar.gz is a format whose test carries the structure-only warning; nothing was read here.
        string archive = Create("a.tar.gz", ["-czf"], "a.txt", "dir");

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (_, _) => false);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
    }

    [Fact]
    public async Task TestAsync_OnlyAFolderPasses_NothingToReadAndClean()
    {
        string archive = Create("a.tar.gz", ["-czf"], "a.txt", "dir");

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (_, isFolder) => isFolder);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_SomeFilesPass_KeepsTheStructureOnlyWarning()
    {
        string archive = Create("a.tar.gz", ["-czf"], "a.txt", "dir");

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (name, _) => name == "a.txt");

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TestedStructureOnly);
    }

    [Fact]
    public async Task TestAsync_Filter_IsAskedForEveryEntryAsTheListingNamesIt()
    {
        // "tar -C dir ." writes "./" before every name; the filter sees the names without it.
        string archive = Create("a.tar", ["-cf"], ".");
        var asked = new List<(string Name, bool IsFolder)>();

        await _sut.TestAsync([archive], entryFilter: (name, isFolder) =>
        {
            asked.Add((name, isFolder));
            return true;
        });

        asked.Should().BeEquivalentTo([("a.txt", false), ("dir/", true), ("dir/b.txt", false),
            (Damaged, false), ("-d.bin", false), ("k1.bin", false), ("z.bin", false)]);
    }

    [SevenZipWriterFact]
    public async Task TestAsync_XzStreamDamagedOutsideTheFilter_StillFails()
    {
        // A damaged xz stream cannot be read past the damage, whichever file is asked for.
        string archive = Create("a.tar.xz", ["-cJf"], Damaged, "-d.bin", "k1.bin", "z.bin");
        Flip(archive, 1000);

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (name, _) => name != Damaged);

        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task TestAsync_MemberListWithATrailingDollar_FindsTheName()
    {
        // libarchive reads a trailing '$' as an end anchor: unescaped, "-T cost$" is "Not found".
        string archive = Path.Combine(_temp.Path, "dollar.tar");
        TarBuilder.WriteTar(archive, [Entry("cost$"), Entry("a.txt"), Entry("b.txt")]);

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (name, _) => name == "cost$");

        result.Errors.Should().BeEmpty();
    }

    private static TarBuilder.Entry Entry(string name) => new() { Name = name, Content = System.Text.Encoding.ASCII.GetBytes(name) };

    private string DamagedSevenZip()
    {
        string archive = Create("a.7z", ["--format=7zip", "--options", "7zip:compression=lzma2", "-cf"],
            Damaged, "./-d.bin", "k1.bin", "z.bin");
        Flip(archive, 1000);
        return archive;
    }

    // Files a.txt, dir/b.txt and four 200 KB random .bin files in "src"; the archive is made from
    // the names given, in that order, with "src" as tar.exe's working folder.
    private string Create(string name, string[] tarSwitches, params string[] members)
    {
        string source = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(Path.Combine(source, "dir"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");
        File.WriteAllText(Path.Combine(source, "dir", "b.txt"), "b");
        var random = new Random(410);
        foreach (string file in (string[])[Damaged, "-d.bin", "k1.bin", "z.bin"])
        {
            byte[] bytes = new byte[200_000];
            random.NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(source, file), bytes);
        }

        string archive = Path.Combine(_temp.Path, name);
        var start = new ProcessStartInfo(TarExecutablePath) { UseShellExecute = false, RedirectStandardError = true, WorkingDirectory = source };
        foreach (string argument in (string[])[.. tarSwitches, archive, .. members])
            start.ArgumentList.Add(argument);
        using Process tar = Process.Start(start)!;
        string stdErr = tar.StandardError.ReadToEnd();
        tar.WaitForExit();
        tar.ExitCode.Should().Be(0, stdErr);
        return archive;
    }

    private static void Flip(string path, int offset)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes[offset] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }
}
