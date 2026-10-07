// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validated fixed Module rows and their decoded producer identities.</summary>
/// <remarks>The borrowed Module and String sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmModuleTableReader
{
    private const int RowSize = 44;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int _recordCount;
    private readonly int _moduleRefCount;
    private readonly int _typeDefCount;
    private readonly int _methodDefCount;
    private readonly int _sourceFileCount;
    private readonly Dictionary<string, int> _identities;

    public int Count => _recordCount;
    public int ModuleRefCount => _moduleRefCount;
    public int TypeDefCount => _typeDefCount;
    public int MethodDefCount => _methodDefCount;
    public int SourceFileCount => _sourceFileCount;

    /// <summary>Validates fixed Module rows and their cross-table ranges.</summary>
    /// <param name="section">The borrowed Module section bytes.</param>
    /// <param name="recordCount">The number of Module rows.</param>
    /// <param name="strings">The validated String table used for module identities.</param>
    /// <param name="moduleRefCount">The number of artifact ModuleRef rows.</param>
    /// <param name="typeDefCount">The number of artifact TypeDef rows.</param>
    /// <param name="methodDefCount">The number of artifact MethodDef rows.</param>
    /// <param name="sourceFileCount">The number of artifact SourceFile rows.</param>
    /// <param name="featureCount">The number of artifact Feature rows, or -1 to skip cross-table bounds checks.</param>
    public TrRmModuleTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, int moduleRefCount, int typeDefCount,
        int methodDefCount, int sourceFileCount, int featureCount = -1)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || !ValidCount(moduleRefCount) || !ValidCount(typeDefCount)
            || !ValidCount(methodDefCount) || !ValidCount(sourceFileCount)
            || featureCount < -1 || featureCount > 0xFF_FFFF)
        {
            throw InvalidModule();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        _moduleRefCount = moduleRefCount;
        _typeDefCount = typeDefCount;
        _methodDefCount = methodDefCount;
        _sourceFileCount = sourceFileCount;
        _identities = new Dictionary<string, int>(recordCount, StringComparer.Ordinal);

        Span<int> cursorOffsets = [28, 32, 36, 40];
        Span<int> tableCounts = [moduleRefCount, typeDefCount, methodDefCount, sourceFileCount];
        for (var row = 0; row < recordCount; row++)
        {
            if (Read(row, 4) > 1 || Read(row, 8) > 1
                || !ValidFeatureRange(Read(row, 16), Read(row, 20), featureCount)
                || Read(row, 24) != 0
                || !_identities.TryAdd(ReadIdentity(row), row + 1))
            {
                throw InvalidModule();
            }

            for (var table = 0; table < cursorOffsets.Length; table++)
            {
                var start = Read(row, cursorOffsets[table]);
                if (start == 0 || start > tableCounts[table] + 1
                    || (row == 0 && start != 1)
                    || (row > 0 && start < Read(row - 1, cursorOffsets[table])))
                {
                    throw InvalidModule();
                }
            }

            var entry = Read(row, 12);
            var methodStart = Read(row, 36);
            var methodEnd = row + 1 < recordCount
                ? Read(row + 1, 36) : (uint)methodDefCount + 1;
            if (entry != 0 && (entry < methodStart || entry >= methodEnd))
            {
                throw InvalidModule();
            }
        }
    }

    public string GetIdentity(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        return ReadIdentity(row - 1);
    }

    public (int Start, int End) GetModuleRefRange(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var start = (int)Read(row - 1, 28);
        var end = row < _recordCount ? (int)Read(row, 28) : _moduleRefCount + 1;
        if (start <= 0 || end < start || end > _moduleRefCount + 1)
        {
            throw InvalidModule();
        }

        return (start, end);
    }

    public (int Start, int End) GetSourceFileRange(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var start = (int)Read(row - 1, 40);
        var end = row < _recordCount ? (int)Read(row, 40) : _sourceFileCount + 1;
        if (start <= 0 || end < start || end > _sourceFileCount + 1)
        {
            throw InvalidModule();
        }

        return (start, end);
    }

    public (int Start, int End) GetTypeDefRange(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var start = (int)Read(row - 1, 32);
        var end = row < _recordCount ? (int)Read(row, 32) : _typeDefCount + 1;
        if (start <= 0 || end < start || end > _typeDefCount + 1)
        {
            throw InvalidModule();
        }

        return (start, end);
    }

    public (int Start, int End) GetMethodDefRange(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var start = (int)Read(row - 1, 36);
        var end = row < _recordCount ? (int)Read(row, 36) : _methodDefCount + 1;
        if (start <= 0 || end < start || end > _methodDefCount + 1)
        {
            throw InvalidModule();
        }

        return (start, end);
    }

    public (uint TargetRequirement, uint RuntimePolicy) GetRequirements(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var target = Read(row - 1, 4);
        var policy = Read(row - 1, 8);
        if (target > 1 || policy > 1)
        {
            throw InvalidModule();
        }

        return (target, policy);
    }

    /// <summary>Gets the one-based Feature row start and row count assigned to a module.</summary>
    /// <param name="row">The one-based Module row number.</param>
    public (int First, int Count) GetFeatureRange(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        return ((int)Read(row - 1, 16), (int)Read(row - 1, 20));
    }

    public bool TryFindModule(string identity, out int row)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Length == 0 || !IsValidIdentity(identity))
        {
            throw new ArgumentException("A module identity must be nonempty, NUL-free scalar text.", nameof(identity));
        }

        return _identities.TryGetValue(identity, out row);
    }

    private string ReadIdentity(int row)
    {
        var reference = Read(row, 0);
        if (reference == 0 || reference > 0xFF_FFFF)
        {
            throw InvalidModule();
        }

        return _strings.DecodeName((int)reference);
    }

    private uint Read(int row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice(row * RowSize + offset, sizeof(uint)));

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static bool ValidFeatureRange(uint first, uint count, int featureCount)
    {
        if (first == 0 && count == 0)
        {
            return true;
        }

        return first != 0 && count != 0
            && first <= 0xFF_FFFF && count <= 0xFF_FFFF
            && ((ulong)first + count - 1) <= 0xFF_FFFF
            && (featureCount < 0 || ((ulong)first + count) <= ((ulong)featureCount + 1));
    }

    private static bool IsValidIdentity(string value)
    {
        var position = 0;
        while (position < value.Length)
        {
            var unit = value[position];
            if (unit == '\0')
            {
                return false;
            }

            if (!char.IsSurrogate(unit))
            {
                position++;
                continue;
            }

            if (Rune.DecodeFromUtf16(value.AsSpan(position), out _, out var consumed) != OperationStatus.Done)
            {
                return false;
            }

            position += consumed;
        }

        return true;
    }

    private static InvalidDataException InvalidModule()
        => new("Invalid TRRM Module table.");
}
