#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This provisional bridge requires Windows x64.
#endif

typedef HRESULT (WINAPI *compile_from_file)(LPCWSTR, const D3D_SHADER_MACRO*,
    ID3DInclude*, LPCSTR, LPCSTR, UINT, UINT, ID3DBlob**, ID3DBlob**);

#define BRIDGE_PATH_CAPACITY 32768

static HRESULT bridge_failure(const char* stage, HRESULT result)
{
    fprintf(stderr, "dx11-shader-layout: %s failed (0x%08lX)\n", stage, (unsigned long)result);
    return result;
}

static HRESULT bridge_win32_failure(const char* stage, DWORD error)
{
    return bridge_failure(stage, error == ERROR_SUCCESS ? E_UNEXPECTED : HRESULT_FROM_WIN32(error));
}

static HRESULT bridge_system_module(HMODULE module, const WCHAR* leaf)
{
    WCHAR actual[BRIDGE_PATH_CAPACITY];
    WCHAR expected[BRIDGE_PATH_CAPACITY];
    UINT length = GetSystemDirectoryW(expected, BRIDGE_PATH_CAPACITY);
    size_t leafLength = wcslen(leaf);
    DWORD actualLength;
    if (length == 0)
    {
        return bridge_win32_failure("GetSystemDirectoryW", GetLastError());
    }
    if (length >= BRIDGE_PATH_CAPACITY || length + 1 + leafLength >= BRIDGE_PATH_CAPACITY)
    {
        return bridge_win32_failure("system module path", ERROR_INSUFFICIENT_BUFFER);
    }
    expected[length++] = L'\\';
    memcpy(expected + length, leaf, (leafLength + 1) * sizeof(WCHAR));
    actualLength = GetModuleFileNameW(module, actual, BRIDGE_PATH_CAPACITY);
    if (actualLength == 0)
    {
        return bridge_win32_failure("GetModuleFileNameW", GetLastError());
    }
    if (actualLength >= BRIDGE_PATH_CAPACITY)
    {
        return bridge_win32_failure("loaded module path", ERROR_INSUFFICIENT_BUFFER);
    }
    if (_wcsicmp(actual, expected) != 0)
    {
        return bridge_win32_failure("non-System32 module rejected", ERROR_ACCESS_DENIED);
    }
    return S_OK;
}

static HRESULT bridge_shader_path(WCHAR* path)
{
    static const WCHAR leaf[] = L"triangle.hlsl";
    DWORD length = GetModuleFileNameW(NULL, path, BRIDGE_PATH_CAPACITY);
    WCHAR* separator;
    size_t prefixLength;
    if (length == 0)
    {
        return bridge_win32_failure("executable path", GetLastError());
    }
    if (length >= BRIDGE_PATH_CAPACITY)
    {
        return bridge_win32_failure("executable path", ERROR_INSUFFICIENT_BUFFER);
    }
    separator = wcsrchr(path, L'\\');
    if (separator == NULL)
    {
        return bridge_win32_failure("executable directory", ERROR_BAD_PATHNAME);
    }
    prefixLength = (size_t)(separator + 1 - path);
    if (prefixLength + sizeof(leaf) / sizeof(WCHAR) > BRIDGE_PATH_CAPACITY)
    {
        return bridge_win32_failure("shader path", ERROR_INSUFFICIENT_BUFFER);
    }
    memcpy(path + prefixLength, leaf, sizeof(leaf));
    return S_OK;
}

