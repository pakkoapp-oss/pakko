using FluentAssertions;

namespace Archiver.CLI.Tests;

public sealed class TestTempFolderTests
{
    [Fact]
    public async Task Delete_FileStillHeldForAMoment_FolderIsRemovedOnceItIsReleased()
    {
        var folder = new TestTempFolder("pakko-cli-temptest-");
        string held = Path.Combine(folder.Path, "two.tar");
        var handle = new FileStream(held, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(300);
            await handle.DisposeAsync();
        });

        await folder.DeleteAsync();
        await release;

        Directory.Exists(folder.Path).Should().BeFalse();
    }
}
