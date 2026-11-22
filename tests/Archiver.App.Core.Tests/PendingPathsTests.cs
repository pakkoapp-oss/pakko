using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F278: a repeated drop gave no feedback, and C:\a\File.txt and c:\a\file.txt were listed twice.
public sealed class PendingPathsTests
{
    [Fact]
    public void NewPaths_AreAllAdded()
    {
        (IReadOnlyList<string> added, int alreadyListed) = PendingPaths.Split([@"C:\a\one.txt"], [@"C:\a\two.txt", @"C:\a\three.txt"]);

        added.Should().Equal(@"C:\a\two.txt", @"C:\a\three.txt");
        alreadyListed.Should().Be(0);
    }

    [Fact]
    public void SamePathInOtherCase_CountsAsAlreadyListed()
    {
        (IReadOnlyList<string> added, int alreadyListed) = PendingPaths.Split([@"C:\a\File.txt"], [@"c:\A\file.TXT"]);

        added.Should().BeEmpty();
        alreadyListed.Should().Be(1);
    }

    [Fact]
    public void RepeatedDrop_CountsEveryListedPath()
    {
        (IReadOnlyList<string> added, int alreadyListed) = PendingPaths.Split(
            [@"C:\a\one.txt", @"C:\a\two"], [@"C:\a\one.txt", @"C:\a\two", @"C:\a\new.txt"]);

        added.Should().Equal(@"C:\a\new.txt");
        alreadyListed.Should().Be(2);
    }

    [Fact]
    public void SamePathTwiceInOneDrop_IsAddedOnce()
    {
        (IReadOnlyList<string> added, int alreadyListed) = PendingPaths.Split([], [@"C:\a\x.txt", @"C:\A\X.txt"]);

        added.Should().Equal(@"C:\a\x.txt");
        alreadyListed.Should().Be(1);
    }

    // T-F556: a drop or an Explorer selection names "b.txt " as listed; by its plain path that is the twin.
    [Fact]
    public void ANameWindowsTrims_IsAddedByItsLiteralPath()
    {
        (IReadOnlyList<string> added, int alreadyListed) = PendingPaths.Split([], [@"C:\a\b.txt ", @"C:\a\c.txt"]);

        added.Should().Equal(@"\\?\C:\a\b.txt ", @"C:\a\c.txt");
        alreadyListed.Should().Be(0);
    }

    // Both spellings of one file are one row: converted before they are compared.
    [Fact]
    public void ANameWindowsTrims_ListedByEitherSpelling_IsOneRow()
    {
        (IReadOnlyList<string> added, int alreadyListed) = PendingPaths.Split([@"\\?\C:\a\b.txt "], [@"C:\a\b.txt "]);

        added.Should().BeEmpty();
        alreadyListed.Should().Be(1);
    }

    [Fact]
    public void EmptyDrop_ChangesNothing()
    {
        (IReadOnlyList<string> added, int alreadyListed) = PendingPaths.Split([@"C:\a\one.txt"], []);

        added.Should().BeEmpty();
        alreadyListed.Should().Be(0);
    }
}
