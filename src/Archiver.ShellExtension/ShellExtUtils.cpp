#include "pch.h"
#include "ShellExtUtils.h"
#include "Localization.h"
#include <memory>

using Microsoft::WRL::ComPtr;

// Defined in dllmain.cpp; set during DLL_PROCESS_ATTACH, never changed afterwards.
extern HMODULE g_hModule;

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

// T-F131: .jar/.war/.ear (Java) and .apk (Android) are real ZIP-format containers — deliberately
// narrower than "every possible ZIP container" (no Office/OpenDocument/.epub), per the user's
// explicit choice. See DECISIONS.md's T-F131 entry.
// T-F133: .asice/.asics (ASiC-E/ASiC-S signed containers, ETSI TS 102 918) and .bdoc (Estonia's
// national ASiC-E profile) — also real ZIP-format containers. See DECISIONS.md's T-F133 entry.
static const wchar_t* const kZipContainerExtensions[] = {
    L".zip", L".jar", L".war", L".ear", L".apk", L".asice", L".asics", L".bdoc"
};

static bool HasZipExtension(const std::wstring& path)
{
    const wchar_t* pExt = PathFindExtensionW(path.c_str());
    if (pExt == nullptr || *pExt == L'\0') return false;

    for (const wchar_t* ext : kZipContainerExtensions)
    {
        if (_wcsicmp(pExt, ext) == 0) return true;
    }
    return false;
}

// T-F86: non-ZIP formats Archiver.Core routes to ITarService - kept in sync with
// Archiver.Core/Services/ArchiveFormatDetector.cs's _recognizedExtensions (minus the ZIP-container
// group above, handled separately). See DECISIONS.md's T-F86 entry for why extension-only, not
// magic-byte, at gating time.
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
// avoids an empty display name; Archiver.Shell/ShellCommands.cs's ArchiveAsync applies the same
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
// basename) \u2014 mirrors Archiver.Shell/ShellCommands.cs's ArchiveAsync, which names a multi-item
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
    return GetSelectionPaths(psia).paths;
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

// ---------------------------------------------------------------------------
// T-F262: Group Policy for the menu.
// ---------------------------------------------------------------------------

std::optional<DWORD> Win32PolicyRegistryReader::GetDword(const wchar_t* valueName) const
{
    DWORD value = 0;
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_LOCAL_MACHINE, m_keyPath, valueName, RRF_RT_REG_DWORD, nullptr, &value, &size) != ERROR_SUCCESS)
        return std::nullopt;
    return value;
}

std::optional<std::vector<std::wstring>> Win32PolicyRegistryReader::GetMultiString(const wchar_t* valueName) const
{
    DWORD size = 0;
    if (RegGetValueW(HKEY_LOCAL_MACHINE, m_keyPath, valueName, RRF_RT_REG_MULTI_SZ, nullptr, nullptr, &size) != ERROR_SUCCESS || size == 0)
        return std::nullopt;

    // RegGetValueW terminates string data itself; one extra pair of NULs keeps the parse below
    // bounded by the buffer even if the value shrank between the two calls.
    std::vector<wchar_t> buffer(size / sizeof(wchar_t) + 2, L'\0');
    if (RegGetValueW(HKEY_LOCAL_MACHINE, m_keyPath, valueName, RRF_RT_REG_MULTI_SZ, nullptr, buffer.data(), &size) != ERROR_SUCCESS)
        return std::nullopt;

    std::vector<std::wstring> entries;
    size_t start = 0;
    while (start < buffer.size() && buffer[start] != L'\0')
    {
        const std::wstring entry(buffer.data() + start);
        start += entry.size() + 1;
        entries.push_back(entry);
    }
    return entries;
}

bool MenuPolicy::IsFormatBlocked(const std::wstring& registryName) const
{
    if (registryName.empty()) return false;
    for (const auto& blocked : blockedFormats)
    {
        if (_wcsicmp(blocked.c_str(), registryName.c_str()) == 0) return true;
    }
    return false;
}

