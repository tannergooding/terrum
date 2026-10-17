#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This test-only composed renderer probe requires Windows x64.
#endif

#define EXPOSURE_TIMER 0x5441
#define EXPOSURE_CLOSE_TIMER 0x5442
#define MINIMIZE_RESTORE_TIMER 0x5443

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "DX11 resize/render probe line %d: %s\n", __LINE__, #condition); ExitProcess(97); } } while (0)

static HWND s_window;
static HWND s_coverWindow;
static ID3D11Device* s_device;
static IDXGISwapChain* s_swap;
static ID3D11DeviceContext* s_context;
static ID3D11Texture2D* s_texture;
static ID3D11RenderTargetView* s_target;
static ID3D11DeviceVtbl *s_deviceOriginal, s_deviceTable;
static IDXGISwapChainVtbl *s_swapOriginal, s_swapTable;
static ID3D11DeviceContextVtbl *s_contextOriginal, s_contextTable;
static ID3D11Texture2DVtbl *s_textureOriginal, s_textureTable;
static ID3D11RenderTargetViewVtbl *s_targetOriginal, s_targetTable;
static char s_report[MAX_PATH];
static UINT s_requestedWidth, s_requestedHeight, s_secondWidth, s_secondHeight, s_width, s_height;
static unsigned s_generation, s_draws, s_presents, s_forwarding, s_owners;
static unsigned s_paintValidations, s_resizeMarkers;
static BOOL s_showHandled, s_offscreenVisible, s_visibleExposure, s_naturalExposure;
static BOOL s_naturalExposurePending, s_paintInvalidated, s_hardware;
static BOOL s_coalesceResize, s_repeatResize, s_roundTripResize;
static BOOL s_observeExtents;
static BOOL s_minimizeRestore, s_minimizeStarted, s_minimizedPhase, s_minimizeNotificationPosted;
static BOOL s_minimizeMarkerDispatched, s_restoreInProgress, s_restoreNotificationPosted;
static BOOL s_restoreMarkerDispatched, s_minimizeRestoreSameSize;
static BOOL s_minimizedIterationObserved, s_restoreTimerArmed, s_offscreenPlacementCaptured;
static BOOL s_restorePlacementPending;
static BOOL s_cleared, s_resizingClient, s_quit;
static WINDOWPLACEMENT s_offscreenPlacement;
static int s_restoreOuterWidth, s_restoreOuterHeight;

static void StartMinimizeRestore(void);

static unsigned ExpectedPresentCount(void)
{
    return (s_visibleExposure || s_minimizeRestore) ? 2u
        : (s_repeatResize ? 4u : (s_roundTripResize ? 2u : 3u));
}

static unsigned CompletedPresentCount(void)
{
    return s_naturalExposure && s_naturalExposurePending ? 1u
        : ExpectedPresentCount();
}

static void Report(const char* format, ...)
{
    FILE* file = NULL;
    va_list arguments;
    CHECK(fopen_s(&file, s_report, "a") == 0 && file);
    va_start(arguments, format);
    CHECK(vfprintf(file, format, arguments) >= 0);
    va_end(arguments);
    CHECK(fputc('\n', file) != EOF);
    CHECK(fclose(file) == 0);
}

static void CheckWindowOffscreen(void)
{
    RECT window;
    int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
    int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
    int right = left + GetSystemMetrics(SM_CXVIRTUALSCREEN);
    int bottom = top + GetSystemMetrics(SM_CYVIRTUALSCREEN);
    CHECK(right > left && bottom > top && GetWindowRect(s_window, &window));
    CHECK(window.right <= left || window.bottom <= top
        || window.left >= right || window.top >= bottom);
}

static void CheckTestWindow(void)
{
    DWORD process = 0;
    CHECK(s_window && IsWindow(s_window));
    if (s_minimizeRestore && s_minimizeStarted)
    {
        CHECK(IsWindowVisible(s_window)
            && (s_minimizedPhase ? IsIconic(s_window) : !IsIconic(s_window))
            && GetForegroundWindow() != s_window);
        if (!s_minimizedPhase)
        {
            CheckWindowOffscreen();
        }
    }
    else if (s_showHandled && (s_offscreenVisible || s_visibleExposure))
    {
        RECT window;
        CHECK(IsWindowVisible(s_window) && GetWindowRect(s_window, &window));
        if (s_offscreenVisible)
        {
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int right = left + GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int bottom = top + GetSystemMetrics(SM_CYVIRTUALSCREEN);
            CHECK(right > left && bottom > top);
            CHECK(window.right <= left || window.bottom <= top
                || window.left >= right || window.top >= bottom);
        }
        else
        {
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int right = left + GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int bottom = top + GetSystemMetrics(SM_CYVIRTUALSCREEN);
            CHECK(right > left && bottom > top);
            CHECK(window.right > left && window.bottom > top
                && window.left < right && window.top < bottom);
        }
    }
    else
    {
        CHECK(!IsWindowVisible(s_window));
    }
    CHECK(GetWindowThreadProcessId(s_window, &process) == GetCurrentThreadId());
    CHECK(process == GetCurrentProcessId());
}

static void CheckFrameWorkAllowed(void)
{
    CHECK(!s_minimizeRestore || !s_minimizedPhase);
}

/* Real implementations can use private/derived slots beyond these public SDK
   tables. Restore all live receivers while forwarding, then reattach observation
   tables only for the next Terrum call. Never count internal calls as source calls. */
static void BeginForward(void)
{
    CHECK(s_forwarding == 0);
    ++s_forwarding;
    if (s_device) { s_device->lpVtbl = s_deviceOriginal; }
    if (s_swap) { s_swap->lpVtbl = s_swapOriginal; }
    if (s_context) { s_context->lpVtbl = s_contextOriginal; }
    if (s_texture) { s_texture->lpVtbl = s_textureOriginal; }
    if (s_target) { s_target->lpVtbl = s_targetOriginal; }
}

static void EndForward(void)
{
    CHECK(s_forwarding == 1);
    --s_forwarding;
    if (s_device) { s_device->lpVtbl = &s_deviceTable; }
    if (s_swap) { s_swap->lpVtbl = &s_swapTable; }
    if (s_context) { s_context->lpVtbl = &s_contextTable; }
    if (s_texture) { s_texture->lpVtbl = &s_textureTable; }
    if (s_target) { s_target->lpVtbl = &s_targetTable; }
}

static ULONG STDMETHODCALLTYPE ReleaseTexture(ID3D11Texture2D* receiver)
{
    ULONG result;
    CheckFrameWorkAllowed();
    CHECK(receiver == s_texture);
    if (s_forwarding)
    {
        return s_textureOriginal->Release(receiver);
    }
    CHECK(s_cleared && !s_target);
    BeginForward();
    result = s_textureOriginal->Release(receiver);
    s_texture = NULL;
    EndForward();
    Report("release-back-buffer");
    if (s_minimizeRestore && s_presents == 1u && !s_minimizeStarted)
    {
        StartMinimizeRestore();
    }
    return result;
}

