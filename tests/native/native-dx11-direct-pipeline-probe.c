#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <intrin.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This test-only real-receiver interception requires Windows x64.
#endif

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "direct pipeline fixture line %d: %s\n", __LINE__, #condition); ExitProcess(97); } } while (0)

static ID3D11Device* s_device;
static IDXGISwapChain* s_swap;
static ID3D11DeviceContext* s_context;
static ID3D11Buffer* s_buffer;
static ID3D11InputLayout* s_layout;
static ID3D11VertexShader* s_vertex;
static ID3D11PixelShader* s_pixel;
static ID3D11Texture2D* s_texture;
static ID3D11RenderTargetView* s_target;
static ID3D11DeviceVtbl s_deviceOriginal, s_deviceTable;
static IDXGISwapChainVtbl s_swapOriginal, s_swapTable;
static ID3D11DeviceContextVtbl s_contextOriginal, s_contextTable;
static ID3D11DeviceVtbl* s_deviceOriginalAddress;
static ID3D11DeviceContextVtbl* s_contextOriginalAddress;
static ID3D11Texture2DVtbl* s_textureOriginalAddress;
static IDXGISwapChainVtbl* s_swapOriginalAddress;
static ID3D11BufferVtbl* s_bufferOriginalAddress;
static ID3D11InputLayoutVtbl* s_layoutOriginalAddress;
static ID3D11VertexShaderVtbl* s_vertexOriginalAddress;
static ID3D11PixelShaderVtbl* s_pixelOriginalAddress;
static ID3D11RenderTargetViewVtbl* s_targetOriginalAddress;
static ID3D11BufferVtbl s_bufferOriginal, s_bufferTable;
static ID3D11InputLayoutVtbl s_layoutOriginal, s_layoutTable;
static ID3D11VertexShaderVtbl s_vertexOriginal, s_vertexTable;
static ID3D11PixelShaderVtbl s_pixelOriginal, s_pixelTable;
static ID3D11Texture2DVtbl s_textureOriginal, s_textureTable;
static ID3D11RenderTargetViewVtbl s_targetOriginal, s_targetTable;
static HWND s_window;
static BOOL s_hardware;
static char s_report[MAX_PATH];
static unsigned s_mode, s_mask, s_state, s_draws, s_cleared, s_messages, s_forwarding, s_quit;
static unsigned s_clientRectCalls, s_viewportWidth, s_viewportHeight;
static const GUID s_texture2DIid = {
    0x6F15AAF2, 0xD208, 0x4E89, { 0x9A, 0xB4, 0x48, 0x95, 0x35, 0xD3, 0x4F, 0x9C }
};

