// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmFixedArrayLayoutValidator
{
    internal static void Validate(
        TrRmFixedArrayLayoutTableReader arrayLayouts,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmFieldDefTableReader fields,
        TrRmLayoutTableReader layouts,
        TrRmLayoutFieldTableReader layoutFields,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmImportTableReader imports,
        TrRmContractTableReader contracts,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? a3ResourceLedger = null)
    {
        if (!targetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            if (arrayLayouts.Count != 0)
            {
                throw InvalidLayout();
            }

            return;
        }

        var resourceLedger = a3ResourceLedger;
        var validator = new ValidationContext(arrayLayouts, typeSpecs,
            typeDefs, fields, layouts, layoutFields, profileRow,
            imports, contracts, genericParams, constraints, schema,
            resourceLedger);
        try
        {
            validator.Validate();
        }
        catch (TrRmResourceLimitException)
        {
            throw;
        }
        catch (OverflowException exception)
        {
            if (resourceLedger is not null
                && TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
                    out var kind)
                && kind == TrRmA3AdmissionFailureKind.TargetLayoutOverflow)
            {
                throw;
            }

            var invalid = InvalidLayout(exception);
            resourceLedger?.AdmissionScope.Mark(invalid,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw invalid;
        }
        catch (InvalidDataException exception) when (resourceLedger is not null)
        {
            resourceLedger.AdmissionScope.Mark(exception,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw;
        }
        catch (NotSupportedException exception) when (resourceLedger is not null)
        {
            resourceLedger.AdmissionScope.Mark(exception,
                TrRmA3AdmissionFailureKind.UnsupportedAdmission);
            throw;
        }
    }

    internal static (ulong Size, ulong Alignment, ulong RequiredAlignment)
        CalculateTypeDefLayout(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int typeDefRow)
    {
        if (!tables.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw InvalidLayout();
        }

        var validator = new ValidationContext(tables.FixedArrayLayouts,
            tables.TypeSpecs, tables.TypeDefs, tables.Fields, tables.Layouts,
            tables.LayoutFields, profileRow, tables.Imports, tables.Contracts,
            tables.GenericParams, tables.Constraints, tables.Schema,
            tables.A3LayoutResourceLedger);
        try
        {
            return validator.CalculateTypeDefLayoutForProvider(typeDefRow);
        }
        catch (TrRmResourceLimitException)
        {
            throw;
        }
        catch (OverflowException exception)
        {
            var resourceLedger = tables.A3LayoutResourceLedger;
            if (resourceLedger is not null
                && TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
                    out var kind)
                && kind == TrRmA3AdmissionFailureKind.TargetLayoutOverflow)
            {
                throw;
            }

            var invalid = InvalidLayout(exception);
            resourceLedger?.AdmissionScope.Mark(invalid,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw invalid;
        }
        catch (InvalidDataException exception)
            when (tables.A3LayoutResourceLedger is not null)
        {
            tables.A3LayoutResourceLedger.AdmissionScope.Mark(exception,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw;
        }
        catch (NotSupportedException exception)
            when (tables.A3LayoutResourceLedger is not null)
        {
            tables.A3LayoutResourceLedger.AdmissionScope.Mark(exception,
                TrRmA3AdmissionFailureKind.UnsupportedAdmission);
            throw;
        }
    }

    private static ulong RoundUp(ulong value, ulong alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw InvalidLayout();
        }

        return checked((value + alignment - 1) & ~(alignment - 1));
    }

    private static TypeLayout GetAtomicLayout(byte code) => code switch
    {
        0x00 => new TypeLayout(0, 0, 1),
        0x01 or 0x02 or 0x06 or 0x0B
            => new TypeLayout(1, 1, 1),
        0x03 or 0x07 or 0x12 or 0x13
            => new TypeLayout(2, 2, 1),
        0x04 or 0x05 or 0x08 or 0x0D or 0x14 or 0x17
            => new TypeLayout(4, 4, 1),
        0x09 or 0x0E or 0x10 or 0x11 or 0x15 or 0x18
            => new TypeLayout(8, 8, 1),
        0x0A or 0x0F or 0x16 or 0x19
            => new TypeLayout(16, 16, 1),
        _ => throw InvalidLayout(),
    };

    private static InvalidDataException InvalidLayout(Exception? inner = null)
        => new("Invalid TRRM FixedArrayLayout element or target geometry.",
            inner);

    private readonly record struct ArrayLayout(
        ulong Stride, ulong Extent, ulong Alignment,
        ulong RequiredAlignment);

    private readonly record struct TypeLayout(
        ulong Size, ulong Alignment, ulong RequiredAlignment);

    private readonly record struct GenericActualFacts(
        bool CopyCapable, bool Unique, bool Nullable, bool Blittable);

    private readonly record struct TypeSpecContext(
        uint Reference, int OwnerModule,
        TypeSubstitutionContext? Substitutions);

    private sealed class GenericVisitBudget(int remaining)
    {
        private int _remaining = remaining;

        internal void Consume()
        {
            if (--_remaining < 0)
            {
                throw InvalidLayout();
            }
        }
    }

    private sealed class TypeSubstitutionContext(
        Dictionary<int, TypeSpecContext> arguments,
        TypeSubstitutionContext? parent)
    {
        private readonly Dictionary<int, TypeSpecContext> _arguments = arguments;
        private readonly TypeSubstitutionContext? _parent = parent;

        internal bool TryGet(int formalRow, out TypeSpecContext argument)
        {
            for (var current = this; current is not null;
                current = current._parent)
            {
                if (current._arguments.TryGetValue(formalRow,
                        out argument))
                {
                    return true;
                }
            }

            argument = default;
            return false;
        }
    }

    private readonly ref struct ValidationContext
    {
        private readonly TrRmFixedArrayLayoutTableReader _arrayLayouts;
        private readonly TrRmTypeSpecTableReader _typeSpecs;
        private readonly TrRmTypeDefTableReader _typeDefs;
        private readonly TrRmFieldDefTableReader _fields;
        private readonly TrRmLayoutTableReader _layouts;
        private readonly TrRmLayoutFieldTableReader _layoutFields;
        private readonly int _profileRow;
        private readonly TrRmImportTableReader _imports;
        private readonly TrRmContractTableReader _contracts;
        private readonly TrRmGenericParamTableReader _genericParams;
        private readonly TrRmConstraintTableReader _constraints;
        private readonly TrRmArtifactSchema _schema;
        private readonly TrRmA3LayoutResourceLedger? _a3ResourceLedger;
        private readonly Dictionary<(int Module, uint TypeSpecRef), TypeLayout>
            _typeCache;
        private readonly HashSet<(int Module, int TypeDef)> _visitingTypeDefs;
        private readonly HashSet<(int Module, int TypeSpecRow)> _visitingArrays;
        private readonly HashSet<string> _activeGenericActuals;
        private readonly GenericVisitBudget _genericVisitBudget;

        internal ValidationContext(
            TrRmFixedArrayLayoutTableReader arrayLayouts,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmTypeDefTableReader typeDefs,
            TrRmFieldDefTableReader fields,
            TrRmLayoutTableReader layouts,
            TrRmLayoutFieldTableReader layoutFields,
            int profileRow,
            TrRmImportTableReader imports,
            TrRmContractTableReader contracts,
            TrRmGenericParamTableReader genericParams,
            TrRmConstraintTableReader constraints,
            TrRmArtifactSchema schema,
            TrRmA3LayoutResourceLedger? a3ResourceLedger)
        {
            _arrayLayouts = arrayLayouts;
            _typeSpecs = typeSpecs;
            _typeDefs = typeDefs;
            _fields = fields;
            _layouts = layouts;
            _layoutFields = layoutFields;
            _profileRow = profileRow;
            _imports = imports;
            _contracts = contracts;
            _genericParams = genericParams;
            _constraints = constraints;
            _schema = schema;
            _a3ResourceLedger = a3ResourceLedger;
            _typeCache = [];
            _visitingTypeDefs = [];
            _visitingArrays = [];
            _activeGenericActuals = [];
            _genericVisitBudget = new GenericVisitBudget(
                SemanticVerifier.MaximumAnalysisVisits);
        }

        internal void Validate()
        {
            for (var row = 1; row <= _arrayLayouts.Count; row++)
            {
                ConsumeWork();
                var layout = _arrayLayouts.GetRow(row);
                var actual = CalculateArrayLayout(layout.OwnerModuleRow,
                    layout.ArrayTypeSpecRow);
                if (actual.Stride != layout.ElementStride
                    || actual.Extent != layout.ByteExtent
                    || actual.Alignment != layout.Alignment)
                {
                    throw InvalidLayout();
                }
            }
        }

        private ArrayLayout CalculateArrayLayout(int ownerModule,
            int arrayTypeSpecRow)
        {
            using var frame = EnterFrame();
            ConsumeWork();
            var key = (ownerModule, arrayTypeSpecRow);
            if (!_visitingArrays.Add(key))
            {
                throw InvalidLayout();
            }

            try
            {
                if (!_typeSpecs.TryGetFixedInlineArray(
                        (uint)arrayTypeSpecRow, out var elementTypeSpecRef,
                        out var elementCount))
                {
                    throw InvalidLayout();
                }
                if (ContainsFormalReference(elementTypeSpecRef))
                {
                    throw UnsupportedLayout();
                }

                var element = CalculateTypeLayout(elementTypeSpecRef,
                    ownerModule);
                var alignment = Math.Max(1UL, element.Alignment);
                var stride = RoundUpTargetLayout(element.Size, alignment);
                var extent = MultiplyTargetLayout(elementCount, stride);
                return new ArrayLayout(stride, extent, alignment,
                    element.RequiredAlignment);
            }
            finally
            {
                _ = _visitingArrays.Remove(key);
            }
        }

        private TypeLayout CalculateTypeLayout(uint typeSpecRef,
            int ownerModule)
            => CalculateTypeLayout(new TypeSpecContext(typeSpecRef,
                ownerModule, null));

        private TypeLayout CalculateTypeLayout(TypeSpecContext input)
        {
            using var frame = EnterFrame();
            ConsumeWork();
            var context = ResolveTypeSpecContext(input);
            if (context.Substitutions is null
                && _typeCache.TryGetValue((context.OwnerModule,
                    context.Reference),
                    out var cached))
            {
                return cached;
            }

            var reference = _typeSpecs.GetTypeSpecRef(context.Reference);
            TypeLayout result;
            if (!reference.IsRow)
            {
                result = GetInlineLayout(reference);
            }
            else
            {
                var row = _typeSpecs.GetRow(reference.Row);
                result = reference.Form switch
                {
                    0x01 => GetAtomicLayout(row[2]),
                    0x02 or 0x03 => CalculateNonGenericTypeDefLayout(
                        context.Reference),
                    0x05 when _schema
                        == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                        => CalculateConstructedTypeLayout(context),
                    0x0C when reference.TypeCode == 0x8C
                        && (reference.Qualifiers & ~1) == 0
                        => new TypeLayout(sizeof(ulong), sizeof(ulong), 1),
                    0x0D => new TypeLayout(sizeof(ulong), sizeof(ulong), 1),
                    0x08 when (reference.Qualifiers & ~1) == 0
                        => new TypeLayout(sizeof(ulong), sizeof(ulong), 1),
                    0x0A when _schema
                        == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                        => CalculateTypeLayout(new TypeSpecContext(
                            Read(row, 2), context.OwnerModule,
                            context.Substitutions)),
                    0x11 => CalculateArrayElementLayout(reference.Row,
                        context.OwnerModule, context.Reference),
                    _ => throw UnsupportedLayout(),
                };
            }

            if (context.Substitutions is null)
            {
                _typeCache.Add((context.OwnerModule, context.Reference),
                    result);
            }
            return result;
        }

        private TypeSpecContext ResolveTypeSpecContext(TypeSpecContext context)
        {
            var remaining = _genericParams.Count + 1;
            while (true)
            {
                ConsumeWork();
                var reference = _typeSpecs.GetTypeSpecRef(context.Reference);
                if (reference.Form != 0x04)
                {
                    return context;
                }

                if (reference.Qualifiers != 0 || remaining-- == 0
                    || context.Substitutions is null
                    || !context.Substitutions.TryGet(
                        checked((int)Read(_typeSpecs.GetRow(reference.Row), 2)),
                        out context))
                {
                    throw InvalidLayout();
                }
            }
        }

        private TypeLayout CalculateConstructedTypeLayout(
            TypeSpecContext context)
        {
            using var frame = EnterFrame();
            ConsumeWork();
            var reference = _typeSpecs.GetTypeSpecRef(context.Reference);
            if (!reference.IsRow || reference.Form != 0x05
                || reference.Qualifiers != 0)
            {
                throw InvalidLayout();
            }

            var typeDef = _typeSpecs.GetNominalTypeDefRow(context.Reference,
                _typeDefs, _imports);
            var (firstFormal, formalCount) = _typeDefs.GetGenericRange(typeDef);
            var argumentCount
                = _typeSpecs.GetConstructedArgumentCount(reference.Row);
            if (formalCount == 0 || argumentCount != formalCount)
            {
                throw InvalidLayout();
            }

            var substitutions = CreateTypeSubstitution(context, typeDef,
                firstFormal, formalCount);
            var key = CreateConstructedTypeLayoutIdentity(context.OwnerModule,
                typeDef, firstFormal, formalCount, substitutions);
            if (!_activeGenericActuals.Add(key))
            {
                throw InvalidLayout();
            }
            try
            {
                return CalculateTypeDefLayout(typeDef, substitutions);
            }
            finally
            {
                _ = _activeGenericActuals.Remove(key);
            }
        }

        private string CreateConstructedTypeLayoutIdentity(int ownerModule,
            int typeDef, int firstFormal, int formalCount,
            TypeSubstitutionContext substitutions)
        {
            using var frame = EnterFrame();
            ConsumeWork();
            ConsumeWork();
            var builder = new StringBuilder(0);
            AppendText(builder, "S:");
            AppendDecimal(builder, (int)_schema);
            AppendText(builder, ";P:");
            AppendDecimal(builder, _profileRow);
            AppendText(builder, ";M:");
            AppendDecimal(builder, ownerModule);
            AppendText(builder, ";D:");
            _a3ResourceLedger?.ConsumeWork(sizeof(uint) + 1);
            AppendDecimal(builder, _typeDefs.GetOwnerModule(typeDef));
            AppendChar(builder, ':');
            AppendDecimal(builder, typeDef);
            AppendChar(builder, ';');
            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                ConsumeWork();
                if (!substitutions.TryGet(firstFormal + ordinal,
                        out var argument))
                {
                    throw InvalidLayout();
                }

                AppendChar(builder, 'A');
                AppendDecimal(builder, ordinal);
                AppendChar(builder, ':');
                AppendTypeLayoutIdentity(builder, argument);
                AppendChar(builder, ';');
            }

            _a3ResourceLedger?.ConsumeWork(
                checked(((ulong)builder.Length * sizeof(char)) + 1));
            return builder.ToString();
        }

        private void AppendTypeLayoutIdentity(StringBuilder builder,
            TypeSpecContext input, bool allowInitQualifier = false)
        {
            using var frame = EnterFrame();
            ConsumeGenericVisit();
            var reference = _typeSpecs.GetTypeSpecRef(input.Reference,
                allowInitQualifier: allowInitQualifier);
            if (reference.Form == 0x04)
            {
                if (reference.Qualifiers != 0)
                {
                    if (!allowInitQualifier || reference.Qualifiers != 0x04
                        || input.Substitutions is null
                        || !input.Substitutions.TryGet(
                            checked((int)Read(
                                _typeSpecs.GetRow(reference.Row), 2)),
                            out var initializedArgument))
                    {
                        throw InvalidLayout();
                    }

                    AppendText(builder, "init(");
                    AppendTypeLayoutIdentity(builder, initializedArgument);
                    AppendChar(builder, ')');
                    return;
                }

                input = ResolveTypeSpecContext(input);
                reference = _typeSpecs.GetTypeSpecRef(input.Reference,
                    allowInitQualifier: allowInitQualifier);
            }

            AppendChar(builder, 'M');
            AppendDecimal(builder, input.OwnerModule);
            AppendChar(builder, '/');
            if (!reference.IsRow)
            {
                AppendChar(builder, 'I');
                AppendHex(builder, reference.Value, "X8", 8);
                return;
            }

            AppendChar(builder, 'F');
            AppendHex(builder, reference.Form, "X2", 2);
            AppendChar(builder, 'Q');
            AppendHex(builder, reference.Qualifiers, "X2", 2);
            var row = _typeSpecs.GetRow(reference.Row);
            switch (reference.Form)
            {
                case 0x01:
                    AppendChar(builder, 'T');
                    AppendHex(builder, reference.TypeCode, "X2", 2);
                    AppendChar(builder, 'A');
                    AppendHex(builder, reference.ArgumentCode, "X2", 2);
                    return;

                case 0x02:
                case 0x03:
                    var typeDef = _typeSpecs.GetNominalTypeDefRow(
                        input.Reference, _typeDefs, _imports);
                    AppendNominalTypeLayoutIdentity(builder, typeDef,
                        reference.Form, Read(row, 2));
                    return;

                case 0x05:
                    var genericTypeSpecRef = Read(row, 2);
                    AppendTypeLayoutIdentity(builder,
                        new TypeSpecContext(genericTypeSpecRef,
                            input.OwnerModule, input.Substitutions));
                    var argumentCount
                        = _typeSpecs.GetConstructedArgumentCount(reference.Row);
                    AppendChar(builder, 'K');
                    AppendDecimal(builder, argumentCount);
                    for (var ordinal = 0; ordinal < argumentCount; ordinal++)
                    {
                        AppendChar(builder, '[');
                        AppendTypeLayoutIdentity(builder,
                            new TypeSpecContext(
                                _typeSpecs.GetConstructedArgument(
                                    reference.Row, ordinal),
                                input.OwnerModule, input.Substitutions));
                        AppendChar(builder, ']');
                    }
                    return;

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    AppendTypeLayoutIdentity(builder,
                        new TypeSpecContext(Read(row, 2), input.OwnerModule,
                            input.Substitutions));
                    return;

                case 0x0C:
                    AppendChar(builder, 'T');
                    AppendHex(builder, reference.TypeCode, "X2", 2);
                    AppendChar(builder, 'A');
                    AppendHex(builder, reference.ArgumentCode, "X2", 2);
                    AppendTypeLayoutIdentity(builder,
                        new TypeSpecContext(Read(row, 3), input.OwnerModule,
                            input.Substitutions),
                        allowInitQualifier: reference.TypeCode == 0x8A);
                    return;

                case 0x0D:
                    AppendHexBytes(builder, row);
                    return;

                case 0x11:
                    AppendChar(builder, 'N');
                    _a3ResourceLedger?.ConsumeWork(sizeof(ulong) + 1);
                    AppendDecimal(builder,
                        BinaryPrimitives.ReadUInt64LittleEndian(row[6..]));
                    AppendChar(builder, 'E');
                    AppendTypeLayoutIdentity(builder,
                        new TypeSpecContext(Read(row, 2), input.OwnerModule,
                            input.Substitutions));
                    return;

                default:
                    throw InvalidLayout();
            }
        }

        private void AppendNominalTypeLayoutIdentity(StringBuilder builder,
            int typeDef, byte form, uint reference)
        {
            AppendChar(builder, 'N');
            AppendHex(builder, form, "X2", 2);
            AppendChar(builder, 'R');
            AppendHex(builder, reference, "X8", 8);
            AppendChar(builder, 'D');
            _a3ResourceLedger?.ConsumeWork(sizeof(uint) + 1);
            AppendDecimal(builder, _typeDefs.GetOwnerModule(typeDef));
            AppendChar(builder, ':');
            AppendDecimal(builder, typeDef);
        }

        private void AppendText(StringBuilder builder, string value)
        {
            var payloadBytes = checked((ulong)value.Length * sizeof(char));
            _a3ResourceLedger?.ConsumeWork(
                checked(1UL + payloadBytes));
            _ = builder.Append(value);
        }

        private void AppendChar(StringBuilder builder, char value)
        {
            _a3ResourceLedger?.ConsumeWork(1UL + sizeof(char));
            _ = builder.Append(value);
        }

        private void AppendDecimal(StringBuilder builder, int value)
        {
            var length = GetDecimalLength(value);
            var payloadBytes = checked(length * (ulong)sizeof(char) * 2UL);
            _a3ResourceLedger?.ConsumeWork(checked(1UL + payloadBytes));
            _ = builder.Append(value.ToString(
                CultureInfo.InvariantCulture));
        }

        private void AppendDecimal(StringBuilder builder, uint value)
        {
            var length = GetDecimalLength(value);
            var payloadBytes = checked(length * (ulong)sizeof(char) * 2UL);
            _a3ResourceLedger?.ConsumeWork(checked(1UL + payloadBytes));
            _ = builder.Append(value.ToString(
                CultureInfo.InvariantCulture));
        }

        private void AppendDecimal(StringBuilder builder, ulong value)
        {
            var length = GetDecimalLength(value);
            var payloadBytes = checked(length * (ulong)sizeof(char) * 2UL);
            _a3ResourceLedger?.ConsumeWork(checked(1UL + payloadBytes));
            _ = builder.Append(value.ToString(
                CultureInfo.InvariantCulture));
        }

        private void AppendHex(StringBuilder builder, uint value,
            string format, int width)
        {
            var payloadBytes = checked((ulong)width * sizeof(char) * 2UL);
            _a3ResourceLedger?.ConsumeWork(checked(1UL + payloadBytes));
            _ = builder.Append(value.ToString(format,
                CultureInfo.InvariantCulture));
        }

        private void AppendHex(StringBuilder builder, byte value,
            string format, int width)
        {
            var payloadBytes = checked((ulong)width * sizeof(char) * 2UL);
            _a3ResourceLedger?.ConsumeWork(checked(1UL + payloadBytes));
            _ = builder.Append(value.ToString(format,
                CultureInfo.InvariantCulture));
        }

        private void AppendHexBytes(StringBuilder builder,
            ReadOnlySpan<byte> bytes)
        {
            var perByteCharge = 1UL + 1UL;
            perByteCharge += (ulong)sizeof(char) * 2UL;
            _a3ResourceLedger?.ConsumeWork(
                checked((ulong)bytes.Length * perByteCharge));
            const string HexDigits = "0123456789ABCDEF";
            foreach (var value in bytes)
            {
                _ = builder.Append(HexDigits[value >> 4]);
                _ = builder.Append(HexDigits[value & 0x0F]);
            }
        }

        private static ulong GetDecimalLength(int value)
        {
            var length = value < 0 ? 1UL : 0UL;
            var magnitude = value < 0
                ? (uint)-(long)value : (uint)value;
            do
            {
                length++;
                magnitude /= 10;
            } while (magnitude != 0);

            return length;
        }

        private static ulong GetDecimalLength(uint value)
        {
            var length = 1UL;
            while (value >= 10)
            {
                value /= 10;
                length++;
            }

            return length;
        }

        private static ulong GetDecimalLength(ulong value)
        {
            var length = 1UL;
            while (value >= 10)
            {
                value /= 10;
                length++;
            }

            return length;
        }

        private TypeSubstitutionContext CreateTypeSubstitution(
            TypeSpecContext context, int typeDef, int firstFormal,
            int formalCount)
        {
            using var frame = EnterFrame();
            ConsumeWork();
            var reference = _typeSpecs.GetTypeSpecRef(context.Reference);
            var argumentCount
                = _typeSpecs.GetConstructedArgumentCount(reference.Row);
            if (formalCount == 0 || argumentCount != formalCount)
            {
                throw InvalidLayout();
            }

            var arguments = new Dictionary<int, TypeSpecContext>(formalCount);
            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                ConsumeGenericVisit();
                var formal = firstFormal + ordinal;
                if (_genericParams.GetFormalKind(formal) != 1)
                {
                    throw InvalidLayout();
                }

                var argument = _typeSpecs.GetConstructedArgument(
                    reference.Row, ordinal);
                var actual = ResolveTypeSpecContext(new TypeSpecContext(
                    argument, context.OwnerModule,
                    context.Substitutions));
                ValidateGenericActual(formal, actual);
                arguments.Add(formal, actual);
            }

            return new TypeSubstitutionContext(arguments,
                context.Substitutions);
        }

        private void ValidateGenericActual(int formal,
            TypeSpecContext actual)
        {
            using var frame = EnterFrame();
            ConsumeGenericVisit();
            var facts = GetGenericActualFacts(actual);
            var ownershipDomain = _genericParams.GetOwnershipDomain(formal);
            var nullabilityDomain = _genericParams.GetNullabilityDomain(formal);
            if ((nullabilityDomain == 1 && facts.Nullable)
                || (ownershipDomain == 1
                    && (facts.Unique || !facts.CopyCapable))
                || (ownershipDomain == 2 && !facts.Unique)
                || (ownershipDomain == 3
                    && (facts.Unique == facts.CopyCapable)))
            {
                throw InvalidLayout();
            }

            var (firstConstraint, constraintCount)
                = _genericParams.GetConstraintRange(formal);
            for (var offset = 0; offset < constraintCount; offset++)
            {
                var constraint = firstConstraint + offset;
                if (_constraints.GetKind(constraint) != 2
                    || _constraints.GetPayload(constraint) != 3
                    || !facts.Blittable)
                {
                    throw InvalidLayout();
                }
            }
        }

        private GenericActualFacts GetGenericActualFacts(
            TypeSpecContext input)
        {
            using var frame = EnterFrame();
            ConsumeGenericVisit();
            ValidateGenericActualTypeQualifications(input);
            var context = ResolveTypeSpecContext(input);
            var reference = _typeSpecs.GetTypeSpecRef(context.Reference);
            switch (reference.Form)
            {
                case 0x01:
                    if (reference.TypeCode >= 0x80)
                    {
                        return GetGenericFamilyFacts(reference.TypeCode,
                            reference.ArgumentCode, reference.Qualifiers,
                            context, indexed: false);
                    }

                    _ = GetAtomicLayout(reference.TypeCode);
                    return new GenericActualFacts(true, false, false, true);

                case 0x02:
                case 0x03:
                    var typeDef = _typeSpecs.GetNominalTypeDefRow(
                        context.Reference, _typeDefs, _imports);
                    if (_typeDefs.GetGenericRange(typeDef).Count != 0)
                    {
                        throw UnsupportedLayout();
                    }

                    return GetTypeDefFacts(typeDef,
                        reference.Qualifiers);

                case 0x05:
                    return GetConstructedTypeFacts(context, reference);

                case 0x06:
                    var inner = GetGenericActualFacts(new TypeSpecContext(
                        Read(_typeSpecs.GetRow(reference.Row), 2),
                        context.OwnerModule, context.Substitutions));
                    return new GenericActualFacts(inner.CopyCapable,
                        inner.Unique, true, false);

                case 0x07:
                    _ = _typeSpecs.GetTypeSpecRef(
                        Read(_typeSpecs.GetRow(reference.Row), 2));
                    return new GenericActualFacts(true, false, false, false);

                case 0x08:
                    _ = _typeSpecs.GetTypeSpecRef(
                        Read(_typeSpecs.GetRow(reference.Row), 2));
                    return new GenericActualFacts(true, false, false, true);

                case 0x09:
                    _ = _typeSpecs.GetTypeSpecRef(
                        Read(_typeSpecs.GetRow(reference.Row), 2));
                    return new GenericActualFacts(true, false, false, false);

                case 0x0A:
                    return GetGenericActualFacts(new TypeSpecContext(
                        Read(_typeSpecs.GetRow(reference.Row), 2),
                        context.OwnerModule, context.Substitutions));

                case 0x0B:
                    _ = _typeSpecs.GetTypeSpecRef(
                        Read(_typeSpecs.GetRow(reference.Row), 2));
                    return new GenericActualFacts(false, true, false, false);

                case 0x0C:
                    return GetGenericFamilyFacts(reference.TypeCode, 0,
                        reference.Qualifiers, context, indexed: true);

                case 0x0D:
                    return new GenericActualFacts(true, false, false, true);

                case 0x11:
                    _ = CalculateArrayLayout(context.OwnerModule,
                        reference.Row);
                    return new GenericActualFacts(true, false, false, true);

                default:
                    throw UnsupportedLayout();
            }
        }

        private void ValidateGenericActualTypeQualifications(
            TypeSpecContext input, bool allowInitQualifier = false)
        {
            using var frame = EnterFrame();
            ConsumeGenericVisit();
            var reference = _typeSpecs.GetTypeSpecRef(input.Reference,
                allowInitQualifier: allowInitQualifier);
            var qualifiers = reference.Qualifiers;
            var initQualified = (qualifiers & 0x04) != 0;
            if (initQualified && !allowInitQualifier)
            {
                throw InvalidLayout();
            }

            qualifiers &= unchecked((byte)~0x04);
            if (reference.Form == 0x04)
            {
                if (qualifiers != 0)
                {
                    throw InvalidLayout();
                }

                var formal = checked((int)Read(
                    _typeSpecs.GetRow(reference.Row), 2));
                if (initQualified)
                {
                    if (input.Substitutions is null
                        || !input.Substitutions.TryGet(formal,
                            out var argument))
                    {
                        throw InvalidLayout();
                    }

                    ValidateGenericActualTypeQualifications(argument);
                    return;
                }

                ValidateGenericActualTypeQualifications(
                    ResolveTypeSpecContext(input));
                return;
            }

            switch (reference.Form)
            {
                case 0x01:
                    if (reference.TypeCode < 0x80)
                    {
                        if (qualifiers != 0)
                        {
                            throw InvalidLayout();
                        }
                    }
                    else
                    {
                        ValidateFamilyQualifiers(reference.TypeCode,
                            qualifiers);
                    }
                    return;

                case 0x02:
                case 0x03:
                    var typeDef = _typeSpecs.GetNominalTypeDefRow(
                        input.Reference, _typeDefs, _imports);
                    var kind = _typeDefs.GetKind(typeDef);
                    if ((kind == 2 && (qualifiers & ~3) != 0)
                        || (kind != 2 && qualifiers != 0))
                    {
                        throw InvalidLayout();
                    }
                    if (_typeDefs.GetGenericRange(typeDef).Count != 0)
                    {
                        throw UnsupportedLayout();
                    }
                    return;

                case 0x05:
                    if (qualifiers != 0)
                    {
                        throw InvalidLayout();
                    }

                    var constructedTypeDef
                        = _typeSpecs.GetNominalTypeDefRow(input.Reference,
                            _typeDefs, _imports);
                    var (firstFormal, formalCount)
                        = _typeDefs.GetGenericRange(constructedTypeDef);
                    var argumentCount
                        = _typeSpecs.GetConstructedArgumentCount(
                            reference.Row);
                    if (formalCount == 0 || argumentCount != formalCount)
                    {
                        throw InvalidLayout();
                    }

                    for (var ordinal = 0; ordinal < formalCount; ordinal++)
                    {
                        if (_genericParams.GetFormalKind(firstFormal + ordinal)
                            != 1)
                        {
                            throw InvalidLayout();
                        }

                        ValidateGenericActualTypeQualifications(
                            new TypeSpecContext(
                                _typeSpecs.GetConstructedArgument(
                                    reference.Row, ordinal),
                                input.OwnerModule, input.Substitutions));
                    }
                    return;

                case 0x06:
                    if ((qualifiers & ~1) != 0)
                    {
                        throw InvalidLayout();
                    }

                    var optionalInner = new TypeSpecContext(
                        Read(_typeSpecs.GetRow(reference.Row), 2),
                        input.OwnerModule, input.Substitutions);
                    if ((qualifiers & 1) != 0
                        && !SupportsWriteAccess(optionalInner))
                    {
                        throw InvalidLayout();
                    }
                    ValidateGenericActualTypeQualifications(optionalInner);
                    return;

                case 0x07:
                    if ((qualifiers & ~3) != 0)
                    {
                        throw InvalidLayout();
                    }
                    ValidateWrappedTypeQualifications(input, reference);
                    return;

                case 0x08:
                    if ((qualifiers & ~1) != 0)
                    {
                        throw InvalidLayout();
                    }
                    ValidateWrappedTypeQualifications(input, reference);
                    return;

                case 0x09:
                    if ((qualifiers & ~3) != 0)
                    {
                        throw InvalidLayout();
                    }
                    ValidateWrappedTypeQualifications(input, reference);
                    return;

                case 0x0A:
                    if (qualifiers != 0)
                    {
                        throw InvalidLayout();
                    }
                    ValidateWrappedTypeQualifications(input, reference);
                    return;

                case 0x0B:
                    if ((qualifiers & ~1) != 0)
                    {
                        throw InvalidLayout();
                    }
                    ValidateWrappedTypeQualifications(input, reference);
                    return;

                case 0x0C:
                    ValidateFamilyQualifiers(reference.TypeCode,
                        qualifiers);
                    ValidateGenericActualTypeQualifications(
                        new TypeSpecContext(
                            Read(_typeSpecs.GetRow(reference.Row), 3),
                            input.OwnerModule, input.Substitutions),
                        allowInitQualifier: reference.TypeCode == 0x8A);
                    return;

                case 0x0D:
                    if (qualifiers != 0)
                    {
                        throw InvalidLayout();
                    }
                    return;

                case 0x0E:
                    if ((qualifiers & ~1) != 0)
                    {
                        throw InvalidLayout();
                    }
                    ValidateWrappedTypeQualifications(input, reference);
                    return;

                case 0x11:
                    if (qualifiers != 0)
                    {
                        throw InvalidLayout();
                    }
                    ValidateWrappedTypeQualifications(input, reference);
                    return;

                default:
                    throw UnsupportedLayout();
            }
        }

        private void ValidateWrappedTypeQualifications(
            TypeSpecContext context, TrRmTypeSpecRefInfo reference)
            => ValidateGenericActualTypeQualifications(new TypeSpecContext(
                Read(_typeSpecs.GetRow(reference.Row), 2),
                context.OwnerModule, context.Substitutions));

        private bool SupportsWriteAccess(TypeSpecContext input)
        {
            var context = ResolveTypeSpecContext(input);
            var reference = _typeSpecs.GetTypeSpecRef(context.Reference);
            return reference.Form switch
            {
                0x01 => IsWritableFamily(reference.TypeCode),
                0x02 or 0x03 => _typeDefs.GetKind(
                    _typeSpecs.GetNominalTypeDefRow(context.Reference,
                        _typeDefs, _imports)) == 2,
                0x05 => _typeDefs.GetKind(
                    _typeSpecs.GetNominalTypeDefRow(context.Reference,
                        _typeDefs, _imports)) == 2,
                0x06 => SupportsWriteAccess(new TypeSpecContext(
                    Read(_typeSpecs.GetRow(reference.Row), 2),
                    context.OwnerModule, context.Substitutions)),
                0x07 or 0x08 or 0x09 or 0x0B or 0x0E => true,
                0x0C => IsWritableFamily(reference.TypeCode),
                _ => false,
            };
        }

        private static bool IsWritableFamily(byte familyCode)
            => familyCode is 0x8A or 0x8B or 0x8C or 0x8D or 0x8F;

        private static void ValidateFamilyQualifiers(byte familyCode,
            byte qualifiers)
        {
            var allowedQualifiers = familyCode switch
            {
                >= 0x80 and <= 0x84 => 0,
                0x8A or 0x8C or 0x8F => 1,
                0x8B or 0x8D => 3,
                0x8E => 0,
                _ => throw InvalidLayout(),
            };
            if ((qualifiers & ~allowedQualifiers) != 0)
            {
                throw InvalidLayout();
            }
        }

        private GenericActualFacts GetGenericFamilyFacts(byte familyCode,
            byte atomicArgumentCode, byte qualifiers,
            TypeSpecContext context, bool indexed)
        {
            if (familyCode is >= 0x80 and <= 0x84)
            {
                var argumentFacts = indexed
                    ? GetGenericActualFacts(new TypeSpecContext(
                        GetIndexedFamilyArgument(context),
                        context.OwnerModule, context.Substitutions))
                    : GetAtomicFamilyArgumentFacts(atomicArgumentCode);
                if (!argumentFacts.Blittable)
                {
                    throw InvalidLayout();
                }

                return new GenericActualFacts(true, false, false,
                    familyCode != 0x84 && (qualifiers & 0x02) == 0);
            }

            return familyCode switch
            {
                0x8A => GetSpanFacts(context, indexed),
                0x8B => GetReferenceFamilyFacts(context, indexed),
                0x8C => GetPointerFamilyFacts(context, indexed, qualifiers),
                0x8D => GetBoxFamilyFacts(context, indexed),
                0x8E => GetValueFamilyFacts(context, indexed,
                    atomicArgumentCode),
                0x8F => GetOwnedFamilyFacts(context, indexed),
                _ => throw InvalidLayout(),
            };
        }

        private static GenericActualFacts GetAtomicFamilyArgumentFacts(
            byte argumentCode)
        {
            _ = GetAtomicLayout(argumentCode);
            return new GenericActualFacts(true, false, false, true);
        }

        private GenericActualFacts GetSpanFacts(TypeSpecContext context,
            bool indexed)
        {
            if (indexed)
            {
                _ = _typeSpecs.GetTypeSpecRef(
                    GetIndexedFamilyArgument(context),
                    allowInitQualifier: true);
            }

            return new GenericActualFacts(true, false, false, false);
        }

        private GenericActualFacts GetReferenceFamilyFacts(
            TypeSpecContext context, bool indexed)
        {
            if (indexed)
            {
                _ = _typeSpecs.GetTypeSpecRef(
                    GetIndexedFamilyArgument(context));
            }

            return new GenericActualFacts(true, false, false, false);
        }

        private GenericActualFacts GetPointerFamilyFacts(
            TypeSpecContext context, bool indexed, byte qualifiers)
        {
            if (indexed)
            {
                _ = _typeSpecs.GetTypeSpecRef(
                    GetIndexedFamilyArgument(context));
            }

            return new GenericActualFacts(true, false, false,
                (qualifiers & 0x02) == 0);
        }

        private GenericActualFacts GetBoxFamilyFacts(
            TypeSpecContext context, bool indexed)
        {
            if (indexed)
            {
                _ = _typeSpecs.GetTypeSpecRef(
                    GetIndexedFamilyArgument(context));
            }

            return new GenericActualFacts(true, false, false, false);
        }

        private GenericActualFacts GetValueFamilyFacts(
            TypeSpecContext context, bool indexed, byte atomicArgumentCode)
            => indexed
                ? GetGenericActualFacts(new TypeSpecContext(
                    GetIndexedFamilyArgument(context),
                    context.OwnerModule, context.Substitutions))
                : GetAtomicFamilyArgumentFacts(atomicArgumentCode);

        private GenericActualFacts GetOwnedFamilyFacts(
            TypeSpecContext context, bool indexed)
        {
            if (indexed)
            {
                _ = _typeSpecs.GetTypeSpecRef(
                    GetIndexedFamilyArgument(context));
            }

            return new GenericActualFacts(false, true, false, false);
        }

        private uint GetIndexedFamilyArgument(TypeSpecContext context)
        {
            var reference = _typeSpecs.GetTypeSpecRef(context.Reference);
            if (!reference.IsRow || reference.Form != 0x0C)
            {
                throw InvalidLayout();
            }

            return Read(_typeSpecs.GetRow(reference.Row), 3);
        }

        private GenericActualFacts GetConstructedTypeFacts(
            TypeSpecContext context, TrRmTypeSpecRefInfo reference)
        {
            using var frame = EnterFrame();
            ConsumeWork();
            if (reference.Qualifiers != 0)
            {
                throw InvalidLayout();
            }

            var typeDef = _typeSpecs.GetNominalTypeDefRow(context.Reference,
                _typeDefs, _imports);
            var (firstFormal, formalCount) = _typeDefs.GetGenericRange(typeDef);
            _ = CreateTypeSubstitution(context, typeDef,
                firstFormal, formalCount);
            return GetTypeDefFacts(typeDef, 0);
        }

        private GenericActualFacts GetTypeDefFacts(int typeDef,
            byte qualifiers)
        {
            var kind = _typeDefs.GetKind(typeDef);
            var unique = kind == 1
                && (_typeDefs.HasDeclaredUnique(typeDef)
                    || (!_schema.SupportsDeclaredUniqueEvidence()
                        && HasCleanup(typeDef)));
            var copyCapable = kind == 2
                || (kind == 1 && _typeDefs.HasDeclaredCopy(typeDef)
                    && !unique);
            var blittable = kind == 1
                && _layouts.HasBlittableLayout(typeDef, 1, 1)
                && (qualifiers & 0x02) == 0;
            return new GenericActualFacts(copyCapable, unique, false,
                blittable);
        }

        private void ConsumeGenericVisit()
        {
            if (_a3ResourceLedger is null)
            {
                _genericVisitBudget.Consume();
            }
            else
            {
                _a3ResourceLedger.ConsumeWork();
            }
        }

        private void ConsumeWork(ulong units = 1)
            => _a3ResourceLedger?.ConsumeWork(units);

        private uint Read(ReadOnlySpan<byte> row, int offset)
        {
            _a3ResourceLedger?.ConsumeWork(sizeof(uint) + 1);
            return BinaryPrimitives.ReadUInt32LittleEndian(
                row.Slice(offset, sizeof(uint)));
        }

        private TrRmA3LayoutResourceLedger.FrameLease EnterFrame()
            => _a3ResourceLedger is null
                ? default
                : _a3ResourceLedger.EnterFrame();

        private void CheckDepth(ulong depth)
            => _a3ResourceLedger?.CheckDepthFromCurrent(depth);

        private ulong RoundUpTargetLayout(ulong value, ulong alignment)
        {
            try
            {
                return RoundUp(value, alignment);
            }
            catch (OverflowException exception)
            {
                if (_a3ResourceLedger is null)
                {
                    throw InvalidLayout(exception);
                }

                _a3ResourceLedger.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.TargetLayoutOverflow);
                throw;
            }
        }

        private ulong AddTargetLayout(ulong left, ulong right)
        {
            try
            {
                return checked(left + right);
            }
            catch (OverflowException exception)
            {
                if (_a3ResourceLedger is null)
                {
                    throw InvalidLayout(exception);
                }

                _a3ResourceLedger.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.TargetLayoutOverflow);
                throw;
            }
        }

        private ulong MultiplyTargetLayout(ulong left, ulong right)
        {
            try
            {
                return checked(left * right);
            }
            catch (OverflowException exception)
            {
                if (_a3ResourceLedger is null)
                {
                    throw InvalidLayout(exception);
                }

                _a3ResourceLedger.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.TargetLayoutOverflow);
                throw;
            }
        }

        private Exception UnsupportedLayout()
            => _schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? new NotSupportedException(
                    "The selected A3 fixed-array layout route is outside supported admission.")
                : InvalidLayout();

        private TypeLayout CalculateNonGenericTypeDefLayout(uint typeSpecRef)
        {
            var typeDef = _typeSpecs.GetNominalTypeDefRow(typeSpecRef,
                _typeDefs, _imports);
            if (_typeDefs.GetGenericRange(typeDef).Count != 0)
            {
                throw InvalidLayout();
            }

            return CalculateTypeDefLayout(typeDef);
        }

        private bool ContainsFormalReference(uint typeSpecRef)
        {
            var pending = new Stack<(uint Reference, bool AllowInitQualifier,
                ulong Depth)>();
            var visited = new Dictionary<(int Row, bool AllowInitQualifier),
                ulong>();
            _a3ResourceLedger?.ReserveWorkAtDepth(1, 1);
            pending.Push((typeSpecRef, false, 1));
            while (pending.TryPop(out var current))
            {
                ConsumeWork();
                CheckDepth(current.Depth);
                var reference = _typeSpecs.GetTypeSpecRef(current.Reference,
                    allowInitQualifier: current.AllowInitQualifier);
                if (!reference.IsRow)
                {
                    continue;
                }
                var visitKey = (reference.Row,
                    current.AllowInitQualifier);
                if (visited.TryGetValue(visitKey, out var previousDepth)
                    && current.Depth <= previousDepth)
                {
                    continue;
                }
                visited[visitKey] = current.Depth;
                if (reference.Form == 0x04)
                {
                    return true;
                }

                var row = _typeSpecs.GetRow(reference.Row);
                switch (reference.Form)
                {
                    case 0x05:
                        for (var ordinal = 0;
                            ordinal < _typeSpecs.GetConstructedArgumentCount(
                                reference.Row);
                            ordinal++)
                        {
                            var childDepth
                                = checked(current.Depth + 1);
                            _a3ResourceLedger?.ReserveWorkAtDepth(1,
                                childDepth);
                            pending.Push((
                                _typeSpecs.GetConstructedArgument(
                                    reference.Row, ordinal), false,
                                childDepth));
                        }
                        break;

                    case >= 0x06 and <= 0x0B:
                    case 0x0E:
                    case 0x11:
                        var unaryDepth = checked(current.Depth + 1);
                        _a3ResourceLedger?.ReserveWorkAtDepth(1,
                            unaryDepth);
                        pending.Push((Read(row, 2), false,
                            unaryDepth));
                        break;

                    case 0x0C:
                        var indexedDepth = checked(current.Depth + 1);
                        _a3ResourceLedger?.ReserveWorkAtDepth(1,
                            indexedDepth);
                        pending.Push((Read(row, 3),
                            reference.TypeCode == 0x8A,
                            indexedDepth));
                        break;
                }
            }

            return false;
        }

        private TypeLayout CalculateArrayElementLayout(int arrayTypeSpecRow,
            int ownerModule, uint arrayTypeSpecRef)
        {
            var layoutRow = _arrayLayouts.FindRow(ownerModule,
                arrayTypeSpecRef, (uint)_profileRow);
            if (layoutRow == 0)
            {
                throw InvalidLayout();
            }

            var array = CalculateArrayLayout(ownerModule, arrayTypeSpecRow);
            var declaredLayout = _arrayLayouts.GetRow(layoutRow);
            if (array.Stride != declaredLayout.ElementStride
                || array.Extent != declaredLayout.ByteExtent
                || array.Alignment != declaredLayout.Alignment)
            {
                throw InvalidLayout();
            }

            return new TypeLayout(array.Extent, array.Alignment,
                array.RequiredAlignment);
        }

        internal (ulong Size, ulong Alignment, ulong RequiredAlignment)
            CalculateTypeDefLayoutForProvider(int typeDef)
        {
            var layout = CalculateTypeDefLayout(typeDef);
            return (layout.Size, layout.Alignment, layout.RequiredAlignment);
        }

        private TypeLayout CalculateTypeDefLayout(int typeDef)
            => CalculateTypeDefLayout(typeDef, null);

        private TypeLayout CalculateTypeDefLayout(int typeDef,
            TypeSubstitutionContext? substitutions)
        {
            using var frame = EnterFrame();
            ConsumeWork();
            var typeDefModule = _typeDefs.GetOwnerModule(typeDef);
            var genericCount = _typeDefs.GetGenericRange(typeDef).Count;
            if (_typeDefs.GetKind(typeDef) != 1
                || !_typeDefs.HasDeclaredCopy(typeDef)
                || !_layouts.HasBlittableLayout(typeDef, 1, 1))
            {
                throw InvalidLayout();
            }
            if (genericCount != 0
                && (_schema
                        != TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                    || substitutions is null))
            {
                throw UnsupportedLayout();
            }

            var typeKey = (typeDefModule, typeDef);
            var trackDefinitionCycle = genericCount == 0;
            if (trackDefinitionCycle && !_visitingTypeDefs.Add(typeKey))
            {
                throw InvalidLayout();
            }

            try
            {
                if (HasCleanup(typeDef))
                {
                    throw InvalidLayout();
                }

                var selectedProfile = checked((uint)_profileRow);
                var layoutRow = _layouts.GetLayoutForProfile(typeDef,
                    selectedProfile);
                if (_layouts.GetTargetProfileRef(layoutRow) != selectedProfile
                    || _layouts.GetFlags(layoutRow) != 1)
                {
                    throw InvalidLayout();
                }

                var kind = _layouts.GetLayoutKind(layoutRow);
                var (firstField, fieldCount)
                    = _layouts.GetLayoutFieldRange(layoutRow);
                if (kind == 3)
                {
                    ConsumeWork();
                    if (fieldCount != 1)
                    {
                        throw InvalidLayout();
                    }

                    var field = _layoutFields.GetFieldDef(firstField);
                    return CalculateTypeLayout(
                        new TypeSpecContext(_fields.GetTypeSpecRef(field),
                            typeDefModule,
                            genericCount == 0 ? null : substitutions));
                }

                if (kind != 1)
                {
                    throw InvalidLayout();
                }

                var declaredPack = _layouts.GetPack(layoutRow);
                var pack = declaredPack == 0 ? 16UL : declaredPack;
                var explicitAlignment = _layouts.GetAlignment(layoutRow);
                var end = 0UL;
                var aggregateAlignment = 1UL;
                var requiredAlignment = Math.Max(1UL, explicitAlignment);
                for (var ordinal = 0; ordinal < fieldCount; ordinal++)
                {
                    ConsumeWork();
                    var field = _layoutFields.GetFieldDef(firstField + ordinal);
                    var fieldLayout = CalculateTypeLayout(
                        new TypeSpecContext(_fields.GetTypeSpecRef(field),
                            typeDefModule,
                            genericCount == 0 ? null : substitutions));
                    requiredAlignment = Math.Max(requiredAlignment,
                        fieldLayout.RequiredAlignment);
                    if (fieldLayout.Size == 0)
                    {
                        continue;
                    }

                    if (fieldLayout.Alignment == 0)
                    {
                        throw InvalidLayout();
                    }

                    var placementAlignment = Math.Max(
                        Math.Min(fieldLayout.Alignment, pack),
                        fieldLayout.RequiredAlignment);
                    end = AddTargetLayout(
                        RoundUpTargetLayout(end, placementAlignment),
                        fieldLayout.Size);
                    aggregateAlignment = Math.Max(aggregateAlignment,
                        placementAlignment);
                }

                if (explicitAlignment != 0)
                {
                    if (explicitAlignment < aggregateAlignment)
                    {
                        throw InvalidLayout();
                    }

                    aggregateAlignment = explicitAlignment;
                }

                var size = RoundUpTargetLayout(end == 0 ? 1UL : end,
                    aggregateAlignment);
                requiredAlignment = Math.Max(requiredAlignment,
                    explicitAlignment);
                return new TypeLayout(size, aggregateAlignment,
                    requiredAlignment);
            }
            catch (OverflowException exception)
            {
                if (_a3ResourceLedger is not null
                    && TrRmExecutableCompiler.TryGetA3AdmissionFailure(
                        exception, out var kind)
                    && kind
                        == TrRmA3AdmissionFailureKind.TargetLayoutOverflow)
                {
                    throw;
                }

                throw InvalidLayout(exception);
            }
            finally
            {
                if (trackDefinitionCycle)
                {
                    _ = _visitingTypeDefs.Remove(typeKey);
                }
            }
        }

        private bool HasCleanup(int typeDef)
        {
            var (first, count) = _typeDefs.GetContractRange(typeDef);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                ConsumeWork();
                if (BinaryPrimitives.ReadUInt16LittleEndian(
                        _contracts.GetRow(first + ordinal)[4..]) == 5)
                {
                    return true;
                }
            }

            return false;
        }

        private static TypeLayout GetInlineLayout(
            TrRmTypeSpecRefInfo reference)
        {
            if (reference.TypeCode == 0x8C)
            {
                return new TypeLayout(sizeof(ulong), sizeof(ulong), 1);
            }

            return reference.TypeCode < 0x80
                ? GetAtomicLayout(reference.TypeCode)
                : throw InvalidLayout();
        }
    }
}
