// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmStandardTypeProviderBinding(
    int ConsumerArtifactIndex, int ConsumerModuleRow, int ImportRow,
    int ExpectedTypeDefRow, int ProviderArtifactIndex, int ProviderModuleRow,
    int ProviderTypeDefRow, string ProducerIdentity, string ExportKey,
    uint TargetRequirement, uint RuntimePolicy);

internal static class TrRmFixedArrayProviderBindingValidator
{
    private const string FixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
    private const string FixedArrayEmptyInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1";
    private const string FixedArrayEmptyUnitInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
    private const string FixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
    private const string FixedArrayCopyReturnCoverageSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-1";
    private const string FixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1";
    private const string LocalAggregateLayoutSchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";
    private const string GenericFixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";

    internal static ImmutableArray<TrRmStandardTypeProviderBinding> Validate(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        byte[][] artifacts, string[] schemaIdentities,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int rootModuleRow)
    {
        var a3ResourceLedger = consumer.Schema
            == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? consumer.A3LayoutResourceLedger
                    ?? throw new InvalidOperationException(
                        "A3 provider validation requires the root request resource ledger.")
                : null;
        var context = new ValidationContext(consumer, artifacts,
            schemaIdentities, modules, a3ResourceLedger);
        try
        {
            return context.Validate(rootModuleRow);
        }
        catch (TrRmResourceLimitException)
        {
            throw;
        }
        catch (OverflowException exception)
            when (a3ResourceLedger is not null)
        {
            if (TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
                    out var kind)
                && kind
                    == TrRmA3AdmissionFailureKind.TargetLayoutOverflow)
            {
                throw;
            }

            var invalid = new InvalidDataException(
                "Invalid TRRM fixed-array TypeDef provider binding.",
                exception);
            a3ResourceLedger.AdmissionScope.Mark(invalid,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw invalid;
        }
        catch (InvalidDataException exception)
            when (a3ResourceLedger is not null)
        {
            if (!TrRmA3AdmissionFailureMarkers.TryGet(exception, out _))
            {
                a3ResourceLedger.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.InvalidArtifact);
            }

            throw;
        }
        catch (NotSupportedException exception)
            when (a3ResourceLedger is not null)
        {
            if (!TrRmA3AdmissionFailureMarkers.TryGet(exception, out _))
            {
                a3ResourceLedger.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.UnsupportedAdmission);
            }

