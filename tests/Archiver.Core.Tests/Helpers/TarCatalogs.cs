using Archiver.Core.Services.Sandbox;

namespace Archiver.Core.Tests.Helpers;

/// <summary>T-F541: a tar.exe catalog of explicit entries: a name ending in '/' is a folder, every other a file of its size.</summary>
internal static class TarCatalogs
{
    public static TarCatalog Of(params (string Name, long Size)[] entries) => new(
        entries.Where(e => !e.Name.EndsWith('/')).Sum(e => e.Size),
        [.. entries.Select(e => e.Name)],
        [.. entries.Select(e => e.Name.EndsWith('/') ? -1 : e.Size)]);
}
