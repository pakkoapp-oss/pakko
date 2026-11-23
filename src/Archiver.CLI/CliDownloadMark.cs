using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>T-F360: what pakko says when Group Policy overrides -snz; T-F496: or forbids -kb.</summary>
public static class CliDownloadMark
{
    /// <summary>A stderr line when an EnforceMOTW policy gives another mark than -snz asked for; otherwise null.</summary>
    public static string? PolicyOverrideWarning(bool? requested, GroupPolicyOptions policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (requested is not { } apply || !policy.MotwModeSetByPolicy)
            return null;
        MotwMode asked = apply ? MotwMode.AllFiles : MotwMode.Disabled;
        if (policy.MotwMode == asked)
            return null;
        string given = apply ? "the download mark on every file" : "no download mark";
        string effect = policy.MotwMode switch
        {
            MotwMode.Disabled => "no extracted file gets the mark",
            MotwMode.UnsafeExtensionsOnly => "only files of unsafe types get the mark",
            _ => "every extracted file gets the mark",
        };
        return $"pakko: warning: -snz asked for {given}, but Group Policy (EnforceMOTW) decides: {effect}";
    }

    /// <summary>The warnings for `x` where Group Policy overrides what the switches asked for.</summary>
    public static IEnumerable<string> ExtractPolicyWarnings(bool? applyDownloadMark, bool keepDamaged, GroupPolicyOptions policy) =>
        new[] { PolicyOverrideWarning(applyDownloadMark, policy), KeepDamagedForbiddenWarning(keepDamaged, policy) }.OfType<string>();

    /// <summary>A stderr line when -kb was given and the DisableKeepDamagedFiles policy forbids it; otherwise null.</summary>
    public static string? KeepDamagedForbiddenWarning(bool requested, GroupPolicyOptions policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return requested && policy.DisableKeepDamagedFiles
            ? "pakko: warning: -kb asked to keep damaged files, but Group Policy (DisableKeepDamagedFiles) forbids it: a damaged file leaves nothing"
            : null;
    }
}
