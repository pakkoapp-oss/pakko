namespace Archiver.Core.Tests.PathMatrix;

/// <summary>
/// T-F512: all-pairs (pairwise) combinations, deterministic, no library. Every pair of values from
/// any two dimensions that the constraint allows appears in at least one case, with far fewer cases
/// than the full product. The constraint judges a partial case (unset dimensions are null) and must
/// be pairwise: whether two values may meet never depends on a third. A pair it forbids is not
/// counted as covered, so the coverage claim stays true (a case a test would skip covers nothing).
/// </summary>
internal static class AllPairs
{
    /// <param name="sizes">The number of values in each dimension.</param>
    /// <param name="allowed">Whether a partial case may stand; null entries are unset.</param>
    /// <returns>Each case as one value index per dimension.</returns>
    public static List<int[]> Generate(IReadOnlyList<int> sizes, Func<int?[], bool> allowed)
    {
        HashSet<(int D1, int V1, int D2, int V2)> uncovered = ValidPairs(sizes, allowed);
        var cases = new List<int[]>();
        while (uncovered.Count > 0)
        {
            // Seeded with the first uncovered pair in a fixed order, so the cases never vary.
            (int d1, int v1, int d2, int v2) = uncovered.Order().First();
            int?[] partial = new int?[sizes.Count];
            partial[d1] = v1;
            partial[d2] = v2;
            for (int d = 0; d < sizes.Count; d++)
            {
                if (partial[d] is not null)
                    continue;
                partial[d] = BestValue(d, sizes[d], partial, uncovered, allowed)
                    ?? throw new InvalidOperationException($"no value of dimension {d} fits the case: the constraint is not pairwise");
            }
            int[] chosen = [.. partial.Select(v => v!.Value)];
            foreach ((int, int, int, int) pair in PairsOf(chosen))
                uncovered.Remove(pair);
            cases.Add(chosen);
        }
        return cases;
    }

    /// <summary>Every pair the constraint allows, as (lower dimension, its value, higher dimension, its value).</summary>
    public static HashSet<(int D1, int V1, int D2, int V2)> ValidPairs(IReadOnlyList<int> sizes, Func<int?[], bool> allowed)
    {
        var pairs = new HashSet<(int, int, int, int)>();
        for (int d1 = 0; d1 < sizes.Count; d1++)
            for (int d2 = d1 + 1; d2 < sizes.Count; d2++)
                for (int v1 = 0; v1 < sizes[d1]; v1++)
                    for (int v2 = 0; v2 < sizes[d2]; v2++)
                    {
                        int?[] partial = new int?[sizes.Count];
                        partial[d1] = v1;
                        partial[d2] = v2;
                        if (allowed(partial))
                            pairs.Add((d1, v1, d2, v2));
                    }
        return pairs;
    }

    public static IEnumerable<(int D1, int V1, int D2, int V2)> PairsOf(int[] chosen)
    {
        for (int d1 = 0; d1 < chosen.Length; d1++)
            for (int d2 = d1 + 1; d2 < chosen.Length; d2++)
                yield return (d1, chosen[d1], d2, chosen[d2]);
    }

    // The allowed value that covers the most uncovered pairs with what is already set; the lowest on a tie.
    private static int? BestValue(int dimension, int size, int?[] partial, HashSet<(int, int, int, int)> uncovered, Func<int?[], bool> allowed)
    {
        int? best = null;
        int bestGain = -1;
        for (int value = 0; value < size; value++)
        {
            partial[dimension] = value;
            if (allowed(partial))
            {
                int gain = 0;
                for (int other = 0; other < partial.Length; other++)
                {
                    if (other == dimension || partial[other] is not { } set)
                        continue;
                    (int, int, int, int) pair = other < dimension ? (other, set, dimension, value) : (dimension, value, other, set);
                    if (uncovered.Contains(pair))
                        gain++;
                }
                if (gain > bestGain)
                {
                    best = value;
                    bestGain = gain;
                }
            }
            partial[dimension] = null;
        }
        return best;
    }
}