static ULONG STDMETHODCALLTYPE ReleaseTarget(ID3D11RenderTargetView* receiver)
{
    ULONG result;
    CheckFrameWorkAllowed();
    CHECK(receiver == s_target);
    if (s_forwarding)
    {
        return s_targetOriginal->Release(receiver);
    }
    CHECK(s_cleared && s_texture);
    BeginForward();
    result = s_targetOriginal->Release(receiver);
    s_target = NULL;
    EndForward();
    Report("release-render-target");
    return result;
}

#define RELEASE_OWNER(name, type, object, original, owners, remaining, event) \
static ULONG STDMETHODCALLTYPE name(type* receiver) \
{ \
    ULONG result; \
    CHECK(receiver == object); \
    if (s_forwarding) { return original->Release(receiver); } \
    CheckTestWindow(); \
    CHECK(s_quit && s_presents == CompletedPresentCount() \
        && !s_texture && !s_target && s_cleared && s_owners == owners); \
    BeginForward(); \
    result = original->Release(receiver); \
    object = NULL; \
    EndForward(); \
    s_owners = remaining; \
    Report(event); \
    return result; \
}

RELEASE_OWNER(ReleaseContext, ID3D11DeviceContext, s_context, s_contextOriginal, 7, 3, "release-context")
RELEASE_OWNER(ReleaseSwap, IDXGISwapChain, s_swap, s_swapOriginal, 3, 1, "release-swap-chain")
RELEASE_OWNER(ReleaseDevice, ID3D11Device, s_device, s_deviceOriginal, 1, 0, "release-device")

static void STDMETHODCALLTYPE ClearState(ID3D11DeviceContext* receiver)
{
    CheckFrameWorkAllowed();
    CHECK(receiver == s_context);
    BeginForward();
    s_contextOriginal->ClearState(receiver);
    EndForward();
    s_cleared = TRUE;
    Report("clear-state");
}

static HRESULT STDMETHODCALLTYPE CreateBuffer(ID3D11Device* receiver,
    const D3D11_BUFFER_DESC* description, const D3D11_SUBRESOURCE_DATA* data, ID3D11Buffer** output)
{
    HRESULT result;
    CHECK(receiver == s_device);
    BeginForward();
    result = s_deviceOriginal->CreateBuffer(receiver, description, data, output);
    EndForward();
    return result;
}

static HRESULT STDMETHODCALLTYPE CreateInputLayout(ID3D11Device* receiver,
    const D3D11_INPUT_ELEMENT_DESC* elements, UINT count, const void* code, SIZE_T size,
    ID3D11InputLayout** output)
{
    HRESULT result;
    CHECK(receiver == s_device);
    BeginForward();
    result = s_deviceOriginal->CreateInputLayout(receiver, elements, count, code, size, output);
    EndForward();
    return result;
}

static HRESULT STDMETHODCALLTYPE CreateVertexShader(ID3D11Device* receiver,
    const void* code, SIZE_T size, ID3D11ClassLinkage* linkage, ID3D11VertexShader** output)
{
    HRESULT result;
    CHECK(receiver == s_device);
    BeginForward();
    result = s_deviceOriginal->CreateVertexShader(receiver, code, size, linkage, output);
    EndForward();
    return result;
}

static HRESULT STDMETHODCALLTYPE CreatePixelShader(ID3D11Device* receiver,
    const void* code, SIZE_T size, ID3D11ClassLinkage* linkage, ID3D11PixelShader** output)
{
    HRESULT result;
    CHECK(receiver == s_device);
    BeginForward();
    result = s_deviceOriginal->CreatePixelShader(receiver, code, size, linkage, output);
    EndForward();
    return result;
}

static void STDMETHODCALLTYPE SetRenderTargets(ID3D11DeviceContext* receiver,
    UINT count, ID3D11RenderTargetView* const* targets, ID3D11DepthStencilView* depth)
{
    CheckFrameWorkAllowed();
    CHECK(receiver == s_context && count == 1 && targets && targets[0] == s_target && !depth);
    BeginForward();
    s_contextOriginal->OMSetRenderTargets(receiver, count, targets, depth);
    EndForward();
}

static void STDMETHODCALLTYPE ClearTarget(ID3D11DeviceContext* receiver,
    ID3D11RenderTargetView* target, const FLOAT color[4])
{
    CheckFrameWorkAllowed();
    CHECK(receiver == s_context && target == s_target);
    BeginForward();
    s_contextOriginal->ClearRenderTargetView(receiver, target, color);
    EndForward();
}

static void STDMETHODCALLTYPE SetInputLayout(ID3D11DeviceContext* receiver, ID3D11InputLayout* layout)
{
    CheckFrameWorkAllowed();
    CHECK(receiver == s_context);
    BeginForward();
    s_contextOriginal->IASetInputLayout(receiver, layout);
    EndForward();
}

static void STDMETHODCALLTYPE SetVertexBuffers(ID3D11DeviceContext* receiver, UINT start,
    UINT count, ID3D11Buffer* const* buffers, const UINT* strides, const UINT* offsets)
{
    CheckFrameWorkAllowed();
    CHECK(receiver == s_context);
    BeginForward();
    s_contextOriginal->IASetVertexBuffers(receiver, start, count, buffers, strides, offsets);
    EndForward();
}

static void STDMETHODCALLTYPE SetTopology(ID3D11DeviceContext* receiver, D3D11_PRIMITIVE_TOPOLOGY topology)
{
    CheckFrameWorkAllowed();
    CHECK(receiver == s_context);
    BeginForward();
    s_contextOriginal->IASetPrimitiveTopology(receiver, topology);
    EndForward();
}

static void STDMETHODCALLTYPE SetVertexShader(ID3D11DeviceContext* receiver,
    ID3D11VertexShader* shader, ID3D11ClassInstance* const* instances, UINT count)
{
    CheckFrameWorkAllowed();
    CHECK(receiver == s_context);
    BeginForward();
    s_contextOriginal->VSSetShader(receiver, shader, instances, count);
    EndForward();
}

static void STDMETHODCALLTYPE SetPixelShader(ID3D11DeviceContext* receiver,
    ID3D11PixelShader* shader, ID3D11ClassInstance* const* instances, UINT count)
{
    CheckFrameWorkAllowed();
    CHECK(receiver == s_context);
    BeginForward();
    s_contextOriginal->PSSetShader(receiver, shader, instances, count);
    EndForward();
}

