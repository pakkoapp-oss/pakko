using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F214: the date columns of a "tar -tvf" line — month abbreviation in the user's locale, day,
/// then "HH:mm" within half a year of now or the year otherwise (bsdtar's own rule).
/// </summary>
public sealed class TarListingDateTests
{
    private static readonly string[] Ukrainian =
        ["Січ", "Лют", "Бер", "Кві", "Тра", "Чер", "Лип", "Сер", "Вер", "Жов", "Лис", "Гру"];

    private static readonly string[] English =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly DateTime Now = new(2026, 9, 29, 16, 0, 0);

    [Fact]
    public void Parse_RecentEntry_ReturnsDateAndMinuteInThisYear()
    {
        DateTime? result = TarListingDate.Parse("-rw-rw-rw-  0 0      0           1 Вер 03 07:05 m9.txt", Ukrainian, Now);

        result.Should().Be(new DateTime(2026, 9, 3, 7, 5, 0));
    }

    [Fact]
    public void Parse_OldEntry_ReturnsDateWithTheListedYear()
    {
        DateTime? result = TarListingDate.Parse("-rw-rw-rw-  0 0      0           1 Січ 03  2025 m1.txt", Ukrainian, Now);

        result.Should().Be(new DateTime(2025, 1, 3));
    }

    [Fact]
    public void Parse_RecentEntryFromLastDecember_InJanuary_ReturnsLastYear()
    {
        DateTime? result = TarListingDate.Parse("-rw-r--r--  0 0 0 5 Dec 20 10:00 a.txt", English, new DateTime(2026, 1, 10));

        result.Should().Be(new DateTime(2025, 12, 20, 10, 0, 0));
    }

    [Fact]
    public void Parse_RecentEntryFromNextJanuary_InDecember_ReturnsNextYear()
    {
        DateTime? result = TarListingDate.Parse("-rw-r--r--  0 0 0 5 Jan 05 08:00 a.txt", English, new DateTime(2026, 12, 20));

        result.Should().Be(new DateTime(2027, 1, 5, 8, 0, 0));
    }

    [Fact]
    public void Parse_DirectoryAndSymlinkLines_ReadTheSameColumns()
    {
        TarListingDate.Parse("drwxrwxrwx  0 0      0           0 Вер 29 16:46 src/", Ukrainian, Now)
            .Should().Be(new DateTime(2026, 9, 29, 16, 46, 0));
        TarListingDate.Parse("lrwxrwxrwx  0 user group 0 Mar 07  2021 link -> target", English, Now)
            .Should().Be(new DateTime(2021, 3, 7));
    }

    [Fact]
    public void Parse_MonthNameWithASpace_IsMatchedWhole()
    {
        string[] spaced = ["Thg 1", "Thg 2", "Thg 3", "Thg 4", "Thg 5", "Thg 6", "Thg 7", "Thg 8", "Thg 9", "Thg 10", "Thg 11", "Thg 12"];

        TarListingDate.Parse("-rw-r--r--  0 0 0 5 Thg 11 02  2024 a.txt", spaced, Now)
            .Should().Be(new DateTime(2024, 11, 2));
    }

    [Fact]
    public void Parse_NameThatLooksLikeADate_DoesNotChangeTheResult()
    {
        TarListingDate.Parse("-rw-r--r--  0 0 0 5 Feb 01  2020 Mar 09 12:00 x", English, Now)
            .Should().Be(new DateTime(2020, 2, 1));
    }

    [Theory]
    [InlineData("-rw-r--r--  0 0 0 5 Xyz 03 07:05 a.txt")] // month in another locale
    [InlineData("-rw-r--r--  0 0 0 5 Feb 30  2024 a.txt")] // no such day
    [InlineData("-rw-r--r--  0 0 0 5 Feb 03 25:05 a.txt")] // no such hour
    [InlineData("-rw-r--r--  0 0 0 5 Feb 03 7.05 a.txt")]
    [InlineData("-rw-r--r--  0 0 0 5 Feb")]
    [InlineData("-rw-r--r--  0 0 0 5")]
    [InlineData("")]
    public void Parse_UnreadableDate_ReturnsNull(string line)
    {
        TarListingDate.Parse(line, English, Now).Should().BeNull();
    }

    [Theory]
    [InlineData("-rw-r--r--  0 0      0           1 Jan 01  1970 z.txt")] // east of UTC
    [InlineData("-rw-r--r--  0 0      0           1 Dec 31  1969 z.txt")] // west of UTC
    public void Parse_NoStoredTime_ReturnsNull(string line)
    {
        // A 7z made with -mtm=off stores no time; tar.exe prints the local Unix epoch for it.
        TarListingDate.Parse(line, English, Now).Should().BeNull();
    }

    [Fact]
    public void Parse_NoMonthNames_ReturnsNull()
    {
        TarListingDate.Parse("-rw-r--r--  0 0 0 5 Feb 03 07:05 a.txt", [], Now).Should().BeNull();
    }

    [Fact]
    public void UserMonthNames_HasTwelveNonEmptyNames()
    {
        TarListingDate.UserMonthNames.Should().HaveCount(12).And.OnlyContain(name => name.Length > 0);
    }
}
