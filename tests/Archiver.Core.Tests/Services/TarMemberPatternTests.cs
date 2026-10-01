using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F284: an extract member handed to tar.exe must match only the entry of that name. "*" and
/// "?" cannot be in a Windows file name, so only this unit test covers them; the bracket case also
/// runs against the real tar.exe (TarSandboxedServiceMemberPatternTests).
/// </summary>
public sealed class TarMemberPatternTests
{
    [Theory]
    [InlineData("docs/readme.txt", "docs/readme.txt")]
    [InlineData("a[1].txt", "a[[]1].txt")]
    [InlineData("d[x]/f.txt", "d[[]x]/f.txt")]
    [InlineData("s*r.txt", "s[*]r.txt")]
    [InlineData("q?.txt", "q[?].txt")]
    [InlineData("[[", "[[][[]")]
    [InlineData("-C", "-C")]
    public void EscapeMemberPattern_WildcardCharactersMatchOnlyThemselves(string name, string expected)
    {
        TarSandboxScope.EscapeMemberPattern(name).Should().Be(expected);
    }
}