static HRESULT STDMETHODCALLTYPE ResizeBuffers(IDXGISwapChain* receiver,
    UINT count, UINT width, UINT height, DXGI_FORMAT format, UINT flags)
{
    RECT rectangle;
    HRESULT result;
    CheckFrameWorkAllowed();
    CHECK(!s_minimizeRestoreSameSize || s_generation == 0);
    CHECK(receiver == s_swap && s_showHandled && s_cleared && !s_texture && !s_target);
    CheckTestWindow();
    CHECK(GetClientRect(s_window, &rectangle));
    CHECK(rectangle.left == 0 && rectangle.top == 0 && rectangle.right > 0 && rectangle.bottom > 0);
    CHECK(!count && width == (UINT)rectangle.right && height == (UINT)rectangle.bottom
        && format == DXGI_FORMAT_UNKNOWN && !flags
        && s_generation < (s_repeatResize ? 3u : (s_roundTripResize ? 1u : 2u)));
    if (s_generation == 1)
    {
        if (s_minimizeRestore)
        {
            CHECK(s_presents == 1 && s_resizeMarkers == 0
                && s_minimizeNotificationPosted && s_minimizeMarkerDispatched
                && s_restoreNotificationPosted && s_restoreMarkerDispatched
                && width == s_requestedWidth && height == s_requestedHeight
                && (width != s_width || height != s_height));
        }
        else
        {
            CHECK(s_presents == 1 && s_resizeMarkers == (s_coalesceResize ? 2u : 1u)
                && width == s_requestedWidth && height == s_requestedHeight
                && (width != s_width || height != s_height));
        }
    }
    else if (s_generation == 2 && s_repeatResize)
    {
        CHECK(s_presents == 2
            && s_resizeMarkers == (s_coalesceResize ? 4u : 2u)
            && width == s_secondWidth && height == s_secondHeight
            && (width != s_width || height != s_height));
    }
    BeginForward();
    result = s_swapOriginal->ResizeBuffers(receiver, count, width, height, format, flags);
    EndForward();
    CHECK(result == S_OK);
    s_width = width;
    s_height = height;
    ++s_generation;
    Report("resize-buffers:%u:%ux%u:%ld", s_generation, width, height, result);
    return result;
}

static HRESULT STDMETHODCALLTYPE GetBuffer(IDXGISwapChain* receiver,
    UINT index, REFIID iid, void** output)
{
    D3D11_TEXTURE2D_DESC description;
    HRESULT result;
    CheckFrameWorkAllowed();
    CHECK(receiver == s_swap && !index && iid && IsEqualGUID(iid, &IID_ID3D11Texture2D)
        && output && !*output && !s_texture && !s_target && s_generation > 0);
    BeginForward();
    result = s_swapOriginal->GetBuffer(receiver, index, iid, output);
    EndForward();
    CHECK(result == S_OK && *output);
    s_texture = (ID3D11Texture2D*)*output;
    s_texture->lpVtbl->GetDesc(s_texture, &description);
    CHECK(description.Width == s_width && description.Height == s_height
        && description.Format == DXGI_FORMAT_R8G8B8A8_UNORM && description.SampleDesc.Count == 1);
    s_textureOriginal = s_texture->lpVtbl;
    s_textureTable = *s_textureOriginal;
    s_textureTable.Release = ReleaseTexture;
    s_texture->lpVtbl = &s_textureTable;
    Report("back-buffer:%u:%ux%u", s_generation, description.Width, description.Height);
    return result;
}

static HRESULT STDMETHODCALLTYPE CreateTarget(ID3D11Device* receiver,
    ID3D11Resource* resource, const D3D11_RENDER_TARGET_VIEW_DESC* description,
    ID3D11RenderTargetView** output)
{
    ID3D11Resource* targetResource = NULL;
    HRESULT result;
    CheckFrameWorkAllowed();
    CHECK(receiver == s_device && s_texture && resource == (ID3D11Resource*)s_texture
        && !description && output && !*output && !s_target);
    BeginForward();
    result = s_deviceOriginal->CreateRenderTargetView(receiver, resource, description, output);
    CHECK(result == S_OK && *output);
    (*output)->lpVtbl->GetResource(*output, &targetResource);
    CHECK(targetResource == resource);
    targetResource->lpVtbl->Release(targetResource);
    EndForward();
    s_target = *output;
    s_targetOriginal = s_target->lpVtbl;
    s_targetTable = *s_targetOriginal;
    s_targetTable.Release = ReleaseTarget;
    s_target->lpVtbl = &s_targetTable;
    Report("render-target:%u", s_generation);
    return result;
}

static void STDMETHODCALLTYPE SetViewports(ID3D11DeviceContext* receiver,
    UINT count, const D3D11_VIEWPORT* viewports)
{
    CheckFrameWorkAllowed();
    CHECK(receiver == s_context && count == 1 && viewports
        && viewports[0].TopLeftX == 0 && viewports[0].TopLeftY == 0
        && viewports[0].Width == (FLOAT)s_width && viewports[0].Height == (FLOAT)s_height
        && viewports[0].MinDepth == 0 && viewports[0].MaxDepth == 1);
    BeginForward();
    s_contextOriginal->RSSetViewports(receiver, count, viewports);
    EndForward();
    Report("viewport:%u:%ux%u", s_generation, s_width, s_height);
}

static void STDMETHODCALLTYPE Draw(ID3D11DeviceContext* receiver, UINT count, UINT start)
{
    unsigned expectedGeneration = s_draws == 0u ? 1u
        : (s_repeatResize && s_draws >= 2u ? 3u
            : (s_visibleExposure || s_roundTripResize ? 1u : 2u));
    CheckFrameWorkAllowed();
    if (s_minimizeRestore && s_draws > 0u)
    {
        expectedGeneration = s_minimizeRestoreSameSize ? 1u : 2u;
    }
    CHECK(receiver == s_context && s_texture && s_target && count == 3 && start == 0
        && s_draws == s_presents
        && s_draws < ExpectedPresentCount()
        && s_generation == expectedGeneration);
    BeginForward();
    s_contextOriginal->Draw(receiver, count, start);
    EndForward();
    s_cleared = FALSE;
    ++s_draws;
    Report("draw:%u:%u", s_generation, s_draws);
}

static void CheckPixel(const BYTE* actual, const int* expected)
{
    unsigned channel;
    for (channel = 0; channel < 4; ++channel)
    {
        CHECK(abs((int)actual[channel] - expected[channel]) <= (channel == 3 ? 0 : 1));
    }
}

static BOOL PixelMatches(const BYTE* actual, const int* expected)
{
    unsigned channel;
    for (channel = 0; channel < 4; ++channel)
    {
        if (abs((int)actual[channel] - expected[channel]) > (channel == 3 ? 0 : 1))
        {
            return FALSE;
        }
    }
    return TRUE;
}

