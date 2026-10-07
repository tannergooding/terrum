#include <stdint.h>
#include <stddef.h>

__declspec(dllexport) int64_t __cdecl NativeFfiSigned(
    int32_t first, uint32_t second, int64_t third, uint64_t fourth,
    int32_t fifth, uint32_t sixth)
{
    if (first != -7 || second != 11 || third != -13 || fourth != 17
        || fifth != -19 || sixth != 23)
    {
        return 0;
    }
    return -INT64_C(1234567890123);
}

__declspec(dllexport) uint64_t __cdecl NativeFfiUnsigned(
    uint64_t first, uint32_t second, int64_t third, uint64_t fourth,
    uint32_t fifth)
{
    if (first != 2 || second != 3 || third != -5 || fourth != 7 || fifth != 11)
    {
        return 0;
    }
    return UINT64_C(0xFEDCBA9876543210);
}

__declspec(dllexport) int32_t __cdecl NativeFfiUtf16Z(
    const uint16_t* first, uint32_t second, uint64_t third,
    const uint16_t* fourth, uint32_t fifth, const uint16_t* sixth)
{
    static const uint16_t expectedFirst[] = { 0x0041, 0x000A, 0xD83D, 0xDE42, 0x0000 };
    static const uint16_t expectedFourth[] = {
        0x0063, 0x0061, 0x0070, 0x0074, 0x0069, 0x006F, 0x006E, 0x0000
    };
    static const uint16_t expectedSixth[] = { 0x0000 };
    if (first == NULL)
    {
        return 1;
    }
    if (fourth == NULL)
    {
        return 2;
    }
    if (sixth == NULL)
    {
        return 3;
    }
    if (((uintptr_t)first & 1) != 0 || ((uintptr_t)fourth & 1) != 0
        || ((uintptr_t)sixth & 1) != 0)
    {
        return 4;
    }
    if (second != UINT32_C(324478056) || third != UINT64_C(81985529216486895)
        || fifth != UINT32_C(610800471))
    {
        return 5;
    }
    for (size_t index = 0; index < sizeof(expectedFirst) / sizeof(expectedFirst[0]); index++)
    {
        if (first[index] != expectedFirst[index])
        {
            return 6;
        }
    }
    for (size_t index = 0; index < sizeof(expectedFourth) / sizeof(expectedFourth[0]); index++)
    {
        if (fourth[index] != expectedFourth[index])
        {
            return 7;
        }
    }
    if (sixth[0] != expectedSixth[0])
    {
        return 8;
    }
    if (first == fourth || first == sixth || fourth == sixth)
    {
        return 9;
    }
    return 0;
}

__declspec(dllexport) int32_t __cdecl NativeFfiMessageBox(
    const void* owner, const uint16_t* text, const uint16_t* caption, uint32_t flags)
{
    static const uint16_t expectedText[] = { 0x0041, 0x000A, 0xD83D, 0xDE42, 0x0000 };
    static const uint16_t expectedCaption[] = {
        0x0063, 0x0061, 0x0070, 0x0074, 0x0069, 0x006F, 0x006E, 0x0000
    };
    if (owner != NULL)
    {
        return -1;
    }
    if (text == NULL || caption == NULL || ((uintptr_t)text & 1) != 0
        || ((uintptr_t)caption & 1) != 0)
    {
        return 2;
    }
    if (flags != 0)
    {
        return 3;
    }
    for (size_t index = 0; index < sizeof(expectedText) / sizeof(expectedText[0]); index++)
    {
        if (text[index] != expectedText[index])
        {
            return 4;
        }
    }
    for (size_t index = 0; index < sizeof(expectedCaption) / sizeof(expectedCaption[0]); index++)
    {
        if (caption[index] != expectedCaption[index])
        {
            return 5;
        }
    }
    return 1;
}