MenuPolicy LoadMenuPolicy(const PolicyRegistryReader& reader)
{
    // Same rules as Archiver.Core's GroupPolicyService.Load: only DisableTarExtraction == 1
    // counts, and a missing or empty BlockedFormats restricts nothing.
    MenuPolicy policy;
    policy.disableTar = reader.GetDword(L"DisableTarExtraction") == 1u;
    if (auto blocked = reader.GetMultiString(L"BlockedFormats"))
        policy.blockedFormats = std::move(*blocked);
    return policy;
}

MenuPolicy GetMenuPolicy()
{
    constexpr ULONGLONG kRefreshMs = 5000;
    static std::mutex s_mutex;
    static MenuPolicy s_policy;
    static ULONGLONG s_readAt = 0;
    static bool s_loaded = false;

    const std::lock_guard<std::mutex> lock(s_mutex);
    const ULONGLONG now = GetTickCount64();
    if (!s_loaded || now - s_readAt >= kRefreshMs)
    {
        s_policy = LoadMenuPolicy(Win32PolicyRegistryReader());
        s_readAt = now;
        s_loaded = true;
    }
    return s_policy;
}

std::wstring GetFormatRegistryName(const std::wstring& path)
{
    if (HasZipExtension(path)) return L"zip";

    struct ExtensionName { const wchar_t* extension; const wchar_t* name; };
    static const ExtensionName kNames[] = {
        { L".tar", L"tar" }, { L".gz", L"gzip" }, { L".tgz", L"gzip" }, { L".bz2", L"bz2" },
        { L".tbz2", L"bz2" }, { L".xz", L"xz" }, { L".txz", L"xz" }, { L".zst", L"zstd" },
        { L".tzst", L"zstd" }, { L".lzma", L"lzma" }, { L".rar", L"rar" }, { L".7z", L"sevenzip" },
    };
    const wchar_t* pExt = PathFindExtensionW(path.c_str());
    if (pExt == nullptr || *pExt == L'\0') return {};
    for (const auto& entry : kNames)
    {
        if (_wcsicmp(pExt, entry.extension) == 0) return entry.name;
    }
    return {};
}

bool IsCreationFormatAllowed(const std::wstring& format, const MenuPolicy& policy)
{
    if (policy.IsFormatBlocked(format)) return false;
    // Creating a tar also runs tar.exe, which DisableTarExtraction turns off entirely.
    return !(format == L"tar" && policy.disableTar);
}

