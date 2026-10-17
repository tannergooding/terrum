#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "warp output harness failed at line %d: %s\n", __LINE__, #condition); exit(97); } } while (0)

typedef HRESULT (WINAPI *test_compiler)(LPCWSTR, const D3D_SHADER_MACRO*, ID3DInclude*,
    LPCSTR, LPCSTR, UINT, UINT, ID3DBlob**, ID3DBlob**);
typedef HRESULT (WINAPI *test_create_blob)(SIZE_T, ID3DBlob**);

static HMODULE WINAPI load_compiler(LPCWSTR, HANDLE, DWORD);
static FARPROC WINAPI find_compiler(HMODULE, LPCSTR);
static BOOL WINAPI unload_compiler(HMODULE);
#ifndef WARP_PRESENTATION_TEST
static HRESULT WINAPI create_device(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
    const D3D_FEATURE_LEVEL*, UINT, UINT, ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);
#endif

#define LoadLibraryExW load_compiler
#define GetProcAddress find_compiler
#define FreeLibrary unload_compiler
#ifdef WARP_PRESENTATION_TEST
#define WARP_OUTPUT_UTILITIES_ONLY
#else
#define D3D11CreateDevice create_device
#endif
#include "native-dx11-warp-output.c"
#undef LoadLibraryExW
#undef GetProcAddress
#undef FreeLibrary
#undef D3D11CreateDevice
#undef WARP_OUTPUT_UTILITIES_ONLY

typedef enum failure_kind {
    no_failure,
    load_failure,
    export_failure,
    compile_distinct_failure,
    compile_alias_failure,
    compile_missing_code,
    compile_null_bytes,
    compile_empty_code,
    compile_empty_diagnostics,
    compile_warning,
    device_partial_failure,
    device_missing_device,
    device_missing_context,
    device_wrong_feature_level,
    create_partial_failure,
    create_missing_output,
    map_failure,
    map_null_data,
    map_short_pitch,
    map_padded_rows,
    pixel_rgb_lower_bound,
    pixel_rgb_upper_bound,
    pixel_rgb_too_low,
    pixel_rgb_too_high,
    pixel_alpha_mismatch,
    pixel_outside_mismatch
} failure_kind;

typedef struct device_proxy {
    ID3D11Device object;
    ID3D11DeviceVtbl vtable;
    ID3D11Device* real;
} device_proxy;

typedef struct context_proxy {
    ID3D11DeviceContext object;
    ID3D11DeviceContextVtbl vtable;
    ID3D11DeviceContext* real;
} context_proxy;

typedef struct blob_proxy {
    ID3DBlob object;
    ID3D10BlobVtbl vtable;
    ID3DBlob* real;
    UINT references;
    UINT acquired;
    UINT released;
    UINT shader;
    BOOL isCode;
    const void* bytes;
    SIZE_T size;
} blob_proxy;

static device_proxy device;
static context_proxy context;
static blob_proxy code[2];
static blob_proxy diagnostics[2];
static HMODULE loadedCompiler;
static test_compiler realCompiler;
static failure_kind failure;
static UINT failureStage;
static BOOL failUnload;
static UINT loadCalls, exportCalls, unloadCalls, compileCalls, deviceCalls, creationCalls;
static UINT clearStateCalls, contextReleaseCalls, deviceReleaseCalls;
static UINT bindingIndex, drawCalls, copyCalls, mapCalls, unmapCalls, realPixelChecks;
static UINT created[8], released[8];
static BOOL realMapped;
static char events[64];
static size_t eventCount;
static UINT caseCount;
#define WARP_OUTPUT_TEST_PITCH (WARP_OUTPUT_WIDTH * 4 + 384)
static BYTE paddedPixels[WARP_OUTPUT_HEIGHT * WARP_OUTPUT_TEST_PITCH];

static void record_event(char event)
{
    CHECK(eventCount + 1 < sizeof(events));
    events[eventCount++] = event;
    events[eventCount] = '\0';
}

/* Only wrapper-owned SDK tables are changed. Every forwarded receiver is real. */
#define CHILD_PROXY(name, type, table, stage) \
    typedef struct name##_proxy { type object; table vtable; type* real; } name##_proxy; \
    static name##_proxy name; \
    static ULONG STDMETHODCALLTYPE release_##name(type* object) \
    { \
        ULONG references; \
        CHECK(object == &name.object && name.real != NULL); \
        CHECK(clearStateCalls == 1 && contextReleaseCalls == 1 && !realMapped); \
        CHECK(++released[stage] == 1); \
        references = name.real->lpVtbl->Release(name.real); \
        name.real = NULL; \
        return references; \
    } \
    static type* wrap_##name(type* real) \
    { \
        CHECK(real != NULL && name.real == NULL && ++created[stage] == 1); \
        name.real = real; \
        name.vtable = *real->lpVtbl; \
        name.vtable.Release = release_##name; \
        name.object.lpVtbl = &name.vtable; \
        return &name.object; \
    }

CHILD_PROXY(vertexShader, ID3D11VertexShader, ID3D11VertexShaderVtbl, 1)
CHILD_PROXY(inputLayout, ID3D11InputLayout, ID3D11InputLayoutVtbl, 2)
CHILD_PROXY(vertexBuffer, ID3D11Buffer, ID3D11BufferVtbl, 3)
CHILD_PROXY(pixelShader, ID3D11PixelShader, ID3D11PixelShaderVtbl, 4)
CHILD_PROXY(target, ID3D11Texture2D, ID3D11Texture2DVtbl, 5)
CHILD_PROXY(targetView, ID3D11RenderTargetView, ID3D11RenderTargetViewVtbl, 6)
CHILD_PROXY(staging, ID3D11Texture2D, ID3D11Texture2DVtbl, 7)

