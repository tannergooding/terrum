using System;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Identifies the nominal declaration kind selected by qualified name.</summary>
public enum PrivateNominalDeclarationKind
{
    /// <summary>An inline structure declaration.</summary>
    Struct,
    /// <summary>A managed class declaration.</summary>
    Class,
    /// <summary>A Core-backed or retained private enum declaration.</summary>
    Enum,
}

/// <summary>Selects one checked nominal declaration in a bound-program snapshot.</summary>
/// <param name="Kind">The nominal declaration kind.</param>
/// <param name="QualifiedName">The exact namespace-qualified declaration name.</param>
public sealed record PrivateNominalDeclarationSelector(
    PrivateNominalDeclarationKind Kind, string QualifiedName);

/// <summary>Assigns a local identity to a selected owned nominal declaration.</summary>
/// <param name="Declaration">The checked declaration selector.</param>
/// <param name="LocalDeclarationKey">The explicit local declaration key.</param>
/// <param name="IsExported">Whether the declaration is exported by this module.</param>
public sealed record PrivateNominalDefinitionSelection(
    PrivateNominalDeclarationSelector Declaration,
    string LocalDeclarationKey, bool IsExported);

/// <summary>Assigns a local proxy and selected provider identity to a declaration.</summary>
/// <param name="Declaration">The checked declaration selector.</param>
/// <param name="LocalDeclarationKey">The explicit local proxy key.</param>
/// <param name="SelectedModuleIdentity">The selected provider module identity.</param>
/// <param name="SelectedExportKey">The exact selected provider export key.</param>
public sealed record PrivateNominalImportSelection(
    PrivateNominalDeclarationSelector Declaration,
    string LocalDeclarationKey, string SelectedModuleIdentity,
    string SelectedExportKey);

/// <summary>Assigns an explicit Core member identity to a selected enum declaration.</summary>
/// <param name="Owner">The checked enum selector.</param>
/// <param name="EnumIdentity">The exact Core identity of the enum.</param>
/// <param name="MemberOrdinal">The member's ordinal in the checked declaration.</param>
/// <param name="MemberIdentity">The exact Core identity of the member.</param>
/// <param name="MemberDeclarationKey">The explicit retained member key.</param>
public sealed record PrivateEnumMemberSelection(
    PrivateNominalDeclarationSelector Owner,
    CoreDeclarationIdentity EnumIdentity,
    int MemberOrdinal,
    CoreDeclarationIdentity MemberIdentity,
    string MemberDeclarationKey);

public sealed partial class PrivateModuleIdentityInputs
{
    /// <summary>
    /// Resolves explicit declaration selectors in one checked program snapshot
    /// and captures the resulting private-module identity inputs.
    /// </summary>
    /// <param name="program">The exact bound-program snapshot to select from.</param>
    /// <param name="usingModuleIdentity">The logical identity of the using module.</param>
    /// <param name="definitions">Owned declarations and their explicit local keys.</param>
    /// <param name="imports">Imported declarations and their explicit provider keys.</param>
    /// <param name="enumMembers">Complete Core enum-member identity assignments.</param>
    /// <param name="sourceEnumDefinitions">Complete source-enum identity and member-key assignments.</param>
    /// <returns>Validated identity inputs associated with <paramref name="program"/>.</returns>
    /// <exception cref="ArgumentException">
    /// A selector is invalid, ambiguous, stale, or inconsistent with checked
    /// declaration ownership or provider provenance.
    /// </exception>
    public static PrivateModuleIdentityInputs CreateFromSelectors(
        BoundProgram program,
        string usingModuleIdentity,
        IReadOnlyList<PrivateNominalDefinitionSelection> definitions,
        IReadOnlyList<PrivateNominalImportSelection> imports,
        IReadOnlyList<PrivateEnumMemberSelection> enumMembers,
        IReadOnlyList<PrivateSourceEnumDefinitionAssignment>
            sourceEnumDefinitions)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(usingModuleIdentity);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(imports);
        ArgumentNullException.ThrowIfNull(enumMembers);
        ArgumentNullException.ThrowIfNull(sourceEnumDefinitions);
        if (!IsValidModuleIdentity(usingModuleIdentity))
        {
            throw new ArgumentException(
                "The logical private Module identity is invalid.",
                nameof(usingModuleIdentity));
        }

