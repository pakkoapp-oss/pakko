using System.Xml.Linq;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// What MainWindow.xaml must keep and no other test would notice losing: read from the markup,
// since the window itself needs a device.
public sealed class MainWindowLayoutTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XDocument Xaml = XDocument.Load(Path.Combine(FindRepoRoot(), "src", "Archiver.App", "MainWindow.xaml"));

    // T-F384: a list with no name is announced as "list" only.
    [Fact]
    public void EveryListView_HasAnAccessibleName()
    {
        List<XElement> lists = [.. Xaml.Descendants().Where(e => e.Name.LocalName == "ListView")];
        lists.Select(Name).Should().BeEquivalentTo(["FileListView", "ArchiveBrowserListView"], "the test must read the lists it is about");

        lists.Where(l => l.Attribute("AutomationProperties.Name") is null && l.Attribute("AutomationProperties.LabeledBy") is null)
            .Select(Name).Should().BeEmpty();
    }

    // T-F381: with encryption on, the password block pushed the PAR2 row out of the card's
    // visible part; above the block it stays in view.
    [Fact]
    public void RecoveryDataRow_ComesBeforeTheEncryptionRows()
    {
        XElement recovery = Xaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "RecoveryDataCheckBox")
            .Ancestors().First(e => e.Attribute("Grid.Row") is not null);
        XElement encrypt = Xaml.Descendants().Single(e => (string?)e.Attribute(X + "Uid") == "EncryptWithPasswordCheck");
        XElement passwords = Xaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "EncryptPasswordBox").Parent!;
        recovery.Parent.Should().BeSameAs(encrypt.Parent, "the rows are of one grid").And.BeSameAs(passwords.Parent);

        Row(recovery).Should().BeLessThan(Row(encrypt));
        Row(encrypt).Should().BeLessThan(Row(passwords));
    }

    private static string? Name(XElement element) => (string?)element.Attribute(X + "Name");

    private static int Row(XElement element) => (int)element.Attribute("Grid.Row")!;

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
