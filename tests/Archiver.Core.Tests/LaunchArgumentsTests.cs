using System.Text;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests;

public sealed class LaunchArgumentsTests
{
    [Theory]
    [InlineData(LaunchOperation.Browse)]
    [InlineData(LaunchOperation.Extract)]
    [InlineData(LaunchOperation.Archive)]
    public void Format_ThenTryParse_RoundTripsOperationAndFiles(LaunchOperation operation)
    {
        string[] files = [@"C:\a b\x.zip", @"\\сервер\спільна\Архів.tar", @"C:\"];

        var arguments = LaunchArguments.Format(operation, files);
        var ok = LaunchArguments.TryParse(arguments, out var parsedOperation, out var parsedFiles);

        ok.Should().BeTrue();
        parsedOperation.Should().Be(operation);
        parsedFiles.Should().Equal(files);
    }

    [Fact]
    public void Format_ContainsNoQuotesOrSpacesInsidePayload()
    {
        var arguments = LaunchArguments.Format(LaunchOperation.Extract, [@"C:\dir with space\", "a\"b"]);

        arguments.Split(' ').Should().HaveCount(2);
        arguments.Should().NotContain("\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_EmptyArguments_ReturnsFalse(string? arguments)
    {
        LaunchArguments.TryParse(arguments, out _, out var files).Should().BeFalse();
        files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--browse")]
    [InlineData("--browse ")]
    [InlineData("--browse !!!notbase64")]
    [InlineData("--delete WyJDOlxcYS56aXAiXQ==")]
    [InlineData("pakko://browse?files=WyJDOlxcYS56aXAiXQ==")]
    [InlineData("--browse WyJDOlxcYS56aXAiXQ== extra")]
    public void TryParse_MalformedOrUnknown_ReturnsFalse(string arguments)
    {
        LaunchArguments.TryParse(arguments, out _, out var files).Should().BeFalse();
        files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    [InlineData("[]")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void TryParse_PayloadNotANonEmptyStringArray_ReturnsFalse(string json)
    {
        var arguments = "--extract " + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        LaunchArguments.TryParse(arguments, out _, out var files).Should().BeFalse();
        files.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_NullAndBlankEntries_AreDropped()
    {
        var json = "[\"C:\\\\a.zip\",null,\"\",\"  \",\"C:\\\\b.zip\"]";
        var arguments = "--archive " + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var ok = LaunchArguments.TryParse(arguments, out var operation, out var files);

        ok.Should().BeTrue();
        operation.Should().Be(LaunchOperation.Archive);
        files.Should().Equal(@"C:\a.zip", @"C:\b.zip");
    }

    [Fact]
    public void TryParse_OnlyBlankEntries_ReturnsFalse()
    {
        var arguments = "--archive " + Convert.ToBase64String(Encoding.UTF8.GetBytes("[null,\"\"]"));

        LaunchArguments.TryParse(arguments, out _, out var files).Should().BeFalse();
        files.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_LongerThanMaxLength_ReturnsFalse()
    {
        var files = Enumerable.Range(0, 400).Select(i => $@"C:\{new string('x', 100)}\{i}.zip").ToArray();
        var arguments = LaunchArguments.Format(LaunchOperation.Extract, files);

        arguments.Length.Should().BeGreaterThan(LaunchArguments.MaxLength);
        LaunchArguments.TryParse(arguments, out _, out var parsed).Should().BeFalse();
        parsed.Should().BeEmpty();
    }

    // Default JsonSerializer output escapes every non-ASCII character as \uXXXX — 6 bytes, 8 base64
    // characters per Cyrillic letter — which cut a Cyrillic selection's capacity roughly threefold.
    [Fact]
    public void Format_EightyLongCyrillicPaths_FitWithinMaxLength()
    {
        var files = Enumerable.Range(0, 80).Select(i => $@"\\сервер\спільна\{new string('Ж', 100)}_{i}.zip").ToArray();

        var arguments = LaunchArguments.Format(LaunchOperation.Extract, files);

        arguments.Length.Should().BeLessThanOrEqualTo(LaunchArguments.MaxLength);
        LaunchArguments.TryParse(arguments, out _, out var parsed).Should().BeTrue();
        parsed.Should().Equal(files);
    }

    [Fact]
    public void Format_EmptyFileList_Throws()
    {
        var act = () => LaunchArguments.Format(LaunchOperation.Browse, []);

        act.Should().Throw<ArgumentException>();
    }
}
