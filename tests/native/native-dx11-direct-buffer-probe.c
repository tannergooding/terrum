#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <intrin.h>
#include <windows.h>
#include <d3d11.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This test-only error-injection fixture requires the Windows x64 ABI.
#endif

static ID3D11DeviceVtbl s_deviceTable;
static IDXGISwapChainVtbl s_swapTable;
static ID3D11DeviceContextVtbl s_contextTable;
static ID3D11BufferVtbl s_bufferTable;
static ID3D11Device s_device;
static IDXGISwapChain s_swap;
static ID3D11DeviceContext s_context;
static ID3D11Buffer s_buffer;
static unsigned s_mask;
static unsigned s_mode;
static unsigned s_acquisitions;
static unsigned s_creates;
static unsigned s_destroyed;
static unsigned s_unregistered;
static unsigned s_registerCalls;
static unsigned s_windowCalls;
static unsigned s_registered;
static HWND s_createdWindow;
static HWND s_window;
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

static void Release(unsigned bit, char event)
{
    unsigned expected = (s_mask & 8) ? 8 : (s_mask & 4) ? 4 : (s_mask & 2) ? 2 : (s_mask & 1) ? 1 : 0;
    if (bit != expected || s_creates != 1 || !IsWindow(s_window) || s_destroyed)
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

static ULONG STDMETHODCALLTYPE ReleaseBuffer(ID3D11Buffer* object)
{
    if (object != &s_buffer)
    {
        __fastfail(7);
    }
    Release(8, 'B');
    return 0;
}

static ULONG STDMETHODCALLTYPE UnexpectedAddRef(ID3D11Buffer* object)
{
    (void)object;
    __fastfail(7);
    return 0;
}

static HRESULT STDMETHODCALLTYPE UnexpectedTexture(ID3D11Device* object,
    const D3D11_TEXTURE1D_DESC* descriptor, const D3D11_SUBRESOURCE_DATA* data, ID3D11Texture1D** output)
{
    (void)object; (void)descriptor; (void)data; (void)output;
    __fastfail(7);
    return E_UNEXPECTED;
}

static BOOL Disjoint(const void* first, SIZE_T firstSize, const void* second, SIZE_T secondSize)
{
    return (uintptr_t)first + firstSize <= (uintptr_t)second
        || (uintptr_t)second + secondSize <= (uintptr_t)first;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreateBuffer(ID3D11Device* object,
    const D3D11_BUFFER_DESC* descriptor, const D3D11_SUBRESOURCE_DATA* data, ID3D11Buffer** output)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    const uint32_t vertices[9] = {
        0, 0x3F19999A, 0x3F000000, 0x3F19999A, 0xBF000000, 0x3F000000,
        0xBF19999A, 0xBF000000, 0x3F000000,
    };
    D3D11_BUFFER_DESC expected;
    if (s_creates++ || s_acquisitions != 1 || s_mask != 7 || object != &s_device
        || !descriptor || !data || !output || *output || !data->pSysMem
        || data->SysMemPitch || data->SysMemSlicePitch || ((uintptr_t)stack & 15) != 8
        || ((uintptr_t)descriptor & 3) || ((uintptr_t)data & 7)
        || ((uintptr_t)output & 7) || ((uintptr_t)data->pSysMem & 3)
        || (uintptr_t)descriptor < (uintptr_t)(stack + 5)
        || (uintptr_t)data < (uintptr_t)(stack + 5)
        || (uintptr_t)output < (uintptr_t)(stack + 5)
        || (uintptr_t)data->pSysMem < (uintptr_t)(stack + 5)
        || !Disjoint(descriptor, sizeof(*descriptor), data, sizeof(*data))
        || !Disjoint(descriptor, sizeof(*descriptor), output, sizeof(*output))
        || !Disjoint(descriptor, sizeof(*descriptor), data->pSysMem, sizeof(vertices))
        || !Disjoint(data, sizeof(*data), output, sizeof(*output))
        || !Disjoint(data, sizeof(*data), data->pSysMem, sizeof(vertices))
        || !Disjoint(output, sizeof(*output), data->pSysMem, sizeof(vertices)))
    {
        __fastfail(7);
    }
    memset(&expected, 0, sizeof(expected));
    expected.ByteWidth = sizeof(vertices);
    expected.Usage = D3D11_USAGE_IMMUTABLE;
    expected.BindFlags = D3D11_BIND_VERTEX_BUFFER;
    if (memcmp(descriptor, &expected, sizeof(expected)) || memcmp(data->pSysMem, vertices, sizeof(vertices)))
    {
        __fastfail(7);
    }
    Report('V');
    if (s_mode <= 2 || s_mode == 4 || s_mode == 5)
    {
        s_mask |= 8;
        *output = &s_buffer;
    }
    /* Leave null cells untouched: initial null is the compiler's obligation, not the fixture's. */
    return s_mode == 1 || s_mode == 7 ? S_FALSE
        : s_mode == 2 || s_mode == 8 ? (HRESULT)0x7FFFFFFF
        : s_mode == 3 || s_mode == 4 ? E_FAIL
        : s_mode == 5 ? (HRESULT)0x80000000U
        : s_mode == 9 ? E_OUTOFMEMORY : S_OK;
}

__declspec(dllexport) HRESULT WINAPI NativeFfiDirectBufferAcquisition(
    IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL* levels, UINT levelCount, UINT sdk,
    const DXGI_SWAP_CHAIN_DESC* descriptor, IDXGISwapChain** swap, ID3D11Device** device,
    D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** context)
{
    if (s_acquisitions++ || adapter || driver != D3D_DRIVER_TYPE_WARP || software || flags
        || !levels || levelCount != 1 || *levels != D3D_FEATURE_LEVEL_11_0 || sdk != D3D11_SDK_VERSION
        || !descriptor || !IsWindow(descriptor->OutputWindow) || descriptor->OutputWindow != s_createdWindow
        || !swap || !device || !context || !selected
        || *swap || *device || *context || *selected)
    {
        __fastfail(7);
    }
    s_window = descriptor->OutputWindow;
    s_mask = 7;
    s_deviceTable.Release = ReleaseDevice;
    s_deviceTable.CreateBuffer = CreateBuffer;
    s_deviceTable.CreateTexture1D = UnexpectedTexture;
    s_swapTable.Release = ReleaseSwap;
    s_contextTable.Release = ReleaseContext;
    s_bufferTable.Release = ReleaseBuffer;
    s_bufferTable.AddRef = UnexpectedAddRef;
    s_device.lpVtbl = &s_deviceTable;
    s_swap.lpVtbl = &s_swapTable;
    s_context.lpVtbl = &s_contextTable;
    s_buffer.lpVtbl = &s_bufferTable;
    *device = &s_device;
    *swap = &s_swap;
    *context = &s_context;
    *selected = D3D_FEATURE_LEVEL_11_0;
    Report('A');
    return S_OK;
}

__declspec(dllexport) ATOM WINAPI NativeFfiDirectBufferRegisterClassExW(const WNDCLASSEXW* windowClass)
{
    ATOM registered;
    if (s_registerCalls++ || !windowClass)
    {
        __fastfail(7);
    }
    if (s_mode == 10)
    {
        Report('R');
        return 0;
    }
    registered = RegisterClassExW(windowClass);
    if (!registered)
    {
        __fastfail(7);
    }
    s_registered = 1;
    if (s_mode == 11)
    {
        Report('R');
    }
    return registered;
}

__declspec(dllexport) HWND WINAPI NativeFfiDirectBufferCreateWindowExW(
    DWORD exStyle, LPCWSTR className, LPCWSTR windowName, DWORD style,
    int x, int y, int width, int height, HWND parent, HMENU menu,
    HINSTANCE instance, LPVOID parameter)
{
    HWND window;
    if (s_windowCalls++ || !s_registered)
    {
        __fastfail(7);
    }
    if (s_mode == 11)
    {
        Report('C');
        return NULL;
    }
    window = CreateWindowExW(exStyle, className, windowName, style, x, y, width, height,
        parent, menu, instance, parameter);
    if (!window)
    {
        __fastfail(7);
    }
    s_createdWindow = window;
    return window;
}

__declspec(dllexport) BOOL WINAPI NativeFfiDirectBufferDestroyWindow(HWND window)
{
    if (s_mask || s_creates != 1 || s_destroyed++ || window != s_window || window != s_createdWindow
        || !DestroyWindow(window))
    {
        __fastfail(7);
    }
    Report('W');
    return TRUE;
}

__declspec(dllexport) BOOL WINAPI NativeFfiDirectBufferUnregisterClass(LPCWSTR name, HINSTANCE instance)
{
    if (s_mask || s_unregistered++)
    {
        __fastfail(7);
    }
    if (s_mode == 11)
    {
        if (s_acquisitions || s_creates || s_destroyed || !s_registered || s_windowCalls != 1)
        {
            __fastfail(7);
        }
    }
    else if (s_destroyed != 1 || s_creates != 1 || s_acquisitions != 1 || !s_registered)
    {
        __fastfail(7);
    }
    if (!UnregisterClassW(name, instance))
    {
        __fastfail(7);
    }
    s_registered = 0;
    Report('U');
    return TRUE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance; (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        char modeText[16];
        DWORD length = GetEnvironmentVariableA("TERRUM_DX11_REPORT", s_report, sizeof(s_report));
        if (!length || length >= sizeof(s_report))
        {
            __fastfail(7);
        }
        length = GetEnvironmentVariableA("TERRUM_DX11_MODE", modeText, sizeof(modeText));
        if (!length || length >= sizeof(modeText))
        {
            __fastfail(7);
        }
        s_mode = (unsigned)strtoul(modeText, NULL, 10);
        if (s_mode > 11)
        {
            __fastfail(7);
        }
    }
    if (reason == DLL_PROCESS_DETACH)
    {
        if (s_mask)
        {
            __fastfail(7);
        }
        if (s_mode <= 9)
        {
            if (s_creates != 1 || s_acquisitions != 1 || s_destroyed != 1 || s_unregistered != 1
                || s_registerCalls != 1 || s_windowCalls != 1 || s_registered || !s_createdWindow)
            {
                __fastfail(7);
            }
        }
        else if (s_mode == 10)
        {
            if (s_creates || s_acquisitions || s_destroyed || s_unregistered
                || s_registerCalls != 1 || s_windowCalls || s_registered || s_createdWindow)
            {
                __fastfail(7);
            }
        }
        else if (s_creates || s_acquisitions || s_destroyed || s_unregistered != 1
            || s_registerCalls != 1 || s_windowCalls != 1 || s_registered || s_createdWindow)
        {
            __fastfail(7);
        }
        Report('Z');
    }
    return TRUE;
}
