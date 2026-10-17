#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include <intrin.h>
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This test-only error-injection fixture requires the Windows x64 ABI.
#endif

static ID3D11DeviceVtbl s_deviceTable;
static IDXGISwapChainVtbl s_swapTable;
static ID3D11DeviceContextVtbl s_contextTable;
static ID3D11BufferVtbl s_bufferTable;
static ID3D11VertexShaderVtbl s_vertexTable;
static ID3D11PixelShaderVtbl s_pixelTable;
static ID3D11InputLayoutVtbl s_layoutTable;
static ID3D10BlobVtbl s_blobTable;
static ID3D11Device s_device;
static IDXGISwapChain s_swap;
static ID3D11DeviceContext s_context;
static ID3D11Buffer s_buffer;
static ID3D11VertexShader s_vertex;
static ID3D11PixelShader s_pixel;
static ID3D11InputLayout s_layout;
static ID3DBlob s_code;
static ID3DBlob s_diagnostics;
static unsigned s_mask;
static unsigned s_mode;
static unsigned s_compiles;
static unsigned s_vertexCreates;
static unsigned s_pixelCreates;
static unsigned s_layoutCreates;
static unsigned s_codeReferences;
static unsigned s_diagnosticReferences;
static HWND s_window;
static char s_report[MAX_PATH];
static const unsigned char s_bytes[8] = { 0x44, 0x58, 0x42, 0x43, 1, 2, 3, 4 };
/* A synthetic extent with high bits checks SIZE_T transport; the fixture never reads the loan. */
#define TEST_SHADER_EXTENT ((SIZE_T)0x100000008ULL)

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

static void ReleaseOwner(unsigned bit, char event)
{
    unsigned expected = (s_mask & 32) ? 32 : (s_mask & 16) ? 16 : (s_mask & 64) ? 64 : (s_mask & 8) ? 8
        : (s_mask & 4) ? 4 : (s_mask & 2) ? 2 : (s_mask & 1) ? 1 : 0;
    /* A failed CreateInputLayout may release its partial output while the original blob owner is still live. */
    if (bit != expected || (bit != 64 && s_codeReferences) || s_diagnosticReferences || !IsWindow(s_window))
    {
        __fastfail(7);
    }
    s_mask &= ~bit;
    Report(event);
}

#define DEFINE_RELEASE(name, type, object, bit, event) \
static ULONG STDMETHODCALLTYPE name(type* receiver) \
{ \
    if (receiver != &object) { __fastfail(7); } \
    ReleaseOwner(bit, event); \
    return 0; \
}

DEFINE_RELEASE(ReleaseDevice, ID3D11Device, s_device, 1, 'D')
DEFINE_RELEASE(ReleaseSwap, IDXGISwapChain, s_swap, 2, 'S')
DEFINE_RELEASE(ReleaseContext, ID3D11DeviceContext, s_context, 4, 'C')
DEFINE_RELEASE(ReleaseBuffer, ID3D11Buffer, s_buffer, 8, 'b')
DEFINE_RELEASE(ReleaseVertex, ID3D11VertexShader, s_vertex, 16, 'x')
DEFINE_RELEASE(ReleasePixel, ID3D11PixelShader, s_pixel, 32, 'y')
DEFINE_RELEASE(ReleaseInputLayout, ID3D11InputLayout, s_layout, 64, 'l')

static ULONG STDMETHODCALLTYPE AddRefBlob(ID3DBlob* object)
{
    if (object != &s_code || s_codeReferences != 1 || s_diagnosticReferences)
    {
        __fastfail(7);
    }
    Report('a');
    return ++s_codeReferences;
}

static ULONG STDMETHODCALLTYPE ReleaseBlob(ID3DBlob* object)
{
    if (object == &s_code && s_codeReferences)
    {
        Report(s_compiles == 1 ? 'v' : 'p');
        return --s_codeReferences;
    }
    if (object == &s_diagnostics && s_diagnosticReferences)
    {
        Report('E');
        return --s_diagnosticReferences;
    }
    __fastfail(7);
    return 0;
}

static LPVOID STDMETHODCALLTYPE BlobPointer(ID3DBlob* object)
{
    unsigned expected = s_compiles == 1 && !s_layoutCreates ? 2 : 1;
    if (object != &s_code || s_codeReferences != expected || s_diagnosticReferences)
    {
        __fastfail(7);
    }
    return s_mode == 12 ? NULL : (LPVOID)s_bytes;
}

static SIZE_T STDMETHODCALLTYPE BlobSize(ID3DBlob* object)
{
    unsigned expected = s_compiles == 1 && !s_layoutCreates ? 2 : 1;
    if (object != &s_code || s_codeReferences != expected || s_diagnosticReferences)
    {
        __fastfail(7);
    }
    return s_mode == 13 ? 0 : TEST_SHADER_EXTENT;
}

