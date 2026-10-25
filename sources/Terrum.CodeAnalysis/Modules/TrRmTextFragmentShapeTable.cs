// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Exact text-operation declarations selected for one using Module.</summary>
/// <remarks>This in-memory caller context has no TRRM row or serialized profile identifier.</remarks>
/// <param name="ModuleRow">The one-based using Module row.</param>
/// <param name="FormatterInterfaceDefRow">The selected IFormattable InterfaceDef row.</param>
/// <param name="FormatterTryFormatMethodDefRow">The selected IFormattable.TryFormat MethodDef row.</param>
/// <param name="WriterInterfaceDefRow">The selected ITextWriter InterfaceDef row.</param>
/// <param name="WriterTryWriteMethodDefRow">The selected ITextWriter.TryWrite MethodDef row.</param>
/// <param name="WriterTryWriteScalarMethodDefRow">The selected ITextWriter.TryWriteScalar MethodDef row.</param>
/// <param name="WriterTryWriteFormattedMethodDefRow">The selected ITextWriter.TryWriteFormatted MethodDef row.</param>
/// <param name="TextSpanTypeSpecRef">The complete selected TextSpan TypeSpecRef.</param>
public readonly record struct TrRmTextConstructionProfile(
    int ModuleRow, int FormatterInterfaceDefRow, int FormatterTryFormatMethodDefRow,
    int WriterInterfaceDefRow, int WriterTryWriteMethodDefRow,
    int WriterTryWriteScalarMethodDefRow, int WriterTryWriteFormattedMethodDefRow,
    uint TextSpanTypeSpecRef);

/// <summary>A literal or formatted operation in a generated text shape.</summary>
/// <param name="ShapeRow">The one-based owning TextContentShape row.</param>
/// <param name="Ordinal">The zero-based fragment ordinal.</param>
/// <param name="Kind">The assigned literal or formatted fragment kind.</param>
/// <param name="ValueCaptureRow">The formatted value capture row, or zero for a literal.</param>
/// <param name="AlignmentCaptureRow">The formatted alignment capture row, or zero for a literal.</param>
/// <param name="FormatCaptureRow">The formatted text-format capture row, or zero for a literal.</param>
/// <param name="FormatterWitnessRow">The closed InterfaceWitness row, or zero.</param>
/// <param name="FormatterConstraintRow">The open type-formal Constraint row, or zero.</param>
/// <param name="FormatterMethodDefRow">The formatter MethodDef row, or zero for a literal.</param>
/// <param name="WriterRequirementMethodDefRow">The required writer MethodDef row.</param>
public readonly record struct TrRmTextFragmentShapeRow(
    int ShapeRow, uint Ordinal, byte Kind,
    uint ValueCaptureRow, uint AlignmentCaptureRow, uint FormatCaptureRow,
    uint FormatterWitnessRow, uint FormatterConstraintRow,
    uint FormatterMethodDefRow, uint WriterRequirementMethodDefRow);

