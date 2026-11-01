// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Modules;

internal sealed record TrRmRegisterBodyMaps(
    ReadOnlyMemory<byte> TypeMap,
    ReadOnlyMemory<byte> MethodMap,
    ImmutableDictionary<uint, ushort> TypeMapIndices,
    ImmutableDictionary<uint, ushort> MethodMapIndices)
{
    internal ushort GetTypeMapIndex(uint token)
        => TypeMapIndices.TryGetValue(token, out var index)
            ? index : throw new KeyNotFoundException("TypeMap token was not collected.");

    internal ushort GetMethodMapIndex(uint token)
        => MethodMapIndices.TryGetValue(token, out var index)
            ? index : throw new KeyNotFoundException("MethodMap token was not collected.");
}

internal static class TrRmRegisterBodyMapBuilder
{
    internal static TrRmRegisterBodyMaps Create(IEnumerable<uint> typeTokens,
        IEnumerable<uint> methodTokens)
    {
        ArgumentNullException.ThrowIfNull(typeTokens);
        ArgumentNullException.ThrowIfNull(methodTokens);
        var typeSet = Collect(typeTokens, nameof(typeTokens));
        var methodSet = Collect(methodTokens, nameof(methodTokens));
        var typeMap = Encode(typeSet, out var typeMapIndices);
        var methodMap = Encode(methodSet, out var methodMapIndices);
        return new TrRmRegisterBodyMaps(typeMap, methodMap,
            typeMapIndices, methodMapIndices);
    }

    private static ImmutableArray<uint> Collect(IEnumerable<uint> tokens,
        string parameterName)
    {
        var result = ImmutableArray.CreateBuilder<uint>();
        var seen = ImmutableHashSet.CreateBuilder<uint>();
        foreach (var token in tokens)
        {
            if (seen.Contains(token))
            {
                continue;
            }
            if (result.Count == 65_536)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            _ = seen.Add(token);
            result.Add(token);
        }

        return result.ToImmutable();
    }

    private static byte[] Encode(IReadOnlyList<uint> tokens,
        out ImmutableDictionary<uint, ushort> indexes)
    {
        var bytes = new byte[tokens.Count * sizeof(uint)];
        var indexBuilder = ImmutableDictionary.CreateBuilder<uint, ushort>();
        var index = 0;
        foreach (var token in tokens)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint)), token);
            indexBuilder.Add(token, checked((ushort)index));
            index++;
        }

        indexes = indexBuilder.ToImmutable();
        return bytes;
    }
}
