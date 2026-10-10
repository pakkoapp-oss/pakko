using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Recovery;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F275 step 3c: the Archive Browser's line about the PAR2 files next to the open archive. The
// first half feeds it hand-built results; the second runs the real test on real sets, so the
// line is checked against what Core says and not against what this file assumes Core says.
public sealed class RecoveryPanelTests : IDisposable
{
    private const int Length = 40_000;
    private const string FoundText = "PAR2 files were found.";
    private static readonly GroupPolicyOptions NoPolicy = new();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PakkoRecoveryPanelTests", Guid.NewGuid().ToString("N"));

    public RecoveryPanelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort temp cleanup */ }
    }

    // The App renders through CoreMessageText.Of; the tag shows the line went through the renderer.
    private static string Render(CoreText? text, string english) => text is null ? english : $"<{text.Code}> {english}";

    public static TheoryData<MessageCode> VerdictCodes =>
    [
        MessageCode.RecoveryDataDamagedRepairable, MessageCode.RecoveryDataDamagedNotRepairable,
        MessageCode.RecoveryDataRepairTooLarge, MessageCode.RecoveryDataDoesNotMatch, MessageCode.RecoveryDataUnusable,
        MessageCode.RecoveryDataForAnotherFile, MessageCode.RecoveryDataNameMismatch,
    ];

    // --- Before a test ---

    [Fact]
    public void None_ShowsNowhere()
    {
        RecoveryPanel.None.HasFiles.Should().BeFalse();
        RecoveryPanel.None.Text.Should().BeEmpty();
        RecoveryPanel.None.IsOpenAt(insideArchive: true, nested: false).Should().BeFalse();
    }

    [Fact]
    public void Found_SaysOnlyThatFilesAreThere()
    {
        var panel = RecoveryPanel.Found(FoundText);

        panel.Should().Be(new RecoveryPanel(true, RecoveryPanelSeverity.Informational, FoundText) { OffersRepair = true });
    }

    [Fact]
    public void Found_ForAnArchiveThatDidNotOpen_LeadsWithWhy()
    {
        var panel = RecoveryPanel.Found(FoundText, "The archive is damaged.");

        panel.Should().Be(new RecoveryPanel(true, RecoveryPanelSeverity.Warning, "The archive is damaged. " + FoundText) { OffersRepair = true });
    }

    [Theory]
    [InlineData(@"Source path does not exist: C:\a\gone.zip", @"Source path does not exist: C:\a\gone.zip. " + FoundText)]
    [InlineData("Cannot read it! ", "Cannot read it! " + FoundText)]
    [InlineData("Why?", "Why? " + FoundText)]
    public void Found_AListingErrorWithoutAStop_StillReadsAsTwoSentences(string listingError, string expected) =>
        RecoveryPanel.Found(FoundText, listingError).Text.Should().Be(expected);

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)] // a nested archive is a temporary copy: the set is not its set
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void IsOpenAt_OnlyInTheArchiveOpenedFromDisk(bool insideArchive, bool nested, bool expected) =>
        RecoveryPanel.Found(FoundText).IsOpenAt(insideArchive, nested).Should().Be(expected);

    // --- After a test: hand-built results ---

    [Theory]
    [MemberData(nameof(VerdictCodes))]
    public void After_AVerdictListedAsAnError_IsAnErrorInCoresWords(MessageCode code)
    {
        var result = new ArchiveResult { Errors = [CoreMessages.Error(@"C:\a\photos.zip", code, 3, 5, 2)] };

        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(@"C:\a\photos.zip", result, Render);

        panel.HasFiles.Should().BeTrue();
        panel.Severity.Should().Be(RecoveryPanelSeverity.Error);
        panel.Text.Should().Be(Render(result.Errors[0].Text, result.Errors[0].Message)).And.StartWith($"<{code}> ");
    }

    [Theory]
    [MemberData(nameof(VerdictCodes))]
    public void After_AVerdictListedAsAWarning_IsAWarning(MessageCode code)
    {
        var text = new CoreText(code, 3, 5, 2);
        var result = new ArchiveResult { Warnings = [new ArchiveWarning { SourcePath = @"C:\a\photos.zip", Message = text.English, Text = text }] };

        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(@"C:\a\photos.zip", result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Warning);
        panel.Text.Should().Be(Render(text, text.English));
    }

    [Fact]
    public void After_AMatch_IsASuccess()
    {
        ArchiveResult result = Matching(@"C:\a\photos.zip");

        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(@"C:\a\photos.zip", result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Success);
        panel.Text.Should().StartWith($"<{MessageCode.RecoveryDataIntact}> The archive matches its recovery data");
    }

    // What the archive's own test found (a CRC mismatch, a wrong password) is the dialog's to say.
    [Fact]
    public void After_OnlyTheArchivesOwnErrors_LeavesThePanelAsItWas()
    {
        var before = RecoveryPanel.Found(FoundText);
        var result = new ArchiveResult
        {
            Errors = [CoreMessages.Error(@"C:\a\photos.zip", MessageCode.RecoveryDataTargetNotFound), new ArchiveError { SourcePath = @"C:\a\photos.zip", Message = "CRC mismatch" }],
            Warnings = [new ArchiveWarning { SourcePath = @"C:\a\photos.zip", Message = "something else" }],
        };

        before.After(@"C:\a\photos.zip", result, Render).Should().BeSameAs(before);
    }

    [Fact]
    public void After_AnEmptyResult_LeavesThePanelAsItWas()
    {
        var before = RecoveryPanel.Found(FoundText, "Cannot read the archive.");

        before.After(@"C:\a\photos.zip", new ArchiveResult(), Render).Should().BeSameAs(before);
        RecoveryPanel.None.After(@"C:\a\photos.zip", new ArchiveResult(), Render).Should().BeSameAs(RecoveryPanel.None);
    }

    // One test can name several archives (a .par2 next to it); only this archive's lines count.
    [Fact]
    public void After_AnotherArchivesVerdict_IsNeverTaken()
    {
        var before = RecoveryPanel.Found(FoundText);
        var other = new CoreText(MessageCode.RecoveryDataDoesNotMatch, 3, 5);
        var result = new ArchiveResult
        {
            Errors = [CoreMessages.Error(@"C:\a\other.zip", MessageCode.RecoveryDataDamagedRepairable, 1, 5, 2)],
            Warnings = [new ArchiveWarning { SourcePath = @"C:\b\photos.zip", Message = other.English, Text = other }],
            RecoveryChecks = Matching(@"C:\a\photos.zip.bak").RecoveryChecks,
        };

        before.After(@"C:\a\photos.zip", result, Render).Should().BeSameAs(before);
        RecoveryPanel.MatchLine(@"C:\a\photos.zip", result, Render).Should().BeNull();
    }

    [Fact]
    public void After_TheSameArchiveSpelledDifferently_IsTaken()
    {
        ArchiveResult result = Matching(@"C:\a\sub\..\PHOTOS.zip");

        RecoveryPanel.None.After(@"c:\a\photos.zip", result, Render).Severity.Should().Be(RecoveryPanelSeverity.Success);
        RecoveryPanel.MatchLine(@"c:\a\photos.zip", result, Render).Should().NotBeNull();
    }

    [Fact]
    public void After_ErrorOutranksWarning_AndBothAreSaid()
    {
        var warning = new CoreText(MessageCode.RecoveryDataNameMismatch, "old.zip");
        var result = new ArchiveResult
        {
            Errors = [CoreMessages.Error(@"C:\a\photos.zip", MessageCode.RecoveryDataDamagedRepairable, 1, 5, 2)],
            Warnings = [new ArchiveWarning { SourcePath = @"C:\a\photos.zip", Message = warning.English, Text = warning }],
        };

        RecoveryPanel panel = RecoveryPanel.None.After(@"C:\a\photos.zip", result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Error);
        panel.Text.Should().Be(Render(result.Errors[0].Text, result.Errors[0].Message) + " " + Render(warning, warning.English));
    }

    // A path that is not a path must not throw out of a property getter's way.
    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("a|b<c>.zip")]
    public void After_AnUnusablePath_NeverThrows(string path)
    {
        var result = new ArchiveResult { Errors = [CoreMessages.Error(path, MessageCode.RecoveryDataUnusable)] };

        Action act = () =>
        {
            RecoveryPanel.None.After(path, result, Render);
            RecoveryPanel.MatchLine(path, result, Render);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void MatchLine_IsTheSetsLineForThatArchive()
    {
        ArchiveResult result = Matching(@"C:\a\photos.zip");

        RecoveryPanel.MatchLine(@"C:\a\photos.zip", result, Render).Should()
            .StartWith($"<{MessageCode.RecoveryDataIntact}> The archive matches its recovery data");
        RecoveryPanel.MatchLine(@"C:\a\photos.zip", new ArchiveResult(), Render).Should().BeNull();
    }

    // A check that is not Intact carries no text: its words are the error or the warning.
    [Fact]
    public void MatchLine_ACheckThatIsNotAMatch_IsNull()
    {
        var result = new ArchiveResult
        {
            RecoveryChecks = [new RecoveryCheck { ArchivePath = @"C:\a\photos.zip", State = RecoveryState.Repairable, Blocks = 10, DamagedBlocks = 1, RecoveryBlocks = 2 }],
        };

        RecoveryPanel.MatchLine(@"C:\a\photos.zip", result, Render).Should().BeNull();
    }

    // --- After a test: the real router on real sets ---

    [Theory]
    [InlineData("photos.zip")]
    [InlineData("notes.tar.gz")]
    public async Task Real_ArchiveThatMatchesItsSet_IsASuccess(string name)
    {
        string archive = name.EndsWith(".zip", StringComparison.Ordinal) ? MakeZip(name, 1) : MakeTarGz(name, 1);
        Protect(archive);

        ArchiveResult result = await TestAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(archive, result, Render);

        result.Outcome.Should().Be(OperationOutcome.Completed);
        panel.Severity.Should().Be(RecoveryPanelSeverity.Success);
        panel.Text.Should().MatchRegex(@"^<RecoveryDataIntact> The archive matches its recovery data \(\d+ blocks, \d+ recovery blocks\)\.$");
        RecoveryPanel.MatchLine(archive, result, Render).Should().Be(panel.Text);
    }

    [Fact]
    public async Task Real_DamageTheSetCanRepair_IsAnErrorThatSaysSo()
    {
        string archive = MakeZip("photos.zip", 1);
        Protect(archive);
        Damage(archive, 5_000, 16);

        ArchiveResult result = await TestAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(archive, result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Error);
        panel.Text.Should().StartWith("<RecoveryDataDamagedRepairable> ").And.Contain("can repair");
        RecoveryPanel.MatchLine(archive, result, Render).Should().BeNull();
    }

    [Fact]
    public async Task Real_DamageBeyondTheSet_IsAnErrorThatSaysSo()
    {
        string archive = MakeZip("photos.zip", 1);
        Protect(archive);
        Damage(archive, 2_000, 30_000);

        ArchiveResult result = await TestAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(archive, result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Error);
        panel.Text.Should().StartWith("<RecoveryDataDamagedNotRepairable> ");
    }

    // The archive that does not open at all (its listing failed) is the case the set is for: the
    // panel was opened with the listing error and the test replaces it with the verdict.
    [Fact]
    public async Task Real_ArchiveCutShort_GoesFromTheListingErrorToTheVerdict()
    {
        string archive = MakeZip("photos.zip", 1);
        Protect(archive);
        using (FileStream stream = File.Open(archive, FileMode.Open, FileAccess.Write))
            stream.SetLength(stream.Length - 300);
        var before = RecoveryPanel.Found(FoundText, "Cannot read the archive.");

        RecoveryPanel panel = before.After(archive, await TestAsync(archive), Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Error);
        panel.Text.Should().StartWith("<RecoveryDataDamaged").And.NotContain(FoundText).And.NotContain("Cannot read the archive.");
    }

    // A good ZIP beside the set of its earlier bytes: a warning about the set, never "damaged".
    [Fact]
    public async Task Real_ArchiveRewrittenBesideItsOldSet_IsAWarning()
    {
        string archive = MakeZip("photos.zip", 1);
        Protect(archive);
        File.Delete(archive);
        MakeZip("photos.zip", 2);

        ArchiveResult result = await TestAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(archive, result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Warning);
        panel.Text.Should().StartWith("<RecoveryDataDoesNotMatch> ").And.NotContain("damaged");
        RecoveryPanel.MatchLine(archive, result, Render).Should().BeNull();
    }

    [Fact]
    public async Task Real_SetThatCannotBeRead_IsAWarning()
    {
        string archive = MakeZip("photos.zip", 1);
        File.WriteAllBytes(archive + ".par2", Noise(3_000, 9));

        ArchiveResult result = await TestAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(archive, result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Warning);
        panel.Text.Should().StartWith("<RecoveryDataUnusable> ");
    }

    // A set put under this archive's name by hand, or by an attacker: never a match.
    [Fact]
    public async Task Real_AnotherFilesSetUnderThisName_IsNotAMatch()
    {
        string archive = MakeZip("photos.zip", 1);
        string other = MakeZip("other.zip", 2);
        Par2CreateResult set = Protect(other);
        File.Move(set.IndexPath, archive + ".par2");
        File.Move(set.VolumePath, Path.Combine(_root, "photos.zip.vol0+1.par2"));
        File.Delete(other);

        ArchiveResult result = await TestAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(archive, result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Warning);
        panel.Text.Should().NotContain("matches its recovery data");
        RecoveryPanel.MatchLine(archive, result, Render).Should().BeNull();
    }

    // A set whose writer was cancelled or killed leaves only temporary files: no panel at open
    // (IRecoveryService.HasFilesFor), and the test says nothing about a set.
    [Fact]
    public async Task Real_OnlyTheWritersTemporaryFiles_NoPanelBeforeOrAfter()
    {
        string archive = MakeZip("photos.zip", 1);
        File.WriteAllBytes(Path.Combine(_root, ".pakko-photos.zip.par2-1.tmp"), Noise(2_000, 3));
        File.WriteAllBytes(archive + ".par2.tmp", Noise(2_000, 4));

        Recovery(NoPolicy).HasFilesFor(archive).Should().BeFalse();
        RecoveryPanel.None.After(archive, await TestAsync(archive), Render).Should().BeSameAs(RecoveryPanel.None);
    }

    [Fact]
    public async Task Real_VolumeCutShort_IsNeverAMatchWithoutItsBlocks()
    {
        string archive = MakeZip("photos.zip", 1);
        Par2CreateResult set = Protect(archive);
        using (FileStream stream = File.Open(set.VolumePath, FileMode.Open, FileAccess.Write))
            stream.SetLength(stream.Length / 2);
        Damage(archive, 5_000, 16);

        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(archive, await TestAsync(archive), Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Error);
        panel.Text.Should().NotContain("matches its recovery data");
    }

    [Fact]
    public async Task Real_UnderThePolicy_NoPanelBeforeOrAfter()
    {
        var policy = new GroupPolicyOptions { DisableRecoveryData = true };
        string archive = MakeZip("photos.zip", 1);
        Protect(archive);

        Recovery(policy).HasFilesFor(archive).Should().BeFalse();
        RecoveryPanel.None.After(archive, await TestAsync(archive, policy), Render).Should().BeSameAs(RecoveryPanel.None);
    }

    // The App's Test in a nested archive passes verifyRecoveryData: false; so does "Test archive".
    [Fact]
    public async Task Real_TestWithoutTheSetCheck_LeavesThePanelAsItWas()
    {
        string archive = MakeZip("photos.zip", 1);
        Protect(archive);
        var before = RecoveryPanel.Found(FoundText);

        ArchiveResult result = await Router(NoPolicy).TestAsync([archive]);

        before.After(archive, result, Render).Should().BeSameAs(before);
    }

    // A cancelled test hands back no result, so there is nothing to make half a verdict from.
    [Fact]
    public async Task Real_CancelledTest_ThrowsAndGivesNoResult()
    {
        string archive = MakeZip("photos.zip", 1);
        Protect(archive);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> test = () => Router(NoPolicy).TestAsync([archive], verifyRecoveryData: true, cancellationToken: cts.Token);

        await test.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Step 4c: when Repair is offered, and what a repair leaves on the line ---

    [Fact]
    public void OffersRepair_BeforeAnyCheck_ButNotWithoutFiles()
    {
        RecoveryPanel.None.OffersRepair.Should().BeFalse();
        RecoveryPanel.Found(FoundText).OffersRepair.Should().BeTrue();
        RecoveryPanel.Found(FoundText, "Cannot read the archive.").OffersRepair.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(VerdictCodes))]
    public void OffersRepair_AfterATest_OnlyForDamageTheSetCanRebuild(MessageCode code)
    {
        var result = new ArchiveResult { Errors = [CoreMessages.Error(@"C:\a\photos.zip", code, 3, 5, 2)] };

        RecoveryPanel panel = RecoveryPanel.Found(FoundText).After(@"C:\a\photos.zip", result, Render);

        panel.OffersRepair.Should().Be(code == MessageCode.RecoveryDataDamagedRepairable);
    }

    [Fact]
    public void OffersRepair_AfterAMatch_IsOff()
    {
        RecoveryPanel.Found(FoundText).After(@"C:\a\photos.zip", Matching(@"C:\a\photos.zip"), Render).OffersRepair.Should().BeFalse();
    }

    // Another archive's "can repair it" must not offer a repair of this one.
    [Fact]
    public void OffersRepair_ForAnotherArchivesDamage_IsNotTurnedOn()
    {
        var result = new ArchiveResult
        {
            Errors = [CoreMessages.Error(@"C:\a\other.zip", MessageCode.RecoveryDataDamagedRepairable, 3, 5, 2)],
            RecoveryChecks = Matching(@"C:\a\photos.zip").RecoveryChecks,
        };

        RecoveryPanel.Found(FoundText).After(@"C:\a\photos.zip", result, Render).OffersRepair.Should().BeFalse();
    }

    [Theory]
    [InlineData("photos.zip", "photos.repaired.zip")]
    [InlineData("notes.tar.gz", "notes.repaired.tar.gz")]
    public async Task Real_Repair_SaysWhereTheCopyIs_AndOffersNoSecondRepair(string name, string repairedName)
    {
        string archive = name.EndsWith(".zip", StringComparison.Ordinal) ? MakeZip(name, 1) : MakeTarGz(name, 1);
        byte[] good = File.ReadAllBytes(archive);
        Protect(archive);
        Damage(archive, 5_000, 16);

        ArchiveResult result = await RepairAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).AfterRepair(archive, result, Render);

        string copy = Path.Combine(_root, repairedName);
        RecoveryPanel.RepairedCopy(archive, result).Should().Be(copy);
        File.ReadAllBytes(copy).Should().Equal(good);
        panel.Severity.Should().Be(RecoveryPanelSeverity.Success);
        panel.OffersRepair.Should().BeFalse();
        panel.Text.Should().StartWith("<RecoveryDataRepaired> The archive was repaired (1 of ").And.Contain(repairedName);
        RecoveryPanel.NeedsAnotherFolder(archive, result).Should().BeFalse();
        // A repaired check is not "the set matches": the test's dialog and line must not take it.
        RecoveryPanel.MatchLine(archive, result, Render).Should().BeNull();
        RecoveryPanel.Found(FoundText).After(archive, result, Render).Severity.Should().Be(RecoveryPanelSeverity.Informational);
    }

    // The archive kept open although it does not list: the line goes from the listing error to
    // the repair, and the copy is a ZIP that lists and tests.
    [Fact]
    public async Task Real_RepairOfAnArchiveCutShort_GoesFromTheListingErrorToTheCopy()
    {
        string archive = MakeZip("photos.zip", 1);
        Protect(archive);
        using (FileStream stream = File.Open(archive, FileMode.Open, FileAccess.Write))
            stream.SetLength(stream.Length - 300);
        var before = RecoveryPanel.Found(FoundText, "Cannot read the archive.");

        ArchiveResult result = await RepairAsync(archive);
        RecoveryPanel panel = before.AfterRepair(archive, result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Success);
        panel.Text.Should().NotContain("Cannot read the archive.").And.NotContain(FoundText);
        ArchiveResult copyTested = await Router(NoPolicy).TestAsync([RecoveryPanel.RepairedCopy(archive, result)!]);
        copyTested.Outcome.Should().Be(OperationOutcome.Completed);
    }

    [Fact]
    public async Task Real_RepairOfAnIntactArchive_IsAMatchAndNoCopy()
    {
        string archive = MakeTarGz("notes.tar.gz", 1);
        Protect(archive);

        ArchiveResult result = await RepairAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).AfterRepair(archive, result, Render);

        RecoveryPanel.RepairedCopy(archive, result).Should().BeNull();
        panel.Severity.Should().Be(RecoveryPanelSeverity.Success);
        panel.OffersRepair.Should().BeFalse();
        panel.Text.Should().StartWith("<RecoveryDataIntact> ");
    }

    [Fact]
    public async Task Real_RepairBeyondTheSet_IsAnErrorAndNoSecondOffer()
    {
        string archive = MakeTarGz("notes.tar.gz", 1);
        Protect(archive);
        Damage(archive, 2_000, 30_000);

        ArchiveResult result = await RepairAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).AfterRepair(archive, result, Render);

        RecoveryPanel.RepairedCopy(archive, result).Should().BeNull();
        panel.Severity.Should().Be(RecoveryPanelSeverity.Error);
        panel.OffersRepair.Should().BeFalse();
        Directory.GetFiles(_root, "*repaired*").Should().BeEmpty();
    }

    // The set files went away between the open and the click: an error on the line, no offer.
    [Fact]
    public async Task Real_RepairAfterTheSetWasDeleted_SaysNoRecoveryData()
    {
        string archive = MakeTarGz("notes.tar.gz", 1);
        Par2CreateResult set = Protect(archive);
        Damage(archive, 5_000, 16);
        File.Delete(set.IndexPath);
        File.Delete(set.VolumePath);

        ArchiveResult result = await RepairAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).AfterRepair(archive, result, Render);

        panel.Severity.Should().Be(RecoveryPanelSeverity.Error);
        panel.Text.Should().StartWith("<RecoveryDataNotFound> ");
        panel.OffersRepair.Should().BeFalse();
    }

    // A forged recovery block: the copy fails the final check, the line says so, nothing is kept.
    [Fact]
    public async Task Real_RepairFromForgedRecoveryData_SaysTheCopyWasNotKept()
    {
        string archive = MakeTarGz("notes.tar.gz", 1);
        Par2CreateResult set = Protect(archive);
        Damage(archive, 5_000, 16);
        byte[] volume = File.ReadAllBytes(set.VolumePath);
        Forge(volume);
        File.WriteAllBytes(set.VolumePath, volume);

        ArchiveResult result = await RepairAsync(archive);
        RecoveryPanel panel = RecoveryPanel.Found(FoundText).AfterRepair(archive, result, Render);

        RecoveryPanel.RepairedCopy(archive, result).Should().BeNull();
        panel.Severity.Should().Be(RecoveryPanelSeverity.Error);
        panel.Text.Should().StartWith("<RecoveryRepairCheckFailed> ");
        Directory.GetFiles(_root, "*repaired*").Should().BeEmpty();
    }

    // A folder where files cannot be created: the App then asks for another one, and that works.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task Real_RepairWhereNothingCanBeWritten_AsksForAnotherFolder_WhichWorks()
    {
        string archive = MakeTarGz("notes.tar.gz", 1);
        byte[] good = File.ReadAllBytes(archive);
        Protect(archive);
        Damage(archive, 5_000, 16);
        var folder = new DirectoryInfo(_root);
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!, System.Security.AccessControl.FileSystemRights.CreateFiles,
            System.Security.AccessControl.InheritanceFlags.None, System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Deny);
        System.Security.AccessControl.DirectorySecurity security = folder.GetAccessControl();
        security.AddAccessRule(deny);
        folder.SetAccessControl(security);
        ArchiveResult refused;
        try
        {
            refused = await RepairAsync(archive);
        }
        finally
        {
            security = folder.GetAccessControl();
            security.RemoveAccessRule(deny);
            folder.SetAccessControl(security);
        }
        string elsewhere = Path.Combine(Path.GetTempPath(), "PakkoRecoveryPanelTests", Guid.NewGuid().ToString("N"));
        try
        {
            ArchiveResult retried = await RepairAsync(archive, elsewhere);

            RecoveryPanel.NeedsAnotherFolder(archive, refused).Should().BeTrue();
            RecoveryPanel.RepairedCopy(archive, refused).Should().BeNull();
            // The line keeps offering the repair: nothing was learned against it.
            RecoveryPanel.Found(FoundText).AfterRepair(archive, refused, Render).OffersRepair.Should().BeTrue();
            RecoveryPanel.NeedsAnotherFolder(archive, retried).Should().BeFalse();
            File.ReadAllBytes(RecoveryPanel.RepairedCopy(archive, retried)!).Should().Equal(good);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public void RepairHelpers_AnotherArchivesResult_IsNeverTaken()
    {
        var result = new ArchiveResult
        {
            Errors = [CoreMessages.Error(@"C:\a\other.zip", MessageCode.RecoveryRepairNotWritten, "denied")],
            RecoveryChecks =
            [
                new RecoveryCheck
                {
                    ArchivePath = @"C:\a\other.zip", State = RecoveryState.Repaired, RepairedPath = @"C:\a\other.repaired.zip",
                    Text = new CoreText(MessageCode.RecoveryDataRepaired, 1, 10, "other.repaired.zip"),
                },
            ],
        };
        var before = RecoveryPanel.Found(FoundText);

        RecoveryPanel.RepairedCopy(@"C:\a\photos.zip", result).Should().BeNull();
        RecoveryPanel.NeedsAnotherFolder(@"C:\a\photos.zip", result).Should().BeFalse();
        before.AfterRepair(@"C:\a\photos.zip", result, Render).Should().BeSameAs(before);
    }

    // Flips one byte of the first recovery block's data and recomputes its packet MD5.
    private static void Forge(byte[] volume)
    {
        byte[] type = System.Text.Encoding.ASCII.GetBytes("PAR 2.0\0RecvSlic");
        for (int offset = 0; offset + 64 <= volume.Length;)
        {
            int length = (int)BitConverter.ToInt64(volume, offset + 8);
            if (volume.AsSpan(offset + 48, 16).SequenceEqual(type))
            {
                volume[offset + 68] ^= 0x01;
                Par2Md5.Hash(volume.AsSpan(offset + 32, length - 32)).CopyTo(volume, offset + 16);
                return;
            }
            offset += length;
        }
        throw new InvalidOperationException("no recovery block in the volume");
    }

    // --- Helpers ---

    private static ArchiveResult Matching(string archivePath) => new()
    {
        RecoveryChecks =
        [
            new RecoveryCheck
            {
                ArchivePath = archivePath, State = RecoveryState.Intact, Blocks = 10, RecoveryBlocks = 2,
                Text = new CoreText(MessageCode.RecoveryDataIntact, 10, 2),
            },
        ],
    };

    private static ExtractionRouter Router(GroupPolicyOptions policy) =>
        new ExtractionRouter(new ZipArchiveService(policy), new TarSandboxedService(policy), new TarCapabilities(), policy);

    private static RecoveryService Recovery(GroupPolicyOptions policy) =>
        new(policy, () => Task.FromResult<Archiver.Core.Interfaces.IExtractionRouter>(Router(policy)));

    private static Task<ArchiveResult> RepairAsync(string archive, string? outputDirectory = null) =>
        Recovery(NoPolicy).RepairAsync(new RepairOptions { Paths = [archive], OutputDirectory = outputDirectory });

    private static Task<ArchiveResult> TestAsync(string archive, GroupPolicyOptions? policy = null) =>
        Router(policy ?? NoPolicy).TestAsync([archive], verifyRecoveryData: true);

    private static Par2CreateResult Protect(string path) =>
        Par2Creator.Create(path, Par2Creator.ChooseParameters(new FileInfo(path).Length, 5)!.Value, null, CancellationToken.None);

    // A real ZIP with one stored entry of noise, so a byte changed in the middle fails its CRC.
    private string MakeZip(string name, int seed)
    {
        string path = Path.Combine(_root, name);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using Stream entry = archive.CreateEntry("noise.bin", CompressionLevel.NoCompression).Open();
        entry.Write(Noise(Length, seed));
        return path;
    }

    // Noise under a gzip header: the set covers bytes, not a format, and no test starts tar.exe.
    private string MakeTarGz(string name, int seed)
    {
        string path = Path.Combine(_root, name);
        byte[] content = Noise(Length, seed);
        new byte[] { 0x1F, 0x8B, 0x08 }.CopyTo(content, 0);
        File.WriteAllBytes(path, content);
        return path;
    }

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