#undef CHILD_PROXY

static LPVOID STDMETHODCALLTYPE get_blob_pointer(ID3DBlob* object)
{
    blob_proxy* proxy = (blob_proxy*)object;
    LPVOID bytes;
    CHECK(proxy->real != NULL && proxy->references != 0 && loadedCompiler != NULL);
    bytes = proxy->real->lpVtbl->GetBufferPointer(proxy->real);
    if (proxy->isCode)
    {
        CHECK(bytes == proxy->bytes);
        if (failure == compile_null_bytes && failureStage == proxy->shader)
        {
            return NULL;
        }
    }
    return bytes;
}

static SIZE_T STDMETHODCALLTYPE get_blob_size(ID3DBlob* object)
{
    blob_proxy* proxy = (blob_proxy*)object;
    SIZE_T size;
    CHECK(proxy->real != NULL && proxy->references != 0 && loadedCompiler != NULL);
    size = proxy->real->lpVtbl->GetBufferSize(proxy->real);
    CHECK(size == proxy->size);
    if (failureStage == proxy->shader
        && ((proxy->isCode && failure == compile_empty_code)
            || (!proxy->isCode && failure == compile_empty_diagnostics)))
    {
        return 0;
    }
    return size;
}

static ULONG STDMETHODCALLTYPE release_blob(ID3DBlob* object)
{
    blob_proxy* proxy = (blob_proxy*)object;
    ULONG references;
    CHECK(proxy->real != NULL && proxy->references != 0 && loadedCompiler != NULL);
    CHECK(device.real == NULL && context.real == NULL);
    CHECK(vertexShader.real == NULL && pixelShader.real == NULL && inputLayout.real == NULL);
    proxy->references--;
    proxy->released++;
    references = proxy->real->lpVtbl->Release(proxy->real);
    if (proxy->references == 0)
    {
        CHECK(references == 0);
        proxy->real = NULL;
    }
    return references;
}

static ID3DBlob* wrap_blob(blob_proxy* proxy, ID3DBlob* real, UINT shader, BOOL isCode)
{
    CHECK(proxy->real == NULL && proxy->references == 0 && real != NULL);
    proxy->real = real;
    proxy->vtable = *real->lpVtbl;
    proxy->vtable.GetBufferPointer = get_blob_pointer;
    proxy->vtable.GetBufferSize = get_blob_size;
    proxy->vtable.Release = release_blob;
    proxy->object.lpVtbl = &proxy->vtable;
    proxy->references = proxy->acquired = 1;
    proxy->shader = shader;
    proxy->isCode = isCode;
    proxy->bytes = real->lpVtbl->GetBufferPointer(real);
    proxy->size = real->lpVtbl->GetBufferSize(real);
    CHECK(proxy->bytes != NULL && proxy->size != 0);
    return &proxy->object;
}

static ID3DBlob* make_diagnostics(UINT shader)
{
    static const char text[] = "injected compiler diagnostic";
    FARPROC symbol = GetProcAddress(loadedCompiler, "D3DCreateBlob");
    test_create_blob createBlob;
    ID3DBlob* real = NULL;
    CHECK(symbol != NULL && sizeof(symbol) == sizeof(createBlob));
    memcpy(&createBlob, &symbol, sizeof(createBlob));
    CHECK(SUCCEEDED(createBlob(sizeof(text) - 1, &real)) && real != NULL);
    memcpy(real->lpVtbl->GetBufferPointer(real), text, sizeof(text) - 1);
    return wrap_blob(&diagnostics[shader], real, shader, FALSE);
}

static HRESULT WINAPI compile_shader(LPCWSTR path, const D3D_SHADER_MACRO* defines,
    ID3DInclude* include, LPCSTR entry, LPCSTR profile, UINT flags1, UINT flags2,
    ID3DBlob** codeOutput, ID3DBlob** diagnosticsOutput)
{
    ID3DBlob* realCode = NULL;
    ID3DBlob* realDiagnostics = NULL;
    UINT shader = compileCalls++;
    HRESULT result;
    CHECK(shader < 2 && *codeOutput == NULL && *diagnosticsOutput == NULL);
    CHECK(defines == NULL && include == NULL && flags1 == 0 && flags2 == 0);
    CHECK(strcmp(entry, shader == 0 ? "VertexMain" : "PixelMain") == 0);
    CHECK(strcmp(profile, shader == 0 ? "vs_5_0" : "ps_5_0") == 0);
    CHECK(wcsstr(path, L"\\triangle.hlsl") != NULL);
    record_event(shader == 0 ? 'v' : 'p');
    result = realCompiler(path, defines, include, entry, profile, flags1, flags2,
        &realCode, &realDiagnostics);
    CHECK(SUCCEEDED(result) && realCode != NULL && realDiagnostics == NULL);
    if (failureStage == shader && failure == compile_missing_code)
    {
        CHECK(realCode->lpVtbl->Release(realCode) == 0);
        *diagnosticsOutput = make_diagnostics(shader);
        return result;
    }
    *codeOutput = wrap_blob(&code[shader], realCode, shader, TRUE);
    CHECK(code[shader].size > 4 && memcmp(code[shader].bytes, "DXBC", 4) == 0);
    if (failureStage == shader)
    {
        if (failure == compile_alias_failure)
        {
            realCode->lpVtbl->AddRef(realCode);
            code[shader].references++;
            code[shader].acquired++;
            *diagnosticsOutput = *codeOutput;
            return E_INVALIDARG;
        }
        if (failure == compile_distinct_failure || failure == compile_empty_diagnostics
            || failure == compile_warning)
        {
            *diagnosticsOutput = make_diagnostics(shader);
        }
        if (failure == compile_distinct_failure)
        {
            return E_INVALIDARG;
        }
    }
    return result;
}

