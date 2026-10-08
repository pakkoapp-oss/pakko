using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F360: the extract wizard's "apply the download mark" checkbox.
public sealed class DownloadMarkOptionTests
{
    private static readonly GroupPolicyOptions NoPolicy = new();

    [Fact]
    public void MarkedArchive_NoPolicy_CheckedWithTheCostNote()
    {
        DownloadMarkView view = DownloadMarkOption.For(extractOffered: true, anyArchiveMarked: true, NoPolicy, userWantsMark: true);

        view.Should().Be(new DownloadMarkView(Visible: true, CanChange: true, Checked: true, DownloadMarkNote.Cost));
        view.ApplyMark.Should().BeTrue();
    }

    [Fact]
    public void UserUnchecks_RiskNoteAndNoMark()
    {
        DownloadMarkView view = DownloadMarkOption.For(true, true, NoPolicy, userWantsMark: false);

        view.Should().Be(new DownloadMarkView(true, true, false, DownloadMarkNote.Risk));
        view.ApplyMark.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void NothingToMarkOrNoExtract_HiddenAndTheMarkStays(bool extractOffered, bool anyArchiveMarked)
    {
        DownloadMarkView view = DownloadMarkOption.For(extractOffered, anyArchiveMarked, NoPolicy, userWantsMark: false);

        view.Visible.Should().BeFalse();
        view.Note.Should().Be(DownloadMarkNote.None);
        view.ApplyMark.Should().BeTrue("a hidden checkbox cannot have been unchecked on purpose");
    }

    [Theory]
    [InlineData(MotwMode.AllFiles, true)]
    [InlineData(MotwMode.UnsafeExtensionsOnly, true)]
    [InlineData(MotwMode.Disabled, false)]
    public void PolicySet_LockedShowsThePolicyAndIgnoresTheUser(MotwMode mode, bool expectChecked)
    {
        var policy = new GroupPolicyOptions { MotwMode = mode, MotwModeSetByPolicy = true };

        foreach (bool userWantsMark in new[] { true, false })
        {
            DownloadMarkView view = DownloadMarkOption.For(true, true, policy, userWantsMark);

            view.Should().Be(new DownloadMarkView(true, CanChange: false, expectChecked, DownloadMarkNote.Policy));
        }
    }

    [Fact]
    public void PolicyNotSet_ModeFromTheDefault_UserDecides()
    {
        // A parameterless policy has MotwMode AllFiles but no administrator chose it.
        DownloadMarkOption.For(true, true, NoPolicy, userWantsMark: false).CanChange.Should().BeTrue();
    }
}
