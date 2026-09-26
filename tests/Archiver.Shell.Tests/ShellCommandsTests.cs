using System.Globalization;
using System.IO.Compression;
using System.Text;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F268: every Explorer command now talks to the user only through IOperationUi. These tests
// drive the real commands against real ZIP files with a fake UI, pinning the behavior the Win32
// dialogs had before the refactor. ZIP only: the extraction router gets empty TarCapabilities, so
// no test here starts tar.exe (the cross-project contention behind T-F130/T-F195).
public sealed class ShellCommandsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PakkoShellCommandsTests", Guid.NewGuid().ToString("N"));
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public ShellCommandsTests()
    {
        Directory.CreateDirectory(_root);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort temp cleanup */ }
    }

    // --- Happy path ---

    [Fact]
    public async Task ExtractHere_TwoFileArchive_ExtractsIntoOwnFolderWithOneSessionAndNoMessage()
    {
        string zip = MakeZip("photos.zip", ("a.txt", "A"), ("b.txt", "B"));
        var ui = new FakeOperationUi();

        await Create(ui).ExtractHereAsync([zip]);

        File.ReadAllText(Path.Combine(_root, "photos", "a.txt")).Should().Be("A");
        File.ReadAllText(Path.Combine(_root, "photos", "b.txt")).Should().Be("B");
        ui.Sessions.Should().ContainSingle();
        ui.Sessions[0].Title.Should().Be("Extracting: photos.zip");
        ui.Sessions[0].Style.Should().Be(ProgressStyle.Bytes);
        ui.Sessions[0].Completed.Should().BeTrue();
        ui.Sessions[0].Disposed.Should().BeTrue();
        ui.Messages.Should().BeEmpty();
    }

    // T-F268 step 3: one window per Explorer command, naming each archive as it starts.
    [Theory]
    [InlineData("here")]
    [InlineData("flat")]
    [InlineData("folder")]
    public async Task Extract_TwoArchives_OneSessionNamingEachArchive(string command)
    {
        string first = MakeZip("one.zip", ("a.txt", "A"), ("b.txt", "B"));
        string second = MakeZip("two.zip", ("c.txt", "C"), ("d.txt", "D"));
        var ui = new FakeOperationUi();

        await RunExtract(Create(ui), command, [first, second]);

        var session = ui.Sessions.Should().ContainSingle().Subject;
        session.Title.Should().Be("Extracting 2 archives");
        session.Items.Should().Equal(("one.zip", 1, 2), ("two.zip", 2, 2));
        session.Completed.Should().BeTrue();
        session.Disposed.Should().BeTrue();
        ui.Messages.Should().BeEmpty();
        Directory.GetFiles(_root, "d.txt", SearchOption.AllDirectories).Should().ContainSingle();
    }

    [Fact]
    public async Task ExtractHere_SingleArchive_NamesItAsTheOnlyItem()
    {
        string zip = MakeZip("photos.zip", ("a.txt", "A"), ("b.txt", "B"));
        var ui = new FakeOperationUi();

        await Create(ui).ExtractHereAsync([zip]);

        ui.Sessions.Should().ContainSingle().Which.Items.Should().Equal(("photos.zip", 1, 1));
    }

    [Fact]
    public async Task Archive_SingleFolder_CreatesZipNextToItWithNoMessage()
    {
        string folder = Path.Combine(_root, "docs");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "x.txt"), "X");
        var ui = new FakeOperationUi();

        await Create(ui).ArchiveAsync([folder], ArchiveContainerFormat.Zip);

        File.Exists(Path.Combine(_root, "docs.zip")).Should().BeTrue();
        ui.Sessions.Should().ContainSingle().Which.Title.Should().Be("Archiving: docs");
        ui.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Test_ValidArchive_ShowsNoErrorsInformation()
    {
        string zip = MakeZip("ok.zip", ("a.txt", "A"));
        var ui = new FakeOperationUi();

        await Create(ui).TestAsync([zip]);

        ui.Sessions.Should().ContainSingle().Which.Title.Should().Be("Testing: ok.zip");
        var message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Information);
        message.Title.Should().Be("Testing: ok.zip");
        message.Text.Should().Be("No errors detected in the archive(s).");
    }

    [Fact]
    public async Task Test_ArchivePlusSkippedInput_ShowsOneCombinedMessage()
    {
        // T-F216: before, the skipped list and "no errors" were two modal boxes in a row.
        string zip = MakeZip("ok.zip", ("a.txt", "A"));
        string notZip = Path.Combine(_root, "notes.tar.gz");
        using (var gzip = new GZipStream(File.Create(notZip), CompressionLevel.Fastest))
            gzip.Write("a gzip stream, which Test skips as a known non-ZIP format"u8);
        var ui = new FakeOperationUi();

        await Create(ui).TestAsync([zip, notZip]);

        var message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Title.Should().Be("Testing 2 archives");
        message.Text.Should().StartWith("Skipped (1):").And.Contain("notes.tar.gz")
            .And.EndWith("No errors detected in the archive(s).");
    }

    [Fact]
    public async Task Hash_SingleFile_ShowsItsSha256()
    {
        string file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "abc");
        var ui = new FakeOperationUi();

        await Create(ui).HashAsync([file], HashAlgorithmKind.Sha256);

        ui.Sessions.Should().ContainSingle().Which.Title.Should().Be("SHA-256: a.txt");
        var message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Information);
        message.Text.ToLowerInvariant().Should().Be("a.txt: ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    // --- Security & boundary ---

    [Fact]
    public async Task ExtractHereFlat_TwoArchivesConflictApplyToAll_AsksOnceForTheWholeSelection()
    {
        File.WriteAllText(Path.Combine(_root, "same.txt"), "old");
        string first = MakeZip("one.zip", ("same.txt", "first"));
        string second = MakeZip("two.zip", ("same.txt", "second"));
        var ui = new FakeOperationUi
        {
            ConflictAnswer = _ => new ConflictDecision { Resolution = ConflictResolution.Overwrite, ApplyToAll = true },
        };

        await Create(ui).ExtractHereFlatAsync([first, second]);

        ui.ConflictPrompts.Should().ContainSingle();
        File.ReadAllText(Path.Combine(_root, "same.txt")).Should().Be("second");
    }

    [Fact]
    public async Task ExtractHereFlat_TwoEncryptedArchivesApplyToRemaining_AsksOnceWithApplyOffered()
    {
        string first = await MakeEncryptedZipAsync("one", "a.txt", "A");
        string second = await MakeEncryptedZipAsync("two", "b.txt", "B");
        var ui = new FakeOperationUi
        {
            PasswordAnswer = _ => new PasswordDecision { Password = Password, ApplyToRemaining = true },
        };

        await Create(ui).ExtractHereFlatAsync([first, second]);

        ui.PasswordPrompts.Should().ContainSingle().Which.CanApplyToRemaining.Should().BeTrue();
        File.ReadAllText(Path.Combine(_root, "a.txt")).Should().Be("A");
        File.ReadAllText(Path.Combine(_root, "b.txt")).Should().Be("B");
        ui.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractHereFlat_SingleEncryptedArchive_DoesNotOfferApplyToRemaining()
    {
        string zip = await MakeEncryptedZipAsync("one", "a.txt", "A");
        var ui = new FakeOperationUi { PasswordAnswer = _ => new PasswordDecision { Password = Password } };

        await Create(ui).ExtractHereFlatAsync([zip]);

        ui.PasswordPrompts.Should().ContainSingle().Which.CanApplyToRemaining.Should().BeFalse();
    }

    [Fact]
    public async Task Test_TwoEncryptedArchivesApplyToRemaining_CoreResolverAsksOnce()
    {
        string first = await MakeEncryptedZipAsync("one", "a.txt", "A");
        string second = await MakeEncryptedZipAsync("two", "b.txt", "B");
        var ui = new FakeOperationUi
        {
            PasswordAnswer = _ => new PasswordDecision { Password = Password, ApplyToRemaining = true },
        };

        await Create(ui).TestAsync([first, second]);

        ui.PasswordPrompts.Should().ContainSingle().Which.CanApplyToRemaining.Should().BeTrue();
        ui.Messages.Should().ContainSingle().Which.Severity.Should().Be(MessageSeverity.Information);
    }

    [Fact]
    public async Task ExtractHereFlat_TraversalEntry_IsReportedAndNothingIsWrittenOutside()
    {
        string inner = Path.Combine(_root, "inner");
        Directory.CreateDirectory(inner);
        string zip = MakeZip(Path.Combine("inner", "evil.zip"), ("../escaped.txt", "E"), ("ok.txt", "OK"));
        var ui = new FakeOperationUi();

        await Create(ui).ExtractHereFlatAsync([zip]);

        File.Exists(Path.Combine(_root, "escaped.txt")).Should().BeFalse();
        File.ReadAllText(Path.Combine(inner, "ok.txt")).Should().Be("OK");
        var message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().NotBe(MessageSeverity.Information);
        message.Text.Should().Contain("escaped.txt");
    }

    // --- Misuse & fool ---

    [Fact]
    public async Task ExtractHereFlat_PasswordCancelled_ShowsErrorAndWritesNothing()
    {
        string zip = await MakeEncryptedZipAsync("locked", "secret.txt", "S");
        var ui = new FakeOperationUi(); // default answer: cancel (null password)

        await Create(ui).ExtractHereFlatAsync([zip]);

        File.Exists(Path.Combine(_root, "secret.txt")).Should().BeFalse();
        ui.Messages.Should().ContainSingle().Which.Severity.Should().Be(MessageSeverity.Error);
    }

    [Fact]
    public async Task ExtractHereFlat_ConflictSkip_KeepsTheExistingFile()
    {
        File.WriteAllText(Path.Combine(_root, "same.txt"), "old");
        string zip = MakeZip("one.zip", ("same.txt", "new"));
        var ui = new FakeOperationUi(); // default answer: Skip

        await Create(ui).ExtractHereFlatAsync([zip]);

        ui.ConflictPrompts.Should().ContainSingle();
        File.ReadAllText(Path.Combine(_root, "same.txt")).Should().Be("old");
    }

    [Fact]
    public async Task ExtractHere_NoProgressWindow_StillExtracts()
    {
        string zip = MakeZip("photos.zip", ("a.txt", "A"), ("b.txt", "B"));
        var ui = new FakeOperationUi { NoProgressWindow = true };

        await Create(ui).ExtractHereAsync([zip]);

        File.Exists(Path.Combine(_root, "photos", "b.txt")).Should().BeTrue();
        ui.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractHere_EmptySelection_DoesNothing()
    {
        var ui = new FakeOperationUi();

        await Create(ui).ExtractHereAsync([]);

        ui.Sessions.Should().BeEmpty();
        ui.Messages.Should().BeEmpty();
    }

    // --- Error path ---

    [Fact]
    public async Task ExtractHereFlat_CorruptedEntry_ShowsErrorNamingTheArchive()
    {
        string zip = MakeCorruptedZip("broken.zip");
        var ui = new FakeOperationUi();

        await Create(ui).ExtractHereFlatAsync([zip]);

        var message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Title.Should().Be("Extracting: broken.zip");
        message.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExtractHereFlat_CorruptedEntryWithoutProgressWindow_StillShowsTheError()
    {
        // Deliberate change (T-F268): before, a missing IProgressDialog skipped the result dialog
        // for Extract/Archive/Test only, so a failure was silent; Hash and Scan already showed it.
        string zip = MakeCorruptedZip("broken.zip");
        var ui = new FakeOperationUi { NoProgressWindow = true };

        await Create(ui).ExtractHereFlatAsync([zip]);

        ui.Messages.Should().ContainSingle().Which.Severity.Should().Be(MessageSeverity.Error);
    }

    [Fact]
    public async Task ExtractHere_CancelledByUser_ShowsNoMessageAndLeavesNoFiles()
    {
        string zip = MakeZip("photos.zip", ("a.txt", "A"), ("b.txt", "B"));
        var ui = new FakeOperationUi { CancelOnBegin = true };

        await Create(ui).ExtractHereAsync([zip]);

        Directory.Exists(Path.Combine(_root, "photos")).Should().BeFalse();
        ui.Messages.Should().BeEmpty();
        ui.Sessions.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }

    // T-F269: Cancel stops the whole Explorer command — before, each archive had its own window
    // and cancel token, so cancelling the first one just moved on to the next.
    [Theory]
    [InlineData("here")]
    [InlineData("flat")]
    [InlineData("folder")]
    public async Task Extract_TwoArchivesCancelledOnTheFirst_SecondIsNeverStarted(string command)
    {
        string first = MakeZip("one.zip", ("a.txt", "A"), ("b.txt", "B"));
        string second = MakeZip("two.zip", ("c.txt", "C"), ("d.txt", "D"));
        var ui = new FakeOperationUi { CancelFirstSessionOnBegin = true };

        await RunExtract(Create(ui), command, [first, second]);

        Directory.GetFiles(_root, "*.txt", SearchOption.AllDirectories).Should().BeEmpty();
        ui.Messages.Should().BeEmpty();
        ui.Sessions.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractHereFlat_TwoCorruptedArchives_OneMessageNamingBoth()
    {
        string first = MakeCorruptedZip("broken1.zip");
        string second = MakeCorruptedZip("broken2.zip");
        var ui = new FakeOperationUi();

        await Create(ui).ExtractHereFlatAsync([first, second]);

        var message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Title.Should().Be("Extracting 2 archives");
        message.Text.Should().Contain("broken1.zip").And.Contain("broken2.zip");
    }

    [Fact]
    public async Task ExtractHereFlat_OneGoodOneCorrupted_ExtractsTheGoodOneAndReportsTheOther()
    {
        string good = MakeZip("good.zip", ("g.txt", "G"));
        string bad = MakeCorruptedZip("bad.zip");
        var ui = new FakeOperationUi();

        await Create(ui).ExtractHereFlatAsync([good, bad]);

        File.ReadAllText(Path.Combine(_root, "g.txt")).Should().Be("G");
        var message = ui.Messages.Should().ContainSingle().Subject;
        message.Text.Should().Contain("bad.zip").And.NotContain("good.zip");
    }

    [Fact]
    public async Task Test_CancelledByUser_ShowsNoMessage()
    {
        string zip = MakeZip("ok.zip", ("a.txt", "A"));
        var ui = new FakeOperationUi { CancelOnBegin = true };

        await Create(ui).TestAsync([zip]);

        ui.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AppLaunchResult.TooManyFiles, "Too many files are selected to open in Pakko. Select fewer files and try again.")]
    [InlineData(AppLaunchResult.NoPackage, "Pakko could not be opened. Reinstall Pakko and try again.")]
    [InlineData(AppLaunchResult.Failed, "The operation failed.")]
    public void OpenUi_LaunchFailure_ShowsErrorWithoutASession(AppLaunchResult launchResult, string expectedText)
    {
        var ui = new FakeOperationUi();

        Create(ui, launch: (_, _) => launchResult).OpenUi(LaunchOperation.Browse, [Path.Combine(_root, "a.zip")]);

        ui.Sessions.Should().BeEmpty();
        var message = ui.Messages.Should().ContainSingle().Subject;
        message.Title.Should().Be("Pakko");
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Be(expectedText);
    }

    [Fact]
    public void OpenUi_Launched_ShowsNothing()
    {
        var ui = new FakeOperationUi();
        LaunchOperation? seen = null;

        Create(ui, launch: (op, _) => { seen = op; return AppLaunchResult.Launched; })
            .OpenUi(LaunchOperation.Extract, [Path.Combine(_root, "a.zip")]);

        seen.Should().Be(LaunchOperation.Extract);
        ui.Messages.Should().BeEmpty();
    }

    // --- Helpers ---

    private const string Password = "correct horse";

    private static ShellCommands Create(
        FakeOperationUi ui,
        Func<LaunchOperation, IReadOnlyList<string>, AppLaunchResult>? launch = null)
    {
        var policy = new GroupPolicyOptions();
        var services = new ShellServices
        {
            CreateExtractionRouterAsync = () => Task.FromResult<IExtractionRouter>(
                new ExtractionRouter(new ZipArchiveService(policy), new TarSandboxedService(policy), new TarCapabilities(), policy)),
            CreateArchiveCreationRouter = () =>
                new ArchiveCreationRouter(new ZipArchiveService(policy), new TarSandboxedService(policy), policy),
            CreateArchiveService = () => new ZipArchiveService(policy),
            CreateScanServiceAsync = () => throw new NotSupportedException("Scan needs AMSI; covered by OperationMessagesTests."),
            LaunchApp = launch ?? ((_, _) => AppLaunchResult.Launched),
        };
        return new ShellCommands(ui, services);
    }

    private static Task RunExtract(ShellCommands commands, string command, IReadOnlyList<string> archives) => command switch
    {
        "here" => commands.ExtractHereAsync(archives),
        "flat" => commands.ExtractHereFlatAsync(archives),
        "folder" => commands.ExtractFolderAsync(archives),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    private string MakeZip(string relativePath, params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(_root, relativePath);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        return path;
    }

    // One stored entry whose content bytes are altered after the CRC-32 was written (T-F246).
    private string MakeCorruptedZip(string name)
    {
        const string content = "PAKKO-CORRUPTION-TARGET-0123456789";
        string path = MakeZip(name, ("data.txt", content));
        byte[] bytes = File.ReadAllBytes(path);
        byte[] marker = Encoding.ASCII.GetBytes(content);
        int at = bytes.AsSpan().IndexOf(marker);
        at.Should().BeGreaterThanOrEqualTo(0);
        bytes[at] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private async Task<string> MakeEncryptedZipAsync(string archiveName, string entryName, string content)
    {
        string source = Path.Combine(_root, "src-" + archiveName);
        Directory.CreateDirectory(source);
        string file = Path.Combine(source, entryName);
        File.WriteAllText(file, content);

        var result = await new ZipArchiveService().ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _root,
            ArchiveName = archiveName,
            ResolvePasswordAsync = _ => Task.FromResult(new PasswordDecision { Password = Password }),
        });
        result.Success.Should().BeTrue();

        Directory.Delete(source, recursive: true);
        return Path.Combine(_root, archiveName + ".zip");
    }
}
