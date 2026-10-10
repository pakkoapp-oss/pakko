// ShellExtUtilsTests.cpp
// Unit tests for the COM-free functions in ShellExtUtils.cpp.
// No COM, no DLL loading — pure argument-building and path-classification logic.

#include "pch.h"         // pulled from src/Archiver.ShellExtension via include dir
#include "ShellExtUtils.h"
#include <gtest/gtest.h>

// No Group Policy configured - the shipped default (T-F262).
static const MenuPolicy kNoPolicy{};

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
    EXPECT_FALSE(AnyPathIsZip({}, kNoPolicy));
}

TEST(AnyPathIsZip, ReturnsTrueWhenOneZip)
{
    EXPECT_TRUE(AnyPathIsZip({ L"C:\\file.txt", L"C:\\archive.zip" }, kNoPolicy));
}

TEST(AnyPathIsZip, ReturnsFalseWhenNoneAreZip)
{
    EXPECT_FALSE(AnyPathIsZip({ L"C:\\file.txt", L"C:\\image.png" }, kNoPolicy));
}

// T-F131: .jar/.war/.ear/.apk are real ZIP-format containers, treated as ZIP for gating purposes.
TEST(AllPathsAreZip, TrueForJarWarEarApk)
{
    EXPECT_TRUE(AllPathsAreZip({ L"C:\\app.jar", L"C:\\site.war", L"C:\\enterprise.ear", L"C:\\app.apk" }));
}

TEST(AnyPathIsZip, TrueForJarAmongOthers)
{
    EXPECT_TRUE(AnyPathIsZip({ L"C:\\file.txt", L"C:\\build.jar" }, kNoPolicy));
}

TEST(AllPathsAreZip, JarCaseInsensitive)
{
    EXPECT_TRUE(AllPathsAreZip({ L"C:\\App.JAR", L"C:\\Site.War" }));
}

// T-F133: .asice/.asics/.bdoc (ASiC signed containers) are also real ZIP-format containers.
TEST(AllPathsAreZip, TrueForAsiceAsicsBdoc)
{
    EXPECT_TRUE(AllPathsAreZip({ L"C:\\signed.asice", L"C:\\signed.asics", L"C:\\document.bdoc" }));
}

TEST(AnyPathIsZip, TrueForAsiceAmongOthers)
{
    EXPECT_TRUE(AnyPathIsZip({ L"C:\\file.txt", L"C:\\signed.asice" }, kNoPolicy));
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
    EXPECT_FALSE(AllPathsAreSupportedArchive({}, kNoPolicy));
}

TEST(AllPathsAreSupportedArchive, TrueForAllZip)
{
    EXPECT_TRUE(AllPathsAreSupportedArchive({ L"C:\\a.zip", L"C:\\b.zip" }, kNoPolicy));
}

TEST(AllPathsAreSupportedArchive, TrueForAllRar)
{
    EXPECT_TRUE(AllPathsAreSupportedArchive({ L"C:\\a.rar", L"C:\\b.rar" }, kNoPolicy));
}

TEST(AllPathsAreSupportedArchive, TrueForMixedZipAndSevenZip)
{
    EXPECT_TRUE(AllPathsAreSupportedArchive({ L"C:\\a.zip", L"C:\\b.7z" }, kNoPolicy));
}

TEST(AllPathsAreSupportedArchive, FalseWhenOnePathIsUnsupported)
{
    EXPECT_FALSE(AllPathsAreSupportedArchive({ L"C:\\a.rar", L"C:\\b.docx" }, kNoPolicy));
}

TEST(AnyPathIsSupportedArchive, ReturnsFalseForEmptyVector)
{
    EXPECT_FALSE(AnyPathIsSupportedArchive({}, kNoPolicy));
}

TEST(AnyPathIsSupportedArchive, TrueWhenOneTarFamilyFileAmongOthers)
{
    EXPECT_TRUE(AnyPathIsSupportedArchive({ L"C:\\notes.txt", L"C:\\archive.gz" }, kNoPolicy));
}

TEST(AnyPathIsSupportedArchive, FalseWhenNoneSupported)
{
    EXPECT_FALSE(AnyPathIsSupportedArchive({ L"C:\\file.txt", L"C:\\image.png" }, kNoPolicy));
}

// ---------------------------------------------------------------------------
// Group Policy for the menu (T-F262)
// ---------------------------------------------------------------------------

namespace
{
    // Hand-rolled fake: a value is present only if the test put it there.
    class FakePolicyReader final : public PolicyRegistryReader
    {
    public:
        std::optional<DWORD> disableTar;
        std::optional<std::vector<std::wstring>> blockedFormats;
        std::optional<std::vector<std::wstring>> allowedFormats;
        std::optional<DWORD> disableRecoveryData;

        std::optional<DWORD> GetDword(const wchar_t* valueName) const override
        {
            if (std::wstring(valueName) == L"DisableRecoveryData") return disableRecoveryData;
            return std::wstring(valueName) == L"DisableTarExtraction" ? disableTar : std::nullopt;
        }

        std::optional<std::vector<std::wstring>> GetMultiString(const wchar_t* valueName) const override
        {
            if (std::wstring(valueName) == L"BlockedFormats") return blockedFormats;
            if (std::wstring(valueName) == L"AllowedFormats") return allowedFormats;
            return std::nullopt;
        }
    };

