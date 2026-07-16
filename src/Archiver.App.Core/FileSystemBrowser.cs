namespace Archiver.App.Core;

/// <summary>
/// Lists real Windows filesystem folders/drives for the Archive Browser's "climb past the
/// archive root" navigation (T-F107) — reuses ArchiveEntryViewModel unchanged (its optional
/// CompressedSize/Crc32 fields already render as "not applicable" for a plain file/folder).
/// Lives in Archiver.App.Core, not Archiver.App, for the same reason ArchiveTreeIndex does:
/// unit-testable without a WinUI test host.
/// </summary>
public static class FileSystemBrowser
{
    public static IReadOnlyList<ArchiveEntryViewModel> ListFolder(string path)
    {
        try
        {
            var result = new List<ArchiveEntryViewModel>();

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var info = new DirectoryInfo(dir);
                result.Add(new ArchiveEntryViewModel
                {
                    FullPath = dir,
                    Name = info.Name,
                    IsFolder = true,
                    Modified = info.LastWriteTime,
                });
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                var info = new FileInfo(file);
                result.Add(new ArchiveEntryViewModel
                {
                    FullPath = file,
                    Name = info.Name,
                    IsFolder = false,
                    Size = info.Length,
                    Modified = info.LastWriteTime,
                });
            }

            // Folders first, then files, both alphabetical — matches ArchiveTreeIndex's own
            // ordering and File Explorer's default sort.
            result.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder)
                    return a.IsFolder ? -1 : 1;
                return string.CompareOrdinal(a.Name, b.Name);
            });

            return result;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A restricted/inaccessible folder just looks empty — matches this codebase's
            // "never throw to callers" convention rather than surfacing an error dialog for
            // every permission-denied system folder encountered while browsing.
            return [];
        }
    }

    public static IReadOnlyList<ArchiveEntryViewModel> ListDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d =>
            {
                string label = SafeVolumeLabel(d);
                return new ArchiveEntryViewModel
                {
                    FullPath = d.RootDirectory.FullName,
                    Name = string.IsNullOrEmpty(label) ? d.Name : $"{label} ({d.Name.TrimEnd('\\')})",
                    IsFolder = true,
                };
            })
            .ToList();

    // DriveInfo.VolumeLabel can throw for a drive that reports IsReady=true but becomes
    // unavailable between the check and the property read (e.g. a removable drive ejected
    // mid-enumeration) — same defensive shape as ListFolder's own catch.
    private static string SafeVolumeLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
