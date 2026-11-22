using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F559: a walk asks every entry whether its plain path opens a device. GetFullPath decides, and runs only
// for a name MayNameADevice lets through; that filter must let every device name through, on this Windows.
public sealed class InputPathsDeviceEntryTests
{
    private const string Folder = @"C:\x\";

    public static TheoryData<string> Candidates()
    {
        var stems = new List<string> { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$", "CONFIG$" };
        foreach (string port in new[] { "COM", "LPT" })
        {
            for (char digit = '0'; digit <= '9'; digit++)
                stems.Add(port + digit);
            foreach (int superscript in new[] { 0xB9, 0xB2, 0xB3 })
                stems.Add(port + (char)superscript);
        }
        var data = new TheoryData<string>();
        foreach (string stem in stems)
        {
            foreach (string name in new[]
            {
                stem, stem.ToLowerInvariant(), stem + ".txt", stem + ".tar.gz", stem + " .txt", stem + "  ", stem + ".",
                stem + ":stream", " " + stem, stem + "x", "x" + stem,
            })
            {
                data.Add(name);
            }
        }
        return data;
    }

    [Theory, MemberData(nameof(Candidates))]
    public void EntryNamesADevice_IsExactlyWhatWindowsOpensAsADevice(string name) =>
        InputPaths.EntryNamesADevice(Folder + name).Should().Be(InputPaths.NamesADevice(Folder + name));

    [Theory]
    [InlineData(@"C:\x\NUL", true)]
    [InlineData(@"C:\x\nul", true)]
    [InlineData(@"C:\x\a.txt", false)]
    [InlineData(@"C:\x\NULL", false)]
    [InlineData(@"\\?\C:\x\NUL", false)]
    public void EntryNamesADevice_OnEveryWindows(string fullName, bool expected) =>
        InputPaths.EntryNamesADevice(fullName).Should().Be(expected);

    [Theory]
    [InlineData("a.txt")]
    [InlineData("readme")]
    [InlineData("ok")]
    [InlineData("document.docx")]
    [InlineData("")]
    public void MayNameADevice_OrdinaryNames_SkipGetFullPath(string name) =>
        InputPaths.MayNameADevice(name).Should().BeFalse();
}
