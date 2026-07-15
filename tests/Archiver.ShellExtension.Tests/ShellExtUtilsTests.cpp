// ShellExtUtilsTests.cpp
// Unit tests for the COM-free functions in ShellExtUtils.cpp.
// No COM, no DLL loading — pure argument-building and path-classification logic.

#include "pch.h"         // pulled from src/Archiver.ShellExtension via include dir
#include "ShellExtUtils.h"
#include <gtest/gtest.h>

// ---------------------------------------------------------------------------
// AllPathsAreZip / AnyPathIsZip
// ---------------------------------------------------------------------------

TEST(AllPathsAreZip, ReturnsFalseForEmptyVector)
{
    EXPECT_FALSE(AllPathsAreZip({}));
}

TEST(AllPathsAreZip, ReturnsTrueWhenAllZip)
{
    EXPECT_TRUE(AllPathsAreZip({ L"C:\\a.zip", L"C:\\b.zip" }));
}

TEST(AllPathsAreZip, ReturnsFalseWhenMixed)
{
    EXPECT_FALSE(AllPathsAreZip({ L"C:\\a.zip", L"C:\\b.txt" }));
}

TEST(AllPathsAreZip, CaseInsensitive)
{
    EXPECT_TRUE(AllPathsAreZip({ L"C:\\archive.ZIP", L"C:\\data.Zip" }));
}

TEST(AnyPathIsZip, ReturnsFalseForEmptyVector)
{
    EXPECT_FALSE(AnyPathIsZip({}));
}

TEST(AnyPathIsZip, ReturnsTrueWhenOneZip)
{
    EXPECT_TRUE(AnyPathIsZip({ L"C:\\file.txt", L"C:\\archive.zip" }));
}

TEST(AnyPathIsZip, ReturnsFalseWhenNoneAreZip)
{
    EXPECT_FALSE(AnyPathIsZip({ L"C:\\file.txt", L"C:\\image.png" }));
}

// ---------------------------------------------------------------------------
// HasSupportedNonZipArchiveExtension / AllPathsAreSupportedArchive / AnyPathIsSupportedArchive
// (T-F86)
// ---------------------------------------------------------------------------

TEST(HasSupportedNonZipArchiveExtension, RecognizesEachSupportedExtension)
{
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.rar"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.7z"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.tar"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.gz"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.tgz"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.bz2"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.tbz2"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.xz"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.txz"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.zst"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.tzst"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\a.lzma"));
}

TEST(HasSupportedNonZipArchiveExtension, CaseInsensitive)
{
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\ARCHIVE.RAR"));
    EXPECT_TRUE(HasSupportedNonZipArchiveExtension(L"C:\\Archive.7Z"));
}

TEST(HasSupportedNonZipArchiveExtension, ReturnsFalseForZip)
{
    // ZIP is handled separately via HasZipExtension/AllPathsAreZip - not this function.
    EXPECT_FALSE(HasSupportedNonZipArchiveExtension(L"C:\\archive.zip"));
}

TEST(HasSupportedNonZipArchiveExtension, ReturnsFalseForUnrelatedExtension)
{
    EXPECT_FALSE(HasSupportedNonZipArchiveExtension(L"C:\\document.docx"));
    EXPECT_FALSE(HasSupportedNonZipArchiveExtension(L"C:\\noextension"));
}

// AllPathsAreSupportedArchive/AnyPathIsSupportedArchive both OR in HasSupportedNonZipArchiveExtension
// only when TarExeExists() is true. On the machine actually running this test suite,
// C:\Windows\System32\tar.exe is expected to exist (ships with Windows 10 1803+), so these tests
// assert the tar.exe-present behavior directly rather than mocking TarExeExists (no seam exists
// for that - see DECISIONS.md's T-F86 entry for why: a real filesystem check, not a fake, is the
// simplest correct answer to "is extraction possible in principle").
TEST(AllPathsAreSupportedArchive, ReturnsFalseForEmptyVector)
{
    EXPECT_FALSE(AllPathsAreSupportedArchive({}));
}

