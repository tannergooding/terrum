# Language and library improvement review

**Design review; §§1.1–1.3 and the bounded collection-based `params`,
callable, tuple, alias, and functor directions in §1.4 are accepted.**
Unresolved grammar and other proposals add no syntax/API promise
or compiler authorization. The review draws on C#/.NET, Rust, Go, Swift, Kotlin, C++
and Java experience without treating any one ecosystem as a template.
[Language](language.md), [lifetimes](lifetimes.md), [type families](type-families.md),
[conversions](conversions.md), and [libraries](libraries.md) remain authoritative;
status follows [implementation](implementation.md) and [roadmap](roadmap.md).

## Thesis

Prefer **explicit, composable contracts** over importing the CLR's universal
object conventions or reproducing each language's feature catalog. A small
number of checked mechanisms can cover many library gaps:

1. **Nominal capabilities and checked conditional witnesses** for equality,
   formatting, generic operators, collections, and user-defined participation.
2. **One representation/access/lifetime model** for arrays, views, shapes,
   opaque layout, and explicit ABI contracts.
3. **Declared compile-time and runtime expansion contracts** for literals,
   handlers/builders, and specialization.
4. **A deliberate effects/failure model** separating expected outcomes,
   exceptional failures, programmer defects, cancellation, and async ownership.

The target is predictable AOT/JIT and separately compiled semantics without
invisible boxing, copying, allocation, or lifetime extension. C#/.NET supplies
ergonomics, Rust foregrounds representation/borrowing/coherence/`Drop`, and
systems ABIs require explicit foreign contracts; none is copied wholesale.

## Current baseline and status vocabulary

| Label | Meaning in this review |
|---|---|
| **Accepted direction** | Already established by Terrum design documents; this review argues for preserving it, not reopening it. |
| **Refinement proposal** | A mechanism to make an existing direction more usable or precise; requires language/library design and contract rules. |
| **Open proposal** | A genuinely unsettled capability or policy; requires a decision before it can be treated as language behavior. |
| **Implemented subset** | Verified in the bounded compiler/runtime profile described by the implementation documents. It is not a claim that the general feature is complete. |

The reviewed implementation baseline is `0597c8b`, with managed
semantic-module v24: optional managed handles, source-defined classes, and
explicit `write` authority for managed handles and mutable class receivers.
It includes compilation-local constrained generic specialization, but not a
public managed ABI, general interfaces/witnesses, array/view libraries, async,
or the proposed libraries. Examples in this review and `.api.terrum` files
remain design material.

## 1. Make capabilities explicit and composable

### 1.1 Accepted: readonly-by-default access and invariant writable storage

Declarations are readonly by default. `mut T value` permits rebinding a local;
`mut class C`/`mut struct S` declares mutable nominal storage; instance
receivers are readonly unless the method is marked `mut`. `write` qualifies
access through a reference/view and grants no authority that its source did not
already possess; it does not override immutable fields or nominal restrictions.
There is no explicit `read` keyword, deep-read type, or v1 frozen/deep-frozen
type. Readonly is an access-path restriction, not graph immutability, alias
uniqueness, purity, or thread safety.

`mut Span<T>` permits descriptor rebinding, `write Span<T>` permits element
slot writes, and `Span<write C>` permits writes through contained `C` handles
without writable slots. Their combination composes these distinct permissions.
`mut` is not a generic type-argument qualifier. A writable view or storage
contract is invariant: treating `Array<Derived>` or a writable
`Span<Derived>` as `Array<Base>`/`Span<Base>` could permit storing a value that
is not a `Derived`. A readonly projection is not automatically covariant; any
future projection must prove compatible representation, preserve
backing/lifetime, and specify any per-read conversion cost. No
reinterpretation, materialization, copy, or hidden conversion is implicit.
Readonly access does not make aliased backing or its object graph immutable.

The canonical reference representations are `ref<T>`, `ptr<T>`, `box<T>`,
`val<T>` and `own<T>`. `init ref<T>` is an initialization loan to vacant
storage, unreadable until full initialization/publication; it is not an
arbitrary writable `ref<T>`. The validated semantic-v24 batch implements this access distinction for source
classes and `ManagedNode`: `mut class` declarations and writable `mut` class
receivers, readonly bare handles, and explicit `write` capability are retained
through fields, optionals, parameters, results and inference. A writable child
handle remains writable through a readonly parent; no deep-read propagation is
implied. `write` storage is invariant, and output destinations require the
exact qualified type; value-level `write`-to-readonly attenuation is allowed.
Constructors initialize unpublished objects before publication. Legacy
`bool`/`out` conditional publication remains the executable output contract;
it is not replaced by `init ref<T>`. Details and current support boundaries are in
[language](language.md), [lifetimes](lifetimes.md) and
[implementation](implementation.md).

As contrast, .NET's reference-array covariance is not inherited by mutable
`Span<T>`: its array constructor rejects covariant arrays unless the runtime
array type is exactly `T[]` ([CoreLib `Span<T>`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Span.cs)).

Do not imply one all-purpose `Array<T>`: separate variable-length storage,
fixed-extent [`InlineArray<T, const nuint N>`](../library/application.api.terrum),
and rank-dynamic tensor views. An erased/root array must not bypass element
validity, storage, ownership, or lifetime. Shape metadata may be inspected, but
writable stores need checked element type/validity/ownership; do not invent
`default(T)` or scan uninitialized slots. Keep logical shape separate from
physical layout: strided views need not be contiguous, and SoA field access is
not `Span<Record>` without that layout. Check bounds, access, aliasing, and
metadata lifetime before mutation; zero-sized dimensions differ from rank-zero.
The CLR `Array` base is runtime-special, not an ordinary generic capability
([CoreLib `Array`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Array.cs)).
Managed graphs, unique buffers, and borrowed spans retain distinct ownership.

### 1.2 Accepted: no universal object operation defaults

