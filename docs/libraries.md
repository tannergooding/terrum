# Standard-library design

This is the proposed Terrum library contract, not the installed .NET library or an implemented Terrum runtime. The `.api.terrum` inventories in [`library`](../library) and the [numerics/tensors example](../samples/design/numerics-and-tensors.terrum) are **DESIGN ONLY**. The current native integer/bool profile does not bind these library declarations or execute these examples ([implementation staging](implementation.md)). [Language](language.md) defines the broader value/effect rules; [lifetimes](lifetimes.md) defines the access and dependency model. In signatures, `mut` grants place rebinding or a writable receiver, while `write` grants reference/view access; the two are not interchangeable. Generic parameters preserve their full caller qualification, and mutable storage is invariant.

## Dependency layers and package boundaries

| Cumulative layer | Representative surface | Required substrate |
|---|---|---|
| **Core** | Numeric primitives and protocols, `void`/options, `Span<T>`, immutable code-unit/text views, static eager algorithms, shape/view and SIMD contracts | Checked value execution, bounds/access and cleanup/failure services; no ordinary-path allocation or OS facility |
| **Foundation** | Explicitly owned buffers/vectors/maps, allocators/arenas/pools, owning native `Text`, persistent tensor-shape metadata and storage leases | Core and a caller/host-supplied allocator; no mandatory tracing collector |
| **Application** | Managed list/linked list/dictionary/set/queue, ordinary managed `Text`, tasks, streams/terminal, files/network, globalization and serialization | Foundation plus tracing and appropriate host services |
| **Platform leaves** | COM/Windows/DirectX and other native bindings | Only layers their adapters actually use; no reverse dependency on platform APIs |

These are three coherent cumulative layers, **not** a feature-profile matrix for each allocator, SIMD width, encoding or collection. Namespaces describe functionality; referencing `Terrum.Collections` does not link every package or grant effects. An Application program can use Core's nonallocating kernels. Core does not promise that *all* error paths or callbacks allocate nothing; freestanding no-allocation execution additionally needs a compatible failure runtime. The initial native compiler does not yet implement these library layers. Optional hardware acceleration may use compatible software paths; a hardware-scalable register type with a target ABI is not secretly software-emulated as another type. Modules publish normalized public contracts, effects, lifetimes, generic witnesses and ABI obligations independently of linkage choice. Pinning an implementation does not expand `internal`/`private` access. Contract-bound replacement and AOT require actual metadata/binder support, not merely equal method names.

The validated semantic-v24 source-class/`ManagedNode` access slice and
semantic-v25/v26 named and nested mutable non-owning struct-receiver contracts do not implement
these general library layers: `List<T>`, `LinkedList<T>`, generic views/builders
and the API inventories below remain design contracts.

The API inventories deliberately mix signatures with selected algorithm bodies. Unspecified bodies do not imply native exports or implementations. `[Copy]`, `[Sized]`, `[ManagedStorable]`, `[SimdElement]` and `[TextCodeUnit]` denote checked capability predicates rather than arbitrary user attributes. The [Foundation ownership inventory](../library/foundation.api.terrum) distinguishes a per-allocation **storage-and-release lease** from an owned **allocator context** retained by a growing vector. `Buffer<T>.Adopt` takes an existing allocation, validating byte extent, actual element alignment and required tracing capabilities; it does not create storage from a release token. Initial `Vector<T>` accepts `[Copy]` elements: `Reserve` obtains a new lease from its retained context, copies the initialized prefix, cleans partial copies on failure and publishes replacement storage only after success. The old allocation cannot be replaced while views borrow it. Affine/borrowed element storage, initialized prefixes, allocator-provider selection, write-guard enforcement and native GC barriers still need complete contracts. Additional proposed signatures and associated-family notation require language design and validation before they can become compiler acceptance fixtures.

## Shared memory and explicit memory primitives

The selected operation surface is one flat static type, `MemoryPrimitives`.
Prefixes identify the family at the use site: `AtomicRead`/`AtomicAdd`,
`NonTemporalWrite`, and `CacheWriteBack`, alongside `Fence`/`CompilerFence`.
Owners, views and allocators retain their separate storage/lifetime duties.

