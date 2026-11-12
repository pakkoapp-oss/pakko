using System.Collections.Frozen;
using System.Runtime.Versioning;
using System.Security;
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
        catch // Any registry failure is "no provider", the Inconclusive-leaning direction
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
            return key is null ? AmsiProviderState.None : Classify(key.GetSubKeyNames(), IsLoadable);
        }
        catch // Any registry failure is "no provider", the Inconclusive-leaning direction
        {
            return AmsiProviderState.None;
        }
    }

    // Without a listed provider every subkey counts, as before T-F448. Beside a listed one, another
    // subkey counts only if amsi.dll could load it: a key left behind by an uninstaller, with its
    // DLL gone, would otherwise turn the listed provider's Clean back into a verdict.
    internal static AmsiProviderState Classify(IReadOnlyCollection<string> subKeyNames, Func<Guid, bool> isLoadable)
    {
        if (subKeyNames.Count == 0)
            return AmsiProviderState.None;
        var others = new List<string>();
        bool anyListed = false;
        foreach (string name in subKeyNames)
        {
            if (Guid.TryParse(name, out Guid clsid) && NonReportingProviders.Contains(clsid))
                anyListed = true;
            else
                others.Add(name);
        }
        if (!anyListed)
            return AmsiProviderState.Usable;
        foreach (string name in others)
        {
            if (Guid.TryParse(name, out Guid clsid) && isLoadable(clsid))
                return AmsiProviderState.Usable;
        }
        return AmsiProviderState.OnlyNonReporting;
    }

    // HKLM's own Classes, never Registry.ClassesRoot: that view merges HKCU, where any process of
    // the user could plant a CLSID pointing at an existing file and undo the check above.
    [SupportedOSPlatform("windows")]
    internal static bool IsLoadable(Guid clsid)
    {
        try
        {
            using RegistryKey? server = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\CLSID\{clsid:B}\InprocServer32");
            if (server?.GetValue(null) is not string path)
                return false;
            path = path.Trim().Trim('"');
            return Path.IsPathFullyQualified(path) && File.Exists(path);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
