// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeUtf16ZContract
{
    internal const int MaximumStorageBytes = 8 * 1024 * 1024;

    internal static bool IsValidLiteral(string value)
    {
        if (value.Contains('\0') || ((long)value.Length + 1) * sizeof(char) > MaximumStorageBytes)
        {
            return false;
        }
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidPool(ImmutableArray<string> literals)
    {
        if (literals.IsDefault)
        {
            return false;
        }
        long byteCount = 0;
        foreach (var literal in literals)
        {
            if (literal is null || !IsValidLiteral(literal))
            {
                return false;
            }
            byteCount += ((long)literal.Length + 1) * sizeof(char);
            if (byteCount > MaximumStorageBytes)
            {
                return false;
            }
        }
        return true;
    }

    internal static long StorageBytes(string value) => ((long)value.Length + 1) * sizeof(char);
}

internal static class NativeUtf8ZContract
{
    internal const int MaximumStorageBytes = NativeUtf16ZContract.MaximumStorageBytes;

    internal static bool IsValidLiteral(string value)
    {
        if (value.Length + 1L > MaximumStorageBytes)
        {
            return false;
        }
        foreach (var character in value)
        {
            if (character is '\0' or > '\u007F')
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidPool(ImmutableArray<string> literals)
    {
        if (literals.IsDefault)
        {
            return false;
        }
        long byteCount = 0;
        foreach (var literal in literals)
        {
            if (literal is null || !IsValidLiteral(literal))
            {
                return false;
            }
            byteCount += StorageBytes(literal);
            if (byteCount > MaximumStorageBytes)
            {
                return false;
            }
        }
        return true;
    }

    internal static long StorageBytes(string value) => (long)value.Length + 1;
}

internal static class NativeStringZPoolContract
{
    internal const int MaximumStorageBytes = NativeUtf16ZContract.MaximumStorageBytes;

    internal static bool IsValid(ImmutableArray<string> utf16Literals, ImmutableArray<string> utf8Literals)
    {
        if (!NativeUtf16ZContract.IsValidPool(utf16Literals)
            || !NativeUtf8ZContract.IsValidPool(utf8Literals))
        {
            return false;
        }

        long byteCount = 0;
        foreach (var literal in utf16Literals)
        {
            byteCount += NativeUtf16ZContract.StorageBytes(literal);
            if (byteCount > MaximumStorageBytes)
            {
                return false;
            }
        }
        foreach (var literal in utf8Literals)
        {
            byteCount += NativeUtf8ZContract.StorageBytes(literal);
            if (byteCount > MaximumStorageBytes)
            {
                return false;
            }
        }
        return true;
    }
}