static void ReadPixels(UINT frame)
{
    static const int clear[4] = { 20, 31, 46, 255 };
    static const int interior[4] = { 26, 204, 230, 255 };
    D3D11_TEXTURE2D_DESC description;
    D3D11_MAPPED_SUBRESOURCE mapped;
    ID3D11Texture2D* staging = NULL;
    unsigned clearPixels = 0;
    unsigned trianglePixels = 0;
    UINT x, y;

    BeginForward();
    s_textureOriginal->GetDesc(s_texture, &description);
    description.Usage = D3D11_USAGE_STAGING;
    description.BindFlags = 0;
    description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    description.MiscFlags = 0;
    CHECK(s_deviceOriginal->CreateTexture2D(s_device, &description, NULL, &staging) == S_OK && staging);
    s_contextOriginal->CopyResource(s_context, (ID3D11Resource*)staging, (ID3D11Resource*)s_texture);
    CHECK(s_contextOriginal->Map(s_context, (ID3D11Resource*)staging, 0, D3D11_MAP_READ, 0, &mapped) == S_OK);
    CHECK(mapped.pData && mapped.RowPitch >= s_width * 4);
    CheckPixel((const BYTE*)mapped.pData, clear);
    CheckPixel((const BYTE*)mapped.pData + (SIZE_T)(s_height / 2) * mapped.RowPitch
        + (SIZE_T)(s_width / 2) * 4, interior);
    /* RowPitch may include padding; classify only the active RGBA8 pixels. */
    for (y = 0; y < s_height; ++y)
    {
        const BYTE* row = (const BYTE*)mapped.pData + (SIZE_T)y * mapped.RowPitch;
        for (x = 0; x < s_width; ++x)
        {
            const BYTE* pixel = row + (SIZE_T)x * 4;
            if (PixelMatches(pixel, clear))
            {
                ++clearPixels;
            }
            else if (PixelMatches(pixel, interior))
            {
                ++trianglePixels;
            }
            else
            {
                CHECK(FALSE);
            }
        }
    }
    CHECK(clearPixels > 0 && trianglePixels > 0);
    s_contextOriginal->Unmap(s_context, (ID3D11Resource*)staging, 0);
    staging->lpVtbl->Release(staging);
    EndForward();
    Report("pixels:%u:%u", s_generation, frame);
}

static void ResizeTestClient(UINT width, UINT height, unsigned expectedMarkers)
{
    RECT rectangle = { 0, 0, (LONG)width, (LONG)height };
    s_resizingClient = TRUE;
    if (s_coalesceResize)
    {
        RECT intermediate = { 0, 0, (LONG)width + 1, (LONG)height + 1 };
        CHECK(AdjustWindowRectEx(&intermediate, (DWORD)GetWindowLongW(s_window, GWL_STYLE),
            FALSE, (DWORD)GetWindowLongW(s_window, GWL_EXSTYLE)));
        CHECK(SetWindowPos(s_window, NULL, 0, 0, intermediate.right - intermediate.left,
            intermediate.bottom - intermediate.top, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE));
    }
    CHECK(AdjustWindowRectEx(&rectangle, (DWORD)GetWindowLongW(s_window, GWL_STYLE),
        FALSE, (DWORD)GetWindowLongW(s_window, GWL_EXSTYLE)));
    CHECK(SetWindowPos(s_window, NULL, 0, 0, rectangle.right - rectangle.left,
        rectangle.bottom - rectangle.top, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE));
    s_resizingClient = FALSE;
    CHECK(s_resizeMarkers == expectedMarkers && GetClientRect(s_window, &rectangle));
    CHECK(rectangle.right == (LONG)width && rectangle.bottom == (LONG)height);
    CheckTestWindow();
    Report("client-resize:%ux%u", width, height);
}

static void ResizeTestClientRoundTrip(void)
{
    RECT intermediate = { 0, 0, (LONG)s_width + 1, (LONG)s_height + 1 };
    RECT original = { 0, 0, (LONG)s_width, (LONG)s_height };
    s_resizingClient = TRUE;
    CHECK(AdjustWindowRectEx(&intermediate, (DWORD)GetWindowLongW(s_window, GWL_STYLE),
        FALSE, (DWORD)GetWindowLongW(s_window, GWL_EXSTYLE)));
    CHECK(SetWindowPos(s_window, NULL, 0, 0, intermediate.right - intermediate.left,
        intermediate.bottom - intermediate.top, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE));
    CHECK(AdjustWindowRectEx(&original, (DWORD)GetWindowLongW(s_window, GWL_STYLE),
        FALSE, (DWORD)GetWindowLongW(s_window, GWL_EXSTYLE)));
    CHECK(SetWindowPos(s_window, NULL, 0, 0, original.right - original.left,
        original.bottom - original.top, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE));
    s_resizingClient = FALSE;
    CHECK(s_resizeMarkers == 2u && GetClientRect(s_window, &original));
    CHECK(original.right == (LONG)s_width && original.bottom == (LONG)s_height);
    CheckTestWindow();
    Report("client-resize:%ux%u", s_width, s_height);
    CHECK(PostMessageW(s_window, WM_PAINT, 0, 0));
    Report("post-paint");
}

static void ObserveFrameExtents(UINT frame)
{
    RECT rectangle;
    D3D11_TEXTURE2D_DESC description;
    D3D11_VIEWPORT viewport;
    UINT count = 1;
    CHECK(s_observeExtents && !s_hardware && !s_offscreenVisible);
    CheckTestWindow();
    CHECK(GetClientRect(s_window, &rectangle));
    CHECK(rectangle.left == 0 && rectangle.top == 0 && rectangle.right > 0 && rectangle.bottom > 0);
    /* Poison outputs so an omitted getter write cannot match the client extent. */
    memset(&description, 0xff, sizeof(description));
    memset(&viewport, 0xff, sizeof(viewport));
    BeginForward();
    s_textureOriginal->GetDesc(s_texture, &description);
    s_contextOriginal->RSGetViewports(s_context, &count, &viewport);
    EndForward();
    CHECK(description.Width == (UINT)rectangle.right && description.Height == (UINT)rectangle.bottom);
    CHECK(count == 1 && viewport.Width == (FLOAT)rectangle.right && viewport.Height == (FLOAT)rectangle.bottom
        && viewport.TopLeftX == 0 && viewport.TopLeftY == 0 && viewport.MinDepth == 0 && viewport.MaxDepth == 1);
    Report("observed-client:%u:%u:%ldx%ld", s_generation, frame, rectangle.right, rectangle.bottom);
    Report("observed-back-buffer:%u:%u:%ux%u", s_generation, frame, description.Width, description.Height);
    Report("observed-viewport:%u:%u:%ux%u", s_generation, frame, (UINT)viewport.Width, (UINT)viewport.Height);
}

