// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private const int MaximumStandardRegisteredQueryFlowConditionals = 13;
    private const int MaximumStandardRegisteredQueryFlowNodes
        = (MaximumStandardRegisteredQueryFlowConditionals * 2) + 1;
    private const int MaximumStandardRegisteredQueryFlowBlocks
        = (MaximumStandardRegisteredQueryFlowConditionals * 3) + 1;
    private const int MaximumStandardRegisteredQueryFlowRecords
        = (MaximumStandardRegisteredQueryFlowConditionals * 4) + 1;

    private static bool HasStandardRegisteredQuery(BoundFunction function)
    {
        var found = false;
        if (!BoundTreeTraversal.VisitExpressions(function.Body, expression =>
            found |= expression is BoundTypeIdentityQuery or BoundLayoutQuery))
        {
            throw Unsupported(
                $"Function '{function.Name}' has a Body that cannot be traversed for registered queries.");
        }

        return found;
    }

    private static ImmutableArray<int>
        CollectStandardRegisteredQueryStructureIndexes(BoundProgram program,
            IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports = null,
            bool includeStandardTypeOrigins = false)
    {
        var included = new bool[program.Structures.Length];
        var pending = new Queue<int>();

        void IncludeType(PrimitiveType type)
        {
            if (BoundEnumTypes.IsType(type)
                || NativePointerTypes.IsType(type)
                || NativeFunctionPointerTypes.IsType(type)
                || type == PrimitiveType.NativeRawPointer
                || (int)type < 100 || (int)type >= BoundEnumTypes.First)
            {
                return;
            }

            var structureIndex = (int)type - 100;
            if ((uint)structureIndex >= (uint)program.Structures.Length)
            {
                throw Unsupported(
                    $"A registered query refers to unrepresented structure type {type}.");
            }

            if (program.Structures[structureIndex].BlittableTargetRequirement
                != FixedLayoutStorage.WindowsX64TargetRequirement)
            {
                throw Unsupported(
                    $"The Standard registered-query emitter requires local structure '{program.Structures[structureIndex].Name}' to have the selected Blittable layout commitment.");
            }

            if (!included[structureIndex])
            {
                included[structureIndex] = true;
                pending.Enqueue(structureIndex);
            }
        }

        foreach (var function in program.Functions)
        {
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    switch (expression)
                    {
                        case BoundTypeIdentityQuery identity:
                            IncludeType(identity.Subject.Type);
                            break;
                        case BoundLayoutQuery query:
                            IncludeType(query.Subject.Type);
                            if (query.Subject.FieldPath is
                                BoundQueryFieldPath path)
                            {
                                IncludeType(path.RootType);
                                foreach (var step in path.Steps)
                                {
                                    IncludeType(step.OwnerType);
                                    IncludeType(step.FieldType);
                                }
                            }
                            break;
                    }
                }))
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a Body that cannot be traversed for registered-query structure identities.");
            }
        }

        if (sourceTypeExports is { Count: > 0 })
        {
            if (program.SourceTypeDeclarations.IsDefault)
            {
                throw Unsupported(
                    "Standard source type exports require source-bound local declarations.");
            }

            var declarations = new Dictionary<(string Namespace, string Name),
                BoundSourceTypeDeclaration>();
            foreach (var declaration in program.SourceTypeDeclarations)
            {
                if ((uint)declaration.StructureIndex
                        >= (uint)program.Structures.Length
                    || !declarations.TryAdd(
                        (declaration.Namespace, declaration.Name), declaration))
                {
                    throw Unsupported(
                        "The source TypeDef declaration map is inconsistent.");
                }

                var structure = program.Structures[declaration.StructureIndex];
                var qualifiedName = declaration.Namespace.Length == 0
                    ? declaration.Name
                    : $"{declaration.Namespace}.{declaration.Name}";
                if (!StringComparer.Ordinal.Equals(structure.Name, qualifiedName)
                    || declaration.DeclarationSnapshot != structure
                    || structure.StandardTypeOrigin is not null)
                {
                    throw Unsupported(
                        "The source TypeDef declaration map no longer matches the checked program.");
                }
            }

            foreach (var export in sourceTypeExports)
            {
                if (export is null || export.Namespace is null
                    || string.IsNullOrEmpty(export.Name)
                    || string.IsNullOrEmpty(export.ExportKey)
                    || !declarations.TryGetValue(
                        (export.Namespace, export.Name), out var declaration))
                {
                    throw Unsupported(
                        "A Standard source TypeDef export does not select one source declaration.");
                }

                var structure = program.Structures[declaration.StructureIndex];
                if (structure.IsPrivate || structure.IsInternal
                    || structure.IsUnique || structure.CleanupMethod is not null
                    || structure.SourceGenericTypeDefinitionIndex >= 0)
                {
                    throw Unsupported(
                        "Standard source TypeDef exports require a public nongeneric Copy struct.");
                }

                IncludeType((PrimitiveType)(100 + declaration.StructureIndex));
            }
        }

        if (includeStandardTypeOrigins)
        {
            for (var structureIndex = 0;
                structureIndex < program.Structures.Length; structureIndex++)
            {
                var structure = program.Structures[structureIndex];
                if (structure.StandardLayoutOnly
                    || structure.StandardTypeOrigin is not null)
                {
                    IncludeType((PrimitiveType)(100 + structureIndex));
                }
            }
        }

        while (pending.TryDequeue(out var structureIndex))
        {
            foreach (var field in program.Structures[structureIndex].Fields)
            {
                IncludeType(field.Type);
            }
        }

        var result = ImmutableArray.CreateBuilder<int>();
        for (var index = 0; index < included.Length; index++)
        {
            if (included[index])
            {
                result.Add(index);
            }
        }

        return result.ToImmutable();
    }

    private static byte[] EncodeStandardRegisteredQueryBody(
        BoundFunction function, BoundProgram program,
        TrRmArtifactSchema schema,
        List<ReadOnlyMemory<byte>> typeRows,
        int nativeRawPointerTypeRow,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows,
        Dictionary<PrimitiveType, int> nativePointerTypeRows,
        Dictionary<int, int> structureTypeDefRows,
        Dictionary<int, int> typeDefTypeSpecRows,
        int[] structureFieldFirst,
        List<TrRmFieldPathRow> fieldPathRows,
        Dictionary<(uint ExpectedDefinitionId, string ProducerIdentity,
            string ExportKey), int> standardFieldImportRows,
        Func<PrimitiveType, uint>? classQueryRootTypeSpecReference = null,
        Dictionary<(BoundGenericNominalTypeIdentity Owner,
            uint FieldDefinitionId), int>? standardClassFieldRows = null,
        bool allowUnsafeClassOffsetQuery = false,
        bool requireClassFieldImport = false)
    {
        if (function.Body.Statements.IsDefaultOrEmpty
            || function.Body.Statements[^1] is not BoundReturn returned
            || returned.Value is null
            || function.ParameterCount > function.Variables.Length)
        {
            throw Unsupported(
                $"Function '{function.Name}' requires a returned registered-query expression.");
        }

        BoundConditional? dynamicIdentityMerge = null;
        if (returned.Value is BoundBinary identityComparison
            && identityComparison.Type == PrimitiveType.Bool
            && (identityComparison.Operator is TokenKind.EqualsEquals
                or TokenKind.BangEquals)
            && identityComparison.Left is BoundConditional conditional
            && conditional.Type == PrimitiveType.TypeIdentity
            && conditional.Condition is BoundRead {
                Type: PrimitiveType.Bool,
            }
            && conditional.WhenTrue is BoundTypeIdentityQuery trueIdentity
            && trueIdentity.Subject.TypeIdentityTerm is null
            && conditional.WhenFalse
                is BoundTypeIdentityQuery falseIdentity
            && falseIdentity.Subject.TypeIdentityTerm is null
            && identityComparison.Right is BoundTypeIdentityQuery rightIdentity
            && rightIdentity.Subject.TypeIdentityTerm is null)
        {
            dynamicIdentityMerge = conditional;
        }

        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        var fieldMap = new TrRmRegisterBodyTokenMapBuilder();
        var code = new List<byte>();
        var blockCodes = new List<List<byte>> { code };
        var exceptionRegions = new List<TrRmExceptionRegionRow> {
            new(0, 0, 0, 0, 0, 0, 0),
        };
        var exceptionHandlers = new List<TrRmExceptionHandlerRow>();
        var exceptionBlockRegions = new List<ushort> { 0 };
        var exceptionExitRecords
            = new List<(int Block, int InstructionOffset,
                byte ExitKind, byte HasTarget, ushort TargetBlock,
                byte ValueKind, byte ValueUse, ushort ValueIndex,
                ushort[] Regions)>();
        var exceptionEmptyStateBlocks = new HashSet<int>();
        var exceptionHomeIndexes = new Dictionary<int, ushort>();
        var exceptionHomeEntries = new List<byte[]>();
        var hasTryFinally = false;
        _ = BoundTreeTraversal.VisitStatements(function.Body,
            statement => hasTryFinally |= statement is BoundTryFinally);
        var exceptionHomeState
            = schema.UsesRegisterExceptionRegions() && hasTryFinally;
        var currentExceptionRegion = 0;
        var localRegisters = new Dictionary<int, int>();
        var localInitializers = new Dictionary<int, BoundExpression>();
        var topLevelLoopCount = 0;
        var loopConditionVariables = new HashSet<int>();
        foreach (var statement in function.Body.Statements)
        {
            if (statement is BoundWhile loop)
            {
                topLevelLoopCount++;
                if (loop.Condition is BoundRead {
                        Type: PrimitiveType.Bool,
                    } condition)
                {
                    _ = loopConditionVariables.Add(condition.Variable);
                }
            }
        }

        var identityMergeRegister = -1;
        var queryValueFlowResultRegister = -1;
        var queryValueFlowJoinBlocks = new List<int>();
        var queryValueFlowEdges
            = new List<(int BranchBlock, int TrueBlock, int FalseBlock)>();
        var loopFlowState
            = new List<(int Register, byte CarrierKind, PrimitiveType Type)>();
        var loopFlowIdentityRegisters = new List<int>();
        var loopFlowEdges
            = new List<(int BranchBlock, int TrueBlock, int FalseBlock)>();
        var loopFlowBranchBlock = -1;
        var loopFlowBodyBlock = -1;
        var loopFlowUpdateBlock = -1;
        var loopFlowBodyBranchBlock = -1;
        var loopFlowExitBlock = -1;
        var loopFlowHasNestedProfile = false;
        var loopFlowHasStructuredExit = false;
        BoundBinary? loopIdentityReturn = null;
        var nextRegister = 0;

        void AddLoopFlowState(int register, byte carrierKind,
            PrimitiveType type)
        {
            foreach (var existing in loopFlowState)
            {
                if (existing.Register != register)
                {
                    continue;
                }

                if (existing.CarrierKind != carrierKind
                    || existing.Type != type)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' changes a loop-carried register contract.");
                }

                return;
            }

            loopFlowState.Add((register, carrierKind, type));
        }

        int AllocateRegister()
        {
            if (nextRegister > 13)
            {
                throw Unsupported(
                    $"Function '{function.Name}' exceeds the registered-query Body register limit.");
            }

            return nextRegister++;
        }

        uint GetTypeSpecRef(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                return 0xF200_0000;
            }
            if (BoundGenericNominalTypeTypes.IsType(type)
                && classQueryRootTypeSpecReference is not null)
            {
                return classQueryRootTypeSpecReference(type);
            }

            var row = NativeValueTypeRow(type, nativeRawPointerTypeRow,
                nativeFunctionPointerTypeRows, typeRows, nativePointerTypeRows,
                program.NativePointerTypes, structureTypeDefRows,
                typeDefTypeSpecRows);
            return checked((uint)row);
        }

        ushort GetTypeMapIndex(PrimitiveType type)
        {
            return typeMap.GetIndex(CreateStandardBodyTypeMapToken(
                GetTypeSpecRef(type)));
        }

        void EnsureExceptionHome(int variableIndex)
        {
            if (!exceptionHomeState
                || exceptionHomeIndexes.ContainsKey(variableIndex))
            {
                return;
            }
            if ((uint)variableIndex >= (uint)function.Variables.Length
                || variableIndex < function.ParameterCount
                || exceptionHomeEntries.Count > ushort.MaxValue)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an invalid exception-region Home.");
            }

            var variable = function.Variables[variableIndex];
            if (!variable.IsMutable || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary
                || variable.IsInitializationLocal
                || variable.Type is not (PrimitiveType.Bool
                    or PrimitiveType.NativeInt))
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an exception-region local without a mutable scalar Home.");
            }

            var homeIndex = checked((ushort)exceptionHomeEntries.Count);
            var entry = new byte[8];
            entry[0] = 3;
            entry[1] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2),
                GetTypeMapIndex(variable.Type));
            exceptionHomeEntries.Add(entry);
            _ = exceptionHomeIndexes.TryAdd(variableIndex, homeIndex);
        }

        void AppendExceptionHomeStore(List<byte> targetCode,
            int variableIndex, int sourceRegister)
        {
            if (!exceptionHomeState)
            {
                return;
            }
            if (!exceptionHomeIndexes.TryGetValue(variableIndex,
                    out var homeIndex)
                || sourceRegister is < 0 or >= 14)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an untracked exception-region value.");
            }

            targetCode.Add(0xD6);
            targetCode.Add((byte)(0x10 | sourceRegister));
            targetCode.Add((byte)homeIndex);
            targetCode.Add((byte)(homeIndex >> 8));
        }

        void AppendExceptionHomeStores(int block)
        {
            if (!exceptionHomeState)
            {
                return;
            }

            var targetCode = blockCodes[block];
            foreach (var (variableIndex, register) in localRegisters)
            {
                AppendExceptionHomeStore(targetCode, variableIndex,
                    register);
            }
        }

        void AppendExceptionHomeLoads(int block)
        {
            if (!exceptionHomeState)
            {
                return;
            }

            var targetCode = blockCodes[block];
            foreach (var (variableIndex, register) in localRegisters)
            {
                if (!exceptionHomeIndexes.TryGetValue(variableIndex,
                        out var homeIndex)
                    || register is < 0 or >= 14)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has an untracked exception-region value.");
                }

                targetCode.Add(0xD5);
                targetCode.Add((byte)register);
                targetCode.Add((byte)homeIndex);
                targetCode.Add((byte)(homeIndex >> 8));
            }
        }

        ushort GetFieldMapIndex(BoundQueryFieldPath path)
        {
            if (!path.HasValidStructure || path.RootType == PrimitiveType.TypeIdentity
                || path.Steps.Length > SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an invalid registered-query FieldPath.");
            }

            var rootTypeSpecRef = GetTypeSpecRef(path.RootType);
            var fieldReferences = new uint[path.Steps.Length];
            for (var index = 0; index < path.Steps.Length; index++)
            {
                var step = path.Steps[index];
                if (step.StandardClassFieldOrigin is
                    BoundStandardClassFieldDeclaration classField)
                {
                    if (index != 0
                        || !BoundGenericNominalTypeContract
                            .IsNominalClassType(step.OwnerType,
                                program.GenericNominalTypeInstances,
                                program.GenericNominalTypeDefinitions)
                        || standardClassFieldRows is null)
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' has a class FieldPath step without an admitted class root.");
                    }

                    var instanceIndex
                        = BoundGenericNominalTypeTypes.Index(step.OwnerType);
                    if ((uint)instanceIndex
                        >= (uint)program.GenericNominalTypeInstances.Length)
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' has a class FieldPath step without a complete class identity.");
                    }

                    var classIdentity = program.GenericNominalTypeInstances[
                        instanceIndex].Definition;
                    if (classField.OwnerDefinitionId
                            != classIdentity.DefinitionId
                        || classField.Type != step.FieldType
                        || classField.SelectedExportKeys.IsEmpty
                        || !standardClassFieldRows.TryGetValue(
                            (classIdentity, classField.DefinitionId),
                            out var classFieldRow))
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' has a class FieldPath step without its selected FieldDef.");
                    }

                    var localClassFieldDefinition = 0x1400_0000u
                        | checked((uint)classFieldRow);
                    var selectedFieldKey
                        = classField.SelectedExportKeys[0];
                    var classProvider = program.StandardClassDeclarations
                        .SingleOrDefault(declaration =>
                            declaration.Declaration.Identity
                                == classIdentity)
                        ?.Declaration.Provider
                        ?? throw Unsupported(
                            $"Function '{function.Name}' has a class FieldPath step without its provider snapshot.");
                    if (standardFieldImportRows.TryGetValue(
                            (localClassFieldDefinition,
                                classProvider.ModuleIdentity,
                                selectedFieldKey),
                            out var classFieldImportRow))
                    {
                        fieldReferences[index] = 0x1C00_0000u
                            | checked((uint)classFieldImportRow);
                    }
                    else
                    {
                        if (requireClassFieldImport)
                        {
                            throw Unsupported(
                                $"Function '{function.Name}' has a class FieldPath step without its exact FieldDef Import.");
                        }

                        fieldReferences[index]
                            = localClassFieldDefinition;
                    }

                    continue;
                }

                var structureIndex = (int)step.OwnerType - 100;
                var fieldOrdinal = checked((int)step.FieldOrdinal);
                if ((uint)structureIndex >= (uint)program.Structures.Length
                    || !structureTypeDefRows.TryGetValue(structureIndex,
                        out var typeDefRow)
                    || fieldOrdinal >= program.Structures[structureIndex]
                        .Fields.Length
                    || program.Structures[structureIndex]
                        .Fields[fieldOrdinal].Type != step.FieldType)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a FieldPath step without an emitted FieldDef.");
                }

                var firstField = structureFieldFirst[typeDefRow - 1];
                if (firstField == 0)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a FieldPath step without an emitted FieldDef.");
                }

                var fieldRow = checked(firstField + fieldOrdinal);
                if (fieldRow > 0x00FF_FFFF)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' exceeds the FieldDef identity range.");
                }

                var localFieldDefinition
                    = 0x1400_0000u | (uint)fieldRow;
                if (step.StandardFieldOrigin is { } fieldOrigin)
                {
                    var selectedFieldKeys
                        = fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty
                            ? fieldOrigin.ExportKeys
                            : fieldOrigin.SelectedExportKeys;
                    if (fieldOrigin.ExportKeys.IsDefaultOrEmpty
                        || selectedFieldKeys.IsDefaultOrEmpty
                        || !standardFieldImportRows.TryGetValue(
                            (localFieldDefinition,
                                fieldOrigin.Provider.ModuleIdentity,
                                selectedFieldKeys[0]),
                            out var importRow))
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' has a FieldPath step without its selected FieldDef Import.");
                    }

                    fieldReferences[index] = 0x1C00_0000u
                        | checked((uint)importRow);
                }
                else
                {
                    fieldReferences[index] = localFieldDefinition;
                }
            }

            if (fieldPathRows.Count >= 0x00FF_FFFF)
            {
                throw Unsupported("The FieldPath table exceeds its row domain.");
            }

            fieldPathRows.Add(new TrRmFieldPathRow(rootTypeSpecRef,
                fieldReferences));
            var fieldPathToken = 0x1D00_0000u
                | checked((uint)fieldPathRows.Count);
            return fieldMap.GetIndex(fieldPathToken);
        }

        int EmitQuery(BoundExpression expression,
            int? requestedDestination = null)
        {
            var destination = requestedDestination
                ?? AllocateRegister();
            byte kind;
            ushort mapIndex;
            switch (expression)
            {
                case BoundTypeIdentityQuery identity:
                    if (identity.Subject.RequiresUnsafe)
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' has a TypeIdentity query with an unsafe obligation.");
                    }

                    kind = 5;
                    mapIndex = GetTypeMapIndex(identity.Subject.Type);
                    break;
                case BoundLayoutQuery query:
                    if (query.Subject.RequiresUnsafe
                        && (!allowUnsafeClassOffsetQuery
                            || !IsAuthorizedClassOffsetQuery(query)))
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' has a layout query with an unsafe obligation.");
                    }

                    kind = query.Kind switch {
                        BoundLayoutQueryKind.SizeOf => 1,
                        BoundLayoutQueryKind.AlignOf => 2,
                        BoundLayoutQueryKind.OffsetOfType => 3,
                        BoundLayoutQueryKind.OffsetOfPlace => 4,
                        _ => throw Unsupported(
                            $"Function '{function.Name}' has an unsupported layout query."),
                    };
                    mapIndex = kind is 3 or 4
                        ? GetFieldMapIndex(query.Subject.FieldPath
                            ?? throw Unsupported(
                                $"Function '{function.Name}' has a layout query without a FieldPath."))
                        : GetTypeMapIndex(query.Subject.Type);
                    break;
                default:
                    throw Unsupported(
                        $"Function '{function.Name}' has an unsupported registered-query expression.");
            }

            code.Add(0xDE);
            code.Add((byte)((kind << 4) | destination));
            code.Add((byte)mapIndex);
            code.Add((byte)(mapIndex >> 8));
            return destination;
        }

        bool IsAuthorizedClassOffsetQuery(BoundLayoutQuery query)
            => query.Kind == BoundLayoutQueryKind.OffsetOfType
                && query.Subject.Kind == BoundQuerySubjectKind.Type
                && query.Subject.RequiresUnsafe
                && query.Subject.IsUnsafeAuthorized
                && query.Subject.FieldPath is BoundQueryFieldPath path
                && BoundGenericNominalTypeContract.IsNominalClassType(
                    path.RootType, program.GenericNominalTypeInstances,
                    program.GenericNominalTypeDefinitions)
                && !path.Steps.IsEmpty
                && path.Steps[0].StandardClassFieldOrigin is not null;

        bool IsQueryValueFlowNode(BoundExpression expression)
        {
            var pending = new Stack<BoundExpression>();
            pending.Push(expression);
            var conditionalCount = 0;
            var nodeCount = 0;
            while (pending.TryPop(out var current))
            {
                if (++nodeCount > MaximumStandardRegisteredQueryFlowNodes)
                {
                    return false;
                }

                if (current is BoundLayoutQuery query)
                {
                    if (query.Type != PrimitiveType.NativeInt
                        || query.Kind is not (BoundLayoutQueryKind.SizeOf
                            or BoundLayoutQueryKind.AlignOf
                            or BoundLayoutQueryKind.OffsetOfType
                            or BoundLayoutQueryKind.OffsetOfPlace))
                    {
                        return false;
                    }

                    continue;
                }

                if (current is not BoundConditional conditional
                    || conditional.Type != PrimitiveType.NativeInt
                    || conditional.Condition is not BoundRead {
                        Type: PrimitiveType.Bool,
                    })
                {
                    return false;
                }

                if (++conditionalCount
                    > MaximumStandardRegisteredQueryFlowConditionals)
                {
                    return false;
                }

                pending.Push(conditional.WhenFalse);
                pending.Push(conditional.WhenTrue);
            }

            return conditionalCount != 0;
        }

        int EmitQueryValueFlow(BoundExpression expression)
        {
            if (expression is not BoundConditional
                || code.Count != 0 || blockCodes.Count != 1
                || localRegisters.Count != 0 || nextRegister != 0
                || function.ParameterCount != function.Variables.Length)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an unsupported query-value control-flow input.");
            }

            var resultRegister = AllocateRegister();
            queryValueFlowResultRegister = resultRegister;

            (int EntryBlock, int CompletionBlock) EmitNode(
                BoundExpression node, bool isRoot = false)
            {
                if (node is BoundLayoutQuery query)
                {
                    var leafBlock = blockCodes.Count;
                    var leafCode = new List<byte>();
                    blockCodes.Add(leafCode);
                    code = leafCode;
                    if (EmitQuery(query, resultRegister) != resultRegister)
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' could not preserve the query value register.");
                    }
                    return (leafBlock, leafBlock);
                }

                var conditional = (BoundConditional)node;
                var condition = (BoundRead)conditional.Condition;
                if ((uint)condition.Variable >= (uint)function.ParameterCount
                    || condition.Variable > 15
                    || function.Variables[condition.Variable].Type
                        != PrimitiveType.Bool)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a query-value branch that does not read a Boolean formal.");
                }

                var branchBlock = isRoot ? 0 : blockCodes.Count;
                var branchCode = isRoot ? blockCodes[0] : new List<byte>();
                if (!isRoot)
                {
                    blockCodes.Add(branchCode);
                }

                var previousBranchCode = code;
                code = branchCode;
                var conditionRegister = AllocateRegister();
                code.Add(0x11);
                code.Add((byte)(conditionRegister
                    | (condition.Variable << 4)));
                code.Add(0xE4);
                code.Add((byte)conditionRegister);
                code.Add(0);
                code.Add(0);
                code = previousBranchCode;

                var edgeIndex = queryValueFlowEdges.Count;
                queryValueFlowEdges.Add(default);
                var (trueEntryBlock, trueCompletionBlock)
                    = EmitNode(conditional.WhenTrue);
                var (falseEntryBlock, falseCompletionBlock)
                    = EmitNode(conditional.WhenFalse);
                var joinBlock = blockCodes.Count;
                blockCodes.Add(new List<byte>());
                queryValueFlowJoinBlocks.Add(joinBlock);

                AppendBranch(trueCompletionBlock, joinBlock);
                AppendBranch(falseCompletionBlock, joinBlock);
                queryValueFlowEdges[edgeIndex] = (branchBlock,
                    trueEntryBlock, falseEntryBlock);
                return (branchBlock, joinBlock);
            }

            void AppendBranch(int sourceBlock, int targetBlock)
            {
                var source = blockCodes[sourceBlock];
                source.Add(0xE3);
                source.Add(0);
                source.Add(checked((byte)targetBlock));
                source.Add(checked((byte)(targetBlock >> 8)));
            }

            var (rootEntryBlock, rootCompletionBlock)
                = EmitNode(expression, isRoot: true);
            if (rootEntryBlock != 0
                || rootCompletionBlock != blockCodes.Count - 1
                || queryValueFlowJoinBlocks.Count
                    != queryValueFlowEdges.Count)
            {
                throw Unsupported(
                    $"Function '{function.Name}' produced an invalid query-value control-flow graph.");
            }

            code = blockCodes[rootCompletionBlock];
            return resultRegister;
        }

        int EmitExpression(BoundExpression expression)
        {
            if (allowUnsafeClassOffsetQuery
                && expression is BoundUnsafeExpression unsafeExpression
                && unsafeExpression.Expression
                    is BoundLayoutQuery unsafeClassQuery
                && IsAuthorizedClassOffsetQuery(unsafeClassQuery))
            {
                return EmitQuery(unsafeClassQuery);
            }

            if (expression is BoundRead read)
            {
                if (localRegisters.TryGetValue(read.Variable,
                        out var localRegister))
                {
                    return localRegister;
                }

                if ((uint)read.Variable < (uint)function.ParameterCount
                    && read.Variable <= 15
                    && function.Variables[read.Variable].Type
                        == PrimitiveType.Bool)
                {
                    var parameter = function.Variables[read.Variable];
                    if (parameter.IsOut || parameter.IsInit
                        || parameter.IsImplicitRef
                        || parameter.IsImplicitReferenceTemporary
                        || parameter.IsInitializationLocal)
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' reads a non-value Boolean formal in its registered-query Body.");
                    }

                    var destination = AllocateRegister();
                    code.Add(0x11);
                    code.Add((byte)(destination
                        | (read.Variable << 4)));
                    return destination;
                }

                throw Unsupported(
                    $"Function '{function.Name}' reads a value not assigned by the bounded query Body local form.");
            }

            if (expression is BoundCompoundAssignment compound
                && compound.Place is BoundRead placeRead
                && localRegisters.TryGetValue(placeRead.Variable,
                    out var placeRegister)
                && compound.Type == PrimitiveType.NativeInt
                && compound.Right.Type == PrimitiveType.NativeInt)
            {
                var operation = (CompoundOperators.Binary(
                    compound.Operator), compound.OverflowBehavior) switch {
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
                    (TokenKind.Slash,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x0C,
                    (TokenKind.Percent,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x0D,
                    _ => throw Unsupported(
                        $"Function '{function.Name}' has an unsupported registered-query compound assignment."),
                };
                var rightRegister = EmitExpression(compound.Right);
                var destination = AllocateRegister();
                var payload = (ushort)(destination | (0x0F << 4)
                    | (placeRegister << 8) | (rightRegister << 12));
                code.Add(0xC0);
                code.Add(operation);
                code.Add(unchecked((byte)payload));
                code.Add((byte)(payload >> 8));
                return destination;
            }

            if (expression is BoundConditional queryValueFlow
                && IsQueryValueFlowNode(queryValueFlow))
            {
                return EmitQueryValueFlow(queryValueFlow);
            }

            if (expression is BoundConditional identityConditional
                && dynamicIdentityMerge is not null
                && ReferenceEquals(identityConditional,
                    dynamicIdentityMerge)
                && identityConditional.Type == PrimitiveType.TypeIdentity
                && identityConditional.Condition is BoundRead {
                    Type: PrimitiveType.Bool,
                } conditionRead
                && identityConditional.WhenTrue
                    is BoundTypeIdentityQuery trueIdentity
                && identityConditional.WhenFalse
                    is BoundTypeIdentityQuery falseIdentity)
            {
                if (identityMergeRegister >= 0 || code.Count != 0
                    || blockCodes.Count != 1
                    || function.ParameterCount != function.Variables.Length
                    || (uint)conditionRead.Variable
                        >= (uint)function.ParameterCount
                    || function.Variables[conditionRead.Variable].Type
                        != PrimitiveType.Bool
                    || conditionRead.Variable > 15
                    || trueIdentity.Subject.TypeIdentityTerm is not null
                    || falseIdentity.Subject.TypeIdentityTerm is not null)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has an unsupported TypeIdentity merge input.");
                }

                var conditionRegister = AllocateRegister();
                code.Add(0x11);
                code.Add((byte)(conditionRegister
                    | (conditionRead.Variable << 4)));
                var valueRegister = AllocateRegister();
                code.Add(0xE4);
                code.Add((byte)conditionRegister);
                code.Add(4);
                code.Add(0);

                code = new List<byte>();
                blockCodes.Add(code);
                var trueRegister = EmitQuery(trueIdentity,
                    valueRegister);
                if (trueRegister != valueRegister)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' could not preserve the TypeIdentity true-edge register.");
                }
                code.Add(0xE3);
                code.Add(0);
                code.Add(3);
                code.Add(0);

                code = new List<byte>();
                blockCodes.Add(code);
                var falseRegister = EmitQuery(falseIdentity,
                    valueRegister);
                if (falseRegister != valueRegister)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' could not preserve the TypeIdentity false-edge register.");
                }
                code.Add(0xE3);
                code.Add(0);
                code.Add(3);
                code.Add(0);

                code = new List<byte>();
                blockCodes.Add(code);
                identityMergeRegister = valueRegister;
                return valueRegister;
            }

            if (expression is BoundConditional orderedConditional
                && orderedConditional.Type == PrimitiveType.NativeInt
                && orderedConditional.WhenTrue is BoundLayoutQuery {
                    Type: PrimitiveType.NativeInt,
                } trueLayout
                && orderedConditional.WhenFalse is BoundLayoutQuery {
                    Type: PrimitiveType.NativeInt,
                } falseLayout
                && orderedConditional.Condition is BoundBinary orderedComparison
                && orderedComparison.Type == PrimitiveType.Bool
                && orderedComparison.Left.Type == PrimitiveType.NativeInt
                && orderedComparison.Right.Type == PrimitiveType.NativeInt)
            {
                var predicate = orderedComparison.Operator switch {
                    TokenKind.Less => (byte)0x01,
                    TokenKind.LessEquals => (byte)0x03,
                    TokenKind.Greater => (byte)0x04,
                    TokenKind.GreaterEquals => (byte)0x06,
                    _ => (byte)0,
                };
                if (predicate == 0)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has an unsupported registered-query selection comparison.");
                }

                var left = EmitExpression(orderedComparison.Left);
                var right = EmitExpression(orderedComparison.Right);
                var relationRegister = AllocateRegister();
                code.Add(0xC2);
                code.Add(0);
                code.Add((byte)(relationRegister | 0xF0));
                code.Add((byte)(left | (right << 4)));
                var whenTrue = EmitQuery(trueLayout);
                var whenFalse = EmitQuery(falseLayout);
                var destination = AllocateRegister();
                var payload = (ushort)(destination
                    | (relationRegister << 4) | (whenTrue << 8)
                    | (whenFalse << 12));
                code.Add(0xC4);
                code.Add(predicate);
                code.Add(unchecked((byte)payload));
                code.Add((byte)(payload >> 8));
                return destination;
            }

            if (expression is BoundConditional conditional
                && conditional.Condition is BoundBinary identityCondition
                && conditional.Type == conditional.WhenTrue.Type
                && conditional.Type == conditional.WhenFalse.Type
                && identityCondition.Type == PrimitiveType.Bool
                && identityCondition.Left.Type
                    == PrimitiveType.TypeIdentity
                && identityCondition.Right.Type
                    == PrimitiveType.TypeIdentity
                && (identityCondition.Operator is TokenKind.EqualsEquals
                    or TokenKind.BangEquals))
            {
                var left = EmitExpression(identityCondition.Left);
                var right = EmitExpression(identityCondition.Right);
                code.Add(0x40);
                code.Add((byte)(left | (right << 4)));
                var whenTrue = EmitExpression(conditional.WhenTrue);
                var whenFalse = EmitExpression(conditional.WhenFalse);
                var destination = AllocateRegister();
                code.Add(0xC4);
                code.Add(identityCondition.Operator
                    == TokenKind.EqualsEquals ? (byte)2 : (byte)8);
                code.Add((byte)(destination | (left << 4)));
                code.Add((byte)(whenTrue | (whenFalse << 4)));
                return destination;
            }

            if (expression is BoundCast {
                    Operand.Type: PrimitiveType.NativeInt,
                    Type: PrimitiveType.Int32,
                } cast)
            {
                var source = EmitExpression(cast.Operand);
                var destination = AllocateRegister();
                var targetTypeMapIndex = GetTypeMapIndex(cast.Type);
                code.Add(0xD0);
                code.Add((byte)(source | (destination << 4)));
                code.Add((byte)targetTypeMapIndex);
                code.Add((byte)(targetTypeMapIndex >> 8));
                return destination;
            }

            if (expression is BoundBinary arithmetic
                && arithmetic.Type == PrimitiveType.NativeInt
                && arithmetic.Left.Type == PrimitiveType.NativeInt
                && arithmetic.Right.Type == PrimitiveType.NativeInt)
            {
                var operation = (arithmetic.Operator,
                    arithmetic.OverflowBehavior) switch {
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
                    (TokenKind.Slash,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x0C,
                    (TokenKind.Percent,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x0D,
                    _ => throw Unsupported(
                        $"Function '{function.Name}' has an unsupported registered-query arithmetic operation."),
                };
                var left = EmitExpression(arithmetic.Left);
                var right = EmitExpression(arithmetic.Right);
                var destination = AllocateRegister();
                var payload = (ushort)(destination | (0x0F << 4)
                    | (left << 8) | (right << 12));
                code.Add(0xC0);
                code.Add(operation);
                code.Add(unchecked((byte)payload));
                code.Add((byte)(payload >> 8));
                return destination;
            }

            if (expression is BoundTypeIdentityQuery or BoundLayoutQuery)
            {
                return EmitQuery(expression);
            }

            if (expression is BoundBinary comparison
                && comparison.Type == PrimitiveType.Bool
                && comparison.Left.Type == PrimitiveType.NativeInt
                && comparison.Right.Type == PrimitiveType.NativeInt
                && comparison.Operator is TokenKind.Less
                    or TokenKind.LessEquals or TokenKind.Greater
                    or TokenKind.GreaterEquals)
            {
                var predicate = comparison.Operator switch {
                    TokenKind.Less => (byte)0x01,
                    TokenKind.LessEquals => (byte)0x03,
                    TokenKind.Greater => (byte)0x04,
                    TokenKind.GreaterEquals => (byte)0x06,
                    _ => (byte)0,
                };
                if (predicate == 0)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has an unsupported registered-query comparison.");
                }

                var left = EmitExpression(comparison.Left);
                var right = EmitExpression(comparison.Right);
                var relationRegister = AllocateRegister();
                code.Add(0xC2);
                code.Add(0);
                code.Add((byte)(relationRegister | 0xF0));
                code.Add((byte)(left | (right << 4)));
                var destination = AllocateRegister();
                var predicatePayload = (ushort)(destination
                    | (relationRegister << 4) | (0x0F << 8)
                    | (0x0F << 12));
                code.Add(0xC3);
                code.Add(predicate);
                code.Add(unchecked((byte)predicatePayload));
                code.Add((byte)(predicatePayload >> 8));
                return destination;
            }

            if (expression is BoundBinary binary
                && (binary.Operator is TokenKind.EqualsEquals
                    or TokenKind.BangEquals)
                && binary.Type == PrimitiveType.Bool
                && binary.Left.Type == binary.Right.Type
                && (binary.Left.Type is PrimitiveType.TypeIdentity
                    or PrimitiveType.NativeInt))
            {
                var left = EmitExpression(binary.Left);
                var right = EmitExpression(binary.Right);
                var destination = AllocateRegister();
                code.Add(0xC2);
                code.Add(binary.Operator == TokenKind.EqualsEquals
                    ? (byte)1 : (byte)2);
                code.Add((byte)(destination | 0xF0));
                code.Add((byte)(left | (right << 4)));
                return destination;
            }

            throw Unsupported(
                $"Function '{function.Name}' has an unsupported registered-query result expression.");
        }

        var maximumLoopDepth = 0;
        var structuredTopLevelLoopCount = 0;
        var structuredLoopTransferCount = 0;
        var hasDeepStructuredLoopTransfer = false;
        var hasStructuredLoopIterator = false;
        void InspectLoopShape(BoundStatement statement, int loopDepth)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var child in block.Statements)
                    {
                        InspectLoopShape(child, loopDepth);
                    }
                    break;
                case BoundWhile loop:
                    maximumLoopDepth = Math.Max(maximumLoopDepth,
                        loopDepth + 1);
                    if (loopDepth == 0)
                    {
                        structuredTopLevelLoopCount++;
                    }
                    InspectLoopShape(loop.Body, loopDepth + 1);
                    if (loop.Iterator is not null)
                    {
                        hasStructuredLoopIterator = true;
                    }
                    break;
                case BoundTryFinally cleanup:
                    InspectLoopShape(cleanup.Body, loopDepth);
                    InspectLoopShape(cleanup.Finally, loopDepth);
                    break;
                case BoundIf conditional:
                    InspectLoopShape(conditional.Then, loopDepth);
                    if (conditional.Else is not null)
                    {
                        InspectLoopShape(conditional.Else, loopDepth);
                    }
                    break;
                case BoundBreak transfer:
                    structuredLoopTransferCount++;
                    hasDeepStructuredLoopTransfer
                        |= transfer.TargetDepth > 1;
                    break;
                case BoundContinue transfer:
                    structuredLoopTransferCount++;
                    hasDeepStructuredLoopTransfer
                        |= transfer.TargetDepth > 1;
                    break;
            }
        }

        InspectLoopShape(function.Body, 0);
        var structuredLoopFlow = function.ReturnType
                == PrimitiveType.NativeInt
            && returned.Value is BoundRead {
                Type: PrimitiveType.NativeInt,
            }
            && maximumLoopDepth != 0
            && (maximumLoopDepth > 2
                || structuredTopLevelLoopCount > 2
                || hasDeepStructuredLoopTransfer
                || hasStructuredLoopIterator
                || structuredLoopTransferCount > 1);
        bool IsStructuredLoopExpression(BoundExpression expression)
        {
            return expression switch {
                BoundRead read => (uint)read.Variable
                        < (uint)function.Variables.Length
                    && function.Variables[read.Variable].Type == read.Type
                    && read.Type is PrimitiveType.Bool
                        or PrimitiveType.NativeInt,
                BoundLayoutQuery query => query.Type
                        == PrimitiveType.NativeInt
                    && !query.Subject.RequiresUnsafe
                    && query.Kind is BoundLayoutQueryKind.SizeOf
                        or BoundLayoutQueryKind.AlignOf
                        or BoundLayoutQueryKind.OffsetOfType
                        or BoundLayoutQueryKind.OffsetOfPlace,
                BoundTypeIdentityQuery identity =>
                    identity.Type == PrimitiveType.TypeIdentity
                    && !identity.Subject.RequiresUnsafe,
                BoundBinary binary when binary.Type
                        == PrimitiveType.NativeInt
                    => binary.Left.Type == PrimitiveType.NativeInt
                        && binary.Right.Type == PrimitiveType.NativeInt
                        && (binary.Operator is TokenKind.Plus
                            or TokenKind.Minus or TokenKind.Star
                            or TokenKind.Slash or TokenKind.Percent)
                    && IsStructuredLoopExpression(binary.Left)
                    && IsStructuredLoopExpression(binary.Right),
                BoundBinary binary when binary.Type == PrimitiveType.Bool
                    && binary.Left.Type == PrimitiveType.NativeInt
                    && binary.Right.Type == PrimitiveType.NativeInt
                    => binary.Operator is TokenKind.EqualsEquals
                            or TokenKind.BangEquals
                            or TokenKind.Less or TokenKind.LessEquals
                            or TokenKind.Greater or TokenKind.GreaterEquals
                        && IsStructuredLoopExpression(binary.Left)
                        && IsStructuredLoopExpression(binary.Right),
                BoundBinary binary when binary.Type == PrimitiveType.Bool
                    && (binary.Operator is TokenKind.EqualsEquals
                        or TokenKind.BangEquals)
                    && binary.Left.Type == PrimitiveType.TypeIdentity
                    && binary.Right.Type == PrimitiveType.TypeIdentity
                    => IsStructuredLoopExpression(binary.Left)
                        && IsStructuredLoopExpression(binary.Right),
                _ => false,
            };
        }

        bool IsStructuredLoopBlock(BoundBlock block, int loopDepth)
        {
            foreach (var child in block.Statements)
            {
                if (!IsStructuredLoopStatement(child, loopDepth))
                {
                    return false;
                }
            }
            return true;
        }

        bool IsStructuredLoopStatement(BoundStatement statement,
            int loopDepth)
        {
            return statement switch {
                BoundBlock block => IsStructuredLoopBlock(block, loopDepth),
                BoundWhile loop => loop.Condition.Type
                        == PrimitiveType.Bool
                    && IsStructuredLoopExpression(loop.Condition)
                    && (loop.Iterator is null
                        || IsStructuredLoopStatement(loop.Iterator,
                            loopDepth + 1))
                    && IsStructuredLoopStatement(loop.Body,
                        loopDepth + 1),
                BoundTryFinally cleanup => exceptionHomeState
                    && IsStructuredLoopStatement(cleanup.Body, loopDepth)
                    && IsStructuredLoopStatement(cleanup.Finally, loopDepth),
                BoundIf conditional => conditional.Condition.Type
                        == PrimitiveType.Bool
                    && IsStructuredLoopExpression(conditional.Condition)
                    && IsStructuredLoopStatement(conditional.Then,
                        loopDepth)
                    && (conditional.Else is null
                        || IsStructuredLoopStatement(conditional.Else,
                            loopDepth)),
                BoundAssignment assignment => (uint)assignment.Variable
                        < (uint)function.Variables.Length
                    && assignment.Variable >= function.ParameterCount
                    && function.Variables[assignment.Variable].IsMutable
                    && function.Variables[assignment.Variable].Type
                        is PrimitiveType.Bool or PrimitiveType.NativeInt
                    && assignment.Value.Type
                        == function.Variables[assignment.Variable].Type
                    && IsStructuredLoopExpression(assignment.Value),
                BoundBreak transfer => transfer.TargetDepth > 0
                    && transfer.TargetDepth <= loopDepth,
                BoundContinue transfer => transfer.TargetDepth > 0
                    && transfer.TargetDepth <= loopDepth,
                _ => false,
            };
        }

        bool IsStructuredLoopLocal(BoundLocal local)
        {
            if ((uint)local.Variable >= (uint)function.Variables.Length
                || local.Variable < function.ParameterCount)
            {
                return false;
            }

            var variable = function.Variables[local.Variable];
            return !variable.IsOut && !variable.IsInit
                && !variable.IsImplicitRef
                && !variable.IsImplicitReferenceTemporary
                && !variable.IsInitializationLocal
                && (variable.Type is PrimitiveType.Bool
                    or PrimitiveType.NativeInt)
                && local.Initializer.Type == variable.Type
                && IsStructuredLoopExpression(local.Initializer);
        }

        bool IsStructuredLoopScope(BoundBlock block)
        {
            var hasLoop = false;
            foreach (var child in block.Statements)
            {
                switch (child)
                {
                    case BoundLocal local when !hasLoop:
                        if (!IsStructuredLoopLocal(local))
                        {
                            return false;
                        }
                        break;

                    case BoundWhile loop when !hasLoop:
                        if (!IsStructuredLoopStatement(loop, 0))
                        {
                            return false;
                        }
                        hasLoop = true;
                        break;

                    default:
                        return false;
                }
            }
            return hasLoop;
        }

        bool CanCompleteNormally(BoundStatement statement)
        {
            if (statement is BoundBlock block)
            {
                foreach (var child in block.Statements)
                {
                    if (!CanCompleteNormally(child))
                    {
                        return false;
                    }
                }
                return true;
            }

            return statement switch {
                BoundBreak or BoundContinue or BoundReturn => false,
                BoundIf conditional => conditional.Else is null
                    || CanCompleteNormally(conditional.Then)
                    || CanCompleteNormally(conditional.Else),
                BoundTryFinally cleanup
                    => CanCompleteNormally(cleanup.Body)
                        && CanCompleteNormally(cleanup.Finally),
                _ => true,
            };
        }

        var structuredLoopFunctionBody = structuredLoopFlow;
        var sawStructuredLoop = false;
        for (var statementIndex = 0;
            structuredLoopFunctionBody
                && statementIndex < function.Body.Statements.Length - 1;
            statementIndex++)
        {
            var statement = function.Body.Statements[statementIndex];
            switch (statement)
            {
                case BoundLocal local when !sawStructuredLoop:
                    structuredLoopFunctionBody
                        = local.Initializer.Type
                                == function.Variables[local.Variable].Type
                            && function.Variables[local.Variable].Type
                                is PrimitiveType.Bool
                                    or PrimitiveType.NativeInt
                            && IsStructuredLoopExpression(
                                local.Initializer);
                    break;
                case BoundAssignment assignment:
                    structuredLoopFunctionBody
                        = IsStructuredLoopStatement(assignment, 0);
                    break;
                case BoundWhile loop:
                    sawStructuredLoop = true;
                    structuredLoopFunctionBody
                        = IsStructuredLoopStatement(loop, 0);
                    break;
                case BoundBlock scope when !sawStructuredLoop:
                    structuredLoopFunctionBody
                        = IsStructuredLoopScope(scope);
                    sawStructuredLoop = structuredLoopFunctionBody;
                    break;
                default:
                    structuredLoopFunctionBody = false;
                    break;
            }
        }
        structuredLoopFlow &= structuredLoopFunctionBody
            && sawStructuredLoop;
        var structuredLoopFlowStarted = false;

        int AllocateStructuredLoopBlock()
        {
            if (blockCodes.Count
                >= MaximumStandardRegisteredQueryFlowBlocks)
            {
                throw Unsupported(
                    $"Function '{function.Name}' exceeds the registered-query structured-loop block limit.");
            }

            var block = blockCodes.Count;
            blockCodes.Add([]);
            if (schema.UsesRegisterExceptionRegions())
            {
                exceptionBlockRegions.Add(
                    checked((ushort)currentExceptionRegion));
            }
            return block;
        }

        void EmitStructuredLoopJump(int sourceBlock, int targetBlock)
        {
            if ((uint)sourceBlock >= (uint)blockCodes.Count
                || (uint)targetBlock >= (uint)blockCodes.Count
                || targetBlock > ushort.MaxValue)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an out-of-range structured-loop edge.");
            }

            code = blockCodes[sourceBlock];
            code.Add(0xE3);
            code.Add(0);
            code.Add(checked((byte)targetBlock));
            code.Add(checked((byte)(targetBlock >> 8)));
        }

        void EmitStructuredLoopBranch(int branchBlock, int conditionRegister,
            int trueBlock, int falseBlock)
        {
            if (conditionRegister is < 0 or >= 14
                || (uint)branchBlock >= (uint)blockCodes.Count
                || (uint)trueBlock >= (uint)blockCodes.Count
                || (uint)falseBlock >= (uint)blockCodes.Count
                || trueBlock > ushort.MaxValue
                || falseBlock > ushort.MaxValue)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an invalid structured-loop condition edge.");
            }

            code = blockCodes[branchBlock];
            code.Add(0xE4);
            code.Add(checked((byte)conditionRegister));
            code.Add(0);
            code.Add(0);
            loopFlowEdges.Add((branchBlock, trueBlock, falseBlock));
            loopFlowBranchBlock = loopFlowBranchBlock < 0
                ? branchBlock : loopFlowBranchBlock;
        }

        ushort[] GetExceptionRegionExitPath(int sourceBlock,
            int targetBlock)
        {
            if (!schema.UsesRegisterExceptionRegions()
                || (uint)sourceBlock >= (uint)exceptionBlockRegions.Count
                || (uint)targetBlock >= (uint)exceptionBlockRegions.Count)
            {
                throw Invalid(
                    "A registered-query exception transfer has no region owner.");
            }

            var currentRegion = exceptionBlockRegions[sourceBlock];
            var targetRegion = exceptionBlockRegions[targetBlock];
            var path = new List<ushort>();
            while (currentRegion != targetRegion)
            {
                if (currentRegion == 0
                    || currentRegion >= exceptionRegions.Count)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' transfers across an unsupported exception region.");
                }

                var region = exceptionRegions[currentRegion];
                if (region.Kind != 1)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' transfers out of a dispatch-only exception region.");
                }

                path.Add(currentRegion);
                currentRegion = region.ParentRegionIndex;
            }
            if (path.Count == 0)
            {
                throw Invalid(
                    "A registered-query RegionExit does not cross a region.");
            }
            return [.. path];
        }

        void EmitExceptionRegionExit(int sourceBlock, byte exitKind,
            int targetBlock, ushort[] regions)
        {
            if (!schema.UsesRegisterExceptionRegions())
            {
                throw Unsupported(
                    $"Function '{function.Name}' requires the register exception-region schema.");
            }

            if (exitKind == 1)
            {
                AppendExceptionHomeStores(sourceBlock);
            }

            var sourceCode = blockCodes[sourceBlock];
            var instructionOffset = sourceCode.Count;
            sourceCode.Add(0xB6);
            sourceCode.Add(0);
            exceptionExitRecords.Add((sourceBlock, instructionOffset,
                exitKind, targetBlock >= 0 ? (byte)1 : (byte)0,
                targetBlock < 0 ? (ushort)0
                    : checked((ushort)targetBlock),
                0, 0, 0, regions));
        }

        void EmitStructuredLoopTransfer(int sourceBlock, int targetBlock)
        {
            if (schema.UsesRegisterExceptionRegions()
                && exceptionBlockRegions[sourceBlock]
                    != exceptionBlockRegions[targetBlock])
            {
                EmitExceptionRegionExit(sourceBlock, 1, targetBlock,
                    GetExceptionRegionExitPath(sourceBlock, targetBlock));
                return;
            }

            if (exceptionHomeState
                && exceptionEmptyStateBlocks.Contains(targetBlock))
            {
                AppendExceptionHomeStores(sourceBlock);
            }
            EmitStructuredLoopJump(sourceBlock, targetBlock);
        }

        void StartStructuredLoopFlow()
        {
            if (structuredLoopFlowStarted)
            {
                return;
            }
            if (blockCodes.Count != 1 || loopFlowState.Count != 0
                || queryValueFlowResultRegister >= 0
                || identityMergeRegister >= 0)
            {
                throw Unsupported(
                    $"Function '{function.Name}' combines structured loops with an unsupported query-flow profile.");
            }

            var usedRegisters = new HashSet<int>();
            foreach (var (variable, register) in localRegisters)
            {
                var type = function.Variables[variable].Type;
                if (type is not (PrimitiveType.Bool
                        or PrimitiveType.NativeInt)
                    || !usedRegisters.Add(register))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has aliased or unsupported structured-loop locals.");
                }
                AddLoopFlowState(register, 0, type);
            }
            loopFlowState.Sort(static (left, right)
                => left.Register.CompareTo(right.Register));
            structuredLoopFlowStarted = true;
        }

        bool HasLocalRegister(int register)
        {
            foreach (var existing in localRegisters.Values)
            {
                if (existing == register)
                {
                    return true;
                }
            }
            return false;
        }

        int CompileStructuredLoopStatement(BoundStatement statement,
            int block, List<(int BreakTarget, int ContinueTarget)> loopTargets)
        {
            switch (statement)
            {
                case BoundBlock body:
                {
                    var currentBlock = block;
                    foreach (var child in body.Statements)
                    {
                        if (currentBlock < 0)
                        {
                            throw Unsupported(
                                $"Function '{function.Name}' has a statement after a terminating structured-loop transfer.");
                        }
                        currentBlock = CompileStructuredLoopStatement(
                            child, currentBlock, loopTargets);
                    }
                    return currentBlock;
                }
                case BoundAssignment assignment:
                {
                    if (!localRegisters.TryGetValue(assignment.Variable,
                            out var destinationRegister))
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' updates an unregistered structured-loop local.");
                    }
                    code = blockCodes[block];
                    var sourceRegister = assignment.Value
                            is BoundLayoutQuery query
                        ? EmitQuery(query, destinationRegister)
                        : EmitExpression(assignment.Value);
                    if (sourceRegister != destinationRegister)
                    {
                        code.Add(0x10);
                        code.Add(checked((byte)(destinationRegister
                            | (sourceRegister << 4))));
                    }
                    AppendExceptionHomeStore(code, assignment.Variable,
                        destinationRegister);
                    return block;
                }
                case BoundIf conditional:
                {
                    code = blockCodes[block];
                    var conditionRegister
                        = EmitExpression(conditional.Condition);
                    var trueBlock = AllocateStructuredLoopBlock();
                    var falseBlock = AllocateStructuredLoopBlock();
                    EmitStructuredLoopBranch(block, conditionRegister,
                        trueBlock, falseBlock);
                    var trueEnd = CompileStructuredLoopStatement(
                        conditional.Then, trueBlock, loopTargets);
                    var falseEnd = conditional.Else is null ? falseBlock
                        : CompileStructuredLoopStatement(conditional.Else,
                            falseBlock, loopTargets);
                    if (trueEnd < 0 && falseEnd < 0)
                    {
                        return -1;
                    }
                    if (trueEnd < 0)
                    {
                        return falseEnd;
                    }
                    if (falseEnd < 0)
                    {
                        return trueEnd;
                    }

                    var joinBlock = AllocateStructuredLoopBlock();
                    EmitStructuredLoopJump(trueEnd, joinBlock);
                    EmitStructuredLoopJump(falseEnd, joinBlock);
                    return joinBlock;
                }
                case BoundTryFinally cleanup:
                    return CompileStructuredLoopTryFinally(cleanup, block,
                        loopTargets);
                case BoundWhile loop:
                {
                    var conditionBlock = AllocateStructuredLoopBlock();
                    var bodyBlock = AllocateStructuredLoopBlock();
                    var exitBlock = AllocateStructuredLoopBlock();
                    if (loop.IsPostTested && loop.Iterator is not null)
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' combines a post-test loop with a registered-query iterator.");
                    }
                    var exitTarget = exitBlock;
                    var continueBlock = conditionBlock;
                    if (!exceptionHomeState && loop.Iterator is not null)
                    {
                        continueBlock = AllocateStructuredLoopBlock();
                    }
                    if (exceptionHomeState)
                    {
                        exitTarget = AllocateStructuredLoopBlock();
                        _ = exceptionEmptyStateBlocks.Add(exitTarget);
                        AppendExceptionHomeLoads(exitTarget);
                        EmitStructuredLoopJump(exitTarget, exitBlock);

                        continueBlock = AllocateStructuredLoopBlock();
                        _ = exceptionEmptyStateBlocks.Add(continueBlock);
                        AppendExceptionHomeLoads(continueBlock);
                    }
                    EmitStructuredLoopJump(block,
                        loop.IsPostTested ? bodyBlock : conditionBlock);
                    var nestedTargets
                        = new List<(int BreakTarget, int ContinueTarget)>(
                            loopTargets) {
                            (exitTarget, continueBlock),
                        };

                    var conditionRegister = -1;
                    if (!loop.IsPostTested)
                    {
                        code = blockCodes[conditionBlock];
                        conditionRegister = EmitExpression(loop.Condition);
                        EmitStructuredLoopBranch(conditionBlock,
                            conditionRegister, bodyBlock, exitTarget);
                    }

                    var bodyEnd = CompileStructuredLoopStatement(loop.Body,
                        bodyBlock, nestedTargets);
                    if (bodyEnd >= 0)
                    {
                        if (exceptionHomeState)
                        {
                            EmitStructuredLoopTransfer(bodyEnd,
                                continueBlock);
                        }
                        else
                        {
                            EmitStructuredLoopJump(bodyEnd, continueBlock);
                        }
                    }

                    if (loop.Iterator is not null)
                    {
                        var iteratorEnd = CompileStructuredLoopStatement(
                            loop.Iterator, continueBlock, nestedTargets);
                        if (iteratorEnd < 0)
                        {
                            throw Unsupported(
                                $"Function '{function.Name}' has a terminating registered-query iterator.");
                        }
                        EmitStructuredLoopJump(iteratorEnd, conditionBlock);
                    }
                    else if (exceptionHomeState)
                    {
                        EmitStructuredLoopJump(continueBlock,
                            conditionBlock);
                    }

                    if (loop.IsPostTested)
                    {
                        code = blockCodes[conditionBlock];
                        conditionRegister = EmitExpression(loop.Condition);
                        EmitStructuredLoopBranch(conditionBlock,
                            conditionRegister, bodyBlock, exitTarget);
                    }
                    return exitBlock;
                }
                case BoundBreak or BoundContinue:
                {
                    var targetDepth = statement switch {
                        BoundBreak breaking => breaking.TargetDepth,
                        BoundContinue continuing => continuing.TargetDepth,
                        _ => 0,
                    };
                    if (targetDepth < 1
                        || targetDepth > loopTargets.Count)
                    {
                        throw Invalid(
                            $"Function '{function.Name}' has a structured-loop transfer outside its lexical loop depth.");
                    }

                    var (breakTarget, continueTarget) = loopTargets[
                        loopTargets.Count - targetDepth];
                    EmitStructuredLoopTransfer(block,
                        statement is BoundBreak
                            ? breakTarget : continueTarget);
                    loopFlowHasStructuredExit = true;
                    return -1;
                }
                default:
                    throw Unsupported(
                        $"Function '{function.Name}' has a statement outside the structured registered-query loop form.");
            }
        }

        int CompileStructuredLoopTryFinally(BoundTryFinally cleanup,
            int block,
            List<(int BreakTarget, int ContinueTarget)> loopTargets)
        {
            if (!exceptionHomeState
                || !schema.UsesRegisterExceptionRegions()
                || !CanCompleteNormally(cleanup.Finally))
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a try/finally outside the bounded registered-query exception profile.");
            }
            if (exceptionRegions.Count > ushort.MaxValue - 1)
            {
                throw Unsupported(
                    $"Function '{function.Name}' exceeds the registered-query exception-region limit.");
            }

            var parentRegion = checked((ushort)currentExceptionRegion);
            var protectedRegionIndex
                = checked((ushort)exceptionRegions.Count);
            exceptionRegions.Add(default);
            var finallyRegionIndex
                = checked((ushort)exceptionRegions.Count);
            exceptionRegions.Add(default);

            currentExceptionRegion = protectedRegionIndex;
            var protectedEntryBlock = AllocateStructuredLoopBlock();
            currentExceptionRegion = finallyRegionIndex;
            var finallyEntryBlock = AllocateStructuredLoopBlock();
            _ = exceptionEmptyStateBlocks.Add(finallyEntryBlock);
            AppendExceptionHomeLoads(finallyEntryBlock);
            currentExceptionRegion = parentRegion;

            exceptionRegions[protectedRegionIndex]
                = new TrRmExceptionRegionRow(1, 1, parentRegion,
                    checked((ushort)protectedEntryBlock),
                    finallyRegionIndex, 0, 0);
            exceptionRegions[finallyRegionIndex]
                = new TrRmExceptionRegionRow(4, 0,
                    protectedRegionIndex,
                    checked((ushort)finallyEntryBlock), 0, 0, 0);

            var hasNormalCompletion
                = CanCompleteNormally(cleanup.Body);
            var normalResumeBlock = -1;
            var normalContinuationBlock = -1;
            if (hasNormalCompletion)
            {
                normalResumeBlock = AllocateStructuredLoopBlock();
                _ = exceptionEmptyStateBlocks.Add(normalResumeBlock);
                normalContinuationBlock = AllocateStructuredLoopBlock();
                AppendExceptionHomeLoads(normalResumeBlock);
                EmitStructuredLoopJump(normalResumeBlock,
                    normalContinuationBlock);
            }

            EmitStructuredLoopJump(block, protectedEntryBlock);
            currentExceptionRegion = protectedRegionIndex;
            var bodyEnd = CompileStructuredLoopStatement(cleanup.Body,
                protectedEntryBlock, loopTargets);
            if (bodyEnd >= 0)
            {
                if (!hasNormalCompletion || normalResumeBlock < 0)
                {
                    throw Invalid(
                        "A registered-query Protected region has an untracked normal exit.");
                }
                EmitStructuredLoopTransfer(bodyEnd, normalResumeBlock);
            }

            currentExceptionRegion = finallyRegionIndex;
            var finallyEnd = CompileStructuredLoopStatement(
                cleanup.Finally, finallyEntryBlock, loopTargets);
            if (finallyEnd < 0)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a terminating registered-query Finally region.");
            }
            AppendExceptionHomeStores(finallyEnd);
            EmitExceptionRegionExit(finallyEnd, 4, -1,
                [finallyRegionIndex]);
            currentExceptionRegion = parentRegion;
            return hasNormalCompletion ? normalContinuationBlock : -1;
        }

        void RegisterStructuredLoopLocal(BoundLocal local)
        {
            if (structuredLoopFlowStarted)
            {
                throw Unsupported(
                    $"Function '{function.Name}' declares a local after its structured query loop begins.");
            }
            if ((uint)local.Variable >= (uint)function.Variables.Length
                || local.Variable < function.ParameterCount
                || localRegisters.ContainsKey(local.Variable))
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an unsupported registered-query local.");
            }

            var variable = function.Variables[local.Variable];
            if (variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary
                || variable.IsInitializationLocal
                || variable.Type is not (PrimitiveType.NativeInt
                    or PrimitiveType.Int32 or PrimitiveType.Bool)
                || local.Initializer.Type != variable.Type)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a local outside the bounded registered-query register form.");
            }

            var valueRegister = EmitExpression(local.Initializer);
            if (structuredLoopFlow
                && HasLocalRegister(valueRegister))
            {
                var distinctRegister = AllocateRegister();
                code.Add(0x10);
                code.Add(checked((byte)(distinctRegister
                    | (valueRegister << 4))));
                valueRegister = distinctRegister;
            }
            _ = localRegisters.TryAdd(local.Variable, valueRegister);
            _ = localInitializers.TryAdd(local.Variable,
                local.Initializer);
            EnsureExceptionHome(local.Variable);
            AppendExceptionHomeStore(code, local.Variable, valueRegister);
        }

        for (var statementIndex = 0;
            statementIndex < function.Body.Statements.Length - 1;
            statementIndex++)
        {
            var statement = function.Body.Statements[statementIndex];
            if (statement is BoundLocal local)
            {
                RegisterStructuredLoopLocal(local);
                continue;
            }

            if (statement is BoundBlock loopScope
                && structuredLoopFlow
                && IsStructuredLoopScope(loopScope))
            {
                foreach (var child in loopScope.Statements)
                {
                    if (child is BoundLocal loopLocal)
                    {
                        RegisterStructuredLoopLocal(loopLocal);
                        continue;
                    }

                    if (child is BoundWhile scopedLoop)
                    {
                        StartStructuredLoopFlow();
                        var currentBlock = blockCodes.IndexOf(code);
                        if (currentBlock < 0)
                        {
                            throw Invalid(
                                "A structured-loop entry has no source Block.");
                        }
                        var exitBlock = CompileStructuredLoopStatement(
                            scopedLoop, currentBlock, []);
                        if (exitBlock < 0)
                        {
                            throw Invalid(
                                "A structured query loop has no normal exit Block.");
                        }
                        code = blockCodes[exitBlock];
                        continue;
                    }

                    throw Invalid(
                        "A validated registered-query loop scope changed shape.");
                }
                continue;
            }

            if (statement is BoundWhile structuredLoop
                && structuredLoopFlow)
            {
                StartStructuredLoopFlow();
                var currentBlock = blockCodes.IndexOf(code);
                if (currentBlock < 0)
                {
                    throw Invalid(
                        "A structured-loop entry has no source Block.");
                }
                var exitBlock = CompileStructuredLoopStatement(
                    structuredLoop, currentBlock, []);
                if (exitBlock < 0)
                {
                    throw Invalid(
                        "A structured query loop has no normal exit Block.");
                }
                code = blockCodes[exitBlock];
                continue;
            }

            if (statement is BoundWhile nestedOuterLoop
                && nestedOuterLoop.Body is BoundBlock {
                    Statements.Length: 2 or 3,
                } nestedOuterBody
                && nestedOuterBody.Statements[0] is BoundWhile nestedInnerLoop
                && nestedOuterBody.Statements[^1]
                    is BoundAssignment outerUpdate)
            {
                var nestedQueryUpdate = nestedOuterBody.Statements.Length == 3
                    ? nestedOuterBody.Statements[1] as BoundAssignment : null;
                var outerCondition
                    = nestedOuterLoop.Condition is BoundRead outerRead
                        ? outerRead : null;
                var innerCondition
                    = nestedInnerLoop.Condition is BoundRead innerRead
                        ? innerRead : null;
                var innerBody
                    = nestedInnerLoop.Body is BoundBlock innerBlock
                        ? innerBlock : null;
                var innerControlIf
                    = innerBody is { Statements.Length: > 0 }
                        ? innerBody.Statements[0] as BoundIf
                            ?? innerBody.Statements[^1] as BoundIf : null;
                var innerControlCondition
                    = innerControlIf?.Condition as BoundRead;
                var innerControlBody = innerControlIf?.Then as BoundBlock;
                var hasInnerBreakExit = innerControlIf is { Else: null }
                    && innerControlCondition?.Type == PrimitiveType.Bool
                    && innerControlBody is { Statements.Length: 1 }
                    && innerControlBody.Statements[0]
                        is BoundBreak { TargetDepth: 1 };
                var hasInnerContinueExit = innerControlIf is { Else: null }
                    && innerControlCondition?.Type == PrimitiveType.Bool
                    && innerControlBody is { Statements.Length: 1 }
                    && innerControlBody.Statements[0]
                        is BoundContinue { TargetDepth: 1 };
                var hasInnerControlExit
                    = hasInnerBreakExit || hasInnerContinueExit;
                var innerAssignmentCount = innerBody is null ? 0
                    : innerBody.Statements.Length
                        - (hasInnerControlExit ? 1 : 0);
                var innerQueryIndex = hasInnerBreakExit ? 1 : 0;
                var innerQueryUpdate
                    = innerAssignmentCount == 2
                        && innerBody is not null
                        && innerBody.Statements[innerQueryIndex]
                            is BoundAssignment innerQueryAssignment
                                ? innerQueryAssignment : null;
                var innerUpdate = innerAssignmentCount is 1 or 2
                    && innerBody is not null
                    && innerBody.Statements[hasInnerContinueExit
                        ? innerBody.Statements.Length - 2
                        : innerBody.Statements.Length - 1]
                        is BoundAssignment innerAssignment
                        ? innerAssignment : null;
                var nestedIdentityComparison
                    = returned.Value as BoundBinary;
                var nestedIdentityLeft
                    = nestedIdentityComparison?.Left
                        as BoundTypeIdentityQuery;
                var nestedIdentityRight
                    = nestedIdentityComparison?.Right
                        as BoundTypeIdentityQuery;
                var isNestedIdentityLoop
                    = function.ReturnType == PrimitiveType.Bool
                    && nestedIdentityComparison is {
                        Type: PrimitiveType.Bool,
                        Operator: TokenKind.EqualsEquals
                            or TokenKind.BangEquals,
                    }
                    && nestedIdentityLeft is not null
                    && !nestedIdentityLeft.Subject.RequiresUnsafe
                    && nestedIdentityLeft.Subject.TypeIdentityTerm is null
                    && nestedIdentityRight is not null
                    && !nestedIdentityRight.Subject.RequiresUnsafe
                    && nestedIdentityRight.Subject.TypeIdentityTerm is null;
                var nestedReturn = returned.Value as BoundRead;
                var nestedResultRegister = -1;
                if (nestedReturn is not null)
                {
                    if (localRegisters.TryGetValue(nestedReturn.Variable,
                            out var registeredNestedResult))
                    {
                        nestedResultRegister = registeredNestedResult;
                    }
                }
                if (loopFlowBranchBlock >= 0 || blockCodes.Count != 1
                    || identityMergeRegister >= 0
                    || queryValueFlowResultRegister >= 0
                    || statementIndex
                        != function.Body.Statements.Length - 2
                    || function.Body.Statements.Length
                        != (isNestedIdentityLoop ? 4 : 5)
                    || nestedOuterLoop.Iterator is not null
                    || nestedInnerLoop.Iterator is not null
                    || (function.ReturnType != PrimitiveType.NativeInt
                        && !isNestedIdentityLoop)
                    || function.ParameterCount < 4
                    || function.Variables.Length
                        != function.ParameterCount
                            + (isNestedIdentityLoop ? 2 : 3)
                    || outerCondition is null
                    || outerCondition.Type != PrimitiveType.Bool
                    || innerCondition is null
                    || innerCondition.Type != PrimitiveType.Bool
                    || innerAssignmentCount is not (1 or 2)
                    || innerUpdate is null || innerUpdate.Value.Type
                        != PrimitiveType.Bool
                    || (innerAssignmentCount == 2
                        && innerQueryUpdate is null)
                    || (hasInnerControlExit
                        && (nestedOuterLoop.IsPostTested
                            || nestedInnerLoop.IsPostTested
                            || innerControlCondition is null
                            || !IsStandardRegisteredQueryBooleanFormal(
                                function, innerControlCondition.Variable)))
                    || outerUpdate.Value.Type != PrimitiveType.Bool
                    || localRegisters.Count
                        != (isNestedIdentityLoop ? 2 : 3)
                    || localInitializers.Count
                        != (isNestedIdentityLoop ? 2 : 3)
                    || (uint)outerCondition.Variable
                        >= (uint)function.Variables.Length
                    || (uint)innerCondition.Variable
                        >= (uint)function.Variables.Length
                    || !function.Variables[outerCondition.Variable]
                        .IsMutable
                    || !function.Variables[innerCondition.Variable]
                        .IsMutable
                    || outerUpdate.Variable != outerCondition.Variable
                    || innerUpdate.Variable != innerCondition.Variable
                    || !localRegisters.TryGetValue(outerCondition.Variable,
                        out var outerRegister)
                    || !localRegisters.TryGetValue(innerCondition.Variable,
                        out var innerRegister)
                    || (!isNestedIdentityLoop
                        && (nestedReturn is null
                            || nestedReturn.Type
                                != PrimitiveType.NativeInt
                            || nestedResultRegister < 0
                            || (uint)nestedReturn.Variable
                                >= (uint)function.Variables.Length))
                    || (isNestedIdentityLoop && nestedReturn is not null))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a nested loop outside the bounded registered-query loop form.");
                }

                if (!localInitializers.TryGetValue(outerCondition.Variable,
                        out var outerInitializer)
                    || outerInitializer is not BoundRead {
                        Type: PrimitiveType.Bool,
                    } outerInitialRead
                    || !localInitializers.TryGetValue(
                        innerCondition.Variable, out var innerInitializer)
                    || innerInitializer is not BoundRead {
                        Type: PrimitiveType.Bool,
                    } innerInitialRead
                    || (!isNestedIdentityLoop
                        && (!localInitializers.TryGetValue(
                                nestedReturn!.Variable,
                                out var nestedResultInitializer)
                            || nestedResultInitializer
                                is not BoundLayoutQuery {
                                    Type: PrimitiveType.NativeInt,
                                }))
                    || (isNestedIdentityLoop
                        && (nestedQueryUpdate is not null
                            || innerQueryUpdate is not null)))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has nested-loop locals outside the query Boolean/register form.");
                }

                if (!IsStandardRegisteredQueryBooleanFormal(function,
                        outerInitialRead.Variable)
                    || !IsStandardRegisteredQueryBooleanFormal(function,
                        innerInitialRead.Variable)
                    || !IsStandardRegisteredQueryBooleanFormal(function,
                        (innerUpdate.Value as BoundRead)?.Variable ?? -1)
                    || !IsStandardRegisteredQueryBooleanFormal(function,
                        (outerUpdate.Value as BoundRead)?.Variable ?? -1))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a nested-loop value that is not a Boolean formal.");
                }

                if ((nestedOuterBody.Statements.Length == 3
                        && nestedQueryUpdate is null)
                    || (nestedQueryUpdate is not null
                        && (nestedReturn is null
                            || nestedQueryUpdate.Variable
                                != nestedReturn.Variable
                            || nestedQueryUpdate.Value
                                is not BoundLayoutQuery {
                                    Type: PrimitiveType.NativeInt,
                                }))
                    || (innerQueryUpdate is not null
                        && (nestedReturn is null
                            || innerQueryUpdate.Variable
                                != nestedReturn.Variable
                            || innerQueryUpdate.Value
                                is not BoundLayoutQuery {
                                    Type: PrimitiveType.NativeInt,
                                }))
                    || ((nestedQueryUpdate is not null
                            || innerQueryUpdate is not null)
                        && (nestedReturn is null
                            || !function.Variables[nestedReturn.Variable]
                            .IsMutable)))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a nested-loop query update outside the supported NativeInt form.");
                }

                if (isNestedIdentityLoop)
                {
                    loopIdentityReturn = nestedIdentityComparison;
                    var leftIdentityRegister
                        = EmitQuery(nestedIdentityLeft!);
                    var rightIdentityRegister
                        = EmitQuery(nestedIdentityRight!);
                    if (leftIdentityRegister == rightIdentityRegister)
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' aliases a loop-carried TypeIdentity register.");
                    }

                    loopFlowIdentityRegisters.Add(leftIdentityRegister);
                    loopFlowIdentityRegisters.Add(rightIdentityRegister);
                    AddLoopFlowState(leftIdentityRegister, 2,
                        PrimitiveType.TypeIdentity);
                    AddLoopFlowState(rightIdentityRegister, 2,
                        PrimitiveType.TypeIdentity);
                }

                var nestedRegisters = new HashSet<int>();
                foreach (var (variable, register) in localRegisters)
                {
                    var type = function.Variables[variable].Type;
                    if ((type is not (PrimitiveType.Bool
                            or PrimitiveType.NativeInt))
                        || !nestedRegisters.Add(register))
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' has aliased or unsupported nested-loop registers.");
                    }

                    AddLoopFlowState(register, 0, type);
                }

                if (!nestedRegisters.Contains(outerRegister)
                    || !nestedRegisters.Contains(innerRegister)
                    || outerRegister == innerRegister
                    || (!isNestedIdentityLoop
                        && (!nestedRegisters.Contains(nestedResultRegister)
                            || outerRegister == nestedResultRegister
                            || innerRegister == nestedResultRegister))
                    || function.Variables[outerCondition.Variable].Type
                        != PrimitiveType.Bool
                    || function.Variables[innerCondition.Variable].Type
                        != PrimitiveType.Bool
                    || (!isNestedIdentityLoop
                        && function.Variables[nestedReturn!.Variable].Type
                            != PrimitiveType.NativeInt))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has inconsistent nested-loop register types.");
                }
                loopFlowState.Sort(static (left, right)
                    => left.Register.CompareTo(right.Register));

                loopFlowHasNestedProfile = true;
                loopFlowBranchBlock
                    = nestedOuterLoop.IsPostTested ? 4 : 1;
                loopFlowBodyBlock = 1;
                var innerConditionBlock = hasInnerControlExit
                    || !nestedInnerLoop.IsPostTested ? 2 : 3;
                var innerBodyBlock = hasInnerControlExit
                    ? 3 : nestedInnerLoop.IsPostTested ? 2 : 3;
                var innerUpdateBlock = hasInnerControlExit
                    ? 4 : innerBodyBlock;
                var outerUpdateBlock = hasInnerControlExit ? 5 : 4;
                loopFlowUpdateBlock = outerUpdateBlock;
                loopFlowExitBlock = hasInnerControlExit ? 6 : 5;
                code.Add(0xE3);
                code.Add(0);
                code.Add(1);
                code.Add(0);

                for (var blockIndex = 1; blockIndex <= loopFlowExitBlock;
                    blockIndex++)
                {
                    blockCodes.Add(new List<byte>());
                }

                code = blockCodes[1];
                if (nestedOuterLoop.IsPostTested)
                {
                    code.Add(0xE3);
                    code.Add(0);
                    code.Add(2);
                    code.Add(0);
                }
                else
                {
                    code.Add(0xE4);
                    code.Add((byte)outerRegister);
                    code.Add(0);
                    code.Add(0);
                    loopFlowEdges.Add((1, 2, loopFlowExitBlock));
                }

                var innerFalseTarget = outerUpdateBlock;
                loopFlowEdges.Add((innerConditionBlock, innerBodyBlock,
                    innerFalseTarget));

                code = blockCodes[innerConditionBlock];
                code.Add(0xE4);
                code.Add((byte)innerRegister);
                code.Add(0);
                code.Add(0);

                if (hasInnerControlExit)
                {
                    code = blockCodes[3];
                    if (hasInnerContinueExit)
                    {
                        if (innerQueryUpdate is not null)
                        {
                            var updatedQueryRegister
                                = EmitExpression(innerQueryUpdate.Value);
                            if (updatedQueryRegister
                                != nestedResultRegister)
                            {
                                code.Add(0x10);
                                code.Add((byte)(nestedResultRegister
                                    | (updatedQueryRegister << 4)));
                            }
                        }

                        var innerValueRegister
                            = EmitExpression(innerUpdate.Value);
                        if (innerValueRegister != innerRegister)
                        {
                            code.Add(0x10);
                            code.Add((byte)(innerRegister
                                | (innerValueRegister << 4)));
                        }
                    }

                    var breakConditionRegister
                        = EmitExpression(innerControlCondition!);
                    code.Add(0xE4);
                    code.Add((byte)breakConditionRegister);
                    code.Add(0);
                    code.Add(0);
                    loopFlowEdges.Add((3,
                        hasInnerBreakExit ? outerUpdateBlock
                            : innerConditionBlock,
                        innerUpdateBlock));
                }

                code = blockCodes[innerUpdateBlock];
                if (!hasInnerContinueExit && innerQueryUpdate is not null)
                {
                    var updatedQueryRegister
                        = EmitExpression(innerQueryUpdate.Value);
                    if (updatedQueryRegister != nestedResultRegister)
                    {
                        code.Add(0x10);
                        code.Add((byte)(nestedResultRegister
                            | (updatedQueryRegister << 4)));
                    }
                }

                if (!hasInnerContinueExit)
                {
                    var innerValueRegister
                        = EmitExpression(innerUpdate.Value);
                    if (innerValueRegister != innerRegister)
                    {
                        code.Add(0x10);
                        code.Add((byte)(innerRegister
                            | (innerValueRegister << 4)));
                    }
                }
                code.Add(0xE3);
                code.Add(0);
                code.Add(checked((byte)innerConditionBlock));
                code.Add(0);

                code = blockCodes[outerUpdateBlock];
                if (nestedQueryUpdate is not null)
                {
                    var updatedQueryRegister
                        = EmitExpression(nestedQueryUpdate.Value);
                    if (updatedQueryRegister != nestedResultRegister)
                    {
                        code.Add(0x10);
                        code.Add((byte)(nestedResultRegister
                            | (updatedQueryRegister << 4)));
                    }
                }

                var outerValueRegister
                    = EmitExpression(outerUpdate.Value);
                if (outerValueRegister != outerRegister)
                {
                    code.Add(0x10);
                    code.Add((byte)(outerRegister
                        | (outerValueRegister << 4)));
                }
                if (nestedOuterLoop.IsPostTested)
                {
                    code.Add(0xE4);
                    code.Add((byte)outerRegister);
                    code.Add(0);
                    code.Add(0);
                    loopFlowEdges.Add((4, 1, 5));
                }
                else
                {
                    code.Add(0xE3);
                    code.Add(0);
                    code.Add(1);
                    code.Add(0);
                }

                code = blockCodes[loopFlowExitBlock];
                continue;
            }

            if (statement is BoundWhile loop)
            {
                var nativeReturn = returned.Value as BoundRead;
                var loopTypeComparison = returned.Value as BoundBinary;
                var identityLeft
                    = loopTypeComparison?.Left as BoundTypeIdentityQuery;
                var identityRight
                    = loopTypeComparison?.Right as BoundTypeIdentityQuery;
                var isIdentityLoop = function.ReturnType
                        == PrimitiveType.Bool
                    && loopTypeComparison is {
                        Type: PrimitiveType.Bool,
                        Operator: TokenKind.EqualsEquals
                            or TokenKind.BangEquals,
                    }
                    && identityLeft is not null
                    && identityRight is not null
                    && !identityLeft.Subject.RequiresUnsafe
                    && identityLeft.Subject.TypeIdentityTerm is null
                    && !identityRight.Subject.RequiresUnsafe
                    && identityRight.Subject.TypeIdentityTerm is null;
                var isNativeIntLoop = function.ReturnType
                        == PrimitiveType.NativeInt
                    && nativeReturn?.Type == PrimitiveType.NativeInt;
                var loopBody = loop.Body as BoundBlock;
                var loopBodyStatements = loopBody?.Statements ?? [];
                BoundIf? loopControlIf = null;
                var loopControlIndex = -1;
                var loopControlCount = 0;
                for (var bodyStatementIndex = 0;
                    bodyStatementIndex < loopBodyStatements.Length;
                    bodyStatementIndex++)
                {
                    if (loopBodyStatements[bodyStatementIndex]
                            is BoundIf {
                                Else: null,
                                Then: BoundBlock {
                                    Statements.Length: 1,
                                } controlBlock,
                            } candidate
                        && controlBlock.Statements[0]
                            is BoundBreak or BoundContinue)
                    {
                        loopControlIf = candidate;
                        loopControlIndex = bodyStatementIndex;
                        loopControlCount++;
                    }
                }
                var loopControlCondition
                    = loopControlIf?.Condition as BoundRead;
                var loopControlBody = loopControlIf?.Then as BoundBlock;
                var isBreakExit = loopControlBody is { Statements.Length: 1 }
                    && loopControlBody.Statements[0]
                        is BoundBreak { TargetDepth: 1 };
                var isContinueExit
                    = loopControlBody is { Statements.Length: 1 }
                    && loopControlBody.Statements[0]
                        is BoundContinue { TargetDepth: 1 };
                var hasLoopControlExit = loopControlCount == 1
                    && loopControlIf is { Else: null }
                    && loopControlCondition?.Type == PrimitiveType.Bool
                    && (isBreakExit || isContinueExit);
                var invalidLoopControlCondition = loopControlCount > 1
                    || (loopControlCount == 1
                        && (!hasLoopControlExit
                            || loopControlCondition is null
                        || loopControlCondition.Variable < 0
                        || loopControlCondition.Variable
                            >= function.ParameterCount
                        || function.Variables[
                            loopControlCondition.Variable].Type
                            != PrimitiveType.Bool));
                var canTransferSiblingLoop = isBreakExit
                    || (isContinueExit
                        && (loop.IsPostTested || loopControlIndex > 0));
                if (loopFlowHasNestedProfile
                    || (loopFlowBranchBlock >= 0
                        && (isIdentityLoop
                            || (hasLoopControlExit
                                && (loopFlowHasStructuredExit
                                    || !(topLevelLoopCount == 2
                                        && canTransferSiblingLoop)))))
                    || (loopFlowBranchBlock < 0 && blockCodes.Count != 1)
                    || statementIndex
                        >= function.Body.Statements.Length - 1
                    || identityMergeRegister >= 0
                    || queryValueFlowResultRegister >= 0
                    || loop.Iterator is not null
                    || loop.Condition is not BoundRead {
                        Type: PrimitiveType.Bool,
                    } condition
                    || loopBody is null
                    || loopBodyStatements.IsDefaultOrEmpty
                    || loopBodyStatements.Length
                        > (hasLoopControlExit ? 3 : 2)
                    || (hasLoopControlExit && isIdentityLoop)
                    || invalidLoopControlCondition
                    || !(isNativeIntLoop || isIdentityLoop)
                    || topLevelLoopCount is < 1 or > 2
                    || loopConditionVariables.Count != topLevelLoopCount
                    || (topLevelLoopCount > 1
                        && (isIdentityLoop
                            || (hasLoopControlExit
                                && !canTransferSiblingLoop)))
                    || (isNativeIntLoop
                        && (nativeReturn!.Variable == condition.Variable
                            || localRegisters.Count
                                != topLevelLoopCount + 1
                            || function.Variables.Length
                                != function.ParameterCount
                                    + topLevelLoopCount + 1))
                    || (isIdentityLoop
                        && (localRegisters.Count != 1
                            || function.Variables.Length
                                != function.ParameterCount + 1))
                    || (uint)condition.Variable
                        >= (uint)function.Variables.Length
                    || !function.Variables[condition.Variable].IsMutable
                    || !localRegisters.TryGetValue(condition.Variable,
                        out var conditionRegister))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a loop outside the bounded registered-query loop form.");
                }

                var loopResultRegister = -1;
                if (isNativeIntLoop
                    && !localRegisters.TryGetValue(nativeReturn!.Variable,
                        out loopResultRegister))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a loop result that is not a registered local.");
                }

                var loopAssignments
                    = new List<(BoundAssignment Assignment,
                        int DestinationRegister)>();
                var assignedVariables = new HashSet<int>();
                var loopPrefixAssignmentCount = 0;
                for (var loopStatementIndex = 0;
                    loopStatementIndex < loopBodyStatements.Length;
                    loopStatementIndex++)
                {
                    if (loopStatementIndex == loopControlIndex)
                    {
                        continue;
                    }

                    var loopStatement
                        = loopBodyStatements[loopStatementIndex];
                    if (loopStatement is not BoundAssignment loopUpdate
                        || (loopUpdate.Variable != condition.Variable
                            && (!isNativeIntLoop
                                || loopUpdate.Variable
                                    != nativeReturn!.Variable))
                        || (uint)loopUpdate.Variable
                            >= (uint)function.Variables.Length
                        || !function.Variables[loopUpdate.Variable].IsMutable
                        || loopUpdate.Value.Type
                            != function.Variables[loopUpdate.Variable].Type
                        || !localRegisters.TryGetValue(loopUpdate.Variable,
                            out var destinationRegister)
                        || !assignedVariables.Add(loopUpdate.Variable))
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' has an unsupported registered-query loop-body assignment.");
                    }

                    loopAssignments.Add((loopUpdate, destinationRegister));
                    if (hasLoopControlExit
                        && loopStatementIndex < loopControlIndex)
                    {
                        loopPrefixAssignmentCount++;
                    }
                }

                var assignedRegisters = new HashSet<int>();
                foreach (var (variable, register) in localRegisters)
                {
                    var type = function.Variables[variable].Type;
                    var isConditionLocal
                        = loopConditionVariables.Contains(variable);
                    var hasValidType = isNativeIntLoop
                        ? (isConditionLocal
                            ? type == PrimitiveType.Bool
                            : variable == nativeReturn!.Variable
                                && type == PrimitiveType.NativeInt)
                        : variable == condition.Variable
                            && type == PrimitiveType.Bool;
                    if (!hasValidType
                        || !assignedRegisters.Add(register))
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' has an unsupported loop-carried register set.");
                    }

                    AddLoopFlowState(register, 0, type);
                }

                if (isIdentityLoop)
                {
                    loopIdentityReturn = loopTypeComparison;
                    var leftIdentityRegister = EmitQuery(identityLeft!);
                    var rightIdentityRegister = EmitQuery(identityRight!);
                    if (!assignedRegisters.Add(leftIdentityRegister)
                        || !assignedRegisters.Add(rightIdentityRegister))
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' aliases a loop-carried TypeIdentity register.");
                    }

                    loopFlowIdentityRegisters.Add(leftIdentityRegister);
                    loopFlowIdentityRegisters.Add(rightIdentityRegister);
                    AddLoopFlowState(leftIdentityRegister, 2,
                        PrimitiveType.TypeIdentity);
                    AddLoopFlowState(rightIdentityRegister, 2,
                        PrimitiveType.TypeIdentity);
                }
                loopFlowState.Sort(static (left, right)
                    => left.Register.CompareTo(right.Register));
                var hasCompleteLoopConditionState = true;
                foreach (var variable in loopConditionVariables)
                {
                    if (!localRegisters.TryGetValue(variable,
                            out var register)
                        || !loopFlowState.Exists(item
                            => item.Register == register
                                && item.CarrierKind == 0
                                && item.Type == PrimitiveType.Bool))
                    {
                        hasCompleteLoopConditionState = false;
                        break;
                    }
                }

                if (loopFlowState.Count != (isIdentityLoop
                        ? 3 : topLevelLoopCount + 1)
                    || !hasCompleteLoopConditionState
                    || (isNativeIntLoop
                    && !loopFlowState.Exists(item
                        => item.Register == loopResultRegister
                            && item.CarrierKind == 0
                            && item.Type == PrimitiveType.NativeInt))
                    || (isIdentityLoop
                    && loopFlowIdentityRegisters.Exists(register
                        => !loopFlowState.Exists(item
                            => item.Register == register
                                && item.CarrierKind == 2
                                && item.Type
                                    == PrimitiveType.TypeIdentity))))
                {
                    throw Unsupported(
                    $"Function '{function.Name}' has an unsupported loop-carried query state.");
                }

                loopFlowHasStructuredExit |= hasLoopControlExit;
                var loopHeader = blockCodes.Count;
                loopFlowBranchBlock = loop.IsPostTested
                    ? loopHeader + (hasLoopControlExit ? 2 : 1) : loopHeader;
                loopFlowBodyBlock = loop.IsPostTested
                    ? loopHeader : loopHeader + 1;
                loopFlowBodyBranchBlock = hasLoopControlExit
                    ? loopFlowBodyBlock : -1;
                loopFlowUpdateBlock = hasLoopControlExit
                    ? loopFlowBodyBlock + 1 : loopFlowBodyBlock;
                loopFlowExitBlock
                    = loopHeader + (hasLoopControlExit ? 3 : 2);
                code.Add(0xE3);
                code.Add(0);
                var firstBlockAfterPreheader = loop.IsPostTested
                    ? loopFlowBodyBlock : loopFlowBranchBlock;
                code.Add(checked((byte)firstBlockAfterPreheader));
                code.Add(checked((byte)(firstBlockAfterPreheader >> 8)));

                for (var blockIndex = loopHeader;
                    blockIndex <= loopFlowExitBlock; blockIndex++)
                {
                    blockCodes.Add(new List<byte>());
                }

                code = blockCodes[loopFlowBranchBlock];
                code.Add(0xE4);
                code.Add((byte)conditionRegister);
                code.Add(0);
                code.Add(0);
                loopFlowEdges.Add((loopFlowBranchBlock,
                    loopFlowBodyBlock, loopFlowExitBlock));

                if (hasLoopControlExit)
                {
                    code = blockCodes[loopFlowBodyBranchBlock];
                    for (var assignmentIndex = 0;
                        assignmentIndex < loopPrefixAssignmentCount;
                        assignmentIndex++)
                    {
                        var (loopUpdate, destinationRegister)
                            = loopAssignments[assignmentIndex];
                        var updatedRegister
                            = EmitExpression(loopUpdate.Value);
                        if (updatedRegister != destinationRegister)
                        {
                            code.Add(0x10);
                            code.Add((byte)(destinationRegister
                                | (updatedRegister << 4)));
                        }
                    }

                    var breakConditionRegister
                        = EmitExpression(loopControlCondition!);
                    code.Add(0xE4);
                    code.Add((byte)breakConditionRegister);
                    code.Add(0);
                    code.Add(0);
                    var controlTarget = isBreakExit
                        ? loopFlowExitBlock : loopFlowBranchBlock;
                    loopFlowEdges.Add((loopFlowBodyBranchBlock,
                        controlTarget, loopFlowUpdateBlock));
                }

                code = blockCodes[loopFlowUpdateBlock];
                for (var assignmentIndex = loopPrefixAssignmentCount;
                    assignmentIndex < loopAssignments.Count;
                    assignmentIndex++)
                {
                    var (loopUpdate, destinationRegister)
                        = loopAssignments[assignmentIndex];
                    var updatedRegister = EmitExpression(loopUpdate.Value);
                    if (updatedRegister != destinationRegister)
                    {
                        code.Add(0x10);
                        code.Add((byte)(destinationRegister
                            | (updatedRegister << 4)));
                    }
                }
                code.Add(0xE3);
                code.Add(0);
                code.Add(checked((byte)loopFlowBranchBlock));
                code.Add(checked((byte)(loopFlowBranchBlock >> 8)));

                code = blockCodes[loopFlowExitBlock];
                continue;
            }

            if (statement is BoundAssignment assignment
                && (uint)assignment.Variable
                    < (uint)function.Variables.Length
                && function.Variables[assignment.Variable].IsMutable
                && localRegisters.ContainsKey(assignment.Variable)
                && assignment.Value.Type
                    == function.Variables[assignment.Variable].Type)
            {
                var assignedRegister = EmitExpression(assignment.Value);
                if (structuredLoopFlow)
                {
                    var destinationRegister
                        = localRegisters[assignment.Variable];
                    if (assignedRegister != destinationRegister)
                    {
                        code.Add(0x10);
                        code.Add(checked((byte)(destinationRegister
                            | (assignedRegister << 4))));
                    }
                }
                else
                {
                    localRegisters[assignment.Variable]
                        = assignedRegister;
                }
                continue;
            }

            if (statement is BoundExpressionStatement {
                    Expression: BoundCompoundAssignment {
                        Place: BoundRead placeRead,
                    } compound,
                }
                && (uint)placeRead.Variable
                    < (uint)function.Variables.Length
                && function.Variables[placeRead.Variable].IsMutable
                && function.Variables[placeRead.Variable].Type
                    == PrimitiveType.NativeInt
                && localRegisters.ContainsKey(placeRead.Variable))
            {
                localRegisters[placeRead.Variable]
                    = EmitExpression(compound);
                continue;
            }

            throw Unsupported(
                $"Function '{function.Name}' has unsupported {statement.GetType().Name} before its registered-query return.");
        }

        if (localRegisters.Count
            != function.Variables.Length - function.ParameterCount)
        {
            throw Unsupported(
                $"Function '{function.Name}' has an unrepresented local in its registered-query Body.");
        }

        int resultRegister;
        if (loopIdentityReturn is { } identityReturn)
        {
            if (loopFlowIdentityRegisters.Count != 2)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an incomplete loop-carried TypeIdentity pair.");
            }

            resultRegister = AllocateRegister();
            code.Add(0xC2);
            code.Add(identityReturn.Operator == TokenKind.EqualsEquals
                ? (byte)1 : (byte)2);
            code.Add((byte)(resultRegister | 0xF0));
            code.Add((byte)(loopFlowIdentityRegisters[0]
                | (loopFlowIdentityRegisters[1] << 4)));
        }
        else
        {
            resultRegister = EmitExpression(returned.Value);
        }

        if (returned.Value.Type != function.ReturnType)
        {
            throw Unsupported(
                $"Function '{function.Name}' has a registered-query result that does not match its signature.");
        }

        code.Add(0x17);
        code.Add((byte)resultRegister);

        var blocks = new List<TrRmRegisterBodyBlock>(blockCodes.Count);
        var mergeShapeCount = (identityMergeRegister >= 0 ? 1 : 0)
            + (queryValueFlowResultRegister >= 0 ? 1 : 0)
            + (loopFlowBranchBlock >= 0 ? 1 : 0);
        if (mergeShapeCount > 1)
        {
            throw Unsupported(
                $"Function '{function.Name}' has multiple registered-query join carriers.");
        }

        var conditionalEdgeCount = 0;
        if (queryValueFlowResultRegister >= 0)
        {
            conditionalEdgeCount = queryValueFlowEdges.Count;
        }
        else if (loopFlowBranchBlock >= 0 || identityMergeRegister >= 0)
        {
            conditionalEdgeCount = loopFlowBranchBlock >= 0
                ? loopFlowEdges.Count : 1;
        }
        var exceptionRecordIndexOffset
            = schema.UsesRegisterExceptionRegions() ? 1 : 0;
        var records = new List<ReadOnlyMemory<byte>>(
            blockCodes.Count + conditionalEdgeCount
                + exceptionRecordIndexOffset
                + exceptionExitRecords.Count);
        if (exceptionRecordIndexOffset != 0)
        {
            records.Add(ReadOnlyMemory<byte>.Empty);
        }
        for (var blockIndex = 0; blockIndex < blockCodes.Count;
            blockIndex++)
        {
            var entryState = new byte[8];
            entryState[0] = 0x01;
            records.Add(entryState);
        }

        if (identityMergeRegister >= 0)
        {
            var entryState = new byte[13];
            entryState[0] = 0x01;
            BinaryPrimitives.WriteUInt32LittleEndian(entryState.AsSpan(4), 1);
            entryState[8] = (byte)identityMergeRegister;
            entryState[9] = 2;
            records[3 + exceptionRecordIndexOffset] = entryState;
            var edge = new byte[8];
            edge[0] = 0x06;
            BinaryPrimitives.WriteUInt16LittleEndian(edge.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(edge.AsSpan(6), 2);
            records.Add(edge);
        }
        else if (loopFlowBranchBlock >= 0)
        {
            for (var blockIndex = 1; blockIndex < blockCodes.Count;
                blockIndex++)
            {
                var entryState = new byte[8 + (loopFlowState.Count * 5)];
                entryState[0] = 0x01;
                BinaryPrimitives.WriteUInt32LittleEndian(entryState.AsSpan(4),
                    checked((uint)loopFlowState.Count));
                for (var rowIndex = 0; rowIndex < loopFlowState.Count;
                    rowIndex++)
                {
                    var (register, carrierKind, type)
                        = loopFlowState[rowIndex];
                    var offset = 8 + (rowIndex * 5);
                    entryState[offset] = checked((byte)register);
                    entryState[offset + 1] = carrierKind;
                    if (carrierKind == 0)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            entryState.AsSpan(offset + 2),
                            GetTypeMapIndex(type));
                    }
                }
                if (!exceptionEmptyStateBlocks.Contains(blockIndex))
                {
                    records[blockIndex + exceptionRecordIndexOffset]
                        = entryState;
                }
            }

            loopFlowEdges.Sort(static (left, right)
                => left.BranchBlock.CompareTo(right.BranchBlock));
            foreach (var (branchBlock, trueBlock, falseBlock)
                in loopFlowEdges)
            {
                var branchCode = blockCodes[branchBlock];
                var branchInstructionOffset = branchCode.Count - 4;
                if (branchInstructionOffset < 0
                    || branchCode[branchInstructionOffset] != 0xE4)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a malformed conditional loop branch.");
                }

                var edgeRecordIndex = checked((ushort)records.Count);
                branchCode[branchInstructionOffset + 2]
                    = (byte)edgeRecordIndex;
                branchCode[branchInstructionOffset + 3]
                    = (byte)(edgeRecordIndex >> 8);
                var edge = new byte[8];
                edge[0] = 0x06;
                BinaryPrimitives.WriteUInt16LittleEndian(edge.AsSpan(4),
                    checked((ushort)trueBlock));
                BinaryPrimitives.WriteUInt16LittleEndian(edge.AsSpan(6),
                    checked((ushort)falseBlock));
                records.Add(edge);
            }
        }
        else if (queryValueFlowResultRegister >= 0)
        {
            if (queryValueFlowJoinBlocks.Count
                != queryValueFlowEdges.Count)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has inconsistent query-value join metadata.");
            }

            var typeMapIndex = GetTypeMapIndex(PrimitiveType.NativeInt);
            foreach (var joinBlock in queryValueFlowJoinBlocks)
            {
                var entryState = new byte[13];
                entryState[0] = 0x01;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    entryState.AsSpan(4), 1);
                entryState[8] = (byte)queryValueFlowResultRegister;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    entryState.AsSpan(10), typeMapIndex);
                records[joinBlock + exceptionRecordIndexOffset]
                    = entryState;
            }

            foreach (var (branchBlock, trueBlock, falseBlock)
                in queryValueFlowEdges)
            {
                var edgeRecordIndex = records.Count;
                var edgeRecord = checked((ushort)edgeRecordIndex);
                blockCodes[branchBlock][4] = (byte)edgeRecord;
                blockCodes[branchBlock][5] = (byte)(edgeRecord >> 8);
                var edge = new byte[8];
                edge[0] = 0x06;
                BinaryPrimitives.WriteUInt16LittleEndian(edge.AsSpan(4),
                    checked((ushort)trueBlock));
                BinaryPrimitives.WriteUInt16LittleEndian(edge.AsSpan(6),
                    checked((ushort)falseBlock));
                records.Add(edge);
            }
        }

        foreach (var (sourceBlock, instructionOffset, exitKind, hasTarget,
            targetBlock, valueKind, valueUse, valueIndex, regions)
            in exceptionExitRecords)
        {
            if (records.Count > ushort.MaxValue)
            {
                throw Unsupported(
                    $"Function '{function.Name}' exceeds the registered-query record-index limit.");
            }

            var recordIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodeRegionExit(
                exitKind, hasTarget, targetBlock, valueKind, valueUse,
                valueIndex, regions));
            var block = blockCodes[sourceBlock];
            if (recordIndex <= byte.MaxValue)
            {
                block[instructionOffset] = 0xB6;
                block[instructionOffset + 1] = (byte)recordIndex;
            }
            else
            {
                block.RemoveRange(instructionOffset, 2);
                block.InsertRange(instructionOffset,
                    [0xEA, 0, (byte)recordIndex,
                        (byte)(recordIndex >> 8)]);
            }
        }

        if (schema.UsesRegisterExceptionRegions())
        {
            if (exceptionRegions.Count == 1)
            {
                exceptionBlockRegions.Clear();
                for (var blockIndex = 0; blockIndex < blockCodes.Count;
                    blockIndex++)
                {
                    exceptionBlockRegions.Add(0);
                }
            }
            if (exceptionBlockRegions.Count != blockCodes.Count)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has unowned exception-region Blocks.");
            }

            var blockRegionBytes = new byte[blockCodes.Count
                * sizeof(ushort)];
            for (var blockIndex = 0; blockIndex < blockCodes.Count;
                blockIndex++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    blockRegionBytes.AsSpan(blockIndex * sizeof(ushort)),
                    exceptionBlockRegions[blockIndex]);
            }
            records[0] = TrRmRegisterBodyRecordWriter
                .EncodeExceptionDirectory(exceptionRegions,
                    exceptionHandlers, blockRegionBytes);
        }

        var exceptionHomeMap
            = new byte[exceptionHomeEntries.Count * 8];
        for (var homeIndex = 0;
            homeIndex < exceptionHomeEntries.Count; homeIndex++)
        {
            exceptionHomeEntries[homeIndex].CopyTo(exceptionHomeMap,
                homeIndex * 8);
        }

        var bodyCode = new List<byte>();
        for (var blockIndex = 0; blockIndex < blockCodes.Count;
            blockIndex++)
        {
            var block = blockCodes[blockIndex];
            var codeOffset = bodyCode.Count;
            bodyCode.AddRange(block);
            blocks.Add(new TrRmRegisterBodyBlock(
                checked((uint)codeOffset), checked((uint)block.Count),
                checked((ushort)(blockIndex
                    + exceptionRecordIndexOffset))));
        }

        return TrRmRegisterBodyWriter.Encode(exceptionHomeMap,
            typeMap.Encode(), default, fieldMap.Encode(), default,
            blocks, records, bodyCode.ToArray(), schema: schema);
    }

    private readonly record struct StandardRegisteredQueryRelation(
        BoundExpression Left, BoundExpression Right,
        bool IsTypeIdentity = false);

    private sealed record StandardRegisteredQueryLoopRegion(
        int Header, HashSet<int> Members, int[] Latches, int[] ExitTargets);

    private sealed record StandardRegisteredQueryLoopControlFlow(
        int[][] Successors, int[][] Predecessors, HashSet<int>[] LiveIn,
        ImmutableArray<StandardRegisteredQueryLoopRegion> Loops);

    private static ImmutableArray<BoundStatement> DecodeStandardRegisteredQueryBody(
        TrRmRegisterBodyReader body, BoundFunction function,
        ImmutableArray<BoundVariable>.Builder variables,
        TrRmTypeDefTableReader typeDefs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFieldDefTableReader fields, TrRmFieldPathTableReader fieldPaths,
        TrRmImportTableReader imports,
        List<NativePointerTypeDefinition> nativePointerTypes,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef)
    {
        if (body.BlockCount == 8 && body.RecordCount == 11
            && function.ReturnType == PrimitiveType.NativeInt
            && body.GetBlockCode(1).Length == 4
            && body.GetBlockCode(1)[0] == 0xE4
            && body.GetBlockCode(2).Length >= 6
            && body.GetBlockCode(2)[^4] == 0xE4
            && body.GetBlockCode(3).Length >= 4
            && body.GetBlockCode(3)[^4] == 0xE3
            && BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlockCode(3)[^2..]) == 1
            && body.GetBlockCode(4).Length == 4
            && body.GetBlockCode(4)[0] == 0xE3
            && BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlockCode(4)[2..]) == 5
            && body.GetBlockCode(5).Length == 4
            && body.GetBlockCode(5)[0] == 0xE4
            && body.GetBlockCode(6).Length >= 8
            && body.GetBlockCode(6)[^4] == 0xE3
            && BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlockCode(6)[^2..]) == 5
            && body.GetBlockCode(7).Length == 2
            && body.GetBlockCode(7)[0] == 0x17)
        {
            return DecodeStandardRegisteredQueryFirstSiblingTransferBody(
                body, function, variables, typeDefs, typeSpecs, fields,
                fieldPaths, imports, nativePointerTypes,
                signatureIndexByAnchorMethod, structureTypeByTypeDef);
        }

        if (body.BlockCount == 8 && body.RecordCount == 11
            && function.ReturnType == PrimitiveType.NativeInt
            && body.GetBlockCode(1).Length == 6
            && body.GetBlockCode(1)[0] == 0x11
            && body.GetBlockCode(1)[2] == 0xE4
            && body.GetBlockCode(4).Length == 4
            && body.GetBlockCode(4)[0] == 0xE3
            && BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlockCode(4)[2..]) == 5
            && body.GetBlockCode(7).Length == 2
            && body.GetBlockCode(7)[0] == 0x17)
        {
            return DecodeStandardRegisteredQueryFirstSiblingTransferBody(
                body, function, variables, typeDefs, typeSpecs, fields,
                fieldPaths, imports, nativePointerTypes,
                signatureIndexByAnchorMethod, structureTypeByTypeDef);
        }

        if (body.BlockCount == 8 && body.RecordCount == 11
            && function.ReturnType == PrimitiveType.NativeInt
            && body.GetBlockCode(3).Length == 4
            && body.GetBlockCode(3)[0] == 0xE3
            && BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlockCode(3)[2..]) == 4
            && body.GetBlockCode(4).Length == 6
            && body.GetBlockCode(4)[0] == 0x11
            && body.GetBlockCode(4)[2] == 0xE4
            && body.GetBlockCode(6).Length == 4
            && body.GetBlockCode(6)[0] == 0xE4
            && body.GetBlockCode(7).Length == 2
            && body.GetBlockCode(7)[0] == 0x17)
        {
            return DecodeStandardRegisteredQuerySiblingTransferBody(body,
                function, variables, typeDefs, typeSpecs, fields, fieldPaths,
                imports, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        }

        if (body.BlockCount == 8 && body.RecordCount == 11
            && function.ReturnType == PrimitiveType.NativeInt
            && body.GetBlockCode(3).Length == 4
            && body.GetBlockCode(3)[0] == 0xE3
            && BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlockCode(3)[2..]) == 4
            && body.GetBlockCode(4).Length == 4
            && body.GetBlockCode(4)[0] == 0xE4
            && body.GetBlockCode(5).Length >= 6
            && body.GetBlockCode(5)[^4] == 0xE4
            && body.GetBlockCode(6).Length >= 4
            && body.GetBlockCode(6)[^4] == 0xE3
            && body.GetBlockCode(7).Length == 2
            && body.GetBlockCode(7)[0] == 0x17)
        {
            return DecodeStandardRegisteredQuerySiblingTransferBody(body,
                function, variables, typeDefs, typeSpecs, fields, fieldPaths,
                imports, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        }

        if (body.BlockCount == 7 && body.RecordCount == 10
            && function.ReturnType == PrimitiveType.NativeInt
            && body.GetBlockCode(1).Length == 4
            && body.GetBlockCode(1)[0] == 0xE4
            && body.GetBlockCode(2).Length == 4
            && body.GetBlockCode(2)[0] == 0xE4
            && body.GetBlockCode(3).Length >= 6
            && body.GetBlockCode(3)[^6] == 0x11
            && body.GetBlockCode(3)[^4] == 0xE4
            && body.GetBlockCode(4).Length >= 4
            && body.GetBlockCode(4)[^4] == 0xE3
            && body.GetBlockCode(5).Length >= 8
            && body.GetBlockCode(5)[^4] == 0xE3
            && body.GetBlockCode(6).Length == 2
            && body.GetBlockCode(6)[0] == 0x17)
        {
            return DecodeStandardRegisteredQueryNestedLoopBody(body,
                function, variables, typeDefs, typeSpecs, fields, fieldPaths,
                imports, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, hasInnerControlExit: true);
        }

        if (body.BlockCount == 7 && body.RecordCount == 9
            && function.ReturnType == PrimitiveType.NativeInt
            && (body.GetBlockCode(1)[0] == 0xE4
                || body.GetBlockCode(2)[0] == 0xE4)
            && body.GetBlockCode(3).Length == 4
            && body.GetBlockCode(3)[0] == 0xE3
            && BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlockCode(3)[2..]) == 4
            && (body.GetBlockCode(4)[0] == 0xE4
                || body.GetBlockCode(5)[0] == 0xE4)
            && body.GetBlockCode(6).Length == 2
            && body.GetBlockCode(6)[0] == 0x17)
        {
            return DecodeStandardRegisteredQuerySiblingLoopBody(body,
                function, variables, typeDefs, typeSpecs, fields, fieldPaths,
                imports, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        }

        if (body.BlockCount == 6
            && function.ReturnType == PrimitiveType.NativeInt
            && (body.GetBlockCode(1)[0] == 0xE4
                || (body.GetBlockCode(4).Length >= 4
                    && body.GetBlockCode(4)[^4] == 0xE4))
            && (body.GetBlockCode(2)[0] == 0xE4
                || body.GetBlockCode(3)[0] == 0xE4))
        {
            return DecodeStandardRegisteredQueryNestedLoopBody(body,
                function, variables, typeDefs, typeSpecs, fields, fieldPaths,
                imports, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, hasInnerControlExit: false);
        }

        if (body.BlockCount is 4 or 5
            && HasStandardRegisteredQueryLoopBranch(body))
        {
            if (function.ReturnType == PrimitiveType.Bool
                && body.BlockCount == 4)
            {
                return DecodeStandardRegisteredQueryIdentityLoopBody(body,
                    function, variables, typeDefs, typeSpecs, fields,
                    fieldPaths, imports, nativePointerTypes,
                    signatureIndexByAnchorMethod, structureTypeByTypeDef);
            }

            if (function.ReturnType == PrimitiveType.NativeInt)
            {
                if (body.BlockCount == 5)
                {
                    return DecodeStandardRegisteredQueryLoopControlBody(body,
                        function, variables, typeDefs, typeSpecs, fields,
                        fieldPaths, imports, nativePointerTypes,
                        signatureIndexByAnchorMethod,
                        structureTypeByTypeDef);
                }

                return DecodeStandardRegisteredQueryLoopBody(body, function,
                    variables, typeDefs, typeSpecs, fields, fieldPaths,
                    imports, nativePointerTypes,
                    signatureIndexByAnchorMethod, structureTypeByTypeDef);
            }

            throw Unsupported(
                "The bounded Standard query loop has an unsupported result type or loop form.");
        }

        if (body.BlockCount > 1)
        {
            if (function.ReturnType is PrimitiveType.NativeInt
                or PrimitiveType.Bool)
            {
                if (HasStandardRegisteredQueryBackwardEdge(body))
                {
                    var controlFlow
                        = AnalyzeStandardRegisteredQueryLoopControlFlow(
                            body);
                    if (RequiresStructuredRegisteredQueryLoopDecoder(
                            body, controlFlow)
                        || function.ReturnType == PrimitiveType.Bool)
                    {
                        return DecodeStandardRegisteredQueryStructuredLoopBody(
                            body, function, variables, typeDefs, typeSpecs,
                            fields, fieldPaths, imports, nativePointerTypes,
                            signatureIndexByAnchorMethod,
                            structureTypeByTypeDef);
                    }
                }

                if (function.ReturnType == PrimitiveType.NativeInt)
                {
                    return DecodeStandardRegisteredQueryValueControlFlow(
                        body, function, typeDefs, typeSpecs, fields,
                        fieldPaths, imports, nativePointerTypes,
                        signatureIndexByAnchorMethod,
                        structureTypeByTypeDef);
                }
            }

            if (body.BlockCount == 4)
            {
                return DecodeStandardRegisteredQueryIdentityDiamond(body,
                    function, typeDefs, typeSpecs, fields, fieldPaths,
                    imports, nativePointerTypes, signatureIndexByAnchorMethod,
                    structureTypeByTypeDef);
            }
        }

        if (body.BlockCount != 1 || body.RecordCount != 1
            || body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || (body.MapMask & ~0x0A) != 0)
        {
            throw Unsupported(
                "The bounded Standard registered-query consumer requires one source-free query Block.");
        }

        var entryState = body.GetRecord(0);
        if (entryState.Length != 8 || entryState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Invalid("A registered-query Body requires an empty BlockEntryState.");
        }

        var registers = new BoundExpression?[15];
        var relationValues = new StandardRegisteredQueryRelation?[15];
        var defined = new bool[15];
        var used = new bool[15];
        var code = body.GetBlockCode(0);
        BoundExpression? result = null;
        for (var position = 0; position < code.Length;)
        {
            var opcode = code[position];
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                opcode, body.Schema);
            switch (opcode)
            {
                case 0x10:
                {
                    var registersPair = code[position + 1];
                    var destination = registersPair & 0x0F;
                    var source = registersPair >> 4;
                    if (destination >= 14 || source >= 14)
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires mov to alias an available query value.");
                    }

                    used[source] = true;
                    if (relationValues[source] is
                        StandardRegisteredQueryRelation relationValue)
                    {
                        registers[destination] = null;
                        relationValues[destination] = relationValue;
                    }
                    else if (registers[source] is BoundExpression value)
                    {
                        registers[destination] = value;
                        relationValues[destination] = null;
                    }
                    else
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires mov to alias an available query value.");
                    }

                    defined[destination] = true;
                    break;
                }
                case 0xDE:
                {
                    var control = code[position + 1];
                    var queryKind = control >> 4;
                    var destination = control & 0x0F;
                    var mapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]);
                    BoundExpression query;
                    if (queryKind is 1 or 2 or 5)
                    {
                        var type = ReadStandardRegisteredQueryType(
                            body, mapIndex, typeSpecs, nativePointerTypes,
                            signatureIndexByAnchorMethod,
                            structureTypeByTypeDef, imports);
                        var subject = new BoundQuerySubject(type,
                            BoundQuerySubjectKind.Type, null, null, false,
                            false);
                        query = queryKind == 5
                            ? new BoundTypeIdentityQuery(subject, default)
                            : new BoundLayoutQuery(
                                queryKind == 1
                                    ? BoundLayoutQueryKind.SizeOf
                                    : BoundLayoutQueryKind.AlignOf,
                                subject, default);
                    }
                    else
                    {
                        if (mapIndex >= body.FieldCount)
                        {
                            throw Invalid(
                                "A registered-query FieldMap index is outside its table.");
                        }

                        var fieldMapToken = BinaryPrimitives
                            .ReadUInt32LittleEndian(body.FieldMap[
                                (mapIndex * sizeof(uint))..]);
                        var path = ReadStandardRegisteredQueryFieldPath(
                            fieldMapToken, typeDefs, typeSpecs, fields,
                            fieldPaths, imports, nativePointerTypes,
                            signatureIndexByAnchorMethod,
                            structureTypeByTypeDef);
                        var isOffsetOfPlace = queryKind == 4;
                        if (isOffsetOfPlace && path.Steps.Length != 1)
                        {
                            throw Invalid(
                                "OffsetOfPlace requires an immediate one-field path.");
                        }

                        var subject = isOffsetOfPlace
                            ? new BoundQuerySubject(path.LeafType,
                                BoundQuerySubjectKind.FieldPathPlace, null,
                                path, false, false)
                            : new BoundQuerySubject(path.RootType,
                                BoundQuerySubjectKind.Type, null, path, false,
                                false);
                        query = new BoundLayoutQuery(
                            isOffsetOfPlace
                                ? BoundLayoutQueryKind.OffsetOfPlace
                                : BoundLayoutQueryKind.OffsetOfType,
                            subject, default);
                    }

                    if (destination != 15)
                    {
                        registers[destination] = query;
                        relationValues[destination] = null;
                        defined[destination] = true;
                    }
                    break;
                }
                case 0xD0:
                {
                    var control = code[position + 1];
                    var sourceRegister = control & 0x0F;
                    var destination = control >> 4;
                    var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]);
                    if (sourceRegister >= 14 || destination >= 14
                        || registers[sourceRegister]
                            is not BoundExpression source
                        || source.Type != PrimitiveType.NativeInt)
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires a native-int source for D0 conversion.");
                    }

                    var targetType = ReadStandardRegisteredQueryType(body,
                        typeMapIndex, typeSpecs, nativePointerTypes,
                        signatureIndexByAnchorMethod,
                        structureTypeByTypeDef, imports);
                    if (targetType != PrimitiveType.Int32)
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer supports only native-int to int32 D0 conversion.");
                    }

                    used[sourceRegister] = true;
                    registers[destination] = new BoundCast(source, targetType,
                        default);
                    relationValues[destination] = null;
                    defined[destination] = true;
                    break;
                }
                case 0xC0:
                {
                    var operation = code[position + 1];
                    var payload = BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]);
                    var destination = payload & 0x0F;
                    var unusedHome = (payload >> 4) & 0x0F;
                    var leftRegister = (payload >> 8) & 0x0F;
                    var rightRegister = payload >> 12;
                    if (destination >= 14 || unusedHome != 15
                        || leftRegister >= 14 || rightRegister >= 14
                        || registers[leftRegister]
                            is not BoundExpression left
                        || registers[rightRegister]
                            is not BoundExpression right
                        || left.Type != PrimitiveType.NativeInt
                        || right.Type != PrimitiveType.NativeInt)
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires C0 NativeInt query operands.");
                    }

                    var (binaryOperator, overflowBehavior) = operation switch {
                        0x01 => (TokenKind.Plus,
                            BoundIntegerOverflowBehavior.Checked),
                        0x02 => (TokenKind.Minus,
                            BoundIntegerOverflowBehavior.Checked),
                        0x03 => (TokenKind.Star,
                            BoundIntegerOverflowBehavior.Checked),
                        0x05 => (TokenKind.Plus,
                            BoundIntegerOverflowBehavior.Wrapping),
                        0x06 => (TokenKind.Minus,
                            BoundIntegerOverflowBehavior.Wrapping),
                        0x07 => (TokenKind.Star,
                            BoundIntegerOverflowBehavior.Wrapping),
                        0x0C => (TokenKind.Slash,
                            BoundIntegerOverflowBehavior.Checked),
                        0x0D => (TokenKind.Percent,
                            BoundIntegerOverflowBehavior.Checked),
                        _ => throw Unsupported(
                            "The bounded Standard query consumer rejects this C0 operation."),
                    };
                    used[leftRegister] = true;
                    used[rightRegister] = true;
                    registers[destination] = new BoundBinary(
                        binaryOperator, left, right, PrimitiveType.NativeInt,
                        default) {
                        OverflowBehavior = overflowBehavior,
                    };
                    relationValues[destination] = null;
                    defined[destination] = true;
                    break;
                }
                case 0xC2:
                {
                    var control = code[position + 1];
                    var resultByte = code[position + 2];
                    var resultRegister = resultByte & 0x0F;
                    var leftRegister = code[position + 3] & 0x0F;
                    var rightRegister = code[position + 3] >> 4;
                    if (control is not (0 or 1 or 2)
                        || resultRegister >= 14
                        || leftRegister >= 14 || rightRegister >= 14
                        || (resultByte & 0xF0) != 0xF0
                        || registers[leftRegister] is not BoundExpression left
                        || registers[rightRegister] is not BoundExpression right
                        || left.Type != right.Type)
                    {
                        throw Unsupported(
                            "The bounded Standard registered-query consumer requires supported C2 query operands.");
                    }

                    used[leftRegister] = true;
                    used[rightRegister] = true;
                    if (control == 0)
                    {
                        if (left.Type != right.Type
                            || (left.Type != PrimitiveType.TypeIdentity
                                && left.Type != PrimitiveType.NativeInt))
                        {
                            throw Unsupported(
                                "The bounded Standard query consumer requires matching TypeIdentity or NativeInt C2 relation operands.");
                        }

                        if (left.Type == PrimitiveType.TypeIdentity
                            && TryCreateStandardTypeIdentityRelation(left,
                                right, out var relation))
                        {
                            registers[resultRegister] = relation;
                            relationValues[resultRegister] = null;
                        }
                        else
                        {
                            registers[resultRegister] = null;
                            relationValues[resultRegister]
                                = new StandardRegisteredQueryRelation(left,
                                    right,
                                    left.Type == PrimitiveType.TypeIdentity);
                        }
                    }
                    else
                    {
                        if (left.Type is not (PrimitiveType.TypeIdentity
                            or PrimitiveType.NativeInt))
                        {
                            throw Unsupported(
                                "The bounded Standard query consumer requires a TypeIdentity or native-int C2 comparison.");
                        }

                        registers[resultRegister] = new BoundBinary(
                            control == 1 ? TokenKind.EqualsEquals
                                : TokenKind.BangEquals,
                            left, right, PrimitiveType.Bool, default);
                        relationValues[resultRegister] = null;
                    }
                    defined[resultRegister] = true;
                    break;
                }
                case 0xC3:
                {
                    var predicate = code[position + 1];
                    var payload = BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]);
                    var destination = payload & 0x0F;
                    var conditionRegister = (payload >> 4) & 0x0F;
                    var unused0 = (payload >> 8) & 0x0F;
                    var unused1 = payload >> 12;
                    if (destination >= 14 || conditionRegister >= 14
                        || unused0 != 15 || unused1 != 15)
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires C3 to test an available TypeIdentity relation result.");
                    }

                    BoundExpression condition;
                    if (relationValues[conditionRegister]
                        is StandardRegisteredQueryRelation relationValue)
                    {
                        if (!TryCreateStandardRelationCondition(
                            relationValue, predicate, out condition))
                        {
                            throw Unsupported(
                                "The bounded Standard query consumer requires C3 to test an available ordered query relation.");
                        }

                        relationValues[conditionRegister] = null;
                    }
                    else if (registers[conditionRegister]
                            is not BoundExpression relation
                        || !TryCreateStandardPredicateCondition(relation,
                            predicate, out condition))
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires C3 to test an available TypeIdentity relation result.");
                    }

                    used[conditionRegister] = true;
                    registers[destination] = condition;
                    relationValues[destination] = null;
                    defined[destination] = true;
                    break;
                }
                case 0xC4:
                {
                    var predicate = code[position + 1];
                    var payload = BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]);
                    var destination = payload & 0x0F;
                    var conditionRegister = (payload >> 4) & 0x0F;
                    var trueRegister = (payload >> 8) & 0x0F;
                    var falseRegister = payload >> 12;
                    if (destination >= 14 || conditionRegister >= 14
                        || trueRegister >= 14 || falseRegister >= 14)
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires C4 to select between available query values.");
                    }

                    BoundExpression condition;
                    if (relationValues[conditionRegister]
                        is StandardRegisteredQueryRelation relationValue)
                    {
                        if (!TryCreateStandardRelationCondition(
                            relationValue, predicate, out condition))
                        {
                            throw Unsupported(
                                "The bounded Standard query consumer requires C4 to test a supported query relation.");
                        }

                        relationValues[conditionRegister] = null;
                    }
                    else if (registers[conditionRegister]
                            is not BoundExpression relation
                        || !TryCreateStandardPredicateCondition(relation,
                            predicate, out condition))
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires C4 to select between available query values.");
                    }

                    if (registers[trueRegister]
                            is not BoundExpression whenTrue
                        || registers[falseRegister]
                            is not BoundExpression whenFalse
                        || whenTrue.Type != whenFalse.Type)
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires C4 to select between available query values.");
                    }

                    used[conditionRegister] = true;
                    used[trueRegister] = true;
                    used[falseRegister] = true;
                    registers[destination] = new BoundConditional(condition,
                        whenTrue, whenFalse, whenTrue.Type, default);
                    relationValues[destination] = null;
                    defined[destination] = true;
                    break;
                }
                case 0xC5:
                {
                    var predicate = code[position + 1];
                    var payload = BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]);
                    var destination = payload & 0x0F;
                    var conditionRegister = (payload >> 4) & 0x0F;
                    var valueRegister = (payload >> 8) & 0x0F;
                    var unused = payload >> 12;
                    if (destination >= 14 || conditionRegister >= 14
                        || valueRegister >= 14 || unused != 15)
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires C5 to retain an available query value.");
                    }

                    BoundExpression condition;
                    if (relationValues[conditionRegister]
                        is StandardRegisteredQueryRelation relationValue)
                    {
                        if (!TryCreateStandardRelationCondition(
                            relationValue, predicate, out condition))
                        {
                            throw Unsupported(
                                "The bounded Standard query consumer requires C5 to test a supported query relation.");
                        }

                        relationValues[conditionRegister] = null;
                    }
                    else if (registers[conditionRegister]
                            is not BoundExpression relation
                        || !TryCreateStandardPredicateCondition(relation,
                            predicate, out condition))
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires C5 to retain an available query value.");
                    }

                    if (registers[destination]
                            is not BoundExpression previousValue
                        || registers[valueRegister]
                            is not BoundExpression selectedValue
                        || previousValue.Type != selectedValue.Type)
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires C5 to retain an available query value.");
                    }

                    used[destination] = true;
                    used[conditionRegister] = true;
                    used[valueRegister] = true;
                    registers[destination] = new BoundConditional(condition,
                        selectedValue, previousValue, previousValue.Type,
                        default);
                    relationValues[destination] = null;
                    defined[destination] = true;
                    break;
                }
                case 0x40:
                {
                    var registersPair = code[position + 1];
                    var destination = registersPair & 0x0F;
                    var rightRegister = registersPair >> 4;
                    if (destination >= 14 || rightRegister >= 14
                        || registers[destination] is not BoundExpression left
                        || registers[rightRegister]
                            is not BoundExpression right
                        || left.Type != right.Type
                        || left.Type != PrimitiveType.TypeIdentity
                        || !TryCreateStandardTypeIdentityRelation(left,
                            right, out var relation))
                    {
                        throw Unsupported(
                            "The bounded Standard query consumer requires short 40 to compare supported TypeIdentity query values.");
                    }

                    used[destination] = true;
                    used[rightRegister] = true;
                    registers[destination] = relation;
                    relationValues[destination] = null;
                    defined[destination] = true;
                    break;
                }
                case 0x17:
                {
                    if (position + instructionLength != code.Length)
                    {
                        throw Invalid(
                            "A registered-query Body must end with one Return.");
                    }

                    var resultRegister = code[position + 1];
                    if (resultRegister > 13
                        || registers[resultRegister] is not BoundExpression value)
                    {
                        throw Unsupported(
                            "A registered-query Body Return does not name a decoded query result.");
                    }

                    used[resultRegister] = true;
                    result = value;
                    break;
                }
                default:
                    throw Unsupported(
                        $"The bounded Standard registered-query consumer does not admit opcode 0x{opcode:X2}.");
            }

            position += instructionLength;
        }

        if (result is null || result.Type != function.ReturnType)
        {
            throw Invalid(
                "A registered-query Body result does not match its MethodSig.");
        }

        for (var register = 0; register < defined.Length; register++)
        {
            if (defined[register] && !used[register])
            {
                throw Unsupported(
                    "The bounded Standard registered-query consumer does not admit an unused query result.");
            }
        }

        return [new BoundReturn(result, default)];
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryIdentityLoopBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (body.BlockCount != 4 || body.RecordCount != 5
            || body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || body.MapMask != 0x02
            || function.ReturnType != PrimitiveType.Bool
            || function.ParameterCount < 2
            || function.Variables.Length != function.ParameterCount
            || variables.Count != function.ParameterCount)
        {
            throw Unsupported(
                "The bounded Standard TypeIdentity loop requires two Boolean formals and a Boolean result.");
        }

        _ = ValidateStandardRegisteredQueryLoopRegion(
            AnalyzeStandardRegisteredQueryLoopControlFlow(body),
            body.BlockCount);

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A registered-query identity-loop BlockEntryState is not in block order.");
            }
        }

        var preheaderEntry = body.GetRecord(0);
        if (preheaderEntry.Length != 8 || preheaderEntry[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(preheaderEntry[4..])
                != 0)
        {
            throw Invalid(
                "A registered-query identity-loop preheader must have an empty entry state.");
        }

        var preheaderCode = body.GetBlockCode(0);
        var firstLoopCode = body.GetBlockCode(1);
        var secondLoopCode = body.GetBlockCode(2);
        var isPostTested = firstLoopCode[0] == 0x11
            && secondLoopCode[0] == 0xE4;
        var headerBlock = isPostTested ? 2 : 1;
        var loopBodyBlock = isPostTested ? 1 : 2;
        var headerCode = isPostTested ? secondLoopCode : firstLoopCode;
        var loopBodyCode = isPostTested ? firstLoopCode : secondLoopCode;
        var exitCode = body.GetBlockCode(3);
        if (preheaderCode.Length != 14 || preheaderCode[0] != 0x11
            || preheaderCode[2] != 0xDE
            || (preheaderCode[3] >> 4) != 5
            || preheaderCode[6] != 0xDE
            || (preheaderCode[7] >> 4) != 5
            || preheaderCode[10] != 0xE3 || preheaderCode[11] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(preheaderCode[12..])
                != 1
            || headerCode.Length != 4 || headerCode[0] != 0xE4
            || loopBodyCode.Length != 8 || loopBodyCode[0] != 0x11
            || loopBodyCode[2] != 0x10 || loopBodyCode[4] != 0xE3
            || loopBodyCode[5] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(loopBodyCode[6..])
                != headerBlock
            || exitCode.Length != 6 || exitCode[0] != 0xC2
            || exitCode[1] is not (1 or 2) || exitCode[4] != 0x17)
        {
            throw Unsupported(
                "The registered-query TypeIdentity loop is outside the bounded preheader/header/body/exit form.");
        }

        var conditionRegister = preheaderCode[1] & 0x0F;
        var conditionParameter = preheaderCode[1] >> 4;
        var leftIdentityRegister = preheaderCode[3] & 0x0F;
        var rightIdentityRegister = preheaderCode[7] & 0x0F;
        var updateParameter = loopBodyCode[1] >> 4;
        var temporaryRegister = loopBodyCode[1] & 0x0F;
        var comparisonPayload = BinaryPrimitives.ReadUInt16LittleEndian(
            exitCode[2..]);
        var resultRegister = comparisonPayload & 0x0F;
        if (conditionRegister >= 14 || conditionParameter
                >= function.ParameterCount
            || function.Variables[conditionParameter].Type
                != PrimitiveType.Bool
            || headerCode[1] != conditionRegister
            || temporaryRegister >= 14
            || updateParameter >= function.ParameterCount
            || function.Variables[updateParameter].Type
                != PrimitiveType.Bool
            || (loopBodyCode[3] & 0x0F) != conditionRegister
            || (loopBodyCode[3] >> 4) != temporaryRegister
            || leftIdentityRegister >= 14 || rightIdentityRegister >= 14
            || conditionRegister >= leftIdentityRegister
            || leftIdentityRegister >= rightIdentityRegister
            || resultRegister >= 14
            || ((comparisonPayload >> 4) & 0x0F) != 0x0F
            || ((comparisonPayload >> 8) & 0x0F)
                != leftIdentityRegister
            || (comparisonPayload >> 12) != rightIdentityRegister
            || exitCode[5] != resultRegister)
        {
            throw Invalid(
                "A registered-query identity loop changes a formal or TypeIdentity register identity.");
        }

        var edgeRecordIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(headerCode[2..]);
        var edge = body.GetRecord(edgeRecordIndex);
        if (edgeRecordIndex != 4 || edge.Length != 8 || edge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[4..])
                != loopBodyBlock
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]) != 3)
        {
            throw Invalid(
                "A registered-query identity loop has invalid conditional exit/body edges.");
        }

        var leftIdentity = ReadStandardRegisteredQueryIdentityInstruction(
            body, preheaderCode.Slice(2, 4), leftIdentityRegister,
            typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, imports);
        var rightIdentity = ReadStandardRegisteredQueryIdentityInstruction(
            body, preheaderCode.Slice(6, 4), rightIdentityRegister,
            typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, imports);

        var headerState = body.GetRecord(1);
        var bodyState = body.GetRecord(2);
        var exitState = body.GetRecord(3);
        if (headerState.Length != 23 || bodyState.Length != 23
            || exitState.Length != 23 || headerState[0] != 0x01
            || bodyState[0] != 0x01 || exitState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(headerState[4..]) != 3
            || BinaryPrimitives.ReadUInt32LittleEndian(bodyState[4..]) != 3
            || BinaryPrimitives.ReadUInt32LittleEndian(exitState[4..]) != 3
            || headerState[8] != conditionRegister
            || headerState[9] != 0 || headerState[12] != 0
            || headerState[13] != leftIdentityRegister
            || headerState[14] != 2
            || BinaryPrimitives.ReadUInt16LittleEndian(
                headerState[15..]) != 0
            || headerState[17] != 0
            || headerState[18] != rightIdentityRegister
            || headerState[19] != 2
            || BinaryPrimitives.ReadUInt16LittleEndian(
                headerState[20..]) != 0
            || headerState[22] != 0
            || !headerState.SequenceEqual(bodyState)
            || !headerState.SequenceEqual(exitState))
        {
            throw Invalid(
                "A registered-query identity loop has inconsistent loop-carried BlockEntryState rows.");
        }

        var conditionTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(headerState[10..]);
        if (ReadStandardRegisteredQueryType(body, conditionTypeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports) != PrimitiveType.Bool)
        {
            throw Invalid(
                "A registered-query identity loop has a non-Boolean condition carrier.");
        }

        var conditionVariable = variables.Count;
        var conditionName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_loop_condition_");
        variables.Add(new BoundVariable(conditionName,
            PrimitiveType.Bool, true, default));
        var returnOperator = exitCode[1] == 1
            ? TokenKind.EqualsEquals : TokenKind.BangEquals;
        var identityComparison = new BoundBinary(returnOperator,
            leftIdentity, rightIdentity, PrimitiveType.Bool, default);
        return [
            new BoundLocal(conditionVariable,
                new BoundRead(conditionParameter, PrimitiveType.Bool,
                    default), default),
            new BoundWhile(
                new BoundRead(conditionVariable, PrimitiveType.Bool, default),
                new BoundBlock([
                    new BoundAssignment(conditionVariable,
                        new BoundRead(updateParameter,
                            PrimitiveType.Bool, default), default),
                ], default), default) {
                IsPostTested = isPostTested,
            },
            new BoundReturn(identityComparison, default),
        ];
    }

    private static bool HasStandardRegisteredQueryLoopBranch(
        TrRmRegisterBodyReader body)
    {
        for (var blockIndex = 1; blockIndex < body.BlockCount - 1;
            blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            if ((code.Length == 4 && code[0] == 0xE4)
                || (code.Length == 6 && code[0] == 0x11
                    && code[2] == 0xE4))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryLoopBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (body.BlockCount != 4 || body.RecordCount != 5
            || body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || (body.MapMask & ~0x0A) != 0
            || (body.MapMask & 0x02) == 0
            || function.ReturnType != PrimitiveType.NativeInt
            || function.ParameterCount < 2
            || function.Variables.Length != function.ParameterCount
            || variables.Count != function.ParameterCount)
        {
            throw Unsupported(
                "The bounded Standard query loop requires two Boolean formals and a NativeInt result.");
        }

        _ = ValidateStandardRegisteredQueryLoopRegion(
            AnalyzeStandardRegisteredQueryLoopControlFlow(body),
            body.BlockCount);

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A registered-query loop BlockEntryState is not in block order.");
            }
        }

        var preheaderEntry = body.GetRecord(0);
        if (preheaderEntry.Length != 8 || preheaderEntry[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(preheaderEntry[4..])
                != 0)
        {
            throw Invalid(
                "A registered-query loop preheader must have an empty entry state.");
        }

        var preheaderCode = body.GetBlockCode(0);
        var firstLoopCode = body.GetBlockCode(1);
        var secondLoopCode = body.GetBlockCode(2);
        var isPostTested = firstLoopCode[0] == 0x11
            && secondLoopCode[0] == 0xE4;
        var headerBlock = isPostTested ? 2 : 1;
        var loopBodyBlock = isPostTested ? 1 : 2;
        var headerCode = isPostTested ? secondLoopCode : firstLoopCode;
        var loopBodyCode = isPostTested ? firstLoopCode : secondLoopCode;
        var exitCode = body.GetBlockCode(3);
        if (preheaderCode.Length != 10 || preheaderCode[0] != 0x11
            || preheaderCode[2] != 0xDE || preheaderCode[6] != 0xE3
            || preheaderCode[7] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(preheaderCode[8..])
                != 1
            || headerCode.Length != 4 || headerCode[0] != 0xE4
            || loopBodyCode.Length < 4
            || loopBodyCode[^4] != 0xE3 || loopBodyCode[^3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(loopBodyCode[^2..])
                != headerBlock
            || exitCode.Length != 2 || exitCode[0] != 0x17)
        {
            throw Unsupported(
                "The registered-query loop is outside the bounded preheader/header/body/exit form.");
        }

        var conditionRegister = preheaderCode[1] & 0x0F;
        var conditionParameter = preheaderCode[1] >> 4;
        var resultRegister = preheaderCode[3] & 0x0F;
        var queryInstruction = preheaderCode.Slice(2, 4);
        if (conditionRegister >= 14 || resultRegister >= 14
            || conditionRegister == resultRegister
            || conditionParameter >= function.ParameterCount
            || function.Variables[conditionParameter].Type
                != PrimitiveType.Bool
            || headerCode[1] != conditionRegister
            || exitCode[1] != resultRegister)
        {
            throw Invalid(
                "A registered-query loop changes a formal, result or loop-carried register identity.");
        }

        var edgeRecordIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(headerCode[2..]);
        var edge = body.GetRecord(edgeRecordIndex);
        if (edgeRecordIndex != 4 || edge.Length != 8 || edge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[4..])
                != loopBodyBlock
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]) != 3)
        {
            throw Invalid(
                "A registered-query loop has invalid conditional exit/body edges.");
        }

        var query = ReadStandardRegisteredQueryValueInstruction(body,
            queryInstruction, resultRegister, typeDefs, typeSpecs, fields,
            fieldPaths, imports, nativePointerTypes,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        if (query.Type != PrimitiveType.NativeInt)
        {
            throw Invalid(
                "A registered-query loop carries a non-NativeInt query result.");
        }

        var headerState = body.GetRecord(1);
        var bodyState = body.GetRecord(2);
        var exitState = body.GetRecord(3);
        if (headerState.Length != 18 || bodyState.Length != 18
            || exitState.Length != 18 || headerState[0] != 0x01
            || bodyState[0] != 0x01 || exitState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(headerState[4..]) != 2
            || BinaryPrimitives.ReadUInt32LittleEndian(bodyState[4..]) != 2
            || BinaryPrimitives.ReadUInt32LittleEndian(exitState[4..]) != 2
            || conditionRegister >= resultRegister
            || headerState[8] != conditionRegister
            || headerState[9] != 0 || headerState[12] != 0
            || headerState[13] != resultRegister
            || headerState[14] != 0 || headerState[17] != 0
            || !headerState.SequenceEqual(bodyState)
            || !headerState.SequenceEqual(exitState))
        {
            throw Invalid(
                "A registered-query loop has inconsistent loop-carried BlockEntryState rows.");
        }

        var conditionTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(headerState[10..]);
        var resultTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(headerState[15..]);
        if (ReadStandardRegisteredQueryType(body, conditionTypeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports) != PrimitiveType.Bool
            || ReadStandardRegisteredQueryType(body, resultTypeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports)
                != PrimitiveType.NativeInt)
        {
            throw Invalid(
                "A registered-query loop BlockEntryState has an incompatible Boolean or NativeInt type.");
        }

        var conditionVariable = variables.Count;
        var conditionName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_loop_condition_");
        variables.Add(new BoundVariable(conditionName,
            PrimitiveType.Bool, true, default));
        var resultVariable = variables.Count;
        var resultName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_query_result_");
        variables.Add(new BoundVariable(resultName,
            PrimitiveType.NativeInt, true, default));

        var bodyCode = loopBodyCode[..^4];
        var loopBodyStatements
            = ReadStandardRegisteredQueryLoopAssignments(body, bodyCode,
                function, conditionRegister, resultRegister,
                conditionVariable, resultVariable, typeDefs, typeSpecs,
                fields, fieldPaths, imports, nativePointerTypes,
                signatureIndexByAnchorMethod, structureTypeByTypeDef);

        return [
            new BoundLocal(conditionVariable,
                new BoundRead(conditionParameter, PrimitiveType.Bool,
                    default), default),
            new BoundLocal(resultVariable, query, default),
            new BoundWhile(
                new BoundRead(conditionVariable, PrimitiveType.Bool, default),
                new BoundBlock(loopBodyStatements, default), default) {
                IsPostTested = isPostTested,
            },
            new BoundReturn(new BoundRead(resultVariable,
                PrimitiveType.NativeInt, default), default),
        ];
    }

    private static ImmutableArray<BoundStatement>
        ReadStandardRegisteredQueryLoopAssignments(
            TrRmRegisterBodyReader body, ReadOnlySpan<byte> code,
            BoundFunction function, int conditionRegister,
            int resultRegister, int conditionVariable, int resultVariable,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (code.Length > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "A registered-query loop body exceeds bounded decode work.");
        }

        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        var registerValues = new BoundExpression?[15];
        registerValues[conditionRegister]
            = new BoundRead(conditionVariable, PrimitiveType.Bool, default);
        registerValues[resultRegister]
            = new BoundRead(resultVariable, PrimitiveType.NativeInt, default);
        var remainingWork = (long)SemanticVerifier.MaximumAnalysisVisits;
        for (var position = 0; position < code.Length;)
        {
            if (remainingWork-- <= 0)
            {
                throw Unsupported(
                    "A registered-query loop body exceeds bounded decode work.");
            }

            var instructionLength
                = TrRmRegisterBodyReader.InstructionLength(code[position],
                    body.Schema);
            if (instructionLength > code.Length - position)
            {
                throw Invalid(
                    "A registered-query loop body contains a truncated instruction.");
            }

            var instruction = code.Slice(position, instructionLength);
            if (instruction[0] == 0x11)
            {
                var destination = instruction[1] & 0x0F;
                var parameter = instruction[1] >> 4;
                if (destination >= 14
                    || !IsStandardRegisteredQueryBooleanFormal(function,
                        parameter))
                {
                    throw Invalid(
                        "A registered-query loop body reads a non-Boolean formal.");
                }

                registerValues[destination] = new BoundRead(parameter,
                    PrimitiveType.Bool, default);
            }
            else if (instruction[0] == 0xDE)
            {
                var destination = instruction[1] & 0x0F;
                if (destination >= 14)
                {
                    throw Invalid(
                        "A registered-query loop query defines a nonbinding register.");
                }

                registerValues[destination]
                    = ReadStandardRegisteredQueryValueInstruction(body,
                        instruction, destination, typeDefs, typeSpecs, fields,
                        fieldPaths, imports, nativePointerTypes,
                        signatureIndexByAnchorMethod,
                        structureTypeByTypeDef);
            }
            else if (instruction[0] == 0x10)
            {
                var destination = instruction[1] & 0x0F;
                var source = instruction[1] >> 4;
                if (destination >= 14 || source >= 14
                    || registerValues[source]
                        is not BoundExpression sourceValue)
                {
                    throw Invalid(
                        "A registered-query loop move names an unavailable register.");
                }

                if (destination == conditionRegister)
                {
                    if (sourceValue.Type != PrimitiveType.Bool)
                    {
                        throw Invalid(
                            "A registered-query loop changes a Boolean local to another type.");
                    }

                    statements.Add(new BoundAssignment(conditionVariable,
                        sourceValue, default));
                    registerValues[destination] = new BoundRead(
                        conditionVariable, PrimitiveType.Bool, default);
                }
                else if (destination == resultRegister)
                {
                    if (sourceValue.Type != PrimitiveType.NativeInt)
                    {
                        throw Invalid(
                            "A registered-query loop changes a NativeInt query local to another type.");
                    }

                    statements.Add(new BoundAssignment(resultVariable,
                        sourceValue, default));
                    registerValues[destination] = new BoundRead(
                        resultVariable, PrimitiveType.NativeInt, default);
                }
                else
                {
                    registerValues[destination] = sourceValue;
                }
            }
            else
            {
                throw Unsupported(
                    $"A registered-query loop body does not admit opcode 0x{instruction[0]:X2}.");
            }

            position += instructionLength;
        }

        return statements.ToImmutable();
    }

    private static string GetUniqueStandardRegisteredQueryLocalName(
        ImmutableArray<BoundVariable>.Builder variables, string prefix)
    {
        for (var suffix = 0; suffix <= variables.Count; suffix++)
        {
            var candidate = $"{prefix}{suffix}";
            var found = false;
            foreach (var variable in variables)
            {
                if (StringComparer.Ordinal.Equals(variable.Name, candidate))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return candidate;
            }
        }

        throw Unsupported(
            "The registered-query local name domain is exhausted.");
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQuerySiblingLoopBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (body.BlockCount != 7 || body.RecordCount != 9
            || body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || (body.MapMask & ~0x0A) != 0
            || (body.MapMask & 0x02) == 0
            || function.ReturnType != PrimitiveType.NativeInt
            || function.ParameterCount < 2
            || function.Variables.Length != function.ParameterCount
            || variables.Count != function.ParameterCount)
        {
            throw Unsupported(
                "The bounded Standard sibling-loop profile requires two Boolean conditions and a NativeInt result.");
        }

        var controlFlow
            = AnalyzeStandardRegisteredQueryLoopControlFlow(body);
        if (controlFlow.Loops.Length != 2
            || controlFlow.Loops[0].Header != 1
            || controlFlow.Loops[0].Members.Count != 2
            || !controlFlow.Loops[0].Members.Contains(1)
            || !controlFlow.Loops[0].Members.Contains(2)
            || controlFlow.Loops[0].ExitTargets is not [3]
            || controlFlow.Loops[1].Header != 4
            || controlFlow.Loops[1].Members.Count != 2
            || !controlFlow.Loops[1].Members.Contains(4)
            || !controlFlow.Loops[1].Members.Contains(5)
            || controlFlow.Loops[1].ExitTargets is not [6])
        {
            throw Unsupported(
                "The registered-query Body does not contain two bounded sibling loops.");
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A sibling registered-query loop BlockEntryState is not in block order.");
            }
        }

        var entryState = body.GetRecord(0);
        if (entryState.Length != 8 || entryState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Invalid(
                "A sibling registered-query loop preheader must have an empty entry state.");
        }

        var preheaderCode = body.GetBlockCode(0);
        var firstBlockCode = body.GetBlockCode(1);
        var secondBlockCode = body.GetBlockCode(2);
        var bridgeCode = body.GetBlockCode(3);
        var fourthBlockCode = body.GetBlockCode(4);
        var fifthBlockCode = body.GetBlockCode(5);
        var exitCode = body.GetBlockCode(6);
        var firstIsPostTested = firstBlockCode[0] != 0xE4;
        var secondIsPostTested = fourthBlockCode[0] != 0xE4;
        var firstConditionCode
            = firstIsPostTested ? secondBlockCode : firstBlockCode;
        var firstBodyCode
            = firstIsPostTested ? firstBlockCode : secondBlockCode;
        var secondConditionCode
            = secondIsPostTested ? fifthBlockCode : fourthBlockCode;
        var secondBodyCode
            = secondIsPostTested ? fourthBlockCode : fifthBlockCode;
        if (preheaderCode.Length != 12
            || firstConditionCode.Length != 4
            || firstConditionCode[0] != 0xE4
            || firstBodyCode.Length < 8
            || firstBodyCode[^4] != 0xE3
            || firstBodyCode[^3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(firstBodyCode[^2..])
                != (firstIsPostTested ? 2 : 1)
            || bridgeCode.Length != 4 || bridgeCode[0] != 0xE3
            || bridgeCode[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(bridgeCode[2..]) != 4
            || secondConditionCode.Length != 4
            || secondConditionCode[0] != 0xE4
            || secondBodyCode.Length < 8
            || secondBodyCode[^4] != 0xE3
            || secondBodyCode[^3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(secondBodyCode[^2..])
                != (secondIsPostTested ? 5 : 4)
            || exitCode.Length != 2 || exitCode[0] != 0x17)
        {
            throw Unsupported(
                "The sibling registered-query loops are outside the bounded preheader/loop/bridge/exit form.");
        }

        var preheaderBranchOffset = preheaderCode.Length - 4;
        if (preheaderCode[preheaderBranchOffset] != 0xE3
            || preheaderCode[preheaderBranchOffset + 1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                preheaderCode[(preheaderBranchOffset + 2)..]) != 1)
        {
            throw Invalid(
                "A sibling registered-query loop preheader has an invalid first-loop edge.");
        }

        var firstConditionRegister = firstConditionCode[1];
        var secondConditionRegister = secondConditionCode[1];
        var resultRegister = exitCode[1];
        if (firstConditionRegister >= 14 || secondConditionRegister >= 14
            || resultRegister >= 14
            || firstConditionRegister == secondConditionRegister
            || firstConditionRegister == resultRegister
            || secondConditionRegister == resultRegister)
        {
            throw Invalid(
                "The sibling registered-query loops alias a condition or result register.");
        }

        var conditionParameters = new Dictionary<int, int>(2);
        ReadOnlySpan<byte> queryInstruction = default;
        for (var position = 0; position < preheaderBranchOffset;)
        {
            var opcode = preheaderCode[position];
            if (opcode == 0x11)
            {
                var instruction = preheaderCode.Slice(position, 2);
                var destination = instruction[1] & 0x0F;
                var parameter = instruction[1] >> 4;
                if (destination >= 14
                    || parameter >= function.ParameterCount
                    || !IsStandardRegisteredQueryBooleanFormal(function,
                        parameter)
                    || !conditionParameters.TryAdd(destination, parameter))
                {
                    throw Invalid(
                        "A sibling registered-query loop has an invalid Boolean local initializer.");
                }

                position += 2;
                continue;
            }

            if (opcode == 0xDE && queryInstruction.IsEmpty)
            {
                queryInstruction = preheaderCode.Slice(position, 4);
                position += 4;
                continue;
            }

            throw Unsupported(
                "A sibling registered-query loop preheader has an unsupported initializer.");
        }

        if (conditionParameters.Count != 2
            || !conditionParameters.TryGetValue(firstConditionRegister,
                out var firstConditionParameter)
            || !conditionParameters.TryGetValue(secondConditionRegister,
                out var secondConditionParameter)
            || queryInstruction.IsEmpty
            || (queryInstruction[1] & 0x0F) != resultRegister)
        {
            throw Invalid(
                "A sibling registered-query loop preheader does not initialize its exact local registers.");
        }

        var firstEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(firstConditionCode[2..]);
        var secondEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(secondConditionCode[2..]);
        var firstEdge = body.GetRecord(firstEdgeIndex);
        var secondEdge = body.GetRecord(secondEdgeIndex);
        if (firstEdgeIndex != 7 || firstEdge.Length != 8
            || firstEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(firstEdge[4..])
                != (firstIsPostTested ? 1 : 2)
            || BinaryPrimitives.ReadUInt16LittleEndian(firstEdge[6..]) != 3
            || secondEdgeIndex != 8 || secondEdge.Length != 8
            || secondEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(secondEdge[4..])
                != (secondIsPostTested ? 4 : 5)
            || BinaryPrimitives.ReadUInt16LittleEndian(secondEdge[6..]) != 6)
        {
            throw Invalid(
                "A sibling registered-query loop has invalid conditional edge targets.");
        }

        var query = ReadStandardRegisteredQueryValueInstruction(body,
            queryInstruction, resultRegister, typeDefs, typeSpecs, fields,
            fieldPaths, imports, nativePointerTypes,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        if (query.Type != PrimitiveType.NativeInt)
        {
            throw Invalid(
                "A sibling registered-query loop query has a non-NativeInt result.");
        }

        var headerState = body.GetRecord(1);
        if (headerState.Length != 23 || headerState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(headerState[4..]) != 3)
        {
            throw Invalid(
                "A sibling registered-query loop has invalid loop-carried register rows.");
        }

        for (var blockIndex = 1; blockIndex < body.BlockCount; blockIndex++)
        {
            if (!headerState.SequenceEqual(body.GetRecord(blockIndex)))
            {
                throw Invalid(
                    "A sibling registered-query loop edge changes a live register carrier or type.");
            }
        }

        var stateRegisters = new HashSet<int>();
        var hasFirstConditionState = false;
        var hasSecondConditionState = false;
        var hasResultState = false;
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            var rowOffset = 8 + (rowIndex * 5);
            var register = headerState[rowOffset];
            var carrierKind = headerState[rowOffset + 1];
            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                headerState[(rowOffset + 2)..]);
            if (register >= 14 || carrierKind != 0
                || headerState[rowOffset + 4] != 0
                || !stateRegisters.Add(register))
            {
                throw Invalid(
                    "A sibling registered-query loop has a duplicate or invalid loop-carried register row.");
            }

            var type = ReadStandardRegisteredQueryType(body, typeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports);
            if (register == firstConditionRegister)
            {
                hasFirstConditionState = type == PrimitiveType.Bool;
            }
            else if (register == secondConditionRegister)
            {
                hasSecondConditionState = type == PrimitiveType.Bool;
            }
            else if (register == resultRegister)
            {
                hasResultState = type == PrimitiveType.NativeInt;
            }
            else
            {
                throw Invalid(
                    "A sibling registered-query loop carries an unexpected register.");
            }
        }

        if (!hasFirstConditionState || !hasSecondConditionState
            || !hasResultState)
        {
            throw Invalid(
                "A sibling registered-query loop has incompatible Boolean or NativeInt register types.");
        }

        var firstConditionVariable = variables.Count;
        var firstConditionName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_first_loop_condition_");
        variables.Add(new BoundVariable(firstConditionName,
            PrimitiveType.Bool, true, default));
        var secondConditionVariable = variables.Count;
        var secondConditionName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_second_loop_condition_");
        variables.Add(new BoundVariable(secondConditionName,
            PrimitiveType.Bool, true, default));
        var resultVariable = variables.Count;
        var resultName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_sibling_query_result_");
        variables.Add(new BoundVariable(resultName,
            PrimitiveType.NativeInt, true, default));

        var firstAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                firstBodyCode[..^4], function, firstConditionRegister,
                resultRegister, firstConditionVariable, resultVariable,
                typeDefs, typeSpecs, fields, fieldPaths, imports,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        var secondAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                secondBodyCode[..^4], function, secondConditionRegister,
                resultRegister, secondConditionVariable, resultVariable,
                typeDefs, typeSpecs, fields, fieldPaths, imports,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        ValidateStandardRegisteredQuerySiblingLoopAssignments(
            firstAssignments, firstConditionVariable, resultVariable);
        ValidateStandardRegisteredQuerySiblingLoopAssignments(
            secondAssignments, secondConditionVariable, resultVariable);

        return [
            new BoundLocal(firstConditionVariable,
                new BoundRead(firstConditionParameter, PrimitiveType.Bool,
                    default), default),
            new BoundLocal(secondConditionVariable,
                new BoundRead(secondConditionParameter, PrimitiveType.Bool,
                    default), default),
            new BoundLocal(resultVariable, query, default),
            new BoundWhile(
                new BoundRead(firstConditionVariable, PrimitiveType.Bool,
                    default),
                new BoundBlock(firstAssignments, default), default) {
                    IsPostTested = firstIsPostTested,
                },
            new BoundWhile(
                new BoundRead(secondConditionVariable, PrimitiveType.Bool,
                    default),
                new BoundBlock(secondAssignments, default), default) {
                    IsPostTested = secondIsPostTested,
                },
            new BoundReturn(new BoundRead(resultVariable,
                PrimitiveType.NativeInt, default), default),
        ];
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryFirstSiblingTransferBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || (body.MapMask & ~0x0A) != 0
            || (body.MapMask & 0x02) == 0
            || function.ParameterCount < 2
            || function.Variables.Length != function.ParameterCount
            || variables.Count != function.ParameterCount)
        {
            throw Unsupported(
                "The bounded Standard first-sibling-transfer profile requires value-formal conditions and a NativeInt result.");
        }

        var controlFlow
            = AnalyzeStandardRegisteredQueryLoopControlFlow(body);
        var firstIsPostTested = body.GetBlockCode(1)[0] != 0xE4;
        var firstControlBlock = firstIsPostTested ? 1 : 2;
        var firstUpdateBlock = firstIsPostTested ? 2 : 3;
        var firstConditionBlock = firstIsPostTested ? 3 : 1;
        var firstContinueTarget = firstConditionBlock;
        var firstContinueUpdateTarget = firstUpdateBlock;
        var controlCode = body.GetBlockCode(firstControlBlock);
        var controlBranchOffset = controlCode.Length - 4;
        var updateCode = body.GetBlockCode(firstUpdateBlock);
        var conditionCode = body.GetBlockCode(firstConditionBlock);
        var controlPrefixCode = controlCode[..controlBranchOffset];
        var firstBodyBlock = firstIsPostTested ? 1 : 2;
        var firstLoopLatchesValid = firstIsPostTested
            ? controlFlow.Loops.Length == 2
                && controlFlow.Loops[0].Latches is [3]
            : controlFlow.Loops.Length == 2
                && (controlFlow.Loops[0].Latches is [3]
                    or [2, 3]);
        if (controlFlow.Loops.Length != 2
            || controlFlow.Loops[0].Header != 1
            || controlFlow.Loops[0].Members.Count != 3
            || !controlFlow.Loops[0].Members.Contains(1)
            || !controlFlow.Loops[0].Members.Contains(2)
            || !controlFlow.Loops[0].Members.Contains(3)
            || !firstLoopLatchesValid
            || controlFlow.Loops[0].ExitTargets is not [4]
            || controlFlow.Loops[1].Header != 5
            || controlFlow.Loops[1].Members.Count != 2
            || !controlFlow.Loops[1].Members.Contains(5)
            || !controlFlow.Loops[1].Members.Contains(6)
            || controlFlow.Loops[1].Latches is not [6]
            || controlFlow.Loops[1].ExitTargets is not [7])
        {
            var loopDetails = new List<string>(
                controlFlow.Loops.Length);
            foreach (var region in controlFlow.Loops)
            {
                loopDetails.Add(
                    $"header={region.Header}, members={region.Members.Count}, latches=[{string.Join(",", region.Latches)}], exits=[{string.Join(",", region.ExitTargets)}]");
            }
            throw Unsupported(
                $"The registered-query Body does not contain a bounded first-sibling transfer followed by a second sibling loop (blocks={body.BlockCount}; {string.Join("; ", loopDetails)}).");
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A first-sibling-transfer registered-query BlockEntryState is not in block order.");
            }
        }

        var entryState = body.GetRecord(0);
        if (entryState.Length != 8 || entryState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Invalid(
                "A first-sibling-transfer registered-query preheader must have an empty entry state.");
        }

        var preheaderCode = body.GetBlockCode(0);
        var bridgeCode = body.GetBlockCode(4);
        var secondConditionCode = body.GetBlockCode(5);
        var secondBodyCode = body.GetBlockCode(6);
        var exitCode = body.GetBlockCode(7);
        if (preheaderCode.Length != 12
            || controlCode.Length < 6
            || controlCode[controlBranchOffset] != 0xE4
            || updateCode.Length < 4 || updateCode[^4] != 0xE3
            || updateCode[^3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(updateCode[^2..])
                != firstConditionBlock
            || conditionCode.Length != 4 || conditionCode[0] != 0xE4
            || bridgeCode.Length != 4 || bridgeCode[0] != 0xE3
            || bridgeCode[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(bridgeCode[2..]) != 5
            || secondConditionCode.Length != 4
            || secondConditionCode[0] != 0xE4
            || secondBodyCode.Length < 8
            || secondBodyCode[^4] != 0xE3
            || secondBodyCode[^3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(secondBodyCode[^2..])
                != 5
            || exitCode.Length != 2 || exitCode[0] != 0x17)
        {
            throw Unsupported(
                "The first-sibling-transfer loops are outside the bounded preheader/transfer/update/condition form.");
        }

        var preheaderBranchOffset = preheaderCode.Length - 4;
        if (preheaderCode[preheaderBranchOffset] != 0xE3
            || preheaderCode[preheaderBranchOffset + 1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                preheaderCode[(preheaderBranchOffset + 2)..]) != 1)
        {
            throw Invalid(
                "A first-sibling-transfer registered-query preheader has an invalid first-loop edge.");
        }

        var firstConditionRegister = conditionCode[1];
        var secondConditionRegister = secondConditionCode[1];
        var resultRegister = exitCode[1];
        var controlRegister = controlCode[controlBranchOffset + 1];
        var continueParameter = -1;
        for (var position = 0; position < controlBranchOffset;)
        {
            var instructionLength
                = TrRmRegisterBodyReader.InstructionLength(
                    controlCode[position], body.Schema);
            if (instructionLength > controlBranchOffset - position)
            {
                throw Invalid(
                    "A first-sibling-transfer control block contains a truncated instruction.");
            }

            var instruction = controlCode.Slice(position,
                instructionLength);
            if (instruction[0] == 0x11
                && (instruction[1] & 0x0F) == controlRegister)
            {
                if (continueParameter >= 0)
                {
                    throw Invalid(
                        "A first-sibling-transfer control block loads its condition register more than once.");
                }
                continueParameter = instruction[1] >> 4;
            }
            position += instructionLength;
        }
        if (firstConditionRegister >= 14
            || secondConditionRegister >= 14 || resultRegister >= 14
            || controlRegister >= 14
            || continueParameter < 0
            || firstConditionRegister == secondConditionRegister
            || firstConditionRegister == resultRegister
            || secondConditionRegister == resultRegister
            || !IsStandardRegisteredQueryBooleanFormal(function,
                continueParameter))
        {
            throw Invalid(
                "The first-sibling-transfer loops alias query registers or use a non-Boolean transfer condition.");
        }

        var conditionParameters = new Dictionary<int, int>(2);
        ReadOnlySpan<byte> queryInstruction = default;
        for (var position = 0; position < preheaderBranchOffset;)
        {
            var opcode = preheaderCode[position];
            if (opcode == 0x11)
            {
                var instruction = preheaderCode.Slice(position, 2);
                var destination = instruction[1] & 0x0F;
                var parameter = instruction[1] >> 4;
                if (destination >= 14
                    || parameter >= function.ParameterCount
                    || !IsStandardRegisteredQueryBooleanFormal(function,
                        parameter)
                    || !conditionParameters.TryAdd(destination, parameter))
                {
                    throw Invalid(
                        "A first-sibling-transfer loop has an invalid Boolean local initializer.");
                }

                position += 2;
                continue;
            }

            if (opcode == 0xDE && queryInstruction.IsEmpty)
            {
                queryInstruction = preheaderCode.Slice(position, 4);
                position += 4;
                continue;
            }

            throw Unsupported(
                "A first-sibling-transfer preheader has an unsupported initializer.");
        }

        if (conditionParameters.Count != 2
            || !conditionParameters.TryGetValue(firstConditionRegister,
                out var firstConditionParameter)
            || !conditionParameters.TryGetValue(secondConditionRegister,
                out var secondConditionParameter)
            || queryInstruction.IsEmpty
            || (queryInstruction[1] & 0x0F) != resultRegister)
        {
            throw Invalid(
                "A first-sibling-transfer preheader does not initialize its exact local registers.");
        }

        var continueEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                controlCode[(controlBranchOffset + 2)..]);
        var firstEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(conditionCode[2..]);
        var secondEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                secondConditionCode[2..]);
        if (continueEdgeIndex < body.BlockCount
            || continueEdgeIndex >= body.RecordCount
            || firstEdgeIndex < body.BlockCount
            || firstEdgeIndex >= body.RecordCount
            || secondEdgeIndex < body.BlockCount
            || secondEdgeIndex >= body.RecordCount
            || continueEdgeIndex == firstEdgeIndex
            || continueEdgeIndex == secondEdgeIndex
            || firstEdgeIndex == secondEdgeIndex)
        {
            throw Invalid(
                "A first-sibling-transfer loop has duplicate or out-of-range conditional edges.");
        }

        var continueEdge = body.GetRecord(continueEdgeIndex);
        var firstEdge = body.GetRecord(firstEdgeIndex);
        var secondEdge = body.GetRecord(secondEdgeIndex);
        var controlTrueTarget = continueEdge.Length == 8
            ? BinaryPrimitives.ReadUInt16LittleEndian(continueEdge[4..]) : -1;
        var controlFalseTarget = continueEdge.Length == 8
            ? BinaryPrimitives.ReadUInt16LittleEndian(continueEdge[6..]) : -1;
        var firstSiblingBreak = controlTrueTarget == 4
            && controlFalseTarget == firstContinueUpdateTarget;
        var firstSiblingContinue = controlTrueTarget == firstContinueTarget
            && controlFalseTarget == firstContinueUpdateTarget;
        if (continueEdge.Length != 8 || continueEdge[0] != 0x06
            || (!firstSiblingBreak && !firstSiblingContinue)
            || firstEdge.Length != 8 || firstEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(firstEdge[4..])
                != firstBodyBlock
            || BinaryPrimitives.ReadUInt16LittleEndian(firstEdge[6..]) != 4
            || secondEdge.Length != 8 || secondEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(secondEdge[4..]) != 6
            || BinaryPrimitives.ReadUInt16LittleEndian(secondEdge[6..]) != 7)
        {
            throw Invalid(
                "A first-sibling transfer loop has invalid conditional edge targets.");
        }

        var query = ReadStandardRegisteredQueryValueInstruction(body,
            queryInstruction, resultRegister, typeDefs, typeSpecs, fields,
            fieldPaths, imports, nativePointerTypes,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        if (query.Type != PrimitiveType.NativeInt)
        {
            throw Invalid(
                "A first-sibling-transfer query has a non-NativeInt result.");
        }

        var headerState = body.GetRecord(1);
        if (headerState.Length != 23 || headerState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(headerState[4..]) != 3)
        {
            throw Invalid(
                "A first-sibling-transfer loop has invalid loop-carried register rows.");
        }

        for (var blockIndex = 1; blockIndex < body.BlockCount; blockIndex++)
        {
            if (!headerState.SequenceEqual(body.GetRecord(blockIndex)))
            {
                throw Invalid(
                    "A first-sibling-transfer loop edge changes a live register carrier or type.");
            }
        }

        var stateRegisters = new HashSet<int>();
        var hasFirstConditionState = false;
        var hasSecondConditionState = false;
        var hasResultState = false;
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            var rowOffset = 8 + (rowIndex * 5);
            var register = headerState[rowOffset];
            var carrierKind = headerState[rowOffset + 1];
            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                headerState[(rowOffset + 2)..]);
            if (register >= 14 || carrierKind != 0
                || headerState[rowOffset + 4] != 0
                || !stateRegisters.Add(register))
            {
                throw Invalid(
                    "A first-sibling-transfer loop has a duplicate or invalid loop-carried register row.");
            }

            var type = ReadStandardRegisteredQueryType(body, typeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports);
            if (register == firstConditionRegister)
            {
                hasFirstConditionState = type == PrimitiveType.Bool;
            }
            else if (register == secondConditionRegister)
            {
                hasSecondConditionState = type == PrimitiveType.Bool;
            }
            else if (register == resultRegister)
            {
                hasResultState = type == PrimitiveType.NativeInt;
            }
            else
            {
                throw Invalid(
                    "A first-sibling-transfer loop carries an unexpected register.");
            }
        }

        if (!hasFirstConditionState || !hasSecondConditionState
            || !hasResultState)
        {
            throw Invalid(
                "A first-sibling-transfer loop has incompatible Boolean or NativeInt register types.");
        }

        var firstConditionVariable = variables.Count;
        var firstConditionName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_first_loop_condition_");
        variables.Add(new BoundVariable(firstConditionName,
            PrimitiveType.Bool, true, default));
        var secondConditionVariable = variables.Count;
        var secondConditionName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_second_loop_condition_");
        variables.Add(new BoundVariable(secondConditionName,
            PrimitiveType.Bool, true, default));
        var resultVariable = variables.Count;
        var resultName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_sibling_continue_result_");
        variables.Add(new BoundVariable(resultName,
            PrimitiveType.NativeInt, true, default));

        var firstPrefixAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                controlPrefixCode, function, firstConditionRegister,
                resultRegister, firstConditionVariable, resultVariable,
                typeDefs, typeSpecs, fields, fieldPaths, imports,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        var firstSuffixAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                updateCode[..^4], function, firstConditionRegister,
                resultRegister, firstConditionVariable, resultVariable,
                typeDefs, typeSpecs, fields, fieldPaths, imports,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        var firstAssignments = firstPrefixAssignments.AddRange(
            firstSuffixAssignments);
        var secondAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                secondBodyCode[..^4], function, secondConditionRegister,
                resultRegister, secondConditionVariable, resultVariable,
                typeDefs, typeSpecs, fields, fieldPaths, imports,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        ValidateStandardRegisteredQuerySiblingLoopAssignments(
            firstAssignments, firstConditionVariable, resultVariable);
        ValidateStandardRegisteredQuerySiblingLoopAssignments(
            secondAssignments, secondConditionVariable, resultVariable);

        BoundStatement firstTransfer = firstSiblingBreak
            ? new BoundBreak(default) : new BoundContinue(default);
        var continueIf = new BoundIf(
            new BoundRead(continueParameter, PrimitiveType.Bool, default),
            new BoundBlock([firstTransfer], default),
            null, default);
        return [
            new BoundLocal(firstConditionVariable,
                new BoundRead(firstConditionParameter, PrimitiveType.Bool,
                    default), default),
            new BoundLocal(secondConditionVariable,
                new BoundRead(secondConditionParameter, PrimitiveType.Bool,
                    default), default),
            new BoundLocal(resultVariable, query, default),
            new BoundWhile(
                new BoundRead(firstConditionVariable, PrimitiveType.Bool,
                    default),
                new BoundBlock([
                    .. firstPrefixAssignments,
                    continueIf,
                    .. firstSuffixAssignments,
                ], default),
                default) {
                    IsPostTested = firstIsPostTested,
                },
            new BoundWhile(
                new BoundRead(secondConditionVariable, PrimitiveType.Bool,
                    default),
                new BoundBlock(secondAssignments, default), default),
            new BoundReturn(new BoundRead(resultVariable,
                PrimitiveType.NativeInt, default), default),
        ];
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQuerySiblingTransferBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || (body.MapMask & ~0x0A) != 0
            || (body.MapMask & 0x02) == 0
            || function.ParameterCount < 2
            || function.Variables.Length != function.ParameterCount
            || variables.Count != function.ParameterCount)
        {
            throw Unsupported(
                "The bounded Standard sibling-transfer profile requires value-formal conditions and a NativeInt result.");
        }

        var controlFlow
            = AnalyzeStandardRegisteredQueryLoopControlFlow(body);
        var secondIsPostTested = body.GetBlockCode(4)[0] != 0xE4;
        var secondControlBlock = secondIsPostTested ? 4 : 5;
        var secondUpdateBlock = secondIsPostTested ? 5 : 6;
        var secondConditionBlock = secondIsPostTested ? 6 : 4;
        var secondContinueTarget = secondConditionBlock;
        var secondContinueUpdateTarget = secondUpdateBlock;
        var controlCode = body.GetBlockCode(secondControlBlock);
        var controlBranchOffset = controlCode.Length - 4;
        var updateCode = body.GetBlockCode(secondUpdateBlock);
        var conditionCode = body.GetBlockCode(secondConditionBlock);
        var controlPrefixCode = controlCode[..controlBranchOffset];
        var secondBodyBlock = secondIsPostTested ? 4 : 5;
        var secondLoopLatchesValid = secondIsPostTested
            ? controlFlow.Loops.Length == 2
                && controlFlow.Loops[1].Latches is [6]
            : controlFlow.Loops.Length == 2
                && (controlFlow.Loops[1].Latches is [6]
                    or [5, 6]);
        if (controlFlow.Loops.Length != 2
            || controlFlow.Loops[0].Header != 1
            || controlFlow.Loops[0].Members.Count != 2
            || !controlFlow.Loops[0].Members.Contains(1)
            || !controlFlow.Loops[0].Members.Contains(2)
            || controlFlow.Loops[0].ExitTargets is not [3]
            || controlFlow.Loops[1].Header != 4
            || controlFlow.Loops[1].Members.Count != 3
            || !controlFlow.Loops[1].Members.Contains(4)
            || !controlFlow.Loops[1].Members.Contains(5)
            || !controlFlow.Loops[1].Members.Contains(6)
            || !secondLoopLatchesValid
            || controlFlow.Loops[1].ExitTargets is not [7])
        {
            throw Unsupported(
                "The registered-query Body does not contain two bounded sibling loops with a second-loop transfer.");
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A sibling-transfer registered-query BlockEntryState is not in block order.");
            }
        }

        var entryState = body.GetRecord(0);
        if (entryState.Length != 8 || entryState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Invalid(
                "A sibling-transfer registered-query preheader must have an empty entry state.");
        }

        var preheaderCode = body.GetBlockCode(0);
        var firstBlockCode = body.GetBlockCode(1);
        var secondBlockCode = body.GetBlockCode(2);
        var bridgeCode = body.GetBlockCode(3);
        var exitCode = body.GetBlockCode(7);
        var firstIsPostTested = firstBlockCode[0] != 0xE4;
        var firstConditionCode
            = firstIsPostTested ? secondBlockCode : firstBlockCode;
        var firstBodyCode
            = firstIsPostTested ? firstBlockCode : secondBlockCode;
        var firstBodyBlock = firstIsPostTested ? 1 : 2;
        var firstConditionBlock = firstIsPostTested ? 2 : 1;
        if (preheaderCode.Length != 12
            || firstConditionCode.Length != 4
            || firstConditionCode[0] != 0xE4
            || firstBodyCode.Length < 8
            || firstBodyCode[^4] != 0xE3
            || firstBodyCode[^3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(firstBodyCode[^2..])
                != firstConditionBlock
            || bridgeCode.Length != 4 || bridgeCode[0] != 0xE3
            || bridgeCode[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(bridgeCode[2..]) != 4
            || controlCode.Length < 6
            || controlCode[controlBranchOffset] != 0xE4
            || updateCode.Length < 4
            || updateCode[^4] != 0xE3 || updateCode[^3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(updateCode[^2..])
                != secondConditionBlock
            || conditionCode.Length != 4 || conditionCode[0] != 0xE4
            || exitCode.Length != 2 || exitCode[0] != 0x17)
        {
            throw Unsupported(
                "The sibling-transfer loops are outside the bounded preheader/loop/transfer/update/exit form.");
        }

        var preheaderBranchOffset = preheaderCode.Length - 4;
        if (preheaderCode[preheaderBranchOffset] != 0xE3
            || preheaderCode[preheaderBranchOffset + 1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                preheaderCode[(preheaderBranchOffset + 2)..]) != 1)
        {
            throw Invalid(
                "A sibling-transfer registered-query preheader has an invalid first-loop edge.");
        }

        var firstConditionRegister = firstConditionCode[1];
        var secondConditionRegister = conditionCode[1];
        var resultRegister = exitCode[1];
        var controlRegister = controlCode[controlBranchOffset + 1];
        var continueParameter = -1;
        for (var position = 0; position < controlBranchOffset;)
        {
            var instructionLength
                = TrRmRegisterBodyReader.InstructionLength(
                    controlCode[position], body.Schema);
            if (instructionLength > controlBranchOffset - position)
            {
                throw Invalid(
                    "A sibling-transfer control block contains a truncated instruction.");
            }

            var instruction = controlCode.Slice(position,
                instructionLength);
            if (instruction[0] == 0x11
                && (instruction[1] & 0x0F) == controlRegister)
            {
                if (continueParameter >= 0)
                {
                    throw Invalid(
                        "A sibling-transfer control block loads its condition register more than once.");
                }
                continueParameter = instruction[1] >> 4;
            }
            position += instructionLength;
        }

        if (firstConditionRegister >= 14
            || secondConditionRegister >= 14 || resultRegister >= 14
            || controlRegister >= 14
            || continueParameter < 0
            || firstConditionRegister == secondConditionRegister
            || firstConditionRegister == resultRegister
            || secondConditionRegister == resultRegister
            || !IsStandardRegisteredQueryBooleanFormal(function,
                continueParameter))
        {
            throw Invalid(
                "The sibling-transfer loops alias query registers or use a non-Boolean transfer condition.");
        }

        var conditionParameters = new Dictionary<int, int>(2);
        ReadOnlySpan<byte> queryInstruction = default;
        for (var position = 0; position < preheaderBranchOffset;)
        {
            var opcode = preheaderCode[position];
            if (opcode == 0x11)
            {
                var instruction = preheaderCode.Slice(position, 2);
                var destination = instruction[1] & 0x0F;
                var parameter = instruction[1] >> 4;
                if (destination >= 14
                    || parameter >= function.ParameterCount
                    || !IsStandardRegisteredQueryBooleanFormal(function,
                        parameter)
                    || !conditionParameters.TryAdd(destination, parameter))
                {
                    throw Invalid(
                        "A sibling-transfer registered-query loop has an invalid Boolean local initializer.");
                }

                position += 2;
                continue;
            }

            if (opcode == 0xDE && queryInstruction.IsEmpty)
            {
                queryInstruction = preheaderCode.Slice(position, 4);
                position += 4;
                continue;
            }

            throw Unsupported(
                "A sibling-transfer registered-query preheader has an unsupported initializer.");
        }

        if (conditionParameters.Count != 2
            || !conditionParameters.TryGetValue(firstConditionRegister,
                out var firstConditionParameter)
            || !conditionParameters.TryGetValue(secondConditionRegister,
                out var secondConditionParameter)
            || queryInstruction.IsEmpty
            || (queryInstruction[1] & 0x0F) != resultRegister)
        {
            throw Invalid(
                "A sibling-transfer registered-query preheader does not initialize its exact local registers.");
        }

        var firstEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                firstConditionCode[2..]);
        var continueEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                controlCode[(controlBranchOffset + 2)..]);
        var secondEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(conditionCode[2..]);
        if (firstEdgeIndex < body.BlockCount
            || firstEdgeIndex >= body.RecordCount
            || continueEdgeIndex < body.BlockCount
            || continueEdgeIndex >= body.RecordCount
            || secondEdgeIndex < body.BlockCount
            || secondEdgeIndex >= body.RecordCount
            || firstEdgeIndex == continueEdgeIndex
            || firstEdgeIndex == secondEdgeIndex
            || continueEdgeIndex == secondEdgeIndex)
        {
            throw Invalid(
                "A sibling-transfer registered-query loop has duplicate or out-of-range conditional edges.");
        }

        var firstEdge = body.GetRecord(firstEdgeIndex);
        var continueEdge = body.GetRecord(continueEdgeIndex);
        var secondEdge = body.GetRecord(secondEdgeIndex);
        var controlTrueTarget = continueEdge.Length == 8
            ? BinaryPrimitives.ReadUInt16LittleEndian(continueEdge[4..]) : -1;
        var controlFalseTarget = continueEdge.Length == 8
            ? BinaryPrimitives.ReadUInt16LittleEndian(continueEdge[6..]) : -1;
        var secondSiblingBreak = controlTrueTarget == 7
            && controlFalseTarget == secondContinueUpdateTarget;
        var secondSiblingContinue = controlTrueTarget == secondContinueTarget
            && controlFalseTarget == secondContinueUpdateTarget;
        if (firstEdge.Length != 8 || firstEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(firstEdge[4..])
                != firstBodyBlock
            || BinaryPrimitives.ReadUInt16LittleEndian(firstEdge[6..]) != 3
            || continueEdge.Length != 8 || continueEdge[0] != 0x06
            || (!secondSiblingBreak && !secondSiblingContinue)
            || secondEdge.Length != 8 || secondEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(secondEdge[4..])
                != secondBodyBlock
            || BinaryPrimitives.ReadUInt16LittleEndian(secondEdge[6..]) != 7)
        {
            throw Invalid(
                "A sibling-transfer registered-query loop has invalid conditional edge targets.");
        }

        var query = ReadStandardRegisteredQueryValueInstruction(body,
            queryInstruction, resultRegister, typeDefs, typeSpecs, fields,
            fieldPaths, imports, nativePointerTypes,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        if (query.Type != PrimitiveType.NativeInt)
        {
            throw Invalid(
                "A sibling-transfer registered-query query has a non-NativeInt result.");
        }

        var headerState = body.GetRecord(1);
        if (headerState.Length != 23 || headerState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(headerState[4..]) != 3)
        {
            throw Invalid(
                "A sibling-transfer registered-query loop has invalid loop-carried register rows.");
        }

        for (var blockIndex = 1; blockIndex < body.BlockCount; blockIndex++)
        {
            if (!headerState.SequenceEqual(body.GetRecord(blockIndex)))
            {
                throw Invalid(
                    "A sibling-transfer registered-query loop edge changes a live register carrier or type.");
            }
        }

        var stateRegisters = new HashSet<int>();
        var hasFirstConditionState = false;
        var hasSecondConditionState = false;
        var hasResultState = false;
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            var rowOffset = 8 + (rowIndex * 5);
            var register = headerState[rowOffset];
            var carrierKind = headerState[rowOffset + 1];
            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                headerState[(rowOffset + 2)..]);
            if (register >= 14 || carrierKind != 0
                || headerState[rowOffset + 4] != 0
                || !stateRegisters.Add(register))
            {
                throw Invalid(
                    "A sibling-transfer registered-query loop has a duplicate or invalid loop-carried register row.");
            }

            var type = ReadStandardRegisteredQueryType(body, typeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports);
            if (register == firstConditionRegister)
            {
                hasFirstConditionState = type == PrimitiveType.Bool;
            }
            else if (register == secondConditionRegister)
            {
                hasSecondConditionState = type == PrimitiveType.Bool;
            }
            else if (register == resultRegister)
            {
                hasResultState = type == PrimitiveType.NativeInt;
            }
            else
            {
                throw Invalid(
                    "A sibling-transfer registered-query loop carries an unexpected register.");
            }
        }

        if (!hasFirstConditionState || !hasSecondConditionState
            || !hasResultState)
        {
            throw Invalid(
                "A sibling-transfer registered-query loop has incompatible Boolean or NativeInt register types.");
        }

        var firstConditionVariable = variables.Count;
        var firstConditionName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_first_loop_condition_");
        variables.Add(new BoundVariable(firstConditionName,
            PrimitiveType.Bool, true, default));
        var secondConditionVariable = variables.Count;
        var secondConditionName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_second_loop_condition_");
        variables.Add(new BoundVariable(secondConditionName,
            PrimitiveType.Bool, true, default));
        var resultVariable = variables.Count;
        var resultName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_sibling_continue_result_");
        variables.Add(new BoundVariable(resultName,
            PrimitiveType.NativeInt, true, default));

        var firstAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                firstBodyCode[..^4], function, firstConditionRegister,
                resultRegister, firstConditionVariable, resultVariable,
                typeDefs, typeSpecs, fields, fieldPaths, imports,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        var secondPrefixAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                controlPrefixCode, function, secondConditionRegister,
                resultRegister, secondConditionVariable, resultVariable,
                typeDefs, typeSpecs, fields, fieldPaths, imports,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        var secondSuffixAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                updateCode[..^4], function, secondConditionRegister,
                resultRegister, secondConditionVariable, resultVariable,
                typeDefs, typeSpecs, fields, fieldPaths, imports,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
        var secondAssignments = secondPrefixAssignments.AddRange(
            secondSuffixAssignments);
        ValidateStandardRegisteredQuerySiblingLoopAssignments(
            firstAssignments, firstConditionVariable, resultVariable);
        ValidateStandardRegisteredQuerySiblingLoopAssignments(
            secondAssignments, secondConditionVariable, resultVariable);

        BoundStatement secondTransfer = secondSiblingBreak
            ? new BoundBreak(default) : new BoundContinue(default);
        var continueIf = new BoundIf(
            new BoundRead(continueParameter, PrimitiveType.Bool, default),
            new BoundBlock([secondTransfer], default),
            null, default);
        return [
            new BoundLocal(firstConditionVariable,
                new BoundRead(firstConditionParameter, PrimitiveType.Bool,
                    default), default),
            new BoundLocal(secondConditionVariable,
                new BoundRead(secondConditionParameter, PrimitiveType.Bool,
                    default), default),
            new BoundLocal(resultVariable, query, default),
            new BoundWhile(
                new BoundRead(firstConditionVariable, PrimitiveType.Bool,
                    default),
                new BoundBlock(firstAssignments, default), default) {
                    IsPostTested = firstIsPostTested,
                },
            new BoundWhile(
                new BoundRead(secondConditionVariable, PrimitiveType.Bool,
                    default),
                new BoundBlock([
                    .. secondPrefixAssignments,
                    continueIf,
                    .. secondSuffixAssignments,
                ], default),
                default) {
                    IsPostTested = secondIsPostTested,
                },
            new BoundReturn(new BoundRead(resultVariable,
                PrimitiveType.NativeInt, default), default),
        ];
    }

    private static void
        ValidateStandardRegisteredQuerySiblingLoopAssignments(
            ImmutableArray<BoundStatement> assignments, int conditionVariable,
            int resultVariable)
    {
        if (assignments.Length is < 1 or > 2)
        {
            throw Unsupported(
                "A sibling registered-query loop has an unsupported update count.");
        }

        var hasConditionUpdate = false;
        var hasResultUpdate = false;
        foreach (var statement in assignments)
        {
            if (statement is not BoundAssignment assignment)
            {
                throw Unsupported(
                    "A sibling registered-query loop has a non-assignment update.");
            }

            if (assignment.Variable == conditionVariable
                && assignment.Value is BoundRead {
                    Type: PrimitiveType.Bool,
                }
                && !hasConditionUpdate)
            {
                hasConditionUpdate = true;
            }
            else if (assignment.Variable == resultVariable
                && assignment.Value is BoundLayoutQuery {
                    Type: PrimitiveType.NativeInt,
                }
                && !hasResultUpdate)
            {
                hasResultUpdate = true;
            }
            else
            {
                throw Unsupported(
                    "A sibling registered-query loop has an unsupported or duplicate local update.");
            }
        }

        if (!hasConditionUpdate)
        {
            throw Unsupported(
                "A sibling registered-query loop omits its Boolean update.");
        }
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryNestedLoopBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef, bool hasInnerControlExit)
    {
        if (body.BlockCount != (hasInnerControlExit ? 7 : 6)
            || body.RecordCount != (hasInnerControlExit ? 10 : 8)
            || body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || (body.MapMask & ~0x0A) != 0
            || (body.MapMask & 0x02) == 0
            || function.ReturnType != PrimitiveType.NativeInt
            || function.ParameterCount < 4
            || function.Variables.Length != function.ParameterCount
            || variables.Count != function.ParameterCount)
        {
            throw Unsupported(
                "The bounded Standard nested query loop requires four Boolean formals and a NativeInt result.");
        }

        var controlFlow
            = AnalyzeStandardRegisteredQueryLoopControlFlow(body);
        if (controlFlow.Loops.Length != 2
            || controlFlow.Loops[0].Header != 1
            || controlFlow.Loops[0].Members.Count
                != (hasInnerControlExit ? 5 : 4)
            || !controlFlow.Loops[0].Members.Contains(1)
            || !controlFlow.Loops[0].Members.Contains(2)
            || !controlFlow.Loops[0].Members.Contains(3)
            || !controlFlow.Loops[0].Members.Contains(4)
            || (hasInnerControlExit
                && !controlFlow.Loops[0].Members.Contains(5))
            || (hasInnerControlExit
                ? controlFlow.Loops[0].ExitTargets is not [6]
                : controlFlow.Loops[0].ExitTargets is not [5])
            || controlFlow.Loops[1].Header != 2
            || controlFlow.Loops[1].Members.Count
                != (hasInnerControlExit ? 3 : 2)
            || !controlFlow.Loops[1].Members.Contains(2)
            || !controlFlow.Loops[1].Members.Contains(3)
            || (hasInnerControlExit
                && !controlFlow.Loops[1].Members.Contains(4))
            || (hasInnerControlExit
                ? controlFlow.Loops[1].ExitTargets is not [5]
                : controlFlow.Loops[1].ExitTargets is not [4]))
        {
            throw Unsupported(
                "The registered-query Body does not contain the bounded nested-loop CFG.");
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A nested registered-query loop BlockEntryState is not in block order.");
            }
        }

        var entryState = body.GetRecord(0);
        if (entryState.Length != 8 || entryState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Invalid(
                "A nested registered-query loop preheader must have an empty entry state.");
        }

        var preheaderCode = body.GetBlockCode(0);
        var outerHeaderCode = body.GetBlockCode(1);
        var innerFirstCode = body.GetBlockCode(2);
        var innerSecondCode = body.GetBlockCode(3);
        var innerIsPostTested
            = !hasInnerControlExit && innerFirstCode[0] != 0xE4;
        var innerHeaderBlock = innerIsPostTested ? 3 : 2;
        var innerBodyBlock = hasInnerControlExit ? 3
            : innerIsPostTested ? 2 : 3;
        var innerUpdateBlock = hasInnerControlExit ? 4 : innerBodyBlock;
        var innerHeaderCode
            = innerIsPostTested ? innerSecondCode : innerFirstCode;
        var innerUpdateCode
            = hasInnerControlExit ? body.GetBlockCode(4)
                : innerIsPostTested ? innerFirstCode : innerSecondCode;
        var outerUpdateBlock = hasInnerControlExit ? 5 : 4;
        var exitBlock = body.BlockCount - 1;
        var outerUpdateCode = body.GetBlockCode(outerUpdateBlock);
        var outerIsPostTested = outerHeaderCode[0] != 0xE4;
        if (outerIsPostTested && outerUpdateCode.Length < 4)
        {
            throw Unsupported(
                "A nested post-test loop has a truncated condition Block.");
        }

        var outerConditionCode = outerIsPostTested
            ? outerUpdateCode[^4..] : outerHeaderCode;
        var innerControlCode = hasInnerControlExit
            ? body.GetBlockCode(3) : default;
        var exitCode = body.GetBlockCode(exitBlock);
        if (preheaderCode.Length != 12
            || outerConditionCode.Length != 4
            || outerConditionCode[0] != 0xE4
            || (outerIsPostTested
                ? outerHeaderCode.Length != 4
                    || outerHeaderCode[0] != 0xE3
                    || outerHeaderCode[1] != 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(
                        outerHeaderCode[2..]) != 2
                : outerHeaderCode.Length != 4)
            || innerHeaderCode.Length != 4 || innerHeaderCode[0] != 0xE4
            || innerUpdateCode.Length < (hasInnerControlExit ? 4 : 8)
            || innerUpdateCode[^4] != 0xE3
            || innerUpdateCode[^3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(innerUpdateCode[^2..])
                != innerHeaderBlock
            || (hasInnerControlExit
                && (outerIsPostTested || innerIsPostTested
                    || innerControlCode.Length < 6
                    || innerControlCode[^6] != 0x11
                    || innerControlCode[^4] != 0xE4))
            || outerUpdateCode.Length < 8
            || (outerIsPostTested
                ? outerUpdateCode[^4] != 0xE4
                : outerUpdateCode[^4] != 0xE3
                    || outerUpdateCode[^3] != 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(
                        outerUpdateCode[^2..]) != 1)
            || exitCode.Length != 2 || exitCode[0] != 0x17)
        {
            throw Unsupported(
                "The nested registered-query loop is outside the bounded outer/inner/update/exit form.");
        }

        var preheaderBranchOffset = preheaderCode.Length - 4;
        if (preheaderCode[preheaderBranchOffset] != 0xE3
            || preheaderCode[preheaderBranchOffset + 1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                preheaderCode[(preheaderBranchOffset + 2)..]) != 1)
        {
            throw Invalid(
                "A nested registered-query loop preheader has an invalid entry edge.");
        }

        var localInitializers = new Dictionary<int, int>(2);
        ReadOnlySpan<byte> queryInstruction = default;
        for (var position = 0; position < preheaderBranchOffset;)
        {
            var opcode = preheaderCode[position];
            if (opcode == 0x11)
            {
                var instruction = preheaderCode.Slice(position, 2);
                var destination = instruction[1] & 0x0F;
                var parameter = instruction[1] >> 4;
                if (destination >= 14
                    || parameter >= function.ParameterCount
                    || !localInitializers.TryAdd(destination, parameter))
                {
                    throw Invalid(
                        "A nested registered-query loop preheader has a duplicate or invalid local initializer.");
                }
                position += 2;
                continue;
            }

            if (opcode == 0xDE && queryInstruction.IsEmpty)
            {
                queryInstruction = preheaderCode.Slice(position, 4);
                position += 4;
                continue;
            }

            throw Unsupported(
                "The nested registered-query loop preheader contains an unsupported initializer.");
        }

        var outerRegister = outerConditionCode[1];
        var innerRegister = innerHeaderCode[1];
        var resultRegister = queryInstruction.IsEmpty
            ? -1 : queryInstruction[1] & 0x0F;
        if (outerRegister >= 14 || innerRegister >= 14
            || resultRegister < 0 || resultRegister >= 14
            || localInitializers.Count != 2
            || !localInitializers.TryGetValue(outerRegister,
                out var outerParameter)
            || !localInitializers.TryGetValue(innerRegister,
                out var innerParameter))
        {
            throw Invalid(
                "A nested registered-query loop preheader does not initialize its exact local registers.");
        }

        if (outerRegister == innerRegister
            || outerRegister == resultRegister
            || innerRegister == resultRegister
            || !IsStandardRegisteredQueryBooleanFormal(function,
                outerParameter)
            || !IsStandardRegisteredQueryBooleanFormal(function,
                innerParameter)
            || innerHeaderCode[1] != innerRegister
            || exitCode[1] != resultRegister)
        {
            throw Invalid(
                "A nested registered-query loop changes a formal or local register identity.");
        }

        var outerEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(outerConditionCode[2..]);
        var innerEdgeIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(innerHeaderCode[2..]);
        var innerControlParameter = -1;
        var innerControlEdgeIndex = -1;
        var innerControlIsContinue = false;
        if (hasInnerControlExit)
        {
            var conditionLoadOffset = innerControlCode.Length - 6;
            var branchOffset = innerControlCode.Length - 4;
            var branchRegister
                = innerControlCode[conditionLoadOffset + 1] & 0x0F;
            innerControlParameter
                = innerControlCode[conditionLoadOffset + 1] >> 4;
            innerControlEdgeIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    innerControlCode[(branchOffset + 2)..]);
            if (branchRegister >= 14
                || innerControlCode[branchOffset + 1] != branchRegister
                || !IsStandardRegisteredQueryBooleanFormal(function,
                    innerControlParameter))
            {
                throw Invalid(
                    "A nested registered-query loop control branch has an invalid Boolean condition.");
            }
        }

        var outerEdge = body.GetRecord(outerEdgeIndex);
        var innerEdge = body.GetRecord(innerEdgeIndex);
        var innerControlEdge = hasInnerControlExit
            ? body.GetRecord(innerControlEdgeIndex) : default;
        if (outerEdgeIndex != (hasInnerControlExit
                ? (ushort)7 : (outerIsPostTested ? (ushort)7 : (ushort)6))
            || innerEdgeIndex != (hasInnerControlExit
                ? (ushort)8 : (outerIsPostTested ? (ushort)6 : (ushort)7))
            || outerEdge.Length != 8 || outerEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(outerEdge[4..])
                != (outerIsPostTested ? 1 : 2)
            || BinaryPrimitives.ReadUInt16LittleEndian(outerEdge[6..])
                != exitBlock
            || innerEdge.Length != 8 || innerEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(innerEdge[4..])
                != innerBodyBlock
            || BinaryPrimitives.ReadUInt16LittleEndian(innerEdge[6..])
                != outerUpdateBlock
            || (hasInnerControlExit
                && (innerControlEdgeIndex != 9
                    || innerControlEdge.Length != 8
                    || innerControlEdge[0] != 0x06
                    || (BinaryPrimitives.ReadUInt16LittleEndian(
                            innerControlEdge[4..]) != innerHeaderBlock
                        && BinaryPrimitives.ReadUInt16LittleEndian(
                            innerControlEdge[4..]) != outerUpdateBlock)
                    || BinaryPrimitives.ReadUInt16LittleEndian(
                        innerControlEdge[6..]) != innerUpdateBlock)))
        {
            throw Invalid(
                "A nested registered-query loop has invalid conditional edge targets.");
        }

        if (hasInnerControlExit)
        {
            innerControlIsContinue
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    innerControlEdge[4..]) == innerHeaderBlock;
        }

        var query = ReadStandardRegisteredQueryValueInstruction(body,
            queryInstruction, resultRegister, typeDefs, typeSpecs,
            fields, fieldPaths, imports, nativePointerTypes,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        if (query.Type != PrimitiveType.NativeInt)
        {
            throw Invalid(
                "A nested registered-query loop query has a non-NativeInt result.");
        }

        var headerState = body.GetRecord(1);
        if (headerState.Length != 23 || headerState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(headerState[4..]) != 3)
        {
            throw Invalid(
                "A nested registered-query loop has invalid loop-carried register rows.");
        }

        for (var blockIndex = 1; blockIndex < body.BlockCount; blockIndex++)
        {
            if (!headerState.SequenceEqual(body.GetRecord(blockIndex)))
            {
                throw Invalid(
                    "A nested registered-query loop edge changes a live register carrier or type.");
            }
        }

        var stateRegisters = new HashSet<int>();
        var hasOuterState = false;
        var hasInnerState = false;
        var hasResultState = false;
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            var rowOffset = 8 + (rowIndex * 5);
            var register = headerState[rowOffset];
            var carrierKind = headerState[rowOffset + 1];
            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                headerState[(rowOffset + 2)..]);
            if (register >= 14 || carrierKind != 0
                || headerState[rowOffset + 4] != 0
                || !stateRegisters.Add(register))
            {
                throw Invalid(
                    "A nested registered-query loop has a duplicate or invalid loop-carried register row.");
            }

            var type = ReadStandardRegisteredQueryType(body, typeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports);
            if (register == outerRegister)
            {
                hasOuterState = type == PrimitiveType.Bool;
            }
            else if (register == innerRegister)
            {
                hasInnerState = type == PrimitiveType.Bool;
            }
            else if (register == resultRegister)
            {
                hasResultState = type == PrimitiveType.NativeInt;
            }
            else
            {
                throw Invalid(
                    "A nested registered-query loop carries an unexpected register.");
            }
        }

        if (!hasOuterState || !hasInnerState || !hasResultState)
        {
            throw Invalid(
                "A nested registered-query loop has incompatible Boolean or NativeInt register types.");
        }

        var outerVariable = variables.Count;
        var outerName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_outer_loop_");
        variables.Add(new BoundVariable(outerName,
            PrimitiveType.Bool, true, default));
        var innerVariable = variables.Count;
        var innerName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_inner_loop_");
        variables.Add(new BoundVariable(innerName,
            PrimitiveType.Bool, true, default));
        var resultVariable = variables.Count;
        var resultName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_nested_query_result_");
        variables.Add(new BoundVariable(resultName,
            PrimitiveType.NativeInt, true, default));

        var innerAssignmentCode
            = hasInnerControlExit && innerControlIsContinue
                ? innerControlCode[..^6] : innerUpdateCode[..^4];
        var innerAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                innerAssignmentCode, function, innerRegister,
                resultRegister, innerVariable, resultVariable, typeDefs,
                typeSpecs, fields, fieldPaths, imports, nativePointerTypes,
                signatureIndexByAnchorMethod, structureTypeByTypeDef);
        if (innerAssignments.Length is < 1 or > 2
            || innerAssignments[^1] is not BoundAssignment {
                Variable: var innerAssignmentVariable,
                Value: BoundRead { Type: PrimitiveType.Bool },
            }
            || innerAssignmentVariable != innerVariable)
        {
            throw Unsupported(
                "The nested registered-query inner loop has an unsupported update body.");
        }

        if (innerAssignments.Length == 2
            && (innerAssignments[0]
                    is not BoundAssignment innerResultAssignment
                || innerResultAssignment.Variable != resultVariable
                || innerResultAssignment.Value is not BoundLayoutQuery {
                    Type: PrimitiveType.NativeInt,
                }))
        {
            throw Unsupported(
                "The nested registered-query inner loop has an unsupported query update.");
        }

        var outerAssignments
            = ReadStandardRegisteredQueryLoopAssignments(body,
                outerUpdateCode[..^4], function, outerRegister,
                resultRegister, outerVariable, resultVariable, typeDefs,
                typeSpecs, fields, fieldPaths, imports, nativePointerTypes,
                signatureIndexByAnchorMethod, structureTypeByTypeDef);
        if (outerAssignments.Length is < 1 or > 2
            || outerAssignments[^1] is not BoundAssignment {
                Variable: var outerAssignmentVariable,
                Value: BoundRead { Type: PrimitiveType.Bool },
            }
            || outerAssignmentVariable != outerVariable)
        {
            throw Unsupported(
                "The nested registered-query outer loop has an unsupported update body.");
        }

        if (outerAssignments.Length == 2
            && (outerAssignments[0] is not BoundAssignment resultAssignment
                || resultAssignment.Variable != resultVariable
                || resultAssignment.Value is not BoundLayoutQuery {
                    Type: PrimitiveType.NativeInt,
                }))
        {
            throw Unsupported(
                "The nested registered-query outer loop has an unsupported query update.");
        }

        var innerLoopStatements = innerAssignments;
        if (hasInnerControlExit)
        {
            var innerControlStatement = new BoundIf(
                new BoundRead(innerControlParameter, PrimitiveType.Bool,
                    default),
                innerControlIsContinue
                    ? new BoundBlock([new BoundContinue(default)], default)
                    : new BoundBlock([new BoundBreak(default)], default),
                null, default);
            innerLoopStatements = innerControlIsContinue
                ? [.. innerAssignments, innerControlStatement]
                : [innerControlStatement, .. innerAssignments];
        }

        var innerLoop = new BoundWhile(
            new BoundRead(innerVariable, PrimitiveType.Bool, default),
            new BoundBlock(innerLoopStatements, default), default) {
                IsPostTested = innerIsPostTested,
            };
        ImmutableArray<BoundStatement> outerLoopStatements
            = [innerLoop, .. outerAssignments];
        return [
            new BoundLocal(outerVariable,
                new BoundRead(outerParameter, PrimitiveType.Bool, default),
                default),
            new BoundLocal(innerVariable,
                new BoundRead(innerParameter, PrimitiveType.Bool, default),
                default),
            new BoundLocal(resultVariable, query, default),
            new BoundWhile(new BoundRead(outerVariable,
                    PrimitiveType.Bool, default),
                new BoundBlock(outerLoopStatements, default), default) {
                IsPostTested = outerIsPostTested,
            },
            new BoundReturn(new BoundRead(resultVariable,
                PrimitiveType.NativeInt, default), default),
        ];
    }

    private static bool IsStandardRegisteredQueryBooleanFormal(
        BoundFunction function, int variable)
        => (uint)variable < (uint)function.ParameterCount
            && function.Variables[variable] is {
                Type: PrimitiveType.Bool,
                IsOut: false,
                IsInit: false,
                IsImplicitRef: false,
                IsImplicitReferenceTemporary: false,
                IsInitializationLocal: false,
            };

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryLoopControlBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (body.BlockCount != 5 || body.RecordCount != 7
            || body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || (body.MapMask & ~0x0A) != 0
            || (body.MapMask & 0x02) == 0
            || function.ReturnType != PrimitiveType.NativeInt
            || function.ParameterCount < 2
            || function.Variables.Length != function.ParameterCount
            || variables.Count != function.ParameterCount)
        {
            throw Unsupported(
                "The bounded Standard query loop-break Body requires two Boolean formals and a NativeInt result.");
        }

        _ = ValidateStandardRegisteredQueryLoopRegion(
            AnalyzeStandardRegisteredQueryLoopControlFlow(body),
            body.BlockCount);

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A registered-query loop-break BlockEntryState is not in block order.");
            }
        }

        var preheaderEntry = body.GetRecord(0);
        if (preheaderEntry.Length != 8 || preheaderEntry[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(preheaderEntry[4..])
                != 0)
        {
            throw Invalid(
                "A registered-query loop-break preheader must have an empty entry state.");
        }

        var preheaderCode = body.GetBlockCode(0);
        var exitBlock = body.BlockCount - 1;
        var exitCode = body.GetBlockCode(exitBlock);
        if (preheaderCode.Length != 10 || preheaderCode[0] != 0x11
            || preheaderCode[2] != 0xDE || preheaderCode[6] != 0xE3
            || preheaderCode[7] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(preheaderCode[8..])
                != 1
            || exitCode.Length != 2 || exitCode[0] != 0x17)
        {
            throw Unsupported(
                "The registered-query loop-break Body is outside its bounded preheader/exit form.");
        }

        var conditionRegister = preheaderCode[1] & 0x0F;
        var conditionParameter = preheaderCode[1] >> 4;
        var resultRegister = preheaderCode[3] & 0x0F;
        if (conditionRegister >= 14 || resultRegister >= 14
            || conditionRegister >= resultRegister
            || conditionParameter >= function.ParameterCount
            || function.Variables[conditionParameter].Type
                != PrimitiveType.Bool
            || exitCode[1] != resultRegister)
        {
            throw Invalid(
                "A registered-query loop-break Body changes its formal or result register.");
        }

        var query = ReadStandardRegisteredQueryValueInstruction(body,
            preheaderCode.Slice(2, 4), resultRegister, typeDefs, typeSpecs,
            fields, fieldPaths, imports, nativePointerTypes,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        var branchCount = 0;
        var expectedEdgeRecord = body.BlockCount;
        var headerBlock = -1;
        var breakBlock = -1;
        var updateBlock = -1;
        var breakParameter = -1;
        var mainTrueBlock = -1;
        var mainFalseBlock = -1;
        var breakTrueBlock = -1;
        var breakFalseBlock = -1;

        for (var blockIndex = 1; blockIndex < exitBlock; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            int branchOffset;
            bool isMainBranch;
            if (code.Length == 4 && code[0] == 0xE4)
            {
                branchOffset = 0;
                isMainBranch = true;
                if (code[1] == conditionRegister)
                {
                    if (headerBlock >= 0)
                    {
                        throw Invalid(
                            "A registered-query loop has multiple condition headers.");
                    }

                    headerBlock = blockIndex;
                }
                else
                {
                    throw Invalid(
                            "A registered-query loop has an unclassified direct condition branch.");
                }
            }
            else if (code.Length == 6 && code[0] == 0x11
                && code[2] == 0xE4)
            {
                branchOffset = 2;
                isMainBranch = false;
                var move = code[1];
                var temporaryRegister = move & 0x0F;
                breakParameter = move >> 4;
                if (temporaryRegister >= 14
                    || code[3] != temporaryRegister
                    || breakParameter >= function.ParameterCount
                    || function.Variables[breakParameter].Type
                        != PrimitiveType.Bool
                    || breakBlock >= 0)
                {
                    throw Invalid(
                        "A registered-query loop break does not branch on one Boolean formal.");
                }

                breakBlock = blockIndex;
            }
            else if (code.Length >= 4 && code[^4] == 0xE3
                && code[^3] == 0)
            {
                if (updateBlock >= 0)
                {
                    throw Unsupported(
                        "A registered-query loop-break Body has multiple update blocks.");
                }

                updateBlock = blockIndex;
                continue;
            }
            else
            {
                throw Unsupported(
                    "A registered-query loop-break block has an unsupported branch or update form.");
            }

            var edgeRecordIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    code[(branchOffset + 2)..]);
            if (edgeRecordIndex != expectedEdgeRecord
                || edgeRecordIndex >= body.RecordCount)
            {
                throw Invalid(
                    "A registered-query loop-break ConditionalEdge is not in block order.");
            }

            expectedEdgeRecord++;
            branchCount++;
            var edge = body.GetRecord(edgeRecordIndex);
            if (edge.Length != 8 || edge[0] != 0x06)
            {
                throw Invalid(
                    "A registered-query loop branch has no ConditionalEdge record.");
            }

            var trueBlock = BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]);
            var falseBlock = BinaryPrimitives.ReadUInt16LittleEndian(
                edge[6..]);
            if (isMainBranch)
            {
                mainTrueBlock = trueBlock;
                mainFalseBlock = falseBlock;
            }
            else
            {
                breakTrueBlock = trueBlock;
                breakFalseBlock = falseBlock;
            }
        }

        if (branchCount != 2 || expectedEdgeRecord != body.RecordCount
            || headerBlock < 0 || breakBlock < 0 || updateBlock < 0
            || mainFalseBlock != exitBlock || mainTrueBlock != breakBlock
            || breakFalseBlock != updateBlock)
        {
            throw Invalid(
                "A registered-query loop-break CFG has incomplete or inconsistent exits.");
        }

        var isBreakExit = breakTrueBlock == exitBlock;
        var isContinueExit = breakTrueBlock == headerBlock;
        if (!isBreakExit && !isContinueExit)
        {
            throw Invalid(
                "A registered-query loop-control branch targets neither its break nor continue block.");
        }

        var isPostTested = headerBlock != 1;
        if (headerBlock != (isPostTested ? 3 : 1)
            || breakBlock != (isPostTested ? 1 : 2)
            || updateBlock != (isPostTested ? 2 : 3))
        {
            throw Unsupported(
                "The registered-query loop-break CFG is not the bounded pretest/post-test shape.");
        }

        var headerState = body.GetRecord(headerBlock);
        if (headerState.Length != 18 || headerState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(headerState[4..]) != 2
            || headerState[8] != conditionRegister
            || headerState[9] != 0 || headerState[12] != 0
            || headerState[13] != resultRegister
            || headerState[14] != 0 || headerState[17] != 0)
        {
            throw Invalid(
                "A registered-query loop-break header has invalid live registers.");
        }

        for (var blockIndex = 1; blockIndex <= exitBlock; blockIndex++)
        {
            if (!headerState.SequenceEqual(body.GetRecord(blockIndex)))
            {
                throw Invalid(
                    "A registered-query loop-break edge changes a live register carrier or type.");
            }
        }

        var conditionTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(headerState[10..]);
        var resultTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(headerState[15..]);
        if (ReadStandardRegisteredQueryType(body, conditionTypeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports) != PrimitiveType.Bool
            || ReadStandardRegisteredQueryType(body, resultTypeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports)
                != PrimitiveType.NativeInt)
        {
            throw Invalid(
                "A registered-query loop-break header has incompatible register types.");
        }

        var conditionVariable = variables.Count;
        var conditionName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_loop_condition_");
        variables.Add(new BoundVariable(conditionName,
            PrimitiveType.Bool, true, default));
        var resultVariable = variables.Count;
        var resultName = GetUniqueStandardRegisteredQueryLocalName(
            variables, "__trrm_query_result_");
        variables.Add(new BoundVariable(resultName,
            PrimitiveType.NativeInt, true, default));

        var updateCode = body.GetBlockCode(updateBlock);
        if (updateCode.Length < 4 || updateCode[^4] != 0xE3
            || updateCode[^3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(updateCode[^2..])
                != headerBlock)
        {
            throw Invalid(
                "A registered-query loop-break update block has an invalid backedge.");
        }

        var loopBodyStatements
            = ImmutableArray.Create<BoundStatement>(new BoundIf(
                new BoundRead(breakParameter, PrimitiveType.Bool, default),
                isBreakExit
                    ? new BoundBlock([new BoundBreak(default)], default)
                    : new BoundBlock([new BoundContinue(default)], default),
                null, default))
            .AddRange(ReadStandardRegisteredQueryLoopAssignments(body,
                updateCode[..^4], function, conditionRegister,
                resultRegister, conditionVariable, resultVariable, typeDefs,
                typeSpecs, fields, fieldPaths, imports, nativePointerTypes,
                signatureIndexByAnchorMethod, structureTypeByTypeDef));

        return [
            new BoundLocal(conditionVariable,
                new BoundRead(conditionParameter, PrimitiveType.Bool,
                    default), default),
            new BoundLocal(resultVariable, query, default),
            new BoundWhile(
                new BoundRead(conditionVariable, PrimitiveType.Bool, default),
                new BoundBlock(loopBodyStatements, default), default) {
                IsPostTested = isPostTested,
            },
            new BoundReturn(new BoundRead(resultVariable,
                PrimitiveType.NativeInt, default), default),
        ];
    }

    private ref struct StandardRegisteredQueryValueControlFlowDecoder
    {
        private readonly TrRmRegisterBodyReader _body;
        private readonly BoundFunction _function;
        private readonly TrRmTypeDefTableReader _typeDefs;
        private readonly TrRmTypeSpecTableReader _typeSpecs;
        private readonly TrRmFieldDefTableReader _fields;
        private readonly TrRmFieldPathTableReader _fieldPaths;
        private readonly TrRmImportTableReader _imports;
        private readonly List<NativePointerTypeDefinition>
            _nativePointerTypes;
        private readonly int[] _signatureIndexByAnchorMethod;
        private readonly int[] _structureTypeByTypeDef;
        private readonly bool[] _visitedBlocks;
        private readonly bool[] _conditionRegisters;
        private int _nextEdgeRecord;
        private int _conditionalCount;
        private int _resultRegister;

        public StandardRegisteredQueryValueControlFlowDecoder(
            TrRmRegisterBodyReader body, BoundFunction function,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
        {
            _body = body;
            _function = function;
            _typeDefs = typeDefs;
            _typeSpecs = typeSpecs;
            _fields = fields;
            _fieldPaths = fieldPaths;
            _imports = imports;
            _nativePointerTypes = nativePointerTypes;
            _signatureIndexByAnchorMethod = signatureIndexByAnchorMethod;
            _structureTypeByTypeDef = structureTypeByTypeDef;
            _visitedBlocks = new bool[body.BlockCount];
            _conditionRegisters = new bool[14];
            _nextEdgeRecord = body.BlockCount;
            _conditionalCount = 0;
            _resultRegister = -1;
        }

        public ImmutableArray<BoundStatement> Decode()
        {
            if (_body.BlockCount < 4
                || _body.BlockCount
                    > MaximumStandardRegisteredQueryFlowBlocks
                || _body.RecordCount
                    > MaximumStandardRegisteredQueryFlowRecords
                || _body.HomeCount != 0
                || _body.MethodCount != 0 || _body.ConstantCount != 0
                || (_body.MapMask & ~0x0A) != 0
                || (_body.MapMask & 0x02) == 0
                || _function.ReturnType != PrimitiveType.NativeInt
                || _function.ParameterCount != _function.Variables.Length)
            {
                throw Unsupported(
                    "The bounded Standard query consumer requires a Boolean-formal NativeInt query control-flow Body.");
            }

            for (var blockIndex = 0; blockIndex < _body.BlockCount;
                blockIndex++)
            {
                var block = _body.GetBlock(blockIndex);
                if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                    != blockIndex)
                {
                    throw Invalid(
                        "A registered-query control-flow BlockEntryState is not in block order.");
                }
            }

            var (nextBlock, completionBlock, completionTarget, expression)
                = ParseNode(0);
            if (nextBlock != _body.BlockCount
                || completionBlock != _body.BlockCount - 1
                || completionTarget is not null
                || expression.Type != _function.ReturnType
                || _resultRegister < 0
                || _conditionalCount == 0
                || _body.RecordCount
                    != _body.BlockCount + _conditionalCount
                || _nextEdgeRecord != _body.RecordCount
                || _conditionRegisters[_resultRegister])
            {
                throw Invalid(
                    "A registered-query control-flow Body is incomplete or has inconsistent join evidence.");
            }

            for (var blockIndex = 0; blockIndex < _visitedBlocks.Length;
                blockIndex++)
            {
                if (!_visitedBlocks[blockIndex])
                {
                    throw Invalid(
                        "A registered-query control-flow Body contains an unreachable block.");
                }
            }

            return [new BoundReturn(expression, default)];
        }

        private readonly int ReadJoinRegister(int blockIndex)
        {
            var record = _body.GetRecord(blockIndex);
            if (record.Length != 13 || record[0] != 0x01
                || BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) != 1
                || record[8] >= 14 || record[9] != 0
                || record[12] != 0)
            {
                throw Invalid(
                    "A registered-query value join must carry one ordinary NativeInt value.");
            }

            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                record[10..]);
            if (ReadStandardRegisteredQueryType(_body, typeMapIndex,
                    _typeSpecs, _nativePointerTypes,
                    _signatureIndexByAnchorMethod, _structureTypeByTypeDef,
                    _imports) != PrimitiveType.NativeInt)
            {
                throw Invalid(
                    "A registered-query value join has a non-NativeInt TypeMap entry.");
            }

            return record[8];
        }

        private (int NextBlock, int CompletionBlock, int? CompletionTarget,
            BoundExpression Expression) ParseNode(int startBlock)
        {
            if ((uint)startBlock >= (uint)_body.BlockCount
                || _visitedBlocks[startBlock])
            {
                throw Invalid(
                    "A registered-query control-flow tree has an invalid or repeated block.");
            }

            _visitedBlocks[startBlock] = true;
            var block = _body.GetBlock(startBlock);
            var entryStateIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                block[8..]);
            if (entryStateIndex != startBlock)
            {
                throw Invalid(
                    "A registered-query control-flow BlockEntryState is not in block order.");
            }

            var code = _body.GetBlockCode(startBlock);
            if (code.Length == 6 && code[0] == 0x11
                && code[2] == 0xE4)
            {
                if (!IsEmptyBlockEntry(_body.GetRecord(startBlock)))
                {
                    throw Invalid(
                        "A registered-query branch block has unexpected incoming value state.");
                }

                var conditionRegister = code[1] & 0x0F;
                var conditionParameter = code[1] >> 4;
                if (conditionRegister >= 14
                    || _conditionRegisters[conditionRegister]
                    || conditionParameter >= _function.ParameterCount
                    || _function.Variables[conditionParameter].Type
                        != PrimitiveType.Bool
                    || code[3] != conditionRegister)
                {
                    throw Invalid(
                        "A registered-query branch does not read a distinct Boolean formal.");
                }

                _conditionRegisters[conditionRegister] = true;
                var edgeRecordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(code[4..]);
                if (edgeRecordIndex != _nextEdgeRecord
                    || edgeRecordIndex >= _body.RecordCount)
                {
                    throw Invalid(
                        "A registered-query conditional edge is not in canonical block order.");
                }

                _nextEdgeRecord++;
                _conditionalCount++;
                var edge = _body.GetRecord(edgeRecordIndex);
                if (edge.Length != 8 || edge[0] != 0x06)
                {
                    throw Invalid(
                        "A registered-query branch does not reference a ConditionalEdge record.");
                }

                var trueBlock = BinaryPrimitives.ReadUInt16LittleEndian(
                    edge[4..]);
                var falseBlock = BinaryPrimitives.ReadUInt16LittleEndian(
                    edge[6..]);
                if (trueBlock != startBlock + 1)
                {
                    throw Invalid(
                        "A registered-query true edge is outside canonical tree order.");
                }

                var (trueNextBlock, _, trueCompletionTarget,
                    trueExpression) = ParseNode(trueBlock);
                if (falseBlock != trueNextBlock)
                {
                    throw Invalid(
                        "A registered-query false edge is outside canonical tree order.");
                }

                var (falseNextBlock, _,
                    falseCompletionTarget, falseExpression)
                    = ParseNode(falseBlock);
                var joinBlock = falseNextBlock;
                if ((uint)joinBlock >= (uint)_body.BlockCount
                    || _visitedBlocks[joinBlock]
                    || trueCompletionTarget != joinBlock
                    || falseCompletionTarget != joinBlock)
                {
                    throw Invalid(
                        "A registered-query branch's incoming paths do not converge at its join.");
                }

                _visitedBlocks[joinBlock] = true;
                var joinRegister = ReadJoinRegister(joinBlock);
                if (_resultRegister < 0)
                {
                    _resultRegister = joinRegister;
                }
                else if (joinRegister != _resultRegister)
                {
                    throw Invalid(
                        "Registered-query joins disagree on the NativeInt result register.");
                }

                var joinCode = _body.GetBlockCode(joinBlock);
                int? completionTarget;
                if (joinCode.Length == 4 && joinCode[0] == 0xE3
                    && joinCode[1] == 0)
                {
                    completionTarget
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            joinCode[2..]);
                }
                else if (joinCode.Length == 2 && joinCode[0] == 0x17
                    && joinCode[1] == joinRegister)
                {
                    completionTarget = null;
                }
                else
                {
                    throw Unsupported(
                        "A registered-query join must branch to its parent or return the NativeInt result.");
                }

                var condition = new BoundRead(conditionParameter,
                    PrimitiveType.Bool, default);
                return (joinBlock + 1, joinBlock, completionTarget,
                    new BoundConditional(condition, trueExpression,
                        falseExpression, PrimitiveType.NativeInt, default));
            }

            if (!IsEmptyBlockEntry(_body.GetRecord(startBlock))
                || code.Length != 8 || code[0] != 0xDE
                || (_resultRegister >= 0
                    && (code[1] & 0x0F) != _resultRegister))
            {
                throw Unsupported(
                    "A registered-query branch leaf must be one NativeInt layout query.");
            }

            var leafRegister = code[1] & 0x0F;
            if (leafRegister >= 14)
            {
                throw Invalid(
                    "A registered-query query result uses a nonbinding register.");
            }

            if (_resultRegister < 0)
            {
                _resultRegister = leafRegister;
            }

            var leafJoin = BinaryPrimitives.ReadUInt16LittleEndian(
                code[6..]);
            var query = ReadStandardRegisteredQueryValueBranch(_body,
                startBlock, _resultRegister, leafJoin, _typeDefs, _typeSpecs,
                _fields, _fieldPaths, _imports, _nativePointerTypes,
                _signatureIndexByAnchorMethod, _structureTypeByTypeDef);
            return (startBlock + 1, startBlock, leafJoin, query);
        }

        private static bool IsEmptyBlockEntry(ReadOnlySpan<byte> record)
        {
            return record.Length == 8 && record[0] == 0x01
                && BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) == 0;
        }
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryValueControlFlow(
            TrRmRegisterBodyReader body, BoundFunction function,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        var decoder = new StandardRegisteredQueryValueControlFlowDecoder(
            body, function, typeDefs, typeSpecs, fields, fieldPaths, imports,
            nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        return decoder.Decode();
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryNestedValueDiamond(
            TrRmRegisterBodyReader body, BoundFunction function,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (body.BlockCount != 7 || body.RecordCount != 9
            || body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || (body.MapMask & ~0x0A) != 0
            || (body.MapMask & 0x02) == 0
            || function.ReturnType != PrimitiveType.NativeInt
            || function.ParameterCount != 2
            || function.Variables.Length != 2
            || function.Variables[0].Type != PrimitiveType.Bool
            || function.Variables[1].Type != PrimitiveType.Bool)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires one nested Boolean-formal NativeInt query diamond.");
        }

        static bool IsEmptyBlockEntry(ReadOnlySpan<byte> record)
        {
            return record.Length == 8 && record[0] == 0x01
                && BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) == 0;
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A nested registered-query diamond BlockEntryState is not in block order.");
            }
        }

        for (var blockIndex = 0; blockIndex < 5; blockIndex++)
        {
            if (!IsEmptyBlockEntry(body.GetRecord(blockIndex)))
            {
                throw Invalid(
                    "A nested registered-query diamond has nonempty state before a value join.");
            }
        }

        var innerEntry = body.GetRecord(5);
        var outerEntry = body.GetRecord(6);
        if (innerEntry.Length != 13 || outerEntry.Length != 13
            || innerEntry[0] != 0x01 || outerEntry[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(innerEntry[4..]) != 1
            || BinaryPrimitives.ReadUInt32LittleEndian(outerEntry[4..]) != 1
            || innerEntry[8] >= 14
            || outerEntry[8] != innerEntry[8]
            || innerEntry[9] != 0 || outerEntry[9] != 0
            || innerEntry[12] != 0 || outerEntry[12] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(innerEntry[10..])
                != BinaryPrimitives.ReadUInt16LittleEndian(
                    outerEntry[10..]))
        {
            throw Invalid(
                "Both nested query joins must carry the same NativeInt register.");
        }

        var resultRegister = innerEntry[8];
        var resultTypeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            innerEntry[10..]);
        if (ReadStandardRegisteredQueryType(body, resultTypeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports)
            != PrimitiveType.NativeInt)
        {
            throw Invalid(
                "A nested registered-query join must carry the NativeInt query result.");
        }

        var outerEdge = body.GetRecord(7);
        var innerEdge = body.GetRecord(8);
        if (outerEdge.Length != 8 || outerEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(outerEdge[4..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(outerEdge[6..]) != 4
            || innerEdge.Length != 8 || innerEdge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(innerEdge[4..]) != 2
            || BinaryPrimitives.ReadUInt16LittleEndian(innerEdge[6..]) != 3)
        {
            throw Invalid(
                "A nested registered-query diamond has invalid conditional edge targets.");
        }

        var outerEntryCode = body.GetBlockCode(0);
        var innerEntryCode = body.GetBlockCode(1);
        if (outerEntryCode.Length != 6 || outerEntryCode[0] != 0x11
            || (outerEntryCode[1] & 0x0F) >= 14
            || (outerEntryCode[1] >> 4) >= function.ParameterCount
            || function.Variables[outerEntryCode[1] >> 4].Type
                != PrimitiveType.Bool
            || outerEntryCode[2] != 0xE4
            || outerEntryCode[3] != (outerEntryCode[1] & 0x0F)
            || BinaryPrimitives.ReadUInt16LittleEndian(
                outerEntryCode[4..]) != 7
            || innerEntryCode.Length != 6 || innerEntryCode[0] != 0x11
            || (innerEntryCode[1] & 0x0F) >= 14
            || (innerEntryCode[1] >> 4) >= function.ParameterCount
            || function.Variables[innerEntryCode[1] >> 4].Type
                != PrimitiveType.Bool
            || innerEntryCode[2] != 0xE4
            || innerEntryCode[3] != (innerEntryCode[1] & 0x0F)
            || BinaryPrimitives.ReadUInt16LittleEndian(
                innerEntryCode[4..]) != 8)
        {
            throw Unsupported(
                "The bounded nested query consumer requires two Boolean formal branches.");
        }

        var outerCondition = new BoundRead(outerEntryCode[1] >> 4,
            PrimitiveType.Bool, default);
        var innerCondition = new BoundRead(innerEntryCode[1] >> 4,
            PrimitiveType.Bool, default);
        var innerTrueQuery = ReadStandardRegisteredQueryValueBranch(body, 2,
            resultRegister, 5, typeDefs, typeSpecs, fields, fieldPaths,
            imports, nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        var innerFalseQuery = ReadStandardRegisteredQueryValueBranch(body, 3,
            resultRegister, 5, typeDefs, typeSpecs, fields, fieldPaths,
            imports, nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        var outerFalseQuery = ReadStandardRegisteredQueryValueBranch(body, 4,
            resultRegister, 6, typeDefs, typeSpecs, fields, fieldPaths,
            imports, nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        var innerJoinCode = body.GetBlockCode(5);
        var outerJoinCode = body.GetBlockCode(6);
        if (innerJoinCode.Length != 4 || innerJoinCode[0] != 0xE3
            || innerJoinCode[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                innerJoinCode[2..]) != 6
            || outerJoinCode.Length != 2 || outerJoinCode[0] != 0x17
            || outerJoinCode[1] != resultRegister)
        {
            throw Unsupported(
                "The bounded nested query consumer requires an inner join followed by one NativeInt Return.");
        }

        var innerConditional = new BoundConditional(innerCondition,
            innerTrueQuery, innerFalseQuery, PrimitiveType.NativeInt, default);
        return [new BoundReturn(new BoundConditional(outerCondition,
            innerConditional, outerFalseQuery, PrimitiveType.NativeInt,
            default), default)];
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryValueDiamond(
            TrRmRegisterBodyReader body, BoundFunction function,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (body.BlockCount != 4 || body.RecordCount != 5
            || body.HomeCount != 0 || body.MethodCount != 0
            || body.ConstantCount != 0 || (body.MapMask & ~0x0A) != 0
            || (body.MapMask & 0x02) == 0
            || function.ReturnType != PrimitiveType.NativeInt
            || function.ParameterCount == 0
            || function.Variables.Length != function.ParameterCount)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires one Boolean-formal NativeInt query diamond.");
        }

        static bool IsEmptyBlockEntry(ReadOnlySpan<byte> record)
        {
            return record.Length == 8 && record[0] == 0x01
                && BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) == 0;
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A registered-query value diamond BlockEntryState is not in block order.");
            }
        }

        if (!IsEmptyBlockEntry(body.GetRecord(0))
            || !IsEmptyBlockEntry(body.GetRecord(1))
            || !IsEmptyBlockEntry(body.GetRecord(2)))
        {
            throw Invalid(
                "A registered-query value diamond has nonempty branch entry state.");
        }

        var mergedEntry = body.GetRecord(3);
        if (mergedEntry.Length != 13 || mergedEntry[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(mergedEntry[4..]) != 1
            || mergedEntry[8] >= 14 || mergedEntry[9] != 0
            || mergedEntry[12] != 0)
        {
            throw Invalid(
                "A registered-query join must carry one ordinary value.");
        }

        var mergedRegister = mergedEntry[8];
        var mergedTypeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            mergedEntry[10..]);
        if (ReadStandardRegisteredQueryType(body, mergedTypeMapIndex,
                typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports)
            != PrimitiveType.NativeInt)
        {
            throw Invalid(
                "A registered-query value join must carry the NativeInt query result.");
        }

        var edge = body.GetRecord(4);
        if (edge.Length != 8 || edge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]) != 2)
        {
            throw Invalid(
                "A registered-query value diamond has invalid branch targets.");
        }

        var entryCode = body.GetBlockCode(0);
        if (entryCode.Length != 6 || entryCode[0] != 0x11
            || (entryCode[1] & 0x0F) >= 14
            || (entryCode[1] >> 4) >= function.ParameterCount
            || function.Variables[entryCode[1] >> 4].Type
                != PrimitiveType.Bool
            || entryCode[2] != 0xE4
            || entryCode[3] != (entryCode[1] & 0x0F)
            || BinaryPrimitives.ReadUInt16LittleEndian(entryCode[4..]) != 4)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires a Boolean formal branch before the NativeInt query merge.");
        }

        var condition = new BoundRead(entryCode[1] >> 4,
            PrimitiveType.Bool, default);
        var trueQuery = ReadStandardRegisteredQueryValueBranch(body, 1,
            mergedRegister, 3, typeDefs, typeSpecs, fields, fieldPaths, imports,
            nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        var falseQuery = ReadStandardRegisteredQueryValueBranch(body, 2,
            mergedRegister, 3, typeDefs, typeSpecs, fields, fieldPaths, imports,
            nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        var joinCode = body.GetBlockCode(3);
        if (joinCode.Length != 2 || joinCode[0] != 0x17
            || joinCode[1] != mergedRegister)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires one NativeInt query Return after the value merge.");
        }

        return [new BoundReturn(new BoundConditional(condition, trueQuery,
            falseQuery, PrimitiveType.NativeInt, default), default)];
    }

    private static BoundLayoutQuery ReadStandardRegisteredQueryValueBranch(
        TrRmRegisterBodyReader body, int branchIndex, int resultRegister,
        int joinBlockTarget,
        TrRmTypeDefTableReader typeDefs,
        TrRmTypeSpecTableReader typeSpecs, TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths, TrRmImportTableReader imports,
        List<NativePointerTypeDefinition> nativePointerTypes,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef)
    {
        var branchCode = body.GetBlockCode(branchIndex);
        if (branchCode.Length != 8
            || branchCode[4] != 0xE3 || branchCode[5] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(branchCode[6..])
                != joinBlockTarget)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires a layout query on both NativeInt merge edges.");
        }

        return ReadStandardRegisteredQueryValueInstruction(body,
            branchCode[..4], resultRegister, typeDefs, typeSpecs, fields,
            fieldPaths, imports, nativePointerTypes,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
    }

    private static BoundLayoutQuery
        ReadStandardRegisteredQueryValueInstruction(
            TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
            int resultRegister, TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (instruction.Length != 4 || instruction[0] != 0xDE
            || (instruction[1] & 0x0F) != resultRegister
            || (instruction[1] >> 4) is not (1 or 2 or 3 or 4))
        {
            throw Unsupported(
                "A registered-query value definition must be a NativeInt layout query.");
        }

        var queryKind = instruction[1] >> 4;
        var mapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            instruction[2..]);
        BoundQuerySubject subject;
        if (queryKind is 1 or 2)
        {
            var subjectType = ReadStandardRegisteredQueryType(body,
                mapIndex, typeSpecs, nativePointerTypes,
                signatureIndexByAnchorMethod, structureTypeByTypeDef,
                imports);
            subject = new BoundQuerySubject(subjectType,
                BoundQuerySubjectKind.Type, null, null, false, false);
        }
        else
        {
            if (mapIndex >= body.FieldCount)
            {
                throw Invalid(
                    "A registered-query value diamond FieldMap index is outside its table.");
            }

            var fieldMapToken = BinaryPrimitives.ReadUInt32LittleEndian(
                body.FieldMap[(mapIndex * sizeof(uint))..]);
            var path = ReadStandardRegisteredQueryFieldPath(fieldMapToken,
                typeDefs, typeSpecs, fields, fieldPaths, imports,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
            var isOffsetOfPlace = queryKind == 4;
            if (isOffsetOfPlace && path.Steps.Length != 1)
            {
                throw Invalid(
                    "OffsetOfPlace requires an immediate one-field path.");
            }

            subject = isOffsetOfPlace
                ? new BoundQuerySubject(path.LeafType,
                    BoundQuerySubjectKind.FieldPathPlace, null, path, false,
                    false)
                : new BoundQuerySubject(path.RootType,
                    BoundQuerySubjectKind.Type, null, path, false, false);
        }

        return new BoundLayoutQuery(
            queryKind switch {
                1 => BoundLayoutQueryKind.SizeOf,
                2 => BoundLayoutQueryKind.AlignOf,
                3 => BoundLayoutQueryKind.OffsetOfType,
                _ => BoundLayoutQueryKind.OffsetOfPlace,
            },
            subject, default);
    }

    private static BoundTypeIdentityQuery
        ReadStandardRegisteredQueryIdentityInstruction(
            TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
            int resultRegister, TrRmTypeSpecTableReader typeSpecs,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef,
            TrRmImportTableReader imports)
    {
        if (instruction.Length != 4 || instruction[0] != 0xDE
            || (instruction[1] & 0x0F) != resultRegister
            || (instruction[1] >> 4) != 5)
        {
            throw Unsupported(
                "A loop-carried TypeIdentity definition must be a TypeOf query.");
        }

        var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            instruction[2..]);
        var subjectType = ReadStandardRegisteredQueryType(body,
            typeMapIndex, typeSpecs, nativePointerTypes,
            signatureIndexByAnchorMethod, structureTypeByTypeDef, imports);
        var subject = new BoundQuerySubject(subjectType,
            BoundQuerySubjectKind.Type, null, null, false, false);
        return new BoundTypeIdentityQuery(subject, default);
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryIdentityDiamond(
            TrRmRegisterBodyReader body, BoundFunction function,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        if (body.RecordCount != 5 || body.HomeCount != 0
            || body.MethodCount != 0 || body.ConstantCount != 0
            || body.FieldCount != 0 || body.MapMask != 0x02
            || function.ReturnType != PrimitiveType.Bool
            || function.ParameterCount != 1
            || function.Variables.Length != 1
            || function.Variables[0].Type != PrimitiveType.Bool)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires one Boolean-formal TypeIdentity diamond.");
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != blockIndex)
            {
                throw Invalid(
                    "A registered-query diamond BlockEntryState is not in block order.");
            }
        }

        static bool IsEmptyBlockEntry(ReadOnlySpan<byte> record)
        {
            return record.Length == 8 && record[0] == 0x01
                && BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) == 0;
        }

        if (!IsEmptyBlockEntry(body.GetRecord(0))
            || !IsEmptyBlockEntry(body.GetRecord(1))
            || !IsEmptyBlockEntry(body.GetRecord(2)))
        {
            throw Unsupported(
                "The bounded Standard query consumer requires empty entry states before both TypeIdentity branches.");
        }

        var mergedEntry = body.GetRecord(3);
        if (mergedEntry.Length != 13 || mergedEntry[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(mergedEntry[4..]) != 1
            || mergedEntry[9] != 2
            || BinaryPrimitives.ReadUInt16LittleEndian(mergedEntry[10..]) != 0
            || mergedEntry[12] != 0
            || mergedEntry[8] >= 14)
        {
            throw Invalid(
                "A registered-query join must carry one TypeIdentity in BlockEntryState.");
        }

        var mergedRegister = mergedEntry[8];
        var edge = body.GetRecord(4);
        if (edge.Length != 8 || edge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]) != 2)
        {
            throw Invalid(
                "A registered-query TypeIdentity diamond has invalid branch targets.");
        }

        var entryCode = body.GetBlockCode(0);
        if (entryCode.Length != 6 || entryCode[0] != 0x11
            || (entryCode[1] & 0x0F) >= 14
            || (entryCode[1] >> 4) != 0
            || entryCode[2] != 0xE4
            || entryCode[3] != (entryCode[1] & 0x0F)
            || BinaryPrimitives.ReadUInt16LittleEndian(entryCode[4..]) != 4)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires a Boolean formal branch before the TypeIdentity merge.");
        }

        var condition = new BoundRead(0, PrimitiveType.Bool, default);
        var trueQuery = ReadStandardRegisteredTypeIdentityQuery(body,
            body.GetBlockCode(1), 0, out var trueRegister, typeSpecs,
            nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, imports);
        var falseQuery = ReadStandardRegisteredTypeIdentityQuery(body,
            body.GetBlockCode(2), 0, out var falseRegister, typeSpecs,
            nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, imports);
        if (trueRegister != mergedRegister
            || falseRegister != mergedRegister)
        {
            throw Invalid(
                "Every registered-query incoming edge must define the joined TypeIdentity register.");
        }

        foreach (var branchIndex in new[] { 1, 2 })
        {
            var branchCode = body.GetBlockCode(branchIndex);
            if (branchCode.Length != 8 || branchCode[4] != 0xE3
                || branchCode[5] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    branchCode[6..]) != 3)
            {
                throw Unsupported(
                    "The bounded Standard query consumer requires both TypeIdentity branches to join at block 3.");
            }
        }

        var joinCode = body.GetBlockCode(3);
        if (joinCode.Length != 10)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires one comparison and Return after the TypeIdentity merge.");
        }

        var rightQuery = ReadStandardRegisteredTypeIdentityQuery(body,
            joinCode, 0, out var rightRegister, typeSpecs,
            nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, imports);
        var compareRegister = (int)(joinCode[6] & 0x0F);
        var comparisonInputs = joinCode[7];
        var control = joinCode[5];
        if (joinCode[4] != 0xC2 || control is not (1 or 2)
            || (joinCode[6] & 0xF0) != 0xF0
            || (comparisonInputs & 0x0F) != mergedRegister
            || (comparisonInputs >> 4) != rightRegister
            || joinCode[8] != 0x17
            || joinCode[9] != compareRegister
            || compareRegister >= 14)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires a typed TypeIdentity comparison after the merge.");
        }

        var selectedIdentity = new BoundConditional(condition, trueQuery,
            falseQuery, PrimitiveType.TypeIdentity, default);
        var comparison = new BoundBinary(
            control == 1 ? TokenKind.EqualsEquals
                : TokenKind.BangEquals,
            selectedIdentity, rightQuery, PrimitiveType.Bool, default);
        return [new BoundReturn(comparison, default)];
    }

    private static BoundTypeIdentityQuery
        ReadStandardRegisteredTypeIdentityQuery(
            TrRmRegisterBodyReader body, ReadOnlySpan<byte> code,
            int position, out int destination,
            TrRmTypeSpecTableReader typeSpecs,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef,
            TrRmImportTableReader imports)
    {
        if (position < 0 || position > code.Length - 4
            || code[position] != 0xDE
            || (code[position + 1] >> 4) != 5
            || (code[position + 1] & 0x0F) >= 14)
        {
            throw Unsupported(
                "The bounded Standard query consumer requires a TypeOf result on each incoming edge.");
        }

        destination = code[position + 1] & 0x0F;
        var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            code[(position + 2)..]);
        var type = ReadStandardRegisteredQueryType(body, typeMapIndex,
            typeSpecs, nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, imports);
        var subject = new BoundQuerySubject(type,
            BoundQuerySubjectKind.Type, null, null, false, false);
        return new BoundTypeIdentityQuery(subject, default);
    }

    private static bool TryGetStandardClosedTypeIdentity(
        BoundExpression expression, out PrimitiveType type)
    {
        if (expression is BoundTypeIdentityQuery identity
            && identity.Subject.TypeIdentityTerm is null)
        {
            type = identity.Subject.Type;
            return true;
        }

        if (expression is BoundConditional conditional
            && TryGetStandardConstantBoolean(conditional.Condition,
                out var condition))
        {
            return TryGetStandardClosedTypeIdentity(
                condition ? conditional.WhenTrue : conditional.WhenFalse,
                out type);
        }

        type = default;
        return false;
    }

    private static bool TryCreateStandardTypeIdentityRelation(
        BoundExpression left, BoundExpression right,
        out BoundExpression relation)
    {
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        return TryCreateStandardTypeIdentityRelation(left, right,
            ref remaining, 0, out relation);
    }

    private static bool TryCreateStandardTypeIdentityRelation(
        BoundExpression left, BoundExpression right, ref int remaining,
        int depth, out BoundExpression relation)
    {
        if (depth > SemanticVerifier.MaximumDepth || --remaining < 0)
        {
            relation = left;
            return false;
        }

        if (TryGetStandardClosedTypeIdentity(left, out var leftType)
            && TryGetStandardClosedTypeIdentity(right, out var rightType))
        {
            relation = new BoundConstant(leftType == rightType ? 2 : 8,
                PrimitiveType.UInt8, default);
            return true;
        }

        if (left is BoundConditional leftConditional
            && leftConditional.Type == PrimitiveType.TypeIdentity
            && TryCreateStandardTypeIdentityRelation(
                leftConditional.WhenTrue, right, ref remaining,
                depth + 1, out var leftTrue)
            && TryCreateStandardTypeIdentityRelation(
                leftConditional.WhenFalse, right, ref remaining,
                depth + 1, out var leftFalse))
        {
            relation = new BoundConditional(leftConditional.Condition,
                leftTrue, leftFalse, PrimitiveType.UInt8, default);
            return true;
        }

        if (right is BoundConditional rightConditional
            && rightConditional.Type == PrimitiveType.TypeIdentity
            && TryCreateStandardTypeIdentityRelation(left,
                rightConditional.WhenTrue, ref remaining,
                depth + 1, out var rightTrue)
            && TryCreateStandardTypeIdentityRelation(left,
                rightConditional.WhenFalse, ref remaining,
                depth + 1, out var rightFalse))
        {
            relation = new BoundConditional(rightConditional.Condition,
                rightTrue, rightFalse, PrimitiveType.UInt8, default);
            return true;
        }

        relation = left;
        return false;
    }

    private static bool TryCreateStandardPredicateCondition(
        BoundExpression relation, byte predicate,
        out BoundExpression condition)
    {
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        return TryCreateStandardPredicateCondition(relation, predicate,
            ref remaining, 0, out condition);
    }

    private static bool TryCreateStandardPredicateCondition(
        BoundExpression relation, byte predicate, ref int remaining,
        int depth, out BoundExpression condition)
    {
        if (depth > SemanticVerifier.MaximumDepth || --remaining < 0)
        {
            condition = relation;
            return false;
        }

        if (relation is BoundConstant {
                Type: PrimitiveType.UInt8,
            } constant)
        {
            condition = new BoundConstant(
                (constant.Value & predicate) != 0 ? 1 : 0,
                PrimitiveType.Bool, default);
            return true;
        }

        if (relation is BoundConditional conditional
            && conditional.Type == PrimitiveType.UInt8
            && TryCreateStandardPredicateCondition(conditional.WhenTrue,
                predicate, ref remaining, depth + 1,
                out var whenTrue)
            && TryCreateStandardPredicateCondition(conditional.WhenFalse,
                predicate, ref remaining, depth + 1,
                out var whenFalse))
        {
            condition = new BoundConditional(conditional.Condition,
                whenTrue, whenFalse, PrimitiveType.Bool, default);
            return true;
        }

        condition = relation;
        return false;
    }

    private static bool TryCreateStandardRelationCondition(
        StandardRegisteredQueryRelation relation, byte predicate,
        out BoundExpression condition)
    {
        if (predicate > 0x0F
            || relation.Left.Type != relation.Right.Type)
        {
            condition = relation.Left;
            return false;
        }

        if (relation.IsTypeIdentity
            || relation.Left.Type == PrimitiveType.TypeIdentity)
        {
            if (relation.Left.Type is not (PrimitiveType.NativeInt
                    or PrimitiveType.TypeIdentity))
            {
                condition = relation.Left;
                return false;
            }

            var equal = (predicate & 0x02) != 0;
            var distinct = (predicate & 0x08) != 0;
            if (equal == distinct)
            {
                condition = new BoundConstant(equal ? 1 : 0,
                    PrimitiveType.Bool, default);
                return true;
            }

            condition = new BoundBinary(equal
                    ? TokenKind.EqualsEquals : TokenKind.BangEquals,
                relation.Left, relation.Right, PrimitiveType.Bool, default);
            return true;
        }

        if (relation.Left.Type == PrimitiveType.NativeInt)
        {
            var less = (predicate & 0x01) != 0;
            var equal = (predicate & 0x02) != 0;
            var greater = (predicate & 0x04) != 0;
            if (less == equal && equal == greater)
            {
                condition = new BoundConstant(less ? 1 : 0,
                    PrimitiveType.Bool, default);
                return true;
            }

            var operation = (less, equal, greater) switch {
                (true, false, false) => TokenKind.Less,
                (true, true, false) => TokenKind.LessEquals,
                (false, true, false) => TokenKind.EqualsEquals,
                (false, false, true) => TokenKind.Greater,
                (false, true, true) => TokenKind.GreaterEquals,
                (true, false, true) => TokenKind.BangEquals,
                _ => (TokenKind?)null,
            };
            if (operation is TokenKind comparison)
            {
                condition = new BoundBinary(comparison, relation.Left,
                    relation.Right, PrimitiveType.Bool, default);
                return true;
            }
        }
        condition = relation.Left;
        return false;
    }

    private static bool TryGetStandardConstantBoolean(
        BoundExpression expression, out bool value)
    {
        if (expression is BoundConstant {
                Type: PrimitiveType.Bool,
            } constant)
        {
            value = constant.Value != 0;
            return true;
        }

        if (expression is BoundBinary binary
            && binary.Type == PrimitiveType.Bool
            && (binary.Operator is TokenKind.EqualsEquals
                or TokenKind.BangEquals)
            && TryGetStandardClosedTypeIdentity(binary.Left,
                out var leftType)
            && TryGetStandardClosedTypeIdentity(binary.Right,
                out var rightType))
        {
            value = binary.Operator == TokenKind.EqualsEquals
                ? leftType == rightType
                : leftType != rightType;
            return true;
        }

        value = false;
        return false;
    }

    private static PrimitiveType ReadStandardRegisteredQueryType(
        TrRmRegisterBodyReader body, int typeMapIndex,
        TrRmTypeSpecTableReader typeSpecs,
        List<NativePointerTypeDefinition> nativePointerTypes,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef,
        TrRmImportTableReader imports)
    {
        if ((uint)typeMapIndex >= (uint)body.TypeCount)
        {
            throw Invalid("A registered-query TypeMap index is outside its table.");
        }

        var token = BinaryPrimitives.ReadUInt32LittleEndian(
            body.TypeMap[(typeMapIndex * sizeof(uint))..]);
        var typeSpecRef = NormalizeStandardRegisteredQueryTypeSpecRef(token);
        return ReadForeignInvocationType(typeSpecs, typeSpecRef,
            nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, imports);
    }

    private static BoundQueryFieldPath ReadStandardRegisteredQueryFieldPath(
        uint fieldMapToken, TrRmTypeDefTableReader typeDefs,
        TrRmTypeSpecTableReader typeSpecs, TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths, TrRmImportTableReader imports,
        List<NativePointerTypeDefinition> nativePointerTypes,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef)
    {
        var tokenKind = (byte)(fieldMapToken >> 24);
        var tokenRow = (int)(fieldMapToken & 0x00FF_FFFF);
        if (tokenKind is 0x14 or 0x1C)
        {
            var fieldRow = ResolveStandardRegisteredQueryFieldRow(
                tokenKind, tokenRow, imports, fields);
            var ownerTypeDef = fields.GetOwnerTypeDef(fieldRow);
            if (typeDefs.GetGenericRange(ownerTypeDef).Count != 0)
            {
                throw Invalid(
                    "A direct FieldMap token cannot identify a generic owner.");
            }

            var ownerType = GetStandardRegisteredQueryStructureType(
                ownerTypeDef, structureTypeByTypeDef);
            var step = CreateStandardRegisteredQueryFieldStep(ownerTypeDef,
                fieldRow, ownerType, typeDefs, typeSpecs, fields,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports);
            return new BoundQueryFieldPath(ownerType, [step]);
        }

        if (tokenKind != 0x1D || tokenRow <= 0
            || tokenRow > fieldPaths.Count)
        {
            throw Invalid("A registered-query FieldMap token is invalid.");
        }

        var rootTypeSpecRef = fieldPaths.GetRootTypeSpecRef(tokenRow);
        var rootType = ReadForeignInvocationType(typeSpecs,
            NormalizeStandardRegisteredQueryTypeSpecRef(rootTypeSpecRef),
            nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, imports);
        var fieldCount = fieldPaths.GetFieldCount(tokenRow);
        if (fieldCount is <= 0
            or > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Invalid("A registered-query FieldPath has an invalid length.");
        }

        var steps = ImmutableArray.CreateBuilder<BoundQueryFieldStep>(
            fieldCount);
        var currentType = rootType;
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            var fieldReference = fieldPaths.GetFieldReference(tokenRow,
                ordinal);
            var fieldRow = ResolveStandardRegisteredQueryFieldRow(
                (byte)(fieldReference >> 24),
                (int)(fieldReference & 0x00FF_FFFF), imports, fields);
            var ownerTypeDef = fields.GetOwnerTypeDef(fieldRow);
            var ownerType = GetStandardRegisteredQueryStructureType(
                ownerTypeDef, structureTypeByTypeDef);
            if (ownerType != currentType)
            {
                throw Invalid(
                    "A registered-query FieldPath step has a discontinuous owner.");
            }

            if (typeDefs.GetGenericRange(ownerTypeDef).Count != 0)
            {
                throw Unsupported(
                    "The bounded Standard registered-query consumer does not reconstruct generic FieldPath substitutions.");
            }

            var step = CreateStandardRegisteredQueryFieldStep(ownerTypeDef,
                fieldRow, ownerType, typeDefs, typeSpecs, fields,
                nativePointerTypes, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports);
            steps.Add(step);
            currentType = step.FieldType;
        }

        var path = new BoundQueryFieldPath(rootType, steps.MoveToImmutable());
        if (!path.HasValidStructure)
        {
            throw Invalid("A registered-query FieldPath is invalid.");
        }

        return path;
    }

    private static BoundQueryFieldStep CreateStandardRegisteredQueryFieldStep(
        int ownerTypeDef, int fieldRow, PrimitiveType ownerType,
        TrRmTypeDefTableReader typeDefs,
        TrRmTypeSpecTableReader typeSpecs, TrRmFieldDefTableReader fields,
        List<NativePointerTypeDefinition> nativePointerTypes,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef,
        TrRmImportTableReader imports)
    {
        var (fieldStart, fieldEnd) = typeDefs.GetFieldRange(ownerTypeDef);
        if (fieldRow < fieldStart || fieldRow >= fieldEnd)
        {
            throw Invalid("A registered-query FieldDef is outside its owner.");
        }

        var fieldType = ReadForeignInvocationType(typeSpecs,
            NormalizeStandardRegisteredQueryTypeSpecRef(
                fields.GetTypeSpecRef(fieldRow)),
            nativePointerTypes, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, imports);
        return new BoundQueryFieldStep(ownerType,
            checked((uint)(fieldRow - fieldStart)), fieldType, false);
    }

    private static int ResolveStandardRegisteredQueryFieldRow(
        byte tokenKind, int tokenRow, TrRmImportTableReader imports,
        TrRmFieldDefTableReader fields)
    {
        if (tokenKind == 0x14)
        {
            if (tokenRow <= 0 || tokenRow > fields.Count)
            {
                throw Invalid("A registered-query FieldDef row is invalid.");
            }

            return tokenRow;
        }

        if (tokenKind != 0x1C || tokenRow <= 0
            || tokenRow > imports.Count)
        {
            throw Invalid("A registered-query FieldDef Import is invalid.");
        }

        var expectedDefinition
            = imports.GetExpectedDefinitionForImportRow(tokenRow);
        if (expectedDefinition >> 24 != 0x14)
        {
            throw Invalid(
                "A registered-query FieldDef Import does not expect a FieldDef.");
        }

        var fieldRow = (int)(expectedDefinition & 0x00FF_FFFF);
        if (fieldRow <= 0 || fieldRow > fields.Count)
        {
            throw Unsupported(
                "The bounded Standard registered-query consumer requires a resolved local FieldDef Import.");
        }

        return fieldRow;
    }

    private static PrimitiveType GetStandardRegisteredQueryStructureType(
        int ownerTypeDef, int[] structureTypeByTypeDef)
    {
        if ((uint)ownerTypeDef >= (uint)structureTypeByTypeDef.Length
            || structureTypeByTypeDef[ownerTypeDef] < 0)
        {
            throw Unsupported(
                "A registered-query FieldPath owner is not a reconstructed local structure.");
        }

        return (PrimitiveType)(100 + structureTypeByTypeDef[ownerTypeDef]);
    }

    private static uint NormalizeStandardRegisteredQueryTypeSpecRef(uint token)
    {
        var kind = (byte)(token >> 24);
        if (kind is 0xF2 or 0xF3)
        {
            return token;
        }

        if (token is > 0 and <= 0x00FF_FFFF)
        {
            return token;
        }

        if (kind != 0x11 || (token & 0x00FF_FFFF) == 0)
        {
            throw Invalid("A registered-query operand is not a TypeSpecRef.");
        }

        return token & 0x00FF_FFFF;
    }
}
