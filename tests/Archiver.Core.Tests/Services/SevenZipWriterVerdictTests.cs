using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F342 (the App and Explorer wave): what the tar.exe probe found, left where the Explorer
// extension reads it, because the extension cannot run the probe.
public sealed class SevenZipWriterVerdictTests : IDisposable
{
    private const string FullName = "PavloRybchenko.Pakko_1.8.1.7_x64__9hkd8feqeqbr4";
    private const string FamilyName = "PavloRybchenko.Pakko_9hkd8feqeqbr4";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PakkoVerdictTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _tar;

    public SevenZipWriterVerdictTests()
    {
        Directory.CreateDirectory(_root);
        _tar = Path.Combine(_root, "tar.exe");
        File.WriteAllBytes(_tar, new byte[1234]);
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private string PackageData()
    {
        string data = Path.Combine(_root, "Packages", FamilyName);
        Directory.CreateDirectory(data);
        return data;
    }

    private string VerdictPath() => Path.Combine(PackageData(), "LocalCache", SevenZipWriterVerdict.FileName);

    private string Expected(bool canCreate) =>
        $"{new FileInfo(_tar).Length};{File.GetLastWriteTimeUtc(_tar).ToFileTimeUtc()};{(canCreate ? 1 : 0)}";

    [Fact]
    public void Format_IsSizeTimeAndOneDigit()
    {
        SevenZipWriterVerdict.Format(1234, 133_000_000_000_000_000, canCreate: false)
            .Should().Be("1234;133000000000000000;0");
        SevenZipWriterVerdict.Format(1, 2, canCreate: true).Should().Be("1;2;1");
    }

    [Fact]
    public void PathFor_PackageFolder_IsTheFamilysLocalCache()
    {
        string data = PackageData();

        SevenZipWriterVerdict.PathFor(Path.Combine(@"C:\Program Files\WindowsApps", FullName), _root)
            .Should().Be(Path.Combine(data, "LocalCache", SevenZipWriterVerdict.FileName));
    }

    [Theory]
    [InlineData(@"C:\src\pakko\bin\Release")]
    [InlineData(@"C:\Program Files\WindowsApps\Name_1.0.0.0_x64_9hkd8feqeqbr4")]
    [InlineData(@"C:\Program Files\WindowsApps\_1.0.0.0_x64__9hkd8feqeqbr4")]
    [InlineData(@"C:\Program Files\WindowsApps\Name_1.0.0.0_x64__")]
    [InlineData(@"C:\Program Files\WindowsApps\Name_1.0.0.0_x64__a_b")]
    [InlineData(@"C:\Program Files\WindowsApps\..\.._1_x64__..")]
    [InlineData("")]
    public void PathFor_NotAPackageFolder_IsNull(string moduleDirectory)
    {
        PackageData();

        SevenZipWriterVerdict.PathFor(moduleDirectory, _root).Should().BeNull();
    }

    [Fact]
    public void PathFor_FamilyHasNoDataFolder_IsNull()
    {
        SevenZipWriterVerdict.PathFor(Path.Combine(@"C:\Program Files\WindowsApps", FullName), _root).Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TryWrite_WritesTheVerdictAndLeavesNoTempFile(bool canCreate)
    {
        string path = VerdictPath();

        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), path, _tar, answer: canCreate).Should().BeTrue();

        File.ReadAllText(path).Should().Be(Expected(canCreate));
        Directory.GetFiles(Path.GetDirectoryName(path)!).Should().Equal(path);
    }

    [Fact]
    public void TryWrite_ReplacesAnOlderVerdict()
    {
        string path = VerdictPath();
        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), path, _tar, answer: false).Should().BeTrue();

        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), path, _tar, answer: true).Should().BeTrue();

        File.ReadAllText(path).Should().Be(Expected(true));
    }

    // Under the policy CanCreateSevenZipAsync answers false without running tar.exe: recording that
    // would keep the menu item hidden after the policy is lifted.
    [Fact]
    public void TryWrite_TarDisabledByPolicy_WritesNothing()
    {
        string path = VerdictPath();

        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions { DisableTarExtraction = true }, path, _tar, answer: false)
            .Should().BeFalse();

        File.Exists(path).Should().BeFalse();
    }

    // No answer (a timeout on a busy machine) is not "no": an older verdict stays as it is.
    [Fact]
    public void TryWrite_NoAnswer_WritesNothingAndKeepsTheOlderVerdict()
    {
        string path = VerdictPath();

        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), path, _tar, answer: null).Should().BeFalse();
        File.Exists(path).Should().BeFalse();

        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), path, _tar, answer: true).Should().BeTrue();
        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), path, _tar, answer: null).Should().BeFalse();
        File.ReadAllText(path).Should().Be(Expected(true));
    }

    [Fact]
    public void TryWrite_NoPath_WritesNothing()
    {
        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), null, _tar, answer: true).Should().BeFalse();
    }

    [Fact]
    public void TryWrite_TarMissing_WritesNothing()
    {
        string path = VerdictPath();

        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), path, Path.Combine(_root, "absent.exe"), answer: false)
            .Should().BeFalse();

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void TryWrite_VerdictIsReadOnly_ReturnsFalseAndKeepsIt()
    {
        string path = VerdictPath();
        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), path, _tar, answer: false).Should().BeTrue();
        File.SetAttributes(path, FileAttributes.ReadOnly);

        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), path, _tar, answer: true).Should().BeFalse();

        File.ReadAllText(path).Should().Be(Expected(false));
        Directory.GetFiles(Path.GetDirectoryName(path)!).Should().Equal(path);
    }

    [Fact]
    public void TryWrite_FolderIsAFile_ReturnsFalse()
    {
        string blocker = Path.Combine(PackageData(), "LocalCache");
        File.WriteAllText(blocker, "not a folder");

        SevenZipWriterVerdict.TryWrite(new GroupPolicyOptions(), Path.Combine(blocker, SevenZipWriterVerdict.FileName), _tar, answer: true)
            .Should().BeFalse();
    }
}
