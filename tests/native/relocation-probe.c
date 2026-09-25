// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct Pair { int32_t First; int32_t Second; } Pair;
typedef Pair (*MakePair)(int32_t, int32_t);
typedef int32_t (*UsePair)(Pair, int32_t, int32_t, int32_t, int32_t);
typedef int32_t (*ReadData)(void);
typedef int32_t (*WriteData)(int32_t);
typedef int32_t (*Recursive)(int32_t);

static int CheckProcess(const char* path, uint32_t expected)
{
    STARTUPINFOA startup = { 0 };
    PROCESS_INFORMATION process = { 0 };
    startup.cb = sizeof(startup);
    if (!CreateProcessA(path, NULL, NULL, NULL, FALSE, DEBUG_ONLY_THIS_PROCESS | CREATE_NO_WINDOW,
        NULL, NULL, &startup, &process))
    {
        fprintf(stderr, "CreateProcessA failed: %lu\n", GetLastError());
        return 100;
    }
    const ULONGLONG deadline = GetTickCount64() + 10000;
    void* base = NULL;
    uint32_t exitCode = 0;
    int exited = 0;
    while (!exited)
    {
        ULONGLONG remaining = deadline - GetTickCount64();
        if (remaining > 10000 || remaining == 0)
        {
            break;
        }
        DEBUG_EVENT event;
        if (!WaitForDebugEvent(&event, (DWORD)remaining))
        {
            break;
        }
        if (event.dwDebugEventCode == CREATE_PROCESS_DEBUG_EVENT)
        {
            base = event.u.CreateProcessInfo.lpBaseOfImage;
            if (event.u.CreateProcessInfo.hFile != NULL)
            {
                CloseHandle(event.u.CreateProcessInfo.hFile);
            }
        }
        if (event.dwDebugEventCode == LOAD_DLL_DEBUG_EVENT && event.u.LoadDll.hFile != NULL)
        {
            CloseHandle(event.u.LoadDll.hFile);
        }
        if (event.dwDebugEventCode == EXIT_PROCESS_DEBUG_EVENT)
        {
            exitCode = event.u.ExitProcess.dwExitCode;
            exited = 1;
        }
        ContinueDebugEvent(event.dwProcessId, event.dwThreadId, DBG_CONTINUE);
    }
    if (!exited)
    {
        TerminateProcess(process.hProcess, 101);
        WaitForSingleObject(process.hProcess, 10000);
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    printf("process-base=%p preferred=%p exit=%lu\n", base, (void*)UINT64_C(0x140000000),
        (unsigned long)exitCode);
    if (!exited || base == NULL || exitCode != expected)
    {
        return 101;
    }
    return base == (void*)UINT64_C(0x140000000) ? 102 : 0;
}

static int CheckImage(HMODULE image, char** offsets)
{
    DWORD64 imageBase = 0;
    PRUNTIME_FUNCTION unwind = RtlLookupFunctionEntry((DWORD64)(uintptr_t)image + 0x1000, &imageBase, NULL);
    if (unwind == NULL || imageBase != (DWORD64)(uintptr_t)image)
    {
        return 96;
    }
    if (offsets == NULL)
    {
        return 0;
    }
    MakePair make = (MakePair)((uint8_t*)image + strtoul(offsets[0], NULL, 10));
    UsePair use = (UsePair)((uint8_t*)image + strtoul(offsets[1], NULL, 10));
    ReadData readData = (ReadData)((uint8_t*)image + strtoul(offsets[2], NULL, 10));
    WriteData writeData = (WriteData)((uint8_t*)image + strtoul(offsets[3], NULL, 10));
    Recursive recurse = (Recursive)((uint8_t*)image + strtoul(offsets[4], NULL, 10));
    Pair pair = make(17, 25);
    if (pair.First != 17 || pair.Second != 25
        || use(pair, 1, 2, 3, 11) != 59
        || readData() != 7 || writeData(39) != 42 || recurse(5) != 42)
    {
        return 95;
    }
    unwind = RtlLookupFunctionEntry((DWORD64)(uintptr_t)recurse, &imageBase, NULL);
    if (unwind == NULL || imageBase != (DWORD64)(uintptr_t)image
        || unwind->BeginAddress > strtoul(offsets[4], NULL, 10)
        || unwind->EndAddress <= strtoul(offsets[4], NULL, 10))
    {
        return 96;
    }
    return 0;
}

int main(int argc, char** argv)
{
    if (argc == 4 && strcmp(argv[1], "--process") == 0)
    {
        return CheckProcess(argv[2], (uint32_t)strtoul(argv[3], NULL, 10));
    }
    if (argc != 2 && argc != 3 && argc != 13)
    {
        return 90;
    }
    const uintptr_t preferred = UINT64_C(0x140000000);
    MEMORY_BASIC_INFORMATION region;
    if (VirtualQuery((const void*)preferred, &region, sizeof(region)) != sizeof(region))
    {
        return 91;
    }
    void* reservation = NULL;
    if (region.State == MEM_FREE)
    {
        reservation = VirtualAlloc((void*)preferred, 0x1000000, MEM_RESERVE, PAGE_NOACCESS);
        if (reservation != (void*)preferred)
        {
            return 92;
        }
    }

    HMODULE images[2] = { NULL, NULL };
    const int count = argc == 2 ? 1 : 2;
    for (int index = 0; index < count; index++)
    {
        images[index] = LoadLibraryExA(argv[1 + index], NULL, DONT_RESOLVE_DLL_REFERENCES);
        if (images[index] == NULL)
        {
            fprintf(stderr, "LoadLibraryExA failed: %lu\n", GetLastError());
            return 93;
        }
        if ((uintptr_t)images[index] == preferred || (index != 0 && images[index] == images[0]))
        {
            return 94;
        }
        int status = CheckImage(images[index], argc == 13 ? &argv[3 + (index * 5)] : NULL);
        if (status != 0)
        {
            return status;
        }
    }
    for (int index = 0; index < count; index++)
    {
        if (!FreeLibrary(images[index]))
        {
            return 97;
        }
    }
    if (reservation != NULL && !VirtualFree(reservation, 0, MEM_RELEASE))
    {
        return 98;
    }
    printf("base1=%p base2=%p preferred=%p\n", (void*)images[0], (void*)images[1], (void*)preferred);
    return 0;
}
