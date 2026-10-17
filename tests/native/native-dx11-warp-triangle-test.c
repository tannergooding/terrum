#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "warp triangle harness failed at line %d: %s\n", __LINE__, #condition); exit(97); } } while (0)

typedef HRESULT (WINAPI *test_compiler)(LPCWSTR, const D3D_SHADER_MACRO*, ID3DInclude*,
    LPCSTR, LPCSTR, UINT, UINT, ID3DBlob**, ID3DBlob**);
typedef HRESULT (WINAPI *test_create_blob)(SIZE_T, ID3DBlob**);

typedef struct device_wrapper {
    ID3D11Device object;
    ID3D11DeviceVtbl vtable;
    ID3D11Device* real;
} device_wrapper;

typedef struct context_wrapper {
    ID3D11DeviceContext object;
    ID3D11DeviceContextVtbl vtable;
    ID3D11DeviceContext* real;
} context_wrapper;

typedef struct vertex_shader_wrapper {
    ID3D11VertexShader object;
    ID3D11VertexShaderVtbl vtable;
    ID3D11VertexShader* real;
} vertex_shader_wrapper;

typedef struct input_layout_wrapper {
    ID3D11InputLayout object;
    ID3D11InputLayoutVtbl vtable;
    ID3D11InputLayout* real;
} input_layout_wrapper;

typedef struct buffer_wrapper {
    ID3D11Buffer object;
    ID3D11BufferVtbl vtable;
    ID3D11Buffer* real;
} buffer_wrapper;

typedef struct blob_wrapper {
    ID3DBlob object;
    ID3D10BlobVtbl vtable;
    ID3D10BlobVtbl realVtable;
    ID3DBlob* real;
    UINT references;
} blob_wrapper;

/* Copy complete SDK tables and forward intercepted calls through saved real interfaces. */
static ID3D11Device* realDevice;
static ID3D11DeviceVtbl originalDeviceVtable;
static device_wrapper deviceProxy;
static ID3D11DeviceContext* realContext;
static ID3D11DeviceContextVtbl originalContextVtable;
static context_wrapper contextProxy;
static ID3D11VertexShader* realVertexShader;
static ID3D11VertexShaderVtbl originalVertexShaderVtable;
static vertex_shader_wrapper vertexShaderProxy;
static ID3D11InputLayout* realInputLayout;
static ID3D11InputLayoutVtbl originalInputLayoutVtable;
static input_layout_wrapper inputLayoutProxy;
static ID3D11Buffer* realVertexBuffer;
static ID3D11BufferVtbl originalVertexBufferVtable;
static buffer_wrapper vertexBufferProxy;
static ID3DBlob* realCodeBlob;
static ID3DBlob* realDiagnosticsBlob;
static blob_wrapper codeBlobProxy;
static blob_wrapper diagnosticsBlobProxy;
static HMODULE loadedCompiler;
static test_compiler realCompiler;
static UINT mode;
static UINT loadCalls;
static UINT exportCalls;
static UINT unloadCalls;
static UINT deviceCalls;
static UINT vertexShaderCalls;
static UINT inputLayoutCalls;
static UINT vertexBufferCalls;
static UINT clearStateCalls;
static UINT contextReleaseCalls;
static UINT deviceReleaseCalls;
static UINT earlyDeviceReleaseCalls;
static UINT drawCalls;
static UINT bindingCalls;
static UINT vertexShaderReleases;
static UINT inputLayoutReleases;
static UINT vertexBufferReleases;
static UINT codeBlobReleases;
static UINT diagnosticsBlobReleases;
static UINT diagnosticsBlobCreates;
static UINT bindingIndex;
static ULONG finalContextReferences;
static ULONG finalDeviceReferences;
static const void* liveBytecode;
static SIZE_T liveBytecodeSize;
static char events[32];
static size_t eventCount;

static const float expectedVertices[9] = {
    0.0f, 0.6f, 0.5f,
    0.6f, -0.5f, 0.5f,
    -0.6f, -0.5f, 0.5f,
};

static LPVOID STDMETHODCALLTYPE get_blob_pointer(ID3DBlob* blob);
static SIZE_T STDMETHODCALLTYPE get_blob_size(ID3DBlob* blob);
static ULONG STDMETHODCALLTYPE release_blob(ID3DBlob* blob);

static void record_event(char event)
{
    CHECK(eventCount + 1 < sizeof(events));
    events[eventCount++] = event;
    events[eventCount] = '\0';
}

