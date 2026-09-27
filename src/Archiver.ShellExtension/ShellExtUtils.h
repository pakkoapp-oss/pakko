#pragma once
#include "pch.h"

// ---------------------------------------------------------------------------
// COM-free utility functions for Archiver.ShellExtension.
// All functions in this file can be unit-tested without COM.
// ---------------------------------------------------------------------------

// Returns the directory that contains this DLL.
// Uses g_hModule set in DllMain. Returns empty string on failure.
std::wstring GetDllDirectory();

// Returns the full path to Archiver.Shell.exe (sibling of this DLL).
// Returns empty string if GetDllDirectory() fails.
std::wstring GetShellExePath();

// Extracts all filesystem paths from psia.
// Returns an empty vector if psia is null or retrieval fails; never throws.
// Items without a filesystem path are skipped - fine for GetTitle/GetState, but Invoke uses
// GetSelectionPaths instead so part of a selection never vanishes silently (T-F235).
std::vector<std::wstring> GetPathsFromShellItemArray(IShellItemArray* psia);

// T-F235: the selection an Invoke acts on. `complete` is false when psia is null, cannot be
// enumerated, or holds any item without a filesystem path (a phone, a library's virtual node, an
// entry inside Explorer's own compressed-folder view) - Invoke then refuses with a message rather
// than run the command on part of what the user selected.
struct SelectionPaths
{
    std::vector<std::wstring> paths;
    bool complete = true;
};
SelectionPaths GetSelectionPaths(IShellItemArray* psia);

// Owns one Win32 HANDLE (null = none) and closes it on destruction.
class UniqueHandle
{
public:
    UniqueHandle() noexcept = default;
    explicit UniqueHandle(HANDLE handle) noexcept : m_handle(handle) {}
    ~UniqueHandle() { reset(); }
    UniqueHandle(const UniqueHandle&) = delete;
    UniqueHandle& operator=(const UniqueHandle&) = delete;
    UniqueHandle(UniqueHandle&& other) noexcept : m_handle(std::exchange(other.m_handle, nullptr)) {}
    UniqueHandle& operator=(UniqueHandle&& other) noexcept
    {
        if (this != &other)
        {
            reset();
            m_handle = std::exchange(other.m_handle, nullptr);
        }
        return *this;
    }

    HANDLE get() const noexcept { return m_handle; }
    void reset() noexcept
    {
        if (m_handle != nullptr)
        {
            CloseHandle(m_handle);
            m_handle = nullptr;
        }
    }

private:
    HANDLE m_handle = nullptr;
};

// T-F235: the selection goes to Archiver.Shell.exe on its stdin, never on its command line (the
// 32,767-character CreateProcess limit is about 300 long paths). The command line is only
// "<exe>" <command> --paths-stdin; Archiver.Shell/StdinPathList.cs reads the list.
extern const wchar_t kPathsStdinFlag[];

// Upper bound on the stdin payload in bytes - matches StdinPathList.MaxBytes on the C# side.
constexpr size_t kMaxPathListBytes = 256u * 1024u * 1024u;

// "<exePath>" <commandArgs> --paths-stdin
std::wstring BuildShellCommandLine(const std::wstring& exePath, const std::wstring& commandArgs);

// UTF-16 code units: every path followed by one NUL, then one more NUL as the end marker, so a
// list cut short by a failed write is rejected as a whole on the reading side.
std::wstring BuildPathListPayload(const std::vector<std::wstring>& paths);

// True iff a payload of `payloadChars` UTF-16 code units is within kMaxPathListBytes.
bool PathListFitsLimit(size_t payloadChars);

// Creates the stdin pipe: the read end inheritable (it goes to the child), the write end never
// (a copy inherited by any process would keep Archiver.Shell from ever seeing end-of-file). The
// buffer is sized to the payload (capped) so writing does not wait for the child to start.
HRESULT CreatePathListPipe(size_t payloadBytes, UniqueHandle& readEnd, UniqueHandle& writeEnd);

// Writes the whole payload, looping over partial writes.
HRESULT WritePathList(HANDLE writeEnd, const std::wstring& payload);

// Starts exePath with commandLine and `payload` on its stdin; the child inherits only the read
// end. Writes after the launch with the local read end already closed, so a child that never got
// the handle makes the write fail instead of the command vanishing. `process`, when given,
// receives the child's process handle.
HRESULT LaunchWithPathList(const std::wstring& exePath, const std::wstring& commandLine,
    const std::wstring& payload, UniqueHandle* process = nullptr);

// "0x8007000E" for a failed launch's message.
std::wstring FormatHResult(HRESULT hr);

// Returns true iff all paths end with .zip (case-insensitive).
// Returns false for an empty vector.
bool AllPathsAreZip(const std::vector<std::wstring>& paths);

// Returns true iff at least one path ends with .zip (case-insensitive).
bool AnyPathIsZip(const std::vector<std::wstring>& paths);

// T-F86: true iff C:\Windows\System32\tar.exe exists. Checked once via GetFileAttributesW and
// cached in a function-local static (mirrors ExplorerCommands.cpp's GetAppIconPath) - GetState()
// is called by Explorer on every right-click, so this must never re-stat the filesystem per call.
bool TarExeExists();