Do not require every value to inherit `Equals`, `GetHashCode`, or `ToString`,
or require universal object-default boxing/allocation or automatic
`IEquatable<T>` or `IFormattable` conformance. This rejects universal
object-to-string behavior; it is **not** a claim that .NET `object` implements
`IFormattable`, nor an objection to explicit formatting protocols.
Primitive-specific built-ins and explicit compiler-verified capabilities
remain valid. This direction does not settle whether Terrum has any root type
at all; root-type existence is a separate open decision.

Keep compiler diagnostic/type metadata distinct from user-visible operations:
the compiler may need type identity for checking, diagnostics, or tools without
requiring each runtime value to inherit a root object or expose identity,
equality, hashing, or formatting.

Keep distinct operations distinct:

- value equality (and its operand/result types);
- key equality paired with a compatible hash;
- reference/identity equality where identity exists;
- invariant machine-readable formatting;
- human-facing display/debug formatting.

`IEqualityComparer<T>`/`IKeyComparer<T>`-style policies are preferable where a
collection needs a choice of equality semantics. Floating `==`, NaN-aware
numeric-key equality, bitwise identity, decimal cohort equality, and display
text must not collapse into one universal override. A useful formatting
protocol can be shared without promising that every value allocates a string.
Compiler/debugger metadata may support tool representations, but that does not
require a language-level `ToString` method.

This separation also exists in .NET: [`Object`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Object.cs)
supplies object defaults; [`IFormattable`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IFormattable.cs)
is separate and opt-in. Terrum preserves explicit formatting protocols without
requiring every value to become a user-visible string.

### 1.3 Accepted direction: generic APIs and type-system policies

The policies below are accepted target-language direction, not claims that
general interfaces, overloads, inference, erasure, or evolution checks are
implemented. Exact grammar, metadata encodings, and ABI details remain open
where noted. The bounded implementation and scheduled work are tracked in
[implementation](implementation.md) and the [roadmap](roadmap.md).

**Generic collections and operation protocols.** Generic collections are the
primary APIs; heterogeneous storage uses explicit carriers, unions, or
interface representations rather than parallel generic and object-based
collection hierarchies. `List<box<I>>` is one such homogeneous carrier whose
elements may refer to different implementations. This does not reject generic
math: granular static-operation protocols and useful composites such as
`IScalarNumber<T>` coexist, with no mandatory universal number interface.
Copy, access, ownership, and family requirements remain independent.
An access constraint applies to the complete qualified `T`; it is distinct
from exposing an unknown `T` through a readonly projection, which does not
rewrite the type argument or erase its original authority.

**Nominal witnesses, coherence, and associated types.** Behavioral conformance
is nominal and requires a checked witness; conditional member guarantees do
not by themselves establish behavioral conformance. Conflicting claims for a
type/interface witness are rejected rather than selected by import order.
Producer-owned, versioned witnesses are the recommended bounded policy;
external conformance ownership and its grammar remain open. Applicable
members outrank extensions; conflicting extensions may be explicitly
qualified, but the qualification syntax remains open and extensions never
replace nominal witnesses.

Associated types and `Self` relationships are accepted direction, including
integer `Wider`/`Signed`/`Unsigned` relations and SIMD element/mask relations.
One selected witness binds each associated result. Do not infer a witness
backwards through a non-injective projection, or let a constraint choose an
arbitrary implementing type. Multiple valid relations require explicit
disambiguation. Exact associated-type and constraint syntax remains open.

**Erased interface representations.** There is no `dyn` keyword and no
implicit storable interface handle. Bare `I` names a nominal behavior/abstract
payload type, not a local or field representation; storage chooses an explicit
form such as `ref<I>` (borrowed erased receiver), `write ref<I>` (mutable
borrow), `box<I>` (managed aliasable handle), or `write box<I>` (writable
receiver). Explicit conformance is written `T : I`; a generic constraint keeps
`T` concrete. `List<box<I>>` can hold heterogeneous implementations.

Projection to `box<I>` requires a checked representation conversion, never an
implicit allocation or reinterpretation of a class-handle slot. Explicitly
boxing a Copy inline struct creates an independent payload; transfer from an
eligible unique value is explicit;
projecting an existing class object may reuse its identity without allocation.
`write ref<I>` can mutate the original implementation but cannot replace an
unknown inline payload with an unrelated implementation. Slot and referent
authority remain distinct: `write ref<box<I>>` can write a slot containing a
readonly object handle, while `ref<write box<I>>` has a readonly slot and
preserves writable object authority. `ref<C>` borrows a class-handle slot, not
automatically the object payload. Opening one existential never proves that
two hidden `Self` types match; dynamic-callability, associated bindings,
borrow lifetime, and dispatch representation remain to define. No spelling
here chooses an ABI or authorizes a cast.

**Overload binding and source compatibility.** Overload resolution is a
source-compilation decision. The selected declaration and closed generic
arguments are recorded; linking and JIT validate and implement that binding,
not rerun source overload resolution. Runtime virtual/interface dispatch and
devirtualization are separate. An exact match precedes permitted erasure;
there are no newly implied numeric conversions. Constraints filter
applicability, but initial bounded resolution has no duplicate signatures
that differ only by constraints or arbitrary logical subsumption. Return-only
overloads are not introduced. Applicable members beat extensions.

For example, a sole `Set(int32)` accepts `Set(1)`. Adding `Set(uint32)` makes
that source call ambiguous, but an already compiled caller retains its recorded
`int32` binding. Removing a bound declaration remains a compatibility break.
This separates source rebinding risk from binary identity and runtime dispatch.

**Generic inference.** Inference proceeds in this order: explicit type
arguments pin their positions; typed arguments establish generic types;
expected-result context fills still-unresolved positions; then contextual
literals and lambdas are checked. Context cannot retarget typed operands or
replace established generic types. Thus `int64 x = 20 * 20` constructs
`int64` operands, while `int32 a = 50000; int64 y = a * a` is invalid without
explicit conversion. A schematic `U Convert<T, U>(T value)` can infer `T` from
a typed argument and `U` from an expected result; `Convert<_, int32>(value)`
pins only `U`. Likewise `func<_, int32> f = ToInt32` can fill one hole from a
unique method signature. Hole spelling is illustrative; all holes must resolve
at local binding, and exported fields, aliases, and parameters cannot retain
unresolved holes. There is no wildcard, existential, higher-kinded, or
non-injective inverse meaning; ambiguity is diagnosed. Current binder support
is narrower and does not implement this general inference contract.

