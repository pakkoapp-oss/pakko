using System.Runtime.Versioning;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Reads Pakko's Group Policy settings (T-F51) from HKLM\Software\Policies\Pakko\. Both
/// Archiver.App (via DI) and Archiver.Shell (no DI container) call Load() once at startup and
/// thread the resulting GroupPolicyOptions into every consumer. Never throws — an absent key, an
/// absent registry hive entirely, or a malformed value all fall back to today's shipped
/// (unrestricted) behavior.
/// </summary>
public static class GroupPolicyService
{
    private const string PolicyKeyPath = @"Software\Policies\Pakko";

    [SupportedOSPlatform("windows")]
    public static GroupPolicyOptions Load() => Load(new Win32RegistryReader());

    public static GroupPolicyOptions Load(IRegistryReader reader)
    {
        MotwMode motwMode = reader.GetDword(PolicyKeyPath, "EnforceMOTW") switch
        {
            0 => MotwMode.Disabled,
            2 => MotwMode.UnsafeExtensionsOnly,
            // Absent key, 1, or any other/malformed value all preserve today's always-on default.
            _ => MotwMode.AllFiles,
        };

        string[]? allowedFormats = reader.GetMultiString(PolicyKeyPath, "AllowedFormats");
        string[]? blockedFormats = reader.GetMultiString(PolicyKeyPath, "BlockedFormats");

        return new GroupPolicyOptions
        {
            MotwMode = motwMode,
            AllowedFormats = allowedFormats is { Length: > 0 } ? allowedFormats : null,
            BlockedFormats = blockedFormats is { Length: > 0 } ? blockedFormats : null,
            DisableTarExtraction = reader.GetDword(PolicyKeyPath, "DisableTarExtraction") == 1,
        };
    }
}
