// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Validated module declarations for source binding, without implementation bodies or executable code.</summary>
public sealed class CompilationReference
{
    internal CompilationReference(ImmutableArray<BoundStruct> structures, ImmutableArray<BoundFunction> functions)
    {
        Structures = structures;
        Functions = functions;
    }

    /// <summary>Gets the value layouts exposed by the imported declarations.</summary>
    public ImmutableArray<BoundStruct> Structures { get; }

    /// <summary>Gets external function declarations whose empty bodies are not implementations.</summary>
    public ImmutableArray<BoundFunction> Functions { get; }
}
