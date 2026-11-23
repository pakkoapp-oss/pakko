using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F571: whether a ZIP entry is encrypted is read from its two headers by the catalog and the
// locator only; a reader asks NamedZipEntry (Encrypted, IsPlain), whose entry has already been
// refused when the headers disagree. A reader that took one header's word alone decoded by one
// header what another program reads by the other (the P3-1 review finding).
public sealed partial class ZipEncryptionOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");

    private static readonly string[] Owners =
    [
        "Archiver.Core/Services/Zip/ZipCatalog.cs",
        "Archiver.Core/Services/Zip/Decryption/RawZipEntryLocator.cs",
        "Archiver.Core/Services/Zip/Decryption/EncryptedZipEntryReader.cs",
    ];

    // Each with why; a new one fails here.
    private static readonly (string File, int Count, string Why)[] Allowed =
    [
        ("Archiver.Core/Services/ZipArchiveService.cs", 2,
            "the password probe, over ZipCatalog.Located (which leaves out an entry whose headers disagree), "
            + "and the list's encryption column, which decodes nothing"),
    ];

    [Fact]
    public void EncryptionBit_IsReadOnlyByTheOwners()
    {
        var found = new Dictionary<string, int>();
        foreach (string file in Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !Owners.Contains(f) && !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal)))
        {
            string code = string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file))
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            int count = EncryptionFact().Count(code);
            if (count > 0)
                found[file] = count;
        }
        Dictionary<string, int> allowed = Allowed.ToDictionary(a => a.File, a => a.Count);

        found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key} x{f.Value}")
            .Should().BeEmpty("a reader asks NamedZipEntry.Encrypted or IsPlain (T-F571)");
        allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key} x{a.Value}")
            .Should().BeEmpty("a listed use that is gone leaves room for a new one");
    }

    [GeneratedRegex(@"GeneralPurposeEncryptedBit|\b[Ee]ntry\.IsEncrypted\b")]
    private static partial Regex EncryptionFact();

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "windows-archiver-wrapper.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }
}
