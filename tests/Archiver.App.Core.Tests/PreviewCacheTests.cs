using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class PreviewCacheTests : IDisposable
{
    public void Dispose() => PreviewCache.DeleteOwn();

    [Fact]
    public void CreateScope_ReturnsNewExistingDirectoryUnderRoot()
    {
        string scope = PreviewCache.CreateScope();

        Directory.Exists(scope).Should().BeTrue();
        Path.GetDirectoryName(scope).Should().Be(PreviewCache.OwnDirectory);
    }

    [Fact]
    public void CreateScope_CalledTwice_ReturnsDistinctDirectories()
    {
        string first = PreviewCache.CreateScope();
        string second = PreviewCache.CreateScope();

        first.Should().NotBe(second);
        Directory.Exists(first).Should().BeTrue();
        Directory.Exists(second).Should().BeTrue();
    }

    [Fact]
    public void DeleteOwn_RemovesOwnDirectory()
    {
        string scope = PreviewCache.CreateScope();
        File.WriteAllText(Path.Combine(scope, "preview.txt"), "content");

        PreviewCache.DeleteOwn();

        Directory.Exists(PreviewCache.OwnDirectory).Should().BeFalse();
    }

    [Fact]
    public void DeleteOwn_DirectoryDoesNotExist_DoesNotThrow()
    {
        PreviewCache.DeleteOwn();

        Action act = () => PreviewCache.DeleteOwn();

        act.Should().NotThrow();
    }
}
