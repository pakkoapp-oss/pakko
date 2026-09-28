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
                if (MessageAssignment().IsMatch(lines[i]) || HashEntryWithError().IsMatch(lines[i]))
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

    [Fact]
    public void FromException_KeepsCoreCodeAndWrapsForeignText()
    {
        CoreMessages.FromException(new CoreTextIOException(CoreMessages.Text(MessageCode.ArchiveInUse, "x")))
            .Code.Should().Be(MessageCode.ArchiveInUse);
        CoreText foreign = CoreMessages.FromException(new IOException("disk says no"));
        foreign.Code.Should().Be(MessageCode.None);
        foreign.English.Should().Be("disk says no");
    }

    // "Message = ", "Reason = ", "ErrorMessage = " as an object-initializer or with-expression
    // member; comparisons ("==") and reads ("= ex.Message") do not match.
    [GeneratedRegex(@"(?<![\w.])(Message|Reason|ErrorMessage)\s*=(?!=)")]
    private static partial Regex MessageAssignment();

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
