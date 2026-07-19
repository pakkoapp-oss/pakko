#pragma once
#include "pch.h"

// ---------------------------------------------------------------------------
// Native localization for Archiver.ShellExtension's IExplorerCommand menu titles (T-F115).
//
// Mirrors Archiver.App/Strings/<locale>/'s 37 BCP-47 locale tags (T-F91) and single-level
// en-US fallback semantic, but as a plain compiled-in lookup table - no .rc/resource-compiler
// infrastructure, no MSIX resource-package involvement. See DECISIONS.md's T-F115 entry for why
// NanaZip's own LangString()/legacy-.rc approaches were rejected in favor of this.
// ---------------------------------------------------------------------------

enum class StringId
{
    ExtractDialog,
    ExtractHereFlat,
    ExtractHereIntelligent,
    ExtractFolderFallback,
    ExtractFolderMultiFallback,
    ExtractFolderNamedTemplate,
    CompressDialog,
    ArchiveFallback,
    ArchiveNamedTemplate,
    TestArchive,
    // T-F03: "Open" verb — launches straight into the Archive Browser (T-F05) instead of the
    // pending-list/extract-options view. No ellipsis: unlike ExtractDialog/CompressDialog, this
    // never shows a further dialog, matching ExtractHereFlat/TestArchive's plain-verb convention.
    BrowseArchive,
    // T-F128: the "Хеш-суми" submenu's own parent title (ECF_HASSUBCOMMANDS). The two leaf items
    // under it ("CRC-32"/"SHA-256") are hardcoded literals, not localized — algorithm names stay
    // untranslated Latin script everywhere, matching T-F105's tar format names.
    HashSubmenu,
};

// Resolves the calling thread's preferred UI language as a BCP-47 tag (e.g. L"uk-UA"), matching
// the same tag convention as Archiver.App/Strings/<locale>/. Falls back to L"en-US" if
// GetThreadPreferredUILanguages fails or returns no languages.
std::wstring GetCurrentUILanguageTag();

// Pure, testable lookup: returns the localized text for `id` under `localeTag`. Falls back to
// the en-US entry for `id` if `localeTag` isn't one of the supported locales - the same
// single-level fallback semantic Archiver.App's resw resources already use for a missing key.
std::wstring GetLocalizedString(StringId id, const std::wstring& localeTag);

// Production convenience overload - resolves the real caller UI language via
// GetCurrentUILanguageTag() and delegates to the explicit-locale overload above.
std::wstring GetLocalizedString(StringId id);

// Substitutes the first literal "{0}" occurrence in `tmpl` with `value`. Used for the two
// templated titles (ExtractFolderNamedTemplate, ArchiveNamedTemplate) so the quoted archive/
// folder name - a user-controlled filename - is never reinterpreted as a format specifier.
// `tmpl` must contain exactly one "{0}"; see Archiver.ShellExtension.Tests' data-integrity test
// that verifies every locale's templates satisfy this.
std::wstring ApplyTemplate(const std::wstring& tmpl, const std::wstring& value);
