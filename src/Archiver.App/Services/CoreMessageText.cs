using System;
using System.Globalization;
using Archiver.Core.Models;
using Archiver.Messages;
using Windows.Globalization;

namespace Archiver.App.Services;

/// <summary>
/// Core's coded messages (T-F209) in the language the App's own strings use. The App's strings come
/// from Windows resource matching over <see cref="ApplicationLanguages.Languages"/>; picking the
/// first of those Pakko translates keeps Core's text and the window's text in the same language.
/// </summary>
internal static class CoreMessageText
{
    private static readonly Lazy<CultureInfo> Culture = new(() => UiCulture.ResolveFirst(ApplicationLanguages.Languages));

    public static string Of(ArchiveError error) => MessageText.Render(error, Culture.Value);

    public static string Of(SkippedFile skipped) => MessageText.Render(skipped, Culture.Value);

    public static string Of(ArchiveWarning warning) => MessageText.Render(warning, Culture.Value);

    public static string Of(CoreText? text, string english) => MessageText.Render(text, english, Culture.Value);
}
