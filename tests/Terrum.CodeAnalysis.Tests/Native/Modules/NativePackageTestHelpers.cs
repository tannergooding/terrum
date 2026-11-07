// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

internal static class NativePackageTestHelpers
{
    private const int HeaderSize = 18;

    internal static int ContractOffset(byte[] artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Length < HeaderSize || !artifact.AsSpan(0, 4).SequenceEqual("TNPK"u8)
            || artifact[4] != 3)
        {
            throw new InvalidDataException("Expected a version-3 TNPK test artifact.");
        }

        var at = checked(HeaderSize + artifact[7] + artifact[8]);
        var importCount = ReadUInt16(artifact, ref at);
        for (var index = 0; index < importCount; index++)
        {
            SkipString(artifact, ref at);
        }

        var expectationCount = ReadUInt16(artifact, ref at);
        for (var expectation = 0; expectation < expectationCount; expectation++)
        {
            for (var field = 0; field < 5; field++)
            {
                SkipString(artifact, ref at);
            }

            var parameterCount = ReadUInt16(artifact, ref at);
            Skip(artifact, ref at, checked(parameterCount * 14));
        }

        return at;
    }

    internal static int ImageOffset(byte[] artifact)
    {
        var contractOffset = ContractOffset(artifact);
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(
            artifact.AsSpan(10, sizeof(int)));
        if (contractLength < 0 || (long)contractOffset + contractLength > artifact.Length)
        {
            throw new InvalidDataException("TNPK contract extent is invalid.");
        }
        return contractOffset + contractLength;
    }

    private static int ReadUInt16(byte[] artifact, ref int at)
    {
        EnsureRemaining(artifact, at, sizeof(ushort));
        var value = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, sizeof(ushort)));
        at += sizeof(ushort);
        return value;
    }

    private static void SkipString(byte[] artifact, ref int at)
    {
        var length = ReadUInt16(artifact, ref at);
        Skip(artifact, ref at, length);
    }

    private static void Skip(byte[] artifact, ref int at, int length)
    {
        EnsureRemaining(artifact, at, length);
        at = checked(at + length);
    }

    private static void EnsureRemaining(byte[] artifact, int at, int length)
    {
        if (length < 0 || at < 0 || at > artifact.Length - length)
        {
            throw new InvalidDataException("TNPK table extent is truncated.");
        }
    }
}
