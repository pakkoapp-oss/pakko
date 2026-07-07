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
std::vector<std::wstring> GetPathsFromShellItemArray(IShellItemArray* psia);

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

// Launches Archiver.Shell.exe with the given argument string via CreateProcess.
// Closes PROCESS_INFORMATION handles immediately after launch; does not wait.
// Returns HRESULT_FROM_WIN32 on CreateProcess failure.
HRESULT LaunchShellExe(const std::wstring& args);

// Command-line argument builders.
// Each path is wrapped in double quotes. No escaping needed: '"' is invalid in NTFS names.
std::wstring BuildExtractHereArgs(const std::vector<std::wstring>& paths);
std::wstring BuildExtractFolderArgs(const std::vector<std::wstring>& paths);
std::wstring BuildArchiveArgs(const std::vector<std::wstring>& paths);
std::wstring BuildTestArgs(const std::vector<std::wstring>& paths);

// Dialog-form commands (T-F63): launch Archiver.App via Archiver.Shell's --open-ui flow
// (ShellArgumentParser.ParseOpenUi) instead of running silently.
std::wstring BuildOpenUiExtractArgs(const std::vector<std::wstring>& paths);
std::wstring BuildOpenUiArchiveArgs(const std::vector<std::wstring>& paths);

// Builds the "Add to <name>.zip" context-menu title. For a single selected path, <name> is
// that path's file name without extension; for multiple paths, <name> is their common
// containing folder's name instead (mirrors RunArchiveAsync's naming in
// Archiver.Shell/Program.cs). Returns "Add to archive..." if paths is empty.
std::wstring BuildAddToArchiveTitle(const std::vector<std::wstring>& paths);

// Builds the "Extract to <name>\" context-menu title. For a single selected archive, <name> is
// that archive's file name without extension - the exact subfolder ExtractFolderCommand::Invoke
// creates. For multiple archives each extracts to its own separately-named subfolder (T-F42), so
// no single name would be truthful; returns "Extract each to its own folder" instead. Returns
// "Extract to folder" if paths is empty. Never ends in an ellipsis: Invoke never shows a dialog.
std::wstring BuildExtractFolderTitle(const std::vector<std::wstring>& paths);
