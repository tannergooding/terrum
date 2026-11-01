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
            TrRmEnumDefTableReader enumDefs,
            TrRmEnumMemberDefTableReader enumMembers,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors,
            TrRmGenericParamTableReader genericParams,
            TrRmConstraintTableReader constraints,
            TrRmInterfaceDefTableReader interfaces,
            TrRmAssociatedTypeDefTableReader associatedTypeDefs,
            TrRmInterfaceWitnessTableReader interfaceWitnesses,
            TrRmWitnessMemberTableReader witnessMembers,
            TrRmWitnessAssociatedTypeTableReader witnessAssociatedTypes,
            TrRmWitnessAssociatedConstTableReader witnessAssociatedConsts,
            TrRmInterfaceBaseTableReader interfaceBases,
            TrRmInterfaceMethodTableReader interfaceMethods,
            TrRmInterfaceConstDefTableReader interfaceConstDefs,
            TrRmAssociatedConstDefTableReader associatedConstDefs,
            TrRmImmutableDataTableReader immutableData,
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
            TrRmPredicateNodeTableReader predicateNodes,
            TrRmPredicateNodeOperandTableReader predicateNodeOperands,
            TrRmPredicateReadRootTableReader predicateReadRoots,
            TrRmPredicateReadDependencyTableReader predicateReadDependencies,
            TrRmPredicateOperationTableReader predicateOperations,
            TrRmPredicateCallTableReader predicateCalls,
            TrRmPredicateTypeUseTableReader predicateTypeUses,
            TrRmPredicateControlTableReader predicateControls,
            TrRmEvaluationReadBoundTableReader evaluationReadBounds,
            TrRmPredicateTypePatternTableReader predicateTypePatterns,
            TrRmBlobTableReader blobs,
            TrRmFrozenValueNodeTableReader frozenValueNodes,
            TrRmConstantSourceTableReader constantSources,
            TrRmConstantExecutionSubstitutionTableReader constantExecutionSubstitutions,
            TrRmConstantExecutionArgumentTableReader constantExecutionArguments,
            TrRmConstantExecutionDependencyTableReader constantExecutionDependencies,
            TrRmLogicalInputViewTableReader logicalInputViews,
            TrRmLogicalViewPathEntryTableReader logicalViewPathEntries,
            TrRmConstantExecutionTableReader constantExecutions,
            List<TrRmConstantExecutionUse> constantExecutionUses,
            TrRmTargetLayoutDependencyTableReader targetLayoutDependencies,
            TrRmTargetLayoutDependencyFieldTableReader targetLayoutDependencyFields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmFieldDefTableReader fields,
            TrRmStaticFieldTableReader staticFields,
            TrRmEagerGroupTableReader eagerGroups,
            TrRmStaticFieldContractTableReader staticFieldContracts,
            TrRmEagerInitializerTableReader eagerInitializers,
            TrRmLazyUnitTableReader lazyUnits,
            TrRmStaticImmutablePublicationTableReader staticPublications,
            TrRmStaticDependencyTableReader staticDependencies,
            TrRmStaticAccessTableReader staticAccesses,
            TrRmIntrinsicRegistrationTableReader intrinsicRegistrations,
            TrRmLayoutTableReader layouts, TrRmLayoutFieldTableReader layoutFields,
            TrRmTargetProfileTableReader targetProfiles,
            TrRmFixedArrayLayoutTableReader fixedArrayLayouts,
            TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
            TrRmMethodDefTableReader methods, TrRmBodyTableReader bodies,
            TrRmContractTableReader contracts,
            TrRmDevelopmentNativeImportTableReader nativeImports,
            TrRmNativeImportTableReader standardNativeImports,
            TrRmNativeExportTableReader nativeExports,
            TrRmNativeCallableTableReader nativeCallables,
            TrRmNativeFunctionPointerSignatureTableReader nativeFunctionPointerSignatures,
            TrRmNativeVTableTableReader nativeVTables, TrRmImportTableReader imports,
            TrRmExportTableReader exports,
            TrRmArtifactSchema schema,
            TrRmA3LayoutResourceLedger? a3LayoutResourceLedger)
        {
            Strings = strings;
            Features = features;
            Modules = modules;
            ModuleRefs = moduleRefs;
            TypeDefs = typeDefs;
            EnumDefs = enumDefs;
            EnumMembers = enumMembers;
            TypeSpecs = typeSpecs;
            NativeFunctionPointerAnchors = nativeFunctionPointerAnchors;
            GenericParams = genericParams;
            Constraints = constraints;
            Interfaces = interfaces;
            AssociatedTypeDefs = associatedTypeDefs;
            InterfaceWitnesses = interfaceWitnesses;
            WitnessMembers = witnessMembers;
            WitnessAssociatedTypes = witnessAssociatedTypes;
            WitnessAssociatedConsts = witnessAssociatedConsts;
            InterfaceBases = interfaceBases;
            InterfaceMethods = interfaceMethods;
            InterfaceConstDefs = interfaceConstDefs;
            AssociatedConstDefs = associatedConstDefs;
            ImmutableData = immutableData;
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
            PredicateNodes = predicateNodes;
            PredicateNodeOperands = predicateNodeOperands;
            PredicateReadRoots = predicateReadRoots;
            PredicateReadDependencies = predicateReadDependencies;
            PredicateOperations = predicateOperations;
            PredicateCalls = predicateCalls;
            PredicateTypeUses = predicateTypeUses;
            PredicateControls = predicateControls;
            EvaluationReadBounds = evaluationReadBounds;
            PredicateTypePatterns = predicateTypePatterns;
            Blobs = blobs;
            FrozenValueNodes = frozenValueNodes;
            ConstantSources = constantSources;
            ConstantExecutionSubstitutions = constantExecutionSubstitutions;
            ConstantExecutionArguments = constantExecutionArguments;
            ConstantExecutionDependencies = constantExecutionDependencies;
            LogicalInputViews = logicalInputViews;
            LogicalViewPathEntries = logicalViewPathEntries;
            ConstantExecutions = constantExecutions;
            ConstantExecutionUses = constantExecutionUses;
            TargetLayoutDependencies = targetLayoutDependencies;
            TargetLayoutDependencyFields = targetLayoutDependencyFields;
            FieldPaths = fieldPaths;
            Fields = fields;
            StaticFields = staticFields;
            EagerGroups = eagerGroups;
            StaticFieldContracts = staticFieldContracts;
            EagerInitializers = eagerInitializers;
            LazyUnits = lazyUnits;
            StaticPublications = staticPublications;
            StaticDependencies = staticDependencies;
            StaticAccesses = staticAccesses;
            IntrinsicRegistrations = intrinsicRegistrations;
            Layouts = layouts;
            LayoutFields = layoutFields;
            TargetProfiles = targetProfiles;
            FixedArrayLayouts = fixedArrayLayouts;
            Signatures = signatures;
            Parameters = parameters;
            Methods = methods;
            Bodies = bodies;
            Contracts = contracts;
            NativeImports = nativeImports;
            StandardNativeImports = standardNativeImports;
            NativeExports = nativeExports;
            NativeCallables = nativeCallables;
            NativeFunctionPointerSignatures = nativeFunctionPointerSignatures;
            NativeVTables = nativeVTables;
            Imports = imports;
            Exports = exports;
            Schema = schema;
            A3LayoutResourceLedger = a3LayoutResourceLedger;
        }

        internal TrRmStringTableReader Strings { get; }
        internal TrRmFeatureTableReader Features { get; }
        internal TrRmModuleTableReader Modules { get; }
        internal TrRmModuleRefTableReader ModuleRefs { get; }
        internal TrRmTypeDefTableReader TypeDefs { get; }
        internal TrRmEnumDefTableReader EnumDefs { get; }
        internal TrRmEnumMemberDefTableReader EnumMembers { get; }
        internal TrRmTypeSpecTableReader TypeSpecs { get; }
        internal TrRmNativeFunctionPointerAnchorTableReader NativeFunctionPointerAnchors { get; }
        internal TrRmGenericParamTableReader GenericParams { get; }
        internal TrRmConstraintTableReader Constraints { get; }
        internal TrRmInterfaceDefTableReader Interfaces { get; }
        internal TrRmAssociatedTypeDefTableReader AssociatedTypeDefs { get; }
        internal TrRmInterfaceWitnessTableReader InterfaceWitnesses { get; }
        internal TrRmWitnessMemberTableReader WitnessMembers { get; }
        internal TrRmWitnessAssociatedTypeTableReader WitnessAssociatedTypes { get; }
        internal TrRmWitnessAssociatedConstTableReader WitnessAssociatedConsts { get; }
        internal TrRmInterfaceBaseTableReader InterfaceBases { get; }
        internal TrRmInterfaceMethodTableReader InterfaceMethods { get; }
        internal TrRmInterfaceConstDefTableReader InterfaceConstDefs { get; }
        internal TrRmAssociatedConstDefTableReader AssociatedConstDefs { get; }
        internal TrRmImmutableDataTableReader ImmutableData { get; }
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
        internal TrRmPredicateNodeTableReader PredicateNodes { get; }
        internal TrRmPredicateNodeOperandTableReader PredicateNodeOperands { get; }
        internal TrRmPredicateReadRootTableReader PredicateReadRoots { get; }
        internal TrRmPredicateReadDependencyTableReader PredicateReadDependencies { get; }
        internal TrRmPredicateOperationTableReader PredicateOperations { get; }
        internal TrRmPredicateCallTableReader PredicateCalls { get; }
        internal TrRmPredicateTypeUseTableReader PredicateTypeUses { get; }
        internal TrRmPredicateControlTableReader PredicateControls { get; }
        internal TrRmEvaluationReadBoundTableReader EvaluationReadBounds { get; }
        internal TrRmPredicateTypePatternTableReader PredicateTypePatterns { get; }
        internal TrRmBlobTableReader Blobs { get; }
        internal TrRmFrozenValueNodeTableReader FrozenValueNodes { get; }
        internal TrRmConstantSourceTableReader ConstantSources { get; }
        internal TrRmConstantExecutionSubstitutionTableReader ConstantExecutionSubstitutions { get; }
        internal TrRmConstantExecutionArgumentTableReader ConstantExecutionArguments { get; }
        internal TrRmConstantExecutionDependencyTableReader ConstantExecutionDependencies { get; }
        internal TrRmLogicalInputViewTableReader LogicalInputViews { get; }
        internal TrRmLogicalViewPathEntryTableReader LogicalViewPathEntries { get; }
        internal TrRmConstantExecutionTableReader ConstantExecutions { get; }
        internal List<TrRmConstantExecutionUse> ConstantExecutionUses { get; }
        internal TrRmTargetLayoutDependencyTableReader TargetLayoutDependencies { get; }
        internal TrRmTargetLayoutDependencyFieldTableReader TargetLayoutDependencyFields { get; }
        internal TrRmFieldPathTableReader FieldPaths { get; }
        internal TrRmFieldDefTableReader Fields { get; }
        internal TrRmStaticFieldTableReader StaticFields { get; }
        internal TrRmEagerGroupTableReader EagerGroups { get; }
        internal TrRmStaticFieldContractTableReader StaticFieldContracts { get; }
        internal TrRmEagerInitializerTableReader EagerInitializers { get; }
        internal TrRmLazyUnitTableReader LazyUnits { get; }
        internal TrRmStaticImmutablePublicationTableReader StaticPublications { get; }
        internal TrRmStaticDependencyTableReader StaticDependencies { get; }
        internal TrRmStaticAccessTableReader StaticAccesses { get; }
        internal TrRmIntrinsicRegistrationTableReader IntrinsicRegistrations { get; }
        internal TrRmLayoutTableReader Layouts { get; }
        internal TrRmLayoutFieldTableReader LayoutFields { get; }
        internal TrRmTargetProfileTableReader TargetProfiles { get; }
        internal TrRmFixedArrayLayoutTableReader FixedArrayLayouts { get; }
        internal TrRmMethodSigTableReader Signatures { get; }
        internal TrRmParamTableReader Parameters { get; }
        internal TrRmMethodDefTableReader Methods { get; }
        internal TrRmBodyTableReader Bodies { get; }
        internal TrRmContractTableReader Contracts { get; }
        internal TrRmDevelopmentNativeImportTableReader NativeImports { get; }
        internal TrRmNativeImportTableReader StandardNativeImports { get; }
        internal TrRmNativeExportTableReader NativeExports { get; }
        internal TrRmNativeCallableTableReader NativeCallables { get; }
        internal TrRmNativeFunctionPointerSignatureTableReader NativeFunctionPointerSignatures { get; }
        internal TrRmNativeVTableTableReader NativeVTables { get; }
        internal TrRmImportTableReader Imports { get; }
        internal TrRmExportTableReader Exports { get; }
        internal TrRmArtifactSchema Schema { get; }
        internal TrRmA3LayoutResourceLedger? A3LayoutResourceLedger { get; }

        internal void ValidateRegisterBodyMapsForMethod(int methodDefRow)
        {
            if (Schema is not (TrRmArtifactSchema.Standard
                    or TrRmArtifactSchema.ForeignInvocationDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                    or TrRmArtifactSchema.ScalarFrozenValuesDraft
                    or TrRmArtifactSchema.NominalEnumsDraft
                    or TrRmArtifactSchema.RootPinRuntimeDraft
                    or TrRmArtifactSchema.GeneralPredicateGraphsDraft
                    or TrRmArtifactSchema.RegisterExceptionRegionsDraft)
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
                Modules, TypeDefs, EnumDefs, TypeSpecs, GenericParams, Methods, Signatures,
                Parameters, Fields, FieldPaths, InterfaceWitnesses, ConstantSources,
                FrozenValueNodes,
                Blobs, Contracts, Imports, NativeFunctionPointerAnchors,
                StandardNativeImports, ModuleRefs, Layouts, FixedArrayLayouts,
                TargetProfiles);
        }

        internal void ValidateNonBodyTypeSpecModuleContexts()
        {
            if (Schema.IsDevelopmentProfile())
            {
                return;
            }

            A3LayoutResourceLedger?.ConsumeWork(
                checked((ulong)Modules.Count + 1UL));
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

            for (var staticField = 1; staticField <= StaticFields.Count; staticField++)
            {
                var ownerType = StaticFields.GetOwnerTypeDefRow(staticField);
                ValidateTypeSpecModuleContext(StaticFields.GetTypeSpecRef(staticField),
                    TypeDefs.GetOwnerModule(ownerType), visitedTypeSpecRows);
            }

            for (var execution = 1; execution <= ConstantExecutions.Count; execution++)
            {
                var (first, count)
                    = ConstantExecutions.GetSubstitutionRange(execution);
                var ownerModule = ConstantExecutions.GetOwnerModule(execution);
                for (var ordinal = 0; ordinal < count; ordinal++)
                {
                    var substitution = first + ordinal;
                    if (ConstantExecutionSubstitutions.GetKind(substitution) == 1)
                    {
                        ValidateTypeSpecModuleContext(
                            ConstantExecutionSubstitutions
                                .GetTypeSpecRef(substitution),
                            ownerModule, visitedTypeSpecRows);
                    }
                    else
                    {
                        var source = checked((int)
                            ConstantExecutionSubstitutions.GetReference(substitution));
                        if (ConstantSources.GetKind(source) == 2)
                        {
                            A3LayoutResourceLedger?.ConsumeWork();
                            ConstantExecutionUses.Add(
                                new TrRmConstantExecutionUse(
                                    checked((int)ConstantSources
                                        .GetPayloadReference(source)),
                                    ownerModule, 0));
                        }
                    }
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

            ValidateFixedArrayLayoutContext(typeSpecRef, ownerModule);
            var visited = visitedTypeSpecRows[ownerModule];
            if (visited is null)
            {
                A3LayoutResourceLedger?.ConsumeWork();
                visited = new HashSet<int>();
                visitedTypeSpecRows[ownerModule] = visited;
            }

            TrRmConstantSourceValueValidator.ValidateConstructedTypeSpecValues(
                typeSpecRef, ownerModule, TypeSpecs, TypeDefs, EnumDefs,
                GenericParams, Imports, ConstantSources, FrozenValueNodes,
                Blobs, Features, Modules, visited, ConstantExecutionUses,
                A3LayoutResourceLedger);
        }

        private void ValidateFixedArrayLayoutContext(uint typeSpecRef,
            int ownerModule)
        {
            if (!Schema.UsesForeignInvocationStorageFixedArrays())
            {
                return;
            }

            uint profileReference = 0;
            A3LayoutResourceLedger?.ConsumeWork(2);
            var pending = new Stack<(uint TypeSpecRef, ulong Depth)>();
            var visited = new HashSet<int>();
            A3LayoutResourceLedger?.ReserveWorkAtDepth(1, 1);
            pending.Push((typeSpecRef, 1));
            while (pending.TryPop(out var current))
            {
                A3LayoutResourceLedger?.ConsumeWork();
                A3LayoutResourceLedger?.CheckDepthFromCurrent(current.Depth);
                var reference = TypeSpecs.GetTypeSpecRef(current.TypeSpecRef);
                if (!reference.IsRow)
                {
                    continue;
                }

                A3LayoutResourceLedger?.ConsumeWork(2);
                if (!visited.Add(reference.Row))
                {
                    continue;
                }

                var row = TypeSpecs.GetRow(reference.Row);
                switch (reference.Form)
                {
                    case 0x05:
                    {
                        var typeDef = TypeSpecs.GetNominalTypeDefRow(
                            current.TypeSpecRef, TypeDefs, Imports);
                        var (firstFormal, formalCount)
                            = TypeDefs.GetGenericRange(typeDef);
                        for (var ordinal = 0; ordinal < formalCount; ordinal++)
                        {
                            A3LayoutResourceLedger?.ConsumeWork();
                            if (GenericParams.GetFormalKind(firstFormal + ordinal) == 1)
                            {
                                var genericArgumentRef
                                    = TypeSpecs.GetConstructedArgument(
                                        reference.Row, ordinal);
                                var genericArgumentDepth
                                    = checked(current.Depth + 1);
                                A3LayoutResourceLedger?.ReserveWorkAtDepth(1,
                                    genericArgumentDepth);
                                pending.Push((genericArgumentRef,
                                    genericArgumentDepth));
                            }
                        }
                        break;
                    }

                    case >= 0x06 and <= 0x0B:
                    case 0x0E:
                    {
                        A3LayoutResourceLedger?.ConsumeWork(
                            sizeof(uint) + 1UL);
                        var wrapperChildRef
                            = BinaryPrimitives.ReadUInt32LittleEndian(row[2..]);
                        var wrapperChildDepth = checked(current.Depth + 1);
                        A3LayoutResourceLedger?.ReserveWorkAtDepth(1,
                            wrapperChildDepth);
                        pending.Push((wrapperChildRef, wrapperChildDepth));
                        break;
                    }

                    case 0x0C:
                    {
                        A3LayoutResourceLedger?.ConsumeWork(
                            sizeof(uint) + 1UL);
                        var qualifiedChildRef
                            = BinaryPrimitives.ReadUInt32LittleEndian(row[3..]);
                        var qualifiedChildDepth = checked(current.Depth + 1);
                        A3LayoutResourceLedger?.ReserveWorkAtDepth(1,
                            qualifiedChildDepth);
                        pending.Push((qualifiedChildRef, qualifiedChildDepth));
                        break;
                    }

                    case 0x11:
                        if (profileReference == 0)
                        {
                            profileReference = TargetProfiles.TryGetProfile(
                                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                                out var profileRow)
                                    ? checked((uint)profileRow)
                                    : throw InvalidMetadata();
                        }

                        if (FixedArrayLayouts.FindRow(ownerModule,
                                (uint)reference.Row, profileReference) == 0)
                        {
                            throw InvalidMetadata();
                        }

                        A3LayoutResourceLedger?.ConsumeWork(
                            sizeof(uint) + 1UL);
                        var arrayElementRef
                            = BinaryPrimitives.ReadUInt32LittleEndian(row[2..]);
                        var arrayElementDepth = checked(current.Depth + 1);
                        A3LayoutResourceLedger?.ReserveWorkAtDepth(1,
                            arrayElementDepth);
                        pending.Push((arrayElementRef, arrayElementDepth));
                        break;
                }
            }
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

    internal static ValidatedExecutionTables
        ValidateDevelopmentFunctionPointerEffectsDraftAndGetExecutionTables(
            TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: true,
            developmentFunctionPointerEffectsDraft: true);

    internal static ValidatedExecutionTables ValidateCopyOriginDraftAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            copyOriginDraft: true);

    internal static ValidatedExecutionTables ValidateForeignInvocationDraftAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            foreignInvocationDraft: true);

    internal static ValidatedExecutionTables
        ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            foreignInvocationStorageAddressDraft: true);

    internal static ValidatedExecutionTables
        ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
        TrRmArtifactIndex index,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger = null)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            foreignInvocationStorageFixedArrayDraft: true,
            a3LayoutResourceLedger: a3LayoutResourceLedger);

    internal static ValidatedExecutionTables
        ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            foreignInvocationStorageFixedArrayEmptyInitializationDraft: true);

    internal static ValidatedExecutionTables
        ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            foreignInvocationStorageFixedArrayEmptyUnitInitializationDraft: true);

    internal static ValidatedExecutionTables
        ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
        TrRmArtifactIndex index,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger = null,
        bool createA3ResourceLedger = true)
    {
        var resourceLedger = a3LayoutResourceLedger
            ?? (createA3ResourceLedger
                ? new TrRmA3LayoutResourceLedger() : null);
        try
        {
            return ValidateCore(index, BodyInspection.None,
                developmentProfile: false,
                foreignInvocationStorageFixedArrayGenericDraft: true,
                a3LayoutResourceLedger: resourceLedger);
        }
        catch (TrRmResourceLimitException)
        {
            throw;
        }
        catch (OverflowException exception)
        {
            if (resourceLedger is null)
            {
                throw;
            }

            if (TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
                    out var kind)
                && kind
                    == TrRmA3AdmissionFailureKind.TargetLayoutOverflow)
            {
                throw;
            }

            var invalid = new InvalidDataException(
                "Invalid TRRM structural metadata.", exception);
            resourceLedger.AdmissionScope.Mark(invalid,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw invalid;
        }
        catch (InvalidDataException exception)
        {
            if (resourceLedger is not null
                && !TrRmA3AdmissionFailureMarkers.TryGet(exception, out _))
            {
                resourceLedger.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.InvalidArtifact);
            }

            throw;
        }
        catch (NotSupportedException exception)
        {
            if (resourceLedger is not null
                && !TrRmA3AdmissionFailureMarkers.TryGet(exception, out _))
            {
                resourceLedger.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.UnsupportedAdmission);
            }

            throw;
        }
    }

    internal static ValidatedExecutionTables ValidateNominalEnumsDraftAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            nominalEnumsDraft: true);

    internal static void ValidateNominalEnumsDraftProviderBindings(
        ReadOnlySpan<byte> consumerArtifact,
        IReadOnlyList<byte[]> providerArtifacts)
    {
        ArgumentNullException.ThrowIfNull(providerArtifacts);
        var consumer = ValidateNominalEnumsDraftAndGetExecutionTables(
            TrRmArtifactIndex.ForNominalEnumsDraft(consumerArtifact));
        TrRmEnumProviderBindingValidator.Validate(consumer, providerArtifacts);
    }

    internal static ValidatedExecutionTables
        ValidateScalarFrozenValuesDraftAndGetExecutionTables(TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            scalarFrozenValuesDraft: true);

    internal static ValidatedExecutionTables
        ValidateRootPinRuntimeDraftAndGetExecutionTables(TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            rootPinRuntimeDraft: true);

    internal static ValidatedExecutionTables
        ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
            TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            generalPredicateGraphsDraft: true);

    internal static ValidatedExecutionTables
        ValidateRegisterExceptionRegionsDraftAndGetExecutionTables(
            TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None, developmentProfile: false,
            registerExceptionRegionsDraft: true);

    internal static void ValidateGeneralPredicateGraphsDraftProviderBindings(
        ReadOnlySpan<byte> consumerArtifact,
        IReadOnlyList<byte[]> providerArtifacts)
    {
        ArgumentNullException.ThrowIfNull(providerArtifacts);
        var consumer = ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
            TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(consumerArtifact));
        TrRmIntrinsicRegistrationProviderBindingValidator.Validate(
            consumer, providerArtifacts);
    }

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
        bool copyOriginDraft = false, bool foreignInvocationDraft = false,
        bool developmentFunctionPointerEffectsDraft = false,
        bool nominalEnumsDraft = false, bool scalarFrozenValuesDraft = false,
        bool rootPinRuntimeDraft = false, bool generalPredicateGraphsDraft = false,
        bool foreignInvocationStorageAddressDraft = false,
        bool foreignInvocationStorageFixedArrayDraft = false,
        bool foreignInvocationStorageFixedArrayEmptyInitializationDraft = false,
        bool foreignInvocationStorageFixedArrayEmptyUnitInitializationDraft = false,
        bool foreignInvocationStorageFixedArrayGenericDraft = false,
        bool registerExceptionRegionsDraft = false,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger = null)
    {
        var selectedSchema = developmentFunctionPointerEffectsDraft
            ? TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
            : developmentProfile
            ? TrRmArtifactSchema.Development
            : copyOriginDraft
                ? TrRmArtifactSchema.CopyOriginDraft
                : foreignInvocationStorageFixedArrayEmptyUnitInitializationDraft
                    ? TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                : foreignInvocationStorageFixedArrayEmptyInitializationDraft
                    ? TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                : foreignInvocationStorageFixedArrayDraft
                    ? TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                : foreignInvocationStorageFixedArrayGenericDraft
                    ? TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                : foreignInvocationStorageAddressDraft
                    ? TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                : foreignInvocationDraft
                    ? TrRmArtifactSchema.ForeignInvocationDraft
                    : nominalEnumsDraft
                        ? TrRmArtifactSchema.NominalEnumsDraft
                    : scalarFrozenValuesDraft
                        ? TrRmArtifactSchema.ScalarFrozenValuesDraft
                    : rootPinRuntimeDraft
                        ? TrRmArtifactSchema.RootPinRuntimeDraft
                    : generalPredicateGraphsDraft
                        ? TrRmArtifactSchema.GeneralPredicateGraphsDraft
                    : registerExceptionRegionsDraft
                        ? TrRmArtifactSchema.RegisterExceptionRegionsDraft
                        : TrRmArtifactSchema.Standard;
        if (index.Schema != selectedSchema)
        {
            throw InvalidMetadata();
        }
        var selectedA3LayoutResourceLedger = a3LayoutResourceLedger;
        foreignInvocationStorageFixedArrayEmptyInitializationDraft
            |= foreignInvocationStorageFixedArrayEmptyUnitInitializationDraft;
        foreignInvocationStorageFixedArrayDraft
            |= foreignInvocationStorageFixedArrayEmptyInitializationDraft;
        foreignInvocationDraft |= foreignInvocationStorageAddressDraft
            || foreignInvocationStorageFixedArrayDraft
            || foreignInvocationStorageFixedArrayGenericDraft;
        developmentProfile |= developmentFunctionPointerEffectsDraft;

        if (!index.TryGetSection(0x0001, out var moduleBytes, out var moduleCount)
            || !index.TryGetSection(0x0002, out var stringBytes, out var stringCount))
        {
            throw InvalidMetadata();
        }

        _ = index.TryGetSection(0x0004, out var featureBytes, out var featureCount);
        _ = index.TryGetSection(0x0010, out var moduleRefBytes, out var moduleRefCount);
        _ = index.TryGetSection(0x0011, out var typeSpecBytes, out var typeSpecCount);
        _ = index.TryGetSection(0x0013, out var typeDefBytes, out var typeDefCount);
        _ = index.TryGetSection(0x0053, out var enumDefBytes, out var enumDefCount);
        _ = index.TryGetSection(0x0054, out var enumMemberDefBytes,
            out var enumMemberDefCount);
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
        _ = index.TryGetSection(0x003A, out var predicateReadDependencyBytes,
            out var predicateReadDependencyCount);
        _ = index.TryGetSection(0x003B, out var predicateNodeBytes,
            out var predicateNodeCount);
        _ = index.TryGetSection(0x003C, out var predicateNodeOperandBytes,
            out var predicateNodeOperandCount);
        _ = index.TryGetSection(0x003D, out var predicateChildEdgeBytes,
            out var predicateChildEdgeCount);
        _ = index.TryGetSection(0x003E, out var predicateCaptureBytes,
            out var predicateCaptureCount);
        _ = index.TryGetSection(0x0041, out var immutableDataBytes,
            out var immutableDataCount);
        _ = index.TryGetSection(0x004D, out var predicateOperationBytes,
            out var predicateOperationCount);
        _ = index.TryGetSection(0x004E, out var predicateCallBytes,
            out var predicateCallCount);
        _ = index.TryGetSection(0x004F, out var predicateTypeUseBytes,
            out var predicateTypeUseCount);
        _ = index.TryGetSection(0x0050, out var predicateControlBytes,
            out var predicateControlCount);
        _ = index.TryGetSection(0x0051, out var evaluationReadBoundBytes,
            out var evaluationReadBoundCount);
        _ = index.TryGetSection(0x0052, out var predicateTypePatternBytes,
            out var predicateTypePatternCount);
        _ = index.TryGetSection(0x0055, out var staticFieldBytes,
            out var staticFieldCount);
        _ = index.TryGetSection(0x0056, out var staticFieldContractBytes,
            out var staticFieldContractCount);
        _ = index.TryGetSection(0x0057, out var eagerGroupBytes,
            out var eagerGroupCount);
        _ = index.TryGetSection(0x0058, out var eagerInitializerBytes,
            out var eagerInitializerCount);
        _ = index.TryGetSection(0x0059, out var lazyUnitBytes,
            out var lazyUnitCount);
        _ = index.TryGetSection(0x005A, out var staticAccessBytes,
            out var staticAccessCount);
        _ = index.TryGetSection(0x005B, out var staticDependencyBytes,
            out var staticDependencyCount);
        _ = index.TryGetSection(0x005C, out var staticPublicationBytes,
            out var staticPublicationCount);
        _ = index.TryGetSection(0x005D, out var intrinsicRegistrationBytes,
            out var intrinsicRegistrationCount);
        _ = index.TryGetSection(0x005E, out var constantExecutionSubstitutionBytes,
            out var constantExecutionSubstitutionCount);
        _ = index.TryGetSection(0x0030, out var constantSourceBytes,
            out var constantSourceCount);
        _ = index.TryGetSection(0x0031, out var interfaceConstBytes,
            out var interfaceConstCount);
        _ = index.TryGetSection(0x0032, out var associatedConstBytes,
            out var associatedConstCount);
        _ = index.TryGetSection(0x0033, out var witnessAssociatedConstBytes,
            out var witnessAssociatedConstCount);
        _ = index.TryGetSection(0x0034, out var unionCaseDefBytes, out var unionCaseDefCount);
        _ = index.TryGetSection(0x0035, out var unionCaseFieldBytes, out var unionCaseFieldCount);
        _ = index.TryGetSection(0x0036, out var associatedRelationBytes,
            out var associatedRelationCount);
        _ = index.TryGetSection(0x003F, out var frozenValueNodeBytes,
            out var frozenValueNodeCount);
        _ = index.TryGetSection(0x0040, out _, out var frozenValueChildCount);
        _ = index.TryGetSection(0x0042, out var constantExecutionBytes,
            out var constantExecutionCount);
        _ = index.TryGetSection(0x0043, out var constantExecutionArgumentBytes,
            out var constantExecutionArgumentCount);
        _ = index.TryGetSection(0x0044, out var constantExecutionDependencyBytes,
            out var constantExecutionDependencyCount);
        _ = index.TryGetSection(0x0046, out var logicalInputViewBytes,
            out var logicalInputViewCount);
        _ = index.TryGetSection(0x0047, out var logicalViewPathEntryBytes,
            out var logicalViewPathEntryCount);
        _ = index.TryGetSection(0x0049, out _, out var targetValueBindingCount);
        _ = index.TryGetSection(0x0045, out var targetLayoutDependencyBytes,
            out var targetLayoutDependencyCount);
        _ = index.TryGetSection(0x004A, out var targetLayoutDependencyFieldBytes,
            out var targetLayoutDependencyFieldCount);
        _ = index.TryGetSection(0x004B, out var fieldPathBytes, out var fieldPathCount);
        _ = index.TryGetSection(0x0015, out var layoutBytes, out var layoutCount);
        _ = index.TryGetSection(0x0023, out var layoutFieldBytes, out var layoutFieldCount);
        _ = index.TryGetSection(0x0012, out var fixedArrayLayoutBytes,
            out var fixedArrayLayoutCount);
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
            : new TrRmBlobTableReader(blobBytes, blobCount,
                selectedA3LayoutResourceLedger);

        var bodies = bodyCount == 0 ? default
            : new TrRmBodyTableReader(bodyBytes, bodyCount, index.Schema,
                selectedA3LayoutResourceLedger);

        var strings = new TrRmStringTableReader(stringBytes, stringCount,
            selectedA3LayoutResourceLedger);
        strings.ValidateShapes(stringCount);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount,
            index.Schema, selectedA3LayoutResourceLedger);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount, strings,
            moduleRefCount, typeDefCount, methodDefCount, sourceFileCount,
            featureCount, selectedA3LayoutResourceLedger);
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
        var intrinsicRegistrations = index.Schema.UsesGeneralPredicateGraphs()
            ? new TrRmIntrinsicRegistrationTableReader(
                intrinsicRegistrationBytes, intrinsicRegistrationCount,
                modules, index.Schema)
            : default;
        if (index.Schema.UsesGeneralPredicateGraphs())
        {
            for (var registration = 1;
                registration <= intrinsicRegistrations.Count; registration++)
            {
                if (!TrRmFeatureManifestValidator.Requires(features, modules,
                        intrinsicRegistrations.GetOwnerModuleRow(registration),
                        TrRmFeatureTableReader.ConstantExecutionFeatureId,
                        index.Schema))
                {
                    throw InvalidMetadata();
                }
            }
        }
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, moduleRefCount,
            strings, modules, selectedA3LayoutResourceLedger);
        var selectedNativeFunctionPointerSignatureCount = developmentProfile
            ? nativeFunctionPointerSignatureCount : 0;
        var typeSpecs = typeSpecCount == 0
            && selectedNativeFunctionPointerSignatureCount == 0 ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
                typeDefCount, importCount, genericParamCount,
                selectedNativeFunctionPointerSignatureCount, index.Schema,
                methodDefCount, predicateCaptureCount,
                selectedA3LayoutResourceLedger);
        var frozenValueNodes = !developmentProfile && frozenValueNodeCount != 0
            ? new TrRmFrozenValueNodeTableReader(frozenValueNodeBytes,
                frozenValueNodeCount, typeSpecs, blobCount, frozenValueChildCount,
                unionCaseDefCount, selectedA3LayoutResourceLedger)
            : default;
        var types = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, fieldDefCount, genericParamCount, layoutCount,
            contractCount, index.Schema, selectedA3LayoutResourceLedger);
        if (index.Schema.SupportsDeclaredCopyEvidence())
        {
            for (var typeDef = 1; typeDef <= types.Count; typeDef++)
            {
                if ((types.HasDeclaredCopy(typeDef)
                        || types.HasDeclaredUnique(typeDef))
                    && !TrRmFeatureManifestValidator.Requires(features, modules,
                        types.GetOwnerModule(typeDef),
                        TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                        index.Schema))
                {
                    throw InvalidMetadata();
                }
            }
        }

        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, targetProfileCount, index.Schema,
            selectedA3LayoutResourceLedger);
        if (index.Schema.UsesRootPinRuntime())
        {
            for (var module = 1; module <= modules.Count; module++)
            {
                if (TrRmFeatureManifestValidator.Requires(features, modules,
                        module, TrRmFeatureTableReader.RootPinRuntimeFeatureId,
                        index.Schema)
                    && !targetProfiles.TryGetProfile(
                        TrRmTargetProfileTableReader.WindowsX64RootPinProfileId,
                        TrRmTargetProfileTableReader.WindowsX64RootPinProfileRuleRevision,
                        out _))
                {
                    throw InvalidMetadata();
                }
            }
        }
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            types, targetProfiles, layoutFieldCount,
            selectedA3LayoutResourceLedger);
        if (index.Schema.UsesRootPinRuntime()
            && targetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64RootPinProfileId,
                TrRmTargetProfileTableReader.WindowsX64RootPinProfileRuleRevision,
                out var rootPinProfileRow))
        {
            for (var layoutRow = 1; layoutRow <= layouts.Count; layoutRow++)
            {
                if (layouts.GetTargetProfileRef(layoutRow) != (uint)rootPinProfileRow)
                {
                    continue;
                }

                var ownerTypeDef = layouts.GetOwnerTypeDef(layoutRow);
                var ownerModule = types.GetOwnerModule(ownerTypeDef);
                if (!TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule, TrRmFeatureTableReader.RootPinRuntimeFeatureId,
                    index.Schema))
                {
                    throw new NotSupportedException(
                        "A RootPin TargetProfile layout requires RootPinRuntime revision 1 in its owning Module.");
                }
            }
        }
        var enumMembers = index.Schema.SupportsNominalEnums()
            ? new TrRmEnumMemberDefTableReader(enumMemberDefBytes,
                enumMemberDefCount, strings, types)
            : default;
        var enumDefs = index.Schema.SupportsNominalEnums()
            ? new TrRmEnumDefTableReader(enumDefBytes, enumDefCount, types,
                typeSpecs, layouts, targetProfiles, enumMembers)
            : default;
        if (index.Schema.SupportsNominalEnums())
        {
            ValidateNominalEnumFeatureAssignments(enumDefs, types, features,
                modules, index.Schema);
        }
        var fields = fieldDefCount == 0 ? default
            : new TrRmFieldDefTableReader(fieldDefBytes, fieldDefCount, strings, types,
                typeSpecs, selectedA3LayoutResourceLedger);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes,
            layoutFieldCount, layouts, types, fields,
            selectedA3LayoutResourceLedger);
        var fixedArrayLayouts = index.Schema.UsesForeignInvocationStorageFixedArrays()
            ? new TrRmFixedArrayLayoutTableReader(fixedArrayLayoutBytes,
                fixedArrayLayoutCount, typeSpecs, targetProfiles, features,
                modules, index.Schema, selectedA3LayoutResourceLedger)
            : default;
        var signatures = methodSigCount == 0 ? default
            : new TrRmMethodSigTableReader(methodSigBytes, methodSigCount,
                typeSpecs, paramCount, genericParamCount,
                selectedA3LayoutResourceLedger);
        var parameters = paramCount == 0 ? default
            : new TrRmParamTableReader(paramBytes, paramCount, signatures,
                typeSpecs, index.Schema, selectedA3LayoutResourceLedger);
        var methods = new TrRmMethodDefTableReader(methodDefBytes, methodDefCount,
            strings, modules, types, signatures, contractCount, bodyCount,
            selectedA3LayoutResourceLedger);
        var eagerGroups = index.Schema.UsesGeneralPredicateGraphs()
            ? new TrRmEagerGroupTableReader(eagerGroupBytes, eagerGroupCount,
                typeDefCount, eagerInitializerCount, methodDefCount, index.Schema)
            : default;
        var staticFields = index.Schema.UsesGeneralPredicateGraphs()
            ? new TrRmStaticFieldTableReader(staticFieldBytes, staticFieldCount,
                strings, types, typeSpecs, eagerGroups, index.Schema)
            : default;
        var staticFieldContracts = index.Schema.UsesGeneralPredicateGraphs()
            ? new TrRmStaticFieldContractTableReader(staticFieldContractBytes,
                staticFieldContractCount, staticFieldCount, lazyUnitCount,
                staticPublicationCount, staticFields, index.Schema)
            : default;
        var eagerInitializers = index.Schema.UsesGeneralPredicateGraphs()
            ? new TrRmEagerInitializerTableReader(eagerInitializerBytes,
                eagerInitializerCount, eagerGroups, staticFields,
                staticFieldContracts, methodDefCount, index.Schema)
            : default;
        var lazyUnits = index.Schema.UsesGeneralPredicateGraphs()
            ? new TrRmLazyUnitTableReader(lazyUnitBytes, lazyUnitCount,
                eagerGroups, eagerInitializers, staticFields,
                staticFieldContracts, methodDefCount, index.Schema)
            : default;
        var properties = propertyCount == 0 ? default
            : new TrRmPropertyDefTableReader(propertyBytes, propertyCount,
                strings, types, typeSpecs, methods, signatures,
                deferGetterTypeIdentity: !developmentProfile,
                resourceLedger: selectedA3LayoutResourceLedger);
        var selectedConstantSourceCount = developmentProfile ? 0 : constantSourceCount;
        var generics = new TrRmGenericParamTableReader(genericParamBytes, genericParamCount,
            strings, types, methods, signatures, typeSpecs, constraintCount,
            selectedConstantSourceCount, selectedA3LayoutResourceLedger);
        var interfaces = default(TrRmInterfaceDefTableReader);
        var associatedTypeDefs = default(TrRmAssociatedTypeDefTableReader);
        var interfaceWitnesses = default(TrRmInterfaceWitnessTableReader);
        var witnessMembers = default(TrRmWitnessMemberTableReader);
        var witnessAssociatedTypes = default(TrRmWitnessAssociatedTypeTableReader);
        var witnessAssociatedConsts = default(TrRmWitnessAssociatedConstTableReader);
        var interfaceBases = default(TrRmInterfaceBaseTableReader);
        var interfaceMethods = default(TrRmInterfaceMethodTableReader);
        var interfaceConstDefs = default(TrRmInterfaceConstDefTableReader);
        var associatedConstDefs = default(TrRmAssociatedConstDefTableReader);
        var immutableData = default(TrRmImmutableDataTableReader);
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
        var predicateNodes = default(TrRmPredicateNodeTableReader);
        var predicateNodeOperands = default(TrRmPredicateNodeOperandTableReader);
        var predicateReadRoots = default(TrRmPredicateReadRootTableReader);
        var predicateReadDependencies = default(TrRmPredicateReadDependencyTableReader);
        var predicateOperations = default(TrRmPredicateOperationTableReader);
        var predicateCalls = default(TrRmPredicateCallTableReader);
        var predicateTypeUses = default(TrRmPredicateTypeUseTableReader);
        var predicateControls = default(TrRmPredicateControlTableReader);
        var evaluationReadBounds = default(TrRmEvaluationReadBoundTableReader);
        var predicateTypePatterns = default(TrRmPredicateTypePatternTableReader);
        var constantSources = default(TrRmConstantSourceTableReader);
        var constantExecutionSubstitutions
            = default(TrRmConstantExecutionSubstitutionTableReader);
        var targetLayoutDependencies = default(TrRmTargetLayoutDependencyTableReader);
        var targetLayoutDependencyFields = default(TrRmTargetLayoutDependencyFieldTableReader);
        var fieldPaths = default(TrRmFieldPathTableReader);
        var staticPublications
            = default(TrRmStaticImmutablePublicationTableReader);
        var staticDependencies = default(TrRmStaticDependencyTableReader);
        var staticAccesses = default(TrRmStaticAccessTableReader);
        var constantExecutionArguments
            = default(TrRmConstantExecutionArgumentTableReader);
        var constantExecutionDependencies
            = default(TrRmConstantExecutionDependencyTableReader);
        var logicalInputViews = default(TrRmLogicalInputViewTableReader);
        var logicalViewPathEntries = default(TrRmLogicalViewPathEntryTableReader);
        var constantExecutions = default(TrRmConstantExecutionTableReader);
        selectedA3LayoutResourceLedger?.ConsumeWork();
        var constantExecutionUses = new List<TrRmConstantExecutionUse>();
        var imports = default(TrRmImportTableReader);
        if (!developmentProfile)
        {
            builderRegistrations = new TrRmBuilderRegistrationTableReader(
                builderRegistrationBytes, builderRegistrationCount, types,
                typeSpecs, selectedA3LayoutResourceLedger);
            textTargets = new TrRmTextTargetTableReader(textTargetBytes,
                textTargetCount, methods, signatures, typeSpecs,
                selectedA3LayoutResourceLedger);
            textContentShapes = new TrRmTextContentShapeTableReader(
                textContentShapeBytes, textContentShapeCount, types,
                textCaptureShapeCount, textFragmentShapeCount,
                selectedA3LayoutResourceLedger);
            textCaptureShapes = new TrRmTextCaptureShapeTableReader(
                textCaptureShapeBytes, textCaptureShapeCount, typeSpecs,
                textContentShapes, selectedA3LayoutResourceLedger);
            predicateGraphs = new TrRmPredicateGraphTableReader(
                predicateGraphBytes, predicateGraphCount, predicateNodeCount,
                predicateReadFootprintCount,
                selectedA3LayoutResourceLedger);
            predicateChildEdges = new TrRmPredicateChildEdgeTableReader(
                predicateChildEdgeBytes, predicateChildEdgeCount,
                predicateNodeCount, predicateGraphs,
                selectedA3LayoutResourceLedger);
            predicateReadFootprints = new TrRmPredicateReadFootprintTableReader(
                predicateReadFootprintBytes, predicateReadFootprintCount,
                predicateReadRootCount, predicateReadDependencyCount,
                selectedA3LayoutResourceLedger);
            predicateCaptures = new TrRmPredicateCaptureTableReader(
                predicateCaptureBytes, predicateCaptureCount,
                predicateGraphCount, typeSpecs,
                selectedA3LayoutResourceLedger);
            predicateReadRoots = new TrRmPredicateReadRootTableReader(
                predicateReadRootBytes, predicateReadRootCount,
                immutableDataCount, predicateCaptureCount, index.Schema,
                selectedA3LayoutResourceLedger);
            predicateReadDependencies = new TrRmPredicateReadDependencyTableReader(
                predicateReadDependencyBytes, predicateReadDependencyCount,
                methodDefCount, constraintCount, interfaceWitnessCount,
                witnessProjectionCount: 0,
                resourceLedger: selectedA3LayoutResourceLedger);
            predicateNodes = new TrRmPredicateNodeTableReader(
                predicateNodeBytes, predicateNodeCount,
                predicateNodeOperandCount, typeSpecs, index.Schema,
                selectedA3LayoutResourceLedger);
            var predicateOperandCounts = new TrRmPredicateTableRowCounts(
                predicateReadRootCount, frozenValueNodeCount, predicateCaptureCount,
                predicateOperationCount, predicateCallCount,
                predicateTypeUseCount, fieldPathCount, propertyCount,
                unionCaseDefCount, predicateControlCount,
                predicateTypePatternCount);
            predicateNodeOperands = new TrRmPredicateNodeOperandTableReader(
                predicateNodeOperandBytes, predicateNodeOperandCount,
                predicateOperandCounts, index.Schema,
                selectedA3LayoutResourceLedger);
            if (index.Schema.UsesGeneralPredicateGraphs())
            {
                predicateOperations = new TrRmPredicateOperationTableReader(
                    predicateOperationBytes, predicateOperationCount, index.Schema);
                predicateCalls = new TrRmPredicateCallTableReader(
                    predicateCallBytes, predicateCallCount,
                    predicateReadDependencyCount, methodDefCount,
                    constantSourceCount, predicateReadDependencies, typeSpecs,
                    index.Schema);
                predicateTypeUses = new TrRmPredicateTypeUseTableReader(
                    predicateTypeUseBytes, predicateTypeUseCount,
                    typeSpecs, index.Schema);
                predicateControls = new TrRmPredicateControlTableReader(
                    predicateControlBytes, predicateControlCount, index.Schema);
                evaluationReadBounds = new TrRmEvaluationReadBoundTableReader(
                    evaluationReadBoundBytes, evaluationReadBoundCount,
                    methodDefCount, predicateReadFootprintCount, index.Schema);
                predicateTypePatterns = new TrRmPredicateTypePatternTableReader(
                    predicateTypePatternBytes, predicateTypePatternCount,
                    predicateCaptureCount, constantSourceCount,
                    predicateTypeUseCount, index.Schema);
            }
            unionCaseDefs = new TrRmUnionCaseDefTableReader(
                unionCaseDefBytes, unionCaseDefCount, strings, types,
                unionCaseFieldCount, selectedA3LayoutResourceLedger);
            unionCaseFields = new TrRmUnionCaseFieldTableReader(
                unionCaseFieldBytes, unionCaseFieldCount, strings, typeSpecs,
                unionCaseDefs, selectedA3LayoutResourceLedger);
            interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes,
                interfaceDefCount, types, generics, interfaceBaseCount,
                interfaceMethodCount, associatedTypeCount, associatedConstCount,
                interfaceConstCount, selectedA3LayoutResourceLedger);
            interfaceBases = new TrRmInterfaceBaseTableReader(interfaceBaseBytes,
                interfaceBaseCount, typeSpecs, interfaces,
                selectedA3LayoutResourceLedger);
            interfaceMethods = new TrRmInterfaceMethodTableReader(interfaceMethodBytes,
                interfaceMethodCount, interfaces, methods, signatures,
                selectedA3LayoutResourceLedger);
            interfaceConstDefs = new TrRmInterfaceConstDefTableReader(interfaceConstBytes,
                interfaceConstCount, strings, typeSpecs, interfaces,
                constantSourceCount, selectedA3LayoutResourceLedger);
            associatedConstDefs = new TrRmAssociatedConstDefTableReader(
                associatedConstBytes, associatedConstCount, strings, typeSpecs,
                interfaces, selectedA3LayoutResourceLedger);
            if (index.Schema.UsesGeneralPredicateGraphs())
            {
                immutableData = new TrRmImmutableDataTableReader(
                    immutableDataBytes, immutableDataCount, interfaceConstCount,
                    witnessAssociatedConstCount, staticFields.Count,
                    typeSpecs, index.Schema);
            }
            constantSources = new TrRmConstantSourceTableReader(constantSourceBytes,
                constantSourceCount, frozenValueNodeCount, constantExecutionCount,
                targetValueBindingCount, selectedA3LayoutResourceLedger);
            if (index.Schema.UsesGeneralPredicateGraphs())
            {
                constantExecutionSubstitutions
                    = new TrRmConstantExecutionSubstitutionTableReader(
                        constantExecutionSubstitutionBytes,
                        constantExecutionSubstitutionCount, constantSourceCount,
                        typeSpecs, index.Schema);
                staticPublications = new TrRmStaticImmutablePublicationTableReader(
                    staticPublicationBytes, staticPublicationCount,
                    staticFields, staticFieldContracts, typeSpecs, lazyUnits,
                    constantSources, index.Schema, deferTypeIdentity: true);
            }
            associatedTypeDefs = new TrRmAssociatedTypeDefTableReader(
                associatedTypeBytes, associatedTypeCount, strings, typeSpecs,
                interfaces, selectedA3LayoutResourceLedger);
            imports = new TrRmImportTableReader(importBytes, importCount,
                strings, moduleRefs, types, methods, properties,
                interfaces, associatedTypeDefs, fields, features, modules,
                index.Schema, associatedConstDefs, staticFields,
                staticFieldContracts, intrinsicRegistrations,
                selectedA3LayoutResourceLedger);
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
                var sourceRow = checked((int)generics.GetDefaultArgumentRef(formal));
                if (constantSources.GetKind(sourceRow) == 2)
                {
                    if (!index.Schema.UsesGeneralPredicateGraphs())
                    {
                        throw InvalidMetadata();
                    }

                    selectedA3LayoutResourceLedger?.ConsumeWork();
                    constantExecutionUses.Add(new TrRmConstantExecutionUse(
                        checked((int)constantSources.GetPayloadReference(sourceRow)),
                        ownerModule, generics.GetConstantTypeSpecRef(formal)));
                }
                else
                {
                    TrRmConstantSourceValueValidator.ValidateFrozenScalar(
                        sourceRow, generics.GetConstantTypeSpecRef(formal),
                        ownerModule, constantSources, frozenValueNodes, blobs,
                        typeSpecs, features, modules, types, enumDefs, imports,
                        selectedA3LayoutResourceLedger);
                }
            }
            for (var row = 1; row <= interfaceConstDefs.Count; row++)
            {
                var ownerInterface = interfaceConstDefs.GetOwnerInterfaceDef(row);
                var ownerTypeDef = interfaces.GetOwnerTypeDef(ownerInterface);
                var sourceRow = interfaceConstDefs.GetConstantSourceRow(row);
                var ownerModule = types.GetOwnerModule(ownerTypeDef);
                if (constantSources.GetKind(sourceRow) == 2)
                {
                    if (!index.Schema.UsesGeneralPredicateGraphs())
                    {
                        throw InvalidMetadata();
                    }

                    selectedA3LayoutResourceLedger?.ConsumeWork();
                    constantExecutionUses.Add(new TrRmConstantExecutionUse(
                        checked((int)constantSources.GetPayloadReference(sourceRow)),
                        ownerModule,
                        interfaceConstDefs.GetConstantTypeSpecRef(row)));
                }
                else
                {
                    TrRmConstantSourceValueValidator.ValidateFrozenScalar(
                        sourceRow,
                        interfaceConstDefs.GetConstantTypeSpecRef(row),
                        ownerModule, constantSources, frozenValueNodes,
                        blobs, typeSpecs, features, modules, types, enumDefs,
                        imports, selectedA3LayoutResourceLedger);
                }
            }
            interfaceWitnesses = new TrRmInterfaceWitnessTableReader(
                interfaceWitnessBytes, interfaceWitnessCount, typeSpecs,
                witnessMemberCount, witnessAssociatedTypeCount,
                witnessAssociatedConstCount,
                selectedA3LayoutResourceLedger);
            witnessMembers = new TrRmWitnessMemberTableReader(witnessMemberBytes,
                witnessMemberCount, interfaceWitnesses, typeSpecs, methods,
                selectedA3LayoutResourceLedger);
        }

        var constraints = constraintCount == 0 ? default
            : new TrRmConstraintTableReader(constraintBytes, constraintCount,
                generics, typeSpecs, selectedA3LayoutResourceLedger);
        var contracts = developmentProfile
            ? new TrRmContractTableReader(contractBytes, contractCount,
                types, methods, signatures, parameters, typeSpecs,
                index.Schema, selectedA3LayoutResourceLedger)
            : new TrRmContractTableReader(contractBytes, contractCount,
                types, methods, signatures, parameters, typeSpecs, fieldPathCount,
                features, modules, index.Schema, predicateGraphs.Count,
                eagerGroups, staticFields, staticFieldContracts,
                eagerInitializers, lazyUnits,
                selectedA3LayoutResourceLedger);
        if (index.Schema.SupportsDeclaredCopyEvidence())
        {
            for (var typeDef = 1; typeDef <= types.Count; typeDef++)
            {
                selectedA3LayoutResourceLedger?.ConsumeWork();
                var hasCleanup = false;
                var (first, count) = types.GetContractRange(typeDef);
                for (var offset = 0; offset < count; offset++)
                {
                    selectedA3LayoutResourceLedger?.ConsumeWork(
                        sizeof(ushort) + 1UL);
                    if (BinaryPrimitives.ReadUInt16LittleEndian(
                            contracts.GetRow(first + offset)[4..]) == 5)
                    {
                        hasCleanup = true;
                        break;
                    }
                }

                if ((types.HasDeclaredCopy(typeDef) && hasCleanup)
                    || (index.Schema.SupportsDeclaredUniqueEvidence()
                        && hasCleanup
                        && !types.HasDeclaredUnique(typeDef)))
                {
                    throw InvalidMetadata();
                }
            }
        }

        var predicateSelectorKinds = Array.Empty<byte>();
        if (!developmentProfile)
        {
            predicateSelectorKinds = index.Schema.UsesGeneralPredicateGraphs()
                ? TrRmGeneralPredicateGraphValidator.Validate(index.Schema,
                    features, modules, methods, signatures, contracts, typeSpecs,
                    types, enumDefs, generics, imports, frozenValueNodes, blobs,
                    immutableData,
                    predicateGraphs, predicateReadFootprints, predicateReadRoots,
                    predicateReadDependencies, predicateNodes, predicateNodeOperands,
                    predicateChildEdges, predicateCaptures, predicateCalls,
                    predicateOperations, predicateTypeUses, predicateControls,
                    predicateTypePatterns, evaluationReadBounds,
                    constantSources, constantExecutionUses)
                : TrRmPublishedPredicateSelectorValidator.Validate(
                    predicateReadRootBytes, predicateReadRootCount,
                    predicateNodeBytes, predicateNodeCount,
                    predicateNodeOperandBytes, predicateNodeOperandCount,
                    predicateReadDependencyCount, predicateGraphs,
                    predicateReadFootprints, predicateChildEdges, predicateCaptures,
                    methods, signatures, typeSpecs, contracts,
                    selectedA3LayoutResourceLedger);
        }
        if (index.Schema.UsesGeneralPredicateGraphs())
        {
            for (var field = 1; field <= staticFields.Count; field++)
            {
                var contract = staticFieldContracts.GetRowForStaticField(field);
                var role = staticFieldContracts.GetDeclarationRole(contract);
                var ownerType = staticFields.GetOwnerTypeDefRow(field);
                var hasOwnerImport = imports.IsExpectedDefinition(
                    0x1300_0000u | (uint)ownerType);
                var hasFieldImport = imports.IsExpectedDefinition(
                    0x2400_0000u | (uint)field);
                if ((role == 1 && (hasOwnerImport || hasFieldImport))
                    || (role == 2 && (!hasOwnerImport || !hasFieldImport))
                    || (role == 3 && (!hasOwnerImport || hasFieldImport)))
                {
                    throw InvalidMetadata();
                }
            }

            for (var registration = 1;
                registration <= intrinsicRegistrations.Count; registration++)
            {
                var role = intrinsicRegistrations.GetDeclarationRole(registration);
                var hasImport = imports.IsExpectedDefinition(
                    0x2600_0000u | (uint)registration);
                if ((role == 1 && hasImport) || (role == 2 && !hasImport))
                {
                    throw InvalidMetadata();
                }
            }

            staticDependencies = new TrRmStaticDependencyTableReader(
                staticDependencyBytes, staticDependencyCount, imports,
                staticFields, staticFieldContracts, eagerGroups,
                staticPublicationCount, typeSpecs, types, index.Schema);
            staticAccesses = new TrRmStaticAccessTableReader(
                staticAccessBytes, staticAccessCount, staticFields,
                staticFieldContracts, staticDependencies, typeSpecs, types,
                imports, index.Schema);
        }
        if (!developmentProfile)
        {
            associatedRelations = new TrRmAssociatedRelationTableReader(
                associatedRelationBytes, associatedRelationCount, typeSpecs,
                associatedTypeDefs.Count, imports,
                selectedA3LayoutResourceLedger);
            textFragmentShapes = new TrRmTextFragmentShapeTableReader(
                textFragmentShapeBytes, textFragmentShapeCount, textContentShapes,
                textCaptureShapes, types, methods, generics, constraints,
                typeSpecs, interfaces, interfaceBases, interfaceMethods,
                interfaceWitnesses, witnessMembers, imports,
                selectedA3LayoutResourceLedger);
        }
        TrRmConstructedTypeSpecValidator.Validate(typeSpecs, types,
            generics, imports, selectedConstantSourceCount,
            selectedA3LayoutResourceLedger);
        if (index.Schema
            == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft)
        {
            TrRmFixedArrayGenericProfileValidator.ValidateA3(typeSpecs,
                types, generics, constraints, imports, methods, signatures,
                parameters, bodies, contracts, features, modules,
                fixedArrayLayouts, targetProfiles,
                selectedA3LayoutResourceLedger);
        }
        if (index.Schema.UsesForeignInvocationStorageFixedArrays())
        {
            TrRmFixedArrayLayoutValidator.Validate(fixedArrayLayouts,
                typeSpecs, types, fields, layouts, layoutFields,
                targetProfiles, imports, contracts, generics, constraints,
                index.Schema, selectedA3LayoutResourceLedger);
        }

        if (!developmentProfile)
        {
            witnessAssociatedTypes = new TrRmWitnessAssociatedTypeTableReader(
                witnessAssociatedTypeBytes, witnessAssociatedTypeCount,
                interfaceWitnesses, typeSpecs, types, interfaces, interfaceBases,
                associatedTypeDefs, imports,
                selectedA3LayoutResourceLedger);
            if (index.Schema.UsesGeneralPredicateGraphs())
            {
                witnessAssociatedConsts = new TrRmWitnessAssociatedConstTableReader(
                    witnessAssociatedConstBytes, witnessAssociatedConstCount,
                    interfaceWitnesses, typeSpecs, types, interfaces, interfaceBases,
                    associatedConstDefs, imports, constantSources, index.Schema,
                    deferAnchorIdentity: true);
                for (var binding = 1;
                    binding <= witnessAssociatedConsts.Count; binding++)
                {
                    var sourceRow = witnessAssociatedConsts.GetConstantSourceRow(binding);
                    if (constantSources.GetKind(sourceRow) != 2)
                    {
                        continue;
                    }
                    if (!index.Schema.UsesGeneralPredicateGraphs())
                    {
                        throw InvalidMetadata();
                    }

                    var reference
                        = witnessAssociatedConsts.GetAssociatedConstDefReference(binding);
                    var definition = (reference >> 24) switch
                    {
                        0x25 => reference,
                        0x1C => imports.GetExpectedDefinitionForImportRow(
                            checked((int)(reference & 0x00FF_FFFF))),
                        _ => throw InvalidMetadata(),
                    };
                    var definitionRow
                        = checked((int)(definition & 0x00FF_FFFF));
                    var valueType
                        = associatedConstDefs.GetConstantTypeSpecRef(definitionRow);
                    var witness = witnessAssociatedConsts.GetWitnessRow(binding);
                    var implementingType
                        = interfaceWitnesses.GetImplementingTypeSpecRef(witness);
                    var implementingTypeDef = typeSpecs.GetNominalTypeDefRow(
                        implementingType, types, imports);
                    selectedA3LayoutResourceLedger?.ConsumeWork();
                    constantExecutionUses.Add(new TrRmConstantExecutionUse(
                        checked((int)constantSources.GetPayloadReference(sourceRow)),
                        types.GetOwnerModule(implementingTypeDef), valueType));
                }
                immutableData.ValidateOwnerBindings(typeSpecs, types, imports,
                    interfaces, interfaceConstDefs, interfaceWitnesses,
                    witnessAssociatedConsts, staticFields, staticFieldContracts,
                    staticPublications, staticDependencies);
            }
            targetLayoutDependencyFields = new TrRmTargetLayoutDependencyFieldTableReader(
                targetLayoutDependencyFieldBytes,
                targetLayoutDependencyFieldCount, fields, imports,
                selectedA3LayoutResourceLedger);
            targetLayoutDependencies = new TrRmTargetLayoutDependencyTableReader(
                targetLayoutDependencyBytes, targetLayoutDependencyCount, typeSpecs,
                targetProfiles, targetLayoutDependencyFieldCount,
                selectedA3LayoutResourceLedger);
            fieldPaths = fieldPathCount == 0 ? default
                : new TrRmFieldPathTableReader(fieldPathBytes, fieldPathCount,
                    typeSpecs, fields, imports,
                    selectedA3LayoutResourceLedger);
            if (index.Schema.UsesGeneralPredicateGraphs())
            {
                constantExecutionArguments =
                    new TrRmConstantExecutionArgumentTableReader(
                        constantExecutionArgumentBytes,
                        constantExecutionArgumentCount, frozenValueNodeCount,
                        logicalInputViewCount, constantExecutionCount);
                constantExecutionDependencies =
                    new TrRmConstantExecutionDependencyTableReader(
                        constantExecutionDependencyBytes,
                        constantExecutionDependencyCount, methods.Count,
                        constraints.Count, interfaceWitnesses.Count,
                        witnessProjectionCount: 0,
                            intrinsicRegistrationCount: intrinsicRegistrations.Count,
                            immutableDataCount: immutableData.Count,
                            fieldDefCount: fields.Count,
                            propertyDefCount: properties.Count,
                            unionCaseDefCount: unionCaseDefs.Count);
                logicalViewPathEntries = new TrRmLogicalViewPathEntryTableReader(
                    logicalViewPathEntryBytes, logicalViewPathEntryCount,
                    fields, unionCaseFields, imports);
                logicalInputViews = new TrRmLogicalInputViewTableReader(
                    logicalInputViewBytes, logicalInputViewCount,
                    frozenValueNodeCount, immutableData.Count,
                    constantExecutionCount, typeSpecs, logicalViewPathEntries);
                constantExecutions = new TrRmConstantExecutionTableReader(
                    constantExecutionBytes, constantExecutionCount,
                    bodyCount, constantExecutionSubstitutions, constantSources,
                    constantExecutionArguments, logicalInputViews,
                    constantExecutionDependencies,
                    targetLayoutDependencies, methods, types, signatures, generics,
                    contracts, typeSpecs, targetProfiles, intrinsicRegistrations,
                    imports, features, modules, index.Schema);
            }
        }
        else if (importCount != 0)
        {
            imports = new TrRmImportTableReader(importBytes, importCount,
                strings, moduleRefs, types, methods, properties, interfaces,
                associatedTypeDefs, fields, features, modules, index.Schema,
                associatedConstDefs, staticFields, staticFieldContracts,
                intrinsicRegistrations, selectedA3LayoutResourceLedger);
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
            index.Schema, associatedConstDefs, staticFields, staticFieldContracts,
            intrinsicRegistrations, selectedA3LayoutResourceLedger);
        var nativeImports = developmentProfile
            ? new TrRmDevelopmentNativeImportTableReader(nativeImportBytes,
                nativeImportCount, strings, methods, signatures, parameters,
                typeSpecs, contracts, layouts)
            : default;
        var standardNativeImports = !developmentProfile && !copyOriginDraft
            ? new TrRmNativeImportTableReader(standardNativeImportBytes,
                standardNativeImportCount, strings, methods, fields, types, imports,
                modules, signatures, parameters, typeSpecs, contracts, layouts,
                selectedA3LayoutResourceLedger)
            : default;
        var nativeExports = !developmentProfile
            ? new TrRmNativeExportTableReader(nativeExportBytes, nativeExportCount,
                strings, methods, fields, types, imports, modules, signatures,
                typeSpecs, contracts, layouts,
                selectedA3LayoutResourceLedger)
            : default;
        var nativeFunctionPointerAnchors = !developmentProfile
            ? new TrRmNativeFunctionPointerAnchorTableReader(typeSpecs, types,
                enumDefs, generics, methods, signatures, parameters, contracts, imports,
                constantSources, frozenValueNodes, blobs, features, index.Schema,
                predicateSelectorKinds, modules,
                exports, properties, interfaceMethods, standardNativeImports,
                nativeExports, selectedA3LayoutResourceLedger)
            : default;
        if (!developmentProfile)
        {
            properties.ValidateGetterTypeIdentity(methods, signatures,
                nativeFunctionPointerAnchors);
            if (index.Schema.UsesGeneralPredicateGraphs())
            {
                staticPublications.ValidateTypeIdentity(staticFields,
                    nativeFunctionPointerAnchors);
                witnessAssociatedConsts.ValidateAnchorIdentity(
                    nativeFunctionPointerAnchors);
            }
        }
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
                nativeVTableCount, methods, signatures, parameters, typeSpecs, contracts, layouts)
            : default;
        if (foreignInvocationDraft)
        {
            ValidateForeignInvocationContractOwners(contracts, imports,
                standardNativeImports, nativeFunctionPointerAnchors, methods);
        }
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
                staticFields, unionCaseDefs, unionCaseFields, textContentShapes,
                textCaptureShapes, predicateGraphs, predicateCaptures,
                contracts, generics, imports, features, modules, index.Schema,
                selectedA3LayoutResourceLedger);
            if (!copyOriginDraft)
            {
                ValidateStandardNativeImportFeatureAssignments(standardNativeImports,
                    features, modules, methods, types, fields, index.Schema);
            }
        }
        var sourceFiles = sourceFileCount == 0 ? default
            : new TrRmSourceFileTableReader(sourceFileBytes, sourceFileCount,
                strings, modules, selectedA3LayoutResourceLedger);
        var sourceMaps = sourceMapCount == 0 ? default
            : new TrRmSourceMapTableReader(sourceMapBytes, sourceMapCount,
                methods, sourceFiles, developmentProfile
                    ? index.Schema : TrRmArtifactSchema.Standard,
                selectedA3LayoutResourceLedger);

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
        _ = sourceFiles;
        _ = sourceMaps;
        selectedA3LayoutResourceLedger?.ConsumeWork();
        var executionTables = new ValidatedExecutionTables(strings, features,
            modules, moduleRefs,
            types, enumDefs, enumMembers, typeSpecs,
            nativeFunctionPointerAnchors,
            generics,
            constraints,
            interfaces, associatedTypeDefs, interfaceWitnesses, witnessMembers,
            witnessAssociatedTypes, witnessAssociatedConsts,
            interfaceBases, interfaceMethods,
            interfaceConstDefs, associatedConstDefs, immutableData, associatedRelations,
            unionCaseDefs, unionCaseFields, builderRegistrations, textTargets,
            textContentShapes, textCaptureShapes, textFragmentShapes,
            predicateGraphs, predicateChildEdges, predicateReadFootprints,
            predicateCaptures, predicateNodes, predicateNodeOperands,
            predicateReadRoots, predicateReadDependencies, predicateOperations,
            predicateCalls, predicateTypeUses, predicateControls,
            evaluationReadBounds, predicateTypePatterns,
            blobs, frozenValueNodes,
            constantSources,
            constantExecutionSubstitutions,
            constantExecutionArguments, constantExecutionDependencies,
            logicalInputViews, logicalViewPathEntries, constantExecutions,
            constantExecutionUses,
            targetLayoutDependencies, targetLayoutDependencyFields, fieldPaths,
            fields, staticFields, eagerGroups, staticFieldContracts,
            eagerInitializers, lazyUnits, staticPublications,
            staticDependencies, staticAccesses,
            intrinsicRegistrations,
            layouts, layoutFields,
            targetProfiles, fixedArrayLayouts, signatures, parameters, methods,
            bodies, contracts, nativeImports,
            standardNativeImports, nativeExports, nativeCallables,
            nativeFunctionPointerSignatures,
            nativeVTables, imports, exports,
            index.Schema, selectedA3LayoutResourceLedger);
        executionTables.ValidateNonBodyTypeSpecModuleContexts();
        if (index.Schema.UsesGeneralPredicateGraphs())
        {
            TrRmConstantExecutionFeatureUseValidator.Validate(
                constantExecutions, constantExecutionArguments,
                constantExecutionDependencies, logicalInputViews,
                constantExecutionUses, features, modules, index.Schema);
        }
        return executionTables;
    }

    private static void ValidateNominalEnumFeatureAssignments(
        TrRmEnumDefTableReader enumDefs, TrRmTypeDefTableReader typeDefs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema)
    {
        for (var row = 1; row <= enumDefs.Count; row++)
        {
            var ownerTypeDef = enumDefs.GetOwnerTypeDef(row);
            var ownerModule = typeDefs.GetOwnerModule(ownerTypeDef);
            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule, 18, schema))
            {
                throw InvalidMetadata();
            }
        }
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
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmArtifactSchema schema)
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
                    schema)
                || !TrRmFeatureManifestValidator.Requires(features, modules, module,
                    TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                    schema)
                || !TrRmFeatureManifestValidator.Requires(features, modules, module,
                    requiredFeature, schema))
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
        TrRmStaticFieldTableReader staticFields,
        TrRmUnionCaseDefTableReader unionCaseDefs,
        TrRmUnionCaseFieldTableReader unionCaseFields,
        TrRmTextContentShapeTableReader textContentShapes,
        TrRmTextCaptureShapeTableReader textCaptureShapes,
        TrRmPredicateGraphTableReader predicateGraphs,
        TrRmPredicateCaptureTableReader predicateCaptures,
        TrRmContractTableReader contracts,
        TrRmGenericParamTableReader genericParams, TrRmImportTableReader imports,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        var moduleCount = modules.Count;
        resourceLedger?.ConsumeWork(checked((ulong)moduleCount + 1UL));
        var containsFunctionPointerValueByModule = new bool[moduleCount + 1];
        var staticFieldCount = staticFields.Count;
        for (var field = 1; field <= staticFieldCount; field++)
        {
            resourceLedger?.ConsumeWork();
            var ownerType = staticFields.GetOwnerTypeDefRow(field);
            var ownerModule = typeDefs.GetOwnerModule(ownerType);
            var containsFunctionPointerValue
                = typeSpecs.ContainsNativeFunctionPointerValue(
                    staticFields.GetTypeSpecRef(field), typeDefs,
                    genericParams, imports);
            resourceLedger?.ConsumeWork();
            containsFunctionPointerValueByModule[ownerModule] |=
                containsFunctionPointerValue;
        }

        var unionCaseFieldCount = unionCaseFields.Count;
        for (var field = 1; field <= unionCaseFieldCount; field++)
        {
            resourceLedger?.ConsumeWork();
            var caseDef = unionCaseFields.GetOwnerUnionCaseDef(field);
            var ownerType = unionCaseDefs.GetOwnerTypeDef(caseDef);
            var ownerModule = typeDefs.GetOwnerModule(ownerType);
            var containsFunctionPointerValue
                = typeSpecs.ContainsNativeFunctionPointerValue(
                    unionCaseFields.GetTypeSpecRef(field), typeDefs,
                    genericParams, imports);
            resourceLedger?.ConsumeWork();
            containsFunctionPointerValueByModule[ownerModule] |=
                containsFunctionPointerValue;
        }

        var textCaptureCount = textCaptureShapes.Count;
        for (var capture = 1; capture <= textCaptureCount; capture++)
        {
            resourceLedger?.ConsumeWork();
            var shape = textCaptureShapes.GetShapeRow(capture);
            var ownerType = textContentShapes.GetGeneratedTypeDef(shape);
            var ownerModule = typeDefs.GetOwnerModule(ownerType);
            var containsFunctionPointerValue
                = typeSpecs.ContainsNativeFunctionPointerValue(
                    textCaptureShapes.GetTypeSpecRef(capture), typeDefs,
                    genericParams, imports);
            resourceLedger?.ConsumeWork();
            containsFunctionPointerValueByModule[ownerModule] |=
                containsFunctionPointerValue;
        }

        var predicateGraphCount = predicateGraphs.Count;
        resourceLedger?.ConsumeWork(
            checked((ulong)predicateGraphCount + 1UL));
        var predicateGraphOwnerModules = new int[predicateGraphCount + 1];
        var methodCount = methods.Count;
        for (var method = 1; method <= methodCount; method++)
        {
            resourceLedger?.ConsumeWork();
            foreach (var predicate in contracts.GetPredicateGraphContracts(method))
            {
                resourceLedger?.ConsumeWork();
                var graph = predicate.PredicateGraphRow;
                if (graph > 0 && graph < predicateGraphOwnerModules.Length)
                {
                    resourceLedger?.ConsumeWork();
                    predicateGraphOwnerModules[graph]
                        = methods.GetOwnerModule(method);
                }
            }
        }

        var predicateCaptureCount = predicateCaptures.Count;
        for (var capture = 1; capture <= predicateCaptureCount; capture++)
        {
            resourceLedger?.ConsumeWork();
            if (predicateCaptures.GetRole(capture) is not (1 or 2))
            {
                continue;
            }

            var graph = predicateCaptures.GetGraphRow(capture);
            var ownerModule = predicateGraphOwnerModules[graph];
            if (ownerModule != 0)
            {
                var containsFunctionPointerValue
                    = typeSpecs.ContainsNativeFunctionPointerValue(
                        predicateCaptures.GetCapturedTypeSpecRef(capture),
                        typeDefs, genericParams, imports);
                resourceLedger?.ConsumeWork();
                containsFunctionPointerValueByModule[ownerModule] |=
                    containsFunctionPointerValue;
            }
        }

        for (var module = 1; module <= moduleCount; module++)
        {
            resourceLedger?.ConsumeWork();
            var containsFunctionPointerValue
                = containsFunctionPointerValueByModule[module];
            var (methodStart, methodEnd) = modules.GetMethodDefRange(module);
            for (var method = methodStart; method < methodEnd; method++)
            {
                resourceLedger?.ConsumeWork();
                var signature = methods.GetSignature(method);
                var containsResultFunctionPointerValue
                    = typeSpecs.ContainsNativeFunctionPointerValue(
                        signatures.GetResultTypeSpec(signature), typeDefs,
                        genericParams, imports);
                resourceLedger?.ConsumeWork();
                containsFunctionPointerValue
                    |= containsResultFunctionPointerValue;
                var receiverTypeSpec = signatures.GetReceiverTypeSpec(signature);
                if (receiverTypeSpec != 0)
                {
                    var containsReceiverFunctionPointerValue = typeSpecs
                        .ContainsNativeFunctionPointerValue(receiverTypeSpec,
                            typeDefs, genericParams, imports);
                    resourceLedger?.ConsumeWork();
                    containsFunctionPointerValue
                        |= containsReceiverFunctionPointerValue;
                }

                var (parameterStart, parameterCount) = signatures.GetParamRange(signature);
                for (var parameter = 0; parameter < parameterCount; parameter++)
                {
                    resourceLedger?.ConsumeWork();
                    var containsParameterFunctionPointerValue
                        = typeSpecs.ContainsNativeFunctionPointerValue(
                            parameters.GetTypeSpec(parameterStart + parameter),
                            typeDefs, genericParams, imports);
                    resourceLedger?.ConsumeWork();
                    containsFunctionPointerValue
                        |= containsParameterFunctionPointerValue;
                }
            }

            var (typeStart, typeEnd) = modules.GetTypeDefRange(module);
            for (var type = typeStart; type < typeEnd; type++)
            {
                resourceLedger?.ConsumeWork();
                var (fieldStart, fieldEnd) = typeDefs.GetFieldRange(type);
                for (var field = fieldStart; field < fieldEnd; field++)
                {
                    resourceLedger?.ConsumeWork();
                    var containsFieldFunctionPointerValue
                        = typeSpecs.ContainsNativeFunctionPointerValue(
                            fields.GetTypeSpecRef(field), typeDefs,
                            genericParams, imports);
                    resourceLedger?.ConsumeWork();
                    containsFunctionPointerValue
                        |= containsFieldFunctionPointerValue;
                }
            }

            resourceLedger?.ConsumeWork();
            if (containsFunctionPointerValue
                && !TrRmFeatureManifestValidator.Requires(features, modules,
                    module, TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                    schema))
            {
                throw InvalidMetadata();
            }
        }
    }

    private static void ValidateForeignInvocationContractOwners(
        TrRmContractTableReader contracts, TrRmImportTableReader imports,
        TrRmNativeImportTableReader nativeImports,
        TrRmNativeFunctionPointerAnchorTableReader anchors,
        TrRmMethodDefTableReader methods)
    {
        for (var method = 1; method <= methods.Count; method++)
        {
            if (contracts.GetNativeParameters(method).IsDefaultOrEmpty
                || nativeImports.HasImportForMethod(method)
                || anchors.IsAnchorMethodDef(method)
                || imports.IsExpectedDefinition(0x1600_0000u | (uint)method))
            {
                continue;
            }

            throw InvalidMetadata();
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
