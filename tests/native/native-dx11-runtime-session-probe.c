#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <wchar.h>

typedef HRESULT (WINAPI* Acquisition)(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
    const D3D_FEATURE_LEVEL*, UINT, UINT, const DXGI_SWAP_CHAIN_DESC*,
    IDXGISwapChain**, ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);

static Acquisition s_acquisition;
#define D3D11CreateDeviceAndSwapChain s_acquisition
#define DllMain PipelineDllMain
#include "native-dx11-direct-pipeline-probe.c"
#undef D3D11CreateDeviceAndSwapChain
#undef DllMain

static HMODULE s_runtime, s_preloaded;
static BOOL s_validated, s_unloaded;
static char s_runtimeMode[40];
static unsigned s_aliasReferences;
static ID3D11Device s_fakeDevice;
static IDXGISwapChain s_fakeSwap;
static ID3D11DeviceContext s_fakeContext;
static ID3D11DeviceVtbl s_fakeDeviceTable;
static IDXGISwapChainVtbl s_fakeSwapTable;
static ID3D11DeviceContextVtbl s_fakeContextTable;

static int Mode(const char* name)
{
    return strcmp(s_runtimeMode, name) == 0;
}

static ULONG STDMETHODCALLTYPE ReleaseFakeDevice(ID3D11Device* receiver)
{
    CHECK(s_runtime && !s_unloaded && receiver == &s_fakeDevice);
    if (s_aliasReferences)
    {
        Report('a');
        if (!--s_aliasReferences) { s_mask = 0; }
        return s_aliasReferences;
    }
    Released(1, 'd');
    return 0;
}

static ULONG STDMETHODCALLTYPE ReleaseFakeSwap(IDXGISwapChain* receiver)
{
    CHECK(s_runtime && !s_unloaded && receiver == &s_fakeSwap);
    Released(2, 's');
    return 0;
}

static ULONG STDMETHODCALLTYPE ReleaseFakeContext(ID3D11DeviceContext* receiver)
{
    CHECK(s_runtime && !s_unloaded && receiver == &s_fakeContext);
    Released(4, 'c');
    return 0;
}

static HRESULT STDMETHODCALLTYPE RejectFakeBuffer(ID3D11Device* receiver,
    const D3D11_BUFFER_DESC* description, const D3D11_SUBRESOURCE_DATA* data, ID3D11Buffer** output)
{
    CHECK(s_runtime && !s_unloaded && receiver == &s_fakeDevice && description && data && output && !*output);
    Report('b');
    return E_FAIL;
}

