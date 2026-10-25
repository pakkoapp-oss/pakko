using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F390: for a name on its list that is gone, tar.exe prints "Couldn't visit directory" with a
/// name that is not the missing one (empty, or bytes of its own environment). Pakko names the
/// source itself and never shows that line.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceVanishedSourceTests : IDisposable
{
    private const string LeakingLine = "Couldn't visit directory";
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task<ArchiveResult> CompressWithSourceRemovedBeforeLaunchAsync(ArchiveContainerFormat format, string vanishing, string staying)
    {
        var sut = new TarSandboxedService(
            new GroupPolicyOptions(),
            () => Task.FromResult(new TarCapabilities()),
            beforeCreationLaunch: () => File.Delete(vanishing));

        return await sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [vanishing, staying],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = "out",
            Format = format,
        });
    }

    [Integration]
    public async Task CompressAsync_SourceRemovedJustBeforeTarStarts_IsNamedByPakkoAndTarsLineIsNotShown()
    {
        foreach (ArchiveContainerFormat format in new[] { ArchiveContainerFormat.Tar, ArchiveContainerFormat.TarGz })
        {
            string vanishing = Path.Combine(_temp.Path, "vanishing.txt");
            string staying = Path.Combine(_temp.Path, "staying.txt");
            File.WriteAllText(vanishing, "v");
            File.WriteAllText(staying, "s");

            ArchiveResult result = await CompressWithSourceRemovedBeforeLaunchAsync(format, vanishing, staying);

            result.Success.Should().BeFalse();
            result.CreatedFiles.Should().BeEmpty();
            result.Errors.Should().Contain(e => e.Text!.Code == MessageCode.SourceNotFound && e.SourcePath == vanishing);
            result.Errors.Should().NotContain(e => e.Message.Contains(LeakingLine));
            Directory.GetFileSystemEntries(Path.Combine(_temp.Path, "out")).Should().BeEmpty();
        }
    }

    [Integration]
    public async Task CompressAsync_FolderRemovedJustBeforeTarStarts_IsNamedByPakko()
    {
        string vanishing = Path.Combine(_temp.Path, "folder");
        Directory.CreateDirectory(vanishing);
        File.WriteAllText(Path.Combine(vanishing, "inner.txt"), "i");
        string staying = Path.Combine(_temp.Path, "staying.txt");
        File.WriteAllText(staying, "s");
        var sut = new TarSandboxedService(
            new GroupPolicyOptions(),
            () => Task.FromResult(new TarCapabilities()),
            beforeCreationLaunch: () => Directory.Delete(vanishing, recursive: true));

        ArchiveResult result = await sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [vanishing, staying],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = "out",
            Format = ArchiveContainerFormat.Tar,
        });

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Text!.Code == MessageCode.SourceNotFound && e.SourcePath == vanishing);
        result.Errors.Should().NotContain(e => e.Message.Contains(LeakingLine));
    }

    // T-F429: every stderr line reached the progress as the "current file", so tar.exe's line for
    // the missing entry - a piece of the environment block in every run (T-F396) - was shown live.
    [Integration]
    public async Task CompressAsync_SourceRemovedJustBeforeTarStarts_ProgressNamesOnlyRealEntries()
    {
        string vanishing = Path.Combine(_temp.Path, "folder");
        Directory.CreateDirectory(vanishing);
        File.WriteAllText(Path.Combine(vanishing, "inner.txt"), "i");
        string staying = Path.Combine(_temp.Path, "staying.txt");
        File.WriteAllText(staying, "s");
        var sut = new TarSandboxedService(
            new GroupPolicyOptions(),
            () => Task.FromResult(new TarCapabilities()),
            beforeCreationLaunch: () => Directory.Delete(vanishing, recursive: true));
        var currentFiles = new List<string?>();

        await sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [vanishing, staying],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = "out",
            Format = ArchiveContainerFormat.Tar,
        }, new SyncProgress(report => currentFiles.Add(report.CurrentFile)));

        currentFiles.Should().OnlyContain(name => name == null || name == "staying.txt");
    }

    // Synchronous, not System.Progress<T>: its callback posts to the pool (T-F162).
    private sealed class SyncProgress(Action<ProgressReport> onReport) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) => onReport(value);
    }
}
