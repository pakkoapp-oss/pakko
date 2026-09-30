using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F280: Test reports a ZIP whose local file headers disagree with the central directory — the
/// ambiguity where different tools extract different names or data — as 7-Zip's Test does
/// ("Headers Error"). The corpus half matters as much: a legitimate archive must never be flagged.
/// </summary>
public sealed class ZipArchiveServiceTestHeadersTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string PlainZip(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach ((string entry, string content) in new[] { ("dir/a.txt", "alpha alpha alpha"), ("b.txt", "bravo"), ("c.txt", "charlie") })
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
                writer.Write(content);
            }
        }
        return path;
    }

    // Offset of the local header whose name is exactly entryName.
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

    private async Task<ArchiveResult> TestAsync(string path) => await _sut.TestAsync([path]);

    private static IEnumerable<ArchiveError> HeaderErrors(ArchiveResult result) =>
        result.Errors.Where(e => e.Text?.Code == MessageCode.LocalHeaderMismatch);

    [Fact]
    public async Task TestAsync_UntouchedArchive_NoHeaderError()
    {
        ArchiveResult result = await TestAsync(PlainZip("clean.zip"));

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_LocalCrcAndLocalNameFlipped_OneHeaderErrorNamingTheFirstEntry()
    {
        // The task's own fixture: one entry's local CRC and another entry's local name byte flipped;
        // data and central directory intact, so every entry still reads and verifies.
        string path = PlainZip("tampered.zip");
        byte[] bytes = File.ReadAllBytes(path);
        bytes[LocalHeaderOffset(bytes, "dir/a.txt") + 14] ^= 0xFF;
        bytes[LocalHeaderOffset(bytes, "b.txt") + 30] = (byte)'x';
        File.WriteAllBytes(path, bytes);

        ArchiveResult result = await TestAsync(path);

        ArchiveError error = HeaderErrors(result).Should().ContainSingle().Subject;
        error.Text!.Arguments.Select(a => a.ToString()).Should().Equal("2", "dir/a.txt");
    }

    [Fact]
    public async Task TestAsync_LocalCompressedSizeChanged_HeaderError()
    {
        string path = PlainZip("size.zip");
        byte[] bytes = File.ReadAllBytes(path);
        int offset = LocalHeaderOffset(bytes, "c.txt") + 18;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) + 1);
        File.WriteAllBytes(path, bytes);

        ArchiveResult result = await TestAsync(path);

        HeaderErrors(result).Should().ContainSingle();
    }

    [Fact]
    public async Task TestAsync_LocalHeaderOffsetPointsAtGarbage_HeaderErrorNotAnException()
    {
        string path = PlainZip("garbage.zip");
        byte[] bytes = File.ReadAllBytes(path);
        bytes[LocalHeaderOffset(bytes, "b.txt")] = 0; // the signature no longer matches
        File.WriteAllBytes(path, bytes);

        ArchiveResult result = await TestAsync(path);

        HeaderErrors(result).Should().ContainSingle();
    }

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.EnumerateFiles(FixtureHelper.ArchivesDir, "*.zip"))
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task TestAsync_RepoFixture_NeverAHeaderError(string fixture)
    {
        ArchiveResult result = await TestAsync(Path.Combine(FixtureHelper.ArchivesDir, fixture));

        HeaderErrors(result).Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_DataDescriptorArchive_NoHeaderError()
    {
        // ZipArchive on a non-seekable stream writes bit 3: local CRC and sizes are zero.
        string path = Path.Combine(_temp.Path, "descriptor.zip");
        using (var file = File.Create(path))
        using (var forwardOnly = new ForwardOnlyStream(file))
        using (var zip = new ZipArchive(forwardOnly, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("a.txt").Open());
            writer.Write("descriptor");
        }

        ArchiveResult result = await TestAsync(path);

        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal, null)]
    [InlineData(CompressionLevel.Fastest, null)]
    [InlineData(CompressionLevel.NoCompression, null)]
    [InlineData(CompressionLevel.Optimal, "s3cret-pass")]
    public async Task TestAsync_PakkoWrittenArchive_NoHeaderError(CompressionLevel level, string? password)
    {
        string source = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "text.txt"), string.Concat(Enumerable.Repeat("pakko ", 500)));
        byte[] random = new byte[300_000];
        new Random(280).NextBytes(random);
        File.WriteAllBytes(Path.Combine(source, "sub", "random.bin"), random); // Stored under T-F299
        File.WriteAllBytes(Path.Combine(source, "empty.txt"), []);
        ArchiveResult created = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = _temp.Path,
            ArchiveName = $"pakko-{level}-{password is not null}",
            CompressionLevel = level,
            ResolvePasswordAsync = password is null ? null : _ => Task.FromResult(new PasswordDecision { Password = password }),
        });
        created.Success.Should().BeTrue();

        ArchiveResult result = await _sut.TestAsync([created.CreatedFiles[0]],
            resolvePasswordAsync: password is null ? null : _ => Task.FromResult(new PasswordDecision { Password = password }));

        result.Errors.Should().BeEmpty();
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
