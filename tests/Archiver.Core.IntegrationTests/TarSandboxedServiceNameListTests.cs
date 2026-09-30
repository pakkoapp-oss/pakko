using System.Formats.Tar;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F273/T-F283: names reach tar.exe as data, never as its own options, and a selection of any
/// length fits. Option-like names used here only select or exclude files — none names a program.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceNameListTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static List<string> EntryNames(string tarPath)
    {
        var names = new List<string>();
        using var reader = new TarReader(File.OpenRead(tarPath));
        while (reader.GetNextEntry() is { } entry)
            names.Add(entry.Name.TrimStart('.', '/'));
        return names;
    }

    private async Task<(ArchiveResult Result, string TarPath)> CompressAsync(IReadOnlyList<string> sources)
    {
        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.Tar,
        });
        return (result, Path.Combine(_temp.Path, "out.tar"));
    }

    [Integration]
    public async Task CompressAsync_OptionLikeSourceNames_AreArchivedAsFiles()
    {
        string dir = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(dir);
        string[] names = ["--exclude=real.txt", "-v", "-T", "-C", "@list.tar", "real.txt"];
        foreach (string name in names)
            File.WriteAllText(Path.Combine(dir, name), name);

        (ArchiveResult result, string tarPath) = await CompressAsync([.. names.Select(n => Path.Combine(dir, n))]);

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        EntryNames(tarPath).Should().BeEquivalentTo(names);
    }

    [Integration]
    public async Task CompressAsync_SelectionLongerThanACommandLine_ArchivesEveryFile()
    {
        string dir = Path.Combine(_temp.Path, "many");
        Directory.CreateDirectory(dir);
        var sources = new List<string>();
        for (int i = 0; i < 300; i++)
        {
            string path = Path.Combine(dir, $"{i:D3}_" + new string('n', 110) + ".txt");
            File.WriteAllText(path, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sources.Add(path);
        }
        sources.Sum(s => s.Length + 3).Should().BeGreaterThan(32_767);

        (ArchiveResult result, string tarPath) = await CompressAsync(sources);

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        EntryNames(tarPath).Should().HaveCount(300);
    }

    [Integration]
    public async Task CompressAsync_NonAsciiAndBracketNames_RoundTrip()
    {
        // Cyrillic on a uk/ru machine, Latin-1 on an en-US one (the CI runner).
        string? name = TarCodePage.PortableNonAsciiName();
        if (name is null)
            return; // this machine's code pages share no candidate
        string folder = Path.GetFileNameWithoutExtension(name) + " dir";
        string dir = Path.Combine(_temp.Path, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), "1");
        string bracket = Path.Combine(_temp.Path, "a[1].txt");
        File.WriteAllText(bracket, "2");

        (ArchiveResult result, string tarPath) = await CompressAsync([dir, bracket]);

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)) + " " + TarCodePage.Describe());
        EntryNames(tarPath).Should().Contain([$"{folder}/{name}", "a[1].txt"], TarCodePage.Describe());
    }

    [Integration]
    public async Task ExtractAsync_SelectedEntryWithOptionLikeName_ExtractsOnlyThatEntry()
    {
        string tarPath = Path.Combine(_temp.Path, "in.tar");
        using (var writer = new TarWriter(File.Create(tarPath)))
        {
            foreach (string name in new[] { "--exclude=other.txt", "other.txt", "b.txt" })
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream("x"u8.ToArray()) };
                writer.WriteEntry(entry);
            }
        }
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [tarPath],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = ["--exclude=other.txt"],
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Select(Path.GetFileName)
            .Should().BeEquivalentTo(["--exclude=other.txt"]);
    }
}
