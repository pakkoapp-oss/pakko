using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>
/// T-F275: the "New archive" card's option to write PAR2 recovery data next to the archive —
/// 5, 10 or 20 % (default 5), hidden by the DisableRecoveryData policy.
/// </summary>
public static class RecoveryDataOption
{
    /// <summary>The choices the card offers, in list order.</summary>
    public static IReadOnlyList<int> Percents { get; } = [5, 10, 20];

    /// <summary>The choice selected until the user picks another.</summary>
    public const int DefaultIndex = 0;

    /// <summary>Whether the card shows the option at all.</summary>
    public static bool IsOffered(GroupPolicyOptions policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return !policy.DisableRecoveryData;
    }

    /// <summary>What <see cref="ArchiveOptions.RecoveryPercent"/> gets: 0 when unticked or not offered.</summary>
    public static int PercentFor(GroupPolicyOptions policy, bool add, int index)
    {
        if (!add || !IsOffered(policy))
            return 0;
        return index >= 0 && index < Percents.Count ? Percents[index] : Percents[DefaultIndex];
    }

    /// <summary>A choice as the culture writes a percent ("5%", "5 %", "%5").</summary>
    public static string PercentText(int percent, CultureInfo culture) =>
        (percent / 100.0).ToString("P0", culture);
}
