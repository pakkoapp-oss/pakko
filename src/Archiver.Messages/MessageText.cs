using System.Globalization;
using System.Resources;
using Archiver.Core.Models;

namespace Archiver.Messages;

/// <summary>
/// Renders Core's coded messages (T-F209) in a UI language — one table of translations shared by
/// every graphical frontend, so the App and the Explorer commands cannot word the same error
/// differently. The CLI stays English and prints Core's English text directly.
/// </summary>
public static class MessageText
{
    private static readonly ResourceManager Resources =
        new("Archiver.Messages.Resources.CoreMessages", typeof(MessageText).Assembly);

    /// <summary>
    /// T-F297 (user decision, 2026-10-03): error details as Windows or Core wrote them stay English
    /// with their code; only the Ukrainian table puts a translation in front. Every other language
    /// has no entry for these codes and falls back to the English template.
    /// </summary>
    public static IReadOnlySet<MessageCode> UkrainianOnlyCodes { get; } = new HashSet<MessageCode>
    {
        MessageCode.SystemInvalidName,
        MessageCode.SystemAccessDenied,
        MessageCode.SystemSharingViolation,
        MessageCode.SystemDiskFull,
        MessageCode.ContentCrcMismatch,
        MessageCode.ContentLargerThanDeclared,
        MessageCode.ContentSmallerThanDeclared,
        MessageCode.EntryDataTruncated,
    };

    /// <summary><paramref name="text"/> in <paramref name="culture"/> (resolved with
    /// <see cref="UiCulture.Resolve"/>); <paramref name="english"/> when there is no code.</summary>
    public static string Render(CoreText? text, string english, CultureInfo culture)
    {
        if (text is null)
            return english;

        CultureInfo resolved = UiCulture.Resolve(culture);
        return text.Render(code => code == MessageCode.None ? null : Resources.GetString(code.ToString(), resolved), resolved);
    }

    /// <summary>An error's message in <paramref name="culture"/>.</summary>
    public static string Render(ArchiveError error, CultureInfo culture) => Render(error.Text, error.Message, culture);

    /// <summary>A skip's reason in <paramref name="culture"/>.</summary>
    public static string Render(SkippedFile skipped, CultureInfo culture) => Render(skipped.Text, skipped.Reason, culture);
}
