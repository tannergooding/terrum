#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <intrin.h>
#include <limits.h>
#include <malloc.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "resize renderer fixture line %d: %s\n", __LINE__, #condition); ExitProcess(97); } } while (0)
#define ABI() CHECK(((uintptr_t)_AddressOfReturnAddress() & 15) == 8)

typedef struct Event { UINT kind; unsigned owner; WPARAM parameter; LONG width, height; } Event;
typedef struct Extent { UINT width, height; } Extent;
static Event s_events[40];
static Extent s_framePlan[8], s_resizePlan[8];
static unsigned s_frameCount, s_resizeCount, s_acquisitions;
static unsigned s_count, s_cursor, s_mask, s_step, s_frames, s_resizes, s_queries, s_translates, s_dispatches, s_paints;
static unsigned s_loaded, s_compilerLoaded, s_liveWindow, s_liveClass, s_drained, s_cleared;
static LONG s_width, s_height;
static RECT s_extent;
static MSG* s_message;
static RECT* s_rectangle;
static D3D11_TEXTURE2D_DESC* s_description;
static D3D11_VIEWPORT* s_observedViewport;
static UINT* s_observedViewportCount;
static unsigned s_textureEvidence;
static unsigned s_viewportEvidence;
static unsigned s_sourceAuthoredViewportEvidence;
static unsigned s_pixelEvidence, s_stagingLive, s_mapped, s_copied, s_pixelChecks;
static BYTE* s_pixelData;
static UINT s_rowPitch;
static ID3D11Texture2D s_staging;
static ID3D11Texture2DVtbl s_stagingTable;
static D3D11_VIEWPORT s_rasterizerViewport;
static char s_mode[40], s_report[MAX_PATH];
static HWND s_window = (HWND)(uintptr_t)UINT64_C(0xABCDEF0112345678);
static ID3D11Device s_device;
static IDXGISwapChain s_swap;
static ID3D11DeviceContext s_context;
static ID3D11Buffer s_buffer;
static ID3D11InputLayout s_layout;
static ID3D11VertexShader s_vertex;
static ID3D11PixelShader s_pixel;
static ID3D11Texture2D s_texture;
static ID3D11RenderTargetView s_target;
static ID3D11DeviceVtbl s_deviceTable;
static IDXGISwapChainVtbl s_swapTable;
static ID3D11DeviceContextVtbl s_contextTable;
static ID3D11BufferVtbl s_bufferTable;
static ID3D11InputLayoutVtbl s_layoutTable;
static ID3D11VertexShaderVtbl s_vertexTable;
static ID3D11PixelShaderVtbl s_pixelTable;
static ID3D11Texture2DVtbl s_textureTable;
static ID3D11RenderTargetViewVtbl s_targetTable;
static HMODULE s_runtime, s_compiler;
static WNDPROC s_callback;
static unsigned s_callbackDepth, s_posts, s_quits;
static WPARAM s_sentParameter;

static int Mode(const char* mode) { return strcmp(s_mode, mode) == 0; }
static int PaintMode(void) { return strncmp(s_mode, "paint-", 6) == 0; }
static int FailureMode(const char* mode) { return Mode(mode) || (PaintMode() && strcmp(s_mode + 6, mode) == 0); }
static int FailedFrame(void) { return s_acquisitions == (Mode("initial-get-partial") || Mode("initial-rtv-partial") ? 1U : 2U); }
static int PixelMode(const char* mode)
{
    return s_acquisitions == 2 ? Mode(mode)
        : s_acquisitions == 1 && strncmp(s_mode, "initial-", 8) == 0 && strcmp(s_mode + 8, mode) == 0;
}

static unsigned ReadPlan(const char* name, Extent* plan)
{
    char text[256], *cursor, *end;
    unsigned count = 0;
    CHECK(GetEnvironmentVariableA(name, text, sizeof(text)));
    if (strcmp(text, "none") == 0) { return 0; }
    cursor = text;
    while (*cursor)
    {
        unsigned long width = strtoul(cursor, &end, 10);
        CHECK(end != cursor && *end == 'x' && width && width <= INT_MAX && count < 8);
        cursor = end + 1;
        unsigned long height = strtoul(cursor, &end, 10);
        CHECK(end != cursor && (*end == ';' || !*end) && height && height <= INT_MAX);
        plan[count++] = (Extent){ (UINT)width, (UINT)height };
        cursor = *end ? end + 1 : end;
    }
    return count;
}