TEST(AllPathsAreSupportedArchive, TrueForAllZip)
{
    EXPECT_TRUE(AllPathsAreSupportedArchive({ L"C:\\a.zip", L"C:\\b.zip" }));
}

TEST(AllPathsAreSupportedArchive, TrueForAllRar)
{
    EXPECT_TRUE(AllPathsAreSupportedArchive({ L"C:\\a.rar", L"C:\\b.rar" }));
}

TEST(AllPathsAreSupportedArchive, TrueForMixedZipAndSevenZip)
{
    EXPECT_TRUE(AllPathsAreSupportedArchive({ L"C:\\a.zip", L"C:\\b.7z" }));
}

TEST(AllPathsAreSupportedArchive, FalseWhenOnePathIsUnsupported)
{
    EXPECT_FALSE(AllPathsAreSupportedArchive({ L"C:\\a.rar", L"C:\\b.docx" }));
}

TEST(AnyPathIsSupportedArchive, ReturnsFalseForEmptyVector)
{
    EXPECT_FALSE(AnyPathIsSupportedArchive({}));
}

TEST(AnyPathIsSupportedArchive, TrueWhenOneTarFamilyFileAmongOthers)
{
    EXPECT_TRUE(AnyPathIsSupportedArchive({ L"C:\\notes.txt", L"C:\\archive.gz" }));
}

TEST(AnyPathIsSupportedArchive, FalseWhenNoneSupported)
{
    EXPECT_FALSE(AnyPathIsSupportedArchive({ L"C:\\file.txt", L"C:\\image.png" }));
}

// ---------------------------------------------------------------------------
// BuildExtractHereArgs
// ---------------------------------------------------------------------------

TEST(BuildExtractHereArgs, SingleFile)
{
    const auto args = BuildExtractHereArgs({ L"C:\\archive.zip" });
    EXPECT_EQ(args, L"--extract-here \"C:\\archive.zip\"");
}

TEST(BuildExtractHereArgs, MultipleFiles)
{
    const auto args = BuildExtractHereArgs({ L"C:\\a.zip", L"C:\\b.zip" });
    EXPECT_EQ(args, L"--extract-here \"C:\\a.zip\" \"C:\\b.zip\"");
}

TEST(BuildExtractHereArgs, PathWithSpaces)
{
    const auto args = BuildExtractHereArgs({ L"C:\\My Files\\test.zip" });
    EXPECT_EQ(args, L"--extract-here \"C:\\My Files\\test.zip\"");
}

TEST(BuildExtractHereArgs, CyrillicPath)
{
    const auto args = BuildExtractHereArgs({ L"C:\\\u0414\u0430\u043D\u0456.zip" });
    EXPECT_EQ(args, L"--extract-here \"C:\\\u0414\u0430\u043D\u0456.zip\"");
}

// ---------------------------------------------------------------------------
// BuildExtractFolderArgs
// ---------------------------------------------------------------------------

TEST(BuildExtractFolderArgs, SingleFile)
{
    const auto args = BuildExtractFolderArgs({ L"C:\\archive.zip" });
    EXPECT_EQ(args, L"--extract-folder \"C:\\archive.zip\"");
}

TEST(BuildExtractFolderArgs, MultipleFiles)
{
    const auto args = BuildExtractFolderArgs({ L"C:\\a.zip", L"C:\\b.zip" });
    EXPECT_EQ(args, L"--extract-folder \"C:\\a.zip\" \"C:\\b.zip\"");
}

// ---------------------------------------------------------------------------
// BuildArchiveArgs
// ---------------------------------------------------------------------------

TEST(BuildArchiveArgs, SingleFile)
{
    const auto args = BuildArchiveArgs({ L"C:\\document.docx" });
    EXPECT_EQ(args, L"--archive \"C:\\document.docx\"");
}

TEST(BuildArchiveArgs, MultipleFiles)
{
    const auto args = BuildArchiveArgs({ L"C:\\file1.txt", L"C:\\file2.txt" });
    EXPECT_EQ(args, L"--archive \"C:\\file1.txt\" \"C:\\file2.txt\"");
}

