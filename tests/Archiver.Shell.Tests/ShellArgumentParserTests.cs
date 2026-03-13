using FluentAssertions;

namespace Archiver.Shell.Tests;

public sealed class ShellArgumentParserTests
{
    // --- Valid: --extract-here ---

    [Fact]
    public void ExtractHere_SingleFile_ReturnsExtractHere()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--extract-here", "archive.zip"]);

        result.Type.Should().Be(CommandType.ExtractHere);
        result.Files.Should().Equal("archive.zip");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ExtractHere_MultipleFiles_ReturnsAllFiles()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--extract-here", "a.zip", "b.zip", "c.zip"]);

        result.Type.Should().Be(CommandType.ExtractHere);
        result.Files.Should().Equal("a.zip", "b.zip", "c.zip");
    }

    // --- Valid: --extract-folder ---

    [Fact]
    public void ExtractFolder_SingleFile_ReturnsExtractFolder()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--extract-folder", "archive.zip"]);

        result.Type.Should().Be(CommandType.ExtractFolder);
        result.Files.Should().Equal("archive.zip");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ExtractFolder_MultipleFiles_ReturnsAllFiles()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--extract-folder", "a.zip", "b.zip"]);

        result.Type.Should().Be(CommandType.ExtractFolder);
        result.Files.Should().Equal("a.zip", "b.zip");
    }

    // --- Valid: --archive ---

    [Fact]
    public void Archive_SingleFile_ReturnsArchive()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--archive", "document.txt"]);

        result.Type.Should().Be(CommandType.Archive);
        result.Files.Should().Equal("document.txt");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Archive_MultipleFiles_ReturnsAllFiles()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--archive", "file1.txt", "file2.docx", "image.png"]);

        result.Type.Should().Be(CommandType.Archive);
        result.Files.Should().Equal("file1.txt", "file2.docx", "image.png");
    }

    // --- Valid: --open-ui --extract ---

    [Fact]
    public void OpenUiExtract_SingleFile_ReturnsOpenUiExtract()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--open-ui", "--extract", "archive.zip"]);

        result.Type.Should().Be(CommandType.OpenUiExtract);
        result.Files.Should().Equal("archive.zip");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void OpenUiExtract_MultipleFiles_ReturnsAllFiles()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--open-ui", "--extract", "a.zip", "b.zip"]);

        result.Type.Should().Be(CommandType.OpenUiExtract);
        result.Files.Should().Equal("a.zip", "b.zip");
    }

    // --- Valid: --open-ui --archive ---

    [Fact]
    public void OpenUiArchive_SingleFile_ReturnsOpenUiArchive()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--open-ui", "--archive", "document.txt"]);

        result.Type.Should().Be(CommandType.OpenUiArchive);
        result.Files.Should().Equal("document.txt");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void OpenUiArchive_MultipleFiles_ReturnsAllFiles()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--open-ui", "--archive", "file1.txt", "file2.docx"]);

        result.Type.Should().Be(CommandType.OpenUiArchive);
        result.Files.Should().Equal("file1.txt", "file2.docx");
    }

    // --- Invalid: no arguments ---

    [Fact]
    public void NoArguments_ReturnsInvalid()
    {
        ParsedCommand result = ShellArgumentParser.Parse([]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // --- Invalid: missing file arguments ---

    [Fact]
    public void ExtractHere_NoFiles_ReturnsInvalid()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--extract-here"]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ExtractFolder_NoFiles_ReturnsInvalid()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--extract-folder"]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Archive_NoFiles_ReturnsInvalid()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--archive"]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void OpenUi_NoSubCommand_ReturnsInvalid()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--open-ui"]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void OpenUiExtract_NoFiles_ReturnsInvalid()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--open-ui", "--extract"]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void OpenUiArchive_NoFiles_ReturnsInvalid()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--open-ui", "--archive"]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // --- Invalid: unknown commands ---

    [Fact]
    public void UnknownTopLevelCommand_ReturnsInvalid()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--unknown", "file.zip"]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void OpenUi_UnknownSubCommand_ReturnsInvalid()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--open-ui", "--unknown", "file.zip"]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // --- Adversarial inputs: path content ---

    [Fact]
    public void Archive_PathWithSpaces_ParsedCorrectly()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--archive", @"C:\My Documents\Annual Report.pdf"]);

        result.Type.Should().Be(CommandType.Archive);
        result.Files.Should().Equal(@"C:\My Documents\Annual Report.pdf");
    }

    [Fact]
    public void Archive_PathWithCyrillicChars_ParsedCorrectly()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--archive", @"C:\Документи\звіт_2024.docx"]);

        result.Type.Should().Be(CommandType.Archive);
        result.Files.Should().Equal(@"C:\Документи\звіт_2024.docx");
    }

    [Fact]
    public void Archive_PathWithSpecialChars_ParsedCorrectly()
    {
        ParsedCommand result = ShellArgumentParser.Parse(["--archive", @"C:\Work (2024)\[DRAFT] report.v1.pdf"]);

        result.Type.Should().Be(CommandType.Archive);
        result.Files.Should().Equal(@"C:\Work (2024)\[DRAFT] report.v1.pdf");
    }

    [Fact]
    public void ExtractHere_PathExceeding260Chars_ParsedCorrectlyNoTruncation()
    {
        string longPath = @"C:\" + new string('a', 128) + @"\" + new string('b', 128) + @"\archive.zip";
        longPath.Length.Should().BeGreaterThan(260);

        ParsedCommand result = ShellArgumentParser.Parse(["--extract-here", longPath]);

        result.Type.Should().Be(CommandType.ExtractHere);
        result.Files.Should().HaveCount(1);
        result.Files[0].Should().Be(longPath, "the full path must not be truncated");
        result.Files[0].Length.Should().BeGreaterThan(260);
    }

    [Fact]
    public void Archive_EmptyStringFileArg_AcceptedByParser()
    {
        // The parser dispatches commands but does not validate file path content.
        // An empty string is passed through; content validation is the caller's responsibility.
        ParsedCommand result = ShellArgumentParser.Parse(["--archive", ""]);

        result.Should().NotBeNull();
        result.Type.Should().Be(CommandType.Archive);
        result.Files.Should().HaveCount(1).And.Contain("");
    }

    [Fact]
    public void WhitespaceOnlyCommand_ReturnsInvalid()
    {
        // A whitespace-only first argument does not match any known command → Invalid.
        ParsedCommand result = ShellArgumentParser.Parse(["   ", "file.zip"]);

        result.Type.Should().Be(CommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
