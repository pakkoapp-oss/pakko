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
    public async Task AnUnsignedArchiveint_IsRefused_BeforeTheHelperIsCopied()
    {
        string archive = Tar(("a.txt", "A"));
        using TarSandboxScope scope = await Scope(archive, needsOutputDir: false);
        scope.VerifyArchiveint = _ => false;

        Func<Task> run = () => scope.RunHelperAsync(ArchiveHelperMode.List, ["--format", "tar"], null, new ArchiveHelperOutput(ArchiveHelperMode.List), CancellationToken.None);

        (await run.Should().ThrowAsync<TarSignatureVerificationException>()).WithMessage("*archiveint.dll*");
        Directory.Exists(Path.Combine(scope.QuarantineRoot, "bin")).Should().BeFalse();
    }

    // Cancelled while the helper runs, seen running: Pakko holds its first "K" line, so the helper
    // fills its stdout pipe and waits. Once the run ends, its copy in bin\ is no longer a mapped image
    // and can be deleted: no helper is left.
    [Fact]
    public async Task ACancelledRun_LeavesNoHelperRunning()
    {
        const int Entries = 20_000;
        string archive = Path.Combine(_temp.Path, "many.tar.gz");
        using (var tar = new MemoryStream())
        {
            for (int i = 0; i < Entries; i++)
                tar.Write(UstarHeader("f" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), 0));
            tar.Write(new byte[1024]);
            File.WriteAllBytes(archive, Gzip(tar.ToArray()));
        }
        using TarSandboxScope scope = await Scope(archive, needsOutputDir: false);
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Test, (_, _) =>
        {
            firstRead.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
        });
        using var cancel = new CancellationTokenSource();

        Task run = scope.RunHelperAsync(ArchiveHelperMode.Test, ["--format", "tar", "--filter", "gzip"], [.. Enumerable.Range(0, Entries).Select(i => (long)i)], output, cancel.Token);
        await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(30));
        bool seenRunning = !run.IsCompleted;
        await cancel.CancelAsync();
        release.Set();

        seenRunning.Should().BeTrue("the helper waits on its full stdout pipe");
        Func<Task> ended = () => run;
        await ended.Should().ThrowAsync<OperationCanceledException>();
        File.Delete(Path.Combine(scope.QuarantineRoot, "bin", "Archiver.ArchiveHelper.exe"));
    }

    // The copy is held from its hash until the helper ends: a running exe can be renamed, and a name
    // freed that way could take another file between the hash and the launch.
    [Fact]
    public async Task TheCopy_CannotBeRenamedWhileItRuns()
    {
        string archive = Tar(("a.txt", "A"));
        using TarSandboxScope scope = await Scope(archive, needsOutputDir: false);
        string copy = Path.Combine(scope.QuarantineRoot, "bin", "Archiver.ArchiveHelper.exe");
        Exception? renameFailure = null;
        bool renamed = false;
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Test, (_, _) =>
        {
            try
            {
                File.Move(copy, copy + ".old");
                renamed = true;
            }
            catch (IOException ex)
            {
                renameFailure = ex;
            }
        });

        (ArchiveHelperExit exit, _) = await scope.RunHelperAsync(ArchiveHelperMode.Test, ["--format", "tar"], [0], output, CancellationToken.None);

        exit.Should().Be(ArchiveHelperExit.Done);
        renamed.Should().BeFalse();
        renameFailure.Should().NotBeNull();
    }

    // --- Harness ---

    private static byte[] Gzip(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(buffer, System.IO.Compression.CompressionLevel.Fastest))
            gzip.Write(data);
        return buffer.ToArray();
    }

    // One ustar header for a regular file of the given size (octal, 11 digits).
    private static byte[] UstarHeader(string name, long size)
    {
        byte[] h = new byte[512];
        Encoding.ASCII.GetBytes(name).CopyTo(h, 0);
        Encoding.ASCII.GetBytes("0000644\0").CopyTo(h, 100);
        Encoding.ASCII.GetBytes(Convert.ToString(size, 8).PadLeft(11, '0') + "\0").CopyTo(h, 124);
        Encoding.ASCII.GetBytes("14540000000\0").CopyTo(h, 136);
        h[156] = (byte)'0';
        Encoding.ASCII.GetBytes("ustar\000").CopyTo(h, 257);
        "        "u8.CopyTo(h.AsSpan(148));
        int sum = h.Sum(b => b);
        Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(h, 148);
        return h;
    }

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
