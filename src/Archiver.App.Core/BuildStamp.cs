using System.Globalization;
using System.Reflection;

namespace Archiver.App.Core;

/// <summary>
/// The window title's build stamp — the proof every on-device check starts from that the running
/// binary is the one just built (CLAUDE.md). T-F218: the compile time from the assembly's
/// <c>PakkoBuildTimeUtc</c> metadata, not the packaged DLL's file time (the MSIX install time).
/// T-F198 item 4: a Store build shows no stamp.
/// </summary>
public static class BuildStamp
{
    /// <summary>The metadata key Archiver.App.csproj writes the compile time under.</summary>
    public const string MetadataKey = "PakkoBuildTimeUtc";

    /// <summary>The window title: "Pakko", plus the local build time unless this is a Store build.</summary>
    public static string Title(string? buildTimeUtc, bool isStoreBuild)
    {
        if (isStoreBuild
            || !DateTime.TryParse(buildTimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime utc))
            return "Pakko";
        string local = utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return $"Pakko — build {local}";
    }

    /// <summary>Reads <see cref="MetadataKey"/> from <paramref name="assembly"/>, or null.</summary>
    public static string? Read(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == MetadataKey)?.Value;
}
