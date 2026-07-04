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
