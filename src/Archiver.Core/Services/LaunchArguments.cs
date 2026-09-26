using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Archiver.Core.Services;

/// <summary>What Archiver.App should do with the files handed to it by Archiver.Shell (T-F232).</summary>
public enum LaunchOperation
{
    /// <summary>Open the Archive Browser on a single archive.</summary>
    Browse,

    /// <summary>Pre-fill the pending list for extraction.</summary>
    Extract,

    /// <summary>Pre-fill the pending list for archive creation.</summary>
    Archive,
}

/// <summary>
/// The launch-argument string Archiver.Shell passes to Archiver.App through
/// <c>IApplicationActivationManager::ActivateApplication</c> — the single owner of both sides of the
/// format, so the producer and the consumer cannot drift apart. Replaced the <c>pakko://</c> URI
/// scheme (T-F232): a registered scheme could be launched by any web page or document link, while an
/// activation argument can only come from a process already running on the machine.
/// Shape: <c>--browse|--extract|--archive &lt;base64 of a UTF-8 JSON string array&gt;</c> — base64
/// keeps paths with spaces, quotes or a trailing backslash out of command-line quoting rules.
/// </summary>
public static class LaunchArguments
{
    /// <summary>
    /// Longest argument string Archiver.Shell may pass. Measured 2026-09-26: 32000 characters
    /// activate normally, while a string past the 32767-character command-line limit makes
    /// <c>ActivateApplication</c> block forever instead of failing.
    /// </summary>
    public const int MaxLength = 32000;

    // The default encoder writes every non-ASCII character as \uXXXX (6 bytes where UTF-8 needs 2
    // for Cyrillic), cutting a non-Latin selection's capacity roughly threefold.
    private static readonly JsonSerializerOptions PayloadOptions =
        new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    /// <summary>Builds the argument string for <paramref name="operation"/> on <paramref name="files"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="files"/> is empty.</exception>
    public static string Format(LaunchOperation operation, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            throw new ArgumentException("At least one file is required.", nameof(files));

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(files, PayloadOptions)));
        return $"{SwitchFor(operation)} {payload}";
    }

    /// <summary>
    /// Parses an argument string produced by <see cref="Format"/>. Never throws: anything else — an
    /// empty string (a plain Start-menu launch), an unknown switch, a malformed payload, or a string
    /// longer than <see cref="MaxLength"/> — returns false with an empty <paramref name="files"/>.
    /// Null or blank entries are dropped; a payload left with none returns false.
    /// </summary>
    public static bool TryParse(string? arguments, out LaunchOperation operation, out IReadOnlyList<string> files)
    {
        operation = default;
        files = [];
        if (string.IsNullOrWhiteSpace(arguments) || arguments.Length > MaxLength)
            return false;

        var parts = arguments.Trim().Split(' ');
        if (parts.Length != 2 || !TryGetOperation(parts[0], out var parsedOperation))
            return false;

        string?[]? decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<string?[]>(Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }

        var nonBlank = decoded?.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!).ToArray() ?? [];
        if (nonBlank.Length == 0)
            return false;

        operation = parsedOperation;
        files = nonBlank;
        return true;
    }

    private static string SwitchFor(LaunchOperation operation) => operation switch
    {
        LaunchOperation.Browse => "--browse",
        LaunchOperation.Extract => "--extract",
        LaunchOperation.Archive => "--archive",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static bool TryGetOperation(string value, out LaunchOperation operation)
    {
        switch (value)
        {
            case "--browse": operation = LaunchOperation.Browse; return true;
            case "--extract": operation = LaunchOperation.Extract; return true;
            case "--archive": operation = LaunchOperation.Archive; return true;
            default: operation = default; return false;
        }
    }
}
