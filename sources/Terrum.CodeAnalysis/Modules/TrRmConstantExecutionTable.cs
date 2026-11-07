// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmConstantExecutionRow(
    byte SourceKind, uint SelectedSourceRef, uint ResultTypeSpecRef,
    int TargetProfileRow, int SubstitutionFirst, int SubstitutionCount,
    int ArgumentFirst, int ArgumentCount, int DependencyFirst, int DependencyCount,
    int TargetLayoutDependencyFirst, int TargetLayoutDependencyCount,
    ulong SemanticSteps, ulong ActiveFrames, ulong LiveStorageBytes,
    ulong EncodedResultBytes);

internal readonly ref struct TrRmConstantExecutionTableReader
{
    private const int RowSize = 80;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly int[] _ownerModules;
    private readonly TrRmTypeSpecTableReader _typeSpecs;

    internal int Count => _recordCount;

    internal TrRmConstantExecutionTableReader(ReadOnlySpan<byte> section,
        int recordCount, int bodyCount,
        TrRmConstantExecutionSubstitutionTableReader substitutions,
        TrRmConstantSourceTableReader constantSources,
        TrRmConstantExecutionArgumentTableReader arguments,
        TrRmLogicalInputViewTableReader views,
        TrRmConstantExecutionDependencyTableReader dependencies,
        TrRmTargetLayoutDependencyTableReader targetLayoutDependencies,
        TrRmMethodDefTableReader methods, TrRmTypeDefTableReader typeDefs,
        TrRmMethodSigTableReader signatures, TrRmGenericParamTableReader generics,
        TrRmContractTableReader contracts, TrRmTypeSpecTableReader typeSpecs,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmIntrinsicRegistrationTableReader intrinsicRegistrations,
        TrRmImportTableReader imports, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, TrRmArtifactSchema schema)
    {
        if (!TrRmConstantExecutionTableValidation.ValidCount(recordCount)
            || !TrRmConstantExecutionTableValidation.ValidCount(bodyCount)
            || (long)recordCount * RowSize != section.Length
            || schema != TrRmArtifactSchema.GeneralPredicateGraphsDraft)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        _rows = section;
        _recordCount = recordCount;
        _typeSpecs = typeSpecs;
        var ownerModules = new int[recordCount + 1];
        var substitutionsOwned = new byte[substitutions.Count + 1];
        var argumentsOwned = new byte[arguments.Count + 1];
        var dependenciesOwned = new byte[dependencies.Count + 1];
        var targetLayoutsOwned = new byte[targetLayoutDependencies.Count + 1];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var sourceKind = data[0];
            var selectedSource = TrRmConstantExecutionTableValidation.Read(data, 4);
            var resultType = TrRmConstantExecutionTableValidation.Read(data, 8);
            var targetProfile = TrRmConstantExecutionTableValidation.Read(data, 12);
            var substitutionFirst = TrRmConstantExecutionTableValidation.Read(data, 16);
            var substitutionCount = TrRmConstantExecutionTableValidation.Read(data, 20);
            var argumentFirst = TrRmConstantExecutionTableValidation.Read(data, 24);
            var argumentCount = TrRmConstantExecutionTableValidation.Read(data, 28);
            var dependencyFirst = TrRmConstantExecutionTableValidation.Read(data, 32);
            var dependencyCount = TrRmConstantExecutionTableValidation.Read(data, 36);
            var targetLayoutFirst = TrRmConstantExecutionTableValidation.Read(data, 40);
            var targetLayoutCount = TrRmConstantExecutionTableValidation.Read(data, 44);
            if (sourceKind is not (1 or 2) || data[1] != 0
                || TrRmConstantExecutionTableValidation.ReadU16(data, 2) != 0
                || resultType == 0
                || (targetProfile != 0
                    && !TrRmConstantExecutionTableValidation.IsRowReference(
                        targetProfile, targetProfiles.Count))
                || !TrRmConstantExecutionTableValidation.IsValidRange(
                    substitutionFirst, substitutionCount, substitutions.Count)
                || !TrRmConstantExecutionTableValidation.IsValidRange(
                    argumentFirst, argumentCount, arguments.Count)
                || !TrRmConstantExecutionTableValidation.IsValidRange(
                    dependencyFirst, dependencyCount, dependencies.Count)
                || !TrRmConstantExecutionTableValidation.IsValidRange(
                    targetLayoutFirst, targetLayoutCount,
                    targetLayoutDependencies.Count))
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "ConstantExecution");
            }

            _ = typeSpecs.GetTypeSpecRef(resultType);
            var ownerModule = sourceKind switch
            {
                1 => GetCheckedBodyOwner(selectedSource, bodyCount, methods,
                    signatures, contracts, typeDefs, generics, substitutions,
                    constantSources, typeSpecs, substitutionFirst,
                    substitutionCount, resultType, argumentCount),
                2 when schema.UsesGeneralPredicateGraphs()
                    && TrRmConstantExecutionTableValidation.IsRowReference(
                        selectedSource, intrinsicRegistrations.Count)
                    => GetIntrinsicOwnerModule(checked((int)selectedSource),
                        intrinsicRegistrations, imports),
                _ => throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "ConstantExecution"),
            };
            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule, TrRmFeatureTableReader.ConstantExecutionFeatureId,
                    schema))
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "ConstantExecution");
            }
            if (targetLayoutCount != 0
                && !TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule, TrRmFeatureTableReader.LayoutQueriesFeatureId,
                    schema))
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "ConstantExecution");
            }

            ownerModules[row] = ownerModule;
            MarkOwnedRange(substitutionsOwned, substitutionFirst,
                substitutionCount, "ConstantExecutionSubstitution");
            MarkOwnedRange(argumentsOwned, argumentFirst, argumentCount,
                "ConstantExecutionArgument");
            MarkOwnedRange(dependenciesOwned, dependencyFirst,
                dependencyCount, "ConstantExecutionDependency");
            MarkOwnedRange(targetLayoutsOwned, targetLayoutFirst,
                targetLayoutCount, "TargetLayoutDependency");
            var dependencyKeys = new HashSet<(byte Kind, uint Target)>();
            for (var ordinal = 0; ordinal < dependencyCount; ordinal++)
            {
                var dependency = checked((int)(dependencyFirst + (uint)ordinal));
                if (!dependencyKeys.Add((dependencies.GetKind(dependency),
                    dependencies.GetTargetRef(dependency))))
                {
                    throw TrRmConstantExecutionTableValidation.InvalidTable(
                        "ConstantExecutionDependency");
                }
            }

            for (var ordinal = 0; ordinal < targetLayoutCount; ordinal++)
            {
                var dependency = checked((int)(targetLayoutFirst + (uint)ordinal));
                if (targetProfile == 0
                    || targetLayoutDependencies.GetTargetProfileRow(dependency)
                        != targetProfile)
                {
                    throw TrRmConstantExecutionTableValidation.InvalidTable(
                        "ConstantExecution");
                }
            }
        }

        ValidateOwnedRows(substitutionsOwned, "ConstantExecutionSubstitution");
        ValidateOwnedRows(argumentsOwned, "ConstantExecutionArgument");
        ValidateOwnedRows(dependenciesOwned, "ConstantExecutionDependency");
        ValidateOwnedRows(targetLayoutsOwned, "TargetLayoutDependency");
        ValidateAcyclicArguments(arguments, views, substitutions,
            constantSources);
        _ownerModules = ownerModules;
    }

    internal byte GetSourceKind(int row) => GetRow(row)[0];
    internal uint GetSelectedSourceRef(int row)
        => TrRmConstantExecutionTableValidation.Read(GetRow(row), 4);
    internal uint GetResultTypeSpecRef(int row)
        => TrRmConstantExecutionTableValidation.Read(GetRow(row), 8);
    internal int GetTargetProfileRow(int row)
        => checked((int)TrRmConstantExecutionTableValidation.Read(GetRow(row), 12));
    internal (int First, int Count) GetSubstitutionRange(int row)
        => ReadRange(GetRow(row), 16);
    internal (int First, int Count) GetArgumentRange(int row)
        => ReadRange(GetRow(row), 24);
    internal (int First, int Count) GetDependencyRange(int row)
        => ReadRange(GetRow(row), 32);
    internal (int First, int Count) GetTargetLayoutDependencyRange(int row)
        => ReadRange(GetRow(row), 40);
    internal ulong GetSemanticSteps(int row)
        => TrRmConstantExecutionTableValidation.ReadU64(GetRow(row), 48);
    internal ulong GetActiveFrames(int row)
        => TrRmConstantExecutionTableValidation.ReadU64(GetRow(row), 56);
    internal ulong GetLiveStorageBytes(int row)
        => TrRmConstantExecutionTableValidation.ReadU64(GetRow(row), 64);
    internal ulong GetEncodedResultBytes(int row)
        => TrRmConstantExecutionTableValidation.ReadU64(GetRow(row), 72);
    internal int GetOwnerModule(int row)
    {
        if (row <= 0 || row >= _ownerModules.Length
            || _ownerModules[row] == 0)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        return _ownerModules[row];
    }
    internal bool ResultTypeMatches(int row, uint expectedTypeSpecRef)
        => _typeSpecs.AreEquivalentTypeSpecRefs(
            GetResultTypeSpecRef(row), expectedTypeSpecRef);

    private static int GetCheckedBodyOwner(uint bodyRow, int bodyCount,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmContractTableReader contracts, TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader generics,
        TrRmConstantExecutionSubstitutionTableReader substitutions,
        TrRmConstantSourceTableReader constantSources,
        TrRmTypeSpecTableReader typeSpecs, uint substitutionFirst,
        uint substitutionCount, uint resultTypeSpecRef, uint argumentCount)
    {
        if (!TrRmConstantExecutionTableValidation.IsRowReference(bodyRow, bodyCount))
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        var method = methods.GetBodyOwnerMethod(checked((int)bodyRow));
        if (method <= 0 || !HasConstantExecutionForm(contracts, method))
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        var signature = methods.GetSignature(method);
        var (_, parameterCount) = signatures.GetParamRange(signature);
        var expectedArgumentCount = parameterCount
            + (signatures.GetReceiverTypeSpec(signature) == 0 ? 0 : 1);
        if (argumentCount != (uint)expectedArgumentCount)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        var ownerType = methods.GetOwnerType(method);
        var ownerFormalRange = ownerType == 0
            ? default : typeDefs.GetGenericRange(ownerType);
        var methodFormalRange = signatures.GetGenericRange(signature);
        var expectedSubstitutionCount
            = ownerFormalRange.Count + methodFormalRange.Count;
        if (substitutionCount != (uint)expectedSubstitutionCount)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        for (var ordinal = 0; ordinal < ownerFormalRange.Count; ordinal++)
        {
            var formal = ownerFormalRange.Start + ordinal;
            ValidateFormalSubstitution(formal,
                checked((int)(substitutionFirst + (uint)ordinal)),
                generics, substitutions, constantSources, typeSpecs);
        }

        for (var ordinal = 0; ordinal < methodFormalRange.Count; ordinal++)
        {
            var formal = methodFormalRange.Start + ordinal;
            var actualOrdinal = ownerFormalRange.Count + ordinal;
            ValidateFormalSubstitution(formal,
                checked((int)(substitutionFirst + (uint)actualOrdinal)),
                generics, substitutions, constantSources, typeSpecs);
        }

        if (expectedSubstitutionCount == 0
            && !typeSpecs.AreEquivalentTypeSpecRefs(
                resultTypeSpecRef,
                signatures.GetResultTypeSpec(signature)))
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        return methods.GetOwnerModule(method);
    }

    private static void ValidateFormalSubstitution(int formal, int substitution,
        TrRmGenericParamTableReader generics,
        TrRmConstantExecutionSubstitutionTableReader substitutions,
        TrRmConstantSourceTableReader constantSources,
        TrRmTypeSpecTableReader typeSpecs)
    {
        var formalKind = generics.GetFormalKind(formal);
        if (formalKind != substitutions.GetKind(substitution))
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        var actual = substitutions.GetReference(substitution);
        _ = formalKind switch
        {
            1 => typeSpecs.GetTypeSpecRef(actual).Value,
            2 => constantSources.GetKind(checked((int)actual)),
            _ => throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution"),
        };
    }

    private static bool HasConstantExecutionForm(
        TrRmContractTableReader contracts, int method)
    {
        foreach (var form in contracts.GetEvaluationFormContracts(method))
        {
            if (form.FormTag == 2)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetIntrinsicOwnerModule(int registration,
        TrRmIntrinsicRegistrationTableReader registrations,
        TrRmImportTableReader imports)
    {
        if (registrations.GetDeclarationRole(registration) == 2
            && !imports.IsExpectedDefinition(0x2600_0000u | (uint)registration))
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        return registrations.GetOwnerModuleRow(registration);
    }

    private static void MarkOwnedRange(byte[] owners, uint first, uint count,
        string tableName)
    {
        for (var offset = 0; offset < count; offset++)
        {
            var row = checked((int)(first + offset));
            if (owners[row] != 0)
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(tableName);
            }

            owners[row] = 1;
        }
    }

    private static void ValidateOwnedRows(byte[] owners, string tableName)
    {
        for (var row = 1; row < owners.Length; row++)
        {
            if (owners[row] == 0)
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(tableName);
            }
        }
    }

    private static (int First, int Count) ReadRange(
        ReadOnlySpan<byte> data, int offset)
        => (
            checked((int)TrRmConstantExecutionTableValidation.Read(data, offset)),
            checked((int)TrRmConstantExecutionTableValidation.Read(
                data, offset + sizeof(uint))));

    private void ValidateAcyclicArguments(
        TrRmConstantExecutionArgumentTableReader arguments,
        TrRmLogicalInputViewTableReader views,
        TrRmConstantExecutionSubstitutionTableReader substitutions,
        TrRmConstantSourceTableReader constantSources)
    {
        var state = new byte[_recordCount + 1];
        var nextEdge = new int[_recordCount + 1];
        var stack = new int[_recordCount];
        for (var start = 1; start <= _recordCount; start++)
        {
            if (state[start] != 0)
            {
                continue;
            }

            var depth = 0;
            stack[0] = start;
            state[start] = 1;
            while (depth >= 0)
            {
                var request = stack[depth];
                var (argumentFirst, argumentCount) = GetArgumentRange(request);
                var (substitutionFirst, substitutionCount)
                    = GetSubstitutionRange(request);
                var edgeCount = checked(argumentCount + substitutionCount);
                var pushedChild = false;
                while (nextEdge[request] < edgeCount)
                {
                    var edge = nextEdge[request]++;
                    var childRequest = edge < argumentCount
                        ? GetArgumentChild(argumentFirst + edge, arguments, views)
                        : GetSubstitutionChild(
                            substitutionFirst + edge - argumentCount,
                            substitutions, constantSources);
                    if (childRequest == 0)
                    {
                        continue;
                    }

                    var child = childRequest;
                    if (state[child] == 1)
                    {
                        throw TrRmConstantExecutionTableValidation.InvalidTable(
                            "ConstantExecution");
                    }

                    if (state[child] == 0)
                    {
                        depth++;
                        if (depth == stack.Length)
                        {
                            throw TrRmConstantExecutionTableValidation.InvalidTable(
                                "ConstantExecution");
                        }

                        stack[depth] = child;
                        state[child] = 1;
                        pushedChild = true;
                        break;
                    }
                }

                if (!pushedChild && nextEdge[request] == edgeCount)
                {
                    state[request] = 2;
                    depth--;
                }
            }
        }
    }

    private static int GetArgumentChild(int argument,
        TrRmConstantExecutionArgumentTableReader arguments,
        TrRmLogicalInputViewTableReader views)
    {
        var source = checked((int)arguments.GetSourceRef(argument));
        return arguments.GetKind(argument) switch
        {
            4 => source,
            2 when views.GetBackingKind(source) == 3
                => checked((int)views.GetBackingRef(source)),
            _ => 0,
        };
    }

    private static int GetSubstitutionChild(int substitution,
        TrRmConstantExecutionSubstitutionTableReader substitutions,
        TrRmConstantSourceTableReader constantSources)
    {
        if (substitutions.GetKind(substitution) != 2)
        {
            return 0;
        }

        var source = checked((int)substitutions.GetReference(substitution));
        return constantSources.GetKind(source) == 2
            ? checked((int)constantSources.GetPayloadReference(source))
            : 0;
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecution");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal static class TrRmConstantExecutionTableWriter
{
    private const int RowSize = 80;

    internal static byte[] Encode(IReadOnlyList<TrRmConstantExecutionRow> rows,
        int bodyCount, TrRmConstantExecutionSubstitutionTableReader substitutions,
        TrRmConstantSourceTableReader constantSources,
        TrRmConstantExecutionArgumentTableReader arguments,
        TrRmLogicalInputViewTableReader views,
        TrRmConstantExecutionDependencyTableReader dependencies,
        TrRmTargetLayoutDependencyTableReader targetLayoutDependencies,
        TrRmMethodDefTableReader methods, TrRmTypeDefTableReader typeDefs,
        TrRmMethodSigTableReader signatures, TrRmGenericParamTableReader generics,
        TrRmContractTableReader contracts, TrRmTypeSpecTableReader typeSpecs,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmIntrinsicRegistrationTableReader intrinsicRegistrations,
        TrRmImportTableReader imports, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * RowSize;
            result[offset] = row.SourceKind;
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 4, row.SelectedSourceRef);
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 8, row.ResultTypeSpecRef);
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 12, unchecked((uint)row.TargetProfileRow));
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 16, unchecked((uint)row.SubstitutionFirst));
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 20, unchecked((uint)row.SubstitutionCount));
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 24, unchecked((uint)row.ArgumentFirst));
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 28, unchecked((uint)row.ArgumentCount));
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 32, unchecked((uint)row.DependencyFirst));
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 36, unchecked((uint)row.DependencyCount));
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 40, unchecked((uint)row.TargetLayoutDependencyFirst));
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 44, unchecked((uint)row.TargetLayoutDependencyCount));
            BinaryPrimitives.WriteUInt64LittleEndian(
                result.AsSpan(offset + 48), row.SemanticSteps);
            BinaryPrimitives.WriteUInt64LittleEndian(
                result.AsSpan(offset + 56), row.ActiveFrames);
            BinaryPrimitives.WriteUInt64LittleEndian(
                result.AsSpan(offset + 64), row.LiveStorageBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(
                result.AsSpan(offset + 72), row.EncodedResultBytes);
        }

        _ = new TrRmConstantExecutionTableReader(result, rows.Count, bodyCount,
            substitutions, constantSources, arguments, views, dependencies,
            targetLayoutDependencies, methods, typeDefs, signatures, generics,
            contracts, typeSpecs,
            targetProfiles, intrinsicRegistrations, imports, features, modules,
            schema);
        return result;
    }
}