bool AnyPathIsZip(const std::vector<std::wstring>& paths, const MenuPolicy& policy)
{
    if (policy.IsFormatBlocked(L"zip")) return false;
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

static bool IsSupportedArchive(const std::wstring& path, const MenuPolicy& policy)
{
    if (policy.IsFormatBlocked(GetFormatRegistryName(path))) return false;
    if (HasZipExtension(path)) return true;
    return !policy.disableTar && TarExeExists() && HasSupportedNonZipArchiveExtension(path);
}

bool AllPathsAreSupportedArchive(const std::vector<std::wstring>& paths, const MenuPolicy& policy)
{
    if (paths.empty()) return false;
    for (const auto& p : paths)
    {
        if (!IsSupportedArchive(p, policy)) return false;
    }
    return true;
}

bool AnyPathIsSupportedArchive(const std::vector<std::wstring>& paths, const MenuPolicy& policy)
{
    for (const auto& p : paths)
    {
        if (IsSupportedArchive(p, policy)) return true;
    }
    return false;
}

// ---------------------------------------------------------------------------
// T-F235: selection transport - the paths go to Archiver.Shell.exe on its stdin.
// ---------------------------------------------------------------------------

const wchar_t kPathsStdinFlag[] = L"--paths-stdin";

// Pipe buffer cap. A payload up to this size is written without waiting for Archiver.Shell to
// start; a larger one waits for it to read (it reads the list before anything else).
constexpr size_t kMaxPipeBufferBytes = 16u * 1024u * 1024u;

namespace
{
    HRESULT LastErrorHResult()
    {
        const DWORD error = GetLastError();
        return error == ERROR_SUCCESS ? E_FAIL : HRESULT_FROM_WIN32(error);
    }

    // A one-entry PROC_THREAD_ATTRIBUTE_HANDLE_LIST, so the child inherits the pipe's read end and
    // nothing else Explorer happens to hold as inheritable.
    class InheritedHandleList
    {
    public:
        InheritedHandleList() = default;
        InheritedHandleList(const InheritedHandleList&) = delete;
        InheritedHandleList& operator=(const InheritedHandleList&) = delete;
        ~InheritedHandleList()
        {
            if (m_initialized)
                DeleteProcThreadAttributeList(get());
        }

        // `handle` must stay alive until CreateProcessW returns - the list stores its address.
        HRESULT Initialize(HANDLE* handle)
        {
            SIZE_T size = 0;
            // Size query: fails with ERROR_INSUFFICIENT_BUFFER by design and reports the size.
            (void)InitializeProcThreadAttributeList(nullptr, 1, 0, &size);
            if (size == 0) return LastErrorHResult();
            m_buffer.resize(size);
            if (!InitializeProcThreadAttributeList(get(), 1, 0, &size)) return LastErrorHResult();
            m_initialized = true;
            if (!UpdateProcThreadAttribute(get(), 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, handle, sizeof(HANDLE), nullptr, nullptr))
                return LastErrorHResult();
            return S_OK;
        }

        LPPROC_THREAD_ATTRIBUTE_LIST get() noexcept
        {
            return reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(m_buffer.data());
        }

    private:
        std::vector<unsigned char> m_buffer;
        bool m_initialized = false;
    };
}

SelectionPaths GetSelectionPaths(IShellItemArray* psia)
{
    SelectionPaths result;
    DWORD count = 0;
    if (!psia || FAILED(psia->GetCount(&count)))
    {
        result.complete = false;
        return result;
    }

    result.paths.reserve(count);
    for (DWORD i = 0; i < count; ++i)
    {
        ComPtr<IShellItem> pItem;
        LPWSTR pszPath = nullptr;
        if (FAILED(psia->GetItemAt(i, &pItem)) || FAILED(pItem->GetDisplayName(SIGDN_FILESYSPATH, &pszPath)))
        {
            result.complete = false;
            continue;
        }
        const std::unique_ptr<wchar_t, decltype(&CoTaskMemFree)> owned(pszPath, &CoTaskMemFree);
        result.paths.emplace_back(owned.get());
    }
    return result;
}

std::wstring BuildShellCommandLine(const std::wstring& exePath, const std::wstring& commandArgs)
{
    return L'"' + exePath + L"\" " + commandArgs + L' ' + kPathsStdinFlag;
}

std::wstring BuildPathListPayload(const std::vector<std::wstring>& paths)
{
    std::wstring payload;
    for (const auto& p : paths)
    {
        payload += p;
        payload += L'\0';
    }
    payload += L'\0';
    return payload;
}

bool PathListFitsLimit(size_t payloadChars)
{
    return payloadChars <= kMaxPathListBytes / sizeof(wchar_t);
}

HRESULT CreatePathListPipe(size_t payloadBytes, UniqueHandle& readEnd, UniqueHandle& writeEnd)
{
    HANDLE read = nullptr;
    HANDLE write = nullptr;
    const DWORD bufferBytes = static_cast<DWORD>(std::min(payloadBytes, kMaxPipeBufferBytes));
    // No SECURITY_ATTRIBUTES: both ends start non-inheritable, then only the read end is marked.
    if (!CreatePipe(&read, &write, nullptr, bufferBytes)) return LastErrorHResult();
    readEnd = UniqueHandle(read);
    writeEnd = UniqueHandle(write);
    if (!SetHandleInformation(readEnd.get(), HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT)) return LastErrorHResult();
    return S_OK;
}

HRESULT WritePathList(HANDLE writeEnd, const std::wstring& payload)
{
    const char* data = reinterpret_cast<const char*>(payload.data());
    size_t remaining = payload.size() * sizeof(wchar_t);
    while (remaining > 0)
    {
        const DWORD chunk = static_cast<DWORD>(std::min<size_t>(remaining, 1u << 20));
        DWORD written = 0;
        if (!WriteFile(writeEnd, data, chunk, &written, nullptr)) return LastErrorHResult();
        if (written == 0 || written > chunk) return E_FAIL;
        data += written;
        remaining -= written;
    }
    return S_OK;
}

HRESULT LaunchWithPathList(const std::wstring& exePath, const std::wstring& commandLine,
    const std::wstring& payload, UniqueHandle* process)
{
    if (payload.empty()) return E_INVALIDARG;
    if (!PathListFitsLimit(payload.size())) return HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW);

    UniqueHandle readEnd;
    UniqueHandle writeEnd;
    HRESULT hr = CreatePathListPipe(payload.size() * sizeof(wchar_t), readEnd, writeEnd);
    if (FAILED(hr)) return hr;

    HANDLE inherited = readEnd.get();
    InheritedHandleList handleList;
    hr = handleList.Initialize(&inherited);
    if (FAILED(hr)) return hr;

    STARTUPINFOEXW si = {};
    si.StartupInfo.cb = sizeof(si);
    si.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
    si.StartupInfo.hStdInput = inherited;
    si.lpAttributeList = handleList.get();

    // CreateProcessW may write to its command-line buffer.
    std::wstring mutableCommandLine = commandLine;
    PROCESS_INFORMATION pi = {};
    const BOOL ok = CreateProcessW(
        exePath.c_str(),
        mutableCommandLine.data(),
        nullptr,   // lpProcessAttributes
        nullptr,   // lpThreadAttributes
        TRUE,      // bInheritHandles - limited to the handle list above
        CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT,
        nullptr,   // lpEnvironment (inherit parent)
        nullptr,   // lpCurrentDirectory (inherit parent)
        &si.StartupInfo,
        &pi);
    if (!ok) return LastErrorHResult();
    UniqueHandle childProcess(pi.hProcess);
    const UniqueHandle childThread(pi.hThread);

    // Closed before writing: if the child did not get the read end, no reader is left and the
    // write fails - reported to the user - instead of the command silently doing nothing.
    readEnd.reset();
    hr = WritePathList(writeEnd.get(), payload);
    writeEnd.reset();

    if (process != nullptr)
        *process = std::move(childProcess);
    return hr;
}

