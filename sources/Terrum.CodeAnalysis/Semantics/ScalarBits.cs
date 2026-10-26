using System;
using System.Numerics;

namespace Terrum.CodeAnalysis.Semantics;

internal readonly record struct ScalarBits(ulong Low, ulong High)
{
    internal static ScalarBits FromLegacyValue(long value, PrimitiveType type)
    {
        var width = PrimitiveTypeFacts.ScalarWidth(type);
        if (width > sizeof(ulong))
        {
            throw new ArgumentException("The legacy scalar payload cannot represent this type.", nameof(type));
        }
        var low = unchecked((ulong)value);
        if (width is > 0 and < sizeof(ulong))
        {
            low &= (1UL << (width * 8)) - 1;
        }
        return new ScalarBits(low, 0);
    }

    internal static ScalarBits FromPayload(PrimitiveType type, ReadOnlySpan<byte> payload)
    {
        var width = PrimitiveTypeFacts.ScalarWidth(type);
        if (width < 0 || payload.Length != width)
        {
            throw new ArgumentException("The scalar payload extent does not match its complete type.", nameof(payload));
        }
        var lowLength = Math.Min(width, sizeof(ulong));
        var low = ReadWord(payload[..lowLength]);
        var high = width > sizeof(ulong) ? ReadWord(payload[sizeof(ulong)..]) : 0;
        return new ScalarBits(low, high);
    }

    internal static bool TryFromIntegerValue(BigInteger value, PrimitiveType type,
        out ScalarBits bits)
    {
        var minimum = type == PrimitiveType.Int128
            ? -(BigInteger.One << 127) : BigInteger.Zero;
        var maximum = type switch {
            PrimitiveType.Int128 => (BigInteger.One << 127) - BigInteger.One,
            PrimitiveType.UInt128 => (BigInteger.One << 128) - BigInteger.One,
            _ => BigInteger.MinusOne,
        };
        if (maximum < BigInteger.Zero || value < minimum || value > maximum)
        {
            bits = default;
            return false;
        }
        var raw = value.Sign < 0 ? (BigInteger.One << 128) + value : value;
        bits = new ScalarBits((ulong)(raw & ulong.MaxValue), (ulong)(raw >> 64));
        return true;
    }

    internal bool TryGetIntegerValue(PrimitiveType type, out BigInteger value)
    {
        if (type is not (PrimitiveType.Int128 or PrimitiveType.UInt128)
            || !IsCanonicalFor(type))
        {
            value = BigInteger.Zero;
            return false;
        }

        var raw = (new BigInteger(High) << 64) | Low;
        if (type == PrimitiveType.Int128 && (High & 0x8000_0000_0000_0000UL) != 0)
        {
            raw -= BigInteger.One << 128;
        }
        value = raw;
        return true;
    }

    internal ScalarBits ToggleSign(PrimitiveType type)
    {
        var width = PrimitiveTypeFacts.ScalarWidth(type);
        if (!PrimitiveTypeFacts.IsFloating(type) || width is not (2 or 4 or 8 or 16))
        {
            throw new ArgumentException("The complete type is not a supported floating format.", nameof(type));
        }

        return width == 16
            ? this with { High = High ^ 0x8000_0000_0000_0000UL }
            : this with { Low = Low ^ (1UL << ((width * 8) - 1)) };
    }

    internal bool IsCanonicalFor(PrimitiveType type)
    {
        var width = PrimitiveTypeFacts.ScalarWidth(type);
        if (width < 0)
        {
            return High == 0;
        }
        if (width == 0)
        {
            return Low == 0 && High == 0;
        }
        if (width <= sizeof(ulong))
        {
            return High == 0 && (width == sizeof(ulong) || Low >> (width * 8) == 0);
        }
        return width == 2 * sizeof(ulong);
    }

    internal void WritePayload(PrimitiveType type, Span<byte> payload)
    {
        var width = PrimitiveTypeFacts.ScalarWidth(type);
        if (width < 0 || payload.Length != width || !IsCanonicalFor(type))
        {
            throw new ArgumentException("The scalar bit payload is invalid for its complete type.", nameof(payload));
        }
        WriteWord(Low, payload[..Math.Min(width, sizeof(ulong))]);
        if (width > sizeof(ulong))
        {
            WriteWord(High, payload[sizeof(ulong)..]);
        }
    }

    internal bool TryGetLegacyValue(PrimitiveType type, out long value)
    {
        var width = PrimitiveTypeFacts.ScalarWidth(type);
        if (width < 0)
        {
            value = unchecked((long)Low);
            return High == 0;
        }
        if (width > sizeof(ulong))
        {
            value = default;
            return false;
        }
        if (width == 0)
        {
            value = 0;
            return IsCanonicalFor(type);
        }

        var raw = Low;
        value = type switch {
            PrimitiveType.Int8 => unchecked((sbyte)raw),
            PrimitiveType.Int16 => unchecked((short)raw),
            PrimitiveType.Int32 or PrimitiveType.UInt32 => unchecked((int)raw),
            PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.Char8
                or PrimitiveType.Char16 or PrimitiveType.Char32 or PrimitiveType.Rune
                => unchecked((long)raw),
            PrimitiveType.Float32 or PrimitiveType.Float16 or PrimitiveType.BFloat16
                or PrimitiveType.Decimal32 => unchecked((long)raw),
            PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float64 or PrimitiveType.Decimal64
                => unchecked((long)raw),
            PrimitiveType.Bool => unchecked((long)raw),
            _ => unchecked((long)raw),
        };
        return true;
    }

    private static ulong ReadWord(ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            value |= (ulong)bytes[index] << (index * 8);
        }
        return value;
    }

    private static void WriteWord(ulong value, Span<byte> bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(value >> (index * 8));
        }
    }
}