internal readonly record struct TrRmConstantExecutionUse(
    int ExecutionRow, int UsingModuleRow, uint ExpectedResultTypeSpecRef);

internal static class TrRmConstantExecutionFeatureUseValidator
{
    internal static void Validate(
        TrRmConstantExecutionTableReader executions,
        TrRmConstantExecutionArgumentTableReader arguments,
        TrRmConstantExecutionDependencyTableReader dependencies,
        TrRmLogicalInputViewTableReader views,
        IReadOnlyList<TrRmConstantExecutionUse> uses,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema)
    {
        var pending = new Queue<(int Request, int Module, uint ExpectedType)>();
        var visited = new HashSet<(int Request, int Module, uint ExpectedType)>();
        foreach (var seed in uses)
        {
            pending.Enqueue((seed.ExecutionRow, seed.UsingModuleRow,
                seed.ExpectedResultTypeSpecRef));
        }

        while (pending.TryDequeue(out var context))
        {
            if (!visited.Add(context))
            {
                continue;
            }

            if (context.ExpectedType != 0
                && !schema.UsesGeneralPredicateGraphs())
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "ConstantExecution");
            }

            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                context.Module, TrRmFeatureTableReader.ConstantExecutionFeatureId,
                schema))
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "ConstantExecution");
            }

            if (context.ExpectedType != 0
                && !executions.ResultTypeMatches(
                    context.Request, context.ExpectedType))
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "ConstantExecution");
            }

            var (dependencyFirst, dependencyCount)
                = executions.GetDependencyRange(context.Request);
            for (var offset = 0; offset < dependencyCount; offset++)
            {
                var dependency = dependencyFirst + offset;
                if (dependencies.GetKind(dependency) == 6)
                {
                    RequireImmutableDataFeature(
                        features, modules, context.Module, schema);
                }
            }

            var (argumentFirst, argumentCount)
                = executions.GetArgumentRange(context.Request);
            for (var offset = 0; offset < argumentCount; offset++)
            {
                var argument = argumentFirst + offset;
                var kind = arguments.GetKind(argument);
                var source = arguments.GetSourceRef(argument);
                if (kind == 2)
                {
                    var view = checked((int)source);
                    var backingKind = views.GetBackingKind(view);
                    if (backingKind == 2)
                    {
                        RequireImmutableDataFeature(
                            features, modules, context.Module, schema);
                    }
                    else if (backingKind == 3)
                    {
                        pending.Enqueue((checked((int)views.GetBackingRef(view)),
                            context.Module, 0));
                    }
                }
                else if (kind == 4)
                {
                    pending.Enqueue((checked((int)source), context.Module, 0));
                }
            }
        }
    }

    private static void RequireImmutableDataFeature(
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        int module, TrRmArtifactSchema schema)
    {
        if (!TrRmFeatureManifestValidator.Requires(features, modules, module,
            TrRmFeatureTableReader.ImmutableDataIdentitiesFeatureId, schema))
        {
            throw new InvalidDataException(
                "A retained ConstantExecution identity requires Feature 19/1 in its using Module.");
        }
    }
}
