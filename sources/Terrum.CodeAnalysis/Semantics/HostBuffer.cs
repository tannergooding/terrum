// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>A fixed, initialized buffer capability supplied explicitly by the compilation host.</summary>
/// <param name="Name">The identifier exposed through the reserved Host boundary.</param>
/// <param name="ElementType">The primitive type of each element.</param>
/// <param name="Values">Initial element bits, copied into execution-owned storage.</param>
/// <param name="IsWritable">Whether checked element assignment is permitted.</param>
/// <remarks>
/// Values use one 32-bit storage word per element, including code units. This is a
/// bounded host interchange layout, not the language's general array or span ABI.
/// Source cannot construct, resize, return, store or transfer the capability.
/// Invocation-scoped buffers are rebound by their named profile adapter;
/// persisted modules retain only profile metadata and symbolic operations.
/// Its backing exists for the entire execution, and all accesses are checked.
/// </remarks>
public sealed record HostBuffer(string Name, PrimitiveType ElementType, ImmutableArray<int> Values, bool IsWritable)
{
    /// <summary>Whether the capability is supplied anew for each invocation and must not bind sample values.</summary>
    public bool IsInvocationScoped { get; init; }

    /// <summary>The named execution profile that owns this invocation-scoped buffer, if any.</summary>
    public string? InvocationProfileId { get; init; }
}
