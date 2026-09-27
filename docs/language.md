# Terrum language design

Terrum (TAIR-um), from TerraFX, is a proposed C#-shaped language for applications through systems code. Proposed tools are `terrum` and `terrumc`. This document describes the intended language, **not a claim of complete implementation**: the compiler supports lossless syntax and a bounded [native value profile](native.md). The status of examples is documented in [samples](samples.md); implementation staging is in [implementation](implementation.md), and the proposed API inventory is in [libraries](libraries.md). Source spellings throughout the design samples are provisional.

## Accepted direction

- C#/.NET application and API ergonomics are the baseline; ownership, access, representation, lifetime, effects and placement are distinct contracts. An application can use managed cyclic graphs without propagating lifetime parameters through every edge; unique native resources and deterministic cleanup remain nonduplicable. There is no implicit copy, boxing, pinning, retain, synchronization or materialization to repair an invalid program.
- AOT and JIT must implement the same semantics for supported features. Independently serviced modules, DLLs and plugins require published, versioned contracts and binding-time compatibility; closed-world optimization or implementation pinning is explicit. The initial backend emits Windows x64 native code; the C#/.NET compiler host does not define Terrum's type system, semantic IL or permanent ABI.
- Concrete types are sealed by default; `sealed` prohibits inheritance, not interface dispatch or API evolution. Types are evolvable by default: closed case sets, fixed layouts and frozen APIs are separate scoped contracts. Interface inheritance and composition replace general concrete inheritance. Public namespaces express concepts, not library tiers. Public source visibility is `public`, `internal` (module) and `private` (declaring type); there is no protected-family or friend-module access through `unsafe` or linking. Author-approved generation may act within the declaring module's implementation scope, but grants no consumer private access. A public Terrum surface is not automatically a native export.
- Exceptions propagate exceptional failures; expected alternatives use conditional outputs or domain statuses. Recovering with `catch` requires an unsafe recovery context; cleanup and `try/finally` do not. Unsafe recovery does not prove an object's invariant has survived. Platform callbacks must contain faults or terminate rather than unwind across a foreign ABI.
- Type, declaration, interface and metadata contracts are authoritative. A compiler may infer private implementation facts but cannot silently publish stronger or weaker promises from a replaceable body. Consumers bind against the producer's resolved contracts and rule version, not their own reinterpretation of its source defaults. [Lifetimes](lifetimes.md) specifies this distinction for borrowed results.

### Types and operations

Declarations are immutable by default. A bare local is fixed after initialization;
`mut T value` permits rebinding that local. `mut class C` and `mut struct S`
declare mutable nominal storage; an instance method's receiver is read-only by
default, and `mut void Update()` explicitly requires a writable receiver. Keep
`mut` for writable places and receivers, not as a referent qualifier.

`write T` is a capability-qualified reference/view that permits writes through
that access path. It does not make immutable fields writable, change nominal
restrictions, or create authority absent from the source. A bare class type has
a readonly handle; `write C` permits mutation through that handle only where
the class declaration and member contract allow it. A readonly-receiver method
may still mutate independently writable arguments or stored handles, but may
not write its own receiver fields or call a `mut` receiver method through a
readonly `this`. A writable binding cannot upgrade a readonly referent.

A source return of `T` preserves its declared semantics even if the selected
ABI uses hidden result storage or copy elision. That lowering does not itself
introduce a loan of the original value's storage. Handles and loans already
contained in the complete `T` retain their declared dependencies. A borrowed
result needs an explicit declared lifetime, access and alias relationship.

The same distinction applies to views: bare `Span<C>` has a fixed descriptor,
readonly element slots, and readonly `C` handles; `mut Span<C>` permits
reassigning the descriptor only; `write Span<C>` permits writes to element
slots but not through its readonly `C` handles; `Span<write C>` retains
readonly slots while permitting writes through its `C` handles. In generic
`Span<T>`, the element's complete qualification is preserved, so a
`write Span<write C>` permits both slot writes and writes through its `C`
handles. `mut write Span<write C>` additionally permits descriptor rebinding.
These are different capabilities and cannot be substituted for each other.
The same distinction applies through `List<T>`,
`LinkedList<T>`, fields, and nested `ptr<T>`, `box<T>`, or `own<T>` forms: a
readonly container path may carry an explicitly writable stored handle without
granting write access to the container's slots or links. Inline struct fields
still require writable containing storage; an element qualifier cannot bypass
that requirement.

The canonical reference representation forms are `ref<T>`, `ptr<T>`,
`box<T>`, and `val<T>`, with `own<T>` for a native owning indirection.
As a parameter type, `mut ref<T>` permits rebinding the reference place while
retaining readonly referent access; `write ref<T>` grants writable referent
access where applicable. `write box<T>` can grant access
through a reference only within the payload's declared mutable-member
contract. `write val<T>` is not a default permission; inline fields need
writable containing storage or a writable loan/view. `own<val<C>>` is an
explicit native owner of an inline payload, not the unqualified/legacy `own C`
spelling. `init ref<T>` is an initialization
loan to vacant storage: it cannot be read before complete initialization and
publication. Legacy `out` declarations are scheduled to migrate coherently to
`init ref<T>`; the bounded executable profile still implements `bool`/`out`
conditional publication today. Call-site spelling for initialization loans
remains open. Parameter-only `ref T`, `T*` and `T&` are not alternate
representations. `mut` is not valid as a generic type-argument qualifier;
use `write` when the referent capability itself is part of the type.

