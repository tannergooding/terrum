# Span-backed token cursor proposal

**Status: unapproved.** This proposal identifies the smallest runtime and
source-binding contracts needed to execute the existing
[`TokenCursor`](../../self-host/TokenCursor.terrum) candidate without copying
its `Span<Token>` backing or claiming ownership of it. It does not change the
approved grammar, current lifetime rules, metadata design or compiler support.

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

## Questions for direction

### 1. What is the initial stored-view representation?

**Summary:** A stored `Span<T>` needs enough information to address its range
and carry its backing, access and guard dependencies. A pointer and length
alone do not prove that the backing remains valid, while an owner generic
argument or per-slice allocation would change the accepted family contract.

**Recommendation:** Start with a fixed-size descriptor and opaque dependency
anchors whose provenance is checked by the compiler. Forward the existing
backing and guard obligations without retaining or extending their lifetimes.
Do not introduce per-slice allocations, a global pointer-to-owner registry or
an owner type parameter.

### 2. What does `[Captures]` authorize?

**Summary:** `TokenCursor` stores the constructor's `Span<Token>` argument in
its `_tokens` field. The capture must preserve the view's validity and access
without turning the cursor into an owner or making a temporary backing live
longer.

**Recommendation:** Define `[Captures(nameof(tokens))]` as a checked
forwarding relationship from the named input's complete backing dependency
set to the stored field. It may not acquire ownership, prolong an allocation
lease, weaken access, or omit a guard dependency. Reject construction or
escape when the compiler cannot prove that every dependency outlives the
cursor's uses.

### 3. What access does the first indexer slice expose?

**Summary:** The candidate reads `Token` values from a readonly
`Span<Token>`. The general Span family also distinguishes mutable descriptor
rebinding, writable slots, and writable contained handles.

**Recommendation:** Begin with checked `nuint` indexing on readonly spans
whose elements are `Copy`, returning the element by value. Match the indexer
receiver's access to the view capability: readonly spans cannot write slots,
`mut Span<T>` only rebinds the descriptor, and `write Span<T>` is required to
mutate storage. Keep ref-returning element loans and writable-view retention
outside the cursor slice.

### 4. When may a capturing constructor publish its value?

**Summary:** The constructor must initialize the span field and validate its
capture before the cursor can be used. A partially initialized unique value
must not expose a descriptor whose dependencies were not checked.

**Recommendation:** Apply ordinary definite-initialization checks to every
field, then validate and attach the declared capture dependencies before the
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
