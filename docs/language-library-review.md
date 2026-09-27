# Language and library improvement review

**Design review; §§1.1 and 1.2 and the collection-based `params` direction in
§1.4 record accepted directions.** Other proposals add no syntax/API promise
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

The reviewed implementation baseline is `f4b0425`, syntax-12/native-20:
optional handles and source-defined cycles, plus private class implementations
in v19 leaf modules. It does not establish a public managed ABI, general
interfaces/witnesses, array/view libraries, async, proposed libraries, or
specialization; examples and `.api.terrum` files remain design material.

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
arbitrary writable `ref<T>`. The bounded compiler still implements legacy
`bool`/`out` publication, which must migrate coherently across declarations
and call sites. It also rejects target `mut class` declarations and writable
`mut` instance-method receivers, while legacy class handles can still write
declared mutable fields; the current type metadata does not encode nominal
mutability. These are implementation gaps, not revisions to the accepted
model. Details and current support boundaries are in
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

### 1.3 Open proposals: generic APIs and type-system policies

**The proposals in §1.3 are not accepted as a whole.** Access categories
follow accepted §1.1 and the canonical [language contract](language.md).
Complete generic qualification follows the opening substitution rule in
[type-family design](type-families.md#type-family-design) and the
[library contract](libraries.md); writable storage remains invariant. These
are settled inputs, not open §1.3 policies. Exact constraint syntax and
additions explicitly labeled as proposals remain design questions; the
accepted baselines called out below are not reopened.
After the access/syntax-category documentation settles, revisit this section
for any remaining duplicated wording; this does not adopt the proposals below.

**Accepted capability baseline.** A nominal behavioral constraint such as
`where T : IFoo` requires a checked witness; a property constraint such as
`where T : [Copy]` is a compiler-verified capability predicate, not an
interface witness. These capabilities are independent: behavior, family
membership, Copy, and storage permission are not inferred from one another or
from operator names. See the canonical [language](language.md),
[library](libraries.md), and [type-family](type-families.md) contracts. §1.3
does not reopen those distinctions.

**Generic/erased API boundary (proposal).** Keep generic collections primary;
do not duplicate their API graph in an erased hierarchy; use a narrow dynamic
adapter only where heterogeneity needs explicit identity, ownership, lifetime,
and dispatch cost. How APIs compose independent operation constraints without
a universal “number” interface remains a design question.
C# static-abstract members are useful prior art
([C# interfaces](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface#static-abstract-and-virtual-members)).

**Nominal/conditional witnesses and coherence (proposal).** The accepted
nominal behavioral-witness distinction above is the baseline. How conditional
member-contract refinement and family membership are declared, versioned, and
checked for coherence remains open; refinement alone would not establish
behavioral conformance. Do not treat a second `ISpecialSpan` or an unsafe
downcast as an accepted answer.

**Associated types (proposal).** Associated results could express targeted
relationships without general higher-kinded types. Swift `Sequence.Element`
hides the concrete sequence; Rust associated types and stable GATs can relate
a lending result to `next`'s borrow
([SE-0346](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0346-light-weight-same-type-syntax.md);
[Rust associated types](https://doc.rust-lang.org/reference/items/traits.html);
[GAT stabilization](https://blog.rust-lang.org/2022/10/28/gats-stabilization/)).
One candidate policy is to expose associated `E` only when one selected
witness determines it, and keep `E` explicit when one `T` has multiple
relations. Borrowed-result dependencies follow the canonical
[lifetime contract](lifetimes.md); this does not decide associated-type
syntax or witness selection.

**Erasure and existential opening (proposal).** Swift opens one existential's
hidden type for an eligible generic call; two values do not prove equal hidden
`Self`, and dependent results cannot escape their proof/backing
([SE-0309](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0309-unlock-existential-types-for-all-protocols.md);
[SE-0352](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0352-implicit-open-existentials.md)).
Rust `dyn` excludes generic-method dispatch and most nonreceiver `Self` uses
(`Self: Sized` methods may exist but are not dynamically callable)
([dyn compatibility](https://doc.rust-lang.org/reference/items/traits.html#dyn-compatibility)).
A proposed Terrum `dyn ISequence<E>` would need to settle root, ownership/loan,
representation, and dispatch; opening one value cannot establish a second's
hidden type or let dependent results escape.

**Overload resolution and source compatibility (proposal).** Resolution is
not coherence. Kotlin extensions use the declared receiver and lose to
members; they add neither member nor witness
([Kotlin extensions](https://kotlinlang.org/docs/extensions.html)). Rust
rejects overlapping implementations. C++ constrained-overload subsumption uses
atomic constraint origins/mappings, not arbitrary logical equivalence
([Rust coherence](https://doc.rust-lang.org/reference/items/implementations.html#trait-implementation-coherence);
[C++ ordering](https://eel.is/c++draft/temp.constr.order);
[atomic constraints](https://eel.is/c++draft/temp.constr.atomic)).
One candidate policy is producer-owned, versioned witnesses with overlap
rejection rather than import-order resolution. A newly applicable overload
could change source meaning without changing old declarations, so source
compatibility and recompilation would matter alongside binary versioning.

**Inference (proposal).** Inference is a policy, not a universal rule. C++
relates parameter and argument types; Go solves some constraint equations and
defaults untyped constants ([C++ deduction](https://eel.is/c++draft/temp.deduct.call);
[Go inference](https://go.dev/ref/spec#Type_inference)). A Terrum inference
proposal must define which relations participate and in what order. A
constraint establishes admissibility, not necessarily `T` for `Parse<T>(text)`
from `IParsable<T>` alone
([C# constraints](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/where-generic-type-constraint)).
Expected-result inference remains open. Typed arguments may establish generic
context before a contextual literal is constructed; numeric literal fallback
and the unconstrained `F(1)` case follow the accepted
[language contract](language.md), not this proposal.

**Accepted enum baseline; public evolution remains open.** Integer-backed
enums include unnamed underlying values; `[Flags]` explicitly defines
bit-set operations. Closed nominal tagged unions have active-payload validity;
overlapping native layout alone does not establish it. These accepted rules
keep enum storage distinct from numeric semantics
([language contract](language.md)). .NET's generic enum operations use enum
constraints and underlying dispatch, not general numeric conformance
([CoreLib `Enum`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Enum.cs)).

**Public enum evolution (proposal).** For public case sets, distinguish closed
exhaustiveness from evolvable sums with unknown-case handling. Swift
non-exhaustive enums address library
evolution; Kotlin sealed subclasses are package/module scoped
([Swift SE-0192](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0192-non-exhaustive-enums.md);
[Kotlin sealed classes](https://kotlinlang.org/docs/sealed-classes.html)).
Neither closes integer enums or makes native layout a safe case test. Unknown-
case/version policy and match behavior remain proposals. Concrete types remain
sealed by default under the canonical [language contract](language.md); that
accepted default does not settle any of the enum evolution proposals here.

### 1.4 Collection-based `params` and callable/type-pack proposals

**Agreed collection-based `params` direction:** `params Collection<T>` with a
common `Span<T>` form, using ordinary collection construction and element
copy/move rules. The existing-collection versus expanded-element binding rule
remains open. This mechanism is distinct from the following unapproved
callable/tuple proposals.

**Unapproved bounded-subset recommendation:** keep homogeneous `params`
separate from heterogeneous type packs. A first subset could provide
compiler-recognized `fnptr`, single-cast `delegate`, and ordered `tuple` type
families at arbitrary supported arities, while each concrete function
signature and tuple has a fixed, closed shape. This avoids a maintained
`Func` arity ladder without introducing user-defined type-pack expansion, pack
indexing/folds, or forwarding. .NET's finite `Func`
arities and recursive `Tuple<..., TRest>` are library conventions, not a
language-level pack ([`Func`](https://learn.microsoft.com/en-us/dotnet/api/system.func-17),
[`Tuple`](https://learn.microsoft.com/en-us/dotnet/api/system.tuple-8)).
The built-in stored-callable family could instead be called `func`; its naming
and relationship to `delegate` remain open, without requiring user-declared
nominal delegate types.

Agreed parameter-model examples:

```terrum
void Consume<T>(params Collection<T> items);
int32 Sum(params Span<int32> values);
```

The following callable/type-family spellings are **recommendations, not
accepted syntax**:

```terrum
fnptr<int32, bool, int64> parse;  // (int32, bool) -> int64; result last
func<int32, int32> map;           // int32 -> int32; one target/environment
tuple<int32, bool> state;         // ordered, heterogeneous inline values

interface IMapper<TInput, TOutput>
{
    TOutput Invoke(TInput value);      // readonly functor receiver
}

interface IStatefulMapper<TInput, TOutput>
{
    mut TOutput Invoke(TInput value);  // requires writable receiver access
}
```

Expanded elements initialize new slots; they are not aliases of the caller's
argument locals. `params` adds no special by-reference parameter mode for
expanded arguments. An explicit reference type may still be an ordinary
element value where the normal storage/loan rules permit it. Access uses the
accepted `mut`/`write` and qualified-`T` rules in §1.1, with existing
lifetime/storage construction contracts rather than a separate parameter
system. Whether a call supplying an existing collection binds it as the
collection argument or expands it as elements remains a rule to specify.

`fnptr` would be a noncapturing raw callable with an explicit managed or native
ABI and effect contract as appropriate. A delegate would contain one target
and its environment, not a multicast list; capture, ownership, lifetime,
boxing, and allocation would be explicit rather than hidden by conversion.
A tuple would be inline ordered heterogeneous storage, not a parameter pack;
its Copy eligibility follows all of its elements, not the argument list that
created it. Its structural fields do not automatically make it a nominal
record or transparent wrapper, and function arguments are not implicitly
lowered to a tuple or given a different ABI. Any later pack design must also
keep bare `Task<void>` (no result) distinct from encoding-erased
`TextSpan<void>`, and preserve a unique `Task<T>` result and its cleanup.
Typed text projection still checks the recorded encoding and preserves
backing/lifetime dependencies; typed `AsSpan()` drops text validity only,
including for empty views.

Also review ordinary nominal functional interfaces/functors. A source-defined
type with a known concrete signature can explicitly implement a callable
protocol without packs. An optional checked `[Callable]` marker could permit
`f(x)` as notation for a checked `Invoke`, but structural SAM conversion and
implicit boxing are not recommended. A stateful inline functor needs a
writable place or `write ref<T>`; a class functor with `mut Invoke` needs a
`write` handle. Do not make `mut Invoke` silently satisfy a readonly `Invoke`
contract. Whether to provide one callable protocol or separate readonly and
mutable protocols, and the marker/notation, remain open; add a special
signature family only if the concrete use case requires it.

**Additional unapproved callable recommendation and open contracts:** a
lifetime-aware, single-cast `func` and a raw ABI-level `fnptr` could share
checked signature and witness machinery without sharing representation.
The agreed alias direction is transparent naming, not nominal delegate
identity; simple nominal wrappers are a separate feature. Alias syntax remains
open. A static callable view may retain named parameters for source contracts
and metadata, but parameter names are not ABI names. C# custom delegates have
named parameters; generic `Func` types expose library-chosen `Invoke` parameter
names. Under the proposed model, calls use the names from the declared
callable view, not the target implementation. Renaming a public parameter can
break source compatibility without changing the ABI. Named arguments retain
source-order evaluation before assignment to their parameter positions.

The signature-versus-value notation remains open. One candidate is
`fn<Args..., R>` for a closed signature, with `ptr<fn<...>>` for a raw code
pointer and `func<fn<...>>` for a stored callable; a flatter
`func<...>`/`fnptr<...>` family is another. Neither is selected syntax. Type-use
attributes for calling convention fit the existing design vocabulary; for
example, `[CallConv(NativeCallingConvention.Winapi)] fnptr<...>` is only an
illustration. A Terrum default versus explicit foreign convention, and any
ergonomic special-casing for `fnptr` declarations or imports/exports, remain
open. Calling convention belongs to the callable entry contract, not to `ptr`
by itself or an inference from pointer representation; no implicit convention
casts are proposed. A delegate's `Invoke` entry ABI can differ from its
underlying target ABI, so an adapter must declare the boundary and preserve
effects while making capture, allocation, and other costs explicit.

The contract must distinguish static functions, unbound instance methods, and
bound instance calls. This illustrative matrix records candidate semantics,
not selected syntax:

| Case | Raw `fnptr` candidate | Stored `func` candidate |
|---|---|---|
| Static | Points to a static function with ordinary arguments | Can store a static `M(T)` target |
| Open instance | `fnptr<this T, void>` candidate passes the receiver explicitly | `func<this T, void>` candidate is invoked with the receiver explicitly |
| Bound instance | Cannot retain a receiver; a pointer may target an adapter, but that adapter's origin is not part of the pointer contract | Can store a bound `C.M(T)` with an explicit environment/context |

The `this` marker and its compatibility with ordinary `T` parameters remain
open. It marks an open receiver role; it does not select a calling convention.
An open receiver's access must match the method (for example, `C`/`write C`
or `ref<S>`/`write ref<S>`). A plain `fnptr<T, void>` has an ordinary `T`
argument, not a bound receiver. Native member-function pointers are not
generally raw code addresses: adjustment thunks and virtual dispatch can
participate, so no blanket reinterpretation as a code pointer is implied.

**Generic callable and scalar/SIMD integration (proposal).** A closed
`func<float32, float32>` does not promise vector implementations. Numeric
kernels could instead require a generic functor whose `Invoke<T>` is checked
against declared operation and storage capabilities, then specialized for the
supported scalar and SIMD family members. Each raw function-pointer
instantiation still has one closed signature and ABI.

A later contextually generic lambda could implement that same functor
contract. For example, `x => x * x` could be checked with symbolic `T` under a
declared multiplication and Copy contract; it would not first become a scalar
callback and then be reinterpreted for vector arguments. Captured scalar
values need an explicit supported broadcast operation where appropriate.
Generic admissibility alone does not establish elementwise equivalence:
invocation counts, state changes, cross-lane operations, inactive lanes,
tails, floating-point order and contraction remain part of the kernel's
semantic contract. A readonly receiver is not a purity guarantee.

The recommended staging is fixed-signature callables and contextual lambdas,
then named generic functors with bounded scalar/SIMD contracts, then
contextually generic lambdas as shorthand for those functors. Arbitrary pack
manipulation, polymorphic callable erasure and automatic vectorization of an
arbitrary scalar callback remain outside this recommendation.

Formatting handlers remain typed writer streams with declared captures, not
universal `ToString` or heterogeneous varargs; see §1.2. C variadics are a
separate foreign-ABI/interop contract, not Terrum `params` or a type pack.
Before any later pack proposal, settle collection-versus-expanded binding,
constraints and overload ambiguity, per-element effects/ownership, tuple and
callable representation, callable entry and target ABIs, static/unbound/bound
instance representation, metadata/code-size costs, and managed/native ABI.

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

**Open proposal.** A general function value should have an explicit signature,
capture/lifetime contract, and single-target invocation. Repeat calls are valid
only while captures remain valid; consuming a capture needs explicit one-shot
semantics or reconstruction. Multicast is
useful for events but should not be an accidental universal delegate semantic:
define a separate event/subscription abstraction with order, exception
aggregation, reentrancy, mutation-during-dispatch, and unsubscribe/disposal
rules. A closure's captures must preserve ownership and borrowing rules; copying
a function value must not silently duplicate a unique capture.

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
Until pack ABI exists, keep arity explicit and multicast out of ordinary calls.

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
| Generic/erased APIs, conditional witnesses, associated results, erasure, inference, overloads | Remaining API and type-system policy choices | Proposal / refinement |
| Generic array/enum capabilities | Preserve accepted enum storage/unnamed-value semantics and independent capability predicates; array projection and public case evolution remain open | Accepted baseline / open proposals |
| Layout and wrappers | Opaque by default; transparent/native layouts preserve ABI, validity, and ownership | Accepted direction / open proposal |
| Sealed concrete types | Preserve; favor interfaces/composition and explicit extension points | Accepted direction |
| Collection-based `params` | Ordinary collection construction and per-element copy/move; existing-collection versus expansion binding remains open | Agreed direction / binding rule open |
| `fnptr`, delegate, tuple, and packs | Fixed closed callable/tuple families are a bounded recommendation; signature-versus-value types, ABI, and receiver representation remain open; no user-defined pack expansion | Unapproved recommendation / open |
| Tensor, multidimensional, SoA/AoS | Separate shape, strides, storage, and truthful projections | Proposal |
| Discards | One evaluation, vacancy, and cleanup rule across contexts | Refinement |
| Literals, consteval, operators, compound assignment | Bounded, hygienic, versioned constant evaluation distinct from runtime witnesses | Open |
| Handlers and builders | Registered expansions; eager left-to-right holes, cleanup, and publish-on-success | Open |
| Specialization | Same-contract optimization with fallback; semantic variants remain named APIs | Open |
| Errors and propagation | Separate expected/domain results, exceptions, invariant defects, host failures, and cleanup | Accepted constraints / open refinement |
| Callables, async, cancellation, structured concurrency | Completion, ownership, supervision, transfer, and revalidation remain explicit | Refinement / open |
| Iteration, allocation, loop capture, callbacks | Lending/invalidation and allocator costs; capture/callback facts need contracts | Open library contracts |
| Generic implementation strategy | AOT/JIT parity; sharing versus specialization is a backend choice | Open |

Validate contracts across AOT/JIT/module replacement before syntax; consider
bounded, versioned literals/handlers where useful. Defer packs/specialization
until workload evidence; no roadmap expansion is implied.

These choices trade implicit convenience for stronger contracts. Keep the cost
manageable with checked derivation of ordinary equality/formatting witnesses,
contextual inference only where the language specifies an unambiguous source of
target type, and useful library defaults -- not universal inherited behavior.
Static witnesses favor optimization but can grow code; explicit dynamic
adapters favor code sharing but add dispatch and storage contracts. Handler
registration is narrower than arbitrary macros, while packs and overlapping
specialization add inference and coherence complexity that ordinary typed
builders do not need.

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
