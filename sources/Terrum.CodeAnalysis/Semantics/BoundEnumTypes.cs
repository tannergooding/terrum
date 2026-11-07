using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal abstract record BoundEnumDeclarationIdentity
{
    internal sealed record Core(CoreDeclarationIdentity Value)
        : BoundEnumDeclarationIdentity;
    internal sealed record Private(BoundPrivateEnumOccurrence Value)
        : BoundEnumDeclarationIdentity;

    internal bool IsValid => this switch {
        Core core => core.Value.IsValid
            && core.Value.Kind == CoreDeclarationKind.EnumType,
        Private @private => @private.Value.IsInitialized,
        _ => false,
    };

    internal bool TryGetCore(out CoreDeclarationIdentity identity)
    {
        if (this is Core core)
        {
            identity = core.Value;
            return true;
        }

        identity = default;
        return false;
    }
}

internal abstract record BoundEnumMemberIdentity
{
    internal sealed record Core(CoreDeclarationIdentity Value)
        : BoundEnumMemberIdentity;
    internal sealed record Private(
        BoundPrivateEnumOccurrence Owner, string DeclarationKey)
        : BoundEnumMemberIdentity;

    internal bool IsValid => this switch {
        Core core => core.Value.IsValid
            && core.Value.Kind == CoreDeclarationKind.EnumMember,
        Private @private => @private.Owner.IsInitialized
            && !string.IsNullOrEmpty(@private.DeclarationKey)
            && !@private.DeclarationKey.Contains('\0',
                StringComparison.Ordinal),
        _ => false,
    };

    internal bool TryGetCore(out CoreDeclarationIdentity identity)
    {
        if (this is Core core)
        {
            identity = core.Value;
            return true;
        }

        identity = default;
        return false;
    }
}

internal sealed class BoundPrivateEnumOccurrence
{
    private BoundEnumDefinition? _expectedSnapshot;

    internal BoundPrivateEnumOccurrence(
        BoundPrivateNominalTypeOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        Origin = origin;
    }

    internal BoundPrivateNominalTypeOrigin Origin { get; }
    internal BoundEnumDefinition ExpectedSnapshot
        => _expectedSnapshot ?? throw new InvalidOperationException(
            "The private enum occurrence has not been initialized.");
    internal bool IsInitialized => _expectedSnapshot is not null;

    internal void Initialize(BoundEnumDefinition expectedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        if (_expectedSnapshot is not null
            || expectedSnapshot.Identity
                is not BoundEnumDeclarationIdentity.Private identity
            || !ReferenceEquals(identity.Value, this)
            || expectedSnapshot.Members.Any(member =>
                member.Identity
                    is not BoundEnumMemberIdentity.Private memberIdentity
                || !ReferenceEquals(memberIdentity.Owner, this)
                || memberIdentity.DeclarationKey != member.DeclarationKey
                || member.DeclaringEnum
                    is not BoundEnumDeclarationIdentity.Private ownerIdentity
                || !ReferenceEquals(ownerIdentity.Value, this)))
        {
            throw new ArgumentException(
                "The private enum snapshot does not match its occurrence.",
                nameof(expectedSnapshot));
        }

        _expectedSnapshot = expectedSnapshot;
    }
}

internal sealed record BoundEnumDefinition(
    BoundEnumDeclarationIdentity Identity,
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
        new(new BoundEnumDeclarationIdentity.Core(definition.Identity),
            definition.QualifiedName, definition.UnderlyingType,
            [.. definition.Members.Select(static member => new BoundEnumMemberDefinition(
                new BoundEnumMemberIdentity.Core(member.Identity),
                new BoundEnumDeclarationIdentity.Core(member.DeclaringEnum),
                member.Name, member.Value, member.IsPublic,
                member.DeclarationKey))],
            definition.IsPublic, definition.IsPrimary, definition.IsFlags)
        {
            BlittableTargetRequirement = definition.BlittableTargetRequirement,
        };

    internal static BoundEnumDefinition CreatePrivate(
        BoundPrivateNominalTypeOrigin origin, string name,
        PrimitiveType underlyingType,
        ImmutableArray<BoundPrivateEnumMemberSnapshot> members,
        bool isPublic, bool isPrimary, bool isFlags,
        int blittableTargetRequirement,
        BoundModuleImport? moduleImport = null)
    {
        var occurrence = new BoundPrivateEnumOccurrence(origin);
        var identity = new BoundEnumDeclarationIdentity.Private(occurrence);
        var definition = new BoundEnumDefinition(identity, name,
            underlyingType,
            [.. members.Select(member =>
                new BoundEnumMemberDefinition(
                    new BoundEnumMemberIdentity.Private(occurrence,
                        member.DeclarationKey),
                    identity, member.Name, member.Value, member.IsPublic,
                    member.DeclarationKey))],
            isPublic, isPrimary, isFlags)
        {
            BlittableTargetRequirement = blittableTargetRequirement,
            ModuleImport = moduleImport,
        };
        occurrence.Initialize(definition);
        return definition;
    }

    internal static BoundEnumDefinition RemapPrivateOccurrence(
        BoundEnumDefinition definition, PrimitiveType privateType)
    {
        if (definition.Identity
            is not BoundEnumDeclarationIdentity.Private privateIdentity)
        {
            return definition;
        }
        if (!BoundEnumTypes.AreEquivalent(definition,
                privateIdentity.Value.ExpectedSnapshot))
        {
            throw new InvalidOperationException(
                "A private enum occurrence cannot be remapped after its retained expectation changed.");
        }

        var origin = privateIdentity.Value.Origin with {
            PrivateType = privateType,
        };
        return CreatePrivate(origin, definition.Name,
            definition.UnderlyingType, [
                .. definition.Members.Select(static member =>
                    new BoundPrivateEnumMemberSnapshot(member.Name,
                        member.Value, member.IsPublic,
                        member.DeclarationKey)),
            ], definition.IsPublic, definition.IsPrimary,
            definition.IsFlags, definition.BlittableTargetRequirement,
            definition.ModuleImport);
    }
}

