// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef uint64_t Buffer;
typedef uint8_t (*TryAcquire)(Buffer*);
typedef Buffer (*Build)(void);
typedef Buffer (*Relay)(Buffer);
typedef Buffer (*Format)(uint64_t, Buffer);
typedef Buffer (*FormatSigned)(int64_t, Buffer);
typedef int32_t (*Consume)(Buffer);
typedef int32_t (*Check)(void);
typedef uint8_t (*TryRebuild)(void);

static void* live[16];
static HANDLE heaps[16];
static int attempts;
static int releases;
static int failAttempt;
static int vacantAttempt;
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
    if ((flags & HEAP_GENERATE_EXCEPTIONS) != 0
        || (attempts == vacantAttempt && LiveCount() != 0))
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
        if (live[index] == allocation && allocation && heaps[index] == heap)
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

// BOOL occupies only EAX; the unused upper return-register bits must not turn false into true.
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

static void Reset(void)
{
    attempts = 0;
    releases = 0;
    failAttempt = 0;
    vacantAttempt = 0;
}

static int MatchesDigits(Buffer buffer, const char* expected)
{
    const int32_t* words = (const int32_t*)(uintptr_t)buffer;
    int length = (int)strlen(expected);
    if (words[0] != length || words[1] != 24)
    {
        return 0;
    }
    for (int index = 0; index < length; index++)
    {
        if (words[index + 2] != (unsigned char)expected[index])
        {
            return 0;
        }
    }
    return 1;
}

#define VERIFY(condition, code) do { \
    if (!(condition)) { \
        fprintf(stderr, "Owned-buffer ABI check failed at line %d.\n", __LINE__); \
        return code; \
    } \
} while (0)

static int RunChecks(HMODULE image, int checked)
{
    TryAcquire acquire = (TryAcquire)GetProcAddress(image, "TryAcquire");
    Build build = (Build)GetProcAddress(image, "Build");
    Relay relay = (Relay)GetProcAddress(image, "Relay");
    Consume consume = (Consume)GetProcAddress(image, "Consume");
    Check rebuild = (Check)GetProcAddress(image, "Rebuild");
    TryRebuild tryRebuild = (TryRebuild)GetProcAddress(image, "TryRebuild");
    Check composite = (Check)GetProcAddress(image, "Composite");
    Check recover = (Check)GetProcAddress(image, "Recover");
    Check fieldMutation = (Check)GetProcAddress(image, "FieldMutation");
    Check recoverField = (Check)GetProcAddress(image, "RecoverField");
    Build workspace = (Build)GetProcAddress(image, "FormattingWorkspace");
    Format format = (Format)GetProcAddress(image, "FormatDigits");
    FormatSigned formatSigned = (FormatSigned)GetProcAddress(image, "FormatSignedDigits");
    Check recoverFormatting = (Check)GetProcAddress(image, "RecoverFormatting");
    VERIFY(acquire && build && relay && consume && rebuild && tryRebuild && composite && fieldMutation, 10);
    VERIFY(workspace && format && formatSigned, 14);
    VERIFY(!checked || (recover && recoverFormatting && recoverField), 11);
    VERIFY(ReplaceImport(image, "HeapAlloc", (void*)TrackedAllocate), 12);
    VERIFY(ReplaceImport(image, "HeapFree", (void*)TrackedRelease), 13);

    __declspec(align(16)) unsigned char storage[48];
    memset(storage, 0xA5, sizeof(storage));
    failAttempt = 1;
    VERIFY(acquire((Buffer*)(storage + 16)) == 0, 20);
    for (int index = 0; index < 48; index++)
    {
        VERIFY(storage[index] == 0xA5, 21);
    }
    VERIFY(attempts == 1 && releases == 0 && LiveCount() == 0 && !invalid, 22);

    Reset();
    VERIFY(acquire((Buffer*)(storage + 16)) == 1, 23);
    for (int index = 0; index < 48; index++)
    {
        if (index < 16 || index >= 24)
        {
            VERIFY(storage[index] == 0xA5, 24);
        }
    }
    Buffer buffer;
    memcpy(&buffer, storage + 16, sizeof(buffer));
    VERIFY(attempts == 1 && releases == 0 && LiveCount() == 1 && !invalid, 25);
    buffer = relay(buffer);
    VERIFY(attempts == 1 && releases == 0 && LiveCount() == 1 && !invalid, 26);
    VERIFY(consume(buffer) == 0, 27);
    VERIFY(releases == 1 && LiveCount() == 0 && !invalid, 28);

    Reset();
    buffer = build();
    VERIFY(attempts == 1 && releases == 0 && LiveCount() == 1 && !invalid, 30);
    VERIFY(consume(buffer) == 42, 31);
    VERIFY(releases == 1 && LiveCount() == 0 && !invalid, 32);

    Reset();
    vacantAttempt = 2;
    VERIFY(rebuild() == 42, 40);
    VERIFY(attempts == 2 && releases == 2 && LiveCount() == 0 && !invalid, 41);

    Reset();
    failAttempt = 2;
    vacantAttempt = 2;
    VERIFY(tryRebuild() == 0, 42);
    VERIFY(attempts == 2 && releases == 1 && LiveCount() == 0 && !invalid, 43);

    Reset();
    VERIFY(composite() == 42, 50);
    VERIFY(attempts == 1 && releases == 1 && LiveCount() == 0 && !invalid, 51);

    Reset();
    VERIFY(fieldMutation() == 42, 52);
    VERIFY(attempts == 1 && releases == 1 && LiveCount() == 0 && !invalid, 53);

    if (checked)
    {
        Reset();
        VERIFY(recover() == 42, 60);
        VERIFY(attempts == 1 && releases == 1 && LiveCount() == 0 && !invalid, 61);
        Reset();
        VERIFY(recoverField() == 42, 62);
        VERIFY(attempts == 1 && releases == 1 && LiveCount() == 0 && !invalid, 63);
    }

    Reset();
    buffer = workspace();
    Buffer original = buffer;
    VERIFY(attempts == 1 && releases == 0 && LiveCount() == 1
        && (void*)(uintptr_t)buffer == live[0] && !invalid, 70);
    buffer = format(UINT64_MAX, buffer);
    VERIFY(buffer == original && MatchesDigits(buffer, "18446744073709551615"), 71);
    buffer = formatSigned(INT64_MIN, buffer);
    VERIFY(buffer == original && MatchesDigits(buffer, "-9223372036854775808"), 77);
    buffer = format(42, buffer);
    VERIFY(buffer == original && MatchesDigits(buffer, "42"), 72);
    buffer = format(0, buffer);
    VERIFY(buffer == original && MatchesDigits(buffer, "0"), 73);
    VERIFY(attempts == 1 && releases == 0 && LiveCount() == 1 && !invalid, 74);
    VERIFY(consume(buffer) == 48, 75);
    VERIFY(attempts == 1 && releases == 1 && LiveCount() == 0 && !invalid, 76);
    if (checked)
    {
        Reset();
        VERIFY(recoverFormatting() == 42, 80);
        VERIFY(attempts == 1 && releases == 1 && LiveCount() == 0 && !invalid, 81);
    }
    return 0;
}

