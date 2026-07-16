using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class PreviewCacheTests : IDisposable
{
    public void Dispose() => PreviewCache.DeleteAll();

    [Fact]
    public void CreateScope_ReturnsNewExistingDirectoryUnderRoot()
    {
        string scope = PreviewCache.CreateScope();

        Directory.Exists(scope).Should().BeTrue();
        Path.GetDirectoryName(scope).Should().Be(PreviewCache.RootDirectory.TrimEnd(Path.DirectorySeparatorChar));
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
    public void DeleteAll_RemovesRootDirectory()
    {
        string scope = PreviewCache.CreateScope();
        File.WriteAllText(Path.Combine(scope, "preview.txt"), "content");

        PreviewCache.DeleteAll();

        Directory.Exists(PreviewCache.RootDirectory).Should().BeFalse();
    }

    [Fact]
    public void DeleteAll_RootDoesNotExist_DoesNotThrow()
    {
        PreviewCache.DeleteAll();

        var act = () => PreviewCache.DeleteAll();

        act.Should().NotThrow();
    }
}
