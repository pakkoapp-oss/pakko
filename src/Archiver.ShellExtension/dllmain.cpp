#include "pch.h"
#include "ExplorerCommands.h"

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::Make;
using Microsoft::WRL::Module;
using Microsoft::WRL::InProc;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;
using Microsoft::WRL::ClassicCom;

// ---------------------------------------------------------------------------
// Module-level HMODULE — set once in DLL_PROCESS_ATTACH, never changed.
// POD type with trivial construction; used by ShellExtUtils::GetDllDirectory.
// ---------------------------------------------------------------------------
HMODULE g_hModule = nullptr;

// ---------------------------------------------------------------------------
// Minimal class factory template.
// Derives from RuntimeClass so Module<InProc> tracks its lifetime for
// DllCanUnloadNow object counting.
// ---------------------------------------------------------------------------
template<class T>
class PakkoClassFactory final :
    public RuntimeClass<RuntimeClassFlags<ClassicCom>, IClassFactory>
{
public:
    STDMETHODIMP CreateInstance(IUnknown* pOuter, REFIID riid, void** ppv) noexcept override
    {
        try
        {
            if (!ppv) return E_POINTER;
            *ppv = nullptr;
            if (pOuter) return CLASS_E_NOAGGREGATION;
            auto pObj = Make<T>();
            if (!pObj) return E_OUTOFMEMORY;
            return pObj->QueryInterface(riid, ppv);
        }
        catch (...) { return E_FAIL; }
    }

    STDMETHODIMP LockServer(BOOL /*fLock*/) noexcept override
    {
        return S_OK;
    }
};

// ---------------------------------------------------------------------------
// DllMain — store HMODULE and return TRUE. No COM init.
// ---------------------------------------------------------------------------
BOOL WINAPI DllMain(HMODULE hModule, DWORD dwReason, LPVOID /*lpReserved*/)
{
    if (dwReason == DLL_PROCESS_ATTACH)
    {
        g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}

// ---------------------------------------------------------------------------
// DllGetClassObject — exported via .def file.
// Only PakkoRootCommand is registered in the manifest; other command classes
// are instantiated internally by PakkoRootCommand::EnumSubCommands.
// ---------------------------------------------------------------------------
STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;

    if (rclsid == CLSID_PakkoRootCommand)
    {
        auto pFactory = Make<PakkoClassFactory<PakkoRootCommand>>();
        if (!pFactory) return E_OUTOFMEMORY;
        return pFactory->QueryInterface(riid, ppv);
    }

    return CLASS_E_CLASSNOTAVAILABLE;
}

// ---------------------------------------------------------------------------
// DllCanUnloadNow — exported via .def file.
// Returns S_OK when all WRL-tracked objects have been released.
// ---------------------------------------------------------------------------
STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}