        var resolvedDefinitions
            = new PrivateNominalDefinition[definitions.Count];
        for (var index = 0; index < definitions.Count; index++)
        {
            var selection = definitions[index];
            if (selection is null || selection.Declaration is null)
            {
                throw new ArgumentException(
                    "A private nominal definition selector is null.",
                    nameof(definitions));
            }

            var type = ResolveDeclaration(program, selection.Declaration,
                false, usingModuleIdentity, selection.LocalDeclarationKey,
                null, null);
            resolvedDefinitions[index] = new PrivateNominalDefinition(type,
                selection.LocalDeclarationKey, selection.IsExported);
        }

        var resolvedImports = new PrivateNominalImport[imports.Count];
        for (var index = 0; index < imports.Count; index++)
        {
            var selection = imports[index];
            if (selection is null || selection.Declaration is null)
            {
                throw new ArgumentException(
                    "A private nominal import selector is null.",
                    nameof(imports));
            }

            var type = ResolveDeclaration(program, selection.Declaration,
                true, usingModuleIdentity, null,
                selection.SelectedModuleIdentity,
                selection.SelectedExportKey);
            resolvedImports[index] = new PrivateNominalImport(type,
                selection.LocalDeclarationKey,
                selection.SelectedModuleIdentity,
                selection.SelectedExportKey);
        }

        var resolvedEnumMembers
            = new PrivateEnumMemberAssignment[enumMembers.Count];
        for (var index = 0; index < enumMembers.Count; index++)
        {
            var selection = enumMembers[index];
            if (selection is null || selection.Owner is null
                || selection.Owner.Kind
                    != PrivateNominalDeclarationKind.Enum)
            {
                throw new ArgumentException(
                    "A Core enum-member assignment requires an enum owner selector.",
                    nameof(enumMembers));
            }

            var enumType = ResolveCoreEnum(program, selection.Owner,
                selection.EnumIdentity);
            resolvedEnumMembers[index] = new PrivateEnumMemberAssignment(
                enumType, selection.EnumIdentity, selection.MemberOrdinal,
                selection.MemberIdentity, selection.MemberDeclarationKey);
        }

