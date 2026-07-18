#include "pch.h"
#include "ExplorerCommands.h"
#include "Localization.h"

// ---------------------------------------------------------------------------
// SubCommandEnum
// ---------------------------------------------------------------------------

void SubCommandEnum::SetCommands(std::vector<ComPtr<IExplorerCommand>> commands)
{
    m_commands = std::move(commands);
    m_current = 0;
}

STDMETHODIMP SubCommandEnum::Next(ULONG celt, IExplorerCommand** rgelt, ULONG* pceltFetched) noexcept
{
    try
    {
        if (!rgelt) return E_POINTER;
        ULONG fetched = 0;
        const auto size = static_cast<ULONG>(m_commands.size());
        while (fetched < celt && m_current < size)
        {
            HRESULT hr = m_commands[m_current].CopyTo(&rgelt[fetched]);
            if (FAILED(hr)) return hr;
            ++fetched;
            ++m_current;
        }
        if (pceltFetched) *pceltFetched = fetched;
        return (fetched == celt) ? S_OK : S_FALSE;
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP SubCommandEnum::Skip(ULONG celt) noexcept
{
    const auto size = static_cast<ULONG>(m_commands.size());
    m_current = (celt > size - m_current) ? size : (m_current + celt);
    return S_OK;
}

STDMETHODIMP SubCommandEnum::Reset() noexcept
{
    m_current = 0;
    return S_OK;
}

STDMETHODIMP SubCommandEnum::Clone(IEnumExplorerCommand** ppenum) noexcept
{
    try
    {
        if (!ppenum) return E_POINTER;
        *ppenum = nullptr;
        auto pClone = Make<SubCommandEnum>();
        if (!pClone) return E_OUTOFMEMORY;
        pClone->m_commands = m_commands;
        pClone->m_current = m_current;
        return pClone.CopyTo(ppenum);
    }
    catch (...) { return E_FAIL; }
}

// ---------------------------------------------------------------------------
// Shared icon path helper (computed once, cached in function-local static).
// ---------------------------------------------------------------------------
static const std::wstring& GetAppIconPath()
{
    static std::once_flag s_flag;
    static std::wstring s_path;
    std::call_once(s_flag, []()
    {
        const std::wstring dir = GetDllDirectory();
        if (!dir.empty())
            s_path = dir + L"\\Archiver.App.exe,0";
    });
    return s_path;
}

// ---------------------------------------------------------------------------
// ExtractHereCommand
// ---------------------------------------------------------------------------

STDMETHODIMP ExtractHereCommand::GetTitle(IShellItemArray*, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    return SHStrDupW(GetLocalizedString(StringId::ExtractHereIntelligent).c_str(), ppszName);
}

STDMETHODIMP ExtractHereCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon) noexcept
{
    if (!ppszIcon) return E_POINTER;
    *ppszIcon = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ExtractHereCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) noexcept
{
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ExtractHereCommand::GetCanonicalName(GUID* pguidCommandName) noexcept
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_ExtractHereCommand;
    return S_OK;
}

STDMETHODIMP ExtractHereCommand::GetState(IShellItemArray* psia, BOOL, EXPCMDSTATE* pCmdState) noexcept
{
    if (!pCmdState) return E_POINTER;
    // T-F86: AllPathsAreSupportedArchive also recognizes RAR/7z/tar-family when tar.exe is
    // present - see DECISIONS.md's T-F86 entry.
    *pCmdState = AllPathsAreSupportedArchive(GetPathsFromShellItemArray(psia)) ? ECS_ENABLED : ECS_HIDDEN;
    return S_OK;
}

STDMETHODIMP ExtractHereCommand::Invoke(IShellItemArray* psia, IBindCtx*) noexcept
{
    try
    {
        const auto paths = GetPathsFromShellItemArray(psia);
        if (paths.empty()) return E_INVALIDARG;
        return LaunchShellExe(BuildExtractHereArgs(paths));
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP ExtractHereCommand::GetFlags(EXPCMDFLAGS* pFlags) noexcept
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

STDMETHODIMP ExtractHereCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

// ---------------------------------------------------------------------------
// ExtractHereFlatCommand (T-F115) - "Extract here", genuinely flat.
// ---------------------------------------------------------------------------

STDMETHODIMP ExtractHereFlatCommand::GetTitle(IShellItemArray*, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    return SHStrDupW(GetLocalizedString(StringId::ExtractHereFlat).c_str(), ppszName);
}

STDMETHODIMP ExtractHereFlatCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon) noexcept
{
    if (!ppszIcon) return E_POINTER;
    *ppszIcon = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ExtractHereFlatCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) noexcept
{
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ExtractHereFlatCommand::GetCanonicalName(GUID* pguidCommandName) noexcept
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_ExtractHereFlatCommand;
    return S_OK;
}

STDMETHODIMP ExtractHereFlatCommand::GetState(IShellItemArray* psia, BOOL, EXPCMDSTATE* pCmdState) noexcept
{
    if (!pCmdState) return E_POINTER;
    *pCmdState = AllPathsAreSupportedArchive(GetPathsFromShellItemArray(psia)) ? ECS_ENABLED : ECS_HIDDEN;
    return S_OK;
}

STDMETHODIMP ExtractHereFlatCommand::Invoke(IShellItemArray* psia, IBindCtx*) noexcept
{
    try
    {
        const auto paths = GetPathsFromShellItemArray(psia);
        if (paths.empty()) return E_INVALIDARG;
        return LaunchShellExe(BuildExtractHereFlatArgs(paths));
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP ExtractHereFlatCommand::GetFlags(EXPCMDFLAGS* pFlags) noexcept
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

STDMETHODIMP ExtractHereFlatCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

// ---------------------------------------------------------------------------
// ExtractFolderCommand
// ---------------------------------------------------------------------------

STDMETHODIMP ExtractFolderCommand::GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    try
    {
        return SHStrDupW(BuildExtractFolderTitle(GetPathsFromShellItemArray(psia), GetCurrentUILanguageTag()).c_str(), ppszName);
    }
    catch (...) { return SHStrDupW(GetLocalizedString(StringId::ExtractFolderFallback).c_str(), ppszName); }
}

STDMETHODIMP ExtractFolderCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon) noexcept
{
    if (!ppszIcon) return E_POINTER;
    *ppszIcon = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ExtractFolderCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) noexcept
{
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ExtractFolderCommand::GetCanonicalName(GUID* pguidCommandName) noexcept
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_ExtractFolderCommand;
    return S_OK;
}

STDMETHODIMP ExtractFolderCommand::GetState(IShellItemArray* psia, BOOL, EXPCMDSTATE* pCmdState) noexcept
{
    if (!pCmdState) return E_POINTER;
    // T-F86: see ExtractHereCommand::GetState above.
    *pCmdState = AllPathsAreSupportedArchive(GetPathsFromShellItemArray(psia)) ? ECS_ENABLED : ECS_HIDDEN;
    return S_OK;
}

STDMETHODIMP ExtractFolderCommand::Invoke(IShellItemArray* psia, IBindCtx*) noexcept
{
    try
    {
        const auto paths = GetPathsFromShellItemArray(psia);
        if (paths.empty()) return E_INVALIDARG;
        return LaunchShellExe(BuildExtractFolderArgs(paths));
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP ExtractFolderCommand::GetFlags(EXPCMDFLAGS* pFlags) noexcept
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

STDMETHODIMP ExtractFolderCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

// ---------------------------------------------------------------------------
// ArchiveCommand
// ---------------------------------------------------------------------------

STDMETHODIMP ArchiveCommand::GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    try
    {
        return SHStrDupW(BuildAddToArchiveTitle(GetPathsFromShellItemArray(psia), L".zip", GetCurrentUILanguageTag()).c_str(), ppszName);
    }
    catch (...) { return SHStrDupW(GetLocalizedString(StringId::ArchiveFallback).c_str(), ppszName); }
}

STDMETHODIMP ArchiveCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon) noexcept
{
    if (!ppszIcon) return E_POINTER;
    *ppszIcon = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ArchiveCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) noexcept
{
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ArchiveCommand::GetCanonicalName(GUID* pguidCommandName) noexcept
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_ArchiveCommand;
    return S_OK;
}

STDMETHODIMP ArchiveCommand::GetState(IShellItemArray* psia, BOOL, EXPCMDSTATE* pCmdState) noexcept
{
    if (!pCmdState) return E_POINTER;
    *pCmdState = AllPathsAreZip(GetPathsFromShellItemArray(psia)) ? ECS_HIDDEN : ECS_ENABLED;
    return S_OK;
}

STDMETHODIMP ArchiveCommand::Invoke(IShellItemArray* psia, IBindCtx*) noexcept
{
    try
    {
        const auto paths = GetPathsFromShellItemArray(psia);
        if (paths.empty()) return E_INVALIDARG;
        return LaunchShellExe(BuildArchiveArgs(paths));
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP ArchiveCommand::GetFlags(EXPCMDFLAGS* pFlags) noexcept
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

STDMETHODIMP ArchiveCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

// ---------------------------------------------------------------------------
// TarArchiveCommand (T-F105) — "Add to <name>.tar", plain/uncompressed tar only
// ---------------------------------------------------------------------------

STDMETHODIMP TarArchiveCommand::GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    try
    {
        return SHStrDupW(BuildAddToArchiveTitle(GetPathsFromShellItemArray(psia), L".tar", GetCurrentUILanguageTag()).c_str(), ppszName);
    }
    catch (...) { return SHStrDupW(GetLocalizedString(StringId::ArchiveFallback).c_str(), ppszName); }
}

STDMETHODIMP TarArchiveCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon) noexcept
{
    if (!ppszIcon) return E_POINTER;
    *ppszIcon = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP TarArchiveCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) noexcept
{
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP TarArchiveCommand::GetCanonicalName(GUID* pguidCommandName) noexcept
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_TarArchiveCommand;
    return S_OK;
}

STDMETHODIMP TarArchiveCommand::GetState(IShellItemArray* psia, BOOL, EXPCMDSTATE* pCmdState) noexcept
{
    if (!pCmdState) return E_POINTER;
    *pCmdState = AllPathsAreZip(GetPathsFromShellItemArray(psia)) ? ECS_HIDDEN : ECS_ENABLED;
    return S_OK;
}

STDMETHODIMP TarArchiveCommand::Invoke(IShellItemArray* psia, IBindCtx*) noexcept
{
    try
    {
        const auto paths = GetPathsFromShellItemArray(psia);
        if (paths.empty()) return E_INVALIDARG;
        return LaunchShellExe(BuildArchiveArgs(paths, L"tar"));
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP TarArchiveCommand::GetFlags(EXPCMDFLAGS* pFlags) noexcept
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

STDMETHODIMP TarArchiveCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

// ---------------------------------------------------------------------------
// TestCommand
// ---------------------------------------------------------------------------

STDMETHODIMP TestCommand::GetTitle(IShellItemArray*, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    return SHStrDupW(GetLocalizedString(StringId::TestArchive).c_str(), ppszName);
}

STDMETHODIMP TestCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon) noexcept
{
    if (!ppszIcon) return E_POINTER;
    *ppszIcon = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP TestCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) noexcept
{
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP TestCommand::GetCanonicalName(GUID* pguidCommandName) noexcept
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_TestCommand;
    return S_OK;
}

STDMETHODIMP TestCommand::GetState(IShellItemArray* psia, BOOL, EXPCMDSTATE* pCmdState) noexcept
{
    if (!pCmdState) return E_POINTER;
    // T-F62: unlike ExtractHere/ExtractFolder, Test appears whenever the selection contains at
    // least one archive — matching NanaZip's NeedExtract-gated Test verb, which fires on a mixed
    // selection too. ZipArchiveService.TestAsync skips non-zip paths internally, the same
    // defense-in-depth pattern ExtractAsync already relies on.
    // T-F86: deliberately stays AnyPathIsZip, NOT AnyPathIsSupportedArchive - ITarService has no
    // Test/verify method, so enabling this for RAR/7z/tar would show "Test archive", run
    // RunTestAsync's ZipArchiveService.TestAsync (which silently skips non-zip paths), and report
    // a false "No errors detected" for an archive that was never actually tested. See
    // DECISIONS.md's T-F86 entry.
    *pCmdState = AnyPathIsZip(GetPathsFromShellItemArray(psia)) ? ECS_ENABLED : ECS_HIDDEN;
    return S_OK;
}

STDMETHODIMP TestCommand::Invoke(IShellItemArray* psia, IBindCtx*) noexcept
{
    try
    {
        const auto paths = GetPathsFromShellItemArray(psia);
        if (paths.empty()) return E_INVALIDARG;
        return LaunchShellExe(BuildTestArgs(paths));
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP TestCommand::GetFlags(EXPCMDFLAGS* pFlags) noexcept
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

STDMETHODIMP TestCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

// ---------------------------------------------------------------------------
// ExtractDialogCommand
// ---------------------------------------------------------------------------

STDMETHODIMP ExtractDialogCommand::GetTitle(IShellItemArray*, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    return SHStrDupW(GetLocalizedString(StringId::ExtractDialog).c_str(), ppszName);
}

STDMETHODIMP ExtractDialogCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon) noexcept
{
    if (!ppszIcon) return E_POINTER;
    *ppszIcon = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ExtractDialogCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) noexcept
{
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP ExtractDialogCommand::GetCanonicalName(GUID* pguidCommandName) noexcept
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_ExtractDialogCommand;
    return S_OK;
}

STDMETHODIMP ExtractDialogCommand::GetState(IShellItemArray* psia, BOOL, EXPCMDSTATE* pCmdState) noexcept
{
    if (!pCmdState) return E_POINTER;
    // Same "any, not all" breadth as TestCommand, not the stricter AllPathsAreSupportedArchive
    // ExtractHereCommand/ExtractFolderCommand use — a dialog lets the user reconsider the
    // destination even for a mixed selection that includes at least one archive.
    // T-F86: unlike TestCommand, this one DOES use AnyPathIsSupportedArchive - Invoke launches
    // --open-ui --extract, which opens Archiver.App and routes through IExtractionRouter
    // (MainViewModel/ExtractionRouter), which does support RAR/7z/tar-family (T-F85). No false
    // "tested OK" risk here since a dialog opens rather than a silent pass/fail messagebox.
    *pCmdState = AnyPathIsSupportedArchive(GetPathsFromShellItemArray(psia)) ? ECS_ENABLED : ECS_HIDDEN;
    return S_OK;
}

STDMETHODIMP ExtractDialogCommand::Invoke(IShellItemArray* psia, IBindCtx*) noexcept
{
    try
    {
        const auto paths = GetPathsFromShellItemArray(psia);
        if (paths.empty()) return E_INVALIDARG;
        return LaunchShellExe(BuildOpenUiExtractArgs(paths));
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP ExtractDialogCommand::GetFlags(EXPCMDFLAGS* pFlags) noexcept
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

STDMETHODIMP ExtractDialogCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

// ---------------------------------------------------------------------------
// CompressDialogCommand
// ---------------------------------------------------------------------------

STDMETHODIMP CompressDialogCommand::GetTitle(IShellItemArray*, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    return SHStrDupW(GetLocalizedString(StringId::CompressDialog).c_str(), ppszName);
}

STDMETHODIMP CompressDialogCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon) noexcept
{
    if (!ppszIcon) return E_POINTER;
    *ppszIcon = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP CompressDialogCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) noexcept
{
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP CompressDialogCommand::GetCanonicalName(GUID* pguidCommandName) noexcept
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_CompressDialogCommand;
    return S_OK;
}

STDMETHODIMP CompressDialogCommand::GetState(IShellItemArray*, BOOL, EXPCMDSTATE* pCmdState) noexcept
{
    if (!pCmdState) return E_POINTER;
    // Shown for any selection (unlike ArchiveCommand, which hides for an all-.zip selection) —
    // archiving a .zip into a new .zip via the dialog is a valid, reachable choice.
    *pCmdState = ECS_ENABLED;
    return S_OK;
}

STDMETHODIMP CompressDialogCommand::Invoke(IShellItemArray* psia, IBindCtx*) noexcept
{
    try
    {
        const auto paths = GetPathsFromShellItemArray(psia);
        if (paths.empty()) return E_INVALIDARG;
        return LaunchShellExe(BuildOpenUiArchiveArgs(paths));
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP CompressDialogCommand::GetFlags(EXPCMDFLAGS* pFlags) noexcept
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

STDMETHODIMP CompressDialogCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

// ---------------------------------------------------------------------------
// PakkoRootCommand
// ---------------------------------------------------------------------------

STDMETHODIMP PakkoRootCommand::GetTitle(IShellItemArray*, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    return SHStrDupW(L"Pakko", ppszName);
}

STDMETHODIMP PakkoRootCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon) noexcept
{
    try
    {
        if (!ppszIcon) return E_POINTER;
        *ppszIcon = nullptr;
        const std::wstring& iconPath = GetAppIconPath();
        if (iconPath.empty()) return E_NOTIMPL;
        return SHStrDupW(iconPath.c_str(), ppszIcon);
    }
    catch (...) { return E_FAIL; }
}

STDMETHODIMP PakkoRootCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) noexcept
{
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

STDMETHODIMP PakkoRootCommand::GetCanonicalName(GUID* pguidCommandName) noexcept
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_PakkoRootCommand;
    return S_OK;
}

STDMETHODIMP PakkoRootCommand::GetState(IShellItemArray*, BOOL, EXPCMDSTATE* pCmdState) noexcept
{
    if (!pCmdState) return E_POINTER;
    *pCmdState = ECS_ENABLED;
    return S_OK;
}

STDMETHODIMP PakkoRootCommand::Invoke(IShellItemArray*, IBindCtx*) noexcept
{
    return E_NOTIMPL;
}

STDMETHODIMP PakkoRootCommand::GetFlags(EXPCMDFLAGS* pFlags) noexcept
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_HASSUBCOMMANDS;
    return S_OK;
}

STDMETHODIMP PakkoRootCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept
{
    try
    {
        if (!ppEnum) return E_POINTER;
        *ppEnum = nullptr;

        auto pExtractDialog = Make<ExtractDialogCommand>();
        auto pExtractHereFlat = Make<ExtractHereFlatCommand>();
        auto pExtractHere   = Make<ExtractHereCommand>();
        auto pExtractFolder = Make<ExtractFolderCommand>();
        auto pCompressDialog = Make<CompressDialogCommand>();
        auto pArchive       = Make<ArchiveCommand>();
        auto pTarArchive    = Make<TarArchiveCommand>();
        auto pTest          = Make<TestCommand>();
        if (!pExtractDialog || !pExtractHereFlat || !pExtractHere || !pExtractFolder || !pCompressDialog || !pArchive || !pTarArchive || !pTest)
            return E_OUTOFMEMORY;

        ComPtr<IExplorerCommand> pCmdExtractDialog, pCmdExtractHereFlat, pCmdA, pCmdB, pCmdCompressDialog, pCmdC, pCmdTarArchive, pCmdTest;
        HRESULT hr = pExtractDialog.As(&pCmdExtractDialog); if (FAILED(hr)) return hr;
        hr = pExtractHereFlat.As(&pCmdExtractHereFlat);      if (FAILED(hr)) return hr;
        hr = pExtractHere.As(&pCmdA);                       if (FAILED(hr)) return hr;
        hr = pExtractFolder.As(&pCmdB);                      if (FAILED(hr)) return hr;
        hr = pCompressDialog.As(&pCmdCompressDialog);        if (FAILED(hr)) return hr;
        hr = pArchive.As(&pCmdC);                            if (FAILED(hr)) return hr;
        hr = pTarArchive.As(&pCmdTarArchive);                if (FAILED(hr)) return hr;
        hr = pTest.As(&pCmdTest);                            if (FAILED(hr)) return hr;

        // Order mirrors NanaZip's real ContextMenu.cpp: within each group, the dialog-based
        // command precedes its one-click siblings (kExtract before kExtractHere/kExtractTo;
        // kCompress before kCompressToZip). Test archive is a diagnostic/verification action,
        // not a primary one — it goes last, after every Extract/Archive variant (deliberate
        // deviation from NanaZip's own Test-before-Compress grouping, per project direction:
        // primary actions before Test, always). T-F105: "Add to X.tar" sits right after
        // "Add to X.zip" — both are one-click archive-creation commands, kept adjacent.
        // T-F115: the new flat "Extract here" sits between the dialog and the (renamed)
        // "...Intelligently" command, matching NanaZip's own three-way extract-verb layout.
        std::vector<ComPtr<IExplorerCommand>> commands;
        commands.push_back(std::move(pCmdExtractDialog));
        commands.push_back(std::move(pCmdExtractHereFlat));
        commands.push_back(std::move(pCmdA));
        commands.push_back(std::move(pCmdB));
        commands.push_back(std::move(pCmdCompressDialog));
        commands.push_back(std::move(pCmdC));
        commands.push_back(std::move(pCmdTarArchive));
        commands.push_back(std::move(pCmdTest));

        auto pEnum = Make<SubCommandEnum>();
        if (!pEnum) return E_OUTOFMEMORY;
        pEnum->SetCommands(std::move(commands));
        return pEnum.CopyTo(ppEnum);
    }
    catch (...) { return E_FAIL; }
}
