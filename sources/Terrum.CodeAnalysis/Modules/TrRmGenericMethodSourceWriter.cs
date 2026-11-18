// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private readonly record struct GenericMethodEmission(
        BoundGenericMethodDefinition Definition,
        BoundFunction ProofFunction,
        int MethodRow);

    private readonly record struct GenericMethodForwardingCall(
        BoundCall Call, int ResultVariable);

    private readonly record struct GenericMethodForwardingTarget(
        BoundCall Call, BoundFunction Function,
        BoundGenericMethodDefinition Definition, int MethodRow,
        int ResultVariable);

    private readonly record struct GenericInterfaceEmission(
        BoundInterfaceDefinition Definition, TrRmSourceTypeExport Export);

    private readonly record struct GenericImportedInterfaceEmission(
        BoundInterfaceDefinition Definition, BoundModuleImport Import);

    private readonly record struct GenericImportedInterfaceMethodKey(
        string ProviderIdentity, string TypeExportKey,
        string QualifiedName, int Arity, int MemberOrdinal,
        string MethodExportKey, uint TargetRequirement,
        uint RuntimePolicy);

    private static byte[] EmitGenericMethodCallsDraftSource(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement,
        SourceGenericMethodExports? sourceMethodExports,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports,
        TrRmArtifactSchema schema = TrRmArtifactSchema.GenericMethodCallsDraft,
        TrRmArtifactSchema baseSchema = TrRmArtifactSchema.Standard,
        bool standardInitializationReferenceBodies = false)
    {
        if (program.GenericMethodDefinitions.IsDefault)
        {
            throw new ArgumentException(
                "The checked program has no validated generic method definition table.",
                nameof(program));
        }

        if (sourceMethodExports is not null
            && !ReferenceEquals(sourceMethodExports.Program, program))
        {
            throw new ArgumentException(
                "Source generic method mappings belong to a different checked program.",
                nameof(sourceMethodExports));
        }

        if (sourceTypeExports is { Count: > 0 }
            && !schema.UsesMethodParameterLabels()
            && sourceTypeExports.Any(static export =>
                !export.InterfaceMethodExports.IsDefaultOrEmpty))
        {
            throw Unsupported(
                "G1 InterfaceMethod exports require the exact G1L1 schema for parameter labels.");
        }

        if (!program.GenericMethodDefinitions.IsEmpty
            && sourceMethodExports is null)
        {
            throw new NotSupportedException(
                "The G1 source producer requires explicit source generic-method mappings for retained open definitions.");
        }

        var emissionRows = new List<GenericMethodEmission>();
        if (sourceMethodExports is not null)
        {
            foreach (var definition in program.GenericMethodDefinitions)
            {
                var proofFunction = GetSupportedOpenGenericProofFunction(
                    definition);
                emissionRows.Add(new GenericMethodEmission(definition,
                    proofFunction, 0));
            }
        }
        var sourceProgram = program;
        var addedSeed = program.Functions.IsEmpty
            && emissionRows.Count != 0;
        if (addedSeed)
        {
            if (!program.NativeFunctionPointerSignatures.IsEmpty
                || !program.NativePointerTypes.IsEmpty
                || !program.NativeFixedArrayTypes.IsEmpty
                || !program.EnumDefinitions.IsEmpty)
            {
                throw Unsupported(
                    "The G1 source writer cannot seed a program with other retained declaration anchors.");
            }

            sourceProgram = program with {
                Functions = [CreateGenericMethodEmitterSeed()],
            };
        }

        var artifact = EmitDevelopment(sourceProgram, moduleIdentity,
            baseSchema, moduleTargetRequirement,
            standardInitializationReferenceBodies:
                standardInitializationReferenceBodies,
            allowGenericMethodNominalTypeTables: true);
        if (addedSeed)
        {
            artifact = RemoveGenericMethodEmitterSeed(artifact, baseSchema);
        }
        var hasGenericCallTargets = ArtifactHasGenericCallTargets(artifact,
            schema);
        var exportedInterfaceTypeSpecRows
            = new Dictionary<(string QualifiedName, int Arity), uint>();
        var exportedInterfaceMethodDefRows
            = new Dictionary<(
                string QualifiedName, int Arity, int MemberOrdinal,
                string DeclarationKey), uint>();
        if (sourceTypeExports is { Count: > 0 })
        {
            if (sourceMethodExports is null)
            {
                throw new ArgumentException(
                    "G1 interface exports require an explicit source mapping set.",
                    nameof(sourceTypeExports));
            }
            artifact = AddGenericMethodInterfaceMetadata(artifact, program,
                sourceTypeExports, sourceMethodExports, schema,
                out exportedInterfaceTypeSpecRows,
                out exportedInterfaceMethodDefRows);
        }
        if (emissionRows.Count != 0)
        {
            artifact = AddImportedGenericInterfaceMetadata(artifact, program,
                schema);
        }
        if (emissionRows.Count != 0
            || sourceTypeExports is { Count: > 0 })
        {
            if (sourceMethodExports is null)
            {
                throw new ArgumentException(
                    "G1 metadata emission requires an explicit source mapping set.",
                    nameof(sourceMethodExports));
            }
            var standardIndex = new TrRmArtifactIndex(artifact, schema);
            _ = standardIndex.TryGetSection(0x0016, out _,
                out var baseMethodCount);
            for (var emissionIndex = 0;
                emissionIndex < emissionRows.Count; emissionIndex++)
            {
                emissionRows[emissionIndex] = emissionRows[
                    emissionIndex] with {
                    MethodRow = checked(baseMethodCount + emissionIndex + 1),
                };
            }
            artifact = AddGenericMethodMetadata(artifact, program,
                sourceMethodExports, emissionRows,
                exportedInterfaceTypeSpecRows,
                exportedInterfaceMethodDefRows, schema);
        }
        else if (hasGenericCallTargets
            || ArtifactHasGenericWriteConstraints(artifact))
        {
            artifact = AddGenericMethodFeatures(artifact, schema,
                hasGenericCallTargets);
        }

        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(index);
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow != 0 && tables.Bodies.GetRow(bodyRow)[0] == 0x01)
            {
                tables.ValidateRegisterBodyMapsForMethod(methodRow);
            }
        }

        return artifact;
    }

    private static BoundFunction CreateGenericMethodEmitterSeed()
        => new("<trrm-generic-method-seed>", PrimitiveType.Int32, [], 0,
            new BoundBlock([
                new BoundReturn(new BoundConstant(0, PrimitiveType.Int32,
                    default), default),
            ], default), default) {
            IsInternal = true,
        };

    private static byte[] RemoveGenericMethodEmitterSeed(byte[] artifact,
        TrRmArtifactSchema schema)
    {
        var artifactIndex = new TrRmArtifactIndex(artifact, schema);
        var sections = new List<TrRmSection>(artifactIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < artifactIndex.SectionCount;
            sectionIndex++)
        {
            artifactIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            if (kind is 0x0016 or 0x0017 or 0x0018 or 0x001D or 0x0020)
            {
                var expectedCount = kind is 0x0018 or 0x001D ? 0 : 1;
                if (count != expectedCount)
                {
                    throw Unsupported(
                        "The G1 source writer seed did not emit its expected single-method tables.");
                }
                continue;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static BoundFunction GetSupportedOpenGenericProofFunction(
        BoundGenericMethodDefinition definition)
    {
        if (definition.Owner is not null || !definition.IsStatic
            || definition.ConstantFormals.IsDefault
            || !definition.ConstantFormals.IsEmpty
            || definition.FormalOrder.IsDefault
            || definition.Formals.IsDefault
            || definition.Formals.IsEmpty
            || definition.FormalOrder.Length != definition.Formals.Length
            || definition.FunctionIndex < 0
            || definition.FunctionIndex
                >= definition.ProofProgram.Functions.Length)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a G1 owner or formal shape outside the current source writer.");
        }

        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            var formal = definition.Formals[ordinal];
            var slot = definition.FormalOrder[ordinal];
            if (formal.Ordinal != ordinal
                || slot.Ordinal != ordinal
                || slot.Kind != BoundGenericMethodFormalKind.Type
                || slot.TypeFormalOrdinal != ordinal
                || !formal.Requirements.IsEmpty
                || formal.InterfaceConstraints.IsDefault
                || formal.OwnershipDomain is < 1 or > 3
                || formal.NullabilityDomain is < 1 or > 2)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an unsupported formal constraint or default.");
            }
        }

        var function = definition.ProofProgram.Functions[
            definition.FunctionIndex];
        if (function.IsExternal
            || function.GenericMethodProofDefinitionOrdinal
                != definition.DefinitionOrdinal
            || function.RequiresUnsafeCaller
                != definition.RequiresUnsafeCaller
            || function.ParameterCount != definition.RuntimeParameterCount
            || function.GenericMethodProofTypeParameters.Length
                != definition.Formals.Length
            || function.Variables.Take(function.ParameterCount).Any(
                static parameter => parameter.IsMutable || parameter.IsOut
                    || parameter.IsInit || parameter.IsImplicitRef)
            || function.NoFailureProof
            || function.NativeOutputContracts.Length != 0
            || function.DirectNativeImport is not null
            || function.NativeVTableMethod is not null
            || function.DeclarationContractClauses is { IsDefault: false,
                IsEmpty: false }
            || !function.CheckedOutputStatePredicates.IsDefaultOrEmpty)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a body or contract outside the current G1 source writer.");
        }

        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            if (function.GenericMethodProofTypeParameters[ordinal]
                != definition.Formals[ordinal].PlaceholderType)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has inconsistent checked formal identities.");
            }
        }

        return function;
    }

    private static bool ContainsGenericMethodCall(BoundFunction function)
    {
        var found = false;
        if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression => found |= expression is BoundCall {
                    GenericMethodCall: not null,
                }))
        {
            throw Unsupported(
                "The G1 source writer could not traverse a checked proof Body.");
        }

        return found;
    }

    private static bool ArtifactHasGenericCallTargets(byte[] artifact,
        TrRmArtifactSchema schema)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x0020, out var bodyBytes,
                out var bodyCount)
            || bodyCount == 0)
        {
            return false;
        }

        var contractCount = index.TryGetSection(0x001B, out _, out var count)
            ? count : 0;
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount, schema);
        for (var bodyRow = 1; bodyRow <= bodies.Count; bodyRow++)
        {
            var body = bodies.GetRow(bodyRow);
            if (!body.IsEmpty && body[0] == 0x01
                && bodies.GetRegisterBody(bodyRow, contractCount)
                    .HasGenericCallTargets)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGenericCallTargets(
        ReadOnlyMemory<byte> body, int contractCount,
        TrRmArtifactSchema schema)
    {
        var bytes = body.Span;
        return !bytes.IsEmpty && bytes[0] == 0x01
            && new TrRmRegisterBodyReader(bytes, contractCount, schema)
                .HasGenericCallTargets;
    }

    private static bool ArtifactHasGenericWriteConstraints(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x001A, out var constraints,
                out var count))
        {
            return false;
        }
        if (constraints.Length != (long)count * 12)
        {
            throw new InvalidDataException(
                "The Constraint table has an invalid fixed-width extent.");
        }

        for (var row = 0; row < count; row++)
        {
            var data = constraints.Slice(row * 12, 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) == 2
                && BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) == 2)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] AddGenericMethodFeatures(byte[] artifact,
        TrRmArtifactSchema schema, bool includeGenericMethodCalls)
    {
        var index = new TrRmArtifactIndex(artifact);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var featureRows = ReadFeatureRows(sections);
        var hasWriteConstraint = ReadConstraintRows(sections).Any(
            static constraint => constraint.Kind == 2
                && constraint.Payload == 2);
        if (hasWriteConstraint && !schema.UsesGenericWriteRequirements())
        {
            throw Unsupported(
                "The selected TRRM schema does not admit generic Write requirements.");
        }
        if (includeGenericMethodCalls
            && !featureRows.Any(static feature => feature.FeatureId
                == TrRmFeatureTableReader.GenericMethodCallsFeatureId))
        {
            featureRows.Add(TrRmFeatureRow.GenericMethodCallsCandidate);
        }
        if (hasWriteConstraint
            && !featureRows.Any(static feature => feature.FeatureId
                == TrRmFeatureTableReader.GenericWriteRequirementsFeatureId))
        {
            featureRows.Add(
                TrRmFeatureRow.GenericWriteRequirementsCandidate);
        }
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var featureCount = featureBytes.Length / 8;

        var moduleSection = GetSection(sections, 0x0001);
        var moduleBytes = moduleSection.Bytes.ToArray();
        if (moduleSection.RecordCount != 1)
        {
            throw Unsupported(
                "G1 source feature emission requires one output Module.");
        }
        var oldFeatureCount = ReadInt(moduleBytes, 28);
        if (oldFeatureCount == 0)
        {
            WriteUInt32(moduleBytes, 24, 1);
        }
        WriteUInt32(moduleBytes, 28, checked((uint)featureCount));

        SetSection(sections, 0x0001, 1, moduleBytes);
        SetSection(sections, 0x0004, checked((uint)featureCount),
            featureBytes);
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static bool ContainsInterfaceCall(BoundFunction function)
    {
        var found = false;
        if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression => found |= expression is BoundCall {
                    InterfaceCallIdentity: not null,
                }))
        {
            throw Unsupported(
                "The G1 source writer could not traverse a checked proof Body.");
        }

        return found;
    }

    private static bool IsDirectGenericMethodMoveReturn(
        BoundFunction function)
        => TryGetDirectGenericMethodMoveReturn(function, out _);

    private static bool TryGetDirectGenericMethodMoveReturn(
        BoundFunction function, out BoundMove? move)
    {
        move = null;
        if (!function.Body.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        var body = function.Body;
        var cleanupVariables = new List<int>();
        while (body.Statements.Length == 1
            && body.Statements[0] is BoundTryFinally tryFinally)
        {
            if (!tryFinally.Finally.RetiredManagedLocals.IsEmpty
                || tryFinally.Finally.Statements.Length != 1
                || tryFinally.Finally.Statements[0]
                    is not BoundDrop drop || drop.IsOutput)
            {
                return false;
            }

            cleanupVariables.Add(drop.Variable);
            body = tryFinally.Body;
        }

        if (body.RetiredManagedLocals.IsEmpty is false
            || body.Statements.Length != 1
            || body.Statements[0] is not BoundReturn {
                Value: BoundMove returnedMove,
            }
            || (cleanupVariables.Count != 0
                && (cleanupVariables.Count != 1
                    || cleanupVariables[0] != returnedMove.Variable)))
        {
            return false;
        }

        move = returnedMove;
        return true;
    }

    private static bool TryGetDirectGenericCallReturn(
        BoundFunction function, out BoundCall? call,
        out int resultVariable)
    {
        call = null;
        resultVariable = -1;
        if (!function.Body.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        var body = function.Body;
        var cleanupVariables = new List<int>();
        while (body.Statements.Length == 1
            && body.Statements[0] is BoundTryFinally tryFinally)
        {
            if (!tryFinally.Finally.RetiredManagedLocals.IsEmpty
                || tryFinally.Finally.Statements.Length != 1
                || tryFinally.Finally.Statements[0]
                    is not BoundDrop drop || drop.IsOutput)
            {
                return false;
            }

            cleanupVariables.Add(drop.Variable);
            body = tryFinally.Body;
        }

        if (body.RetiredManagedLocals.IsEmpty is false)
        {
            return false;
        }

        BoundCall? returnedCall = null;
        if (body.Statements.Length == 1
            && body.Statements[0] is BoundReturn {
                Value: BoundCall directCall,
            })
        {
            returnedCall = directCall;
        }
        else if (body.Statements.Length >= 2
            && body.Statements[^1] is BoundReturn {
                Value: BoundRead returnedRead,
            })
        {
            // Same-typed Copy aliases require no distinct serialized Home.
            var localCount = body.Statements.Length - 1;
            if (localCount > SemanticVerifier.MaximumVariables
                || function.Variables.Length
                    != function.ParameterCount + localCount)
            {
                return false;
            }

            var previousAlias = -1;
            for (var ordinal = 0; ordinal < localCount; ordinal++)
            {
                if (body.Statements[ordinal] is not BoundLocal local
                    || local.Variable
                        != function.ParameterCount + ordinal
                    || (uint)local.Variable
                        >= (uint)function.Variables.Length)
                {
                    return false;
                }

                var variable = function.Variables[local.Variable];
                if (variable.Type != local.Initializer.Type
                    || variable.IsMutable || variable.IsOut
                    || variable.IsInit || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary
                    || variable.IsInitializationLocal)
                {
                    return false;
                }

                if (ordinal == 0)
                {
                    if (local.Initializer is not BoundCall localCall
                        || localCall.Type == PrimitiveType.Void
                        || variable.Type != function.ReturnType)
                    {
                        return false;
                    }

                    returnedCall = localCall;
                }
                else if (local.Initializer is BoundRead read
                    && read.Variable == previousAlias
                    && read.Type == variable.Type)
                {
                    if (returnedCall is null
                        || variable.Type != returnedCall.Type)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                previousAlias = local.Variable;
            }

            if (returnedCall is null
                || returnedRead.Variable != previousAlias
                || returnedRead.Type != function.ReturnType)
            {
                return false;
            }

            resultVariable = previousAlias;
        }

        if (returnedCall is null)
        {
            return false;
        }

        var movedVariables = returnedCall.Arguments
            .OfType<BoundMove>()
            .Select(static move => move.Variable)
            .ToArray();
        if (cleanupVariables.Count != movedVariables.Length
            || movedVariables.Distinct().Count() != movedVariables.Length
            || cleanupVariables.Except(movedVariables).Any()
            || movedVariables.Except(cleanupVariables).Any())
        {
            return false;
        }

        call = returnedCall;
        return true;
    }

    private static bool TryGetDirectGenericUnitCall(
        BoundFunction function, out BoundCall? call)
    {
        call = null;
        var body = function.Body;
        if (function.ReturnType != PrimitiveType.Void
            || function.ParameterCount > 8
            || function.Variables.Length != function.ParameterCount
            || !body.RetiredManagedLocals.IsEmpty
            || body.Statements.Length is < 1 or > 2
            || body.Statements[0] is not BoundExpressionStatement {
                Expression: BoundCall directCall,
            }
            || directCall.Type != PrimitiveType.Void
            || (body.Statements.Length == 2
                && body.Statements[1] is not BoundReturn {
                    Value: null,
                }))
        {
            return false;
        }

        call = directCall;
        return true;
    }

    private static bool TryGetGenericMethodForwardingChain(
        BoundFunction function,
        out ImmutableArray<GenericMethodForwardingCall> calls)
    {
        calls = [];
        var body = function.Body;
        if (!body.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        var hasUnitReturn = body.Statements.Length != 0
            && body.Statements[^1] is BoundReturn { Value: null };
        var unitCallIndex = body.Statements.Length
            - (hasUnitReturn ? 2 : 1);
        if (function.ReturnType == PrimitiveType.Void
            && unitCallIndex >= 1
            && unitCallIndex <= SemanticVerifier.MaximumVariables
            && function.ParameterCount + unitCallIndex
                <= SemanticVerifier.MaximumVariables
            && function.Variables.Length
                == function.ParameterCount + unitCallIndex
            && body.Statements[unitCallIndex]
                is BoundExpressionStatement {
                    Expression: BoundCall unitCall,
                }
            && unitCall.Type == PrimitiveType.Void)
        {
            var unitChain
                = ImmutableArray.CreateBuilder<GenericMethodForwardingCall>(
                    unitCallIndex + 1);
            for (var ordinal = 0; ordinal < unitCallIndex; ordinal++)
            {
                var resultVariable = function.ParameterCount + ordinal;
                if (body.Statements[ordinal] is not BoundLocal local
                    || local.Variable != resultVariable
                    || local.Initializer is not BoundCall call
                    || call.Type == PrimitiveType.Void
                    || function.Variables[resultVariable].Type != call.Type)
                {
                    return false;
                }

                var variable = function.Variables[resultVariable];
                if (variable.IsMutable || variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary
                    || variable.IsInitializationLocal)
                {
                    return false;
                }

                if (ordinal != 0
                    && !call.Arguments.Any(argument =>
                        argument is BoundRead read
                        && read.Variable >= function.ParameterCount
                        && read.Variable < resultVariable))
                {
                    return false;
                }

                unitChain.Add(new GenericMethodForwardingCall(call,
                    resultVariable));
            }

            if (!unitCall.Arguments.Any(argument =>
                    argument is BoundRead read
                    && read.Variable >= function.ParameterCount
                    && read.Variable < function.Variables.Length))
            {
                return false;
            }

            unitChain.Add(new GenericMethodForwardingCall(unitCall, -1));
            calls = unitChain.MoveToImmutable();
            return true;
        }

        var localCount = body.Statements.Length - 1;
        if (localCount < 2
            || localCount > SemanticVerifier.MaximumVariables
            || function.Variables.Length
                != function.ParameterCount + localCount
            || body.Statements[^1] is not BoundReturn {
                Value: BoundRead returnedRead,
            }
            || returnedRead.Variable
                != function.ParameterCount + localCount - 1
            || returnedRead.Type != function.ReturnType
            || function.Variables[returnedRead.Variable].Type
                != function.ReturnType)
        {
            return false;
        }

        var builder
            = ImmutableArray.CreateBuilder<GenericMethodForwardingCall>(
                localCount);
        for (var ordinal = 0; ordinal < localCount; ordinal++)
        {
            var resultVariable = function.ParameterCount + ordinal;
            if (body.Statements[ordinal] is not BoundLocal local
                || local.Variable != resultVariable
                || local.Initializer is not BoundCall call
                || call.Type == PrimitiveType.Void
                || function.Variables[resultVariable].Type != call.Type)
            {
                return false;
            }

            var variable = function.Variables[resultVariable];
            if (variable.IsMutable || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary
                || variable.IsInitializationLocal)
            {
                return false;
            }

            if (ordinal != 0)
            {
                var consumesEarlierCallResult = false;
                foreach (var argument in call.Arguments)
                {
                    if (argument is BoundRead read
                        && read.Variable >= function.ParameterCount
                        && read.Variable < resultVariable)
                    {
                        consumesEarlierCallResult = true;
                        break;
                    }
                }

                if (!consumesEarlierCallResult)
                {
                    return false;
                }
            }

            builder.Add(new GenericMethodForwardingCall(call,
                resultVariable));
        }

        calls = builder.MoveToImmutable();
        return true;
    }

    private static byte[] EncodeGenericMethodMoveReturnBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        if (!TryGetDirectGenericMethodMoveReturn(function,
                out var move)
            || move is null
            || (uint)move.Variable >= (uint)function.ParameterCount
            || function.Variables.Length != function.ParameterCount
            || function.ParameterCount > 8
            || move.Type != function.ReturnType
            || function.Variables[move.Variable].Type != move.Type
            || function.Variables.Take(function.ParameterCount).Any(
                static parameter => parameter.IsMutable || parameter.IsOut
                    || parameter.IsInit || parameter.IsImplicitRef))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a move Body outside the direct-parameter return subset.");
        }

        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        uint BodyTypeToken(PrimitiveType type)
        {
            return CreateStandardBodyTypeMapToken(
                GenericMethodTypeSpecRef(type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows, spanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes));
        }

        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                typeMap.GetIndex(BodyTypeToken(
                    function.Variables[parameter].Type)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4), checked((ushort)parameter));
        }

        var entryState = new byte[8];
        entryState[0] = 1;
        var code = new byte[] {
            0xB8,
            checked((byte)move.Variable),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, typeMap.Encode(),
            default, default, default,
            [new TrRmRegisterBodyBlock(0, checked((uint)code.Length), 0)],
            [entryState], code, schema: schema);
    }

    private static byte[] EncodeGenericMethodForwardingBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        IReadOnlyList<GenericMethodEmission> emissions,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        if (TryGetGenericMethodForwardingChain(function, out var callChain))
        {
            return EncodeGenericMethodForwardingChainBody(function,
                definition, emissions, callChain,
                formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                spanTypes, schema, nominalTypes);
        }

        var isUnitCall = TryGetDirectGenericUnitCall(function,
            out var genericCall);
        var resultVariable = -1;
        if (!isUnitCall && !TryGetDirectGenericCallReturn(function,
                out genericCall, out resultVariable))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a GenericCall Body outside the direct-return or unit-forwarding subset.");
        }

        if (genericCall is null
            || genericCall.GenericMethodCall
                is not BoundGenericMethodInstantiation call
            || (isUnitCall
                ? genericCall.Type != PrimitiveType.Void
                    || function.ReturnType != PrimitiveType.Void
                : genericCall.Type == PrimitiveType.Void
                    || genericCall.Type != function.ReturnType)
            || (resultVariable < 0
                ? function.Variables.Length != function.ParameterCount
                : resultVariable < function.ParameterCount
                    || resultVariable
                        != function.Variables.Length - 1)
            || function.ParameterCount > 8)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a GenericCall Body outside the direct-return or unit-forwarding subset.");
        }

        var proofProgram = definition.ProofProgram;
        if ((uint)genericCall.Function >= (uint)proofProgram.Functions.Length)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' calls an unknown proof target.");
        }

        var target = proofProgram.Functions[genericCall.Function];
        var targetCall = target.GenericMethodProofCall;
        var targetEmissions = emissions.Where(candidate =>
            candidate.Definition.DefinitionOrdinal == call.DefinitionOrdinal)
            .ToArray();
        if (targetCall is null
            || targetEmissions.Length != 1
            || targetCall.DefinitionOrdinal != call.DefinitionOrdinal
            || !targetCall.TypeArguments.SequenceEqual(call.TypeArguments)
            || !targetCall.ConstantArguments.IsEmpty
            || !call.ConstantArguments.IsEmpty
            || target.ParameterCount != genericCall.Arguments.Length
            || target.ParameterCount != target.Variables.Length
            || target.ParameterCount > 8
            || (target.ReturnType == PrimitiveType.Void) != isUnitCall
            || call.TypeArguments.Length
                != targetEmissions[0].Definition.Formals.Length
            || (!genericCall.ArgumentParameterIndices.IsDefault
                && genericCall.ArgumentParameterIndices.Length
                    != genericCall.Arguments.Length))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a GenericCall target outside the local type-only forwarding subset.");
        }

        var targetDefinition = targetEmissions[0].Definition;
        var targetDoesNotReturn = target.DoesNotReturn;
        for (var ordinal = 0; ordinal < targetDefinition.Formals.Length;
            ordinal++)
        {
            var formal = targetDefinition.Formals[ordinal];
            if (!formal.Requirements.IsEmpty
                || formal.InterfaceConstraints.IsDefault
                || formal.OwnershipDomain is < 1 or > 3
                || formal.NullabilityDomain is < 1 or > 2)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' forwards to a target with unsupported generic formal obligations.");
            }
        }

        var callerFormalOrdinalsByTargetFormal = new Dictionary<int, int>();
        for (var targetOrdinal = 0;
            targetOrdinal < targetDefinition.Formals.Length;
            targetOrdinal++)
        {
            var actualType = call.TypeArguments[targetOrdinal];
            for (var callerOrdinal = 0;
                callerOrdinal < definition.Formals.Length;
                callerOrdinal++)
            {
                if (definition.Formals[callerOrdinal].PlaceholderType
                    != actualType)
                {
                    continue;
                }

                callerFormalOrdinalsByTargetFormal.Add(
                    targetDefinition.Formals[targetOrdinal].Ordinal,
                    definition.Formals[callerOrdinal].Ordinal);
                break;
            }
        }

        bool SatisfiesInterfaceConstraints(int targetFormalOrdinal,
            BoundGenericMethodFormal callerFormal)
        {
            var targetFormal = targetDefinition.Formals[
                targetFormalOrdinal];
            foreach (var targetConstraint in targetFormal.InterfaceConstraints)
            {
                if (!TrySubstituteGenericMethodInterfaceConstraint(
                        targetConstraint,
                        callerFormalOrdinalsByTargetFormal,
                        out var substitutedConstraint)
                    || !callerFormal.InterfaceConstraints.Any(
                        callerConstraint =>
                            BoundInterfaceContract.IsInInheritanceClosure(
                                callerConstraint, substitutedConstraint,
                                definition.ProofProgram.Interfaces)))
                {
                    return false;
                }
            }

            return true;
        }

        var formalParameters = new bool[target.ParameterCount];
        var inputs = new TrRmRegisterBodyCallInput[target.ParameterCount];
        for (var argumentIndex = 0;
            argumentIndex < genericCall.Arguments.Length; argumentIndex++)
        {
            var argument = genericCall.Arguments[argumentIndex];
            var formalIndex = genericCall.ArgumentParameterIndices.IsDefault
                ? argumentIndex
                : genericCall.ArgumentParameterIndices[argumentIndex];
            var (argumentVariable, sourceUse) = argument switch
            {
                BoundRead read => (read.Variable, (byte)1),
                BoundMove move => (move.Variable, (byte)2),
                BoundReferenceReborrow { Descriptor: BoundRead read }
                    => (read.Variable, (byte)5),
                _ => (-1, (byte)0),
            };
            if ((uint)formalIndex >= (uint)inputs.Length
                || formalParameters[formalIndex]
                || sourceUse == 0
                || (uint)argumentVariable >= (uint)function.ParameterCount
                || function.Variables[argumentVariable].Type != argument.Type
                || !GenericMethodTypeMatchesAfterSubstitution(
                    target.Variables[formalIndex].Type, argument.Type,
                    targetDefinition, definition, call.TypeArguments, 0)
                || target.Variables[formalIndex].IsMutable
                || target.Variables[formalIndex].IsOut
                || target.Variables[formalIndex].IsInit
                || target.Variables[formalIndex].IsImplicitRef
                || function.Variables[argumentVariable].IsMutable
                || function.Variables[argumentVariable].IsOut
                || function.Variables[argumentVariable].IsInit
                || function.Variables[argumentVariable].IsImplicitRef)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an argument outside the parameter-home forwarding subset.");
            }

            // Transfers require a target with no failing operation before it returns the moved formal.
            if (sourceUse == 2
                && (!TryGetDirectGenericMethodMoveReturn(
                        targetEmissions[0].ProofFunction,
                        out var targetMove)
                    || targetMove is null
                    || targetMove.Variable != formalIndex))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' transfers a value to a target without a matching direct move-return Body.");
            }

            var argumentFormalOrdinal = -1;
            for (var candidate = 0;
                candidate < definition.Formals.Length; candidate++)
            {
                if (definition.Formals[candidate].PlaceholderType
                    == argument.Type)
                {
                    argumentFormalOrdinal = candidate;
                    break;
                }
            }
            if (sourceUse == 5
                ? !IsGenericMethodInitializationReferenceType(
                    target.Variables[formalIndex].Type,
                    targetDefinition.ProofProgram.CheckedReferenceTypes)
                : argumentFormalOrdinal < 0
                    ? sourceUse != 1
                    : definition.Formals[argumentFormalOrdinal]
                            .OwnershipDomain switch {
                        1 => sourceUse != 1,
                        2 => sourceUse != 2,
                        _ => true,
                    })
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' uses a parameter Home without the required Copy or unique transfer mode.");
            }

            formalParameters[formalIndex] = true;
            inputs[formalIndex] = new TrRmRegisterBodyCallInput(
                2, sourceUse, checked((ushort)argumentVariable));
        }

        if (formalParameters.Any(static assigned => !assigned)
            || !GenericMethodTypeMatchesAfterSubstitution(
                target.ReturnType, function.ReturnType, targetDefinition,
                definition, call.TypeArguments, 0))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a GenericCall signature outside the checked forwarding subset.");
        }

        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        uint GetBodyTypeToken(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                throw Unsupported(
                    "G1 GenericCall forwarding requires non-unit values.");
            }

            return CreateStandardBodyTypeMapToken(
                GenericMethodTypeSpecRef(type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows, spanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes));
        }

        var homeCount = function.ParameterCount
            + (targetDoesNotReturn || isUnitCall ? 0 : 1);
        var homeMap = new byte[checked(homeCount * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                typeMap.GetIndex(GetBodyTypeToken(
                    function.Variables[parameter].Type)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4), checked((ushort)parameter));
        }

        ushort resultHome = 0;
        if (!targetDoesNotReturn && !isUnitCall)
        {
            resultHome = checked((ushort)function.ParameterCount);
            var resultOffset = resultHome * 8;
            homeMap[resultOffset] = 3;
            homeMap[resultOffset + 1] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(resultOffset + 2),
                typeMap.GetIndex(GetBodyTypeToken(function.ReturnType)));
        }

        var targetMethodMap = new TrRmRegisterBodyTokenMapBuilder();
        var targetMethodToken = 0x1600_0000u
            | checked((uint)targetEmissions[0].MethodRow);
        var targetMethodMapIndex = targetMethodMap.GetIndex(
            targetMethodToken);
        var targetArguments = new TrRmRegisterBodyGenericCallArgument[
            call.TypeArguments.Length];
        for (var ordinal = 0; ordinal < targetArguments.Length; ordinal++)
        {
            var actual = call.TypeArguments[ordinal];
            var targetFormal = targetDefinition.Formals[ordinal];
            var callerFormalOrdinal = -1;
            for (var candidate = 0;
                candidate < definition.Formals.Length; candidate++)
            {
                if (definition.Formals[candidate].PlaceholderType == actual)
                {
                    callerFormalOrdinal = candidate;
                    break;
                }
            }

            if (callerFormalOrdinal >= 0)
            {
                var callerFormal = definition.Formals[callerFormalOrdinal];
                if (!SemanticVerifier
                        .IsGenericMethodFormalContractSatisfied(
                            targetFormal.OwnershipDomain,
                            targetFormal.NullabilityDomain,
                            targetFormal.RequiresWritable,
                            targetFormal.RequiresBlittable,
                            callerFormal.OwnershipDomain,
                            callerFormal.NullabilityDomain,
                            callerFormal.RequiresWritable,
                            callerFormal.RequiresBlittable)
                    || !SatisfiesInterfaceConstraints(
                        ordinal, callerFormal))
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' forwards a caller formal without the target domain or property obligations.");
                }
            }
            else if (actual == PrimitiveType.Void
                || targetFormal.OwnershipDomain == 2
                || targetFormal.RequiresWritable
                || !targetFormal.InterfaceConstraints.IsEmpty)
            {
                throw Unsupported(
                    "G1 GenericCall forwarding has a type actual that does not satisfy the target formal.");
            }

            targetArguments[ordinal]
                = new TrRmRegisterBodyGenericCallArgument(1,
                    GenericMethodTypeSpecRef(actual, formalTypeSpecRows,
                        builtInTypeSpecRows, typeSpecRows, spanTypes,
                        definition.ProofProgram.CheckedReferenceTypes,
                        definition.DefinitionOrdinal, definition,
                        allowSpanType: true, nominalTypes: nominalTypes));
        }

        var records = new List<ReadOnlyMemory<byte>>(3);
        var entryState = new byte[8];
        entryState[0] = 1;
        records.Add(entryState);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeGenericCallTarget(
            1, 0, targetMethodMapIndex, 0, 0, targetArguments,
            schema));
        var callSiteIndex = checked((ushort)records.Count);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
            4, targetDoesNotReturn || isUnitCall ? (byte)0 : (byte)2, 1,
            resultHome, inputs, [], schema: schema));

        var code = targetDoesNotReturn || isUnitCall
            ? new byte[] { 0x90, checked((byte)callSiteIndex) }
            : [
                0x90,
                checked((byte)callSiteIndex),
                0xB8,
                checked((byte)resultHome),
            ];
        return TrRmRegisterBodyWriter.Encode(homeMap, typeMap.Encode(),
            targetMethodMap.Encode(), default, default,
            [new TrRmRegisterBodyBlock(0, checked((uint)code.Length), 0)],
            records, code, schema: schema);
    }

    private static byte[] EncodeGenericMethodForwardingChainBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        IReadOnlyList<GenericMethodEmission> emissions,
        ImmutableArray<GenericMethodForwardingCall> callChain,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        var hasUnitResult = callChain.Length != 0
            && callChain[^1].ResultVariable < 0;
        var resultCallCount = callChain.Length
            - (hasUnitResult ? 1 : 0);
        if (function.ParameterCount > 8
            || ((function.ReturnType == PrimitiveType.Void)
                != hasUnitResult)
            || callChain.Length < 2
            || callChain.Length > SemanticVerifier.MaximumVariables
            || resultCallCount < 1
            || function.ParameterCount + resultCallCount
                > ushort.MaxValue
            || (!hasUnitResult
                && function.ParameterCount + resultCallCount - 1
                    > byte.MaxValue)
            || callChain.Length > ushort.MaxValue / 2)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a call chain outside the bounded forwarding subset.");
        }

        var proofProgram = definition.ProofProgram;
        GenericMethodForwardingTarget PrepareTarget(
            GenericMethodForwardingCall forwardingCall)
        {
            var genericCall = forwardingCall.Call;
            if (genericCall.GenericMethodCall
                    is not BoundGenericMethodInstantiation call
                || genericCall.ImportedGenericMethodCall is not null
                || (uint)genericCall.Function
                    >= (uint)proofProgram.Functions.Length)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a call-chain target outside the local type-only subset.");
            }

            var target = proofProgram.Functions[genericCall.Function];
            var targetCall = target.GenericMethodProofCall;
            var targetEmissions = emissions.Where(candidate =>
                candidate.Definition.DefinitionOrdinal
                    == call.DefinitionOrdinal).ToArray();
            if (targetCall is null || targetEmissions.Length != 1
                || targetCall.DefinitionOrdinal != call.DefinitionOrdinal
                || !targetCall.TypeArguments.SequenceEqual(
                    call.TypeArguments)
                || !targetCall.ConstantArguments.IsEmpty
                || target.DeclaringStruct is not null || !target.IsStatic
                || targetEmissions[0].Definition.Owner is not null
                || !targetEmissions[0].Definition.IsStatic
                || target.DoesNotReturn
                || target.ParameterCount != genericCall.Arguments.Length
                || target.ParameterCount != target.Variables.Length
                || target.ParameterCount > 8
                || (target.ReturnType == PrimitiveType.Void)
                    != (forwardingCall.ResultVariable < 0)
                || genericCall.Type != target.ReturnType
                || HasConstantFormals(targetEmissions[0].Definition)
                || call.TypeArguments.Length
                    != targetEmissions[0].Definition.Formals.Length
                || (!genericCall.ArgumentParameterIndices.IsDefault
                    && genericCall.ArgumentParameterIndices.Length
                        != genericCall.Arguments.Length))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' targets an unsupported local GenericMethod.");
            }

            var targetDefinition = targetEmissions[0].Definition;
            for (var ordinal = 0;
                ordinal < targetDefinition.Formals.Length; ordinal++)
            {
                var formal = targetDefinition.Formals[ordinal];
                if (!formal.Requirements.IsEmpty
                    || formal.InterfaceConstraints.IsDefault
                    || !formal.InterfaceConstraints.IsEmpty
                    || formal.OwnershipDomain is < 1 or > 3
                    || formal.NullabilityDomain is < 1 or > 2)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' targets a method with unsupported formal obligations.");
                }
            }

            return new GenericMethodForwardingTarget(genericCall, target,
                targetDefinition, targetEmissions[0].MethodRow,
                forwardingCall.ResultVariable);
        }

        static bool HasConstantFormals(
            BoundGenericMethodDefinition target)
            => !target.ConstantFormals.IsDefaultOrEmpty;

        var targets = new GenericMethodForwardingTarget[callChain.Length];
        for (var index = 0; index < targets.Length; index++)
        {
            targets[index] = PrepareTarget(callChain[index]);
        }
        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        uint GetBodyTypeToken(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                throw Unsupported(
                    "G1 GenericCall forwarding requires non-unit values.");
            }

            return CreateStandardBodyTypeMapToken(
                GenericMethodTypeSpecRef(type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows, spanTypes,
                    proofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes));
        }

        var homeRows = new List<byte[]>(
            function.ParameterCount + resultCallCount);
        var homeIndicesByVariable = new int[function.Variables.Length];
        Array.Fill(homeIndicesByVariable, -1);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var home = new byte[8];
            home[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                home.AsSpan(2), typeMap.GetIndex(GetBodyTypeToken(
                    function.Variables[parameter].Type)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                home.AsSpan(4), checked((ushort)parameter));
            homeIndicesByVariable[parameter] = homeRows.Count;
            homeRows.Add(home);
        }

        foreach (var target in targets)
        {
            var resultVariable = target.ResultVariable;
            if (resultVariable < 0)
            {
                continue;
            }

            if ((uint)resultVariable >= (uint)function.Variables.Length
                || homeIndicesByVariable[resultVariable] != -1
                || homeRows.Count >= ushort.MaxValue)
            {
                throw Unsupported(
                    "A G1 GenericCall result exceeds its bounded Home map.");
            }

            var home = new byte[8];
            home[0] = 3;
            home[1] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                home.AsSpan(2), typeMap.GetIndex(GetBodyTypeToken(
                    function.Variables[resultVariable].Type)));
            homeIndicesByVariable[resultVariable] = homeRows.Count;
            homeRows.Add(home);
        }

        var homeMap = new byte[checked(homeRows.Count * 8)];
        for (var homeIndex = 0; homeIndex < homeRows.Count; homeIndex++)
        {
            homeRows[homeIndex].CopyTo(homeMap, homeIndex * 8);
        }

        var methodMap = new TrRmRegisterBodyTokenMapBuilder();
        var records = new List<ReadOnlyMemory<byte>>(
            checked(1 + (callChain.Length * 2)));
        var entryState = new byte[8];
        entryState[0] = 1;
        records.Add(entryState);
        var code = new List<byte>(checked((callChain.Length * 4) + 2));
        foreach (var target in targets)
        {
            var genericCall = target.Call;
            var call = genericCall.GenericMethodCall!;
            var targetDefinition = target.Definition;
            var inputs = new TrRmRegisterBodyCallInput[
                target.Function.ParameterCount];
            var assignedParameters = new bool[inputs.Length];
            for (var argumentIndex = 0;
                argumentIndex < genericCall.Arguments.Length;
                argumentIndex++)
            {
                var argument = genericCall.Arguments[argumentIndex];
                var formalIndex
                    = genericCall.ArgumentParameterIndices.IsDefault
                        ? argumentIndex
                        : genericCall.ArgumentParameterIndices[
                            argumentIndex];
                if (argument is not BoundRead read
                    || (uint)formalIndex >= (uint)inputs.Length
                    || assignedParameters[formalIndex]
                    || (uint)read.Variable
                        >= (uint)(target.ResultVariable < 0
                            ? function.Variables.Length
                            : target.ResultVariable)
                    || (uint)read.Variable
                        >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type
                        != argument.Type
                    || !GenericMethodTypeMatchesAfterSubstitution(
                        target.Function.Variables[formalIndex].Type,
                        argument.Type, targetDefinition, definition,
                        call.TypeArguments, 0)
                    || target.Function.Variables[formalIndex].IsMutable
                    || target.Function.Variables[formalIndex].IsOut
                    || target.Function.Variables[formalIndex].IsInit
                    || target.Function.Variables[formalIndex]
                        .IsImplicitRef
                    || function.Variables[read.Variable].IsMutable
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit
                    || function.Variables[read.Variable].IsImplicitRef
                    || homeIndicesByVariable[read.Variable] < 0)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has a call-chain input outside the Copy Home subset.");
                }

                var callerFormalOrdinal = -1;
                for (var ordinal = 0;
                    ordinal < definition.Formals.Length; ordinal++)
                {
                    if (definition.Formals[ordinal].PlaceholderType
                        == argument.Type)
                    {
                        callerFormalOrdinal = ordinal;
                        break;
                    }
                }
                if (callerFormalOrdinal >= 0
                    && definition.Formals[callerFormalOrdinal]
                        .OwnershipDomain != 1)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' reads a non-Copy caller formal in a call chain.");
                }

                assignedParameters[formalIndex] = true;
                inputs[formalIndex] = new TrRmRegisterBodyCallInput(
                    2, 1, checked((ushort)homeIndicesByVariable[
                        read.Variable]));
            }

            var resultType = target.ResultVariable < 0
                ? PrimitiveType.Void
                : function.Variables[
                    target.ResultVariable].Type;
            if (assignedParameters.Any(static assigned => !assigned)
                || !GenericMethodTypeMatchesAfterSubstitution(
                    target.Function.ReturnType,
                    resultType,
                    targetDefinition, definition, call.TypeArguments, 0))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a call-chain signature outside the checked subset.");
            }

            var targetArguments
                = new TrRmRegisterBodyGenericCallArgument[
                    call.TypeArguments.Length];
            for (var ordinal = 0; ordinal < targetArguments.Length;
                ordinal++)
            {
                var actual = call.TypeArguments[ordinal];
                var formal = targetDefinition.Formals[ordinal];
                var callerFormalOrdinal = -1;
                for (var candidate = 0;
                    candidate < definition.Formals.Length; candidate++)
                {
                    if (definition.Formals[candidate].PlaceholderType
                        == actual)
                    {
                        callerFormalOrdinal = candidate;
                        break;
                    }
                }

                if (callerFormalOrdinal >= 0)
                {
                    var callerFormal
                        = definition.Formals[callerFormalOrdinal];
                    if (!SemanticVerifier
                            .IsGenericMethodFormalContractSatisfied(
                                formal.OwnershipDomain,
                                formal.NullabilityDomain,
                                formal.RequiresWritable,
                                formal.RequiresBlittable,
                                callerFormal.OwnershipDomain,
                                callerFormal.NullabilityDomain,
                                callerFormal.RequiresWritable,
                                callerFormal.RequiresBlittable))
                    {
                        throw Unsupported(
                            $"Generic MethodDef '{definition.Name}' forwards a caller formal without satisfying the target domain.");
                    }
                }
                else if (actual == PrimitiveType.Void
                    || formal.OwnershipDomain == 2
                    || formal.RequiresWritable)
                {
                    throw Unsupported(
                        "A G1 GenericCall chain has a TypeActual outside its supported domain.");
                }

                targetArguments[ordinal]
                    = new TrRmRegisterBodyGenericCallArgument(1,
                        GenericMethodTypeSpecRef(actual,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows, spanTypes,
                            proofProgram.CheckedReferenceTypes,
                            definition.DefinitionOrdinal, definition,
                            allowSpanType: true,
                            nominalTypes: nominalTypes));
            }

            var methodToken = 0x1600_0000u
                | checked((uint)target.MethodRow);
            var methodMapIndex = methodMap.GetIndex(methodToken);
            var targetRecordIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeGenericCallTarget(
                    1, 0, methodMapIndex, 0, 0, targetArguments,
                    schema));
            var callSiteIndex = checked((ushort)records.Count);
            var hasResultHome = target.ResultVariable >= 0;
            var resultHome = hasResultHome
                ? checked((ushort)homeIndicesByVariable[
                    target.ResultVariable]) : (ushort)0;
            records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
                4, hasResultHome ? (byte)2 : (byte)0,
                targetRecordIndex, resultHome, inputs, [], schema: schema));
            if (callSiteIndex <= byte.MaxValue)
            {
                code.Add(0x90);
                code.Add(checked((byte)callSiteIndex));
            }
            else
            {
                code.Add(0xE0);
                code.Add(0);
                code.Add((byte)callSiteIndex);
                code.Add((byte)(callSiteIndex >> 8));
            }
        }

        if (!hasUnitResult)
        {
            var returnHome = homeIndicesByVariable[
                callChain[^1].ResultVariable];
            if (returnHome > byte.MaxValue)
            {
                throw Unsupported(
                    "A G1 forwarding chain exceeds the compact return-Home index.");
            }
            code.Add(0xB8);
            code.Add(checked((byte)returnHome));
        }
        return TrRmRegisterBodyWriter.Encode(homeMap,
            typeMap.Encode(), methodMap.Encode(), default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), schema: schema);
    }

    private static bool TrySubstituteGenericMethodInterfaceConstraint(
        BoundInterfaceType type,
        IReadOnlyDictionary<int, int> callerFormalOrdinals,
        out BoundInterfaceType substituted, int depth = 0)
    {
        substituted = null!;
        if (depth > SemanticVerifier.MaximumDepth
            || type.TypeArguments.IsDefault)
        {
            return false;
        }
        if (type.Kind == BoundInterfaceTypeKind.TypeParameter)
        {
            if (type.TypeParameterScope
                    != BoundInterfaceTypeParameterScope.Member
                || !callerFormalOrdinals.TryGetValue(
                    type.TypeParameterOrdinal, out var callerOrdinal))
            {
                return false;
            }

            substituted = type with {
                TypeParameterOrdinal = callerOrdinal,
            };
            return true;
        }

        var typeArguments
            = ImmutableArray.CreateBuilder<BoundInterfaceTypeArgument>(
                type.TypeArguments.Length);
        foreach (var argument in type.TypeArguments)
        {
            if (argument is null
                || argument.ConstantParameterScope
                    != BoundInterfaceTypeParameterScope.None)
            {
                return false;
            }

            BoundInterfaceType? argumentType = null;
            BoundInterfaceType? constantType = null;
            if (argument.Type is BoundInterfaceType nestedType
                && !TrySubstituteGenericMethodInterfaceConstraint(
                    nestedType, callerFormalOrdinals,
                    out argumentType, depth + 1))
            {
                return false;
            }
            if (argument.ConstantType is BoundInterfaceType nestedConstantType
                && !TrySubstituteGenericMethodInterfaceConstraint(
                    nestedConstantType, callerFormalOrdinals,
                    out constantType, depth + 1))
            {
                return false;
            }

            typeArguments.Add(argument with {
                Type = argumentType,
                ConstantType = constantType,
            });
        }

        substituted = type with {
            TypeArguments = typeArguments.MoveToImmutable(),
        };
        return true;
    }

    private static byte[] EncodeGenericMethodRequirementBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        BoundProgram program,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        IReadOnlyDictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        IReadOnlyDictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint> exportedInterfaceMethodDefRows,
        IReadOnlyDictionary<GenericImportedInterfaceMethodKey, uint>
            importedInterfaceMethodImportRows,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        IReadOnlyDictionary<(int FormalOrdinal, int ConstraintOrdinal),
            (uint ConstraintRow, uint TypeSpecRef)>
                interfaceConstraintRowRefs,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        if (!schema.UsesMethodParameterLabels()
            || !TryGetDirectGenericCallReturn(function, out var call,
                out var resultVariable)
            || call is null
            || call.InterfaceCallIdentity is not
                BoundInterfaceCallIdentity identity
            || !identity.IsConstraintCall
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || call.Type == PrimitiveType.Void
            || call.Type != function.ReturnType
            || identity.Requirement.AccessorOrdinal != -1
            || !BoundInterfaceContract.AreEquivalent(
                identity.WitnessInterface, identity.RequestedInterface)
            || !BoundInterfaceContract.AreEquivalent(
                identity.Requirement.DeclaringInterface,
                identity.RequestedInterface)
            || (resultVariable < 0
                ? function.Variables.Length != function.ParameterCount
                : resultVariable != function.ParameterCount
                    || function.Variables.Length
                        != function.ParameterCount + 1)
            || function.ParameterCount > 8)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an interface-requirement Body outside the direct-return subset.");
        }

        var proofProgram = definition.ProofProgram;
        if ((uint)call.Function >= (uint)proofProgram.Functions.Length)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' calls an unknown interface requirement.");
        }

        var requirementStub = proofProgram.Functions[call.Function];
        if (requirementStub.InterfaceRequirement
                is not BoundInterfaceMemberIdentity targetRequirement
            || !BoundInterfaceContract.AreEquivalent(targetRequirement,
                identity.Requirement)
            || !requirementStub.IsExternal || !requirementStub.IsPrivate
            || requirementStub.IsStatic
            || requirementStub.DeclaringStruct is not null
            || requirementStub.GenericMethodProofCall is not null
            || requirementStub.ParameterCount != call.Arguments.Length
            || requirementStub.Variables.Length
                != requirementStub.ParameterCount
            || requirementStub.ParameterCount is < 1 or > 9
            || requirementStub.Variables[0].Type
                != identity.ImplementingType
            || requirementStub.Variables[0].IsOut
            || requirementStub.Variables[0].IsInit
            || requirementStub.Variables[0].IsImplicitRef
            || requirementStub.ReturnType != call.Type
            || requirementStub.DoesNotReturn
            || requirementStub.RequiresUnsafeCaller)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an unsupported interface requirement target.");
        }

        var interfaceName
            = identity.Requirement.DeclaringInterface.NominalName;
        if (interfaceName is null)
        {
            throw Unsupported(
                "A G1 interface requirement has no declaring-interface identity.");
        }

        var arity
            = identity.Requirement.DeclaringInterface.TypeArguments.Length;
        var matchingInterfaces = program.Interfaces.Where(candidate =>
            candidate.ModuleImport
                == identity.Requirement.DeclaringInterface.ModuleImport
            && StringComparer.Ordinal.Equals(candidate.QualifiedName,
                interfaceName)
            && candidate.Formals.Length == arity).ToArray();
        if (matchingInterfaces.Length != 1
            || (uint)identity.Requirement.MemberOrdinal
                >= (uint)matchingInterfaces[0].Members.Length)
        {
            throw Unsupported(
                "A G1 interface requirement has no exact local or imported InterfaceMethod target.");
        }

        var matchingInterface = matchingInterfaces[0];
        var requirementMember = matchingInterface.Members[
            identity.Requirement.MemberOrdinal];
        if (!StringComparer.Ordinal.Equals(requirementMember.DeclarationKey,
                identity.Requirement.DeclarationKey)
            || (requirementMember.RequiresMutableReceiver
                && !schema.UsesMethodReceiverAccess())
            || requirementStub.Variables[0].IsMutable
                != requirementMember.RequiresMutableReceiver)
        {
            throw Unsupported(
                "A G1 interface requirement has no exact local or imported InterfaceMethod target.");
        }

        uint targetMethodToken;
        if (identity.Requirement.DeclaringInterface.ModuleImport
            is BoundModuleImport importedInterface)
        {
            if (!importedInterfaceMethodImportRows.TryGetValue(
                    new GenericImportedInterfaceMethodKey(
                        importedInterface.ProducerIdentity,
                        importedInterface.ExportKey, interfaceName, arity,
                        identity.Requirement.MemberOrdinal,
                        identity.Requirement.DeclarationKey,
                        importedInterface.TargetRequirement,
                        importedInterface.RuntimePolicy),
                    out var importedMethodImportRow))
            {
                throw Unsupported(
                    "A G1 interface requirement has no exact MethodDef Import.");
            }

            targetMethodToken = 0x1C00_0000u | importedMethodImportRow;
        }
        else
        {
            if (!exportedInterfaceTypeSpecRows.ContainsKey(
                    (interfaceName, arity))
                || !exportedInterfaceMethodDefRows.TryGetValue(
                    (interfaceName, arity,
                        identity.Requirement.MemberOrdinal,
                        identity.Requirement.DeclarationKey),
                    out var localRequirementMethodRow))
            {
                throw Unsupported(
                    "A G1 interface requirement has no directly exported InterfaceMethod MethodDef.");
            }

            targetMethodToken = 0x1600_0000u
                | localRequirementMethodRow;
        }

        var implementingFormalOrdinal = -1;
        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            if (definition.Formals[ordinal].PlaceholderType
                != identity.ImplementingType)
            {
                continue;
            }

            if (implementingFormalOrdinal >= 0)
            {
                throw Unsupported(
                    "A G1 interface requirement receiver has an ambiguous formal identity.");
            }
            implementingFormalOrdinal = ordinal;
        }
        if (implementingFormalOrdinal < 0)
        {
            throw Unsupported(
                "A G1 interface requirement receiver is not an in-scope type formal.");
        }

        var callerFormal = definition.Formals[implementingFormalOrdinal];
        if (callerFormal.InterfaceConstraints.IsDefault
            || callerFormal.OwnershipDomain != 1
            || (requirementMember.RequiresMutableReceiver
                && !callerFormal.RequiresWritable))
        {
            throw Unsupported(
                "A G1 interface requirement receiver lacks a supported Copy formal contract.");
        }

        var matchingConstraintOrdinal = -1;
        for (var ordinal = 0;
            ordinal < callerFormal.InterfaceConstraints.Length; ordinal++)
        {
            if (!BoundInterfaceContract.AreEquivalent(
                    callerFormal.InterfaceConstraints[ordinal],
                    identity.RequestedInterface))
            {
                continue;
            }

            if (matchingConstraintOrdinal >= 0)
            {
                throw Unsupported(
                    "A G1 interface requirement receiver has duplicate Constraint evidence.");
            }
            matchingConstraintOrdinal = ordinal;
        }
        if (matchingConstraintOrdinal < 0
            || !interfaceConstraintRowRefs.TryGetValue(
                (implementingFormalOrdinal, matchingConstraintOrdinal),
                out var interfaceConstraint)
            || interfaceConstraint.ConstraintRow == 0
            || interfaceConstraint.TypeSpecRef == 0)
        {
            throw Unsupported(
                "A G1 interface requirement call has no exact caller Constraint evidence.");
        }

        var argumentIndices = new bool[requirementStub.ParameterCount];
        var inputs = new TrRmRegisterBodyCallInput[
            requirementStub.ParameterCount];
        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var argument = call.Arguments[argumentIndex];
            var parameterIndex = call.ArgumentParameterIndices.IsDefault
                ? argumentIndex
                : call.ArgumentParameterIndices[argumentIndex];
            var isReceiver = parameterIndex == 0;
            var inputVariable = argument switch {
                BoundRead read => read.Variable,
                BoundReceiverLoan loan when isReceiver
                    && !requirementMember.RequiresMutableReceiver
                    => loan.Variable,
                BoundMutableReceiverLoan loan when isReceiver
                    => loan.Variable,
                _ => -1,
            };
            if ((uint)parameterIndex >= (uint)inputs.Length
                || (argumentIndex == 0 && parameterIndex != 0)
                || argumentIndices[parameterIndex]
                || (isReceiver
                    ? argument is not (BoundRead or BoundReceiverLoan
                        or BoundMutableReceiverLoan)
                    : argument is not BoundRead)
                || (isReceiver
                    && requirementMember.RequiresMutableReceiver
                    && argument is not BoundMutableReceiverLoan)
                || (uint)inputVariable >= (uint)function.ParameterCount
                || function.Variables[inputVariable].Type != argument.Type
                || requirementStub.Variables[parameterIndex].Type
                    != argument.Type
                || (!isReceiver
                    && function.Variables[inputVariable].IsMutable)
                || function.Variables[inputVariable].IsOut
                || function.Variables[inputVariable].IsInit
                || function.Variables[inputVariable].IsImplicitRef
                || (parameterIndex != 0
                    && requirementStub.Variables[parameterIndex].IsMutable)
                || requirementStub.Variables[parameterIndex].IsOut
                || requirementStub.Variables[parameterIndex].IsInit
                || requirementStub.Variables[parameterIndex]
                    .IsImplicitRef)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an interface-call argument outside the parameter-Home subset.");
            }

            var argumentFormalOrdinal = -1;
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                if (definition.Formals[ordinal].PlaceholderType
                    == argument.Type)
                {
                    argumentFormalOrdinal = ordinal;
                    break;
                }
            }
            if (argumentFormalOrdinal >= 0
                && definition.Formals[argumentFormalOrdinal]
                    .OwnershipDomain != 1)
            {
                throw Unsupported(
                    "A G1 interface-call input does not have the Copy domain required by its parameter Home.");
            }

            argumentIndices[parameterIndex] = true;
            var sourceUse = (byte)1;
            if (isReceiver && schema.UsesMethodReceiverAccess())
            {
                sourceUse = requirementMember.RequiresMutableReceiver
                    ? (byte)4 : (byte)3;
            }
            inputs[parameterIndex] = new TrRmRegisterBodyCallInput(
                2, sourceUse, checked((ushort)inputVariable));
        }
        if (argumentIndices.Any(static assigned => !assigned)
            || requirementStub.ParameterCount != call.Arguments.Length)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an incomplete interface-call input list.");
        }

        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        uint GetBodyTypeToken(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                throw Unsupported(
                    "G1 interface-requirement calls require a non-unit result.");
            }

            return CreateStandardBodyTypeMapToken(
                GenericMethodTypeSpecRef(type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows, spanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes));
        }

        var homeCount = function.ParameterCount + 1;
        var homeMap = new byte[checked(homeCount * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                typeMap.GetIndex(GetBodyTypeToken(
                    function.Variables[parameter].Type)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4), checked((ushort)parameter));
        }

        var resultHome = checked((ushort)function.ParameterCount);
        var resultOffset = resultHome * 8;
        homeMap[resultOffset] = 3;
        homeMap[resultOffset + 1] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(
            homeMap.AsSpan(resultOffset + 2),
            typeMap.GetIndex(GetBodyTypeToken(function.ReturnType)));

        var targetMethodMap = new TrRmRegisterBodyTokenMapBuilder();
        var targetMethodMapIndex = targetMethodMap.GetIndex(
            targetMethodToken);
        var records = new List<ReadOnlyMemory<byte>>(3);
        var entryState = new byte[8];
        entryState[0] = 1;
        records.Add(entryState);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeGenericCallTarget(
            2, 1, targetMethodMapIndex,
            interfaceConstraint.TypeSpecRef,
            interfaceConstraint.ConstraintRow, [], schema));
        var callSiteIndex = checked((ushort)records.Count);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
            4, 2, 1, resultHome, inputs, [], schema: schema));

        var code = new byte[] {
            0x90,
            checked((byte)callSiteIndex),
            0xB8,
            checked((byte)resultHome),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, typeMap.Encode(),
            targetMethodMap.Encode(), default, default,
            [new TrRmRegisterBodyBlock(0, checked((uint)code.Length), 0)],
            records, code, schema: schema);
    }

    private static byte[] AddGenericMethodMetadata(byte[] artifact,
        BoundProgram program, SourceGenericMethodExports sourceMethodExports,
        IReadOnlyList<GenericMethodEmission> emissions,
        IReadOnlyDictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        IReadOnlyDictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint> exportedInterfaceMethodDefRows,
        TrRmArtifactSchema schema)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var sections = new List<TrRmSection>(index.SectionCount + 2);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var stringSection = GetSection(sections, 0x0002);
        var stringRows = new TrRmStringTableReader(stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        var names = new List<(string Text, int Encoding)>(
            stringRows.Count + (emissions.Count * 2));
        for (var row = 1; row <= stringRows.Count; row++)
        {
            names.Add((stringRows.DecodeNamespace(row), 1));
        }

        var methodNameRows = new Dictionary<int, int>();
        var formalNameRows = new Dictionary<(int Definition, int Formal),
            int>();
        foreach (var emission in emissions)
        {
            methodNameRows.Add(emission.Definition.DefinitionOrdinal,
                names.Count + 1);
            names.Add((emission.Definition.Name, 1));
            for (var ordinal = 0;
                ordinal < emission.Definition.Formals.Length; ordinal++)
            {
                formalNameRows.Add((emission.Definition.DefinitionOrdinal,
                    ordinal), names.Count + 1);
                names.Add((emission.Definition.Formals[ordinal].Name, 1));
            }
        }

        var exportNameRows = new List<int>(sourceMethodExports.Exports.Length);
        foreach (var export in sourceMethodExports.Exports)
        {
            exportNameRows.Add(names.Count + 1);
            names.Add((export.ExportKey, 1));
        }
        var exportedDefinitionOrdinals = sourceMethodExports.Exports
            .Select(static export => export.Definition.DefinitionOrdinal)
            .ToHashSet();
        var labelSection = default(TrRmSection);
        var labelBearing = schema.UsesMethodParameterLabels();
        var hasLabelSection = labelBearing
            && TryGetSection(sections, 0x0066, out labelSection);
        var methodParameterLabelBytes = hasLabelSection
            ? labelSection.Bytes.ToArray().ToList() : [];
        var methodParameterLabelCount = hasLabelSection
            ? checked((int)labelSection.RecordCount) : 0;
        if (labelBearing)
        {
            foreach (var emission in emissions)
            {
                if (!exportedDefinitionOrdinals.Contains(
                        emission.Definition.DefinitionOrdinal))
                {
                    continue;
                }

                var function = emission.ProofFunction;
                if (function.Variables.Length < function.ParameterCount)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{emission.Definition.Name}' has no complete checked parameter-name view.");
                }

                for (var ordinal = 0; ordinal < function.ParameterCount;
                    ordinal++)
                {
                    if (methodParameterLabelCount == 0xFF_FFFF)
                    {
                        throw Unsupported(
                            "The G1L1 MethodParameterLabel table exceeds its row limit.");
                    }

                    var nameRow = names.Count + 1;
                    names.Add((function.Variables[ordinal].Name, 1));
                    AppendUInt32(methodParameterLabelBytes,
                        checked((uint)emission.MethodRow));
                    AppendUInt32(methodParameterLabelBytes,
                        checked((uint)ordinal));
                    AppendUInt32(methodParameterLabelBytes,
                        checked((uint)nameRow));
                    methodParameterLabelCount
                        = checked(methodParameterLabelCount + 1);
                }
            }
        }

        var typeSpecSection = GetSection(sections, 0x0011);
        var typeDefCount = GetCount(sections, 0x0013);
        var importCount = GetCount(sections, 0x001C);
        var nativeFunctionPointerSignatureCount
            = GetCount(sections, 0x0052);
        var typeSpecRowsIndex = new TrRmVariableTableIndex(
            typeSpecSection.Bytes.Span,
            checked((int)typeSpecSection.RecordCount), 2, 1);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            checked(typeSpecRowsIndex.Count
                + emissions.Sum(static emission =>
                    emission.Definition.Formals.Length)));
        for (var row = 1; row <= typeSpecRowsIndex.Count; row++)
        {
            typeSpecRows.Add(typeSpecRowsIndex.GetRow(row).ToArray());
        }
        var importedInterfaceImportRows
            = ReadImportedInterfaceImportRows(sections, stringRows);
        var importedInterfaceMethodImportRows
            = ReadImportedInterfaceMethodImportRows(sections, stringRows);

        var methodSection = TryGetSection(sections, 0x0016,
            out var foundMethodSection) ? foundMethodSection : default;
        var methodBytes = methodSection.Bytes.ToArray().ToList();
        if (schema.UsesMethodReceiverAccess())
        {
            if (methodSection.RecordCount
                < checked((uint)program.Functions.Length))
            {
                throw Invalid(
                    "The G1 base MethodDef range does not cover its source functions.");
            }

            for (var functionIndex = 0;
                functionIndex < program.Functions.Length; functionIndex++)
            {
                var function = program.Functions[functionIndex];
                if (function.DeclaringStruct is null || function.IsStatic
                    || function.IsConstructor || function.IsClassConstructor
                    || function.IsCleanup || function.Variables.IsEmpty
                    || function.Variables[0].IsInit
                    || (!function.Variables[0].IsMutable
                        && !ManagedClassTypes.IsWritable(
                            function.Variables[0].Type)))
                {
                    continue;
                }

                var methodFlags = function.IsPrivate ? 0u
                    : function.IsInternal ? 1u : 2u;
                WriteUInt32(methodBytes,
                    checked((functionIndex * 28) + 12),
                    methodFlags | 4u);
            }
        }

        var signatureSection = TryGetSection(sections, 0x0017,
            out var foundSignatureSection) ? foundSignatureSection : default;
        var signatureBytes = signatureSection.Bytes.ToArray().ToList();
        var parameterSection = TryGetSection(sections, 0x0018,
            out var foundParameterSection)
            ? foundParameterSection : default;
        var parameterBytes = parameterSection.Bytes.ToArray().ToList();
        var bodySection = TryGetSection(sections, 0x0020,
            out var foundBodySection) ? foundBodySection : default;
        var bodyRows = new List<ReadOnlyMemory<byte>>(
            checked((int)bodySection.RecordCount) + emissions.Count);
        if (bodySection.RecordCount != 0)
        {
            var bodyIndex = new TrRmVariableTableIndex(bodySection.Bytes.Span,
                checked((int)bodySection.RecordCount), 1, 1);
            for (var row = 1; row <= bodyIndex.Count; row++)
            {
                bodyRows.Add(bodyIndex.GetRow(row).ToArray());
            }
        }

        var genericParameterRows = ReadGenericParamRows(sections);
        var constraintRows = ReadConstraintRows(sections);
        var contractRows = ReadVariableRows(sections, 0x001B, 8, 4);
        var nominalTypes = new GenericMethodNominalTypeWriter(sections,
            names, typeSpecRows, genericParameterRows, constraintRows,
            program, emissions, schema);
        var baseBuiltInTypeSpecRows = GetBuiltInTypeSpecRows(typeSpecRows);
        var hasBlittableConstraint = false;
        var hasWriteConstraint = constraintRows.Any(
            static constraint => constraint.Kind == 2
                && constraint.Payload == 2);
        var emissionOrdinal = 0;
        foreach (var emission in emissions)
        {
            var builtInTypeSpecRows = new Dictionary<PrimitiveType, uint>(
                baseBuiltInTypeSpecRows);
            var definition = emission.Definition;
            var methodRow = emission.MethodRow;
            var signatureRow = checked((int)signatureSection.RecordCount
                + emissionOrdinal + 1);
            emissionOrdinal++;
            var function = emission.ProofFunction;
            var parameterCount = function.ParameterCount;
            if (function.IsStatic is false
                || parameterCount != definition.RuntimeParameterCount
                || parameterCount != function.Variables
                    .Take(parameterCount)
                    .Count(static parameter => !parameter.IsOut
                        && !parameter.IsInit)
                || parameterCount < 0
                || parameterCount > 8)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an unsupported parameter shape.");
            }

            nominalTypes.Prepare(definition, function);
            var firstGenericParam = genericParameterRows.Count + 1;
            var formalTypeSpecRows = new Dictionary<PrimitiveType, uint>();
            var genericParamRows = new int[definition.Formals.Length];
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var genericParamRow = checked(
                    genericParameterRows.Count + ordinal + 1);
                genericParamRows[ordinal] = genericParamRow;
                var typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                    checked((uint)genericParamRow)));
                if (!formalTypeSpecRows.TryAdd(formal.PlaceholderType,
                    typeSpecRow))
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has duplicate placeholder type identities.");
                }
            }

            var interfaceConstraintVisits
                = SemanticVerifier.MaximumAnalysisVisits;
            var interfaceConstraintRowRefs = new Dictionary<(
                int FormalOrdinal, int ConstraintOrdinal),
                (uint ConstraintRow, uint TypeSpecRef)>();
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var genericParamRow = genericParamRows[ordinal];
                var constraintFirst = constraintRows.Count + 1;
                if (formal.RequiresWritable)
                {
                    constraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, 2, 2));
                    hasWriteConstraint = true;
                }
                if (formal.RequiresBlittable)
                {
                    constraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, 2, 3));
                    hasBlittableConstraint = true;
                }
                if (formal.InterfaceConstraints.IsDefault)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has an invalid interface-constraint view.");
                }
                if (!formal.InterfaceConstraints.IsEmpty)
                {
                    if (!schema.UsesMethodParameterLabels())
                    {
                        throw Unsupported(
                            "G1 interface constraints require the exact G1L1 schema.");
                    }

                    for (var constraintOrdinal = 0;
                        constraintOrdinal
                            < formal.InterfaceConstraints.Length;
                        constraintOrdinal++)
                    {
                        var interfaceConstraint
                            = formal.InterfaceConstraints[
                                constraintOrdinal];
                        var constraintTypeSpec
                            = GenericMethodInterfaceConstraintTypeSpecRef(
                                interfaceConstraint, definition,
                                exportedInterfaceTypeSpecRows,
                                importedInterfaceImportRows,
                                formalTypeSpecRows, genericParamRows,
                                builtInTypeSpecRows, typeSpecRows, 0,
                                ref interfaceConstraintVisits);
                        var constraintRow
                            = checked((uint)constraintRows.Count + 1);
                        constraintRows.Add(new TrRmConstraintRow(
                            genericParamRow, 1, constraintTypeSpec));
                        if (!interfaceConstraintRowRefs.TryAdd(
                                (ordinal, constraintOrdinal),
                                (constraintRow, constraintTypeSpec)))
                        {
                            throw Invalid(
                                "A G1L1 GenericParam interface Constraint has an ambiguous source identity.");
                        }
                    }
                }
                var constraintCount = constraintRows.Count
                    - constraintFirst + 1;
                if (constraintCount == 0)
                {
                    constraintFirst = 0;
                }

                var defaultArgument = formal.TypeDefault is null ? 0u
                    : GenericMethodTypeDefaultRef(formal.TypeDefault,
                        ordinal, definition, formalTypeSpecRows,
                        builtInTypeSpecRows, typeSpecRows, nominalTypes);
                genericParameterRows.Add(new TrRmGenericParamRow(
                    0x1600_0000u | checked((uint)methodRow), ordinal,
                    formalNameRows[(definition.DefinitionOrdinal, ordinal)],
                    constraintFirst, constraintCount, 1,
                    formal.OwnershipDomain, formal.NullabilityDomain,
                    0, defaultArgument));
            }

            var firstParameter = parameterCount == 0
                ? 0 : (parameterBytes.Count / 16) + 1;
            for (var parameter = 0; parameter < parameterCount; parameter++)
            {
                var typeSpecRef = GenericMethodTypeSpecRef(
                    function.Variables[parameter].Type,
                    formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                    definition.ProofProgram.SpanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes);
                AppendUInt32(parameterBytes, checked((uint)signatureRow));
                AppendUInt32(parameterBytes, checked((uint)parameter));
                AppendUInt32(parameterBytes, typeSpecRef);
                parameterBytes.Add(0);
                parameterBytes.Add(0);
                parameterBytes.Add(0);
                parameterBytes.Add(0);
            }

            var resultTypeSpecRef = GenericMethodTypeSpecRef(
                function.ReturnType, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows,
                definition.ProofProgram.SpanTypes,
                definition.ProofProgram.CheckedReferenceTypes,
                definition.DefinitionOrdinal, definition,
                allowSpanType: true, nominalTypes: nominalTypes);
            AppendUInt32(signatureBytes, resultTypeSpecRef);
            AppendUInt32(signatureBytes, 0);
            AppendUInt32(signatureBytes, checked((uint)firstParameter));
            AppendUInt32(signatureBytes, checked((uint)parameterCount));
            AppendUInt32(signatureBytes,
                checked((uint)firstGenericParam));
            AppendUInt32(signatureBytes,
                checked((uint)definition.Formals.Length));

            var (hasFloatingTypes, _, _) = AnalyzeFunction(function);
            EnsureGenericBodyTypeMap(hasFloatingTypes,
                HasNativeWidthScalar(function),
                typeSpecRows);
            var containsGenericMethodCall
                = ContainsGenericMethodCall(function);
            var containsInterfaceCall = ContainsInterfaceCall(function);
            if (containsGenericMethodCall && containsInterfaceCall)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' combines unsupported GenericCall target roles.");
            }

            bodyRows.Add(containsGenericMethodCall
                ? EncodeGenericMethodForwardingBody(function,
                    definition, emissions, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows,
                    definition.ProofProgram.SpanTypes, schema,
                    nominalTypes)
                : containsInterfaceCall
                    ? EncodeGenericMethodRequirementBody(function,
                        definition, program,
                        definition.ProofProgram.SpanTypes,
                        exportedInterfaceTypeSpecRows,
                        exportedInterfaceMethodDefRows,
                        importedInterfaceMethodImportRows,
                        formalTypeSpecRows, builtInTypeSpecRows,
                        typeSpecRows, interfaceConstraintRowRefs, schema,
                        nominalTypes)
                : IsDirectGenericMethodMoveReturn(function)
                    ? EncodeGenericMethodMoveReturnBody(function,
                        definition, formalTypeSpecRows,
                        builtInTypeSpecRows, typeSpecRows,
                        definition.ProofProgram.SpanTypes, schema,
                        nominalTypes)
                    : EncodeBody(function, hasFloatingTypes, 0,
                        new Dictionary<PrimitiveType, int>(),
                        new Dictionary<PrimitiveType, int>()));

            var contractFirst = 0;
            var contractCount = 0;
            var effectMask = (function.DoesNotReturn ? 2u : 0u)
                | (definition.RequiresUnsafeCaller ? 4u : 0u);
            if (effectMask != 0)
            {
                contractFirst = contractRows.Count + 1;
                contractRows.Add(EncodeGenericMethodEffectContract(
                    methodRow, s_effectPayloads[
                        checked((int)effectMask)]));
                contractCount++;
            }
            foreach (var dependencyContract
                in EncodeGenericMethodDependencyContracts(function,
                    methodRow))
            {
                if (contractFirst == 0)
                {
                    contractFirst = contractRows.Count + 1;
                }
                contractRows.Add(dependencyContract);
                contractCount++;
            }

            AppendUInt32(methodBytes, 0);
            AppendUInt32(methodBytes, checked((uint)methodNameRows[
                definition.DefinitionOrdinal]));
            AppendUInt32(methodBytes, checked((uint)signatureRow));
            AppendUInt32(methodBytes, definition.IsPublic ? 2u : 0u);
            AppendUInt32(methodBytes, checked((uint)contractFirst));
            AppendUInt32(methodBytes, checked((uint)contractCount));
            AppendUInt32(methodBytes,
                checked((uint)bodyRows.Count));
        }

        nominalTypes.Complete();
        typeDefCount = GetCount(sections, 0x0013);
        importCount = GetCount(sections, 0x001C);
        hasWriteConstraint |= constraintRows.Any(
            static constraint => constraint.Kind == 2
                && constraint.Payload == 2);
        hasBlittableConstraint |= constraintRows.Any(
            static constraint => constraint.Kind == 2
                && constraint.Payload == 3);
        var rewrittenStrings = TrRmStringTableWriter.EncodeTextRows(names);
        var removedClosedExports = new HashSet<uint>();
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length; functionIndex++)
        {
            if (program.Functions[functionIndex]
                .GenericMethodInstantiation is not null)
            {
                _ = removedClosedExports.Add(0x1600_0000u
                    | checked((uint)(functionIndex + 1)));
                WriteUInt32(methodBytes,
                    checked((functionIndex * 28) + 12), 0);
            }
        }

        var exportRows = ReadExportRows(sections, removedClosedExports);
        exportRows.AddRange(nominalTypes.Exports);
        for (var exportIndex = 0;
            exportIndex < sourceMethodExports.Exports.Length; exportIndex++)
        {
            var export = sourceMethodExports.Exports[exportIndex];
            var emission = emissions.First(candidate =>
                candidate.Definition.DefinitionOrdinal
                    == export.Definition.DefinitionOrdinal);
            exportRows.Add(new TrRmExportRow(
                0x1600_0000u | checked((uint)emission.MethodRow),
                exportNameRows[exportIndex]));
        }
        var exportBytes = EncodeExportRows(exportRows);

        var featureRows = ReadFeatureRows(sections);
        if (featureRows.Any(static feature =>
                feature.FeatureId is
                    TrRmFeatureTableReader.GenericMethodCallsFeatureId
                    or TrRmFeatureTableReader.MethodParameterLabelsFeatureId
                    or TrRmFeatureTableReader.GenericWriteRequirementsFeatureId))
        {
            throw Unsupported("The source emitter cannot replace pre-existing G1 Features.");
        }
        if (hasWriteConstraint && !schema.UsesGenericWriteRequirements())
        {
            throw Unsupported(
                "The selected TRRM schema does not admit generic Write requirements.");
        }
        var hasGenericCallTargets = bodyRows.Any(body =>
            HasGenericCallTargets(body, contractRows.Count, schema));
        if (emissions.Count != 0 || hasGenericCallTargets)
        {
            featureRows.Add(TrRmFeatureRow.GenericMethodCallsCandidate);
        }
        if (hasWriteConstraint)
        {
            featureRows.Add(
                TrRmFeatureRow.GenericWriteRequirementsCandidate);
        }
        if (labelBearing && (exportedDefinitionOrdinals.Count != 0
                || GetCount(sections, 0x0024) != 0))
        {
            featureRows.Add(TrRmFeatureRow.MethodParameterLabelsCandidate);
        }
        if (hasBlittableConstraint
            && !featureRows.Any(static feature =>
                feature.FeatureId
                    == TrRmFeatureTableReader.LayoutQueriesFeatureId))
        {
            featureRows.Add(TrRmFeatureRow.LayoutQueries);
        }
        nominalTypes.MergeFeatures(featureRows);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var featureCount = featureBytes.Length / 8;
        var moduleSection = GetSection(sections, 0x0001);
        var moduleBytes = moduleSection.Bytes.ToArray();
        if (moduleSection.RecordCount != 1)
        {
            throw Unsupported(
                "G1 source method emission requires one output Module.");
        }
        var oldFeatureCount = ReadInt(moduleBytes, 28);
        if (featureCount == 0)
        {
            WriteUInt32(moduleBytes, 24, 0);
        }
        else if (oldFeatureCount == 0)
        {
            WriteUInt32(moduleBytes, 24, 1);
        }
        WriteUInt32(moduleBytes, 28, checked((uint)featureCount));

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, importCount, genericParameterRows.Count,
            nativeFunctionPointerSignatureCount,
            schema,
            checked((int)methodSection.RecordCount + emissions.Count));
        var finalMethodBytes = methodBytes.ToArray();
        var finalSignatureBytes = signatureBytes.ToArray();
        var finalParameterBytes = parameterBytes.ToArray();
        var (genericParameterBytes, constraintBytes) =
            CreateGenericMetadataBytes(
            rewrittenStrings, names.Count, moduleBytes, finalMethodBytes,
            finalSignatureBytes, typeSpecBytes,
            checked((int)typeSpecRows.Count),
            genericParameterRows, constraintRows, sections, typeDefCount, importCount,
            nativeFunctionPointerSignatureCount, featureCount,
            checked((int)signatureSection.RecordCount
                + emissions.Count),
            finalParameterBytes.Length / 16, contractRows.Count,
            bodyRows.Count, GetCount(sections, 0x0010), schema);

        SetSection(sections, 0x0001, 1, moduleBytes);
        SetSection(sections, 0x0002, checked((uint)names.Count),
            rewrittenStrings);
        SetSection(sections, 0x0004, checked((uint)featureCount),
            featureBytes);
        SetSection(sections, 0x0011,
            checked((uint)typeSpecRows.Count), typeSpecBytes);
        SetSection(sections, 0x0016,
            checked(methodSection.RecordCount + (uint)emissions.Count),
            finalMethodBytes);
        SetSection(sections, 0x0017,
            checked(signatureSection.RecordCount
                + (uint)emissions.Count), finalSignatureBytes);
        SetSection(sections, 0x0018,
            checked((uint)(finalParameterBytes.Length / 16)),
            finalParameterBytes);
        SetSection(sections, 0x0019,
            checked((uint)genericParameterRows.Count),
            genericParameterBytes);
        SetSection(sections, 0x001A,
            checked((uint)constraintRows.Count), constraintBytes);
        SetSection(sections, 0x001B,
            checked((uint)contractRows.Count),
            contractRows.Count == 0 ? []
                : TrRmVariableTableWriter.EncodeBlobs(contractRows));
        SetSection(sections, 0x0020, checked((uint)bodyRows.Count),
            bodyRows.Count == 0 ? []
                : TrRmVariableTableWriter.EncodeBodies(bodyRows));
        SetSection(sections, 0x001D, checked((uint)exportRows.Count),
            exportBytes);
        SetSection(sections, 0x0066,
            checked((uint)methodParameterLabelCount),
            methodParameterLabelBytes.Count == 0 ? []
                : [.. methodParameterLabelBytes]);

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections,
            schema);
    }

    private static byte[] AddGenericMethodInterfaceMetadata(byte[] artifact,
        BoundProgram program, IReadOnlyList<TrRmSourceTypeExport> typeExports,
        SourceGenericMethodExports sourceMethodExports,
        TrRmArtifactSchema schema,
        out Dictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        out Dictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint> exportedInterfaceMethodDefRows)
    {
        exportedInterfaceTypeSpecRows = [];
        exportedInterfaceMethodDefRows = [];
        var artifactIndex = new TrRmArtifactIndex(artifact);
        var sections = new List<TrRmSection>(artifactIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < artifactIndex.SectionCount;
            sectionIndex++)
        {
            artifactIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        if (GetCount(sections, 0x0024) != 0
            || GetCount(sections, 0x0025) != 0
            || GetCount(sections, 0x0026) != 0
            || GetCount(sections, 0x0027) != 0
            || GetCount(sections, 0x0028) != 0
            || GetCount(sections, 0x0029) != 0)
        {
            throw Unsupported(
                "The G1 source emitter cannot merge pre-existing interface or witness rows.");
        }

        var emissions = CaptureGenericInterfaceEmissions(program,
            typeExports, schema);
        if (emissions.Count == 0)
        {
            return artifact;
        }

        var stringSection = GetSection(sections, 0x0002);
        var strings = new TrRmStringTableReader(stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        var names = new List<(string Text, int Encoding)>(strings.Count
            + (emissions.Count * 2));
        for (var row = 1; row <= strings.Count; row++)
        {
            names.Add((strings.DecodeNamespace(row), 1));
        }

        var exportRows = ReadExportRows(sections, []);
        var exportKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in exportRows)
        {
            if (!exportKeys.Add(strings.DecodeName(
                    export.ExportKeyStringRow)))
            {
                throw new InvalidDataException(
                    "The base TRRM artifact contains duplicate ExportKeys.");
            }
        }
        foreach (var export in sourceMethodExports.Exports)
        {
            if (!exportKeys.Add(export.ExportKey))
            {
                throw new ArgumentException(
                    "G1 interface ExportKeys must be unique within the Module.",
                    nameof(typeExports));
            }
        }
        foreach (var emission in emissions)
        {
            if (!exportKeys.Add(emission.Export.ExportKey))
            {
                throw new ArgumentException(
                    "G1 interface ExportKeys must be unique within the Module.",
                    nameof(typeExports));
            }
            foreach (var memberExport in
                emission.Export.InterfaceMethodExports)
            {
                if (!exportKeys.Add(memberExport.ExportKey))
                {
                    throw new ArgumentException(
                        "G1 interface requirement ExportKeys must be unique within the Module.",
                        nameof(typeExports));
                }
            }
        }

        var typeDefCount = GetCount(sections, 0x0013);
        var interfaceDefCount = GetCount(sections, 0x0025);
        var interfaceMethodCount = GetCount(sections, 0x0024);
        var methodCount = GetCount(sections, 0x0016);
        var signatureCount = GetCount(sections, 0x0017);
        var parameterCount = GetCount(sections, 0x0018);
        var layoutCount = GetCount(sections, 0x0015);
        var fieldCount = GetCount(sections, 0x0014);
        var contractCount = GetCount(sections, 0x001B);
        var importCount = GetCount(sections, 0x001C);
        var moduleRefCount = GetCount(sections, 0x0010);
        var sourceFileCount = GetCount(sections, 0x001F);
        var bodyCount = GetCount(sections, 0x0020);
        var nativeFunctionPointerSignatureCount
            = GetCount(sections, 0x0052);
        var constantSourceCount = GetCount(sections, 0x0030);

        var typeDefBytes = TryGetSection(sections, 0x0013,
            out var typeDefSection)
            ? typeDefSection.Bytes.ToArray().ToList() : [];
        var layoutBytes = TryGetSection(sections, 0x0015,
            out var layoutSection)
            ? layoutSection.Bytes.ToArray().ToList() : [];
        var methodBytes = TryGetSection(sections, 0x0016,
            out var methodSection)
            ? methodSection.Bytes.ToArray().ToList() : [];
        var signatureBytes = TryGetSection(sections, 0x0017,
            out var signatureSection)
            ? signatureSection.Bytes.ToArray().ToList() : [];
        var parameterBytes = TryGetSection(sections, 0x0018,
            out var parameterSection)
            ? parameterSection.Bytes.ToArray().ToList() : [];

        var typeSpecRows = ReadVariableRows(sections, 0x0011, 2, 1);
        var builtInTypeSpecRows = GetBuiltInTypeSpecRows(typeSpecRows);
        var genericParameterRows = ReadGenericParamRows(sections);
        var constraintRows = ReadConstraintRows(sections);
        var interfaceDefRows = new List<TrRmInterfaceDefRow>(
            emissions.Count);
        var interfaceMethodRows = new List<TrRmInterfaceMethodRow>();
        var methodParameterLabelBytes = new List<byte>();
        var methodParameterLabelCount = 0;
        var interfaceTypeExportNameRows = new List<int>(
            emissions.Count);
        var interfaceMethodExportNameRows = new List<int>();
        var typeDefRowBase = typeDefCount;
        var methodRowBase = methodCount;
        var signatureRowBase = signatureCount;
        var parameterRowBase = parameterCount;
        var layoutRowBase = layoutCount;
        var interfaceDefRowBase = interfaceDefCount;
        var interfaceMethodRowBase = interfaceMethodCount;
        var emittedMethods = 0;
        var emittedSignatures = 0;
        var emittedParameters = 0;

        foreach (var emission in emissions)
        {
            var definition = emission.Definition;
            var export = emission.Export;
            var typeDefRow = checked(typeDefRowBase
                + interfaceDefRows.Count + 1);
            var interfaceDefRow = checked(interfaceDefRowBase
                + interfaceDefRows.Count + 1);
            var namespaceStringRow = names.Count + 1;
            var qualifiedNameSeparator
                = definition.QualifiedName.LastIndexOf('.',
                    StringComparison.Ordinal);
            names.Add((qualifiedNameSeparator < 0 ? ""
                : definition.QualifiedName[..qualifiedNameSeparator], 1));
            var nameStringRow = names.Count + 1;
            names.Add((qualifiedNameSeparator < 0
                ? definition.QualifiedName
                : definition.QualifiedName[(qualifiedNameSeparator + 1)..],
                1));
            interfaceTypeExportNameRows.Add(names.Count + 1);
            names.Add((export.ExportKey, 1));

            var genericFirst = definition.Formals.IsEmpty
                ? 0 : checked(genericParameterRows.Count + 1);
            var constraintsByFormal
                = new List<BoundInterfaceConstraint>?[
                    definition.Formals.Length];
            foreach (var constraint in definition.Constraints)
            {
                var formalConstraints
                    = constraintsByFormal[constraint.FormalOrdinal] ??= [];
                formalConstraints.Add(constraint);
            }
            var interfaceTypeSpecRow = checked(
                (uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
                checked((uint)typeDefRow)));
            if (!exportedInterfaceTypeSpecRows.TryAdd(
                    (definition.QualifiedName, definition.Formals.Length),
                    interfaceTypeSpecRow))
            {
                throw Invalid(
                    "A G1 source interface has an ambiguous TypeSpec identity.");
            }
            var formalTypeSpecRows = new Dictionary<int, uint>();
            var formalTypeSpecArguments = new uint[
                definition.Formals.Length];
            var selfFormalRow = 0;
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var formalRow = checked(genericParameterRows.Count + 1);
                var formalNameStringRow = names.Count + 1;
                names.Add((formal.Name, 1));
                var formalConstraints = constraintsByFormal[ordinal];
                var constraintFirst = formalConstraints is null ? 0
                    : checked(constraintRows.Count + 1);
                if (formalConstraints is not null)
                {
                    foreach (var constraint in formalConstraints)
                    {
                        if (!IsGenericWriteConstraint(constraint))
                        {
                            throw Unsupported(
                                $"Interface '{definition.QualifiedName}' has an unsupported generic constraint.");
                        }

                        constraintRows.Add(new TrRmConstraintRow(
                            formalRow, 2, 2));
                    }
                }
                genericParameterRows.Add(new TrRmGenericParamRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    ordinal, formalNameStringRow, constraintFirst,
                    formalConstraints?.Count ?? 0, 1, 1, 1, 0, 0));
                var formalTypeSpecRow = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                    checked((uint)formalRow)));
                formalTypeSpecRows.Add(ordinal, formalTypeSpecRow);
                formalTypeSpecArguments[ordinal] = formalTypeSpecRow;
                if (formal.IsSelf)
                {
                    if (selfFormalRow != 0)
                    {
                        throw Unsupported(
                            $"Interface '{definition.QualifiedName}' declares multiple self formals.");
                    }
                    selfFormalRow = formalRow;
                }
            }

            var receiverTypeSpecRef = interfaceTypeSpecRow;
            if (formalTypeSpecArguments.Length != 0)
            {
                receiverTypeSpecRef = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(
                    interfaceTypeSpecRow, formalTypeSpecArguments));
            }

            var methodFirst = definition.Members.IsEmpty ? 0
                : checked(interfaceMethodRowBase
                    + interfaceMethodRows.Count + 1);
            for (var memberOrdinal = 0;
                memberOrdinal < definition.Members.Length;
                memberOrdinal++)
            {
                var member = definition.Members[memberOrdinal];
                var mapping = export.InterfaceMethodExports[
                    memberOrdinal];
                var methodNameStringRow = names.Count + 1;
                names.Add((member.Name, 1));
                interfaceMethodExportNameRows.Add(names.Count + 1);
                names.Add((mapping.ExportKey, 1));

                var signatureRow = checked(signatureRowBase
                    + emittedSignatures + 1);
                var firstParameter = member.Parameters.IsEmpty ? 0
                    : checked(parameterRowBase + emittedParameters + 1);
                var methodRow = checked(methodRowBase + emittedMethods + 1);
                if (!exportedInterfaceMethodDefRows.TryAdd(
                        (definition.QualifiedName,
                            definition.Formals.Length, member.Ordinal,
                            member.DeclarationKey),
                        checked((uint)methodRow)))
                {
                    throw Invalid(
                        "A G1L1 InterfaceMethod has an ambiguous source identity.");
                }
                for (var parameterOrdinal = 0;
                    parameterOrdinal < member.Parameters.Length;
                    parameterOrdinal++)
                {
                    var parameter = member.Parameters[parameterOrdinal];
                    if (schema.UsesMethodParameterLabels())
                    {
                        if (methodParameterLabelCount == 0xFF_FFFF)
                        {
                            throw Unsupported(
                                "The G1L1 MethodParameterLabel table exceeds its row limit.");
                        }

                        var parameterNameStringRow = names.Count + 1;
                        names.Add((parameter.Name, 1));
                        AppendUInt32(methodParameterLabelBytes,
                            checked((uint)methodRow));
                        AppendUInt32(methodParameterLabelBytes,
                            checked((uint)parameterOrdinal));
                        AppendUInt32(methodParameterLabelBytes,
                            checked((uint)parameterNameStringRow));
                        methodParameterLabelCount
                            = checked(methodParameterLabelCount + 1);
                    }
                    var parameterTypeSpecRef
                        = GenericInterfaceTypeSpecRef(parameter.Type,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows);
                    AppendUInt32(parameterBytes,
                        checked((uint)signatureRow));
                    AppendUInt32(parameterBytes,
                        checked((uint)parameterOrdinal));
                    AppendUInt32(parameterBytes, parameterTypeSpecRef);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                }
                emittedParameters += member.Parameters.Length;

                var resultTypeSpecRef = member.ResultType is null
                    ? GetBuiltInTypeSpecRef(PrimitiveType.Void,
                        builtInTypeSpecRows, typeSpecRows)
                    : GenericInterfaceTypeSpecRef(member.ResultType,
                        formalTypeSpecRows, builtInTypeSpecRows,
                        typeSpecRows);
                AppendUInt32(signatureBytes, resultTypeSpecRef);
                AppendUInt32(signatureBytes, receiverTypeSpecRef);
                AppendUInt32(signatureBytes,
                    checked((uint)firstParameter));
                AppendUInt32(signatureBytes,
                    checked((uint)member.Parameters.Length));
                AppendUInt32(signatureBytes, 0);
                AppendUInt32(signatureBytes, 0);

                AppendUInt32(methodBytes, checked((uint)typeDefRow));
                AppendUInt32(methodBytes,
                    checked((uint)methodNameStringRow));
                AppendUInt32(methodBytes, checked((uint)signatureRow));
                AppendUInt32(methodBytes, 2u
                    | (schema.UsesMethodReceiverAccess()
                        && member.RequiresMutableReceiver ? 4u : 0u));
                AppendUInt32(methodBytes, 0);
                AppendUInt32(methodBytes, 0);
                AppendUInt32(methodBytes, 0);
                interfaceMethodRows.Add(new TrRmInterfaceMethodRow(
                    interfaceDefRow, member.Ordinal, methodRow));
                emittedMethods++;
                emittedSignatures++;
            }

            var interfaceMethodCountForDefinition
                = definition.Members.Length;
            interfaceDefRows.Add(new TrRmInterfaceDefRow(typeDefRow,
                selfFormalRow, 0, 0, methodFirst,
                interfaceMethodCountForDefinition, 0, 0, 0, 0, 0, 0));

            AppendUInt32(typeDefBytes, checked((uint)namespaceStringRow));
            AppendUInt32(typeDefBytes, checked((uint)nameStringRow));
            AppendUInt32(typeDefBytes, 2u | (3u << 2));
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, checked((uint)genericFirst));
            AppendUInt32(typeDefBytes,
                checked((uint)definition.Formals.Length));
            AppendUInt32(typeDefBytes,
                checked((uint)(layoutRowBase + interfaceDefRows.Count)));
            AppendUInt32(typeDefBytes, 1);
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, 0);

            AppendUInt32(layoutBytes, checked((uint)typeDefRow));
            AppendUInt32(layoutBytes, 0);
            layoutBytes.Add(0);
            layoutBytes.Add(0);
            layoutBytes.Add(0);
            layoutBytes.Add(0);
            AppendUInt32(layoutBytes, 0);
            AppendUInt32(layoutBytes, 0);
            AppendUInt32(layoutBytes, 0);
            AppendUInt32(layoutBytes, 0);
        }

        var rewrittenStrings = TrRmStringTableWriter.EncodeTextRows(names);
        var stringTable = new TrRmStringTableReader(rewrittenStrings,
            names.Count);
        var finalTypeDefCount = checked(typeDefCount + emissions.Count);
        var finalMethodCount = checked(methodCount + emittedMethods);
        var finalSignatureCount = checked(signatureCount + emittedSignatures);
        var finalParameterCount = checked(parameterCount + emittedParameters);
        var finalLayoutCount = checked(layoutCount + emissions.Count);
        var finalGenericParamCount = genericParameterRows.Count;
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            finalTypeDefCount, importCount, finalGenericParamCount,
            nativeFunctionPointerSignatureCount,
            schema, finalMethodCount);
        var moduleSection = GetSection(sections, 0x0001);
        var modules = new TrRmModuleTableReader(moduleSection.Bytes.Span,
            checked((int)moduleSection.RecordCount), stringTable,
            moduleRefCount, finalTypeDefCount, finalMethodCount,
            sourceFileCount, GetCount(sections, 0x0004));
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes.ToArray(),
            finalTypeDefCount, stringTable, modules, fieldCount,
            finalGenericParamCount, finalLayoutCount, contractCount,
            schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, finalTypeDefCount, importCount,
            finalGenericParamCount, nativeFunctionPointerSignatureCount,
            schema, finalMethodCount);
        var finalSignatureBytes = signatureBytes.ToArray();
        var signatures = finalSignatureCount == 0 ? default
            : new TrRmMethodSigTableReader(
                finalSignatureBytes, finalSignatureCount, typeSpecs,
                finalParameterCount, finalGenericParamCount);
        var finalMethodBytes = methodBytes.ToArray();
        var methods = finalMethodCount == 0 ? default
            : new TrRmMethodDefTableReader(finalMethodBytes,
                finalMethodCount, stringTable, modules, typeDefs, signatures,
                contractCount, bodyCount, schema);

        var genericParamBytes = finalGenericParamCount == 0 ? []
            : TrRmGenericParamTableWriter.Encode(genericParameterRows,
                stringTable, typeDefs, methods, signatures, typeSpecs,
                constraintRows.Count, constantSourceCount);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamBytes, finalGenericParamCount, stringTable,
            typeDefs, methods, signatures, typeSpecs,
            constraintRows.Count, constantSourceCount);
        var constraintBytes = constraintRows.Count == 0 ? []
            : TrRmConstraintTableWriter.Encode(constraintRows,
                genericParams, typeSpecs, default);
        var finalInterfaceDefBytes = TrRmInterfaceDefTableWriter.Encode(
            interfaceDefRows, typeDefs, genericParams, 0,
            interfaceMethodRows.Count, 0, 0, 0);
        var interfaces = new TrRmInterfaceDefTableReader(
            finalInterfaceDefBytes, interfaceDefRows.Count, typeDefs,
            genericParams, 0, interfaceMethodRows.Count, 0, 0, 0);
        var interfaceMethodBytes = TrRmInterfaceMethodTableWriter.Encode(
            interfaceMethodRows, interfaces, methods, signatures);
        var targetProfileSection = TryGetSection(sections, 0x0048,
            out var targetProfilesSection) ? targetProfilesSection
            : default;
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileSection.Bytes.Span,
            checked((int)targetProfileSection.RecordCount),
            schema);
        var finalLayoutBytes = layoutBytes.ToArray();
        _ = new TrRmLayoutTableReader(finalLayoutBytes, finalLayoutCount,
            typeDefs, targetProfiles, GetCount(sections, 0x0023));
        int[] exportNameRows = [
            .. interfaceTypeExportNameRows,
            .. interfaceMethodExportNameRows,
        ];
        for (var index = 0; index < exportNameRows.Length; index++)
        {
            var owner = index < interfaceTypeExportNameRows.Count
                ? 0x1300_0000u | checked((uint)(typeDefRowBase
                    + index + 1))
                : 0u;
            if (owner == 0)
            {
                var methodExportIndex = index
                    - interfaceTypeExportNameRows.Count;
                owner = 0x1600_0000u
                    | checked((uint)(methodRowBase
                        + methodExportIndex + 1));
            }
            exportRows.Add(new TrRmExportRow(owner,
                exportNameRows[index]));
        }

        SetSection(sections, 0x0002, checked((uint)names.Count),
            rewrittenStrings);
        SetSection(sections, 0x0011,
            checked((uint)typeSpecRows.Count), typeSpecBytes);
        SetSection(sections, 0x0013,
            checked((uint)finalTypeDefCount), [.. typeDefBytes]);
        SetSection(sections, 0x0015, checked((uint)finalLayoutCount),
            finalLayoutBytes);
        SetSection(sections, 0x0016, checked((uint)finalMethodCount),
            finalMethodBytes);
        SetSection(sections, 0x0017, checked((uint)finalSignatureCount),
            finalSignatureBytes);
        SetSection(sections, 0x0018, checked((uint)finalParameterCount),
            [.. parameterBytes]);
        SetSection(sections, 0x0019,
            checked((uint)finalGenericParamCount), genericParamBytes);
        SetSection(sections, 0x001A,
            checked((uint)constraintRows.Count), constraintBytes);
        SetSection(sections, 0x0024,
            checked((uint)interfaceMethodRows.Count), interfaceMethodBytes);
        SetSection(sections, 0x0025,
            checked((uint)interfaceDefRows.Count), finalInterfaceDefBytes);
        SetSection(sections, 0x0066,
            checked((uint)methodParameterLabelCount),
            methodParameterLabelBytes.Count == 0 ? []
                : [.. methodParameterLabelBytes]);
        SetSection(sections, 0x001D, checked((uint)exportRows.Count),
            EncodeExportRows(exportRows));

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections,
            schema);
    }

    private static byte[] AddImportedGenericInterfaceMetadata(
        byte[] artifact, BoundProgram program, TrRmArtifactSchema schema)
    {
        var emissions = CaptureImportedGenericInterfaceEmissions(program,
            schema);
        if (emissions.Count == 0)
        {
            return artifact;
        }
        if (!schema.UsesMethodParameterLabels())
        {
            throw Unsupported(
                "Imported G1 interface constraints require the exact G1L1 schema.");
        }

        var artifactIndex = new TrRmArtifactIndex(artifact, schema);
        var sections = new List<TrRmSection>(artifactIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < artifactIndex.SectionCount;
            sectionIndex++)
        {
            artifactIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var stringSection = GetSection(sections, 0x0002);
        var strings = new TrRmStringTableReader(stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        var names = new List<(string Text, int Encoding)>(strings.Count
            + (emissions.Count * 4));
        for (var row = 1; row <= strings.Count; row++)
        {
            names.Add((strings.DecodeNamespace(row), 1));
        }

        var moduleRefRows = new List<TrRmModuleRefRow>(
            GetCount(sections, 0x0010) + emissions.Count);
        var moduleRefRowsByProvider = new Dictionary<string,
            (int Row, uint TargetRequirement, uint RuntimePolicy)>(
                StringComparer.Ordinal);
        if (TryGetSection(sections, 0x0010,
                out var moduleRefSection))
        {
            var count = checked((int)moduleRefSection.RecordCount);
            if (moduleRefSection.Bytes.Length != checked(count * 12))
            {
                throw Invalid(
                    "The G1L1 ModuleRef table has an invalid fixed-width extent.");
            }

            for (var row = 1; row <= count; row++)
            {
                var data = moduleRefSection.Bytes.Span.Slice(
                    (row - 1) * 12, 12);
                var providerIdentity = strings.DecodeName(
                    ReadInt(data, 0));
                var requirements = (
                    TargetRequirement: BinaryPrimitives
                        .ReadUInt32LittleEndian(data[4..]),
                    RuntimePolicy: BinaryPrimitives
                        .ReadUInt32LittleEndian(data[8..]));
                if (moduleRefRowsByProvider.TryGetValue(providerIdentity,
                        out var existing)
                    && (existing.TargetRequirement
                            != requirements.TargetRequirement
                        || existing.RuntimePolicy
                            != requirements.RuntimePolicy))
                {
                    throw Invalid(
                        "The G1L1 ModuleRef table has conflicting requirements for one provider.");
                }
                _ = moduleRefRowsByProvider.TryAdd(providerIdentity,
                    (row, requirements.TargetRequirement,
                        requirements.RuntimePolicy));

                moduleRefRows.Add(new TrRmModuleRefRow(
                    ReadInt(data, 0), requirements.TargetRequirement,
                    requirements.RuntimePolicy));
            }
        }

        var importRows = new List<TrRmImportRow>(
            GetCount(sections, 0x001C)
                + emissions.Sum(static emission =>
                    1 + emission.Definition.Members.Length));
        var importTriples = new HashSet<(
            int ModuleRefRow, string ExportKey, uint ExpectedDefinition)>();
        if (TryGetSection(sections, 0x001C, out var importSection))
        {
            var count = checked((int)importSection.RecordCount);
            if (importSection.Bytes.Length != checked(count * 12))
            {
                throw Invalid(
                    "The G1L1 Import table has an invalid fixed-width extent.");
            }

            for (var row = 1; row <= count; row++)
            {
                var data = importSection.Bytes.Span.Slice(
                    (row - 1) * 12, 12);
                var moduleRefRow = ReadInt(data, 0);
                var exportKey = strings.DecodeName(ReadInt(data, 4));
                var expectedDefinition
                    = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                if (!importTriples.Add((moduleRefRow, exportKey,
                        expectedDefinition)))
                {
                    throw Invalid(
                        "The G1L1 Import table contains a duplicate expectation.");
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    ReadInt(data, 4), expectedDefinition));
            }
        }

        int GetModuleRefRow(BoundModuleImport import)
        {
            if (moduleRefRowsByProvider.TryGetValue(
                    import.ProducerIdentity, out var existing))
            {
                if (existing.TargetRequirement != import.TargetRequirement
                    || existing.RuntimePolicy != import.RuntimePolicy)
                {
                    throw Unsupported(
                        "An imported interface and an existing ModuleRef disagree on provider requirements.");
                }

                return existing.Row;
            }

            var providerStringRow = names.Count + 1;
            names.Add((import.ProducerIdentity, 1));
            var moduleRefRow = moduleRefRows.Count + 1;
            moduleRefRows.Add(new TrRmModuleRefRow(providerStringRow,
                import.TargetRequirement, import.RuntimePolicy));
            moduleRefRowsByProvider.Add(import.ProducerIdentity,
                (moduleRefRow, import.TargetRequirement,
                    import.RuntimePolicy));
            return moduleRefRow;
        }

        var typeDefCount = GetCount(sections, 0x0013);
        var interfaceDefCount = GetCount(sections, 0x0025);
        var interfaceMethodCount = GetCount(sections, 0x0024);
        var methodCount = GetCount(sections, 0x0016);
        var signatureCount = GetCount(sections, 0x0017);
        var parameterCount = GetCount(sections, 0x0018);
        var layoutCount = GetCount(sections, 0x0015);
        var fieldCount = GetCount(sections, 0x0014);
        var contractCount = GetCount(sections, 0x001B);
        var sourceFileCount = GetCount(sections, 0x001F);
        var featureCount = GetCount(sections, 0x0004);
        var nativeFunctionPointerSignatureCount
            = GetCount(sections, 0x0052);

        var typeDefBytes = TryGetSection(sections, 0x0013,
            out var typeDefSection)
            ? typeDefSection.Bytes.ToArray().ToList() : [];
        var layoutBytes = TryGetSection(sections, 0x0015,
            out var layoutSection)
            ? layoutSection.Bytes.ToArray().ToList() : [];
        var methodBytes = TryGetSection(sections, 0x0016,
            out var methodSection)
            ? methodSection.Bytes.ToArray().ToList() : [];
        var signatureBytes = TryGetSection(sections, 0x0017,
            out var signatureSection)
            ? signatureSection.Bytes.ToArray().ToList() : [];
        var parameterBytes = TryGetSection(sections, 0x0018,
            out var parameterSection)
            ? parameterSection.Bytes.ToArray().ToList() : [];
        var methodParameterLabelCount = GetCount(sections, 0x0066);
        var methodParameterLabelBytes = TryGetSection(sections, 0x0066,
            out var methodParameterLabelSection)
            ? methodParameterLabelSection.Bytes.ToArray().ToList() : [];
        if (methodParameterLabelBytes.Count
            != checked(methodParameterLabelCount * 12))
        {
            throw Invalid(
                "The G1L1 MethodParameterLabel table has an invalid fixed-width extent.");
        }

        var typeSpecRows = ReadVariableRows(sections, 0x0011, 2, 1);
        var builtInTypeSpecRows = GetBuiltInTypeSpecRows(typeSpecRows);
        var genericParameterRows = ReadGenericParamRows(sections);
        var constraintRows = ReadConstraintRows(sections);
        var interfaceDefRows = new List<TrRmInterfaceDefRow>(
            emissions.Count);
        var interfaceMethodRows = new List<TrRmInterfaceMethodRow>();
        var emissionOrdinal = 0;
        var emittedMethods = 0;
        var emittedSignatures = 0;
        var emittedParameters = 0;

        foreach (var emission in emissions)
        {
            var definition = emission.Definition;
            var typeDefRow = checked(typeDefCount + emissionOrdinal + 1);
            var interfaceDefRow = checked(interfaceDefCount
                + emissionOrdinal + 1);
            var moduleRefRow = GetModuleRefRow(emission.Import);
            var namespaceStringRow = names.Count + 1;
            var separator = definition.QualifiedName.LastIndexOf('.',
                StringComparison.Ordinal);
            names.Add((separator < 0 ? ""
                : definition.QualifiedName[..separator], 1));
            var nameStringRow = names.Count + 1;
            names.Add((separator < 0 ? definition.QualifiedName
                : definition.QualifiedName[(separator + 1)..], 1));
            var typeExportKeyStringRow = names.Count + 1;
            names.Add((emission.Import.ExportKey, 1));
            var typeDefExpectedId = 0x1300_0000u
                | checked((uint)typeDefRow);
            if (!importTriples.Add((moduleRefRow,
                    emission.Import.ExportKey, typeDefExpectedId)))
            {
                throw Invalid(
                    "The G1L1 imported interface TypeDef duplicates an existing Import expectation.");
            }

            importRows.Add(new TrRmImportRow(moduleRefRow,
                typeExportKeyStringRow, typeDefExpectedId));

            var genericFirst = definition.Formals.IsEmpty ? 0
                : checked(genericParameterRows.Count + 1);
            var constraintsByFormal
                = new List<BoundInterfaceConstraint>?[
                    definition.Formals.Length];
            foreach (var constraint in definition.Constraints)
            {
                var formalConstraints
                    = constraintsByFormal[constraint.FormalOrdinal] ??= [];
                formalConstraints.Add(constraint);
            }

            var interfaceTypeSpecRow = checked(
                (uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
                checked((uint)typeDefRow)));
            var formalTypeSpecRows = new Dictionary<int, uint>();
            var formalTypeSpecArguments = new uint[
                definition.Formals.Length];
            var selfFormalRow = 0;
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var formalRow = checked(genericParameterRows.Count + 1);
                var formalNameStringRow = names.Count + 1;
                names.Add((formal.Name, 1));
                var formalConstraints = constraintsByFormal[ordinal];
                var constraintFirst = formalConstraints is null ? 0
                    : checked(constraintRows.Count + 1);
                if (formalConstraints is not null)
                {
                    foreach (var constraint in formalConstraints)
                    {
                        if (!IsGenericWriteConstraint(constraint))
                        {
                            throw Unsupported(
                                $"Imported interface '{definition.QualifiedName}' has an unsupported generic constraint.");
                        }

                        constraintRows.Add(new TrRmConstraintRow(
                            formalRow, 2, 2));
                    }
                }
                genericParameterRows.Add(new TrRmGenericParamRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    ordinal, formalNameStringRow, constraintFirst,
                    formalConstraints?.Count ?? 0, 1, 1, 1, 0, 0));
                var formalTypeSpecRow = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                    checked((uint)formalRow)));
                formalTypeSpecRows.Add(ordinal, formalTypeSpecRow);
                formalTypeSpecArguments[ordinal] = formalTypeSpecRow;
                if (formal.IsSelf)
                {
                    if (selfFormalRow != 0)
                    {
                        throw Unsupported(
                            $"Imported interface '{definition.QualifiedName}' declares multiple self formals.");
                    }
                    selfFormalRow = formalRow;
                }
            }

            var receiverTypeSpecRef = interfaceTypeSpecRow;
            if (formalTypeSpecArguments.Length != 0)
            {
                receiverTypeSpecRef = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(
                    interfaceTypeSpecRow, formalTypeSpecArguments));
            }

            var methodFirst = definition.Members.IsEmpty ? 0
                : checked(interfaceMethodCount
                    + interfaceMethodRows.Count + 1);
            foreach (var member in definition.Members)
            {
                var methodNameStringRow = names.Count + 1;
                names.Add((member.Name, 1));
                var methodExportKeyStringRow = names.Count + 1;
                names.Add((member.DeclarationKey, 1));
                var signatureRow = checked(signatureCount
                    + emittedSignatures + 1);
                var firstParameter = member.Parameters.IsEmpty ? 0
                    : checked(parameterCount + emittedParameters + 1);
                var methodRow = checked(methodCount + emittedMethods + 1);
                var methodExpectedId = 0x1600_0000u
                    | checked((uint)methodRow);
                if (!importTriples.Add((moduleRefRow,
                        member.DeclarationKey, methodExpectedId)))
                {
                    throw Invalid(
                        "The G1L1 imported InterfaceMethod duplicates an existing Import expectation.");
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    methodExportKeyStringRow, methodExpectedId));
                for (var parameterOrdinal = 0;
                    parameterOrdinal < member.Parameters.Length;
                    parameterOrdinal++)
                {
                    if (methodParameterLabelCount == 0xFF_FFFF)
                    {
                        throw Unsupported(
                            "The G1L1 MethodParameterLabel table exceeds its row limit.");
                    }

                    var parameter = member.Parameters[parameterOrdinal];
                    var parameterNameStringRow = names.Count + 1;
                    names.Add((parameter.Name, 1));
                    AppendUInt32(methodParameterLabelBytes,
                        checked((uint)methodRow));
                    AppendUInt32(methodParameterLabelBytes,
                        checked((uint)parameterOrdinal));
                    AppendUInt32(methodParameterLabelBytes,
                        checked((uint)parameterNameStringRow));
                    methodParameterLabelCount
                        = checked(methodParameterLabelCount + 1);
                    var parameterTypeSpecRef
                        = GenericInterfaceTypeSpecRef(parameter.Type,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows);
                    AppendUInt32(parameterBytes,
                        checked((uint)signatureRow));
                    AppendUInt32(parameterBytes,
                        checked((uint)parameterOrdinal));
                    AppendUInt32(parameterBytes, parameterTypeSpecRef);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                }
                emittedParameters += member.Parameters.Length;

                var resultTypeSpecRef = member.ResultType is null
                    ? GetBuiltInTypeSpecRef(PrimitiveType.Void,
                        builtInTypeSpecRows, typeSpecRows)
                    : GenericInterfaceTypeSpecRef(member.ResultType,
                        formalTypeSpecRows, builtInTypeSpecRows,
                        typeSpecRows);
                AppendUInt32(signatureBytes, resultTypeSpecRef);
                AppendUInt32(signatureBytes, receiverTypeSpecRef);
                AppendUInt32(signatureBytes,
                    checked((uint)firstParameter));
                AppendUInt32(signatureBytes,
                    checked((uint)member.Parameters.Length));
                AppendUInt32(signatureBytes, 0);
                AppendUInt32(signatureBytes, 0);

                AppendUInt32(methodBytes, checked((uint)typeDefRow));
                AppendUInt32(methodBytes,
                    checked((uint)methodNameStringRow));
                AppendUInt32(methodBytes, checked((uint)signatureRow));
                AppendUInt32(methodBytes, 2u
                    | (schema.UsesMethodReceiverAccess()
                        && member.RequiresMutableReceiver ? 4u : 0u));
                AppendUInt32(methodBytes, 0);
                AppendUInt32(methodBytes, 0);
                AppendUInt32(methodBytes, 0);
                interfaceMethodRows.Add(new TrRmInterfaceMethodRow(
                    interfaceDefRow, member.Ordinal, methodRow));
                emittedMethods++;
                emittedSignatures++;
            }

            interfaceDefRows.Add(new TrRmInterfaceDefRow(typeDefRow,
                selfFormalRow, 0, 0, methodFirst,
                definition.Members.Length, 0, 0, 0, 0, 0, 0));

            AppendUInt32(typeDefBytes, checked((uint)namespaceStringRow));
            AppendUInt32(typeDefBytes, checked((uint)nameStringRow));
            AppendUInt32(typeDefBytes, 2u | (3u << 2));
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, checked((uint)genericFirst));
            AppendUInt32(typeDefBytes,
                checked((uint)definition.Formals.Length));
            AppendUInt32(typeDefBytes,
                checked((uint)(layoutCount + emissionOrdinal + 1)));
            AppendUInt32(typeDefBytes, 1);
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, 0);

            AppendUInt32(layoutBytes, checked((uint)typeDefRow));
            AppendUInt32(layoutBytes, 0);
            layoutBytes.Add(0);
            layoutBytes.Add(0);
            layoutBytes.Add(0);
            layoutBytes.Add(0);
            AppendUInt32(layoutBytes, 0);
            AppendUInt32(layoutBytes, 0);
            AppendUInt32(layoutBytes, 0);
            AppendUInt32(layoutBytes, 0);

            emissionOrdinal++;
        }

        var rewrittenStrings = TrRmStringTableWriter.EncodeTextRows(names);
        var stringTable = new TrRmStringTableReader(rewrittenStrings,
            names.Count);
        var finalTypeDefCount = checked(typeDefCount + emissions.Count);
        var finalMethodCount = checked(methodCount + emittedMethods);
        var finalSignatureCount = checked(signatureCount + emittedSignatures);
        var finalParameterCount = checked(parameterCount + emittedParameters);
        var finalLayoutCount = checked(layoutCount + emissions.Count);
        var finalGenericParameterCount = genericParameterRows.Count;
        var moduleSection = GetSection(sections, 0x0001);
        var modules = new TrRmModuleTableReader(moduleSection.Bytes.Span,
            checked((int)moduleSection.RecordCount), stringTable,
            moduleRefRows.Count, finalTypeDefCount, finalMethodCount,
            sourceFileCount, featureCount);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            stringTable, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            moduleRefRows.Count, stringTable, modules);
        var finalTypeDefBytes = typeDefBytes.ToArray();
        var typeDefs = new TrRmTypeDefTableReader(finalTypeDefBytes,
            finalTypeDefCount, stringTable, modules, fieldCount,
            finalGenericParameterCount, finalLayoutCount, contractCount,
            schema);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            finalTypeDefCount, importRows.Count,
            finalGenericParameterCount,
            nativeFunctionPointerSignatureCount, schema, finalMethodCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, finalTypeDefCount, importRows.Count,
            finalGenericParameterCount,
            nativeFunctionPointerSignatureCount, schema, finalMethodCount);
        var finalSignatureBytes = signatureBytes.ToArray();
        var signatures = finalSignatureCount == 0 ? default
            : new TrRmMethodSigTableReader(finalSignatureBytes,
                finalSignatureCount, typeSpecs, finalParameterCount,
                finalGenericParameterCount);
        var finalMethodBytes = methodBytes.ToArray();
        var methods = finalMethodCount == 0 ? default
            : new TrRmMethodDefTableReader(finalMethodBytes,
                finalMethodCount, stringTable, modules, typeDefs, signatures,
                contractCount, GetCount(sections, 0x0020), schema);
        var genericParameterBytes = genericParameterRows.Count == 0 ? []
            : TrRmGenericParamTableWriter.Encode(genericParameterRows,
                stringTable, typeDefs, methods, signatures, typeSpecs,
                constraintRows.Count, GetCount(sections, 0x0030));
        var genericParams = new TrRmGenericParamTableReader(
            genericParameterBytes, genericParameterRows.Count,
            stringTable, typeDefs, methods, signatures, typeSpecs,
            constraintRows.Count, GetCount(sections, 0x0030));
        var constraintBytes = constraintRows.Count == 0 ? []
            : TrRmConstraintTableWriter.Encode(constraintRows,
                genericParams, typeSpecs, default);
        var finalInterfaceMethodCount = checked(interfaceMethodCount
            + interfaceMethodRows.Count);
        var interfaceBaseCount = GetCount(sections, 0x0026);
        var associatedTypeCount = GetCount(sections, 0x0027);
        var associatedConstCount = GetCount(sections, 0x0028);
        var interfaceConstCount = GetCount(sections, 0x0029);
        var newInterfaceDefBytes = TrRmInterfaceDefTableWriter.Encode(
            interfaceDefRows, typeDefs, genericParams, interfaceBaseCount,
            finalInterfaceMethodCount, associatedTypeCount,
            associatedConstCount, interfaceConstCount);
        var existingInterfaceDefBytes = TryGetSection(sections, 0x0025,
            out var existingInterfaceDefSection)
            ? existingInterfaceDefSection.Bytes.ToArray() : [];
        var finalInterfaceDefBytes = new byte[
            existingInterfaceDefBytes.Length + newInterfaceDefBytes.Length];
        existingInterfaceDefBytes.CopyTo(finalInterfaceDefBytes, 0);
        newInterfaceDefBytes.CopyTo(finalInterfaceDefBytes,
            existingInterfaceDefBytes.Length);
        var interfaces = new TrRmInterfaceDefTableReader(
            finalInterfaceDefBytes,
            checked(interfaceDefCount + interfaceDefRows.Count),
            typeDefs, genericParams, interfaceBaseCount,
            finalInterfaceMethodCount, associatedTypeCount,
            associatedConstCount, interfaceConstCount);
        var newInterfaceMethodBytes
            = interfaceMethodRows.Count == 0 ? []
                : TrRmInterfaceMethodTableWriter.Encode(
                    interfaceMethodRows, interfaces, methods, signatures);
        var existingInterfaceMethodBytes = TryGetSection(sections, 0x0024,
            out var existingInterfaceMethodSection)
            ? existingInterfaceMethodSection.Bytes.ToArray() : [];
        var finalInterfaceMethodBytes = new byte[
            existingInterfaceMethodBytes.Length
                + newInterfaceMethodBytes.Length];
        existingInterfaceMethodBytes.CopyTo(finalInterfaceMethodBytes, 0);
        newInterfaceMethodBytes.CopyTo(finalInterfaceMethodBytes,
            existingInterfaceMethodBytes.Length);
        var fields = fieldCount == 0 ? default
            : new TrRmFieldDefTableReader(
                GetSection(sections, 0x0014).Bytes.Span, fieldCount,
                stringTable, typeDefs, typeSpecs);
        var finalImportBytes = TrRmImportTableWriter.Encode(importRows,
            stringTable, moduleRefs, typeDefs, methods, default, interfaces,
            default, fields, default);

        SetSection(sections, 0x0002, checked((uint)names.Count),
            rewrittenStrings);
        SetSection(sections, 0x0010, checked((uint)moduleRefRows.Count),
            moduleRefBytes);
        SetSection(sections, 0x0011,
            checked((uint)typeSpecRows.Count), typeSpecBytes);
        SetSection(sections, 0x0013, checked((uint)finalTypeDefCount),
            finalTypeDefBytes);
        SetSection(sections, 0x0015, checked((uint)finalLayoutCount),
            layoutBytes.ToArray());
        SetSection(sections, 0x0016, checked((uint)finalMethodCount),
            finalMethodBytes);
        SetSection(sections, 0x0017, checked((uint)finalSignatureCount),
            finalSignatureBytes);
        SetSection(sections, 0x0018, checked((uint)finalParameterCount),
            parameterBytes.ToArray());
        SetSection(sections, 0x0019,
            checked((uint)finalGenericParameterCount),
            genericParameterBytes);
        SetSection(sections, 0x001A,
            checked((uint)constraintRows.Count), constraintBytes);
        SetSection(sections, 0x001C, checked((uint)importRows.Count),
            finalImportBytes);
        SetSection(sections, 0x0024,
            checked((uint)finalInterfaceMethodCount),
            finalInterfaceMethodBytes);
        SetSection(sections, 0x0025,
            checked((uint)(interfaceDefCount + interfaceDefRows.Count)),
            finalInterfaceDefBytes);
        SetSection(sections, 0x0066,
            checked((uint)methodParameterLabelCount),
            methodParameterLabelBytes.Count == 0 ? []
                : [.. methodParameterLabelBytes]);

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static List<GenericImportedInterfaceEmission>
        CaptureImportedGenericInterfaceEmissions(BoundProgram program,
            TrRmArtifactSchema schema)
    {
        var required = new Dictionary<(
            string ProviderIdentity, string ExportKey, string QualifiedName,
            int Arity, uint TargetRequirement, uint RuntimePolicy),
            BoundModuleImport>();
        var remainingVisits = SemanticVerifier.MaximumAnalysisVisits;
        void Capture(BoundInterfaceType type, int depth)
        {
            if (type is null || depth > SemanticVerifier.MaximumDepth
                || --remainingVisits < 0)
            {
                throw Unsupported(
                    "Imported G1 interface constraints exceed the supported analysis limit.");
            }

            if (type.ModuleImport is BoundModuleImport moduleImport)
            {
                if (type.Kind != BoundInterfaceTypeKind.Nominal
                    || string.IsNullOrEmpty(type.NominalName)
                    || string.IsNullOrEmpty(moduleImport.ProducerIdentity)
                    || string.IsNullOrEmpty(moduleImport.ExportKey)
                    || moduleImport.ExportKey.Contains('\0',
                        StringComparison.Ordinal)
                    || !IsValidSourceGenericMethodExportKey(
                        moduleImport.ExportKey))
                {
                    throw Unsupported(
                        "An imported G1 interface constraint has no nominal provider identity.");
                }

                var arity = type.TypeArguments.IsDefault
                    ? 0 : type.TypeArguments.Length;
                _ = required.TryAdd((moduleImport.ProducerIdentity,
                    moduleImport.ExportKey, type.NominalName, arity,
                    moduleImport.TargetRequirement,
                    moduleImport.RuntimePolicy), moduleImport);
            }

            if (type.TypeArguments.IsDefaultOrEmpty)
            {
                return;
            }

            foreach (var argument in type.TypeArguments)
            {
                if (argument is null)
                {
                    throw Unsupported(
                        "An imported G1 interface constraint has an invalid type argument.");
                }

                if (argument.Kind == BoundInterfaceTypeArgumentKind.Type
                    && argument.Type is BoundInterfaceType argumentType)
                {
                    Capture(argumentType, depth + 1);
                }
            }
        }

        foreach (var definition in program.GenericMethodDefinitions)
        {
            if (definition.Formals.IsDefault)
            {
                throw Unsupported(
                    "A generic method has an invalid interface-constraint view.");
            }

            foreach (var formal in definition.Formals)
            {
                if (formal.InterfaceConstraints.IsDefault)
                {
                    throw Unsupported(
                        "A generic method has an invalid interface-constraint view.");
                }

                foreach (var constraint in formal.InterfaceConstraints)
                {
                    if (constraint is null)
                    {
                        throw Unsupported(
                            "A generic method has an invalid interface-constraint view.");
                    }

                    Capture(constraint, 0);
                }
            }
        }

        var result = new List<GenericImportedInterfaceEmission>(
            required.Count);
        foreach (var (key, import) in required.OrderBy(
                     static pair => pair.Key.ProviderIdentity,
                     StringComparer.Ordinal).ThenBy(
                     static pair => pair.Key.ExportKey,
                     StringComparer.Ordinal).ThenBy(
                     static pair => pair.Key.QualifiedName,
                     StringComparer.Ordinal).ThenBy(
                     static pair => pair.Key.Arity))
        {
            var matches = program.Interfaces.Where(definition =>
                definition.ModuleImport == import
                && StringComparer.Ordinal.Equals(definition.QualifiedName,
                    key.QualifiedName)
                && definition.Formals.Length == key.Arity).ToArray();
            if (matches.Length != 1)
            {
                throw Unsupported(
                    "An imported G1 interface constraint has no unique retained provider declaration.");
            }

            ValidateImportedGenericInterface(matches[0], schema);
            var definitionVisits = checked(1
                + matches[0].Formals.Length
                + matches[0].Members.Length
                + matches[0].Members.Sum(
                    static member => member.Parameters.Length));
            if (definitionVisits > remainingVisits)
            {
                throw Unsupported(
                    "Imported G1 interface constraints exceed the supported analysis limit.");
            }
            remainingVisits -= definitionVisits;
            result.Add(new GenericImportedInterfaceEmission(matches[0],
                import));
        }

        if (result.Count > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "Imported G1 interface constraints exceed the supported analysis limit.");
        }

        return result;
    }

    private static void ValidateImportedGenericInterface(
        BoundInterfaceDefinition definition, TrRmArtifactSchema schema)
    {
        if (!definition.IsPublic
            || definition.Accessibility
                != BoundInterfaceAccessibility.Public
            || definition.ModuleImport is not BoundModuleImport import
            || string.IsNullOrEmpty(definition.QualifiedName)
            || !definition.Bases.IsDefaultOrEmpty
            || !definition.AttributeTokens.IsDefaultOrEmpty
            || definition.ForeignNativeInterface is not null
            || definition.Formals.IsDefault
            || definition.Formals.Length > SemanticVerifier.MaximumArguments
            || definition.Constraints.IsDefault
            || definition.Members.IsDefault
            || definition.Members.Length
                > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                $"Imported interface '{definition.QualifiedName}' is outside the G1L1 declaration subset.");
        }
        if (string.IsNullOrEmpty(import.ProducerIdentity)
            || string.IsNullOrEmpty(import.ExportKey)
            || import.ExportKey.Contains('\0', StringComparison.Ordinal)
            || !IsValidSourceGenericMethodExportKey(import.ExportKey))
        {
            throw Unsupported(
                $"Imported interface '{definition.QualifiedName}' has an invalid provider identity.");
        }

        var constraintsByFormal
            = new List<BoundInterfaceConstraint>?[definition.Formals.Length];
        foreach (var constraint in definition.Constraints)
        {
            if (constraint is null
                || !IsGenericWriteConstraint(constraint)
                || (uint)constraint.FormalOrdinal
                    >= (uint)definition.Formals.Length)
            {
                throw Unsupported(
                    $"Imported interface '{definition.QualifiedName}' has an unsupported generic constraint.");
            }

            var formalConstraints
                = constraintsByFormal[constraint.FormalOrdinal] ??= [];
            formalConstraints.Add(constraint);
        }

        var selfFormalCount = 0;
        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            var formal = definition.Formals[ordinal];
            var formalConstraints = constraintsByFormal[ordinal];
            if (formal is null
                || formal.Ordinal != ordinal
                || formal.Kind != BoundInterfaceFormalKind.Type
                || string.IsNullOrEmpty(formal.Name)
                || formal.ConstantType is not null
                || formal.TypeDefault is not null
                || formal.ConstantDefault is not null
                || !formal.Attributes.IsDefaultOrEmpty
                || !formal.AttributeTokens.IsDefaultOrEmpty
                || formal.RequiresWritable
                    != (formalConstraints is { Count: > 0 })
                || formalConstraints is { Count: > 1 })
            {
                throw Unsupported(
                    $"Imported interface '{definition.QualifiedName}' has an unsupported owner formal.");
            }

            if (formal.IsSelf)
            {
                selfFormalCount++;
            }
        }
        if (selfFormalCount > 1)
        {
            throw Unsupported(
                $"Imported interface '{definition.QualifiedName}' declares multiple self formals.");
        }

        var memberExportKeys = new HashSet<string>(StringComparer.Ordinal);
        var typeExportKey = definition.ModuleImport.ExportKey;
        for (var ordinal = 0; ordinal < definition.Members.Length; ordinal++)
        {
            var member = definition.Members[ordinal];
            if (member is null)
            {
                throw Unsupported(
                    $"Imported interface '{definition.QualifiedName}' has an invalid member.");
            }

            if (member.Ordinal != ordinal
                || member.Kind != BoundInterfaceMemberKind.Callable
                || member.Accessibility
                    != BoundInterfaceAccessibility.Public
                || string.IsNullOrEmpty(member.Name)
                || string.IsNullOrEmpty(member.DeclarationKey)
                || !IsValidSourceGenericMethodExportKey(
                    member.DeclarationKey)
                || member.DeclarationKey.Contains('\0',
                    StringComparison.Ordinal)
                || StringComparer.Ordinal.Equals(member.DeclarationKey,
                    typeExportKey)
                || !memberExportKeys.Add(member.DeclarationKey)
                || member.IsStatic || member.IsExternal
                || (member.RequiresMutableReceiver
                    && !schema.UsesMethodReceiverAccess())
                || member.IsPartial || member.RequiresUnsafeCaller
                || member.DeclaredRequiresUnsafeCaller
                || member.DoesNotReturn || member.IsAsync
                || member.HasBody
                || member.NativeCallableConvention is not null
                || !member.Attributes.IsDefaultOrEmpty
                || !member.AttributeTokens.IsDefaultOrEmpty
                || !member.ContractTokens.IsDefaultOrEmpty
                || !member.TypeParameters.IsDefaultOrEmpty
                || !member.Constraints.IsDefaultOrEmpty
                || !member.Dependencies.IsDefaultOrEmpty
                || !member.Accessors.IsDefaultOrEmpty
                || member.AssociatedDefaultType is not null
                || member.AssociatedConstantType is not null
                || member.AssociatedConstantDefault is not null
                || member.Parameters.IsDefault
                || member.Parameters.Length
                    > SemanticVerifier.MaximumArguments
                || member.Parameters.Any(parameter =>
                    parameter is null
                    || string.IsNullOrEmpty(parameter.Name)
                    || parameter.Mode != BoundInterfaceParameterMode.Value
                    || !parameter.Attributes.IsDefaultOrEmpty
                    || !parameter.AttributeTokens.IsDefaultOrEmpty
                    || parameter.DefaultValue is not null
                    || parameter.DefaultExpression is not null
                    || parameter.HasPendingDefault
                    || !IsSupportedGenericInterfaceSignatureType(
                        parameter.Type, definition.Formals.Length,
                        isReturn: false))
                || (member.ResultType is not null
                    && !IsSupportedGenericInterfaceSignatureType(
                        member.ResultType, definition.Formals.Length,
                        isReturn: true)))
            {
                throw Unsupported(
                    $"Imported interface requirement '{definition.QualifiedName}.{member.Name}' is outside the G1L1 scalar/value signature subset.");
            }
        }
    }

    private static List<GenericInterfaceEmission>
        CaptureGenericInterfaceEmissions(BoundProgram program,
            IReadOnlyList<TrRmSourceTypeExport> typeExports,
            TrRmArtifactSchema schema)
    {
        var result = new List<GenericInterfaceEmission>(typeExports.Count);
        var selectors = new HashSet<(string Namespace, string Name, int Arity)>();
        foreach (var export in typeExports)
        {
            if (export.GenericArity < 0
                || !export.EnumMemberDeclarationKeys.IsDefault
                || !export.FieldExports.IsDefaultOrEmpty
                || export.InterfaceMethodExports.IsDefault
                    || export.Namespace is null
                    || export.Namespace.Contains('\0',
                        StringComparison.Ordinal)
                || string.IsNullOrEmpty(export.Name)
                || string.IsNullOrEmpty(export.ExportKey)
                || !IsValidSourceGenericMethodExportKey(export.ExportKey)
                || export.ExportKey.Contains('\0', StringComparison.Ordinal)
                || !selectors.Add((export.Namespace, export.Name,
                    export.GenericArity)))
            {
                throw new ArgumentException(
                    "G1 TypeDef mappings require unique interface selectors, nonnegative arity, and valid interface ExportKeys.",
                    nameof(typeExports));
            }

            var matching = program.Interfaces.Where(definition =>
                StringComparer.Ordinal.Equals(
                    GetQualifiedInterfaceNamespace(definition.QualifiedName),
                    export.Namespace)
                && StringComparer.Ordinal.Equals(
                    GetQualifiedInterfaceName(definition.QualifiedName),
                    export.Name)
                && definition.Formals.Length == export.GenericArity
                && definition.ModuleImport is null).ToArray();
            if (matching.Length != 1 || !matching[0].IsPublic)
            {
                throw new ArgumentException(
                    "A G1 TypeDef mapping must select one public local source interface of the exact name and arity.",
                    nameof(typeExports));
            }

            var definition = matching[0];
            if (!definition.Bases.IsEmpty
                || definition.Constraints.IsDefault
                || definition.Constraints.Any(constraint =>
                    constraint is null
                    || !IsGenericWriteConstraint(constraint)
                    || (uint)constraint.FormalOrdinal
                        >= (uint)definition.Formals.Length
                    || definition.Formals[constraint.FormalOrdinal].Kind
                        != BoundInterfaceFormalKind.Type)
                || !definition.AttributeTokens.IsDefaultOrEmpty
                || definition.ForeignNativeInterface is not null
                || definition.Formals.IsDefault
                || definition.Formals.Length
                    > SemanticVerifier.MaximumArguments
                || definition.Members.IsDefault
                || definition.Formals.Any(static formal =>
                    formal.Kind != BoundInterfaceFormalKind.Type
                    || formal.ConstantType is not null
                    || formal.TypeDefault is not null
                    || formal.ConstantDefault is not null
                    || !formal.Attributes.IsDefaultOrEmpty
                    || !formal.AttributeTokens.IsDefaultOrEmpty))
            {
                throw Unsupported(
                    $"Interface '{definition.QualifiedName}' is outside the G1 type-only, unconstrained declaration subset.");
            }
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                if (definition.Formals[ordinal].Ordinal != ordinal)
                {
                    throw new InvalidDataException(
                        "A bound interface formal range is not contiguous.");
                }
            }
            if (export.InterfaceMethodExports.Length
                != definition.Members.Length)
            {
                throw new ArgumentException(
                    "Every directly declared interface requirement needs one source-anchored ExportKey.",
                    nameof(typeExports));
            }
            for (var ordinal = 0; ordinal < definition.Members.Length;
                ordinal++)
            {
                var member = definition.Members[ordinal];
                var memberExport = export.InterfaceMethodExports[ordinal];
                if (member.Ordinal != ordinal
                    || member.SourceInputOrdinal
                        != memberExport.SourceInputOrdinal
                    || member.SourceDeclarationSpan
                        != memberExport.DeclarationSpan
                    || memberExport.SourceInputOrdinal < 0
                    || memberExport.SourceInputOrdinal
                        >= program.SourceInputCount
                    || memberExport.DeclarationSpan.Start < 0
                    || memberExport.DeclarationSpan.Length <= 0
                    || (long)memberExport.DeclarationSpan.Start
                        + memberExport.DeclarationSpan.Length > int.MaxValue
                    || string.IsNullOrEmpty(memberExport.ExportKey)
                    || !IsValidSourceGenericMethodExportKey(
                        memberExport.ExportKey)
                    || memberExport.ExportKey.Contains('\0',
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "An InterfaceMethod mapping must select the exact directly declared requirement by source input and full declaration span.",
                        nameof(typeExports));
                }

                if (member.Kind != BoundInterfaceMemberKind.Callable
                    || member.Accessibility
                        != BoundInterfaceAccessibility.Public
                    || member.IsStatic || member.IsExternal
                    || (member.RequiresMutableReceiver
                        && !schema.UsesMethodReceiverAccess())
                    || member.IsPartial || member.RequiresUnsafeCaller
                    || member.DeclaredRequiresUnsafeCaller
                    || member.DoesNotReturn || member.IsAsync
                    || member.HasBody
                    || member.NativeCallableConvention is not null
                    || !member.Attributes.IsDefaultOrEmpty
                    || !member.AttributeTokens.IsDefaultOrEmpty
                    || !member.ContractTokens.IsDefaultOrEmpty
                    || !member.TypeParameters.IsDefaultOrEmpty
                    || !member.Constraints.IsDefaultOrEmpty
                    || !member.Dependencies.IsDefaultOrEmpty
                    || !member.Accessors.IsDefaultOrEmpty
                    || member.AssociatedDefaultType is not null
                    || member.AssociatedConstantType is not null
                    || member.AssociatedConstantDefault is not null
                    || member.Parameters.IsDefault
                    || member.Parameters.Length
                        > SemanticVerifier.MaximumArguments
                    || member.Parameters.Any(parameter =>
                        parameter.Mode != BoundInterfaceParameterMode.Value
                        || !parameter.Attributes.IsDefaultOrEmpty
                        || !parameter.AttributeTokens.IsDefaultOrEmpty
                        || !IsSupportedGenericInterfaceSignatureType(
                            parameter.Type, definition.Formals.Length,
                            isReturn: false))
                    || (member.ResultType is not null
                        && !IsSupportedGenericInterfaceSignatureType(
                            member.ResultType, definition.Formals.Length,
                            isReturn: true)))
                {
                    throw Unsupported(
                        $"Interface requirement '{definition.QualifiedName}.{member.Name}' is outside the G1 bodyless scalar/value signature subset.");
                }
            }

            result.Add(new GenericInterfaceEmission(definition, export));
        }

        return result;
    }

    private static bool IsGenericWriteConstraint(
        BoundInterfaceConstraint constraint) =>
        constraint.Kind == BoundInterfaceConstraintKind.Write
            && constraint.Type is null
            && constraint.AttributeName is null
            && constraint.FormalScope
                == BoundInterfaceTypeParameterScope.Interface;

    private static string GetQualifiedInterfaceNamespace(
        string qualifiedName)
    {
        var separator = qualifiedName.LastIndexOf('.',
            StringComparison.Ordinal);
        return separator < 0 ? "" : qualifiedName[..separator];
    }

    private static string GetQualifiedInterfaceName(string qualifiedName)
    {
        var separator = qualifiedName.LastIndexOf('.',
            StringComparison.Ordinal);
        return separator < 0 ? qualifiedName
            : qualifiedName[(separator + 1)..];
    }

    private static bool IsSupportedGenericInterfaceSignatureType(
        BoundInterfaceType type, int formalCount, bool isReturn)
    {
        if (type.IsWritable || type.IsOptional
            || type.IsGarbageCollected || type.IsInitialization
            || type.ModuleImport is not null
            || !type.TypeArguments.IsDefaultOrEmpty)
        {
            return false;
        }

        return type.Kind switch {
            BoundInterfaceTypeKind.Primitive
                => Array.IndexOf(s_types, type.PrimitiveType) >= 0
                    && (isReturn
                        || type.PrimitiveType != PrimitiveType.Void),
            BoundInterfaceTypeKind.TypeParameter
                => type.TypeParameterScope
                        == BoundInterfaceTypeParameterScope.Interface
                    && type.TypeParameterOrdinal >= 0
                    && type.TypeParameterOrdinal < formalCount,
            _ => false,
        };
    }

    private static uint GenericInterfaceTypeSpecRef(
        BoundInterfaceType type,
        Dictionary<int, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows)
        => type.Kind switch {
            BoundInterfaceTypeKind.Primitive
                => GetBuiltInTypeSpecRef(type.PrimitiveType,
                    builtInTypeSpecRows, typeSpecRows),
            BoundInterfaceTypeKind.TypeParameter
                when type.TypeParameterScope
                    == BoundInterfaceTypeParameterScope.Interface
                    && formalTypeSpecRows.TryGetValue(
                        type.TypeParameterOrdinal, out var formalTypeSpec)
                    => formalTypeSpec,
            _ => throw Unsupported(
                "A G1 interface requirement TypeSpec is outside the selected scalar and owner-formal subset."),
        };

    private static uint GenericMethodInterfaceConstraintTypeSpecRef(
        BoundInterfaceType type, BoundGenericMethodDefinition method,
        IReadOnlyDictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        IReadOnlyDictionary<(string ProviderIdentity, string ExportKey,
            string QualifiedName, int Arity, uint TargetRequirement,
            uint RuntimePolicy), uint>
            importedInterfaceImportRows,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        int[] genericParamRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows, int depth,
        ref int remainingVisits)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || --remainingVisits < 0
            || (type.IsWritable
                && type.Kind != BoundInterfaceTypeKind.TypeParameter)
            || type.IsOptional
            || type.IsGarbageCollected || type.IsInitialization
            || type.IsGenericNominalConstantDomain
            || type.GenericNominalTypeIdentity is not null
            || type.SourceGenericTypeDefinitionIndex != -1)
        {
            throw Unsupported(
                "A G1 interface constraint exceeds the supported local type subset.");
        }

        switch (type.Kind)
        {
            case BoundInterfaceTypeKind.Primitive:
                if (type.PrimitiveType == PrimitiveType.Void
                    || Array.IndexOf(s_types, type.PrimitiveType) < 0
                    || type.NominalName is not null
                    || type.TypeParameterOrdinal != -1
                    || type.TypeParameterScope
                        != BoundInterfaceTypeParameterScope.None
                    || !type.TypeArguments.IsDefaultOrEmpty)
                {
                    throw Unsupported(
                        "A G1 interface constraint uses an unsupported primitive type.");
                }

                return GetBuiltInTypeSpecRef(type.PrimitiveType,
                    builtInTypeSpecRows, typeSpecRows);

            case BoundInterfaceTypeKind.TypeParameter:
                if (type.TypeParameterScope
                        != BoundInterfaceTypeParameterScope.Member
                    || (uint)type.TypeParameterOrdinal
                        >= (uint)method.Formals.Length
                    || type.PrimitiveType != PrimitiveType.Void
                    || type.NominalName is not null
                    || !type.TypeArguments.IsDefaultOrEmpty
                    || !formalTypeSpecRows.TryGetValue(
                        method.Formals[type.TypeParameterOrdinal]
                            .PlaceholderType, out var formalTypeSpecRow))
                {
                    throw Unsupported(
                        "A G1 interface constraint references an unsupported method formal.");
                }

                if (!type.IsWritable)
                {
                    return formalTypeSpecRow;
                }

                var writableFormalTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                    checked((uint)genericParamRows[
                        type.TypeParameterOrdinal]), 0x01));
                return writableFormalTypeSpecRow;

            case BoundInterfaceTypeKind.Nominal:
                if (string.IsNullOrEmpty(type.NominalName)
                    || type.TypeParameterOrdinal != -1
                    || type.PrimitiveType != PrimitiveType.Void
                    || type.TypeParameterScope
                        != BoundInterfaceTypeParameterScope.None)
                {
                    throw Unsupported(
                        "A G1 interface constraint has no selected local interface declaration.");
                }

                var argumentCount = type.TypeArguments.IsDefault
                    ? 0 : type.TypeArguments.Length;
                uint interfaceTypeSpecRow;
                if (type.ModuleImport is BoundModuleImport moduleImport)
                {
                    if (!importedInterfaceImportRows.TryGetValue(
                            (moduleImport.ProducerIdentity,
                                moduleImport.ExportKey, type.NominalName,
                                argumentCount, moduleImport.TargetRequirement,
                                moduleImport.RuntimePolicy),
                            out var importRow))
                    {
                        throw Unsupported(
                            "A G1 interface constraint has no exact expected interface Import.");
                    }

                    interfaceTypeSpecRow
                        = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x03,
                        importRow));
                }
                else if (!exportedInterfaceTypeSpecRows.TryGetValue(
                             (type.NominalName, argumentCount),
                             out interfaceTypeSpecRow))
                {
                    throw Unsupported(
                        "A G1 interface constraint has no selected local interface declaration.");
                }

                if (argumentCount == 0)
                {
                    return interfaceTypeSpecRow;
                }

                var arguments = new uint[argumentCount];
                for (var ordinal = 0; ordinal < argumentCount; ordinal++)
                {
                    var argument = type.TypeArguments[ordinal];
                    if (argument.Kind != BoundInterfaceTypeArgumentKind.Type
                        || argument.Type is not BoundInterfaceType
                            argumentType
                        || argument.ConstantType is not null
                        || argument.ConstantValue is not null
                        || argument.ConstantParameterScope
                            != BoundInterfaceTypeParameterScope.None
                        || argument.ConstantParameterOrdinal != -1)
                    {
                        throw Unsupported(
                            "A G1 interface constraint has an unsupported interface argument.");
                    }

                    arguments[ordinal]
                        = GenericMethodInterfaceConstraintTypeSpecRef(
                            argumentType, method,
                            exportedInterfaceTypeSpecRows,
                            importedInterfaceImportRows, formalTypeSpecRows,
                            genericParamRows, builtInTypeSpecRows, typeSpecRows,
                            depth + 1,
                            ref remainingVisits);
                }

                var constructedTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(
                    interfaceTypeSpecRow, arguments));
                return constructedTypeSpecRow;

            default:
                throw Unsupported(
                    "A G1 interface constraint uses an unsupported type constructor.");
        }
    }

    private static Dictionary<(string ProviderIdentity, string ExportKey,
        string QualifiedName, int Arity, uint TargetRequirement,
        uint RuntimePolicy), uint> ReadImportedInterfaceImportRows(
        IReadOnlyList<TrRmSection> sections,
        TrRmStringTableReader strings)
    {
        var result = new Dictionary<(string ProviderIdentity,
            string ExportKey, string QualifiedName, int Arity,
            uint TargetRequirement, uint RuntimePolicy), uint>();
        if (!TryGetSection(sections, 0x001C, out var importSection))
        {
            return result;
        }

        if (!TryGetSection(sections, 0x0010, out var moduleRefSection)
            || !TryGetSection(sections, 0x0013, out var typeDefSection))
        {
            throw Invalid(
                "The generic method source artifact has Imports without ModuleRefs or TypeDefs.");
        }

        var importCount = checked((int)importSection.RecordCount);
        var moduleRefCount = checked((int)moduleRefSection.RecordCount);
        var typeDefCount = checked((int)typeDefSection.RecordCount);
        if (importSection.Bytes.Length != importCount * 12
            || moduleRefSection.Bytes.Length != moduleRefCount * 12
            || typeDefSection.Bytes.Length != typeDefCount * 44)
        {
            throw Invalid(
                "A G1 interface Import source table has an invalid extent.");
        }

        for (var importRow = 1; importRow <= importCount; importRow++)
        {
            var import = importSection.Bytes.Span.Slice(
                (importRow - 1) * 12, 12);
            var moduleRefRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(import));
            var exportKeyRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(import[4..]));
            var expectedDefinition
                = BinaryPrimitives.ReadUInt32LittleEndian(import[8..]);
            if ((expectedDefinition >> 24) != 0x13)
            {
                continue;
            }

            var typeDefRow = checked((int)(expectedDefinition
                & 0x00FF_FFFF));
            if (typeDefRow <= 0 || typeDefRow > typeDefCount
                || moduleRefRow <= 0 || moduleRefRow > moduleRefCount)
            {
                throw Invalid(
                    "A G1 interface Import has an invalid expected TypeDef or ModuleRef.");
            }

            var typeDef = typeDefSection.Bytes.Span.Slice(
                (typeDefRow - 1) * 44, 44);
            var typeDefFlags = BinaryPrimitives.ReadUInt32LittleEndian(
                typeDef[8..]);
            if (((typeDefFlags >> 2) & 7) != 3)
            {
                continue;
            }

            var namespaceRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(typeDef));
            var nameRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(typeDef[4..]));
            var genericArity = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(typeDef[24..]));
            var namespaceName = strings.DecodeNamespace(namespaceRow);
            var name = strings.DecodeName(nameRow);
            var qualifiedName = namespaceName.Length == 0 ? name
                : namespaceName + "." + name;

            var moduleRef = moduleRefSection.Bytes.Span.Slice(
                (moduleRefRow - 1) * 12, 12);
            var providerStringRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(moduleRef));
            var providerIdentity = strings.DecodeName(providerStringRow);
            var targetRequirement = BinaryPrimitives
                .ReadUInt32LittleEndian(moduleRef[4..]);
            var runtimePolicy = BinaryPrimitives
                .ReadUInt32LittleEndian(moduleRef[8..]);
            var exportKey = strings.DecodeName(exportKeyRow);
            var key = (providerIdentity, exportKey, qualifiedName,
                genericArity, targetRequirement, runtimePolicy);
            if (!result.TryAdd(key, checked((uint)importRow)))
            {
                throw Unsupported(
                    "A G1 interface constraint has ambiguous expected Import views.");
            }
        }

        return result;
    }

    private static Dictionary<GenericImportedInterfaceMethodKey, uint>
        ReadImportedInterfaceMethodImportRows(
            IReadOnlyList<TrRmSection> sections,
            TrRmStringTableReader strings)
    {
        var result
            = new Dictionary<GenericImportedInterfaceMethodKey, uint>();
        if (!TryGetSection(sections, 0x001C, out var importSection)
            || !TryGetSection(sections, 0x0025, out var interfaceDefSection)
            || !TryGetSection(sections, 0x0024,
                out var interfaceMethodSection))
        {
            return result;
        }

        if (!TryGetSection(sections, 0x0010, out var moduleRefSection)
            || !TryGetSection(sections, 0x0013, out var typeDefSection)
            || !TryGetSection(sections, 0x0016, out var methodSection))
        {
            throw Invalid(
                "The G1L1 interface MethodDef Imports have incomplete declaration tables.");
        }

        var importCount = checked((int)importSection.RecordCount);
        var moduleRefCount = checked((int)moduleRefSection.RecordCount);
        var typeDefCount = checked((int)typeDefSection.RecordCount);
        var methodCount = checked((int)methodSection.RecordCount);
        var interfaceDefCount
            = checked((int)interfaceDefSection.RecordCount);
        var interfaceMethodCount
            = checked((int)interfaceMethodSection.RecordCount);
        if (importSection.Bytes.Length != checked(importCount * 12)
            || moduleRefSection.Bytes.Length
                != checked(moduleRefCount * 12)
            || typeDefSection.Bytes.Length != checked(typeDefCount * 44)
            || methodSection.Bytes.Length != checked(methodCount * 28)
            || interfaceDefSection.Bytes.Length
                != checked(interfaceDefCount * 48)
            || interfaceMethodSection.Bytes.Length
                != checked(interfaceMethodCount * 12))
        {
            throw Invalid(
                "A G1L1 interface MethodDef Import source table has an invalid extent.");
        }

        var typeImports = new Dictionary<int, (
            string ProviderIdentity, string ExportKey, string QualifiedName,
            int Arity, uint TargetRequirement, uint RuntimePolicy)>();
        for (var importRow = 1; importRow <= importCount; importRow++)
        {
            var import = importSection.Bytes.Span.Slice(
                (importRow - 1) * 12, 12);
            var expectedDefinition
                = BinaryPrimitives.ReadUInt32LittleEndian(import[8..]);
            if ((expectedDefinition >> 24) != 0x13)
            {
                continue;
            }

            var typeDefRow = checked((int)(expectedDefinition
                & 0x00FF_FFFF));
            if (typeDefRow <= 0 || typeDefRow > typeDefCount)
            {
                throw Invalid(
                    "A G1L1 interface MethodDef Import has an invalid expected TypeDef.");
            }

            var typeDef = typeDefSection.Bytes.Span.Slice(
                (typeDefRow - 1) * 44, 44);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(
                typeDef[8..]);
            if (((flags >> 2) & 7) != 3)
            {
                continue;
            }

            var moduleRefRow = ReadInt(import, 0);
            if (moduleRefRow <= 0 || moduleRefRow > moduleRefCount)
            {
                throw Invalid(
                    "A G1L1 interface TypeDef Import has an invalid ModuleRef.");
            }

            var moduleRef = moduleRefSection.Bytes.Span.Slice(
                (moduleRefRow - 1) * 12, 12);
            var providerIdentity = strings.DecodeName(
                ReadInt(moduleRef, 0));
            var namespaceName = strings.DecodeNamespace(
                ReadInt(typeDef, 0));
            var name = strings.DecodeName(ReadInt(typeDef, 4));
            var qualifiedName = namespaceName.Length == 0 ? name
                : namespaceName + "." + name;
            var typeImport = (
                ProviderIdentity: providerIdentity,
                ExportKey: strings.DecodeName(ReadInt(import, 4)),
                QualifiedName: qualifiedName,
                Arity: ReadInt(typeDef, 24),
                TargetRequirement: BinaryPrimitives
                    .ReadUInt32LittleEndian(moduleRef[4..]),
                RuntimePolicy: BinaryPrimitives
                    .ReadUInt32LittleEndian(moduleRef[8..]));
            if (!typeImports.TryAdd(typeDefRow, typeImport))
            {
                throw Unsupported(
                    "A G1L1 imported interface TypeDef has ambiguous expected Import views.");
            }
        }

        var interfaceDefByTypeDef = new Dictionary<int, int>(
            interfaceDefCount);
        for (var interfaceDefRow = 1;
            interfaceDefRow <= interfaceDefCount; interfaceDefRow++)
        {
            var interfaceDef = interfaceDefSection.Bytes.Span.Slice(
                (interfaceDefRow - 1) * 48, 48);
            var typeDefRow = ReadInt(interfaceDef, 0);
            if (!interfaceDefByTypeDef.TryAdd(typeDefRow,
                    interfaceDefRow))
            {
                throw Invalid(
                    "A G1L1 InterfaceDef has an ambiguous TypeDef owner.");
            }
        }

        var interfaceMethodByMethod = new Dictionary<int,
            (int InterfaceDefRow, int Ordinal)>(interfaceMethodCount);
        for (var interfaceMethodRow = 1;
            interfaceMethodRow <= interfaceMethodCount;
            interfaceMethodRow++)
        {
            var interfaceMethod = interfaceMethodSection.Bytes.Span.Slice(
                (interfaceMethodRow - 1) * 12, 12);
            var methodRow = ReadInt(interfaceMethod, 8);
            if (!interfaceMethodByMethod.TryAdd(methodRow,
                    (ReadInt(interfaceMethod, 0),
                        ReadInt(interfaceMethod, 4))))
            {
                throw Invalid(
                    "An InterfaceMethod MethodDef has an ambiguous imported member identity.");
            }
        }

        for (var importRow = 1; importRow <= importCount; importRow++)
        {
            var import = importSection.Bytes.Span.Slice(
                (importRow - 1) * 12, 12);
            var expectedDefinition
                = BinaryPrimitives.ReadUInt32LittleEndian(import[8..]);
            if ((expectedDefinition >> 24) != 0x16)
            {
                continue;
            }

            var methodRow = checked((int)(expectedDefinition
                & 0x00FF_FFFF));
            if (methodRow <= 0 || methodRow > methodCount)
            {
                throw Invalid(
                    "A G1L1 interface MethodDef Import has an invalid expected MethodDef.");
            }

            var method = methodSection.Bytes.Span.Slice(
                (methodRow - 1) * 28, 28);
            var typeDefRow = ReadInt(method, 0);
            if (!typeImports.TryGetValue(typeDefRow,
                    out var typeImport))
            {
                continue;
            }
            if (!interfaceDefByTypeDef.TryGetValue(typeDefRow,
                    out var interfaceDefRow)
                || !interfaceMethodByMethod.TryGetValue(methodRow,
                    out var interfaceMethod)
                || interfaceMethod.InterfaceDefRow != interfaceDefRow)
            {
                throw Invalid(
                    "An imported interface MethodDef has no direct InterfaceMethod owner.");
            }

            var moduleRefRow = ReadInt(import, 0);
            if (moduleRefRow <= 0 || moduleRefRow > moduleRefCount)
            {
                throw Invalid(
                    "An imported interface MethodDef has an invalid ModuleRef.");
            }
            var moduleRef = moduleRefSection.Bytes.Span.Slice(
                (moduleRefRow - 1) * 12, 12);
            var providerIdentity
                = strings.DecodeName(ReadInt(moduleRef, 0));
            var targetRequirement
                = BinaryPrimitives.ReadUInt32LittleEndian(moduleRef[4..]);
            var runtimePolicy
                = BinaryPrimitives.ReadUInt32LittleEndian(moduleRef[8..]);
            if (!StringComparer.Ordinal.Equals(providerIdentity,
                    typeImport.ProviderIdentity)
                || targetRequirement != typeImport.TargetRequirement
                || runtimePolicy != typeImport.RuntimePolicy)
            {
                throw Invalid(
                    "An imported interface MethodDef selects a different provider than its TypeDef.");
            }

            var key = new GenericImportedInterfaceMethodKey(
                typeImport.ProviderIdentity, typeImport.ExportKey,
                typeImport.QualifiedName, typeImport.Arity,
                interfaceMethod.Ordinal,
                strings.DecodeName(ReadInt(import, 4)),
                typeImport.TargetRequirement, typeImport.RuntimePolicy);
            if (!result.TryAdd(key, checked((uint)importRow)))
            {
                throw Unsupported(
                    "A G1L1 imported InterfaceMethod has an ambiguous expected Import.");
            }
        }

        return result;
    }

    private static (byte[] GenericParams, byte[] Constraints)
        CreateGenericMetadataBytes(byte[] stringBytes,
        int stringCount, byte[] moduleBytes, byte[] methodBytes,
        byte[] signatureBytes, byte[] typeSpecBytes, int typeSpecCount,
        List<TrRmGenericParamRow> genericParameterRows,
        List<TrRmConstraintRow> constraintRows,
        IReadOnlyList<TrRmSection> sections, int typeDefCount, int importCount,
        int nativeFunctionPointerSignatureCount, int featureCount,
        int signatureCount, int parameterCount, int contractCount,
        int bodyCount, int moduleRefCount, TrRmArtifactSchema schema)
    {
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount, typeDefCount, methodBytes.Length / 28, 0,
            featureCount);
        TrRmTypeDefTableReader types = default;
        if (typeDefCount != 0)
        {
            var typeDefSection = GetSection(sections, 0x0013);
            types = new TrRmTypeDefTableReader(typeDefSection.Bytes.Span,
                typeDefCount, strings, modules, GetCount(sections, 0x0014),
                genericParameterRows.Count, GetCount(sections, 0x0015),
                contractCount, schema);
        }
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecCount, typeDefCount, importCount,
            genericParameterRows.Count,
            nativeFunctionPointerSignatureCount,
            schema,
            methodBytes.Length / 28);
        var signatures = signatureCount == 0 ? default
            : new TrRmMethodSigTableReader(signatureBytes,
                signatureCount, typeSpecs, parameterCount,
                genericParameterRows.Count);
        var methodCount = methodBytes.Length / 28;
        var methods = methodCount == 0 ? default
            : new TrRmMethodDefTableReader(methodBytes, methodCount, strings,
                modules, types, signatures, contractCount, bodyCount, schema);
        var genericParamBytes = genericParameterRows.Count == 0 ? []
            : TrRmGenericParamTableWriter.Encode(genericParameterRows,
                strings, types, methods, signatures, typeSpecs,
                constraintRows.Count, GetCount(sections, 0x0030));
        var genericParams = new TrRmGenericParamTableReader(
            genericParamBytes, genericParameterRows.Count, strings, types,
            methods, signatures, typeSpecs, constraintRows.Count,
            GetCount(sections, 0x0030));
        var constraintBytes = constraintRows.Count == 0 ? []
            : TrRmConstraintTableWriter.Encode(constraintRows,
                genericParams, typeSpecs, default);
        return (genericParamBytes, constraintBytes);
    }

    private static uint GenericMethodTypeDefaultRef(
        BoundGenericMethodTypeDefault typeDefault, int formalOrdinal,
        BoundGenericMethodDefinition definition,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        if (BoundGenericMethodTypeTerms.ContainsKind(typeDefault.Term,
                BoundGenericMethodTypeTermKind.ImportedGeneric))
        {
            if (!typeDefault.ConstantFormalDependencies.IsEmpty
                || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    typeDefault.Term, out var dependencies)
                || !dependencies.SequenceEqual(typeDefault.FormalDependencies)
                || dependencies.Any(dependency =>
                    dependency < 0 || dependency >= formalOrdinal))
            {
                throw Unsupported(
                    "A G1 nominal type default has inconsistent earlier-formal dependencies.");
            }
            return nominalTypes.TypeTermRef(typeDefault.Term, definition,
                formalTypeSpecRows, builtInTypeSpecRows, 0);
        }
        if (typeDefault.Term.Kind
            == BoundGenericMethodTypeTermKind.Span)
        {
            if (!typeDefault.ConstantFormalDependencies.IsEmpty
                || !typeDefault.Term.ConstantArguments.IsEmpty
                || typeDefault.Term.IsGarbageCollected
                || typeDefault.Term.IsOptional
                || typeDefault.Term.AllowsVoid
                || typeDefault.Term.ElementCount != 0
                || typeDefault.Term.CallingConvention
                    != NativeCallingConvention.Native
                || typeDefault.Term.ParameterCount != 0
                || typeDefault.Term.NominalTypeIdentity is not null
                || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    typeDefault.Term, out var dependencies)
                || !dependencies.SequenceEqual(
                    typeDefault.FormalDependencies)
                || dependencies.Any(dependency =>
                    dependency < 0 || dependency >= formalOrdinal))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a Span type default outside the earlier-formal subset.");
            }

            return GenericMethodTypeDefaultSpecRef(typeDefault.Term,
                formalOrdinal, definition, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows);
        }

        if (typeDefault.Term.Kind
            == BoundGenericMethodTypeTermKind.CheckedReference)
        {
            if (!typeDefault.ConstantFormalDependencies.IsEmpty
                || typeDefault.Term.TypeArguments.Length != 1
                || typeDefault.Term.IsGarbageCollected
                || typeDefault.Term.IsOptional
                || typeDefault.Term.AllowsVoid
                || typeDefault.Term.IsElementWritable
                || typeDefault.Term.ElementCount != 0
                || typeDefault.Term.CallingConvention
                    != NativeCallingConvention.Native
                || typeDefault.Term.ParameterCount != 0
                || typeDefault.Term.NominalTypeIdentity is not null
                || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    typeDefault.Term, out var dependencies)
                || !dependencies.SequenceEqual(
                    typeDefault.FormalDependencies)
                || dependencies.Any(dependency =>
                    dependency < 0 || dependency >= formalOrdinal))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a checked-reference type default outside the earlier-formal subset.");
            }

            return GenericMethodTypeDefaultSpecRef(typeDefault.Term,
                formalOrdinal, definition, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows);
        }

        var redundantWritableFormal
            = typeDefault.Term.Kind
                    == BoundGenericMethodTypeTermKind.Formal
                && (uint)typeDefault.Term.FormalOrdinal
                    < (uint)definition.Formals.Length
                && definition.Formals[typeDefault.Term.FormalOrdinal]
                    .RequiresWritable;
        if (!typeDefault.ConstantFormalDependencies.IsEmpty
            || typeDefault.Term.TypeArguments.IsDefaultOrEmpty
                == false
            || !typeDefault.Term.ConstantArguments.IsEmpty
            || (typeDefault.Term.IsWritable
                && !redundantWritableFormal)
            || typeDefault.Term.IsGarbageCollected
            || typeDefault.Term.IsInitialization
            || typeDefault.Term.IsOptional
            || typeDefault.Term.ElementCount != 0
            || typeDefault.Term.CallingConvention
                != NativeCallingConvention.Native
            || typeDefault.Term.ParameterCount != 0
            || typeDefault.Term.AllowsVoid
            || typeDefault.Term.IsElementWritable
            || typeDefault.Term.NominalTypeIdentity is not null)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a type default outside the G1 direct-type subset.");
        }

        if (typeDefault.Term.Kind
            == BoundGenericMethodTypeTermKind.Formal)
        {
            var dependency = typeDefault.Term.FormalOrdinal;
            if (dependency < 0 || dependency >= formalOrdinal
                || !typeDefault.FormalDependencies.SequenceEqual(
                    [dependency]))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a non-earlier or inconsistent type default.");
            }

            var dependencyType = definition.Formals[dependency]
                .PlaceholderType;
            return formalTypeSpecRows.TryGetValue(dependencyType,
                    out var typeSpecRef)
                ? typeSpecRef
                : throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a type default before its formal.");
        }

        if (typeDefault.Term.Kind
            == BoundGenericMethodTypeTermKind.Concrete
            && typeDefault.FormalDependencies.IsEmpty)
        {
            return GetBuiltInTypeSpecRef(typeDefault.Term.ConcreteType,
                builtInTypeSpecRows, typeSpecRows);
        }

        throw Unsupported(
            $"Generic MethodDef '{definition.Name}' has a type default outside the G1 direct-type subset.");
    }

    private static uint GenericMethodTypeDefaultSpecRef(
        BoundGenericMethodTypeTerm term, int formalOrdinal,
        BoundGenericMethodDefinition definition,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        if (term.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            if (term.FormalOrdinal < 0
                || term.IsElementWritable
                || term.FormalOrdinal >= formalOrdinal
                || (uint)term.FormalOrdinal
                    >= (uint)definition.Formals.Length
                || (term.IsWritable
                    && !definition.Formals[term.FormalOrdinal]
                        .RequiresWritable)
                || !formalTypeSpecRows.TryGetValue(
                    definition.Formals[term.FormalOrdinal]
                        .PlaceholderType, out var formalTypeSpecRef))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a Span default that does not reference an earlier formal.");
            }
            return formalTypeSpecRef;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.CheckedReference)
        {
            if (term.TypeArguments.Length != 1
                || term.IsGarbageCollected || term.IsOptional
                || term.AllowsVoid || term.IsElementWritable
                || term.ElementCount != 0
                || term.CallingConvention
                    != NativeCallingConvention.Native
                || term.ParameterCount != 0
                || term.NominalTypeIdentity is not null
                || term.TypeArguments[0].AllowsVoid
                || term.TypeArguments[0].IsOptional)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a checked-reference type default outside the selected TypeSpec family.");
            }

            var referentTerm = term.TypeArguments[0];
            uint referentTypeSpecRef;
            if (referentTerm.Kind
                == BoundGenericMethodTypeTermKind.Formal)
            {
                var dependency = referentTerm.FormalOrdinal;
                if (dependency < 0 || dependency >= formalOrdinal
                    || (uint)dependency
                        >= (uint)definition.Formals.Length
                    || !formalTypeSpecRows.TryGetValue(
                        definition.Formals[dependency].PlaceholderType,
                        out referentTypeSpecRef))
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has a checked-reference default that does not reference an earlier formal.");
                }
                if (referentTerm.IsWritable)
                {
                    if (!definition.Formals[dependency]
                            .RequiresWritable)
                    {
                        throw Unsupported(
                            $"Generic MethodDef '{definition.Name}' has a write-qualified referent without the formal's write requirement.");
                    }
                }
            }
            else if (referentTerm.Kind
                == BoundGenericMethodTypeTermKind.Concrete)
            {
                if (referentTerm.ConcreteType == PrimitiveType.Void)
                {
                    throw Unsupported(
                        "A G1 checked-reference referent cannot be void.");
                }
                referentTypeSpecRef = GenericMethodTypeSpecRef(
                    referentTerm.ConcreteType, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows,
                    definition.ProofProgram.SpanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true,
                    allowFormal: false);
            }
            else
            {
                referentTypeSpecRef = GenericMethodTypeDefaultSpecRef(
                    referentTerm, formalOrdinal, definition,
                    formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows);
            }

            var typeSpecRef = checked((uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(term.IsInitialization
                ? TrRmTypeSpecTableWriter.InitializationReference(
                    referentTypeSpecRef, term.IsWritable)
                : TrRmTypeSpecTableWriter.Wrapped(0x07,
                    referentTypeSpecRef,
                    term.IsWritable ? (byte)0x01 : (byte)0));
            return typeSpecRef;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            if (term.AllowsVoid || term.IsElementWritable)
            {
                throw Unsupported(
                    "A G1 Span element type cannot be void.");
            }
            return GenericMethodTypeSpecRef(term.ConcreteType,
                formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                definition.ProofProgram.SpanTypes,
                definition.ProofProgram.CheckedReferenceTypes,
                definition.DefinitionOrdinal, definition,
                allowSpanType: true);
        }

        if (term.Kind != BoundGenericMethodTypeTermKind.Span
            || term.TypeArguments.Length != 1
            || term.IsGarbageCollected || term.IsOptional
            || term.AllowsVoid || term.ElementCount != 0
            || term.CallingConvention != NativeCallingConvention.Native
            || term.ParameterCount != 0
            || term.NominalTypeIdentity is not null
            || (term.TypeArguments[0].Kind
                    == BoundGenericMethodTypeTermKind.Concrete
                && (term.TypeArguments[0].ConcreteType
                        == PrimitiveType.Void
                    || term.TypeArguments[0].AllowsVoid))
            || (term.IsElementWritable
                && term.TypeArguments[0].Kind
                    != BoundGenericMethodTypeTermKind.Formal))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a nested Span default outside the selected TypeSpec family.");
        }

        var elementQualifiers = (byte)(
            (term.IsElementWritable ? 0x01 : 0)
            | (term.IsInitialization ? 0x04 : 0));
        var elementTypeSpecRef = GenericMethodSpanArgumentTypeSpecRef(
            term.TypeArguments[0], elementQualifiers, formalOrdinal,
            definition, formalTypeSpecRows, builtInTypeSpecRows,
            typeSpecRows);
        var typeSpecRef = checked((uint)typeSpecRows.Count + 1);
        typeSpecRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8A,
            elementTypeSpecRef, term.IsWritable ? (byte)0x01 : (byte)0));
        return typeSpecRef;
    }

    private static uint GenericMethodSpanArgumentTypeSpecRef(
        BoundGenericMethodTypeTerm elementTerm, byte qualifiers,
        int formalOrdinal, BoundGenericMethodDefinition definition,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        if ((qualifiers & 0x01) != 0)
        {
            if (elementTerm.Kind
                    != BoundGenericMethodTypeTermKind.Formal
                || (uint)elementTerm.FormalOrdinal
                    >= (uint)definition.Formals.Length
                || !definition.Formals[elementTerm.FormalOrdinal]
                    .RequiresWritable)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a writable Span element without the formal's write requirement.");
            }

        }

        if (qualifiers == 0
            && elementTerm.Kind
                == BoundGenericMethodTypeTermKind.Concrete
            && !formalTypeSpecRows.ContainsKey(elementTerm.ConcreteType)
            && !BoundSpanTypeTypes.IsValidType(
                elementTerm.ConcreteType,
                definition.ProofProgram.SpanTypes))
        {
            var builtinIndex = Array.IndexOf(s_types,
                elementTerm.ConcreteType);
            if (builtinIndex >= 0)
            {
                return 0xF200_0000u | s_codes[builtinIndex];
            }
        }

        uint typeSpecRef;
        if (elementTerm.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            typeSpecRef = GenericMethodTypeDefaultSpecRef(elementTerm,
                formalOrdinal, definition, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows);
        }
        else if (elementTerm.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            if (elementTerm.AllowsVoid
                || elementTerm.ConcreteType == PrimitiveType.Void)
            {
                throw Unsupported("A G1 Span element cannot be void.");
            }
            typeSpecRef = GenericMethodTypeSpecRef(
                elementTerm.ConcreteType, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows,
                definition.ProofProgram.SpanTypes,
                definition.ProofProgram.CheckedReferenceTypes,
                definition.DefinitionOrdinal, definition,
                allowSpanType: true,
                allowFormal: false);
        }
        else if (elementTerm.Kind == BoundGenericMethodTypeTermKind.Span)
        {
            typeSpecRef = GenericMethodTypeDefaultSpecRef(elementTerm,
                formalOrdinal, definition, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows);
        }
        else
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a Span default element outside the selected type terms.");
        }

        return qualifiers == 0
            ? typeSpecRef
            : AddGenericTypeSpecQualifiers(typeSpecRef, qualifiers,
                typeSpecRows);
    }

    private static List<TrRmExportRow> ReadExportRows(
        IReadOnlyList<TrRmSection> sections,
        HashSet<uint> removedDefinitions)
    {
        var rows = new List<TrRmExportRow>();
        if (!TryGetSection(sections, 0x001D, out var section))
        {
            return rows;
        }

        for (var index = 0; index < section.Bytes.Length / 8; index++)
        {
            var offset = index * 8;
            var localDefId = BinaryPrimitives.ReadUInt32LittleEndian(
                section.Bytes.Span.Slice(offset, 4));
            if (!removedDefinitions.Contains(localDefId))
            {
                rows.Add(new TrRmExportRow(localDefId,
                    ReadInt(section.Bytes.Span, offset + 4)));
            }
        }

        return rows;
    }

    private static List<TrRmGenericParamRow> ReadGenericParamRows(
        IReadOnlyList<TrRmSection> sections)
    {
        var count = GetCount(sections, 0x0019);
        if (count == 0)
        {
            return [];
        }

        var section = GetSection(sections, 0x0019);
        if (section.Bytes.Length != checked(count * 32))
        {
            throw new InvalidDataException(
                "The GenericParam table has an invalid fixed-width extent.");
        }

        var rows = new List<TrRmGenericParamRow>(count);
        for (var index = 0; index < count; index++)
        {
            var data = section.Bytes.Span.Slice(index * 32, 32);
            if (data[23] != 0)
            {
                throw new InvalidDataException(
                    "The GenericParam table contains a nonzero reserved byte.");
            }

            rows.Add(new TrRmGenericParamRow(
                BinaryPrimitives.ReadUInt32LittleEndian(data),
                ReadInt(data, 4), ReadInt(data, 8), ReadInt(data, 12),
                ReadInt(data, 16), data[20], data[21], data[22],
                BinaryPrimitives.ReadUInt32LittleEndian(data[24..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[28..])));
        }

        return rows;
    }

    private static List<TrRmConstraintRow> ReadConstraintRows(
        IReadOnlyList<TrRmSection> sections)
    {
        var count = GetCount(sections, 0x001A);
        if (count == 0)
        {
            return [];
        }

        var section = GetSection(sections, 0x001A);
        if (section.Bytes.Length != checked(count * 12))
        {
            throw new InvalidDataException(
                "The Constraint table has an invalid fixed-width extent.");
        }

        var rows = new List<TrRmConstraintRow>(count);
        for (var index = 0; index < count; index++)
        {
            var data = section.Bytes.Span.Slice(index * 12, 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0)
            {
                throw new InvalidDataException(
                    "The Constraint table contains nonzero reserved flags.");
            }

            rows.Add(new TrRmConstraintRow(ReadInt(data, 0),
                BinaryPrimitives.ReadUInt16LittleEndian(data[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[8..])));
        }

        return rows;
    }

    private static List<ReadOnlyMemory<byte>> ReadVariableRows(
        IReadOnlyList<TrRmSection> sections, ushort kind,
        int minimumRowBytes, int rowAlignment)
    {
        if (!TryGetSection(sections, kind, out var section))
        {
            return [];
        }

        var index = new TrRmVariableTableIndex(section.Bytes.Span,
            checked((int)section.RecordCount), minimumRowBytes,
            rowAlignment);
        var rows = new List<ReadOnlyMemory<byte>>(index.Count);
        for (var row = 1; row <= index.Count; row++)
        {
            rows.Add(index.GetRow(row).ToArray());
        }
        return rows;
    }

    private static ReadOnlyMemory<byte> EncodeGenericMethodEffectContract(
        int methodRow, ReadOnlyMemory<byte> effectMask)
    {
        if (effectMask.Length != sizeof(uint))
        {
            throw new InvalidOperationException(
                "The generic method effect payload has an invalid extent.");
        }

        var row = new byte[8 + effectMask.Length];
        WriteUInt32(row, 0, 0x1600_0000u | checked((uint)methodRow));
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4, 2), 1);
        effectMask.Span.CopyTo(row.AsSpan(8));
        return row;
    }

    private static ImmutableArray<ReadOnlyMemory<byte>>
        EncodeGenericMethodDependencyContracts(
            BoundFunction function, int methodRow)
    {
        var dependencies = function.DependencyContracts;
        if (dependencies.IsDefault)
        {
            throw Unsupported(
                "A G1 Generic MethodDef has an invalid dependency-contract set.");
        }
        if (dependencies.IsEmpty)
        {
            return [];
        }

        BoundDependencyContract? dependsOn = null;
        BoundDependencyContract? forwardsBacking = null;
        BoundDependencyContract? borrowSourceStorage = null;
        foreach (var dependency in dependencies)
        {
            if (dependency.Destination
                    != BoundDependencyDestination.Result
                || dependency.DestinationOrdinal is not null
                || dependency.Sources.IsDefault
                || dependency.Relation is not (
                    BoundDependencyRelation.DependsOn
                    or BoundDependencyRelation.ForwardsBacking
                    or BoundDependencyRelation.BorrowSourceStorage))
            {
                throw Unsupported(
                    "G1 generic MethodDefs currently support only ordinary result dependency contracts.");
            }

            if (dependency.Relation == BoundDependencyRelation.DependsOn)
            {
                if (dependsOn is not null)
                {
                    throw Unsupported(
                        "A G1 generic MethodDef has multiple ordinary result DependsOn contracts.");
                }
                dependsOn = dependency;
            }
            else if (dependency.Relation
                == BoundDependencyRelation.ForwardsBacking)
            {
                if (forwardsBacking is not null)
                {
                    throw Unsupported(
                        "A G1 generic MethodDef has multiple ordinary result ForwardsBacking contracts.");
                }
                forwardsBacking = dependency;
            }
            else
            {
                if (borrowSourceStorage is not null)
                {
                    throw Unsupported(
                        "A G1 generic MethodDef has multiple ordinary result BorrowSourceStorage contracts.");
                }
                borrowSourceStorage = dependency;
            }
        }

        if (dependsOn is not BoundDependencyContract declared
            || (forwardsBacking is BoundDependencyContract forwarded
                && (declared.Sources.IsEmpty
                    || forwarded.Sources.Distinct().Count()
                        != forwarded.Sources.Length
                    || !forwarded.Sources.All(declared.Sources.Contains)))
            || (borrowSourceStorage is BoundDependencyContract borrowed
                && (declared.Sources.IsEmpty
                    || borrowed.Sources.Distinct().Count()
                        != borrowed.Sources.Length
                    || !borrowed.Sources.All(declared.Sources.Contains))))
        {
            throw Unsupported(
                "A G1 generic MethodDef has a result refinement source outside its declared DependsOn set.");
        }

        var orderedSources = declared.Sources
            .OrderBy(static source => source.Kind)
            .ThenBy(static source => source.FormalOrdinal ?? -1)
            .ToArray();
        if (orderedSources.Distinct().Count() != orderedSources.Length
            || orderedSources.Any(source =>
                source.Kind != BoundDependencySourceKind.Formal
                || source.FormalOrdinal is not int ordinal
                || (uint)ordinal >= (uint)function.ParameterCount))
        {
            throw Unsupported(
                "G1 generic MethodDef result dependencies must name unique method formals.");
        }

        var forwardedSources = new HashSet<BoundDependencySource>();
        if (forwardsBacking is BoundDependencyContract backing)
        {
            forwardedSources.UnionWith(backing.Sources);
        }
        var storageSources = borrowSourceStorage
            is BoundDependencyContract storage
                ? storage.Sources.ToHashSet()
                : new HashSet<BoundDependencySource>();
        if ((orderedSources.Length == 0
                && (!forwardedSources.IsEmpty || !storageSources.IsEmpty))
            || storageSources.Overlaps(forwardedSources))
        {
            throw Unsupported(
                "G1 generic MethodDef result dependency refinements must be nonempty and disjoint.");
        }

        var rows = ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>(
            Math.Max(1, orderedSources.Length));
        if (orderedSources.Length == 0)
        {
            rows.Add(EncodeGenericMethodDependencyRow(methodRow,
                sourceKind: 0, relationship: 0, sourceOrdinal: 0));
            return rows.MoveToImmutable();
        }

        foreach (var source in orderedSources)
        {
            var ordinal = source.FormalOrdinal!.Value;
            rows.Add(EncodeGenericMethodDependencyRow(methodRow,
                sourceKind: 1,
                relationship: storageSources.Contains(source)
                    ? (byte)1
                    : forwardedSources.Contains(source) ? (byte)2 : (byte)3,
                checked((uint)ordinal)));
        }
        return rows.MoveToImmutable();
    }

    private static ReadOnlyMemory<byte> EncodeGenericMethodDependencyRow(
        int methodRow, byte sourceKind, byte relationship,
        uint sourceOrdinal)
    {
        var row = new byte[20];
        WriteUInt32(row, 0, 0x1600_0000u | checked((uint)methodRow));
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4, 2), 4);
        row[8] = 1;
        row[9] = sourceKind;
        row[10] = relationship;
        row[11] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(16),
            sourceOrdinal);
        return row;
    }

    private static byte[] EncodeExportRows(
        List<TrRmExportRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var bytes = new byte[checked(rows.Count * 8)];
        for (var index = 0; index < rows.Count; index++)
        {
            var offset = index * 8;
            WriteUInt32(bytes, offset, rows[index].LocalDefId);
            WriteUInt32(bytes, offset + 4,
                checked((uint)rows[index].ExportKeyStringRow));
        }
        return bytes;
    }

    private static List<TrRmFeatureRow> ReadFeatureRows(
        IReadOnlyList<TrRmSection> sections)
    {
        var rows = new List<TrRmFeatureRow>();
        if (!TryGetSection(sections, 0x0004, out var section))
        {
            return rows;
        }

        for (var index = 0; index < section.Bytes.Length / 8; index++)
        {
            rows.Add(new TrRmFeatureRow(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    section.Bytes.Span.Slice(index * 8, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(
                    section.Bytes.Span.Slice((index * 8) + 4, 4))));
        }
        return rows;
    }

    private static uint GenericMethodTypeSpecRef(PrimitiveType type,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        int definitionOrdinal, BoundGenericMethodDefinition definition,
        bool allowSpanType = false,
        bool allowFormal = true,
        GenericMethodNominalTypeWriter? nominalTypes = null,
        int depth = 0)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw Unsupported(
                "A G1 source type exceeds the supported nesting depth.");
        }
        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            return nominalTypes is null
                ? throw Unsupported(
                    "This G1 type position does not select nominal TypeSpec emission.")
                : nominalTypes.TypeSpecRef(type, definition,
                    formalTypeSpecRows, builtInTypeSpecRows, depth + 1);
        }
        if (BoundSpanTypeTypes.IsValidType(type, spanTypes))
        {
            if (!allowSpanType)
            {
                throw Unsupported(
                    "The selected G1 profile does not admit Span values in MethodSig or Body value types.");
            }
            if (builtInTypeSpecRows.TryGetValue(type,
                    out var existingSpanTypeSpecRef))
            {
                return existingSpanTypeSpecRef;
            }

            var spanType = spanTypes[BoundSpanTypeTypes.Index(type)];
            if (spanType.ElementFormalDefinitionOrdinal
                    != definitionOrdinal
                && spanType.ElementFormalDefinitionOrdinal >= 0)
            {
                throw Unsupported(
                    "A G1 Span element formal belongs to a different Generic MethodDef.");
            }
            if (spanType.ElementFormalDefinitionOrdinal >= 0
                && !formalTypeSpecRows.ContainsKey(spanType.ElementType))
            {
                throw Unsupported(
                    "A G1 Span element formal is not owned by the emitted Generic MethodDef.");
            }

            BoundGenericMethodTypeTerm elementTerm;
            if (spanType.ElementFormalDefinitionOrdinal >= 0)
            {
                if ((uint)spanType.ElementFormalOrdinal
                    >= (uint)formalTypeSpecRows.Count)
                {
                    throw Unsupported(
                        "A G1 Span element formal is outside the emitted Generic MethodDef.");
                }
                elementTerm = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Formal,
                    PrimitiveType.Void, spanType.ElementFormalOrdinal, -1,
                    [], false, false, false, false);
            }
            else
            {
                if (spanType.ElementFormalDefinitionOrdinal
                        != -1
                    || spanType.IsElementWritable)
                {
                    throw Unsupported(
                        "A G1 Span element formal belongs to an unsupported owner.");
                }
                elementTerm = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Concrete,
                    spanType.ElementType, -1, -1, [],
                    false, false, false, false) {
                    AllowsVoid = spanType.ElementType == PrimitiveType.Void,
                };
            }

            var isElementWritable = spanType.IsElementWritable;
            if (isElementWritable)
            {
                if ((uint)spanType.ElementFormalOrdinal
                        >= (uint)definition.Formals.Length
                    || !definition.Formals[
                        spanType.ElementFormalOrdinal].RequiresWritable)
                {
                    throw Unsupported(
                        "A G1 writable Span element formal requires its declared write property.");
                }
            }

            var elementQualifiers = (byte)(
                (isElementWritable ? 0x01 : 0)
                | (spanType.IsInitialization ? 0x04 : 0));
            var elementTypeSpecRef = nominalTypes is null
                ? GenericMethodSpanArgumentTypeSpecRef(
                    elementTerm, elementQualifiers,
                    definition.Formals.Length,
                    definition, formalTypeSpecRows, builtInTypeSpecRows,
                    typeSpecRows)
                : GenericMethodTypeSpecRef(spanType.ElementType,
                    formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                    spanTypes, checkedReferenceTypes, definitionOrdinal,
                    definition, allowSpanType: true, allowFormal,
                    nominalTypes, depth + 1);
            if (nominalTypes is not null && elementQualifiers != 0)
            {
                elementTypeSpecRef = AddGenericTypeSpecQualifiers(
                    elementTypeSpecRef, elementQualifiers, typeSpecRows);
            }

            var typeSpecRef = checked((uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8A,
                elementTypeSpecRef, spanType.IsWritable ? (byte)0x01 : (byte)0));
            builtInTypeSpecRows.Add(type, typeSpecRef);
            return typeSpecRef;
        }

        if (CheckedReferenceTypeContract.IsValidCompleteType(type,
                checkedReferenceTypes))
        {
            var referenceType = checkedReferenceTypes[
                CheckedReferenceTypes.Index(type)];
            if (referenceType.IsGarbageCollected
                || referenceType.IsOptional
                || referenceType.IsOptionalValue)
            {
                throw Unsupported(
                    "The selected G1 profile supports only non-optional, non-GC checked-reference types.");
            }
            var referentTypeSpecRef = GenericMethodTypeSpecRef(
                referenceType.ReferentType, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows, spanTypes,
                checkedReferenceTypes, definitionOrdinal, definition,
                allowSpanType, allowFormal, nominalTypes, depth + 1);
            if (referenceType.IsReferentWritable)
            {
                if (!formalTypeSpecRows.ContainsKey(
                        referenceType.ReferentType))
                {
                    throw Unsupported(
                        "A G1 initialization-reference write referent must be a formal owned by the emitted Generic MethodDef.");
                }
                var hasWritableFormal = false;
                foreach (var formal in definition.Formals)
                {
                    if (formal.PlaceholderType
                        != referenceType.ReferentType)
                    {
                        continue;
                    }
                    hasWritableFormal = formal.RequiresWritable;
                    break;
                }
                if (!hasWritableFormal)
                {
                    throw Unsupported(
                        "A G1 write-qualified reference referent requires its formal's write property.");
                }
            }
            var referenceTypeSpecRef = checked(
                (uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(referenceType.IsInitialization
                ? TrRmTypeSpecTableWriter.InitializationReference(
                    referentTypeSpecRef, referenceType.IsWritable)
                : TrRmTypeSpecTableWriter.Wrapped(0x07,
                    referentTypeSpecRef,
                    referenceType.IsWritable ? (byte)0x01 : (byte)0));
            return referenceTypeSpecRef;
        }

        return allowFormal
            && formalTypeSpecRows.TryGetValue(type, out var formalTypeSpec)
            ? formalTypeSpec
            : GetBuiltInTypeSpecRef(type, builtInTypeSpecRows,
                typeSpecRows);
    }

    private static uint AddGenericTypeSpecQualifiers(uint typeSpecRef,
        byte qualifiers, List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        if (typeSpecRef == 0
            || typeSpecRef > (uint)typeSpecRows.Count)
        {
            throw Unsupported(
                "A G1 qualified type has no row-backed TypeSpec reference.");
        }

        var typeSpec = typeSpecRows[checked((int)typeSpecRef - 1)].Span;
        if (typeSpec.Length < 2)
        {
            throw Unsupported(
                "A G1 qualified type has an invalid TypeSpec row.");
        }

        var qualifiedTypeSpec = typeSpec.ToArray();
        qualifiedTypeSpec[1] |= qualifiers;
        var qualifiedTypeSpecRef = checked((uint)typeSpecRows.Count + 1);
        typeSpecRows.Add(qualifiedTypeSpec);
        return qualifiedTypeSpecRef;
    }

    private static void EnsureGenericBodyTypeMap(bool hasFloatingTypes,
        bool hasNativeWidthTypes, List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        foreach (var type in CompactBuiltinTypeMap(hasFloatingTypes,
            hasNativeWidthTypes))
        {
            var typeRow = TypeRow(type);
            if (typeRow > typeSpecRows.Count
                || !typeSpecRows[typeRow - 1].Span.SequenceEqual(
                    TrRmTypeSpecTableWriter.Builtin(
                        s_codes[Array.IndexOf(s_types, type)])))
            {
                throw Unsupported(
                    $"Generic method Body type map for '{type}' is not representable in the selected Standard type table.");
            }
        }
    }

    private static Dictionary<PrimitiveType, uint> GetBuiltInTypeSpecRows(
        List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        var result = new Dictionary<PrimitiveType, uint>();
        for (var index = 0; index < typeSpecRows.Count; index++)
        {
            var row = typeSpecRows[index].Span;
            if (row.Length != 3 || row[0] != 0x01 || row[1] != 0)
            {
                continue;
            }

            var typeIndex = Array.IndexOf(s_codes, row[2]);
            if (typeIndex >= 0)
            {
                _ = result.TryAdd(s_types[typeIndex],
                    checked((uint)index + 1));
            }
        }

        return result;
    }

    private static uint GetBuiltInTypeSpecRef(PrimitiveType type,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        if (builtInTypeSpecRows.TryGetValue(type, out var typeSpecRef))
        {
            return typeSpecRef;
        }

        var typeIndex = Array.IndexOf(s_types, type);
        if (typeIndex < 0)
        {
            throw Unsupported(
                $"Generic MethodDef type '{type}' has no G1 source TypeSpec.");
        }

        typeSpecRef = checked((uint)typeSpecRows.Count + 1);
        typeSpecRows.Add(TrRmTypeSpecTableWriter.Builtin(
            s_codes[typeIndex]));
        builtInTypeSpecRows.Add(type, typeSpecRef);
        return typeSpecRef;
    }

    private static bool IsGenericMethodInitializationReferenceType(
        PrimitiveType type,
        ImmutableArray<CheckedReferenceTypeDefinition> checkedReferenceTypes)
    {
        if (!CheckedReferenceTypeContract.IsValidType(type,
                checkedReferenceTypes))
        {
            return false;
        }

        var reference = checkedReferenceTypes[
            CheckedReferenceTypes.Index(type)];
        return reference.IsInitialization
            && !reference.IsGarbageCollected
            && !reference.IsOptional
            && !reference.IsOptionalValue;
    }

    private static bool GenericMethodTypeMatchesAfterSubstitution(
        PrimitiveType targetType, PrimitiveType callerType,
        BoundGenericMethodDefinition targetDefinition,
        BoundGenericMethodDefinition callerDefinition,
        ImmutableArray<PrimitiveType> typeArguments, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || typeArguments.Length != targetDefinition.Formals.Length)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < targetDefinition.Formals.Length;
            ordinal++)
        {
            if (targetDefinition.Formals[ordinal].PlaceholderType
                == targetType)
            {
                return typeArguments[ordinal] == callerType;
            }
        }

        if (BoundGenericNominalTypeTypes.IsType(targetType)
            || BoundGenericNominalTypeTypes.IsType(callerType))
        {
            var targetProgram = targetDefinition.ProofProgram;
            var callerProgram = callerDefinition.ProofProgram;
            var targetFunction = targetProgram.Functions[
                targetDefinition.FunctionIndex];
            var callerFunction = callerProgram.Functions[
                callerDefinition.FunctionIndex];
            if (!SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                    targetType, targetFunction, targetProgram, depth + 1,
                    out var targetTerm)
                || !SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                    callerType, callerFunction, callerProgram, depth + 1,
                    out var callerTerm))
            {
                return false;
            }
            var actualTerms = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(typeArguments.Length);
            foreach (var argument in typeArguments)
            {
                if (!SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                        argument, callerFunction, callerProgram, depth + 1,
                        out var argumentTerm))
                {
                    return false;
                }
                actualTerms.Add(argumentTerm);
            }
            return BoundGenericMethodTypeTerms.TrySubstitute(targetTerm,
                    actualTerms.MoveToImmutable(), out var substituted)
                && BoundGenericMethodTypeTerms.AreEquivalent(substituted,
                    callerTerm, depth + 1);
        }

        var targetReferenceIsValid
            = CheckedReferenceTypeContract.IsValidCompleteType(targetType,
                targetDefinition.ProofProgram.CheckedReferenceTypes);
        var callerReferenceIsValid
            = CheckedReferenceTypeContract.IsValidCompleteType(callerType,
                callerDefinition.ProofProgram.CheckedReferenceTypes);
        if (targetReferenceIsValid || callerReferenceIsValid)
        {
            if (!targetReferenceIsValid || !callerReferenceIsValid)
            {
                return false;
            }

            var targetReference = targetDefinition.ProofProgram
                .CheckedReferenceTypes[CheckedReferenceTypes.Index(
                    targetType)];
            var callerReference = callerDefinition.ProofProgram
                .CheckedReferenceTypes[CheckedReferenceTypes.Index(
                    callerType)];
            return targetReference.IsWritable
                    == callerReference.IsWritable
                && targetReference.IsGarbageCollected
                    == callerReference.IsGarbageCollected
                && targetReference.IsInitialization
                    == callerReference.IsInitialization
                && targetReference.IsOptional
                    == callerReference.IsOptional
                && targetReference.IsOptionalValue
                    == callerReference.IsOptionalValue
                && targetReference.IsReferentWritable
                    == callerReference.IsReferentWritable
                && GenericMethodTypeMatchesAfterSubstitution(
                    targetReference.ReferentType,
                    callerReference.ReferentType, targetDefinition,
                    callerDefinition, typeArguments, depth + 1);
        }

        var targetSpanIsValid = BoundSpanTypeTypes.IsValidType(targetType,
            targetDefinition.ProofProgram.SpanTypes);
        var callerSpanIsValid = BoundSpanTypeTypes.IsValidType(callerType,
            callerDefinition.ProofProgram.SpanTypes);
        if (targetSpanIsValid || callerSpanIsValid)
        {
            if (!targetSpanIsValid || !callerSpanIsValid)
            {
                return false;
            }

            var targetSpan = targetDefinition.ProofProgram.SpanTypes[
                BoundSpanTypeTypes.Index(targetType)];
            var callerSpan = callerDefinition.ProofProgram.SpanTypes[
                BoundSpanTypeTypes.Index(callerType)];
            if (targetSpan.IsElementWritable
                    != callerSpan.IsElementWritable
                || targetSpan.IsWritable != callerSpan.IsWritable
                || targetSpan.IsInitialization
                    != callerSpan.IsInitialization)
            {
                return false;
            }

            if (targetSpan.ElementFormalDefinitionOrdinal
                == targetDefinition.DefinitionOrdinal)
            {
                var targetFormalOrdinal
                    = targetSpan.ElementFormalOrdinal;
                if ((uint)targetFormalOrdinal
                        >= (uint)typeArguments.Length
                    || targetDefinition.Formals[targetFormalOrdinal]
                        .PlaceholderType != targetSpan.ElementType
                    || callerSpan.ElementType
                        != typeArguments[targetFormalOrdinal])
                {
                    return false;
                }

                var callerFormalOrdinal = -1;
                for (var ordinal = 0;
                    ordinal < callerDefinition.Formals.Length; ordinal++)
                {
                    if (callerDefinition.Formals[ordinal].PlaceholderType
                        == callerSpan.ElementType)
                    {
                        callerFormalOrdinal = ordinal;
                        break;
                    }
                }

                return callerFormalOrdinal >= 0
                    ? callerSpan.ElementFormalDefinitionOrdinal
                        == callerDefinition.DefinitionOrdinal
                        && callerSpan.ElementFormalOrdinal
                            == callerFormalOrdinal
                    : callerSpan.ElementFormalDefinitionOrdinal == -1
                        && !targetSpan.IsElementWritable;
            }

            return targetSpan.ElementFormalDefinitionOrdinal == -1
                && callerSpan.ElementFormalDefinitionOrdinal == -1
                && GenericMethodTypeMatchesAfterSubstitution(
                    targetSpan.ElementType, callerSpan.ElementType,
                    targetDefinition, callerDefinition, typeArguments,
                    depth + 1);
        }

        return targetType == callerType;
    }

    private static TrRmSection GetSection(
        IReadOnlyList<TrRmSection> sections, ushort kind)
        => TryGetSection(sections, kind, out var section)
            ? section
            : throw new InvalidDataException(
                $"TRRM section 0x{kind:X4} is required.");

    private static bool TryGetSection(IReadOnlyList<TrRmSection> sections,
        ushort kind, out TrRmSection section)
    {
        foreach (var candidate in sections)
        {
            if (candidate.Kind == kind)
            {
                section = candidate;
                return true;
            }
        }
        section = default;
        return false;
    }

    private static int GetCount(IReadOnlyList<TrRmSection> sections,
        ushort kind)
        => TryGetSection(sections, kind, out var section)
            ? checked((int)section.RecordCount) : 0;

    private static void SetSection(List<TrRmSection> sections, ushort kind,
        uint recordCount, byte[] bytes)
    {
        var index = sections.FindIndex(section => section.Kind == kind);
        if (recordCount == 0)
        {
            if (index >= 0)
            {
                sections.RemoveAt(index);
            }
            return;
        }

        var flags = index >= 0 ? sections[index].Flags : (ushort)0;
        var replacement = new TrRmSection(kind, flags, recordCount, bytes);
        if (index >= 0)
        {
            sections[index] = replacement;
        }
        else
        {
            sections.Add(replacement);
        }
    }

    private static int ReadInt(ReadOnlySpan<byte> bytes, int offset)
        => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, sizeof(uint))));

    private static void AppendUInt32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 24));
    }

    private static void WriteUInt32(List<byte> bytes, int offset,
        uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset, sizeof(uint)), value);

}
