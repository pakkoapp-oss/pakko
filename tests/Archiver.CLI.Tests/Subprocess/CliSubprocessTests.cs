using System.IO.Compression;
using System.Security.Cryptography;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// Real Process.Start of the built pakko.exe against real archive fixtures, asserting real
/// exit codes and real stdout/stderr text — genuinely new to this repo (TASKS.md's T-F09):
/// Archiver.Shell's args are only ever generated programmatically by the shell extension, so
/// unit-testing its parser class in isolation is sufficient there. A human or script types
/// Archiver.CLI's arguments directly, so its exit code and stdout/stderr text ARE the public
/// contract, not an implementation detail — a parser-only suite would never catch a real process
/// returning the wrong exit code or malformed output.
/// </summary>
public sealed class CliSubprocessTests
{
    // --- x: happy path ---

    [Fact]
    public void Extract_ZipHappyPath_ExtractsFilesAndExitsZero()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        // T-F156: two root-level files with no common containing folder no longer get wrapped in a
        // subfolder named after the archive under SingleFolder mode (`pakko.exe x` always uses it) —
        // reversed per a direct user decision; see DECISIONS.md's T-F156 entry.
        string extractedFile = Path.Combine(destDir, "a.txt");
        File.Exists(extractedFile).Should().BeTrue();
        File.ReadAllText(extractedFile).Should().Be("hello world");
    }

    // T-F360: -snz0 leaves the archive's download mark off; without it the mark is applied.
    // Assumes no EnforceMOTW Group Policy on the test machine.
    [Theory]
    [InlineData(null, true)]
    [InlineData("-snz", true)]
    [InlineData("-snz0", false)]
    public void Extract_MarkedZip_SnzDecidesTheMark(string? snz, bool expectMarked)
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string zipPath = Path.Combine(scratchDir, "marked.zip");
        File.Copy(CliFixtureFiles.ValidZip, zipPath);
        File.WriteAllText(zipPath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        string destDir = CliFixtureFiles.CreateScratchDir();
        string[] args = snz is null ? ["x", $"-o{destDir}", zipPath] : ["x", snz, $"-o{destDir}", zipPath];
        (int exitCode, _, string stdErr) = CliProcessRunner.Run(args);

        exitCode.Should().Be(0, stdErr);
        File.Exists(Path.Combine(destDir, "a.txt") + ":Zone.Identifier").Should().Be(expectMarked);
    }

    // T-F154: pakko.exe's own "x" command always uses ExtractMode.SingleFolder (see
    // Archiver.CLI/Program.cs's BuildExtractOptions) — never SeparateFolders, the mode the T-F154
    // bug actually lived in (App's default Extract button, Explorer's "Extract Here"). Confirms
    // that directly, rather than leaving it as an inference from Extract_SevenZipFixture's own
    // single-file fixture a few tests down.
    [Fact]
    public void Extract_SingleFileZip_ExtractsDirectlyWithoutWrapperFolder()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string zipPath = Path.Combine(scratchDir, "photo.zip");
        string sourceFile = Path.Combine(scratchDir, "photo.png");
        File.WriteAllText(sourceFile, "not a real png, just single-file content");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            archive.CreateEntryFromFile(sourceFile, "photo.png");

        string destDir = CliFixtureFiles.CreateScratchDir();
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", zipPath);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        File.Exists(Path.Combine(destDir, "photo.png")).Should().BeTrue("a single-file archive should not get a wrapper subfolder");
        Directory.Exists(Path.Combine(destDir, "photo")).Should().BeFalse();
    }

    // T-F206: like `7z x`, no -o means the current directory — not the archive's own folder.
    [Fact]
    public void Extract_NoOutputSwitch_ExtractsIntoCurrentDirectoryNotBesideArchive()
    {
        string archiveDir = CliFixtureFiles.CreateScratchDir();
        string zipPath = Path.Combine(archiveDir, "valid.zip");
        File.Copy(CliFixtureFiles.ValidZip, zipPath);
        string workingDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(workingDir, "x", zipPath);

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(Path.Combine(workingDir, "a.txt")).Should().Be("hello world");
        File.Exists(Path.Combine(workingDir, "b.txt")).Should().BeTrue();
        Directory.GetFileSystemEntries(archiveDir).Should().ContainSingle("nothing may land beside the archive");
    }

    // T-F179 (test-coverage audit): precursor to T-F160 (interactive conflict dialog for
    // `pakko x`, still an open design question). Locks down today's real, observed behavior —
    // not a guessed one — as a baseline before that design work happens. Confirmed by reading
    // Archiver.CLI/Program.cs directly: `pakko x` never passes ConflictBehavior.Ask at all (the
    // advisor's own caution before this was written) — OnConflict is
    // `command.OverwriteMode ?? (command.AssumeYes ? Overwrite : Skip)`, so with neither `-ao`
    // nor `-y` the real default is a deliberate Skip, not a fallthrough to ConflictResolver's
    // null-callback default (that path is irrelevant here — Skip is chosen at the CLI argument-
    // mapping layer itself, before ConflictResolver is ever consulted).
    [Fact]
    public void Extract_OverlappingFileTwiceNoOverwriteSwitch_TodaysBehaviorIsSkipNotOverwriteNotThrow()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string preExistingFile = Path.Combine(destDir, "a.txt");
        File.WriteAllText(preExistingFile, "pre-existing content that must survive a Skip");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", CliFixtureFiles.ValidZip);

        // T-F313: the kept file is named, the way forward is offered, and the exit code says
        // not everything was extracted - what a tar archive already did. It was exit 0 and silence.
        exitCode.Should().Be(1, because: stdErr);
        stdErr.Should().Contain("pakko: skipped: a.txt: File already exists at destination.").And.Contain("-aoa");
        File.ReadAllText(preExistingFile).Should().Be("pre-existing content that must survive a Skip",
            "today's real, observed default (no -ao/-y switch) is Skip — the archive's own " +
            "\"hello world\" content for a.txt must NOT overwrite what was already there");
        File.Exists(Path.Combine(destDir, "b.txt")).Should().BeTrue();
    }

    // T-F313: -aos is the user's own choice to keep existing files - named, but no hint.
    [Fact]
    public void Extract_OverlappingFileWithSkipSwitch_NamesTheKeptFileWithoutAHint()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "kept");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", "-aos", $"-o{destDir}", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(1, because: stdErr);
        stdErr.Should().Contain("pakko: skipped: a.txt: File already exists at destination.").And.NotContain("hint");
    }

    [Fact]
    public void Extract_OverlappingFileWithOverwriteSwitch_ExitsZeroAndSaysNothing()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "old");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", "-aoa", $"-o{destDir}", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0, because: stdErr);
        stdErr.Should().BeEmpty();
    }

    [RequiresTarExe]
    public void Extract_TarGzHappyPath_ExtractsFilesAndExitsZero()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", CliFixtureFiles.ValidTarGz!);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        // T-F156: `pakko.exe x` always uses SingleFolder mode, and two root-level files with no
        // common containing folder no longer get wrapped in a subfolder named after the archive
        // (T-F118 used to do this) — reversed per a direct user decision; see DECISIONS.md's
        // T-F156 entry. This is exactly the "dumb mode should extract flat" case the user reported.
        File.Exists(Path.Combine(destDir, "a.txt")).Should().BeTrue();
    }

    // T-F234: 7za's own default for Cyrillic names — cp866 bytes, UTF-8 flag clear, plus a 0x7075
    // Unicode Path extra — so the expected names hold on any machine's code pages. Used to be
    // written as mojibake, and the two one-letter names collapsed into one file.
    [Fact]
    public void Extract_LegacyOemNamedZip_WritesRealNames()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string zipPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy_oem866_7za.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", zipPath);

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(Path.Combine(destDir, "А.txt")).Should().Be("A");
        File.ReadAllText(Path.Combine(destDir, "Б.txt")).Should().Be("B");
        File.ReadAllText(Path.Combine(destDir, "Тека", "Документ_квартал.txt")).Should().Be("D");
    }

    [RequiresTarCapability("7z")]
    public void Extract_SevenZipFixture_ExtractsFileWithContentAndExitsZero()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string sevenZipPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.7z");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", sevenZipPath);

        exitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(destDir, "seven.txt")).Should().Be("hello from a real 7z fixture\n");
    }

    [RequiresTarCapability("rar")]
    public void Extract_RarFixture_ExtractsFileWithContentAndExitsZero()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string rarPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.rar");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", rarPath);

        exitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(destDir, "rar.txt")).Should().Be("hello from a real rar fixture\n");
    }

    // --- t: happy path + tar-family skip ---

    [Fact]
    public void Test_ZipHappyPath_ExitsZero()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
    }

    [RequiresTarExe]
    public void Test_TarGzArchive_ExitsOneAndNamesReason()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", CliFixtureFiles.ValidTarGz!);

        exitCode.Should().Be(1);
        stdErr.Should().Contain("tar-family archives have no test capability");
    }

    // --- T-F191: -p{pwd} ---

    private static string EncryptedZipPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "encrypted_aes256.zip");

    [Fact]
    public void Extract_EncryptedZipWithCorrectPassword_ExtractsRealContentAndExitsZero()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(
            "x", "-ptestpassword", $"-o{destDir}", EncryptedZipPath);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        string extracted = Path.Combine(destDir, "compressible.txt");
        File.Exists(extracted).Should().BeTrue();
        // Full-content hash match against the known plaintext fixture (MANIFEST.sha256's own
        // "compressible.txt" entry) rather than a text comparison — avoids embedding this
        // fixture's real non-ASCII content as a literal in source (see CLAUDE.md's documented
        // Edit-tool corruption risk for complex-script literals).
        byte[] extractedBytes = File.ReadAllBytes(extracted);
        Convert.ToHexString(SHA256.HashData(extractedBytes)).ToLowerInvariant()
            .Should().Be("f4493c3682d924e263f2573f7700fe8a417af61759cd700f56bba849e8ebce00");
    }

    [Fact]
    public void Test_EncryptedZipWithCorrectPassword_ExitsZero()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", "-ptestpassword", EncryptedZipPath);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
    }

    [Fact]
    public void Extract_EncryptedZipWithWrongPassword_FailsCleanlyWithNoPartialFiles()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(
            "x", "-pWrongPassword", $"-o{destDir}", EncryptedZipPath);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("incorrect password");
        Directory.GetFileSystemEntries(destDir).Should().BeEmpty("a rejected password must not leave any partial output");
    }

    [Fact]
    public void Extract_EncryptedZipNoPasswordSwitch_FailsWithSamePreT191MessageAsToday()
    {
        // Characterization test (docs/DECISIONS.md's T-F191 entry) — this exact behavior already
        // passes before this task's change, since CliProcessRunner.Run always redirects stdin
        // (never a real interactive console), so BuildPasswordResolver never wires a resolver here
        // regardless of -y. Pins the pre-existing message so a future change doesn't silently
        // alter it while adding real -p support alongside it.
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", EncryptedZipPath);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("password-protected and cannot be extracted");
        Directory.GetFileSystemEntries(destDir).Should().BeEmpty();
    }

    // --- T-F193: a -p{pwd} (AES-256 creation) ---

    private static (string ScratchDir, string SourceFile) CreateSourceFile()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string sourceFile = Path.Combine(scratchDir, "secret.txt");
        File.WriteAllText(sourceFile, "top secret content");
        return (scratchDir, sourceFile);
    }

    [Fact]
    public void Archive_WithPassword_CreatesEncryptedZipThatOnlyTheSamePasswordOpens()
    {
        (string scratchDir, string sourceFile) = CreateSourceFile();
        string zipPath = Path.Combine(scratchDir, "out.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-pSecret123", "-mem=AES256", zipPath, sourceFile);

        exitCode.Should().Be(0, because: stdErr);
        CliProcessRunner.Run("x", "-pWrong", $"-o{CliFixtureFiles.CreateScratchDir()}", zipPath).ExitCode
            .Should().Be(2, "the archive must really be encrypted, not stored in the clear");
        CliProcessRunner.Run("t", "-pSecret123", zipPath).ExitCode.Should().Be(0);

        string destDir = CliFixtureFiles.CreateScratchDir();
        CliProcessRunner.Run("x", "-pSecret123", $"-o{destDir}", zipPath).ExitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(destDir, "secret.txt")).Should().Be("top secret content");
    }

    // T-F275: -rr writes the PAR2 index and one volume next to the archive and prints nothing more
    // on success (pakko a is silent then). Assumes no DisableRecoveryData Group Policy here.
    [Theory]
    [InlineData("-tzip", "out.zip")]
    [InlineData("-ttar.gz", "out.tar.gz")]
    public void Archive_RecoveryData_WritesTheSetNextToTheArchive(string typeSwitch, string name)
    {
        (string scratchDir, string sourceFile) = CreateSourceFile();
        string archivePath = Path.Combine(scratchDir, name);

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("a", "-rr10", typeSwitch, archivePath, sourceFile);

        exitCode.Should().Be(0, because: stdErr);
        stdOut.Should().BeEmpty();
        File.Exists(archivePath + ".par2").Should().BeTrue();
        Directory.GetFiles(scratchDir, name + ".vol*+*.par2").Should().ContainSingle();
    }

    [Fact]
    public void Archive_RecoveryDataWithStdout_ExitsSevenAndCreatesNothing()
    {
        (string scratchDir, string sourceFile) = CreateSourceFile();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-rr", "-so", Path.Combine(scratchDir, "out.zip"), sourceFile);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("-so");
        Directory.GetFiles(scratchDir).Should().Equal(sourceFile);
    }

    // T-F275 step 3: `t` checks the archive against a set next to it. 20 KB of noise, stored, so
    // the archive's bytes are the source's and the damage lands where the test puts it.
    private static string ProtectedArchive(string name)
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string sourceFile = Path.Combine(scratchDir, "noise.bin");
        byte[] noise = new byte[20_000];
        new Random(275).NextBytes(noise);
        File.WriteAllBytes(sourceFile, noise);
        string archivePath = Path.Combine(scratchDir, name);
        string typeSwitch = name.EndsWith(".zip", StringComparison.Ordinal) ? "-tzip" : "-ttar.gz";
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-rr10", "-mx=0", typeSwitch, archivePath, sourceFile);
        exitCode.Should().Be(0, because: stdErr);
        return archivePath;
    }

    private static void Overwrite(string path, long offset, int count)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = offset;
        stream.Write(Enumerable.Repeat((byte)0x5A, count).ToArray());
    }

    [Theory]
    [InlineData("out.zip")]
    [InlineData("out.tar.gz")]
    public void Test_IntactSet_SaysSoOnStdoutAndExitsZero(string name)
    {
        string archivePath = ProtectedArchive(name);

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", archivePath);

        exitCode.Should().Be(0, because: stdErr);
        stdOut.Should().Contain($"{name}: recovery data intact (");
        stdErr.Should().BeEmpty();
    }

    [Fact]
    public void Test_Par2Path_ChecksTheArchiveItProtects()
    {
        string archivePath = ProtectedArchive("out.tar.gz");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", archivePath + ".par2");

        exitCode.Should().Be(0, because: stdErr);
        stdOut.Should().Contain("out.tar.gz: recovery data intact (");
    }

    [Theory]
    [InlineData("out.zip")]
    [InlineData("out.tar.gz")]
    public void Test_DamageTheSetCanRepair_ExitsTwoAndSaysItCanBeRepaired(string name)
    {
        string archivePath = ProtectedArchive(name);
        Overwrite(archivePath, new FileInfo(archivePath).Length / 2, 8);

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", archivePath);

        exitCode.Should().Be(2);
        stdErr.Should().Contain($"pakko: error: {name}: The archive is damaged (").And.Contain("its recovery data can repair it")
            .And.Contain("pakko: hint: 'pakko r <archive>'");
    }

    // --- r: repair from the set (T-F275 step 4) ---

    [Theory]
    [InlineData("out.zip", "out.repaired.zip")]
    [InlineData("out.tar.gz", "out.repaired.tar.gz")]
    public void Repair_DamagedArchive_WritesACopyThatIsTheArchiveAgain_AndLeavesTheOriginal(string name, string repairedName)
    {
        string archivePath = ProtectedArchive(name);
        string folder = Path.GetDirectoryName(archivePath)!;
        byte[] good = File.ReadAllBytes(archivePath);
        Overwrite(archivePath, good.Length / 2, 8);
        byte[] damaged = File.ReadAllBytes(archivePath);
        string[] before = Directory.GetFiles(folder);

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("r", archivePath);

        string repaired = Path.Combine(folder, repairedName);
        exitCode.Should().Be(0, because: stdErr);
        stdErr.Should().BeEmpty();
        stdOut.Should().Contain($"{name}: repaired (1 of ").And.Contain(repaired);
        File.ReadAllBytes(repaired).Should().Equal(good);
        File.ReadAllBytes(archivePath).Should().Equal(damaged);
        Directory.GetFiles(folder).Should().BeEquivalentTo([.. before, repaired]);
    }

    [Fact]
    public void Repair_Par2PathAndOutputDirectory_WritesTheCopyThere()
    {
        string archivePath = ProtectedArchive("out.tar.gz");
        string folder = Path.GetDirectoryName(archivePath)!;
        byte[] good = File.ReadAllBytes(archivePath);
        Overwrite(archivePath, good.Length / 2, 8);
        string[] before = Directory.GetFiles(folder);
        string outDir = Path.Combine(folder, "fixed");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("r", archivePath + ".par2", "-o" + outDir, "-y");

        exitCode.Should().Be(0, because: stdErr);
        File.ReadAllBytes(Path.Combine(outDir, "out.repaired.tar.gz")).Should().Equal(good);
        Directory.GetFiles(folder).Should().Equal(before);
    }

    [Fact]
    public void Repair_IntactArchive_SaysNothingToRepair_ExitsZero_WritesNothing()
    {
        string archivePath = ProtectedArchive("out.zip");
        string[] before = Directory.GetFiles(Path.GetDirectoryName(archivePath)!);

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("r", archivePath);

        exitCode.Should().Be(0, because: stdErr);
        stdErr.Should().BeEmpty();
        stdOut.Should().Contain("out.zip: nothing to repair, recovery data intact (");
        Directory.GetFiles(Path.GetDirectoryName(archivePath)!).Should().Equal(before);
    }

    [Theory]
    [InlineData("out.zip")]
    [InlineData("out.tar.gz")]
    public void Repair_DamageBeyondTheSet_ExitsTwo_WritesNothing(string name)
    {
        string archivePath = ProtectedArchive(name);
        Overwrite(archivePath, 1000, 8000);
        string[] before = Directory.GetFiles(Path.GetDirectoryName(archivePath)!);

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("r", archivePath);

        exitCode.Should().Be(2);
        stdOut.Should().BeEmpty();
        stdErr.Should().Contain("beyond what its recovery data can repair").And.NotContain("pakko: hint");
        Directory.GetFiles(Path.GetDirectoryName(archivePath)!).Should().Equal(before);
    }

    [Fact]
    public void Repair_ArchiveWithoutASet_ExitsTwo()
    {
        (string scratchDir, string sourceFile) = CreateSourceFile();
        string archivePath = Path.Combine(scratchDir, "out.zip");
        CliProcessRunner.Run("a", archivePath, sourceFile).ExitCode.Should().Be(0);

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("r", archivePath);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("pakko: error: out.zip: No recovery data was found next to the archive.");
        Directory.GetFiles(scratchDir, "*repaired*").Should().BeEmpty();
    }

    // The set files zeroed (a failed disk, a bad copy): for `t` a warning, for `r` the reason it did nothing.
    [Fact]
    public void Repair_SetFilesDamaged_ExitsTwo()
    {
        string archivePath = ProtectedArchive("out.tar.gz");
        Overwrite(archivePath, 1000, 8);
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(archivePath)!, "*.par2"))
            File.WriteAllBytes(file, new byte[new FileInfo(file).Length]);

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("r", archivePath);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("pakko: error: out.tar.gz: The recovery data is damaged or in a form Pakko cannot read.");
        Directory.GetFiles(Path.GetDirectoryName(archivePath)!, "*repaired*").Should().BeEmpty();
    }

    // Another tool rewrote the ZIP and left the old set: the ZIP's own test passes, so `r` builds
    // nothing from that set (it would be the old version) and says what `t` says.
    [Fact]
    public void Repair_ZipRewrittenBesideItsOldSet_IsAWarning_AndNothingIsBuilt()
    {
        string archivePath = ProtectedArchive("out.zip");
        string folder = Path.GetDirectoryName(archivePath)!;
        string keep = Directory.CreateDirectory(Path.Combine(folder, "keep")).FullName;
        foreach (string file in Directory.GetFiles(folder, "*.par2"))
            File.Copy(file, Path.Combine(keep, Path.GetFileName(file)));
        string other = Path.Combine(folder, "other.bin");
        byte[] noise = new byte[20_000];
        new Random(4).NextBytes(noise);
        File.WriteAllBytes(other, noise);
        CliProcessRunner.Run("a", "-y", "-mx=0", "-tzip", archivePath, other).ExitCode.Should().Be(0);
        foreach (string file in Directory.GetFiles(keep))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));

        (int tested, _, string testedErr) = CliProcessRunner.Run("t", archivePath);
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("r", archivePath);

        tested.Should().Be(1);
        exitCode.Should().Be(1);
        stdOut.Should().BeEmpty();
        stdErr.Should().Contain("pakko: warning: out.zip: The recovery data does not match the archive").And.Be(testedErr);
        Directory.GetFiles(folder, "*repaired*").Should().BeEmpty();
    }

    [Fact]
    public void Repair_RepairedNameTaken_UsesTheNextNumber()
    {
        string archivePath = ProtectedArchive("out.tar.gz");
        string folder = Path.GetDirectoryName(archivePath)!;
        Overwrite(archivePath, 1000, 8);
        string taken = Path.Combine(folder, "out.repaired.tar.gz");
        File.WriteAllText(taken, "keep");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("r", archivePath);

        exitCode.Should().Be(0, because: stdErr);
        stdOut.Should().Contain("out.repaired (1).tar.gz");
        File.ReadAllText(taken).Should().Be("keep");
    }

    // A folder where files cannot be created (a read-only share, another user's folder): the
    // error names the way out, and the way out works.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Repair_FolderNotWritable_ExitsTwoWithTheOutputHint_AndOutputDirectoryWorks()
    {
        string archivePath = ProtectedArchive("out.tar.gz");
        var folder = new DirectoryInfo(Path.GetDirectoryName(archivePath)!);
        string outDir = CliFixtureFiles.CreateScratchDir();
        byte[] good = File.ReadAllBytes(archivePath);
        Overwrite(archivePath, 1000, 8);
        string[] before = Directory.GetFiles(folder.FullName);
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!, System.Security.AccessControl.FileSystemRights.CreateFiles,
            System.Security.AccessControl.InheritanceFlags.None, System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Deny);
        System.Security.AccessControl.DirectorySecurity security = folder.GetAccessControl();
        security.AddAccessRule(deny);
        folder.SetAccessControl(security);
        try
        {
            (int exitCode, _, string stdErr) = CliProcessRunner.Run("r", archivePath);
            (int elsewhere, _, string elsewhereErr) = CliProcessRunner.Run("r", archivePath, "-o" + outDir);

            exitCode.Should().Be(2);
            stdErr.Should().Contain("pakko: error: out.tar.gz: The repaired copy could not be written:")
                .And.Contain("pakko: hint: -o<dir> writes the repaired copy to another folder");
            Directory.GetFiles(folder.FullName).Should().Equal(before);
            elsewhere.Should().Be(0, because: elsewhereErr);
            File.ReadAllBytes(Path.Combine(outDir, "out.repaired.tar.gz")).Should().Equal(good);
        }
        finally
        {
            security = folder.GetAccessControl();
            security.RemoveAccessRule(deny);
            folder.SetAccessControl(security);
        }
    }

    [Theory]
    [InlineData("r")]
    [InlineData("r", "out.zip", "-si")]
    [InlineData("r", "out.zip", "-nosuchswitch")]
    [InlineData("r", "out.zip", "-o")]
    public void Repair_BadCommandLine_ExitsSeven(params string[] args)
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(args);

        exitCode.Should().Be(7);
        stdOut.Should().BeEmpty();
        stdErr.Should().NotBeEmpty();
    }

    [Fact]
    public void Repair_PathsThatAreNotThere_ExitTwoEachWithItsReason()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("r", Path.Combine(scratchDir, "gone.zip"), Path.Combine(scratchDir, "gone.zip.par2"));

        exitCode.Should().Be(2);
        stdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith("pakko: error:", StringComparison.Ordinal)).Should().HaveCount(2, because: stdErr);
    }

    // The process dies while the copy is being built (Task Manager, a power cut). No file named
    // as a repaired copy may exist, the original is as it was, and the next run repairs and
    // sweeps what the dead one left.
    [Fact]
    public async Task Repair_KilledWhileBuildingTheCopy_LeavesNoCopy_AndTheNextRunRepairs()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string big = Path.Combine(scratchDir, "big.bin");
        byte[] noise = new byte[48 * 1024 * 1024];
        new Random(374).NextBytes(noise);
        File.WriteAllBytes(big, noise);
        string archivePath = Path.Combine(scratchDir, "out.zip");
        CliProcessRunner.Run("a", "-rr10", "-mx=0", "-tzip", archivePath, big).ExitCode.Should().Be(0);
        File.Delete(big);
        byte[] good = File.ReadAllBytes(archivePath);
        for (int i = 0; i < 150; i++)
            Overwrite(archivePath, 1000 + (long)i * (good.Length / 160), 8);
        byte[] damaged = File.ReadAllBytes(archivePath);
        var startInfo = new System.Diagnostics.ProcessStartInfo(CliProcessRunner.ExePath)
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in new[] { "r", archivePath })
            startInfo.ArgumentList.Add(arg);

        bool killedWhileBuilding;
        using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!)
        {
            process.StandardInput.Close();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!process.HasExited && clock.Elapsed < TimeSpan.FromSeconds(60)
                && !Directory.EnumerateFiles(scratchDir, ".pakko-a-*.tmp").Any())
            {
                await Task.Delay(2);
            }
            killedWhileBuilding = !process.HasExited;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        killedWhileBuilding.Should().BeTrue("the kill has to land before the copy is finished");
        Directory.GetFiles(scratchDir, "*repaired*").Should().BeEmpty();
        File.ReadAllBytes(archivePath).Should().Equal(damaged);
        (int tested, _, string testedErr) = CliProcessRunner.Run("t", archivePath);
        tested.Should().Be(2);
        testedErr.Should().Contain("its recovery data can repair it");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("r", archivePath);
        exitCode.Should().Be(0, because: stdErr);
        File.ReadAllBytes(Path.Combine(scratchDir, "out.repaired.zip")).Should().Equal(good);
        Directory.GetFiles(scratchDir, ".pakko-a-*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("out.zip")]
    [InlineData("out.tar.gz")]
    public void Test_DamageBeyondTheSet_ExitsTwoAndSaysItCannotBeRepaired(string name)
    {
        string archivePath = ProtectedArchive(name);
        Overwrite(archivePath, 1000, 8000);

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", archivePath);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("beyond what its recovery data can repair");
    }

    [Theory]
    [InlineData("out.zip", 0)]
    [InlineData("out.tar.gz", 1)]
    public void Test_SetFilesDamaged_IsAWarningAndTheArchiveIsTestedAsWithoutOne(string name, int linesBesidesTheWarning)
    {
        string archivePath = ProtectedArchive(name);
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(archivePath)!, "*.par2"))
            File.WriteAllBytes(file, new byte[new FileInfo(file).Length]);

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", archivePath);

        exitCode.Should().Be(1);
        stdOut.Should().BeEmpty();
        stdErr.Should().Contain($"pakko: warning: {name}: The recovery data is damaged or in a form Pakko cannot read.");
        stdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1 + linesBesidesTheWarning, because: stdErr);
    }

    [Theory]
    [InlineData("out.zip")]
    [InlineData("out.tar.gz")]
    public void Test_RewrittenWithoutASet_OldSetRemoved_NoRecoveryVerdict(string name)
    {
        string archivePath = ProtectedArchive(name);
        string folder = Path.GetDirectoryName(archivePath)!;
        string other = Path.Combine(folder, "other.txt");
        File.WriteAllText(other, "a different archive under the same name");
        (int written, _, string writeErr) = CliProcessRunner.Run("a", "-y", name.EndsWith(".zip", StringComparison.Ordinal) ? "-tzip" : "-ttar.gz", archivePath, other);
        written.Should().Be(0, because: writeErr);
        Directory.GetFiles(folder, "*.par2").Should().BeEmpty();

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", archivePath);

        // As if the archive had never had a set: a ZIP passes its own test, a tar is not testable.
        stdOut.Should().NotContain("recovery data");
        if (name.EndsWith(".zip", StringComparison.Ordinal))
        {
            exitCode.Should().Be(0, because: stdErr);
            stdErr.Should().BeEmpty();
        }
        else
        {
            exitCode.Should().Be(1);
            stdErr.Trim().Should().EndWith("tar-family archives have no test capability");
        }
    }

    // The process dies while the new set is being written (Task Manager, a power cut). What it
    // leaves must not make `t` report the rewritten archive as damaged or as not matching its set,
    // and the next run must clean up after it.
    [Fact]
    public async Task Archive_KilledWhileWritingTheSet_LeavesNoFalseVerdict_AndTheNextRunRecovers()
    {
        string archivePath = ProtectedArchive("out.zip");
        string folder = Path.GetDirectoryName(archivePath)!;
        long earlierLength = new FileInfo(archivePath).Length;
        string big = Path.Combine(folder, "big.bin");
        byte[] noise = new byte[24 * 1024 * 1024];
        new Random(373).NextBytes(noise);
        File.WriteAllBytes(big, noise);
        var startInfo = new System.Diagnostics.ProcessStartInfo(CliProcessRunner.ExePath)
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in new[] { "a", "-y", "-rr100", "-mx=0", "-tzip", archivePath, big })
            startInfo.ArgumentList.Add(arg);

        bool killedWhileWriting;
        using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!)
        {
            process.StandardInput.Close();
            // The new archive is in place and the set's temporary files exist: the set is being written.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!process.HasExited && clock.Elapsed < TimeSpan.FromSeconds(60)
                && !(new FileInfo(archivePath) is { Exists: true } info && info.Length > earlierLength
                    && Directory.EnumerateFiles(folder, ".pakko-a-*.tmp").Any()))
            {
                await Task.Delay(2);
            }
            killedWhileWriting = !process.HasExited;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        killedWhileWriting.Should().BeTrue("the kill has to land before the set is finished");
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", archivePath);
        exitCode.Should().Be(0, because: stdErr);
        stdErr.Should().BeEmpty();
        stdOut.Should().NotContain("recovery data");

        (int again, _, string againErr) = CliProcessRunner.Run("a", "-y", "-rr10", "-mx=0", "-tzip", archivePath, big);
        again.Should().Be(0, because: againErr);
        Directory.GetFiles(folder, ".pakko-a-*.tmp").Should().BeEmpty();
        Directory.GetFiles(folder, "*.par2").Should().HaveCount(2);
        (int verified, string verifiedOut, string verifiedErr) = CliProcessRunner.Run("t", archivePath);
        verified.Should().Be(0, because: verifiedErr);
        verifiedOut.Should().Contain("recovery data");
    }

    [Fact]
    public void Archive_NonAsciiPassword_ExitsSevenAndCreatesNothing()
    {
        (string scratchDir, string sourceFile) = CreateSourceFile();
        string zipPath = Path.Combine(scratchDir, "out.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-pпароль", zipPath, sourceFile);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("English letters");
        File.Exists(zipPath).Should().BeFalse();
    }

    [Fact]
    public void Archive_PasswordWithTarFormat_ExitsSevenAndCreatesNothing()
    {
        (string scratchDir, string sourceFile) = CreateSourceFile();
        string tarPath = Path.Combine(scratchDir, "out.tar");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-pSecret123", "-ttar", tarPath, sourceFile);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("only ZIP");
        File.Exists(tarPath).Should().BeFalse();
    }

    [Fact]
    public void Archive_ZipCryptoMethod_ExitsSevenAndCreatesNothing()
    {
        (string scratchDir, string sourceFile) = CreateSourceFile();
        string zipPath = Path.Combine(scratchDir, "out.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-pSecret123", "-mem=ZipCrypto", zipPath, sourceFile);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("AES-256");
        File.Exists(zipPath).Should().BeFalse();
    }

    [Fact]
    public void Archive_BarePasswordWithRedirectedStdin_ExitsSevenAndCreatesNothing()
    {
        // CliProcessRunner always redirects stdin, so there is no console to type into.
        (string scratchDir, string sourceFile) = CreateSourceFile();
        string zipPath = Path.Combine(scratchDir, "out.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-p", zipPath, sourceFile);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("stdin is redirected");
        File.Exists(zipPath).Should().BeFalse();
    }

    [Fact]
    public void Extract_BarePasswordWithRedirectedStdin_ExitsSevenAndExtractsNothing()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", "-p", $"-o{destDir}", EncryptedZipPath);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("stdin is redirected");
        Directory.GetFileSystemEntries(destDir).Should().BeEmpty();
    }

    // --- i: happy path ---

    [Fact]
    public void Info_ExitsZeroAndMentionsZip()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("i");

        exitCode.Should().Be(0);
        stdOut.Should().Contain("zip");
    }

    // --- --version / -v ---

    // T-F222: tests build without Publish-Cli.ps1's /p:Version, so this is always a dev build — it
    // printed a stale "pakko 1.4.2", indistinguishable from the real v1.4.2 release.
    // T-F317: the packaged copy (PAKKO_CLI_EXE pointing at the alias) appends its package name.
    private const string DevBuildVersionPattern = @"^pakko \d+\.\d+\.\d+-dev(\+[0-9a-f]{7})?( \(package PavloRybchenko\.Pakko_[^ )]+\))?$";

    [Fact]
    public void DashDashVersion_ExitsZeroAndPrintsPakkoPrefixedVersion()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("--version");

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        stdOut.Trim().Should().MatchRegex(DevBuildVersionPattern);
    }

    [Fact]
    public void DashV_ExitsZeroAndPrintsPakkoPrefixedVersion()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("-v");

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        stdOut.Trim().Should().MatchRegex(DevBuildVersionPattern);
    }

    // --- a: happy path ---

    [Fact]
    public void Archive_ZipHappyPath_CreatesRealZipWithBothEntries()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string outputZip = Path.Combine(destDir, "out.zip");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(
            "a", outputZip, CliFixtureFiles.SourceFileA, CliFixtureFiles.SourceFileB);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        File.Exists(outputZip).Should().BeTrue();

        using ZipArchive archive = ZipFile.OpenRead(outputZip);
        archive.Entries.Select(e => e.Name).Should().BeEquivalentTo("a.txt", "b.txt");
    }

    // T-F338: "." is the current folder, archived under its own name as 7-Zip does - the entries
    // were "./a.txt".
    [Fact]
    public void Archive_DotAsSource_NamesEntriesByTheCurrentFolder()
    {
        string sourceDir = Path.Combine(CliFixtureFiles.CreateScratchDir(), "proj");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(sourceDir, "sub", "b.txt"), "b");
        string outputZip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(sourceDir, "a", outputZip, ".");

        exitCode.Should().Be(0, stdErr);
        using ZipArchive archive = ZipFile.OpenRead(outputZip);
        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo("proj/a.txt", "proj/sub/b.txt");
    }

    [RequiresTarExe]
    public void Archive_DotAsSourceToTar_NamesEntriesByTheCurrentFolder()
    {
        string sourceDir = Path.Combine(CliFixtureFiles.CreateScratchDir(), "proj");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "a");
        string outputTar = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out.tar");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(sourceDir, "a", "-ttar", outputTar, ".");

        exitCode.Should().Be(0, stdErr);
        (_, string tarStdOut, _) = RunTarExe("-tf", outputTar);
        tarStdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Should().Contain("proj/a.txt").And.OnlyContain(name => name.StartsWith("proj/"));
    }

    [RequiresTarExe]
    public void Archive_TarGzType_CreatesRealGzipCompressedTar()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string outputPath = Path.Combine(destDir, "out.tar.gz");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(
            "a", "-ttar.gz", outputPath, CliFixtureFiles.SourceFileA, CliFixtureFiles.SourceFileB);

        exitCode.Should().Be(0);
        File.Exists(outputPath).Should().BeTrue();

        (int tarExitCode, string tarStdOut, _) = RunTarExe("-tzvf", outputPath);
        tarExitCode.Should().Be(0);
        tarStdOut.Should().Contain("a.txt").And.Contain("b.txt");
    }

    // --- l: happy path ---

    [Fact]
    public void List_ZipHappyPath_PrintsHeaderAndBothEntries()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        stdOut.Should().Contain("Size\tCompressed\tCrc32\tModified\tType\tEncrypted\tPath");
        stdOut.Should().Contain("\tf\t-\ta.txt");
        stdOut.Should().Contain("\tf\t-\tb.txt");
    }

    // T-F238: redirected output used the console code page, so on a cp866 console `l > list.txt`
    // wrote '?' for letters cp866 lacks (і, ї, є). -sccUTF-8 (7-Zip's switch) writes exact UTF-8
    // with no BOM. Stdout is compared as raw bytes on purpose: a decoded capture could hide the loss.
    [Fact]
    public void List_SccUtf8_RedirectedStdoutCarriesExactUtf8NamesWithoutBom()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string zipPath = Path.Combine(scratchDir, "names.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("Звіт.txt");
            archive.CreateEntry("报告.txt");
        }

        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio([], "l", "-sccUTF-8", zipPath);

        exitCode.Should().Be(0, stdErr);
        stdOut.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "a BOM would corrupt `> list.txt`");
        string text = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(stdOut);
        text.Should().Contain("Звіт.txt").And.Contain("报告.txt");
    }

    // T-F263 / T-F244 item 4: a killed `x -so` leaves its output (possibly decrypted) in %TEMP%;
    // the next x/t/l/a run removes staging folders whose process is gone. PID int.MaxValue never
    // exists, so this folder is always a dead run's.
    [Fact]
    public void List_SweepsStagingFolderLeftByADeadProcess()
    {
        string abandoned = Path.Combine(Path.GetTempPath(), "Archiver.CLI.Stdout", $"{int.MaxValue}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(abandoned);
        File.WriteAllText(Path.Combine(abandoned, "secret.txt"), "decrypted plaintext");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("l", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0, stdErr);
        Directory.Exists(abandoned).Should().BeFalse();
    }

    // --- h: happy path (T-F128/T-F09 follow-up) ---

    [Fact]
    public void Hash_SingleFile_PrintsCrc32AndExitsZero()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("h", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        stdOut.Should().Contain($"0D4A1185  {CliFixtureFiles.SourceFileA}");
    }

    [Fact]
    public void Hash_ScrcSha256_PrintsSha256AndExitsZero()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("h", "-scrcSHA256", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0);
        stdOut.Should().Contain($"b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9  {CliFixtureFiles.SourceFileA}");
    }

    [Fact]
    public void Hash_MultipleFiles_PrintsEachIndependently_NoFolderSummary()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("h", CliFixtureFiles.SourceFileA, CliFixtureFiles.SourceFileB);

        exitCode.Should().Be(0);
        stdOut.Should().Contain($"0D4A1185  {CliFixtureFiles.SourceFileA}");
        stdOut.Should().Contain($"735AF9A0  {CliFixtureFiles.SourceFileB}");
        stdOut.Should().NotContain("DataSum");
    }

    [Fact]
    public void Hash_OneFolder_PrintsDataSumAndNamesSumMatchingSevenZip()
    {
        // Real value cross-checked against the vendored 7za.exe (same technique as
        // FileHashServiceTests) for this exact fixture pair (a.txt="hello world",
        // b.txt="second file") — proves the real built pakko.exe's 'h' command, not just the
        // library call, produces NanaZip-compatible output end to end.
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("h", CliFixtureFiles.SourceDir);

        exitCode.Should().Be(0);
        stdOut.Should().Contain("Files: 2");
        stdOut.Should().Contain("CRC32 for data:           80A50B25-00000000");
        stdOut.Should().Contain("CRC32 for data and names: 2FF76A07-00000000");
    }

    [Fact]
    public void Hash_MissingFile_ExitsTwoAndReportsError()
    {
        string missing = Path.Combine(CliFixtureFiles.CreateScratchDir(), "does-not-exist.txt");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("h", missing);

        exitCode.Should().Be(2);
        stdErr.Should().Contain(missing);
    }

    [Fact]
    public void Hash_UnknownScrcMethod_ExitsSevenNamingSupportedMethods()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("h", "-scrcSHA1", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("not supported by Pakko");
    }

    // Real proof of the genuine single-pass streaming path (no CliStreamStaging temp file
    // involved for 'h', unlike x/t/l's -si) — pipes "hello world"'s exact bytes to the real
    // built pakko.exe's stdin and checks the same known-correct CRC-32 comes back.
    [Fact]
    public void Hash_StdinCrc32_MatchesKnownValue()
    {
        byte[] stdinBytes = "hello world"u8.ToArray();

        (int exitCode, byte[] stdOutBytes, string stdErr) = CliProcessRunner.RunWithBinaryStdio(stdinBytes, "h", "-si");

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        System.Text.Encoding.UTF8.GetString(stdOutBytes).Should().Contain("0D4A1185  (stdin)");
    }

    [Fact]
    public void Hash_StdinSha256_MatchesKnownValue()
    {
        byte[] stdinBytes = "hello world"u8.ToArray();

        (int exitCode, byte[] stdOutBytes, string stdErr) = CliProcessRunner.RunWithBinaryStdio(stdinBytes, "h", "-scrcSHA256", "-si");

        exitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(stdOutBytes)
            .Should().Contain("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9  (stdin)");
    }

    // --- three-way rule: one real instance of each category ---

    [Fact]
    public void UnknownCommand_ExitsSevenWithIncorrectCommandLineMessage()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("q", "archive.zip");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("Incorrect command line");
    }

    [Fact]
    public void DeliberatelyUnsupportedCommand_ExitsSevenNamingReason()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("d", "archive.zip");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("not supported by Pakko");
    }

    [Fact]
    public void UnsupportedSwitchOnSupportedCommand_ExitsSevenNamingSwitch()
    {
        // Case 3 (a real 7z switch, deliberately unsupported on any command) — kept as a
        // still-genuinely-unsupported switch after T-F191 gave '-p' a real meaning on 'x'/'t'
        // (see the parser-level test of the same shape in CliArgumentParserTests.cs).
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", "-r0", "archive.zip");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("not supported");
    }

    // --- T-F116: -si / -so streaming ---

    [Fact]
    public void ArchiveWithSo_ThenExtractWithSi_RoundTripsBytesExactly()
    {
        (int archiveExit, byte[] archiveBytes, string archiveErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "a", "-so", "out.zip", CliFixtureFiles.SourceFileA, CliFixtureFiles.SourceFileB);

        archiveExit.Should().Be(0);
        archiveErr.Should().BeEmpty();
        archiveBytes.Should().NotBeEmpty();

        string destDir = CliFixtureFiles.CreateScratchDir();
        (int extractExit, byte[] extractStdOut, string extractErr) = CliProcessRunner.RunWithBinaryStdio(
            archiveBytes, "x", "-si", $"-o{destDir}");

        extractExit.Should().Be(0);
        extractErr.Should().BeEmpty();
        extractStdOut.Should().BeEmpty();
        // T-F156: two root-level files, no common folder — SingleFolder mode no longer wraps them
        // in a subfolder at all (see DECISIONS.md's T-F156 entry), so the private staged-temp-file
        // naming quirk T-F116 documented here no longer surfaces either.
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello world");
        File.ReadAllText(Path.Combine(destDir, "b.txt")).Should().Be("second file");
    }

    [RequiresTarCapability("7z")]
    public void Extract_SevenZipFixtureWithSo_StreamsSingleFileToStdout()
    {
        string sevenZipPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.7z");

        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "x", "-so", sevenZipPath);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        System.Text.Encoding.UTF8.GetString(stdOut).Should().Be("hello from a real 7z fixture\n");
    }

    [RequiresTarCapability("rar")]
    public void Extract_RarFixtureWithSo_StreamsSingleFileToStdout()
    {
        string rarPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.rar");

        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "x", "-so", rarPath);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        System.Text.Encoding.UTF8.GetString(stdOut).Should().Be("hello from a real rar fixture\n");
    }

    [Fact]
    public void Extract_ZipWithSo_MultipleFiles_ExitsTwoNamingCount()
    {
        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "x", "-so", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("found 2");
        stdOut.Should().BeEmpty();
    }

    // T-F116 originally found a genuinely unrecognized single archive path (empty file, or bytes
    // that match no known archive magic number) was silently treated as a no-op success by
    // ZipArchiveService.ExtractAsync's per-item IsZipFile/GetKnownArchiveReason gate (neither an
    // error nor a skipped-file entry was recorded when GetKnownArchiveReason returned null) —
    // confirmed by reproducing the identical exit-0 result against a real on-disk garbage .zip via
    // plain 'x' (no -si involved at all). That Core-level gap is now fixed by T-F117 (a real
    // ArchiveError is recorded), so these tests assert the new loud-error behavior instead. See
    // DECISIONS.md's T-F116/T-F117 entries.
    [Fact]
    public void Extract_SiWithEmptyStdin_SaysStdinWasEmpty()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "x", "-si", $"-o{destDir}");

        // T-F221 item 4: said as what it is, not as an unrecognized archive under a temp name.
        exitCode.Should().Be(2);
        stdErr.Should().Contain("stdin was empty");
        Directory.GetFiles(destDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void Extract_SiWithGarbageBytes_ErrorsAsUnrecognizedArchive()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        byte[] garbage = System.Text.Encoding.UTF8.GetBytes("this is not an archive, just plain garbage text data");

        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            garbage, "x", "-si", $"-o{destDir}");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("File is not a recognized archive format");
        Directory.GetFiles(destDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void Archive_SiSwitch_ExitsSevenNamingReason()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(
            "a", "-si", "out.zip", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("not supported on this command");
    }

    [Fact]
    public void CmdPipe_ArchiveSoToExtractSi_RoundTripsBytesExactly()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string logPath = Path.Combine(destDir, "log.txt");
        string exe = CliProcessRunner.ExePath;

        // Exercises the actual documented recipe (CliHelpText.Text / CLI.md) for a real shell
        // pipeline, not just .NET's own RedirectStandardInput/Output plumbing — this is the test
        // that proves -si/-so actually work "in a pipeline", per T-F116's empirical finding that
        // native PowerShell 5.1 corrupts binary data between two executables while cmd /c "..."
        // does not, on any shell version.
        string pipeline = $"\"{exe}\" a -so out.zip \"{CliFixtureFiles.SourceFileA}\" | " +
                           $"\"{exe}\" x -si -o\"{destDir}\" > \"{logPath}\" 2>&1";

        // Arguments (a raw string), not ArgumentList: .NET's ArgumentList re-escapes each element
        // independently, which mangles a pipeline string that already contains its own embedded
        // quotes/pipes/redirects — confirmed empirically (ArgumentList produced a garbled command
        // line cmd.exe couldn't parse, exit 255; the literal `cmd /c "..."` form below matches
        // exactly what a user would type and is what CliHelpText.Text documents).
        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c \"{pipeline}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        bool exited = process.WaitForExit(30000);

        exited.Should().BeTrue();
        process.ExitCode.Should().Be(0);
        // Single-file archive → isSingleRootFile, no smart-folder wrapper.
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello world");
    }

    // --- x: warnings (T-F280) ---

    // b.txt's local header is renamed to x.txt; the central directory still says b.txt.
    private static string ZipWithALocalNameThatDisagrees(string scratchDir)
    {
        string zipPath = Path.Combine(scratchDir, "tampered.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("b.txt").Open());
            writer.Write("bravo");
        }
        byte[] bytes = File.ReadAllBytes(zipPath);
        int local = bytes.AsSpan().IndexOf("PK\u0003\u0004"u8);
        bytes[local + 30] = (byte)'x';
        File.WriteAllBytes(zipPath, bytes);
        return zipPath;
    }

    [Fact]
    public void Extract_LocalHeadersDisagreeWithCentralDirectory_ExtractsWarnsAndExitsOne()
    {
        string zipPath = ZipWithALocalNameThatDisagrees(CliFixtureFiles.CreateScratchDir());
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", zipPath);

        exitCode.Should().Be(1, "7-Zip's code for a warning; nothing failed and nothing was skipped");
        stdErr.Should().Contain("pakko: warning: tampered.zip: Entries whose local header does not match the central directory: 1 (first: 'b.txt').");
        stdErr.Should().NotContain("pakko: error:").And.NotContain("pakko: skipped:");
        File.ReadAllText(Path.Combine(destDir, "b.txt")).Should().Be("bravo");
        File.Exists(Path.Combine(destDir, "x.txt")).Should().BeFalse();
    }

    // A failed archive next to a warned one: both lines, and the error decides the exit code.
    [Fact]
    public void Extract_OneArchiveWarnsAndOneFails_PrintsBothAndExitsTwo()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string zipPath = ZipWithALocalNameThatDisagrees(scratchDir);
        string missing = Path.Combine(scratchDir, "missing.zip");
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", zipPath, missing);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("pakko: error:").And.Contain("pakko: warning: tampered.zip:");
    }

    private static (int ExitCode, string StdOut, string StdErr) RunTarExe(params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(@"C:\Windows\System32\tar.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        string stdOut = process.StandardOutput.ReadToEnd();
        string stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }
}
