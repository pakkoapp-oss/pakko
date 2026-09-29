namespace Archiver.Core.Tests.Fuzz;

/// <summary>
/// Drives one fuzz target over mutations of one seed input (T-F240). The default budget keeps the
/// ordinary test run short; the nightly canary raises it through environment variables:
/// <list type="bullet">
/// <item><c>PAKKO_FUZZ_ITERATIONS</c> — mutations per seed input (default 10).</item>
/// <item><c>PAKKO_FUZZ_SEED</c> — base seed (default fixed, so a plain test run is reproducible).</item>
/// <item><c>PAKKO_FUZZ_ONLY_ITERATION</c> — replays a single iteration from a failure message.</item>
/// <item><c>PAKKO_FUZZ_OUTPUT</c> — where inputs are saved (default <c>%TEMP%\pakko-fuzz</c>).</item>
/// </list>
/// Every input is written to disk before it runs, so a crash or a hang that never returns still
/// leaves the input behind (<c>current-*.bin</c>); a caught failure is kept as <c>failure-*.bin</c>.
/// </summary>
internal static class FuzzRunner
{
    private static readonly TimeSpan IterationTimeout = TimeSpan.FromSeconds(30);

    public static int Iterations => ReadInt("PAKKO_FUZZ_ITERATIONS") ?? 10;
    public static int BaseSeed => ReadInt("PAKKO_FUZZ_SEED") ?? 20260929;
    public static string OutputDir =>
        Environment.GetEnvironmentVariable("PAKKO_FUZZ_OUTPUT") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Path.GetTempPath(), "pakko-fuzz");

    /// <param name="target">Short target name, used in file names and failure messages.</param>
    /// <param name="seedName">The seed input's name (a fixture file name).</param>
    /// <param name="seedInput">The unmutated input.</param>
    /// <param name="runAsync">Runs one input; throws <see cref="FuzzViolation"/> or anything
    /// unexpected to report a failure.</param>
    public static async Task RunAsync(string target, string seedName, byte[] seedInput, Func<byte[], Task> runAsync)
    {
        Directory.CreateDirectory(OutputDir);
        string currentPath = Path.Combine(OutputDir, $"current-{target}.bin");
        int? only = ReadInt("PAKKO_FUZZ_ONLY_ITERATION");

        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            if (only is not null && only != iteration)
                continue;

            int caseSeed = CaseSeed(seedName, iteration);
            var log = new List<string>();
            byte[] input = ByteMutator.Mutate(seedInput, new Random(caseSeed), log);
            await File.WriteAllBytesAsync(currentPath, input);

            try
            {
                await Task.Run(() => runAsync(input)).WaitAsync(IterationTimeout);
            }
            catch (Exception ex)
            {
                string savedPath = Path.Combine(OutputDir, $"failure-{target}-{Path.GetFileNameWithoutExtension(seedName)}-{iteration}.bin");
                await File.WriteAllBytesAsync(savedPath, input);
                throw new FuzzViolation(
                    $"{target} failed on {seedName}, PAKKO_FUZZ_SEED={BaseSeed} PAKKO_FUZZ_ONLY_ITERATION={iteration} " +
                    $"(mutations: {string.Join(", ", log)}); input saved to {savedPath}", ex);
            }
        }

        File.Delete(currentPath);
    }

    // Stable across processes (string.GetHashCode is randomized per process), and independent per
    // iteration, so one case replays without the ones before it.
    private static int CaseSeed(string seedName, int iteration)
    {
        uint hash = 2166136261;
        foreach (char c in seedName)
            hash = (hash ^ c) * 16777619;
        return unchecked((int)hash ^ (BaseSeed * 486187739) ^ (iteration * 16777619));
    }

    private static int? ReadInt(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : null;
}