static HMODULE WINAPI load_compiler(LPCWSTR name, HANDLE file, DWORD flags)
{
    CHECK(wcscmp(name, L"D3DCompiler_47.dll") == 0 && file == NULL);
    CHECK(flags == LOAD_LIBRARY_SEARCH_SYSTEM32 && ++loadCalls == 1);
    if (failure == load_failure)
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
    FARPROC symbol;
    FARPROC proxySymbol;
    test_compiler proxy = compile_shader;
    CHECK(module == loadedCompiler && strcmp(name, "D3DCompileFromFile") == 0);
    CHECK(++exportCalls == 1);
    if (failure == export_failure)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return NULL;
    }
    symbol = GetProcAddress(module, name);
    CHECK(symbol != NULL && sizeof(symbol) == sizeof(realCompiler));
    memcpy(&realCompiler, &symbol, sizeof(realCompiler));
    memcpy(&proxySymbol, &proxy, sizeof(proxySymbol));
    return proxySymbol;
}

static void check_released(void)
{
    UINT shader;
    UINT resource;
    CHECK(device.real == NULL && context.real == NULL && !realMapped);
    CHECK(vertexShader.real == NULL && inputLayout.real == NULL && vertexBuffer.real == NULL);
    CHECK(pixelShader.real == NULL && target.real == NULL && targetView.real == NULL && staging.real == NULL);
    for (shader = 0; shader < 2; shader++)
    {
        CHECK(code[shader].real == NULL && code[shader].references == 0);
        CHECK(diagnostics[shader].real == NULL && diagnostics[shader].references == 0);
        CHECK(code[shader].released == code[shader].acquired);
        CHECK(diagnostics[shader].released == diagnostics[shader].acquired);
    }
    for (resource = 1; resource <= 7; resource++)
    {
        CHECK(created[resource] == released[resource]);
    }
}

static BOOL WINAPI unload_compiler(HMODULE module)
{
    CHECK(module == loadedCompiler && ++unloadCalls == 1);
    check_released();
    CHECK(FreeLibrary(module));
    loadedCompiler = NULL;
    if (failUnload)
    {
        SetLastError(ERROR_BUSY);
        return FALSE;
    }
    return TRUE;
}

static BOOL begin_create(ID3D11Device* object, UINT stage, char event)
{
    CHECK(object == &device.object && device.real != NULL);
    CHECK(++creationCalls == stage && clearStateCalls == 0);
    record_event(event);
    return failure == create_missing_output && failureStage == stage;
}

static HRESULT finish_create(UINT stage, HRESULT result)
{
    CHECK(SUCCEEDED(result));
    return failure == create_partial_failure && failureStage == stage ? E_INVALIDARG : result;
}

static void check_bytecode(UINT shader, const void* bytes, SIZE_T size)
{
    CHECK(code[shader].real != NULL && code[shader].references == 1);
    CHECK(bytes == code[shader].bytes && size == code[shader].size);
    CHECK(size > 4 && memcmp(bytes, "DXBC", 4) == 0);
}

static HRESULT STDMETHODCALLTYPE create_vertex_shader(ID3D11Device* object,
    const void* bytes, SIZE_T size, ID3D11ClassLinkage* linkage, ID3D11VertexShader** output)
{
    ID3D11VertexShader* real = NULL;
    HRESULT result;
    CHECK(*output == NULL && linkage == NULL);
    check_bytecode(0, bytes, size);
    if (begin_create(object, 1, 'V'))
    {
        return S_OK;
    }
    result = device.real->lpVtbl->CreateVertexShader(device.real, bytes, size, linkage, &real);
    CHECK(SUCCEEDED(result));
    *output = wrap_vertexShader(real);
    return finish_create(1, result);
}

static HRESULT STDMETHODCALLTYPE create_input_layout(ID3D11Device* object,
    const D3D11_INPUT_ELEMENT_DESC* description, UINT count, const void* bytes,
    SIZE_T size, ID3D11InputLayout** output)
{
    ID3D11InputLayout* real = NULL;
    HRESULT result;
    CHECK(*output == NULL && description != NULL && count == 1);
    CHECK(strcmp(description->SemanticName, "POSITION") == 0 && description->SemanticIndex == 0);
    CHECK(description->Format == DXGI_FORMAT_R32G32B32_FLOAT && description->InputSlot == 0);
    CHECK(description->AlignedByteOffset == 0 && description->InputSlotClass == D3D11_INPUT_PER_VERTEX_DATA);
    CHECK(description->InstanceDataStepRate == 0 && vertexShader.real != NULL);
    check_bytecode(0, bytes, size);
    if (begin_create(object, 2, 'L'))
    {
        return S_OK;
    }
    result = device.real->lpVtbl->CreateInputLayout(device.real, description, count, bytes, size, &real);
    CHECK(SUCCEEDED(result));
    *output = wrap_inputLayout(real);
    return finish_create(2, result);
}

