#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "bridge harness failed at line %d: %s\n", __LINE__, #condition); exit(97); } } while (0)

typedef HRESULT (WINAPI *test_compiler)(LPCWSTR, const D3D_SHADER_MACRO*, ID3DInclude*,
    LPCSTR, LPCSTR, UINT, UINT, ID3DBlob**, ID3DBlob**);

typedef struct blob_proxy {
    ID3DBlob object;
    ID3D10BlobVtbl vtable;
    ID3DBlob* real;
    int live;
    char releaseEvent;
} blob_proxy;

static blob_proxy codeProxy;
static blob_proxy diagnosticsProxy;
static ID3D11Device deviceProxy;
static ID3D11DeviceVtbl deviceVtable;
static ID3D11Device* realDevice;
static ID3D11InputLayout layoutProxy;
static ID3D11InputLayoutVtbl layoutVtable;
static ID3D11InputLayout* realLayout;
static HMODULE loadedCompiler;
static test_compiler realCompiler;
static int mode;
static int loadCalls;
static int exportCalls;
static int deviceCalls;
static int layoutCalls;
static int unloadCalls;
static int preloadedShadow;
static char events[16];
static size_t eventCount;

static void record_release(char event)
{
    CHECK(eventCount + 1 < sizeof(events));
    events[eventCount++] = event;
    events[eventCount] = '\0';
}

static LPVOID STDMETHODCALLTYPE blob_pointer(ID3DBlob* object)
{
    blob_proxy* proxy = (blob_proxy*)object;
    CHECK(proxy->live && loadedCompiler != NULL);
    return proxy->real->lpVtbl->GetBufferPointer(proxy->real);
}

static SIZE_T STDMETHODCALLTYPE blob_size(ID3DBlob* object)
{
    blob_proxy* proxy = (blob_proxy*)object;
    CHECK(proxy->live && loadedCompiler != NULL);
    return proxy->real->lpVtbl->GetBufferSize(proxy->real);
}

static ULONG STDMETHODCALLTYPE blob_release(ID3DBlob* object)
{
    blob_proxy* proxy = (blob_proxy*)object;
    ULONG references;
    CHECK(proxy->live && loadedCompiler != NULL && realDevice == NULL && realLayout == NULL);
    if (proxy == &codeProxy)
    {
        CHECK(!diagnosticsProxy.live);
    }
    references = proxy->real->lpVtbl->Release(proxy->real);
    if (proxy == &codeProxy && proxy->live == 1)
    {
        CHECK(references == 0);
    }
    record_release(proxy == &codeProxy && proxy->live == 2 ? 'E' : proxy->releaseEvent);
    proxy->live--;
    return references;
}

static ID3DBlob* wrap_blob(blob_proxy* proxy, ID3DBlob* real, char releaseEvent)
{
    CHECK(!proxy->live && real != NULL);
    proxy->vtable = *real->lpVtbl;
    proxy->vtable.GetBufferPointer = blob_pointer;
    proxy->vtable.GetBufferSize = blob_size;
    proxy->vtable.Release = blob_release;
    proxy->object.lpVtbl = &proxy->vtable;
    proxy->real = real;
    proxy->live = 1;
    proxy->releaseEvent = releaseEvent;
    return &proxy->object;
}

static ULONG STDMETHODCALLTYPE layout_release(ID3D11InputLayout* object)
{
    ULONG references;
    CHECK(object == &layoutProxy && realLayout != NULL && realDevice != NULL && codeProxy.live);
    references = realLayout->lpVtbl->Release(realLayout);
    realLayout = NULL;
    record_release('L');
    return references;
}

static ULONG STDMETHODCALLTYPE device_release(ID3D11Device* object)
{
    ULONG references;
    CHECK(object == &deviceProxy && realDevice != NULL && realLayout == NULL && codeProxy.live);
    references = realDevice->lpVtbl->Release(realDevice);
    realDevice = NULL;
    record_release('D');
    return references;
}

