using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

/// <summary>
/// T-F160: the key-by-key line reader behind both interactive prompts. Reading key by key with
/// Console.TreatControlCAsInput is what lets Ctrl+C stop the conflict prompt at all — a blocked
/// Console.ReadLine never returns after a cancelled Ctrl+C (confirmed on device).
/// </summary>
public sealed class CliLineInputTests
{
    private static ConsoleKeyInfo Key(char c) => new(c, ConsoleKey.A, false, false, false);
    private static readonly ConsoleKeyInfo Enter = new('\r', ConsoleKey.Enter, false, false, false);
    private static readonly ConsoleKeyInfo Backspace = new('\b', ConsoleKey.Backspace, false, false, false);
    private static readonly ConsoleKeyInfo CtrlC = new('\x03', ConsoleKey.C, false, false, true);

    private static Func<ConsoleKeyInfo> QueueOf(params ConsoleKeyInfo[] keys)
    {
        var queue = new Queue<ConsoleKeyInfo>(keys);
        return () => queue.Dequeue();
    }

    [Fact]
    public void Read_Unmasked_EchoesTheTypedCharactersThemselves()
    {
        var echoed = new List<char>();

        string? line = CliLineInput.Read(QueueOf(Key('y'), Key('e'), Backspace, Enter), echoed.Add, mask: false);

        line.Should().Be("y");
        echoed.Should().Equal('y', 'e', '\b');
    }

    [Fact]
    public void Read_Masked_EchoesAsterisks()
    {
        var echoed = new List<char>();

        CliLineInput.Read(QueueOf(Key('p'), Key('w'), Enter), echoed.Add, mask: true).Should().Be("pw");

        echoed.Should().Equal('*', '*');
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_CtrlC_ReturnsNull(bool mask)
    {
        CliLineInput.Read(QueueOf(Key('q'), CtrlC), echo: null, mask).Should().BeNull();
    }
}
