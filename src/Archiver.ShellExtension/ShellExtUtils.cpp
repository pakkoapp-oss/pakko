#include "pch.h"
#include "ShellExtUtils.h"
#include "Localization.h"

using Microsoft::WRL::ComPtr;

// Defined in dllmain.cpp; set during DLL_PROCESS_ATTACH, never changed afterwards.
extern HMODULE g_hModule;

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

static std::wstring QuotePath(const std::wstring& path)
{
    // T-F99: a trailing backslash immediately before the closing quote escapes the quote itself
    // under Win32/CRT command-line parsing (CommandLineToArgvW) instead of closing the quoted
    // argument, corrupting every argument after it. Only a bare drive root (e.g. "Z:\") ends in
    // a backslash - a real file/folder path from Explorer never does - but T-F99 makes drive
    // roots a reachable selection, so this must be handled. Doubling the trailing backslash
    // makes the parser see a literal backslash followed by a real closing quote.
    std::wstring escaped = path;
    if (!escaped.empty() && escaped.back() == L'\\')
        escaped += L'\\';
    return L'"' + escaped + L'"';
}

static bool HasZipExtension(const std::wstring& path)
{
    const wchar_t* pExt = PathFindExtensionW(path.c_str());
    return pExt != nullptr && _wcsicmp(pExt, L".zip") == 0;
}

// T-F86: non-ZIP formats Archiver.Core routes to ITarService - kept in sync with
// Archiver.App/ViewModels/MainViewModel.cs's _extractableTypes (minus "ZIP", handled separately
// above). See DECISIONS.md's T-F86 entry for why extension-only, not magic-byte, at gating time.
static const wchar_t* const kSupportedNonZipArchiveExtensions[] = {
    L".rar", L".7z", L".tar", L".gz", L".tgz", L".bz2", L".tbz2",
    L".xz", L".txz", L".zst", L".tzst", L".lzma"
};

// T-F103: kept in sync with Archiver.Core/Services/ArchiveNaming.cs's compound extension list.
// Path.GetFileNameWithoutExtension-style single-dot stripping leaves ".tar" on the end of
// "archive.tar.gz" — these must be stripped as a unit before falling back to the single-dot rule.
static const wchar_t* const kCompoundArchiveExtensions[] = {
    L".tar.gz", L".tar.bz2", L".tar.xz", L".tar.zst", L".tar.lzma"
};

static bool EndsWithCaseInsensitive(const std::wstring& value, const wchar_t* suffix)
{
    const size_t suffixLen = wcslen(suffix);
    if (value.size() < suffixLen) return false;
    return _wcsicmp(value.c_str() + (value.size() - suffixLen), suffix) == 0;
}

// Deliberately deviates from .NET's Path.GetFileNameWithoutExtension for dotfiles: real .NET
// strips everything from the only dot in ".gitignore", leaving "". Keeping the full name instead
// avoids an empty display name; Archiver.Shell/Program.cs's RunArchiveAsync applies the same
// "don't strip to empty" fallback so the title shown here matches the archive actually created.
static std::wstring GetFileNameWithoutExtension(const std::wstring& path)
{
    const std::wstring fileName(PathFindFileNameW(path.c_str()));

    for (const wchar_t* ext : kCompoundArchiveExtensions)
    {
        if (EndsWithCaseInsensitive(fileName, ext))
            return fileName.substr(0, fileName.size() - wcslen(ext));
    }

    const auto pos = fileName.rfind(L'.');
    return (pos != std::wstring::npos && pos != 0) ? fileName.substr(0, pos) : fileName;
}

// Returns the name of the folder containing `path` (i.e. path's parent directory's own
// basename) \u2014 mirrors Archiver.Shell/Program.cs's RunArchiveAsync, which names a multi-item
// archive after the common containing folder rather than an arbitrary selected item.
static std::wstring GetParentFolderName(const std::wstring& path)
{
    const auto lastSep = path.rfind(L'\\');
    if (lastSep == std::wstring::npos) return {};
    const std::wstring parentPath = path.substr(0, lastSep);
    const auto parentSep = parentPath.rfind(L'\\');
    return (parentSep != std::wstring::npos) ? parentPath.substr(parentSep + 1) : parentPath;
}

