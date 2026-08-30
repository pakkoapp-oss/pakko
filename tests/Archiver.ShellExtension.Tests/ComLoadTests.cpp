// ComLoadTests.cpp
// COM smoke tests: LoadLibrary → DllGetClassObject → CreateInstance →
// GetTitle non-null → CoTaskMemFree succeeds → DllCanUnloadNow.
//
// The test EXE must have Archiver.ShellExtension.dll in its directory
// (copied by the post-build event in the .vcxproj).

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <objbase.h>
#include <shobjidl_core.h>
#include <gtest/gtest.h>

// CLSIDs from ExplorerCommands.h (duplicated here to avoid pulling in WRL headers).
static const CLSID TEST_CLSID_PakkoRootCommand =
    { 0x1EABC7CE, 0x20A4, 0x48EE, { 0xA9, 0x9F, 0x43, 0xD4, 0xE0, 0xF5, 0x8D, 0x6A } };

// T-F174 (test-coverage audit): the 12 leaf-command CLSIDs, in the exact order
// PakkoRootCommand::EnumSubCommands builds them (see ExplorerCommands.cpp) — duplicated here for
// the same "avoid pulling in WRL headers" reason as the root CLSID above.
static const CLSID TEST_CLSID_BrowseCommand =
    { 0x996B23C2, 0xAD0A, 0x4B5E, { 0x9F, 0xEB, 0xDC, 0xFE, 0xB6, 0x14, 0x3A, 0x78 } };
static const CLSID TEST_CLSID_ExtractDialogCommand =
    { 0x01564B8D, 0x111A, 0x4999, { 0x83, 0xB9, 0xA2, 0xD1, 0xEE, 0x2B, 0xCD, 0x79 } };
static const CLSID TEST_CLSID_ExtractHereFlatCommand =
    { 0x1BC0E1C4, 0xC5BC, 0x48A4, { 0xB3, 0xF0, 0xA7, 0x2A, 0xC6, 0xA1, 0x6B, 0x83 } };
static const CLSID TEST_CLSID_ExtractHereCommand =
    { 0x5677E0FB, 0x114E, 0x45D6, { 0x87, 0x75, 0x04, 0x17, 0x7F, 0x85, 0xE3, 0x46 } };
static const CLSID TEST_CLSID_ExtractFolderCommand =
    { 0x52980F0F, 0x55A8, 0x458B, { 0xB6, 0x8E, 0xBE, 0xCA, 0x0D, 0x14, 0x21, 0x07 } };
static const CLSID TEST_CLSID_CompressDialogCommand =
    { 0xADB98ED2, 0x801C, 0x418D, { 0xBE, 0x22, 0x95, 0xAB, 0xA4, 0xDA, 0x58, 0xD0 } };
static const CLSID TEST_CLSID_ArchiveCommand =
    { 0xE84DDF12, 0x7539, 0x4D06, { 0x85, 0xD8, 0xBF, 0xA4, 0xF8, 0x7B, 0xCF, 0x27 } };
static const CLSID TEST_CLSID_TarArchiveCommand =
    { 0x5F440071, 0x6288, 0x4446, { 0xAE, 0x25, 0x3F, 0x4E, 0xDA, 0x49, 0x0D, 0xDC } };
static const CLSID TEST_CLSID_TestCommand =
    { 0xBA69EF3A, 0xF324, 0x46CB, { 0x93, 0x91, 0x6D, 0x14, 0xFE, 0x95, 0x97, 0xD3 } };
static const CLSID TEST_CLSID_ScanCommand =
    { 0x1E694800, 0x18F6, 0x4C35, { 0xA8, 0x2B, 0x8E, 0x34, 0xA9, 0x49, 0x48, 0xF9 } };
static const CLSID TEST_CLSID_HashCrc32Command =
    { 0x2C3D0C54, 0xC8B3, 0x469C, { 0xBE, 0x57, 0x6D, 0x91, 0x3C, 0x90, 0xFB, 0x8B } };
static const CLSID TEST_CLSID_HashSha256Command =
    { 0x7A39E7E6, 0xB088, 0x400F, { 0x95, 0x11, 0x32, 0xED, 0x44, 0x07, 0x94, 0x63 } };

