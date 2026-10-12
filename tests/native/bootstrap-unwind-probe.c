// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef PEXCEPTION_ROUTINE (NTAPI *RtlVirtualUnwindFunction)(
    DWORD, DWORD64, DWORD64, PRUNTIME_FUNCTION, PCONTEXT, PVOID*, PDWORD64,
    PKNONVOLATILE_CONTEXT_POINTERS);

typedef struct EntryExecution
{
    LPTHREAD_START_ROUTINE EntryPoint;
    ULONG_PTR InitialGuardPage;
    ULONG_PTR FinalGuardPage;
} EntryExecution;

static ULONG_PTR GetCurrentStackGuardPage(void)
{
    BYTE stackAnchor = 0;
    MEMORY_BASIC_INFORMATION region;
    ULONG_PTR allocationBase;
    ULONG_PTR queryAddress = (ULONG_PTR)&stackAnchor;
    DWORD searchDepth;

    if (VirtualQuery((LPCVOID)queryAddress, &region, sizeof(region)) != sizeof(region)
        || region.State != MEM_COMMIT || region.AllocationBase == NULL)
    {
        fprintf(stderr, "Cannot query the current committed stack region.\n");
        ExitProcess(97);
    }
    allocationBase = (ULONG_PTR)region.AllocationBase;
    for (searchDepth = 0; searchDepth < 1024; searchDepth++)
    {
        ULONG_PTR regionBase;
        if (VirtualQuery((LPCVOID)queryAddress, &region, sizeof(region))
            != sizeof(region) || (ULONG_PTR)region.AllocationBase != allocationBase)
        {
            fprintf(stderr, "Cannot query the current thread's stack allocation.\n");
            ExitProcess(97);
        }
        regionBase = (ULONG_PTR)region.BaseAddress;
        if (region.State == MEM_COMMIT && (region.Protect & PAGE_GUARD) != 0)
        {
            return regionBase;
        }
        if (region.State != MEM_COMMIT || regionBase <= allocationBase)
        {
            fprintf(stderr, "The current thread stack has no committed guard page.\n");
            ExitProcess(97);
        }
        queryAddress = regionBase - 1;
    }
    fprintf(stderr, "Cannot locate the current thread's stack guard page.\n");
    return 0;
}

static DWORD WINAPI ExecuteEntryPoint(void* parameter)
{
    EntryExecution* execution = (EntryExecution*)parameter;
    DWORD result;

    execution->InitialGuardPage = GetCurrentStackGuardPage();
    if (execution->InitialGuardPage == 0)
    {
        ExitProcess(97);
    }
    result = execution->EntryPoint(NULL);
    execution->FinalGuardPage = GetCurrentStackGuardPage();
    if (execution->FinalGuardPage == 0)
    {
        ExitProcess(97);
    }
    return result;
}

static DWORD GetCallOffset(PRUNTIME_FUNCTION function, DWORD epilogueLength)
{
    return function->EndAddress - function->BeginAddress - 5 - epilogueLength;
}

static int CheckUnwindState(
    RtlVirtualUnwindFunction unwind, BYTE* image, PRUNTIME_FUNCTION function,
    DWORD offset, DWORD allocation)
{
    const SIZE_T allocationUnits = allocation / sizeof(DWORD64);
    const SIZE_T stackCellCount = allocationUnits + 4;
    DWORD64* stack;
    DWORD64* entry;
    const DWORD64 returnAddress = 0x123456789ABCDEF0;
    CONTEXT context = { 0 };
    DWORD64 establisherFrame = 0;
    PVOID handlerData = NULL;
    DWORD64 initialRsp;

    if (allocation % sizeof(DWORD64) != 0
        || stackCellCount > SIZE_MAX / sizeof(DWORD64))
    {
        fprintf(stderr, "Synthetic unwind stack layout is invalid.\n");
        return 0;
    }
    stack = (DWORD64*)calloc(stackCellCount, sizeof(DWORD64));
    if (stack == NULL)
    {
        fprintf(stderr, "Cannot allocate synthetic unwind stack.\n");
        return 0;
    }
    entry = stack + allocationUnits + 2;
    entry[0] = returnAddress;
    context.ContextFlags = CONTEXT_CONTROL;
    context.Rip = (DWORD64)(uintptr_t)image + function->BeginAddress + offset;
    context.Rsp = (DWORD64)(uintptr_t)entry - allocation;
    initialRsp = context.Rsp;
    unwind(UNW_FLAG_NHANDLER, (DWORD64)(uintptr_t)image, context.Rip, function,
        &context, &handlerData, &establisherFrame, NULL);
    if (context.Rip != returnAddress
        || context.Rsp != (DWORD64)(uintptr_t)(entry + 1)
        || establisherFrame != initialRsp
        || handlerData != NULL)
    {
        fprintf(stderr, "Incorrect unwind at generated code offset %lu.\n", offset);
        free(stack);
        return 0;
    }
    free(stack);
    return 1;
}

