// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef int32_t (*Check)(void);
typedef int32_t (*Collect)(uint64_t);
typedef void (*ThrowingExport)(void);

static void* live[16];
static HANDLE heaps[16];
static int attempts;
static int releases;
static int failAttempt;
static int invalid;

static int LiveCount(void)
{
    int count = 0;
    for (int index = 0; index < 16; index++)
    {
        count += live[index] != NULL;
    }
    return count;
}

static LPVOID WINAPI TrackedAllocate(HANDLE heap, DWORD flags, SIZE_T bytes)
{
    attempts++;
    if ((flags & HEAP_GENERATE_EXCEPTIONS) != 0)
    {
        invalid = 1;
    }
    if (attempts == failAttempt)
    {
        return NULL;
    }
    void* allocation = HeapAlloc(heap, flags, bytes);
    if (!allocation)
    {
        invalid = 1;
        return NULL;
    }
    for (int index = 0; index < 16; index++)
    {
        if (!live[index])
        {
            live[index] = allocation;
            heaps[index] = heap;
            return allocation;
        }
    }
    invalid = 1;
    HeapFree(heap, 0, allocation);
    return NULL;
}

static BOOL WINAPI TrackedRelease(HANDLE heap, DWORD flags, LPVOID allocation)
{
    for (int index = 0; index < 16; index++)
    {
        if (allocation && live[index] == allocation && heaps[index] == heap)
        {
            BOOL result = HeapFree(heap, flags, allocation);
            if (result)
            {
                live[index] = NULL;
                heaps[index] = NULL;
                releases++;
            }
            else
            {
                invalid = 1;
            }
            return result;
        }
    }
    invalid = 1;
    return FALSE;
}

// BOOL's unused upper return-register bits cannot turn a failed release into success.
static uint64_t WINAPI RejectRelease(HANDLE heap, DWORD flags, LPVOID allocation)
{
    (void)heap;
    (void)flags;
    (void)allocation;
    return UINT64_C(0xA5A5A5A500000000);
}

static int ReplaceImport(HMODULE image, const char* name, void* replacement)
{
    unsigned char* base = (unsigned char*)image;
    IMAGE_DOS_HEADER* dos = (IMAGE_DOS_HEADER*)base;
    IMAGE_NT_HEADERS64* nt = (IMAGE_NT_HEADERS64*)(base + dos->e_lfanew);
    IMAGE_IMPORT_DESCRIPTOR* descriptor = (IMAGE_IMPORT_DESCRIPTOR*)(base
        + nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);
    for (; descriptor->Name != 0; descriptor++)
    {
        IMAGE_THUNK_DATA64* lookup = (IMAGE_THUNK_DATA64*)(base + descriptor->OriginalFirstThunk);
        IMAGE_THUNK_DATA64* address = (IMAGE_THUNK_DATA64*)(base + descriptor->FirstThunk);
        for (; lookup->u1.AddressOfData != 0; lookup++, address++)
        {
            if (IMAGE_SNAP_BY_ORDINAL64(lookup->u1.Ordinal))
            {
                continue;
            }
            IMAGE_IMPORT_BY_NAME* imported = (IMAGE_IMPORT_BY_NAME*)(base + lookup->u1.AddressOfData);
            if (strcmp((const char*)imported->Name, name) == 0)
            {
                DWORD protection;
                DWORD ignored;
                if (!VirtualProtect(&address->u1.Function, sizeof(void*), PAGE_READWRITE, &protection))
                {
                    return 0;
                }
                InterlockedExchangePointer((PVOID volatile*)&address->u1.Function, replacement);
                return VirtualProtect(&address->u1.Function, sizeof(void*), protection, &ignored) != 0;
            }
        }
    }
    return 0;
}

#define VERIFY(condition, code) do { \
    if (!(condition)) { \
        fprintf(stderr, "Managed-node ABI check failed at line %d.\n", __LINE__); \
        return code; \
    } \
} while (0)

