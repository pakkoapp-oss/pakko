namespace Archiver.Core.Tests.Helpers;

public static class FixtureHelper
{
    private static readonly string FixturesRoot = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "tests", "Archiver.Core.Tests", "Fixtures");

    public static string ArchivesDir => Path.GetFullPath(Path.Combine(FixturesRoot, "archives"));
    public static string FilesDir    => Path.GetFullPath(Path.Combine(FixturesRoot, "files"));

    /// <summary>
    /// Returns the path to a required fixture. Fails the test with a helpful message if missing.
    /// Use for auto-generated fixtures that must exist.
    /// </summary>
    public static string Archive(string name)
    {
        var path = Path.Combine(ArchivesDir, name);
        if (!File.Exists(path))
            throw new Exception($"Fixture not found: {name} — run: dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures");
        return path;
    }

    /// <summary>
    /// Returns the path to an optional fixture, or null if absent.
    /// Use for manual fixtures — tests should return early when null (skip gracefully).
    /// </summary>
    public static string? ArchiveOptional(string name)
    {
        var path = Path.Combine(ArchivesDir, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Returns the path to a required plain file fixture. Fails if missing.
    /// </summary>
    public static string PlainFile(string name)
    {
        var path = Path.Combine(FilesDir, name);
        if (!File.Exists(path))
            throw new Exception($"Fixture not found: {name} — run: dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures");
        return path;
    }
}