**Type and case evolution.** Evolvability is the default, independently of
`sealed`: sealed prohibits inheritance, not interface dispatch and not future
API evolution. A closed case-set promise, fixed layout, and frozen API are
separate scoped contracts. Integer enums admit unnamed underlying values;
`[Flags]` defines named bit operations but does not close the set. Nominal
unions still require a valid active payload, and an overlapping raw layout is
not a safe union representation.

A closed case-set contract makes adding a case breaking; compatibility
validation ordinarily rejects the replacement before execution. Exhaustive
compiled dispatch still has a
checked fail-fast unknown-case path, never an optimizer `unreachable` promise.
Runtime fallback and compile-time explicit coverage are separate dimensions.
The accepted `[Exhaustive]` fallback attribute, shown as
`[Exhaustive] _ => HandleUnknown(status)`, handles unknown runtime cases while
excluding itself from known-case coverage;
missing known cases warn (and may be promoted), guarded arms do not establish
coverage merely by naming a case, and the initial design permits only a final
unguarded wildcard. Ordinary `_`
suppresses missing-case warnings. Match grammar and the closed-set marker
remain open. Enum coverage diagnostics concern
named values; the fallback also handles unnamed underlying values. Unknown payloads
must remain safely rooted, forwarded, and disposed, or the representation is
rejected.

Adding an interface member compatibly for existing implementations requires a
default implementation that satisfies
its declared contracts and a compatible witness/dispatch mechanism. A default
body alone does not make every evolution safe: new required associated types,
tighter effects/access/loan bounds, or conflicting inherited defaults can
break implementers. Sealed types are not frozen APIs.

**Compatibility forwarding and tooling.** A future compiler-recognized
forwarder preserves the old identity and its full effects, access, lifetime,
and ABI contract while naming one exact replacement or closed specialization.
It may synthesize an adapter or old native entry point; fuzzy linker searches
are not allowed. Source overload priority is separate, and metadata-stub
visibility to source must be explicit. Exact attributes and version policy
remain open. Build tooling should compare declared baselines/contracts and
report binary breaks, source-binding risks (including overload additions and
public parameter-label changes), witness/default conflicts, closed-case
evolution, and layout/access/ownership/effect/lifetime requirements. It cannot
prove arbitrary implementation behavior. The existing bounded semantic-module
replacement checks are not this general forwarding or compatibility-analysis
facility.

### 1.4 Collection-based `params`, callables, and tuples

**Agreed collection-based `params` direction:** `params Collection<T>` with a
common `Span<T>` form uses ordinary checked collection construction and
ordinary per-element copy/move rules. Expanded elements initialize new slots,
not aliases of caller argument locals. There is no special `ref`-parameter
mode for expanded arguments, though an explicit reference type may be an
ordinary element value where normal storage and lifetime rules permit it.
Access follows the accepted `mut`/`write` and qualified-`T` rules in §1.1.
The exact overload/binding rule for an existing collection versus expanded
elements remains open.

**Accepted bounded callable and tuple direction:** the compiler knows these
closed type families at any supported arity; each callable signature and
tuple instance has one fixed shape. This is not a user-defined type-pack
facility: no pack declarations, expansion, indexing, folds, or forwarding.
Result is last and parameter names are omitted from these type spellings:

```terrum
void Consume<T>(params Collection<T> items);
int32 Sum(params Span<int32> values);

fnptr<int32, bool, int64> parse;  // (int32, bool) -> int64
func<int32, int32> map;           // int32 -> int32
tuple<int32, bool> state;         // ordered inline elements
```

`func<Args..., R>` is a lifetime-aware, single-cast callable with one target;
it may represent a static target or retain a bound receiver/environment under
ordinary checked capture, ownership, and lifetime rules. It does not imply
that a borrow is owned, that a lifetime is extended, or that storage is
allocated. Its layout is uniform for the complete type regardless of the
specific target, but an exact fat size is not promised. `fnptr<Args..., R>` is
a raw pointer to target-defined code, with no independently retained
environment; the code must outlive every use. There is no multicast callable
or custom nominal-delegate declaration in this bounded direction. Type aliases,
including generic signature aliases, are transparent aliases, not nominal
delegate identities; generic aliases preserve labels, access, effects, and
calling convention with the complete signature. Simple nominal wrappers remain
a separate feature.

Callable parameter names and tuple element labels belong to the statically
visible source view and its metadata/tooling, not to the ABI or nominal type
identity. Calls use labels from that static view, not target implementation
names; renaming a public label can break source compatibility without changing
the ABI. Named arguments evaluate in source order before binding to parameter
positions. Tuples are inline ordered heterogeneous values with fixed,
closed shapes, not implicit argument lists or alternate function ABIs. Their
Copy, uniqueness, borrow, and access properties follow the complete element
types. A tuple's labels do not make it nominal or transparent.

An unannotated `fnptr` uses the Terrum callable convention in this direction,
analogous to a C# managed function-pointer contract but not CLR execution.
Foreign conventions are explicit checked type-use attributes on the callable
entry, not inferred from `ptr` or repaired by pointer casts. For example,
`[CallConv(NativeCallingConvention.Winapi)] fnptr<...>` and
`NativeExport(CallingConvention = ...)` use existing design vocabulary, but
exact attribute names, grammar, and import/export ergonomics remain open. The
annotation follows the callable through nested pointers, aliases, parameters,
and results. No convention conversion is implicit. ABI choice does not itself
promise no-GC, no-allocation, exception containment, or marshalling. The `func`
invocation entry uses Terrum's callable contract even when its target uses a
foreign ABI; any adapter crosses that declared boundary and preserves the
target's effects while making capture/allocation costs explicit. Native
`ptr + context` callbacks remain ordinary explicit aggregates with lifetime
rules; C++ member-function pointers are not generally raw code addresses, and a
trampoline does not hide its state or lifetime.

