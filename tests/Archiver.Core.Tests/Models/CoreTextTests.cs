using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Models;

// T-F237: an entry name or an OS message can be a 40,000-character path; a message keeps its head
// and tail so both the top folders and the file name stay visible.
public sealed class CoreTextTests
{
    [Fact]
    public void LongArgument_KeepsHeadAndTail()
    {
        string name = string.Concat(Enumerable.Repeat("a/", 20_000)) + "x.txt";

        string english = CoreMessages.Text(MessageCode.CannotExtractEntry, name, "too long").English;

        english.Length.Should().BeLessThan(4 * 1024);
        english.Should().StartWith("Cannot extract 'a/a/a/").And.EndWith("a/x.txt': too long").And.Contain("...");
    }

    [Fact]
    public void ArgumentAtTheLimit_IsUnchanged()
    {
        string name = new('n', CoreText.MaxArgumentLength);

        CoreText.Raw(name).English.Should().Be(name);
    }

    [Fact]
    public void Shortening_NeverSplitsASurrogatePair()
    {
        // The leading "x" puts both cut points inside a pair.
        string name = "x" + string.Concat(Enumerable.Repeat("\U0001F600", CoreText.MaxArgumentLength)) + "y";

        string english = CoreText.Raw(name).English;

        english.Length.Should().BeLessThan(name.Length);
        for (int i = 0; i < english.Length; i++)
        {
            if (char.IsHighSurrogate(english[i]))
                char.IsLowSurrogate(english[i + 1]).Should().BeTrue();
            else if (char.IsLowSurrogate(english[i]))
                char.IsHighSurrogate(english[i - 1]).Should().BeTrue();
        }
    }
}
