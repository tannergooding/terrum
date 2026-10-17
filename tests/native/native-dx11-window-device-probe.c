#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <intrin.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This fixture requires the Windows x64 ABI.
#endif

enum
{
    ModeNormalDispatch = 0,
    ModeRegistrationFailure = 1,
    ModeWindowCreationFailure = 2,
    ModeAcquisitionFailure = 3,
    ModeMessageError = 4
};

static const wchar_t s_className[] = L"TerrumOwnerClass";
static ID3D11DeviceVtbl s_deviceTable;
static IDXGISwapChainVtbl s_swapTable;
static ID3D11DeviceContextVtbl s_contextTable;
static ID3D11Device s_device;
static IDXGISwapChain s_swap;
static ID3D11DeviceContext s_context;
static HINSTANCE s_instance;
static WNDPROC s_windowProcedure;
static HWND s_window;
static char s_report[MAX_PATH];
static char s_trace[64];
static unsigned s_mode;
static unsigned s_traceLength;
static unsigned s_registerCalls;
static unsigned s_createCalls;
static unsigned s_acquisitionCalls;
static unsigned s_showCalls;
static unsigned s_messageCalls;
static unsigned s_translateCalls;
static unsigned s_dispatchCalls;
static unsigned s_quitCalls;
static unsigned s_releaseCalls;
static unsigned s_destroyCalls;
static unsigned s_unregisterCalls;
static unsigned s_registered;
static unsigned s_created;
static unsigned s_destroyed;
static unsigned s_quitPosted;

static ULONG STDMETHODCALLTYPE ReleaseDevice(ID3D11Device* object);
static ULONG STDMETHODCALLTYPE ReleaseSwap(IDXGISwapChain* object);
static ULONG STDMETHODCALLTYPE ReleaseContext(ID3D11DeviceContext* object);

static void UnexpectedCall(char event)
{
    if (s_traceLength < sizeof(s_trace) - 1)
    {
        s_trace[s_traceLength++] = event;
        s_trace[s_traceLength] = '\0';
    }
    __fastfail(7);
}

static void Report(char event)
{
    DWORD written = 0;
    HANDLE file;
    if (s_traceLength >= sizeof(s_trace) - 1)
    {
        __fastfail(7);
    }
    s_trace[s_traceLength++] = event;
    s_trace[s_traceLength] = '\0';
    file = CreateFileA(s_report, FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE || !WriteFile(file, &event, 1, &written, NULL)
        || written != 1 || !CloseHandle(file))
    {
        __fastfail(7);
    }
}

static BOOL EqualText(const char* expected)
{
    return strcmp(s_trace, expected) == 0;
}

__declspec(dllexport) ATOM WINAPI NativeFfiWindowDeviceRegisterClassExW(
    const WNDCLASSEXW* windowClass)
{
    if (s_registerCalls++ || !windowClass || windowClass->cbSize != sizeof(*windowClass)
        || windowClass->lpfnWndProc == NULL || windowClass->lpszClassName == NULL
        || wcscmp(windowClass->lpszClassName, s_className) != 0 || windowClass->hInstance == NULL)
    {
        UnexpectedCall('R');
    }
    if (s_mode == ModeRegistrationFailure)
    {
        Report('r');
        return 0;
    }
    if (s_mode != ModeNormalDispatch && s_mode != ModeWindowCreationFailure
        && s_mode != ModeAcquisitionFailure && s_mode != ModeMessageError)
    {
        UnexpectedCall('R');
    }
    s_windowProcedure = windowClass->lpfnWndProc;
    s_instance = windowClass->hInstance;
    s_registered = 1;
    Report('R');
    return 1;
}

__declspec(dllexport) HWND WINAPI NativeFfiWindowDeviceCreateWindowExW(
    DWORD exStyle, LPCWSTR className, LPCWSTR windowName, DWORD style, int x, int y,
    int width, int height, HWND parent, HMENU menu, HINSTANCE instance, LPVOID parameter)
{
    (void)exStyle;
    (void)windowName;
    (void)style;
    (void)x;
    (void)y;
    (void)width;
    (void)height;
    (void)parent;
    (void)menu;
    (void)parameter;
    if (s_mode == ModeRegistrationFailure || !s_registered || s_createCalls++
        || !className || wcscmp(className, s_className) != 0 || !instance
        || s_windowProcedure == NULL)
    {
        UnexpectedCall('W');
    }
    if (instance != s_instance)
    {
        UnexpectedCall('W');
    }
    if (s_mode == ModeWindowCreationFailure)
    {
        Report('w');
        return NULL;
    }
    s_window = (HWND)(uintptr_t)0x12340;
    s_created = 1;
    Report('W');
    return s_window;
}