TEST(BuildArchiveArgs, PathWithSpacesIsQuoted)
{
    const auto args = BuildArchiveArgs({ L"C:\\Program Files\\app.exe" });
    EXPECT_NE(args.find(L"\"C:\\Program Files\\app.exe\""), std::wstring::npos);
}

// T-F99: a drive root (e.g. "Z:\") ends in a backslash. Quoting it naively as "Z:\" leaves an
// odd number of backslashes before the closing quote, which CommandLineToArgvW/CRT parsing
// reads as an escaped literal quote rather than the end of the argument - corrupting every
// argument after it. Found via a live on-device test: Compress on a drive root silently produced
// an empty pending list because the rest of the command line was swallowed into one argument.
TEST(BuildArchiveArgs, DriveRootTrailingBackslashIsEscaped)
{
    const auto args = BuildArchiveArgs({ L"Z:\\" });
    EXPECT_EQ(args, L"--archive \"Z:\\\\\"");
}

// T-F105: default format ("zip", or the arg omitted entirely) stays flag-less on the command
// line — this is what keeps every BuildArchiveArgs test above unchanged after adding the param.
TEST(BuildArchiveArgs, DefaultFormatOmitsFormatFlag)
{
    const auto args = BuildArchiveArgs({ L"C:\\document.docx" });
    EXPECT_EQ(args, L"--archive \"C:\\document.docx\"");
}

TEST(BuildArchiveArgs, ExplicitZipFormatOmitsFormatFlag)
{
    const auto args = BuildArchiveArgs({ L"C:\\document.docx" }, L"zip");
    EXPECT_EQ(args, L"--archive \"C:\\document.docx\"");
}

TEST(BuildArchiveArgs, TarFormatEmitsFormatFlag)
{
    const auto args = BuildArchiveArgs({ L"C:\\document.docx" }, L"tar");
    EXPECT_EQ(args, L"--archive --format tar \"C:\\document.docx\"");
}

TEST(BuildArchiveArgs, TarFormatMultipleFiles)
{
    const auto args = BuildArchiveArgs({ L"C:\\file1.txt", L"C:\\file2.txt" }, L"tar");
    EXPECT_EQ(args, L"--archive --format tar \"C:\\file1.txt\" \"C:\\file2.txt\"");
}

// ---------------------------------------------------------------------------
// BuildTestArgs
// ---------------------------------------------------------------------------

TEST(BuildTestArgs, SingleFile)
{
    const auto args = BuildTestArgs({ L"C:\\archive.zip" });
    EXPECT_EQ(args, L"--test \"C:\\archive.zip\"");
}

TEST(BuildTestArgs, MultipleFiles)
{
    const auto args = BuildTestArgs({ L"C:\\a.zip", L"C:\\b.zip" });
    EXPECT_EQ(args, L"--test \"C:\\a.zip\" \"C:\\b.zip\"");
}

// ---------------------------------------------------------------------------
// BuildOpenUiExtractArgs (T-F63)
// ---------------------------------------------------------------------------

TEST(BuildOpenUiExtractArgs, SingleFile)
{
    const auto args = BuildOpenUiExtractArgs({ L"C:\\archive.zip" });
    EXPECT_EQ(args, L"--open-ui --extract \"C:\\archive.zip\"");
}

TEST(BuildOpenUiExtractArgs, MultipleFiles)
{
    const auto args = BuildOpenUiExtractArgs({ L"C:\\a.zip", L"C:\\b.zip" });
    EXPECT_EQ(args, L"--open-ui --extract \"C:\\a.zip\" \"C:\\b.zip\"");
}

// ---------------------------------------------------------------------------
// BuildOpenUiArchiveArgs (T-F63)
// ---------------------------------------------------------------------------

TEST(BuildOpenUiArchiveArgs, SingleFile)
{
    const auto args = BuildOpenUiArchiveArgs({ L"C:\\document.docx" });
    EXPECT_EQ(args, L"--open-ui --archive \"C:\\document.docx\"");
}

