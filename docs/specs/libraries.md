# Standard-library design

This document records accepted library behavior and dependency constraints,
not an implemented Terrum runtime. The [adopted inventory](libraries/adopted-inventory.md)
indexes selected roles and incomplete surfaces, and
[design scenarios](libraries/design-scenarios.md) expose provider/consumer
contracts and deliberate framework-design departures. The canonical
chapters linked below own semantics; the broader layered inventory remains
the [core-library proposal](../proposals/core-library-reference.md). The
`.api.terrum` files in [`library`](../../library) and
[numerics/tensors example](../../samples/LanguageFeatures/Numerics/numerics-and-tensors.terrum)
are **DESIGN ONLY**. Current compiler support is tracked in
[implementation](../implementation.md) and [library implementation notes](../implementation/libraries.md).
[Language](language.md) defines
broader value/effect rules and [lifetimes](lifetimes.md) access and
dependencies. In signatures, `mut` grants place rebinding or a writable
receiver, while `write` grants reference/view access; generic parameters
preserve their full qualification and mutable storage is invariant.

## Definitions

| Term | Meaning |
|---|---|
| Language baseline profile (`language`) | The selected compilation/conformance floor: its closed primitive binding catalog and required substrate, distinct from optional library APIs. Exact profile inventories and feature dependencies must be selected explicitly. |
| Dependency layer (Core/Foundation/Application/Platform leaves) | A candidate cumulative package-boundary grouping, described in the [core-library proposal](../proposals/core-library-reference.md#dependency-layers); each layer may depend only on preceding layers and its declared substrate. |
| Storage-and-release lease | A per-allocation handle that grants access to allocated storage and the obligation to release it; distinct from an owned allocator context. |
| Allocator context | A retained handle to an allocator, used to obtain leases; owned by a container such as a growing vector, not per-allocation. |
| Family, member, witness | Defined in [type families](type-families.md); a family is a declared set of related types (members), and conformance to a behavioral contract is established by an explicit witness. |
| `func` / `fnptr` | `func<Args..., R>` is a lifetime-aware, single-cast callable retaining its capture dependencies; `fnptr<Args..., R>` is a raw target-defined code pointer with no retained environment. |

## Scope

This document is accepted library design direction, not an implemented
library or a claim about which signatures currently compile. A referenced
sample, proposal or self-hosted source file may illustrate direction without
supplying an adopted declaration, registration or profile.
The [status inventory](libraries/adopted-inventory.md#1-status-and-identity)
distinguishes selected protocols, partially specified families, direction
and deferred facilities. Individual accepted decisions do not approve this
document as a whole or every candidate API it references.

## Dependency layers and package boundaries

The accepted lowest **`language`** profile describes what the selected
language requires for those features. It publishes a closed
reserved-primitive binding catalog independently of optional APIs; it does
not grant an ordinary package authority to redefine those bindings.
Above it, **Core** is the scoped generally useful surface, while broader
framework/ecosystem facilities can be supplied by independent packages.
Exact selected primitive/profile inventories remain separate from the
candidate width/API listings.

The candidate cumulative **Core/Foundation/Application/Platform**
[dependency layers](../proposals/core-library-reference.md#dependency-layers)
are library groupings, not replacements for the `language` baseline or
newly assigned target-runtime profiles. The Core
library tier is broader than the genuinely minimal language requirements
and is not a mandatory API baseline; `ref<T>`,
`ptr<T>` and `int32` are illustrative foundational forms, not an exhaustive
type list. Core/Foundation/Application are existing organizational labels, not
newly standardized profile names or a feature-profile matrix for each allocator,
SIMD width, encoding or collection. Core does not imply that the language
requires a heap, tracing collector, scheduler or OS services.

Namespaces describe functionality; referencing `Terrum.Collections` does not
link every package or grant effects. An Application program can use Core's
nonallocating kernels. Core does not promise that *all* error paths or callbacks
allocate nothing; freestanding no-allocation execution additionally needs a
compatible failure runtime. Features that require runtime facilities retain
their dependencies and checked contracts when used; a missing facility does
not silently weaken semantics or substitute an implicit fallback. Not every
environment is required to support every feature. Optional hardware acceleration may use
compatible software paths; a hardware-scalable register type with a target ABI
is not secretly software-emulated as another type. Modules publish normalized
public contracts, effects, lifetimes, generic witnesses and ABI obligations
independently of linkage choice. Pinning an implementation does not expand
`internal`/`private` access. Contract-bound replacement and AOT require actual
metadata/binder support, not merely equal method names.

These general library layers — `List<T>`, `LinkedList<T>`, generic
views/builders and the API inventories below — remain design contracts.
Their names and sketches supply no missing member/registration/representation
catalog. Current support is separate [implementation work](../implementation/libraries.md).

The [pure-evaluation contract](language/published-predicates-and-effects.md)
selects Core's no-argument, nonrepeatable `Pure` and `ConstEval` semantic
attribute identities and the static readonly Boolean
`RuntimeHelpers.IsPureEval`/`IsConstEval` intrinsic properties.
These compiler-recognized identities do not depend on a matching name,
mutable static state, heap, scheduler or runtime initialization group.
The queries report the selected evaluation form, with constant implying
pure evaluation. Their program/value/effect recipes remain required
admission obligations; this logical API selection assigns no native export,
intrinsic wire ID or completed general-purpose Core API inventory.
Core's no-argument, nonrepeatable parameter `NoAlias` schema publishes the
checked invocation separation contract. It is not a write/exclusive-access
capability, type qualifier or ordinary runtime alias-test API.

[General dependency-source schemas](language/dependencies-and-views.md)
select Core DependsOn, Captures and ForwardsBacking with whole receiver
`this` and formal `nameof(parameter)` recipes. Results and initialized outputs
retain permitted source sets; construction, existing/named holders and admitted
operations use their exact capture destinations/extents. Backing-only does not
acquire or retain an owner, imply exclusivity or supply an access protocol.
An exact member-contract constraint can require a checked published witness
refinement. This selects no general Span anchor ABI, closure/task substrate
or otherwise proposed collection/guard/stream API.

[Local failure eligibility](language/local-failures.md) selects Core's
no-argument, nonrepeatable primary Exception capability for nominal storage
types and declared catch-category interfaces. Explicit category witnesses
provide nominal opt-in, and ordinary Exception property constraints check
the complete argument. Named value catches require Copy; reference catches
borrow without consuming the active event. No empty mandatory IException
interface, universal Exception base, message/stack-trace field, implicit box,
default constructor or public capture API follows from this selection.
[Runtime requirement failures](language/requirement-failures.md) separately
select ordinary `new E()` with zero supplied arguments on failure, the resolved
primary-callable `RequireException(typeof(E))` recipe and Core's exact public
`Terrum.RequirementException` fallback. The latter is a nongeneric zero-data
Exception/Copy struct with a public zero-parameter constructor and no authored
construction effects or diagnostic fields. Explicit `require<E>` wins; recipes
and defaults belong to their semantic source owner, not caller/type/module
inheritance. No constructor runs on a successful check, and actual constructor
failure is not replaced with a fabricated E or another default.
[Scalar checked failures](language/scalar-failures.md) additionally select
exact public nongeneric zero-data Exception/Copy structs
Terrum.OverflowException, Terrum.DivideByZeroException and
Terrum.ArgumentOutOfRangeException, each with a public zero-parameter
constructor and no authored construction effects. Their initial mappings
cover unrepresentable checked numeric results, integer zero divisors and
invalid original shift counts. Provenance/causes belong to the event, not
mandatory operand/message/stack-trace fields. Zero-data payloads do not imply
failure-free or allocation-free transport. Other compiler/runtime failure mappings have their own canonical owners.

[Standard runtime categories](runtime/standard-failures.md) additionally
select zero-data Exception/Copy PostconditionException, IndexOutOfRangeException,
OutOfMemoryException, InitializationCycleException and InitializationFailedException
with the same exact public zero-parameter construction shape.
They apply respectively to normally false runtime ensure, already admitted
checked index access, explicitly recoverable allocation exhaustion, cyclic
unfinished initialization demand and accesses to a failed unit.
The initializing flow receives its actual escaping event; waiting/later
accesses create fresh failure events without copying or sharing the original.
No universal recoverable-OOM runtime, user-indexer mapping, retry API or
shared capture facility follows.

[Numeric selection](language/numeric-selection.md#4-reusable-invalidboundsexception)
separately selects exact public nongeneric zero-data Terrum.InvalidBoundsException
with Exception/Copy and a public zero-parameter instance constructor without
authored checks/effects. It describes bounds that do not satisfy a published
relationship, not a value outside valid bounds or a universal exception base.
Clamp explicitly uses require<InvalidBoundsException> for min <= max;
no comparison syntax, name or caller default automatically supplies that type.

[Captured exceptions](language/captured-exceptions.md) separately select
exact public nongeneric opaque unique Terrum.CapturedException and its public
static nongeneric zero-parameter CapturedException Capture() member.
Only a direct invocation in the active source handler body supplies its
compiler-known event source. Publication transfers the existing event,
payload/causes and dependencies; it is not construction from E, a payload
Copy or an ambient current-exception query. Named consuming throw requires
move. The owner has no ordinary constructor/default, Exception/Copy opt-in,
payload extraction API, shared/typed family or implicit thread capability.
Creation effects and complete nonthrowing/nonsuspending retirement support
remain required; no heap, native packet adoption API or physical codec follows.

[Core task responsibility](language/task-responsibility.md) selects the exact
Terrum.Threading.Tasks.Task<T = void> family as an affine completion owner,
its readonly bool IsCompleted status and compiler-owned consuming await.
Named tasks require explicit move. Payload transfer, terminal observation,
output reservations and every-exit settlement remain separately checked.
Unlike a reusable .NET Task observation handle, this value owns completion
and failure-handling responsibility; separate observation does not copy that
owner. No TaskScope, general TaskAwaiter, scheduler/cancellation API or task
object representation follows from this logical selection.

[Owning-field cleanup](language/ownership-and-cleanup.md) selects the resolved
Cleanup semantic role, not a universal Dispose/Close API. Its complete
readonly body precedes automatic reverse-declaration field retirement;
structural partial moves require ordinary write/init authority and derived
cleanup. Library wrappers must retain those transitions, every-exit task
settlement and actual dependency bounds. No general swap/take intrinsic,
resource suppression policy or physical owner carrier follows.

[Thread capability and sharing contracts](runtime/sharing-and-handoff.md)
select resolved no-argument ThreadTransferable/ThreadShareable primary-type
opt-ins with capability-owned ordinary where conditions. Core publishes its
scalar and admitted family facts; user types do not inherit them from layout.
Providers retain actual handoff/publication/guard/scalar protocol identities,
affinity, backing and every-exit responsibility. No thread/lock/guard API,
custom-algorithm proof, physical token or helper ABI is selected by that schema.

[Registered Core value-layout queries](language/registered-queries.md)
select `RuntimeHelpers.SizeOf<T>()` and `AlignmentOf<T>()`: ordinary static
generic APIs requiring the complete form's checked `[Sized]` property and
returning `nuint`. They measure value/handle representation rather than
object payload or reachable backing. Runtime type/layout evidence, required
constant admission and fixed-versus-coupled layout retention remain distinct.
The exact intrinsic contracts are pure and constant-eligible without mutable
static storage, allocation or a reflection object; their scalar result does
not freeze an uncommitted queried layout. No general query attribute or
additional physical intrinsic assignment is selected.

[Constant-generic domains](language/constant-generics.md) derive eligible
one-field integer/Boolean wrapper participation from the checked Transparent
contract and existing construction, validity, frozen Copy/Blittable and typed
value-codec obligations. There is no second Core attribute or carrier-
exposure protocol. Canonical generic identity is the exact complete nominal
domain plus carrier value, independent of user equality/hash witnesses.
Literal parsers, constructors, conversions and runtime operations remain
separate explicitly admitted APIs.

The API inventories deliberately mix signatures with selected algorithm bodies. Unspecified bodies do not imply native exports or implementations. `[Copy]`, `[Sized]`, `[ManagedStorable]`, `[SimdElement]` and `[TextCodeUnit]` denote checked capability predicates rather than arbitrary user attributes. The [Foundation ownership inventory](../../library/foundation.api.terrum) distinguishes a per-allocation **storage-and-release lease** from an owned **allocator context** retained by a growing vector. `Buffer<T>.Adopt` takes an existing allocation, validating byte extent, actual element alignment and required tracing capabilities; it does not create storage from a release token. Initial `Vector<T>` accepts `[Copy]` elements: `Reserve` obtains a new lease from its retained context, copies the initialized prefix, cleans partial copies on failure and publishes replacement storage only after success. The old allocation cannot be replaced while views borrow it. Affine/borrowed element storage, initialized prefixes, allocator-provider selection, write-guard enforcement and native GC barriers still need complete contracts. Additional proposed signatures and associated-family notation require language design and validation before they can become compiler acceptance fixtures.

## Shared memory and explicit memory primitives

The selected operation surface is one flat static type, `MemoryPrimitives`.
Prefixes identify the family at the use site: `AtomicRead`/`AtomicAdd`,
`UnalignedRead`/`UnalignedWrite`, `NonTemporalWrite`, and `CacheWriteBack`,
alongside `Fence`/`CompilerFence`.
Owners, views and allocators retain their separate storage/lifetime duties.

Storage capabilities do not silently change ordinary accesses. Explicit
atomic reads default to acquire ordering, writes to release, and RMWs to SC
plus full ordering. One compile-time-known `MemoryOrder` selects other valid
orders; checked/unchecked selection is a separate declared arithmetic policy.
The [memory-model library surface](memory-model.md#library-access-surface)
specifies the operation families, result conventions and remaining details.
This is accepted design, not an implemented library.

The [concurrent event contract](runtime/concurrent-executions.md) owns actual
read sources, matched access/fence publication, coherence-consistent SC and
full-boundary effects. Attempted checked overflow is read-only with retained
read/full-order effects, not release of a value that was never stored.
Result-free variants cannot weaken those effects. Live storage updates and
actual lifetime reuse remain distinct.

Default atomic operations require supported lock-free implementations, not
single instructions or wait freedom. Explicit built-in fallback APIs may use
locks only under a compatible shared coordination protocol and declared
dependencies. CAS requires a defined bitwise-comparison representation, not a
user equality witness. Unaligned value access does not promise an atomic snapshot.

## Scalar mathematics, keys and conversion

Arithmetic and bit-selection witnesses are ordinary source-declared
interfaces, not an implicitly supplied numeric hierarchy: a type conforms
only by an explicit, checked witness declaration, and a generic algorithm may
require only the operators its declared constraints name (for example,
addition and multiplication, or separately, subtraction, division, remainder
and bitwise `&`/`|`/`^`). Comparisons, shifts, unary and heterogeneous
operators are likewise separate, independently declared constraints under
this principle; declaring one never implies another. This is a design
principle, not an implicit adoption of every illustrated operator family.
The [bounded witness-support inventory](../implementation/libraries.md#generic-witness-support)
is informative. A helper needing no operator witness may compose over
nested values without one. Calls cannot acquire an undeclared protocol from a
concrete specialization or from a callee's body. Scalar-returning and
conditional-output helpers retain the ordinary signature and
definite-initialization rules; an invalid selection publishes no value rather
than a fabricated generic default. Compiler-local template behavior and sample coverage are
[implementation observations](../implementation/libraries.md#generic-witness-support),
not public generic contracts or restrictions on selected capability witnesses.

The [selected numeric capabilities](language/numeric-capabilities.md) group
same-type additive, multiplicative, increment/decrement, equality/comparison,
bitwise/shift, magnitude and selection requirements. Min/Max/Clamp are one
INumericSelection group. Its comparison evidence supplies the exact Clamp
precondition; the independent IAbsoluteValue group supplies Abs.
IScalarNumber is an optional composition, not a universal numeric root or
prerequisite for a smaller group. The chapter publishes exact self/formal
shapes and primitive witnesses, including every integer data/count pair.
No group implies purity, constant eligibility, total-order laws, parsing,
formatting, conversion, classification or transcendental functions.

The heterogeneous operator/result, identity, floating classification and
conversion interfaces in the [numeric sketch](../../library/numerics.api.terrum)
remain broader proposals, not alternate definitions of these selected groups.
SIMD/complex/matrix contracts need their own actual witnesses and result
relationships; supplying addition/multiplication does not make a vector a
scalar number.

The [primitive matrix](language/primitive-operators.md) owns mandatory scalar
signatures independently of those composite interfaces. Typed integer counts
are control operands, not numeric promotion. Floating `%` uses truncating
remainder, with dividend-signed zero and the selected decimal cohort; a named
nearest-integer IEEE remainder retains its separate contract.

Integer arithmetic is checked regardless of build; wrapping and saturating forms are explicit. Checked integer-to-integer conversions preserve the value or throw on range overflow; unchecked conversions wrap to destination-width bits. For floating-to-integer conversion, checked behavior truncates toward zero then throws if out of range, while unchecked behavior and explicit saturating APIs truncate then saturate (not modulo), with NaN converting to zero. Floating-to-floating conversions follow IEEE-754, with nearest/ties-even default rounding and no language failure for inexactness, overflow to infinity, underflow, NaN or infinity; alternate rounding is explicit, not ambient. No floating-point exception traps or global control-word API are provided. [Conversion policies](conversions.md) distinguish these cases from explicit saturating APIs and `FromBits`/`ToBits` reinterpretation. No promotion between already typed numbers is implicit. Numeric literals are constructed directly in an unambiguous target type, not converted from an implicit default. The single explicit conversion spelling is `T(expression)`; built-in or declared checked conversion contracts select its behavior, while reusable checked/saturating/truncating protocols replace permanent per-source-type `FromInt32` method families. `Splat`/broadcast replicates the *same* lane value or tensor elements, without itself converting them. A widening vector conversion must define output shape and lane count explicitly -- it cannot silently discard half the elements. Fixed-width `float32` math has strict target-format rounding and no implicit reassociation/contraction; `FusedMultiplyAdd`, native/approximate functions and reproducible reductions are separate contracts. IEEE `decimal32` is not CLR `decimal` or a binary64 intermediary. The CLR's decimal BID software implementation is a useful reference, not proof of matching Terrum ABI, parsing, rounding and exceptional behavior.

The target `IParsable<TSelf>.TryParse` contract initializes `init ref<TSelf>` only on success; the destination is vacant and unreadable until then. A development parser's legacy passing modes are [informative support](../implementation/libraries.md#parser-output-support), not alternate source signatures or mandatory alpha compatibility. `TryParse` is the fundamental expected-input API: callers branch on its published success contract, or deliberately use `int32 value = require int32.TryParse(text);` when malformed input should raise. A requirement-failure type may be selected explicitly; that selection applies only to a false result, not exceptions from evaluating the predicate. Core parsing/formatting is ordinal plus invariant: ASCII signs/digits/punctuation, whole-input parse, no locale, normalization or Unicode lookalike substitution. Formatting writes through the supplied typed writer, never a required intermediate `Text`. The default typed hook forwards to the value formatter; a custom handler can intercept values and forward nested typed parts. Refusal stops further writes without replay and can leave already accepted fragments. The supplied [numeric sketch](../../library/numerics.api.terrum) retains the typed writer forwarding point. Collection/text expressions select explicitly registered handler types (for example, `[CollectionBuilder(typeof(ListBuilder<T>))]` on `List<T>` and `[TextBuilder(typeof(TextBuilder))]`), rather than arbitrary same-named methods. The [builder recipe and eager construction contracts](language/construction-handlers.md) bind constructed types by the target's own formal identities, with complete nominal operation admission separately required. Source captures evaluate once, left-to-right before that site's builder acquisition and draining; refusal or consumer disablement cannot suppress these effects. Formatting and spread enumeration occur only when draining reaches them. Ownership, lifetime, refusal, cleanup and publication remain checked, without adopting incremental policies or a deferred-content interface. Expression handlers consume declared typed operation graphs, not callback bodies divined for vectorization; additional standard-library handler APIs require their own adoption.

[TextTarget](language/construction-handlers.md#texttarget-parameter-schema)
is a no-argument parameter schema over eligible generic IFormattable content,
with `[param(content): TextTarget]` as the equivalent named target.
Interpolation forms eager typed captures; later formatting uses the checked
writer/value witnesses without a deferred-content interface or replay of
source holes. Equivalent complete content shapes share a generated type
within the originating module; values, literal text and backing remain
per formatter. Stored/async content retains its actual lifetime obligations.

[Plain construction refusal](language/construction-handlers.md#refusal-in-plain-value-construction)
uses ordinary `require` failure on false, with no prefix/default result.
Explicit Try/build/write calls retain their checked status/output contracts,
and operation exceptions are not converted to Boolean refusal.

[Core IBuilder completion](language/construction-handlers.md#consuming-builder-completion)
is the selected nominal static consuming role, not an instance method found
by name. Source construction transfers its private builder value and retains
the exact true/live, false/vacant and exceptional-cleanup output relation.
Reusable APIs can participate through checked adapters; result-transferred
storage is not dropped again with residual builder state.

[Core IBuilderFactory](language/construction-handlers.md#parameterless-builder-acquisition)
is the selected parameterless source acquisition role with an exact
init-builder output. It runs after captures with its own declared effects/
dependencies. Runtime allocator/destination context remains an explicit API
input, not an argument synthesized by the type-only registration.

[Standard construction operations](language/construction-handlers.md#standard-text-operations)
fix the resolved IFormattable/ITextWriter member signatures. Formatted writes
take borrowed typed value, int32 alignment and validated TextSpan format;
minimum widths count Unicode scalars, not code units or terminal columns.
Staging follows the actual writer contract and cannot replay formatting.
[ICollectionBuilder](language/construction-handlers.md#collection-acceptance-and-spreads)
composes exact factory/completion roles with by-value acceptance and exact
contextual result identity. Target-specific storage/Copy constraints remain
separate; spread iteration requires its own admitted item/cleanup contract.

[Literal registration](language/literal-construction.md) selects one
target-owned parser or grammar adapter. Numeric input retains the complete
signed token spelling; text input is validated decoded content.
The method name alone does not register it, and ordinary parser grammar
need not silently become Terrum source grammar. Required literal construction
has no runtime fallback and grants no allocating string or constant-formal
capability.

These operations are selected capabilities, not universal object defaults:
Terrum does not require every value to inherit `Equals`, `GetHashCode`, or
`ToString`, or impose object-default boxing/allocation on every value or
automatic `IEquatable<T>`/`IFormattable` conformance. Explicit formatting
protocols remain valid, and no contract requires every value to become a
string. Primitive built-ins and compiler-verified operation capabilities remain
valid; no universal root is introduced merely to supply equality, hashing or
formatting. Use operation-specific witnesses/interfaces and explicit adapters.
Keep value equality, identity, key equality paired with a compatible hash,
machine-readable formatting, and display/debug rendering distinct.
Compiler/debugger metadata does not require user-visible root methods.

Adapters declare additional nominal extension-interface behavior without
retroactively changing the original type's witnesses. Family participation is
explicit and declares checked projections/constraints; neither adapter intent
nor equal representation bits implies a universal projection or conformance
for the original type.

Floating `==` follows IEEE (NaN unequal, signed zero equal). A **separate numeric-key comparer** makes all NaNs mutually key-equal, zeros equal and hashes consistent. Decimal key hashing must normalize numerically equal members of the same *cohort* (`1.0`/`1.00`), not hash its raw encoding; signed zero and NaN also need canonical key hashes. A bitwise-identity comparer is different again. Maps/sets accept a paired equality/hash policy (`IKeyComparer<T>` in the [algorithm inventory](../../library/algorithms.api.terrum)); `IndexOf` under ordinary operator equality must not silently adopt numeric-key semantics. Ordering and `Min` variants explicitly select NaN propagation and signed-zero policies. The runtime's distinct decimal `operator ==`, `Equals`, and cohort-normalizing hash illustrate why one implicit universal equality is insufficient ([source notes](references.md)).

The accepted API direction exposes `Abs`, `Min`, `Max` and `Clamp` as static
members directly on each built-in numeric type, not standalone `*Math` service
types. A layered generic-math design makes those members available to
constrained algorithms through coherent independent capability groups and an
optional scalar composite. The canonical
[numeric selection contract](language/numeric-selection.md) fixes same-type
public static nongeneric T Min(T left, T right), T Max(T left, T right) and
T Clamp(T value, T min, T max) on the 20 numeric scalars.
Exact logical interface names, groups and primitive membership are selected
in [numeric capabilities](language/numeric-capabilities.md).
Physical witness/member/program codecs and implementation support remain
separate from that selection.

The canonical [absolute-value member contract](language/absolute-value.md)
fixes public static nongeneric `T Abs(T value)` on the 20 numeric scalars.
Unsigned values are unchanged; signed integers return their same-type
nonnegative magnitude except that the minimum raises checked overflow and
publishes no result. Floating Abs maps either zero to positive zero and
either infinity to positive infinity. It clears only the representation sign,
preserving decimal quantum and NaN payload/quiet-signaling state.
An explicit wrapping magnitude could return the signed minimum unchanged,
but no variant name, policy case or witness is selected. Ordinary Abs never
silently acquires that behavior from an enclosing policy or build mode.

The accepted `Min`, `Max` and `Clamp` selection behavior applies across
built-in numeric types. `Min` and `Max` select the numeric
extreme, producing quiet NaN if either operand is quiet or signaling NaN.
For a zero pair, `Min` returns negative zero if either operand is negative zero
(otherwise positive zero); `Max` returns positive zero if either operand is
positive zero (otherwise negative zero). Equal finite same-sign representations,
including decimal zeros, use IEEE 754-2019 totalOrder for the tie:
Min selects the earlier representation and Max the later, retaining the
selected operand's quantum. This does not use totalOrder to select among NaNs
or impose a raw-encoding order. After ordinary source-order argument
staging, `Clamp` first checks its published min <= max precondition;
NaN or numerically reversed bounds use exact Core InvalidBoundsException
through ordinary explicit requirement construction. With valid bounds,
a NaN input produces quiet NaN where supported; otherwise only a strictly
out-of-range input selects a bound, and an in-range input is returned
unchanged, including its signed-zero sign and decimal cohort member. A selected
non-NaN bound retains its representation as well. Infinite and equal bounds
are valid where supported. This is not generally the same result contract
as Min(Max(value, min), max).
[Floating representation and environment rules](language/floating-environment.md)
make Abs/negation sign-only, require quiet NaNs from arithmetic/conversion/
selection, and select positive quiet zero-payload NaNs for admitted constant
NaN-processing execution. Runtime processing-result sign/payload remain
unspecified; no ambient language status-flag facility is supplied.
Actual native control duties and complete execution/failure dependencies remain.
Abs's exact owning
subjects, same-type signature and integer overflow domain are selected;
numeric selection signatures, representation ties and invalid-bound payload
are also selected;
logical numeric groups and floating result contracts are also selected;
physical member/witness/program/event codecs are not. The
[scalar catalog](language/scalar-failures.md) now assigns
OverflowException to signed-minimum Abs without selecting its physical
transport. No unconditional `NoThrow` contract,
primitive intrinsic, or additional math operation follows from this direction.

## SIMD as a family of shapes

Explicit `simd64/128/256/512<T>` are fixed ABI shapes where supported; `simd<T>` denotes a **genuine target-scalable** type, not a preferred-width fixed vector. The [SIMD contract inventory](../../library/simd.api.terrum) uses locally labeled family pseudocode: "any fixed vector of float32" preserves the actual concrete type/width and its associated mask for checking/codegen, unlike runtime-erased text. Two operands and their result must retain required same-instantiation relationships. Membership and common operations are checked declarations, not matching names. Vector addition returns that same vector type; lane comparisons return its mask; reductions may return the element type; whole-vector equality returns `bool`. A mask represents **boolean lane predicates**, not a numeric data vector of all-one bit patterns. `CompareEqual` produces a mask; `Any`/`All` reduce it to `bool`; `Select` consumes it. Mask shape and granularity must match element shape. Bit reinterpretation, numeric conversion, splatting, and rebinding families are distinct operations. Family participation requires explicit opt-in with declared checked projections and constraints; exact family/associated-member grammar and eligible projections remain specification work. No universal `Type<T>`-to-`Type<void>` erasure or inferred covariance follows; see [type families](type-families.md).

Fixed SIMD values can be `[Sized]` if layout supports their element type. The proposed initial scalable profile is SVE-like **sizeless**: locals, scoped loans and statically specialized compatible calls, but not ordinary fields, heap boxes, array/pool elements or aggregate payloads. [Arm ACLE](references.md#other-official-library-specific-prior-art) specifies related SVE vector/predicate restrictions, but does not supply Terrum's ABI or lifetime rules. Such live values cannot cross `await`, incompatible vector contexts or an ABI boundary that cannot carry them. RVV requires its own validated profile; mask representation, vector length and ABI cannot be copied blindly from SVE. Specializations explicitly name an exact closed generic declaration and target requirement, for example `[Specialize(Scale<float32>, Requires = CpuFeature.Avx2, Priority = 100)]`; source binding remains on the generic declaration and AOT dispatch checks CPU and OS support. AOT publishes a bounded explicit variant plan rather than inventing runtime code generation. Reusable static operators/witnesses allow code sharing without erasing masks, boxing sizeless values or conflating vectors with scalar `INumber`.

Tail handling is part of correctness: `First(remaining)` masks the final iteration, inactive masked **loads/stores do not access memory**, and no safe lane is poison/undefined. Stores with repeated scatter indexes preflight active bounds before writes, then define duplicate-index order (last active lane wins). Aliasing/alignment rules are checked at the public boundary. A fixed vector may use full-vector processing and a scalar tail instead; neither strategy may read beyond the provided span. Dot/sum ordering across vector widths needs an explicitly chosen reproducibility policy: per-vector lane order alone is insufficient to make a whole-input reduction bitwise stable. Strict scalar order is a defensible default; a separately named relaxed/high-throughput reduction may regroup and document architecture-dependent results.

## Callable kernels and collection parameters

The target `func<Args..., R>` is a lifetime-aware single-cast callable;
`fnptr<Args..., R>` is a raw code pointer with a closed target-defined
signature and no retained context. Named nominal functors remain ordinary
checked protocols, as illustrated by `IMapper` and `IPredicate` in the
[algorithm inventory](../../library/algorithms.api.terrum). Compiler-supplied
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
be elements when normal storage and lifetime contracts permit them. `params`
modifies the collection parameter's call form, not its type. A matching
collection argument binds directly in normal form; expanded form gathers
individual arguments into a new collection.
Resolve applicability first, then compare argument quality independently:
identity is better than permitted access/qualification weakening, and a
candidate is better only if it is no worse for every argument and better for
at least one. Crossed better/worse arguments are ambiguous; do not sum match
scores. Apply declared specificity and the accepted by-value/`implicit ref<T>`
and `params` rules; a fixed-arity, non-expanded candidate wins over an
otherwise equal expanded `params` candidate. Do not break ties by return type,
declaration order or literal numeric fit, and do not search conversion chains.
Callable grammar and contextual completion follow
[calls and inference](language/calls-and-inference.md), not a candidate
collection's private implementation. Binding is fixed at source
compilation, not selected by a backend. For `Log<T>(T item)` versus
`Log<T>(params List<T> items)`, a
`List<int32>` argument selects `List<T>`: both infer one `T` and match by
identity after inference, but `List<T>` is more specific than bare `T`. This
does not depend on the `params` modifier or on the number/cost of inferred
parameters. These callable and parameter examples retain the canonical binding rules;
the candidate library declarations are not thereby adopted or executable APIs.

## Views, shapes and tensor primitives

Span/TextSpan value relationships and the selected dependency schemas are
canonical. The additional tensor, SIMD, algorithm and representation names
below describe constrained design direction, not a complete exported API
catalog. Exact family members, witnesses and representation support still
need separate admission; [inventory status](libraries/adopted-inventory.md)
does not infer them from these examples.

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
the checked [member-contract refinement](language/dependencies-and-views.md#published-witnesses-and-generic-refinements),
published in the witness and required by the generic declaration.
It is not inferred from a current body or resolved
with a second `IScopedSequence` interface. Neither the broad nor narrowed rule
extends owner/allocation lifetime or bypasses disposal.

`TextSpan<TCodeUnit = void>` is one family; bare `TextSpan` is exactly its declared encoding-erased `TextSpan<void>` specialization. `void` does not satisfy `[TextCodeUnit]`; only the `char8`/`char16`/`char32` members expose public `AsSpan()` and `IContiguousSequence<TCodeUnit>`. The common surface includes `IsEmpty`, ordinal operations and formatting. Erasure preserves actual encoding, immutable validity and every backing/lease dependency without text copy/transcoding. Checked projection returns a validated typed view only when representation matches; its `AsSpan()` can then forget validity, not immutability or lifetime. There is no `Span<void>` projection. Conditional members/conformance require family contracts, not extension lookup alone; [application sketches](../../library/application.api.terrum) label that grammar as provisional.

[`TensorShape` and `TensorSpan<T>`](../../library/tensors.api.terrum) express a *logical* rank-dynamic view, not a second owner family. Rank and dimension lengths are `nuint`; element strides are signed `nint` to allow reversed/transposed views. Rank-zero `[]` is a scalar with one element, while `[0, n]` is empty with zero elements; a default/empty sentinel must have an explicit shape rather than ambiguously representing both. Validate **every** dimension length as representable within the address/offset range, independently of order. Then `ElementCount` is zero if **any** dimension is zero; do not multiply the remaining nonzero dimensions of an empty shape. Otherwise calculate their checked product (rank zero has the multiplicative identity, one). For nonempty views, `origin + Σ(index[i] × stride[i])` uses checked signed arithmetic; derive minimum and maximum reachable offset using `(length[i]-1) × stride[i]` with checked operations and validate *both* against backing bounds. For zero-element views **skip all `(length-1)` address arithmetic and dereference**; their origin may be an in-bounds end location, and metadata/owner dependencies remain valid. `nuint` lengths cannot be casually cast to signed offsets; reject nonempty shapes outside representable address/element ranges. Width, alignment and shape-data lifetime are not inferred from a pointer alone.

Small shape metadata can be inline; arbitrary rank may use independent, immutable Foundation-owned metadata or explicitly borrowed metadata. A tensor view then has **two** dependency sources: data backing/access *and* externally stored shape metadata. Returning/storing/erasing it preserves both, even at an empty/end slice. The whole-source schemas can declare both dependencies; the tensor's exact construction/shape and anchor ABI remain separate. A provisional ViewOf sketch cannot prove them. The factory requires `write Span<T>` for writable storage and returns `write TensorSpan<T>`; `mut Span<T>` would only permit descriptor rebinding. A read-only source returns a read-only view, never an upgraded writable one. Writable tensors require injective logical-index-to-address mapping (zero-stride broadcasting with length greater than one is read-only); overlapping or duplicate addresses cannot manufacture multiple writable loans. Broadcasting is a read view or an explicit materialization, not an implicit arithmetic conversion. `TryAsDenseSpan` succeeds only when the logical order actually maps to one contiguous range with proper access/lifetimes. An arbitrary transposition must not pretend to be contiguous. `CopyTo` checks equal logical shapes and `[Copy]` elements, traverses logical indexes, accepts disjoint backings or **identical per-index mappings** (no-op), and rejects displaced or differently strided overlap before writing; it does not allocate a hidden temporary.

Core `TensorPrimitives` operate over borrowed spans and caller destinations, not hidden owning tensors. `Add` requires compatible lengths/shapes and preflights destination capacity and alias safety before mutation. `Add`, `Sum`, `Dot` and copying tensor views explicitly require `[Copy]` complete elements when invoking value-taking operators; arithmetic witnesses alone confer **no** copy permission. A future operator consuming `ref<T>` would need a separate contract, not implicit duplication. Exact same-position in-place operands are permitted only where each output reads all required inputs before overwriting; displaced overlap is rejected unless an operation explicitly supplies memmove/temporary semantics. Numeric callbacks can still throw mid-operation, so an algorithm without a no-throw bound does not promise transactional output. `Sum(empty)` returns additive identity, `Dot(empty,empty)` returns zero; unequal dot lengths fail. A strict reduction observes logical row-major order and specified floating behavior; accelerated relaxed variants can specify different rounding/order. Fused operations avoid an intermediate allocation, but must not silently change IEEE rounding to FMA. Transcendentals, normalization and softmax require separate domain, overflow, NaN and stability policies; the runtime `SoftMax` span implementation uses exp-then-sum-then-divide, illustrating a cost/overflow tradeoff, not a Terrum semantic mandate.

## Collections, algorithms and application services

The concrete collection, algorithm, writer and application names below are
candidate surfaces constrained by this design direction. The selected
Core construction/iteration/formatting roles are distinct from adoption of
those candidates. The [informative implementation inventory](../implementation/libraries.md)
separately records the bounded managed-node example; it is not the generic
List/LinkedList API or evidence of its guards, builders and public contracts.

Generic collections are the primary collection APIs, not one half of parallel
generic and object-based hierarchies. Heterogeneous values use explicit
carriers, unions, or interface representations; for example,
`List<box<I>>` stores one checked managed-handle form while allowing distinct
implementations. The `I` payload itself is not an implicit stored handle.

Foundation owns explicit `Buffer<T>`, `Vector<T>`, arena-backed storage, maps and persistent shape metadata with allocator/lease obligations; Application provides managed aliasable collections. The mutable `List<T>` and `LinkedList<T>` declarations require writable receiver access for structural mutation. A read-only collection path may still return a `write C` element handle if that is part of the element type, but cannot write its slots, nodes or links. The [managed collection inventory](../../library/collections.api.terrum) specifies initial `List<T>` element restrictions (`[ManagedStorable]`, `[Copy]`, `[Sized]`); this is **not** a permanent language ban on storing pointers/loans or affine elements. GC visits only initialized slots and uses write barriers; no fabricated `default(T)` fills unused capacity. Preflight capacity, checked count/version and guard conflicts precede mutation; remove/clear vacate slots and release retained references. `ListWriteAccess<T>` lends checked exclusive slot access, keeps actual storage stable and live and releases once. Every alias, indexer, cursor and reentrant callback checks the relevant guard. It is not a cross-thread mutex. `TryGet` distinguishes missing index from a present optional/null element.
For `LinkedList<T>`, `First`/`Last` and node-link projections use checked
`[MatchedAccess]`: readonly traversal returns readonly node handles, while a
writable list path may preserve write access. This is distinct from an element
whose own `T` qualification is `write C`; a readonly list cannot expose a
writable structural node whose `Value` or links would mutate list-owned state.

`GetWriteAccess`/`TryGetWriteAccess` are provisional names. Renaming is not needed to settle the invariant: acquisition checks all conflicting aliases, a view cannot outlive its exclusive guard, and cleanup releases authority exactly once.

`LinkedListNode<T>` has stable **logical node identity**, not a promised machine address. Detached nodes remain usable and may be reinserted; attached nodes retain exactly one owner. Removal clears owner/links. `Clear` detaches *all* nodes, including externally retained nodes, and cannot merely drop the head/tail. Node values are accessors, never public writable fields that bypass guards. Membership/count/version are checked before nonthrowing link changes. A concrete `foreach` cursor need not box; structural mutation invalidates it, while payload replacement alone is not structural. Copy-, borrow-, consume- and snapshot-yielding iteration are separate operations.

[Standard synchronous value iteration](language/iteration.md) uses Core's
Terrum.ICursor<T> with TryMoveNext(init ref<T>) and mandatory Close, and
Terrum.ISequence<T, TCursor> with readonly GetEnumerator. Both factory sources
and already-created direct cursors are admitted; distinct source relationships
need explicit selection/adaptation, not precedence by name or body.
True publishes one complete item, false leaves it vacant, and checked faults
prevent item publication while preserving required cleanup-safe close.
This Core protocol has no universal Copy bound or standard Current property.
Its availability and required representations/backing remain explicit profile
dependencies, not an automatic requirement for the freestanding language floor.

[Standard async value iteration](language/async-iteration.md) uses exact Core
IAsyncSequence<T, TCursor>/IAsyncCursor<T>. The readonly GetAsyncEnumerator
factory is synchronous; direct cursors keep ordinary explicit affine transfer.
TryMoveNextAsync(init ref<T>) reserves its exact destination/cursor state and
publishes one complete item only with observed true completion. False/failure
leaves vacancy after cleanup, not an absent optional item or swallowed error.
CloseAsync is required and awaited after pending advance and item retirement,
before source/header dependencies end. No Current, universal Copy bound,
Task<TCursor> acquisition, general Task lifecycle or implicit async spread is
introduced; required completion/reservation support stays profile-explicit.

The inventory's ListCursor<T>/LinkedListCursor<T> implement that by-value
contract under their retained `[ManagedStorable], [Copy], [Sized]` item profile.
They copy selected stored items only with that permission, retaining any
actual item dependencies and never moving nodes/elements out of a borrowed
collection. Their published source/guard relationship survives until close;
version/guard failure cannot make required closing unsafe or dispose the list.
Named affine cursors transfer with move; closing a loop cursor does not
dispose the original collection. No result becomes a loan because it is large.

[Standard lending iteration](language/lending-iteration.md) selects distinct
readonly/writable sync/async source/cursor roles and single descriptor outputs.
Loop bindings alias existing element places; writable factories require mut
receiver access and actual element-write authority. Loans are cursor-bound by
default, with checked published backing/invalidation refinements permitting
longer validity where available. Every actual source/root/guard remains live;
ending a descriptor/alias releases its loan, not T, and task completion does
not erase a surviving loan's dependencies. Cursor-owned scratch retains its
own declared holder cleanup after dependent loans end.
These protocols do not register lending witnesses or stable backing for the
inventory's List/LinkedList cursors, Span, views or MatchedAccess candidates.

[Generated producers](language/generated-producers.md) register same-module
IteratorCursor shells and their sole Iterator callable with direct affine
results. Source creation and cursor operation contracts are distinct, and
async body creation still returns its cursor synchronously.
The compiler supplies private frame/selected slots, not a universal erased
Sequence/Task factory. Concrete collection/range APIs retain their independently
checked bodies, storage, capture and layout contracts.

The proposed `List<T>` and `LinkedList<T>` APIs are intended to participate in
the same selected collection-expression protocol; their candidate declarations
do not yet supply adopted registrations or witnesses.
For an independently admitted provider, `LinkedList<int32> pending = [10, 20]`
would construct fresh nodes from element values, without moving nodes from
another list or promising contiguous capacity. Its builder must accept values
once in source order and publish the private completed graph only on success.
Failure/abandonment retires partially built nodes exactly once; false publishes
no default list. Node-based insertion remains a separate candidate operation.

[`SpanAlgorithms`](../../library/algorithms.api.terrum) illustrates constrained
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

Text targets give character fragments `rune` context and forward them through `TryWriteScalar`, without requiring a temporary text object or a per-primitive overload catalog. `require format.IsEmpty` expresses Point's supported-format check; a false predicate raises the selected requirement failure in every build, not a formatter refusal. `require` is safe; unsafe authorization is needed only to catch/recover. Async library design preserves a function's or callback's declared affinity, including cleanup and unwinding, without giving unbound helpers affinity merely through their caller. An incomplete ordinary `await` does not capture the caller's executor; APIs that require a particular awaiting continuation use an explicit executor adapter. Cancellation remains a request, not completion. Exact scheduler/awaiter spellings and runtime implementation remain open.

## Open contract work

Associated vector/mask family syntax and valid element predicates; scalable
ABI/vector-context rules; exact anchored view and multi-source shape
representation; TensorShape layout and signed-stride support; strict/relaxed
reduction APIs; allocator selection; concrete text storage and async/backpressure
APIs; and collection storage profiles beyond the candidate Copy restrictions
remain separately staged. The selected eager handler and checked
dependency/member-refinement contracts are no longer unspecified by that list.
Complete source/runtime/target/physical support remains required.
Resolve each [unselected surface](libraries/adopted-inventory.md#3-direction-and-deferred-surfaces)
before claiming its API or replacement compatibility; implementation support
for the inventories remains [informative](../implementation/libraries.md).
