using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F192: MapResult is the pure, testable seam of PasswordDialog -- the DialogBoxIndirectParamW
// P/Invoke body itself isn't unit-testable (real native UI, same precedent as ShellConflictDialog
// and NativeProgressDialog -- see CLAUDE.md's "Known test gaps" section).
public sealed class PasswordDialogTests
{
    private const int IdOk = 1;
    private const int IdCancel = 2;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapResult_OkButton_ReturnsPasswordWithApplyToRemainingPassedThrough(bool applyChecked)
    {
        var decision = PasswordDialog.MapResult(IdOk, "hunter2", applyChecked);

        decision.Password.Should().Be("hunter2");
        decision.ApplyToRemaining.Should().Be(applyChecked);
    }

    [Fact]
    public void MapResult_OkButtonWithEmptyPassword_ReturnsEmptyStringNotNull()
    {
        // An empty password is a real (if unusual) user choice, distinct from Cancel -- must not
        // collapse to the same "no resolver" null the caller uses for a declined/missing prompt.
        var decision = PasswordDialog.MapResult(IdOk, string.Empty, applyToRemainingChecked: false);

        decision.Password.Should().Be(string.Empty);
    }

    [Fact]
    public void MapResult_CancelButton_ReturnsNullPassword()
    {
        var decision = PasswordDialog.MapResult(IdCancel, "typed-before-cancel", applyToRemainingChecked: true);

        decision.Password.Should().BeNull();
    }

    [Fact]
    public void MapResult_UnrecognizedButtonId_ReturnsNullPassword()
    {
        // Esc/Alt-F4 with a modal dialog can surface as an unexpected id depending on how the
        // message loop unwinds -- must fail safe (null) exactly like an explicit Cancel, mirroring
        // ShellConflictDialog.MapResult's identical "unrecognized -> safe default" convention.
        var decision = PasswordDialog.MapResult(99999, "irrelevant", applyToRemainingChecked: false);

        decision.Password.Should().BeNull();
    }
}
