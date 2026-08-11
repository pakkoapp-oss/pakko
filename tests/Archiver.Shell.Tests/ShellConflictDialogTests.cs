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
        var decision = ShellConflictDialog.MapResult(IdOverwrite, applyToAll);

        decision.Resolution.Should().Be(ConflictResolution.Overwrite);
        decision.ApplyToAll.Should().Be(applyToAll);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapResult_RenameButton_ReturnsRenameWithApplyToAllPassedThrough(bool applyToAll)
    {
        var decision = ShellConflictDialog.MapResult(IdRename, applyToAll);

        decision.Resolution.Should().Be(ConflictResolution.Rename);
        decision.ApplyToAll.Should().Be(applyToAll);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapResult_SkipButton_ReturnsSkipWithApplyToAllPassedThrough(bool applyToAll)
    {
        var decision = ShellConflictDialog.MapResult(IdSkip, applyToAll);

        decision.Resolution.Should().Be(ConflictResolution.Skip);
        decision.ApplyToAll.Should().Be(applyToAll);
    }

    [Fact]
    public void MapResult_IdCancel_ReturnsSkip()
    {
        // Esc/Alt-F4 with TDF_ALLOW_DIALOG_CANCELLATION set -- must not be treated as Overwrite.
        var decision = ShellConflictDialog.MapResult(IdCancel, applyToAllChecked: false);

        decision.Resolution.Should().Be(ConflictResolution.Skip);
    }

    [Fact]
    public void MapResult_UnrecognizedButtonId_ReturnsSkip()
    {
        var decision = ShellConflictDialog.MapResult(buttonId: 99999, applyToAllChecked: false);

        decision.Resolution.Should().Be(ConflictResolution.Skip);
    }
}
