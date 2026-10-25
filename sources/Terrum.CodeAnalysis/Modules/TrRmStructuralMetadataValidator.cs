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
            TrRmModuleTableReader modules, TrRmModuleRefTableReader moduleRefs,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
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
            TrRmPredicateCaptureTableReader predicateCaptures,
            TrRmConstantSourceTableReader constantSources,
            TrRmTargetLayoutDependencyTableReader targetLayoutDependencies,
            TrRmTargetLayoutDependencyFieldTableReader targetLayoutDependencyFields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmFieldDefTableReader fields,
            TrRmLayoutTableReader layouts, TrRmLayoutFieldTableReader layoutFields,
            TrRmTargetProfileTableReader targetProfiles,
            TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
            TrRmMethodDefTableReader methods, TrRmBodyTableReader bodies,
            TrRmContractTableReader contracts, TrRmNativeImportTableReader nativeImports,
            TrRmNativeCallableTableReader nativeCallables,
            TrRmNativeFunctionPointerSignatureTableReader nativeFunctionPointerSignatures,
            TrRmNativeVTableTableReader nativeVTables, TrRmImportTableReader imports,
            TrRmArtifactSchema schema)
        {
            Strings = strings;
            Modules = modules;
            ModuleRefs = moduleRefs;
            TypeDefs = typeDefs;
            TypeSpecs = typeSpecs;
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
            PredicateCaptures = predicateCaptures;
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
            NativeCallables = nativeCallables;
            NativeFunctionPointerSignatures = nativeFunctionPointerSignatures;
            NativeVTables = nativeVTables;
            Imports = imports;
            Schema = schema;
        }

        internal TrRmStringTableReader Strings { get; }
        internal TrRmModuleTableReader Modules { get; }
        internal TrRmModuleRefTableReader ModuleRefs { get; }
        internal TrRmTypeDefTableReader TypeDefs { get; }
        internal TrRmTypeSpecTableReader TypeSpecs { get; }
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
        internal TrRmPredicateCaptureTableReader PredicateCaptures { get; }
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
        internal TrRmNativeImportTableReader NativeImports { get; }
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
                methodDefRow, Methods.GetOwnerModule(methodDefRow), TypeDefs, TypeSpecs,
                GenericParams, Methods, Signatures, Parameters, Fields,
                FieldPaths, ConstantSources, Imports, ModuleRefs);
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
        BodyInspection inspection, bool developmentProfile)
    {
        if (index.Schema != (developmentProfile
            ? TrRmArtifactSchema.Development : TrRmArtifactSchema.Standard))
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
        _ = index.TryGetSection(0x0038, out _, out var predicateReadFootprintCount);
        _ = index.TryGetSection(0x003B, out _, out var predicateNodeCount);
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
        _ = index.TryGetSection(0x003F, out _, out var frozenValueNodeCount);
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

        if (blobCount != 0)
        {
            _ = new TrRmBlobTableReader(blobBytes, blobCount);
        }

        var bodies = bodyCount == 0 ? default
            : new TrRmBodyTableReader(bodyBytes, bodyCount);

        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        strings.ValidateShapes(stringCount);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
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
            TrRmFeatureManifestValidator.Validate(features, modules);
        }
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, moduleRefCount,
            strings, modules);
        var selectedNativeFunctionPointerSignatureCount = developmentProfile
            ? nativeFunctionPointerSignatureCount : 0;
        var typeSpecs = typeSpecCount == 0
            && selectedNativeFunctionPointerSignatureCount == 0 ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
                typeDefCount, importCount, genericParamCount,
                selectedNativeFunctionPointerSignatureCount);
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
            : new TrRmParamTableReader(paramBytes, paramCount, signatures, typeSpecs);
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
        var contracts = new TrRmContractTableReader(contractBytes, contractCount,
            types, methods, signatures, parameters, typeSpecs, developmentProfile);
        var imports = new TrRmImportTableReader(importBytes, importCount,
            strings, moduleRefs, types, methods, properties,
            interfaces, associatedTypeDefs, fields);
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
        var exports = new TrRmExportTableReader(exportBytes, exportCount,
            strings, types, methods, properties, imports,
            interfaces, associatedTypeDefs, fields);
        var nativeImports = developmentProfile
            ? new TrRmNativeImportTableReader(nativeImportBytes,
                nativeImportCount, strings, methods, signatures, parameters,
                typeSpecs, contracts, layouts)
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
                developmentProfile);
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
        return new ValidatedExecutionTables(strings, modules, moduleRefs, types, typeSpecs,
            generics,
            constraints,
            interfaces, associatedTypeDefs, interfaceWitnesses, witnessMembers,
            witnessAssociatedTypes,
            interfaceBases, interfaceMethods,
            interfaceConstDefs, associatedConstDefs, associatedRelations,
            unionCaseDefs, unionCaseFields, builderRegistrations, textTargets,
            textContentShapes, textCaptureShapes, textFragmentShapes,
            predicateGraphs, predicateCaptures,
            constantSources,
            targetLayoutDependencies, targetLayoutDependencyFields, fieldPaths,
            fields, layouts, layoutFields,
            targetProfiles, signatures, parameters, methods, bodies, contracts, nativeImports,
            nativeCallables, nativeFunctionPointerSignatures, nativeVTables, imports,
            index.Schema);
    }

    private static void ValidateNativeBoundaryFeatureAssignments(
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmMethodDefTableReader methods,
        TrRmBodyTableReader bodies, TrRmContractTableReader contracts,
        TrRmLayoutTableReader layouts,
        TrRmNativeImportTableReader nativeImports,
        TrRmNativeCallableTableReader nativeCallables,
        TrRmNativeFunctionPointerSignatureTableReader nativeFunctionPointerSignatures,
        TrRmNativeVTableTableReader nativeVTables, bool developmentProfile)
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
                developmentProfile);
            var declaresRootlessNativeValues = RequiresModuleFeature(features, modules, module,
                TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                TrRmDevelopmentFeatureTableReader.RootlessNativeValuesFeatureId,
                developmentProfile);
            var declaresDirectNativeCalls = RequiresModuleFeature(features, modules, module,
                TrRmFeatureTableReader.DirectNativeCallsFeatureId,
                TrRmDevelopmentFeatureTableReader.DirectNativeCallsFeatureId,
                developmentProfile);
            var declaresNativeInterfaceCalls = RequiresModuleFeature(features, modules, module,
                TrRmFeatureTableReader.NativeInterfaceCallsFeatureId,
                TrRmDevelopmentFeatureTableReader.NativeVTableCallsFeatureId,
                developmentProfile);
            var declaresNativeCallableEntries = RequiresModuleFeature(features, modules, module,
                TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
                TrRmDevelopmentFeatureTableReader.NativeCallableEntriesFeatureId,
                developmentProfile);

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
        uint developmentFeatureId, bool developmentProfile)
    {
        if (!developmentProfile)
        {
            return TrRmFeatureManifestValidator.Requires(
                features, modules, module, currentFeatureId);
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
