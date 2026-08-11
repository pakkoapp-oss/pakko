using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class DestinationConflictResolverTests
{
    private const string DestPath = @"C:\Dest\archive.zip";
    private const string RenamedPath = @"C:\Dest\archive (1).zip";

    // One Fact per reachable (onDiskConflict, sameRunConflict, ConflictBehavior) combination —
    // not a [Theory]/[InlineData] over DestinationConflictOutcome directly, since it's internal
    // and hits CS0051 on xUnit's required-public test method (same rule as
    // ExtractionDestinationPlannerTests/RootShape, T-F157).

    [Fact]
    public async Task ResolveAsync_NoConflict_ProceedsWithoutInvokingResolver()
    {
        // Guard: proves the short-circuit branch never awaits the resolver at all when there is
        // no conflict — provable from the line, not just by reading the `if`.
        var resolver = new ConflictResolver(ConflictBehavior.Ask,
            _ => throw new InvalidOperationException("must not be invoked when there is no conflict"));

        var (outcome, resolvedDestPath) = await DestinationConflictResolver.ResolveAsync(
            DestPath, onDiskConflict: false, sameRunConflict: false, resolver, ThrowingRenameCandidate);

        outcome.Should().Be(DestinationConflictOutcome.Proceed);
        resolvedDestPath.Should().Be(DestPath);
    }

    [Fact]
    public async Task ResolveAsync_OnDiskOnly_Skip_ReturnsSkip()
    {
        await AssertResolveAsync(onDiskConflict: true, sameRunConflict: false, ConflictBehavior.Skip,
            DestinationConflictOutcome.Skip, DestPath);
    }

    [Fact]
    public async Task ResolveAsync_OnDiskOnly_Overwrite_DeletesExisting()
    {
        await AssertResolveAsync(onDiskConflict: true, sameRunConflict: false, ConflictBehavior.Overwrite,
            DestinationConflictOutcome.ProceedAfterDeletingExisting, DestPath);
    }

    [Fact]
    public async Task ResolveAsync_OnDiskOnly_Rename_ProceedsRenamed()
    {
        await AssertResolveAsync(onDiskConflict: true, sameRunConflict: false, ConflictBehavior.Rename,
            DestinationConflictOutcome.Proceed, RenamedPath);
    }

    [Fact]
    public async Task ResolveAsync_SameRunOnly_Skip_ReturnsSkip()
    {
        await AssertResolveAsync(onDiskConflict: false, sameRunConflict: true, ConflictBehavior.Skip,
            DestinationConflictOutcome.Skip, DestPath);
    }

    [Fact]
    public async Task ResolveAsync_SameRunOnly_Overwrite_RenamesInsteadOfDeleting()
    {
        // The key differentiator this task exists for: a same-run collision under Overwrite
        // renames, it never deletes — there may be nothing on disk yet to delete (another
        // in-flight worker owns creating it).
        await AssertResolveAsync(onDiskConflict: false, sameRunConflict: true, ConflictBehavior.Overwrite,
            DestinationConflictOutcome.Proceed, RenamedPath);
    }

    [Fact]
    public async Task ResolveAsync_SameRunOnly_Rename_ProceedsRenamed()
    {
        await AssertResolveAsync(onDiskConflict: false, sameRunConflict: true, ConflictBehavior.Rename,
            DestinationConflictOutcome.Proceed, RenamedPath);
    }

    // Both flags true is unreachable in production today (see DECISIONS.md's T-F158 entry — Zip's
    // pre-pass loop is synchronous, so an on-disk Overwrite delete always completes before the
    // next same-basename source is evaluated), but Resolve still defines an answer for it.

    [Fact]
    public async Task ResolveAsync_BothConflicts_Skip_ReturnsSkip()
    {
        await AssertResolveAsync(onDiskConflict: true, sameRunConflict: true, ConflictBehavior.Skip,
            DestinationConflictOutcome.Skip, DestPath);
    }

    [Fact]
    public async Task ResolveAsync_BothConflicts_Overwrite_RenamesInsteadOfDeleting()
    {
        await AssertResolveAsync(onDiskConflict: true, sameRunConflict: true, ConflictBehavior.Overwrite,
            DestinationConflictOutcome.Proceed, RenamedPath);
    }

    private static async Task AssertResolveAsync(
        bool onDiskConflict, bool sameRunConflict, ConflictBehavior configured,
        DestinationConflictOutcome expectedOutcome, string expectedDestPath)
    {
        var resolver = new ConflictResolver(configured, resolveConflictAsync: null);

        var (outcome, resolvedDestPath) = await DestinationConflictResolver.ResolveAsync(
            DestPath, onDiskConflict, sameRunConflict, resolver, RenameCandidate);

        outcome.Should().Be(expectedOutcome);
        resolvedDestPath.Should().Be(expectedDestPath);
    }

    private static string RenameCandidate(string path) => RenamedPath;

    private static string ThrowingRenameCandidate(string path) =>
        throw new InvalidOperationException("must not be invoked when there is no conflict");
}