| Callable case | `fnptr` direction | `func` direction |
|---|---|---|
| Static | Raw pointer to a static function with ordinary arguments | Stores a static target such as `M(T)` |
| Open instance | Candidate `fnptr<this C, T, R>` passes the receiver and remaining arguments explicitly | Candidate `func<this C, T, R>` is invoked with the receiver and remaining arguments explicitly |
| Bound instance | No stored receiver; may point at a static adapter, whose origin is not part of the pointer contract | Stores the bound target and its receiver/environment, with dependencies retained |

The illustrative receiver marker `this` is a role in the checked signature,
not permission or calling convention; its exact syntax remains open. Receiver
access must match the method: e.g. `this C` versus `this write C`, or
`this ref<S>` versus `this write ref<S>`. A plain
`fnptr<T, void>` has an ordinary `T` argument and no bound state. A plain
`func<T, void>` may store either a static `M(T)` or bound `C.M(T)`; binding
removes the receiver from the call signature and retains its required
dependencies. A bound callable may use a thin trampoline, so boundness does
not mandate a separate thin/fat type cross-product. The bounded direction
does not define the full open/closed-by-thin/fat family cross-product, a fat
`fnptr`, or a mandatory separate no-capture family.

Compiler-supplied checked witnesses may expose a `func` or `fnptr` through a
callable protocol only when its complete signature, effects, access, lifetime,
and ABI satisfy that protocol; no allocated interface adapter, boxing, or
arbitrary structural single-abstract-method conversion is implied. Ordinary
nominal functors remain supported: a readonly `Invoke` contract is distinct
from a `mut Invoke` contract, and `mut` cannot silently satisfy readonly.
Stateful inline functors require a writable place or `write ref<T>`; a class
functor with `mut Invoke` requires a `write` handle. Contextual lambdas bind a
concrete expected `func`, `fnptr`, or functor contract while preserving its
representation; a capturing lambda is not silently converted to raw
`fnptr`. `[Callable]`-style marker naming and callable invocation syntax
remain open.

Named generic functors can express operations over scalar and registered SIMD
family members through definition-site symbolic constraints and
specialization. A closed scalar callable such as `func<float32, float32>` is
not a promise of vectorization. `x => x * x` may be contextually checked for
symbolic `T` only under the declared multiplication and Copy constraints;
captured scalar values require an explicit supported broadcast, not an
implicit vector conversion. Generic admissibility does not prove elementwise
semantics: effects, call counts, cross-lane behavior, inactive lanes, tails,
floating-point order, and contraction require a separate contract.
Contextually generic lambdas are the later shorthand for these same generic
functor contracts, not a new pack system.

Formatting handlers remain typed writer streams with declared captures, not
universal `ToString` or heterogeneous varargs; see §1.2. C variadics remain a
separate foreign-ABI/interop contract, not Terrum `params` or a callable pack.
The remaining design choices are existing-collection binding, exact
receiver/attribute/alias/lambda grammar, callable marker/invocation spelling,
generic constraints and specialization policy, elementwise kernel semantics,
and import/export ergonomics. None of these directions is compiler support;
no general pack, polymorphic callable erasure, or automatic vectorization of
arbitrary scalar callbacks is included.

## 2. Make representation and cost visible

### 2.1 Opaque layout and explicit ABI proposals