static int CheckNestedCallChainUnwind(
    RtlVirtualUnwindFunction unwind, BYTE* image,
    PRUNTIME_FUNCTION functions, DWORD forwardingIndex, DWORD offset,
    DWORD allocation, DWORD prologSize, DWORD epilogueLength)
{
    const SIZE_T allocationUnits = allocation / sizeof(DWORD64);
    SIZE_T stackCellCount;
    DWORD64* stack;
    DWORD64* parentRsp;
    DWORD64* forwardingReturn;
    DWORD64* forwardingRsp;
    PRUNTIME_FUNCTION forwardingFunction = &functions[forwardingIndex];
    PRUNTIME_FUNCTION parentFunction = &functions[forwardingIndex - 1];
    const DWORD parentCallOffset = GetCallOffset(parentFunction, epilogueLength);
    const DWORD64 returnToParent = (DWORD64)(uintptr_t)image
        + parentFunction->BeginAddress + parentCallOffset + 5;
    const DWORD64 returnFromParent = forwardingIndex == 1
        ? 0x123456789ABCDEF0
        : (DWORD64)(uintptr_t)image + functions[forwardingIndex - 2].BeginAddress
        + GetCallOffset(&functions[forwardingIndex - 2], epilogueLength) + 5;
    CONTEXT context = { 0 };
    DWORD64 establisherFrame = 0;
    PVOID handlerData = NULL;

    if (allocation % sizeof(DWORD64) != 0
        || allocationUnits > (SIZE_MAX - 4) / 2)
    {
        fprintf(stderr, "Synthetic nested-unwind stack layout is invalid.\n");
        return 0;
    }
    stackCellCount = allocationUnits * 2 + 4;
    if (stackCellCount > SIZE_MAX / sizeof(DWORD64))
    {
        fprintf(stderr, "Synthetic nested-unwind stack layout is too large.\n");
        return 0;
    }
    stack = (DWORD64*)calloc(stackCellCount, sizeof(DWORD64));
    if (stack == NULL)
    {
        fprintf(stderr, "Cannot allocate synthetic nested-unwind stack.\n");
        return 0;
    }
    parentRsp = stack + allocationUnits + 2;
    forwardingReturn = parentRsp - 1;
    forwardingRsp = forwardingReturn
        - (offset >= prologSize ? allocationUnits : 0);
    parentRsp[allocationUnits] = returnFromParent;
    forwardingReturn[0] = returnToParent;
    context.ContextFlags = CONTEXT_CONTROL;
    context.Rip = (DWORD64)(uintptr_t)image
        + forwardingFunction->BeginAddress + offset;
    context.Rsp = (DWORD64)(uintptr_t)forwardingRsp;
    unwind(UNW_FLAG_NHANDLER, (DWORD64)(uintptr_t)image, context.Rip,
        forwardingFunction, &context, &handlerData, &establisherFrame, NULL);
    if (context.Rip != returnToParent
        || context.Rsp != (DWORD64)(uintptr_t)parentRsp
        || establisherFrame != (DWORD64)(uintptr_t)forwardingRsp
        || handlerData != NULL)
    {
        fprintf(stderr,
            "Incorrect forwarding-frame unwind at generated code offset %lu.\n",
            offset);
        free(stack);
        return 0;
    }

    establisherFrame = 0;
    handlerData = NULL;
    unwind(UNW_FLAG_NHANDLER, (DWORD64)(uintptr_t)image, context.Rip,
        parentFunction, &context, &handlerData, &establisherFrame, NULL);
    if (context.Rip != returnFromParent
        || context.Rsp != (DWORD64)(uintptr_t)(parentRsp + allocationUnits + 1)
        || establisherFrame != (DWORD64)(uintptr_t)parentRsp
        || handlerData != NULL)
    {
        fprintf(stderr,
            "Incorrect parent-frame unwind after forwarding frame "
            "(RIP=%llX, RSP=%llX, establisher=%llX; expected RIP=%llX, "
            "RSP=%llX, establisher=%llX).\n",
            (unsigned long long)context.Rip,
            (unsigned long long)context.Rsp,
            (unsigned long long)establisherFrame,
            (unsigned long long)returnFromParent,
            (unsigned long long)(uintptr_t)(parentRsp + allocationUnits + 1),
            (unsigned long long)(uintptr_t)parentRsp);
        free(stack);
        return 0;
    }
    free(stack);
    return 1;
}

