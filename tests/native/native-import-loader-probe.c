#define _WIN32_WINNT 0x0601
#include <windows.h>
#include <wchar.h>
#include <string.h>

#pragma comment(lib, "User32.lib")
#pragma comment(linker, "/include:__imp_MessageBoxW")

int main(void)
{
    wchar_t loadedPath[MAX_PATH];
    wchar_t systemPath[MAX_PATH];
    HMODULE user32 = GetModuleHandleW(L"user32.dll");
    if (user32 == NULL)
    {
        return 1;
    }

    DWORD loadedLength = GetModuleFileNameW(user32, loadedPath, MAX_PATH);
    UINT systemLength = GetSystemDirectoryW(systemPath, MAX_PATH);
    static const wchar_t suffix[] = L"\\user32.dll";
    if (loadedLength == 0 || loadedLength >= MAX_PATH
        || systemLength == 0 || systemLength + _countof(suffix) > MAX_PATH)
    {
        return 2;
    }

    memcpy(systemPath + systemLength, suffix, sizeof(suffix));
    return CompareStringOrdinal(loadedPath, -1, systemPath, -1, TRUE) == CSTR_EQUAL ? 0 : 3;
}
