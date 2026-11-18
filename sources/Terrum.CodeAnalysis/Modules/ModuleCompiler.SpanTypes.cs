// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private sealed record SpanTypeReferenceMerge(
        ImmutableArray<BoundSpanTypeDefinition> Types,
        ImmutableArray<PrimitiveType>[] TypeMappings,
        ImmutableArray<int>[] FormalOwnerMappings);

    private static ImmutableArray<int>[] CreateSpanFormalOwnerMappings(
        IReadOnlyList<CompilationReference> references)
    {
        var formalOwnerMappings = new ImmutableArray<int>[references.Count];
        var nextFormalOwnerOrdinal = 0;
        for (var referenceIndex = 0;
            referenceIndex < references.Count; referenceIndex++)
        {
            var reference = references[referenceIndex];
            var sourceTypes = reference.SpanTypes;
            if (!BoundSpanTypeContract.TryCreateTypeIdentityIndex(
                    sourceTypes, out _))
            {
                throw new ModuleContractException(
                    "A compilation reference has an invalid Span type table.");
            }

            var owners = new SortedSet<int>();
            var importedFormalOwners = new HashSet<int>();
            var pendingImports = new Stack<BoundGenericMethodImport>(
                reference.GenericMethodImports);
            foreach (var function in reference.Functions)
            {
                if (function.ImportedGenericMethod is
                    BoundGenericMethodImport imported)
                {
                    pendingImports.Push(imported);
                }
                if (!BoundTreeTraversal.VisitExpressions(function.Body,
                        expression =>
                        {
                            if (expression is BoundCall {
                                    ImportedGenericMethodCall:
                                        BoundGenericMethodImport called,
                                })
                            {
                                pendingImports.Push(called);
                            }
                        }))
                {
                    throw new ModuleContractException(
                        "A compilation reference has an invalid expression tree.");
                }
            }
            var visitedImports = new HashSet<BoundGenericMethodImport>();
            while (pendingImports.Count != 0)
            {
                var import = pendingImports.Pop();
                if (!visitedImports.Add(import))
                {
                    continue;
                }
                if (import.FormalTypeOwnerOrdinal < -1
                    || import.FormalTypeOwnerOrdinal
                        >= BoundSpanTypeTypes.MaximumCount)
                {
                    throw new ModuleContractException(
                        "A generic MethodDef import has an invalid Span formal owner.");
                }
                if (import.FormalTypeOwnerOrdinal >= 0)
                {
                    owners.Add(import.FormalTypeOwnerOrdinal);
                    importedFormalOwners.Add(
                        import.FormalTypeOwnerOrdinal);
                }
                if (import.Binding is BoundGenericMethodImportBinding binding)
                {
                    pendingImports.Push(binding.Expected);
                    pendingImports.Push(binding.Provider);
                }
                foreach (var function in import.ProofSupportFunctions
                    .Prepend(import.ProofFunction))
                {
                    if (function.ImportedGenericMethod is
                        BoundGenericMethodImport imported)
                    {
                        pendingImports.Push(imported);
                    }
                    if (!BoundTreeTraversal.VisitExpressions(function.Body,
                            expression =>
                            {
                                if (expression is BoundCall {
                                        ImportedGenericMethodCall:
                                            BoundGenericMethodImport called,
                                    })
                                {
                                    pendingImports.Push(called);
                                }
                            }))
                    {
                        throw new ModuleContractException(
                            "A generic MethodDef proof has an invalid expression tree.");
                    }
                }
            }

            foreach (var spanType in sourceTypes)
            {
                if (spanType.ElementFormalDefinitionOrdinal >= 0)
                {
                    throw new ModuleContractException(
                        "A compilation reference cannot merge a source-local Span formal owner.");
                }
                if (spanType.ElementFormalDefinitionOrdinal
                    <= BoundSpanTypeContract.ImportedFormalDefinitionOrdinal)
                {
                    var ownerOrdinal = (long)
                        BoundSpanTypeContract.ImportedFormalDefinitionOrdinal
                        - spanType.ElementFormalDefinitionOrdinal;
                    if (ownerOrdinal >= BoundSpanTypeTypes.MaximumCount)
                    {
                        throw new ModuleContractException(
                            "A Span type has an out-of-range imported formal owner.");
                    }
                    if (!importedFormalOwners.Contains((int)ownerOrdinal))
                    {
                        throw new ModuleContractException(
                            "A Span type references an unbound imported formal owner.");
                    }
                    owners.Add((int)ownerOrdinal);
                }
            }

            var ownerMapLength = owners.Count == 0
                ? 0 : checked(owners.Max() + 1);
            var ownerMap = ImmutableArray.CreateBuilder<int>(
                ownerMapLength);
            for (var owner = 0; owner < ownerMapLength; owner++)
            {
                ownerMap.Add(-1);
            }
            foreach (var owner in owners)
            {
                if (nextFormalOwnerOrdinal
                    >= BoundSpanTypeTypes.MaximumCount)
                {
                    throw new ModuleContractException(
                        "Combined generic imports exceed the Span formal-owner limit.");
                }
                ownerMap[owner] = nextFormalOwnerOrdinal++;
            }
            formalOwnerMappings[referenceIndex]
                = ownerMap.MoveToImmutable();
        }

        return formalOwnerMappings;
    }

    private static SpanTypeReferenceMerge MergeSpanTypeDefinitions(
        IReadOnlyList<CompilationReference> references,
        Func<int, BoundSpanTypeDefinition, PrimitiveType>
            remapElementType,
        ImmutableArray<int>[]? formalOwnerMappings = null)
    {
        formalOwnerMappings ??= CreateSpanFormalOwnerMappings(references);

        var typeMappings = new ImmutableArray<PrimitiveType>[references.Count];
        var mergedTypes = ImmutableArray.CreateBuilder<
            BoundSpanTypeDefinition>();
        var typeIndexes = new Dictionary<(PrimitiveType ElementType,
            int ElementFormalDefinitionOrdinal, int ElementFormalOrdinal,
            bool IsElementWritable, bool IsWritable,
            bool IsInitialization), int>();
        for (var referenceIndex = 0;
            referenceIndex < references.Count; referenceIndex++)
        {
            var reference = references[referenceIndex];
            var sourceTypes = reference.SpanTypes;
            var sourceToDestination = ImmutableArray.CreateBuilder<
                PrimitiveType>(sourceTypes.Length);
            var ownerMapping = formalOwnerMappings[referenceIndex];
            for (var sourceIndex = 0; sourceIndex < sourceTypes.Length;
                sourceIndex++)
            {
                var spanType = sourceTypes[sourceIndex];
                var elementType = remapElementType(referenceIndex,
                    spanType);
                if (BoundSpanTypeTypes.IsType(elementType))
                {
                    var nestedIndex = BoundSpanTypeTypes.Index(elementType);
                    if ((uint)nestedIndex >= (uint)sourceIndex)
                    {
                        throw new ModuleContractException(
                            "A Span type table has a forward or cyclic nested row.");
                    }
                    elementType = sourceToDestination[nestedIndex];
                }

                var formalDefinitionOrdinal
                    = spanType.ElementFormalDefinitionOrdinal;
                if (formalDefinitionOrdinal
                    <= BoundSpanTypeContract.ImportedFormalDefinitionOrdinal)
                {
                    var sourceOwnerOrdinal = checked((int)(
                        (long)BoundSpanTypeContract
                            .ImportedFormalDefinitionOrdinal
                        - formalDefinitionOrdinal));
                    if ((uint)sourceOwnerOrdinal
                            >= (uint)ownerMapping.Length
                        || ownerMapping[sourceOwnerOrdinal] < 0)
                    {
                        throw new ModuleContractException(
                            "A Span formal owner has no merged destination.");
                    }
                    formalDefinitionOrdinal = checked(
                        BoundSpanTypeContract
                            .ImportedFormalDefinitionOrdinal
                        - ownerMapping[sourceOwnerOrdinal]);
                }

                var key = (elementType, formalDefinitionOrdinal,
                    spanType.ElementFormalOrdinal,
                    spanType.IsElementWritable, spanType.IsWritable,
                    spanType.IsInitialization);
                if (!typeIndexes.TryGetValue(key, out var destinationIndex))
                {
                    if (mergedTypes.Count >= BoundSpanTypeTypes.MaximumCount)
                    {
                        throw new ModuleContractException(
                            "Combined Span type tables exceed their identity limit.");
                    }
                    destinationIndex = mergedTypes.Count;
                    var remappedSpanType =
                        BoundTreeTraversal.RewriteTypes(spanType,
                            _ => elementType);
                    mergedTypes.Add(remappedSpanType with {
                        ElementFormalDefinitionOrdinal
                            = formalDefinitionOrdinal,
                    });
                    typeIndexes.Add(key, destinationIndex);
                }
                sourceToDestination.Add(
                    BoundSpanTypeTypes.Type(destinationIndex));
            }
            typeMappings[referenceIndex]
                = sourceToDestination.MoveToImmutable();
        }

        var result = mergedTypes.ToImmutable();
        if (!BoundSpanTypeContract.TryCreateTypeIdentityIndex(result,
                out _))
        {
            throw new ModuleContractException(
                "The merged Span type table is not canonical.");
        }

        return new SpanTypeReferenceMerge(result, typeMappings,
            formalOwnerMappings);
    }

    private sealed record MixedSpanNominalTypeMerge(
        ImmutableArray<BoundGenericNominalTypeInstance> GenericNominalTypes,
        SpanTypeReferenceMerge SpanTypes);

    private static MixedSpanNominalTypeMerge
        CanonicalizeMixedSpanAndGenericNominalTypes(
            ImmutableArray<BoundGenericNominalTypeInstance> genericNominalTypes,
            ImmutableArray<int> genericNominalInstanceReferenceIndexes,
            int[][] genericNominalTypeMappings,
            SpanTypeReferenceMerge spanTypes)
    {
        if (genericNominalTypes.Length
                != genericNominalInstanceReferenceIndexes.Length)
        {
            throw new ModuleContractException(
                "The merged generic nominal instances have incomplete provider origins.");
        }

        var canonicalizationPasses = 0;
        while (true)
        {
            if (canonicalizationPasses++ > SemanticVerifier.MaximumDepth)
            {
                throw new ModuleContractException(
                    "Mixed Span and constructed nominal canonicalization exceeded the type-depth limit.");
            }

            var nominalTypeMappings = new int[genericNominalTypes.Length];
            var canonicalNominalTypes = ImmutableArray.CreateBuilder<
                BoundGenericNominalTypeInstance>(genericNominalTypes.Length);
            var canonicalNominalReferenceIndexes = ImmutableArray.CreateBuilder<
                int>(genericNominalInstanceReferenceIndexes.Length);
            var nominalTypeIndexes = new Dictionary<string, int>(
                StringComparer.Ordinal);
            for (var instanceIndex = 0;
                instanceIndex < genericNominalTypes.Length;
                instanceIndex++)
            {
                var instance = BoundTreeTraversal.RewriteTypes(
                    genericNominalTypes[instanceIndex], type =>
                    {
                        if (!BoundGenericNominalTypeTypes.IsType(type))
                        {
                            return type;
                        }

                        var nestedIndex
                            = BoundGenericNominalTypeTypes.Index(type);
                        if ((uint)nestedIndex >= (uint)instanceIndex)
                        {
                            throw new ModuleContractException(
                                "Generic nominal type substitutions are cyclic or out of order.");
                        }

                        return BoundGenericNominalTypeTypes.Type(
                            nominalTypeMappings[nestedIndex]);
                    });
                var key = BoundGenericNominalTypeContract.CreateUseKey(
                    instance.Definition, instance.Arguments,
                    instance.Qualifiers, instance.TypeSpecForm);
                if (!nominalTypeIndexes.TryGetValue(key,
                        out var destinationIndex))
                {
                    destinationIndex = canonicalNominalTypes.Count;
                    canonicalNominalTypes.Add(instance);
                    canonicalNominalReferenceIndexes.Add(
                        genericNominalInstanceReferenceIndexes[
                            instanceIndex]);
                    nominalTypeIndexes.Add(key, destinationIndex);
                }

                nominalTypeMappings[instanceIndex] = destinationIndex;
            }

            var nominalTypesMerged
                = canonicalNominalTypes.Count != genericNominalTypes.Length;
            foreach (var referenceMappings in genericNominalTypeMappings)
            {
                for (var instanceIndex = 0;
                    instanceIndex < referenceMappings.Length;
                    instanceIndex++)
                {
                    var sourceIndex = referenceMappings[instanceIndex];
                    if ((uint)sourceIndex
                        >= (uint)nominalTypeMappings.Length)
                    {
                        throw new ModuleContractException(
                            "A generic nominal source mapping is invalid.");
                    }

                    referenceMappings[instanceIndex]
                        = nominalTypeMappings[sourceIndex];
                }
            }

            var spanTypeMappings = new int[spanTypes.Types.Length];
            var canonicalSpanTypes = ImmutableArray.CreateBuilder<
                BoundSpanTypeDefinition>(spanTypes.Types.Length);
            var spanTypeIndexes = new Dictionary<(
                PrimitiveType ElementType,
                int ElementFormalDefinitionOrdinal,
                int ElementFormalOrdinal,
                bool IsElementWritable,
                bool IsWritable,
                bool IsInitialization), int>(spanTypes.Types.Length);
            for (var spanIndex = 0;
                spanIndex < spanTypes.Types.Length;
                spanIndex++)
            {
                var spanType = spanTypes.Types[spanIndex];
                var elementType = spanType.ElementType;
                if (BoundGenericNominalTypeTypes.IsType(elementType))
                {
                    var nominalIndex
                        = BoundGenericNominalTypeTypes.Index(elementType);
                    if ((uint)nominalIndex >= (uint)nominalTypeMappings.Length)
                    {
                        throw new ModuleContractException(
                            "A Span type references an invalid generic nominal instance.");
                    }

                    elementType = BoundGenericNominalTypeTypes.Type(
                        nominalTypeMappings[nominalIndex]);
                }
                if (BoundSpanTypeTypes.IsType(elementType))
                {
                    var nestedIndex = BoundSpanTypeTypes.Index(elementType);
                    if ((uint)nestedIndex >= (uint)spanIndex)
                    {
                        throw new ModuleContractException(
                            "A Span type table has a forward or cyclic nested row.");
                    }

                    elementType = BoundSpanTypeTypes.Type(
                        spanTypeMappings[nestedIndex]);
                }

                var key = (elementType,
                    spanType.ElementFormalDefinitionOrdinal,
                    spanType.ElementFormalOrdinal,
                    spanType.IsElementWritable, spanType.IsWritable,
                    spanType.IsInitialization);
                if (!spanTypeIndexes.TryGetValue(key,
                        out var destinationIndex))
                {
                    destinationIndex = canonicalSpanTypes.Count;
                    canonicalSpanTypes.Add(spanType with {
                        ElementType = elementType,
                    });
                    spanTypeIndexes.Add(key, destinationIndex);
                }

                spanTypeMappings[spanIndex] = destinationIndex;
            }

            var spanTypesMerged
                = canonicalSpanTypes.Count != spanTypes.Types.Length;
            var canonicalSpanTypeMappings
                = new ImmutableArray<PrimitiveType>[
                    spanTypes.TypeMappings.Length];
            for (var referenceIndex = 0;
                referenceIndex < spanTypes.TypeMappings.Length;
                referenceIndex++)
            {
                var sourceMappings = spanTypes.TypeMappings[referenceIndex];
                var destinationMappings
                    = ImmutableArray.CreateBuilder<PrimitiveType>(
                        sourceMappings.Length);
                foreach (var sourceType in sourceMappings)
                {
                    if (!BoundSpanTypeTypes.IsType(sourceType))
                    {
                        throw new ModuleContractException(
                            "A Span source mapping has no Span identity.");
                    }

                    var sourceIndex = BoundSpanTypeTypes.Index(sourceType);
                    if ((uint)sourceIndex >= (uint)spanTypeMappings.Length)
                    {
                        throw new ModuleContractException(
                            "A Span source mapping is invalid.");
                    }

                    destinationMappings.Add(BoundSpanTypeTypes.Type(
                        spanTypeMappings[sourceIndex]));
                }
                canonicalSpanTypeMappings[referenceIndex]
                    = destinationMappings.MoveToImmutable();
            }

            var nextGenericNominalTypes
                = canonicalNominalTypes.ToImmutable();
            if (spanTypesMerged)
            {
                var remappedNominalTypes
                    = ImmutableArray.CreateBuilder<
                        BoundGenericNominalTypeInstance>(
                            nextGenericNominalTypes.Length);
                foreach (var instance in nextGenericNominalTypes)
                {
                    remappedNominalTypes.Add(
                        BoundTreeTraversal.RewriteTypes(instance, type =>
                        {
                            if (!BoundSpanTypeTypes.IsType(type))
                            {
                                return type;
                            }

                            var sourceIndex = BoundSpanTypeTypes.Index(type);
                            if ((uint)sourceIndex
                                >= (uint)spanTypeMappings.Length)
                            {
                                throw new ModuleContractException(
                                    "A generic nominal instance has an invalid Span argument.");
                            }

                            return BoundSpanTypeTypes.Type(
                                spanTypeMappings[sourceIndex]);
                        }));
                }
                nextGenericNominalTypes
                    = remappedNominalTypes.MoveToImmutable();
            }

            genericNominalTypes = nextGenericNominalTypes;
            genericNominalInstanceReferenceIndexes
                = canonicalNominalReferenceIndexes.ToImmutable();
            spanTypes = new SpanTypeReferenceMerge(
                canonicalSpanTypes.ToImmutable(),
                canonicalSpanTypeMappings,
                spanTypes.FormalOwnerMappings);
            if (!nominalTypesMerged && !spanTypesMerged)
            {
                break;
            }
        }

        ValidateMixedSpanAndGenericNominalTypeGraph(genericNominalTypes,
            spanTypes.Types);
        if (!BoundSpanTypeContract.TryCreateTypeIdentityIndex(
                spanTypes.Types, out _))
        {
            throw new ModuleContractException(
                "Mixed Span and constructed nominal canonicalization produced duplicate identities.");
        }

        return new MixedSpanNominalTypeMerge(genericNominalTypes,
            spanTypes);
    }

    private static void ValidateMixedSpanAndGenericNominalTypeGraph(
        ImmutableArray<BoundGenericNominalTypeInstance> genericNominalTypes,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes)
    {
        var nominalStates = new byte[genericNominalTypes.Length];
        var spanStates = new byte[spanTypes.Length];
        var pending = new Stack<(PrimitiveType Type, bool IsExit)>();
        for (var index = 0; index < genericNominalTypes.Length; index++)
        {
            pending.Push((BoundGenericNominalTypeTypes.Type(index), false));
        }
        for (var index = 0; index < spanTypes.Length; index++)
        {
            pending.Push((BoundSpanTypeTypes.Type(index), false));
        }

        while (pending.TryPop(out var item))
        {
            var isSpan = BoundSpanTypeTypes.IsType(item.Type);
            var isNominal = BoundGenericNominalTypeTypes.IsType(item.Type);
            if (!isSpan && !isNominal)
            {
                continue;
            }
            var typeIndex = isSpan
                ? BoundSpanTypeTypes.Index(item.Type)
                : BoundGenericNominalTypeTypes.Index(item.Type);
            var typeStates = isSpan ? spanStates : nominalStates;
            if ((uint)typeIndex >= (uint)typeStates.Length)
            {
                throw new ModuleContractException(
                    "The merged Span and generic nominal type graph has an invalid index.");
            }
            if (item.IsExit)
            {
                typeStates[typeIndex] = 2;
                continue;
            }
            if (typeStates[typeIndex] == 1)
            {
                throw new ModuleContractException(
                    "The merged Span and generic nominal type graph is cyclic.");
            }
            if (typeStates[typeIndex] == 2)
            {
                continue;
            }

            typeStates[typeIndex] = 1;
            pending.Push((item.Type, true));
            if (isSpan)
            {
                pending.Push((spanTypes[typeIndex].ElementType, false));
                continue;
            }

            foreach (var argument in genericNominalTypes[typeIndex].Arguments)
            {
                if (argument.Kind == BoundGenericArgumentKind.Type)
                {
                    pending.Push((argument.Type, false));
                }
                if (argument.TypeArgumentTerm is
                    BoundGenericMethodTypeTerm typeArgumentTerm)
                {
                    PushConcreteTypeTerms(typeArgumentTerm, pending);
                }
            }
        }
    }

    private static void PushConcreteTypeTerms(
        BoundGenericMethodTypeTerm root,
        Stack<(PrimitiveType Type, bool IsExit)> pending)
    {
        var terms = new Stack<BoundGenericMethodTypeTerm>();
        terms.Push(root);
        while (terms.TryPop(out var term))
        {
            if (term.Kind == BoundGenericMethodTypeTermKind.Concrete
                && (BoundGenericNominalTypeTypes.IsType(term.ConcreteType)
                    || BoundSpanTypeTypes.IsType(term.ConcreteType)))
            {
                pending.Push((term.ConcreteType, false));
            }
            foreach (var child in term.TypeArguments)
            {
                terms.Push(child);
            }
        }
    }

    private static PrimitiveType RemapSpanType(PrimitiveType type,
        ImmutableArray<PrimitiveType> mapping)
    {
        if (!BoundSpanTypeTypes.IsType(type))
        {
            return type;
        }

        var index = BoundSpanTypeTypes.Index(type);
        if ((uint)index >= (uint)mapping.Length
            || !BoundSpanTypeTypes.IsType(mapping[index]))
        {
            throw new ModuleContractException(
                "A Span type has no merged destination identity.");
        }
        return mapping[index];
    }

    private sealed class SpanTypeImportRemapper
    {
        private readonly int _referenceIndex;
        private readonly SpanTypeReferenceMerge _merge;
        private readonly Func<PrimitiveType, PrimitiveType> _rewriteType;
        private readonly Dictionary<(BoundGenericMethodImport Import,
            int ReferenceIndex), BoundGenericMethodImport> _cache = [];
        private readonly Dictionary<BoundGenericMethodSignature,
            BoundGenericMethodSignature> _signatureCache
                = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<
            ImmutableArray<BoundGenericMethodFormal>,
            ImmutableArray<BoundGenericMethodFormal>> _formalCache = [];

        internal SpanTypeImportRemapper(int referenceIndex,
            SpanTypeReferenceMerge merge,
            Func<PrimitiveType, PrimitiveType>? rewriteAdditionalType = null)
        {
            _referenceIndex = referenceIndex;
            _merge = merge;
            var typeMapping = merge.TypeMappings[referenceIndex];
            _rewriteType = rewriteAdditionalType is null
                ? type => RemapSpanType(type, typeMapping)
                : type => rewriteAdditionalType(
                    RemapSpanType(type, typeMapping));
        }

        internal ImmutableArray<BoundGenericMethodImport> RemapImports(
            CompilationReference reference)
        {
            var imports = ImmutableArray.CreateBuilder<
                BoundGenericMethodImport>(
                    reference.GenericMethodImports.Length);
            foreach (var import in reference.GenericMethodImports)
            {
                imports.Add(Remap(import));
            }
            return imports.MoveToImmutable();
        }

        internal BoundGenericMethodImport Remap(
            BoundGenericMethodImport import) =>
            RemapGenericMethodImport(import, _referenceIndex, _merge,
                _cache, _signatureCache, _formalCache, _rewriteType);

        internal BoundFunction RemapFunction(BoundFunction function)
        {
            var typeMapping = _merge.TypeMappings[_referenceIndex];
            var rewritten = BoundTreeTraversal.RewriteTypes(function,
                type => RemapSpanType(type, typeMapping));
            rewritten = rewritten with {
                ImportedGenericMethod =
                    function.ImportedGenericMethod is
                        BoundGenericMethodImport imported
                        ? Remap(imported) : null,
            };
            return BoundTreeTraversal.RewriteExpressions(rewritten,
                expression => expression is BoundCall {
                        ImportedGenericMethodCall:
                            BoundGenericMethodImport importedCall,
                    } call
                    ? call with {
                        ImportedGenericMethodCall = Remap(importedCall),
                    }
                    : expression);
        }
    }

    private static BoundGenericMethodImport RemapGenericMethodImport(
        BoundGenericMethodImport import, int referenceIndex,
        SpanTypeReferenceMerge merge,
        Dictionary<(BoundGenericMethodImport Import, int ReferenceIndex),
            BoundGenericMethodImport> cache,
        Dictionary<BoundGenericMethodSignature,
            BoundGenericMethodSignature> signatureCache,
        Dictionary<ImmutableArray<BoundGenericMethodFormal>,
            ImmutableArray<BoundGenericMethodFormal>> formalCache,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var key = (import, referenceIndex);
        if (cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var ownerMapping = merge.FormalOwnerMappings[referenceIndex];
        var formalOwnerOrdinal = RemapSpanFormalOwner(
            import.FormalTypeOwnerOrdinal, ownerMapping);
        if (!signatureCache.TryGetValue(import.Signature,
                out var signature))
        {
            signature = RemapGenericMethodSignature(import.Signature,
                rewriteType);
            signatureCache.Add(import.Signature, signature);
        }
        if (!formalCache.TryGetValue(import.Formals, out var formals))
        {
            formals = RemapGenericMethodFormals(import.Formals,
                rewriteType);
            formalCache.Add(import.Formals, formals);
        }
        var clone = new BoundGenericMethodImport(import.Origin,
            signature, formals, import.ProofFunction,
            formalOwnerOrdinal, merge.Types,
            import.OrdinaryParameterDefaults);
        cache.Add(key, clone);

        BoundGenericMethodImport? expected = null;
        BoundGenericMethodImport? provider = null;
        if (import.Binding is BoundGenericMethodImportBinding binding)
        {
            expected = RemapGenericMethodImport(binding.Expected,
                referenceIndex, merge, cache, signatureCache,
                formalCache, rewriteType);
            provider = RemapGenericMethodImport(binding.Provider,
                referenceIndex, merge, cache, signatureCache,
                formalCache, rewriteType);
        }

        var proofFunction = RemapGenericMethodImportFunction(
            import.ProofFunction, referenceIndex, merge, cache,
            signatureCache, formalCache, rewriteType);
        ImmutableArray<BoundFunction> proofSupportFunctions
            = import.ProofSupportFunctions.IsDefault
                ? [] : [.. import.ProofSupportFunctions.Select(function =>
                RemapGenericMethodImportFunction(function, referenceIndex,
                    merge, cache, signatureCache, formalCache,
                    rewriteType))];
        if (import.Binding is BoundGenericMethodImportBinding sourceBinding
            && expected is not null && provider is not null)
        {
            if (ReferenceEquals(import.ProofFunction.Body,
                    sourceBinding.Provider.ProofFunction.Body))
            {
                proofFunction = proofFunction with {
                    Body = provider.ProofFunction.Body,
                };
            }
            if (import.ProofSupportFunctions.SequenceEqual(
                    sourceBinding.Provider.ProofSupportFunctions))
            {
                proofSupportFunctions = provider.ProofSupportFunctions;
            }
        }

        clone.CompleteProofFunction(proofFunction, proofSupportFunctions,
            formalOwnerOrdinal);
        if (import.Binding is BoundGenericMethodImportBinding sourceBinding2
            && expected is not null && provider is not null)
        {
            clone.CompleteBinding(new BoundGenericMethodImportBinding(
                sourceBinding2.ImportRow, sourceBinding2.ModuleRefRow,
                sourceBinding2.ProviderIdentity, sourceBinding2.ExportKey,
                sourceBinding2.TargetRequirement,
                sourceBinding2.RuntimePolicy, expected, provider,
                [.. sourceBinding2.ExpectedInterfaceDefinitions.Select(
                    definition => RemapSpanTypes(definition,
                    rewriteType))]));
        }
        return clone;
    }

    private static int RemapSpanFormalOwner(int formalOwnerOrdinal,
        ImmutableArray<int> mapping)
    {
        if (formalOwnerOrdinal < 0)
        {
            return formalOwnerOrdinal;
        }
        if ((uint)formalOwnerOrdinal >= (uint)mapping.Length
            || mapping[formalOwnerOrdinal] < 0)
        {
            throw new ModuleContractException(
                "A generic MethodDef import has no merged Span formal owner.");
        }
        return mapping[formalOwnerOrdinal];
    }

    private static BoundFunction RemapGenericMethodImportFunction(
        BoundFunction function, int referenceIndex,
        SpanTypeReferenceMerge merge,
        Dictionary<(BoundGenericMethodImport Import, int ReferenceIndex),
            BoundGenericMethodImport> cache,
        Dictionary<BoundGenericMethodSignature,
            BoundGenericMethodSignature> signatureCache,
        Dictionary<ImmutableArray<BoundGenericMethodFormal>,
            ImmutableArray<BoundGenericMethodFormal>> formalCache,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var rewritten = BoundTreeTraversal.RewriteTypes(function,
            rewriteType);
        rewritten = rewritten with {
            ImportedGenericMethod = function.ImportedGenericMethod is
                BoundGenericMethodImport imported
                ? RemapGenericMethodImport(imported, referenceIndex,
                    merge, cache, signatureCache, formalCache,
                    rewriteType) : null,
        };
        return BoundTreeTraversal.RewriteExpressions(rewritten,
            expression => expression is BoundCall {
                    ImportedGenericMethodCall:
                        BoundGenericMethodImport importedCall,
                } call
                ? call with {
                    ImportedGenericMethodCall =
                        RemapGenericMethodImport(importedCall,
                            referenceIndex, merge, cache, signatureCache,
                            formalCache, rewriteType),
                }
                : expression);
    }

    private static BoundGenericMethodSignature
        RemapGenericMethodSignature(BoundGenericMethodSignature signature,
            Func<PrimitiveType, PrimitiveType> rewriteType) =>
        signature with {
            Owner = signature.Owner is BoundGenericMethodOwner owner
                ? BoundTreeTraversal.RewriteTypes(owner, rewriteType)
                : null,
            Formals = [.. signature.Formals.Select(formal => formal with {
                InterfaceConstraints = [.. formal.InterfaceConstraints
                    .Select(type => BoundTreeTraversal.RewriteTypes(type,
                        rewriteType))],
            })],
            ConstantFormals = [.. signature.ConstantFormals.Select(formal =>
                formal with {
                    Domain = RewriteGenericMethodTypeTerm(formal.Domain,
                        rewriteType),
                })],
        };

    private static ImmutableArray<BoundGenericMethodFormal>
        RemapGenericMethodFormals(
            ImmutableArray<BoundGenericMethodFormal> formals,
            Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var result = ImmutableArray.CreateBuilder<
            BoundGenericMethodFormal>(formals.Length);
        foreach (var formal in formals)
        {
            result.Add(formal with {
                TypeDefault = formal.TypeDefault is
                    BoundGenericMethodTypeDefault typeDefault
                    ? RewriteGenericMethodTypeDefault(typeDefault,
                        rewriteType) : null,
                InterfaceConstraints = [.. formal.InterfaceConstraints
                    .Select(type => BoundTreeTraversal.RewriteTypes(type,
                        rewriteType))],
            });
        }
        return result.MoveToImmutable();
    }

    private static BoundGenericMethodTypeDefault
        RewriteGenericMethodTypeDefault(
            BoundGenericMethodTypeDefault typeDefault,
            Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        if (!BoundTreeTraversal.TryRewriteGenericMethodTypeDefault(
                typeDefault, rewriteType, static ordinal => ordinal,
                static definition => definition,
                static identity => identity, out var rewritten))
        {
            throw new ModuleContractException(
                "A generic MethodDef type default cannot be remapped.");
        }
        return rewritten;
    }

    private static BoundGenericMethodTypeTerm
        RewriteGenericMethodTypeTerm(
            BoundGenericMethodTypeTerm typeTerm,
            Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        if (!BoundTreeTraversal.TryRewriteGenericMethodTypeTerm(
                typeTerm, rewriteType, static ordinal => ordinal,
                static definition => definition,
                static identity => identity, out var rewritten))
        {
            throw new ModuleContractException(
                "A generic MethodDef type term cannot be remapped.");
        }
        return rewritten;
    }

    private static BoundInterfaceDefinition RemapSpanTypes(
        BoundInterfaceDefinition definition,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        definition with {
            Formals = [.. definition.Formals.Select(formal =>
                BoundTreeTraversal.RewriteTypes(formal, rewriteType))],
            Bases = [.. definition.Bases.Select(type =>
                BoundTreeTraversal.RewriteTypes(type, rewriteType))],
            Constraints = [.. definition.Constraints.Select(constraint =>
                BoundTreeTraversal.RewriteTypes(constraint, rewriteType))],
            Members = [.. definition.Members.Select(member =>
                BoundTreeTraversal.RewriteTypes(member, rewriteType))],
        };
}
