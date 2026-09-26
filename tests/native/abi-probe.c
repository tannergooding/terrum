// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

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
typedef struct Nested { Pair Value; } Nested;
typedef Nested (*MakeNested)(int32_t, int32_t);
typedef int32_t (*ReadNested)(Nested, Nested, Nested, Nested, Nested);
typedef uint8_t (*TryNested)(uint8_t, Nested*);
typedef struct AlignedInner { uint16_t Unit; uint8_t Valid; } AlignedInner;
typedef struct AlignedOuter { uint16_t Prefix; AlignedInner Value; uint16_t Suffix; } AlignedOuter;
typedef AlignedOuter (*MakeAligned)(void);
typedef int32_t (*ReadAligned)(AlignedOuter);
typedef struct Three { uint8_t First; uint8_t Second; uint8_t Third; } Three;
typedef struct Six { uint16_t First; uint16_t Second; uint16_t Third; } Six;
typedef struct Twelve { int32_t First; int32_t Second; int32_t Third; } Twelve;
typedef struct Wide { Twelve Value; int32_t Tail; } Wide;
typedef Three (*MakeThree)(void);
typedef int32_t (*ReadThree)(Three);
typedef Six (*MakeSix)(void);
typedef int32_t (*ReadSix)(Six);
typedef Twelve (*MakeTwelve)(int32_t, int32_t, int32_t);
typedef int32_t (*ReadMixed)(Twelve, Three, Pair, Six, Twelve, int32_t);
typedef Wide (*MakeWide)(int32_t);
typedef int32_t (*ReadWide)(Wide);
typedef Wide (*EchoWide)(Wide, Wide, Wide, Wide, Wide);
typedef Wide (*RecurseWide)(int32_t);
typedef uint8_t (*TryWide)(uint8_t, Wide*);
typedef uint8_t (*TryThree)(uint8_t, Three*);
typedef uint8_t (*TrySix)(uint8_t, Six*);
typedef uint8_t (*TryTwelve)(uint8_t, Twelve*);
typedef Wide (*MakeShiftedWide)(int32_t, int32_t, int32_t, int32_t, int32_t);
typedef void* (*RawIndirectReturn)(void*, int32_t, int32_t, int32_t);
typedef void* (*RawWideIntegerReturn)(void*, int64_t, uint64_t);
typedef int64_t (*SumInt64)(int64_t, int64_t, int64_t, int64_t, int64_t, int64_t);
typedef uint64_t (*EchoUInt64)(uint64_t);
typedef struct WideIntegerPair { int64_t Signed; uint64_t Unsigned; } WideIntegerPair;
typedef WideIntegerPair (*MakeWideIntegerPair)(int64_t, uint64_t);
typedef int32_t (*ReadWideIntegerPair)(WideIntegerPair, int64_t, int64_t, int64_t, int64_t);
typedef uint8_t (*TryWideIntegerPair)(uint8_t, WideIntegerPair*);
typedef struct SmallOwner { int32_t Value; } SmallOwner;
typedef struct WordOwner { uint64_t Value; } WordOwner;
typedef struct LargeOwner { int64_t Signed; uint64_t Unsigned; } LargeOwner;
typedef int32_t (*ReadSmallOwner)(const SmallOwner*);
typedef uint64_t (*ReadWordOwner)(const WordOwner*);
typedef uint64_t (*ReadLargeOwner)(const LargeOwner*);
typedef WideIntegerPair (*PackSmallOwner)(const SmallOwner*, int64_t, int64_t, int64_t, uint64_t, uint64_t);
extern int32_t CallWithCarrier(const void*, uint64_t);

// Exercise the machine ABI directly: RCX is the hidden result pointer and RAX returns it.
static int CheckGuardedReturn(const void* function, int size, int32_t first, int32_t second, int32_t third)
{
    __declspec(align(16)) uint8_t guarded[48];
    memset(guarded, 0xA5, sizeof(guarded));
    RawIndirectReturn invoke = (RawIndirectReturn)function;
    if (invoke(guarded + 16, first, second, third) != guarded + 16)
    {
        return 0;
    }
    for (int index = 0; index < 48; index++)
    {
        if ((index < 16 || index >= 16 + size) && guarded[index] != 0xA5)
        {
            return 0;
        }
    }
    return 1;
}

