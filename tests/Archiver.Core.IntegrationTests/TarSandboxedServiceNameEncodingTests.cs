using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F204 / T-F244 item 2 / T-F215: names crossing the tar.exe boundary. tar.exe prints names in
/// the user locale's ANSI code page (Pakko read them as UTF-8 — garbage in listings), reads a tar
/// header with no charset as the OEM page (a GNU tar or 7-Zip archive with UTF-8 names was
/// written to disk as mojibake, exit 0), and cannot represent a name outside that code page at
/// all. Expectations are built from this machine's own code pages, so the same tests hold on a
/// uk-UA (1251/866) and an en-US (1252/437) machine.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceNameEncodingTests : IDisposable
{
    private readonly TarSandboxedService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string> Layouts => new() { "utf8", "oem", "pax" };

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task NonAsciiName_ExtractsAndListsTheRealName(string layout)
    {
        string? name = TarCodePage.PortableNonAsciiName();
        if (name is null)
            return; // this machine's code pages share no candidate
        string archivePath = Path.Combine(_temp.Path, layout + ".tar");
        byte[] content = Encoding.ASCII.GetBytes("payload");
        TarBuilder.Entry[] entries = layout switch
        {
            "utf8" => [new TarBuilder.Entry { Name = "x", NameBytes = Encoding.UTF8.GetBytes(name), Content = content }],
            "oem" => [new TarBuilder.Entry { Name = "x", NameBytes = TarCodePage.Encoding(TarCodePage.UserOem).GetBytes(name), Content = content }],
            _ => [TarBuilder.PaxPath(name), new TarBuilder.Entry { Name = "placeholder.txt", Content = content }],
        };
        TarBuilder.WriteTar(archivePath, entries);

        var list = await _sut.ListEntriesAsync(archivePath);
        list.Success.Should().BeTrue(list.ErrorMessage);
        list.Entries.Select(e => e.Path).Should().Equal(name);

        string dest = Path.Combine(_temp.Path, "out-" + layout);
        var result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [archivePath], DestinationFolder = dest, Mode = ExtractMode.SingleFolder });
        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(dest, name)).Should().Be("payload");
        Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Should().ContainSingle();
    }

    // A UTF-8 name outside the locale's code page (U+2713) is never written as mojibake: either
    // tar.exe can name it (a UTF-8 or "C" locale, as on the CI runner) and it arrives under its
    // real name, or it cannot and the failure says so with nothing written. Before fix phase 4 it
    // was extracted as "tick тЬУ.txt" with success.
    [Fact]
    public async Task NameOutsideCodePage_RealNameOrClearFailure_NeverMojibake()
    {
        const string name = "tick ✓.txt";
        string archivePath = Path.Combine(_temp.Path, "tick.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "x", NameBytes = Encoding.UTF8.GetBytes(name), Content = [1] },
        ]);

        string dest = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [archivePath], DestinationFolder = dest, Mode = ExtractMode.SingleFolder });

        string[] written = Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories) : [];
        if (result.Success)
        {
            written.Select(Path.GetFileName).Should().Equal(name);
        }
        else
        {
            result.Errors.Should().ContainSingle().Which.Message.Should().Contain("code page");
            written.Should().BeEmpty();
        }
    }

    // The pre-scan split names on '/' only; Windows also treats '\' as a separator.
    [Fact]
    public async Task BackslashTraversalName_RejectedByThePreScan()
    {
        string archivePath = Path.Combine(_temp.Path, "evil.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "ok.txt", Content = [1] },
            new TarBuilder.Entry { Name = "..\\evil.txt", Content = [2] },
        ]);

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath], DestinationFolder = Path.Combine(_temp.Path, "out"), Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("unsafe entry path");
    }

    // T-F215: a failed creation reported tar.exe's "-v" progress lines ("a src", "a src/a.txt")
    // instead of the reason.
    [Fact]
    public async Task CreationFailure_ReportsTheReasonNotProgressLines()
    {
        string source = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");
        string locked = Path.Combine(source, "b.txt");
        File.WriteAllText(locked, "b");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [source], DestinationFolder = _temp.Path, ArchiveName = "out", Format = ArchiveContainerFormat.Tar,
        });

        result.Success.Should().BeFalse();
        string message = result.Errors.Should().ContainSingle().Which.Message;
        message.Should().Contain("b.txt");
        message.Should().NotContain("a src");
    }
}
