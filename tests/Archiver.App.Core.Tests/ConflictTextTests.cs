using System.Globalization;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F220 item 2: the App's conflict dialog named only the file; the Explorer window already showed
// both files' size and date (OperationWindowText). Same shape here, in the App's own words.
public sealed class ConflictTextTests
{
    private static readonly DateTimeOffset When = new(2026, 10, 3, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Details_SizeAndDate()
    {
        string expectedDate = When.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

        ConflictText.Details(2048, When).Should().Be($"{DisplayText.FormatSize(2048)} · modified {expectedDate}");
    }

    [Fact]
    public void Details_SizeOnly() =>
        ConflictText.Details(10, null).Should().Be("10 B");

    [Fact]
    public void Details_NothingKnown_IsNull() =>
        ConflictText.Details(null, null).Should().BeNull();

    [Fact]
    public void Existing_ReadsTheFileOnDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pakko-conflicttext-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "12345");
        try
        {
            (long? size, DateTimeOffset? modified) = ConflictText.Existing(path);
            size.Should().Be(5);
            modified.Should().NotBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Existing_MissingFile_IsUnknown() =>
        ConflictText.Existing(Path.Combine(Path.GetTempPath(), $"pakko-missing-{Guid.NewGuid():N}")).Should().Be((null, null));
}