static ID3DBlob* wrap_blob(blob_wrapper* proxy, ID3DBlob* real)
{
    CHECK(proxy->real == NULL && proxy->references == 0 && real != NULL);
    proxy->realVtable = *real->lpVtbl;
    proxy->vtable = proxy->realVtable;
    proxy->vtable.GetBufferPointer = get_blob_pointer;
    proxy->vtable.GetBufferSize = get_blob_size;
    proxy->vtable.Release = release_blob;
    proxy->object.lpVtbl = &proxy->vtable;
    proxy->real = real;
    proxy->references = 1;
    return &proxy->object;
}

static ID3DBlob* create_diagnostics_blob(void)
{
    FARPROC symbol;
    test_create_blob createBlob;
    ID3DBlob* blob = NULL;
    symbol = GetProcAddress(loadedCompiler, "D3DCreateBlob");
    CHECK(symbol != NULL);
    memcpy(&createBlob, &symbol, sizeof(createBlob));
    CHECK(SUCCEEDED(createBlob(16, &blob)) && blob != NULL);
    diagnosticsBlobCreates++;
    return blob;
}

static LPVOID STDMETHODCALLTYPE get_blob_pointer(ID3DBlob* object)
{
    blob_wrapper* proxy = (blob_wrapper*)object;
    LPVOID pointer;
    CHECK(loadedCompiler != NULL && proxy->real != NULL && proxy->references != 0);
    pointer = proxy->realVtable.GetBufferPointer(proxy->real);
    if (proxy == &codeBlobProxy)
    {
        CHECK(pointer != NULL);
        if (liveBytecode == NULL)
        {
            liveBytecode = pointer;
        }
        CHECK(pointer == liveBytecode);
    }
    return pointer;
}

static SIZE_T STDMETHODCALLTYPE get_blob_size(ID3DBlob* object)
{
    blob_wrapper* proxy = (blob_wrapper*)object;
    SIZE_T size;
    CHECK(loadedCompiler != NULL && proxy->real != NULL && proxy->references != 0);
    size = proxy->realVtable.GetBufferSize(proxy->real);
    if (proxy == &codeBlobProxy)
    {
        CHECK(size != 0);
        if (liveBytecodeSize == 0)
        {
            liveBytecodeSize = size;
        }
        CHECK(size == liveBytecodeSize);
        if (mode == 15)
        {
            return 0;
        }
    }
    return size;
}

static ULONG STDMETHODCALLTYPE release_blob(ID3DBlob* object)
{
    blob_wrapper* proxy = (blob_wrapper*)object;
    int isDiagnostics;
    ULONG references;
    CHECK(loadedCompiler != NULL && proxy->real != NULL && proxy->references != 0);
    isDiagnostics = proxy == &diagnosticsBlobProxy
        || (proxy == &codeBlobProxy && mode == 13 && proxy->references == 2);
    if (isDiagnostics)
    {
        diagnosticsBlobReleases++;
        if (proxy == &diagnosticsBlobProxy && proxy->references == 1)
        {
            realDiagnosticsBlob = NULL;
        }
        else
        {
            CHECK(proxy == &codeBlobProxy && mode == 13 && proxy->references == 2);
            realDiagnosticsBlob = NULL;
        }
    }
    else
    {
        CHECK(proxy == &codeBlobProxy && vertexShaderReleases >= vertexShaderCalls);
        codeBlobReleases++;
        realCodeBlob = NULL;
    }
    proxy->references--;
    references = proxy->realVtable.Release(proxy->real);
    if (proxy->references == 0)
    {
        proxy->real = NULL;
    }
    return references;
}

static HRESULT WINAPI compile_shader(LPCWSTR path, const D3D_SHADER_MACRO* defines,
    ID3DInclude* include, LPCSTR entry, LPCSTR target, UINT flags1, UINT flags2,
    ID3DBlob** code, ID3DBlob** diagnostics)
{
    ID3DBlob* realCode = NULL;
    ID3DBlob* realDiagnostics = NULL;
    ID3DBlob* codeOutput;
    ID3DBlob* diagnosticsOutput = NULL;
    HRESULT result;
    CHECK(*code == NULL && *diagnostics == NULL && defines == NULL && include == NULL);
    CHECK(strcmp(entry, "VertexMain") == 0 && strcmp(target, "vs_5_0") == 0);
    CHECK(flags1 == 0 && flags2 == 0 && wcsstr(path, L"\\triangle.hlsl") != NULL);
    result = realCompiler(path, defines, include, entry, target, flags1, flags2,
        &realCode, &realDiagnostics);
    CHECK(SUCCEEDED(result) && realCode != NULL && realDiagnostics == NULL);
    realCodeBlob = realCode;
    codeOutput = wrap_blob(&codeBlobProxy, realCode);
    if (mode == 12 || mode == 13)
    {
        if (mode == 12)
        {
            realDiagnosticsBlob = create_diagnostics_blob();
            diagnosticsOutput = wrap_blob(&diagnosticsBlobProxy, realDiagnosticsBlob);
            CHECK(codeOutput != diagnosticsOutput);
        }
        else
        {
            realCode->lpVtbl->AddRef(realCode);
            codeBlobProxy.references++;
            realDiagnosticsBlob = realCode;
            diagnosticsOutput = codeOutput;
            CHECK(codeOutput == diagnosticsOutput);
        }
        *code = codeOutput;
        *diagnostics = diagnosticsOutput;
        return mode == 12 ? (HRESULT)0x80070057L : (HRESULT)0x80004005L;
    }
    if (mode == 14)
    {
        CHECK(realCode->lpVtbl->Release(realCode) == 0);
        codeBlobProxy.real = NULL;
        codeBlobProxy.references = 0;
        realCodeBlob = NULL;
        realDiagnosticsBlob = create_diagnostics_blob();
        *diagnostics = wrap_blob(&diagnosticsBlobProxy, realDiagnosticsBlob);
        return result;
    }
    *code = codeOutput;
    if (mode == 15)
    {
        realDiagnosticsBlob = create_diagnostics_blob();
        *diagnostics = wrap_blob(&diagnosticsBlobProxy, realDiagnosticsBlob);
        CHECK(*code != *diagnostics);
    }
    return result;
}