static int CheckLeafThroughCallChainUnwind(
    RtlVirtualUnwindFunction unwind, BYTE* image,
    PRUNTIME_FUNCTION functions, DWORD functionCount,
    DWORD allocation, DWORD epilogueLength)
{
    const SIZE_T allocationUnits = allocation / sizeof(DWORD64);
    const SIZE_T frameStride = allocationUnits + 1;
    const SIZE_T frameCount = functionCount;
    SIZE_T stackCellCount;
    DWORD64* stack;
    DWORD64* mainRsp;
    DWORD64* leafReturn;
    CONTEXT context = { 0 };
    DWORD index;

    if (allocation % sizeof(DWORD64) != 0
        || frameCount == SIZE_MAX
        || frameCount + 1 > SIZE_MAX / frameStride)
    {
        fprintf(stderr, "Synthetic leaf-chain stack layout is invalid.\n");
        return 0;
    }
    stackCellCount = (frameCount + 1) * frameStride;
    if (stackCellCount > SIZE_MAX / sizeof(DWORD64))
    {
        fprintf(stderr, "Synthetic leaf-chain stack layout is too large.\n");
        return 0;
    }
    stack = (DWORD64*)calloc(stackCellCount, sizeof(DWORD64));
    if (stack == NULL)
    {
        fprintf(stderr, "Cannot allocate synthetic leaf-chain stack.\n");
        return 0;
    }
    mainRsp = stack + frameCount * frameStride;
    mainRsp[allocationUnits] = 0x123456789ABCDEF0;
    for (index = 1; index < functionCount; index++)
    {
        DWORD64* frameRsp = mainRsp - (SIZE_T)index * frameStride;
        PRUNTIME_FUNCTION parentFunction = &functions[index - 1];
        const DWORD parentCallOffset = GetCallOffset(parentFunction, epilogueLength);
        frameRsp[allocationUnits] = (DWORD64)(uintptr_t)image
            + parentFunction->BeginAddress + parentCallOffset + 5;
    }

    leafReturn = mainRsp - (SIZE_T)(functionCount - 1) * frameStride - 1;
    leafReturn[0] = (DWORD64)(uintptr_t)image
        + functions[functionCount - 1].BeginAddress
        + GetCallOffset(
            &functions[functionCount - 1], epilogueLength) + 5;
    context.ContextFlags = CONTEXT_CONTROL;
    context.Rsp = (DWORD64)(uintptr_t)leafReturn;
    context.Rip = *(DWORD64*)(uintptr_t)context.Rsp;
    context.Rsp += sizeof(DWORD64);
    if (context.Rip != (DWORD64)(uintptr_t)image
        + functions[functionCount - 1].BeginAddress
        + GetCallOffset(&functions[functionCount - 1], epilogueLength) + 5
        || context.Rsp != (DWORD64)(uintptr_t)
            (mainRsp - (SIZE_T)(functionCount - 1) * frameStride))
    {
        fprintf(stderr, "Incorrect leaf-frame unwind before forwarding chain.\n");
        free(stack);
        return 0;
    }

    for (index = functionCount - 1; index > 0; index--)
    {
        DWORD64* frameRsp = mainRsp - (SIZE_T)index * frameStride;
        DWORD64* parentRsp = mainRsp - (SIZE_T)(index - 1) * frameStride;
        PRUNTIME_FUNCTION function = &functions[index];
        const DWORD callOffset = GetCallOffset(function, epilogueLength);
        const DWORD64 expectedCurrentRip = (DWORD64)(uintptr_t)image
            + function->BeginAddress + callOffset + 5;
        const DWORD64 expectedCallerRip = frameRsp[allocationUnits];
        DWORD64 establisherFrame = 0;
        PVOID handlerData = NULL;

        if (context.Rip != expectedCurrentRip
            || context.Rsp != (DWORD64)(uintptr_t)frameRsp)
        {
            fprintf(stderr, "Incorrect frame order in synthetic leaf chain.\n");
            free(stack);
            return 0;
        }
        unwind(UNW_FLAG_NHANDLER, (DWORD64)(uintptr_t)image, context.Rip,
            function, &context, &handlerData, &establisherFrame, NULL);
        if (context.Rip != expectedCallerRip
            || context.Rsp != (DWORD64)(uintptr_t)parentRsp
            || establisherFrame != (DWORD64)(uintptr_t)frameRsp
            || handlerData != NULL)
        {
            fprintf(stderr, "Incorrect forwarding-frame unwind in leaf chain.\n");
            free(stack);
            return 0;
        }
    }

    {
        DWORD64 establisherFrame = 0;
        PVOID handlerData = NULL;
        const DWORD64 expectedFinalRsp = (DWORD64)(uintptr_t)mainRsp
            + allocation + sizeof(DWORD64);
        unwind(UNW_FLAG_NHANDLER, (DWORD64)(uintptr_t)image, context.Rip,
            &functions[0], &context, &handlerData, &establisherFrame, NULL);
        if (context.Rip != 0x123456789ABCDEF0
            || context.Rsp != expectedFinalRsp
            || establisherFrame != (DWORD64)(uintptr_t)mainRsp
            || handlerData != NULL)
        {
            fprintf(stderr, "Incorrect entry-frame unwind after leaf call chain.\n");
            free(stack);
            return 0;
        }
    }
    free(stack);
    return 1;
}

