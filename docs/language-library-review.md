# Language and library improvement review

**Design review; §§1.1–1.3, the bounded collection-based `params`, callable,
tuple, alias, and functor directions in §1.4, the representation and safety
directions in §§2.1–2.4, and the compile-time/handler/specialization directions
in §§3.1–3.3, plus the requirement/failure and single-target callable
directions in §§4.1–4.2 and the no-orphaned-async-work direction in §4.3 are
accepted.** The final representation direction accepts parameter-only
`implicit ref<T>` borrowing, including by-value overload preference. It
selects no adaptive result/view family and does not change ordinary receiver
semantics. A future type-level opt-in is only a deferred possibility; no core
feature, name or semantics are selected. Existing APIs are unchanged unless
they explicitly adopt the parameter contract. Task start/ownership mechanics
and scheduler policy remain open.
Unresolved grammar and other proposals add no syntax/API promise
or compiler authorization. The review draws on C#/.NET, Rust, Go, Swift, Kotlin, C++
and Java experience without treating any one ecosystem as a template.
[Language](language.md), [lifetimes](lifetimes.md), [type families](type-families.md),
[conversions](conversions.md), and [libraries](libraries.md) remain authoritative;
status follows [implementation](implementation.md) and [roadmap](roadmap.md).

The follow-up [call/output/contract review](language-contract-review.md) records
the clarified equivalence of implicit and explicit readonly references and
proposes precise evaluation, publication, completion, and proof rules. The
separate [memory-model proposal](memory-model.md) covers checked sharing and
portable versus target-specific memory behavior. Both are for review; their
new mechanisms are not approved language features or implementation work.

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
The current bounded implementation also has a semantic-v25 `MutReceiver`
contract for persisted, non-exported `mut` methods on non-owning compact
structs. Source visibility may be public or private; named mutable locals and
forwarding through an existing `this` are supported receiver places. Public
source visibility does not expose a public mutating-receiver ABI.
It includes compilation-local constrained generic specialization, but not a
public managed ABI, general interfaces/witnesses, array/view libraries, async,
or the proposed libraries. The syntax-15 numeric front end rejects width
suffixes with `TRM0006`, diagnoses a `0x`/`0X` prefix without an ASCII
hexadecimal digit as `TRM0007`, no longer parses prefix casts, and supports
contextual decimal and hexadecimal integer literals plus checked type-call
conversions for `int32`, `uint32`, `int64`, `uint64` and `char16`. The
scanner-4 record shape is unchanged and retains recognized suffix tags only
on diagnostic-bearing tokens. Floating/custom literal parsing and
`[ConstEval]` remain outside the executable profile. Examples in this review
and `.api.terrum` files remain design material unless specifically identified
as implemented.

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
arbitrary writable `ref<T>`. The validated semantic-v24 batch implements this
access distinction for source classes and `ManagedNode`: `mut class`
declarations and writable `mut` class
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

### 2.1 Opaque layout and explicit ABI contracts

**Accepted direction:** ordinary layout is opaque, and layout/ABI promises
need explicit checked contracts. Sequential, Union and Transparent are
distinct opt-in layout forms. Sequential field positions and padding are
computed from field order, size and alignment; arbitrary user-specified field
offsets are not supported. These contracts constrain servicing and do not
follow from a type's current bytes.

**Accepted transparent-wrapper direction:** an eligible simple wrapper stays
nominal for Terrum type checking but is carrier-equivalent in the declared ABI.
That is stronger than equal size or field layout: validation must cover
argument and return classification, size/alignment, ownership, validity,
cleanup and any niche representation. It does not permit blind
reinterpretation, erase the wrapper's invariant, or create an implicit
Terrum source conversion. Construction and imports still validate the
wrapper's narrower contract. `[Layout(Transparent)]` illustrates how this
could fit the existing layout-attribute convention; only the Transparent
concept is accepted, not that exact spelling or its eligible-field grammar.
The current binder rejects struct attributes and field attributes with
`TRM3001`; it neither silently honors nor ignores a transparent/layout
attribute. Its `ValueLayout` is the bounded, naturally aligned 1–16-byte
aggregate profile, not transparent-wrapper support. The accepted opaque default
does not retroactively make current module layouts resilient; those contracts
remain exact.

