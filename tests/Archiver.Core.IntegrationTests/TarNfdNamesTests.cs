using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Text;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F466: a tar from macOS stores names in NFD; tar.exe lists them, matches "-T" names and writes
// them in NFC (measured on 3.8.9 and 3.5.2). Pakko's header walk compared the NFD header name with
// the NFC listing and refused every filter and every selection of that entry. Two stored forms of
// one name still refuse: they list alike, and "-T" takes both (measured).
[Collection("TarSandbox")]
public sealed class TarNfdNamesTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());

    public void Dispose() => _temp.Dispose();

    [NfdIntegration]
    public async Task Extract_SelectionOfAnNfdEntry_ItsOwnFile()
    {
        var (nfc, nfd) = Names();
        string archive = Write(nfdOnly: true, nfd, nfc);
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, SelectedEntryPaths = [nfc],
        });

        result.Errors.Should().BeEmpty();
        File.ReadAllText(Path.Combine(dest, nfc)).Should().Be("nfd content");
        File.Exists(Path.Combine(dest, "plain.txt")).Should().BeFalse();
    }

    [NfdIntegration]
    public async Task Extract_FilterOverAnNfdEntry_EveryFile()
    {
        var (nfc, nfd) = Names();
        string archive = Write(nfdOnly: true, nfd, nfc);
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, EntryFilter = (_, _) => true,
        });

        result.Errors.Should().BeEmpty();
        File.ReadAllText(Path.Combine(dest, nfc)).Should().Be("nfd content");
        File.ReadAllText(Path.Combine(dest, "plain.txt")).Should().Be("plain");
    }

    [NfdIntegration]
    public async Task Test_FilterOverAnNfdEntry_Succeeds()
    {
        var (nfc, nfd) = Names();
        string archive = Write(nfdOnly: true, nfd, nfc);

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (path, _) => path != "plain.txt");

        result.Errors.Should().BeEmpty();
        result.Success.Should().BeTrue();
    }

    [NfdIntegration]
    public async Task Extract_SelectionOfANameStoredInBothForms_Refused()
    {
        var (nfc, nfd) = Names();
        string archive = Write(nfdOnly: false, nfd, nfc);
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, SelectedEntryPaths = [nfc],
        });

        result.Success.Should().BeFalse();
        (Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories) : []).Should().BeEmpty();
    }

    private string Write(bool nfdOnly, string nfd, string nfc)
    {
        string path = Path.Combine(_temp.Path, nfdOnly ? "nfd.tar" : "both.tar");
        TarBuilder.WriteTar(path,
        [
            .. (nfdOnly
                ? (TarBuilder.Entry[])[]
                : [TarBuilder.PaxPath(nfc), new TarBuilder.Entry { Name = "nfc", Content = Encoding.ASCII.GetBytes("nfc content") }]),
            TarBuilder.PaxPath(nfd),
            new TarBuilder.Entry { Name = "nfd", Content = Encoding.ASCII.GetBytes("nfd content") },
            new TarBuilder.Entry { Name = "plain.txt", Content = Encoding.ASCII.GetBytes("plain") },
        ]);
        return path;
    }

    // A precomposed letter this machine's ANSI page holds: e-acute (1252, 1250, UTF-8), short i (1251).
    private static (string Nfc, string Nfd)? Pick()
    {
        Encoding page = SystemCodePages.ForStrict(SystemCodePages.Ansi);
        foreach (string nfd in new[] { "cafe" + (char)0x301 + ".txt", "ka" + (char)0x438 + (char)0x306 + ".txt" })
        {
            string nfc = nfd.Normalize(NormalizationForm.FormC);
            try
            {
                if (page.GetString(page.GetBytes(nfc)) == nfc)
                    return (nfc, nfd);
            }
            catch (EncoderFallbackException)
            {
                // not in this page: try the next letter
            }
        }
        return null;
    }

    private static (string Nfc, string Nfd) Names() => Pick() ?? throw new InvalidOperationException("no letter; the attribute skips this test");

    private sealed class NfdIntegrationAttribute : FactAttribute
    {
        public NfdIntegrationAttribute()
        {
            Skip = new IntegrationAttribute().Skip;
            if (Skip is null && Pick() is null)
                Skip = $"system page {SystemCodePages.Ansi} holds none of the precomposed letters";
        }
    }
}
