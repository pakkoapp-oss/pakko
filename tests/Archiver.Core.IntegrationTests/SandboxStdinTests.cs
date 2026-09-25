using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F233: the archive reaches the sandboxed tar.exe only as an inherited stdin handle
/// ("-f -"), opened by Pakko itself. The AppContainer gets no ACE on the user's file and no path
/// to it — the folder holding the archive here grants the AppContainer nothing at all. 7z needs
/// seeking, which works because stdin is a real file handle, not a pipe.
/// </summary>
[Collection("TarSandbox")]
public sealed class SandboxStdinTests : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";

    private readonly TempDirectory _temp = new();
    private readonly AppContainerProfile _profile = new("Pakko.TarSandbox.Test." + Guid.NewGuid());

    public void Dispose()
    {
        _temp.Dispose();
        try { _profile.Delete(); } catch { }
    }

    [Theory]
    [InlineData("gz")]
    [InlineData("7z")]
    public async Task ArchiveAsStdin_NoGrantOnArchive_ListsAndExtractsInsideAppContainer(string kind)
    {
        _profile.EnsureExists();
        using var sid = _profile.GetSid();

        string privateDir = Path.Combine(_temp.Path, "private");
        Directory.CreateDirectory(privateDir);
        string archivePath = Path.Combine(privateDir, "archive." + kind);
        if (kind == "7z")
            File.Copy(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "valid.7z"), archivePath);
        else
            ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-czf", [("a.txt", "stdin content")]);

        string root = Path.Combine(_temp.Path, "q");
        string outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        QuarantineAcl.GrantTraverseOnly(_temp.Path, sid);
        QuarantineAcl.GrantTraverseListReadAttributes(root, sid);
        QuarantineAcl.GrantModify(outDir, sid);

        using (var list = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var (exitCode, stdOut, stdErr) = await SandboxedProcessLauncher.RunAsync(
                TarExecutablePath, ["-tf", "-"],
                new ProcessLaunchOptions(AppContainerSid: sid, StdIn: list.SafeFileHandle, WorkingDirectory: root),
                CancellationToken.None);
            exitCode.Should().Be(0, because: stdErr);
            stdOut.Should().NotBeNullOrWhiteSpace();
        }

        using (var extract = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var (exitCode, _, stdErr) = await SandboxedProcessLauncher.RunAsync(
                TarExecutablePath, ["-xf", "-", "-C", "out"],
                new ProcessLaunchOptions(AppContainerSid: sid, StdIn: extract.SafeFileHandle, WorkingDirectory: root),
                CancellationToken.None);
            exitCode.Should().Be(0, because: stdErr);
        }

        Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Should().NotBeEmpty();
    }

    // Negative control: the same archive by PATH is unreadable to the AppContainer — so the
    // stdin success above really comes from the handle, not from some inherited grant.
    [Fact]
    public async Task ArchiveByPath_NoGrantOnArchive_Fails()
    {
        _profile.EnsureExists();
        using var sid = _profile.GetSid();

        string privateDir = Path.Combine(_temp.Path, "private");
        Directory.CreateDirectory(privateDir);
        string archivePath = Path.Combine(privateDir, "archive.tar.gz");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-czf", [("a.txt", "x")]);
        QuarantineAcl.GrantTraverseOnly(_temp.Path, sid);

        var (exitCode, _, _) = await SandboxedProcessLauncher.RunAsync(
            TarExecutablePath, ["-tf", archivePath],
            new ProcessLaunchOptions(AppContainerSid: sid), CancellationToken.None);

        exitCode.Should().NotBe(0);
    }
}
