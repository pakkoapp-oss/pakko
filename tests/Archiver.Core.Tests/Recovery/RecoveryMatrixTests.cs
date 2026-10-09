using Archiver.Core.Recovery;
using FluentAssertions;

namespace Archiver.Core.Tests.Recovery;

// T-F275: choosing recovery blocks and inverting. PAR 2.0's code is not MDS: with slices
// [2, 48, 237] missing, the blocks with exponents {1, 2, 4} are linearly dependent (rank 2), so
// par2cmdline, which takes the first k blocks, fails on {1, 2, 4, 5} although {1, 2, 5} solves it.
public sealed class RecoveryMatrixTests
{
    private static readonly ushort[] Constants = Gf16.InputConstants(300);

    [Fact]
    public void Solve_DependentFirstBlocksButASpareOne_ChoosesAroundThem()
    {
        RecoverySolution? solution = RecoveryMatrix.Solve(Constants, [2, 48, 237], [1, 2, 4, 5], CancellationToken.None);

        solution.Should().NotBeNull();
        AssertInverse(solution!, [2, 48, 237], [1, 2, 4, 5]);
    }

    [Fact]
    public void Solve_OnlyTheDependentBlocks_IsNull()
    {
        RecoveryMatrix.Solve(Constants, [2, 48, 237], [1, 2, 4], CancellationToken.None).Should().BeNull();
    }

    [Fact]
    public void Solve_FewerBlocksThanMissingSlices_IsNull()
    {
        RecoveryMatrix.Solve(Constants, [1, 2, 3], [0, 1], CancellationToken.None).Should().BeNull();
    }

    [Fact]
    public void Solve_ContiguousExponents_AlwaysSolvable()
    {
        var random = new Random(275);
        for (int trial = 0; trial < 50; trial++)
        {
            int count = random.Next(1, 40);
            int[] missing = Enumerable.Range(0, Constants.Length).OrderBy(_ => random.Next()).Take(count).ToArray();
            uint first = (uint)random.Next(0, 1000);
            uint[] exponents = Enumerable.Range(0, count).Select(i => first + (uint)i).ToArray();

            RecoverySolution? solution = RecoveryMatrix.Solve(Constants, missing, exponents, CancellationToken.None);

            solution.Should().NotBeNull($"trial {trial}");
            AssertInverse(solution!, missing, exponents);
        }
    }

    [Fact]
    public void Solve_DuplicateExponentModuloTheGroupOrder_IsSkipped()
    {
        // 65535 ≡ 0: the same row twice.
        RecoverySolution? solution = RecoveryMatrix.Solve(Constants, [3, 7], [0, 65535, 1], CancellationToken.None);

        solution!.ChosenBlocks.Should().Equal(0, 2);
    }

    [Fact]
    public void Solve_NothingMissing_IsEmpty()
    {
        RecoveryMatrix.Solve(Constants, [], [0, 1], CancellationToken.None)!.ChosenBlocks.Should().BeEmpty();
    }

    [Fact]
    public void Solve_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Action act = () => RecoveryMatrix.Solve(Constants, [1], [0], cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    // Inverse · A = I, where A[t][j] = constant(missing j) ^ exponent(chosen t).
    private static void AssertInverse(RecoverySolution solution, int[] missing, uint[] exponents)
    {
        int m = missing.Length;
        solution.ChosenBlocks.Should().HaveCount(m).And.OnlyHaveUniqueItems();
        for (int j = 0; j < m; j++)
        {
            for (int column = 0; column < m; column++)
            {
                ushort sum = 0;
                for (int t = 0; t < m; t++)
                    sum ^= Gf16.Mul(solution.Inverse[j][t], Gf16.Pow(Constants[missing[column]], exponents[solution.ChosenBlocks[t]]));
                sum.Should().Be((ushort)(j == column ? 1 : 0));
            }
        }
    }
}
