#include <initguid.h>
#define WARP_PRESENTATION_TEST
#define WARP_OUTPUT_WIDTH 320
#define WARP_OUTPUT_HEIGHT 320
#include "native-dx11-warp-output-test.c"
#include <dwmapi.h>

typedef enum presentation_failure {
    presentation_ok,
    desktop_unavailable,
    composition_unavailable,
    register_failure,
    adjust_failure,
    window_failure,
    client_failure,
    empty_client,
    close_initial,
    quit_initial,
    minimize_initial,
    resize_initial,
    hidden_initial,
    paint_begin_failure,
    paint_end_failure,
    timeout_initial,
    timeout_messages,
    missing_outputs,
    failed_outputs,
    parent_partial,
    parent_missing,
    association_failure,
    bad_backbuffer,
    close_before_present,
    close_after_present,
    close_after_composition,
    minimize_before_present,
    resize_before_present,
    desktop_after_present,
    timeout_before_present,
    present_failure,
    present_occluded,
    present_positive,
    present_removed,
    present_reset,
    present_removed_without_reason,
    map_removed,
    composition_failure,
    composition_positive,
    destroy_failure,
    unregister_failure,
    quit_before_present,
    quit_after_present,
    quit_after_composition,
    close_twice
} presentation_failure;

typedef struct swap_proxy {
    IDXGISwapChain object;
    IDXGISwapChainVtbl vtable;
    IDXGISwapChain* real;
} swap_proxy;

typedef struct factory_proxy {
    IDXGIFactory object;
    IDXGIFactoryVtbl vtable;
    IDXGIFactory* real;
} factory_proxy;

static swap_proxy swap;
static factory_proxy associatedFactory;
static presentation_failure presentationFailure;
static UINT outputMask;
static UINT registerCalls, windowCalls, showCalls, destroyCalls, unregisterCalls;
static UINT parentCalls, associationCalls, getBufferCalls, presentCalls, compositionCalls, reasonCalls;
static UINT swapReleaseCalls, factoryReleaseCalls, beginPaintCalls, endPaintCalls, dispatchCalls;
static UINT translateCalls, closeCalls, consumedQuitCalls, postedQuitCalls;
static BOOL inDestroy;
static const MSG* translatedMessage;
static DWORD creatingThread;
static HWND ownedWindow;
static HWND destroyedWindow;
static WNDPROC helperWndproc;
static void* callbackState;
static BOOL injected;

static HRESULT WINAPI create_swap_device(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
    const D3D_FEATURE_LEVEL*, UINT, UINT, const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**,
    ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);
static ATOM WINAPI register_class(const WNDCLASSEXW*);
static BOOL WINAPI adjust_window(LPRECT, DWORD, BOOL, DWORD);
static HWND WINAPI create_window(DWORD, LPCWSTR, LPCWSTR, DWORD, int, int, int, int,
    HWND, HMENU, HINSTANCE, LPVOID);
static BOOL WINAPI show_window(HWND, int);
static BOOL WINAPI client_rect(HWND, LPRECT);
static BOOL WINAPI visible_window(HWND);
static BOOL WINAPI peek_message(LPMSG, HWND, UINT, UINT, UINT);
static BOOL WINAPI translate_message(const MSG*);
static LRESULT WINAPI dispatch_message(const MSG*);
static void WINAPI post_quit(int);
static HDC WINAPI begin_paint(HWND, LPPAINTSTRUCT);
static BOOL WINAPI end_paint(HWND, const PAINTSTRUCT*);
static BOOL WINAPI destroy_window(HWND);
static BOOL WINAPI unregister_class(LPCWSTR, HINSTANCE);
static HDESK WINAPI input_desktop(DWORD, BOOL, ACCESS_MASK);
static ULONGLONG WINAPI ticks(void);
static HRESULT WINAPI composition_enabled(BOOL*);
static HRESULT WINAPI composition_flush(void);

#define WARP_OUTPUT_UTILITIES_INCLUDED
#define D3D11CreateDeviceAndSwapChain create_swap_device
#define RegisterClassExW register_class
#define AdjustWindowRectEx adjust_window
#define CreateWindowExW create_window
#define ShowWindow show_window
#define GetClientRect client_rect
#define IsWindowVisible visible_window
#define PeekMessageW peek_message
#define TranslateMessage translate_message
#define DispatchMessageW dispatch_message
#define PostQuitMessage post_quit
#define BeginPaint begin_paint
#define EndPaint end_paint
#define DestroyWindow destroy_window
#define UnregisterClassW unregister_class
#define OpenInputDesktop input_desktop
#define GetTickCount64 ticks
#define DwmIsCompositionEnabled composition_enabled
#define DwmFlush composition_flush
#define LoadLibraryExW load_compiler
#define GetProcAddress find_compiler
#define FreeLibrary unload_compiler
#pragma warning(push)
#pragma warning(disable: 4459)
#include "native-dx11-warp-presentation.c"
#pragma warning(pop)
#undef D3D11CreateDeviceAndSwapChain
#undef RegisterClassExW
#undef AdjustWindowRectEx
#undef CreateWindowExW
#undef ShowWindow
#undef GetClientRect
#undef IsWindowVisible
#undef PeekMessageW
#undef TranslateMessage
#undef DispatchMessageW
#undef PostQuitMessage
#undef BeginPaint
#undef EndPaint
#undef DestroyWindow
#undef UnregisterClassW
#undef OpenInputDesktop
#undef GetTickCount64
#undef DwmIsCompositionEnabled
#undef DwmFlush
#undef LoadLibraryExW
#undef GetProcAddress
#undef FreeLibrary

static void check_window_thread(void)
{
    CHECK(creatingThread == GetCurrentThreadId());
}

