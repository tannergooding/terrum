#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wchar.h>

static void Trace(char value)
{
    WCHAR path[MAX_PATH];
    DWORD length = GetEnvironmentVariableW(L"TERRUM_DX11_PATH_TRACE", path, MAX_PATH);
    if (!length || length >= MAX_PATH)
    {
        return;
    }
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE)
    {
        return;
    }
    DWORD written;
    WriteFile(file, &value, 1, &written, NULL);
    CloseHandle(file);
}

static void RecordPath(LPCWSTR path)
{
    WCHAR report[MAX_PATH];
    DWORD length = GetEnvironmentVariableW(L"TERRUM_DX11_PATH_RESULT", report, MAX_PATH);
    if (!length || length >= MAX_PATH)
    {
        return;
    }
    HANDLE file = CreateFileW(report, GENERIC_WRITE, FILE_SHARE_READ, NULL,
        CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE)
    {
        return;
    }
    DWORD written;
    WriteFile(file, path, ((DWORD)wcslen(path) + 1) * sizeof(WCHAR), &written, NULL);
    CloseHandle(file);
}

__declspec(dllexport) DWORD WINAPI NativeFfiShaderPathGetModuleFileNameW(
    HMODULE module, LPWSTR filename, DWORD size)
{
    if (module)
    {
        return GetModuleFileNameW(module, filename, size);
    }
    WCHAR mode[32];
    WCHAR fixture[260];
    DWORD modeLength = GetEnvironmentVariableW(L"TERRUM_DX11_PATH_MODE", mode, 32);
    Trace('Q');
    if (module || !filename || size != 260 || !modeLength || modeLength >= 32)
    {
        Trace('!');
        return 0;
    }
    if (wcscmp(mode, L"zero") == 0)
    {
        SetLastError(ERROR_ACCESS_DENIED);
        return 0;
    }
    if (wcscmp(mode, L"truncated") == 0)
    {
        for (DWORD index = 0; index < size; ++index)
        {
            filename[index] = L'x';
        }
        return size;
    }

    DWORD length = GetEnvironmentVariableW(L"TERRUM_DX11_PATH_FIXTURE", fixture, 260);
    if (!length || length >= 260 || length + 1 > size)
    {
        Trace('!');
        return 0;
    }
    CopyMemory(filename, fixture, (length + 1) * sizeof(WCHAR));
    return length;
}

__declspec(dllexport) DWORD WINAPI NativeFfiShaderPathGetLastError(void)
{
    Trace('E');
    return ERROR_ACCESS_DENIED;
}

__declspec(dllexport) HRESULT WINAPI NativeFfiShaderPathD3DCompileFromFile(
    LPCWSTR filename, const void* defines, void* include, LPCSTR entryPoint,
    LPCSTR target, UINT flags1, UINT flags2, void** code, void** errors)
{
    (void)entryPoint;
    (void)target;
    (void)defines;
    (void)include;
    (void)flags1;
    (void)flags2;
    if (code)
    {
        *code = NULL;
    }
    if (errors)
    {
        *errors = NULL;
    }
    Trace('C');
    if (filename)
    {
        RecordPath(filename);
    }
    return E_FAIL;
}

__declspec(dllexport) FARPROC WINAPI NativeFfiShaderPathGetProcAddress(HMODULE module, LPCSTR name)
{
    if (!module)
    {
        ExitProcess(91);
    }
    return strcmp(name, "D3DCompileFromFile") == 0
        ? (FARPROC)NativeFfiShaderPathD3DCompileFromFile : GetProcAddress(module, name);
}
