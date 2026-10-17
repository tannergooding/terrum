#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "compiler session fixture line %d: %s\n", __LINE__, #condition); ExitProcess(91); } } while (0)

typedef HRESULT (WINAPI* CompileFunction)(LPCWSTR, const D3D_SHADER_MACRO*, ID3DInclude*,
    LPCSTR, LPCSTR, UINT, UINT, ID3DBlob**, ID3DBlob**);

typedef struct Blob {
    ID3D10BlobVtbl* table;
    ID3DBlob* original;
    ULONG references;
} Blob;

static HMODULE s_module, s_preloaded;
static CompileFunction s_compile;
static unsigned s_references, s_compilations, s_unloads;
static char s_mode[40];
static WCHAR s_trace[MAX_PATH];

__declspec(dllexport) HRESULT WINAPI NativeFfiCompilerAcquisition(
    IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL* levels, UINT count, UINT version, const DXGI_SWAP_CHAIN_DESC* description,
    IDXGISwapChain** swap, ID3D11Device** device, D3D_FEATURE_LEVEL* level, ID3D11DeviceContext** context)
{
    char expected[4];
    CHECK(GetEnvironmentVariableA("TERRUM_DX11_COMPILER_DRIVER", expected, sizeof(expected)));
    CHECK(!adapter && !software && driver == (D3D_DRIVER_TYPE)atoi(expected));
    /* Only the emitted test image substitutes hardware with WARP; production never falls back. */
    return D3D11CreateDeviceAndSwapChain(adapter, D3D_DRIVER_TYPE_WARP, software, flags,
        levels, count, version, description, swap, device, level, context);
}

__declspec(dllexport) BOOL WINAPI NativeFfiCompilerGetMessageW(LPMSG message, HWND window, UINT minimum, UINT maximum)
{
    CHECK(message && !window && !minimum && !maximum && s_compilations == 2 && !s_references && !s_unloads);
    return 0;
}

