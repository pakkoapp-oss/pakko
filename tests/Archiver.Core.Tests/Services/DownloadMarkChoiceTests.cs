using System.IO.Compression;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F360: the user may leave the download mark off for one extraction
/// (<see cref="ExtractOptions.ApplyDownloadMark"/>); a <c>EnforceMOTW</c> Group Policy wins over
/// that choice either way.
/// </summary>
public sealed class DownloadMarkChoiceTests : IDisposable
{
    private const string Mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
    private static readonly DateTime EntryTime = new(2020, 5, 6, 7, 8, 10, DateTimeKind.Local);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string MarkedZip()
    {
        string zipPath = Path.Combine(_temp.Path, "marked.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("doc.txt");
            entry.LastWriteTime = EntryTime;
            using StreamWriter writer = new(entry.Open());
            writer.Write("hello");
        }
        File.WriteAllText(zipPath + ":Zone.Identifier", Mark);
        return zipPath;
    }

    private async Task<string> ExtractThroughRouterAsync(GroupPolicyOptions policy, bool applyMark)
    {
        string destDir = Path.Combine(_temp.Path, "out");
        IExtractionRouter router = await PakkoServices.Create(policy).CreateExtractionRouterAsync();
        ArchiveResult result = await router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [MarkedZip()],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ApplyDownloadMark = applyMark,
        });
        result.Errors.Should().BeEmpty();
        return Path.Combine(destDir, "doc.txt");
    }

    // --- Happy path ---

    [Fact]
    public void ApplyDownloadMark_IsOnByDefault()
    {
        new ExtractOptions { ArchivePaths = [], DestinationFolder = "x" }.ApplyDownloadMark.Should().BeTrue();
    }

    [Fact]
    public async Task MarkOn_NoPolicy_FileIsMarked()
    {
        string file = await ExtractThroughRouterAsync(new GroupPolicyOptions(), applyMark: true);

        File.Exists(file + ":Zone.Identifier").Should().BeTrue();
    }

    [Fact]
    public async Task MarkOff_NoPolicy_FileIsNotMarkedAndKeepsItsTime()
    {
        string file = await ExtractThroughRouterAsync(new GroupPolicyOptions(), applyMark: false);

        File.Exists(file + ":Zone.Identifier").Should().BeFalse("the user left the mark off");
        File.GetLastWriteTime(file).Should().Be(EntryTime);
    }

    // --- Security & Boundary: the policy wins ---

    [Fact]
    public async Task MarkOff_PolicySaysAllFiles_FileIsStillMarked()
    {
        var policy = new GroupPolicyOptions { MotwMode = MotwMode.AllFiles, MotwModeSetByPolicy = true };

        string file = await ExtractThroughRouterAsync(policy, applyMark: false);

        File.Exists(file + ":Zone.Identifier").Should().BeTrue("a set EnforceMOTW policy overrides the user");
    }

    [Fact]
    public async Task MarkOn_PolicySaysDisabled_FileIsNotMarked()
    {
        var policy = new GroupPolicyOptions { MotwMode = MotwMode.Disabled, MotwModeSetByPolicy = true };

        string file = await ExtractThroughRouterAsync(policy, applyMark: true);

        File.Exists(file + ":Zone.Identifier").Should().BeFalse();
    }

    [Theory]
    // applyMark, setByPolicy, policy mode -> effective mode
    [InlineData(true, false, MotwMode.AllFiles, MotwMode.AllFiles)]
    [InlineData(true, false, MotwMode.Disabled, MotwMode.Disabled)]
    [InlineData(true, true, MotwMode.AllFiles, MotwMode.AllFiles)]
    [InlineData(true, true, MotwMode.Disabled, MotwMode.Disabled)]
    [InlineData(false, false, MotwMode.AllFiles, MotwMode.Disabled)]
    [InlineData(false, false, MotwMode.UnsafeExtensionsOnly, MotwMode.Disabled)]
    [InlineData(false, true, MotwMode.AllFiles, MotwMode.AllFiles)]
    [InlineData(false, true, MotwMode.UnsafeExtensionsOnly, MotwMode.UnsafeExtensionsOnly)]
    public void EffectiveMotwMode_PolicyWinsOtherwiseTheChoice(bool applyMark, bool setByPolicy, MotwMode mode, MotwMode expected)
    {
        var policy = new GroupPolicyOptions { MotwMode = mode, MotwModeSetByPolicy = setByPolicy };

        policy.EffectiveMotwMode(applyMark).Should().Be(expected);
    }

    // --- ArchiveDownloadMark.IsPresent ---

    [Fact]
    public void IsPresent_MarkedArchive_True()
    {
        ArchiveDownloadMark.IsPresent(MarkedZip()).Should().BeTrue();
    }

    [Fact]
    public void IsPresent_UnmarkedArchive_False()
    {
        ArchiveDownloadMark.IsPresent(_temp.CreateFile("plain.zip", "x")).Should().BeFalse();
    }

    // --- Misuse & Error path: never throws ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\definitely\\not\\here\\a.zip")]
    [InlineData("bad|name?.zip")]
    public void IsPresent_BadOrMissingPath_FalseWithoutThrowing(string path)
    {
        ArchiveDownloadMark.IsPresent(path).Should().BeFalse();
    }

    [Fact]
    public void IsPresent_Folder_False()
    {
        ArchiveDownloadMark.IsPresent(_temp.Path).Should().BeFalse();
    }
}