/// <summary>Validates fragment ranges, capture ownership and typed method/evidence references.</summary>
public readonly ref struct TrRmTextFragmentShapeTableReader
{
    private const int RowSize = 40;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated TextFragmentShape rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates assigned literal/formatted fields and their selected references.</summary>
    /// <param name="section">The borrowed 40-byte TextFragmentShape rows.</param>
    /// <param name="recordCount">The number of TextFragmentShape rows.</param>
    /// <param name="shapes">The validated TextContentShape table.</param>
    /// <param name="captures">The validated TextCaptureShape table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="formals">The validated GenericParam table.</param>
    /// <param name="constraints">The validated Constraint table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="interfaceBases">The validated InterfaceBase table.</param>
    /// <param name="interfaceMethods">The validated InterfaceMethod table.</param>
    /// <param name="witnesses">The validated InterfaceWitness table.</param>
    /// <param name="witnessMembers">The validated WitnessMember table.</param>
    /// <param name="imports">The validated Import table.</param>
    public TrRmTextFragmentShapeTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTextContentShapeTableReader shapes,
        TrRmTextCaptureShapeTableReader captures,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmGenericParamTableReader formals, TrRmConstraintTableReader constraints,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmInterfaceMethodTableReader interfaceMethods,
        TrRmInterfaceWitnessTableReader witnesses,
        TrRmWitnessMemberTableReader witnessMembers,
        TrRmImportTableReader imports)
    {
        if (!ValidCount(recordCount)
            || recordCount != shapes.FragmentCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidTextFragmentShape();
        }

        _rows = section;
        _recordCount = recordCount;
        for (var shape = 1; shape <= shapes.Count; shape++)
        {
            var (first, count) = shapes.GetFragmentRange(shape);
            var generatedTypeDef = shapes.GetGeneratedTypeDef(shape);
            var generatedModule = types.GetOwnerModule(generatedTypeDef);
            var writerInterfaceTypeDef = 0;
            var formatterInterfaceTypeDef = 0;
            uint formatterMethodDef = 0;
            var formatterInterfaceClosure = new Dictionary<int, bool>();
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var row = first + ordinal;
                var data = GetRow(row);
                var ownerShape = Read(data, 0);
                var fragmentOrdinal = Read(data, 4);
                var kind = data[8];
                var flags = data[9];
                var reserved = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
                var valueCapture = Read(data, 12);
                var alignmentCapture = Read(data, 16);
                var formatCapture = Read(data, 20);
                var formatterWitness = Read(data, 24);
                var formatterConstraint = Read(data, 28);
                var formatterMethod = Read(data, 32);
                var writerRequirement = Read(data, 36);
                var hasFormatterWitness = formatterWitness != 0;
                var hasFormatterConstraint = formatterConstraint != 0;
                if (ownerShape != (uint)shape || fragmentOrdinal != (uint)ordinal
                    || kind is not (0 or 1) || flags != 0 || reserved != 0)
                {
                    throw InvalidTextFragmentShape();
                }

                if (!ValidMethodReference(writerRequirement, methods, generatedModule))
                {
                    throw InvalidTextFragmentShape();
                }

                var writerInterface = GetRequirementInterface(writerRequirement,
                    interfaces, interfaceMethods, methods);
                var currentWriterInterfaceTypeDef = interfaces.GetOwnerTypeDef(writerInterface);
                if (types.GetGenericRange(currentWriterInterfaceTypeDef).Count != 0
                    || (writerInterfaceTypeDef != 0
                        && writerInterfaceTypeDef != currentWriterInterfaceTypeDef))
                {
                    throw InvalidTextFragmentShape();
                }
                writerInterfaceTypeDef = currentWriterInterfaceTypeDef;

                if (kind == 0)
                {
                    if (valueCapture != 0 || alignmentCapture != 0 || formatCapture != 0
                        || formatterWitness != 0 || formatterConstraint != 0
                        || formatterMethod != 0)
                    {
                        throw InvalidTextFragmentShape();
                    }

                    continue;
                }

                if (valueCapture == 0 || alignmentCapture == 0 || formatCapture == 0
                    || valueCapture == alignmentCapture || valueCapture == formatCapture
                    || alignmentCapture == formatCapture
                    || hasFormatterWitness == hasFormatterConstraint
                    || formatterMethod == 0
                    || !ValidMethodReference(formatterMethod, methods, generatedModule)
                    || (formatterMethodDef != 0 && formatterMethodDef != formatterMethod))
                {
                    throw InvalidTextFragmentShape();
                }

                var formatterInterface = GetRequirementInterface(formatterMethod,
                    interfaces, interfaceMethods, methods);
                var currentFormatterInterfaceTypeDef = interfaces.GetOwnerTypeDef(formatterInterface);
                if (types.GetGenericRange(currentFormatterInterfaceTypeDef).Count != 0
                    || currentFormatterInterfaceTypeDef == writerInterfaceTypeDef
                    || (formatterInterfaceTypeDef != 0
                        && formatterInterfaceTypeDef != currentFormatterInterfaceTypeDef))
                {
                    throw InvalidTextFragmentShape();
                }
                formatterInterfaceTypeDef = currentFormatterInterfaceTypeDef;
                formatterMethodDef = formatterMethod;

                ValidateCapture(valueCapture, shape, captures);
                ValidateCapture(alignmentCapture, shape, captures);
                ValidateCapture(formatCapture, shape, captures);
                if (formatterWitness != 0)
                {
                    ValidateFormatterWitness(formatterWitness, valueCapture,
                        formatterMethod, formatterInterface, formatterInterfaceTypeDef,
                        captures, typeSpecs, types, interfaces, interfaceBases,
                        witnesses, witnessMembers, imports, formatterInterfaceClosure);
                }
                else
                {
                    var formal = ValidateFormatterConstraint(formatterConstraint,
                        generatedTypeDef, formals, constraints);
                    ValidateOpenFormatterEvidence(valueCapture, formal,
                        formatterInterfaceTypeDef, captures, typeSpecs, types,
                        interfaces, interfaceBases, imports, constraints,
                        formatterConstraint, formatterInterfaceClosure);
                }
            }
        }
    }

    /// <summary>Gets the one-based owning TextContentShape row.</summary>
    public int GetShapeRow(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the zero-based fragment ordinal.</summary>
    public uint GetOrdinal(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the assigned literal/formatted kind.</summary>
    public byte GetKind(int row)
        => GetRow(row)[8];

    /// <summary>Gets the one-based value capture, or zero for a literal.</summary>
    public uint GetValueCaptureRow(int row)
        => Read(GetRow(row), 12);

    /// <summary>Gets the one-based alignment capture, or zero for a literal.</summary>
    public uint GetAlignmentCaptureRow(int row)
        => Read(GetRow(row), 16);

    /// <summary>Gets the one-based format capture, or zero for a literal.</summary>
    public uint GetFormatCaptureRow(int row)
        => Read(GetRow(row), 20);

    /// <summary>Gets the one-based InterfaceWitness row, or zero.</summary>
    public uint GetFormatterWitnessRow(int row)
        => Read(GetRow(row), 24);

    /// <summary>Gets the one-based Constraint row, or zero.</summary>
    public uint GetFormatterConstraintRow(int row)
        => Read(GetRow(row), 28);

    /// <summary>Gets the formatter MethodDef row, or zero for a literal.</summary>
    public uint GetFormatterMethodDefRow(int row)
        => Read(GetRow(row), 32);

    /// <summary>Gets the required writer MethodDef row.</summary>
    public uint GetWriterRequirementMethodDefRow(int row)
        => Read(GetRow(row), 36);

    private static void ValidateCapture(uint capture, int shape,
        TrRmTextCaptureShapeTableReader captures)
    {
        if (!ValidRowReference(capture, captures.Count)
            || captures.GetShapeRow((int)capture) != shape)
        {
            throw InvalidTextFragmentShape();
        }
    }

    private static int ValidateFormatterConstraint(uint constraint,
        int generatedTypeDef, TrRmGenericParamTableReader formals,
        TrRmConstraintTableReader constraints)
    {
        if (!ValidRowReference(constraint, constraints.Count)
            || constraints.GetKind((int)constraint) != 1)
        {
            throw InvalidTextFragmentShape();
        }

        var formal = constraints.GetOwnerGenericParam((int)constraint);
        if (formals.GetFormalKind(formal) != 1
            || formals.GetOwnerDefId(formal) != (0x1300_0000u | (uint)generatedTypeDef))
        {
            throw InvalidTextFragmentShape();
        }

        return formal;
    }

    private static void ValidateFormatterWitness(uint witness, uint valueCapture,
        uint formatterMethod, int formatterInterface, int formatterInterfaceTypeDef,
        TrRmTextCaptureShapeTableReader captures, TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader types, TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmInterfaceWitnessTableReader witnesses,
        TrRmWitnessMemberTableReader witnessMembers,
        TrRmImportTableReader imports, Dictionary<int, bool> formatterInterfaceClosure)
    {
        if (!ValidRowReference(witness, witnesses.Count)
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                captures.GetTypeSpecRef(checked((int)valueCapture)),
                witnesses.GetImplementingTypeSpecRef(checked((int)witness)))
            || !SuppliesInterface(witnesses.GetInterfaceTypeSpecRef(checked((int)witness)),
                formatterInterfaceTypeDef, typeSpecs, types, interfaces,
                interfaceBases, imports, formatterInterfaceClosure))
        {
            throw InvalidTextFragmentShape();
        }

        var matchingMemberCount = 0;
        var (first, count) = witnesses.GetMemberRange(checked((int)witness));
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var member = first + ordinal;
            if (witnessMembers.GetRequirementMethodDef(member) != formatterMethod)
            {
                continue;
            }

            if (typeSpecs.GetNominalTypeDefRow(
                    witnessMembers.GetDeclaringInterfaceTypeSpecRef(member), types, imports)
                != interfaces.GetOwnerTypeDef(formatterInterface))
            {
                throw InvalidTextFragmentShape();
            }

            matchingMemberCount++;
        }

        if (matchingMemberCount != 1)
        {
            throw InvalidTextFragmentShape();
        }
    }

    private static void ValidateOpenFormatterEvidence(uint valueCapture, int formal,
        int formatterInterfaceTypeDef, TrRmTextCaptureShapeTableReader captures,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader types,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmImportTableReader imports, TrRmConstraintTableReader constraints,
        uint formatterConstraint, Dictionary<int, bool> formatterInterfaceClosure)
    {
        var captureType = typeSpecs.GetTypeSpecRef(
            captures.GetTypeSpecRef(checked((int)valueCapture)));
        if (!captureType.IsRow || captureType.Form != 0x04
            || captureType.Qualifiers != 0
            || Read(typeSpecs.GetRow(captureType.Row), 2) != (uint)formal
            || !SuppliesInterface(
                constraints.GetPayload(checked((int)formatterConstraint)),
                formatterInterfaceTypeDef, typeSpecs, types, interfaces,
                interfaceBases, imports, formatterInterfaceClosure))
        {
            throw InvalidTextFragmentShape();
        }
    }

    private static int GetRequirementInterface(uint method,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceMethodTableReader interfaceMethods,
        TrRmMethodDefTableReader methods)
    {
        var result = interfaceMethods.GetOwnerInterfaceDefForMethod(checked((int)method));
        if (result == 0 || methods.GetOwnerType(checked((int)method))
            != interfaces.GetOwnerTypeDef(result))
        {
            throw InvalidTextFragmentShape();
        }

        return result;
    }

    private static bool SuppliesInterface(uint interfaceTypeSpecRef,
        int requiredInterfaceTypeDef, TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader types, TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmImportTableReader imports, Dictionary<int, bool> cache)
    {
        var rootTypeDef = typeSpecs.GetNominalTypeDefRow(
            interfaceTypeSpecRef, types, imports);
        if (cache.TryGetValue(rootTypeDef, out var cachedResult))
        {
            return cachedResult;
        }

        var pending = new Stack<int>();
        var visited = new HashSet<int>();
        pending.Push(rootTypeDef);
        while (pending.TryPop(out var typeDef))
        {
            if (!visited.Add(typeDef))
            {
                continue;
            }

            if (typeDef == requiredInterfaceTypeDef)
            {
                cache[rootTypeDef] = true;
                return true;
            }

            var interfaceDef = interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (interfaceDef == 0)
            {
                throw InvalidTextFragmentShape();
            }

            var (first, count) = interfaces.GetBaseRange(interfaceDef);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                pending.Push(typeSpecs.GetNominalTypeDefRow(
                    interfaceBases.GetBaseTypeSpecRef(first + ordinal), types, imports));
            }
        }

        cache[rootTypeDef] = false;
        return false;
    }

    private static bool ValidMethodReference(uint method, TrRmMethodDefTableReader methods,
        int generatedModule)
        => ValidRowReference(method, methods.Count)
            && methods.GetOwnerModule((int)method) == generatedModule;

    private static bool ValidRowReference(uint row, int count)
        => row is > 0 and <= 0xFF_FFFF && row <= (uint)count;

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTextFragmentShape();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidTextFragmentShape()
        => new("Invalid TRRM TextFragmentShape table.");
}