static HMODULE WINAPI load_compiler(LPCWSTR name, HANDLE file, DWORD flags)
{
    CHECK(wcscmp(name, L"D3DCompiler_47.dll") == 0 && file == NULL);
    CHECK(flags == LOAD_LIBRARY_SEARCH_SYSTEM32 && ++loadCalls == 1);
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
    symbol = GetProcAddress(module, name);
    CHECK(symbol != NULL);
    memcpy(&realCompiler, &symbol, sizeof(realCompiler));
    memcpy(&proxySymbol, &proxy, sizeof(proxySymbol));
    return proxySymbol;
}

static BOOL WINAPI unload_compiler(HMODULE module)
{
    BOOL result;
    CHECK(module == loadedCompiler && ++unloadCalls == 1);
    CHECK(realCodeBlob == NULL && realDiagnosticsBlob == NULL);
    CHECK(contextReleaseCalls == (mode == 2 ? 0u : (deviceCalls ? 1u : 0u)));
    CHECK(realContext == NULL && realDevice == NULL);
    CHECK(diagnosticsBlobCreates == (mode == 12 || mode == 14 || mode == 15 ? 1u : 0u));
    if (exportCalls == 0)
    {
        CHECK(codeBlobReleases == 0 && diagnosticsBlobReleases == 0);
    }
    else if (mode == 12 || mode == 13 || mode == 15)
    {
        CHECK(codeBlobReleases == 1 && diagnosticsBlobReleases == 1);
    }
    else if (mode == 14)
    {
        CHECK(codeBlobReleases == 0 && diagnosticsBlobReleases == 1);
    }
    else
    {
        CHECK(codeBlobReleases == 1 && diagnosticsBlobReleases == 0);
    }
    result = FreeLibrary(module);
    CHECK(result);
    loadedCompiler = NULL;
    if (mode == 10 || mode == 11)
    {
        SetLastError(ERROR_BUSY);
        return FALSE;
    }
    return result;
}

static ULONG STDMETHODCALLTYPE release_vertex_shader(ID3D11VertexShader* shader)
{
    ULONG references;
    CHECK(shader == &vertexShaderProxy.object && vertexShaderProxy.real == realVertexShader);
    CHECK(realVertexShader != NULL);
    CHECK((clearStateCalls == 1 && contextReleaseCalls <= 1)
        || (mode == 7 && clearStateCalls == 0 && contextReleaseCalls == 0));
    vertexShaderReleases++;
    references = originalVertexShaderVtable.Release(realVertexShader);
    if (references == 0)
    {
        realVertexShader = NULL;
        vertexShaderProxy.real = NULL;
    }
    return references;
}

static ULONG STDMETHODCALLTYPE release_input_layout(ID3D11InputLayout* layout)
{
    ULONG references;
    CHECK(layout == &inputLayoutProxy.object && inputLayoutProxy.real == realInputLayout);
    CHECK(realInputLayout != NULL);
    CHECK((clearStateCalls == 1 && contextReleaseCalls <= 1)
        || (mode == 8 && clearStateCalls == 0 && contextReleaseCalls == 0));
    inputLayoutReleases++;
    references = originalInputLayoutVtable.Release(realInputLayout);
    if (references == 0)
    {
        realInputLayout = NULL;
        inputLayoutProxy.real = NULL;
    }
    return references;
}

