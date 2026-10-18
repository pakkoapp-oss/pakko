using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team: every switch CLI.md lists as not done ("Can add", "Declined"), repeats,
/// hostile values, and the read-only commands. A refused command line changes nothing on disk.
/// </summary>
public sealed class SwitchAdversarialTests : IDisposable
{
    private readonly AdversarialScratch _scratch = new();

    public SwitchAdversarialTests()
    {
        _scratch.WriteFile(@"src\a.txt", "A");
        _scratch.WriteFile(@"src\b.txt", "B");
        _scratch.Zip("arc.zip", ("a.txt", "FROM-ARCHIVE"));
        _scratch.WriteFile(@"out\a.txt", "PRECIOUS");
    }

    public void Dispose() => _scratch.Dispose();

    private string[] Snapshot() =>
        [.. Directory.EnumerateFileSystemEntries(_scratch.Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_scratch.Root, path) + "=" + (File.Exists(path) ? Convert.ToHexString(File.ReadAllBytes(path)) : "dir"))
            .Order(StringComparer.Ordinal)];

    // -sdel deletes the sources in 7-Zip; refused here, the sources must all still be there.
    [Theory]
    [InlineData("a", "-sdel")]
    [InlineData("a", "-spe")]
    [InlineData("a", "-sse")]
    [InlineData("a", "-ssw")]
    [InlineData("a", "-stl")]
    [InlineData("a", "-wtmp")]
    [InlineData("a", "-w")]
    [InlineData("a", "-v1m")]
    [InlineData("a", "-mmt4")]
    [InlineData("a", "-mhe=on")]
    [InlineData("a", "-spf")]
    [InlineData("a", "-sni")]
    [InlineData("a", "-aoa")]
    [InlineData("a", "-u-")]
    [InlineData("a", "-slt")]
    [InlineData("x", "-sdel")]
    [InlineData("x", "-aot")]
    [InlineData("x", "-spe")]
    [InlineData("x", "-spf")]
    [InlineData("x", "-snl")]
    [InlineData("x", "-snh")]
    [InlineData("x", "-wtmp")]
    [InlineData("x", "-stl")]
    [InlineData("x", "-scrcSHA256")]
    [InlineData("x", "-snz2")]
    [InlineData("x", "-ao")]
    [InlineData("x", "-aoz")]
    [InlineData("x", "-AOA")]
    [InlineData("x", "-o")]
    public void UnsupportedSwitch_IsExitSevenAndChangesNothing(string command, string unsupported)
    {
        string[] before = Snapshot();
        string[] args = command == "a"
            ? ["a", "o.zip", "src", unsupported]
            : ["x", "arc.zip", "-oout", "-aoa", unsupported];
        if (command == "x" && (unsupported.StartsWith("-ao", StringComparison.OrdinalIgnoreCase) || unsupported == "-o"))
            args = ["x", "arc.zip", unsupported];

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        exitCode.Should().Be(7, stdErr);
        Snapshot().Should().Equal(before, "a refused command line touches nothing");
    }

    // -sdel in the combinations where a 7-Zip that honoured it would delete the most.
    [Theory]
    [InlineData("a", "-sdel", "-so", "o.zip", "src")]
    [InlineData("a", "-sdel", "-y", "o.zip", @"src\*.txt", "-x!b.txt")]
    [InlineData("a", "-sdel", @"src\o.zip", "src")]
    [InlineData("a", "-sdel", "-rr", "o.zip", "src")]
    [InlineData("a", "-sdel", "-ttar", "o.tar", "src")]
    [InlineData("a", "-y", "o.zip", "src", "--", "-sdel")]
    [InlineData("-sdel", "a", "o.zip", "src")]
    [InlineData("a", "-SDEL", "o.zip", "src")]
    public void SdelInAnyCombination_DeletesNoSource(params string[] args)
    {
        string[] before = Snapshot().Where(e => e.StartsWith("src", StringComparison.Ordinal)).ToArray();

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        Snapshot().Where(e => e.StartsWith("src", StringComparison.Ordinal)).Should().Equal(before, $"exit {exitCode}: {stdErr}");
        if (!args.Contains("--"))
            exitCode.Should().Be(7, stdErr);
    }

    // 7-Zip's commands Pakko declines; d and rn change an archive in place there.
    [Theory]
    [InlineData("e")]
    [InlineData("d")]
    [InlineData("u")]
    [InlineData("rn")]
    [InlineData("b")]
    [InlineData("E")]
    public void DeclinedCommand_IsExitSevenAndChangesNothing(string command)
    {
        string[] before = Snapshot();

        (int exitCode, _, string stdErr) = _scratch.Pakko(command, "arc.zip", "-oout", "-aoa", "a.txt", "b.txt");

        exitCode.Should().Be(7, stdErr);
        stdErr.Should().Contain("not supported by Pakko");
        Snapshot().Should().Equal(before);
    }

    // stdout carries the data under -so: nothing else may be mixed into it.
    [Fact]
    public void ExtractToStdout_WithLogLevel_StdoutHoldsOnlyTheEntryBytes()
    {
        (int exitCode, string stdOut, string stdErr) = _scratch.Pakko("x", "arc.zip", "a.txt", "-so", "-bb1");

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().Be("FROM-ARCHIVE");
    }

    [Theory]
    [InlineData("x", "arc.zip", "-so", "-bso1")]
    [InlineData("x", "arc.zip", "-so", "-bse1")]
    [InlineData("a", "-so", "-bso1", "o.zip", "src")]
    public void MessagesOntoTheDataStream_IsExitSeven(params string[] args)
    {
        (int exitCode, string stdOut, _) = _scratch.Pakko(args);

        exitCode.Should().Be(7);
        stdOut.Should().BeEmpty();
    }

    [Fact]
    public void MissingListFile_IsExitSevenAndChangesNothing()
    {
        string[] before = Snapshot();

        (int exitCode, _, _) = _scratch.Pakko("x", "arc.zip", "-oout", "-aoa", "@missing.txt");

        exitCode.Should().Be(7);
        Snapshot().Should().Equal(before);
    }

    [Theory]
    [InlineData(@"..\a.txt")]
    [InlineData(@"C:\a.txt")]
    public void TraversalOrAbsoluteFilter_WithAoa_MatchesNothingAndOverwritesNothing(string filter)
    {
        (int exitCode, string stdOut, _) = _scratch.Pakko("x", "arc.zip", "-oout", "-aoa", filter);

        exitCode.Should().Be(0);
        stdOut.Should().Contain("No files to process");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("PRECIOUS");
    }

    [Fact]
    public void ListFileOnA_WithAMissingLine_ArchivesTheRestAndExitsOne()
    {
        _scratch.WriteFile("list.txt", "src\\a.txt\r\nnot-there.txt\r\n");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "@list.txt");

        exitCode.Should().Be(1, stdErr);
        File.Exists(_scratch.Inside("o.zip")).Should().BeTrue();
    }

    // CLI.md's three-way rule: a real 7z switch Pakko does not implement is "not supported by
    // Pakko: <reason>", never the typo message "Incorrect command line: unknown switch". -sdel and
    // the others are 7-Zip's own (kSwitchForms, listed in CLI.md's matrix as "Can add").
    [Theory]
    [InlineData("-sdel")]
    [InlineData("-spe")]
    [InlineData("-sse")]
    [InlineData("-ssw")]
    [InlineData("-stl")]
    [InlineData("-wtmp")]
    [InlineData("-mmt4")]
    public void Real7ZipSwitchOnA_IsNotReportedAsATypo(string realSwitch)
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "src", realSwitch);

        exitCode.Should().Be(7);
        stdErr.Should().NotContain("Incorrect command line: unknown switch", "rule 1 is for typos; this is a real 7z switch (rule 2/3)");
    }

    // Rule 3: name the switch. `a ... -aoa` says only "not supported on this command".
    [Theory]
    [InlineData("-aoa")]
    [InlineData("-si")]
    public void SupportedSwitchOnTheWrongCommand_NamesTheSwitch(string wrongHere)
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "src", wrongHere);

        exitCode.Should().Be(7);
        stdErr.Should().Contain(wrongHere, "CLI.md rule 3: name the specific switch in the error");
    }

    [Theory]
    [InlineData("-y", "-y")]
    [InlineData("-oout", "-oout2")]
    [InlineData("-aoa", "-aos")]
    [InlineData("-psecret", "-psecret")]
    public void RepeatedSwitch_IsExitSevenAndChangesNothing(string first, string second)
    {
        string[] before = Snapshot();

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", first, second);

        exitCode.Should().Be(7);
        stdErr.Should().NotContain("secret");
        Snapshot().Should().Equal(before);
    }

    [Fact]
    public void VeryLongSwitchValue_IsRefusedCleanly()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-o" + new string('x', 30000)); // under CreateProcess's 32767

        exitCode.Should().BeOneOf(2, 7);
        stdErr.Should().NotContain("Unexpected error");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("PRECIOUS");
    }

    [Theory]
    [InlineData("-o\u0007ctl")]
    [InlineData("-oa|b")]
    [InlineData("-oa<b")]
    [InlineData("-oa\"b")]
    public void InvalidCharacterInOutput_IsAnErrorNotACrash(string output)
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", output);

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().NotContain("Unexpected error");
    }

    // -o with no folder after a space: 7-Zip and pakko read the folder as the archive name.
    [Fact]
    public void OutputWithASpace_IsExitSeven()
    {
        (int exitCode, _, _) = _scratch.Pakko("x", "arc.zip", "-o", "out");

        exitCode.Should().Be(7);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("PRECIOUS");
    }

    // PowerShell's "-name.rest" split (CLI.md): -oout.d arriving as -oout .d is refused.
    [Fact]
    public void ShellSplitOutputSwitch_IsRefused()
    {
        (int exitCode, _, _) = _scratch.Pakko("x", "arc.zip", "-oout", ".d", "-aoa");

        exitCode.Should().Be(7);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("PRECIOUS");
    }

    // A name after "--" that looks like a switch is a name: "-aoa" there must not overwrite.
    [Fact]
    public void SwitchShapedNameAfterDoubleDash_IsNotTakenAsASwitch()
    {
        (int exitCode, _, _) = _scratch.Pakko("x", "-oout", "--", "arc.zip", "-aoa");

        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("PRECIOUS", "after --, '-aoa' is a name filter, not -aoa");
        exitCode.Should().Be(0, "a filter that passes nothing is 'No files to process', exit 0");
    }

    [Fact]
    public void BarePasswordWordAsCommand_IsNotPrinted()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("-p", "hunter2", "x", "arc.zip");

        exitCode.Should().Be(7);
        stdErr.Should().NotContain("hunter2");
    }

    // --- the read-only commands write nothing ---

    [Theory]
    [InlineData("t", "arc.zip")]
    [InlineData("t", "-y", "arc.zip")]
    [InlineData("l", "arc.zip")]
    [InlineData("l", "-y", "-p-", "arc.zip")]
    [InlineData("h", "arc.zip")]
    [InlineData("h", "src")]
    [InlineData("i")]
    public void ReadOnlyCommand_WritesNothing(params string[] args)
    {
        string[] before = Snapshot();

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        exitCode.Should().Be(0, stdErr);
        Snapshot().Should().Equal(before);
    }

    [Theory]
    [InlineData("t")]
    [InlineData("l")]
    [InlineData("x")]
    public void NotAnArchive_IsExitTwo(string command)
    {
        _scratch.WriteFile("fake.zip", "this is plain text, not a zip");

        string[] args = command == "x" ? [command, "fake.zip", "-oout2"] : [command, "fake.zip"];
        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        exitCode.Should().Be(2, stdErr);
        Directory.Exists(_scratch.Inside("out2")).Should().BeFalse("nothing was extracted, so no -o folder is made");
    }

    [Fact]
    public void TruncatedZip_ExtractWithAoa_KeepsTheExistingFile()
    {
        byte[] whole = File.ReadAllBytes(_scratch.Inside("arc.zip"));
        File.WriteAllBytes(_scratch.Inside("cut.zip"), whole[..(whole.Length / 2)]);

        (int exitCode, _, _) = _scratch.Pakko("x", "cut.zip", "-oout", "-aoa");

        exitCode.Should().Be(2);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("PRECIOUS");
    }

    [Fact]
    public void CorruptEntryData_ExtractWithAoa_KeepsTheExistingFile()
    {
        string archive = _scratch.Zip("bad.zip", ("a.txt", new string('Z', 4000)));
        byte[] bytes = File.ReadAllBytes(archive);
        bytes[60] ^= 0xFF; // inside the first entry's data, after its 30-byte header and name
        File.WriteAllBytes(archive, bytes);

        (int exitCode, _, _) = _scratch.Pakko("x", "bad.zip", "-oout", "-aoa");

        exitCode.Should().Be(2);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("PRECIOUS", "a CRC failure must not replace the good file with bad bytes");
    }

    [Fact]
    public void WrongPassword_WithAoa_KeepsTheExistingFileAndWritesNothing()
    {
        string encrypted = Path.Combine(AppContext.BaseDirectory, "Fixtures", "encrypted_aes256.zip");
        File.Copy(encrypted, _scratch.Inside("enc.zip"));
        string[] before = Snapshot();

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "enc.zip", "-oout", "-aoa", "-pwrong");

        exitCode.Should().Be(2);
        stdErr.Should().NotContain("wrong'").And.NotContain("-pwrong");
        Snapshot().Should().Equal(before);
    }
}