static void Report(char event)
{
    DWORD written = 0;
    HANDLE file = CreateFileA(s_report, FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    CHECK(file != INVALID_HANDLE_VALUE);
    CHECK(WriteFile(file, &event, 1, &written, NULL) && written == 1);
    CHECK(CloseHandle(file));
}

static void Released(unsigned bit, char event)
{
    unsigned expected = 256;
    while (expected && !(s_mask & expected))
    {
        expected >>= 1;
    }
    CHECK(bit == expected && IsWindow(s_window));
    CHECK(s_state == 0 || (s_cleared == 1 && s_draws == 1 && s_state == 11));
    s_mask &= ~bit;
    Report(event);
}

/* Keep the actual SDK receivers, copy complete tables, and restore each table before its source-owned release.
   Internal calls made while forwarding are not counted as Terrum calls or source-owner releases. */
#define RELEASE(name, type, object, original, bit, event) \
static ULONG STDMETHODCALLTYPE name(type* receiver) \
{ \
    CHECK(receiver == object); \
    if (s_forwarding) { return original.Release(receiver); } \
    Released(bit, event); \
    receiver->lpVtbl = original##Address; \
    ++s_forwarding; \
    { ULONG result = original.Release(receiver); --s_forwarding; return result; } \
}

RELEASE(ReleaseDevice, ID3D11Device, s_device, s_deviceOriginal, 1, 'D')
RELEASE(ReleaseSwap, IDXGISwapChain, s_swap, s_swapOriginal, 2, 'S')
RELEASE(ReleaseContext, ID3D11DeviceContext, s_context, s_contextOriginal, 4, 'C')
RELEASE(ReleaseBuffer, ID3D11Buffer, s_buffer, s_bufferOriginal, 8, 'B')
RELEASE(ReleaseLayout, ID3D11InputLayout, s_layout, s_layoutOriginal, 16, 'I')
RELEASE(ReleaseVertex, ID3D11VertexShader, s_vertex, s_vertexOriginal, 32, 'X')
RELEASE(ReleasePixel, ID3D11PixelShader, s_pixel, s_pixelOriginal, 64, 'Y')
RELEASE(ReleaseTexture, ID3D11Texture2D, s_texture, s_textureOriginal, 128, 'g')
RELEASE(ReleaseTarget, ID3D11RenderTargetView, s_target, s_targetOriginal, 256, 'r')

#define CAPTURE(object, original, table, release, bit) \
    do { CHECK(object != NULL); original##Address = object->lpVtbl; original = *object->lpVtbl; table = original; \
        table.Release = release; object->lpVtbl = &table; s_mask |= bit; } while (0)

static HRESULT STDMETHODCALLTYPE CreateBuffer(ID3D11Device* receiver,
    const D3D11_BUFFER_DESC* description, const D3D11_SUBRESOURCE_DATA* data, ID3D11Buffer** output)
{
    HRESULT result;
    CHECK(receiver == s_device && s_mask == 7 && output && !*output);
    Report('B');
    if (s_mode == 7) { return E_FAIL; }
    ++s_forwarding;
    result = s_deviceOriginal.CreateBuffer(receiver, description, data, output);
    --s_forwarding;
    CHECK(SUCCEEDED(result));
    s_buffer = *output;
    CAPTURE(s_buffer, s_bufferOriginal, s_bufferTable, ReleaseBuffer, 8);
    return result;
}

static HRESULT STDMETHODCALLTYPE CreateLayout(ID3D11Device* receiver,
    const D3D11_INPUT_ELEMENT_DESC* descriptions, UINT count, const void* code, SIZE_T size,
    ID3D11InputLayout** output)
{
    HRESULT result;
    CHECK(receiver == s_device && s_mask == 15 && output && !*output);
    Report('L');
    if (s_mode == 5) { return E_FAIL; }
    ++s_forwarding;
    result = s_deviceOriginal.CreateInputLayout(receiver, descriptions, count, code, size, output);
    --s_forwarding;
    CHECK(SUCCEEDED(result));
    s_layout = *output;
    CAPTURE(s_layout, s_layoutOriginal, s_layoutTable, ReleaseLayout, 16);
    return s_mode == 11 ? E_FAIL : result;
}

static HRESULT STDMETHODCALLTYPE CreateVertex(ID3D11Device* receiver, const void* code, SIZE_T size,
    ID3D11ClassLinkage* linkage, ID3D11VertexShader** output)
{
    HRESULT result;
    CHECK(receiver == s_device && s_mask == 31 && output && !*output);
    Report('V');
    if (s_mode == 3) { return E_FAIL; }
    ++s_forwarding;
    result = s_deviceOriginal.CreateVertexShader(receiver, code, size, linkage, output);
    --s_forwarding;
    CHECK(SUCCEEDED(result));
    s_vertex = *output;
    CAPTURE(s_vertex, s_vertexOriginal, s_vertexTable, ReleaseVertex, 32);
    return s_mode == 10 ? E_FAIL : result;
}

static HRESULT STDMETHODCALLTYPE CreatePixel(ID3D11Device* receiver, const void* code, SIZE_T size,
    ID3D11ClassLinkage* linkage, ID3D11PixelShader** output)
{
    HRESULT result;
    CHECK(receiver == s_device && s_mask == 63 && output && !*output);
    Report('P');
    if (s_mode == 4) { return E_FAIL; }
    ++s_forwarding;
    result = s_deviceOriginal.CreatePixelShader(receiver, code, size, linkage, output);
    --s_forwarding;
    CHECK(SUCCEEDED(result));
    s_pixel = *output;
    CAPTURE(s_pixel, s_pixelOriginal, s_pixelTable, ReleasePixel, 64);
    return s_mode == 12 ? E_FAIL : result;
}

static HRESULT STDMETHODCALLTYPE GetBuffer(IDXGISwapChain* receiver, UINT index, REFIID iid, void** output)
{
    HRESULT result;
    CHECK(receiver == s_swap && receiver->lpVtbl == &s_swapTable && index == 0
        && iid && memcmp(iid, &s_texture2DIid, sizeof(s_texture2DIid)) == 0
        && ((uintptr_t)iid & 7) == 0 && output && ((uintptr_t)output & 7) == 0
        && output != (void**)receiver && !*output && s_mask == 127);
    Report('G');
    if (s_mode == 8) { return E_FAIL; }
    ++s_forwarding;
    result = s_swapOriginal.GetBuffer(receiver, index, iid, output);
    --s_forwarding;
    CHECK(SUCCEEDED(result));
    s_texture = (ID3D11Texture2D*)*output;
    CAPTURE(s_texture, s_textureOriginal, s_textureTable, ReleaseTexture, 128);
    return (HRESULT)INT32_MAX;
}

static HRESULT STDMETHODCALLTYPE ResizeSwap(IDXGISwapChain* receiver, UINT count, UINT width, UINT height,
    DXGI_FORMAT format, UINT flags)
{
    HRESULT result;
    CHECK(receiver == s_swap && receiver->lpVtbl == &s_swapTable && count == 0
        && width > 0 && height > 0 && format == DXGI_FORMAT_UNKNOWN && flags == 0 && s_mask == 127);
    receiver->lpVtbl = s_swapOriginalAddress;
    s_device->lpVtbl = s_deviceOriginalAddress;
    s_context->lpVtbl = s_contextOriginalAddress;
    s_buffer->lpVtbl = s_bufferOriginalAddress;
    s_layout->lpVtbl = s_layoutOriginalAddress;
    s_vertex->lpVtbl = s_vertexOriginalAddress;
    s_pixel->lpVtbl = s_pixelOriginalAddress;
    ++s_forwarding;
    result = s_swapOriginal.ResizeBuffers(receiver, count, width, height, format, flags);
    --s_forwarding;
    s_device->lpVtbl = &s_deviceTable;
    s_context->lpVtbl = &s_contextTable;
    s_buffer->lpVtbl = &s_bufferTable;
    s_layout->lpVtbl = &s_layoutTable;
    s_vertex->lpVtbl = &s_vertexTable;
    s_pixel->lpVtbl = &s_pixelTable;
    receiver->lpVtbl = &s_swapTable;
    CHECK(SUCCEEDED(result));
    return result;
}

static HRESULT STDMETHODCALLTYPE CreateTarget(ID3D11Device* receiver, ID3D11Resource* texture,
    const D3D11_RENDER_TARGET_VIEW_DESC* description, ID3D11RenderTargetView** output)
{
    HRESULT result;
    CHECK(receiver == s_device && receiver->lpVtbl == &s_deviceTable
        && texture == (ID3D11Resource*)s_texture && (void*)texture == (void*)s_texture
        && s_mask == 255 && !description && output && ((uintptr_t)output & 7) == 0
        && output != (ID3D11RenderTargetView**)receiver && !*output);
    Report('R');
    if (s_mode == 6) { return E_FAIL; }
    ++s_forwarding;
    result = s_deviceOriginal.CreateRenderTargetView(receiver, texture, description, output);
    --s_forwarding;
    CHECK(SUCCEEDED(result));
    s_target = *output;
    CAPTURE(s_target, s_targetOriginal, s_targetTable, ReleaseTarget, 256);
    return s_mode == 9 ? (HRESULT)INT32_MIN : (HRESULT)INT32_MAX;
}

static void Step(ID3D11DeviceContext* receiver, unsigned state, char event, const uintptr_t* stack)
{
    CHECK(receiver == s_context && s_mask == 511 && !s_cleared && s_state == state);
    CHECK(((uintptr_t)stack & 15) == 8);
    ++s_state;
    Report(event);
}

static __declspec(noinline) void STDMETHODCALLTYPE ClearTarget(ID3D11DeviceContext* receiver,
    ID3D11RenderTargetView* target, const FLOAT color[4])
{
    const uint32_t expected[4] = { 0x3DA3D70A, 0x3DF5C28F, 0x3E3851EC, 0x3F800000 };
    Step(receiver, 0, 'c', (const uintptr_t*)_AddressOfReturnAddress());
    CHECK(target == s_target && color && !memcmp(color, expected, sizeof(expected)));
    ++s_forwarding;
    s_contextOriginal.ClearRenderTargetView(receiver, target, color);
    --s_forwarding;
}

static __declspec(noinline) void STDMETHODCALLTYPE SetTargets(ID3D11DeviceContext* receiver, UINT count,
    ID3D11RenderTargetView* const* targets, ID3D11DepthStencilView* depth)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    Step(receiver, 1, 'o', stack);
    CHECK(count == 1 && targets && *targets == s_target && !depth && ((uintptr_t)targets & 7) == 0
        && (uintptr_t)targets >= (uintptr_t)(stack + 5) && (void*)targets != (void*)s_target);
    ++s_forwarding;
    s_contextOriginal.OMSetRenderTargets(receiver, count, targets, depth);
    --s_forwarding;
}