internal sealed record BoundEnumMemberDefinition(
    BoundEnumMemberIdentity Identity,
    BoundEnumDeclarationIdentity DeclaringEnum,
    string Name,
    int Value,
    bool IsPublic,
    string DeclarationKey);

internal sealed record BoundPrivateEnumMemberSnapshot(
    string Name, int Value, bool IsPublic, string DeclarationKey);

internal static class BoundEnumTypes
{
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

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

    internal static bool HasSameContractShape(
        BoundEnumDefinition left, BoundEnumDefinition right)
    {
        if (left.Name != right.Name
            || left.UnderlyingType != right.UnderlyingType
            || left.IsPublic != right.IsPublic
            || left.IsPrimary != right.IsPrimary
            || left.IsFlags != right.IsFlags
            || left.BlittableTargetRequirement
                != right.BlittableTargetRequirement
            || left.Members.Length != right.Members.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Members.Length; index++)
        {
            var leftMember = left.Members[index];
            var rightMember = right.Members[index];
            if (leftMember.Name != rightMember.Name
                || leftMember.Value != rightMember.Value
                || leftMember.IsPublic != rightMember.IsPublic
                || leftMember.DeclarationKey != rightMember.DeclarationKey)
            {
                return false;
            }
        }

        return true;
    }

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

    internal static bool MatchesLayoutRegistration(
        ImmutableArray<BoundEnumDefinition> definitions,
        LayoutCoreRegistrationSet registration)
    {
        if (definitions.IsDefault || !registration.IsValid())
        {
            return false;
        }

        var expected = BoundEnumDefinition.FromCore(registration.LayoutKinds);
        foreach (var definition in definitions)
        {
            if (definition is not null
                && definition.Identity == expected.Identity)
            {
                return AreEquivalent(definition, expected);
            }
        }

        return true;
    }

