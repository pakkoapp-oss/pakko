using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>
/// T-F388: which formats the "New archive" card lists. The rule is the one
/// <c>ArchiveCreationRouter</c> refuses by, so the card offers nothing Core would turn down.
/// </summary>
public static class CreationFormatOption
{
    /// <summary>Whether the card lists <paramref name="format"/> under <paramref name="policy"/>.</summary>
    public static bool IsOffered(GroupPolicyOptions policy, ArchiveContainerFormat format)
    {
        ArgumentNullException.ThrowIfNull(policy);
        // T-F342: Core creates 7z, but the card has no entry for it until the App wave adds one.
        if (format == ArchiveContainerFormat.SevenZip)
            return false;
        // DisableTarExtraction stops tar.exe in both directions, and only ZIP is written without it.
        return policy.IsFormatAllowed(ArchiveFormatRegistryNames.ToRegistryName(format))
            && (format == ArchiveContainerFormat.Zip || !policy.DisableTarExtraction);
    }

    /// <summary>The format the card starts on: ZIP when it is offered, else the first offered one;
    /// ZIP again when nothing is (Core then refuses the creation and says why).</summary>
    public static ArchiveContainerFormat Default(GroupPolicyOptions policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return Enum.GetValues<ArchiveContainerFormat>()
            .Where(format => IsOffered(policy, format))
            .DefaultIfEmpty(ArchiveContainerFormat.Zip)
            .First();
    }
}