static LRESULT CALLBACK proxy_wndproc(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
{
    LRESULT result;
    check_window_thread();
    CHECK(helperWndproc != NULL);
    if (message == WM_NCCREATE)
    {
        CHECK(((CREATESTRUCTW*)lparam)->lpCreateParams == callbackState);
    }
    if (message == WM_NCDESTROY)
    {
        CHECK(GetWindowLongPtrW(window, GWLP_USERDATA) == (LONG_PTR)callbackState);
        CHECK(((warp_present_window*)callbackState)->destroying
            || presentationFailure == window_failure);
        record_event('N');
    }
    if (message == WM_DESTROY)
    {
        CHECK(!inDestroy);
        inDestroy = TRUE;
    }
    result = helperWndproc(window, message, wparam, lparam);
    if (message == WM_DESTROY)
    {
        CHECK(postedQuitCalls == 1);
        inDestroy = FALSE;
    }
    if (message == WM_CLOSE)
    {
        CHECK(result == 0 && ++closeCalls == 1 && destroyCalls == 0);
        CHECK(((warp_present_window*)callbackState)->cancelled);
        CHECK(!((warp_present_window*)callbackState)->destroying
            && !((warp_present_window*)callbackState)->destroyed && IsWindow(window));
    }
    if (message == WM_NCDESTROY)
    {
        CHECK(((warp_present_window*)callbackState)->destroyed);
    }
    return result;
}

static ATOM WINAPI register_class(const WNDCLASSEXW* description)
{
    WNDCLASSEXW proxy = *description;
    UINT expectedCalls = presentationFailure == unregister_failure && registerCalls == 1 ? 2u : 1u;
    CHECK(++registerCalls == expectedCalls && description->cbSize == sizeof(*description));
    CHECK(description->style == 0 && description->cbClsExtra == 0 && description->cbWndExtra == 0);
    CHECK(description->hbrBackground == NULL && description->lpszMenuName == NULL);
    creatingThread = GetCurrentThreadId();
    helperWndproc = description->lpfnWndProc;
    proxy.lpfnWndProc = proxy_wndproc;
    record_event('r');
    if (presentationFailure == register_failure)
    {
        SetLastError(ERROR_ACCESS_DENIED);
        return 0;
    }
    return RegisterClassExW(&proxy);
}

static BOOL WINAPI adjust_window(LPRECT bounds, DWORD style, BOOL menu, DWORD extended)
{
    check_window_thread();
    CHECK(bounds->right == WARP_OUTPUT_WIDTH && bounds->bottom == WARP_OUTPUT_HEIGHT);
    CHECK(style == (WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU) && !menu && extended == 0);
    if (presentationFailure == adjust_failure)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }
    return AdjustWindowRectEx(bounds, style, menu, extended);
}

static HWND WINAPI create_window(DWORD extended, LPCWSTR className, LPCWSTR title, DWORD style,
    int x, int y, int width, int height, HWND parent, HMENU menu, HINSTANCE instance, LPVOID parameter)
{
    check_window_thread();
    CHECK(++windowCalls == 1 && extended == 0 && parent == NULL && menu == NULL);
    CHECK(style == (WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU) && !(style & WS_VISIBLE));
    CHECK(x == CW_USEDEFAULT && y == CW_USEDEFAULT && width > WARP_OUTPUT_WIDTH && height > WARP_OUTPUT_HEIGHT);
    callbackState = parameter;
    CHECK(((warp_present_window*)parameter)->thread == creatingThread);
    record_event('n');
    if (presentationFailure == window_failure)
    {
        SetLastError(ERROR_ACCESS_DENIED);
        return NULL;
    }
    ownedWindow = CreateWindowExW(extended, className, title, style, x, y, width, height,
        parent, menu, instance, parameter);
    CHECK(ownedWindow != NULL);
    return ownedWindow;
}

static BOOL WINAPI show_window(HWND window, int command)
{
    BOOL result;
    check_window_thread();
    CHECK(window == ownedWindow && command == SW_SHOWNORMAL && ++showCalls == 1);
    record_event('h');
    result = ShowWindow(window, command);
    CHECK(IsWindowVisible(window) && !IsIconic(window));
    CHECK(InvalidateRect(window, NULL, FALSE));
    return result;
}

static BOOL WINAPI client_rect(HWND window, LPRECT rectangle)
{
    BOOL result = GetClientRect(window, rectangle);
    CHECK(window == ownedWindow);
    if (presentationFailure == client_failure)
    {
        SetLastError(ERROR_INVALID_WINDOW_HANDLE);
        return FALSE;
    }
    if (presentationFailure == empty_client)
    {
        rectangle->right = rectangle->left;
    }
    return result;
}

static BOOL WINAPI visible_window(HWND window)
{
    CHECK(window == ownedWindow);
    return presentationFailure == hidden_initial ? FALSE : IsWindowVisible(window);
}

static BOOL WINAPI peek_message(LPMSG message, HWND window, UINT first, UINT last, UINT remove)
{
    BOOL found;
    check_window_thread();
    CHECK(window == NULL && first == 0 && last == 0 && remove == PM_REMOVE);
    CHECK(closeCalls == 0 && consumedQuitCalls == 0);
    if (!injected)
    {
        UINT injectedMessage = 0;
        WPARAM wparam = 0;
        LPARAM lparam = 0;
        if (presentationFailure == close_initial || presentationFailure == close_twice
            || (presentationFailure == close_before_present && unmapCalls == 1)
            || (presentationFailure == close_after_present && presentCalls == 1)
            || (presentationFailure == close_after_composition && compositionCalls == 1))
        {
            injectedMessage = WM_CLOSE;
        }
        else if (presentationFailure == quit_initial
            || (presentationFailure == quit_before_present && unmapCalls == 1)
            || (presentationFailure == quit_after_present && presentCalls == 1)
            || (presentationFailure == quit_after_composition && compositionCalls == 1))
        {
            PostQuitMessage(113);
            injected = TRUE;
        }
        else if (presentationFailure == minimize_initial
            || (presentationFailure == minimize_before_present && unmapCalls == 1))
        {
            injectedMessage = WM_SIZE;
            wparam = SIZE_MINIMIZED;
        }
        else if (presentationFailure == resize_initial
            || (presentationFailure == resize_before_present && unmapCalls == 1))
        {
            injectedMessage = WM_SIZE;
            lparam = MAKELPARAM(WARP_OUTPUT_WIDTH + 1, WARP_OUTPUT_HEIGHT);
        }
        if (injectedMessage != 0)
        {
            CHECK(PostMessageW(ownedWindow, injectedMessage, wparam, lparam));
            if (presentationFailure == close_twice)
            {
                CHECK(PostMessageW(ownedWindow, WM_CLOSE, 0, 0));
            }
            injected = TRUE;
        }
    }
    if (presentationFailure == timeout_messages)
    {
        memset(message, 0, sizeof(*message));
        message->hwnd = ownedWindow;
        message->message = WM_NULL;
        return TRUE;
    }
    found = PeekMessageW(message, window, first, last, remove);
    if (found && message->message == WM_QUIT)
    {
        CHECK(message->hwnd == NULL && message->wParam == 113 && ++consumedQuitCalls == 1);
    }
    return found;
}

