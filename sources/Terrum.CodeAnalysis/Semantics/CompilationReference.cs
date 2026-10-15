// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Validated module declarations for source binding, without implementation bodies or executable code.</summary>
public sealed class CompilationReference
{
    internal CompilationReference(ImmutableArray<BoundStruct> structures, ImmutableArray<BoundFunction> functions,
        BoundRuntimePolicy runtimePolicy, bool requiresSourceGrant = false, string? invocationProfileId = null,
        NativeImportCatalog? nativeImportCatalog = null)
    {
        Structures = structures;
        Functions = functions;
        RuntimePolicy = runtimePolicy;
        RequiresSourceGrant = requiresSourceGrant;
        InvocationProfileId = invocationProfileId;
        NativeImportCatalog = nativeImportCatalog;
    }

    /// <summary>Gets the value layouts exposed by the imported declarations.</summary>
    public ImmutableArray<BoundStruct> Structures { get; }

    /// <summary>Gets external function declarations whose empty bodies are not implementations.</summary>
    public ImmutableArray<BoundFunction> Functions { get; }

    /// <summary>The checked-failure behavior required by imported declarations.</summary>
    public BoundRuntimePolicy RuntimePolicy { get; }

    /// <summary>Whether the retained provider contract requires an invocation-scoped <c>Host.Source</c> grant.</summary>
    public bool RequiresSourceGrant { get; }

    /// <summary>The retained provider's named invocation profile, if it has one.</summary>
    public string? InvocationProfileId { get; }

    internal NativeImportCatalog? NativeImportCatalog { get; }
}