    internal static bool MatchesStaticInitializationRegistration(
        ImmutableArray<BoundEnumDefinition> definitions,
        StaticInitializationCoreRegistrationSet registration)
    {
        if (definitions.IsDefault || !registration.IsValid())
        {
            return false;
        }

        var expected = BoundEnumDefinition.FromCore(registration.TriggerEnum);
        foreach (var definition in definitions)
        {
            if (definition is not null
                && definition.Identity == expected.Identity)
            {
                return AreEquivalent(definition, expected);
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

        var typeIdentities = new HashSet<BoundEnumDeclarationIdentity>();
        var memberIdentities = new HashSet<BoundEnumMemberIdentity>();
        var coreNames = new HashSet<string>(StringComparer.Ordinal);
        for (var definitionIndex = 0;
             definitionIndex < definitions.Length; definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            var coreIdentity = definition?.Identity.TryGetCore(
                out var coreValue) == true
                ? coreValue : (CoreDeclarationIdentity?)null;
            if (definition is null || !definition.Identity.IsValid
                || definition.UnderlyingType != PrimitiveType.Int32
                || definition.BlittableTargetRequirement is not (0
                    or FixedLayoutStorage.WindowsX64TargetRequirement)
                || (definition.ModuleImport is BoundModuleImport moduleImport
                    && (moduleImport.TargetRequirement > 1
                        || moduleImport.RuntimePolicy > 1))
                || string.IsNullOrWhiteSpace(definition.Name)
                || !definition.IsPublic || !definition.IsPrimary || definition.Members.IsDefault
                || definition.Members.Length > SemanticVerifier.MaximumAnalysisVisits
                || !typeIdentities.Add(definition.Identity)
                || (coreIdentity is CoreDeclarationIdentity core
                    && ((definition.ModuleImport is BoundModuleImport import
                            && (import.ProducerIdentity
                                    != core.ProviderIdentity
                                || import.ExportKey != core.ExportKey))
                        || !coreNames.Add(definition.Name)))
                || (definition.Identity
                    is BoundEnumDeclarationIdentity.Private privateIdentity
                    && !IsValidPrivateOrigin(definitionIndex, definition,
                        privateIdentity.Value)))
            {
                return false;
            }

            var memberNames = new HashSet<string>(StringComparer.Ordinal);
            var memberDeclarationKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in definition.Members)
            {
                var memberCoreIdentity = member?.Identity.TryGetCore(
                    out var memberCoreValue) == true
                    ? memberCoreValue
                    : (CoreDeclarationIdentity?)null;
                if (member is null || !member.Identity.IsValid
                    || member.DeclaringEnum != definition.Identity
                    || string.IsNullOrWhiteSpace(member.Name)
                    || !member.IsPublic
                    || !IsValidMemberDeclarationKey(member)
                    || !memberIdentities.Add(member.Identity)
                    || !memberNames.Add(member.Name)
                    || !memberDeclarationKeys.Add(member.DeclarationKey))
                {
                    return false;
                }

                if (memberCoreIdentity is CoreDeclarationIdentity memberCore)
                {
                    if (coreIdentity is not CoreDeclarationIdentity enumCore
                        || memberCore.ProviderIdentity
                            != enumCore.ProviderIdentity)
                    {
                        return false;
                    }
                }
                else if (member.Identity
                        is BoundEnumMemberIdentity.Private privateMember
                    && (definition.Identity
                            is not BoundEnumDeclarationIdentity.Private
                                privateEnum
                        || !ReferenceEquals(privateMember.Owner,
                            privateEnum.Value)
                        || privateMember.DeclarationKey
                            != member.DeclarationKey))
                {
                    return false;
                }
            }
        }

        return true;

        static bool IsValidPrivateOrigin(int index,
            BoundEnumDefinition definition,
            BoundPrivateEnumOccurrence occurrence)
        {
            var origin = occurrence.Origin;
            var expectedSnapshot = occurrence.ExpectedSnapshot;
            if (origin.PrivateType != Type(index)
                || !AreEquivalent(expectedSnapshot, definition)
                || expectedSnapshot.UnderlyingType
                    != PrimitiveType.Int32
                || origin.OriginKind
                    is not (BoundPrivateNominalOriginKind.OwnedDefinition
                        or BoundPrivateNominalOriginKind.ImportedExpectation)
                || string.IsNullOrWhiteSpace(origin.UsingModuleIdentity)
                || string.IsNullOrEmpty(origin.LocalDeclarationKey)
                || origin.LocalDeclarationKey.Contains('\0',
                    StringComparison.Ordinal)
                || (origin.OriginKind
                        == BoundPrivateNominalOriginKind.OwnedDefinition
                    && (origin.SelectedModuleIdentity is not null
                        || origin.SelectedExportKey is not null))
                || (origin.OriginKind
                        == BoundPrivateNominalOriginKind.ImportedExpectation
                    && (origin.IsExported
                        || string.IsNullOrWhiteSpace(
                            origin.SelectedModuleIdentity)
                        || string.IsNullOrWhiteSpace(
                            origin.SelectedExportKey))))
            {
                return false;
            }

            return definition.ModuleImport is not BoundModuleImport import
                || (origin.OriginKind
                        == BoundPrivateNominalOriginKind.ImportedExpectation
                    && import.ProducerIdentity
                        == origin.SelectedModuleIdentity
                    && import.ExportKey == origin.SelectedExportKey);
        }

        static bool IsValidMemberDeclarationKey(
            BoundEnumMemberDefinition member)
        {
            if (member.Identity is not BoundEnumMemberIdentity.Private)
            {
                return !string.IsNullOrWhiteSpace(member.DeclarationKey)
                    && !member.DeclarationKey.Contains('\0',
                        StringComparison.Ordinal);
            }

            if (string.IsNullOrEmpty(member.DeclarationKey)
                || member.DeclarationKey.Contains('\0',
                    StringComparison.Ordinal))
            {
                return false;
            }
            try
            {
                return s_strictUtf8.GetByteCount(member.DeclarationKey)
                    is >= 1 and <= 256;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }
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