    MenuPolicy PolicyFrom(std::optional<DWORD> disableTar, std::optional<std::vector<std::wstring>> blocked)
    {
        FakePolicyReader reader;
        reader.disableTar = disableTar;
        reader.blockedFormats = std::move(blocked);
        return LoadMenuPolicy(reader);
    }

    const std::vector<std::wstring> kOneOfEachFormat = {
        L"C:\\a.zip", L"C:\\a.jar", L"C:\\a.tar", L"C:\\a.gz", L"C:\\a.tgz", L"C:\\a.tar.gz", L"C:\\a.bz2",
        L"C:\\a.tbz2", L"C:\\a.xz", L"C:\\a.txz", L"C:\\a.zst", L"C:\\a.tzst", L"C:\\a.lzma", L"C:\\a.rar", L"C:\\a.7z",
    };
}

TEST(GetFormatRegistryName, MapsEachExtensionToTheGroupPolicyVocabulary)
{
    // Same names as Archiver.Core's ArchiveFormatRegistryNames, by final extension.
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.zip"), L"zip");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.APK"), L"zip");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.bdoc"), L"zip");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.tar"), L"tar");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.tar.gz"), L"gzip");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.tgz"), L"gzip");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.tbz2"), L"bz2");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.txz"), L"xz");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.tzst"), L"zstd");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.lzma"), L"lzma");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.rar"), L"rar");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.7z"), L"sevenzip");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\a.docx"), L"");
    EXPECT_EQ(GetFormatRegistryName(L"C:\\noextension"), L"");
}

// --- Happy: nothing configured hides nothing ---

TEST(MenuPolicy, NothingConfiguredRestrictsNothing)
{
    const MenuPolicy policy = PolicyFrom(std::nullopt, std::nullopt);

    EXPECT_FALSE(policy.disableTar);
    EXPECT_TRUE(policy.blockedFormats.empty());
    EXPECT_TRUE(AllPathsAreSupportedArchive(kOneOfEachFormat, policy));
    EXPECT_TRUE(AnyPathIsZip({ L"C:\\a.zip" }, policy));
    EXPECT_TRUE(IsCreationFormatAllowed(L"zip", policy));
    EXPECT_TRUE(IsCreationFormatAllowed(L"tar", policy));
}

// --- DisableTarExtraction=1: every tar.exe-backed item hides, ZIP stays ---

TEST(MenuPolicy, DisableTarHidesEveryTarFamilyExtractionAndTarCreationOnly)
{
    const MenuPolicy policy = PolicyFrom(1, std::nullopt);

    for (const auto& path : kOneOfEachFormat)
    {
        const bool isZip = GetFormatRegistryName(path) == L"zip";
        EXPECT_EQ(AllPathsAreSupportedArchive({ path }, policy), isZip) << path;
        EXPECT_EQ(AnyPathIsSupportedArchive({ path }, policy), isZip) << path;
    }
    EXPECT_TRUE(AnyPathIsZip({ L"C:\\a.zip" }, policy));
    EXPECT_TRUE(IsCreationFormatAllowed(L"zip", policy));
    EXPECT_FALSE(IsCreationFormatAllowed(L"tar", policy));
}

TEST(MenuPolicy, DisableTarWithMixedSelectionHidesOneClickExtractButKeepsAnyGates)
{
    const MenuPolicy policy = PolicyFrom(1, std::nullopt);

    // "Extract here" needs every item extractable; the dialog/scan gates need just one.
    EXPECT_FALSE(AllPathsAreSupportedArchive({ L"C:\\a.zip", L"C:\\b.7z" }, policy));
    EXPECT_TRUE(AnyPathIsSupportedArchive({ L"C:\\a.zip", L"C:\\b.7z" }, policy));
}

// --- BlockedFormats: exactly the named formats hide ---

TEST(MenuPolicy, BlockedSevenZipHidesOnlySevenZip)
{
    const MenuPolicy policy = PolicyFrom(std::nullopt, std::vector<std::wstring>{ L"sevenzip" });

    for (const auto& path : kOneOfEachFormat)
        EXPECT_EQ(AllPathsAreSupportedArchive({ path }, policy), GetFormatRegistryName(path) != L"sevenzip") << path;
    EXPECT_TRUE(IsCreationFormatAllowed(L"zip", policy));
    EXPECT_TRUE(IsCreationFormatAllowed(L"tar", policy));
}

TEST(MenuPolicy, BlockedZipHidesZipExtractionTestAndZipCreation)
{
    const MenuPolicy policy = PolicyFrom(std::nullopt, std::vector<std::wstring>{ L"zip" });

    EXPECT_FALSE(AllPathsAreSupportedArchive({ L"C:\\a.zip" }, policy));
    EXPECT_FALSE(AllPathsAreSupportedArchive({ L"C:\\a.jar" }, policy));
    EXPECT_FALSE(AnyPathIsZip({ L"C:\\a.zip", L"C:\\b.txt" }, policy));
    EXPECT_FALSE(IsCreationFormatAllowed(L"zip", policy));
    EXPECT_TRUE(IsCreationFormatAllowed(L"tar", policy));
    EXPECT_TRUE(AllPathsAreSupportedArchive({ L"C:\\a.rar" }, policy));
}