Constructor initialization does not imply later mutation permission. Loans are
place- and flow-sensitive, ending after last use; overlapping read loans are
allowed, conflicting write loans are not. Qualified generic `T` preserves its
complete caller-selected qualification: a concrete readonly default does not
strip `write` from a substitution. A generic writable operation must state the
required access contract; `write T` is not a universal make-mutable transform.
A readonly-only type constraint is not the same as projecting a readonly view
from an unknown, caller-qualified `T`. The exact generic constraint syntax,
representation-aware mutation rules, and required protocol for writable
generic operations remain open.

An ordinary `struct` is inline and promises checked `[Copy]`; every field must support a code-free, allocation-free copy without duplicating a unique obligation. Adding a destructor or unique field must fail the declaration instead of silently removing that public capability. A `unique struct` or `unique union` is noncopyable even if its bytes look trivial. An ordinary `class` uses aliasable managed handles; copying a handle shares its object, not its payload. A `unique class` has a unique owning handle, not necessarily a unique reachable graph or a prohibition on GC. `unique` restricts duplication; `move` explicitly transfers a named value (including a copyable value when invalidating its source is desired). By-value copy/consume is based on the **complete qualified form**, not merely the payload declaration. A moved handle need not relocate the payload; large `[Copy]` values are not assumed cheap.

Representation projections are separate: `val<T>` denotes an eligible inline
payload, `box<T>` a reference form, `ref<T>` a bounded loan, `ptr<T>` an
explicit pointer representation, and `own<T>` a unique native owning
indirection to storage containing `T`. `own<T>` does not imply write permission, uniqueness of the reachable graph,
or mutable access. In particular, `own<write ptr<T>>` owns storage for a
pointer value that explicitly carries writable access; it does **not** own the
pointed-to `T` or extend that target's lifetime. Ownership of the pointer slot
does not create its write authority. `write ref<T>` and `write ptr<T>` likewise
express immediate writable access through that reference, subject to the
source's actual permission and lifetime. The permitted representation, move,
lifetime, and cleanup rules remain constrained by the owning profile.

Compatible `box<C>` is `C`, nested `box` normalizes, and `box<T?>` is
`box<T>?`; normalization does not allocate or invent a clone. Explicit
`AsBox` can reuse an existing reference or allocate for an eligible inline
value while preserving qualifiers and absence. `val<box<S>>` names a payload
type without extracting it. `val<C>` remains conditional on explicit
inline-payload, identity, copying and address-stability rules; class-handle
aliasing cannot be silently inherited by inline storage. Scoped loans,
arbitrary cleanup obligations and raw pointers cannot be boxed to escape
restrictions. `val<T>` has no implicit writable-default interpretation.

There is no universal `default(T)`: live fields and active union payloads must be initialized. Explicitly repeated optional modifiers (`int32??`, `T??`, including intervening trivia) are syntax errors. Optional construction remains semantically idempotent: substituting `T = int32?` into `T?` yields `int32?`, not a second presence layer. `Option<T>` likewise normalizes an optional payload rather than adding a presence layer. Modifiers on distinct nested types, such as `List<int32?>?`, are valid. `is some` yields the canonical payload, and `is null` tests absence. Nominal unions preserve distinct cases such as `Missing` versus `Found(null)`; `with` updates need separate `Unchanged`/`Set(null)` states. `T?` and `box<T>` are non-injective projections, so inverse generic inference cannot guess a unique original type. Normalization precedes signature identity/overload checks, without flattening nominal wrappers. Niche encodings must be declared validity/ABI contracts (e.g. reserved `-1` for nonnegative `int32`, zero for nonzero `int32`, null for a non-null reference); invalid encodings and padding are not free option states. Imports and mutations preserve validity.

Integer-backed `enum` values include unnamed underlying values; `[Flags]`
defines named bit-set operations without closing the set. Nominal unions are
evolvable by default; an explicitly closed case-set promise makes adding a case
a breaking change, ordinarily rejected before consumers bind to it. Union
representations still require a valid active payload;
an overlapping native layout does **not** establish a safe tagged union.
The layout forms are `[Layout(Sequential)]`, `[Layout(Union)]` and
`[Layout(Transparent)]`, each with a checked ABI contract. Sequential field
offsets and padding are computed from declaration order, size and alignment;
arbitrary user-specified field offsets are not supported. Structural `T | U`
unions, especially overlapping alternatives, are undecided.

Ordinary type layout is opaque. An accepted transparent-wrapper direction
keeps the wrapper nominal for Terrum source checking while making an eligible
simple wrapper carrier-equivalent under a checked ABI contract. Equal size or
field layout alone is insufficient: argument/return classification,
size/alignment, validity, ownership, cleanup and niche representation must
match the declared carrier rules. Transparency does not permit reinterpret
casts or implicit source conversion. `[Layout(Transparent)]` is illustrative
of the existing layout-attribute convention, not settled spelling or
eligibility grammar; the current compiler has no general transparent-wrapper
ABI feature. Struct and field attributes currently diagnose `TRM3001`; the
existing `ValueLayout` is limited to naturally aligned 1–16-byte aggregates
and does not implement transparent layout. The target opaque/evolvable default
does not change the current module format's exact layout contracts.

### Interfaces, access and generic properties

`where T : IFoo` requires a checked behavioral witness; `where T : [Sized]`, `[Copy]`, `[ManagedStorable]`, `[Send]`, `[Sync]`, `[TextCodeUnit]` or `[NativeComInterface]` asks for a compiler-verified property of the complete form, **not** implementation of an `I...` interface or the mere presence of an attribute. `[Sized]` does not promise a size constant in shared generic code. `[TextCodeUnit]` admits `char8`, `char16`, `char32`, not arbitrary valid encoded text. Native COM additionally requires checked IID, inherited slots, signatures, layout and lifetime. User shape generation can provide witnesses or adapters only after checking costs, ownership and ABI.

