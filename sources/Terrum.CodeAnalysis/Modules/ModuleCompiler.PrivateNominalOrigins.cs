// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private static ImmutableArray<PrimitiveType> PrivateNominalInputRoots(
        BoundProgram program, PrivateModuleIdentityInputs? inputs)
    {
        var result = ImmutableArray.CreateBuilder<PrimitiveType>();
        foreach (var origin in program.PrivateNominalTypeOrigins)
        {
            result.Add(origin.PrivateType);
        }
        if (inputs is not null)
        {
            foreach (var import in inputs.Imports)
            {
                result.Add(import.Type);
            }
            foreach (var definition in inputs.Definitions)
            {
                if (definition.IsExported)
                {
                    result.Add(definition.Type);
                }
            }
        }
        return [.. result.Distinct()];
    }

    private static ImmutableArray<BoundPrivateNominalTypeOrigin>
        BuildPrivateNominalTypeOrigins(
            BoundProgram sourceProgram, Module module,
            PrivateModuleIdentityInputs? inputs)
    {
        var hasExplicitInputs = inputs is not null
            && (!inputs.Definitions.IsEmpty || !inputs.Imports.IsEmpty
                || !inputs.EnumMembers.IsEmpty);
        var hasInheritedOrigins
            = !sourceProgram.PrivateNominalTypeOrigins.IsEmpty;
        var selectsRevision105 = !module.CheckedReferenceTypes.IsEmpty
            || module.Functions.Any(static item =>
                item.Function is BoundFunction function
                && function.Variables.Any(static variable =>
                    variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary))
            || module.NativeFunctionPointerSignatures.Any(
                static signature => signature is not null
                    && (signature.CallingConvention
                            != NativeCallingConvention.Native
                        || !NativeFunctionPointerContract
                            .CanPersistRevision92(signature)));
        if (!hasExplicitInputs && !hasInheritedOrigins
            && !selectsRevision105)
        {
            return [];
        }
        if (!hasExplicitInputs && !hasInheritedOrigins
            && module.Structures.IsEmpty && module.Classes.IsEmpty
            && !HasNominalCheckedReferenceTypeClosure(
                module.CheckedReferenceTypes, module.Structures,
                module.Classes, module.NativeFunctionPointerSignatures,
                module.NativePointerTypes, module.NativeFixedArrayTypes))
        {
            return [];
        }

        var inheritedOrigins = RemapPrivateNominalTypeOrigins(
            sourceProgram.PrivateNominalTypeOrigins,
            sourceProgram.Structures, module.Structures,
            sourceEnumDefinitions: sourceProgram.EnumDefinitions,
            destinationEnumDefinitions: module.EnumDefinitions);
        HashSet<PrimitiveType> explicitlyImportedEnumTypes = inputs is null
            ? []
            : [.. inputs.Imports.Where(static import =>
                    BoundEnumTypes.IsType(import.Type))
                .Select(import => RemapPrivateNominalType(import.Type,
                    sourceProgram.Structures, module.Structures,
                    sourceProgram.EnumDefinitions,
                    module.EnumDefinitions))];
        if (explicitlyImportedEnumTypes.Count != 0)
        {
            inheritedOrigins = [
                .. inheritedOrigins.Where(origin =>
                    !BoundEnumTypes.IsType(origin.PrivateType)
                    || !explicitlyImportedEnumTypes.Contains(
                        origin.PrivateType)),
            ];
        }
        var inheritedRoots = inheritedOrigins.Select(
            static origin => origin.PrivateType);
        var inputRoots = inputs is null
            ? []
            : inputs.Imports.Select(import => RemapPrivateNominalType(
                    import.Type, sourceProgram.Structures,
                    module.Structures, sourceProgram.EnumDefinitions,
                    module.EnumDefinitions))
                .Concat(inputs.Definitions
                    .Where(static definition => definition.IsExported)
                    .Select(definition => RemapPrivateNominalType(
                        definition.Type, sourceProgram.Structures,
                        module.Structures, sourceProgram.EnumDefinitions,
                        module.EnumDefinitions)));
        var requiredTypes = CollectPrivateNominalTypeClosure(module,
            inheritedRoots.Concat(inputRoots));
        var inheritedClassSnapshotTypes
            = PrivateClassSnapshotTypes(sourceProgram);
        if (inputs is null && requiredTypes.Any(type =>
                !inheritedOrigins.Any(origin =>
                    origin.PrivateType == type)))
        {
            throw new ModuleContractException(
                "A nominal private module requires explicit logical Module and declaration-key inputs.");
        }

        var origins = new List<BoundPrivateNominalTypeOrigin>(
            inheritedOrigins.Length + (inputs?.Definitions.Length ?? 0)
                + (inputs?.Imports.Length ?? 0));
        origins.AddRange(inheritedOrigins);

        if (inputs is not null)
        {
            foreach (var definition in inputs.Definitions)
            {
                var privateType = RemapPrivateNominalType(definition.Type,
                    sourceProgram.Structures, module.Structures,
                    sourceProgram.EnumDefinitions,
                    module.EnumDefinitions);
                if (!definition.IsExported
                    && !requiredTypes.Contains(privateType))
                {
                    continue;
                }
                AddOrigin(new BoundPrivateNominalTypeOrigin(privateType,
                    BoundPrivateNominalOriginKind.OwnedDefinition,
                    definition.IsExported, inputs.UsingModuleIdentity,
                    definition.LocalDeclarationKey));
            }

            foreach (var import in inputs.Imports)
            {
                var privateType = RemapPrivateNominalType(import.Type,
                    sourceProgram.Structures, module.Structures,
                    sourceProgram.EnumDefinitions,
                    module.EnumDefinitions);
                AddOrigin(new BoundPrivateNominalTypeOrigin(privateType,
                    BoundPrivateNominalOriginKind.ImportedExpectation,
                    false, inputs.UsingModuleIdentity,
                    import.LocalDeclarationKey,
                    import.SelectedModuleIdentity,
                    import.SelectedExportKey));
            }
        }

        var result = origins.ToImmutableArray();
        var completedModule = module with { NominalTypeOrigins = result };
        ValidatePrivateNominalTypeOrigins(completedModule,
            reading: false, requireCompleteClosure: true);

        if (inputs is not null)
        {
            foreach (var privateType in requiredTypes)
            {
                if (!result.Any(origin => origin.PrivateType == privateType
                    && origin.UsingModuleIdentity
                        == inputs.UsingModuleIdentity))
                {
                    if (inheritedClassSnapshotTypes.Contains(privateType)
                        && result.Any(origin =>
                            origin.PrivateType == privateType))
                    {
                        continue;
                    }
                    throw new ModuleContractException(
                        "A retained nominal closure lacks its explicit using-Module origin assignment.");
                }
            }
        }

        return result;

        void AddOrigin(BoundPrivateNominalTypeOrigin origin)
        {
            var existing = origins.FirstOrDefault(candidate =>
                candidate.PrivateType == origin.PrivateType
                && candidate.UsingModuleIdentity
                    == origin.UsingModuleIdentity);
            if (existing is not null)
            {
                if (existing != origin)
                {
                    throw new ModuleContractException(
                        "Private nominal identity inputs conflict with a retained origin.");
                }
                return;
            }
            origins.Add(origin);
        }
    }

    private static HashSet<PrimitiveType> PrivateClassSnapshotTypes(
        BoundProgram program)
    {
        var result = new HashSet<PrimitiveType>();
        foreach (var reference in program.PrivateClassReferences)
        {
            foreach (var origin in reference.ExpectedSnapshot
                .NominalTypeOrigins)
            {
                var type = origin.PrivateType;
                if (ManagedClassTypes.IsClass(type)
                    || (ManagedClassTypes.IsOptional(type)
                        && ManagedClassTypes.IsClass(
                            ManagedClassTypes.Required(type))))
                {
                    var index = ManagedClassTypes.ClassIndex(type);
                    if ((uint)index
                        < (uint)reference.ExpectedClassMapping.Length)
                    {
                        type = (PrimitiveType)(ManagedClassTypes.First
                            + reference.ExpectedClassMapping[index]);
                    }
                }
                else
                {
                    var index = (int)type - 100;
                    if (index >= 0
                        && index < BoundEnumTypes.First - 100
                        && (uint)index
                            < (uint)reference.ExpectedStructureMapping.Length)
                    {
                        type = (PrimitiveType)(100
                            + reference.ExpectedStructureMapping[index]);
                    }
                }
                _ = result.Add(type);
            }
        }
        return result;
    }

    private static HashSet<string> PrivateClassSnapshotOnlyStructureNames(
        Module module)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var references = CreatePrivateClassReferences(module.Structures,
            module.Classes, module.CheckedReferenceTypes,
            module.NativeFunctionPointerSignatures,
            module.NativePointerTypes, module.NativeFixedArrayTypes,
            module.NominalTypeOrigins, moduleFunctions: module.Functions,
            isCurrentSnapshot: true,
            enumDefinitions: module.EnumDefinitions);
        foreach (var reference in references)
        {
            foreach (var origin in reference.ExpectedSnapshot
                .NominalTypeOrigins)
            {
                if (origin.OriginKind
                        != BoundPrivateNominalOriginKind.OwnedDefinition
                    || origin.IsExported
                    || !IsStructureType(origin.PrivateType,
                        reference.ExpectedSnapshot.Structures))
                {
                    continue;
                }

                var sourceIndex = (int)origin.PrivateType - 100;
                if ((uint)sourceIndex
                    >= (uint)reference.ExpectedStructureMapping.Length)
                {
                    throw new ModuleContractException(
                        "A private class snapshot structure has no current mapping.");
                }

                var structureIndex
                    = reference.ExpectedStructureMapping[sourceIndex];
                if ((uint)structureIndex >= (uint)module.Structures.Length)
                {
                    throw new ModuleContractException(
                        "A private class snapshot structure mapping is out of range.");
                }

                var name = module.Structures[structureIndex].Name;
                if (!module.ImportedStructures.Contains(name,
                    StringComparer.Ordinal))
                {
                    _ = result.Add(name);
                }
            }
        }

        return result;
    }

    private static PrimitiveType RemapPrivateNominalType(
        PrimitiveType type, ImmutableArray<BoundStruct> sourceStructures,
        IReadOnlyList<BoundStruct> destinationStructures,
        ImmutableArray<BoundEnumDefinition> sourceEnumDefinitions,
        IReadOnlyList<BoundEnumDefinition>? destinationEnumDefinitions)
    {
        if (ManagedClassTypes.IsClass(type)
            || ManagedClassTypes.IsOptional(type))
        {
            var classIndex = ManagedClassTypes.ClassIndex(type);
            return (PrimitiveType)(ManagedClassTypes.First + classIndex);
        }
        if (BoundEnumTypes.IsType(type))
        {
            var sourceIndex = BoundEnumTypes.Index(type);
            if ((uint)sourceIndex
                    >= (uint)sourceEnumDefinitions.Length
                || destinationEnumDefinitions is null)
            {
                throw new ModuleContractException(
                    "Private nominal enum origin has no source definition.");
            }
            var sourceDefinition = sourceEnumDefinitions[sourceIndex];
            var matches = destinationEnumDefinitions.Select(
                    (definition, index) => (definition, index))
                .Where(item => item.definition is not null
                    && BoundEnumTypes.AreEquivalent(sourceDefinition,
                        item.definition))
                .Select(static item => item.index)
                .ToArray();
            if (matches.Length != 1)
            {
                throw new ModuleContractException(
                    "Private nominal enum origin has no unique remapped definition.");
            }
            return BoundEnumTypes.Type(matches[0]);
        }
        return RemapType(type, sourceStructures, destinationStructures);
    }

    private static Module ProjectPrivateEnumDefinitions(Module module)
    {
        var sourceDefinitions = module.EnumDefinitions;
        if (sourceDefinitions.IsDefaultOrEmpty)
        {
            if (module.NominalTypeOrigins.Any(static origin =>
                    BoundEnumTypes.IsType(origin.PrivateType)))
            {
                throw new ModuleContractException(
                    "Private enum origins require a retained enum definition.");
            }
            return module with { EnumDefinitions = [] };
        }

        var mapping = new int[sourceDefinitions.Length];
        Array.Fill(mapping, -1);
        var definitions = ImmutableArray.CreateBuilder<BoundEnumDefinition>();
        foreach (var origin in module.NominalTypeOrigins)
        {
            if (!BoundEnumTypes.IsType(origin.PrivateType))
            {
                continue;
            }

            var sourceIndex = BoundEnumTypes.Index(origin.PrivateType);
            if ((uint)sourceIndex >= (uint)sourceDefinitions.Length
                || mapping[sourceIndex] >= 0)
            {
                throw new ModuleContractException(
                    "Private enum origins require distinct retained type rows.");
            }
            var destinationType = BoundEnumTypes.Type(
                definitions.Count);
            mapping[sourceIndex] = BoundEnumTypes.Index(destinationType);
            var sourceDefinition = sourceDefinitions[sourceIndex];
            definitions.Add(sourceDefinition.Identity
                is BoundEnumDeclarationIdentity.Private
                    ? CreatePrivateEnumDefinition(sourceDefinition,
                        origin with { PrivateType = destinationType })
                    : sourceDefinition);
        }

        return RemapModuleEnumTypes(module, [.. mapping],
            definitions.ToImmutable());
    }

    private static BoundEnumDefinition CreatePrivateEnumDefinition(
        BoundEnumDefinition definition,
        BoundPrivateNominalTypeOrigin origin)
        => BoundEnumDefinition.CreatePrivate(origin, definition.Name,
            definition.UnderlyingType, [
                .. definition.Members.Select(static member =>
                    new BoundPrivateEnumMemberSnapshot(member.Name,
                        member.Value, member.IsPublic,
                        member.DeclarationKey)),
            ], definition.IsPublic, definition.IsPrimary,
            definition.IsFlags, definition.BlittableTargetRequirement,
            definition.ModuleImport);

    private static BoundEnumDefinition RemapPrivateEnumDefinition(
        BoundEnumDefinition definition, PrimitiveType privateType)
    {
        try
        {
            return BoundEnumDefinition.RemapPrivateOccurrence(definition,
                privateType);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException)
        {
            throw new ModuleContractException(
                "A private enum occurrence cannot be remapped without preserving its complete expectation.",
                exception);
        }
    }

    private static bool SamePrivateEnumContractShape(
        BoundEnumDefinition left, BoundEnumDefinition right)
    {
        return left.Name == right.Name
            && left.UnderlyingType == right.UnderlyingType
            && left.IsPublic == right.IsPublic
            && left.IsPrimary == right.IsPrimary
            && left.IsFlags == right.IsFlags
            && left.BlittableTargetRequirement
                == right.BlittableTargetRequirement
            && left.Members.Length == right.Members.Length
            && left.Members.Zip(right.Members).All(
                static pair => pair.First.Name == pair.Second.Name
                    && pair.First.Value == pair.Second.Value
                    && pair.First.IsPublic == pair.Second.IsPublic
                    && pair.First.DeclarationKey
                        == pair.Second.DeclarationKey);
    }

    private static ImmutableArray<int> CreatePrivateEnumTypeMapping(
        ImmutableArray<BoundEnumDefinition> sourceDefinitions,
        ImmutableArray<BoundEnumDefinition> destinationDefinitions)
    {
        var mapping = ImmutableArray.CreateBuilder<int>(
            sourceDefinitions.Length);
        foreach (var source in sourceDefinitions)
        {
            var matches = destinationDefinitions.Select(
                    (definition, index) => (definition, index))
                .Where(item => SameEnumIdentity(source,
                    item.definition))
                .Select(static item => item.index)
                .ToArray();
            if (matches.Length > 1)
            {
                throw new ModuleContractException(
                    "A private enum type has multiple remapped directory entries.");
            }
            mapping.Add(matches.Length == 0 ? -1 : matches[0]);
        }
        return mapping.MoveToImmutable();

        static bool SameEnumIdentity(BoundEnumDefinition left,
            BoundEnumDefinition right)
        {
            if (left.Identity is BoundEnumDeclarationIdentity.Core
                    leftCore
                && right.Identity
                    is BoundEnumDeclarationIdentity.Core rightCore)
            {
                return leftCore.Value == rightCore.Value
                    && BoundEnumTypes.AreEquivalent(left, right);
            }
            if (left.Identity
                    is BoundEnumDeclarationIdentity.Private leftPrivate
                && right.Identity
                    is BoundEnumDeclarationIdentity.Private rightPrivate)
            {
                var leftOrigin = leftPrivate.Value.Origin;
                var rightOrigin = rightPrivate.Value.Origin;
                var sameOrigin = (leftOrigin with {
                        PrivateType = default,
                    }) == (rightOrigin with {
                        PrivateType = default,
                    });
                var sourceModule
                    = leftOrigin.OriginKind
                            == BoundPrivateNominalOriginKind.OwnedDefinition
                        && leftOrigin.IsExported
                        ? leftOrigin.UsingModuleIdentity
                        : leftOrigin.OriginKind
                            == BoundPrivateNominalOriginKind.ImportedExpectation
                            ? leftOrigin.SelectedModuleIdentity : null;
                var sourceExport
                    = leftOrigin.OriginKind
                            == BoundPrivateNominalOriginKind.OwnedDefinition
                        && leftOrigin.IsExported
                        ? leftOrigin.LocalDeclarationKey
                        : leftOrigin.OriginKind
                            == BoundPrivateNominalOriginKind.ImportedExpectation
                            ? leftOrigin.SelectedExportKey : null;
                var importsSelectedOrigin
                    = rightOrigin.OriginKind
                            == BoundPrivateNominalOriginKind
                                .ImportedExpectation
                        && sourceModule is not null
                        && sourceExport is not null
                        && rightOrigin.SelectedModuleIdentity
                            == sourceModule
                        && rightOrigin.SelectedExportKey == sourceExport;
                return (sameOrigin || importsSelectedOrigin)
                    && SamePrivateEnumContractShape(left, right);
            }
            return false;
        }
    }

    private static BoundEnumDefinition PrivateEnumExpectation(
        BoundEnumDefinition definition)
        => definition.Identity
            is BoundEnumDeclarationIdentity.Private privateIdentity
            ? privateIdentity.Value.ExpectedSnapshot
            : definition;

    private static ImmutableArray<BoundEnumDefinition>
        MergePrivateEnumDefinitions(
            IReadOnlyList<CompilationReference> references,
            out ImmutableArray<ImmutableArray<int>> mappings)
    {
        var definitions = ImmutableArray.CreateBuilder<BoundEnumDefinition>();
        var resultMappings
            = ImmutableArray.CreateBuilder<ImmutableArray<int>>(
                references.Count);
        var privateOwners = new Dictionary<(string Module, string Key),
            (BoundPrivateNominalTypeOrigin Origin,
                BoundEnumDefinition Definition, int Index)>();
        var coreIdentities = new Dictionary<CoreDeclarationIdentity,
            (BoundEnumDefinition Definition, int Index)>();
        var selectedOwners = new Dictionary<(string Module, string Key),
            (BoundPrivateNominalTypeOrigin Origin,
                BoundEnumDefinition Definition)>();
        for (var referenceIndex = 0;
            referenceIndex < references.Count;
            referenceIndex++)
        {
            var reference = references[referenceIndex];
            for (var sourceIndex = 0;
                sourceIndex < reference.EnumDefinitions.Length;
                sourceIndex++)
            {
                var definition = reference.EnumDefinitions[sourceIndex];
                if (definition.Identity
                        is not BoundEnumDeclarationIdentity.Private
                            privateIdentity)
                {
                    continue;
                }

                var origin = privateIdentity.Value.Origin;
                if (origin.OriginKind
                        != BoundPrivateNominalOriginKind.OwnedDefinition
                    || !origin.IsExported)
                {
                    continue;
                }

                var owner = (origin.UsingModuleIdentity,
                    origin.LocalDeclarationKey);
                if (selectedOwners.TryGetValue(owner,
                        out var existing)
                    && ((existing.Origin with {
                            PrivateType = default,
                        }) != (origin with { PrivateType = default })
                        || !SamePrivateEnumContract(
                            existing.Definition, definition)))
                {
                    throw new ModuleContractException(
                        "Selected private enum providers disagree on an exported identity.");
                }
                _ = selectedOwners.TryAdd(owner, (origin, definition));
            }
        }

        foreach (var reference in references)
        {
            var mapping = ImmutableArray.CreateBuilder<int>(
                reference.EnumDefinitions.Length);
            for (var sourceIndex = 0;
                sourceIndex < reference.EnumDefinitions.Length;
                sourceIndex++)
            {
                var definition = reference.EnumDefinitions[sourceIndex];
                var sourceType = BoundEnumTypes.Type(sourceIndex);
                var matchingOrigins = reference.PrivateNominalTypeOrigins
                    .Where(candidate =>
                        candidate.PrivateType == sourceType)
                    .ToArray();
                if (matchingOrigins.Length > 1)
                {
                    throw new ModuleContractException(
                        "A private enum type has multiple origin bindings in one reference.");
                }
                var origin = matchingOrigins
                    .SingleOrDefault();
                if (definition.Identity
                    is BoundEnumDeclarationIdentity.Private
                        privateIdentity)
                {
                    if (origin is null
                        || origin != privateIdentity.Value.Origin)
                    {
                        throw new ModuleContractException(
                            "A private enum reference does not retain its exact origin binding.");
                    }

                    if (origin.OriginKind
                            == BoundPrivateNominalOriginKind
                                .ImportedExpectation
                        && origin.SelectedModuleIdentity is string selectedModule
                        && origin.SelectedExportKey is string selectedExport
                        && selectedOwners.TryGetValue((selectedModule,
                            selectedExport),
                            out var selectedOwner))
                    {
                        if (!SamePrivateEnumContract(definition,
                                selectedOwner.Definition))
                        {
                            throw new ModuleContractException(
                                "Selected private enum provider differs from the original expected declaration.");
                        }

                        var selectedOwnerKey = (
                            selectedOwner.Origin.UsingModuleIdentity,
                            selectedOwner.Origin.LocalDeclarationKey);
                        if (!privateOwners.TryGetValue(selectedOwnerKey,
                                out var mergedOwner))
                        {
                            var selectedDestinationIndex
                                = definitions.Count;
                            if (selectedDestinationIndex
                                >= BoundEnumTypes.MaximumCount)
                            {
                                throw new ModuleContractException(
                                    "The merged enum declarations exceed the private type identity range.");
                            }
                            definitions.Add(RemapPrivateEnumDefinition(
                                selectedOwner.Definition,
                                BoundEnumTypes.Type(
                                    selectedDestinationIndex)));
                            mergedOwner = (selectedOwner.Origin,
                                selectedOwner.Definition,
                                selectedDestinationIndex);
                            privateOwners.Add(selectedOwnerKey,
                                mergedOwner);
                        }
                        else if ((mergedOwner.Origin with {
                                PrivateType = default,
                            }) != (selectedOwner.Origin with {
                                PrivateType = default,
                            })
                            || !SamePrivateEnumContract(
                                mergedOwner.Definition,
                                selectedOwner.Definition))
                        {
                            throw new ModuleContractException(
                                "Selected private enum provider has conflicting owner snapshots.");
                        }
                        mapping.Add(mergedOwner.Index);
                        continue;
                    }

                    var owner = (origin.UsingModuleIdentity,
                        origin.LocalDeclarationKey);
                    if (privateOwners.TryGetValue(owner,
                            out var existing))
                    {
                        if ((existing.Origin with {
                                PrivateType = default,
                            }) != (origin with { PrivateType = default })
                            || !SamePrivateEnumContract(
                                existing.Definition, definition))
                        {
                            throw new ModuleContractException(
                                "Repeated private enum identities have conflicting original expectations.");
                        }
                        mapping.Add(existing.Index);
                        continue;
                    }

                    var destinationIndex = definitions.Count;
                    if (destinationIndex >= BoundEnumTypes.MaximumCount)
                    {
                        throw new ModuleContractException(
                            "The merged enum declarations exceed the private type identity range.");
                    }
                    definitions.Add(RemapPrivateEnumDefinition(
                        definition,
                        BoundEnumTypes.Type(destinationIndex)));
                    privateOwners.Add(owner,
                        (origin, definition, destinationIndex));
                    mapping.Add(destinationIndex);
                    continue;
                }

                if (definition.Identity.TryGetCore(
                        out var coreIdentity))
                {
                    if (coreIdentities.TryGetValue(coreIdentity,
                            out var existing))
                    {
                        if (!BoundEnumTypes.AreEquivalent(
                                existing.Definition, definition))
                        {
                            throw new ModuleContractException(
                                "Repeated Core enum identities have conflicting declarations.");
                        }
                        mapping.Add(existing.Index);
                        continue;
                    }

                    var destinationIndex = definitions.Count;
                    if (destinationIndex >= BoundEnumTypes.MaximumCount)
                    {
                        throw new ModuleContractException(
                            "The merged enum declarations exceed the private type identity range.");
                    }
                    coreIdentities.Add(coreIdentity,
                        (definition, destinationIndex));
                    definitions.Add(definition);
                    mapping.Add(destinationIndex);
                    continue;
                }

                throw new ModuleContractException(
                    "A merged enum declaration has no supported identity carrier.");
            }
            resultMappings.Add(mapping.MoveToImmutable());
        }

        mappings = resultMappings.MoveToImmutable();
        return definitions.ToImmutable();

        static bool SamePrivateEnumContract(
            BoundEnumDefinition left, BoundEnumDefinition right)
        {
            return SamePrivateEnumContractShape(left, right);
        }
    }

    private static ImmutableArray<BoundPrivateClassReference>
        CreatePrivateClassReferences(
            ImmutableArray<BoundStruct> structures,
            ImmutableArray<BoundClass> classes,
            ImmutableArray<CheckedReferenceTypeDefinition>
                checkedReferenceTypes,
            ImmutableArray<NativeFunctionPointerSignatureDefinition>
                functionPointerSignatures,
            ImmutableArray<NativePointerTypeDefinition> pointerTypes,
            ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
            ImmutableArray<BoundPrivateNominalTypeOrigin> origins,
            ImmutableArray<int> classMapping = default,
            ImmutableArray<PrimitiveType> nativePointerMapping = default,
            ImmutableArray<PrimitiveType> functionPointerMapping = default,
            ImmutableArray<PrimitiveType> fixedArrayMapping = default,
            ImmutableArray<int> checkedReferenceMapping = default,
            ImmutableArray<ModuleFunction> moduleFunctions = default,
            bool isCurrentSnapshot = false,
            ImmutableArray<BoundEnumDefinition> enumDefinitions = default,
            ImmutableArray<int> enumMapping = default)
    {
        var snapshot = new BoundPrivateClassSnapshot(structures, classes,
            checkedReferenceTypes, functionPointerSignatures, pointerTypes,
            fixedArrayTypes, origins,
            enumDefinitions.IsDefault
                ? []
                : enumDefinitions);
        var implementedClassNames = moduleFunctions.IsDefaultOrEmpty
            ? new HashSet<string>(StringComparer.Ordinal)
            : moduleFunctions.Where(static item =>
                    item.Function is BoundFunction function
                    && (function.IsClassConstructor
                        || function.ClassConstructor >= 0)
                    && function.DeclaringStruct is not null)
                .Select(static item => item.Function!.DeclaringStruct!)
                .ToHashSet(StringComparer.Ordinal);
        var expectedNativePointerMapping = MapTypeRows(
            nativePointerMapping, pointerTypes.Length,
            NativePointerTypes.IsType, NativePointerTypes.Index,
            "native-pointer");
        var expectedFunctionPointerMapping = MapTypeRows(
            functionPointerMapping, functionPointerSignatures.Length,
            NativeFunctionPointerTypes.IsType,
            NativeFunctionPointerTypes.Index, "function-pointer");
        var expectedFixedArrayMapping = MapTypeRows(fixedArrayMapping,
            fixedArrayTypes.Length, NativeFixedArrayTypes.IsType,
            NativeFixedArrayTypes.Index, "fixed-array");
        var expectedEnumMapping = enumMapping.IsDefault
            ? enumDefinitions.IsEmpty
                ? []
                : [.. Enumerable.Range(0, enumDefinitions.Length)]
            : enumMapping;
        if (expectedEnumMapping.Length != enumDefinitions.Length
            || expectedEnumMapping.Any(static index => index < -1))
        {
            throw new ModuleContractException(
                "Private class reference has an inconsistent enum mapping.");
        }
        var expectedClassMapping = classMapping.IsDefault
            ? [.. Enumerable.Range(0, classes.Length)]
            : classMapping;
        var expectedCheckedReferenceMapping = checkedReferenceMapping.IsDefault
            ? [.. Enumerable.Range(0, checkedReferenceTypes.Length)]
            : checkedReferenceMapping;
        if (expectedCheckedReferenceMapping.Length
            != checkedReferenceTypes.Length
            || expectedCheckedReferenceMapping.Any(static index =>
                index < -1))
        {
            throw new ModuleContractException(
                "Private class reference has an inconsistent checked-reference mapping.");
        }
        var references = ImmutableArray.CreateBuilder<
            BoundPrivateClassReference>();
        foreach (var origin in origins)
        {
            if (!ManagedClassTypes.IsClass(origin.PrivateType)
                || (origin.OriginKind
                        == BoundPrivateNominalOriginKind.OwnedDefinition
                    && !origin.IsExported))
            {
                continue;
            }
            if ((uint)ManagedClassTypes.ClassIndex(origin.PrivateType)
                >= (uint)classes.Length)
            {
                throw new ModuleContractException(
                    "Private class reference has no class definition.");
            }
            if (!classMapping.IsDefaultOrEmpty
                && classMapping[ManagedClassTypes.ClassIndex(
                    origin.PrivateType)] < 0)
            {
                continue;
            }
            references.Add(new BoundPrivateClassReference(
                RemapManagedClassType(origin.PrivateType, classMapping),
                origin with {
                    PrivateType = RemapManagedClassType(
                        origin.PrivateType, classMapping),
                }, snapshot, origin.PrivateType,
                origin.OriginKind
                    == BoundPrivateNominalOriginKind.OwnedDefinition
                    && origin.IsExported
                    && implementedClassNames.Contains(
                        classes[ManagedClassTypes.ClassIndex(
                            origin.PrivateType)].Name),
                isCurrentSnapshot
                    ? [.. Enumerable.Range(0, structures.Length)] : [],
                expectedClassMapping,
                expectedNativePointerMapping,
                expectedFunctionPointerMapping,
                expectedFixedArrayMapping,
                expectedCheckedReferenceMapping,
                expectedEnumMapping));
        }
        return references.ToImmutable();

        static ImmutableArray<int> MapTypeRows(
            ImmutableArray<PrimitiveType> mapping, int count,
            Func<PrimitiveType, bool> isType,
            Func<PrimitiveType, int> getIndex, string family)
        {
            if (mapping.IsDefault)
            {
                return [.. Enumerable.Range(0, count)];
            }
            if (mapping.Length != count)
            {
                throw new ModuleContractException(
                    $"Private class reference has an inconsistent {family} mapping.");
            }
            return [.. mapping.Select(type => isType(type)
                ? getIndex(type) : -1)];
        }
    }

    private static ImmutableArray<PrimitiveType>
        PublicStructureNominalRoots(
            ImmutableArray<BoundPrivateNominalTypeOrigin> origins,
            ImmutableArray<BoundStruct> structures)
    {
        var roots = ImmutableArray.CreateBuilder<PrimitiveType>();
        foreach (var origin in origins)
        {
            if (origin.OriginKind
                    != BoundPrivateNominalOriginKind.OwnedDefinition
                || !origin.IsExported
                || !IsStructureType(origin.PrivateType, structures))
            {
                continue;
            }
            roots.Add(origin.PrivateType);
        }
        return [.. roots.Distinct()];
    }

    private static ImmutableArray<PrimitiveType>
        PublicEnumNominalRoots(
            ImmutableArray<BoundPrivateNominalTypeOrigin> origins,
            ImmutableArray<BoundEnumDefinition> enumDefinitions)
    {
        var roots = ImmutableArray.CreateBuilder<PrimitiveType>();
        foreach (var origin in origins)
        {
            if (origin.OriginKind
                    != BoundPrivateNominalOriginKind.OwnedDefinition
                || !origin.IsExported
                || !BoundEnumTypes.IsType(origin.PrivateType)
                || (uint)BoundEnumTypes.Index(origin.PrivateType)
                    >= (uint)enumDefinitions.Length)
            {
                continue;
            }
            roots.Add(origin.PrivateType);
        }
        return [.. roots.Distinct()];
    }

    private static ImmutableArray<BoundPrivateNominalTypeOrigin>
        ProjectPrivateNominalTypeOrigins(
            ImmutableArray<BoundPrivateNominalTypeOrigin> origins,
            ImmutableArray<BoundStruct> sourceStructures,
            ImmutableArray<BoundStruct> destinationStructures,
            ImmutableArray<int> classMapping,
            ImmutableArray<BoundEnumDefinition> sourceEnumDefinitions = default,
            IReadOnlyList<BoundEnumDefinition>?
                destinationEnumDefinitions = null,
            ImmutableArray<int> enumMapping = default)
    {
        var destinationStructureNames = destinationStructures
            .Select(static structure => structure.Name)
            .ToHashSet(StringComparer.Ordinal);
        var retained = ImmutableArray.CreateBuilder<
            BoundPrivateNominalTypeOrigin>();
        foreach (var origin in origins)
        {
            if (ManagedClassTypes.IsClass(origin.PrivateType))
            {
                var classIndex = ManagedClassTypes.ClassIndex(
                    origin.PrivateType);
                if ((uint)classIndex < (uint)classMapping.Length
                    && classMapping[classIndex] >= 0)
                {
                    retained.Add(origin);
                }
                continue;
            }
            if (BoundEnumTypes.IsType(origin.PrivateType))
            {
                var enumIndex = BoundEnumTypes.Index(origin.PrivateType);
                if (!enumMapping.IsDefault
                    ? (uint)enumIndex < (uint)enumMapping.Length
                        && enumMapping[enumIndex] >= 0
                    : (uint)enumIndex
                            < (uint)sourceEnumDefinitions.Length
                        && destinationEnumDefinitions is not null
                        && destinationEnumDefinitions.Any(definition =>
                            BoundEnumTypes.AreEquivalent(
                                sourceEnumDefinitions[enumIndex],
                                definition)))
                {
                    retained.Add(origin);
                }
                continue;
            }
            var typeIndex = (int)origin.PrivateType - 100;
            if ((uint)typeIndex < (uint)sourceStructures.Length
                && destinationStructureNames.Contains(
                    sourceStructures[typeIndex].Name))
            {
                retained.Add(origin);
            }
        }
        return RemapPrivateNominalTypeOrigins(retained.ToImmutable(),
            sourceStructures, destinationStructures,
            classMapping: classMapping,
            sourceEnumDefinitions: sourceEnumDefinitions,
            destinationEnumDefinitions: destinationEnumDefinitions,
            enumMapping: enumMapping);
    }

    private static ImmutableArray<BoundClass>
        ProjectPrivateClassDefinitions(
            ImmutableArray<BoundClass> sourceClasses,
            ImmutableArray<BoundPrivateNominalTypeOrigin> origins,
            out ImmutableArray<int> classMapping)
    {
        var mapping = new int[sourceClasses.Length];
        Array.Fill(mapping, -1);
        var selected = origins.Where(static origin =>
                ManagedClassTypes.IsClass(origin.PrivateType))
            .Select(static origin =>
                ManagedClassTypes.ClassIndex(origin.PrivateType))
            .Distinct()
            .Order()
            .ToArray();
        if (selected.Length > 256)
        {
            throw new ModuleContractException(
                "Private class reference projection exceeds its type identity range.");
        }

        var classes = ImmutableArray.CreateBuilder<BoundClass>(
            selected.Length);
        foreach (var sourceIndex in selected)
        {
            if ((uint)sourceIndex >= (uint)sourceClasses.Length)
            {
                throw new ModuleContractException(
                    "Private class origin has no definition in its snapshot.");
            }
            mapping[sourceIndex] = classes.Count;
            classes.Add(sourceClasses[sourceIndex]);
        }
        classMapping = [.. mapping];
        return classes.MoveToImmutable();
    }

    private static ImmutableArray<BoundPrivateClassReference>
        RemapPrivateClassReferences(
            ImmutableArray<BoundPrivateClassReference> references,
            ImmutableArray<BoundStruct> sourceStructures,
            IReadOnlyList<BoundStruct> destinationStructures,
            ImmutableArray<int> classMapping,
            Dictionary<string, PrimitiveType>? destinationTypes
                    = null,
            ImmutableArray<PrimitiveType> nativePointerMapping = default,
            ImmutableArray<PrimitiveType> functionPointerMapping = default,
            ImmutableArray<PrimitiveType> fixedArrayMapping = default,
            ImmutableArray<int> checkedReferenceMapping = default,
            ImmutableArray<int> enumMapping = default)
    {
        if (references.IsDefaultOrEmpty)
        {
            return [];
        }
        var remapped = ImmutableArray.CreateBuilder<
            BoundPrivateClassReference>(references.Length);
        foreach (var reference in references)
        {
            var origin = RemapPrivateNominalTypeOrigins(
                [reference.Origin], sourceStructures,
                destinationStructures, destinationTypes,
                classMapping)[0];
            var expectedStructureMapping
                = reference.ExpectedStructureMapping;
            if (!expectedStructureMapping.IsDefaultOrEmpty
                && destinationTypes is not null)
            {
                var mappedStructures
                    = ImmutableArray.CreateBuilder<int>(
                        expectedStructureMapping.Length);
                foreach (var sourceIndex in expectedStructureMapping)
                {
                    if ((uint)sourceIndex >= (uint)sourceStructures.Length
                        || !destinationTypes.TryGetValue(
                            sourceStructures[sourceIndex].Name,
                            out var mappedType))
                    {
                        throw new ModuleContractException(
                            "A private class snapshot structure has no remapped type.");
                    }
                    mappedStructures.Add((int)mappedType - 100);
                }
                expectedStructureMapping
                    = mappedStructures.MoveToImmutable();
            }
            var expectedClassMapping
                = reference.ExpectedClassMapping;
            if (!expectedClassMapping.IsDefaultOrEmpty)
            {
                var mappedClasses = ImmutableArray.CreateBuilder<int>(
                    expectedClassMapping.Length);
                foreach (var sourceIndex in expectedClassMapping)
                {
                    if (sourceIndex < 0)
                    {
                        mappedClasses.Add(-1);
                        continue;
                    }
                    if ((uint)sourceIndex
                        >= (uint)classMapping.Length
                        || classMapping[sourceIndex] < 0)
                    {
                        throw new ModuleContractException(
                            "A private class snapshot has no remapped class.");
                    }
                    mappedClasses.Add(classMapping[sourceIndex]);
                }
                expectedClassMapping = mappedClasses.MoveToImmutable();
            }
            var expectedNativePointerMapping
                = reference.ExpectedNativePointerMapping;
            if (!nativePointerMapping.IsDefault
                && !expectedNativePointerMapping.IsDefaultOrEmpty)
            {
                var mappedPointers = ImmutableArray.CreateBuilder<int>(
                    expectedNativePointerMapping.Length);
                foreach (var sourceIndex in expectedNativePointerMapping)
                {
                    if (sourceIndex < 0)
                    {
                        mappedPointers.Add(-1);
                        continue;
                    }
                    if ((uint)sourceIndex >= (uint)nativePointerMapping.Length
                        || !NativePointerTypes.IsType(
                            nativePointerMapping[sourceIndex]))
                    {
                        throw new ModuleContractException(
                            "A private class snapshot pointer has no remapped type.");
                    }
                    mappedPointers.Add(NativePointerTypes.Index(
                        nativePointerMapping[sourceIndex]));
                }
                expectedNativePointerMapping
                    = mappedPointers.MoveToImmutable();
            }
            var expectedFunctionPointerMapping
                = RemapTypeRows(
                    reference.ExpectedNativeFunctionPointerMapping,
                    functionPointerMapping,
                    NativeFunctionPointerTypes.IsType,
                    NativeFunctionPointerTypes.Index,
                    "function-pointer");
            var expectedFixedArrayMapping = RemapTypeRows(
                reference.ExpectedNativeFixedArrayMapping,
                fixedArrayMapping, NativeFixedArrayTypes.IsType,
                NativeFixedArrayTypes.Index, "fixed-array");
            var expectedCheckedReferenceMapping
                = RemapIndexRows(
                    reference.ExpectedCheckedReferenceMapping,
                    checkedReferenceMapping, "checked-reference");
            var expectedEnumMapping = RemapIndexRows(
                reference.ExpectedEnumMapping, enumMapping, "enum");
            remapped.Add(reference with {
                ClassType = RemapManagedClassType(reference.ClassType,
                    classMapping),
                Origin = origin,
                ExpectedStructureMapping
                    = expectedStructureMapping,
                ExpectedClassMapping = expectedClassMapping,
                ExpectedNativePointerMapping
                    = expectedNativePointerMapping,
                ExpectedNativeFunctionPointerMapping
                    = expectedFunctionPointerMapping,
                ExpectedNativeFixedArrayMapping
                    = expectedFixedArrayMapping,
                ExpectedCheckedReferenceMapping
                    = expectedCheckedReferenceMapping,
                ExpectedEnumMapping = expectedEnumMapping,
            });
        }
        return remapped.MoveToImmutable();

        static ImmutableArray<int> RemapTypeRows(
            ImmutableArray<int> expectedMapping,
            ImmutableArray<PrimitiveType> destinationMapping,
            Func<PrimitiveType, bool> isType,
            Func<PrimitiveType, int> getIndex, string family)
        {
            if (destinationMapping.IsDefault
                || expectedMapping.IsDefaultOrEmpty)
            {
                return expectedMapping;
            }
            var mapped = ImmutableArray.CreateBuilder<int>(
                expectedMapping.Length);
            foreach (var sourceIndex in expectedMapping)
            {
                if (sourceIndex < 0)
                {
                    mapped.Add(-1);
                    continue;
                }
                if ((uint)sourceIndex >= (uint)destinationMapping.Length
                    || !isType(destinationMapping[sourceIndex]))
                {
                    throw new ModuleContractException(
                        $"A private class snapshot {family} has no remapped type.");
                }
                mapped.Add(getIndex(destinationMapping[sourceIndex]));
            }
            return mapped.MoveToImmutable();
        }

        static ImmutableArray<int> RemapIndexRows(
            ImmutableArray<int> expectedMapping,
            ImmutableArray<int> destinationMapping, string family)
        {
            if (destinationMapping.IsDefault
                || expectedMapping.IsDefaultOrEmpty)
            {
                return expectedMapping;
            }
            var mapped = ImmutableArray.CreateBuilder<int>(
                expectedMapping.Length);
            foreach (var sourceIndex in expectedMapping)
            {
                if (sourceIndex < 0)
                {
                    mapped.Add(-1);
                    continue;
                }
                if ((uint)sourceIndex >= (uint)destinationMapping.Length
                    || destinationMapping[sourceIndex] < 0)
                {
                    throw new ModuleContractException(
                        $"A private class snapshot {family} type has no remapped type.");
                }
                mapped.Add(destinationMapping[sourceIndex]);
            }
            return mapped.MoveToImmutable();
        }
    }

    private static ImmutableArray<PrimitiveType>
        CollectPrivateNominalTypeClosure(Module module,
            IEnumerable<PrimitiveType>? additionalRoots = null)
    {
        var nominalTypes = new HashSet<PrimitiveType>();
        var visitedTypes = new HashSet<PrimitiveType>();
        foreach (var referenceType in module.CheckedReferenceTypes)
        {
            AddType(referenceType.ReferentType);
        }
        foreach (var origin in module.NominalTypeOrigins)
        {
            AddType(origin.PrivateType);
        }
        if (additionalRoots is not null)
        {
            foreach (var root in additionalRoots)
            {
                AddType(root);
            }
        }

        foreach (var item in module.Functions)
        {
            if (item.Function is not BoundFunction function)
            {
                continue;
            }
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                    expression => {
                        if (expression is BoundPrivateReferenceForm form)
                        {
                            AddPlace(form.Place);
                        }
                        else if (expression is BoundPrivatePlaceRead read)
                        {
                            AddPlace(read.Place);
                        }
                        else if (expression
                            is BoundPrivatePlaceIsInitialized initialized)
                        {
                            AddPlace(initialized.Place);
                        }
                        else if (expression
                            is BoundPrivateCheckedStore store)
                        {
                            AddPlace(store.Destination);
                        }
                        else if (expression
                            is BoundNativeStorageAddress address)
                        {
                            AddType(address.CarrierType);
                            AddType(address.ElementType);
                            foreach (var projection in address.Projections)
                            {
                                AddType(projection.Layout.CarrierType);
                                AddType(projection.Layout.ElementType);
                            }
                        }
                        else if (expression
                            is BoundNativeFixedArrayPathElement path)
                        {
                            foreach (var projection in path.Projections)
                            {
                                AddType(projection.Layout.CarrierType);
                                AddType(projection.Layout.ElementType);
                            }
                        }
                        else if (expression
                            is BoundNativeFixedArrayPathWrite pathWrite)
                        {
                            foreach (var projection in pathWrite.Projections)
                            {
                                AddType(projection.Layout.CarrierType);
                                AddType(projection.Layout.ElementType);
                            }
                        }
                    }))
            {
                throw new ModuleFormatException(
                    "Private nominal closure contains an invalid Body.");
            }
        }

        return [.. nominalTypes.OrderBy(static type => (int)type)];

        void AddPlace(BoundPrivatePlace place)
        {
            AddType(place.Type);
            switch (place)
            {
                case BoundPrivateInlineFieldPlace field:
                    AddType(field.OwnerType);
                    AddPlace(field.Receiver);
                    break;
                case BoundPrivateObjectFieldPlace field:
                    AddType(field.OwnerType);
                    AddType(field.Receiver.Type);
                    break;
                case BoundPrivateReferentPlace referent:
                    AddType(referent.Descriptor.Type);
                    break;
                case BoundPrivateFixedArrayElementPlace element:
                    AddType(element.ArrayType);
                    AddPlace(element.ArrayPlace);
                    break;
                case BoundPrivateInvocationBufferElementPlace buffer:
                    AddType(buffer.Index.Type);
                    break;
                case BoundPrivateLexicalViewElementPlace view:
                    AddType(view.Index.Type);
                    break;
            }
        }

        void AddType(PrimitiveType type)
        {
            if (!visitedTypes.Add(type))
            {
                return;
            }
            if (NativePointerTypes.IsType(type))
            {
                var index = NativePointerTypes.Index(type);
                if ((uint)index >= (uint)module.NativePointerTypes.Length)
                {
                    throw new ModuleFormatException(
                        "Private nominal closure references an absent pointer type.");
                }
                AddType(module.NativePointerTypes[index].PointeeType);
                return;
            }
            if (NativeFunctionPointerTypes.IsType(type))
            {
                var index = NativeFunctionPointerTypes.Index(type);
                if ((uint)index
                    >= (uint)module.NativeFunctionPointerSignatures.Length)
                {
                    throw new ModuleFormatException(
                        "Private nominal closure references an absent function-pointer type.");
                }
                var signature
                    = module.NativeFunctionPointerSignatures[index];
                AddType(signature.ReturnType);
                foreach (var parameter in signature.ParameterTypes)
                {
                    AddType(parameter);
                }
                return;
            }
            if (NativeFixedArrayTypes.IsType(type))
            {
                var index = NativeFixedArrayTypes.Index(type);
                if ((uint)index
                    >= (uint)module.NativeFixedArrayTypes.Length)
                {
                    throw new ModuleFormatException(
                        "Private nominal closure references an absent fixed-array type.");
                }
                AddType(module.NativeFixedArrayTypes[index].ElementType);
                return;
            }
            if (CheckedReferenceTypeContract.IsValidType(type,
                    module.CheckedReferenceTypes))
            {
                AddType(module.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(type)].ReferentType);
                return;
            }
            if (BoundEnumTypes.IsType(type))
            {
                if (!BoundEnumTypes.IsValidType(type,
                        module.EnumDefinitions))
                {
                    throw new ModuleFormatException(
                        "Private nominal closure references an absent enum definition.");
                }
                _ = nominalTypes.Add(type);
                return;
            }
            if (ManagedClassTypes.IsClass(type)
                || ManagedClassTypes.IsOptional(type))
            {
                var classIndex = ManagedClassTypes.ClassIndex(type);
                if ((uint)classIndex >= (uint)module.Classes.Length)
                {
                    throw new ModuleFormatException(
                        "Private nominal closure references an absent class.");
                }
                var canonicalType = (PrimitiveType)(
                    ManagedClassTypes.First + classIndex);
                if (nominalTypes.Add(canonicalType))
                {
                    if (module.Classes.Length > 256)
                    {
                        throw new ModuleFormatException(
                            "Private nominal class-origin closure exceeds 256 classes.");
                    }
                    foreach (var field in module.Classes[classIndex].Fields)
                    {
                        AddType(field.Type);
                    }
                }
                return;
            }
            if (type == PrimitiveType.TokenSpan)
            {
                var tokenIndex = TokenSpanContract.Find(module.Structures,
                    TokenSpanContract.TokenName);
                if (tokenIndex >= 0)
                {
                    AddType((PrimitiveType)(100 + tokenIndex));
                }
                return;
            }

            var typeValue = (int)type;
            var structureIndex = typeValue >= 100
                ? typeValue - 100 : -1;
            if (structureIndex < 0
                || (uint)structureIndex >= (uint)module.Structures.Length)
            {
                return;
            }
            if (nominalTypes.Add(type))
            {
                foreach (var field in module.Structures[structureIndex].Fields)
                {
                    AddType(field.Type);
                }
            }
        }
    }

    private static void ValidatePrivateNominalTypeOrigins(Module module,
        bool reading, bool requireCompleteClosure)
    {
        if (reading && module.FormatVersion != CheckedReferenceVersion
            && module.NominalTypeOrigins.IsEmpty)
        {
            return;
        }
        if (module.NominalTypeOrigins.IsEmpty
            && module.Structures.IsEmpty && module.Classes.IsEmpty
            && !HasNominalCheckedReferenceTypeClosure(
                module.CheckedReferenceTypes, module.Structures,
                module.Classes, module.NativeFunctionPointerSignatures,
                module.NativePointerTypes, module.NativeFixedArrayTypes))
        {
            return;
        }
        var failure = reading
            ? (Action<string>)(message => throw new ModuleFormatException(message))
            : message => throw new ModuleContractException(message);
        if (module.NominalTypeOrigins.IsDefault
            || module.NominalTypeOrigins.Length > MaximumNodes)
        {
            failure("Private nominal-type origin table is invalid.");
            return;
        }

        var seenTypes = new HashSet<(string Module, PrimitiveType Type)>();
        var localKeys = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);
        foreach (var origin in module.NominalTypeOrigins)
        {
            if (!PrivateModuleIdentityInputs.IsValidModuleIdentity(
                    origin.UsingModuleIdentity)
                || !PrivateModuleIdentityInputs.IsValidDeclarationKey(
                    origin.LocalDeclarationKey)
                || !seenTypes.Add((origin.UsingModuleIdentity,
                    origin.PrivateType)))
            {
                failure("Private nominal-type origin has invalid or duplicate Binding identity.");
                return;
            }

            if (!localKeys.TryGetValue(origin.UsingModuleIdentity,
                    out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                localKeys.Add(origin.UsingModuleIdentity, keys);
            }
            if (!keys.Add(origin.LocalDeclarationKey))
            {
                failure("Private nominal declaration keys collide within a using Module.");
                return;
            }

            if (origin.OriginKind
                == BoundPrivateNominalOriginKind.OwnedDefinition)
            {
                if (origin.SelectedModuleIdentity is not null
                    || origin.SelectedExportKey is not null
                    || !IsNominalTypeAvailable(module, origin.PrivateType,
                        requirePublic: origin.IsExported))
                {
                    failure("Owned private nominal origin has an invalid declaration or export.");
                    return;
                }
            }
            else if (origin.OriginKind
                    == BoundPrivateNominalOriginKind.ImportedExpectation)
            {
                if (origin.IsExported
                    || !PrivateModuleIdentityInputs.IsValidModuleIdentity(
                        origin.SelectedModuleIdentity)
                    || !PrivateModuleIdentityInputs.IsValidDeclarationKey(
                        origin.SelectedExportKey)
                    || !IsNominalTypeAvailable(module, origin.PrivateType,
                        requirePublic: false))
                {
                    failure("Imported private nominal origin has an invalid proxy or selected export.");
                    return;
                }
            }
            else
            {
                failure("Private nominal origin has an unknown Binding kind.");
                return;
            }
        }

        if (!requireCompleteClosure)
        {
            return;
        }
        var closure = CollectPrivateNominalTypeClosure(module);
        foreach (var type in closure)
        {
            if (!module.NominalTypeOrigins.Any(origin =>
                    origin.PrivateType == type))
            {
                failure($"Private nominal closure lacks its required origin Binding for '{type}'.");
                return;
            }
        }

        if (!module.Consumer)
        {
            var exportedFunctions = module.Functions
                .Where(static function => function.Import)
                .Select(static function => function.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (module.NominalTypeOrigins.Any(origin =>
                    origin.IsExported
                    && exportedFunctions.Contains(
                        origin.LocalDeclarationKey)))
            {
                failure("Private nominal type export collides with a function export.");
            }
        }
    }

    private static void ValidateSelectedPrivateNominalImports(
        Module consumer, Module provider,
        PrivateModuleIdentityInputs? inputs)
    {
        if (inputs is null)
        {
            ValidatePrivateEnumImports(consumer, provider);
            return;
        }

        foreach (var import in inputs.Imports)
        {
            var privateType = RemapPrivateNominalType(import.Type,
                inputs.Program.Structures, consumer.Structures,
                inputs.Program.EnumDefinitions,
                consumer.EnumDefinitions);
            if (!consumer.NominalTypeOrigins.Any(origin =>
                    origin.PrivateType == privateType
                    && origin.OriginKind
                        == BoundPrivateNominalOriginKind.ImportedExpectation
                    && origin.UsingModuleIdentity
                        == inputs.UsingModuleIdentity
                    && origin.LocalDeclarationKey
                        == import.LocalDeclarationKey
                    && origin.SelectedModuleIdentity
                        == import.SelectedModuleIdentity
                    && origin.SelectedExportKey
                        == import.SelectedExportKey))
            {
                throw new ModuleContractException(
                    "Consumer nominal import lost its original Binding expectation.");
            }

            var providers = provider.NominalTypeOrigins.Where(origin =>
                origin.OriginKind
                    == BoundPrivateNominalOriginKind.OwnedDefinition
                && origin.IsExported
                && origin.UsingModuleIdentity
                    == import.SelectedModuleIdentity
                && origin.LocalDeclarationKey
                    == import.SelectedExportKey
                && SameNominalKind(privateType, origin.PrivateType))
                .ToArray();
            if (providers.Length != 1)
            {
                throw new ModuleContractException(
                    "Selected nominal provider does not publish the exact owned type export.");
            }

            if (IsStructureType(privateType, consumer.Structures)
                && IsStructureType(providers[0].PrivateType,
                    provider.Structures))
            {
                var expectedStructure = consumer.Structures[
                    (int)privateType - 100];
                var actualStructure = provider.Structures[
                    (int)providers[0].PrivateType - 100];
                if (!SameStructure(expectedStructure, actualStructure,
                        consumer.Structures, provider.Structures,
                        consumer.NativeFunctionPointerSignatures,
                        provider.NativeFunctionPointerSignatures,
                        consumer.NativePointerTypes,
                        provider.NativePointerTypes,
                        consumer.NativeFixedArrayTypes,
                        provider.NativeFixedArrayTypes,
                        consumer.Classes, provider.Classes))
                {
                    throw new ModuleContractException(
                        "Selected nominal provider differs from the original expected struct.");
                }
            }
            else if (BoundEnumTypes.IsType(privateType)
                && BoundEnumTypes.IsType(providers[0].PrivateType))
            {
                var expectedIndex = BoundEnumTypes.Index(privateType);
                var actualIndex = BoundEnumTypes.Index(
                    providers[0].PrivateType);
                if ((uint)expectedIndex
                        >= (uint)consumer.EnumDefinitions.Length
                    || (uint)actualIndex
                        >= (uint)provider.EnumDefinitions.Length
                    || !SamePrivateEnumContractShape(
                        PrivateEnumExpectation(
                            consumer.EnumDefinitions[expectedIndex]),
                        provider.EnumDefinitions[actualIndex]))
                {
                    throw new ModuleContractException(
                        "Selected nominal provider differs from the original expected enum.");
                }
            }
            else if (ManagedClassTypes.IsClass(privateType))
            {
                var expectedReferences = inputs.Program
                    .PrivateClassReferences.Where(reference =>
                        reference.ClassType == import.Type
                        && reference.IsSourceLookupRoot
                        && reference.Origin.UsingModuleIdentity
                            == import.SelectedModuleIdentity
                        && reference.Origin.LocalDeclarationKey
                            == import.SelectedExportKey)
                    .ToArray();
                var expectedClassIndex = expectedReferences.Length == 1
                    ? ManagedClassTypes.ClassIndex(
                        expectedReferences[0].ExpectedClassType) : -1;
                var providerClassIndex = ManagedClassTypes.ClassIndex(
                    providers[0].PrivateType);
                if (expectedReferences.Length != 1
                    || expectedClassIndex < 0
                    || (uint)expectedClassIndex
                        >= (uint)expectedReferences[0]
                            .ExpectedSnapshot.Classes.Length
                    || (uint)providerClassIndex
                        >= (uint)provider.Classes.Length
                    || !SameClass(expectedReferences[0].ExpectedSnapshot
                            .Classes[expectedClassIndex],
                        provider.Classes[providerClassIndex],
                        expectedReferences[0].ExpectedSnapshot.Structures,
                        provider.Structures,
                        expectedReferences[0].ExpectedSnapshot.Classes,
                        provider.Classes,
                        expectedReferences[0].ExpectedSnapshot
                            .NativeFunctionPointerSignatures,
                        provider.NativeFunctionPointerSignatures,
                        expectedReferences[0].ExpectedSnapshot
                            .CheckedReferenceTypes,
                        provider.CheckedReferenceTypes,
                        expectedReferences[0].ExpectedSnapshot
                            .NativePointerTypes,
                        provider.NativePointerTypes,
                        expectedReferences[0].ExpectedSnapshot
                            .NativeFixedArrayTypes,
                        provider.NativeFixedArrayTypes,
                        expectedReferences[0].ExpectedSnapshot
                            .NominalTypeOrigins,
                        provider.NominalTypeOrigins,
                        expectedReferences[0].ExpectedSnapshot
                            .EnumDefinitions,
                        provider.EnumDefinitions))
                {
                    throw new ModuleContractException(
                        "Selected nominal provider differs from the original expected class.");
                }
            }
            else
            {
                throw new ModuleContractException(
                    "Selected nominal import kind is not admitted by the current private reader.");
            }
        }

        ValidatePrivateEnumImports(consumer, provider);
    }

    private static void ValidatePrivateEnumImports(Module consumer,
        Module provider)
    {
        foreach (var import in consumer.NominalTypeOrigins.Where(
                static origin => origin.OriginKind
                    == BoundPrivateNominalOriginKind.ImportedExpectation
                && BoundEnumTypes.IsType(origin.PrivateType)))
        {
            var matches = provider.NominalTypeOrigins.Where(origin =>
                origin.OriginKind
                    == BoundPrivateNominalOriginKind.OwnedDefinition
                && origin.IsExported
                && origin.UsingModuleIdentity
                    == import.SelectedModuleIdentity
                && origin.LocalDeclarationKey
                    == import.SelectedExportKey
                && BoundEnumTypes.IsType(origin.PrivateType))
                .ToArray();
            if (matches.Length != 1
                || !BoundEnumTypes.IsValidType(import.PrivateType,
                    consumer.EnumDefinitions)
                || !BoundEnumTypes.IsValidType(matches[0].PrivateType,
                    provider.EnumDefinitions))
            {
                throw new ModuleContractException(
                    "Selected nominal provider does not publish the exact owned enum export.");
            }

            var expected = PrivateEnumExpectation(
                consumer.EnumDefinitions[
                    BoundEnumTypes.Index(import.PrivateType)]);
            var actual = provider.EnumDefinitions[
                BoundEnumTypes.Index(matches[0].PrivateType)];
            if (!SamePrivateEnumContractShape(expected, actual))
            {
                throw new ModuleContractException(
                    "Selected nominal provider differs from the original expected enum.");
            }
        }
    }

    private static void ValidatePrivateNominalImportsAgainstReference(
        Module consumer, CompilationReference provider)
    {
        foreach (var import in consumer.NominalTypeOrigins.Where(
                static origin => origin.OriginKind
                    == BoundPrivateNominalOriginKind.ImportedExpectation))
        {
            var matches = provider.PrivateNominalTypeOrigins.Where(origin =>
                origin.OriginKind
                    == BoundPrivateNominalOriginKind.OwnedDefinition
                && origin.IsExported
                && origin.UsingModuleIdentity
                    == import.SelectedModuleIdentity
                && origin.LocalDeclarationKey
                    == import.SelectedExportKey
                && SameNominalKind(import.PrivateType,
                    origin.PrivateType))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new ModuleContractException(
                    "Selected nominal provider does not publish the exact owned type export.");
            }

            if (IsStructureType(import.PrivateType, consumer.Structures)
                && IsStructureType(matches[0].PrivateType,
                    provider.Structures))
            {
                var expectedStructure = consumer.Structures[
                    (int)import.PrivateType - 100];
                var actualStructure = provider.Structures[
                    (int)matches[0].PrivateType - 100];
                if (!SameStructure(expectedStructure, actualStructure,
                        consumer.Structures, provider.Structures,
                        consumer.NativeFunctionPointerSignatures,
                        provider.NativeFunctionPointerSignatures,
                        consumer.NativePointerTypes, provider.NativePointerTypes,
                        consumer.NativeFixedArrayTypes,
                        provider.NativeFixedArrayTypes))
                {
                    throw new ModuleContractException(
                        "Selected nominal provider differs from the original expected struct.");
                }
            }
            else if (BoundEnumTypes.IsType(import.PrivateType)
                && BoundEnumTypes.IsType(matches[0].PrivateType))
            {
                if (!BoundEnumTypes.IsValidType(import.PrivateType,
                        consumer.EnumDefinitions)
                    || !BoundEnumTypes.IsValidType(matches[0].PrivateType,
                        provider.EnumDefinitions)
                    || !SamePrivateEnumContractShape(
                        PrivateEnumExpectation(consumer.EnumDefinitions[
                            BoundEnumTypes.Index(import.PrivateType)]),
                        provider.EnumDefinitions[BoundEnumTypes.Index(
                            matches[0].PrivateType)]))
                {
                    throw new ModuleContractException(
                        "Selected nominal provider differs from the original expected enum.");
                }
            }
            else if (ManagedClassTypes.IsClass(import.PrivateType)
                && ManagedClassTypes.IsClass(matches[0].PrivateType))
            {
                var expectedClassIndex = ManagedClassTypes.ClassIndex(
                    import.PrivateType);
                var actualClassReferences =
                    provider.PrivateClassReferences.Where(reference =>
                        reference.IsSourceLookupRoot
                        && reference.Origin.UsingModuleIdentity
                            == import.SelectedModuleIdentity
                        && reference.Origin.LocalDeclarationKey
                            == import.SelectedExportKey
                        && reference.Origin.PrivateType
                            == matches[0].PrivateType)
                    .ToArray();
                var actualClassIndex =
                    actualClassReferences.Length == 1
                        ? ManagedClassTypes.ClassIndex(
                            actualClassReferences[0].ExpectedClassType)
                        : -1;
                if ((uint)expectedClassIndex
                        >= (uint)consumer.Classes.Length
                    || actualClassReferences.Length != 1
                    || actualClassIndex < 0
                    || (uint)actualClassIndex
                        >= (uint)actualClassReferences[0]
                            .ExpectedSnapshot.Classes.Length
                    || !SameClass(consumer.Classes[expectedClassIndex],
                        actualClassReferences[0].ExpectedSnapshot
                            .Classes[actualClassIndex],
                        consumer.Structures,
                        actualClassReferences[0].ExpectedSnapshot
                            .Structures,
                        consumer.Classes,
                        actualClassReferences[0].ExpectedSnapshot.Classes,
                        consumer.NativeFunctionPointerSignatures,
                        actualClassReferences[0].ExpectedSnapshot
                            .NativeFunctionPointerSignatures,
                        consumer.CheckedReferenceTypes,
                        actualClassReferences[0].ExpectedSnapshot
                            .CheckedReferenceTypes,
                        consumer.NativePointerTypes,
                        actualClassReferences[0].ExpectedSnapshot
                            .NativePointerTypes,
                        consumer.NativeFixedArrayTypes,
                        actualClassReferences[0].ExpectedSnapshot
                            .NativeFixedArrayTypes,
                        consumer.NominalTypeOrigins,
                        actualClassReferences[0].ExpectedSnapshot
                            .NominalTypeOrigins,
                        consumer.EnumDefinitions,
                        actualClassReferences[0].ExpectedSnapshot
                            .EnumDefinitions))
                {
                    throw new ModuleContractException(
                        "Selected nominal provider differs from the original expected class.");
                }
            }
            else
            {
                throw new ModuleContractException(
                    "Selected nominal import kind is not admitted by the current private reader.");
            }
        }
    }

    private static bool SameNominalKind(PrimitiveType left,
        PrimitiveType right) =>
        NominalKind(left) == NominalKind(right);

    private static byte NominalKind(PrimitiveType type)
    {
        if (BoundEnumTypes.IsType(type))
        {
            return 3;
        }
        if (ManagedClassTypes.IsClass(type)
            || ManagedClassTypes.IsOptional(type))
        {
            return 2;
        }
        var typeValue = (int)type;
        return typeValue is >= 100 and < BoundEnumTypes.First ? (byte)1 : (byte)0;
    }

    private static bool IsStructureType(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures)
    {
        var typeValue = (int)type;
        var structureIndex = typeValue >= 100
            ? typeValue - 100 : -1;
        return structureIndex >= 0
            && (uint)structureIndex < (uint)structures.Count
            && !BoundEnumTypes.IsType(type)
            && !ManagedClassTypes.IsManaged(type);
    }

    private static bool IsNominalTypeAvailable(Module module,
        PrimitiveType type, bool requirePublic)
    {
        if (BoundEnumTypes.IsType(type))
        {
            if (!BoundEnumTypes.IsValidType(type,
                    module.EnumDefinitions))
            {
                return false;
            }
            return !requirePublic
                || module.EnumDefinitions[BoundEnumTypes.Index(type)]
                    .IsPublic;
        }
        var typeValue = (int)type;
        var structureIndex = typeValue >= 100
            ? typeValue - 100 : -1;
        if (structureIndex >= 0
            && (uint)structureIndex < (uint)module.Structures.Length)
        {
            var structure = module.Structures[structureIndex];
            return !requirePublic
                || (!structure.IsPrivate && !structure.IsInternal);
        }
        if (ManagedClassTypes.IsClass(type)
            || ManagedClassTypes.IsOptional(type))
        {
            var classIndex = ManagedClassTypes.ClassIndex(type);
            return type == (PrimitiveType)(ManagedClassTypes.First
                    + classIndex)
                && (uint)classIndex < (uint)module.Classes.Length
                && (!requirePublic || !module.Classes[classIndex].IsInternal);
        }
        return false;
    }

    private static void WritePrivateNominalEnumDefinitions(
        BinaryWriter writer, Module module)
    {
        if (writer is not ModuleWriter {
                FormatVersion: CheckedReferenceVersion,
            })
        {
            if (!module.EnumDefinitions.IsDefaultOrEmpty)
            {
                throw new ModuleContractException(
                    "Private nominal enums require the selected revision 106 schema.");
            }
            return;
        }

        if (!BoundEnumTypes.IsValidTable(module.EnumDefinitions)
            || module.EnumDefinitions.Length > BoundEnumTypes.MaximumCount
            || (!module.EnumDefinitions.IsEmpty
                && module.Structures.Length
                    > BoundEnumTypes.First - 100))
        {
            throw new ModuleContractException(
                "Private nominal enum table is invalid.");
        }

        var enumOrigins = module.NominalTypeOrigins
            .Select((origin, index) => (origin, index))
            .Where(static item => BoundEnumTypes.IsType(
                item.origin.PrivateType))
            .ToArray();
        var totalMembers = module.EnumDefinitions.Sum(
            static definition => (long)definition.Members.Length);
        if (enumOrigins.Length != module.EnumDefinitions.Length
            || totalMembers + enumOrigins.Length > MaximumNodes)
        {
            throw new ModuleContractException(
                "Private nominal enum declarations lack a complete bounded origin mapping.");
        }

        writer.Write(module.EnumDefinitions.Length);
        for (var index = 0; index < module.EnumDefinitions.Length; index++)
        {
            var definition = module.EnumDefinitions[index];
            var expectedType = BoundEnumTypes.Type(index);
            var matchingOrigins = enumOrigins.Where(item =>
                item.origin.PrivateType == expectedType).ToArray();
            if (matchingOrigins.Length != 1
                || enumOrigins[index].origin.PrivateType != expectedType)
            {
                throw new ModuleContractException(
                    "Private nominal enum directory is not in canonical origin order.");
            }

            var originIndex = matchingOrigins[0].index;
            writer.Write(checked((uint)originIndex + 1));
            PutString(writer, definition.Name);
            writer.Write((int)PrimitiveType.Int32);
            writer.Write(definition.IsFlags ? (byte)1 : (byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)0);
            writer.Write(definition.BlittableTargetRequirement);
            writer.Write(definition.Members.Length);
            foreach (var member in definition.Members)
            {
                if (!PrivateModuleIdentityInputs.IsValidEnumMemberKey(
                        member.DeclarationKey))
                {
                    throw new ModuleContractException(
                        "Private nominal enum member key is invalid.");
                }
                PutString(writer, member.DeclarationKey);
                PutString(writer, member.Name);
                writer.Write(member.Value);
            }
        }
    }

    private static void WritePrivateNominalTypeOrigins(BinaryWriter writer,
        Module module)
    {
        if (writer is not ModuleWriter {
                FormatVersion: CheckedReferenceVersion,
            })
        {
            if (!module.NominalTypeOrigins.IsDefaultOrEmpty)
            {
                throw new ModuleContractException(
                    "Private nominal origins require the selected revision 106 schema.");
            }
            return;
        }

        ValidatePrivateNominalTypeOrigins(module, reading: false,
            requireCompleteClosure: true);
        writer.Write(module.NominalTypeOrigins.Length);
        foreach (var origin in module.NominalTypeOrigins)
        {
            writer.Write((int)origin.PrivateType);
            writer.Write((byte)origin.OriginKind);
            writer.Write(origin.IsExported);
            writer.Write((ushort)0);
            PutString(writer, origin.UsingModuleIdentity);
            PutString(writer, origin.LocalDeclarationKey);
            if (origin.OriginKind
                == BoundPrivateNominalOriginKind.ImportedExpectation)
            {
                PutString(writer, origin.SelectedModuleIdentity!);
                PutString(writer, origin.SelectedExportKey!);
            }
        }
    }

    private static ImmutableArray<BoundPrivateNominalTypeOrigin>
        ReadPrivateNominalTypeOrigins(BinaryReader reader)
    {
        if (reader is not ModuleReader {
                FormatVersion: CheckedReferenceVersion,
            })
        {
            return [];
        }

        var count = Count(reader, MaximumNodes);
        var origins = ImmutableArray.CreateBuilder<
            BoundPrivateNominalTypeOrigin>(count);
        for (var index = 0; index < count; index++)
        {
            var privateType = (PrimitiveType)reader.ReadInt32();
            var originKind = (BoundPrivateNominalOriginKind)reader.ReadByte();
            var isExported = GetBool(reader);
            var reserved = reader.ReadUInt16();
            var usingModuleIdentity = GetString(reader);
            var localDeclarationKey = GetString(reader);
            string? selectedModuleIdentity = null;
            string? selectedExportKey = null;
            if (originKind
                == BoundPrivateNominalOriginKind.ImportedExpectation)
            {
                selectedModuleIdentity = GetString(reader);
                selectedExportKey = GetString(reader);
            }
            if (reserved != 0
                || !PrivateModuleIdentityInputs.IsValidModuleIdentity(
                    usingModuleIdentity)
                || !PrivateModuleIdentityInputs.IsValidDeclarationKey(
                    localDeclarationKey))
            {
                throw new ModuleFormatException(
                    "Private nominal origin has malformed reserved or Binding identity data.");
            }

            origins.Add(new BoundPrivateNominalTypeOrigin(privateType,
                originKind, isExported, usingModuleIdentity,
                localDeclarationKey, selectedModuleIdentity,
                selectedExportKey));
        }

        return origins.MoveToImmutable();
    }
}