// Returns true iff path ends with a non-ZIP extension Archiver.Core can extract via ITarService
// (rar, 7z, tar, gz, tgz, bz2, tbz2, xz, txz, zst, tzst, lzma - case-insensitive). Extension-only,
// no content sniffing - mirrors Archiver.App/ViewModels/MainViewModel.cs's _extractableTypes
// allowlist (see DECISIONS.md's T-F86 entry for why an allowlist, not NanaZip's exclusion-list
// approach, and why extension-only, not magic-byte, at gating time).
bool HasSupportedNonZipArchiveExtension(const std::wstring& path);

// Returns true iff all paths are either .zip or a HasSupportedNonZipArchiveExtension format with
// tar.exe present. Returns false for an empty vector. Used in place of AllPathsAreZip for
// Extract-here/Extract-to-folder gating (T-F86).
bool AllPathsAreSupportedArchive(const std::vector<std::wstring>& paths);

// Returns true iff at least one path is .zip or a HasSupportedNonZipArchiveExtension format with
// tar.exe present. Used in place of AnyPathIsZip for Test/Extract-dialog gating (T-F86).
bool AnyPathIsSupportedArchive(const std::vector<std::wstring>& paths);

// Launches Archiver.Shell.exe (next to this DLL) with commandArgs and `paths` on its stdin (see
// LaunchWithPathList). Does not wait for the child. A failure HRESULT means nothing ran - Explorer
// ignores Invoke's return value, so the caller must tell the user.
HRESULT LaunchShellExe(const std::wstring& commandArgs, const std::vector<std::wstring>& paths);

// Command builders - the switch part of Archiver.Shell's command line, consumed by
// ShellArgumentParser. The paths themselves always go on stdin (T-F235).
std::wstring BuildExtractHereArgs();
// T-F115: the new genuinely-flat "Extract here" command - dumps into the archive's own
// containing folder, no wrapper folder created. Consumed by ShellArgumentParser's new
// "--extract-flat" switch (Archiver.Shell/ShellCommands.cs's ExtractHereFlatAsync).
std::wstring BuildExtractHereFlatArgs();
std::wstring BuildExtractFolderArgs();
// T-F105: format is "zip" (default, matches pre-T-F105 behavior — no --format flag emitted) or
// "tar" (emits "--format tar", consumed by ShellArgumentParser.ParseArchive on the .NET side).
std::wstring BuildArchiveArgs(const std::wstring& format = L"zip");
std::wstring BuildTestArgs();
// T-F146: "Scan for threats".
std::wstring BuildScanArgs();
// T-F128: algorithm is "crc32" or "sha256" - always emitted explicitly (unlike BuildArchiveArgs'
// format parameter, which stays flag-less for its zip default), matching ShellArgumentParser's
// ParseHash, which always requires "--algorithm" right after "--hash".
std::wstring BuildHashArgs(const std::wstring& algorithm);

// Dialog-form commands (T-F63): launch Archiver.App via Archiver.Shell's --open-ui flow
// (ShellArgumentParser.ParseOpenUi) instead of running silently.
std::wstring BuildOpenUiExtractArgs();
std::wstring BuildOpenUiArchiveArgs();
// T-F03: launches straight into the Archive Browser (T-F05) instead of the pending-list/
// extract-options view. BrowseCommand::Invoke only ever sends exactly one path.
std::wstring BuildOpenUiBrowseArgs();

// Builds the "Add to <name><ext>" context-menu title (ext defaults to ".zip"; T-F105's
// TarArchiveCommand passes ".tar"). For a single selected path, <name> is that path's file name
// without extension; for multiple paths, <name> is their common containing folder's name instead
// (mirrors ArchiveAsync's naming in Archiver.Shell/ShellCommands.cs). Returns the localized
// "Add to archive..." fallback if paths is empty.
// T-F115: `localeTag` selects the surrounding phrase's translation (see Localization.h);
// defaults to L"en-US" so every pre-existing English-text test keeps passing unchanged -
// production call sites (ExplorerCommands.cpp) pass GetCurrentUILanguageTag() explicitly.
std::wstring BuildAddToArchiveTitle(const std::vector<std::wstring>& paths, const std::wstring& ext = L".zip", const std::wstring& localeTag = L"en-US");

// Builds the "Extract to <name>\" context-menu title. For a single selected archive, <name> is
// that archive's file name without extension - the exact subfolder ExtractFolderCommand::Invoke
// creates. For multiple archives each extracts to its own separately-named subfolder (T-F42), so
// no single name would be truthful; returns the localized "Extract each to its own folder"
// fallback instead. Returns the localized "Extract to folder" fallback if paths is empty. Never
// ends in an ellipsis: Invoke never shows a dialog.
// T-F115: `localeTag` defaults to L"en-US" for the same test-stability reason as
// BuildAddToArchiveTitle above.
std::wstring BuildExtractFolderTitle(const std::vector<std::wstring>& paths, const std::wstring& localeTag = L"en-US");