No universal root is required to supply `Equals`, `GetHashCode`, or
`ToString`, or to impose object-default boxing/allocation on every value; nor
is there automatic `IEquatable<T>`/`IFormattable` conformance. This rejects
universal object-to-string behavior, not explicit opt-in formatting protocols.
Primitive built-ins and compiler-verified operation capabilities remain valid;
whether a root type exists at all is a separate open decision. Keep value equality,
identity where identity exists, key equality paired with a compatible hash,
machine-readable formatting, and display/debug rendering distinct. Compiler
and debugger type metadata does not require user-visible root methods, and no
contract requires every value to become a string.

Behavioral conformance is nominal and uses a checked witness; conditional
member guarantees are not themselves conformance. Conflicting type/interface
witness claims are rejected, not selected by import order. Producer-owned,
versioned witnesses are the recommended bounded policy; external conformance
ownership and exact grammar remain open. Applicable members outrank extensions;
conflicting extensions may be explicitly qualified, but extension lookup never
replaces a nominal witness. Associated types and `Self` relations are intended
for relationships such as integer `Wider`/`Signed`/`Unsigned` types and SIMD
element/mask types. One selected witness binds an associated result; inference
does not run backward through non-injective projections or choose an arbitrary
implementer from a constraint. Multiple relations require explicit
disambiguation.

Multiple behavioral interface bases retain declaring-member identity through
diamonds; conflicting requirements need explicit disambiguation or an adapter.
This does not introduce stateful bases or an implicit default-method
linearization. A generic `IRandom` call through `write ref<TRandom>` mutates
the borrowed inline value without defensive copying or boxing; storing
`TRandom` additionally requires `[Sized]`. Generic pointer and stored-loan
arguments remain intended where their actual storage, tracing, and lifetime
properties permit them, not categorically prohibited by the narrower initial
managed `List<T>`. Sizeless scalable vectors can satisfy behavior without
satisfying `[Sized]`.

There is no `dyn` keyword or implicit stored interface handle. Bare `I` names
the behavior/abstract payload type, not a storable local or field. `T : I`
is nominal conformance; `where T : I` as a generic constraint keeps `T`
concrete.
An erased receiver or stored value chooses its representation explicitly:
`ref<I>` is a borrowed erased receiver, `write ref<I>` a mutable borrow,
`box<I>` a managed aliasable handle, and `write box<I>` a writable receiver.
`List<box<I>>` is a generic heterogeneous carrier. Explicitly boxing a Copy inline
struct creates an independent payload, and transferring an eligible unique
value is explicit; projecting an existing class object may preserve identity
without allocation. Type notation alone neither allocates nor reinterprets a
class-handle slot. `write ref<I>` can mutate the original implementation but
cannot replace an unknown inline payload with an unrelated implementation.
The slot/referent distinction remains explicit: `write ref<box<I>>` writes a
slot of readonly handles; `ref<write box<I>>` has a readonly slot that preserves
writable object authority. `ref<C>` borrows the class-handle slot, not
automatically the payload. Opening one erased value does not prove another
value has the same hidden `Self`; dynamic-callability, associated bindings,
borrow lifetimes, dispatch and ABI remain to be specified.

Source overload resolution records the selected declaration and closed generic
arguments. A linker or JIT validates and implements that binding rather than
rerunning source resolution; runtime virtual/interface dispatch is separate.
Exact matches precede permitted erasure, with no invented numeric conversions.
Constraints filter applicability but do not create overloads differing only by
constraints or require arbitrary logical subsumption; return-only overloads
are not introduced. Applicable members beat extensions. Thus a sole
`Set(int32)` accepts `Set(1)`, but adding `Set(uint32)` makes the source literal
ambiguous while an already compiled caller retains its `int32` binding.
Removing the bound declaration remains a compatibility break.

Generic inference is staged: explicit arguments pin positions, typed arguments
establish types, expected-result context fills only unresolved positions, and
then contextual literals/lambdas are checked. Context never retargets typed
operands or established generic types. For example, `int64 x = 50000 * 50000`
constructs `int64` operands, while `int32 a = 50000; int64 y = a * a` needs
explicit conversion. Partial `_` holes are accepted direction (exact spelling
is open): `Convert<_, int32>(typedValue)` may infer only the first argument,
and `int32 result = Convert(typedValue)` may infer a result-only type from its
target. `func<_, int32> f = ToInt32` can fill a hole from one unique method
signature. All holes resolve at local binding; exported fields, aliases, and
parameters cannot retain them. There is no wildcard/existential/higher-kinded
meaning, and non-injective projections are not inverted. Ambiguity diagnoses.
These are target binding rules, not a claim that the inference feature is
implemented. The current binder propagates expected `int32`, `uint32`, `int64`,
or `uint64` types into nested untyped arithmetic, so
`int64 x = 50000 * 50000` is valid; it rejects retargeting typed `int32`
operands and leaves context-free literals unresolved. Ordinary overload
declarations are rejected with `TRM3003`; generic source calls require
explicit type arguments. Generic signature-based result-type lookup is
implemented, but neither arguments nor expected results infer generic type
arguments. Overload selection and general argument/result inference, including
partial holes, remain scheduled.

Underscore has context-specific roles rather than one universal discard
operation. In a discard-value context, evaluate once and apply ordinary value
ownership and cleanup; in a wildcard pattern, match without binding or
dropping the source; in a lambda parameter, omit the local name without
changing argument evaluation or parameter mode. As a generic type-argument
hole, `_` is filled only by unique inference, has no default or wildcard
meaning, and cannot remain unresolved in an exported contract. These are
target semantics; exact syntax and contexts remain open, and the compiler
does not implement general discard, wildcard-pattern or generic-hole forms.
The binder currently treats `_` as an ordinary identifier and assignment;
expression statements and unused-local cleanup are not implementations of
these contextual roles.
The lexer continues to recognize underscore in identifiers outside
grammar-defined special contexts; there is no blanket underscore reservation.

