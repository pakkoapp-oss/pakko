using System.Text;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Text;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F467 (the user, 2026-11-07: every character of every page in tests): one ustar per machine Windows
// can run under, holding every character of the page its raw header names are read in, in every byte
// form Windows decodes ("<unit>.t"). libarchive reads each name into the output page and bsdtar prints
// it (BsdtarListingModel, checked against the real tar.exe). For each listed row, a selection of it
// must reach tar.exe only if tar.exe, which matches "-T" names as text in its page (measured), takes
// exactly that row's entry; and every row whose name no other entry shares and that was read without
// loss must reach it.
public sealed class TarSendsAsListedExhaustiveTests
{
    public static TheoryData<int, int, string, bool> Machines()
    {
        var data = new TheoryData<int, int, string, bool>();
        foreach ((int ansi, int oem, string name) in MachineList)
        {
            data.Add(ansi, oem, name, false);
            data.Add(ansi, oem, name, true);
        }
        return data;
    }

    private static readonly (int Ansi, int Oem, string Name)[] MachineList =
    [
        (874, 874, "Thai_Thailand"),
        (932, 932, "Japanese_Japan"),
        (936, 936, "Chinese (Simplified)_China"),
        (949, 949, "Korean_Korea"),
        (950, 950, "Chinese (Traditional)_Taiwan"),
        (1250, 852, "Polish_Poland"),
        (1251, 866, "Russian_Russia"),
        (1251, 866, "Ukrainian_Ukraine"),
        (1252, 437, "English_United States"),
        (1252, 850, "German_Germany"),
        (1253, 737, "Greek_Greece"),
        (1254, 857, "Turkish_Turkey"),
        (1255, 862, "Hebrew_Israel"),
        (1256, 720, "Arabic_Saudi Arabia"),
        (1257, 775, "Lithuanian_Lithuania"),
        (1258, 1258, "Vietnamese_Vietnam"),
        (65001, 65001, "English_United States"),
    ];

    [Theory]
    [MemberData(nameof(Machines))]
    public void EveryForm_SentOnlyWhenItTakesExactlyItsEntry(int ansi, int oem, string localeName, bool tar389)
    {
        var machine = new MachinePages(ansi, oem, localeName);
        int headerPage = TextBoundaries.LibarchiveOemPage(machine);
        uint page = (uint)ansi;
        Encoding listingPage = TextBoundaries.TarExeOutput(machine).Lenient;
        Encoding headerDecoding = TextBoundaries.TarRawHeaderNames(machine).Lenient;

        List<byte[]> all = [.. BsdtarListingModel.Units(headerPage).Select(unit => (byte[])[.. unit, (byte)'.', (byte)'t'])];
        string[] allHeaders = [.. all.Select(name => headerDecoding.GetString(name))];
        // 3.8.9 leaves out a name it could read only with the default character (measured).
        List<(byte[] Raw, byte[] Read)> rows = [.. all
            .Select(name => (Raw: name, Read: BsdtarListingModel.ReadName(name, headerPage, ansi, tar389)))
            .Where(row => row.Read is not null)
            .Select(row => (row.Raw, row.Read!))];
        bool entriesLeftOut = rows.Count < all.Count;
        List<byte[]> raw = [.. rows.Select(row => row.Raw)];
        List<byte[]> read = [.. rows.Select(row => row.Read)];
        byte[] listing;
        using (var model = new BsdtarListingModel(ansi))
            listing = [.. read.SelectMany(name => (byte[])[.. model.Print(name), (byte)'\r', (byte)'\n'])];

        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes(listing, listingPage);
        Dictionary<string, byte[]?> byText = TarNames.IndexByText(listed, page);
        string[] shown = TarNames.ReadListing(listingPage.GetString(listing), listingPage)
            ?? throw new InvalidOperationException("the model printed an escape bsdtar never writes");
        shown.Should().HaveCount(raw.Count);
        string[] headers = [.. raw.Select(name => headerDecoding.GetString(name))];
        HashSet<string> lossy = entriesLeftOut ? new HashSet<string>(shown, StringComparer.Ordinal) : LossyNames(shown, allHeaders);
        string?[] texts = [.. read.Select(name => TarCommandLineEncoding.Decode(name, page))];
        var entriesOf = texts.Where(text => text is not null)
            .GroupBy(text => text!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var takesAnother = new List<string>();
        var refused = new List<string>();
        for (int i = 0; i < raw.Count; i++)
        {
            string key = shown[i].TrimEnd('/');
            byte[]? sent = TarCommandLineEncoding.Encode(key, page);
            bool reaches = sent is not null
                && TarNames.ReachesTheListedEntry(key, listed, byText, page, sent)
                && !lossy.Contains(shown[i]);
            string? sentText = sent is null ? null : TarCommandLineEncoding.Decode(sent, page);
            bool onlyItsEntry = texts[i] is { } text && sentText == text && entriesOf[text] == 1;
            // T-F416: a real '?' is refused by design (no Windows file name holds one).
            bool readWithoutLoss = texts[i] == headers[i] && !headers[i].Contains('?');
            if (reaches && !onlyItsEntry)
                takesAnother.Add(Convert.ToHexString(raw[i]));
            else if (!reaches && !entriesLeftOut && texts[i] is { } own && entriesOf[own] == 1 && readWithoutLoss)
                refused.Add(Convert.ToHexString(raw[i]));
        }

        takesAnother.Should().BeEmpty("a sent name must take exactly its own entry ({0} names, header page {1})", raw.Count, headerPage);
        // A listing shorter than the headers (an entry 3.8.9 left out) refuses every selection (T-F441's
        // count check, fail closed); that the check still sees it is asserted here.
        if (entriesLeftOut)
            TarNames.Judge(shown, allHeaders, [shown[0]]).Trust.Should().NotBe(TarNameTrust.Faithful);
        refused.Should().BeEmpty("a name read without loss that no other entry shares must be sendable ({0} names, header page {1})", raw.Count, headerPage);
    }

    // The names Judge refuses as a selection of their own. A verdict over a set is Faithful only when it
    // is for each member, so each round's example is one such name, taken out until the rest pass.
    private static HashSet<string> LossyNames(string[] shown, string[] headers)
    {
        var deciding = new HashSet<string>(shown, StringComparer.Ordinal);
        var lossy = new HashSet<string>(StringComparer.Ordinal);
        while (deciding.Count > 0)
        {
            TarListingVerdict verdict = TarNames.Judge(shown, headers, deciding);
            if (verdict.Trust == TarNameTrust.Faithful)
                break;
            if (verdict.Example is not { } example)
                return new HashSet<string>(shown, StringComparer.Ordinal);
            string row = deciding.Contains(example) ? example : deciding.First(name => name.TrimEnd('/') == example);
            lossy.Add(row);
            deciding.Remove(row);
        }
        return lossy;
    }
}
