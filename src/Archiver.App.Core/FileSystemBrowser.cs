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
    /// <summary>Lists a real folder's immediate children as browser rows (folders first, then files).</summary>
    public static IReadOnlyList<ArchiveEntryViewModel> ListFolder(string path)
    {
        try
        {
            var result = new List<ArchiveEntryViewModel>();

            foreach (string dir in Directory.EnumerateDirectories(path))
            {
                var info = new DirectoryInfo(dir);
                if (IsProtectedSystemEntry(info.Attributes))
                    continue;
                result.Add(new ArchiveEntryViewModel
                {
                    FullPath = dir,
                    Name = info.Name,
                    IsFolder = true,
                    Modified = info.LastWriteTime,
                });
            }

            foreach (string file in Directory.EnumerateFiles(path))
            {
                var info = new FileInfo(file);
                if (IsProtectedSystemEntry(info.Attributes))
                    continue;
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

    // T-F324: Hidden and System together is what Explorer hides as "protected operating system
    // files" ($Recycle.Bin, pagefile.sys). Hidden alone stays listed: Up from an archive under
    // %TEMP% climbs through AppData, and the folder just left must be in its parent's list.
    private static bool IsProtectedSystemEntry(FileAttributes attributes)
    {
        const FileAttributes Protected = FileAttributes.Hidden | FileAttributes.System;
        return (attributes & Protected) == Protected;
    }

    /// <summary>Lists every ready drive as browser rows for the synthetic "This PC" node.</summary>
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
