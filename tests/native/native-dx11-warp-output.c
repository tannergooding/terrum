#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This provisional gate requires Windows x64.
#endif

typedef HRESULT (WINAPI *warp_output_compiler)(LPCWSTR, const D3D_SHADER_MACRO*,
    ID3DInclude*, LPCSTR, LPCSTR, UINT, UINT, ID3DBlob**, ID3DBlob**);

typedef struct warp_output_shader {
    ID3DBlob* code;
    ID3DBlob* diagnostics;
    const void* bytes;
    SIZE_T size;
} warp_output_shader;

#define WARP_OUTPUT_PATH_CAPACITY 32768
#ifndef WARP_OUTPUT_WIDTH
#define WARP_OUTPUT_WIDTH 32
#endif
#ifndef WARP_OUTPUT_HEIGHT
#define WARP_OUTPUT_HEIGHT 32
#endif

static HRESULT warp_output_failure(const char* stage, HRESULT result)
{
    fprintf(stderr, "dx11-warp-output: %s failed (0x%08lX)\n",
        stage, (unsigned long)result);
    return result;
}

static HRESULT warp_output_win32_failure(const char* stage, DWORD error)
{
    return warp_output_failure(stage,
        error == ERROR_SUCCESS ? E_UNEXPECTED : HRESULT_FROM_WIN32(error));
}

static HRESULT warp_output_system_module(HMODULE module, const WCHAR* leaf)
{
    WCHAR actual[WARP_OUTPUT_PATH_CAPACITY];
    WCHAR expected[WARP_OUTPUT_PATH_CAPACITY];
    UINT length = GetSystemDirectoryW(expected, WARP_OUTPUT_PATH_CAPACITY);
    size_t leafLength = wcslen(leaf);
    DWORD actualLength;
    if (length == 0)
    {
        return warp_output_win32_failure("GetSystemDirectoryW", GetLastError());
    }
    if (length >= WARP_OUTPUT_PATH_CAPACITY
        || length + 1 + leafLength >= WARP_OUTPUT_PATH_CAPACITY)
    {
        return warp_output_win32_failure("system module path", ERROR_INSUFFICIENT_BUFFER);
    }
    expected[length++] = L'\\';
    memcpy(expected + length, leaf, (leafLength + 1) * sizeof(WCHAR));
    actualLength = GetModuleFileNameW(module, actual, WARP_OUTPUT_PATH_CAPACITY);
    if (actualLength == 0)
    {
        return warp_output_win32_failure("GetModuleFileNameW", GetLastError());
    }
    if (actualLength >= WARP_OUTPUT_PATH_CAPACITY)
    {
        return warp_output_win32_failure("loaded module path", ERROR_INSUFFICIENT_BUFFER);
    }
    if (_wcsicmp(actual, expected) != 0)
    {
        return warp_output_win32_failure("non-System32 module rejected", ERROR_ACCESS_DENIED);
    }
    return S_OK;
}

static HRESULT warp_output_shader_path(WCHAR* path)
{
    static const WCHAR leaf[] = L"triangle.hlsl";
    DWORD length = GetModuleFileNameW(NULL, path, WARP_OUTPUT_PATH_CAPACITY);
    WCHAR* separator;
    size_t prefixLength;
    if (length == 0)
    {
        return warp_output_win32_failure("executable path", GetLastError());
    }
    if (length >= WARP_OUTPUT_PATH_CAPACITY)
    {
        return warp_output_win32_failure("executable path", ERROR_INSUFFICIENT_BUFFER);
    }
    separator = wcsrchr(path, L'\\');
    if (separator == NULL)
    {
        return warp_output_win32_failure("executable directory", ERROR_BAD_PATHNAME);
    }
    prefixLength = (size_t)(separator + 1 - path);
    if (prefixLength + sizeof(leaf) / sizeof(WCHAR) > WARP_OUTPUT_PATH_CAPACITY)
    {
        return warp_output_win32_failure("shader path", ERROR_INSUFFICIENT_BUFFER);
    }
    memcpy(path + prefixLength, leaf, sizeof(leaf));
    return S_OK;
}