Rust [`repr(C)`/`repr(transparent)`](https://doc.rust-lang.org/reference/type-layout.html)
specify representation, not Terrum validity or ownership. `repr(C)` does not
recursively change nested layout, while transparent representation carries a
field's ABI only under its own checked rules. Neither precedent bypasses
Terrum construction/import validation or allows arbitrary reinterpretation.
For .NET comparisons, distinguish runtime marshalling from
[`DisableRuntimeMarshalling`](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/disabled-marshalling):
supported unmanaged types without auto-layout fields are treated as blittable
on that path.
Terrum has no built-in marshalling: source or generated tooling owns
conversions and wrappers. The compiler/runtime provide only the checked
representation, ABI and lifetime/ownership prerequisites needed to expose safe
APIs over imported C; they do not marshal values.

Request required storage capabilities (`[Sized]`, managed-storable, copyable)
separately from behavioral interfaces; sizeless values cannot silently become
fields or array elements.

### 2.2 Keep SIMD ABI primitives distinct from tensor views

**Accepted direction:** generic algorithms use declared capabilities without
boxing; constraints do not license representation erasure or value duplication.
Constraints, static witnesses and representation families solve distinct
problems. SIMD and tensor abstractions are related library tools, not one
representation family.

Keep vector/tensor concepts separate:

- scalar arithmetic protocols;
- fixed-width SIMD family membership, lane-mask association and ABI shape;
- hardware-scalable, potentially sizeless vectors and their target ABI;
- logical multidimensional tensor views, their shape metadata and backing
  strides, analogous to a higher-level `Span<T>` over a one-dimensional range;
- domain vectors such as geometric points, whose operations need not be
  arithmetic on a scalar number.

SIMD types describe register/vector values and their ABI; tensor spans describe
logical indexing over storage and may use shape metadata, arbitrary strides,
and checked projections. A tensor is not a SIMD value, and flattening it does
not follow merely from matching element counts. SIMD comparisons return a
declared mask; whole-vector equality may return `bool`. Tails, inactive lanes,
alignment, overlap, reduction order, CPU/OS selection, scalable ABI, and
suspension restrictions are correctness contracts. Capabilities apply to the
complete qualified form: `[Copy]` on a payload does not make `own<T>` or a
borrowed `ref<T>` copyable. Access only attenuates: `mut Span<T>` permits
descriptor rebinding but does not grant writable slots; it may yield a read
view, never a writable upgrade from one. A writable projection requires
existing write authority on the relevant backing.

Handlers, functional interfaces and more broadly composable kernels remain
important later standard-library work. They are not prerequisites for, or part
of, settling the present SIMD-versus-tensor representation distinction.

### 2.3 Iteration, allocation and return representation are lifetime contracts

**Accepted safety direction:** do not infer that one `foreach`/`Range` surface
makes every source interchangeable. Collections may yield copied values,
collection-tied loans, or iterator-state lending items. Contracts must state
retention/invalidation, finite versus multipass behavior, effect reevaluation,
mutation, allocation/relocation and early-exit cleanup. Go 1.23 added
range-over-function partly because `sync.Map.Range`, `flag.Visit`, and
`filepath.Walk` exposed incompatible callbacks. Push and pull each have
stopping/cleanup tradeoffs
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
associated `Item<'a>` is a candidate for expressing that `next(&'a mut self)`
lends storage for `'a`; while the loan is live, advancing again is unavailable.
The selected relation must carry the complete qualified item type, ownership,
alias and lifetime dependencies. If one implementation intentionally exposes
multiple item relations, keep them explicit rather than guessing one associated
type. Selection and ABI remain open library design, not an inferred property
of a method body.

Allocator behavior is observable too. C++ allocator-aware move construction
with an equal explicit allocator can be constant time; an unequal one can
require linear relocation. Move/copy/swap propagation are separate policies
([C++ container allocator requirements](https://eel.is/c++draft/container.alloc.reqmts)).
Specify allocator/backing ownership, relocation, and destination selection.
Do not hide reallocation or allocator substitution behind “move,” or invent a
universal default allocator.

Expose allocator choice where storage policy belongs to the caller; test
failure cleanup and publication.

Returning `T` preserves its declared semantics even when the physical ABI uses
indirect result storage or copy elision. That lowering does not itself
introduce a loan of the original value's storage. Handles and loans already
contained in the complete `T` retain their declared dependencies; a borrowed
result requires explicit lifetime, ownership and alias relationships.

### 2.4 Context-specific underscore roles

**Accepted semantic direction; grammar and support remain open.** `_` marks an
intentionally unnamed slot, but the slot's role is determined by context, not
by one universal "ignore this" operation:

- In a discard-value context, evaluate the expression once, preserve its
  effects, and apply ordinary value ownership and cleanup.
- In a wildcard pattern, match without binding; the pattern does not itself
  consume or drop the matched source value.
- In a lambda parameter position, omit the local binding while retaining
  ordinary argument evaluation and parameter-mode rules.
- In a generic type-argument position, `_` is an inference hole filled only
  when the existing constraints and context determine one unique type. It has
  no fallback/default, wildcard, or existential meaning; unresolved holes are
  errors and cannot escape into exported contracts.

Where assignment or deconstruction permits a discard destination, its value
still follows the discard-value evaluation and cleanup rule. A failed
conditional initialization remains vacant; it is not a discard or a
`default(T)`. Discarding a task result does not detach or supervise its work.
The spelling does not reserve every underscore-prefixed identifier, and the
lexer may continue to recognize `_` as an identifier outside a grammar-defined
context. Exact supported contexts and spellings must be specified separately.
The current binder has no special discard handling: `_` follows ordinary
identifier/assignment binding. Expression statements and unused-local cleanup
do not implement the full discard contract; wildcard-pattern and generic-hole
forms are also unsupported.

## 3. Make compile-time participation and code generation declared

### 3.1 Checked type-call conversions, registered literals and const evaluation

**Accepted direction.** `T(expression)` is the single explicit conversion
spelling. Remove both prefix casts and numeric width suffixes; bare literals
remain contextual and have no fallback type. The unquoted numeric token keeps
the language's radix, exponent and signed-minimum rules; digit-separator
grammar/support is separate scheduled literal-frontend work. Context or an
explicit target selects a registered parser that consumes
the original token spelling and constructs the value directly in the target
representation:

```terrum
int32 count = 1234;
int32 same = int32(1234);
uint64 mask = uint64(0xFFFFFFFF);
float32 fraction = float32(0.1);
float64 converted = float64(count);
```

The syntax-15 executable profile supports contextual decimal and hexadecimal
integer literals and the supported built-in integer/code-unit conversion types;
the floating-point examples above illustrate target syntax only.

`T(untypedLiteral)` invokes the registered literal parser directly: it does
not first manufacture an `int32` or `float64`. `T(typedExpression)` is an
explicit checked conversion and requires a built-in rule or declared
conversion witness/operator. It never implies boxing, `write` authority,
pinning or reinterpretation. `new T(arguments)` remains nominal construction;
it is not the conversion form, and there is no reason to add `new int32(value)`.
The implemented built-in type-call cases preserve existing source-function
and local shadowing/call precedence. This bounded support does not implement
custom literal registration or arbitrary user-defined conversion lookup.

For example, `Port` is a nominal wrapper around `uint16`:

```terrum
public struct Port : IParsable<Port>
{
    [Literal(LiteralKind.Integer), ConstEval]
    public static bool TryParse(TextSpan text, init ref<Port> value);

    public static explicit operator Port(uint16 value);

    public Port(uint16 raw);
}

Port parsed = 1234;           // Registered parser; no intermediate integer type.
Port parsedExplicitly = Port(1234);
Port converted = Port(raw);   // Explicit conversion from a typed uint16.
Port constructed = new Port(raw);
```

`[Literal(...)]` registers the ordinary text parser for a source token class;
the method's name is irrelevant to registration. The `IParsable<TSelf>`
contract is reused rather than introducing an `IntegerLiteral` carrier API.
Registration may select the ordinary parser directly or a thin adapter for
source-literal grammar over the same parsing implementation.
Literal parsing consumes the complete borrowed `TextSpan`, must produce a
valid representable constant, and on `false` leaves the initialization
destination vacant. The registered parser must be `[ConstEval]`; literal
construction never falls back to runtime parsing. The same parser remains an
ordinary callable at runtime. Borrowing immutable literal text does not require
allocating a `String`. Invalid spelling or target-range failure diagnoses at
compile time. Registration does not add suffix syntax, a custom lexer, or a
constructor/conversion search to guess a custom literal's type.

String text is likewise parsed by a registered target. `utf8z("Terrum")` and
`utf16z("Terrum")` illustrate call-form targets, not lexer prefixes; those
names are illustrative unless corresponding target types are defined. The
target validates encoding, termination and embedded-zero policy. A zero-ended
representation does not itself confer pointer lifetime or storage ownership.
Escape decoding preserves the string payload. An ordinary parser call remains
available when runtime parsing is intended.

`[ConstEval]` marks a declaration that may be executed by the bounded,
deterministic compile-time evaluator; a use-site requirement is separate:

```terrum
[ConstEval]
public static int32 Twice(int32 value) => value + value;

const int32 baked = Twice(21); // The declaration requires a constant value.
int32 required = const Twice(21); // This expression requires one; `required` is not const.
int32 runtime = Twice(21); // Ordinary runtime call; optimization remains independent.
```

The `const` prefix requires compile-time evaluation; `const(expr)` is just that
prefix followed by grouping. There is no `runtime(...)` opt-out, no separate
consteval-only function category, and no extra compiler-phase branch for
immediate functions. Every literal takes its registered constant-parsing path,
including literals in otherwise ordinary local initializers. A constant
requirement does not change type checking or overload resolution, and does not
prevent an optimizer from folding an ordinary call when semantics permit.
`MyType value = 1 + 2` is an ordinary expression: construct each literal under
its own context, then bind `+`; do not stringify or fold the whole expression
and pass it to a literal parser.

The evaluator preserves target numeric semantics and signed-minimum handling;
it is deterministic and resource-bounded, has no ambient I/O, and permits only
checked local mutation and bounded temporaries. Published constants retain
valid ownership/representation and record the producer, evaluator rule and
value-ABI dependencies needed to keep baked results stable across replacement.
`[Pure]` alone does not grant compile-time execution. Checked `operator +` can
be independently `[ConstEval]`; it does not imply compound assignment,
wrapping or saturating behavior. Built-in compound operations are
language-provided. User compound assignment is an explicit contract:

```terrum
public static void operator +=(write ref<T> target, T value);
```

The place is evaluated once, then the right operand once. The operation does
not imply a hidden read/copy/write sequence, especially for unique or large
values. A user-defined `+` does not automatically provide `+=`; wrapping and
saturating operators/conversions are separate declared contracts.

### 3.2 Registered builders and parameter-directed handlers

**Accepted direction.** Collection expressions and text/interpolation
construction use an explicitly registered target handler type, not arbitrary
same-named methods:

```terrum
[CollectionBuilder(typeof(ListBuilder<>))]
public class List<T> { ... }

[TextBuilder(typeof(TextBuilder))]
public class Text { ... }
```

The associated library types and complete APIs remain design work. Collection
elements follow ordinary checked construction, per-element copy/move, and
normal slot/lifetime rules; a collection parameter is not a new ref-parameter
mode. Explicit reference element types remain possible when storage and
lifetime permit. Existing-collection versus expanded-argument overload binding
still needs an exact rule.

Parameter-directed streaming handlers declare their handler type and the
parameter shape they accept. Holes are evaluated once, left-to-right, into
typed captures before any segment drains. Ownership transfer, refusal,
partial-failure cleanup, publication and borrowed-capture lifetime remain
explicit. Refusal does not skip already evaluated holes or roll back prior
output. This preserves ordinary evaluation even when a handler avoids a
temporary string or collection.

An expression handler may consume a declared typed operation graph; for
example, `Tensor.Apply(destination, i => x[i] * y[i] + z[i])` is not an
ordinary callback whose body the compiler guesses how to vectorize. Captures,
backing and aliasing are explicit, and each handler declares its supported
operation set. It cannot silently introduce FMA, reassociation, reordering,
changed call/effect counts or different checked-failure behavior. Unsupported
expressions diagnose or use a separately declared fallback. A target may treat
the same capture as data or as code, but not by invoking an arbitrary host
compiler callback or unbounded plugin by default. Detailed standard-library
handler contracts remain scheduled work.

### 3.3 Explicit, contract-preserving specialization

**Accepted direction.** A specialization names the exact closed generic
declaration and target requirements; it is not fuzzy string lookup, body
divination or a new source overload:

```terrum
[Specialize(Scale<float32>, Requires = CpuFeature.Avx2, Priority = 100)]
private static float32 ScaleAvx2(float32 value);
```

The generic body remains independently valid and source binding stays attached
to its original closed generic declaration. Initial selectors are exact type
arguments plus declared target capabilities. Explicit priority orders
applicable candidates; overlapping ties reject rather than depending on
import order. AOT publishes a portable baseline and eligible variants with
CPU/OS dispatch; a declaration that explicitly requires a target may omit the
fallback, but cannot secretly require a JIT.

Every specialization preserves effects, access, lifetime, ownership, cleanup
and floating-point contracts, including no implicit FMA. Strict and fast
algorithms with different semantics are distinct APIs, not interchangeable
specializations. Compiler checks can validate formal contracts but cannot
prove arbitrary handwritten algorithms equivalent; implementations need
differential tests or proof obligations against the generic definition/same
operation graph. Generic sharing and monomorphization remain separate backend
choices.

## 4. Make failures, functions, and async lifetimes explicit

### 4.1 Distinguish expected outcomes from exceptional failures

**Accepted direction.** Expected alternatives use conditional initialization
outputs or explicitly checked domain statuses; exceptional failures propagate.
There is no mandatory `Option`/`Result` wrapper or typed-throws failure-set
system. Use a strong domain value or protocol object, such as an HTTP response,
when the failure/result itself is substantive.
Keep expected external failures, invalid user input, programmer defects, and
environmental causes distinguishable from the separate question of whether
recovery is safe. Expected operational failures normally use `Try`/status
contracts; retry additionally needs idempotence, backing and cleanup rules.
`TryParse` is a fundamental API. An assertion-style caller may deliberately
lift its declared success output with `int32 value = require
int32.TryParse(text);`, or select `require<FormatException>` for that false
result. The `require` failure type applies only when its predicate returns
false; exceptions thrown while evaluating the predicate propagate unchanged.
Runtime declaration pre/postcondition failures use the same false-only
selection rule; static forms diagnose and never select or throw a runtime
exception.

Failure publication comes from checked declaration metadata, not method names.
A boolean success contract can initialize a vacant destination only on success;
a rare enum status uses an explicit declared publication predicate, never
zero/member-name guessing. The caller can explicitly match a status to retain
domain detail. Initially the `require` expression lifts one declared
success-conditioned output, invokes the operation once, publishes only on
success and cleans partial state under the ordinary contract before raising on
false. It never reads an uninitialized or fabricated `default(T)` value.
Multiple conditional outputs remain explicit; declaration-driven some/none or
status-pattern syntax and `init ref` invocation grammar remain scheduled, not
implied by this lifting form.

Requirement contracts are determined by placement. A declaration `require`
publishes a precondition; declaration `ensure` publishes a postcondition over
normal executable returns. These conditions are executable unless proven
redundant. Stronger preconditions and weaker postconditions are compatibility
breaks. Published clauses use a checked, side-effect-free contract language.
A body `require` retains general executable predicates and is an ordinary
point check, not an inferred public precondition. A body `static require`
proves a condition at that point, while outer-body `static ensure` proves a
postcondition on every normal return; these body-only guarantees are private,
not inferred into the public declaration.

Static requirements are compile-time-only in every build: the proof language
is side-effect-free, and a false or unproven required proof is an error, never a
runtime fallback or unchecked optimizer assumption. Runtime values may satisfy
symbolic proofs; this is distinct from constant evaluation to a value.
`ensure return >= 0` uses `return` as the contextual value of the actual
function result. It does not introduce a result binder/lambda or reevaluate a
return expression; a `void` function has no result-valued return to reference.
The initially accepted static postcondition applies only at the outer function
body, not at arbitrary nested block exits. Aliasing and mutation can invalidate
facts, so proofs must respect writes and declared effects.

The ordinary frontend should prove stable cases without depending on
optimization level. A proven-to-fail ordinary runtime guard produces a warning
by default, promotable to an error; a deliberately false `require` remains a
valid failure path when that diagnostic is not promoted.
Required static/constant proofs that fail or cannot be established diagnose.
Eliminating a guard must preserve evaluation, effects, exception behavior,
output publication and cleanup. `const` evaluation is not this proof system.
Neither `NoThrow` nor other declaration guarantees are inferred from a
conditional check in a body; public guarantees come from checked declarations.

`require` is safe and raises its selected failure on false. Only
catching/recovering is unsafe;
checks, ordinary calls and cleanup-only `finally` do not require unsafe context.
Unsafe recovery does not prove that invariants survived or roll back earlier
effects. Built-in exceptions such as `ArgumentNullException`,
`ArgumentOutOfRangeException` and `FormatException` are useful diagnostic
identities, not a recoverability classification. APIs should be able to
annotate a default requirement-failure type for brevity, with an explicit
call-site selection taking precedence. Common null/range checks may receive
recognized diagnostics/classification, but not through arbitrary method-name
inference or reevaluation of arguments, properties, or user comparisons.
Exception construction shape and the complete exception ABI remain work; do
not assume arbitrary parameterless constructors.

Fail-fast termination promises **no cleanup**, like losing power. Durable-state
protocols must tolerate abrupt termination and restart; `finally` and
destructors do not provide crash consistency. On defined unwinding paths,
preserve the existing ownership, output-publication, and `NoThrow` cleanup
contracts. Unsafe catch does not make those paths transactional.

`ArgumentException` deriving from .NET `SystemException` illustrates why
exception inheritance is not a failure/recovery taxonomy
([API reference](https://learn.microsoft.com/en-us/dotnet/api/system.systemexception?view=net-10.0)).
Propagation syntax has real demand but no consensus design. Go's 2025 review
reported boilerplate and hundreds of proposals; `check`/`handle`, `try`, and
`?` did not reach agreement, and syntax work paused -- not an adoption of `?`
([Go retrospective](https://go.dev/blog/error-syntax)).

Use one failure/cleanup vocabulary for error returns, conditional results,
exceptions, callbacks, and builder refusal: specify initialized outputs,
current owner, and cleanup before propagation. Foreign promises remain a
separate trust boundary.

Illustrative cleanup expansion, not a proposed `?` operator or a mandatory
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

### 4.2 Keep callables single-target; keep event aggregation in libraries

**Accepted direction with open capture details.** An ordinary callable has one
target and an explicit closed signature. Multicast is not an ordinary callable
behavior and Terrum does not gain a language-level event feature from this
direction. Event/subscription behavior, if provided, is an explicit collection
or dispatch/library abstraction. Its contract must define ordering, refusal,
exceptions, reentrancy, subscription mutation, cleanup/disposal, and handling
of **all** results; it must not silently discard earlier results and return
only the last as C# multicast invocation does.

`func` capture follows checked ownership and borrow/lifetime rules; copying a
function value must not silently duplicate a unique capture. Repeated calls
are valid only while dependencies remain valid; one-shot callables remain a
possible explicit library/representation contract with exact syntax open.

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

.NET multicast runs in order, stops on throw, returns the last result; that is
one precedent, not Terrum's implied event result policy
([delegates](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/delegates/using-delegates)).
The bounded callable families need no pack ABI; keep each signature closed and
aggregation explicit.

### 4.3 Keep responsibility for every asynchronous operation

**Accepted direction.** Asynchronous operations return task-like completion,
including `Task<void>` when there is no result; synchronous `void` remains
valid, but `async void` is not permitted. A task represents
completion, not by itself ownership or discharge of responsibility. Every
started operation remains owned by a structured scope that joins it, is
explicitly transferred to another responsible owner, or is handled by an
explicit supervisor with error, lifetime and shutdown policy. Returning a task
alone is insufficient when no caller/owner remains responsible. Discarding a
task does not cancel the operation, release its resources, or evade its
completion/error obligation.

Cancellation is a request, not completion. It does not end a task's borrows or
permit releasing borrowed backing until the operation has actually stopped and
completed required cleanup. Task transfer, capture cleanup and exception
reporting remain explicit. Task start policy (cold or hot), scheduler and
affinity mechanics remain open; this direction selects neither policy nor an
actor mandate.

In .NET, `async void` posts exceptions to a captured context/thread pool instead
of faulting `Task`; use an explicit callback adapter
([`AsyncVoidMethodBuilder`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncVoidMethodBuilder.cs);
[`AsyncTaskMethodBuilder`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilder.cs)).

Declared task borrows may keep backing live through actual completion and
required cleanup; cancellation does not shorten that lifetime. Specify capture
cleanup and keep `Task<void>` distinct from `Task<T>` result erasure. A task
does not legalize a loan escaping its backing's permitted lifetime.

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
| Implicit readonly parameter loans | Parameter-only `implicit ref<T>` borrowing; ordinary value matches take precedence; no adaptive result/view family or ordinary receiver change | Accepted direction / implementation scheduled |
| Generic array/enum capabilities | Preserve independent capability predicates, unnamed enum values, open-by-default evolution and explicit case-set/layout contracts | Accepted direction / representation details open |
| Layout and wrappers | Opaque by default; opt-in Sequential, Union and Transparent contracts; transparent wrappers remain nominal but are carrier-equivalent only under checked ABI, validity, ownership and cleanup contracts; no arbitrary user-selected field offsets | Accepted direction / exact syntax and ABI profiles open |
| Sealed concrete types | `sealed` controls inheritance; type/API evolution and case-set/layout promises are separate | Accepted direction |
| Collection-based `params` | Ordinary collection construction and per-element copy/move; existing-collection versus expansion binding remains open | Agreed direction / binding rule open |
| `func`, `fnptr`, tuples, and packs | Bounded closed callable/tuple direction, transparent aliases, and checked callable witnesses; exact grammar, ABI annotations and generic callable contracts remain scheduled/open; no user-defined pack expansion | Accepted direction / scheduled details |
| SIMD and tensor views | SIMD values carry vector ABI; tensors are higher-level logical multidimensional views over storage with explicit shape/stride contracts | Accepted distinction / library support scheduled |
| Iteration, return representation and allocation | Preserve lifetime, ownership, cleanup and relocation contracts; physical by-value lowering does not imply a source loan | Accepted safety direction |
| Context-specific underscore roles | Value discards evaluate and clean up; wildcard patterns do not consume the scrutinee; lambda names and generic inference holes are distinct contexts | Accepted semantic direction / grammar and support open |
| Conversions, literals, and constant evaluation | One checked `T(expression)` conversion form; contextual literals without suffixes or fallback types; registered parsing and bounded `[ConstEval]` with declaration capability/use-site requirement | Conversion syntax and bounded built-in numeric subset implemented; custom registration and evaluator remain scheduled |
| Operators and compound assignment | Checked runtime operators are distinct from `[ConstEval]`; user `+=` is explicit single-place `write ref<T>` mutation, not inferred from `+` | Accepted direction / operator grammar and implementation scheduled |
| Handlers and builders | Registered collection/text targets and parameter-directed streaming handlers; eager left-to-right typed captures, ownership cleanup and publish-on-success | Accepted direction / exact library APIs scheduled |
| Specialization | Exact closed generic references and declared target requirements; same-contract AOT/JIT variants, explicit priority, reject tied overlap | Accepted direction / implementation scheduled |
| Requirements and failures | Placement determines published pre/postconditions versus private body checks; static proofs are compile-time-only; safe `require` lifts declared conditional output; expected failures remain explicit without mandatory Result/typed-throws machinery | Accepted direction / syntax, exception construction and diagnostic mapping scheduled |
| Single-target callables and event aggregation | Ordinary callables have one target; events are an explicit library aggregation contract defining all results and dispatch/cleanup behavior | Accepted direction / library contract details scheduled |
| Async completion and responsibility | Every operation retains a structured owner, is explicitly transferred, or is supervised; task discard and cancellation do not discharge completion/borrow obligations | Accepted direction / start, ownership and scheduler mechanics open |
| Loop capture and callbacks | Capture, callback and scheduling facts need contracts | Open library contracts |
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
their registered target parser, not a rounded intermediate; registration alone
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

**Responsibility sketch, not syntax or start policy.** Before an asynchronous
operation can outlive its initiating expression, a structured owner must join
it, responsibility must be explicitly transferred to another owner, or a
supervisor must provide error, lifetime and shutdown policy. Owners cannot
release captures before actual completion and required cleanup; cancellation
does not discharge that obligation. Exact start timing, acceptance/rejection
behavior, spelling and scheduler policy remain open; executable claims require
producer/witness, lifetime, and AOT/JIT/module checks.

## Evidence and limits

Specifications establish behavior; proposal motivations and issue references
establish concrete design problems, not popularity. The Go retrospectives and
Swift typed-throws proposal provide explicit recurring-request evidence.
Historical proposal limitations are not claims about every current compiler.
These comparisons inform Terrum recommendations, not automatic adoption or
measured performance claims. [Source notes](references.md) cover the existing
CoreLib and ABI research.

## Accepted parameter-only implicit readonly loans

The accepted opt-in form is the parameter-only modifier `implicit ref<T>`.
`implicit` modifies a readonly reference parameter; it is not `implicit T` or
`in T`, a qualifier inside `T` or `ref<T>`, a `view<T>`/`View<T>`
representation, or a `ViewRepresentation` attribute. A caller supplies an ordinary expression:
evaluate it once, borrow an eligible existing place, or materialize a temporary
and clean it under normal call/exception cleanup. No `[Copy]` requirement or
ownership transfer is imposed merely to borrow.

The introduced loan is of the argument's **slot**, is call-scoped and
nonescaping, and cannot be returned, stored as a reference, retained by a
capture, or carried across suspension. This does not erase dependencies already
contained in the complete `T`: copying a `[Copy]` value out preserves its
qualification and backing/root/dependency contracts. A readonly outer loan
does not strip `write` authority inside `T`, imply deep immutability, or promise
purity or exclusivity. The modifier applies to concrete and generic parameters
and to explicitly declared extension receivers; it does not change ordinary
instance receiver defaults. The modifier does not apply to `write ref<T>`,
`init ref<T>`, or `ptr<T>` parameters and does not authorize arbitrary
conversion chains.

An explicit `ref<T>` parameter and `implicit ref<T>` have the same reference
signature; the modifier is call-side loan/noescape contract metadata, not a
second overload distinction. The preference between an applicable by-value
`T` overload and an `implicit ref<T>` overload is settled: an ordinary
`F(value)` match selects `F(T)` over `F(implicit ref<T>)`. An explicit
reference argument may select a reference overload. A declaration cannot
overload only on explicit versus implicit readonly `ref<T>`.

Physical by-value or indirect ABI lowering must preserve snapshot, borrow,
ownership and lifetime semantics. There is no universal size threshold or
adaptive type attribute. Returns remain `T` or an explicit `ref<T>` governed by
existing lifetime metadata; a generic result that adaptively chooses value or
loan is deferred until a concrete Terrum library need exists beyond family and
associated-type contracts.

Readonly access does not imply by-reference passing: `.NET Vector128<T>`
extensions may deliberately use a by-value receiver to avoid address-taking
spills on non-inlined paths, and even `int32` can remain better enregistered by
value. Physical ABI lowering must account for target register classification,
addressability, inlining and spill/load versus register-copy costs, not just
`sizeof(T)`, while preserving the declared snapshot or loan semantics. An
explicitly writable value receiver still needs a writeback mechanism;
mutating a by-value copy does not update the caller's original storage.

A future type-level opt-in might be considered, likely at a library/attribute
layer rather than as a core-language feature. That is only a deferred
possibility: no attribute name, mechanism or semantics are selected.
