using System.Text;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Text;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F550: the "-t" lines kept for T-F465's check, and the check over them (TarNames.FirstUnreached).
public sealed class TarPrintedNamesTests
{
    [Fact]
    public void Lines_ComeBackInOrder_AcrossBlocks()
    {
        var printed = new TarPrintedNames(blockSize: 16);
        string[] lines = ["a.txt", "a much longer line than a block", "b", "", "c\r", "0123456789a"];
        foreach (string line in lines)
            printed.Add(Encoding.ASCII.GetBytes(line));

        var back = new List<string>();
        printed.ForEach(line => back.Add(Encoding.ASCII.GetString(line)));

        back.Should().Equal("a.txt", "a much longer line than a block", "b", "c", "0123456789a");
        printed.BlockCount.Should().Be(4, "a line that does not fit the rest of a block starts a new one");
    }

    [Fact]
    public void Empty_NoLines()
    {
        var printed = new TarPrintedNames();
        printed.Add("\r"u8);
        int count = 0;

        printed.ForEach(_ => count++);

        count.Should().Be(0);
        printed.BlockCount.Should().Be(0);
    }

    // The check over the kept lines gives every name the verdict T-F465's whole-listing dictionaries give
    // (ReadListedNameBytes + IndexByText): random listings of lines that print one text in several bytes,
    // escapes, lossy and unreadable lines, read in tar.exe's page and in another one (T-F465's swap).
    [Theory]
    [InlineData(932, 932u)]
    [InlineData(1251, 1251u)]
    [InlineData(1252, 1251u)]
    [InlineData(65001, 932u)]
    public void FirstUnreached_SameVerdictsAsTheWholeListing(int outputPage, uint commandLinePage)
    {
        Encoding encoding = new TextBoundary(outputPage).Lenient;
        byte[][] pool =
        [
            "a.txt"u8.ToArray(), "sub/"u8.ToArray(), "sub/b.txt"u8.ToArray(), "a\\210"u8.ToArray(), "a\\200"u8.ToArray(),
            [(byte)'a', 0x80], [(byte)'a', 0x88], [0x87, 0x90], [0x81, 0xE0], [0x87, 0x9C], [0x81, 0xBE], [0x5C, 0x5C, 0xBE],
            [0xED, 0x40], [0xFA, 0x5C], [(byte)'a', 0xFF], "bad\\q"u8.ToArray(), [0xD0, 0x96], [0xE2, 0x88, 0xAA],
        ];
        byte[] alphabet = [(byte)'a', 0x5C, 0x80, 0x81, 0x87, 0x88, 0x90, 0x9C, 0xBE, 0xE0, 0xED, 0x40, 0xFA, 0xFF, (byte)'/', (byte)'2', (byte)'1', (byte)'0'];
        var random = new Random(550);

        for (int round = 0; round < 1500; round++)
        {
            var lines = new List<byte[]>();
            for (int i = random.Next(1, 7); i > 0; i--)
            {
                lines.Add(random.Next(3) > 0
                    ? pool[random.Next(pool.Length)]
                    : [.. Enumerable.Range(0, random.Next(1, 5)).Select(_ => alphabet[random.Next(alphabet.Length)])]);
            }
            byte[] listing = [.. lines.SelectMany(line => (byte[])[.. line, (byte)'\n'])];
            var printed = new TarPrintedNames(blockSize: 8);
            foreach (byte[] line in lines)
                printed.Add(line);
            Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes(listing, encoding);
            Dictionary<string, byte[]?> byText = TarNames.IndexByText(listed, commandLinePage);
            string[] keys = [.. listed.Keys, "a", "missing.txt"];

            foreach (string key in keys)
            {
                (string, byte[]?)[] one = [(key, TarCommandLineEncoding.Encode(key, commandLinePage))];
                TarNames.FirstUnreached(printed, encoding, commandLinePage, one)
                    .Should().Be(WholeListing(listed, byText, commandLinePage, one), $"round {round}, {key}");
            }
            (string, byte[]?)[] some = [.. keys.Where(_ => random.Next(2) == 0).Select(key => (key, TarCommandLineEncoding.Encode(key, commandLinePage)))];
            TarNames.FirstUnreached(printed, encoding, commandLinePage, some)
                .Should().Be(WholeListing(listed, byText, commandLinePage, some), $"round {round}");
        }
    }

    // T-F465's check as it was: dictionaries of every listed name.
    private static int WholeListing(
        Dictionary<string, byte[]?> listed, Dictionary<string, byte[]?> byText, uint commandLinePage, (string Key, byte[]? Sent)[] selection)
    {
        for (int i = 0; i < selection.Length; i++)
        {
            if (selection[i].Sent is not { } sent || !TarNames.ReachesTheListedEntry(selection[i].Key, listed, byText, commandLinePage, sent))
                return i;
        }
        return -1;
    }
}
