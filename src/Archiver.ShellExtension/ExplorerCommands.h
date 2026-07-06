#pragma once
#include "pch.h"
#include "ShellExtUtils.h"

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::Make;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;
using Microsoft::WRL::ClassicCom;

// ---------------------------------------------------------------------------
// CLSIDs
// ---------------------------------------------------------------------------

// {1EABC7CE-20A4-48EE-A99F-43D4E0F58D6A}
static const CLSID CLSID_PakkoRootCommand =
    { 0x1EABC7CE, 0x20A4, 0x48EE, { 0xA9, 0x9F, 0x43, 0xD4, 0xE0, 0xF5, 0x8D, 0x6A } };

// {5677E0FB-114E-45D6-8775-04177F85E346}
static const CLSID CLSID_ExtractHereCommand =
    { 0x5677E0FB, 0x114E, 0x45D6, { 0x87, 0x75, 0x04, 0x17, 0x7F, 0x85, 0xE3, 0x46 } };

// {52980F0F-55A8-458B-B68E-BECA0D142107}
static const CLSID CLSID_ExtractFolderCommand =
    { 0x52980F0F, 0x55A8, 0x458B, { 0xB6, 0x8E, 0xBE, 0xCA, 0x0D, 0x14, 0x21, 0x07 } };

// {E84DDF12-7539-4D06-85D8-BFA4F87BCF27}
static const CLSID CLSID_ArchiveCommand =
    { 0xE84DDF12, 0x7539, 0x4D06, { 0x85, 0xD8, 0xBF, 0xA4, 0xF8, 0x7B, 0xCF, 0x27 } };

// {BA69EF3A-F324-46CB-9391-6D14FE9597D3}
static const CLSID CLSID_TestCommand =
    { 0xBA69EF3A, 0xF324, 0x46CB, { 0x93, 0x91, 0x6D, 0x14, 0xFE, 0x95, 0x97, 0xD3 } };

// {01564B8D-111A-4999-83B9-A2D1EE2BCD79}
static const CLSID CLSID_ExtractDialogCommand =
    { 0x01564B8D, 0x111A, 0x4999, { 0x83, 0xB9, 0xA2, 0xD1, 0xEE, 0x2B, 0xCD, 0x79 } };

// {ADB98ED2-801C-418D-BE22-95ABA4DA58D0}
static const CLSID CLSID_CompressDialogCommand =
    { 0xADB98ED2, 0x801C, 0x418D, { 0xBE, 0x22, 0x95, 0xAB, 0xA4, 0xDA, 0x58, 0xD0 } };

// ---------------------------------------------------------------------------
// IEnumExplorerCommand implementation that owns a snapshot of sub-commands.
// ---------------------------------------------------------------------------
class SubCommandEnum final :
    public RuntimeClass<RuntimeClassFlags<ClassicCom>, IEnumExplorerCommand>
{
public:
    void SetCommands(std::vector<ComPtr<IExplorerCommand>> commands);

    STDMETHODIMP Next(ULONG celt, IExplorerCommand** rgelt, ULONG* pceltFetched) noexcept override;
    STDMETHODIMP Skip(ULONG celt) noexcept override;
    STDMETHODIMP Reset() noexcept override;
    STDMETHODIMP Clone(IEnumExplorerCommand** ppenum) noexcept override;

private:
    std::vector<ComPtr<IExplorerCommand>> m_commands;
    ULONG m_current = 0;
};

// ---------------------------------------------------------------------------
// Leaf command: "Extract here"
// ---------------------------------------------------------------------------
class ExtractHereCommand final :
    public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    STDMETHODIMP GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept override;
    STDMETHODIMP GetIcon(IShellItemArray* psia, LPWSTR* ppszIcon) noexcept override;
    STDMETHODIMP GetToolTip(IShellItemArray* psia, LPWSTR* ppszInfotip) noexcept override;
    STDMETHODIMP GetCanonicalName(GUID* pguidCommandName) noexcept override;
    STDMETHODIMP GetState(IShellItemArray* psia, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) noexcept override;
    STDMETHODIMP Invoke(IShellItemArray* psia, IBindCtx* pbc) noexcept override;
    STDMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) noexcept override;
    STDMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept override;
};

// ---------------------------------------------------------------------------
// Leaf command: "Extract to folder..."
// ---------------------------------------------------------------------------
class ExtractFolderCommand final :
    public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    STDMETHODIMP GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept override;
    STDMETHODIMP GetIcon(IShellItemArray* psia, LPWSTR* ppszIcon) noexcept override;
    STDMETHODIMP GetToolTip(IShellItemArray* psia, LPWSTR* ppszInfotip) noexcept override;
    STDMETHODIMP GetCanonicalName(GUID* pguidCommandName) noexcept override;
    STDMETHODIMP GetState(IShellItemArray* psia, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) noexcept override;
    STDMETHODIMP Invoke(IShellItemArray* psia, IBindCtx* pbc) noexcept override;
    STDMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) noexcept override;
    STDMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept override;
};