static int CheckWideIntegerReturn(const void* function)
{
    __declspec(align(16)) uint8_t guarded[48];
    memset(guarded, 0xA5, sizeof(guarded));
    RawWideIntegerReturn invoke = (RawWideIntegerReturn)function;
    if (invoke(guarded + 16, INT64_MIN, UINT64_MAX) != guarded + 16)
    {
        return 0;
    }
    for (int index = 0; index < 48; index++)
    {
        if ((index < 16 || index >= 32) && guarded[index] != 0xA5)
        {
            return 0;
        }
    }
    WideIntegerPair result;
    memcpy(&result, guarded + 16, sizeof(result));
    return result.Signed == INT64_MIN && result.Unsigned == UINT64_MAX;
}

// The fixture supplies source-function RVAs from the emitted unwind
// table. Loading an EXE as a library does not execute its process entrypoint.
int main(int argc, char** argv)
{
    if (argc != 40)
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

    MakeNested makeNested = (MakeNested)((uint8_t*)image + strtoul(argv[10], NULL, 10));
    ReadNested readNested = (ReadNested)((uint8_t*)image + strtoul(argv[11], NULL, 10));
    TryNested tryNested = (TryNested)((uint8_t*)image + strtoul(argv[12], NULL, 10));
    Nested nested = makeNested(17, 25);
    if (nested.Value.Start != 17 || nested.Value.Length != 25
        || readNested(nested, nested, nested, nested, nested) != 109)
    {
        result = 100;
    }
    Nested guarded[3] = { {{-1, -2}}, {{-3, -4}}, {{-5, -6}} };
    if (!tryNested(1, &guarded[1]) || guarded[1].Value.Start != 17 || guarded[1].Value.Length != 25
        || guarded[0].Value.Start != -1 || guarded[0].Value.Length != -2
        || guarded[2].Value.Start != -5 || guarded[2].Value.Length != -6
        || tryNested(0, &guarded[1]) || guarded[1].Value.Start != 17 || guarded[1].Value.Length != 25)
    {
        result = 101;
    }
    MakeAligned makeAligned = (MakeAligned)((uint8_t*)image + strtoul(argv[13], NULL, 10));
    ReadAligned readAligned = (ReadAligned)((uint8_t*)image + strtoul(argv[14], NULL, 10));
    AlignedOuter aligned = makeAligned();
    if (aligned.Prefix != 1 || aligned.Value.Unit != 39 || aligned.Value.Valid != 1
        || aligned.Suffix != 2 || readAligned(aligned) != 42)
    {
        result = 102;
    }

    MakeThree makeThree = (MakeThree)((uint8_t*)image + strtoul(argv[15], NULL, 10));
    ReadThree readThree = (ReadThree)((uint8_t*)image + strtoul(argv[16], NULL, 10));
    MakeSix makeSix = (MakeSix)((uint8_t*)image + strtoul(argv[17], NULL, 10));
    ReadSix readSix = (ReadSix)((uint8_t*)image + strtoul(argv[18], NULL, 10));
    MakeTwelve makeTwelve = (MakeTwelve)((uint8_t*)image + strtoul(argv[19], NULL, 10));
    ReadMixed readMixed = (ReadMixed)((uint8_t*)image + strtoul(argv[20], NULL, 10));
    MakeWide makeWide = (MakeWide)((uint8_t*)image + strtoul(argv[21], NULL, 10));
    ReadWide readWide = (ReadWide)((uint8_t*)image + strtoul(argv[22], NULL, 10));
    EchoWide echoWide = (EchoWide)((uint8_t*)image + strtoul(argv[23], NULL, 10));
    RecurseWide recurseWide = (RecurseWide)((uint8_t*)image + strtoul(argv[24], NULL, 10));
    TryWide tryWide = (TryWide)((uint8_t*)image + strtoul(argv[25], NULL, 10));
    TryWide forwardWide = (TryWide)((uint8_t*)image + strtoul(argv[26], NULL, 10));
    Three three = makeThree();
    Six six = makeSix();
    Twelve twelve = makeTwelve(1, 2, 7);
    Wide wide = makeWide(9);
    if (sizeof(Three) != 3 || sizeof(Six) != 6 || sizeof(Twelve) != 12 || sizeof(Wide) != 16
        || three.First != 1 || three.Second != 0 || three.Third != 1
        || readThree(three) != 42 || six.First != 4 || six.Second != 5 || six.Third != 6
        || readSix(six) != 15 || twelve.First != 1 || twelve.Second != 2 || twelve.Third != 7
        || readMixed(twelve, three, value, six, twelve, 19) != 60
        || wide.Value.First != 10 || wide.Value.Second != 11 || wide.Value.Third != 12
        || wide.Tail != 9 || readWide(wide) != 42)
    {
        result = 103;
    }
    Wide echoed = echoWide(wide, wide, wide, wide, wide);
    Wide recursive = recurseWide(5);
    if (readWide(echoed) != 42 || readWide(recursive) != 42)
    {
        result = 104;
    }
    __declspec(align(16)) Wide guardedWide[3] = {
        {{-1, -2, -3}, -4}, {{-5, -6, -7}, -8}, {{-9, -10, -11}, -12}
    };
    if (((uintptr_t)&guardedWide[1] & 15) != 0
        || !tryWide(1, &guardedWide[1])
        || guardedWide[1].Value.First != 10 || guardedWide[1].Value.Second != 11
        || guardedWide[1].Value.Third != 12 || guardedWide[1].Tail != 9
        || guardedWide[0].Value.First != -1 || guardedWide[0].Tail != -4
        || guardedWide[2].Value.First != -9 || guardedWide[2].Tail != -12
        || forwardWide(0, &guardedWide[1]) || guardedWide[1].Tail != 9)
    {
        result = 105;
    }
    if (!forwardWide(1, &guardedWide[1]) || readWide(guardedWide[1]) != 42)
    {
        result = 106;
    }
    if (!CheckGuardedReturn((const void*)makeThree, 3, 0, 0, 0)
        || !CheckGuardedReturn((const void*)makeSix, 6, 0, 0, 0)
        || !CheckGuardedReturn((const void*)makeTwelve, 12, 1, 2, 7)
        || !CheckGuardedReturn((const void*)makeWide, 16, 9, 0, 0))
    {
        result = 108;
    }
    TryThree tryThree = (TryThree)((uint8_t*)image + strtoul(argv[27], NULL, 10));
    TrySix trySix = (TrySix)((uint8_t*)image + strtoul(argv[28], NULL, 10));
    TryTwelve tryTwelve = (TryTwelve)((uint8_t*)image + strtoul(argv[29], NULL, 10));
    MakeShiftedWide makeShiftedWide = (MakeShiftedWide)((uint8_t*)image + strtoul(argv[30], NULL, 10));
    Three guardedThree[3] = {{101, 102, 103}, {104, 105, 106}, {107, 108, 109}};
    Six guardedSix[3] = {{101, 102, 103}, {104, 105, 106}, {107, 108, 109}};
    Twelve guardedTwelve[3] = {{101, 102, 103}, {104, 105, 106}, {107, 108, 109}};
    if (!tryThree(1, &guardedThree[1]) || guardedThree[1].First != 1
        || guardedThree[1].Second != 0 || guardedThree[1].Third != 1
        || guardedThree[0].Third != 103 || guardedThree[2].First != 107
        || tryThree(0, &guardedThree[1]) || guardedThree[1].Third != 1
        || !trySix(1, &guardedSix[1]) || guardedSix[1].First != 4
        || guardedSix[1].Second != 5 || guardedSix[1].Third != 6
        || guardedSix[0].Third != 103 || guardedSix[2].First != 107
        || trySix(0, &guardedSix[1]) || guardedSix[1].Third != 6
        || !tryTwelve(1, &guardedTwelve[1]) || guardedTwelve[1].First != 1
        || guardedTwelve[1].Second != 2 || guardedTwelve[1].Third != 7
        || guardedTwelve[0].Third != 103 || guardedTwelve[2].First != 107
        || tryTwelve(0, &guardedTwelve[1]) || guardedTwelve[1].Third != 7
        || readWide(makeShiftedWide(1, 2, 3, 1, 2)) != 42)
    {
        result = 107;
    }

    SumInt64 sumInt64 = (SumInt64)((uint8_t*)image + strtoul(argv[31], NULL, 10));
    EchoUInt64 echoUInt64 = (EchoUInt64)((uint8_t*)image + strtoul(argv[32], NULL, 10));
    MakeWideIntegerPair makeWideIntegerPair = (MakeWideIntegerPair)((uint8_t*)image + strtoul(argv[33], NULL, 10));
    ReadWideIntegerPair readWideIntegerPair = (ReadWideIntegerPair)((uint8_t*)image + strtoul(argv[34], NULL, 10));
    TryWideIntegerPair tryWideIntegerPair = (TryWideIntegerPair)((uint8_t*)image + strtoul(argv[35], NULL, 10));
    WideIntegerPair integersWide = makeWideIntegerPair(INT64_MIN, UINT64_MAX);
    if (sumInt64(INT64_C(-2147483649), INT64_C(2147483648), 1, 2, 3, 37) != 42
        || echoUInt64(UINT64_MAX) != UINT64_MAX
        || integersWide.Signed != INT64_MIN || integersWide.Unsigned != UINT64_MAX
        || !CheckWideIntegerReturn((const void*)makeWideIntegerPair)
        || readWideIntegerPair(integersWide, INT64_C(-2147483649), INT64_C(2147483648), 1, 42) != 42)
    {
        result = 109;
    }
    __declspec(align(16)) WideIntegerPair guardedInteger[3] = {
        {-1, 2}, {-3, 4}, {-5, 6}
    };
    if (!tryWideIntegerPair(1, &guardedInteger[1])
        || guardedInteger[1].Signed != INT64_MIN || guardedInteger[1].Unsigned != UINT64_MAX
        || guardedInteger[0].Signed != -1 || guardedInteger[0].Unsigned != 2
        || guardedInteger[2].Signed != -5 || guardedInteger[2].Unsigned != 6
        || tryWideIntegerPair(0, &guardedInteger[1])
        || guardedInteger[1].Signed != INT64_MIN || guardedInteger[1].Unsigned != UINT64_MAX)
    {
        result = 110;
    }

    ReadSmallOwner readSmallOwner = (ReadSmallOwner)((uint8_t*)image + strtoul(argv[36], NULL, 10));
    ReadWordOwner readWordOwner = (ReadWordOwner)((uint8_t*)image + strtoul(argv[37], NULL, 10));
    ReadLargeOwner readLargeOwner = (ReadLargeOwner)((uint8_t*)image + strtoul(argv[38], NULL, 10));
    PackSmallOwner packSmallOwner = (PackSmallOwner)((uint8_t*)image + strtoul(argv[39], NULL, 10));
    SmallOwner smallOwners[3] = {{-1}, {42}, {-2}};
    WordOwner wordOwner = {UINT64_MAX};
    LargeOwner largeOwner = {INT64_MIN, UINT64_MAX};
    WideIntegerPair packed = packSmallOwner(&smallOwners[1], -INT64_C(4294967296), INT64_C(4294967296),
        42, UINT64_MAX - 42, 42);
    if (sizeof(SmallOwner) != 4 || sizeof(WordOwner) != 8 || sizeof(LargeOwner) != 16
        || readSmallOwner(&smallOwners[1]) != 42 || readWordOwner(&wordOwner) != UINT64_MAX
        || readLargeOwner(&largeOwner) != UINT64_MAX || packed.Signed != 84 || packed.Unsigned != UINT64_MAX
        || smallOwners[0].Value != -1 || smallOwners[1].Value != 42 || smallOwners[2].Value != -2
        || wordOwner.Value != UINT64_MAX || largeOwner.Signed != INT64_MIN || largeOwner.Unsigned != UINT64_MAX)
    {
        result = 111;
    }

    if (!FreeLibrary(image))
    {
        return 95;
    }

    return result;
}