// Expected EnumSubCommands order — mirrors ExplorerCommands.cpp's own build order exactly (see
// its "Order mirrors NanaZip's real ContextMenu.cpp" comment): Browse first (T-F03), then the
// Extract group (dialog, flat, "here", folder), then the Archive group (dialog, zip, tar), then
// the diagnostic group last (Test, Scan, then the two hash leaves) — CLAUDE.md's hard constraint
// that primary actions precede diagnostic ones (T-F62) is asserted by this exact ordering.
static const CLSID* const kExpectedSubCommandOrder[] = {
    &TEST_CLSID_BrowseCommand, &TEST_CLSID_ExtractDialogCommand, &TEST_CLSID_ExtractHereFlatCommand,
    &TEST_CLSID_ExtractHereCommand, &TEST_CLSID_ExtractFolderCommand, &TEST_CLSID_CompressDialogCommand,
    &TEST_CLSID_ArchiveCommand, &TEST_CLSID_TarArchiveCommand, &TEST_CLSID_TestCommand,
    &TEST_CLSID_ScanCommand, &TEST_CLSID_HashCrc32Command, &TEST_CLSID_HashSha256Command,
};

// Helper: locate Archiver.ShellExtension.dll next to this test EXE.
static std::wstring DllPath()
{
    wchar_t buf[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, buf, MAX_PATH);
    std::wstring path(buf);
    const auto slash = path.rfind(L'\\');
    return (slash != std::wstring::npos ? path.substr(0, slash + 1) : L"")
        + L"Archiver.ShellExtension.dll";
}

// ---------------------------------------------------------------------------
// Fixture: loads and unloads the DLL around each test.
// ---------------------------------------------------------------------------
class DllFixture : public ::testing::Test
{
protected:
    HMODULE m_hMod = nullptr;

    void SetUp() override
    {
        m_hMod = LoadLibraryW(DllPath().c_str());
        ASSERT_NE(m_hMod, nullptr)
            << "LoadLibraryW failed — ensure Archiver.ShellExtension.dll is "
               "in the same directory as this test EXE. "
               "Error: " << GetLastError();
    }

    void TearDown() override
    {
        if (m_hMod)
        {
            FreeLibrary(m_hMod);
            m_hMod = nullptr;
        }
    }
};

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

TEST_F(DllFixture, DllGetClassObjectExported)
{
    auto pfn = reinterpret_cast<HRESULT(WINAPI*)(REFCLSID, REFIID, void**)>(
        GetProcAddress(m_hMod, "DllGetClassObject"));
    ASSERT_NE(pfn, nullptr) << "DllGetClassObject not exported";
}

TEST_F(DllFixture, DllCanUnloadNowExported)
{
    auto pfn = reinterpret_cast<HRESULT(WINAPI*)()>(
        GetProcAddress(m_hMod, "DllCanUnloadNow"));
    ASSERT_NE(pfn, nullptr) << "DllCanUnloadNow not exported";
}

TEST_F(DllFixture, DllGetClassObjectReturnsFactoryForRootCommand)
{
    auto pfnGCO = reinterpret_cast<HRESULT(WINAPI*)(REFCLSID, REFIID, void**)>(
        GetProcAddress(m_hMod, "DllGetClassObject"));
    ASSERT_NE(pfnGCO, nullptr);

    IClassFactory* pCF = nullptr;
    HRESULT hr = pfnGCO(TEST_CLSID_PakkoRootCommand, IID_IClassFactory,
                        reinterpret_cast<void**>(&pCF));
    EXPECT_EQ(hr, S_OK);
    EXPECT_NE(pCF, nullptr);
    if (pCF) pCF->Release();
}

TEST_F(DllFixture, DllGetClassObjectReturnsClassNotAvailableForUnknownClsid)
{
    auto pfnGCO = reinterpret_cast<HRESULT(WINAPI*)(REFCLSID, REFIID, void**)>(
        GetProcAddress(m_hMod, "DllGetClassObject"));
    ASSERT_NE(pfnGCO, nullptr);

    const CLSID unknownClsid = { 0xDEADBEEF, 0, 0, {} };
    void* pv = nullptr;
    HRESULT hr = pfnGCO(unknownClsid, IID_IClassFactory, &pv);
    EXPECT_EQ(hr, CLASS_E_CLASSNOTAVAILABLE);
    EXPECT_EQ(pv, nullptr);
}

