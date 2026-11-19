// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static TrRmArtifactSchema GetC1M1ClassMetadataSchema(
        string schemaIdentity)
        => schemaIdentity switch {
            StandardC1M1SchemaIdentity
                => TrRmArtifactSchema.C1M1Draft,
            StandardC1M1Construction2L1R1ClassParameterDefaultsSchemaIdentity
                => TrRmArtifactSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft,
            _ => throw Unsupported(
                "Source-free class MethodDef re-emission requires an exact supported C1M1 schema selection."),
        };

    private sealed record C1M1TypeDefEmissionView(
        byte Kind, string Namespace, string Name, uint TypeDefFlags,
        ImmutableArray<BoundStandardClassFieldDeclaration> Fields,
        ImmutableArray<BoundStandardClassLayoutVariant> Layouts);

    private sealed record C1M1MethodEmissionPlan(
        BoundStandardClassTypeView Owner,
        BoundStandardClassMethodDeclaration Method,
        ImmutableArray<BoundStandardClassMethodExpectation> Imports,
        ImmutableArray<string> ExportKeys,
        ReadOnlyMemory<byte> Body);

    private sealed record C1M1Construction2SourceInitializer(
        int ClassIndex, BoundFunction Function, uint ClassTypeSpecReference,
        ImmutableArray<BoundFieldAssignment> FieldAssignments,
        int InitializerMethodDefRow, int ProjectionMethodDefRow);

    private sealed record C1M1Construction2SourceMethod(
        BoundFunction Function, int MethodDefRow,
        int DirectReturnHome = -1,
        int IdentityForwarderTargetFunctionIndex = -1,
        byte[]? EncodedBody = null,
        BoundBinary? CallBinaryExpression = null);

    private sealed record C1M1SourceImport(
        int ModuleRefRow, string ExportKey, uint ExpectedDefinitionId);

    private sealed record C1M1ImportedStructImportPlan(
        BoundSourceTypeProviderSnapshot Provider,
        ImmutableArray<string> ExportKeys);

    private sealed record C1M1ImportedClassImportPlan(
        BoundSourceTypeProviderSnapshot Provider,
        ImmutableArray<string> ExportKeys);

    private readonly record struct C1M1Construction2ConditionalReturn(
        BoundIf Root);

    private readonly record struct C1M1Construction2NestedFieldRead(
        ImmutableArray<int> FieldDefRows,
        ImmutableArray<PrimitiveType> FieldTypes,
        int BaseVariableIndex);

    private readonly record struct C1M1Construction2NestedFieldWrite(
        ImmutableArray<int> FieldDefRows,
        ImmutableArray<PrimitiveType> FieldTypes,
        int BaseVariableIndex,
        BoundExpression Value);

    private readonly record struct C1M1ParameterSurfaceRow(
        int MethodDefRow, int Ordinal, string Name,
        uint DefaultConstantSourceRow);

    private readonly struct C1M1FieldPathIdentity
        : IEquatable<C1M1FieldPathIdentity>
    {
        private readonly uint _rootTypeSpecRef;
        private readonly ImmutableArray<uint> _fieldReferences;

        internal C1M1FieldPathIdentity(uint rootTypeSpecRef,
            ImmutableArray<uint> fieldReferences)
        {
            _rootTypeSpecRef = rootTypeSpecRef;
            _fieldReferences = fieldReferences;
        }

        public bool Equals(C1M1FieldPathIdentity other)
            => _rootTypeSpecRef == other._rootTypeSpecRef
                && _fieldReferences.AsSpan().SequenceEqual(
                    other._fieldReferences.AsSpan());

        public override bool Equals(object? obj)
            => obj is C1M1FieldPathIdentity other && Equals(other);

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(_rootTypeSpecRef);
            foreach (var fieldReference in _fieldReferences)
            {
                hashCode.Add(fieldReference);
            }

            return hashCode.ToHashCode();
        }
    }

    private static byte[] EmitC1M1SourceClassDeclarations(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement, SourceClassExports sourceClassExports,
        out TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        TrRmArtifactSchema schema = TrRmArtifactSchema.C1M1Draft)
    {
        var emitsConstruction2 = schema.UsesObjectConstruction();
        if (schema != TrRmArtifactSchema.C1M1Draft
            && !emitsConstruction2)
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }

        SemanticVerifier.VerifyLibraryReferences(program);
        var standardClassIndexByIdentity
            = CreateC1M1StandardClassIndexByIdentity(program);
        var sourceStructDeclarations
            = CreateC1M1SourceStructDeclarations(program);
        if (!ReferenceEquals(sourceClassExports.Program, program)
            || program.EntryPoint != -1
            || !program.InterfaceWitnesses.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.GenericMethodOperatorProtocols.IsEmpty
            || !HasSupportedC1M1ImportedNominalClosure(program,
                sourceStructDeclarations, standardClassIndexByIdentity)
            || (!schema.UsesClassParameterDefaults()
                && (!program.Interfaces.IsEmpty
                    || !program.GenericMethodDefinitions.IsEmpty)))
        {
            throw Unsupported(
                "C1M1 source declaration emission requires captured local non-generic classes and the bounded imported nominal closure without generic or interface definitions.");
        }
        if (!emitsConstruction2
            && sourceClassExports.Exports.Any(static export =>
                export.MemberExports.Any(static member =>
                    member.Kind != SourceClassMemberKind.Field)))
        {
            throw Unsupported(
                "C1M1 source class declarations do not publish constructor, method, or property ExportKeys; use the exact construction successor.");
        }
        if (emitsConstruction2
            && sourceClassExports.Exports.Any(static export =>
                export.MemberExports.Any(static member =>
                    member.Kind == SourceClassMemberKind.Property)))
        {
            throw Unsupported(
                "The bounded Construction-2 class source emitter does not yet retain property Bodies.");
        }
        if (program.Classes.Any(static @class =>
                @class.HasCleanup is not false))
        {
            throw Unsupported(
                "The selected C1M1 class TypeDef view does not preserve class Cleanup selection.");
        }
        if (program.Classes.Any(static @class =>
                @class.Fields.Any(static field =>
                    field.BackingPropertyName is not null)))
        {
            throw Unsupported(
                "Property backing fields require the exact PI1 schema and cannot be emitted as named FieldDefs.");
        }

        var exportKeysByClass = new SortedDictionary<int, List<string>>();
        foreach (var export in sourceClassExports.Exports)
        {
            var classIndex = export.Declaration.ClassIndex;
            if (classIndex < 0 || classIndex >= program.Classes.Length
                || !ReferenceEquals(program.Classes[classIndex],
                    export.Declaration.DeclarationSnapshot))
            {
                throw new InvalidDataException(
                    "A captured source class no longer identifies its checked local declaration.");
            }

            if (!exportKeysByClass.TryGetValue(classIndex,
                    out var exportKeys))
            {
                exportKeys = [];
                exportKeysByClass.Add(classIndex, exportKeys);
            }

            exportKeys.Add(export.ExportKey);
        }

        if (exportKeysByClass.Count == 0
            || exportKeysByClass.Count > ManagedClassTypes.OptionalFirst
                - ManagedClassTypes.First)
        {
            throw Unsupported(
                "C1M1 source declaration emission requires at least one captured class within the local class limit.");
        }

        var sourceClassProgram = program.SourceClassProgramAssociation;
        if (sourceClassProgram is null)
        {
            throw new InvalidDataException(
                "Captured C1M1 class exports have no compiler-owned source class association.");
        }

        var localClassIndexes = sourceClassProgram.Declarations
            .Select(static declaration => declaration.ClassIndex)
            .ToHashSet();
        var pendingTypeDefinitions = new Queue<(bool IsClass, int Index)>(
            exportKeysByClass.Keys.Select(static classIndex =>
                (IsClass: true, classIndex)));
        foreach (var declaration in sourceClassProgram.Declarations)
        {
            if (exportKeysByClass.TryAdd(declaration.ClassIndex, []))
            {
                if (exportKeysByClass.Count
                    > ManagedClassTypes.OptionalFirst
                        - ManagedClassTypes.First)
                {
                    throw Unsupported(
                        "C1M1 source class declarations exceed the local class limit.");
                }

                pendingTypeDefinitions.Enqueue((true,
                    declaration.ClassIndex));
            }
        }

        var retainedStructureIndexes = new HashSet<int>();
        if (exportKeysByClass.Count
            > ManagedClassTypes.OptionalFirst - ManagedClassTypes.First)
        {
            throw Unsupported(
                "C1M1 source class declarations exceed the local class limit.");
        }

        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var field in declaration.Declaration.Fields)
            {
                if (TryGetC1M1ImportedClassFieldType(field.Type, program,
                        standardClassIndexByIdentity, out _, out _))
                {
                    continue;
                }

                if (TryGetC1M1SourceStructFieldType(field.Type,
                        sourceStructDeclarations,
                        out var dependencyStructureIndex))
                {
                    if (retainedStructureIndexes.Add(
                            dependencyStructureIndex))
                    {
                        if (retainedStructureIndexes.Count
                            > SemanticVerifier.MaximumVariables)
                        {
                            throw Unsupported(
                                "C1M1 imported class struct dependency closure exceeds the local structure limit.");
                        }

                        pendingTypeDefinitions.Enqueue((false,
                            dependencyStructureIndex));
                    }

                    continue;
                }

                _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
            }
        }

        while (pendingTypeDefinitions.TryDequeue(out var pendingDefinition))
        {
            ImmutableArray<BoundStructField> pendingFields;
            if (pendingDefinition.IsClass)
            {
                pendingFields
                    = program.Classes[pendingDefinition.Index].Fields;
            }
            else
            {
                var structure = program.Structures[pendingDefinition.Index];
                if (!IsC1M1SourceStructLayoutSupported(structure,
                        sourceStructDeclarations[pendingDefinition.Index]))
                {
                    throw Unsupported(
                        "C1M1 class declaration closure supports only non-generic local or imported structs with auto layout and no ownership or cleanup contract.");
                }

                pendingFields = structure.Fields;
            }

            foreach (var field in pendingFields)
            {
                if (TryGetC1M1SourceClassFieldType(field.Type,
                        out var dependencyClassIndex, out _, out _))
                {
                    if (!localClassIndexes.Contains(dependencyClassIndex))
                    {
                        throw Unsupported(
                            "C1M1 source class declarations do not capture imported or private class TypeDef dependencies.");
                    }

                    if (exportKeysByClass.TryAdd(dependencyClassIndex, []))
                    {
                        if (exportKeysByClass.Count
                            > ManagedClassTypes.OptionalFirst
                                - ManagedClassTypes.First)
                        {
                            throw Unsupported(
                                "C1M1 source class dependency closure exceeds the local class limit.");
                        }

                        pendingTypeDefinitions.Enqueue((true,
                            dependencyClassIndex));
                    }

                    continue;
                }

                if (TryGetC1M1ImportedClassFieldType(field.Type, program,
                        standardClassIndexByIdentity, out _, out _))
                {
                    continue;
                }

                if (TryGetC1M1SourceStructFieldType(field.Type,
                        sourceStructDeclarations,
                        out var dependencyStructureIndex))
                {
                    if (retainedStructureIndexes.Add(
                            dependencyStructureIndex))
                    {
                        if (retainedStructureIndexes.Count
                            > SemanticVerifier.MaximumVariables)
                        {
                            throw Unsupported(
                                "C1M1 source class struct dependency closure exceeds the local structure limit.");
                        }

                        pendingTypeDefinitions.Enqueue((false,
                            dependencyStructureIndex));
                    }
                    continue;
                }

                _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
            }
        }

        var typeDefRows = new List<TrRmTypeDefRow>(
            exportKeysByClass.Count
                + program.StandardClassDeclarations.Length);
        var typeDefNames = new List<(string Namespace, string Name)>(
            exportKeysByClass.Count
                + program.StandardClassDeclarations.Length);
        var fieldRows = new List<TrRmFieldDefRow>();
        var fieldNames = new List<string>();
        var layoutRows = new List<TrRmLayoutRow>(
            exportKeysByClass.Count
                + program.StandardClassDeclarations.Length
                + retainedStructureIndexes.Count);
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        var typeSpecRowByClassType = new Dictionary<
            (int ClassIndex, bool IsWritable, bool IsOptional), uint>();
        var typeSpecRowByStructure = new Dictionary<int, uint>();
        var exportRows = new List<(uint DefinitionId, string Key)>();
        var methodRows = new List<TrRmMethodDefRow>();
        var methodNames = new List<string>();
        var methodSignatureRows = new List<TrRmMethodSigRow>();
        var parameterRows = new List<TrRmParamRow>();
        var parameterSurfaceRows
            = new List<C1M1ParameterSurfaceRow>();
        var contractRows = new List<TrRmContractRow>();
        var bodyRows = new List<ReadOnlyMemory<byte>>();
        var featureRows = new List<TrRmFeatureRow>();
        frozenScalarValueBuilder = new TrRmFrozenScalarValueBuilder();
        var constructionInitializers
            = new List<C1M1Construction2SourceInitializer>();
        var construction2SourceMethods
            = new List<C1M1Construction2SourceMethod>();
        var methodDefRowByFunctionIndex = new Dictionary<int, int>();
        var sourceNames = new HashSet<(string Namespace, string Name)>();
        var typeDefRowByClass = exportKeysByClass.Keys
            .Select(static (classIndex, ordinal) =>
                (classIndex, typeDefRow: ordinal + 1))
            .ToDictionary(static item => item.classIndex,
                static item => item.typeDefRow);
        var fieldDefRowByClassAndIndex = new Dictionary<
            (int ClassIndex, int FieldIndex), int>();
        var fieldDefRowByStandardClassAndIndex = new Dictionary<
            (int StandardClassIndex, int FieldIndex), int>();
        var fieldDefRowByStructureAndIndex = new Dictionary<
            (int StructureIndex, int FieldIndex), int>();
        var standardClassIndexes = Enumerable.Range(0,
            program.StandardClassDeclarations.Length).ToHashSet();
        var typeDefRowByStandardClass = standardClassIndexes
            .Order()
            .Select(static (standardClassIndex, ordinal) =>
                (standardClassIndex,
                    typeDefRow: ordinal + 1))
            .ToDictionary(static item => item.standardClassIndex,
                item => item.typeDefRow + exportKeysByClass.Count);
        var typeDefRowByStructure = retainedStructureIndexes
            .Order()
            .Select(static (structureIndex, ordinal) =>
                (structureIndex, typeDefRow: ordinal + 1))
            .ToDictionary(static item => item.structureIndex,
                item => item.typeDefRow + exportKeysByClass.Count
                    + standardClassIndexes.Count);
        var typeImportRowByStandardClass = new Dictionary<int, int>();
        var typeImportPlanByStandardClass
            = new Dictionary<int, C1M1ImportedClassImportPlan>();
        var typeImportRowByStructure = new Dictionary<int, int>();
        var typeImportPlanByStructure
            = new Dictionary<int, C1M1ImportedStructImportPlan>();
        var typeSpecRowByStandardClassType = new Dictionary<int, uint>();
        var moduleReferences = new List<(
            string ProducerIdentity, uint TargetRequirement,
            uint RuntimePolicy)>();
        var moduleReferenceRows = new Dictionary<(
            string ProducerIdentity, uint TargetRequirement,
            uint RuntimePolicy), int>();
        var imports = new List<C1M1SourceImport>();
        var importIdentities = new HashSet<(
            int ModuleRefRow, string ExportKey, uint ExpectedDefinitionId)>();

        int GetModuleRefRow(string producerIdentity,
            uint targetRequirement, uint runtimePolicy)
        {
            var key = (producerIdentity, targetRequirement, runtimePolicy);
            if (!moduleReferenceRows.TryGetValue(key, out var moduleRefRow))
            {
                moduleRefRow = moduleReferences.Count + 1;
                moduleReferences.Add(key);
                moduleReferenceRows.Add(key, moduleRefRow);
            }

            return moduleRefRow;
        }

        void AddImport(string producerIdentity, uint targetRequirement,
            uint runtimePolicy, string exportKey, uint expectedDefinitionId)
        {
            var moduleRefRow = GetModuleRefRow(producerIdentity,
                targetRequirement, runtimePolicy);
            if (importIdentities.Add((moduleRefRow, exportKey,
                    expectedDefinitionId)))
            {
                imports.Add(new C1M1SourceImport(moduleRefRow, exportKey,
                    expectedDefinitionId));
            }
        }

        foreach (var standardClassIndex in standardClassIndexes.Order())
        {
            var declaration
                = program.StandardClassDeclarations[standardClassIndex];
            var importPlan = GetC1M1ImportedClassImportPlan(declaration);
            typeImportPlanByStandardClass.Add(standardClassIndex,
                importPlan);
            var typeDefRow
                = typeDefRowByStandardClass[standardClassIndex];
            foreach (var exportKey in importPlan.ExportKeys)
            {
                AddImport(importPlan.Provider.ModuleIdentity,
                    importPlan.Provider.TargetRequirement,
                    importPlan.Provider.RuntimePolicy, exportKey,
                    0x1300_0000u | checked((uint)typeDefRow));
            }

            typeImportRowByStandardClass.Add(standardClassIndex,
                imports.Count - importPlan.ExportKeys.Length + 1);
        }

        foreach (var structureIndex in retainedStructureIndexes.Order())
        {
            var structure = program.Structures[structureIndex];
            if (structure.StandardTypeOrigin is not
                BoundStandardTypeOrigin origin)
            {
                continue;
            }

            var typeDefRow = typeDefRowByStructure[structureIndex];
            var importPlan = GetC1M1ImportedStructImportPlan(origin);
            typeImportPlanByStructure.Add(structureIndex, importPlan);
            foreach (var exportKey in importPlan.ExportKeys)
            {
                AddImport(importPlan.Provider.ModuleIdentity,
                    importPlan.Provider.TargetRequirement,
                    importPlan.Provider.RuntimePolicy, exportKey,
                    0x1300_0000u | checked((uint)typeDefRow));
            }

            typeImportRowByStructure.Add(structureIndex,
                imports.Count - importPlan.ExportKeys.Length + 1);
        }

        var typeDefOrder = exportKeysByClass.Keys
            .Select(static classIndex => (Kind: 0, Index: classIndex))
            .Concat(standardClassIndexes.Order().Select(
                static standardClassIndex => (Kind: 1,
                    Index: standardClassIndex)))
            .Concat(retainedStructureIndexes.Order().Select(
                static structureIndex => (Kind: 2,
                    Index: structureIndex)));

        uint FieldTypeSpecReference(PrimitiveType type)
        {
            if (TryGetC1M1ImportedClassFieldType(type, program,
                    standardClassIndexByIdentity,
                    out var standardClassIndex, out var instanceIndex))
            {
                if (!typeImportRowByStandardClass.TryGetValue(
                        standardClassIndex, out var importRow))
                {
                    throw new InvalidDataException(
                        "A captured source class field refers to an unretained Standard class TypeDef.");
                }

                if (!typeSpecRowByStandardClassType.TryGetValue(
                        instanceIndex, out var typeSpecRow))
                {
                    typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        0x03, checked((uint)importRow),
                        program.GenericNominalTypeInstances[
                            instanceIndex].Qualifiers));
                    typeSpecRowByStandardClassType.Add(instanceIndex,
                        typeSpecRow);
                }

                return typeSpecRow;
            }

            if (TryGetC1M1SourceClassFieldType(type,
                    out var classIndex, out var isWritable,
                    out var isOptional))
            {
                if (!typeDefRowByClass.TryGetValue(classIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "A captured source class FieldDef refers to an unretained local class.");
                }

                var key = (classIndex, isWritable, isOptional);
                if (!typeSpecRowByClassType.TryGetValue(key,
                        out var reference))
                {
                    var classTypeSpecRow
                        = checked((uint)typeSpecRows.Count + 1);
                    var qualifiers = isWritable ? (byte)0x01 : (byte)0;
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        0x02, checked((uint)typeDefRow), qualifiers));
                    reference = classTypeSpecRow;
                    if (isOptional)
                    {
                        reference = checked((uint)typeSpecRows.Count + 1);
                        typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(
                            0x06, classTypeSpecRow));
                    }

                    typeSpecRowByClassType.Add(key, reference);
                }

                return reference;
            }

            if (TryGetC1M1SourceStructFieldType(type,
                    sourceStructDeclarations,
                    out var structureIndex))
            {
                if (!typeDefRowByStructure.TryGetValue(structureIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "A captured source class FieldDef refers to an unretained local struct.");
                }

                if (!typeSpecRowByStructure.TryGetValue(structureIndex,
                        out var typeSpecRow))
                {
                    typeSpecRow
                        = checked((uint)typeSpecRows.Count + 1);
                    var typeSpecForm = typeImportRowByStructure.TryGetValue(
                        structureIndex, out var typeImportRow) ? (byte)0x03
                        : (byte)0x02;
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        typeSpecForm,
                        checked((uint)(typeSpecForm == 0x03
                            ? typeImportRow : typeDefRow))));
                    typeSpecRowByStructure.Add(structureIndex, typeSpecRow);
                }

                return typeSpecRow;
            }

            return GetC1M1SourceScalarTypeSpecReference(type);
        }

        uint Construction2ValueTypeSpecReference(PrimitiveType type)
            => type == PrimitiveType.Void
                ? 0xF200_0000u : FieldTypeSpecReference(type);

        int AppendC1M1Construction2Parameters(
            int signatureRow, BoundFunction function, int firstVariable)
        {
            var parameterCount = function.ParameterCount - firstVariable;
            if (parameterCount < 0
                || parameterCount > SemanticVerifier.MaximumArguments)
            {
                throw Unsupported(
                    "A Construction-2 constructor exceeds the checked logical parameter limit.");
            }

            var firstParam = parameterCount == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var variable = function.Variables[firstVariable + ordinal];
                if (variable.IsMutable || variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsInitializationLocal)
                {
                    throw Unsupported(
                        "The bounded Construction-2 constructor projection supports only immutable by-value parameters.");
                }

                parameterRows.Add(new TrRmParamRow(signatureRow, ordinal,
                    ParameterTypeSpecReference(variable.Type), 0));
            }

            return firstParam;
        }

        uint ParameterTypeSpecReference(PrimitiveType type)
            => Construction2ValueTypeSpecReference(type);

        void AppendC1M1Construction2ParameterSurface(
            int methodDefRow, BoundFunction function, int firstVariable)
        {
            if (!schema.UsesClassParameterDefaults())
            {
                return;
            }

            var parameterCount = function.ParameterCount - firstVariable;
            var defaultsByOrdinal
                = new Dictionary<int, BoundOrdinaryParameterDefault>();
            var previousOrdinal = -1;
            if (!function.HasOrdinaryParameterDefaultSurface
                || function.SourceDeclarationInputOrdinal < 0
                || function.OrdinaryParameterDefaults.IsDefault)
            {
                throw Unsupported(
                    "CM1 source publication requires the checked primary parameter surface for every retained class method.");
            }

            foreach (var parameterDefault
                in function.OrdinaryParameterDefaults)
            {
                if (parameterDefault is null
                    || parameterDefault.ParameterOrdinal <= previousOrdinal
                    || parameterDefault.ParameterOrdinal >= parameterCount
                    || parameterDefault.SourceInputOrdinal
                        != function.SourceDeclarationInputOrdinal
                    || parameterDefault.IsPending
                    || parameterDefault.Value is not BoundConstant
                        || parameterDefault.Expression is not null
                    || !defaultsByOrdinal.TryAdd(
                        parameterDefault.ParameterOrdinal,
                        parameterDefault))
                {
                    throw Unsupported(
                        "CM1 source defaults require one checked, closed scalar value per authored parameter default.");
                }

                previousOrdinal = parameterDefault.ParameterOrdinal;
            }

            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var variable = function.Variables[
                    firstVariable + ordinal];
                var defaultConstantSourceRow = 0u;
                if (defaultsByOrdinal.TryGetValue(ordinal,
                        out var parameterDefault))
                {
                    var value = (BoundConstant)parameterDefault.Value!;
                    if (value.Type != variable.Type
                        || value.RepresentationType != variable.Type
                        || (value.Type is PrimitiveType.NativeInt
                            or PrimitiveType.NativeUInt)
                        || !value.Bits.IsCanonicalFor(variable.Type))
                    {
                        throw Unsupported(
                            "CM1 parameter defaults require exact fixed-width SF1 scalar values.");
                    }

                    var expectedTypeSpecRef
                        = ParameterTypeSpecReference(variable.Type);
                    var frozenValue
                        = frozenScalarValueBuilder.Add(value);
                    if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
                    {
                        throw Unsupported(
                            "CM1 parameter default values must retain the exact parameter TypeSpec.");
                    }

                    defaultConstantSourceRow
                        = frozenValue.ConstantSourceRow;
                }

                if (parameterSurfaceRows.Count >= 0xFF_FFFF)
                {
                    throw Unsupported(
                        "The CM1 MethodParameterSurface table exceeds its row limit.");
                }

                parameterSurfaceRows.Add(new C1M1ParameterSurfaceRow(
                    methodDefRow, ordinal, variable.Name,
                    defaultConstantSourceRow));
            }
        }

        foreach (var (typeKind, sourceIndex) in typeDefOrder)
        {
            var isClass = typeKind == 0;
            var isStandardClass = typeKind == 1;
            if (typeKind is < 0 or > 2)
            {
                throw new InvalidDataException(
                    "A retained class TypeDef has an unknown emission kind.");
            }

            if (isClass
                && program.Classes[sourceIndex].IsUnique is not false)
            {
                throw Unsupported(
                    "C1M1 class TypeDefs require known aliasable handle ownership.");
            }

            var standardClassView = isStandardClass
                ? program.StandardClassDeclarations[sourceIndex].Declaration
                : null;
            var declarationName = isClass
                ? program.Classes[sourceIndex].Name
                : isStandardClass
                ? standardClassView!.Name
                : program.Structures[sourceIndex].Name;
            var exportKeys = isClass
                ? exportKeysByClass[sourceIndex] : [];
            var typeDefFlags = isClass
                ? (exportKeys.Count != 0 ? 0x02u
                    : program.Classes[sourceIndex].IsInternal ? 0x01u : 0x02u)
                    | 0x08u
                    | (program.Classes[sourceIndex].IsMutable ? 0x80u : 0u)
                : isStandardClass
                ? standardClassView!.TypeDefFlags
                : (program.Structures[sourceIndex].IsPrivate ? 0u
                    : program.Structures[sourceIndex].IsInternal ? 1u : 2u)
                    | 0x04u;
            var typeFieldCount = isClass
                ? program.Classes[sourceIndex].Fields.Length
                : isStandardClass
                ? standardClassView!.Fields.Length
                : program.Structures[sourceIndex].Fields.Length;
            var typeFieldsAreDefault = isClass
                ? program.Classes[sourceIndex].Fields.IsDefault
                : isStandardClass
                ? standardClassView!.Fields.IsDefault
                : program.Structures[sourceIndex].Fields.IsDefault;
            var typeDefRow = isClass
                ? typeDefRowByClass[sourceIndex]
                : isStandardClass
                ? typeDefRowByStandardClass[sourceIndex]
                : typeDefRowByStructure[sourceIndex];
            if (typeDefRow != typeDefRows.Count + 1
                || typeFieldsAreDefault
                || (isClass && typeFieldCount == 0)
                || (isClass && exportKeys.Count != 0
                    && program.Classes[sourceIndex].IsInternal))
            {
                throw Unsupported(
                    "C1M1 source class declarations require complete instance fields and public roots.");
            }
            var separator = declarationName.LastIndexOf('.');
            var namespaceName = isStandardClass
                ? standardClassView!.Namespace
                : !isClass
                ? sourceStructDeclarations[sourceIndex].Namespace
                : separator < 0 ? string.Empty
                    : declarationName[..separator];
            var typeName = isStandardClass
                ? standardClassView!.Name
                : !isClass
                ? sourceStructDeclarations[sourceIndex].Name
                : separator < 0 ? declarationName
                    : declarationName[(separator + 1)..];
            if (typeName.Length == 0
                || !sourceNames.Add((namespaceName, typeName)))
            {
                throw new InvalidDataException(
                    "Captured source class names are empty or duplicated.");
            }

            var fieldFirst = fieldRows.Count + 1;
            var layoutFieldFirst = layoutFieldRows.Count + 1;
            for (var fieldOrdinal = 0; fieldOrdinal < typeFieldCount;
                fieldOrdinal++)
            {
                string fieldName;
                PrimitiveType fieldType;
                uint fieldFlags;
                if (isStandardClass)
                {
                    var field = standardClassView!.Fields[fieldOrdinal];
                    var layout = standardClassView.Layouts[0];
                    if (field.OwnerDefinitionId
                            != standardClassView.Identity.DefinitionId
                        || field.DefinitionId >> 24 != 0x14
                        || layout.FieldDefinitionIds[fieldOrdinal]
                            != field.DefinitionId)
                    {
                        throw new InvalidDataException(
                            "An imported class FieldDef or layout changed its checked identity.");
                    }

                    fieldName = field.Name;
                    fieldType = field.Type;
                    fieldFlags = field.Flags;
                }
                else
                {
                    var field = isClass
                        ? program.Classes[sourceIndex].Fields[fieldOrdinal]
                        : program.Structures[sourceIndex]
                            .Fields[fieldOrdinal];
                    if (field is null || (field.IsPrivate && field.IsInternal)
                        || field.RequiresUnsafeCaller)
                    {
                        throw Unsupported(
                            "C1M1 source class FieldDefs cannot discard conflicting access or unsafe-caller facts.");
                    }

                    var visibility = field.IsPrivate ? 0u
                        : field.IsInternal ? 1u : 2u;
                    fieldFlags = visibility
                        | (field.IsMutable ? 0x04u : 0u);
                    fieldName = field.Name;
                    fieldType = field.Type;
                }

                var fieldDefRow = fieldRows.Count + 1;
                fieldRows.Add(new TrRmFieldDefRow(typeDefRow, 0,
                    FieldTypeSpecReference(fieldType), fieldFlags));
                fieldNames.Add(fieldName);
                if (isClass)
                {
                    fieldDefRowByClassAndIndex.Add(
                        (sourceIndex, fieldOrdinal), fieldDefRow);
                }
                else if (isStandardClass)
                {
                    fieldDefRowByStandardClassAndIndex.Add(
                        (sourceIndex, fieldOrdinal), fieldDefRow);
                }
                else
                {
                    fieldDefRowByStructureAndIndex.Add(
                        (sourceIndex, fieldOrdinal), fieldDefRow);
                }

                layoutFieldRows.Add(new TrRmLayoutFieldRow(
                    layoutRows.Count + 1, fieldOrdinal, fieldDefRow));
            }

            if (isClass)
            {
                var exportDefinitionId = 0x1300_0000u
                    | checked((uint)typeDefRow);
                foreach (var exportKey in exportKeys)
                {
                    exportRows.Add((exportDefinitionId, exportKey));
                }
            }

            typeDefRows.Add(new TrRmTypeDefRow(0, 0, typeDefFlags,
                typeFieldCount == 0 ? 0 : fieldFirst, typeFieldCount, 0, 0,
                layoutRows.Count + 1, 1, 0, 0));
            typeDefNames.Add((namespaceName, typeName));
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, 0, 0, 0, 0,
                typeFieldCount == 0 ? 0 : layoutFieldFirst,
                typeFieldCount));
        }

        foreach (var structureIndex in retainedStructureIndexes.Order())
        {
            var structure = program.Structures[structureIndex];
            if (structure.StandardTypeOrigin is not
                BoundStandardTypeOrigin)
            {
                continue;
            }

            for (var fieldIndex = 0;
                fieldIndex < structure.Fields.Length; fieldIndex++)
            {
                var field = structure.Fields[fieldIndex];
                if (field.StandardFieldOrigin is not
                    BoundStandardFieldOrigin fieldOrigin)
                {
                    continue;
                }

                if (!fieldOrigin.ExportKeys.IsDefaultOrEmpty)
                {
                    if (!typeImportPlanByStructure.TryGetValue(
                            structureIndex, out var importPlan)
                        || !IsC1M1ImportedStructFieldImportable(structure,
                            field, importPlan))
                    {
                        throw Unsupported(
                            "C1M1 class source emission requires selected public FieldDef exports from the exact imported struct provider.");
                    }

                    if (!fieldDefRowByStructureAndIndex.TryGetValue((
                            structureIndex, fieldIndex), out var fieldDefRow))
                    {
                        throw new InvalidDataException(
                            "A selected imported struct FieldDef has no local expected stub.");
                    }

                    var importKeys
                        = fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty
                            ? fieldOrigin.ExportKeys
                            : fieldOrigin.SelectedExportKeys;
                    foreach (var exportKey in importKeys)
                    {
                        AddImport(fieldOrigin.Provider.ModuleIdentity,
                            fieldOrigin.Provider.TargetRequirement,
                            fieldOrigin.Provider.RuntimePolicy, exportKey,
                            0x1400_0000u
                                | checked((uint)fieldDefRow));
                    }
                }
            }
        }

        foreach (var standardClassIndex in standardClassIndexes.Order())
        {
            var classView = program.StandardClassDeclarations[
                standardClassIndex].Declaration;
            if (!typeImportPlanByStandardClass.TryGetValue(
                    standardClassIndex, out var importPlan))
            {
                throw new InvalidDataException(
                    "A retained Standard class has no TypeDef Import plan.");
            }

            for (var fieldIndex = 0;
                fieldIndex < classView.Fields.Length; fieldIndex++)
            {
                var field = classView.Fields[fieldIndex];
                if (field.SelectedExportKeys.IsDefaultOrEmpty)
                {
                    continue;
                }

                if (!IsC1M1ImportedClassFieldImportable(classView, field,
                        importPlan)
                    || !fieldDefRowByStandardClassAndIndex.TryGetValue((
                        standardClassIndex, fieldIndex), out var fieldDefRow))
                {
                    throw Unsupported(
                        "C1M1 class source emission requires public instance FieldDef exports from the exact imported class provider.");
                }

                foreach (var exportKey in field.SelectedExportKeys)
                {
                    AddImport(importPlan.Provider.ModuleIdentity,
                        importPlan.Provider.TargetRequirement,
                        importPlan.Provider.RuntimePolicy, exportKey,
                        0x1400_0000u | checked((uint)fieldDefRow));
                }
            }
        }

        foreach (var classExport in sourceClassExports.Exports)
        {
            foreach (var memberExport in classExport.MemberExports)
            {
                if (memberExport.Kind != SourceClassMemberKind.Field)
                {
                    continue;
                }

                if (!fieldDefRowByClassAndIndex.TryGetValue((
                        classExport.Declaration.ClassIndex,
                        memberExport.FieldIndex), out var fieldDefRow))
                {
                    throw new InvalidDataException(
                        "A captured source class field ExportKey has no emitted FieldDef.");
                }

                exportRows.Add((
                    0x1400_0000u | checked((uint)fieldDefRow),
                    memberExport.ExportKey));
            }
        }

        if (emitsConstruction2)
        {
            var sourceClassDeclarationsByIndex
                = sourceClassProgram.Declarations.ToDictionary(
                    static declaration => declaration.ClassIndex);
            var constructorFunctionsBySourceDeclaration
                = program.Functions
                    .Select(static (function, functionIndex) =>
                        (function, functionIndex))
                    .Where(static candidate =>
                        candidate.function.IsClassConstructor)
                    .GroupBy(static candidate => (
                        candidate.function.SourceDeclarationInputOrdinal,
                        candidate.function.SourceDeclarationSpan))
                    .ToDictionary(static group => group.Key,
                        static group => group.ToArray());
            var classMethodsBySourceDeclaration = program.Functions
                .Select(static (function, functionIndex) =>
                    (function, functionIndex))
                .Where(static candidate =>
                    candidate.function.DeclaringStruct is not null
                    && !candidate.function.IsClassConstructor
                    && candidate.function.PropertyName is null
                    && candidate.function.SourceDeclarationInputOrdinal
                        >= 0)
                .GroupBy(static candidate => (
                    candidate.function.SourceDeclarationInputOrdinal,
                    candidate.function.SourceDeclarationSpan))
                .ToDictionary(static group => group.Key,
                    static group => group.ToArray());
            var memberExportsByClassAndFunction
                = sourceClassExports.Exports
                    .SelectMany(static export =>
                        export.MemberExports.Select(member => (
                            export.Declaration.ClassIndex, Member: member)))
                    .GroupBy(static candidate => (
                        candidate.ClassIndex,
                        candidate.Member.FunctionIndex))
                    .ToDictionary(static group => group.Key,
                        static group => group
                            .Select(static candidate => candidate.Member)
                            .ToArray());
            foreach (var classIndex in exportKeysByClass.Keys)
            {
                if (!sourceClassDeclarationsByIndex.TryGetValue(classIndex,
                        out var declaration))
                {
                    throw new InvalidDataException(
                        "A retained class TypeDef has no exact source declaration.");
                }

                var constructorDeclarations = new List<(
                    BoundSourceClassPart Part, SyntaxNode Declaration)>();
                var methodDeclarations = new List<(
                    BoundSourceClassPart Part, SyntaxNode Declaration)>();
                foreach (var part in declaration.Parts)
                {
                    foreach (var member in part.Declaration.Children
                        .OfType<SyntaxNode>())
                    {
                        if (member.Kind == SyntaxKind.FieldDeclaration)
                        {
                            if (member.Children
                                .OfType<SyntaxTokenElement>()
                                .Any(static token => token.Token.Kind
                                    == TokenKind.StaticKeyword))
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 class source emitter does not preserve static field initialization.");
                            }

                            continue;
                        }

                        if (member.Kind == SyntaxKind.FunctionDeclaration)
                        {
                            if (!schema.UsesClassParameterDefaults()
                                && HasC1M1Construction2ParameterDefaults(
                                    member))
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 source emitter does not yet retain ordinary member parameter defaults.");
                            }

                            methodDeclarations.Add((part, member));
                            continue;
                        }

                        if (member.Kind != SyntaxKind.ConstructorDeclaration)
                        {
                            throw Unsupported(
                                "The bounded Construction-2 class source emitter does not yet retain properties or other class member declarations.");
                        }

                        if (!schema.UsesClassParameterDefaults()
                            && HasC1M1Construction2ParameterDefaults(member))
                        {
                            throw Unsupported(
                                "The bounded Construction-2 source emitter does not yet retain ordinary member parameter defaults.");
                        }

                        if (IsC1M1Construction2PartialSecondary(member))
                        {
                            continue;
                        }

                        constructorDeclarations.Add((part, member));
                    }
                }

                if (constructorDeclarations.Count == 0)
                {
                    throw Unsupported(
                        "The bounded Construction-2 class source emitter requires at least one complete source constructor per retained class.");
                }

                foreach (var (constructorPart, constructorDeclaration)
                    in constructorDeclarations)
                {
                    if (!constructorFunctionsBySourceDeclaration.TryGetValue((
                            constructorPart.SourceInputOrdinal,
                            constructorDeclaration.Span),
                            out var constructorFunctions)
                        || constructorFunctions.Length != 1)
                    {
                        throw new InvalidDataException(
                            "A retained source class constructor has no unique checked initializer.");
                    }

                    var (function, functionIndex) = constructorFunctions[0];
                    if (function.IsPartialConstructor
                            != IsC1M1Construction2PartialPrimary(
                                constructorDeclaration)
                        || (function.IsPartialConstructor
                            && (function.SourceDeclarationInputOrdinal
                                    != constructorPart.SourceInputOrdinal
                                || function.SourceDeclarationSpan
                                    != constructorDeclaration.Span
                                || !HasC1M1Construction2PartialSecondary(
                                    declaration.Parts, function)))
                    {
                        throw new InvalidDataException(
                            "A partial source class constructor does not map its primary to one checked implementing secondary.");
                    }

                    var classDeclaration
                        = program.Classes[classIndex];
                    var receiverType = function.Variables.IsEmpty
                        ? PrimitiveType.Void : function.Variables[0].Type;
                    if (!function.IsClassConstructor
                        || function.IsConstructor || function.IsStatic
                        || function.IsExternal
                        || function.Intrinsic != BoundIntrinsic.None
                        || function.ClassConstructor != -1
                        || function.DeclaringStruct != classDeclaration.Name
                        || function.ReturnType != PrimitiveType.Void
                        || function.ParameterCount < 1
                        || function.ParameterCount != function.Variables.Length
                        || function.RequiresUnsafeCaller
                        || function.DeclaredRequiresUnsafeCaller
                        || function.DoesNotReturn || function.NoFailureProof
                        || !function.CapturedParameters.IsEmpty
                        || !function.DeclarationContractClauses.IsEmpty
                        || function.ModuleImport is not null
                        || function.GenericMethodInstantiation is not null
                        || ManagedClassTypes.IsOptional(receiverType)
                        || !ManagedClassTypes.IsClass(receiverType)
                        || ManagedClassTypes.ClassIndex(receiverType) != classIndex
                        || ManagedClassTypes.IsWritable(receiverType)
                            != classDeclaration.IsMutable
                        || (function.IsPrivate && function.IsInternal))
                    {
                        throw Unsupported(
                            "The bounded Construction-2 class source emitter cannot discard constructor ownership, signature, access, effect, or completion facts.");
                    }

                    var fieldAssignments
                        = GetC1M1Construction2FieldAssignments(program,
                            function, classDeclaration,
                            sourceStructDeclarations);
                    foreach (var field in classDeclaration.Fields)
                    {
                        _ = FieldTypeSpecReference(field.Type);
                    }

                    var memberExports
                        = memberExportsByClassAndFunction.TryGetValue((
                            classIndex, functionIndex), out var mappedExports)
                            ? mappedExports
                            : Array.Empty<SourceClassMemberExport>();
                    if (memberExports.Any(static member =>
                            member.Kind != SourceClassMemberKind.Constructor))
                    {
                        throw new InvalidDataException(
                            "A class constructor mapping contains a non-constructor member.");
                    }
                    var initializerExportKeys = memberExports
                        .Select(static member => member.ExportKey)
                        .ToImmutableArray();
                    var projectionExportKeys = memberExports
                        .Where(static member =>
                            member.ConstructionProjectionExportKey is not null)
                        .Select(static member =>
                            member.ConstructionProjectionExportKey!)
                        .ToImmutableArray();
                    var classTypeSpecReference
                        = FieldTypeSpecReference(receiverType);
                    var initializerMethodDefRow = methodRows.Count + 1;
                    var projectionMethodDefRow
                        = initializerMethodDefRow + 1;
                    var initializerSignatureRow
                        = methodSignatureRows.Count + 1;
                    var initializerParamFirst
                        = AppendC1M1Construction2Parameters(
                            initializerSignatureRow, function, 1);
                    methodSignatureRows.Add(new TrRmMethodSigRow(
                        0xF200_0000u, classTypeSpecReference,
                        initializerParamFirst,
                        function.ParameterCount - 1, 0, 0));
                    var projectionSignatureRow
                        = methodSignatureRows.Count + 1;
                    var projectionParamFirst
                        = AppendC1M1Construction2Parameters(
                            projectionSignatureRow, function, 1);
                    methodSignatureRows.Add(new TrRmMethodSigRow(
                        classTypeSpecReference, 0, projectionParamFirst,
                        function.ParameterCount - 1, 0, 0));

                    var initializerContractFirst
                        = contractRows.Count + 1;
                    contractRows.Add(new TrRmContractRow(
                        0x1600_0000u
                            | checked((uint)initializerMethodDefRow),
                        0x0003,
                        EncodeC1M1Construction2InitializerContract(
                            fieldAssignments, fieldDefRowByClassAndIndex,
                            classIndex)));
                    var projectionContractFirst
                        = contractRows.Count + 1;
                    contractRows.Add(new TrRmContractRow(
                        0x1600_0000u
                            | checked((uint)projectionMethodDefRow),
                        0x0003,
                        EncodeC1M1Construction2ProjectionContract(
                            initializerMethodDefRow)));

                    var visibility = function.IsPrivate ? 0u
                        : function.IsInternal ? 1u : 2u;
                    methodRows.Add(new TrRmMethodDefRow(
                        typeDefRowByClass[classIndex], 0,
                        initializerSignatureRow, visibility,
                        initializerContractFirst, 1, 0));
                    methodNames.Add(function.Name);
                    methodRows.Add(new TrRmMethodDefRow(
                        typeDefRowByClass[classIndex], 0,
                        projectionSignatureRow,
                        projectionExportKeys.IsEmpty ? 0u : 2u,
                        projectionContractFirst, 1, 0));
                    methodNames.Add(function.Name);
                    AppendC1M1Construction2ParameterSurface(
                        initializerMethodDefRow, function, 1);
                    AppendC1M1Construction2ParameterSurface(
                        projectionMethodDefRow, function, 1);

                    foreach (var exportKey in initializerExportKeys)
                    {
                        exportRows.Add((
                            0x1600_0000u
                                | checked((uint)initializerMethodDefRow),
                            exportKey));
                    }

                    foreach (var exportKey in projectionExportKeys)
                    {
                        exportRows.Add((
                            0x1600_0000u
                                | checked((uint)projectionMethodDefRow),
                            exportKey));
                    }

                    constructionInitializers.Add(
                        new C1M1Construction2SourceInitializer(
                            classIndex, function, classTypeSpecReference,
                            fieldAssignments, initializerMethodDefRow,
                            projectionMethodDefRow));
                }

                foreach (var (methodPart, methodDeclaration)
                    in methodDeclarations)
                {
                    if (!classMethodsBySourceDeclaration.TryGetValue((
                            methodPart.SourceInputOrdinal,
                            methodDeclaration.Span),
                            out var methodFunctions)
                        || methodFunctions.Length != 1)
                    {
                        throw new InvalidDataException(
                            "A retained source class method has no unique checked MethodDef.");
                    }

                    var (methodFunction, methodFunctionIndex)
                        = methodFunctions[0];
                    var isSupportedNoOpInstanceMethod
                        = !methodFunction.IsStatic
                            && methodFunction.ParameterCount
                                == methodFunction.Variables.Length
                            && methodFunction.ParameterCount >= 1
                            && methodFunction.ReturnType
                                == PrimitiveType.Void;
                    var directReturnHome = -1;
                    var isSupportedDirectReturnInstanceMethod
                        = !methodFunction.IsStatic
                            && methodFunction.ReturnType
                                != PrimitiveType.Void
                            && methodFunction.ParameterCount
                                == methodFunction.Variables.Length
                            && TryGetC1M1Construction2DirectParameterReturn(
                                methodFunction, out directReturnHome);
                    var directFieldIndex = -1;
                    var isSupportedDirectFieldReadInstanceMethod
                        = !methodFunction.IsStatic
                            && methodFunction.ReturnType
                                != PrimitiveType.Void
                            && TryGetC1M1Construction2DirectFieldRead(
                                methodFunction, out directFieldIndex);
                    var nestedFieldRead
                        = !isSupportedDirectFieldReadInstanceMethod
                            ? TryGetC1M1Construction2NestedFieldRead(
                                program, methodFunction, classIndex,
                                sourceStructDeclarations,
                                typeImportPlanByStructure,
                                standardClassIndexByIdentity,
                                typeImportPlanByStandardClass,
                                fieldDefRowByClassAndIndex,
                                fieldDefRowByStandardClassAndIndex,
                                fieldDefRowByStructureAndIndex)
                            : null;
                    var directFieldWriteIndex = -1;
                    var isSupportedDirectFieldWriteInstanceMethod
                        = !methodFunction.IsStatic
                            && methodFunction.ReturnType
                                == PrimitiveType.Void
                            && TryGetC1M1Construction2DirectFieldWrite(
                                methodFunction,
                                out directFieldWriteIndex);
                    C1M1Construction2NestedFieldWrite? nestedFieldWrite
                        = !isSupportedDirectFieldWriteInstanceMethod
                            ? TryGetC1M1Construction2ImportedClassFieldWrite(
                                program, methodFunction,
                                standardClassIndexByIdentity,
                                typeImportPlanByStandardClass,
                                fieldDefRowByStandardClassAndIndex)
                            : null;
                    nestedFieldWrite ??=
                        !isSupportedDirectFieldWriteInstanceMethod
                            ? TryGetC1M1Construction2NestedFieldWrite(
                                program, methodFunction, classIndex,
                                sourceStructDeclarations,
                                typeImportPlanByStructure,
                                fieldDefRowByClassAndIndex,
                                fieldDefRowByStructureAndIndex)
                            : null;
                    var conditionalReturn
                        = TryGetC1M1Construction2ConditionalReturn(
                            methodFunction);
                    var identityForwarderTargetFunctionIndex = -1;
                    var isSupportedIdentityForwarder
                        = TryGetC1M1Construction2IdentityForwarder(
                            methodFunction, program.Functions,
                            out identityForwarderTargetFunctionIndex);
                    var isSupportedUnitIdentityForwarder
                        = !isSupportedIdentityForwarder
                            && TryGetC1M1Construction2UnitIdentityForwarder(
                                methodFunction, program.Functions,
                                out identityForwarderTargetFunctionIndex);
                    var directNumericBinary
                        = TryGetC1M1Construction2DirectNumericBinary(
                            methodFunction);
                    var directNumericUnary
                        = TryGetC1M1Construction2DirectNumericUnary(
                            methodFunction);
                    var directBooleanComparison
                        = TryGetC1M1Construction2DirectBooleanComparison(
                            methodFunction);
                    var callNumericBinary
                        = TryGetC1M1Construction2DirectCallNumericBinary(
                            methodFunction, program.Functions);
                    var localCopyReturn
                        = TryGetC1M1Construction2LocalCopyReturn(
                            methodFunction);
                    var localNumericBinaryReturn
                        = TryGetC1M1Construction2LocalNumericBinaryReturn(
                            methodFunction);
                    if ((!methodFunction.IsStatic
                            && !isSupportedNoOpInstanceMethod
                            && !isSupportedDirectReturnInstanceMethod
                            && !isSupportedDirectFieldReadInstanceMethod
                            && nestedFieldRead is null
                            && !isSupportedDirectFieldWriteInstanceMethod
                            && nestedFieldWrite is null
                            && !isSupportedIdentityForwarder
                            && !isSupportedUnitIdentityForwarder
                            && directNumericBinary is null
                            && directNumericUnary is null
                            && directBooleanComparison is null
                            && conditionalReturn is null
                            && callNumericBinary is null
                            && !localCopyReturn
                            && !localNumericBinaryReturn)
                        || methodFunction.IsClassConstructor
                        || methodFunction.IsConstructor
                        || methodFunction.IsExternal
                        || methodFunction.IsCleanup
                        || methodFunction.IsOperator
                        || methodFunction.Intrinsic != BoundIntrinsic.None
                        || methodFunction.ClassConstructor != -1
                        || methodFunction.DeclaringStruct
                            != classDeclaration.Name
                        || methodFunction.RequiresUnsafeCaller
                        || methodFunction.DeclaredRequiresUnsafeCaller
                        || methodFunction.DoesNotReturn
                        || methodFunction.NoFailureProof
                        || methodFunction.HasPureEvaluationForm
                        || methodFunction.DirectNativeImport is not null
                        || methodFunction.NativeVTableMethod is not null
                        || methodFunction.NativeFunctionPointerType
                            is not null
                        || methodFunction.NativeCallableConvention
                            is not null
                        || methodFunction.NativeCallableEntryName
                            is not null
                        || methodFunction.HasNativeExport
                        || methodFunction.NativeExportEntryName
                            is not null
                        || methodFunction.StaticConstructorIdentity
                            is not null
                        || methodFunction.StaticFieldInitializerIndex
                            >= 0
                        || methodFunction.ModuleImport is not null
                        || methodFunction.GenericMethodInstantiation
                            is not null
                        || methodFunction.PropertyName is not null
                        || methodFunction.IndexedBody is not null
                        || !methodFunction.CapturedParameters.IsEmpty
                        || !methodFunction.ResultDependencyParameters
                            .IsEmpty
                        || methodFunction.ResultDependencyIncludesReceiver
                        || methodFunction.ReturnsForwardedBacking
                        || methodFunction.NativeOutputRole is not null
                        || !methodFunction.NativeOutputContracts.IsEmpty
                        || !methodFunction.DependencyContracts.IsEmpty
                        || methodFunction.CopyOriginSummary is not null
                        || methodFunction.HasRawUnionReturnCoverage
                        || !methodFunction.RawUnionReturnCoverage.IsEmpty
                        || !methodFunction.FriendTargets.IsEmpty
                        || !methodFunction.DeclarationContractClauses
                            .IsEmpty
                        || (methodFunction.IsPrivate
                            && methodFunction.IsInternal))
                    {
                        throw Unsupported(
                            "The bounded Construction-2 class method producer supports effect-free scalar static methods, exact identity forwarders, readonly instance int32/Boolean operations and same-class call-binary methods, scalar local-copy/binary returns, empty unit instance methods, readonly instance methods returning one scalar value parameter, direct scalar field reads and direct or nested inline-struct scalar field writes.");
                    }

                    if (methodFunction.ReturnType != PrimitiveType.Void)
                    {
                        if (!methodFunction.IsStatic)
                        {
                            if (!isSupportedDirectReturnInstanceMethod
                                && !isSupportedDirectFieldReadInstanceMethod
                                && nestedFieldRead is null
                                && !isSupportedDirectFieldWriteInstanceMethod
                                && !isSupportedIdentityForwarder
                                && !isSupportedUnitIdentityForwarder
                                && directNumericBinary is null
                                && directNumericUnary is null
                                && directBooleanComparison is null
                                && conditionalReturn is null
                                && callNumericBinary is null
                                && !localCopyReturn
                                && !localNumericBinaryReturn)
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 instance method producer supports only unit, direct scalar-parameter, direct numeric/Boolean operations, same-class fixed-integer call-binary, local-copy/binary returns or direct scalar-field results and direct scalar-field writes.");
                            }
                        }

                        _ = GetC1M1SourceScalarTypeSpecReference(
                            methodFunction.ReturnType);
                    }

                    if (methodFunction.IsStatic)
                    {
                        for (var variableIndex = 0;
                            variableIndex < methodFunction.Variables.Length;
                            variableIndex++)
                        {
                            var variable
                                = methodFunction.Variables[variableIndex];
                            var isUnitParameter
                                = variable.Type == PrimitiveType.Void
                                    && variableIndex
                                        < methodFunction.ParameterCount;
                            if ((variable.Type == PrimitiveType.Void
                                    && !isUnitParameter)
                                || variable.IsOut || variable.IsInit
                                || variable.IsImplicitRef
                                || variable.IsInitializationLocal)
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 static method producer supports only scalar local storage.");
                            }

                            if (!isUnitParameter)
                            {
                                if (!TryGetC1M1SourceScalarTypeSpecReference(
                                        variable.Type, out _)
                                    && !TryGetC1M1ImportedClassFieldType(
                                        variable.Type, program,
                                        standardClassIndexByIdentity,
                                        out _, out _))
                                {
                                    throw Unsupported(
                                        "The bounded Construction-2 static method producer supports scalar or selected imported class-handle value parameters.");
                                }
                            }
                        }
                    }
                    else
                    {
                        var receiverType = methodFunction.Variables[0].Type;
                        if (ManagedClassTypes.IsOptional(receiverType)
                            || !ManagedClassTypes.IsClass(receiverType)
                            || ManagedClassTypes.ClassIndex(receiverType)
                                != classIndex)
                        {
                            throw Unsupported(
                                "The bounded Construction-2 instance method producer requires its exact nominal class receiver.");
                        }

                        for (var variableIndex = 1;
                            variableIndex < methodFunction.Variables.Length;
                            variableIndex++)
                        {
                            var variable
                                = methodFunction.Variables[variableIndex];
                            var isUnitParameter
                                = variable.Type == PrimitiveType.Void
                                    && variableIndex
                                        < methodFunction.ParameterCount;
                            if ((variable.Type == PrimitiveType.Void
                                    && !isUnitParameter)
                                || variable.IsOut || variable.IsInit
                                || variable.IsImplicitRef
                                || variable.IsInitializationLocal)
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 instance method producer supports only scalar value parameters.");
                            }

                            if (!isUnitParameter)
                            {
                                if (!IsC1M1Construction2SourceCopyType(
                                        variable.Type, program,
                                        sourceStructDeclarations)
                                    && !TryGetC1M1ImportedClassFieldType(
                                        variable.Type, program,
                                        standardClassIndexByIdentity,
                                        out _, out _))
                                {
                                    throw Unsupported(
                                        "The bounded Construction-2 instance method producer supports scalar, Copy-struct or selected imported class-handle value parameters.");
                                }
                            }
                        }
                    }

                    var methodExports
                        = memberExportsByClassAndFunction.TryGetValue((
                            classIndex, methodFunctionIndex),
                            out var mappedMethodExports)
                            ? mappedMethodExports
                            : Array.Empty<SourceClassMemberExport>();
                    if (methodExports.Any(static member =>
                            member.Kind != SourceClassMemberKind.Method))
                    {
                        throw new InvalidDataException(
                            "A class method mapping contains a non-method member.");
                    }

                    var methodExportKeys = methodExports
                        .Select(static member => member.ExportKey)
                        .ToArray();
                    var methodRow = methodRows.Count + 1;
                    if (!methodDefRowByFunctionIndex.TryAdd(
                            methodFunctionIndex, methodRow))
                    {
                        throw new InvalidDataException(
                            "A checked class method was assigned more than one MethodDef row.");
                    }

                    byte[]? encodedBody = null;
                    if (isSupportedDirectFieldReadInstanceMethod)
                    {
                        if (!fieldDefRowByClassAndIndex.TryGetValue((
                                classIndex, directFieldIndex),
                                out var fieldDefRow))
                        {
                            throw new InvalidDataException(
                                "A class instance method reads a FieldDef outside its retained owner.");
                        }

                        encodedBody
                            = EncodeC1M1Construction2FieldReadBody(
                                fieldDefRow);
                    }
                    else if (nestedFieldRead is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2NestedFieldReadBody(
                                methodFunction, nestedFieldRead,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (isSupportedDirectFieldWriteInstanceMethod)
                    {
                        if (!fieldDefRowByClassAndIndex.TryGetValue((
                                classIndex, directFieldWriteIndex),
                                out var fieldDefRow))
                        {
                            throw new InvalidDataException(
                                "A class instance method writes a FieldDef outside its retained owner.");
                        }

                        var field = program.Classes[classIndex]
                            .Fields[directFieldWriteIndex];
                        if (!field.IsMutable
                            || field.Type
                                != methodFunction.Variables[1].Type)
                        {
                            throw new InvalidDataException(
                                "A compact class FieldWrite requires a mutable FieldDef with the exact formal type.");
                        }

                        _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
                        encodedBody
                            = EncodeC1M1Construction2FieldWriteBody(
                                fieldDefRow, 0, sourceUse: 1);
                    }
                    else if (nestedFieldWrite is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2NestedFieldWriteBody(
                                methodFunction, nestedFieldWrite,
                                frozenScalarValueBuilder,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (directNumericBinary is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2DirectNumericBinaryBody(
                                methodFunction, directNumericBinary,
                                frozenScalarValueBuilder,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (directBooleanComparison is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2DirectBooleanComparisonBody(
                                methodFunction, directBooleanComparison,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (directNumericUnary is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2DirectNumericUnaryBody(
                                methodFunction, directNumericUnary,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (conditionalReturn is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2ConditionalReturnBody(
                                methodFunction, conditionalReturn.Value,
                                frozenScalarValueBuilder,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (localCopyReturn)
                    {
                        encodedBody
                            = EncodeC1M1Construction2LocalCopyReturnBody(
                                methodFunction,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (localNumericBinaryReturn)
                    {
                        encodedBody
                            = EncodeC1M1Construction2LocalNumericBinaryReturnBody(
                                methodFunction,
                                frozenScalarValueBuilder,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }

                    var methodSignatureRow
                        = methodSignatureRows.Count + 1;
                    var methodParamFirst
                        = AppendC1M1Construction2Parameters(
                            methodSignatureRow, methodFunction,
                            methodFunction.IsStatic ? 0 : 1);
                    var methodResultTypeSpecReference
                        = methodFunction.ReturnType == PrimitiveType.Void
                            ? 0xF200_0000u
                            : FieldTypeSpecReference(
                                methodFunction.ReturnType);
                    var methodReceiverTypeSpecReference
                        = methodFunction.IsStatic ? 0u
                            : FieldTypeSpecReference(
                                methodFunction.Variables[0].Type);
                    methodSignatureRows.Add(new TrRmMethodSigRow(
                        methodResultTypeSpecReference,
                        methodReceiverTypeSpecReference,
                        methodParamFirst,
                        methodFunction.ParameterCount
                            - (methodFunction.IsStatic ? 0 : 1),
                        0, 0));
                    var methodEffectsMask
                        = (methodFunction.DoesNotReturn ? 2u : 0u)
                            | (methodFunction.RequiresUnsafeCaller ? 4u : 0u);
                    var methodContractFirst = methodEffectsMask == 0
                        ? 0 : contractRows.Count + 1;
                    if (methodEffectsMask != 0)
                    {
                        contractRows.Add(new TrRmContractRow(
                            0x1600_0000u | checked((uint)methodRow), 1,
                            s_effectPayloads[methodEffectsMask]));
                    }

                    var methodVisibility = methodFunction.IsPrivate ? 0u
                        : methodFunction.IsInternal ? 1u : 2u;
                    if (!methodFunction.IsStatic
                        && methodFunction.Variables[0].IsMutable)
                    {
                        if (!schema.UsesMethodReceiverAccess())
                        {
                            throw Unsupported(
                                "A mutable class method receiver requires the exact receiver-access successor.");
                        }

                        methodVisibility |= 4u;
                    }

                    methodRows.Add(new TrRmMethodDefRow(
                        typeDefRowByClass[classIndex], 0,
                        methodSignatureRow, methodVisibility,
                        methodContractFirst,
                        methodEffectsMask == 0 ? 0 : 1, 0));
                    methodNames.Add(methodFunction.Name);
                    AppendC1M1Construction2ParameterSurface(methodRow,
                        methodFunction,
                        methodFunction.IsStatic ? 0 : 1);
                    foreach (var exportKey in methodExportKeys)
                    {
                        exportRows.Add((
                            0x1600_0000u | checked((uint)methodRow),
                            exportKey));
                    }

                    construction2SourceMethods.Add(
                        new C1M1Construction2SourceMethod(
                            methodFunction, methodRow,
                            isSupportedDirectReturnInstanceMethod
                                ? directReturnHome : -1,
                            isSupportedIdentityForwarder
                                || isSupportedUnitIdentityForwarder
                                ? identityForwarderTargetFunctionIndex : -1,
                            encodedBody, callNumericBinary));
                }
            }

            featureRows.Add(
                TrRmFeatureRow.ClassConstructionBodiesCandidate);
            if (schema.UsesClassParameterDefaults())
            {
                featureRows.Add(
                    TrRmFeatureRow.MethodParameterLabelsCandidate);
            }

            foreach (var initializer in constructionInitializers)
            {
                bodyRows.Add(EncodeC1M1Construction2InitializerBody(
                    initializer, fieldDefRowByClassAndIndex,
                    Construction2ValueTypeSpecReference,
                    frozenScalarValueBuilder, contractRows.Count, schema));
                methodRows[initializer.InitializerMethodDefRow - 1]
                    = methodRows[initializer.InitializerMethodDefRow - 1]
                        with { BodyRow = bodyRows.Count };
                bodyRows.Add(EncodeC1M1Construction2ProjectionBody(
                    initializer, Construction2ValueTypeSpecReference,
                    contractRows.Count, schema));
                methodRows[initializer.ProjectionMethodDefRow - 1]
                    = methodRows[initializer.ProjectionMethodDefRow - 1]
                        with { BodyRow = bodyRows.Count };
            }

            foreach (var method in construction2SourceMethods)
            {
                var body = method.EncodedBody;
                if (body is null
                    && method.IdentityForwarderTargetFunctionIndex >= 0)
                {
                    if (!methodDefRowByFunctionIndex.TryGetValue(
                            method.IdentityForwarderTargetFunctionIndex,
                            out var targetMethodDefRow))
                    {
                        throw new InvalidDataException(
                            "A class identity forwarder targets a MethodDef outside its retained class closure.");
                    }

                    body = EncodeC1M1Construction2IdentityForwarderBody(
                        targetMethodDefRow);
                }

                if (body is null && method.CallBinaryExpression is not null)
                {
                    if (method.CallBinaryExpression
                            is BoundBinary callBinaryExpression
                        && callBinaryExpression.Left is BoundCall targetCall)
                    {
                        if (!methodDefRowByFunctionIndex.TryGetValue(
                                targetCall.Function,
                                out var targetMethodDefRow))
                        {
                            throw new InvalidDataException(
                                "A class call Body targets a MethodDef outside its retained class closure.");
                        }

                        body = EncodeC1M1Construction2CallNumericBinaryBody(
                            method.Function, callBinaryExpression,
                            targetMethodDefRow,
                            program.Functions[targetCall.Function],
                            frozenScalarValueBuilder,
                            Construction2ValueTypeSpecReference,
                            contractRows.Count, schema);
                    }
                    else
                    {
                        throw new InvalidDataException(
                            "A retained class call Body lost its checked direct target.");
                    }
                }

                if (body is null)
                {
                    body = method.Function.IsStatic
                        ? EncodeC1M1Construction2StaticMethodBody(
                            method.Function, Construction2ValueTypeSpecReference,
                            frozenScalarValueBuilder, contractRows.Count,
                            schema)
                        : method.DirectReturnHome >= 0
                        ? EncodeC1M1Construction2InstanceMethodBody(
                            method.Function, method.DirectReturnHome,
                            Construction2ValueTypeSpecReference,
                            contractRows.Count, schema)
                        : EncodeC1M1Construction2NoOpInstanceMethodBody(
                            method.Function,
                            Construction2ValueTypeSpecReference,
                            contractRows.Count, schema);
                }

                bodyRows.Add(body);
                methodRows[method.MethodDefRow - 1]
                    = methodRows[method.MethodDefRow - 1]
                        with { BodyRow = bodyRows.Count };
            }

            if (frozenScalarValueBuilder.ConstantSourceCount != 0
                && !featureRows.Contains(TrRmFeatureRow.TypedFrozenValues))
            {
                featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
            }

        }

        var totalRows = (long)typeDefRows.Count + fieldRows.Count
            + layoutRows.Count + layoutFieldRows.Count
            + typeSpecRows.Count + exportRows.Count
            + moduleReferences.Count + imports.Count + methodRows.Count
            + methodSignatureRows.Count + parameterRows.Count
            + parameterSurfaceRows.Count + contractRows.Count
            + bodyRows.Count + featureRows.Count
            + frozenScalarValueBuilder.BlobCount
            + frozenScalarValueBuilder.FrozenValueNodeCount
            + frozenScalarValueBuilder.ConstantSourceCount;
        if (typeDefRows.Count > 0xFF_FFFF
            || fieldRows.Count > 0xFF_FFFF
            || layoutRows.Count > 0xFF_FFFF
            || layoutFieldRows.Count > 0xFF_FFFF
            || typeSpecRows.Count > 0xFF_FFFF
            || exportRows.Count > 0xFF_FFFF
            || moduleReferences.Count > 0xFF_FFFF
            || imports.Count > 0xFF_FFFF
            || methodRows.Count > 0xFF_FFFF
            || methodSignatureRows.Count > 0xFF_FFFF
            || parameterRows.Count > 0xFF_FFFF
            || parameterSurfaceRows.Count > 0xFF_FFFF
            || contractRows.Count > 0xFF_FFFF
            || bodyRows.Count > 0xFF_FFFF
            || featureRows.Count > 0xFF_FFFF
            || frozenScalarValueBuilder.BlobCount > 0xFF_FFFF
            || frozenScalarValueBuilder.FrozenValueNodeCount > 0xFF_FFFF
            || frozenScalarValueBuilder.ConstantSourceCount > 0xFF_FFFF
            || totalRows > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The C1M1 source class declaration closure exceeds the checked metadata row limits.");
        }

        var textRows = new List<(string Text, int Encoding)>();
        var stringRows = new Dictionary<string, int>(StringComparer.Ordinal);
        int AddString(string text)
        {
            if (!stringRows.TryGetValue(text, out var row))
            {
                textRows.Add((text, 1));
                row = textRows.Count;
                stringRows.Add(text, row);
            }

            return row;
        }

        var moduleIdentityStringRow = AddString(moduleIdentity);
        foreach (var (namespaceName, typeName) in typeDefNames)
        {
            _ = AddString(namespaceName);
            _ = AddString(typeName);
        }

        foreach (var fieldName in fieldNames)
        {
            _ = AddString(fieldName);
        }

        foreach (var methodName in methodNames)
        {
            _ = AddString(methodName);
        }

        foreach (var parameterSurface in parameterSurfaceRows)
        {
            _ = AddString(parameterSurface.Name);
        }

        foreach (var (_, exportKey) in exportRows)
        {
            _ = AddString(exportKey);
        }

        foreach (var (producerIdentity, _, _) in moduleReferences)
        {
            _ = AddString(producerIdentity);
        }

        foreach (var import in imports)
        {
            _ = AddString(import.ExportKey);
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes,
            textRows.Count);
        for (var typeIndex = 0; typeIndex < typeDefRows.Count; typeIndex++)
        {
            typeDefRows[typeIndex] = typeDefRows[typeIndex] with {
                NamespaceStringRow
                    = stringRows[typeDefNames[typeIndex].Namespace],
                NameStringRow = stringRows[typeDefNames[typeIndex].Name],
            };
        }

        for (var fieldIndex = 0; fieldIndex < fieldRows.Count; fieldIndex++)
        {
            fieldRows[fieldIndex] = fieldRows[fieldIndex] with {
                NameStringRow = stringRows[fieldNames[fieldIndex]],
            };
        }

        for (var methodIndex = 0;
            methodIndex < methodRows.Count; methodIndex++)
        {
            methodRows[methodIndex] = methodRows[methodIndex] with {
                NameStringRow = stringRows[methodNames[methodIndex]],
            };
        }

        var featureBytes = featureRows.Count == 0 ? []
            : TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = featureRows.Count == 0 ? default
            : new TrRmFeatureTableReader(featureBytes,
                featureRows.Count, schema);
        var moduleRefRows = moduleReferences.Select(reference =>
            new TrRmModuleRefRow(
                stringRows[reference.ProducerIdentity],
                reference.TargetRequirement, reference.RuntimePolicy))
            .ToArray();
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleIdentityStringRow,
                moduleTargetRequirement, (uint)program.RuntimePolicy, 0,
                1, 1, 1, 1,
                featureRows.Count == 0 ? 0u : 1u,
                checked((uint)featureRows.Count)),
        ], strings, moduleRefRows.Length, typeDefRows.Count,
            methodRows.Count, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefRows.Length, typeDefRows.Count, methodRows.Count, 0,
            featureRows.Count);
        var moduleRefBytes = moduleRefRows.Length == 0 ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings,
                modules);
        var moduleRefs = moduleRefRows.Length == 0 ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Length, strings, modules);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldRows.Count, methodRows.Count,
            layoutRows.Count, contractRows.Count, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefRows.Count, strings, modules, fieldRows.Count,
            methodRows.Count, layoutRows.Count, contractRows.Count, schema);
        var typeSpecBytes = typeSpecRows.Count == 0 ? [] :
            TrRmTypeSpecTableWriter.Encode(typeSpecRows, typeDefRows.Count,
                imports.Count, 0, 0, schema,
                methodDefCount: methodRows.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(
            typeSpecRows.Count == 0
                ? new byte[sizeof(uint)] : typeSpecBytes,
            typeSpecRows.Count, typeDefRows.Count, imports.Count, 0, 0,
            schema, methodDefCount: methodRows.Count);
        var frozenScalarValueTables
            = frozenScalarValueBuilder.Encode(typeSpecs);
        var parameterSurfaceBytes
            = new byte[checked(parameterSurfaceRows.Count * 16)];
        for (var index = 0; index < parameterSurfaceRows.Count; index++)
        {
            var parameterSurface = parameterSurfaceRows[index];
            var offset = index * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterSurfaceBytes.AsSpan(offset, 4),
                checked((uint)parameterSurface.MethodDefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterSurfaceBytes.AsSpan(offset + 4, 4),
                checked((uint)parameterSurface.Ordinal));
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterSurfaceBytes.AsSpan(offset + 8, 4),
                checked((uint)stringRows[parameterSurface.Name]));
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterSurfaceBytes.AsSpan(offset + 12, 4),
                parameterSurface.DefaultConstantSourceRow);
        }
        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs, schema);
        var fields = new TrRmFieldDefTableReader(fieldBytes,
            fieldRows.Count, strings, typeDefs, typeSpecs);
        var importRows = imports.Select(import =>
            new TrRmImportRow(import.ModuleRefRow,
                stringRows[import.ExportKey],
                import.ExpectedDefinitionId)).ToArray();
        var signatureBytes = methodSignatureRows.Count == 0 ? []
            : TrRmMethodSigTableWriter.Encode(methodSignatureRows,
                typeSpecs, parameterRows.Count, 0);
        var signatures = methodSignatureRows.Count == 0 ? default
            : new TrRmMethodSigTableReader(signatureBytes,
                methodSignatureRows.Count, typeSpecs,
                parameterRows.Count, 0);
        var parameterBytes = parameterRows.Count == 0 ? []
            : TrRmParamTableWriter.Encode(parameterRows, signatures,
                typeSpecs, schema);
        var parameters = parameterRows.Count == 0 ? default
            : new TrRmParamTableReader(parameterBytes,
                parameterRows.Count, signatures, typeSpecs, schema);
        var methodBytes = methodRows.Count == 0 ? []
            : TrRmMethodDefTableWriter.Encode(methodRows, strings, modules,
                typeDefs, signatures, contractRows.Count, bodyRows.Count,
                schema);
        var methods = methodRows.Count == 0 ? default
            : new TrRmMethodDefTableReader(methodBytes, methodRows.Count,
                strings, modules, typeDefs, signatures, contractRows.Count,
                bodyRows.Count, schema);
        var importBytes = importRows.Length == 0 ? []
            : TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                typeDefs, methods, default, default, default, fields,
                features, modules, schema);
        var contractBytes = contractRows.Count == 0 ? []
            : TrRmContractTableWriter.EncodeC1M1Construction2Draft(
                contractRows, typeDefs, methods, signatures, parameters,
                typeSpecs, fields, default, default, features, modules,
                predicateGraphCount: 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs, targetProfiles,
            layoutFieldRows.Count);
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var exportBytes = EncodeC1M1Exports(exportRows,
            stringRows);

        var sections = new List<TrRmSection> {
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, checked((uint)textRows.Count),
                stringBytes),
            new TrRmSection(0x0013, 0,
                checked((uint)typeDefRows.Count), typeDefBytes),
            new TrRmSection(0x0014, 0, checked((uint)fieldRows.Count),
                fieldBytes),
            new TrRmSection(0x0015, 0, checked((uint)layoutRows.Count),
                layoutBytes),
            new TrRmSection(0x001D, 0,
                checked((uint)exportRows.Count), exportBytes),
            new TrRmSection(0x0023, 0,
                checked((uint)layoutFieldRows.Count), layoutFieldBytes),
        };
        if (featureRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                checked((uint)featureRows.Count), featureBytes));
        }
        if (typeSpecRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0011, 0,
                checked((uint)typeSpecRows.Count), typeSpecBytes));
        }
        if (moduleRefRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                checked((uint)moduleRefRows.Length), moduleRefBytes));
        }
        if (importRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                checked((uint)importRows.Length), importBytes));
        }
        if (methodRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0016, 0,
                checked((uint)methodRows.Count), methodBytes));
            sections.Add(new TrRmSection(0x0017, 0,
                checked((uint)methodSignatureRows.Count),
                signatureBytes));
        }
        if (parameterRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                checked((uint)parameterRows.Count), parameterBytes));
        }
        if (contractRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001B, 0,
                checked((uint)contractRows.Count), contractBytes));
        }
        if (bodyRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0020, 0,
                checked((uint)bodyRows.Count),
                TrRmVariableTableWriter.EncodeBodies(bodyRows)));
        }
        if (frozenScalarValueTables.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)frozenScalarValueTables.BlobCount),
                frozenScalarValueTables.BlobSection));
        }
        if (frozenScalarValueTables.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                checked((uint)frozenScalarValueTables.ConstantSourceCount),
                frozenScalarValueTables.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                checked((uint)frozenScalarValueTables.FrozenValueNodeCount),
                frozenScalarValueTables.FrozenValueNodeSection));
        }
        if (parameterSurfaceRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0066, 0,
                checked((uint)parameterSurfaceRows.Count),
                parameterSurfaceBytes));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections,
            schema);
        if (!schema.UsesClassParameterDefaults())
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(artifact, schema));
        }

        return artifact;
    }

    private static uint GetC1M1SourceScalarTypeSpecReference(
        PrimitiveType type)
    {
        if (!TryGetC1M1SourceScalarTypeSpecReference(type,
                out var typeSpecReference))
        {
            throw Unsupported(
                $"C1M1 source class field type '{type}' has no supported local scalar TypeSpec.");
        }

        return typeSpecReference;
    }

    private static bool TryGetC1M1SourceScalarTypeSpecReference(
        PrimitiveType type, out uint typeSpecReference)
    {
        var typeCode = type switch {
            PrimitiveType.Bool => 0x01,
            PrimitiveType.Char8 => 0x02,
            PrimitiveType.Char16 => 0x03,
            PrimitiveType.Char32 => 0x04,
            PrimitiveType.Rune => 0x05,
            PrimitiveType.Int8 => 0x06,
            PrimitiveType.Int16 => 0x07,
            PrimitiveType.Int32 => 0x08,
            PrimitiveType.Int64 => 0x09,
            PrimitiveType.Int128 => 0x0A,
            PrimitiveType.UInt8 => 0x0B,
            PrimitiveType.UInt16 => 0x0C,
            PrimitiveType.UInt32 => 0x0D,
            PrimitiveType.UInt64 => 0x0E,
            PrimitiveType.UInt128 => 0x0F,
            PrimitiveType.NativeInt => 0x10,
            PrimitiveType.NativeUInt => 0x11,
            PrimitiveType.Float16 => 0x12,
            PrimitiveType.BFloat16 => 0x13,
            PrimitiveType.Float32 => 0x14,
            PrimitiveType.Float64 => 0x15,
            PrimitiveType.Float128 => 0x16,
            PrimitiveType.Decimal32 => 0x17,
            PrimitiveType.Decimal64 => 0x18,
            PrimitiveType.Decimal128 => 0x19,
            _ => -1,
        };
        if (typeCode < 0)
        {
            typeSpecReference = 0;
            return false;
        }

        typeSpecReference = 0xF200_0000u | checked((uint)typeCode);
        return true;
    }

    private static bool HasC1M1Construction2ParameterDefaults(
        SyntaxNode declaration)
    {
        var parameterList = declaration.Children
            .OfType<SyntaxNode>()
            .SingleOrDefault(static child =>
                child.Kind == SyntaxKind.ParameterList);
        return parameterList is not null
            && ContainsTokenKind(parameterList, TokenKind.Equals);

        static bool ContainsTokenKind(SyntaxNode node, TokenKind kind)
        {
            foreach (var child in node.Children)
            {
                if (child is SyntaxTokenElement token
                    && token.Token.Kind == kind)
                {
                    return true;
                }

                if (child is SyntaxNode nested
                    && ContainsTokenKind(nested, kind))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool IsC1M1Construction2PartialPrimary(
        SyntaxNode declaration)
        => HasPartialConstructorModifier(declaration)
            && declaration.Children.OfType<SyntaxNode>().Any(
                static child => child.Kind == SyntaxKind.ExternalBody);

    private static bool IsC1M1Construction2PartialSecondary(
        SyntaxNode declaration)
        => HasPartialConstructorModifier(declaration)
            && declaration.Children.OfType<SyntaxNode>().All(
                static child => child.Kind != SyntaxKind.ExternalBody);

    private static bool HasC1M1Construction2PartialSecondary(
        ImmutableArray<BoundSourceClassPart> parts,
        BoundFunction function)
    {
        var matches = 0;
        foreach (var part in parts)
        {
            if (part.SourceInputOrdinal != function.SourceInputOrdinal)
            {
                continue;
            }

            foreach (var declaration in part.Declaration.Children
                .OfType<SyntaxNode>())
            {
                if (declaration.Span == function.Span
                    && IsC1M1Construction2PartialSecondary(declaration))
                {
                    matches++;
                }
            }
        }

        return matches == 1;
    }

    private static bool HasPartialConstructorModifier(
        SyntaxNode declaration)
        => declaration.Children.OfType<SyntaxTokenElement>().Any(
            static token => token.Token.Kind == TokenKind.PartialKeyword);

    private static ImmutableArray<BoundFieldAssignment>
        GetC1M1Construction2FieldAssignments(
            BoundProgram program, BoundFunction function, BoundClass @class,
            IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
                sourceStructDeclarations)
    {
        if (function.Variables.Length != function.ParameterCount
            || function.Body.Statements.IsDefault)
        {
            throw Unsupported(
                "The bounded Construction-2 initializer requires a checked body without untracked local storage.");
        }

        var assignments = ImmutableArray.CreateBuilder<
            BoundFieldAssignment>(@class.Fields.Length);
        var assignedFields = new HashSet<int>();
        var returned = false;
        Collect(function.Body);
        if (assignments.Count != @class.Fields.Length
            || assignedFields.Count != @class.Fields.Length)
        {
            throw Unsupported(
                "The bounded Construction-2 initializer must initialize every class field exactly once.");
        }

        return assignments.MoveToImmutable();

        void Collect(BoundBlock block)
        {
            foreach (var statement in block.Statements)
            {
                if (returned)
                {
                    throw Unsupported(
                        "A Construction-2 initializer cannot continue after its unit return.");
                }

                if (statement is BoundBlock nested)
                {
                    Collect(nested);
                    continue;
                }

                if (statement is BoundReturn { Value: null })
                {
                    returned = true;
                    continue;
                }

                if (statement is not BoundFieldAssignment assignment
                    || assignment.Variable != 0
                    || !assignment.NestedFields.IsEmpty
                    || assignment.IsUnsafeAuthorized
                    || assignment.Field < 0
                    || assignment.Field >= @class.Fields.Length
                    || !assignedFields.Add(assignment.Field))
                {
                    throw Unsupported(
                        "The bounded Construction-2 initializer supports only direct field stores with each field initialized once.");
                }

                var fieldType = @class.Fields[assignment.Field].Type;
                if (assignment.Value is BoundRead read)
                {
                    if (read.Variable <= 0
                        || read.Variable >= function.ParameterCount
                        || read.Type != fieldType
                        || function.Variables[read.Variable].Type
                            != read.Type
                        || !IsC1M1Construction2SourceCopyType(read.Type,
                            program, sourceStructDeclarations))
                    {
                        throw Unsupported(
                            "A Construction-2 initializer parameter store must copy a matching checked value formal.");
                    }
                }
                else if (assignment.Value is BoundConstant constant)
                {
                    if (constant.Type != fieldType
                        || !TryGetC1M1SourceScalarTypeSpecReference(
                            constant.Type, out _))
                    {
                        throw Unsupported(
                            "A Construction-2 initializer constant store must match a supported scalar field type.");
                    }
                }
                else
                {
                    throw Unsupported(
                        "A Construction-2 initializer store must use a direct formal or scalar constant.");
                }

                assignments.Add(assignment);
            }
        }
    }

    private static bool IsC1M1Construction2SourceCopyType(
        PrimitiveType type, BoundProgram program,
        IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
            sourceStructDeclarations)
    {
        var pending = new Stack<(PrimitiveType Type, bool Exit)>();
        var structStates = new Dictionary<int, byte>();
        pending.Push((type, false));
        var work = 0;
        while (pending.Count != 0)
        {
            if (++work > SemanticVerifier.MaximumAnalysisVisits)
            {
                return false;
            }

            var (current, exit) = pending.Pop();
            if (TryGetC1M1SourceScalarTypeSpecReference(current, out _))
            {
                continue;
            }

            if (!TryGetC1M1SourceStructFieldType(current,
                    sourceStructDeclarations, out var structureIndex)
                || (uint)structureIndex >= (uint)program.Structures.Length)
            {
                return false;
            }

            if (exit)
            {
                structStates[structureIndex] = 2;
                continue;
            }

            if (structStates.TryGetValue(structureIndex,
                    out var state))
            {
                if (state == 1)
                {
                    return false;
                }
                continue;
            }

            var structure = program.Structures[structureIndex];
            if (!sourceStructDeclarations.TryGetValue(structureIndex,
                    out var declaration)
                || !IsC1M1SourceStructLayoutSupported(structure,
                    declaration))
            {
                return false;
            }

            structStates.Add(structureIndex, 1);
            pending.Push((current, true));
            for (var fieldIndex = structure.Fields.Length - 1;
                fieldIndex >= 0; fieldIndex--)
            {
                var field = structure.Fields[fieldIndex];
                if (field is null || field.RequiresUnsafeCaller)
                {
                    return false;
                }

                pending.Push((field.Type, false));
            }
        }

        return true;
    }

    private static byte[] EncodeC1M1Construction2InitializerContract(
        ImmutableArray<BoundFieldAssignment> assignments,
        Dictionary<(int ClassIndex, int FieldIndex), int>
            fieldDefRowByClassAndIndex,
        int classIndex)
    {
        var payload = new byte[checked(8 + (assignments.Length * sizeof(uint)))];
        payload[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
            checked((uint)assignments.Length));
        for (var index = 0; index < assignments.Length; index++)
        {
            if (!fieldDefRowByClassAndIndex.TryGetValue(
                    (classIndex, assignments[index].Field),
                    out var fieldDefRow))
            {
                throw new InvalidDataException(
                    "A Construction-2 initializer field has no emitted FieldDef.");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                payload.AsSpan(8 + (index * sizeof(uint))),
                0x1400_0000u | checked((uint)fieldDefRow));
        }

        return payload;
    }

    private static byte[] EncodeC1M1Construction2ProjectionContract(
        int initializerMethodDefRow)
    {
        var payload = new byte[8];
        payload[0] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
            checked((uint)initializerMethodDefRow));
        return payload;
    }

    private static byte[] EncodeC1M1Construction2InitializerBody(
        C1M1Construction2SourceInitializer initializer,
        Dictionary<(int ClassIndex, int FieldIndex), int>
            fieldDefRowByClassAndIndex,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        int contractCount, TrRmArtifactSchema schema)
    {
        var function = initializer.Function;
        var typeTokens = function.Variables
            .Select(variable => CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(variable.Type)))
            .Concat(initializer.FieldAssignments.Select(assignment =>
                CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(assignment.Value.Type))))
            .ToArray();
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        var homeMap = new byte[checked(function.Variables.Length * 8)];
        for (var variableIndex = 0;
            variableIndex < function.Variables.Length; variableIndex++)
        {
            var home = homeMap.AsSpan(variableIndex * 8, 8);
            home[0] = variableIndex == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(home[2..],
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[variableIndex])));
            if (variableIndex != 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(home[4..],
                    checked((ushort)(variableIndex - 1)));
            }
        }

        var records = new List<ReadOnlyMemory<byte>> {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        var code = new List<byte>();
        var constantRows = new List<TrRmFrozenScalarValueReference>();
        foreach (var assignment in initializer.FieldAssignments)
        {
            if (!fieldDefRowByClassAndIndex.TryGetValue((
                    initializer.ClassIndex, assignment.Field),
                    out var fieldDefRow))
            {
                throw new InvalidDataException(
                    "A Construction-2 initializer Body cannot bind its checked field assignment.");
            }

            var fieldTypeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(assignment.Value.Type));
            var selectionRecord = records.Count;
            records.Add(TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 2, accessKind: 3, destinationKind: 1,
                baseKind: 2, destinationIndex: 0, baseIndex: 0,
                indexKind: 0, targetRefKind: 1, indexIndex: 0,
                targetRef: checked((uint)fieldDefRow),
                referentTypeMapIndex: checked((ushort)
                    maps.GetTypeMapIndex(fieldTypeToken)),
                schema));
            AppendRecordReference(0xD8, selectionRecord);

            byte sourceKind;
            ushort sourceIndex;
            if (assignment.Value is BoundRead read)
            {
                sourceKind = 2;
                sourceIndex = checked((ushort)read.Variable);
            }
            else if (assignment.Value is BoundConstant constant)
            {
                sourceKind = 1;
                sourceIndex = 1;
                var constantIndex = checked((ushort)
                    GetConstantMapIndex(constant));
                code.Add(0xD4);
                code.Add(1);
                code.Add((byte)constantIndex);
                code.Add((byte)(constantIndex >> 8));
            }
            else
            {
                throw new InvalidDataException(
                    "A Construction-2 initializer Body contains an unsupported field value.");
            }

            var storeRecord = records.Count;
            records.Add(TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                placeKind: 1, sourceKind: sourceKind, transition: 1,
                sourceUse: 1, placeIndex: 0, sourceIndex: sourceIndex,
                schema: schema));
            AppendRecordReference(0xD9, storeRecord);
        }

        code.Add(0x18);
        code.Add(0);
        var codeBytes = code.ToArray();
        var constantMap = new byte[checked(constantRows.Count * 8)];
        for (var index = 0; index < constantRows.Count; index++)
        {
            var offset = index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset), constantRows[index].TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                constantRows[index].ConstantSourceRow);
        }

        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)codeBytes.Length), 0)],
            records, codeBytes, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        int GetConstantMapIndex(BoundConstant constant)
        {
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            if (frozenValue.TypeSpecRef
                != typeSpecReferenceFor(constant.Type))
            {
                throw Unsupported(
                    "A Construction-2 initializer ConstantMap requires the exact field TypeSpecRef.");
            }

            var constantIndex = constantRows.IndexOf(frozenValue);
            if (constantIndex < 0)
            {
                if (constantRows.Count > ushort.MaxValue)
                {
                    throw Unsupported(
                        "A Construction-2 initializer Body exceeds the ConstantMap index limit.");
                }

                constantIndex = constantRows.Count;
                constantRows.Add(frozenValue);
            }

            return constantIndex;
        }

        void AppendRecordReference(byte opcode, int recordIndex)
        {
            code.Add(opcode);
            code.Add(0);
            var index = checked((ushort)recordIndex);
            code.Add((byte)index);
            code.Add((byte)(index >> 8));
        }
    }

    private static byte[] EncodeC1M1Construction2ProjectionBody(
        C1M1Construction2SourceInitializer initializer,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        var function = initializer.Function;
        var typeTokens = new uint[function.ParameterCount];
        typeTokens[0] = CreateStandardBodyTypeMapToken(
            initializer.ClassTypeSpecReference);
        for (var parameterIndex = 1;
            parameterIndex < function.ParameterCount; parameterIndex++)
        {
            typeTokens[parameterIndex]
                = CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(
                        function.Variables[parameterIndex].Type));
        }

        var methodMapToken = 0x1600_0000u
            | checked((uint)initializer.InitializerMethodDefRow);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            [methodMapToken]);
        var homeMap = new byte[checked(
            (function.ParameterCount + 1) * 8)];
        homeMap[0] = 4;
        BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(2),
            checked((ushort)maps.GetTypeMapIndex(typeTokens[0])));
        homeMap[8] = 3;
        BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(10),
            checked((ushort)maps.GetTypeMapIndex(typeTokens[0])));
        var inputs = new List<TrRmRegisterBodyCallInput>(
            function.ParameterCount) {
            new(2, 5, 0),
        };
        for (var parameterIndex = 1;
            parameterIndex < function.ParameterCount; parameterIndex++)
        {
            var homeIndex = parameterIndex + 1;
            var home = homeMap.AsSpan(homeIndex * 8, 8);
            home[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(home[2..],
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[parameterIndex])));
            BinaryPrimitives.WriteUInt16LittleEndian(home[4..],
                checked((ushort)(parameterIndex - 1)));
            if (function.Variables[parameterIndex].Type
                == PrimitiveType.Void)
            {
                inputs.Add(new TrRmRegisterBodyCallInput(0, 0, 0));
                continue;
            }

            inputs.Add(new TrRmRegisterBodyCallInput(2, 1,
                checked((ushort)homeIndex)));
        }

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
            TrRmRegisterBodyRecordWriter.EncodeObjectConstruction(1, 0,
                0, 0, schema),
            TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 0, 1, 0,
                inputs, [], schema: schema),
            TrRmRegisterBodyRecordWriter.EncodeObjectConstruction(2, 2,
                0, 1, schema),
        };
        var code = new byte[] {
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0xED, 0, 3, 0,
            0xB8, 1,
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2ConditionalReturnBody(
        BoundFunction function,
        C1M1Construction2ConditionalReturn conditionalReturn,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
            typeTokens[parameter] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            var isReceiver = !function.IsStatic && parameter == 0;
            homeMap[offset] = isReceiver ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[parameter])));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                isReceiver ? (ushort)0
                    : checked((ushort)(parameter
                        - (function.IsStatic ? 0 : 1)));
        }

        var constantRows = new List<TrRmFrozenScalarValueReference>();
        var codeBlocks = new List<byte[]> { [] };
        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var pending = new Stack<(int Block, BoundStatement Statement)>();
        pending.Push((0, conditionalReturn.Root));
        var visits = 0;
        while (pending.Count != 0)
        {
            if (++visits > SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "A Construction-2 conditional Body exceeds the control-flow analysis limit.");
            }

            var (blockIndex, pendingStatement) = pending.Pop();
            var statement = pendingStatement;
            while (statement is BoundBlock block)
            {
                if (++visits > SemanticVerifier.MaximumAnalysisVisits
                    || !block.RetiredManagedLocals.IsEmpty
                    || block.Statements.Length != 1)
                {
                    throw new InvalidDataException(
                        "A checked Construction-2 conditional branch has unsupported block structure.");
                }

                statement = block.Statements[0];
            }

            if (statement is BoundIf branch)
            {
                if (branch.Else is null)
                {
                    throw new InvalidDataException(
                        "A checked Construction-2 conditional branch lost its false arm.");
                }

                var elseBranch = branch.Else;
                var trueBlock = AddBlock();
                var falseBlock = AddBlock();
                if (records.Count > ushort.MaxValue)
                {
                    throw Unsupported(
                        "A Construction-2 conditional Body exceeds the ConditionalEdge record limit.");
                }

                var edgeRecordIndex = checked((ushort)records.Count);
                records.Add(new(
                    TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(
                        checked((ushort)trueBlock),
                        checked((ushort)falseBlock))));
                codeBlocks[blockIndex] = EncodeCondition(
                    branch.Condition, edgeRecordIndex);
                pending.Push((falseBlock, elseBranch));
                pending.Push((trueBlock, branch.Then));
            }
            else if (statement is BoundReturn { Value: not null } returned)
            {
                codeBlocks[blockIndex]
                    = EncodeReturnBlock(returned.Value);
            }
            else
            {
                throw new InvalidDataException(
                    "A checked Construction-2 conditional branch has an unsupported statement.");
            }
        }

        var blocks = new List<TrRmRegisterBodyBlock>(codeBlocks.Count);
        var code = new List<byte>();
        foreach (var blockCode in codeBlocks)
        {
            blocks.Add(new TrRmRegisterBodyBlock(
                checked((uint)code.Count),
                checked((uint)blockCode.Length), 0));
            code.AddRange(blockCode);
        }

        var constantMap = new byte[checked(
            constantRows.Count * sizeof(uint) * 2)];
        for (var index = 0; index < constantRows.Count; index++)
        {
            var constant = constantRows[index];
            var offset = index * sizeof(uint) * 2;
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset), constant.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                constant.ConstantSourceRow);
        }

        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap, blocks, records, code.ToArray(),
            contractCount: contractCount, schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        int AddBlock()
        {
            if (codeBlocks.Count > ushort.MaxValue)
            {
                throw Unsupported(
                    "A Construction-2 conditional Body exceeds the Block index limit.");
            }

            var block = codeBlocks.Count;
            codeBlocks.Add([]);
            return block;
        }

        byte[] EncodeCondition(BoundExpression condition,
            ushort edgeIndex)
        {
            var instructions = new List<byte>(18);
            if (condition is BoundRead read)
            {
                AppendHomeLoad(instructions, 0, read.Variable);
            }
            else if (condition is BoundBinary {
                         Left: BoundRead left,
                         Right: BoundRead right,
                     } comparison
                     && comparison.Type == PrimitiveType.Bool)
            {
                AppendHomeLoad(instructions, 0, left.Variable);
                AppendHomeLoad(instructions, 1, right.Variable);
                const byte destinationRegister = 0;
                const byte leftRegister = 0;
                const byte rightRegister = 1;
                var payload = (ushort)(destinationRegister
                    | (0x0F << 4) | (leftRegister << 8)
                    | (rightRegister << 12));
                if (left.Type == PrimitiveType.Bool
                    && right.Type == PrimitiveType.Bool
                    && comparison.Operator is (TokenKind.EqualsEquals
                        or TokenKind.BangEquals))
                {
                    instructions.Add(0xC2);
                    instructions.Add(comparison.Operator
                        == TokenKind.EqualsEquals
                            ? (byte)0x01 : (byte)0x02);
                    instructions.Add((byte)payload);
                    instructions.Add((byte)(payload >> 8));
                }
                else if (left.Type == right.Type
                    && IsC1M1Construction2BodyNumericType(left.Type)
                    && IsC1M1Construction2IntegerComparisonOperator(
                        comparison.Operator))
                {
                    instructions.Add(0xC2);
                    instructions.Add(0);
                    instructions.Add((byte)payload);
                    instructions.Add((byte)(payload >> 8));
                    var predicatePayload = (ushort)(
                        destinationRegister
                        | (destinationRegister << 4) | (0x0F << 8)
                        | (0x0F << 12));
                    instructions.Add(0xC3);
                    instructions.Add(
                        GetC1M1Construction2IntegerComparisonPredicate(
                            comparison.Operator));
                    instructions.Add((byte)predicatePayload);
                    instructions.Add((byte)(predicatePayload >> 8));
                }
                else
                {
                    throw new InvalidDataException(
                        "A Construction-2 conditional Body has an unsupported comparison.");
                }
            }
            else
            {
                throw new InvalidDataException(
                    "A Construction-2 conditional Body lost its checked Boolean condition.");
            }

            instructions.Add(0xE4);
            instructions.Add(0);
            instructions.Add((byte)edgeIndex);
            instructions.Add((byte)(edgeIndex >> 8));
            return instructions.ToArray();
        }

        static void AppendHomeLoad(List<byte> instructions,
            byte destination, int homeIndex)
        {
            var home = checked((ushort)homeIndex);
            instructions.Add(0xD5);
            instructions.Add(destination);
            instructions.Add((byte)home);
            instructions.Add((byte)(home >> 8));
        }

        byte[] EncodeReturnBlock(BoundExpression value)
        {
            if (value is BoundRead read)
            {
                var home = checked((ushort)read.Variable);
                return [
                    0xD5, 0, (byte)home, (byte)(home >> 8), 0x17, 0,
                ];
            }

            if (value is BoundConstant constant)
            {
                var index = checked((ushort)GetConstantMapIndex(constant));
                return [
                    0xD4, 0, (byte)index, (byte)(index >> 8), 0x17, 0,
                ];
            }

            if (value is BoundUnary unary
                && unary.Operand is BoundRead operand
                && unary.Type == function.ReturnType
                && IsC1M1Construction2BodyNumericType(unary.Type)
                && operand.Type == unary.Type)
            {
                var instructions = new List<byte>(10);
                AppendHomeLoad(instructions, 0, operand.Variable);
                byte opcode;
                byte control;
                if (PrimitiveTypeFacts.IsFloating(unary.Type))
                {
                    if (unary.Operator != TokenKind.Minus)
                    {
                        throw new InvalidDataException(
                            "A Construction-2 conditional Body has an unsupported floating unary operation.");
                    }

                    opcode = 0xC0;
                    control = 0x25;
                }
                else
                {
                    (opcode, control)
                        = (unary.Operator, unary.OverflowBehavior) switch
                        {
                            (TokenKind.Tilde, _)
                                => ((byte)0xC1, (byte)0x04),
                            (TokenKind.Minus,
                                BoundIntegerOverflowBehavior.Checked)
                                => ((byte)0xC0, (byte)0x04),
                            (TokenKind.Minus,
                                BoundIntegerOverflowBehavior.Wrapping)
                                => ((byte)0xC0, (byte)0x08),
                            _ => throw new InvalidDataException(
                                "A Construction-2 conditional Body has an unsupported integer unary operation."),
                        };
                }

                instructions.Add(opcode);
                instructions.Add(control);
                instructions.Add(0xF0);
                instructions.Add(0xF0);
                instructions.Add(0x17);
                instructions.Add(0);
                return instructions.ToArray();
            }

            if (value is BoundBinary binary
                && binary.Left.Type == binary.Type
                && binary.Right.Type == binary.Type
                && IsC1M1Construction2BodyNumericType(binary.Type))
            {
                var instructions = new List<byte>(14);
                AppendNumericOperandLoad(instructions, 0, binary.Left);
                AppendNumericOperandLoad(instructions, 1, binary.Right);
                const byte destinationRegister = 0;
                const byte leftRegister = 0;
                const byte rightRegister = 1;
                var payload = (ushort)(destinationRegister
                    | (0x0F << 4) | (leftRegister << 8)
                    | (rightRegister << 12));
                if (PrimitiveTypeFacts.IsFloating(binary.Type))
                {
                    instructions.Add(0xC0);
                    instructions.Add(
                        GetC1M1Construction2FloatingBinaryControl(
                            binary.Operator));
                }
                else
                {
                    var (opcode, control)
                        = GetC1M1Construction2IntegerBinaryEncoding(
                            binary.Operator, binary.OverflowBehavior);
                    instructions.Add(opcode);
                    instructions.Add(control);
                }
                instructions.Add((byte)payload);
                instructions.Add((byte)(payload >> 8));
                instructions.Add(0x17);
                instructions.Add(0);
                return instructions.ToArray();
            }

            throw new InvalidDataException(
                "A Construction-2 conditional Body lost its checked scalar return.");
        }

        int GetConstantMapIndex(BoundConstant constant)
        {
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            if (frozenValue.TypeSpecRef
                != typeSpecReferenceFor(constant.Type))
            {
                throw Unsupported(
                    "A Construction-2 conditional Body ConstantMap requires the exact operand TypeSpecRef.");
            }

            var constantIndex = constantRows.IndexOf(frozenValue);
            if (constantIndex < 0)
            {
                constantIndex = constantRows.Count;
                constantRows.Add(frozenValue);
            }
            return constantIndex;
        }

        void AppendNumericOperandLoad(List<byte> instructions,
            byte destination, BoundExpression operand)
        {
            if (operand is BoundRead read)
            {
                AppendHomeLoad(instructions, destination, read.Variable);
                return;
            }

            if (operand is BoundConstant constant)
            {
                var index = checked((ushort)GetConstantMapIndex(constant));
                instructions.Add(0xD4);
                instructions.Add(destination);
                instructions.Add((byte)index);
                instructions.Add((byte)(index >> 8));
                return;
            }

            throw new InvalidDataException(
                "A Construction-2 conditional binary Body lost a checked numeric operand.");
        }
    }

    private static byte[] EncodeC1M1Construction2StaticMethodBody(
        BoundFunction function,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        int contractCount, TrRmArtifactSchema schema)
    {
        if (!function.IsStatic
            || function.ParameterCount != function.Variables.Length
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            throw Unsupported(
                "A Construction-2 static register Body requires only checked value-formal Homes.");
        }

        BoundExpression? returnValue = null;
        if (function.Body.Statements.IsEmpty)
        {
            if (function.ReturnType != PrimitiveType.Void)
            {
                throw Unsupported(
                    "A Construction-2 static non-unit Body requires an explicit return value.");
            }
        }
        else if (function.Body.Statements is [
                     BoundReturn statementReturn
                 ])
        {
            returnValue = statementReturn.Value;
        }
        else
        {
            throw Unsupported(
                "The bounded Construction-2 static register Body supports one direct return.");
        }

        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var variable = function.Variables[parameter];
            if (variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            {
                throw Unsupported(
                    "A Construction-2 static register Body requires by-value formal Homes.");
            }
        }

        uint[] typeTokens = [];
        byte[] homeMap = [];
        byte[] constantMap = [];
        byte[] code;
        (int LeftHome, int RightHome) SetScalarBinaryHomes(
            PrimitiveType type, int leftVariable, int rightVariable)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(type));
            var homeCount = leftVariable == rightVariable ? 1 : 2;
            typeTokens = new uint[homeCount];
            homeMap = new byte[homeCount * 8];
            for (var home = 0; home < homeCount; home++)
            {
                var variable = home == 0 ? leftVariable : rightVariable;
                typeTokens[home] = typeToken;
                homeMap[home * 8] = 2;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(home * 8 + 4, 2),
                    checked((ushort)variable));
            }

            return (0, homeCount == 1 ? 0 : 1);
        }

        void SetIntegerFormalHome(int variable, PrimitiveType type)
        {
            typeTokens = [
                CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(type)),
            ];
            homeMap = new byte[8];
            homeMap[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(4, 2), checked((ushort)variable));
        }

        if (function.ReturnType == PrimitiveType.Void)
        {
            if (returnValue is not null
                && (returnValue.Type != PrimitiveType.Void
                    || returnValue is not (BoundRead or BoundConstant)))
            {
                throw Unsupported(
                    "A Construction-2 static unit Body can only return unit without dropping an expression effect.");
            }

            code = [0x18, 0];
        }
        else if (returnValue is BoundRead read)
        {
            if (read.Variable < 0
                || read.Variable >= function.ParameterCount
                || read.Type != function.ReturnType
                || function.Variables[read.Variable].Type
                    != function.ReturnType)
            {
                throw Unsupported(
                    "A Construction-2 static Body can return only an exact value formal.");
            }

            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(read.Type));
            typeTokens = [typeToken];
            homeMap = new byte[8];
            homeMap[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(4), checked((ushort)read.Variable));
            code = [
                0xD5, 0,
                0, 0,
                0x17, 0,
            ];
        }
        else if (returnValue is BoundUnary unary
            && function.ReturnType == unary.Type
            && IsC1M1Construction2BodyIntegerType(unary.Type)
            && unary.Operand is BoundRead operand
            && operand.Type == unary.Type
            && (uint)operand.Variable < (uint)function.ParameterCount
            && function.Variables[operand.Variable].Type
                == unary.Type
            && unary.Operator is TokenKind.Minus or TokenKind.Tilde)
        {
            SetIntegerFormalHome(operand.Variable, unary.Type);
            var (opcode, operation) = (unary.Operator,
                unary.OverflowBehavior) switch
            {
                (TokenKind.Tilde, _) => ((byte)0xC1, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Checked)
                    => ((byte)0xC0, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Wrapping)
                    => ((byte)0xC0, (byte)0x08),
                _ => throw Unsupported(
                    "The selected static register Body does not encode this unary integer policy."),
            };
            code = [
                0xD5, 0, 0, 0,
                opcode, operation, 0xF0, 0xF0,
                0x17, 0,
            ];
        }
        else if (returnValue is BoundUnary floatingUnary
            && function.ReturnType == floatingUnary.Type
            && PrimitiveTypeFacts.IsFloating(floatingUnary.Type)
            && floatingUnary.Operator == TokenKind.Minus
            && floatingUnary.Operand is BoundRead floatingOperand
            && floatingOperand.Type == floatingUnary.Type
            && (uint)floatingOperand.Variable
                < (uint)function.ParameterCount
            && function.Variables[floatingOperand.Variable].Type
                == floatingUnary.Type)
        {
            SetIntegerFormalHome(floatingOperand.Variable,
                floatingUnary.Type);
            code = [
                0xD5, 0, 0, 0,
                0xC0, 0x25, 0xF0, 0xF0,
                0x17, 0,
            ];
        }
        else if (returnValue is BoundBinary binary
            && function.ReturnType == binary.Type
            && IsC1M1Construction2BodyIntegerType(binary.Type)
            && binary.Left is BoundRead left
            && binary.Right is BoundRead right
            && left.Type == binary.Type
            && right.Type == binary.Type
            && (uint)left.Variable < (uint)function.ParameterCount
            && (uint)right.Variable < (uint)function.ParameterCount
            && function.Variables[left.Variable].Type
                == binary.Type
            && function.Variables[right.Variable].Type
                == binary.Type
            && binary.Operator is TokenKind.Ampersand or TokenKind.Pipe
                or TokenKind.Caret)
        {
            var (leftHome, rightHome)
                = SetScalarBinaryHomes(binary.Type,
                    left.Variable, right.Variable);
            var operation = binary.Operator switch
            {
                TokenKind.Ampersand => (byte)0x01,
                TokenKind.Pipe => (byte)0x02,
                TokenKind.Caret => (byte)0x03,
                _ => throw new InvalidDataException(
                    "A static Body bitwise operation lost its assigned control."),
            };
            var instructions = new List<byte>(14);
            instructions.Add(0xD5);
            instructions.Add(0);
            instructions.Add(checked((byte)leftHome));
            instructions.Add(0);
            instructions.Add(0xD5);
            instructions.Add(1);
            instructions.Add(checked((byte)rightHome));
            instructions.Add(0);
            const byte destinationRegister = 0;
            const byte leftRegister = 0;
            const byte rightRegister = 1;
            var payload = (ushort)(destinationRegister | (0x0F << 4)
                | (leftRegister << 8) | (rightRegister << 12));
            instructions.Add(0xC1);
            instructions.Add(operation);
            instructions.Add((byte)payload);
            instructions.Add((byte)(payload >> 8));
            instructions.Add(0x17);
            instructions.Add(0);
            code = instructions.ToArray();
        }
        else if (returnValue is BoundBinary arithmetic
            && function.ReturnType == arithmetic.Type
            && IsC1M1Construction2BodyNumericType(arithmetic.Type)
            && arithmetic.Left is BoundRead arithmeticLeft
            && arithmetic.Right is BoundRead arithmeticRight
            && arithmeticLeft.Type == arithmetic.Type
            && arithmeticRight.Type == arithmetic.Type
            && (uint)arithmeticLeft.Variable
                < (uint)function.ParameterCount
            && (uint)arithmeticRight.Variable
                < (uint)function.ParameterCount
            && function.Variables[arithmeticLeft.Variable].Type
                == arithmetic.Type
            && function.Variables[arithmeticRight.Variable].Type
                == arithmetic.Type
            && arithmetic.Operator is TokenKind.Plus or TokenKind.Minus
                or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            var (leftHome, rightHome) = SetScalarBinaryHomes(
                arithmetic.Type, arithmeticLeft.Variable,
                arithmeticRight.Variable);
            var operation = PrimitiveTypeFacts.IsFloating(arithmetic.Type)
                ? GetC1M1Construction2FloatingBinaryControl(
                    arithmetic.Operator)
                : (arithmetic.Operator, arithmetic.OverflowBehavior) switch
                {
                    (TokenKind.Plus,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x01,
                    (TokenKind.Minus,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x02,
                    (TokenKind.Star,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x03,
                    (TokenKind.Slash,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x0C,
                    (TokenKind.Percent,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x0D,
                    (TokenKind.Plus,
                        BoundIntegerOverflowBehavior.Wrapping) => (byte)0x05,
                    (TokenKind.Minus,
                        BoundIntegerOverflowBehavior.Wrapping) => (byte)0x06,
                    (TokenKind.Star,
                        BoundIntegerOverflowBehavior.Wrapping) => (byte)0x07,
                    _ => throw Unsupported(
                        "The selected static register Body does not encode this integer overflow policy."),
                };
            var instructions = new List<byte>(14);
            instructions.Add(0xD5);
            instructions.Add(0);
            instructions.Add(checked((byte)leftHome));
            instructions.Add(0);
            instructions.Add(0xD5);
            instructions.Add(1);
            instructions.Add(checked((byte)rightHome));
            instructions.Add(0);
            const byte destinationRegister = 0;
            const byte leftRegister = 0;
            const byte rightRegister = 1;
            var payload = (ushort)(destinationRegister | (0x0F << 4)
                | (leftRegister << 8) | (rightRegister << 12));
            instructions.Add(0xC0);
            instructions.Add(operation);
            instructions.Add((byte)payload);
            instructions.Add((byte)(payload >> 8));
            instructions.Add(0x17);
            instructions.Add(0);
            code = instructions.ToArray();
        }
        else if (returnValue is BoundBinary comparison
            && function.ReturnType == PrimitiveType.Bool
            && comparison.Type == PrimitiveType.Bool
            && comparison.Left is BoundRead comparisonLeft
            && comparison.Right is BoundRead comparisonRight
            && IsC1M1Construction2BodyNumericType(
                comparisonLeft.Type)
            && comparisonRight.Type == comparisonLeft.Type
            && (uint)comparisonLeft.Variable
                < (uint)function.ParameterCount
            && (uint)comparisonRight.Variable
                < (uint)function.ParameterCount
            && function.Variables[comparisonLeft.Variable].Type
                == comparisonLeft.Type
            && function.Variables[comparisonRight.Variable].Type
                == comparisonRight.Type
            && IsC1M1Construction2IntegerComparisonOperator(
                comparison.Operator))
        {
            var (leftHome, rightHome) = SetScalarBinaryHomes(
                comparisonLeft.Type, comparisonLeft.Variable,
                comparisonRight.Variable);
            code = EncodeC1M1Construction2IntegerComparisonCode(
                leftHome, rightHome, comparison.Operator);
        }
        else if (returnValue is BoundBinary booleanComparison
            && function.ReturnType == PrimitiveType.Bool
            && booleanComparison.Type == PrimitiveType.Bool
            && booleanComparison.Left is BoundRead booleanLeft
            && booleanComparison.Right is BoundRead booleanRight
            && booleanLeft.Type == PrimitiveType.Bool
            && booleanRight.Type == PrimitiveType.Bool
            && (uint)booleanLeft.Variable
                < (uint)function.ParameterCount
            && (uint)booleanRight.Variable
                < (uint)function.ParameterCount
            && function.Variables[booleanLeft.Variable].Type
                == PrimitiveType.Bool
            && function.Variables[booleanRight.Variable].Type
                == PrimitiveType.Bool
            && booleanComparison.Operator is (TokenKind.EqualsEquals
                or TokenKind.BangEquals))
        {
            var (leftHome, rightHome) = SetScalarBinaryHomes(
                PrimitiveType.Bool, booleanLeft.Variable,
                booleanRight.Variable);
            code = EncodeC1M1Construction2BooleanEqualityCode(
                leftHome, rightHome, booleanComparison.Operator);
        }
        else if (returnValue is BoundBinary constantBinary
            && function.ReturnType == constantBinary.Type
            && IsC1M1Construction2BodyNumericType(constantBinary.Type)
            && (PrimitiveTypeFacts.IsFloating(constantBinary.Type)
                ? constantBinary.Operator is TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent
                : constantBinary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent))
        {
            var (formalRead, constant, constantOnLeft)
                = (constantBinary.Left, constantBinary.Right) switch
                {
                    (BoundRead read, BoundConstant value)
                        => (read, value, false),
                    (BoundConstant value, BoundRead read)
                        => (read, value, true),
                    _ => throw Unsupported(
                        "A Construction-2 static constant binary Body requires one direct numeric formal and one scalar constant."),
                };

            if (formalRead.Type != constantBinary.Type
                || (uint)formalRead.Variable
                    >= (uint)function.ParameterCount
                || function.Variables[formalRead.Variable].Type
                    != constantBinary.Type
                || constant.Type != constantBinary.Type
                || constant.RepresentationType != constantBinary.Type
                || !constant.Bits.IsCanonicalFor(constantBinary.Type))
            {
                throw Unsupported(
                    "A Construction-2 static constant binary Body requires exact canonical same-typed operands.");
            }

            SetIntegerFormalHome(formalRead.Variable, formalRead.Type);
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(formalRead.Type);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 static constant binary Body requires the exact operand TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
            var instructions = new List<byte>(14);
            if (constantOnLeft)
            {
                instructions.Add(0xD4);
                instructions.Add(0);
                instructions.Add(0);
                instructions.Add(0);
                instructions.Add(0xD5);
                instructions.Add(1);
                instructions.Add(0);
                instructions.Add(0);
            }
            else
            {
                instructions.Add(0xD5);
                instructions.Add(0);
                instructions.Add(0);
                instructions.Add(0);
                instructions.Add(0xD4);
                instructions.Add(1);
                instructions.Add(0);
                instructions.Add(0);
            }

            var (opcode, operation)
                = PrimitiveTypeFacts.IsFloating(constantBinary.Type)
                    ? ((byte)0xC0,
                        GetC1M1Construction2FloatingBinaryControl(
                            constantBinary.Operator))
                    : GetC1M1Construction2IntegerBinaryEncoding(
                        constantBinary.Operator,
                        constantBinary.OverflowBehavior);
            const byte destinationRegister = 0;
            const byte leftRegister = 0;
            const byte rightRegister = 1;
            var payload = (ushort)(destinationRegister | (0x0F << 4)
                | (leftRegister << 8) | (rightRegister << 12));
            instructions.Add(opcode);
            instructions.Add(operation);
            instructions.Add((byte)payload);
            instructions.Add((byte)(payload >> 8));
            instructions.Add(0x17);
            instructions.Add(0);
            code = instructions.ToArray();
        }
        else if (returnValue is BoundConstant constant)
        {
            if (constant.Type != function.ReturnType
                || constant.RepresentationType != function.ReturnType
                || !constant.Bits.IsCanonicalFor(function.ReturnType))
            {
                throw Unsupported(
                    "A Construction-2 static literal Body requires an exact canonical scalar constant.");
            }

            var frozenValue = frozenScalarValueBuilder.Add(constant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(function.ReturnType);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 static Body ConstantMap requires the exact result TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
            code = [
                0xD4, 0, 0, 0,
                0x17, 0,
            ];
        }
        else
        {
            throw Unsupported(
                "The bounded Construction-2 static register Body supports direct scalar return, numeric unary/binary/comparison, Boolean equality/inequality, or fixed-scalar returns.");
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        if (homeMap.Length != 0)
        {
            if (typeTokens.Length != homeMap.Length / 8)
            {
                throw new InvalidDataException(
                    "A static register Body HomeMap must have one TypeMap token per Home.");
            }

            for (var home = 0; home < typeTokens.Length; home++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(home * 8 + 2, 2),
                    maps.GetTypeMapIndex(typeTokens[home]));
            }
        }

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static bool IsC1M1Construction2BodyIntegerType(
        PrimitiveType type)
        => PrimitiveTypeFacts.IsIntegerOrWide(type)
            && type is not (PrimitiveType.NativeInt
                or PrimitiveType.NativeUInt);

    private static bool IsC1M1Construction2BodyNumericType(
        PrimitiveType type)
        => IsC1M1Construction2BodyIntegerType(type)
            || PrimitiveTypeFacts.IsFloating(type);

    private static bool IsC1M1Construction2IntegerComparisonOperator(
        TokenKind operation)
        => operation is TokenKind.EqualsEquals or TokenKind.BangEquals
            or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater
            or TokenKind.GreaterEquals;

    private static byte GetC1M1Construction2IntegerComparisonPredicate(
        TokenKind operation)
        => operation switch
        {
            TokenKind.EqualsEquals => 2,
            TokenKind.BangEquals => 13,
            TokenKind.Less => 1,
            TokenKind.LessEquals => 3,
            TokenKind.Greater => 4,
            TokenKind.GreaterEquals => 6,
            _ => throw Unsupported(
                "The selected integer comparison has no registered predicate."),
        };

    private static (byte Opcode, byte Control)
        GetC1M1Construction2IntegerBinaryEncoding(TokenKind operation,
            BoundIntegerOverflowBehavior overflowBehavior)
        => (operation, overflowBehavior) switch
        {
            (TokenKind.Ampersand, _) => (0xC1, 0x01),
            (TokenKind.Pipe, _) => (0xC1, 0x02),
            (TokenKind.Caret, _) => (0xC1, 0x03),
            (TokenKind.Plus, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x01),
            (TokenKind.Minus, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x02),
            (TokenKind.Star, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x03),
            (TokenKind.Slash, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x0C),
            (TokenKind.Percent, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x0D),
            (TokenKind.Plus, BoundIntegerOverflowBehavior.Wrapping)
                => (0xC0, 0x05),
            (TokenKind.Minus, BoundIntegerOverflowBehavior.Wrapping)
                => (0xC0, 0x06),
            (TokenKind.Star, BoundIntegerOverflowBehavior.Wrapping)
                => (0xC0, 0x07),
            _ => throw Unsupported(
                "The selected integer binary Body has no registered operation."),
        };

    private static byte GetC1M1Construction2FloatingBinaryControl(
        TokenKind operation)
        => operation switch
        {
            TokenKind.Plus => 0x20,
            TokenKind.Minus => 0x21,
            TokenKind.Star => 0x22,
            TokenKind.Slash => 0x23,
            TokenKind.Percent => 0x24,
            _ => throw Unsupported(
                "The selected floating binary Body has no registered operation."),
        };

    private static byte[] EncodeC1M1Construction2IntegerComparisonCode(
        int leftHome, int rightHome, TokenKind operation)
    {
        if (leftHome < 0 || leftHome > ushort.MaxValue
            || rightHome < 0 || rightHome > ushort.MaxValue)
        {
            throw new InvalidDataException(
                "An integer comparison Body has an invalid Home index.");
        }

        var code = new List<byte>(18);
        AppendHomeLoad(0, leftHome);
        AppendHomeLoad(1, rightHome);
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        code.Add(0xC2);
        code.Add(0);
        code.Add((byte)payload);
        code.Add((byte)(payload >> 8));
        var predicatePayload = (ushort)(destinationRegister
            | (destinationRegister << 4) | (0x0F << 8) | (0x0F << 12));
        code.Add(0xC3);
        code.Add(GetC1M1Construction2IntegerComparisonPredicate(operation));
        code.Add((byte)predicatePayload);
        code.Add((byte)(predicatePayload >> 8));
        code.Add(0x17);
        code.Add(0);
        return code.ToArray();

        void AppendHomeLoad(byte destination, int homeIndex)
        {
            code.Add(0xD5);
            code.Add(destination);
            code.Add(checked((byte)homeIndex));
            code.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2BooleanEqualityCode(
        int leftHome, int rightHome, TokenKind operation)
    {
        if (leftHome < 0 || leftHome > ushort.MaxValue
            || rightHome < 0 || rightHome > ushort.MaxValue
            || operation is not (TokenKind.EqualsEquals
                or TokenKind.BangEquals))
        {
            throw new InvalidDataException(
                "A Boolean comparison Body has an invalid Home or operator.");
        }

        var code = new List<byte>(14);
        AppendHomeLoad(0, leftHome);
        AppendHomeLoad(1, rightHome);
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        code.Add(0xC2);
        code.Add(operation == TokenKind.EqualsEquals
            ? (byte)0x01 : (byte)0x02);
        code.Add((byte)payload);
        code.Add((byte)(payload >> 8));
        code.Add(0x17);
        code.Add(0);
        return code.ToArray();

        void AppendHomeLoad(byte destination, int homeIndex)
        {
            code.Add(0xD5);
            code.Add(destination);
            code.Add(checked((byte)homeIndex));
            code.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2FieldReadBody(
        int fieldDefRow)
    {
        if (fieldDefRow <= 0 || fieldDefRow > 0xFF_FFFF)
        {
            throw new InvalidDataException(
                "A class field-read Body has an invalid FieldDef row.");
        }

        var body = new byte[8];
        body[0] = 0x02;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),
            0x1400_0000u | checked((uint)fieldDefRow));
        return body;
    }

    private static byte[] EncodeC1M1Construction2NestedFieldReadBody(
        BoundFunction function,
        C1M1Construction2NestedFieldRead fieldRead,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        if (function.IsStatic
            || function.ParameterCount != function.Variables.Length
            || function.ParameterCount is < 1 or > 2
            || (uint)fieldRead.BaseVariableIndex
                >= (uint)function.ParameterCount
            || (fieldRead.BaseVariableIndex == 0
                && fieldRead.FieldDefRows.Length < 2)
            || fieldRead.FieldDefRows.Length
                != fieldRead.FieldTypes.Length)
        {
            throw new InvalidDataException(
                "A nested class FieldRead Body has an invalid receiver or path.");
        }

        var typeTokens = new uint[checked(function.ParameterCount
            + fieldRead.FieldTypes.Length)];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var variableIndex = 0;
            variableIndex < function.ParameterCount; variableIndex++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(
                    function.Variables[variableIndex].Type));
            typeTokens[variableIndex] = typeToken;
            var homeOffset = variableIndex * 8;
            homeMap[homeOffset] = variableIndex == 0
                ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(homeOffset + 2, 2),
                checked((ushort)variableIndex));
            if (variableIndex != 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(homeOffset + 4, 2),
                    checked((ushort)(variableIndex - 1)));
            }
        }

        for (var index = 0; index < fieldRead.FieldTypes.Length; index++)
        {
            typeTokens[function.ParameterCount + index]
                = CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(fieldRead.FieldTypes[index]));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var variableIndex = 0;
            variableIndex < function.ParameterCount; variableIndex++)
        {
            var homeOffset = variableIndex * 8;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(homeOffset + 2, 2),
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[variableIndex])));
        }

        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var code = new List<byte>(
            checked((fieldRead.FieldDefRows.Length * 4) + 2));
        for (var index = 0;
            index < fieldRead.FieldDefRows.Length; index++)
        {
            var isTerminal = index == fieldRead.FieldDefRows.Length - 1;
            var destinationRegister = isTerminal ? (byte)0 : (byte)1;
            var baseKind = index == 0 ? (byte)2 : (byte)1;
            var baseIndex = index == 0
                ? checked((ushort)fieldRead.BaseVariableIndex)
                : (ushort)1;
            var fieldTypeToken = typeTokens[
                function.ParameterCount + index];
            if (records.Count > ushort.MaxValue)
            {
                throw Unsupported(
                    "A nested class FieldRead Body exceeds the PlaceSelection index limit.");
            }

            var recordIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 2, accessKind: 1, destinationKind: 1,
                baseKind: baseKind,
                destinationIndex: destinationRegister,
                baseIndex: baseIndex, indexKind: 0, targetRefKind: 1,
                indexIndex: 0,
                targetRef: checked((uint)fieldRead.FieldDefRows[index]),
                referentTypeMapIndex: checked((ushort)
                    maps.GetTypeMapIndex(fieldTypeToken)),
                schema: schema));

            var opcode = isTerminal ? (byte)0xDA : (byte)0xD8;
            code.Add(opcode);
            code.Add(destinationRegister);
            code.Add((byte)recordIndex);
            code.Add((byte)(recordIndex >> 8));
        }

        code.Add(0x17);
        code.Add(0);
        var codeBytes = code.ToArray();
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)codeBytes.Length), 0)],
            records, codeBytes, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2FieldWriteBody(
        int fieldDefRow, int formalOrdinal, byte sourceUse)
    {
        if (fieldDefRow <= 0 || fieldDefRow > 0xFF_FFFF
            || formalOrdinal < 0 || formalOrdinal > ushort.MaxValue
            || sourceUse is < 1 or > 3)
        {
            throw new InvalidDataException(
                "A class field-write Body has an invalid FieldDef, formal or source-use.");
        }

        var body = new byte[12];
        body[0] = 0x03;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),
            0x1400_0000u | checked((uint)fieldDefRow));
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8),
            checked((ushort)formalOrdinal));
        body[10] = sourceUse;
        return body;
    }

    private static byte[] EncodeC1M1Construction2NestedFieldWriteBody(
        BoundFunction function,
        C1M1Construction2NestedFieldWrite fieldWrite,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        if (function.ParameterCount != function.Variables.Length
            || fieldWrite.BaseVariableIndex != 0
            || (function.IsStatic
                ? fieldWrite.FieldDefRows.Length != 1
                    || function.ParameterCount
                        != (fieldWrite.Value is BoundRead ? 2 : 1)
                : !function.Variables[0].IsMutable
                    || function.ParameterCount != 2
                    || fieldWrite.FieldDefRows.Length < 2)
            || fieldWrite.FieldDefRows.Length
                != fieldWrite.FieldTypes.Length
            || fieldWrite.FieldTypes[^1]
                != fieldWrite.Value.Type)
        {
            throw new InvalidDataException(
                "A nested class FieldWrite Body has an invalid receiver, formal or path.");
        }

        var typeTokens = new uint[checked(
            function.ParameterCount + fieldWrite.FieldTypes.Length)];
        for (var index = 0; index < function.ParameterCount; index++)
        {
            typeTokens[index] = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[index].Type));
        }
        for (var index = 0; index < fieldWrite.FieldTypes.Length; index++)
        {
            typeTokens[function.ParameterCount + index]
                = CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(fieldWrite.FieldTypes[index]));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var index = 0; index < function.ParameterCount; index++)
        {
            var home = homeMap.AsSpan(index * 8, 8);
            var isReceiver = !function.IsStatic && index == 0;
            home[0] = isReceiver ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(home[2..],
                checked((ushort)maps.GetTypeMapIndex(typeTokens[index])));
            if (!isReceiver)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(home[4..],
                    checked((ushort)(index - (function.IsStatic ? 0 : 1))));
            }
        }

        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var code = new List<byte>(checked(
            (fieldWrite.FieldDefRows.Length * 4) + 10));
        for (var index = 0;
            index < fieldWrite.FieldDefRows.Length; index++)
        {
            if (records.Count > ushort.MaxValue)
            {
                throw Unsupported(
                    "A nested class FieldWrite Body exceeds the PlaceSelection index limit.");
            }

            var recordIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 2, accessKind: 2, destinationKind: 1,
                baseKind: index == 0 ? (byte)2 : (byte)1,
                destinationIndex: 1,
                baseIndex: index == 0
                    ? checked((ushort)fieldWrite.BaseVariableIndex)
                    : (ushort)1,
                indexKind: 0, targetRefKind: 1, indexIndex: 0,
                targetRef: checked((uint)fieldWrite.FieldDefRows[index]),
                referentTypeMapIndex: checked((ushort)
                    maps.GetTypeMapIndex(typeTokens[
                        function.ParameterCount + index])),
                schema: schema));
            code.Add(0xD8);
            code.Add(1);
            code.Add((byte)recordIndex);
            code.Add((byte)(recordIndex >> 8));
        }

        var constantRows = new List<TrRmFrozenScalarValueReference>();
        byte sourceKind;
        ushort sourceIndex;
        if (fieldWrite.Value is BoundRead read
            && read.Variable == 1
            && read.Type == function.Variables[1].Type)
        {
            sourceKind = 2;
            sourceIndex = 1;
        }
        else if (function.IsStatic
            && fieldWrite.Value is BoundConstant constant
            && constant.Type == fieldWrite.FieldTypes[^1])
        {
            sourceKind = 1;
            sourceIndex = 0;
            var constantIndex = checked((ushort)
            GetConstantMapIndex(constant));
            code.Add(0xD4);
            code.Add(0);
            code.Add((byte)constantIndex);
            code.Add((byte)(constantIndex >> 8));
        }
        else
        {
            throw new InvalidDataException(
            "A nested class FieldWrite Body has an unsupported scalar source.");
        }

        if (records.Count > ushort.MaxValue)
        {
            throw Unsupported(
                "A nested class FieldWrite Body exceeds the SemanticStore index limit.");
        }

        var storeRecordIndex = checked((ushort)records.Count);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
            placeKind: 1, sourceKind: sourceKind, transition: 2,
            sourceUse: 1, placeIndex: 1, sourceIndex: sourceIndex,
            schema: schema));
        code.Add(0xD9);
        code.Add(0);
        code.Add((byte)storeRecordIndex);
        code.Add((byte)(storeRecordIndex >> 8));
        code.Add(0x18);
        code.Add(0);
        var codeBytes = code.ToArray();
        var constantMap = new byte[checked(constantRows.Count * 8)];
        for (var index = 0; index < constantRows.Count; index++)
        {
            var offset = index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset), constantRows[index].TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                constantRows[index].ConstantSourceRow);
        }

        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)codeBytes.Length), 0)],
            records, codeBytes, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        int GetConstantMapIndex(BoundConstant constant)
        {
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            if (frozenValue.TypeSpecRef
                != typeSpecReferenceFor(constant.Type))
            {
                throw Unsupported(
                    "A Construction-2 class field-write ConstantMap requires the exact FieldDef TypeSpecRef.");
            }

            var constantIndex = constantRows.IndexOf(frozenValue);
            if (constantIndex < 0)
            {
                if (constantRows.Count > ushort.MaxValue)
                {
                    throw Unsupported(
                        "A Construction-2 class FieldWrite Body exceeds the ConstantMap index limit.");
                }

                constantIndex = constantRows.Count;
                constantRows.Add(frozenValue);
            }

            return constantIndex;
        }
    }

    private static byte[] EncodeC1M1Construction2DirectNumericBinaryBody(
        BoundFunction function, BoundBinary binary,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        const int receiverCount = 1;
        if (function.IsStatic
            || function.ParameterCount < receiverCount + 1
            || function.Variables.Length != function.ParameterCount
            || function.Variables[0].IsMutable
            || function.Variables.Skip(receiverCount).Any(
                static variable =>
                    variable.IsMutable || variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsInitializationLocal))
        {
            throw Unsupported(
                "A Construction-2 instance numeric binary Body requires readonly value formals and a readonly receiver.");
        }

        var leftRead = binary.Left as BoundRead;
        var rightRead = binary.Right as BoundRead;
        var leftConstant = binary.Left as BoundConstant;
        var rightConstant = binary.Right as BoundConstant;
        var leftReadIsValid = IsValidFormalRead(leftRead);
        var rightReadIsValid = IsValidFormalRead(rightRead);
        var leftConstantIsValid = IsValidConstant(leftConstant);
        var rightConstantIsValid = IsValidConstant(rightConstant);
        var leftType = leftRead?.Type ?? leftConstant?.Type;
        var rightType = rightRead?.Type ?? rightConstant?.Type;
        var hasSameNumericType
            = leftType.HasValue && rightType == leftType
                && IsC1M1Construction2BodyNumericType(leftType.Value);
        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    binary.Operator)
                && hasSameNumericType
                && (leftReadIsValid || leftConstantIsValid)
                && (rightReadIsValid || rightConstantIsValid)
                && (leftReadIsValid || rightReadIsValid);
        var isIntegerValueOperation
            = function.ReturnType == binary.Type
                && leftType.HasValue && binary.Type == leftType.Value
                && hasSameNumericType
                && IsC1M1Construction2BodyIntegerType(leftType.Value)
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        var isFloatingValueOperation
            = function.ReturnType == binary.Type
                && leftType.HasValue && binary.Type == leftType.Value
                && hasSameNumericType
                && PrimitiveTypeFacts.IsFloating(leftType.Value)
                && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        if ((!leftReadIsValid && !leftConstantIsValid)
            || (!rightReadIsValid && !rightConstantIsValid)
            || (!isComparison && !isIntegerValueOperation
                && !isFloatingValueOperation))
        {
            throw Unsupported(
                "A Construction-2 instance numeric Body requires direct same-typed formal reads or one canonical constant.");
        }

        byte[] constantMap = [];
        if (leftConstantIsValid || rightConstantIsValid)
        {
            var constant = leftConstantIsValid
                ? leftConstant! : rightConstant!;
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(constant.Type);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 instance constant Body requires the exact operand TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
            typeTokens[parameter] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var code = new List<byte>(18);
        AppendOperandLoad(0, leftRead, leftReadIsValid);
        AppendOperandLoad(1, rightRead, rightReadIsValid);
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        if (isComparison)
        {
            code.Add(0xC2);
            code.Add(0);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
            var predicatePayload = (ushort)(destinationRegister
                | (destinationRegister << 4) | (0x0F << 8)
                | (0x0F << 12));
            code.Add(0xC3);
            code.Add(GetC1M1Construction2IntegerComparisonPredicate(
                binary.Operator));
            code.Add((byte)predicatePayload);
            code.Add((byte)(predicatePayload >> 8));
        }
        else if (isFloatingValueOperation)
        {
            code.Add(0xC0);
            code.Add(GetC1M1Construction2FloatingBinaryControl(
                binary.Operator));
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        else
        {
            var (opcode, operation)
                = GetC1M1Construction2IntegerBinaryEncoding(
                    binary.Operator, binary.OverflowBehavior);
            code.Add(opcode);
            code.Add(operation);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        code.Add(0x17);
        code.Add(0);

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        bool IsValidFormalRead(BoundRead? read)
            => read is not null
                && IsC1M1Construction2BodyNumericType(read.Type)
                && read.Variable >= receiverCount
                && read.Variable < function.ParameterCount
                && function.Variables[read.Variable].Type
                    == read.Type;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.RepresentationType == constant.Type
                && constant.Bits.IsCanonicalFor(constant.Type);

        void AppendOperandLoad(byte destination, BoundRead? read,
            bool readIsValid)
        {
            if (readIsValid && read is not null)
            {
                AppendHomeLoad(destination, read.Variable);
            }
            else
            {
                code.Add(0xD4);
                code.Add(destination);
                code.Add(0);
                code.Add(0);
            }
        }

        static void AppendHomeLoad(List<byte> instructions,
            byte destination, int homeIndex)
        {
            instructions.Add(0xD5);
            instructions.Add(destination);
            instructions.Add(checked((byte)homeIndex));
            instructions.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2DirectBooleanComparisonBody(
        BoundFunction function, BoundBinary binary,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        if (function.IsStatic || function.ReturnType != PrimitiveType.Bool
            || function.ParameterCount != 3
            || function.Variables.Length != function.ParameterCount
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != PrimitiveType.Bool
            || function.Variables[2].Type != PrimitiveType.Bool
            || function.Variables.Skip(1).Any(static variable =>
                variable.IsMutable || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || binary.Type != PrimitiveType.Bool
            || binary.Operator is not (TokenKind.EqualsEquals
                or TokenKind.BangEquals)
            || binary.Left is not BoundRead left
            || binary.Right is not BoundRead right
            || left.Type != PrimitiveType.Bool
            || right.Type != PrimitiveType.Bool
            || left.Variable is not (1 or 2)
            || right.Variable is not (1 or 2))
        {
            throw Unsupported(
                "A Construction-2 instance Boolean comparison Body requires two direct Boolean value formals and a readonly receiver.");
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            typeTokens[parameter] = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var code = EncodeC1M1Construction2BooleanEqualityCode(
            left.Variable, right.Variable, binary.Operator);
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2DirectNumericUnaryBody(
        BoundFunction function, BoundUnary unary,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        if (function.IsStatic
            || function.ParameterCount != 2
            || function.Variables.Length != function.ParameterCount
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != unary.Type
            || function.Variables[1].IsMutable
            || function.Variables[1].IsOut || function.Variables[1].IsInit
            || function.Variables[1].IsImplicitRef
            || function.Variables[1].IsInitializationLocal
            || function.ReturnType != unary.Type
            || !IsC1M1Construction2BodyNumericType(unary.Type)
            || unary.Operand is not BoundRead operand
            || operand.Type != unary.Type
            || operand.Variable != 1
            || (PrimitiveTypeFacts.IsFloating(unary.Type)
                ? unary.Operator != TokenKind.Minus
                : unary.Operator is not (TokenKind.Minus or TokenKind.Tilde)))
        {
            throw Unsupported(
                "A Construction-2 instance numeric unary Body requires one direct parameter read and a readonly receiver.");
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
            typeTokens[parameter] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var (opcode, operation) = PrimitiveTypeFacts.IsFloating(unary.Type)
            ? ((byte)0xC0, (byte)0x25)
            : (unary.Operator, unary.OverflowBehavior) switch
            {
                (TokenKind.Tilde, _) => ((byte)0xC1, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Checked)
                    => ((byte)0xC0, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Wrapping)
                    => ((byte)0xC0, (byte)0x08),
                _ => throw Unsupported(
                    "The selected instance register Body does not encode this integer unary operation."),
            };
        var code = new byte[] {
            0xD5, 0, 1, 0,
            opcode, operation, 0xF0, 0xF0,
            0x17, 0,
        };
        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2CallNumericBinaryBody(
        BoundFunction function, BoundBinary binary, int targetMethodDefRow,
        BoundFunction targetFunction,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var callerReceiverCount = function.IsStatic ? 0 : 1;
        var targetReceiverCount = targetFunction.IsStatic ? 0 : 1;
        if (function.ParameterCount != callerReceiverCount + 2
            || function.Variables.Length != function.ParameterCount)
        {
            throw Unsupported(
                "A Construction-2 call-binary Body requires exactly two value parameters.");
        }

        var operationType = function.Variables[callerReceiverCount].Type;
        var rightRead = binary.Right as BoundRead;
        var rightConstant = binary.Right as BoundConstant;
        if (targetMethodDefRow <= 0 || targetMethodDefRow > 0xFF_FFFF
            || !IsC1M1Construction2BodyNumericType(operationType)
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || function.Variables.Skip(callerReceiverCount).Any(
                variable => variable.Type != operationType
                || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || binary.Left is not BoundCall call
            || (rightRead is null && !IsValidConstant(rightConstant))
            || (rightRead is not null
                && (rightRead.Type != operationType
                    || rightRead.Variable < callerReceiverCount
                    || rightRead.Variable >= function.ParameterCount))
            || (rightConstant is not null
                && rightConstant.Type != operationType)
            || call.Type != operationType
            || call.Arguments.Length != targetFunction.ParameterCount
            || !call.DefaultedArguments.IsDefaultOrEmpty
            || call.InterfaceCallIdentity is not null
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || targetFunction.IsExternal || targetFunction.IsClassConstructor
            || targetFunction.IsConstructor || targetFunction.IsCleanup
            || targetFunction.IsOperator
            || targetFunction.ModuleImport is not null
            || targetFunction.GenericMethodInstantiation is not null
            || !StringComparer.Ordinal.Equals(function.DeclaringStruct,
                targetFunction.DeclaringStruct)
            || targetFunction.ReturnType != operationType
            || targetFunction.ParameterCount != targetReceiverCount + 2
            || targetFunction.Variables.Length
                < targetFunction.ParameterCount
            || (!targetFunction.IsStatic
                && targetFunction.Variables[0].IsMutable)
            || targetFunction.Variables.Skip(targetReceiverCount)
                .Take(2).Any(variable =>
                    variable.Type != operationType
                    || variable.IsMutable || variable.IsOut
                    || variable.IsInit || variable.IsImplicitRef
                    || variable.IsInitializationLocal))
        {
            throw Unsupported(
                "A Construction-2 call-binary Body requires a same-class direct numeric call and one direct same-typed read or canonical constant.");
        }

        for (var parameter = 0;
            parameter < targetFunction.ParameterCount; parameter++)
        {
            var sourceVariable = targetFunction.IsStatic
                ? callerReceiverCount + parameter
                : parameter == 0 ? 0
                    : callerReceiverCount + parameter - 1;
            var source = function.Variables[sourceVariable];
            var targetParameter = targetFunction.Variables[parameter];
            if (call.ParameterIndex(parameter) != parameter
                || call.ImplicitReferenceTemporary(parameter) != -1
                || call.Arguments[parameter] is not BoundRead argument
                || argument.Variable != sourceVariable
                || argument.Type != source.Type
                || source.Type != targetParameter.Type
                || source.IsMutable != targetParameter.IsMutable
                || source.IsOut != targetParameter.IsOut
                || source.IsInit != targetParameter.IsInit
                || source.IsImplicitRef != targetParameter.IsImplicitRef
                || source.IsInitializationLocal
                    != targetParameter.IsInitializationLocal)
            {
                throw Unsupported(
                    "A Construction-2 call-binary Body requires direct argument Homes matching the selected MethodSig.");
            }
        }

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    binary.Operator);
        var isIntegerValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && IsC1M1Construction2BodyIntegerType(operationType)
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent);
        var isFloatingValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && PrimitiveTypeFacts.IsFloating(operationType)
                && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent);
        if (!isComparison && !isIntegerValueOperation
            && !isFloatingValueOperation)
        {
            throw Unsupported(
                "A Construction-2 call-binary Body requires an assigned numeric operation or Boolean comparison.");
        }

        byte[] constantMap = [];
        if (rightConstant is not null)
        {
            var frozenValue
                = frozenScalarValueBuilder.Add(rightConstant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(operationType);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 call constant Body requires the exact operand TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
            typeTokens[parameter] = typeToken;
        }

        var targetMethodToken
            = 0x1600_0000u | checked((uint)targetMethodDefRow);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            [targetMethodToken]);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            var role = !function.IsStatic && parameter == 0
                ? (byte)1 : (byte)2;
            homeMap[offset] = role;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                role == 2
                    ? checked((ushort)(parameter - callerReceiverCount))
                    : (ushort)0);
        }

        var callInputs = new TrRmRegisterBodyCallInput[
            targetFunction.ParameterCount];
        for (var parameter = 0;
            parameter < callInputs.Length; parameter++)
        {
            var sourceHome = targetFunction.IsStatic
                ? callerReceiverCount + parameter
                : parameter == 0 ? 0
                    : callerReceiverCount + parameter - 1;
            var sourceUse = !targetFunction.IsStatic && parameter == 0
                ? (byte)3 : (byte)1;
            callInputs[parameter] = new(2, sourceUse,
                checked((ushort)sourceHome));
        }
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
            maps.GetMethodMapIndex(targetMethodToken), 0, callInputs, [],
            schema: schema);
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
            new(callSite),
        ];
        var callSiteRecordIndex = checked((ushort)(records.Length - 1));
        var code = new List<byte>(isComparison ? 18 : 14);
        code.Add(0xE0);
        code.Add(0);
        code.Add(unchecked((byte)callSiteRecordIndex));
        code.Add((byte)(callSiteRecordIndex >> 8));
        if (rightRead is not null)
        {
            AppendHomeLoad(1, rightRead.Variable);
        }
        else
        {
            code.Add(0xD4);
            code.Add(1);
            code.Add(0);
            code.Add(0);
        }
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        if (isComparison)
        {
            code.Add(0xC2);
            code.Add(0);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
            var predicatePayload = (ushort)(destinationRegister
                | (destinationRegister << 4) | (0x0F << 8)
                | (0x0F << 12));
            code.Add(0xC3);
            code.Add(GetC1M1Construction2IntegerComparisonPredicate(
                binary.Operator));
            code.Add((byte)predicatePayload);
            code.Add((byte)(predicatePayload >> 8));
        }
        else if (isFloatingValueOperation)
        {
            code.Add(0xC0);
            code.Add(GetC1M1Construction2FloatingBinaryControl(
                binary.Operator));
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        else
        {
            var (opcode, control)
                = GetC1M1Construction2IntegerBinaryEncoding(
                    binary.Operator, binary.OverflowBehavior);
            code.Add(opcode);
            code.Add(control);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        code.Add(0x17);
        code.Add(0);
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        void AppendHomeLoad(byte destination, int homeIndex)
        {
            code.Add(0xD5);
            code.Add(destination);
            code.Add(checked((byte)homeIndex));
            code.Add(checked((byte)(homeIndex >> 8)));
        }

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.Type == constant.RepresentationType
                && constant.Bits.IsCanonicalFor(constant.Type);
    }

    private static byte[] EncodeC1M1Construction2LocalCopyReturnBody(
        BoundFunction function,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localVariable = receiverCount + 1;
        if (function.ParameterCount != receiverCount + 1
            || function.Variables.Length != receiverCount + 2)
        {
            throw Unsupported(
                "A Construction-2 local-copy Body requires one value formal and one local.");
        }

        var operationType = function.Variables[receiverCount].Type;
        if (!IsC1M1Construction2BodyNumericType(operationType)
            || function.ReturnType != operationType
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || function.Variables[receiverCount].IsMutable
            || function.Variables[receiverCount].IsOut
            || function.Variables[receiverCount].IsInit
            || function.Variables[receiverCount].IsImplicitRef
            || function.Variables[receiverCount].IsInitializationLocal
            || function.Variables[localVariable].Type
                != operationType
            || function.Variables[localVariable].IsMutable
            || function.Variables[localVariable].IsOut
            || function.Variables[localVariable].IsInit
            || function.Variables[localVariable].IsImplicitRef
            || function.Variables[localVariable].IsInitializationLocal
            || function.Body.RetiredManagedLocals.Length != 0
            || function.Body.Statements is not [
                BoundLocal local,
                BoundReturn { Value: BoundRead returnedRead },
            ]
            || local.Variable != localVariable
            || local.Initializer is not BoundRead initialRead
            || initialRead.Type != operationType
            || initialRead.Variable != receiverCount
            || returnedRead.Type != operationType
            || returnedRead.Variable != localVariable)
        {
            throw Unsupported(
                "A Construction-2 local-copy Body requires one initialized numeric local copied from a same-typed value formal and returned directly.");
        }

        var typeTokens = new uint[function.Variables.Length];
        var homeMap = new byte[checked(function.Variables.Length * 8)];
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[variable].Type));
            typeTokens[variable] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            var offset = variable * 8;
            var role = !function.IsStatic && variable == 0 ? (byte)1
                : variable < function.ParameterCount ? (byte)2 : (byte)3;
            homeMap[offset] = role;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[variable]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                role == 2
                    ? checked((ushort)(variable - receiverCount))
                    : (ushort)0);
        }

        var code = new byte[] {
            0xD5, 0, checked((byte)receiverCount), 0,
            0xD6, 0x10, checked((byte)localVariable), 0,
            0xEB, 0, checked((byte)localVariable), 0,
        };
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2LocalNumericBinaryReturnBody(
        BoundFunction function,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localCount = function.Variables.Length
            - function.ParameterCount;
        if (function.ParameterCount < receiverCount + 1
            || localCount <= 0
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != localCount + 1
            || function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundBinary binary,
            })
        {
            throw Unsupported(
                "A Construction-2 local-binary Body requires initialized numeric locals followed by one binary return.");
        }

        var operationType = function.Variables[receiverCount].Type;
        if (!IsC1M1Construction2BodyNumericType(operationType)
            || function.Variables.Skip(receiverCount).Any(variable =>
                variable.Type != operationType || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            throw Unsupported(
                "A Construction-2 local-binary Body requires one numeric value type and immutable local copies.");
        }

        var localInitializers = new BoundRead[localCount];
        for (var localOrdinal = 0;
            localOrdinal < localCount; localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            if (function.Body.Statements[localOrdinal] is not BoundLocal local
                || local.Variable != localVariable
                || local.Initializer is not BoundRead initializer
                || initializer.Type != operationType
                || initializer.Variable < 0
                || initializer.Variable >= localVariable
                || function.Variables[initializer.Variable].Type
                    != operationType)
            {
                throw Unsupported(
                    "A Construction-2 local-binary Body requires source-ordered numeric local copies from initialized Homes.");
            }

            localInitializers[localOrdinal] = initializer;
        }

        var leftRead = binary.Left as BoundRead;
        var rightRead = binary.Right as BoundRead;
        var leftConstant = binary.Left as BoundConstant;
        var rightConstant = binary.Right as BoundConstant;
        var leftReadIsValid = IsValidHomeRead(leftRead);
        var rightReadIsValid = IsValidHomeRead(rightRead);
        var leftConstantIsValid = IsValidConstant(leftConstant);
        var rightConstantIsValid = IsValidConstant(rightConstant);
        var leftType = leftRead?.Type ?? leftConstant?.Type;
        var rightType = rightRead?.Type ?? rightConstant?.Type;
        var hasSameNumericType
            = leftType == operationType && rightType == leftType;

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    binary.Operator)
                && hasSameNumericType
                && (leftReadIsValid || leftConstantIsValid)
                && (rightReadIsValid || rightConstantIsValid)
                && (leftReadIsValid || rightReadIsValid);
        var isIntegerValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && hasSameNumericType
                && IsC1M1Construction2BodyIntegerType(operationType)
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        var isFloatingValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && hasSameNumericType
                && PrimitiveTypeFacts.IsFloating(operationType)
                && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        if ((!leftReadIsValid && !leftConstantIsValid)
            || (!rightReadIsValid && !rightConstantIsValid)
            || (!isComparison && !isIntegerValueOperation
                && !isFloatingValueOperation))
        {
            throw Unsupported(
                "A Construction-2 local-binary Body requires direct same-typed Home reads or one canonical constant.");
        }

        byte[] constantMap = [];
        if (leftConstantIsValid || rightConstantIsValid)
        {
            var constant = leftConstantIsValid
                ? leftConstant! : rightConstant!;
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(operationType);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 local constant Body requires the exact operand TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
        }

        var typeTokens = new uint[function.Variables.Length];
        var homeMap = new byte[checked(function.Variables.Length * 8)];
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            typeTokens[variable] = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[variable].Type));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            var offset = variable * 8;
            var role = !function.IsStatic && variable == 0 ? (byte)1
                : variable < function.ParameterCount ? (byte)2 : (byte)3;
            homeMap[offset] = role;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[variable]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                role == 2
                    ? checked((ushort)(variable - receiverCount))
                    : (ushort)0);
        }

        var code = new List<byte>(checked(localCount * 8
            + (isComparison ? 18 : 14)));
        for (var localOrdinal = 0;
            localOrdinal < localCount; localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            AppendHomeLoad(code, 0,
                localInitializers[localOrdinal].Variable);
            var homeIndex = checked((ushort)localVariable);
            code.Add(0xD6);
            code.Add(0x10);
            code.Add((byte)homeIndex);
            code.Add((byte)(homeIndex >> 8));
        }

        AppendOperandLoad(0, leftRead, leftReadIsValid);
        AppendOperandLoad(1, rightRead, rightReadIsValid);
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        if (isComparison)
        {
            code.Add(0xC2);
            code.Add(0);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
            var predicatePayload = (ushort)(destinationRegister
                | (destinationRegister << 4) | (0x0F << 8)
                | (0x0F << 12));
            code.Add(0xC3);
            code.Add(GetC1M1Construction2IntegerComparisonPredicate(
                binary.Operator));
            code.Add((byte)predicatePayload);
            code.Add((byte)(predicatePayload >> 8));
        }
        else if (isFloatingValueOperation)
        {
            code.Add(0xC0);
            code.Add(GetC1M1Construction2FloatingBinaryControl(
                binary.Operator));
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        else
        {
            var (opcode, control)
                = GetC1M1Construction2IntegerBinaryEncoding(
                    binary.Operator, binary.OverflowBehavior);
            code.Add(opcode);
            code.Add(control);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        code.Add(0x17);
        code.Add(0);

        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        bool IsValidHomeRead(BoundRead? read)
            => read is not null && read.Type == operationType
                && IsC1M1Construction2BodyNumericType(read.Type)
                && read.Variable >= receiverCount
                && read.Variable < function.Variables.Length
                && function.Variables[read.Variable].Type
                    == operationType;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.RepresentationType == constant.Type
                && constant.Bits.IsCanonicalFor(constant.Type);

        void AppendOperandLoad(byte destination, BoundRead? read,
            bool readIsValid)
        {
            if (readIsValid && read is not null)
            {
                AppendHomeLoad(code, destination, read.Variable);
            }
            else
            {
                code.Add(0xD4);
                code.Add(destination);
                code.Add(0);
                code.Add(0);
            }
        }

        static void AppendHomeLoad(List<byte> instructions,
            byte destination, int homeIndex)
        {
            instructions.Add(0xD5);
            instructions.Add(destination);
            instructions.Add(checked((byte)homeIndex));
            instructions.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2IdentityForwarderBody(
        int targetMethodDefRow)
    {
        if (targetMethodDefRow <= 0 || targetMethodDefRow > 0xFF_FFFF)
        {
            throw new InvalidDataException(
                "A class identity-forwarder Body has an invalid MethodDef row.");
        }

        var body = new byte[8];
        body[0] = 0x05;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),
            0x1600_0000u | checked((uint)targetMethodDefRow));
        return body;
    }

    private static byte[] EncodeC1M1Construction2InstanceMethodBody(
        BoundFunction function, int returnHomeIndex,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        if (function.IsStatic
            || function.ParameterCount != function.Variables.Length
            || returnHomeIndex <= 0
            || returnHomeIndex >= function.ParameterCount
            || function.Variables[returnHomeIndex].Type
                != function.ReturnType)
        {
            throw Unsupported(
                "A Construction-2 instance ReturnHome must select a typed explicit value parameter.");
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var variable = function.Variables[parameter];
            if (variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            {
                throw Unsupported(
                    "A Construction-2 instance ReturnHome requires value receiver and parameter Homes.");
            }

            typeTokens[parameter] = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(variable.Type));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        var code = new byte[] { 0xEB, 0, 0, 0 };
        BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(2),
            checked((ushort)returnHomeIndex));
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2NoOpInstanceMethodBody(
        BoundFunction function,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        RequireC1M1Construction2NoOpUnitBody(function.Body);
        if (function.ParameterCount != function.Variables.Length
            || function.ParameterCount < 1)
        {
            throw Unsupported(
                "A Construction-2 unit instance method has an invalid receiver or parameter map.");
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var variable = function.Variables[parameter];
            if (variable.IsOut || variable.IsInit
                || variable.IsImplicitRef)
            {
                throw Unsupported(
                    "A Construction-2 unit instance method requires value-parameter Homes.");
            }

            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(variable.Type));
            typeTokens[parameter] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        var code = new byte[] { 0x18, 0 };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static C1M1Construction2ConditionalReturn?
        TryGetC1M1Construction2ConditionalReturn(BoundFunction function)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        if (function.ParameterCount < receiverCount
            || function.ParameterCount != function.Variables.Length
            || function.Body.Statements.Length != 1
            || function.Body.Statements[0] is not BoundIf root
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return null;
        }

        if (receiverCount != 0)
        {
            var receiver = function.Variables[0];
            if (ManagedClassTypes.IsOptional(receiver.Type)
                || !ManagedClassTypes.IsClass(receiver.Type)
                || receiver.IsMutable || receiver.IsOut
                || receiver.IsInit || receiver.IsImplicitRef
                || receiver.IsInitializationLocal)
            {
                return null;
            }
        }

        if (function.ReturnType != PrimitiveType.Bool
            && !IsC1M1Construction2BodyNumericType(function.ReturnType))
        {
            return null;
        }

        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            if (parameter >= receiverCount
                && !IsScalarParameter(parameter))
            {
                return null;
            }
        }

        var pending = new Stack<BoundStatement>();
        pending.Push(root);
        var visits = 0;
        while (pending.Count != 0)
        {
            if (++visits > SemanticVerifier.MaximumAnalysisVisits)
            {
                return null;
            }

            var statement = pending.Pop();
            while (statement is BoundBlock block)
            {
                if (++visits > SemanticVerifier.MaximumAnalysisVisits
                    || !block.RetiredManagedLocals.IsEmpty
                    || block.Statements.Length != 1)
                {
                    return null;
                }

                statement = block.Statements[0];
            }

            if (statement is BoundIf branch)
            {
                if (branch.Else is null
                    || !branch.GenericUniquePropertyWhenTrue.IsEmpty
                    || !branch.GenericUniquePropertyWhenFalse.IsEmpty
                    || branch.GenericUniquePropertyKnownResult is not null
                    || branch.Condition.Type != PrimitiveType.Bool
                    || !IsSupportedCondition(branch.Condition))
                {
                    return null;
                }

                pending.Push(branch.Else);
                pending.Push(branch.Then);
                continue;
            }

            if (statement is BoundReturn { Value: not null } returned
                && returned.Value.Type == function.ReturnType
                && IsScalarReturn(returned.Value))
            {
                continue;
            }

            return null;
        }

        return new C1M1Construction2ConditionalReturn(root);

        bool IsScalarParameter(int variableIndex)
        {
            if ((uint)variableIndex >= (uint)function.ParameterCount
                || variableIndex < receiverCount)
            {
                return false;
            }

            var variable = function.Variables[variableIndex];
            return (variable.Type == PrimitiveType.Bool
                    || IsC1M1Construction2BodyNumericType(variable.Type))
                && !variable.IsMutable
                && !variable.IsOut
                && !variable.IsInit
                && !variable.IsImplicitRef
                && !variable.IsInitializationLocal;
        }

        bool IsScalarRead(BoundRead read)
            => IsScalarParameter(read.Variable)
                && function.Variables[read.Variable].Type == read.Type;

        bool IsSupportedCondition(BoundExpression expression)
        {
            if (expression is BoundRead read)
            {
                return IsScalarRead(read)
                    && read.Type == PrimitiveType.Bool;
            }

            return expression is BoundBinary {
                       Left: BoundRead left,
                       Right: BoundRead right,
                   } comparison
                && comparison.Type == PrimitiveType.Bool
                && IsScalarRead(left)
                && IsScalarRead(right)
                && ((left.Type == PrimitiveType.Bool
                        && right.Type == PrimitiveType.Bool
                        && comparison.Operator is (
                            TokenKind.EqualsEquals
                            or TokenKind.BangEquals))
                    || (left.Type == right.Type
                        && IsC1M1Construction2BodyNumericType(left.Type)
                        && IsC1M1Construction2IntegerComparisonOperator(
                            comparison.Operator)));
        }

        bool IsScalarReturn(BoundExpression value)
        {
            if (value is BoundRead read)
            {
                return IsScalarRead(read);
            }

            if (value is BoundConstant constant)
            {
                return IsScalarConstant(constant, function.ReturnType);
            }

            if (value is BoundBinary binary)
            {
                return IsScalarBinaryReturn(binary);
            }

            if (value is BoundUnary unary)
            {
                return IsScalarUnaryReturn(unary);
            }

            return false;
        }

        static bool IsScalarConstant(BoundConstant constant,
            PrimitiveType type)
            => constant.Type == type
                && constant.RepresentationType == type
                && constant.Bits.IsCanonicalFor(type);

        bool IsScalarNumericOperand(BoundExpression value,
            PrimitiveType type)
            => value is BoundRead read
                ? read.Type == type && IsScalarRead(read)
                : value is BoundConstant constant
                    && IsScalarConstant(constant, type);

        bool IsScalarUnaryReturn(BoundUnary unary)
        {
            if (unary.Type != function.ReturnType
                || !IsC1M1Construction2BodyNumericType(unary.Type)
                || unary.Operand is not BoundRead operand
                || operand.Type != unary.Type
                || !IsScalarRead(operand))
            {
                return false;
            }

            if (PrimitiveTypeFacts.IsFloating(unary.Type))
            {
                return unary.Operator == TokenKind.Minus;
            }

            return unary.Operator switch
            {
                TokenKind.Tilde => true,
                TokenKind.Minus => unary.OverflowBehavior is
                    BoundIntegerOverflowBehavior.Checked
                    or BoundIntegerOverflowBehavior.Wrapping,
                _ => false,
            };
        }

        bool IsScalarBinaryReturn(BoundBinary binary)
        {
            if (binary.Type != function.ReturnType
                || !IsC1M1Construction2BodyNumericType(binary.Type)
                || !IsScalarNumericOperand(binary.Left, binary.Type)
                || !IsScalarNumericOperand(binary.Right, binary.Type)
                || (binary.Left is not BoundRead
                    && binary.Right is not BoundRead))
            {
                return false;
            }

            if (PrimitiveTypeFacts.IsFloating(binary.Type))
            {
                return binary.Operator is TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent;
            }

            if (binary.Operator is TokenKind.Ampersand
                or TokenKind.Pipe or TokenKind.Caret)
            {
                return true;
            }

            return binary.Operator switch
            {
                TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                    => binary.OverflowBehavior is
                        BoundIntegerOverflowBehavior.Checked
                        or BoundIntegerOverflowBehavior.Wrapping,
                TokenKind.Slash or TokenKind.Percent
                    => binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked,
                _ => false,
            };
        }

        static BoundExpression? GetSingleScalarReturn(
            BoundStatement statement)
        {
            if (statement is BoundBlock block)
            {
                if (!block.RetiredManagedLocals.IsEmpty
                    || block.Statements.Length != 1)
                {
                    return null;
                }

                statement = block.Statements[0];
            }

            return statement switch
            {
                BoundReturn { Value: BoundRead read } => read,
                BoundReturn { Value: BoundConstant constant } => constant,
                BoundReturn { Value: BoundBinary binary } => binary,
                BoundReturn { Value: BoundUnary unary } => unary,
                _ => null,
            };
        }
    }

    private static bool TryGetC1M1Construction2DirectFieldRead(
        BoundFunction function, out int fieldIndex)
    {
        fieldIndex = -1;
        if (function.IsStatic || function.ParameterCount != 1
            || function.Variables.Length != 1
            || function.Variables[0].IsMutable
            || function.ReturnType == PrimitiveType.Void)
        {
            return false;
        }

        BoundFieldRead? returnedField = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedField is null
            || returnedField.Receiver is not BoundRead { Variable: 0 }
            || returnedField.Type != function.ReturnType)
        {
            return false;
        }

        fieldIndex = returnedField.Field;
        return fieldIndex >= 0;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedField is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundFieldRead fieldRead
                })
                {
                    returnedField = fieldRead;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static C1M1Construction2NestedFieldRead?
        TryGetC1M1Construction2NestedFieldRead(
            BoundProgram program, BoundFunction function, int classIndex,
            IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
                sourceStructDeclarations,
            IReadOnlyDictionary<int, C1M1ImportedStructImportPlan>
                typeImportPlanByStructure,
            IReadOnlyDictionary<BoundGenericNominalTypeIdentity, int>
                standardClassIndexByIdentity,
            IReadOnlyDictionary<int, C1M1ImportedClassImportPlan>
                typeImportPlanByStandardClass,
            IReadOnlyDictionary<(int ClassIndex, int FieldIndex), int>
                fieldDefRowByClassAndIndex,
            IReadOnlyDictionary<(int StandardClassIndex, int FieldIndex),
                int> fieldDefRowByStandardClassAndIndex,
            IReadOnlyDictionary<(int StructureIndex, int FieldIndex), int>
                fieldDefRowByStructureAndIndex)
    {
        if (function.IsStatic
            || function.ParameterCount != function.Variables.Length
            || function.ParameterCount is < 1 or > 2
            || function.Variables[0].IsMutable
            || function.ReturnType == PrimitiveType.Void
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return null;
        }

        BoundFieldRead? returnedField = null;
        var pendingBlocks = new Stack<BoundBlock>();
        pendingBlocks.Push(function.Body);
        var visits = 0;
        while (pendingBlocks.Count != 0)
        {
            var block = pendingBlocks.Pop();
            if (++visits > SemanticVerifier.MaximumAnalysisVisits
                || !block.RetiredManagedLocals.IsEmpty)
            {
                return null;
            }

            foreach (var statement in block.Statements)
            {
                if (++visits > SemanticVerifier.MaximumAnalysisVisits
                    || returnedField is not null)
                {
                    return null;
                }

                if (statement is BoundBlock nested)
                {
                    pendingBlocks.Push(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundFieldRead fieldRead
                })
                {
                    returnedField = fieldRead;
                }
                else
                {
                    return null;
                }
            }
        }

        if (returnedField is null
            || returnedField.Type != function.ReturnType
            || !TryGetC1M1SourceScalarTypeSpecReference(
                function.ReturnType, out _))
        {
            return null;
        }

        var fieldPath = new List<BoundFieldRead>();
        BoundExpression root = returnedField;
        while (root is BoundFieldRead fieldRead)
        {
            if (fieldPath.Count
                >= SemanticVerifier.MaximumAnalysisVisits)
            {
                return null;
            }

            fieldPath.Add(fieldRead);
            root = fieldRead.Receiver;
        }

        if (root is not BoundRead receiver
            || (uint)receiver.Variable
                >= (uint)function.ParameterCount
            || receiver.Type != function.Variables[receiver.Variable].Type)
        {
            return null;
        }

        var baseVariableIndex = receiver.Variable;
        var standardClassIndex = -1;
        if (baseVariableIndex == 0)
        {
            if (function.ParameterCount != 1 || fieldPath.Count < 2
                || ManagedClassTypes.IsOptional(receiver.Type)
                || !ManagedClassTypes.IsClass(receiver.Type)
                || ManagedClassTypes.ClassIndex(receiver.Type) != classIndex)
            {
                return null;
            }
        }
        else if (baseVariableIndex == 1
            && function.ParameterCount == 2
            && TryGetC1M1ImportedClassFieldType(receiver.Type, program,
                standardClassIndexByIdentity, out standardClassIndex,
                out _))
        {
            if (fieldPath.Count == 0)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        fieldPath.Reverse();
        var fieldDefRows = ImmutableArray.CreateBuilder<int>(
            fieldPath.Count);
        var fieldTypes = ImmutableArray.CreateBuilder<PrimitiveType>(
            fieldPath.Count);
        for (var index = 0; index < fieldPath.Count; index++)
        {
            var fieldRead = fieldPath[index];
            var ownerType = fieldRead.Receiver.Type;
            int fieldDefRow;
            PrimitiveType fieldType;
            if (index == 0 && baseVariableIndex == 0)
            {
                if (!ManagedClassTypes.IsClass(ownerType)
                    || ManagedClassTypes.IsOptional(ownerType)
                    || ManagedClassTypes.ClassIndex(ownerType)
                        != classIndex
                    || (uint)fieldRead.Field
                        >= (uint)program.Classes[classIndex].Fields.Length
                    || !fieldDefRowByClassAndIndex.TryGetValue((
                        classIndex, fieldRead.Field), out fieldDefRow))
                {
                    return null;
                }

                fieldType
                    = program.Classes[classIndex].Fields[fieldRead.Field]
                        .Type;
            }
            else if (index == 0)
            {
                var classFields
                    = program.StandardClassDeclarations[standardClassIndex]
                        .Declaration.Fields;
                if (!TryGetC1M1ImportedClassFieldType(ownerType, program,
                        standardClassIndexByIdentity,
                        out var fieldOwnerStandardClassIndex, out _)
                    || fieldOwnerStandardClassIndex != standardClassIndex
                    || (uint)fieldRead.Field >= (uint)classFields.Length
                    || !typeImportPlanByStandardClass.TryGetValue(
                        standardClassIndex, out var importPlan)
                    || !IsC1M1ImportedClassFieldImportable(
                        program.StandardClassDeclarations[standardClassIndex]
                            .Declaration,
                        classFields[fieldRead.Field], importPlan)
                    || !fieldDefRowByStandardClassAndIndex.TryGetValue((
                        standardClassIndex, fieldRead.Field),
                        out fieldDefRow))
                {
                    return null;
                }

                fieldType = classFields[fieldRead.Field].Type;
            }
            else
            {
                if (!TryGetC1M1SourceStructFieldType(ownerType,
                        sourceStructDeclarations,
                        out var structureIndex)
                    || (uint)structureIndex
                        >= (uint)program.Structures.Length
                    || (uint)fieldRead.Field
                        >= (uint)program.Structures[structureIndex]
                            .Fields.Length
                    || !sourceStructDeclarations.TryGetValue(
                        structureIndex, out var declaration)
                    || !IsC1M1SourceStructLayoutSupported(
                        program.Structures[structureIndex], declaration)
                    || (program.Structures[structureIndex]
                            .StandardTypeOrigin is not null
                        && (!typeImportPlanByStructure.TryGetValue(
                                structureIndex, out var importPlan)
                            || !IsC1M1ImportedStructFieldImportable(
                                program.Structures[structureIndex],
                                program.Structures[structureIndex]
                                    .Fields[fieldRead.Field],
                                importPlan)))
                    || !fieldDefRowByStructureAndIndex.TryGetValue((
                        structureIndex, fieldRead.Field),
                        out fieldDefRow))
                {
                    return null;
                }

                fieldType = program.Structures[structureIndex]
                    .Fields[fieldRead.Field].Type;
            }

            if (fieldRead.Type != fieldType)
            {
                return null;
            }

            if (index + 1 < fieldPath.Count
                && (!TryGetC1M1SourceStructFieldType(fieldType,
                        sourceStructDeclarations,
                        out var nestedStructureIndex)
                    || (uint)nestedStructureIndex
                        >= (uint)program.Structures.Length
                    || !sourceStructDeclarations.TryGetValue(
                        nestedStructureIndex, out var nestedDeclaration)
                    || !IsC1M1SourceStructLayoutSupported(
                        program.Structures[nestedStructureIndex],
                        nestedDeclaration)
                    || fieldPath[index + 1].Receiver.Type != fieldType))
            {
                return null;
            }

            fieldDefRows.Add(fieldDefRow);
            fieldTypes.Add(fieldType);
        }

        return new C1M1Construction2NestedFieldRead(
            fieldDefRows.MoveToImmutable(),
            fieldTypes.MoveToImmutable(), baseVariableIndex);
    }

    private static bool TryGetC1M1Construction2DirectFieldWrite(
        BoundFunction function, out int fieldIndex)
    {
        fieldIndex = -1;
        if (function.IsStatic || function.ReturnType != PrimitiveType.Void
            || function.ParameterCount != 2
            || function.Variables.Length != 2
            || !function.Variables[0].IsMutable)
        {
            return false;
        }

        BoundFieldAssignment? assignment = null;
        var returned = false;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || assignment is null || !returned
            || assignment.Variable != 0
            || !assignment.NestedFields.IsEmpty
            || assignment.Value is not BoundRead value
            || value.Variable != 1
            || value.Type != function.Variables[1].Type)
        {
            return false;
        }

        fieldIndex = assignment.Field;
        return fieldIndex >= 0;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returned)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundFieldAssignment fieldWrite
                    && assignment is null)
                {
                    assignment = fieldWrite;
                }
                else if (statement is BoundReturn { Value: null }
                    && assignment is not null)
                {
                    returned = true;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static C1M1Construction2NestedFieldWrite?
        TryGetC1M1Construction2ImportedClassFieldWrite(
            BoundProgram program, BoundFunction function,
            IReadOnlyDictionary<BoundGenericNominalTypeIdentity, int>
                standardClassIndexByIdentity,
            IReadOnlyDictionary<int, C1M1ImportedClassImportPlan>
                typeImportPlanByStandardClass,
            IReadOnlyDictionary<(int StandardClassIndex, int FieldIndex),
                int> fieldDefRowByStandardClassAndIndex)
    {
        if (!function.IsStatic || function.ReturnType != PrimitiveType.Void
            || function.ParameterCount is < 1 or > 2
            || function.Variables.Length != function.ParameterCount
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return null;
        }

        BoundFieldAssignment? assignment = null;
        var returned = false;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || assignment is null
            || assignment.Variable != 0
            || !assignment.NestedFields.IsEmpty
            || assignment.NestedFields.IsDefault
            || assignment.IsUnsafeAuthorized
            || !returned
            || !TryGetC1M1ImportedClassFieldType(
                function.Variables[0].Type, program,
                standardClassIndexByIdentity,
                out var standardClassIndex, out var instanceIndex)
            || !typeImportPlanByStandardClass.TryGetValue(
                standardClassIndex, out var importPlan))
        {
            return null;
        }

        if (assignment.Value is BoundRead read)
        {
            if (function.ParameterCount != 2 || read.Variable != 1
                || read.Type != function.Variables[1].Type)
            {
                return null;
            }
        }
        else if (function.ParameterCount != 1
            || assignment.Value is not BoundConstant)
        {
            return null;
        }

        var classDeclaration
            = program.StandardClassDeclarations[standardClassIndex]
                .Declaration;
        var classTypeInstance
            = program.GenericNominalTypeInstances[instanceIndex];
        if ((classTypeInstance.Qualifiers & 0x01) == 0
            || (classDeclaration.TypeDefFlags & 0x80) == 0
            || (uint)assignment.Field >= (uint)classDeclaration.Fields.Length)
        {
            return null;
        }

        var field = classDeclaration.Fields[assignment.Field];
        if ((field.Flags & 3) != 2 || (field.Flags & 0x08) != 0
            || (field.Flags & 0x04) == 0
            || field.SelectedExportKeys.IsEmpty
            || field.Type != assignment.Value.Type
            || !TryGetC1M1SourceScalarTypeSpecReference(field.Type, out _)
            || !IsC1M1ImportedClassFieldImportable(classDeclaration,
                field, importPlan)
            || !fieldDefRowByStandardClassAndIndex.TryGetValue((
                standardClassIndex, assignment.Field), out var fieldDefRow))
        {
            return null;
        }

        return new C1M1Construction2NestedFieldWrite(
            [fieldDefRow], [field.Type], 0, assignment.Value);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returned)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundFieldAssignment fieldWrite
                    && assignment is null)
                {
                    assignment = fieldWrite;
                }
                else if (statement is BoundReturn { Value: null }
                    && assignment is not null)
                {
                    returned = true;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static C1M1Construction2NestedFieldWrite?
        TryGetC1M1Construction2NestedFieldWrite(
            BoundProgram program, BoundFunction function, int classIndex,
            IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
                sourceStructDeclarations,
            IReadOnlyDictionary<int, C1M1ImportedStructImportPlan>
                typeImportPlanByStructure,
            IReadOnlyDictionary<(int ClassIndex, int FieldIndex), int>
                fieldDefRowByClassAndIndex,
            IReadOnlyDictionary<(int StructureIndex, int FieldIndex), int>
                fieldDefRowByStructureAndIndex)
    {
        if (function.IsStatic || function.ReturnType != PrimitiveType.Void
            || function.ParameterCount != 2
            || function.Variables.Length != 2
            || !function.Variables[0].IsMutable
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return null;
        }

        BoundFieldAssignment? assignment = null;
        var returned = false;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || assignment is null
            || assignment.Variable != 0
            || assignment.NestedFields.IsDefaultOrEmpty
            || assignment.IsUnsafeAuthorized
            || assignment.Value is not BoundRead value
            || value.Variable != 1
            || value.Type != function.Variables[1].Type
            || !returned)
        {
            return null;
        }

        var fieldCount = checked(assignment.NestedFields.Length + 1);
        if (fieldCount > SemanticVerifier.MaximumAnalysisVisits
            || !ManagedClassTypes.IsClass(function.Variables[0].Type)
            || ManagedClassTypes.IsOptional(function.Variables[0].Type)
            || ManagedClassTypes.ClassIndex(function.Variables[0].Type)
                != classIndex)
        {
            return null;
        }

        var fieldDefRows = ImmutableArray.CreateBuilder<int>(fieldCount);
        var fieldTypes
            = ImmutableArray.CreateBuilder<PrimitiveType>(fieldCount);
        var ownerType = function.Variables[0].Type;
        for (var index = 0; index < fieldCount; index++)
        {
            var fieldIndex = index == 0 ? assignment.Field
                : assignment.NestedFields[index - 1];
            int fieldDefRow;
            BoundStructField field;
            if (index == 0)
            {
                if ((uint)fieldIndex
                        >= (uint)program.Classes[classIndex].Fields.Length
                    || !fieldDefRowByClassAndIndex.TryGetValue((
                        classIndex, fieldIndex), out fieldDefRow))
                {
                    return null;
                }

                field = program.Classes[classIndex].Fields[fieldIndex];
            }
            else
            {
                if (!TryGetC1M1SourceStructFieldType(ownerType,
                        sourceStructDeclarations,
                        out var structureIndex)
                    || (uint)structureIndex
                        >= (uint)program.Structures.Length
                    || (uint)fieldIndex
                        >= (uint)program.Structures[structureIndex]
                            .Fields.Length
                    || !sourceStructDeclarations.TryGetValue(
                        structureIndex, out var declaration)
                    || !IsC1M1SourceStructLayoutSupported(
                        program.Structures[structureIndex], declaration)
                    || (program.Structures[structureIndex]
                            .StandardTypeOrigin is not null
                        && (!typeImportPlanByStructure.TryGetValue(
                                structureIndex, out var importPlan)
                            || !IsC1M1ImportedStructFieldImportable(
                                program.Structures[structureIndex],
                                program.Structures[structureIndex]
                                    .Fields[fieldIndex],
                                importPlan)))
                    || !fieldDefRowByStructureAndIndex.TryGetValue((
                        structureIndex, fieldIndex), out fieldDefRow))
                {
                    return null;
                }

                field = program.Structures[structureIndex]
                    .Fields[fieldIndex];
            }

            if (field is null || !field.IsMutable
                || field.RequiresUnsafeCaller)
            {
                return null;
            }

            var fieldType = field.Type;
            if (index + 1 < fieldCount
                && (!TryGetC1M1SourceStructFieldType(fieldType,
                        sourceStructDeclarations,
                        out var nestedStructureIndex)
                    || (uint)nestedStructureIndex
                        >= (uint)program.Structures.Length
                    || !sourceStructDeclarations.TryGetValue(
                        nestedStructureIndex, out var nestedDeclaration)
                    || !IsC1M1SourceStructLayoutSupported(
                        program.Structures[nestedStructureIndex],
                        nestedDeclaration)))
            {
                return null;
            }

            fieldDefRows.Add(fieldDefRow);
            fieldTypes.Add(fieldType);
            ownerType = fieldType;
        }

        if (fieldTypes[^1] != value.Type
            || !TryGetC1M1SourceScalarTypeSpecReference(value.Type, out _))
        {
            return null;
        }

        return new C1M1Construction2NestedFieldWrite(
            fieldDefRows.MoveToImmutable(),
            fieldTypes.MoveToImmutable(), 0, value);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returned)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundFieldAssignment fieldWrite
                    && assignment is null)
                {
                    assignment = fieldWrite;
                }
                else if (statement is BoundReturn { Value: null }
                    && assignment is not null)
                {
                    returned = true;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static bool TryGetC1M1Construction2IdentityForwarder(
        BoundFunction function, ImmutableArray<BoundFunction> functions,
        out int targetFunctionIndex)
    {
        targetFunctionIndex = -1;
        if (function.ReturnType == PrimitiveType.Void
            || function.ParameterCount != function.Variables.Length
            || (!function.IsStatic && (function.ParameterCount == 0
                || function.Variables[0].IsMutable)))
        {
            return false;
        }

        BoundCall? returnedCall = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedCall is null
            || returnedCall.Type != function.ReturnType
            || returnedCall.Arguments.Length != function.ParameterCount
            || !returnedCall.DefaultedArguments.IsDefaultOrEmpty
            || returnedCall.InterfaceCallIdentity is not null
            || returnedCall.GenericMethodCall is not null
            || returnedCall.ImportedGenericMethodCall is not null
            || (uint)returnedCall.Function >= (uint)functions.Length)
        {
            return false;
        }

        var target = functions[returnedCall.Function];
        if (target.IsStatic != function.IsStatic || target.IsExternal
            || target.IsClassConstructor || target.IsConstructor
            || target.IsCleanup || target.IsOperator
            || target.ModuleImport is not null
            || target.GenericMethodInstantiation is not null
            || !StringComparer.Ordinal.Equals(target.DeclaringStruct,
                function.DeclaringStruct)
            || target.ReturnType != function.ReturnType
            || target.ParameterCount != function.ParameterCount
            || target.Variables.Length < target.ParameterCount)
        {
            return false;
        }

        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var source = function.Variables[parameter];
            var targetParameter = target.Variables[parameter];
            if (returnedCall.ParameterIndex(parameter) != parameter
                || returnedCall.ImplicitReferenceTemporary(parameter) != -1
                || returnedCall.Arguments[parameter]
                    is not BoundRead read)
            {
                return false;
            }

            if (read.Variable != parameter
                || read.Type != source.Type
                || source.Type != targetParameter.Type
                || source.IsMutable != targetParameter.IsMutable
                || source.IsOut != targetParameter.IsOut
                || source.IsInit != targetParameter.IsInit
                || source.IsImplicitRef != targetParameter.IsImplicitRef
                || source.IsInitializationLocal
                    != targetParameter.IsInitializationLocal)
            {
                return false;
            }
        }

        targetFunctionIndex = returnedCall.Function;
        return true;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedCall is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundCall call
                })
                {
                    returnedCall = call;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static bool TryGetC1M1Construction2UnitIdentityForwarder(
        BoundFunction function, ImmutableArray<BoundFunction> functions,
        out int targetFunctionIndex)
    {
        targetFunctionIndex = -1;
        if (function.ReturnType != PrimitiveType.Void
            || function.ParameterCount != function.Variables.Length
            || (!function.IsStatic && (function.ParameterCount == 0
                || function.Variables[0].IsMutable))
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        BoundCall? call = null;
        if (function.Body.Statements is [
                BoundExpressionStatement {
                    Expression: BoundCall singleCall,
                },
            ])
        {
            call = singleCall;
        }
        else if (function.Body.Statements is [
                    BoundExpressionStatement {
                        Expression: BoundCall callBeforeUnitReturn,
                    },
                    BoundReturn { Value: null },
                ])
        {
            call = callBeforeUnitReturn;
        }

        if (call is null || call.Type != PrimitiveType.Void
            || call.Arguments.Length != function.ParameterCount
            || !call.DefaultedArguments.IsDefaultOrEmpty
            || call.InterfaceCallIdentity is not null
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || (uint)call.Function >= (uint)functions.Length)
        {
            return false;
        }

        var target = functions[call.Function];
        if (target.IsStatic != function.IsStatic || target.IsExternal
            || target.IsClassConstructor || target.IsConstructor
            || target.IsCleanup || target.IsOperator
            || target.ModuleImport is not null
            || target.GenericMethodInstantiation is not null
            || !StringComparer.Ordinal.Equals(target.DeclaringStruct,
                function.DeclaringStruct)
            || target.ReturnType != PrimitiveType.Void
            || target.ParameterCount != function.ParameterCount
            || target.Variables.Length < target.ParameterCount)
        {
            return false;
        }

        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var source = function.Variables[parameter];
            var targetParameter = target.Variables[parameter];
            if (call.ParameterIndex(parameter) != parameter
                || call.ImplicitReferenceTemporary(parameter) != -1
                || call.Arguments[parameter] is not BoundRead read
                || read.Variable != parameter
                || read.Type != source.Type
                || source.Type != targetParameter.Type
                || source.IsMutable != targetParameter.IsMutable
                || source.IsOut != targetParameter.IsOut
                || source.IsInit != targetParameter.IsInit
                || source.IsImplicitRef != targetParameter.IsImplicitRef
                || source.IsInitializationLocal
                    != targetParameter.IsInitializationLocal)
            {
                return false;
            }
        }

        targetFunctionIndex = call.Function;
        return true;
    }

    private static BoundBinary?
        TryGetC1M1Construction2DirectBooleanComparison(
        BoundFunction function)
    {
        if (function.IsStatic || function.ReturnType != PrimitiveType.Bool
            || function.ParameterCount != 3
            || function.ParameterCount != function.Variables.Length
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != PrimitiveType.Bool
            || function.Variables[2].Type != PrimitiveType.Bool
            || function.Variables.Skip(1).Any(static variable =>
                variable.IsMutable || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            return null;
        }

        BoundBinary? returnedBinary = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedBinary is null
            || returnedBinary.Type != PrimitiveType.Bool
            || returnedBinary.Operator is not (TokenKind.EqualsEquals
                or TokenKind.BangEquals)
            || returnedBinary.Left is not BoundRead left
            || returnedBinary.Right is not BoundRead right
            || left.Type != PrimitiveType.Bool
            || right.Type != PrimitiveType.Bool
            || left.Variable is not (1 or 2)
            || right.Variable is not (1 or 2))
        {
            return null;
        }

        return returnedBinary;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedBinary is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundBinary binary
                })
                {
                    returnedBinary = binary;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static BoundBinary? TryGetC1M1Construction2DirectNumericBinary(
        BoundFunction function)
    {
        if (function.IsStatic || function.ParameterCount < 2
            || function.ParameterCount != function.Variables.Length
            || function.Variables[0].IsMutable
            || function.Variables.Skip(1).Any(static variable =>
                variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            return null;
        }

        BoundBinary? returnedBinary = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedBinary is null)
        {
            return null;
        }

        var leftRead = returnedBinary.Left as BoundRead;
        var rightRead = returnedBinary.Right as BoundRead;
        var leftConstant = returnedBinary.Left as BoundConstant;
        var rightConstant = returnedBinary.Right as BoundConstant;
        var leftReadIsValid = IsValidFormalRead(leftRead);
        var rightReadIsValid = IsValidFormalRead(rightRead);
        var leftConstantIsValid = IsValidConstant(leftConstant);
        var rightConstantIsValid = IsValidConstant(rightConstant);
        var leftType = leftRead?.Type ?? leftConstant?.Type;
        var rightType = rightRead?.Type ?? rightConstant?.Type;
        var hasSameNumericType
            = leftType.HasValue && rightType == leftType
                && IsC1M1Construction2BodyNumericType(leftType.Value);
        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && returnedBinary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    returnedBinary.Operator)
                && hasSameNumericType
                && (leftReadIsValid || leftConstantIsValid)
                && (rightReadIsValid || rightConstantIsValid)
                && (leftReadIsValid || rightReadIsValid);
        var isIntegerValueOperation
            = function.ReturnType == returnedBinary.Type
                && leftType.HasValue && returnedBinary.Type == leftType.Value
                && hasSameNumericType
                && IsC1M1Construction2BodyIntegerType(leftType.Value)
                && (returnedBinary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        var isFloatingValueOperation
            = function.ReturnType == returnedBinary.Type
                && leftType.HasValue && returnedBinary.Type == leftType.Value
                && hasSameNumericType
                && PrimitiveTypeFacts.IsFloating(leftType.Value)
                && (returnedBinary.Operator is TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        return isComparison || isIntegerValueOperation
                || isFloatingValueOperation
            ? returnedBinary : null;

        bool IsValidFormalRead(BoundRead? read)
            => read is not null
                && IsC1M1Construction2BodyNumericType(read.Type)
                && read.Variable > 0
                && read.Variable < function.ParameterCount
                && function.Variables[read.Variable].Type
                    == read.Type;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.RepresentationType == constant.Type
                && constant.Bits.IsCanonicalFor(constant.Type);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedBinary is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundBinary binary
                })
                {
                    returnedBinary = binary;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static BoundUnary? TryGetC1M1Construction2DirectNumericUnary(
        BoundFunction function)
    {
        if (function.IsStatic || function.ParameterCount != 2
            || function.ParameterCount != function.Variables.Length
            || function.Variables[0].IsMutable
            || !IsC1M1Construction2BodyNumericType(
                function.Variables[1].Type)
            || function.Variables[1].IsMutable
            || function.Variables[1].IsOut || function.Variables[1].IsInit
            || function.Variables[1].IsImplicitRef
            || function.Variables[1].IsInitializationLocal)
        {
            return null;
        }

        BoundUnary? returnedUnary = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedUnary is null
            || returnedUnary.Type != function.ReturnType
            || !IsC1M1Construction2BodyNumericType(returnedUnary.Type)
            || (PrimitiveTypeFacts.IsFloating(returnedUnary.Type)
                ? returnedUnary.Operator != TokenKind.Minus
                : returnedUnary.Operator is not (TokenKind.Minus
                    or TokenKind.Tilde))
            || returnedUnary.Operand is not BoundRead operand
            || operand.Type != returnedUnary.Type
            || operand.Variable != 1)
        {
            return null;
        }

        return returnedUnary;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedUnary is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundUnary unary
                })
                {
                    returnedUnary = unary;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static BoundBinary?
        TryGetC1M1Construction2DirectCallNumericBinary(
        BoundFunction function, ImmutableArray<BoundFunction> functions)
    {
        var callerReceiverCount = function.IsStatic ? 0 : 1;
        if (function.ParameterCount != callerReceiverCount + 2
            || function.ParameterCount != function.Variables.Length
            || (!function.IsStatic && function.Variables[0].IsMutable))
        {
            return null;
        }

        var operationType = function.Variables[callerReceiverCount].Type;
        if (!IsC1M1Construction2BodyNumericType(operationType)
            || function.Variables.Skip(callerReceiverCount).Any(
                variable => variable.Type != operationType
                || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            return null;
        }

        BoundBinary? returnedBinary = null;
        var isSupported = true;
        Visit(function.Body);
        var rightRead = returnedBinary?.Right as BoundRead;
        var rightConstant = returnedBinary?.Right as BoundConstant;
        if (!isSupported || returnedBinary is null
            || returnedBinary.Left is not BoundCall call
            || (rightRead is null && !IsValidConstant(rightConstant))
            || (rightRead is not null
                && (rightRead.Type != operationType
                    || rightRead.Variable < callerReceiverCount
                    || rightRead.Variable >= function.ParameterCount))
            || (rightConstant is not null
                && rightConstant.Type != operationType)
            || call.Type != operationType
            || !call.DefaultedArguments.IsDefaultOrEmpty
            || call.InterfaceCallIdentity is not null
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || (uint)call.Function >= (uint)functions.Length)
        {
            return null;
        }

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && returnedBinary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    returnedBinary.Operator);
        var isIntegerValueOperation
            = function.ReturnType == operationType
                && returnedBinary.Type == operationType
                && IsC1M1Construction2BodyIntegerType(operationType)
                && (returnedBinary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret
                    or TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent);
        var isFloatingValueOperation
            = function.ReturnType == operationType
                && returnedBinary.Type == operationType
                && PrimitiveTypeFacts.IsFloating(operationType)
                && (returnedBinary.Operator is TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent);
        if (!isComparison && !isIntegerValueOperation
            && !isFloatingValueOperation)
        {
            return null;
        }

        var target = functions[call.Function];
        var targetReceiverCount = target.IsStatic ? 0 : 1;
        if (target.IsExternal
            || target.IsClassConstructor || target.IsConstructor
            || target.IsCleanup || target.IsOperator
            || target.ModuleImport is not null
            || target.GenericMethodInstantiation is not null
            || !StringComparer.Ordinal.Equals(target.DeclaringStruct,
                function.DeclaringStruct)
            || target.ReturnType != operationType
            || target.ParameterCount != targetReceiverCount + 2
            || call.Arguments.Length != target.ParameterCount
            || target.Variables.Length < target.ParameterCount
            || target.Variables.Skip(targetReceiverCount).Take(2).Any(
                variable => variable.Type != operationType
                    || variable.IsMutable || variable.IsOut
                    || variable.IsInit || variable.IsImplicitRef
                    || variable.IsInitializationLocal))
        {
            return null;
        }

        for (var parameter = 0;
            parameter < target.ParameterCount; parameter++)
        {
            var sourceVariable = target.IsStatic
                ? callerReceiverCount + parameter
                : parameter == 0 ? 0
                    : callerReceiverCount + parameter - 1;
            var source = function.Variables[sourceVariable];
            var targetParameter = target.Variables[parameter];
            if (call.ParameterIndex(parameter) != parameter
                || call.ImplicitReferenceTemporary(parameter) != -1
                || call.Arguments[parameter] is not BoundRead argument
                || argument.Variable != sourceVariable
                || argument.Type != source.Type
                || source.Type != targetParameter.Type
                || source.IsMutable != targetParameter.IsMutable
                || source.IsOut != targetParameter.IsOut
                || source.IsInit != targetParameter.IsInit
                || source.IsImplicitRef != targetParameter.IsImplicitRef
                || source.IsInitializationLocal
                    != targetParameter.IsInitializationLocal)
            {
                return null;
            }
        }

        return returnedBinary;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && constant.Type == constant.RepresentationType
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.Bits.IsCanonicalFor(constant.Type);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedBinary is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundBinary binary
                })
                {
                    returnedBinary = binary;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static bool TryGetC1M1Construction2LocalCopyReturn(
        BoundFunction function)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var formalVariable = receiverCount;
        var localVariable = receiverCount + 1;
        if (function.ParameterCount != receiverCount + 1
            || function.Variables.Length != receiverCount + 2
            || function.Body.RetiredManagedLocals.Length != 0
            || function.Body.Statements is not [
                BoundLocal local,
                BoundReturn { Value: BoundRead returnedRead },
            ])
        {
            return false;
        }

        var operationType = function.Variables[formalVariable].Type;
        return IsC1M1Construction2BodyNumericType(operationType)
            && function.ReturnType == operationType
            && (function.IsStatic || !function.Variables[0].IsMutable)
            && !function.Variables[formalVariable].IsMutable
            && !function.Variables[formalVariable].IsOut
            && !function.Variables[formalVariable].IsInit
            && !function.Variables[formalVariable].IsImplicitRef
            && !function.Variables[formalVariable].IsInitializationLocal
            && function.Variables[localVariable].Type
                == operationType
            && !function.Variables[localVariable].IsMutable
            && !function.Variables[localVariable].IsOut
            && !function.Variables[localVariable].IsInit
            && !function.Variables[localVariable].IsImplicitRef
            && !function.Variables[localVariable].IsInitializationLocal
            && local.Variable == localVariable
            && local.Initializer is BoundRead initialRead
            && initialRead.Type == operationType
            && initialRead.Variable == formalVariable
            && returnedRead.Type == operationType
            && returnedRead.Variable == localVariable;
    }

    private static bool TryGetC1M1Construction2LocalNumericBinaryReturn(
        BoundFunction function)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localCount = function.Variables.Length
            - function.ParameterCount;
        if (function.ParameterCount < receiverCount + 1
            || localCount <= 0
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != localCount + 1)
        {
            return false;
        }

        var operationType = function.Variables[receiverCount].Type;
        if (!IsC1M1Construction2BodyNumericType(operationType)
            || !function.Variables.Skip(receiverCount).All(
                variable => variable.Type == operationType
                    && !variable.IsMutable && !variable.IsOut
                    && !variable.IsInit && !variable.IsImplicitRef
                    && !variable.IsInitializationLocal))
        {
            return false;
        }

        for (var localOrdinal = 0;
            localOrdinal < localCount; localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            if (function.Body.Statements[localOrdinal] is not BoundLocal local
                || local.Variable != localVariable
                || local.Initializer is not BoundRead initializer
                || initializer.Type != operationType
                || initializer.Variable < 0
                || initializer.Variable >= localVariable
                || function.Variables[initializer.Variable].Type
                    != operationType)
            {
                return false;
            }
        }

        if (function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundBinary binary,
            })
        {
            return false;
        }

        var leftRead = binary.Left as BoundRead;
        var rightRead = binary.Right as BoundRead;
        var leftConstant = binary.Left as BoundConstant;
        var rightConstant = binary.Right as BoundConstant;
        var leftReadIsValid = IsValidHomeRead(leftRead);
        var rightReadIsValid = IsValidHomeRead(rightRead);
        var leftConstantIsValid = IsValidConstant(leftConstant);
        var rightConstantIsValid = IsValidConstant(rightConstant);
        var leftType = leftRead?.Type ?? leftConstant?.Type;
        var rightType = rightRead?.Type ?? rightConstant?.Type;
        var hasSameNumericType
            = leftType == operationType && rightType == leftType;
        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    binary.Operator)
                && hasSameNumericType
                && (leftReadIsValid || leftConstantIsValid)
                && (rightReadIsValid || rightConstantIsValid)
                && (leftReadIsValid || rightReadIsValid);
        var isIntegerValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && hasSameNumericType
                && IsC1M1Construction2BodyIntegerType(operationType)
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        var isFloatingValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && hasSameNumericType
                && PrimitiveTypeFacts.IsFloating(operationType)
                && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        return isComparison || isIntegerValueOperation
            || isFloatingValueOperation;

        bool IsValidHomeRead(BoundRead? read)
            => read is not null && read.Type == operationType
                && IsC1M1Construction2BodyNumericType(read.Type)
                && read.Variable >= receiverCount
                && read.Variable < function.Variables.Length
                && function.Variables[read.Variable].Type
                    == operationType;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.RepresentationType == constant.Type
                && constant.Bits.IsCanonicalFor(constant.Type);
    }

    private static bool TryGetC1M1Construction2DirectParameterReturn(
        BoundFunction function, out int returnHomeIndex)
    {
        returnHomeIndex = -1;
        if (function.ReturnType == PrimitiveType.Void
            || function.ParameterCount != function.Variables.Length
            || function.ParameterCount < 2
            || function.Variables[0].IsMutable)
        {
            return false;
        }

        BoundRead? returnedRead = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedRead is null
            || returnedRead.Variable <= 0
            || returnedRead.Variable >= function.ParameterCount
            || returnedRead.Type != function.ReturnType
            || function.Variables[returnedRead.Variable].Type
                != function.ReturnType)
        {
            return false;
        }

        returnHomeIndex = returnedRead.Variable;
        return true;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedRead is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn { Value: BoundRead read })
                {
                    returnedRead = read;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static void RequireC1M1Construction2NoOpUnitBody(
        BoundBlock body)
    {
        var returned = false;
        Visit(body);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                throw Unsupported(
                    "The bounded Construction-2 instance method producer cannot discard retired-local cleanup.");
            }

            foreach (var statement in block.Statements)
            {
                if (returned)
                {
                    throw Unsupported(
                        "A Construction-2 no-op method cannot continue after its unit return.");
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn { Value: null })
                {
                    returned = true;
                }
                else
                {
                    throw Unsupported(
                        "The bounded Construction-2 instance method producer supports only an empty unit body.");
                }
            }
        }
    }

    private static bool TryGetC1M1SourceClassFieldType(
        PrimitiveType type, out int classIndex, out bool isWritable,
        out bool isOptional)
    {
        isOptional = ManagedClassTypes.IsOptional(type);
        var requiredType = isOptional
            ? ManagedClassTypes.Required(type) : type;
        if (!ManagedClassTypes.IsClass(requiredType))
        {
            classIndex = -1;
            isWritable = false;
            return false;
        }

        classIndex = ManagedClassTypes.ClassIndex(requiredType);
        isWritable = ManagedClassTypes.IsWritable(type);
        return true;
    }

    private static bool TryGetC1M1ImportedClassFieldType(
        PrimitiveType type, BoundProgram program,
        IReadOnlyDictionary<BoundGenericNominalTypeIdentity, int>
            standardClassIndexByIdentity,
        out int classIndex, out int instanceIndex)
    {
        if (!BoundGenericNominalTypeTypes.IsType(type))
        {
            classIndex = -1;
            instanceIndex = -1;
            return false;
        }

        instanceIndex = BoundGenericNominalTypeTypes.Index(type);
        if ((uint)instanceIndex
                >= (uint)program.GenericNominalTypeInstances.Length
            || !BoundGenericNominalTypeContract.IsNominalClassType(type,
                program.GenericNominalTypeInstances,
                program.GenericNominalTypeDefinitions))
        {
            classIndex = -1;
            return false;
        }

        var instance = program.GenericNominalTypeInstances[instanceIndex];
        if (!instance.Arguments.IsEmpty
            || (instance.Qualifiers & ~0x01) != 0
            || instance.TypeSpecForm is not (0 or 0x02 or 0x03)
            || !standardClassIndexByIdentity.TryGetValue(
                instance.Definition, out classIndex))
        {
            classIndex = -1;
            instanceIndex = -1;
            return false;
        }

        return true;
    }

    private static bool TryGetC1M1SourceStructFieldType(
        PrimitiveType type,
        Dictionary<int, BoundSourceTypeDeclaration> declarations,
        out int structureIndex)
    {
        var typeValue = (int)type;
        if (typeValue is >= 100 and < BoundEnumTypes.First)
        {
            structureIndex = typeValue - 100;
            return declarations.ContainsKey(structureIndex);
        }

        structureIndex = -1;
        return false;
    }

    private static bool IsC1M1SourceStructLayoutSupported(
        BoundStruct structure)
        => structure is not null
            && !structure.Fields.IsDefault
            && structure.LayoutKind == 0
            && structure.BlittableTargetRequirement == 0
            && structure.Packing == 0
            && structure.DeclaredAlignment is null
            && !structure.IsUnique
            && !(structure.IsPrivate && structure.IsInternal)
            && structure.CleanupMethod is null
            && structure.StandardTypeOrigin is null
            && structure.SourceGenericTypeDefinitionIndex < 0;

    private static bool IsC1M1SourceStructLayoutSupported(
        BoundStruct structure, BoundSourceTypeDeclaration declaration)
    {
        if (structure.StandardTypeOrigin is not
            BoundStandardTypeOrigin origin)
        {
            return IsC1M1SourceStructLayoutSupported(structure)
                && structure == declaration.DeclarationSnapshot;
        }

        if (!structure.StandardLayoutOnly
            || origin.DefinitionId >> 24 != 0x13
            || (origin.DefinitionId & 0x00FF_FFFF) == 0
            || origin.Provider is null
            || origin.ExportKeys.IsDefaultOrEmpty
            || !IsC1M1SourceStructLayoutSupported(
                declaration.DeclarationSnapshot))
        {
            return false;
        }

        var actual = structure with {
            Fields = [.. structure.Fields.Select(static field => field with {
                StandardFieldOrigin = null,
            })],
            StandardTypeOrigin = null,
            StandardLayoutOnly = false,
        };
        return actual == declaration.DeclarationSnapshot
            && (origin.ExpectedDeclaration is null
                || origin.ExpectedDeclaration
                    == declaration.DeclarationSnapshot);
    }

    private static bool IsC1M1ImportedStructFieldImportable(
        BoundStruct owner, BoundStructField field,
        C1M1ImportedStructImportPlan importPlan)
    {
        if (owner.StandardTypeOrigin is not BoundStandardTypeOrigin)
        {
            return true;
        }

        private static Dictionary<BoundGenericNominalTypeIdentity, int>
            CreateC1M1StandardClassIndexByIdentity(BoundProgram program)
        {
            var indexes
                = new Dictionary<BoundGenericNominalTypeIdentity, int>();
            for (var index = 0;
                index < program.StandardClassDeclarations.Length; index++)
            {
                var declaration = program.StandardClassDeclarations[index];
                Add(declaration.Declaration.Identity, index);
                foreach (var expectation in declaration.Expectations)
                {
                    Add(expectation.ExpectedDeclaration.Identity, index);
                }
            }

            return indexes;

            void Add(BoundGenericNominalTypeIdentity identity, int index)
            {
                if (!indexes.TryAdd(identity, index)
                    && indexes[identity] != index)
                {
                    throw new InvalidDataException(
                        "One nominal class identity selects conflicting Standard class declarations.");
                }
            }
        }

        if (field.StandardFieldOrigin is not
            BoundStandardFieldOrigin fieldOrigin
            || fieldOrigin.Provider is null
            || !SameA2SourceTypeProviderSnapshot(fieldOrigin.Provider,
                importPlan.Provider)
            || fieldOrigin.DefinitionId >> 24 != 0x14
            || (fieldOrigin.DefinitionId & 0x00FF_FFFF) == 0
            || field.IsPrivate || field.IsInternal
            || fieldOrigin.ExportKeys.IsDefaultOrEmpty)
        {
            return false;
        }

        var exportKeys = fieldOrigin.ExportKeys.ToHashSet(
            StringComparer.Ordinal);
        if (exportKeys.Count != fieldOrigin.ExportKeys.Length
            || exportKeys.Any(static key =>
                !BoundEnumTypes.IsValidModuleDeclarationKey(key)))
        {
            return false;
        }

        if (!fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty)
        {
            var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exportKey in fieldOrigin.SelectedExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !exportKeys.Contains(exportKey)
                    || !selectedKeys.Add(exportKey))
                {
                    return false;
                }
            }
        }

        var actualField = field with {
            StandardFieldOrigin = null,
        };
        return fieldOrigin.ExpectedDeclaration is null
            || actualField == fieldOrigin.ExpectedDeclaration;
    }

    private static bool IsC1M1ImportedClassFieldImportable(
        BoundStandardClassTypeView owner,
        BoundStandardClassFieldDeclaration field,
        C1M1ImportedClassImportPlan importPlan)
    {
        if (!SameA2SourceTypeProviderSnapshot(owner.Provider,
                importPlan.Provider)
            || field.DefinitionId >> 24 != 0x14
            || (field.DefinitionId & 0x00FF_FFFF) == 0
            || field.OwnerDefinitionId != owner.Identity.DefinitionId
            || (field.Flags & 3) != 2
            || (field.Flags & 0x08) != 0
            || field.ExportKeys.IsDefaultOrEmpty)
        {
            return false;
        }

        var exportKeys = field.ExportKeys.ToHashSet(StringComparer.Ordinal);
        if (exportKeys.Count != field.ExportKeys.Length
            || exportKeys.Any(static key =>
                !BoundEnumTypes.IsValidModuleDeclarationKey(key)))
        {
            return false;
        }

        if (!field.SelectedExportKeys.IsDefaultOrEmpty)
        {
            var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exportKey in field.SelectedExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !exportKeys.Contains(exportKey)
                    || !selectedKeys.Add(exportKey))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static C1M1ImportedStructImportPlan
        GetC1M1ImportedStructImportPlan(
            BoundStandardTypeOrigin origin)
    {
        if (origin.Provider is null
            || origin.ExportKeys.IsDefaultOrEmpty)
        {
            throw Unsupported(
                "C1M1 class source emission requires an exact Standard struct TypeDef provider and ExportKey.");
        }

        var exportKeys = origin.ExportKeys.ToHashSet(StringComparer.Ordinal);
        var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        if (origin.Expectations.IsDefaultOrEmpty)
        {
            if (!origin.IsLookupRoot
                || origin.SelectedExportKeys.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "C1M1 class source emission requires an explicitly selected Standard struct TypeDef Export.");
            }

            foreach (var exportKey in origin.SelectedExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !exportKeys.Contains(exportKey)
                    || !selectedKeys.Add(exportKey))
                {
                    throw Unsupported(
                        "C1M1 class source emission requires exact selected Standard struct TypeDef ExportKeys.");
                }
            }

            return new C1M1ImportedStructImportPlan(origin.Provider,
                origin.SelectedExportKeys);
        }

        if (origin.IsLookupRoot
            || !origin.SelectedExportKeys.IsDefaultOrEmpty)
        {
            throw Unsupported(
                "C1M1 imported struct TypeDef expectations cannot be mixed with selected lookup-root exports.");
        }

        var rootSnapshot = origin.ReferenceRoot ?? origin.Provider;
        BoundSourceTypeProviderSnapshot? provider = null;
        uint providerDefinitionId = 0;
        uint expectedDefinitionId = 0;
        var exportKeyBuilder = ImmutableArray.CreateBuilder<string>();
        foreach (var expectation in origin.Expectations)
        {
            if (!SameA2SourceTypeProviderSnapshot(expectation.UsingModule,
                    rootSnapshot))
            {
                continue;
            }

            if (expectation.ImportRow <= 0
                || expectation.ExpectedDefinitionId >> 24 != 0x13
                || (expectation.ExpectedDefinitionId & 0x00FF_FFFF) == 0
                || expectation.ProviderDefinitionId >> 24 != 0x13
                || (expectation.ProviderDefinitionId & 0x00FF_FFFF) == 0
                || !StringComparer.Ordinal.Equals(
                    expectation.ProducerIdentity,
                    expectation.Provider.ModuleIdentity)
                || expectation.TargetRequirement
                    != expectation.Provider.TargetRequirement
                || expectation.RuntimePolicy
                    != expectation.Provider.RuntimePolicy
                || !BoundEnumTypes.IsValidModuleDeclarationKey(
                    expectation.ExportKey)
                || !exportKeys.Contains(expectation.ExportKey))
            {
                throw new InvalidDataException(
                    "An imported struct TypeDef has an invalid root-owned provider expectation.");
            }

            if (provider is null)
            {
                provider = expectation.Provider;
                providerDefinitionId = expectation.ProviderDefinitionId;
                expectedDefinitionId = expectation.ExpectedDefinitionId;
            }
            else if (!SameA2SourceTypeProviderSnapshot(provider,
                         expectation.Provider)
                || providerDefinitionId
                    != expectation.ProviderDefinitionId
                || expectedDefinitionId
                    != expectation.ExpectedDefinitionId)
            {
                throw Invalid(
                    "One imported struct TypeDef has conflicting root-owned provider expectations.");
            }

            if (selectedKeys.Add(expectation.ExportKey))
            {
                exportKeyBuilder.Add(expectation.ExportKey);
            }
        }

        if (provider is null
            || providerDefinitionId != origin.DefinitionId
            || !SameA2SourceTypeProviderSnapshot(provider, origin.Provider)
            || exportKeyBuilder.Count == 0)
        {
            throw Unsupported(
                "C1M1 imported struct closure requires one exact root-owned TypeDef provider binding.");
        }

        return new C1M1ImportedStructImportPlan(provider,
            exportKeyBuilder.MoveToImmutable());
    }

    private static Dictionary<int, BoundSourceTypeDeclaration>
        CreateC1M1SourceStructDeclarations(BoundProgram program)
    {
        var declarations = program.SourceTypeDeclarations.ToDictionary(
            static declaration => declaration.StructureIndex);
        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            var structure = program.Structures[structureIndex];
            if (structure.StandardTypeOrigin is null)
            {
                continue;
            }

            var separator = structure.Name.LastIndexOf('.',
                StringComparison.Ordinal);
            var namespaceName = separator < 0
                ? string.Empty : structure.Name[..separator];
            var typeName = separator < 0
                ? structure.Name : structure.Name[(separator + 1)..];
            var declarationSnapshot = structure with {
                Fields = [.. structure.Fields.Select(static field => field with {
                    StandardFieldOrigin = null,
                })],
                StandardTypeOrigin = null,
                StandardLayoutOnly = false,
            };
            if (string.IsNullOrEmpty(typeName)
                || !declarations.TryAdd(structureIndex,
                    new BoundSourceTypeDeclaration(structureIndex,
                        namespaceName, typeName,
                        structure.StandardTypeOrigin.ExpectedDeclaration
                            ?? declarationSnapshot)))
            {
                throw new InvalidDataException(
                    "A standard struct view has no unique source declaration identity.");
            }
        }

        return declarations;
    }

    private static bool HasSupportedC1M1ImportedNominalClosure(
        BoundProgram program,
        IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
            sourceStructDeclarations,
        IReadOnlyDictionary<BoundGenericNominalTypeIdentity, int>
            standardClassIndexByIdentity)
    {
        var importedStructIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            if (sourceStructDeclarations.ContainsKey(structureIndex)
                && program.Structures[structureIndex]
                    .StandardTypeOrigin is
                    BoundStandardTypeOrigin origin)
            {
                importedStructIdentities.Add(
                    new BoundGenericNominalTypeIdentity(
                        origin.Provider.ModuleIdentity,
                        origin.Provider.ModuleIdentity,
                        origin.DefinitionId));
                foreach (var expectation in origin.Expectations)
                {
                    importedStructIdentities.Add(
                        new BoundGenericNominalTypeIdentity(
                            expectation.UsingModule.ModuleIdentity,
                            expectation.UsingModule.ModuleIdentity,
                            expectation.ExpectedDefinitionId));
                    importedStructIdentities.Add(
                        new BoundGenericNominalTypeIdentity(
                            expectation.Provider.ModuleIdentity,
                            expectation.Provider.ModuleIdentity,
                            expectation.ProviderDefinitionId));
                }
            }
        }

        var standardClassIdentities
            = standardClassIndexByIdentity.Keys.ToHashSet();
        bool IsImportedNominalIdentity(
            BoundGenericNominalTypeIdentity identity)
            => identity.IsValid
                && (importedStructIdentities.Contains(identity)
                    || standardClassIdentities.Contains(identity));

        if ((!program.StandardClassDeclarations.IsEmpty
                && !BoundStandardClassDeclarationContract.IsValidTable(
                    program.StandardClassDeclarations,
                    program.GenericNominalTypeDefinitions))
            || program.StandardClassDeclarations.Any(
                static declaration =>
                    !IsC1M1ImportedClassLayoutSupported(declaration)))
        {
            return false;
        }

        foreach (var definition in program.GenericNominalTypeDefinitions)
        {
            var isImportedStruct
                = importedStructIdentities.Contains(definition.Identity);
            if (!definition.Formals.IsEmpty
                || !IsImportedNominalIdentity(definition.Identity)
                || definition.NominalKind is not (1 or 2)
                || (definition.NominalKind == 1 && !isImportedStruct)
                || (definition.NominalKind == 2 && isImportedStruct))
            {
                return false;
            }
        }

        foreach (var instance in program.GenericNominalTypeInstances)
        {
            if (!instance.Arguments.IsEmpty
                || (instance.Qualifiers & ~0x01) != 0
                || instance.TypeSpecForm is not (0 or 0x02 or 0x03)
                || !IsImportedNominalIdentity(instance.Definition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsC1M1ImportedClassLayoutSupported(
        BoundStandardClassDeclaration declaration)
    {
        if (declaration.Expectations.IsDefault
            || declaration.SelectedExportKeys.IsDefault
            || declaration.ExportKeys.IsDefaultOrEmpty
            || (declaration.Expectations.IsEmpty
                && declaration.SelectedExportKeys.IsEmpty))
        {
            return false;
        }

        var view = declaration.Declaration;
        if (view.Identity.DefinitionId >> 24 != 0x13
            || (view.Identity.DefinitionId & 0x00FF_FFFF) == 0
            || view.Fields.IsDefault
            || (view.TypeDefFlags & 3) != 2
            || (view.TypeDefFlags >> 2 & 7) != 2
            || !view.Methods.IsDefaultOrEmpty
            || view.Layouts.IsDefault
            || view.Layouts.Length != 1)
        {
            return false;
        }

        var layout = view.Layouts[0];
        if (layout.OwnerDefinitionId != view.Identity.DefinitionId
            || layout.TargetProfileReference != 0
            || layout.Kind != 0 || layout.Flags != 0
            || layout.Alignment != 0 || layout.Pack != 0
            || layout.FieldDefinitionIds.Length != view.Fields.Length)
        {
            return false;
        }

        for (var fieldIndex = 0; fieldIndex < view.Fields.Length;
            fieldIndex++)
        {
            var field = view.Fields[fieldIndex];
            if (field.DefinitionId >> 24 != 0x14
                || (field.DefinitionId & 0x00FF_FFFF) == 0
                || field.OwnerDefinitionId != view.Identity.DefinitionId
                || layout.FieldDefinitionIds[fieldIndex]
                    != field.DefinitionId)
            {
                return false;
            }
        }

        if (declaration.ExportKeys.IsDefaultOrEmpty)
        {
            return false;
        }

        var exportKeys = declaration.ExportKeys.ToHashSet(
            StringComparer.Ordinal);
        if (exportKeys.Count != declaration.ExportKeys.Length
            || declaration.SelectedExportKeys.Any(key =>
                !BoundEnumTypes.IsValidModuleDeclarationKey(key)
                || !exportKeys.Contains(key))
            || declaration.SelectedExportKeys.Distinct(
                StringComparer.Ordinal).Count()
                != declaration.SelectedExportKeys.Length)
        {
            return false;
        }

        return true;
    }

    private static C1M1ImportedClassImportPlan
        GetC1M1ImportedClassImportPlan(
            BoundStandardClassDeclaration declaration)
    {
        if (!IsC1M1ImportedClassLayoutSupported(declaration))
        {
            throw Unsupported(
                "C1M1 class source emission supports directly selected imported non-generic classes with one default Auto Layout variant.");
        }

        var view = declaration.Declaration;
        var exportKeys = declaration.ExportKeys.ToHashSet(
            StringComparer.Ordinal);
        var selectedExportKeys = new HashSet<string>(StringComparer.Ordinal);
        if (!declaration.SelectedExportKeys.IsEmpty)
        {
            foreach (var exportKey in declaration.SelectedExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !exportKeys.Contains(exportKey)
                    || !selectedExportKeys.Add(exportKey))
                {
                    throw new InvalidDataException(
                        "A selected imported class TypeDef key is absent from its checked provider view.");
                }
            }
        }
        else
        {
            foreach (var expectation in declaration.Expectations)
            {
                var binding = expectation.Binding;
                if (!SameA2SourceTypeProviderSnapshot(binding.Provider,
                        view.Provider)
                    || binding.ProviderDefinitionId
                        != view.Identity.DefinitionId
                    || !StringComparer.Ordinal.Equals(
                        binding.ProducerIdentity,
                        view.Provider.ModuleIdentity)
                    || binding.TargetRequirement
                        != view.Provider.TargetRequirement
                    || binding.RuntimePolicy != view.Provider.RuntimePolicy
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        binding.ExportKey)
                    || !exportKeys.Contains(binding.ExportKey))
                {
                    throw new InvalidDataException(
                        "An imported class TypeDef expectation lost its exact provider ExportKey.");
                }

                _ = selectedExportKeys.Add(binding.ExportKey);
            }
        }

        if (selectedExportKeys.Count == 0)
        {
            throw Unsupported(
                "C1M1 class source emission requires a selected class TypeDef Import or ExportKey.");
        }

        return new C1M1ImportedClassImportPlan(
            view.Provider, [.. selectedExportKeys]);
    }

    private static byte[] EmitC1M1MetadataClassTypeReference(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement, string schemaIdentity)
    {
        var schema = GetC1M1ClassMetadataSchema(schemaIdentity);
        if (program.EntryPoint != -1
            || !program.Classes.IsEmpty || !program.Interfaces.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.SourceTypeDeclarations.IsEmpty
            || !program.SourceGenericTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || !program.EnumDefinitions.IsEmpty
            || !program.SourceGenericTypeInstances.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || !program.NativeFixedArrayTypes.IsEmpty
            || !program.CheckedReferenceTypes.IsEmpty
            || !program.PrivateNominalTypeOrigins.IsEmpty
            || !program.PrivateClassReferences.IsEmpty
            || !program.StaticFields.IsEmpty
            || !program.StaticInitializationGroups.IsEmpty
            || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty
            || !program.Buffers.IsEmpty
            || program.StandardMethodProviderClosure is not null
            || program.StandardFieldProviderClosure is not null
            || program.StandardQuerySourceAssociation is not null
            || program.NativeCoreRegistration is not null
            || program.NativeOutputCoreRegistration is not null
            || program.LayoutCoreRegistration is not null
            || program.StaticInitializationCoreRegistration is not null
            || program.LazyInitializationCoreRegistration is not null
            || program.CoreTaskType is not null
            || !program.CoreExceptionIdentitySelections.IsEmpty
            || !program.CoreContractFailureIdentitySelections.IsEmpty
            || program.CoreContractFailureIdentitySelection is not null
            || program.CoreEvaluationFormIdentitySelection is not null
            || !program.CoreEvaluationFormIdentitySelections.IsEmpty
            || !program.CoreEvaluationFormSchemaProviders.IsEmpty
            || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.NativeLoweringAgreement is not null
            || !BoundGenericNominalTypeContract
                .IsValidDefinitionTable(
                    program.GenericNominalTypeDefinitions)
            || !BoundStandardClassDeclarationContract.IsValidTable(
                program.StandardClassDeclarations,
                program.GenericNominalTypeDefinitions))
        {
            throw Unsupported(
                "C1M1 metadata-only class re-emission requires a type-only class reference with complete checked carriers.");
        }

        SemanticVerifier.VerifyLibraryReferences(program);
        var rootDeclarations = program.StandardClassDeclarations
            .Where(static declaration =>
                declaration.Expectations.IsEmpty
                && !declaration.SelectedExportKeys.IsEmpty)
            .ToArray();
        if (rootDeclarations.Length == 0)
        {
            throw Unsupported(
                "C1M1 metadata-only class re-emission requires an explicitly exported class root.");
        }
        var classIdentities = program.StandardClassDeclarations
            .Select(static declaration => declaration.Declaration.Identity)
            .ToHashSet();
        if (program.StandardClassDeclarations.Any(declaration =>
                !program.GenericNominalTypeDefinitions.Any(definition =>
                    definition.Identity
                        == declaration.Declaration.Identity
                    && definition.NominalKind == 2))
            || program.GenericNominalTypeDefinitions.Any(definition =>
                definition.NominalKind == 1
                    ? definition.ProviderExpectations.IsEmpty
                    : definition.NominalKind != 2
                        || !classIdentities.Contains(definition.Identity)))
        {
            throw Unsupported(
                "C1M1 class re-emission cannot discard or invent a nominal definition.");
        }

        var rootSnapshot = rootDeclarations[0].Declaration.Provider;
        if (!StringComparer.Ordinal.Equals(rootSnapshot.SchemaIdentity,
                schemaIdentity)
            || rootSnapshot.ModuleRow <= 0
            || !StringComparer.Ordinal.Equals(rootSnapshot.ModuleIdentity,
                rootDeclarations[0].Declaration.Identity.ModuleIdentity)
            || rootSnapshot.TargetRequirement != moduleTargetRequirement
            || rootSnapshot.RuntimePolicy != (uint)program.RuntimePolicy)
        {
            throw Unsupported(
                "C1M1 metadata-only class re-emission must retain one exact root Module contract.");
        }

        foreach (var declaration in rootDeclarations)
        {
            if (!SourceTypeSnapshotsMatch(rootSnapshot,
                    declaration.Declaration.Provider))
            {
                throw Unsupported(
                    "C1M1 class roots from multiple source Modules cannot be combined into one output Module.");
            }
        }

        var providerInputs = new List<TrRmStandardArtifactInput>();
        void AddProviderInput(BoundSourceTypeProviderSnapshot provider)
        {
            if (!StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                    schemaIdentity)
                || provider.ArtifactSnapshot.IsDefaultOrEmpty)
            {
                throw new InvalidDataException(
                    "A retained C1M1 type expectation has an invalid provider snapshot.");
            }

            if (provider.ArtifactSnapshot.AsSpan().SequenceEqual(
                    rootSnapshot.ArtifactSnapshot.AsSpan()))
            {
                return;
            }

            foreach (var providerInput in providerInputs)
            {
                if (providerInput.Artifact.Span.SequenceEqual(
                        provider.ArtifactSnapshot.AsSpan()))
                {
                    return;
                }
            }

            providerInputs.Add(new TrRmStandardArtifactInput(
                schemaIdentity,
                provider.ArtifactSnapshot.ToArray()));
        }

        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                AddProviderInput(expectation.Binding.Provider);
                foreach (var fieldExpectation
                    in expectation.FieldExpectations)
                {
                    AddProviderInput(fieldExpectation.Provider);
                }
                foreach (var methodExpectation
                    in expectation.MethodExpectations)
                {
                    AddProviderInput(methodExpectation.Provider);
                }
            }
        }

        foreach (var definition in program.GenericNominalTypeDefinitions)
        {
            foreach (var expectation in definition.ProviderExpectations)
            {
                AddProviderInput(expectation.Provider);
            }
        }

        var artifacts = new byte[providerInputs.Count + 1][];
        var schemaIdentities = new string[artifacts.Length];
        artifacts[0] = rootSnapshot.ArtifactSnapshot.ToArray();
        schemaIdentities[0] = schemaIdentity;
        for (var index = 0; index < providerInputs.Count; index++)
        {
            artifacts[index + 1] = providerInputs[index].Artifact.ToArray();
            schemaIdentities[index + 1] = providerInputs[index].SchemaIdentity;
        }

        var moduleValidation = ValidateStandardInputModules(artifacts,
            schemaIdentities, rootSnapshot.ModuleIdentity,
            a3LayoutResourceLedger: null);
        var rootModuleRow = moduleValidation.RootModuleRow;
        if (rootModuleRow != rootSnapshot.ModuleRow)
        {
            throw new InvalidDataException(
                "The retained C1M1 class root Module row changed.");
        }
        var rootIndex = new TrRmArtifactIndex(artifacts[0], schema);
        var rootTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(rootIndex);
        EnsureC1ClassDeclarationTablesSupported(rootTables);
        var classProviderValidation
            = TrRmFixedArrayProviderBindingValidator
                .ValidateC1M1ClassImports(rootTables, artifacts,
                    schemaIdentities, moduleValidation.Modules,
                    rootModuleRow);
        ValidateC1M1ClassCarrierBindings(program, moduleValidation.Modules,
            artifacts, schemaIdentities, classProviderValidation.TypeBindings,
            classProviderValidation.MethodBindings);
        ValidateC1M1StructCarrierBindings(program, moduleValidation.Modules,
            artifacts, schemaIdentities,
            classProviderValidation.TypeBindings);
        if (!StringComparer.Ordinal.Equals(
                rootTables.Modules.GetIdentity(rootModuleRow),
                rootSnapshot.ModuleIdentity)
            || rootTables.Modules.GetRequirements(rootModuleRow)
                != (rootSnapshot.TargetRequirement,
                    rootSnapshot.RuntimePolicy)
            || rootTables.Modules.GetGoverningRuleEdition(rootModuleRow)
                != rootSnapshot.GoverningRuleEdition
            || rootTables.Modules.GetEntryMethod(rootModuleRow) != 0)
        {
            throw new InvalidDataException(
                "The retained C1M1 class root snapshot is inconsistent.");
        }

        var typeViewsByOldRow
            = new SortedDictionary<int, BoundStandardClassTypeView>();
        var exportsByOldRow
            = new Dictionary<int, ImmutableArray<string>>();
        foreach (var declaration in rootDeclarations)
        {
            var view = declaration.Declaration;
            var typeDefRow = GetTypeDefRow(view.Identity.DefinitionId);
            if (rootTables.TypeDefs.GetOwnerModule(typeDefRow)
                    != rootModuleRow
                || rootTables.TypeDefs.GetKind(typeDefRow) != 2
                || rootTables.TypeDefs.GetGenericRange(typeDefRow).Count != 0
                || rootTables.TypeDefs.GetContractRange(typeDefRow).Count
                    != 0)
            {
                throw Unsupported(
                    "C1M1 metadata-only class re-emission supports nongeneric class roots without TypeDef contracts.");
            }

            var parsedView = CreateStandardClassTypeView(rootTables,
                view.Identity, rootSnapshot, typeDefRow);
            if (!StandardClassViewsMatch(view, parsedView)
                || !GetStandardTypeExportKeys(rootTables, rootModuleRow,
                        typeDefRow).AsSpan()
                    .SequenceEqual(declaration.ExportKeys.AsSpan()))
            {
                throw new InvalidDataException(
                    "The retained C1M1 class root does not match its checked snapshot.");
            }

            if (!typeViewsByOldRow.TryAdd(typeDefRow, parsedView)
                || !exportsByOldRow.TryAdd(typeDefRow,
                    declaration.ExportKeys))
            {
                throw new InvalidDataException(
                    "The C1M1 class reference contains duplicate root TypeDefs.");
            }
        }

        var bindingsByImportRow = new SortedDictionary<int,
            BoundSourceTypeExpectation>();
        var fieldBindingsByImportRow = new SortedDictionary<int,
            TrRmStandardFieldProviderBinding>();
        foreach (var fieldBinding in classProviderValidation.FieldBindings)
        {
            if (fieldBinding.ConsumerArtifactIndex != 0
                || fieldBinding.ConsumerModuleRow != rootModuleRow)
            {
                continue;
            }

            if (!fieldBindingsByImportRow.TryAdd(fieldBinding.ImportRow,
                    fieldBinding))
            {
                throw new InvalidDataException(
                    "A retained C1M1 FieldDef Import has conflicting provider bindings.");
            }
        }

        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                var binding = expectation.Binding;
                if (!SourceTypeSnapshotsMatch(binding.UsingModule,
                        rootSnapshot))
                {
                    continue;
                }

                var expectedTypeDefRow = GetTypeDefRow(
                    binding.ExpectedDefinitionId);
                var parsedExpectedView = CreateStandardClassTypeView(
                    rootTables, expectation.ExpectedDeclaration.Identity,
                    rootSnapshot, expectedTypeDefRow);
                if (rootTables.TypeDefs.GetOwnerModule(expectedTypeDefRow)
                        != rootModuleRow
                    || rootTables.TypeDefs.GetKind(expectedTypeDefRow) != 2
                    || rootTables.TypeDefs.GetGenericRange(
                        expectedTypeDefRow).Count != 0
                    || rootTables.TypeDefs.GetContractRange(
                        expectedTypeDefRow).Count != 0
                    || !StandardClassViewsMatch(
                        expectation.ExpectedDeclaration,
                        parsedExpectedView)
                    || !bindingsByImportRow.TryAdd(binding.ImportRow,
                        binding))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class Import expectation does not match its root snapshot.");
                }

                if (typeViewsByOldRow.TryGetValue(expectedTypeDefRow,
                        out var existingView))
                {
                    if (!StandardClassViewsMatch(existingView,
                            parsedExpectedView))
                    {
                        throw new InvalidDataException(
                            "One C1M1 expected TypeDef has conflicting class views.");
                    }
                }
                else
                {
                    typeViewsByOldRow.Add(expectedTypeDefRow,
                        parsedExpectedView);
                    exportsByOldRow.Add(expectedTypeDefRow, []);
                }

                foreach (var fieldExpectation
                    in expectation.FieldExpectations)
                {
                    if (fieldExpectation.ImportRow <= 0
                        || fieldExpectation.ImportRow
                            > rootTables.Imports.Count)
                    {
                        throw new InvalidDataException(
                            "A retained class FieldDef Import row is out of range.");
                    }

                    var fieldRow = GetFieldDefRow(
                        fieldExpectation.ExpectedDefinitionId);
                    var expectedOwnerTypeDefRow = GetTypeDefRow(
                        fieldExpectation.ExpectedOwnerDefinitionId);
                    var fieldModuleRefRow = rootTables.Imports
                        .GetModuleRefForImportRow(
                            fieldExpectation.ImportRow);
                    var fieldImportBinding = rootTables.Imports
                        .GetBindingForImportRow(
                            fieldExpectation.ImportRow);
                    var fieldRequirements = rootTables.ModuleRefs
                        .GetRequirements(fieldModuleRefRow);
                    if ((fieldExpectation.ExpectedDefinitionId >> 24)
                            != 0x14
                        || (fieldExpectation.ProviderDefinitionId >> 24)
                            != 0x14
                        || (fieldExpectation.ExpectedOwnerDefinitionId
                            >> 24) != 0x13
                        || (fieldExpectation.ProviderOwnerDefinitionId
                            >> 24) != 0x13
                        || fieldExpectation.OwnerImportRow
                            != binding.ImportRow
                        || fieldExpectation.ExpectedOwnerDefinitionId
                            != binding.ExpectedDefinitionId
                        || fieldExpectation.ProviderOwnerDefinitionId
                            != binding.ProviderDefinitionId
                        || rootTables.TypeDefs.GetOwnerModule(
                            expectedOwnerTypeDefRow) != rootModuleRow
                        || rootTables.Fields.GetOwnerTypeDef(fieldRow)
                            != expectedOwnerTypeDefRow
                        || !rootTables.Imports.IsExpectedDefinition(
                            fieldExpectation.ExpectedDefinitionId)
                        || !fieldBindingsByImportRow.TryGetValue(
                            fieldExpectation.ImportRow,
                            out var resolvedFieldBinding)
                        || resolvedFieldBinding.ExpectedFieldDefRow
                            != fieldRow
                        || resolvedFieldBinding.ExpectedOwnerTypeDefRow
                            != expectedOwnerTypeDefRow
                        || resolvedFieldBinding.OwnerImportRow
                            != fieldExpectation.OwnerImportRow
                        || resolvedFieldBinding.ProviderFieldDefRow
                            != GetFieldDefRow(
                                fieldExpectation.ProviderDefinitionId)
                        || resolvedFieldBinding.ProviderTypeDefRow
                            != GetTypeDefRow(
                                fieldExpectation.ProviderOwnerDefinitionId)
                        || resolvedFieldBinding.ProviderModuleRow
                            != fieldExpectation.Provider.ModuleRow
                        || !StringComparer.Ordinal.Equals(
                            resolvedFieldBinding.ProducerIdentity,
                            fieldExpectation.Provider.ModuleIdentity)
                        || !StringComparer.Ordinal.Equals(
                            resolvedFieldBinding.ExportKey,
                            fieldExpectation.ExportKey)
                        || resolvedFieldBinding.TargetRequirement
                            != fieldExpectation.Provider.TargetRequirement
                        || resolvedFieldBinding.RuntimePolicy
                            != fieldExpectation.Provider.RuntimePolicy
                        || rootTables.Imports
                            .GetExpectedDefinitionForImportRow(
                                fieldExpectation.ImportRow)
                            != fieldExpectation.ExpectedDefinitionId
                        || rootTables.ModuleRefs.GetOwnerModule(
                            fieldModuleRefRow) != rootModuleRow
                        || !SourceTypeSnapshotsMatch(
                            fieldExpectation.Provider, binding.Provider)
                        || !StringComparer.Ordinal.Equals(
                            fieldImportBinding.ExportKey,
                            fieldExpectation.ExportKey)
                        || !StringComparer.Ordinal.Equals(
                            rootTables.ModuleRefs.GetProducerIdentity(
                                fieldModuleRefRow),
                            fieldExpectation.Provider.ModuleIdentity)
                        || fieldRequirements
                            != (fieldExpectation.Provider.TargetRequirement,
                                fieldExpectation.Provider.RuntimePolicy))
                    {
                        throw new InvalidDataException(
                            "A retained class FieldDef Import no longer matches its owner and selected provider.");
                    }
                }
            }
        }

        var typeDefViewsByOldRow
            = new SortedDictionary<int, C1M1TypeDefEmissionView>();
        foreach (var (typeDefRow, view) in typeViewsByOldRow)
        {
            typeDefViewsByOldRow.Add(typeDefRow,
                ToC1M1TypeDefEmissionView(rootTables, typeDefRow, view));
        }
        foreach (var definition in program.GenericNominalTypeDefinitions)
        {
            if (definition.NominalKind != 1)
            {
                continue;
            }

            foreach (var expectation in definition.ProviderExpectations)
            {
                if (!SourceTypeSnapshotsMatch(expectation.UsingModule,
                        rootSnapshot))
                {
                    continue;
                }

                var expectedTypeDefRow = GetTypeDefRow(
                    expectation.ExpectedDefinitionId);
                if (rootTables.TypeDefs.GetOwnerModule(expectedTypeDefRow)
                        != rootModuleRow
                    || rootTables.TypeDefs.GetKind(expectedTypeDefRow) != 1
                    || rootTables.TypeDefs.GetGenericRange(
                        expectedTypeDefRow).Count != 0
                    || rootTables.TypeDefs.GetContractRange(
                        expectedTypeDefRow).Count != 0
                    || !bindingsByImportRow.TryAdd(expectation.ImportRow,
                        expectation))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 struct Import expectation does not match its root snapshot.");
                }

                var typeDefView = CreateC1M1TypeDefEmissionView(
                    rootTables, rootSnapshot, expectedTypeDefRow);
                if (typeDefView.Kind != 1
                    || !typeDefViewsByOldRow.TryAdd(expectedTypeDefRow,
                        typeDefView)
                    || !exportsByOldRow.TryAdd(expectedTypeDefRow, []))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 struct Import TypeDef is duplicated.");
                }
            }
        }
        AddC1M1LocalNonGenericTypeClosure(rootTables, rootModuleRow,
            rootSnapshot, typeDefViewsByOldRow, exportsByOldRow);

        var methodBindingsByImportRow = new SortedDictionary<int,
            BoundStandardClassMethodExpectation>();
        var expectedMethodsByOldRow = new SortedDictionary<int,
            BoundStandardClassMethodDeclaration>();
        var expectedMethodOwnersByOldRow = new Dictionary<int,
            BoundStandardClassTypeView>();
        var methodImportsByExpectedMethodRow = new Dictionary<int,
            List<BoundStandardClassMethodExpectation>>();
        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                var classBinding = expectation.Binding;
                if (!SourceTypeSnapshotsMatch(classBinding.UsingModule,
                        rootSnapshot))
                {
                    continue;
                }

                foreach (var methodExpectation
                    in expectation.MethodExpectations)
                {
                    var methodRow = GetMethodDefRow(
                        methodExpectation.ExpectedDefinitionId);
                    var method = expectation.ExpectedDeclaration.Methods
                        .SingleOrDefault(candidate =>
                            candidate.DefinitionId
                                == methodExpectation.ExpectedDefinitionId);
                    if (method is null
                        || expectation.ExpectedDeclaration.Identity
                            .DefinitionId
                            != methodExpectation.ExpectedOwnerDefinitionId
                        || methodExpectation.OwnerImportRow
                            != classBinding.ImportRow
                        || methodExpectation.ExpectedOwnerDefinitionId
                            != classBinding.ExpectedDefinitionId
                        || !bindingsByImportRow.ContainsKey(
                            methodExpectation.OwnerImportRow)
                        || !methodBindingsByImportRow.TryAdd(
                            methodExpectation.ImportRow, methodExpectation)
                        || rootTables.Methods.GetOwnerType(methodRow)
                            != GetTypeDefRow(
                                methodExpectation.ExpectedOwnerDefinitionId)
                        || rootTables.Methods.GetBodyRow(methodRow) != 0
                        || !rootTables.Imports.IsExpectedDefinition(
                            methodExpectation.ExpectedDefinitionId))
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef Import has an invalid or unsupported local expectation.");
                    }

                    if (expectedMethodsByOldRow.TryGetValue(methodRow,
                            out var existingMethod))
                    {
                        if (!StandardClassMethodsMatch(existingMethod,
                                method))
                        {
                            throw new InvalidDataException(
                                "One expected class MethodDef has conflicting retained views.");
                        }
                    }
                    else
                    {
                        expectedMethodsByOldRow.Add(methodRow, method);
                        expectedMethodOwnersByOldRow.Add(methodRow,
                            expectation.ExpectedDeclaration);
                        methodImportsByExpectedMethodRow.Add(methodRow, []);
                    }

                    if (!methodImportsByExpectedMethodRow.TryGetValue(
                            methodRow, out var methodImports))
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef Import has no expected method group.");
                    }

                    methodImports.Add(methodExpectation);
                }
            }
        }

        var methodEmissionPlans = new SortedDictionary<int,
            C1M1MethodEmissionPlan>();
        foreach (var (methodRow, method) in expectedMethodsByOldRow)
        {
            var imports = methodImportsByExpectedMethodRow[methodRow];
            var firstImport = imports[0];
            if (imports.Any(import =>
                    import.ProviderDefinitionId
                        != firstImport.ProviderDefinitionId
                    || import.ProviderOwnerDefinitionId
                        != firstImport.ProviderOwnerDefinitionId
                    || !StringComparer.Ordinal.Equals(
                        import.Provider.ModuleIdentity,
                        firstImport.Provider.ModuleIdentity)
                    || import.Provider.ModuleRow
                        != firstImport.Provider.ModuleRow
                    || !import.Provider.ProviderArtifactDigest.AsSpan()
                        .SequenceEqual(
                            firstImport.Provider.ProviderArtifactDigest
                                .AsSpan())))
            {
                throw new InvalidDataException(
                    "A class MethodDef alias group resolved through distinct provider views.");
            }

            methodEmissionPlans.Add(methodRow,
                new C1M1MethodEmissionPlan(
                    expectedMethodOwnersByOldRow[methodRow], method,
                    [.. imports], [], ReadOnlyMemory<byte>.Empty));
        }

        foreach (var declaration in rootDeclarations)
        {
            var owner = declaration.Declaration;
            var ownerTypeDefRow
                = GetTypeDefRow(owner.Identity.DefinitionId);
            foreach (var method in owner.Methods)
            {
                var methodRow = GetMethodDefRow(method.DefinitionId);
                var bodyRow = checked((int)method.BodyRow);
                if (bodyRow == 0
                    || !schema.UsesConstructionContracts()
                    || rootTables.TypeDefs.GetOwnerModule(ownerTypeDefRow)
                        != rootModuleRow
                    || rootTables.Methods.GetOwnerType(methodRow)
                        != ownerTypeDefRow
                    || rootTables.Methods.GetBodyRow(methodRow)
                        != method.BodyRow
                    || methodEmissionPlans.ContainsKey(methodRow))
                {
                    throw Unsupported(
                        "CM1 local class MethodDef Bodies require the exact Construction-2 profile and root-owned Body rows.");
                }

                var body = rootTables.Bodies.GetRegisterBody(bodyRow,
                    rootTables.Contracts.Count);
                for (var typeMapIndex = 0;
                    typeMapIndex < body.TypeCount; typeMapIndex++)
                {
                    var typeMapToken = BinaryPrimitives
                        .ReadUInt32LittleEndian(body.TypeMap.Slice(
                            typeMapIndex * sizeof(uint), sizeof(uint)));
                    var encoding = (byte)(typeMapToken >> 24);
                    if (encoding == 0x11)
                    {
                        _ = rootTables.TypeSpecs.GetTypeSpecRef(
                            typeMapToken & 0x00FF_FFFF);
                    }
                    else if (encoding is not (0xF2 or 0xF3))
                    {
                        throw Unsupported(
                            "CM1 local class MethodDef Bodies require inline or TypeSpec-backed TypeMap entries.");
                    }
                }

                methodEmissionPlans.Add(methodRow,
                    new C1M1MethodEmissionPlan(owner, method, [],
                        method.ExportKeys,
                        rootTables.Bodies.GetRow(bodyRow).ToArray()));
            }
        }

        var rootImportCount = 0;
        for (var importRow = 1; importRow <= rootTables.Imports.Count;
            importRow++)
        {
            var moduleRefRow
                = rootTables.Imports.GetModuleRefForImportRow(importRow);
            if (rootTables.ModuleRefs.GetOwnerModule(moduleRefRow)
                != rootModuleRow)
            {
                continue;
            }

            rootImportCount++;
            var definitionKind = rootTables.Imports
                .GetExpectedDefinitionForImportRow(importRow) >> 24;
            if (definitionKind is not (0x13 or 0x14 or 0x16)
                || (definitionKind == 0x13
                    ? !bindingsByImportRow.ContainsKey(importRow)
                    : definitionKind == 0x14
                        ? !fieldBindingsByImportRow.ContainsKey(importRow)
                        : !methodBindingsByImportRow.ContainsKey(importRow)))
            {
                throw new NotSupportedException(
                    "C1M1 class re-emission only retains selected TypeDef, FieldDef and bodyless MethodDef Imports.");
            }
        }

        if (typeDefViewsByOldRow.Count == 0
            || bindingsByImportRow.Count + fieldBindingsByImportRow.Count
                + methodBindingsByImportRow.Count != rootImportCount)
        {
            throw new InvalidDataException(
                "The retained C1M1 class provider closure is incomplete.");
        }

        var (methodStart, methodEnd)
            = rootTables.Modules.GetMethodDefRange(rootModuleRow);
        if (methodEnd - methodStart != methodEmissionPlans.Count
            || Enumerable.Range(methodStart, methodEnd - methodStart)
                .Any(methodRow =>
                    !methodEmissionPlans.ContainsKey(methodRow)))
        {
            throw Unsupported(
                "C1M1 class re-emission only retains selected bodyless class Imports and supported local root MethodDefs.");
        }

        var moduleRefStartEnd
            = rootTables.Modules.GetModuleRefRange(rootModuleRow);
        var moduleRefRowsByOldRow = new SortedSet<int>();
        foreach (var importRow in bindingsByImportRow.Keys
            .Concat(fieldBindingsByImportRow.Keys)
            .Concat(methodBindingsByImportRow.Keys).Order())
        {
            var isMethodImport
                = methodBindingsByImportRow.TryGetValue(importRow,
                    out var methodBinding);
            var isFieldImport
                = fieldBindingsByImportRow.TryGetValue(importRow,
                    out var fieldBinding);
            var binding = isMethodImport || isFieldImport ? null
                : bindingsByImportRow[importRow];
            var expectedDefinitionId = isMethodImport
                ? methodBinding!.ExpectedDefinitionId
                : isFieldImport
                    ? 0x1400_0000u
                        | checked((uint)fieldBinding!.ExpectedFieldDefRow)
                    : binding!.ExpectedDefinitionId;
            var producerIdentity = isMethodImport
                ? methodBinding!.Provider.ModuleIdentity
                : isFieldImport ? fieldBinding!.ProducerIdentity
                    : binding!.ProducerIdentity;
            var exportKey = isMethodImport
                ? methodBinding!.ExportKey
                : isFieldImport ? fieldBinding!.ExportKey
                    : binding!.ExportKey;
            var targetRequirement = isMethodImport
                ? methodBinding!.Provider.TargetRequirement
                : isFieldImport ? fieldBinding!.TargetRequirement
                    : binding!.TargetRequirement;
            var runtimePolicy = isMethodImport
                ? methodBinding!.Provider.RuntimePolicy
                : isFieldImport ? fieldBinding!.RuntimePolicy
                    : binding!.RuntimePolicy;
            if (importRow <= 0 || importRow > rootTables.Imports.Count
                || (rootTables.Imports
                        .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    != (isMethodImport ? 0x16u
                        : isFieldImport ? 0x14u : 0x13u)
                || rootTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) != expectedDefinitionId
                || !StringComparer.Ordinal.Equals(
                    rootTables.Imports.GetBindingForImportRow(importRow)
                        .ExportKey, exportKey)
                || !StringComparer.Ordinal.Equals(producerIdentity,
                    rootTables.ModuleRefs.GetProducerIdentity(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)))
                || targetRequirement
                    != rootTables.ModuleRefs.GetRequirements(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)).TargetRequirement
                || runtimePolicy
                    != rootTables.ModuleRefs.GetRequirements(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)).RuntimePolicy)
            {
                throw new InvalidDataException(
                    "A retained C1M1 class Import no longer matches its selected provider.");
            }

            var moduleRefRow
                = rootTables.Imports.GetModuleRefForImportRow(importRow);
            _ = moduleRefRowsByOldRow.Add(moduleRefRow);
        }

        if (moduleRefRowsByOldRow.Count
                != moduleRefStartEnd.End - moduleRefStartEnd.Start
            || moduleRefRowsByOldRow.Any(moduleRefRow =>
                moduleRefRow < moduleRefStartEnd.Start
                || moduleRefRow >= moduleRefStartEnd.End))
        {
            throw new InvalidDataException(
                "The retained C1M1 class ModuleRef range is incomplete.");
        }

        var typeDefRowMap = new Dictionary<int, int>();
        var nextTypeDefRow = 0;
        foreach (var oldTypeDefRow in typeDefViewsByOldRow.Keys)
        {
            typeDefRowMap.Add(oldTypeDefRow, ++nextTypeDefRow);
        }

        var methodDefRowMap = new Dictionary<int, int>();
        var nextMethodDefRow = 0;
        foreach (var oldMethodDefRow in methodEmissionPlans.Keys)
        {
            methodDefRowMap.Add(oldMethodDefRow, ++nextMethodDefRow);
        }

        var importRowMap = new Dictionary<int, int>();
        var nextImportRow = 0;
        foreach (var oldImportRow in bindingsByImportRow.Keys
            .Concat(fieldBindingsByImportRow.Keys)
            .Concat(methodBindingsByImportRow.Keys).Order())
        {
            importRowMap.Add(oldImportRow, ++nextImportRow);
        }

        var moduleRefRowMap = new Dictionary<int, int>();
        var nextModuleRefRow = 0;
        foreach (var oldModuleRefRow in moduleRefRowsByOldRow)
        {
            moduleRefRowMap.Add(oldModuleRefRow, ++nextModuleRefRow);
        }

        var outputTypeSpecRows
            = new List<ReadOnlyMemory<byte>>();
        var typeSpecRowMap = new Dictionary<int, uint>();
        var activeTypeSpecRows = new HashSet<int>();
        var fieldRows = new List<TrRmFieldDefRow>();
        var layoutRows = new List<TrRmLayoutRow>();
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        var outputTypeDefRows = new List<TrRmTypeDefRow>();
        var typeDefNames = new List<(string Namespace, string Name)>();
        var fieldRowMap = new Dictionary<int, int>();
        var fieldNames = new List<string>();
        var methodRows = new List<TrRmMethodDefRow>();
        var methodSignatureRows = new List<TrRmMethodSigRow>();
        var parameterRows = new List<TrRmParamRow>();
        var methodNames = new List<string>();
        var bodyRows = new List<ReadOnlyMemory<byte>>();
        var fieldPathRows = new List<TrRmFieldPathRow>();
        var fieldPathRowMap = new Dictionary<int, int>();
        var fieldPathRowsByIdentity
            = new Dictionary<C1M1FieldPathIdentity, int>();
        var parameterSurfaceRows = new List<C1M1ParameterSurfaceRow>();
        var contractRows = new List<TrRmContractRow>();
        var frozenScalarValueBuilder = new TrRmFrozenScalarValueBuilder();
        var exportRows = new List<(uint DefinitionId, string Key)>();
        var exportKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (oldTypeDefRow, view) in typeDefViewsByOldRow)
        {
            if (view.Fields.Length > 0xFF_FFFF
                || view.Layouts.IsEmpty
                || view.Layouts.Length > 0xFF_FFFF)
            {
                throw Unsupported(
                    "The C1M1 TypeDef closure exceeds the metadata row limits.");
            }

            var newTypeDefRow = typeDefRowMap[oldTypeDefRow];
            var fieldFirst = view.Fields.IsEmpty ? 0
                : fieldRows.Count + 1;
            foreach (var field in view.Fields)
            {
                if (GetTypeDefRow(field.OwnerDefinitionId)
                        != oldTypeDefRow
                    || (field.DefinitionId >> 24) != 0x14)
                {
                    throw new InvalidDataException(
                        "A retained C1M1 TypeDef FieldDef has an invalid owner.");
                }

                var oldFieldRow = GetFieldDefRow(field.DefinitionId);
                if (!fieldRowMap.TryAdd(oldFieldRow,
                        fieldRows.Count + 1))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 TypeDef FieldDef is assigned more than once.");
                }

                var remappedTypeSpec
                    = RemapC1M1ClassTypeSpec(rootTables,
                        field.TypeSpecReference, typeDefRowMap,
                        importRowMap, outputTypeSpecRows, typeSpecRowMap,
                        activeTypeSpecRows, depth: 0);
                fieldRows.Add(new TrRmFieldDefRow(newTypeDefRow,
                    0, remappedTypeSpec, field.Flags));
                fieldNames.Add(field.Name);
            }

            var layoutFirst = layoutRows.Count + 1;
            foreach (var layout in view.Layouts)
            {
                var newLayoutRow = layoutRows.Count + 1;
                var layoutFieldFirst = layout.FieldDefinitionIds.IsEmpty
                    ? 0 : layoutFieldRows.Count + 1;
                for (var ordinal = 0;
                    ordinal < layout.FieldDefinitionIds.Length; ordinal++)
                {
                    var oldFieldRow = GetFieldDefRow(
                        layout.FieldDefinitionIds[ordinal]);
                    if (!fieldRowMap.TryGetValue(oldFieldRow,
                            out var newFieldRow))
                    {
                        throw new InvalidDataException(
                            "A retained C1M1 Layout refers to an unowned FieldDef.");
                    }

                    layoutFieldRows.Add(new TrRmLayoutFieldRow(
                        newLayoutRow, ordinal, newFieldRow));
                }

                layoutRows.Add(new TrRmLayoutRow(newTypeDefRow,
                    layout.TargetProfileReference, layout.Kind,
                    layout.Flags, layout.Alignment, layout.Pack,
                    layoutFieldFirst,
                    layout.FieldDefinitionIds.Length));
            }

            var exportKeysForType = exportsByOldRow[oldTypeDefRow];
            foreach (var exportKey in exportKeysForType)
            {
                if (!exportKeys.Add(exportKey))
                {
                    throw new InvalidDataException(
                        "C1M1 class ExportKeys must be unique within the Module.");
                }

                exportRows.Add((
                    0x1300_0000u | checked((uint)newTypeDefRow),
                    exportKey));
            }

            outputTypeDefRows.Add(new TrRmTypeDefRow(0, 0, view.TypeDefFlags,
                fieldFirst, view.Fields.Length,
                0, 0, layoutFirst, view.Layouts.Length,
                0, 0));
            typeDefNames.Add((view.Namespace, view.Name));
        }

        foreach (var (oldMethodRow, plan) in methodEmissionPlans)
        {
            var method = plan.Method;
            var ownerTypeDefRow
                = GetTypeDefRow(plan.Owner.Identity.DefinitionId);
            var hasBody = !plan.Body.IsEmpty;
            if ((hasBody
                    ? method.BodyRow == 0 || !plan.Imports.IsEmpty
                    : method.BodyRow != 0 || plan.Imports.IsEmpty
                        || !plan.ExportKeys.IsEmpty)
                || method.GenericParameterCount != 0
                || method.ConstructionRole != 0
                || method.InitializerMethodDefinitionId != 0
                || method.ConstructionPropertyDefinitionId != 0
                || !method.InitializedMemberDefinitionIds.IsEmpty
                || (method.Flags & 3) == 3
                || (!plan.Imports.IsEmpty
                    && (method.Flags & 3) != 2)
                || ((method.Flags & 4) != 0
                    && (method.ReceiverTypeSpecReference == 0
                        || !schema.UsesMethodReceiverAccess()))
                || (method.EffectsMask & ~6u) != 0
                || !typeDefRowMap.TryGetValue(ownerTypeDefRow,
                    out var newOwnerTypeDefRow))
            {
                throw Unsupported(
                    "C1M1 source-free class re-emission supports bodyless public MethodDef Imports and bounded local nongeneric method Bodies without construction roles.");
            }

            if (method.ReceiverTypeSpecReference != 0)
            {
                var receiver = rootTables.TypeSpecs.GetTypeSpecRef(
                    method.ReceiverTypeSpecReference);
                var mutableReceiver = (method.Flags & 4) != 0;
                if (!receiver.IsRow
                    || receiver.Form is not (0x02 or 0x03)
                    || receiver.Qualifiers
                        != (mutableReceiver ? 0x01 : 0x00)
                    || rootTables.TypeSpecs.GetNominalTypeDefRow(
                        method.ReceiverTypeSpecReference,
                        rootTables.TypeDefs, rootTables.Imports)
                        != ownerTypeDefRow)
                {
                    throw Unsupported(
                        "A retained class MethodDef receiver must be the exact unqualified expected class handle.");
                }
            }

            var resultTypeSpec = RemapC1M1ClassTypeSpec(rootTables,
                method.ReturnTypeSpecReference, typeDefRowMap,
                importRowMap, outputTypeSpecRows, typeSpecRowMap,
                activeTypeSpecRows, depth: 0);
            var receiverTypeSpec = method.ReceiverTypeSpecReference == 0
                ? 0
                : RemapC1M1ClassTypeSpec(rootTables,
                    method.ReceiverTypeSpecReference, typeDefRowMap,
                    importRowMap, outputTypeSpecRows, typeSpecRowMap,
                    activeTypeSpecRows, depth: 0);
            var signatureRow = methodSignatureRows.Count + 1;
            var parameterFirst = method.Parameters.IsEmpty
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < method.Parameters.Length;
                ordinal++)
            {
                var parameter = method.Parameters[ordinal];
                if (parameter.Mode != 0 || parameter.Flags != 0)
                {
                    throw Unsupported(
                        "A retained class MethodDef parameter must use the bounded by-value mode.");
                }

                var remappedTypeSpec = RemapC1M1ClassTypeSpec(rootTables,
                    parameter.TypeSpecReference, typeDefRowMap,
                    importRowMap, outputTypeSpecRows, typeSpecRowMap,
                    activeTypeSpecRows, depth: 0);
                parameterRows.Add(new TrRmParamRow(signatureRow, ordinal,
                    remappedTypeSpec, parameter.Mode, parameter.Flags));

                if (schema.UsesClassParameterDefaults())
                {
                    uint defaultConstantSourceRow = 0;
                    if (parameter.DefaultValue is BoundConstant value)
                    {
                        if (!TryGetC1M1ClassScalarType(rootTables,
                                parameter.TypeSpecReference,
                                out var parameterType)
                            || parameterType is (PrimitiveType.NativeInt
                                or PrimitiveType.NativeUInt)
                            || value.Type != parameterType
                            || value.RepresentationType != parameterType
                            || parameterType is PrimitiveType.NativeInt
                                or PrimitiveType.NativeUInt
                            || !value.Bits.IsCanonicalFor(parameterType))
                        {
                            throw Unsupported(
                                "A retained CM1 class parameter default must be an exact fixed-width scalar value.");
                        }

                        var frozenValue
                            = frozenScalarValueBuilder.Add(value);
                        if (frozenValue.TypeSpecRef != remappedTypeSpec)
                        {
                            throw Unsupported(
                                "A retained CM1 class parameter default changed its exact parameter TypeSpec.");
                        }

                        defaultConstantSourceRow
                            = frozenValue.ConstantSourceRow;
                    }

                    parameterSurfaceRows.Add(
                        new C1M1ParameterSurfaceRow(
                            methodDefRowMap[oldMethodRow], ordinal,
                            parameter.Name, defaultConstantSourceRow));
                }
            }

            methodSignatureRows.Add(new TrRmMethodSigRow(
                resultTypeSpec, receiverTypeSpec, parameterFirst,
                method.Parameters.Length, 0, 0));

            var contractFirst = method.EffectsMask == 0
                ? 0 : contractRows.Count + 1;
            if (method.EffectsMask != 0)
            {
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u
                        | checked((uint)methodDefRowMap[oldMethodRow]),
                    1, s_effectPayloads[method.EffectsMask]));
            }

            var outputBodyRow = hasBody ? bodyRows.Count + 1 : 0;
            if (hasBody)
            {
                bodyRows.Add(RemapC1M1ClassMethodBody(rootTables,
                    plan.Body, typeDefRowMap, methodDefRowMap,
                    fieldRowMap, importRowMap, fieldPathRowMap,
                    fieldPathRows, fieldPathRowsByIdentity,
                    frozenScalarValueBuilder, outputTypeSpecRows,
                    typeSpecRowMap, activeTypeSpecRows, schema));
            }

            methodRows.Add(new TrRmMethodDefRow(newOwnerTypeDefRow, 0,
                signatureRow, method.Flags, contractFirst,
                method.EffectsMask == 0 ? 0 : 1, outputBodyRow));
            methodNames.Add(method.Name);
            foreach (var exportKey in plan.ExportKeys)
            {
                if (!exportKeys.Add(exportKey))
                {
                    throw new InvalidDataException(
                        "C1M1 class MethodDef ExportKeys must be unique within the Module.");
                }

                exportRows.Add((
                    0x1600_0000u
                        | checked((uint)methodDefRowMap[oldMethodRow]),
                    exportKey));
            }
        }

        foreach (var function in program.Functions)
        {
            var matches = methodEmissionPlans.Values.Where(plan =>
                    StringComparer.Ordinal.Equals(function.Name,
                        (string.IsNullOrEmpty(plan.Owner.Namespace)
                            ? plan.Owner.Name
                            : plan.Owner.Namespace + "."
                                + plan.Owner.Name)
                        + "." + plan.Method.Name))
                .ToArray();
            if (matches.Length != 1)
            {
                throw Unsupported(
                    "The retained class MethodDef call views exceed the selected Import and local Body subset.");
            }

            var methodPlan = matches[0];
            var providerIdentity = methodPlan.Imports.IsEmpty
                ? rootSnapshot.ModuleIdentity
                : methodPlan.Imports[0].Provider.ModuleIdentity;
            var providerTargetRequirement = methodPlan.Imports.IsEmpty
                ? rootSnapshot.TargetRequirement
                : methodPlan.Imports[0].Provider.TargetRequirement;
            var providerRuntimePolicy = methodPlan.Imports.IsEmpty
                ? rootSnapshot.RuntimePolicy
                : methodPlan.Imports[0].Provider.RuntimePolicy;
            var hasSelectedExport
                = function.ModuleImport is BoundModuleImport selectedImport
                    && (methodPlan.Imports.IsEmpty
                        ? methodPlan.ExportKeys.Contains(
                            selectedImport.ExportKey,
                            StringComparer.Ordinal)
                        : methodPlan.Imports.Any(import =>
                            StringComparer.Ordinal.Equals(import.ExportKey,
                                selectedImport.ExportKey)));
            if (function.ModuleImport is not BoundModuleImport functionImport
                || !function.IsExternal
                || function.IsStatic
                    != (methodPlan.Method.ReceiverTypeSpecReference == 0)
                || !StringComparer.Ordinal.Equals(
                    function.DeclaringStruct,
                    string.IsNullOrEmpty(methodPlan.Owner.Namespace)
                        ? methodPlan.Owner.Name
                        : methodPlan.Owner.Namespace + "."
                            + methodPlan.Owner.Name)
                || !StringComparer.Ordinal.Equals(
                    functionImport.ProducerIdentity,
                    providerIdentity)
                || functionImport.TargetRequirement
                    != providerTargetRequirement
                || functionImport.RuntimePolicy
                    != providerRuntimePolicy
                || !hasSelectedExport
                || !TryGetC1M1ClassScalarType(rootTables,
                    methodPlan.Method.ReturnTypeSpecReference,
                    out var methodReturnType)
                || function.ReturnType != methodReturnType
                || function.ParameterCount
                    != methodPlan.Method.Parameters.Length
                        + (methodPlan.Method.ReceiverTypeSpecReference == 0
                            ? 0 : 1)
                || function.Variables.Length < function.ParameterCount
                || (!function.IsStatic
                && function.Variables[0].IsMutable
                    != ((methodPlan.Method.Flags & 4) != 0))
                || function.RequiresUnsafeCaller
                    != ((methodPlan.Method.EffectsMask & 4) != 0)
                || function.DoesNotReturn
                    != ((methodPlan.Method.EffectsMask & 2) != 0))
            {
                throw Unsupported(
                    "A retained class MethodDef call view does not match its complete scalar Import expectation.");
            }

            for (var ordinal = 0;
                ordinal < methodPlan.Method.Parameters.Length; ordinal++)
            {
                if (!TryGetC1M1ClassScalarType(rootTables,
                        methodPlan.Method.Parameters[ordinal]
                            .TypeSpecReference, out var parameterType)
                    || function.Variables[ordinal
                        + (methodPlan.Method.ReceiverTypeSpecReference == 0
                            ? 0 : 1)].Type != parameterType)
                {
                    throw Unsupported(
                        "A retained class MethodDef call view changed its checked parameter types.");
                }
            }
        }

        var methodClosureRowCount = (long)methodRows.Count
            + methodSignatureRows.Count + parameterRows.Count
            + parameterSurfaceRows.Count + contractRows.Count
            + bodyRows.Count + fieldPathRows.Count
            + frozenScalarValueBuilder.BlobCount
            + frozenScalarValueBuilder.FrozenValueNodeCount
            + frozenScalarValueBuilder.ConstantSourceCount;
        if (methodRows.Count > 0xFF_FFFF
            || methodSignatureRows.Count > 0xFF_FFFF
            || parameterRows.Count > 0xFF_FFFF
            || parameterSurfaceRows.Count > 0xFF_FFFF
            || contractRows.Count > 0xFF_FFFF
            || bodyRows.Count > 0xFF_FFFF
            || fieldPathRows.Count > 0xFF_FFFF
            || methodClosureRowCount > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The retained class MethodDef closure exceeds the checked metadata row limits.");
        }

        foreach (var (definitionId, exportKey) in
            rootTables.Exports.GetExportsForModule(rootModuleRow))
        {
            if ((definitionId >> 24) != 0x14)
            {
                continue;
            }

            var oldFieldRow = GetFieldDefRow(definitionId);
            var oldOwnerTypeDefRow = rootTables.Fields
                .GetOwnerTypeDef(oldFieldRow);
            if (!exportsByOldRow.TryGetValue(oldOwnerTypeDefRow,
                    out var ownerExportKeys)
                || ownerExportKeys.IsEmpty)
            {
                continue;
            }

            if (!fieldRowMap.TryGetValue(oldFieldRow,
                    out var newFieldRow)
                || !exportKeys.Add(exportKey))
            {
                throw new InvalidDataException(
                    "A retained C1M1 class FieldDef ExportKey cannot be remapped.");
            }

            exportRows.Add((
                0x1400_0000u | checked((uint)newFieldRow), exportKey));
        }

        var featureRows = rootSnapshot.Features
            .Select(static feature => new TrRmFeatureRow(feature.FeatureId,
                feature.RuleRevision))
            .ToArray();

        var textRows = new List<(string Text, int Encoding)>();
        var stringRows = new Dictionary<string, int>(StringComparer.Ordinal);
        int AddString(string text)
        {
            if (!stringRows.TryGetValue(text, out var row))
            {
                textRows.Add((text, 1));
                row = textRows.Count;
                stringRows.Add(text, row);
            }

            return row;
        }

        var moduleIdentityStringRow = AddString(moduleIdentity);
        foreach (var (oldTypeDefRow, view) in typeDefViewsByOldRow)
        {
            _ = oldTypeDefRow;
            AddString(view.Namespace);
            AddString(view.Name);
        }

        foreach (var fieldName in fieldNames)
        {
            AddString(fieldName);
        }
        foreach (var methodName in methodNames)
        {
            AddString(methodName);
        }
        foreach (var parameterSurface in parameterSurfaceRows)
        {
            AddString(parameterSurface.Name);
        }
        foreach (var moduleRefRow in moduleRefRowsByOldRow)
        {
            AddString(rootTables.ModuleRefs.GetProducerIdentity(moduleRefRow));
        }
        foreach (var binding in bindingsByImportRow.Values)
        {
            AddString(binding.ExportKey);
        }
        foreach (var binding in fieldBindingsByImportRow.Values)
        {
            AddString(binding.ExportKey);
        }
        foreach (var binding in methodBindingsByImportRow.Values)
        {
            AddString(binding.ExportKey);
        }
        foreach (var exportRow in exportRows)
        {
            AddString(exportRow.Key);
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes,
            textRows.Count);
        for (var typeIndex = 0;
            typeIndex < outputTypeDefRows.Count; typeIndex++)
        {
            outputTypeDefRows[typeIndex] = outputTypeDefRows[typeIndex] with {
                NamespaceStringRow
                    = stringRows[typeDefNames[typeIndex].Namespace],
                NameStringRow = stringRows[typeDefNames[typeIndex].Name],
            };
        }
        for (var fieldIndex = 0; fieldIndex < fieldRows.Count; fieldIndex++)
        {
            fieldRows[fieldIndex] = fieldRows[fieldIndex] with {
                NameStringRow = stringRows[fieldNames[fieldIndex]],
            };
        }
        for (var methodIndex = 0; methodIndex < methodRows.Count;
            methodIndex++)
        {
            methodRows[methodIndex] = methodRows[methodIndex] with {
                NameStringRow = stringRows[methodNames[methodIndex]],
            };
        }

        var outputTypeSpecBytes = outputTypeSpecRows.Count == 0
            ? []
            : TrRmTypeSpecTableWriter.Encode(outputTypeSpecRows,
                outputTypeDefRows.Count, importRowMap.Count, 0, 0,
                schema, methodDefCount: methodRows.Count);
        var typeSpecReaderBytes = outputTypeSpecRows.Count == 0
            ? new byte[sizeof(uint)] : outputTypeSpecBytes;
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecReaderBytes,
            outputTypeSpecRows.Count, outputTypeDefRows.Count,
            importRowMap.Count, 0, 0, schema,
            methodDefCount: methodRows.Count);

        var targetProfileBytes = GetTargetProfileSection(rootIndex,
            out var targetProfileCount);
        var targetProfiles = targetProfileBytes.Length == 0
            ? new TrRmTargetProfileTableReader([], 0)
            : new TrRmTargetProfileTableReader(targetProfileBytes,
                targetProfileCount);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleIdentityStringRow,
                moduleTargetRequirement, rootSnapshot.RuntimePolicy, 0,
                1, 1, 1, 1,
                featureRows.Length == 0 ? 0 : 1,
                featureRows.Length, rootSnapshot.GoverningRuleEdition),
        ], strings, moduleRefRowMap.Count, outputTypeDefRows.Count,
            methodRows.Count, 0,
            featureRows.Length);
        var moduleTables = new TrRmModuleTableReader(moduleBytes, 1,
            strings, moduleRefRowMap.Count, outputTypeDefRows.Count,
            methodRows.Count, 0,
            featureRows.Length);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(outputTypeDefRows,
            strings, moduleTables, fieldRows.Count, methodRows.Count,
            layoutRows.Count, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            outputTypeDefRows.Count, strings, moduleTables, fieldRows.Count,
            methodRows.Count, layoutRows.Count, 0, schema);

        var layoutFieldCount = layoutFieldRows.Count;
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings, typeDefs,
                typeSpecs, schema);
        var fields = new TrRmFieldDefTableReader(fieldBytes,
            fieldRows.Count, strings, typeDefs, typeSpecs);
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, layoutFieldCount);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs, targetProfiles, layoutFieldCount);
        var layoutFieldBytes = layoutFieldCount == 0 ? []
            : TrRmLayoutFieldTableWriter.Encode(layoutFieldRows, layouts,
                typeDefs, fields);

        var moduleRefRows = new TrRmModuleRefRow[moduleRefRowMap.Count];
        foreach (var (oldModuleRefRow, newModuleRefRow)
            in moduleRefRowMap)
        {
            var producerIdentity = rootTables.ModuleRefs
                .GetProducerIdentity(oldModuleRefRow);
            var requirements = rootTables.ModuleRefs.GetRequirements(
                oldModuleRefRow);
            moduleRefRows[newModuleRefRow - 1] = new TrRmModuleRefRow(
                stringRows[producerIdentity], requirements.TargetRequirement,
                requirements.RuntimePolicy);
        }
        var moduleRefBytes = moduleRefRows.Length == 0 ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings,
                moduleTables);
        var moduleRefs = moduleRefRows.Length == 0 ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Length, strings, moduleTables);
        var signatureBytes = methodSignatureRows.Count == 0 ? []
            : TrRmMethodSigTableWriter.Encode(methodSignatureRows,
                typeSpecs, parameterRows.Count, 0);
        var signatures = methodSignatureRows.Count == 0 ? default
            : new TrRmMethodSigTableReader(signatureBytes,
                methodSignatureRows.Count, typeSpecs,
                parameterRows.Count, 0);
        var parameterBytes = parameterRows.Count == 0 ? []
            : TrRmParamTableWriter.Encode(parameterRows, signatures,
                typeSpecs, schema);
        var parameters = parameterRows.Count == 0 ? default
            : new TrRmParamTableReader(parameterBytes, parameterRows.Count,
                signatures, typeSpecs, schema);
        var methodBytes = methodRows.Count == 0 ? []
            : TrRmMethodDefTableWriter.Encode(methodRows, strings,
                moduleTables, typeDefs, signatures, contractRows.Count, 0,
                schema);
        var bodyBytes = bodyRows.Count == 0 ? []
            : TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var methods = methodRows.Count == 0 ? default
            : new TrRmMethodDefTableReader(methodBytes, methodRows.Count,
                strings, moduleTables, typeDefs, signatures,
                contractRows.Count, 0, schema);
        var classImportRows = CreateC1M1ClassImports(
            bindingsByImportRow, fieldBindingsByImportRow,
            methodBindingsByImportRow, importRowMap, moduleRefRowMap,
            typeDefRowMap, fieldRowMap, methodDefRowMap, stringRows,
            rootTables);
        var featureReader = featureRows.Length == 0 ? default
            : new TrRmFeatureTableReader(featureBytes, featureRows.Length,
                schema);
        var importBytes = classImportRows.Length == 0 ? []
            : TrRmImportTableWriter.Encode(classImportRows, strings,
                moduleRefs, typeDefs, methods, default, default, default,
                fields, featureReader, moduleTables, schema);
        var imports = classImportRows.Length == 0 ? default
            : new TrRmImportTableReader(importBytes, classImportRows.Length,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, fields, featureReader, moduleTables, schema);
        var fieldPathBytes = fieldPathRows.Count == 0 ? []
            : TrRmFieldPathTableWriter.Encode(fieldPathRows, typeSpecs,
                fields, imports);
        var frozenScalarValueTables
            = frozenScalarValueBuilder.Encode(typeSpecs);
        var parameterSurfaceBytes
            = new byte[checked(parameterSurfaceRows.Count * 16)];
        for (var index = 0; index < parameterSurfaceRows.Count; index++)
        {
            var surface = parameterSurfaceRows[index];
            var offset = index * 16;
            Write(parameterSurfaceBytes, offset,
                checked((uint)surface.MethodDefRow));
            Write(parameterSurfaceBytes, offset + 4,
                checked((uint)surface.Ordinal));
            Write(parameterSurfaceBytes, offset + 8,
                checked((uint)stringRows[surface.Name]));
            Write(parameterSurfaceBytes, offset + 12,
                surface.DefaultConstantSourceRow);
        }
        var contractBytes = contractRows.Count == 0 ? []
            : schema.UsesConstructionContracts()
                ? TrRmContractTableWriter
                    .EncodeConstructionDraftForSchema(contractRows,
                        typeDefs, methods, signatures, parameters, typeSpecs,
                        fields, default, default, featureReader, moduleTables,
                        predicateGraphCount: 0, schema: schema)
                : TrRmContractTableWriter.EncodeWithPredicateGraphs(
                    contractRows, typeDefs, methods, signatures, parameters,
                    typeSpecs, featureReader, moduleTables,
                    predicateGraphCount: 0, schema: schema);
        var exportBytes = exportRows.Count == 0 ? []
            : EncodeC1M1Exports(exportRows, stringRows);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)textRows.Count), stringBytes),
            new(0x0013, 0, checked((uint)outputTypeDefRows.Count),
                typeDefBytes),
            new(0x0015, 0, checked((uint)layoutRows.Count), layoutBytes),
        };
        if (featureRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                checked((uint)featureRows.Length), featureBytes));
        }
        if (targetProfileBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)targetProfileCount), targetProfileBytes));
        }
        if (moduleRefRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                checked((uint)moduleRefRows.Length), moduleRefBytes));
        }
        if (outputTypeSpecRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0011, 0,
                checked((uint)outputTypeSpecRows.Count),
                outputTypeSpecBytes));
        }
        if (fieldRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0014, 0,
                checked((uint)fieldRows.Count), fieldBytes));
        }
        if (layoutFieldCount != 0)
        {
            sections.Add(new TrRmSection(0x0023, 0,
                checked((uint)layoutFieldCount), layoutFieldBytes));
        }
        if (importBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                checked((uint)classImportRows.Length), importBytes));
        }
        if (exportBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exportRows.Count), exportBytes));
        }
        if (methodRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0016, 0,
                checked((uint)methodRows.Count), methodBytes));
            sections.Add(new TrRmSection(0x0017, 0,
                checked((uint)methodSignatureRows.Count), signatureBytes));
        }
        if (parameterRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                checked((uint)parameterRows.Count), parameterBytes));
        }
        if (bodyRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0020, 0,
                checked((uint)bodyRows.Count), bodyBytes));
        }
        if (contractRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001B, 0,
                checked((uint)contractRows.Count), contractBytes));
        }
        if (frozenScalarValueTables.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)frozenScalarValueTables.BlobCount),
                frozenScalarValueTables.BlobSection));
        }
        if (frozenScalarValueTables.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                checked((uint)frozenScalarValueTables.ConstantSourceCount),
                frozenScalarValueTables.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                checked((uint)frozenScalarValueTables.FrozenValueNodeCount),
                frozenScalarValueTables.FrozenValueNodeSection));
        }
        if (parameterSurfaceRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0066, 0,
                checked((uint)parameterSurfaceRows.Count),
                parameterSurfaceBytes));
        }
        if (fieldPathRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x004B, 0,
                checked((uint)fieldPathRows.Count), fieldPathBytes));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections,
            schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(artifact, schema));
        return artifact;
    }

    private static void ValidateC1M1ClassCarrierBindings(
        BoundProgram program,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        byte[][] artifacts,
        string[] schemaIdentities,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings,
        ImmutableArray<TrRmStandardMethodProviderBinding>
            methodProviderBindings)
    {
        var expectationsByImport = new Dictionary<
            (string ModuleIdentity, int ModuleRow, int ImportRow),
            BoundStandardClassExpectation>();
        var methodExpectationsByImport = new Dictionary<
            (string ModuleIdentity, int ModuleRow, int ImportRow),
            (BoundStandardClassExpectation Owner,
                BoundStandardClassMethodExpectation Method)>();
        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                var binding = expectation.Binding;
                var usingModule = modules.Single(module =>
                    StringComparer.Ordinal.Equals(module.Identity,
                        binding.UsingModule.ModuleIdentity)
                    && module.ModuleRow == binding.UsingModule.ModuleRow);
                var usingTables = GetC1M1Tables(artifacts,
                    usingModule.ArtifactIndex,
                    schemaIdentities[usingModule.ArtifactIndex]);
                EnsureC1ClassDeclarationTablesSupported(usingTables);
                var usingSnapshot = CreateA2SourceTypeProviderSnapshot(
                    usingTables, artifacts[usingModule.ArtifactIndex],
                    schemaIdentities[usingModule.ArtifactIndex],
                    usingModule.ModuleRow);
                var expectedTypeDefRow
                    = GetTypeDefRow(binding.ExpectedDefinitionId);
                var expectedModuleRefRow = usingTables.Imports
                    .GetModuleRefForImportRow(binding.ImportRow);
                var importBinding = usingTables.Imports
                    .GetBindingForImportRow(binding.ImportRow);
                var moduleRefRequirements = usingTables.ModuleRefs
                    .GetRequirements(expectedModuleRefRow);
                if (!SourceTypeSnapshotsMatch(binding.UsingModule,
                        usingSnapshot)
                    || usingTables.TypeDefs.GetOwnerModule(
                        expectedTypeDefRow) != usingModule.ModuleRow
                    || usingTables.TypeDefs.GetKind(expectedTypeDefRow) != 2
                    || usingTables.Imports
                        .GetExpectedDefinitionForImportRow(binding.ImportRow)
                        != binding.ExpectedDefinitionId
                    || !StringComparer.Ordinal.Equals(
                        importBinding.ProducerIdentity,
                        binding.ProducerIdentity)
                    || !StringComparer.Ordinal.Equals(importBinding.ExportKey,
                        binding.ExportKey)
                    || importBinding.TargetRequirement
                        != binding.TargetRequirement
                    || importBinding.RuntimePolicy != binding.RuntimePolicy
                    || moduleRefRequirements
                        != (binding.TargetRequirement, binding.RuntimePolicy)
                    || !expectationsByImport.TryAdd((
                        binding.UsingModule.ModuleIdentity,
                        binding.UsingModule.ModuleRow, binding.ImportRow),
                        expectation))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class Import view no longer matches its using Module.");
                }

                var expectedView = CreateStandardClassTypeView(usingTables,
                    expectation.ExpectedDeclaration.Identity,
                    usingSnapshot, expectedTypeDefRow);
                if (!StandardClassViewsMatch(
                        expectation.ExpectedDeclaration, expectedView))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class expectation no longer matches its TypeDef.");
                }

                foreach (var methodExpectation
                    in expectation.MethodExpectations)
                {
                    if (methodExpectation.OwnerImportRow != binding.ImportRow
                        || methodExpectation.ExpectedOwnerDefinitionId
                            != binding.ExpectedDefinitionId
                        || methodExpectation.ProviderOwnerDefinitionId
                            != binding.ProviderDefinitionId
                        || !SourceTypeSnapshotsMatch(
                            methodExpectation.Provider, binding.Provider)
                        || !methodExpectationsByImport.TryAdd((
                            binding.UsingModule.ModuleIdentity,
                            binding.UsingModule.ModuleRow,
                            methodExpectation.ImportRow),
                            (expectation, methodExpectation)))
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef Import has an invalid owner or duplicate expectation.");
                    }
                }

                var providerModule = modules.Single(module =>
                    StringComparer.Ordinal.Equals(module.Identity,
                        binding.ProducerIdentity)
                    && module.ModuleRow == binding.Provider.ModuleRow);
                var providerTables = GetC1M1Tables(artifacts,
                    providerModule.ArtifactIndex,
                    schemaIdentities[providerModule.ArtifactIndex]);
                EnsureC1ClassDeclarationTablesSupported(providerTables);
                var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                    providerTables, artifacts[providerModule.ArtifactIndex],
                    schemaIdentities[providerModule.ArtifactIndex],
                    providerModule.ModuleRow);
                var providerTypeDefRow
                    = GetTypeDefRow(binding.ProviderDefinitionId);
                if (!SourceTypeSnapshotsMatch(binding.Provider,
                        providerSnapshot)
                    || providerTables.TypeDefs.GetOwnerModule(
                        providerTypeDefRow) != providerModule.ModuleRow
                    || providerTables.TypeDefs.GetKind(providerTypeDefRow) != 2
                    || !providerTables.Exports.TryGetDefinitionForKey(
                        providerModule.ModuleRow, binding.ExportKey,
                        out var providerDefinition)
                    || providerDefinition != binding.ProviderDefinitionId)
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class provider selection no longer matches its Module and ExportKey.");
                }

                var providerIdentity = new BoundGenericNominalTypeIdentity(
                    providerModule.Identity, providerModule.Identity,
                    binding.ProviderDefinitionId);
                var actualDeclaration = program.StandardClassDeclarations
                    .SingleOrDefault(candidate =>
                        candidate.Declaration.Identity == providerIdentity);
                if (actualDeclaration is null
                    || !SourceTypeSnapshotsMatch(
                        actualDeclaration.Declaration.Provider,
                        providerSnapshot))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class provider declaration is missing.");
                }

                var actualView = CreateStandardClassTypeView(providerTables,
                    providerIdentity, providerSnapshot,
                    providerTypeDefRow);
                if (!StandardClassViewsMatch(
                        actualDeclaration.Declaration, actualView)
                    || !GetStandardTypeExportKeys(providerTables,
                            providerModule.ModuleRow, providerTypeDefRow)
                        .AsSpan().SequenceEqual(
                            actualDeclaration.ExportKeys.AsSpan()))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class provider view no longer matches its TypeDef.");
                }
            }
        }

        var boundImports = new HashSet<(
            string ModuleIdentity, int ModuleRow, int ImportRow)>();
        foreach (var binding in typeProviderBindings)
        {
            var consumerModule = modules.Single(module =>
                module.ArtifactIndex == binding.ConsumerArtifactIndex
                && module.ModuleRow == binding.ConsumerModuleRow);
            var consumerTables = GetC1M1Tables(artifacts,
                consumerModule.ArtifactIndex,
                schemaIdentities[consumerModule.ArtifactIndex]);
            if (consumerTables.TypeDefs.GetKind(
                    binding.ExpectedTypeDefRow) != 2)
            {
                continue;
            }

            var providerModule = modules.Single(module =>
                module.ArtifactIndex == binding.ProviderArtifactIndex
                && module.ModuleRow == binding.ProviderModuleRow);
            var key = (consumerModule.Identity, binding.ConsumerModuleRow,
                binding.ImportRow);
            if (!expectationsByImport.TryGetValue(key, out var expectation)
                || !boundImports.Add(key)
                || binding.ExpectedTypeDefRow
                    != GetTypeDefRow(
                        expectation.Binding.ExpectedDefinitionId)
                || binding.ProviderTypeDefRow
                    != GetTypeDefRow(
                        expectation.Binding.ProviderDefinitionId)
                || binding.ProviderArtifactIndex
                    != providerModule.ArtifactIndex
                || !StringComparer.Ordinal.Equals(binding.ProducerIdentity,
                    expectation.Binding.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(binding.ExportKey,
                    expectation.Binding.ExportKey)
                || binding.TargetRequirement
                    != expectation.Binding.TargetRequirement
                || binding.RuntimePolicy
                    != expectation.Binding.RuntimePolicy)
            {
                throw new InvalidDataException(
                    "C1M1 class provider imports do not match every retained expectation.");
            }
        }

        if (boundImports.Count != expectationsByImport.Count)
        {
            throw new InvalidDataException(
                "A retained C1M1 class Import expectation has no selected provider binding.");
        }

        var boundMethodImports = new HashSet<(
            string ModuleIdentity, int ModuleRow, int ImportRow)>();
        foreach (var binding in methodProviderBindings)
        {
            var consumerModule = modules.Single(module =>
                module.ArtifactIndex == binding.ConsumerArtifactIndex
                && module.ModuleRow == binding.ConsumerModuleRow);
            var consumerTables = GetC1M1Tables(artifacts,
                consumerModule.ArtifactIndex,
                schemaIdentities[consumerModule.ArtifactIndex]);
            var key = (consumerModule.Identity, binding.ConsumerModuleRow,
                binding.ImportRow);
            if (!methodExpectationsByImport.TryGetValue(key,
                    out var retained)
                || !boundMethodImports.Add(key)
                || binding.ExpectedMethodDefRow
                    != GetMethodDefRow(
                        retained.Method.ExpectedDefinitionId)
                || binding.ExpectedOwnerTypeDefRow
                    != GetTypeDefRow(
                        retained.Method.ExpectedOwnerDefinitionId)
                || binding.OwnerImportRow
                    != retained.Method.OwnerImportRow
                || binding.ProviderMethodDefRow
                    != GetMethodDefRow(
                        retained.Method.ProviderDefinitionId)
                || binding.ProviderTypeDefRow
                    != GetTypeDefRow(
                        retained.Method.ProviderOwnerDefinitionId)
                || !StringComparer.Ordinal.Equals(
                    binding.ProducerIdentity,
                    retained.Method.Provider.ModuleIdentity)
                || !StringComparer.Ordinal.Equals(binding.ExportKey,
                    retained.Method.ExportKey)
                || binding.TargetRequirement
                    != retained.Method.Provider.TargetRequirement
                || binding.RuntimePolicy
                    != retained.Method.Provider.RuntimePolicy)
            {
                throw new InvalidDataException(
                    "C1M1 class MethodDef Imports do not match every retained expectation.");
            }

            var expectedMethodRow = binding.ExpectedMethodDefRow;
            var expectedMethod = retained.Owner.ExpectedDeclaration.Methods
                .SingleOrDefault(method =>
                    method.DefinitionId
                        == retained.Method.ExpectedDefinitionId);
            var expectedModuleRefRow = consumerTables.Imports
                .GetModuleRefForImportRow(binding.ImportRow);
            var importBinding = consumerTables.Imports
                .GetBindingForImportRow(binding.ImportRow);
            var moduleRefRequirements = consumerTables.ModuleRefs
                .GetRequirements(expectedModuleRefRow);
            if (expectedMethod is null || expectedMethod.BodyRow != 0
                || consumerTables.Methods.GetOwnerType(expectedMethodRow)
                    != binding.ExpectedOwnerTypeDefRow
                || consumerTables.Imports
                    .GetExpectedDefinitionForImportRow(binding.ImportRow)
                    != retained.Method.ExpectedDefinitionId
                || binding.ModuleRefRow != expectedModuleRefRow
                || !StringComparer.Ordinal.Equals(importBinding.ExportKey,
                    retained.Method.ExportKey)
                || !StringComparer.Ordinal.Equals(
                    importBinding.ProducerIdentity,
                    retained.Method.Provider.ModuleIdentity)
                || importBinding.TargetRequirement
                    != retained.Method.Provider.TargetRequirement
                || importBinding.RuntimePolicy
                    != retained.Method.Provider.RuntimePolicy
                || moduleRefRequirements
                    != (retained.Method.Provider.TargetRequirement,
                        retained.Method.Provider.RuntimePolicy))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef expectation no longer matches its bodyless Import row.");
            }

            var providerModule = modules.Single(module =>
                StringComparer.Ordinal.Equals(module.Identity,
                    retained.Method.Provider.ModuleIdentity)
                && module.ModuleRow == retained.Method.Provider.ModuleRow);
            var providerTables = GetC1M1Tables(artifacts,
                providerModule.ArtifactIndex,
                schemaIdentities[providerModule.ArtifactIndex]);
            var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                providerTables, artifacts[providerModule.ArtifactIndex],
                schemaIdentities[providerModule.ArtifactIndex],
                providerModule.ModuleRow);
            var providerMethodRow = binding.ProviderMethodDefRow;
            var providerMethodDefinitionId = 0x1600_0000u
                | checked((uint)providerMethodRow);
            if (!SourceTypeSnapshotsMatch(retained.Method.Provider,
                    providerSnapshot)
                || binding.ProviderArtifactIndex
                    != providerModule.ArtifactIndex
                || binding.ProviderModuleRow != providerModule.ModuleRow
                || providerTables.TypeDefs.GetOwnerModule(
                    binding.ProviderTypeDefRow) != providerModule.ModuleRow
                || providerTables.TypeDefs.GetKind(
                    binding.ProviderTypeDefRow) != 2
                || providerTables.Methods.GetOwnerType(providerMethodRow)
                    != binding.ProviderTypeDefRow
                || !providerTables.Exports.TryGetDefinitionForKey(
                    providerModule.ModuleRow, retained.Method.ExportKey,
                    out var providerDefinition)
                || providerDefinition != providerMethodDefinitionId)
            {
                throw new InvalidDataException(
                    "A retained class MethodDef provider selection no longer matches its Module and ExportKey.");
            }

            var providerIdentity = new BoundGenericNominalTypeIdentity(
                providerModule.Identity, providerModule.Identity,
                retained.Method.ProviderOwnerDefinitionId);
            var actualDeclaration = program.StandardClassDeclarations
                .SingleOrDefault(candidate =>
                    candidate.Declaration.Identity == providerIdentity);
            var actualMethod = actualDeclaration?.Declaration.Methods
                .SingleOrDefault(method =>
                    method.DefinitionId == providerMethodDefinitionId);
            if (actualMethod is null
                || actualMethod.BodyRow == 0
                || actualMethod.GenericParameterCount != 0
                || actualMethod.ConstructionRole != 0
                || !actualMethod.ExportKeys.Contains(
                    retained.Method.ExportKey, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef provider view is missing its selected method.");
            }
        }

        if (boundMethodImports.Count != methodExpectationsByImport.Count)
        {
            throw new InvalidDataException(
                "A retained class MethodDef Import expectation has no selected provider binding.");
        }
    }

    private static void ValidateC1M1StructCarrierBindings(
        BoundProgram program,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        byte[][] artifacts,
        string[] schemaIdentities,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        var expectationsByImport = new Dictionary<(
            string ModuleIdentity, int ModuleRow, int ImportRow),
            BoundSourceTypeExpectation>();
        foreach (var definition in program.GenericNominalTypeDefinitions)
        {
            if (definition.NominalKind != 1)
            {
                continue;
            }

            foreach (var expectation in definition.ProviderExpectations)
            {
                var binding = expectation;
                var key = (binding.UsingModule.ModuleIdentity,
                    binding.UsingModule.ModuleRow, binding.ImportRow);
                if (!expectationsByImport.TryAdd(key, binding))
                {
                    throw new InvalidDataException(
                        "A C1M1 struct Import has more than one retained expectation.");
                }
            }
        }

        var boundImports = new HashSet<(
            string ModuleIdentity, int ModuleRow, int ImportRow)>();
        foreach (var binding in typeProviderBindings)
        {
            var consumerModule = modules.Single(module =>
                module.ArtifactIndex == binding.ConsumerArtifactIndex
                && module.ModuleRow == binding.ConsumerModuleRow);
            var consumerTables = GetC1M1Tables(artifacts,
                consumerModule.ArtifactIndex,
                schemaIdentities[consumerModule.ArtifactIndex]);
            if (consumerTables.TypeDefs.GetKind(
                    binding.ExpectedTypeDefRow) != 1)
            {
                continue;
            }

            var key = (consumerModule.Identity,
                binding.ConsumerModuleRow, binding.ImportRow);
            if (!expectationsByImport.TryGetValue(key, out var expectation)
                || !boundImports.Add(key)
                || binding.ExpectedTypeDefRow
                    != GetTypeDefRow(expectation.ExpectedDefinitionId)
                || binding.ProviderTypeDefRow
                    != GetTypeDefRow(expectation.ProviderDefinitionId)
                || !StringComparer.Ordinal.Equals(
                    binding.ProducerIdentity, expectation.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(binding.ExportKey,
                    expectation.ExportKey)
                || binding.TargetRequirement
                    != expectation.TargetRequirement
                || binding.RuntimePolicy != expectation.RuntimePolicy)
            {
                throw new InvalidDataException(
                    "C1M1 struct provider imports do not match every retained expectation.");
            }

            var usingSnapshot = CreateA2SourceTypeProviderSnapshot(
                consumerTables, artifacts[consumerModule.ArtifactIndex],
                schemaIdentities[consumerModule.ArtifactIndex],
                consumerModule.ModuleRow);
            var providerModule = modules.Single(module =>
                StringComparer.Ordinal.Equals(module.Identity,
                    expectation.ProducerIdentity)
                && module.ModuleRow == expectation.Provider.ModuleRow);
            var providerTables = GetC1M1Tables(artifacts,
                providerModule.ArtifactIndex,
                schemaIdentities[providerModule.ArtifactIndex]);
            var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                providerTables, artifacts[providerModule.ArtifactIndex],
                schemaIdentities[providerModule.ArtifactIndex],
                providerModule.ModuleRow);
            var expectedTypeDefRow
                = GetTypeDefRow(expectation.ExpectedDefinitionId);
            var providerTypeDefRow
                = GetTypeDefRow(expectation.ProviderDefinitionId);
            var importModuleRefRow = consumerTables.Imports
                .GetModuleRefForImportRow(expectation.ImportRow);
            var importBinding = consumerTables.Imports
                .GetBindingForImportRow(expectation.ImportRow);
            if (!SourceTypeSnapshotsMatch(expectation.UsingModule,
                    usingSnapshot)
                || !SourceTypeSnapshotsMatch(expectation.Provider,
                    providerSnapshot)
                || consumerTables.TypeDefs.GetOwnerModule(
                    expectedTypeDefRow) != consumerModule.ModuleRow
                || consumerTables.TypeDefs.GetKind(expectedTypeDefRow) != 1
                || providerTables.TypeDefs.GetOwnerModule(
                    providerTypeDefRow) != providerModule.ModuleRow
                || providerTables.TypeDefs.GetKind(providerTypeDefRow) != 1
                || consumerTables.Imports
                    .GetExpectedDefinitionForImportRow(expectation.ImportRow)
                    != expectation.ExpectedDefinitionId
                || !StringComparer.Ordinal.Equals(
                    importBinding.ProducerIdentity,
                    expectation.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(importBinding.ExportKey,
                    expectation.ExportKey)
                || importBinding.TargetRequirement
                    != expectation.TargetRequirement
                || importBinding.RuntimePolicy
                    != expectation.RuntimePolicy
                || consumerTables.ModuleRefs.GetRequirements(
                    importModuleRefRow)
                    != (expectation.TargetRequirement,
                        expectation.RuntimePolicy)
                || !providerTables.Exports.TryGetDefinitionForKey(
                    providerModule.ModuleRow, expectation.ExportKey,
                    out var actualDefinition)
                || actualDefinition != expectation.ProviderDefinitionId)
            {
                throw new InvalidDataException(
                    "A retained C1M1 struct Import no longer matches its using and provider Modules.");
            }
        }

        if (boundImports.Count != expectationsByImport.Count)
        {
            throw new InvalidDataException(
                "A retained C1M1 struct Import has no selected provider binding.");
        }
    }

    private static TrRmStructuralMetadataValidator.ValidatedExecutionTables
        GetC1M1Tables(byte[][] artifacts, int artifactIndex,
            string schemaIdentity)
    {
        if ((uint)artifactIndex >= (uint)artifacts.Length)
        {
            throw new InvalidDataException(
                "A C1M1 class provider artifact index is out of range.");
        }

        var schema = GetC1M1ClassMetadataSchema(schemaIdentity);
        return TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(artifacts[artifactIndex], schema));
    }

    private static byte[] GetTargetProfileSection(TrRmArtifactIndex index,
        out int recordCount)
    {
        if (index.TryGetSection(0x0003, out var bytes, out recordCount))
        {
            return bytes.ToArray();
        }

        recordCount = 0;
        return [];
    }

    private static byte[] RemapC1M1ClassMethodBody(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables sourceTables,
        ReadOnlyMemory<byte> bodyBytes, Dictionary<int, int> typeDefRowMap,
        Dictionary<int, int> methodDefRowMap,
        Dictionary<int, int> fieldRowMap, Dictionary<int, int> importRowMap,
        Dictionary<int, int> fieldPathRowMap,
        List<TrRmFieldPathRow> fieldPathRows,
        Dictionary<C1M1FieldPathIdentity, int> fieldPathRowsByIdentity,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        List<ReadOnlyMemory<byte>> outputTypeSpecRows,
        Dictionary<int, uint> typeSpecRowMap,
        HashSet<int> activeTypeSpecRows, TrRmArtifactSchema schema)
    {
        var body = new TrRmRegisterBodyReader(bodyBytes.Span,
            sourceTables.Contracts.Count, schema);

        var typeMap = body.TypeMap.ToArray();
        for (var typeMapIndex = 0; typeMapIndex < body.TypeCount;
            typeMapIndex++)
        {
            var offset = typeMapIndex * sizeof(uint);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                typeMap.AsSpan(offset));
            var typeSpecReference = (byte)(token >> 24) switch
            {
                0x11 => token & 0x00FF_FFFF,
                0xF2 or 0xF3 => token,
                _ => throw Unsupported(
                    "CM1 local class MethodDef Body TypeMap entries must be inline or TypeSpec-backed."),
            };
            var remappedTypeSpec = RemapC1M1ClassTypeSpec(sourceTables,
                typeSpecReference, typeDefRowMap, importRowMap,
                outputTypeSpecRows, typeSpecRowMap, activeTypeSpecRows,
                depth: 0);
            BinaryPrimitives.WriteUInt32LittleEndian(typeMap.AsSpan(offset),
                CreateStandardBodyTypeMapToken(remappedTypeSpec));
        }

        int RemapFieldPath(int sourceRow)
        {
            if (fieldPathRowMap.TryGetValue(sourceRow,
                    out var existingRow))
            {
                return existingRow;
            }

            var sourceRootTypeSpec = sourceTables.FieldPaths
                .GetRootTypeSpecRef(sourceRow);
            var rootTypeSpec = RemapC1M1ClassTypeSpec(sourceTables,
                sourceRootTypeSpec, typeDefRowMap, importRowMap,
                outputTypeSpecRows, typeSpecRowMap, activeTypeSpecRows,
                depth: 0);
            var fieldCount = sourceTables.FieldPaths.GetFieldCount(
                sourceRow);
            var fieldReferences = new uint[fieldCount];
            for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                var token = sourceTables.FieldPaths.GetFieldReference(
                    sourceRow, fieldIndex);
                var row = checked((int)(token & 0x00FF_FFFF));
                fieldReferences[fieldIndex] = (byte)(token >> 24) switch
                {
                    0x14 when fieldRowMap.TryGetValue(row,
                        out var localFieldRow)
                        => 0x1400_0000u
                            | checked((uint)localFieldRow),
                    0x1C when importRowMap.TryGetValue(row,
                        out var fieldImportRow)
                        => 0x1C00_0000u
                            | checked((uint)fieldImportRow),
                    _ => throw Unsupported(
                        "CM1 local class MethodDef Body FieldPath steps must target retained FieldDefs or FieldDef Imports."),
                };
            }

            var identity = new C1M1FieldPathIdentity(rootTypeSpec,
                ImmutableArray.CreateRange(fieldReferences));
            if (!fieldPathRowsByIdentity.TryGetValue(identity,
                    out var outputRow))
            {
                if (fieldPathRows.Count >= 0xFF_FFFF)
                {
                    throw Unsupported(
                        "CM1 local class MethodDef Bodies exceed the FieldPath row limit.");
                }

                outputRow = checked(fieldPathRows.Count + 1);
                fieldPathRows.Add(new TrRmFieldPathRow(rootTypeSpec,
                    fieldReferences));
                fieldPathRowsByIdentity.Add(identity, outputRow);
            }

            fieldPathRowMap.Add(sourceRow, outputRow);
            return outputRow;
        }

        var methodMap = body.MethodMap.ToArray();
        for (var methodMapIndex = 0; methodMapIndex < body.MethodCount;
            methodMapIndex++)
        {
            var offset = methodMapIndex * sizeof(uint);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                methodMap.AsSpan(offset));
            var row = checked((int)(token & 0x00FF_FFFF));
            var remappedRow = (byte)(token >> 24) switch
            {
                0x16 when methodDefRowMap.TryGetValue(row,
                    out var localMethodRow) => localMethodRow,
                0x1C when importRowMap.TryGetValue(row,
                    out var importRow) => importRow,
                _ => throw Unsupported(
                    "CM1 local class MethodDef Body MethodMap targets must be retained local or imported MethodDefs.")
            };

            BinaryPrimitives.WriteUInt32LittleEndian(methodMap.AsSpan(offset),
                (token & 0xFF00_0000u) | checked((uint)remappedRow));
        }

        var fieldMap = body.FieldMap.ToArray();
        for (var fieldMapIndex = 0; fieldMapIndex < body.FieldCount;
            fieldMapIndex++)
        {
            var offset = fieldMapIndex * sizeof(uint);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                fieldMap.AsSpan(offset));
            var row = checked((int)(token & 0x00FF_FFFF));
            var remappedToken = (byte)(token >> 24) switch
            {
                0x14 when fieldRowMap.TryGetValue(row,
                    out var localFieldRow)
                    => 0x1400_0000u | checked((uint)localFieldRow),
                0x1C when importRowMap.TryGetValue(row,
                    out var fieldImportRow)
                    => 0x1C00_0000u | checked((uint)fieldImportRow),
                0x1D => 0x1D00_0000u
                    | checked((uint)RemapFieldPath(row)),
                _ => throw Unsupported(
                    "CM1 local class MethodDef Body FieldMap targets must be retained local FieldDefs, FieldDef Imports or FieldPaths."),
            };

            BinaryPrimitives.WriteUInt32LittleEndian(fieldMap.AsSpan(offset),
                remappedToken);
        }

        var constantMap = body.ConstantMap.ToArray();
        for (var constantMapIndex = 0;
            constantMapIndex < body.ConstantCount; constantMapIndex++)
        {
            var offset = constantMapIndex * 2 * sizeof(uint);
            var typeSpecReference = BinaryPrimitives
                .ReadUInt32LittleEndian(constantMap.AsSpan(offset));
            var type = sourceTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            if (type.IsRow || type.Form != 0x01 || type.Qualifiers != 0
                || !TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                    type.TypeCode, out var primitiveType))
            {
                throw Unsupported(
                    "CM1 local class MethodDef Body ConstantMap entries require an inline scalar TypeSpec.");
            }

            var sourceRow = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(constantMap.AsSpan(
                    offset + sizeof(uint))));
            if (sourceTables.ConstantSources.GetKind(sourceRow) != 1)
            {
                throw Unsupported(
                    "CM1 local class MethodDef Body ConstantMap entries require frozen scalar ConstantSources.");
            }

            var valueNodeRow = checked((int)sourceTables.ConstantSources
                .GetPayloadReference(sourceRow));
            if (sourceTables.FrozenValueNodes.GetTypeSpecRef(valueNodeRow)
                != typeSpecReference)
            {
                throw new InvalidDataException(
                    "A retained class MethodDef Body ConstantMap changed its frozen scalar type.");
            }

            var payload = sourceTables.FrozenValueNodes.GetScalarPayload(
                valueNodeRow, sourceTables.Blobs);
            var bits = ScalarBits.FromPayload(primitiveType, payload);
            var remappedConstant = frozenScalarValueBuilder.Add(
                new BoundConstant(bits, primitiveType, default));
            if (remappedConstant.TypeSpecRef != typeSpecReference)
            {
                throw new InvalidDataException(
                    "A retained class MethodDef Body ConstantMap changed its exact scalar TypeSpec.");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset), remappedConstant.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                remappedConstant.ConstantSourceRow);
        }

        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks[blockIndex] = new(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(
                    block.Slice(sizeof(uint))),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    block.Slice(2 * sizeof(uint))));
        }

        var records = new ReadOnlyMemory<byte>[body.RecordCount];
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if ((record[0] == 0x03
                    && BinaryPrimitives.ReadUInt32LittleEndian(
                        record.Slice(16)) != 0)
                || (record[0] == 0x05
                    && BinaryPrimitives.ReadUInt32LittleEndian(
                        record.Slice(12)) != 0))
            {
                throw Unsupported(
                    "CM1 local class MethodDef Bodies with contract-row references are not supported.");
            }

            records[recordIndex] = record.ToArray();
        }

        return TrRmRegisterBodyWriter.Encode(body.HomeMap.ToArray(),
            typeMap, methodMap, fieldMap,
            constantMap, blocks, records,
            body.Code.ToArray(), contractCount: 0, schema: schema);
    }

    private static uint RemapC1M1ClassTypeSpec(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables sourceTables,
        uint typeSpecReference, Dictionary<int, int> typeDefRowMap,
        Dictionary<int, int> importRowMap,
        List<ReadOnlyMemory<byte>> outputRows,
        Dictionary<int, uint> rowMap, HashSet<int> activeRows, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw Unsupported(
                "C1M1 class TypeSpec remapping exceeds the type nesting limit.");
        }

        var reference = sourceTables.TypeSpecs.GetTypeSpecRef(
            typeSpecReference);
        if (!reference.IsRow)
        {
            return typeSpecReference;
        }
        if (rowMap.TryGetValue(reference.Row, out var existingReference))
        {
            return existingReference;
        }
        if (!activeRows.Add(reference.Row))
        {
            throw new InvalidDataException(
                "C1M1 type dependency TypeSpec rows contain a cycle.");
        }

        try
        {
            var row = sourceTables.TypeSpecs.GetRow(reference.Row).ToArray();
            switch (reference.Form)
            {
                case 0x01:
                    break;

                case 0x02:
                {
                    var oldTypeDefRow = checked((int)
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            row.AsSpan(2)));
                    if (!typeDefRowMap.TryGetValue(oldTypeDefRow,
                            out var newTypeDefRow))
                    {
                        throw Unsupported(
                            "A class field refers to an unretained local TypeDef.");
                    }

                    Write(row, 2, checked((uint)newTypeDefRow));
                    break;
                }

                case 0x03:
                {
                    var oldImportRow = checked((int)
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            row.AsSpan(2)));
                    if (!importRowMap.TryGetValue(oldImportRow,
                            out var newImportRow))
                    {
                        throw Invalid(
                            "A class field TypeSpec refers to an unretained Import.");
                    }

                    Write(row, 2, checked((uint)newImportRow));
                    break;
                }

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                {
                    var child = BinaryPrimitives.ReadUInt32LittleEndian(
                        row.AsSpan(2));
                    var remappedChild = RemapC1M1ClassTypeSpec(
                        sourceTables, child, typeDefRowMap, importRowMap,
                        outputRows, rowMap, activeRows, depth + 1);
                    Write(row, 2, remappedChild);
                    break;
                }

                case 0x0C:
                {
                    var child = BinaryPrimitives.ReadUInt32LittleEndian(
                        row.AsSpan(3));
                    var remappedChild = RemapC1M1ClassTypeSpec(
                        sourceTables, child, typeDefRowMap, importRowMap,
                        outputRows, rowMap, activeRows, depth + 1);
                    Write(row, 3, remappedChild);
                    break;
                }

                default:
                    throw Unsupported(
                        "C1M1 type dependency TypeSpec remapping does not support this TypeSpec form.");
            }

            outputRows.Add(row);
            var newReference = checked((uint)outputRows.Count);
            rowMap.Add(reference.Row, newReference);
            return newReference;
        }
        finally
        {
            _ = activeRows.Remove(reference.Row);
        }
    }

    private static C1M1TypeDefEmissionView ToC1M1TypeDefEmissionView(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int typeDefRow, BoundStandardClassTypeView view)
        => new(tables.TypeDefs.GetKind(typeDefRow), view.Namespace,
            view.Name, view.TypeDefFlags, view.Fields, view.Layouts);

    private static C1M1TypeDefEmissionView CreateC1M1TypeDefEmissionView(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        BoundSourceTypeProviderSnapshot provider, int typeDefRow)
    {
        var identity = new BoundGenericNominalTypeIdentity(
            provider.ModuleIdentity, provider.ModuleIdentity,
            0x1300_0000u | checked((uint)typeDefRow));
        var classView = CreateStandardClassTypeView(tables, identity,
            provider, typeDefRow);
        return ToC1M1TypeDefEmissionView(tables, typeDefRow, classView);
    }

    private static void AddC1M1LocalNonGenericTypeClosure(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables rootTables,
        int rootModuleRow, BoundSourceTypeProviderSnapshot rootSnapshot,
        SortedDictionary<int, C1M1TypeDefEmissionView>
            typeDefViewsByOldRow,
        Dictionary<int, ImmutableArray<string>> exportsByOldRow)
    {
        var pendingTypeDefRows = new Queue<int>(typeDefViewsByOldRow.Keys);
        var pendingTypeSpecs = new Stack<(uint Reference, int Depth)>();
        var visitedTypeSpecs = new HashSet<int>();
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pendingTypeDefRows.TryDequeue(out var typeDefRow))
        {
            if (--remaining < 0)
            {
                throw Unsupported(
                    "C1M1 class dependency closure exceeds the metadata traversal limit.");
            }

            var view = typeDefViewsByOldRow[typeDefRow];
            foreach (var field in view.Fields)
            {
                if (--remaining < 0)
                {
                    throw Unsupported(
                        "C1M1 class dependency closure exceeds the metadata traversal limit.");
                }

                pendingTypeSpecs.Push((field.TypeSpecReference, 1));
                while (pendingTypeSpecs.TryPop(out var pendingTypeSpec))
                {
                    if (--remaining < 0
                        || pendingTypeSpec.Depth
                            > SemanticVerifier.MaximumDepth)
                    {
                        throw Unsupported(
                            "C1M1 class TypeSpec dependency closure exceeds its traversal limit.");
                    }

                    var reference = rootTables.TypeSpecs.GetTypeSpecRef(
                        pendingTypeSpec.Reference);
                    if (!reference.IsRow
                        || !visitedTypeSpecs.Add(reference.Row))
                    {
                        continue;
                    }

                    var row = rootTables.TypeSpecs.GetRow(reference.Row);
                    switch (reference.Form)
                    {
                        case 0x01:
                        case 0x03:
                            break;

                        case 0x02:
                        {
                            var referencedTypeDefRow = checked((int)
                                BinaryPrimitives.ReadUInt32LittleEndian(
                                    row[2..]));
                            if (rootTables.TypeDefs.GetOwnerModule(
                                    referencedTypeDefRow)
                                != rootModuleRow)
                            {
                                throw new InvalidDataException(
                                    "A C1M1 class TypeSpec directly references a TypeDef owned by another Module.");
                            }
                            var kind = rootTables.TypeDefs.GetKind(
                                referencedTypeDefRow);
                            if (kind is not (1 or 2))
                            {
                                throw Unsupported(
                                    "C1M1 class re-emission does not yet retain local interface TypeDef dependencies.");
                            }

                            if (rootTables.TypeDefs.GetGenericRange(
                                    referencedTypeDefRow).Count != 0
                                || rootTables.TypeDefs.GetContractRange(
                                    referencedTypeDefRow).Count != 0)
                            {
                                throw Unsupported(
                                    "C1M1 class re-emission does not yet retain generic or contracted local TypeDef dependencies.");
                            }

                            if (!typeDefViewsByOldRow.ContainsKey(
                                    referencedTypeDefRow))
                            {
                                var dependencyView
                                    = CreateC1M1TypeDefEmissionView(
                                        rootTables, rootSnapshot,
                                        referencedTypeDefRow);
                                if (dependencyView.Kind != kind
                                    || !typeDefViewsByOldRow.TryAdd(
                                        referencedTypeDefRow,
                                        dependencyView)
                                    || !exportsByOldRow.TryAdd(
                                        referencedTypeDefRow, []))
                                {
                                    throw new InvalidDataException(
                                        "A C1M1 local TypeDef dependency was retained more than once.");
                                }

                                pendingTypeDefRows.Enqueue(
                                    referencedTypeDefRow);
                            }

                            break;
                        }

                        case >= 0x06 and <= 0x0B:
                        case 0x0C:
                        case 0x0E:
                        {
                            var childOffset = reference.Form == 0x0C
                                ? 3 : 2;
                            var child = BinaryPrimitives
                                .ReadUInt32LittleEndian(
                                    row[childOffset..]);
                            pendingTypeSpecs.Push((child,
                                checked(pendingTypeSpec.Depth + 1)));
                            break;
                        }

                        default:
                            throw Unsupported(
                                "C1M1 class TypeSpec dependency closure does not support this TypeSpec form.");
                    }
                }
            }
        }
    }

    private static TrRmImportRow[] CreateC1M1ClassImports(
        SortedDictionary<int, BoundSourceTypeExpectation> bindingsByImportRow,
        SortedDictionary<int, TrRmStandardFieldProviderBinding>
            fieldBindingsByImportRow,
        SortedDictionary<int, BoundStandardClassMethodExpectation>
            methodBindingsByImportRow,
        Dictionary<int, int> importRowMap,
        Dictionary<int, int> moduleRefRowMap,
        Dictionary<int, int> typeDefRowMap,
        Dictionary<int, int> fieldRowMap,
        Dictionary<int, int> methodDefRowMap,
        Dictionary<string, int> stringRows,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables sourceTables)
    {
        var rows = new TrRmImportRow[checked(bindingsByImportRow.Count
            + fieldBindingsByImportRow.Count
            + methodBindingsByImportRow.Count)];
        foreach (var oldImportRow in bindingsByImportRow.Keys
            .Concat(fieldBindingsByImportRow.Keys)
            .Concat(methodBindingsByImportRow.Keys).Order())
        {
            var newImportRow = importRowMap[oldImportRow];
            var oldModuleRefRow = sourceTables.Imports
                .GetModuleRefForImportRow(oldImportRow);
            var newModuleRefRow = moduleRefRowMap[oldModuleRefRow];
            if (bindingsByImportRow.TryGetValue(oldImportRow,
                    out var typeBinding))
            {
                var expectedOldTypeDefRow
                    = GetTypeDefRow(typeBinding.ExpectedDefinitionId);
                var expectedNewTypeDefRow
                    = typeDefRowMap[expectedOldTypeDefRow];
                rows[newImportRow - 1] = new TrRmImportRow(
                    newModuleRefRow,
                    stringRows[typeBinding.ExportKey],
                    0x1300_0000u | checked((uint)expectedNewTypeDefRow));
                continue;
            }

            if (fieldBindingsByImportRow.TryGetValue(oldImportRow,
                    out var fieldBinding))
            {
                rows[newImportRow - 1] = new TrRmImportRow(
                    newModuleRefRow,
                    stringRows[fieldBinding.ExportKey],
                    0x1400_0000u | checked((uint)fieldRowMap[
                        fieldBinding.ExpectedFieldDefRow]));
                continue;
            }

            var methodBinding
                = methodBindingsByImportRow[oldImportRow];
            var expectedOldMethodDefRow
                = GetMethodDefRow(methodBinding.ExpectedDefinitionId);
            rows[newImportRow - 1] = new TrRmImportRow(
                newModuleRefRow,
                stringRows[methodBinding.ExportKey],
                0x1600_0000u | checked((uint)methodDefRowMap[
                    expectedOldMethodDefRow]));
        }

        return rows;
    }

    private static byte[] EncodeC1M1Exports(
        IReadOnlyList<(uint DefinitionId, string Key)> exports,
        Dictionary<string, int> stringRows)
    {
        var bytes = new byte[checked(exports.Count * 8)];
        for (var index = 0; index < exports.Count; index++)
        {
            var export = exports[index];
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * 8), export.DefinitionId);
            Write(bytes, (index * 8) + 4,
                checked((uint)stringRows[export.Key]));
        }

        return bytes;
    }

    private static bool SourceTypeSnapshotsMatch(
        BoundSourceTypeProviderSnapshot left,
        BoundSourceTypeProviderSnapshot right)
        => StringComparer.Ordinal.Equals(left.SchemaIdentity,
                right.SchemaIdentity)
            && left.ModuleRow == right.ModuleRow
            && StringComparer.Ordinal.Equals(left.ModuleIdentity,
                right.ModuleIdentity)
            && left.GoverningRuleEdition == right.GoverningRuleEdition
            && left.TargetRequirement == right.TargetRequirement
            && left.RuntimePolicy == right.RuntimePolicy
            && left.Features.AsSpan().SequenceEqual(right.Features.AsSpan())
            && left.TargetProfiles.AsSpan().SequenceEqual(
                right.TargetProfiles.AsSpan())
            && left.ArtifactSnapshot.AsSpan().SequenceEqual(
                right.ArtifactSnapshot.AsSpan());

    private static bool StandardClassViewsMatch(
        BoundStandardClassTypeView left,
        BoundStandardClassTypeView right)
    {
        if (left.Identity != right.Identity
            || !StringComparer.Ordinal.Equals(left.Namespace,
                right.Namespace)
            || !StringComparer.Ordinal.Equals(left.Name, right.Name)
            || left.TypeDefFlags != right.TypeDefFlags
            || !SourceTypeSnapshotsMatch(left.Provider, right.Provider)
            || left.Fields.Length != right.Fields.Length
            || left.Layouts.Length != right.Layouts.Length
            || left.Methods.Length != right.Methods.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Fields.Length; index++)
        {
            if (!StandardClassFieldsMatch(left.Fields[index],
                    right.Fields[index]))
            {
                return false;
            }
        }

        for (var index = 0; index < left.Layouts.Length; index++)
        {
            var leftLayout = left.Layouts[index];
            var rightLayout = right.Layouts[index];
            if (leftLayout.Row != rightLayout.Row
                || leftLayout.OwnerDefinitionId
                    != rightLayout.OwnerDefinitionId
                || leftLayout.TargetProfileReference
                    != rightLayout.TargetProfileReference
                || leftLayout.TargetProfileId
                    != rightLayout.TargetProfileId
                || leftLayout.TargetProfileRuleRevision
                    != rightLayout.TargetProfileRuleRevision
                || leftLayout.Kind != rightLayout.Kind
                || leftLayout.Flags != rightLayout.Flags
                || leftLayout.Alignment != rightLayout.Alignment
                || leftLayout.Pack != rightLayout.Pack
                || !leftLayout.FieldDefinitionIds.AsSpan()
                    .SequenceEqual(
                        rightLayout.FieldDefinitionIds.AsSpan()))
            {
                return false;
            }
        }

        for (var index = 0; index < left.Methods.Length; index++)
        {
            if (!StandardClassMethodsMatch(left.Methods[index],
                    right.Methods[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StandardClassFieldsMatch(
        BoundStandardClassFieldDeclaration left,
        BoundStandardClassFieldDeclaration right)
        => left.DefinitionId == right.DefinitionId
            && left.OwnerDefinitionId == right.OwnerDefinitionId
            && StringComparer.Ordinal.Equals(left.Name, right.Name)
            && left.Flags == right.Flags
            && left.TypeSpecReference == right.TypeSpecReference
            && left.Type == right.Type
            && left.ExportKeys.AsSpan().SequenceEqual(
                right.ExportKeys.AsSpan());

    private static bool StandardClassMethodsMatch(
        BoundStandardClassMethodDeclaration left,
        BoundStandardClassMethodDeclaration right)
    {
        if (left.DefinitionId != right.DefinitionId
            || !StringComparer.Ordinal.Equals(left.Name, right.Name)
            || left.Flags != right.Flags
            || left.EffectsMask != right.EffectsMask
            || left.ReturnTypeSpecReference
                != right.ReturnTypeSpecReference
            || left.ReceiverTypeSpecReference
                != right.ReceiverTypeSpecReference
            || left.GenericParameterCount != right.GenericParameterCount
            || left.BodyRow != right.BodyRow
            || left.ConstructionRole != right.ConstructionRole
            || left.InitializerMethodDefinitionId
                != right.InitializerMethodDefinitionId
            || left.ConstructionPropertyDefinitionId
                != right.ConstructionPropertyDefinitionId
            || !left.InitializedMemberDefinitionIds.AsSpan()
                .SequenceEqual(
                    right.InitializedMemberDefinitionIds.AsSpan())
            || !left.ExportKeys.AsSpan().SequenceEqual(
                right.ExportKeys.AsSpan())
            || !left.SelectedExportKeys.AsSpan().SequenceEqual(
                right.SelectedExportKeys.AsSpan())
            || left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Parameters.Length; index++)
        {
            var leftParameter = left.Parameters[index];
            var rightParameter = right.Parameters[index];
            if (!StringComparer.Ordinal.Equals(leftParameter.Name,
                    rightParameter.Name)
                || leftParameter.TypeSpecReference
                    != rightParameter.TypeSpecReference
                || leftParameter.Mode != rightParameter.Mode
                || leftParameter.Flags != rightParameter.Flags
                || !Equals(leftParameter.DefaultValue,
                    rightParameter.DefaultValue))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetMethodDefRow(uint definitionId)
    {
        if ((definitionId >> 24) != 0x16
            || (definitionId & 0x00FF_FFFF) == 0)
        {
            throw Invalid(
                "A C1M1 class reference has an invalid MethodDef identity.");
        }

        return checked((int)(definitionId & 0x00FF_FFFF));
    }

    private static bool TryGetC1M1ClassScalarType(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        uint typeSpecReference, out PrimitiveType type)
    {
        var typeSpec = tables.TypeSpecs.GetTypeSpecRef(typeSpecReference);
        type = PrimitiveType.Void;
        return typeSpec.Form == 1 && typeSpec.Row == 0
            && typeSpec.Qualifiers == 0
            && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                typeSpec.TypeCode, out type);
    }

    private static int GetTypeDefRow(uint definitionId)
    {
        if ((definitionId >> 24) != 0x13
            || (definitionId & 0x00FF_FFFF) == 0)
        {
            throw Invalid("A C1M1 type reference has an invalid TypeDef identity.");
        }

        return checked((int)(definitionId & 0x00FF_FFFF));
    }

    private static int GetFieldDefRow(uint definitionId)
    {
        if ((definitionId >> 24) != 0x14
            || (definitionId & 0x00FF_FFFF) == 0)
        {
            throw Invalid("A C1M1 type reference has an invalid FieldDef identity.");
        }

        return checked((int)(definitionId & 0x00FF_FFFF));
    }

    private static void Write(Span<byte> bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
}
