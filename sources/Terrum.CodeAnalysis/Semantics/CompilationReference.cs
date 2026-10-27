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
        NativeCoreRegistrationSet? nativeCoreRegistration = null)
    {
        if (nativeCoreRegistration is not null && !nativeCoreRegistration.IsValid())
        {
            throw new ArgumentException("The selected Core native registration is invalid.",
                nameof(nativeCoreRegistration));
        }

        Structures = structures;
        Functions = functions;
        RuntimePolicy = runtimePolicy;
        RequiresSourceGrant = requiresSourceGrant;
        InvocationProfileId = invocationProfileId;
        NativeFunctionPointerSignatures = nativeFunctionPointerSignatures.IsDefault
            ? [] : nativeFunctionPointerSignatures;
        NativePointerTypes = nativePointerTypes.IsDefault ? [] : nativePointerTypes;
        NativeCoreRegistration = nativeCoreRegistration;
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

    // This host-selected Core profile is deliberately separate from normalized module declarations.
    internal NativeCoreRegistrationSet? NativeCoreRegistration { get; }
}
