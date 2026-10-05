using FluentAssertions;

namespace Archiver.Messages.Tests;

// T-F329: the App, Explorer's operation window and Explorer's menu each keep their own copy of
// the conflict dialog, the password dialog, the bomb warning and more (about 30 English strings in
// two or three sources). Nothing ties the copies together, so a fix made in one of them leaves the
// other saying something else for the same thing.
public sealed class SharedStringTests
{
    // The same English, on purpose not the same thing: a button's verb and a dialog title's noun,
    // and four OS-error wrappers that share one "{0} ({1})" shape.
    private static readonly string[] NotCopies =
    [
        "App/TestResultTitle",
        "CoreMessages/SystemInvalidName", "CoreMessages/SystemAccessDenied",
        "CoreMessages/SystemSharingViolation", "CoreMessages/SystemDiskFull",
    ];

    private static readonly string[][] Copies =
    [
        .. LocalizedSources.Read(LocalizedSources.English)
            .Where(s => !NotCopies.Contains(s.Key))
            .GroupBy(s => s.Value, s => s.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToArray()),
    ];

    public static TheoryData<string> Locales()
    {
        var data = new TheoryData<string>();
        foreach (string locale in LocalizedSources.Locales().Where(l => l != LocalizedSources.English))
            data.Add(locale);
        return data;
    }

    [Fact]
    public void English_HasStringsKeptInSeveralSources() =>
        Copies.Should().Contain(g => g.Contains("App/ConflictDialogTitle") && g.Contains("ConflictMessages/ConflictDialogTitle"))
            .And.Contain(g => g.Contains("App/ScanButton.Content") && g.Contains("ShellExt/scanArchive"))
            .And.HaveCountGreaterThan(30);

    [Theory]
    [MemberData(nameof(Locales))]
    public void TheSameEnglish_IsTranslatedTheSameWay_InEverySource(string locale)
    {
        Dictionary<string, string> strings = LocalizedSources.Read(locale);

        foreach (string[] keys in Copies)
            keys.Select(k => strings[k]).Distinct(StringComparer.Ordinal)
                .Should().HaveCount(1, $"{locale}: {string.Join(", ", keys)} are one English string");
    }
}
