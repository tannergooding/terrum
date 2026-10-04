# Span-backed token cursor proposal

**Status: initial cursor direction accepted; a bounded compilation-local
implementation path is selected provisionally, but remains unsupported until
its proof and regression gates pass.**
This proposal identifies the smallest runtime and source-binding contracts
needed to execute the existing [`TokenCursor`](../../self-host/TokenCursor.terrum)
candidate without copying its `Span<Token>` backing or claiming ownership of
it. The implementation slice is limited to this readonly Copy-indexed cursor;
it does not approve general stored views or imply current compiler support.

## Current boundary

`Span<T>` is one qualified view family with opaque backing dependencies.
Read-only element access, writable storage access, mutable descriptor
rebinding, and writable element referents are distinct capabilities.
Forwarding a descriptor does not keep its backing allocation or guard alive;
the backing dependencies must remain valid through each use. The exact anchor
representation, calling convention and source relationships remain open in
the [lifetime specification](../lifetimes.md) outside this slice. For the
initial cursor, use a fixed 16-byte x64 descriptor containing a data address
and element count; keep backing and guard provenance compiler-checked rather
than storing runtime roots or leases. The candidate still requires Span
indexing, matched access, stored backing dependencies, a capture contract and
constructor initialization before it can be executed.

## Accepted initial slice

For the first `TokenCursor` implementation, use a fixed-size runtime descriptor
containing the data address and element count. Keep backing and guard
provenance as compiler-checked dependency facts; these facts are forwarded by
capture, but are not runtime roots, owners or leases and do not extend their
lifetimes. The first slice accepts readonly `Span<T>` access only when `T` is
`Copy`, checks `nuint` indexes before reading, and returns elements by value.
`[Captures(nameof(tokens))]` forwards the complete dependency set to the
cursor field; construction is rejected if any dependency cannot be proven to
outlive the cursor's uses. To keep the cursor within the current 16-byte
inline-value limit, store the remaining view in one `mut Span<T>` field
instead of storing a second position counter. A successful read checks for a
nonempty view, reads element zero, and advances that descriptor by one
element. Validate all fields and captures before publishing the cursor.

This direction does not add writable views, ref-returning element loans,
arbitrary view construction or slicing, owner/guard retention, persisted
cross-module capture metadata, or a parser integration contract. The
cursor-only `AdvanceOne` operation requires a nonempty descriptor, rebinds
only the descriptor, and preserves its complete dependency set; it does not
establish a general slicing API. Broader `Span<T>` construction and escape
rules remain open; this initial slice must not be described as general
stored-view support.

## Remaining execution prerequisite: acquiring the input view

The accepted cursor rules describe how a `Span<Token>` is stored and read,
while current scanner components expose managed token storage. The provisional
bridge below uses only a completed `TokenSnapshot`; it does not define general
parser integration or convert `HostBuffer` or `ManagedTokenChain` into spans.
Those remain separate contracts.

**Summary:** An end-to-end cursor needs both a specified source for its input
view and a proof that the view's complete dependencies remain valid through
cursor use. A raw pointer/length bridge would establish neither the initialized
snapshot prefix nor the snapshot owner's cleanup lifetime.

**Recommendation (provisional):** Implement the compiler-owned readonly
`Span<T>` representation, stored-dependency proof, and a narrow
`TokenSnapshot`-to-`Span<Token>` projection as one compilation-local unit;
do not implement the raw bridge first. Limit `T` to eligible closed `Copy`
elements, initially `Token`, and the current Windows x64 native profile.
Validate the snapshot's complete initialized three-word prefix, scanner kind
and source-range records before publishing the typed view; check all count,
stride and address calculations without overflow. Verify the resolved `Token`
layout (field offsets 0/4/8, 12-byte stride, four-byte alignment) before
lowering by-value element loads. Preserve the snapshot owner as an opaque
compile-time dependency through the view, captured cursor field, cursor moves,
control-flow joins and exceptional unwinding; moving or ending the owner while
any dependent view/cursor is live must be rejected. Empty and exhausted views
retain the same dependency. Failed construction publishes no descriptor and
does not transfer or duplicate cleanup.

Honor the accepted `nuint` index contract with unsigned native-width bounds
checks on this x64 profile; do not substitute signed `int32` or claim general
`nuint` arithmetic or non-x64 support. Keep escaping results, heap fields,
cross-module capture persistence, writable views, general slicing, and
arbitrary view construction rejected. This provisional choice does not approve
general stored-view support or claim that the bridge is currently implemented.

## Verified implementation boundary

The candidate record layout is compatible with the proposed readonly view:
[`Token`](../../self-host/Token.terrum) contains one `int32` kind and a
[`SourceRange`](../../self-host/SourceRange.terrum) of two `int32` values, so
each record is 12 bytes, matching the three-word prefix copied by
[`TokenSnapshot.TryBuild`](../../self-host/TokenSnapshot.terrum). The snapshot
validates each scanner record before appending it and publishes the output
only after the complete prefix has been copied. On the current Windows x64
profile, the owned buffer's eight-byte header follows 16-byte-aligned
`HeapAlloc` storage, so the record base and each four-byte field remain
naturally aligned; the bounded buffer implementation documents that guarantee
in [`native.md`](../native.md).

The unresolved part is dependency preservation, not record layout. The
`OwnedInt32Buffer` is an eight-byte unique handle and does not expose its data
pointer. The existing `ReadonlyInt32BufferView` uses a 16-byte local
descriptor whose owner root remains in structured semantic IR; it cannot be
stored or returned and does not extend owner lifetime. The current
implementation therefore has no checked representation for forwarding the
snapshot owner's lifetime through a stored `Span<Token>` field and
`[Captures]` relationship. A raw pointer/length bridge would lose that
obligation and is not safe to implement as a mechanical adapter.

**Implementation gate:** The bridge and cursor remain unsupported until the
bounded stored-dependency proof and regression gates pass. The selected
provisional path above is to implement that proof together with a readonly
bridge limited to the validated `TokenSnapshot` prefix. Do not add mutable
access, arbitrary span construction, general slicing, parser integration, or
a new public ABI.

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

### 5. Cursor layout

**Summary:** The existing candidate stores a two-word span descriptor and a
separate `nuint` position. That needs 24 bytes on the current 64-bit target,
exceeding `ValueLayout.MaximumSize` of 16 bytes. Increasing the general inline
layout limit for this candidate would broaden layout and ABI support beyond
this slice.

**Selected:** Store the remaining span descriptor in a mutable field, read
index zero, then use the checked cursor-only `AdvanceOne` operation to rebind
it to the remaining range. This keeps the unique cursor at the span
descriptor's fixed size, preserves readonly element access and capture
provenance, and does not add general slicing or enlarge inline values.

## Scope of the selected initial slice

The first implementation target is the existing readonly `TokenCursor`:
checked length/index reads, `Copy` token results, and dependency forwarding
through its constructor and cursor value. It does not add arbitrary stored
views, general slicing, generic collection iteration, writable spans, new
cleanup rules or a parser integration contract. The bridge does not repurpose
the separate [`HostBuffer`](../native.md) interchange surfaces or
`ManagedTokenChain`; both remain distinct contracts and neither is a Span
substitute.
