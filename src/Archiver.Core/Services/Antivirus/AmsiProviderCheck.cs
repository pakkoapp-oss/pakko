using System.Collections.Frozen;
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

    // T-F447/T-F448: providers measured to answer S_OK with AMSI_RESULT_CLEAN (0) for EICAR and
    // for Microsoft's AMSI test sample, with protection on. Only the CLSIDs measured go here.
    private static readonly FrozenSet<Guid> NonReportingProviders =
    [
        // Trend Micro Maximum Security 17.9 (TmAMSIProvider64.dll), PakkoW10, 2026-11-04.
        new Guid("6138A34F-DCA4-48D0-95CC-51E1D8F30B58"),
    ];

    /// <summary>
    /// The registered providers' state for a scan. Never throws: a failed read is
    /// <see cref="AmsiProviderState.None"/>, as in <see cref="IsAnyProviderRegistered"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static AmsiProviderState GetState()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ProvidersKeyPath);
            return key is null ? AmsiProviderState.None : Classify(key.GetSubKeyNames());
        }
        catch
        {
            return AmsiProviderState.None;
        }
    }

    // A subkey name that is no GUID is an unknown provider, taken as Usable like any other.
    internal static AmsiProviderState Classify(IReadOnlyCollection<string> subKeyNames)
    {
        if (subKeyNames.Count == 0)
            return AmsiProviderState.None;
        foreach (string name in subKeyNames)
        {
            if (!Guid.TryParse(name, out Guid clsid) || !NonReportingProviders.Contains(clsid))
                return AmsiProviderState.Usable;
        }
        return AmsiProviderState.OnlyNonReporting;
    }
}
