using Archiver.Core.IO;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

/// <summary>
/// T-F352: <see cref="DiskSeekPenalty"/> answers "no seek penalty" only when Windows says so. What
/// a real disk answers depends on the machine (a CI runner's virtual disk may not answer at all),
/// so the tests here pin the cases that must be false and that nothing throws.
/// </summary>
public sealed class DiskSeekPenaltyTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(@"\\no-such-server-pakko\share\file.bin")]
    [InlineData(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\file.bin")]
    public void IsKnownAbsent_NetworkOrUnknownVolume_IsFalse(string path)
    {
        DiskSeekPenalty.IsKnownAbsent(path).Should().BeFalse();
    }

    [Fact]
    public void IsKnownAbsent_DriveLetterWithNoDisk_IsFalse()
    {
        char? free = "ZYXWVUTSRQPONMLKJIHGFED".Cast<char?>().FirstOrDefault(letter => !Directory.Exists($@"{letter}:\"));
        if (free is null)
            return; // every letter is taken on this machine

        DiskSeekPenalty.IsKnownAbsent($@"{free}:\folder\file.bin").Should().BeFalse();
    }

    [Fact]
    public void IsKnownAbsent_ExistingFileItsFolderAndAPathNotYetCreated_AnswerTheSame()
    {
        string file = _temp.CreateFile("a.txt");

        bool forFolder = DiskSeekPenalty.IsKnownAbsent(_temp.Path);

        DiskSeekPenalty.IsKnownAbsent(file).Should().Be(forFolder);
        DiskSeekPenalty.IsKnownAbsent(Path.Combine(_temp.Path, "not", "yet", "there.zip")).Should().Be(forFolder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\0b")]
    [InlineData("::::")]
    public void IsKnownAbsent_PathThatIsNotOne_IsFalseAndDoesNotThrow(string path)
    {
        DiskSeekPenalty.IsKnownAbsent(path).Should().BeFalse();
    }
}