static __declspec(noinline) void STDMETHODCALLTYPE SetViewports(ID3D11DeviceContext* receiver, UINT count,
    const D3D11_VIEWPORT* viewports)
{
    RECT client;
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    Step(receiver, 2, 'v', stack);
    CHECK(count == 1 && viewports && ((uintptr_t)viewports & 3) == 0
        && (uintptr_t)viewports >= (uintptr_t)(stack + 5)
        && GetClientRect(s_window, &client) && client.left == 0 && client.top == 0
        && client.right > 0 && client.bottom > 0
        && viewports->TopLeftX == 0 && viewports->TopLeftY == 0
        && viewports->Width == (FLOAT)client.right && viewports->Height == (FLOAT)client.bottom
        && viewports->MinDepth == 0 && viewports->MaxDepth == 1);
    s_viewportWidth = (unsigned)viewports->Width;
    s_viewportHeight = (unsigned)viewports->Height;
    ++s_forwarding;
    s_contextOriginal.RSSetViewports(receiver, count, viewports);
    --s_forwarding;
}

static __declspec(noinline) void STDMETHODCALLTYPE PipelineSetLayout(ID3D11DeviceContext* receiver, ID3D11InputLayout* layout)
{
    Step(receiver, 3, 'l', (const uintptr_t*)_AddressOfReturnAddress());
    CHECK(layout == s_layout);
    ++s_forwarding;
    s_contextOriginal.IASetInputLayout(receiver, layout);
    --s_forwarding;
}