static void Report(char event)
{
    DWORD written;
    HANDLE file = CreateFileA(s_report, FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    CHECK(file != INVALID_HANDLE_VALUE);
    CHECK(WriteFile(file, &event, 1, &written, NULL) && written == 1 && CloseHandle(file));
}

static void Released(unsigned bit, char event)
{
    unsigned expected = 256;
    CHECK(s_loaded && s_liveWindow && s_liveClass && s_cleared && s_mask & bit && !s_stagingLive && !s_mapped);
    while (expected && !(s_mask & expected)) { expected >>= 1; }
    CHECK(bit == expected);
    s_mask &= ~bit;
    Report(event);
}

#define RELEASE(name, type, object, bit, event) \
static __declspec(noinline) ULONG STDMETHODCALLTYPE name(type* receiver) \
{ ABI(); CHECK(receiver == &object); Released(bit, event); return 0; }
RELEASE(ReleaseDevice, ID3D11Device, s_device, 1, 'D')
RELEASE(ReleaseSwap, IDXGISwapChain, s_swap, 2, 'S')
RELEASE(ReleaseContext, ID3D11DeviceContext, s_context, 4, 'C')
RELEASE(ReleaseBuffer, ID3D11Buffer, s_buffer, 8, 'B')
RELEASE(ReleaseLayout, ID3D11InputLayout, s_layout, 16, 'I')
RELEASE(ReleaseVertex, ID3D11VertexShader, s_vertex, 32, 'X')
RELEASE(ReleasePixel, ID3D11PixelShader, s_pixel, 64, 'Y')
RELEASE(ReleaseTexture, ID3D11Texture2D, s_texture, 128, 'g')
RELEASE(ReleaseTarget, ID3D11RenderTargetView, s_target, 256, 'r')

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreateBuffer(ID3D11Device* receiver,
    const D3D11_BUFFER_DESC* desc, const D3D11_SUBRESOURCE_DATA* data, ID3D11Buffer** output)
{
    ABI();
    CHECK(receiver == &s_device && s_mask == 7 && desc && desc->ByteWidth == 36
        && desc->Usage == D3D11_USAGE_IMMUTABLE && desc->BindFlags == D3D11_BIND_VERTEX_BUFFER
        && data && data->pSysMem && output && !*output);
    Report('B');
    *output = &s_buffer; s_mask |= 8;
    return S_OK;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreateLayout(ID3D11Device* receiver,
    const D3D11_INPUT_ELEMENT_DESC* desc, UINT count, const void* code, SIZE_T size, ID3D11InputLayout** output)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    ABI();
    CHECK(receiver == &s_device && s_mask == 15 && desc && count == 1
        && strcmp(desc->SemanticName, "POSITION") == 0 && desc->Format == DXGI_FORMAT_R32G32B32_FLOAT
        && code && size && output && !*output && stack[5] == size && stack[6] == (uintptr_t)output);
    Report('L');
    *output = &s_layout; s_mask |= 16;
    return S_OK;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreateVertex(ID3D11Device* receiver,
    const void* code, SIZE_T size, ID3D11ClassLinkage* linkage, ID3D11VertexShader** output)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    ABI();
    CHECK(receiver == &s_device && s_mask == 31 && code && size && !linkage
        && output && !*output && stack[5] == (uintptr_t)output);
    Report('V'); *output = &s_vertex; s_mask |= 32;
    return S_OK;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreatePixel(ID3D11Device* receiver,
    const void* code, SIZE_T size, ID3D11ClassLinkage* linkage, ID3D11PixelShader** output)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    ABI();
    CHECK(receiver == &s_device && s_mask == 63 && code && size && !linkage
        && output && !*output && stack[5] == (uintptr_t)output);
    Report('P'); *output = &s_pixel; s_mask |= 64;
    return S_OK;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE Resize(IDXGISwapChain* receiver,
    UINT count, UINT width, UINT height, DXGI_FORMAT format, UINT flags)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    ABI();
    CHECK(receiver == &s_swap && s_mask == 127 && s_cleared && s_drained
        && !count && width == (UINT)s_extent.right && height == (UINT)s_extent.bottom
        && width && height && !format && !flags && stack[5] == 0 && stack[6] == 0);
    CHECK(s_resizes < s_resizeCount && width == s_resizePlan[s_resizes].width
        && height == s_resizePlan[s_resizes].height);
    ++s_resizes; Report('z');
    CHECK(s_resizes == 1 || width != (UINT)s_width || height != (UINT)s_height);
    if (Mode("resize-failure") && s_resizes == 2) { return E_FAIL; }
    s_width = (LONG)width; s_height = (LONG)height;
    return Mode("positive-success") ? (HRESULT)0x7FFFFFFF : S_OK;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE GetBuffer(IDXGISwapChain* receiver,
    UINT index, REFIID iid, void** output)
{
    static const GUID expected = { 0x6F15AAF2, 0xD208, 0x4E89, { 0x9A, 0xB4, 0x48, 0x95, 0x35, 0xD3, 0x4F, 0x9C } };
    ABI();
    CHECK(receiver == &s_swap && s_mask == 127 && s_cleared && s_drained
        && index == 0 && iid && !memcmp(iid, &expected, sizeof(expected)) && output && !*output);
    CHECK(s_acquisitions < s_frameCount && s_framePlan[s_acquisitions].width == (UINT)s_width
        && s_framePlan[s_acquisitions].height == (UINT)s_height
        && s_width == s_extent.right && s_height == s_extent.bottom);
    ++s_acquisitions; s_step = 0;
    Report('G');
    if (FailureMode("get-failure") && FailedFrame()) { return E_FAIL; }
    if (FailureMode("get-missing") && FailedFrame()) { return S_FALSE; }
    *output = &s_texture; s_mask |= 128;
    return (FailureMode("get-partial") || Mode("initial-get-partial")) && FailedFrame() ? E_FAIL : S_FALSE;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreateTarget(ID3D11Device* receiver,
    ID3D11Resource* texture, const D3D11_RENDER_TARGET_VIEW_DESC* desc, ID3D11RenderTargetView** output)
{
    ABI();
    CHECK(receiver == &s_device && texture == (ID3D11Resource*)&s_texture
        && s_mask == 255 && s_cleared && !desc && output && !*output);
    Report('R');
    if (FailureMode("rtv-failure") && FailedFrame()) { return E_FAIL; }
    if (FailureMode("rtv-missing") && FailedFrame()) { return S_FALSE; }
    *output = &s_target; s_mask |= 256;
    return (FailureMode("rtv-partial") || Mode("initial-rtv-partial")) && FailedFrame() ? E_FAIL : S_FALSE;
}

static __declspec(noinline) void STDMETHODCALLTYPE GetTextureDescription(
    ID3D11Texture2D* receiver, D3D11_TEXTURE2D_DESC* description)
{
    unsigned index;
    ABI();
    CHECK(s_textureEvidence && receiver == &s_texture && s_mask == 255 && s_cleared
        && s_drained && description && ((uintptr_t)description & 15) == 0);
    CHECK((uintptr_t)description + sizeof(*description) <= (uintptr_t)s_message
        || (uintptr_t)s_message + sizeof(*s_message) <= (uintptr_t)description);
    CHECK((uintptr_t)description + sizeof(*description) <= (uintptr_t)s_rectangle
        || (uintptr_t)s_rectangle + sizeof(*s_rectangle) <= (uintptr_t)description);
    if (!s_description) { s_description = description; }
    CHECK(description == s_description);
    for (index = 0; index < (unsigned)sizeof(*description); ++index) { CHECK(((BYTE*)description)[index] == 0); }
    if (s_pixelEvidence && PixelMode("pixel-desc-partial"))
    {
        description->Width = (UINT)s_width; description->Height = (UINT)s_height;
        Report('T'); return;
    }
    memset(description, 0xA5, sizeof(*description));
    description->Width = (UINT)s_width;
    description->Height = (UINT)s_height;
    if (s_pixelEvidence)
    {
        description->MipLevels = 1; description->ArraySize = 1;
        description->Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        description->SampleDesc = (DXGI_SAMPLE_DESC){ 1, 0 };
        description->Usage = D3D11_USAGE_DEFAULT;
        description->BindFlags = D3D11_BIND_RENDER_TARGET;
        description->CPUAccessFlags = 0; description->MiscFlags = 0;
        if (PixelMode("pixel-format")) { description->Format = DXGI_FORMAT_B8G8R8A8_UNORM; }
        if (PixelMode("pixel-mips")) { description->MipLevels = 2; }
        if (PixelMode("pixel-array")) { description->ArraySize = 2; }
        if (PixelMode("pixel-samples")) { description->SampleDesc.Count = 2; }
        if (PixelMode("pixel-quality")) { description->SampleDesc.Quality = 1; }
    }
    if (Mode("initial-width-mismatch") || (Mode("width-mismatch") && FailedFrame())) { ++description->Width; }
    if (Mode("initial-height-mismatch") || (Mode("height-mismatch") && FailedFrame())) { ++description->Height; }
    Report('T');
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreateStaging(ID3D11Device* receiver,
    const D3D11_TEXTURE2D_DESC* description, const D3D11_SUBRESOURCE_DATA* data, ID3D11Texture2D** output)
{
    ABI();
    CHECK(s_pixelEvidence && receiver == &s_device && s_mask == 511 && !s_cleared && s_step == 9
        && !s_stagingLive && !s_mapped && description == s_description
        && description->Width == (UINT)s_width && description->Height == (UINT)s_height
        && description->MipLevels == 1 && description->ArraySize == 1
        && description->Format == DXGI_FORMAT_R8G8B8A8_UNORM
        && description->SampleDesc.Count == 1 && description->SampleDesc.Quality == 0
        && description->Usage == D3D11_USAGE_STAGING && description->BindFlags == 0
        && description->CPUAccessFlags == D3D11_CPU_ACCESS_READ && description->MiscFlags == 0
        && !data && output && !*output && (BYTE*)output == (BYTE*)s_message + 224);
    Report('a');
    s_copied = 0;
    if (PixelMode("staging-failure")) { return E_FAIL; }
    if (PixelMode("staging-missing")) { return S_FALSE; }
    *output = &s_staging; s_stagingLive = 1;
    return PixelMode("staging-partial") ? E_FAIL : S_FALSE;
}

static __declspec(noinline) void STDMETHODCALLTYPE CopyPixels(ID3D11DeviceContext* receiver,
    ID3D11Resource* destination, ID3D11Resource* source)
{
    ABI();
    CHECK(receiver == &s_context && s_pixelEvidence && s_stagingLive && !s_mapped && !s_copied
        && s_step == 9 && s_mask == 511 && destination == (ID3D11Resource*)&s_staging
        && source == (ID3D11Resource*)&s_texture && destination != source);
    s_copied = 1; Report('c');
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE MapPixels(ID3D11DeviceContext* receiver,
    ID3D11Resource* resource, UINT subresource, D3D11_MAP type, UINT flags, D3D11_MAPPED_SUBRESOURCE* output)
{
    static const BYTE clear[4] = { 20, 31, 46, 255 }, interior[4] = { 26, 204, 230, 255 };
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    BYTE* center;
    unsigned channel, index, row, column;
    ABI();
    CHECK(receiver == &s_context && resource == (ID3D11Resource*)&s_staging
        && s_stagingLive && !s_mapped && s_copied && s_step == 9 && s_mask == 511
        && !subresource && type == D3D11_MAP_READ && !flags && stack[5] == 0
        && stack[6] == (uintptr_t)output && output && (BYTE*)output == (BYTE*)s_message + 240);
    for (index = 0; index < sizeof(*output); ++index) { CHECK(((BYTE*)output)[index] == 0); }
    CHECK(*(const UINT*)((const BYTE*)output + 16) == 1);
    Report('m');
    if (PixelMode("map-failure")) { return E_FAIL; }
    if (PixelMode("map-populated-failure"))
    {
        output->pData = (void*)(uintptr_t)UINT64_MAX;
        output->RowPitch = UINT_MAX;
        return E_FAIL;
    }
    s_mapped = 1;
    if (PixelMode("map-no-write")) { return S_FALSE; }
    s_rowPitch = (UINT)s_width * 4 + (Mode("pixel-tight") ? 0 : 64);
    s_pixelData = (BYTE*)_aligned_malloc((SIZE_T)s_rowPitch * (UINT)s_height, 16);
    CHECK(s_pixelData);
    memset(s_pixelData, 0xCD, (SIZE_T)s_rowPitch * (UINT)s_height);
    for (row = 0; row < (UINT)s_height; ++row)
    {
        for (column = 0; column < (UINT)s_width; ++column)
        {
            memcpy(s_pixelData + (SIZE_T)row * s_rowPitch + (SIZE_T)column * 4, clear, sizeof(clear));
        }
    }
    center = s_pixelData + (SIZE_T)((UINT)s_height / 2) * s_rowPitch + ((UINT)s_width / 2) * 4;
    memcpy(center, interior, sizeof(interior));
    if (PixelMode("pixel-stray"))
    {
        s_pixelData[(SIZE_T)((UINT)s_height - 2) * s_rowPitch + ((UINT)s_width - 2) * 4] = 0;
    }
    for (channel = 0; channel < 4; ++channel)
    {
        char name[40];
        CHECK(sprintf_s(name, sizeof(name), "pixel-clear-%u", channel) > 0);
        if (PixelMode(name)) { s_pixelData[channel] -= channel == 3 ? 1 : 2; }
        CHECK(sprintf_s(name, sizeof(name), "pixel-interior-%u", channel) > 0);
        if (PixelMode(name)) { center[channel] -= channel == 3 ? 1 : 2; }
        if (channel < 3)
        {
            CHECK(sprintf_s(name, sizeof(name), "pixel-clear-high-%u", channel) > 0);
            if (PixelMode(name)) { s_pixelData[channel] += 2; }
            CHECK(sprintf_s(name, sizeof(name), "pixel-interior-high-%u", channel) > 0);
            if (PixelMode(name)) { center[channel] += 2; }
        }
        if (channel < 3 && Mode("pixel-tolerance"))
        {
            s_pixelData[channel] += s_acquisitions == 1 ? 1 : -1;
            center[channel] += s_acquisitions == 1 ? -1 : 1;
        }
    }
    output->pData = s_pixelData;
    output->RowPitch = s_rowPitch;
    output->DepthPitch = Mode("pixel-depth-zero") ? 0 : UINT_MAX;
    if (PixelMode("map-null")) { output->pData = NULL; }
    if (PixelMode("map-row-zero")) { output->RowPitch = 0; }
    if (PixelMode("map-row-short")) { output->RowPitch = (UINT)s_width * 4 - 1; }
    if (PixelMode("map-pointer-wrap"))
    {
        output->pData = (void*)(uintptr_t)(UINT64_MAX - 0x10000);
        output->RowPitch = 0x80000000;
    }
    if (PixelMode("map-pointer-end")) { output->pData = (void*)(uintptr_t)(UINT64_MAX - 1); }
    if (PixelMode("map-pointer-last-byte")) { output->pData = (void*)(uintptr_t)UINTPTR_MAX; }
    if (PixelMode("map-pointer-last-three")) { output->pData = (void*)(uintptr_t)(UINTPTR_MAX - 2); }
    if (PixelMode("map-partial")) { output->RowPitch = 0; output->DepthPitch = 0; }
    return Mode("positive-success") ? (HRESULT)0x7FFFFFFF : S_FALSE;
}

static __declspec(noinline) void STDMETHODCALLTYPE UnmapPixels(ID3D11DeviceContext* receiver,
    ID3D11Resource* resource, UINT subresource)
{
    ABI();
    CHECK(receiver == &s_context && resource == (ID3D11Resource*)&s_staging
        && !subresource && s_mapped && s_stagingLive && s_step == 9 && s_mask == 511);
    s_mapped = 0; ++s_pixelChecks; Report('u');
    if (s_pixelData) { _aligned_free(s_pixelData); s_pixelData = NULL; }
}

static __declspec(noinline) ULONG STDMETHODCALLTYPE ReleaseStaging(ID3D11Texture2D* receiver)
{
    ABI();
    CHECK(receiver == &s_staging && s_stagingLive && !s_mapped && !s_pixelData && s_step == 9 && s_mask == 511);
    s_stagingLive = 0; Report('h');
    return 0;
}

static void Step(ID3D11DeviceContext* receiver, unsigned expected, char event)
{
    CHECK(receiver == &s_context && s_mask == 511 && s_step == expected && (expected == 0 || !s_cleared));
    s_cleared = 0; ++s_step; Report(event);
}

static __declspec(noinline) void STDMETHODCALLTYPE ClearTarget(ID3D11DeviceContext* receiver,
    ID3D11RenderTargetView* target, const FLOAT color[4])
{
    static const uint32_t expected[] = { 0x3DA3D70A, 0x3DF5C28F, 0x3E3851EC, 0x3F800000 };
    ABI(); Step(receiver, 0, 'c');
    CHECK(target == &s_target && color && !memcmp(color, expected, sizeof(expected)));
}
static __declspec(noinline) void STDMETHODCALLTYPE Targets(ID3D11DeviceContext* receiver,
    UINT count, ID3D11RenderTargetView* const* targets, ID3D11DepthStencilView* depth)
{
    ABI(); Step(receiver, 1, 'o');
    CHECK(count == 1 && targets && *targets == &s_target && !depth
        && (void*)targets != (void*)&s_target && ((uintptr_t)targets & 7) == 0);
}
static __declspec(noinline) void STDMETHODCALLTYPE Viewports(ID3D11DeviceContext* receiver,
    UINT count, const D3D11_VIEWPORT* viewport)
{
    ABI(); Step(receiver, 2, 'v');
    CHECK(count == 1 && viewport && ((uintptr_t)viewport & 3) == 0
        && viewport->TopLeftX == 0 && viewport->TopLeftY == 0
        && viewport->Width == (FLOAT)s_width && viewport->Height == (FLOAT)s_height
        && viewport->MinDepth == 0 && viewport->MaxDepth == 1);
    s_rasterizerViewport = *viewport;
}
static __declspec(noinline) void STDMETHODCALLTYPE GetViewports(ID3D11DeviceContext* receiver,
    UINT* count, D3D11_VIEWPORT* viewport)
{
    unsigned index;
    uint32_t bits[6];
    ABI();
    CHECK(s_viewportEvidence);
    CHECK(receiver == &s_context);
    CHECK(s_mask == 511);
    CHECK(!s_cleared);
    CHECK(s_step == 10);
    CHECK(s_drained);
    CHECK(count);
    CHECK(*count == 1);
    CHECK(viewport && ((uintptr_t)viewport & (s_sourceAuthoredViewportEvidence ? 3 : 15)) == 0
        && ((uintptr_t)count & 3) == 0);
    if (!s_observedViewport) { s_observedViewport = viewport; }
    CHECK(viewport == s_observedViewport);
    if (!s_observedViewportCount) { s_observedViewportCount = count; }
    CHECK(count == s_observedViewportCount);
    for (index = 0; index < (s_sourceAuthoredViewportEvidence ? 6U : 8U); ++index)
    {
        CHECK(((const uint32_t*)viewport)[index]
            == (s_sourceAuthoredViewportEvidence ? 0xBF800000 : 0x7FC00000));
    }
    Report('V');
    if (Mode("viewport-no-write") && s_frames == 2) { return; }
    memcpy(bits, &s_rasterizerViewport, sizeof(bits));
    if (s_frames == 2 || strncmp(s_mode, "initial-viewport-", 17) == 0)
    {
        const char* mode = s_frames == 1 ? s_mode + 8 : s_mode;
        if (strcmp(mode, "viewport-count-zero") == 0) { *count = 0; }
        if (strcmp(mode, "viewport-count-two") == 0) { *count = 2; }
        if (strcmp(mode, "viewport-count-max") == 0) { *count = UINT_MAX; }
        if (strcmp(mode, "viewport-x") == 0) { bits[0] = 0x3F800000; }
        if (strcmp(mode, "viewport-y") == 0) { bits[1] = 0x3F800000; }
        if (strcmp(mode, "viewport-negative-zero") == 0) { bits[0] = 0x80000000; }
        if (strcmp(mode, "viewport-width") == 0) { ++bits[2]; }
        if (strcmp(mode, "viewport-height") == 0) { ++bits[3]; }
        if (strcmp(mode, "viewport-min-depth") == 0) { bits[4] = 0x3F000000; }
        if (strcmp(mode, "viewport-max-depth") == 0) { bits[5] = 0x3F000000; }
        if (strcmp(mode, "viewport-nan") == 0) { bits[2] = 0x7FC00000; }
        if (strcmp(mode, "viewport-infinity") == 0) { bits[3] = 0x7F800000; }
        if (strcmp(mode, "viewport-stale") == 0) { bits[2] = 0x44480000; bits[3] = 0x44160000; }
        if (strcmp(mode, "viewport-partial") == 0)
        {
            memcpy(viewport, bits, sizeof(bits) - sizeof(bits[5]));
            return;
        }
    }
    memcpy(viewport, bits, sizeof(bits));
}
static __declspec(noinline) void STDMETHODCALLTYPE Layout(ID3D11DeviceContext* receiver, ID3D11InputLayout* layout)
{
    ABI(); Step(receiver, 3, 'l'); CHECK(layout == &s_layout);
}
static __declspec(noinline) void STDMETHODCALLTYPE Buffers(ID3D11DeviceContext* receiver,
    UINT start, UINT count, ID3D11Buffer* const* buffers, const UINT* strides, const UINT* offsets)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    ABI(); Step(receiver, 4, 'b');
    CHECK(!start && count == 1 && buffers && *buffers == &s_buffer && strides && *strides == 12
        && offsets && !*offsets && strides != offsets
        && stack[5] == (uintptr_t)strides && stack[6] == (uintptr_t)offsets);
}
static __declspec(noinline) void STDMETHODCALLTYPE Topology(ID3D11DeviceContext* receiver, D3D11_PRIMITIVE_TOPOLOGY topology)
{
    ABI(); Step(receiver, 5, 't'); CHECK(topology == D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
}
static __declspec(noinline) void STDMETHODCALLTYPE Vertex(ID3D11DeviceContext* receiver,
    ID3D11VertexShader* shader, ID3D11ClassInstance* const* instances, UINT count)
{
    ABI(); Step(receiver, 6, 'x'); CHECK(shader == &s_vertex && !instances && !count);
}
static __declspec(noinline) void STDMETHODCALLTYPE Pixel(ID3D11DeviceContext* receiver,
    ID3D11PixelShader* shader, ID3D11ClassInstance* const* instances, UINT count)
{
    ABI(); Step(receiver, 7, 'y'); CHECK(shader == &s_pixel && !instances && !count);
}
static __declspec(noinline) void STDMETHODCALLTYPE Draw(ID3D11DeviceContext* receiver, UINT count, UINT start)
{
    ABI(); Step(receiver, 8, 'd'); CHECK(count == 3 && !start);
}
static __declspec(noinline) HRESULT STDMETHODCALLTYPE Present(IDXGISwapChain* receiver, UINT interval, UINT flags)
{
    ABI(); CHECK(receiver == &s_swap && s_step == 9 && s_mask == 511 && !s_cleared && interval == 1 && !flags
        && !s_stagingLive && !s_mapped && (!s_pixelEvidence || s_pixelChecks == s_acquisitions));
    ++s_step; ++s_frames; Report('p');
    return FailureMode("present-failure") && s_frames == 2 ? E_FAIL : S_FALSE;
}
static __declspec(noinline) void STDMETHODCALLTYPE ClearState(ID3D11DeviceContext* receiver)
{
    ABI(); CHECK(receiver == &s_context && s_mask >= 127 && !s_stagingLive && !s_mapped
        && (s_cleared || s_step == 10 || (s_pixelEvidence && s_step == 9)));
    s_cleared = 1; Report('K');
}

static __declspec(noinline) HRESULT WINAPI Acquire(IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver,
    HMODULE software, UINT flags, const D3D_FEATURE_LEVEL* levels, UINT count, UINT sdk,
    const DXGI_SWAP_CHAIN_DESC* desc, IDXGISwapChain** swap, ID3D11Device** device,
    D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** context)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    ABI();
    CHECK(s_loaded && !s_mask && !adapter
        && driver == (GetEnvironmentVariableA("TERRUM_DX11_RESIZE_HARDWARE", NULL, 0) ? 1 : 5)
        && !software && !flags && levels && *levels == 0xB000 && count == 1 && sdk == 7
        && desc && desc->OutputWindow == s_window && desc->BufferDesc.Width == 800 && desc->BufferDesc.Height == 600
        && swap && !*swap && device && !*device && selected && !*selected && context && !*context
        && stack[5] == (uintptr_t)levels && stack[6] == count && stack[7] == sdk
        && stack[8] == (uintptr_t)desc && stack[9] == (uintptr_t)swap && stack[10] == (uintptr_t)device
        && stack[11] == (uintptr_t)selected && stack[12] == (uintptr_t)context);
    *device = &s_device; *swap = &s_swap; *context = &s_context; *selected = 0xB000;
    s_mask = 7; s_cleared = 1; Report('A');
    return Mode("acquire-failure") ? E_FAIL : S_FALSE;
}

static void Add(UINT kind, unsigned owner, WPARAM parameter, LONG width, LONG height)
{
    CHECK(s_count < 40); s_events[s_count++] = (Event){ kind, owner, parameter, width, height };
}
static void Size(WPARAM parameter, LONG width, LONG height)
{
    Add(s_textureEvidence ? WM_SIZE : WM_APP, 1, parameter, width, height);
}
static void SentSize(WPARAM parameter, LONG width, LONG height) { Add(UINT_MAX - 2, 1, parameter, width, height); }
static void Paint(unsigned owner) { Add(WM_PAINT, owner, 0, 0, 0); }
static void Batch(void) { Add(UINT_MAX - 1, 0, 0, 0, 0); }
static void BuildEvents(void)
{
    s_extent = (RECT){ 0, 0, 800, 600 };
    if (s_textureEvidence)
    {
        if (Mode("viewport-rounded"))
        {
            s_extent.right = 16777217; s_extent.bottom = 16777219;
        }
        if (s_pixelEvidence && Mode("initial-pixel-too-wide")) { s_extent.right = 4097; }
        if (s_pixelEvidence && Mode("initial-pixel-too-short")) { s_extent.bottom = 1; }
        if (s_pixelEvidence && Mode("pixel-min-width")) { s_extent.right = 16; }
        if (s_pixelEvidence && Mode("pixel-min-height")) { s_extent.bottom = 16; }
        if (s_pixelEvidence && Mode("pixel-max-width")) { s_extent.right = 4096; }
        if (s_pixelEvidence && Mode("pixel-max-height")) { s_extent.bottom = 4096; }
        if (s_pixelEvidence && Mode("pixel-below-width")) { s_extent.right = 15; }
        if (s_pixelEvidence && Mode("pixel-below-height")) { s_extent.bottom = 15; }
        if (s_pixelEvidence && Mode("pixel-above-height")) { s_extent.bottom = 4097; }
        if (s_pixelEvidence && Mode("pixel-odd")) { s_extent.right = 801; s_extent.bottom = 601; }
        if (Mode("peek-quit")) { Add(WM_QUIT, 0, 0, 0, 0); return; }
        if (Mode("pending-quit")) { Batch(); Add(WM_QUIT, 0, 0, 0, 0); return; }
        if (Mode("zero-width")) { s_extent.right = 0; }
        if (Mode("zero-height")) { s_extent.bottom = 0; }
        if (Mode("minimized")) { Size(SIZE_MINIMIZED, 800, 600); }
        Batch(); Batch();
        return;
    }
    if (Mode("pending-quit")) { Size(SIZE_RESTORED, 900, 700); Add(WM_QUIT, 0, 0, 0, 0); return; }
    if (Mode("quit-error-peek")) { Size(SIZE_RESTORED, 900, 700); Add(WM_QUIT, 0, 1, 0, 0); return; }
    if (Mode("peek-quit")) { Add(WM_QUIT, 0, 0, 0, 0); return; }
    if (Mode("paint-pending-quit")) { Paint(1); Add(WM_QUIT, 0, 0, 0, 0); return; }
    if (Mode("paint-initial")) { Paint(1); Paint(1); }
    if (Mode("minimized") || Mode("minimized-last"))
    {
        if (Mode("minimized-last")) { Size(SIZE_RESTORED, 900, 700); }
        Size(SIZE_MINIMIZED, 900, 700); Batch();
    }
    if (Mode("zero-width") || Mode("zero-height") || Mode("zero-both"))
    {
        Size(SIZE_RESTORED, Mode("zero-height") ? 800 : 0, Mode("zero-width") ? 600 : 0); Batch();
    }
    Size(SIZE_RESTORED, 800, 600); Batch();
    if (Mode("initial") || Mode("get-quit") || Mode("paint-initial")
        || Mode("callback-show") || Mode("callback-show-post-failure") || Mode("callback-create-post-failure")
        || Mode("callback-delegate"))
    {
        Add(WM_QUIT, 0, 0, 0, 0); return;
    }
    if (Mode("quit-error-get")) { Add(WM_QUIT, 0, 1, 0, 0); return; }
    if (strncmp(s_mode, "callback-", 9) == 0)
    {
        if (Mode("callback-peek-post-failure")) { Add(WM_NULL, 1, 0, 0, 0); }
        if (Mode("callback-dispatch") || Mode("callback-dispatch-post-failure"))
        {
            Add(WM_WINDOWPOSCHANGED, 1, 0, 0, 0);
            Batch(); Add(WM_QUIT, 0, 0, 0, 0); return;
        }
        if (Mode("callback-close") || Mode("callback-close-post-failure"))
        {
            Add(UINT_MAX - 3, 1, 0, 0, 0); return;
        }
        if (Mode("callback-coalesced"))
        {
            SentSize(SIZE_RESTORED, 900, 700); SentSize(SIZE_MINIMIZED, 0, 0);
            SentSize(SIZE_MAXIMIZED, 1024, 768); SentSize(SIZE_MAXSHOW, 1024, 768);
            SentSize(SIZE_MAXHIDE, 1024, 768);
        }
        if (Mode("callback-minimized"))
        {
            SentSize(SIZE_MINIMIZED, 0, 0); Batch();
        }
        SentSize(Mode("callback-wide-wparam") ? (WPARAM)UINT64_C(0xFEDCBA9876543210) : SIZE_RESTORED, 1280, 720);
        Batch(); Add(WM_QUIT, 0, 0, 0, 0); return;
    }
    if (Mode("quit-after-frame"))
    {
        Size(SIZE_RESTORED, 900, 700); Add(WM_QUIT, 0, 0, 0, 0); return;
    }
    if (Mode("paint-quit-after-frame"))
    {
        Paint(1); Add(WM_QUIT, 0, 0, 0, 0); return;
    }
    if (Mode("paint-minimized-stale") || Mode("paint-zero-stale"))
    {
        Size(Mode("paint-minimized-stale") ? SIZE_MINIMIZED : SIZE_RESTORED, 0, 0); Batch();
        Paint(1); Paint(1); Batch();
        Size(SIZE_RESTORED, 800, 600); Batch();
        Add(WM_NULL, 1, 0, 0, 0); Batch();
    }
    else if (Mode("paint-resize") || Mode("paint-final-rect"))
    {
        Paint(1); Size(SIZE_RESTORED, 1024, 768); Size(SIZE_RESTORED, 1280, 720);
        if (Mode("paint-final-rect")) { Paint(1); }
        Batch();
    }
    else if (Mode("resize-paint"))
    {
        Size(SIZE_RESTORED, 1024, 768); Paint(1); Size(SIZE_RESTORED, 1280, 720); Paint(1); Batch();
    }
    else if (Mode("paint-peek"))
    {
        Add(WM_NULL, 1, 0, 0, 0); Paint(1); Batch();
    }
    else if (Mode("paint-repeat"))
    {
        Paint(1); Batch(); Paint(1); Batch(); Paint(1); Batch();
    }
    else if (Mode("paint-then-resize"))
    {
        Paint(1); Batch(); Size(SIZE_RESTORED, 1280, 720); Batch();
    }
    else if (Mode("resize-then-paint"))
    {
        Size(SIZE_RESTORED, 1280, 720); Batch(); Paint(1); Batch();
    }
    else if (Mode("paint-foreign"))
    {
        Paint(2); Paint(0); Add(WM_NULL, 1, 0, 0, 0); Batch();
    }
    else if (Mode("paint-coalesced"))
    {
        Paint(1); Paint(1); Paint(1); Batch();
    }
    else if (Mode("paint-unchanged-size"))
    {
        Paint(1); Size(SIZE_RESTORED, 800, 600); Paint(1); Batch();
    }
    else if (PaintMode())
    {
        Paint(1); Batch();
    }
    else if (Mode("unchanged") || Mode("restore-unchanged") || Mode("zero-restore-unchanged"))
    {
        if (Mode("restore-unchanged")) { Size(SIZE_MINIMIZED, 0, 0); Batch(); }
        if (Mode("zero-restore-unchanged")) { Size(SIZE_RESTORED, 0, 0); Batch(); }
        Size(SIZE_RESTORED, 800, 600); Batch();
    }
    else if (Mode("foreign"))
    {
        Add(WM_APP, 2, SIZE_MINIMIZED, 0, 0); Add(WM_APP, 0, SIZE_MINIMIZED, 0, 0); Batch();
    }
    else if (Mode("unrelated-messages"))
    {
        Add(WM_SIZE, 1, SIZE_MINIMIZED, 0, 0); Add(WM_APP + 1, 1, SIZE_MINIMIZED, 0, 0);
        Add(WM_USER, 1, SIZE_MINIMIZED, 0, 0); Batch();
    }
    else
    {
        if (Mode("coalesced"))
        {
            Size(SIZE_RESTORED, 900, 700); Size(SIZE_MINIMIZED, 0, 0); Size(SIZE_RESTORED, 1024, 768);
        }
        Size(SIZE_RESTORED, Mode("full-width") ? 70000 : 1280, Mode("full-width") ? 80000 : 720); Batch();
    }
    Add(FailureMode("message-error") ? UINT_MAX : WM_QUIT, 0, 0, 0, 0);
}
static void MessageAddress(MSG* message)
{
    CHECK(message && ((uintptr_t)message & 7) == 0 && s_liveWindow && !(s_mask & 384));
    if (!s_message) { s_message = message; }
    CHECK(s_message == message);
}
static void Insert(unsigned index, Event event)
{
    CHECK(s_count < 40 && index <= s_count);
    memmove(s_events + index + 1, s_events + index, (s_count - index) * sizeof(Event));
    s_events[index] = event; ++s_count;
}
__declspec(dllexport) __declspec(noinline) BOOL WINAPI NativeFfiResizeRenderPostMessageW(
    HWND window, UINT message, WPARAM parameter, LPARAM packed)
{
    unsigned index = s_cursor;
    ABI();
    CHECK(!s_textureEvidence && s_callbackDepth == 1 && window == s_window && message == WM_APP
        && parameter == s_sentParameter && packed == 0 && s_liveWindow);
    ++s_posts; Report('a');
    if (Mode("callback-get-post-failure") || Mode("callback-peek-post-failure")
        || Mode("callback-show-post-failure") || Mode("callback-create-post-failure")
        || Mode("callback-dispatch-post-failure") || Mode("callback-post-failure-close")
        || Mode("callback-close-post-failure")) { return FALSE; }
    while (index < s_count && s_events[index].kind != UINT_MAX - 1) { ++index; }
    Insert(index, (Event){ WM_APP, 1, parameter, LONG_MIN, LONG_MIN });
    return -13;
}
__declspec(dllexport) __declspec(noinline) void WINAPI NativeFfiResizeRenderPostQuitMessage(int code)
{
    int expected = Mode("callback-close") || (Mode("callback-close-post-failure") && !s_quits) ? 0 : 1;
    ABI();
    CHECK(!s_textureEvidence && s_callbackDepth == 1 && code == expected);
    ++s_quits; Report('Q');
    if (s_cursor < s_count && s_events[s_cursor].kind == WM_QUIT)
    {
        s_events[s_cursor].parameter = (WPARAM)code; return;
    }
    Insert(s_cursor, (Event){ WM_QUIT, 0, (WPARAM)code, 0, 0 });
}
static void CallSize(WPARAM parameter)
{
    unsigned mask = s_mask, step = s_step;
    CHECK(s_callback && !s_callbackDepth);
    s_sentParameter = parameter; ++s_callbackDepth; Report('s');
    CHECK(s_callback(s_window, WM_SIZE, parameter, (LPARAM)UINT64_C(0xFEDCBA9876543210)) == 0);
    --s_callbackDepth;
    CHECK(s_mask == mask && s_step == step);
    if (Mode("callback-post-failure-close"))
    {
        ++s_callbackDepth; CHECK(s_callback(s_window, WM_CLOSE, 0, 0) == 0); --s_callbackDepth;
    }
}
static void SentMessages(void)
{
    while (s_cursor < s_count && (s_events[s_cursor].kind == UINT_MAX - 2
        || s_events[s_cursor].kind == UINT_MAX - 3))
    {
        Event event = s_events[s_cursor++];
        if (event.kind == UINT_MAX - 3)
        {
            ++s_callbackDepth; CHECK(s_callback(s_window, WM_CLOSE, 0, 0) == 0); --s_callbackDepth;
            if (Mode("callback-close-post-failure")) { CallSize(SIZE_RESTORED); }
        }
        else
        {
            s_extent.right = event.width; s_extent.bottom = event.height;
            CallSize(event.parameter);
        }
    }
}
static void Next(MSG* message)
{
    const Event* event;
    CHECK(s_cursor < s_count); event = &s_events[s_cursor++];
    memset(message, 0, sizeof(*message));
    message->hwnd = event->owner == 1 ? s_window : event->owner == 2 ? (HWND)(uintptr_t)0x9999 : NULL;
    message->message = event->kind; message->wParam = event->parameter;
    message->lParam = MAKELPARAM(17, 19);
    if ((s_textureEvidence ? event->kind == WM_SIZE : event->kind == WM_APP)
        && event->owner == 1)
    {
        message->lParam = 0;
        if (event->width != LONG_MIN) { s_extent.right = event->width; s_extent.bottom = event->height; }
    }
    if (event->kind == WM_PAINT && event->owner == 1) { ++s_paints; }
    s_drained = 0;
}

__declspec(dllexport) __declspec(noinline) BOOL WINAPI NativeFfiResizeRenderPeekMessageW(
    MSG* message, HWND window, UINT minimum, UINT maximum, UINT remove)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    ABI();
    if (s_callbackDepth)
    {
        UINT expectedRemove = Mode("callback-close")
            || (Mode("callback-post-failure-close") && s_quits)
            || (Mode("callback-close-post-failure") && !s_quits)
            ? PM_NOREMOVE : PM_REMOVE;
        CHECK(message && message != s_message && ((uintptr_t)message & 7) == 0
            && !window && minimum == WM_QUIT && maximum == WM_QUIT
            && remove == expectedRemove && stack[5] == expectedRemove);
        CHECK(!message->hwnd && !message->message && !message->wParam && !message->lParam);
        Report('u');
        if (s_cursor < s_count && s_events[s_cursor].kind == WM_QUIT)
        {
            message->message = WM_QUIT; message->wParam = s_events[s_cursor].parameter;
            if (remove == PM_REMOVE) { ++s_cursor; }
            return -17;
        }
        return FALSE;
    }
    MessageAddress(message);
    SentMessages();
    CHECK(!window && !minimum && !maximum && remove == PM_REMOVE && stack[5] == PM_REMOVE && s_cursor < s_count);
    if (s_events[s_cursor].kind == UINT_MAX - 1) { ++s_cursor; s_drained = 1; Report('E'); return FALSE; }
    Next(message); Report('q'); return -7;
}
__declspec(dllexport) __declspec(noinline) BOOL WINAPI NativeFfiResizeRenderGetMessageW(
    MSG* message, HWND window, UINT minimum, UINT maximum)
{
    ABI(); MessageAddress(message);
    SentMessages();
    CHECK(!window && !minimum && !maximum && s_drained && s_cleared);
    Next(message); Report('w');
    return message->message == UINT_MAX ? -1 : message->message == WM_QUIT ? 0 : 0x7FFFFFFF;
}
__declspec(dllexport) BOOL WINAPI NativeFfiResizeRenderTranslateMessage(const MSG* message)
{
    CHECK(message == s_message && message->message != WM_QUIT && !s_drained);
    ++s_translates; Report('t'); return FALSE;
}
__declspec(dllexport) LRESULT WINAPI NativeFfiResizeRenderDispatchMessageW(const MSG* message)
{
    CHECK(message == s_message && message->message != WM_QUIT && s_translates == s_dispatches + 1);
    if ((Mode("callback-dispatch") || Mode("callback-dispatch-post-failure"))
        && message->message == WM_WINDOWPOSCHANGED)
    {
        s_extent.right = 1280; s_extent.bottom = 720; CallSize(SIZE_RESTORED);
    }
    if (message->message == WM_PAINT && message->hwnd == s_window)
    {
        CHECK(s_paints); --s_paints;
    }
    if (Mode("paint-final-rect") && message->message == WM_PAINT && s_extent.right == 1280)
    {
        s_extent.right = 1600; s_extent.bottom = 900;
    }
    if (Mode("paint-extent-change") && message->message == WM_PAINT)
    {
        s_extent.right = 1280; s_extent.bottom = 720;
    }
    ++s_dispatches; Report('f'); return -19;
}
__declspec(dllexport) BOOL WINAPI NativeFfiResizeRenderGetClientRect(HWND window, RECT* rectangle)
{
    CHECK(window == s_window && rectangle && !rectangle->left && !rectangle->top && !rectangle->right && !rectangle->bottom);
    if (s_mask)
    {
        CHECK(s_drained && s_mask == 127 && !s_paints && s_translates == s_dispatches);
        ++s_queries; Report('e');
        if (s_textureEvidence) { s_rectangle = rectangle; }
        if (FailureMode("query-error") && s_queries == 2) { return FALSE; }
        *rectangle = s_extent;
        if (s_queries == 2)
        {
            if (FailureMode("negative-width")) { rectangle->right = -1; }
            if (FailureMode("negative-height")) { rectangle->bottom = -1; }
            if (FailureMode("left")) { rectangle->left = 1; }
            if (FailureMode("top")) { rectangle->top = 1; }
        }
    }
    else { *rectangle = (RECT){ 0, 0, 800, 600 }; }
    return -9;
}
__declspec(dllexport) ATOM WINAPI NativeFfiResizeRenderRegisterClassExW(const WNDCLASSEXW* desc)
{
    CHECK(desc && desc->cbSize == sizeof(*desc) && desc->lpfnWndProc && !s_liveClass++);
    s_callback = desc->lpfnWndProc;
    Report('N'); return 123;
}
__declspec(dllexport) __declspec(noinline) LRESULT WINAPI NativeFfiResizeRenderDefWindowProcW(
    HWND window, UINT message, WPARAM parameter, LPARAM packed)
{
    ABI();
    CHECK(window == s_window && message == WM_NULL && parameter == (WPARAM)UINT64_C(0xFEDCBA9876543210)
        && packed == (LPARAM)UINT64_C(0x87654321FEDCBA98) && !s_posts && !s_quits);
    Report('d'); return (LRESULT)UINT64_C(0x8123456789ABCDEF);
}
__declspec(dllexport) HWND WINAPI NativeFfiResizeRenderCreateWindowExW(DWORD exStyle, LPCWSTR className,
    LPCWSTR title, DWORD style, int x, int y, int width, int height,
    HWND parent, HMENU menu, HINSTANCE instance, LPVOID parameter)
{
    CHECK(!exStyle && className && title && !(style & WS_VISIBLE) && x == 100 && y == 100
        && width == 800 && height == 600 && !parent && !menu && instance && !parameter && s_liveClass && !s_liveWindow++);
    Report('H');
    if (Mode("callback-create-post-failure")) { CallSize(SIZE_RESTORED); }
    if (Mode("callback-delegate"))
    {
        CHECK(s_callback(s_window, WM_NULL, (WPARAM)UINT64_C(0xFEDCBA9876543210),
            (LPARAM)UINT64_C(0x87654321FEDCBA98)) == (LRESULT)UINT64_C(0x8123456789ABCDEF));
    }
    return s_window;
}
__declspec(dllexport) int WINAPI NativeFfiResizeRenderShowWindow(HWND window, int command)
{
    CHECK(!s_textureEvidence && window == s_window && command == 5 && s_mask == 127); Report('h');
    if (Mode("callback-show") || Mode("callback-show-post-failure")) { CallSize(SIZE_RESTORED); }
    return FALSE;
}
__declspec(dllexport) __declspec(noinline) BOOL WINAPI NativeFfiResizeRenderSetWindowPos(
    HWND window, HWND insertAfter, int x, int y, int width, int height, UINT flags)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    ABI();
    CHECK(s_textureEvidence && window == s_window && !insertAfter && !x && !y
        && width == 1024 && height == 768 && flags == (SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE)
        && stack[5] == 1024 && stack[6] == 768 && stack[7] == flags
        && s_mask == 127 && s_cleared && s_frames == 1);
    Report('s');
    if (Mode("set-window-pos-failure")) { return FALSE; }
    s_extent.right = Mode("unchanged-width") || Mode("unchanged") ? 800 : 1024;
    s_extent.bottom = Mode("unchanged-height") || Mode("unchanged") ? 600 : 768;
    return -11;
}
__declspec(dllexport) BOOL WINAPI NativeFfiResizeRenderDestroyWindow(HWND window)
{
    CHECK(window == s_window && !s_loaded && !s_compilerLoaded && !s_mask && s_liveWindow);
    Report('W'); s_liveWindow = 0; return TRUE;
}
__declspec(dllexport) BOOL WINAPI NativeFfiResizeRenderUnregisterClassW(LPCWSTR name, HINSTANCE instance)
{
    CHECK(name && instance && !s_liveWindow && s_liveClass); Report('U'); s_liveClass = 0; return TRUE;
}
__declspec(dllexport) HMODULE WINAPI NativeFfiResizeRenderLoadLibraryExW(LPCWSTR name, HANDLE file, DWORD flags)
{
    CHECK(!file && flags == 0x800);
    HMODULE module = LoadLibraryExW(name, file, flags); CHECK(module);
    if (wcscmp(name, L"d3d11.dll") == 0) { CHECK(!s_loaded++); s_runtime = module; Report('1'); }
    else { CHECK(wcscmp(name, L"D3DCompiler_47.dll") == 0 && !s_compilerLoaded++); s_compiler = module; Report('2'); }
    return module;
}
__declspec(dllexport) FARPROC WINAPI NativeFfiResizeRenderGetProcAddress(HMODULE module, LPCSTR name)
{
    if (module == s_runtime)
    {
        CHECK(s_loaded && strcmp(name, "D3D11CreateDeviceAndSwapChain") == 0);
        return (FARPROC)Acquire;
    }
    CHECK(module == s_compiler && strcmp(name, "D3DCompileFromFile") == 0);
    return GetProcAddress(module, name);
}
__declspec(dllexport) BOOL WINAPI NativeFfiResizeRenderFreeLibrary(HMODULE module)
{
    if (module == s_runtime) { CHECK(s_loaded && !s_mask && !s_compilerLoaded); s_loaded = 0; Report('6'); }
    else { CHECK(module == s_compiler && s_compilerLoaded && s_mask == 15); s_compilerLoaded = 0; Report('j'); }
    return FreeLibrary(module);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance; (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        CHECK(GetEnvironmentVariableA("TERRUM_DX11_RESIZE_RENDER_MODE", s_mode, sizeof(s_mode)));
        CHECK(GetEnvironmentVariableA("TERRUM_DX11_RESIZE_RENDER_REPORT", s_report, sizeof(s_report)));
        s_textureEvidence = GetEnvironmentVariableA("TERRUM_DX11_RESIZE_TEXTURE_EVIDENCE", NULL, 0) != 0;
        s_viewportEvidence = GetEnvironmentVariableA("TERRUM_DX11_RESIZE_VIEWPORT_EVIDENCE", NULL, 0) != 0;
        s_sourceAuthoredViewportEvidence =
            GetEnvironmentVariableA("TERRUM_DX11_RESIZE_VIEWPORT_SOURCE_AUTHORED", NULL, 0) != 0;
        s_pixelEvidence = GetEnvironmentVariableA("TERRUM_DX11_RESIZE_PIXEL_EVIDENCE", NULL, 0) != 0;
        CHECK(!s_viewportEvidence || s_textureEvidence);
        CHECK(!s_sourceAuthoredViewportEvidence || s_viewportEvidence);
        CHECK(!s_pixelEvidence || s_viewportEvidence);
        CHECK(!s_pixelEvidence || s_sourceAuthoredViewportEvidence);
        // Independently supplied plans permit multiple frames at one swap-chain extent.
        s_frameCount = ReadPlan("TERRUM_DX11_RESIZE_RENDER_FRAMES", s_framePlan);
        s_resizeCount = ReadPlan("TERRUM_DX11_RESIZE_RENDER_RESIZES", s_resizePlan);
        BuildEvents();
        s_deviceTable.Release = ReleaseDevice; s_deviceTable.CreateBuffer = CreateBuffer;
        s_deviceTable.CreateInputLayout = CreateLayout; s_deviceTable.CreateVertexShader = CreateVertex;
        s_deviceTable.CreatePixelShader = CreatePixel; s_deviceTable.CreateRenderTargetView = CreateTarget;
        s_deviceTable.CreateTexture2D = CreateStaging;
        s_swapTable.Release = ReleaseSwap; s_swapTable.ResizeBuffers = Resize;
        s_swapTable.GetBuffer = GetBuffer; s_swapTable.Present = Present;
        s_contextTable.Release = ReleaseContext; s_contextTable.ClearRenderTargetView = ClearTarget;
        s_contextTable.OMSetRenderTargets = Targets; s_contextTable.RSSetViewports = Viewports;
        s_contextTable.RSGetViewports = GetViewports;
        s_contextTable.CopyResource = CopyPixels; s_contextTable.Map = MapPixels; s_contextTable.Unmap = UnmapPixels;
        s_contextTable.IASetInputLayout = Layout; s_contextTable.IASetVertexBuffers = Buffers;
        s_contextTable.IASetPrimitiveTopology = Topology; s_contextTable.VSSetShader = Vertex;
        s_contextTable.PSSetShader = Pixel; s_contextTable.Draw = Draw; s_contextTable.ClearState = ClearState;
        s_bufferTable.Release = ReleaseBuffer; s_layoutTable.Release = ReleaseLayout;
        s_vertexTable.Release = ReleaseVertex; s_pixelTable.Release = ReleasePixel;
        s_textureTable.Release = ReleaseTexture; s_textureTable.GetDesc = GetTextureDescription;
        s_stagingTable.Release = ReleaseStaging; s_staging.lpVtbl = &s_stagingTable;
        s_targetTable.Release = ReleaseTarget;
        s_device.lpVtbl = &s_deviceTable; s_swap.lpVtbl = &s_swapTable; s_context.lpVtbl = &s_contextTable;
        s_buffer.lpVtbl = &s_bufferTable; s_layout.lpVtbl = &s_layoutTable;
        s_vertex.lpVtbl = &s_vertexTable; s_pixel.lpVtbl = &s_pixelTable;
        s_texture.lpVtbl = &s_textureTable; s_target.lpVtbl = &s_targetTable;
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        CHECK(!s_mask && !s_loaded && !s_compilerLoaded && !s_liveWindow && !s_liveClass);
        CHECK(!s_stagingLive && !s_mapped && !s_pixelData);
        CHECK(s_translates == s_dispatches);
        CHECK(!s_paints);
        CHECK(s_acquisitions == s_frameCount);
        CHECK(s_resizes == s_resizeCount);
        Report('Z');
    }
    return TRUE;
}
