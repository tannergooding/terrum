#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <intrin.h>
#include <windows.h>
#include <d3d11.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This fixture requires the Windows x64 ABI.
#endif

static ID3D11DeviceVtbl s_deviceTable;
static IDXGISwapChainVtbl s_swapTable;
static ID3D11DeviceContextVtbl s_contextTable;
static struct {
    IUnknownVtbl unknown;
    HRESULT (STDMETHODCALLTYPE* CreateBuffer)(ID3D11Device*, const D3D11_BUFFER_DESC*,
        const D3D11_SUBRESOURCE_DATA*, ID3D11Buffer**);
} s_aliasTable;
static ID3D11Device s_device;
static IDXGISwapChain s_swap;
static ID3D11DeviceContext s_context;
static IUnknown s_alias;
static unsigned s_mask;
static unsigned s_aliasReferences;
static unsigned s_calls;
static unsigned s_mode;
static unsigned s_registerCalls;
static unsigned s_createCalls;
static unsigned s_destroyCalls;
static unsigned s_unregisterCalls;
static BOOL s_aliasMode;
static BOOL s_hardware;
static char s_report[MAX_PATH];

static void Report(char event)
{
    DWORD written = 0;
    HANDLE file = CreateFileA(s_report, FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE || !WriteFile(file, &event, 1, &written, NULL)
        || written != 1 || !CloseHandle(file))
    {
        __fastfail(7);
    }
}

__declspec(noreturn) static void UnexpectedCall(char event)
{
    Report(event);
    __fastfail(7);
}

__declspec(dllexport) ATOM WINAPI NativeFfiDirectWarpRegisterClassExW(const WNDCLASSEXW* windowClass)
{
    if ((s_mode != 21 && s_mode != 22) || s_registerCalls++ || !windowClass
        || windowClass->cbSize != sizeof(*windowClass))
    {
        UnexpectedCall('R');
    }
    if (s_mode == 21)
    {
        Report('r');
        return 0;
    }
    Report('R');
    return 1;
}

__declspec(dllexport) HWND WINAPI NativeFfiDirectWarpCreateWindowExW(DWORD exStyle, LPCWSTR className,
    LPCWSTR windowName, DWORD style, int x, int y, int width, int height, HWND parent,
    HMENU menu, HINSTANCE instance, LPVOID parameter)
{
    (void)exStyle; (void)className; (void)windowName; (void)style;
    (void)x; (void)y; (void)width; (void)height; (void)parent;
    (void)menu; (void)instance; (void)parameter;
    if (s_mode != 22 || s_registerCalls != 1 || s_createCalls++)
    {
        UnexpectedCall('W');
    }
    Report('w');
    return NULL;
}

__declspec(dllexport) BOOL WINAPI NativeFfiDirectWarpDestroyWindow(HWND window)
{
    (void)window;
    ++s_destroyCalls;
    UnexpectedCall('D');
}

__declspec(dllexport) BOOL WINAPI NativeFfiDirectWarpUnregisterClassW(LPCWSTR className, HINSTANCE instance)
{
    if (s_mode != 22 || s_registerCalls != 1 || s_createCalls != 1
        || s_unregisterCalls++ || !className || !instance)
    {
        UnexpectedCall('U');
    }
    Report('U');
    return TRUE;
}

static void Release(unsigned bit, char event)
{
    /* Every populated cell contributes one independent reference, in C/D/S scope-exit order. */
    unsigned expected = (s_mask & 4) ? 4 : (s_mask & 1) ? 1 : (s_mask & 2) ? 2 : 0;
    if (bit != expected)
    {
        __fastfail(7);
    }
    s_mask &= ~bit;
    Report(event);
}

static ULONG STDMETHODCALLTYPE ReleaseDevice(ID3D11Device* object)
{
    if (object != &s_device)
    {
        __fastfail(7);
    }
    Release(1, 'D');
    return 0;
}

static ULONG STDMETHODCALLTYPE ReleaseSwap(IDXGISwapChain* object)
{
    if (object != &s_swap)
    {
        __fastfail(7);
    }
    Release(2, 'S');
    return 0;
}

