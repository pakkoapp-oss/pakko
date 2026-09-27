namespace Archiver.Core.Models;

/// <summary>
/// Effective Group Policy settings for Pakko (T-F51), read from
/// HKLM\Software\Policies\Pakko\ via GroupPolicyService.Load(). A parameterless instance means
/// no policy is set (MOTW propagated to all files, no format restriction, tar extraction
/// enabled). T-F261: every engine and router requires an instance — none falls back to this
/// default on its own.
/// </summary>
public sealed record GroupPolicyOptions
{
    /// <summary>Whether the Zone.Identifier ADS gets propagated to extracted files, and for which ones.</summary>
    public MotwMode MotwMode { get; init; } = MotwMode.AllFiles;

    /// <summary>ArchiveFormatRegistryNames names permitted for extraction/creation. Null/empty imposes no restriction.</summary>
    public IReadOnlyList<string>? AllowedFormats { get; init; }

    /// <summary>ArchiveFormatRegistryNames names denied for extraction/creation. Takes precedence over <see cref="AllowedFormats"/>.</summary>
    public IReadOnlyList<string>? BlockedFormats { get; init; }

    /// <summary>Disables tar-family (tar.exe-backed) extraction entirely when true.</summary>
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