TEST_F(DllFixture, CreateInstanceProducesIExplorerCommand)
{
    auto pfnGCO = reinterpret_cast<HRESULT(WINAPI*)(REFCLSID, REFIID, void**)>(
        GetProcAddress(m_hMod, "DllGetClassObject"));
    ASSERT_NE(pfnGCO, nullptr);

    IClassFactory* pCF = nullptr;
    HRESULT hr = pfnGCO(TEST_CLSID_PakkoRootCommand, IID_IClassFactory,
                        reinterpret_cast<void**>(&pCF));
    ASSERT_EQ(hr, S_OK);
    ASSERT_NE(pCF, nullptr);

    IExplorerCommand* pCmd = nullptr;
    hr = pCF->CreateInstance(nullptr, IID_IExplorerCommand,
                             reinterpret_cast<void**>(&pCmd));
    EXPECT_EQ(hr, S_OK);
    EXPECT_NE(pCmd, nullptr);

    if (pCmd) pCmd->Release();
    pCF->Release();
}

TEST_F(DllFixture, GetTitleReturnsNonNullAndCanBeFreedWithCoTaskMemFree)
{
    auto pfnGCO = reinterpret_cast<HRESULT(WINAPI*)(REFCLSID, REFIID, void**)>(
        GetProcAddress(m_hMod, "DllGetClassObject"));
    ASSERT_NE(pfnGCO, nullptr);

    IClassFactory* pCF = nullptr;
    HRESULT hr = pfnGCO(TEST_CLSID_PakkoRootCommand, IID_IClassFactory,
                        reinterpret_cast<void**>(&pCF));
    ASSERT_EQ(hr, S_OK);

    IExplorerCommand* pCmd = nullptr;
    hr = pCF->CreateInstance(nullptr, IID_IExplorerCommand,
                             reinterpret_cast<void**>(&pCmd));
    ASSERT_EQ(hr, S_OK);
    ASSERT_NE(pCmd, nullptr);

    LPWSTR pszTitle = nullptr;
    hr = pCmd->GetTitle(nullptr, &pszTitle);
    EXPECT_EQ(hr, S_OK);
    EXPECT_NE(pszTitle, nullptr);
    // Validates that the string was allocated with CoTaskMemAlloc (SHStrDupW),
    // not new[] or malloc. CoTaskMemFree must not crash.
    if (pszTitle)
    {
        EXPECT_STREQ(pszTitle, L"Pakko");
        CoTaskMemFree(pszTitle);
    }

    pCmd->Release();
    pCF->Release();
}

TEST_F(DllFixture, DllCanUnloadNowReturnsSFalseWhileObjectsAlive)
{
    auto pfnGCO = reinterpret_cast<HRESULT(WINAPI*)(REFCLSID, REFIID, void**)>(
        GetProcAddress(m_hMod, "DllGetClassObject"));
    auto pfnCUN = reinterpret_cast<HRESULT(WINAPI*)()>(
        GetProcAddress(m_hMod, "DllCanUnloadNow"));
    ASSERT_NE(pfnGCO, nullptr);
    ASSERT_NE(pfnCUN, nullptr);

    IClassFactory* pCF = nullptr;
    pfnGCO(TEST_CLSID_PakkoRootCommand, IID_IClassFactory,
           reinterpret_cast<void**>(&pCF));
    ASSERT_NE(pCF, nullptr);

    // Factory is alive → must not unload.
    EXPECT_EQ(pfnCUN(), S_FALSE);

    pCF->Release();

    // All objects released → safe to unload.
    EXPECT_EQ(pfnCUN(), S_OK);
}

// ---------------------------------------------------------------------------
// T-F174 (test-coverage audit): EnumSubCommands / Invoke / GetIcon were the only real entry
// points ComLoadTests.cpp didn't exercise yet — added here reusing the exact same real-DLL-via-
// LoadLibraryW pattern as every test above, per the audit's own minimal-diff conclusion (compiling
// dllmain.cpp/ExplorerCommands.cpp directly into this test target was evaluated and rejected: it
// collides with TestMain.cpp's own g_hModule stub and wouldn't exercise real DllMain semantics).
// ---------------------------------------------------------------------------

static HRESULT CreateRootCommand(HMODULE hMod, IExplorerCommand** ppCmd)
{
    auto pfnGCO = reinterpret_cast<HRESULT(WINAPI*)(REFCLSID, REFIID, void**)>(
        GetProcAddress(hMod, "DllGetClassObject"));
    if (!pfnGCO) return E_NOINTERFACE;

    IClassFactory* pCF = nullptr;
    HRESULT hr = pfnGCO(TEST_CLSID_PakkoRootCommand, IID_IClassFactory,
                        reinterpret_cast<void**>(&pCF));
    if (FAILED(hr)) return hr;

    hr = pCF->CreateInstance(nullptr, IID_IExplorerCommand, reinterpret_cast<void**>(ppCmd));
    pCF->Release();
    return hr;
}