Storage capabilities do not silently change ordinary accesses. Explicit
atomic reads default to acquire ordering, writes to release, and RMWs to SC
plus full ordering. One compile-time-known `MemoryOrder` selects other valid
orders; checked/unchecked selection is a separate declared arithmetic policy.
The [memory-model library surface](memory-model.md#library-access-surface)
specifies the operation families, result conventions and remaining details.
This is accepted design, not an implemented library.

## Scalar mathematics, keys and conversion

The executable [static-protocol sample](../samples/native/static-protocol-quadratic.terrum)
now exercises a bounded part of this direction: source-defined addition and
multiplication protocols, explicit nominal Copy conformance and one constrained
algorithm specialized for two wrappers. The [integer-power sample](../samples/native/static-protocol-power.terrum)
extends this to initialized generic locals, scalar loop control and a constrained
square helper, with an explicit identity argument rather than implicit
construction of a generic literal or default value.
The [binary-protocol sample](../samples/native/static-protocol-binary.terrum)
adds homogeneous subtraction, division, remainder and bitwise `&`, `|`, `^`
witnesses. Its arithmetic and bit-selection helpers are separately constrained;
the interfaces are source declarations, not an implicitly supplied numeric
hierarchy. Comparisons, shifts, unary and heterogeneous operators remain outside
this executable protocol subset.
Helpers require only their declared constraints: the
[Copy selection candidate](../self-host/CopyAlgorithms.terrum) needs no operator
witness and composes over nested token/range values. Calls cannot acquire
undeclared protocols from a concrete specialization or a callee's body.
Scalar-returning and conditional-output helpers retain the ordinary signature
and definite-initialization rules; invalid selections publish no value rather
than a fabricated generic default.
Templates and witnesses remain
compilation-local; the [implementation limits](implementation.md) do not extend
to these complete inventories, primitive numeric conformance, mixed-result
operators, families or generic collection algorithms.

The granular `IAdditionOperators<TSelf, TOther, TResult>` and `IMultiplyOperators<...>` protocols in [numerics](../library/numerics.api.terrum) express an operand and a *possibly different* result. `IAdditiveIdentity<TSelf, TResult>` supplies an operation's zero. `IScalarNumber<TSelf>` composes selected closed scalar operations; IEEE classification/functions live on `IScalarIeee754<TSelf>`. A declared fixed-vector family checks its common operations and associated element/mask relationships; it can supply the same granular operator witnesses without requiring users to name an `ISimdVector` interface merely to abstract over widths. Granular interfaces remain useful across unrelated vector, complex and matrix types. A SIMD vector does **not** become a scalar number by providing `+` and `*`. The inventory is representative, not an implemented numeric surface.

This granular design preserves the useful generic-math model; it does not
reject generic math or require every numeric type to implement one universal
number interface. A composite such as `IScalarNumber<TSelf>` is an optional
convenience contract, not a prerequisite for using the smaller operation
protocols.

Integer arithmetic is checked regardless of build; wrapping/saturating/try forms are explicit. No promotion between already typed numbers is implicit. Numeric literals are constructed directly in an unambiguous target type, not converted from an implicit default. The single explicit conversion spelling is `T(expression)`; built-in or declared checked conversion contracts select its behavior, while reusable checked/saturating/truncating protocols replace permanent per-source-type `FromInt32` method families. [Conversion policies](conversions.md) specify fractional rounding separately from range and modulo narrowing: checked can be inexact, and bit reinterpretation remains `FromBits`/`ToBits`. `Splat`/broadcast replicates the *same* lane value or tensor elements, without itself converting them. A widening vector conversion must define output shape and lane count explicitly -- it cannot silently discard half the elements. Fixed-width `float32` math has strict target-format rounding and no implicit reassociation/contraction; `FusedMultiplyAdd`, native/approximate functions and reproducible reductions are separate contracts. IEEE `decimal32` is not CLR `decimal` or a binary64 intermediary. The CLR's decimal BID software implementation is a useful reference, not proof of matching Terrum ABI, parsing, rounding and exceptional behavior.

The target `IParsable<TSelf>.TryParse` contract initializes `init ref<TSelf>` only on success; the destination is vacant and unreadable until then. The bounded executable profile still uses legacy `bool`/`out`, so its public declaration and call sites need coordinated migration. `TryParse` is the fundamental expected-input API: callers branch on its published success contract, or deliberately use `int32 value = require int32.TryParse(text);` when malformed input should raise. A requirement-failure type may be selected explicitly; that selection applies only to a false result, not exceptions from evaluating the predicate. Core parsing/formatting is ordinal plus invariant: ASCII signs/digits/punctuation, whole-input parse, no locale, normalization or Unicode lookalike substitution. Formatting writes through the supplied typed writer, never a required intermediate `Text`. The default typed hook forwards to the value formatter; a custom handler can intercept values and forward nested typed parts. Refusal stops further writes without replay and can leave already accepted fragments. The supplied [numeric sketch](../library/numerics.api.terrum) retains the typed writer forwarding point. Collection/text expressions select explicitly registered handler types (for example, `[CollectionBuilder(typeof(ListBuilder<>))]` and `[TextBuilder(typeof(TextBuilder))]`), rather than arbitrary same-named methods. Streaming captures evaluate once, left-to-right before draining; ownership, lifetime, refusal, cleanup and publication remain checked. Expression handlers consume declared typed operation graphs, not callback bodies divined for vectorization; detailed standard-library handler APIs remain scheduled.

These operations are selected capabilities, not universal object defaults:
Terrum does not require every value to inherit `Equals`, `GetHashCode`, or
`ToString`, or impose object-default boxing/allocation on every value or
automatic `IEquatable<T>`/`IFormattable` conformance. Explicit formatting
protocols remain valid, and no contract requires every value to become a
string. Primitive built-ins and compiler-verified operation capabilities remain
valid; whether a root type exists is a separate open decision. Keep value
equality, identity, key equality paired with a compatible hash,
machine-readable formatting, and display/debug rendering distinct.
Compiler/debugger metadata does not require user-visible root methods.

Floating `==` follows IEEE (NaN unequal, signed zero equal). A **separate numeric-key comparer** makes all NaNs mutually key-equal, zeros equal and hashes consistent. Decimal key hashing must normalize numerically equal *cohorts* (`1.0`/`1.00`), not hash its raw encoding; signed zero and NaN also need canonical key hashes. A bitwise-identity comparer is different again. Maps/sets accept a paired equality/hash policy (`IKeyComparer<T>` in the [algorithm inventory](../library/algorithms.api.terrum)); `IndexOf` under ordinary operator equality must not silently adopt numeric-key semantics. Ordering and `Min` variants explicitly select NaN propagation and signed-zero policies. The runtime's distinct decimal `operator ==`, `Equals`, and cohort-normalizing hash illustrate why one implicit universal equality is insufficient ([source notes](references.md)).

## SIMD as a family of shapes

Explicit `simd64/128/256/512<T>` are fixed ABI shapes where supported; `simd<T>` denotes a **genuine target-scalable** type, not a preferred-width fixed vector. The [SIMD contract inventory](../library/simd.api.terrum) uses locally labeled family pseudocode: "any fixed vector of float32" preserves the actual concrete type/width and its associated mask for checking/codegen, unlike runtime-erased text. Two operands and their result must retain required same-instantiation relationships. Membership and common operations are checked declarations, not matching names. Vector addition returns that same vector type; lane comparisons return its mask; reductions may return the element type; whole-vector equality returns `bool`. A mask represents **boolean lane predicates**, not a numeric data vector of all-one bit patterns. `CompareEqual` produces a mask; `Any`/`All` reduce it to `bool`; `Select` consumes it. Mask shape and granularity must match element shape. Bit reinterpretation, numeric conversion, splatting, and rebinding families are distinct operations. Final family/associated-member grammar and custom participation policy remain open; see [type families](type-families.md).

Fixed SIMD values can be `[Sized]` if layout supports their element type. The proposed initial scalable profile is SVE-like **sizeless**: locals, scoped loans and statically specialized compatible calls, but not ordinary fields, heap boxes, array/pool elements or aggregate payloads. [Arm ACLE](references.md#other-official-library-specific-prior-art) specifies related SVE vector/predicate restrictions, but does not supply Terrum's ABI or lifetime rules. Such live values cannot cross `await`, incompatible vector contexts or an ABI boundary that cannot carry them. RVV requires its own validated profile; mask representation, vector length and ABI cannot be copied blindly from SVE. Specializations explicitly name an exact closed generic declaration and target requirement, for example `[Specialize(Scale<float32>, Requires = CpuFeature.Avx2, Priority = 100)]`; source binding remains on the generic declaration and AOT dispatch checks CPU and OS support. AOT publishes a bounded explicit variant plan rather than inventing runtime code generation. Reusable static operators/witnesses allow code sharing without erasing masks, boxing sizeless values or conflating vectors with scalar `INumber`.

Tail handling is part of correctness: `First(remaining)` masks the final iteration, inactive masked **loads/stores do not access memory**, and no safe lane is poison/undefined. Stores with repeated scatter indexes preflight active bounds before writes, then define duplicate-index order (last active lane wins). Aliasing/alignment rules are checked at the public boundary. A fixed vector may use full-vector processing and a scalar tail instead; neither strategy may read beyond the provided span. Dot/sum ordering across vector widths needs an explicitly chosen reproducibility policy: per-vector lane order alone is insufficient to make a whole-input reduction bitwise stable. Strict scalar order is a defensible default; a separately named relaxed/high-throughput reduction may regroup and document architecture-dependent results.

## Callable kernels and collection parameters

The target `func<Args..., R>` is a lifetime-aware single-cast callable;
`fnptr<Args..., R>` is a raw code pointer with a closed target-defined
signature and no retained context. Named nominal functors remain ordinary
checked protocols, as illustrated by `IMapper` and `IPredicate` in the
[algorithm inventory](../library/algorithms.api.terrum). Compiler-supplied
callable witnesses require the complete signature, effects, access, lifetime,
and ABI to satisfy the protocol; they do not box, allocate a structural
adapter, or turn a closed callback into a generic one.

Generic scalar/SIMD kernels use a named generic functor with definition-site
symbolic constraints and specialization for the registered family members.
A scalar `func<float32, float32>` or scalar callback does not promise a vector
implementation. A contextually generic lambda is later shorthand for the same
checked generic-functor contract. Elementwise equivalence still needs an
operation contract for effects, invocation counts, cross-lane behavior,
inactive lanes, tails and floating-point order/contraction. Captured scalar
values require an explicit supported broadcast rather than an implicit
vector conversion. Neither a readonly functor receiver nor scalar generic
admissibility guarantees purity or automatic vectorization.

The agreed `params Collection<T>` and `params Span<T>` direction uses ordinary
construction and per-element copy/move into new slots, not aliases to caller
locals or a special by-reference parameter mode. Explicit reference types may
be elements when normal storage and lifetime contracts permit them. Whether
an existing collection binds directly or expands into elements remains a
binding/overload rule to settle. These callable and parameter contracts are
design targets, not executable APIs in the current native compiler.

## Views, shapes and tensor primitives

SIMD values are vector/ABI representations; `TensorSpan<T>` is a higher-level
logical multidimensional view over backing storage, analogous to `Span<T>` for
one-dimensional ranges. Shape, strides and checked projection belong to the
tensor contract, not to a vector's lane ABI; a tensor is not flattened or
converted to SIMD merely because the element count matches.

One `Span<T>` family carries qualified read/write authority and opaque backing
dependencies, **not** an owner generic parameter. Bare `Span<C>` has a fixed
descriptor, readonly slots, and readonly handles; `mut Span<C>` permits
descriptor rebinding only; `write Span<C>` permits writing slots but not through
its readonly `C` handles; `Span<write C>` keeps slots readonly while carrying
writable `C` handles. In generic `Span<T>`, the complete qualification of `T`
is preserved independently of the outer view qualification. A `write` element
qualifier never bypasses the storage permission required to mutate an inline
struct field. `Span<T>` implements `IContiguousSequence<T>.AsSpan()`
*explicitly* so a constrained generic caller can obtain a read projection
without boxing, while `span.AsSpan()` is intentionally absent on concrete
spans. Its implementation **forwards existing backing dependencies**, without
borrowing the descriptor place simply because it read the descriptor. The broad
interface receiver-view contract permits implementations to borrow receiver
storage. A generic caller sees that broad contract; propagation of an
implementation's narrower backing-only relationship through a wrapper requires
a future **checked conditional member refinement**, published in the witness
and validated/versioned. It is not inferred from a current body or resolved
with a second `IScopedSequence` interface. Neither the broad nor narrowed rule
extends owner/allocation lifetime or bypasses disposal.

`TextSpan<TCodeUnit = void>` is one family; bare `TextSpan` is exactly its declared encoding-erased `TextSpan<void>` specialization. `void` does not satisfy `[TextCodeUnit]`; only the `char8`/`char16`/`char32` members expose public `AsSpan()` and `IContiguousSequence<TCodeUnit>`. The common surface includes `IsEmpty`, ordinal operations and formatting. Erasure preserves actual encoding, immutable validity and every backing/lease dependency without text copy/transcoding. Checked projection returns a validated typed view only when representation matches; its `AsSpan()` can then forget validity, not immutability or lifetime. There is no `Span<void>` projection. Conditional members/conformance require family contracts, not extension lookup alone; [application sketches](../library/application.api.terrum) label that grammar as provisional.

[`TensorShape` and `TensorSpan<T>`](../library/tensors.api.terrum) express a *logical* rank-dynamic view, not a second owner family. Rank and dimension lengths are `nuint`; element strides are signed `nint` to allow reversed/transposed views. Rank-zero `[]` is a scalar with one element, while `[0, n]` is empty with zero elements; a default/empty sentinel must have an explicit shape rather than ambiguously representing both. Validate **every** dimension length as representable within the address/offset range, independently of order. Then `ElementCount` is zero if **any** dimension is zero; do not multiply the remaining nonzero dimensions of an empty shape. Otherwise calculate their checked product (rank zero has the multiplicative identity, one). For nonempty views, `origin + Σ(index[i] × stride[i])` uses checked signed arithmetic; derive minimum and maximum reachable offset using `(length[i]-1) × stride[i]` with checked operations and validate *both* against backing bounds. For zero-element views **skip all `(length-1)` address arithmetic and dereference**; their origin may be an in-bounds end location, and metadata/owner dependencies remain valid. `nuint` lengths cannot be casually cast to signed offsets; reject nonempty shapes outside representable address/element ranges. Width, alignment and shape-data lifetime are not inferred from a pointer alone.

Small shape metadata can be inline; arbitrary rank may use independent, immutable Foundation-owned metadata or explicitly borrowed metadata. A tensor view then has **two** dependency sources: data backing/access *and* externally stored shape metadata. Returning/storing/erasing it preserves both, even at an empty/end slice. The exact multi-source declaration/anchor ABI is unresolved; the sketch intentionally does not claim that the current `[ViewOf]` spelling proves the view factory. The factory requires `write Span<T>` for writable storage and returns `write TensorSpan<T>`; `mut Span<T>` would only permit descriptor rebinding. A read-only source returns a read-only view, never an upgraded writable one. Writable tensors require injective logical-index-to-address mapping (zero-stride broadcasting with length greater than one is read-only); overlapping or duplicate addresses cannot manufacture multiple writable loans. Broadcasting is a read view or an explicit materialization, not an implicit arithmetic conversion. `TryAsDenseSpan` succeeds only when the logical order actually maps to one contiguous range with proper access/lifetimes. An arbitrary transposition must not pretend to be contiguous. `CopyTo` checks equal logical shapes and `[Copy]` elements, traverses logical indexes, accepts disjoint backings or **identical per-index mappings** (no-op), and rejects displaced or differently strided overlap before writing; it does not allocate a hidden temporary.

Core `TensorPrimitives` operate over borrowed spans and caller destinations, not hidden owning tensors. `Add` requires compatible lengths/shapes and preflights destination capacity and alias safety before mutation. `Add`, `Sum`, `Dot` and copying tensor views explicitly require `[Copy]` complete elements when invoking value-taking operators; arithmetic witnesses alone confer **no** copy permission. A future operator consuming `ref<T>` would need a separate contract, not implicit duplication. Exact same-position in-place operands are permitted only where each output reads all required inputs before overwriting; displaced overlap is rejected unless an operation explicitly supplies memmove/temporary semantics. Numeric callbacks can still throw mid-operation, so an algorithm without a no-throw bound does not promise transactional output. `Sum(empty)` returns additive identity, `Dot(empty,empty)` returns zero; unequal dot lengths fail. A strict reduction observes logical row-major order and specified floating behavior; accelerated relaxed variants can specify different rounding/order. Fused operations avoid an intermediate allocation, but must not silently change IEEE rounding to FMA. Transcendentals, normalization and softmax require separate domain, overflow, NaN and stability policies; the runtime `SoftMax` span implementation uses exp-then-sum-then-divide, illustrating a cost/overflow tradeoff, not a Terrum semantic mandate.

## Collections, algorithms and application services

The executed [managed token-chain candidate](../self-host/ManagedTokenChain.terrum)
is a bounded consumer of source-defined classes and optional edges: it stores
complete 12-byte Copy tokens with validated source ranges, preflights append
mutations, tracks count/version and leaves collection explicit. It exposes no
node handles or borrowed source storage. Copied results survive collection of
the original links. This is not the generic `List<T>`/`LinkedList<T>` API below
and does not implement their guards, builders, iterator invalidation or public
module contracts.

Generic collections are the primary collection APIs, not one half of parallel
generic and object-based hierarchies. Heterogeneous values use explicit
carriers, unions, or interface representations; for example,
`List<box<I>>` stores one checked managed-handle form while allowing distinct
implementations. The `I` payload itself is not an implicit stored handle.

Foundation owns explicit `Buffer<T>`, `Vector<T>`, arena-backed storage, maps and persistent shape metadata with allocator/lease obligations; Application provides managed aliasable collections. The mutable `List<T>` and `LinkedList<T>` declarations require writable receiver access for structural mutation. A read-only collection path may still return a `write C` element handle if that is part of the element type, but cannot write its slots, nodes or links. The [managed collection inventory](../library/collections.api.terrum) specifies initial `List<T>` element restrictions (`[ManagedStorable]`, `[Copy]`, `[Sized]`); this is **not** a permanent language ban on storing pointers/loans or affine elements. GC visits only initialized slots and uses write barriers; no fabricated `default(T)` fills unused capacity. Preflight capacity, checked count/version and guard conflicts precede mutation; remove/clear vacate slots and release retained references. `ListWriteAccess<T>` lends checked exclusive slot access, keeps actual storage stable and live and releases once. Every alias, indexer, cursor and reentrant callback checks the relevant guard. It is not a cross-thread mutex. `TryGet` distinguishes missing index from a present optional/null element.
For `LinkedList<T>`, `First`/`Last` and node-link projections use checked
`[MatchedAccess]`: readonly traversal returns readonly node handles, while a
writable list path may preserve write access. This is distinct from an element
whose own `T` qualification is `write C`; a readonly list cannot expose a
writable structural node whose `Value` or links would mutate list-owned state.

`GetWriteAccess`/`TryGetWriteAccess` are provisional names. Renaming is not needed to settle the invariant: acquisition checks all conflicting aliases, a view cannot outlive its exclusive guard, and cleanup releases authority exactly once.

`LinkedListNode<T>` has stable **logical node identity**, not a promised machine address. Detached nodes remain usable and may be reinserted; attached nodes retain exactly one owner. Removal clears owner/links. `Clear` detaches *all* nodes, including externally retained nodes, and cannot merely drop the head/tail. Node values are accessors, never public writable fields that bypass guards. Membership/count/version are checked before nonthrowing link changes. A concrete `foreach` cursor need not box; structural mutation invalidates it, while payload replacement alone is not structural. Copy-, borrow-, consume- and snapshot-yielding iteration are separate operations.

The inventory's `LinkedListCursor<T>.Current` is a by-value Copy result; it is
not a general promise for every sequence. Future lending iteration may use an
associated `Item<'a>` relation, but its selected contract must preserve the
complete item type, source lifetime, access and ownership. No iterator result
is silently changed to a loan because its payload is large.

Both `List<T>` and `LinkedList<T>` participate in the same registered collection-expression protocol. `LinkedList<int32> pending = [10, 20]` constructs fresh nodes from element values; it does not move nodes out of another list or promise contiguous capacity. `LinkedListBuilder<T>.TryAdd` accepts values once in source order, and `TryBuild` publishes the private completed graph. Failure/abandonment detaches and releases all partially built nodes exactly once, preserving correct initialized-element cleanup; false publishes no default list. Existing node-based insertion remains a separate operation.

[`SpanAlgorithms`](../library/algorithms.api.terrum) illustrates constrained
static/eager calls over contiguous views. Element inputs use readonly `ref<T>`;
stateful comparer/mapper/predicate parameters use `write ref<TState>` so their
referents can be mutated. These callbacks have their own checked
effects/captures; no hidden boxing, closure, allocation, deferred iterator or
`IEnumerable` materialization. Output destinations need `write Span<T>`:
`mut Span<T>` would only permit rebinding the descriptor. `Select` validates
output length and **rejects any source/destination overlap, including exact
in-place**, before invoking callbacks. `Where` reserves capacity for the
*upper bound* `source.Length` so a stateful predicate is invoked exactly once
per element and returns the written length. It accepts disjoint storage or
**same-start in-place compaction**, reading the `[Copy]` source element and
completing its predicate before writing at an index no greater than the read
index; displaced overlap is rejected before callbacks. Callback failure in
either algorithm may leave an accepted output prefix. A smaller destination
needs an explicitly incremental/status-returning API; a preflight callback
counting pass would change effects. Other LINQ-like producers
(grouping/joining, sorting, lazy iteration) specify storage or laziness
separately. Raw code-unit algorithms do not silently become Unicode-aware, and
equality/hash policies remain paired.

`IFormattable` forwards typed fragments to `ITextWriter`, including nested Point components; bounded `SpanTextWriter<TCodeUnit>` captures its destination exclusively, counts accepted code units and refuses whole fragments without modifying the destination for that fragment. Immutable `Text`/`TextSpan` cannot become writable via a read cast; creating text validates/freezes storage, potentially transferring exclusively owned backing. `Terminal.Write`/`WriteLine` target **standard output** by default; `output: TerminalOutput.Error` selects error and invalid selectors fail. Streaming writers can have externally visible partial writes. Async writes format once into operation-owned buffering, drain to completion and retain borrowed content only within the declared operation lifetime; no replay, silent stringify fallback or cancellation-as-cleanup. The caller must keep completion responsibility with a structured owner, explicitly transfer it, or use a supervisor with error/lifetime/shutdown policy; discarding the task does not cancel the write. Application file/network/terminal controls and generated serialization registrations are separate from Core; platform bindings require explicit native ABI/ownership/encoding adapters.

Text targets give character fragments `rune` context and forward them through `TryWriteScalar`, without requiring a temporary text object or a per-primitive overload catalog. `require format.IsEmpty` expresses Point's supported-format check; a false predicate raises the selected requirement failure in every build, not a formatter refusal. `require` is safe; unsafe authorization is needed only to catch/recover. Async library design preserves declared affinity and does not assume that cancellation means completion. Whether continuations capture ambient UI context or use another scheduler policy remains open; neither pervasive `ConfigureAwait(false)` nor implicit context capture is selected.

## Open contract work

Associated vector/mask family syntax and valid element predicates; scalable ABI/vector-context rules; exact anchored view and multi-source shape representation; checked conditional member refinement through generic wrappers; `TensorShape` zero-dimensional/default layout and signed-stride support; fast versus reproducible reduction naming; allocator selection; detailed text storage/handler and async/backpressure API contracts; collection storage capabilities beyond `[Copy]`; and language parser/type checker support for these inventories remain staged. Resolve and test these before treating a design sample as executable or claiming interface/module replacement compatibility.
