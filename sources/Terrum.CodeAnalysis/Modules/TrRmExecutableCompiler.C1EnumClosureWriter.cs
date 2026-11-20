using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private sealed record C1EnumClosureEmissionMember(
        string DeclarationKey, string Name, BigInteger Value);

    private sealed record C1EnumClosureImport(
        string ProducerIdentity, string ExportKey, uint TargetRequirement,
        uint RuntimePolicy, int RootImportRow = 0,
        string? RootImportProviderIdentity = null,
        ImmutableArray<C1EnumClosureRootImport> RootImports = default);

    private sealed record C1EnumClosureRootImport(
        string ProviderIdentity, int ImportRow);

    private sealed record C1EnumClosureStandardClassEmission(
        BoundStandardClassTypeView View, ImmutableArray<string> ExportKeys,
        C1EnumClosureImport? Import, int RootTypeDefRow,
        ImmutableArray<BoundStandardClassFieldExpectation>
            FieldExpectations)
    {
        internal string? OwnerModuleIdentity { get; init; }
        internal ImmutableArray<C1EnumClosureImport> AdditionalImports
            { get; init; } = [];
        internal ImmutableArray<BoundStandardClassMethodExpectation>
            MethodExpectations { get; init; } = [];
    }

    private static BoundFunction? GetEN1ClassLayoutQueryFunction(
        BoundProgram program, string schemaIdentity)
    {
        var moduleFunctions = program.Functions
            .Where(static function => function.DeclaringStruct is null)
            .ToArray();
        if (moduleFunctions.Length == 0)
        {
            return null;
        }
        if (!IsSelectedC1ClassInstanceLayoutSchemaIdentity(schemaIdentity)
            || moduleFunctions.Length != 1)
        {
            throw Unsupported(
                "The bounded EN1 Body route accepts one parameterless nint method with authorized class-root OffsetOfType queries.");
        }

        var function = moduleFunctions[0];
        if (function.ReturnType != PrimitiveType.NativeInt
            || function.ParameterCount != 0
            || function.IsExternal || function.IsConstructor
            || function.DirectNativeImport is not null
            || function.NativeFunctionPointerType is not null
            || function.NativeVTableMethod is not null
            || function.ModuleImport is not null
            || function.DeclaringStruct is not null
            || !function.Variables.IsEmpty
            || function.Body.Statements.Length != 1
            || function.Body.Statements[0] is not BoundReturn)
        {
            throw Unsupported(
                "The bounded EN1 Body route accepts one parameterless nint method with authorized class-root OffsetOfType queries.");
        }

        var returned = (BoundReturn)function.Body.Statements[0];
        if (returned.Value is null)
        {
            throw Unsupported(
                "An EN1 class query Body requires a returned class layout expression.");
        }

        var queries = GetEN1ClassLayoutQueries(function);
        if (queries.IsEmpty)
        {
            throw Unsupported(
                "An EN1 class query Body must contain an OffsetOfType operation.");
        }

        foreach (var query in queries)
        {
            var fieldPath = query.Subject.FieldPath;
            if (query.Type != PrimitiveType.NativeInt
                || query.Kind != BoundLayoutQueryKind.OffsetOfType
                || query.Subject.Kind != BoundQuerySubjectKind.Type
                || !query.Subject.RequiresUnsafe
                || !query.Subject.IsUnsafeAuthorized
                || fieldPath is null
                || !fieldPath.HasValidStructure
                || fieldPath.RootType != query.Subject.Type
                || !BoundGenericNominalTypeContract.IsNominalClassType(
                    fieldPath.RootType,
                    program.GenericNominalTypeInstances,
                    program.GenericNominalTypeDefinitions))
            {
                throw Unsupported(
                    "The bounded EN1 Body route accepts only authorized class-root OffsetOfType queries.");
            }

            var instanceIndex
                = BoundGenericNominalTypeTypes.Index(fieldPath.RootType);
            if ((uint)instanceIndex
                >= (uint)program.GenericNominalTypeInstances.Length)
            {
                throw Unsupported(
                    "An EN1 class query Body requires an ordinary nongeneric class root.");
            }

            var instance = program.GenericNominalTypeInstances[
                instanceIndex];
            if (instance is null || !instance.Arguments.IsEmpty
                || instance.Qualifiers != 0
                || (instance.TypeSpecReference == 0
                    ? instance.TypeSpecProviderIdentity is not null
                        || instance.TypeSpecForm != 0
                    : string.IsNullOrEmpty(
                            instance.TypeSpecProviderIdentity)
                        || instance.TypeSpecForm is not (0x02 or 0x03)))
            {
                throw Unsupported(
                    "An EN1 class query Body requires ordinary nongeneric class roots and exact TypeSpec contexts.");
            }
            var declaration
                = program.StandardClassDeclarations.SingleOrDefault(
                    candidate =>
                        candidate.Declaration.Identity
                            == instance.Definition
                        && !candidate.SelectedExportKeys.IsEmpty);
            if (declaration is null || fieldPath.Steps.IsEmpty
                || fieldPath.Steps.Skip(1).Any(step =>
                    step.StandardClassFieldOrigin is not null))
            {
                throw Unsupported(
                    "The bounded EN1 Body route requires a selected public class FieldDef followed only by inline-struct FieldPath steps.");
            }

            var classField
                = fieldPath.Steps[0].StandardClassFieldOrigin;
            if (classField is null
                || classField.OwnerDefinitionId
                    != declaration.Declaration.Identity.DefinitionId
                || classField.SelectedExportKeys.IsEmpty
                || (classField.Flags & 3) != 2
                || (classField.Flags & 8) != 0)
            {
                throw Unsupported(
                    "The bounded EN1 Body route requires a selected public nonstatic class FieldDef.");
            }
        }

        return function;
    }

    private static ImmutableArray<BoundLayoutQuery>
        GetEN1ClassLayoutQueries(BoundFunction function)
    {
        var queries = ImmutableArray.CreateBuilder<BoundLayoutQuery>();
        var hasTypeIdentityQuery = false;
        if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression => {
                    if (expression is BoundLayoutQuery query)
                    {
                        queries.Add(query);
                    }
                    else if (expression is BoundTypeIdentityQuery)
                    {
                        hasTypeIdentityQuery = true;
                    }
                })
            || hasTypeIdentityQuery)
        {
            throw Unsupported(
                "An EN1 class query Body contains an unsupported query expression.");
        }

        return queries.ToImmutable();
    }

    private static bool IsSupportedEN1FieldPointer(
        NativePointerTypeDefinition pointer, BoundProgram program)
        => pointer.PointeeType == PrimitiveType.Void
            ? pointer.IsWritable
            : PrimitiveTypeFacts.IsBuiltInScalar(pointer.PointeeType)
                || IsC1EnumClosurePointerPointeeType(
                    pointer.PointeeType, program.Classes,
                    allowClassHandlePointee: true,
                    nominalTypeInstances:
                        program.GenericNominalTypeInstances,
                    nominalTypeDefinitions:
                        program.GenericNominalTypeDefinitions);

    private static int GetEN1ClassQueryRootTypeDefRow(
        BoundGenericNominalTypeInstance instance,
        BoundSourceTypeProviderSnapshot rootSnapshot,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables rootTables)
    {
        if (instance.TypeSpecProviderIdentity is not string usingModuleIdentity
            || !rootTables.Modules.TryFindModule(usingModuleIdentity,
                out var usingModuleRow)
            || instance.Qualifiers != 0
            || instance.TypeSpecForm is not (0x02 or 0x03))
        {
            throw Unsupported(
                "An EN1 class query Body root has no exact unqualified root TypeSpec.");
        }

        var typeSpec = rootTables.TypeSpecs.GetTypeSpecRef(
            instance.TypeSpecReference);
        if (!typeSpec.IsRow || typeSpec.Form != instance.TypeSpecForm
            || typeSpec.Qualifiers != 0)
        {
            throw Invalid(
                "An EN1 class query Body root TypeSpec changed before emission.");
        }

        var typeDefRow = typeSpec.Form switch {
            0x02 => typeSpec.Row,
            0x03 => checked((int)(
                rootTables.Imports.GetExpectedDefinitionForImportRow(
                    typeSpec.Row) & 0x00FF_FFFF)),
            _ => throw Invalid(
                "An EN1 class query Body root TypeSpec has an unsupported form."),
        };
        if (typeSpec.Form == 0x03
            && rootTables.Imports.GetExpectedDefinitionForImportRow(
                typeSpec.Row) >> 24 != 0x13)
        {
            throw Invalid(
                "An EN1 class query Body root Import does not expect a TypeDef.");
        }
        if (typeSpec.Form == 0x02
            && !StringComparer.Ordinal.Equals(
                rootTables.Modules.GetIdentity(
                    rootTables.TypeDefs.GetOwnerModule(typeDefRow)),
                rootSnapshot.ModuleIdentity))
        {
            throw Invalid(
                "An EN1 direct class query root changed its packed provider Module identity.");
        }
        if (typeSpec.Form == 0x03
            && (rootTables.TypeDefs.GetOwnerModule(typeDefRow)
                    != usingModuleRow
                || !StringComparer.Ordinal.Equals(
                    rootTables.Imports.GetBindingForImportRow(
                        checked((int)typeSpec.Row)).ProducerIdentity,
                    rootSnapshot.ModuleIdentity)))
        {
            throw Invalid(
                "An EN1 class query root Import changed its using or provider Module identity.");
        }
        if (rootTables.TypeDefs.GetKind(typeDefRow) != 2)
        {
            throw Invalid(
                "An EN1 class query Body root TypeSpec is not a class.");
        }

        return typeDefRow;
    }

    private sealed record C1EnumClosureEmissionLayout(
        uint TargetProfileId, uint TargetProfileRuleRevision,
        byte Kind, byte Flags, uint Alignment, uint Pack);

    private sealed record C1EnumClosureEmission(
        int DefinitionIndex,
        string Namespace, string Name, uint TypeDefFlags,
        PrimitiveType UnderlyingType, uint EnumFlags,
        ImmutableArray<C1EnumClosureEmissionLayout> Layouts,
        ImmutableArray<C1EnumClosureEmissionMember> Members,
        ImmutableArray<string> ExportKeys,
        C1EnumClosureImport? Import,
        ImmutableArray<C1EnumClosureRootImport> RootImports = default);

    private sealed record C1EnumClosureSourceConstructorPlan(
        int ClassIndex, BoundFunction Function, int FunctionIndex,
        ImmutableArray<BoundFieldAssignment> FieldAssignments,
        uint ClassTypeSpecReference,
        ImmutableArray<SourceClassMemberExport> MemberExports);

    private sealed record C1EnumClosureRetainedClassMethodBodyPlan(
        BoundStandardClassTypeView Owner,
        BoundStandardClassMethodDeclaration Method,
        int SourceMethodDefRow, int MethodDefRow);

    private static byte[] EncodeEN1ImportedClassConstructionClause(
        BoundStandardClassMethodDeclaration method,
        BoundGenericNominalTypeIdentity owner,
        Dictionary<(BoundGenericNominalTypeIdentity Owner,
            uint FieldDefinitionId), int> fieldRows,
        Dictionary<(BoundGenericNominalTypeIdentity Owner,
            uint MethodDefinitionId), int> methodRows)
    {
        if (method.ConstructionRole == 1)
        {
            var members = new List<uint>(
                method.InitializedMemberDefinitionIds.Length);
            foreach (var memberDefinitionId
                in method.InitializedMemberDefinitionIds)
            {
                if ((memberDefinitionId >> 24) != 0x14
                    || !fieldRows.TryGetValue((owner,
                        memberDefinitionId), out var fieldRow))
                {
                    throw Unsupported(
                        "An EN1 imported initializer guarantee refers to an unretained FieldDef.");
                }

                members.Add(0x1400_0000u
                    | checked((uint)fieldRow));
            }

            members.Sort();
            for (var index = 1; index < members.Count; index++)
            {
                if (members[index - 1] == members[index])
                {
                    throw new InvalidDataException(
                        "An EN1 imported initializer repeats an initialized FieldDef.");
                }
            }

            var payload = new byte[checked(
                sizeof(byte) * 4 + sizeof(uint)
                    + members.Count * sizeof(uint))];
            payload[0] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                checked((uint)members.Count));
            for (var index = 0; index < members.Count; index++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(8 + (index * sizeof(uint))),
                    members[index]);
            }

            return payload;
        }

        if (method.ConstructionRole == 2
            && method.InitializedMemberDefinitionIds.IsEmpty
            && methodRows.TryGetValue((owner,
                method.InitializerMethodDefinitionId),
                out var initializerMethodDefRow))
        {
            var payload = new byte[8];
            payload[0] = 2;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                checked((uint)initializerMethodDefRow));
            return payload;
        }

        throw Unsupported(
            "An EN1 imported projection does not reference its retained initializer MethodDef.");
    }

    private static byte[] EmitC1EnumClosureDeclarationLibrary(
        BoundProgram program, string moduleIdentity, string schemaIdentity,
        uint moduleTargetRequirement, TrRmArtifactSchema schema,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports,
        SourceGenericMethodExports? sourceGenericMethodExports,
        SourceClassExports? sourceClassExports,
        C1EnumClosureSourceEnumExports? sourceEnumExports)
    {
        var hasSourceTypeExports = sourceTypeExports is { Count: > 0 };
        var hasStandardStructViews = !program.Structures.IsEmpty
            && program.Structures.All(static structure =>
                structure.StandardLayoutOnly
                && structure.StandardTypeOrigin is
                    BoundStandardTypeOrigin {
                        Provider.SchemaIdentity: var schemaIdentity,
                    }
                    && IsSelectedC1EnumClosureSchemaIdentity(
                        schemaIdentity));
        var standardClassDeclarations = program.StandardClassDeclarations;
        var hasStandardClassDeclarations
            = !standardClassDeclarations.IsEmpty;
        if (!program.Functions.IsEmpty)
        {
            SemanticVerifier.Verify(program);
        }
        if (!program.NativePointerTypes.IsEmpty
            && !NativePointerTypeContract.IsValidTable(program))
        {
            throw new InvalidDataException(
                "The EN1 native pointer type table is invalid.");
        }
        if (!program.NativeFunctionPointerSignatures.IsEmpty
            && !NativeFunctionPointerContract.IsValidTable(
                program.NativeFunctionPointerSignatures))
        {
            throw new InvalidDataException(
                "The EN1 native function-pointer signature table is invalid.");
        }

        var classQueryFunction = GetEN1ClassLayoutQueryFunction(program,
            schemaIdentity);
        var classQueryRootIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        if (classQueryFunction is not null)
        {
            foreach (var query in GetEN1ClassLayoutQueries(
                classQueryFunction))
            {
                var instanceIndex
                    = BoundGenericNominalTypeTypes.Index(
                        query.Subject.FieldPath!.RootType);
                _ = classQueryRootIdentities.Add(
                    program.GenericNominalTypeInstances[
                        instanceIndex].Definition);
            }
        }
        var selectedClassHandleTypeIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        var pendingClassIdentities
            = new Queue<BoundGenericNominalTypeIdentity>(
                standardClassDeclarations
                    .Where(declaration =>
                        !declaration.SelectedExportKeys.IsDefaultOrEmpty
                        || classQueryRootIdentities.Contains(
                            declaration.Declaration.Identity))
                    .Select(static declaration =>
                        declaration.Declaration.Identity));
        var visitedClassIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        while (pendingClassIdentities.TryDequeue(
            out var classIdentity))
        {
            if (!visitedClassIdentities.Add(classIdentity))
            {
                continue;
            }

            var declaration
                = standardClassDeclarations.SingleOrDefault(candidate =>
                    candidate.Declaration.Identity == classIdentity)
                    ?? throw Invalid(
                        "A selected class handle dependency has no provider declaration view.");
            foreach (var field in declaration.Declaration.Fields)
            {
                if (!BoundGenericNominalTypeTypes.IsType(field.Type))
                {
                    continue;
                }

                var instanceIndex
                    = BoundGenericNominalTypeTypes.Index(field.Type);
                if ((uint)instanceIndex
                    >= (uint)program.GenericNominalTypeInstances.Length)
                {
                    throw Invalid(
                        "A selected class FieldDef has an invalid nominal type instance.");
                }

                var instance
                    = program.GenericNominalTypeInstances[instanceIndex];
                var definition
                    = program.GenericNominalTypeDefinitions.FirstOrDefault(
                        candidate =>
                            candidate.Identity == instance.Definition);
                if (definition is null)
                {
                    throw Invalid(
                        "A selected class FieldDef has no nominal TypeDef definition.");
                }

                if (definition.NominalKind == 2)
                {
                    _ = selectedClassHandleTypeIdentities.Add(
                        definition.Identity);
                    pendingClassIdentities.Enqueue(
                        definition.Identity);
                }
            }
        }
        bool IsSelectedClassQueryRootProvider(
            BoundSourceTypeProviderSnapshot provider)
            => standardClassDeclarations.Any(declaration =>
                (classQueryFunction is null
                    || classQueryRootIdentities.Contains(
                            declaration.Declaration.Identity)
                    || selectedClassHandleTypeIdentities.Contains(
                        declaration.Declaration.Identity))
                && ((!declaration.SelectedExportKeys.IsDefaultOrEmpty
                            && BoundStandardEnumDeclarationContract
                                .SnapshotsMatch(
                                    declaration.Declaration.Provider, provider))
                        || declaration.Expectations.Any(expectation =>
                            BoundStandardEnumDeclarationContract.SnapshotsMatch(
                                expectation.Binding.UsingModule, provider)
                            || (selectedClassHandleTypeIdentities.Contains(
                                    declaration.Declaration.Identity)
                                && BoundStandardEnumDeclarationContract
                                    .SnapshotsMatch(
                                        expectation.Binding.Provider,
                                        provider))));

        bool HasStandardClassTypeSpecProvider(
            BoundGenericNominalTypeInstance instance)
        {
            if (instance.TypeSpecReference == 0
                || instance.TypeSpecProviderIdentity
                    is not string typeSpecProviderIdentity
                || instance.TypeSpecForm is not (0x02 or 0x03 or 0x06))
            {
                return false;
            }

            return standardClassDeclarations.Any(declaration =>
                declaration.Declaration.Identity == instance.Definition
                && (StringComparer.Ordinal.Equals(
                        declaration.Declaration.Provider.ModuleIdentity,
                        typeSpecProviderIdentity)
                    || declaration.Expectations.Any(expectation =>
                        StringComparer.Ordinal.Equals(
                            expectation.Binding.UsingModule.ModuleIdentity,
                            typeSpecProviderIdentity))));
        }
        var standardClassIdentities = standardClassDeclarations.Select(
                static declaration => declaration.Declaration.Identity)
            .ToHashSet();
        var standardClassExpectedIdentities = standardClassDeclarations
            .SelectMany(static declaration => declaration.Expectations)
            .Select(static expectation =>
                expectation.ExpectedDeclaration.Identity)
            .ToHashSet();
        var standardStructureIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        foreach (var structure in program.Structures)
        {
            if (structure.StandardTypeOrigin is not
                BoundStandardTypeOrigin origin)
            {
                continue;
            }

            _ = standardStructureIdentities.Add(
                new BoundGenericNominalTypeIdentity(
                    origin.Provider.ModuleIdentity,
                    origin.Provider.ModuleIdentity, origin.DefinitionId));
            foreach (var expectation in origin.Expectations)
            {
                _ = standardStructureIdentities.Add(
                    new BoundGenericNominalTypeIdentity(
                        expectation.UsingModule.ModuleIdentity,
                        expectation.UsingModule.ModuleIdentity,
                        expectation.ExpectedDefinitionId));
            }
        }
        bool IsSupportedClassConstructorMethod(
            BoundStandardClassMethodDeclaration method)
            => schema.UsesConstructionContracts()
                && method.ConstructionRole is 1 or 2
                && (method.ConstructionRole != 2
                    || schema.UsesObjectConstruction())
                && method.ConstructionPropertyDefinitionId == 0
                && method.GenericParameterCount == 0
                && method.GenericParameters.IsEmpty
                && method.DependencyClauses.IsEmpty
                && (method.EffectsMask & ~6u) == 0
                && (method.ConstructionRole == 1
                    ? method.InitializerMethodDefinitionId == 0
                        && !method.InitializedMemberDefinitionIds.IsDefault
                    : (method.InitializerMethodDefinitionId >> 24) == 0x16
                        && (method.InitializerMethodDefinitionId
                            & 0x00FF_FFFF) != 0
                        && method.InitializedMemberDefinitionIds
                            .IsDefaultOrEmpty)
                && method.Parameters.All(static parameter =>
                    parameter.Mode == 0 && parameter.Flags == 0
                    && parameter.DefaultValue is null);
        var standardClassClosureIsValid
            = standardClassDeclarations.IsEmpty
                ? program.GenericNominalTypeDefinitions.IsEmpty
                : BoundStandardClassDeclarationContract.IsValidTable(
                        standardClassDeclarations,
                        program.GenericNominalTypeDefinitions)
                    && standardClassDeclarations.All(declaration =>
                        declaration.Declaration.Methods.All(
                            IsSupportedClassConstructorMethod)
                        && declaration.Expectations.All(expectation =>
                            expectation.ExpectedDeclaration.Methods
                                .All(IsSupportedClassConstructorMethod)
                            && expectation.MethodExpectations.All(
                                methodExpectation =>
                                    expectation.ExpectedDeclaration.Methods
                                        .Any(method =>
                                            method.DefinitionId
                                                == methodExpectation
                                                    .ExpectedDefinitionId))))
                    && program.GenericNominalTypeDefinitions.All(
                        definition =>
                            definition.Formals.IsEmpty
                            && (definition.NominalKind == 2
                                ? standardClassIdentities.Contains(
                                    definition.Identity)
                                    || standardClassExpectedIdentities
                                        .Contains(definition.Identity)
                                : definition.NominalKind == 1
                                    && standardStructureIdentities.Contains(
                                        definition.Identity)));
        var standardClassTypeInstancesAreValid
            = program.GenericNominalTypeInstances.IsEmpty
                || (hasStandardClassDeclarations
                    && program.GenericNominalTypeInstances.All(instance =>
                        instance is not null
                        && instance.Arguments.IsEmpty
                        && standardClassIdentities.Contains(
                            instance.Definition)
                        && (instance.TypeSpecReference != 0
                            ? HasStandardClassTypeSpecProvider(instance)
                            : classQueryFunction is not null
                                && instance.Qualifiers == 0
                                && instance.TypeSpecProviderIdentity is null
                                && instance.TypeSpecForm == 0
                                && classQueryRootIdentities.Contains(
                                    instance.Definition)));
        BoundSourceTypeProviderSnapshot? rootSnapshot = null;
        if ((sourceEnumExports is not null
                && !ReferenceEquals(sourceEnumExports.Program, program))
            || (sourceClassExports is not null
                && !ReferenceEquals(sourceClassExports.Program, program))
            || sourceGenericMethodExports?.Exports.IsEmpty == false
            || (hasSourceTypeExports && sourceEnumExports is null)
            || (sourceEnumExports is not null && hasStandardStructViews)
            || (hasStandardClassDeclarations
                && (sourceClassExports is not null
                    || sourceEnumExports is not null
                    || hasSourceTypeExports))
            || program.EntryPoint != -1
            || (!program.Functions.IsEmpty
                && program.Functions.Any(static function =>
                    function.DeclaringStruct is null)
                && classQueryFunction is null)
            || program.Functions.Any(static function =>
                function.DeclaringStruct is not null
                && !function.IsClassConstructor)
            || (!program.Structures.IsEmpty && !hasSourceTypeExports
                && !hasStandardStructViews
                && sourceClassExports?.Exports.IsEmpty != false)
            || (!program.Classes.IsEmpty && sourceClassExports is null)
            || !program.Interfaces.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty
            || !program.Buffers.IsEmpty
            || program.NativeFunctionPointerSignatures.Any(signature =>
                !IsSupportedEN1FieldFunctionPointer(signature))
            || program.NativePointerTypes.Any(pointer =>
                !IsSupportedEN1FieldPointer(pointer, program))
            || !program.NativeFixedArrayTypes.IsEmpty
            || !program.CheckedReferenceTypes.IsEmpty
            || !program.PrivateNominalTypeOrigins.IsEmpty
            || !program.PrivateClassReferences.IsEmpty
            || !program.StaticFields.IsEmpty
            || !program.StaticInitializationGroups.IsEmpty
            || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty
            || program.StandardMethodProviderClosure is not null
            || (program.StandardFieldProviderClosure is not null
                && !hasStandardStructViews
                && classQueryFunction is null)
            || (program.StandardQuerySourceAssociation is not null
                && !hasStandardStructViews
                && classQueryFunction is null)
            || program.NativeCoreRegistration is not null
            || program.NativeOutputCoreRegistration is not null
            || (program.LayoutCoreRegistration is not null
                && classQueryFunction is null)
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
            || (!program.SourceTypeDeclarations.IsEmpty
                && !hasSourceTypeExports
                && sourceClassExports?.Exports.IsEmpty != false)
            || !program.SourceGenericTypeDefinitions.IsEmpty
            || !program.SourceGenericTypeInstances.IsEmpty
            || !standardClassTypeInstancesAreValid
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.GenericMethodOperatorProtocols.IsEmpty
            || !standardClassClosureIsValid
            || !schema.UsesC1EnumClosure()
            || !BoundEnumTypes.IsValidTable(program.EnumDefinitions)
            || program.EnumDefinitions.Any(static definition =>
                definition.Identity is not
                    (BoundEnumDeclarationIdentity.Source
                    or BoundEnumDeclarationIdentity.Standard))
            || (sourceEnumExports is null
                && program.EnumDefinitions.Any(static definition =>
                    definition.Identity
                        is not BoundEnumDeclarationIdentity.Standard)))
        {
            throw Unsupported(
                "The bounded EN1 declaration route accepts mapped source enums or retained Standard enum views, with local class and non-generic struct declaration closure; only the selected minimal class query Body is admitted.");
        }

        if (hasSourceTypeExports)
        {
            if (sourceEnumExports is null
                || program.SourceTypeDeclarations.IsDefault
                || program.SourceTypeDeclarations.Select(
                    static declaration => declaration.StructureIndex)
                    .Distinct().Count()
                    != program.SourceTypeDeclarations.Length)
            {
                throw new ArgumentException(
                    "EN1 struct exports require exact local source declaration identities.",
                    nameof(sourceTypeExports));
            }

            SemanticVerifier.VerifyLibraryReferences(program);
        }
        else if (sourceClassExports is not null)
        {
            SemanticVerifier.VerifyLibraryReferences(program);
        }
        else if (hasStandardClassDeclarations)
        {
            SemanticVerifier.VerifyLibraryReferences(program);
        }
        else if (hasStandardStructViews)
        {
            SemanticVerifier.VerifyLibraryReferences(program);
            foreach (var structure in program.Structures)
            {
                var origin = structure.StandardTypeOrigin
                    ?? throw Invalid(
                        "A mixed EN1 struct re-emission lost its TypeDef origin.");
                var provider = origin.Provider;
                var referenceRoot = origin.ReferenceRoot ?? provider;
                var hasImports = !origin.Expectations.IsDefaultOrEmpty;
                var sourceSchema = provider.SchemaIdentity;
                if (!StringComparer.Ordinal.Equals(
                        referenceRoot.ModuleIdentity, moduleIdentity)
                    || !IsSelectedC1EnumClosureSchemaIdentity(sourceSchema)
                    || !IsSelectedC1EnumClosureSchemaIdentity(
                        referenceRoot.SchemaIdentity)
                    || origin.DefinitionId >> 24 != 0x13
                    || (origin.DefinitionId & 0x00FF_FFFF) == 0
                    || origin.ExportKeys.IsDefault
                    || origin.SelectedExportKeys.IsDefault
                    || origin.ExpectedDeclaration is null
                    || (hasImports
                        && (origin.IsLookupRoot
                            || !origin.SelectedExportKeys.IsEmpty))
                    || (!hasImports && origin.IsLookupRoot
                        && origin.SelectedExportKeys.IsEmpty)
                    || (!hasImports && !origin.IsLookupRoot
                        && !origin.SelectedExportKeys.IsEmpty)
                    || origin.SelectedExportKeys.Any(key =>
                        !origin.ExportKeys.Contains(key,
                            StringComparer.Ordinal))
                    || (!hasImports
                        && !SameA2SourceTypeProviderSnapshot(provider,
                            referenceRoot))
                    || !structure.StandardLayoutOnly)
                {
                    throw Unsupported(
                        "Mixed EN1 struct re-emission requires exact local or directly imported TypeDef snapshots.");
                }

                if (rootSnapshot is not null
                    && !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                        referenceRoot))
                {
                    throw Unsupported(
                        "Mixed EN1 struct re-emission cannot combine root Modules.");
                }

                if (origin.IsLookupRoot)
                {
                    rootSnapshot = referenceRoot;
                }

                var expected = origin.ExpectedDeclaration;
                var actual = structure with {
                    Fields = [.. structure.Fields.Select(
                        static field => field with {
                            StandardFieldOrigin = null,
                        })],
                    StandardTypeOrigin = null,
                    StandardLayoutOnly = false,
                };
                if (actual != expected)
                {
                    throw Unsupported(
                        "Mixed EN1 struct re-emission cannot weaken its retained declaration snapshot.");
                }
            }
        }

        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            moduleTargetRequirement,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);

        var emittedEnums = new List<C1EnumClosureEmission>(
            program.EnumDefinitions.Length);
        var emittedMemberCount = 0L;
        void EnsureEmissionCapacity(int memberCount)
        {
            if (emittedEnums.Count >= SemanticVerifier.MaximumAnalysisVisits
                || memberCount < 0
                || emittedMemberCount + memberCount
                    > SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "The EN1 enum closure exceeds the checked declaration/member work limits.");
            }

            emittedMemberCount += memberCount;
        }

        C1EnumClosureEmission CreateStandardEmission(
            int definitionIndex, BoundStandardEnumView view,
            ImmutableArray<string> exportKeys,
            C1EnumClosureImport? import,
            ImmutableArray<C1EnumClosureRootImport> rootImports = default)
        {
            EnsureEmissionCapacity(view.Members.Length);
            var layouts
                = ImmutableArray.CreateBuilder<
                    C1EnumClosureEmissionLayout>(view.Layouts.Length);
            foreach (var layout in view.Layouts)
            {
                layouts.Add(new C1EnumClosureEmissionLayout(
                    layout.TargetProfileId,
                    layout.TargetProfileRuleRevision, layout.Kind,
                    layout.Flags, layout.Alignment, layout.Pack));
            }

            var members
                = ImmutableArray.CreateBuilder<
                    C1EnumClosureEmissionMember>(view.Members.Length);
            foreach (var member in view.Members)
            {
                members.Add(new C1EnumClosureEmissionMember(
                    member.DeclarationKey, member.Name, member.Value));
            }

            return new C1EnumClosureEmission(definitionIndex,
                view.Namespace, view.Name, view.TypeDefFlags,
                view.UnderlyingType, view.EnumFlags,
                layouts.MoveToImmutable(), members.MoveToImmutable(),
                exportKeys, import, rootImports);
        }

        var standardClassEmissions
            = new List<C1EnumClosureStandardClassEmission>();
        var allowMultipleClassQueryProviders = false;
        var packedClassModuleIdentities
            = new HashSet<string>(StringComparer.Ordinal);
        if (hasStandardClassDeclarations)
        {
            if (schema.UsesPropertyInitializedState())
            {
                throw Unsupported(
                    "EN1 class re-emission does not retain PI1 property and initialized-state declarations.");
            }

            BoundSourceTypeProviderSnapshot? importedStructRootProvider
                = null;
            bool IsRetainedClassLayoutStructure(BoundStruct structure)
            {
                var origin = structure.StandardTypeOrigin;
                if (origin is null
                    || origin.ExpectedDeclaration is null
                    || origin.ExportKeys.IsDefault
                    || origin.SelectedExportKeys.IsDefault
                    || !origin.AdditionalExpectedDeclarations.IsDefaultOrEmpty
                    || !StringComparer.Ordinal.Equals(
                        origin.Provider.SchemaIdentity, schemaIdentity))
                {
                    return false;
                }

                if (origin.Expectations.IsDefaultOrEmpty)
                {
                    return origin.IsLookupRoot
                        && !origin.SelectedExportKeys.IsDefaultOrEmpty
                        && IsSelectedClassQueryRootProvider(origin.Provider);
                }

                if (origin.IsLookupRoot
                    || !origin.SelectedExportKeys.IsEmpty)
                {
                    return false;
                }

                foreach (var expectation in origin.Expectations)
                {
                    var binding = expectation;
                    if (!IsSelectedClassQueryRootProvider(
                            binding.UsingModule)
                        || !BoundStandardEnumDeclarationContract
                            .SnapshotsMatch(binding.Provider,
                                origin.Provider)
                        || binding.ProviderDefinitionId
                            != origin.DefinitionId)
                    {
                        return false;
                    }

                    importedStructRootProvider ??= binding.UsingModule;
                }

                return true;
            }

            var structureProvidersFitQueryRoots
                = program.Structures.All(IsRetainedClassLayoutStructure);
            var enumProvidersFitQueryRoots = true;
            foreach (var definition in program.EnumDefinitions)
            {
                if (definition.Identity
                        is not BoundEnumDeclarationIdentity.Standard standard
                    || !BoundStandardEnumDeclarationContract.IsValidDefinition(
                        standard.Value, definition))
                {
                    enumProvidersFitQueryRoots = false;
                    break;
                }

                var occurrence = standard.Value;
                if (occurrence.IsLookupRoot
                    && !occurrence.SelectedExportKeys.IsDefaultOrEmpty
                    && occurrence.Expectations.IsEmpty)
                {
                    enumProvidersFitQueryRoots
                        = IsSelectedClassQueryRootProvider(
                            occurrence.Declaration.Site.Snapshot);
                }
                else if (!occurrence.IsLookupRoot
                    && occurrence.SelectedExportKeys.IsEmpty
                    && !occurrence.Expectations.IsDefaultOrEmpty)
                {
                    foreach (var expectation in occurrence.Expectations)
                    {
                        var usingModule = expectation.Binding.UsingModule;
                        if (!IsSelectedClassQueryRootProvider(usingModule))
                        {
                            enumProvidersFitQueryRoots = false;
                            break;
                        }
                    }
                }
                else
                {
                    enumProvidersFitQueryRoots = false;
                }

                if (!enumProvidersFitQueryRoots)
                {
                    break;
                }
            }
            var queryDependenciesFitRoots
                = structureProvidersFitQueryRoots
                    && enumProvidersFitQueryRoots;
            allowMultipleClassQueryProviders
                = classQueryRootIdentities.Count > 1
                    && program.Classes.IsEmpty
                    && queryDependenciesFitRoots
                    && standardClassDeclarations.Where(declaration =>
                            classQueryRootIdentities.Contains(
                                declaration.Declaration.Identity))
                        .All(declaration =>
                            (!declaration.SelectedExportKeys
                                .IsDefaultOrEmpty
                                && IsSelectedClassQueryRootProvider(
                                    declaration.Declaration.Provider)
                                && declaration.Expectations.All(
                                    expectation =>
                                        IsSelectedClassQueryRootProvider(
                                            expectation.Binding.UsingModule)))
                            || (!declaration.Expectations.IsDefaultOrEmpty
                                && declaration.Expectations.All(
                                    expectation =>
                                        IsSelectedClassQueryRootProvider(
                                            expectation.Binding.UsingModule)))
                            || (declaration.Expectations.IsEmpty
                                && IsSelectedClassQueryRootProvider(
                                    declaration.Declaration.Provider)));
            if (importedStructRootProvider is not null)
            {
                rootSnapshot = importedStructRootProvider;
            }
            else if (allowMultipleClassQueryProviders
                && !program.Structures.IsEmpty)
            {
                rootSnapshot = program.Structures[0].StandardTypeOrigin!
                    .Provider;
            }
            else if (allowMultipleClassQueryProviders
                && program.EnumDefinitions.Any(static definition =>
                    definition.Identity
                        is BoundEnumDeclarationIdentity.Standard standard
                    && standard.Value.IsLookupRoot))
            {
                var enumOccurrence = (BoundEnumDeclarationIdentity.Standard)
                    program.EnumDefinitions.First(static definition =>
                        definition.Identity
                            is BoundEnumDeclarationIdentity.Standard standard
                        && standard.Value.IsLookupRoot).Identity;
                rootSnapshot
                    = enumOccurrence.Value.Declaration.Site.Snapshot;
            }

            var classRootProviders
                = new List<BoundSourceTypeProviderSnapshot>();
            foreach (var declaration in standardClassDeclarations)
            {
                if (classQueryFunction is not null
                    && !classQueryRootIdentities.Contains(
                        declaration.Declaration.Identity))
                {
                    continue;
                }

                if (!declaration.SelectedExportKeys.IsDefaultOrEmpty)
                {
                    classRootProviders.Add(
                        declaration.Declaration.Provider);
                }
                else if (declaration.Expectations.IsEmpty
                    && (StringComparer.Ordinal.Equals(
                            declaration.Declaration.Provider.ModuleIdentity,
                            moduleIdentity)
                        || classQueryRootIdentities.Contains(
                            declaration.Declaration.Identity)))
                {
                    classRootProviders.Add(
                        declaration.Declaration.Provider);
                }

                foreach (var expectation in declaration.Expectations)
                {
                    if (IsSelectedClassQueryRootProvider(
                            expectation.Binding.UsingModule))
                    {
                        classRootProviders.Add(
                            expectation.Binding.UsingModule);
                    }
                }
            }

            foreach (var provider in classRootProviders)
            {
                if (!IsSelectedC1EnumClosureSchemaIdentity(
                        provider.SchemaIdentity)
                    || !StringComparer.Ordinal.Equals(
                        provider.SchemaIdentity, schemaIdentity)
                    || GetForeignInvocationSchema(provider.SchemaIdentity)
                        != schema
                    || provider.TargetRequirement
                        != moduleTargetRequirement
                    || provider.RuntimePolicy != (uint)program.RuntimePolicy
                    || (rootSnapshot is not null
                        && !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            provider)
                        && !allowMultipleClassQueryProviders))
                {
                    throw Unsupported(
                        "EN1 class re-emission requires one exact selected root Module with matching target and runtime.");
                }

                rootSnapshot ??= provider;
            }

            if (standardClassDeclarations.Any(declaration =>
                    selectedClassHandleTypeIdentities.Contains(
                        declaration.Declaration.Identity)
                    && declaration.SelectedExportKeys.IsDefaultOrEmpty
                    && declaration.Expectations.IsEmpty
                    && !StringComparer.Ordinal.Equals(
                        declaration.Declaration.Provider.ModuleIdentity,
                        moduleIdentity)))
            {
                foreach (var declaration in standardClassDeclarations)
                {
                    if ((classQueryRootIdentities.Contains(
                            declaration.Declaration.Identity)
                        || selectedClassHandleTypeIdentities.Contains(
                            declaration.Declaration.Identity))
                        && !StringComparer.Ordinal.Equals(
                            declaration.Declaration.Provider.ModuleIdentity,
                            moduleIdentity))
                    {
                        _ = packedClassModuleIdentities.Add(
                            declaration.Declaration.Provider.ModuleIdentity);
                    }
                }

                if (!program.Classes.IsEmpty
                    || !program.Structures.IsEmpty
                    || !program.EnumDefinitions.IsEmpty
                    || !program.NativePointerTypes.IsEmpty
                    || !program.NativeFixedArrayTypes.IsEmpty
                    || !program.NativeFunctionPointerSignatures.IsEmpty
                    || !program.CheckedReferenceTypes.IsEmpty)
                {
                    throw Unsupported(
                        "The EN1 packed class provider closure currently requires class-only TypeDef and Body rows.");
                }

                foreach (var declaration in standardClassDeclarations.Where(
                    declaration =>
                        packedClassModuleIdentities.Contains(
                            declaration.Declaration.Provider.ModuleIdentity)
                        && (classQueryRootIdentities.Contains(
                                declaration.Declaration.Identity)
                            || selectedClassHandleTypeIdentities.Contains(
                                declaration.Declaration.Identity))))
                {
                    if (!declaration.Declaration.Methods.IsEmpty
                        || declaration.Declaration.Fields.Any(field =>
                            !BoundGenericNominalTypeTypes.IsType(field.Type)
                            && !IsStandardClassTypeOnlyComposedFieldType(
                                field.Type)))
                    {
                        throw Unsupported(
                            "The EN1 packed class provider closure supports scalar and packed class-handle fields only.");
                    }
                }
            }

            ImmutableArray<C1EnumClosureRootImport> GetClassRootImports(
                BoundStandardClassDeclaration declaration)
            {
                var rootImports = new List<C1EnumClosureRootImport>();
                foreach (var expectation in declaration.Expectations)
                {
                    var binding = expectation.Binding;
                    if (!IsSelectedClassQueryRootProvider(
                            binding.UsingModule))
                    {
                        throw Unsupported(
                            "An EN1 class TypeDef Import is outside the selected class query/provider closure.");
                    }

                    if ((binding.ExpectedDefinitionId >> 24) != 0x13
                        || (binding.ExpectedDefinitionId
                            & 0x00FF_FFFF) == 0
                        || binding.ImportRow <= 0
                        || !SameA2SourceTypeProviderSnapshot(
                            binding.UsingModule,
                            expectation.ExpectedDeclaration.Provider)
                        || !SameA2SourceTypeProviderSnapshot(
                            binding.Provider,
                            declaration.Declaration.Provider)
                        || binding.UsingModule.TargetRequirement
                            != moduleTargetRequirement
                        || binding.Provider.TargetRequirement
                            != moduleTargetRequirement
                        || binding.TargetRequirement
                            != moduleTargetRequirement
                        || binding.UsingModule.RuntimePolicy
                            != (uint)program.RuntimePolicy
                        || binding.Provider.RuntimePolicy
                            != (uint)program.RuntimePolicy
                        || binding.RuntimePolicy
                            != (uint)program.RuntimePolicy
                        || binding.ProviderDefinitionId
                            != declaration.Declaration.Identity.DefinitionId
                        || binding.ExpectedDefinitionId
                            != expectation.ExpectedDeclaration.Identity
                                .DefinitionId)
                    {
                        throw Invalid(
                            "An EN1 class TypeDef Import lost its expected or provider identity.");
                    }

                    var rootImport = new C1EnumClosureRootImport(
                        binding.UsingModule.ModuleIdentity,
                        binding.ImportRow);
                    if (!rootImports.Contains(rootImport))
                    {
                        rootImports.Add(rootImport);
                    }
                }

                return [.. rootImports];
            }

            ImmutableArray<BoundStandardClassFieldExpectation>
                GetDirectClassFieldExpectations(
                    BoundStandardClassTypeView classView)
            {
                var fieldExpectations = ImmutableArray.CreateBuilder<
                    BoundStandardClassFieldExpectation>();
                foreach (var field in classView.Fields)
                {
                    if (field.SelectedExportKeys.IsEmpty)
                    {
                        continue;
                    }
                    if ((field.Flags & 3) != 2
                        || (field.Flags & 8) != 0
                        || field.SelectedExportKeys.Any(key =>
                            !field.ExportKeys.Contains(key,
                                StringComparer.Ordinal)))
                    {
                        throw Invalid(
                            "An EN1 imported class query selects an invalid FieldDef key.");
                    }

                    fieldExpectations.Add(
                        new BoundStandardClassFieldExpectation(
                            0, 0, field.DefinitionId,
                            field.OwnerDefinitionId,
                            field.DefinitionId,
                            field.OwnerDefinitionId,
                            field.SelectedExportKeys[0],
                            classView.Provider));
                }

                return fieldExpectations.ToImmutable();
            }

            foreach (var declaration in standardClassDeclarations)
            {
                var classView = declaration.Declaration;
                var isClassQueryRoot = classQueryRootIdentities.Contains(
                    classView.Identity);
                var isSelectedHandleDependency
                    = selectedClassHandleTypeIdentities.Contains(
                        classView.Identity);
                if (classQueryFunction is not null
                    && !isClassQueryRoot
                    && !isSelectedHandleDependency)
                {
                    continue;
                }

                if (rootSnapshot is null
                    || (!allowMultipleClassQueryProviders
                        && !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            classView.Provider)
                        && !isSelectedHandleDependency
                        && !(isClassQueryRoot
                            && !declaration.Expectations.IsDefaultOrEmpty)))
                {
                    continue;
                }

                if ((classView.Identity.DefinitionId >> 24) != 0x13
                    || (classView.Identity.DefinitionId
                        & 0x00FF_FFFF) == 0)
                {
                    throw Invalid(
                        "An EN1 class root has an invalid TypeDef identity.");
                }

                var rootTypeDefRow = checked((int)(
                    classView.Identity.DefinitionId & 0x00FF_FFFF));
                if (packedClassModuleIdentities.Contains(
                        classView.Provider.ModuleIdentity))
                {
                    if (declaration.Expectations.Any(expectation =>
                            expectation.Binding.ImportRow <= 0
                            || !BoundEnumTypes
                                .IsValidModuleDeclarationKey(
                                    expectation.Binding.ExportKey)
                            || !SameA2SourceTypeProviderSnapshot(
                                expectation.Binding.UsingModule,
                                expectation.ExpectedDeclaration.Provider)
                            || !SameA2SourceTypeProviderSnapshot(
                                expectation.Binding.Provider,
                                classView.Provider)
                            || expectation.Binding.ProviderDefinitionId
                                != classView.Identity.DefinitionId
                            || expectation.Binding.ExpectedDefinitionId
                                != expectation.ExpectedDeclaration.Identity
                                    .DefinitionId
                            || expectation.Binding.TargetRequirement
                                != moduleTargetRequirement
                            || expectation.Binding.RuntimePolicy
                                != (uint)program.RuntimePolicy
                            || expectation.Binding.UsingModule.TargetRequirement
                                != moduleTargetRequirement
                            || expectation.Binding.UsingModule.RuntimePolicy
                                != (uint)program.RuntimePolicy
                            || (!StringComparer.Ordinal.Equals(
                                    expectation.Binding.UsingModule
                                        .ModuleIdentity,
                                    moduleIdentity)
                                && !packedClassModuleIdentities.Contains(
                                    expectation.Binding.UsingModule
                                        .ModuleIdentity))))
                    {
                        throw Invalid(
                            "An EN1 packed class TypeDef lost its provider or expected-module identity.");
                    }

                    standardClassEmissions.Add(
                        new C1EnumClosureStandardClassEmission(
                            classView, declaration.ExportKeys, null,
                            rootTypeDefRow, []) {
                            OwnerModuleIdentity
                                = classView.Provider.ModuleIdentity,
                        });
                    continue;
                }

                if (!declaration.Expectations.IsDefaultOrEmpty
                    && declaration.Expectations.Any(expectation =>
                        !expectation.MethodExpectations.IsDefaultOrEmpty))
                {
                    foreach (var expectation in declaration.Expectations)
                    {
                        var binding = expectation.Binding;
                        if (rootSnapshot is null
                            || !SameA2SourceTypeProviderSnapshot(
                                rootSnapshot, binding.UsingModule))
                        {
                            continue;
                        }

                        var expectedView = expectation.ExpectedDeclaration;
                        if ((binding.ExpectedDefinitionId >> 24) != 0x13
                            || (binding.ExpectedDefinitionId
                                & 0x00FF_FFFF) == 0
                            || binding.ImportRow <= 0
                            || !SameA2SourceTypeProviderSnapshot(
                                rootSnapshot, expectedView.Provider)
                            || !SameA2SourceTypeProviderSnapshot(
                                classView.Provider, binding.Provider)
                            || binding.ProviderDefinitionId
                                != classView.Identity.DefinitionId
                            || binding.ExpectedDefinitionId
                                != expectedView.Identity.DefinitionId)
                        {
                            throw Invalid(
                                "An EN1 class TypeDef Import lost its expected or provider identity.");
                        }

                        standardClassEmissions.Add(
                            new C1EnumClosureStandardClassEmission(
                                expectedView, [],
                                new C1EnumClosureImport(
                                    binding.ProducerIdentity,
                                    binding.ExportKey,
                                    binding.TargetRequirement,
                                    binding.RuntimePolicy,
                                    binding.ImportRow,
                                    binding.UsingModule.ModuleIdentity),
                                checked((int)(binding.ExpectedDefinitionId
                                    & 0x00FF_FFFF)),
                                expectation.FieldExpectations) {
                                MethodExpectations
                                    = expectation.MethodExpectations,
                            });
                    }

                    continue;
                }

                if (StringComparer.Ordinal.Equals(
                        classView.Provider.ModuleIdentity, moduleIdentity))
                {
                    if (!declaration.Expectations.IsEmpty)
                    {
                        throw Unsupported(
                            "An EN1 local class TypeDef cannot retain imported class expectations.");
                    }

                    standardClassEmissions.Add(
                        new C1EnumClosureStandardClassEmission(
                            classView, declaration.ExportKeys, null,
                            rootTypeDefRow, []));
                    continue;
                }

                C1EnumClosureImport typeImport;
                ImmutableArray<C1EnumClosureRootImport> rootImports;
                if (!declaration.SelectedExportKeys.IsDefaultOrEmpty)
                {
                    if (declaration.ExportKeys.IsDefaultOrEmpty
                        || declaration.SelectedExportKeys.Any(key =>
                            !declaration.ExportKeys.Contains(key,
                                StringComparer.Ordinal)))
                    {
                        throw Invalid(
                            "An EN1 selected class TypeDef key is absent from its provider declaration.");
                    }

                    rootImports = GetClassRootImports(declaration);
                    typeImport = new C1EnumClosureImport(
                        classView.Provider.ModuleIdentity,
                        declaration.SelectedExportKeys[0],
                        classView.Provider.TargetRequirement,
                        classView.Provider.RuntimePolicy,
                        RootImports: rootImports);
                }
                else if (!declaration.Expectations.IsDefaultOrEmpty
                    && (isSelectedHandleDependency || isClassQueryRoot))
                {
                    rootImports = GetClassRootImports(declaration);
                    var binding = declaration.Expectations[0].Binding;
                    typeImport = new C1EnumClosureImport(
                        binding.ProducerIdentity, binding.ExportKey,
                        binding.TargetRequirement, binding.RuntimePolicy,
                        RootImports: rootImports);
                }
                else
                {
                    throw Unsupported(
                        "An EN1 class handle dependency has no exact selected TypeDef Import.");
                }

                standardClassEmissions.Add(
                    new C1EnumClosureStandardClassEmission(
                        classView, [], typeImport,
                        rootTypeDefRow,
                        GetDirectClassFieldExpectations(classView)));
            }

            standardClassEmissions.Sort((left, right) =>
            {
                var leftOwner = left.OwnerModuleIdentity ?? moduleIdentity;
                var rightOwner = right.OwnerModuleIdentity ?? moduleIdentity;
                var leftOwnerOrder
                    = StringComparer.Ordinal.Equals(
                        leftOwner, moduleIdentity) ? 0 : 1;
                var rightOwnerOrder
                    = StringComparer.Ordinal.Equals(
                        rightOwner, moduleIdentity) ? 0 : 1;
                var ownerOrder = leftOwnerOrder.CompareTo(rightOwnerOrder);
                if (ownerOrder != 0)
                {
                    return ownerOrder;
                }

                var ownerIdentityOrder = StringComparer.Ordinal.Compare(
                    leftOwner, rightOwner);
                return ownerIdentityOrder != 0
                    ? ownerIdentityOrder
                    : left.RootTypeDefRow.CompareTo(right.RootTypeDefRow);
            });
            for (var index = 1;
                index < standardClassEmissions.Count; index++)
            {
                var previous = standardClassEmissions[index - 1];
                var current = standardClassEmissions[index];
                if (StringComparer.Ordinal.Equals(
                        previous.View.Provider.ModuleIdentity,
                        current.View.Provider.ModuleIdentity)
                    && previous.RootTypeDefRow == current.RootTypeDefRow)
                {
                    throw Invalid(
                        "EN1 class re-emission contains duplicate root TypeDef views.");
                }
            }
        }

        if (sourceEnumExports is not null)
        {
            var sourceExportsByIndex = sourceEnumExports.Exports
                .ToDictionary(static export => export.EnumIndex);
            var sourceDefinitionCount = program.EnumDefinitions.Count(
                static definition => definition.Identity
                    is BoundEnumDeclarationIdentity.Source);
            if (sourceExportsByIndex.Count != sourceDefinitionCount)
            {
                throw new InvalidDataException(
                    "The EN1 source enum map does not cover the complete source declaration table.");
            }

            void AddFreshStandardEnumImports(int definitionIndex,
                BoundStandardEnumOccurrence occurrence)
            {
                var view = occurrence.Declaration;
                var provider = view.Site.Snapshot;
                if (!IsSelectedC1EnumClosureSchemaIdentity(
                        provider.SchemaIdentity)
                    || !StringComparer.Ordinal.Equals(
                        provider.SchemaIdentity, schemaIdentity)
                    || GetForeignInvocationSchema(provider.SchemaIdentity)
                        != schema
                    || provider.RuntimePolicy != (uint)program.RuntimePolicy)
                {
                    throw Unsupported(
                        "A fresh EN1 source declaration requires a selected Standard enum provider with the exact schema and runtime.");
                }

                var selectedEdges = new HashSet<(
                    string ProducerIdentity, string ExportKey,
                    uint TargetRequirement, uint RuntimePolicy)>();
                foreach (var expectation in occurrence.Expectations)
                {
                    var binding = expectation.Binding;
                    var expected = expectation.ExpectedDeclaration;
                    if (!BoundStandardEnumDeclarationContract
                            .SnapshotsMatch(binding.UsingModule,
                                expected.Site.Snapshot)
                        || binding.ExpectedDefinitionId
                            != expected.Site.DefinitionId
                        || !BoundStandardEnumDeclarationContract
                            .SnapshotsMatch(binding.Provider, provider)
                        || binding.ProviderDefinitionId
                            != view.Site.DefinitionId
                        || !BoundStandardEnumDeclarationContract
                            .ContractMatches(expected, view)
                        || !StringComparer.Ordinal.Equals(
                            binding.ProducerIdentity,
                            provider.ModuleIdentity)
                        || binding.TargetRequirement
                            != provider.TargetRequirement
                        || binding.RuntimePolicy != provider.RuntimePolicy
                        || !BoundEnumTypes.IsValidModuleDeclarationKey(
                            binding.ExportKey)
                        || !occurrence.ExportKeys.Contains(
                            binding.ExportKey, StringComparer.Ordinal))
                    {
                        throw Invalid(
                            "A fresh EN1 source enum Import lost its checked provider expectation.");
                    }

                    _ = selectedEdges.Add((binding.ProducerIdentity,
                        binding.ExportKey, binding.TargetRequirement,
                        binding.RuntimePolicy));
                    emittedEnums.Add(CreateStandardEmission(
                        definitionIndex, expected, [],
                        new C1EnumClosureImport(
                            binding.ProducerIdentity, binding.ExportKey,
                            binding.TargetRequirement,
                            binding.RuntimePolicy)));
                }

                foreach (var exportKey in occurrence.SelectedExportKeys)
                {
                    var edge = (provider.ModuleIdentity, exportKey,
                        provider.TargetRequirement, provider.RuntimePolicy);
                    if (!selectedEdges.Add(edge))
                    {
                        continue;
                    }

                    emittedEnums.Add(CreateStandardEmission(
                        definitionIndex, view, [],
                        new C1EnumClosureImport(provider.ModuleIdentity,
                            exportKey, provider.TargetRequirement,
                            provider.RuntimePolicy)));
                }

                if (selectedEdges.Count == 0)
                {
                    throw Unsupported(
                        "Fresh EN1 source emission cannot publish an unselected closure-only Standard enum without a retained Import edge.");
                }
            }

            for (var enumIndex = 0;
                enumIndex < program.EnumDefinitions.Length; enumIndex++)
            {
                var definition = program.EnumDefinitions[enumIndex];
                if (definition.Identity
                    is BoundEnumDeclarationIdentity.Standard standard)
                {
                    AddFreshStandardEnumImports(enumIndex,
                        standard.Value);
                    continue;
                }

                if (definition.Identity
                        is not BoundEnumDeclarationIdentity.Source
                    || !sourceExportsByIndex.TryGetValue(enumIndex,
                        out var sourceExport))
                {
                    throw Invalid(
                        "The EN1 source enum map no longer matches the checked declaration table.");
                }

                if (!ReferenceEquals(sourceExport.Definition, definition)
                    || sourceExport.MemberDeclarationKeys.IsDefault
                    || sourceExport.MemberDeclarationKeys.Length
                        != definition.Members.Length)
                {
                    throw new InvalidDataException(
                        "The EN1 source enum assignment no longer matches its checked definition.");
                }

                var separator = definition.Name.LastIndexOf(
                    '.', StringComparison.Ordinal);
                var namespaceName = separator < 0
                    ? "" : definition.Name[..separator];
                var typeName = separator < 0
                    ? definition.Name : definition.Name[(separator + 1)..];
                if (string.IsNullOrWhiteSpace(typeName) || separator == 0)
                {
                    throw Unsupported(
                        "EN1 enum names require valid namespace and type-name components.");
                }

                EnsureEmissionCapacity(definition.Members.Length);
                var members
                    = ImmutableArray.CreateBuilder<
                        C1EnumClosureEmissionMember>(
                        definition.Members.Length);
                for (var memberIndex = 0;
                    memberIndex < definition.Members.Length; memberIndex++)
                {
                    var member = definition.Members[memberIndex];
                    members.Add(new C1EnumClosureEmissionMember(
                        sourceExport.MemberDeclarationKeys[memberIndex],
                        member.Name, member.Value));
                }

                if (!TryGetEnumTypeCode(definition.UnderlyingType,
                    out var underlyingTypeCode))
                {
                    throw new InvalidDataException(
                        "The EN1 source enum has no admitted integer base.");
                }

                var needsWindowsProfile
                    = definition.BlittableTargetRequirement != 0
                        || underlyingTypeCode is 0x10 or 0x11;
                var layouts
                    = ImmutableArray.CreateBuilder<
                        C1EnumClosureEmissionLayout>(
                        needsWindowsProfile ? 2 : 1);
                layouts.Add(new C1EnumClosureEmissionLayout(0, 0, 0, 0,
                    0, 0));
                if (needsWindowsProfile)
                {
                    layouts.Add(new C1EnumClosureEmissionLayout(1, 1, 0,
                        definition.BlittableTargetRequirement == 0
                            ? (byte)0 : (byte)1, 0, 0));
                }

                var visibility = definition.IsPublic ? 2u
                    : sourceExport.Declaration.IsFileLocal ? 0u : 1u;
                emittedEnums.Add(new C1EnumClosureEmission(enumIndex,
                    namespaceName,
                    typeName, 0x10u | visibility,
                    definition.UnderlyingType,
                    definition.IsFlags ? 1u : 0u,
                    layouts.MoveToImmutable(), members.MoveToImmutable(),
                    sourceExport.ExportKeys, null));
            }
        }
        else
        {
            if (hasStandardStructViews)
            {
                foreach (var structure in program.Structures)
                {
                    var origin = structure.StandardTypeOrigin!;
                    var hasRootOwnedImport
                        = rootSnapshot is
                            BoundSourceTypeProviderSnapshot selectedRoot
                            && !origin.Expectations.IsDefaultOrEmpty
                            && origin.Expectations.Any(expectation =>
                                BoundStandardEnumDeclarationContract
                                    .SnapshotsMatch(
                                        expectation.UsingModule,
                                        selectedRoot)
                                && BoundStandardEnumDeclarationContract
                                    .SnapshotsMatch(
                                        expectation.Provider,
                                        origin.Provider)
                                && expectation.ProviderDefinitionId
                                    == origin.DefinitionId);
                    if (rootSnapshot is not null
                        && !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            origin.Provider)
                        && !allowMultipleClassQueryProviders
                        && !hasRootOwnedImport)
                    {
                        throw Unsupported(
                            "Mixed EN1 struct and enum re-emission cannot combine root Modules.");
                    }
                    if (origin.IsLookupRoot
                        && (!allowMultipleClassQueryProviders
                            || rootSnapshot is null))
                    {
                        rootSnapshot = origin.Provider;
                    }
                }
            }

            foreach (var definition in program.EnumDefinitions)
            {
                var occurrence
                    = ((BoundEnumDeclarationIdentity.Standard)
                        definition.Identity).Value;
                var snapshot = occurrence.Declaration.Site.Snapshot;
                if (!IsSelectedC1EnumClosureSchemaIdentity(
                        snapshot.SchemaIdentity)
                    || !StringComparer.Ordinal.Equals(
                        snapshot.SchemaIdentity, schemaIdentity)
                    || GetForeignInvocationSchema(snapshot.SchemaIdentity)
                        != schema)
                {
                    throw Unsupported(
                        "EN1 source-free enum emission requires declarations bound to the exact selected schema.");
                }

                if (!occurrence.SelectedExportKeys.IsEmpty)
                {
                    if (rootSnapshot is not null
                        && !BoundStandardEnumDeclarationContract
                            .SnapshotsMatch(rootSnapshot, snapshot)
                        && !allowMultipleClassQueryProviders)
                    {
                        throw Unsupported(
                            "EN1 source-free enum emission does not combine multiple root Modules.");
                    }

                    rootSnapshot ??= snapshot;
                }
            }

            if (rootSnapshot is null
                || rootSnapshot.TargetRequirement
                    != moduleTargetRequirement
                || rootSnapshot.RuntimePolicy != (uint)program.RuntimePolicy)
            {
                throw Unsupported(
                    "EN1 source-free enum emission requires a selected root Module with the exact target/runtime binding.");
            }

            for (var definitionIndex = 0;
                definitionIndex < program.EnumDefinitions.Length;
                definitionIndex++)
            {
                var definition = program.EnumDefinitions[definitionIndex];
                var occurrence
                    = ((BoundEnumDeclarationIdentity.Standard)
                        definition.Identity).Value;
                var snapshot = occurrence.Declaration.Site.Snapshot;
                if (occurrence.Expectations.IsEmpty)
                {
                    if (!BoundStandardEnumDeclarationContract.SnapshotsMatch(
                            occurrence.Declaration.Site.Snapshot,
                            rootSnapshot)
                        && !allowMultipleClassQueryProviders)
                    {
                        throw Unsupported(
                            "EN1 source-free enum emission cannot re-own a declaration from another Module.");
                    }

                    if (!StringComparer.Ordinal.Equals(
                            snapshot.ModuleIdentity, moduleIdentity))
                    {
                        if (!occurrence.IsLookupRoot
                            || occurrence.SelectedExportKeys.IsDefaultOrEmpty)
                        {
                            throw Unsupported(
                                "An EN1 foreign enum dependency requires an exact selected TypeDef ExportKey.");
                        }

                        emittedEnums.Add(CreateStandardEmission(
                            definitionIndex, occurrence.Declaration, [],
                            new C1EnumClosureImport(
                                snapshot.ModuleIdentity,
                                occurrence.SelectedExportKeys[0],
                                snapshot.TargetRequirement,
                                snapshot.RuntimePolicy)));
                    }
                    else
                    {
                        emittedEnums.Add(CreateStandardEmission(
                            definitionIndex, occurrence.Declaration,
                            occurrence.ExportKeys, null));
                    }
                    continue;
                }

                var rootExpectations = occurrence.Expectations
                    .Where(expectation =>
                        IsSelectedClassQueryRootProvider(
                            expectation.Binding.UsingModule))
                    .ToArray();
                if (rootExpectations.Length == 0)
                {
                    continue;
                }

                var firstExpectation = rootExpectations[0];
                var firstBinding = firstExpectation.Binding;
                var rootImports = rootExpectations.Select(expectation =>
                        new C1EnumClosureRootImport(
                            expectation.Binding.UsingModule.ModuleIdentity,
                            expectation.Binding.ImportRow))
                    .Distinct().ToImmutableArray();
                emittedEnums.Add(CreateStandardEmission(
                    definitionIndex, firstExpectation.ExpectedDeclaration, [],
                    new C1EnumClosureImport(
                        firstBinding.ProducerIdentity,
                        firstBinding.ExportKey,
                        firstBinding.TargetRequirement,
                        firstBinding.RuntimePolicy),
                    rootImports));
            }
        }

        if (emittedEnums.Count == 0)
        {
            throw Unsupported(
                "EN1 declaration emission requires retained enum declarations.");
        }

        var sourceStructExportsByIndex
            = new SortedDictionary<int, List<string>>();
        var sourceStructImportsByIndex
            = new Dictionary<int, List<C1EnumClosureImport>>();
        var rootTypeDefRowsByStructureIndex
            = new List<(int StructureIndex, string RootModuleIdentity,
                int TypeDefRow)>();
        var sourceStructFieldExportsByIndex = new Dictionary<
            (int StructureIndex, int FieldIndex), List<string>>();
        var sourceStructFieldImportsByIndex = new Dictionary<
            (int StructureIndex, int FieldIndex),
            List<C1EnumClosureImport>>();
        var sourceStructDeclarationsByIndex
            = program.SourceTypeDeclarations.IsDefault
                ? new Dictionary<int, BoundSourceTypeDeclaration>()
                : program.SourceTypeDeclarations.ToDictionary(
                    static declaration => declaration.StructureIndex);
        var usedExportKeys = new HashSet<string>(StringComparer.Ordinal);
        var moduleExportKeysByIdentity = new Dictionary<string,
            HashSet<string>>(StringComparer.Ordinal) {
            [moduleIdentity] = usedExportKeys,
        };
        HashSet<string> GetModuleExportKeys(string identity)
        {
            if (!moduleExportKeysByIdentity.TryGetValue(identity,
                    out var exportKeys))
            {
                exportKeys = new HashSet<string>(StringComparer.Ordinal);
                moduleExportKeysByIdentity.Add(identity, exportKeys);
            }

            return exportKeys;
        }

        var sourceClassExportsByIndex
            = new SortedDictionary<int, List<string>>();
        var sourceClassFieldExportsByIndex = new Dictionary<
            (int ClassIndex, int FieldIndex), List<string>>();
        var sourceClassConstructorExportsByIndex = new Dictionary<
            (int ClassIndex, int FunctionIndex),
            List<SourceClassMemberExport>>();
        var retainedClassIndexes = new SortedSet<int>();
        var sourceClassAssociation = program.SourceClassProgramAssociation;
        var localClassIndexes = sourceClassAssociation is null
            || sourceClassAssociation.Declarations.IsDefault
                ? new HashSet<int>()
                : sourceClassAssociation.Declarations.Select(
                    static declaration => declaration.ClassIndex)
                    .ToHashSet();
        if (sourceClassExports is not null)
        {
            if (sourceClassAssociation is null
                || !ReferenceEquals(sourceClassAssociation.Program, program)
                || sourceClassAssociation.Declarations.IsDefault)
            {
                throw new ArgumentException(
                    "EN1 class exports require exact compiler-owned source class declarations.",
                    nameof(sourceClassExports));
            }

            foreach (var classExport in sourceClassExports.Exports)
            {
                var classIndex = classExport.Declaration.ClassIndex;
                if ((uint)classIndex >= (uint)program.Classes.Length
                    || !localClassIndexes.Contains(classIndex)
                    || !ReferenceEquals(program.Classes[classIndex],
                        classExport.Declaration.DeclarationSnapshot)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        classExport.ExportKey)
                    || !usedExportKeys.Add(classExport.ExportKey))
                {
                    throw new ArgumentException(
                        "An EN1 class mapping does not identify one exact local declaration with a unique ExportKey.",
                        nameof(sourceClassExports));
                }

                if (!sourceClassExportsByIndex.TryGetValue(classIndex,
                        out var classExportKeys))
                {
                    classExportKeys = [];
                    sourceClassExportsByIndex.Add(classIndex,
                        classExportKeys);
                }
                classExportKeys.Add(classExport.ExportKey);

                foreach (var memberExport in classExport.MemberExports)
                {
                    if (memberExport.Kind == SourceClassMemberKind.Constructor)
                    {
                        if (!schema.UsesConstructionContracts()
                            || (memberExport.ConstructionProjectionExportKey
                                is not null
                                && !schema.UsesObjectConstruction())
                            || (uint)memberExport.FunctionIndex
                                >= (uint)program.Functions.Length)
                        {
                            throw Unsupported(
                                "The selected EN1 agreement cannot emit this constructor or its construction projection.");
                        }

                        var constructor
                            = program.Functions[memberExport.FunctionIndex];
                        if (!constructor.IsClassConstructor
                            || constructor.DeclaringStruct
                                != program.Classes[classIndex].Name)
                        {
                            throw new ArgumentException(
                                "An EN1 constructor ExportKey must select the checked initializer of its declaring class.",
                                nameof(sourceClassExports));
                        }

                        if (!sourceClassConstructorExportsByIndex.TryGetValue(
                                (classIndex, memberExport.FunctionIndex),
                                out var constructorExports))
                        {
                            constructorExports = [];
                            sourceClassConstructorExportsByIndex.Add(
                                (classIndex, memberExport.FunctionIndex),
                                constructorExports);
                        }

                        constructorExports.Add(memberExport);
                        continue;
                    }

                    if (memberExport.Kind != SourceClassMemberKind.Field)
                    {
                        throw Unsupported(
                            "The bounded EN1 class route emits FieldDefs and constructor initializers only; methods and properties require their complete Body closure.");
                    }

                    var fields = program.Classes[classIndex].Fields;
                    if ((uint)memberExport.FieldIndex >= (uint)fields.Length)
                    {
                        throw new ArgumentException(
                            "An EN1 class FieldDef ExportKey must select one public direct field and be Module-unique.",
                            nameof(sourceClassExports));
                    }

                    var field = fields[memberExport.FieldIndex];
                    if (field is null
                        || field.IsPrivate || field.IsInternal
                        || !BoundEnumTypes.IsValidModuleDeclarationKey(
                            memberExport.ExportKey)
                        || !usedExportKeys.Add(memberExport.ExportKey))
                    {
                        throw new ArgumentException(
                            "An EN1 class FieldDef ExportKey must select one public direct field and be Module-unique.",
                            nameof(sourceClassExports));
                    }

                    if (!sourceClassFieldExportsByIndex.TryGetValue(
                            (classIndex, memberExport.FieldIndex),
                            out var fieldKeys))
                    {
                        fieldKeys = [];
                        sourceClassFieldExportsByIndex.Add(
                            (classIndex, memberExport.FieldIndex), fieldKeys);
                    }
                    fieldKeys.Add(memberExport.ExportKey);
                }
            }
        }

        bool IsSupportedSourceStruct(BoundStruct structure)
            => hasStandardStructViews
                ? structure.StandardLayoutOnly
                    && structure.StandardTypeOrigin is not null
                    && !structure.IsUnique
                    && structure.CleanupMethod is null
                    && structure.SourceGenericTypeDefinitionIndex < 0
                    && structure.LayoutKind == 0
                    && structure.BlittableTargetRequirement == 0
                    && structure.Packing == 0
                    && structure.DeclaredAlignment is null
                : IsC1M1SourceStructLayoutSupported(structure);

        bool TryGetEN1SourceStructFieldType(PrimitiveType type,
            out int structureIndex)
        {
            if (TryGetC1M1SourceStructFieldType(type,
                    sourceStructDeclarationsByIndex,
                    out structureIndex))
            {
                return true;
            }

            var candidate = (int)type - 100;
            if (hasStandardStructViews && candidate >= 0
                && candidate < program.Structures.Length
                && candidate < ManagedClassTypes.First - 100
                && !BoundEnumTypes.IsType(type))
            {
                structureIndex = candidate;
                return true;
            }

            structureIndex = -1;
            return false;
        }

        foreach (var emittedEnum in emittedEnums)
        {
            foreach (var exportKey in emittedEnum.ExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !usedExportKeys.Add(exportKey))
                {
                    throw new ArgumentException(
                        "EN1 TypeDef ExportKeys must be valid and Module-unique.",
                        nameof(sourceEnumExports));
                }
            }
        }

        if (hasSourceTypeExports)
        {
            foreach (var typeExport in sourceTypeExports!)
            {
                if (typeExport is null
                    || string.IsNullOrWhiteSpace(typeExport.Name)
                    || string.IsNullOrEmpty(typeExport.ExportKey)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        typeExport.ExportKey)
                    || typeExport.GenericArity < -1
                    || typeExport.GenericArity > 0
                    || !typeExport.EnumMemberDeclarationKeys.IsDefaultOrEmpty
                    || !typeExport.InterfaceMethodExports.IsDefaultOrEmpty
                    || !typeExport.InterfaceAssociatedTypeExports
                        .IsDefaultOrEmpty)
                {
                    throw new ArgumentException(
                        "EN1 source type exports select non-generic structs and cannot assign enum members or interface methods.",
                        nameof(sourceTypeExports));
                }

                var matchingDeclarations = program.SourceTypeDeclarations
                    .Where(declaration =>
                        StringComparer.Ordinal.Equals(declaration.Namespace,
                            typeExport.Namespace)
                        && StringComparer.Ordinal.Equals(declaration.Name,
                            typeExport.Name))
                    .ToArray();
                if (matchingDeclarations.Length != 1)
                {
                    throw new ArgumentException(
                        "An EN1 source TypeDef mapping must select one exact local struct declaration.",
                        nameof(sourceTypeExports));
                }

                var declaration = matchingDeclarations[0];
                if ((uint)declaration.StructureIndex
                        >= (uint)program.Structures.Length
                    || !ReferenceEquals(program.Structures[
                        declaration.StructureIndex],
                        declaration.DeclarationSnapshot)
                    || declaration.DeclarationSnapshot.IsPrivate
                    || declaration.DeclarationSnapshot.IsInternal
                    || !IsC1M1SourceStructLayoutSupported(
                        declaration.DeclarationSnapshot))
                {
                    throw new ArgumentException(
                        "EN1 source TypeDef mappings can select public, supported local structs only.",
                        nameof(sourceTypeExports));
                }

                if (!usedExportKeys.Add(typeExport.ExportKey))
                {
                    throw new ArgumentException(
                        "EN1 TypeDef and FieldDef ExportKeys must be Module-unique.",
                        nameof(sourceTypeExports));
                }

                if (!sourceStructExportsByIndex.TryGetValue(
                        declaration.StructureIndex, out var typeKeys))
                {
                    typeKeys = [];
                    sourceStructExportsByIndex.Add(
                        declaration.StructureIndex, typeKeys);
                }
                typeKeys.Add(typeExport.ExportKey);

                if (typeExport.FieldExports.IsDefault)
                {
                    continue;
                }

                var structure = declaration.DeclarationSnapshot;
                foreach (var fieldExport in typeExport.FieldExports)
                {
                    if (fieldExport is null
                        || string.IsNullOrEmpty(fieldExport.ExportKey)
                        || !BoundEnumTypes.IsValidModuleDeclarationKey(
                            fieldExport.ExportKey)
                        || !usedExportKeys.Add(fieldExport.ExportKey))
                    {
                        throw new ArgumentException(
                            "EN1 FieldDef ExportKeys must be valid and Module-unique.",
                            nameof(sourceTypeExports));
                    }

                    var fieldIndex = -1;
                    for (var index = 0;
                        index < structure.Fields.Length; index++)
                    {
                        if (StringComparer.Ordinal.Equals(
                                structure.Fields[index].Name,
                                fieldExport.Name))
                        {
                            if (fieldIndex >= 0)
                            {
                                throw new InvalidDataException(
                                    "An EN1 source struct has duplicate field names.");
                            }
                            fieldIndex = index;
                        }
                    }

                    if (fieldIndex < 0
                        || structure.Fields[fieldIndex].IsPrivate
                        || structure.Fields[fieldIndex].IsInternal)
                    {
                        throw new ArgumentException(
                            "An EN1 FieldDef mapping must select one public direct field of its mapped struct.",
                            nameof(sourceTypeExports));
                    }

                    if (!sourceStructFieldExportsByIndex.TryGetValue(
                            (declaration.StructureIndex, fieldIndex),
                            out var fieldKeys))
                    {
                        fieldKeys = [];
                        sourceStructFieldExportsByIndex.Add(
                            (declaration.StructureIndex, fieldIndex),
                            fieldKeys);
                    }
                    fieldKeys.Add(fieldExport.ExportKey);
                }
            }
        }

        if (hasStandardStructViews)
        {
            for (var structureIndex = 0;
                structureIndex < program.Structures.Length;
                structureIndex++)
            {
                var structure = program.Structures[structureIndex];
                var origin = structure.StandardTypeOrigin
                    ?? throw Invalid(
                        "A mixed EN1 struct re-emission lost its TypeDef origin.");
                var directForeignStructure
                    = origin.Expectations.IsDefaultOrEmpty
                        && !StringComparer.Ordinal.Equals(
                            origin.Provider.ModuleIdentity, moduleIdentity);
                if (rootSnapshot is null
                    || origin.ExportKeys.IsDefault
                    || origin.SelectedExportKeys.IsDefault
                    || origin.ExpectedDeclaration is null)
                {
                    throw Invalid(
                        "A mixed EN1 struct re-emission changed its provider or ExportKey snapshot.");
                }

                if (!origin.Expectations.IsDefaultOrEmpty)
                {
                    if (origin.IsLookupRoot
                        || !origin.SelectedExportKeys.IsEmpty)
                    {
                        throw Invalid(
                            "An EN1 imported struct TypeDef cannot become a source lookup root.");
                    }

                    var rootImports
                        = new List<C1EnumClosureRootImport>();
                    C1EnumClosureImport? importedType = null;
                    foreach (var expectation in origin.Expectations)
                    {
                        var binding = expectation;
                        if (!IsSelectedClassQueryRootProvider(
                                binding.UsingModule))
                        {
                            continue;
                        }

                        if (!BoundStandardEnumDeclarationContract
                                .SnapshotsMatch(binding.Provider,
                                    origin.Provider)
                            || binding.ImportRow <= 0
                            || (binding.ExpectedDefinitionId >> 24) != 0x13
                            || (binding.ExpectedDefinitionId
                                & 0x00FF_FFFF) == 0
                            || binding.ProviderDefinitionId
                                != origin.DefinitionId
                            || !StringComparer.Ordinal.Equals(
                                binding.ProducerIdentity,
                                origin.Provider.ModuleIdentity)
                            || binding.TargetRequirement
                                != origin.Provider.TargetRequirement
                            || binding.RuntimePolicy
                                != origin.Provider.RuntimePolicy
                            || !BoundEnumTypes.IsValidModuleDeclarationKey(
                                binding.ExportKey)
                            || !origin.ExportKeys.Contains(
                                binding.ExportKey, StringComparer.Ordinal))
                        {
                            throw Invalid(
                                "An EN1 imported struct lost its root-owned TypeDef Import edge.");
                        }

                        rootImports.Add(new C1EnumClosureRootImport(
                            binding.UsingModule.ModuleIdentity,
                            binding.ImportRow));
                        rootTypeDefRowsByStructureIndex.Add((structureIndex,
                            binding.UsingModule.ModuleIdentity,
                            checked((int)(binding.ExpectedDefinitionId
                                & 0x00FF_FFFF))));
                        importedType ??= new C1EnumClosureImport(
                            binding.ProducerIdentity, binding.ExportKey,
                            binding.TargetRequirement,
                            binding.RuntimePolicy);
                    }

                    if (importedType is null)
                    {
                        throw Unsupported(
                            "Mixed EN1 struct re-emission requires at least one root-owned TypeDef Import edge.");
                    }

                    sourceStructImportsByIndex.Add(structureIndex, [
                        importedType with {
                            RootImports = [.. rootImports.Distinct()],
                        },
                    ]);
                    sourceStructExportsByIndex.Add(structureIndex, []);
                }
                else if (directForeignStructure)
                {
                    if (!IsSelectedC1EnumClosureSchemaIdentity(
                            origin.Provider.SchemaIdentity)
                        || !StringComparer.Ordinal.Equals(
                            origin.Provider.SchemaIdentity, schemaIdentity)
                        || GetForeignInvocationSchema(
                            origin.Provider.SchemaIdentity) != schema
                        || origin.Provider.TargetRequirement
                            != moduleTargetRequirement
                        || origin.Provider.RuntimePolicy
                            != (uint)program.RuntimePolicy
                        || !origin.IsLookupRoot
                        || origin.ExportKeys.IsDefaultOrEmpty
                        || origin.SelectedExportKeys.IsDefaultOrEmpty
                        || origin.SelectedExportKeys.Any(key =>
                            !origin.ExportKeys.Contains(key,
                                StringComparer.Ordinal))
                        || (origin.DefinitionId >> 24) != 0x13
                        || (origin.DefinitionId & 0x00FF_FFFF) == 0)
                    {
                        throw Unsupported(
                            "An EN1 foreign inline structure requires an exact selected TypeDef ExportKey.");
                    }

                    var typeImport = new C1EnumClosureImport(
                        origin.Provider.ModuleIdentity,
                        origin.SelectedExportKeys[0],
                        origin.Provider.TargetRequirement,
                        origin.Provider.RuntimePolicy);
                    sourceStructImportsByIndex.Add(structureIndex, [
                        typeImport,
                    ]);
                    sourceStructExportsByIndex.Add(structureIndex, []);
                    rootTypeDefRowsByStructureIndex.Add((structureIndex,
                        origin.Provider.ModuleIdentity,
                        checked((int)(origin.DefinitionId
                            & 0x00FF_FFFF))));
                }
                else
                {
                    if (!SameA2SourceTypeProviderSnapshot(
                            rootSnapshot, origin.Provider)
                        || (origin.DefinitionId & 0x00FF_FFFF) == 0)
                    {
                        throw Invalid(
                            "A local EN1 struct declaration changed its provider identity.");
                    }

                    var typeKeys = origin.ExportKeys.ToList();
                    foreach (var exportKey in typeKeys)
                    {
                        if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                                exportKey)
                            || !usedExportKeys.Add(exportKey))
                        {
                            throw Invalid(
                                "Mixed EN1 struct TypeDef ExportKeys are invalid or duplicated.");
                        }
                    }
                    sourceStructExportsByIndex.Add(structureIndex,
                        typeKeys);
                    rootTypeDefRowsByStructureIndex.Add((structureIndex,
                        origin.Provider.ModuleIdentity,
                        checked((int)(origin.DefinitionId
                            & 0x00FF_FFFF))));
                }

                for (var fieldIndex = 0;
                    fieldIndex < structure.Fields.Length; fieldIndex++)
                {
                    if (structure.Fields[fieldIndex].StandardFieldOrigin
                        is not BoundStandardFieldOrigin fieldOrigin)
                    {
                        continue;
                    }
                    if ((fieldOrigin.DefinitionId >> 24) != 0x14
                        || (fieldOrigin.DefinitionId & 0x00FF_FFFF) == 0
                        || fieldOrigin.IsLookupRoot
                        || fieldOrigin.ExpectedDeclaration is null)
                    {
                        throw Invalid(
                            "A mixed EN1 struct FieldDef origin changed its provider or declaration identity.");
                    }

                    var actualField = structure.Fields[fieldIndex] with {
                            StandardFieldOrigin = null,
                        };
                    if (actualField != fieldOrigin.ExpectedDeclaration)
                    {
                        throw Unsupported(
                            "Mixed EN1 struct re-emission cannot weaken its retained FieldDef snapshot.");
                    }

                    if (directForeignStructure)
                    {
                        if (!SameA2SourceTypeProviderSnapshot(
                                origin.Provider, fieldOrigin.Provider)
                            || fieldOrigin.ExportKeys.IsDefaultOrEmpty
                            || fieldOrigin.SelectedExportKeys
                                .IsDefaultOrEmpty
                            || fieldOrigin.SelectedExportKeys.Any(key =>
                                !fieldOrigin.ExportKeys.Contains(key,
                                    StringComparer.Ordinal))
                            || fieldOrigin.SelectedExportKeys.Any(key =>
                                !BoundEnumTypes.IsValidModuleDeclarationKey(
                                    key)))
                        {
                            throw Unsupported(
                                "An EN1 foreign inline structure FieldDef requires an exact selected provider ExportKey.");
                        }

                        sourceStructFieldImportsByIndex.Add(
                            (structureIndex, fieldIndex), [
                                new C1EnumClosureImport(
                                    fieldOrigin.Provider.ModuleIdentity,
                                    fieldOrigin.SelectedExportKeys[0],
                                    fieldOrigin.Provider.TargetRequirement,
                                    fieldOrigin.Provider.RuntimePolicy),
                            ]);
                    }
                    else if (SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            fieldOrigin.Provider))
                    {
                        if (!origin.Expectations.IsDefaultOrEmpty
                            || fieldOrigin.ExportKeys.IsDefault
                            || fieldOrigin.ExportKeys.IsEmpty)
                        {
                            throw Invalid(
                                "A root-owned FieldDef Export has an invalid local owner or key set.");
                        }

                        foreach (var exportKey in fieldOrigin.ExportKeys)
                        {
                            if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                                    exportKey)
                                || !usedExportKeys.Add(exportKey))
                            {
                                throw Invalid(
                                    "Mixed EN1 struct FieldDef ExportKeys are invalid or duplicated.");
                            }
                        }
                        sourceStructFieldExportsByIndex.Add(
                            (structureIndex, fieldIndex),
                            [.. fieldOrigin.ExportKeys]);
                    }
                    else if (!origin.Expectations.IsDefaultOrEmpty
                        && SameA2SourceTypeProviderSnapshot(
                            origin.Provider, fieldOrigin.Provider)
                        && !fieldOrigin.ExportKeys.IsDefaultOrEmpty
                        && fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty)
                    {
                        continue;
                    }
                    else if (!origin.Expectations.IsDefaultOrEmpty
                        && SameA2SourceTypeProviderSnapshot(
                            origin.Provider, fieldOrigin.Provider)
                        && !fieldOrigin.ExportKeys.IsDefaultOrEmpty
                        && !fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty
                        && fieldOrigin.SelectedExportKeys.All(key =>
                            fieldOrigin.ExportKeys.Contains(key,
                                StringComparer.Ordinal)
                            && BoundEnumTypes.IsValidModuleDeclarationKey(
                                key)))
                    {
                        sourceStructFieldImportsByIndex.Add(
                            (structureIndex, fieldIndex), [
                                .. fieldOrigin.SelectedExportKeys.Select(key =>
                                    new C1EnumClosureImport(
                                        fieldOrigin.Provider.ModuleIdentity,
                                        key,
                                        fieldOrigin.Provider
                                            .TargetRequirement,
                                        fieldOrigin.Provider.RuntimePolicy)),
                            ]);
                    }
                    else
                    {
                        throw Invalid(
                            "An EN1 FieldDef origin is neither a local Export nor a provider Import.");
                    }
                }
            }
        }

        var fieldProviderClosures = ImmutableArray<
            BoundStandardFieldProviderClosure>.Empty;
        if (program.StandardFieldProviderClosure is
            BoundStandardFieldProviderClosure retainedFieldProviderClosure)
        {
            fieldProviderClosures = [retainedFieldProviderClosure];
        }
        if (program.StandardQuerySourceAssociation is
            BoundStandardQuerySourceAssociation fieldAssociation)
        {
            if (fieldAssociation.ProviderClosures.IsDefaultOrEmpty
                || (program.StandardFieldProviderClosure is
                        BoundStandardFieldProviderClosure retainedClosure
                    && !fieldAssociation.ProviderClosures.Any(closure =>
                        ReferenceEquals(closure, retainedClosure))))
            {
                throw Unsupported(
                    "Mixed EN1 struct re-emission requires exact retained FieldDef provider closures.");
            }

            fieldProviderClosures = fieldAssociation.ProviderClosures;
        }

        BoundSourceTypeProviderSnapshot? FindFieldProviderRootSnapshot(
            BoundStandardFieldProviderClosure closure)
        {
            if (closure.Artifacts.IsDefaultOrEmpty)
            {
                return null;
            }

            var rootArtifact = closure.Artifacts[0];
            var selectedProvider
                = program.StandardClassDeclarations
                    .Select(static declaration =>
                        declaration.Declaration.Provider)
                    .FirstOrDefault(provider =>
                        StringComparer.Ordinal.Equals(
                            provider.ModuleIdentity,
                            closure.RootModuleIdentity)
                        && provider.ModuleRow == closure.RootModuleRow
                        && StringComparer.Ordinal.Equals(
                            provider.SchemaIdentity,
                            rootArtifact.SchemaIdentity)
                        && provider.ArtifactSnapshot.AsSpan().SequenceEqual(
                            rootArtifact.ArtifactSnapshot.AsSpan()));
            if (selectedProvider is not null)
            {
                return selectedProvider;
            }

            return rootSnapshot is not null
                && StringComparer.Ordinal.Equals(rootSnapshot.ModuleIdentity,
                    closure.RootModuleIdentity)
                && rootSnapshot.ModuleRow == closure.RootModuleRow
                && StringComparer.Ordinal.Equals(rootSnapshot.SchemaIdentity,
                    rootArtifact.SchemaIdentity)
                && rootSnapshot.ArtifactSnapshot.AsSpan().SequenceEqual(
                    rootArtifact.ArtifactSnapshot.AsSpan())
                    ? rootSnapshot : null;
        }

        foreach (var fieldProviderClosure in fieldProviderClosures)
        {
            var selectedRootSnapshot
                = FindFieldProviderRootSnapshot(fieldProviderClosure)
                    ?? throw Invalid(
                        "An EN1 FieldDef provider closure has no selected root Module.");
            if ((!hasStandardStructViews
                    && classQueryFunction is null)
                || !IsSelectedC1EnumClosureSchemaIdentity(
                    fieldProviderClosure.SchemaIdentity)
                || !StringComparer.Ordinal.Equals(
                    fieldProviderClosure.SchemaIdentity,
                    selectedRootSnapshot.SchemaIdentity)
                || !StringComparer.Ordinal.Equals(
                    fieldProviderClosure.RootModuleIdentity,
                    selectedRootSnapshot.ModuleIdentity)
                || (classQueryFunction is null
                    && !StringComparer.Ordinal.Equals(
                        fieldProviderClosure.RootModuleIdentity,
                        moduleIdentity))
                || fieldProviderClosure.RootModuleRow
                    != selectedRootSnapshot.ModuleRow
                || fieldProviderClosure.TargetRequirement
                    != moduleTargetRequirement
                || fieldProviderClosure.RuntimePolicy
                    != (uint)program.RuntimePolicy
                || fieldProviderClosure.Artifacts.IsDefaultOrEmpty
                || fieldProviderClosure.Modules.IsDefaultOrEmpty
                || fieldProviderClosure.FieldEdges.IsDefault
                || fieldProviderClosure.Artifacts[0].ArtifactIndex != 0
                || !StringComparer.Ordinal.Equals(
                    fieldProviderClosure.Artifacts[0].SchemaIdentity,
                    selectedRootSnapshot.SchemaIdentity)
                || !fieldProviderClosure.Artifacts[0].ArtifactSnapshot
                    .AsSpan().SequenceEqual(
                        selectedRootSnapshot.ArtifactSnapshot.AsSpan()))
            {
                throw Unsupported(
                    "Mixed EN1 struct re-emission cannot change its FieldDef provider root or artifact snapshot.");
            }

            var rootTables = ValidateForeignInvocationArtifact(
                CreateForeignInvocationIndex(
                    fieldProviderClosure.Artifacts[0].ArtifactSnapshot
                        .ToArray(),
                    fieldProviderClosure.SchemaIdentity),
                fieldProviderClosure.SchemaIdentity, null,
                createA3ResourceLedger: false);
            bool IsRetainedClassFieldProviderEdge(
                BoundStandardFieldProviderEdge edge)
            {
                if (classQueryFunction is null
                    || (edge.ExpectedDefinitionId >> 24) != 0x14
                    || (edge.ActualDefinitionId >> 24) != 0x14)
                {
                    return false;
                }

                var expectedOwnerDefinitionId = 0x1300_0000u
                    | checked((uint)edge.ExpectedOwnerTypeDefRow);
                var providerOwnerDefinitionId = 0x1300_0000u
                    | checked((uint)edge.ProviderTypeDefRow);
                var expectation = program.StandardClassDeclarations
                    .SelectMany(static declaration =>
                        declaration.Expectations)
                    .Where(expectation =>
                        BoundStandardEnumDeclarationContract.SnapshotsMatch(
                            expectation.Binding.UsingModule,
                            selectedRootSnapshot))
                    .SelectMany(static declaration =>
                        declaration.FieldExpectations)
                    .SingleOrDefault(field =>
                        field.ImportRow == edge.ImportRow
                        && field.OwnerImportRow == edge.OwnerImportRow);
                var providerArtifact
                    = fieldProviderClosure.Artifacts.FirstOrDefault(
                        artifact => artifact.ArtifactIndex
                            == edge.ProviderArtifactIndex);
                var providerModule = fieldProviderClosure.Modules
                    .FirstOrDefault(module =>
                        module.ArtifactIndex == edge.ProviderArtifactIndex
                        && module.ModuleRow == edge.ProviderModuleRow);
                return expectation is not null
                    && providerArtifact is not null
                    && providerModule is not null
                    && expectation.ExpectedDefinitionId
                        == edge.ExpectedDefinitionId
                    && expectation.ExpectedOwnerDefinitionId
                        == expectedOwnerDefinitionId
                    && expectation.ProviderDefinitionId
                        == edge.ActualDefinitionId
                    && expectation.ProviderOwnerDefinitionId
                        == providerOwnerDefinitionId
                    && StringComparer.Ordinal.Equals(
                        expectation.ExportKey, edge.ExportKey)
                    && StringComparer.Ordinal.Equals(
                        expectation.Provider.ModuleIdentity,
                        edge.ProducerIdentity)
                    && expectation.Provider.ModuleRow
                        == edge.ProviderModuleRow
                    && expectation.Provider.TargetRequirement
                        == edge.TargetRequirement
                    && expectation.Provider.RuntimePolicy
                        == edge.RuntimePolicy
                    && StringComparer.Ordinal.Equals(
                        providerArtifact.SchemaIdentity,
                        expectation.Provider.SchemaIdentity)
                    && providerArtifact.ArtifactSnapshot.AsSpan()
                        .SequenceEqual(
                            expectation.Provider.ArtifactSnapshot.AsSpan())
                    && StringComparer.Ordinal.Equals(
                        providerModule.Identity, edge.ProducerIdentity)
                    && providerModule.TargetRequirement
                        == edge.TargetRequirement
                    && providerModule.RuntimePolicy
                        == edge.RuntimePolicy;
            }

            var structureIndexByExpectedTypeDef
                = new Dictionary<(string ModuleIdentity,
                    int TypeDefRow), int>(program.Structures.Length);
            for (var structureIndex = 0;
                structureIndex < program.Structures.Length;
                structureIndex++)
            {
                var origin = program.Structures[structureIndex]
                    .StandardTypeOrigin
                    ?? throw Invalid(
                        "An EN1 FieldDef provider closure has an unoriginated struct.");
                var rootDefinitionIds = origin.Expectations
                    .Where(expectation =>
                        BoundStandardEnumDeclarationContract.SnapshotsMatch(
                            expectation.UsingModule,
                            selectedRootSnapshot))
                    .Select(static expectation =>
                        expectation.ExpectedDefinitionId)
                    .Distinct()
                    .ToArray();
                if (rootDefinitionIds.Length == 0)
                {
                    if (!SameA2SourceTypeProviderSnapshot(origin.Provider,
                            selectedRootSnapshot))
                    {
                        continue;
                    }

                    rootDefinitionIds = [origin.DefinitionId];
                }

                foreach (var rootDefinitionId in rootDefinitionIds)
                {
                    var rootTypeDefKey = (
                        selectedRootSnapshot.ModuleIdentity,
                        checked((int)(rootDefinitionId
                            & 0x00FF_FFFF)));
                    if ((rootDefinitionId >> 24) != 0x13
                        || (rootDefinitionId & 0x00FF_FFFF) == 0
                        || (!structureIndexByExpectedTypeDef.TryAdd(
                                rootTypeDefKey, structureIndex)
                            && structureIndexByExpectedTypeDef[
                                rootTypeDefKey] != structureIndex))
                    {
                        throw Invalid(
                            "An EN1 struct origin has an invalid or duplicate root TypeDef identity.");
                    }
                }
            }

            foreach (var edge in fieldProviderClosure.FieldEdges)
            {
                if (edge.ConsumerArtifactIndex != 0
                    || edge.ConsumerModuleRow
                        != fieldProviderClosure.RootModuleRow)
                {
                    continue;
                }

                if ((edge.ExpectedDefinitionId >> 24) != 0x14
                    || (edge.ActualDefinitionId >> 24) != 0x14
                    || edge.ImportRow <= 0
                    || edge.ModuleRefRow <= 0
                    || edge.OwnerImportRow <= 0)
                {
                    throw Invalid(
                        "An EN1 FieldDef provider edge has an invalid root or definition identity.");
                }

                if (!structureIndexByExpectedTypeDef.TryGetValue((
                        selectedRootSnapshot.ModuleIdentity,
                        edge.ExpectedOwnerTypeDefRow),
                        out var structureIndex))
                {
                    if (IsRetainedClassFieldProviderEdge(edge))
                    {
                        continue;
                    }

                    throw Invalid(
                        "An EN1 FieldDef provider edge has no retained struct or class expectation.");
                }

                var expectedFieldRow = checked((int)(
                    edge.ExpectedDefinitionId & 0x00FF_FFFF));
                var (fieldStart, fieldEnd) = rootTables.TypeDefs
                    .GetFieldRange(edge.ExpectedOwnerTypeDefRow);
                if (expectedFieldRow < fieldStart
                    || expectedFieldRow >= fieldEnd
                    || rootTables.Fields.GetOwnerTypeDef(expectedFieldRow)
                        != edge.ExpectedOwnerTypeDefRow
                    || rootTables.Imports
                        .GetExpectedDefinitionForImportRow(edge.OwnerImportRow)
                        != (0x1300_0000u
                            | checked((uint)edge.ExpectedOwnerTypeDefRow))
                    || rootTables.Imports
                        .GetExpectedDefinitionForImportRow(edge.ImportRow)
                        != edge.ExpectedDefinitionId)
                {
                    throw Invalid(
                        "An EN1 FieldDef provider edge lost its expected owner or stub.");
                }

                var structure = program.Structures[structureIndex];
                var fieldOrdinal = expectedFieldRow - fieldStart;
                if ((uint)fieldOrdinal >= (uint)structure.Fields.Length
                    || structure.StandardTypeOrigin
                        is not BoundStandardTypeOrigin typeOrigin
                    || (typeOrigin.DefinitionId >> 24) != 0x13
                    || typeOrigin.DefinitionId
                        != (0x1300_0000u
                            | checked((uint)edge.ProviderTypeDefRow))
                    || !StringComparer.Ordinal.Equals(
                        typeOrigin.Provider.ModuleIdentity,
                        edge.ProducerIdentity)
                    || typeOrigin.Provider.TargetRequirement
                        != edge.TargetRequirement
                    || typeOrigin.Provider.RuntimePolicy
                        != edge.RuntimePolicy
                    || !typeOrigin.Expectations.Any(expectation =>
                        expectation.ImportRow == edge.OwnerImportRow
                        && expectation.ExpectedDefinitionId
                            == (0x1300_0000u
                                | checked((uint)edge.ExpectedOwnerTypeDefRow))
                        && expectation.ProviderDefinitionId
                            == (0x1300_0000u
                                | checked((uint)edge.ProviderTypeDefRow))
                        && BoundStandardEnumDeclarationContract
                            .SnapshotsMatch(expectation.UsingModule,
                                selectedRootSnapshot)))
                {
                    throw Invalid(
                        "An EN1 FieldDef provider edge does not match its imported owner.");
                }

                var field = structure.Fields[fieldOrdinal];
                var providerArtifact
                    = fieldProviderClosure.Artifacts.FirstOrDefault(
                        artifact => artifact.ArtifactIndex
                            == edge.ProviderArtifactIndex);
                var providerModule
                    = fieldProviderClosure.Modules.FirstOrDefault(
                        module => module.ArtifactIndex
                                == edge.ProviderArtifactIndex
                            && module.ModuleRow == edge.ProviderModuleRow);
                var fieldBinding = rootTables.Imports
                    .GetBindingForImportRow(edge.ImportRow);
                if (field.StandardFieldOrigin
                    is not BoundStandardFieldOrigin fieldOrigin
                    || providerArtifact is null
                    || providerModule is null
                    || !StringComparer.Ordinal.Equals(
                        providerArtifact.SchemaIdentity,
                        fieldOrigin.Provider.SchemaIdentity)
                    || !providerArtifact.ArtifactSnapshot.AsSpan()
                        .SequenceEqual(
                            fieldOrigin.Provider.ArtifactSnapshot.AsSpan())
                    || providerModule.Identity
                        != fieldOrigin.Provider.ModuleIdentity
                    || providerModule.ModuleRow
                        != fieldOrigin.Provider.ModuleRow
                    || providerModule.TargetRequirement
                        != fieldOrigin.Provider.TargetRequirement
                    || providerModule.RuntimePolicy
                        != fieldOrigin.Provider.RuntimePolicy
                    || fieldOrigin.DefinitionId != edge.ActualDefinitionId
                    || fieldOrigin.Provider.ModuleRow
                        != edge.ProviderModuleRow
                    || fieldOrigin.Provider.TargetRequirement
                        != edge.TargetRequirement
                    || fieldOrigin.Provider.RuntimePolicy
                        != edge.RuntimePolicy
                    || !StringComparer.Ordinal.Equals(
                        fieldOrigin.Provider.ModuleIdentity,
                        edge.ProducerIdentity)
                    || fieldBinding.ExportKey != edge.ExportKey
                    || fieldBinding.ProducerIdentity
                        != edge.ProducerIdentity
                    || fieldBinding.TargetRequirement
                        != edge.TargetRequirement
                    || fieldBinding.RuntimePolicy != edge.RuntimePolicy
                    || rootTables.Imports.GetModuleRefForImportRow(
                        edge.ImportRow) != edge.ModuleRefRow
                    || !fieldOrigin.ExportKeys.Contains(edge.ExportKey,
                        StringComparer.Ordinal))
                {
                    throw Invalid(
                        "An EN1 FieldDef provider edge lost its checked field origin.");
                }

                var fieldImports = sourceStructFieldImportsByIndex.GetValueOrDefault(
                    (structureIndex, fieldOrdinal));
                if (fieldImports is null)
                {
                    fieldImports = [];
                    sourceStructFieldImportsByIndex.Add(
                        (structureIndex, fieldOrdinal), fieldImports);
                }
                fieldImports.Add(new C1EnumClosureImport(
                    edge.ProducerIdentity, edge.ExportKey,
                    edge.TargetRequirement, edge.RuntimePolicy));
            }

        }
        if (!fieldProviderClosures.IsEmpty)
        {
            for (var structureIndex = 0;
                structureIndex < program.Structures.Length;
                structureIndex++)
            {
                var structure = program.Structures[structureIndex];
                for (var fieldIndex = 0;
                    fieldIndex < structure.Fields.Length; fieldIndex++)
                {
                    if (structure.Fields[fieldIndex].StandardFieldOrigin
                            is not BoundStandardFieldOrigin fieldOrigin
                        || SameA2SourceTypeProviderSnapshot(
                            fieldOrigin.Provider, rootSnapshot!))
                    {
                        continue;
                    }

                    if (!sourceStructFieldImportsByIndex.TryGetValue(
                            (structureIndex, fieldIndex),
                            out var fieldImports)
                        || fieldOrigin.ExportKeys.IsDefault
                        || fieldImports.Any(fieldImport =>
                            !fieldOrigin.ExportKeys.Contains(
                                fieldImport.ExportKey,
                                StringComparer.Ordinal))
                        || (fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty
                                ? fieldOrigin.ExportKeys
                                : fieldOrigin.SelectedExportKeys)
                            .Any(exportKey =>
                            !fieldImports.Any(fieldImport =>
                                StringComparer.Ordinal.Equals(
                                    fieldImport.ExportKey, exportKey))))
                    {
                        throw Invalid(
                            "An EN1 imported FieldDef origin has no complete retained provider edge set.");
                    }
                }
            }
        }
        else if (hasStandardStructViews
            && program.Structures.Any(structure =>
                structure.Fields.Any(field =>
                    field.StandardFieldOrigin is
                        BoundStandardFieldOrigin fieldOrigin
                    && rootSnapshot is not null
                    && !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                        fieldOrigin.Provider))))
        {
            throw Invalid(
                "A mixed EN1 imported FieldDef lost its provider closure.");
        }

        var retainedStructureIndexes = new SortedSet<int>();
        var pendingClassIndexes = new Queue<int>(
            sourceClassExportsByIndex.Keys);
        var pendingStructureIndexes = new Queue<int>(
            sourceStructExportsByIndex.Keys);
        void RetainLocalClassPointerPointee(PrimitiveType pointeeType)
        {
            if (!TryGetC1M1SourceClassFieldType(pointeeType,
                    out var dependencyClassIndex, out _, out _))
            {
                return;
            }

            if ((uint)dependencyClassIndex >= (uint)program.Classes.Length
                || !localClassIndexes.Contains(dependencyClassIndex))
            {
                throw Unsupported(
                    "EN1 native pointers require retained local class pointees.");
            }

            if (!sourceClassExportsByIndex.ContainsKey(
                    dependencyClassIndex))
            {
                sourceClassExportsByIndex.Add(dependencyClassIndex, []);
            }

            pendingClassIndexes.Enqueue(dependencyClassIndex);
        }

        while (pendingClassIndexes.Count != 0
            || pendingStructureIndexes.Count != 0)
        {
            while (pendingClassIndexes.TryDequeue(out var classIndex))
            {
                if (!retainedClassIndexes.Add(classIndex))
                {
                    continue;
                }

                if ((uint)classIndex >= (uint)program.Classes.Length
                    || !localClassIndexes.Contains(classIndex)
                    || retainedClassIndexes.Count
                        > ManagedClassTypes.OptionalFirst
                            - ManagedClassTypes.First)
                {
                    throw Unsupported(
                        "EN1 class closure supports only checked local classes within the registered limit.");
                }

                var @class = program.Classes[classIndex];
                if (@class.Fields.IsDefaultOrEmpty
                    || @class.IsUnique is not false
                    || @class.HasCleanup is not false
                    || @class.CleanupMethod is not null)
                {
                    throw Unsupported(
                        "The bounded EN1 class TypeDef route requires nonempty fields, known aliasable ownership and no Cleanup.");
                }

                foreach (var field in @class.Fields)
                {
                    if (field is null || (field.IsPrivate && field.IsInternal)
                        || field.RequiresUnsafeCaller
                        || field.BackingPropertyName is not null)
                    {
                        throw Unsupported(
                            "EN1 class FieldDefs cannot discard conflicting access, unsafe-caller or backing-property facts.");
                    }

                    if (TryGetC1M1SourceClassFieldType(field.Type,
                            out var dependencyClassIndex, out _, out _))
                    {
                        if ((uint)dependencyClassIndex
                                >= (uint)program.Classes.Length
                            || !localClassIndexes.Contains(
                                dependencyClassIndex))
                        {
                            throw Unsupported(
                                "The bounded EN1 class closure does not admit imported or private class TypeDef dependencies.");
                        }
                        if (!sourceClassExportsByIndex.ContainsKey(
                                dependencyClassIndex))
                        {
                            sourceClassExportsByIndex.Add(
                                dependencyClassIndex, []);
                        }
                        pendingClassIndexes.Enqueue(dependencyClassIndex);
                        continue;
                    }

                    if (TryGetC1M1SourceStructFieldType(field.Type,
                            sourceStructDeclarationsByIndex,
                            out var dependencyStructureIndex))
                    {
                        pendingStructureIndexes.Enqueue(
                            dependencyStructureIndex);
                        continue;
                    }

                    if (BoundEnumTypes.IsType(field.Type))
                    {
                        if ((uint)BoundEnumTypes.Index(field.Type)
                            >= (uint)program.EnumDefinitions.Length)
                        {
                            throw Invalid(
                                "An EN1 class field refers to an invalid enum identity.");
                        }
                        continue;
                    }

                    if (NativePointerTypes.IsType(field.Type))
                    {
                        var pointerIndex = NativePointerTypes.Index(
                            field.Type);
                        if ((uint)pointerIndex
                                >= (uint)program.NativePointerTypes.Length
                            || !IsSupportedEN1FieldPointer(
                                program.NativePointerTypes[pointerIndex],
                                program))
                        {
                            throw Unsupported(
                                "EN1 class fields require a covered native data pointer with a scalar, enum, non-generic struct or class-handle pointee.");
                        }

                        if (moduleTargetRequirement
                            != TrRmModuleTableReader
                                .WindowsX64NativeTargetRequirement)
                        {
                            throw Unsupported(
                                "EN1 native data pointer fields require the selected Windows x64 target.");
                        }

                        RetainLocalClassPointerPointee(
                            program.NativePointerTypes[pointerIndex]
                                .PointeeType);
                        if (TryGetEN1SourceStructFieldType(
                                program.NativePointerTypes[pointerIndex]
                                    .PointeeType,
                                out var pointerStructureIndex))
                        {
                            if ((uint)pointerStructureIndex
                                    >= (uint)program.Structures.Length
                                || !IsSupportedSourceStruct(
                                    program.Structures[
                                        pointerStructureIndex]))
                            {
                                throw Unsupported(
                                    "EN1 native pointers require retained non-generic struct pointees.");
                            }

                            pendingStructureIndexes.Enqueue(
                                pointerStructureIndex);
                        }

                        continue;
                    }

                    if (NativeFunctionPointerTypes.IsType(field.Type))
                    {
                        var signatureIndex
                            = NativeFunctionPointerTypes.Index(field.Type);
                        if ((uint)signatureIndex
                                >= (uint)program.NativeFunctionPointerSignatures.Length
                            || !IsSupportedEN1FieldFunctionPointer(
                                program.NativeFunctionPointerSignatures[
                                    signatureIndex]))
                        {
                            throw Unsupported(
                                "EN1 class fields require a covered scalar-signature native function pointer.");
                        }

                        if (moduleTargetRequirement
                            != TrRmModuleTableReader
                                .WindowsX64NativeTargetRequirement)
                        {
                            throw Unsupported(
                                "EN1 native function-pointer fields require the selected Windows x64 target.");
                        }

                        continue;
                    }

                    if (field.Type == PrimitiveType.NativeRawPointer)
                    {
                        if (moduleTargetRequirement
                            != TrRmModuleTableReader
                                .WindowsX64NativeTargetRequirement)
                        {
                            throw Unsupported(
                                "EN1 native pointer fields require the selected Windows x64 target.");
                        }

                        continue;
                    }

                    if ((field.Type is PrimitiveType.NativeInt
                            or PrimitiveType.NativeUInt)
                        && moduleTargetRequirement
                            != TrRmModuleTableReader
                                .WindowsX64NativeTargetRequirement)
                    {
                        throw Unsupported(
                            "EN1 native-width class fields require the selected Windows x64 target.");
                    }

                    _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
                }
            }

            while (pendingStructureIndexes.TryDequeue(
                out var structureIndex))
            {
                if (!retainedStructureIndexes.Add(structureIndex))
                {
                    continue;
                }

                var structure = program.Structures[structureIndex];
                if (!IsSupportedSourceStruct(structure))
                {
                    throw Unsupported(
                        "EN1 source struct closure supports only non-generic local structs with auto layout and no ownership or cleanup contract.");
                }

                foreach (var field in structure.Fields)
                {
                    if (TryGetC1M1SourceClassFieldType(field.Type,
                            out var dependencyClassIndex, out _, out _))
                    {
                        if ((uint)dependencyClassIndex
                                >= (uint)program.Classes.Length
                            || !localClassIndexes.Contains(
                                dependencyClassIndex))
                        {
                            throw Unsupported(
                                "EN1 struct fields require retained local class declarations.");
                        }
                        if (!sourceClassExportsByIndex.ContainsKey(
                                dependencyClassIndex))
                        {
                            sourceClassExportsByIndex.Add(
                                dependencyClassIndex, []);
                        }
                        pendingClassIndexes.Enqueue(dependencyClassIndex);
                        continue;
                    }

                    if (TryGetEN1SourceStructFieldType(field.Type,
                            out var dependencyStructureIndex))
                    {
                        if ((uint)dependencyStructureIndex
                                >= (uint)program.Structures.Length
                            || !IsSupportedSourceStruct(
                                program.Structures[dependencyStructureIndex]))
                        {
                            throw Unsupported(
                                "EN1 source struct fields require retained local non-generic struct declarations.");
                        }

                        pendingStructureIndexes.Enqueue(
                            dependencyStructureIndex);
                        continue;
                    }

                    if (NativePointerTypes.IsType(field.Type))
                    {
                        var pointerIndex = NativePointerTypes.Index(
                            field.Type);
                        if ((uint)pointerIndex
                                >= (uint)program.NativePointerTypes.Length
                            || !IsSupportedEN1FieldPointer(
                                program.NativePointerTypes[pointerIndex],
                                program))
                        {
                            throw Unsupported(
                                "EN1 struct fields require a covered native data pointer with a scalar, enum, non-generic struct or class-handle pointee.");
                        }

                        if (moduleTargetRequirement
                            != TrRmModuleTableReader
                                .WindowsX64NativeTargetRequirement)
                        {
                            throw Unsupported(
                                "EN1 native data pointer fields require the selected Windows x64 target.");
                        }

                        RetainLocalClassPointerPointee(
                            program.NativePointerTypes[pointerIndex]
                                .PointeeType);
                        if (TryGetEN1SourceStructFieldType(
                                program.NativePointerTypes[pointerIndex]
                                    .PointeeType,
                                out var pointerStructureIndex))
                        {
                            if ((uint)pointerStructureIndex
                                    >= (uint)program.Structures.Length
                                || !IsSupportedSourceStruct(
                                    program.Structures[
                                        pointerStructureIndex]))
                            {
                                throw Unsupported(
                                    "EN1 native pointers require retained non-generic struct pointees.");
                            }

                            pendingStructureIndexes.Enqueue(
                                pointerStructureIndex);
                        }

                        continue;
                    }

                    if (NativeFunctionPointerTypes.IsType(field.Type))
                    {
                        var signatureIndex
                            = NativeFunctionPointerTypes.Index(field.Type);
                        if ((uint)signatureIndex
                                >= (uint)program.NativeFunctionPointerSignatures.Length
                            || !IsSupportedEN1FieldFunctionPointer(
                                program.NativeFunctionPointerSignatures[
                                    signatureIndex]))
                        {
                            throw Unsupported(
                                "EN1 struct fields require a covered scalar-signature native function pointer.");
                        }

                        if (moduleTargetRequirement
                            != TrRmModuleTableReader
                                .WindowsX64NativeTargetRequirement)
                        {
                            throw Unsupported(
                                "EN1 native function-pointer fields require the selected Windows x64 target.");
                        }

                        continue;
                    }

                    if ((field.Type is PrimitiveType.NativeInt
                            or PrimitiveType.NativeUInt)
                        && moduleTargetRequirement
                            != TrRmModuleTableReader
                                .WindowsX64NativeTargetRequirement)
                    {
                        throw Unsupported(
                            "EN1 native-width struct fields require the selected Windows x64 target.");
                    }

                    if (field.Type == PrimitiveType.NativeRawPointer)
                    {
                        if (moduleTargetRequirement
                            != TrRmModuleTableReader
                                .WindowsX64NativeTargetRequirement)
                        {
                            throw Unsupported(
                                "EN1 native pointer fields require the selected Windows x64 target.");
                        }

                        continue;
                    }

                    if (BoundEnumTypes.IsType(field.Type))
                    {
                        if ((uint)BoundEnumTypes.Index(field.Type)
                            >= (uint)program.EnumDefinitions.Length)
                        {
                            throw new InvalidDataException(
                                "An EN1 source struct field refers to an invalid enum identity.");
                        }
                        continue;
                    }

                    _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
                }
            }
        }

        var structureIndexByRootTypeDefRow
            = new Dictionary<(string ModuleIdentity, int TypeDefRow), int>(
                rootTypeDefRowsByStructureIndex.Count);
        foreach (var (structureIndex, rootModuleIdentity, typeDefRow)
            in rootTypeDefRowsByStructureIndex)
        {
            var rootTypeDefKey = (rootModuleIdentity, typeDefRow);
            if (typeDefRow <= 0
                || (!structureIndexByRootTypeDefRow.TryAdd(rootTypeDefKey,
                        structureIndex)
                    && structureIndexByRootTypeDefRow[rootTypeDefKey]
                        != structureIndex))
            {
                throw Invalid(
                    "EN1 class re-emission found an invalid or duplicate root struct TypeDef identity.");
            }
        }
        var stringRows = new List<(string Text, int Encoding)> {
            ("", 0), (moduleIdentity, GetStringEncoding(moduleIdentity)),
        };
        var stringRowsByText = new Dictionary<string, int>(
            StringComparer.Ordinal) {
            [""] = 1,
            [moduleIdentity] = 2,
        };
        var standardClassDeclarationsByTypeDef
            = standardClassEmissions.ToArray();
        var standardClassProviderByIdentity
            = standardClassDeclarationsByTypeDef.ToDictionary(
                static emission => emission.View.Identity,
                static emission => emission.View.Provider);
        var standardClassRootTables
            = standardClassDeclarationsByTypeDef.Length == 0
                ? null
                : ValidateForeignInvocationArtifact(
                    CreateForeignInvocationIndex(
                        rootSnapshot!.ArtifactSnapshot.ToArray(),
                        rootSnapshot.SchemaIdentity),
                    rootSnapshot.SchemaIdentity, null,
                    createA3ResourceLedger: false);
        var standardClassRootTablesByProvider
            = new Dictionary<string,
                TrRmStructuralMetadataValidator.ValidatedExecutionTables>(
                StringComparer.Ordinal);
        foreach (var emission in standardClassDeclarationsByTypeDef)
        {
            var provider = emission.View.Provider;
            if (standardClassRootTablesByProvider.ContainsKey(
                    provider.ModuleIdentity))
            {
                continue;
            }

            var providerTables = ValidateForeignInvocationArtifact(
                CreateForeignInvocationIndex(
                    provider.ArtifactSnapshot.ToArray(),
                    provider.SchemaIdentity),
                provider.SchemaIdentity, null,
                createA3ResourceLedger: false);
            standardClassRootTablesByProvider.Add(
                provider.ModuleIdentity, providerTables);
        }
        var typeDefRows = new List<TrRmTypeDefRow>(
            emittedEnums.Count + retainedClassIndexes.Count
                + standardClassDeclarationsByTypeDef.Length
                + retainedStructureIndexes.Count);
        var typeDefModuleIdentities = new List<string>(
            emittedEnums.Count + retainedClassIndexes.Count
                + standardClassDeclarationsByTypeDef.Length
                + retainedStructureIndexes.Count);
        var methodSignatureRows = new List<TrRmMethodSigRow>();
        var methodRows = new List<TrRmMethodDefRow>();
        var nativeFunctionPointerAnchorMethodRows = new int[
            program.NativeFunctionPointerSignatures.Length];
        var usedFunctionPointerSignatureIndexes = new SortedSet<int>();
        void AddFunctionPointerSignature(PrimitiveType type)
        {
            if (!NativeFunctionPointerTypes.IsType(type))
            {
                return;
            }

            var signatureIndex = NativeFunctionPointerTypes.Index(type);
            if ((uint)signatureIndex
                    >= (uint)nativeFunctionPointerAnchorMethodRows.Length)
            {
                throw new InvalidDataException(
                    "An EN1 field references an invalid native function-pointer signature.");
            }

            _ = usedFunctionPointerSignatureIndexes.Add(signatureIndex);
        }
        foreach (var classIndex in retainedClassIndexes)
        {
            foreach (var field in program.Classes[classIndex].Fields)
            {
                AddFunctionPointerSignature(field.Type);
            }
        }
        foreach (var structureIndex in retainedStructureIndexes)
        {
            foreach (var field in program.Structures[structureIndex].Fields)
            {
                AddFunctionPointerSignature(field.Type);
            }
        }
        foreach (var emission in standardClassEmissions)
        {
            foreach (var field in emission.View.Fields)
            {
                AddFunctionPointerSignature(field.Type);
            }
            var retainedMethods = emission.Import is null
                ? emission.View.Methods
                : emission.Import.RootImportRow > 0
                    ? emission.View.Methods.Where(method =>
                            emission.MethodExpectations.Any(
                                expectation =>
                                    expectation.ExpectedDefinitionId
                                        == method.DefinitionId))
                        .ToImmutableArray()
                    : ImmutableArray<BoundStandardClassMethodDeclaration>.Empty;
            foreach (var method in retainedMethods)
            {
                foreach (var parameter in method.Parameters)
                {
                    AddFunctionPointerSignatureFromTypeSpec(
                        parameter.TypeSpecReference);
                }
                AddFunctionPointerSignatureFromTypeSpec(
                    method.ReturnTypeSpecReference);
                AddFunctionPointerSignatureFromTypeSpec(
                    method.ReceiverTypeSpecReference);
            }
        }

        foreach (var function in program.Functions.Where(
            static function => function.IsClassConstructor))
        {
            foreach (var variable in function.Variables)
            {
                AddFunctionPointerSignature(variable.Type);
            }
        }

        var classMethodCountBeforeFunctionPointerAnchors
            = standardClassDeclarationsByTypeDef.Sum(emission =>
                emission.Import is null
                    ? emission.View.Methods.Length
                    : emission.Import.RootImportRow > 0
                        ? emission.MethodExpectations.Select(
                                static expectation =>
                                    expectation.ExpectedDefinitionId)
                            .Distinct().Count()
                        : 0);
        var nextFunctionPointerAnchorRow
            = checked(classMethodCountBeforeFunctionPointerAnchors + 1);
        foreach (var signatureIndex in usedFunctionPointerSignatureIndexes)
        {
            nativeFunctionPointerAnchorMethodRows[signatureIndex]
                = nextFunctionPointerAnchorRow++;
        }

        void AddFunctionPointerSignatureFromTypeSpec(
            uint typeSpecReference)
        {
            if (typeSpecReference == 0 || standardClassRootTables is null)
            {
                return;
            }

            var typeSpec = standardClassRootTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            if (!typeSpec.IsRow || typeSpec.Form != 0x0D)
            {
                return;
            }

            var signature = DecodeEN1FieldFunctionPointer(
                standardClassRootTables, typeSpec.Row,
                rootSnapshot!.ModuleRow);
            for (var signatureIndex = 0;
                signatureIndex < program.NativeFunctionPointerSignatures.Length;
                signatureIndex++)
            {
                if (!NativeFunctionPointerContract.AreEquivalent(
                        program.NativeFunctionPointerSignatures[
                            signatureIndex], signature))
                {
                    continue;
                }

                _ = usedFunctionPointerSignatureIndexes.Add(
                    signatureIndex);
                return;
            }

            throw Invalid(
                "An EN1 class MethodDef TypeSpec has no retained function-pointer signature.");
        }

        var parameterRows = new List<TrRmParamRow>();
        var bodyRows = new List<ReadOnlyMemory<byte>>();
        var methodParameterLabelRows
            = new List<(int MethodDefRow, int Ordinal, string Name)>();
        var contractRows = new List<TrRmContractRow>();
        var typeDefNames = new List<(string Namespace, string Name)>(
            emittedEnums.Count + retainedClassIndexes.Count
                + standardClassDeclarationsByTypeDef.Length
                + retainedStructureIndexes.Count);
        var layoutRows = new List<TrRmLayoutRow>(
            checked(emittedEnums.Count * 2
                + retainedClassIndexes.Count
                + standardClassDeclarationsByTypeDef.Length
                + retainedStructureIndexes.Count));
        var fieldRows = new List<TrRmFieldDefRow>();
        var fieldNames = new List<string>();
        var fieldDefRowByClassAndIndex
            = new Dictionary<(int ClassIndex, int FieldIndex), int>();
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        var sourceClassConstructorPlans
            = new List<C1EnumClosureSourceConstructorPlan>();
        var enumRows = new List<TrRmEnumDefRow>(
            emittedEnums.Count);
        var enumMemberRows = new List<TrRmTypedEnumMemberDefRow>();
        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        var fieldPathRows = new List<TrRmFieldPathRow>();
        var targetLayoutDependencyRows
            = new List<TrRmTargetLayoutDependencyRow>();
        var targetLayoutDependencyFieldReferences
            = new List<uint>();
        var typeSpecByEmissionEnumIndex = new Dictionary<int, uint>();
        var typeSpecByBoundEnumIndex = new Dictionary<int, uint>();
        var typeDefRowByEnumIndex = new Dictionary<int, int>();
        var importRowByEnumIndex = new Dictionary<int, int>();
        var enumIndexByRootEnumImport
            = new Dictionary<(string ModuleIdentity, int ImportRow), int>();
        var importRowByStructureIndex = new Dictionary<int, int>();
        var importRowByRootStructureImport
            = new Dictionary<(string ModuleIdentity, int ImportRow), int>();
        var importRowByRootClassImport
            = new Dictionary<(string ModuleIdentity, int ImportRow), int>();
        var importRowByRootClassIdentity
            = new Dictionary<BoundGenericNominalTypeIdentity, int>();
        var rootProviderClassTypeDefs
            = standardClassDeclarationsByTypeDef
                .Select((emission, ordinal) => (emission,
                    typeDefRow: emittedEnums.Count
                        + retainedClassIndexes.Count + ordinal + 1))
                .Where(item => rootSnapshot is not null
                    && SameA2SourceTypeProviderSnapshot(rootSnapshot,
                        item.emission.View.Provider))
                .ToArray();
        var ambiguousRootClassTypeDefRows
            = rootProviderClassTypeDefs
                .GroupBy(static item => item.emission.RootTypeDefRow)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key).ToHashSet();
        var standardClassFieldRowsByIdentity = new Dictionary<(
            BoundGenericNominalTypeIdentity Owner, uint FieldDefinitionId),
            int>();
        var standardClassFieldRowMap = new Dictionary<int, int>();
        var standardClassMethodRowsByIdentity = new Dictionary<(
            BoundGenericNominalTypeIdentity Owner, uint MethodDefinitionId),
            int>();
        var standardClassMethodDefRowMap = new Dictionary<int, int>();
        var retainedClassMethodBodyPlans
            = new List<C1EnumClosureRetainedClassMethodBodyPlan>();
        var hasImportedClassConstructionMethods = false;
        var hasLocalClassConstructionBodies = false;
        var standardFieldImportRows = new Dictionary<(
            uint ExpectedDefinitionId, string ProducerIdentity,
            string ExportKey), int>();
        var typeDefRowByClass = retainedClassIndexes
            .Select((classIndex, ordinal) => (classIndex,
                typeDefRow: emittedEnums.Count + ordinal + 1))
            .ToDictionary(static item => item.classIndex,
                static item => item.typeDefRow);
        var typeDefRowByRootClassTypeDef
            = rootProviderClassTypeDefs
                .Where(item => !ambiguousRootClassTypeDefRows.Contains(
                    item.emission.RootTypeDefRow))
                .ToDictionary(static item =>
                        item.emission.RootTypeDefRow,
                    static item => item.typeDefRow);
        var typeDefRowByProviderClassTypeDef
            = standardClassDeclarationsByTypeDef
                .Select((emission, ordinal) => (emission,
                    typeDefRow: emittedEnums.Count
                        + retainedClassIndexes.Count + ordinal + 1))
                .ToDictionary(static item => (
                        item.emission.View.Provider.ModuleIdentity,
                        checked((int)(item.emission.View.Identity
                            .DefinitionId & 0x00FF_FFFF))),
                    static item => item.typeDefRow);
        var typeDefRowByRootClassIdentity
            = standardClassDeclarationsByTypeDef
                .Select((emission, ordinal) => (emission,
                    typeDefRow: emittedEnums.Count
                        + retainedClassIndexes.Count + ordinal + 1))
                .ToDictionary(static item => item.emission.View.Identity,
                    static item => item.typeDefRow);
        var packedClassIdentityByRootClassImport
            = new Dictionary<(string ModuleIdentity, int ImportRow),
                BoundGenericNominalTypeIdentity>();
        foreach (var declaration in standardClassDeclarations)
        {
            if (!packedClassModuleIdentities.Contains(
                    declaration.Declaration.Provider.ModuleIdentity)
                || !typeDefRowByRootClassIdentity.ContainsKey(
                    declaration.Declaration.Identity))
            {
                continue;
            }

            foreach (var expectation in declaration.Expectations)
            {
                var key = (
                    expectation.Binding.UsingModule.ModuleIdentity,
                    expectation.Binding.ImportRow);
                if (packedClassIdentityByRootClassImport.TryGetValue(
                        key, out var existingIdentity))
                {
                    if (existingIdentity
                        != declaration.Declaration.Identity)
                    {
                        throw Invalid(
                            "One packed class Import row resolves to conflicting nominal TypeDefs.");
                    }

                    continue;
                }

                packedClassIdentityByRootClassImport.Add(key,
                    declaration.Declaration.Identity);
            }
        }
        var typeDefRowByStructure = retainedStructureIndexes
            .Select((structureIndex, ordinal) => (structureIndex,
                typeDefRow: emittedEnums.Count + retainedClassIndexes.Count
                    + standardClassDeclarationsByTypeDef.Length
                    + ordinal + 1))
            .ToDictionary(static item => item.structureIndex,
                static item => item.typeDefRow);
        var typeSpecByClassType = new Dictionary<
            (int ClassIndex, bool IsWritable, bool IsOptional), uint>();
        var typeSpecByStructure = new Dictionary<int, uint>();
        var exportRows = new List<TrRmExportRow>();
        var fieldExportRows = new List<TrRmExportRow>();
        var moduleRefRows = new List<TrRmModuleRefRow>();
        var moduleRefRowsByProvider = new Dictionary<
            (string ProducerIdentity, uint TargetRequirement,
                uint RuntimePolicy), int>();
        var importRows = new List<TrRmImportRow>();
        var valueBuilder = new TrRmFrozenScalarValueBuilder();
        var hasBlittableEnum = false;
        var hasTargetProfile = false;
        var standardClassFieldExportKeysByDefinition
            = new Dictionary<uint, List<string>>();
        if (standardClassRootTables is not null)
        {
            foreach (var (definition, exportKey) in standardClassRootTables
                .Exports.GetExportsForModule(rootSnapshot!.ModuleRow))
            {
                if ((definition >> 24) != 0x14)
                {
                    continue;
                }

                if (!standardClassFieldExportKeysByDefinition.TryGetValue(
                        definition, out var exportKeys))
                {
                    exportKeys = [];
                    standardClassFieldExportKeysByDefinition.Add(
                        definition, exportKeys);
                }
                exportKeys.Add(exportKey);
            }
        }
        var enumIndexByRootTypeDefRow
            = new Dictionary<(string ModuleIdentity, int TypeDefRow), int>();
        var enumIndexByProviderTypeDefRow
            = new Dictionary<(string ModuleIdentity, int TypeDefRow), int>();
        var importRowByProviderEnumImport
            = new Dictionary<(string ModuleIdentity, int ImportRow), int>();
        for (var enumIndex = 0;
            enumIndex < program.EnumDefinitions.Length; enumIndex++)
        {
            var definition = program.EnumDefinitions[enumIndex];
            if (definition.Identity
                is not BoundEnumDeclarationIdentity.Standard standard)
            {
                throw Invalid(
                    "EN1 class re-emission requires retained Standard enum identities.");
            }

            var view = standard.Value.Declaration;
            var typeDefId = view.Site.DefinitionId;
            var typeDefRow = checked((int)(typeDefId & 0x00FF_FFFF));
            if ((typeDefId >> 24) != 0x13 || typeDefRow == 0
                || !enumIndexByProviderTypeDefRow.TryAdd((
                    view.Site.Snapshot.ModuleIdentity, typeDefRow),
                    enumIndex)
                || !enumIndexByRootTypeDefRow.TryAdd((
                    view.Site.Snapshot.ModuleIdentity, typeDefRow),
                    enumIndex))
            {
                throw Invalid(
                    "EN1 class re-emission found an invalid or duplicate enum provider TypeDef identity.");
            }
        }

        int AddString(string text)
        {
            if (!stringRowsByText.TryGetValue(text, out var row))
            {
                row = stringRows.Count + 1;
                stringRows.Add((text, GetStringEncoding(text)));
                stringRowsByText.Add(text, row);
            }

            return row;
        }

        var standardClassTypeSpecRows
            = new Dictionary<(string ModuleIdentity, uint TypeSpecRef), uint>();
        var activeStandardClassTypeSpecs
            = new HashSet<(string ModuleIdentity, uint TypeSpecRef)>();
        uint StandardClassFieldTypeSpecReference(
            BoundStandardClassFieldDeclaration field,
            BoundSourceTypeProviderSnapshot provider)
            => NativeFunctionPointerTypes.IsType(field.Type)
                ? FieldTypeSpecReference(field.Type)
                : RemapStandardClassTypeSpecReference(
                    field.TypeSpecReference, 0, provider);

        uint RemapStandardClassTypeSpecReference(uint sourceTypeSpec,
            int depth,
            BoundSourceTypeProviderSnapshot? sourceProvider = null)
        {
            sourceProvider ??= rootSnapshot;
            if (sourceProvider is null
                || !standardClassRootTablesByProvider.TryGetValue(
                    sourceProvider.ModuleIdentity, out var sourceTables))
            {
                throw new InvalidDataException(
                    "An EN1 Standard class FieldDef has no validated root artifact.");
            }
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "EN1 class TypeSpec remapping exceeds the checked nesting limit.");
            }

            var typeSpecKey = (sourceProvider.ModuleIdentity,
                sourceTypeSpec);
            var type = sourceTables.TypeSpecs.GetTypeSpecRef(
                sourceTypeSpec);
            if (type.Form == 0xF2 && type.Qualifiers == 0)
            {
                return sourceTypeSpec;
            }
            if (type.Form == 0x01 && type.TypeCode == 0x8C
                && type.ArgumentCode == 0 && type.Qualifiers == 0)
            {
                if (standardClassTypeSpecRows.TryGetValue(typeSpecKey,
                        out var existingPointerTypeSpec))
                {
                    return existingPointerTypeSpec;
                }

                var pointerTypeSpec
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.InlineFamily(
                    0x8C, 0));
                standardClassTypeSpecRows.Add(typeSpecKey,
                    pointerTypeSpec);
                return pointerTypeSpec;
            }
            if (type.Form is 0x01 or 0x0C
                && type.TypeCode == 0x8C
                && (type.Qualifiers & ~0x01) == 0
                && sourceTables.TypeSpecs
                    .TryGetNativePointerPointeeTypeSpecRef(
                        sourceTypeSpec, out var pointeeTypeSpecReference))
            {
                var pointee = sourceTables.TypeSpecs
                    .GetTypeSpecRef(pointeeTypeSpecReference);
                if (type.Form == 0x01
                    && (pointee.Form != 0xF2
                        || pointee.Qualifiers != 0
                        || !TrRmConstantSourceValueValidator
                            .TryGetBoundScalarType(pointee.TypeCode,
                                out _)))
                {
                    throw Unsupported(
                        "An inline EN1 native pointer TypeSpec requires a scalar pointee.");
                }

                var remappedPointeeTypeSpecReference = type.Form == 0x0C
                    ? RemapStandardClassTypeSpecReference(
                        pointeeTypeSpecReference, depth + 1,
                        sourceProvider)
                    : pointeeTypeSpecReference;
                var pointerTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(type.Form == 0x0C
                    ? TrRmTypeSpecTableWriter.IndexedFamily(0x8C,
                        remappedPointeeTypeSpecReference, type.Qualifiers)
                    : TrRmTypeSpecTableWriter.InlineFamily(0x8C,
                        type.ArgumentCode, type.Qualifiers));
                standardClassTypeSpecRows.Add(typeSpecKey,
                    pointerTypeSpecRow);
                return pointerTypeSpecRow;
            }
            if (type.Form == 0x0D && type.Qualifiers == 0)
            {
                if (standardClassTypeSpecRows.TryGetValue(typeSpecKey,
                        out var existingFunctionPointerTypeSpec))
                {
                    return existingFunctionPointerTypeSpec;
                }

                var anchorMethodRow = sourceTables
                    .NativeFunctionPointerAnchors
                    .GetAnchorMethodDefRow(type.Row);
                var signatureRow = 0;
                for (var candidateRow = 1;
                    candidateRow
                        <= sourceTables
                            .NativeFunctionPointerSignatures.Count;
                    candidateRow++)
                {
                    if (sourceTables
                            .NativeFunctionPointerSignatures
                            .GetMethodDefRow(candidateRow)
                        == anchorMethodRow)
                    {
                        signatureRow = candidateRow;
                        break;
                    }
                }
                if (signatureRow == 0)
                {
                    throw Invalid(
                        "An EN1 class MethodDef TypeSpec lost its function-pointer anchor.");
                }

                var signature = DecodeEN1FieldFunctionPointer(
                    sourceTables, type.Row,
                    sourceProvider.ModuleRow);
                var signatureIndex = -1;
                for (var index = 0;
                    index < program.NativeFunctionPointerSignatures.Length;
                    index++)
                {
                    if (NativeFunctionPointerContract.AreEquivalent(
                            program.NativeFunctionPointerSignatures[index],
                            signature))
                    {
                        signatureIndex = index;
                        break;
                    }
                }
                if (signatureIndex < 0
                    || (uint)signatureIndex
                        >= (uint)nativeFunctionPointerAnchorMethodRows.Length
                    || nativeFunctionPointerAnchorMethodRows[
                        signatureIndex] == 0)
                {
                    throw Invalid(
                        "An EN1 class MethodDef function-pointer signature has no output anchor.");
                }

                var functionPointerTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.NativeFunctionPointer(
                        (byte)signature.CallingConvention,
                        checked((uint)nativeFunctionPointerAnchorMethodRows[
                            signatureIndex])));
                standardClassTypeSpecRows.Add(typeSpecKey,
                    functionPointerTypeSpecRow);
                return functionPointerTypeSpecRow;
            }
            if (standardClassTypeSpecRows.TryGetValue(typeSpecKey,
                    out var existingTypeSpec))
            {
                return existingTypeSpec;
            }
            if (!type.IsRow || !activeStandardClassTypeSpecs.Add(
                    typeSpecKey))
            {
                throw Invalid(
                    "An EN1 class FieldDef contains a cyclic or non-row TypeSpec reference.");
            }

            try
            {
                uint encodedTypeSpec;
                if (type.Form == 0x06 && type.Qualifiers == 0)
                {
                    var row = sourceTables.TypeSpecs.GetRow(
                        type.Row);
                    var innerTypeSpec = BinaryPrimitives
                        .ReadUInt32LittleEndian(row[2..]);
                    encodedTypeSpec = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(
                        type.Form, RemapStandardClassTypeSpecReference(
                            innerTypeSpec, depth + 1, sourceProvider),
                        type.Qualifiers));
                }
                else if (type.Form is 0x02 or 0x03)
                {
                    var oldReferenceRow = checked((int)type.Row);
                    uint newReferenceRow;
                    var newForm = type.Form;
                    var typeDefProviderIdentity = type.Form == 0x02
                        ? sourceTables.Modules.GetIdentity(
                            sourceTables.TypeDefs.GetOwnerModule(
                                oldReferenceRow))
                        : sourceProvider.ModuleIdentity;
                    if (type.Form == 0x02
                        && typeDefRowByProviderClassTypeDef.TryGetValue((
                            typeDefProviderIdentity,
                            oldReferenceRow), out var providerTypeDefRow))
                    {
                        newReferenceRow
                            = checked((uint)providerTypeDefRow);
                    }
                    else if (type.Form == 0x02
                        && StringComparer.Ordinal.Equals(
                            typeDefProviderIdentity,
                            sourceProvider.ModuleIdentity)
                        && rootSnapshot is not null
                        && SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            sourceProvider)
                        && typeDefRowByRootClassTypeDef.TryGetValue(
                            oldReferenceRow, out var classTypeDefRow))
                    {
                        newReferenceRow = checked((uint)classTypeDefRow);
                    }
                    else if (type.Form == 0x03
                        && packedClassIdentityByRootClassImport.TryGetValue((
                            sourceProvider.ModuleIdentity,
                            oldReferenceRow), out var packedIdentity)
                        && typeDefRowByRootClassIdentity.TryGetValue(
                            packedIdentity, out var packedTypeDefRow))
                    {
                        newForm = 0x02;
                        newReferenceRow
                            = checked((uint)packedTypeDefRow);
                    }
                    else if (type.Form == 0x03
                        && importRowByRootClassImport.TryGetValue(
                            (sourceProvider.ModuleIdentity,
                                oldReferenceRow),
                            out var classImportRow))
                    {
                        newReferenceRow = checked((uint)classImportRow);
                    }
                    else if (type.Form == 0x02 && type.Qualifiers == 0
                        && enumIndexByProviderTypeDefRow.TryGetValue(
                            (typeDefProviderIdentity,
                                oldReferenceRow),
                            out var enumIndex))
                    {
                        encodedTypeSpec = FieldTypeSpecReference(
                            BoundEnumTypes.Type(enumIndex));
                        standardClassTypeSpecRows.Add(typeSpecKey,
                            encodedTypeSpec);
                        return encodedTypeSpec;
                    }
                    else if (type.Form == 0x03 && type.Qualifiers == 0
                        && importRowByProviderEnumImport.TryGetValue(
                            (sourceProvider.ModuleIdentity,
                                oldReferenceRow),
                            out var enumImportRow))
                    {
                        newReferenceRow = checked((uint)enumImportRow);
                    }
                    else if (type.Form == 0x02 && type.Qualifiers == 0
                        && structureIndexByRootTypeDefRow.TryGetValue(
                            (typeDefProviderIdentity,
                                oldReferenceRow),
                            out var structureIndex))
                    {
                        encodedTypeSpec = FieldTypeSpecReference(
                            (PrimitiveType)(100 + structureIndex));
                        standardClassTypeSpecRows.Add(typeSpecKey,
                            encodedTypeSpec);
                        return encodedTypeSpec;
                    }
                    else if (type.Form == 0x03 && type.Qualifiers == 0
                        && importRowByRootStructureImport.TryGetValue(
                            (sourceProvider.ModuleIdentity, oldReferenceRow),
                            out var structureImportRow))
                    {
                        newReferenceRow
                            = checked((uint)structureImportRow);
                    }
                    else
                    {
                        throw Unsupported(
                            "An EN1 class FieldDef TypeSpec refers to an unretained class, struct or enum identity.");
                    }

                    encodedTypeSpec = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        newForm, newReferenceRow, type.Qualifiers));
                }
                else
                {
                    throw Unsupported(
                        "The bounded EN1 class re-emission supports scalar, wrapper and class/struct/enum TypeSpec forms only.");
                }

                standardClassTypeSpecRows.Add(typeSpecKey,
                    encodedTypeSpec);
                return encodedTypeSpec;
            }
            finally
            {
                _ = activeStandardClassTypeSpecs.Remove(typeSpecKey);
            }
        }

        uint FieldTypeSpecReference(PrimitiveType type)
        {
            if (BoundGenericNominalTypeTypes.IsType(type))
            {
                var instanceIndex
                    = BoundGenericNominalTypeTypes.Index(type);
                if ((uint)instanceIndex
                        >= (uint)program.GenericNominalTypeInstances.Length
                    || !BoundGenericNominalTypeContract.IsNominalClassType(
                        type, program.GenericNominalTypeInstances,
                        program.GenericNominalTypeDefinitions))
                {
                    throw Unsupported(
                        "EN1 FieldDefs support metadata-only nongeneric class handles only.");
                }

                var instance = program.GenericNominalTypeInstances[
                    instanceIndex];
                if (instance.TypeSpecReference == 0
                    || rootSnapshot is null
                    || !StringComparer.Ordinal.Equals(
                        instance.TypeSpecProviderIdentity,
                        rootSnapshot.ModuleIdentity))
                {
                    throw Invalid(
                        "An EN1 class FieldDef lost its root TypeSpec origin.");
                }

                return RemapStandardClassTypeSpecReference(
                    instance.TypeSpecReference, 0);
            }

            if (TryGetC1M1SourceClassFieldType(type,
                    out var classIndex, out var isWritable,
                    out var isOptional))
            {
                if (!typeDefRowByClass.TryGetValue(classIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "A captured EN1 class FieldDef refers to an unretained local class.");
                }

                var key = (classIndex, isWritable, isOptional);
                if (!typeSpecByClassType.TryGetValue(key,
                        out var typeSpecRow))
                {
                    var classTypeSpecRow
                        = checked((uint)typeSpecRows.Count + 1);
                    var qualifiers = isWritable ? (byte)0x01 : (byte)0;
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        0x02, checked((uint)typeDefRow), qualifiers));
                    typeSpecRow = classTypeSpecRow;
                    if (isOptional)
                    {
                        typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                        typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(
                            0x06, classTypeSpecRow));
                    }

                    typeSpecByClassType.Add(key, typeSpecRow);
                }

                return typeSpecRow;
            }

            if (TryGetEN1SourceStructFieldType(type,
                    out var structureIndex))
            {
                if (!typeDefRowByStructure.TryGetValue(structureIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "An EN1 source struct field refers to an unretained local struct.");
                }

                if (!typeSpecByStructure.TryGetValue(structureIndex,
                        out var typeSpecRow))
                {
                    typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                    var importRow = importRowByStructureIndex.TryGetValue(
                        structureIndex, out var selectedImportRow)
                        ? selectedImportRow : 0;
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        importRow == 0 ? (byte)0x02 : (byte)0x03,
                        checked((uint)(importRow == 0
                            ? typeDefRow : importRow))));
                    typeSpecByStructure.Add(structureIndex, typeSpecRow);
                }

                return typeSpecRow;
            }

            if (BoundEnumTypes.IsType(type))
            {
                var enumIndex = BoundEnumTypes.Index(type);
                if (!typeDefRowByEnumIndex.TryGetValue(enumIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "An EN1 source struct field refers to an unretained local enum.");
                }

                if (!typeSpecByBoundEnumIndex.TryGetValue(enumIndex,
                        out var typeSpecRow))
                {
                    typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                    var importRow = importRowByEnumIndex.TryGetValue(
                        enumIndex, out var selectedImportRow)
                        ? selectedImportRow : 0;
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        importRow == 0 ? (byte)0x02 : (byte)0x03,
                        checked((uint)(importRow == 0
                            ? typeDefRow : importRow))));
                    typeSpecByBoundEnumIndex.Add(enumIndex, typeSpecRow);
                }

                return typeSpecRow;
            }

            if (NativeFunctionPointerTypes.IsType(type))
            {
                var signatureIndex
                    = NativeFunctionPointerTypes.Index(type);
                if ((uint)signatureIndex
                        >= (uint)program.NativeFunctionPointerSignatures.Length
                    || !IsSupportedEN1FieldFunctionPointer(
                        program.NativeFunctionPointerSignatures[
                            signatureIndex]))
                {
                    throw Unsupported(
                        "EN1 FieldDefs require a covered scalar-signature native function pointer.");
                }

                var anchorMethodRow
                    = nativeFunctionPointerAnchorMethodRows[signatureIndex];
                if (anchorMethodRow == 0)
                {
                    throw new InvalidDataException(
                        "An EN1 native function-pointer FieldDef has no retained MethodDef anchor.");
                }

                var functionPointerTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.NativeFunctionPointer(
                        (byte)program.NativeFunctionPointerSignatures[
                            signatureIndex].CallingConvention,
                        checked((uint)anchorMethodRow)));
                return functionPointerTypeSpecRow;
            }

            if (NativePointerTypes.IsType(type))
            {
                var pointerIndex = NativePointerTypes.Index(type);
                if ((uint)pointerIndex
                        >= (uint)program.NativePointerTypes.Length)
                {
                    throw Unsupported(
                        "An EN1 class field references an unknown native pointer type.");
                }

                var pointer = program.NativePointerTypes[pointerIndex];
                if (!IsSupportedEN1FieldPointer(pointer, program))
                {
                    throw Unsupported(
                        "EN1 class fields require a covered native data pointer with a scalar, enum, non-generic struct or class-handle pointee.");
                }

                var qualifiers
                    = pointer.IsWritable ? (byte)1 : (byte)0;
                if (pointer.PointeeType == PrimitiveType.Void
                    || PrimitiveTypeFacts.IsBuiltInScalar(
                        pointer.PointeeType))
                {
                    var pointeeTypeCode = pointer.PointeeType
                            == PrimitiveType.Void
                        ? (byte)0
                        : checked((byte)(
                            GetC1M1SourceScalarTypeSpecReference(
                                pointer.PointeeType) & 0xFF));
                    var pointerTypeSpecRow
                        = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(
                        TrRmTypeSpecTableWriter.InlineFamily(0x8C,
                            pointeeTypeCode, qualifiers));
                    return pointerTypeSpecRow;
                }

                var pointeeTypeSpecReference
                    = FieldTypeSpecReference(pointer.PointeeType);
                var indexedPointerTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(
                    0x8C, pointeeTypeSpecReference, qualifiers));
                return indexedPointerTypeSpecRow;
            }

            if (type == PrimitiveType.NativeRawPointer)
            {
                var pointerTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.InlineFamily(
                    0x8C, 0));
                return pointerTypeSpecRow;
            }

            return GetC1M1SourceScalarTypeSpecReference(type);
        }

        for (var enumIndex = 0; enumIndex < emittedEnums.Count; enumIndex++)
        {
            var emittedEnum = emittedEnums[enumIndex];
            if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                    emittedEnum.Name)
                || !TryGetEnumTypeCode(emittedEnum.UnderlyingType,
                    out var underlyingTypeCode))
            {
                throw new InvalidDataException(
                    "The EN1 enum emission no longer matches its checked declaration.");
            }

            if (string.IsNullOrWhiteSpace(emittedEnum.Name)
                || (emittedEnum.TypeDefFlags & 0x1C) != 0x10
                || (emittedEnum.TypeDefFlags & 3) == 3
                || (emittedEnum.TypeDefFlags & ~0x1Fu) != 0
                || emittedEnum.EnumFlags > 1
                || emittedEnum.Layouts.IsDefaultOrEmpty
                || emittedEnum.Members.IsDefault
                || emittedEnum.ExportKeys.IsDefault
                || (emittedEnum.Import is not null
                    && !emittedEnum.ExportKeys.IsEmpty))
            {
                throw new InvalidDataException(
                    "An EN1 enum emission has invalid declaration flags or mappings.");
            }

            var typeDefRow = enumIndex + 1;
            if ((uint)emittedEnum.DefinitionIndex
                    >= (uint)program.EnumDefinitions.Length)
            {
                throw Invalid(
                    "An EN1 enum emission lost its checked declaration index.");
            }
            _ = typeDefRowByEnumIndex.TryAdd(
                emittedEnum.DefinitionIndex, typeDefRow);
            var namespaceName = emittedEnum.Namespace;
            var typeName = emittedEnum.Name;
            var layoutFirst = layoutRows.Count + 1;
            var hasWindowsProfileForEnum = false;
            for (var layoutIndex = 0;
                layoutIndex < emittedEnum.Layouts.Length; layoutIndex++)
            {
                var layout = emittedEnum.Layouts[layoutIndex];
                if (layout.Kind != 0 || (layout.Flags & ~1) != 0
                    || layout.Alignment != 0 || layout.Pack != 0)
                {
                    throw new InvalidDataException(
                        "An EN1 enum layout has unsupported fields or flags.");
                }

                var targetProfileReference = 0u;
                if (layout.TargetProfileId != 0
                    || layout.TargetProfileRuleRevision != 0)
                {
                    if (layout.TargetProfileId
                            != TrRmTargetProfileTableReader
                                .WindowsX64NativeProfileId
                        || layout.TargetProfileRuleRevision
                            != TrRmTargetProfileTableReader
                                .WindowsX64NativeProfileRuleRevision)
                    {
                        throw Unsupported(
                            "EN1 enum layouts require the selected Windows x64 target profile.");
                    }

                    targetProfileReference = 1;
                    hasWindowsProfileForEnum = true;
                    hasTargetProfile = true;
                }

                if ((layout.Flags & 1) != 0
                    && targetProfileReference == 0)
                {
                    throw new InvalidDataException(
                        "An EN1 Blittable enum layout requires a target profile.");
                }

                hasBlittableEnum |= (layout.Flags & 1) != 0;
                layoutRows.Add(new TrRmLayoutRow(typeDefRow,
                    targetProfileReference, layout.Kind, layout.Flags,
                    layout.Alignment, layout.Pack, 0, 0));
            }

            var hasNativeWidth = underlyingTypeCode is 0x10 or 0x11;
            if ((hasNativeWidth || hasWindowsProfileForEnum)
                && moduleTargetRequirement
                    != TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement)
            {
                throw Unsupported(
                    "EN1 native-width and profile-specific enum layouts require the selected Windows x64 target.");
            }
            if (hasNativeWidth && !hasWindowsProfileForEnum)
            {
                throw new InvalidDataException(
                    "An EN1 native-width enum has no Windows x64 layout variant.");
            }

            typeDefRows.Add(new TrRmTypeDefRow(0, 0,
                emittedEnum.TypeDefFlags, 0, 0, 0, 0,
                checked((uint)layoutFirst),
                checked((uint)emittedEnum.Layouts.Length),
                0, 0));
            typeDefModuleIdentities.Add(moduleIdentity);
            typeDefNames.Add((namespaceName, typeName));

            var enumImportRow = 0;
            if (emittedEnum.Import is { } enumImport)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                        enumImport.ProducerIdentity)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        enumImport.ExportKey))
                {
                    throw new InvalidDataException(
                        "An EN1 enum Import has an invalid producer identity or ExportKey.");
                }

                var providerKey = (enumImport.ProducerIdentity,
                    enumImport.TargetRequirement, enumImport.RuntimePolicy);
                if (!moduleRefRowsByProvider.TryGetValue(providerKey,
                        out var moduleRefRow))
                {
                    moduleRefRow = moduleRefRows.Count + 1;
                    moduleRefRows.Add(new TrRmModuleRefRow(
                        AddString(enumImport.ProducerIdentity),
                        enumImport.TargetRequirement,
                        enumImport.RuntimePolicy));
                    moduleRefRowsByProvider.Add(providerKey, moduleRefRow);
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    AddString(enumImport.ExportKey),
                    0x1300_0000u | checked((uint)typeDefRow)));
                enumImportRow = importRows.Count;
                if (!emittedEnum.RootImports.IsDefaultOrEmpty)
                {
                    foreach (var rootImport in emittedEnum.RootImports)
                    {
                        var occurrence
                            = ((BoundEnumDeclarationIdentity.Standard)
                                program.EnumDefinitions[
                                    emittedEnum.DefinitionIndex].Identity)
                                .Value;
                        var rootExpectation = occurrence.Expectations
                            .SingleOrDefault(expectation =>
                                expectation.Binding.ImportRow
                                    == rootImport.ImportRow
                                && StringComparer.Ordinal.Equals(
                                    expectation.Binding.UsingModule
                                        .ModuleIdentity,
                                    rootImport.ProviderIdentity));
                        if (rootImport.ImportRow <= 0
                            || rootExpectation is null
                            || !standardClassDeclarationsByTypeDef.Any(
                                emission =>
                                    BoundStandardEnumDeclarationContract
                                        .SnapshotsMatch(
                                            emission.View.Provider,
                                            rootExpectation.Binding
                                                .UsingModule))
                            || !standardClassRootTablesByProvider.TryGetValue(
                                rootImport.ProviderIdentity,
                                out var rootTables))
                        {
                            throw Invalid(
                                "An EN1 class enum Import lost its exact root provider snapshot.");
                        }

                        var rootBinding
                            = rootTables.Imports.GetBindingForImportRow(
                                rootImport.ImportRow);
                        var expectedEnumDefinition
                            = rootTables.Imports
                                .GetExpectedDefinitionForImportRow(
                                    rootImport.ImportRow);
                        var expectedEnumTypeDefRow = checked((int)(
                            expectedEnumDefinition & 0x00FF_FFFF));
                        var expectedEnumProviderTypeDefKey
                            = (rootImport.ProviderIdentity,
                                expectedEnumTypeDefRow);
                        if (!StringComparer.Ordinal.Equals(
                                rootBinding.ProducerIdentity,
                                enumImport.ProducerIdentity)
                            || rootBinding.TargetRequirement
                                != enumImport.TargetRequirement
                            || rootBinding.RuntimePolicy
                                != enumImport.RuntimePolicy
                            || !StringComparer.Ordinal.Equals(
                                rootBinding.ExportKey,
                                rootExpectation.Binding.ExportKey)
                            || expectedEnumDefinition
                                != rootExpectation.Binding
                                    .ExpectedDefinitionId
                            || (expectedEnumDefinition >> 24) != 0x13
                            || expectedEnumTypeDefRow <= 0
                            || expectedEnumTypeDefRow
                                > rootTables.TypeDefs.Count
                            || rootTables.ModuleRefs.GetOwnerModule(
                                rootTables.Imports.GetModuleRefForImportRow(
                                    rootImport.ImportRow))
                                != rootExpectation.Binding.UsingModule
                                    .ModuleRow
                            || rootTables.TypeDefs.GetKind(
                                expectedEnumTypeDefRow) != 4
                            || !importRowByProviderEnumImport.TryAdd((
                                rootImport.ProviderIdentity,
                                rootImport.ImportRow), enumImportRow))
                        {
                            throw Invalid(
                                "An EN1 class enum Import has an invalid or duplicate root Import identity.");
                        }

                        if (!enumIndexByProviderTypeDefRow.TryAdd(
                                expectedEnumProviderTypeDefKey,
                                emittedEnum.DefinitionIndex)
                            && enumIndexByProviderTypeDefRow[
                                expectedEnumProviderTypeDefKey]
                                != emittedEnum.DefinitionIndex)
                        {
                            throw Invalid(
                                "An EN1 class enum Import conflicts with its expected provider TypeDef identity.");
                        }
                        if (!enumIndexByRootTypeDefRow.TryAdd(
                                expectedEnumProviderTypeDefKey,
                                emittedEnum.DefinitionIndex)
                            && enumIndexByRootTypeDefRow[
                                expectedEnumProviderTypeDefKey]
                                != emittedEnum.DefinitionIndex)
                        {
                            throw Invalid(
                                "An EN1 class enum Import conflicts with its expected root TypeDef identity.");
                        }
                        if (!enumIndexByRootEnumImport.TryAdd((
                                rootImport.ProviderIdentity,
                                rootImport.ImportRow),
                            emittedEnum.DefinitionIndex)
                            && enumIndexByRootEnumImport[(
                                rootImport.ProviderIdentity,
                                rootImport.ImportRow)]
                                != emittedEnum.DefinitionIndex)
                        {
                            throw Invalid(
                                "An EN1 class enum Import conflicts with its expected root Import identity.");
                        }
                    }
                }
                else
                {
                    var occurrence
                            = ((BoundEnumDeclarationIdentity.Standard)
                                program.EnumDefinitions[
                                    emittedEnum.DefinitionIndex].Identity).Value;
                    var providerTypeDefId
                            = occurrence.Declaration.Site.DefinitionId;
                    var providerTypeDefRow = checked((int)(
                            providerTypeDefId & 0x00FF_FFFF));
                    var enumProviderSnapshot
                        = occurrence.Declaration.Site.Snapshot;
                    if (!standardClassRootTablesByProvider.TryGetValue(
                            enumProviderSnapshot.ModuleIdentity,
                            out var enumProviderTables)
                        || !StringComparer.Ordinal.Equals(
                            enumProviderSnapshot.ModuleIdentity,
                            enumImport.ProducerIdentity)
                        || (providerTypeDefId >> 24) != 0x13
                        || providerTypeDefRow == 0
                        || enumProviderTables.TypeDefs.GetKind(
                            providerTypeDefRow) != 4
                        || !enumIndexByProviderTypeDefRow.TryGetValue(
                            (enumProviderSnapshot.ModuleIdentity,
                                providerTypeDefRow),
                            out var mappedEnumIndex)
                        || mappedEnumIndex != emittedEnum.DefinitionIndex)
                    {
                        throw Invalid(
                            "An EN1 direct enum Import lost its provider TypeDef identity.");
                    }

                }
                _ = importRowByEnumIndex.TryAdd(
                    emittedEnum.DefinitionIndex, enumImportRow);
            }

            if (!emittedEnum.Members.IsEmpty)
            {
                var typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                    enumImportRow == 0 ? (byte)0x02 : (byte)0x03,
                    checked((uint)(enumImportRow == 0
                        ? typeDefRow : enumImportRow))));
                typeSpecByEmissionEnumIndex.Add(enumIndex, typeSpecRow);
            }

            var memberFirst = emittedEnum.Members.IsEmpty
                ? 0 : checked(enumMemberRows.Count + 1);
            for (var memberIndex = 0;
                memberIndex < emittedEnum.Members.Length; memberIndex++)
            {
                var member = emittedEnum.Members[memberIndex];
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                        member.DeclarationKey)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        member.Name)
                    || !ScalarBits.TryFromIntegerValue(member.Value,
                        emittedEnum.UnderlyingType, out var bits))
                {
                    throw Unsupported(
                        "An EN1 enum member value is outside its exact underlying integer type.");
                }

                var constant = new BoundConstant(bits,
                    BoundEnumTypes.Type(enumIndex),
                    emittedEnum.UnderlyingType, default);
                var frozenValue = valueBuilder.AddC1EnumClosureMember(
                    constant, typeSpecByEmissionEnumIndex[enumIndex],
                    emittedEnum.UnderlyingType, schema);
                enumMemberRows.Add(new TrRmTypedEnumMemberDefRow(
                    typeDefRow, enumMemberRows.Count - memberFirst + 1,
                    AddString(member.DeclarationKey),
                    AddString(member.Name),
                    frozenValue.ConstantSourceRow));
            }

            enumRows.Add(new TrRmEnumDefRow(typeDefRow,
                0xF200_0000u | underlyingTypeCode,
                emittedEnum.EnumFlags, memberFirst,
                emittedEnum.Members.Length));
            foreach (var exportKey in emittedEnum.ExportKeys)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    AddString(exportKey)));
            }
        }

        foreach (var emission in standardClassDeclarationsByTypeDef)
        {
            if (emission.Import is not C1EnumClosureImport primaryTypeImport)
            {
                continue;
            }

            if (!typeDefRowByRootClassIdentity.TryGetValue(
                    emission.View.Identity, out var typeDefRow))
            {
                throw Invalid(
                    "An EN1 class TypeDef Import has an invalid provider or root identity.");
            }

            var typeImports = ImmutableArray.CreateBuilder<
                C1EnumClosureImport>(1
                    + emission.AdditionalImports.Length);
            typeImports.Add(primaryTypeImport);
            typeImports.AddRange(emission.AdditionalImports);
            foreach (var typeImport in typeImports)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                        typeImport.ProducerIdentity)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        typeImport.ExportKey)
                    || typeImport.TargetRequirement
                        != moduleTargetRequirement
                    || typeImport.RuntimePolicy
                        != (uint)program.RuntimePolicy
                    || typeImport.RootImportRow < 0
                    || (!typeImport.RootImports.IsDefaultOrEmpty
                        && typeImport.RootImports.Any(rootImport =>
                            !BoundEnumTypes.IsValidModuleDeclarationKey(
                                rootImport.ProviderIdentity)
                            || rootImport.ImportRow <= 0)))
                {
                    throw Invalid(
                        "An EN1 class TypeDef Import has an invalid provider or root identity.");
                }

                var providerKey = (typeImport.ProducerIdentity,
                    typeImport.TargetRequirement, typeImport.RuntimePolicy);
                if (!moduleRefRowsByProvider.TryGetValue(providerKey,
                        out var moduleRefRow))
                {
                    moduleRefRow = moduleRefRows.Count + 1;
                    moduleRefRows.Add(new TrRmModuleRefRow(
                        AddString(typeImport.ProducerIdentity),
                        typeImport.TargetRequirement,
                        typeImport.RuntimePolicy));
                    moduleRefRowsByProvider.Add(providerKey, moduleRefRow);
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    AddString(typeImport.ExportKey),
                    0x1300_0000u | checked((uint)typeDefRow)));
                if (!typeImport.RootImports.IsDefaultOrEmpty)
                {
                    foreach (var rootImport in typeImport.RootImports)
                    {
                        if (!importRowByRootClassImport.TryAdd((
                                rootImport.ProviderIdentity,
                                rootImport.ImportRow), importRows.Count))
                        {
                            throw Invalid(
                                "An EN1 class TypeDef Import row was emitted more than once for one root provider.");
                        }
                    }
                }
                else if (typeImport.RootImportRow > 0)
                {
                    var rootProviderIdentity
                        = typeImport.RootImportProviderIdentity
                            ?? rootSnapshot?.ModuleIdentity;
                    if (rootProviderIdentity is null
                        || !importRowByRootClassImport.TryAdd((
                            rootProviderIdentity,
                            typeImport.RootImportRow), importRows.Count))
                    {
                        throw Invalid(
                            "An EN1 class TypeDef Import row was emitted more than once.");
                    }
                }
                _ = importRowByRootClassIdentity.TryAdd(
                    emission.View.Identity, importRows.Count);
            }
        }

        foreach (var (structureIndex, importsForType) in
            sourceStructImportsByIndex.OrderBy(
                static item => item.Key))
        {
            var typeDefRow = typeDefRowByStructure[structureIndex];
            foreach (var typeImport in importsForType)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                        typeImport.ProducerIdentity)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        typeImport.ExportKey))
                {
                    throw Invalid(
                        "A mixed EN1 struct Import has an invalid provider identity or ExportKey.");
                }

                var providerKey = (typeImport.ProducerIdentity,
                    typeImport.TargetRequirement, typeImport.RuntimePolicy);
                if (!moduleRefRowsByProvider.TryGetValue(providerKey,
                        out var moduleRefRow))
                {
                    moduleRefRow = moduleRefRows.Count + 1;
                    moduleRefRows.Add(new TrRmModuleRefRow(
                        AddString(typeImport.ProducerIdentity),
                        typeImport.TargetRequirement,
                        typeImport.RuntimePolicy));
                    moduleRefRowsByProvider.Add(providerKey, moduleRefRow);
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    AddString(typeImport.ExportKey),
                    0x1300_0000u | checked((uint)typeDefRow)));
                _ = importRowByStructureIndex.TryAdd(structureIndex,
                    importRows.Count);
                if (!typeImport.RootImports.IsDefaultOrEmpty)
                {
                    foreach (var rootImport in typeImport.RootImports)
                    {
                        var structureOrigin
                            = program.Structures[structureIndex]
                                .StandardTypeOrigin!;
                        var rootExpectation = structureOrigin.Expectations
                            .SingleOrDefault(expectation =>
                                expectation.ImportRow == rootImport.ImportRow
                                && StringComparer.Ordinal.Equals(
                                    expectation.UsingModule.ModuleIdentity,
                                    rootImport.ProviderIdentity));
                        if (rootImport.ImportRow <= 0
                            || rootExpectation is null
                            || !standardClassDeclarationsByTypeDef.Any(
                                emission =>
                                    BoundStandardEnumDeclarationContract
                                        .SnapshotsMatch(
                                            emission.View.Provider,
                                            rootExpectation.UsingModule))
                            || !standardClassRootTablesByProvider.TryGetValue(
                                rootImport.ProviderIdentity,
                                out var rootTables))
                        {
                            throw Invalid(
                                "An EN1 struct TypeDef Import lost its selected root provider.");
                        }

                        var rootBinding
                            = rootTables.Imports.GetBindingForImportRow(
                                rootImport.ImportRow);
                        var expectedTypeDefId
                            = rootTables.Imports
                                .GetExpectedDefinitionForImportRow(
                                    rootImport.ImportRow);
                        var expectedTypeDefRow = checked((int)(
                            expectedTypeDefId & 0x00FF_FFFF));
                        if (!StringComparer.Ordinal.Equals(
                                rootBinding.ProducerIdentity,
                                typeImport.ProducerIdentity)
                            || !StringComparer.Ordinal.Equals(
                                rootBinding.ExportKey,
                                rootExpectation.ExportKey)
                            || rootBinding.TargetRequirement
                                != typeImport.TargetRequirement
                            || rootBinding.RuntimePolicy
                                != typeImport.RuntimePolicy
                            || expectedTypeDefId
                                != rootExpectation.ExpectedDefinitionId
                            || (expectedTypeDefId >> 24) != 0x13
                            || expectedTypeDefRow <= 0
                            || expectedTypeDefRow > rootTables.TypeDefs.Count
                            || rootTables.TypeDefs.GetKind(
                                expectedTypeDefRow) != 1
                            || rootTables.ModuleRefs.GetOwnerModule(
                                rootTables.Imports.GetModuleRefForImportRow(
                                    rootImport.ImportRow))
                                != rootExpectation.UsingModule.ModuleRow
                            || !importRowByRootStructureImport.TryAdd((
                                rootImport.ProviderIdentity,
                                rootImport.ImportRow), importRows.Count))
                        {
                            throw Invalid(
                                "An EN1 struct TypeDef Import has an invalid or duplicate root binding.");
                        }
                    }
                }
                else if (typeImport.RootImportRow != 0)
                {
                    var rootProviderIdentity
                        = typeImport.RootImportProviderIdentity
                            ?? rootSnapshot?.ModuleIdentity;
                    if (rootProviderIdentity is null
                        || typeImport.RootImportRow < 0
                        || !importRowByRootStructureImport.TryAdd((
                            rootProviderIdentity,
                            typeImport.RootImportRow), importRows.Count))
                    {
                        throw Invalid(
                            "An EN1 struct TypeDef Import row was emitted more than once.");
                    }
                }
            }
        }

        foreach (var classIndex in retainedClassIndexes)
        {
            var @class = program.Classes[classIndex];
            var className = @class.Name;
            var separator = className.LastIndexOf('.',
                StringComparison.Ordinal);
            var namespaceName = separator < 0
                ? "" : className[..separator];
            var typeName = separator < 0
                ? className : className[(separator + 1)..];
            if (string.IsNullOrWhiteSpace(typeName) || separator == 0)
            {
                throw Unsupported(
                    "EN1 class names require valid namespace and type-name components.");
            }

            var typeDefRow = typeDefRowByClass[classIndex];
            var classExportKeys = sourceClassExportsByIndex[classIndex];
            var typeDefFlags = (classExportKeys.Count != 0 ? 2u
                : @class.IsInternal ? 1u : 2u) | 0x08u
                    | (@class.IsMutable ? 0x80u : 0u);
            if (schema.UsesConstructionInitializerCopyEvidence())
            {
                if (@class.HasCleanup == true
                    || @class.CleanupMethod is not null
                    || @class.IsUnique is not bool isUnique)
                {
                    throw Unsupported(
                        "Initializer Copy evidence requires a retained total class-uniqueness fact without an unrepresented class Cleanup.");
                }

                if (isUnique)
                {
                    typeDefFlags
                        |= TrRmTypeDefTableReader.DeclaredUniqueFlag;
                }
            }
            var fieldFirst = fieldRows.Count + 1;
            var layoutFieldFirst = layoutFieldRows.Count + 1;
            var fieldNamesForClass = new HashSet<string>(
                StringComparer.Ordinal);
            for (var fieldOrdinal = 0;
                fieldOrdinal < @class.Fields.Length; fieldOrdinal++)
            {
                var field = @class.Fields[fieldOrdinal];
                if (field is null || (field.IsPrivate && field.IsInternal)
                    || field.RequiresUnsafeCaller
                    || field.BackingPropertyName is not null
                    || !fieldNamesForClass.Add(field.Name))
                {
                    throw Unsupported(
                        "EN1 class FieldDefs cannot discard conflicting access, unsafe-caller or backing-property facts.");
                }

                var visibility = field.IsPrivate ? 0u
                    : field.IsInternal ? 1u : 2u;
                var fieldFlags = visibility
                    | (field.IsMutable ? 0x04u : 0u);
                var fieldDefRow = fieldRows.Count + 1;
                fieldRows.Add(new TrRmFieldDefRow(typeDefRow, 0,
                    FieldTypeSpecReference(field.Type), fieldFlags));
                fieldNames.Add(field.Name);
                fieldDefRowByClassAndIndex.Add(
                    (classIndex, fieldOrdinal), fieldDefRow);
                layoutFieldRows.Add(new TrRmLayoutFieldRow(
                    layoutRows.Count + 1, fieldOrdinal, fieldDefRow));
                if (sourceClassFieldExportsByIndex.TryGetValue(
                        (classIndex, fieldOrdinal),
                        out var fieldExportKeys))
                {
                    foreach (var exportKey in fieldExportKeys)
                    {
                        fieldExportRows.Add(new TrRmExportRow(
                            0x1400_0000u | checked((uint)fieldDefRow),
                            AddString(exportKey)));
                    }
                }
            }

            if (!schema.UsesConstructionContracts())
            {
                throw Unsupported(
                    "EN1 class source publication requires its exact Construction successor.");
            }

            var constructorFunctions = program.Functions
                .Select(static (function, functionIndex) =>
                    (Function: function, FunctionIndex: functionIndex))
                .Where(item => item.Function.IsClassConstructor
                    && StringComparer.Ordinal.Equals(
                        item.Function.DeclaringStruct, @class.Name))
                .OrderBy(static item => item.FunctionIndex)
                .ToArray();
            if (constructorFunctions.Length == 0)
            {
                throw new InvalidDataException(
                    "A retained EN1 source class has no checked constructor initializer.");
            }

            foreach (var (function, functionIndex) in constructorFunctions)
            {
                var memberExports
                    = sourceClassConstructorExportsByIndex.TryGetValue(
                        (classIndex, functionIndex), out var mappedExports)
                        ? mappedExports.ToImmutableArray()
                        : ImmutableArray<SourceClassMemberExport>.Empty;
                if (memberExports.Any(static member =>
                        member.Kind != SourceClassMemberKind.Constructor)
                    || (classExportKeys.Count != 0
                        && !function.IsPrivate && !function.IsInternal
                        && memberExports.IsEmpty)
                    || (!schema.UsesObjectConstruction()
                        && memberExports.Any(static member =>
                            member.ConstructionProjectionExportKey
                                is not null)))
                {
                    throw new InvalidDataException(
                        "An EN1 class constructor mapping does not preserve its selected initializer and supported projection keys.");
                }

                var receiverType = function.Variables.IsEmpty
                    ? PrimitiveType.Void : function.Variables[0].Type;
                if (!function.IsClassConstructor || function.IsConstructor
                    || function.IsStatic || function.IsExternal
                    || function.Intrinsic != BoundIntrinsic.None
                    || function.ClassConstructor != -1
                    || function.DeclaringStruct != @class.Name
                    || function.ReturnType != PrimitiveType.Void
                    || function.ParameterCount < 1
                    || function.ParameterCount
                        != function.Variables.Length
                    || function.ParameterCount
                        > SemanticVerifier.MaximumArguments
                    || function.DoesNotReturn || function.NoFailureProof
                    || !function.CapturedParameters.IsEmpty
                    || !function.DeclarationContractClauses.IsEmpty
                    || function.ModuleImport is not null
                    || function.GenericMethodInstantiation is not null
                    || function.IsPartialConstructor
                    || function.HasPureEvaluationForm
                    || function.IsCleanup
                    || !function.OrdinaryParameterDefaults.IsEmpty
                    || function.IsPrivate && function.IsInternal
                    || !ManagedClassTypes.IsClass(receiverType)
                    || ManagedClassTypes.ClassIndex(receiverType)
                        != classIndex
                    || ManagedClassTypes.IsOptional(receiverType)
                    || ManagedClassTypes.IsWritable(receiverType)
                        != @class.IsMutable)
                {
                    throw Unsupported(
                        "The bounded EN1 class constructor path cannot discard source ownership, signature, access, effect or completion facts.");
                }

                var fieldAssignments
                    = GetC1M1Construction2FieldAssignments(program,
                        function, @class, sourceStructDeclarationsByIndex,
                        schema, schemaIdentity);
                var classTypeSpecReference
                    = FieldTypeSpecReference(receiverType);
                sourceClassConstructorPlans.Add(
                    new C1EnumClosureSourceConstructorPlan(
                        classIndex, function, functionIndex,
                        fieldAssignments, classTypeSpecReference,
                        memberExports));
            }

            typeDefRows.Add(new TrRmTypeDefRow(0, 0, typeDefFlags,
                fieldFirst, @class.Fields.Length, 0, 0,
                checked((uint)(layoutRows.Count + 1)), 1, 0, 0));
            typeDefModuleIdentities.Add(moduleIdentity);
            typeDefNames.Add((namespaceName, typeName));
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, 0, 0, 0, 0,
                layoutFieldFirst, @class.Fields.Length));
            foreach (var exportKey in classExportKeys)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    AddString(exportKey)));
            }
        }

        foreach (var emission in standardClassDeclarationsByTypeDef)
        {
            var classView = emission.View;
            var isClassQueryRoot = classQueryRootIdentities.Contains(
                classView.Identity);
            var isSelectedHandleDependency
                = selectedClassHandleTypeIdentities.Contains(
                    classView.Identity);
            var isImportedClassQueryRoot
                = isClassQueryRoot
                    && emission.Import is C1EnumClosureImport typeImport
                    && !typeImport.RootImports.IsDefaultOrEmpty;
            if (standardClassRootTables is null
                || !IsSelectedC1EnumClosureSchemaIdentity(
                    classView.Provider.SchemaIdentity)
                || !StringComparer.Ordinal.Equals(
                    classView.Provider.SchemaIdentity, schemaIdentity)
                || classView.Provider.TargetRequirement
                    != moduleTargetRequirement
                || classView.Provider.RuntimePolicy
                    != (uint)program.RuntimePolicy
                || !standardClassRootTablesByProvider.ContainsKey(
                    classView.Provider.ModuleIdentity)
                || (!allowMultipleClassQueryProviders
                    && !SameA2SourceTypeProviderSnapshot(rootSnapshot!,
                        classView.Provider)
                    && !isSelectedHandleDependency
                    && !isImportedClassQueryRoot)
                || (classQueryFunction is not null
                    && !isClassQueryRoot
                    && !isSelectedHandleDependency)
                || classView.Layouts.Length != 1)
            {
                throw Unsupported(
                    "EN1 class re-emission requires one exact local class declaration with a single default layout.");
            }

            var layout = classView.Layouts[0];
            if (layout.TargetProfileReference != 0
                || layout.Kind != 0 || layout.Flags != 0
                || layout.Alignment != 0 || layout.Pack != 0
                || layout.FieldDefinitionIds.Length
                    != classView.Fields.Length)
            {
                throw Unsupported(
                    "EN1 class re-emission supports the default Auto Layout variant only.");
            }

            var typeDefRow = typeDefRowByRootClassIdentity[
                classView.Identity];
            var fieldFirst = classView.Fields.IsEmpty
                ? 0 : checked(fieldRows.Count + 1);
            var layoutFieldFirst = classView.Fields.IsEmpty
                ? 0 : checked(layoutFieldRows.Count + 1);
            var fieldNamesForClass = new HashSet<string>(
                StringComparer.Ordinal);
            for (var fieldOrdinal = 0;
                fieldOrdinal < classView.Fields.Length; fieldOrdinal++)
            {
                var field = classView.Fields[fieldOrdinal];
                if (field.OwnerDefinitionId
                        != classView.Identity.DefinitionId
                    || (field.DefinitionId >> 24) != 0x14
                    || !fieldNamesForClass.Add(field.Name)
                    || layout.FieldDefinitionIds[fieldOrdinal]
                        != field.DefinitionId)
                {
                    throw Invalid(
                        "An EN1 class FieldDef or Layout no longer matches its retained declaration snapshot.");
                }

                var fieldDefRow = checked(fieldRows.Count + 1);
                fieldRows.Add(new TrRmFieldDefRow(typeDefRow, 0,
                    StandardClassFieldTypeSpecReference(field,
                        classView.Provider),
                    field.Flags));
                var classFieldIdentity
                    = (classView.Identity, field.DefinitionId);
                if (!standardClassFieldRowsByIdentity.TryAdd(
                        classFieldIdentity, fieldDefRow))
                {
                    throw Invalid(
                        "EN1 class re-emission contains a duplicate FieldDef identity.");
                }
                var sourceFieldRow = checked((int)(
                    field.DefinitionId & 0x00FF_FFFF));
                if (!standardClassFieldRowMap.TryAdd(sourceFieldRow,
                        fieldDefRow))
                {
                    throw Invalid(
                        "EN1 class re-emission maps one source FieldDef row more than once.");
                }
                fieldNames.Add(field.Name);
                layoutFieldRows.Add(new TrRmLayoutFieldRow(
                    checked(layoutRows.Count + 1), fieldOrdinal,
                    fieldDefRow));
                if (emission.Import is null)
                {
                    ImmutableArray<string> fieldExportKeys;
                    if (emission.OwnerModuleIdentity is not null)
                    {
                        fieldExportKeys = field.ExportKeys;
                    }
                    else if (!emission.ExportKeys.IsDefaultOrEmpty
                        && standardClassFieldExportKeysByDefinition
                            .TryGetValue(field.DefinitionId,
                                out var rootFieldExportKeys))
                    {
                        fieldExportKeys = rootFieldExportKeys.ToImmutableArray();
                    }
                    else
                    {
                        fieldExportKeys = [];
                    }

                    var moduleExportKeys = GetModuleExportKeys(
                        emission.OwnerModuleIdentity ?? moduleIdentity);
                    foreach (var exportKey in fieldExportKeys)
                    {
                        if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                                exportKey)
                            || !moduleExportKeys.Add(exportKey))
                        {
                            throw Invalid(
                                "An EN1 class FieldDef ExportKey is invalid or duplicated within its Module.");
                        }

                        fieldExportRows.Add(new TrRmExportRow(
                            0x1400_0000u | checked((uint)fieldDefRow),
                            AddString(exportKey)));
                    }
                }

                foreach (var fieldExpectation in
                    emission.FieldExpectations)
                {
                    if (fieldExpectation.ExpectedDefinitionId
                        != field.DefinitionId)
                    {
                        continue;
                    }

                    if (emission.Import is not C1EnumClosureImport typeImport
                        || (typeImport.RootImportRow == 0
                            ? fieldExpectation.ImportRow != 0
                                || fieldExpectation.OwnerImportRow != 0
                                || fieldExpectation.ExpectedDefinitionId
                                    != fieldExpectation.ProviderDefinitionId
                                || fieldExpectation.ExpectedOwnerDefinitionId
                                    != fieldExpectation.ProviderOwnerDefinitionId
                            : fieldExpectation.ImportRow <= 0
                                || fieldExpectation.OwnerImportRow
                                    != typeImport.RootImportRow)
                        || fieldExpectation.ExpectedOwnerDefinitionId
                            != classView.Identity.DefinitionId
                        || !StringComparer.Ordinal.Equals(
                            fieldExpectation.Provider.ModuleIdentity,
                            typeImport.ProducerIdentity)
                        || fieldExpectation.Provider.TargetRequirement
                            != typeImport.TargetRequirement
                        || fieldExpectation.Provider.RuntimePolicy
                            != typeImport.RuntimePolicy)
                    {
                        throw Invalid(
                            "An EN1 class FieldDef Import lost its retained owner or provider identity.");
                    }

                    var fieldProviderKey = (
                        fieldExpectation.Provider.ModuleIdentity,
                        fieldExpectation.Provider.TargetRequirement,
                        fieldExpectation.Provider.RuntimePolicy);
                    if (!moduleRefRowsByProvider.TryGetValue(
                            fieldProviderKey, out var fieldModuleRefRow))
                    {
                        fieldModuleRefRow = moduleRefRows.Count + 1;
                        moduleRefRows.Add(new TrRmModuleRefRow(
                            AddString(fieldExpectation.Provider
                                .ModuleIdentity),
                            fieldExpectation.Provider.TargetRequirement,
                            fieldExpectation.Provider.RuntimePolicy));
                        moduleRefRowsByProvider.Add(fieldProviderKey,
                            fieldModuleRefRow);
                    }

                    var localFieldDefinition = 0x1400_0000u
                        | checked((uint)fieldDefRow);
                    importRows.Add(new TrRmImportRow(fieldModuleRefRow,
                        AddString(fieldExpectation.ExportKey),
                        localFieldDefinition));
                    var fieldImportKey = (localFieldDefinition,
                        fieldExpectation.Provider.ModuleIdentity,
                        fieldExpectation.ExportKey);
                    if (!standardFieldImportRows.TryAdd(fieldImportKey,
                            importRows.Count))
                    {
                        throw Invalid(
                            "An EN1 class query FieldDef Import was emitted more than once.");
                    }
                }
            }

            var importedConstructorMethods
                = new List<(BoundStandardClassMethodDeclaration Method,
                    int MethodDefRow,
                    ImmutableArray<BoundStandardClassMethodExpectation>
                        Expectations)>();
            foreach (var methodGroup in emission.MethodExpectations
                .GroupBy(static expectation =>
                    expectation.ExpectedDefinitionId)
                .OrderBy(static group => group.Key))
            {
                var methodExpectations = methodGroup
                    .OrderBy(static expectation => expectation.ExportKey,
                        StringComparer.Ordinal)
                    .ToImmutableArray();
                var methodExpectation = methodExpectations[0];
                var method = classView.Methods.SingleOrDefault(candidate =>
                    candidate.DefinitionId
                        == methodExpectation.ExpectedDefinitionId);
                if (method is null
                    || !IsSupportedClassConstructorMethod(method)
                    || emission.Import
                        is not C1EnumClosureImport classTypeImport
                    || classTypeImport.RootImportRow <= 0
                    || methodExpectation.OwnerImportRow
                        != classTypeImport.RootImportRow
                    || methodExpectation.ExpectedOwnerDefinitionId
                        != classView.Identity.DefinitionId
                    || methodExpectation.ExpectedDefinitionId
                        != method.DefinitionId
                    || (methodExpectation.ProviderDefinitionId >> 24)
                        != 0x16
                    || (methodExpectation.ProviderDefinitionId
                        & 0x00FF_FFFF) == 0
                    || (methodExpectation.ProviderOwnerDefinitionId >> 24)
                        != 0x13
                    || (methodExpectation.ProviderOwnerDefinitionId
                        & 0x00FF_FFFF) == 0)
                {
                    throw Unsupported(
                        "An EN1 imported class constructor expectation does not retain one exact owner and Construction declaration.");
                }

                if (methodExpectations.Any(expectation =>
                        expectation.ExpectedOwnerDefinitionId
                            != methodExpectation.ExpectedOwnerDefinitionId
                        || expectation.ProviderDefinitionId
                            != methodExpectation.ProviderDefinitionId
                        || expectation.ProviderOwnerDefinitionId
                            != methodExpectation.ProviderOwnerDefinitionId
                        || expectation.OwnerImportRow
                            != methodExpectation.OwnerImportRow
                        || !StringComparer.Ordinal.Equals(
                            expectation.Provider.ModuleIdentity,
                            methodExpectation.Provider.ModuleIdentity)
                        || expectation.Provider.TargetRequirement
                            != methodExpectation.Provider.TargetRequirement
                        || expectation.Provider.RuntimePolicy
                            != methodExpectation.Provider.RuntimePolicy
                        || !expectation.Provider.ProviderArtifactDigest
                            .AsSpan().SequenceEqual(
                                methodExpectation.Provider
                                    .ProviderArtifactDigest.AsSpan()))
                    || methodExpectations.Any(expectation =>
                        !BoundEnumTypes.IsValidModuleDeclarationKey(
                            expectation.ExportKey))
                    || methodExpectations.Select(
                            static expectation => expectation.ExportKey)
                        .Distinct(StringComparer.Ordinal).Count()
                        != methodExpectations.Length
                    || (methodExpectation.ProviderDefinitionId >> 24)
                        != 0x16
                    || (methodExpectation.ProviderOwnerDefinitionId
                        >> 24) != 0x13
                    || !StringComparer.Ordinal.Equals(
                        methodExpectation.Provider.ModuleIdentity,
                        classTypeImport.ProducerIdentity)
                    || methodExpectation.Provider.TargetRequirement
                        != classTypeImport.TargetRequirement
                    || methodExpectation.Provider.RuntimePolicy
                        != classTypeImport.RuntimePolicy)
                {
                    throw Invalid(
                        "An EN1 imported class constructor alias changed its selected provider origin.");
                }

                var methodDefRow = checked(methodRows.Count + 1);
                var signatureRow = checked(
                    methodSignatureRows.Count + 1);
                var parameterFirst = method.Parameters.IsEmpty
                    ? 0 : checked(parameterRows.Count + 1);
                var returnTypeSpec = RemapStandardClassTypeSpecReference(
                    method.ReturnTypeSpecReference, 0);
                var receiverTypeSpec = method.ReceiverTypeSpecReference == 0
                    ? 0
                    : RemapStandardClassTypeSpecReference(
                        method.ReceiverTypeSpecReference, 0);
                for (var ordinal = 0;
                    ordinal < method.Parameters.Length; ordinal++)
                {
                    var parameter = method.Parameters[ordinal];
                    if (parameter.Mode != 0 || parameter.Flags != 0
                        || parameter.DefaultValue is not null)
                    {
                        throw Unsupported(
                            "EN1 imported constructor MethodDefs require ordinary parameters without defaults.");
                    }

                    parameterRows.Add(new TrRmParamRow(signatureRow,
                        ordinal,
                        RemapStandardClassTypeSpecReference(
                            parameter.TypeSpecReference, 0),
                        parameter.Mode, parameter.Flags));
                    if (schema.UsesMethodParameterLabels())
                    {
                        methodParameterLabelRows.Add((
                            methodDefRow, ordinal, parameter.Name));
                        _ = AddString(parameter.Name);
                    }
                }

                methodSignatureRows.Add(new TrRmMethodSigRow(
                    returnTypeSpec, receiverTypeSpec, parameterFirst,
                    method.Parameters.Length, 0, 0));
                methodRows.Add(new TrRmMethodDefRow(typeDefRow,
                    AddString(method.Name), signatureRow, method.Flags,
                    0, 0, 0));
                if (!standardClassMethodRowsByIdentity.TryAdd((
                        classView.Identity, method.DefinitionId),
                        methodDefRow))
                {
                    throw Invalid(
                        "An EN1 imported class MethodDef identity was emitted more than once.");
                }
                var expectedMethodRow = checked((int)(
                    method.DefinitionId & 0x00FF_FFFF));
                if (!standardClassMethodDefRowMap.TryAdd(
                        expectedMethodRow, methodDefRow))
                {
                    throw Invalid(
                        "An EN1 imported class MethodDef row was emitted more than once.");
                }

                importedConstructorMethods.Add((method, methodDefRow,
                    methodExpectations));
            }
            hasImportedClassConstructionMethods
                |= importedConstructorMethods.Count != 0;

            foreach (var (method, methodDefRow, methodExpectations)
                in importedConstructorMethods)
            {
                var constructionPayload
                    = EncodeEN1ImportedClassConstructionClause(method,
                        classView.Identity);
                var contractFirst = contractRows.Count + 1;
                var contractCount = 0;
                if (method.EffectsMask != 0)
                {
                    contractRows.Add(new TrRmContractRow(
                        0x1600_0000u | checked((uint)methodDefRow), 1,
                        s_effectPayloads[method.EffectsMask]));
                    contractCount++;
                }

                if (contractFirst == 0)
                {
                    contractFirst = contractRows.Count + 1;
                }
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u | checked((uint)methodDefRow),
                    0x0003, constructionPayload));
                contractCount++;
                methodRows[methodDefRow - 1] = methodRows[
                    methodDefRow - 1] with {
                    ContractFirst = contractFirst,
                    ContractCount = contractCount,
                };

                foreach (var expectation in methodExpectations)
                {
                    var providerKey = (
                        expectation.Provider.ModuleIdentity,
                        expectation.Provider.TargetRequirement,
                        expectation.Provider.RuntimePolicy);
                    if (!moduleRefRowsByProvider.TryGetValue(providerKey,
                            out var moduleRefRow))
                    {
                        moduleRefRow = moduleRefRows.Count + 1;
                        moduleRefRows.Add(new TrRmModuleRefRow(
                            AddString(expectation.Provider.ModuleIdentity),
                            expectation.Provider.TargetRequirement,
                            expectation.Provider.RuntimePolicy));
                        moduleRefRowsByProvider.Add(providerKey,
                            moduleRefRow);
                    }

                    importRows.Add(new TrRmImportRow(moduleRefRow,
                        AddString(expectation.ExportKey),
                        0x1600_0000u | checked((uint)methodDefRow)));
                }
            }

            if (emission.Import is null)
            {
                if (standardClassRootTables is null
                    || classView.Methods.Any(static method =>
                        method.BodyRow == 0))
                {
                    throw Unsupported(
                        "EN1 source-free local class methods require their retained provider Bodies.");
                }

                var localMethods
                    = new List<(BoundStandardClassMethodDeclaration Method,
                        int SourceMethodDefRow, int MethodDefRow)>();
                foreach (var method in classView.Methods.OrderBy(
                    static method => method.DefinitionId))
                {
                    if (!IsSupportedClassConstructorMethod(method)
                        || (method.DefinitionId >> 24) != 0x16
                        || (method.DefinitionId & 0x00FF_FFFF) == 0)
                    {
                        throw Unsupported(
                            "EN1 source-free local class Methods are limited to the supported I/P construction declarations.");
                    }

                    var sourceMethodRow = checked((int)(
                        method.DefinitionId & 0x00FF_FFFF));
                    if (standardClassRootTables.Methods.GetOwnerType(
                            sourceMethodRow)
                            != (int)(classView.Identity.DefinitionId
                                & 0x00FF_FFFF)
                        || standardClassRootTables.Methods.GetBodyRow(
                            sourceMethodRow) != method.BodyRow
                        || standardClassRootTables.Methods.GetFlags(
                            sourceMethodRow) != method.Flags)
                    {
                        throw Invalid(
                            "An EN1 source-free local class MethodDef changed its retained owner, Body or access flags.");
                    }

                    var methodDefRow = checked(methodRows.Count + 1);
                    var signatureRow = checked(
                        methodSignatureRows.Count + 1);
                    var parameterFirst = method.Parameters.IsEmpty
                        ? 0 : checked(parameterRows.Count + 1);
                    var resultTypeSpec = RemapStandardClassTypeSpecReference(
                        method.ReturnTypeSpecReference, 0);
                    var receiverTypeSpec
                        = method.ReceiverTypeSpecReference == 0 ? 0
                        : RemapStandardClassTypeSpecReference(
                            method.ReceiverTypeSpecReference, 0);
                    for (var ordinal = 0;
                        ordinal < method.Parameters.Length; ordinal++)
                    {
                        var parameter = method.Parameters[ordinal];
                        if (parameter.Mode != 0 || parameter.Flags != 0
                            || parameter.DefaultValue is not null)
                        {
                            throw Unsupported(
                                "EN1 source-free local constructor MethodDefs require ordinary parameters without defaults.");
                        }

                        parameterRows.Add(new TrRmParamRow(signatureRow,
                            ordinal,
                            RemapStandardClassTypeSpecReference(
                                parameter.TypeSpecReference, 0),
                            parameter.Mode, parameter.Flags));
                        if (schema.UsesMethodParameterLabels())
                        {
                            methodParameterLabelRows.Add((
                                methodDefRow, ordinal, parameter.Name));
                            _ = AddString(parameter.Name);
                        }
                    }

                    methodSignatureRows.Add(new TrRmMethodSigRow(
                        resultTypeSpec, receiverTypeSpec, parameterFirst,
                        method.Parameters.Length, 0, 0));
                    methodRows.Add(new TrRmMethodDefRow(typeDefRow,
                        AddString(method.Name), signatureRow, method.Flags,
                        0, 0, 0));
                    if (!standardClassMethodRowsByIdentity.TryAdd((
                            classView.Identity, method.DefinitionId),
                            methodDefRow)
                        || !standardClassMethodDefRowMap.TryAdd(
                            sourceMethodRow, methodDefRow))
                    {
                        throw Invalid(
                            "EN1 source-free local class MethodDef rows are duplicated.");
                    }

                    if ((method.Flags & 3) == 2
                        && method.ExportKeys.IsEmpty)
                    {
                        throw Invalid(
                            "An EN1 source-free public class MethodDef has no retained ExportKey.");
                    }

                    foreach (var exportKey in method.ExportKeys)
                    {
                        if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                                exportKey)
                            || !usedExportKeys.Add(exportKey))
                        {
                            throw Invalid(
                                "An EN1 source-free class MethodDef ExportKey is invalid or duplicated.");
                        }

                        exportRows.Add(new TrRmExportRow(
                            0x1600_0000u
                                | checked((uint)methodDefRow),
                            AddString(exportKey)));
                    }

                    localMethods.Add((method, sourceMethodRow,
                        methodDefRow));
                    retainedClassMethodBodyPlans.Add(
                        new C1EnumClosureRetainedClassMethodBodyPlan(
                            classView, method, sourceMethodRow,
                            methodDefRow));
                }

                foreach (var (method, _, methodDefRow) in localMethods)
                {
                    var constructionPayload
                        = EncodeEN1ImportedClassConstructionClause(method,
                            classView.Identity,
                            standardClassFieldRowsByIdentity,
                            standardClassMethodRowsByIdentity);
                    var contractFirst = contractRows.Count + 1;
                    var contractCount = 0;
                    if (method.EffectsMask != 0)
                    {
                        contractRows.Add(new TrRmContractRow(
                            0x1600_0000u
                                | checked((uint)methodDefRow),
                            1, s_effectPayloads[method.EffectsMask]));
                        contractCount++;
                    }

                    if (contractFirst == 0)
                    {
                        contractFirst = contractRows.Count + 1;
                    }
                    contractRows.Add(new TrRmContractRow(
                        0x1600_0000u
                            | checked((uint)methodDefRow),
                        0x0003, constructionPayload));
                    contractCount++;
                    methodRows[methodDefRow - 1] = methodRows[
                        methodDefRow - 1] with {
                        ContractFirst = contractFirst,
                        ContractCount = contractCount,
                    };
                }

                hasLocalClassConstructionBodies
                    |= localMethods.Count != 0;
            }
            else if (emission.Import is {
                    RootImportRow: > 0,
                } && classView.Methods.Any(static method =>
                    method.BodyRow != 0))
            {
                throw Invalid(
                    "An EN1 imported class method expectation contains a provider Body.");
            }

            var layoutFirst = checked(layoutRows.Count + 1);
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, layout.Kind,
                layout.Flags, layout.Alignment, layout.Pack,
                layoutFieldFirst, classView.Fields.Length));
            typeDefRows.Add(new TrRmTypeDefRow(0, 0,
                classView.TypeDefFlags, fieldFirst,
                classView.Fields.Length, 0, 0,
                checked((uint)layoutFirst), 1, 0, 0));
            typeDefModuleIdentities.Add(
                emission.OwnerModuleIdentity ?? moduleIdentity);
            typeDefNames.Add((classView.Namespace, classView.Name));
            var typeModuleExportKeys = GetModuleExportKeys(
                emission.OwnerModuleIdentity ?? moduleIdentity);
            foreach (var exportKey in emission.ExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !typeModuleExportKeys.Add(exportKey))
                {
                    throw Invalid(
                        "An EN1 class TypeDef ExportKey is invalid or duplicated within its Module.");
                }

                exportRows.Add(new TrRmExportRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    AddString(exportKey)));
            }
        }

        foreach (var structureIndex in retainedStructureIndexes)
        {
            var structure = program.Structures[structureIndex];
            string namespaceName;
            string typeName;
            if (sourceStructDeclarationsByIndex.TryGetValue(structureIndex,
                    out var declaration))
            {
                namespaceName = declaration.Namespace;
                typeName = declaration.Name;
            }
            else
            {
                var separator = structure.Name.LastIndexOf(
                    '.', StringComparison.Ordinal);
                namespaceName = separator < 0 ? "" : structure.Name[..separator];
                typeName = separator < 0 ? structure.Name
                    : structure.Name[(separator + 1)..];
            }

            var typeDefRow = typeDefRowByStructure[structureIndex];
            var typeDefFlags = (structure.IsPrivate ? 0u
                : structure.IsInternal ? 1u : 2u) | 0x04u;
            if (schema.UsesConstructionInitializerCopyEvidence())
            {
                var declaresCopy = structure.StandardTypeOrigin is not null
                    ? structure.DeclaredCopyEvidence == true
                    : IsC1M1Construction2SourceCopyType(
                        (PrimitiveType)(100 + structureIndex), program,
                        schema, schemaIdentity,
                        sourceStructDeclarationsByIndex);
                if (declaresCopy)
                {
                    typeDefFlags
                        |= TrRmTypeDefTableReader.DeclaredCopyFlag;
                }
            }

            var fieldFirst = structure.Fields.IsEmpty
                ? 0 : fieldRows.Count + 1;
            var layoutFieldFirst = structure.Fields.IsEmpty
                ? 0 : layoutFieldRows.Count + 1;
            var fieldNamesForType = new HashSet<string>(
                StringComparer.Ordinal);
            for (var fieldOrdinal = 0;
                fieldOrdinal < structure.Fields.Length; fieldOrdinal++)
            {
                var field = structure.Fields[fieldOrdinal];
                if (field is null || (field.IsPrivate && field.IsInternal)
                    || field.RequiresUnsafeCaller
                    || field.BackingPropertyName is not null
                    || !fieldNamesForType.Add(field.Name))
                {
                    throw Unsupported(
                        "EN1 source struct FieldDefs cannot discard conflicting access, unsafe-caller or backing-property facts.");
                }

                var visibility = field.IsPrivate ? 0u
                    : field.IsInternal ? 1u : 2u;
                var fieldFlags = visibility
                    | (field.IsMutable ? 0x04u : 0u);
                var fieldDefRow = fieldRows.Count + 1;
                fieldRows.Add(new TrRmFieldDefRow(typeDefRow, 0,
                    FieldTypeSpecReference(field.Type), fieldFlags));
                fieldNames.Add(field.Name);
                layoutFieldRows.Add(new TrRmLayoutFieldRow(
                    layoutRows.Count + 1, fieldOrdinal, fieldDefRow));

                if (sourceStructFieldExportsByIndex.TryGetValue(
                        (structureIndex, fieldOrdinal),
                        out var fieldExportKeys))
                {
                    foreach (var exportKey in fieldExportKeys)
                    {
                        fieldExportRows.Add(new TrRmExportRow(
                            0x1400_0000u | checked((uint)fieldDefRow),
                            AddString(exportKey)));
                    }
                }

                if (sourceStructFieldImportsByIndex.TryGetValue(
                        (structureIndex, fieldOrdinal),
                        out var fieldImports))
                {
                    if (sourceStructFieldExportsByIndex.ContainsKey(
                            (structureIndex, fieldOrdinal))
                        || !sourceStructImportsByIndex.TryGetValue(
                            structureIndex, out var ownerImports)
                        || ownerImports.Count != 1
                        || fieldImports.Any(fieldImport =>
                            !ownerImports.Any(ownerImport =>
                                StringComparer.Ordinal.Equals(
                                    ownerImport.ProducerIdentity,
                                    fieldImport.ProducerIdentity)
                                && ownerImport.TargetRequirement
                                    == fieldImport.TargetRequirement
                                && ownerImport.RuntimePolicy
                                    == fieldImport.RuntimePolicy)))
                    {
                        throw Invalid(
                            "An EN1 FieldDef Import does not retain one matching owner TypeDef Import.");
                    }

                    foreach (var fieldImport in fieldImports)
                    {
                        var providerKey = (fieldImport.ProducerIdentity,
                            fieldImport.TargetRequirement,
                            fieldImport.RuntimePolicy);
                        if (!moduleRefRowsByProvider.TryGetValue(
                                providerKey, out var moduleRefRow))
                        {
                            moduleRefRow = moduleRefRows.Count + 1;
                            moduleRefRows.Add(new TrRmModuleRefRow(
                                AddString(fieldImport.ProducerIdentity),
                                fieldImport.TargetRequirement,
                                fieldImport.RuntimePolicy));
                            moduleRefRowsByProvider.Add(providerKey,
                                moduleRefRow);
                        }

                        var localFieldDefinition = 0x1400_0000u
                            | checked((uint)fieldDefRow);
                        var fieldImportKey = (localFieldDefinition,
                            fieldImport.ProducerIdentity,
                            fieldImport.ExportKey);
                        if (standardFieldImportRows.TryGetValue(
                                fieldImportKey, out var existingImportRow))
                        {
                            var existingImport
                                = importRows[existingImportRow - 1];
                            var existingModule
                                = moduleRefRows[
                                    checked((int)existingImport.ModuleRefRow)
                                        - 1];
                            if (!StringComparer.Ordinal.Equals(
                                    existingImport.ExportKey,
                                    fieldImport.ExportKey)
                                || existingModule.TargetRequirement
                                    != fieldImport.TargetRequirement
                                || existingModule.RuntimePolicy
                                    != fieldImport.RuntimePolicy)
                            {
                                throw Invalid(
                                    "Repeated EN1 FieldDef provider edges disagree on their import contract.");
                            }

                            continue;
                        }

                        importRows.Add(new TrRmImportRow(moduleRefRow,
                            AddString(fieldImport.ExportKey),
                            localFieldDefinition));
                        standardFieldImportRows.Add(fieldImportKey,
                            importRows.Count);
                    }
                }
            }

            var layoutRow = layoutRows.Count + 1;
            typeDefRows.Add(new TrRmTypeDefRow(0, 0, typeDefFlags,
                fieldFirst, structure.Fields.Length, 0, 0,
                checked((uint)layoutRow), 1, 0, 0));
            typeDefModuleIdentities.Add(moduleIdentity);
            typeDefNames.Add((namespaceName, typeName));
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, 0, 0, 0, 0,
                layoutFieldFirst, structure.Fields.Length));

            if (sourceStructExportsByIndex.TryGetValue(structureIndex,
                    out var exportKeys))
            {
                foreach (var exportKey in exportKeys)
                {
                    exportRows.Add(new TrRmExportRow(
                        0x1300_0000u | checked((uint)typeDefRow),
                        AddString(exportKey)));
                }
            }
        }

        foreach (var plan in retainedClassMethodBodyPlans)
        {
            if (standardClassRootTables is null
                || !SameA2SourceTypeProviderSnapshot(rootSnapshot!,
                    plan.Owner.Provider))
            {
                throw Invalid(
                    "An EN1 local constructor Body lost its exact root provider snapshot.");
            }

            var fieldRowMap = new Dictionary<int, int>(
                plan.Owner.Fields.Length);
            foreach (var field in plan.Owner.Fields)
            {
                if (!standardClassFieldRowsByIdentity.TryGetValue(
                        (plan.Owner.Identity, field.DefinitionId),
                        out var remappedFieldRow)
                    || !fieldRowMap.TryAdd(checked((int)(
                            field.DefinitionId & 0x00FF_FFFF)),
                            remappedFieldRow))
                {
                    throw Invalid(
                        "An EN1 local constructor Body has an unmapped FieldDef.");
                }
            }

            var sourceMethodRow = plan.SourceMethodDefRow;
            if (standardClassRootTables.Methods.GetBodyRow(sourceMethodRow)
                    != plan.Method.BodyRow
                || standardClassRootTables.Methods.GetOwnerType(
                    sourceMethodRow)
                    != (int)(plan.Owner.Identity.DefinitionId
                        & 0x00FF_FFFF))
            {
                throw Invalid(
                    "An EN1 local constructor Body changed its source MethodDef owner or Body row.");
            }

            var sourceBody = standardClassRootTables.Bodies.GetRow(
                checked((int)plan.Method.BodyRow)).ToArray();
            var remappedBody = RemapC1M1ClassMethodBody(
                standardClassRootTables, sourceBody,
                new Dictionary<int, int>(),
                standardClassMethodDefRowMap, fieldRowMap,
                new Dictionary<int, int>(),
                new Dictionary<int, int>(), fieldPathRows,
                new Dictionary<C1M1FieldPathIdentity, int>(),
                new Dictionary<int, int>(), valueBuilder,
                typeSpecRows, new Dictionary<int, uint>(), [],
                schema, new Dictionary<int, int>(),
                typeSpecReference
                    => RemapStandardClassTypeSpecReference(
                        typeSpecReference, 0),
                RemapConstructorConstantSource);
            bodyRows.Add(remappedBody);
            methodRows[plan.MethodDefRow - 1]
                = methodRows[plan.MethodDefRow - 1] with {
                    BodyRow = bodyRows.Count,
                };
        }

        TrRmFrozenScalarValueReference RemapConstructorConstantSource(
            int constantSourceRow, uint sourceTypeSpecReference)
        {
            if (standardClassRootTables is null || rootSnapshot is null)
            {
                throw Invalid(
                    "An EN1 constructor constant has no root value context.");
            }

            var constant = TrRmConstantSourceValueValidator
                .MaterializeFrozenScalar(constantSourceRow,
                    sourceTypeSpecReference, rootSnapshot.ModuleRow,
                    rootSnapshot.TargetRequirement,
                    standardClassRootTables.ConstantSources,
                    standardClassRootTables.FrozenValueNodes,
                    standardClassRootTables.Blobs,
                    standardClassRootTables.TypeSpecs,
                    standardClassRootTables.Features,
                    standardClassRootTables.Modules,
                    standardClassRootTables.TypeDefs,
                    standardClassRootTables.EnumDefs,
                    standardClassRootTables.Imports,
                    standardClassRootTables.A3LayoutResourceLedger,
                    standardClassRootTables.Fields);
            var remappedTypeSpec
                = RemapStandardClassTypeSpecReference(
                    sourceTypeSpecReference, 0);
            var sourceType = standardClassRootTables.TypeSpecs
                .GetTypeSpecRef(sourceTypeSpecReference);
            if (!sourceType.IsRow && sourceType.Form == 0x01
                && sourceType.Qualifiers == 0
                && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                    sourceType.TypeCode, out _))
            {
                var frozenValue = valueBuilder.Add(constant);
                if (frozenValue.TypeSpecRef != remappedTypeSpec)
                {
                    throw Invalid(
                        "An EN1 constructor scalar ConstantMap changed its exact TypeSpec.");
                }

                return frozenValue;
            }

            var enumIndex = -1;
            if (sourceType.IsRow && sourceType.Form == 0x02
                && enumIndexByRootTypeDefRow.TryGetValue(
                    (rootSnapshot.ModuleIdentity,
                        checked((int)sourceType.Row)), out enumIndex))
            {
            }
            else if (sourceType.IsRow && sourceType.Form == 0x03
                && enumIndexByRootEnumImport.TryGetValue(
                    (rootSnapshot.ModuleIdentity,
                        checked((int)sourceType.Row)), out enumIndex))
            {
            }
            else
            {
                throw Unsupported(
                    "An EN1 constructor ConstantMap requires a selected scalar or nominal enum value.");
            }

            if ((uint)enumIndex >= (uint)program.EnumDefinitions.Length)
            {
                throw Invalid(
                    "An EN1 constructor ConstantMap lost its nominal enum identity.");
            }

            var underlyingType
                = program.EnumDefinitions[enumIndex].UnderlyingType;
            var enumConstant = new BoundConstant(constant.Bits,
                BoundEnumTypes.Type(enumIndex), underlyingType, default);
            return valueBuilder.AddC1EnumClosureMember(enumConstant,
                remappedTypeSpec, underlyingType, schema);
        }

        exportRows.AddRange(fieldExportRows);

        if (usedFunctionPointerSignatureIndexes.Count != 0)
        {
            var anchorNames = program.Functions.Select(
                    static function => function.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var method in methodRows)
            {
                _ = anchorNames.Add(
                    stringRows[method.NameStringRow - 1].Text);
            }
            var anchorNameOrdinal = 1;
            foreach (var signatureIndex
                in usedFunctionPointerSignatureIndexes)
            {
                var methodRow = checked(methodRows.Count + 1);
                if (methodRow
                    != nativeFunctionPointerAnchorMethodRows[
                        signatureIndex])
                {
                    throw new InvalidDataException(
                        "The EN1 native function-pointer anchor order changed during emission.");
                }

                var signature
                    = program.NativeFunctionPointerSignatures[
                        signatureIndex];
                var methodSignatureRow
                    = checked(methodSignatureRows.Count + 1);
                var parameterFirst = signature.ParameterTypes.IsEmpty
                    ? 0 : checked(parameterRows.Count + 1);
                for (var parameterIndex = 0;
                    parameterIndex < signature.ParameterTypes.Length;
                    parameterIndex++)
                {
                    parameterRows.Add(new TrRmParamRow(
                        methodSignatureRow, parameterIndex,
                        FieldTypeSpecReference(
                            signature.ParameterTypes[parameterIndex]), 0));
                }

                var returnTypeSpecReference
                    = signature.ReturnType == PrimitiveType.Void
                    ? 0xF200_0000u
                    : FieldTypeSpecReference(signature.ReturnType);
                methodSignatureRows.Add(new TrRmMethodSigRow(
                    returnTypeSpecReference, 0, parameterFirst,
                    signature.ParameterTypes.Length, 0, 0));

                string anchorName;
                do
                {
                    anchorName
                        = $"__trrm_fnptr_anchor_{anchorNameOrdinal++}";
                }
                while (!anchorNames.Add(anchorName));

                methodRows.Add(new TrRmMethodDefRow(0,
                    AddString(anchorName), methodSignatureRow, 0, 0, 0, 0));
            }
        }

        foreach (var constructorPlan in sourceClassConstructorPlans)
        {
            var classIndex = constructorPlan.ClassIndex;
            var function = constructorPlan.Function;
            var initializerMethodDefRow = methodRows.Count + 1;
            var initializerSignatureRow
                = methodSignatureRows.Count + 1;
            var initializerParamCount = function.ParameterCount - 1;
            var initializerParamFirst = initializerParamCount == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < initializerParamCount; ordinal++)
            {
                var parameter = function.Variables[ordinal + 1];
                if (parameter.IsMutable || parameter.IsOut
                    || parameter.IsInit || parameter.IsImplicitRef
                    || parameter.IsInitializationLocal)
                {
                    throw Unsupported(
                        "The bounded EN1 class initializer supports ordinary immutable value parameters.");
                }

                parameterRows.Add(new TrRmParamRow(
                    initializerSignatureRow, ordinal,
                    FieldTypeSpecReference(parameter.Type), 0, 0));
                if (schema.UsesMethodParameterLabels())
                {
                    methodParameterLabelRows.Add((
                        initializerMethodDefRow, ordinal, parameter.Name));
                    _ = AddString(parameter.Name);
                }
            }

            methodSignatureRows.Add(new TrRmMethodSigRow(
                0xF200_0000u,
                constructorPlan.ClassTypeSpecReference,
                initializerParamFirst, initializerParamCount, 0, 0));

            var effectsMask
                = (function.DoesNotReturn ? 2u : 0u)
                    | (function.RequiresUnsafeCaller ? 4u : 0u);
            if (function.DoesNotReturn
                || function.DeclaredRequiresUnsafeCaller
                    != function.RequiresUnsafeCaller
                || function.NoFailureProof
                || function.HasPureEvaluationForm
                || function.HasOrdinaryParameterDefaultSurface
                    && !function.OrdinaryParameterDefaults.IsEmpty
                || !function.DependencyContracts.IsEmpty
                || !function.DeclarationContractClauses.IsEmpty)
            {
                throw Unsupported(
                    "The bounded EN1 constructor initializer cannot discard its completion, dependency, graph or default obligations.");
            }

            var initializerContractFirst = 0;
            var initializerContractCount = 0;
            if (effectsMask != 0)
            {
                initializerContractFirst = contractRows.Count + 1;
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u
                        | checked((uint)initializerMethodDefRow),
                    1, s_effectPayloads[effectsMask]));
                initializerContractCount++;
            }

            var constructionPayload
                = EncodeC1M1Construction2InitializerContract(
                    constructorPlan.FieldAssignments,
                    fieldDefRowByClassAndIndex, classIndex);
            if (initializerContractFirst == 0)
            {
                initializerContractFirst = contractRows.Count + 1;
            }
            contractRows.Add(new TrRmContractRow(
                0x1600_0000u
                    | checked((uint)initializerMethodDefRow),
                0x0003, constructionPayload));
            initializerContractCount++;

            var visibility = function.IsPrivate ? 0u
                : function.IsInternal ? 1u : 2u;
            var initializer = new C1M1Construction2SourceInitializer(
                classIndex, function,
                constructorPlan.ClassTypeSpecReference,
                constructorPlan.FieldAssignments,
                initializerMethodDefRow, 0);
            var initializerBody = EncodeC1M1Construction2InitializerBody(
                program, initializer, fieldDefRowByClassAndIndex,
                FieldTypeSpecReference, valueBuilder, contractRows.Count,
                schema);
            var initializerBodyRow = bodyRows.Count + 1;
            bodyRows.Add(initializerBody);
            methodRows.Add(new TrRmMethodDefRow(
                typeDefRowByClass[classIndex],
                AddString(function.Name), initializerSignatureRow,
                visibility, initializerContractFirst,
                initializerContractCount, initializerBodyRow));

            foreach (var memberExport in constructorPlan.MemberExports)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1600_0000u
                        | checked((uint)initializerMethodDefRow),
                    AddString(memberExport.ExportKey)));
            }

            var projectionExportKeys = constructorPlan.MemberExports
                .Where(static member =>
                    member.ConstructionProjectionExportKey is not null)
                .Select(static member =>
                    member.ConstructionProjectionExportKey!)
                .ToArray();
            if (!schema.UsesObjectConstruction())
            {
                continue;
            }

            if (constructorPlan.FieldAssignments.Length
                != program.Classes[classIndex].Fields.Length)
            {
                if (projectionExportKeys.Length != 0)
                {
                    throw Unsupported(
                        "A Construction-2 projection ExportKey requires a constructor that initializes every class field.");
                }

                continue;
            }

            if (!function.DependencyContracts.IsEmpty)
            {
                if (projectionExportKeys.Length != 0)
                {
                    throw Unsupported(
                        "A Construction-2 projection ExportKey requires a checked result-dependency mapping for its initializer.");
                }

                continue;
            }

            var projectionMethodDefRow = methodRows.Count + 1;
            var projectionSignatureRow = methodSignatureRows.Count + 1;
            var projectionParamFirst = initializerParamCount == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < initializerParamCount; ordinal++)
            {
                var parameter = function.Variables[ordinal + 1];
                var parameterTypeSpecReference
                    = FieldTypeSpecReference(parameter.Type);
                parameterRows.Add(new TrRmParamRow(projectionSignatureRow,
                    ordinal, parameterTypeSpecReference, 0, 0));
                if (schema.UsesMethodParameterLabels())
                {
                    methodParameterLabelRows.Add((
                        projectionMethodDefRow, ordinal, parameter.Name));
                }
            }

            methodSignatureRows.Add(new TrRmMethodSigRow(
                constructorPlan.ClassTypeSpecReference, 0,
                projectionParamFirst, initializerParamCount, 0, 0));
            var projectionContractFirst = contractRows.Count + 1;
            var projectionContractCount = 0;
            if (effectsMask != 0)
            {
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u
                        | checked((uint)projectionMethodDefRow),
                    1, s_effectPayloads[effectsMask]));
                projectionContractCount++;
            }

            if (projectionContractCount == 0)
            {
                projectionContractFirst = contractRows.Count + 1;
            }
            contractRows.Add(new TrRmContractRow(
                0x1600_0000u | checked((uint)projectionMethodDefRow),
                0x0003,
                EncodeC1M1Construction2ProjectionContract(
                    initializerMethodDefRow)));
            projectionContractCount++;
            var projectionVisibility = projectionExportKeys.Length == 0
                ? 0u : 2u;
            var projection = new C1M1Construction2SourceInitializer(
                classIndex, function,
                constructorPlan.ClassTypeSpecReference,
                constructorPlan.FieldAssignments,
                initializerMethodDefRow, projectionMethodDefRow);
            var projectionBody = EncodeC1M1Construction2ProjectionBody(
                projection, FieldTypeSpecReference, contractRows.Count,
                schema);
            var projectionBodyRow = bodyRows.Count + 1;
            bodyRows.Add(projectionBody);
            methodRows.Add(new TrRmMethodDefRow(
                typeDefRowByClass[classIndex],
                AddString(function.Name), projectionSignatureRow,
                projectionVisibility, projectionContractFirst,
                projectionContractCount,
                projectionBodyRow));
            foreach (var exportKey in projectionExportKeys)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1600_0000u | checked((uint)projectionMethodDefRow),
                    AddString(exportKey)));
            }
        }

        if (classQueryFunction is not null)
        {
            var classFieldPaths
                = GetEN1ClassLayoutQueries(classQueryFunction)
                    .Select(static query => query.Subject.FieldPath!)
                    .ToArray();
            if (rootSnapshot is null || standardClassRootTables is null)
            {
                throw Unsupported(
                    "An EN1 class query Body has no selected class provider snapshot.");
            }

            var queryProviderModules = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (var fieldPath in classFieldPaths)
            {
                var instanceIndex
                    = BoundGenericNominalTypeTypes.Index(
                        fieldPath.RootType);
                var instance = program.GenericNominalTypeInstances[
                    instanceIndex];
                var identity = instance.Definition;
                if (!standardClassProviderByIdentity.TryGetValue(identity,
                        out var provider)
                    || !standardClassRootTablesByProvider.TryGetValue(
                        provider.ModuleIdentity, out var providerTables))
                {
                    throw Unsupported(
                        "An EN1 class query Body root has no exact provider snapshot.");
                }

                _ = instance.TypeSpecReference == 0
                    ? checked((int)(identity.DefinitionId
                        & 0x00FF_FFFF))
                    : GetEN1ClassQueryRootTypeDefRow(instance, provider,
                        providerTables);
                if (!typeDefRowByRootClassIdentity.ContainsKey(identity)
                    || (!StringComparer.Ordinal.Equals(
                            provider.ModuleIdentity, moduleIdentity)
                        && !importRowByRootClassIdentity.ContainsKey(
                            identity)))
                {
                    throw Unsupported(
                        "An EN1 class query Body has no emitted class TypeDef or exact provider Import.");
                }

                _ = queryProviderModules.Add(provider.ModuleIdentity);
            }

            var hasLocalClassQueryProvider
                = queryProviderModules.Contains(moduleIdentity);
            var hasForeignClassQueryProvider
                = queryProviderModules.Any(provider =>
                    !StringComparer.Ordinal.Equals(provider,
                        moduleIdentity));
            if (hasLocalClassQueryProvider
                && hasForeignClassQueryProvider)
            {
                throw Unsupported(
                    "One EN1 class query Body cannot mix local and imported class layout roots.");
            }

            var classQueryRootTypeSpecRows
                = new Dictionary<(BoundGenericNominalTypeIdentity Identity,
                    uint TypeSpecReference), uint>();
            uint GetClassQueryRootTypeSpecReference(PrimitiveType type)
            {
                if (!BoundGenericNominalTypeContract.IsNominalClassType(type,
                        program.GenericNominalTypeInstances,
                        program.GenericNominalTypeDefinitions))
                {
                    throw Unsupported(
                        "An EN1 class query Body root is not a nominal class type.");
                }

                var instanceIndex
                    = BoundGenericNominalTypeTypes.Index(type);
                if ((uint)instanceIndex
                    >= (uint)program.GenericNominalTypeInstances.Length)
                {
                    throw Invalid(
                        "An EN1 class query Body root has an invalid type instance.");
                }

                var instance = program.GenericNominalTypeInstances[
                    instanceIndex];
                var identity = instance.Definition;
                if (!standardClassProviderByIdentity.TryGetValue(identity,
                            out var provider)
                        || !standardClassRootTablesByProvider.TryGetValue(
                            provider.ModuleIdentity, out var providerTables)
                        || !typeDefRowByRootClassIdentity.TryGetValue(identity,
                        out var localTypeDefRow))
                {
                    throw Unsupported(
                        "An EN1 class query Body root has no emitted TypeDef stub.");
                }

                var typeDefRow = instance.TypeSpecReference == 0
                        ? checked((int)(identity.DefinitionId
                            & 0x00FF_FFFF))
                        : GetEN1ClassQueryRootTypeDefRow(instance, provider,
                            providerTables);
                var typeSpecKey = (identity, instance.TypeSpecReference);
                if (classQueryRootTypeSpecRows.TryGetValue(typeSpecKey,
                        out var existingTypeSpecRow))
                {
                    return existingTypeSpecRow;
                }

                var typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                ReadOnlyMemory<byte> typeReference;
                if (instance.TypeSpecReference != 0)
                {
                    var sourceType = providerTables.TypeSpecs
                        .GetTypeSpecRef(instance.TypeSpecReference);
                    if (sourceType.Form == 0x03
                        && packedClassIdentityByRootClassImport.TryGetValue(
                            (provider.ModuleIdentity,
                                checked((int)sourceType.Row)),
                            out var packedIdentity)
                        && typeDefRowByRootClassIdentity.TryGetValue(
                            packedIdentity, out var packedRootTypeDefRow))
                    {
                        typeReference
                            = TrRmTypeSpecTableWriter.Reference(0x02,
                                checked((uint)packedRootTypeDefRow));
                    }
                    else if (sourceType.Form == 0x03
                        && importRowByRootClassImport.TryGetValue(
                            (provider.ModuleIdentity,
                                checked((int)sourceType.Row)),
                            out var importedRootRow))
                    {
                        typeReference
                            = TrRmTypeSpecTableWriter.Reference(0x03,
                                checked((uint)importedRootRow));
                    }
                    else if (sourceType.Form == 0x02
                        && typeDefRow == checked((int)(
                            identity.DefinitionId & 0x00FF_FFFF)))
                    {
                        typeReference
                            = TrRmTypeSpecTableWriter.Reference(0x02,
                                checked((uint)localTypeDefRow));
                    }
                    else
                    {
                        throw Unsupported(
                            "An EN1 class query Body root lost its exact TypeDef Import.");
                    }
                }
                else if (importRowByRootClassIdentity.TryGetValue(identity,
                        out var importRow))
                {
                    typeReference = TrRmTypeSpecTableWriter.Reference(0x03,
                        checked((uint)importRow));
                }
                else
                {
                    typeReference = TrRmTypeSpecTableWriter.Reference(0x02,
                        checked((uint)localTypeDefRow));
                }

                typeSpecRows.Add(typeReference);
                classQueryRootTypeSpecRows.Add(typeSpecKey, typeSpecRow);
                return typeSpecRow;
            }

            var structureFieldFirst = new int[typeDefRows.Count];
            for (var typeDefIndex = 0;
                typeDefIndex < typeDefRows.Count; typeDefIndex++)
            {
                structureFieldFirst[typeDefIndex]
                    = checked((int)typeDefRows[typeDefIndex].FieldFirst);
            }

            bodyRows.Add(EncodeStandardRegisteredQueryBody(
                classQueryFunction, program, schema, typeSpecRows, 0,
                new Dictionary<PrimitiveType, int>(),
                new Dictionary<PrimitiveType, int>(),
                typeDefRowByStructure, new Dictionary<int, int>(),
                structureFieldFirst, fieldPathRows, standardFieldImportRows,
                GetClassQueryRootTypeSpecReference,
                standardClassFieldRowsByIdentity,
                allowUnsafeClassOffsetQuery: true,
                requireClassFieldImport: hasForeignClassQueryProvider));
            if (fieldPathRows.Count != classFieldPaths.Length)
            {
                throw Invalid(
                    "An EN1 class query Body did not emit one FieldPath per query.");
            }

            for (var pathIndex = 0;
                pathIndex < fieldPathRows.Count; pathIndex++)
            {
                var fieldPath = fieldPathRows[pathIndex];
                if (fieldPath.RootTypeSpecRef == 0
                    || fieldPath.FieldReferences.Count
                        != classFieldPaths[pathIndex].Steps.Length)
                {
                    throw Invalid(
                        "An EN1 class query Body emitted an incomplete FieldPath.");
                }

                targetLayoutDependencyFieldReferences.AddRange(
                    fieldPath.FieldReferences);
                targetLayoutDependencyRows.Add(
                    new TrRmTargetLayoutDependencyRow(3, 0, 0,
                        fieldPath.RootTypeSpecRef, 1, 1,
                        fieldPath.FieldReferences.Count));
            }

            hasTargetProfile = true;

            var methodNameRow = AddString(classQueryFunction.Name);
            var methodVisibility = classQueryFunction.IsPrivate ? 0u
                : classQueryFunction.IsInternal ? 1u : 2u;
            var methodRow = checked(methodRows.Count + 1);
            methodSignatureRows.Add(new TrRmMethodSigRow(
                GetC1M1SourceScalarTypeSpecReference(
                    classQueryFunction.ReturnType),
                0, 0, 0, 0, 0));
            methodRows.Add(new TrRmMethodDefRow(0, methodNameRow,
                methodSignatureRows.Count, methodVisibility, 0, 0,
                bodyRows.Count));
            if (methodVisibility == 2)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                        classQueryFunction.Name))
                {
                    throw Unsupported(
                        "An exported EN1 class query method has an invalid declaration name.");
                }

                exportRows.Add(new TrRmExportRow(
                    0x1600_0000u | checked((uint)methodRow),
                    methodNameRow));
            }
        }

        foreach (var typeName in typeDefNames)
        {
            _ = AddString(typeName.Namespace);
            _ = AddString(typeName.Name);
        }
        foreach (var fieldName in fieldNames)
        {
            _ = AddString(fieldName);
        }
        _ = AddString(moduleIdentity);
        foreach (var packedModuleIdentity in packedClassModuleIdentities
            .OrderBy(static identity => identity, StringComparer.Ordinal))
        {
            _ = AddString(packedModuleIdentity);
        }

        var textBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(textBytes, stringRows.Count);
        for (var typeDef = 0; typeDef < typeDefRows.Count; typeDef++)
        {
            typeDefRows[typeDef] = typeDefRows[typeDef] with {
                NamespaceStringRow
                    = stringRowsByText[typeDefNames[typeDef].Namespace],
                NameStringRow = stringRowsByText[
                    typeDefNames[typeDef].Name],
            };
        }
        for (var fieldIndex = 0; fieldIndex < fieldRows.Count; fieldIndex++)
        {
            fieldRows[fieldIndex] = fieldRows[fieldIndex] with {
                NameStringRow = stringRowsByText[fieldNames[fieldIndex]],
            };
        }

        var featureRows = new List<TrRmFeatureRow>();
        if (enumRows.Count != 0)
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
            featureRows.Add(new TrRmFeatureRow(18, 1));
        }
        else if (valueBuilder.ConstantSourceCount != 0)
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }
        if (schema.UsesObjectConstruction()
            && (sourceClassConstructorPlans.Count != 0
                || hasImportedClassConstructionMethods
                || hasLocalClassConstructionBodies))
        {
            featureRows.Add(TrRmFeatureRow.ClassConstructionBodiesCandidate);
        }
        if (methodParameterLabelRows.Count != 0)
        {
            featureRows.Add(
                TrRmFeatureRow.MethodParameterLabelsCandidate);
        }
        if (classQueryFunction is not null
            || sourceClassFieldExportsByIndex.Count != 0
            || standardClassFieldExportKeysByDefinition.Count != 0
            || sourceStructFieldExportsByIndex.Count != 0
            || sourceStructFieldImportsByIndex.Count != 0)
        {
            featureRows.Add(TrRmFeatureRow.LayoutQueries);
        }
        if (hasBlittableEnum
            || usedFunctionPointerSignatureIndexes.Count != 0)
        {
            featureRows.Add(TrRmFeatureRow.NativeBoundaryContracts);
        }
        if (usedFunctionPointerSignatureIndexes.Count != 0)
        {
            featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
        }
        featureRows.Sort(static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));
        if (typeDefModuleIdentities.Count != typeDefRows.Count)
        {
            throw Invalid(
                "EN1 TypeDef ownership was not retained while packing the class closure.");
        }

        var packedClassProvidersByIdentity
            = new Dictionary<string,
                BoundSourceTypeProviderSnapshot>(StringComparer.Ordinal);
        foreach (var emission in standardClassDeclarationsByTypeDef)
        {
            var provider = emission.View.Provider;
            if (!packedClassModuleIdentities.Contains(
                    provider.ModuleIdentity))
            {
                continue;
            }

            if (packedClassProvidersByIdentity.TryGetValue(
                    provider.ModuleIdentity, out var existingProvider))
            {
                if (!SameA2SourceTypeProviderSnapshot(existingProvider,
                        provider))
                {
                    throw Invalid(
                        "A packed class Module has conflicting provider snapshots.");
                }
            }
            else
            {
                packedClassProvidersByIdentity.Add(provider.ModuleIdentity,
                    provider);
            }
        }

        if (packedClassModuleIdentities.Count
            != packedClassProvidersByIdentity.Count)
        {
            throw Invalid(
                "A packed class Module has no checked provider snapshot.");
        }
        if (!packedClassModuleIdentities.IsEmpty
            && (moduleRefRows.Count != 0 || !importRows.IsEmpty))
        {
            throw Unsupported(
                "The EN1 packed class provider closure cannot retain external Imports yet.");
        }

        if (!packedClassModuleIdentities.IsEmpty
            && (!retainedClassIndexes.IsEmpty
                || !retainedStructureIndexes.IsEmpty
                || !emittedEnums.IsEmpty))
        {
            throw Unsupported(
                "The EN1 packed class closure currently requires class-only TypeDef, FieldDef and MethodDef dependencies.");
        }

        var outputModuleIdentities = new List<string>(
            1 + packedClassModuleIdentities.Count) {
            moduleIdentity,
        };
        outputModuleIdentities.AddRange(
            packedClassModuleIdentities
                .OrderBy(static identity => identity,
                    StringComparer.Ordinal));
        var outputFeatureRows = new List<TrRmFeatureRow>();
        var moduleFeatureRanges
            = new Dictionary<string, (int First, int Count)>(
                StringComparer.Ordinal);
        foreach (var outputModuleIdentity in outputModuleIdentities)
        {
            var sourceFeatures = StringComparer.Ordinal.Equals(
                    outputModuleIdentity, moduleIdentity)
                ? featureRows
                : packedClassProvidersByIdentity[
                        outputModuleIdentity].Features
                    .Select(static feature => new TrRmFeatureRow(
                        feature.FeatureId, feature.RuleRevision))
                    .ToList();
            sourceFeatures.Sort(static (left, right)
                => left.FeatureId.CompareTo(right.FeatureId));
            var distinctFeatures
                = new List<TrRmFeatureRow>(sourceFeatures.Count);
            foreach (var feature in sourceFeatures)
            {
                if (distinctFeatures.Count != 0
                    && distinctFeatures[^1].FeatureId == feature.FeatureId)
                {
                    if (distinctFeatures[^1].RuleRevision
                        != feature.RuleRevision)
                    {
                        throw Invalid(
                            "A packed Module has conflicting revisions for one Feature.");
                    }

                    continue;
                }

                distinctFeatures.Add(feature);
            }

            var featureFirst = distinctFeatures.Count == 0
                ? 0 : checked(outputFeatureRows.Count + 1);
            outputFeatureRows.AddRange(distinctFeatures);
            moduleFeatureRanges.Add(outputModuleIdentity,
                (featureFirst, distinctFeatures.Count));
        }

        var methodOwnerModuleIdentities
            = new List<string>(methodRows.Count);
        foreach (var method in methodRows)
        {
            if (method.OwnerTypeDefRow < 0
                || method.OwnerTypeDefRow > typeDefModuleIdentities.Count)
            {
                throw Invalid(
                    "An EN1 MethodDef has an invalid packed TypeDef owner.");
            }

            methodOwnerModuleIdentities.Add(method.OwnerTypeDefRow == 0
                ? moduleIdentity
                : typeDefModuleIdentities[method.OwnerTypeDefRow - 1]);
        }

        static Dictionary<string, (int Start, int Count)> CreateModuleRanges(
            List<string> owners, IReadOnlyList<string> moduleIdentities,
            int recordCount, string tableName)
        {
            var ranges = new Dictionary<string, (int Start, int Count)>(
                StringComparer.Ordinal);
            var cursor = 1;
            foreach (var identity in moduleIdentities)
            {
                var first = -1;
                var count = 0;
                for (var index = 0; index < owners.Count; index++)
                {
                    if (!StringComparer.Ordinal.Equals(
                            owners[index], identity))
                    {
                        continue;
                    }

                    if (first < 0)
                    {
                        first = index;
                    }
                    count++;
                }

                if (count != 0)
                {
                    if (first + 1 != cursor)
                    {
                        throw Unsupported(
                            $"EN1 packed {tableName} rows are not grouped by Module identity.");
                    }

                    for (var offset = 0; offset < count; offset++)
                    {
                        if (!StringComparer.Ordinal.Equals(
                                owners[first + offset], identity))
                        {
                            throw Unsupported(
                                $"EN1 packed {tableName} rows are not contiguous by Module identity.");
                        }
                    }
                }

                ranges.Add(identity, (cursor, count));
                cursor = checked(cursor + count);
            }

            if (cursor != recordCount + 1)
            {
                throw Invalid(
                    $"EN1 packed {tableName} rows have no Module owner.");
            }

            return ranges;
        }

        var typeDefRanges = CreateModuleRanges(typeDefModuleIdentities,
            outputModuleIdentities, typeDefRows.Count, "TypeDef");
        var methodRanges = CreateModuleRanges(methodOwnerModuleIdentities,
            outputModuleIdentities, methodRows.Count, "MethodDef");
        var moduleRows = new List<TrRmModuleRow>(
            outputModuleIdentities.Count);
        foreach (var outputModuleIdentity in outputModuleIdentities)
        {
            var isRootModule = StringComparer.Ordinal.Equals(
                outputModuleIdentity, moduleIdentity);
            var provider = isRootModule
                ? null
                : packedClassProvidersByIdentity[outputModuleIdentity];
            var typeDefRange = typeDefRanges[outputModuleIdentity];
            var methodRange = methodRanges[outputModuleIdentity];
            var featureRange = moduleFeatureRanges[outputModuleIdentity];
            moduleRows.Add(new TrRmModuleRow(
                AddString(outputModuleIdentity),
                provider?.TargetRequirement ?? moduleTargetRequirement,
                provider?.RuntimePolicy ?? (uint)program.RuntimePolicy,
                0, 1, typeDefRange.Start,
                methodRange.Start, 1, featureRange.First,
                featureRange.Count,
                provider?.GoverningRuleEdition ?? 1));
        }

        var targetProfileCount = hasTargetProfile ? 1 : 0;
        var totalRows = (long)moduleRows.Count + stringRows.Count
            + typeDefRows.Count
            + moduleRefRows.Count + importRows.Count + fieldRows.Count
            + layoutRows.Count + layoutFieldRows.Count + methodRows.Count
            + methodSignatureRows.Count + parameterRows.Count + bodyRows.Count
            + fieldPathRows.Count + targetLayoutDependencyRows.Count
            + targetLayoutDependencyFieldReferences.Count + enumRows.Count
            + enumMemberRows.Count + typeSpecRows.Count + exportRows.Count
            + methodParameterLabelRows.Count + contractRows.Count
            + outputFeatureRows.Count + targetProfileCount
            + valueBuilder.BlobCount
            + valueBuilder.ConstantSourceCount
            + valueBuilder.FrozenValueNodeCount;
        if (moduleRows.Count > 0xFF_FFFF
            || stringRows.Count > 0xFF_FFFF
            || typeDefRows.Count > 0xFF_FFFF
            || moduleRefRows.Count > 0xFF_FFFF
            || importRows.Count > 0xFF_FFFF
            || fieldRows.Count > 0xFF_FFFF
            || layoutRows.Count > 0xFF_FFFF
            || layoutFieldRows.Count > 0xFF_FFFF
            || methodRows.Count > 0xFF_FFFF
            || methodSignatureRows.Count > 0xFF_FFFF
            || parameterRows.Count > 0xFF_FFFF
            || bodyRows.Count > 0xFF_FFFF
            || fieldPathRows.Count > 0xFF_FFFF
            || targetLayoutDependencyRows.Count > 0xFF_FFFF
            || targetLayoutDependencyFieldReferences.Count > 0xFF_FFFF
            || enumRows.Count > 0xFF_FFFF
            || enumMemberRows.Count > 0xFF_FFFF
            || typeSpecRows.Count > 0xFF_FFFF
            || exportRows.Count > 0xFF_FFFF
            || methodParameterLabelRows.Count > 0xFF_FFFF
            || contractRows.Count > 0xFF_FFFF
            || outputFeatureRows.Count > 0xFF_FFFF
            || totalRows > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The EN1 enum closure exceeds the checked metadata row limits.");
        }

        var featureBytes
            = TrRmFeatureTableWriter.EncodeModuleRanges(
                outputFeatureRows, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows, strings,
            moduleRefRows.Count, typeDefRows.Count, methodRows.Count, 0,
            outputFeatureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes,
            moduleRows.Count, strings,
            moduleRefRows.Count, typeDefRows.Count, methodRows.Count, 0,
            outputFeatureRows.Count);
        var features = outputFeatureRows.Count == 0
            ? default
            : new TrRmFeatureTableReader(featureBytes,
                outputFeatureRows.Count, schema);
        var moduleRefBytes = moduleRefRows.Count == 0 ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings,
                modules);
        var moduleRefs = moduleRefRows.Count == 0 ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Count, strings, modules);
        var typeDefBytes = typeDefRows.Count == 0 ? []
            : TrRmTypeDefTableWriter.Encode(typeDefRows, strings, modules,
                fieldRows.Count, methodRows.Count, layoutRows.Count, 0,
                schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefRows.Count, strings, modules, fieldRows.Count,
            methodRows.Count,
            layoutRows.Count, 0, schema);
        var targetProfileBytes = hasTargetProfile
            ? TrRmTargetProfileTableWriter.Encode([
                new TrRmTargetProfileRow(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision),
            ])
            : [];
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, targetProfileCount);
        var layoutBytes = layoutRows.Count == 0 ? []
            : TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
                targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs, targetProfiles,
            layoutFieldRows.Count);
        var typeSpecBytes = typeSpecRows.Count == 0 ? []
            : TrRmTypeSpecTableWriter.Encode(typeSpecRows,
                typeDefRows.Count, importRows.Count, 0, 0, schema,
                methodDefCount: methodRows.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefRows.Count, importRows.Count, 0, 0,
            schema, methodDefCount: methodRows.Count);
        var methodSignatureBytes = methodSignatureRows.Count == 0 ? []
            : TrRmMethodSigTableWriter.Encode(methodSignatureRows,
                typeSpecs, parameterRows.Count, 0);
        var methodSignatures = methodSignatureRows.Count == 0 ? default
            : new TrRmMethodSigTableReader(methodSignatureBytes,
                methodSignatureRows.Count, typeSpecs, parameterRows.Count,
                0);
        var parameterBytes = parameterRows.Count == 0 ? []
            : TrRmParamTableWriter.Encode(parameterRows, methodSignatures,
                typeSpecs, schema);
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings, typeDefs,
                typeSpecs, schema);
        var fields = fieldRows.Count == 0 ? default
            : new TrRmFieldDefTableReader(fieldBytes, fieldRows.Count,
                strings, typeDefs, typeSpecs, schema);
        var layoutFieldBytes = layoutFieldRows.Count == 0 ? []
            : TrRmLayoutFieldTableWriter.Encode(layoutFieldRows, layouts,
                typeDefs, fields);
        var methodBytes = methodRows.Count == 0 ? []
            : TrRmMethodDefTableWriter.Encode(methodRows, strings, modules,
                typeDefs, methodSignatures, contractRows.Count,
                bodyRows.Count, schema);
        var methods = methodRows.Count == 0 ? default
            : new TrRmMethodDefTableReader(methodBytes, methodRows.Count,
                strings, modules, typeDefs, methodSignatures,
                contractRows.Count, bodyRows.Count, schema);
        var importBytes = importRows.Count == 0 ? []
            : TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                typeDefs, methods, default, default, default, fields,
                features, modules, schema);
        var imports = new TrRmImportTableReader(importBytes,
            importRows.Count, strings,
            moduleRefs, typeDefs, methods, default, default, default, fields,
            features, modules, schema);
        var fieldPathBytes = fieldPathRows.Count == 0 ? []
            : TrRmFieldPathTableWriter.Encode(fieldPathRows, typeSpecs,
                fields, imports);
        var targetLayoutDependencyFieldBytes
            = targetLayoutDependencyFieldReferences.Count == 0 ? []
            : TrRmTargetLayoutDependencyFieldTableWriter.Encode(
                targetLayoutDependencyFieldReferences, fields, imports);
        var targetLayoutDependencyBytes
            = targetLayoutDependencyRows.Count == 0 ? []
            : TrRmTargetLayoutDependencyTableWriter.Encode(
                targetLayoutDependencyRows, typeSpecs, targetProfiles,
                targetLayoutDependencyFieldReferences.Count);
        var bodyBytes = bodyRows.Count == 0 ? []
            : TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var contractBytes = contractRows.Count == 0 ? []
            : TrRmContractTableWriter
                .EncodeConstructionDraftForSchema(contractRows, typeDefs,
                    methods, methodSignatures, parameters, typeSpecs,
                    fields, default, imports, features, modules,
                    predicateGraphCount: 0, schema: schema);
        var enumMemberBytes = enumMemberRows.Count == 0 ? []
            : TrRmEnumTablesWriter.EncodeTypedEnumMemberValues(
                enumMemberRows, strings, typeDefs,
                valueBuilder.ConstantSourceCount);
        var enumMembers = new TrRmEnumMemberDefTableReader(enumMemberBytes,
            enumMemberRows.Count, strings, typeDefs,
            valueBuilder.ConstantSourceCount);
        var enumDefBytes = enumRows.Count == 0 ? []
            : TrRmEnumTablesWriter.Encode(enumRows, typeDefs, typeSpecs,
                layouts, targetProfiles, enumMembers);
        var enumDefs = new TrRmEnumDefTableReader(enumDefBytes,
            enumRows.Count, typeDefs, typeSpecs, layouts,
            targetProfiles, enumMembers);
        var values = valueBuilder.EncodeC1EnumClosure(typeSpecs, typeDefs,
            enumDefs, imports);
        var exportBytes = exportRows.Count == 0 ? []
            : TrRmExportTableWriter.Encode(exportRows, strings, typeDefs,
                methods, default, imports, default, default, fields,
                features, modules, schema);
        var orderedMethodParameterLabelRows = methodParameterLabelRows
            .OrderBy(static row => row.MethodDefRow)
            .ThenBy(static row => row.Ordinal)
            .ToArray();
        var methodParameterLabelBytes
            = new byte[checked(
                orderedMethodParameterLabelRows.Length * 12)];
        for (var index = 0;
            index < orderedMethodParameterLabelRows.Length; index++)
        {
            var label = orderedMethodParameterLabelRows[index];
            var offset = index * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodParameterLabelBytes.AsSpan(offset),
                checked((uint)label.MethodDefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodParameterLabelBytes.AsSpan(offset + 4),
                checked((uint)label.Ordinal));
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodParameterLabelBytes.AsSpan(offset + 8),
                checked((uint)stringRowsByText[label.Name]));
        }

        var sections = new List<TrRmSection> {
            new(0x0001, 0, checked((uint)moduleRows.Count), moduleBytes),
            new(0x0002, 0, checked((uint)stringRows.Count), textBytes),
        };
        if (outputFeatureRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                checked((uint)outputFeatureRows.Count), featureBytes));
        }
        if (values.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)values.BlobCount), values.BlobSection));
        }
        if (typeSpecRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0011, 0,
                checked((uint)typeSpecRows.Count), typeSpecBytes));
        }
        if (moduleRefRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                checked((uint)moduleRefRows.Count), moduleRefBytes));
        }
        if (typeDefRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0013, 0,
                checked((uint)typeDefRows.Count), typeDefBytes));
        }
        if (fieldRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0014, 0,
                checked((uint)fieldRows.Count), fieldBytes));
        }
        if (layoutRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0015, 0,
                checked((uint)layoutRows.Count), layoutBytes));
        }
        if (layoutFieldRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0023, 0,
                checked((uint)layoutFieldRows.Count), layoutFieldBytes));
        }
        if (methodRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0016, 0,
                checked((uint)methodRows.Count), methodBytes));
        }
        if (methodSignatureRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0017, 0,
                checked((uint)methodSignatureRows.Count),
                methodSignatureBytes));
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
        if (orderedMethodParameterLabelRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0066, 0,
                checked((uint)orderedMethodParameterLabelRows.Length),
                methodParameterLabelBytes));
        }
        if (exportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exportRows.Count), exportBytes));
        }
        if (importRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                checked((uint)importRows.Count), importBytes));
        }
        if (targetLayoutDependencyRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0045, 0,
                checked((uint)targetLayoutDependencyRows.Count),
                targetLayoutDependencyBytes));
        }
        if (targetLayoutDependencyFieldReferences.Count != 0)
        {
            sections.Add(new TrRmSection(0x004A, 0,
                checked((uint)targetLayoutDependencyFieldReferences.Count),
                targetLayoutDependencyFieldBytes));
        }
        if (fieldPathRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x004B, 0,
                checked((uint)fieldPathRows.Count), fieldPathBytes));
        }
        if (values.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                checked((uint)values.ConstantSourceCount),
                values.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                checked((uint)values.FrozenValueNodeCount),
                values.FrozenValueNodeSection));
        }
        if (enumRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0053, 0,
                checked((uint)enumRows.Count), enumDefBytes));
        }
        if (enumMemberRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0054, 0,
                checked((uint)enumMemberRows.Count), enumMemberBytes));
        }
        if (hasTargetProfile)
        {
            sections.Add(new TrRmSection(0x0048, 0, 1,
                targetProfileBytes));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections, schema);
        if (IsSelectedC1ClassInstanceLayoutSchemaIdentity(schemaIdentity))
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(artifact, schema, null,
                        usesNativePointerLoads: false,
                        standardSchemaIdentity: schemaIdentity));
        }
        else
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(artifact, schema));
        }

        return artifact;
    }

    private static byte GetStringEncoding(string text)
    {
        foreach (var character in text)
        {
            if (character > 0x7F)
            {
                return 1;
            }
        }

        return 0;
    }

    private static bool TryGetEnumTypeCode(PrimitiveType type,
        out byte typeCode)
        => TrRmConstantSourceValueValidator.TryGetIntegerTypeCode(type,
            out typeCode);
}