// A folder/file name can be up to 255 characters - left untruncated, that would make the
// "Pakko" context-menu submenu absurdly wide. Truncate in the middle (head + "..." + tail),
// the same "recognizable prefix + ellipsis + tail" shape Windows itself uses for long names
// (see PathCompactPathExW) - a plain right-truncation would hide the tail, which for many
// real names (dates, versions, "_final", "_v2") is the most distinguishing part.
constexpr size_t kMaxDisplayNameLength = 40;
constexpr size_t kDisplayNameHeadLength = 22;
constexpr size_t kDisplayNameTailLength = 15;

static std::wstring TruncateMiddle(const std::wstring& name)
{
    if (name.size() <= kMaxDisplayNameLength) return name;
    return name.substr(0, kDisplayNameHeadLength) + L"\u2026" + name.substr(name.size() - kDisplayNameTailLength);
}

// ---------------------------------------------------------------------------
// Public functions
// ---------------------------------------------------------------------------

std::wstring GetDllDirectory()
{
    wchar_t buf[MAX_PATH] = {};
    const DWORD len = GetModuleFileNameW(g_hModule, buf, MAX_PATH);
    if (len == 0 || len >= MAX_PATH) return {};

    std::wstring path(buf, len);
    const auto pos = path.rfind(L'\\');
    return (pos != std::wstring::npos) ? path.substr(0, pos) : std::wstring{};
}

std::wstring GetShellExePath()
{
    const std::wstring dir = GetDllDirectory();
    return dir.empty() ? std::wstring{} : dir + L"\\Archiver.Shell.exe";
}

std::vector<std::wstring> GetPathsFromShellItemArray(IShellItemArray* psia)
{
    std::vector<std::wstring> result;
    if (!psia) return result;

    DWORD count = 0;
    if (FAILED(psia->GetCount(&count))) return result;

    result.reserve(count);
    for (DWORD i = 0; i < count; ++i)
    {
        ComPtr<IShellItem> pItem;
        if (FAILED(psia->GetItemAt(i, &pItem))) continue;

        LPWSTR pszPath = nullptr;
        if (FAILED(pItem->GetDisplayName(SIGDN_FILESYSPATH, &pszPath))) continue;

        result.emplace_back(pszPath);
        CoTaskMemFree(pszPath);
    }
    return result;
}

bool AllPathsAreZip(const std::vector<std::wstring>& paths)
{
    if (paths.empty()) return false;
    for (const auto& p : paths)
    {
        if (!HasZipExtension(p)) return false;
    }
    return true;
}

bool AnyPathIsZip(const std::vector<std::wstring>& paths)
{
    for (const auto& p : paths)
    {
        if (HasZipExtension(p)) return true;
    }
    return false;
}

bool TarExeExists()
{
    static std::once_flag s_flag;
    static bool s_exists = false;
    std::call_once(s_flag, []()
    {
        const DWORD attrs = GetFileAttributesW(L"C:\\Windows\\System32\\tar.exe");
        s_exists = attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
    });
    return s_exists;
}

bool HasSupportedNonZipArchiveExtension(const std::wstring& path)
{
    const wchar_t* pExt = PathFindExtensionW(path.c_str());
    if (pExt == nullptr || *pExt == L'\0') return false;

    for (const wchar_t* ext : kSupportedNonZipArchiveExtensions)
    {
        if (_wcsicmp(pExt, ext) == 0) return true;
    }
    return false;
}

static bool IsSupportedArchive(const std::wstring& path)
{
    return HasZipExtension(path) || (TarExeExists() && HasSupportedNonZipArchiveExtension(path));
}

bool AllPathsAreSupportedArchive(const std::vector<std::wstring>& paths)
{
    if (paths.empty()) return false;
    for (const auto& p : paths)
    {
        if (!IsSupportedArchive(p)) return false;
    }
    return true;
}

bool AnyPathIsSupportedArchive(const std::vector<std::wstring>& paths)
{
    for (const auto& p : paths)
    {
        if (IsSupportedArchive(p)) return true;
    }
    return false;
}

HRESULT LaunchShellExe(const std::wstring& args)
{
    const std::wstring exePath = GetShellExePath();
    if (exePath.empty()) return E_FAIL;

    // CreateProcess requires a mutable command line buffer.
    std::wstring cmdLine = L'"' + exePath + L'"' + L' ' + args;

    STARTUPINFOW si = {};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi = {};

    const BOOL ok = CreateProcessW(
        exePath.c_str(),
        cmdLine.data(),
        nullptr,   // lpProcessAttributes
        nullptr,   // lpThreadAttributes
        FALSE,     // bInheritHandles
        CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
        nullptr,   // lpEnvironment (inherit parent)
        nullptr,   // lpCurrentDirectory (inherit parent)
        &si,
        &pi
    );

    if (!ok) return HRESULT_FROM_WIN32(GetLastError());

    // Close handles immediately; we do not wait for the child.
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    return S_OK;
}

