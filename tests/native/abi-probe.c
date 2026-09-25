// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>

typedef struct Pair
{
    int32_t Start;
    int32_t Length;
} Pair;

typedef Pair (*MakePair)(int32_t, int32_t);
typedef int32_t (*ReadPair)(Pair);
typedef uint8_t (*TryPair)(int32_t, int32_t, Pair*);
typedef uint8_t (*TryInt32)(int32_t*);
typedef uint8_t (*TryChar16)(uint16_t*);
typedef uint8_t (*TryBool)(uint8_t*);
extern int32_t CallWithCarrier(const void*, uint64_t);

// The fixture supplies source-function RVAs from the emitted unwind
// table. Loading an EXE as a library does not execute its process entrypoint.
int main(int argc, char** argv)
{
    if (argc != 10)
    {
        return 90;
    }

    HMODULE image = LoadLibraryExA(argv[1], NULL, DONT_RESOLVE_DLL_REFERENCES);
    if (image == NULL)
    {
        return 91;
    }

    MakePair make = (MakePair)((uint8_t*)image + strtoul(argv[2], NULL, 10));
    ReadPair read = (ReadPair)((uint8_t*)image + strtoul(argv[3], NULL, 10));
    TryPair attempt = (TryPair)((uint8_t*)image + strtoul(argv[4], NULL, 10));
    TryInt32 attemptInt32 = (TryInt32)((uint8_t*)image + strtoul(argv[5], NULL, 10));
    TryChar16 attemptChar16 = (TryChar16)((uint8_t*)image + strtoul(argv[6], NULL, 10));
    TryBool attemptBool = (TryBool)((uint8_t*)image + strtoul(argv[7], NULL, 10));
    Pair value = make(17, 25);
    int result = 0;
    if (value.Start != 17 || value.Length != 25 || read(value) != 42)
    {
        result = 92;
    }

    Pair published = { -7, -8 };
    if (!attempt(4, 5, &published) || published.Start != 4 || published.Length != 5)
    {
        result = 93;
    }
    published.Start = -7;
    published.Length = -8;
    if (attempt(-1, 5, &published) || published.Start != -7 || published.Length != -8)
    {
        result = 94;
    }

    int32_t integers[4] = { 123, 123, 123, 123 };
    uint16_t units[6] = { 123, 123, 123, 123, 123, 123 };
    uint8_t booleans[10] = { 123, 123, 123, 123, 123, 123, 123, 123, 123, 123 };
    if (!attemptInt32(&integers[1]) || integers[1] != INT32_MIN
        || integers[0] != 123 || integers[2] != 123 || integers[3] != 123)
    {
        result = 96;
    }
    if (!attemptChar16(&units[1]) || units[1] != 55296
        || units[0] != 123 || units[2] != 123 || units[3] != 123 || units[4] != 123 || units[5] != 123)
    {
        result = 97;
    }
    if (!attemptBool(&booleans[1]) || booleans[1] != 1 || booleans[0] != 123)
    {
        result = 98;
    }
    for (int index = 2; index < 10; index++)
    {
        if (booleans[index] != 123)
        {
            result = 98;
        }
    }
    const void* readChar16 = (uint8_t*)image + strtoul(argv[8], NULL, 10);
    const void* readBool = (uint8_t*)image + strtoul(argv[9], NULL, 10);
    if (CallWithCarrier(readChar16, UINT64_C(0x123456789ABCFFFF)) != 65535
        || CallWithCarrier(readBool, UINT64_C(0x123456789ABCFF00)) != 0
        || CallWithCarrier(readBool, UINT64_C(0x123456789ABCFF01)) != 1)
    {
        result = 99;
    }

    if (!FreeLibrary(image))
    {
        return 95;
    }

    return result;
}
