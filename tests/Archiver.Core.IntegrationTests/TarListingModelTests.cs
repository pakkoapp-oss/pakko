using System.Diagnostics;
using System.Text.RegularExpressions;
using Archiver.Core.Tests.Services.Sandbox;
using Archiver.Core.Text;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F467: the real tar.exe lists a ustar holding every character of this machine's header page
// exactly as BsdtarListingModel says, byte for byte; the exhaustive guard test runs on that model
// for every machine (14 more locales on PakkoW10 with 3.5.2 and 3.8.9, docs/DECISIONS.md "T-F467 done").
[Collection("TarSandbox")]
public sealed partial class TarListingModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pakko-listing-model-" + Guid.NewGuid().ToString("N"));

    public TarListingModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Integration]
    public void RealListing_IsTheModels()
    {
        MachinePages machine = MachinePages.Current;
        int headerPage = TextBoundaries.LibarchiveOemPage(machine);
        List<byte[]> raw = [.. BsdtarListingModel.Units(headerPage).Select(unit => (byte[])[.. unit, (byte)'.', (byte)'t'])];
        string archive = Path.Combine(_dir, "every.tar");
        TarBuilder.WriteTar(archive, raw.Select(name => new TarBuilder.Entry { Name = "x", NameBytes = name }));

        byte[] Expected(bool skipsDefaultCharacter)
        {
            using var model = new BsdtarListingModel(machine.Ansi);
            return [.. raw
                .Select(name => BsdtarListingModel.ReadName(name, headerPage, machine.Ansi, skipsDefaultCharacter))
                .Where(name => name is not null)
                .SelectMany(name => (byte[])[.. model.Print(name!), (byte)'\r', (byte)'\n'])];
        }

        byte[] listing = Run("-tf", archive);

        // Measured: 3.8.9 leaves out a name read with the default character; the in-box 3.5.2 prints it.
        Version version = TarVersion();
        if (version >= new Version(3, 8))
            listing.Should().Equal(Expected(true), "tar.exe {0} at ANSI {1}, header page {2}", version, machine.Ansi, headerPage);
        else if (version < new Version(3, 6))
            listing.Should().Equal(Expected(false), "tar.exe {0} at ANSI {1}, header page {2}", version, machine.Ansi, headerPage);
        else
            (listing.SequenceEqual(Expected(true)) || listing.SequenceEqual(Expected(false)))
                .Should().BeTrue("tar.exe {0} lists as one of the two measured builds", version);
    }

    private static Version TarVersion()
    {
        string text = System.Text.Encoding.ASCII.GetString(Run("--version"));
        Match match = BsdtarVersion().Match(text);
        match.Success.Should().BeTrue("tar.exe --version names bsdtar's version: {0}", text);
        return Version.Parse(match.Groups[1].Value);
    }

    // Exit 1 is expected where libarchive warns about a name it could not read without loss.
    private static byte[] Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(@"C:\Windows\System32\tar.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process tar = Process.Start(start) ?? throw new InvalidOperationException("tar.exe did not start");
        using var stdout = new MemoryStream();
        Task<string> errors = tar.StandardError.ReadToEndAsync();
        tar.StandardOutput.BaseStream.CopyTo(stdout);
        errors.Wait(TimeSpan.FromMinutes(1)).Should().BeTrue("tar.exe's stderr closes when it exits");
        tar.WaitForExit(60_000).Should().BeTrue("tar.exe lists a small archive in under a minute");
        tar.ExitCode.Should().BeOneOf([0, 1], "tar.exe ran: {0}", errors.Result);
        return stdout.ToArray();
    }

    [GeneratedRegex(@"bsdtar (\d+\.\d+)")]
    private static partial Regex BsdtarVersion();
}
