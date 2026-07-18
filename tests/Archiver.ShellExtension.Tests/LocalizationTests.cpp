// LocalizationTests.cpp (T-F115)
// Unit tests for the COM-free localization lookup in Localization.cpp.
// No COM, no DLL loading - pure lookup/fallback/template logic.

#include "pch.h"         // pulled from src/Archiver.ShellExtension via include dir
#include "Localization.h"
#include <gtest/gtest.h>

// ---------------------------------------------------------------------------
// GetLocalizedString(id, tag)
// ---------------------------------------------------------------------------

TEST(GetLocalizedString, KnownTagReturnsExactString)
{
    EXPECT_EQ(GetLocalizedString(StringId::TestArchive, L"en-US"), L"Test archive");
}

TEST(GetLocalizedString, BrowseArchiveKnownTagReturnsExactString)
{
    // T-F03: no ellipsis - a direct action, matches ExtractHereFlat/TestArchive's convention,
    // not ExtractDialog/CompressDialog's.
    EXPECT_EQ(GetLocalizedString(StringId::BrowseArchive, L"en-US"), L"Open");
    EXPECT_EQ(GetLocalizedString(StringId::BrowseArchive, L"uk-UA"), L"Відкрити");
}

TEST(GetLocalizedString, KnownNonEnglishTagReturnsExactString)
{
    // uk-UA's ExtractHereIntelligent is authored to match NanaZip's own text verbatim.
    EXPECT_EQ(GetLocalizedString(StringId::ExtractHereIntelligent, L"uk-UA"),
        L"Видобути до поточної папки (Інтелектуально)");
}

TEST(GetLocalizedString, UnknownTagFallsBackToEnUS)
{
    EXPECT_EQ(GetLocalizedString(StringId::TestArchive, L"xx-XX"), GetLocalizedString(StringId::TestArchive, L"en-US"));
    EXPECT_EQ(GetLocalizedString(StringId::ExtractHereFlat, L"xx-XX"), L"Extract here");
}

TEST(GetLocalizedString, EveryFieldIsNonEmptyForEnUS)
{
    for (auto id : { StringId::ExtractDialog, StringId::ExtractHereFlat, StringId::ExtractHereIntelligent,
                      StringId::ExtractFolderFallback, StringId::ExtractFolderMultiFallback,
                      StringId::ExtractFolderNamedTemplate, StringId::CompressDialog,
                      StringId::ArchiveFallback, StringId::ArchiveNamedTemplate, StringId::TestArchive,
                      StringId::BrowseArchive })
    {
        EXPECT_FALSE(GetLocalizedString(id, L"en-US").empty());
    }
}

// ---------------------------------------------------------------------------
// Data integrity: every locale's two templated strings must contain the literal "{0}"
// placeholder exactly once, or ApplyTemplate silently produces an un-substituted title.
// Mirrors the 37 BCP-47 tags Archiver.App/Strings/<locale>/ (T-F91) supports.
// ---------------------------------------------------------------------------

static const wchar_t* const kAllLocaleTags[] = {
    L"en-US", L"ar-SA", L"bg-BG", L"cs-CZ", L"da-DK", L"de-DE", L"el-GR", L"es-ES", L"et-EE",
    L"fi-FI", L"fr-FR", L"he-IL", L"hi-IN", L"hr-HR", L"hu-HU", L"id-ID", L"it-IT", L"ja-JP",
    L"ko-KR", L"lt-LT", L"lv-LV", L"nb-NO", L"nl-NL", L"pl-PL", L"pt-PT", L"ro-RO", L"sk-SK",
    L"sl-SI", L"sr-Latn-RS", L"sv-SE", L"sw-KE", L"th-TH", L"tr-TR", L"uk-UA", L"ur-PK",
    L"vi-VN", L"zh-Hans",
};

TEST(LocalizationDataIntegrity, EveryLocaleHasExactlyThirtySevenSupportedTags)
{
    EXPECT_EQ(std::size(kAllLocaleTags), 37u);
}

TEST(LocalizationDataIntegrity, EveryLocaleExtractFolderNamedTemplateContainsPlaceholder)
{
    for (const wchar_t* tag : kAllLocaleTags)
    {
        const auto s = GetLocalizedString(StringId::ExtractFolderNamedTemplate, tag);
        EXPECT_NE(s.find(L"{0}"), std::wstring::npos) << "locale: " << tag;
    }
}

TEST(LocalizationDataIntegrity, EveryLocaleArchiveNamedTemplateContainsPlaceholder)
{
    for (const wchar_t* tag : kAllLocaleTags)
    {
        const auto s = GetLocalizedString(StringId::ArchiveNamedTemplate, tag);
        EXPECT_NE(s.find(L"{0}"), std::wstring::npos) << "locale: " << tag;
    }
}

TEST(LocalizationDataIntegrity, EveryLocaleResolvesToItselfNotTheEnUSFallback)
{
    // A typo'd/missing map key would silently resolve to en-US instead - catch that directly by
    // confirming each non-English locale's TestArchive string differs from en-US's.
    for (const wchar_t* tag : kAllLocaleTags)
    {
        if (std::wstring(tag) == L"en-US") continue;
        EXPECT_NE(GetLocalizedString(StringId::TestArchive, tag), GetLocalizedString(StringId::TestArchive, L"en-US"))
            << "locale: " << tag;
    }
}

TEST(LocalizationDataIntegrity, EveryLocaleBrowseArchiveIsNonEmpty)
{
    // T-F03: the most direct catch for a row where the new 11th field was left unset (nullptr) -
    // GetLocalizedString would construct std::wstring from a null pointer, so a bad row here
    // crashes rather than merely mismatching.
    for (const wchar_t* tag : kAllLocaleTags)
    {
        EXPECT_FALSE(GetLocalizedString(StringId::BrowseArchive, tag).empty()) << "locale: " << tag;
    }
}

// ---------------------------------------------------------------------------
// ApplyTemplate
// ---------------------------------------------------------------------------

TEST(ApplyTemplate, SubstitutesPlaceholder)
{
    EXPECT_EQ(ApplyTemplate(L"Extract to \"{0}\"", L"report\\"), L"Extract to \"report\\\"");
}

TEST(ApplyTemplate, NoPlaceholderReturnsTemplateUnchanged)
{
    EXPECT_EQ(ApplyTemplate(L"no placeholder here", L"value"), L"no placeholder here");
}

// ---------------------------------------------------------------------------
// GetCurrentUILanguageTag
// ---------------------------------------------------------------------------

TEST(GetCurrentUILanguageTag, ReturnsNonEmptyTag)
{
    // Whatever the test-runner machine's actual UI language is, this must never come back empty -
    // GetCurrentUILanguageTag() falls back to L"en-US" on any Win32 failure.
    EXPECT_FALSE(GetCurrentUILanguageTag().empty());
}