Adding an interface member compatibly for existing implementations requires a
default implementation that satisfies
its declared contracts and a compatible witness/dispatch mechanism. Defaults
alone do not ensure safe evolution: new required associated types, tightened
effects/access/loan requirements and conflicting inherited defaults may
break implementations. A closed-case exhaustive dispatch has a checked
fail-fast path for unknown runtime cases, never an optimizer `unreachable`
assumption. An older consumer can route a newly added case to an explicit
fallback; recompiling against the new case diagnoses missing explicit
coverage. The accepted fallback attribute is shown schematically as
`[Exhaustive] _ => HandleUnknown(status)`: it accepts unknown cases but does
not count as explicit coverage of known cases. This is not a new keyword or
special `_` syntax. Missing known cases warn, guarded arms do not establish
coverage merely by naming a case, and
the initial form is a final unguarded wildcard. Missing-case warnings can be
elevated; ordinary `_` suppresses them. For integer enums, diagnostics concern
declared names while a fallback still accepts unnamed underlying values.
Exact match grammar and the closed-case marker remain open. Unknown payloads
must be safely rooted,
forwarded and disposed or the representation is rejected.

One access-qualified `Span<T>` and one qualified collection-interface family avoid
nominal readonly/writable duplicates. `mut` on a view descriptor permits
rebinding that descriptor; it does not grant writable element slots. `write` on
the view grants only the element-slot access represented by the view, while a
`write` element argument grants a distinct capability on contained referents.
The mutable-storage generic contract is invariant: a writable `Span<Derived>`
does not become a writable `Span<Base>`. A readonly projection is not
automatically covariant either; any future projection must prove representation,
lifetime, and conversion cost. Matching access never upgrades a readonly source
through a returned view; fresh construction can produce independent writable
storage, but shallow copying cannot upgrade retained aliases.

Structural read access protects the container, not independently mutable
element handles. Readonly is an access-path restriction, neither graph
immutability nor purity nor a claim of alias uniqueness or thread safety. It
is not a deep `read` qualifier. Do not hide an explicitly writable stored-handle
capability behind an unstated recursive attenuation rule. There is no `read`
keyword because the unqualified form is already readonly by default.
Deep-read and frozen/deep-frozen types are not part of v1. A future graph-read
proposal could attenuate references traversed from a root; a separate
immutable-graph contract would need to exclude writes through all aliases for
its defined extent. Frozen promotion cannot be implicit from readonly and must
exclude writable aliases. Checked `[Pure]` additionally
excludes observable writes, I/O, identity-producing allocation, ambient
mutable state, volatile/atomic observation and synchronization. Repeated-call
reuse needs stable observations, equivalent inputs and compatible code
identity; mutable inputs can change between pure calls. Dynamic effects/read
regions belong to interface and module contracts.

The working `IContiguousSequence<T>.AsSpan()` returns a read projection. `Span<T>` implements it **explicitly**, permitting constrained generic calls without a redundant concrete `span.AsSpan()` member. Known-code-unit `TextSpan<TCodeUnit>.AsSpan()` and `Text.AsSpan()` stay public because those change the view; erased `TextSpan<void>` has no `AsSpan()` or `IContiguousSequence<void>`. A stored or returned view must preserve its opaque backing dependency, even for empty/end slices. Detailed receiver and backing rules are in [lifetimes](lifetimes.md). `ISequence<T>` traversal and associated borrowing contracts remain to be specified. Working `Task<TResult = void>` makes `Task` precisely `Task<void>` after type-argument expansion, not an independent task protocol; type-default syntax remains open.

Declared [type families](type-families.md) can expose checked common operations and conditional members/conformance. A static parameter meaning "any fixed vector of float32" preserves its actual type, width and associated mask, including same-instantiation relationships between operands/results; it does not require users to introduce a nominal `ISimdVector` solely to name the family. Runtime interchange that hides an encoding is different from preserving an unspecified static instantiation. Erased forms are explicit family contracts, never an automatic `Type<T>` to `Type<void>` rule; copy/move, tracing, cleanup, lifetime and ABI obligations survive. `Task<void>` still means no result, not permission to discard an arbitrary owning result.

### Primitives, strict arithmetic and text

Width-explicit integers `int8` through `int128` and unsigned counterparts, distinct target-size `nint`/`nuint`, IEEE binary `float16/32/64/128`, `bfloat16`, IEEE decimal `decimal32/64/128`, raw `char8/16/32`, validated scalar `rune`, `bool` and `void` are the proposed families. `simd64/128/256/512<T>` are fixed ABI shapes; `simd<T>` is genuinely hardware-scalable and initially sizeless, not an unspecified preferred width. BID versus DPD decimal representation, native-integer names and precise vector-family syntax remain open; the decimal value semantics do not become CLR `System.Decimal`.

Numeric literals have **no fallback type**: an expected type or explicit
target must disambiguate; `var x = 1` and unconstrained `F(1)` diagnose.
Contextual construction rounds/validates directly in the selected format, not
through a hidden `int32`/`float64`. `int32(1234)`, `uint64(0xFFFFFFFF)` and
`float32(0.1)` illustrate direct target parsing. Width suffixes do not exist.
`T(untypedLiteral)` uses the registered parser directly; `T(typedExpression)`
is a checked explicit conversion. `new T(arguments)` remains nominal
construction. Already-typed numbers never implicitly widen or narrow.
Type/value-call binding must resolve a type target versus an ordinary callable;
this target rule is not yet compiler support. `[ConstEval]` marks declarations
eligible for bounded deterministic compile-time execution; `const T a = F()`
requires a constant declaration value, while `T b = const F()` requires only
the expression. Literal parsing always uses the registered compile-time
parser, even in ordinary local initialization; there is no runtime fallback.
The evaluator has no ambient I/O, preserves target semantics and recorded
producer/rule/value-ABI dependencies, and permits only checked bounded
evaluation. It does not change type checking, overload binding or optimizer
freedom. A declaration with `operator +` needs a separate contract for
`operator +=`; user compound assignment mutates its place once through
`write ref<T>` without an implicit read/copy/write sequence. Wrapping and
saturating operations remain explicit.

