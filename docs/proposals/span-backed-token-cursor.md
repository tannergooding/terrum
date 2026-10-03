# Span-backed token cursor proposal

**Status: initial direction accepted; implementation not yet supported.**
This proposal identifies the smallest runtime and source-binding contracts
needed to execute the existing [`TokenCursor`](../../self-host/TokenCursor.terrum)
candidate without copying its `Span<Token>` backing or claiming ownership of
it. The accepted slice is limited to this readonly Copy-indexed cursor; it
does not approve general stored views or imply current compiler support.

## Current boundary

`Span<T>` is one qualified view family with opaque backing dependencies.
Read-only element access, writable storage access, mutable descriptor
rebinding, and writable element referents are distinct capabilities.
Forwarding a descriptor does not keep its backing allocation or guard alive;
the backing dependencies must remain valid through each use. The exact anchor
representation, calling convention and source relationships remain open in
the [lifetime specification](../lifetimes.md). The candidate currently requires
Span indexing, matched access, stored backing dependencies, a capture contract
and constructor initialization before it can be executed.

## Accepted initial slice

For the first `TokenCursor` implementation, use a fixed-size runtime descriptor
containing the data address and element count. Keep backing and guard
provenance as compiler-checked dependency facts; these facts are forwarded by
capture, but are not runtime roots, owners or leases and do not extend their
lifetimes. The first slice accepts readonly `Span<T>` access only when `T` is
`Copy`, checks `nuint` indexes before reading, and returns elements by value.
`[Captures(nameof(tokens))]` forwards the complete dependency set to the
cursor field; construction is rejected if any dependency cannot be proven to
outlive the cursor's uses. Validate all fields and captures before publishing
the cursor.

This direction does not add writable views, ref-returning element loans,
arbitrary view construction or slicing, owner/guard retention, persisted
cross-module capture metadata, or a parser integration contract. Broader
`Span<T>` construction and escape rules remain open; this initial slice must
not be described as general stored-view support.

## Decisions and rationale

### 1. Stored-view representation

**Summary:** A stored `Span<T>` needs enough information to address its range
and carry its backing, access and guard dependencies. A pointer and length
alone do not prove that the backing remains valid, while an owner generic
argument or per-slice allocation would change the accepted family contract.

**Selected:** Use a fixed-size descriptor with compiler-checked opaque
dependency provenance. Forward the existing backing and guard obligations
without retaining or extending their lifetimes. Do not introduce per-slice
allocations, a global pointer-to-owner registry or an owner type parameter.

### 2. Capture relationship

**Summary:** `TokenCursor` stores the constructor's `Span<Token>` argument in
its `_tokens` field. The capture must preserve the view's validity and access
without turning the cursor into an owner or making a temporary backing live
longer.

**Selected:** Define `[Captures(nameof(tokens))]` as a checked forwarding
relationship from the named input's complete backing dependency set to the
stored field. It may not acquire ownership, prolong an allocation lease,
weaken access, or omit a guard dependency. Reject construction or escape when
the compiler cannot prove that every dependency outlives the cursor's uses.

### 3. Initial indexer access

**Summary:** The candidate reads `Token` values from a readonly
`Span<Token>`. The general Span family also distinguishes mutable descriptor
rebinding, writable slots, and writable contained handles.

**Selected:** Begin with checked `nuint` indexing on readonly spans whose
elements are `Copy`, returning the element by value. Match the indexer
receiver's access to the view capability: readonly spans cannot write slots,
`mut Span<T>` only rebinds the descriptor, and `write Span<T>` is required to
mutate storage. Keep ref-returning element loans and writable-view retention
outside the cursor slice.

### 4. Constructor publication

**Summary:** The constructor must initialize the span field and validate its
capture before the cursor can be used. A partially initialized unique value
must not expose a descriptor whose dependencies were not checked.

**Selected:** Apply ordinary definite-initialization checks to every field,
then validate and attach the declared capture dependencies before the
constructed value becomes observable. Failure leaves no published cursor;
the operation does not transfer or duplicate cleanup responsibility for the
backing.

## Scope after approval

The first implementation target is the existing readonly `TokenCursor`:
checked length/index reads, `Copy` token results, and dependency forwarding
through its constructor and cursor value. It does not add arbitrary stored
views, slicing, generic collection iteration, writable spans, new cleanup
rules or a parser integration contract. The separate
[`HostBuffer`](../native.md) interchange surfaces and `ManagedTokenChain`
remain unchanged and are not Span substitutes.