static HRESULT STDMETHODCALLTYPE create_layout(ID3D11Device* object,
    const D3D11_INPUT_ELEMENT_DESC* description, UINT count, const void* bytecode,
    SIZE_T size, ID3D11InputLayout** output)
{
    HRESULT result;
    CHECK(object == &deviceProxy && realDevice != NULL && codeProxy.live && *output == NULL);
    CHECK(count == 1 && strcmp(description->SemanticName, "POSITION") == 0);
    CHECK(description->SemanticIndex == 0 && description->Format == DXGI_FORMAT_R32G32B32_FLOAT);
    CHECK(description->InputSlot == 0 && description->AlignedByteOffset == 0);
    CHECK(description->InputSlotClass == D3D11_INPUT_PER_VERTEX_DATA && description->InstanceDataStepRate == 0);
    CHECK(bytecode == blob_pointer(&codeProxy.object) && size == blob_size(&codeProxy.object));
    CHECK(size > 16 && memcmp(bytecode, "DXBC", 4) == 0);
    layoutCalls++;
    result = realDevice->lpVtbl->CreateInputLayout(realDevice, description, count, bytecode, size, &realLayout);
    CHECK(SUCCEEDED(result) && realLayout != NULL);
    layoutVtable = *realLayout->lpVtbl;
    layoutVtable.Release = layout_release;
    layoutProxy.lpVtbl = &layoutVtable;
    *output = &layoutProxy;
    if (mode == 8)
    {
        return E_FAIL;
    }
    if (mode == 9)
    {
        realLayout->lpVtbl->Release(realLayout);
        realLayout = NULL;
        *output = NULL;
    }
    return result;
}

static HRESULT WINAPI compile_shader(LPCWSTR path, const D3D_SHADER_MACRO* defines,
    ID3DInclude* include, LPCSTR entry, LPCSTR target, UINT flags1, UINT flags2,
    ID3DBlob** code, ID3DBlob** diagnostics)
{
    ID3DBlob* realCode = NULL;
    ID3DBlob* realDiagnostics = NULL;
    HRESULT result;
    CHECK(*code == NULL && *diagnostics == NULL && defines == NULL && include == NULL);
    CHECK(strcmp(entry, "VertexMain") == 0 && strcmp(target, "vs_5_0") == 0 && flags1 == 0 && flags2 == 0);
    CHECK(wcsstr(path, L"\\triangle.hlsl") != NULL);
    result = realCompiler(path, defines, include, entry, target, flags1, flags2, &realCode, &realDiagnostics);
    CHECK(SUCCEEDED(result) && realCode != NULL && realDiagnostics == NULL);
    *code = wrap_blob(&codeProxy, realCode, 'C');
    if (mode == 3)
    {
        realCode->lpVtbl->AddRef(realCode);
        *diagnostics = wrap_blob(&diagnosticsProxy, realCode, 'E');
        return E_FAIL;
    }
    if (mode == 4)
    {
        realCode->lpVtbl->AddRef(realCode);
        codeProxy.live++;
        *diagnostics = *code;
        return E_FAIL;
    }
    if (mode == 5)
    {
        realCode->lpVtbl->Release(realCode);
        codeProxy.live = 0;
        *code = NULL;
    }
    return result;
}

static HMODULE WINAPI load_compiler(LPCWSTR name, HANDLE file, DWORD flags)
{
    CHECK(wcscmp(name, L"D3DCompiler_47.dll") == 0 && file == NULL && flags == LOAD_LIBRARY_SEARCH_SYSTEM32);
    CHECK(++loadCalls == 1);
    if (mode == 1)
    {
        SetLastError(ERROR_MOD_NOT_FOUND);
        return NULL;
    }
    loadedCompiler = LoadLibraryExW(name, file, flags);
    CHECK(loadedCompiler != NULL);
    return loadedCompiler;
}

static FARPROC WINAPI find_compiler(HMODULE module, LPCSTR name)
{
    FARPROC realSymbol;
    FARPROC proxySymbol;
    test_compiler proxy = compile_shader;
    CHECK(module == loadedCompiler && strcmp(name, "D3DCompileFromFile") == 0 && ++exportCalls == 1);
    if (mode == 2)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return NULL;
    }
    realSymbol = GetProcAddress(module, name);
    CHECK(realSymbol != NULL);
    memcpy(&realCompiler, &realSymbol, sizeof(realCompiler));
    memcpy(&proxySymbol, &proxy, sizeof(proxySymbol));
    return proxySymbol;
}

static HRESULT WINAPI create_device(IDXGIAdapter* adapter, D3D_DRIVER_TYPE driverType,
    HMODULE software, UINT flags, const D3D_FEATURE_LEVEL* levels, UINT levelCount,
    UINT version, ID3D11Device** output, D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** context)
{
    HRESULT result;
    CHECK(adapter == NULL && driverType == D3D_DRIVER_TYPE_WARP && software == NULL && flags == 0);
    CHECK(levelCount == 1 && *levels == D3D_FEATURE_LEVEL_11_0 && version == D3D11_SDK_VERSION);
    CHECK(*output == NULL && selected == NULL && context == NULL && codeProxy.live);
    deviceCalls++;
    result = D3D11CreateDevice(adapter, driverType, software, flags, levels, levelCount,
        version, &realDevice, selected, context);
    CHECK(SUCCEEDED(result) && realDevice != NULL);
    deviceVtable = *realDevice->lpVtbl;
    deviceVtable.Release = device_release;
    deviceVtable.CreateInputLayout = create_layout;
    deviceProxy.lpVtbl = &deviceVtable;
    *output = &deviceProxy;
    if (mode == 6)
    {
        return E_FAIL;
    }
    if (mode == 7)
    {
        realDevice->lpVtbl->Release(realDevice);
        realDevice = NULL;
        *output = NULL;
    }
    return result;
}

