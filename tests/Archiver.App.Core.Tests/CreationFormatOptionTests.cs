using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F388: the New archive card offered formats a policy makes Core refuse.
public sealed class CreationFormatOptionTests
{
    private static readonly ArchiveContainerFormat[] All = Enum.GetValues<ArchiveContainerFormat>();

    [Fact]
    public void NoPolicy_OffersEveryFormat()
    {
        All.Should().OnlyContain(f => CreationFormatOption.IsOffered(new GroupPolicyOptions(), f, true));
        CreationFormatOption.Default(new GroupPolicyOptions(), true).Should().Be(ArchiveContainerFormat.Zip);
    }

    // T-F342: 7z is listed only where this system's tar.exe writes it.
    [Fact]
    public void NoSevenZipWriter_OffersEveryFormatButSevenZip()
    {
        All.Where(f => !CreationFormatOption.IsOffered(new GroupPolicyOptions(), f, false))
            .Should().Equal(ArchiveContainerFormat.SevenZip);
    }

    [Fact]
    public void SevenZip_FollowsThePolicyLikeTheTarFamily()
    {
        CreationFormatOption.IsOffered(new GroupPolicyOptions { BlockedFormats = ["SevenZip"] }, ArchiveContainerFormat.SevenZip, true)
            .Should().BeFalse();
        CreationFormatOption.IsOffered(new GroupPolicyOptions { AllowedFormats = ["zip"] }, ArchiveContainerFormat.SevenZip, true)
            .Should().BeFalse();
        CreationFormatOption.IsOffered(new GroupPolicyOptions { DisableTarExtraction = true }, ArchiveContainerFormat.SevenZip, true)
            .Should().BeFalse();
        CreationFormatOption.IsOffered(new GroupPolicyOptions { AllowedFormats = ["sevenzip"] }, ArchiveContainerFormat.SevenZip, true)
            .Should().BeTrue();
    }

    // The card starts on ZIP before the probe answers; once it has, 7z is the one format left.
    [Fact]
    public void OnlySevenZipAllowed_DefaultFollowsTheWriter()
    {
        var policy = new GroupPolicyOptions { AllowedFormats = ["sevenzip"] };

        CreationFormatOption.Default(policy, false).Should().Be(ArchiveContainerFormat.Zip);
        CreationFormatOption.Default(policy, true).Should().Be(ArchiveContainerFormat.SevenZip);
    }

    [Fact]
    public void ZipOffered_DefaultIsNeverSevenZip()
    {
        CreationFormatOption.Default(new GroupPolicyOptions { BlockedFormats = ["tar"] }, true)
            .Should().Be(ArchiveContainerFormat.Zip);
    }

    [Fact]
    public void AllowedFormatsZip_OffersZipOnly()
    {
        var policy = new GroupPolicyOptions { AllowedFormats = ["zip"] };

        All.Where(f => CreationFormatOption.IsOffered(policy, f, true)).Should().Equal(ArchiveContainerFormat.Zip);
    }

    [Fact]
    public void BlockedFormats_HideExactlyTheBlockedOnes()
    {
        var policy = new GroupPolicyOptions { BlockedFormats = ["GZIP", "xz"] };

        All.Where(f => !CreationFormatOption.IsOffered(policy, f, true))
            .Should().BeEquivalentTo([ArchiveContainerFormat.TarGz, ArchiveContainerFormat.TarXz]);
    }

    [Fact]
    public void DisableTarExtraction_LeavesZipOnly()
    {
        var policy = new GroupPolicyOptions { DisableTarExtraction = true };

        All.Where(f => CreationFormatOption.IsOffered(policy, f, true)).Should().Equal(ArchiveContainerFormat.Zip);
    }

    // The card must not start on a format it does not list.
    [Fact]
    public void ZipBlocked_DefaultIsTheFirstOfferedFormat()
    {
        var policy = new GroupPolicyOptions { BlockedFormats = ["zip", "tar"] };

        CreationFormatOption.Default(policy, false).Should().Be(ArchiveContainerFormat.TarGz);
        CreationFormatOption.Default(policy, true).Should().Be(ArchiveContainerFormat.SevenZip, "7z is listed before the tar family");
    }

    // The maintainer's order (2026-10-12): 7z right after ZIP, the tar family under them.
    [Fact]
    public void ListOrder_IsZipThenSevenZipThenTheTarFamily()
    {
        CreationFormatOption.ListOrder.Should().Equal(
            ArchiveContainerFormat.Zip, ArchiveContainerFormat.SevenZip, ArchiveContainerFormat.Tar,
            ArchiveContainerFormat.TarGz, ArchiveContainerFormat.TarBz2, ArchiveContainerFormat.TarXz,
            ArchiveContainerFormat.TarZst, ArchiveContainerFormat.TarLzma);
        CreationFormatOption.ListOrder.Should().BeEquivalentTo(All, "every format has a row");
    }

    [Fact]
    public void IndexAndFormat_RoundTripInListOrder()
    {
        for (int index = 0; index < CreationFormatOption.ListOrder.Count; index++)
        {
            CreationFormatOption.FromIndex(index).Should().Be(CreationFormatOption.ListOrder[index]);
            CreationFormatOption.ToIndex(CreationFormatOption.ListOrder[index]).Should().Be(index);
        }
    }

    // A ComboBox with nothing selected reports -1.
    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(int.MaxValue)]
    public void FromIndex_OutsideTheList_IsZip(int index)
    {
        CreationFormatOption.FromIndex(index).Should().Be(ArchiveContainerFormat.Zip);
    }

    [Fact]
    public void NothingOffered_DefaultStaysZip_AndCoreRefusesIt()
    {
        var policy = new GroupPolicyOptions { BlockedFormats = ["zip"], DisableTarExtraction = true };

        All.Should().NotContain(f => CreationFormatOption.IsOffered(policy, f, true));
        CreationFormatOption.Default(policy, true).Should().Be(ArchiveContainerFormat.Zip);
    }

    // What the card offers is what ArchiveCreationRouter accepts: same rule, both sides.
    [Fact]
    public void Offered_AgreesWithThePolicysOwnAnswer()
    {
        var policy = new GroupPolicyOptions { AllowedFormats = ["zip", "tar", "zstd"], BlockedFormats = ["tar"] };

        foreach (ArchiveContainerFormat format in All)
            CreationFormatOption.IsOffered(policy, format, true).Should().Be(
                policy.IsFormatAllowed(Archiver.Core.Services.ArchiveFormatRegistryNames.ToRegistryName(format)), format.ToString());
    }
}