/// <summary>Encodes literal and formatted fragments with checked typed references.</summary>
public static class TrRmTextFragmentShapeTableWriter
{
    private const int RowSize = 40;

    /// <summary>Encodes TextFragmentShape rows in one-based table order.</summary>
    /// <param name="rows">The ordered literal and formatted fragments.</param>
    /// <param name="shapes">The validated TextContentShape table.</param>
    /// <param name="captures">The validated TextCaptureShape table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="formals">The validated GenericParam table.</param>
    /// <param name="constraints">The validated Constraint table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="interfaceBases">The validated InterfaceBase table.</param>
    /// <param name="interfaceMethods">The validated InterfaceMethod table.</param>
    /// <param name="witnesses">The validated InterfaceWitness table.</param>
    /// <param name="witnessMembers">The validated WitnessMember table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <returns>The encoded TextFragmentShape section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmTextFragmentShapeRow> rows,
        TrRmTextContentShapeTableReader shapes, TrRmTextCaptureShapeTableReader captures,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmGenericParamTableReader formals, TrRmConstraintTableReader constraints,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmInterfaceMethodTableReader interfaceMethods,
        TrRmInterfaceWitnessTableReader witnesses,
        TrRmWitnessMemberTableReader witnessMembers,
        TrRmImportTableReader imports)
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
            Write(result, offset, unchecked((uint)row.ShapeRow));
            Write(result, offset + 4, row.Ordinal);
            result[offset + 8] = row.Kind;
            Write(result, offset + 12, row.ValueCaptureRow);
            Write(result, offset + 16, row.AlignmentCaptureRow);
            Write(result, offset + 20, row.FormatCaptureRow);
            Write(result, offset + 24, row.FormatterWitnessRow);
            Write(result, offset + 28, row.FormatterConstraintRow);
            Write(result, offset + 32, row.FormatterMethodDefRow);
            Write(result, offset + 36, row.WriterRequirementMethodDefRow);
        }

        _ = new TrRmTextFragmentShapeTableReader(result, rows.Count, shapes,
            captures, types, methods, formals, constraints, typeSpecs, interfaces,
            interfaceBases, interfaceMethods, witnesses, witnessMembers, imports);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