static ULONG STDMETHODCALLTYPE release_vertex_buffer(ID3D11Buffer* buffer)
{
    ULONG references;
    CHECK(buffer == &vertexBufferProxy.object && vertexBufferProxy.real == realVertexBuffer);
    CHECK(realVertexBuffer != NULL);
    CHECK((clearStateCalls == 1 && contextReleaseCalls <= 1)
        || (mode == 9 && clearStateCalls == 0 && contextReleaseCalls == 0));
    vertexBufferReleases++;
    references = originalVertexBufferVtable.Release(realVertexBuffer);
    if (references == 0)
    {
        realVertexBuffer = NULL;
        vertexBufferProxy.real = NULL;
    }
    return references;
}

static HRESULT STDMETHODCALLTYPE create_vertex_shader(ID3D11Device* device,
    const void* bytecode, SIZE_T bytecodeSize, ID3D11ClassLinkage* linkage,
    ID3D11VertexShader** output)
{
    ID3D11VertexShader* created = NULL;
    HRESULT result;
    CHECK(device == &deviceProxy.object && deviceProxy.real == realDevice && realDevice != NULL);
    CHECK(realCodeBlob != NULL && codeBlobReleases == 0 && *output == NULL);
    CHECK(bytecode == liveBytecode && bytecodeSize == liveBytecodeSize);
    CHECK(bytecode != NULL && bytecodeSize > 4 && memcmp(bytecode, "DXBC", 4) == 0);
    CHECK(linkage == NULL && ++vertexShaderCalls == 1);
    record_event('V');
    result = originalDeviceVtable.CreateVertexShader(realDevice, bytecode, bytecodeSize,
        linkage, &created);
    CHECK(SUCCEEDED(result) && created != NULL);
    realVertexShader = created;
    originalVertexShaderVtable = *realVertexShader->lpVtbl;
    vertexShaderProxy.real = realVertexShader;
    vertexShaderProxy.vtable = originalVertexShaderVtable;
    vertexShaderProxy.vtable.Release = release_vertex_shader;
    vertexShaderProxy.object.lpVtbl = &vertexShaderProxy.vtable;
    *output = &vertexShaderProxy.object;
    if (mode == 4 || mode == 11)
    {
        return (HRESULT)0x80004005L;
    }
    if (mode == 7)
    {
        CHECK(release_vertex_shader(&vertexShaderProxy.object) == 0);
        *output = NULL;
    }
    return result;
}

static HRESULT STDMETHODCALLTYPE create_input_layout(ID3D11Device* device,
    const D3D11_INPUT_ELEMENT_DESC* description, UINT count, const void* bytecode,
    SIZE_T bytecodeSize, ID3D11InputLayout** output)
{
    ID3D11InputLayout* created = NULL;
    HRESULT result;
    CHECK(device == &deviceProxy.object && deviceProxy.real == realDevice && realDevice != NULL);
    CHECK(vertexShaderCalls == 1 && realVertexShader != NULL && codeBlobReleases == 0);
    CHECK(description != NULL && count == 1 && description->SemanticName != NULL);
    CHECK(strcmp(description->SemanticName, "POSITION") == 0);
    CHECK(description->SemanticIndex == 0 && description->Format == DXGI_FORMAT_R32G32B32_FLOAT);
    CHECK(description->InputSlot == 0 && description->AlignedByteOffset == 0);
    CHECK(description->InputSlotClass == D3D11_INPUT_PER_VERTEX_DATA
        && description->InstanceDataStepRate == 0);
    CHECK(bytecode == liveBytecode && bytecodeSize == liveBytecodeSize && *output == NULL);
    CHECK(bytecode != NULL && bytecodeSize > 4 && memcmp(bytecode, "DXBC", 4) == 0);
    CHECK(++inputLayoutCalls == 1);
    record_event('L');
    result = originalDeviceVtable.CreateInputLayout(realDevice, description, count, bytecode,
        bytecodeSize, &created);
    CHECK(SUCCEEDED(result) && created != NULL);
    realInputLayout = created;
    originalInputLayoutVtable = *realInputLayout->lpVtbl;
    inputLayoutProxy.real = realInputLayout;
    inputLayoutProxy.vtable = originalInputLayoutVtable;
    inputLayoutProxy.vtable.Release = release_input_layout;
    inputLayoutProxy.object.lpVtbl = &inputLayoutProxy.vtable;
    *output = &inputLayoutProxy.object;
    if (mode == 5)
    {
        return (HRESULT)0x80004005L;
    }
    if (mode == 8)
    {
        CHECK(release_input_layout(&inputLayoutProxy.object) == 0);
        *output = NULL;
    }
    return result;
}

