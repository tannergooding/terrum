// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Composes provisional TRRM (1, 0) section and cross-table structural checks.</summary>
/// <remarks>Does not verify executable Body instructions, target ABI layouts, executable contracts or import binding.</remarks>
public static class TrRmStructuralMetadataValidator
{
    internal readonly ref struct ValidatedExecutionTables
    {
        internal ValidatedExecutionTables(TrRmStringTableReader strings,
            TrRmFeatureTableReader features,
            TrRmModuleTableReader modules, TrRmModuleRefTableReader moduleRefs,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors,
            TrRmGenericParamTableReader genericParams,
            TrRmConstraintTableReader constraints,
            TrRmInterfaceDefTableReader interfaces,
            TrRmAssociatedTypeDefTableReader associatedTypeDefs,
            TrRmInterfaceWitnessTableReader interfaceWitnesses,
            TrRmWitnessMemberTableReader witnessMembers,
            TrRmWitnessAssociatedTypeTableReader witnessAssociatedTypes,
            TrRmInterfaceBaseTableReader interfaceBases,
            TrRmInterfaceMethodTableReader interfaceMethods,
            TrRmInterfaceConstDefTableReader interfaceConstDefs,
            TrRmAssociatedConstDefTableReader associatedConstDefs,
            TrRmAssociatedRelationTableReader associatedRelations,
            TrRmUnionCaseDefTableReader unionCaseDefs,
            TrRmUnionCaseFieldTableReader unionCaseFields,
            TrRmBuilderRegistrationTableReader builderRegistrations,
            TrRmTextTargetTableReader textTargets,
            TrRmTextContentShapeTableReader textContentShapes,
            TrRmTextCaptureShapeTableReader textCaptureShapes,
            TrRmTextFragmentShapeTableReader textFragmentShapes,
            TrRmPredicateGraphTableReader predicateGraphs,
            TrRmPredicateChildEdgeTableReader predicateChildEdges,
            TrRmPredicateReadFootprintTableReader predicateReadFootprints,
            TrRmPredicateCaptureTableReader predicateCaptures,
            TrRmBlobTableReader blobs,
            TrRmFrozenValueNodeTableReader frozenValueNodes,
            TrRmConstantSourceTableReader constantSources,
            TrRmTargetLayoutDependencyTableReader targetLayoutDependencies,
            TrRmTargetLayoutDependencyFieldTableReader targetLayoutDependencyFields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmFieldDefTableReader fields,
            TrRmLayoutTableReader layouts, TrRmLayoutFieldTableReader layoutFields,
            TrRmTargetProfileTableReader targetProfiles,
            TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
            TrRmMethodDefTableReader methods, TrRmBodyTableReader bodies,
            TrRmContractTableReader contracts,
            TrRmDevelopmentNativeImportTableReader nativeImports,
            TrRmNativeExportTableReader nativeExports,
            TrRmNativeCallableTableReader nativeCallables,
            TrRmNativeFunctionPointerSignatureTableReader nativeFunctionPointerSignatures,
            TrRmNativeVTableTableReader nativeVTables, TrRmImportTableReader imports,
            TrRmArtifactSchema schema)
        {
            Strings = strings;
            Features = features;
            Modules = modules;
            ModuleRefs = moduleRefs;
            TypeDefs = typeDefs;
            TypeSpecs = typeSpecs;
            NativeFunctionPointerAnchors = nativeFunctionPointerAnchors;
            GenericParams = genericParams;
            Constraints = constraints;
            Interfaces = interfaces;
            AssociatedTypeDefs = associatedTypeDefs;
            InterfaceWitnesses = interfaceWitnesses;
            WitnessMembers = witnessMembers;
            WitnessAssociatedTypes = witnessAssociatedTypes;
            InterfaceBases = interfaceBases;
            InterfaceMethods = interfaceMethods;
            InterfaceConstDefs = interfaceConstDefs;
            AssociatedConstDefs = associatedConstDefs;
            AssociatedRelations = associatedRelations;
            UnionCaseDefs = unionCaseDefs;
            UnionCaseFields = unionCaseFields;
            BuilderRegistrations = builderRegistrations;
            TextTargets = textTargets;
            TextContentShapes = textContentShapes;
            TextCaptureShapes = textCaptureShapes;
            TextFragmentShapes = textFragmentShapes;
            PredicateGraphs = predicateGraphs;
            PredicateChildEdges = predicateChildEdges;
            PredicateReadFootprints = predicateReadFootprints;
            PredicateCaptures = predicateCaptures;
            Blobs = blobs;
            FrozenValueNodes = frozenValueNodes;
            ConstantSources = constantSources;
            TargetLayoutDependencies = targetLayoutDependencies;
            TargetLayoutDependencyFields = targetLayoutDependencyFields;
            FieldPaths = fieldPaths;
            Fields = fields;
            Layouts = layouts;
            LayoutFields = layoutFields;
            TargetProfiles = targetProfiles;
            Signatures = signatures;
            Parameters = parameters;
            Methods = methods;
            Bodies = bodies;
            Contracts = contracts;
            NativeImports = nativeImports;
            NativeExports = nativeExports;
            NativeCallables = nativeCallables;
            NativeFunctionPointerSignatures = nativeFunctionPointerSignatures;
            NativeVTables = nativeVTables;
            Imports = imports;
            Schema = schema;
        }

        internal TrRmStringTableReader Strings { get; }
        internal TrRmFeatureTableReader Features { get; }
        internal TrRmModuleTableReader Modules { get; }
        internal TrRmModuleRefTableReader ModuleRefs { get; }
        internal TrRmTypeDefTableReader TypeDefs { get; }
        internal TrRmTypeSpecTableReader TypeSpecs { get; }
        internal TrRmNativeFunctionPointerAnchorTableReader NativeFunctionPointerAnchors { get; }
        internal TrRmGenericParamTableReader GenericParams { get; }
        internal TrRmConstraintTableReader Constraints { get; }
        internal TrRmInterfaceDefTableReader Interfaces { get; }
        internal TrRmAssociatedTypeDefTableReader AssociatedTypeDefs { get; }
        internal TrRmInterfaceWitnessTableReader InterfaceWitnesses { get; }
        internal TrRmWitnessMemberTableReader WitnessMembers { get; }
        internal TrRmWitnessAssociatedTypeTableReader WitnessAssociatedTypes { get; }
        internal TrRmInterfaceBaseTableReader InterfaceBases { get; }
        internal TrRmInterfaceMethodTableReader InterfaceMethods { get; }
        internal TrRmInterfaceConstDefTableReader InterfaceConstDefs { get; }
        internal TrRmAssociatedConstDefTableReader AssociatedConstDefs { get; }
        internal TrRmAssociatedRelationTableReader AssociatedRelations { get; }
        internal TrRmUnionCaseDefTableReader UnionCaseDefs { get; }
        internal TrRmUnionCaseFieldTableReader UnionCaseFields { get; }
        internal TrRmBuilderRegistrationTableReader BuilderRegistrations { get; }
        internal TrRmTextTargetTableReader TextTargets { get; }
        internal TrRmTextContentShapeTableReader TextContentShapes { get; }
        internal TrRmTextCaptureShapeTableReader TextCaptureShapes { get; }
        internal TrRmTextFragmentShapeTableReader TextFragmentShapes { get; }
        internal TrRmPredicateGraphTableReader PredicateGraphs { get; }
        internal TrRmPredicateChildEdgeTableReader PredicateChildEdges { get; }
        internal TrRmPredicateReadFootprintTableReader PredicateReadFootprints { get; }
        internal TrRmPredicateCaptureTableReader PredicateCaptures { get; }
        internal TrRmBlobTableReader Blobs { get; }
        internal TrRmFrozenValueNodeTableReader FrozenValueNodes { get; }
        internal TrRmConstantSourceTableReader ConstantSources { get; }
        internal TrRmTargetLayoutDependencyTableReader TargetLayoutDependencies { get; }
        internal TrRmTargetLayoutDependencyFieldTableReader TargetLayoutDependencyFields { get; }
        internal TrRmFieldPathTableReader FieldPaths { get; }
        internal TrRmFieldDefTableReader Fields { get; }
        internal TrRmLayoutTableReader Layouts { get; }
        internal TrRmLayoutFieldTableReader LayoutFields { get; }
        internal TrRmTargetProfileTableReader TargetProfiles { get; }
        internal TrRmMethodSigTableReader Signatures { get; }
        internal TrRmParamTableReader Parameters { get; }
        internal TrRmMethodDefTableReader Methods { get; }
        internal TrRmBodyTableReader Bodies { get; }
        internal TrRmContractTableReader Contracts { get; }
        internal TrRmDevelopmentNativeImportTableReader NativeImports { get; }
        internal TrRmNativeExportTableReader NativeExports { get; }
        internal TrRmNativeCallableTableReader NativeCallables { get; }
        internal TrRmNativeFunctionPointerSignatureTableReader NativeFunctionPointerSignatures { get; }
        internal TrRmNativeVTableTableReader NativeVTables { get; }
        internal TrRmImportTableReader Imports { get; }
        internal TrRmArtifactSchema Schema { get; }

        internal void ValidateRegisterBodyMapsForMethod(int methodDefRow)
        {
            if (Schema != TrRmArtifactSchema.Standard
                || methodDefRow <= 0 || methodDefRow > Methods.Count)
            {
                throw InvalidMetadata();
            }

            var bodyRow = Methods.GetBodyRow(methodDefRow);
            if (bodyRow == 0)
            {
                throw InvalidMetadata();
            }

            var body = Bodies.GetRegisterBody(bodyRow, Contracts.Count);
            TrRmRegisterBodyMapValidator.Validate(body,
                methodDefRow, Methods.GetOwnerModule(methodDefRow), Features,
                Modules, TypeDefs, TypeSpecs, GenericParams, Methods, Signatures,
                Parameters, Fields, FieldPaths, ConstantSources, FrozenValueNodes,
                Blobs, Contracts, Imports, NativeFunctionPointerAnchors, ModuleRefs);
        }

        internal void ValidateNonBodyTypeSpecModuleContexts()
        {
            if (Schema != TrRmArtifactSchema.Standard)
            {
                return;
            }

            var visitedTypeSpecRows = new HashSet<int>[Modules.Count + 1];
            for (var typeDef = 1; typeDef <= TypeDefs.Count; typeDef++)
            {
                var ownerModule = TypeDefs.GetOwnerModule(typeDef);
                var (firstField, endField) = TypeDefs.GetFieldRange(typeDef);
                for (var field = firstField; field < endField; field++)
                {
                    ValidateTypeSpecModuleContext(Fields.GetTypeSpecRef(field),
                        ownerModule, visitedTypeSpecRows);
                }
            }

            for (var method = 1; method <= Methods.Count; method++)
            {
                var ownerModule = Methods.GetOwnerModule(method);
                var signature = Methods.GetSignature(method);
                ValidateTypeSpecModuleContext(
                    Signatures.GetResultTypeSpec(signature), ownerModule,
                    visitedTypeSpecRows);
                ValidateTypeSpecModuleContext(
                    Signatures.GetReceiverTypeSpec(signature), ownerModule,
                    visitedTypeSpecRows);
                var (firstParameter, parameterCount) =
                    Signatures.GetParamRange(signature);
                for (var parameter = 0; parameter < parameterCount; parameter++)
                {
                    ValidateTypeSpecModuleContext(
                        Parameters.GetTypeSpec(firstParameter + parameter),
                        ownerModule, visitedTypeSpecRows);
                }
            }

            for (var formal = 1; formal <= GenericParams.Count; formal++)
            {
                var ownerModule = GetGenericParamOwnerModule(formal);
                if (GenericParams.GetFormalKind(formal) == 1)
                {
                    var defaultType = GenericParams.GetDefaultArgumentRef(formal);
                    if (defaultType != 0)
                    {
                        ValidateTypeSpecModuleContext(defaultType, ownerModule,
                            visitedTypeSpecRows);
                    }
                }
                else
                {
                    ValidateTypeSpecModuleContext(
                        GenericParams.GetConstantTypeSpecRef(formal), ownerModule,
                        visitedTypeSpecRows);
                }
            }

            for (var constraint = 1; constraint <= Constraints.Count; constraint++)
            {
                if (Constraints.GetKind(constraint) != 1)
                {
                    continue;
                }

                var formal = Constraints.GetOwnerGenericParam(constraint);
                ValidateTypeSpecModuleContext(Constraints.GetPayload(constraint),
                    GetGenericParamOwnerModule(formal), visitedTypeSpecRows);
            }

            for (var row = 1; row <= InterfaceBases.Count; row++)
            {
                var ownerInterface = InterfaceBases.GetOwnerInterfaceDef(row);
                var ownerType = Interfaces.GetOwnerTypeDef(ownerInterface);
                ValidateTypeSpecModuleContext(InterfaceBases.GetBaseTypeSpecRef(row),
                    TypeDefs.GetOwnerModule(ownerType), visitedTypeSpecRows);
            }

            for (var row = 1; row <= AssociatedTypeDefs.Count; row++)
            {
                var defaultType = AssociatedTypeDefs.GetDefaultTypeSpecRef(row);
                if (defaultType == 0)
                {
                    continue;
                }

                var ownerInterface = AssociatedTypeDefs.GetOwnerInterfaceDef(row);
                var ownerType = Interfaces.GetOwnerTypeDef(ownerInterface);
                ValidateTypeSpecModuleContext(defaultType,
                    TypeDefs.GetOwnerModule(ownerType), visitedTypeSpecRows);
            }

            for (var row = 1; row <= InterfaceConstDefs.Count; row++)
            {
                var ownerInterface = InterfaceConstDefs.GetOwnerInterfaceDef(row);
                var ownerType = Interfaces.GetOwnerTypeDef(ownerInterface);
                ValidateTypeSpecModuleContext(
                    InterfaceConstDefs.GetConstantTypeSpecRef(row),
                    TypeDefs.GetOwnerModule(ownerType), visitedTypeSpecRows);
            }

            for (var row = 1; row <= AssociatedConstDefs.Count; row++)
            {
                var ownerInterface = AssociatedConstDefs.GetOwnerInterfaceDef(row);
                var ownerType = Interfaces.GetOwnerTypeDef(ownerInterface);
                ValidateTypeSpecModuleContext(
                    AssociatedConstDefs.GetConstantTypeSpecRef(row),
                    TypeDefs.GetOwnerModule(ownerType), visitedTypeSpecRows);
            }

            for (var row = 1; row <= UnionCaseFields.Count; row++)
            {
                var caseDef = UnionCaseFields.GetOwnerUnionCaseDef(row);
                var ownerType = UnionCaseDefs.GetOwnerTypeDef(caseDef);
                ValidateTypeSpecModuleContext(UnionCaseFields.GetTypeSpecRef(row),
                    TypeDefs.GetOwnerModule(ownerType), visitedTypeSpecRows);
            }

            for (var row = 1; row <= BuilderRegistrations.Count; row++)
            {
                var ownerModule = TypeDefs.GetOwnerModule(
                    BuilderRegistrations.GetTargetTypeDef(row));
                ValidateTypeSpecModuleContext(
                    BuilderRegistrations.GetSchemaTypeSpecRef(row), ownerModule,
                    visitedTypeSpecRows);
                ValidateTypeSpecModuleContext(
                    BuilderRegistrations.GetRecipeTypeSpecRef(row), ownerModule,
                    visitedTypeSpecRows);
            }

            for (var row = 1; row <= TextTargets.Count; row++)
            {
                var ownerMethod = TextTargets.GetOwnerMethodDef(row);
                ValidateTypeSpecModuleContext(TextTargets.GetSchemaTypeSpecRef(row),
                    Methods.GetOwnerModule(ownerMethod), visitedTypeSpecRows);
            }

            for (var row = 1; row <= TextCaptureShapes.Count; row++)
            {
                var shape = TextCaptureShapes.GetShapeRow(row);
                var ownerType = TextContentShapes.GetGeneratedTypeDef(shape);
                ValidateTypeSpecModuleContext(TextCaptureShapes.GetTypeSpecRef(row),
                    TypeDefs.GetOwnerModule(ownerType), visitedTypeSpecRows);
            }
        }

        private void ValidateTypeSpecModuleContext(uint typeSpecRef, int ownerModule,
            HashSet<int>[] visitedTypeSpecRows)
        {
            if (typeSpecRef == 0)
            {
                return;
            }

            var visited = visitedTypeSpecRows[ownerModule];
            if (visited is null)
            {
                visited = new HashSet<int>();
                visitedTypeSpecRows[ownerModule] = visited;
            }

            TrRmConstantSourceValueValidator.ValidateConstructedTypeSpecValues(
                typeSpecRef, ownerModule, TypeSpecs, TypeDefs, GenericParams,
                Imports, ConstantSources, FrozenValueNodes, Blobs, Features,
                Modules, visited);
        }

        private int GetGenericParamOwnerModule(int formal)
        {
            var owner = GenericParams.GetOwnerDefId(formal);
            var ownerRow = checked((int)(owner & 0x00FF_FFFF));
            return (byte)(owner >> 24) switch
            {
                0x13 => TypeDefs.GetOwnerModule(ownerRow),
                0x16 => Methods.GetOwnerModule(ownerRow),
                _ => throw InvalidMetadata(),
            };
        }
    }