TEST(MenuPolicy, BlockedTarHidesPlainTarOnlyNotCompressedTar)
{
    // Same as Core: a .tar.gz is detected as gzip, so blocking "tar" leaves it extractable.
    const MenuPolicy policy = PolicyFrom(std::nullopt, std::vector<std::wstring>{ L"tar" });

    EXPECT_FALSE(AllPathsAreSupportedArchive({ L"C:\\a.tar" }, policy));
    EXPECT_TRUE(AllPathsAreSupportedArchive({ L"C:\\a.tar.gz" }, policy));
    EXPECT_FALSE(IsCreationFormatAllowed(L"tar", policy));
}

TEST(MenuPolicy, BlockedFormatNamesMatchCaseInsensitively)
{
    const MenuPolicy policy = PolicyFrom(std::nullopt, std::vector<std::wstring>{ L"SevenZip", L"RAR" });

    EXPECT_FALSE(AllPathsAreSupportedArchive({ L"C:\\a.7z" }, policy));
    EXPECT_FALSE(AllPathsAreSupportedArchive({ L"C:\\a.rar" }, policy));
}

// --- Misuse: values that are not "configured" in GroupPolicyService's terms ---

TEST(MenuPolicy, DisableTarOtherThanOneIsIgnored)
{
    EXPECT_FALSE(PolicyFrom(0, std::nullopt).disableTar);
    EXPECT_FALSE(PolicyFrom(2, std::nullopt).disableTar);
    EXPECT_FALSE(PolicyFrom(0xFFFFFFFF, std::nullopt).disableTar);
}

TEST(MenuPolicy, UnknownOrEmptyBlockedNamesHideNothing)
{
    const MenuPolicy policy = PolicyFrom(std::nullopt, std::vector<std::wstring>{ L"exe", L"", L" zip " });

    EXPECT_TRUE(AllPathsAreSupportedArchive(kOneOfEachFormat, policy));
    EXPECT_TRUE(IsCreationFormatAllowed(L"zip", policy));
}

TEST(MenuPolicy, EmptyBlockedListRestrictsNothing)
{
    EXPECT_TRUE(AllPathsAreSupportedArchive(kOneOfEachFormat, PolicyFrom(std::nullopt, std::vector<std::wstring>{})));
}

TEST(MenuPolicy, AllowedFormatsIsNotReadByTheMenu)
{
    // Only DisableTarExtraction and BlockedFormats are in scope for the menu (T-F262's decision).
    FakePolicyReader reader;
    reader.allowedFormats = std::vector<std::wstring>{ L"zip" };

    EXPECT_TRUE(AllPathsAreSupportedArchive({ L"C:\\a.7z" }, LoadMenuPolicy(reader)));
}

// --- Error: the real registry reader never fails loudly ---

TEST(Win32PolicyRegistryReader, MissingKeyReadsAsNotConfigured)
{
    const Win32PolicyRegistryReader reader(L"Software\\Policies\\Pakko_T-F262_NoSuchKey");

    EXPECT_FALSE(reader.GetDword(L"DisableTarExtraction").has_value());
    EXPECT_FALSE(reader.GetMultiString(L"BlockedFormats").has_value());
    EXPECT_FALSE(LoadMenuPolicy(reader).disableTar);
    EXPECT_TRUE(LoadMenuPolicy(reader).blockedFormats.empty());
}

