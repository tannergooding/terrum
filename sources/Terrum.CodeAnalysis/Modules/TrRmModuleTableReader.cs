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
    internal const uint WindowsX64NativeTargetRequirement = 1;
    private const int RowSize = 48;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int _recordCount;
    private readonly int _moduleRefCount;
    private readonly int _typeDefCount;
    private readonly int _methodDefCount;
    private readonly int _sourceFileCount;
    private readonly Dictionary<string, int> _identities;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of Module rows.</summary>
    public int Count => _recordCount;
    /// <summary>Gets the number of ModuleRef rows in the artifact.</summary>
    public int ModuleRefCount => _moduleRefCount;
    /// <summary>Gets the number of TypeDef rows in the artifact.</summary>
    public int TypeDefCount => _typeDefCount;
    /// <summary>Gets the number of MethodDef rows in the artifact.</summary>
    public int MethodDefCount => _methodDefCount;
    /// <summary>Gets the number of SourceFile rows in the artifact.</summary>
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
        : this(section, recordCount, strings, moduleRefCount, typeDefCount,
            methodDefCount, sourceFileCount, featureCount, null)
    {
    }

    internal TrRmModuleTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, int moduleRefCount, int typeDefCount,
        int methodDefCount, int sourceFileCount, int featureCount,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(7);
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
        _resourceLedger = resourceLedger;
        _resourceLedger?.ConsumeWork(checked((ulong)recordCount
            + (ulong)recordCount));
        _identities = new Dictionary<string, int>(recordCount, StringComparer.Ordinal);

        Span<int> cursorOffsets = [32, 36, 40, 44];
        Span<int> tableCounts = [moduleRefCount, typeDefCount, methodDefCount, sourceFileCount];
        for (var row = 0; row < recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            if (Read(row, 4) == 0 || Read(row, 8) > 1 || Read(row, 12) > 1
                || Read(row, 20) != 0
                || !ValidFeatureRange(Read(row, 24), Read(row, 28), featureCount))
            {
                throw InvalidModule();
            }

            var identity = ReadIdentity(row);
            var identityBytes = checked((ulong)identity.Length * sizeof(char));
            _resourceLedger?.ConsumeWork(checked(identityBytes + 1UL));
            if (!IsValidIdentity(identity))
            {
                throw InvalidModule();
            }
            _resourceLedger?.ConsumeWork(checked(identityBytes + 2UL));
            if (!_identities.TryAdd(identity, row + 1))
            {
                throw InvalidModule();
            }

            for (var table = 0; table < cursorOffsets.Length; table++)
            {
                _resourceLedger?.ConsumeWork(4);
                var start = Read(row, cursorOffsets[table]);
                if (start == 0 || start > tableCounts[table] + 1
                    || (row == 0 && start != 1)
                    || (row > 0 && start < Read(row - 1, cursorOffsets[table])))
                {
                    throw InvalidModule();
                }
            }

            var entry = Read(row, 16);
            var methodStart = Read(row, 40);
            var methodEnd = row + 1 < recordCount
                ? Read(row + 1, 40) : (uint)methodDefCount + 1;
            _resourceLedger?.ConsumeWork(3);
            if (entry != 0 && (entry < methodStart || entry >= methodEnd))
            {
                throw InvalidModule();
            }
        }
    }

    /// <summary>Gets the producer identity selected by a Module row.</summary>
    /// <param name="row">The one-based Module row.</param>
    /// <returns>The decoded producer identity.</returns>
    public string GetIdentity(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        return ReadIdentity(row - 1);
    }

    /// <summary>Gets the module entry MethodDef row, or zero when absent.</summary>
    /// <param name="row">The one-based Module row.</param>
    /// <returns>The entry MethodDef row or zero.</returns>
    public int GetEntryMethod(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        return checked((int)Read(row - 1, 16));
    }

    /// <summary>Gets the one-based half-open ModuleRef row range owned by a Module.</summary>
    /// <param name="row">The one-based Module row.</param>
    public (int Start, int End) GetModuleRefRange(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var start = (int)Read(row - 1, 32);
        var end = row < _recordCount ? (int)Read(row, 32) : _moduleRefCount + 1;
        if (start <= 0 || end < start || end > _moduleRefCount + 1)
        {
            throw InvalidModule();
        }

        return (start, end);
    }

    /// <summary>Gets the one-based half-open SourceFile row range owned by a Module.</summary>
    /// <param name="row">The one-based Module row.</param>
    public (int Start, int End) GetSourceFileRange(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var start = (int)Read(row - 1, 44);
        var end = row < _recordCount ? (int)Read(row, 44) : _sourceFileCount + 1;
        if (start <= 0 || end < start || end > _sourceFileCount + 1)
        {
            throw InvalidModule();
        }

        return (start, end);
    }

    /// <summary>Gets the one-based half-open TypeDef row range owned by a Module.</summary>
    /// <param name="row">The one-based Module row.</param>
    public (int Start, int End) GetTypeDefRange(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var start = (int)Read(row - 1, 36);
        var end = row < _recordCount ? (int)Read(row, 36) : _typeDefCount + 1;
        if (start <= 0 || end < start || end > _typeDefCount + 1)
        {
            throw InvalidModule();
        }

        return (start, end);
    }

    /// <summary>Gets the one-based half-open MethodDef row range owned by a Module.</summary>
    /// <param name="row">The one-based Module row.</param>
    public (int Start, int End) GetMethodDefRange(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var start = (int)Read(row - 1, 40);
        var end = row < _recordCount ? (int)Read(row, 40) : _methodDefCount + 1;
        if (start <= 0 || end < start || end > _methodDefCount + 1)
        {
            throw InvalidModule();
        }

        return (start, end);
    }

    /// <summary>Gets the target and runtime requirements selected by a Module.</summary>
    /// <param name="row">The one-based Module row.</param>
    public (uint TargetRequirement, uint RuntimePolicy) GetRequirements(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        var target = Read(row - 1, 8);
        var policy = Read(row - 1, 12);
        if (target > 1 || policy > 1)
        {
            throw InvalidModule();
        }

        return (target, policy);
    }

    /// <summary>Gets the exact governing semantic edition selected by a module.</summary>
    public uint GetGoverningRuleEdition(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        return Read(row - 1, 4);
    }

    /// <summary>Gets the one-based Feature row start and row count assigned to a module.</summary>
    /// <param name="row">The one-based Module row number.</param>
    public (int First, int Count) GetFeatureRange(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidModule();
        }

        return ((int)Read(row - 1, 24), (int)Read(row - 1, 28));
    }

    /// <summary>Finds the Module row with the exact producer identity.</summary>
    /// <param name="identity">The producer identity to find.</param>
    /// <param name="row">Receives the one-based Module row when found.</param>
    /// <returns><see langword="true"/> when the identity is present.</returns>
    public bool TryFindModule(string identity, out int row)
    {
        _resourceLedger?.ConsumeWork();
        ArgumentNullException.ThrowIfNull(identity);
        _resourceLedger?.ConsumeWork(checked(((ulong)identity.Length
            * sizeof(char)) + 1UL));
        if (identity.Length == 0 || !IsValidIdentity(identity))
        {
            throw new ArgumentException("A module identity must be nonempty, NUL-free scalar text.", nameof(identity));
        }

        _resourceLedger?.ConsumeWork(checked(((ulong)identity.Length
            * sizeof(char)) + 1UL));
        return _identities.TryGetValue(identity, out row);
    }

    private string ReadIdentity(int row)
    {
        var reference = Read(row, 0);
        if (reference is 0 or > 0xFF_FFFF)
        {
            throw InvalidModule();
        }

        return _strings.DecodeName((int)reference);
    }

    private uint Read(int row, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            _rows.Slice((row * RowSize) + offset, sizeof(uint)));
    }

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