Integer arithmetic and extent arithmetic are checked in every build. Division
truncates toward zero; signed minimum divided by `-1` fails while its remainder
is zero. Shift counts are checked; left shift is width-truncating bit
manipulation, not checked multiplication. Floating operations use target-format
round-to-nearest ties-even without implicit FMA, reassociation, ambient rounding
or flush dependence; arithmetic NaNs canonicalize in the working profile,
while bitcasts/copies preserve bits. [Conversion policies](conversions.md)
distinguish range, fractional rounding, modulo narrowing, nonfinite handling
and bit reinterpretation. Numeric-key comparer semantics (all NaNs equal,
signed zeros equal, decimal cohorts normalized) are separate from IEEE
operator equality.

Source text is UTF-8; runtime text has **no default encoding**. The single `TextSpan<TCodeUnit = void>` family has immutable validated `char8`/`char16`/`char32` members and a family-declared encoding-erased `void` specialization: bare `TextSpan` is exactly `TextSpan<void>`. Here `void` does not mean zero-size units or a default encoding. Erasure needs no text copy/transcoding and retains actual encoding, backing, access and lifetime/lease dependencies. Checked typed projection rejects a mismatched representation rather than transcoding; only known-code-unit forms expose typed `AsSpan` and contiguous-sequence conformance. Common `IsEmpty`, ordinal and formatting operations work on either form. A raw `AsSpan()` can forget text validity but not immutable backing, access, lifetime or rooting. Raw slices can split scalars; text slices must preserve scalar boundaries. Editing copies into mutable raw storage or a builder, then validates and freezes before publication; transferring an exclusively held initial buffer may avoid a copy only by revoking all writes. Readonly aliasing or validation alone cannot establish immutable text. Registered string targets use ordinary calls such as `utf8z("Terrum")`, not lexer prefixes or suffix tokens; the target validates encoding, termination and embedded-zero rules without implying pointer lifetime. Final storage, string facade, adaptive/block/RLE representation and projection ABI require experiments.

