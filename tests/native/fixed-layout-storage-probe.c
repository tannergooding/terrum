#include <stdint.h>
#include <stddef.h>

typedef struct Int32Carrier { int32_t Value; } Int32Carrier;
typedef struct UInt32Carrier { uint32_t Value; } UInt32Carrier;
typedef struct Int64Carrier { int64_t Value; } Int64Carrier;
typedef struct UInt64Carrier { uint64_t Value; } UInt64Carrier;
typedef struct Char16Carrier { uint16_t Value; } Char16Carrier;

typedef struct Inner
{
    int32_t Signed;
    uint16_t Unit;
} Inner;

typedef struct Outer
{
    uint16_t Prefix;
    Inner Value;
    uint32_t Tail;
} Outer;

typedef struct Padded
{
    uint16_t Prefix;
    uint32_t Value;
    uint16_t Suffix;
} Padded;

typedef struct Over16
{
    uint16_t Prefix;
    int64_t Value;
    uint16_t Suffix;
} Over16;

#define ASSERT_LAYOUT(type, size, alignment, field, offset) \
    typedef char type##_size_must_match[(sizeof(type) == (size)) ? 1 : -1]; \
    typedef char type##_alignment_must_match[(__alignof(type) == (alignment)) ? 1 : -1]; \
    typedef char type##_##field##_offset_must_match[(offsetof(type, field) == (offset)) ? 1 : -1]

ASSERT_LAYOUT(Int32Carrier, 4, 4, Value, 0);
ASSERT_LAYOUT(UInt32Carrier, 4, 4, Value, 0);
ASSERT_LAYOUT(Int64Carrier, 8, 8, Value, 0);
ASSERT_LAYOUT(UInt64Carrier, 8, 8, Value, 0);
ASSERT_LAYOUT(Char16Carrier, 2, 2, Value, 0);
ASSERT_LAYOUT(Inner, 8, 4, Signed, 0);
typedef char Inner_Unit_offset_must_match[(offsetof(Inner, Unit) == 4) ? 1 : -1];
ASSERT_LAYOUT(Outer, 16, 4, Prefix, 0);
typedef char Outer_Value_offset_must_match[(offsetof(Outer, Value) == 4) ? 1 : -1];
typedef char Outer_Tail_offset_must_match[(offsetof(Outer, Tail) == 12) ? 1 : -1];
ASSERT_LAYOUT(Padded, 12, 4, Prefix, 0);
typedef char Padded_Value_offset_must_match[(offsetof(Padded, Value) == 4) ? 1 : -1];
typedef char Padded_Suffix_offset_must_match[(offsetof(Padded, Suffix) == 8) ? 1 : -1];
ASSERT_LAYOUT(Over16, 24, 8, Prefix, 0);
typedef char Over16_Value_offset_must_match[(offsetof(Over16, Value) == 8) ? 1 : -1];
typedef char Over16_Suffix_offset_must_match[(offsetof(Over16, Suffix) == 16) ? 1 : -1];

int main(void)
{
    return 0;
}
