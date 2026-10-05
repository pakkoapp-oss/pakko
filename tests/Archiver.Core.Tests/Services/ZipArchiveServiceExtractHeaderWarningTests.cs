using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F280, extraction half (user decision 2026-09-30): a ZIP whose local file headers disagree with
/// the central directory is extracted by the central directory, and the result carries a warning —
/// not an error (the extraction did what was asked) and not a skip (nothing was left out). A
/// legitimate archive must never be warned about.
/// </summary>
public sealed class ZipArchiveServiceExtractHeaderWarningTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string PlainZip(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string entry, string content) in new[] { ("dir/a.txt", "alpha alpha alpha"), ("b.txt", "bravo"), ("c.txt", "charlie") })
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }
        return path;
    }

    // One entry's local CRC and another entry's local name byte changed; data and central
    // directory intact, so every entry still reads and verifies.
    private string TamperedZip(string name)
    {
        string path = PlainZip(name);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[LocalHeaderOffset(bytes, "dir/a.txt") + 14] ^= 0xFF;
        bytes[LocalHeaderOffset(bytes, "b.txt") + 30] = (byte)'x';
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static int LocalHeaderOffset(byte[] bytes, string entryName)
    {
        byte[] name = Encoding.ASCII.GetBytes(entryName);
        for (int i = 0; i <= bytes.Length - 30 - name.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == 0x04034b50
                && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i + 26)) == name.Length
                && bytes.AsSpan(i + 30, name.Length).SequenceEqual(name))
                return i;
        }
        throw new InvalidOperationException($"No local header for {entryName}");
    }

    private async Task<(ArchiveResult Result, string Destination)> ExtractAsync(
        string archive, IReadOnlyList<string>? selected = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? password = null)
    {
        string destination = Path.Combine(_temp.Path, "x-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = destination,
            Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = selected,
            ResolvePasswordAsync = password,
        });
        return (result, destination);
    }

    [Fact]
    public async Task ExtractAsync_LocalHeadersDisagree_ExtractsByTheCentralDirectoryWithOneWarning()
    {
        string archive = TamperedZip("tampered.zip");

        (ArchiveResult result, string destination) = await ExtractAsync(archive);

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        ArchiveWarning warning = result.Warnings.Should().ContainSingle().Subject;
        warning.SourcePath.Should().Be(archive);
        warning.Text!.Code.Should().Be(MessageCode.LocalHeaderMismatch);
        warning.Text.Arguments.Select(a => a.ToString()).Should().Equal("2", "dir/a.txt");
        warning.Message.Should().Be(warning.Text.English);
        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(OperationOutcome.CompletedWithWarnings);
        result.FullyProcessedSources.Should().Equal([archive], "the archive was extracted in full");
        File.ReadAllText(Path.Combine(destination, "dir", "a.txt")).Should().Be("alpha alpha alpha");
        File.ReadAllText(Path.Combine(destination, "b.txt")).Should().Be("bravo", "the central directory names the file");
        File.Exists(Path.Combine(destination, "x.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_LocalHeadersDisagree_ASelectionGetsTheSameWarning()
    {
        (ArchiveResult result, string destination) = await ExtractAsync(TamperedZip("subset.zip"), selected: ["c.txt"]);

        result.Warnings.Should().ContainSingle();
        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destination, "c.txt")).Should().Be("charlie");
    }

    [Fact]
    public async Task ExtractAsync_TwoArchivesOneTampered_WarnsAboutThatOneOnly()
    {
        string clean = PlainZip("clean.zip");
        string tampered = TamperedZip("tampered.zip");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [clean, tampered],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ExtractMode.SeparateFolders,
        });

        result.Warnings.Should().ContainSingle().Which.SourcePath.Should().Be(tampered);
        result.CreatedFiles.Should().HaveCount(2);
    }

    // Nothing extracted means the errors speak; a warning about headers would only add noise.
    [Fact]
    public async Task ExtractAsync_LocalHeadersDisagreeButEverythingAlreadyExists_NoWarning()
    {
        string archive = TamperedZip("again.zip");
        string destination = Path.Combine(_temp.Path, "same");
        var options = new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = destination,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        };
        (await _sut.ExtractAsync(options)).Warnings.Should().ContainSingle();

        ArchiveResult second = await _sut.ExtractAsync(options);

        second.CreatedFiles.Should().BeEmpty();
        second.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_UntouchedArchive_NoWarning()
    {
        (ArchiveResult result, _) = await ExtractAsync(PlainZip("clean.zip"));

        result.Warnings.Should().BeEmpty();
        result.Outcome.Should().Be(OperationOutcome.Completed);
    }

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.EnumerateFiles(FixtureHelper.ArchivesDir, "*.zip"))
            data.Add(Path.GetFileName(path));
        return data;
    }

    // The fixtures include hostile and broken archives; whatever else happens to them, none has
    // local headers that disagree with its central directory (T-F280's Test half checks the same).
    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task ExtractAsync_RepoFixture_NeverAHeaderWarning(string fixture)
    {
        (ArchiveResult result, _) = await ExtractAsync(
            Path.Combine(FixtureHelper.ArchivesDir, fixture),
            password: _ => Task.FromResult(new PasswordDecision { Password = null }));

        result.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal, null)]
    [InlineData(CompressionLevel.Fastest, null)]
    [InlineData(CompressionLevel.NoCompression, null)]
    [InlineData(CompressionLevel.Optimal, "s3cret-pass")]
    public async Task ExtractAsync_PakkoWrittenArchive_NoWarning(CompressionLevel level, string? password)
    {
        string source = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "text.txt"), string.Concat(Enumerable.Repeat("pakko ", 500)));
        byte[] random = new byte[300_000];
        new Random(280).NextBytes(random);
        File.WriteAllBytes(Path.Combine(source, "sub", "random.bin"), random); // Stored under T-F299
        File.WriteAllBytes(Path.Combine(source, "empty.txt"), []);
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolver =
            password is null ? null : _ => Task.FromResult(new PasswordDecision { Password = password });
        ArchiveResult created = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = _temp.Path,
            ArchiveName = $"pakko-{level}-{password is not null}",
            CompressionLevel = level,
            ResolvePasswordAsync = resolver,
        });
        created.Success.Should().BeTrue();

        (ArchiveResult result, _) = await ExtractAsync(created.CreatedFiles[0], password: resolver);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.Outcome.Should().Be(OperationOutcome.Completed);
    }

    [Fact]
    public async Task ExtractAsync_DataDescriptorArchive_NoWarning()
    {
        // ZipArchive on a non-seekable stream writes bit 3: local CRC and sizes are zero.
        string path = Path.Combine(_temp.Path, "descriptor.zip");
        using (FileStream file = File.Create(path))
        using (var forwardOnly = new ForwardOnlyStream(file))
        using (var zip = new ZipArchive(forwardOnly, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("a.txt").Open());
            writer.Write("descriptor");
        }

        (ArchiveResult result, _) = await ExtractAsync(path);

        result.Warnings.Should().BeEmpty();
        result.Success.Should().BeTrue();
    }

    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
