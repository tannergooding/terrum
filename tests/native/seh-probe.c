// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define OWN_EXCEPTION ((DWORD)0xE0540091)
#define FOREIGN_EXCEPTION ((DWORD)0xE0550091)

static volatile LONG eventCount;
static volatile LONG events[16];
static volatile LONG frameErrors;
static volatile LONG abnormalFinally;

static void Record(LONG value)
{
    if (eventCount < 16)
    {
        events[eventCount++] = value;
    }
}

__declspec(noinline) static void RaiseFromC(int foreign)
{
    RaiseException(foreign ? FOREIGN_EXCEPTION : OWN_EXCEPTION, EXCEPTION_NONCONTINUABLE, 0, NULL);
}

static int CFilter(EXCEPTION_POINTERS* pointers)
{
    if ((pointers->ExceptionRecord->ExceptionFlags & EXCEPTION_NONCONTINUABLE) == 0)
    {
        ++frameErrors;
    }
    Record(1);
    return pointers->ExceptionRecord->ExceptionCode == OWN_EXCEPTION
        ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH;
}

__declspec(noinline) static int CReference(int mode)
{
    int result = 0;
    __try
    {
        __try
        {
            if (mode != 0)
            {
                RaiseFromC(mode == 2);
            }
            result = 10;
        }
        __finally
        {
            Record(2);
        }
    }
    __except(CFilter(GetExceptionInformation()))
    {
        Record(3);
        result = 20;
    }
    return result;
}

__declspec(noinline) static int DynamicBody(int mode)
{
    if (mode != 0)
    {
        RaiseFromC(mode == 2);
    }
    return 10;
}

static int DynamicFilter(EXCEPTION_POINTERS* pointers, void* frame)
{
    if (*(int*)((BYTE*)frame + 32) != 41)
    {
        ++frameErrors;
    }
    return CFilter(pointers);
}

static void DynamicFinally(BOOLEAN abnormal, void* frame)
{
    if (*(int*)((BYTE*)frame + 32) != 41)
    {
        ++frameErrors;
    }
    *(int*)((BYTE*)frame + 32) = 42;
    abnormalFinally = abnormal != FALSE;
    Record(2);
}

static void DynamicCatch(void* frame)
{
    if (*(int*)((BYTE*)frame + 32) != 42)
    {
        ++frameErrors;
    }
    *(int*)((BYTE*)frame + 40) = 20;
    Record(3);
}

typedef int (*Entry)(int);

static void Put32(BYTE* image, DWORD at, DWORD value)
{
    memcpy(image + at, &value, sizeof(value));
}

static DWORD Bytes(BYTE* image, DWORD* at, const BYTE* values, DWORD count)
{
    DWORD start = *at;
    memcpy(image + start, values, count);
    *at += count;
    return start;
}

#define EMIT(...) do { const BYTE bytes[] = { __VA_ARGS__ }; Bytes(image, &at, bytes, sizeof(bytes)); } while (0)

static void CallPointer(BYTE* image, DWORD* at, DWORD pointer)
{
    image[(*at)++] = 0xFF;
    image[(*at)++] = 0x15;
    Put32(image, *at, pointer - (*at + 4));
    *at += 4;
}

static DWORD Trampoline(BYTE* image, DWORD* at, DWORD pointer)
{
    DWORD start = *at;
    image[(*at)++] = 0xFF;
    image[(*at)++] = 0x25;
    Put32(image, *at, pointer - (*at + 4));
    *at += 4;
    return start;
}