            throw;
        }
    }

    private readonly record struct TypeOrigin(
        int ArtifactIndex, int ModuleRow, int TypeDefRow);

    private readonly record struct TypeOriginPair(
        TypeOrigin Expected, TypeOrigin Actual, bool IsInlineStorage,
        ulong Depth = 1);

    private readonly record struct TypeSpecLocation(
        int ArtifactIndex, int ModuleRow, uint TypeSpecRef,
        bool IsInlineStorage, ulong Depth = 1);

    private readonly record struct ImportResolution(
        TypeOrigin Expected, TypeOrigin Actual,
        TrRmStandardTypeProviderBinding Binding);

    private readonly record struct LayoutIdentity(
        uint ProfileId, uint RuleRevision, byte Kind, byte Flags,
        uint Alignment, uint Pack);

    private readonly record struct TypeSpecComparison(
        int LeftArtifactIndex, int LeftModuleRow, uint LeftTypeSpecRef,
        int RightArtifactIndex, int RightModuleRow, uint RightTypeSpecRef,
        bool IsInlineStorage, TypeOrigin? LeftFormalOwner = null,
        TypeOrigin? RightFormalOwner = null,
        bool AllowInitQualifier = false, ulong Depth = 1);

    private ref struct ValidationContext
    {
        private readonly TrRmStructuralMetadataValidator.ValidatedExecutionTables
            _consumer;
        private readonly byte[][] _artifacts;
        private readonly string[] _schemaIdentities;
        private readonly ImmutableArray<TrRmStandardModuleSelection> _modules;
        private readonly TrRmA3LayoutResourceLedger? _a3ResourceLedger;
        private readonly Dictionary<(int ArtifactIndex, int ImportRow),
            ImportResolution> _imports;
        private readonly HashSet<TypeOrigin> _visitedLocalTypes;
        private readonly HashSet<(TypeOrigin Expected, TypeOrigin Actual,
            bool IsInlineStorage)> _visitedTypePairs;
        private readonly HashSet<(int LeftArtifact, int LeftMethod,
            int RightArtifact, int RightMethod)> _activeAnchorPairs;
        private TrRmStructuralMetadataValidator.ValidatedExecutionTables
            _cachedProviderTables;
        private int _cachedProviderArtifactIndex;
        private bool _hasCachedProviderTables;

        internal ValidationContext(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
            byte[][] artifacts, string[] schemaIdentities,
            ImmutableArray<TrRmStandardModuleSelection> modules,
            TrRmA3LayoutResourceLedger? a3ResourceLedger)
        {
            _consumer = consumer;
            _artifacts = artifacts;
            _schemaIdentities = schemaIdentities;
            _modules = modules;
            _a3ResourceLedger = a3ResourceLedger;
            _a3ResourceLedger?.ConsumeWork(4);
            _imports = [];
            _visitedLocalTypes = [];
            _visitedTypePairs = [];
            _activeAnchorPairs = [];
            _cachedProviderTables = default;
            _cachedProviderArtifactIndex = -1;
            _hasCachedProviderTables = false;
        }

        internal ImmutableArray<TrRmStandardTypeProviderBinding> Validate(
            int rootModuleRow)
        {
            if (_artifacts.Length != _schemaIdentities.Length
                || _artifacts.Length == 0 || rootModuleRow <= 0
                || rootModuleRow > _consumer.Modules.Count)
            {
                throw InvalidBinding();
            }

            _a3ResourceLedger?.ConsumeWork();
            var pendingTypes = new Queue<TypeOriginPair>();
            for (var moduleIndex = 0; moduleIndex < _modules.Length;
                moduleIndex++)
            {
                var module = _modules[moduleIndex];
                if (!IsFixedArraySchemaIdentity(
                        _schemaIdentities[module.ArtifactIndex]))
                {
                    continue;
                }

                var moduleTables = GetTables(module.ArtifactIndex);
                for (var importRow = 1;
                    importRow <= moduleTables.Imports.Count; importRow++)
                {
                    ConsumeWork();
                    var moduleRefRow = moduleTables.Imports
                        .GetModuleRefForImportRow(importRow);
                    if (moduleTables.ModuleRefs.GetOwnerModule(moduleRefRow)
                        != module.ModuleRow)
                    {
                        continue;
                    }

                    if ((moduleTables.Imports
                            .GetExpectedDefinitionForImportRow(importRow)
                            >> 24) != 0x13)
                    {
                        continue;
                    }

                    var resolution = ResolveImport(module.ArtifactIndex,
                        module.ModuleRow, moduleTables, importRow);
                    AddTypePair(resolution.Expected, resolution.Actual,
                        isInlineStorage: false, pendingTypes);
                }

                var (typeDefStart, typeDefEnd)
                    = moduleTables.Modules.GetTypeDefRange(module.ModuleRow);
                for (var typeDefRow = typeDefStart;
                    typeDefRow < typeDefEnd; typeDefRow++)
                {
                    var (fieldStart, fieldEnd)
                        = moduleTables.TypeDefs.GetFieldRange(typeDefRow);
                    for (var fieldRow = fieldStart; fieldRow < fieldEnd;
                        fieldRow++)
                    {
                        ConsumeWork();
                        CollectStorageType(module.ArtifactIndex,
                            module.ModuleRow,
                            moduleTables.Fields.GetTypeSpecRef(fieldRow),
                            pendingTypes);
                    }
                }
            }

            for (var layoutRow = 1;
                layoutRow <= _consumer.FixedArrayLayouts.Count; layoutRow++)
            {
                ConsumeWork();
                var layout = _consumer.FixedArrayLayouts.GetRow(layoutRow);
                if (layout.OwnerModuleRow != rootModuleRow)
                {
                    continue;
                }

                _ = _consumer.TypeSpecs.TryGetFixedInlineArray(
                    (uint)layout.ArrayTypeSpecRow,
                    out var elementTypeSpecRef, out _);
                CollectStorageType(0, rootModuleRow,
                    elementTypeSpecRef, pendingTypes);
            }

            while (pendingTypes.TryDequeue(out var pair))
            {
                ConsumeWork();
                _a3ResourceLedger?.CheckDepth(pair.Depth);
                ValidateTypePair(pair, pendingTypes);
            }

            ConsumeWork(checked((ulong)_imports.Count + 1UL));
            var orderedBindings
                = new List<TrRmStandardTypeProviderBinding>(_imports.Count);
            foreach (var resolution in _imports.Values)
            {
                orderedBindings.Add(resolution.Binding);
            }

            var resourceLedger = _a3ResourceLedger;
            orderedBindings.Sort((left, right) =>
            {
                resourceLedger?.ConsumeWork();
                var result = left.ConsumerArtifactIndex
                    .CompareTo(right.ConsumerArtifactIndex);
                if (result != 0)
                {
                    return result;
                }

                resourceLedger?.ConsumeWork();
                return left.ImportRow.CompareTo(right.ImportRow);
            });

            ConsumeWork(checked((ulong)orderedBindings.Count + 1UL));
            return [.. orderedBindings];
        }

        private void CollectStorageType(int artifactIndex, int moduleRow,
            uint typeSpecRef, Queue<TypeOriginPair> pendingTypes)
        {
            ConsumeWork();
            var pending = new Stack<TypeSpecLocation>();
            PushLocation(pending, new TypeSpecLocation(artifactIndex,
                moduleRow, typeSpecRef, IsInlineStorage: true));
            while (pending.TryPop(out var location))
            {
                ConsumeWork();
                _a3ResourceLedger?.CheckDepth(location.Depth);
                var tables = GetTables(location.ArtifactIndex);
                var reference = tables.TypeSpecs.GetTypeSpecRef(
                    location.TypeSpecRef);
                if (!reference.IsRow)
                {
                    if (reference.TypeCode == 0x8C)
                    {
                        continue;
                    }

                    continue;
                }

                var row = tables.TypeSpecs.GetRow(reference.Row);
                switch (reference.Form)
                {
                    case 0x01:
                        break;

                    case 0x02:
                        if (location.IsInlineStorage)
                        {
                            var typeDefRow = checked((int)Read(row, 2));
                            AddTypePair(new TypeOrigin(
                                    location.ArtifactIndex,
                                    tables.TypeDefs.GetOwnerModule(typeDefRow),
                                    typeDefRow),
                                new TypeOrigin(location.ArtifactIndex,
                                    tables.TypeDefs.GetOwnerModule(typeDefRow),
                                    typeDefRow),
                                isInlineStorage: true, pendingTypes,
                                location.Depth);
                            CollectLocalType(location.ArtifactIndex,
                                typeDefRow, pending, location.Depth);
                        }
                        break;

                    case 0x03:
                    {
                        var importRow = checked((int)Read(row, 2));
                        var resolution = ResolveImport(location.ArtifactIndex,
                            location.ModuleRow, tables, importRow);
                        AddTypePair(resolution.Expected, resolution.Actual,
                            location.IsInlineStorage, pendingTypes,
                            location.Depth);
                        break;
                    }

                    // Resolve a formal only in its constructed owner/actual context.
                    case 0x04:
                        break;

                    case 0x05:
                    {
                        var definitionRef = Read(row, 2);
                        var definition = tables.TypeSpecs.GetTypeSpecRef(
                            definitionRef);
                        var definitionRow = tables.TypeSpecs.GetRow(
                            definition.Row);
                        var origin = ResolveNominalType(tables,
                            location.ArtifactIndex, location.ModuleRow,
                            definition, definitionRow);
                        AddTypePair(origin.Expected, origin.Actual,
                            location.IsInlineStorage, pendingTypes,
                            location.Depth);
                        var argumentCount
                            = tables.TypeSpecs.GetConstructedArgumentCount(
                                reference.Row);
                        for (var ordinal = argumentCount - 1;
                            ordinal >= 0; ordinal--)
                        {
                            PushLocation(pending, new TypeSpecLocation(
                                location.ArtifactIndex, location.ModuleRow,
                                tables.TypeSpecs.GetConstructedArgument(
                                    reference.Row, ordinal),
                                IsInlineStorage: false,
                                Depth: checked(location.Depth + 1)));
                        }
                        break;
                    }

                    case >= 0x06 and <= 0x0B:
                    case 0x0E:
                        PushLocation(pending, new TypeSpecLocation(
                            location.ArtifactIndex, location.ModuleRow,
                            Read(row, 2), IsInlineStorage: false,
                            Depth: checked(location.Depth + 1)));
                        break;

                    case 0x0C:
                        if (location.IsInlineStorage
                            && reference.TypeCode != 0x8C)
                        {
                            throw InvalidBinding();
                        }

                        PushLocation(pending, new TypeSpecLocation(
                            location.ArtifactIndex, location.ModuleRow,
                            Read(row, 3), IsInlineStorage: false,
                            Depth: checked(location.Depth + 1)));
                        break;

                    case 0x0D:
                        break;

                    case 0x11:
                        if (!tables.TargetProfiles.TryGetProfile(
                                TrRmTargetProfileTableReader
                                    .WindowsX64NativeProfileId,
                                TrRmTargetProfileTableReader
                                    .WindowsX64NativeProfileRuleRevision,
                                out var profileRow)
                            || tables.FixedArrayLayouts.FindRow(
                                location.ModuleRow, location.TypeSpecRef,
                                (uint)profileRow) == 0)
                        {
                            throw InvalidBinding();
                        }

                        PushLocation(pending, new TypeSpecLocation(
                            location.ArtifactIndex, location.ModuleRow,
                            Read(row, 2), IsInlineStorage: true,
                            Depth: checked(location.Depth + 1)));
                        break;

                    default:
                        throw InvalidBinding();
                }
            }
        }

        private void CollectLocalType(int artifactIndex, int typeDefRow,
            Stack<TypeSpecLocation> pending, ulong depth)
        {
            ConsumeWork();
            _a3ResourceLedger?.CheckDepth(depth);
            var origin = new TypeOrigin(artifactIndex,
                GetTables(artifactIndex).TypeDefs.GetOwnerModule(typeDefRow),
                typeDefRow);
            ConsumeWork(2);
            if (!_visitedLocalTypes.Add(origin))
            {
                return;
            }

            var tables = GetTables(artifactIndex);
            if (_a3ResourceLedger is null)
            {
                _ = TrRmFixedArrayLayoutValidator.CalculateTypeDefLayout(
                    tables, typeDefRow);
            }
            else
            {
                using var depthContext
                    = _a3ResourceLedger.EnterDepthContext(depth);
                _ = TrRmFixedArrayLayoutValidator.CalculateTypeDefLayout(
                    tables, typeDefRow);
            }
            var layout = GetWindowsLayout(tables, typeDefRow);
            var (fieldFirst, fieldCount)
                = tables.Layouts.GetLayoutFieldRange(layout);
            for (var ordinal = fieldCount - 1; ordinal >= 0; ordinal--)
            {
                ConsumeWork();
                var field = tables.LayoutFields.GetFieldDef(
                    fieldFirst + ordinal);
                PushLocation(pending, new TypeSpecLocation(artifactIndex,
                    origin.ModuleRow,
                    tables.Fields.GetTypeSpecRef(field),
                    IsInlineStorage: true,
                    Depth: checked(depth + 1)));
            }
        }

        private void ValidateTypePair(TypeOriginPair pair,
            Queue<TypeOriginPair> pendingTypes)
        {
            ConsumeWork();
            _a3ResourceLedger?.CheckDepth(pair.Depth);
            ConsumeWork(2);
            if (!_visitedTypePairs.Add((pair.Expected, pair.Actual,
                    pair.IsInlineStorage)))
            {
                return;
            }

            var expected = GetTables(pair.Expected.ArtifactIndex);
            var actual = GetTables(pair.Actual.ArtifactIndex);
            var expectedType = pair.Expected.TypeDefRow;
            var actualType = pair.Actual.TypeDefRow;
            var expectedKind = expected.TypeDefs.GetKind(expectedType);
            var actualKind = actual.TypeDefs.GetKind(actualType);
            var (expectedFormalFirst, expectedFormalCount)
                = expected.TypeDefs.GetGenericRange(expectedType);
            var (actualFormalFirst, actualFormalCount)
                = actual.TypeDefs.GetGenericRange(actualType);
            ConsumeWork(4);
            if (expected.TypeDefs.GetOwnerModule(expectedType)
                    != pair.Expected.ModuleRow
                || actual.TypeDefs.GetOwnerModule(actualType)
                    != pair.Actual.ModuleRow
                || expectedKind is not (1 or 2)
                || expectedKind != actualKind
                || expected.TypeDefs.GetFlags(expectedType)
                    != actual.TypeDefs.GetFlags(actualType)
                || !TypeNamesMatch(expected, expectedType,
                    actual, actualType)
                || expectedFormalCount != actualFormalCount
                || (pair.IsInlineStorage
                    && (expectedKind != 1
                        || !expected.TypeDefs.HasDeclaredCopy(expectedType)
                        || !actual.TypeDefs.HasDeclaredCopy(actualType))))
            {
                throw InvalidBinding();
            }

            if (expectedFormalCount != 0
                && (!IsGenericFixedArraySchemaIdentity(
                        _schemaIdentities[pair.Expected.ArtifactIndex])
                    || !IsGenericFixedArraySchemaIdentity(
                        _schemaIdentities[pair.Actual.ArtifactIndex])))
            {
                throw InvalidBinding();
            }

            if (pair.IsInlineStorage)
            {
                if (expectedFormalCount == 0)
                {
                    using var depthContext = _a3ResourceLedger is null
                        ? default
                        : _a3ResourceLedger.EnterDepthContext(pair.Depth);
                    var expectedLayout
                        = TrRmFixedArrayLayoutValidator
                            .CalculateTypeDefLayout(expected, expectedType);
                    var actualLayout
                        = TrRmFixedArrayLayoutValidator
                            .CalculateTypeDefLayout(actual, actualType);
                    if (expectedLayout != actualLayout)
                    {
                        throw InvalidBinding();
                    }
                }
                else if (!expected.Layouts.HasBlittableLayout(
                            expectedType, 1, 1)
                    || !actual.Layouts.HasBlittableLayout(actualType, 1, 1))
                {
                    throw InvalidBinding();
                }
            }

            if (expectedFormalCount != 0
                && !GenericFormalsMatch(pair.Expected, pair.Actual,
                    expected, actual, expectedFormalFirst,
                    actualFormalFirst, expectedFormalCount, pendingTypes,
                    checked(pair.Depth + 1)))
            {
                throw InvalidBinding();
            }

            var (expectedFirst, expectedEnd)
                = expected.TypeDefs.GetFieldRange(expectedType);
            var (actualFirst, actualEnd)
                = actual.TypeDefs.GetFieldRange(actualType);
            if (expectedEnd - expectedFirst != actualEnd - actualFirst)
            {
                throw InvalidBinding();
            }

            for (var ordinal = 0; ordinal < expectedEnd - expectedFirst;
                ordinal++)
            {
                ConsumeWork();
                var expectedField = expectedFirst + ordinal;
                var actualField = actualFirst + ordinal;
                if (expected.Fields.GetFlags(expectedField)
                        != actual.Fields.GetFlags(actualField)
                    || !FieldNamesMatch(expected, expectedField,
                        actual, actualField)
                    || !TypeSpecsMatch(expected, pair.Expected.ArtifactIndex,
                        pair.Expected.ModuleRow,
                        expected.Fields.GetTypeSpecRef(expectedField),
                        actual, pair.Actual.ArtifactIndex, pair.Actual.ModuleRow,
                        actual.Fields.GetTypeSpecRef(actualField),
                        isInlineStorage:
                            (expected.Fields.GetFlags(expectedField) & 8) == 0,
                        pendingTypes,
                        leftFormalOwner: expectedFormalCount == 0
                            ? null : pair.Expected,
                        rightFormalOwner: actualFormalCount == 0
                            ? null : pair.Actual,
                        depth: checked(pair.Depth + 1)))
                {
                    throw InvalidBinding();
                }
            }

            if (!LayoutsMatch(pair.Expected, pair.Actual, expected, actual,
                    pendingTypes,
                    expectedFormalCount == 0 ? null : pair.Expected,
                    actualFormalCount == 0 ? null : pair.Actual,
                    checked(pair.Depth + 1)))
            {
                throw InvalidBinding();
            }
        }

        private readonly bool TypeNamesMatch(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
            int expectedType,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
            int actualType)
        {
            var expectedNamespace = expected.TypeDefs.GetNamespace(expectedType);
            var actualNamespace = actual.TypeDefs.GetNamespace(actualType);
            ConsumeWork(GetStringComparisonWork(
                expectedNamespace, actualNamespace));
            if (!StringComparer.Ordinal.Equals(
                    expectedNamespace, actualNamespace))
            {
                return false;
            }

            var expectedName = expected.TypeDefs.GetName(expectedType);
            var actualName = actual.TypeDefs.GetName(actualType);
            ConsumeWork(GetStringComparisonWork(expectedName, actualName));
            return StringComparer.Ordinal.Equals(expectedName, actualName);
        }

        private readonly bool FieldNamesMatch(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
            int expectedField,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
            int actualField)
        {
            var expectedName = expected.Fields.GetName(expectedField);
            var actualName = actual.Fields.GetName(actualField);
            ConsumeWork(GetStringComparisonWork(expectedName, actualName));
            return StringComparer.Ordinal.Equals(expectedName, actualName);
        }

        private bool GenericFormalsMatch(TypeOrigin expectedOrigin,
            TypeOrigin actualOrigin,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
            int expectedFormalFirst, int actualFormalFirst, int formalCount,
            Queue<TypeOriginPair> pendingTypes, ulong depth)
        {
            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                ConsumeWork();
                var expectedFormal = expectedFormalFirst + ordinal;
                var actualFormal = actualFormalFirst + ordinal;
                if (expected.GenericParams.GetOwnerDefId(expectedFormal)
                        != (0x1300_0000u | (uint)expectedOrigin.TypeDefRow)
                    || actual.GenericParams.GetOwnerDefId(actualFormal)
                        != (0x1300_0000u | (uint)actualOrigin.TypeDefRow)
                    || expected.GenericParams.GetOrdinal(expectedFormal)
                        != actual.GenericParams.GetOrdinal(actualFormal)
                    || expected.GenericParams.GetFormalKind(expectedFormal)
                        != actual.GenericParams.GetFormalKind(actualFormal)
                    || expected.GenericParams.GetOwnershipDomain(
                        expectedFormal)
                        != actual.GenericParams.GetOwnershipDomain(actualFormal)
                    || expected.GenericParams.GetNullabilityDomain(
                        expectedFormal)
                        != actual.GenericParams.GetNullabilityDomain(actualFormal)
                    || expected.GenericParams.GetConstantTypeSpecRef(
                        expectedFormal)
                        != actual.GenericParams.GetConstantTypeSpecRef(
                            actualFormal))
                {
                    return false;
                }

                var expectedDefault
                    = expected.GenericParams.GetDefaultArgumentRef(
                        expectedFormal);
                var actualDefault
                    = actual.GenericParams.GetDefaultArgumentRef(actualFormal);
                var expectedHasDefault = expectedDefault != 0;
                var actualHasDefault = actualDefault != 0;
                if (expectedHasDefault != actualHasDefault
                    || (expectedDefault != 0
                        && !TypeSpecsMatch(expected,
                            expectedOrigin.ArtifactIndex,
                            expectedOrigin.ModuleRow, expectedDefault,
                            actual, actualOrigin.ArtifactIndex,
                            actualOrigin.ModuleRow, actualDefault,
                            isInlineStorage: false, pendingTypes,
                            expectedOrigin, actualOrigin,
                            checked(depth + 1))))
                {
                    return false;
                }

                var (expectedConstraintFirst, expectedConstraintCount)
                    = expected.GenericParams.GetConstraintRange(expectedFormal);
                var (actualConstraintFirst, actualConstraintCount)
                    = actual.GenericParams.GetConstraintRange(actualFormal);
                if (expectedConstraintCount != actualConstraintCount)
                {
                    return false;
                }

                for (var constraintOrdinal = 0;
                    constraintOrdinal < expectedConstraintCount;
                    constraintOrdinal++)
                {
                    ConsumeWork();
                    var expectedConstraint
                        = expectedConstraintFirst + constraintOrdinal;
                    var actualConstraint
                        = actualConstraintFirst + constraintOrdinal;
                    if (expected.Constraints.GetKind(expectedConstraint)
                            != actual.Constraints.GetKind(actualConstraint)
                        || expected.Constraints.GetPayload(expectedConstraint)
                            != actual.Constraints.GetPayload(actualConstraint))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool LayoutsMatch(TypeOrigin expectedOrigin,
            TypeOrigin actualOrigin,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
            Queue<TypeOriginPair> pendingTypes,
            TypeOrigin? leftFormalOwner = null,
            TypeOrigin? rightFormalOwner = null, ulong depth = 1)
        {
            var (expectedFirst, expectedCount)
                = expected.TypeDefs.GetLayoutRange(expectedOrigin.TypeDefRow);
            var (actualFirst, actualCount)
                = actual.TypeDefs.GetLayoutRange(actualOrigin.TypeDefRow);
            if (expectedCount != actualCount)
            {
                return false;
            }

            ConsumeWork(checked((ulong)actualCount + 1UL));
            var actualRows = new Dictionary<LayoutIdentity, int>(actualCount);
            for (var ordinal = 0; ordinal < actualCount; ordinal++)
            {
                ConsumeWork();
                var row = actualFirst + ordinal;
                ConsumeWork(2);
                if (!actualRows.TryAdd(GetLayoutIdentity(actual, row), row))
                {
                    return false;
                }
            }

            for (var ordinal = 0; ordinal < expectedCount; ordinal++)
            {
                ConsumeWork();
                var expectedLayout = expectedFirst + ordinal;
                var identity = GetLayoutIdentity(expected, expectedLayout);
                ConsumeWork();
                if (!actualRows.Remove(identity, out var actualLayout))
                {
                    return false;
                }

                var (expectedFields, expectedFieldCount)
                    = expected.Layouts.GetLayoutFieldRange(expectedLayout);
                var (actualFields, actualFieldCount)
                    = actual.Layouts.GetLayoutFieldRange(actualLayout);
                if (expectedFieldCount != actualFieldCount)
                {
                    return false;
                }

                var (expectedTypeFields, expectedTypeFieldEnd)
                    = expected.TypeDefs.GetFieldRange(
                        expectedOrigin.TypeDefRow);
                var (actualTypeFields, actualTypeFieldEnd)
                    = actual.TypeDefs.GetFieldRange(actualOrigin.TypeDefRow);
                for (var fieldOrdinal = 0;
                    fieldOrdinal < expectedFieldCount; fieldOrdinal++)
                {
                    ConsumeWork();
                    var expectedRow = expectedFields + fieldOrdinal;
                    var actualRow = actualFields + fieldOrdinal;
                    var expectedField = expected.LayoutFields.GetFieldDef(
                        expectedRow);
                    var actualField = actual.LayoutFields.GetFieldDef(
                        actualRow);
                    if (expected.LayoutFields.GetOrdinal(expectedRow)
                            != actual.LayoutFields.GetOrdinal(actualRow)
                        || expectedField < expectedTypeFields
                        || expectedField >= expectedTypeFieldEnd
                        || actualField < actualTypeFields
                        || actualField >= actualTypeFieldEnd
                        || expectedField - expectedTypeFields
                            != actualField - actualTypeFields
                        || !TypeSpecsMatch(expected,
                            expectedOrigin.ArtifactIndex,
                            expectedOrigin.ModuleRow,
                            expected.Fields.GetTypeSpecRef(expectedField),
                            actual, actualOrigin.ArtifactIndex,
                            actualOrigin.ModuleRow,
                            actual.Fields.GetTypeSpecRef(actualField),
                            isInlineStorage: true, pendingTypes,
                            leftFormalOwner, rightFormalOwner,
                            depth: checked(depth + 1)))
                    {
                        return false;
                    }
                }
            }

            return actualRows.Count == 0;
        }

        private bool TypeSpecsMatch(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables leftTables,
            int leftArtifactIndex, int leftModuleRow, uint leftTypeSpecRef,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables rightTables,
            int rightArtifactIndex, int rightModuleRow, uint rightTypeSpecRef,
            bool isInlineStorage,
            Queue<TypeOriginPair> pendingTypes,
            TypeOrigin? leftFormalOwner = null,
            TypeOrigin? rightFormalOwner = null, ulong depth = 1)
        {
            ConsumeWork();
            var pending = new Stack<TypeSpecComparison>();
            PushComparison(pending, new TypeSpecComparison(leftArtifactIndex,
                leftModuleRow, leftTypeSpecRef, rightArtifactIndex,
                rightModuleRow, rightTypeSpecRef, isInlineStorage,
                leftFormalOwner, rightFormalOwner, Depth: depth));
            while (pending.TryPop(out var comparison))
            {
                ConsumeWork();
                _a3ResourceLedger?.CheckDepth(comparison.Depth);
                var left = leftTables.TypeSpecs.GetTypeSpecRef(
                    comparison.LeftTypeSpecRef,
                    allowInitQualifier: comparison.AllowInitQualifier);
                var right = rightTables.TypeSpecs.GetTypeSpecRef(
                    comparison.RightTypeSpecRef,
                    allowInitQualifier: comparison.AllowInitQualifier);
                var leftIsNominalType = left.Form is 0x02 or 0x03;
                var rightIsNominalType = right.Form is 0x02 or 0x03;
                if (left.Qualifiers != right.Qualifiers
                    || (left.Form != right.Form
                        && !(leftIsNominalType && rightIsNominalType)))
                {
                    return false;
                }

                if (!left.IsRow || !right.IsRow)
                {
                    if (left.Form == 0x01
                        && left.TypeCode == right.TypeCode
                        && left.ArgumentCode == right.ArgumentCode)
                    {
                        continue;
                    }

                    if (left.Form == 0x0F)
                    {
                        continue;
                    }

                    if (left.IsRow || right.IsRow)
                    {
                        return false;
                    }

                    return false;
                }

                var leftRow = leftTables.TypeSpecs.GetRow(left.Row);
                var rightRow = rightTables.TypeSpecs.GetRow(right.Row);
                switch (left.Form)
                {
                    case 0x01:
                        if (left.TypeCode != right.TypeCode
                            || left.ArgumentCode != right.ArgumentCode)
                        {
                            return false;
                        }
                        break;

                    case 0x02:
                    case 0x03:
                    {
                        var leftOrigin = ResolveNominalType(
                            leftTables,
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, left, leftRow);
                        var rightOrigin = ResolveNominalType(
                            rightTables,
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, right, rightRow);
                        if (leftOrigin.Actual != rightOrigin.Actual)
                        {
                            return false;
                        }

                        AddTypePair(leftOrigin.Expected, rightOrigin.Actual,
                            comparison.IsInlineStorage, pendingTypes,
                            comparison.Depth);
                        AddTypePair(rightOrigin.Expected, rightOrigin.Actual,
                            comparison.IsInlineStorage, pendingTypes,
                            comparison.Depth);
                        break;
                    }

                    case 0x04:
                    {
                        if (comparison.LeftFormalOwner is not TypeOrigin
                                leftFormalTypeOwner
                            || comparison.RightFormalOwner is not TypeOrigin
                                rightFormalTypeOwner)
                        {
                            return false;
                        }

                        var leftFormal = checked((int)Read(leftRow, 2));
                        var rightFormal = checked((int)Read(rightRow, 2));
                        var leftOwner = leftTables.GenericParams
                            .GetOwnerDefId(leftFormal);
                        var rightOwner = rightTables.GenericParams
                            .GetOwnerDefId(rightFormal);
                        if (leftOwner != (0x1300_0000u
                                | (uint)leftFormalTypeOwner.TypeDefRow)
                            || rightOwner != (0x1300_0000u
                                | (uint)rightFormalTypeOwner.TypeDefRow)
                            || leftTables.TypeDefs.GetOwnerModule(
                                leftFormalTypeOwner.TypeDefRow)
                                != leftFormalTypeOwner.ModuleRow
                            || rightTables.TypeDefs.GetOwnerModule(
                                rightFormalTypeOwner.TypeDefRow)
                                != rightFormalTypeOwner.ModuleRow
                            || leftTables.GenericParams.GetOrdinal(leftFormal)
                                != rightTables.GenericParams.GetOrdinal(
                                    rightFormal)
                            || leftTables.GenericParams.GetFormalKind(
                                leftFormal)
                                != rightTables.GenericParams.GetFormalKind(
                                    rightFormal)
                            || leftTables.GenericParams.GetOwnershipDomain(
                                leftFormal)
                                != rightTables.GenericParams.GetOwnershipDomain(
                                    rightFormal)
                            || leftTables.GenericParams.GetNullabilityDomain(
                                leftFormal)
                                != rightTables.GenericParams.GetNullabilityDomain(
                                    rightFormal))
                        {
                            return false;
                        }
                        break;
                    }

                    case >= 0x06 and <= 0x0B:
                    case 0x0E:
                        PushComparison(pending, new TypeSpecComparison(
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, Read(leftRow, 2),
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, Read(rightRow, 2),
                            IsInlineStorage: false,
                            comparison.LeftFormalOwner,
                            comparison.RightFormalOwner,
                            Depth: checked(comparison.Depth + 1)));
                        break;

                    case 0x0C:
                        ConsumeWork(5);
                        if (leftRow[2] != rightRow[2])
                        {
                            return false;
                        }

                        PushComparison(pending, new TypeSpecComparison(
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, Read(leftRow, 3),
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, Read(rightRow, 3),
                            IsInlineStorage: false,
                            comparison.LeftFormalOwner,
                            comparison.RightFormalOwner,
                            AllowInitQualifier: left.TypeCode == 0x8A,
                            Depth: checked(comparison.Depth + 1)));
                        break;

                    case 0x0D:
                        if (!FunctionPointerAnchorsMatch(comparison,
                                leftTables, rightTables, pendingTypes))
                        {
                            return false;
                        }
                        break;
                    case 0x10:
                        if (leftRow.Length != rightRow.Length)
                        {
                            return false;
                        }

                        ConsumeWork(checked((ulong)leftRow.Length * 3));
                        if (!leftRow.SequenceEqual(rightRow))
                        {
                            return false;
                        }
                        break;

                    case 0x05:
                    {
                        var leftDefinitionRef = Read(leftRow, 2);
                        var rightDefinitionRef = Read(rightRow, 2);
                        var leftDefinition
                            = leftTables.TypeSpecs.GetTypeSpecRef(
                                leftDefinitionRef);
                        var rightDefinition
                            = rightTables.TypeSpecs.GetTypeSpecRef(
                                rightDefinitionRef);
                        var leftDefinitionRow
                            = leftTables.TypeSpecs.GetRow(leftDefinition.Row);
                        var rightDefinitionRow
                            = rightTables.TypeSpecs.GetRow(rightDefinition.Row);
                        var leftOrigin = ResolveNominalType(leftTables,
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, leftDefinition,
                            leftDefinitionRow);
                        var rightOrigin = ResolveNominalType(rightTables,
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, rightDefinition,
                            rightDefinitionRow);
                        var leftActualTables
                            = GetTables(leftOrigin.Actual.ArtifactIndex);
                        var rightActualTables
                            = GetTables(rightOrigin.Actual.ArtifactIndex);
                        var leftFormalCount
                            = leftActualTables.TypeDefs.GetGenericRange(
                                leftOrigin.Actual.TypeDefRow).Count;
                        var rightFormalCount
                            = rightActualTables.TypeDefs.GetGenericRange(
                                rightOrigin.Actual.TypeDefRow).Count;
                        var leftArgumentCount
                            = leftTables.TypeSpecs.GetConstructedArgumentCount(
                                left.Row);
                        var rightArgumentCount
                            = rightTables.TypeSpecs.GetConstructedArgumentCount(
                                right.Row);
                        if (leftOrigin.Actual != rightOrigin.Actual
                            || leftFormalCount != rightFormalCount
                            || leftArgumentCount != leftFormalCount
                            || rightArgumentCount != rightFormalCount)
                        {
                            return false;
                        }

                        AddTypePair(leftOrigin.Expected,
                            rightOrigin.Actual, comparison.IsInlineStorage,
                            pendingTypes, comparison.Depth);
                        AddTypePair(rightOrigin.Expected,
                            rightOrigin.Actual, comparison.IsInlineStorage,
                            pendingTypes, comparison.Depth);
                        for (var ordinal = leftArgumentCount - 1;
                            ordinal >= 0; ordinal--)
                        {
                            ConsumeWork();
                            PushComparison(pending, new TypeSpecComparison(
                                comparison.LeftArtifactIndex,
                                comparison.LeftModuleRow,
                                leftTables.TypeSpecs.GetConstructedArgument(
                                    left.Row, ordinal),
                                comparison.RightArtifactIndex,
                                comparison.RightModuleRow,
                                rightTables.TypeSpecs.GetConstructedArgument(
                                    right.Row, ordinal),
                                IsInlineStorage: false,
                                comparison.LeftFormalOwner,
                                comparison.RightFormalOwner,
                                Depth: checked(comparison.Depth + 1)));
                        }
                        break;
                    }

                    case 0x0F:
                        break;

                    case 0x11:
                        ConsumeWork(19);
                        if (BinaryPrimitives.ReadUInt64LittleEndian(
                                leftRow[6..])
                            != BinaryPrimitives.ReadUInt64LittleEndian(
                                rightRow[6..]))
                        {
                            return false;
                        }

                        PushComparison(pending, new TypeSpecComparison(
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, Read(leftRow, 2),
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, Read(rightRow, 2),
                            comparison.IsInlineStorage,
                            comparison.LeftFormalOwner,
                            comparison.RightFormalOwner,
                            Depth: checked(comparison.Depth + 1)));
                        break;

                    default:
                        return false;
                }
            }

            return true;
        }

        private bool FunctionPointerAnchorsMatch(
            TypeSpecComparison comparison,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables left,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables right,
            Queue<TypeOriginPair> pendingTypes)
        {
            var leftTypeSpecRow = left.TypeSpecs.GetTypeSpecRef(
                comparison.LeftTypeSpecRef).Row;
            var rightTypeSpecRow = right.TypeSpecs.GetTypeSpecRef(
                comparison.RightTypeSpecRef).Row;
            var leftAnchors = left.NativeFunctionPointerAnchors;
            var rightAnchors = right.NativeFunctionPointerAnchors;
            if (!leftAnchors.HasAnchorForTypeSpec(leftTypeSpecRow)
                || !rightAnchors.HasAnchorForTypeSpec(rightTypeSpecRow))
            {
                return false;
            }

            var leftMethod = leftAnchors.GetAnchorMethodDefRow(
                leftTypeSpecRow);
            var rightMethod = rightAnchors.GetAnchorMethodDefRow(
                rightTypeSpecRow);
            var anchorPair = (comparison.LeftArtifactIndex, leftMethod,
                comparison.RightArtifactIndex, rightMethod);
            ConsumeWork(2);
            if (leftAnchors.GetCallingConvention(leftTypeSpecRow)
                    != rightAnchors.GetCallingConvention(rightTypeSpecRow)
                || !_activeAnchorPairs.Add(anchorPair))
            {
                return false;
            }

            try
            {
                var leftSignature = left.Methods.GetSignature(leftMethod);
                var rightSignature = right.Methods.GetSignature(rightMethod);
                if (left.Signatures.GetReceiverTypeSpec(leftSignature) != 0
                    || right.Signatures.GetReceiverTypeSpec(rightSignature) != 0
                    || left.Signatures.GetGenericCount(leftSignature) != 0
                    || right.Signatures.GetGenericCount(rightSignature) != 0
                    || !TypeSpecsMatch(left, comparison.LeftArtifactIndex,
                        left.Methods.GetOwnerModule(leftMethod),
                        left.Signatures.GetResultTypeSpec(leftSignature),
                        right, comparison.RightArtifactIndex,
                        right.Methods.GetOwnerModule(rightMethod),
                        right.Signatures.GetResultTypeSpec(rightSignature),
                        isInlineStorage: false, pendingTypes,
                        depth: checked(comparison.Depth + 1)))
                {
                    return false;
                }

                var (leftFirst, leftCount)
                    = left.Signatures.GetParamRange(leftSignature);
                var (rightFirst, rightCount)
                    = right.Signatures.GetParamRange(rightSignature);
                if (leftCount != rightCount)
                {
                    return false;
                }

                for (var ordinal = 0; ordinal < leftCount; ordinal++)
                {
                    ConsumeWork();
                    var leftParameter = leftFirst + ordinal;
                    var rightParameter = rightFirst + ordinal;
                    if (left.Parameters.GetMode(leftParameter)
                            != right.Parameters.GetMode(rightParameter)
                        || left.Parameters.GetFlags(leftParameter)
                            != right.Parameters.GetFlags(rightParameter)
                        || !TypeSpecsMatch(left,
                            comparison.LeftArtifactIndex,
                            left.Methods.GetOwnerModule(leftMethod),
                            left.Parameters.GetTypeSpec(leftParameter),
                            right, comparison.RightArtifactIndex,
                            right.Methods.GetOwnerModule(rightMethod),
                            right.Parameters.GetTypeSpec(rightParameter),
                            isInlineStorage: false, pendingTypes,
                            depth: checked(comparison.Depth + 1)))
                    {
                        return false;
                    }
                }

                return leftAnchors.AreEquivalentAnchorContracts(leftMethod,
                    rightAnchors, rightMethod);
            }
            finally
            {
                ConsumeWork();
                _ = _activeAnchorPairs.Remove(anchorPair);
            }
        }

        private TypeOriginPair ResolveNominalType(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int artifactIndex, int moduleRow, TrRmTypeSpecRefInfo reference,
            ReadOnlySpan<byte> row)
        {
            if (reference.Form == 0x02)
            {
                var typeDefRow = checked((int)Read(row, 2));
                return new TypeOriginPair(
                    new TypeOrigin(artifactIndex,
                        tables.TypeDefs.GetOwnerModule(typeDefRow),
                        typeDefRow),
                    new TypeOrigin(artifactIndex,
                        tables.TypeDefs.GetOwnerModule(typeDefRow),
                        typeDefRow), false);
            }

            if (reference.Form == 0x03)
            {
                var resolution = ResolveImport(artifactIndex, moduleRow,
                    tables,
                    checked((int)Read(row, 2)));
                return new TypeOriginPair(resolution.Expected,
                    resolution.Actual, false);
            }

            throw InvalidBinding();
        }

        private ImportResolution ResolveImport(int artifactIndex,
            int moduleRow,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
            int importRow)
        {
            ConsumeWork();
            var key = (artifactIndex, importRow);
            ConsumeWork(2);
            if (_imports.TryGetValue(key, out var existing))
            {
                if (existing.Expected.ModuleRow != moduleRow)
                {
                    throw InvalidBinding();
                }

                return existing;
            }

            var expectedDefinition
                = consumer.Imports.GetExpectedDefinitionForImportRow(importRow);
            if ((expectedDefinition >> 24) != 0x13)
            {
                throw InvalidBinding();
            }

            var expectedTypeDefRow = checked((int)(
                expectedDefinition & 0x00FF_FFFF));
            var expectedGenericCount
                = consumer.TypeDefs.GetGenericRange(expectedTypeDefRow).Count;
            var expectedRequiresGenericSchema = expectedGenericCount != 0
                || consumer.TypeDefs.HasDeclaredUnique(expectedTypeDefRow);
            var expectedModule
                = consumer.TypeDefs.GetOwnerModule(expectedTypeDefRow);
            var moduleRefRow
                = consumer.Imports.GetModuleRefForImportRow(importRow);
            var binding = consumer.Imports.GetBindingForImportRow(importRow);
            if (expectedModule != moduleRow
                || (expectedRequiresGenericSchema
                    && !IsGenericFixedArraySchemaIdentity(
                        _schemaIdentities[artifactIndex]))
                || consumer.ModuleRefs.GetOwnerModule(moduleRefRow) != moduleRow)
            {
                throw InvalidBinding();
            }

            var producerIdentity
                = consumer.ModuleRefs.GetProducerIdentity(moduleRefRow);
            ConsumeWork(GetStringComparisonWork(producerIdentity,
                binding.ProducerIdentity));
            if (!StringComparer.Ordinal.Equals(producerIdentity,
                    binding.ProducerIdentity)
                || consumer.ModuleRefs.GetRequirements(moduleRefRow)
                    != (binding.TargetRequirement, binding.RuntimePolicy))
            {
                throw InvalidBinding();
            }

            var selected = FindModule(binding.ProducerIdentity);
            if ((selected.ArtifactIndex == artifactIndex
                    && selected.ModuleRow == moduleRow)
                || !IsFixedArraySchemaIdentity(
                    _schemaIdentities[selected.ArtifactIndex]))
            {
                throw InvalidBinding();
            }

            var provider = GetTables(selected.ArtifactIndex);
            if (provider.Modules.GetRequirements(selected.ModuleRow)
                    != (binding.TargetRequirement, binding.RuntimePolicy)
                || !provider.Exports.TryGetDefinitionForKey(
                    selected.ModuleRow, binding.ExportKey,
                    out var providerDefinition)
                || (providerDefinition >> 24) != 0x13)
            {
                throw InvalidBinding();
            }

            var providerTypeDefRow = checked((int)(
                providerDefinition & 0x00FF_FFFF));
            if (providerTypeDefRow <= 0
                || providerTypeDefRow > provider.TypeDefs.Count
                || provider.TypeDefs.GetOwnerModule(providerTypeDefRow)
                    != selected.ModuleRow
                || ((expectedGenericCount != 0
                    || provider.TypeDefs.GetGenericRange(
                        providerTypeDefRow).Count != 0
                    || consumer.TypeDefs.HasDeclaredUnique(expectedTypeDefRow)
                    || provider.TypeDefs.HasDeclaredUnique(providerTypeDefRow))
                    && (!IsGenericFixedArraySchemaIdentity(
                            _schemaIdentities[artifactIndex])
                        || !IsGenericFixedArraySchemaIdentity(
                            _schemaIdentities[selected.ArtifactIndex]))))
            {
                throw InvalidBinding();
            }

            var providerBinding = new TrRmStandardTypeProviderBinding(
                artifactIndex, moduleRow, importRow, expectedTypeDefRow,
                selected.ArtifactIndex, selected.ModuleRow,
                providerTypeDefRow, binding.ProducerIdentity,
                binding.ExportKey, binding.TargetRequirement,
                binding.RuntimePolicy);
            var result = new ImportResolution(
                new TypeOrigin(artifactIndex, expectedModule,
                    expectedTypeDefRow),
                new TypeOrigin(selected.ArtifactIndex, selected.ModuleRow,
                    providerTypeDefRow),
                providerBinding);
            ConsumeWork(2);
            _imports.Add(key, result);
            return result;
        }

        private readonly TrRmStandardModuleSelection FindModule(
            string identity)
        {
            TrRmStandardModuleSelection? selected = null;
            foreach (var module in _modules)
            {
                ConsumeWork();
                ConsumeWork(GetStringComparisonWork(module.Identity,
                    identity));
                if (!StringComparer.Ordinal.Equals(module.Identity, identity))
                {
                    continue;
                }

                if (selected is not null)
                {
                    throw InvalidBinding();
                }

                selected = module;
            }

            return selected ?? throw InvalidBinding();
        }

        private readonly void AddTypePair(TypeOrigin expected,
            TypeOrigin actual,
            bool isInlineStorage, Queue<TypeOriginPair> pendingTypes,
            ulong depth = 1)
        {
            var pair = (expected, actual, isInlineStorage);
            ConsumeWork(2);
            if (!_visitedTypePairs.Contains(pair))
            {
                _a3ResourceLedger?.ReserveWorkAtDepth(1, depth);
                pendingTypes.Enqueue(new TypeOriginPair(expected, actual,
                    isInlineStorage, depth));
            }
        }

        private readonly void PushLocation(Stack<TypeSpecLocation> pending,
            TypeSpecLocation location)
        {
            _a3ResourceLedger?.ReserveWorkAtDepth(1, location.Depth);
            pending.Push(location);
        }

        private readonly void PushComparison(Stack<TypeSpecComparison> pending,
            TypeSpecComparison comparison)
        {
            _a3ResourceLedger?.ReserveWorkAtDepth(1, comparison.Depth);
            pending.Push(comparison);
        }

        private readonly LayoutIdentity GetLayoutIdentity(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int layoutRow)
        {
            ConsumeWork(sizeof(uint) + 1);
            var profileRow = tables.Layouts.GetTargetProfileRef(layoutRow);
            ConsumeWork();
            var profile = profileRow == 0
                ? default
                : tables.TargetProfiles.GetProfile(checked((int)profileRow));
            ConsumeWork(2);
            var kind = tables.Layouts.GetLayoutKind(layoutRow);
            ConsumeWork(2);
            var flags = tables.Layouts.GetFlags(layoutRow);
            ConsumeWork(sizeof(uint) + 1);
            var alignment = tables.Layouts.GetAlignment(layoutRow);
            ConsumeWork(sizeof(uint) + 1);
            var pack = tables.Layouts.GetPack(layoutRow);
            return new LayoutIdentity(profile.ProfileId,
                profile.RuleRevision, kind, flags, alignment, pack);
        }

        private readonly int GetWindowsLayout(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int typeDefRow)
        {
            ConsumeWork();
            if (!tables.TargetProfiles.TryGetProfile(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision,
                    out var profileRow))
            {
                throw InvalidBinding();
            }

            ConsumeWork();
            var layoutRow = tables.Layouts.GetLayoutForProfile(typeDefRow,
                (uint)profileRow);
            ConsumeWork(sizeof(uint) + 1);
            if (tables.Layouts.GetTargetProfileRef(layoutRow)
                != (uint)profileRow)
            {
                throw InvalidBinding();
            }

            return layoutRow;
        }

        private TrRmStructuralMetadataValidator.ValidatedExecutionTables
            GetTables(int artifactIndex)
        {
            ConsumeWork();
            if (artifactIndex == 0)
            {
                return _consumer;
            }

            if (artifactIndex <= 0 || artifactIndex >= _artifacts.Length
                || !IsFixedArraySchemaIdentity(
                    _schemaIdentities[artifactIndex]))
            {
                throw InvalidBinding();
            }

            if (_hasCachedProviderTables
                && _cachedProviderArtifactIndex == artifactIndex)
            {
                return _cachedProviderTables;
            }

            // Nested substitutions revisit immutable provider snapshots.
            var schema = GetFixedArraySchema(_schemaIdentities[artifactIndex]);
            var index = _a3ResourceLedger is not null
                ? new TrRmArtifactIndex(_artifacts[artifactIndex], schema,
                    _a3ResourceLedger)
                : schema == TrRmArtifactSchema.LocalAggregateLayoutDraft
                ? TrRmArtifactIndex.ForLocalAggregateLayoutDraft(
                    _artifacts[artifactIndex])
                : schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        _artifacts[artifactIndex])
                : schema
                    .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        _artifacts[artifactIndex])
                : schema.UsesRawUnionValueCoverage()
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageDraft(
                        _artifacts[artifactIndex])
                : schema.UsesScalarFrozenValues()
                    ? TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                            _artifacts[artifactIndex])
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    ? TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                            _artifacts[artifactIndex])
                : schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    ? TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                            _artifacts[artifactIndex])
                : TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        _artifacts[artifactIndex]);
            var tables = schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    index, _a3ResourceLedger,
                    createA3ResourceLedger: _consumer.Schema
                        == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft)
                : schema
                .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index)
                : schema.UsesRawUnionValueCoverage()
                ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageDraftAndGetExecutionTables(
                        index)
                : schema.UsesScalarFrozenValues()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                            index)
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                            index)
                : schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                            index)
                : TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                        index, _a3ResourceLedger);
            _cachedProviderTables = tables;
            _cachedProviderArtifactIndex = artifactIndex;
            _hasCachedProviderTables = true;
            return tables;
        }

        private readonly bool IsFixedArraySchemaIdentity(
            string schemaIdentity)
        {
            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArraySchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArraySchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArrayEmptyInitializationSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyInitializationSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArrayEmptyUnitInitializationSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyUnitInitializationSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArrayCopyReturnCoverageSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayCopyReturnCoverageSchemaIdentity))
            {
                return true;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                LocalAggregateLayoutSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                LocalAggregateLayoutSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                GenericFixedArraySchemaIdentity));
            return StringComparer.Ordinal.Equals(schemaIdentity,
                GenericFixedArraySchemaIdentity);
        }

        private readonly bool IsGenericFixedArraySchemaIdentity(
            string schemaIdentity)
        {
            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                GenericFixedArraySchemaIdentity));
            return StringComparer.Ordinal.Equals(schemaIdentity,
                GenericFixedArraySchemaIdentity);
        }

        private readonly TrRmArtifactSchema GetFixedArraySchema(
            string schemaIdentity)
        {
            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                GenericFixedArraySchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                GenericFixedArraySchemaIdentity))
            {
                return TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyInitializationSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyUnitInitializationSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayCopyReturnCoverageSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                LocalAggregateLayoutSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                LocalAggregateLayoutSchemaIdentity))
            {
                return TrRmArtifactSchema.LocalAggregateLayoutDraft;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArraySchemaIdentity));
            return StringComparer.Ordinal.Equals(schemaIdentity,
                    FixedArraySchemaIdentity)
                ? TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                : throw InvalidBinding();
        }

        private readonly uint Read(ReadOnlySpan<byte> row, int offset)
        {
            ConsumeWork(sizeof(uint) + 1);
            return BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(offset,
                sizeof(uint)));
        }

        private readonly void ConsumeWork(ulong units = 1)
            => _a3ResourceLedger?.ConsumeWork(units);

        private static ulong GetStringComparisonWork(string left,
            string right)
        {
            var characterCount
                = checked((ulong)left.Length + (ulong)right.Length);
            var payloadBytes = checked(characterCount * sizeof(char));
            return checked(payloadBytes + 1UL);
        }

        private static InvalidDataException InvalidBinding()
            => new("Invalid TRRM fixed-array TypeDef provider binding.");
    }
}
