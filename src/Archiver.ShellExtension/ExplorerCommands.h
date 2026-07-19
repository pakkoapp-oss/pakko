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

// {1BC0E1C4-C5BC-48A4-B3F0-A72AC6A16B83}
static const CLSID CLSID_ExtractHereFlatCommand =
    { 0x1BC0E1C4, 0xC5BC, 0x48A4, { 0xB3, 0xF0, 0xA7, 0x2A, 0xC6, 0xA1, 0x6B, 0x83 } };

// {52980F0F-55A8-458B-B68E-BECA0D142107}
static const CLSID CLSID_ExtractFolderCommand =
    { 0x52980F0F, 0x55A8, 0x458B, { 0xB6, 0x8E, 0xBE, 0xCA, 0x0D, 0x14, 0x21, 0x07 } };

// {E84DDF12-7539-4D06-85D8-BFA4F87BCF27}
static const CLSID CLSID_ArchiveCommand =
    { 0xE84DDF12, 0x7539, 0x4D06, { 0x85, 0xD8, 0xBF, 0xA4, 0xF8, 0x7B, 0xCF, 0x27 } };

// {5F440071-6288-4446-AE25-3F4EDA490DDC}
static const CLSID CLSID_TarArchiveCommand =
    { 0x5F440071, 0x6288, 0x4446, { 0xAE, 0x25, 0x3F, 0x4E, 0xDA, 0x49, 0x0D, 0xDC } };

// {BA69EF3A-F324-46CB-9391-6D14FE9597D3}
static const CLSID CLSID_TestCommand =
    { 0xBA69EF3A, 0xF324, 0x46CB, { 0x93, 0x91, 0x6D, 0x14, 0xFE, 0x95, 0x97, 0xD3 } };

// {01564B8D-111A-4999-83B9-A2D1EE2BCD79}
static const CLSID CLSID_ExtractDialogCommand =
    { 0x01564B8D, 0x111A, 0x4999, { 0x83, 0xB9, 0xA2, 0xD1, 0xEE, 0x2B, 0xCD, 0x79 } };

// {ADB98ED2-801C-418D-BE22-95ABA4DA58D0}
static const CLSID CLSID_CompressDialogCommand =
    { 0xADB98ED2, 0x801C, 0x418D, { 0xBE, 0x22, 0x95, 0xAB, 0xA4, 0xDA, 0x58, 0xD0 } };

// {996B23C2-AD0A-4B5E-9FEB-DCFEB6143A78}
static const CLSID CLSID_BrowseCommand =
    { 0x996B23C2, 0xAD0A, 0x4B5E, { 0x9F, 0xEB, 0xDC, 0xFE, 0xB6, 0x14, 0x3A, 0x78 } };

// {5FFA06F4-D608-4B14-B84A-56CAC77EDEC5}
static const CLSID CLSID_HashCommand =
    { 0x5FFA06F4, 0xD608, 0x4B14, { 0xB8, 0x4A, 0x56, 0xCA, 0xC7, 0x7E, 0xDE, 0xC5 } };

// {2C3D0C54-C8B3-469C-BE57-6D913C90FB8B}
static const CLSID CLSID_HashCrc32Command =
    { 0x2C3D0C54, 0xC8B3, 0x469C, { 0xBE, 0x57, 0x6D, 0x91, 0x3C, 0x90, 0xFB, 0x8B } };

// {7A39E7E6-B088-400F-9511-32ED44079463}
static const CLSID CLSID_HashSha256Command =
    { 0x7A39E7E6, 0xB088, 0x400F, { 0x95, 0x11, 0x32, 0xED, 0x44, 0x07, 0x94, 0x63 } };

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
// Leaf command: "Extract to current folder (Intelligently)" - T-F115 rename. Behavior unchanged:
// single root folder in the archive -> strip prefix and merge; multiple roots -> wrap in one new
// folder (ExtractMode.SeparateFolders). The class/CLSID name stays ExtractHereCommand (unchanged,
// minimal diff) even though the title it now returns no longer says "here" - see
// ExtractHereFlatCommand below for the new, genuinely flat command that took over that label.
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
// Leaf command: "Extract here" (T-F115) - genuinely flat: dumps every archive's contents
// directly into its own containing folder, no new wrapper folder ever, regardless of how many
// root entries the archive has (ExtractMode.SingleFolder with DestinationFolder = the archive's
// own folder, no computed subfolder - see Archiver.Shell/Program.cs's RunExtractHereFlatAsync).
// ---------------------------------------------------------------------------
class ExtractHereFlatCommand final :
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
// Leaf command: "Add to <name>.tar" (T-F105) — plain/uncompressed tar only, mirroring
// ArchiveCommand's ZIP one-click but with --format tar. Never tar.gz or any other compressed
// tar variant: one-click commands never prompt the user for anything, so this is limited to the
// one tar-family format with no filter/level choice to make. Compressed tar variants remain
// reachable only through CompressDialogCommand's format selector.
// ---------------------------------------------------------------------------
class TarArchiveCommand final :
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
// Leaf command: "Open" (T-F03) - launches straight into the Archive Browser (T-F05) instead of
// the pending-list/extract-options view. Mirrors NanaZip's real kOpen verb (confirmed against
// NanaZip.UI.Modern/SevenZip/CPP/7zip/UI/Explorer/ContextMenu.cpp/.h): a command distinct from,
// and NOT a replacement for, the dialog-form Extract - NanaZip's kOpen launches its own file
// manager (7zFM.exe) with the archive path, entirely separate from kExtract/kExtractHere/kExtractTo.
// Only shown for a single-item selection - browsing more than one archive at once has no meaning
// (mirrors FileActivationRouter's identical one-archive-only rule for double-click, T-F100).
// ---------------------------------------------------------------------------
class BrowseCommand final :
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
// Leaf command: "CRC-32" (T-F128) — under the "Хеш-суми" submenu. Title is a hardcoded literal,
// not a StringId — algorithm names stay untranslated Latin script everywhere (T-F105 precedent).
// Enabled for any non-empty selection (files and/or folders); Archiver.Shell's FileHashService
// decides how to handle each shape (single file, multi-file, or a single folder recursively).
// ---------------------------------------------------------------------------
class HashCrc32Command final :
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
// Leaf command: "SHA-256" (T-F128) — sibling of HashCrc32Command above, same shape.
// ---------------------------------------------------------------------------
class HashSha256Command final :
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
// Parent command: "Хеш-суми" (T-F128) — always ECF_HASSUBCOMMANDS, enumerates
// HashCrc32Command/HashSha256Command. Mirrors PakkoRootCommand's own pure-submenu-container
// shape (Invoke -> E_NOTIMPL, never called directly).
// ---------------------------------------------------------------------------
class HashCommand final :
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
