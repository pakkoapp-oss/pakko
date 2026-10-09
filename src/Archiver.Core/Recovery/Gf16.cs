namespace Archiver.Core.Recovery;

/// <summary>
/// Arithmetic in GF(2^16) with the generator polynomial PAR 2.0 fixes (x^16 + x^12 + x^3 + x + 1,
/// 0x1100B), through log/exp tables (T-F275). Addition in the field is XOR.
/// </summary>
internal static class Gf16
{
    /// <summary>The order of the multiplicative group: 2^16 - 1.</summary>
    internal const int Order = 65535;

    private const int Generator = 0x1100B;

    // Twice the group order, so Exp[Log[a] + Log[b]] needs no reduction.
    private static readonly ushort[] Exp = BuildExp();
    private static readonly ushort[] Log = BuildLog(Exp);

    internal static ushort Mul(ushort a, ushort b) =>
        a == 0 || b == 0 ? (ushort)0 : Exp[Log[a] + Log[b]];

    internal static ushort Inv(ushort a)
    {
        ArgumentOutOfRangeException.ThrowIfZero(a);
        return Exp[Order - Log[a]];
    }

    internal static ushort Pow(ushort a, uint exponent)
    {
        if (exponent == 0)
            return 1;
        if (a == 0)
            return 0;
        long log = (long)Log[a] * (exponent % Order) % Order;
        return Exp[log];
    }

    /// <summary>
    /// The constant each input slice is multiplied by: 2^n for the n-th exponent n ≥ 0 with
    /// gcd(n, 65535) = 1, in order (the specification's "Reed-Solomon coding" section).
    /// </summary>
    internal static ushort[] InputConstants(int count)
    {
        var constants = new ushort[count];
        int found = 0;
        for (int n = 0; found < count; n++)
        {
            if (n % 3 != 0 && n % 5 != 0 && n % 17 != 0 && n % 257 != 0)
                constants[found++] = Exp[n];
        }
        return constants;
    }

    private static ushort[] BuildExp()
    {
        var exp = new ushort[2 * Order];
        int x = 1;
        for (int i = 0; i < Order; i++)
        {
            exp[i] = (ushort)x;
            exp[i + Order] = (ushort)x;
            x <<= 1;
            if ((x & 0x10000) != 0)
                x ^= Generator;
        }
        return exp;
    }

    private static ushort[] BuildLog(ushort[] exp)
    {
        var log = new ushort[Order + 1];
        for (int i = 0; i < Order; i++)
            log[exp[i]] = (ushort)i;
        return log;
    }
}