static BYTE* BuildDynamic(FARPROC handler, PRUNTIME_FUNCTION* table)
{
    BYTE* image = (BYTE*)VirtualAlloc(NULL, 8192, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    DWORD oldProtection;
    DWORD at = 0;
    DWORD begin, end, jump, landing, epilog, functionEnd, languageHandler, filter, cleanup;
    ULONG64 pointers[] = {
        (ULONG64)(uintptr_t)DynamicBody,
        (ULONG64)(uintptr_t)DynamicFinally,
        (ULONG64)(uintptr_t)DynamicCatch,
        (ULONG64)(uintptr_t)handler,
        (ULONG64)(uintptr_t)DynamicFilter,
    };
    if (image == NULL)
    {
        return NULL;
    }
    EMIT(0x48, 0x81, 0xEC, 0x38, 0, 0, 0); // sub rsp, 56
    EMIT(0xC7, 0x44, 0x24, 0x20, 41, 0, 0, 0);
    EMIT(0x89, 0x4C, 0x24, 0x24); // save mode outside shadow space
    begin = at;
    EMIT(0x8B, 0x4C, 0x24, 0x24);
    CallPointer(image, &at, 4096);
    EMIT(0x89, 0x44, 0x24, 0x28); // result = DynamicBody(mode)
    end = at;
    EMIT(0x31, 0xC9, 0x48, 0x89, 0xE2); // normal finally: cl = false, rdx = establisher
    CallPointer(image, &at, 4104);
    EMIT(0xE9, 0, 0, 0, 0);
    jump = at - 4;
    landing = at;
    EMIT(0x48, 0x89, 0xE1); // catch body: rcx = establisher
    CallPointer(image, &at, 4112);
    epilog = at;
    EMIT(0x8B, 0x44, 0x24, 0x28, 0x48, 0x81, 0xC4, 0x38, 0, 0, 0, 0xC3);
    functionEnd = at;
    Put32(image, jump, epilog - (jump + 4));
    languageHandler = Trampoline(image, &at, 4120);
    filter = Trampoline(image, &at, 4128);
    cleanup = Trampoline(image, &at, 4104);
    memcpy(image + 4096, pointers, sizeof(pointers));

    // UNW_FLAG_EHANDLER | UNW_FLAG_UHANDLER, shared seven-byte allocation prolog.
    image[4160] = 0x19;
    image[4161] = 7;
    image[4162] = 2;
    image[4164] = 7;
    image[4165] = 1;
    image[4166] = 7; // 56 / 8, UWOP_ALLOC_LARGE extra slot
    Put32(image, 4168, languageHandler);
    Put32(image, 4172, 2); // SCOPE_TABLE.Count
    Put32(image, 4176, begin);
    Put32(image, 4180, end);
    Put32(image, 4184, cleanup);
    Put32(image, 4188, 0); // finally scopes have no jump target
    Put32(image, 4192, begin);
    Put32(image, 4196, landing);
    Put32(image, 4200, filter);
    Put32(image, 4204, landing);
    *table = (PRUNTIME_FUNCTION)(image + 4224);
    (*table)->BeginAddress = 0;
    (*table)->EndAddress = functionEnd;
    (*table)->UnwindData = 4160;
    if (!VirtualProtect(image, 4096, PAGE_EXECUTE_READ, &oldProtection)
        || !VirtualProtect(image + 4096, 4096, PAGE_READONLY, &oldProtection)
        || !FlushInstructionCache(GetCurrentProcess(), image, 4096)
        || !RtlAddFunctionTable(*table, 1, (DWORD64)(uintptr_t)image))
    {
        VirtualFree(image, 0, MEM_RELEASE);
        return NULL;
    }
    return image;
}

static int Invoke(Entry entry, int mode)
{
    int result = -1;
    __try
    {
        result = entry(mode);
    }
    __except(GetExceptionCode() == FOREIGN_EXCEPTION ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH)
    {
        Record(4);
        result = 30;
    }
    return result;
}

static int Check(Entry entry, int dynamic, int mode)
{
    static const LONG expected[][3] = { { 2, 0, 0 }, { 1, 2, 3 }, { 1, 2, 4 } };
    int result;
    int count = mode == 0 ? 1 : 3;
    eventCount = 0;
    frameErrors = 0;
    abnormalFinally = 0;
    result = Invoke(entry, mode);
    if (result != (mode + 1) * 10 || eventCount != count || frameErrors != 0
        || (dynamic && abnormalFinally != (mode != 0)))
    {
        fprintf(stderr, "mode=%d dynamic=%d result=%d count=%ld frameErrors=%ld abnormal=%ld\n",
            mode, dynamic, result, eventCount, frameErrors, abnormalFinally);
        return 1;
    }
    for (int index = 0; index < count; index++)
    {
        if (events[index] != expected[mode][index])
        {
            fprintf(stderr, "event[%d]=%ld expected=%ld\n", index, events[index], expected[mode][index]);
            return 1;
        }
    }
    printf("%s mode=%d result=%d events=%ld,%ld,%ld\n", dynamic ? "dynamic" : "C",
        mode, result, events[0], count > 1 ? events[1] : 0, count > 2 ? events[2] : 0);
    return 0;
}

int main(void)
{
    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    FARPROC handler = ntdll != NULL ? GetProcAddress(ntdll, "__C_specific_handler") : NULL;
    PRUNTIME_FUNCTION table = NULL;
    BYTE* image;
    int result = 0;
    DWORD64 imageBase = 0;
    MEMORY_BASIC_INFORMATION protection;
    if (handler == NULL || (image = BuildDynamic(handler, &table)) == NULL)
    {
        fprintf(stderr, "Cannot establish dynamic SEH fixture: %lu\n", GetLastError());
        return 1;
    }
    if (RtlLookupFunctionEntry((DWORD64)(uintptr_t)(image + 16), &imageBase, NULL) != table
        || imageBase != (DWORD64)(uintptr_t)image)
    {
        result = 1;
    }
    if (VirtualQuery(image, &protection, sizeof(protection)) != sizeof(protection)
        || protection.Protect != PAGE_EXECUTE_READ
        || VirtualQuery(image + 4096, &protection, sizeof(protection)) != sizeof(protection)
        || protection.Protect != PAGE_READONLY)
    {
        result = 1;
    }
    for (int mode = 0; mode < 3 && result == 0; mode++)
    {
        result |= Check(CReference, 0, mode);
        result |= Check((Entry)(uintptr_t)image, 1, mode);
    }
    if (!RtlDeleteFunctionTable(table))
    {
        result = 1;
    }
    if (RtlLookupFunctionEntry((DWORD64)(uintptr_t)(image + 16), &imageBase, NULL) != NULL)
    {
        result = 1;
    }
    if (!VirtualFree(image, 0, MEM_RELEASE))
    {
        result = 1;
    }
    return result;
}
