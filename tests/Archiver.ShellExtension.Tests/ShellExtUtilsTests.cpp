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