static BOOL WINAPI translate_message(const MSG* message)
{
    check_window_thread();
    CHECK(message->message != WM_QUIT && consumedQuitCalls == 0 && closeCalls == 0);
    CHECK(++translateCalls == dispatchCalls + 1);
    translatedMessage = message;
    return TranslateMessage(message);
}

static LRESULT WINAPI dispatch_message(const MSG* message)
{
    check_window_thread();
    CHECK(message->message != WM_QUIT && consumedQuitCalls == 0 && closeCalls == 0);
    CHECK(++dispatchCalls == translateCalls && message == translatedMessage);
    return DispatchMessageW(message);
}

static void WINAPI post_quit(int exitCode)
{
    check_window_thread();
    CHECK(inDestroy && destroyCalls == 1 && exitCode == 0 && ++postedQuitCalls == 1);
    CHECK(((warp_present_window*)callbackState)->destroying
        && !((warp_present_window*)callbackState)->destroyed);
    CHECK(swap.real == NULL && associatedFactory.real == NULL);
    check_released();
    PostQuitMessage(exitCode);
}

static HDC WINAPI begin_paint(HWND window, LPPAINTSTRUCT paint)
{
    HDC dc;
    check_window_thread();
    CHECK(window == ownedWindow && beginPaintCalls++ == endPaintCalls);
    if (presentationFailure == paint_begin_failure)
    {
        SetLastError(ERROR_ACCESS_DENIED);
        return NULL;
    }
    dc = BeginPaint(window, paint);
    CHECK(dc != NULL);
    return dc;
}

static BOOL WINAPI end_paint(HWND window, const PAINTSTRUCT* paint)
{
    check_window_thread();
    CHECK(window == ownedWindow && ++endPaintCalls == beginPaintCalls);
    CHECK(EndPaint(window, paint));
    if (presentationFailure == paint_end_failure)
    {
        SetLastError(ERROR_ACCESS_DENIED);
        return FALSE;
    }
    return TRUE;
}

static BOOL WINAPI destroy_window(HWND window)
{
    check_window_thread();
    CHECK(window == ownedWindow && ++destroyCalls == 1);
    CHECK(swap.real == NULL && associatedFactory.real == NULL && loadedCompiler == NULL);
    check_released();
    CHECK(((warp_present_window*)callbackState)->destroying);
    record_event('x');
    if (presentationFailure == destroy_failure)
    {
        SetLastError(ERROR_ACCESS_DENIED);
        return FALSE;
    }
    destroyedWindow = window;
    CHECK(DestroyWindow(window));
    CHECK(((warp_present_window*)callbackState)->destroyed && !IsWindow(window));
    ownedWindow = NULL;
    return TRUE;
}

static BOOL WINAPI unregister_class(LPCWSTR name, HINSTANCE instance)
{
    check_window_thread();
    CHECK(++unregisterCalls == 1
        && (ownedWindow == NULL || presentationFailure == destroy_failure));
    CHECK(windowCalls == 0 || presentationFailure == window_failure || destroyCalls == 1);
    record_event('z');
    if (presentationFailure == destroy_failure)
    {
        CHECK(IsWindow(ownedWindow));
        SetLastError(ERROR_ACCESS_DENIED);
        return FALSE;
    }
    if (presentationFailure == unregister_failure)
    {
        SetLastError(ERROR_ACCESS_DENIED);
        return FALSE;
    }
    CHECK(UnregisterClassW(name, instance));
    return TRUE;
}

static HDESK WINAPI input_desktop(DWORD flags, BOOL inherit, ACCESS_MASK access)
{
    CHECK(flags == 0 && !inherit && access == DESKTOP_READOBJECTS);
    if (presentationFailure == desktop_unavailable
        || (presentationFailure == desktop_after_present && presentCalls == 1))
    {
        SetLastError(ERROR_ACCESS_DENIED);
        return NULL;
    }
    return OpenInputDesktop(flags, inherit, access);
}

static ULONGLONG WINAPI ticks(void)
{
    ULONGLONG now = GetTickCount64();
    if (((presentationFailure == timeout_initial || presentationFailure == timeout_messages)
            && showCalls == 1)
        || (presentationFailure == timeout_before_present && unmapCalls == 1))
    {
        return now + 30000;
    }
    return now;
}

static HRESULT WINAPI composition_enabled(BOOL* enabled)
{
    HRESULT result = DwmIsCompositionEnabled(enabled);
    CHECK(result == S_OK && *enabled);
    if (presentationFailure == composition_unavailable)
    {
        *enabled = FALSE;
    }
    return result;
}

static HRESULT WINAPI composition_flush(void)
{
    check_window_thread();
    CHECK(presentCalls == 1 && unmapCalls == 1 && ++compositionCalls == 1);
    CHECK(IsWindowVisible(ownedWindow) && !IsIconic(ownedWindow));
    record_event('W');
    if (presentationFailure == composition_failure)
    {
        return E_INVALIDARG;
    }
    if (presentationFailure == composition_positive)
    {
        return S_FALSE;
    }
    return DwmFlush();
}