static ULONG STDMETHODCALLTYPE ReleaseContext(ID3D11DeviceContext* object)
{
    if (object != &s_context)
    {
        __fastfail(7);
    }
    Release(4, 'C');
    return 0;
}

static ULONG STDMETHODCALLTYPE ReleaseAlias(IUnknown* object)
{
    if (object != &s_alias || !s_aliasReferences)
    {
        __fastfail(7);
    }
    --s_aliasReferences;
    Report('R');
    return s_aliasReferences;
}

static BOOL Disjoint(const void* first, SIZE_T firstSize, const void* second, SIZE_T secondSize)
{
    return (uintptr_t)first + firstSize <= (uintptr_t)second
        || (uintptr_t)second + secondSize <= (uintptr_t)first;
}

static HRESULT STDMETHODCALLTYPE RejectBuffer(ID3D11Device* receiver,
    const D3D11_BUFFER_DESC* descriptor, const D3D11_SUBRESOURCE_DATA* data, ID3D11Buffer** output)
{
    if (!s_hardware || receiver != (s_aliasMode ? (ID3D11Device*)&s_alias : &s_device)
        || !descriptor || !data || !output || *output)
    {
        __fastfail(7);
    }
    Report('B');
    return E_FAIL;
}

__declspec(dllexport) __declspec(noinline) HRESULT WINAPI NativeFfiDirectWarpAcquisition(
    IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL* levels, UINT levelCount, UINT sdk,
    const DXGI_SWAP_CHAIN_DESC* descriptor, IDXGISwapChain** swap, ID3D11Device** device,
    D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** context)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    const void* cells[5] = { device, swap, context, selected, levels };
    const SIZE_T sizes[5] = { sizeof(*device), sizeof(*swap), sizeof(*context),
        sizeof(*selected), sizeof(*levels) };
    DXGI_SWAP_CHAIN_DESC expected;
    unsigned mode;
    unsigned i;
    unsigned j;
    mode = s_mode;
    if (mode >= 21 && mode != 23)
    {
        UnexpectedCall('A');
    }
    if (s_calls++ || (mode > 20 && mode != 23))
    {
        __fastfail(7);
    }
    if (adapter || driver != (s_hardware ? D3D_DRIVER_TYPE_HARDWARE : D3D_DRIVER_TYPE_WARP)
        || software || flags || !levels || levelCount != 1 || *levels != D3D_FEATURE_LEVEL_11_0
        || sdk != (mode == 23 ? 0U : D3D11_SDK_VERSION) || !descriptor || ((uintptr_t)descriptor & 7)
        || !IsWindow(descriptor->OutputWindow) || !swap || !device || !context || !selected
        || *swap || *device || *context || *selected)
    {
        __fastfail(7);
    }
    if (((uintptr_t)stack & 15) != 8 || stack[5] != (uintptr_t)levels || stack[6] != levelCount
        || stack[7] != sdk || stack[8] != (uintptr_t)descriptor || stack[9] != (uintptr_t)swap
        || stack[10] != (uintptr_t)device || stack[11] != (uintptr_t)selected
        || stack[12] != (uintptr_t)context)
    {
        __fastfail(7);
    }
    for (i = 0; i < 5; ++i)
    {
        if ((uintptr_t)cells[i] < (uintptr_t)(stack + 13)
            || ((uintptr_t)cells[i] & (sizes[i] - 1))
            || !Disjoint(cells[i], sizes[i], descriptor, sizeof(*descriptor)))
        {
            __fastfail(7);
        }
        for (j = i + 1; j < 5; ++j)
        {
            if (!Disjoint(cells[i], sizes[i], cells[j], sizes[j]))
            {
                __fastfail(7);
            }
        }
    }
    memset(&expected, 0, sizeof(expected));
    expected.BufferDesc.Width = 1;
    expected.BufferDesc.Height = 1;
    if (s_hardware)
    {
        RECT client;
        if (!GetClientRect(descriptor->OutputWindow, &client) || client.left || client.top
            || client.right <= 0 || client.bottom <= 0)
        {
            __fastfail(7);
        }
        expected.BufferDesc.Width = (UINT)client.right;
        expected.BufferDesc.Height = (UINT)client.bottom;
    }
    expected.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    expected.SampleDesc.Count = 1;
    expected.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    expected.BufferCount = 1;
    expected.OutputWindow = descriptor->OutputWindow;
    expected.Windowed = TRUE;
    if (memcmp(&expected, descriptor, sizeof(expected)))
    {
        __fastfail(7);
    }
    Report('A');
    if (mode == 23)
    {
        return E_INVALIDARG;
    }
    s_mask = mode == 1 ? 0 : mode >= 2 && mode <= 8 ? mode - 1
        : mode >= 10 && mode <= 16 ? mode - 10 : 7;
    s_aliasMode = mode == 17 || mode == 18;
    if (s_aliasMode)
    {
        s_aliasTable.unknown.Release = ReleaseAlias;
        s_aliasTable.CreateBuffer = RejectBuffer;
        s_alias.lpVtbl = &s_aliasTable.unknown;
        s_aliasReferences = 3;
        /* Aliasing is test-only: each output still transfers an independent owned reference. */
        *device = (ID3D11Device*)&s_alias;
        *swap = (IDXGISwapChain*)&s_alias;
        *context = (ID3D11DeviceContext*)&s_alias;
        s_mask = 0;
    }
    else
    {
        s_deviceTable.Release = ReleaseDevice;
        s_deviceTable.CreateBuffer = RejectBuffer;
        s_swapTable.Release = ReleaseSwap;
        s_contextTable.Release = ReleaseContext;
        s_device.lpVtbl = &s_deviceTable;
        s_swap.lpVtbl = &s_swapTable;
        s_context.lpVtbl = &s_contextTable;
        if (s_mask & 1)
        {
            *device = &s_device;
        }
        if (s_mask & 2)
        {
            *swap = &s_swap;
        }
        if (s_mask & 4)
        {
            *context = &s_context;
        }
    }
    *selected = D3D_FEATURE_LEVEL_11_0;
    if ((mode >= 1 && mode <= 8) || mode == 18)
    {
        return E_FAIL;
    }
    if (mode == 20)
    {
        return (HRESULT)0x80000000U;
    }
    return mode == 9 ? S_FALSE : mode == 19 ? (HRESULT)0x7FFFFFFFU : S_OK;
}

