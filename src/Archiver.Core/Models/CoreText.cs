using System.Globalization;
using Archiver.Core.Services;

namespace Archiver.Core.Models;

/// <summary>
/// A user-visible Core message as a code plus the values it was built from (T-F209), so a frontend
/// can render it in the user's language. An argument is either a <see cref="string"/> (a file name,
/// a number already formatted, an exception or tar.exe message) or another <see cref="CoreText"/>
/// ("Cannot extract archive: {tar.exe extraction failed: ...}").
/// </summary>
public sealed class CoreText
{
    // Core nests at most three levels: an outer error, the tar.exe failure inside it, and the
    // unreadable-name detail inside that. The limit only guards the recursive Render against a bug.
    private const int MaxDepth = 8;

    internal CoreText(MessageCode code, params object[] arguments)
    {
        Code = code;
        Arguments = arguments;
    }

    /// <summary>The message, or <see cref="MessageCode.None"/> for text Core did not write.</summary>
    public MessageCode Code { get; }

    /// <summary>The template's values in placeholder order; each a <see cref="string"/> or a nested
    /// <see cref="CoreText"/>.</summary>
    public IReadOnlyList<object> Arguments { get; }

    /// <summary>The English text — what <c>ArchiveError.Message</c> and the other English fields hold.</summary>
    public string English => Render(MessageTemplates.English, CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders with <paramref name="templateFor"/>, which returns a code's template in the wanted
    /// language, or null to use the English one. Nested messages use the same lookup.
    /// </summary>
    public string Render(Func<MessageCode, string?> templateFor, IFormatProvider provider) =>
        Render(templateFor, provider, 0);

    /// <inheritdoc />
    public override string ToString() => English;

    internal static CoreText Raw(string text) => new(MessageCode.None, text);

    private string Render(Func<MessageCode, string?> templateFor, IFormatProvider provider, int depth)
    {
        if (depth >= MaxDepth)
            return string.Empty;

        string template = templateFor(Code) ?? MessageTemplates.English(Code);
        object[] values = new object[Arguments.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Arguments[i] is CoreText nested
                ? nested.Render(templateFor, provider, depth + 1)
                : Arguments[i];
        }
        return string.Format(provider, template, values);
    }
}
