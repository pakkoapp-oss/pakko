using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class ProtocolActivationRouterTests
{
    private static string BuildUri(string host, params string[] files)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(files)));
        return $"pakko://{host}?files={base64}";
    }

    [Fact]
    public void TryGetBrowsePath_SingleFileBrowseUri_ReturnsTrueWithPath()
    {
        var uri = BuildUri("browse", "C:\\archive.zip");

        var result = ProtocolActivationRouter.TryGetBrowsePath(uri, out var path);

        result.Should().BeTrue();
        path.Should().Be("C:\\archive.zip");
    }

    [Fact]
    public void TryGetBrowsePath_MultipleFilesBrowseUri_ReturnsFalse()
    {
        var uri = BuildUri("browse", "C:\\a.zip", "C:\\b.zip");

        var result = ProtocolActivationRouter.TryGetBrowsePath(uri, out var path);

        result.Should().BeFalse();
        path.Should().BeNull();
    }

    [Fact]
    public void TryGetBrowsePath_ExtractUri_ReturnsFalse()
    {
        var uri = BuildUri("extract", "C:\\archive.zip");

        var result = ProtocolActivationRouter.TryGetBrowsePath(uri, out var path);

        result.Should().BeFalse();
        path.Should().BeNull();
    }

    [Fact]
    public void TryGetBrowsePath_ArchiveUri_ReturnsFalse()
    {
        var uri = BuildUri("archive", "C:\\document.docx");

        var result = ProtocolActivationRouter.TryGetBrowsePath(uri, out var path);

        result.Should().BeFalse();
        path.Should().BeNull();
    }

    [Fact]
    public void TryGetBrowsePath_MalformedUri_ReturnsFalse()
    {
        var result = ProtocolActivationRouter.TryGetBrowsePath("not a uri", out var path);

        result.Should().BeFalse();
        path.Should().BeNull();
    }

    [Fact]
    public void TryGetBrowsePath_MissingFilesParam_ReturnsFalse()
    {
        var result = ProtocolActivationRouter.TryGetBrowsePath("pakko://browse", out var path);

        result.Should().BeFalse();
        path.Should().BeNull();
    }
}
