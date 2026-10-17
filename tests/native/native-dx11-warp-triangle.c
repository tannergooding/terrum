#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This provisional gate requires Windows x64.
#endif

typedef HRESULT (WINAPI *compile_from_file)(LPCWSTR, const D3D_SHADER_MACRO*,
    ID3DInclude*, LPCSTR, LPCSTR, UINT, UINT, ID3DBlob**, ID3DBlob**);

#define WARP_TRIANGLE_PATH_CAPACITY 32768

static HRESULT warp_triangle_failure(const char* stage, HRESULT result)
{
    fprintf(stderr, "dx11-warp-triangle: %s failed (0x%08lX)\n",
        stage, (unsigned long)result);
    return result;
}

static HRESULT warp_triangle_win32_failure(const char* stage, DWORD error)
{
    return warp_triangle_failure(stage,
        error == ERROR_SUCCESS ? E_UNEXPECTED : HRESULT_FROM_WIN32(error));
}

static HRESULT warp_triangle_system_module(HMODULE module, const WCHAR* leaf)
{
    WCHAR actual[WARP_TRIANGLE_PATH_CAPACITY];
    WCHAR expected[WARP_TRIANGLE_PATH_CAPACITY];
    UINT length = GetSystemDirectoryW(expected, WARP_TRIANGLE_PATH_CAPACITY);
    size_t leafLength = wcslen(leaf);
    DWORD actualLength;
    if (length == 0)
    {
        return warp_triangle_win32_failure("GetSystemDirectoryW", GetLastError());
    }
    if (length >= WARP_TRIANGLE_PATH_CAPACITY
        || length + 1 + leafLength >= WARP_TRIANGLE_PATH_CAPACITY)
    {
        return warp_triangle_win32_failure("system module path", ERROR_INSUFFICIENT_BUFFER);
    }
    expected[length++] = L'\\';
    memcpy(expected + length, leaf, (leafLength + 1) * sizeof(WCHAR));
    actualLength = GetModuleFileNameW(module, actual, WARP_TRIANGLE_PATH_CAPACITY);
    if (actualLength == 0)
    {
        return warp_triangle_win32_failure("GetModuleFileNameW", GetLastError());
    }
    if (actualLength >= WARP_TRIANGLE_PATH_CAPACITY)
    {
        return warp_triangle_win32_failure("loaded module path", ERROR_INSUFFICIENT_BUFFER);
    }
    if (_wcsicmp(actual, expected) != 0)
    {
        return warp_triangle_win32_failure("non-System32 module rejected", ERROR_ACCESS_DENIED);
    }
    return S_OK;
}

static HRESULT warp_triangle_shader_path(WCHAR* path)
{
    static const WCHAR leaf[] = L"triangle.hlsl";
    DWORD length = GetModuleFileNameW(NULL, path, WARP_TRIANGLE_PATH_CAPACITY);
    WCHAR* separator;
    size_t prefixLength;
    if (length == 0)
    {
        return warp_triangle_win32_failure("executable path", GetLastError());
    }
    if (length >= WARP_TRIANGLE_PATH_CAPACITY)
    {
        return warp_triangle_win32_failure("executable path", ERROR_INSUFFICIENT_BUFFER);
    }
    separator = wcsrchr(path, L'\\');
    if (separator == NULL)
    {
        return warp_triangle_win32_failure("executable directory", ERROR_BAD_PATHNAME);
    }
    prefixLength = (size_t)(separator + 1 - path);
    if (prefixLength + sizeof(leaf) / sizeof(WCHAR) > WARP_TRIANGLE_PATH_CAPACITY)
    {
        return warp_triangle_win32_failure("shader path", ERROR_INSUFFICIENT_BUFFER);
    }
    memcpy(path + prefixLength, leaf, sizeof(leaf));
    return S_OK;
}

