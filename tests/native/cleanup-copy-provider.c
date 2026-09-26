// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#include <stdint.h>

typedef struct Child { int32_t Tag; } Child;
typedef struct Parent { Child Value; int64_t Marker; } Parent;

static int parentCalls;
static int childCalls;
static int invalid;

__declspec(dllexport) void ClobberParent(Parent value)
{
    parentCalls++;
    volatile unsigned char* storage = (volatile unsigned char*)&value;
    for (unsigned int index = 0; index < sizeof(value); index++)
    {
        storage[index] = 0;
    }
}

__declspec(dllexport) void RecordChild(Child value)
{
    childCalls++;
    if (parentCalls != childCalls || value.Tag != 42)
    {
        invalid = 1;
    }
}

__declspec(dllexport) void ResetTrace(void)
{
    parentCalls = 0;
    childCalls = 0;
    invalid = 0;
}

__declspec(dllexport) int CheckTrace(int expected)
{
    return sizeof(Parent) == 16 && parentCalls == expected && childCalls == expected && invalid == 0;
}
