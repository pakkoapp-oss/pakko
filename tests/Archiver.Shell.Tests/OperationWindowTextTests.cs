using Archiver.Core.Models;
using Archiver.OperationUi.Protocol;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F268 step 5: the conflict prompt's "newer" mark.
public sealed class OperationWindowTextTests : IDisposable
{
    private static readonly DateTime ExistingUtc = new(2026, 9, 12, 11, 3, 0, DateTimeKind.Utc);

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("PakkoWindowTextTests");
    private readonly string _existing;

    public OperationWindowTextTests()
    {
        _existing = Path.Combine(_temp.FullName, "a.txt");
        File.WriteAllText(_existing, "x");
        File.SetLastWriteTimeUtc(_existing, ExistingUtc);
    }

    public void Dispose() => _temp.Delete(recursive: true);

    // ZIP keeps times in 2-second steps: the same file read back can differ by up to 2 s.
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(-3600, false)]
    public void Newer_OnlyBeyondTheZipTimeStep(int incomingSecondsLater, bool newer)
    {
        var info = new ConflictInfo
        {
            ExistingPath = _existing,
            IncomingModified = new DateTimeOffset(ExistingUtc.AddSeconds(incomingSecondsLater)),
        };

        OperationWindowText.CreateAskConflict(1, info).IncomingIsNewer.Should().Be(newer);
    }

    [Fact]
    public void Newer_ComparesInstantsNotLocalClockReadings()
    {
        var info = new ConflictInfo
        {
            ExistingPath = _existing,
            // The same instant written with another offset.
            IncomingModified = new DateTimeOffset(ExistingUtc).ToOffset(TimeSpan.FromHours(3)),
        };

        OperationWindowText.CreateAskConflict(1, info).IncomingIsNewer.Should().BeFalse();
    }

    [Fact]
    public void UnknownIncomingTime_IsNeverNewer()
    {
        AskConflict ask = OperationWindowText.CreateAskConflict(1, new ConflictInfo { ExistingPath = _existing, IncomingSize = 10 });

        ask.IncomingIsNewer.Should().BeFalse();
        ask.IncomingDetails.Should().Be(ProgressText.FormatBytes(10));
        ask.ExistingDetails.Should().StartWith(ProgressText.FormatBytes(1) + " · ");
    }
}