TEST(BuildOpenUiArchiveArgs, MultipleFiles)
{
    const auto args = BuildOpenUiArchiveArgs({ L"C:\\file1.txt", L"C:\\file2.txt" });
    EXPECT_EQ(args, L"--open-ui --archive \"C:\\file1.txt\" \"C:\\file2.txt\"");
}

// ---------------------------------------------------------------------------
// BuildAddToArchiveTitle
// ---------------------------------------------------------------------------

TEST(BuildAddToArchiveTitle, ReturnsFallbackForEmptyVector)
{
    EXPECT_EQ(BuildAddToArchiveTitle({}), L"Add to archive\u2026");
}

TEST(BuildAddToArchiveTitle, SingleFileUsesNameWithoutExtension)
{
    EXPECT_EQ(BuildAddToArchiveTitle({ L"C:\\Docs\\report.docx" }), L"Add to \"report.zip\"");
}

// T-F103: a compound tar extension must be stripped as a unit, not just the last dot segment
// (e.g. "backup.tar.gz" must not become "backup.tar.zip").
TEST(BuildAddToArchiveTitle, CompoundTarExtensionStripsBothComponents)
{
    EXPECT_EQ(BuildAddToArchiveTitle({ L"C:\\Docs\\backup.tar.gz" }), L"Add to \"backup.zip\"");
}

TEST(BuildAddToArchiveTitle, MultipleFilesUseContainingFolderName)
{
    const auto title = BuildAddToArchiveTitle(
        { L"C:\\Projects\\MyStuff\\first.txt", L"C:\\Projects\\MyStuff\\second.txt" });
    EXPECT_EQ(title, L"Add to \"MyStuff.zip\"");
}

TEST(BuildAddToArchiveTitle, MultipleFilesAtDriveRootFallsBackToArchive)
{
    const auto title = BuildAddToArchiveTitle({ L"C:\\first.txt", L"C:\\second.txt" });
    EXPECT_EQ(title, L"Add to \"archive.zip\"");
}

// T-F99: a single drive-root selection (e.g. "Z:\") is a distinct case from the
// multi-file-at-drive-root case above — PathFindFileNameW returns the whole "Z:\" string
// unchanged (not an empty tail), so it needs its own trailing-backslash check to fall back.
TEST(BuildAddToArchiveTitle, SingleDriveRootFallsBackToArchive)
{
    const auto title = BuildAddToArchiveTitle({ L"Z:\\" });
    EXPECT_EQ(title, L"Add to \"archive.zip\"");
}

TEST(BuildAddToArchiveTitle, FolderWithNoExtensionKeepsFullName)
{
    EXPECT_EQ(BuildAddToArchiveTitle({ L"C:\\Projects\\MyFolder" }), L"Add to \"MyFolder.zip\"");
}

TEST(BuildAddToArchiveTitle, LeadingDotIsNotTreatedAsExtension)
{
    EXPECT_EQ(BuildAddToArchiveTitle({ L"C:\\Projects\\.gitignore" }), L"Add to \".gitignore.zip\"");
}

TEST(BuildAddToArchiveTitle, NameAtLimitIsNotTruncated)
{
    // 40 chars exactly — must pass through unchanged.
    const std::wstring name(40, L'a');
    EXPECT_EQ(BuildAddToArchiveTitle({ L"C:\\" + name + L".txt" }), L"Add to \"" + name + L".zip\"");
}

TEST(BuildAddToArchiveTitle, NameOverLimitIsTruncatedInTheMiddle)
{
    const auto title = BuildAddToArchiveTitle(
        { L"C:\\My Very Long Project Folder Name With Lots Of Words 2026 Final Report.txt" });
    EXPECT_EQ(title, L"Add to \"My Very Long Project F\u202626 Final Report.zip\"");
}

