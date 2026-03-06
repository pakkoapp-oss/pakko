using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.Core.Tests.Models;

public sealed class ArchiveOptionsTests
{
    [Fact]
    public void ArchiveOptions_Defaults_AreCorrect()
    {
        var options = new ArchiveOptions();

        options.SourcePaths.Should().BeEmpty();
        options.DestinationFolder.Should().Be(string.Empty);
        options.ArchiveName.Should().BeNull();
        options.Mode.Should().Be(ArchiveMode.SingleArchive);
        options.OnConflict.Should().Be(ConflictBehavior.Ask);
        options.OpenDestinationFolder.Should().BeFalse();
        options.DeleteSourceFiles.Should().BeFalse();
    }

    [Fact]
    public void ExtractOptions_Defaults_AreCorrect()
    {
        var options = new ExtractOptions();

        options.ArchivePaths.Should().BeEmpty();
        options.Mode.Should().Be(ExtractMode.SeparateFolders);
        options.DeleteArchiveAfterExtraction.Should().BeFalse();
    }
}
