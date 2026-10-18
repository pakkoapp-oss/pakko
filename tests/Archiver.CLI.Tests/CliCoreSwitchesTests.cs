using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F411, wave E: 7-Zip's switches that Core carries out (-w, -ssp, -mmt, -stl, -ssw, -sse, -spe,
// -aot, -sdel), as CliArgumentParser.Parse returns them. Measured against 7za 26.02 (docs/DECISIONS.md,
// "T-F411, wave E").
public sealed class CliCoreSwitchesTests
{
    // --- -w: 7-Zip's working folder (ArchiveCommandLine.cpp: an empty one is the system's temp) ---

    [Theory]
    [InlineData(@"-wC:\work", @"C:\work")]
    [InlineData("-w", "")]
    [InlineData(@"-Wd:\Mixed Case", @"d:\Mixed Case")]
    public void Archive_WorkingFolder_IsTaken(string token, string folder)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.WorkingFolder.Should().Be(folder);
    }

    [Fact]
    public void Archive_NoWorkingFolder_IsNull()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "out.zip", "f.txt"]);

        result.WorkingFolder.Should().BeNull();
    }

    [Fact]
    public void Archive_WorkingFolderTwice_IsAMultipleInstanceError()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-wa", "-wb", "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("Multiple instances for switch");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    [InlineData("l")]
    public void WorkingFolder_OnACommandThatReadsAnArchive_IsNotOnThisCommand(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-wC:\\work", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported on this command");
    }

    // --- -mmt: the ZIP writer's threads (7za 26.02: -mmt0 and -mmt= are taken, -mmtON and -mmt=-1 are not) ---

    [Theory]
    [InlineData("-mmt", 0)]
    [InlineData("-mmt=", 0)]
    [InlineData("-mmt=on", 0)]
    [InlineData("-mmt=ON", 0)]
    [InlineData("-mmt0", 0)]
    [InlineData("-mmt=off", 1)]
    [InlineData("-mmt=OFF", 1)]
    [InlineData("-mmt1", 1)]
    [InlineData("-mmt=4", 4)]
    [InlineData("-MMT=2", 2)]
    [InlineData("-mmt99", 99)]
    public void Archive_ThreadCount_IsTaken(string token, int threads)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.MaxThreads.Should().Be(threads);
    }

    [Theory]
    [InlineData("-mmtON")]
    [InlineData("-mmton")]
    [InlineData("-mmt=-1")]
    [InlineData("-mmt=abc")]
    [InlineData("-mmt99999999999")]
    [InlineData("-mmt=4x")]
    public void Archive_ThreadCountThatIsNotACount_IsRefused(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-mmt");
    }

    [Fact]
    public void Archive_ThreadCountWithTheLevel_BothTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-mx9", "-mmt=2", "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.MaxThreads.Should().Be(2);
        result.CompressionLevel.Should().Be(System.IO.Compression.CompressionLevel.SmallestSize);
    }

    [Fact]
    public void Archive_NoThreadCount_IsZero()
    {
        CliArgumentParser.Parse(["a", "out.zip", "f.txt"]).MaxThreads.Should().Be(0);
    }

    // --- -stl: the archive takes its newest entry's time (7-Zip's SWFRM_SIMPLE: no postfix) ---

    [Fact]
    public void Archive_ArchiveTime_IsTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-STL", "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.SetArchiveTime.Should().BeTrue();
        CliArgumentParser.Parse(["a", "out.zip", "f.txt"]).SetArchiveTime.Should().BeFalse();
    }

    [Theory]
    [InlineData("a", "-stl-", "-stl takes no value")]
    [InlineData("a", "-stlx", "-stl takes no value")]
    [InlineData("x", "-stl", "not supported on this command")]
    [InlineData("t", "-stl", "not supported on this command")]
    public void ArchiveTime_WithAPostfixOrOnAnotherCommand_IsRefused(string command, string token, string reason)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, token, "a.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(reason);
    }

    // --- -ssw: read files open for writing (SWFRM_SIMPLE) ---

    [Fact]
    public void Archive_ReadFilesOpenForWriting_IsTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-SSW", "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.ReadFilesOpenForWriting.Should().BeTrue();
        CliArgumentParser.Parse(["a", "out.zip", "f.txt"]).ReadFilesOpenForWriting.Should().BeFalse();
    }

    [Theory]
    [InlineData("a", "-ssw-", "-ssw takes no value")]
    [InlineData("x", "-ssw", "not supported on this command")]
    public void ReadFilesOpenForWriting_WithAPostfixOrOnAnotherCommand_IsRefused(string command, string token, string reason)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, token, "a.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(reason);
    }

    // --- -sse: stop at a file that cannot be read (SWFRM_SIMPLE) ---

    [Fact]
    public void Archive_StopOnUnreadableSource_IsTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-SSE", "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.StopOnUnreadableSource.Should().BeTrue();
        CliArgumentParser.Parse(["a", "out.zip", "f.txt"]).StopOnUnreadableSource.Should().BeFalse();
    }

    [Theory]
    [InlineData("a", "-sse1", "-sse takes no value")]
    [InlineData("x", "-sse", "not supported on this command")]
    public void StopOnUnreadableSource_WithAPostfixOrOnAnotherCommand_IsRefused(string command, string token, string reason)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, token, "a.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(reason);
    }

    // --- -spe: drop the root folder named like -o's last part (SWFRM_MINUS) ---

    [Theory]
    [InlineData("-spe", true)]
    [InlineData("-SPE", true)]
    [InlineData("-spe-", false)]
    public void Extract_DropRootNamedLikeOutput_IsTaken(string token, bool expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", token, "a.zip", @"-oC:\out\src"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.DropRootNamedLikeOutput.Should().Be(expected);
    }

    [Theory]
    [InlineData("x", "-spex", "-spe takes only '-'")]
    [InlineData("a", "-spe", "not supported on this command")]
    [InlineData("t", "-spe", "not supported on this command")]
    public void DropRootNamedLikeOutput_WithAPostfixOrOnAnotherCommand_IsRefused(string command, string token, string reason)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, token, "a.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(reason);
    }

    // --- -sdel: delete the sources once archived (SWFRM_SIMPLE) ---

    [Fact]
    public void Archive_DeleteSourcesAfter_IsTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-SDEL", "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.DeleteSourcesAfter.Should().BeTrue();
        CliArgumentParser.Parse(["a", "out.zip", "f.txt"]).DeleteSourcesAfter.Should().BeFalse();
    }

    // 7za takes -sdel on x and does nothing; Pakko says so instead (the three-way rule).
    [Theory]
    [InlineData("a", "-sdel-", "-sdel takes no value")]
    [InlineData("x", "-sdel", "not supported on this command")]
    [InlineData("t", "-sdel", "not supported on this command")]
    public void DeleteSourcesAfter_WithAPostfixOrOnAnotherCommand_IsRefused(string command, string token, string reason)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, token, "a.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(reason);
    }

    // --- -ssp: declined (Pakko cannot keep the sources' last access time through tar.exe) ---

    [Theory]
    [InlineData("a", "out.zip")]
    [InlineData("x", "a.zip")]
    public void PreserveAccessTime_IsDeclined(string command, string archive)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-ssp", archive, "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported by Pakko: -ssp");
    }
}