static HRESULT STDMETHODCALLTYPE create_buffer(ID3D11Device* object,
    const D3D11_BUFFER_DESC* description, const D3D11_SUBRESOURCE_DATA* initial,
    ID3D11Buffer** output)
{
    static const float vertices[9] = {
        0.0f, 0.6f, 0.5f, 0.6f, -0.5f, 0.5f, -0.6f, -0.5f, 0.5f
    };
    ID3D11Buffer* real = NULL;
    HRESULT result;
    CHECK(*output == NULL && description != NULL && initial != NULL);
    CHECK(description->ByteWidth == 36 && description->Usage == D3D11_USAGE_IMMUTABLE);
    CHECK(description->BindFlags == D3D11_BIND_VERTEX_BUFFER && description->CPUAccessFlags == 0);
    CHECK(description->MiscFlags == 0 && description->StructureByteStride == 0);
    CHECK(initial->pSysMem != NULL && memcmp(initial->pSysMem, vertices, sizeof(vertices)) == 0);
    CHECK(initial->SysMemPitch == 0 && initial->SysMemSlicePitch == 0 && inputLayout.real != NULL);
    if (begin_create(object, 3, 'B'))
    {
        return S_OK;
    }
    result = device.real->lpVtbl->CreateBuffer(device.real, description, initial, &real);
    CHECK(SUCCEEDED(result));
    *output = wrap_vertexBuffer(real);
    return finish_create(3, result);
}

static HRESULT STDMETHODCALLTYPE create_pixel_shader(ID3D11Device* object,
    const void* bytes, SIZE_T size, ID3D11ClassLinkage* linkage, ID3D11PixelShader** output)
{
    ID3D11PixelShader* real = NULL;
    HRESULT result;
    CHECK(*output == NULL && linkage == NULL && vertexBuffer.real != NULL);
    check_bytecode(1, bytes, size);
    if (begin_create(object, 4, 'P'))
    {
        return S_OK;
    }
    result = device.real->lpVtbl->CreatePixelShader(device.real, bytes, size, linkage, &real);
    CHECK(SUCCEEDED(result));
    *output = wrap_pixelShader(real);
    return finish_create(4, result);
}

static HRESULT STDMETHODCALLTYPE create_texture(ID3D11Device* object,
    const D3D11_TEXTURE2D_DESC* description, const D3D11_SUBRESOURCE_DATA* initial,
    ID3D11Texture2D** output)
{
    ID3D11Texture2D* real = NULL;
    BOOL isStaging;
    UINT stage;
    HRESULT result;
    CHECK(description != NULL && initial == NULL && *output == NULL);
    CHECK(description->Width == WARP_OUTPUT_WIDTH && description->Height == WARP_OUTPUT_HEIGHT);
    CHECK(description->MipLevels == 1 && description->ArraySize == 1);
    CHECK(description->Format == DXGI_FORMAT_R8G8B8A8_UNORM);
    CHECK(description->SampleDesc.Count == 1 && description->SampleDesc.Quality == 0);
    CHECK(description->MiscFlags == 0);
    isStaging = creationCalls == 6;
    stage = isStaging ? 7u : 5u;
    CHECK(description->Usage == (isStaging ? D3D11_USAGE_STAGING : D3D11_USAGE_DEFAULT));
    CHECK(description->BindFlags == (isStaging ? 0u : D3D11_BIND_RENDER_TARGET));
    CHECK(description->CPUAccessFlags == (isStaging ? D3D11_CPU_ACCESS_READ : 0u));
    CHECK(isStaging ? targetView.real != NULL : pixelShader.real != NULL);
    if (begin_create(object, stage, isStaging ? 'S' : 'T'))
    {
        return S_OK;
    }
    result = device.real->lpVtbl->CreateTexture2D(device.real, description, initial, &real);
    CHECK(SUCCEEDED(result));
    *output = isStaging ? wrap_staging(real) : wrap_target(real);
    return finish_create(stage, result);
}

static HRESULT STDMETHODCALLTYPE create_target_view(ID3D11Device* object, ID3D11Resource* resource,
    const D3D11_RENDER_TARGET_VIEW_DESC* description, ID3D11RenderTargetView** output)
{
    ID3D11RenderTargetView* real = NULL;
    HRESULT result;
    CHECK(resource == (ID3D11Resource*)&target.object && target.real != NULL);
    CHECK(description == NULL && *output == NULL);
    if (begin_create(object, 6, 'R'))
    {
        return S_OK;
    }
    result = device.real->lpVtbl->CreateRenderTargetView(device.real,
        (ID3D11Resource*)target.real, description, &real);
    CHECK(SUCCEEDED(result));
    *output = wrap_targetView(real);
    return finish_create(6, result);
}

static void check_context(ID3D11DeviceContext* object)
{
    CHECK(object == &context.object && context.real != NULL);
}

static void binding(ID3D11DeviceContext* object, UINT index, char event)
{
    check_context(object);
    CHECK(creationCalls == 7 && created[7] == 1 && bindingIndex++ == index);
    CHECK(clearStateCalls == 0 && drawCalls == 0);
    record_event(event);
}

static void STDMETHODCALLTYPE clear_target(ID3D11DeviceContext* object,
    ID3D11RenderTargetView* view, const FLOAT* color)
{
    binding(object, 0, 'c');
    CHECK(view == &targetView.object && color != NULL);
    CHECK(color[0] == 0.0f && color[1] == 0.0f && color[2] == 0.0f && color[3] == 1.0f);
    context.real->lpVtbl->ClearRenderTargetView(context.real, targetView.real, color);
}

