#include "pch.h"
#include "ShellExtUtils.h"

using Microsoft::WRL::ComPtr;

// Defined in dllmain.cpp; set during DLL_PROCESS_ATTACH, never changed afterwards.
extern HMODULE g_hModule;

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

static std::wstring QuotePath(const std::wstring& path)
{
    return L'"' + path + L'"';
}

static bool HasZipExtension(const std::wstring& path)
{
    const wchar_t* pExt = PathFindExtensionW(path.c_str());
    return pExt != nullptr && _wcsicmp(pExt, L".zip") == 0;
}

// Mirrors .NET's Path.GetFileNameWithoutExtension: a leading dot (e.g. ".gitignore")
// is not treated as an extension separator.
static std::wstring GetFileNameWithoutExtension(const std::wstring& path)
{
    const std::wstring fileName(PathFindFileNameW(path.c_str()));
    const auto pos = fileName.rfind(L'.');
    return (pos != std::wstring::npos && pos != 0) ? fileName.substr(0, pos) : fileName;
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

std::wstring BuildArchiveArgs(const std::vector<std::wstring>& paths)
{
    std::wstring args = L"--archive";
    for (const auto& p : paths)
    {
        args += L' ';
        args += QuotePath(p);
    }
    return args;
}

std::wstring BuildAddToArchiveTitle(const std::vector<std::wstring>& paths)
{
    if (paths.empty()) return L"Add to archive\u2026";
    const std::wstring name = TruncateMiddle(GetFileNameWithoutExtension(paths.front()));
    return L"Add to \"" + name + L".zip\"";
}
