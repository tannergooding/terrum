using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public sealed partial class PrivateModuleIdentityInputs
{
    private static ForeignInterfaceValidationResult
        ValidateForeignInterfaceAssignments(
            BoundProgram program, string usingModuleIdentity,
            IReadOnlyList<PrivateForeignInterfaceDefinitionAssignment>
                definitions,
            IReadOnlyList<PrivateForeignInterfaceImportAssignment> imports,
            HashSet<string> localKeys)
    {
        var interfaces = program.Interfaces;
        if (interfaces.IsDefault
            || !BoundInterfaceContract.IsValidTable(interfaces,
                static _ => true, program.GenericNominalTypeDefinitions,
                program.SourceGenericTypeDefinitions)
            || !ForeignNativeInterfaceContract.IsValidTable(program)
            || !NativeOutputContractValidator.IsValidInterfaceTable(
                interfaces, program.Structures, program.NativePointerTypes,
                program.NativeFunctionPointerSignatures,
                program.EnumDefinitions, program.NativeCoreRegistration,
                program.NativeOutputCoreRegistration))
        {
            throw new ModuleContractException(
                "The checked foreign-interface table is not eligible for private persistence.");
        }

        var copiedDefinitions
            = ImmutableArray.CreateBuilder<
                PrivateForeignInterfaceDefinitionAssignment>(
                    definitions.Count);
        var copiedImports
            = ImmutableArray.CreateBuilder<
                PrivateForeignInterfaceImportAssignment>(imports.Count);
        var origins
            = new BoundPrivateForeignInterfaceOrigin?[interfaces.Length];
        var captures
            = ImmutableArray.CreateBuilder<ForeignInterfaceCapture>(
                interfaces.Length);
        var selectedOrdinals = new HashSet<int>();

        foreach (var assignment in definitions)
        {
            if (assignment is null
                || !TrySelectForeignInterface(interfaces,
                    assignment.InterfaceOrdinal, selectedOrdinals,
                    out var definition)
                || assignment.Members.IsDefault
                || assignment.Members.Length
                    != definition.Members.Length
                || definition.Members.IsDefaultOrEmpty
                || definition.Accessibility is not (
                    BoundInterfaceAccessibility.Public
                    or BoundInterfaceAccessibility.Internal)
                || definition.ModuleImport is not null
                || (definition.SourceDeclaration is not null
                    && !IsValidSourceForeignInterfaceAssociation(
                        definition, definition.SourceDeclaration))
                || (definition.SourceDeclaration is null
                    && (definition.PrivateForeignInterfaceOrigin is null
                        || definition.PrivateForeignInterfaceOrigin.Owner
                            .Kind != BoundPrivateForeignInterfaceBindingKind
                                .OwnedDefinition
                        || definition.PrivateForeignInterfaceOrigin.Owner
                            .UsingModuleIdentity != usingModuleIdentity
                        || !BoundPrivateForeignInterfaceOriginContract
                            .IsValid(definition))))
            {
                throw new ArgumentException(
                    "A private foreign-interface definition assignment does not identify one complete owned declaration.",
                    nameof(definitions));
            }

            if (!IsValidDeclarationKey(assignment.LocalDeclarationKey)
                || !localKeys.Add(assignment.LocalDeclarationKey)
                || (assignment.IsExported && !definition.IsPublic))
            {
                throw new ArgumentException(
                    "A private foreign-interface owner key or export assignment is invalid.",
                    nameof(definitions));
            }

            var ownerBinding = new BoundPrivateForeignInterfaceBinding(
                BoundPrivateForeignInterfaceBindingKind.OwnedDefinition,
                assignment.IsExported, usingModuleIdentity,
                assignment.LocalDeclarationKey);
            var memberBindings
                = ImmutableArray.CreateBuilder<
                    BoundPrivateForeignInterfaceBinding>(
                    assignment.Members.Length);
            var retainedOrigin
                = definition.PrivateForeignInterfaceOrigin;
            var copiedMembers
                = ImmutableArray.CreateBuilder<
                    PrivateForeignInterfaceMemberDefinitionAssignment>(
                    assignment.Members.Length);
            for (var memberOrdinal = 0;
                memberOrdinal < assignment.Members.Length;
                memberOrdinal++)
            {
                var memberAssignment = assignment.Members[memberOrdinal];
                var member = definition.Members[memberOrdinal];
                if (memberAssignment is null
                    || memberAssignment.MemberOrdinal != memberOrdinal
                    || member.Ordinal != memberOrdinal
                    || !IsValidDeclarationKey(
                        memberAssignment.LocalDeclarationKey)
                    || !localKeys.Add(
                        memberAssignment.LocalDeclarationKey)
                    || (definition.SourceDeclaration
                            is BoundSourceInterfaceDeclarationHandle
                                sourceDeclaration
                        ? !IsValidSourceForeignInterfaceMember(
                            member, memberOrdinal, sourceDeclaration)
                        : retainedOrigin is null
                            || retainedOrigin.Members[memberOrdinal]
                                .LocalDeclarationKey
                                != member.DeclarationKey))
                {
                    throw new ArgumentException(
                        "A private foreign-interface definition must assign every checked member in declaration order.",
                        nameof(definitions));
                }

                memberBindings.Add(ownerBinding with {
                    LocalDeclarationKey =
                        memberAssignment.LocalDeclarationKey,
                });
                copiedMembers.Add(memberAssignment with { });
            }

            var origin = new BoundPrivateForeignInterfaceOrigin(
                ownerBinding, memberBindings.MoveToImmutable());
            if (definition.PrivateForeignInterfaceOrigin
                    is BoundPrivateForeignInterfaceOrigin retained
                && !SameForeignInterfaceOrigin(retained, origin))
            {
                throw new ArgumentException(
                    "A supplied foreign-interface definition assignment contradicts its retained Binding graph.",
                    nameof(definitions));
            }
            if (!BoundPrivateForeignInterfaceOriginContract.IsValid(
                    definition with {
                        PrivateForeignInterfaceOrigin = origin,
                    }))
            {
                throw new ArgumentException(
                    "A private foreign-interface definition assignment is inconsistent with its bound owner.",
                    nameof(definitions));
            }

            var copiedAssignment = assignment with {
                Members = copiedMembers.MoveToImmutable(),
            };
            copiedDefinitions.Add(copiedAssignment);
            origins[assignment.InterfaceOrdinal] = origin;
            captures.Add(new ForeignInterfaceCapture(
                assignment.InterfaceOrdinal, definition.SourceDeclaration,
                definition.PrivateForeignInterfaceOrigin,
                definition.ModuleImport, origin));
        }

        foreach (var assignment in imports)
        {
            if (assignment is null
                || !TrySelectForeignInterface(interfaces,
                    assignment.InterfaceOrdinal, selectedOrdinals,
                    out var definition)
                || definition.ModuleImport is not BoundModuleImport moduleImport
                || definition.PrivateForeignInterfaceOrigin
                    is not BoundPrivateForeignInterfaceOrigin providerOrigin
                || !BoundPrivateForeignInterfaceOriginContract.IsValid(
                    definition)
                || assignment.Members.IsDefault
                || assignment.Members.Length != definition.Members.Length
                || definition.Members.IsDefaultOrEmpty
                || definition.Accessibility
                    != BoundInterfaceAccessibility.Public)
            {
                throw new ArgumentException(
                    "A private foreign-interface import assignment does not identify one complete private provider declaration.",
                    nameof(imports));
            }

            if (!IsValidDeclarationKey(assignment.LocalDeclarationKey)
                || !localKeys.Add(assignment.LocalDeclarationKey)
                || !IsValidModuleIdentity(
                    assignment.SelectedModuleIdentity)
                || !IsValidDeclarationKey(assignment.SelectedExportKey)
                || moduleImport.ProducerIdentity
                    != assignment.SelectedModuleIdentity
                || moduleImport.ExportKey != assignment.SelectedExportKey
                || moduleImport.TargetRequirement != 1
                || moduleImport.RuntimePolicy != (uint)program.RuntimePolicy)
            {
                throw new ArgumentException(
                    "A private foreign-interface import owner selection is invalid or differs from its checked provider.",
                    nameof(imports));
            }

            if (providerOrigin.Owner.Kind
                    == BoundPrivateForeignInterfaceBindingKind.OwnedDefinition)
            {
                if (!providerOrigin.Owner.IsExported
                    || providerOrigin.Owner.UsingModuleIdentity
                        != assignment.SelectedModuleIdentity
                    || providerOrigin.Owner.LocalDeclarationKey
                        != assignment.SelectedExportKey)
                {
                    throw new ArgumentException(
                        "A foreign-interface import must select the provider's exact owned exported owner.",
                        nameof(imports));
                }
            }
            else if (providerOrigin.Owner.UsingModuleIdentity
                    != usingModuleIdentity
                || providerOrigin.Owner.SelectedModuleIdentity
                    != assignment.SelectedModuleIdentity
                || providerOrigin.Owner.SelectedExportKey
                    != assignment.SelectedExportKey)
            {
                throw new ArgumentException(
                    "A retained foreign-interface proxy cannot be re-exported or rebound to another provider.",
                    nameof(imports));
            }

            var ownerBinding = new BoundPrivateForeignInterfaceBinding(
                BoundPrivateForeignInterfaceBindingKind.ImportedExpectation,
                false, usingModuleIdentity, assignment.LocalDeclarationKey,
                assignment.SelectedModuleIdentity,
                assignment.SelectedExportKey);
            var selectingOwnedProvider
                = providerOrigin.Owner.Kind
                    == BoundPrivateForeignInterfaceBindingKind
                        .OwnedDefinition;
            var memberBindings
                = ImmutableArray.CreateBuilder<
                    BoundPrivateForeignInterfaceBinding>(
                    assignment.Members.Length);
            var copiedMembers
                = ImmutableArray.CreateBuilder<
                    PrivateForeignInterfaceMemberImportAssignment>(
                    assignment.Members.Length);
            for (var memberOrdinal = 0;
                memberOrdinal < assignment.Members.Length;
                memberOrdinal++)
            {
                var memberAssignment = assignment.Members[memberOrdinal];
                var member = definition.Members[memberOrdinal];
                var providerMember = providerOrigin.Members[memberOrdinal];
                if (memberAssignment is null
                    || memberAssignment.MemberOrdinal != memberOrdinal
                    || member.Ordinal != memberOrdinal
                    || !IsValidDeclarationKey(
                        memberAssignment.LocalDeclarationKey)
                    || !localKeys.Add(
                        memberAssignment.LocalDeclarationKey)
                    || !IsValidDeclarationKey(
                        memberAssignment.SelectedExportKey)
                    || (selectingOwnedProvider
                        ? providerMember.Kind
                                != BoundPrivateForeignInterfaceBindingKind
                                    .OwnedDefinition
                            || !providerMember.IsExported
                            || providerMember.UsingModuleIdentity
                                != assignment.SelectedModuleIdentity
                            || memberAssignment.SelectedExportKey
                                != providerMember.LocalDeclarationKey
                            || member.DeclarationKey
                                != providerMember.LocalDeclarationKey
                        : providerMember.Kind
                                != BoundPrivateForeignInterfaceBindingKind
                                    .ImportedExpectation
                            || providerMember.UsingModuleIdentity
                                != usingModuleIdentity
                            || providerMember.SelectedModuleIdentity
                                != assignment.SelectedModuleIdentity
                            || providerMember.SelectedExportKey
                                != memberAssignment.SelectedExportKey
                            || providerMember.LocalDeclarationKey
                                != memberAssignment.LocalDeclarationKey
                            || member.DeclarationKey
                                != providerMember.LocalDeclarationKey)
                    || !member.AdditionalDeclarationKeys.IsEmpty)
                {
                    throw new ArgumentException(
                        "A private foreign-interface import must select each exact exported provider member in declaration order.",
                        nameof(imports));
                }

                memberBindings.Add(new BoundPrivateForeignInterfaceBinding(
                    BoundPrivateForeignInterfaceBindingKind
                        .ImportedExpectation,
                    false, usingModuleIdentity,
                    memberAssignment.LocalDeclarationKey,
                    assignment.SelectedModuleIdentity,
                    memberAssignment.SelectedExportKey));
                copiedMembers.Add(memberAssignment with { });
            }

            var origin = new BoundPrivateForeignInterfaceOrigin(
                ownerBinding, memberBindings.MoveToImmutable());
            if (!selectingOwnedProvider
                && !SameForeignInterfaceOrigin(providerOrigin, origin))
            {
                throw new ArgumentException(
                    "A supplied foreign-interface import assignment contradicts its retained Binding graph.",
                    nameof(imports));
            }
            var copiedAssignment = assignment with {
                Members = copiedMembers.MoveToImmutable(),
            };
            copiedImports.Add(copiedAssignment);
            origins[assignment.InterfaceOrdinal] = origin;
            captures.Add(new ForeignInterfaceCapture(
                assignment.InterfaceOrdinal, definition.SourceDeclaration,
                providerOrigin, moduleImport, origin));
        }

        for (var interfaceOrdinal = 0;
            interfaceOrdinal < interfaces.Length;
            interfaceOrdinal++)
        {
            var definition = interfaces[interfaceOrdinal];
            if (definition.ForeignNativeInterface is null
                || origins[interfaceOrdinal] is not null)
            {
                continue;
            }

            var retained = definition.PrivateForeignInterfaceOrigin;
            if (retained is null
                || retained.Owner.UsingModuleIdentity
                    != usingModuleIdentity
                || !BoundPrivateForeignInterfaceOriginContract.IsValid(
                    definition)
                || !AddPrivateForeignInterfaceKeys(retained, localKeys))
            {
                throw new ArgumentException(
                    "A foreign-interface declaration requires one complete explicit identity assignment or a retained Binding graph in the same using Module.",
                    nameof(definitions));
            }

            origins[interfaceOrdinal] = retained;
            captures.Add(new ForeignInterfaceCapture(interfaceOrdinal,
                definition.SourceDeclaration, retained,
                definition.ModuleImport, retained));
        }

        return new ForeignInterfaceValidationResult(
            copiedDefinitions.MoveToImmutable(),
            copiedImports.MoveToImmutable(),
            [.. origins],
            captures.ToImmutable());
    }

    private static bool TrySelectForeignInterface(
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        int interfaceOrdinal, HashSet<int> selectedOrdinals,
        out BoundInterfaceDefinition definition)
    {
        definition = null!;
        if ((uint)interfaceOrdinal >= (uint)interfaces.Length
            || !selectedOrdinals.Add(interfaceOrdinal)
            || interfaces[interfaceOrdinal] is not
                BoundInterfaceDefinition selected
            || selected.ForeignNativeInterface is null
            || selected.Members.IsDefaultOrEmpty)
        {
            return false;
        }

        definition = selected;
        return true;
    }

    private static bool IsValidSourceForeignInterfaceAssociation(
        BoundInterfaceDefinition definition,
        BoundSourceInterfaceDeclarationHandle sourceDeclaration)
    {
        if (definition.ForeignNativeInterface is null
            || definition.IsPartial
            || definition.Accessibility is not (
                BoundInterfaceAccessibility.Public
                or BoundInterfaceAccessibility.Internal)
            || !definition.Formals.IsEmpty
            || !definition.Bases.IsEmpty
            || !definition.Constraints.IsEmpty
            || !ReferenceEquals(definition.SourceDeclaration,
                sourceDeclaration)
            || sourceDeclaration.QualifiedName != definition.QualifiedName
            || !sourceDeclaration.IsValid
            || sourceDeclaration.Parts[0].SourceInputOrdinal
                != definition.SourceInputOrdinal
            || definition.Members.IsDefaultOrEmpty)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < definition.Members.Length; ordinal++)
        {
            var member = definition.Members[ordinal];
            if (member is null
                || !IsValidSourceForeignInterfaceMember(member, ordinal,
                    sourceDeclaration))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidSourceForeignInterfaceMember(
        BoundInterfaceMemberDefinition member, int ordinal,
        BoundSourceInterfaceDeclarationHandle sourceDeclaration)
        => member.Ordinal == ordinal
            && member.SourceInputOrdinal
                == sourceDeclaration.Parts[0].SourceInputOrdinal
            && member.SourceDeclaration
                is BoundSourceInterfaceMemberHandle handle
            && handle.IsValid && handle.Ordinal == ordinal
            && ReferenceEquals(handle.Owner, sourceDeclaration)
            && member.SourceDeclarationSpan == handle.Declaration.Span;

    private static bool AddPrivateForeignInterfaceKeys(
        BoundPrivateForeignInterfaceOrigin origin,
        HashSet<string> localKeys)
    {
        if (!IsValidPrivateForeignInterfaceBinding(origin.Owner)
            || !localKeys.Add(origin.Owner.LocalDeclarationKey))
        {
            return false;
        }

        foreach (var member in origin.Members)
        {
            if (!IsValidPrivateForeignInterfaceBinding(member)
                || !localKeys.Add(member.LocalDeclarationKey))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsValidPrivateForeignInterfaceBinding(
        BoundPrivateForeignInterfaceBinding binding)
        => IsValidModuleIdentity(binding.UsingModuleIdentity)
        && IsValidDeclarationKey(binding.LocalDeclarationKey)
        && (binding.Kind
                == BoundPrivateForeignInterfaceBindingKind.OwnedDefinition
            ? binding.SelectedModuleIdentity is null
                && binding.SelectedExportKey is null
            : binding.Kind
                    == BoundPrivateForeignInterfaceBindingKind
                        .ImportedExpectation
                && !binding.IsExported
                && IsValidModuleIdentity(binding.SelectedModuleIdentity)
                && IsValidDeclarationKey(binding.SelectedExportKey));

    private static bool SameForeignInterfaceOrigin(
        BoundPrivateForeignInterfaceOrigin left,
        BoundPrivateForeignInterfaceOrigin right)
        => left.Owner == right.Owner
            && !left.Members.IsDefault
            && !right.Members.IsDefault
            && left.Members.SequenceEqual(right.Members);