static BOOL WINAPI unload_compiler(HMODULE module)
{
    CHECK(module == loadedCompiler && !codeProxy.live && !diagnosticsProxy.live);
    CHECK(realDevice == NULL && realLayout == NULL && ++unloadCalls == 1);
    CHECK(FreeLibrary(module));
    loadedCompiler = NULL;
    if (!preloadedShadow)
    {
        CHECK(GetModuleHandleW(L"D3DCompiler_47.dll") == NULL);
    }
    record_release('U');
    if (mode == 10)
    {
        SetLastError(ERROR_BUSY);
        return FALSE;
    }
    return TRUE;
}

#define LoadLibraryExW load_compiler
#define GetProcAddress find_compiler
#define FreeLibrary unload_compiler
#define D3D11CreateDevice create_device
#include "native-dx11-shader-layout-bridge.c"
#undef LoadLibraryExW
#undef GetProcAddress
#undef FreeLibrary
#undef D3D11CreateDevice

int main(int argc, char** argv)
{
    const HRESULT expected[] = {
        S_OK, HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND), HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND),
        E_FAIL, E_FAIL, E_UNEXPECTED, E_FAIL, E_UNEXPECTED, E_FAIL, E_UNEXPECTED,
        HRESULT_FROM_WIN32(ERROR_BUSY)
    };
    static const char* const expectedEvents[] = {
        "LDCU", "", "U", "ECU", "ECU", "U", "DCU", "CU", "LDCU", "DCU", "LDCU"
    };
    if (argc == 2)
    {
        WCHAR shadowPath[BRIDGE_PATH_CAPACITY];
        WCHAR* separator;
        HMODULE shadow;
        CHECK(strcmp(argv[1], "preloaded-shadow") == 0);
        CHECK(GetModuleFileNameW(NULL, shadowPath, BRIDGE_PATH_CAPACITY) != 0);
        separator = wcsrchr(shadowPath, L'\\');
        CHECK(separator != NULL);
        memcpy(separator + 1, L"D3DCompiler_47.dll", sizeof(L"D3DCompiler_47.dll"));
        shadow = LoadLibraryExW(shadowPath, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
        CHECK(shadow != NULL);
        preloadedShadow = 1;
        CHECK(NativeFfiCompileTriangleInputLayout() == HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED));
        CHECK(loadCalls == 1 && exportCalls == 0 && strcmp(events, "U") == 0);
        CHECK(FreeLibrary(shadow));
        CHECK(GetModuleHandleW(L"D3DCompiler_47.dll") == NULL);
        puts("preloaded-shadow: rejected before export resolution; owned module reference released");
        return 0;
    }
    CHECK(argc == 1);
    for (mode = 0; mode < (int)(sizeof(expected) / sizeof(expected[0])); mode++)
    {
        HRESULT result;
        CHECK(GetModuleHandleW(L"D3DCompiler_47.dll") == NULL);
        loadCalls = exportCalls = deviceCalls = layoutCalls = unloadCalls = 0;
        eventCount = 0;
        events[0] = '\0';
        result = NativeFfiCompileTriangleInputLayout();
        CHECK(result == expected[mode] && loadCalls == 1 && strcmp(events, expectedEvents[mode]) == 0);
        CHECK(exportCalls == (mode == 1 ? 0 : 1));
        CHECK(deviceCalls == (mode == 0 || mode >= 6 ? 1 : 0));
        CHECK(layoutCalls == (mode == 0 || mode >= 8 ? 1 : 0));
        CHECK(unloadCalls == (mode == 1 ? 0 : 1));
        CHECK(!codeProxy.live && !diagnosticsProxy.live && realDevice == NULL && realLayout == NULL);
        printf("case %d: 0x%08lX; cleanup=%s\n", mode, (unsigned long)result, events);
    }
    puts("real DXBC pointer/size and WARP layout verified; 11 lifetime/failure cases passed");
    return 0;
}
