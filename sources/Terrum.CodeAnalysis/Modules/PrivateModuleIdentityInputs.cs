// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Assigns a checked private nominal type to its local declaration identity.</summary>
/// <param name="Type">The exact nominal type in the bound program.</param>
/// <param name="LocalDeclarationKey">The source-local declaration key.</param>
/// <param name="IsExported">Whether the definition is a public provider declaration.</param>
public sealed record PrivateNominalDefinition(
    PrimitiveType Type, string LocalDeclarationKey, bool IsExported);

/// <summary>Binds a checked private nominal type to one selected provider declaration.</summary>
/// <param name="Type">The exact nominal type in the bound program.</param>
/// <param name="LocalDeclarationKey">The source-local declaration key.</param>
/// <param name="SelectedModuleIdentity">The selected provider's logical Module identity.</param>
/// <param name="SelectedExportKey">The exact selected provider declaration key.</param>
public sealed record PrivateNominalImport(
    PrimitiveType Type, string LocalDeclarationKey,
    string SelectedModuleIdentity, string SelectedExportKey);

/// <summary>Supplies exact Core enum and member identities for a private nominal enum.</summary>
/// <param name="EnumType">The enum type in the bound program.</param>
/// <param name="EnumIdentity">The exact Core identity of the enum declaration.</param>
/// <param name="MemberOrdinal">The member's ordinal in the checked enum definition.</param>
/// <param name="MemberIdentity">The exact Core identity of the enum member.</param>
/// <param name="MemberDeclarationKey">The member declaration key retained by the provider.</param>
public sealed record PrivateEnumMemberAssignment(
    PrimitiveType EnumType,
    CoreDeclarationIdentity EnumIdentity,
    int MemberOrdinal,
    CoreDeclarationIdentity MemberIdentity,
    string MemberDeclarationKey);

/// <summary>
/// Captures explicit nominal origins for one checked private-module producer or consumer.
/// </summary>
public sealed class PrivateModuleIdentityInputs
{
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    private PrivateModuleIdentityInputs(BoundProgram program,
        string usingModuleIdentity,
        ImmutableArray<PrivateNominalDefinition> definitions,
        ImmutableArray<PrivateNominalImport> imports,
        ImmutableArray<PrivateEnumMemberAssignment> enumMembers)
    {
        Program = program;
        UsingModuleIdentity = usingModuleIdentity;
        Definitions = definitions;
        Imports = imports;
        EnumMembers = enumMembers;
    }

    internal BoundProgram Program { get; }

    /// <summary>Gets the logical identity of the Module using these declarations.</summary>
    public string UsingModuleIdentity { get; }

    /// <summary>Gets the owned nominal definition assignments.</summary>
    public ImmutableArray<PrivateNominalDefinition> Definitions { get; }

    /// <summary>Gets the selected nominal import assignments.</summary>
    public ImmutableArray<PrivateNominalImport> Imports { get; }

    /// <summary>Gets the exact Core enum-member assignments.</summary>
    public ImmutableArray<PrivateEnumMemberAssignment> EnumMembers { get; }

    /// <summary>Captures validated nominal identities for a checked program snapshot.</summary>
    /// <param name="program">The exact bound-program snapshot to which the inputs apply.</param>
    /// <param name="usingModuleIdentity">The logical identity of the using Module.</param>
    /// <param name="definitions">Owned nominal definitions and their local keys.</param>
    /// <param name="imports">Imported nominal declarations and selected provider keys.</param>
    /// <returns>The validated identity inputs bound to <paramref name="program"/>.</returns>
    public static PrivateModuleIdentityInputs Create(
        BoundProgram program,
        string usingModuleIdentity,
        IReadOnlyList<PrivateNominalDefinition> definitions,
        IReadOnlyList<PrivateNominalImport> imports) =>
        Create(program, usingModuleIdentity, definitions, imports, []);

