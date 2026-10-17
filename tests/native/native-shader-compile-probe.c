#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <wchar.h>
#include <windows.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This fixture requires the Windows x64 ABI.
#endif

typedef struct FixtureBlob FixtureBlob;
typedef struct FixtureMacro { const char* Name; const char* Definition; } FixtureMacro;
typedef struct FixtureInclude FixtureInclude;

typedef struct FixtureBlobVtable
{
    HRESULT (STDMETHODCALLTYPE *QueryInterface)(FixtureBlob*, const void*, void**);
    ULONG (STDMETHODCALLTYPE *AddRef)(FixtureBlob*);
    ULONG (STDMETHODCALLTYPE *Release)(FixtureBlob*);
    void* (STDMETHODCALLTYPE *GetBufferPointer)(FixtureBlob*);
    SIZE_T (STDMETHODCALLTYPE *GetBufferSize)(FixtureBlob*);
} FixtureBlobVtable;

struct FixtureBlob
{
    const FixtureBlobVtable* lpVtbl;
    ULONG refs;
    ULONG releases;
    ULONG pointerCalls;
    ULONG sizeCalls;
    SIZE_T size;
    uint8_t bytes[24];
};

typedef char fixture_hresult_width[(sizeof(HRESULT) == 4) ? 1 : -1];
typedef char fixture_uint_width[(sizeof(UINT) == 4) ? 1 : -1];
typedef char fixture_ulong_width[(sizeof(ULONG) == 4) ? 1 : -1];
typedef char fixture_size_width[(sizeof(SIZE_T) == 8) ? 1 : -1];
typedef char fixture_cell_width[(sizeof(FixtureBlob*) == 8) ? 1 : -1];
typedef char fixture_release_slot[(offsetof(FixtureBlobVtable, Release) == 16) ? 1 : -1];
typedef char fixture_pointer_slot[(offsetof(FixtureBlobVtable, GetBufferPointer) == 24) ? 1 : -1];
typedef char fixture_size_slot[(offsetof(FixtureBlobVtable, GetBufferSize) == 32) ? 1 : -1];

static __declspec(thread) FixtureBlob s_code;
static __declspec(thread) FixtureBlob s_diagnostics;
static __declspec(thread) UINT s_mode;
static __declspec(thread) UINT s_active;
static __declspec(thread) UINT s_compiled;
static __declspec(thread) UINT s_observed;
static __declspec(thread) UINT s_error;
static const HRESULT s_failure = (HRESULT)0x80004005L;

static FixtureBlob* RequireLive(FixtureBlob* blob)
{
    if ((blob != &s_code && blob != &s_diagnostics) || !blob->refs || !blob->lpVtbl)
    {
        __fastfail(7);
    }
    return blob;
}

