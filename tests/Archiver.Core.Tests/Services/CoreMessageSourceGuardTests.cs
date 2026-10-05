using System.Text.RegularExpressions;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F209: a message field set outside CoreMessages has no code, so every frontend shows it in
// English. Reads Core's source and fails on any such assignment — a new message cannot slip in
// without a code.
public sealed partial class CoreMessageSourceGuardTests
{
    private static readonly string CoreSource = Path.Combine(FindRepoRoot(), "src", "Archiver.Core");

    [Fact]
    public void MessageFields_AreSetOnlyInCoreMessages()
    {
        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(CoreSource, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(CoreSource, file);
            if (relative.StartsWith("bin", StringComparison.Ordinal) || relative.StartsWith("obj", StringComparison.Ordinal)
                || relative == Path.Combine("Services", "CoreMessages.cs"))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (MessageAssignment().IsMatch(lines[i]) || HashEntryWithError().IsMatch(lines[i])
                    || RawExceptionTextArgument().IsMatch(lines[i]))
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void EveryCode_HasAnEnglishTemplate()
    {
        MessageTemplates.Codes.Should().BeEquivalentTo(Enum.GetValues<MessageCode>().Where(c => c != MessageCode.None));
    }

    [Fact]
    public void Render_NestedText_UsesTheLookupAtEveryLevel()
    {
        CoreText inner = CoreMessages.Text(MessageCode.TarExtractionFailed, "boom");
        CoreText outer = CoreMessages.Text(MessageCode.CannotExtractArchive, inner);

        outer.English.Should().Be("Cannot extract archive: tar.exe extraction failed: boom");
        outer.Render(code => code == MessageCode.TarExtractionFailed ? "T[{0}]" : "O<{0}>", System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be("O<T[boom]>");
    }

    [Fact]
    public void Error_SetsMessageFromTheSameText()
    {
        ArchiveError error = CoreMessages.Error("a.zip", MessageCode.ZipCorrupted);

        error.Text!.Code.Should().Be(MessageCode.ZipCorrupted);
        error.Message.Should().Be("File has ZIP signature but appears corrupted or incomplete.");
    }

    // T-F280: the guard above reads ArchiveWarning's Message like every other message field.
    [Fact]
    public void Warning_SetsMessageFromTheSameText()
    {
        ArchiveWarning warning = CoreMessages.Warning("a.zip", CoreMessages.Text(MessageCode.LocalHeaderMismatch, "2", "b.txt"));

        warning.SourcePath.Should().Be("a.zip");
        warning.Text!.Code.Should().Be(MessageCode.LocalHeaderMismatch);
        warning.Message.Should().Be(warning.Text.English);
        MessageAssignment().IsMatch("new ArchiveWarning { SourcePath = p, Message = \"bare\" }").Should().BeTrue();
    }

    [Fact]
    public void FromException_KeepsCoreCodeAndWrapsForeignText()
    {
        CoreMessages.FromException(new CoreTextIOException(CoreMessages.Text(MessageCode.ArchiveInUse, "x")))
            .Code.Should().Be(MessageCode.ArchiveInUse);
        CoreText foreign = CoreMessages.FromException(new IOException("disk says no"));
        foreign.Code.Should().Be(MessageCode.None);
        foreign.English.Should().Be("disk says no");
    }

    // T-F297: a Windows error (facility 7) keeps its English text and gets its code; the common
    // ones get a code of their own so a frontend can add a translation.
    [Theory]
    [InlineData(unchecked((int)0x8007007B), MessageCode.SystemInvalidName)]
    [InlineData(unchecked((int)0x80070005), MessageCode.SystemAccessDenied)]
    [InlineData(unchecked((int)0x80070020), MessageCode.SystemSharingViolation)]
    [InlineData(unchecked((int)0x80070021), MessageCode.SystemSharingViolation)]
    [InlineData(unchecked((int)0x80070070), MessageCode.SystemDiskFull)]
    [InlineData(unchecked((int)0x80070027), MessageCode.SystemDiskFull)]
    public void Detail_CommonWindowsError_HasItsCodeAndKeepsTheEnglishText(int hResult, MessageCode expected)
    {
        CoreText detail = CoreMessages.Detail(new IOException("os text", hResult));

        detail.Code.Should().Be(expected);
        detail.English.Should().Be($"os text (0x{hResult:X8})");
    }

    [Fact]
    public void Detail_WindowsErrorOnAnInnerException_IsFound()
    {
        CoreText detail = CoreMessages.Detail(new IOException("wrapped", new UnauthorizedAccessException("denied")));

        detail.Code.Should().Be(MessageCode.SystemAccessDenied);
        detail.English.Should().Be("wrapped (0x80070005)");
    }

    [Fact]
    public void Detail_OtherWindowsError_IsTextAndCodeWithoutAMessageCode()
    {
        CoreText detail = CoreMessages.Detail(new IOException("not found", unchecked((int)0x80070002)));

        detail.Code.Should().Be(MessageCode.None);
        detail.English.Should().Be("not found (0x80070002)");
    }

    [Fact]
    public void Detail_NoWindowsError_KeepsTheTextAsIs()
    {
        CoreMessages.Detail(new InvalidDataException("bad data")).English.Should().Be("bad data");
        CoreMessages.Detail(new IOException("plain")).English.Should().Be("plain");
    }

    [Fact]
    public void Detail_Rewrite_ChangesOnlyTheEnglishText()
    {
        CoreText detail = CoreMessages.Detail(new IOException(@"C:\stage\x", unchecked((int)0x8007007B)),
            text => text.Replace(@"C:\stage", @"C:\dest", StringComparison.Ordinal));

        detail.English.Should().Be(@"C:\dest\x (0x8007007B)");
    }

    [Fact]
    public void FromException_ForeignWindowsError_CarriesTheCode()
    {
        CoreMessages.FromException(new IOException("disk says no", unchecked((int)0x80070070)))
            .English.Should().Be("disk says no (0x80070070)");
    }

    // "Message = ", "Reason = ", "ErrorMessage = " as an object-initializer or with-expression
    // member; comparisons ("==") and reads ("= ex.Message") do not match.
    [GeneratedRegex(@"(?<![\w.])(Message|Reason|ErrorMessage)\s*=(?!=)")]
    private static partial Regex MessageAssignment();

    // T-F297: an exception's text goes into a message only through CoreMessages.Detail.
    [GeneratedRegex(@"CoreMessages\.Text\(.*\bex\.Message\b")]
    private static partial Regex RawExceptionTextArgument();

    [GeneratedRegex(@"new HashEntry\([^,]+,\s*null,\s*(?!null\))")]
    private static partial Regex HashEntryWithError();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