__declspec(dllexport) HRESULT WINAPI NativeFfiDrawTriangleWarp(void)
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
    WCHAR shaderPath[WARP_TRIANGLE_PATH_CAPACITY];
    HMODULE compilerModule = NULL;
    HMODULE d3d11Module;
    FARPROC symbol;
    compile_from_file compiler;
    ID3DBlob* code = NULL;
    ID3DBlob* diagnostics = NULL;
    ID3D11Device* device = NULL;
    ID3D11DeviceContext* context = NULL;
    ID3D11VertexShader* vertexShader = NULL;
    ID3D11InputLayout* inputLayout = NULL;
    ID3D11Buffer* vertexBuffer = NULL;
    D3D_FEATURE_LEVEL selectedFeatureLevel = 0;
    D3D11_BUFFER_DESC bufferDescription = {
        36, D3D11_USAGE_IMMUTABLE, D3D11_BIND_VERTEX_BUFFER, 0, 0, 0
    };
    D3D11_SUBRESOURCE_DATA initialData = { vertices, 0, 0 };
    D3D11_VIEWPORT viewport = { 0.0f, 0.0f, 1.0f, 1.0f, 0.0f, 1.0f };
    const void* bytecode;
    SIZE_T bytecodeSize;
    UINT stride = 12;
    UINT offset = 0;
    HRESULT result = warp_triangle_shader_path(shaderPath);
    if (FAILED(result))
    {
        goto cleanup;
    }
    d3d11Module = GetModuleHandleW(L"d3d11.dll");
    if (d3d11Module == NULL)
    {
        result = warp_triangle_win32_failure("D3D11 startup import", GetLastError());
        goto cleanup;
    }
    result = warp_triangle_system_module(d3d11Module, L"d3d11.dll");
    if (FAILED(result))
    {
        goto cleanup;
    }
    compilerModule = LoadLibraryExW(L"D3DCompiler_47.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (compilerModule == NULL)
    {
        result = warp_triangle_win32_failure("LoadLibraryExW compiler", GetLastError());
        goto cleanup;
    }
    result = warp_triangle_system_module(compilerModule, L"D3DCompiler_47.dll");
    if (FAILED(result))
    {
        goto cleanup;
    }
    symbol = GetProcAddress(compilerModule, "D3DCompileFromFile");
    if (symbol == NULL)
    {
        result = warp_triangle_win32_failure("D3DCompileFromFile export", GetLastError());
        goto cleanup;
    }
    {
        typedef char compiler_pointer_width[(sizeof(compiler) == sizeof(symbol)) ? 1 : -1];
        (void)sizeof(compiler_pointer_width);
    }
    memcpy(&compiler, &symbol, sizeof(compiler));
    result = compiler(shaderPath, NULL, NULL, "VertexMain", "vs_5_0", 0, 0, &code, &diagnostics);
    if (diagnostics != NULL)
    {
        const void* text = diagnostics->lpVtbl->GetBufferPointer(diagnostics);
        SIZE_T size = diagnostics->lpVtbl->GetBufferSize(diagnostics);
        if (text != NULL && size != 0)
        {
            fprintf(stderr, "dx11-warp-triangle: compiler diagnostics:\n");
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
        warp_triangle_failure("D3DCompileFromFile", result);
        goto cleanup;
    }
    if (code == NULL)
    {
        result = warp_triangle_failure("compiler success without bytecode", E_UNEXPECTED);
        goto cleanup;
    }
    bytecode = code->lpVtbl->GetBufferPointer(code);
    bytecodeSize = code->lpVtbl->GetBufferSize(code);
    if (bytecode == NULL || bytecodeSize == 0)
    {
        result = warp_triangle_failure("compiler bytecode extent", E_UNEXPECTED);
        goto cleanup;
    }
    result = D3D11CreateDevice(NULL, D3D_DRIVER_TYPE_WARP, NULL, 0, featureLevels,
        1, D3D11_SDK_VERSION, &device, &selectedFeatureLevel, &context);
    if (FAILED(result))
    {
        warp_triangle_failure("D3D11CreateDevice WARP", result);
        goto cleanup;
    }
    if (device == NULL || context == NULL
        || selectedFeatureLevel != D3D_FEATURE_LEVEL_11_0)
    {
        result = warp_triangle_failure("WARP success without required device/context/feature level",
            E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateVertexShader(device, bytecode, bytecodeSize, NULL,
        &vertexShader);
    if (FAILED(result))
    {
        warp_triangle_failure("CreateVertexShader", result);
        goto cleanup;
    }
    if (vertexShader == NULL)
    {
        result = warp_triangle_failure("vertex-shader success without output", E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateInputLayout(device, &inputElement, 1, bytecode,
        bytecodeSize, &inputLayout);
    if (FAILED(result))
    {
        warp_triangle_failure("CreateInputLayout", result);
        goto cleanup;
    }
    if (inputLayout == NULL)
    {
        result = warp_triangle_failure("input-layout success without output", E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateBuffer(device, &bufferDescription, &initialData,
        &vertexBuffer);
    if (FAILED(result))
    {
        warp_triangle_failure("CreateBuffer", result);
        goto cleanup;
    }
    if (vertexBuffer == NULL)
    {
        result = warp_triangle_failure("vertex-buffer success without output", E_UNEXPECTED);
        goto cleanup;
    }
    context->lpVtbl->VSSetShader(context, vertexShader, NULL, 0);
    context->lpVtbl->IASetInputLayout(context, inputLayout);
    context->lpVtbl->IASetVertexBuffers(context, 0, 1, &vertexBuffer, &stride, &offset);
    context->lpVtbl->IASetPrimitiveTopology(context, D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->lpVtbl->RSSetViewports(context, 1, &viewport);
    context->lpVtbl->Draw(context, 3, 0);

cleanup:
    if (context != NULL)
    {
        context->lpVtbl->ClearState(context);
        context->lpVtbl->Release(context);
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
    if (diagnostics != NULL)
    {
        diagnostics->lpVtbl->Release(diagnostics);
    }
    if (code != NULL)
    {
        code->lpVtbl->Release(code);
    }
    if (compilerModule != NULL && !FreeLibrary(compilerModule))
    {
        HRESULT unloadResult = warp_triangle_win32_failure("FreeLibrary compiler", GetLastError());
        if (SUCCEEDED(result))
        {
            result = unloadResult;
        }
    }
    return result;
}