TEST(Win32PolicyRegistryReader, WrongValueTypeReadsAsNotConfigured)
{
    // ProductName is a REG_SZ on every Windows install.
    const Win32PolicyRegistryReader reader(L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");

    EXPECT_FALSE(reader.GetDword(L"ProductName").has_value());
    EXPECT_FALSE(reader.GetMultiString(L"ProductName").has_value());
}

TEST(Win32PolicyRegistryReader, RealValuesAreRead)
{
    // CurrentMajorVersionNumber is a REG_DWORD on Windows 10/11.
    const Win32PolicyRegistryReader reader(L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");

    const auto major = reader.GetDword(L"CurrentMajorVersionNumber");
    ASSERT_TRUE(major.has_value());
    EXPECT_EQ(*major, 10u);
}

// ---------------------------------------------------------------------------
// T-F275 step 3b: "Verify with PAR2" is shown only when there is something to verify
// ---------------------------------------------------------------------------

namespace
{
    // A folder as the lister sees it: every name is returned whatever the pattern, so a test also
    // proves the names are checked and not just trusted to the pattern.
    struct FakeFolder
    {
        std::vector<std::wstring> names;
        mutable std::vector<std::wstring> patterns;

        FolderLister Lister() const
        {
            return [this](const std::wstring& pattern) { patterns.push_back(pattern); return names; };
        }
    };

    MenuPolicy RecoveryPolicy(std::optional<DWORD> disableRecoveryData)
    {
        FakePolicyReader reader;
        reader.disableRecoveryData = disableRecoveryData;
        return LoadMenuPolicy(reader);
    }
}

// --- Happy ---

TEST(AnyPathHasRecoveryData, Par2FileAloneNeedsNoLookAtTheDisk)
{
    const FakeFolder folder{};

    EXPECT_TRUE(AnyPathHasRecoveryData({ L"C:\\d\\notes.tar.gz.par2" }, kNoPolicy, folder.Lister()));
    EXPECT_TRUE(AnyPathHasRecoveryData({ L"C:\\d\\notes.tar.gz.vol0+10.PAR2" }, kNoPolicy, folder.Lister()));
    EXPECT_TRUE(folder.patterns.empty());
}

TEST(AnyPathHasRecoveryData, ArchiveWithItsSetUnderTheFullName)
{
    const FakeFolder folder{ { L"notes.tar.gz.par2", L"notes.tar.gz.vol0+10.par2" } };

    EXPECT_TRUE(AnyPathHasRecoveryData({ L"C:\\d\\notes.tar.gz" }, kNoPolicy, folder.Lister()));
    ASSERT_EQ(folder.patterns.size(), 1u);
    EXPECT_EQ(folder.patterns[0], L"C:\\d\\notes.tar.*par2");
}

TEST(AnyPathHasRecoveryData, ArchiveWithItsSetUnderTheShortName)
{
    // par2cmdline's own habit: "par2 c photos photos.zip" writes photos.par2.
    const FakeFolder folder{ { L"photos.par2", L"photos.vol000+02.par2" } };

    EXPECT_TRUE(AnyPathHasRecoveryData({ L"C:\\d\\photos.zip" }, kNoPolicy, folder.Lister()));
    EXPECT_EQ(folder.patterns[0], L"C:\\d\\photos.*par2");
}

TEST(AnyPathHasRecoveryData, PartOfAMixedSelection)
{
    const FakeFolder folder{};

    EXPECT_TRUE(AnyPathHasRecoveryData({ L"C:\\d\\readme.txt", L"C:\\d\\a.zip", L"C:\\d\\a.zip.par2" }, kNoPolicy, folder.Lister()));
}

// --- The set is half there ---

TEST(AnyPathHasRecoveryData, VolumesWithoutAnIndexStillCount)
{
    // Core checks an archive against volumes alone, so the item must not vanish with the index.
    const FakeFolder folder{ { L"notes.tar.gz.vol0+10.par2" } };

    EXPECT_TRUE(AnyPathHasRecoveryData({ L"C:\\d\\notes.tar.gz" }, kNoPolicy, folder.Lister()));
}

TEST(AnyPathHasRecoveryData, TemporaryFilesOfAnInterruptedRunAreNotASet)
{
    const FakeFolder folder{ { L".pakko-a-1234.tmp", L"notes.tar.gz", L"notes.tar.gz.par2.tmp" } };

    EXPECT_FALSE(AnyPathHasRecoveryData({ L"C:\\d\\notes.tar.gz" }, kNoPolicy, folder.Lister()));
}

// --- Another file's set ---

TEST(AnyPathHasRecoveryData, SetOfAFileWhoseNameOnlyStartsTheSameIsNotThisOnes)
{
    const FakeFolder folder{ { L"photos2.zip.par2", L"photosXpar2", L"photos.par2x", L"photos-old.par2" } };

    EXPECT_FALSE(AnyPathHasRecoveryData({ L"C:\\d\\photos.zip" }, kNoPolicy, folder.Lister()));
}

TEST(IsRecoverySetFileName, FollowsCoresRule)
{
    EXPECT_TRUE(IsRecoverySetFileName(L"a.zip.par2", L"a.zip"));
    EXPECT_TRUE(IsRecoverySetFileName(L"A.ZIP.Vol00+01.PAR2", L"a.zip"));
    EXPECT_TRUE(IsRecoverySetFileName(L"a.zip.par2", L"a"));
    EXPECT_TRUE(IsRecoverySetFileName(L"a.anything.par2", L"a"));
    EXPECT_FALSE(IsRecoverySetFileName(L"a.zip", L"a.zip"));
    EXPECT_FALSE(IsRecoverySetFileName(L"a.zippar2", L"a.zip"));
    EXPECT_FALSE(IsRecoverySetFileName(L"a.zip.par2", L"a.zi"));
    EXPECT_FALSE(IsRecoverySetFileName(L"a.zip.par2.bak", L"a.zip"));
    EXPECT_FALSE(IsRecoverySetFileName(L".par2", L""));
    EXPECT_FALSE(IsRecoverySetFileName(L"", L"a"));
}

// --- Misuse and boundaries ---

TEST(AnyPathHasRecoveryData, NothingToVerify)
{
    const FakeFolder empty{};
    const FakeFolder withSet{ { L"notes.par2", L"notes.txt.par2" } };

    EXPECT_FALSE(AnyPathHasRecoveryData({}, kNoPolicy, empty.Lister()));
    EXPECT_FALSE(AnyPathHasRecoveryData({ L"C:\\d\\a.zip" }, kNoPolicy, empty.Lister()));
    // Not an archive: Pakko's menu verifies archives; the .par2 file itself still offers the item.
    EXPECT_FALSE(AnyPathHasRecoveryData({ L"C:\\d\\notes.txt", L"C:\\d\\folder" }, kNoPolicy, withSet.Lister()));
    EXPECT_TRUE(withSet.patterns.empty());
}

TEST(AnyPathHasRecoveryData, OnlyTheFirstArchivesOfALargeSelectionAreLookedAt)
{
    FakeFolder folder;
    std::vector<std::wstring> paths;
    for (size_t i = 0; i < kMaxRecoveryProbes + 50; ++i)
        paths.push_back(L"C:\\d\\a" + std::to_wstring(i) + L".zip");

    EXPECT_FALSE(AnyPathHasRecoveryData(paths, kNoPolicy, folder.Lister()));
    EXPECT_EQ(folder.patterns.size(), kMaxRecoveryProbes);

    // A .par2 anywhere in the selection costs nothing to see.
    paths.push_back(L"C:\\d\\a0.zip.par2");
    folder.patterns.clear();
    EXPECT_TRUE(AnyPathHasRecoveryData(paths, kNoPolicy, folder.Lister()));
    EXPECT_TRUE(folder.patterns.empty());
}

// --- Group Policy ---

TEST(AnyPathHasRecoveryData, HiddenUnderDisableRecoveryData)
{
    const FakeFolder folder{ { L"a.zip.par2" } };
    const MenuPolicy policy = RecoveryPolicy(1u);

    EXPECT_TRUE(policy.disableRecoveryData);
    EXPECT_FALSE(AnyPathHasRecoveryData({ L"C:\\d\\a.zip", L"C:\\d\\a.zip.par2" }, policy, folder.Lister()));
    EXPECT_TRUE(folder.patterns.empty());
}

TEST(AnyPathHasRecoveryData, OnlyTheValueOneDisables)
{
    EXPECT_FALSE(RecoveryPolicy(std::nullopt).disableRecoveryData);
    EXPECT_FALSE(RecoveryPolicy(0u).disableRecoveryData);
    EXPECT_FALSE(RecoveryPolicy(2u).disableRecoveryData);
}

TEST(AnyPathHasRecoveryData, AnArchiveWhoseFormatIsBlockedIsNotLookedAt)
{
    const FakeFolder folder{ { L"a.zip.par2", L"a.tar.gz.par2" } };

    EXPECT_FALSE(AnyPathHasRecoveryData({ L"C:\\d\\a.zip" }, PolicyFrom(std::nullopt, std::vector<std::wstring>{ L"zip" }), folder.Lister()));
    EXPECT_FALSE(AnyPathHasRecoveryData({ L"C:\\d\\a.tar.gz" }, PolicyFrom(1u, std::nullopt), folder.Lister()));
    EXPECT_TRUE(folder.patterns.empty());
}

// --- The real folder listing ---

TEST(ListFolderNames, FindsASetLeftWithOnlyItsVolume_AndNothingInAMissingFolder)
{
    wchar_t temp[MAX_PATH]{};
    ASSERT_NE(GetTempPathW(MAX_PATH, temp), 0u);
    const std::wstring dir = std::wstring(temp) + L"PakkoRecoveryProbe_" + std::to_wstring(GetCurrentProcessId()) + L"_" + std::to_wstring(GetTickCount64());
    ASSERT_TRUE(CreateDirectoryW(dir.c_str(), nullptr));
    const std::vector<std::wstring> files = { L"notes.tar.gz", L"notes.tar.gz.vol0+10.par2", L".pakko-a-1.tmp", L"other.zip.par2" };
    for (const auto& name : files)
    {
        const HANDLE h = CreateFileW((dir + L"\\" + name).c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        ASSERT_NE(h, INVALID_HANDLE_VALUE);
        CloseHandle(h);
    }

    EXPECT_TRUE(AnyPathHasRecoveryData({ dir + L"\\notes.tar.gz" }, kNoPolicy, ListFolderNames));
    EXPECT_FALSE(AnyPathHasRecoveryData({ dir + L"\\other.tar.gz" }, kNoPolicy, ListFolderNames));
    EXPECT_FALSE(AnyPathHasRecoveryData({ dir + L"\\missing\\notes.tar.gz" }, kNoPolicy, ListFolderNames));
    EXPECT_TRUE(ListFolderNames(dir + L"\\missing\\*").empty());

    for (const auto& name : files)
        DeleteFileW((dir + L"\\" + name).c_str());
    RemoveDirectoryW(dir.c_str());
}

// ---------------------------------------------------------------------------
// Command builders (T-F235: the switch part only - the paths go on stdin)
// ---------------------------------------------------------------------------

TEST(CommandBuilders, EachEmitsTheSwitchShellArgumentParserExpects)
{
    EXPECT_EQ(BuildExtractHereArgs(), L"--extract-here");
    EXPECT_EQ(BuildExtractHereFlatArgs(), L"--extract-flat");
    EXPECT_EQ(BuildExtractFolderArgs(), L"--extract-folder");
    EXPECT_EQ(BuildTestArgs(), L"--test");
    EXPECT_EQ(BuildRecoveryVerifyArgs(), L"--recovery-verify");
    EXPECT_EQ(BuildScanArgs(), L"--scan");
    EXPECT_EQ(BuildOpenUiExtractArgs(), L"--open-ui --extract");
    EXPECT_EQ(BuildOpenUiArchiveArgs(), L"--open-ui --archive");
    EXPECT_EQ(BuildOpenUiBrowseArgs(), L"--open-ui --browse");
}

// T-F105: default format ("zip", or the arg omitted entirely) stays flag-less on the command line.
TEST(BuildArchiveArgs, DefaultFormatOmitsFormatFlag)
{
    EXPECT_EQ(BuildArchiveArgs(), L"--archive");
    EXPECT_EQ(BuildArchiveArgs(L"zip"), L"--archive");
}

TEST(BuildArchiveArgs, TarFormatEmitsFormatFlag)
{
    EXPECT_EQ(BuildArchiveArgs(L"tar"), L"--archive --format tar");
}

// T-F128
TEST(BuildHashArgs, AlgorithmIsAlwaysExplicit)
{
    EXPECT_EQ(BuildHashArgs(L"crc32"), L"--hash --algorithm crc32");
    EXPECT_EQ(BuildHashArgs(L"sha256"), L"--hash --algorithm sha256");
}

// ---------------------------------------------------------------------------
// Selection transport (T-F235)
// ---------------------------------------------------------------------------

namespace
{
    // The reading side's rule (Archiver.Shell/StdinPathList.cs): entries up to the final end marker.
    std::vector<std::wstring> SplitPayload(const std::wstring& payload)
    {
        std::vector<std::wstring> paths;
        if (payload.size() < 2 || payload[payload.size() - 1] != L'\0' || payload[payload.size() - 2] != L'\0')
            return paths;
        size_t start = 0;
        const size_t bodyEnd = payload.size() - 1;
        while (start < bodyEnd)
        {
            const size_t nul = payload.find(L'\0', start);
            paths.push_back(payload.substr(start, nul - start));
            start = nul + 1;
        }
        return paths;
    }

    std::vector<std::wstring> ManyLongPaths(size_t count)
    {
        std::vector<std::wstring> paths;
        paths.reserve(count);
        for (size_t i = 0; i < count; ++i)
            paths.push_back(L"C:\\scratch\\many\\" + std::to_wstring(i) + L"_" + std::wstring(90, L'x') + L".txt");
        return paths;
    }

    std::wstring ReadAll(HANDLE readEnd)
    {
        std::string bytes;
        std::vector<char> buffer(65536);
        DWORD read = 0;
        while (ReadFile(readEnd, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr) && read > 0)
            bytes.append(buffer.data(), read);
        return std::wstring(reinterpret_cast<const wchar_t*>(bytes.data()), bytes.size() / sizeof(wchar_t));
    }
}

TEST(BuildShellCommandLine, QuotesTheExeAndEndsWithTheStdinFlag)
{
    EXPECT_EQ(BuildShellCommandLine(L"C:\\Program Files\\Pakko\\Archiver.Shell.exe", BuildArchiveArgs(L"tar")),
        L"\"C:\\Program Files\\Pakko\\Archiver.Shell.exe\" --archive --format tar --paths-stdin");
}

TEST(BuildPathListPayload, FormatIsNulTerminatedEntriesPlusEndMarker)
{
    // Same bytes Archiver.Shell.Tests' StdinPathListTests.Read_SameBytesAsTheNativeBuilder_ParsesIdentically reads.
    const std::wstring expected(L"C:\\a\0Z:\\\0\0", 10);
    EXPECT_EQ(BuildPathListPayload({ L"C:\\a", L"Z:\\" }), expected);
}

TEST(BuildPathListPayload, KeepsSpacesTrailingBackslashAndUnicodeVerbatim)
{
    const std::wstring cyrillic = std::wstring(L"C:\\") + static_cast<wchar_t>(0x0414) + static_cast<wchar_t>(0x0430) + L".zip";
    const std::vector<std::wstring> paths = { L"C:\\My Files\\a b.zip", L"Z:\\", cyrillic };
    EXPECT_EQ(SplitPayload(BuildPathListPayload(paths)), paths);
}

// The T-F235 repro: 300 files with ~95-character names made a ~71,400-character command line,
// past CreateProcess's 32,767 limit, and every command silently did nothing.
TEST(PathListTransport, ALargeSelectionGoesToThePayloadNotTheCommandLine)
{
    const auto paths = ManyLongPaths(300);
    const std::wstring commandLine = BuildShellCommandLine(L"C:\\Program Files\\Pakko\\Archiver.Shell.exe", BuildArchiveArgs());
    const std::wstring payload = BuildPathListPayload(paths);

    EXPECT_LT(commandLine.size(), 32767u);
    EXPECT_EQ(commandLine.find(L"many"), std::wstring::npos);
    EXPECT_GT(payload.size(), 32767u);
    EXPECT_EQ(SplitPayload(payload), paths);
}

TEST(PathListFitsLimit, BoundaryAndOverflow)
{
    EXPECT_TRUE(PathListFitsLimit(kMaxPathListBytes / sizeof(wchar_t)));
    EXPECT_FALSE(PathListFitsLimit(kMaxPathListBytes / sizeof(wchar_t) + 1));
    EXPECT_FALSE(PathListFitsLimit(SIZE_MAX));
}

TEST(PathListTransport, OnlyTheReadEndIsInheritable)
{
    UniqueHandle readEnd, writeEnd;
    ASSERT_HRESULT_SUCCEEDED(CreatePathListPipe(64, readEnd, writeEnd));

    DWORD readFlags = 0, writeFlags = 0;
    ASSERT_TRUE(GetHandleInformation(readEnd.get(), &readFlags));
    ASSERT_TRUE(GetHandleInformation(writeEnd.get(), &writeFlags));
    EXPECT_NE(readFlags & HANDLE_FLAG_INHERIT, 0u);
    EXPECT_EQ(writeFlags & HANDLE_FLAG_INHERIT, 0u);
}

TEST(PathListTransport, PipeCarriesTenThousandPaths)
{
    const auto paths = ManyLongPaths(10000);
    const std::wstring payload = BuildPathListPayload(paths);
    UniqueHandle readEnd, writeEnd;
    ASSERT_HRESULT_SUCCEEDED(CreatePathListPipe(payload.size() * sizeof(wchar_t), readEnd, writeEnd));

    // Nobody reads yet: the buffer is sized to the payload, so this must complete, not block.
    ASSERT_HRESULT_SUCCEEDED(WritePathList(writeEnd.get(), payload));
    writeEnd.reset();

    EXPECT_EQ(SplitPayload(ReadAll(readEnd.get())), paths);
}

TEST(PathListTransport, WriteFailsWhenNoReaderIsLeft)
{
    UniqueHandle readEnd, writeEnd;
    ASSERT_HRESULT_SUCCEEDED(CreatePathListPipe(64, readEnd, writeEnd));
    readEnd.reset();

    EXPECT_HRESULT_FAILED(WritePathList(writeEnd.get(), BuildPathListPayload({ L"C:\\a.zip" })));
}

// A real child process (powershell.exe copying its stdin to a file) receives exactly the payload
// through the inherited handle - the same launch path LaunchShellExe uses for Archiver.Shell.exe.
TEST(LaunchWithPathList, ChildReceivesTheExactPayloadOnStdin)
{
    wchar_t tempDir[MAX_PATH] = {};
    ASSERT_NE(GetTempPathW(MAX_PATH, tempDir), 0u);
    const std::wstring outFile = std::wstring(tempDir) + L"pakko_tf235_" + std::to_wstring(GetCurrentProcessId()) + L".bin";
    DeleteFileW(outFile.c_str());

    const std::wstring exe = L"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe";
    const std::wstring commandLine = L"\"" + exe + L"\" -NoProfile -NonInteractive -Command \"$i=[Console]::OpenStandardInput(); $o=[IO.File]::Create('"
        + outFile + L"'); $i.CopyTo($o); $o.Close()\"";
    const std::wstring payload = BuildPathListPayload(ManyLongPaths(2000));

    UniqueHandle process;
    ASSERT_HRESULT_SUCCEEDED(LaunchWithPathList(exe, commandLine, payload, &process));
    ASSERT_EQ(WaitForSingleObject(process.get(), 60000), WAIT_OBJECT_0);
    DWORD exitCode = 1;
    ASSERT_TRUE(GetExitCodeProcess(process.get(), &exitCode));
    EXPECT_EQ(exitCode, 0u);

    UniqueHandle file(CreateFileW(outFile.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr));
    ASSERT_NE(file.get(), INVALID_HANDLE_VALUE);
    const std::wstring received = ReadAll(file.get());
    file.reset();
    DeleteFileW(outFile.c_str());
    EXPECT_EQ(received, payload);
}

TEST(LaunchWithPathList, MissingExeFails)
{
    EXPECT_HRESULT_FAILED(LaunchWithPathList(L"C:\\no\\such\\Archiver.Shell.exe",
        L"\"C:\\no\\such\\Archiver.Shell.exe\" --test --paths-stdin", BuildPathListPayload({ L"C:\\a.zip" })));
}

TEST(LaunchWithPathList, EmptyPayloadIsRejected)
{
    EXPECT_EQ(LaunchWithPathList(L"C:\\Windows\\System32\\cmd.exe", L"cmd.exe /c exit", L""), E_INVALIDARG);
}

TEST(FormatHResult, IsEightHexDigits)
{
    EXPECT_EQ(FormatHResult(E_OUTOFMEMORY), L"0x8007000E");
    EXPECT_EQ(FormatHResult(S_OK), L"0x00000000");
}

// ---------------------------------------------------------------------------
// GetSelectionPaths (T-F235) - needs COM, initialized by TestMain's ComEnvironment
// ---------------------------------------------------------------------------

namespace
{
    Microsoft::WRL::ComPtr<IShellItemArray> MakeArray(const std::vector<std::wstring>& parsingNames)
    {
        std::vector<PIDLIST_ABSOLUTE> pidls;
        for (const auto& name : parsingNames)
        {
            PIDLIST_ABSOLUTE pidl = nullptr;
            if (SUCCEEDED(SHParseDisplayName(name.c_str(), nullptr, &pidl, 0, nullptr)))
                pidls.push_back(pidl);
        }
        Microsoft::WRL::ComPtr<IShellItemArray> array;
        if (pidls.size() == parsingNames.size())
            (void)SHCreateShellItemArrayFromIDLists(static_cast<UINT>(pidls.size()),
                const_cast<PCIDLIST_ABSOLUTE_ARRAY>(pidls.data()), &array);
        for (PIDLIST_ABSOLUTE pidl : pidls)
            CoTaskMemFree(pidl);
        return array;
    }

    // Control Panel: a real shell item with no filesystem path.
    constexpr wchar_t kControlPanel[] = L"::{26EE0668-A00A-44D7-9371-BEB064C98683}";
}

TEST(GetSelectionPaths, FileSystemItemsAreCompleteAndInOrder)
{
    const auto array = MakeArray({ L"C:\\Windows", L"C:\\Windows\\System32" });
    ASSERT_NE(array.Get(), nullptr);

    const SelectionPaths selection = GetSelectionPaths(array.Get());

    EXPECT_TRUE(selection.complete);
    EXPECT_EQ(selection.paths, (std::vector<std::wstring>{ L"C:\\Windows", L"C:\\Windows\\System32" }));
}

TEST(GetSelectionPaths, AnItemWithoutAFileSystemPathMarksTheSelectionIncomplete)
{
    const auto array = MakeArray({ L"C:\\Windows", kControlPanel });
    ASSERT_NE(array.Get(), nullptr);

    const SelectionPaths selection = GetSelectionPaths(array.Get());

    EXPECT_FALSE(selection.complete);
    EXPECT_EQ(selection.paths, (std::vector<std::wstring>{ L"C:\\Windows" }));
}

TEST(GetSelectionPaths, NullArrayIsIncomplete)
{
    const SelectionPaths selection = GetSelectionPaths(nullptr);

    EXPECT_FALSE(selection.complete);
    EXPECT_TRUE(selection.paths.empty());
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

TEST(BuildAddToArchiveTitle, MultipleFilesAtDriveRootUseTheDriveLetter)
{
    const auto title = BuildAddToArchiveTitle({ L"C:\\first.txt", L"C:\\second.txt" });
    EXPECT_EQ(title, L"Add to \"C.zip\"");
}

// T-F99/T-F281: a single drive-root selection (e.g. "Z:\") is a distinct case from the
// multi-file-at-drive-root case above — PathFindFileNameW returns the whole "Z:\" string
// unchanged (not an empty tail). Both are named after the drive letter (user decision 2026-09-30).
TEST(BuildAddToArchiveTitle, SingleDriveRootUsesTheDriveLetter)
{
    EXPECT_EQ(BuildAddToArchiveTitle({ L"Z:\\" }), L"Add to \"Z.zip\"");
    EXPECT_EQ(BuildAddToArchiveTitle({ L"d:\\" }), L"Add to \"D.zip\"");
}

// T-F264: a UNC share root; ArchiveNamingTests.GetDefaultArchiveName_MatchesTheExplorerMenuTitle
// checks the C# side names the created archive the same way.
TEST(BuildAddToArchiveTitle, FilesAtAUncShareRootUseTheShareName)
{
    EXPECT_EQ(BuildAddToArchiveTitle({ L"\\\\server\\share\\a.txt", L"\\\\server\\share\\b.txt" }), L"Add to \"share.zip\"");
    EXPECT_EQ(BuildAddToArchiveTitle({ L"\\\\server\\share" }), L"Add to \"share.zip\"");
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

TEST(BuildAddToArchiveTitle, TarExtensionMultipleFilesAtDriveRootUseTheDriveLetter)
{
    const auto title = BuildAddToArchiveTitle({ L"C:\\first.txt", L"C:\\second.txt" }, L".tar");
    EXPECT_EQ(title, L"Add to \"C.tar\"");
}

TEST(BuildAddToArchiveTitle, TarExtensionSingleDriveRootUsesTheDriveLetter)
{
    const auto title = BuildAddToArchiveTitle({ L"Z:\\" }, L".tar");
    EXPECT_EQ(title, L"Add to \"Z.tar\"");
}

TEST(BuildAddToArchiveTitle, TarExtensionNameOverLimitIsTruncatedInTheMiddle)
{
    const auto title = BuildAddToArchiveTitle(
        { L"C:\\My Very Long Project Folder Name With Lots Of Words 2026 Final Report.txt" }, L".tar");
    EXPECT_EQ(title, L"Add to \"My Very Long Project F\u202626 Final Report.tar\"");
}

// T-F115: explicit non-default localeTag flows through to the surrounding phrase.
TEST(BuildAddToArchiveTitle, UkrainianLocaleTranslatesSurroundingPhrase)
{
    EXPECT_EQ(BuildAddToArchiveTitle({ L"C:\\Docs\\report.docx" }, L".zip", L"uk-UA"), L"\u0414\u043e\u0434\u0430\u0442\u0438 \u0434\u043e \"report.zip\"");
}

TEST(BuildAddToArchiveTitle, UkrainianLocaleEmptyVectorFallback)
{
    EXPECT_EQ(BuildAddToArchiveTitle({}, L".zip", L"uk-UA"), L"\u0414\u043e\u0434\u0430\u0442\u0438 \u0434\u043e \u0430\u0440\u0445\u0456\u0432\u0443\u2026");
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

// T-F115: explicit non-default localeTag flows through to the surrounding phrase.
TEST(BuildExtractFolderTitle, UkrainianLocaleTranslatesSurroundingPhrase)
{
    EXPECT_EQ(BuildExtractFolderTitle({ L"C:\\Docs\\report.zip" }, L"uk-UA"), L"\u0412\u0438\u0434\u043e\u0431\u0443\u0442\u0438 \u0434\u043e \"report\\\"");
}

TEST(BuildExtractFolderTitle, UkrainianLocaleMultipleArchivesFallback)
{
    EXPECT_EQ(BuildExtractFolderTitle({ L"C:\\a.zip", L"C:\\b.zip" }, L"uk-UA"), L"\u0412\u0438\u0434\u043e\u0431\u0443\u0442\u0438 \u043a\u043e\u0436\u0435\u043d \u0443 \u0441\u0432\u043e\u044e \u043f\u0430\u043f\u043a\u0443");
}
