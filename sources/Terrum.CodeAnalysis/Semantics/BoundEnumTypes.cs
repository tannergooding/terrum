using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record BoundEnumDefinition(
    CoreDeclarationIdentity Identity,
    string Name,
    PrimitiveType UnderlyingType,
    ImmutableArray<BoundEnumMemberDefinition> Members,
    bool IsPublic,
    bool IsPrimary,
    bool IsFlags)
{
    internal int BlittableTargetRequirement { get; init; }
    internal BoundModuleImport? ModuleImport { get; init; }

    internal static BoundEnumDefinition FromCore(CoreEnumDefinition definition) =>
        new(definition.Identity, definition.QualifiedName, definition.UnderlyingType,
            [.. definition.Members.Select(static member => new BoundEnumMemberDefinition(
                member.Identity, member.DeclaringEnum, member.Name, member.Value,
                member.IsPublic, member.DeclarationKey))],
            definition.IsPublic, definition.IsPrimary, definition.IsFlags)
        {
            BlittableTargetRequirement = definition.BlittableTargetRequirement,
        };
}

internal sealed record BoundEnumMemberDefinition(
    CoreDeclarationIdentity Identity,
    CoreDeclarationIdentity DeclaringEnum,
    string Name,
    int Value,
    bool IsPublic,
    string DeclarationKey);

internal static class BoundEnumTypes
{
    internal const int First = 100 + SemanticVerifier.MaximumVariables;
    internal const int MaximumCount = ManagedClassTypes.First - First;

    internal static bool IsType(PrimitiveType type) =>
        (int)type is >= First and < ManagedClassTypes.First;

    internal static int Index(PrimitiveType type) => checked((int)type - First);

    internal static PrimitiveType Type(int index)
    {
        if (index is < 0 or >= MaximumCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return (PrimitiveType)(First + index);
    }

    internal static bool IsValidType(PrimitiveType type,
        ImmutableArray<BoundEnumDefinition> definitions) =>
        !definitions.IsDefault && IsType(type)
            && (uint)Index(type) < (uint)definitions.Length;

    internal static bool IsValidType(PrimitiveType type, int definitionCount) =>
        IsType(type) && (uint)Index(type) < (uint)definitionCount;

    internal static bool AreEquivalent(BoundEnumDefinition left, BoundEnumDefinition right) =>
        left.Identity == right.Identity && left.Name == right.Name
            && left.UnderlyingType == right.UnderlyingType
            && left.IsPublic == right.IsPublic && left.IsPrimary == right.IsPrimary
            && left.IsFlags == right.IsFlags
            && left.BlittableTargetRequirement == right.BlittableTargetRequirement
            && left.ModuleImport == right.ModuleImport
            && left.Members.SequenceEqual(right.Members);

    internal static bool ContainsRegistration(
        ImmutableArray<BoundEnumDefinition> definitions,
        NativeCoreRegistrationSet registration)
    {
        if (definitions.IsDefault || !registration.IsValid())
        {
            return false;
        }

        foreach (var coreDefinition in registration.EnumDefinitions)
        {
            var expected = BoundEnumDefinition.FromCore(coreDefinition);
            if (!definitions.Any(definition => definition is not null
                && AreEquivalent(definition, expected)))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidTable(ImmutableArray<BoundEnumDefinition> definitions)
    {
        if (definitions.IsDefault || definitions.Length > MaximumCount)
        {
            return false;
        }

        var typeIdentities = new HashSet<CoreDeclarationIdentity>();
        var memberIdentities = new HashSet<CoreDeclarationIdentity>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (definition is null || !definition.Identity.IsValid
                || definition.Identity.Kind != CoreDeclarationKind.EnumType
                || definition.UnderlyingType != PrimitiveType.Int32
                || definition.BlittableTargetRequirement is not (0
                    or FixedLayoutStorage.WindowsX64TargetRequirement)
                || (definition.ModuleImport is BoundModuleImport moduleImport
                    && (moduleImport.ProducerIdentity
                            != definition.Identity.ProviderIdentity
                        || moduleImport.ExportKey != definition.Identity.ExportKey
                        || moduleImport.TargetRequirement > 1
                        || moduleImport.RuntimePolicy > 1))
                || string.IsNullOrWhiteSpace(definition.Name)
                || !definition.IsPublic || !definition.IsPrimary || definition.Members.IsDefault
                || definition.Members.Length > SemanticVerifier.MaximumAnalysisVisits
                || !typeIdentities.Add(definition.Identity)
                || !names.Add(definition.Name))
            {
                return false;
            }

            var memberNames = new HashSet<string>(StringComparer.Ordinal);
            var memberDeclarationKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in definition.Members)
            {
                if (member is null || !member.Identity.IsValid
                    || member.Identity.Kind != CoreDeclarationKind.EnumMember
                    || member.DeclaringEnum != definition.Identity
                    || member.Identity.ProviderIdentity != definition.Identity.ProviderIdentity
                    || string.IsNullOrWhiteSpace(member.Name) || !member.IsPublic
                    || string.IsNullOrWhiteSpace(member.DeclarationKey)
                    || member.DeclarationKey.Contains('\0',
                        StringComparison.Ordinal)
                    || !memberIdentities.Add(member.Identity)
                    || !memberNames.Add(member.Name)
                    || !memberDeclarationKeys.Add(member.DeclarationKey))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static bool UsesAny(BoundProgram program)
    {
        if (program.Structures.Any(static structure =>
                structure.Fields.Any(static field => IsType(field.Type)))
            || program.Classes.Any(static @class =>
                @class.Fields.Any(static field => IsType(field.Type)))
            || program.Buffers.Any(static buffer => IsType(buffer.ElementType))
            || program.NativePointerTypes.Any(static pointer => IsType(pointer.PointeeType))
            || program.NativeFunctionPointerSignatures.Any(static signature =>
                IsType(signature.ReturnType) || signature.ParameterTypes.Any(IsType)
                || signature.NativeParameters.Any(static parameter => IsType(parameter.PointeeType)))
            || (!program.NativeFixedArrayTypes.IsDefaultOrEmpty
                && program.NativeFixedArrayTypes.Any(static array =>
                    IsType(array.ElementType))))
        {
            return true;
        }

        foreach (var function in program.Functions)
        {
            if (IsType(function.ReturnType)
                || function.Variables.Any(static variable => IsType(variable.Type))
                || (function.DirectNativeImport is DirectNativeImport import
                    && (import.Parameters.Any(static parameter => IsType(parameter.PointeeType))
                        || import.Outputs.Any(static output => IsType(output.PointeeType))))
                || (function.NativeVTableMethod is NativeVTableMethodDefinition vtable
                    && vtable.Parameters.Any(static parameter => IsType(parameter.PointeeType)))
                || function.NativeOutputContracts.Any(static output => IsType(output.PointeeType))
                || function.IndexedBody?.Types.Any(IsType) == true)
            {
                return true;
            }

            var usesEnum = false;
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                    expression => usesEnum |= IsType(expression.Type))
                || usesEnum)
            {
                return true;
            }
        }

        return false;
    }
}