static HRESULT STDMETHODCALLTYPE create_vertex_buffer(ID3D11Device* device,
    const D3D11_BUFFER_DESC* description, const D3D11_SUBRESOURCE_DATA* initialData,
    ID3D11Buffer** output)
{
    ID3D11Buffer* created = NULL;
    HRESULT result;
    CHECK(device == &deviceProxy.object && deviceProxy.real == realDevice && realDevice != NULL);
    CHECK(inputLayoutCalls == 1 && realInputLayout != NULL && *output == NULL);
    CHECK(description != NULL && description->ByteWidth == 36);
    CHECK(description->Usage == D3D11_USAGE_IMMUTABLE);
    CHECK(description->BindFlags == D3D11_BIND_VERTEX_BUFFER);
    CHECK(description->CPUAccessFlags == 0 && description->MiscFlags == 0
        && description->StructureByteStride == 0);
    CHECK(initialData != NULL && initialData->pSysMem != NULL);
    CHECK(memcmp(initialData->pSysMem, expectedVertices, sizeof(expectedVertices)) == 0);
    CHECK(initialData->SysMemPitch == 0 && initialData->SysMemSlicePitch == 0);
    CHECK(++vertexBufferCalls == 1);
    record_event('B');
    result = originalDeviceVtable.CreateBuffer(realDevice, description, initialData, &created);
    CHECK(SUCCEEDED(result) && created != NULL);
    realVertexBuffer = created;
    originalVertexBufferVtable = *realVertexBuffer->lpVtbl;
    vertexBufferProxy.real = realVertexBuffer;
    vertexBufferProxy.vtable = originalVertexBufferVtable;
    vertexBufferProxy.vtable.Release = release_vertex_buffer;
    vertexBufferProxy.object.lpVtbl = &vertexBufferProxy.vtable;
    *output = &vertexBufferProxy.object;
    if (mode == 6)
    {
        return (HRESULT)0x80004005L;
    }
    if (mode == 9)
    {
        CHECK(release_vertex_buffer(&vertexBufferProxy.object) == 0);
        *output = NULL;
    }
    return result;
}

static ULONG STDMETHODCALLTYPE release_context(ID3D11DeviceContext* context)
{
    ULONG references;
    CHECK(context == &contextProxy.object && contextProxy.real == realContext && realContext != NULL);
    CHECK(clearStateCalls == 1 && ++contextReleaseCalls == 1);
    references = originalContextVtable.Release(realContext);
    finalContextReferences = references;
    if (references == 0)
    {
        realContext = NULL;
        contextProxy.real = NULL;
    }
    return references;
}

static ULONG STDMETHODCALLTYPE release_device(ID3D11Device* device)
{
    ULONG references;
    CHECK(device == &deviceProxy.object && deviceProxy.real == realDevice && realDevice != NULL);
    if (realContext != NULL)
    {
        CHECK(contextReleaseCalls == 1 && clearStateCalls == 1);
    }
    deviceReleaseCalls++;
    references = originalDeviceVtable.Release(realDevice);
    finalDeviceReferences = references;
    if (references == 0)
    {
        realDevice = NULL;
        deviceProxy.real = NULL;
    }
    contextProxy.real = NULL;
    realContext = NULL;
    return references;
}

static void STDMETHODCALLTYPE clear_context(ID3D11DeviceContext* context)
{
    CHECK(context == &contextProxy.object && contextProxy.real == realContext
        && realContext != NULL && ++clearStateCalls == 1);
    if (mode == 0 || mode == 10)
    {
        CHECK(bindingIndex == 5 && drawCalls == 1);
    }
    else
    {
        CHECK(drawCalls == 0);
    }
    record_event('C');
    originalContextVtable.ClearState(realContext);
}

static void STDMETHODCALLTYPE set_vertex_shader(ID3D11DeviceContext* context,
    ID3D11VertexShader* shader, ID3D11ClassInstance* const* instances, UINT instanceCount)
{
    CHECK(context == &contextProxy.object && contextProxy.real == realContext
        && realContext != NULL && shader == &vertexShaderProxy.object);
    CHECK(instances == NULL && instanceCount == 0 && bindingIndex == 0);
    CHECK(mode == 0 || mode == 10);
    bindingIndex++;
    bindingCalls++;
    record_event('S');
    originalContextVtable.VSSetShader(realContext, realVertexShader, instances, instanceCount);
}

static void STDMETHODCALLTYPE set_input_layout(ID3D11DeviceContext* context,
    ID3D11InputLayout* layout)
{
    CHECK(context == &contextProxy.object && contextProxy.real == realContext
        && realContext != NULL && layout == &inputLayoutProxy.object);
    CHECK(bindingIndex == 1 && (mode == 0 || mode == 10));
    bindingIndex++;
    bindingCalls++;
    record_event('I');
    originalContextVtable.IASetInputLayout(realContext, realInputLayout);
}