TEST_F(DllFixture, RootCommand_Invoke_ReturnsENotImpl)
{
    IExplorerCommand* pCmd = nullptr;
    ASSERT_EQ(CreateRootCommand(m_hMod, &pCmd), S_OK);
    ASSERT_NE(pCmd, nullptr);

    // The root command itself is never launched — ECF_HASSUBCOMMANDS means Explorer only ever
    // invokes one of its 12 leaves. Invoke on the root is asserted here to stay E_NOTIMPL, not a
    // launch attempt, per ExplorerCommands.cpp's own PakkoRootCommand::Invoke body.
    HRESULT hr = pCmd->Invoke(nullptr, nullptr);
    EXPECT_EQ(hr, E_NOTIMPL);

    pCmd->Release();
}

TEST_F(DllFixture, RootCommand_GetIcon_NeverReturnsSFalseWithNullOutParam)
{
    // CLAUDE.md's COM HRESULT hard constraint: S_FALSE is a SUCCEEDED() code, so a caller
    // checking only SUCCEEDED() would dereference a null *ppszIcon if this ever returned
    // S_FALSE+null — PakkoRootCommand::GetIcon's real body only ever returns S_OK+non-null,
    // E_NOTIMPL+null (no app icon found), or E_FAIL+null (exception) — never S_FALSE.
    IExplorerCommand* pCmd = nullptr;
    ASSERT_EQ(CreateRootCommand(m_hMod, &pCmd), S_OK);
    ASSERT_NE(pCmd, nullptr);

    LPWSTR pszIcon = nullptr;
    HRESULT hr = pCmd->GetIcon(nullptr, &pszIcon);

    EXPECT_NE(hr, S_FALSE) << "S_FALSE is SUCCEEDED() — a SUCCEEDED()-only caller would "
                               "dereference a null icon pointer";
    if (SUCCEEDED(hr))
    {
        EXPECT_NE(pszIcon, nullptr);
        if (pszIcon) CoTaskMemFree(pszIcon);
    }
    else
    {
        EXPECT_EQ(pszIcon, nullptr);
    }

    pCmd->Release();
}

TEST_F(DllFixture, EnumSubCommands_ReturnsAllTwelveLeafCommandsInDocumentedOrder)
{
    // Order asserted here is CLAUDE.md's own hard constraint (T-F62: primary actions — Extract/
    // Archive — always precede diagnostic ones — Test/Scan) made executable, not just documented.
    IExplorerCommand* pRoot = nullptr;
    ASSERT_EQ(CreateRootCommand(m_hMod, &pRoot), S_OK);
    ASSERT_NE(pRoot, nullptr);

    IEnumExplorerCommand* pEnum = nullptr;
    HRESULT hr = pRoot->EnumSubCommands(&pEnum);
    ASSERT_EQ(hr, S_OK);
    ASSERT_NE(pEnum, nullptr);

    constexpr size_t kExpectedCount = std::size(kExpectedSubCommandOrder);
    for (size_t i = 0; i < kExpectedCount; ++i)
    {
        IExplorerCommand* pLeaf = nullptr;
        ULONG fetched = 0;
        hr = pEnum->Next(1, &pLeaf, &fetched);
        ASSERT_EQ(hr, S_OK) << "expected a leaf command at index " << i;
        ASSERT_EQ(fetched, 1u);
        ASSERT_NE(pLeaf, nullptr);

        GUID actual = {};
        hr = pLeaf->GetCanonicalName(&actual);
        EXPECT_EQ(hr, S_OK);
        EXPECT_TRUE(IsEqualGUID(actual, *kExpectedSubCommandOrder[i]))
            << "leaf command at index " << i << " has an unexpected canonical name/order";

        pLeaf->Release();
    }

    // Exactly 12 — a 13th Next() call must report end-of-sequence, not a stray extra command.
    IExplorerCommand* pExtra = nullptr;
    ULONG extraFetched = 0;
    hr = pEnum->Next(1, &pExtra, &extraFetched);
    EXPECT_EQ(hr, S_FALSE);
    EXPECT_EQ(extraFetched, 0u);
    EXPECT_EQ(pExtra, nullptr);

    pEnum->Release();
    pRoot->Release();
}
