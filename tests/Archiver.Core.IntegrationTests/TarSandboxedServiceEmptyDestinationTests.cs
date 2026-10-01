using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F309: an extraction that produced nothing — refused by the pre-scan, or cancelled — must not
/// leave behind the empty destination folder it created (ZIP's T-F230 rule). A folder that was
/// there before the run always stays.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceEmptyDestinationTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteArchive(string name, string entryName)
    {
        string archivePath = Path.Combine(_temp.Path, name);
        TarBuilder.WriteTar(archivePath, [new TarBuilder.Entry { Name = entryName, Content = Encoding.ASCII.GetBytes("x") }]);
        return archivePath;
    }

    private static ExtractOptions OptionsFor(string archivePath, string destDir) => new()
    {
        ArchivePaths = [archivePath],
        DestinationFolder = destDir,
        Mode = ExtractMode.SingleFolder,
    };

    [Integration]
    public async Task ExtractAsync_RefusedArchive_RemovesTheDestinationItCreated()
    {
        string archive = WriteArchive("evil.tar", "../evil.txt");
        string destDir = Path.Combine(_temp.Path, "fresh");

        ArchiveResult result = await _sut.ExtractAsync(OptionsFor(archive, destDir));

        result.Errors.Should().NotBeEmpty();
        Directory.Exists(destDir).Should().BeFalse();
    }

    [Integration]
    public async Task ExtractAsync_RefusedArchive_KeepsADestinationThatExisted()
    {
        string archive = WriteArchive("evil.tar", "../evil.txt");
        string destDir = Path.Combine(_temp.Path, "existing");
        Directory.CreateDirectory(destDir);

        ArchiveResult result = await _sut.ExtractAsync(OptionsFor(archive, destDir));

        result.Errors.Should().NotBeEmpty();
        Directory.Exists(destDir).Should().BeTrue();
    }

    [Integration]
    public async Task ExtractAsync_Cancelled_RemovesTheDestinationItCreated()
    {
        string archive = WriteArchive("ok.tar", "a.txt");
        string destDir = Path.Combine(_temp.Path, "cancelled");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Func<Task> act = () => _sut.ExtractAsync(OptionsFor(archive, destDir), cancellationToken: cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(destDir).Should().BeFalse();
    }

    [Integration]
    public async Task ExtractAsync_Extracted_KeepsTheDestination()
    {
        string archive = WriteArchive("ok.tar", "a.txt");
        string destDir = Path.Combine(_temp.Path, "kept");

        ArchiveResult result = await _sut.ExtractAsync(OptionsFor(archive, destDir));

        result.Errors.Should().BeEmpty();
        File.Exists(Path.Combine(destDir, "a.txt")).Should().BeTrue();
    }
}