std::wstring FormatHResult(HRESULT hr)
{
    wchar_t buffer[11] = {};
    swprintf_s(buffer, L"0x%08X", static_cast<unsigned int>(hr));
    return buffer;
}

HRESULT LaunchShellExe(const std::wstring& commandArgs, const std::vector<std::wstring>& paths)
{
    if (paths.empty()) return E_INVALIDARG;
    const std::wstring exePath = GetShellExePath();
    if (exePath.empty()) return E_FAIL;
    return LaunchWithPathList(exePath, BuildShellCommandLine(exePath, commandArgs), BuildPathListPayload(paths));
}

std::wstring BuildExtractHereArgs() { return L"--extract-here"; }
std::wstring BuildExtractHereFlatArgs() { return L"--extract-flat"; }
std::wstring BuildExtractFolderArgs() { return L"--extract-folder"; }

std::wstring BuildArchiveArgs(const std::wstring& format)
{
    // T-F105: "zip" is the pre-existing default and stays flag-less on the command line, so
    // ShellArgumentParser.ParseArchive's existing zip-when-absent default keeps working
    // unchanged; only a non-zip format needs to be spelled out explicitly.
    return format == L"zip" ? L"--archive" : L"--archive --format " + format;
}

std::wstring BuildTestArgs() { return L"--test"; }
std::wstring BuildScanArgs() { return L"--scan"; }
std::wstring BuildHashArgs(const std::wstring& algorithm) { return L"--hash --algorithm " + algorithm; }
std::wstring BuildOpenUiExtractArgs() { return L"--open-ui --extract"; }
std::wstring BuildOpenUiArchiveArgs() { return L"--open-ui --archive"; }
std::wstring BuildOpenUiBrowseArgs() { return L"--open-ui --browse"; }

std::wstring BuildAddToArchiveTitle(const std::vector<std::wstring>& paths, const std::wstring& ext, const std::wstring& localeTag)
{
    if (paths.empty()) return GetLocalizedString(StringId::ArchiveFallback, localeTag);

    std::wstring name = paths.size() > 1
        ? GetParentFolderName(paths.front())
        : GetFileNameWithoutExtension(paths.front());

    // Empty (no parent, e.g. a drive root) or a bare drive letter like "C:" \u2014 invalid as a
    // display name (and as the file name ShellCommands.ArchiveAsync would build) \u2014 fall back.
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
