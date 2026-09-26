// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef int32_t (*Check)(void);
typedef int32_t (*Collect)(uint64_t);
typedef void (*ThrowingExport)(void);

enum { TrackedCapacity = 4096 };
static void* live[TrackedCapacity];
static HANDLE heaps[TrackedCapacity];
static int attempts;
static int releases;
static int failAttempt;
static int invalid;

static int LiveCount(void)
{
    int count = 0;
    for (int index = 0; index < TrackedCapacity; index++)
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
    for (int index = 0; index < TrackedCapacity; index++)
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
    for (int index = 0; index < TrackedCapacity; index++)
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

static int RunClassChecks(HMODULE image, int checkedUnwind)
{
    const char* names[] = { "MixedEdges", "AliasUpdates", "DuringConstruction", "AssignmentAcrossCollection" };
    const int allocations[] = { 4, 3, 3, 3 };
    const int survivors[] = { 4, 3, 2, 2 };
    Collect collect = (Collect)GetProcAddress(image, "CollectWithScalar");
    Collect scalar = (Collect)GetProcAddress(image, "ScalarNonRoot");
    Check loose = (Check)GetProcAddress(image, "CreateLoose");
    Check failure = (Check)GetProcAddress(image, "AllocationFailure");
    Check retain = (Check)GetProcAddress(image, "RetainAcrossAllocationFailure");
    VERIFY(collect && scalar && loose && failure && retain, 70);
    VERIFY(ReplaceImport(image, "HeapAlloc", (void*)TrackedAllocate), 71);
    VERIFY(ReplaceImport(image, "HeapFree", (void*)TrackedRelease), 72);

    for (int iteration = 0; iteration < 64; iteration++)
    {
        for (int index = 0; index < 4; index++)
        {
            Check check = (Check)GetProcAddress(image, names[index]);
            VERIFY(check != NULL, 73);
            attempts = 0;
            releases = 0;
            VERIFY(check() == 42, 74);
            VERIFY(attempts == allocations[index] && LiveCount() == survivors[index]
                && releases == attempts - survivors[index] && !invalid, 75);
            VERIFY(collect(0) == survivors[index], 76);
            VERIFY(releases == attempts && LiveCount() == 0 && !invalid, 77);
        }
        attempts = 0;
        releases = 0;
        VERIFY(loose() == 42 && attempts == 2 && releases == 0 && LiveCount() == 2, 78);
        uint64_t pointerBits = 0;
        for (int index = 0; index < 16; index++)
        {
            if (live[index])
            {
                pointerBits = (uint64_t)(uintptr_t)live[index];
                break;
            }
        }
        VERIFY(pointerBits != 0 && scalar(pointerBits) == 42, 79);
        VERIFY(attempts == 3 && releases == 2 && LiveCount() == 1 && !invalid, 80);
        VERIFY(collect(0) == 1 && releases == 3 && LiveCount() == 0 && !invalid, 81);
    }

    attempts = 0;
    releases = 0;
    failAttempt = 1;
    VERIFY(failure() == 42 && attempts == 1 && releases == 0 && LiveCount() == 0 && !invalid, 82);
    attempts = 0;
    failAttempt = 2;
    VERIFY(retain() == 42 && attempts == 2 && releases == 0 && LiveCount() == 1 && !invalid, 83);
    VERIFY(collect(0) == 1 && releases == 1 && LiveCount() == 0 && !invalid, 84);
    failAttempt = 0;

    if (checkedUnwind)
    {
        Check constructorFailure = (Check)GetProcAddress(image, "ConstructorFailure");
        Check assignmentFailure = (Check)GetProcAddress(image, "AssignmentFailure");
        ThrowingExport escape = (ThrowingExport)GetProcAddress(image, "EscapeConstructorToNative");
        VERIFY(constructorFailure && assignmentFailure && escape, 85);
        for (int iteration = 0; iteration < 64; iteration++)
        {
            attempts = 0;
            releases = 0;
            VERIFY(assignmentFailure() == 42 && attempts == 3 && releases == 1
                && LiveCount() == 2 && !invalid, 90);
            VERIFY(collect(0) == 2 && releases == 3 && LiveCount() == 0 && !invalid, 91);
            attempts = 0;
            releases = 0;
            VERIFY(constructorFailure() == 42 && attempts == 2 && releases == 1
                && LiveCount() == 1 && !invalid, 86);
            VERIFY(collect(0) == 1 && releases == 2 && LiveCount() == 0 && !invalid, 87);
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
            VERIFY(caught && attempts == 2 && releases == 1 && LiveCount() == 1 && !invalid, 88);
            VERIFY(collect(0) == 1 && releases == 2 && LiveCount() == 0 && !invalid, 89);
        }
    }
    return 0;
}

static int RunOptionalChecks(HMODULE image, int checkedUnwind)
{
    const char* names[] = { "OptionalCycles", "OptionalSnapshots", "OptionalNullOutput" };
    const int allocations[] = { 3, 3, 0 };
    const int survivors[] = { 0, 1, 0 };
    Collect collect = (Collect)GetProcAddress(image, "CollectWithScalar");
    Check allocationFailure = (Check)GetProcAddress(image, "OptionalAllocationFailure");
    Check retain = (Check)GetProcAddress(image, "OptionalRetainAcrossFailure");
    VERIFY(collect && allocationFailure && retain, 100);
    VERIFY(ReplaceImport(image, "HeapAlloc", (void*)TrackedAllocate), 101);
    VERIFY(ReplaceImport(image, "HeapFree", (void*)TrackedRelease), 102);

    for (int iteration = 0; iteration < 64; iteration++)
    {
        for (int index = 0; index < 3; index++)
        {
            Check check = (Check)GetProcAddress(image, names[index]);
            VERIFY(check != NULL, 103);
            attempts = 0;
            releases = 0;
            VERIFY(check() == 42, 104);
            VERIFY(attempts == allocations[index] && LiveCount() == survivors[index]
                && releases == attempts - survivors[index] && !invalid, 105);
            VERIFY(collect(0) == survivors[index], 106);
            VERIFY(releases == attempts && LiveCount() == 0 && !invalid, 107);
        }
        if (checkedUnwind)
        {
            Check recover = (Check)GetProcAddress(image, "OptionalRecovery");
            ThrowingExport escape = (ThrowingExport)GetProcAddress(image, "OptionalEscapeToNative");
            VERIFY(recover && escape, 108);
            attempts = 0;
            releases = 0;
            VERIFY(recover() == 42 && attempts == 3 && releases == 2
                && LiveCount() == 1 && !invalid, 109);
            VERIFY(collect(0) == 1 && releases == 3 && LiveCount() == 0 && !invalid, 110);
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
            VERIFY(caught && attempts == 3 && releases == 0 && LiveCount() == 3 && !invalid, 111);
            VERIFY(collect(0) == 3 && releases == 3 && LiveCount() == 0 && !invalid, 112);
        }
    }
    attempts = 0;
    releases = 0;
    failAttempt = 1;
    VERIFY(allocationFailure() == 42 && attempts == 1 && releases == 0
        && LiveCount() == 0 && !invalid, 113);
    attempts = 0;
    failAttempt = 3;
    VERIFY(retain() == 42 && attempts == 3 && releases == 0 && LiveCount() == 2 && !invalid, 114);
    VERIFY(collect(0) == 2 && releases == 2 && LiveCount() == 0 && !invalid, 115);
    failAttempt = 0;
    return 0;
}

static int RunTokenChainChecks(HMODULE image)
{
    Check reuse = (Check)GetProcAddress(image, "ChainReuse");
    Check allocationFailure = (Check)GetProcAddress(image, "ChainAllocationFailure");
    Collect collect = (Collect)GetProcAddress(image, "CollectWithScalar");
    VERIFY(reuse && allocationFailure && collect, 120);
    VERIFY(ReplaceImport(image, "HeapAlloc", (void*)TrackedAllocate), 121);
    VERIFY(ReplaceImport(image, "HeapFree", (void*)TrackedRelease), 122);
    for (int iteration = 0; iteration < 64; iteration++)
    {
        attempts = 0;
        releases = 0;
        VERIFY(reuse() == 42 && attempts == 6 && releases == 5 && LiveCount() == 1 && !invalid, 123);
        VERIFY(collect(0) == 1 && releases == 6 && LiveCount() == 0 && !invalid, 124);
        attempts = 0;
        releases = 0;
        failAttempt = 3;
        VERIFY(allocationFailure() == 42 && attempts == 3 && releases == 0
            && LiveCount() == 2 && !invalid, 125);
        VERIFY(collect(0) == 2 && releases == 2 && LiveCount() == 0 && !invalid, 126);
        failAttempt = 0;
    }
    return 0;
}

static int RunClassCapacityChecks(HMODULE image)
{
    Check fill = (Check)GetProcAddress(image, "CapacityCycle");
    Collect collect = (Collect)GetProcAddress(image, "CollectWithScalar");
    VERIFY(fill && collect, 130);
    VERIFY(ReplaceImport(image, "HeapAlloc", (void*)TrackedAllocate), 131);
    VERIFY(ReplaceImport(image, "HeapFree", (void*)TrackedRelease), 132);
    for (int iteration = 0; iteration < 2; iteration++)
    {
        attempts = 0;
        releases = 0;
        VERIFY(fill() == 42 && attempts == TrackedCapacity
            && releases == 0 && LiveCount() == TrackedCapacity && !invalid, 133);
        VERIFY(collect(0) == TrackedCapacity && releases == attempts
            && LiveCount() == 0 && !invalid, 134);
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
    if (argc == 3 && strcmp(argv[2], "classes-reject-release") == 0)
    {
        Check failure = (Check)GetProcAddress(image, "ConstructorFailure");
        if (!failure || !ReplaceImport(image, "HeapFree", (void*)RejectRelease))
        {
            FreeLibrary(image);
            return 4;
        }
        (void)failure();
        FreeLibrary(image);
        return 5;
    }
    int unwind = argc == 3 && strcmp(argv[2], "unwind") == 0;
    int checkedClasses = argc == 3 && strcmp(argv[2], "classes-checked") == 0;
    int classes = checkedClasses || (argc == 3 && strcmp(argv[2], "classes") == 0);
    int checkedOptional = argc == 3 && strcmp(argv[2], "optional-checked") == 0;
    int optional = checkedOptional || (argc == 3 && strcmp(argv[2], "optional") == 0);
    int chain = argc == 3 && strcmp(argv[2], "chain") == 0;
    int capacity = argc == 3 && strcmp(argv[2], "class-capacity") == 0;
    if (argc == 3 && !unwind && !classes && !optional && !chain && !capacity)
    {
        fprintf(stderr, "Unknown managed-heap probe mode: %s.\n", argv[2]);
        FreeLibrary(image);
        return 2;
    }
    int result = capacity ? RunClassCapacityChecks(image)
        : chain ? RunTokenChainChecks(image)
        : optional ? RunOptionalChecks(image, checkedOptional)
        : classes ? RunClassChecks(image, checkedClasses)
        : unwind ? RunUnwindChecks(image) : RunChecks(image);
    FreeLibrary(image);
    if (result == 0)
    {
        puts(capacity
            ? "managed-class-capacity rounds=2 live-limit=4096 refusal=checked reuse=checked live=0"
            : chain
            ? "managed-token-chain rounds=64 reuse=checked allocation-failure=checked live=0"
            : optional
            ? checkedOptional
                ? "managed-optionals rounds=64 cycles=checked snapshots=checked unwind=checked live=0"
                : "managed-optionals rounds=64 cycles=checked snapshots=checked unwind=disabled live=0"
            : classes
            ? checkedClasses
                ? "managed-classes rounds=64 descriptors=checked construction=checked unwind=checked live=0"
                : "managed-classes rounds=64 descriptors=checked construction=checked unwind=disabled live=0"
            : unwind
            ? "managed-node-unwind rounds=64 lexical=checked staging=checked native-catch=checked live=0"
            : "managed-node-abi roots=precise cycle-rounds=64 publication=checked failures=checked live=0");
    }
    return result;
}