__declspec(dllexport) FARPROC WINAPI NativeFfiDirectWarpGetProcAddress(HMODULE module, LPCSTR name)
{
    if (strcmp(name, "D3D11CreateDeviceAndSwapChain") == 0)
    {
        return (FARPROC)NativeFfiDirectWarpAcquisition;
    }
    return GetProcAddress(module, name);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance; (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        DWORD length = GetEnvironmentVariableA("TERRUM_DX11_REPORT", s_report, sizeof(s_report));
        char modeText[16];
        DWORD modeLength = GetEnvironmentVariableA("TERRUM_DX11_MODE", modeText, sizeof(modeText));
        char hardwareText[2];
        DWORD hardwareLength = GetEnvironmentVariableA("TERRUM_DX11_HARDWARE", hardwareText, sizeof(hardwareText));
        if (hardwareLength && (hardwareLength != 1 || hardwareText[0] != '1'))
        {
            __fastfail(7);
        }
        s_hardware = hardwareLength != 0;
        if (!length || length >= sizeof(s_report) || modeLength >= sizeof(modeText))
        {
            __fastfail(7);
        }
        if (modeLength)
        {
            s_mode = (unsigned)strtoul(modeText, NULL, 10);
        }
        if (s_mode > 23)
        {
            __fastfail(7);
        }
    }
    if (reason == DLL_PROCESS_DETACH)
    {
        if (s_mode == 21)
        {
            if (s_registerCalls != 1 || s_createCalls || s_destroyCalls || s_unregisterCalls
                || s_calls || s_mask || s_aliasReferences)
            {
                __fastfail(7);
            }
        }
        else if (s_mode == 22)
        {
            if (s_registerCalls != 1 || s_createCalls != 1 || s_destroyCalls || s_unregisterCalls != 1
                || s_calls || s_mask || s_aliasReferences)
            {
                __fastfail(7);
            }
        }
        else if (s_calls != 1 || s_mask || s_aliasReferences)
        {
            __fastfail(7);
        }
        Report('Z');
    }
    return TRUE;
}
