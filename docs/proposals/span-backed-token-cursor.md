# Span-backed token cursor proposal

**Status: the bounded compilation-local `TokenCursor` slice is implemented and
verified. General `Span<T>` support and source-level native-sized integers
remain unapproved.**
This proposal identifies the smallest runtime and source-binding contracts
needed to execute the existing [`TokenCursor`](../../self-host/TokenCursor.terrum)
candidate without copying its `Span<Token>` backing or claiming ownership of
it. The implementation slice is limited to this readonly Copy-indexed cursor;
it does not approve general stored views or imply compiler support outside
the bounded slice below.

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
than storing runtime roots or leases. The bounded `TokenCursor` path below
supplies indexing, stored backing dependencies, capture checking and
constructor initialization. Other `Span<T>` uses remain unsupported.

## Accepted initial slice

For the first `TokenCursor` implementation, use a fixed-size runtime descriptor
containing the data address and element count. Keep backing and guard
provenance as compiler-checked dependency facts; these facts are forwarded by
capture, but are not runtime roots, owners or leases and do not extend their
lifetimes. The first slice accepts readonly `Span<T>` access only when `T` is
`Copy`, checks unsigned native-width indexes before reading, and returns
elements by value. On the current x64 target, the compiler uses its existing
`uint64` value as the index carrier; this does not add source-level `nuint`,
an alias, or general native-sized arithmetic.
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

## Input-view bridge and execution

The cursor reads a `Span<Token>` projected from a completed `TokenSnapshot`.
This does not define general parser integration or convert `HostBuffer` or
`ManagedTokenChain` into spans; those remain separate contracts.

**Summary:** An end-to-end cursor needs both a specified source for its input
view and a proof that the view's complete dependencies remain valid through
cursor use. A raw pointer/length bridge would establish neither the initialized
snapshot prefix nor the snapshot owner's cleanup lifetime.

**Implemented bounded slice:** The compiler-owned readonly `Span<T>`
representation, stored-dependency proof, and narrow
`TokenSnapshot`-to-`Span<Token>` projection are one compilation-local unit.
`T` is limited to `Token` on the current Windows x64 native profile. A shared
validated snapshot factory checks the complete initialized three-word prefix,
scanner kind and source-range records before publishing; the native projection
then checks buffer count/capacity, alignment, extent, and source bounds with
overflow-safe arithmetic. The resolved `Token` layout is verified (field
offsets 0/4/8, 12-byte stride, four-byte alignment) before by-value element
loads. The snapshot owner remains an opaque compile-time dependency through
the view, captured cursor field, cursor moves, control-flow joins and
exceptional unwinding; moving or ending the owner while any dependent
view/cursor is live is rejected. Empty and exhausted views retain the same
dependency. Failed construction publishes no descriptor and does not transfer
or duplicate cleanup.

The bounded slice uses full-width unsigned native-width bounds checks on this
x64 profile; it does not substitute signed `int32` or add source-level `nuint`
or general native-sized arithmetic. Escaping results, heap fields,
cross-module capture persistence, writable views, general slicing, and
arbitrary view construction remain rejected.

## Verified implementation boundary

The candidate record layout is compatible with the proposed readonly view:
[`Token`](../../self-host/Token.terrum) contains one `int32` kind and a
[`SourceRange`](../../self-host/SourceRange.terrum) of two `int32` values, so
each record is 12 bytes, matching the three-word prefix copied by
[`TokenSnapshot.TryBuild`](../../self-host/TokenSnapshot.terrum). The snapshot
builders publish only after the shared validating factory accepts the complete
prefix. The native projection independently verifies the owned-buffer header,
capacity, alignment, address extent and source-range/EOF boundaries before
publishing its descriptor. On the current Windows x64
profile, the owned buffer's eight-byte header follows 16-byte-aligned
`HeapAlloc` storage, so the record base and each four-byte field remain
naturally aligned; the bounded buffer implementation documents that guarantee
in [`native.md`](../native.md).

`OwnedInt32Buffer` remains an eight-byte unique handle and does not expose its
data pointer. The compiler-owned Span descriptor is 16 bytes on x64 and holds
the data address and unsigned count. `uint64` supplies the bounded slice's
unsigned native-width index semantics; the source-level native-sized integer
family remains open and unsupported. `[Captures(nameof(tokens))]` forwards
backing dependencies as compiler-checked facts, not runtime references,
owners or leases.

**Implemented boundary:** Only readonly `Span<Token>` projection from a
validated `TokenSnapshot`, by-value indexing with full-width unsigned bounds
checks, and the stored dependency in `TokenCursor` are supported. Do not add
mutable access, arbitrary span construction, general slicing, parser
integration, source-level `nuint`, or a new public ABI.

The complete-program scalar frontend uses this bounded cursor only while
copying validated snapshot records into its existing managed token chain.
Parsing and lookahead continue to use the managed token APIs. This narrow
transfer does not establish a general parser integration contract.

## Proposed next unit: indexed scalar-parser integration

**Status: unapproved proposal; no parser support is implemented by this
section.** The recommendation is to replace the scalar frontend's repeated
managed-chain reads with indexed reads from the already validated
`TokenSnapshot`, while keeping parser position and parse state as ordinary
scalar/managed state. This is an internal representation change only: preserve
the accepted source subset, diagnostics, resource limits, v85 output and
hosted verifier/emitter boundary. Do not wait for general `Span<T>` support,
but do not treat sequential `TokenCursor.TryRead` plus fixed lookahead as
sufficient.

