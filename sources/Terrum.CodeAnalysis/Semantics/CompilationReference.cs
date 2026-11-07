// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

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
        ImmutableArray<BoundNativeFixedArrayType> nativeFixedArrayTypes = default,
        ImmutableArray<CoreExceptionIdentitySelection> coreExceptionIdentitySelections = default,
        LayoutCoreRegistrationSet? layoutCoreRegistration = null,
        ImmutableArray<BoundPrivateNominalTypeOrigin>
            privateNominalTypeOrigins = default,
        ImmutableArray<BoundClass> classes = default,
        ImmutableArray<BoundPrivateClassReference>
            privateClassReferences = default,
        StaticInitializationCoreRegistrationSet?
            staticInitializationCoreRegistration = null,
        ImmutableArray<CoreContractFailureIdentitySelection>
            coreContractFailureIdentitySelections = default,
        LazyInitializationCoreRegistrationSet?
            lazyInitializationCoreRegistration = null)
    {
        if (nativeCoreRegistration is not null && !nativeCoreRegistration.IsValid())
        {
            throw new ArgumentException("The selected Core native registration is invalid.",
                nameof(nativeCoreRegistration));
        }
        if (!classes.IsDefault
            && classes.Length > ManagedClassTypes.OptionalFirst
                - ManagedClassTypes.First)
        {
            throw new ArgumentException(
                "The selected managed class table exceeds its type identity range.",
                nameof(classes));
        }
        if (!privateClassReferences.IsDefault)
        {
            foreach (var reference in privateClassReferences)
            {
                if (!IsValidPrivateClassReference(reference,
                        privateNominalTypeOrigins, classes,
                        checkedReferenceTypes.IsDefault
                            ? [] : checkedReferenceTypes,
                        nativeFunctionPointerSignatures.IsDefault
                            ? [] : nativeFunctionPointerSignatures,
                        nativePointerTypes.IsDefault
                            ? [] : nativePointerTypes,
                        nativeFixedArrayTypes.IsDefault
                            ? [] : nativeFixedArrayTypes,
                        enumDefinitions.IsDefault
                            ? [] : enumDefinitions))
                {
                    throw new ArgumentException(
                        "The selected private class reference is invalid.",
                        nameof(privateClassReferences));
                }
            }
        }
        if (layoutCoreRegistration is not null && !layoutCoreRegistration.IsValid())
        {
            throw new ArgumentException("The selected Core layout registration is invalid.",
                nameof(layoutCoreRegistration));
        }
        if (staticInitializationCoreRegistration is not null
            && !staticInitializationCoreRegistration.IsValid())
        {
            throw new ArgumentException(
                "The selected static-initialization Core registration is invalid.",
                nameof(staticInitializationCoreRegistration));
        }
        if (lazyInitializationCoreRegistration is not null
            && !lazyInitializationCoreRegistration.IsValid())
        {
            throw new ArgumentException(
                "The selected LazyInitialization Core registration is invalid.",
                nameof(lazyInitializationCoreRegistration));
        }
        if (nativeCoreRegistration is not null && layoutCoreRegistration is not null
            && !ReferenceEquals(nativeCoreRegistration.ModuleBindings,
                layoutCoreRegistration.ModuleBindings))
        {
            throw new ArgumentException(
                "The selected Core native and layout registrations must share one provider binding snapshot.",
                nameof(layoutCoreRegistration));
        }
        var selectedBindings = nativeCoreRegistration?.ModuleBindings
            ?? layoutCoreRegistration?.ModuleBindings
            ?? staticInitializationCoreRegistration?.ModuleBindings
            ?? lazyInitializationCoreRegistration?.ModuleBindings;
        if ((layoutCoreRegistration is not null
                && !ReferenceEquals(selectedBindings,
                    layoutCoreRegistration.ModuleBindings))
            || (staticInitializationCoreRegistration is not null
                && !ReferenceEquals(selectedBindings,
                    staticInitializationCoreRegistration.ModuleBindings))
            || (lazyInitializationCoreRegistration is not null
                && !ReferenceEquals(selectedBindings,
                    lazyInitializationCoreRegistration.ModuleBindings)))
        {
            throw new ArgumentException(
                "Selected Core registrations must share one provider binding snapshot.",
                nameof(lazyInitializationCoreRegistration));
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
        if (!CoreExceptionIdentitySelectionContract.IsValidTable(
            coreExceptionIdentitySelections))
        {
            throw new ArgumentException(
                "The selected Core exception identities are invalid.",
                nameof(coreExceptionIdentitySelections));
        }
        if (!CoreContractFailureIdentitySelectionContract.IsValidTable(
            coreContractFailureIdentitySelections))
        {
            throw new ArgumentException(
                "The selected Core contract-failure identities are invalid.",
                nameof(coreContractFailureIdentitySelections));
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
        LayoutCoreRegistration = layoutCoreRegistration;
        StaticInitializationCoreRegistration
            = staticInitializationCoreRegistration;
        LazyInitializationCoreRegistration
            = lazyInitializationCoreRegistration;
        EnumDefinitions = enumDefinitions.IsDefault ? [] : enumDefinitions;
        CheckedReferenceTypes = checkedReferenceTypes.IsDefault ? [] : checkedReferenceTypes;
        Interfaces = interfaces.IsDefault ? [] : interfaces;
        GenericNominalTypeDefinitions = genericNominalTypeDefinitions.IsDefault
            ? [] : genericNominalTypeDefinitions;
        GenericNominalTypeInstances = genericNominalTypeInstances.IsDefault
            ? [] : genericNominalTypeInstances;
        CoreTaskType = coreTaskType;
        CoreExceptionIdentitySelections
            = coreExceptionIdentitySelections.IsDefault
                ? [] : coreExceptionIdentitySelections;
        CoreContractFailureIdentitySelections
            = coreContractFailureIdentitySelections.IsDefault
                ? [] : coreContractFailureIdentitySelections;
        PrivateNominalTypeOrigins = privateNominalTypeOrigins.IsDefault
            ? [] : privateNominalTypeOrigins;
        Classes = classes.IsDefault ? [] : classes;
        PrivateClassReferences = privateClassReferences.IsDefault
            ? [] : privateClassReferences;
    }

    private static bool IsValidPrivateClassReference(
        BoundPrivateClassReference? reference,
        ImmutableArray<BoundPrivateNominalTypeOrigin> origins,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
        ImmutableArray<BoundEnumDefinition> enumDefinitions)
    {
        if (reference is null || reference.Origin is null
            || reference.ExpectedSnapshot is null
            || origins.IsDefault || classes.IsDefault
            || !ManagedClassTypes.IsClass(reference.ClassType)
            || !ManagedClassTypes.IsClass(reference.ExpectedClassType))
        {
            return false;
        }

        var snapshot = reference.ExpectedSnapshot;
        var origin = reference.Origin;
        var classIndex = ManagedClassTypes.ClassIndex(
            reference.ClassType);
        var expectedClassIndex = ManagedClassTypes.ClassIndex(
            reference.ExpectedClassType);
        return reference.ClassType
                == (PrimitiveType)(ManagedClassTypes.First + classIndex)
            && (uint)classIndex < (uint)classes.Length
            && !snapshot.Structures.IsDefault
            && !snapshot.Classes.IsDefault
            && !snapshot.CheckedReferenceTypes.IsDefault
            && !snapshot.NativeFunctionPointerSignatures.IsDefault
            && !snapshot.NativePointerTypes.IsDefault
            && !snapshot.NativeFixedArrayTypes.IsDefault
            && !snapshot.NominalTypeOrigins.IsDefault
            && !snapshot.EnumDefinitions.IsDefault
            && BoundEnumTypes.IsValidTable(snapshot.EnumDefinitions)
            && IsValidMapping(reference.ExpectedNativePointerMapping,
                snapshot.NativePointerTypes.Length,
                nativePointerTypes.Length)
            && IsValidMapping(
                reference.ExpectedNativeFunctionPointerMapping,
                snapshot.NativeFunctionPointerSignatures.Length,
                functionPointerSignatures.Length)
            && IsValidMapping(reference.ExpectedNativeFixedArrayMapping,
                snapshot.NativeFixedArrayTypes.Length,
                fixedArrayTypes.Length)
            && IsValidMapping(reference.ExpectedCheckedReferenceMapping,
                snapshot.CheckedReferenceTypes.Length,
                checkedReferenceTypes.Length)
            && IsValidMapping(reference.ExpectedEnumMapping,
                snapshot.EnumDefinitions.Length,
                enumDefinitions.Length)
            && IsValidMapping(reference.ExpectedClassMapping,
                snapshot.Classes.Length, classes.Length)
            && (uint)expectedClassIndex
                < (uint)snapshot.Classes.Length
            && reference.ExpectedClassMapping[expectedClassIndex]
                == classIndex
            && origin.PrivateType == reference.ClassType
            && origins.Contains(origin)
            && origins.Any(candidate =>
                candidate.PrivateType == reference.ClassType)
            && classes[classIndex].Name
                == snapshot.Classes[expectedClassIndex].Name
            && classes[classIndex].Size
                == snapshot.Classes[expectedClassIndex].Size
            && classes[classIndex].Fields.Length
                == snapshot.Classes[expectedClassIndex].Fields.Length;

        static bool IsValidMapping(ImmutableArray<int> mapping,
            int sourceCount, int destinationCount)
        {
            return !mapping.IsDefault && mapping.Length == sourceCount
                && mapping.All(index => index >= -1
                    && (index < 0
                        || (uint)index < (uint)destinationCount));
        }
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

    // This host-selected Core profile is deliberately separate from normalized module declarations.
    internal LayoutCoreRegistrationSet? LayoutCoreRegistration { get; }

    internal StaticInitializationCoreRegistrationSet?
        StaticInitializationCoreRegistration { get; }

    internal LazyInitializationCoreRegistrationSet?
        LazyInitializationCoreRegistration { get; }

    internal ImmutableArray<BoundEnumDefinition> EnumDefinitions { get; }

    internal ImmutableArray<CheckedReferenceTypeDefinition> CheckedReferenceTypes { get; }

    internal ImmutableArray<BoundPrivateNominalTypeOrigin>
        PrivateNominalTypeOrigins { get; }

    internal ImmutableArray<BoundClass> Classes { get; }

    internal ImmutableArray<BoundPrivateClassReference>
        PrivateClassReferences { get; }

    internal ImmutableArray<BoundInterfaceDefinition> Interfaces { get; }

    /// <summary>The exact nominal TypeDefs selected from checked providers.</summary>
    public ImmutableArray<BoundGenericNominalTypeDefinition> GenericNominalTypeDefinitions { get; }

    /// <summary>Nominal TypeDef uses retained by an imported declaration set.</summary>
    public ImmutableArray<BoundGenericNominalTypeInstance> GenericNominalTypeInstances { get; }

    /// <summary>The host-selected exact Core Task declaration, if one was selected for this provider set.</summary>
    public CoreTaskTypeIdentity? CoreTaskType { get; }

    /// <summary>Exact host-selected Core exception anchors for using Modules in this reference.</summary>
    public ImmutableArray<CoreExceptionIdentitySelection>
        CoreExceptionIdentitySelections { get; }

    /// <summary>Exact host-selected Core contract-failure identities for originating using Modules.</summary>
    public ImmutableArray<CoreContractFailureIdentitySelection>
        CoreContractFailureIdentitySelections { get; }

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
            selection, NativeFixedArrayTypes, CoreExceptionIdentitySelections,
            LayoutCoreRegistration,
            privateNominalTypeOrigins: PrivateNominalTypeOrigins,
            classes: Classes,
            privateClassReferences: PrivateClassReferences,
            staticInitializationCoreRegistration:
                StaticInitializationCoreRegistration,
            coreContractFailureIdentitySelections:
                CoreContractFailureIdentitySelections,
            lazyInitializationCoreRegistration:
                LazyInitializationCoreRegistration);
    }
}
