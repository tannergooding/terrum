// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>

typedef int (*SixArguments)(int, int, int, int, int, int);
typedef unsigned char (*TryWrite)(int*);
typedef void (*Cleanup)(unsigned char, void*);

static DWORD64 expectedBase;
static DWORD64 expectedCleanup;
static DWORD64 expectedCallerRbp;
static LONG checkedUnwind;
static LONG checkedCleanupUnwind;

static int ReportUnexpectedFault(EXCEPTION_POINTERS* pointers)
{
    fprintf(stderr, "Unexpected native fault: code=%08lX rva=%llX\n",
        pointers->ExceptionRecord->ExceptionCode,
        (unsigned long long)(pointers->ContextRecord->Rip - expectedBase));
    return EXCEPTION_EXECUTE_HANDLER;
}

static int InspectFault(EXCEPTION_POINTERS* pointers)
{
    DWORD64 base = 0;
    DWORD64 frame = 0;
    PVOID handlerData = NULL;
    CONTEXT context = *pointers->ContextRecord;
    PRUNTIME_FUNCTION function;
    if (pointers->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION)
    {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    function = RtlLookupFunctionEntry(context.Rip, &base, NULL);
    if (function == NULL || base != expectedBase || context.Rbp + 16 > context.Rsp + 0xF00)
    {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, base, context.Rip, function, &context, &handlerData, &frame, NULL);
    if (frame != pointers->ContextRecord->Rbp || context.Rbp == frame
        || context.Rsp <= frame || handlerData != NULL || context.Rip == 0)
    {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    checkedUnwind = 1;
    return EXCEPTION_EXECUTE_HANDLER;
}

static int InspectCleanupFault(EXCEPTION_POINTERS* pointers)
{
    DWORD64 base = 0;
    DWORD64 frame = 0;
    PVOID handlerData = NULL;
    CONTEXT context = *pointers->ContextRecord;
    PRUNTIME_FUNCTION function;
    if (pointers->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION || context.Rbp != 1)
    {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    function = RtlLookupFunctionEntry(context.Rip, &base, NULL);
    if (function == NULL || base != expectedBase || base + function->BeginAddress != expectedCleanup)
    {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, base, context.Rip, function, &context, &handlerData, &frame, NULL);
    if (frame != pointers->ContextRecord->Rsp || frame == pointers->ContextRecord->Rbp
        || context.Rbp != expectedCallerRbp || context.Rsp <= frame || handlerData != NULL || context.Rip == 0)
    {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    checkedCleanupUnwind = 1;
    return EXCEPTION_EXECUTE_HANDLER;
}

static int CheckPrologUnwind(PRUNTIME_FUNCTION function, DWORD64 base)
{
    static const DWORD offsets[] = { 0, 1, 8, 11 };
    const BYTE* code = (const BYTE*)(uintptr_t)(base + function->BeginAddress);
    const BYTE* info = (const BYTE*)(uintptr_t)(base + function->UnwindData);
    DWORD64 stack[512] = { 0 };
    DWORD64* entry = &stack[511];
    DWORD64 savedRbp = 0x12345678;
    DWORD64 returnAddress = (DWORD64)(uintptr_t)&CheckPrologUnwind;
    DWORD allocation = (DWORD)(info[6] | (info[7] << 8)) * 8;
    DWORD index;
    if (info[2] != 3 || allocation + 8 > 0xF00 || (allocation & 15) != 0)
    {
        return 0;
    }
    entry[-1] = savedRbp;
    entry[0] = returnAddress;
    for (index = 0; index < sizeof(offsets) / sizeof(offsets[0]); index++)
    {
        CONTEXT context = { 0 };
        DWORD64 frame = 0;
        DWORD64 initialRsp;
        PVOID handlerData = NULL;
        context.Rip = base + function->BeginAddress + offsets[index];
        context.Rsp = (DWORD64)(uintptr_t)entry;
        if (offsets[index] >= 1)
        {
            context.Rsp -= 8;
        }
        if (offsets[index] >= 8)
        {
            context.Rsp -= allocation;
        }
        initialRsp = context.Rsp;
        context.Rbp = offsets[index] < 11 ? savedRbp : code[10] == 0xD5 ? 1 : initialRsp;
        RtlVirtualUnwind(UNW_FLAG_NHANDLER, base, context.Rip, function, &context, &handlerData, &frame, NULL);
        if (context.Rip != returnAddress || context.Rbp != savedRbp
            || context.Rsp != (DWORD64)(uintptr_t)(entry + 1) || frame != initialRsp || handlerData != NULL)
        {
            fprintf(stderr, "Incorrect partial-prolog unwind at offset %lu.\n", offsets[index]);
            return 0;
        }
    }
    return 1;
}

int wmain(int argc, wchar_t** argv)
{
    HMODULE library;
    SixArguments mix;
    TryWrite write;
    DWORD64 base = 0;
    PRUNTIME_FUNCTION function;
    const IMAGE_DOS_HEADER* dos;
    const IMAGE_NT_HEADERS64* nt;
    const RUNTIME_FUNCTION* table;
    DWORD tableSize;
    DWORD index;
    int funclets = 0;
    const BYTE* info;
    int value = 0;
    int valid = 0;
    CONTEXT caller = { 0 };
    if (argc != 2 || (library = LoadLibraryW(argv[1])) == NULL)
    {
        fprintf(stderr, "Cannot load native library: %lu\n", GetLastError());
        return 1;
    }
    mix = (SixArguments)(uintptr_t)GetProcAddress(library, "Mix");
    write = (TryWrite)(uintptr_t)GetProcAddress(library, "TryWrite");
    expectedBase = (DWORD64)(uintptr_t)library;
    if (mix == NULL || write == NULL)
    {
        fprintf(stderr, "Native export ABI differs.\n");
        FreeLibrary(library);
        return 1;
    }
    __try
    {
        valid = mix(1, 2, 3, 4, 5, 6) == 42 && write(&value) == 1 && value == 42;
    }
    __except(ReportUnexpectedFault(GetExceptionInformation()))
    {
        FreeLibrary(library);
        return 1;
    }
    if (!valid)
    {
        fprintf(stderr, "Native export ABI differs.\n");
        FreeLibrary(library);
        return 1;
    }
    function = RtlLookupFunctionEntry((DWORD64)(uintptr_t)write, &base, NULL);
    if (function == NULL || base != expectedBase)
    {
        fprintf(stderr, "Native unwind entry is unavailable.\n");
        FreeLibrary(library);
        return 1;
    }
    info = (const BYTE*)(uintptr_t)(base + function->UnwindData);
    if (info[0] != 1 || info[1] != 11 || info[2] != 3 || info[3] != 0
        || info[4] != 8 || info[5] != 1 || info[8] != 1 || info[9] != 0x50)
    {
        fprintf(stderr, "Saved-RBP unwind operations differ.\n");
        FreeLibrary(library);
        return 1;
    }
    if (!CheckPrologUnwind(function, base))
    {
        FreeLibrary(library);
        return 1;
    }
    dos = (const IMAGE_DOS_HEADER*)(uintptr_t)library;
    nt = (const IMAGE_NT_HEADERS64*)((const BYTE*)library + dos->e_lfanew);
    tableSize = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION].Size;
    table = (const RUNTIME_FUNCTION*)((const BYTE*)library
        + nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION].VirtualAddress);
    for (index = 0; index < tableSize / sizeof(RUNTIME_FUNCTION); index++)
    {
        const BYTE* code = (const BYTE*)library + table[index].BeginAddress;
        if (table[index].EndAddress - table[index].BeginAddress >= 11
            && code[0] == 0x55 && code[8] == 0x48 && code[9] == 0x89 && code[10] == 0xD5)
        {
            const BYTE* unwind = (const BYTE*)library + table[index].UnwindData;
            DWORD64 funcletBase = 0;
            PRUNTIME_FUNCTION found = RtlLookupFunctionEntry((DWORD64)(uintptr_t)(code + 11), &funcletBase, NULL);
            if (unwind[0] != 1 || unwind[1] != 11 || unwind[2] != 3
                || unwind[8] != 1 || unwind[9] != 0x50 || found == NULL
                || funcletBase != expectedBase || found->BeginAddress != table[index].BeginAddress)
            {
                fprintf(stderr, "Cleanup funclet unwind record differs.\n");
                FreeLibrary(library);
                return 1;
            }
            if (!CheckPrologUnwind(found, funcletBase))
            {
                FreeLibrary(library);
                return 1;
            }
            expectedCleanup = (DWORD64)(uintptr_t)code;
            funclets++;
        }
    }
    if (funclets != 1)
    {
        fprintf(stderr, "Expected one generated parent-frame cleanup funclet, found %d.\n", funclets);
        FreeLibrary(library);
        return 1;
    }
    __try
    {
        write(NULL);
    }
    __except(InspectFault(GetExceptionInformation()))
    {
    }
    if (checkedUnwind != 1)
    {
        fprintf(stderr, "Foreign fault did not unwind the saved frame.\n");
        FreeLibrary(library);
        return 1;
    }
    RtlCaptureContext(&caller);
    expectedCallerRbp = caller.Rbp;
    __try
    {
        // Test-only foreign caller: an invalid parent forces a fault inside the generated funclet.
        ((Cleanup)(uintptr_t)expectedCleanup)(0, (void*)(uintptr_t)1);
    }
    __except(InspectCleanupFault(GetExceptionInformation()))
    {
    }
    if (checkedCleanupUnwind != 1)
    {
        fprintf(stderr, "Cleanup fault did not distinguish its own stack from the parent frame.\n");
        FreeLibrary(library);
        return 1;
    }
    printf("native-frame exports=42 unwound-rbp=1 funclets=1 unwound-funclet=1 prolog-states=8\n");
    FreeLibrary(library);
    return 0;
}