static HRESULT STDMETHODCALLTYPE CreateBuffer(ID3D11Device* object,
    const D3D11_BUFFER_DESC* descriptor, const D3D11_SUBRESOURCE_DATA* data, ID3D11Buffer** output)
{
    if (object != &s_device || s_mask != 7 || !descriptor || descriptor->ByteWidth != 36
        || descriptor->Usage != D3D11_USAGE_IMMUTABLE || !data || !data->pSysMem || !output || *output)
    {
        __fastfail(7);
    }
    s_mask |= 8;
    *output = &s_buffer;
    Report('B');
    return S_OK;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreateInputLayout(ID3D11Device* object,
    const D3D11_INPUT_ELEMENT_DESC* description, UINT count, const void* signature,
    SIZE_T signatureLength, ID3D11InputLayout** output)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    if (object != &s_device || s_mask != 15 || s_layoutCreates++ || s_compiles != 1
        || s_vertexCreates || s_pixelCreates || !description || count != 1
        || !description->SemanticName || strcmp(description->SemanticName, "POSITION")
        || description->SemanticIndex != 0 || description->Format != DXGI_FORMAT_R32G32B32_FLOAT
        || description->InputSlot != 0 || description->AlignedByteOffset != 0
        || description->InputSlotClass != D3D11_INPUT_PER_VERTEX_DATA
        || description->InstanceDataStepRate != 0 || signature != s_bytes
        || signatureLength != TEST_SHADER_EXTENT || !output || *output
        || s_codeReferences != 2 || s_diagnosticReferences || ((uintptr_t)stack & 15) != 8
        || stack[5] != TEST_SHADER_EXTENT || stack[6] != (uintptr_t)output
        || ((uintptr_t)output & 7) || (uintptr_t)output < (uintptr_t)(stack + 7))
    {
        __fastfail(7);
    }
    Report('I');
    if (s_mode != 22 && s_mode != 23)
    {
        s_mask |= 64;
        *output = &s_layout;
    }
    return s_mode == 21 || s_mode == 22 ? E_FAIL : s_mode == 24 ? S_FALSE : S_OK;
}