__declspec(dllexport) HRESULT WINAPI NativeFfiWindowDeviceCreateDeviceAndSwapChain(
    IDXGIAdapter* adapter, D3D_DRIVER_TYPE driverType, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL* featureLevels, UINT featureLevelCount, UINT sdkVersion,
    const DXGI_SWAP_CHAIN_DESC* descriptor, IDXGISwapChain** swapChain,
    ID3D11Device** device, D3D_FEATURE_LEVEL* selectedFeatureLevel,
    ID3D11DeviceContext** immediateContext)
{
    if (s_mode == ModeRegistrationFailure || s_mode == ModeWindowCreationFailure
        || !s_created || s_acquisitionCalls++ || adapter != NULL
        || driverType != D3D_DRIVER_TYPE_WARP || software != NULL || flags != 0
        || !featureLevels || featureLevelCount != 1
        || featureLevels[0] != D3D_FEATURE_LEVEL_11_0 || sdkVersion != D3D11_SDK_VERSION
        || !descriptor || descriptor->OutputWindow != s_window || !swapChain || !device
        || !selectedFeatureLevel || !immediateContext || *swapChain || *device
        || *selectedFeatureLevel || *immediateContext)
    {
        UnexpectedCall('A');
    }
    if (s_mode == ModeAcquisitionFailure)
    {
        Report('a');
        return E_FAIL;
    }
    if (s_mode != ModeNormalDispatch && s_mode != ModeMessageError)
    {
        UnexpectedCall('A');
    }
    s_deviceTable.Release = ReleaseDevice;
    s_swapTable.Release = ReleaseSwap;
    s_contextTable.Release = ReleaseContext;
    s_device.lpVtbl = &s_deviceTable;
    s_swap.lpVtbl = &s_swapTable;
    s_context.lpVtbl = &s_contextTable;
    *swapChain = &s_swap;
    *device = &s_device;
    *selectedFeatureLevel = D3D_FEATURE_LEVEL_11_0;
    *immediateContext = &s_context;
    Report('A');
    return S_OK;
}

__declspec(dllexport) BOOL WINAPI NativeFfiWindowDeviceShowWindow(HWND window, int command)
{
    if (!s_created || s_mode == ModeAcquisitionFailure || s_showCalls++ || window != s_window
        || command != SW_SHOW)
    {
        UnexpectedCall('H');
    }
    Report('H');
    /* This probe records the import without showing or activating its test window. */
    return FALSE;
}

__declspec(dllexport) int WINAPI NativeFfiWindowDeviceGetMessageW(
    MSG* message, HWND window, UINT minimum, UINT maximum)
{
    if (!s_created || s_mode == ModeAcquisitionFailure || s_messageCalls >= 2 || !message
        || window != NULL || minimum != 0 || maximum != 0)
    {
        UnexpectedCall('M');
    }
    ++s_messageCalls;
    if (s_mode == ModeMessageError)
    {
        if (s_messageCalls != 1)
        {
            UnexpectedCall('!');
        }
        Report('!');
        return -1;
    }
    if (s_mode != ModeNormalDispatch)
    {
        UnexpectedCall('M');
    }
    if (s_messageCalls == 1)
    {
        message->hwnd = s_window;
        message->message = WM_CLOSE;
        message->wParam = 0;
        message->lParam = 0;
        Report('M');
        return 1;
    }
    if (s_messageCalls == 2 && s_quitPosted)
    {
        Report('e');
        return 0;
    }
    UnexpectedCall('M');
    return -1;
}

__declspec(dllexport) BOOL WINAPI NativeFfiWindowDeviceTranslateMessage(const MSG* message)
{
    if (s_mode != ModeNormalDispatch || s_translateCalls++ || !message
        || message->hwnd != s_window || message->message != WM_CLOSE)
    {
        UnexpectedCall('T');
    }
    Report('T');
    return TRUE;
}

__declspec(dllexport) LRESULT WINAPI NativeFfiWindowDeviceDispatchMessageW(const MSG* message)
{
    LRESULT result;
    if (s_mode != ModeNormalDispatch || s_dispatchCalls++ || !message
        || message->hwnd != s_window || message->message != WM_CLOSE || !s_windowProcedure)
    {
        UnexpectedCall('P');
    }
    Report('P');
    result = s_windowProcedure(message->hwnd, message->message, message->wParam, message->lParam);
    if (result != 0 || !s_quitPosted)
    {
        UnexpectedCall('P');
    }
    return result;
}

__declspec(dllexport) LRESULT WINAPI NativeFfiWindowDeviceDefWindowProcW(
    HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    (void)window;
    (void)message;
    (void)wParam;
    (void)lParam;
    UnexpectedCall('F');
    return 0;
}

__declspec(dllexport) void WINAPI NativeFfiWindowDevicePostQuitMessage(int exitCode)
{
    if (s_mode != ModeNormalDispatch || s_quitCalls++ || s_dispatchCalls != 1
        || exitCode != 0)
    {
        UnexpectedCall('Q');
    }
    s_quitPosted = 1;
    Report('Q');
}

__declspec(dllexport) BOOL WINAPI NativeFfiWindowDeviceDestroyWindow(HWND window)
{
    if (!s_created || s_destroyCalls++ || s_destroyed || window != s_window)
    {
        UnexpectedCall('X');
    }
    if ((s_mode == ModeNormalDispatch || s_mode == ModeMessageError) && s_releaseCalls != 3)
    {
        UnexpectedCall('X');
    }
    if (s_mode == ModeRegistrationFailure || s_mode == ModeWindowCreationFailure)
    {
        UnexpectedCall('X');
    }
    s_destroyed = 1;
    Report('X');
    return TRUE;
}