static HRESULT warp_output_compile(warp_output_compiler compiler, const WCHAR* path,
    const char* entry, const char* target, warp_output_shader* shader)
{
    HRESULT result = compiler(path, NULL, NULL, entry, target, 0, 0,
        &shader->code, &shader->diagnostics);
    if (shader->diagnostics != NULL)
    {
        const void* text = shader->diagnostics->lpVtbl->GetBufferPointer(shader->diagnostics);
        SIZE_T size = shader->diagnostics->lpVtbl->GetBufferSize(shader->diagnostics);
        if (text != NULL && size != 0)
        {
            fprintf(stderr, "dx11-warp-output: %s compiler diagnostics:\n", entry);
            fwrite(text, 1, size, stderr);
            fputc('\n', stderr);
        }
        else if (SUCCEEDED(result))
        {
            result = E_UNEXPECTED;
        }
    }
    if (FAILED(result))
    {
        return warp_output_failure(entry, result);
    }
    if (shader->code == NULL)
    {
        return warp_output_failure("compiler success without bytecode", E_UNEXPECTED);
    }
    shader->bytes = shader->code->lpVtbl->GetBufferPointer(shader->code);
    shader->size = shader->code->lpVtbl->GetBufferSize(shader->code);
    if (shader->bytes == NULL || shader->size == 0)
    {
        return warp_output_failure("compiler bytecode extent", E_UNEXPECTED);
    }
    return S_OK;
}

static void warp_output_release_shader(warp_output_shader* shader)
{
    if (shader->diagnostics != NULL)
    {
        shader->diagnostics->lpVtbl->Release(shader->diagnostics);
    }
    if (shader->code != NULL)
    {
        shader->code->lpVtbl->Release(shader->code);
    }
}

static HRESULT warp_output_check_pixels(const D3D11_MAPPED_SUBRESOURCE* data)
{
    static const int expected[4] = { 26, 204, 230, 255 };
    const BYTE* outside;
    const BYTE* inside;
    UINT channel;
    if (data->pData == NULL || data->RowPitch < WARP_OUTPUT_WIDTH * 4)
    {
        return warp_output_failure("mapped data/pitch", E_UNEXPECTED);
    }
    outside = (const BYTE*)data->pData;
    inside = outside + (SIZE_T)(WARP_OUTPUT_HEIGHT / 2) * data->RowPitch
        + (WARP_OUTPUT_WIDTH / 2) * 4;
    /* Linear UNORM rounding may differ by one RGB code; both centers avoid edges. */
    for (channel = 0; channel < 4; channel++)
    {
        int difference = (int)inside[channel] - expected[channel];
        int tolerance = channel == 3 ? 0 : 1;
        if (difference < -tolerance || difference > tolerance
            || outside[channel] != (channel == 3 ? 255 : 0))
        {
            fprintf(stderr, "dx11-warp-output: pixels (%u,%u)=(%u,%u,%u,%u), "
                "(0,0)=(%u,%u,%u,%u)\n", WARP_OUTPUT_WIDTH / 2, WARP_OUTPUT_HEIGHT / 2,
                inside[0], inside[1], inside[2], inside[3],
                outside[0], outside[1], outside[2], outside[3]);
            return warp_output_failure("pixel output", E_FAIL);
        }
    }
    return S_OK;
}