static void ShowExposureCover(void)
{
    RECT window;
    CHECK(s_visibleExposure && !s_coverWindow && GetWindowRect(s_window, &window));
    s_coverWindow = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        L"STATIC", L"", WS_POPUP | SS_BLACKRECT, window.left, window.top,
        window.right - window.left, window.bottom - window.top,
        s_window, NULL, GetModuleHandleW(NULL), NULL);
    CHECK(s_coverWindow);
    CHECK(SetWindowPos(s_coverWindow, HWND_TOPMOST, window.left, window.top,
        window.right - window.left, window.bottom - window.top,
        SWP_NOACTIVATE | SWP_SHOWWINDOW));
    (void)ShowWindow(s_coverWindow, SW_SHOWNOACTIVATE);
    CHECK(UpdateWindow(s_coverWindow));
    CHECK(SetTimer(s_window, EXPOSURE_TIMER, 300, NULL));
    Report("cover-window-shown");
}

static void StartMinimizeRestore(void)
{
    WINDOWPLACEMENT placement;
    RECT window;
    RECT client;
    int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
    int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
    int offscreenLeft;
    int offscreenTop;
    LONG offsetX;
    LONG offsetY;
    HWND foreground;
    CHECK(s_minimizeRestore && !s_minimizeStarted && s_showHandled
        && s_presents == 1 && s_draws == 1 && s_generation == 1
        && !s_texture && !s_target && s_cleared
        && !IsWindowVisible(s_window) && !IsIconic(s_window)
        && GetWindowRect(s_window, &window));
    foreground = GetForegroundWindow();
    CHECK(foreground != s_window);
    if (s_minimizeRestoreSameSize)
    {
        CHECK(GetClientRect(s_window, &client) && client.left == 0 && client.top == 0
            && client.right > 0 && client.bottom > 0
            && client.right == (LONG)s_width && client.bottom == (LONG)s_height);
        s_requestedWidth = (UINT)client.right;
        s_requestedHeight = (UINT)client.bottom;
        Report("same-size-original-client:%ux%u", s_requestedWidth, s_requestedHeight);
    }
    else
    {
        CHECK(s_requestedWidth != s_width || s_requestedHeight != s_height);
    }
    memset(&placement, 0, sizeof(placement));
    placement.length = sizeof(placement);
    CHECK(GetWindowPlacement(s_window, &placement));
    offscreenLeft = left - (window.right - window.left) - 64;
    offscreenTop = top - (window.bottom - window.top) - 64;
    offsetX = offscreenLeft - window.left;
    offsetY = offscreenTop - window.top;
    placement.rcNormalPosition.left += offsetX;
    placement.rcNormalPosition.right += offsetX;
    placement.rcNormalPosition.top += offsetY;
    placement.rcNormalPosition.bottom += offsetY;
    placement.showCmd = SW_HIDE;
    CHECK(SetWindowPlacement(s_window, &placement));
    CHECK(!IsWindowVisible(s_window));
    CHECK(SetWindowPos(s_window, NULL, offscreenLeft, offscreenTop, 0, 0,
        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE));
    CheckWindowOffscreen();
    s_offscreenPlacement = placement;
    s_offscreenPlacementCaptured = TRUE;
    s_minimizeStarted = TRUE;
    s_minimizedPhase = TRUE;
    Report("window-offscreen");
    Report("offscreen-placement-captured");
    Report("minimize-request");
    /* Keep the real minimize notification while leaving the desktop foreground untouched. */
    CHECK(!ShowWindow(s_window, SW_SHOWMINNOACTIVE));
    CHECK(IsWindowVisible(s_window) && IsIconic(s_window)
        && GetForegroundWindow() != s_window);
    Report("minimized");
}

static void RestoreMinimizeRestore(void)
{
    WINDOWPLACEMENT placement;
    RECT bounds = { 0, 0, (LONG)s_requestedWidth, (LONG)s_requestedHeight };
    RECT client;
    CHECK(s_minimizeRestore && s_minimizeStarted && s_minimizedPhase
        && s_minimizeNotificationPosted && s_minimizeMarkerDispatched
        && s_minimizedIterationObserved && s_restoreTimerArmed
        && s_offscreenPlacementCaptured
        && !s_restoreInProgress && !s_restoreNotificationPosted
        && s_generation == 1 && s_draws == 1 && s_presents == 1
        && !s_texture && !s_target && s_cleared && s_resizeMarkers == 0
        && IsWindowVisible(s_window) && IsIconic(s_window)
        && GetForegroundWindow() != s_window);
    CHECK(KillTimer(s_window, MINIMIZE_RESTORE_TIMER));
    Report("minimized-quiescent");
    CHECK(SetWindowPos(s_window, NULL, 0, 0, 0, 0,
        SWP_HIDEWINDOW | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE));
    CHECK(!IsWindowVisible(s_window));
    Report("restore-window-hidden");
    memset(&placement, 0, sizeof(placement));
    placement.length = sizeof(placement);
    CHECK(GetWindowPlacement(s_window, &placement));
    placement.rcNormalPosition = s_offscreenPlacement.rcNormalPosition;
    CHECK(AdjustWindowRectEx(&bounds, (DWORD)GetWindowLongW(s_window, GWL_STYLE),
        FALSE, (DWORD)GetWindowLongW(s_window, GWL_EXSTYLE)));
    CHECK(bounds.right > bounds.left && bounds.bottom > bounds.top);
    placement.rcNormalPosition.right = placement.rcNormalPosition.left
        + bounds.right - bounds.left;
    placement.rcNormalPosition.bottom = placement.rcNormalPosition.top
        + bounds.bottom - bounds.top;
    s_restoreOuterWidth = bounds.right - bounds.left;
    s_restoreOuterHeight = bounds.bottom - bounds.top;
    placement.flags = 0;
    placement.showCmd = SW_SHOWNOACTIVATE;
    s_restoreInProgress = TRUE;
    s_restorePlacementPending = TRUE;
    CHECK(!IsWindowVisible(s_window));
    Report("restore-placement-hidden");
    CHECK(SetWindowPlacement(s_window, &placement));
    CHECK(!s_restorePlacementPending);
    CHECK(IsWindowVisible(s_window) && !IsIconic(s_window)
        && GetForegroundWindow() != s_window
        && GetClientRect(s_window, &client)
        && client.left == 0 && client.top == 0
        && client.right == (LONG)s_requestedWidth && client.bottom == (LONG)s_requestedHeight);
    CheckWindowOffscreen();
    Report("restored-offscreen");
    s_minimizedPhase = FALSE;
    s_restoreInProgress = FALSE;
    CHECK(s_restoreNotificationPosted);
    CheckTestWindow();
    Report("restored:%ux%u", s_requestedWidth, s_requestedHeight);
    if (s_minimizeRestoreSameSize)
    {
        CHECK(s_requestedWidth == s_width && s_requestedHeight == s_height
            && client.right == (LONG)s_width && client.bottom == (LONG)s_height);
        Report("same-size-restored-client:%ux%u", s_requestedWidth, s_requestedHeight);
        {
            RECT update;
            CHECK(InvalidateRect(s_window, NULL, FALSE)
                && GetUpdateRect(s_window, &update, FALSE)
                && update.right > update.left && update.bottom > update.top);
            CheckWindowOffscreen();
            Report("restore-invalidate");
        }
    }
}

