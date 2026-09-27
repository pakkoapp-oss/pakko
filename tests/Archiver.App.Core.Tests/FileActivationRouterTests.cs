using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class FileActivationRouterTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("pakko-fileactivation-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteBytes(string name, byte[] bytes)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Decide_SingleZip_ReturnsBrowse()
    {
        string path = WriteBytes("a.zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);

        FileActivationDecision decision = FileActivationRouter.Decide([path]);

        decision.Mode.Should().Be(FileActivationMode.Browse);
        decision.BrowsePath.Should().Be(path);
    }

    [Fact]
    public void Decide_SingleSupportedNonZipArchive_ReturnsBrowse()
    {
        string path = WriteBytes("a.rar", [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);

        FileActivationDecision decision = FileActivationRouter.Decide([path]);

        decision.Mode.Should().Be(FileActivationMode.Browse);
        decision.BrowsePath.Should().Be(path);
    }

    [Fact]
    public void Decide_MultipleFiles_ReturnsAddToList()
    {
        string a = WriteBytes("a.zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);
        string b = WriteBytes("b.zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);

        FileActivationDecision decision = FileActivationRouter.Decide([a, b]);

        decision.Mode.Should().Be(FileActivationMode.AddToList);
        decision.BrowsePath.Should().BeNull();
    }

    [Fact]
    public void Decide_SingleUnknownFormatFile_ReturnsAddToList()
    {
        string path = WriteBytes("notes.txt", [0x48, 0x65, 0x6C, 0x6C, 0x6F]);

        FileActivationDecision decision = FileActivationRouter.Decide([path]);

        decision.Mode.Should().Be(FileActivationMode.AddToList);
        decision.BrowsePath.Should().BeNull();
    }
}
