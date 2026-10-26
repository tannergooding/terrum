// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Semantics;

internal static class PrimitiveTypeFacts
{
    internal static bool IsInteger(PrimitiveType type) => type is PrimitiveType.Int8 or PrimitiveType.Int16
        or PrimitiveType.Int32 or PrimitiveType.Int64 or PrimitiveType.UInt8 or PrimitiveType.UInt16
        or PrimitiveType.UInt32 or PrimitiveType.UInt64;

    internal static bool IsWideInteger(PrimitiveType type) =>
        type is PrimitiveType.Int128 or PrimitiveType.UInt128;

    internal static bool IsIntegerOrWide(PrimitiveType type) =>
        IsInteger(type) || IsWideInteger(type);

    internal static bool IsSignedInteger(PrimitiveType type) => type is PrimitiveType.Int8 or PrimitiveType.Int16
        or PrimitiveType.Int32 or PrimitiveType.Int64;

    internal static bool IsSignedIntegerOrWide(PrimitiveType type) =>
        IsSignedInteger(type) || type == PrimitiveType.Int128;

    internal static int IntegerWidth(PrimitiveType type) => type switch {
        PrimitiveType.Int8 or PrimitiveType.UInt8 => 8,
        PrimitiveType.Int16 or PrimitiveType.UInt16 => 16,
        PrimitiveType.Int32 or PrimitiveType.UInt32 => 32,
        PrimitiveType.Int64 or PrimitiveType.UInt64 => 64,
        _ => 0,
    };

    internal static int IntegerWidthOrWide(PrimitiveType type) =>
        IsWideInteger(type) ? 128 : IntegerWidth(type);

    internal static int ScalarWidth(PrimitiveType type) => type switch {
        PrimitiveType.Void => 0,
        PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Int8 or PrimitiveType.UInt8 => 1,
        PrimitiveType.Char16 or PrimitiveType.Int16 or PrimitiveType.UInt16
            or PrimitiveType.Float16 or PrimitiveType.BFloat16 => 2,
        PrimitiveType.Char32 or PrimitiveType.Rune or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Float32 or PrimitiveType.Decimal32 => 4,
        PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float64 or PrimitiveType.Decimal64 => 8,
        PrimitiveType.Int128 or PrimitiveType.UInt128 or PrimitiveType.Float128 or PrimitiveType.Decimal128 => 16,
        _ => -1,
    };

    internal static bool IsCodeUnitOrRune(PrimitiveType type) => type is PrimitiveType.Char8
        or PrimitiveType.Char16 or PrimitiveType.Char32 or PrimitiveType.Rune;
}
