using System.Runtime.Versioning;
using Archiver.Core.Interfaces;
using Microsoft.Win32;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IRegistryReader"/>
[SupportedOSPlatform("windows")]
public sealed class Win32RegistryReader : IRegistryReader
{
    public int? GetDword(string keyPath, string valueName)
    {
        object? value = ReadValue(keyPath, valueName);
        return value is int i ? i : null;
    }

    public string[]? GetMultiString(string keyPath, string valueName)
    {
        object? value = ReadValue(keyPath, valueName);
        return value as string[];
    }

    private static object? ReadValue(string keyPath, string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(valueName);
        }
        catch
        {
            // Absent key, access denied, or any other registry failure — GroupPolicyService
            // must never throw on a missing/malformed policy, so treat every failure as "no
            // value present" rather than propagating it.
            return null;
        }
    }
}