__declspec(noinline) static HRESULT WINAPI Acquire(
    IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL* levels, UINT count, UINT sdk, const DXGI_SWAP_CHAIN_DESC* description,
    IDXGISwapChain** swap, ID3D11Device** device, D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** context)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    CHECK(s_runtime && s_validated && !s_unloaded && !s_mask);
    CHECK(!adapter && driver == (s_hardware ? 1 : 5) && !software && !flags
        && levels && *levels == 0xB000 && count == 1 && sdk == 7 && description
        && swap && !*swap && device && !*device && selected && !*selected && context && !*context);
    CHECK(((uintptr_t)stack & 15) == 8 && stack[5] == (uintptr_t)levels && stack[6] == count
        && stack[7] == sdk && stack[8] == (uintptr_t)description && stack[9] == (uintptr_t)swap
        && stack[10] == (uintptr_t)device && stack[11] == (uintptr_t)selected && stack[12] == (uintptr_t)context);
    CHECK(((uintptr_t)description & 7) == 0 && ((uintptr_t)device & 7) == 0
        && ((uintptr_t)swap & 7) == 0 && ((uintptr_t)context & 7) == 0);
    CHECK(device != (ID3D11Device**)swap && device != (ID3D11Device**)context
        && swap != (IDXGISwapChain**)context);
    s_window = description->OutputWindow;
    if (strncmp(s_runtimeMode, "partial-", 8) == 0 || strncmp(s_runtimeMode, "null-", 5) == 0
        || Mode("alias-failure") || Mode("alias-success"))
    {
        s_mode = 3;
        s_mask = strncmp(s_runtimeMode, "null-", 5) == 0
            ? (unsigned)strtoul(s_runtimeMode + 5, NULL, 10)
            : strncmp(s_runtimeMode, "partial-", 8) == 0 ? (unsigned)strtoul(s_runtimeMode + 8, NULL, 10) : 7;
        CHECK(s_mask <= 7);
        s_fakeDeviceTable.Release = ReleaseFakeDevice;
        s_fakeDeviceTable.CreateBuffer = RejectFakeBuffer;
        s_fakeSwapTable.Release = ReleaseFakeSwap;
        s_fakeContextTable.Release = ReleaseFakeContext;
        s_fakeDevice.lpVtbl = &s_fakeDeviceTable;
        s_fakeSwap.lpVtbl = &s_fakeSwapTable;
        s_fakeContext.lpVtbl = &s_fakeContextTable;
        if (s_mask & 1) { *device = &s_fakeDevice; }
        if (s_mask & 2) { *swap = &s_fakeSwap; }
        if (s_mask & 4) { *context = &s_fakeContext; }
        if (Mode("alias-failure") || Mode("alias-success"))
        {
            s_aliasReferences = 3;
            *swap = (IDXGISwapChain*)&s_fakeDevice;
            *context = (ID3D11DeviceContext*)&s_fakeDevice;
        }
        *selected = D3D_FEATURE_LEVEL_11_0;
        Report('A');
        return strncmp(s_runtimeMode, "null-", 5) == 0 || Mode("alias-success") ? S_FALSE : (HRESULT)0x80000000U;
    }
    HRESULT result = NativeFfiDirectPipelineAcquisition(adapter, driver, software, flags,
        levels, count, sdk, description, swap, device, selected, context);
    if (Mode("acquire-failure"))
    {
        s_mode = 3;
        return E_FAIL;
    }
    if (Mode("buffer-failure")) { s_mode = 7; }
    if (Mode("vertex-failure")) { s_mode = 3; }
    if (Mode("pixel-failure")) { s_mode = 4; }
    return Mode("positive-success") ? S_FALSE : Mode("maximum-success") ? (HRESULT)0x7FFFFFFFU : result;
}

__declspec(dllexport) HMODULE WINAPI NativeFfiRuntimeLoadLibraryExW(LPCWSTR name, HANDLE file, DWORD flags)
{
    if (wcscmp(name, L"D3DCompiler_47.dll") == 0)
    {
        CHECK(s_runtime && s_validated && !s_unloaded && !file && flags == 0x800);
        return LoadLibraryExW(name, file, flags);
    }
    CHECK(GetEnvironmentVariableA("TERRUM_DX11_RUNTIME_MODE", s_runtimeMode, sizeof(s_runtimeMode)));
    CHECK(wcscmp(name, L"d3d11.dll") == 0 && !file && flags == 0x800 && !s_runtime);
    Report('1');
    if (Mode("load-failure")) { s_mode = 3; return NULL; }
    if (Mode("preloaded"))
    {
        WCHAR shadow[MAX_PATH];
        CHECK(GetEnvironmentVariableW(L"TERRUM_DX11_RUNTIME_SHADOW", shadow, MAX_PATH));
        s_preloaded = LoadLibraryExW(shadow, NULL, 0);
        CHECK(s_preloaded);
    }
    s_runtime = LoadLibraryExW(name, file, flags);
    CHECK(s_runtime && (!s_preloaded || s_runtime == s_preloaded));
    return s_runtime;
}

__declspec(dllexport) UINT WINAPI NativeFfiRuntimeGetSystemDirectoryW(LPWSTR buffer, UINT size)
{
    if (s_validated) { return GetSystemDirectoryW(buffer, size); }
    CHECK(s_runtime && buffer && size == 260 && !s_unloaded);
    Report('2');
    if (Mode("directory-zero")) { return 0; }
    if (Mode("directory-truncated")) { return 260; }
    if (Mode("directory-required")) { return 261; }
    if (Mode("directory-max")) { return MAXDWORD; }
    if (Mode("directory-long")) { return 250; }
    if (Mode("directory-boundary"))
    {
        for (UINT index = 0; index < 249; ++index) { buffer[index] = L'a'; }
        buffer[249] = 0;
        return 249;
    }
    return GetSystemDirectoryW(buffer, size);
}