// ---------------------------------------------------------------------------
// Leaf command: "Add to archive..."
// ---------------------------------------------------------------------------
class ArchiveCommand final :
    public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    STDMETHODIMP GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept override;
    STDMETHODIMP GetIcon(IShellItemArray* psia, LPWSTR* ppszIcon) noexcept override;
    STDMETHODIMP GetToolTip(IShellItemArray* psia, LPWSTR* ppszInfotip) noexcept override;
    STDMETHODIMP GetCanonicalName(GUID* pguidCommandName) noexcept override;
    STDMETHODIMP GetState(IShellItemArray* psia, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) noexcept override;
    STDMETHODIMP Invoke(IShellItemArray* psia, IBindCtx* pbc) noexcept override;
    STDMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) noexcept override;
    STDMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept override;
};

// ---------------------------------------------------------------------------
// Leaf command: "Test archive"
// Shown whenever the selection contains at least one .zip (T-F62) — mirrors NanaZip's
// IDS_CONTEXT_TEST verb, which fires whenever any selected item needs extraction, not only
// when every item does (contrast with ExtractHereCommand/ExtractFolderCommand's AllPathsAreZip).
// ---------------------------------------------------------------------------
class TestCommand final :
    public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    STDMETHODIMP GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept override;
    STDMETHODIMP GetIcon(IShellItemArray* psia, LPWSTR* ppszIcon) noexcept override;
    STDMETHODIMP GetToolTip(IShellItemArray* psia, LPWSTR* ppszInfotip) noexcept override;
    STDMETHODIMP GetCanonicalName(GUID* pguidCommandName) noexcept override;
    STDMETHODIMP GetState(IShellItemArray* psia, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) noexcept override;
    STDMETHODIMP Invoke(IShellItemArray* psia, IBindCtx* pbc) noexcept override;
    STDMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) noexcept override;
    STDMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept override;
};

// ---------------------------------------------------------------------------
// Leaf command: "Extract..." (T-F63) — dialog form. Opens Archiver.App with the
// selected archives pre-loaded instead of extracting silently. Shown whenever the
// selection contains at least one .zip, same as TestCommand (AnyPathIsZip), not the
// stricter AllPathsAreZip ExtractHereCommand/ExtractFolderCommand use.
// ---------------------------------------------------------------------------
class ExtractDialogCommand final :
    public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    STDMETHODIMP GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept override;
    STDMETHODIMP GetIcon(IShellItemArray* psia, LPWSTR* ppszIcon) noexcept override;
    STDMETHODIMP GetToolTip(IShellItemArray* psia, LPWSTR* ppszInfotip) noexcept override;
    STDMETHODIMP GetCanonicalName(GUID* pguidCommandName) noexcept override;
    STDMETHODIMP GetState(IShellItemArray* psia, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) noexcept override;
    STDMETHODIMP Invoke(IShellItemArray* psia, IBindCtx* pbc) noexcept override;
    STDMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) noexcept override;
    STDMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept override;
};

// ---------------------------------------------------------------------------
// Leaf command: "Compress..." (T-F63) — dialog form. Opens Archiver.App with the
// selected items pre-loaded instead of archiving silently. Shown for any selection,
// unlike ArchiveCommand which hides when the selection is all-.zip.
// ---------------------------------------------------------------------------
class CompressDialogCommand final :
    public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    STDMETHODIMP GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept override;
    STDMETHODIMP GetIcon(IShellItemArray* psia, LPWSTR* ppszIcon) noexcept override;
    STDMETHODIMP GetToolTip(IShellItemArray* psia, LPWSTR* ppszInfotip) noexcept override;
    STDMETHODIMP GetCanonicalName(GUID* pguidCommandName) noexcept override;
    STDMETHODIMP GetState(IShellItemArray* psia, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) noexcept override;
    STDMETHODIMP Invoke(IShellItemArray* psia, IBindCtx* pbc) noexcept override;
    STDMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) noexcept override;
    STDMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept override;
};

// ---------------------------------------------------------------------------
// Root command: "Pakko >"
// Registered via com:SurrogateServer in Package.appxmanifest.
// Always ECF_HASSUBCOMMANDS; always returns all three leaf commands.
// ---------------------------------------------------------------------------
class PakkoRootCommand final :
    public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    STDMETHODIMP GetTitle(IShellItemArray* psia, LPWSTR* ppszName) noexcept override;
    STDMETHODIMP GetIcon(IShellItemArray* psia, LPWSTR* ppszIcon) noexcept override;
    STDMETHODIMP GetToolTip(IShellItemArray* psia, LPWSTR* ppszInfotip) noexcept override;
    STDMETHODIMP GetCanonicalName(GUID* pguidCommandName) noexcept override;
    STDMETHODIMP GetState(IShellItemArray* psia, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) noexcept override;
    STDMETHODIMP Invoke(IShellItemArray* psia, IBindCtx* pbc) noexcept override;
    STDMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) noexcept override;
    STDMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) noexcept override;
};