static HRESULT STDMETHODCALLTYPE Present(IDXGISwapChain* receiver, UINT interval, UINT flags)
{
    unsigned expectedGeneration = s_presents == 0u ? 1u
        : (s_repeatResize && s_presents >= 2u ? 3u
            : (s_visibleExposure || s_roundTripResize ? 1u : 2u));
    HRESULT result;
    CheckFrameWorkAllowed();
    if (s_minimizeRestore && s_presents > 0u)
    {
        expectedGeneration = s_minimizeRestoreSameSize ? 1u : 2u;
    }
    CHECK(receiver == s_swap && interval == 1 && !flags && s_draws == s_presents + 1u
        && s_presents < ExpectedPresentCount()
        && s_generation == expectedGeneration && !s_cleared);
    CheckTestWindow();
    if (s_observeExtents)
    {
        ObserveFrameExtents(s_presents + 1u);
    }
    ReadPixels(s_presents + 1u);
    BeginForward();
    result = s_swapOriginal->Present(receiver, interval, flags);
    EndForward();
    /* Pixel readback above proves rendering independently of presentation visibility. */
    CHECK(SUCCEEDED(result));
    ++s_presents;
    Report("present:%u:%u:%ld", s_generation, s_presents, result);
    if (s_visibleExposure)
    {
        if (s_presents == 1u)
        {
            ShowExposureCover();
        }
    }
    else if (s_presents == 1u)
    {
        if (!s_minimizeRestore && s_roundTripResize)
        {
            ResizeTestClientRoundTrip();
        }
        else if (!s_minimizeRestore)
        {
            ResizeTestClient(s_requestedWidth, s_requestedHeight, s_coalesceResize ? 2u : 1u);
        }
    }
    else if (s_presents == 2u && s_repeatResize)
    {
        ResizeTestClient(s_secondWidth, s_secondHeight, s_coalesceResize ? 4u : 2u);
    }
    else if ((s_presents == 2u && !s_roundTripResize && !s_minimizeRestore)
        || (s_presents == 3u && s_repeatResize))
    {
        CHECK(s_generation == (s_repeatResize ? 3u : 2u) && s_draws == s_presents);
        if (s_offscreenVisible)
        {
            RECT update;
            CHECK(InvalidateRect(s_window, NULL, FALSE));
            CHECK(GetUpdateRect(s_window, &update, FALSE));
            s_paintInvalidated = TRUE;
            Report("invalidate-paint-region");
        }
        else
        {
            CHECK(PostMessageW(s_window, WM_PAINT, 0, 0));
            Report("post-paint");
        }
    }
    else
    {
        CHECK(s_presents == ExpectedPresentCount()
            && s_generation == (s_repeatResize ? 3u : (s_visibleExposure || s_roundTripResize
                || s_minimizeRestoreSameSize ? 1u : 2u))
            && s_draws == s_presents);
        CHECK(PostMessageW(s_window, WM_CLOSE, 0, 0));
        Report("post-close");
    }
    return result;
}

static HRESULT WINAPI CreateDeviceAndSwapChain(IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver,
    HMODULE software, UINT flags, const D3D_FEATURE_LEVEL* levels, UINT count, UINT sdk,
    const DXGI_SWAP_CHAIN_DESC* description, IDXGISwapChain** swap, ID3D11Device** device,
    D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** context)
{
    HRESULT result;
    CheckTestWindow();
    CHECK(driver == (s_hardware ? D3D_DRIVER_TYPE_HARDWARE : D3D_DRIVER_TYPE_WARP)
        && !adapter && !software && !flags
        && levels && count == 1 && levels[0] == D3D_FEATURE_LEVEL_11_0 && sdk == D3D11_SDK_VERSION
        && description && description->OutputWindow == s_window && description->Windowed
        && swap && !*swap && device && !*device && context && !*context && selected);
    result = D3D11CreateDeviceAndSwapChain(adapter, driver, software, flags, levels, count,
        sdk, description, swap, device, selected, context);
    CHECK(result == S_OK && *swap && *device && *context && *selected == D3D_FEATURE_LEVEL_11_0);
    s_device = *device;
    s_swap = *swap;
    s_context = *context;
    s_deviceOriginal = s_device->lpVtbl;
    s_deviceTable = *s_deviceOriginal;
    s_deviceTable.CreateBuffer = CreateBuffer;
    s_deviceTable.CreateInputLayout = CreateInputLayout;
    s_deviceTable.CreateVertexShader = CreateVertexShader;
    s_deviceTable.CreatePixelShader = CreatePixelShader;
    s_deviceTable.CreateRenderTargetView = CreateTarget;
    s_deviceTable.Release = ReleaseDevice;
    s_device->lpVtbl = &s_deviceTable;
    s_swapOriginal = s_swap->lpVtbl;
    s_swapTable = *s_swapOriginal;
    s_swapTable.ResizeBuffers = ResizeBuffers;
    s_swapTable.GetBuffer = GetBuffer;
    s_swapTable.Present = Present;
    s_swapTable.Release = ReleaseSwap;
    s_swap->lpVtbl = &s_swapTable;
    s_contextOriginal = s_context->lpVtbl;
    s_contextTable = *s_contextOriginal;
    s_contextTable.ClearState = ClearState;
    s_contextTable.OMSetRenderTargets = SetRenderTargets;
    s_contextTable.ClearRenderTargetView = ClearTarget;
    s_contextTable.IASetInputLayout = SetInputLayout;
    s_contextTable.IASetVertexBuffers = SetVertexBuffers;
    s_contextTable.IASetPrimitiveTopology = SetTopology;
    s_contextTable.VSSetShader = SetVertexShader;
    s_contextTable.PSSetShader = SetPixelShader;
    s_contextTable.RSSetViewports = SetViewports;
    s_contextTable.Draw = Draw;
    s_contextTable.Release = ReleaseContext;
    s_context->lpVtbl = &s_contextTable;
    s_owners = 7;
    Report(s_hardware ? "create-hardware" : "create-warp");
    return result;
}

__declspec(dllexport) HWND WINAPI NativeFfiHiddenWarpCreateWindowExW(DWORD exStyle,
    LPCWSTR className, LPCWSTR title, DWORD style, int x, int y, int width, int height,
    HWND parent, HMENU menu, HINSTANCE instance, LPVOID parameter)
{
    CHECK(!s_window && !(style & WS_VISIBLE) && !parent);
    s_window = CreateWindowExW(exStyle, className, title, style, x, y, width, height,
        parent, menu, instance, parameter);
    CheckTestWindow();
    Report(s_visibleExposure ? "create-visible-window" : "create-hidden-window");
    return s_window;
}

