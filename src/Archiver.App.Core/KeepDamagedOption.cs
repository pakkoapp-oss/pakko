using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>
/// T-F496: the extract card's "keep what a damaged file holds" checkbox. Shown only where
/// extraction is the action and no Group Policy forbids it; it starts unchecked for every new list
/// or archive, is cleared once an extraction has read it, and is never saved.
/// </summary>
public static class KeepDamagedOption
{
    /// <summary>Whether the checkbox is shown.</summary>
    public static bool Offered(bool extractOffered, GroupPolicyOptions policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return extractOffered && !policy.DisableKeepDamagedFiles;
    }

    /// <summary>What <see cref="ExtractOptions.KeepDamagedFiles"/> gets: never from a hidden
    /// checkbox, and never for the one file the unsafe-type warning extracts to open it.</summary>
    public static bool Requested(bool offered, bool userChecked, bool openingOneFile) =>
        offered && userChecked && !openingOneFile;
}
