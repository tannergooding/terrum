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
    private readonly int _sourceFileCount;
    private readonly Dictionary<string, int> _identities;

    public int Count => _recordCount;
    public int ModuleRefCount => _moduleRefCount;
    public int TypeDefCount => _typeDefCount;
    public int SourceFileCount => _sourceFileCount;

    public TrRmModuleTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, int moduleRefCount, int typeDefCount,
        int methodDefCount, int sourceFileCount)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || !ValidCount(moduleRefCount) || !ValidCount(typeDefCount)
            || !ValidCount(methodDefCount) || !ValidCount(sourceFileCount))
        {
            throw InvalidModule();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        _moduleRefCount = moduleRefCount;
        _typeDefCount = typeDefCount;
        _sourceFileCount = sourceFileCount;
        _identities = new Dictionary<string, int>(StringComparer.Ordinal);

        Span<int> cursorOffsets = [28, 32, 36, 40];
        Span<int> tableCounts = [moduleRefCount, typeDefCount, methodDefCount, sourceFileCount];
        for (var row = 0; row < recordCount; row++)
        {
            if (Read(row, 4) > 1 || Read(row, 8) > 1 || Read(row, 16) != 0
                || Read(row, 20) != 0 || Read(row, 24) != 0
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

    public bool TryFindModule(string identity, out int row)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Length == 0 || identity.Contains('\0') || !IsWellFormed(identity))
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

    private static bool IsWellFormed(string value)
    {
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done)
            {
                return false;
            }

            remaining = remaining[consumed..];
        }

        return true;
    }

    private static InvalidDataException InvalidModule()
        => new("Invalid TRRM Module table.");
}