__declspec(dllexport) BOOL WINAPI NativeFfiWindowDeviceUnregisterClassW(
    LPCWSTR className, HINSTANCE instance)
{
    if (!s_registered || s_unregisterCalls++ || !className
        || wcscmp(className, s_className) != 0 || !instance)
    {
        UnexpectedCall('U');
    }
    if (s_created && !s_destroyed)
    {
        UnexpectedCall('U');
    }
    if ((s_mode == ModeNormalDispatch || s_mode == ModeMessageError
        || s_mode == ModeAcquisitionFailure) && !s_destroyed)
    {
        UnexpectedCall('U');
    }
    s_registered = 0;
    Report('U');
    return TRUE;
}

static ULONG STDMETHODCALLTYPE ReleaseDevice(ID3D11Device* object)
{
    if (object != &s_device || s_mode == ModeAcquisitionFailure)
    {
        UnexpectedCall('d');
    }
    if (s_releaseCalls != 2 || s_destroyed)
    {
        UnexpectedCall('d');
    }
    ++s_releaseCalls;
    Report('d');
    return 0;
}

static ULONG STDMETHODCALLTYPE ReleaseSwap(IDXGISwapChain* object)
{
    if (object != &s_swap || s_mode == ModeAcquisitionFailure)
    {
        UnexpectedCall('s');
    }
    if (s_releaseCalls != 1 || s_destroyed)
    {
        UnexpectedCall('s');
    }
    ++s_releaseCalls;
    Report('s');
    return 0;
}

static ULONG STDMETHODCALLTYPE ReleaseContext(ID3D11DeviceContext* object)
{
    if (object != &s_context || (s_mode != ModeNormalDispatch && s_mode != ModeMessageError))
    {
        UnexpectedCall('c');
    }
    if (s_releaseCalls != 0 || s_destroyed)
    {
        UnexpectedCall('c');
    }
    ++s_releaseCalls;
    Report('c');
    return 0;
}

static BOOL StateMatches(void)
{
    switch (s_mode)
    {
        case ModeRegistrationFailure:
            return EqualText("r") && s_registerCalls == 1 && !s_registered
                && !s_createCalls && !s_acquisitionCalls && !s_showCalls && !s_messageCalls
                && !s_releaseCalls && !s_destroyCalls && !s_unregisterCalls;
        case ModeWindowCreationFailure:
            return EqualText("RwU") && s_registerCalls == 1 && !s_registered
                && s_createCalls == 1 && !s_created && !s_acquisitionCalls
                && !s_showCalls && !s_messageCalls && !s_releaseCalls
                && !s_destroyCalls && s_unregisterCalls == 1;
        case ModeAcquisitionFailure:
            return EqualText("RWaXU") && !s_registered && s_registerCalls == 1
                && s_createCalls == 1 && s_created && s_acquisitionCalls == 1
                && !s_showCalls && !s_messageCalls && !s_releaseCalls
                && s_destroyCalls == 1 && s_unregisterCalls == 1 && s_destroyed;
        case ModeMessageError:
            return EqualText("RWAH!csdXU") && !s_registered && s_registerCalls == 1
                && s_createCalls == 1 && s_created && s_acquisitionCalls == 1
                && s_showCalls == 1 && s_messageCalls == 1 && !s_translateCalls
                && !s_dispatchCalls && !s_quitCalls && s_releaseCalls == 3
                && s_destroyCalls == 1 && s_unregisterCalls == 1 && s_destroyed;
        case ModeNormalDispatch:
            return EqualText("RWAHMTPQecsdXU") && !s_registered && s_registerCalls == 1
                && s_createCalls == 1 && s_created && s_acquisitionCalls == 1
                && s_showCalls == 1 && s_messageCalls == 2 && s_translateCalls == 1
                && s_dispatchCalls == 1 && s_quitCalls == 1 && s_releaseCalls == 3
                && s_destroyCalls == 1 && s_unregisterCalls == 1 && s_destroyed;
        default:
            return FALSE;
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance;
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        char modeText[16];
        DWORD modeLength = GetEnvironmentVariableA(
            "TERRUM_WINDOW_DEVICE_MODE", modeText, sizeof(modeText));
        DWORD reportLength = GetEnvironmentVariableA(
            "TERRUM_WINDOW_DEVICE_REPORT", s_report, sizeof(s_report));
        if (!modeLength || modeLength >= sizeof(modeText) || !reportLength
            || reportLength >= sizeof(s_report))
        {
            __fastfail(7);
        }
        s_mode = (unsigned)strtoul(modeText, NULL, 10);
        if (s_mode > ModeMessageError)
        {
            __fastfail(7);
        }
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        if (!StateMatches())
        {
            __fastfail(7);
        }
        Report('Z');
    }
    return TRUE;
}