static ULONG STDMETHODCALLTYPE release_factory(IDXGIFactory* object)
{
    ULONG references;
    CHECK(object == &associatedFactory.object && associatedFactory.real != NULL);
    CHECK(context.real == NULL && target.real == NULL && targetView.real == NULL && swap.real != NULL);
    CHECK(++factoryReleaseCalls == 1);
    record_event('f');
    references = associatedFactory.real->lpVtbl->Release(associatedFactory.real);
    associatedFactory.real = NULL;
    return references;
}

static HRESULT STDMETHODCALLTYPE associate_window(IDXGIFactory* object, HWND window, UINT flags)
{
    CHECK(object == &associatedFactory.object && associatedFactory.real != NULL);
    CHECK(window == ownedWindow && flags == DXGI_MWA_NO_ALT_ENTER && ++associationCalls == 1);
    CHECK(parentCalls == 1 && creationCalls == 0);
    record_event('a');
    if (presentationFailure == association_failure)
    {
        return E_INVALIDARG;
    }
    return associatedFactory.real->lpVtbl->MakeWindowAssociation(associatedFactory.real, window, flags);
}

static HRESULT STDMETHODCALLTYPE get_parent(IDXGISwapChain* object, REFIID iid, void** output)
{
    HRESULT result;
    CHECK(object == &swap.object && swap.real != NULL && *output == NULL);
    CHECK(IsEqualIID(iid, &IID_IDXGIFactory) && ++parentCalls == 1 && creationCalls == 0);
    record_event('g');
    if (presentationFailure == parent_missing)
    {
        return S_OK;
    }
    result = swap.real->lpVtbl->GetParent(swap.real, iid, (void**)&associatedFactory.real);
    CHECK(result == S_OK && associatedFactory.real != NULL);
    associatedFactory.vtable = *associatedFactory.real->lpVtbl;
    associatedFactory.vtable.Release = release_factory;
    associatedFactory.vtable.MakeWindowAssociation = associate_window;
    associatedFactory.object.lpVtbl = &associatedFactory.vtable;
    *output = &associatedFactory.object;
    return presentationFailure == parent_partial ? E_INVALIDARG : result;
}

static void STDMETHODCALLTYPE backbuffer_description(ID3D11Texture2D* object,
    D3D11_TEXTURE2D_DESC* description)
{
    CHECK(object == &target.object && target.real != NULL && getBufferCalls == 1);
    record_event('q');
    target.real->lpVtbl->GetDesc(target.real, description);
    if (presentationFailure == bad_backbuffer)
    {
        description->SampleDesc.Quality = 1;
    }
}

static HRESULT STDMETHODCALLTYPE get_buffer(IDXGISwapChain* object, UINT index, REFIID iid, void** output)
{
    ID3D11Texture2D* real = NULL;
    HRESULT result;
    CHECK(object == &swap.object && index == 0 && *output == NULL);
    CHECK(IsEqualIID(iid, &IID_ID3D11Texture2D) && ++getBufferCalls == 1 && associationCalls == 1);
    if (begin_create(&device.object, 5, 'T'))
    {
        return S_OK;
    }
    result = swap.real->lpVtbl->GetBuffer(swap.real, index, iid, (void**)&real);
    CHECK(result == S_OK && real != NULL);
    *output = wrap_target(real);
    target.vtable.GetDesc = backbuffer_description;
    return finish_create(5, result);
}

static HRESULT STDMETHODCALLTYPE present_frame(IDXGISwapChain* object, UINT interval, UINT flags)
{
    check_window_thread();
    CHECK(object == &swap.object && swap.real != NULL && interval == 1 && flags == 0);
    CHECK(unmapCalls == 1 && !realMapped && clearStateCalls == 0 && ++presentCalls == 1);
    CHECK(IsWindowVisible(ownedWindow) && !IsIconic(ownedWindow));
    record_event('F');
    switch (presentationFailure)
    {
        case present_failure: return E_INVALIDARG;
        case present_occluded: return DXGI_STATUS_OCCLUDED;
        case present_positive: return S_FALSE;
        case present_removed:
        case present_removed_without_reason: return DXGI_ERROR_DEVICE_REMOVED;
        case present_reset: return DXGI_ERROR_DEVICE_RESET;
        case destroy_failure:
        case unregister_failure: return S_OK;
        default:
        {
            HRESULT result = swap.real->lpVtbl->Present(swap.real, interval, flags);
            if (result != S_OK)
            {
                DWORD cloaked = 0;
                RECT bounds = { 0 };
                HRESULT cloakResult = DwmGetWindowAttribute(ownedWindow, DWMWA_CLOAKED,
                    &cloaked, sizeof(cloaked));
                BOOL hasBounds = GetWindowRect(ownedWindow, &bounds);
                fprintf(stderr, "real presentation: status=0x%08lX; cloak-status=0x%08lX; "
                    "cloaked=%lu; bounds-valid=%d; bounds=(%ld,%ld,%ld,%ld)\n",
                    (unsigned long)result, (unsigned long)cloakResult, cloaked,
                    hasBounds, bounds.left, bounds.top, bounds.right, bounds.bottom);
            }
            return result;
        }
    }
}

static HRESULT STDMETHODCALLTYPE removed_reason(ID3D11Device* object)
{
    CHECK(object == &device.object && device.real != NULL && ++reasonCalls == 1);
    CHECK(clearStateCalls == 0);
    record_event('!');
    CHECK(device.real->lpVtbl->GetDeviceRemovedReason(device.real) == S_OK);
    return presentationFailure == present_removed_without_reason ? S_OK : DXGI_ERROR_DRIVER_INTERNAL_ERROR;
}

static HRESULT STDMETHODCALLTYPE presentation_map(ID3D11DeviceContext* object, ID3D11Resource* resource,
    UINT subresource, D3D11_MAP type, UINT flags, D3D11_MAPPED_SUBRESOURCE* output)
{
    HRESULT result = map_resource(object, resource, subresource, type, flags, output);
    return presentationFailure == map_removed ? DXGI_ERROR_DEVICE_REMOVED : result;
}

