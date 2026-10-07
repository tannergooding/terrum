#include <stdint.h>

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