static int RunChecks(HMODULE image)
{
    Check cycles = (Check)GetProcAddress(image, "Cycles");
    Collect collect = (Collect)GetProcAddress(image, "CollectWithScalar");
    Check failure = (Check)GetProcAddress(image, "AllocationFailure");
    Check retain = (Check)GetProcAddress(image, "RetainAcrossAllocationFailure");
    VERIFY(cycles && collect && failure && retain, 10);
    VERIFY(ReplaceImport(image, "HeapAlloc", (void*)TrackedAllocate), 11);
    VERIFY(ReplaceImport(image, "HeapFree", (void*)TrackedRelease), 12);

    for (int iteration = 0; iteration < 64; iteration++)
    {
        attempts = 0;
        releases = 0;
        int32_t result = cycles();
        if (result != 42)
        {
            fprintf(stderr, "Cycle round %d returned %d; allocations=%d releases=%d live=%d invalid=%d.\n",
                iteration, result, attempts, releases, LiveCount(), invalid);
        }
        VERIFY(result == 42, 20);
        VERIFY(attempts == 4 && releases == 2 && LiveCount() == 2 && !invalid, 21);
        uint64_t pointerBits = 0;
        for (int index = 0; index < 16; index++)
        {
            if (live[index])
            {
                pointerBits = (uint64_t)(uintptr_t)live[index];
                break;
            }
        }
        // A pointer-shaped scalar in either native frame is not a managed root.
        VERIFY(pointerBits != 0 && collect(pointerBits) == 2, 22);
        VERIFY(attempts == 4 && releases == 4 && LiveCount() == 0 && !invalid, 23);
        VERIFY(collect(pointerBits) == 0, 24);
    }

    attempts = 0;
    releases = 0;
    failAttempt = 1;
    VERIFY(failure() == 42, 30);
    VERIFY(attempts == 1 && releases == 0 && LiveCount() == 0 && !invalid, 31);
    VERIFY(collect(0) == 0, 32);

    attempts = 0;
    releases = 0;
    failAttempt = 2;
    VERIFY(retain() == 42, 40);
    VERIFY(attempts == 2 && releases == 0 && LiveCount() == 1 && !invalid, 41);
    VERIFY(collect(0) == 1, 42);
    VERIFY(releases == 1 && LiveCount() == 0 && !invalid, 43);
    return 0;
}

static int CatchRequirement(EXCEPTION_POINTERS* pointers)
{
    const EXCEPTION_RECORD* record = pointers->ExceptionRecord;
    return record->ExceptionCode == 0xE0540092u
        && (record->ExceptionFlags & EXCEPTION_NONCONTINUABLE) != 0
        && record->NumberParameters == 3
        && record->ExceptionInformation[0] == UINT64_C(0x54455252554D0001)
        && record->ExceptionInformation[1] == 1
        && record->ExceptionInformation[2] == 3
        ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH;
}

static int RunUnwindChecks(HMODULE image)
{
    const char* names[] = {
        "SameFrame", "EscapingFrame", "LaterArgument", "OutputFailure", "ReturnAcrossCleanup"
    };
    const int allocations[] = { 2, 3, 3, 2, 2 };
    Collect collect = (Collect)GetProcAddress(image, "CollectWithScalar");
    ThrowingExport escape = (ThrowingExport)GetProcAddress(image, "EscapeToNative");
    VERIFY(collect && escape, 50);
    VERIFY(ReplaceImport(image, "HeapAlloc", (void*)TrackedAllocate), 51);
    VERIFY(ReplaceImport(image, "HeapFree", (void*)TrackedRelease), 52);

    for (int iteration = 0; iteration < 64; iteration++)
    {
        for (int index = 0; index < 5; index++)
        {
            Check check = (Check)GetProcAddress(image, names[index]);
            VERIFY(check != NULL, 53);
            attempts = 0;
            releases = 0;
            VERIFY(check() == 42, 54);
            VERIFY(attempts == allocations[index] && releases == attempts - 1
                && LiveCount() == 1 && !invalid, 55);
            VERIFY(collect(0) == 1, 56);
            VERIFY(releases == attempts && LiveCount() == 0 && !invalid, 57);
        }

        attempts = 0;
        releases = 0;
        int caught = 0;
        __try
        {
            escape();
        }
        __except (CatchRequirement(GetExceptionInformation()))
        {
            caught = 1;
        }
        // The image's shadow-root chain must no longer reference either departed Terrum frame.
        VERIFY(caught && attempts == 2 && releases == 0 && LiveCount() == 2, 58);
        VERIFY(collect(0) == 2, 59);
        VERIFY(releases == 2 && LiveCount() == 0 && !invalid, 60);
    }
    return 0;
}

int main(int argc, char** argv)
{
    if (argc != 2 && argc != 3)
    {
        return 2;
    }
    HMODULE image = LoadLibraryExA(argv[1], NULL,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!image)
    {
        fprintf(stderr, "Cannot load managed-node ABI fixture: %lu.\n", GetLastError());
        return 3;
    }
    if (argc == 3 && strcmp(argv[2], "reject-release") == 0)
    {
        Check cycles = (Check)GetProcAddress(image, "Cycles");
        if (!cycles || !ReplaceImport(image, "HeapFree", (void*)RejectRelease))
        {
            FreeLibrary(image);
            return 4;
        }
        (void)cycles();
        FreeLibrary(image);
        return 5;
    }
    int unwind = argc == 3 && strcmp(argv[2], "unwind") == 0;
    int result = unwind ? RunUnwindChecks(image) : RunChecks(image);
    FreeLibrary(image);
    if (result == 0)
    {
        puts(unwind
            ? "managed-node-unwind rounds=64 lexical=checked staging=checked native-catch=checked live=0"
            : "managed-node-abi roots=precise cycle-rounds=64 publication=checked failures=checked live=0");
    }
    return result;
}