static ULONG STDMETHODCALLTYPE release_swap(IDXGISwapChain* object)
{
    ULONG references;
    CHECK(object == &swap.object && swap.real != NULL && ++swapReleaseCalls == 1);
    CHECK(context.real == NULL && associatedFactory.real == NULL);
    CHECK(target.real == NULL && targetView.real == NULL && staging.real == NULL);
    record_event('s');
    references = swap.real->lpVtbl->Release(swap.real);
    CHECK(references == 0);
    swap.real = NULL;
    return references;
}

static ULONG STDMETHODCALLTYPE presentation_release_device(ID3D11Device* object)
{
    CHECK(swap.real == NULL && associatedFactory.real == NULL);
    record_event('e');
    return release_device(object);
}

static HRESULT WINAPI create_swap_device(IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver,
    HMODULE software, UINT flags, const D3D_FEATURE_LEVEL* levels, UINT count, UINT version,
    const DXGI_SWAP_CHAIN_DESC* description, IDXGISwapChain** swapOutput, ID3D11Device** deviceOutput,
    D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** contextOutput)
{
    HRESULT result;
    RECT client;
    check_window_thread();
    CHECK(adapter == NULL && driver == D3D_DRIVER_TYPE_WARP && software == NULL && flags == 0);
    CHECK(levels != NULL && count == 1 && levels[0] == D3D_FEATURE_LEVEL_11_0);
    CHECK(version == D3D11_SDK_VERSION && *swapOutput == NULL && *deviceOutput == NULL);
    CHECK(*contextOutput == NULL && *selected == 0 && compileCalls == 2 && ++deviceCalls == 1);
    CHECK(description->OutputWindow == ownedWindow && GetClientRect(ownedWindow, &client));
    CHECK(description->BufferDesc.Width == (UINT)(client.right - client.left));
    CHECK(description->BufferDesc.Height == (UINT)(client.bottom - client.top));
    CHECK(description->BufferDesc.Width == WARP_OUTPUT_WIDTH && description->BufferDesc.Height == WARP_OUTPUT_HEIGHT);
    CHECK(description->BufferDesc.RefreshRate.Numerator == 0 && description->BufferDesc.RefreshRate.Denominator == 0);
    CHECK(description->BufferDesc.Format == DXGI_FORMAT_R8G8B8A8_UNORM);
    CHECK(description->BufferDesc.ScanlineOrdering == 0 && description->BufferDesc.Scaling == 0);
    CHECK(description->SampleDesc.Count == 1 && description->SampleDesc.Quality == 0);
    CHECK(description->BufferUsage == DXGI_USAGE_RENDER_TARGET_OUTPUT && description->BufferCount == 1);
    CHECK(description->Windowed == TRUE && description->SwapEffect == DXGI_SWAP_EFFECT_DISCARD);
    CHECK(description->Flags == 0);
    record_event('d');
    result = D3D11CreateDeviceAndSwapChain(adapter, driver, software, flags, levels, count, version,
        description, &swap.real, &device.real, selected, &context.real);
    CHECK(result == S_OK && swap.real != NULL && device.real != NULL && context.real != NULL);
    CHECK(*selected == D3D_FEATURE_LEVEL_11_0);
    swap.vtable = *swap.real->lpVtbl;
    swap.vtable.GetParent = get_parent;
    swap.vtable.GetBuffer = get_buffer;
    swap.vtable.Present = present_frame;
    swap.vtable.Release = release_swap;
    swap.object.lpVtbl = &swap.vtable;
    device.vtable = *device.real->lpVtbl;
    device.vtable.CreateVertexShader = create_vertex_shader;
    device.vtable.CreateInputLayout = create_input_layout;
    device.vtable.CreateBuffer = create_buffer;
    device.vtable.CreatePixelShader = create_pixel_shader;
    device.vtable.CreateTexture2D = create_texture;
    device.vtable.CreateRenderTargetView = create_target_view;
    device.vtable.GetDeviceRemovedReason = removed_reason;
    device.vtable.Release = presentation_release_device;
    device.object.lpVtbl = &device.vtable;
    context.vtable = *context.real->lpVtbl;
    context.vtable.ClearRenderTargetView = clear_target;
    context.vtable.OMSetRenderTargets = set_targets;
    context.vtable.PSSetShader = set_pixel_shader;
    context.vtable.VSSetShader = set_vertex_shader;
    context.vtable.IASetInputLayout = set_input_layout;
    context.vtable.IASetVertexBuffers = set_vertex_buffers;
    context.vtable.IASetPrimitiveTopology = set_topology;
    context.vtable.RSSetViewports = set_viewports;
    context.vtable.Draw = draw;
    context.vtable.CopyResource = copy_resource;
    context.vtable.Map = presentation_map;
    context.vtable.Unmap = unmap_resource;
    context.vtable.ClearState = clear_context;
    context.vtable.Release = release_context;
    context.object.lpVtbl = &context.vtable;
    *swapOutput = &swap.object;
    *deviceOutput = &device.object;
    *contextOutput = &context.object;
    if (presentationFailure == missing_outputs || presentationFailure == failed_outputs)
    {
        if (outputMask & 1)
        {
            context.real->lpVtbl->Release(context.real);
            context.real = NULL;
            *contextOutput = NULL;
        }
        if (outputMask & 2)
        {
            swap.real->lpVtbl->Release(swap.real);
            swap.real = NULL;
            *swapOutput = NULL;
        }
        if (outputMask & 4)
        {
            device.real->lpVtbl->Release(device.real);
            device.real = NULL;
            *deviceOutput = NULL;
        }
        return presentationFailure == failed_outputs ? E_INVALIDARG : S_OK;
    }
    if (failure == device_wrong_feature_level)
    {
        *selected = D3D_FEATURE_LEVEL_10_0;
    }
    return result;
}

