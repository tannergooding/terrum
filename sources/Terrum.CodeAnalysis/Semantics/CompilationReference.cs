// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Validated module declarations for source binding, without implementation bodies or executable code.</summary>
public sealed class CompilationReference
{
    internal CompilationReference(ImmutableArray<BoundStruct> structures, ImmutableArray<BoundFunction> functions,
        BoundRuntimePolicy runtimePolicy, bool requiresSourceGrant = false, string? invocationProfileId = null,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> nativeFunctionPointerSignatures = default,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes = default,
        NativeCoreRegistrationSet? nativeCoreRegistration = null,
        ImmutableArray<BoundEnumDefinition> enumDefinitions = default,
        ImmutableArray<CheckedReferenceTypeDefinition> checkedReferenceTypes = default,
        ImmutableArray<BoundInterfaceDefinition> interfaces = default,
        ImmutableArray<BoundGenericNominalTypeDefinition> genericNominalTypeDefinitions = default,
        ImmutableArray<BoundGenericNominalTypeInstance> genericNominalTypeInstances = default,
        CoreTaskTypeIdentity? coreTaskType = null,
        ImmutableArray<BoundNativeFixedArrayType> nativeFixedArrayTypes = default)
    {
        if (nativeCoreRegistration is not null && !nativeCoreRegistration.IsValid())
        {
            throw new ArgumentException("The selected Core native registration is invalid.",
                nameof(nativeCoreRegistration));
        }
        if (!enumDefinitions.IsDefault && !BoundEnumTypes.IsValidTable(enumDefinitions))
        {
            throw new ArgumentException("The selected nominal enum declarations are invalid.",
                nameof(enumDefinitions));
        }
        if (!checkedReferenceTypes.IsDefault
            && !CheckedReferenceTypeContract.IsValidTable(checkedReferenceTypes))
        {
            throw new ArgumentException("The selected checked-reference types are invalid.",
                nameof(checkedReferenceTypes));
        }
        if (!nativeFixedArrayTypes.IsDefault
            && !NativeFixedArrayContract.IsValidTable(structures,
                nativeFixedArrayTypes))
        {
            throw new ArgumentException("The selected fixed-array types are invalid.",
                nameof(nativeFixedArrayTypes));
        }
        if (!interfaces.IsDefault && !BoundInterfaceContract.IsValidTable(interfaces,
            static _ => true))
        {
            throw new ArgumentException("The selected interface declarations are invalid.",
                nameof(interfaces));
        }
        if (!genericNominalTypeDefinitions.IsDefault
            && !BoundGenericNominalTypeContract.IsValidDefinitionTable(
                genericNominalTypeDefinitions))
        {
            throw new ArgumentException("The selected generic nominal declarations are invalid.",
                nameof(genericNominalTypeDefinitions));
        }
        if (!genericNominalTypeInstances.IsDefault
            && !BoundGenericNominalTypeContract.IsValidInstanceTable(
                genericNominalTypeInstances,
                genericNominalTypeDefinitions.IsDefault
                    ? [] : genericNominalTypeDefinitions))
        {
            throw new ArgumentException("The selected generic nominal instances are invalid.",
                nameof(genericNominalTypeInstances));
        }
        if (!BoundGenericNominalTypeContract.IsValidCoreTaskSelection(
            coreTaskType, genericNominalTypeDefinitions.IsDefault
                ? [] : genericNominalTypeDefinitions))
        {
            throw new ArgumentException("The selected Core Task identity is invalid.",
                nameof(coreTaskType));
        }

        Structures = structures;
        Functions = functions;
        RuntimePolicy = runtimePolicy;
        RequiresSourceGrant = requiresSourceGrant;
        InvocationProfileId = invocationProfileId;
        NativeFunctionPointerSignatures = nativeFunctionPointerSignatures.IsDefault
            ? [] : nativeFunctionPointerSignatures;
        NativePointerTypes = nativePointerTypes.IsDefault ? [] : nativePointerTypes;
        NativeFixedArrayTypes = nativeFixedArrayTypes.IsDefault
            ? [] : nativeFixedArrayTypes;
        NativeCoreRegistration = nativeCoreRegistration;
        EnumDefinitions = enumDefinitions.IsDefault ? [] : enumDefinitions;
        CheckedReferenceTypes = checkedReferenceTypes.IsDefault ? [] : checkedReferenceTypes;
        Interfaces = interfaces.IsDefault ? [] : interfaces;
        GenericNominalTypeDefinitions = genericNominalTypeDefinitions.IsDefault
            ? [] : genericNominalTypeDefinitions;
        GenericNominalTypeInstances = genericNominalTypeInstances.IsDefault
            ? [] : genericNominalTypeInstances;
        CoreTaskType = coreTaskType;
    }

