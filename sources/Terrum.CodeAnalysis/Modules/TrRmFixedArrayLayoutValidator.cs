// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmReturnCoverageSubjectGeometry(
    ulong Size, ImmutableArray<TrRmReturnCoverageRegion> PossiblePayloads);

internal static class TrRmFixedArrayLayoutValidator
{
    private const int MaximumReturnCoverageComparisonDepth = 64;

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

    internal static void ValidateReturnCoverageSubjects(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        if (!tables.Schema.UsesRawUnionValueCoverage())
        {
            return;
        }

        var hasReturnCoverage = false;
        for (var method = 1; method <= tables.Methods.Count; method++)
        {
            if (!tables.Contracts.GetReturnCoverageContracts(method).IsEmpty)
            {
                hasReturnCoverage = true;
                break;
            }
        }
        if (!hasReturnCoverage)
        {
            return;
        }

        if (!tables.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision,
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
            validator.ValidateReturnCoverageSubjects(tables.Methods,
                tables.Signatures, tables.Contracts);
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
            when (tables.A3LayoutResourceLedger is { } resourceLedger)
        {
            resourceLedger.AdmissionScope.Mark(exception,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw;
        }
        catch (NotSupportedException exception)
            when (tables.A3LayoutResourceLedger is { } resourceLedger)
        {
            resourceLedger.AdmissionScope.Mark(exception,
                TrRmA3AdmissionFailureKind.UnsupportedAdmission);
            throw;
        }
    }

    internal static TrRmReturnCoverageSubjectGeometry
        CalculateReturnCoverageSubjectGeometry(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            uint resultTypeSpecRef, int ownerModule)
    {
        if (!tables.Schema.UsesRawUnionValueCoverage()
            || !tables.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision,
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
            return validator.CalculateReturnCoverageSubjectGeometry(
                resultTypeSpecRef, ownerModule);
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
    }

    internal static void ValidateReturnCoverageSubjectAndRegions(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        uint resultTypeSpecRef, int ownerModule,
        ImmutableArray<TrRmReturnCoverageRegion> regions)
    {
        if (!tables.Schema.UsesRawUnionValueCoverage()
            || !tables.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw InvalidLayout();
        }

        var validator = new ValidationContext(tables.FixedArrayLayouts,
            tables.TypeSpecs, tables.TypeDefs, tables.Fields, tables.Layouts,
            tables.LayoutFields, profileRow, tables.Imports, tables.Contracts,
            tables.GenericParams, tables.Constraints, tables.Schema,
            tables.A3LayoutResourceLedger);
        validator.ValidateReturnCoverageSubjectAndRegions(resultTypeSpecRef,
            ownerModule, regions);
    }

    internal static bool IsReturnCoverageSubjectAndRegionsValid(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        uint resultTypeSpecRef, int ownerModule,
        ImmutableArray<TrRmReturnCoverageRegion> regions)
    {
        try
        {
            ValidateReturnCoverageSubjectAndRegions(tables,
                resultTypeSpecRef, ownerModule, regions);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static ulong RoundUp(ulong value, ulong alignment,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork();
        if (alignment == 0)
        {
            throw InvalidLayout();
        }

        resourceLedger?.ConsumeWork();
        var alignmentMask = alignment - 1;
        resourceLedger?.ConsumeWork(2);
        if ((alignment & alignmentMask) != 0)
        {
            throw InvalidLayout();
        }

        resourceLedger?.ConsumeWork();
        var roundedValue = checked(value + alignmentMask);
        resourceLedger?.ConsumeWork();
        return roundedValue & ~alignmentMask;
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
        0x8C => new TypeLayout(sizeof(ulong), sizeof(ulong), 1),
        _ => throw InvalidLayout(),
    };

    private static InvalidDataException InvalidLayout(Exception? inner = null)
        => new("Invalid TRRM FixedArrayLayout element or target geometry.",
            inner);

    private readonly record struct ArrayLayout(
        ulong Stride, ulong Extent, ulong Alignment,
        ulong RequiredAlignment,
        ImmutableArray<TrRmReturnCoverageRegion> PossiblePayloads = default,
        bool ContainsRawUnion = false);

    private readonly record struct TypeLayout(
        ulong Size, ulong Alignment, ulong RequiredAlignment,
        ImmutableArray<TrRmReturnCoverageRegion> PossiblePayloads = default,
        bool ContainsRawUnion = false);

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
        TypeSubstitutionContext? parent,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        private readonly Dictionary<int, TypeSpecContext> _arguments = arguments;
        private readonly TypeSubstitutionContext? _parent = parent;
        private readonly TrRmA3LayoutResourceLedger? _resourceLedger
            = resourceLedger;

        internal bool TryGet(int formalRow, out TypeSpecContext argument)
        {
            for (var current = this; current is not null;
                current = current._parent)
            {
                _resourceLedger?.ConsumeWork();
                if (current._arguments.TryGetValue(formalRow,
                        out argument))
                {
                    _resourceLedger?.ConsumeWork();
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
            _a3ResourceLedger?.ConsumeWork(5);
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
            ConsumeWork(2);
            if (_visitingArrays.Contains(key))
            {
                throw InvalidLayout();
            }
            ConsumeWork();
            _ = _visitingArrays.Add(key);

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
                ConsumeWork();
                var alignment = Math.Max(1UL, element.Alignment);
                var stride = RoundUpTargetLayout(element.Size, alignment);
                var extent = MultiplyTargetLayout(elementCount, stride);
                var payloads = CanonicalizePayloadRanges(
                    AddArrayRepetition(element.PossiblePayloads,
                        elementCount, stride));
                return new ArrayLayout(stride, extent, alignment,
                    element.RequiredAlignment, payloads,
                    element.ContainsRawUnion);
            }
            finally
            {
                ConsumeWork();
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
            if (context.Substitutions is null)
            {
                var cacheKey = (context.OwnerModule, context.Reference);
                ConsumeWork(2);
                if (_typeCache.TryGetValue(cacheKey, out var cached))
                {
                    ConsumeWork();
                    return cached;
                }
            }

            var reference = _typeSpecs.GetTypeSpecRef(context.Reference);
            TypeLayout result;
            if (!reference.IsRow)
            {
                result = AddAtomicPayload(GetInlineLayout(reference));
            }
            else
            {
                var row = _typeSpecs.GetRow(reference.Row);
                result = reference.Form switch
                {
                    0x01 => AddAtomicPayload(GetAtomicLayout(row[2])),
                    0x02 or 0x03 => CalculateNonGenericTypeDefLayout(
                        context.Reference),
                    0x05 when _schema
                        == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                        => CalculateConstructedTypeLayout(context),
                    0x0C when reference.TypeCode == 0x8C
                        && (reference.Qualifiers & ~1) == 0
                        => AddAtomicPayload(new TypeLayout(sizeof(ulong),
                            sizeof(ulong), 1)),
                    0x0D => AddAtomicPayload(new TypeLayout(sizeof(ulong),
                        sizeof(ulong), 1)),
                    0x08 when (reference.Qualifiers & ~1) == 0
                        => AddAtomicPayload(new TypeLayout(sizeof(ulong),
                            sizeof(ulong), 1)),
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
                ConsumeWork();
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
            var identityPayloadBytes = checked((ulong)key.Length * sizeof(char));
            ConsumeWork(checked(identityPayloadBytes + 1UL));
            if (_activeGenericActuals.Contains(key))
            {
                throw InvalidLayout();
            }
            ConsumeWork(checked(identityPayloadBytes + 2UL));
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
                ConsumeWork(checked(identityPayloadBytes + 1UL));
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

        private ulong GetDecimalLength(int value)
        {
            ConsumeWork(5);
            var isNegative = value < 0;
            var length = isNegative ? 1UL : 0UL;
            var magnitude = isNegative
                ? (uint)-(long)value : (uint)value;
            while (true)
            {
                ConsumeWork(2);
                length++;
                magnitude /= 10;
                ConsumeWork();
                if (magnitude == 0)
                {
                    break;
                }
            }

            return length;
        }

        private ulong GetDecimalLength(uint value)
        {
            var length = 1UL;
            while (true)
            {
                ConsumeWork();
                if (value < 10)
                {
                    break;
                }

                ConsumeWork(2);
                value /= 10;
                length++;
            }

            return length;
        }

        private ulong GetDecimalLength(ulong value)
        {
            var length = 1UL;
            while (true)
            {
                ConsumeWork();
                if (value < 10)
                {
                    break;
                }

                ConsumeWork(2);
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

            ConsumeWork(checked((ulong)formalCount + 1UL));
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

            ConsumeWork();
            return new TypeSubstitutionContext(arguments,
                context.Substitutions, _a3ResourceLedger);
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
                return RoundUp(value, alignment, _a3ResourceLedger);
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
                ConsumeWork();
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
                ConsumeWork();
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
            _a3ResourceLedger?.ReserveWorkAtDepth(1, 1);
            var pending = new Stack<(uint Reference, bool AllowInitQualifier,
                ulong Depth)>();
            pending.Push((typeSpecRef, false, 1));
            ConsumeWork();
            var visited = new Dictionary<(int Row, bool AllowInitQualifier),
                ulong>();
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
                ConsumeWork(2);
                if (visited.TryGetValue(visitKey, out var previousDepth))
                {
                    if (current.Depth <= previousDepth)
                    {
                        continue;
                    }

                    ConsumeWork();
                    visited[visitKey] = current.Depth;
                }
                else
                {
                    ConsumeWork();
                    visited.Add(visitKey, current.Depth);
                }
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
                array.RequiredAlignment, array.PossiblePayloads,
                array.ContainsRawUnion);
        }

        internal void ValidateReturnCoverageSubjects(
            TrRmMethodDefTableReader methods,
            TrRmMethodSigTableReader signatures,
            TrRmContractTableReader contracts)
        {
            for (var method = 1; method <= methods.Count; method++)
            {
                ConsumeWork();
                var coverageContracts
                    = contracts.GetReturnCoverageContracts(method);
                if (coverageContracts.IsEmpty)
                {
                    continue;
                }

                var signature = methods.GetSignature(method);
                var resultType = signatures.GetResultTypeSpec(signature);
                var ownerModule = methods.GetOwnerModule(method);
                foreach (var coverage in coverageContracts)
                {
                    ValidateReturnCoverageSubjectAndRegions(resultType,
                        ownerModule, coverage.Regions);
                }
            }
        }

        internal TrRmReturnCoverageSubjectGeometry
            CalculateReturnCoverageSubjectGeometry(
                uint resultTypeSpecRef, int ownerModule)
        {
            if (!_schema.UsesRawUnionValueCoverage()
                || ownerModule <= 0 || resultTypeSpecRef == 0)
            {
                throw InvalidLayout();
            }

            TypeLayout layout;
            try
            {
                layout = CalculateTypeLayout(resultTypeSpecRef, ownerModule);
            }
            catch (NotSupportedException exception)
            {
                throw InvalidLayout(exception);
            }

            if (!layout.ContainsRawUnion)
            {
                throw InvalidLayout();
            }

            return new TrRmReturnCoverageSubjectGeometry(layout.Size,
                layout.PossiblePayloads.IsDefault
                    ? []
                    : layout.PossiblePayloads);
        }

        internal void ValidateReturnCoverageSubjectAndRegions(
            uint resultTypeSpecRef, int ownerModule,
            ImmutableArray<TrRmReturnCoverageRegion> regions)
        {
            var geometry = CalculateReturnCoverageSubjectGeometry(
                resultTypeSpecRef, ownerModule);
            if (!IsCoverageSubsetOfPossiblePayloads(regions,
                    geometry.PossiblePayloads))
            {
                throw InvalidLayout();
            }

            foreach (var region in regions)
            {
                ConsumeWork();
                var span = GetRegionSpan(region);
                if (region.Offset > geometry.Size
                    || span > geometry.Size - region.Offset)
                {
                    throw InvalidLayout();
                }
            }
        }

        internal (ulong Size, ulong Alignment, ulong RequiredAlignment)
            CalculateTypeDefLayoutForProvider(int typeDef)
        {
            var layout = CalculateTypeDefLayout(typeDef);
            return (layout.Size, layout.Alignment, layout.RequiredAlignment);
        }

        private TypeLayout AddAtomicPayload(TypeLayout layout)
        {
            if (!_schema.UsesRawUnionValueCoverage() || layout.Size == 0)
            {
                return layout;
            }

            ConsumeWork(2);
            return layout with
            {
                PossiblePayloads = [
                    new TrRmReturnCoverageRegion(0, layout.Size, []),
                ],
            };
        }

        private ImmutableArray<TrRmReturnCoverageRegion> AddArrayRepetition(
            ImmutableArray<TrRmReturnCoverageRegion> payloads,
            ulong count, ulong stride)
        {
            if (!_schema.UsesRawUnionValueCoverage()
                || payloads.IsDefaultOrEmpty)
            {
                return [];
            }
            if (count == 0)
            {
                return [];
            }
            if (count == 1)
            {
                return payloads;
            }

            ConsumeWork(checked((ulong)payloads.Length + 1UL));
            var result
                = ImmutableArray.CreateBuilder<TrRmReturnCoverageRegion>(
                    payloads.Length);
            foreach (var payload in payloads)
            {
                ConsumeWork();
                var repetitions = payload.Repetitions.IsDefault
                    ? []
                    : payload.Repetitions;
                result.Add(payload with
                {
                    Repetitions = repetitions.Add(
                        new TrRmReturnCoverageRepetition(count, stride)),
                });
            }

            return result.ToImmutable();
        }

        private ImmutableArray<TrRmReturnCoverageRegion>
            CanonicalizePayloadRanges(
                ImmutableArray<TrRmReturnCoverageRegion> payloads)
        {
            if (payloads.IsDefaultOrEmpty)
            {
                return [];
            }

            ConsumeWork(checked((ulong)payloads.Length + 1UL));
            var ordered = new TrRmReturnCoverageRegion[payloads.Length];
            for (var index = 0; index < ordered.Length; index++)
            {
                ConsumeWork();
                ordered[index] = NormalizePayloadRegion(payloads[index]);
            }

            var resourceLedger = _a3ResourceLedger;
            Array.Sort(ordered, (left, right) =>
            {
                resourceLedger?.ConsumeWork();
                return ComparePayloadRegions(left, right);
            });
            ConsumeWork(checked((ulong)ordered.Length + 1UL));
            var result
                = ImmutableArray.CreateBuilder<TrRmReturnCoverageRegion>(
                    ordered.Length);
            foreach (var region in ordered)
            {
                ConsumeWork();
                if (result.Count == 0
                    || !SamePayloadRepetitions(result[^1], region))
                {
                    result.Add(region);
                    continue;
                }

                var previousIndex = result.Count - 1;
                var previous = result[previousIndex];
                var previousEnd = checked(previous.Offset + previous.Length);
                var regionEnd = checked(region.Offset + region.Length);
                if (region.Offset > previousEnd)
                {
                    result.Add(region);
                }
                else if (regionEnd > previousEnd)
                {
                    result[previousIndex] = previous with
                    {
                        Length = regionEnd - previous.Offset,
                    };
                }
            }

            ConsumeWork(checked((ulong)result.Count + 1UL));
            return result.ToImmutable();
        }

        private TrRmReturnCoverageRegion NormalizePayloadRegion(
            TrRmReturnCoverageRegion region)
        {
            var sourceRepetitions = region.Repetitions.IsDefault
                ? []
                : region.Repetitions;
            if (sourceRepetitions.IsEmpty)
            {
                return region with { Repetitions = sourceRepetitions };
            }

            ConsumeWork(checked((ulong)sourceRepetitions.Length + 1UL));
            var repetitions
                = ImmutableArray.CreateBuilder<TrRmReturnCoverageRepetition>(
                    sourceRepetitions.Length);
            var length = region.Length;
            var span = length;
            var isContiguous = true;
            foreach (var repetition in sourceRepetitions)
            {
                ConsumeWork();
                if (isContiguous && repetition.Stride == span)
                {
                    length = checked(length * repetition.Count);
                    span = length;
                }
                else
                {
                    repetitions.Add(repetition);
                    isContiguous &= repetition.Stride == span;
                    span = checked(((repetition.Count - 1)
                        * repetition.Stride) + span);
                }
            }

            return region with
            {
                Length = length,
                Repetitions = repetitions.ToImmutable(),
            };
        }

        private static bool SamePayloadRepetitions(
            TrRmReturnCoverageRegion left, TrRmReturnCoverageRegion right)
        {
            var leftRepetitions = left.Repetitions.IsDefault
                ? []
                : left.Repetitions;
            var rightRepetitions = right.Repetitions.IsDefault
                ? []
                : right.Repetitions;
            if (leftRepetitions.Length != rightRepetitions.Length)
            {
                return false;
            }

            for (var index = 0; index < leftRepetitions.Length; index++)
            {
                if (leftRepetitions[index] != rightRepetitions[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ComparePayloadRegions(
            TrRmReturnCoverageRegion left, TrRmReturnCoverageRegion right)
        {
            var leftRepetitions = left.Repetitions.IsDefault
                ? []
                : left.Repetitions;
            var rightRepetitions = right.Repetitions.IsDefault
                ? []
                : right.Repetitions;
            var comparison = leftRepetitions.Length.CompareTo(
                rightRepetitions.Length);
            for (var index = 0;
                comparison == 0 && index < leftRepetitions.Length;
                index++)
            {
                comparison = leftRepetitions[index].Count.CompareTo(
                    rightRepetitions[index].Count);
                if (comparison == 0)
                {
                    comparison = leftRepetitions[index].Stride.CompareTo(
                        rightRepetitions[index].Stride);
                }
            }

            if (comparison == 0)
            {
                comparison = left.Offset.CompareTo(right.Offset);
            }
            if (comparison == 0)
            {
                comparison = left.Length.CompareTo(right.Length);
            }

            return comparison;
        }

        private bool IsCoverageSubsetOfPossiblePayloads(
            ImmutableArray<TrRmReturnCoverageRegion> coverage,
            ImmutableArray<TrRmReturnCoverageRegion> possiblePayloads)
        {
            if (coverage.IsDefaultOrEmpty)
            {
                return true;
            }

            var normalizedPayloads
                = CanonicalizePayloadRanges(possiblePayloads);
            ConsumeWork(checked((ulong)coverage.Length + 1UL));
            var remainingCoverage
                = new List<TrRmReturnCoverageRegion>(coverage.Length);
            foreach (var region in coverage)
            {
                ConsumeWork();
                var normalizedRegion = NormalizePayloadRegion(region);
                if (TryGetSmallCoverageByteCount(normalizedRegion,
                        out var byteCount))
                {
                    if (!IsSmallCoverageCoveredByPayloadUnion(
                            normalizedRegion, byteCount, normalizedPayloads))
                    {
                        return false;
                    }
                }
                else
                {
                    remainingCoverage.Add(normalizedRegion);
                }
            }

            var normalizedCoverage = CanonicalizePayloadRanges(
                [.. remainingCoverage]);
            foreach (var region in normalizedCoverage)
            {
                ConsumeWork();
                var isContained = false;
                foreach (var payload in normalizedPayloads)
                {
                    ConsumeWork();
                    if (ContainsPayloadRegion(payload, region))
                    {
                        isContained = true;
                        break;
                    }
                }

                if (!isContained
                    && !IsLargeCoverageCoveredByPayloadUnion(
                        region, normalizedPayloads))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsLargeCoverageCoveredByPayloadUnion(
            TrRmReturnCoverageRegion coverage,
            ImmutableArray<TrRmReturnCoverageRegion> possiblePayloads)
            => coverage.Repetitions.IsDefaultOrEmpty
                ? IsCompactRankZeroCoverageCoveredByPayloadUnion(coverage,
                    possiblePayloads, 0)
                : IsRepeatedCoverageCoveredByCommonOuterDimensions(coverage,
                    possiblePayloads);

        private bool IsCompactRankZeroCoverageCoveredByPayloadUnion(
            TrRmReturnCoverageRegion coverage,
            ImmutableArray<TrRmReturnCoverageRegion> possiblePayloads,
            int depth)
        {
            if (!coverage.Repetitions.IsDefaultOrEmpty
                || possiblePayloads.IsDefaultOrEmpty)
            {
                return false;
            }

            if (TryGetSmallCoverageByteCount(coverage, out var byteCount))
            {
                return IsSmallCoverageCoveredByPayloadUnion(coverage,
                    byteCount, possiblePayloads);
            }

            foreach (var payload in possiblePayloads)
            {
                ConsumeWork();
                if (ContainsPayloadRegion(payload, coverage))
                {
                    return true;
                }
            }

            var allPayloadsRankZero = true;
            foreach (var payload in possiblePayloads)
            {
                ConsumeWork();
                if (!payload.Repetitions.IsDefaultOrEmpty)
                {
                    allPayloadsRankZero = false;
                    break;
                }
            }

            if (allPayloadsRankZero)
            {
                var coverageEnd = checked(coverage.Offset
                    + coverage.Length);
                var coveredUntil = coverage.Offset;
                foreach (var payload in possiblePayloads)
                {
                    ConsumeWork();
                    if (payload.Offset > coveredUntil)
                    {
                        return false;
                    }

                    var payloadEnd = checked(payload.Offset
                        + payload.Length);
                    coveredUntil = Math.Max(coveredUntil, payloadEnd);
                    if (coveredUntil >= coverageEnd)
                    {
                        return true;
                    }
                }

                return false;
            }

            if (depth >= MaximumReturnCoverageComparisonDepth)
            {
                throw new TrRmResourceLimitException(
                    "validating ReturnCoverage payload inclusion",
                    "MethodDef Contract 000F", "depth",
                    MaximumReturnCoverageComparisonDepth,
                    new BigInteger(depth + 1), moduleIdentity: null);
            }

            var commonOuter = default(
                TrRmReturnCoverageRepetition);
            var hasCommonOuter = false;
            var elementPayloads
                = ImmutableArray.CreateBuilder<
                    TrRmReturnCoverageRegion>(possiblePayloads.Length);
            foreach (var payload in possiblePayloads)
            {
                ConsumeWork();
                var repetitions = payload.Repetitions;
                if (repetitions.IsDefaultOrEmpty)
                {
                    return IsRankZeroCoverageCoveredByPeriodicPayloadUnion(
                        coverage, possiblePayloads);
                }

                var outer = repetitions[^1];
                if (!hasCommonOuter)
                {
                    commonOuter = outer;
                    hasCommonOuter = true;
                }
                else if (outer != commonOuter)
                {
                    return IsRankZeroCoverageCoveredByPeriodicPayloadUnion(
                        coverage, possiblePayloads);
                }

                if (outer.Stride == 0)
                {
                    return false;
                }
                if (payload.Offset >= outer.Stride)
                {
                    return IsRankZeroCoverageCoveredByPeriodicPayloadUnion(
                        coverage, possiblePayloads);
                }

                var innerRepetitionCount = repetitions.Length - 1;
                var innerRepetitions
                    = ImmutableArray.CreateBuilder<
                        TrRmReturnCoverageRepetition>(
                            innerRepetitionCount);
                for (var dimension = 0;
                    dimension < innerRepetitionCount;
                    dimension++)
                {
                    ConsumeWork();
                    innerRepetitions.Add(repetitions[dimension]);
                }

                var elementPayload = payload with
                {
                    Repetitions = innerRepetitions.MoveToImmutable(),
                };
                var elementSpan = GetRegionSpan(elementPayload);
                if (elementSpan > outer.Stride - payload.Offset)
                {
                    return IsRankZeroCoverageCoveredByPeriodicPayloadUnion(
                        coverage, possiblePayloads);
                }
                elementPayloads.Add(elementPayload);
            }

            var arrayExtent = checked(commonOuter.Count
                * commonOuter.Stride);
            if (coverage.Offset > arrayExtent
                || coverage.Length > arrayExtent - coverage.Offset)
            {
                return false;
            }

            var normalizedElementPayloads = CanonicalizePayloadRanges(
                elementPayloads.MoveToImmutable());
            var remainingBytes = coverage.Length;
            var elementOffset = coverage.Offset % commonOuter.Stride;
            if (elementOffset != 0)
            {
                var prefixLength = Math.Min(remainingBytes,
                    commonOuter.Stride - elementOffset);
                if (!IsCompactRankZeroCoverageCoveredByPayloadUnion(
                        new TrRmReturnCoverageRegion(elementOffset,
                            prefixLength, []),
                        normalizedElementPayloads, depth + 1))
                {
                    return false;
                }

                remainingBytes -= prefixLength;
            }

            var completeElementCount
                = remainingBytes / commonOuter.Stride;
            if (completeElementCount != 0
                && !IsCompactRankZeroCoverageCoveredByPayloadUnion(
                    new TrRmReturnCoverageRegion(0,
                        commonOuter.Stride, []),
                    normalizedElementPayloads, depth + 1))
            {
                return false;
            }

            var suffixLength = remainingBytes % commonOuter.Stride;
            return suffixLength == 0
                || IsCompactRankZeroCoverageCoveredByPayloadUnion(
                    new TrRmReturnCoverageRegion(0, suffixLength, []),
                    normalizedElementPayloads, depth + 1);
        }

        private bool IsRankZeroCoverageCoveredByPeriodicPayloadUnion(
            TrRmReturnCoverageRegion coverage,
            ImmutableArray<TrRmReturnCoverageRegion> possiblePayloads)
        {
            if (possiblePayloads.IsDefaultOrEmpty)
            {
                return false;
            }

            var period = BigInteger.One;
            var periodicStart = 0UL;
            foreach (var payload in possiblePayloads)
            {
                ConsumeWork();
                var repetitions = payload.Repetitions;
                if (repetitions.IsDefaultOrEmpty)
                {
                    throw ReturnCoverageResourceLimit(
                        EstimateCoverageWork(coverage));
                }

                var outer = repetitions[^1];
                if (outer.Stride == 0)
                {
                    return false;
                }

                periodicStart = Math.Max(periodicStart,
                    payload.Offset - (payload.Offset % outer.Stride));
                var stride = new BigInteger(outer.Stride);
                period = period / BigInteger.GreatestCommonDivisor(
                    period, stride) * stride;
                if (period > ValueLayout.MaximumAddressStorageSize)
                {
                    throw ReturnCoverageResourceLimit(period);
                }
            }

            var periodSize = checked((ulong)period);
            var periodicEnd = ulong.MaxValue;
            foreach (var payload in possiblePayloads)
            {
                ConsumeWork();
                var outer = payload.Repetitions[^1];
                var periodCount = periodSize / outer.Stride;
                if (outer.Count <= periodCount)
                {
                    throw ReturnCoverageResourceLimit(
                        EstimateCoverageWork(coverage));
                }

                var lastPeriodicStart = checked(payload.Offset
                    + ((outer.Count - periodCount) * outer.Stride));
                periodicEnd = Math.Min(periodicEnd, lastPeriodicStart);
            }

            if (periodicStart >= periodicEnd)
            {
                throw ReturnCoverageResourceLimit(
                    EstimateCoverageWork(coverage));
            }

            var coverageEnd = checked(coverage.Offset + coverage.Length);
            var prefixEnd = Math.Min(coverageEnd, periodicStart);
            if (prefixEnd > coverage.Offset
                && !IsSmallRankZeroSegmentCovered(coverage.Offset,
                    prefixEnd - coverage.Offset, possiblePayloads))
            {
                return false;
            }

            var periodicSegmentStart = Math.Max(coverage.Offset,
                periodicStart);
            var periodicSegmentEnd = Math.Min(coverageEnd, periodicEnd);
            if (periodicSegmentStart >= periodicSegmentEnd)
            {
                throw ReturnCoverageResourceLimit(
                    EstimateCoverageWork(coverage));
            }

            var periodicLength = periodicSegmentEnd
                - periodicSegmentStart;
            var wholePeriods = periodicLength / periodSize;
            var repeatedLength = wholePeriods * periodSize;
            var templateLength = wholePeriods == 0
                ? periodicLength : periodSize;
            if (!IsSmallRankZeroSegmentCovered(periodicSegmentStart,
                    templateLength, possiblePayloads))
            {
                return false;
            }

            var remainderLength = periodicLength - repeatedLength;
            if (remainderLength != 0
                && !IsSmallRankZeroSegmentCovered(
                    checked(periodicSegmentStart + repeatedLength),
                    remainderLength, possiblePayloads))
            {
                return false;
            }

            return periodicSegmentEnd >= coverageEnd
                || IsSmallRankZeroSegmentCovered(periodicSegmentEnd,
                    coverageEnd - periodicSegmentEnd, possiblePayloads);
        }

        private bool IsSmallRankZeroSegmentCovered(ulong offset,
            ulong length,
            ImmutableArray<TrRmReturnCoverageRegion> possiblePayloads)
        {
            if (length == 0)
            {
                return true;
            }
            if (length > ValueLayout.MaximumAddressStorageSize)
            {
                throw ReturnCoverageResourceLimit(new BigInteger(length));
            }

            var segment = new TrRmReturnCoverageRegion(offset, length, []);
            return IsSmallCoverageCoveredByPayloadUnion(segment,
                checked((int)length), possiblePayloads);
        }

        private static BigInteger EstimateCoverageWork(
            TrRmReturnCoverageRegion coverage)
        {
            var required = new BigInteger(coverage.Length);
            if (!coverage.Repetitions.IsDefault)
            {
                foreach (var repetition in coverage.Repetitions)
                {
                    required *= repetition.Count;
                }
            }

            return BigInteger.Max(required,
                ValueLayout.MaximumAddressStorageSize + 1);
        }

        private static TrRmResourceLimitException
            ReturnCoverageResourceLimit(BigInteger required)
            => new("validating ReturnCoverage payload inclusion",
                "MethodDef Contract 000F", "work",
                ValueLayout.MaximumAddressStorageSize,
                BigInteger.Max(required,
                    ValueLayout.MaximumAddressStorageSize + 1),
                moduleIdentity: null);

        private bool IsRepeatedCoverageCoveredByCommonOuterDimensions(
            TrRmReturnCoverageRegion coverage,
            ImmutableArray<TrRmReturnCoverageRegion> possiblePayloads)
        {
            if (coverage.Offset != 0)
            {
                return false;
            }

            var coverageRepetitions = coverage.Repetitions;
            var commonSuffixLength = 0;
            for (var candidateLength = coverageRepetitions.Length;
                candidateLength > 0;
                candidateLength--)
            {
                var matches = true;
                foreach (var payload in possiblePayloads)
                {
                    ConsumeWork();
                    var payloadRepetitions = payload.Repetitions;
                    if (payloadRepetitions.IsDefaultOrEmpty
                        || payloadRepetitions.Length < candidateLength)
                    {
                        matches = false;
                        break;
                    }

                    for (var dimension = 1;
                        dimension <= candidateLength;
                        dimension++)
                    {
                        ConsumeWork();
                        var coverageRepetition
                            = coverageRepetitions[
                                coverageRepetitions.Length - dimension];
                        var payloadRepetition
                            = payloadRepetitions[
                                payloadRepetitions.Length - dimension];
                        if (coverageRepetition.Stride
                                != payloadRepetition.Stride
                            || coverageRepetition.Count
                                > payloadRepetition.Count)
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (!matches)
                    {
                        break;
                    }

                    var outerStride
                        = coverageRepetitions[
                            coverageRepetitions.Length - 1].Stride;
                    if (payload.Offset >= outerStride)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    commonSuffixLength = candidateLength;
                    break;
                }
            }

            if (commonSuffixLength == 0)
            {
                return false;
            }

            var baseRepetitionCount = coverageRepetitions.Length
                - commonSuffixLength;
            var baseRepetitions
                = ImmutableArray.CreateBuilder<
                    TrRmReturnCoverageRepetition>(baseRepetitionCount);
            for (var dimension = 0;
                dimension < baseRepetitionCount;
                dimension++)
            {
                ConsumeWork();
                baseRepetitions.Add(coverageRepetitions[dimension]);
            }

            var baseCoverage = coverage with
            {
                Repetitions = baseRepetitions.MoveToImmutable(),
            };
            if (!TryGetSmallCoverageByteCount(baseCoverage,
                    out var byteCount))
            {
                return false;
            }

            var result = baseCoverage.Repetitions.IsDefaultOrEmpty
                ? IsCompactRankZeroCoverageCoveredByPayloadUnion(
                    baseCoverage, possiblePayloads, 0)
                : IsSmallCoverageCoveredByPayloadUnion(baseCoverage,
                    byteCount, possiblePayloads);
            return result;
        }

        private bool IsSmallCoverageCoveredByPayloadUnion(
            TrRmReturnCoverageRegion coverage, int byteCount,
            ImmutableArray<TrRmReturnCoverageRegion> possiblePayloads)
        {
            var repetitions = coverage.Repetitions.IsDefault
                ? []
                : coverage.Repetitions;
            var instanceCount = BigInteger.One;
            foreach (var repetition in repetitions)
            {
                ConsumeWork();
                instanceCount *= repetition.Count;
            }

            var instances = checked((int)instanceCount);
            ConsumeWork(checked((ulong)instances + (ulong)byteCount + 1UL));
            for (var instance = 0; instance < instances; instance++)
            {
                var remainingIndex = instance;
                var byteOffset = coverage.Offset;
                for (var dimension = 0;
                    dimension < repetitions.Length;
                    dimension++)
                {
                    ConsumeWork();
                    var repetition = repetitions[dimension];
                    var index = (ulong)remainingIndex
                        % repetition.Count;
                    remainingIndex = checked((int)(
                        (ulong)remainingIndex / repetition.Count));
                    byteOffset = checked(byteOffset
                        + (index * repetition.Stride));
                }

                var end = checked(byteOffset + coverage.Length);
                for (var currentByte = byteOffset;
                    currentByte < end;
                    currentByte++)
                {
                    ConsumeWork();
                    var isCovered = false;
                    foreach (var payload in possiblePayloads)
                    {
                        ConsumeWork();
                        if (PayloadContainsByte(payload, currentByte))
                        {
                            isCovered = true;
                            break;
                        }
                    }

                    if (!isCovered)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool TryGetSmallCoverageByteCount(
            TrRmReturnCoverageRegion region, out int byteCount)
        {
            var totalBytes = new BigInteger(region.Length);
            if (!region.Repetitions.IsDefault)
            {
                foreach (var repetition in region.Repetitions)
                {
                    ConsumeWork();
                    totalBytes *= repetition.Count;
                    if (totalBytes > ValueLayout.MaximumAddressStorageSize)
                    {
                        byteCount = 0;
                        return false;
                    }
                }
            }

            if (totalBytes > ValueLayout.MaximumAddressStorageSize)
            {
                byteCount = 0;
                return false;
            }

            byteCount = checked((int)totalBytes);
            return true;
        }

        private bool PayloadContainsByte(
            TrRmReturnCoverageRegion payload, ulong offset)
        {
            if (offset < payload.Offset)
            {
                return false;
            }

            var localOffset = offset - payload.Offset;
            var repetitions = payload.Repetitions;
            if (repetitions.IsDefaultOrEmpty)
            {
                return localOffset < payload.Length;
            }

            for (var dimension = repetitions.Length - 1;
                dimension >= 0;
                dimension--)
            {
                ConsumeWork();
                var repetition = repetitions[dimension];
                var index = localOffset / repetition.Stride;
                if (index >= repetition.Count)
                {
                    return false;
                }

                localOffset -= index * repetition.Stride;
            }

            return localOffset < payload.Length;
        }

        private bool ContainsPayloadRegion(
            TrRmReturnCoverageRegion payload,
            TrRmReturnCoverageRegion coverage)
        {
            var coverageSpan = GetRegionSpan(coverage);
            if (payload.Repetitions.IsDefaultOrEmpty)
            {
                return coverage.Offset >= payload.Offset
                    && coverage.Offset - payload.Offset <= payload.Length
                    && coverageSpan <= payload.Length
                        - (coverage.Offset - payload.Offset);
            }

            if (!TryGetPayloadPosition(payload, coverage.Offset,
                    out var coordinates, out var localOffset)
                || localOffset > payload.Length
                || coverage.Length > payload.Length - localOffset)
            {
                return false;
            }

            if (coverageSpan <= payload.Length - localOffset)
            {
                return true;
            }

            var payloadRepetitions = payload.Repetitions;
            var coverageRepetitions = coverage.Repetitions.IsDefault
                ? []
                : coverage.Repetitions;
            if (TryMapCoverageRepetitions(payloadRepetitions,
                    coverageRepetitions, coordinates))
            {
                return true;
            }

            return payloadRepetitions.Length == 1
                && coverageRepetitions.Length == 1
                && IsRankOneCoverageSubset(payload, coverage,
                    coordinates[0], localOffset);
        }

        private bool TryMapCoverageRepetitions(
            ImmutableArray<TrRmReturnCoverageRepetition> payloadRepetitions,
            ImmutableArray<TrRmReturnCoverageRepetition> coverageRepetitions,
            ulong[] coordinates)
        {
            if (coverageRepetitions.IsEmpty)
            {
                return false;
            }

            var maximumIncrements = new ulong[payloadRepetitions.Length];
            ConsumeWork(checked((ulong)maximumIncrements.Length + 1UL));
            foreach (var coverageRepetition in coverageRepetitions)
            {
                ConsumeWork(checked((ulong)payloadRepetitions.Length + 1UL));
                var remainingStride = coverageRepetition.Stride;
                for (var dimension = payloadRepetitions.Length - 1;
                    dimension >= 0;
                    dimension--)
                {
                    ConsumeWork();
                    var payloadStride
                        = payloadRepetitions[dimension].Stride;
                    var step = remainingStride / payloadStride;
                    remainingStride -= step * payloadStride;
                    maximumIncrements[dimension] = checked(
                        maximumIncrements[dimension]
                        + ((coverageRepetition.Count - 1) * step));
                }

                if (remainingStride != 0)
                {
                    return false;
                }
            }

            for (var dimension = 0;
                dimension < payloadRepetitions.Length;
                dimension++)
            {
                ConsumeWork();
                if (maximumIncrements[dimension]
                    >= payloadRepetitions[dimension].Count
                        - coordinates[dimension])
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsRankOneCoverageSubset(
            TrRmReturnCoverageRegion payload,
            TrRmReturnCoverageRegion coverage,
            ulong payloadIndex, ulong localOffset)
        {
            var payloadRepetition = payload.Repetitions[0];
            var coverageRepetition = coverage.Repetitions[0];
            if (coverage.Length > payload.Length)
            {
                return false;
            }

            var payloadExtent = checked(
                ((payloadRepetition.Count - 1) * payloadRepetition.Stride)
                + payload.Length);
            var coverageOffset = checked(
                (payloadIndex * payloadRepetition.Stride) + localOffset);
            var coverageSpan = GetRegionSpan(coverage);
            if (coverageOffset > payloadExtent
                || coverageSpan > payloadExtent - coverageOffset)
            {
                return false;
            }

            var residueLimit = checked(
                payload.Length - coverage.Length + 1UL);
            if (residueLimit >= payloadRepetition.Stride)
            {
                return true;
            }

            var count = new BigInteger(coverageRepetition.Count);
            var modulus = new BigInteger(payloadRepetition.Stride);
            var stride = new BigInteger(
                coverageRepetition.Stride % payloadRepetition.Stride);
            var start = new BigInteger(localOffset);
            var residuesAtOrAboveLimit
                = FloorSum(count, modulus, stride,
                    start + modulus - residueLimit)
                    - FloorSum(count, modulus, stride, start);
            return count - residuesAtOrAboveLimit == count;
        }

        private BigInteger FloorSum(BigInteger count, BigInteger modulus,
            BigInteger stride, BigInteger start)
        {
            var sum = BigInteger.Zero;
            while (true)
            {
                ConsumeWork();
                if (stride >= modulus)
                {
                    sum += (count - 1) * count * (stride / modulus) / 2;
                    stride %= modulus;
                }
                if (start >= modulus)
                {
                    sum += count * (start / modulus);
                    start %= modulus;
                }

                var maximum = (stride * count) + start;
                if (maximum < modulus)
                {
                    return sum;
                }

                count = maximum / modulus;
                start = maximum % modulus;
                (modulus, stride) = (stride, modulus);
            }
        }

        private static ulong GetRegionSpan(
            TrRmReturnCoverageRegion region)
        {
            var span = region.Length;
            if (!region.Repetitions.IsDefault)
            {
                foreach (var repetition in region.Repetitions)
                {
                    span = checked(((repetition.Count - 1)
                        * repetition.Stride) + span);
                }
            }

            return span;
        }

        private bool TryGetPayloadPosition(
            TrRmReturnCoverageRegion payload, ulong offset,
            out ulong[] coordinates, out ulong localOffset)
        {
            var repetitions = payload.Repetitions;
            if (offset < payload.Offset || repetitions.IsDefaultOrEmpty)
            {
                coordinates = [];
                localOffset = 0;
                return false;
            }

            ConsumeWork(checked((ulong)repetitions.Length + 1UL));
            coordinates = new ulong[repetitions.Length];
            localOffset = offset - payload.Offset;
            for (var dimension = repetitions.Length - 1;
                dimension >= 0;
                dimension--)
            {
                ConsumeWork();
                var repetition = repetitions[dimension];
                var index = localOffset / repetition.Stride;
                if (index >= repetition.Count)
                {
                    return false;
                }

                coordinates[dimension] = index;
                localOffset -= index * repetition.Stride;
            }

            return true;
        }

        private void AppendPayloadRanges(
            List<TrRmReturnCoverageRegion> destination,
            ImmutableArray<TrRmReturnCoverageRegion> payloads,
            ulong offset)
        {
            if (payloads.IsDefaultOrEmpty)
            {
                return;
            }

            ConsumeWork(checked((ulong)payloads.Length + 1UL));
            foreach (var payload in payloads)
            {
                ConsumeWork();
                destination.Add(payload with
                {
                    Offset = checked(payload.Offset + offset),
                });
            }
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
            if (trackDefinitionCycle)
            {
                ConsumeWork(2);
                if (_visitingTypeDefs.Contains(typeKey))
                {
                    throw InvalidLayout();
                }

                ConsumeWork();
                _ = _visitingTypeDefs.Add(typeKey);
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

                if (kind is not (1 or 2))
                {
                    throw InvalidLayout();
                }

                var declaredPack = _layouts.GetPack(layoutRow);
                ConsumeWork();
                var pack = (ulong)declaredPack;
                var explicitAlignment = _layouts.GetAlignment(layoutRow);
                var end = 0UL;
                var aggregateAlignment = 1UL;
                ConsumeWork();
                var requiredAlignment = Math.Max(1UL, explicitAlignment);
                var payloads = _schema.UsesRawUnionValueCoverage()
                    ? new List<TrRmReturnCoverageRegion>() : null;
                var containsRawUnion = kind == 2;
                for (var ordinal = 0; ordinal < fieldCount; ordinal++)
                {
                    ConsumeWork();
                    var field = _layoutFields.GetFieldDef(firstField + ordinal);
                    var fieldLayout = CalculateTypeLayout(
                        new TypeSpecContext(_fields.GetTypeSpecRef(field),
                            typeDefModule,
                            genericCount == 0 ? null : substitutions));
                    containsRawUnion |= fieldLayout.ContainsRawUnion;
                    ConsumeWork();
                    requiredAlignment = Math.Max(requiredAlignment,
                        fieldLayout.RequiredAlignment);
                    ConsumeWork();
                    if (fieldLayout.Size == 0)
                    {
                        continue;
                    }

                    ConsumeWork();
                    if (fieldLayout.Alignment == 0)
                    {
                        throw InvalidLayout();
                    }

                    ConsumeWork(2);
                    var placementAlignment = Math.Max(
                        Math.Min(fieldLayout.Alignment, pack),
                        fieldLayout.RequiredAlignment);
                    var fieldOffset = kind == 2 ? 0
                        : RoundUpTargetLayout(end, placementAlignment);
                    end = kind == 2
                        ? Math.Max(end, fieldLayout.Size)
                        : AddTargetLayout(fieldOffset, fieldLayout.Size);
                    if (payloads is not null)
                    {
                        AppendPayloadRanges(payloads,
                            fieldLayout.PossiblePayloads, fieldOffset);
                    }
                    ConsumeWork();
                    aggregateAlignment = Math.Max(aggregateAlignment,
                        placementAlignment);
                }

                ConsumeWork();
                if (explicitAlignment != 0)
                {
                    ConsumeWork();
                    if (explicitAlignment < aggregateAlignment)
                    {
                        throw InvalidLayout();
                    }

                    aggregateAlignment = explicitAlignment;
                }

                ConsumeWork();
                var size = RoundUpTargetLayout(end == 0 ? 1UL : end,
                    aggregateAlignment);
                ConsumeWork();
                requiredAlignment = Math.Max(requiredAlignment,
                    explicitAlignment);
                var canonicalPayloads = payloads is null ? default
                    : CanonicalizePayloadRanges(
                        [.. payloads]);
                return new TypeLayout(size, aggregateAlignment,
                    requiredAlignment, canonicalPayloads,
                    containsRawUnion);
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
                    ConsumeWork();
                    _ = _visitingTypeDefs.Remove(typeKey);
                }
            }
        }

        private bool HasCleanup(int typeDef)
        {
            var (first, count) = _typeDefs.GetContractRange(typeDef);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                ConsumeWork(sizeof(ushort) + 1UL);
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
