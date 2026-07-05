#include "pch.h"
#include "ExplorerCommands.h"

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
    return SHStrDupW(L"Extract here", ppszName);
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
    *pCmdState = AllPathsAreZip(GetPathsFromShellItemArray(psia)) ? ECS_ENABLED : ECS_HIDDEN;
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
// ExtractFolderCommand
// ---------------------------------------------------------------------------

STDMETHODIMP ExtractFolderCommand::GetTitle(IShellItemArray*, LPWSTR* ppszName) noexcept
{
    if (!ppszName) return E_POINTER;
    return SHStrDupW(L"Extract to folder\u2026", ppszName);
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
    *pCmdState = AllPathsAreZip(GetPathsFromShellItemArray(psia)) ? ECS_ENABLED : ECS_HIDDEN;
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
        return SHStrDupW(BuildAddToArchiveTitle(GetPathsFromShellItemArray(psia)).c_str(), ppszName);
    }
    catch (...) { return SHStrDupW(L"Add to archive\u2026", ppszName); }
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

        auto pExtractHere   = Make<ExtractHereCommand>();
        auto pExtractFolder = Make<ExtractFolderCommand>();
        auto pArchive       = Make<ArchiveCommand>();
        if (!pExtractHere || !pExtractFolder || !pArchive) return E_OUTOFMEMORY;

        ComPtr<IExplorerCommand> pCmdA, pCmdB, pCmdC;
        HRESULT hr = pExtractHere.As(&pCmdA);   if (FAILED(hr)) return hr;
        hr = pExtractFolder.As(&pCmdB);          if (FAILED(hr)) return hr;
        hr = pArchive.As(&pCmdC);                if (FAILED(hr)) return hr;

        std::vector<ComPtr<IExplorerCommand>> commands;
        commands.push_back(std::move(pCmdA));
        commands.push_back(std::move(pCmdB));
        commands.push_back(std::move(pCmdC));

        auto pEnum = Make<SubCommandEnum>();
        if (!pEnum) return E_OUTOFMEMORY;
        pEnum->SetCommands(std::move(commands));
        return pEnum.CopyTo(ppEnum);
    }
    catch (...) { return E_FAIL; }
}