__declspec(dllexport) BOOL WINAPI NativeFfiHiddenWarpGetClientRect(HWND window, LPRECT rectangle)
{
    BOOL result = GetClientRect(window, rectangle);
    Report("get-client-rect:%d:%p:%ldx%ld", result, rectangle,
        result ? rectangle->right : 0, result ? rectangle->bottom : 0);
    return result;
}

__declspec(dllexport) BOOL WINAPI NativeFfiHiddenWarpShowWindow(HWND window, int command)
{
    CHECK(window == s_window && command == SW_SHOW && !s_showHandled);
    CheckTestWindow();
    if (s_visibleExposure)
    {
        s_showHandled = TRUE;
        BOOL wasVisible = ShowWindow(window, command);
        CheckTestWindow();
        (void)UpdateWindow(window);
        Report("show-visible");
        return wasVisible;
    }
    if (!s_offscreenVisible)
    {
        s_showHandled = TRUE;
        Report("suppress-show");
        return FALSE;
    }
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN) - 900;
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN) - 900;
        CHECK(SetWindowPos(window, NULL, x, y, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE));
    }
    s_showHandled = TRUE;
    {
        BOOL wasVisible = ShowWindow(window, command);
        CheckTestWindow();
        (void)UpdateWindow(window);
        Report("show-offscreen");
        return wasVisible;
    }
}

static void ObserveMinimizeRestoreMarker(HWND window, UINT message, WPARAM wParam)
{
    if (s_minimizeRestore && window == s_window && message == WM_APP)
    {
        if (wParam == SIZE_MINIMIZED)
        {
            CHECK(s_minimizeNotificationPosted && s_minimizedPhase
                && IsIconic(s_window) && !s_minimizeMarkerDispatched);
            s_minimizeMarkerDispatched = TRUE;
            Report("consumed-size:minimized");
        }
        else if (wParam == SIZE_RESTORED && s_restoreNotificationPosted)
        {
            CHECK(!s_restoreInProgress && !s_minimizedPhase
                && !IsIconic(s_window) && !s_restoreMarkerDispatched);
            s_restoreMarkerDispatched = TRUE;
            Report("consumed-size:restored");
        }
    }
}

__declspec(dllexport) BOOL WINAPI NativeFfiHiddenWarpPeekMessageW(
    LPMSG message, HWND window, UINT minimum, UINT maximum, UINT remove)
{
    BOOL result = PeekMessageW(message, window, minimum, maximum, remove);
    if (result && (remove & PM_REMOVE) != 0)
    {
        ObserveMinimizeRestoreMarker(message->hwnd, message->message, message->wParam);
    }
    return result;
}

__declspec(dllexport) int WINAPI NativeFfiHiddenWarpGetMessageW(
    LPMSG message, HWND window, UINT minimum, UINT maximum)
{
    if (s_minimizeRestore && s_minimizedPhase && s_minimizeMarkerDispatched
        && !s_minimizedIterationObserved)
    {
        MSG pending;
        CheckTestWindow();
        if (PeekMessageW(&pending, NULL, 0, 0, PM_NOREMOVE) == 0)
        {
            s_minimizedIterationObserved = TRUE;
            Report("minimized-wait-entered");
            CHECK(SetTimer(s_window, MINIMIZE_RESTORE_TIMER, 1, NULL));
            s_restoreTimerArmed = TRUE;
            Report("timer-restore-armed");
        }
    }
    int result = GetMessageW(message, window, minimum, maximum);
    if (result > 0)
    {
        ObserveMinimizeRestoreMarker(message->hwnd, message->message, message->wParam);
    }
    return result;
}

__declspec(dllexport) LRESULT WINAPI NativeFfiHiddenWarpDefWindowProcW(
    HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    BOOL validatePaint = s_offscreenVisible && s_paintInvalidated && message == WM_PAINT;
    BOOL validateNaturalExposure = s_naturalExposure && s_naturalExposurePending
        && window == s_window && message == WM_PAINT;
    if (s_restorePlacementPending && window == s_window && message == WM_WINDOWPOSCHANGING)
    {
        WINDOWPOS* position = (WINDOWPOS*)lParam;
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        CHECK(position && s_restoreInProgress
            && s_restoreOuterWidth > 0 && s_restoreOuterHeight > 0);
        position->x = left - s_restoreOuterWidth - 64;
        position->y = top - s_restoreOuterHeight - 64;
        position->flags &= ~SWP_NOMOVE;
        position->flags |= SWP_NOACTIVATE | SWP_NOZORDER;
        s_restorePlacementPending = FALSE;
        Report("restore-position-forced-offscreen");
    }
    if (s_minimizeRestore && window == s_window
        && message == WM_TIMER && wParam == MINIMIZE_RESTORE_TIMER)
    {
        RestoreMinimizeRestore();
    }
    if (s_visibleExposure && window == s_window && message == WM_TIMER)
    {
        if (wParam == EXPOSURE_TIMER)
        {
            CHECK(s_coverWindow && KillTimer(s_window, EXPOSURE_TIMER));
            if (s_naturalExposure)
            {
                s_naturalExposurePending = TRUE;
                Report("await-natural-exposure");
            }
            (void)ShowWindow(s_coverWindow, SW_HIDE);
            CHECK(DestroyWindow(s_coverWindow) && !IsWindow(s_coverWindow));
            s_coverWindow = NULL;
            Report("cover-window-hidden");
            if (!s_naturalExposure)
            {
                RECT update;
                CHECK(InvalidateRect(s_window, NULL, FALSE));
                CHECK(GetUpdateRect(s_window, &update, FALSE));
                Report("invalidate-after-uncover");
            }
            CHECK(SetTimer(s_window, EXPOSURE_CLOSE_TIMER, 1000, NULL));
        }
        else if (wParam == EXPOSURE_CLOSE_TIMER)
        {
            CHECK(KillTimer(s_window, EXPOSURE_CLOSE_TIMER));
            if (s_naturalExposure)
            {
                Report(s_naturalExposurePending
                    ? "natural-exposure-no-paint"
                    : "natural-exposure-redraw");
            }
            CHECK(PostMessageW(s_window, WM_CLOSE, 0, 0));
            Report("post-close");
        }
    }
    LRESULT result;
    if (validatePaint)
    {
        RECT update;
        CHECK(window == s_window && GetUpdateRect(window, &update, FALSE));
        Report("paint-region-before-dispatch");
    }
    if (validateNaturalExposure)
    {
        RECT update;
        CHECK(GetUpdateRect(window, &update, FALSE)
            && update.right > update.left && update.bottom > update.top);
        Report("natural-exposure-paint-before-dispatch");
    }
    result = DefWindowProcW(window, message, wParam, lParam);
    if (validatePaint)
    {
        RECT update;
        CHECK(!GetUpdateRect(window, &update, FALSE));
        s_paintInvalidated = FALSE;
        ++s_paintValidations;
        Report("paint-region-validated");
    }
    if (validateNaturalExposure)
    {
        RECT update;
        CHECK(!GetUpdateRect(window, &update, FALSE));
        s_naturalExposurePending = FALSE;
        Report("natural-exposure-paint-validated");
    }
    return result;
}

