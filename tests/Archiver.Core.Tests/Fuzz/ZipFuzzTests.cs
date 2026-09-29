using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Zip.Decryption;
using Archiver.Core.Tests.Helpers;

namespace Archiver.Core.Tests.Fuzz;

/// <summary>
/// T-F240: mutational fuzzing of the parsers that read untrusted archive bytes — the raw
/// central-directory/Zip64 locator, the ZipCrypto and WinZip AES readers (reached through
/// Test/Extract with the fixtures' password), and the public "never throws" contract of
/// ListEntriesAsync/TestAsync/ExtractAsync. Seed inputs are the real fixture archives.
/// See <see cref="FuzzRunner"/> for the budget knobs the nightly canary raises.
/// </summary>
[Trait("Category", "Fuzz")]
public sealed class ZipFuzzTests : IDisposable
{
    private const string FixturePassword = "testpassword";
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // The EICAR fixture is left out: extracting it would hand real-time antivirus a test virus.
    public static TheoryData<string> SeedArchives()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.GetFiles(FixtureHelper.ArchivesDir, "*.zip").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (!name.Contains("eicar", StringComparison.OrdinalIgnoreCase))
                data.Add(name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SeedArchives))]
    public Task LocateAll_MutatedArchive_ThrowsOnlyWhatCallersCatch(string seed) =>
        FuzzRunner.RunAsync("locate", seed, File.ReadAllBytes(FixtureHelper.Archive(seed)), input =>
        {
            // Every caller (listing, Test, Extract) catches IOException and InvalidDataException
            // around this parser; anything else would escape the engines' "never throws" contract.
            try
            {
                using var stream = new MemoryStream(input, writable: false);
                RawZipEntryLocator.LocateAll(stream);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                // expected for a damaged archive
            }
            return Task.CompletedTask;
        });

    [Theory]
    [MemberData(nameof(SeedArchives))]
    public Task ListEntriesAsync_MutatedArchive_NeverThrows(string seed) =>
        FuzzRunner.RunAsync("list", seed, File.ReadAllBytes(FixtureHelper.Archive(seed)), async input =>
        {
            string path = WriteInput(input);
            await _sut.ListEntriesAsync(path);
        });

    [Theory]
    [MemberData(nameof(SeedArchives))]
    public Task TestAsync_MutatedArchive_NeverThrows(string seed) =>
        FuzzRunner.RunAsync("test", seed, File.ReadAllBytes(FixtureHelper.Archive(seed)), async input =>
        {
            string path = WriteInput(input);
            await _sut.TestAsync([path], resolvePasswordAsync: FixedPassword);
        });

    [Theory]
    [MemberData(nameof(SeedArchives))]
    public Task ExtractAsync_MutatedArchive_NeverThrowsOrWritesOutsideDestination(string seed) =>
        FuzzRunner.RunAsync("extract", seed, File.ReadAllBytes(FixtureHelper.Archive(seed)), async input =>
        {
            string sandbox = Path.Combine(_temp.Path, Path.GetRandomFileName());
            string inputDir = Path.Combine(sandbox, "in");
            string destination = Path.Combine(sandbox, "out", "dest");
            Directory.CreateDirectory(inputDir);
            Directory.CreateDirectory(destination);
            string archivePath = Path.Combine(inputDir, "input.zip");
            await File.WriteAllBytesAsync(archivePath, input);

            try
            {
                await _sut.ExtractAsync(new ExtractOptions
                {
                    ArchivePaths = [archivePath],
                    DestinationFolder = destination,
                    // A mutated size field must not make a nightly run write gigabytes.
                    ConfirmCompressionBombExtraction = _ => Task.FromResult(false),
                    ResolvePasswordAsync = FixedPassword,
                });

                string[] outside = Directory.EnumerateFileSystemEntries(sandbox, "*", SearchOption.AllDirectories)
                    .Where(p => !IsUnder(p, inputDir) && !IsUnder(p, destination) && p != Path.Combine(sandbox, "out"))
                    .ToArray();
                if (outside.Length > 0)
                    throw new FuzzViolation($"Extraction wrote outside the destination: {string.Join(", ", outside)}");
            }
            finally
            {
                Directory.Delete(sandbox, recursive: true);
            }
        });

    [Fact]
    public Task LaunchArgumentsTryParse_MutatedArguments_NeverThrows()
    {
        // T-F232 replaced the pakko:// URI router with these activation arguments; they are the
        // App's only input that arrives from another process.
        string valid = LaunchArguments.Format(LaunchOperation.Extract, [@"C:\a b\archive.zip", "D:\\\u0444\u0430\u0439\u043b.zip"]);
        return FuzzRunner.RunAsync("launch-args", "launch-args", Encoding.UTF8.GetBytes(valid), input =>
        {
            if (LaunchArguments.TryParse(Encoding.UTF8.GetString(input), out _, out IReadOnlyList<string> files)
                && files.Any(string.IsNullOrWhiteSpace))
                throw new FuzzViolation("TryParse returned a blank file path.");
            return Task.CompletedTask;
        });
    }

    private string WriteInput(byte[] input)
    {
        string path = Path.Combine(_temp.Path, Path.GetRandomFileName() + ".zip");
        File.WriteAllBytes(path, input);
        return path;
    }

    private static Task<PasswordDecision> FixedPassword(PasswordPromptInfo _) =>
        Task.FromResult(new PasswordDecision { Password = FixturePassword });

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
}
