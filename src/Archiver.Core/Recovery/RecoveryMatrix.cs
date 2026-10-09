using System.Runtime.InteropServices;

namespace Archiver.Core.Recovery;

/// <summary>Which recovery blocks a repair uses and how to combine them: missing slice j is
/// Σ_t Inverse[j][t] · (block ChosenBlocks[t] with the intact slices' share removed).</summary>
internal sealed record RecoverySolution(int[] ChosenBlocks, ushort[][] Inverse);

/// <summary>
/// Solves for the missing slices of a PAR 2.0 set (T-F275). PAR 2.0's code is not MDS: a set of
/// recovery blocks with non-contiguous exponents can be linearly dependent for some missing slices,
/// so taking the first k blocks, as par2cmdline does, fails on sets another choice solves. This
/// takes the blocks in exponent order and keeps each one that is independent of those kept so far,
/// until it has one per missing slice — so it fails only when no choice of blocks can succeed.
/// </summary>
internal static class RecoveryMatrix
{
    /// <param name="constants">The input constant of every slice of the set.</param>
    /// <param name="missing">The indices of the missing slices.</param>
    /// <param name="exponents">The exponents of the available recovery blocks, ascending.</param>
    /// <param name="cancellationToken">Checked once per candidate block.</param>
    /// <returns>Null when the available blocks span fewer than <c>missing.Count</c> dimensions.</returns>
    internal static RecoverySolution? Solve(ushort[] constants, IReadOnlyList<int> missing, IReadOnlyList<uint> exponents, CancellationToken cancellationToken)
    {
        int m = missing.Count;
        // Each kept row: [coefficients over the missing slices | combination of the kept blocks],
        // reduced against the rows kept before it and scaled to 1 at its pivot column.
        var basis = new List<ushort[]>(m);
        var pivots = new List<int>(m);
        var chosen = new List<int>(m);
        for (int candidate = 0; candidate < exponents.Count && basis.Count < m; candidate++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = new ushort[2 * m];
            for (int j = 0; j < m; j++)
                row[j] = Gf16.Pow(constants[missing[j]], exponents[candidate]);
            row[m + basis.Count] = 1;
            for (int b = 0; b < basis.Count; b++)
                Eliminate(row, basis[b], pivots[b]);

            int pivot = Array.FindIndex(row, 0, m, value => value != 0);
            if (pivot < 0)
                continue; // dependent on the blocks already kept
            Scale(row, Gf16.Inv(row[pivot]));
            basis.Add(row);
            pivots.Add(pivot);
            chosen.Add(candidate);
        }
        if (basis.Count < m)
            return null;

        // Back substitution: a row has zeros at the pivots of the rows kept before it, so clearing
        // each pivot column from the earlier rows, last row first, leaves one 1 per row.
        for (int b = m - 1; b >= 0; b--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int earlier = 0; earlier < b; earlier++)
                Eliminate(basis[earlier], basis[b], pivots[b]);
        }

        var inverse = new ushort[m][];
        for (int b = 0; b < m; b++)
            inverse[pivots[b]] = basis[b][m..];
        return new RecoverySolution([.. chosen], inverse);
    }

    // row -= row[pivot] · source, which has 1 at pivot.
    private static void Eliminate(ushort[] row, ushort[] source, int pivot)
    {
        ushort factor = row[pivot];
        if (factor != 0)
            Gf16Region.MulAdd(factor, MemoryMarshal.AsBytes(source.AsSpan()), MemoryMarshal.AsBytes(row.AsSpan()));
    }

    private static void Scale(ushort[] row, ushort factor)
    {
        for (int k = 0; k < row.Length; k++)
            row[k] = Gf16.Mul(row[k], factor);
    }
}
