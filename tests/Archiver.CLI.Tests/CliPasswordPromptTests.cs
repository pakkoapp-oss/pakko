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

    // T-F160 (found while making Ctrl+C work at the conflict prompt): the caller sets
    // Console.TreatControlCAsInput = true while reading, so Ctrl+C arrives as KeyChar '\x03' —
    // it used to be appended to the password as a literal control character instead of cancelling.
    [Fact]
    public void Read_CtrlC_ReturnsNullCancelledNotAppended()
    {
        var ctrlC = new ConsoleKeyInfo('\x03', ConsoleKey.C, shift: false, alt: false, control: true);
        var keys = QueueOf(Key('a'), ctrlC, Special(ConsoleKey.Enter));

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

    // --- T-F193: ReadNewPassword (bare -p on 'a' — asked twice, like 7z) ---

    private static ConsoleKeyInfo[] Typed(string text) =>
        [.. text.Select(Key), Special(ConsoleKey.Enter)];

    [Fact]
    public void ReadNewPassword_MatchingEntries_ReturnsPasswordAfterAskingTwice()
    {
        var written = new List<string>();

        var result = CliPasswordPrompt.ReadNewPassword(QueueOf([.. Typed("Secret1"), .. Typed("Secret1")]), written.Add);

        result.Password.Should().Be("Secret1");
        result.Error.Should().BeNull();
        result.Cancelled.Should().BeFalse();
        written.Should().Contain(w => w.Contains("Enter password"))
            .And.Contain(w => w.Contains("Reenter password"));
    }

    [Fact]
    public void ReadNewPassword_Mismatch_ReturnsErrorAndNoPassword()
    {
        var result = CliPasswordPrompt.ReadNewPassword(QueueOf([.. Typed("Secret1"), .. Typed("Secret2")]), _ => { });

        result.Password.Should().BeNull();
        result.Error.Should().Contain("do not match");
        result.Cancelled.Should().BeFalse();
    }

    [Fact]
    public void ReadNewPassword_EscapeAtFirstPrompt_IsCancelledWithoutAskingAgain()
    {
        var written = new List<string>();

        var result = CliPasswordPrompt.ReadNewPassword(QueueOf(Key('a'), Special(ConsoleKey.Escape)), written.Add);

        result.Cancelled.Should().BeTrue();
        result.Password.Should().BeNull();
        written.Should().NotContain(w => w.Contains("Reenter"));
    }

    [Fact]
    public void ReadNewPassword_CtrlCAtSecondPrompt_IsCancelled()
    {
        var ctrlC = new ConsoleKeyInfo('\x03', ConsoleKey.C, shift: false, alt: false, control: true);

        var result = CliPasswordPrompt.ReadNewPassword(QueueOf([.. Typed("Secret1"), Key('S'), ctrlC]), _ => { });

        result.Cancelled.Should().BeTrue();
    }

    [Fact]
    public void ReadNewPassword_NonAsciiFirstEntry_IsRefusedBeforeAskingAgain()
    {
        var written = new List<string>();

        var result = CliPasswordPrompt.ReadNewPassword(QueueOf(Typed("пароль")), written.Add);

        result.Password.Should().BeNull();
        result.Error.Should().Contain("English letters");
        written.Should().NotContain(w => w.Contains("Reenter"));
    }

    [Fact]
    public void ReadNewPassword_EmptyEntry_IsRefused()
    {
        var result = CliPasswordPrompt.ReadNewPassword(QueueOf(Special(ConsoleKey.Enter)), _ => { });

        result.Error.Should().Contain("empty");
        result.Cancelled.Should().BeFalse();
    }

    [Fact]
    public void ReadNewPassword_EntryOneOverMaxLength_IsRefused()
    {
        var result = CliPasswordPrompt.ReadNewPassword(QueueOf(Typed(new string('a', 100))), _ => { });

        result.Error.Should().Contain("99");
    }

    [Fact]
    public void DescribeEncryptProblem_CoversEveryProblemAndNoneIsNull()
    {
        foreach (var problem in Enum.GetValues<Archiver.Core.Services.EncryptionPasswordProblem>())
        {
            string? text = CliPasswordPrompt.DescribeEncryptProblem(problem);
            if (problem == Archiver.Core.Services.EncryptionPasswordProblem.None)
                text.Should().BeNull();
            else
                text.Should().NotBeNullOrWhiteSpace();
        }
    }
}
