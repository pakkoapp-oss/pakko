using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// T-F410: -stx through the real exe, against what 7za 26.02 did with the same lines
/// (docs/DECISIONS.md, "T-F410, wave B").
/// </summary>
public sealed class CliArchiveTypeExclusionSubprocessTests
{
    [Fact]
    public void Test_ExcludedType_CannotOpenTheArchive()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", CliFixtureFiles.ValidZip, "-stxzip");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("Cannot open the file as archive");
    }

    [Fact]
    public void Test_ExcludedTypeByItsBytes_NotItsName()
    {
        string renamed = Path.Combine(CliFixtureFiles.CreateScratchDir(), "a.dat");
        File.Copy(CliFixtureFiles.ValidZip, renamed);

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", renamed, "-stxZIP");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("Cannot open the file as archive");
    }

    [Fact]
    public void Test_AnotherTypeExcluded_TestsAsBefore()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", CliFixtureFiles.ValidZip, "-stx7z");

        exitCode.Should().Be(0, stdErr);
    }

    [Fact]
    public void Test_TarGz_OnlyTheOuterFormatCounts()
    {
        if (CliFixtureFiles.ValidTarGz is not { } tarGz)
            return; // no tar.exe to make the fixture

        CliProcessRunner.Run("t", tarGz, "-stxtar").ExitCode.Should().NotBe(2, "-stxtar leaves the gzip stream alone");
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", tarGz, "-stxgzip");
        exitCode.Should().Be(2);
        stdErr.Should().Contain("Cannot open the file as archive");
    }

    [Fact]
    public void Test_UnknownType_IsUnsupportedExit2()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", CliFixtureFiles.ValidZip, "-stxgz");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("Unsupported archive type");
    }

    [Fact]
    public void Test_StxWithoutAType_IsACommandLineError()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", CliFixtureFiles.ValidZip, "-stx");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("Too short switch");
    }

    [Fact]
    public void Test_Stdin_ExcludedTypeIsRefused()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.RunWithBinaryStdio(File.ReadAllBytes(CliFixtureFiles.ValidZip), "t", "-si", "-stxzip");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("Cannot open the file as archive");
    }

    [Fact]
    public void Extract_OneOfTwoExcluded_ExtractsTheOtherAndExits2()
    {
        if (CliFixtureFiles.ValidTarGz is not { } tarGz)
            return;
        string folder = CliFixtureFiles.CreateScratchDir();
        File.Copy(CliFixtureFiles.ValidZip, Path.Combine(folder, "a.zip"));
        File.Copy(tarGz, Path.Combine(folder, "b.tar.gz"));
        string dest = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", "-y", $"-o{dest}", Path.Combine(folder, "*"), "-stxgzip");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("b.tar.gz").And.Contain("Cannot open the file as archive");
        File.Exists(Path.Combine(dest, "a.txt")).Should().BeTrue("a.zip was extracted: " + stdErr);
    }

    [Fact]
    public void List_ExcludedType_IsExit2()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("l", CliFixtureFiles.ValidZip, "-stxzip");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("Cannot open the file as archive");
    }
}