static void CheckShader(ID3D11Device* object, const void* bytes, SIZE_T size,
    ID3D11ClassLinkage* linkage, void* const* output, const uintptr_t* stack, unsigned mask)
{
    if (object != &s_device || s_mask != mask || bytes != s_bytes || size != TEST_SHADER_EXTENT || linkage
        || !output || *output || s_codeReferences != 1 || s_diagnosticReferences
        || ((uintptr_t)stack & 15) != 8 || stack[5] != (uintptr_t)output
        || ((uintptr_t)output & 7) || (uintptr_t)output < (uintptr_t)(stack + 6))
    {
        __fastfail(7);
    }
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreateVertex(ID3D11Device* object,
    const void* bytes, SIZE_T size, ID3D11ClassLinkage* linkage, ID3D11VertexShader** output)
{
    CheckShader(object, bytes, size, linkage, (void* const*)output,
        (const uintptr_t*)_AddressOfReturnAddress(), 79);
    if (s_vertexCreates++ || s_compiles != 1)
    {
        __fastfail(7);
    }
    Report('X');
    if (s_mode != 7 && s_mode != 16)
    {
        s_mask |= 16;
        *output = &s_vertex;
    }
    return s_mode == 6 || s_mode == 16 ? E_FAIL : s_mode == 1 ? S_FALSE
        : s_mode == 2 ? (HRESULT)0x7FFFFFFF : S_OK;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE CreatePixel(ID3D11Device* object,
    const void* bytes, SIZE_T size, ID3D11ClassLinkage* linkage, ID3D11PixelShader** output)
{
    CheckShader(object, bytes, size, linkage, (void* const*)output,
        (const uintptr_t*)_AddressOfReturnAddress(), 95);
    if (s_pixelCreates++ || s_compiles != 2)
    {
        __fastfail(7);
    }
    Report('Y');
    if (s_mode != 11 && s_mode != 17)
    {
        s_mask |= 32;
        *output = &s_pixel;
    }
    return s_mode == 10 || s_mode == 17 ? E_FAIL : s_mode == 19 ? (HRESULT)0x80000000U
        : s_mode == 1 ? S_FALSE : s_mode == 2 ? (HRESULT)0x7FFFFFFF : S_OK;
}

static HRESULT STDMETHODCALLTYPE GetBuffer(IDXGISwapChain* object, UINT index, REFIID iid, void** output)
{
    if (object != &s_swap || index || !iid || !output || *output || s_mask != 127
        || s_codeReferences || s_diagnosticReferences || s_compiles != 2
        || s_vertexCreates != 1 || s_pixelCreates != 1 || s_layoutCreates != 1)
    {
        __fastfail(7);
    }
    Report('G');
    return E_FAIL;
}

__declspec(dllexport) __declspec(noinline) HRESULT WINAPI NativeFfiDirectShaderCompile(
    LPCWSTR file, const D3D_SHADER_MACRO* defines, ID3DInclude* include, LPCSTR entry, LPCSTR target,
    UINT flags1, UINT flags2, ID3DBlob** code, ID3DBlob** diagnostics)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    unsigned pixel = s_compiles == 1;
    if (++s_compiles > 2 || !file || wcscmp(file, L"triangle.hlsl") || defines || include
        || !entry || strcmp(entry, pixel ? "PixelMain" : "VertexMain")
        || !target || strcmp(target, pixel ? "ps_5_0" : "vs_5_0") || flags1 || flags2
        || !code || !diagnostics || *code || *diagnostics || code == diagnostics
        || s_codeReferences || s_diagnosticReferences || s_mask != (pixel ? 95U : 15U)
        || ((uintptr_t)stack & 15) != 8 || stack[5] != (uintptr_t)target
        || stack[6] || stack[7] || stack[8] != (uintptr_t)code || stack[9] != (uintptr_t)diagnostics
        || ((uintptr_t)code & 7) || ((uintptr_t)diagnostics & 7)
        || ((uintptr_t)code < (uintptr_t)diagnostics + sizeof(*diagnostics)
            && (uintptr_t)diagnostics < (uintptr_t)code + sizeof(*code))
        || (uintptr_t)code < (uintptr_t)(stack + 10)
        || (uintptr_t)diagnostics < (uintptr_t)(stack + 10))
    {
        __fastfail(7);
    }
    Report(pixel ? 'P' : 'V');
    if (!pixel && s_mode == 3)
    {
        return E_FAIL;
    }
    if ((!pixel && (s_mode == 5 || s_mode == 20)) || (pixel && s_mode == 9))
    {
        s_diagnosticReferences = 1;
        *diagnostics = &s_diagnostics;
        return s_mode == 20 ? S_FALSE : S_OK;
    }
    s_codeReferences = 1;
    *code = &s_code;
    if (s_mode == 14 || s_mode == 15)
    {
        s_codeReferences++;
        *diagnostics = &s_code;
    }
    else
    {
        s_diagnosticReferences = 1;
        *diagnostics = &s_diagnostics;
    }
    return (!pixel && (s_mode == 4 || s_mode == 15)) || (pixel && s_mode == 8) ? E_FAIL
        : (!pixel && s_mode == 18) ? (HRESULT)0x80000000U
        : s_mode == 1 ? S_FALSE : s_mode == 2 ? (HRESULT)0x7FFFFFFF : S_OK;
}

__declspec(dllexport) HRESULT WINAPI NativeFfiDirectShaderAcquisition(
    IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL* levels, UINT levelCount, UINT sdk,
    const DXGI_SWAP_CHAIN_DESC* descriptor, IDXGISwapChain** swap, ID3D11Device** device,
    D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** context)
{
    if (s_mask || adapter || driver != D3D_DRIVER_TYPE_WARP || software || flags || !levels
        || levelCount != 1 || *levels != D3D_FEATURE_LEVEL_11_0 || sdk != D3D11_SDK_VERSION
        || !descriptor || !IsWindow(descriptor->OutputWindow)
        || !descriptor->BufferDesc.Width || !descriptor->BufferDesc.Height
        || !swap || !device || !selected || !context || *swap || *device || *selected || *context)
    {
        __fastfail(7);
    }
    s_mask = 7;
    s_window = descriptor->OutputWindow;
    *swap = &s_swap;
    *device = &s_device;
    *context = &s_context;
    *selected = D3D_FEATURE_LEVEL_11_0;
    Report('A');
    return S_OK;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    char mode[16];
    (void)instance; (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        if (!GetEnvironmentVariableA("TERRUM_DX11_SHADER_MODE", mode, sizeof(mode))
            || !GetEnvironmentVariableA("TERRUM_DX11_SHADER_REPORT", s_report, sizeof(s_report)))
        {
            return FALSE;
        }
        s_mode = (unsigned)strtoul(mode, NULL, 10);
        s_deviceTable.Release = ReleaseDevice;
        s_deviceTable.CreateBuffer = CreateBuffer;
        s_deviceTable.CreateInputLayout = CreateInputLayout;
        s_deviceTable.CreateVertexShader = CreateVertex;
        s_deviceTable.CreatePixelShader = CreatePixel;
        s_swapTable.Release = ReleaseSwap;
        s_swapTable.GetBuffer = GetBuffer;
        s_contextTable.Release = ReleaseContext;
        s_bufferTable.Release = ReleaseBuffer;
        s_vertexTable.Release = ReleaseVertex;
        s_pixelTable.Release = ReleasePixel;
        s_layoutTable.Release = ReleaseInputLayout;
        s_blobTable.AddRef = AddRefBlob;
        s_blobTable.Release = ReleaseBlob;
        s_blobTable.GetBufferPointer = BlobPointer;
        s_blobTable.GetBufferSize = BlobSize;
        s_device.lpVtbl = &s_deviceTable;
        s_swap.lpVtbl = &s_swapTable;
        s_context.lpVtbl = &s_contextTable;
        s_buffer.lpVtbl = &s_bufferTable;
        s_vertex.lpVtbl = &s_vertexTable;
        s_pixel.lpVtbl = &s_pixelTable;
        s_layout.lpVtbl = &s_layoutTable;
        s_code.lpVtbl = &s_blobTable;
        s_diagnostics.lpVtbl = &s_blobTable;
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        if (s_mask || s_codeReferences || s_diagnosticReferences || IsWindow(s_window))
        {
            __fastfail(7);
        }
        Report('Z');
    }
    return TRUE;
}
