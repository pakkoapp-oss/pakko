using System.IO.Compression;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// T-F413: 'pakko a -tgzip/-tbzip2/-txz' through the real exe and the real tar.exe (its raw
/// format). The bytes say enough: gzip decompresses back with .NET's GZipStream, bzip2 and xz
/// start with their own signatures and hold no tar header.
/// </summary>
public sealed class CliSingleFileStreamSubprocessTests : IAsyncLifetime
{
    private static readonly string Text = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 400));

    private readonly TestTempFolder _temp = new("pakko-cli-stream-");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _temp.DeleteAsync();

    private string Source(string name, string? text = null)
    {
        string folder = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, text ?? Text);
        return path;
    }

    private string Output(string name) => Path.Combine(_temp.Path, name);

    private static string Gunzip(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    // --- Happy path ---

    [RequiresTarExe]
    public void Gzip_OneFile_IsABareGzipOfItsBytes()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-tgzip", Output("out.gz"), Source("a.txt"));

        exitCode.Should().Be(0, stdErr);
        Gunzip(Output("out.gz")).Should().Be(Text, "no tar header: the stream is the file's bytes alone");
        new FileInfo(Output("out.gz")).Length.Should().BeLessThan(Text.Length);
    }

    [RequiresTarCapability("xz")]
    public void Xz_OneFile_StartsWithTheXzSignature()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-txz", Output("out.xz"), Source("a.txt"));

        exitCode.Should().Be(0, stdErr);
        File.ReadAllBytes(Output("out.xz")).AsSpan(0, 6).ToArray().Should().Equal(0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00);
    }

    [RequiresTarCapability("bz2")]
    public void Bzip2_OneFile_StartsWithTheBzip2Signature()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-tbzip2", Output("out.bz2"), Source("a.txt"));

        exitCode.Should().Be(0, stdErr);
        File.ReadAllBytes(Output("out.bz2")).AsSpan(0, 3).ToArray().Should().Equal((byte)'B', (byte)'Z', (byte)'h');
    }

    // patool's call: `7z a -y -txz -- <file>.xz <file>`; 7-Zip adds the extension to a bare name.
    [RequiresTarCapability("xz")]
    public void Xz_PatoolsCall_AndANameWithoutExtension()
    {
        (int patoolExit, _, string patoolErr) = CliProcessRunner.Run("a", "-y", "-txz", "--", Output("a.txt.xz"), Source("a.txt"));
        (int bareExit, _, string bareErr) = CliProcessRunner.Run("a", "-txz", Output("made"), Source("a.txt"));

        patoolExit.Should().Be(0, patoolErr);
        File.Exists(Output("a.txt.xz")).Should().BeTrue();
        bareExit.Should().Be(0, bareErr);
        File.Exists(Output("made.xz")).Should().BeTrue();
    }

    // 7-Zip's two-step tar.gz.
    [RequiresTarExe]
    public void Gzip_OfATar_NamedTarGz_IsAccepted()
    {
        string tar = Output("x.tar");
        (int tarExit, _, string tarErr) = CliProcessRunner.Run("a", "-ttar", tar, Source("a.txt"));
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-tgzip", Output("x.tar.gz"), tar);

        tarExit.Should().Be(0, tarErr);
        exitCode.Should().Be(0, stdErr);
        (int listExit, string listed, string listErr) = CliProcessRunner.Run("l", Output("x.tar.gz"));
        listExit.Should().Be(0, listErr);
        listed.Should().Contain("a.txt");
    }

    [RequiresTarExe]
    public void Gzip_EmptyFile_IsAValidEmptyStream()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-tgzip", Output("e.gz"), Source("e.txt", ""));

        exitCode.Should().Be(0, stdErr);
        Gunzip(Output("e.gz")).Should().BeEmpty();
    }

    // --- Misuse: as 7-Zip, two files or a folder are exit 2 and write nothing ---

    [RequiresTarExe]
    public void Gzip_TwoFiles_IsExitTwoAndWritesNothing()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-tgzip", Output("two.gz"), Source("a.txt"), Source("b.txt"));

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("gzip compresses a single file");
        File.Exists(Output("two.gz")).Should().BeFalse();
        Directory.GetFiles(_temp.Path).Should().BeEmpty("no temporary file is left either");
    }

    [RequiresTarExe]
    public void Xz_AFolder_IsExitTwoAndWritesNothing()
    {
        Source("a.txt");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-txz", Output("dir.xz"), Path.Combine(_temp.Path, "src"));

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("xz compresses a single file");
        File.Exists(Output("dir.xz")).Should().BeFalse();
    }

    [RequiresTarExe]
    public void Gzip_NameOfAnotherStream_IsExitSeven()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-tgzip", Output("out.xz"), Source("a.txt"));

        exitCode.Should().Be(7, stdErr);
        File.Exists(Output("out.xz")).Should().BeFalse();
    }
}
