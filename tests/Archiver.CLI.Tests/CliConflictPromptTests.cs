using System.Diagnostics;
using System.IO.Compression;
using Archiver.CLI;
using Archiver.CLI.Tests.Subprocess;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.CLI.Tests;

/// <summary>
/// T-F160: `pakko x`'s interactive overwrite prompt, modeled on real 7-Zip's console
/// (NanaZip-vendored UI/Console/UserInputUtils.cpp ScanUserYesNoAllQuit): "(Y)es / (N)o /
/// (A)lways / (S)kip all / A(u)to rename all / (Q)uit?". Tested through an injected line source —
/// the Subprocess/ layer always redirects the child's stdin, so the real prompt is unreachable
/// end-to-end (same constraint as CliPasswordPromptTests).
/// </summary>
public sealed class CliConflictPromptTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("pakko-cli-conflict-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static Func<string?> Lines(params string?[] lines)
    {
        var queue = new Queue<string?>(lines);
        return () => queue.Count > 0 ? queue.Dequeue() : null;
    }

    private static ConflictInfo Conflict(string path = @"C:\out\a.txt") => new() { ExistingPath = path };

    [Theory]
    [InlineData("y", ConflictResolution.Overwrite, false)]
    [InlineData("n", ConflictResolution.Skip, false)]
    [InlineData("a", ConflictResolution.Overwrite, true)]
    [InlineData("s", ConflictResolution.Skip, true)]
    [InlineData("u", ConflictResolution.Rename, true)]
    [InlineData("Y", ConflictResolution.Overwrite, false)]
    [InlineData("  u  ", ConflictResolution.Rename, true)]
    public void Ask_EachSevenZipAnswer_MapsToTheMatchingDecision(string answer, ConflictResolution expected, bool applyToAll)
    {
        ConflictDecision? decision = CliConflictPrompt.Ask(Conflict(), Lines(answer), _ => { });

        decision.Should().NotBeNull();
        decision!.Resolution.Should().Be(expected);
        decision.ApplyToAll.Should().Be(applyToAll);
    }

    [Theory]
    [InlineData("q")]
    [InlineData("Q")]
    [InlineData(null)] // EOF — 7z treats it as quit too (NUserAnswerMode::kEof -> E_ABORT)
    public void Ask_QuitOrEndOfInput_ReturnsNull(string? answer)
    {
        CliConflictPrompt.Ask(Conflict(), Lines(answer), _ => { }).Should().BeNull();
    }

    [Fact]
    public void Ask_InvalidAnswers_RePromptUntilAValidOne()
    {
        var written = new List<string>();

        ConflictDecision? decision = CliConflictPrompt.Ask(Conflict(), Lines("maybe", "", "yes", "n"), written.Add);

        decision!.Resolution.Should().Be(ConflictResolution.Skip);
        written.Count(w => w.Contains("(Y)es / (N)o / (A)lways / (S)kip all / A(u)to rename all / (Q)uit?", StringComparison.Ordinal))
            .Should().Be(4);
    }

    [Fact]
    public void Ask_PromptNamesTheExistingFile()
    {
        var written = new List<string>();

        CliConflictPrompt.Ask(Conflict(@"C:\out\report.txt"), Lines("n"), written.Add);

        string.Concat(written).Should().Contain("Would you like to replace the existing file")
            .And.Contain(@"C:\out\report.txt");
    }

    [Fact]
    public async Task CreateResolver_Quit_CancelsTheOperationAndDeclinesTheCurrentFile()
    {
        using var quit = new CancellationTokenSource();
        var resolver = CliConflictPrompt.CreateResolver(Lines("q"), _ => { }, quit);

        ConflictDecision decision = await resolver.ResolveAsync(Conflict());

        quit.IsCancellationRequested.Should().BeTrue();
        decision.Resolution.Should().Be(ConflictResolution.Skip);
    }

    // ExtractionRouter makes separate Core calls for the zip and the tar in one `pakko x a.zip
    // b.tar` — Core's own ConflictResolver forgets "Always" between them, so the CLI's resolver
    // must carry it across (StickyCallback). Both archives hold one conflicting root file.
    [RequiresTarExe]
    public async Task CreateResolver_AlwaysAcrossZipAndTar_PromptsOnceAndOverwritesBoth()
    {
        string zipPath = Path.Combine(_temp, "one.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("from-zip.txt").Open()))
            writer.Write("zip content");

        string tarSource = Path.Combine(_temp, "tar-src");
        Directory.CreateDirectory(tarSource);
        File.WriteAllText(Path.Combine(tarSource, "from-tar.txt"), "tar content");
        string tarPath = Path.Combine(_temp, "two.tar");
        using (var tar = Process.Start(new ProcessStartInfo(@"C:\Windows\System32\tar.exe")
        {
            ArgumentList = { "-cf", tarPath, "-C", tarSource, "from-tar.txt" },
            UseShellExecute = false,
            CreateNoWindow = true,
        })!)
        {
            await tar.WaitForExitAsync();
            tar.ExitCode.Should().Be(0);
        }

        string dest = Path.Combine(_temp, "dest");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "from-zip.txt"), "old");
        File.WriteAllText(Path.Combine(dest, "from-tar.txt"), "old");

        int prompts = 0;
        using var quit = new CancellationTokenSource();
        var resolver = CliConflictPrompt.CreateResolver(() => { prompts++; return "a"; }, _ => { }, quit);

        var tarService = new TarSandboxedService();
        var router = new ExtractionRouter(new ZipArchiveService(), tarService, await tarService.DetectCapabilitiesAsync());
        var result = await router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zipPath, tarPath],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = resolver.ResolveAsync,
        });

        result.Errors.Should().BeEmpty();
        prompts.Should().Be(1);
        File.ReadAllText(Path.Combine(dest, "from-zip.txt")).Should().Be("zip content");
        File.ReadAllText(Path.Combine(dest, "from-tar.txt")).Should().Be("tar content");
    }

    private string MakeZip(string name, params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(_temp, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
            writer.Write(content);
        }
        return path;
    }

    private async Task<string> MakeTarAsync(string name, params (string Name, string Content)[] entries)
    {
        string source = Path.Combine(_temp, name + "-src");
        Directory.CreateDirectory(source);
        var startInfo = new ProcessStartInfo(@"C:\Windows\System32\tar.exe") { UseShellExecute = false, CreateNoWindow = true };
        string path = Path.Combine(_temp, name);
        startInfo.ArgumentList.Add("-cf");
        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(source);
        foreach (var (entryName, content) in entries)
        {
            File.WriteAllText(Path.Combine(source, entryName), content);
            startInfo.ArgumentList.Add(entryName);
        }
        using var tar = Process.Start(startInfo)!;
        await tar.WaitForExitAsync();
        tar.ExitCode.Should().Be(0);
        return path;
    }

    // Mirrors Program.RunExtractAsync: the same token the prompt cancels is passed to the router.
    private static async Task ExtractWithQuitAnswerAsync(string dest, params string[] archives)
    {
        using var quit = new CancellationTokenSource();
        var resolver = CliConflictPrompt.CreateResolver(Lines("q"), _ => { }, quit);
        var tarService = new TarSandboxedService();
        var router = new ExtractionRouter(new ZipArchiveService(), tarService, await tarService.DetectCapabilitiesAsync());
        try
        {
            await router.ExtractAsync(new ExtractOptions
            {
                ArchivePaths = archives,
                DestinationFolder = dest,
                Mode = ExtractMode.SingleFolder,
                OnConflict = ConflictBehavior.Ask,
                ResolveConflictAsync = resolver.ResolveAsync,
            }, progress: null, quit.Token);
        }
        catch (OperationCanceledException)
        {
            // Program.RunExtractAsync maps this to exit 255 — either outcome is fine here.
        }
        quit.IsCancellationRequested.Should().BeTrue();
    }

    private string DestWithExisting(string name)
    {
        string dest = Path.Combine(_temp, "dest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, name), "old");
        return dest;
    }

    // T-F160 (advisor-caught): "Q" must mean nothing further is written — not just "this one file
    // is skipped". The tar move phase places files one at a time after the conflict prompt.
    [RequiresTarExe]
    public async Task Quit_InTar_WritesNoFurtherFiles()
    {
        string tar = await MakeTarAsync("q.tar", ("a_conflict.txt", "new"), ("b_fresh.txt", "new"));
        string dest = DestWithExisting("a_conflict.txt");

        await ExtractWithQuitAnswerAsync(dest, tar);

        File.ReadAllText(Path.Combine(dest, "a_conflict.txt")).Should().Be("old");
        File.Exists(Path.Combine(dest, "b_fresh.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Quit_InZip_WritesNoFurtherFiles()
    {
        string zip = MakeZip("q.zip", ("a_conflict.txt", "new"), ("b_fresh.txt", "new"));
        string dest = DestWithExisting("a_conflict.txt");

        await ExtractWithQuitAnswerAsync(dest, zip);

        File.ReadAllText(Path.Combine(dest, "a_conflict.txt")).Should().Be("old");
        File.Exists(Path.Combine(dest, "b_fresh.txt")).Should().BeFalse();
    }

    [RequiresTarExe]
    public async Task Quit_InZip_TarInTheSameRunIsNotExtracted()
    {
        string zip = MakeZip("first.zip", ("a_conflict.txt", "new"));
        string tar = await MakeTarAsync("second.tar", ("from_tar.txt", "new"));
        string dest = DestWithExisting("a_conflict.txt");

        await ExtractWithQuitAnswerAsync(dest, zip, tar);

        File.Exists(Path.Combine(dest, "from_tar.txt")).Should().BeFalse();
    }
}