    /// <summary>Gets the value layouts exposed by the imported declarations.</summary>
    public ImmutableArray<BoundStruct> Structures { get; }

    /// <summary>Gets imported declarations and dispatch stubs whose empty bodies are not Terrum implementations.</summary>
    public ImmutableArray<BoundFunction> Functions { get; }

    /// <summary>The checked-failure behavior required by imported declarations.</summary>
    public BoundRuntimePolicy RuntimePolicy { get; }

    /// <summary>Whether the retained provider contract requires an invocation-scoped <c>Host.Source</c> grant.</summary>
    public bool RequiresSourceGrant { get; }

    /// <summary>The retained provider's named invocation profile, if it has one.</summary>
    public string? InvocationProfileId { get; }

    internal ImmutableArray<NativeFunctionPointerSignatureDefinition> NativeFunctionPointerSignatures { get; }

    internal ImmutableArray<NativePointerTypeDefinition> NativePointerTypes { get; }

    internal ImmutableArray<BoundNativeFixedArrayType> NativeFixedArrayTypes { get; }

    // This host-selected Core profile is deliberately separate from normalized module declarations.
    internal NativeCoreRegistrationSet? NativeCoreRegistration { get; }

    internal ImmutableArray<BoundEnumDefinition> EnumDefinitions { get; }

    internal ImmutableArray<CheckedReferenceTypeDefinition> CheckedReferenceTypes { get; }

    internal ImmutableArray<BoundInterfaceDefinition> Interfaces { get; }

    /// <summary>The exact nominal TypeDefs selected from checked providers.</summary>
    public ImmutableArray<BoundGenericNominalTypeDefinition> GenericNominalTypeDefinitions { get; }

    /// <summary>Nominal TypeDef uses retained by an imported declaration set.</summary>
    public ImmutableArray<BoundGenericNominalTypeInstance> GenericNominalTypeInstances { get; }

    /// <summary>The host-selected exact Core Task declaration, if one was selected for this provider set.</summary>
    public CoreTaskTypeIdentity? CoreTaskType { get; }

    /// <summary>Returns a reference that explicitly selects one checked provider definition as Core Task.</summary>
    public CompilationReference SelectCoreTaskType(BoundGenericNominalTypeIdentity identity)
    {
        var selection = new CoreTaskTypeIdentity(identity);
        if (!BoundGenericNominalTypeContract.IsValidCoreTaskSelection(
            selection, GenericNominalTypeDefinitions))
        {
            throw new ArgumentException(
                "The selected identity does not name a checked Terrum.Threading.Tasks.Task<T = void> definition.",
                nameof(identity));
        }

        if (CoreTaskType is not null)
        {
            throw new InvalidOperationException("A Core Task identity is already selected.");
        }

        return new CompilationReference(Structures, Functions, RuntimePolicy,
            RequiresSourceGrant, InvocationProfileId,
            NativeFunctionPointerSignatures, NativePointerTypes,
            NativeCoreRegistration, EnumDefinitions, CheckedReferenceTypes,
            Interfaces, GenericNominalTypeDefinitions, GenericNominalTypeInstances,
            selection, NativeFixedArrayTypes);
    }
}