__declspec(dllexport) DWORD WINAPI NativeFfiRuntimeGetModuleFileNameW(HMODULE module, LPWSTR buffer, DWORD size)
{
    if (module != s_runtime) { return GetModuleFileNameW(module, buffer, size); }
    CHECK(s_runtime && buffer && size == 260 && !s_unloaded);
    Report('3');
    if (Mode("module-zero")) { return 0; }
    if (Mode("module-truncated")) { return 260; }
    if (Mode("module-required")) { return 261; }
    if (Mode("module-max")) { return MAXDWORD; }
    if (Mode("wrong-path") || Mode("reject-unload-failure"))
    {
        wcscpy_s(buffer, size, L"C:\\untrusted\\d3d11.dll");
        return (DWORD)wcslen(buffer);
    }
    if (Mode("suffix"))
    {
        DWORD length = GetSystemDirectoryW(buffer, size);
        CHECK(length && length < 200);
        wcscpy_s(buffer + length, size - length, L"\\d3d11.dll.extra");
        return (DWORD)wcslen(buffer);
    }
    if (Mode("directory-boundary"))
    {
        for (UINT index = 0; index < 249; ++index) { buffer[index] = L'a'; }
        wcscpy_s(buffer + 249, size - 249, L"\\d3d11.dll");
        return 259;
    }
    DWORD length = GetModuleFileNameW(module, buffer, size);
    if (Mode("case-fold")) { CHECK(CharLowerBuffW(buffer, length) == length); }
    return length;
}

__declspec(dllexport) int WINAPI NativeFfiRuntimeCompareStringOrdinal(
    LPCWCH expected, int expectedLength, LPCWCH actual, int actualLength, BOOL ignoreCase)
{
    if (s_validated) { return CompareStringOrdinal(expected, expectedLength, actual, actualLength, ignoreCase); }
    CHECK(s_runtime && !s_unloaded && expected && actual && expectedLength > 0 && expectedLength < 260
        && actualLength > 0 && actualLength < 260 && ignoreCase == TRUE
        && expected[expectedLength] == 0 && actual[actualLength] == 0);
    Report('4');
    return Mode("compare-failure") ? 0 : CompareStringOrdinal(expected, expectedLength, actual, actualLength, ignoreCase);
}

__declspec(dllexport) FARPROC WINAPI NativeFfiRuntimeGetProcAddress(HMODULE module, LPCSTR name)
{
    if (module != s_runtime) { return GetProcAddress(module, name); }
    CHECK(s_runtime && !s_unloaded && !s_validated && strcmp(name, "D3D11CreateDeviceAndSwapChain") == 0);
    Report('5');
    if (Mode("export-missing")) { return NULL; }
    s_acquisition = (Acquisition)GetProcAddress(module, name);
    CHECK(s_acquisition);
    s_validated = TRUE;
    return (FARPROC)Acquire;
}

__declspec(dllexport) BOOL WINAPI NativeFfiRuntimeFreeLibrary(HMODULE module)
{
    if (module != s_runtime) { return FreeLibrary(module); }
    CHECK(s_runtime && !s_unloaded && !s_mask && !s_aliasReferences);
    if (s_draws) { CHECK(s_cleared == 1); }
    s_unloaded = TRUE;
    Report('6');
    if (!s_validated) { s_mode = 3; s_window = NULL; }
    if (Mode("unload-failure") || Mode("reject-unload-failure")) { return FALSE; }
    CHECK(FreeLibrary(module));
    s_runtime = NULL;
    if (s_preloaded)
    {
        CHECK(GetModuleHandleW(L"d3d11.dll") == s_preloaded && FreeLibrary(s_preloaded));
        CHECK(!GetModuleHandleW(L"d3d11.dll"));
        s_preloaded = NULL;
    }
    return TRUE;
}

__declspec(dllexport) BOOL WINAPI NativeFfiRuntimeDestroyWindow(HWND window)
{
    CHECK(!s_runtime && !s_mask && DestroyWindow(window));
    Report('W');
    return TRUE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    if (reason == DLL_PROCESS_DETACH
        && (strncmp(s_runtimeMode, "null-", 5) == 0 || Mode("unload-failure") || Mode("reject-unload-failure")))
    {
        return TRUE;
    }
    return PipelineDllMain(instance, reason, reserved);
}
