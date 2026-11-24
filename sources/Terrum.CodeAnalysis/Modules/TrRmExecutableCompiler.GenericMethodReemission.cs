// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private readonly record struct RetainedGenericMethodOriginKey(
        string ProviderIdentity, string SchemaIdentity, string DigestHex,
        uint MethodDefId);

    /// <summary>Re-emits explicitly selected checked root generic MethodDefs under one exact G1-family schema.</summary>
    /// <param name="program">The checked program retaining the selected root MethodDefs.</param>
    /// <param name="moduleIdentity">The output Module identity.</param>
    /// <param name="schemaIdentity">The exact selected G1-family schema identity.</param>
    /// <param name="moduleTargetRequirement">The selected Module target requirement.</param>
    /// <param name="genericMethodExports">The explicit retained MethodDef origin and output-key selections.</param>
    /// <returns>The complete validated TRRM artifact.</returns>
    /// <exception cref="ArgumentNullException">The program or selection list is null.</exception>
    /// <exception cref="ArgumentException">A selection does not identify a checked root MethodDef or has invalid identity data.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The target requirement is unassigned or the selection exceeds the Export row limit.</exception>
    /// <exception cref="InvalidDataException">A retained direct MethodDef target has a malformed origin.</exception>
    /// <exception cref="NotSupportedException">The selected declaration or retained graph is outside the supported re-emission subset.</exception>
    public static byte[] EmitStandardReemittingGenericMethods(
        BoundProgram program, string moduleIdentity, string schemaIdentity,
        uint moduleTargetRequirement,
        IReadOnlyList<TrRmRetainedGenericMethodExport> genericMethodExports)
    {
        ArgumentNullException.ThrowIfNull(program);
        RejectUnconsumedDeclarationFailureBindings(program);
        ArgumentNullException.ThrowIfNull(genericMethodExports);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentException.ThrowIfNullOrEmpty(schemaIdentity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            moduleTargetRequirement,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        if (!IsValidRetainedGenericMethodSelectorText(moduleIdentity))
        {
            throw new ArgumentException(
                "The output Module identity is invalid.",
                nameof(moduleIdentity));
        }
        if (!StringComparer.Ordinal.Equals(schemaIdentity,
                StandardGenericMethodCallsSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardGenericMethodCallsParameterLabelsSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardGenericMethodCallsParameterLabelsReceiverAccessSchemaIdentity))
        {
            throw new ArgumentException(
                "Source-free generic MethodDef re-emission requires an exact G1-family schema identity.",
                nameof(schemaIdentity));
        }
        if (!program.GenericMethodDefinitions.IsEmpty)
        {
            throw Unsupported(
                "Source-free generic MethodDef re-emission does not accept fresh source generic definitions.");
        }

        var exportCount = genericMethodExports.Count;
        if (exportCount is < 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(
                nameof(genericMethodExports));
        }
        var snapshot = new TrRmRetainedGenericMethodExport[
            exportCount];
        for (var index = 0; index < snapshot.Length; index++)
        {
            var export = genericMethodExports[index]
                ?? throw new ArgumentException(
                    "Retained generic MethodDef selections cannot contain null entries.",
                    nameof(genericMethodExports));
            snapshot[index] = export with {
                ProviderArtifactDigest = export.ProviderArtifactDigest.IsDefault
                    ? default : [.. export.ProviderArtifactDigest],
            };
        }

        if (snapshot.Length == 0)
        {
            return EmitStandardCore(program, moduleIdentity,
                schemaIdentity, moduleTargetRequirement, null);
        }
        var retainedRoots = program.RetainedGenericMethodRoots;
        if (retainedRoots.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "The checked program retains no generic MethodDef roots.",
                nameof(genericMethodExports));
        }

        var outputKeys = new HashSet<string>(StringComparer.Ordinal);
        var definitionsByOrigin
            = new Dictionary<RetainedGenericMethodOriginKey,
                BoundGenericMethodDefinition>();
        var definitions = new List<BoundGenericMethodDefinition>(
            snapshot.Length);
        var exports = ImmutableArray.CreateBuilder<
            SourceGenericMethodExport>(snapshot.Length);
        foreach (var export in snapshot)
        {
            if (!ReferenceEquals(export.Program, program)
                || !StringComparer.Ordinal.Equals(export.SchemaIdentity,
                    schemaIdentity)
                || !IsValidRetainedGenericMethodSelectorText(
                    export.ProviderIdentity)
                || export.ProviderArtifactDigest.IsDefault
                || export.ProviderArtifactDigest.Length != 32
                || (export.MethodDefId >> 24) != 0x16
                || (export.MethodDefId & 0x00FF_FFFF) == 0
                || !IsValidRetainedGenericMethodSelectorText(
                    export.OriginExportKey)
                || !IsValidRetainedGenericMethodSelectorText(
                    export.ExportKey)
                || !outputKeys.Add(export.ExportKey))
            {
                throw new ArgumentException(
                    "A retained generic MethodDef selection is invalid or belongs to a different checked program.",
                    nameof(genericMethodExports));
            }

            var matchingRoots = retainedRoots
                .Where(root => root is not null
                    && !root.Origin.IsMethodDefTarget
                    && root.Origin.MethodDefId == export.MethodDefId
                    && StringComparer.Ordinal.Equals(
                        root.Origin.ProviderIdentity,
                        export.ProviderIdentity)
                    && StringComparer.Ordinal.Equals(
                        root.Origin.SchemaIdentity, export.SchemaIdentity)
                    && StringComparer.Ordinal.Equals(root.Origin.ExportKey,
                        export.OriginExportKey)
                    && root.Origin.ProviderArtifactDigest.AsSpan()
                        .SequenceEqual(
                            export.ProviderArtifactDigest.AsSpan()))
                .ToArray();
            if (matchingRoots.Length != 1)
            {
                throw new ArgumentException(
                    "The selection does not identify exactly one retained public root MethodDef.",
                    nameof(genericMethodExports));
            }

            var root = matchingRoots[0];
            if (!root.Signature.IsPublic || root.ProofFunction.IsExternal
                || root.Signature.Formals.IsDefault
                || root.Signature.Formals.IsEmpty
                || root.Signature.ConstantFormals is { IsDefaultOrEmpty: false })
            {
                throw new ArgumentException(
                    "The selection must identify a public open generic MethodDef with an actual retained Body.",
                    nameof(genericMethodExports));
            }
            if (!root.Signature.IsStatic || root.Signature.Owner is not null
                || root.Signature.IsMutable
                || root.FormalTypeOwnerOrdinal < 0
                || root.Signature.Formals.Length != root.Formals.Length
                || root.Signature.FormalOrder.IsDefault
                || root.Signature.FormalOrder.Length
                    != root.Formals.Length)
            {
                throw Unsupported(
                    "The retained generic MethodDef has an unsupported owner, receiver or formal shape.");
            }

            var originKey = new RetainedGenericMethodOriginKey(
                export.ProviderIdentity, export.SchemaIdentity,
                Convert.ToHexString(
                    export.ProviderArtifactDigest.AsSpan()),
                export.MethodDefId);
            if (!definitionsByOrigin.TryGetValue(originKey,
                    out var definition))
            {
                definition = CreateRetainedGenericMethodDefinition(
                    program, root, definitions.Count);
                definitionsByOrigin.Add(originKey, definition);
                definitions.Add(definition);
            }

            exports.Add(new SourceGenericMethodExport(definition,
                export.ExportKey));
        }

        AddRetainedGenericMethodBodyClosure(program, definitions,
            definitionsByOrigin);
        var sourceMethodExports = new SourceGenericMethodExports(program,
            exports.MoveToImmutable());
        return EmitStandardCore(program, moduleIdentity, schemaIdentity,
            moduleTargetRequirement, null, sourceMethodExports,
            retainedGenericMethodDefinitions: definitions);
    }

    private static BoundGenericMethodDefinition
        CreateRetainedGenericMethodDefinition(BoundProgram program,
            BoundGenericMethodImport import, int definitionOrdinal)
    {
        if (import.ProofFunction.IsExternal
            || !import.Signature.IsStatic
            || import.Signature.IsMutable
            || import.Signature.Owner is not null
            || !import.Signature.ConstantFormals.IsDefaultOrEmpty
            || import.FormalTypeOwnerOrdinal < 0
            || import.Formals.IsDefaultOrEmpty
            || import.Signature.Formals.IsDefaultOrEmpty
            || import.Signature.Formals.Length != import.Formals.Length
            || import.Signature.FormalOrder.IsDefault
            || import.Signature.FormalOrder.Length
                != import.Formals.Length)
        {
            throw Unsupported(
                "The retained generic MethodDef has an unsupported Body, owner, receiver or formal shape.");
        }

        var sourceFunction = import.ProofFunction with {
            GenericMethodProofDefinitionOrdinal = definitionOrdinal,
            GenericMethodProofTypeFormalOwnerOrdinal =
                import.FormalTypeOwnerOrdinal,
            GenericMethodProofTypeParameters = [
                .. import.Formals.Select(static formal =>
                    formal.PlaceholderType),
            ],
        };
        var supportFunctions = import.ProofSupportFunctions.IsDefault
            ? ImmutableArray<BoundFunction>.Empty
            : import.ProofSupportFunctions;
        var genericCallTargets = new List<BoundFunction>();
        var genericCallFunctionIndices
            = new Dictionary<BoundCall, int>(
                ReferenceEqualityComparer.Instance);
        if (!BoundTreeTraversal.VisitExpressions(sourceFunction.Body,
                expression =>
                {
                    if (expression is not BoundCall {
                            InterfaceCallIdentity: null,
                            ImportedGenericMethodCall: { } target,
                        } call)
                    {
                        return;
                    }
                    if (genericCallFunctionIndices.ContainsKey(call))
                    {
                        return;
                    }

                    var targetFunction = target.ProofFunction with {
                        ImportedGenericMethod = target,
                        ImportedGenericMethodTypeArguments =
                            call.ImportedGenericMethodTypeArguments,
                    };
                    var targetIndex = supportFunctions.Length
                        + genericCallTargets.Count;
                    genericCallTargets.Add(targetFunction);
                    genericCallFunctionIndices.Add(call, targetIndex);
                }))
        {
            throw Unsupported(
                "The retained generic MethodDef Body could not be traversed.");
        }

        var proofFunction = BoundTreeTraversal.RewriteCalls(sourceFunction,
            call => genericCallFunctionIndices.TryGetValue(call,
                    out var targetIndex)
                ? call with { Function = targetIndex }
                : call);
        var proofFunctions = ImmutableArray.CreateBuilder<BoundFunction>(
            1 + supportFunctions.Length + genericCallTargets.Count);
        proofFunctions.AddRange(supportFunctions);
        proofFunctions.AddRange(genericCallTargets);
        var proofFunctionIndex = proofFunctions.Count;
        proofFunctions.Add(proofFunction);
        var proofProgram = program with {
            Functions = proofFunctions.MoveToImmutable(),
            EntryPoint = -1,
            GenericMethodDefinitions = [],
            GenericMethodSignatures = [],
        };
        return new BoundGenericMethodDefinition(definitionOrdinal,
            proofFunction.Name, import.Signature.IsPublic,
            import.Signature.RequiresUnsafeCaller, proofFunctionIndex, -1, default,
            import.Formals, proofProgram) {
            FormalTypeOwnerOrdinal = import.FormalTypeOwnerOrdinal,
            RetainedOrigin = import.Origin,
            IsStatic = import.Signature.IsStatic,
            IsMutable = import.Signature.IsMutable,
            Owner = import.Signature.Owner,
            FormalOrder = import.Signature.FormalOrder,
            RuntimeParameterCount = proofFunction.ParameterCount,
        };
    }

    private static void AddRetainedGenericMethodBodyClosure(
        BoundProgram program,
        List<BoundGenericMethodDefinition> definitions,
        Dictionary<RetainedGenericMethodOriginKey,
            BoundGenericMethodDefinition> definitionsByOrigin)
    {
        var retainedRootArtifacts = definitionsByOrigin.Keys
            .Select(static origin => (
                origin.ProviderIdentity, origin.SchemaIdentity,
                origin.DigestHex))
            .ToHashSet();
        var remainingVisits = SemanticVerifier.MaximumAnalysisVisits;
        for (var definitionIndex = 0;
            definitionIndex < definitions.Count; definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            var proofFunction = definition.ProofProgram.Functions[
                definition.FunctionIndex];
            var directTargets = new List<BoundGenericMethodImport>();
            if (!BoundTreeTraversal.VisitExpressions(proofFunction.Body,
                    expression =>
                    {
                        if (--remainingVisits < 0)
                        {
                            throw Unsupported(
                                "The retained generic MethodDef closure exceeds the supported analysis limit.");
                        }
                        if (expression is BoundCall {
                                ImportedGenericMethodCall: { } target,
                            }
                            && target.Origin.IsMethodDefTarget
                            && target.Binding is null)
                        {
                            directTargets.Add(target);
                        }
                    }))
            {
                throw Unsupported(
                    "The retained generic MethodDef closure could not be traversed.");
            }

            foreach (var target in directTargets)
            {
                var origin = target.Origin;
                if (!retainedRootArtifacts.Contains((
                        origin.ProviderIdentity, origin.SchemaIdentity,
                        Convert.ToHexString(
                            origin.ProviderArtifactDigest.AsSpan())))
                    || origin.ProviderArtifactDigest.IsDefault
                    || origin.ProviderArtifactDigest.Length != 32
                    || (origin.MethodDefId >> 24) != 0x16
                    || (origin.MethodDefId & 0x00FF_FFFF) == 0)
                {
                    throw new InvalidDataException(
                        "A retained GenericCall directly targets a MethodDef outside its selected root artifact.");
                }

                var originKey = GetRetainedGenericMethodOriginKey(origin);
                if (definitionsByOrigin.ContainsKey(originKey))
                {
                    continue;
                }

                var helper = CreateRetainedGenericMethodDefinition(program,
                    target, definitions.Count);
                definitionsByOrigin.Add(originKey, helper);
                definitions.Add(helper);
            }
        }
    }

    private static bool IsSameRetainedGenericMethodOrigin(
        BoundGenericMethodImportOrigin? definitionOrigin,
        BoundGenericMethodImportOrigin targetOrigin)
        => definitionOrigin is not null
            && definitionOrigin.MethodDefId == targetOrigin.MethodDefId
            && definitionOrigin.TargetRequirement
                == targetOrigin.TargetRequirement
            && definitionOrigin.RuntimePolicy == targetOrigin.RuntimePolicy
            && StringComparer.Ordinal.Equals(
                definitionOrigin.ProviderIdentity,
                targetOrigin.ProviderIdentity)
            && StringComparer.Ordinal.Equals(
                definitionOrigin.SchemaIdentity,
                targetOrigin.SchemaIdentity)
            && definitionOrigin.ProviderArtifactDigest.AsSpan()
                .SequenceEqual(
                    targetOrigin.ProviderArtifactDigest.AsSpan());

    private static RetainedGenericMethodOriginKey
        GetRetainedGenericMethodOriginKey(
            BoundGenericMethodImportOrigin origin)
        => new(origin.ProviderIdentity, origin.SchemaIdentity,
            Convert.ToHexString(origin.ProviderArtifactDigest.AsSpan()),
            origin.MethodDefId);

    private static bool IsValidRetainedGenericMethodSelectorText(
        string? value)
        => !string.IsNullOrEmpty(value)
            && !value.Contains('\0', StringComparison.Ordinal)
            && IsValidSourceGenericMethodExportKey(value);
}