static __declspec(noinline) void STDMETHODCALLTYPE SetBuffers(ID3D11DeviceContext* receiver, UINT start, UINT count,
    ID3D11Buffer* const* buffers, const UINT* strides, const UINT* offsets)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    Step(receiver, 4, 'b', stack);
    CHECK(start == 0 && count == 1 && buffers && *buffers == s_buffer
        && (void*)buffers != (void*)s_buffer && strides && *strides == 12 && offsets && *offsets == 0
        && strides != offsets && ((uintptr_t)buffers & 7) == 0
        && ((uintptr_t)strides & 3) == 0 && ((uintptr_t)offsets & 3) == 0
        && stack[5] == (uintptr_t)strides && stack[6] == (uintptr_t)offsets
        && (uintptr_t)buffers >= (uintptr_t)(stack + 7)
        && (uintptr_t)strides >= (uintptr_t)(stack + 7)
        && (uintptr_t)offsets >= (uintptr_t)(stack + 7)
        && ((uintptr_t)buffers + 8 <= (uintptr_t)strides || (uintptr_t)strides + 4 <= (uintptr_t)buffers)
        && ((uintptr_t)buffers + 8 <= (uintptr_t)offsets || (uintptr_t)offsets + 4 <= (uintptr_t)buffers));
    ++s_forwarding;
    s_contextOriginal.IASetVertexBuffers(receiver, start, count, buffers, strides, offsets);
    --s_forwarding;
}

