using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F155: MapResult is the pure, testable seam of ShellConflictDialog -- the TaskDialogIndirect
// P/Invoke body itself isn't unit-testable (real native UI, matches this project's existing
// precedent for NativeProgressDialog -- see CLAUDE.md's "Known test gaps" section).
public sealed class ShellConflictDialogTests
{
    private const int IdOverwrite = 1001;
    private const int IdRename = 1002;
    private const int IdSkip = 1003;
    private const int IdCancel = 2; // returned by TaskDialogIndirect on Esc/Alt-F4

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapResult_OverwriteButton_ReturnsOverwriteWithApplyToAllPassedThrough(bool applyToAll)
    {
        ConflictDecision decision = ShellConflictDialog.MapResult(IdOverwrite, applyToAll);

        decision.Resolution.Should().Be(ConflictResolution.Overwrite);
        decision.ApplyToAll.Should().Be(applyToAll);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapResult_RenameButton_ReturnsRenameWithApplyToAllPassedThrough(bool applyToAll)
    {
        ConflictDecision decision = ShellConflictDialog.MapResult(IdRename, applyToAll);

        decision.Resolution.Should().Be(ConflictResolution.Rename);
        decision.ApplyToAll.Should().Be(applyToAll);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapResult_SkipButton_ReturnsSkipWithApplyToAllPassedThrough(bool applyToAll)
    {
        ConflictDecision decision = ShellConflictDialog.MapResult(IdSkip, applyToAll);

        decision.Resolution.Should().Be(ConflictResolution.Skip);
        decision.ApplyToAll.Should().Be(applyToAll);
    }

    [Fact]
    public void MapResult_IdCancel_ReturnsSkip()
    {
        // Esc/Alt-F4 with TDF_ALLOW_DIALOG_CANCELLATION set -- must not be treated as Overwrite.
        ConflictDecision decision = ShellConflictDialog.MapResult(IdCancel, applyToAllChecked: false);

        decision.Resolution.Should().Be(ConflictResolution.Skip);
    }

    [Fact]
    public void MapResult_UnrecognizedButtonId_ReturnsSkip()
    {
        ConflictDecision decision = ShellConflictDialog.MapResult(buttonId: 99999, applyToAllChecked: false);

        decision.Resolution.Should().Be(ConflictResolution.Skip);
    }

    // T-F253: the message named only the file, so same-named files in several folders could not
    // be told apart, and it gave no size or date to decide with.
    [Fact]
    public void BuildContent_NamesTheFullPathAndBothFilesDetails()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pakko-tf253-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        string existing = Path.Combine(dir, "sub", "a.txt");
        File.WriteAllText(existing, "12345");
        File.SetLastWriteTimeUtc(existing, new DateTime(2020, 1, 2, 3, 4, 0, DateTimeKind.Utc));
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var info = new ConflictInfo
            {
                ExistingPath = existing,
                IncomingSize = 2048,
                IncomingModified = new DateTimeOffset(2024, 5, 6, 7, 8, 0, TimeSpan.Zero),
            };

            string content = ShellConflictDialog.BuildContent(info);

            string[] lines = content.Split(Environment.NewLine);
            lines[0].Should().Be(existing);
            content.Should().Contain("Existing file: 5 B").And.Contain("From the archive: 2 KB").And.Contain("newer");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = original;
            Directory.Delete(dir, recursive: true);
        }
    }
}