The current parser requires non-consuming indexed access, repeatable reads,
variable-distance forward probes within its 64-token input, backward reads,
speculative position restoration and complete parser retries. A bounded
implementation therefore needs a readonly view of the original validated
snapshot and an external integer position; it must not use an advanced
remaining-view cursor as the parser's sole state. The new bounded
`TokenCursor.TryGet` operation supplies repeatable by-value access relative to
the cursor's current remaining view: it checks a zero-based index against the
view length before converting to the existing unsigned native-width index
carrier and publishes no token on failure. That operation is implemented and
tested, but the parser does not yet use it.
Passing the view through parsing calls is preferred to storing it in
`ScalarFrontendState`, which is a managed class and must not become a general
stored-view exception. Keep all view use inside the scope that owns the
snapshot; borrowed views do not retain that owner.

For complete-program parsing, each function frame must still observe a
function-local terminal EOF at the last included token's `Range.End`, not the
next declaration or the whole-program EOF. Preserve the global scanner-token
index used to address integer snapshot values separately from function-local
parser indices. Keep inventory order, legacy/tree/structured retry order,
fallback publication, and the existing 64-token bound unchanged. The snapshot
owner must remain live through every inventory and parse attempt, including
the single-function fallback; fallback must not require a returned borrowed
view or trigger a scanner rescan.

Before replacing `ManagedTokenChain` parsing, prove the limited view-parameter
call shape with focused lifetime tests. Regression acceptance should cover
grouped/speculative expressions, rewind and previous-token spans, label and
literal searches, per-function synthetic EOF, later-frame integer operands,
all parser retries and diagnostics, exact 64/65-token boundaries, owner
invalidation rejection, and differential equality of complete frontend
buffers. Continue to audit and execute through the existing hosted pipeline;
this proposal does not expand general Span construction, storage, slicing,
parser APIs, persistence or ABI.

## Decisions and rationale

### 1. Stored-view representation

**Summary:** A stored `Span<T>` needs enough information to address its range
and carry its backing, access and guard dependencies. A pointer and length
alone do not prove that the backing remains valid, while an owner generic
argument or per-slice allocation would change the accepted family contract.

**Implemented for this slice:** Use a fixed-size descriptor with compiler-checked opaque
dependency provenance. Forward the existing backing and guard obligations
without retaining or extending their lifetimes. Do not introduce per-slice
allocations, a global pointer-to-owner registry or an owner type parameter.

### 2. Capture relationship

**Summary:** `TokenCursor` stores the constructor's `Span<Token>` argument in
its `_tokens` field. The capture must preserve the view's validity and access
without turning the cursor into an owner or making a temporary backing live
longer.

**Implemented for this slice:** Define `[Captures(nameof(tokens))]` as a checked forwarding
relationship from the named input's complete backing dependency set to the
stored field. It may not acquire ownership, prolong an allocation lease,
weaken access, or omit a guard dependency. Reject construction or escape when
the compiler cannot prove that every dependency outlives the cursor's uses.

### 3. Initial indexer access

**Summary:** The candidate reads `Token` values from a readonly
`Span<Token>`. The general Span family also distinguishes mutable descriptor
rebinding, writable slots, and writable contained handles.

**Implemented for this slice:** Use checked unsigned native-width indexing on
readonly spans whose elements are `Copy`, returning the element by value. The
x64 implementation uses the existing `uint64` carrier and does not add
source-level `nuint`. Match the indexer
receiver's access to the view capability: readonly spans cannot write slots,
`mut Span<T>` only rebinds the descriptor, and `write Span<T>` is required to
mutate storage. Keep ref-returning element loans and writable-view retention
outside the cursor slice.

### 4. Constructor publication

**Summary:** The constructor must initialize the span field and validate its
capture before the cursor can be used. A partially initialized unique value
must not expose a descriptor whose dependencies were not checked.

**Implemented for this slice:** Apply ordinary definite-initialization checks to every field,
then validate and attach the declared capture dependencies before the
constructed value becomes observable. Failure leaves no published cursor;
the operation does not transfer or duplicate cleanup responsibility for the
backing.

### 5. Cursor layout

**Summary:** An early candidate stored a two-word span descriptor and a
separate `nuint` position. That needs 24 bytes on the current 64-bit target,
exceeding `ValueLayout.MaximumSize` of 16 bytes. Increasing the general inline
layout limit for this candidate would broaden layout and ABI support beyond
this slice.

**Implemented for this slice:** Store the remaining span descriptor in a mutable field, read
index zero, then use the checked cursor-only `AdvanceOne` operation to rebind
it to the remaining range. This keeps the unique cursor at the span
descriptor's fixed size, preserves readonly element access and capture
provenance, and does not add general slicing or enlarge inline values.

## Scope of the implemented slice

The implemented target is the existing readonly `TokenCursor`:
checked length/index reads, `Copy` token results, and dependency forwarding
through its constructor and cursor value. It does not add arbitrary stored
views, general slicing, generic collection iteration, writable spans, new
cleanup rules or a parser integration contract. The bridge does not repurpose
the separate [`HostBuffer`](../native.md) interchange surfaces or
`ManagedTokenChain`; both remain distinct contracts and neither is a Span
substitute.
