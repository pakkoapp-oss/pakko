using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F219: "Hash..." hashes the pending list and the dialog can copy its result.
public sealed class HashReportTests
{
    [Fact]
    public void EmptyList_AsksForFiles()
    {
        HashReport.SourcesFor([]).Should().BeNull();
    }

    [Fact]
    public void ListedItems_AreHashedAsListed()
    {
        HashReport.SourcesFor([@"C:\a\one.txt", @"C:\b\dir"]).Should().Equal(@"C:\a\one.txt", @"C:\b\dir");
    }

    [Fact]
    public void ListedFile_IsShownByItsName()
    {
        HashReport.DisplayName(@"C:\a\one.txt", [@"C:\a\one.txt"]).Should().Be("one.txt");
    }

    [Fact]
    public void FileInsideAListedFolder_IsShownUnderTheFoldersName()
    {
        HashReport.DisplayName(@"C:\a\dir\sub\x.txt", [@"C:\a\dir"]).Should().Be(@"dir\sub\x.txt");
    }

    [Fact]
    public void FileUnderAListedDriveRoot_IsShownRelativeToTheRoot()
    {
        HashReport.DisplayName(@"C:\sub\x.txt", [@"C:\"]).Should().Be(@"sub\x.txt");
    }

    [Fact]
    public void FolderPrefix_MatchesOnlyWholeNames()
    {
        HashReport.DisplayName(@"C:\a\dir2\x.txt", [@"C:\a\dir"]).Should().Be("x.txt");
    }

    // The dialog's real input: a listed folder hashed by FileHashService's folder mode.
    [Fact]
    public async Task ListedFolder_ThroughFileHashService_IsNamedUnderTheFolder()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory("PakkoHashReport");
        try
        {
            string folder = Path.Combine(temp.FullName, "dir");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllText(Path.Combine(folder, "a.txt"), "abc");
            File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "xyz");
            string[] listed = [folder];

            HashResult result = await FileHashService.ComputeAsync(listed, Archiver.Core.Models.HashAlgorithmKind.Sha256, null, CancellationToken.None);

            result.Entries.Where(e => e.Hash is not null).Select(e => HashReport.DisplayName(e.SourcePath, listed))
                .Should().BeEquivalentTo(@"dir\a.txt", @"dir\sub\b.txt");
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void CopyText_IsSha256sumFormat_AndLeavesOutFailures()
    {
        HashResult result = new()
        {
            Entries =
            [
                new HashEntry(@"C:\a\one.txt", "aa11", null),
                new HashEntry(@"C:\a\bad.txt", null, "locked"),
                new HashEntry(@"C:\a\two.txt", "bb22", null),
            ],
        };

        HashReport.CopyText(result, [@"C:\a\one.txt", @"C:\a\bad.txt", @"C:\a\two.txt"])
            .Should().Be("aa11  one.txt" + Environment.NewLine + "bb22  two.txt");
    }
}
