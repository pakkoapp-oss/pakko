using Archiver.Core.IO;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F312: an archive is written to a temporary file next to it and renamed into place. A sync client,
/// a backup program or an antivirus scanner can hold either file for a moment, so the rename retries
/// briefly before it gives up.
/// </summary>
public sealed class ArchiveTempFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Create_NamesAFreshOwnedFileNextToTheArchive_WithoutCreatingIt()
    {
        string dest = Path.Combine(_temp.Path, "big3.zip");

        string temp = ArchiveTempFile.Create(dest);

        Path.GetDirectoryName(temp).Should().Be(_temp.Path);
        Path.GetFileName(temp).Should().StartWith(".pakko-a-" + TempOwner.CurrentTag + "-").And.EndWith(".tmp");
        File.Exists(temp).Should().BeFalse();
    }

    [Fact]
    public void Create_SweepsALeftoverOfADeadRunOnThisMachine()
    {
        string machine = TempOwner.CurrentTag.Split('-')[0];
        string leftover = _temp.CreateFile($".pakko-a-{machine}-{Environment.ProcessId}-1-0123456789abcdef0123456789abcdef.tmp");

        ArchiveTempFile.Create(Path.Combine(_temp.Path, "big3.zip"));

        File.Exists(leftover).Should().BeFalse("a process id with another start time is a reused id: its owner is gone");
    }

    [Fact]
    public async Task CommitAsync_DestinationHeldBriefly_Commits()
    {
        string temp = _temp.CreateFile("t.tmp", "new archive");
        string dest = _temp.CreateFile("a.zip", "old archive");
        var held = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Delay(300).ContinueWith(_ => held.Dispose(), TaskScheduler.Default);

        await ArchiveTempFile.CommitAsync(temp, dest, CancellationToken.None);

        File.ReadAllText(dest).Should().Be("new archive");
        File.Exists(temp).Should().BeFalse();
    }

    [Fact]
    public async Task CommitAsync_TempHeldBriefly_Commits()
    {
        string temp = _temp.CreateFile("t.tmp", "new archive");
        string dest = Path.Combine(_temp.Path, "a.zip");
        var held = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
        _ = Task.Delay(300).ContinueWith(_ => held.Dispose(), TaskScheduler.Default);

        await ArchiveTempFile.CommitAsync(temp, dest, CancellationToken.None);

        File.ReadAllText(dest).Should().Be("new archive");
    }

    [Fact]
    public async Task CommitAsync_DestinationHeldThroughout_ThrowsAfterRetrying()
    {
        string temp = _temp.CreateFile("t.tmp", "new archive");
        string dest = _temp.CreateFile("a.zip", "old archive");
        using var held = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None);

        Func<Task> act = () => ArchiveTempFile.CommitAsync(temp, dest, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().Where(e => e is IOException || e is UnauthorizedAccessException);
        File.Exists(temp).Should().BeTrue("the caller removes the temporary file");
    }

    [Fact]
    public async Task CommitAsync_MissingTemp_ThrowsAtOnce()
    {
        string dest = Path.Combine(_temp.Path, "a.zip");
        var started = DateTime.UtcNow;

        Func<Task> act = () => ArchiveTempFile.CommitAsync(Path.Combine(_temp.Path, "none.tmp"), dest, CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromMilliseconds(90), "only a held file is worth waiting for");
    }

    [Fact]
    public async Task CommitAsync_CancelledWhileHeld_ThrowsInsteadOfWaiting()
    {
        string temp = _temp.CreateFile("t.tmp", "new archive");
        string dest = _temp.CreateFile("a.zip", "old archive");
        using var held = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None);
        Func<Task> act = () => ArchiveTempFile.CommitAsync(temp, dest, new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