static void run_presentation_case(const char* name, presentation_failure kind, failure_kind inherited,
    UINT stage, BOOL unloadFailure, HRESULT expectedResult, const char* expectedEvents)
{
    HRESULT result;
    MSG message;
    UINT cleanupQuitCalls = 0;
    reset_case();
    CHECK(swap.real == NULL && associatedFactory.real == NULL && ownedWindow == NULL);
    presentationFailure = kind;
    outputMask = stage;
    failure = inherited;
    failureStage = stage;
    failUnload = unloadFailure;
    registerCalls = windowCalls = showCalls = destroyCalls = unregisterCalls = 0;
    parentCalls = associationCalls = getBufferCalls = presentCalls = compositionCalls = reasonCalls = 0;
    swapReleaseCalls = factoryReleaseCalls = beginPaintCalls = endPaintCalls = dispatchCalls = 0;
    translateCalls = closeCalls = consumedQuitCalls = postedQuitCalls = 0;
    inDestroy = FALSE;
    translatedMessage = NULL;
    helperWndproc = NULL;
    callbackState = NULL;
    creatingThread = 0;
    destroyedWindow = NULL;
    injected = FALSE;
    result = NativeFfiPresentTriangleWarp();
    if (strcmp(events, expectedEvents) != 0 || result != expectedResult)
    {
        fprintf(stderr, "case %s[%u]: result=0x%08lX expected=0x%08lX events=%s expected=%s\n",
            name, stage, (unsigned long)result, (unsigned long)expectedResult, events, expectedEvents);
    }
    CHECK(result == expectedResult && strcmp(events, expectedEvents) == 0);
    if (kind == destroy_failure)
    {
        HRESULT retry;
        CHECK(ownedWindow != NULL && IsWindow(ownedWindow)
            && ((warp_present_window*)callbackState)->destroying
            && !((warp_present_window*)callbackState)->destroyed && postedQuitCalls == 0);
        retry = NativeFfiPresentTriangleWarp();
        CHECK(retry == E_UNEXPECTED && ownedWindow != NULL && IsWindow(ownedWindow)
            && destroyCalls == 1 && unregisterCalls == 1);
        destroyedWindow = ownedWindow;
        CHECK(DestroyWindow(ownedWindow));
        CHECK(((warp_present_window*)callbackState)->destroyed && !IsWindow(destroyedWindow));
        ownedWindow = NULL;
        CHECK(UnregisterClassW(L"Terrum.Provisional.WarpPresentation", GetModuleHandleW(NULL)));
        printf("destroy-failure state verified: live callback state rejected reentry; harness cleanup succeeded\n");
    }
    else if (kind == unregister_failure)
    {
        HRESULT retry;
        CHECK(ownedWindow == NULL && destroyedWindow != NULL && !IsWindow(destroyedWindow)
            && ((warp_present_window*)callbackState)->destroyed
            && postedQuitCalls == 1 && unregisterCalls == 1);
        retry = NativeFfiPresentTriangleWarp();
        CHECK(retry == HRESULT_FROM_WIN32(ERROR_CLASS_ALREADY_EXISTS)
            && registerCalls == 2 && unregisterCalls == 1 && ownedWindow == NULL);
        CHECK(UnregisterClassW(L"Terrum.Provisional.WarpPresentation", GetModuleHandleW(NULL)));
        printf("unregister-failure state verified: registered class rejected reentry; harness cleanup succeeded\n");
    }
    /* Drain the harness thread's SDK messages and final quit between invocations. */
    while (PeekMessageW(&message, NULL, 0, 0, PM_REMOVE))
    {
        if (message.message == WM_QUIT)
        {
            CHECK(message.hwnd == NULL && message.wParam == 0);
            cleanupQuitCalls++;
        }
        else if (destroyedWindow != NULL && message.hwnd == destroyedWindow)
        {
            CHECK(!IsWindow(destroyedWindow));
        }
        else
        {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    check_released();
    CHECK(swap.real == NULL && associatedFactory.real == NULL && ownedWindow == NULL && loadedCompiler == NULL);
    CHECK(beginPaintCalls == endPaintCalls + (kind == paint_begin_failure ? 1u : 0u));
    CHECK(mapCalls <= 1 && unmapCalls == (failure == map_failure ? 0u : mapCalls));
    CHECK(presentCalls <= 1 && compositionCalls <= presentCalls);
    CHECK(destroyCalls == (showCalls || kind == client_failure || kind == empty_client ? 1u : 0u));
    CHECK(unregisterCalls == (registerCalls && kind != register_failure ? 1u : 0u));
    CHECK(translateCalls == dispatchCalls && cleanupQuitCalls == destroyCalls
        && postedQuitCalls == destroyCalls && !inDestroy);
    CHECK(closeCalls == (kind == close_initial || kind == close_twice || kind == close_before_present
        || kind == close_after_present || kind == close_after_composition ? 1u : 0u));
    CHECK(consumedQuitCalls == (kind == quit_initial || kind == quit_before_present
        || kind == quit_after_present || kind == quit_after_composition ? 1u : 0u));
    CHECK(reasonCalls == (kind == present_removed || kind == present_reset
        || kind == present_removed_without_reason || kind == map_removed ? 1u : 0u));
    printf("case %s[%u]: 0x%08lX; events=%s; maps=%u; unmaps=%u; present=%u; composition=%u; "
        "close=%u; consumed-quit=%u; posted-quit=%u\n",
        name, stage, (unsigned long)result, expectedEvents, mapCalls, unmapCalls, presentCalls, compositionCalls,
        closeCalls, consumedQuitCalls, postedQuitCalls);
    fflush(stdout);
    caseCount++;
}

#define COMPLETE "rnhvpdgaVLBPTqRScopviatwDXMUFWCfsexNz"
#define DESTROY_FAILED "rnhvpdgaVLBPTqRScopviatwDXMUFWCfsexz"
#define BEFORE_PRESENT "rnhvpdgaVLBPTqRScopviatwDXMUCfsexNz"
#define PRESENT_FAILED "rnhvpdgaVLBPTqRScopviatwDXMUFCfsexNz"
#define REMOVED "rnhvpdgaVLBPTqRScopviatwDXMUF!CfsexNz"

int main(int argc, char** argv)
{
    UINT stage;
    const HRESULT cancelled = HRESULT_FROM_WIN32(ERROR_CANCELLED);
    const HRESULT timedOut = HRESULT_FROM_WIN32(ERROR_TIMEOUT);
    CHECK(argc == 1 || argc == 2);
    if (argc == 2 && strcmp(argv[1], "close-quit") == 0)
    {
        run_presentation_case("early-close", close_initial, no_failure, 0, FALSE, cancelled, "rnhxNz");
        run_presentation_case("early-quit", quit_initial, no_failure, 0, FALSE, cancelled, "rnhxNz");
        run_presentation_case("repeated-close", close_twice, no_failure, 0, FALSE, cancelled, "rnhxNz");
        run_presentation_case("readback-close", close_before_present, no_failure, 0, FALSE, cancelled, BEFORE_PRESENT);
        run_presentation_case("readback-quit", quit_before_present, no_failure, 0, FALSE, cancelled, BEFORE_PRESENT);
        printf("%u close/quit cases verified: unfiltered pump, no quit translation/dispatch, "
            "deferred destruction and cleanup quit\n", caseCount);
        return 0;
    }
    if (argc == 2 && strcmp(argv[1], "destroy-failure") == 0)
    {
        run_presentation_case("destroy-failed", destroy_failure, no_failure, 0, FALSE,
            E_ACCESSDENIED, DESTROY_FAILED);
        return 0;
    }
    if (argc == 2 && strcmp(argv[1], "unregister-failure") == 0)
    {
        run_presentation_case("unregister-failed", unregister_failure, no_failure, 0, FALSE,
            E_ACCESSDENIED, COMPLETE);
        return 0;
    }
    CHECK(argc == 1);
    run_presentation_case("real-presentation", presentation_ok, no_failure, 0, FALSE, S_OK, COMPLETE);
    run_presentation_case("desktop", desktop_unavailable, no_failure, 0, FALSE, E_ACCESSDENIED, "");
    run_presentation_case("no-composition", composition_unavailable, no_failure, 0, FALSE, E_FAIL, "");
    run_presentation_case("register", register_failure, no_failure, 0, FALSE, E_ACCESSDENIED, "r");
    run_presentation_case("adjust", adjust_failure, no_failure, 0, FALSE, E_INVALIDARG, "rz");
    run_presentation_case("create-window", window_failure, no_failure, 0, FALSE, E_ACCESSDENIED, "rnz");
    run_presentation_case("client", client_failure, no_failure, 0, FALSE,
        HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE), "rnxNz");
    run_presentation_case("empty-client", empty_client, no_failure, 0, FALSE, E_UNEXPECTED, "rnxNz");
    for (stage = close_initial; stage <= hidden_initial; stage++)
    {
        run_presentation_case("initial-window-cancel", (presentation_failure)stage, no_failure,
            0, FALSE, cancelled, "rnhxNz");
    }
    run_presentation_case("paint-begin", paint_begin_failure, no_failure, 0, FALSE, E_ACCESSDENIED, "rnhxNz");
    run_presentation_case("paint-end", paint_end_failure, no_failure, 0, FALSE, E_ACCESSDENIED, "rnhxNz");
    run_presentation_case("timeout", timeout_initial, no_failure, 0, FALSE, timedOut, "rnhxNz");
    run_presentation_case("message-timeout", timeout_messages, no_failure, 0, FALSE, timedOut, "rnhxNz");
    run_presentation_case("load", presentation_ok, load_failure, 0, FALSE, (HRESULT)0x8007007E, "rnhxNz");
    run_presentation_case("export", presentation_ok, export_failure, 0, FALSE, (HRESULT)0x8007007F, "rnhxNz");
    for (stage = 0; stage < 2; stage++)
    {
        const char* expected = stage == 0 ? "rnhvxNz" : "rnhvpxNz";
        run_presentation_case("compiler-partial", presentation_ok, compile_distinct_failure,
            stage, FALSE, E_INVALIDARG, expected);
        run_presentation_case("compiler-alias", presentation_ok, compile_alias_failure,
            stage, FALSE, E_INVALIDARG, expected);
        run_presentation_case("compiler-null", presentation_ok, compile_missing_code,
            stage, FALSE, E_UNEXPECTED, expected);
        run_presentation_case("compiler-null-bytes", presentation_ok, compile_null_bytes,
            stage, FALSE, E_UNEXPECTED, expected);
        run_presentation_case("compiler-empty", presentation_ok, compile_empty_code,
            stage, FALSE, E_UNEXPECTED, expected);
        run_presentation_case("compiler-empty-diagnostics", presentation_ok, compile_empty_diagnostics,
            stage, FALSE, E_UNEXPECTED, expected);
        run_presentation_case("compiler-warning", presentation_ok, compile_warning,
            stage, FALSE, S_OK, COMPLETE);
    }
    for (stage = 0; stage < 8; stage++)
    {
        char expected[64] = "rnhvpd";
        if (!(stage & 1)) { strcat_s(expected, sizeof(expected), "C"); }
        if (!(stage & 2)) { strcat_s(expected, sizeof(expected), "s"); }
        if (!(stage & 4)) { strcat_s(expected, sizeof(expected), "e"); }
        strcat_s(expected, sizeof(expected), "xNz");
        run_presentation_case("device-partial", failed_outputs, no_failure, stage, FALSE, E_INVALIDARG, expected);
        if (stage != 0)
        {
            run_presentation_case("device-null", missing_outputs, no_failure, stage, FALSE, E_UNEXPECTED, expected);
        }
    }
    run_presentation_case("feature-level", presentation_ok, device_wrong_feature_level,
        0, FALSE, E_UNEXPECTED, "rnhvpdCsexNz");
    run_presentation_case("parent-partial", parent_partial, no_failure, 0, FALSE, E_INVALIDARG, "rnhvpdgCfsexNz");
    run_presentation_case("parent-null", parent_missing, no_failure, 0, FALSE, E_UNEXPECTED, "rnhvpdgCsexNz");
    run_presentation_case("association", association_failure, no_failure, 0, FALSE, E_INVALIDARG, "rnhvpdgaCfsexNz");
    run_presentation_case("backbuffer-desc", bad_backbuffer, no_failure, 0, FALSE, E_UNEXPECTED,
        "rnhvpdgaVLBPTqCfsexNz");
    for (stage = 1; stage <= 7; stage++)
    {
        char expected[64] = "rnhvpdga";
        const char* creates = "VLBPT";
        strncat_s(expected, sizeof(expected), creates, stage < 5 ? stage : 5);
        if (stage >= 6) { strcat_s(expected, sizeof(expected), stage == 6 ? "qR" : "qRS"); }
        strcat_s(expected, sizeof(expected), "CfsexNz");
        run_presentation_case("resource-partial", presentation_ok, create_partial_failure,
            stage, FALSE, E_INVALIDARG, expected);
        if (stage == 5) { strcpy_s(expected, sizeof(expected), "rnhvpdgaVLBPTCfsexNz"); }
        run_presentation_case("resource-null", presentation_ok, create_missing_output,
            stage, FALSE, E_UNEXPECTED, expected);
    }
    run_presentation_case("map-failed", presentation_ok, map_failure, 0, FALSE, E_INVALIDARG,
        "rnhvpdgaVLBPTqRScopviatwDXMCfsexNz");
    run_presentation_case("map-removed", map_removed, map_failure, 0, FALSE, DXGI_ERROR_DRIVER_INTERNAL_ERROR,
        "rnhvpdgaVLBPTqRScopviatwDXM!CfsexNz");
    run_presentation_case("map-null", presentation_ok, map_null_data, 0, FALSE, E_UNEXPECTED, BEFORE_PRESENT);
    run_presentation_case("pitch-zero", presentation_ok, map_short_pitch, 0, FALSE, E_UNEXPECTED, BEFORE_PRESENT);
    run_presentation_case("pitch-short", presentation_ok, map_short_pitch,
        WARP_OUTPUT_WIDTH * 4 - 1, FALSE, E_UNEXPECTED, BEFORE_PRESENT);
    run_presentation_case("padded-rows", presentation_ok, map_padded_rows, 0, FALSE, S_OK, COMPLETE);
    for (stage = 0; stage < 3; stage++)
    {
        run_presentation_case("rgb-lower", presentation_ok, pixel_rgb_lower_bound, stage, FALSE, S_OK, COMPLETE);
        run_presentation_case("rgb-upper", presentation_ok, pixel_rgb_upper_bound, stage, FALSE, S_OK, COMPLETE);
        run_presentation_case("rgb-low", presentation_ok, pixel_rgb_too_low, stage, FALSE, E_FAIL, BEFORE_PRESENT);
        run_presentation_case("rgb-high", presentation_ok, pixel_rgb_too_high, stage, FALSE, E_FAIL, BEFORE_PRESENT);
    }
    run_presentation_case("alpha", presentation_ok, pixel_alpha_mismatch, 0, FALSE, E_FAIL, BEFORE_PRESENT);
    for (stage = 0; stage < 4; stage++)
    {
        run_presentation_case("outside", presentation_ok, pixel_outside_mismatch, stage, FALSE, E_FAIL, BEFORE_PRESENT);
    }
    run_presentation_case("early-close", close_before_present, no_failure, 0, FALSE, cancelled, BEFORE_PRESENT);
    run_presentation_case("repeated-close", close_twice, no_failure, 0, FALSE, cancelled, "rnhxNz");
    run_presentation_case("early-quit", quit_before_present, no_failure, 0, FALSE, cancelled, BEFORE_PRESENT);
    run_presentation_case("minimized", minimize_before_present, no_failure, 0, FALSE, cancelled, BEFORE_PRESENT);
    run_presentation_case("resized", resize_before_present, no_failure, 0, FALSE, cancelled, BEFORE_PRESENT);
    run_presentation_case("readback-timeout", timeout_before_present, no_failure, 0, FALSE, timedOut, BEFORE_PRESENT);
    run_presentation_case("post-present-close", close_after_present, no_failure, 0, FALSE, cancelled, PRESENT_FAILED);
    run_presentation_case("post-present-quit", quit_after_present, no_failure, 0, FALSE, cancelled, PRESENT_FAILED);
    run_presentation_case("desktop-switch", desktop_after_present, no_failure, 0, FALSE, E_ACCESSDENIED, PRESENT_FAILED);
    run_presentation_case("post-composition-close", close_after_composition, no_failure, 0, FALSE, cancelled, COMPLETE);
    run_presentation_case("post-composition-quit", quit_after_composition, no_failure, 0, FALSE, cancelled, COMPLETE);
    run_presentation_case("present-failed", present_failure, no_failure, 0, FALSE, E_INVALIDARG, PRESENT_FAILED);
    run_presentation_case("occluded", present_occluded, no_failure, 0, FALSE, E_FAIL, PRESENT_FAILED);
    run_presentation_case("present-positive", present_positive, no_failure, 0, FALSE, E_FAIL, PRESENT_FAILED);
    run_presentation_case("device-removed", present_removed, no_failure, 0, FALSE, DXGI_ERROR_DRIVER_INTERNAL_ERROR, REMOVED);
    run_presentation_case("device-reset", present_reset, no_failure, 0, FALSE, DXGI_ERROR_DRIVER_INTERNAL_ERROR, REMOVED);
    run_presentation_case("no-removal-reason", present_removed_without_reason, no_failure, 0, FALSE,
        DXGI_ERROR_DEVICE_REMOVED, REMOVED);
    run_presentation_case("composition-failed", composition_failure, no_failure, 0, FALSE, E_INVALIDARG, COMPLETE);
    run_presentation_case("composition-positive", composition_positive, no_failure, 0, FALSE, E_FAIL, COMPLETE);
    run_presentation_case("unload-failed", presentation_ok, no_failure, 0, TRUE, (HRESULT)0x800700AA, COMPLETE);
    run_presentation_case("first-failure", present_failure, no_failure, 0, TRUE, E_INVALIDARG, PRESENT_FAILED);
    printf("%u presentation cases verified: SDK proxies, actual back-buffer pixels, presentation/composition and teardown\n",
        caseCount);
    return 0;
}