        return Create(program, usingModuleIdentity, resolvedDefinitions,
            resolvedImports, resolvedEnumMembers, sourceEnumDefinitions);
    }

    private static PrimitiveType ResolveDeclaration(BoundProgram program,
        PrivateNominalDeclarationSelector selector, bool isImport,
        string usingModuleIdentity, string? localDeclarationKey,
        string? selectedModuleIdentity, string? selectedExportKey)
    {
        if (selector is null
            || string.IsNullOrWhiteSpace(selector.QualifiedName))
        {
            throw new ArgumentException(
                "A private nominal declaration selector is invalid.",
                nameof(selector));
        }

        return selector.Kind switch {
            PrivateNominalDeclarationKind.Struct => ResolveStructure(program,
                selector, isImport, selectedModuleIdentity,
                selectedExportKey),
            PrivateNominalDeclarationKind.Class => ResolveClass(program,
                selector, isImport, selectedModuleIdentity,
                selectedExportKey),
            PrivateNominalDeclarationKind.Enum => ResolveEnum(program,
                selector, isImport, usingModuleIdentity,
                localDeclarationKey, selectedModuleIdentity,
                selectedExportKey),
            _ => throw new ArgumentException(
                "A private nominal declaration selector has an unknown kind.",
                nameof(selector)),
        };
    }

    private static PrimitiveType ResolveStructure(BoundProgram program,
        PrivateNominalDeclarationSelector selector, bool isImport,
        string? selectedModuleIdentity, string? selectedExportKey)
    {
        var candidates = program.Structures
            .Select((structure, index) => (structure, index))
            .Where(item => HasStructureName(program, item.index,
                selector.QualifiedName))
            .Where(item => !IsGenericStructure(program, item.index))
            .Where(item => isImport
                ? HasSelectedOrigin(program, StructureType(item.index),
                    selectedModuleIdentity, selectedExportKey)
                : IsLocalSourceStructure(program, item.index,
                    selector.QualifiedName))
            .Select(item => StructureType(item.index));
        return RequireUnique(candidates, selector);
    }

    private static PrimitiveType ResolveClass(BoundProgram program,
        PrivateNominalDeclarationSelector selector, bool isImport,
        string? selectedModuleIdentity, string? selectedExportKey)
    {
        var candidates = program.Classes
            .Select((classDefinition, index) => (classDefinition, index))
            .Where(item => HasClassName(program, item.index,
                selector.QualifiedName))
            .Where(item => isImport
                ? HasSelectedClassReference(program,
                    ClassType(item.index), selectedModuleIdentity,
                    selectedExportKey)
                : IsLocalSourceClass(program, item.index,
                    selector.QualifiedName))
            .Select(item => ClassType(item.index));
        return RequireUnique(candidates, selector);
    }

    private static PrimitiveType ResolveEnum(BoundProgram program,
        PrivateNominalDeclarationSelector selector, bool isImport,
        string usingModuleIdentity, string? localDeclarationKey,
        string? selectedModuleIdentity, string? selectedExportKey)
    {
        var candidates = program.EnumDefinitions
            .Select((definition, index) => (definition, index))
            .Where(item => item.definition.Name == selector.QualifiedName)
            .Where(item => {
                var type = BoundEnumTypes.Type(item.index);
                if (isImport)
                {
                    if (item.definition.Identity
                        is BoundEnumDeclarationIdentity.Core core)
                    {
                        return core.Value.ProviderIdentity
                                == selectedModuleIdentity
                            && core.Value.ExportKey == selectedExportKey
                            && (item.definition.ModuleImport is null
                                || (item.definition.ModuleImport
                                        .ProducerIdentity
                                    == selectedModuleIdentity
                                    && item.definition.ModuleImport.ExportKey
                                        == selectedExportKey));
                    }

                    return item.definition.Identity
                            is BoundEnumDeclarationIdentity.Private
                        && HasSelectedOrigin(program, type,
                            selectedModuleIdentity, selectedExportKey);
                }

                return item.definition.Identity
                        is BoundEnumDeclarationIdentity.Core ownedCore
                    && item.definition.ModuleImport is null
                    && ownedCore.Value.ProviderIdentity
                        == usingModuleIdentity
                    && ownedCore.Value.ExportKey == localDeclarationKey;
            })
            .Select(item => BoundEnumTypes.Type(item.index));
        return RequireUnique(candidates, selector);
    }

    private static PrimitiveType ResolveCoreEnum(BoundProgram program,
        PrivateNominalDeclarationSelector selector,
        CoreDeclarationIdentity enumIdentity)
    {
        if (selector is null
            || selector.Kind != PrivateNominalDeclarationKind.Enum
            || !enumIdentity.IsValid
            || enumIdentity.Kind != CoreDeclarationKind.EnumType)
        {
            throw new ArgumentException(
                "A Core enum-member assignment has an invalid enum identity selector.",
                nameof(selector));
        }

        var candidates = program.EnumDefinitions
            .Select((definition, index) => (definition, index))
            .Where(item => item.definition.Name == selector.QualifiedName
                && item.definition.Identity
                    is BoundEnumDeclarationIdentity.Core core
                && core.Value == enumIdentity)
            .Select(item => BoundEnumTypes.Type(item.index));
        return RequireUnique(candidates, selector);
    }

    private static PrimitiveType RequireUnique(
        IEnumerable<PrimitiveType> candidates,
        PrivateNominalDeclarationSelector selector)
    {
        var matches = candidates.ToArray();
        if (matches.Length != 1)
        {
            var message = matches.Length == 0
                ? "does not identify a checked declaration with the required ownership or provider provenance."
                : "is ambiguous in the checked program snapshot.";
            throw new ArgumentException(
                $"Nominal selector '{selector.QualifiedName}' {message}",
                nameof(selector));
        }

        return matches[0];
    }

    private static PrimitiveType StructureType(int index)
        => (PrimitiveType)checked(100 + index);

    private static PrimitiveType ClassType(int index)
        => (PrimitiveType)checked(ManagedClassTypes.First + index);

    private static bool IsLocalSourceStructure(BoundProgram program,
        int index, string qualifiedName)
    {
        if ((uint)index >= (uint)program.Structures.Length
            || program.SourceTypeDeclarations.IsDefaultOrEmpty
            || IsGenericStructure(program, index))
        {
            return false;
        }

        var type = StructureType(index);
        return !HasAnyPrivateOrigin(program, type)
            && program.SourceTypeDeclarations.Any(declaration =>
                declaration.StructureIndex == index
                && GetQualifiedName(declaration.Namespace,
                    declaration.Name) == qualifiedName
                && declaration.DeclarationSnapshot
                    == program.Structures[index]);
    }

    private static bool IsGenericStructure(BoundProgram program, int index)
        => program.Structures[index].SourceGenericTypeDefinitionIndex >= 0
            || (!program.SourceGenericTypeInstances.IsDefaultOrEmpty
                && program.SourceGenericTypeInstances.Any(instance =>
                    instance.StructureIndex == index));

    private static bool IsLocalSourceClass(BoundProgram program, int index,
        string qualifiedName)
    {
        if ((uint)index >= (uint)program.Classes.Length)
        {
            return false;
        }

        var type = ClassType(index);
        return program.Classes[index].Name == qualifiedName
            && !HasAnyPrivateOrigin(program, type)
            && (program.PrivateClassReferences.IsDefaultOrEmpty
                || !program.PrivateClassReferences.Any(reference =>
                    reference.ClassType == type));
    }

    private static bool HasStructureName(BoundProgram program, int index,
        string qualifiedName)
    {
        if ((uint)index >= (uint)program.Structures.Length)
        {
            return false;
        }
        if (program.Structures[index].Name == qualifiedName)
        {
            return true;
        }
        if (program.PrivateClassReferences.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var reference in program.PrivateClassReferences)
        {
            if (reference.ExpectedStructureMapping.IsDefaultOrEmpty)
            {
                continue;
            }
            for (var expectedIndex = 0;
                expectedIndex < reference.ExpectedStructureMapping.Length;
                expectedIndex++)
            {
                if (reference.ExpectedStructureMapping[expectedIndex]
                        == index
                    && (uint)expectedIndex
                        < (uint)reference.ExpectedSnapshot.Structures.Length
                    && reference.ExpectedSnapshot.Structures[expectedIndex]
                        .Name == qualifiedName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasClassName(BoundProgram program, int index,
        string qualifiedName)
    {
        if ((uint)index >= (uint)program.Classes.Length)
        {
            return false;
        }
        if (program.Classes[index].Name == qualifiedName)
        {
            return true;
        }
        if (program.PrivateClassReferences.IsDefaultOrEmpty)
        {
            return false;
        }

        var type = ClassType(index);
        foreach (var reference in program.PrivateClassReferences)
        {
            if (reference.ClassType != type
                || !ManagedClassTypes.IsClass(reference.ExpectedClassType))
            {
                continue;
            }

            var expectedIndex = ManagedClassTypes.ClassIndex(
                reference.ExpectedClassType);
            if ((uint)expectedIndex
                    < (uint)reference.ExpectedSnapshot.Classes.Length
                && reference.ExpectedSnapshot.Classes[expectedIndex].Name
                    == qualifiedName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSelectedClassReference(BoundProgram program,
        PrimitiveType type, string? selectedModuleIdentity,
        string? selectedExportKey)
        => !program.PrivateClassReferences.IsDefaultOrEmpty
            && program.PrivateClassReferences.Any(reference =>
                reference.ClassType == type && reference.IsSourceLookupRoot
                && OriginMatches(reference.Origin, selectedModuleIdentity,
                    selectedExportKey));

    private static bool HasSelectedOrigin(BoundProgram program,
        PrimitiveType type, string? selectedModuleIdentity,
        string? selectedExportKey)
        => !program.PrivateNominalTypeOrigins.IsDefaultOrEmpty
            && program.PrivateNominalTypeOrigins.Any(origin =>
                origin.PrivateType == type
                && OriginMatches(origin, selectedModuleIdentity,
                    selectedExportKey));

    private static bool HasAnyPrivateOrigin(BoundProgram program,
        PrimitiveType type)
        => !program.PrivateNominalTypeOrigins.IsDefaultOrEmpty
            && program.PrivateNominalTypeOrigins.Any(origin =>
                origin.PrivateType == type);

    private static bool OriginMatches(BoundPrivateNominalTypeOrigin origin,
        string? selectedModuleIdentity, string? selectedExportKey)
    {
        if (origin.OriginKind
                == BoundPrivateNominalOriginKind.OwnedDefinition
            && origin.IsExported)
        {
            return origin.UsingModuleIdentity == selectedModuleIdentity
                && origin.LocalDeclarationKey == selectedExportKey;
        }
        return origin.OriginKind
                == BoundPrivateNominalOriginKind.ImportedExpectation
            && origin.SelectedModuleIdentity == selectedModuleIdentity
            && origin.SelectedExportKey == selectedExportKey;
    }

    private static string GetQualifiedName(string @namespace, string name)
        => @namespace.Length == 0 ? name : @namespace + "." + name;
}
