using System.Globalization;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Recovery;
using Archiver.Core.Services;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F275 step 3b: Explorer's "Verify with PAR2" (--recovery-verify) against real PAR2 sets, with
// a fake UI. The archives are noise with the right magic: the set covers bytes, not a format, and
// the router gets empty TarCapabilities, so no test here starts tar.exe.
public sealed class ShellCommandsRecoveryTests : IDisposable
{
    private const int Length = 40_000;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PakkoShellRecoveryTests", Guid.NewGuid().ToString("N"));
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public ShellCommandsRecoveryTests()
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
    public async Task TarFamilyArchiveWithItsSet_SaysItMatches()
    {
        string archive = Protected("notes.tar.gz", out _);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([archive]);

        ui.Sessions.Should().ContainSingle().Which.Title.Should().Be("Testing: notes.tar.gz");
        ui.EndsWithResult.Should().Equal(true);
        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Information);
        message.Text.Should().MatchRegex(
            @"^No errors detected in the archive\(s\)\.\r?\n\r?\nnotes\.tar\.gz: The archive matches its recovery data \(\d+ blocks, \d+ recovery blocks\)\.$");
    }

    // The item is offered on a .par2 file too: the message names the archive the set protects.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Par2FileAlone_ChecksTheArchiveItProtects(bool index)
    {
        Protected("notes.tar.gz", out Par2CreateResult set);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([index ? set.IndexPath : set.VolumePath]);

        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Information);
        message.Text.Should().Contain("notes.tar.gz: The archive matches its recovery data");
    }

    [Fact]
    public async Task ArchiveSelectedWithItsIndexAndVolume_IsCheckedOnce()
    {
        string archive = Protected("notes.tar.gz", out Par2CreateResult set);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([set.VolumePath, archive, set.IndexPath]);

        ui.Sessions.Should().ContainSingle().Which.Title.Should().Be("Testing archives: 3");
        string text = ui.Messages.Should().ContainSingle().Subject.Text;
        text.Split("matches its recovery data").Should().HaveCount(2);
    }

    [Fact]
    public async Task UnderUkrainian_TheVerdictIsTranslated()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");
        string archive = Protected("notes.tar.gz", out _);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([archive]);

        ui.Messages.Should().ContainSingle().Which.Text.Should().NotContain("matches its recovery data")
            .And.Contain("notes.tar.gz: ").And.Contain("блок");
    }

    // "Test archive" is unchanged by this step: it does not look at a set (docs/DECISIONS.md, "Step 3b").
    [Fact]
    public async Task PlainTest_DoesNotLookAtTheSet()
    {
        string archive = Protected("notes.tar.gz", out _);
        var ui = new FakeOperationUi();

        await Create(ui).TestAsync([archive]);

        ui.Messages.Should().ContainSingle().Which.Text.Should().StartWith("Skipped (1):").And.NotContain("recovery data");
    }

    // --- The archive is damaged ---

    [Fact]
    public async Task DamagedWithinTheSet_IsAnErrorThatSaysItCanBeRepaired_AndNothingIsWritten()
    {
        string archive = Protected("notes.tar.gz", out _);
        Damage(archive, 5_000, 16);
        string[] before = Listing();
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([archive]);

        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Contain("notes.tar.gz").And.Contain("its recovery data can repair it")
            .And.NotContain("No errors detected").And.NotContain("matches its recovery data");
        Listing().Should().Equal(before);
    }

    // Also what a forged set looks like from here: packets with valid hashes that describe other bytes.
    [Fact]
    public async Task DamagedBeyondTheSet_SaysSo()
    {
        string archive = Protected("notes.tar.gz", out _);
        File.Copy(Archive("spare.tar.gz", seed: 99), archive, overwrite: true);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([archive]);

        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Contain("beyond what its recovery data can repair").And.NotContain("No errors detected");
    }

    [Fact]
    public async Task OneIntactOneDamaged_OneMessageWithBothVerdicts()
    {
        string good = Protected("good.tar.gz", out _);
        string bad = Protected("bad.tar.gz", out _);
        Damage(bad, 5_000, 16);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([good, bad]);

        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Contain("bad.tar.gz").And.Contain("can repair it")
            .And.Contain("good.tar.gz: The archive matches its recovery data").And.NotContain("No errors detected");
    }

    // --- The set is damaged, replaced, forged or half-written ---

    [Fact]
    public async Task SetOfAnotherFile_IsNotAVerdictOnThisArchive()
    {
        string archive = Archive("notes.tar.gz", seed: 1);
        string other = Protected("other.tar.gz", out Par2CreateResult set, seed: 2);
        File.Move(set.IndexPath, archive + ".par2");
        File.Move(set.VolumePath, archive + Path.GetFileName(set.VolumePath)["other.tar.gz".Length..]);
        File.Delete(other);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([archive]);

        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().Contain("made for another file").And.NotContain("damaged (").And.NotContain("No errors detected");
    }

    [Fact]
    public async Task UnreadableSet_NextToAnArchive_IsAWarning_AndSelectedItself_AnError()
    {
        string archive = Archive("notes.tar.gz", seed: 1);
        string par2 = archive + ".par2";
        File.WriteAllBytes(par2, Noise(4_000, seed: 3));
        var forArchive = new FakeOperationUi();
        var forPar2 = new FakeOperationUi();

        await Create(forArchive).VerifyRecoveryAsync([archive]);
        await Create(forPar2).VerifyRecoveryAsync([par2]);

        forArchive.Messages.Should().ContainSingle().Which.Text.Should()
            .Contain("damaged or in a form Pakko cannot read").And.NotContain("No errors detected");
        OperationMessage message = forPar2.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Contain("damaged or in a form Pakko cannot read");
    }

    [Fact]
    public async Task SetWithoutItsIndex_StillChecksTheArchive()
    {
        string archive = Protected("notes.tar.gz", out Par2CreateResult set);
        File.Delete(set.IndexPath);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([archive]);

        ui.Messages.Should().ContainSingle().Which.Text.Should().Contain("notes.tar.gz: The archive matches its recovery data");
    }

    [Fact]
    public async Task SetWithoutItsVolume_DamagedArchive_CannotBeRepaired()
    {
        string archive = Protected("notes.tar.gz", out Par2CreateResult set);
        File.Delete(set.VolumePath);
        Damage(archive, 5_000, 16);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([archive]);

        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Contain("beyond what its recovery data can repair (0 recovery blocks)");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(1_000)]
    public async Task VolumeCutShort_IndexGone_OneMessageAndNoFalseVerdict(int keep)
    {
        string archive = Protected("notes.tar.gz", out Par2CreateResult set);
        File.Delete(set.IndexPath);
        File.WriteAllBytes(set.VolumePath, File.ReadAllBytes(set.VolumePath)[..keep]);
        Damage(archive, 5_000, 16);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([archive]);

        ui.Messages.Should().ContainSingle().Which.Text.Should()
            .NotContain("can repair it").And.NotContain("matches its recovery data").And.NotContain("No errors detected");
    }

    // What a killed `pakko a -rr` leaves: temporary files, no set. Nothing was checked.
    [Fact]
    public async Task OnlyTemporaryFilesOfAnInterruptedRun_NothingIsClaimed()
    {
        string archive = Archive("notes.tar.gz", seed: 1);
        File.WriteAllBytes(Path.Combine(_root, ".pakko-a-1234.tmp"), Noise(2_000, seed: 4));
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([archive]);

        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().StartWith("Skipped (1):").And.NotContain("No errors detected").And.NotContain("recovery data");
    }

    [Fact]
    public async Task Par2FileWhoseArchiveIsGone_IsAnError()
    {
        string archive = Protected("notes.tar.gz", out Par2CreateResult set);
        File.Delete(archive);
        var ui = new FakeOperationUi();

        await Create(ui).VerifyRecoveryAsync([set.IndexPath]);

        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().NotContain("No errors detected");
    }

    // --- Policy, cancel ---

    [Fact]
    public async Task UnderDisableRecoveryData_APar2FileIsRefused_AndAnArchiveIsOnlyTested()
    {
        string archive = Protected("notes.tar.gz", out Par2CreateResult set);
        var policy = new GroupPolicyOptions { DisableRecoveryData = true };
        var forPar2 = new FakeOperationUi();
        var forArchive = new FakeOperationUi();

        await Create(forPar2, policy).VerifyRecoveryAsync([set.IndexPath]);
        await Create(forArchive, policy).VerifyRecoveryAsync([archive]);

        OperationMessage message = forPar2.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Contain("Group Policy");
        forArchive.Messages.Should().ContainSingle().Which.Text.Should().StartWith("Skipped (1):").And.NotContain("recovery data");
    }

    [Fact]
    public async Task CancelledByUser_ShowsNoMessage()
    {
        string archive = Protected("notes.tar.gz", out _);
        var ui = new FakeOperationUi { CancelOnBegin = true };

        await Create(ui).VerifyRecoveryAsync([archive]);

        ui.Messages.Should().BeEmpty();
        ui.Sessions.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }

    // --- Helpers ---

    private static ShellCommands Create(FakeOperationUi ui, GroupPolicyOptions? policy = null)
    {
        policy ??= new GroupPolicyOptions();
        var services = new ShellServices
        {
            CreateExtractionRouterAsync = () => Task.FromResult<IExtractionRouter>(
                new ExtractionRouter(new ZipArchiveService(policy), new TarSandboxedService(policy), new TarCapabilities(), policy)),
            CreateArchiveCreationRouter = () => throw new NotSupportedException("Nothing is created here."),
            CreateScanServiceAsync = () => throw new NotSupportedException("Nothing is scanned here."),
            LaunchApp = (_, _) => AppLaunchResult.Launched,
        };
        return new ShellCommands(ui, services);
    }

    // Noise under a gzip header, which is what makes the detector call "x.tar.gz" a tar-family archive.
    private string Archive(string name, int seed)
    {
        string path = Path.Combine(_root, name);
        byte[] content = Noise(Length, seed);
        new byte[] { 0x1F, 0x8B, 0x08 }.CopyTo(content, 0);
        File.WriteAllBytes(path, content);
        return path;
    }

    // The archive with a 5 % set next to it, as `pakko a -rr` writes it.
    private string Protected(string name, out Par2CreateResult set, int seed = 1)
    {
        string path = Archive(name, seed);
        set = Par2Creator.Create(path, Par2Creator.ChooseParameters(Length, 5)!.Value, null, CancellationToken.None);
        return path;
    }

    private string[] Listing() =>
        [.. Directory.GetFiles(_root).Order(StringComparer.Ordinal).Select(p => $"{Path.GetFileName(p)} {new FileInfo(p).Length} {File.GetLastWriteTimeUtc(p).Ticks}")];

    private static void Damage(string path, int offset, int count)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = offset;
        stream.Write(Enumerable.Repeat((byte)0xA5, count).ToArray());
    }

    private static byte[] Noise(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }
}
