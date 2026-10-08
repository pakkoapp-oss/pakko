using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>T-F360: what pakko says when Group Policy overrides -snz.</summary>
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
}
