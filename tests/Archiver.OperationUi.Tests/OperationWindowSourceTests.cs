using FluentAssertions;

namespace Archiver.OperationUi.Tests;

// T-F487: the window's root follows the locale's direction, and in ar-SA, he-IL and ur-PK its title
// bar inherited RightToLeft, laying "Pakko" under the caption buttons, which stay on the right.
// OperationWindow is WinUI and has no test host, so the test reads its source, as the App's
// MainWindowLayoutTests read MainWindow.xaml.
public sealed class OperationWindowSourceTests
{
    [Fact]
    public void TitleBar_IsLeftToRight_WhateverTheLocale()
    {
        string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Archiver.OperationUi", "OperationWindow.cs"));

        string titleBar = source.Split('\n').Single(l => l.Contains("var titleBar = new Grid", StringComparison.Ordinal));

        titleBar.Should().Contain("FlowDirection = FlowDirection.LeftToRight");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
