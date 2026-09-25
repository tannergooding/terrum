// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef struct Twelve { int32_t First; int32_t Second; int32_t Third; } Twelve;
typedef struct Wide { int64_t Signed; uint64_t Unsigned; } Wide;
typedef Twelve (*MakeTwelve)(int32_t, int32_t, int32_t);
typedef Wide (*MakeWide)(int64_t, uint64_t);
typedef int32_t (*Evaluate)(Twelve, Wide, int64_t, int32_t, int32_t);
typedef uint8_t (*TryWide)(uint8_t, Wide*);

static int CheckLibrary(const char* path, int32_t expected)
{
    HMODULE image = LoadLibraryA(path);
    if (image == NULL)
    {
        fprintf(stderr, "LoadLibraryA failed: %lu\n", GetLastError());
        return 91;
    }
    const IMAGE_DOS_HEADER* dos = (const IMAGE_DOS_HEADER*)image;
    const IMAGE_NT_HEADERS64* nt = (const IMAGE_NT_HEADERS64*)((const uint8_t*)image + dos->e_lfanew);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || nt->Signature != IMAGE_NT_SIGNATURE
        || nt->OptionalHeader.AddressOfEntryPoint != 0
        || !(nt->FileHeader.Characteristics & IMAGE_FILE_DLL)
        || (nt->FileHeader.Characteristics & IMAGE_FILE_RELOCS_STRIPPED)
        || (uintptr_t)image == (uintptr_t)UINT64_C(0x140000000))
    {
        fprintf(stderr, "image=%p base=0x%llx entry=%lx flags=%x mz=%x pe=%lx\n",
            (void*)image, (unsigned long long)nt->OptionalHeader.ImageBase,
            (unsigned long)nt->OptionalHeader.AddressOfEntryPoint, nt->FileHeader.Characteristics,
            dos->e_magic, (unsigned long)nt->Signature);
        return 92;
    }
    MakeTwelve makeTwelve = (MakeTwelve)GetProcAddress(image, "MakeTwelve");
    MakeWide makeWide = (MakeWide)GetProcAddress(image, "MakeWide");
    Evaluate evaluate = (Evaluate)GetProcAddress(image, "Evaluate");
    TryWide tryWide = (TryWide)GetProcAddress(image, "TryWide");
    if (makeTwelve == NULL || makeWide == NULL || evaluate == NULL || tryWide == NULL
        || GetProcAddress(image, "Hidden") != NULL || GetProcAddress(image, "Unknown") != NULL)
    {
        return 93;
    }
    Twelve twelve = makeTwelve(10, 11, 12);
    Wide wide = makeWide(INT64_MIN, UINT64_MAX);
    if (twelve.First != 10 || twelve.Second != 11 || twelve.Third != 12
        || wide.Signed != INT64_MIN || wide.Unsigned != UINT64_MAX
        || evaluate(twelve, wide, INT64_C(2147483648), 1, 2) != expected)
    {
        return 94;
    }
    __declspec(align(16)) Wide guarded[3] = {{1, 2}, {3, 4}, {5, 6}};
    if (!tryWide(1, &guarded[1]) || guarded[1].Signed != INT64_MIN
        || guarded[1].Unsigned != UINT64_MAX || guarded[0].Signed != 1
        || guarded[0].Unsigned != 2 || guarded[2].Signed != 5 || guarded[2].Unsigned != 6
        || tryWide(0, &guarded[1]) || guarded[1].Unsigned != UINT64_MAX)
    {
        return 95;
    }
    DWORD64 base = 0;
    if (RtlLookupFunctionEntry((DWORD64)(uintptr_t)evaluate, &base, NULL) == NULL
        || base != (DWORD64)(uintptr_t)image)
    {
        return 96;
    }
    printf("library=%p result=%ld\n", (void*)image, (long)expected);
    if (!FreeLibrary(image) || GetModuleHandleA(path) != NULL)
    {
        return 97;
    }
    return 0;
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        return 90;
    }
    if (strcmp(argv[1], "--failure") == 0)
    {
        HMODULE image = LoadLibraryA(argv[2]);
        if (image == NULL)
        {
            return 98;
        }
        Evaluate evaluate = (Evaluate)GetProcAddress(image, "Evaluate");
        if (evaluate == NULL)
        {
            return 99;
        }
        Twelve value = {10, 11, 12};
        Wide invalid = {0, 0};
        evaluate(value, invalid, INT64_C(2147483648), 1, 2);
        return 100;
    }
    void* preferred = (void*)(uintptr_t)UINT64_C(0x140000000);
    MEMORY_BASIC_INFORMATION region;
    if (VirtualQuery(preferred, &region, sizeof(region)) == 0)
    {
        return 89;
    }
    void* reservation = NULL;
    if (region.State == MEM_FREE)
    {
        reservation = VirtualAlloc(preferred, 0x10000, MEM_RESERVE, PAGE_NOACCESS);
        if (reservation != preferred)
        {
            return 88;
        }
    }
    int first = CheckLibrary(argv[1], 42);
    int second = first == 0 ? CheckLibrary(argv[2], 43) : first;
    if (reservation != NULL && !VirtualFree(reservation, 0, MEM_RELEASE))
    {
        return 87;
    }
    return second;
}