static void STDMETHODCALLTYPE set_targets(ID3D11DeviceContext* object, UINT count,
    ID3D11RenderTargetView* const* views, ID3D11DepthStencilView* depth)
{
    ID3D11RenderTargetView* realViews[1];
    binding(object, 1, 'o');
    CHECK(count == 1 && views != NULL && views[0] == &targetView.object && depth == NULL);
    realViews[0] = targetView.real;
    context.real->lpVtbl->OMSetRenderTargets(context.real, count, realViews, depth);
}

static void STDMETHODCALLTYPE set_pixel_shader(ID3D11DeviceContext* object,
    ID3D11PixelShader* shader, ID3D11ClassInstance* const* instances, UINT count)
{
    binding(object, 2, 'p');
    CHECK(shader == &pixelShader.object && instances == NULL && count == 0);
    context.real->lpVtbl->PSSetShader(context.real, pixelShader.real, instances, count);
}

static void STDMETHODCALLTYPE set_vertex_shader(ID3D11DeviceContext* object,
    ID3D11VertexShader* shader, ID3D11ClassInstance* const* instances, UINT count)
{
    binding(object, 3, 'v');
    CHECK(shader == &vertexShader.object && instances == NULL && count == 0);
    context.real->lpVtbl->VSSetShader(context.real, vertexShader.real, instances, count);
}

static void STDMETHODCALLTYPE set_input_layout(ID3D11DeviceContext* object, ID3D11InputLayout* layout)
{
    binding(object, 4, 'i');
    CHECK(layout == &inputLayout.object);
    context.real->lpVtbl->IASetInputLayout(context.real, inputLayout.real);
}

static void STDMETHODCALLTYPE set_vertex_buffers(ID3D11DeviceContext* object, UINT slot,
    UINT count, ID3D11Buffer* const* buffers, const UINT* strides, const UINT* offsets)
{
    ID3D11Buffer* realBuffers[1];
    binding(object, 5, 'a');
    CHECK(slot == 0 && count == 1 && buffers != NULL && buffers[0] == &vertexBuffer.object);
    CHECK(strides != NULL && strides[0] == 12 && offsets != NULL && offsets[0] == 0);
    realBuffers[0] = vertexBuffer.real;
    context.real->lpVtbl->IASetVertexBuffers(context.real, slot, count, realBuffers, strides, offsets);
}

