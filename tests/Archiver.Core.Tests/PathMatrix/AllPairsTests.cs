using FluentAssertions;

namespace Archiver.Core.Tests.PathMatrix;

// T-F512: the generator's own promise, checked on a constrained example shaped like the disk matrix.
public sealed class AllPairsTests
{
    private static readonly int[] Sizes = [9, 12, 2, 2, 6];

    // Value 0 of the first dimension cannot meet value 1 of the fourth (an input-only name has no
    // twin); the last value of the fifth cannot meet position 1 (a file role in the middle).
    private static bool Allowed(int?[] c) =>
        !(c[0] == 0 && c[3] == 1) && !(c[4] == 5 && c[2] == 1);

    [Fact]
    public void Generate_CoversEveryAllowedPair_WithAllowedCasesOnly()
    {
        List<int[]> cases = AllPairs.Generate(Sizes, Allowed);

        HashSet<(int, int, int, int)> covered = [.. cases.SelectMany(AllPairs.PairsOf)];
        AllPairs.ValidPairs(Sizes, Allowed).Should().BeSubsetOf(covered);
        cases.Where(c => !Allowed(c.Select(v => (int?)v).ToArray())).Should().BeEmpty();
    }

    [Fact]
    public void Generate_IsFarSmallerThanTheProduct_AndTheSameEveryRun()
    {
        List<int[]> first = AllPairs.Generate(Sizes, Allowed);
        List<int[]> second = AllPairs.Generate(Sizes, Allowed);

        first.Count.Should().BeLessThan(9 * 12 * 2 * 2 * 6 / 10);
        first.Should().BeEquivalentTo(second, o => o.WithStrictOrdering());
    }

    [Fact]
    public void ValidPairs_LeavesOutWhatTheConstraintForbids()
    {
        AllPairs.ValidPairs(Sizes, Allowed).Should().NotContain((0, 0, 3, 1)).And.NotContain((2, 1, 4, 5));
    }
}
