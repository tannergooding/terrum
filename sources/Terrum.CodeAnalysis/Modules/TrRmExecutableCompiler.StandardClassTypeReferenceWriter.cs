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
    private sealed record C1M1TypeDefEmissionView(
        byte Kind, string Namespace, string Name, uint TypeDefFlags,
        ImmutableArray<BoundStandardClassFieldDeclaration> Fields,
        ImmutableArray<BoundStandardClassLayoutVariant> Layouts);

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

    private readonly record struct C1M1ParameterSurfaceRow(
        int MethodDefRow, int Ordinal, string Name,
        uint DefaultConstantSourceRow);

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

        if (!ReferenceEquals(sourceClassExports.Program, program)
            || program.EntryPoint != -1
            || !program.StandardClassDeclarations.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.GenericMethodOperatorProtocols.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || (!schema.UsesClassParameterDefaults()
                && (!program.Interfaces.IsEmpty
                    || !program.GenericMethodDefinitions.IsEmpty))
        {
            throw Unsupported(
                "C1M1 source declaration emission requires captured local non-generic classes without imported class, generic or interface closure.");
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

        SemanticVerifier.VerifyLibraryReferences(program);
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
        var sourceStructDeclarations = program.SourceTypeDeclarations
            .ToDictionary(static declaration => declaration.StructureIndex);
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
                if (!IsC1M1SourceStructLayoutSupported(structure))
                {
                    throw Unsupported(
                        "C1M1 class declaration closure supports only non-generic local structs with auto layout and no ownership or cleanup contract.");
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
            exportKeysByClass.Count);
        var typeDefNames = new List<(string Namespace, string Name)>(
            exportKeysByClass.Count);
        var fieldRows = new List<TrRmFieldDefRow>();
        var fieldNames = new List<string>();
        var layoutRows = new List<TrRmLayoutRow>(
            exportKeysByClass.Count + retainedStructureIndexes.Count);
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
        var typeDefRowByStructure = retainedStructureIndexes
            .Order()
            .Select(static (structureIndex, ordinal) =>
                (structureIndex, typeDefRow: ordinal + 1))
            .ToDictionary(static item => item.structureIndex,
                item => item.typeDefRow + exportKeysByClass.Count);
        var typeDefOrder = exportKeysByClass.Keys
            .Select(static classIndex => (IsClass: true, Index: classIndex))
            .Concat(retainedStructureIndexes.Order().Select(
                static structureIndex => (IsClass: false,
                    Index: structureIndex)));

        uint FieldTypeSpecReference(PrimitiveType type)
        {
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
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
                        checked((uint)typeDefRow)));
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

        foreach (var (isClass, sourceIndex) in typeDefOrder)
        {
            if (isClass
                && program.Classes[sourceIndex].IsUnique is not false)
            {
                throw Unsupported(
                    "C1M1 class TypeDefs require known aliasable handle ownership.");
            }

            var declarationName = isClass
                ? program.Classes[sourceIndex].Name
                : program.Structures[sourceIndex].Name;
            var exportKeys = isClass
                ? exportKeysByClass[sourceIndex] : [];
            var typeFields = isClass
                ? program.Classes[sourceIndex].Fields
                : program.Structures[sourceIndex].Fields;
            var typeDefFlags = isClass
                ? (exportKeys.Count != 0 ? 0x02u
                    : program.Classes[sourceIndex].IsInternal ? 0x01u : 0x02u)
                    | 0x08u
                    | (program.Classes[sourceIndex].IsMutable ? 0x80u : 0u)
                : (program.Structures[sourceIndex].IsPrivate ? 0u
                    : program.Structures[sourceIndex].IsInternal ? 1u : 2u)
                    | 0x04u;
            var typeDefRow = typeDefRows.Count + 1;
            if (typeFields.IsDefault
                || (isClass && typeFields.IsEmpty)
                || (isClass && exportKeys.Count != 0
                    && program.Classes[sourceIndex].IsInternal))
            {
                throw Unsupported(
                    "C1M1 source class declarations require complete instance fields and public roots.");
            }
            var separator = declarationName.LastIndexOf('.');
            var namespaceName = !isClass
                ? sourceStructDeclarations[sourceIndex].Namespace
                : separator < 0 ? string.Empty
                    : declarationName[..separator];
            var typeName = !isClass
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
            for (var fieldOrdinal = 0; fieldOrdinal < typeFields.Length;
                fieldOrdinal++)
            {
                var field = typeFields[fieldOrdinal];
                if (field is null || (field.IsPrivate && field.IsInternal)
                    || field.RequiresUnsafeCaller)
                {
                    throw Unsupported(
                        "C1M1 source class FieldDefs cannot discard conflicting access or unsafe-caller facts.");
                }

                var visibility = field.IsPrivate ? 0u
                    : field.IsInternal ? 1u : 2u;
                var fieldFlags = visibility
                    | (field.IsMutable ? 0x04u : 0u);
                var fieldDefRow = fieldRows.Count + 1;
                fieldRows.Add(new TrRmFieldDefRow(typeDefRow, 0,
                    FieldTypeSpecReference(field.Type), fieldFlags));
                fieldNames.Add(field.Name);
                if (isClass)
                {
                    fieldDefRowByClassAndIndex.Add(
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
                typeFields.IsEmpty ? 0 : fieldFirst, typeFields.Length, 0, 0,
                layoutRows.Count + 1, 1, 0, 0));
            typeDefNames.Add((namespaceName, typeName));
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, 0, 0, 0, 0,
                typeFields.IsEmpty ? 0 : layoutFieldFirst,
                typeFields.Length));
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

                        constructorDeclarations.Add((part, member));
                    }
                }

                if (constructorDeclarations.Count != 1)
                {
                    throw Unsupported(
                        "The bounded Construction-2 class source emitter requires one complete source constructor per retained class.");
                }

                var (constructorPart, constructorDeclaration)
                    = constructorDeclarations[0];
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
                var classDeclaration
                    = program.Classes[classIndex];
                var receiverType = function.Variables.IsEmpty
                    ? PrimitiveType.Void : function.Variables[0].Type;
                if (!function.IsClassConstructor
                    || function.IsConstructor || function.IsStatic
                    || function.IsExternal || function.IsPartialConstructor
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
                    = GetC1M1Construction2FieldAssignments(function,
                        classDeclaration);
                foreach (var field in classDeclaration.Fields)
                {
                    _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
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
                    var directFieldWriteIndex = -1;
                    var isSupportedDirectFieldWriteInstanceMethod
                        = !methodFunction.IsStatic
                            && methodFunction.ReturnType
                                == PrimitiveType.Void
                            && TryGetC1M1Construction2DirectFieldWrite(
                                methodFunction,
                                out directFieldWriteIndex);
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
                    var directInt32Binary
                        = TryGetC1M1Construction2DirectInt32Binary(
                            methodFunction);
                    var directInt32Unary
                        = TryGetC1M1Construction2DirectInt32Unary(
                            methodFunction);
                    var callInt32Binary
                        = TryGetC1M1Construction2DirectCallInt32Binary(
                            methodFunction, program.Functions);
                    var localCopyReturn
                        = TryGetC1M1Construction2LocalCopyReturn(
                            methodFunction);
                    var localInt32BinaryReturn
                        = TryGetC1M1Construction2LocalInt32BinaryReturn(
                            methodFunction);
                    if ((!methodFunction.IsStatic
                            && !isSupportedNoOpInstanceMethod
                            && !isSupportedDirectReturnInstanceMethod
                            && !isSupportedDirectFieldReadInstanceMethod
                            && !isSupportedDirectFieldWriteInstanceMethod
                            && !isSupportedIdentityForwarder
                            && !isSupportedUnitIdentityForwarder
                            && directInt32Binary is null
                            && directInt32Unary is null
                            && callInt32Binary is null
                            && !localCopyReturn
                            && !localInt32BinaryReturn)
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
                            "The bounded Construction-2 class method producer supports effect-free scalar static methods, exact identity forwarders, readonly instance int32 unary/binary/comparison and same-class call-binary methods, scalar local-copy/binary returns, empty unit instance methods, readonly instance methods returning one scalar value parameter and direct scalar field reads/writes.");
                    }

                    if (methodFunction.ReturnType != PrimitiveType.Void)
                    {
                        if (!methodFunction.IsStatic)
                        {
                            if (!isSupportedDirectReturnInstanceMethod
                                && !isSupportedDirectFieldReadInstanceMethod
                                && !isSupportedDirectFieldWriteInstanceMethod
                                && !isSupportedIdentityForwarder
                                && !isSupportedUnitIdentityForwarder
                                && directInt32Binary is null
                                && directInt32Unary is null
                                && callInt32Binary is null
                                && !localCopyReturn
                                && !localInt32BinaryReturn)
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 instance method producer supports only unit, direct scalar-parameter, direct int32 unary/binary/comparison, same-class call-binary, scalar local-copy/binary returns or direct scalar-field results and direct scalar-field writes.");
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
                                _ = GetC1M1SourceScalarTypeSpecReference(
                                    variable.Type);
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
                                _ = GetC1M1SourceScalarTypeSpecReference(
                                    variable.Type);
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
                    else if (directInt32Binary is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2DirectInt32BinaryBody(
                                methodFunction, directInt32Binary,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (directInt32Unary is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2DirectInt32UnaryBody(
                                methodFunction, directInt32Unary,
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
                    else if (localInt32BinaryReturn)
                    {
                        encodedBody
                            = EncodeC1M1Construction2LocalInt32BinaryReturnBody(
                                methodFunction,
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
                        methodSignatureRow, methodVisibility, 0, 0, 0));
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
                            encodedBody, callInt32Binary));
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
                    contractRows.Count, schema));
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

                        body = EncodeC1M1Construction2CallInt32BinaryBody(
                            method.Function, callBinaryExpression,
                            targetMethodDefRow,
                            program.Functions[targetCall.Function],
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
            + typeSpecRows.Count + exportRows.Count + methodRows.Count
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
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleIdentityStringRow,
                moduleTargetRequirement, (uint)program.RuntimePolicy, 0,
                1, 1, 1, 1,
                featureRows.Count == 0 ? 0u : 1u,
                checked((uint)featureRows.Count)),
        ], strings, 0, typeDefRows.Count, methodRows.Count, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, typeDefRows.Count, methodRows.Count, 0,
            featureRows.Count);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldRows.Count, methodRows.Count,
            layoutRows.Count, contractRows.Count, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefRows.Count, strings, modules, fieldRows.Count,
            methodRows.Count, layoutRows.Count, contractRows.Count, schema);
        var typeSpecBytes = typeSpecRows.Count == 0 ? [] :
            TrRmTypeSpecTableWriter.Encode(typeSpecRows, typeDefRows.Count,
                0, 0, 0, schema, methodDefCount: methodRows.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(
            typeSpecRows.Count == 0
                ? new byte[sizeof(uint)] : typeSpecBytes,
            typeSpecRows.Count, typeDefRows.Count, 0, 0, 0,
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
            _ => throw Unsupported(
                $"C1M1 source class field type '{type}' has no supported local scalar TypeSpec."),
        };
        return 0xF200_0000u | checked((uint)typeCode);
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

    private static ImmutableArray<BoundFieldAssignment>
        GetC1M1Construction2FieldAssignments(
            BoundFunction function, BoundClass @class)
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
                    || !assignedFields.Add(assignment.Field)
                    || assignment.Value is not BoundRead read
                    || read.Variable <= 0
                    || read.Variable >= function.ParameterCount
                    || read.Type != @class.Fields[assignment.Field].Type
                    || function.Variables[read.Variable].Type != read.Type)
                {
                    throw Unsupported(
                        "The bounded Construction-2 initializer supports only direct, single-use scalar parameter-to-field stores.");
                }

                _ = GetC1M1SourceScalarTypeSpecReference(read.Type);
                assignments.Add(assignment);
            }
        }
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
        int contractCount, TrRmArtifactSchema schema)
    {
        var function = initializer.Function;
        var typeTokens = function.Variables
            .Select(variable => CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(variable.Type)))
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
        foreach (var assignment in initializer.FieldAssignments)
        {
            if (assignment.Value is not BoundRead read
                || !fieldDefRowByClassAndIndex.TryGetValue((
                    initializer.ClassIndex, assignment.Field),
                    out var fieldDefRow))
            {
                throw new InvalidDataException(
                    "A Construction-2 initializer Body cannot bind its checked field assignment.");
            }

            var fieldTypeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(
                    function.Variables[read.Variable].Type));
            var selectionRecord = records.Count;
            records.Add(TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 2, accessKind: 3, destinationKind: 1,
                baseKind: 2, destinationIndex: 0, baseIndex: 0,
                indexKind: 0, targetRefKind: 1, indexIndex: 0,
                targetRef: checked((uint)fieldDefRow),
                referentTypeMapIndex: checked((ushort)
                    maps.GetTypeMapIndex(fieldTypeToken)),
                schema));
            var storeRecord = records.Count;
            records.Add(TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                placeKind: 1, sourceKind: 2, transition: 1,
                sourceUse: 1, placeIndex: 0,
                sourceIndex: checked((ushort)read.Variable), schema));
            AppendRecordReference(0xD8, selectionRecord);
            AppendRecordReference(0xD9, storeRecord);
        }

        code.Add(0x18);
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
        (int LeftHome, int RightHome) SetInt32BinaryHomes(
            int leftVariable, int rightVariable)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(PrimitiveType.Int32));
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

        void SetInt32FormalHome(int variable)
        {
            typeTokens = [
                CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(PrimitiveType.Int32)),
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
            && function.ReturnType == PrimitiveType.Int32
            && unary.Type == PrimitiveType.Int32
            && unary.Operand is BoundRead operand
            && operand.Type == PrimitiveType.Int32
            && (uint)operand.Variable < (uint)function.ParameterCount
            && function.Variables[operand.Variable].Type
                == PrimitiveType.Int32
            && unary.Operator is TokenKind.Minus or TokenKind.Tilde)
        {
            SetInt32FormalHome(operand.Variable);
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
        else if (returnValue is BoundBinary binary
            && function.ReturnType == PrimitiveType.Int32
            && binary.Type == PrimitiveType.Int32
            && binary.Left is BoundRead left
            && binary.Right is BoundRead right
            && left.Type == PrimitiveType.Int32
            && right.Type == PrimitiveType.Int32
            && (uint)left.Variable < (uint)function.ParameterCount
            && (uint)right.Variable < (uint)function.ParameterCount
            && function.Variables[left.Variable].Type
                == PrimitiveType.Int32
            && function.Variables[right.Variable].Type
                == PrimitiveType.Int32
            && binary.Operator is TokenKind.Ampersand or TokenKind.Pipe
                or TokenKind.Caret)
        {
            var (leftHome, rightHome)
                = SetInt32BinaryHomes(left.Variable, right.Variable);
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
            && function.ReturnType == PrimitiveType.Int32
            && arithmetic.Type == PrimitiveType.Int32
            && arithmetic.Left is BoundRead arithmeticLeft
            && arithmetic.Right is BoundRead arithmeticRight
            && arithmeticLeft.Type == PrimitiveType.Int32
            && arithmeticRight.Type == PrimitiveType.Int32
            && (uint)arithmeticLeft.Variable
                < (uint)function.ParameterCount
            && (uint)arithmeticRight.Variable
                < (uint)function.ParameterCount
            && function.Variables[arithmeticLeft.Variable].Type
                == PrimitiveType.Int32
            && function.Variables[arithmeticRight.Variable].Type
                == PrimitiveType.Int32
            && arithmetic.Operator is TokenKind.Plus or TokenKind.Minus
                or TokenKind.Star)
        {
            var (leftHome, rightHome) = SetInt32BinaryHomes(
                arithmeticLeft.Variable, arithmeticRight.Variable);
            var operation = (arithmetic.Operator,
                arithmetic.OverflowBehavior) switch
            {
                (TokenKind.Plus,
                    BoundIntegerOverflowBehavior.Checked) => (byte)0x01,
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Checked) => (byte)0x02,
                (TokenKind.Star,
                    BoundIntegerOverflowBehavior.Checked) => (byte)0x03,
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
            && comparisonLeft.Type == PrimitiveType.Int32
            && comparisonRight.Type == PrimitiveType.Int32
            && (uint)comparisonLeft.Variable
                < (uint)function.ParameterCount
            && (uint)comparisonRight.Variable
                < (uint)function.ParameterCount
            && function.Variables[comparisonLeft.Variable].Type
                == PrimitiveType.Int32
            && function.Variables[comparisonRight.Variable].Type
                == PrimitiveType.Int32
            && IsC1M1Construction2Int32ComparisonOperator(
                comparison.Operator))
        {
            var (leftHome, rightHome) = SetInt32BinaryHomes(
                comparisonLeft.Variable, comparisonRight.Variable);
            code = EncodeC1M1Construction2Int32ComparisonCode(
                leftHome, rightHome, comparison.Operator);
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
                "The bounded Construction-2 static register Body supports direct value-formal, int32 unary/binary/comparison, or fixed-scalar returns.");
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

    private static bool IsC1M1Construction2Int32ComparisonOperator(
        TokenKind operation)
        => operation is TokenKind.EqualsEquals or TokenKind.BangEquals
            or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater
            or TokenKind.GreaterEquals;

    private static byte GetC1M1Construction2Int32ComparisonPredicate(
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
                "The selected int32 comparison has no registered predicate."),
        };

    private static (byte Opcode, byte Control)
        GetC1M1Construction2Int32BinaryEncoding(TokenKind operation,
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
            (TokenKind.Plus, BoundIntegerOverflowBehavior.Wrapping)
                => (0xC0, 0x05),
            (TokenKind.Minus, BoundIntegerOverflowBehavior.Wrapping)
                => (0xC0, 0x06),
            (TokenKind.Star, BoundIntegerOverflowBehavior.Wrapping)
                => (0xC0, 0x07),
            _ => throw Unsupported(
                "The selected int32 binary Body has no registered operation."),
        };

    private static byte[] EncodeC1M1Construction2Int32ComparisonCode(
        int leftHome, int rightHome, TokenKind operation)
    {
        if (leftHome < 0 || leftHome > ushort.MaxValue
            || rightHome < 0 || rightHome > ushort.MaxValue)
        {
            throw new InvalidDataException(
                "An int32 comparison Body has an invalid Home index.");
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
        code.Add(GetC1M1Construction2Int32ComparisonPredicate(operation));
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

    private static byte[] EncodeC1M1Construction2DirectInt32BinaryBody(
        BoundFunction function, BoundBinary binary,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        if (function.IsStatic || function.ParameterCount != 3
            || function.Variables.Length != function.ParameterCount
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != PrimitiveType.Int32
            || function.Variables[2].Type != PrimitiveType.Int32
            || function.Variables[1].IsMutable
            || function.Variables[2].IsMutable
            || function.Variables.Skip(1).Any(static variable =>
                variable.IsOut || variable.IsInit || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || binary.Left is not BoundRead left
            || binary.Right is not BoundRead right
            || left.Type != PrimitiveType.Int32
            || right.Type != PrimitiveType.Int32
            || left.Variable is not (1 or 2)
            || right.Variable is not (1 or 2))
        {
            throw Unsupported(
                "A Construction-2 instance int32 binary Body requires direct parameter reads and a readonly receiver.");
        }

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2Int32ComparisonOperator(
                    binary.Operator);
        var isInt32ValueOperation
            = function.ReturnType == PrimitiveType.Int32
                && binary.Type == PrimitiveType.Int32
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star);
        if (!isComparison && !isInt32ValueOperation)
        {
            throw Unsupported(
                "A Construction-2 instance int32 binary Body requires a supported int32 operation or Boolean comparison.");
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

        byte[] code;
        if (isComparison)
        {
            code = EncodeC1M1Construction2Int32ComparisonCode(
                left.Variable, right.Variable, binary.Operator);
        }
        else
        {
            var instructions = new List<byte>(14);
            AppendHomeLoad(instructions, 0, left.Variable);
            AppendHomeLoad(instructions, 1, right.Variable);
            const byte destinationRegister = 0;
            const byte leftRegister = 0;
            const byte rightRegister = 1;
            var payload = (ushort)(destinationRegister | (0x0F << 4)
                | (leftRegister << 8) | (rightRegister << 12));
            var (opcode, operation)
                = GetC1M1Construction2Int32BinaryEncoding(
                    binary.Operator, binary.OverflowBehavior);
            instructions.Add(opcode);
            instructions.Add(operation);
            instructions.Add((byte)payload);
            instructions.Add((byte)(payload >> 8));
            instructions.Add(0x17);
            instructions.Add(0);
            code = instructions.ToArray();
        }

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

        static void AppendHomeLoad(List<byte> instructions,
            byte destination, int homeIndex)
        {
            instructions.Add(0xD5);
            instructions.Add(destination);
            instructions.Add(checked((byte)homeIndex));
            instructions.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2DirectInt32UnaryBody(
        BoundFunction function, BoundUnary unary,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        if (function.IsStatic || function.ReturnType != PrimitiveType.Int32
            || function.ParameterCount != 2
            || function.Variables.Length != function.ParameterCount
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != PrimitiveType.Int32
            || function.Variables[1].IsMutable
            || function.Variables[1].IsOut || function.Variables[1].IsInit
            || function.Variables[1].IsImplicitRef
            || function.Variables[1].IsInitializationLocal
            || unary.Type != PrimitiveType.Int32
            || unary.Operand is not BoundRead operand
            || operand.Type != PrimitiveType.Int32
            || operand.Variable != 1
            || unary.Operator is not (TokenKind.Minus or TokenKind.Tilde))
        {
            throw Unsupported(
                "A Construction-2 instance int32 unary Body requires one direct int32 parameter read and a readonly receiver.");
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

    private static byte[] EncodeC1M1Construction2CallInt32BinaryBody(
        BoundFunction function, BoundBinary binary, int targetMethodDefRow,
        BoundFunction targetFunction,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var callerReceiverCount = function.IsStatic ? 0 : 1;
        var targetReceiverCount = targetFunction.IsStatic ? 0 : 1;
        if (targetMethodDefRow <= 0 || targetMethodDefRow > 0xFF_FFFF
            || function.ParameterCount != callerReceiverCount + 2
            || function.Variables.Length != function.ParameterCount
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || function.Variables.Skip(callerReceiverCount).Any(
                static variable =>
                variable.Type != PrimitiveType.Int32 || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || binary.Left is not BoundCall call
            || binary.Right is not BoundRead right
            || right.Type != PrimitiveType.Int32
            || right.Variable < callerReceiverCount
            || right.Variable >= function.ParameterCount
            || call.Type != PrimitiveType.Int32
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
            || targetFunction.ReturnType != PrimitiveType.Int32
            || targetFunction.ParameterCount != targetReceiverCount + 2
            || targetFunction.Variables.Length
                < targetFunction.ParameterCount
            || (!targetFunction.IsStatic
                && targetFunction.Variables[0].IsMutable)
            || targetFunction.Variables.Skip(targetReceiverCount)
                .Take(2).Any(static variable =>
                    variable.Type != PrimitiveType.Int32
                    || variable.IsMutable || variable.IsOut
                    || variable.IsInit || variable.IsImplicitRef
                    || variable.IsInitializationLocal))
        {
            throw Unsupported(
                "A Construction-2 call-binary Body requires a same-class direct int32 call and one direct int32 parameter read.");
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
                && IsC1M1Construction2Int32ComparisonOperator(
                    binary.Operator);
        var isInt32ValueOperation
            = function.ReturnType == PrimitiveType.Int32
                && binary.Type == PrimitiveType.Int32
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star);
        if (!isComparison && !isInt32ValueOperation)
        {
            throw Unsupported(
                "A Construction-2 instance call-binary Body requires an assigned int32 operation or Boolean comparison.");
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
        AppendHomeLoad(1, right.Variable);
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
            code.Add(GetC1M1Construction2Int32ComparisonPredicate(
                binary.Operator));
            code.Add((byte)predicatePayload);
            code.Add((byte)(predicatePayload >> 8));
        }
        else
        {
            var (opcode, control)
                = GetC1M1Construction2Int32BinaryEncoding(
                    binary.Operator, binary.OverflowBehavior);
            code.Add(opcode);
            code.Add(control);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        code.Add(0x17);
        code.Add(0);
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default,
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
    }

    private static byte[] EncodeC1M1Construction2LocalCopyReturnBody(
        BoundFunction function,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localVariable = receiverCount + 1;
        if (function.ReturnType != PrimitiveType.Int32
            || function.ParameterCount != receiverCount + 1
            || function.Variables.Length != receiverCount + 2
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || function.Variables[receiverCount].Type
                != PrimitiveType.Int32
            || function.Variables[receiverCount].IsMutable
            || function.Variables[receiverCount].IsOut
            || function.Variables[receiverCount].IsInit
            || function.Variables[receiverCount].IsImplicitRef
            || function.Variables[receiverCount].IsInitializationLocal
            || function.Variables[localVariable].Type
                != PrimitiveType.Int32
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
            || local.Initializer is not BoundRead {
                Type: PrimitiveType.Int32,
            } initialRead
            || initialRead.Variable != receiverCount
            || returnedRead.Type != PrimitiveType.Int32
            || returnedRead.Variable != localVariable)
        {
            throw Unsupported(
                "A Construction-2 local-copy Body requires one initialized int32 local copied from an int32 value formal and returned directly.");
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

    private static byte[] EncodeC1M1Construction2LocalInt32BinaryReturnBody(
        BoundFunction function,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localCount = function.Variables.Length
            - function.ParameterCount;
        if (function.ParameterCount != receiverCount + 2
            || localCount <= 0
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || function.Variables.Skip(receiverCount).Any(
                static variable =>
                variable.Type != PrimitiveType.Int32 || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != localCount + 1
            || function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundBinary binary,
            })
        {
            throw Unsupported(
                "A Construction-2 local-binary Body requires initialized int32 locals followed by one binary return.");
        }

        var localInitializers = new BoundRead[localCount];
        for (var localOrdinal = 0;
            localOrdinal < localCount; localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            if (function.Body.Statements[localOrdinal] is not BoundLocal local
                || local.Variable != localVariable
                || local.Initializer is not BoundRead initializer
                || initializer.Type != PrimitiveType.Int32
                || initializer.Variable < 0
                || initializer.Variable >= localVariable
                || function.Variables[initializer.Variable].Type
                    != PrimitiveType.Int32)
            {
                throw Unsupported(
                    "A Construction-2 local-binary Body requires source-ordered scalar local copies from initialized Homes.");
            }

            localInitializers[localOrdinal] = initializer;
        }

        if (binary.Left is not BoundRead left
            || binary.Right is not BoundRead right
            || left.Type != PrimitiveType.Int32
            || right.Type != PrimitiveType.Int32
            || left.Variable < receiverCount
            || left.Variable >= function.Variables.Length
            || right.Variable < receiverCount
            || right.Variable >= function.Variables.Length
            || function.Variables[left.Variable].Type
                != PrimitiveType.Int32
            || function.Variables[right.Variable].Type
                != PrimitiveType.Int32)
        {
            throw Unsupported(
                "A Construction-2 local-binary Body requires direct int32 Home operands.");
        }

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2Int32ComparisonOperator(
                    binary.Operator);
        var isInt32ValueOperation
            = function.ReturnType == PrimitiveType.Int32
                && binary.Type == PrimitiveType.Int32
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star);
        if (!isComparison && !isInt32ValueOperation)
        {
            throw Unsupported(
                "A Construction-2 local-binary Body requires an assigned int32 operation or Boolean comparison.");
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

        if (isComparison)
        {
            code.AddRange(EncodeC1M1Construction2Int32ComparisonCode(
                left.Variable, right.Variable, binary.Operator));
        }
        else
        {
            AppendHomeLoad(code, 0, left.Variable);
            AppendHomeLoad(code, 1, right.Variable);
            const byte destinationRegister = 0;
            const byte leftRegister = 0;
            const byte rightRegister = 1;
            var payload = (ushort)(destinationRegister | (0x0F << 4)
                | (leftRegister << 8) | (rightRegister << 12));
            var (opcode, control)
                = GetC1M1Construction2Int32BinaryEncoding(
                    binary.Operator, binary.OverflowBehavior);
            code.Add(opcode);
            code.Add(control);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
            code.Add(0x17);
            code.Add(0);
        }

        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

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

    private static BoundBinary? TryGetC1M1Construction2DirectInt32Binary(
        BoundFunction function)
    {
        if (function.IsStatic || function.ParameterCount != 3
            || function.ParameterCount != function.Variables.Length
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != PrimitiveType.Int32
            || function.Variables[2].Type != PrimitiveType.Int32
            || function.Variables[1].IsMutable
            || function.Variables[2].IsMutable
            || function.Variables.Skip(1).Any(static variable =>
                variable.IsOut || variable.IsInit || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            return null;
        }

        BoundBinary? returnedBinary = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedBinary is null
            || returnedBinary.Left is not BoundRead left
            || returnedBinary.Right is not BoundRead right
            || left.Type != PrimitiveType.Int32
            || right.Type != PrimitiveType.Int32
            || left.Variable is not (1 or 2)
            || right.Variable is not (1 or 2))
        {
            return null;
        }

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && returnedBinary.Type == PrimitiveType.Bool
                && IsC1M1Construction2Int32ComparisonOperator(
                    returnedBinary.Operator);
        var isInt32ValueOperation
            = function.ReturnType == PrimitiveType.Int32
                && returnedBinary.Type == PrimitiveType.Int32
                && (returnedBinary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star);
        return isComparison || isInt32ValueOperation
            ? returnedBinary : null;

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

    private static BoundUnary? TryGetC1M1Construction2DirectInt32Unary(
        BoundFunction function)
    {
        if (function.IsStatic || function.ReturnType != PrimitiveType.Int32
            || function.ParameterCount != 2
            || function.ParameterCount != function.Variables.Length
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != PrimitiveType.Int32
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
            || returnedUnary.Type != PrimitiveType.Int32
            || returnedUnary.Operator is not (TokenKind.Minus
                or TokenKind.Tilde)
            || returnedUnary.Operand is not BoundRead operand
            || operand.Type != PrimitiveType.Int32
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
        TryGetC1M1Construction2DirectCallInt32Binary(
        BoundFunction function, ImmutableArray<BoundFunction> functions)
    {
        var callerReceiverCount = function.IsStatic ? 0 : 1;
        if (function.ParameterCount != callerReceiverCount + 2
            || function.ParameterCount != function.Variables.Length
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || function.Variables.Skip(callerReceiverCount).Any(
                static variable =>
                variable.Type != PrimitiveType.Int32 || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            return null;
        }

        BoundBinary? returnedBinary = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedBinary is null
            || returnedBinary.Left is not BoundCall call
            || returnedBinary.Right is not BoundRead right
            || right.Type != PrimitiveType.Int32
            || right.Variable < callerReceiverCount
            || right.Variable >= function.ParameterCount
            || call.Type != PrimitiveType.Int32
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
                && IsC1M1Construction2Int32ComparisonOperator(
                    returnedBinary.Operator);
        var isInt32ValueOperation
            = function.ReturnType == PrimitiveType.Int32
                && returnedBinary.Type == PrimitiveType.Int32
                && (returnedBinary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret
                    or TokenKind.Plus or TokenKind.Minus or TokenKind.Star);
        if (!isComparison && !isInt32ValueOperation)
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
            || target.ReturnType != PrimitiveType.Int32
            || target.ParameterCount != targetReceiverCount + 2
            || call.Arguments.Length != target.ParameterCount
            || target.Variables.Length < target.ParameterCount)
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
        return function.ReturnType == PrimitiveType.Int32
            && function.ParameterCount == receiverCount + 1
            && function.Variables.Length == receiverCount + 2
            && (function.IsStatic || !function.Variables[0].IsMutable)
            && function.Variables[formalVariable].Type
                == PrimitiveType.Int32
            && !function.Variables[formalVariable].IsMutable
            && !function.Variables[formalVariable].IsOut
            && !function.Variables[formalVariable].IsInit
            && !function.Variables[formalVariable].IsImplicitRef
            && !function.Variables[formalVariable].IsInitializationLocal
            && function.Variables[localVariable].Type
                == PrimitiveType.Int32
            && !function.Variables[localVariable].IsMutable
            && !function.Variables[localVariable].IsOut
            && !function.Variables[localVariable].IsInit
            && !function.Variables[localVariable].IsImplicitRef
            && !function.Variables[localVariable].IsInitializationLocal
            && function.Body.RetiredManagedLocals.IsEmpty
            && function.Body.Statements is [
                BoundLocal local,
                BoundReturn { Value: BoundRead returnedRead },
            ]
            && local.Variable == localVariable
            && local.Initializer is BoundRead initialRead
            && initialRead.Type == PrimitiveType.Int32
            && initialRead.Variable == formalVariable
            && returnedRead.Type == PrimitiveType.Int32
            && returnedRead.Variable == localVariable;
    }

    private static bool TryGetC1M1Construction2LocalInt32BinaryReturn(
        BoundFunction function)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localCount = function.Variables.Length
            - function.ParameterCount;
        if (function.ParameterCount != receiverCount + 2
            || localCount <= 0
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || !function.Variables.Skip(receiverCount).All(
                static variable =>
                variable.Type == PrimitiveType.Int32
                && !variable.IsMutable && !variable.IsOut
                && !variable.IsInit && !variable.IsImplicitRef
                && !variable.IsInitializationLocal)
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != localCount + 1)
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
                || initializer.Type != PrimitiveType.Int32
                || initializer.Variable < 0
                || initializer.Variable >= localVariable
                || function.Variables[initializer.Variable].Type
                    != PrimitiveType.Int32)
            {
                return false;
            }
        }

        if (function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundBinary binary,
            }
            || binary.Left is not BoundRead left
            || binary.Right is not BoundRead right
            || left.Type != PrimitiveType.Int32
            || right.Type != PrimitiveType.Int32
            || left.Variable < receiverCount
            || left.Variable >= function.Variables.Length
            || right.Variable < receiverCount
            || right.Variable >= function.Variables.Length
            || function.Variables[left.Variable].Type
                != PrimitiveType.Int32
            || function.Variables[right.Variable].Type
                != PrimitiveType.Int32)
        {
            return false;
        }

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2Int32ComparisonOperator(
                    binary.Operator);
        var isInt32ValueOperation
            = function.ReturnType == PrimitiveType.Int32
                && binary.Type == PrimitiveType.Int32
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star);
        return isComparison || isInt32ValueOperation;
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

    private static byte[] EmitC1M1MetadataClassTypeReference(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement)
    {
        if (program.EntryPoint != -1 || !program.Functions.IsEmpty
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
                StandardC1M1SchemaIdentity)
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
                    StandardC1M1SchemaIdentity)
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
                StandardC1M1SchemaIdentity,
                provider.ArtifactSnapshot.ToArray()));
        }

        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                AddProviderInput(expectation.Binding.Provider);
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
        schemaIdentities[0] = StandardC1M1SchemaIdentity;
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
        var rootIndex
            = TrRmArtifactIndex.ForC1M1Draft(artifacts[0]);
        var rootTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(rootIndex);
        EnsureC1ClassDeclarationTablesSupported(rootTables);
        var typeProviderBindings
            = TrRmFixedArrayProviderBindingValidator
                .ValidateC1M1TypeDefImports(rootTables, artifacts,
                    schemaIdentities, moduleValidation.Modules,
                    rootModuleRow);
        ValidateC1M1ClassCarrierBindings(program, moduleValidation.Modules,
            artifacts, typeProviderBindings);
        ValidateC1M1StructCarrierBindings(program, moduleValidation.Modules,
            artifacts, typeProviderBindings);
        if (!StringComparer.Ordinal.Equals(
                rootTables.Modules.GetIdentity(rootModuleRow),
                rootSnapshot.ModuleIdentity)
            || rootTables.Modules.GetRequirements(rootModuleRow)
                != (rootSnapshot.TargetRequirement,
                    rootSnapshot.RuntimePolicy)
            || rootTables.Modules.GetGoverningRuleEdition(rootModuleRow)
                != rootSnapshot.GoverningRuleEdition
            || rootTables.Modules.GetEntryMethod(rootModuleRow) != 0
            || rootTables.Modules.GetMethodDefRange(rootModuleRow).Start
                != rootTables.Modules.GetMethodDefRange(rootModuleRow).End)
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
            if ((rootTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) >> 24) != 0x13)
            {
                throw new NotSupportedException(
                    "C1M1 class re-emission only retains TypeDef Imports.");
            }
        }

        if (typeDefViewsByOldRow.Count == 0
            || bindingsByImportRow.Count != rootImportCount)
        {
            throw new InvalidDataException(
                "The retained C1M1 class provider closure is incomplete.");
        }

        var moduleRefStartEnd
            = rootTables.Modules.GetModuleRefRange(rootModuleRow);
        var moduleRefRowsByOldRow = new SortedSet<int>();
        foreach (var (importRow, binding) in bindingsByImportRow)
        {
            if (importRow <= 0 || importRow > rootTables.Imports.Count
                || (rootTables.Imports
                        .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    != 0x13
                || rootTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) != binding.ExpectedDefinitionId
                || !StringComparer.Ordinal.Equals(
                    rootTables.Imports.GetBindingForImportRow(importRow)
                        .ExportKey, binding.ExportKey)
                || !StringComparer.Ordinal.Equals(binding.ProducerIdentity,
                    rootTables.ModuleRefs.GetProducerIdentity(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)))
                || binding.TargetRequirement
                    != rootTables.ModuleRefs.GetRequirements(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)).TargetRequirement
                || binding.RuntimePolicy
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

        var importRowMap = new Dictionary<int, int>();
        var nextImportRow = 0;
        foreach (var oldImportRow in bindingsByImportRow.Keys)
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
        foreach (var moduleRefRow in moduleRefRowsByOldRow)
        {
            AddString(rootTables.ModuleRefs.GetProducerIdentity(moduleRefRow));
        }
        foreach (var binding in bindingsByImportRow.Values)
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

        var outputTypeSpecBytes = outputTypeSpecRows.Count == 0
            ? []
            : TrRmTypeSpecTableWriter.Encode(outputTypeSpecRows,
                outputTypeDefRows.Count, importRowMap.Count, 0, 0,
                TrRmArtifactSchema.C1M1Draft);
        var typeSpecReaderBytes = outputTypeSpecRows.Count == 0
            ? new byte[sizeof(uint)] : outputTypeSpecBytes;
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecReaderBytes,
            outputTypeSpecRows.Count, outputTypeDefRows.Count,
            importRowMap.Count, 0, 0, TrRmArtifactSchema.C1M1Draft);

        var targetProfileBytes = GetTargetProfileSection(rootIndex,
            out var targetProfileCount);
        var targetProfiles = targetProfileBytes.Length == 0
            ? new TrRmTargetProfileTableReader([], 0)
            : new TrRmTargetProfileTableReader(targetProfileBytes,
                targetProfileCount);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.C1M1Draft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleIdentityStringRow,
                moduleTargetRequirement, rootSnapshot.RuntimePolicy, 0,
                1, 1, 1, 1,
                featureRows.Length == 0 ? 0 : 1,
                featureRows.Length, rootSnapshot.GoverningRuleEdition),
        ], strings, moduleRefRowMap.Count, outputTypeDefRows.Count, 0, 0,
            featureRows.Length);
        var moduleTables = new TrRmModuleTableReader(moduleBytes, 1,
            strings, moduleRefRowMap.Count, outputTypeDefRows.Count, 0, 0,
            featureRows.Length);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(outputTypeDefRows,
            strings, moduleTables, fieldRows.Count, 0, layoutRows.Count, 0,
            TrRmArtifactSchema.C1M1Draft);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            outputTypeDefRows.Count, strings, moduleTables, fieldRows.Count,
            0, layoutRows.Count, 0, TrRmArtifactSchema.C1M1Draft);

        var layoutFieldCount = layoutFieldRows.Count;
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings, typeDefs,
                typeSpecs);
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
        var importBytes = bindingsByImportRow.Count == 0 ? []
            : EncodeC1M1ClassImports(bindingsByImportRow, importRowMap,
                moduleRefRowMap, typeDefRowMap, stringRows,
                rootTables);
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
                checked((uint)bindingsByImportRow.Count), importBytes));
        }
        if (exportBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exportRows.Count), exportBytes));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.C1M1Draft);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Draft(artifact));
        return artifact;
    }

    private static void ValidateC1M1ClassCarrierBindings(
        BoundProgram program,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        byte[][] artifacts,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        var expectationsByImport = new Dictionary<
            (string ModuleIdentity, int ModuleRow, int ImportRow),
            BoundStandardClassExpectation>();
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
                    usingModule.ArtifactIndex);
                EnsureC1ClassDeclarationTablesSupported(usingTables);
                var usingSnapshot = CreateA2SourceTypeProviderSnapshot(
                    usingTables, artifacts[usingModule.ArtifactIndex],
                    StandardC1M1SchemaIdentity, usingModule.ModuleRow);
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

                var providerModule = modules.Single(module =>
                    StringComparer.Ordinal.Equals(module.Identity,
                        binding.ProducerIdentity)
                    && module.ModuleRow == binding.Provider.ModuleRow);
                var providerTables = GetC1M1Tables(artifacts,
                    providerModule.ArtifactIndex);
                EnsureC1ClassDeclarationTablesSupported(providerTables);
                var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                    providerTables, artifacts[providerModule.ArtifactIndex],
                    StandardC1M1SchemaIdentity, providerModule.ModuleRow);
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
                consumerModule.ArtifactIndex);
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
    }

    private static void ValidateC1M1StructCarrierBindings(
        BoundProgram program,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        byte[][] artifacts,
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
                consumerModule.ArtifactIndex);
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
                StandardC1M1SchemaIdentity, consumerModule.ModuleRow);
            var providerModule = modules.Single(module =>
                StringComparer.Ordinal.Equals(module.Identity,
                    expectation.ProducerIdentity)
                && module.ModuleRow == expectation.Provider.ModuleRow);
            var providerTables = GetC1M1Tables(artifacts,
                providerModule.ArtifactIndex);
            var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                providerTables, artifacts[providerModule.ArtifactIndex],
                StandardC1M1SchemaIdentity, providerModule.ModuleRow);
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
        GetC1M1Tables(byte[][] artifacts, int artifactIndex)
    {
        if ((uint)artifactIndex >= (uint)artifacts.Length)
        {
            throw new InvalidDataException(
                "A C1M1 class provider artifact index is out of range.");
        }

        return TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Draft(
                    artifacts[artifactIndex]));
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

    private static byte[] EncodeC1M1ClassImports(
        SortedDictionary<int, BoundSourceTypeExpectation> bindingsByImportRow,
        Dictionary<int, int> importRowMap,
        Dictionary<int, int> moduleRefRowMap,
        Dictionary<int, int> typeDefRowMap,
        Dictionary<string, int> stringRows,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables sourceTables)
    {
        var bytes = new byte[checked(bindingsByImportRow.Count * 12)];
        foreach (var (oldImportRow, binding) in bindingsByImportRow)
        {
            var newImportRow = importRowMap[oldImportRow];
            var oldModuleRefRow = sourceTables.Imports
                .GetModuleRefForImportRow(oldImportRow);
            var newModuleRefRow = moduleRefRowMap[oldModuleRefRow];
            var expectedOldTypeDefRow
                = GetTypeDefRow(binding.ExpectedDefinitionId);
            var expectedNewTypeDefRow
                = typeDefRowMap[expectedOldTypeDefRow];
            var offset = (newImportRow - 1) * 12;
            Write(bytes, offset, checked((uint)newModuleRefRow));
            Write(bytes, offset + 4,
                checked((uint)stringRows[binding.ExportKey]));
            Write(bytes, offset + 8,
                0x1300_0000u | checked((uint)expectedNewTypeDefRow));
        }

        return bytes;
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
            || left.Layouts.Length != right.Layouts.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Fields.Length; index++)
        {
            if (left.Fields[index] != right.Fields[index])
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

        return true;
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