static int CheckLargeFrameProbe(const BYTE* code, DWORD functionSize, DWORD allocation)
{
    static const BYTE expected[] = {
        0x49, 0x89, 0xE3, 0x41, 0xBA, 0, 0, 0, 0,
        0x49, 0x81, 0xEB, 0x00, 0x10, 0x00, 0x00,
        0x41, 0xF6, 0x03, 0x00,
        0x41, 0x81, 0xEA, 0x00, 0x10, 0x00, 0x00,
        0x41, 0x81, 0xFA, 0x00, 0x10, 0x00, 0x00,
        0x73, 0xE5,
        0x4D, 0x29, 0xD3,
        0x41, 0xF6, 0x03, 0x00,
        0x48, 0x81, 0xEC, 0, 0, 0, 0,
    };
    DWORD firstAllocation;
    DWORD secondAllocation;

    if (functionSize < sizeof(expected))
    {
        fprintf(stderr, "Generated large-frame probe prolog is truncated.\n");
        return 0;
    }
    memcpy(&firstAllocation, code + 5, sizeof(firstAllocation));
    memcpy(&secondAllocation, code + 46, sizeof(secondAllocation));
    if (memcmp(code, expected, 5) != 0
        || memcmp(code + 9, expected + 9, 37) != 0
        || firstAllocation != allocation
        || secondAllocation != allocation)
    {
        fprintf(stderr, "Generated large-frame probe sequence differs.\n");
        return 0;
    }
    return 1;
}

static int CheckStackFrameProlog(
    const BYTE* code, DWORD functionSize, DWORD allocation)
{
    if (allocation >= 0x1000)
    {
        return CheckLargeFrameProbe(code, functionSize, allocation);
    }
    if (allocation > 0x7F)
    {
        BYTE expected[] = { 0x48, 0x81, 0xEC, 0, 0, 0, 0 };
        if (functionSize < sizeof(expected))
        {
            fprintf(stderr, "Generated stack-frame prolog is truncated.\n");
            return 0;
        }
        memcpy(expected + 3, &allocation, sizeof(allocation));
        if (memcmp(code, expected, sizeof(expected)) != 0)
        {
            fprintf(stderr, "Generated stack-frame prolog differs.\n");
            return 0;
        }
        return 1;
    }
    else
    {
        const BYTE expected[] = { 0x48, 0x83, 0xEC, (BYTE)allocation };
        if (functionSize < sizeof(expected)
            || memcmp(code, expected, sizeof(expected)) != 0)
        {
            fprintf(stderr, "Generated stack-frame prolog differs.\n");
            return 0;
        }
    }
    return 1;
}

static DWORD GetStackFrameSize(DWORD argumentCount)
{
    const DWORD frameBase = 0x20 + (argumentCount > 4
        ? (argumentCount - 4) * 8 : 0);
    return ((frameBase + 8 + 15) & ~15u) - 8;
}

static DWORD GetCallerFunctionSize(DWORD argumentCount)
{
    const DWORD frameSize = GetStackFrameSize(argumentCount);
    DWORD callerEnd = frameSize >= 0x1000 ? 0x32
        : frameSize > 0x7F ? 7 : 4;
    DWORD argumentIndex;
    for (argumentIndex = 0; argumentIndex < argumentCount; argumentIndex++)
    {
        if (argumentIndex < 2)
        {
            callerEnd += 5;
        }
        else if (argumentIndex < 4)
        {
            callerEnd += 6;
        }
        else
        {
            const DWORD callerStackOffset = 0x20 + (argumentIndex - 4) * 8;
            callerEnd += callerStackOffset > 0x7F ? 11 : 8;
        }
    }
    return callerEnd + 5 + (frameSize > 0x7F ? 8 : 5);
}

