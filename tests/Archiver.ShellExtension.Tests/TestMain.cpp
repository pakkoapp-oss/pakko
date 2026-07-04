// TestMain.cpp
// Google Test entry point + COM initialization for the COM smoke tests.
// Also defines g_hModule stub so ShellExtUtils.cpp (compiled directly into
// this project) can link without dllmain.cpp.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <objbase.h>
#include <gtest/gtest.h>

// Stub for g_hModule declared as extern in ShellExtUtils.cpp.
// In the test context, GetDllDirectory()/LaunchShellExe() are not exercised
// by unit tests, so nullptr is safe.
HMODULE g_hModule = nullptr;

// COM environment: initializes STA apartment for ComLoadTests.
class ComEnvironment : public ::testing::Environment
{
public:
    void SetUp() override
    {
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    }

    void TearDown() override
    {
        CoUninitialize();
    }
};

int main(int argc, char** argv)
{
    ::testing::InitGoogleTest(&argc, argv);
    ::testing::AddGlobalTestEnvironment(new ComEnvironment());
    return RUN_ALL_TESTS();
}