static void STDMETHODCALLTYPE set_vertex_buffers(ID3D11DeviceContext* context,
    UINT startSlot, UINT bufferCount, ID3D11Buffer* const* buffers,
    const UINT* strides, const UINT* offsets)
{
    ID3D11Buffer* realBuffers[1];
    CHECK(context == &contextProxy.object && contextProxy.real == realContext
        && realContext != NULL);
    CHECK(startSlot == 0 && bufferCount == 1 && buffers != NULL);
    CHECK(buffers[0] == &vertexBufferProxy.object && strides != NULL && strides[0] == 12);
    CHECK(offsets != NULL && offsets[0] == 0 && bindingIndex == 2);
    CHECK(mode == 0 || mode == 10);
    bindingIndex++;
    bindingCalls++;
    record_event('A');
    realBuffers[0] = realVertexBuffer;
    originalContextVtable.IASetVertexBuffers(realContext, startSlot, bufferCount,
        realBuffers, strides, offsets);
}

static void STDMETHODCALLTYPE set_topology(ID3D11DeviceContext* context,
    D3D11_PRIMITIVE_TOPOLOGY topology)
{
    CHECK(context == &contextProxy.object && contextProxy.real == realContext
        && realContext != NULL);
    CHECK(topology == D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST && bindingIndex == 3);
    CHECK(mode == 0 || mode == 10);
    bindingIndex++;
    bindingCalls++;
    record_event('T');
    originalContextVtable.IASetPrimitiveTopology(realContext, topology);
}

static void STDMETHODCALLTYPE set_viewports(ID3D11DeviceContext* context,
    UINT count, const D3D11_VIEWPORT* viewports)
{
    CHECK(context == &contextProxy.object && contextProxy.real == realContext
        && realContext != NULL && count == 1 && viewports != NULL);
    CHECK(viewports[0].TopLeftX == 0.0f && viewports[0].TopLeftY == 0.0f);
    CHECK(viewports[0].Width == 1.0f && viewports[0].Height == 1.0f);
    CHECK(viewports[0].MinDepth == 0.0f && viewports[0].MaxDepth == 1.0f);
    CHECK(bindingIndex == 4 && (mode == 0 || mode == 10));
    bindingIndex++;
    bindingCalls++;
    record_event('R');
    originalContextVtable.RSSetViewports(realContext, count, viewports);
}

static void STDMETHODCALLTYPE draw(ID3D11DeviceContext* context,
    UINT vertexCount, UINT startVertex)
{
    CHECK(context == &contextProxy.object && contextProxy.real == realContext
        && realContext != NULL);
    CHECK(vertexCount == 3 && startVertex == 0 && bindingIndex == 5);
    CHECK(mode == 0 || mode == 10);
    CHECK(++drawCalls == 1);
    record_event('D');
    originalContextVtable.Draw(realContext, vertexCount, startVertex);
}

static HRESULT WINAPI create_device(IDXGIAdapter* adapter, D3D_DRIVER_TYPE driverType,
    HMODULE software, UINT flags, const D3D_FEATURE_LEVEL* levels, UINT levelCount,
    UINT version, ID3D11Device** output, D3D_FEATURE_LEVEL* selected,
    ID3D11DeviceContext** contextOutput)
{
    ID3D11DeviceContext* createdContext = NULL;
    HRESULT result;
    CHECK(adapter == NULL && driverType == D3D_DRIVER_TYPE_WARP && software == NULL && flags == 0);
    CHECK(levels != NULL && levelCount == 1 && levels[0] == D3D_FEATURE_LEVEL_11_0);
    CHECK(version == D3D11_SDK_VERSION && output != NULL && *output == NULL);
    CHECK(selected != NULL && *selected == 0 && contextOutput != NULL && *contextOutput == NULL);
    CHECK(++deviceCalls == 1 && realDevice == NULL && realContext == NULL);
    result = D3D11CreateDevice(adapter, driverType, software, flags, levels, levelCount,
        version, &realDevice, selected, &createdContext);
    CHECK(SUCCEEDED(result) && realDevice != NULL && createdContext != NULL);
    CHECK(*selected == D3D_FEATURE_LEVEL_11_0);
    realContext = createdContext;
    originalDeviceVtable = *realDevice->lpVtbl;
    deviceProxy.real = realDevice;
    deviceProxy.vtable = originalDeviceVtable;
    deviceProxy.vtable.CreateVertexShader = create_vertex_shader;
    deviceProxy.vtable.CreateInputLayout = create_input_layout;
    deviceProxy.vtable.CreateBuffer = create_vertex_buffer;
    deviceProxy.vtable.Release = release_device;
    deviceProxy.object.lpVtbl = &deviceProxy.vtable;
    originalContextVtable = *realContext->lpVtbl;
    contextProxy.real = realContext;
    contextProxy.vtable = originalContextVtable;
    contextProxy.vtable.VSSetShader = set_vertex_shader;
    contextProxy.vtable.Draw = draw;
    contextProxy.vtable.IASetInputLayout = set_input_layout;
    contextProxy.vtable.IASetVertexBuffers = set_vertex_buffers;
    contextProxy.vtable.IASetPrimitiveTopology = set_topology;
    contextProxy.vtable.RSSetViewports = set_viewports;
    contextProxy.vtable.ClearState = clear_context;
    contextProxy.vtable.Release = release_context;
    contextProxy.object.lpVtbl = &contextProxy.vtable;
    *output = &deviceProxy.object;
    *contextOutput = &contextProxy.object;
    if (mode == 1)
    {
        return (HRESULT)0x80004005L;
    }
    if (mode == 2)
    {
        CHECK(originalContextVtable.Release(realContext) == 0);
        realContext = NULL;
        contextProxy.real = NULL;
        *contextOutput = NULL;
    }
    else if (mode == 3)
    {
        CHECK(originalDeviceVtable.Release(realDevice) != 0);
        earlyDeviceReleaseCalls++;
        realDevice = NULL;
        deviceProxy.real = NULL;
        *output = NULL;
    }
    return result;
}