static int CheckCallAndEpilogue(
    BYTE* image, PRUNTIME_FUNCTION function, DWORD callOffset,
    DWORD epilogueLength, DWORD allocation, DWORD expectedTarget)
{
    const DWORD functionSize = function->EndAddress - function->BeginAddress;
    const BYTE* code = image + function->BeginAddress;
    const BYTE* epilogue = code + callOffset + 5;
    LONG relativeTarget;
    LONGLONG targetRva;

    if ((ULONGLONG)callOffset + 5 + epilogueLength != functionSize
        || code[callOffset] != 0xE8)
    {
        fprintf(stderr, "Generated callsite does not precede the expected epilogue.\n");
        return 0;
    }
    memcpy(&relativeTarget, code + callOffset + 1, sizeof(relativeTarget));
    targetRva = (LONGLONG)function->BeginAddress + callOffset + 5
        + relativeTarget;
    if (relativeTarget != (LONG)epilogueLength
        || targetRva != expectedTarget)
    {
        fprintf(stderr, "Generated call does not target the adjacent function.\n");
        return 0;
    }
    if (allocation <= 0x7F)
    {
        const BYTE expected[] = {
            0x48, 0x83, 0xC4, (BYTE)allocation, 0xC3,
        };
        if (epilogueLength != sizeof(expected)
            || memcmp(epilogue, expected, sizeof(expected)) != 0)
        {
            fprintf(stderr, "Generated frame epilogue differs.\n");
            return 0;
        }
    }
    else
    {
        BYTE expected[] = {
            0x48, 0x81, 0xC4, 0, 0, 0, 0, 0xC3,
        };
        if (epilogueLength != sizeof(expected))
        {
            fprintf(stderr, "Generated frame epilogue length differs.\n");
            return 0;
        }
        memcpy(expected + 3, &allocation, sizeof(allocation));
        if (memcmp(epilogue, expected, sizeof(expected)) != 0)
        {
            fprintf(stderr, "Generated frame epilogue differs.\n");
            return 0;
        }
    }
    return 1;
}

