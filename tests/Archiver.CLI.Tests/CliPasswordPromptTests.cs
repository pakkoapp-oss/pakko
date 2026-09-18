using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

/// <summary>
/// Unit tests for CliPasswordPrompt's masked-input editing logic, via an injected fake key
/// source — the real interactive path (Console.ReadKey against a real console) is structurally
/// unreachable from Archiver.CLI.Tests' Subprocess/ layer, since CliProcessRunner always
/// redirects the child's stdin (see T-F191's DECISIONS.md entry).
/// </summary>
public sealed class CliPasswordPromptTests
{
    private static ConsoleKeyInfo Key(char c) => new(c, CharToConsoleKey(c), false, false, false);
    private static ConsoleKeyInfo Special(ConsoleKey key) => new('\0', key, false, false, false);

    private static ConsoleKey CharToConsoleKey(char c) =>
        Enum.TryParse<ConsoleKey>(char.ToUpperInvariant(c).ToString(), out var parsed) ? parsed : ConsoleKey.NoName;

    private static Func<ConsoleKeyInfo> QueueOf(params ConsoleKeyInfo[] keys)
    {
        var queue = new Queue<ConsoleKeyInfo>(keys);
        return () => queue.Dequeue();
    }

    // --- Happy path ---

    [Fact]
    public void Read_TypedCharactersThenEnter_ReturnsTypedString()
    {
        var keys = QueueOf(Key('h'), Key('i'), Special(ConsoleKey.Enter));

        string? result = CliPasswordPrompt.Read(keys);

        result.Should().Be("hi");
    }

    [Fact]
    public void Read_EchoesAsteriskPerAcceptedCharacter()
    {
        var keys = QueueOf(Key('a'), Key('b'), Special(ConsoleKey.Enter));
        var echoed = new List<char>();

        CliPasswordPrompt.Read(keys, echoed.Add);

        echoed.Should().Equal('*', '*');
    }

    // --- Security & Boundary ---

    [Fact]
    public void Read_EmptyPasswordThenEnter_ReturnsEmptyString()
    {
        var keys = QueueOf(Special(ConsoleKey.Enter));

        string? result = CliPasswordPrompt.Read(keys);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Read_BackspaceOnEmptyBuffer_DoesNotUnderflowOrEcho()
    {
        var keys = QueueOf(Special(ConsoleKey.Backspace), Key('x'), Special(ConsoleKey.Enter));
        var echoed = new List<char>();

        string? result = CliPasswordPrompt.Read(keys, echoed.Add);

        result.Should().Be("x");
        echoed.Should().Equal('*'); // the backspace on empty produced no echo call at all
    }

    [Fact]
    public void Read_Backspace_DeletesLastCharacterAndEchoesBackspace()
    {
        var keys = QueueOf(Key('a'), Key('b'), Special(ConsoleKey.Backspace), Special(ConsoleKey.Enter));
        var echoed = new List<char>();

        string? result = CliPasswordPrompt.Read(keys, echoed.Add);

        result.Should().Be("a");
        echoed.Should().Equal('*', '*', '\b');
    }

    [Fact]
    public void Read_Escape_ReturnsNullCancelled()
    {
        var keys = QueueOf(Key('a'), Special(ConsoleKey.Escape));

        string? result = CliPasswordPrompt.Read(keys);

        result.Should().BeNull();
    }

    // --- Misuse & Fool ---

    [Fact]
    public void Read_NonPrintableKeyLikeArrow_IsIgnoredNotAppended()
    {
        var keys = QueueOf(Key('a'), Special(ConsoleKey.LeftArrow), Special(ConsoleKey.F5), Key('b'), Special(ConsoleKey.Enter));

        string? result = CliPasswordPrompt.Read(keys);

        result.Should().Be("ab");
    }
}