#define LoadLibraryExW load_compiler
#define GetProcAddress find_compiler
#define FreeLibrary unload_compiler
#define D3D11CreateDevice create_device
#include "native-dx11-warp-triangle.c"
#undef LoadLibraryExW
#undef GetProcAddress
#undef FreeLibrary
#undef D3D11CreateDevice

static void reset_case(void)
{
    CHECK(realDevice == NULL && realContext == NULL && realVertexShader == NULL);
    CHECK(realInputLayout == NULL && realVertexBuffer == NULL);
    CHECK(deviceProxy.real == NULL && contextProxy.real == NULL);
    CHECK(vertexShaderProxy.real == NULL && inputLayoutProxy.real == NULL
        && vertexBufferProxy.real == NULL);
    CHECK(codeBlobProxy.real == NULL && codeBlobProxy.references == 0);
    CHECK(diagnosticsBlobProxy.real == NULL && diagnosticsBlobProxy.references == 0);
    mode = 0;
    loadCalls = exportCalls = unloadCalls = deviceCalls = 0;
    vertexShaderCalls = inputLayoutCalls = vertexBufferCalls = 0;
    clearStateCalls = contextReleaseCalls = deviceReleaseCalls = earlyDeviceReleaseCalls = 0;
    drawCalls = bindingCalls = bindingIndex = 0;
    vertexShaderReleases = inputLayoutReleases = vertexBufferReleases = 0;
    codeBlobReleases = diagnosticsBlobReleases = 0;
    diagnosticsBlobCreates = 0;
    finalContextReferences = finalDeviceReferences = 99;
    liveBytecode = NULL;
    liveBytecodeSize = 0;
    eventCount = 0;
    events[0] = '\0';
    realCodeBlob = realDiagnosticsBlob = NULL;
}

