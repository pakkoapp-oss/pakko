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
    /// (T-F342): 7z is listed only where this system's tar.exe writes it.
    /// </summary>
    public static bool IsOffered(GroupPolicyOptions policy, ArchiveContainerFormat format, bool sevenZipWriter)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (format == ArchiveContainerFormat.SevenZip && !sevenZipWriter)
            return false;
        // DisableTarExtraction stops tar.exe in both directions, and only ZIP is written without it.
        return policy.IsFormatAllowed(ArchiveFormatRegistryNames.ToRegistryName(format))
            && (format == ArchiveContainerFormat.Zip || !policy.DisableTarExtraction);
    }

    /// <summary>The format the card starts on: ZIP when it is offered, else the first listed one that is;
    /// ZIP again when nothing is (Core then refuses the creation and says why).</summary>
    public static ArchiveContainerFormat Default(GroupPolicyOptions policy, bool sevenZipWriter)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return ListOrder
            .Where(format => IsOffered(policy, format, sevenZipWriter))
            .DefaultIfEmpty(ArchiveContainerFormat.Zip)
            .First();
    }
}
