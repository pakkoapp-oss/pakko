namespace Archiver.Core.Models;

/// <summary>
/// Effective Group Policy settings for Pakko (T-F51), read from
/// HKLM\Software\Policies\Pakko\ via GroupPolicyService.Load(). A parameterless instance
/// reproduces today's shipped behavior exactly (MOTW propagated to all files, no format
/// restriction, tar extraction enabled) — this is the default every service falls back to when
/// no GroupPolicyOptions is supplied.
/// </summary>
public sealed record GroupPolicyOptions
{
    public MotwMode MotwMode { get; init; } = MotwMode.AllFiles;
    public IReadOnlyList<string>? AllowedFormats { get; init; }
    public IReadOnlyList<string>? BlockedFormats { get; init; }
    public bool DisableTarExtraction { get; init; }

    /// <summary>
    /// True if the given format (an ArchiveFormatRegistryNames name, e.g. "zip") is permitted.
    /// BlockedFormats always takes precedence over AllowedFormats (matches NanaZip's
    /// AllowedHandlers/BlockedHandlers). Absent lists impose no restriction.
    /// </summary>
    public bool IsFormatAllowed(string registryName)
    {
        if (BlockedFormats is { Count: > 0 } blocked &&
            blocked.Contains(registryName, StringComparer.OrdinalIgnoreCase))
            return false;

        if (AllowedFormats is { Count: > 0 } allowed &&
            !allowed.Contains(registryName, StringComparer.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