static void check_case(UINT currentMode, HRESULT result)
{
    static const HRESULT expectedResults[] = {
        S_OK, (HRESULT)0x80004005L, E_UNEXPECTED, E_UNEXPECTED,
        (HRESULT)0x80004005L, (HRESULT)0x80004005L, (HRESULT)0x80004005L,
        E_UNEXPECTED, E_UNEXPECTED, E_UNEXPECTED,
        (HRESULT)0x800700AAL, (HRESULT)0x80004005L,
        (HRESULT)0x80070057L, (HRESULT)0x80004005L, E_UNEXPECTED, E_UNEXPECTED
    };
    UINT expectedVertexShaderCalls = currentMode <= 11
        && (currentMode == 0 || currentMode >= 4) ? 1u : 0u;
    UINT expectedInputLayoutCalls = currentMode == 0 || currentMode == 5 || currentMode == 6
        || currentMode == 8 || currentMode == 9 || currentMode == 10 ? 1u : 0u;
    UINT expectedVertexBufferCalls = currentMode == 0 || currentMode == 6 || currentMode == 9
        || currentMode == 10 ? 1u : 0u;
    UINT expectedContext = currentMode <= 11 && currentMode != 2;
    char expectedEvents[16] = "";
    CHECK(result == expectedResults[currentMode]);
    CHECK(loadCalls == 1 && exportCalls == 1 && unloadCalls == 1);
    CHECK(deviceCalls == (currentMode <= 11 ? 1u : 0u));
    CHECK(vertexShaderCalls == expectedVertexShaderCalls);
    CHECK(inputLayoutCalls == expectedInputLayoutCalls);
    CHECK(vertexBufferCalls == expectedVertexBufferCalls);
    CHECK(drawCalls == (currentMode == 0 || currentMode == 10 ? 1u : 0u));
    CHECK(bindingCalls == (currentMode == 0 || currentMode == 10 ? 5u : 0u));
    CHECK(vertexShaderReleases >= expectedVertexShaderCalls);
    CHECK(inputLayoutReleases >= expectedInputLayoutCalls);
    CHECK(vertexBufferReleases >= expectedVertexBufferCalls);
    CHECK(codeBlobReleases == (currentMode == 14 ? 0u : 1u));
    CHECK(diagnosticsBlobReleases == (currentMode >= 12 ? 1u : 0u));
    CHECK(diagnosticsBlobCreates == (currentMode == 12 || currentMode == 14
        || currentMode == 15 ? 1u : 0u));
    CHECK(clearStateCalls == (expectedContext ? 1u : 0u));
    CHECK(contextReleaseCalls == (expectedContext ? 1u : 0u));
    CHECK(deviceReleaseCalls >= (currentMode <= 11 && currentMode != 3 ? 1u : 0u));
    CHECK(earlyDeviceReleaseCalls == (currentMode == 3 ? 1u : 0u));
    CHECK(expectedContext ? finalContextReferences != 99u : finalContextReferences == 99u);
    if (currentMode >= 12)
    {
        CHECK(finalDeviceReferences == 99u);
    }
    else if (currentMode == 0 || currentMode == 10)
    {
        strcpy_s(expectedEvents, sizeof(expectedEvents), "VLBSIATRDC");
        CHECK(finalDeviceReferences == 0);
    }
    else if (currentMode == 1 || currentMode == 3)
    {
        strcpy_s(expectedEvents, sizeof(expectedEvents), "C");
    }
    else if (currentMode == 2)
    {
        strcpy_s(expectedEvents, sizeof(expectedEvents), "");
        CHECK(finalDeviceReferences == 0);
    }
    else if (currentMode == 4 || currentMode == 7 || currentMode == 11)
    {
        strcpy_s(expectedEvents, sizeof(expectedEvents), "VC");
    }
    else if (currentMode == 5 || currentMode == 8)
    {
        strcpy_s(expectedEvents, sizeof(expectedEvents), "VLC");
    }
    else
    {
        strcpy_s(expectedEvents, sizeof(expectedEvents), "VLBC");
    }
    CHECK(strcmp(events, expectedEvents) == 0);
    if (currentMode <= 11 && currentMode != 3)
    {
        CHECK(finalDeviceReferences == 0);
    }
    CHECK(realDevice == NULL && realContext == NULL && realVertexShader == NULL);
    CHECK(realInputLayout == NULL && realVertexBuffer == NULL && loadedCompiler == NULL);
    CHECK(deviceProxy.real == NULL && contextProxy.real == NULL);
    CHECK(vertexShaderProxy.real == NULL && inputLayoutProxy.real == NULL
        && vertexBufferProxy.real == NULL);
    CHECK(codeBlobProxy.real == NULL && codeBlobProxy.references == 0);
    CHECK(diagnosticsBlobProxy.real == NULL && diagnosticsBlobProxy.references == 0);
}

int main(int argc, char** argv)
{
    UINT currentMode;
    CHECK(argc == 1 || (argc == 2 && strcmp(argv[1], "preloaded-shadow") == 0));
    if (argc == 2)
    {
        WCHAR shadowPath[32768];
        WCHAR* separator;
        HMODULE shadow;
        reset_case();
        CHECK(GetModuleFileNameW(NULL, shadowPath, 32768) != 0);
        separator = wcsrchr(shadowPath, L'\\');
        CHECK(separator != NULL);
        memcpy(separator + 1, L"D3DCompiler_47.dll", sizeof(L"D3DCompiler_47.dll"));
        shadow = LoadLibraryExW(shadowPath, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
        CHECK(shadow != NULL);
        CHECK(NativeFfiDrawTriangleWarp() == HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED));
        CHECK(loadCalls == 1 && exportCalls == 0 && unloadCalls == 1 && deviceCalls == 0);
        CHECK(codeBlobReleases == 0 && realDevice == NULL && realContext == NULL);
        CHECK(FreeLibrary(shadow));
        CHECK(GetModuleHandleW(L"D3DCompiler_47.dll") == NULL);
        puts("preloaded-shadow: rejected before export resolution");
        return 0;
    }
    for (currentMode = 0; currentMode < 16; currentMode++)
    {
        HRESULT result;
        reset_case();
        mode = currentMode;
        result = NativeFfiDrawTriangleWarp();
        check_case(currentMode, result);
        printf("case %u: 0x%08lX; setup=%s\n",
            currentMode, (unsigned long)result, events);
        fflush(stdout);
    }
    puts("real WARP setup, ordered bindings, one draw, 12 existing cases, and 4 compiler-output cases verified");
    return 0;
}