static HRESULT STDMETHODCALLTYPE FixtureQueryInterface(FixtureBlob* blob, const void* iid, void** result)
{
    (void)RequireLive(blob);
    (void)iid;
    s_error = 1;
    if (result)
    {
        *result = NULL;
    }
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE FixtureAddRef(FixtureBlob* blob)
{
    (void)RequireLive(blob);
    s_error = 1;
    return blob->refs;
}

static __declspec(noinline) uint64_t ClobberVolatile(
    uint64_t first, uint64_t second, uint64_t third, uint64_t fourth)
{
    volatile uint64_t value = first ^ second ^ third ^ fourth;
    return value;
}

static ULONG STDMETHODCALLTYPE FixtureRelease(FixtureBlob* blob)
{
    (void)RequireLive(blob);
    if (!s_observed)
    {
        s_error = 1;
    }
    blob->releases++;
    blob->refs--;
    if (!blob->refs)
    {
        blob->lpVtbl = NULL;
        memset(blob->bytes, 0xDD, sizeof(blob->bytes));
    }
    (void)ClobberVolatile(11, 22, 33, 44);
    return blob->refs;
}

static void* STDMETHODCALLTYPE FixtureGetPointer(FixtureBlob* blob)
{
    (void)RequireLive(blob);
    if (s_observed || blob->pointerCalls != blob->sizeCalls)
    {
        s_error = 1;
    }
    blob->pointerCalls++;
    (void)ClobberVolatile(55, 66, 77, 88);
    return blob->bytes;
}

static SIZE_T STDMETHODCALLTYPE FixtureGetSize(FixtureBlob* blob)
{
    (void)RequireLive(blob);
    if (s_observed || blob->pointerCalls != blob->sizeCalls + 1)
    {
        s_error = 1;
    }
    blob->sizeCalls++;
    (void)ClobberVolatile(99, 111, 222, 333);
    return blob->size;
}

static const FixtureBlobVtable s_vtable = {
    FixtureQueryInterface, FixtureAddRef, FixtureRelease, FixtureGetPointer, FixtureGetSize
};

static void InitializeBlob(FixtureBlob* blob, ULONG refs, SIZE_T size, uint8_t seed)
{
    memset(blob, 0, sizeof(*blob));
    blob->lpVtbl = &s_vtable;
    blob->refs = refs;
    blob->size = size;
    for (SIZE_T index = 0; index < size; index++)
    {
        blob->bytes[index] = (uint8_t)(seed + index);
    }
}

__declspec(dllexport) void WINAPI NativeFfiShaderMode(UINT mode)
{
    if (mode > 4 || s_active || s_code.refs || s_diagnostics.refs)
    {
        __fastfail(7);
    }
    memset(&s_code, 0, sizeof(s_code));
    memset(&s_diagnostics, 0, sizeof(s_diagnostics));
    s_mode = mode;
    s_active = 1;
    s_compiled = 0;
    s_observed = 0;
    s_error = 0;
}

/* This has the D3DCompileFromFile ABI, but is not D3DCompiler and never compiles or reads a shader. */
__declspec(dllexport) HRESULT WINAPI NativeFfiCompileFromFile(
    const WCHAR* file, const FixtureMacro* defines, FixtureInclude* include,
    const char* entry, const char* target, UINT flags1, UINT flags2,
    FixtureBlob** code, FixtureBlob** diagnostics)
{
    if (!s_active || s_compiled++)
    {
        __fastfail(7);
    }
    if (!file || wcscmp(file, L"fixture.hlsl") || defines || include
        || !entry || strcmp(entry, "main") || !target || strcmp(target, "vs_5_0")
        || flags1 || flags2 || !code || !diagnostics || code == diagnostics
        || ((uintptr_t)code & 7) || ((uintptr_t)diagnostics & 7)
        || *code || *diagnostics)
    {
        s_error = 1;
        return E_INVALIDARG;
    }
    if (s_mode == 0 || s_mode == 1 || s_mode == 4)
    {
        InitializeBlob(&s_code, s_mode == 4 ? 2 : 1, 19, 0x81);
        *code = &s_code;
    }
    if (s_mode == 1 || s_mode == 2)
    {
        InitializeBlob(&s_diagnostics, 1, 23, 0xA1);
        *diagnostics = &s_diagnostics;
    }
    if (s_mode == 4)
    {
        *diagnostics = &s_code;
    }
    (void)ClobberVolatile(444, 555, 666, 777);
    return s_mode == 2 || s_mode == 3 ? s_failure : S_OK;
}

static int CheckOutput(FixtureBlob* actual, const void* pointer, SIZE_T size, FixtureBlob* expected)
{
    if (!expected)
    {
        return actual == NULL && pointer == NULL && size == 0;
    }
    if (actual != expected || !expected->refs || expected->lpVtbl != &s_vtable
        || pointer != expected->bytes || size != expected->size || size > sizeof(expected->bytes))
    {
        return 0;
    }
    uint8_t seed = expected == &s_code ? 0x81 : 0xA1;
    for (SIZE_T index = 0; index < size; index++)
    {
        if (((const uint8_t*)pointer)[index] != (uint8_t)(seed + index))
        {
            return 0;
        }
    }
    return 1;
}

__declspec(dllexport) int32_t WINAPI NativeFfiObserveShaderOutputs(
    HRESULT result, FixtureBlob* code, const void* codePointer, SIZE_T codeSize,
    FixtureBlob* diagnostics, const void* diagnosticsPointer, SIZE_T diagnosticsSize)
{
    FixtureBlob* expectedCode = s_mode == 0 || s_mode == 1 || s_mode == 4 ? &s_code : NULL;
    FixtureBlob* expectedDiagnostics = s_mode == 4 ? &s_code
        : s_mode == 1 || s_mode == 2 ? &s_diagnostics : NULL;
    ULONG expectedCodeCalls = s_mode == 4 ? 2 : expectedCode ? 1 : 0;
    ULONG expectedDiagnosticCalls = s_mode == 1 || s_mode == 2 ? 1 : 0;
    HRESULT expectedResult = s_mode == 2 || s_mode == 3 ? s_failure : S_OK;
    if (!s_active || s_compiled != 1 || s_observed)
    {
        __fastfail(7);
    }
    s_observed = 1;
    if (s_error || result != expectedResult || (s_mode == 2 || s_mode == 3 ? result >= 0 : result < 0)
        || !CheckOutput(code, codePointer, codeSize, expectedCode)
        || !CheckOutput(diagnostics, diagnosticsPointer, diagnosticsSize, expectedDiagnostics)
        || s_code.refs != expectedCodeCalls || s_diagnostics.refs != expectedDiagnosticCalls
        || s_code.pointerCalls != expectedCodeCalls || s_code.sizeCalls != expectedCodeCalls
        || s_diagnostics.pointerCalls != expectedDiagnosticCalls
        || s_diagnostics.sizeCalls != expectedDiagnosticCalls || s_code.releases || s_diagnostics.releases)
    {
        return 1;
    }
    return 0;
}

/* Only inspect counts here. The generated caller, never this checker, must release every owned output. */
__declspec(dllexport) int32_t WINAPI NativeFfiFinishShader(int32_t observation)
{
    ULONG expectedCodeReleases = s_mode == 4 ? 2 : s_mode == 0 || s_mode == 1 ? 1 : 0;
    ULONG expectedDiagnosticReleases = s_mode == 1 || s_mode == 2 ? 1 : 0;
    if (!s_active || s_compiled != 1 || !s_observed || s_error || observation
        || s_code.refs || s_diagnostics.refs
        || s_code.releases != expectedCodeReleases || s_diagnostics.releases != expectedDiagnosticReleases
        || s_code.lpVtbl || s_diagnostics.lpVtbl)
    {
        return 2;
    }
    s_active = 0;
    return 0;
}

#if defined(NATIVE_SHADER_FIXTURE_SELF_TEST)
int main(void)
{
    for (UINT mode = 0; mode < 5; mode++)
    {
        FixtureBlob* code = NULL;
        FixtureBlob* diagnostics = NULL;
        NativeFfiShaderMode(mode);
        HRESULT result = NativeFfiCompileFromFile(L"fixture.hlsl", NULL, NULL, "main", "vs_5_0",
            0, 0, &code, &diagnostics);
        void* codePointer = code ? code->lpVtbl->GetBufferPointer(code) : NULL;
        SIZE_T codeSize = code ? code->lpVtbl->GetBufferSize(code) : 0;
        void* diagnosticsPointer = diagnostics ? diagnostics->lpVtbl->GetBufferPointer(diagnostics) : NULL;
        SIZE_T diagnosticsSize = diagnostics ? diagnostics->lpVtbl->GetBufferSize(diagnostics) : 0;
        if (NativeFfiObserveShaderOutputs(result ^ 1, code, codePointer, codeSize,
            diagnostics, diagnosticsPointer, diagnosticsSize) != 1)
        {
            return 1;
        }
        s_observed = 0;
        if (code && NativeFfiObserveShaderOutputs(result, code, codePointer, codeSize + 1,
            diagnostics, diagnosticsPointer, diagnosticsSize) != 1)
        {
            return 2;
        }
        s_observed = 0;
        int32_t observed = NativeFfiObserveShaderOutputs(result, code, codePointer, codeSize,
            diagnostics, diagnosticsPointer, diagnosticsSize);
        if (observed)
        {
            return 3;
        }
        if ((code || diagnostics) && NativeFfiFinishShader(observed) != 2)
        {
            return 4;
        }
        if (code)
        {
            code->lpVtbl->Release(code);
        }
        if (mode == 4 && NativeFfiFinishShader(observed) != 2)
        {
            return 5;
        }
        if (diagnostics)
        {
            diagnostics->lpVtbl->Release(diagnostics);
        }
        if (NativeFfiFinishShader(observed))
        {
            return 6;
        }
    }
    /* Verify rejected call shapes without manufacturing any references or cleanup. */
    for (UINT variant = 0; variant < 10; variant++)
    {
        FixtureBlob* code = NULL;
        FixtureBlob* diagnostics = NULL;
        NativeFfiShaderMode(3);
        if (variant == 9)
        {
            code = &s_code;
        }
        HRESULT result = NativeFfiCompileFromFile(variant == 0 ? L"wrong.hlsl" : L"fixture.hlsl",
            variant == 1 ? (const FixtureMacro*)1 : NULL, variant == 2 ? (FixtureInclude*)1 : NULL,
            variant == 3 ? "wrong" : "main", variant == 4 ? "wrong" : "vs_5_0",
            variant == 5 ? 1 : 0, variant == 6 ? 1 : 0, &code,
            variant == 7 ? &code : variant == 8 ? (FixtureBlob**)((uint8_t*)&diagnostics + 1) : &diagnostics);
        if (result != E_INVALIDARG || !s_error || s_code.refs || s_diagnostics.refs)
        {
            return 7;
        }
        s_active = 0;
    }
    return 0;
}
#endif
