namespace Archiver.CLI;

/// <summary>
/// T-F222: the <c>pakko -v</c> line, from the assembly's informational version. A release build
/// (Publish-Cli.ps1 -Version X.Y.Z) prints exactly <c>pakko X.Y.Z</c>; every other build carries
/// Archiver.CLI.csproj's <c>-dev</c> suffix plus the commit, so it can never pass for a release.
/// </summary>
public static class CliVersionText
{
    private const int ShortShaLength = 7;

    /// <summary>Formats <paramref name="informationalVersion"/> ("X.Y.Z[-pre][+sha]").</summary>
    public static string Format(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion))
            return "pakko 0.0.0";

        int plus = informationalVersion.IndexOf('+');
        string core = plus < 0 ? informationalVersion : informationalVersion[..plus];
        if (plus < 0 || !core.Contains('-'))
            return $"pakko {core}";

        string metadata = informationalVersion[(plus + 1)..];
        if (metadata.Length == 0)
            return $"pakko {core}";
        return $"pakko {core}+{metadata[..Math.Min(ShortShaLength, metadata.Length)]}";
    }
}
