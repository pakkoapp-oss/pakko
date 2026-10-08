using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>The text under the download-mark checkbox.</summary>
public enum DownloadMarkNote
{
    /// <summary>The checkbox is hidden.</summary>
    None,

    /// <summary>The mark is on: what it costs for many small files.</summary>
    Cost,

    /// <summary>The user left the mark off: what that risks.</summary>
    Risk,

    /// <summary>Group Policy decides; the checkbox is locked.</summary>
    Policy,
}

/// <summary>What the download-mark checkbox shows (T-F360).</summary>
/// <param name="Visible">Shown at all.</param>
/// <param name="CanChange">The user may click it.</param>
/// <param name="Checked">Its tick.</param>
/// <param name="Note">The text under it.</param>
public readonly record struct DownloadMarkView(bool Visible, bool CanChange, bool Checked, DownloadMarkNote Note)
{
    /// <summary>
    /// What <see cref="ExtractOptions.ApplyDownloadMark"/> gets: a hidden checkbox is checked, so it
    /// keeps the mark. Core applies the policy itself, so a locked one needs nothing more.
    /// </summary>
    public bool ApplyMark => Checked;
}

/// <summary>
/// T-F360: the extract wizard offers to leave an archive's download mark off the extracted files.
/// Shown only when extraction is the action and an archive carries the mark; an EnforceMOTW Group
/// Policy locks it to the policy's mode.
/// </summary>
public static class DownloadMarkOption
{
    /// <summary>The checkbox for the current list and the user's tick.</summary>
    public static DownloadMarkView For(bool extractOffered, bool anyArchiveMarked, GroupPolicyOptions policy, bool userWantsMark)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!extractOffered || !anyArchiveMarked)
            return new DownloadMarkView(false, false, true, DownloadMarkNote.None);
        if (policy.MotwModeSetByPolicy)
            return new DownloadMarkView(true, false, policy.MotwMode != MotwMode.Disabled, DownloadMarkNote.Policy);
        return new DownloadMarkView(true, true, userWantsMark, userWantsMark ? DownloadMarkNote.Cost : DownloadMarkNote.Risk);
    }
}
