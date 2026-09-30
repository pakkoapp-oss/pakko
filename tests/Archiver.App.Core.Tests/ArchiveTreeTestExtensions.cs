namespace Archiver.App.Core.Tests;

internal static class ArchiveTreeTestExtensions
{
    public static IReadOnlyList<ArchiveEntryViewModel> At(this ArchiveTree tree, string folderPath) =>
        tree.TryGetChildren(folderPath, out IReadOnlyList<ArchiveEntryViewModel> children)
            ? children
            : throw new KeyNotFoundException(folderPath);
}