Design-only character literals carry one scalar, target-typed to a representable code-unit/scalar type without a numeric or UTF-16 fallback. Registered text targets provide `rune` context and forward scalar fragments directly through the writer protocol. A raw `char8` is not automatically a valid standalone UTF-8 scalar. Precise representability rules and the current unsupported-syntax boundary are in [conversions](conversions.md#character-intent).

Defaults are **ordinal plus invariant together**: exact Unicode scalar sequence equality and scalar ordering across encodings, matching hash, no implicit normalization/casing/linguistic comparison, and ASCII digits/signs/punctuation for numeric parsing and formatting. U+0661/U+FF11 digits and U+2212 minus do not act like ASCII tokens. Arbitrary valid Unicode remains valid text data. Termination, embedded-zero exclusion, pinning and lifetime are distinct native-string requirements. `Text` is a common payload/facade. The target sketch uses `own<val<Text>>` when it means a native owner of inline `Text` payload; construction/conversion and cleanup syntax remain unsettled, and this is not implemented.

### Callable values, `params` and tuples

The target callable families are `func<Args..., R>` for a lifetime-aware
single-cast callable and `fnptr<Args..., R>` for a raw pointer to target-defined
code; the result is last, and each concrete signature is closed at any
supported arity. A `func` retains its checked target and capture dependencies
without silently owning borrows or extending lifetimes. A `fnptr` retains no
independent environment, and its code must outlive each use. Neither form
multicasts, boxes by default, or implies an allocation. Type aliases,
including generic aliases, preserve parameter labels, access, effects, and
calling convention with the complete signature; nominal wrappers are a
separate feature.

Callable parameter names and tuple element labels are source-view and
metadata/tooling information, not ABI names or nominal identity. Calls use the
statically visible callable labels, and named arguments evaluate in source
order before binding to parameter positions. Tuple values are inline ordered
heterogeneous storage with a fixed closed shape, not a parameter pack or
implicit function-argument ABI. Their copy, uniqueness, borrow, and access
rules follow every complete element type.

`fnptr` uses the Terrum callable convention by default, analogous to a C#
managed function-pointer contract but not CLR execution. Foreign conventions
are explicit checked type-use attributes on the callable entry, retained
through pointer nesting, aliases, parameters, and results. For example,
`[CallConv(NativeCallingConvention.Winapi)] fnptr<...>` and
`NativeExport(CallingConvention = ...)` illustrate existing design vocabulary,
but exact names/grammar and import/export ergonomics remain open. No implicit
convention casts are allowed, and ABI choice alone makes no promise about GC,
allocation, exception containment, or marshalling. A `func` invokes through its
Terrum contract and may explicitly adapt a target with a different ABI. Open
instance callables receive an explicit receiver whose access matches the
method; bound callables retain receiver dependencies in their environment.
The exact receiver marker syntax is open. A bound instance does not require a
separate thin/fat type family; no full open/closed-by-thin/fat cross-product,
fat `fnptr`, or mandatory no-capture family is part of this bounded direction.

Nominal functors have checked receiver contracts: readonly `Invoke` is distinct
from `mut Invoke`; a stateful inline functor requires a writable place or
`write ref<T>`, and a class functor with `mut Invoke` requires a `write` handle.
Compiler-supplied callable witnesses apply only when the complete signature,
effects, access, lifetime, and ABI satisfy the protocol; they do not
synthesize allocated interface adapters or authorize arbitrary structural
conversion. Contextual lambdas preserve the expected callable or functor
representation, and a capturing lambda is not implicitly a `fnptr`. Generic
functors use declared symbolic constraints and specialization; ordinary
scalar callbacks do not imply SIMD implementations or automatic vectorization.

`params Collection<T>` and common `params Span<T>` forms use ordinary checked
collection construction and element copy/move rules. Expanded values occupy
new slots rather than aliasing caller locals; `params` adds no by-reference
parameter mode, though explicit reference types may be elements where normal
storage and lifetime rules permit them. Whether an existing collection binds
as the collection argument or expands into elements remains open.

These are target contracts, not claims of current compiler support. Callable
and tuple syntax, checked witnesses, and contextual lambda lowering remain
scheduled work; user-defined type-pack expansion, pack indexing/folds,
polymorphic callable erasure, and implicit callback vectorization are outside
this bounded direction.

### Construction, errors and effects

Ordinary explicit constructors are baseline. Independent opt-in `Generate` operations can supply construction, equality/hash, deconstruction, formatting and combined validated withers from declared storage/mappings; no field-name magic, automatic primary-constructor capture, implicit successive setters or parameter-name-only overload distinctions. Omitted update members are `Unchanged`, not a null/default value. Affine and class withers need explicit failure, cleanup, allocation and identity behavior.

The target inference order also applies to constructors: known argument types
establish generic positions, expected-result context may fill unresolved ones,
and contextual literals follow. `new DifferentRandom(new CounterRandom(0),
0xA5A5A5A5)` illustrates first-argument inference and declared `uint32`
literal contexts; it does not infer field capture, change ownership, choose a
type from constraints or invert optional/box normalization. General constructor
inference is not implemented by the current binder.

The target parser contract is `IParsable<TSelf>.TryParse(TextSpan, init ref<TSelf>)`, including user types. `init ref<T>` is a vacant initialization loan: its destination cannot be read before complete initialization and publication. A boolean success contract publishes on true and leaves it vacant on false; other returns initialize it on every normal return unless an explicit condition such as `[OutputWhen]` says otherwise. Failure cleans partially initialized values, never fabricates `default(T)`; asynchronous publication occurs at operation completion. The call-site spelling is not settled. The bounded executable profile still implements `bool`/`out` conditional publication; its declarations and calls must migrate together rather than treating `init ref` as a parser alias or general writable reference. Expected invalid input normally branches on `Try...`/status. Domain outcomes may use response types; there is no mandatory language-wide `Result` or implicit catch-and-continue.

`require predicate;` is a normal user-facing checked boolean requirement: evaluate the predicate once, continue on true, and throw a requirement-failure exception on false **in every build**. It accepts general predicates (`require format.IsEmpty;`) and can lift a declared conditional initialization success so the initialized value is available afterward. Status APIs require an explicit success predicate, such as `require status == CalculateStatus.Success;`, using the declaration's output relationship. It is not a debug assertion, optimizer assumption, catch or silent status conversion; exceptional/partial-output cleanup remains intact. Metadata exposes resolved success/output/effect contracts to ordinary tools and consumers. The call-site requirement itself belongs to semantic code/IR, not a body-inferred public precondition or framework-private intrinsic. Boolean requirements and conditional outputs for the bounded native value profile are implemented. Explicit fail-fast terminates on false; the separate bounded checked policy propagates and can recover only four compiler-generated failure reasons. General exception identity/ABI remains unimplemented; completed compact owning
outputs are cleaned up on false or checked exceptional exit.

Formatting uses one `IFormattable` → `ITextWriter` protocol with typed fragments/values, no obligatory allocating formatter per type. Target-typed interpolation and collection expressions select registered handler types, such as `[TextBuilder(typeof(TextBuilder))]` or `[CollectionBuilder(typeof(ListBuilder<>))]`, not arbitrary similarly named methods. Holes evaluate once left-to-right into typed captures before segments drain; ownership, borrowing and refusal cleanup remain explicit. Named unique transfers require `move`. Nested formatting forwards the same writer so a Point and its scalar fields can reach a custom handler without temporary strings or boxing. Refusal stops future writes but cannot undo prior output or hole evaluation; no replay, rollback or transactional stream guarantee. Expression handlers may consume an explicitly supported typed operation graph, as in `Tensor.Apply(destination, i => x[i] * y[i] + z[i])`; this is not body-divined autovectorization of an ordinary callback. Unsupported expressions diagnose or require a separately declared fallback. Owning text publishes only after full validation/freeze and cleans partial storage. Collection spreads enumerate once in order; builders own accepted elements, clean rejected consumed elements and abandon partial state exactly once. Detailed standard-library APIs and resumable async formatting remain scheduled. Synchronous formatter refusal is not a resumable continuation.

Allocation, GC, rooted reads/writes, blocking, suspension, I/O, atomics, throwing/no-throw, normal/no-return, cleanup and retirement are independently expressible effect bounds, finer-grained than Core/Foundation/Application dependency layers. `void` is the single zero-data result type, with value `()`, usable as a zero-argument `func<void>` and `Task<void>`; `Option<void>` still represents two states. A checked `[DoesNotReturn]` on a `void` call removes its normal continuation but retains exception/cleanup edges; `[NoThrow]` is separate. A verified implementation cannot normally return, including by completing a task that terminates *later*. There is no public `unit` or `never` primitive; the compiler may use internal bottom flow. Removing an exported no-return guarantee breaks callers that rely on it.

The current native profiles implement conservative checked `[NoThrow]` and
`[DoesNotReturn]` bounds and their versioned declaration contracts. A separate
explicit checked-exceptions proof policy uses private Windows SEH records for
four compiler-generated failures and permits recovery only in `unsafe`
`try`/`catch` scopes. Structured `try`/`finally` handles normal and checked
exceptional exits in that policy; explicit fail-fast termination still skips
pending cleanup. General exception identity, low-memory semantics and
other effect bounds remain later implementation work. A compact
`unique struct` proof checks named moves, by-value parameter transfer, and
conditional owning `bool`/`out` publication with lexical NoThrow cleanup.
Fresh constructor/factory results can transfer directly; a moved owner is
vacant. Receiver loans remain synchronous and readonly. Compact owning fields
have partial-construction cleanup in reverse actual acquisition order and
complete-value recursive cleanup. The compiler-owned `OwnedInt32Buffer` adds
checked native allocation, initialized-prefix operations and lexical readonly or
exclusive writable slices; partial moves, general stored loans and broader
resource adapters are not implemented.
See the [native proof limits](native.md).

### Storage, concurrency, entry and boundaries

Tracing GC handles aliased/cyclic application graphs; ownership handles deterministic native storage and resources. A unique owner may still refer to GC storage; a GC root does not keep a logically disposed native allocation valid. Distinguish storage acquisition, initialization, close, value destruction, storage release and eventual reclamation. Vacant/partially initialized/live/retired states, reverse-order partial cleanup, no-throw/non-suspending scope cleanup, direct destination construction and stable-address requirements must be checked. Replacing a live value builds the replacement before dropping the old one; explicit reconstruct-in-place may leave vacancy on failure. Pool leases revoke access on return; deferred dead-storage retirement differs from deferred live-value cleanup, with one retirement owner after commitment and explicit completion endpoints.

Ordinary mutable access is local/nonconcurrent, not intrinsically bound to an OS thread. `[Send]` transfers actual authority; `[Sync]` governs sharing; neither follows from GC, `[Copy]` or readonly. A checked `ListWriteAccess` guard arbitrates *all* aliases/reentrancy before mutable span access, without silently registering failed static loans or becoming a cross-thread mutex. A managed `LinkedList<T>` retains node identity through remove/reinsert; `Clear` detaches even externally held nodes. Wrong-list/preflight failures leave the structure unchanged; enumeration fails after structural mutations, not mere payload replacement. Common collection semantics and examples are in [samples](samples.md) and [libraries](libraries.md).

Tasks are scope-owned and structured; cold start is a **recommendation**, not a frozen contract. Cancellation does not establish completion of external buffer use, and suspension cannot extend invalid loans. Entry conventions are `Main` returning `void`/`int32` or `MainAsync` returning `Task`/`Task<int32>`, with no arguments or `Span<TextSpan> args` excluding the program name. Host-selected arguments/rooting last through async completion and structured cleanup; `async` describes implementation, `Async` describes API behavior, and no implicit blocking bridge is inserted. `Terminal.Write`/`WriteLine` default to standard output; `output: TerminalOutput.Error` selects stderr, with writer access for composition. Redirected streams work without an interactive terminal; terminal controls require endpoint capabilities.

Recommended continuation policy: no ambient `SynchronizationContext`-style capture by default and no pervasive per-await `ConfigureAwait(false)`. Explicit execution/affinity scopes or declarations govern thread-bound/UI continuations, including cleanup and cancellation. Generic library code composes through those declared requirements without acquiring accidental UI dependencies. No capture is **not** "always switch to a thread pool": synchronous continuation is allowed, logical serialized authority need not mean one OS thread, and non-Send/affine state must never migrate unsafely. A caller's explicit UI obligation remains valid across a library call without imposing it on every internal continuation. Scheduler/awaiter syntax, propagation and boundary validation still need design; there is no implemented async runtime.

Reflection respects visibility, access, lifetime and trimming. Symbol-resolved tokens and compiler-checked `[ConstantExpected]` names (including through wrappers) support safe bounded lookup; open-ended runtime discovery or private access needs a *different* privileged unsafe API, explicit authorization and retained targets. Retention of layout, executable bodies, descriptors and debugging data is independent. Author-authorized deterministic generation can inspect its own defining module's private members but cannot grant consumers friendship or bypass normal checks. Foreign interop uses source or generated tooling for marshalling; the compiler/runtime provide checked native symbols, calling conventions, representations, ownership, callback-lifetime and exception-containment prerequisites for safe imported-C APIs, not built-in value marshalling. COM requires exact profiles, not behavioral-interface resemblance.

The proposed portable structured semantic IL carries typed blocks/arguments/edges, values and memory places, loop/exception/cleanup regions, roots, checked operations, construction and suspension, with validation of dominance, ownership and region consistency. It is **not** CLI IL. The initial backend lowers a smaller verified structured representation directly to Windows x64 machine code, without CIL or LLVM. LMOD remains a provisional versioned module container for semantic types/signatures, layout/ABI, bodies, dependencies, roots, foreign profiles and native caches. Contract-bound vs implementation-pinned replacement is independent of static vs dynamic linkage. Unchanged JIT/AOT consumers must reject incompatible capture, output, access, layout and effect changes; AOT-only deployment cannot secretly require a JIT. The bounded module implementation checks declaration-derived signatures,
compact layouts, ownership/output modes and the supported effect promises before
loading unchanged native consumers. General capture/root contracts and resilient
layouts are not yet implemented. Generic specialization, sharing and explicit scenarios need enforceable ABI/dictionary/rooting rules; unsupported strict codegen policies must fail instead of silently boxing or duplicating code.

## Provisional source notation and open decisions

The design examples use canonical `ref<T>`, `ptr<T>`, `box<T>`, `val<T>`,
`own<T>`, `init ref<T>`, `mut`, and `write` forms, plus `move`, bracketed
checked predicates, `[ViewOf]`, `[Captures]`, `[OutputWhen]`, `[Pure]`,
`[MatchedAccess]`, `[Layout(Transparent)]`, `require`, `[TextTarget]`,
`[TextBuilder(typeof(...))]`, `[CollectionBuilder(typeof(...))]`,
`[Literal(...)]`, `[ConstEval]`, `[Specialize(...)]`, `[NativeExport]`,
`utf8z("...")`/`utf16z("...")` calls and generic trailing defaults as
**illustrations**, not implemented compiler promises. Conversion calls use
the accepted `T(expression)` form; width suffixes and prefix casts are not
target syntax. In particular, `out` remains the current bounded
implementation spelling while a coherent migration to initialization loans is
planned; invocation spelling for `init ref` is open. Exact generic constraint,
associated-type and extension-disambiguation syntax, inference-hole spelling,
closed-case marker/match grammar and compatibility-forwarder attributes/source
visibility remain open. So do transparent-wrapper eligibility/ABI profiles,
conditional copy capabilities, first-class
stored-loan ABI, erased-interface dispatch ABI, span-anchor layout/calling
convention, inline `val<C>`, native ownership transitions, structural unions,
decimal encoding, text storage, wither omission/overloads,
handling/registration, async start/cancellation, generic dispatch selectors
and exception ABI.
The applicable accepted semantics and current implementation limits are
distinct; syntax should follow checked contracts rather than determine them.

## Current implementation limit

The current compiler binds and verifies a bounded integer/boolean/code-unit and
compact-value profile, emits native Windows x64 executables, and executes the
same lowering through a JIT worker. Independently produced semantic modules
support checked process-start replacement; the compiled Terrum scanner can
supply the real front end's lexical input. These are not a general LMOD
deployment model or a compiler bootstrap. The current subset rejects target
`ref<T>`/`ptr<T>`/`box<T>`/`val<T>` forms, `own<T>`, general loans and
`init ref<T>` invocation. The validated semantic-v24 class/`ManagedNode`
access batch supports `mut class`, writable `mut` class receivers, readonly
bare handles, and explicit `write` capability through fields, optionals,
parameters, results and inference. Constructors initialize unpublished
objects before publication; a writable child handle retains its capability
through a readonly parent without deep-read propagation. Writable storage is
invariant, output destinations require an exact qualified type, and value
`write`-to-readonly attenuation is allowed. Semantic readers normalize private
managed artifacts from versions 18 through 23 into qualified internal IR,
translating legacy class/`ManagedNode` mutation authority to explicit `write`
exactly where previously granted. Programs not requiring the semantic-v25
`MutReceiver` contract retain their prior versions and bytes regardless of
managedness. Native handle layout and the scalar-only public module boundary
are unchanged.

The semantic-v25 `MutReceiver` contract supports `mut` methods on non-owning
compact structs as non-exported persisted calls. Source visibility may be
public or private, but does not expose a public mutating-receiver ABI. A method
invoked on a named mutable local, or forwarded through an existing `this`,
borrows and writes the original place, including writes before a checked exception;
default-readonly methods and by-value arguments preserve copy behavior, and
the ordinary readonly/by-value receiver ABI is unchanged. The verifier rejects
mutable receiver loans forged from immutable places, while
nested field writes through a valid receiver are allowed. These methods remain
non-exported: public scalar entry wrappers may call them; source-level
`public` visibility does not provide a public mutating-receiver ABI. Older
module headers reject the v25 contract; programs not requiring it keep their
prior semantic versions and bytes. Unique/owning mutating receivers,
temporaries, nested-field receiver paths such as `outer.Inner.Mutate()`,
rebinding `this`, and general reference forms remain unsupported. Primitive
`Buffer` views retain separate legacy access paths, and the coherent
`out`-to-`init ref<T>` migration remains scheduled. Neither the fail-fast runtime nor bounded
checked-exceptions proof is the language's future general exception runtime.
The bounded numeric compiler still accepts legacy width-suffix and prefix-cast
grammar; unified `T(expression)` conversions, unsuffixed literal registration
and `[ConstEval]` execution remain scheduled syntax/runtime work.
Bounded ownership and lexical-view checks, source-defined managed classes,
optional edges and explicit cycle tracing are implemented; general
ownership/lifetime analysis, public managed module contracts and
source-compatible standard libraries remain future stages.
Syntax-only commands do not validate semantic obligations; native builds
diagnose unsupported constructs rather than silently weakening them. See
[implementation](implementation.md) and [native](native.md) for actual support,
[roadmap](roadmap.md) for staging and [samples](samples.md) for runnable
examples versus broader design sketches.

## Sources and prior art

[TerraFX conventions](https://github.com/terrafx/terrafx/blob/9def09f6c24a0465035ce004ab240ecce0a97a04/.editorconfig) and [.NET Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/) inform naming, not copied restrictions. [C# scoped/ref escape rules](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/low-level-struct-improvements.md), [C# 13 ref-struct interfaces](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md), [collection builders](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions), [interpolated handlers](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/interpolated-string-handler), [ECMA-335](https://ecma-international.org/publications-and-standards/standards/ecma-335/), [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) and [Arm sizeless types](https://arm-software.github.io/acle/main/acle.html#sizeless-types) are comparison points, not automatic adoption. The current C#/.NET previews are backend/prior-art candidates subject to semantic and ABI verification.
