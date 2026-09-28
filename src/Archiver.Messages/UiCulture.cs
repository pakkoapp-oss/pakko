using System.Globalization;

namespace Archiver.Messages;

/// <summary>
/// Maps the user's UI language onto one Pakko ships (T-F254). .NET's own resource fallback only
/// walks the parent chain (de-AT -> de -> English), and Pakko's translations are region-specific
/// (de-DE), so a German user in Austria would get English. Rule: exact match, then Simplified
/// Chinese for zh-CN/zh-SG, then any shipped culture of the same language, then English.
/// </summary>
public static class UiCulture
{
    /// <summary>Every culture with its own translation; English is the neutral fallback.</summary>
    public static IReadOnlyList<string> Supported { get; } =
    [
        "ar-SA", "bg-BG", "cs-CZ", "da-DK", "de-DE", "el-GR", "es-ES", "et-EE", "fi-FI", "fr-FR",
        "he-IL", "hi-IN", "hr-HR", "hu-HU", "id-ID", "it-IT", "ja-JP", "ko-KR", "lt-LT", "lv-LV",
        "nb-NO", "nl-NL", "pl-PL", "pt-PT", "ro-RO", "sk-SK", "sl-SI", "sr-Latn-RS", "sv-SE",
        "sw-KE", "th-TH", "tr-TR", "uk-UA", "ur-PK", "vi-VN", "zh-Hans",
    ];

    /// <summary>The shipped culture for <paramref name="requested"/>, or the invariant culture (English).</summary>
    public static CultureInfo Resolve(CultureInfo requested) =>
        ResolveName(requested.Name) is { } name ? CultureInfo.GetCultureInfo(name) : CultureInfo.InvariantCulture;

    /// <summary>
    /// The first of the user's preferred languages Pakko has a translation for, the way Windows
    /// resource matching picks the App's own strings; English when none has one.
    /// </summary>
    public static CultureInfo ResolveFirst(IEnumerable<string> preferredTags)
    {
        foreach (string tag in preferredTags)
        {
            // English is the neutral table: a user who lists it before another language gets it.
            if (tag.Equals("en", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
                return CultureInfo.InvariantCulture;
            if (ResolveName(tag) is { } name)
                return CultureInfo.GetCultureInfo(name);
        }
        return CultureInfo.InvariantCulture;
    }

    /// <summary>The shipped culture name for a BCP-47 tag, or null for English.</summary>
    public static string? ResolveName(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;

        string? exact = Supported.FirstOrDefault(supported => supported.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        string[] parts = tag.Split('-');
        string language = parts[0].ToLowerInvariant();

        // Windows reports Simplified Chinese as zh-CN/zh-SG (or zh-Hans-*); Traditional (zh-TW,
        // zh-HK, zh-Hant-*) has no translation and must not get the Simplified one.
        if (language == "zh")
            return IsSimplifiedChinese(parts) ? "zh-Hans" : null;

        // Norwegian: "no" and Bokmål share the nb translation.
        if (language == "no")
            language = "nb";

        return Supported.FirstOrDefault(supported => supported.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSimplifiedChinese(string[] parts) =>
        parts.Length == 1
        || parts[1].Equals("Hans", StringComparison.OrdinalIgnoreCase)
        || parts[1].Equals("CN", StringComparison.OrdinalIgnoreCase)
        || parts[1].Equals("SG", StringComparison.OrdinalIgnoreCase);
}