static void Trace(char event)
{
    DWORD written;
    HANDLE file = CreateFileW(s_trace, FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    CHECK(file != INVALID_HANDLE_VALUE);
    CHECK(WriteFile(file, &event, 1, &written, NULL) && written == 1);
    CHECK(CloseHandle(file));
}

static int Mode(const char* value)
{
    return strcmp(s_mode, value) == 0;
}

static HRESULT STDMETHODCALLTYPE QueryInterface(ID3DBlob* receiver, REFIID iid, void** output)
{
    (void)receiver;
    (void)iid;
    (void)output;
    CHECK(0);
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE AddRef(ID3DBlob* receiver)
{
    Blob* blob = (Blob*)receiver;
    CHECK(s_module && !s_unloads && blob->references);
    ++s_references;
    ++blob->references;
    Trace('A');
    return blob->original->lpVtbl->AddRef(blob->original);
}

static ULONG STDMETHODCALLTYPE Release(ID3DBlob* receiver)
{
    Blob* blob = (Blob*)receiver;
    CHECK(s_module && !s_unloads && blob->references && s_references);
    ULONG result = blob->original->lpVtbl->Release(blob->original);
    --s_references;
    --blob->references;
    Trace('R');
    if (!blob->references)
    {
        CHECK(HeapFree(GetProcessHeap(), 0, blob));
    }
    return result;
}

static LPVOID STDMETHODCALLTYPE GetBufferPointer(ID3DBlob* receiver)
{
    Blob* blob = (Blob*)receiver;
    CHECK(s_module && !s_unloads && blob->references);
    return blob->original->lpVtbl->GetBufferPointer(blob->original);
}

static SIZE_T STDMETHODCALLTYPE GetBufferSize(ID3DBlob* receiver)
{
    Blob* blob = (Blob*)receiver;
    CHECK(s_module && !s_unloads && blob->references);
    return blob->original->lpVtbl->GetBufferSize(blob->original);
}

static ID3D10BlobVtbl s_table = { QueryInterface, AddRef, Release, GetBufferPointer, GetBufferSize };

static ID3DBlob* Wrap(ID3DBlob* original)
{
    if (!original)
    {
        return NULL;
    }
    Blob* blob = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(Blob));
    CHECK(blob);
    blob->table = &s_table;
    blob->original = original;
    blob->references = 1;
    ++s_references;
    return (ID3DBlob*)blob;
}

static HRESULT WINAPI Compile(LPCWSTR file, const D3D_SHADER_MACRO* defines, ID3DInclude* include,
    LPCSTR entry, LPCSTR target, UINT flags1, UINT flags2, ID3DBlob** code, ID3DBlob** errors)
{
    CHECK(s_module && !s_unloads && s_compile);
    CHECK(file && !defines && !include && entry && target && !flags1 && !flags2);
    CHECK(code && errors && code != errors && !*code && !*errors);
    CHECK(((UINT_PTR)code & 7) == 0 && ((UINT_PTR)errors & 7) == 0);
    CHECK((!s_compilations && strcmp(entry, "VertexMain") == 0 && strcmp(target, "vs_5_0") == 0)
        || (s_compilations == 1 && strcmp(entry, "PixelMain") == 0 && strcmp(target, "ps_5_0") == 0));
    ++s_compilations;
    Trace('V');
    HRESULT result = s_compile(file, defines, include,
        Mode("diagnostics") || (Mode("pixel-failure") && s_compilations == 2) ? "MissingEntry" : entry,
        target, flags1, flags2, code, errors);
    *code = Wrap(*code);
    *errors = Wrap(*errors);
    if (Mode("compile-failure") || Mode("aliased-failure"))
    {
        CHECK(SUCCEEDED(result) && *code && !*errors);
        if (Mode("aliased-failure"))
        {
            (*code)->lpVtbl->AddRef(*code);
            *errors = *code;
        }
        return E_FAIL;
    }
    if (Mode("null-success"))
    {
        CHECK(SUCCEEDED(result) && *code && !*errors);
        (*code)->lpVtbl->Release(*code);
        *code = NULL;
        return S_OK;
    }
    if (Mode("positive-success"))
    {
        CHECK(SUCCEEDED(result));
        return S_FALSE;
    }
    return result;
}

__declspec(dllexport) HMODULE WINAPI NativeFfiCompilerLoadLibraryExW(LPCWSTR name, HANDLE file, DWORD flags)
{
    if (wcscmp(name, L"d3d11.dll") == 0)
    {
        CHECK(!file && flags == 0x800);
        return LoadLibraryExW(name, file, flags);
    }
    CHECK(GetEnvironmentVariableA("TERRUM_DX11_COMPILER_MODE", s_mode, sizeof(s_mode)));
    CHECK(GetEnvironmentVariableW(L"TERRUM_DX11_COMPILER_TRACE", s_trace, MAX_PATH));
    CHECK(wcscmp(name, L"D3DCompiler_47.dll") == 0 && !file && flags == 0x800);
    Trace('L');
    if (Mode("load-failure"))
    {
        SetLastError(ERROR_MOD_NOT_FOUND);
        return NULL;
    }
    if (Mode("preloaded"))
    {
        WCHAR shadow[MAX_PATH];
        CHECK(GetEnvironmentVariableW(L"TERRUM_DX11_COMPILER_SHADOW", shadow, MAX_PATH));
        s_preloaded = LoadLibraryExW(shadow, NULL, 0);
        CHECK(s_preloaded);
    }
    s_module = LoadLibraryExW(name, file, flags);
    CHECK(s_module);
    if (Mode("preloaded"))
    {
        CHECK(s_module == s_preloaded);
    }
    return s_module;
}

__declspec(dllexport) UINT WINAPI NativeFfiCompilerGetSystemDirectoryW(LPWSTR buffer, UINT size)
{
    if (!s_module || s_unloads)
    {
        return GetSystemDirectoryW(buffer, size);
    }
    CHECK(s_module && buffer && size == 260);
    Trace('S');
    if (Mode("directory-zero"))
    {
        return 0;
    }
    if (Mode("directory-truncated"))
    {
        return 260;
    }
    if (Mode("directory-required"))
    {
        return 261;
    }
    if (Mode("directory-max"))
    {
        return MAXDWORD;
    }
    if (Mode("directory-long"))
    {
        return 241;
    }
    if (Mode("directory-boundary"))
    {
        for (UINT index = 0; index < 240; ++index)
        {
            buffer[index] = L'a';
        }
        buffer[240] = 0;
        return 240;
    }
    return GetSystemDirectoryW(buffer, size);
}

__declspec(dllexport) DWORD WINAPI NativeFfiCompilerGetModuleFileNameW(HMODULE module, LPWSTR buffer, DWORD size)
{
    if (!module || module != s_module)
    {
        return GetModuleFileNameW(module, buffer, size);
    }
    CHECK(module == s_module && buffer && size == 260);
    Trace('M');
    if (Mode("module-zero"))
    {
        return 0;
    }
    if (Mode("module-truncated"))
    {
        return 260;
    }
    if (Mode("module-required"))
    {
        return 261;
    }
    if (Mode("module-max"))
    {
        return MAXDWORD;
    }
    if (Mode("wrong-path") || Mode("reject-unload-failure"))
    {
        wcscpy_s(buffer, size, L"C:\\untrusted\\D3DCompiler_47.dll");
        return (DWORD)wcslen(buffer);
    }
    if (Mode("directory-boundary"))
    {
        for (UINT index = 0; index < 240; ++index)
        {
            buffer[index] = L'a';
        }
        wcscpy_s(buffer + 240, size - 240, L"\\D3DCompiler_47.dll");
        return 259;
    }
    DWORD length = GetModuleFileNameW(module, buffer, size);
    if (Mode("case-fold"))
    {
        CHECK(CharLowerBuffW(buffer, length) == length);
    }
    return length;
}

__declspec(dllexport) int WINAPI NativeFfiCompilerCompareStringOrdinal(
    LPCWCH expected, int expectedLength, LPCWCH actual, int actualLength, BOOL ignoreCase)
{
    if (!s_module || s_unloads)
    {
        return CompareStringOrdinal(expected, expectedLength, actual, actualLength, ignoreCase);
    }
    CHECK(s_module && expected && actual && expectedLength > 0 && expectedLength < 260);
    CHECK(actualLength > 0 && actualLength < 260 && ignoreCase == TRUE);
    CHECK(expected[expectedLength] == 0 && actual[actualLength] == 0);
    Trace('C');
    if (Mode("compare-failure"))
    {
        return 0;
    }
    return CompareStringOrdinal(expected, expectedLength, actual, actualLength, ignoreCase);
}

__declspec(dllexport) FARPROC WINAPI NativeFfiCompilerGetProcAddress(HMODULE module, LPCSTR name)
{
    if (strcmp(name, "D3D11CreateDeviceAndSwapChain") == 0)
    {
        return (FARPROC)NativeFfiCompilerAcquisition;
    }
    CHECK(module == s_module && strcmp(name, "D3DCompileFromFile") == 0 && !s_unloads);
    Trace('E');
    if (Mode("export-missing"))
    {
        return NULL;
    }
    s_compile = (CompileFunction)GetProcAddress(module, name);
    CHECK(s_compile);
    return (FARPROC)Compile;
}

__declspec(dllexport) BOOL WINAPI NativeFfiCompilerFreeLibrary(HMODULE module)
{
    if (module != s_module)
    {
        return FreeLibrary(module);
    }
    CHECK(module == s_module && !s_references && !s_unloads);
    ++s_unloads;
    Trace('F');
    if (Mode("unload-failure") || Mode("reject-unload-failure"))
    {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    CHECK(FreeLibrary(module));
    s_module = NULL;
    if (s_preloaded)
    {
        CHECK(GetModuleHandleW(L"D3DCompiler_47.dll") == s_preloaded);
        CHECK(FreeLibrary(s_preloaded));
        CHECK(!GetModuleHandleW(L"D3DCompiler_47.dll"));
        s_preloaded = NULL;
    }
    return TRUE;
}
