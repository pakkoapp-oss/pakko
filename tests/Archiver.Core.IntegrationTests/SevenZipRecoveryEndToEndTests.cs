using System.Diagnostics;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Recovery;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F397, end to end with the real tar.exe: a 7z with a PAR2 set is damaged, the test sees it,
/// the repair rebuilds it, and what the repaired copy extracts to is the source, byte for byte.
/// And the rule that keeps a repair honest: an archive whose own test verifies its contents and
/// passes is a newer archive beside an older set, never "damage"; a format tar.exe verifies
/// nothing of gets no such benefit.
/// </summary>
[Collection("TarSandbox")]
public sealed class SevenZipRecoveryEndToEndTests : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";

    private readonly GroupPolicyOptions _policy = new();
    private readonly TarSandboxedService _tar;
    private readonly TempDirectory _temp = new();

    public SevenZipRecoveryEndToEndTests() => _tar = new TarSandboxedService(_policy);

    public void Dispose() => _temp.Dispose();

    private string Work
    {
        get
        {
            string work = Path.Combine(_temp.Path, "work");
            Directory.CreateDirectory(work);
            return work;
        }
    }

    private async Task<ExtractionRouter> RouterAsync() =>
        new(new ZipArchiveService(_policy), _tar, await _tar.DetectCapabilitiesAsync(), _policy);

    private RecoveryService Recovery() =>
        new(_policy, async () => (IExtractionRouter)await RouterAsync());

    private string BuildTree(int seed)
    {
        string tree = Path.Combine(_temp.Path, "tree" + seed);
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        File.WriteAllText(Path.Combine(tree, "text.txt"), string.Concat(Enumerable.Repeat("The quick brown fox. ", 5_000)));
        byte[] noise = new byte[200_000];
        new Random(seed).NextBytes(noise);
        File.WriteAllBytes(Path.Combine(tree, "sub", "data.bin"), noise);
        File.WriteAllBytes(Path.Combine(tree, "empty.txt"), []);
        return tree;
    }

    private string CreateArchive(string name, string tree, params string[] tarSwitches)
    {
        string archive = Path.Combine(Work, name);
        var start = new ProcessStartInfo(TarExecutablePath) { UseShellExecute = false, RedirectStandardError = true };
        foreach (string argument in (string[])[.. tarSwitches, archive, "-C", tree, "."])
            start.ArgumentList.Add(argument);
        using Process tar = Process.Start(start)!;
        string stdErr = tar.StandardError.ReadToEnd();
        tar.WaitForExit();
        tar.ExitCode.Should().Be(0, stdErr);
        return archive;
    }

    private string SevenZip(string tree) =>
        CreateArchive("a.7z", tree, "--format=7zip", "--options", "7zip:compression=lzma2", "-cf");

    private static void Protect(string archive, int percent) =>
        Par2Creator.Create(archive, Par2Creator.ChooseParameters(new FileInfo(archive).Length, percent)!.Value, null, CancellationToken.None);

    private static void Flip(string path, long offset)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = offset;
        int value = stream.ReadByte();
        stream.Position = offset;
        stream.WriteByte((byte)(value ^ 0xFF));
    }

    private static void ShouldHoldTheSameFiles(string extracted, string tree)
    {
        string[] expected = [.. Directory.GetFiles(tree, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(tree, p)).Order(StringComparer.Ordinal)];
        string[] actual = [.. Directory.GetFiles(extracted, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(extracted, p)).Order(StringComparer.Ordinal)];
        actual.Should().Equal(expected);
        foreach (string relative in expected)
            File.ReadAllBytes(Path.Combine(extracted, relative)).Should().Equal(File.ReadAllBytes(Path.Combine(tree, relative)), relative);
    }

    public enum Damage
    {
        Data,
        Header,
        Truncated,
    }

    [SevenZipWriterTheory]
    [InlineData(Damage.Data)]
    [InlineData(Damage.Header)]
    [InlineData(Damage.Truncated)]
    public async Task DamagedSevenZipWithASet_TestSeesIt_RepairRebuildsIt_AndTheCopyExtractsToTheSource(Damage damage)
    {
        string tree = BuildTree(seed: 1);
        string archive = SevenZip(tree);
        Protect(archive, percent: 30);
        long length = new FileInfo(archive).Length;
        switch (damage)
        {
            case Damage.Data: Flip(archive, length / 2); break;
            case Damage.Header: Flip(archive, 10); break;
            case Damage.Truncated:
                using (var stream = new FileStream(archive, FileMode.Open, FileAccess.Write))
                    stream.SetLength(length - (length / 10));
                break;
        }

        ArchiveResult tested = await (await RouterAsync()).TestAsync([archive], verifyRecoveryData: true);

        tested.Errors.Should().NotBeEmpty("tar.exe's own test and the set both see the damage");
        tested.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Repairable);

        ArchiveResult repairedResult = await Recovery().RepairAsync(new RepairOptions { Paths = [archive] });

        repairedResult.Errors.Should().BeEmpty();
        string repaired = repairedResult.CreatedFiles.Should().ContainSingle().Subject;
        Path.GetFileName(repaired).Should().Be("a.repaired.7z");

        ArchiveResult retested = await _tar.TestAsync([repaired]);
        retested.Errors.Should().BeEmpty();
        retested.Warnings.Should().BeEmpty("a 7z's contents are verified");

        string extracted = Path.Combine(_temp.Path, "extracted");
        ArchiveResult extraction = await _tar.ExtractAsync(new ExtractOptions { ArchivePaths = [repaired], DestinationFolder = extracted });
        extraction.Errors.Should().BeEmpty();
        // The default mode extracts into a folder named after the archive.
        ShouldHoldTheSameFiles(Path.Combine(extracted, "a.repaired"), tree);
    }

    [SevenZipWriterFact]
    public async Task WholeSevenZipWithItsSet_PassesWithNoWarning_AndIsIntact()
    {
        string archive = SevenZip(BuildTree(seed: 1));
        Protect(archive, percent: 10);

        ArchiveResult tested = await (await RouterAsync()).TestAsync([archive], verifyRecoveryData: true);

        tested.Errors.Should().BeEmpty();
        tested.Warnings.Should().BeEmpty();
        tested.Outcome.Should().Be(OperationOutcome.Completed);
        tested.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Intact);
    }

    // A 7z written again under the same name, its old set left behind: the archive passes a test
    // that verifies its contents, so the set is the stale one and nothing is rebuilt from it.
    [SevenZipWriterFact]
    public async Task NewerWholeSevenZipBesideAnOlderSet_IsNotRebuilt()
    {
        string archive = SevenZip(BuildTree(seed: 1));
        Protect(archive, percent: 30);
        File.Delete(archive);
        SevenZip(BuildTree(seed: 2));
        string[] before = Directory.GetFiles(Work);

        ArchiveResult result = await Recovery().RepairAsync(new RepairOptions { Paths = [archive] });

        result.CreatedFiles.Should().BeEmpty();
        Directory.GetFiles(Work).Should().BeEquivalentTo(before);
        result.RecoveryChecks.Should().ContainSingle().Which.State.Should().NotBe(RecoveryState.Repaired);
    }

    // The other side of that rule. tar.exe reads a tar.gz with a changed byte to its end and exits
    // 0, so its pass proves nothing: the set's verdict stands and the archive is rebuilt.
    [Integration]
    public async Task DamagedTarGzWhoseOwnTestSeesNothing_IsStillRebuiltFromItsSet()
    {
        string tree = BuildTree(seed: 1);
        string archive = CreateArchive("a.tar.gz", tree, "-czf");
        byte[] good = File.ReadAllBytes(archive);
        Protect(archive, percent: 30);
        Flip(archive, good.Length / 2);
        (await _tar.TestAsync([archive])).Errors.Should().BeEmpty("the measured limitation this test stands on");

        ArchiveResult tested = await (await RouterAsync()).TestAsync([archive], verifyRecoveryData: true);
        ArchiveResult repairedResult = await Recovery().RepairAsync(new RepairOptions { Paths = [archive] });

        tested.RecoveryChecks.Should().ContainSingle().Which.State.Should().Be(RecoveryState.Repairable);
        string repaired = repairedResult.CreatedFiles.Should().ContainSingle().Subject;
        File.ReadAllBytes(repaired).Should().Equal(good);
    }
}
