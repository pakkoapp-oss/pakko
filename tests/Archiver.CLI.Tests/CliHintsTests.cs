using Archiver.CLI;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F296 item 2 (absorbs T-F293): hints keyed on the cause code of each error and skip.
public sealed class CliHintsTests
{
    private const string OverwriteHint = "existing files were kept";
    private const string PasswordHint = "-p<password>";

    private static IReadOnlyList<string> Hints(
        MessageCode?[] errors, MessageCode?[] skips, bool passwordGiven = false, bool keptExistingByDefault = true) =>
        CliHints.For(errors, skips, passwordGiven, keptExistingByDefault);

    [Fact]
    public void EveryMessageCode_IsClassified()
    {
        CliHints.CauseTable.Keys.Should().BeEquivalentTo(Enum.GetValues<MessageCode>());
    }

    // T-F293's repro: one entry failed its CRC check, so Core also says every entry was skipped.
    [Fact]
    public void EntryFailedThenAllEntriesSkipped_NoOverwriteHint()
    {
        Hints([MessageCode.EntryFailed], [MessageCode.AllEntriesSkipped])
            .Should().NotContain(h => h.Contains(OverwriteHint));
    }

    [Fact]
    public void UnsafeEntryThenAllEntriesSkipped_NoOverwriteHint()
    {
        Hints([], [MessageCode.UnsafeEntryPath, MessageCode.AllEntriesSkipped])
            .Should().NotContain(h => h.Contains(OverwriteHint));
    }

    // ZIP lists no per-file conflict skip: an AllEntriesSkipped with nothing else wrong is a conflict.
    [Fact]
    public void OnlyAllEntriesSkipped_OverwriteHint()
    {
        Hints([], [MessageCode.AllEntriesSkipped]).Should().ContainSingle(h => h.Contains(OverwriteHint));
    }

    [Fact]
    public void FileExistsAtDestination_OverwriteHintEvenNextToOtherProblems()
    {
        Hints([MessageCode.EntryFailed], [MessageCode.FileExistsAtDestination, MessageCode.FileExistsAtDestination])
            .Should().ContainSingle(h => h.Contains(OverwriteHint));
    }

    [Fact]
    public void ConflictChosenOnTheCommandLine_NoOverwriteHint()
    {
        Hints([], [MessageCode.FileExistsAtDestination], keptExistingByDefault: false).Should().BeEmpty();
    }

    [Theory]
    [InlineData(MessageCode.PasswordProtectedExtract)]
    [InlineData(MessageCode.PasswordProtectedTest)]
    public void PasswordProtectedWithoutPassword_PasswordHint(MessageCode code)
    {
        Hints([code], []).Should().ContainSingle(h => h.Contains(PasswordHint));
    }

    // T-F322: tar.exe cannot decrypt a 7z or RAR at all, so -p is no way forward.
    [Fact]
    public void PasswordProtectedFormatPakkoCannotDecrypt_NoPasswordHint()
    {
        Hints([MessageCode.PasswordProtectedFormatNotSupported], []).Should().BeEmpty();
    }

    [Fact]
    public void PasswordProtectedWithPasswordGiven_NoPasswordHint()
    {
        Hints([MessageCode.PasswordProtectedExtract], [], passwordGiven: true).Should().BeEmpty();
    }

    [Fact]
    public void ErrorWithoutCode_NoHint()
    {
        Hints([null], [null]).Should().BeEmpty();
    }

    // T-F275 step 4: `t` says a damaged archive can be repaired; the hint says how.
    [Fact]
    public void DamageTheSetCanRepair_PointsToTheRepairCommand()
    {
        Hints([MessageCode.RecoveryDataDamagedRepairable], []).Should().ContainSingle().Which.Should().Contain("pakko r <archive>");
    }

    [Fact]
    public void RepairedCopyNotWritten_PointsToAnotherFolder()
    {
        Hints([MessageCode.RecoveryRepairNotWritten], []).Should().ContainSingle().Which.Should().Contain("-o<dir>");
    }

    [Theory]
    [InlineData(MessageCode.RecoveryDataDamagedNotRepairable)]
    [InlineData(MessageCode.RecoveryDataRepairTooLarge)]
    [InlineData(MessageCode.RecoveryDataNotFound)]
    [InlineData(MessageCode.RecoveryRepairCheckFailed)]
    [InlineData(MessageCode.RecoveryDataUnusable)]
    public void WhatNoRepairCanFix_HasNoHint(MessageCode code)
    {
        Hints([code], []).Should().BeEmpty();
    }
}
