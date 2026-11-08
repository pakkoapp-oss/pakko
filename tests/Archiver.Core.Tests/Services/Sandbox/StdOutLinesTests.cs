using System.Text;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F470: an untrusted child's stdout is taken line by line, within a line and a total limit.
public sealed class StdOutLinesTests
{
    [Fact]
    public async Task LinesSplitAcrossReads_ArriveWhole()
    {
        var lines = new List<string>();
        using var stream = new TrickleStream(Encoding.ASCII.GetBytes("V 41\nE 0\n\nD\n"), 3);

        await SandboxedProcessLauncher.ReadLinesWithinAsync(stream,
            new StdOutLines(l => lines.Add(Encoding.ASCII.GetString(l)), 16, 100), CancellationToken.None);

        lines.Should().Equal("V 41", "E 0", "", "D");
    }

    [Fact]
    public async Task ALineAtTheLimit_Passes_AndOneByteMore_Fails()
    {
        await Read("12345678\n", maxLine: 8, maxTotal: 100);

        Func<Task> overlong = () => Read("123456789\n", maxLine: 8, maxTotal: 100);
        await overlong.Should().ThrowAsync<IOException>().WithMessage("*longer than 8*");
    }

    [Fact]
    public async Task OutputPastTheTotal_Fails()
    {
        await Read("ab\ncd\n", maxLine: 8, maxTotal: 6);

        Func<Task> tooMuch = () => Read("ab\ncd\ne\n", maxLine: 8, maxTotal: 6);
        await tooMuch.Should().ThrowAsync<IOException>().WithMessage("*more than 6 bytes*");
    }

    [Fact]
    public async Task OutputEndingInsideALine_Fails()
    {
        Func<Task> cut = () => Read("D\nE 0", maxLine: 8, maxTotal: 100);

        await cut.Should().ThrowAsync<IOException>().WithMessage("*inside a line*");
    }

    [Fact]
    public async Task TheHandlersException_StopsTheRead()
    {
        int seen = 0;
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("a\nb\nc\n"));
        Func<Task> read = () => SandboxedProcessLauncher.ReadLinesWithinAsync(stream,
            new StdOutLines(_ => { if (++seen == 2) throw new InvalidDataException("bad"); }, 8, 100), CancellationToken.None);

        await read.Should().ThrowAsync<InvalidDataException>();
        seen.Should().Be(2);
    }

    // A child killed for its output must be gone: a real process that writes a line too long.
    [Fact]
    public async Task AChildBreakingTheLimit_IsKilled()
    {
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Func<Task> run = () => SandboxedProcessLauncher.RunAsync(cmd,
            ["/c", "for /L %i in (1,1,200000) do @echo 0123456789012345678901234567890123456789"],
            new ProcessLaunchOptions(StdOutLines: new StdOutLines(_ => { }, 8, 1L << 30)), CancellationToken.None);

        (await run.Should().ThrowAsync<IOException>()).WithMessage("*longer than 8*");
    }

    private static async Task Read(string text, int maxLine, long maxTotal)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));
        await SandboxedProcessLauncher.ReadLinesWithinAsync(stream, new StdOutLines(_ => { }, maxLine, maxTotal), CancellationToken.None);
    }

    // Hands out at most `step` bytes per read.
    private sealed class TrickleStream(byte[] data, int step) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(step, buffer.Length)], cancellationToken);
    }
}