__declspec(dllexport) HRESULT WINAPI NativeFfiCompileTriangleInputLayout(void)
{
    static const D3D11_INPUT_ELEMENT_DESC description = {
        "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0
    };
    static const D3D_FEATURE_LEVEL featureLevel = D3D_FEATURE_LEVEL_11_0;
    WCHAR shaderPath[BRIDGE_PATH_CAPACITY];
    HMODULE compilerModule = NULL;
    HMODULE d3d11Module;
    FARPROC symbol;
    compile_from_file compiler;
    ID3DBlob* code = NULL;
    ID3DBlob* diagnostics = NULL;
    ID3D11Device* device = NULL;
    ID3D11InputLayout* layout = NULL;
    const void* bytecode;
    SIZE_T bytecodeSize;
    HRESULT result = bridge_shader_path(shaderPath);
    if (FAILED(result))
    {
        goto cleanup;
    }
    d3d11Module = GetModuleHandleW(L"d3d11.dll");
    if (d3d11Module == NULL)
    {
        result = bridge_win32_failure("D3D11 startup import", GetLastError());
        goto cleanup;
    }
    result = bridge_system_module(d3d11Module, L"d3d11.dll");
    if (FAILED(result))
    {
        goto cleanup;
    }
    compilerModule = LoadLibraryExW(L"D3DCompiler_47.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (compilerModule == NULL)
    {
        result = bridge_win32_failure("LoadLibraryExW compiler", GetLastError());
        goto cleanup;
    }
    result = bridge_system_module(compilerModule, L"D3DCompiler_47.dll");
    if (FAILED(result))
    {
        goto cleanup;
    }
    symbol = GetProcAddress(compilerModule, "D3DCompileFromFile");
    if (symbol == NULL)
    {
        result = bridge_win32_failure("D3DCompileFromFile export", GetLastError());
        goto cleanup;
    }
    // Windows x64 code pointers share representation; only this exact SDK signature is admitted.
    typedef char compiler_pointer_width[(sizeof(compiler) == sizeof(symbol)) ? 1 : -1];
    memcpy(&compiler, &symbol, sizeof(compiler));
    result = compiler(shaderPath, NULL, NULL, "VertexMain", "vs_5_0", 0, 0, &code, &diagnostics);
    if (diagnostics != NULL)
    {
        const void* text = diagnostics->lpVtbl->GetBufferPointer(diagnostics);
        SIZE_T size = diagnostics->lpVtbl->GetBufferSize(diagnostics);
        if (text != NULL && size != 0)
        {
            fprintf(stderr, "dx11-shader-layout: compiler diagnostics:\n");
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
        bridge_failure("D3DCompileFromFile", result);
        goto cleanup;
    }
    if (code == NULL)
    {
        result = bridge_failure("compiler success without bytecode", E_UNEXPECTED);
        goto cleanup;
    }
    bytecode = code->lpVtbl->GetBufferPointer(code);
    bytecodeSize = code->lpVtbl->GetBufferSize(code);
    if (bytecode == NULL || bytecodeSize == 0)
    {
        result = bridge_failure("compiler bytecode extent", E_UNEXPECTED);
        goto cleanup;
    }
    result = D3D11CreateDevice(NULL, D3D_DRIVER_TYPE_WARP, NULL, 0, &featureLevel, 1,
        D3D11_SDK_VERSION, &device, NULL, NULL);
    if (FAILED(result))
    {
        bridge_failure("D3D11CreateDevice WARP", result);
        goto cleanup;
    }
    if (device == NULL)
    {
        result = bridge_failure("WARP success without device", E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateInputLayout(device, &description, 1, bytecode, bytecodeSize, &layout);
    if (FAILED(result))
    {
        bridge_failure("CreateInputLayout", result);
    }
    else if (layout == NULL)
    {
        result = bridge_failure("layout success without output", E_UNEXPECTED);
    }

cleanup:
    if (layout != NULL)
    {
        layout->lpVtbl->Release(layout);
    }
    if (device != NULL)
    {
        device->lpVtbl->Release(device);
    }
    // Ownership is per output reference, including aliased blob pointers.
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
        HRESULT unloadResult = bridge_win32_failure("FreeLibrary compiler", GetLastError());
        if (SUCCEEDED(result))
        {
            result = unloadResult;
        }
    }
    return result;
}