int wmain(int argc, wchar_t** argv)
{
    FILE* file = NULL;
    BYTE* fileBytes = NULL;
    BYTE* image = NULL;
    ULONGLONG fileSize;
    IMAGE_DOS_HEADER* dos;
    IMAGE_NT_HEADERS64* nt;
    IMAGE_SECTION_HEADER* sections;
    IMAGE_SECTION_HEADER* textSection = NULL;
    IMAGE_DATA_DIRECTORY exceptionDirectory;
    PRUNTIME_FUNCTION functions;
    PRUNTIME_FUNCTION mainFunction = NULL;
    RtlVirtualUnwindFunction unwind;
    HMODULE ntdll;
    HANDLE thread = NULL;
    DWORD index;
    DWORD allocation;
    DWORD callOffset;
    DWORD functionCount;
    DWORD prologSize;
    DWORD epilogueLength;
    DWORD functionSize;
    DWORD argumentCount = 0;
    int hasArgumentCount = 0;
    DWORD threadExitCode;
    DWORD waitResult;
    DWORD oldProtection;
    SIZE_T textSize;
    DWORD unwindStateCount = 4;
    EntryExecution execution = { 0 };
    int threadMayBeRunning = 0;
    int result = 1;

    if (argc != 2 || _wfopen_s(&file, argv[1], L"rb") != 0 || file == NULL
        || _fseeki64(file, 0, SEEK_END) != 0)
    {
        fprintf(stderr, "Cannot open generated PE image.\n");
        goto cleanup;
    }
    {
        const __int64 length = _ftelli64(file);
        if (length < 0 || _fseeki64(file, 0, SEEK_SET) != 0)
        {
            fprintf(stderr, "Cannot measure generated PE image.\n");
            goto cleanup;
        }
        fileSize = (ULONGLONG)length;
    }
    if (fileSize < sizeof(IMAGE_DOS_HEADER) || fileSize > SIZE_MAX)
    {
        fprintf(stderr, "Generated PE image size is invalid.\n");
        goto cleanup;
    }
    fileBytes = (BYTE*)malloc((size_t)fileSize);
    if (fileBytes == NULL || fread(fileBytes, 1, (size_t)fileSize, file) != (size_t)fileSize)
    {
        fprintf(stderr, "Cannot read generated PE image.\n");
        goto cleanup;
    }
    dos = (IMAGE_DOS_HEADER*)fileBytes;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0
        || (ULONGLONG)(DWORD)dos->e_lfanew + sizeof(IMAGE_NT_HEADERS64) > fileSize)
    {
        fprintf(stderr, "Generated PE headers are invalid.\n");
        goto cleanup;
    }
    nt = (IMAGE_NT_HEADERS64*)(fileBytes + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE
        || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64
        || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC
        || nt->OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_EXCEPTION
        || nt->OptionalHeader.SizeOfHeaders > fileSize
        || nt->OptionalHeader.SizeOfImage == 0)
    {
        fprintf(stderr, "Generated PE is not a mapped Windows x64 image.\n");
        goto cleanup;
    }
    image = (BYTE*)VirtualAlloc(NULL, nt->OptionalHeader.SizeOfImage,
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (image == NULL)
    {
        fprintf(stderr, "Cannot allocate mapped image: %lu\n", GetLastError());
        goto cleanup;
    }
    memcpy(image, fileBytes, nt->OptionalHeader.SizeOfHeaders);
    sections = IMAGE_FIRST_SECTION(nt);
    for (index = 0; index < nt->FileHeader.NumberOfSections; index++)
    {
        const IMAGE_SECTION_HEADER* section = &sections[index];
        if ((ULONGLONG)section->PointerToRawData + section->SizeOfRawData > fileSize
            || (ULONGLONG)section->VirtualAddress + section->SizeOfRawData
                > nt->OptionalHeader.SizeOfImage)
        {
            fprintf(stderr, "Generated PE section bounds are invalid.\n");
            goto cleanup;
        }
        if (section->SizeOfRawData != 0)
        {
            memcpy(image + section->VirtualAddress,
                fileBytes + section->PointerToRawData, section->SizeOfRawData);
        }
        if (strncmp((const char*)section->Name, ".text", IMAGE_SIZEOF_SHORT_NAME) == 0)
        {
            if (textSection != NULL)
            {
                fprintf(stderr, "Generated PE contains multiple .text sections.\n");
                goto cleanup;
            }
            textSection = (IMAGE_SECTION_HEADER*)section;
        }
    }
    if (textSection == NULL)
    {
        fprintf(stderr, "Generated PE .text section is unavailable.\n");
        goto cleanup;
    }
    textSize = textSection->Misc.VirtualSize > textSection->SizeOfRawData
        ? textSection->Misc.VirtualSize : textSection->SizeOfRawData;
    if (textSize == 0
        || (ULONGLONG)textSection->VirtualAddress + textSize > nt->OptionalHeader.SizeOfImage
        || nt->OptionalHeader.AddressOfEntryPoint < textSection->VirtualAddress
        || (ULONGLONG)nt->OptionalHeader.AddressOfEntryPoint
            >= (ULONGLONG)textSection->VirtualAddress + textSize)
    {
        fprintf(stderr, "Generated PE entrypoint is outside the .text section.\n");
        goto cleanup;
    }
    exceptionDirectory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    if (exceptionDirectory.Size < sizeof(RUNTIME_FUNCTION)
        || exceptionDirectory.Size % sizeof(RUNTIME_FUNCTION) != 0
        || exceptionDirectory.VirtualAddress % sizeof(DWORD) != 0
        || (ULONGLONG)exceptionDirectory.VirtualAddress + exceptionDirectory.Size
            > nt->OptionalHeader.SizeOfImage)
    {
        fprintf(stderr, "Generated PE exception directory is invalid.\n");
        goto cleanup;
    }
    functionCount = (DWORD)(exceptionDirectory.Size / sizeof(RUNTIME_FUNCTION));
    functions = (PRUNTIME_FUNCTION)(image + exceptionDirectory.VirtualAddress);
    if (functions[0].BeginAddress != nt->OptionalHeader.AddressOfEntryPoint)
    {
        fprintf(stderr, "Generated exception directory does not begin with Main.\n");
        goto cleanup;
    }
    mainFunction = &functions[0];
    for (index = 0; index < functionCount; index++)
    {
        PRUNTIME_FUNCTION function = &functions[index];
        if (function->BeginAddress < textSection->VirtualAddress
            || function->EndAddress <= function->BeginAddress
            || (ULONGLONG)function->EndAddress
                > (ULONGLONG)textSection->VirtualAddress + textSize
            || (ULONGLONG)function->UnwindData + 8
                > nt->OptionalHeader.SizeOfImage
            || (index != 0
                && function->BeginAddress != functions[index - 1].EndAddress))
        {
            fprintf(stderr, "Generated nonleaf function table layout is invalid.\n");
            goto cleanup;
        }
    }
    functionSize = mainFunction->EndAddress - mainFunction->BeginAddress;
    {
        const BYTE* info = image + mainFunction->UnwindData;
        const DWORD largeAllocationUnits = (DWORD)info[6] | ((DWORD)info[7] << 8);
        if (info[0] != 1)
        {
            fprintf(stderr, "Generated entrypoint has an unsupported unwind version.\n");
            goto cleanup;
        }
        if (info[1] == 4 && info[2] == 1
            && info[4] == 4 && (info[5] & 0x0F) == 2
            && ((info[5] >> 4) + 1) >= 5
            && ((info[5] >> 4) + 1) <= 15
            && ((info[5] >> 4) + 1) % 2 == 1)
        {
            prologSize = 4;
            allocation = ((DWORD)(info[5] >> 4) + 1) * 8;
        }
        else if (info[1] == 7 && info[2] == 2
            && info[4] == 7 && info[5] == 1
            && (info[6] | (info[7] << 8)) >= 17
            && (info[6] | (info[7] << 8)) <= 511
            && (info[6] | (info[7] << 8)) % 2 == 1)
        {
            prologSize = 7;
            allocation = ((DWORD)(info[6] | (info[7] << 8))) * 8;
        }
        else if (info[1] == 0x32 && info[2] == 2
            && info[4] == 0x32 && info[5] == 1
            && largeAllocationUnits >= 0x200
            && largeAllocationUnits % 2 == 1)
        {
            prologSize = 0x32;
            allocation = largeAllocationUnits * 8;
        }
        else
        {
            fprintf(stderr, "Generated entrypoint allocation unwind encoding differs.\n");
            goto cleanup;
        }
    }
    if (!CheckStackFrameProlog(
        image + mainFunction->BeginAddress, functionSize, allocation))
    {
        goto cleanup;
    }
    for (index = 0; index <= 0xFFFF; index++)
    {
        if (functionSize == GetCallerFunctionSize(index))
        {
            argumentCount = index;
            hasArgumentCount = 1;
            break;
        }
    }
    if (!hasArgumentCount || allocation != GetStackFrameSize(argumentCount))
    {
        fprintf(stderr, "Generated entrypoint caller range or allocation is unsupported.\n");
        goto cleanup;
    }
    epilogueLength = allocation > 0x7F ? 8 : 5;
    if (functionSize <= 5 + epilogueLength)
    {
        fprintf(stderr, "Generated entrypoint is too short for the frame probe.\n");
        goto cleanup;
    }
    callOffset = functionSize - 5 - epilogueLength;
    if (callOffset < prologSize)
    {
        fprintf(stderr, "Generated entrypoint call precedes the end of its prolog.\n");
        goto cleanup;
    }
    for (index = 0; index < functionCount; index++)
    {
        PRUNTIME_FUNCTION function = &functions[index];
        const DWORD currentSize = function->EndAddress - function->BeginAddress;
        const DWORD currentCallOffset = GetCallOffset(function, epilogueLength);
        const DWORD expectedTarget = index + 1 < functionCount
            ? functions[index + 1].BeginAddress : function->EndAddress;
        if ((index != 0
                && (currentSize < prologSize + 5 + epilogueLength
                    || memcmp(image + mainFunction->UnwindData,
                        image + function->UnwindData, 8) != 0
                    || !CheckStackFrameProlog(
                        image + function->BeginAddress, currentSize, allocation)))
            || expectedTarget < textSection->VirtualAddress
            || (ULONGLONG)expectedTarget
                >= (ULONGLONG)textSection->VirtualAddress + textSize
            || !CheckCallAndEpilogue(image, function, currentCallOffset,
                epilogueLength, allocation, expectedTarget))
        {
            if (index != 0)
            {
                fprintf(stderr, "Generated forwarding frame shape is unsupported.\n");
            }
            goto cleanup;
        }
    }
    ntdll = GetModuleHandleW(L"ntdll.dll");
    unwind = ntdll == NULL ? NULL
        : (RtlVirtualUnwindFunction)(uintptr_t)GetProcAddress(ntdll, "RtlVirtualUnwind");
    if (unwind == NULL)
    {
        fprintf(stderr, "RtlVirtualUnwind is unavailable.\n");
        goto cleanup;
    }
    if (prologSize == 0x32)
    {
        static const DWORD probeOffsets[] = {
            0x03, 0x09, 0x10, 0x14, 0x1B, 0x22, 0x24, 0x27, 0x2B,
        };
        unwindStateCount += (DWORD)(sizeof(probeOffsets) / sizeof(probeOffsets[0]));
        for (index = 0; index < sizeof(probeOffsets) / sizeof(probeOffsets[0]); index++)
        {
            if (!CheckUnwindState(
                unwind, image, mainFunction, probeOffsets[index], 0))
            {
                goto cleanup;
            }
        }
    }
    if (!CheckUnwindState(unwind, image, mainFunction, 0, 0)
        || !CheckUnwindState(unwind, image, mainFunction, prologSize, allocation)
        || !CheckUnwindState(unwind, image, mainFunction, prologSize + 5, allocation)
        || !CheckUnwindState(unwind, image, mainFunction, callOffset, allocation))
    {
        goto cleanup;
    }
    if (functionCount > 1)
    {
        DWORD nestedStateCount = 0;
        for (index = 1; index < functionCount; index++)
        {
            PRUNTIME_FUNCTION forwardingFunction = &functions[index];
            const DWORD forwardingSize = forwardingFunction->EndAddress
                - forwardingFunction->BeginAddress;
            const DWORD forwardingCallOffset = GetCallOffset(
                forwardingFunction, epilogueLength);
            const DWORD forwardingOffsets[] = {
                0, prologSize, prologSize + 5, forwardingCallOffset,
            };
            DWORD offsetIndex;
            for (offsetIndex = 0;
                offsetIndex < sizeof(forwardingOffsets) / sizeof(DWORD);
                offsetIndex++)
            {
                if (!CheckNestedCallChainUnwind(
                    unwind, image, functions, index, forwardingOffsets[offsetIndex],
                    allocation, prologSize, epilogueLength))
                {
                    goto cleanup;
                }
                nestedStateCount++;
            }
            if (forwardingSize < prologSize + 5 + epilogueLength
                || !CheckUnwindState(unwind, image, forwardingFunction, 0, 0)
                || !CheckUnwindState(
                    unwind, image, forwardingFunction, prologSize, allocation)
                || !CheckUnwindState(
                    unwind, image, forwardingFunction, prologSize + 5, allocation)
                || !CheckUnwindState(
                    unwind, image, forwardingFunction, forwardingCallOffset, allocation))
            {
                goto cleanup;
            }
        }
        if (!CheckLeafThroughCallChainUnwind(
            unwind, image, functions, functionCount, allocation, epilogueLength))
        {
            goto cleanup;
        }
        printf("nested-unwind-states=%lu allocation=%lu\n",
            nestedStateCount, allocation);
        printf("leaf-chain-unwind-states=%lu allocation=%lu\n",
            functionCount + 1, allocation);
    }
    printf("unwind-states=%lu allocation=%lu\n", unwindStateCount, allocation);
    if (!VirtualProtect(image + textSection->VirtualAddress, textSize,
        PAGE_EXECUTE_READ, &oldProtection))
    {
        fprintf(stderr, "Cannot protect generated .text section: %lu\n", GetLastError());
        goto cleanup;
    }
    if (!FlushInstructionCache(GetCurrentProcess(),
        image + textSection->VirtualAddress, textSize))
    {
        fprintf(stderr, "Cannot flush generated .text instruction cache: %lu\n",
            GetLastError());
        goto cleanup;
    }
    execution.EntryPoint = (LPTHREAD_START_ROUTINE)(uintptr_t)
        (image + nt->OptionalHeader.AddressOfEntryPoint);
    thread = CreateThread(
        NULL, 4096, ExecuteEntryPoint, &execution, 0, NULL);
    if (thread == NULL)
    {
        fprintf(stderr, "Cannot create generated entrypoint thread: %lu\n", GetLastError());
        goto cleanup;
    }
    threadMayBeRunning = 1;
    waitResult = WaitForSingleObject(thread, 10000);
    if (waitResult != WAIT_OBJECT_0)
    {
        if (waitResult == WAIT_TIMEOUT)
        {
            fprintf(stderr, "Generated entrypoint thread timed out after 10000 ms.\n");
        }
        else
        {
            fprintf(stderr, "Cannot wait for generated entrypoint thread: %lu\n",
                GetLastError());
        }
        goto cleanup;
    }
    threadMayBeRunning = 0;
    if (!GetExitCodeThread(thread, &threadExitCode))
    {
        fprintf(stderr, "Cannot retrieve generated entrypoint thread exit code: %lu\n",
            GetLastError());
        goto cleanup;
    }
    if (!CloseHandle(thread))
    {
        fprintf(stderr, "Cannot close generated entrypoint thread handle: %lu\n",
            GetLastError());
        thread = NULL;
        goto cleanup;
    }
    thread = NULL;
    // The test thread's native trampoline may consume part of its initial commit.
    if ((ULONGLONG)allocation * functionCount >= 0x2000)
    {
        if (execution.FinalGuardPage >= execution.InitialGuardPage)
        {
            fprintf(stderr, "Generated entrypoint did not move the stack guard.\n");
            goto cleanup;
        }
        printf("probe-stack-growth-bytes=%llu\n",
            (unsigned long long)(execution.InitialGuardPage - execution.FinalGuardPage));
    }
    printf("probe-execution-result=%lu\n", threadExitCode);
    result = 0;

cleanup:
    if (thread != NULL && !CloseHandle(thread))
    {
        fprintf(stderr, "Cannot close generated entrypoint thread handle: %lu\n",
            GetLastError());
        result = 1;
    }
    if (image != NULL && !threadMayBeRunning)
    {
        VirtualFree(image, 0, MEM_RELEASE);
    }
    free(fileBytes);
    if (file != NULL)
    {
        fclose(file);
    }
    return result;
}