**Accepted direction:** layout/ABI promises need explicit checked contracts.
**Proposed default:** ordinary layout is opaque. **Open proposal:** a
transparent single-field wrapper could promise a validated representation, not
permission to erase validity, ownership, mutability, cleanup, niches, or ABI.
It must preserve carrier ABI and narrower payload validity/ownership; equal
bytes never permit arbitrary reinterpretation or automatically propagate every
niche optimization. Construction and imports validate the wrapper's narrower
invariant; layout equality never waives that check.
Sequential/explicit/union layouts are opt-in per ABI and constrain servicing;
interop separately declares calling convention, alignment, ownership, encoding,
callbacks, and exception containment. .NET distinguishes unmanaged from
blittable (`bool`/`char` differ) and native marshalling from managed layout
([interop guidance](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices#blittable-types)).
Rust [`repr(C)`/`repr(transparent)`](https://doc.rust-lang.org/reference/type-layout.html)
specify representation, not Terrum validity/ownership: `repr(C)` does not
recursively change nested layout or give a wrapper its field's call ABI;
transparent requires one nontrivial
field. Neither bypasses construction/import validation or allows blind
reinterpretation.

Request required storage capabilities (`[Sized]`, managed-storable, copyable)
separately from behavioral interfaces; sizeless values cannot silently become
fields or array elements.

### 2.2 Generic operations should not imply generic representation

**Refinement proposal.** Generic algorithms use declared capabilities without
boxing; constraints do not license representation erasure or value duplication.
Constraints, static witnesses, and representation families solve distinct
problems; avoid one universal mechanism.

Keep vector/tensor concepts separate:

- scalar arithmetic protocols;
- fixed-width SIMD family membership and its lane-mask association;
- hardware-scalable, potentially sizeless vectors and their target ABI;
- logical multidimensional shapes and their backing strides;
- domain vectors such as geometric points, whose operations need not be
  arithmetic on a scalar number.

SIMD comparisons return a declared mask; whole-vector equality may return
`bool`. Tails, inactive lanes, alignment, overlap, reduction order, CPU/OS
selection, scalable ABI, and suspension restrictions are correctness contracts.
Capabilities apply to the complete qualified form: `[Copy]` on a payload does
not make `own<T>` or a borrowed `ref<T>` copyable. Access only attenuates:
`mut Span<T>` permits descriptor rebinding but does not grant writable slots;
it may yield a read view, never a writable upgrade from one. A writable
projection requires existing write authority on the relevant backing.

### 2.3 Iteration and allocation are lifetime contracts

**Open library-contract proposal.** Do not infer that one `foreach`/`Range`
surface makes every source interchangeable: collections may yield copied
values, collection-tied loans, or iterator-state lending items. Specify
retention/invalidation, finite versus multipass behavior, effect reevaluation,
mutation, and early-exit cleanup. Go 1.23 added range-over-function partly
because `sync.Map.Range`, `flag.Visit`, and `filepath.Walk` exposed incompatible
callbacks. Push and pull each have stopping/cleanup tradeoffs
([Go range functions](https://go.dev/blog/range-functions)).

C++ `borrowed_range` permits iterators to outlive a temporary range object; it
does **not** keep the underlying owner alive or make iteration multipass
([C++ `borrowed_range`](https://eel.is/c++draft/range.range)). Terrum backing
and shape dependencies likewise do not decide cursor lifetime or repeatability.

```text
while (mut ref item = cursor.NextLoan()) {
    inspect(item)             // loan ends before advancing
    cursor.Advance()
}
```

Not proposed syntax: the item borrows cursor scratch and cannot be retained
across `Advance`, returned, or suspended without a separate contract. An owning
iterator can instead transfer each item once; a generic algorithm must not
silently copy a lending item.
Where the selected sequence witness determines one item relation, an
associated `Item<'a>` can express that `next(&'a mut self)` lends storage for
`'a`; while the loan is live, advancing again is unavailable. If one
implementation intentionally exposes multiple item relations, keep them
explicit rather than guessing one associated type.

Allocator behavior is observable too. C++ allocator-aware move construction
with an equal explicit allocator can be constant time; an unequal one can
require linear relocation. Move/copy/swap propagation are separate policies
([C++ container allocator requirements](https://eel.is/c++draft/container.alloc.reqmts)).
Specify allocator/backing ownership, relocation, and destination selection.
Do not hide reallocation or allocator substitution behind “move,” or invent a
universal default allocator.

Expose allocator choice where storage policy belongs to the caller; test
failure cleanup and publication.

### 2.4 Discards need one meaning

**Refinement proposal.** Choose a single rule for discards (`_`) across
deconstruction, assignment, pattern matching, and lambda parameters. It should
mean “intentionally do not bind/use this value,” not inconsistently mean
“fresh local” in one position and “wildcard” in another. Define binding versus
wildcard, RHS evaluation/effects, temporary and unique cleanup, and the vacancy
of failed conditional `out`; failure cleans partial owners and never creates
`default(T)`. Discarding a task result is not detaching or
supervising work. Tooling/diagnostics must report these meanings consistently.
Keep `_` distinct from an uninitialized destination or omitted optional field.
Audit prior examples before freezing a rule; conventions differ by context.

## 3. Make compile-time participation and code generation declared

### 3.1 One checked participation model, multiple capabilities

**Refinement proposal.** User-defined types should be able to participate in
const-evaluation, typed literals, checked operators, compound assignment, and
generic constraints through declarations that can be verified and published.
Avoid compiler magic keyed to names or a handful of intrinsic types, but do not
allow arbitrary user callbacks to execute inside the compiler without
determinism and resource bounds. `[Pure]` alone does not imply const-evaluability.
Registered literals/const-eval must be
target-format and culture-independent, exclude ambient path/I/O, enforce
resource budgets, preserve source error ranges, and validate through the normal
constructor or witness contract. A baked consumer cannot change retroactively
when its producer is replaced; require rebuild or reject mismatched
compiler/rule/module/value-ABI dependencies, not assume the same body remains
([.NET `const`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/const)).

The selected literal target may be written or established by an unambiguous
context; literal construction still has no fallback type. The model should
distinguish:

- **literal parsing/construction**: validate a token against its resolved
  contextual target or explicit type tag, with no fallback/intermediate type;
- **const evaluation**: a restricted interpreter evaluates approved pure,
  deterministic operations over validated constant values;
- **checked runtime operators**: declared behavior, effects, and failure policy
  apply in ordinary code;
- **compound assignment**: a single-place operation with defined evaluation
  count, aliasing, overflow, and whether it mutates in place or returns a value.

These can share capability declarations without sharing one execution model.
Const-eval must not bypass validity, execute I/O, or assume target behavior
([Rust const evaluation](https://doc.rust-lang.org/reference/const_eval.html)).
Compound assignment should not lower to an
unintended read/copy/write sequence for affine or large values. A declaration
that participates in checked `+` does not automatically claim support for
`+=`, saturating arithmetic, wrapping arithmetic, or constant evaluation.

C++ distinguishes `constexpr` functions that may run
at runtime from `consteval` immediate functions
([C++ constexpr](https://eel.is/c++draft/dcl.constexpr)). Rust procedural
macros are explicitly unhygienic, execute in the compiler process with file
and resource access, and can hang compilation
([Rust procedural macros](https://doc.rust-lang.org/reference/procedural-macros.html)).
That is a deliberate cost/expressiveness contrast, not a default Terrum model.
Swift library evolution likewise distinguishes resilient binary evolution
from frozen-layout optimization
([Swift library evolution](https://www.swift.org/blog/library-evolution/)):
a consumer-baked value must bind to compiler rules, producer, and value ABI
versions rather than assuming a replacement body will recompute it.

### 3.2 First-class handlers for target-directed construction

**Open proposal.** Interpolation, collection expressions, and future typed
literal builders need a principled handler/build protocol. Treat them as
explicit target-directed expansions that specify evaluation order, captured
values, ownership transfer, partial failure, cleanup, and the result's
representation. A handler can consume typed segments directly without
allocating a temporary string; a builder can accept elements once without
materializing an intermediate collection.

The language should require a registered handler contract, not select any
method with a convenient name. Preserve the existing interpolation direction:
evaluate every hole exactly once, left-to-right, into typed captures **before
draining any segment**. C#'s [handler protocol](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/interpolated-string-handler)
can skip later holes after refusal; do not silently adopt that behavior. If
lazy/conditional evaluation is useful, make it a separate construct with its
own effects and ordering contract. Clean partially accepted unique values on
refusal/failure, including remaining captured owners never accepted by the
handler, exactly once; borrowed captures stay valid through drain. A scalar
writer preflights room for the full encoding. Refusal stops later writes, not
pre-evaluated hole effects or prior stream output. Builders publish only after
success; refusal is not an implicit async continuation.

### 3.3 Make specialization a declaration, not body-divination

**Open proposal.** First-class specialization can improve generic hot paths and
enable target-specific implementations, but specialization must remain an
implementation choice with declared applicability and semantic obligations.
An explicit specialization may optimize a verified generic definition; it
must not silently redefine its source contract or make AOT depend on a JIT.
The generic implementation remains a valid fallback unless the declaration
explicitly requires a target capability. This is a machine-code selection, not
a semantic overload or a way to select a different algorithm contract.

Validate that the specialized implementation preserves observable behavior,
including checked failure/cleanup, effects, ownership, aliasing, access,
published contracts, and floating-point guarantees (including FMA/reordering
policy). Rust still treats specialization as unstable
([Rust Unstable Book](https://doc.rust-lang.org/unstable-book/language-features/specialization.html)),
which reinforces the coherence and contract burden rather than supplying a
design to copy. A selected type or current body cannot legalize an invalid
source escape or strengthen a public guarantee. Define code-size, versioning,
and fallback rules before syntax; start with explicit target/library
registrations. This is not a ban on semantic algorithm variants: e.g. strict
and fast reductions may be distinct named APIs with different rounding,
reordering, and effect contracts, not same-contract specialization.

Keep generic semantics separate from code-generation strategy. Go's generic
design addressed real costs in duplicated algorithms, reflection, interfaces,
and generators; its FAQ describes shape-based code sharing and the difficulty
of generic methods whose implementations could arise from unknown interface
implementers ([Go generics rationale](https://go.dev/blog/why-generics);
[Go generics FAQ](https://go.dev/doc/faq)).
C# shares runtime code for reference-type instantiations but specializes
value-type instantiations
([C# generics at run time](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generics-in-the-run-time)).
Terrum's witness/representation contracts do not dictate monomorphization,
shape sharing, or universal code sharing. Require viable AOT/JIT implementations
with equivalent semantics; measure code size and workload before promising a
strategy or calling any strategy free.

## 4. Make failures, functions, and async lifetimes explicit

### 4.1 Distinguish expected outcomes from exceptional failures

**Accepted direction.** Expected alternatives use conditional outputs or domain
statuses; exceptional failures propagate; catch-based recovery is unsafe and
does not prove invariants survived. `require` is checked in every build.
**Refinement proposal:** distinguish diagnostics, expected results, operational
failures, invariant defects, and host/process failures without conflating cause,
recoverability, or cleanup. `ArgumentException` deriving from .NET
`SystemException` illustrates why exception inheritance is not that taxonomy
([API reference](https://learn.microsoft.com/en-us/dotnet/api/system.systemexception?view=net-10.0)).

No classification makes recovery safe: even environmental exceptions need
separate unsafe authorization and proof of object integrity. A `require` failure
may be checked/recoverable; cancellation is a control/lifetime signal, not proof
that a foreign operation stopped using a borrow. No-throw, no-return, cleanup,
and expected-result contracts remain independent.

Propagation syntax has real demand but no consensus design. Go's 2025 review
reported boilerplate and hundreds of proposals; `check`/`handle`, `try`, and
`?` did not reach agreement, and syntax work paused -- not an adoption of `?`
([Go retrospective](https://go.dev/blog/error-syntax)).

**Open typed-failure proposal:** retain domain failure types where useful, with
erased/exceptional propagation as a boundary option. Swift 6 implemented typed
throws for cases such as `Result`/`Task`, but full inference did not ship in
6.0; Kotlin remains unchecked-by-default. Together they show the information
benefit versus propagation/versioning cost, not a universal answer
([Swift SE-0413](https://raw.githubusercontent.com/swiftlang/swift-evolution/main/proposals/0413-typed-throws.md);
[Kotlin exceptions](https://kotlinlang.org/docs/exceptions.html)).
Terrum must choose closed, evolvable/versioned, or erased public failure sets;
typed propagation does not make `catch` safe. Consumers bind to
published/versioned contracts, independent of private inference.

Use one failure/cleanup vocabulary for error returns, conditional results,
exceptions, callbacks, and builder refusal: specify initialized outputs,
current owner, and cleanup before propagation. Safe source contracts need
compiler evidence; foreign promises remain a separate trust boundary.

Illustrative cleanup expansion, not a proposed `?` operator or an accepted
`Result` API:

```text
own File file = open(path)
try {
    match parseConfig(ref file) {
        success(config) -> publish(config)
        failure(parseError) -> return domainFailure(parseError)
    }
} finally {
    releaseNoThrow(move file)
}
```

The parser borrows the file without retaining it; the expansion retires its
owner exactly once through a `NoThrow` release. I/O failure propagates, while
invalid configuration is a result. Fallible `Finish`/`Commit` runs before
retirement and needs an explicit primary/secondary failure policy. For contrast,
Java try-with-resources permits throwing close operations and uses reverse close order and suppressed
exceptions to preserve the body failure
([Java resource handling](https://docs.oracle.com/javase/tutorial/essential/exceptions/tryResourceClose.html)).
Terrum need not copy Java, but a naive `finally` must not mask the primary error.

JEP 421 is deprecation-for-removal, not completed removal of finalization; it
cites unbounded latency, resurrection, unspecified thread/order,
partial-construction hazards, and leaks
([JEP 421](https://openjdk.org/jeps/421)). Terrum's deterministic unique
cleanup, precise tracing, and complete publication already address much.
Preserve those contracts: deterministic release stays `NoThrow`; fallible
`Finish`/`Commit` is separate and defines primary/secondary failure reporting.
Do not add fallible destructors or broaden recovery implicitly.

### 4.2 Prefer single-target callables; make event aggregation explicit

**Accepted single-target direction with open capture refinements.** `func` and
`fnptr` have one target and an explicit closed signature; multicast is not an
ordinary callable behavior. `func` capture follows checked ownership and
borrow/lifetime rules; copying a function value must not silently duplicate a
unique capture. Repeated calls are valid only while dependencies remain valid;
consuming a capture may require explicit one-shot semantics or reconstruction,
which remains open. Multicast is useful for events but should not be an
accidental universal delegate semantic: a separate event/subscription
abstraction would need order, exception aggregation, reentrancy,
mutation-during-dispatch, and unsubscribe/disposal rules.

Go 1.22 shipped fresh loop bindings per iteration after production capture bugs;
the version gate exposed analyzer false positives/negatives
([preview](https://go.dev/blog/loopvar-preview); [release](https://go.dev/doc/go1.22)).
Fresh binding is not deep copy: choose loan/copy/move and preserve it by module version.

Kotlin KEEP-0139 links callback cardinality/returns-implies contracts to
smart-cast and definite-initialization issues. Its prototype trusted
declarations and left formal verification out of scope; the authoring DSL's
experimental status differs from stdlib metadata consumption
([KEEP-0139](https://github.com/Kotlin/KEEP/blob/main/proposals/KEEP-0139-kotlin-contracts.md)).
For Terrum, prove `does not escape`, `does not suspend`, and `exactly once on
normal return` separately from thread affinity, reentrancy, and purity. A
`withResource`/`Visit` can lend storage or initialize an output without a
built-in-only case. Exceptional paths still need cleanup; foreign promises
remain a trust/unsafe boundary.

```text
withReadLease(owner, callback) // read loan; noescape, no-suspend, once on normal return
callback failure -> release lease, then propagate
```

Not syntax: cardinality, noescape, no-suspend, and `[Pure]` are distinct.

.NET multicast runs in order, stops on throw, returns the last result; select
it only for an explicit event adapter ([delegates](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/delegates/using-delegates)).
The bounded callable families need no pack ABI; keep each signature closed and
multicast out of ordinary calls.

### 4.3 Never let async work disappear

**Accepted direction plus refinement.** Return task-like completion, including
no-result `Task<void>`; ordinary APIs should not use `async void`. Detached work
needs explicit supervision, error sink, cancellation/lifetime, and shutdown/
join policy -- not an unobserved `void` task.

In .NET, `async void` posts exceptions to a captured context/thread pool instead
of faulting `Task`; use an explicit callback adapter
([`AsyncVoidMethodBuilder`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncVoidMethodBuilder.cs);
[`AsyncTaskMethodBuilder`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilder.cs)).

Tasks do not extend borrows; cancellation is not proof that external work
stopped. Specify capture cleanup and keep `Task<void>` distinct from
`Task<T>` result erasure. Scheduler, affinity, start, cancellation remain open.

Prefer **structured lifetime**, not an actor mandate. Swift structured
concurrency and Kotlin scopes await children; cancellation is
cooperative and Kotlin says not to swallow `CancellationException`
([Swift SE-0304](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0304-structured-concurrency.md);
[Kotlin cancellation](https://kotlinlang.org/docs/coroutines-cancellation.html)).
Swift `Sendable` separately checks transfer/sharing and closure captures;
`[Copy]` alone is not sendability
([SE-0302](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0302-concurrent-value-and-concurrent-closures.md)).
Keep joining, cancellation, transfer safety, and failure reporting distinct;
detached work also needs a sink and shutdown policy.

Actors prevent unsynchronized access, not invariant changes across suspension.
Swift accepts reentrancy for progress instead of holding an actor busy
([Swift SE-0306](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0306-actors.md)).
Another operation may change state while the first awaits:

```text
// Conceptual revalidation, not Terrum syntax.
request = ownedSnapshot(state.data)
observedVersion = state.version
release any state borrow or guard
response = await fetchRemote(request)
if state.version != observedVersion:
    reject or recompute only if the remote operation is safe to repeat
revalidate current invariant before commit
commit(response)
```

Revalidate before commit; version checks cannot undo remote effects, so retry
requires idempotency or compensation. Synchronous callbacks can also reenter.
Keep distinct data races, deadlock, and logical reentrancy; solving one does
not prove the others absent. Cancellation cannot revoke a foreign borrow before
the operation actually stops.

Go `context` propagates cancellation and requires cancel functions to release
resources; `AfterFunc.stop` does not wait for a running callback. Propagation
is not structured joining
([Go `context`](https://pkg.go.dev/context);
[`AfterFunc`](https://pkg.go.dev/context#AfterFunc)).

C++ execution's working draft makes connect/start, operation-state lifetime,
scheduler/allocator queries, and value/error/stopped completion explicit.
P2300 cites `std::async`/`future` composition limits; draft status implies
neither broad vendor availability nor race freedom/zero cost. It is a tradeoff
reference, not a Terrum API
([P2300R10](https://www.open-std.org/jtc1/sc22/wg21/docs/papers/2024/p2300r10.html);
[C++ execution](https://eel.is/c++draft/exec)). A smaller task/scope model
trades generality for simplicity.

## Coverage and priority

| Concern raised | Shared response in this review | Status |
|---|---|---|
| Mutable-array variance and root arrays | Writable storage is invariant; any readonly projection needs checked representation, access, and lifetime rules | Accepted invariant; projection/root-array policy open |
| Universal object defaults | No universal object methods or automatic capabilities; opt-in equality/hash/format and separate diagnostic identity | Accepted direction |
| Nominal witnesses and capability predicates | Checked `IFoo` witnesses remain distinct from `[Copy]`, storage, and family capabilities | Accepted baseline |
| Generic/erased APIs, conditional witnesses, associated results, erasure, inference, overloads | Accepted policy; exact grammar, representations, dispatch and general compiler support are staged | Accepted direction / scheduled |
| Generic array/enum capabilities | Preserve independent capability predicates, unnamed enum values, open-by-default evolution and explicit case-set/layout contracts | Accepted direction / representation details open |
| Layout and wrappers | Opaque by default; transparent/native layouts preserve ABI, validity, and ownership | Accepted direction / open proposal |
| Sealed concrete types | `sealed` controls inheritance; type/API evolution and case-set/layout promises are separate | Accepted direction |
| Collection-based `params` | Ordinary collection construction and per-element copy/move; existing-collection versus expansion binding remains open | Agreed direction / binding rule open |
| `func`, `fnptr`, tuples, and packs | Bounded closed callable/tuple direction, transparent aliases, and checked callable witnesses; exact grammar, ABI annotations and generic callable contracts remain scheduled/open; no user-defined pack expansion | Accepted direction / scheduled details |
| Tensor, multidimensional, SoA/AoS | Separate shape, strides, storage, and truthful projections | Proposal |
| Discards | One evaluation, vacancy, and cleanup rule across contexts | Refinement |
| Literals, consteval, operators, compound assignment | Bounded, hygienic, versioned constant evaluation distinct from runtime witnesses | Open |
| Handlers and builders | Registered expansions; eager left-to-right holes, cleanup, and publish-on-success | Open |
| Specialization | Same-contract optimization with fallback; semantic variants remain named APIs | Open |
| Errors and propagation | Separate expected/domain results, exceptions, invariant defects, host failures, and cleanup | Accepted constraints / open refinement |
| Callables, async, cancellation, structured concurrency | Completion, ownership, supervision, transfer, and revalidation remain explicit | Refinement / open |
| Iteration, allocation, loop capture, callbacks | Lending/invalidation and allocator costs; capture/callback facts need contracts | Open library contracts |
| Generic implementation strategy | AOT/JIT parity; sharing versus specialization is a backend choice | Open |

Validate accepted contracts across AOT/JIT/module replacement while scheduling
each mechanism in the existing compiler stages; do not defer already accepted
policies to an unspecified future. Type-pack declarations, arbitrary macro
systems and unbounded specialization remain separate, later questions.

These choices trade implicit convenience for stronger contracts. Keep the cost
manageable with checked derivation of ordinary equality/formatting witnesses,
contextual inference only where the language specifies an unambiguous source of
target type, and useful library defaults -- not universal inherited behavior.
Static witnesses favor optimization but can grow code; explicit erased
representations add dispatch and storage contracts. Handler registration is
narrower than arbitrary macros, while packs and overlapping specialization add
inference and coherence complexity that ordinary typed builders do not need.

The deeper priority is safe composition before a full higher-kinded type
system, arbitrary macro facility, or general effect language. A single failing
resource/API path that demonstrates typed domain propagation, cleanup,
callback lifetime, and useful diagnostics is more informative than a larger
syntax inventory. Any compiler extension also needs source-mapped diagnostics
and explicit compile-time resource budgets; this review makes no unmeasured
zero-cost or performance claim.

## Illustrative contract sketches (not syntax)

These contract expansions are **pseudocode, not grammar or promised APIs**:

**Constraint bridging.**

```text
Forward<T, E>(source: read loan T)
    requires T : IContiguousSequence<E>
    returns source.AsSpan()
    receiver dependency by default; narrower backing-only dependency
    only with a published conditional proof

typed argument -> infer T; constraint alone does not
authorized producer/version -> resolve witness, then applicable overload
resolved target -> construct contextual literal; underconstrained F(1) diagnoses
open x as hidden S for one generic call
open y as hidden T; no proof that S = T or that a dependent result may escape
```

Associated outputs depend on the selected witness; family/interface proof is
separate from member-lifetime refinement. Opening a type for one call never
re-roots or erases its original ownership/backing dependency.

**Target-directed construction.**

```text
value: int32 = 42
own<val<Text>> result = $"item={value}"

resolve own<val<Text>>'s builder
evaluate/capture holes once, left-to-right
drain typed literal/value segments into private builder state
validate/freeze -> publish result
failure -> clean private state and all still-owned captures exactly once
```

Clean every unconsumed owner; loans stay valid through drain. Literals use
their resolved target/tag, not a rounded intermediate; registration alone
does not make operations const-evaluable.

**Shape versus storage.**

```text
shape = (m, n); elementStrides = (1, m)
element(i, j) = backing[base + i + m*j]
SoA field projection: points.x[i] = xBuffer[i]
AoS record projection: points[i] = recordBuffer[i]
```

Validate dimensions, signed strides, element count, and reachable backing
extrema before exposing a nonempty view. Empty shapes skip `length - 1`
arithmetic/dereference but retain backing/metadata dependencies. Check indexes;
SoA `xBuffer` may lend an `x` span, not `Span<Point>`.

**Heterogeneous forwarding.**

```text
signature pack = [(read loan A), (consume B), (value C)]
call arguments = [ref a, move b, c]
requires complete-form C : [Copy]
expand in source order, evaluating each argument once
invoke the checked signature without boxing or a universal tuple layout
```

On failure clean only owners still held; consume is transfer, not copy. Loans
cannot outlive the call/suspension; effects, inference, and ABI are per-element.

**Supervised start.** Establish ownership before work runs.

```text
factory = operation factory with explicit transferred/borrowed captures
completion: Task<void> = scope.Start(factory)
await completion
scope exit: join any outstanding operation before releasing its captures
```

`Start` is hypothetical: scope accepts before invoking; rejection cleans
transferred captures. Exceptional exit still joins/cleans; cancellation does
not discharge that obligation. Detached work needs a supervisor/error sink.

Spelling and start policy remain open; executable claims require
producer/witness, lifetime, and AOT/JIT/module checks.

## Evidence and limits

Specifications establish behavior; proposal motivations and issue references
establish concrete design problems, not popularity. The Go retrospectives and
Swift typed-throws proposal provide explicit recurring-request evidence.
Historical proposal limitations are not claims about every current compiler.
These comparisons inform Terrum recommendations, not automatic adoption or
measured performance claims. [Source notes](references.md) cover the existing
CoreLib and ABI research.
