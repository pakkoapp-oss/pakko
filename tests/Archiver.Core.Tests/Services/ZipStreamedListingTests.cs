using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F540: a ZIP's streamed listing - each entry once, in the directory's order, as the list has
// them; what the caller's callback throws reaches it as thrown; cancellation midway throws.
public sealed class ZipStreamedListingTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pakko-zipstream-").FullName;
    private readonly ZipArchiveService _service = new(new GroupPolicyOptions());

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Streamed_PassesEachEntryOnce_AsTheListHasThem()
    {
        string zip = Zip("d/", "d/a.txt", "b.txt");
        var passed = new List<ArchiveEntryInfo>();

        ArchiveListSummary summary = await _service.ListEntriesAsync(zip, passed.Add);

        summary.Success.Should().BeTrue(summary.ErrorMessage);
        summary.Count.Should().Be(3);
        passed.Should().Equal((await _service.ListEntriesAsync(zip)).Entries);
    }

    [Fact]
    public async Task Streamed_ACallbackThatThrows_ItsExceptionReachesTheCaller()
    {
        string zip = Zip("a.txt", "b.txt");
        var closed = new IOException("the pipe is being closed");
        int calls = 0;

        Func<Task> list = () => _service.ListEntriesAsync(zip, _ =>
        {
            calls++;
            throw closed;
        });

        (await list.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(closed);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Streamed_CancelledMidway_ThrowsCancellation()
    {
        string zip = Zip("a.txt", "b.txt", "c.txt");
        using var cancel = new CancellationTokenSource();
        int calls = 0;

        Func<Task> list = () => _service.ListEntriesAsync(zip, _ =>
        {
            calls++;
            cancel.Cancel();
        }, cancel.Token);

        await list.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Streamed_NotAZip_FailsAndPassesNothing()
    {
        string notZip = Path.Combine(_folder, "text.zip");
        File.WriteAllText(notZip, "not an archive");
        int calls = 0;

        ArchiveListSummary summary = await _service.ListEntriesAsync(notZip, _ => calls++);

        summary.Success.Should().BeFalse();
        summary.ErrorText.Should().NotBeNull();
        calls.Should().Be(0);
    }

    private string Zip(params string[] names)
    {
        string path = Path.Combine(_folder, Guid.NewGuid().ToString("N")[..8] + ".zip");
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string name in names)
            zip.CreateEntry(name);
        return path;
    }
}
