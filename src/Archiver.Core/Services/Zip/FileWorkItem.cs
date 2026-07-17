namespace Archiver.Core.Services.Zip;

internal enum FileWorkKind { File, DirectoryPlaceholder }

/// <summary>
/// One unit of work produced by <see cref="WorkItemEnumerator"/> in the exact deterministic
/// order (T-F31/T-F32) the final ZIP's entries must be written in. <see cref="SourcePath"/> is
/// empty for <see cref="FileWorkKind.DirectoryPlaceholder"/> (T-F66 empty-folder entries — no
/// source file to read).
/// </summary>
internal readonly record struct FileWorkItem(
    string SourcePath,
    string EntryName,
    FileWorkKind Kind,
    long FileSize,
    DateTime LastWriteTime);
