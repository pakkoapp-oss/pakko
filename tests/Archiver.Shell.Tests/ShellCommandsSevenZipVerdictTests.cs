using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F342 (the App and Explorer wave): "Add to name.7z" is the one command that learns whether
// tar.exe writes 7z, so it is the one that leaves the verdict for the Explorer menu.
public sealed class ShellCommandsSevenZipVerdictTests
{
    private static readonly string Source = Path.Combine(Path.GetTempPath(), "pakko-verdict-source.txt");

    [Fact]
    public async Task Archive_SevenZip_RecordsTheVerdictOnce_AfterTheCreation()
    {
        var router = new RecordingRouter();
        var order = new List<string>();
        router.Called += () => order.Add("create");

        await Create(router, () => order.Add("record")).ArchiveAsync([Source], ArchiveContainerFormat.SevenZip);

        order.Should().Equal("create", "record");
        router.Formats.Should().Equal(ArchiveContainerFormat.SevenZip);
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip)]
    [InlineData(ArchiveContainerFormat.Tar)]
    public async Task Archive_OtherFormat_RecordsNothing(ArchiveContainerFormat format)
    {
        int recorded = 0;

        await Create(new RecordingRouter(), () => recorded++).ArchiveAsync([Source], format);

        recorded.Should().Be(0);
    }

    [Fact]
    public async Task Archive_SevenZip_NoSources_RecordsNothing()
    {
        int recorded = 0;

        await Create(new RecordingRouter(), () => recorded++).ArchiveAsync([], ArchiveContainerFormat.SevenZip);

        recorded.Should().Be(0);
    }

    // A refusal (no writer) is exactly the verdict the menu needs.
    [Fact]
    public async Task Archive_SevenZip_CreationRefused_StillRecords()
    {
        int recorded = 0;
        var router = new RecordingRouter
        {
            Result = new ArchiveResult { Errors = [new ArchiveError { SourcePath = Source, Message = "This system's tar.exe cannot create 7z archives." }] },
        };

        await Create(router, () => recorded++).ArchiveAsync([Source], ArchiveContainerFormat.SevenZip);

        recorded.Should().Be(1);
    }

    private static ShellCommands Create(IArchiveCreationRouter router, Action record) => new(
        new FakeOperationUi(),
        new ShellServices
        {
            CreateExtractionRouterAsync = () => throw new NotSupportedException(),
            CreateArchiveCreationRouter = () => router,
            CreateScanServiceAsync = () => throw new NotSupportedException(),
            CreateRecoveryService = () => throw new NotSupportedException(),
            LaunchApp = (_, _) => AppLaunchResult.Launched,
            RecordSevenZipWriterAsync = () =>
            {
                record();
                return Task.CompletedTask;
            },
        });

    private sealed class RecordingRouter : IArchiveCreationRouter
    {
        public event Action? Called;

        public List<ArchiveContainerFormat> Formats { get; } = [];

        public ArchiveResult Result { get; init; } = new();

        public Task<ArchiveResult> ArchiveAsync(
            ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        {
            Formats.Add(options.Format);
            Called?.Invoke();
            return Task.FromResult(Result);
        }
    }
}