    private enum BodyInspection
    {
        None,
        CompactLinear,
        CompactIndexed,
        CompactMixed,
    }

    public static void Validate(ReadOnlySpan<byte> artifact)
        => _ = ValidateCore(new TrRmArtifactIndex(artifact), BodyInspection.None,
            developmentProfile: false);

    /// <summary>Validates selected text shapes against their using-Module profile identities.</summary>
    /// <param name="artifact">The exact standard TRRM artifact.</param>
    /// <param name="profiles">The resolved text-operation identities, keyed by using Module row.</param>
    public static void ValidateTextConstructionProfile(ReadOnlySpan<byte> artifact,
        IReadOnlyList<TrRmTextConstructionProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var tables = ValidateCore(new TrRmArtifactIndex(artifact),
            BodyInspection.None, developmentProfile: false);
        ValidateTextConstructionProfile(tables, profiles);
    }

    internal static void Validate(TrRmArtifactIndex index)
        => _ = ValidateCore(index, BodyInspection.None, developmentProfile: false);

    internal static ValidatedExecutionTables ValidateAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false);

    internal static ValidatedExecutionTables ValidateDevelopmentAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: true);

    internal static ValidatedExecutionTables ValidateCopyOriginDraftAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            copyOriginDraft: true);

    public static void ValidateCompactLinear(ReadOnlySpan<byte> artifact)
        => _ = ValidateCore(new TrRmArtifactIndex(artifact,
                TrRmArtifactSchema.Development), BodyInspection.CompactLinear,
            developmentProfile: true);

    public static void ValidateCompactIndexed(ReadOnlySpan<byte> artifact)
        => _ = ValidateCore(new TrRmArtifactIndex(artifact,
                TrRmArtifactSchema.Development), BodyInspection.CompactIndexed,
            developmentProfile: true);

    public static void ValidateCompact(ReadOnlySpan<byte> artifact)
        => _ = ValidateCore(new TrRmArtifactIndex(artifact,
                TrRmArtifactSchema.Development), BodyInspection.CompactMixed,
            developmentProfile: true);

    private static void ValidateTextConstructionProfile(
        ValidatedExecutionTables tables,
        IReadOnlyList<TrRmTextConstructionProfile> profiles)
    {
        var profilesByModule = new Dictionary<int, TrRmTextConstructionProfile>();
        foreach (var profile in profiles)
        {
            if (profile.ModuleRow <= 0 || profile.ModuleRow > tables.Modules.Count
                || !profilesByModule.TryAdd(profile.ModuleRow, profile))
            {
                throw InvalidMetadata();
            }

            ValidateProfileMethod(tables, profile.ModuleRow,
                profile.FormatterInterfaceDefRow, profile.FormatterTryFormatMethodDefRow,
                profile, TextConstructionMethodRole.FormatterTryFormat);
            ValidateProfileMethod(tables, profile.ModuleRow,
                profile.WriterInterfaceDefRow, profile.WriterTryWriteMethodDefRow,
                profile, TextConstructionMethodRole.WriterTryWrite);
            ValidateProfileMethod(tables, profile.ModuleRow,
                profile.WriterInterfaceDefRow, profile.WriterTryWriteScalarMethodDefRow,
                profile, TextConstructionMethodRole.WriterTryWriteScalar);
            ValidateProfileMethod(tables, profile.ModuleRow,
                profile.WriterInterfaceDefRow, profile.WriterTryWriteFormattedMethodDefRow,
                profile, TextConstructionMethodRole.WriterTryWriteFormatted);
            if (profile.FormatterInterfaceDefRow == profile.WriterInterfaceDefRow
                || profile.TextSpanTypeSpecRef == 0)
            {
                throw InvalidMetadata();
            }

            _ = tables.TypeSpecs.GetTypeSpecRef(profile.TextSpanTypeSpecRef);
        }

        for (var shape = 1; shape <= tables.TextContentShapes.Count; shape++)
        {
            var generatedTypeDef = tables.TextContentShapes.GetGeneratedTypeDef(shape);
            var module = tables.TypeDefs.GetOwnerModule(generatedTypeDef);
            if (!profilesByModule.TryGetValue(module, out var profile))
            {
                throw InvalidMetadata();
            }

            var (first, count) = tables.TextContentShapes.GetFragmentRange(shape);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var fragment = first + ordinal;
                var writerMethod = tables.TextFragmentShapes.GetWriterRequirementMethodDefRow(fragment);
                var kind = tables.TextFragmentShapes.GetKind(fragment);
                var expectedWriter = kind == 0
                    ? profile.WriterTryWriteMethodDefRow
                    : profile.WriterTryWriteFormattedMethodDefRow;
                if (writerMethod != (uint)expectedWriter
                    || (kind == 1
                        && tables.TextFragmentShapes.GetFormatterMethodDefRow(fragment)
                            != (uint)profile.FormatterTryFormatMethodDefRow))
                {
                    throw InvalidMetadata();
                }
            }
        }
    }

    private static void ValidateProfileMethod(ValidatedExecutionTables tables,
        int module, int interfaceDef, int methodDef,
        TrRmTextConstructionProfile profile, TextConstructionMethodRole role)
    {
        if (interfaceDef <= 0 || interfaceDef > tables.Interfaces.Count
            || methodDef <= 0 || methodDef > tables.Methods.Count)
        {
            throw InvalidMetadata();
        }

        var ownerTypeDef = tables.Interfaces.GetOwnerTypeDef(interfaceDef);
        if (tables.TypeDefs.GetOwnerModule(ownerTypeDef) != module
            || (tables.TypeDefs.GetFlags(ownerTypeDef) & 3) != 2
            || tables.TypeDefs.GetGenericRange(ownerTypeDef).Count != 0
            || tables.Methods.GetOwnerModule(methodDef) != module
            || tables.Methods.GetOwnerType(methodDef) != ownerTypeDef
            || (tables.Methods.GetFlags(methodDef) & 3) != 2
            || (tables.Contracts.GetEffectsMask(methodDef) & 0x06) != 0
            || tables.InterfaceMethods.GetOwnerInterfaceDefForMethod(methodDef) != interfaceDef)
        {
            throw InvalidMetadata();
        }

        var signature = tables.Methods.GetSignature(methodDef);
        var expectedReceiverQualifier = role == TextConstructionMethodRole.FormatterTryFormat
            ? (byte)0 : (byte)1;
        var receiver = tables.Signatures.GetReceiverTypeSpec(signature);
        var receiverInfo = tables.TypeSpecs.GetTypeSpecRef(receiver);
        if (!tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                tables.Signatures.GetResultTypeSpec(signature), 0xF200_0001)
            || !receiverInfo.IsRow || receiverInfo.Form is not (0x02 or 0x03)
            || receiverInfo.Qualifiers != expectedReceiverQualifier
            || tables.TypeSpecs.GetNominalTypeDefRow(receiver,
                tables.TypeDefs, tables.Imports) != ownerTypeDef)
        {
            throw InvalidMetadata();
        }

        var (parameterFirst, parameterCount) = tables.Signatures.GetParamRange(signature);
        var (formalFirst, formalCount) = tables.Signatures.GetGenericRange(signature);
        var expectedParameterCount = role switch
        {
            TextConstructionMethodRole.FormatterTryFormat => 2,
            TextConstructionMethodRole.WriterTryWrite => 1,
            TextConstructionMethodRole.WriterTryWriteScalar => 1,
            TextConstructionMethodRole.WriterTryWriteFormatted => 3,
            _ => throw InvalidMetadata(),
        };
        var expectedFormalCount = role is TextConstructionMethodRole.FormatterTryFormat
            or TextConstructionMethodRole.WriterTryWriteFormatted ? 1 : 0;
        if (parameterCount != expectedParameterCount || formalCount != expectedFormalCount)
        {
            throw InvalidMetadata();
        }

        var formal = formalCount == 0 ? 0 : formalFirst;
        if (formalCount != 0)
        {
            var expectedOwner = 0x1600_0000u | (uint)methodDef;
            var (constraintFirst, constraintCount) =
                tables.GenericParams.GetConstraintRange(formal);
            var expectedConstraintType = role == TextConstructionMethodRole.FormatterTryFormat
                ? profile.WriterInterfaceDefRow
                : profile.FormatterInterfaceDefRow;
            var expectedConstraintTypeDef = tables.Interfaces.GetOwnerTypeDef(
                expectedConstraintType);
            if (tables.GenericParams.GetOwnerDefId(formal) != expectedOwner
                || tables.GenericParams.GetOrdinal(formal) != 0
                || tables.GenericParams.GetFormalKind(formal) != 1
                || tables.GenericParams.GetOwnershipDomain(formal) != 1
                || tables.GenericParams.GetNullabilityDomain(formal) != 1
                || tables.GenericParams.GetDefaultArgumentRef(formal) != 0
                || constraintCount != 1
                || tables.Constraints.GetOwnerGenericParam(constraintFirst) != formal
                || tables.Constraints.GetKind(constraintFirst) != 1)
            {
                throw InvalidMetadata();
            }

            var constraintTypeSpec = tables.Constraints.GetPayload(constraintFirst);
            var constraintTypeInfo = tables.TypeSpecs.GetTypeSpecRef(constraintTypeSpec);
            if (!constraintTypeInfo.IsRow || constraintTypeInfo.Form is not (0x02 or 0x03)
                || constraintTypeInfo.Qualifiers != 0
                || tables.TypeSpecs.GetNominalTypeDefRow(constraintTypeSpec,
                    tables.TypeDefs, tables.Imports) != expectedConstraintTypeDef)
            {
                throw InvalidMetadata();
            }
        }

        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var parameter = parameterFirst + ordinal;
            if (tables.Parameters.GetMode(parameter) != 0)
            {
                throw InvalidMetadata();
            }

            var typeSpec = tables.Parameters.GetTypeSpec(parameter);
            var validType = role switch
            {
                TextConstructionMethodRole.FormatterTryFormat when ordinal == 0
                => IsReferenceToFormal(typeSpec, formal, true, tables.TypeSpecs),
                TextConstructionMethodRole.FormatterTryFormat when ordinal == 1
                    => tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                        typeSpec, profile.TextSpanTypeSpecRef),
                TextConstructionMethodRole.WriterTryWrite
                    => tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                        typeSpec, profile.TextSpanTypeSpecRef),
                TextConstructionMethodRole.WriterTryWriteScalar
                    => tables.TypeSpecs.AreEquivalentTypeSpecRefs(typeSpec, 0xF200_0005),
                TextConstructionMethodRole.WriterTryWriteFormatted when ordinal == 0
                    => IsReferenceToFormal(typeSpec, formal, false, tables.TypeSpecs),
                TextConstructionMethodRole.WriterTryWriteFormatted when ordinal == 1
                    => tables.TypeSpecs.AreEquivalentTypeSpecRefs(typeSpec, 0xF200_0008),
                TextConstructionMethodRole.WriterTryWriteFormatted when ordinal == 2
                    => tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                        typeSpec, profile.TextSpanTypeSpecRef),
                _ => false,
            };
            if (!validType)
            {
                throw InvalidMetadata();
            }
        }
    }

    private static bool IsReferenceToFormal(uint typeSpecRef, int formal,
        bool write, TrRmTypeSpecTableReader typeSpecs)
    {
        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (!reference.IsRow || reference.Form != 0x07
            || reference.Qualifiers != (write ? (byte)0x01 : (byte)0))
        {
            return false;
        }

        var inner = typeSpecs.GetTypeSpecRef(BinaryPrimitives.ReadUInt32LittleEndian(
            typeSpecs.GetRow(reference.Row)[2..]));
        return inner.IsRow && inner.Form == 0x04 && inner.Qualifiers == 0
            && BinaryPrimitives.ReadUInt32LittleEndian(
                typeSpecs.GetRow(inner.Row)[2..]) == (uint)formal;
    }

    private enum TextConstructionMethodRole
    {
        FormatterTryFormat,
        WriterTryWrite,
        WriterTryWriteScalar,
        WriterTryWriteFormatted,
    }

    private static ValidatedExecutionTables ValidateCore(TrRmArtifactIndex index,
        BodyInspection inspection, bool developmentProfile,
        bool copyOriginDraft = false)
    {
        var selectedSchema = developmentProfile
            ? TrRmArtifactSchema.Development
            : copyOriginDraft
                ? TrRmArtifactSchema.CopyOriginDraft : TrRmArtifactSchema.Standard;
        if (index.Schema != selectedSchema)
        {
            throw InvalidMetadata();
        }

        if (!index.TryGetSection(0x0001, out var moduleBytes, out var moduleCount)
            || !index.TryGetSection(0x0002, out var stringBytes, out var stringCount))
        {
            throw InvalidMetadata();
        }

        _ = index.TryGetSection(0x0004, out var featureBytes, out var featureCount);
        _ = index.TryGetSection(0x0010, out var moduleRefBytes, out var moduleRefCount);
        _ = index.TryGetSection(0x0011, out var typeSpecBytes, out var typeSpecCount);
        _ = index.TryGetSection(0x0013, out var typeDefBytes, out var typeDefCount);
        _ = index.TryGetSection(0x0014, out var fieldDefBytes, out var fieldDefCount);
        _ = index.TryGetSection(0x0024, out var interfaceMethodBytes,
            out var interfaceMethodCount);
        _ = index.TryGetSection(0x0025, out var interfaceDefBytes, out var interfaceDefCount);
        _ = index.TryGetSection(0x0026, out var interfaceBaseBytes,
            out var interfaceBaseCount);
        _ = index.TryGetSection(0x0027, out var associatedTypeBytes,
            out var associatedTypeCount);
        _ = index.TryGetSection(0x0028, out var interfaceWitnessBytes,
            out var interfaceWitnessCount);
        _ = index.TryGetSection(0x0029, out var witnessMemberBytes,
            out var witnessMemberCount);
        _ = index.TryGetSection(0x002A, out var witnessAssociatedTypeBytes,
            out var witnessAssociatedTypeCount);
        _ = index.TryGetSection(0x002B, out var builderRegistrationBytes,
            out var builderRegistrationCount);
        _ = index.TryGetSection(0x002C, out var textTargetBytes, out var textTargetCount);
        _ = index.TryGetSection(0x002D, out var textContentShapeBytes,
            out var textContentShapeCount);
        _ = index.TryGetSection(0x002E, out var textCaptureShapeBytes,
            out var textCaptureShapeCount);
        _ = index.TryGetSection(0x002F, out var textFragmentShapeBytes,
            out var textFragmentShapeCount);
        _ = index.TryGetSection(0x0037, out var predicateGraphBytes,
            out var predicateGraphCount);
        _ = index.TryGetSection(0x0038, out var predicateReadFootprintBytes,
            out var predicateReadFootprintCount);
        _ = index.TryGetSection(0x0039, out var predicateReadRootBytes,
            out var predicateReadRootCount);
        _ = index.TryGetSection(0x003A, out _, out var predicateReadDependencyCount);
        _ = index.TryGetSection(0x003B, out var predicateNodeBytes,
            out var predicateNodeCount);
        _ = index.TryGetSection(0x003C, out var predicateNodeOperandBytes,
            out var predicateNodeOperandCount);
        _ = index.TryGetSection(0x003D, out var predicateChildEdgeBytes,
            out var predicateChildEdgeCount);
        _ = index.TryGetSection(0x003E, out var predicateCaptureBytes,
            out var predicateCaptureCount);
        _ = index.TryGetSection(0x0030, out var constantSourceBytes,
            out var constantSourceCount);
        _ = index.TryGetSection(0x0031, out var interfaceConstBytes,
            out var interfaceConstCount);
        _ = index.TryGetSection(0x0032, out var associatedConstBytes,
            out var associatedConstCount);
        _ = index.TryGetSection(0x0033, out _, out var witnessAssociatedConstCount);
        _ = index.TryGetSection(0x0034, out var unionCaseDefBytes, out var unionCaseDefCount);
        _ = index.TryGetSection(0x0035, out var unionCaseFieldBytes, out var unionCaseFieldCount);
        _ = index.TryGetSection(0x0036, out var associatedRelationBytes,
            out var associatedRelationCount);
        _ = index.TryGetSection(0x003F, out var frozenValueNodeBytes,
            out var frozenValueNodeCount);
        _ = index.TryGetSection(0x0040, out _, out var frozenValueChildCount);
        _ = index.TryGetSection(0x0042, out _, out var constantExecutionCount);
        _ = index.TryGetSection(0x0049, out _, out var targetValueBindingCount);
        _ = index.TryGetSection(0x0045, out var targetLayoutDependencyBytes,
            out var targetLayoutDependencyCount);
        _ = index.TryGetSection(0x004A, out var targetLayoutDependencyFieldBytes,
            out var targetLayoutDependencyFieldCount);
        _ = index.TryGetSection(0x004B, out var fieldPathBytes, out var fieldPathCount);
        _ = index.TryGetSection(0x0015, out var layoutBytes, out var layoutCount);
        _ = index.TryGetSection(0x0023, out var layoutFieldBytes, out var layoutFieldCount);
        _ = index.TryGetSection(0x0048, out var targetProfileBytes, out var targetProfileCount);
        _ = index.TryGetSection(0x0016, out var methodDefBytes, out var methodDefCount);
        _ = index.TryGetSection(0x0017, out var methodSigBytes, out var methodSigCount);
        _ = index.TryGetSection(0x0018, out var paramBytes, out var paramCount);
        _ = index.TryGetSection(0x0019, out var genericParamBytes, out var genericParamCount);
        _ = index.TryGetSection(0x001A, out var constraintBytes, out var constraintCount);
        _ = index.TryGetSection(0x001B, out var contractBytes, out var contractCount);
        _ = index.TryGetSection(0x001C, out var importBytes, out var importCount);
        _ = index.TryGetSection(0x001D, out var exportBytes, out var exportCount);
        _ = index.TryGetSection(0x001E, out var propertyBytes, out var propertyCount);
        _ = index.TryGetSection(0x001F, out var sourceFileBytes, out var sourceFileCount);
        _ = index.TryGetSection(0x0003, out var blobBytes, out var blobCount);
        _ = index.TryGetSection(0x0020, out var bodyBytes, out var bodyCount);
        _ = index.TryGetSection(0x0030, out var nativeImportBytes, out var nativeImportCount);
        _ = index.TryGetSection(0x0031, out var nativeCallableBytes, out var nativeCallableCount);
        _ = index.TryGetSection(0x0032, out var nativeVTableBytes, out var nativeVTableCount);
        _ = index.TryGetSection(0x0033, out var nativeFunctionPointerSignatureBytes,
            out var nativeFunctionPointerSignatureCount);
        _ = index.TryGetSection(0x7F00, out var sourceMapBytes, out var sourceMapCount);
        ReadOnlySpan<byte> copyOriginNativeImportBytes = default;
        var copyOriginNativeImportCount = 0;
        _ = index.TryGetSection(0x0021, out var standardNativeImportBytes,
            out var standardNativeImportCount);
        _ = index.TryGetSection(0x0022, out var nativeExportBytes,
            out var nativeExportCount);
        if (copyOriginDraft)
        {
            _ = index.TryGetSection(0x0021, out copyOriginNativeImportBytes,
                out copyOriginNativeImportCount);
        }

        var blobs = blobCount == 0 ? default
            : new TrRmBlobTableReader(blobBytes, blobCount);

        var bodies = bodyCount == 0 ? default
            : new TrRmBodyTableReader(bodyBytes, bodyCount);

        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        strings.ValidateShapes(stringCount);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount,
            index.Schema);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount, strings,
            moduleRefCount, typeDefCount, methodDefCount, sourceFileCount, featureCount);
        for (var module = 1; module <= modules.Count; module++)
        {
            if (modules.GetGoverningRuleEdition(module) != 1)
            {
                throw InvalidMetadata();
            }
        }
        if (!developmentProfile)
        {
            TrRmFeatureManifestValidator.Validate(features, modules, index.Schema);
        }
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, moduleRefCount,
            strings, modules);
        var selectedNativeFunctionPointerSignatureCount = developmentProfile
            ? nativeFunctionPointerSignatureCount : 0;
        var typeSpecs = typeSpecCount == 0
            && selectedNativeFunctionPointerSignatureCount == 0 ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
                typeDefCount, importCount, genericParamCount,
                selectedNativeFunctionPointerSignatureCount, index.Schema,
                methodDefCount);
        var frozenValueNodes = !developmentProfile && frozenValueNodeCount != 0
            ? new TrRmFrozenValueNodeTableReader(frozenValueNodeBytes,
                frozenValueNodeCount, typeSpecs, blobCount, frozenValueChildCount,
                unionCaseDefCount)
            : default;
        var types = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, fieldDefCount, genericParamCount, layoutCount, contractCount);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, targetProfileCount);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            types, targetProfiles, layoutFieldCount);
        var fields = fieldDefCount == 0 ? default
            : new TrRmFieldDefTableReader(fieldDefBytes, fieldDefCount, strings, types,
                typeSpecs);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes,
            layoutFieldCount, layouts, types, fields);
        var signatures = methodSigCount == 0 ? default
            : new TrRmMethodSigTableReader(methodSigBytes, methodSigCount,
                typeSpecs, paramCount, genericParamCount);
        var parameters = paramCount == 0 ? default
            : new TrRmParamTableReader(paramBytes, paramCount, signatures,
                typeSpecs, index.Schema);
        var methods = new TrRmMethodDefTableReader(methodDefBytes, methodDefCount,
            strings, modules, types, signatures, contractCount, bodyCount);
        var properties = propertyCount == 0 ? default
            : new TrRmPropertyDefTableReader(propertyBytes, propertyCount,
                strings, types, typeSpecs, methods, signatures);
        var selectedConstantSourceCount = developmentProfile ? 0 : constantSourceCount;
        var generics = new TrRmGenericParamTableReader(genericParamBytes, genericParamCount,
            strings, types, methods, signatures, typeSpecs, constraintCount,
            selectedConstantSourceCount);
        var interfaces = default(TrRmInterfaceDefTableReader);
        var associatedTypeDefs = default(TrRmAssociatedTypeDefTableReader);
        var interfaceWitnesses = default(TrRmInterfaceWitnessTableReader);
        var witnessMembers = default(TrRmWitnessMemberTableReader);
        var witnessAssociatedTypes = default(TrRmWitnessAssociatedTypeTableReader);
        var interfaceBases = default(TrRmInterfaceBaseTableReader);
        var interfaceMethods = default(TrRmInterfaceMethodTableReader);
        var interfaceConstDefs = default(TrRmInterfaceConstDefTableReader);
        var associatedConstDefs = default(TrRmAssociatedConstDefTableReader);
        var associatedRelations = default(TrRmAssociatedRelationTableReader);
        var unionCaseDefs = default(TrRmUnionCaseDefTableReader);
        var unionCaseFields = default(TrRmUnionCaseFieldTableReader);
        var builderRegistrations = default(TrRmBuilderRegistrationTableReader);
        var textTargets = default(TrRmTextTargetTableReader);
        var textContentShapes = default(TrRmTextContentShapeTableReader);
        var textCaptureShapes = default(TrRmTextCaptureShapeTableReader);
        var textFragmentShapes = default(TrRmTextFragmentShapeTableReader);
        var predicateGraphs = default(TrRmPredicateGraphTableReader);
        var predicateChildEdges = default(TrRmPredicateChildEdgeTableReader);
        var predicateReadFootprints = default(TrRmPredicateReadFootprintTableReader);
        var predicateCaptures = default(TrRmPredicateCaptureTableReader);
        var constantSources = default(TrRmConstantSourceTableReader);
        var targetLayoutDependencies = default(TrRmTargetLayoutDependencyTableReader);
        var targetLayoutDependencyFields = default(TrRmTargetLayoutDependencyFieldTableReader);
        var fieldPaths = default(TrRmFieldPathTableReader);
        if (!developmentProfile)
        {
            builderRegistrations = new TrRmBuilderRegistrationTableReader(
                builderRegistrationBytes, builderRegistrationCount, types, typeSpecs);
            textTargets = new TrRmTextTargetTableReader(textTargetBytes,
                textTargetCount, methods, signatures, typeSpecs);
            textContentShapes = new TrRmTextContentShapeTableReader(
                textContentShapeBytes, textContentShapeCount, types,
                textCaptureShapeCount, textFragmentShapeCount);
            textCaptureShapes = new TrRmTextCaptureShapeTableReader(
                textCaptureShapeBytes, textCaptureShapeCount, typeSpecs,
                textContentShapes);
            predicateGraphs = new TrRmPredicateGraphTableReader(
                predicateGraphBytes, predicateGraphCount, predicateNodeCount,
                predicateReadFootprintCount);
            predicateChildEdges = new TrRmPredicateChildEdgeTableReader(
                predicateChildEdgeBytes, predicateChildEdgeCount,
                predicateNodeCount, predicateGraphs);
            predicateReadFootprints = new TrRmPredicateReadFootprintTableReader(
                predicateReadFootprintBytes, predicateReadFootprintCount,
                predicateReadRootCount, predicateReadDependencyCount);
            predicateCaptures = new TrRmPredicateCaptureTableReader(
                predicateCaptureBytes, predicateCaptureCount,
                predicateGraphCount, typeSpecs);
            unionCaseDefs = new TrRmUnionCaseDefTableReader(
                unionCaseDefBytes, unionCaseDefCount, strings, types,
                unionCaseFieldCount);
            unionCaseFields = new TrRmUnionCaseFieldTableReader(
                unionCaseFieldBytes, unionCaseFieldCount, strings, typeSpecs,
                unionCaseDefs);
            interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes,
                interfaceDefCount, types, generics, interfaceBaseCount,
                interfaceMethodCount, associatedTypeCount, associatedConstCount,
                interfaceConstCount);
            interfaceBases = new TrRmInterfaceBaseTableReader(interfaceBaseBytes,
                interfaceBaseCount, typeSpecs, interfaces);
            interfaceMethods = new TrRmInterfaceMethodTableReader(interfaceMethodBytes,
                interfaceMethodCount, interfaces, methods, signatures);
            interfaceConstDefs = new TrRmInterfaceConstDefTableReader(interfaceConstBytes,
                interfaceConstCount, strings, typeSpecs, interfaces, constantSourceCount);
            associatedConstDefs = new TrRmAssociatedConstDefTableReader(
                associatedConstBytes, associatedConstCount, strings, typeSpecs, interfaces);
            constantSources = new TrRmConstantSourceTableReader(constantSourceBytes,
                constantSourceCount, frozenValueNodeCount, constantExecutionCount,
                targetValueBindingCount);
            for (var formal = 1; formal <= generics.Count; formal++)
            {
                if (generics.GetFormalKind(formal) != 2
                    || generics.GetDefaultArgumentRef(formal) == 0)
                {
                    continue;
                }

                var owner = generics.GetOwnerDefId(formal);
                var ownerRow = checked((int)(owner & 0x00FF_FFFF));
                var ownerModule = (byte)(owner >> 24) switch
                {
                    0x13 => types.GetOwnerModule(ownerRow),
                    0x16 => methods.GetOwnerModule(ownerRow),
                    _ => throw InvalidMetadata(),
                };
                TrRmConstantSourceValueValidator.ValidateFrozenScalar(
                    checked((int)generics.GetDefaultArgumentRef(formal)),
                    generics.GetConstantTypeSpecRef(formal), ownerModule,
                    constantSources, frozenValueNodes, blobs, typeSpecs,
                    features, modules);
            }
            for (var row = 1; row <= interfaceConstDefs.Count; row++)
            {
                var ownerInterface = interfaceConstDefs.GetOwnerInterfaceDef(row);
                var ownerTypeDef = interfaces.GetOwnerTypeDef(ownerInterface);
                TrRmConstantSourceValueValidator.ValidateFrozenScalar(
                    interfaceConstDefs.GetConstantSourceRow(row),
                    interfaceConstDefs.GetConstantTypeSpecRef(row),
                    types.GetOwnerModule(ownerTypeDef), constantSources,
                    frozenValueNodes, blobs, typeSpecs, features, modules);
            }
            associatedTypeDefs = new TrRmAssociatedTypeDefTableReader(
                associatedTypeBytes, associatedTypeCount, strings, typeSpecs, interfaces);
            interfaceWitnesses = new TrRmInterfaceWitnessTableReader(
                interfaceWitnessBytes, interfaceWitnessCount, typeSpecs,
                witnessMemberCount, witnessAssociatedTypeCount,
                witnessAssociatedConstCount);
            witnessMembers = new TrRmWitnessMemberTableReader(witnessMemberBytes,
                witnessMemberCount, interfaceWitnesses, typeSpecs, methods);
        }

        var constraints = constraintCount == 0 ? default
            : new TrRmConstraintTableReader(constraintBytes, constraintCount,
                generics, typeSpecs);
        var contracts = developmentProfile
            ? new TrRmContractTableReader(contractBytes, contractCount,
                types, methods, signatures, parameters, typeSpecs,
                developmentProfile: true)
            : new TrRmContractTableReader(contractBytes, contractCount,
                types, methods, signatures, parameters, typeSpecs, fieldPathCount,
                features, modules, index.Schema, predicateGraphs.Count);
        var predicateSelectorKinds = Array.Empty<byte>();
        if (!developmentProfile)
        {
            predicateSelectorKinds = TrRmPublishedPredicateSelectorValidator.Validate(
                predicateReadRootBytes, predicateReadRootCount,
                predicateNodeBytes, predicateNodeCount,
                predicateNodeOperandBytes, predicateNodeOperandCount,
                predicateReadDependencyCount, predicateGraphs,
                predicateReadFootprints, predicateChildEdges, predicateCaptures,
                methods, signatures, typeSpecs, contracts);
        }
        var imports = new TrRmImportTableReader(importBytes, importCount,
            strings, moduleRefs, types, methods, properties,
            interfaces, associatedTypeDefs, fields, features, modules,
            index.Schema);
        if (!developmentProfile)
        {
            associatedRelations = new TrRmAssociatedRelationTableReader(
                associatedRelationBytes, associatedRelationCount, typeSpecs,
                associatedTypeDefs.Count, imports);
            textFragmentShapes = new TrRmTextFragmentShapeTableReader(
                textFragmentShapeBytes, textFragmentShapeCount, textContentShapes,
                textCaptureShapes, types, methods, generics, constraints,
                typeSpecs, interfaces, interfaceBases, interfaceMethods,
                interfaceWitnesses, witnessMembers, imports);
        }
        TrRmConstructedTypeSpecValidator.Validate(typeSpecs, types,
            generics, imports, selectedConstantSourceCount);
        if (!developmentProfile)
        {
            witnessAssociatedTypes = new TrRmWitnessAssociatedTypeTableReader(
                witnessAssociatedTypeBytes, witnessAssociatedTypeCount,
                interfaceWitnesses, typeSpecs, types, interfaces, interfaceBases,
                associatedTypeDefs, imports);
            targetLayoutDependencyFields = new TrRmTargetLayoutDependencyFieldTableReader(
                targetLayoutDependencyFieldBytes, targetLayoutDependencyFieldCount,
                fields, imports);
            targetLayoutDependencies = new TrRmTargetLayoutDependencyTableReader(
                targetLayoutDependencyBytes, targetLayoutDependencyCount, typeSpecs,
                targetProfiles, targetLayoutDependencyFieldCount);
            fieldPaths = fieldPathCount == 0 ? default
                : new TrRmFieldPathTableReader(fieldPathBytes, fieldPathCount,
                    typeSpecs, fields, imports);
        }
        if (copyOriginDraft)
        {
            TrRmCopyOriginContractValidator.Validate(contracts, types, fields,
                methods, signatures, parameters, typeSpecs, fieldPaths, imports,
                unionCaseDefs, layouts, copyOriginNativeImportBytes,
                copyOriginNativeImportCount);
        }
        var exports = new TrRmExportTableReader(exportBytes, exportCount,
            strings, types, methods, properties, imports,
            interfaces, associatedTypeDefs, fields, features, modules,
            index.Schema);
        var nativeImports = developmentProfile
            ? new TrRmDevelopmentNativeImportTableReader(nativeImportBytes,
                nativeImportCount, strings, methods, signatures, parameters,
                typeSpecs, contracts, layouts)
            : default;
        var standardNativeImports = !developmentProfile && !copyOriginDraft
            ? new TrRmNativeImportTableReader(standardNativeImportBytes,
                standardNativeImportCount, strings, methods, fields, types, imports,
                modules, signatures, parameters, typeSpecs, contracts, layouts)
            : default;
        var nativeExports = !developmentProfile
            ? new TrRmNativeExportTableReader(nativeExportBytes, nativeExportCount,
                strings, methods, fields, types, imports, modules, signatures,
                typeSpecs, contracts, layouts)
            : default;
        var nativeFunctionPointerAnchors = !developmentProfile
            ? new TrRmNativeFunctionPointerAnchorTableReader(typeSpecs, types,
                generics, methods, signatures, parameters, contracts, imports,
                constantSources, frozenValueNodes, blobs, features, index.Schema,
                predicateSelectorKinds, modules,
                exports, properties, interfaceMethods, standardNativeImports,
                nativeExports)
            : default;
        var nativeFunctionPointerSignatures = developmentProfile
            ? new TrRmNativeFunctionPointerSignatureTableReader(
                nativeFunctionPointerSignatureBytes,
                nativeFunctionPointerSignatureCount, methods, signatures, parameters,
                typeSpecs, contracts)
            : default;
        var nativeCallables = developmentProfile
            ? new TrRmNativeCallableTableReader(nativeCallableBytes,
                nativeCallableCount, strings, methods, signatures, parameters, typeSpecs,
                contracts, nativeFunctionPointerSignatures, imports)
            : default;
        var nativeVTables = developmentProfile
            ? new TrRmNativeVTableTableReader(nativeVTableBytes,
                nativeVTableCount, methods, signatures, parameters, typeSpecs, contracts)
            : default;
        if (developmentProfile)
        {
            for (var method = 1; method <= methods.Count; method++)
            {
                if (contracts.HasNativeAbi(method)
                    && contracts.GetNativeAbi(method).Role is not (1 or 2 or 3 or 4))
                {
                    throw InvalidMetadata();
                }
            }

            ValidateNativeBoundaryFeatureAssignments(features, modules, typeSpecs,
                signatures, parameters, methods, bodies, contracts, layouts, nativeImports,
                nativeCallables, nativeFunctionPointerSignatures, nativeVTables,
                developmentProfile, index.Schema);
        }
        else
        {
            ValidateStandardNativeCallableFeatureAssignments(contracts, features,
                modules, methods, index.Schema);
            ValidateStandardNativeExportFeatureAssignments(nativeExports,
                features, modules, methods, types, fields, index.Schema);
            ValidateStandardNativeFunctionPointerValueFeatureAssignments(
                typeSpecs, signatures, parameters, methods, types, fields,
                generics, imports, features, modules, index.Schema);
            if (!copyOriginDraft)
            {
                ValidateStandardNativeImportFeatureAssignments(standardNativeImports,
                    features, modules, methods, types, fields);
            }
        }
        var sourceFiles = sourceFileCount == 0 ? default
            : new TrRmSourceFileTableReader(sourceFileBytes, sourceFileCount, strings, modules);
        var sourceMaps = sourceMapCount == 0 ? default
            : new TrRmSourceMapTableReader(sourceMapBytes, sourceMapCount,
                methods, sourceFiles, developmentProfile
                    ? TrRmArtifactSchema.Development : TrRmArtifactSchema.Standard);

        if (inspection != BodyInspection.None)
        {
            var developmentFeatures = new TrRmDevelopmentFeatureTableReader(
                featureBytes, featureCount);
            var linearCounts = sourceMapCount == 0
                || inspection != BodyInspection.CompactLinear ? []
                : new int[methods.Count + 1];
            var indexedCounts = sourceMapCount == 0
                || inspection == BodyInspection.CompactLinear
                    ? [] : new int[methods.Count + 1][];
            if (bodyCount != 0)
            {
                for (var method = 1; method <= methods.Count; method++)
                {
                    var row = methods.GetBodyRow(method);
                    if (row != 0)
                    {
                        var linear = inspection == BodyInspection.CompactLinear
                            || (inspection == BodyInspection.CompactMixed
                                && (bodies.GetRow(row)[0] & 0x0C) == 0x04);
                        if (linear)
                        {
                            var body = new TrRmCompactLinearBodyReader(bodies, method, modules,
                                methods, signatures, parameters, typeSpecs, types, imports,
                                nativeImports);
                            if (sourceMapCount != 0)
                            {
                                if (inspection == BodyInspection.CompactMixed)
                                {
                                    indexedCounts[method] = [0, body.OperationCount];
                                }
                                else
                                {
                                    linearCounts[method] = body.OperationCount;
                                }
                            }
                        }
                        else
                        {
                            var body = new TrRmCompactIndexedBodyReader(bodies, method, modules,
                                methods, signatures, parameters, typeSpecs, types, imports,
                                developmentFeatures, nativeCallables, nativeImports,
                                nativeFunctionPointerSignatures,
                                nativeVTables, (contracts.GetEffectsMask(method) & 2) != 0);
                            if (sourceMapCount != 0)
                            {
                                var counts = new int[body.BlockCount + 1];
                                for (var block = 1; block <= body.BlockCount; block++)
                                {
                                    counts[block] = body.GetOperationCount(block);
                                }

                                indexedCounts[method] = counts;
                            }
                        }
                    }
                }
            }

            if (sourceMapCount != 0)
            {
                if (inspection == BodyInspection.CompactLinear)
                {
                    sourceMaps.ValidateCompactLinearLocations(methods, linearCounts);
                }
                else
                {
                    sourceMaps.ValidateCompactLocations(methods, indexedCounts);
                }
            }
        }

        _ = fields;
        _ = layouts;
        _ = parameters;
        _ = contracts;
        _ = exports;
        _ = sourceFiles;
        _ = sourceMaps;
        var executionTables = new ValidatedExecutionTables(strings, features,
            modules, moduleRefs,
            types, typeSpecs,
            nativeFunctionPointerAnchors,
            generics,
            constraints,
            interfaces, associatedTypeDefs, interfaceWitnesses, witnessMembers,
            witnessAssociatedTypes,
            interfaceBases, interfaceMethods,
            interfaceConstDefs, associatedConstDefs, associatedRelations,
            unionCaseDefs, unionCaseFields, builderRegistrations, textTargets,
            textContentShapes, textCaptureShapes, textFragmentShapes,
            predicateGraphs, predicateChildEdges, predicateReadFootprints,
            predicateCaptures,
            blobs, frozenValueNodes,
            constantSources,
            targetLayoutDependencies, targetLayoutDependencyFields, fieldPaths,
            fields, layouts, layoutFields,
            targetProfiles, signatures, parameters, methods, bodies, contracts, nativeImports,
            nativeExports, nativeCallables, nativeFunctionPointerSignatures,
            nativeVTables, imports,
            index.Schema);
        executionTables.ValidateNonBodyTypeSpecModuleContexts();
        return executionTables;
    }

    private static void ValidateNativeBoundaryFeatureAssignments(
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmMethodDefTableReader methods,
        TrRmBodyTableReader bodies, TrRmContractTableReader contracts,
        TrRmLayoutTableReader layouts,
        TrRmDevelopmentNativeImportTableReader nativeImports,
        TrRmNativeCallableTableReader nativeCallables,
        TrRmNativeFunctionPointerSignatureTableReader nativeFunctionPointerSignatures,
        TrRmNativeVTableTableReader nativeVTables, bool developmentProfile,
        TrRmArtifactSchema schema)
    {
        for (var module = 1; module <= modules.Count; module++)
        {
            var hasNativeBoundaryContracts = false;
            var hasNativeVTables = false;
            var hasNativeFunctionPointerSignatures = false;
            var hasNativePointerValues = false;
            var hasNativeImports = false;
            var hasNativeCallableEntries = false;
            var (typeStart, typeEnd) = modules.GetTypeDefRange(module);
            for (var type = typeStart; type < typeEnd; type++)
            {
                hasNativeBoundaryContracts |= layouts.HasBlittableVariant(type);
            }
            var (methodStart, methodEnd) = modules.GetMethodDefRange(module);
            for (var method = methodStart; method < methodEnd; method++)
            {
                var hasFunctionPointerContract = nativeFunctionPointerSignatures.HasSignatureForMethod(method)
                    || nativeCallables.HasCallableForMethod(method)
                    || HasNativeFunctionPointerType(method, methods, signatures,
                        parameters, typeSpecs);
                hasNativePointerValues |= HasNativePointerType(method, methods,
                    signatures, parameters, typeSpecs)
                    || HasNativePointerValueInBody(method, methods, bodies, typeSpecs);
                hasNativeImports |= nativeImports.HasImportForMethod(method);
                hasNativeCallableEntries |= nativeCallables.HasCallableForMethod(method);
                hasNativeBoundaryContracts |= nativeImports.HasImportForMethod(method)
                    || hasFunctionPointerContract
                    || hasNativePointerValues
                    || nativeVTables.HasVTableForMethod(method);
                hasNativeFunctionPointerSignatures |= hasFunctionPointerContract;
                hasNativeVTables |= nativeVTables.HasVTableForMethod(method);
            }

            var declaresNativeBoundaryFeature = RequiresModuleFeature(features, modules, module,
                TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
                TrRmDevelopmentFeatureTableReader.NativeBoundaryContractsFeatureId,
                developmentProfile, schema);
            var declaresRootlessNativeValues = RequiresModuleFeature(features, modules, module,
                TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                TrRmDevelopmentFeatureTableReader.RootlessNativeValuesFeatureId,
                developmentProfile, schema);
            var declaresDirectNativeCalls = RequiresModuleFeature(features, modules, module,
                TrRmFeatureTableReader.DirectNativeCallsFeatureId,
                TrRmDevelopmentFeatureTableReader.DirectNativeCallsFeatureId,
                developmentProfile, schema);
            var declaresNativeInterfaceCalls = RequiresModuleFeature(features, modules, module,
                TrRmFeatureTableReader.NativeInterfaceCallsFeatureId,
                TrRmDevelopmentFeatureTableReader.NativeVTableCallsFeatureId,
                developmentProfile, schema);
            var declaresNativeCallableEntries = RequiresModuleFeature(features, modules, module,
                TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
                TrRmDevelopmentFeatureTableReader.NativeCallableEntriesFeatureId,
                developmentProfile, schema);

            if (hasNativeBoundaryContracts && !declaresNativeBoundaryFeature)
            {
                throw InvalidMetadata();
            }
            if ((hasNativeVTables && (!declaresRootlessNativeValues || !declaresNativeInterfaceCalls))
                || (hasNativeFunctionPointerSignatures && !declaresRootlessNativeValues)
                || (hasNativePointerValues && !declaresRootlessNativeValues)
                || (declaresRootlessNativeValues && !declaresNativeBoundaryFeature)
                || (declaresNativeInterfaceCalls && !declaresRootlessNativeValues)
                || (hasNativeImports && !declaresDirectNativeCalls)
                || (hasNativeCallableEntries && !declaresNativeCallableEntries)
                || (declaresDirectNativeCalls
                    && (!declaresNativeBoundaryFeature || !declaresRootlessNativeValues)))
            {
                throw InvalidMetadata();
            }
        }
    }

    private static bool RequiresModuleFeature(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, int module, uint currentFeatureId,
        uint developmentFeatureId, bool developmentProfile,
        TrRmArtifactSchema schema)
    {
        if (!developmentProfile)
        {
            return TrRmFeatureManifestValidator.Requires(
                features, modules, module, currentFeatureId, schema);
        }

        var (first, count) = modules.GetFeatureRange(module);
        for (var offset = 0; offset < count; offset++)
        {
            if (features.GetFeature(first + offset).FeatureId == developmentFeatureId)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateStandardNativeImportFeatureAssignments(
        TrRmNativeImportTableReader nativeImports, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields)
    {
        for (var row = 1; row <= nativeImports.Count; row++)
        {
            var import = nativeImports.GetRow(row);
            var (module, requiredFeature) = import.OwnerKind switch
            {
                1 => (methods.GetOwnerModule(import.OwnerRef),
                    TrRmFeatureTableReader.DirectNativeCallsFeatureId),
                2 => (types.GetOwnerModule(fields.GetOwnerTypeDef(import.OwnerRef)),
                    TrRmFeatureTableReader.NativeDataImportsFeatureId),
                _ => throw InvalidMetadata(),
            };
            if (!TrRmFeatureManifestValidator.Requires(features, modules, module,
                    TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
                    TrRmArtifactSchema.Standard)
                || !TrRmFeatureManifestValidator.Requires(features, modules, module,
                    TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                    TrRmArtifactSchema.Standard)
                || !TrRmFeatureManifestValidator.Requires(features, modules, module,
                    requiredFeature, TrRmArtifactSchema.Standard))
            {
                throw InvalidMetadata();
            }
        }
    }

    private static void ValidateStandardNativeCallableFeatureAssignments(
        TrRmContractTableReader contracts, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmArtifactSchema schema)
    {
        for (var method = 1; method <= methods.Count; method++)
        {
            if (contracts.HasNativeCallableContract(method)
                && !TrRmFeatureManifestValidator.Requires(features, modules,
                    methods.GetOwnerModule(method),
                    TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
                    schema))
            {
                throw InvalidMetadata();
            }
        }
    }

    private static void ValidateStandardNativeExportFeatureAssignments(
        TrRmNativeExportTableReader nativeExports, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmTypeDefTableReader typeDefs, TrRmFieldDefTableReader fields,
        TrRmArtifactSchema schema)
    {
        for (var row = 1; row <= nativeExports.Count; row++)
        {
            var export = nativeExports.GetRow(row);
            var module = export.OwnerKind switch
            {
                1 => methods.GetOwnerModule(export.OwnerRef),
                2 => typeDefs.GetOwnerModule(fields.GetOwnerTypeDef(export.OwnerRef)),
                _ => throw InvalidMetadata(),
            };
            if (!TrRmFeatureManifestValidator.Requires(features, modules, module,
                    TrRmFeatureTableReader.NativeExportEntriesFeatureId, schema))
            {
                throw InvalidMetadata();
            }
        }
    }

    private static void ValidateStandardNativeFunctionPointerValueFeatureAssignments(
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmMethodDefTableReader methods,
        TrRmTypeDefTableReader typeDefs, TrRmFieldDefTableReader fields,
        TrRmGenericParamTableReader genericParams, TrRmImportTableReader imports,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema)
    {
        for (var module = 1; module <= modules.Count; module++)
        {
            var containsFunctionPointerValue = false;
            var (methodStart, methodEnd) = modules.GetMethodDefRange(module);
            for (var method = methodStart; method < methodEnd; method++)
            {
                var signature = methods.GetSignature(method);
                containsFunctionPointerValue |= typeSpecs.ContainsNativeFunctionPointerValue(
                    signatures.GetResultTypeSpec(signature), typeDefs, genericParams,
                    imports);
                var receiverTypeSpec = signatures.GetReceiverTypeSpec(signature);
                if (receiverTypeSpec != 0)
                {
                    containsFunctionPointerValue |= typeSpecs
                        .ContainsNativeFunctionPointerValue(receiverTypeSpec,
                            typeDefs, genericParams, imports);
                }

                var (parameterStart, parameterCount) = signatures.GetParamRange(signature);
                for (var parameter = 0; parameter < parameterCount; parameter++)
                {
                    containsFunctionPointerValue |= typeSpecs.ContainsNativeFunctionPointerValue(
                        parameters.GetTypeSpec(parameterStart + parameter), typeDefs,
                        genericParams, imports);
                }
            }

            var (typeStart, typeEnd) = modules.GetTypeDefRange(module);
            for (var type = typeStart; type < typeEnd; type++)
            {
                var (fieldStart, fieldEnd) = typeDefs.GetFieldRange(type);
                for (var field = fieldStart; field < fieldEnd; field++)
                {
                    containsFunctionPointerValue |= typeSpecs.ContainsNativeFunctionPointerValue(
                        fields.GetTypeSpecRef(field), typeDefs, genericParams, imports);
                }
            }

            if (containsFunctionPointerValue
                && !TrRmFeatureManifestValidator.Requires(features, modules,
                    module, TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                    schema))
            {
                throw InvalidMetadata();
            }
        }
    }

    private static bool HasNativeFunctionPointerType(int method,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs)
    {
        var signature = methods.GetSignature(method);
        if (typeSpecs.GetTypeSpecBytes(signatures.GetResultTypeSpec(signature))[0] == 0x0D)
        {
            return true;
        }

        var (first, count) = signatures.GetParamRange(signature);
        for (var parameter = 0; parameter < count; parameter++)
        {
            if (typeSpecs.GetTypeSpecBytes(parameters.GetTypeSpec(first + parameter))[0] == 0x0D)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasNativePointerType(int method,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs)
    {
        var signature = methods.GetSignature(method);
        if (IsNativePointerType(typeSpecs.GetTypeSpecBytes(
            signatures.GetResultTypeSpec(signature))))
        {
            return true;
        }

        var (first, count) = signatures.GetParamRange(signature);
        for (var parameter = 0; parameter < count; parameter++)
        {
            if (IsNativePointerType(typeSpecs.GetTypeSpecBytes(
                parameters.GetTypeSpec(first + parameter))))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasNativePointerValueInBody(int method,
        TrRmMethodDefTableReader methods, TrRmBodyTableReader bodies,
        TrRmTypeSpecTableReader typeSpecs)
    {
        var bodyRow = methods.GetBodyRow(method);
        if (bodyRow == 0)
        {
            return false;
        }

        var body = bodies.GetRow(bodyRow);
        if (body.Length < 2 || (body[0] & 0x0C) is not (0x04 or 0x08)
            || (body[0] & 3) == 3)
        {
            return false;
        }

        var width = (body[0] & 3) switch { 0 => 1, 1 => 2, _ => 4 };
        if (body.Length < 1 + width)
        {
            throw InvalidMetadata();
        }

        var position = 1;
        var bodyForm = body[0] & 0x0C;
        if (bodyForm == 0x08)
        {
            _ = ReadBodyCount(body, ref position, width);
            _ = ReadBodyCount(body, ref position, width);
            _ = ReadBodyCount(body, ref position, width);
        }
        var typeCount = ReadBodyCount(body, ref position, width);
        if (typeCount > (uint)((body.Length - position) / sizeof(uint)))
        {
            throw InvalidMetadata();
        }

        for (var type = 0u; type < typeCount; type++)
        {
            var typeSpec = BinaryPrimitives.ReadUInt32LittleEndian(
                body.Slice(position, sizeof(uint)));
            position += sizeof(uint);
            if (typeSpec == 0 || typeSpec > (uint)typeSpecs.Count)
            {
                throw InvalidMetadata();
            }
            if (IsNativePointerType(typeSpecs.GetRow((int)typeSpec)))
            {
                return true;
            }
        }
        return false;
    }

    private static uint ReadBodyCount(ReadOnlySpan<byte> body, ref int position, int width)
    {
        if (width > body.Length - position)
        {
            throw InvalidMetadata();
        }

        var value = width switch
        {
            1 => body[position],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(position, 2)),
            _ => BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(position, 4)),
        };
        position += width;
        return value;
    }

    private static bool IsNativePointerType(ReadOnlySpan<byte> typeSpec)
        => typeSpec.Length >= 3 && typeSpec[2] == 0x8C
            && typeSpec[0] is 0x01 or 0x0C;

    private static InvalidDataException InvalidMetadata()
        => new("Invalid TRRM structural metadata.");
}
