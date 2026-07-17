using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class NestedArchiveCacheTests : IDisposable
{
    public void Dispose() => NestedArchiveCache.DeleteAll();

    [Fact]
    public void CreateScope_ReturnsNewExistingDirectoryUnderRoot()
    {
        string scope = NestedArchiveCache.CreateScope();

        Directory.Exists(scope).Should().BeTrue();
        Path.GetDirectoryName(scope).Should().Be(NestedArchiveCache.RootDirectory.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void CreateScope_CalledTwice_ReturnsDistinctDirectories()
    {
        string first = NestedArchiveCache.CreateScope();
        string second = NestedArchiveCache.CreateScope();

        first.Should().NotBe(second);
        Directory.Exists(first).Should().BeTrue();
        Directory.Exists(second).Should().BeTrue();
    }

    [Fact]
    public void DeleteScope_RemovesOnlyThatScope()
    {
        string first = NestedArchiveCache.CreateScope();
        string second = NestedArchiveCache.CreateScope();
        File.WriteAllText(Path.Combine(first, "inner.rar"), "content");

        NestedArchiveCache.DeleteScope(first);

        Directory.Exists(first).Should().BeFalse();
        Directory.Exists(second).Should().BeTrue();
    }

    [Fact]
    public void DeleteScope_ScopeDoesNotExist_DoesNotThrow()
    {
        var act = () => NestedArchiveCache.DeleteScope(Path.Combine(NestedArchiveCache.RootDirectory, "nonexistent"));

        act.Should().NotThrow();
    }

    [Fact]
    public void DeleteAll_RemovesRootDirectory()
    {
        string scope = NestedArchiveCache.CreateScope();
        File.WriteAllText(Path.Combine(scope, "inner.rar"), "content");

        NestedArchiveCache.DeleteAll();

        Directory.Exists(NestedArchiveCache.RootDirectory).Should().BeFalse();
    }

    [Fact]
    public void DeleteAll_RootDoesNotExist_DoesNotThrow()
    {
        NestedArchiveCache.DeleteAll();

        var act = () => NestedArchiveCache.DeleteAll();

        act.Should().NotThrow();
    }
}
