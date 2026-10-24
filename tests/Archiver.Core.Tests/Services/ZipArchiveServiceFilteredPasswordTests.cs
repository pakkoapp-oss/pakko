using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F417: a filtered or selected Test/Extract asks for the password only when an encrypted entry
/// passes, as 7-Zip does (measured with the vendored 7za 26.02: "t mixed.zip readme.txt" exits 0
/// with no password, "t mixed.zip compressible.txt" fails), and checks it against an encrypted
/// entry that passes. Fixtures: readme.txt plain + compressible.txt encrypted ("testpassword"),
/// AES-256 and ZipCrypto; encrypted_two_passwords.zip has readme.txt under "otherpassword" too.
/// </summary>
public sealed class ZipArchiveServiceFilteredPasswordTests : IDisposable
{
    private const string RealPassword = "testpassword";
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string> MixedFixtures => ["mixed_encrypted_and_plain.zip", "mixed_zipcrypto_and_plain.zip"];

    private sealed class CountingPassword(string password)
    {
        public int Prompts { get; private set; }

        public Task<PasswordDecision> AskAsync(PasswordPromptInfo _)
        {
            Prompts++;
            return Task.FromResult(new PasswordDecision { Password = password });
        }
    }

    private static Func<string, bool, bool> Only(string name) => (entry, _) => entry == name;

    private static bool HasCode(CoreText? text, MessageCode code) =>
        text is not null && (text.Code == code || text.Arguments.OfType<CoreText>().Any(nested => HasCode(nested, code)));

