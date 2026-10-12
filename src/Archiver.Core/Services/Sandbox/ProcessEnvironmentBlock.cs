using System.Collections;
using System.Text;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F342: the environment block CreateProcessW takes with CREATE_UNICODE_ENVIRONMENT - every
/// "name=value" followed by a NUL, sorted by name without regard to case, and one more NUL at the
/// end. It is this process's whole environment with some values replaced: a child given only the
/// replaced ones has no SystemRoot or PATH.
/// </summary>
internal static class ProcessEnvironmentBlock
{
    /// <summary>This process's environment with <paramref name="overrides"/> applied.</summary>
    public static char[] Build(IReadOnlyDictionary<string, string> overrides)
    {
        var current = new List<KeyValuePair<string, string>>();
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            current.Add(new((string)variable.Key, (string?)variable.Value ?? ""));
        return Build(current, overrides);
    }

    internal static char[] Build(IEnumerable<KeyValuePair<string, string>> current, IReadOnlyDictionary<string, string> overrides)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in current)
            variables[name] = value;
        foreach ((string name, string value) in overrides)
        {
            if (name.Length == 0 || name.Contains('=') || name.Contains('\0') || value.Contains('\0'))
                throw new ArgumentException($"'{name}' cannot be set in an environment block.", nameof(overrides));
            variables.Remove(name); // the override's own spelling of the name wins
            variables[name] = value;
        }

        var block = new StringBuilder();
        foreach ((string name, string value) in variables)
            block.Append(name).Append('=').Append(value).Append('\0');
        if (variables.Count == 0)
            block.Append('\0');
        block.Append('\0');
        return block.ToString().ToCharArray();
    }
}