// T-F105: TarArchiveCommand::GetTitle passes L".tar" explicitly \u2014 mirrors the default-.zip
// cases above for the extension-parameterized paths (drive-root fallback, compound-extension
// stripping, truncation), since those all run through the same shared code before the extension
// is appended at the very end.
TEST(BuildAddToArchiveTitle, TarExtensionSingleFileUsesNameWithoutExtension)
{
    EXPECT_EQ(BuildAddToArchiveTitle({ L"C:\\Docs\\report.docx" }, L".tar"), L"Add to \"report.tar\"");
}

TEST(BuildAddToArchiveTitle, TarExtensionCompoundTarExtensionStripsBothComponents)
{
    EXPECT_EQ(BuildAddToArchiveTitle({ L"C:\\Docs\\backup.tar.gz" }, L".tar"), L"Add to \"backup.tar\"");
}

TEST(BuildAddToArchiveTitle, TarExtensionMultipleFilesAtDriveRootFallsBackToArchive)
{
    const auto title = BuildAddToArchiveTitle({ L"C:\\first.txt", L"C:\\second.txt" }, L".tar");
    EXPECT_EQ(title, L"Add to \"archive.tar\"");
}

TEST(BuildAddToArchiveTitle, TarExtensionSingleDriveRootFallsBackToArchive)
{
    const auto title = BuildAddToArchiveTitle({ L"Z:\\" }, L".tar");
    EXPECT_EQ(title, L"Add to \"archive.tar\"");
}

TEST(BuildAddToArchiveTitle, TarExtensionNameOverLimitIsTruncatedInTheMiddle)
{
    const auto title = BuildAddToArchiveTitle(
        { L"C:\\My Very Long Project Folder Name With Lots Of Words 2026 Final Report.txt" }, L".tar");
    EXPECT_EQ(title, L"Add to \"My Very Long Project F\u202626 Final Report.tar\"");
}

// ---------------------------------------------------------------------------
// BuildExtractFolderTitle
// ---------------------------------------------------------------------------

TEST(BuildExtractFolderTitle, ReturnsFallbackForEmptyVector)
{
    EXPECT_EQ(BuildExtractFolderTitle({}), L"Extract to folder");
}

TEST(BuildExtractFolderTitle, SingleArchiveUsesNameWithoutExtension)
{
    EXPECT_EQ(BuildExtractFolderTitle({ L"C:\\Docs\\report.zip" }), L"Extract to \"report\\\"");
}

// T-F103: "browse_test.tar.gz" must extract to "browse_test\", not "browse_test.tar\".
TEST(BuildExtractFolderTitle, CompoundTarExtensionStripsBothComponents)
{
    EXPECT_EQ(BuildExtractFolderTitle({ L"C:\\Docs\\browse_test.tar.gz" }), L"Extract to \"browse_test\\\"");
}

TEST(BuildExtractFolderTitle, MultipleArchivesDoNotClaimASingleName)
{
    const auto title = BuildExtractFolderTitle({ L"C:\\a.zip", L"C:\\b.zip" });
    EXPECT_EQ(title, L"Extract each to its own folder");
}

TEST(BuildExtractFolderTitle, LeadingDotIsNotTreatedAsExtension)
{
    EXPECT_EQ(BuildExtractFolderTitle({ L"C:\\Projects\\.gitignore.zip" }), L"Extract to \".gitignore\\\"");
}

TEST(BuildExtractFolderTitle, NameAtLimitIsNotTruncated)
{
    // 40 chars exactly \u2014 must pass through unchanged.
    const std::wstring name(40, L'a');
    EXPECT_EQ(BuildExtractFolderTitle({ L"C:\\" + name + L".zip" }), L"Extract to \"" + name + L"\\\"");
}

TEST(BuildExtractFolderTitle, NameOverLimitIsTruncatedInTheMiddle)
{
    const auto title = BuildExtractFolderTitle(
        { L"C:\\My Very Long Project Folder Name With Lots Of Words 2026 Final Report.zip" });
    EXPECT_EQ(title, L"Extract to \"My Very Long Project F\u202626 Final Report\\\"");
}