static __declspec(noinline) void STDMETHODCALLTYPE SetTopology(ID3D11DeviceContext* receiver,
    D3D11_PRIMITIVE_TOPOLOGY topology)
{
    Step(receiver, 5, 't', (const uintptr_t*)_AddressOfReturnAddress());
    CHECK(topology == D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    s_contextOriginal.IASetPrimitiveTopology(receiver, topology);
}

static __declspec(noinline) void STDMETHODCALLTYPE SetVertex(ID3D11DeviceContext* receiver,
    ID3D11VertexShader* shader, ID3D11ClassInstance* const* instances, UINT count)
{
    Step(receiver, 6, 'x', (const uintptr_t*)_AddressOfReturnAddress());
    CHECK(shader == s_vertex && !instances && count == 0);
    ++s_forwarding;
    s_contextOriginal.VSSetShader(receiver, shader, instances, count);
    --s_forwarding;
}

static __declspec(noinline) void STDMETHODCALLTYPE PipelineSetPixel(ID3D11DeviceContext* receiver,
    ID3D11PixelShader* shader, ID3D11ClassInstance* const* instances, UINT count)
{
    Step(receiver, 7, 'y', (const uintptr_t*)_AddressOfReturnAddress());
    CHECK(shader == s_pixel && !instances && count == 0);
    ++s_forwarding;
    s_contextOriginal.PSSetShader(receiver, shader, instances, count);
    --s_forwarding;
}

static __declspec(noinline) void STDMETHODCALLTYPE Draw(ID3D11DeviceContext* receiver, UINT count, UINT start)
{
    Step(receiver, 8, 'd', (const uintptr_t*)_AddressOfReturnAddress());
    CHECK(count == 3 && start == 0 && s_draws++ == 0);
    s_contextOriginal.Draw(receiver, count, start);
}

static void Readback(void)
{
    D3D11_TEXTURE2D_DESC description;
    D3D11_MAPPED_SUBRESOURCE mapped;
    ID3D11Texture2D* staging = NULL;
    const BYTE* outside;
    const BYTE* inside;
    const int interior[4] = { 26, 204, 230, 255 };
    const int clear[4] = { 20, 31, 46, 255 };
    unsigned channel;
    HRESULT result;
    s_device->lpVtbl = s_deviceOriginalAddress;
    s_context->lpVtbl = s_contextOriginalAddress;
    s_texture->lpVtbl = s_textureOriginalAddress;
    s_textureOriginal.GetDesc(s_texture, &description);
    CHECK(description.Width > 400 && description.Height > 300
        && description.Format == DXGI_FORMAT_R8G8B8A8_UNORM && description.SampleDesc.Count == 1);
    CHECK(s_viewportWidth > 0 && s_viewportHeight > 0
        && s_viewportWidth <= description.Width && s_viewportHeight <= description.Height);
    description.Usage = D3D11_USAGE_STAGING;
    description.BindFlags = 0;
    description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    description.MiscFlags = 0;
    result = s_deviceOriginal.CreateTexture2D(s_device, &description, NULL, &staging);
    if (FAILED(result))
    {
        fprintf(stderr, "readback texture: HRESULT=0x%08lX size=%ux%u mips=%u array=%u format=%u sample=%u/%u usage=%u cpu=%u\n",
            (unsigned long)result, description.Width, description.Height, description.MipLevels, description.ArraySize,
            description.Format, description.SampleDesc.Count, description.SampleDesc.Quality,
            description.Usage, description.CPUAccessFlags);
    }
    CHECK(SUCCEEDED(result) && staging);
    s_contextOriginal.CopyResource(s_context, (ID3D11Resource*)staging, (ID3D11Resource*)s_texture);
    CHECK(SUCCEEDED(s_contextOriginal.Map(s_context, (ID3D11Resource*)staging, 0, D3D11_MAP_READ, 0, &mapped)));
    CHECK(mapped.pData && mapped.RowPitch >= description.Width * 4);
    outside = (const BYTE*)mapped.pData;
    inside = outside + (SIZE_T)(s_viewportHeight / 2) * mapped.RowPitch + (s_viewportWidth / 2) * 4;
    for (channel = 0; channel < 4; ++channel)
    {
        int tolerance = channel == 3 ? 0 : 1;
        CHECK(abs((int)inside[channel] - interior[channel]) <= tolerance);
        CHECK(abs((int)outside[channel] - clear[channel]) <= tolerance);
    }
    s_contextOriginal.Unmap(s_context, (ID3D11Resource*)staging, 0);
    staging->lpVtbl->Release(staging);
    s_device->lpVtbl = &s_deviceTable;
    s_context->lpVtbl = &s_contextTable;
    s_texture->lpVtbl = &s_textureTable;
    Report('O');
}

static HRESULT STDMETHODCALLTYPE Present(IDXGISwapChain* receiver, UINT interval, UINT flags)
{
    HRESULT result;
    CHECK(receiver == s_swap && s_mask == 511 && s_state == 9 && s_draws == 1 && !s_cleared
        && interval == 1 && flags == 0);
    ++s_state;
    ++s_forwarding;
    Readback();
    --s_forwarding;
    Report('p');
    if (s_mode == 1) { return E_FAIL; }
    ++s_forwarding;
    result = s_swapOriginal.Present(receiver, interval, flags);
    --s_forwarding;
    CHECK(SUCCEEDED(result));
    return result;
}

static __declspec(noinline) void STDMETHODCALLTYPE ClearState(ID3D11DeviceContext* receiver)
{
    if (receiver == s_context && (s_mask == 127 || s_mask == 511) && s_state == 0
        && !s_cleared && !s_draws)
    {
        CHECK(((uintptr_t)_AddressOfReturnAddress() & 15) == 8);
        Report('K');
    }
    else
    {
        Step(receiver, 10, 'K', (const uintptr_t*)_AddressOfReturnAddress());
        CHECK(s_draws == 1 && s_cleared++ == 0);
    }
    ++s_forwarding;
    s_contextOriginal.ClearState(receiver);
    --s_forwarding;
}

__declspec(dllexport) HRESULT WINAPI NativeFfiDirectPipelineAcquisition(
    IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL* levels, UINT count, UINT sdk, const DXGI_SWAP_CHAIN_DESC* description,
    IDXGISwapChain** swap, ID3D11Device** device, D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** context)
{
    HRESULT result;
    CHECK(!s_mask && driver == (s_hardware ? D3D_DRIVER_TYPE_HARDWARE : D3D_DRIVER_TYPE_WARP)
        && !adapter && !software && !flags && levels && count == 1
        && *levels == D3D_FEATURE_LEVEL_11_0 && sdk == D3D11_SDK_VERSION
        && swap && !*swap && device && !*device && selected && !*selected && context && !*context
        && description && IsWindow(description->OutputWindow));
    /* Test-only deterministic receivers, not a production hardware fallback. */
    result = D3D11CreateDeviceAndSwapChain(adapter, D3D_DRIVER_TYPE_WARP, software, flags, levels, count, sdk,
        description, swap, device, selected, context);
    CHECK(SUCCEEDED(result));
    s_window = description->OutputWindow;
    s_device = *device;
    s_swap = *swap;
    s_context = *context;
    CAPTURE(s_device, s_deviceOriginal, s_deviceTable, ReleaseDevice, 1);
    CAPTURE(s_swap, s_swapOriginal, s_swapTable, ReleaseSwap, 2);
    CAPTURE(s_context, s_contextOriginal, s_contextTable, ReleaseContext, 4);
    s_deviceTable.CreateBuffer = CreateBuffer;
    s_deviceTable.CreateInputLayout = CreateLayout;
    s_deviceTable.CreateVertexShader = CreateVertex;
    s_deviceTable.CreatePixelShader = CreatePixel;
    s_deviceTable.CreateRenderTargetView = CreateTarget;
    s_swapTable.GetBuffer = GetBuffer;
    s_swapTable.ResizeBuffers = ResizeSwap;
    s_swapTable.Present = Present;
    s_contextTable.ClearRenderTargetView = ClearTarget;
    s_contextTable.OMSetRenderTargets = SetTargets;
    s_contextTable.RSSetViewports = SetViewports;
    s_contextTable.IASetInputLayout = PipelineSetLayout;
    s_contextTable.IASetVertexBuffers = SetBuffers;
    s_contextTable.IASetPrimitiveTopology = SetTopology;
    s_contextTable.VSSetShader = SetVertex;
    s_contextTable.PSSetShader = PipelineSetPixel;
    s_contextTable.Draw = Draw;
    s_contextTable.ClearState = ClearState;
    Report('A');
    return result;
}

__declspec(dllexport) int WINAPI NativeFfiDirectPipelineGetMessageW(MSG* message, HWND window, UINT minimum, UINT maximum)
{
    CHECK((s_mode == 17
            ? s_state == 0 && !s_draws && !s_cleared
            : s_hardware
                ? s_state == 10 && s_draws == 1 && !s_cleared
                : s_state == 11 && s_draws == 1 && s_cleared == 1)
        && message && !window && !minimum && !maximum);
    if (s_mode == 2)
    {
        CHECK(s_messages++ == 0);
        Report('!');
        return -1;
    }
    CHECK((s_mode == 0 || s_mode == 14 || s_mode == 17) && s_messages < 2);
    if (s_messages++ == 0)
    {
        memset(message, 0, sizeof(*message));
        message->hwnd = s_window;
        message->message = WM_CLOSE;
        Report('m');
        return 1;
    }
    CHECK(s_quit == 1);
    /* PostQuitMessage is a low-priority virtual message; drain pending window messages before observing it. */
    while (PeekMessageW(message, NULL, 0, 0, PM_REMOVE) && message->message != WM_QUIT)
    {
    }
    CHECK(message->message == WM_QUIT);
    Report('e');
    return 0;
}

__declspec(dllexport) BOOL WINAPI NativeFfiDirectPipelineTranslateMessage(const MSG* message)
{
    CHECK((s_mode == 0 || s_mode == 14 || s_mode == 17) && s_messages == 1
        && (s_mode == 17 ? !s_cleared && !s_draws
            : s_hardware ? !s_cleared && s_draws == 1 : s_cleared == 1)
        && message && message->hwnd == s_window && message->message == WM_CLOSE);
    Report('T');
    return TranslateMessage(message);
}

__declspec(dllexport) LRESULT WINAPI NativeFfiDirectPipelineDispatchMessageW(const MSG* message)
{
    CHECK((s_mode == 0 || s_mode == 14 || s_mode == 17) && s_messages == 1
        && (s_mode == 17 ? !s_cleared && !s_draws
            : s_hardware ? !s_cleared && s_draws == 1 : s_cleared == 1)
        && message && message->hwnd == s_window && message->message == WM_CLOSE);
    Report('F');
    return DispatchMessageW(message);
}

__declspec(dllexport) void WINAPI NativeFfiDirectPipelinePostQuitMessage(int code)
{
    CHECK((s_mode == 0 || s_mode == 14 || s_mode == 17) && code == 0 && s_quit++ == 0
        && (s_mode == 17 ? !s_cleared && !s_draws
            : s_hardware ? !s_cleared && s_draws == 1 : s_cleared == 1));
    Report('Q');
    PostQuitMessage(code);
}

__declspec(dllexport) BOOL WINAPI NativeFfiDirectPipelineGetClientRect(HWND window, LPRECT rectangle)
{
    unsigned call = s_clientRectCalls++;
    CHECK(IsWindow(window) && rectangle && (!s_window || window == s_window));
    if (call == 0 && s_mode == 19)
    {
        s_window = window;
        return FALSE;
    }
    if (call == 1 && s_mode == 13)
    {
        return FALSE;
    }
    if (call == 1 && s_mode >= 15 && s_mode <= 18)
    {
        if (!GetClientRect(window, rectangle))
        {
            return FALSE;
        }
        switch (s_mode)
        {
            case 15: rectangle->left = 1; break;
            case 16: rectangle->top = 1; break;
            case 17: rectangle->right = 0; break;
            case 18: rectangle->bottom = -1; break;
        }
        return TRUE;
    }
    {
        return GetClientRect(window, rectangle);
    }
}

__declspec(dllexport) int WINAPI NativeFfiDirectPipelineShowWindow(HWND window, int command)
{
    CHECK(window == s_window && command == SW_SHOW);
    if (s_mode == 14)
    {
        CHECK(SetWindowPos(window, NULL, 0, 0, 640, 480,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE));
    }
    /* Keep the ABI probe window hidden while preserving ShowWindow's prior-visibility result. */
    return FALSE;
}

__declspec(dllexport) BOOL WINAPI NativeFfiDirectPipelineDestroyWindow(HWND window)
{
    CHECK(!s_mask && window == s_window && DestroyWindow(window));
    Report('W');
    return TRUE;
}

__declspec(dllexport) BOOL WINAPI NativeFfiDirectPipelineUnregisterClassW(LPCWSTR name, HINSTANCE instance)
{
    CHECK(!s_mask && !IsWindow(s_window) && UnregisterClassW(name, instance));
    Report('U');
    return TRUE;
}

__declspec(dllexport) FARPROC WINAPI NativeFfiDirectPipelineGetProcAddress(HMODULE module, LPCSTR name)
{
    if (strcmp(name, "D3D11CreateDeviceAndSwapChain") == 0)
    {
        return (FARPROC)NativeFfiDirectPipelineAcquisition;
    }
    return GetProcAddress(module, name);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance; (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        char mode[16];
        char hardware[2];
        DWORD hardwareLength = GetEnvironmentVariableA("TERRUM_DX11_HARDWARE", hardware, sizeof(hardware));
        CHECK(!hardwareLength || (hardwareLength == 1 && hardware[0] == '1'));
        s_hardware = hardwareLength != 0;
        if (!GetEnvironmentVariableA("TERRUM_DX11_PIPELINE_MODE", mode, sizeof(mode))
            || !GetEnvironmentVariableA("TERRUM_DX11_PIPELINE_REPORT", s_report, sizeof(s_report)))
        {
            return FALSE;
        }
        s_mode = (unsigned)strtoul(mode, NULL, 10);
        CHECK(s_mode <= 19);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        CHECK(!s_mask && !IsWindow(s_window));
        CHECK(s_mode <= 2 || s_mode == 14 ? s_draws == 1 && s_cleared == 1
            : s_draws == 0 && s_cleared == 0);
        Report('Z');
    }
    return TRUE;
}