static void STDMETHODCALLTYPE set_topology(ID3D11DeviceContext* object, D3D11_PRIMITIVE_TOPOLOGY topology)
{
    binding(object, 6, 't');
    CHECK(topology == D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context.real->lpVtbl->IASetPrimitiveTopology(context.real, topology);
}

static void STDMETHODCALLTYPE set_viewports(ID3D11DeviceContext* object, UINT count,
    const D3D11_VIEWPORT* viewports)
{
    binding(object, 7, 'w');
    CHECK(count == 1 && viewports != NULL && viewports[0].TopLeftX == 0.0f && viewports[0].TopLeftY == 0.0f);
    CHECK(viewports[0].Width == (FLOAT)WARP_OUTPUT_WIDTH && viewports[0].Height == (FLOAT)WARP_OUTPUT_HEIGHT);
    CHECK(viewports[0].MinDepth == 0.0f && viewports[0].MaxDepth == 1.0f);
    context.real->lpVtbl->RSSetViewports(context.real, count, viewports);
}

static void STDMETHODCALLTYPE draw(ID3D11DeviceContext* object, UINT count, UINT start)
{
    check_context(object);
    CHECK(count == 3 && start == 0 && bindingIndex == 8 && ++drawCalls == 1);
    record_event('D');
    context.real->lpVtbl->Draw(context.real, count, start);
}

static void STDMETHODCALLTYPE copy_resource(ID3D11DeviceContext* object,
    ID3D11Resource* destination, ID3D11Resource* source)
{
    check_context(object);
    CHECK(destination == (ID3D11Resource*)&staging.object && source == (ID3D11Resource*)&target.object);
    CHECK(drawCalls == 1 && ++copyCalls == 1);
    record_event('X');
    context.real->lpVtbl->CopyResource(context.real, (ID3D11Resource*)staging.real,
        (ID3D11Resource*)target.real);
}

static HRESULT STDMETHODCALLTYPE map_resource(ID3D11DeviceContext* object, ID3D11Resource* resource,
    UINT subresource, D3D11_MAP type, UINT flags, D3D11_MAPPED_SUBRESOURCE* output)
{
    static const int expected[4] = { 26, 204, 230, 255 };
    HRESULT result;
    const BYTE* inside;
    const BYTE* outside;
    UINT channel;
    UINT row;
    check_context(object);
    CHECK(resource == (ID3D11Resource*)&staging.object && subresource == 0);
    CHECK(type == D3D11_MAP_READ && flags == 0 && copyCalls == 1 && ++mapCalls == 1);
    CHECK(output != NULL && output->pData == NULL && output->RowPitch == 0 && output->DepthPitch == 0);
    record_event('M');
    if (failure == map_failure)
    {
        output->pData = paddedPixels;
        output->RowPitch = WARP_OUTPUT_TEST_PITCH;
        return E_INVALIDARG;
    }
    result = context.real->lpVtbl->Map(context.real, (ID3D11Resource*)staging.real,
        subresource, type, flags, output);
    CHECK(SUCCEEDED(result) && output->pData != NULL && output->RowPitch >= WARP_OUTPUT_WIDTH * 4 && !realMapped);
    realMapped = TRUE;
    outside = (const BYTE*)output->pData;
    inside = outside + (SIZE_T)(WARP_OUTPUT_HEIGHT / 2) * output->RowPitch + (WARP_OUTPUT_WIDTH / 2) * 4;
    for (channel = 0; channel < 4; channel++)
    {
        int difference = (int)inside[channel] - expected[channel];
        CHECK(difference >= (channel == 3 ? 0 : -1) && difference <= (channel == 3 ? 0 : 1));
        CHECK(outside[channel] == (channel == 3 ? 255 : 0));
    }
    realPixelChecks++;
    if (failure == no_failure)
    {
        printf("real pixels: (%u,%u)=(%u,%u,%u,%u); (0,0)=(%u,%u,%u,%u)\n",
            WARP_OUTPUT_WIDTH / 2, WARP_OUTPUT_HEIGHT / 2,
            inside[0], inside[1], inside[2], inside[3], outside[0], outside[1], outside[2], outside[3]);
    }
    if (failure == map_null_data)
    {
        output->pData = NULL;
    }
    else if (failure == map_short_pitch)
    {
        output->RowPitch = failureStage;
    }
    else if (failure >= map_padded_rows)
    {
        BYTE* modifiedInside;
        /* Repack actual WARP rows, keeping poison padding so tight-pitch addressing fails. */
        memset(paddedPixels, 0xCD, sizeof(paddedPixels));
        for (row = 0; row < WARP_OUTPUT_HEIGHT; row++)
        {
            memcpy(paddedPixels + row * WARP_OUTPUT_TEST_PITCH, outside + (SIZE_T)row * output->RowPitch,
                WARP_OUTPUT_WIDTH * 4);
        }
        output->pData = paddedPixels;
        output->RowPitch = WARP_OUTPUT_TEST_PITCH;
        modifiedInside = paddedPixels + (WARP_OUTPUT_HEIGHT / 2) * WARP_OUTPUT_TEST_PITCH
            + (WARP_OUTPUT_WIDTH / 2) * 4;
        if (failure >= pixel_rgb_lower_bound && failure <= pixel_rgb_too_high)
        {
            int delta = failure == pixel_rgb_lower_bound ? -1
                : failure == pixel_rgb_upper_bound ? 1 : failure == pixel_rgb_too_low ? -2 : 2;
            CHECK(failureStage < 3);
            modifiedInside[failureStage] = (BYTE)(expected[failureStage] + delta);
        }
        else if (failure == pixel_alpha_mismatch)
        {
            modifiedInside[3] = 254;
        }
        else if (failure == pixel_outside_mismatch)
        {
            CHECK(failureStage < 4);
            paddedPixels[failureStage] = failureStage == 3 ? 254 : 1;
        }
    }
    return result;
}

static void STDMETHODCALLTYPE unmap_resource(ID3D11DeviceContext* object,
    ID3D11Resource* resource, UINT subresource)
{
    check_context(object);
    CHECK(resource == (ID3D11Resource*)&staging.object && subresource == 0);
    CHECK(realMapped && mapCalls == 1 && ++unmapCalls == 1 && clearStateCalls == 0);
    record_event('U');
    context.real->lpVtbl->Unmap(context.real, (ID3D11Resource*)staging.real, subresource);
    realMapped = FALSE;
}

static void STDMETHODCALLTYPE clear_context(ID3D11DeviceContext* object)
{
    check_context(object);
    CHECK(!realMapped && ++clearStateCalls == 1);
    CHECK(mapCalls == 0 || unmapCalls == (failure == map_failure ? 0u : 1u));
    record_event('C');
    context.real->lpVtbl->ClearState(context.real);
}

static ULONG STDMETHODCALLTYPE release_context(ID3D11DeviceContext* object)
{
    ULONG references;
    check_context(object);
    CHECK(clearStateCalls == 1 && ++contextReleaseCalls == 1 && !realMapped);
    references = context.real->lpVtbl->Release(context.real);
    context.real = NULL;
    return references;
}

static ULONG STDMETHODCALLTYPE release_device(ID3D11Device* object)
{
    ULONG references;
    CHECK(object == &device.object && device.real != NULL && context.real == NULL);
    CHECK(++deviceReleaseCalls == 1);
    CHECK(vertexShader.real == NULL && inputLayout.real == NULL && vertexBuffer.real == NULL);
    CHECK(pixelShader.real == NULL && target.real == NULL && targetView.real == NULL && staging.real == NULL);
    references = device.real->lpVtbl->Release(device.real);
    CHECK(references == 0);
    device.real = NULL;
    return references;
}

#ifndef WARP_PRESENTATION_TEST
static HRESULT WINAPI create_device(IDXGIAdapter* adapter, D3D_DRIVER_TYPE driverType,
    HMODULE software, UINT flags, const D3D_FEATURE_LEVEL* levels, UINT count, UINT version,
    ID3D11Device** deviceOutput, D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** contextOutput)
{
    HRESULT result;
    CHECK(adapter == NULL && driverType == D3D_DRIVER_TYPE_WARP && software == NULL && flags == 0);
    CHECK(levels != NULL && count == 1 && levels[0] == D3D_FEATURE_LEVEL_11_0);
    CHECK(version == D3D11_SDK_VERSION && *deviceOutput == NULL && *contextOutput == NULL && *selected == 0);
    CHECK(compileCalls == 2 && ++deviceCalls == 1 && device.real == NULL && context.real == NULL);
    record_event('d');
    result = D3D11CreateDevice(adapter, driverType, software, flags, levels, count, version,
        &device.real, selected, &context.real);
    CHECK(SUCCEEDED(result) && device.real != NULL && context.real != NULL);
    CHECK(*selected == D3D_FEATURE_LEVEL_11_0);
    device.vtable = *device.real->lpVtbl;
    device.vtable.CreateVertexShader = create_vertex_shader;
    device.vtable.CreateInputLayout = create_input_layout;
    device.vtable.CreateBuffer = create_buffer;
    device.vtable.CreatePixelShader = create_pixel_shader;
    device.vtable.CreateTexture2D = create_texture;
    device.vtable.CreateRenderTargetView = create_target_view;
    device.vtable.Release = release_device;
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
    context.vtable.Map = map_resource;
    context.vtable.Unmap = unmap_resource;
    context.vtable.ClearState = clear_context;
    context.vtable.Release = release_context;
    context.object.lpVtbl = &context.vtable;
    *deviceOutput = &device.object;
    *contextOutput = &context.object;
    if (failure == device_partial_failure)
    {
        return E_INVALIDARG;
    }
    if (failure == device_missing_device)
    {
        CHECK(device.real->lpVtbl->Release(device.real) != 0);
        device.real = NULL;
        *deviceOutput = NULL;
    }
    else if (failure == device_missing_context)
    {
        CHECK(context.real->lpVtbl->Release(context.real) == 0);
        context.real = NULL;
        *contextOutput = NULL;
    }
    else if (failure == device_wrong_feature_level)
    {
        *selected = D3D_FEATURE_LEVEL_10_0;
    }
    return result;
}
#endif

static void reset_case(void)
{
    check_released();
    CHECK(loadedCompiler == NULL);
    memset(code, 0, sizeof(code));
    memset(diagnostics, 0, sizeof(diagnostics));
    memset(created, 0, sizeof(created));
    memset(released, 0, sizeof(released));
    loadCalls = exportCalls = unloadCalls = compileCalls = deviceCalls = creationCalls = 0;
    clearStateCalls = contextReleaseCalls = deviceReleaseCalls = 0;
    bindingIndex = drawCalls = copyCalls = mapCalls = unmapCalls = realPixelChecks = 0;
    eventCount = 0;
    events[0] = '\0';
    failure = no_failure;
    failureStage = 0;
    failUnload = FALSE;
}

#ifndef WARP_PRESENTATION_TEST
static void run_case(const char* name, failure_kind kind, UINT stage, BOOL unloadFailure,
    HRESULT expectedResult)
{
    static const char fullEvents[] = "vpdVLBPTRScopviatwDXMUC";
    char expectedEvents[64];
    UINT expectedCompileCalls = 2;
    UINT expectedDeviceCalls = 1;
    UINT expectedCreationCalls = 7;
    UINT expectedContext = 1;
    UINT expectedMapCalls = 1;
    UINT resource;
    HRESULT result;
    reset_case();
    failure = kind;
    failureStage = stage;
    failUnload = unloadFailure;
    result = NativeFfiRenderTriangleWarp();
    CHECK(result == expectedResult);
    strcpy_s(expectedEvents, sizeof(expectedEvents), fullEvents);
    if (kind == load_failure || kind == export_failure)
    {
        expectedCompileCalls = expectedDeviceCalls = expectedCreationCalls = expectedContext = expectedMapCalls = 0;
        expectedEvents[0] = '\0';
    }
    else if (kind >= compile_distinct_failure && kind <= compile_empty_diagnostics)
    {
        expectedCompileCalls = stage + 1;
        expectedDeviceCalls = expectedCreationCalls = expectedContext = expectedMapCalls = 0;
        expectedEvents[expectedCompileCalls] = '\0';
    }
    else if (kind >= device_partial_failure && kind <= device_wrong_feature_level)
    {
        expectedCreationCalls = expectedMapCalls = 0;
        expectedContext = kind == device_missing_context ? 0u : 1u;
        strcpy_s(expectedEvents, sizeof(expectedEvents), expectedContext ? "vpdC" : "vpd");
    }
    else if (kind == create_partial_failure || kind == create_missing_output)
    {
        expectedCreationCalls = stage;
        expectedMapCalls = 0;
        expectedEvents[3 + stage] = 'C';
        expectedEvents[4 + stage] = '\0';
    }
    else if (kind == map_failure)
    {
        strcpy_s(expectedEvents, sizeof(expectedEvents), "vpdVLBPTRScopviatwDXMC");
    }
    CHECK(strcmp(events, expectedEvents) == 0);
    CHECK(loadCalls == 1 && exportCalls == (kind == load_failure ? 0u : 1u));
    CHECK(unloadCalls == (kind == load_failure ? 0u : 1u));
    CHECK(compileCalls == expectedCompileCalls && deviceCalls == expectedDeviceCalls);
    CHECK(creationCalls == expectedCreationCalls);
    CHECK(clearStateCalls == expectedContext && contextReleaseCalls == expectedContext);
    CHECK(deviceReleaseCalls == (expectedDeviceCalls && kind != device_missing_device ? 1u : 0u));
    CHECK(bindingIndex == (expectedMapCalls ? 8u : 0u));
    CHECK(drawCalls == expectedMapCalls && copyCalls == expectedMapCalls && mapCalls == expectedMapCalls);
    CHECK(unmapCalls == (kind == map_failure ? 0u : expectedMapCalls));
    CHECK(realPixelChecks == unmapCalls);
    for (resource = 1; resource <= 7; resource++)
    {
        UINT expected = resource <= expectedCreationCalls
            && !(kind == create_missing_output && stage == resource) ? 1u : 0u;
        CHECK(created[resource] == expected && released[resource] == expected);
    }
    check_released();
    CHECK(loadedCompiler == NULL);
    printf("case %s[%u]: 0x%08lX; events=%s; maps=%u; unmaps=%u\n",
        name, stage, (unsigned long)result, events, mapCalls, unmapCalls);
    fflush(stdout);
    caseCount++;
}

int main(int argc, char** argv)
{
    UINT stage;
    CHECK(argc == 1 || (argc == 2 && strcmp(argv[1], "preloaded-shadow") == 0));
    if (argc == 2)
    {
        WCHAR path[32768];
        WCHAR* separator;
        HMODULE shadow;
        reset_case();
        CHECK(GetModuleFileNameW(NULL, path, 32768) != 0);
        separator = wcsrchr(path, L'\\');
        CHECK(separator != NULL);
        memcpy(separator + 1, L"D3DCompiler_47.dll", sizeof(L"D3DCompiler_47.dll"));
        shadow = LoadLibraryExW(path, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
        CHECK(shadow != NULL);
        CHECK(NativeFfiRenderTriangleWarp() == HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED));
        CHECK(loadCalls == 1 && exportCalls == 0 && unloadCalls == 1 && compileCalls == 0);
        check_released();
        CHECK(FreeLibrary(shadow) && GetModuleHandleW(L"D3DCompiler_47.dll") == NULL);
        puts("preloaded-shadow: rejected before export resolution");
        return 0;
    }
    run_case("real-output", no_failure, 0, FALSE, S_OK);
    run_case("load", load_failure, 0, FALSE, (HRESULT)0x8007007E);
    run_case("export", export_failure, 0, FALSE, (HRESULT)0x8007007F);
    for (stage = 0; stage < 2; stage++)
    {
        run_case("compile-distinct", compile_distinct_failure, stage, FALSE, E_INVALIDARG);
        run_case("compile-alias", compile_alias_failure, stage, FALSE, E_INVALIDARG);
        run_case("compile-missing", compile_missing_code, stage, FALSE, E_UNEXPECTED);
        run_case("compile-null-bytes", compile_null_bytes, stage, FALSE, E_UNEXPECTED);
        run_case("compile-empty", compile_empty_code, stage, FALSE, E_UNEXPECTED);
        run_case("compile-empty-diagnostics", compile_empty_diagnostics, stage, FALSE, E_UNEXPECTED);
        run_case("compile-warning", compile_warning, stage, FALSE, S_OK);
    }
    run_case("device-partial", device_partial_failure, 0, FALSE, E_INVALIDARG);
    run_case("device-missing", device_missing_device, 0, FALSE, E_UNEXPECTED);
    run_case("context-missing", device_missing_context, 0, FALSE, E_UNEXPECTED);
    run_case("feature-level", device_wrong_feature_level, 0, FALSE, E_UNEXPECTED);
    for (stage = 1; stage <= 7; stage++)
    {
        run_case("create-partial", create_partial_failure, stage, FALSE, E_INVALIDARG);
        run_case("create-missing", create_missing_output, stage, FALSE, E_UNEXPECTED);
    }
    run_case("map-failed", map_failure, 0, FALSE, E_INVALIDARG);
    run_case("map-null", map_null_data, 0, FALSE, E_UNEXPECTED);
    run_case("pitch-zero", map_short_pitch, 0, FALSE, E_UNEXPECTED);
    run_case("pitch-short", map_short_pitch, 127, FALSE, E_UNEXPECTED);
    run_case("padded-rows", map_padded_rows, 0, FALSE, S_OK);
    for (stage = 0; stage < 3; stage++)
    {
        run_case("rgb-lower-bound", pixel_rgb_lower_bound, stage, FALSE, S_OK);
        run_case("rgb-upper-bound", pixel_rgb_upper_bound, stage, FALSE, S_OK);
        run_case("rgb-too-low", pixel_rgb_too_low, stage, FALSE, E_FAIL);
        run_case("rgb-too-high", pixel_rgb_too_high, stage, FALSE, E_FAIL);
    }
    run_case("alpha", pixel_alpha_mismatch, 0, FALSE, E_FAIL);
    for (stage = 0; stage < 4; stage++)
    {
        run_case("outside", pixel_outside_mismatch, stage, FALSE, E_FAIL);
    }
    run_case("unload", no_failure, 0, TRUE, (HRESULT)0x800700AA);
    run_case("compile-and-unload", compile_distinct_failure, 1, TRUE, E_INVALIDARG);
    run_case("create-and-unload", create_partial_failure, 6, TRUE, E_INVALIDARG);
    run_case("map-and-unload", map_failure, 0, TRUE, E_INVALIDARG);
    run_case("pixels-and-unload", pixel_alpha_mismatch, 0, TRUE, E_FAIL);
    printf("%u wrapper cases verified: real WARP pixels, exact calls, RowPitch, Map/Unmap, and cleanup\n",
        caseCount);
    return 0;
}
#endif
