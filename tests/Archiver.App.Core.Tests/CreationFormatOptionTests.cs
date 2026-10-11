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
        All.Should().OnlyContain(f => CreationFormatOption.IsOffered(new GroupPolicyOptions(), f));
        CreationFormatOption.Default(new GroupPolicyOptions()).Should().Be(ArchiveContainerFormat.Zip);
    }

    [Fact]
    public void AllowedFormatsZip_OffersZipOnly()
    {
        var policy = new GroupPolicyOptions { AllowedFormats = ["zip"] };

        All.Where(f => CreationFormatOption.IsOffered(policy, f)).Should().Equal(ArchiveContainerFormat.Zip);
    }

    [Fact]
    public void BlockedFormats_HideExactlyTheBlockedOnes()
    {
        var policy = new GroupPolicyOptions { BlockedFormats = ["GZIP", "xz"] };

        All.Where(f => !CreationFormatOption.IsOffered(policy, f))
            .Should().BeEquivalentTo([ArchiveContainerFormat.TarGz, ArchiveContainerFormat.TarXz]);
    }

    [Fact]
    public void DisableTarExtraction_LeavesZipOnly()
    {
        var policy = new GroupPolicyOptions { DisableTarExtraction = true };

        All.Where(f => CreationFormatOption.IsOffered(policy, f)).Should().Equal(ArchiveContainerFormat.Zip);
    }

    // The card must not start on a format it does not list.
    [Fact]
    public void ZipBlocked_DefaultIsTheFirstOfferedFormat()
    {
        var policy = new GroupPolicyOptions { BlockedFormats = ["zip", "tar"] };

        CreationFormatOption.Default(policy).Should().Be(ArchiveContainerFormat.TarGz);
    }

    [Fact]
    public void NothingOffered_DefaultStaysZip_AndCoreRefusesIt()
    {
        var policy = new GroupPolicyOptions { BlockedFormats = ["zip"], DisableTarExtraction = true };

        All.Should().NotContain(f => CreationFormatOption.IsOffered(policy, f));
        CreationFormatOption.Default(policy).Should().Be(ArchiveContainerFormat.Zip);
    }

    // What the card offers is what ArchiveCreationRouter accepts: same rule, both sides.
    [Fact]
    public void Offered_AgreesWithThePolicysOwnAnswer()
    {
        var policy = new GroupPolicyOptions { AllowedFormats = ["zip", "tar", "zstd"], BlockedFormats = ["tar"] };

        foreach (ArchiveContainerFormat format in All)
            CreationFormatOption.IsOffered(policy, format).Should().Be(
                policy.IsFormatAllowed(Archiver.Core.Services.ArchiveFormatRegistryNames.ToRegistryName(format)), format.ToString());
    }
}