#ifndef WARP_OUTPUT_UTILITIES_ONLY
__declspec(dllexport) HRESULT WINAPI NativeFfiRenderTriangleWarp(void)
{
    static const D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_0 };
    static const D3D11_INPUT_ELEMENT_DESC inputElement = {
        "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 0,
        D3D11_INPUT_PER_VERTEX_DATA, 0
    };
    static const float vertices[9] = {
        0.0f, 0.6f, 0.5f,
        0.6f, -0.5f, 0.5f,
        -0.6f, -0.5f, 0.5f,
    };
    static const FLOAT clearColor[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    WCHAR shaderPath[WARP_OUTPUT_PATH_CAPACITY];
    HMODULE compilerModule = NULL;
    HMODULE d3d11Module;
    FARPROC symbol;
    warp_output_compiler compiler;
    warp_output_shader vertexCode = { 0 };
    warp_output_shader pixelCode = { 0 };
    ID3D11Device* device = NULL;
    ID3D11DeviceContext* context = NULL;
    ID3D11VertexShader* vertexShader = NULL;
    ID3D11PixelShader* pixelShader = NULL;
    ID3D11InputLayout* inputLayout = NULL;
    ID3D11Buffer* vertexBuffer = NULL;
    ID3D11Texture2D* target = NULL;
    ID3D11RenderTargetView* targetView = NULL;
    ID3D11Texture2D* staging = NULL;
    D3D_FEATURE_LEVEL selectedFeatureLevel = 0;
    D3D11_BUFFER_DESC bufferDescription = {
        36, D3D11_USAGE_IMMUTABLE, D3D11_BIND_VERTEX_BUFFER, 0, 0, 0
    };
    D3D11_SUBRESOURCE_DATA initialData = { vertices, 0, 0 };
    D3D11_TEXTURE2D_DESC textureDescription = {
        WARP_OUTPUT_WIDTH, WARP_OUTPUT_HEIGHT, 1, 1, DXGI_FORMAT_R8G8B8A8_UNORM,
        { 1, 0 }, D3D11_USAGE_DEFAULT, D3D11_BIND_RENDER_TARGET, 0, 0
    };
    D3D11_VIEWPORT viewport = {
        0.0f, 0.0f, (FLOAT)WARP_OUTPUT_WIDTH, (FLOAT)WARP_OUTPUT_HEIGHT, 0.0f, 1.0f
    };
    D3D11_MAPPED_SUBRESOURCE data = { 0 };
    BOOL mapped = FALSE;
    UINT stride = 12;
    UINT offset = 0;
    HRESULT result = warp_output_shader_path(shaderPath);
    if (FAILED(result))
    {
        goto cleanup;
    }
    d3d11Module = GetModuleHandleW(L"d3d11.dll");
    if (d3d11Module == NULL)
    {
        result = warp_output_win32_failure("D3D11 startup import", GetLastError());
        goto cleanup;
    }
    result = warp_output_system_module(d3d11Module, L"d3d11.dll");
    if (FAILED(result))
    {
        goto cleanup;
    }
    compilerModule = LoadLibraryExW(L"D3DCompiler_47.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (compilerModule == NULL)
    {
        result = warp_output_win32_failure("LoadLibraryExW compiler", GetLastError());
        goto cleanup;
    }
    result = warp_output_system_module(compilerModule, L"D3DCompiler_47.dll");
    if (FAILED(result))
    {
        goto cleanup;
    }
    symbol = GetProcAddress(compilerModule, "D3DCompileFromFile");
    if (symbol == NULL)
    {
        result = warp_output_win32_failure("D3DCompileFromFile export", GetLastError());
        goto cleanup;
    }
    {
        typedef char compiler_pointer_width[(sizeof(compiler) == sizeof(symbol)) ? 1 : -1];
        (void)sizeof(compiler_pointer_width);
    }
    memcpy(&compiler, &symbol, sizeof(compiler));
    result = warp_output_compile(compiler, shaderPath, "VertexMain", "vs_5_0", &vertexCode);
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = warp_output_compile(compiler, shaderPath, "PixelMain", "ps_5_0", &pixelCode);
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = D3D11CreateDevice(NULL, D3D_DRIVER_TYPE_WARP, NULL, 0, featureLevels,
        1, D3D11_SDK_VERSION, &device, &selectedFeatureLevel, &context);
    if (FAILED(result))
    {
        warp_output_failure("D3D11CreateDevice WARP", result);
        goto cleanup;
    }
    if (device == NULL || context == NULL || selectedFeatureLevel != D3D_FEATURE_LEVEL_11_0)
    {
        result = warp_output_failure("WARP success without required device/context/feature level",
            E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateVertexShader(device, vertexCode.bytes, vertexCode.size,
        NULL, &vertexShader);
    if (FAILED(result) || vertexShader == NULL)
    {
        result = warp_output_failure("CreateVertexShader", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateInputLayout(device, &inputElement, 1, vertexCode.bytes,
        vertexCode.size, &inputLayout);
    if (FAILED(result) || inputLayout == NULL)
    {
        result = warp_output_failure("CreateInputLayout", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateBuffer(device, &bufferDescription, &initialData, &vertexBuffer);
    if (FAILED(result) || vertexBuffer == NULL)
    {
        result = warp_output_failure("CreateBuffer", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreatePixelShader(device, pixelCode.bytes, pixelCode.size,
        NULL, &pixelShader);
    if (FAILED(result) || pixelShader == NULL)
    {
        result = warp_output_failure("CreatePixelShader", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateTexture2D(device, &textureDescription, NULL, &target);
    if (FAILED(result) || target == NULL)
    {
        result = warp_output_failure("CreateTexture2D target", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateRenderTargetView(device, (ID3D11Resource*)target,
        NULL, &targetView);
    if (FAILED(result) || targetView == NULL)
    {
        result = warp_output_failure("CreateRenderTargetView", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    textureDescription.Usage = D3D11_USAGE_STAGING;
    textureDescription.BindFlags = 0;
    textureDescription.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    result = device->lpVtbl->CreateTexture2D(device, &textureDescription, NULL, &staging);
    if (FAILED(result) || staging == NULL)
    {
        result = warp_output_failure("CreateTexture2D staging", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    context->lpVtbl->ClearRenderTargetView(context, targetView, clearColor);
    context->lpVtbl->OMSetRenderTargets(context, 1, &targetView, NULL);
    context->lpVtbl->PSSetShader(context, pixelShader, NULL, 0);
    context->lpVtbl->VSSetShader(context, vertexShader, NULL, 0);
    context->lpVtbl->IASetInputLayout(context, inputLayout);
    context->lpVtbl->IASetVertexBuffers(context, 0, 1, &vertexBuffer, &stride, &offset);
    context->lpVtbl->IASetPrimitiveTopology(context, D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->lpVtbl->RSSetViewports(context, 1, &viewport);
    context->lpVtbl->Draw(context, 3, 0);
    context->lpVtbl->CopyResource(context, (ID3D11Resource*)staging, (ID3D11Resource*)target);
    result = context->lpVtbl->Map(context, (ID3D11Resource*)staging, 0, D3D11_MAP_READ, 0, &data);
    if (FAILED(result))
    {
        warp_output_failure("Map staging", result);
        goto cleanup;
    }
    mapped = TRUE;
    result = warp_output_check_pixels(&data);

cleanup:
    if (mapped)
    {
        context->lpVtbl->Unmap(context, (ID3D11Resource*)staging, 0);
    }
    if (context != NULL)
    {
        context->lpVtbl->ClearState(context);
        context->lpVtbl->Release(context);
    }
    if (staging != NULL)
    {
        staging->lpVtbl->Release(staging);
    }
    if (targetView != NULL)
    {
        targetView->lpVtbl->Release(targetView);
    }
    if (target != NULL)
    {
        target->lpVtbl->Release(target);
    }
    if (pixelShader != NULL)
    {
        pixelShader->lpVtbl->Release(pixelShader);
    }
    if (vertexBuffer != NULL)
    {
        vertexBuffer->lpVtbl->Release(vertexBuffer);
    }
    if (inputLayout != NULL)
    {
        inputLayout->lpVtbl->Release(inputLayout);
    }
    if (vertexShader != NULL)
    {
        vertexShader->lpVtbl->Release(vertexShader);
    }
    if (device != NULL)
    {
        device->lpVtbl->Release(device);
    }
    warp_output_release_shader(&pixelCode);
    warp_output_release_shader(&vertexCode);
    if (compilerModule != NULL && !FreeLibrary(compilerModule))
    {
        HRESULT unloadResult = warp_output_win32_failure("FreeLibrary compiler", GetLastError());
        if (SUCCEEDED(result))
        {
            result = unloadResult;
        }
    }
    return result;
}
#endif
