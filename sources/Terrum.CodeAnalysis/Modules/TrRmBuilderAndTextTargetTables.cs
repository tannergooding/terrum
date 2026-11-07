// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A target-owned builder registration and its complete type recipe.</summary>
/// <param name="TargetTypeDefRow">The one-based registered target TypeDef row.</param>
/// <param name="SchemaTypeSpecRef">The complete selected builder-schema TypeSpecRef.</param>
/// <param name="RecipeTypeSpecRef">The complete registered recipe TypeSpecRef.</param>
public readonly record struct TrRmBuilderRegistrationRow(
    int TargetTypeDefRow, uint SchemaTypeSpecRef, uint RecipeTypeSpecRef);

/// <summary>Validates target-owned builder registrations and complete type recipes.</summary>
public readonly ref struct TrRmBuilderRegistrationTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated BuilderRegistration rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates target TypeDef rows and both required TypeSpecRefs.</summary>
    /// <param name="section">The borrowed 12-byte BuilderRegistration rows.</param>
    /// <param name="recordCount">The number of BuilderRegistration rows.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    public TrRmBuilderRegistrationTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeDefTableReader typeDefs,
        TrRmTypeSpecTableReader typeSpecs)
        : this(section, recordCount, typeDefs, typeSpecs, null)
    {
    }

    internal TrRmBuilderRegistrationTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeDefTableReader typeDefs,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (!ValidCount(recordCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidBuilderRegistration();
        }

        _rows = section;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var targetTypeDef = Read(data, 0);
            var schemaType = Read(data, 4);
            var recipeType = Read(data, 8);
            if (targetTypeDef is 0 or > 0xFF_FFFF
                || targetTypeDef > (uint)typeDefs.Count
                || schemaType == 0 || recipeType == 0)
            {
                throw InvalidBuilderRegistration();
            }

            _ = typeSpecs.GetTypeSpecRef(schemaType);
            _ = typeSpecs.GetTypeSpecRef(recipeType);
        }
    }

    /// <summary>Gets the one-based target TypeDef row.</summary>
    public int GetTargetTypeDef(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the complete builder-schema TypeSpecRef.</summary>
    public uint GetSchemaTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the complete recipe TypeSpecRef.</summary>
    public uint GetRecipeTypeSpecRef(int row)
        => Read(GetRow(row), 8);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidBuilderRegistration();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidBuilderRegistration()
        => new("Invalid TRRM BuilderRegistration table.");
}

/// <summary>Encodes target-owned builder registrations with checked type references.</summary>
public static class TrRmBuilderRegistrationTableWriter
{
    private const int RowSize = 12;

    /// <summary>Encodes BuilderRegistration rows in one-based table order.</summary>
    /// <param name="rows">The target-owned schema and recipe type references.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    /// <returns>The encoded BuilderRegistration section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmBuilderRegistrationRow> rows,
        TrRmTypeDefTableReader typeDefs, TrRmTypeSpecTableReader typeSpecs)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * RowSize;
            Write(result, offset, unchecked((uint)row.TargetTypeDefRow));
            Write(result, offset + 4, row.SchemaTypeSpecRef);
            Write(result, offset + 8, row.RecipeTypeSpecRef);
        }

        _ = new TrRmBuilderRegistrationTableReader(
            result, rows.Count, typeDefs, typeSpecs);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}

/// <summary>A retained TextTarget schema on one exact MethodDef parameter.</summary>
/// <param name="OwnerMethodDefRow">The one-based owning MethodDef row.</param>
/// <param name="SchemaTypeSpecRef">The complete resolved schema TypeSpecRef.</param>
/// <param name="ParameterOrdinal">The zero-based target parameter ordinal.</param>
public readonly record struct TrRmTextTargetRow(
    int OwnerMethodDefRow, uint SchemaTypeSpecRef, uint ParameterOrdinal);

/// <summary>Validates TextTarget method, parameter and schema references.</summary>
public readonly ref struct TrRmTextTargetTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated TextTarget rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates each TextTarget parameter ordinal and complete schema.</summary>
    /// <param name="section">The borrowed 12-byte TextTarget rows.</param>
    /// <param name="recordCount">The number of TextTarget rows.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    public TrRmTextTargetTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader types)
        : this(section, recordCount, methods, signatures, types, null)
    {
    }

    internal TrRmTextTargetTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader types,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (!ValidCount(recordCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidTextTarget();
        }

        _rows = section;
        _recordCount = recordCount;
        resourceLedger?.ConsumeWork(checked((ulong)recordCount + 1UL));
        var targets = new HashSet<(uint Method, uint Ordinal)>(recordCount);
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var method = Read(data, 0);
            var schemaType = Read(data, 4);
            var ordinal = Read(data, 8);
            _resourceLedger?.ConsumeWork(2);
            if (method is 0 or > 0xFF_FFFF || method > (uint)methods.Count
                || schemaType == 0
                || !targets.Add((method, ordinal)))
            {
                throw InvalidTextTarget();
            }

            _ = types.GetTypeSpecRef(schemaType);
            var signature = methods.GetSignature((int)method);
            var parameterCount = signatures.GetParamRange(signature).Count;
            if (ordinal >= (uint)parameterCount)
            {
                throw InvalidTextTarget();
            }
        }
    }

    /// <summary>Gets the one-based owning MethodDef row.</summary>
    public int GetOwnerMethodDef(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the complete TextTarget schema TypeSpecRef.</summary>
    public uint GetSchemaTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the zero-based target parameter ordinal.</summary>
    public uint GetParameterOrdinal(int row)
        => Read(GetRow(row), 8);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTextTarget();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidTextTarget()
        => new("Invalid TRRM TextTarget table.");
}

/// <summary>Encodes TextTarget records with checked MethodDef parameter references.</summary>
public static class TrRmTextTargetTableWriter
{
    private const int RowSize = 12;

    /// <summary>Encodes TextTarget rows in one-based table order.</summary>
    /// <param name="rows">The retained TextTarget parameter schemas.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <returns>The encoded TextTarget section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmTextTargetRow> rows,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmTypeSpecTableReader types)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * RowSize;
            Write(result, offset, unchecked((uint)row.OwnerMethodDefRow));
            Write(result, offset + 4, row.SchemaTypeSpecRef);
            Write(result, offset + 8, row.ParameterOrdinal);
        }

        _ = new TrRmTextTargetTableReader(
            result, rows.Count, methods, signatures, types);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
