using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>
/// T-F388: which formats the "New archive" card lists. The rule is the one
/// <c>ArchiveCreationRouter</c> refuses by, so the card offers nothing Core would turn down.
/// </summary>
public static class CreationFormatOption
{
    /// <summary>
    /// The rows of the card's Format list, top to bottom: the list's selected index is a position
    /// here, and <c>MainWindow.xaml</c> keeps its items in this order (a test compares the two).
    /// </summary>
    public static IReadOnlyList<ArchiveContainerFormat> ListOrder { get; } =
    [
        ArchiveContainerFormat.Zip,
        ArchiveContainerFormat.SevenZip,
        ArchiveContainerFormat.Tar,
        ArchiveContainerFormat.TarGz,
        ArchiveContainerFormat.TarBz2,
        ArchiveContainerFormat.TarXz,
        ArchiveContainerFormat.TarZst,
        ArchiveContainerFormat.TarLzma,
    ];

    /// <summary>The format of a list row; ZIP for an index outside the list (nothing selected).</summary>
    public static ArchiveContainerFormat FromIndex(int index) =>
        index >= 0 && index < ListOrder.Count ? ListOrder[index] : ArchiveContainerFormat.Zip;

    /// <summary>The list row of <paramref name="format"/>.</summary>
    public static int ToIndex(ArchiveContainerFormat format)
    {
        for (int index = 0; index < ListOrder.Count; index++)
        {
            if (ListOrder[index] == format)
                return index;
        }
        return 0;
    }

    /// <summary>
    /// Whether the card lists <paramref name="format"/> under <paramref name="policy"/>.
    /// <paramref name="sevenZipWriter"/> is what <c>ITarService.CanCreateSevenZipAsync</c> found
    /// (T-F342): 7z is listed only where this system's tar.exe writes it. T-F403: with
    /// <paramref name="tarCapabilities"/>, a format is listed only where this tar.exe can make it
    /// (<see cref="ArchiveFormatPolicy.GetTarCreationRefusalReason"/>, the rule Core refuses by);
    /// for 7z that asks for the format, the method list then holds the methods it can write.
    /// </summary>
    public static bool IsOffered(
        GroupPolicyOptions policy, ArchiveContainerFormat format, bool sevenZipWriter, TarCapabilities? tarCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (format == ArchiveContainerFormat.SevenZip && !sevenZipWriter)
            return false;
        if (tarCapabilities is not null
            && ArchiveFormatPolicy.GetTarCreationRefusalReason(format, SevenZipMethod.Copy, tarCapabilities) is not null)
        {
            return false;
        }
        // DisableTarExtraction stops tar.exe in both directions, and only ZIP is written without it.
        return policy.IsFormatAllowed(ArchiveFormatRegistryNames.ToRegistryName(format))
            && (format == ArchiveContainerFormat.Zip || !policy.DisableTarExtraction);
    }

    /// <summary>The format the card starts on: ZIP when it is offered, else the first listed one that is;
    /// ZIP again when nothing is (Core then refuses the creation and says why).</summary>
    public static ArchiveContainerFormat Default(
        GroupPolicyOptions policy, bool sevenZipWriter, TarCapabilities? tarCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return ListOrder
            .Where(format => IsOffered(policy, format, sevenZipWriter, tarCapabilities))
            .DefaultIfEmpty(ArchiveContainerFormat.Zip)
            .First();
    }

    /// <summary>
    /// T-F399: the format the card stands on once <c>ITarService.CanCreateSevenZipAsync</c> has
    /// found a 7z writer. <paramref name="startupDefault"/> is what <see cref="Default"/> picked
    /// before that answer: a selection still on it follows the new default, one the user changed
    /// stays unless it is no longer offered.
    /// </summary>
    public static ArchiveContainerFormat AfterSevenZipProbe(
        GroupPolicyOptions policy, ArchiveContainerFormat selected, ArchiveContainerFormat startupDefault,
        TarCapabilities? tarCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return selected == startupDefault || !IsOffered(policy, selected, true, tarCapabilities)
            ? Default(policy, true, tarCapabilities)
            : selected;
    }
}