__declspec(dllexport) FARPROC WINAPI NativeFfiHiddenWarpGetProcAddress(HMODULE module, LPCSTR name)
{
    if (strcmp(name, "D3D11CreateDeviceAndSwapChain") == 0)
    {
        return (FARPROC)CreateDeviceAndSwapChain;
    }
    return GetProcAddress(module, name);
}

__declspec(dllexport) BOOL WINAPI NativeFfiHiddenWarpPostMessageW(
    HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    BOOL result;
    BOOL minimizeNotification = s_minimizeRestore && window == s_window && wParam == SIZE_MINIMIZED;
    BOOL restoreNotification = s_minimizeRestore && window == s_window
        && wParam == SIZE_RESTORED && s_restoreInProgress;
    CHECK(message == WM_APP && !lParam);
    if (minimizeNotification)
    {
        CHECK(s_minimizeStarted && s_minimizedPhase && IsIconic(s_window)
            && !s_minimizeNotificationPosted);
    }
    else if (restoreNotification)
    {
        RECT client;
        CHECK(s_minimizedPhase && !IsIconic(s_window) && !s_restoreNotificationPosted
            && GetClientRect(s_window, &client)
            && client.left == 0 && client.top == 0
            && client.right == (LONG)s_requestedWidth
            && client.bottom == (LONG)s_requestedHeight);
    }
    if (s_resizingClient)
    {
        CHECK(window == s_window && wParam == SIZE_RESTORED);
        ++s_resizeMarkers;
        Report("resize-marker");
    }
    result = PostMessageW(window, message, wParam, lParam);
    CHECK(result);
    if (minimizeNotification)
    {
        s_minimizeNotificationPosted = TRUE;
        Report("size-notification:minimized");
    }
    else if (restoreNotification)
    {
        s_restoreNotificationPosted = TRUE;
        Report("queued-size:restored:%ux%u", s_requestedWidth, s_requestedHeight);
    }
    return result;
}

__declspec(dllexport) void WINAPI NativeFfiHiddenWarpPostQuitMessage(int code)
{
    unsigned expectedPresents = CompletedPresentCount();
    CheckTestWindow();
    CHECK(!code && s_presents == expectedPresents
        && s_draws == s_presents
        && !s_texture && !s_target && !s_quit && !s_paintInvalidated);
    CHECK(!s_naturalExposure || (s_naturalExposurePending
        ? s_presents == 1u : s_presents == 2u));
    CHECK(s_paintValidations == (s_offscreenVisible ? 1u : 0u));
    s_quit = TRUE;
    PostQuitMessage(code);
    Report("post-quit:%d", code);
}

__declspec(dllexport) BOOL WINAPI NativeFfiHiddenWarpDestroyWindow(HWND window)
{
    CHECK(window == s_window && s_quit && !s_owners && !s_texture && !s_target);
    CheckTestWindow();
    CHECK(DestroyWindow(window) && !IsWindow(window));
    Report(s_visibleExposure ? "destroy-visible-window"
        : (s_minimizeRestore ? "destroy-restored-offscreen-window" : "destroy-hidden-window"));
    return TRUE;
}

__declspec(dllexport) BOOL WINAPI NativeFfiHiddenWarpUnregisterClassW(LPCWSTR name, HINSTANCE instance)
{
    CHECK(s_quit && !s_owners && !IsWindow(s_window)
        && s_generation == (s_repeatResize ? 3u : (s_visibleExposure || s_roundTripResize
            || s_minimizeRestoreSameSize ? 1u : 2u))
        && s_presents == CompletedPresentCount());
    CHECK(UnregisterClassW(name, instance));
    Report("unregister-class");
    Report("complete");
    return TRUE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance;
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        char value[16];
        DWORD length = GetEnvironmentVariableA("TERRUM_DX11_HIDDEN_WARP_REPORT", s_report, sizeof(s_report));
        CHECK(length > 0 && length < sizeof(s_report));
        length = GetEnvironmentVariableA("TERRUM_DX11_HIDDEN_WARP_WIDTH", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        s_requestedWidth = (UINT)atoi(value);
        length = GetEnvironmentVariableA("TERRUM_DX11_HIDDEN_WARP_HEIGHT", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        s_requestedHeight = (UINT)atoi(value);
        CHECK(s_requestedWidth > 0 && s_requestedWidth <= 2048
            && s_requestedHeight > 0 && s_requestedHeight <= 2048);
        s_secondWidth = 513;
        s_secondHeight = 337;
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_OFFSCREEN_VISIBLE", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_offscreenVisible = value[0] == '1';
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_VISIBLE_EXPOSURE", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_visibleExposure = value[0] == '1';
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_NATURAL_EXPOSURE", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_naturalExposure = value[0] == '1';
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_COALESCE_RESIZE", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_coalesceResize = value[0] == '1';
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_REPEAT_RESIZE", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_repeatResize = value[0] == '1';
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_ROUND_TRIP_RESIZE", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_roundTripResize = value[0] == '1';
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_HARDWARE", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_hardware = value[0] == '1';
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_OBSERVE_EXTENTS", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_observeExtents = value[0] == '1';
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_MINIMIZE_RESTORE", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_minimizeRestore = value[0] == '1';
        length = GetEnvironmentVariableA(
            "TERRUM_DX11_HIDDEN_WARP_MINIMIZE_RESTORE_SAME_SIZE", value, sizeof(value));
        CHECK(length > 0 && length < sizeof(value));
        CHECK(strcmp(value, "0") == 0 || strcmp(value, "1") == 0);
        s_minimizeRestoreSameSize = value[0] == '1';
        CHECK(!(s_offscreenVisible && s_visibleExposure)
            && (!s_naturalExposure || s_visibleExposure)
            && (!s_visibleExposure || (!s_hardware && !s_coalesceResize
                && !s_repeatResize && !s_roundTripResize && !s_observeExtents))
            && (!s_minimizeRestore || ((!s_hardware || s_minimizeRestoreSameSize)
                && !s_offscreenVisible && !s_visibleExposure
                && !s_coalesceResize && !s_repeatResize && !s_roundTripResize))
            && (!s_minimizeRestoreSameSize || (s_hardware && s_minimizeRestore
                && !s_observeExtents && !s_offscreenVisible && !s_visibleExposure
                && !s_coalesceResize && !s_repeatResize && !s_roundTripResize))
            && (!s_observeExtents || (!s_hardware && !s_offscreenVisible)));
    }
    return TRUE;
}
