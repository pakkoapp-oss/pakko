using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Archiver.Core.Services.Antivirus;

/// <summary>
/// T-F146. AmsiScanBuffer alone cannot distinguish "no AV is registered as an AMSI provider" from
/// "a provider is registered and says clean" — both plausibly return AMSI_RESULT_NOT_DETECTED, and
/// rendering the former as Clean would silently violate the Inconclusive-must-never-become-Clean
/// rule (docs/DECISIONS.md's T-F146 entry). Confirmed empirically (same entry): this registry key
/// has one subkey per registered provider (Defender's own GUID on a normal dev machine) and is
/// readable non-elevated via the same Microsoft.Win32.Registry access already established for
/// Group Policy (T-F51, Win32RegistryReader) — zero new NuGet package reference.
/// </summary>
public static class AmsiProviderCheck
{
    private const string ProvidersKeyPath = @"SOFTWARE\Microsoft\AMSI\Providers";

    /// <summary>
    /// True if at least one AMSI provider is registered. Never throws — a missing key, a missing
    /// hive, or an access-denied read are all treated as "no provider" (the safe, Inconclusive-
    /// leaning direction), matching GroupPolicyService.Load's own "never throws" discipline.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsAnyProviderRegistered()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ProvidersKeyPath);
            return key is not null && key.SubKeyCount > 0;
        }
        catch
        {
            return false;
        }
    }
}