    // ── Test ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task TestAsync_FilterPassesOnlyThePlainEntry_NoPasswordAsked(string fixture)
    {
        var password = new CountingPassword(RealPassword);

        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive(fixture)], resolvePasswordAsync: password.AskAsync, entryFilter: Only("readme.txt"));

        result.Errors.Should().BeEmpty();
        password.Prompts.Should().Be(0);
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task TestAsync_FilterPassesOnlyThePlainEntry_NoResolverAtAll_Passes(string fixture)
    {
        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive(fixture)], entryFilter: Only("readme.txt"));

        result.Errors.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task TestAsync_FilterPassesOnlyThePlainEntry_AWrongPasswordGivenUpFrontIsNeverUsed(string fixture)
    {
        var password = new CountingPassword("wrong");

        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive(fixture)], resolvePasswordAsync: password.AskAsync, entryFilter: Only("readme.txt"));

        result.Errors.Should().BeEmpty();
        password.Prompts.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task TestAsync_FilterPassesTheEncryptedEntry_AsksOnceAndPasses(string fixture)
    {
        var password = new CountingPassword(RealPassword);

        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive(fixture)], resolvePasswordAsync: password.AskAsync, entryFilter: Only("compressible.txt"));

        result.Errors.Should().BeEmpty();
        password.Prompts.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task TestAsync_FilterPassesTheEncryptedEntry_WrongPassword_IsStillReported(string fixture)
    {
        var password = new CountingPassword("wrong");

        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive(fixture)], resolvePasswordAsync: password.AskAsync, entryFilter: Only("compressible.txt"));

        result.Errors.Should().ContainSingle().Which.Text.Should().Match<CoreText>(t => HasCode(t, MessageCode.PasswordProtectedTest));
        password.Prompts.Should().Be(3);
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task TestAsync_FilterPassesTheEncryptedEntry_NoResolver_IsReportedPasswordProtected(string fixture)
    {
        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive(fixture)], entryFilter: Only("compressible.txt"));

        result.Errors.Should().ContainSingle().Which.Text.Should().Match<CoreText>(t => HasCode(t, MessageCode.PasswordProtectedTest));
    }

    [Fact]
    public async Task TestAsync_TwoPasswords_PasswordIsCheckedAgainstTheEncryptedEntryThatPasses()
    {
        var password = new CountingPassword(RealPassword);

        ArchiveResult result = await _sut.TestAsync(
            [FixtureHelper.Archive("encrypted_two_passwords.zip")], resolvePasswordAsync: password.AskAsync, entryFilter: Only("compressible.txt"));

        result.Errors.Should().BeEmpty("readme.txt's own password is never asked for: it is filtered out");
        password.Prompts.Should().Be(1);
    }

    // ── Extract ──────────────────────────────────────────────────────────────

    private async Task<(ArchiveResult Result, string Dest)> ExtractAsync(
        string fixture, CountingPassword? password, Func<string, bool, bool>? filter = null, IReadOnlyList<string>? selection = null)
    {
        string dest = Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive(fixture)],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            EntryFilter = filter,
            SelectedEntryPaths = selection,
            ResolvePasswordAsync = password is null ? null : password.AskAsync,
        });
        return (result, dest);
    }

    private static string[] Extracted(string dest) =>
        Directory.Exists(dest) ? [.. Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Order(StringComparer.Ordinal)!] : [];

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task ExtractAsync_FilterPassesOnlyThePlainEntry_NoPasswordAsked(string fixture)
    {
        var password = new CountingPassword(RealPassword);

        (ArchiveResult result, string dest) = await ExtractAsync(fixture, password, filter: Only("readme.txt"));

        result.Errors.Should().BeEmpty();
        password.Prompts.Should().Be(0);
        Extracted(dest).Should().Equal("readme.txt");
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task ExtractAsync_SelectionOfOnlyThePlainEntry_NoPasswordAsked(string fixture)
    {
        var password = new CountingPassword(RealPassword);

        (ArchiveResult result, string dest) = await ExtractAsync(fixture, password, selection: ["readme.txt"]);

        result.Errors.Should().BeEmpty();
        password.Prompts.Should().Be(0);
        Extracted(dest).Should().Equal("readme.txt");
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task ExtractAsync_FilterPassesOnlyThePlainEntry_NoResolverAtAll_Extracts(string fixture)
    {
        (ArchiveResult result, string dest) = await ExtractAsync(fixture, password: null, filter: Only("readme.txt"));

        result.Errors.Should().BeEmpty();
        Extracted(dest).Should().Equal("readme.txt");
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task ExtractAsync_SelectionOfTheEncryptedEntry_AsksOnceAndExtracts(string fixture)
    {
        var password = new CountingPassword(RealPassword);

        (ArchiveResult result, string dest) = await ExtractAsync(fixture, password, selection: ["compressible.txt"]);

        result.Errors.Should().BeEmpty();
        password.Prompts.Should().Be(1);
        Extracted(dest).Should().Equal("compressible.txt");
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task ExtractAsync_FilterPassesTheEncryptedEntry_WrongPassword_IsStillReported(string fixture)
    {
        var password = new CountingPassword("wrong");

        (ArchiveResult result, string dest) = await ExtractAsync(fixture, password, filter: Only("compressible.txt"));

        result.Errors.Should().ContainSingle().Which.Text.Should().Match<CoreText>(t => HasCode(t, MessageCode.PasswordProtectedExtract));
        password.Prompts.Should().Be(3);
        Extracted(dest).Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_TwoPasswords_PasswordIsCheckedAgainstTheSelectedEncryptedEntry()
    {
        var password = new CountingPassword(RealPassword);

        (ArchiveResult result, string dest) = await ExtractAsync("encrypted_two_passwords.zip", password, selection: ["compressible.txt"]);

        result.Errors.Should().BeEmpty();
        password.Prompts.Should().Be(1);
        Extracted(dest).Should().Equal("compressible.txt");
    }

    [Theory]
    [MemberData(nameof(MixedFixtures))]
    public async Task ExtractAsync_NoFilterNoSelection_StillAsksOnce(string fixture)
    {
        var password = new CountingPassword(RealPassword);

        (ArchiveResult result, string dest) = await ExtractAsync(fixture, password);

        result.Errors.Should().BeEmpty();
        password.Prompts.Should().Be(1);
        Extracted(dest).Should().Equal("compressible.txt", "readme.txt");
    }
}