std::wstring BuildExtractHereArgs(const std::vector<std::wstring>& paths)
{
    std::wstring args = L"--extract-here";
    for (const auto& p : paths)
    {
        args += L' ';
        args += QuotePath(p);
    }
    return args;
}

std::wstring BuildExtractHereFlatArgs(const std::vector<std::wstring>& paths)
{
    std::wstring args = L"--extract-flat";
    for (const auto& p : paths)
    {
        args += L' ';
        args += QuotePath(p);
    }
    return args;
}

std::wstring BuildExtractFolderArgs(const std::vector<std::wstring>& paths)
{
    std::wstring args = L"--extract-folder";
    for (const auto& p : paths)
    {
        args += L' ';
        args += QuotePath(p);
    }
    return args;
}

std::wstring BuildArchiveArgs(const std::vector<std::wstring>& paths, const std::wstring& format)
{
    std::wstring args = L"--archive";
    // T-F105: "zip" is the pre-existing default and stays flag-less on the command line, so
    // ShellArgumentParser.ParseArchive's existing zip-when-absent default keeps working
    // unchanged; only a non-zip format needs to be spelled out explicitly.
    if (format != L"zip")
    {
        args += L" --format ";
        args += format;
    }
    for (const auto& p : paths)
    {
        args += L' ';
        args += QuotePath(p);
    }
    return args;
}

std::wstring BuildTestArgs(const std::vector<std::wstring>& paths)
{
    std::wstring args = L"--test";
    for (const auto& p : paths)
    {
        args += L' ';
        args += QuotePath(p);
    }
    return args;
}

std::wstring BuildOpenUiExtractArgs(const std::vector<std::wstring>& paths)
{
    std::wstring args = L"--open-ui --extract";
    for (const auto& p : paths)
    {
        args += L' ';
        args += QuotePath(p);
    }
    return args;
}

std::wstring BuildOpenUiArchiveArgs(const std::vector<std::wstring>& paths)
{
    std::wstring args = L"--open-ui --archive";
    for (const auto& p : paths)
    {
        args += L' ';
        args += QuotePath(p);
    }
    return args;
}

std::wstring BuildOpenUiBrowseArgs(const std::vector<std::wstring>& paths)
{
    std::wstring args = L"--open-ui --browse";
    for (const auto& p : paths)
    {
        args += L' ';
        args += QuotePath(p);
    }
    return args;
}

std::wstring BuildAddToArchiveTitle(const std::vector<std::wstring>& paths, const std::wstring& ext, const std::wstring& localeTag)
{
    if (paths.empty()) return GetLocalizedString(StringId::ArchiveFallback, localeTag);

    std::wstring name = paths.size() > 1
        ? GetParentFolderName(paths.front())
        : GetFileNameWithoutExtension(paths.front());

    // Empty (no parent, e.g. a drive root) or a bare drive letter like "C:" \u2014 invalid as a
    // display name (and as the file name RunArchiveAsync would build) \u2014 fall back.
    // T-F99: PathFindFileNameW returns the whole string unchanged for a path ending in a
    // backslash (e.g. "Z:\", a real drive root's SIGDN_FILESYSPATH) rather than an empty tail,
    // so name.back() == L':' alone doesn't catch it \u2014 check for a trailing backslash too.
    if (name.empty() || name.back() == L':' || name.back() == L'\\') name = L"archive";

    const std::wstring tmpl = GetLocalizedString(StringId::ArchiveNamedTemplate, localeTag);
    return ApplyTemplate(tmpl, TruncateMiddle(name) + ext);
}

std::wstring BuildExtractFolderTitle(const std::vector<std::wstring>& paths, const std::wstring& localeTag)
{
    if (paths.empty()) return GetLocalizedString(StringId::ExtractFolderFallback, localeTag);
    if (paths.size() > 1) return GetLocalizedString(StringId::ExtractFolderMultiFallback, localeTag);

    const std::wstring name = GetFileNameWithoutExtension(paths.front());
    if (name.empty()) return GetLocalizedString(StringId::ExtractFolderFallback, localeTag);

    const std::wstring tmpl = GetLocalizedString(StringId::ExtractFolderNamedTemplate, localeTag);
    return ApplyTemplate(tmpl, TruncateMiddle(name) + L"\\");
}