static int RunReleaseFailure(HMODULE image, int checked)
{
    VERIFY(ReplaceImport(image, "HeapAlloc", (void*)TrackedAllocate), 12);
    VERIFY(ReplaceImport(image, "HeapFree", (void*)RejectRelease), 13);
    if (checked)
    {
        Check recover = (Check)GetProcAddress(image, "Recover");
        VERIFY(recover, 11);
        recover();
    }
    else
    {
        Build build = (Build)GetProcAddress(image, "Build");
        Consume consume = (Consume)GetProcAddress(image, "Consume");
        VERIFY(build && consume, 10);
        Buffer buffer = build();
        consume(buffer);
    }
    fprintf(stderr, "Failed heap release returned or was caught instead of terminating.\n");
    return 70;
}

int main(int argc, char** argv)
{
    if ((argc != 3 && argc != 4)
        || (strcmp(argv[2], "checked") != 0 && strcmp(argv[2], "fail-fast") != 0)
        || (argc == 4 && strcmp(argv[3], "reject-release") != 0))
    {
        return 1;
    }
    HMODULE image = LoadLibraryA(argv[1]);
    if (!image)
    {
        fprintf(stderr, "Could not load owned-buffer ABI fixture: %lu\n", GetLastError());
        return 2;
    }
    int result;
    __try
    {
        int checked = strcmp(argv[2], "checked") == 0;
        result = argc == 4 ? RunReleaseFailure(image, checked) : RunChecks(image, checked);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        fprintf(stderr, "Unexpected owned-buffer ABI exception: %08lX\n", GetExceptionCode());
        result = 3;
    }
    if (!FreeLibrary(image))
    {
        return 4;
    }
    if (result == 0)
    {
        puts("owned-buffer-abi opaque=8 publication=checked views=scoped reuse=checked field=tracked heap=tracked live=0");
    }
    return result;
}
