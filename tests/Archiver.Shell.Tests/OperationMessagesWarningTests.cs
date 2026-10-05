using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F280: a warning never fails an Explorer command, but the user sees it — on its own, under the
// errors, or under the skipped list.
public sealed class OperationMessagesWarningTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "PakkoShellWarn-" + Path.GetRandomFileName());

    private static readonly ArchiveWarning AWarning = new() { SourcePath = @"C:\dir\a.zip", Message = "headers disagree" };

    public OperationMessagesWarningTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        CultureInfo.CurrentCulture = _originalCulture;
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void ForArchiveResult_OnlyAWarning_IsAWarningMessageNamingTheArchive()
    {
        var result = new ArchiveResult { CreatedFiles = [@"C:\dir\a"], Warnings = [AWarning] };

        OperationMessage message = OperationMessages.ForArchiveResult("T", result)!;

        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().Be("a.zip: headers disagree");
    }

    [Fact]
    public void ForArchiveResult_ErrorAndWarning_ListsTheErrorThenTheWarning()
    {
        var result = new ArchiveResult
        {
            Errors = [new ArchiveError { SourcePath = "b.tar", Message = "tar failed" }],
            Warnings = [AWarning],
        };

        OperationMessage message = OperationMessages.ForArchiveResult("T", result)!;

        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Split(Environment.NewLine).Should().Equal("b.tar: tar failed", "", "a.zip: headers disagree");
    }

    [Fact]
    public void ForArchiveResult_SkipAndWarning_ListsTheSkipsThenTheWarning()
    {
        var result = new ArchiveResult
        {
            CreatedFiles = [@"C:\dir\a"],
            SkippedFiles = [new SkippedFile { Path = "bad.txt", Reason = "ADS entry" }],
            Warnings = [AWarning],
        };

        OperationMessage message = OperationMessages.ForArchiveResult("T", result)!;

        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().StartWith("Skipped (1):").And.EndWith(Environment.NewLine + Environment.NewLine + "a.zip: headers disagree");
    }

    [Fact]
    public void ForArchiveResult_TwelveWarnings_ShowsTenAndTheRestCount()
    {
        var result = new ArchiveResult
        {
            Warnings = [.. Enumerable.Range(1, 12).Select(i => new ArchiveWarning { SourcePath = $"f{i}.zip", Message = "x" })],
        };

        string[] lines = OperationMessages.ForArchiveResult("T", result)!.Text.Split(Environment.NewLine);

        lines.Should().HaveCount(11);
        lines[10].Should().Be("…and 2 more");
    }

    [Fact]
    public void Combine_SeveralResults_KeepsEveryWarning()
    {
        ArchiveResult combined = ShellCommands.Combine(
        [
            new ArchiveResult { CreatedFiles = ["a"], Warnings = [AWarning] },
            new ArchiveResult { CreatedFiles = ["b"] },
            new ArchiveResult { Warnings = [AWarning with { SourcePath = "c.zip" }] },
        ]);

        combined.Warnings.Select(w => w.SourcePath).Should().Equal(@"C:\dir\a.zip", "c.zip");
        combined.Outcome.Should().Be(OperationOutcome.CompletedWithWarnings);
    }

    // The real thing end to end: Core's warning for an archive whose local headers disagree with
    // its central directory, rendered by its code in the UI language.
    [Fact]
    public async Task ForArchiveResult_RealHeaderMismatchUnderUkrainian_IsRenderedInUkrainian()
    {
        string archive = Path.Combine(_scratch, "tampered.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("b.txt").Open());
            writer.Write("bravo");
        }
        byte[] bytes = File.ReadAllBytes(archive);
        int local = bytes.AsSpan().IndexOf("PK\u0003\u0004"u8);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local + 26)).Should().Be(5);
        bytes[local + 30] = (byte)'x';
        File.WriteAllBytes(archive, bytes);
        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions()).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = Path.Combine(_scratch, "out"),
            Mode = ExtractMode.SingleFolder,
        });
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");

        OperationMessage message = OperationMessages.ForArchiveResult("T", result)!;

        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().StartWith("tampered.zip: Елементів, у яких локальний заголовок не збігається з центральним каталогом: 1")
            .And.NotContain("local header");
        File.ReadAllText(Path.Combine(_scratch, "out", "b.txt"), Encoding.UTF8).Should().Be("bravo");
    }
}
