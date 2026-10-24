using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F388: the New archive card offered formats a policy makes Core refuse.
public sealed class CreationFormatOptionTests
{
    // T-F413: gzip, bzip2 and xz of one file are the CLI's only; the card lists archives.
    private static readonly ArchiveContainerFormat[] All =
        [.. Enum.GetValues<ArchiveContainerFormat>().Where(f => !ArchiveFormatPolicy.IsSingleFileFormat(f))];

    [Theory]
    [InlineData(ArchiveContainerFormat.GZip)]
    [InlineData(ArchiveContainerFormat.BZip2)]
    [InlineData(ArchiveContainerFormat.Xz)]
    public void SingleFileFormats_AreNeverOffered(ArchiveContainerFormat format)
    {
        CreationFormatOption.IsOffered(new GroupPolicyOptions(), format, true).Should().BeFalse();
        CreationFormatOption.ListOrder.Should().NotContain(format);
    }

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

    // T-F399: the probe answers after the card is built. A selection still at the startup default
    // moves to the new first row; one the user changed stays.
    [Fact]
    public void AfterSevenZipProbe_StartupDefaultMovesToSevenZip_WhenZipIsBlocked()
    {
        var policy = new GroupPolicyOptions { BlockedFormats = ["zip"] };
        ArchiveContainerFormat startup = CreationFormatOption.Default(policy, false);

        startup.Should().Be(ArchiveContainerFormat.Tar);
        CreationFormatOption.AfterSevenZipProbe(policy, startup, startup).Should().Be(ArchiveContainerFormat.SevenZip);
    }

    [Fact]
    public void AfterSevenZipProbe_KeepsAFormatTheUserPicked()
    {
        var policy = new GroupPolicyOptions { BlockedFormats = ["zip"] };

        CreationFormatOption.AfterSevenZipProbe(policy, ArchiveContainerFormat.TarGz, ArchiveContainerFormat.Tar)
            .Should().Be(ArchiveContainerFormat.TarGz);
    }

    [Fact]
    public void AfterSevenZipProbe_ZipStaysWhereZipIsOffered()
    {
        CreationFormatOption.AfterSevenZipProbe(new GroupPolicyOptions(), ArchiveContainerFormat.Zip, ArchiveContainerFormat.Zip)
            .Should().Be(ArchiveContainerFormat.Zip);
    }

    // Under a policy that allows only 7z the startup default is ZIP, which Core refuses.
    [Fact]
    public void AfterSevenZipProbe_LeavesAFormatNoLongerOffered()
    {
        var policy = new GroupPolicyOptions { AllowedFormats = ["sevenzip"] };

        CreationFormatOption.AfterSevenZipProbe(policy, ArchiveContainerFormat.Zip, ArchiveContainerFormat.Zip)
            .Should().Be(ArchiveContainerFormat.SevenZip);
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

    // T-F403: the card offers only what this system's tar.exe can make. Windows 10 22H2 and Server
    // 2022 (measured) have a zlib-only tar.exe: tar and tar.gz, no other codec and no 7z.
    [Fact]
    public void ZlibOnlyTar_OffersZipTarAndTarGzOnly()
    {
        TarCapabilities zlibOnly = Archiver.Core.Services.TarVersionParser.Parse("bsdtar 3.5.2 - libarchive 3.5.2 zlib/1.2.5.f-ipp ");

        All.Where(f => CreationFormatOption.IsOffered(new GroupPolicyOptions(), f, true, zlibOnly)).Should().Equal(
            ArchiveContainerFormat.Zip, ArchiveContainerFormat.Tar, ArchiveContainerFormat.TarGz);
        CreationFormatOption.Default(new GroupPolicyOptions { BlockedFormats = ["zip"] }, true, zlibOnly)
            .Should().Be(ArchiveContainerFormat.Tar);
    }

    [Fact]
    public void EveryCodec_OffersEveryFormat()
    {
        TarCapabilities all = Archiver.Core.Services.TarVersionParser.Parse(
            "bsdtar 3.8.8 - libarchive 3.8.8 zlib/1.2.13 liblzma/5.8.1 bz2lib/1.0.8 libzstd/1.5.7");

        All.Should().OnlyContain(f => CreationFormatOption.IsOffered(new GroupPolicyOptions(), f, true, all));
    }
}