    /// <summary>Captures validated nominal and enum-member identities for a checked program snapshot.</summary>
    /// <param name="program">The exact bound-program snapshot to which the inputs apply.</param>
    /// <param name="usingModuleIdentity">The logical identity of the using Module.</param>
    /// <param name="definitions">Owned nominal definitions and their local keys.</param>
    /// <param name="imports">Imported nominal declarations and selected provider keys.</param>
    /// <param name="enumMembers">Complete Core identities for selected nominal enum members.</param>
    /// <returns>The validated identity inputs bound to <paramref name="program"/>.</returns>
    public static PrivateModuleIdentityInputs Create(
        BoundProgram program,
        string usingModuleIdentity,
        IReadOnlyList<PrivateNominalDefinition> definitions,
        IReadOnlyList<PrivateNominalImport> imports,
        IReadOnlyList<PrivateEnumMemberAssignment> enumMembers)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(usingModuleIdentity);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(imports);
        ArgumentNullException.ThrowIfNull(enumMembers);
        if (!IsValidModuleIdentity(usingModuleIdentity))
        {
            throw new ArgumentException(
                "The logical private Module identity is invalid.",
                nameof(usingModuleIdentity));
        }

        var owned = ImmutableArray.CreateBuilder<PrivateNominalDefinition>(
            definitions.Count);
        var imported = ImmutableArray.CreateBuilder<PrivateNominalImport>(
            imports.Count);
        var enumMemberAssignments
            = ImmutableArray.CreateBuilder<PrivateEnumMemberAssignment>(
                enumMembers.Count);
        var assignedTypes = new HashSet<PrimitiveType>();
        var localKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var retainedOrigins = program.PrivateNominalTypeOrigins
                .Where(origin => origin.PrivateType == definition?.Type)
                .ToArray();
            if (definition is null
                || !IsNominalType(program, definition.Type)
                || !assignedTypes.Add(definition.Type)
                || !IsValidDeclarationKey(definition.LocalDeclarationKey)
                || !localKeys.Add(definition.LocalDeclarationKey)
                || retainedOrigins.Length != 0
                || (definition.IsExported
                    && !IsPublicNominalType(program, definition.Type)))
            {
                throw new ArgumentException(
                    "A private nominal definition assignment is invalid or conflicts with another local key.",
                    nameof(definitions));
            }
            owned.Add(definition with { });
        }

        foreach (var import in imports)
        {
            var retainedOrigins = program.PrivateNominalTypeOrigins
                .Where(origin => origin.PrivateType == import?.Type)
                .ToArray();
            if (import is null
                || !IsNominalType(program, import.Type)
                || !assignedTypes.Add(import.Type)
                || !IsValidDeclarationKey(import.LocalDeclarationKey)
                || !localKeys.Add(import.LocalDeclarationKey)
                || !IsValidModuleIdentity(import.SelectedModuleIdentity)
                || !IsValidDeclarationKey(import.SelectedExportKey))
            {
                throw new ArgumentException(
                    "A private nominal import assignment is invalid or conflicts with another local key.",
                    nameof(imports));
            }
            if (ManagedClassTypes.IsClass(import.Type)
                && !program.PrivateClassReferences.Any(reference =>
                    reference.ClassType == import.Type
                    && reference.IsSourceLookupRoot
                    && reference.Origin.UsingModuleIdentity
                        == import.SelectedModuleIdentity
                    && reference.Origin.LocalDeclarationKey
                        == import.SelectedExportKey))
            {
                throw new ArgumentException(
                    "A private class import requires its exact retained reference snapshot.",
                    nameof(imports));
            }
            foreach (var retainedOrigin in retainedOrigins)
            {
                var retainedModule = retainedOrigin.OriginKind
                    == BoundPrivateNominalOriginKind.OwnedDefinition
                    && retainedOrigin.IsExported
                    ? retainedOrigin.UsingModuleIdentity
                    : retainedOrigin.SelectedModuleIdentity;
                var retainedKey = retainedOrigin.OriginKind
                    == BoundPrivateNominalOriginKind.OwnedDefinition
                    && retainedOrigin.IsExported
                    ? retainedOrigin.LocalDeclarationKey
                    : retainedOrigin.SelectedExportKey;
                if (retainedModule != import.SelectedModuleIdentity
                    || retainedKey != import.SelectedExportKey)
                {
                    throw new ArgumentException(
                        "A private nominal import cannot replace a retained provider expectation.",
                        nameof(imports));
                }
            }
            imported.Add(import with { });
        }

        ValidateEnumMemberAssignments(program, usingModuleIdentity,
            owned, imported, enumMembers, enumMemberAssignments);

        return new PrivateModuleIdentityInputs(program, usingModuleIdentity,
            owned.MoveToImmutable(), imported.MoveToImmutable(),
            enumMemberAssignments.MoveToImmutable());
    }

    internal bool IsFor(BoundProgram program) =>
        ReferenceEquals(Program, program);

    internal PrivateModuleIdentityInputs Rebind(BoundProgram sourceProgram,
        BoundProgram reboundProgram)
    {
        if (!IsFor(sourceProgram))
        {
            throw new ArgumentException(
                "Private nominal identity inputs were captured for another bound-program snapshot.",
                nameof(sourceProgram));
        }
        return new PrivateModuleIdentityInputs(reboundProgram,
            UsingModuleIdentity, Definitions, Imports, EnumMembers);
    }

    internal static bool IsValidModuleIdentity(string? identity)
    {
        if (identity is not { Length: >= 1 and <= 64 }
            || !IsLetter(identity[0]))
        {
            return false;
        }
        for (var index = 1; index < identity.Length; index++)
        {
            var character = identity[index];
            if (!IsLetter(character)
                && character is not (>= '0' and <= '9')
                && character != '_')
            {
                return false;
            }
        }
        return true;

        static bool IsLetter(char character)
        {
            return character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
        }
    }

    internal static bool IsValidDeclarationKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }
        try
        {
            var byteCount = s_strictUtf8.GetByteCount(key);
            return byteCount is >= 1 and <= 256;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    internal static bool IsValidEnumMemberKey(string? key) =>
        IsValidDeclarationKey(key)
        && key!.IndexOf('\0', StringComparison.Ordinal) < 0;

    private static void ValidateEnumMemberAssignments(
        BoundProgram program,
        string usingModuleIdentity,
        ImmutableArray<PrivateNominalDefinition>.Builder definitions,
        ImmutableArray<PrivateNominalImport>.Builder imports,
        IReadOnlyList<PrivateEnumMemberAssignment> assignments,
        ImmutableArray<PrivateEnumMemberAssignment>.Builder retained)
    {
        var assignedTypes = new Dictionary<PrimitiveType, (bool IsImport,
            string ModuleIdentity, string ExportKey)>();
        foreach (var definition in definitions)
        {
            if (!BoundEnumTypes.IsType(definition.Type))
            {
                continue;
            }
            var enumDefinition = program.EnumDefinitions[
                BoundEnumTypes.Index(definition.Type)];
            ValidateEnumDeclaration(enumDefinition, definition.Type,
                usingModuleIdentity, definition.LocalDeclarationKey,
                isImport: false, definition.IsExported);
        }
        foreach (var import in imports)
        {
            if (!BoundEnumTypes.IsType(import.Type))
            {
                continue;
            }
            var enumDefinition = program.EnumDefinitions[
                BoundEnumTypes.Index(import.Type)];
            ValidateEnumDeclaration(enumDefinition, import.Type,
                import.SelectedModuleIdentity, import.SelectedExportKey,
                isImport: true, isExported: false);
        }

        var seenOrdinals = new HashSet<(PrimitiveType Type, int Ordinal)>();
        var seenIdentities = new HashSet<(PrimitiveType Type,
            CoreDeclarationIdentity Identity)>();
        var seenKeys = new HashSet<(PrimitiveType Type, string Key)>();
        var counts = new Dictionary<PrimitiveType, int>();
        foreach (var assignment in assignments)
        {
            if (assignment is null
                || !BoundEnumTypes.IsType(assignment.EnumType)
                || (uint)BoundEnumTypes.Index(assignment.EnumType)
                    >= (uint)program.EnumDefinitions.Length
                || !assignedTypes.ContainsKey(assignment.EnumType))
            {
                throw new ArgumentException(
                    "Enum member assignment has no selected nominal origin.",
                    nameof(assignments));
            }

            var definition = program.EnumDefinitions[
                BoundEnumTypes.Index(assignment.EnumType)];
            if (definition.Identity
                is not BoundEnumDeclarationIdentity.Core enumIdentity)
            {
                throw new ArgumentException(
                    "Core enum-member assignments cannot identify a private-only enum.",
                    nameof(assignments));
            }
            if (!assignment.EnumIdentity.IsValid
                || assignment.EnumIdentity.Kind
                    != CoreDeclarationKind.EnumType
                || assignment.EnumIdentity != enumIdentity.Value
                || assignment.MemberOrdinal < 0
                || assignment.MemberOrdinal >= definition.Members.Length
                || !assignment.MemberIdentity.IsValid
                || assignment.MemberIdentity.Kind
                    != CoreDeclarationKind.EnumMember
                || assignment.MemberIdentity.ProviderIdentity
                    != enumIdentity.Value.ProviderIdentity)
            {
                throw new ArgumentException(
                    "Enum member assignment does not identify its exact owner and declaration ordinal.",
                    nameof(assignments));
            }

            var member = definition.Members[assignment.MemberOrdinal];
            if (member.DeclaringEnum
                    is not BoundEnumDeclarationIdentity.Core declaringEnum
                || declaringEnum.Value != enumIdentity.Value
                || member.Identity
                    is not BoundEnumMemberIdentity.Core memberIdentity
                || memberIdentity.Value != assignment.MemberIdentity
                || memberIdentity.Value.Kind
                    != CoreDeclarationKind.EnumMember
                || memberIdentity.Value.ProviderIdentity
                    != enumIdentity.Value.ProviderIdentity
                || !IsValidEnumMemberKey(assignment.MemberDeclarationKey)
                || assignment.MemberDeclarationKey != member.DeclarationKey
                || !seenOrdinals.Add((assignment.EnumType,
                    assignment.MemberOrdinal))
                || !seenIdentities.Add((assignment.EnumType,
                    assignment.MemberIdentity))
                || !seenKeys.Add((assignment.EnumType,
                    assignment.MemberDeclarationKey)))
            {
                throw new ArgumentException(
                    "Enum member key or identity differs from its checked declaration.",
                    nameof(assignments));
            }

            counts[assignment.EnumType]
                = counts.TryGetValue(assignment.EnumType, out var count)
                    ? count + 1 : 1;
            retained.Add(assignment with { });
        }

        foreach (var (type, _) in assignedTypes)
        {
            var definition = program.EnumDefinitions[
                BoundEnumTypes.Index(type)];
            if (definition.Identity
                is BoundEnumDeclarationIdentity.Private)
            {
                continue;
            }

            var expectedCount = definition.Members.Length;
            if (counts.GetValueOrDefault(type) != expectedCount)
            {
                throw new ArgumentException(
                    "Every retained nonempty enum requires a complete member-key assignment.",
                    nameof(assignments));
            }
        }

        void ValidateEnumDeclaration(BoundEnumDefinition definition,
            PrimitiveType type, string selectedModuleIdentity,
            string selectedExportKey, bool isImport, bool isExported)
        {
            if (definition.Identity
                is BoundEnumDeclarationIdentity.Private privateIdentity)
            {
                var occurrence = privateIdentity.Value;
                var origin = occurrence.Origin;
                var expectedModule = origin.OriginKind
                        == BoundPrivateNominalOriginKind.OwnedDefinition
                    && origin.IsExported
                    ? origin.UsingModuleIdentity
                    : origin.SelectedModuleIdentity;
                var expectedKey = origin.OriginKind
                        == BoundPrivateNominalOriginKind.OwnedDefinition
                    && origin.IsExported
                    ? origin.LocalDeclarationKey
                    : origin.SelectedExportKey;
                var validBinding = isImport
                    ? expectedModule == selectedModuleIdentity
                        && expectedKey == selectedExportKey
                    : origin.OriginKind
                            == BoundPrivateNominalOriginKind.OwnedDefinition
                        && origin.UsingModuleIdentity
                            == usingModuleIdentity
                        && origin.LocalDeclarationKey == selectedExportKey
                        && origin.IsExported == isExported;
                if (!BoundEnumTypes.IsValidTable(
                        program.EnumDefinitions)
                    || !program.PrivateNominalTypeOrigins.Contains(origin)
                    || !BoundEnumTypes.AreEquivalent(definition,
                        occurrence.ExpectedSnapshot)
                    || !validBinding
                    || !definition.IsPublic || !definition.IsPrimary
                    || definition.UnderlyingType != PrimitiveType.Int32
                    || definition.BlittableTargetRequirement is not (0
                        or FixedLayoutStorage
                            .WindowsX64TargetRequirement)
                    || !assignedTypes.TryAdd(type,
                        (isImport, selectedModuleIdentity,
                            selectedExportKey)))
                {
                    throw new ArgumentException(
                        "Private enum occurrence does not satisfy its exact retained origin binding.",
                        isImport ? nameof(imports) : nameof(definitions));
                }
                return;
            }

            if (definition.Identity
                    is not BoundEnumDeclarationIdentity.Core identity
                || !definition.Identity.IsValid
                || !definition.IsPublic || !definition.IsPrimary
                || definition.UnderlyingType != PrimitiveType.Int32
                || definition.BlittableTargetRequirement is not (0
                    or FixedLayoutStorage.WindowsX64TargetRequirement)
                || (!isImport
                    && (definition.ModuleImport is not null
                        || identity.Value.ProviderIdentity
                            != usingModuleIdentity
                        || identity.Value.ExportKey != selectedExportKey))
                || (isImport
                    && (identity.Value.ProviderIdentity
                            != selectedModuleIdentity
                        || identity.Value.ExportKey != selectedExportKey
                        || (definition.ModuleImport is not null
                            && (definition.ModuleImport.ProducerIdentity
                                    != selectedModuleIdentity
                                || definition.ModuleImport.ExportKey
                                    != selectedExportKey))))
                || !assignedTypes.TryAdd(type,
                    (isImport, selectedModuleIdentity,
                        selectedExportKey)))
            {
                throw new ArgumentException(
                    "Selected enum declaration does not satisfy its exact origin binding.",
                    isImport ? nameof(imports) : nameof(definitions));
            }
        }
    }

    private static bool IsNominalType(BoundProgram program,
        PrimitiveType type)
    {
        var typeValue = (int)type;
        if (BoundEnumTypes.IsType(type))
        {
            return (uint)BoundEnumTypes.Index(type)
                < (uint)program.EnumDefinitions.Length;
        }
        var structureIndex = typeValue >= 100 ? typeValue - 100 : -1;
        if (structureIndex >= 0
            && (uint)structureIndex < (uint)program.Structures.Length)
        {
            return true;
        }
        if (ManagedClassTypes.IsClass(type)
            || ManagedClassTypes.IsOptional(type))
        {
            var classIndex = ManagedClassTypes.ClassIndex(type);
            return type == (PrimitiveType)(ManagedClassTypes.First
                    + classIndex)
                && (uint)classIndex < (uint)program.Classes.Length;
        }
        return false;
    }

    private static bool IsPublicNominalType(BoundProgram program,
        PrimitiveType type)
    {
        if (BoundEnumTypes.IsType(type))
        {
            var definition = program.EnumDefinitions[
                BoundEnumTypes.Index(type)];
            return definition.IsPublic;
        }
        var typeValue = (int)type;
        var structureIndex = typeValue >= 100 ? typeValue - 100 : -1;
        if (structureIndex >= 0
            && (uint)structureIndex < (uint)program.Structures.Length)
        {
            var structure = program.Structures[structureIndex];
            return !structure.IsPrivate && !structure.IsInternal;
        }
        if (ManagedClassTypes.IsClass(type)
            || ManagedClassTypes.IsOptional(type))
        {
            var classIndex = ManagedClassTypes.ClassIndex(type);
            return type == (PrimitiveType)(ManagedClassTypes.First
                    + classIndex)
                && !program.Classes[classIndex].IsInternal;
        }
        return false;
    }
}
