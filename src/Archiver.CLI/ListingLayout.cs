namespace Archiver.CLI;

/// <summary>
/// T-F412: how 'l' prints one archive: the '# archive' lines, the header, 7-Zip's -slt blocks.
/// Its own file (T-F440, S3903): beside the top-level statements it sat in the global namespace.
/// </summary>
internal readonly record struct ListingLayout(bool Multiple, bool Header, bool Technical);
