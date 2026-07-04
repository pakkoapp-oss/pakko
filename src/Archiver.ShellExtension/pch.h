#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>
#include <shlobj.h>        // IShellItemArray, IShellItem, SIGDN_FILESYSPATH
#include <shlwapi.h>       // PathFindExtensionW, SHStrDupW
#include <objbase.h>       // CoTaskMemAlloc, CoTaskMemFree
#pragma warning(push)
#pragma warning(disable: 4324)
#include <wrl/implements.h>
#include <wrl/module.h>
#pragma warning(pop)
#include <shobjidl_core.h> // IExplorerCommand, IEnumExplorerCommand, EXPCMDSTATE, EXPCMDFLAGS

#include <string>
#include <vector>
#include <mutex>
#include <algorithm>
