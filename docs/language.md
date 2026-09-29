# Terrum language design

Terrum (TAIR-um), from TerraFX, is a proposed C#-shaped language for applications through systems code. This document specifies accepted semantic direction; source spellings in design samples remain provisional. Detailed call, output and checking contracts are in [language contracts](language-contracts.md). Actual compiler and runtime support is documented in [implementation](implementation.md), its staging in [roadmap](roadmap.md), runnable examples in [samples](samples.md), accepted library direction in [libraries](libraries.md), and candidate API declarations in the [core-library proposal](proposals/core-library-reference.md).

## Definitions and notation

**Contract** means a published type, declaration, interface, ownership, access,
lifetime, effect or representation guarantee that source implementations and
callers must satisfy. **Accepted direction** identifies selected language
semantics; it does not imply that the compiler or runtime implements them.
**Complete qualified form** includes a type's access and ownership qualifiers,
not only its nominal name. Thus `write C` and `C` are distinct forms, and
`mut` qualifies a place or receiver rather than a type argument. `F<T>` denotes
a type constructor applied to `T`; `T?` denotes an optional form where the
surrounding contract permits it.

`Copy` is a checked property of a complete type that permits copying without
duplicating a unique obligation; it does not imply bitwise copying or low cost.
`unique` prohibits implicit duplication and uses explicit ownership transfer,
but does not imply a unique reachable object graph or prohibit managed
storage. The canonical representation forms are `ref<T>` (borrowed reference),
`ptr<T>` (pointer), `box<T>` (managed aliasable indirection), `val<T>` (inline
value) and `own<T>` (native owning indirection).

## Accepted direction

- C#/.NET application and API ergonomics are the baseline; ownership, access, representation, lifetime, effects and placement are distinct contracts. An application can use managed cyclic graphs without propagating lifetime parameters through every edge; unique native resources and deterministic cleanup remain nonduplicable. There is no implicit copy, boxing, pinning, retain, synchronization or materialization to repair an invalid program.
- AOT and JIT must implement the same semantics for supported features. Independently serviced modules, DLLs and plugins require published, versioned contracts and binding-time compatibility; closed-world optimization or implementation pinning is explicit. Neither a compiler host nor a particular backend defines Terrum's type system, semantic IL or permanent ABI; current target support is documented in [implementation](implementation.md).
- Concrete types are sealed by default; `sealed` prohibits inheritance, not interface dispatch or API evolution. Types are evolvable by default: closed case sets, fixed layouts and frozen APIs are separate scoped contracts. Interface inheritance and composition replace general concrete inheritance. Public namespaces express concepts, not library tiers. Public source visibility is `public`, `internal` (module) and `private` (declaring type); there is no protected-family or friend-module access through `unsafe` or linking. Author-approved generation may act within the declaring module's implementation scope, but grants no consumer private access. A public Terrum surface is not automatically a native export.
- Exceptions propagate exceptional failures; expected alternatives use conditional outputs or explicit domain statuses. `require` is a safe checked failure; catching/recovering requires unsafe recovery authorization, while unsafe operations and caller-unsafe invocations require explicit `unsafe { ... }` blocks. Cleanup and `try/finally` do not require unsafe authorization. Unsafe recovery does not prove an object's invariant has survived. Platform callbacks must contain faults or terminate rather than unwind across a foreign ABI.
- Type, declaration, interface and metadata contracts are authoritative. A compiler may infer private implementation facts but cannot silently publish stronger or weaker promises from a replaceable body. Consumers bind against the producer's resolved contracts and rule version, not their own reinterpretation of its source defaults. [Lifetimes](lifetimes.md) specifies this distinction for borrowed results.
- Source compilation checks authored bodies against declared contracts. Ordinary binary consumers safely decode and structurally validate artifacts, resolve references, check binding/contract compatibility and validate caller use, then trust declared metadata contracts for external dependencies instead of re-proving dependency implementations. Callers still satisfy imported preconditions, access, ownership and effects. This is not permission to trust malformed bytes; optional deeper audit tooling is not a v1 promise. Signing/provenance is separate from semantic correctness; no mandatory signature or trust store is selected, and signer identity does not prove a contract true. Current reader and verifier support is documented in [implementation](implementation.md).
- Keep minimum language requirements separate from the standard application-library/runtime surface and feature-specific dependencies. Foundational types such as `ref<T>`, `ptr<T>` and `int32` are illustrative, not a complete mandatory API list. Language conformance alone does not require heap/GC, a scheduler or OS services; richer features retain their dependencies and checked contracts when used, with no silent semantic weakening or fallback. This can enable embedded/kernel environments in principle, but does not promise a backend there or universal support for every feature.

### Safe and unsafe boundaries

The [memory model](memory-model.md) defines the accepted access, optimization
and concurrency contracts. `unsafe` transfers proof obligations to the
developer; it does not change specified operations, imply `unchecked`, or
authorize arbitrary behavior when analysis discovers a violation. Existing
explicit checks remain. Optional diagnostics may identify misuse without
inserting repairs or changing the contract after inlining or specialization.

Mark caller obligations with `unsafe` on the callable declaration:
`unsafe void M()` propagates obligations to callers, while ordinary
`void M()` does not. This modifier does not make the callable body an unsafe
context: unsafe operations inside either form still require explicit
`unsafe { ... }` blocks, and calls to caller-unsafe declarations require an
explicit unsafe block at the call site. Encourage a `# safety` documentation
section to explain caller responsibilities; prose is informative, not a
compiler proof or a substitute for checked contract metadata. Safe declarations
may contain internally scoped unsafe work without an extra
trusted-implementation marker.
These are target directions. Current support for `unsafe` recovery is
documented in [implementation](implementation.md).

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
spelling. `init ref<T>` is an initialization loan to vacant storage: it cannot
be read before complete initialization and publication. The accepted call-side
form passes `init name` for vacant storage or declares a vacant local with
`init var name`. Conditional publication is distinct from ordinary writable
references and preserves the destination's complete type and cleanup
contract. Parameter-only `ref T`, `T*` and `T&` are not alternate
representations. `mut` is not valid as a generic type-argument qualifier;
use `write` when the referent capability itself is part of the type. See
[implementation](implementation.md) for the supported output modes.

The accepted call-side readonly-borrow form is the parameter-only modifier
`implicit ref<T>`, not `implicit T`, `in T`, or a qualifier inside `T` or
`ref<T>`. Callers pass ordinary expressions, evaluated once: an eligible
existing place is borrowed, otherwise a hidden local is initialized at that
argument's original evaluation point in the current enclosing lexical scope.
Its initialization is not hoisted to scope entry, and it is not wrapped in a
shorter lifetime-only scope. It follows ordinary local initialization, ownership/root
cleanup and unwind rules; if conditional or short-circuit evaluation skips the
argument, no local is initialized and no owner is created to clean up. The
introduced slot loan has the same semantics as an explicitly authored readonly
`ref<T>`, including declared result and backing dependencies. A valid returned
reference to a materialized local may be used by later statements in that same
scope. `implicit` adds no special noescape or suspension restriction, does not
widen an existing place's or backing value's lifetime, and grants no new
capture, suspension or escape permission: ordinary declared dependencies still
govern each use.

Borrowing imposes no `[Copy]` requirement or ownership
transfer just to borrow. The slot is readonly, but `T` keeps its complete
qualification and dependencies; for example, borrowing a descriptor does not
erase `write` authority or backing/root obligations inside it. This is not
deep immutability, purity or exclusivity. The modifier applies to concrete and
generic parameters and explicitly declared extension receivers, not as a
change to ordinary instance-receiver defaults. Existing APIs do not acquire
this behavior automatically; it requires an explicit parameter declaration.
The modifier does not apply to `write ref<T>`, `init ref<T>`, or `ptr<T>`
parameters and does not authorize arbitrary conversion chains. It is not a distinct overload from
an explicit readonly `ref<T>`; the priority between a by-value `T` overload
and an `implicit ref<T>` overload is settled: for ordinary `F(value)`,
`F(T)` wins when both are applicable. An explicit reference argument may
select a reference overload; explicit and implicit readonly `ref<T>` alone
cannot distinguish overloads. Physical ABI lowering preserves the declared
value/loan semantics; no size threshold or adaptive type attribute is implied.
No adaptive result family or change to default receiver semantics is selected.
Current support for this contract is documented in [implementation](implementation.md).

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
The layout-kind spellings below are illustrative, not compiler syntax. `Auto`
is the default: fields are private and external access uses properties or
methods; it grants no `Blittable`, packing or aggregate-alignment commitment.
`Sequential` places fields in declaration order; `Union` places every field at
offset zero. Neither kind permits arbitrary user-selected field offsets or
freezes its representation across versions.
`Transparent` is exactly one field and explicitly bundles the carrier
representation with carrier-equivalent argument/result classification for the
target profile. It inherits the carrier ABI and rejects custom packing and
aggregate alignment; this is an explicit contract, not layout-kind
auto-freezing.

`Blittable` is the accepted fixed, directly-interoperable representation
contract per target profile, covering size, alignment, offsets and transitively
compatible fields. `Blittable: true` is conceptual notation; exact attribute
and schema spelling remain specification work.
Ordinary `ref<T>` is excluded; `ptr<T>` may qualify subject to ordinary pointer
safety. The exact source spelling, target limits and metadata remain
specification work. Unfrozen fields remain private; a frozen representation
may expose fields without changing ordinary readonly or `mut` access rules.
`Blittable` does not imply `Copy`, atomicity, sharing, arbitrary-bit validity,
or a universal cross-platform wire format. Public field access requires the
fixed `Blittable` commitment, regardless of layout kind. Published
language-level signatures and their layout, access, ownership and effect
contracts are binary compatibility commitments even without foreign
interoperability; a public method does not need a foreign-layout annotation
merely because it is public. Layout-dependent values without a fixed
commitment cannot cross an independently replaceable dynamic boundary; they
can be used in layout-coupled compile/rebuild/invalidation scopes. JIT/AOT
labels alone do not decide whether such a boundary is coupled.

Both a power-of-two member-packing limit and a separate power-of-two
aggregate-alignment control are accepted and may be combined. Packing caps
member and derived aggregate alignment; the other control over-aligns the
aggregate, with tail padding preserving array stride. Exact argument spelling
and target limits remain to specify. Transparent inherits its carrier and
rejects either control. Initializing fields does not initialize padding; raw
object-byte blits and unmediated raw foreign transfer are unsafe. A safe
serializer must account for the initialization and representation of every
emitted byte, but may use verified or explicitly trusted raw copy. Details are
in the [layout contract](data-layout-and-abi.md#fixed-directly-interoperable-representations).

These are target design contracts, not blanket compiler support claims.
Transparent and sequential layout support boundaries are documented in
[implementation](implementation.md).
The provisional `[Layout(Sequential)]` spelling admits nonpublic compact
Copy structs with private fields and eligible nested Copy fields, using the
existing natural declaration-order layout (1–16 bytes). It does not publish
fields, freeze representation, or permit independently replaceable layout
boundaries; no semantic wire field or ABI migration is implied.
`ValueLayout` remains limited to naturally aligned 1–16-byte aggregates.
Existing non-transparent compact-value and module ABI contracts remain
authoritative. Broader enforcement requires an explicit compatibility
migration, not retroactive rejection or reinterpretation of existing artifacts.
Language/module compatibility is distinct from foreign ABI and serialization:
external wire or callback encodings are specified for concrete protocols, not
as one global boolean, reference or SIMD format.
Declared overlapping field storage is available only through an explicit
`[Layout(Union)]` declaration with the fixed `Blittable: true` commitment; it
is unsafe to access overlapping alternatives, with no active-member tracking
or special safe subset. This does not prohibit unsafe pointer-based
aliasing/reinterpretation or compiler-private overlapping representations. A
safe non-Blittable discriminated-union/sum type is a separate higher language
feature: case/tag checks must preserve ordinary validity, ownership, roots and
cleanup, and do not grant raw reinterpretation. Its grammar and representation
remain open.

### Interfaces, access and generic properties

For getter-only, by-value properties on compact `Copy` structs and source
classes, the accepted authoring direction uses an explicit accessor body with
accessor-local effects:

```terrum
public int32 Count
{
    [NoThrow] get { return _count; }
}
```

The getter is a named, source-bound callable member with a readonly receiver
and by-value result; property/accessor identity and effects are checked
metadata, not an inferred `get_Name` convention. Module visibility follows
the containing type's published representation and state contract. Setter,
auto-property and ref-return semantics are separate. The current supported
property subset is documented in [implementation](implementation.md).

`where T : IFoo` requires a checked behavioral witness; `where T : [Sized]`, `[Copy]`, `[ManagedStorable]`, `[ThreadTransferable]`, `[ThreadShareable]`, `[TextCodeUnit]` or `[NativeComInterface]` asks for a compiler-verified property of the complete qualified form, **not** implementation of an `I...` interface or the mere presence of an attribute. Thread capabilities describe which checked transfer or concurrent-use contracts a complete form can satisfy; they do not imply that a value is currently shared, authorize mutation, or perform synchronization. Thread confinement is the default but does not bind a value to one OS thread or executor. `[Sized]` does not promise a size constant in shared generic code. `[TextCodeUnit]` admits `char8`, `char16`, `char32`, not arbitrary valid encoded text. Native COM additionally requires checked IID, inherited slots, signatures, layout and lifetime. User shape generation can provide witnesses or adapters only after checking costs, ownership and ABI.

Thread-capability proofs can be derived from complete definitions and
dependencies, but consumer-visible guarantees come from declaration contracts
and producer-published rules, not incidental private layout. Generic bodies use
declared constraints and published conditional rules; machine-code
specialization cannot invent a missing source capability. A promised capability
cannot silently disappear when an implementation changes. Capability stability,
fixed layout and trivial-wrapper atomicity are separate contracts.

No universal root is required to supply `Equals`, `GetHashCode`, or
`ToString`, or to impose object-default boxing/allocation on every value; nor
is there automatic `IEquatable<T>`/`IFormattable` conformance. This rejects
universal object-to-string behavior, not explicit opt-in formatting protocols.
Primitive built-ins and compiler-verified operation capabilities remain valid;
no universal root is introduced merely to supply these operations. Use
operation-specific witnesses/interfaces and explicit adapters. Keep value
equality, identity where identity exists, key equality paired with a compatible
hash, machine-readable formatting, and display/debug rendering distinct.
Compiler and debugger type metadata does not require user-visible root methods,
and no contract requires every value to become a string.

Behavioral conformance is nominal and uses a checked witness; conditional
member guarantees are not themselves conformance. Conflicting type/interface
witness claims are rejected, not selected by import order. Types declare their
own ordinary implementations; importing a package does not retroactively add
a global conformance to an unrelated type. Explicit adapter types are the
intended way to add an extension-interface behavior set without changing the
original type's conformance.

An ABI-transparent nominal wrapper and an adapter are distinct contracts. For
example, `NeverNegativeInt32` could have the `int32` carrier ABI while allowing
only nonnegative values; identical bits do not make every `int32` a valid
wrapper value or permit arbitrary reinterpretation. Unused encodings may be
potential niches, but no niche encoding is selected by this direction.
`Int32Adapter` could carry an `int32` and explicitly implement `IExtra` without
making `int32` itself conform to `IExtra`. The type system may select the
original or adapter behavior set using separate dispatch tables/vtables or an
equivalent mechanism; it does not require two physical vtable pointers in each
value.

One-way `Original`-to-`Adapter` projections are a possible explicit contract,
including a possible `List<Original>`-to-`List<Adapter>` projection. They are
not blanket generic-container reinterpretation or writable covariance: each
projection must preserve validity, representation, access, ownership, lifetime
and container representation. A narrowing invariant such as
`NeverNegativeInt32` is not a total adapter merely because its payload bits
match. Exact adapter syntax, forwarding, conflicts, dispatch metadata and
projection eligibility remain specification work; `Blittable` is a separate
fixed-representation promise.

Applicable members outrank ordinary extension members; conflicting member
extensions may be explicitly qualified. Ordinary extension-member lookup does
not replace a nominal witness. An explicitly declared adapter is a separate
nominal type with its own extension-interface witness and leaves the original
type's conformance set unchanged.
Associated types and `Self` relations are intended for relationships such as
integer `Wider`/`Signed`/`Unsigned` types and SIMD element/mask types. One
selected witness binds an associated result; inference does not run backward
through non-injective projections or choose an arbitrary implementer from a
constraint. Multiple relations require explicit disambiguation.

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

An interface member without a body requires an implementation; one with a
body supplies a default. Neither needs an `abstract` source modifier in the
target grammar. A checked conformance maps each member's declaring-interface
identity to an explicit implementation if the type provides one, otherwise
to its available default. An explicit implementation may replace a default
without changing that identity or weakening its published signature, effects,
access, ownership or lifetime guarantees. Calls through the interface,
including calls made by a default body to another interface member, dispatch
through the selected witness. A same-named extension is not an override.
Static operator witnesses are selected without a runtime vtable; erased
instance dispatch may use a table/slot or equivalent, without prescribing a
physical slot for every default. Diamond conflicts require explicit
resolution, and compatible interface evolution requires a dispatch mechanism
that can bind older witnesses to a newly supplied default. Exact witness
mapping syntax, metadata and ABI remain to specify; current bounded
`static abstract` parser support is documented in [implementation](implementation.md).

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
Exact matches precede only previously permitted, declared projections or
erasures. Overload binding does not search implicit numeric or arbitrary type
conversions; permitted access/qualification weakening can affect match
quality (for example, `write Span<T>` to a readonly `Span<T>` parameter), but
does not infer a new or reversed family projection. Binding first determines
applicability, then compares each argument's match quality independently:
identity is better than permitted access/qualification weakening. One
candidate is better only when it is no worse for every argument and better
for at least one; crossed better/worse arguments are ambiguous rather than
combined into a score. Declared specificity resolves otherwise tied
applicable shapes, including by-value versus `implicit ref<T>` and the
accepted `params` normal/expanded rules. A fixed-arity non-expanded candidate
wins over an otherwise equal expanded `params` candidate. Constraints filter
applicability but do not create overloads differing only by constraints or
require arbitrary logical subsumption; return-only overloads are not
introduced. Declaration order, return type, and literal numeric fit do not
break ties. Applicable members beat extensions. Thus a sole
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
The broad inference order above is target direction, not a claim that general
inference is implemented. Typed arguments and explicit type arguments establish
their positions before result context fills unresolved positions; contextual
literals are checked only after their target type is known. An outer result
context cannot retarget a typed argument, and a literal does not implicitly
construct a struct or supply a default type. Definition-site constraints and
effects remain authoritative for each independently inferred parameter; a
caller cannot lend stronger constraints to a template. A `write C` argument
fixes its parameter as `write C` before result-context fallback, and any
permitted result weakening does not retarget that parameter. Generic
signature lookup is distinct from type-argument inference. Constructor
inference, partial holes, general overload inference and public generic ABI
remain separate design work. The bounded inference profile and its exact
limits are documented in [implementation](implementation.md#bounded-generic-functions).

Operator protocols are explicitly declared and constrained; homogeneous
operands require the applicable conformance. Operator spelling does not imply
`[NoThrow]`, derive complements or ordering laws, or establish universal
equality, hashing or managed identity policy. Unary result types are declared,
not inferred from operator spelling; `!` is not inherently bool-result, and
result type alone does not distinguish overloads. Primitive and heterogeneous
witnesses, nullable operands and public generic ABI are separate contracts.
See [implementation](implementation.md) for the bounded operator subset.
Static `++`/`--` have operand-shaped results rather than an arbitrary result
type; this does not import C# inheritance. `true` and
`false` are the explicit exception: both return `bool` and are declared
together. C# 14's instance-mutating `++`/`--` and `void` compound-assignment
operators are not adopted here. `++`/`--`, `true`/`false` and shift
protocols remain unsupported.

`_` is reserved and never names an identifier. `_ = expression;` is the
accepted standalone discard statement: evaluate the expression once, preserve
its effects and failures, and apply ordinary ownership/cleanup. It is not an
assignment and has no name-dependent fallback. Positional wildcard patterns
and omitted lambda parameter names are separate nonbinding contexts; they do
not drop the matched/argument value or alter parameter mode. Separately
proposed type holes are not identifiers and are not covered by this discard
rule. Exact grammar contexts beyond the accepted discard direction remain to
specify. Wildcard patterns, lambda placeholders and generic type holes are
separate features; see the [implementation boundary](implementation.md).

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
through a hidden `int32`/`float64`. `int32(1234)`, `uint64(0xFFFF_FFFF)` and
`float32(0.1)` illustrate direct target parsing. Width suffixes do not exist.
`T(untypedLiteral)` uses the registered parser directly; `T(typedExpression)`
is a checked explicit conversion. `new T(arguments)` remains nominal
construction. Already-typed numbers never implicitly widen or narrow.
Contextual decimal, hexadecimal and binary integer literals have no fallback
type, and numeric type-call conversions are checked. Type/value-call binding
preserves existing source-function and local shadowing/call precedence.
Current executable coverage, including floating literals, is documented in
[implementation](implementation.md).

Decimal integer literals keep numeric range semantics, including the direct
negative signed-minimum case. Hexadecimal and binary literals instead
construct the expected target width as a bit pattern: `int32(0x8000_0000)` is
`int32.MinValue`, `int32(0xFFFF_FFFF)` is `-1`, and `int64(0xFFFF_FFFF)` is
`4294967295`. Nonzero bits above the target width are rejected; leading zeroes
and separators do not add significant bits. Once constructed, the value is
typed normally and subsequent conversions remain checked:
`int32(uint32(0xFFFF_FFFF))` fails, while
`int64(int32(0xFFFF_FFFF))` yields `-1`. Unary minus on a based literal is
ordinary checked arithmetic: with an `int32` context, `-0x8000_0000` fails and
`-0xFFFF_FFFF` produces `1`; an `int64` context gives different initial bits
and therefore different results. Binary literals use `0b`/`0B`; `_` runs may
separate digits or follow a based prefix, but cannot trail a digit run or
attach to a decimal point, exponent marker or sign.

`[ConstEval]` marks declarations eligible for bounded deterministic
compile-time execution; `const T a = F()`
requires a constant declaration value, while `T b = const F()` requires only
the expression. `const F() + x` requires
only `F()`, while `const (F() + x)` requires the whole expression. Literal
parsing always uses the registered compile-time parser, even in ordinary local
initialization; there is no runtime fallback. Current evaluator support is
documented in [implementation](implementation.md).
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
or flush dependence; arithmetic NaNs canonicalize in the working profile.
Arithmetic NaN canonicalization does not apply to bitcasts or copies, which
preserve bits under their representation contracts. Representation-level
`FromBits`/`ToBits` operations have their own bit contracts.
Floating-to-floating conversions follow IEEE-754:
inexactness, finite overflow to infinity, underflow, NaN and infinity do not
raise language failures, and no floating-point exception traps are enabled.
Other rounding modes require explicit APIs/adapters; there is no global
floating-point control-word API. Checked floating-to-integer conversions
truncate toward zero then throw if out of range; unchecked conversions truncate
then saturate rather than wrap, with NaN converting to zero.
[Conversion policies](conversions.md) distinguish range, fractional rounding,
integer wrapping, nonfinite handling and bit reinterpretation; floating
conversion does not promise cross-format NaN-payload preservation or exactness.
Numeric-key comparer semantics (all NaNs equal, signed zeros equal, decimal
cohorts normalized) are separate from IEEE operator equality.

Source text is UTF-8; runtime text has **no default encoding**. The single `TextSpan<TCodeUnit = void>` family has immutable validated `char8`/`char16`/`char32` members and a family-declared encoding-erased `void` specialization: bare `TextSpan` is exactly `TextSpan<void>`. Here `void` does not mean zero-size units or a default encoding. Erasure needs no text copy/transcoding and retains actual encoding, backing, access and lifetime/lease dependencies. Checked typed projection rejects a mismatched representation rather than transcoding; only known-code-unit forms expose typed `AsSpan` and contiguous-sequence conformance. Common `IsEmpty`, ordinal and formatting operations work on either form. A raw `AsSpan()` can forget text validity but not immutable backing, access, lifetime or rooting. Raw slices can split scalars; text slices must preserve scalar boundaries. Editing copies into mutable raw storage or a builder, then validates and freezes before publication; transferring an exclusively held initial buffer may avoid a copy only by revoking all writes. Readonly aliasing or validation alone cannot establish immutable text. Registered string targets use ordinary calls such as `utf8z("Terrum")`, not lexer prefixes or suffix tokens; the target validates encoding, termination and embedded-zero rules without implying pointer lifetime. Final storage, string facade, adaptive/block/RLE representation and projection ABI require experiments.

Design-only character literals carry one scalar, target-typed to a representable code-unit/scalar type without a numeric or UTF-16 fallback. Registered text targets provide `rune` context and forward scalar fragments directly through the writer protocol. A raw `char8` is not automatically a valid standalone UTF-8 scalar. Precise representability rules and the current unsupported-syntax boundary are in [conversions](conversions.md#character-intent).

Defaults are **ordinal plus invariant together**: exact Unicode scalar sequence equality and scalar ordering across encodings, matching hash, no implicit normalization/casing/linguistic comparison, and ASCII digits/signs/punctuation for numeric parsing and formatting. U+0661/U+FF11 digits and U+2212 minus do not act like ASCII tokens. Arbitrary valid Unicode remains valid text data. Termination, embedded-zero exclusion, pinning and lifetime are distinct native-string requirements. `Text` is a common payload/facade. The target sketch uses `own<val<Text>>` when it means a native owner of inline `Text` payload; construction, conversion and cleanup syntax remain open. Text implementation support is documented in [implementation](implementation.md).

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

Precedence builds the expression tree; it does not reorder evaluation.
Ordinary eager operators evaluate their left operand before their right
operand, then apply the operator; short-circuit and conditional operators
evaluate only selected operands. Evaluate a callable/receiver before its
arguments and stage the arguments in source order, regardless of labels.
Place formation evaluates receivers, indices and checks once at its position.
If a later argument fails before invocation, the caller cleans completed
staged owners on defined unwinding; moved sources do not become live again.
Ownership transfers at the invocation boundary, and an output destination is
not cleared while earlier arguments are still being evaluated.

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
conversion. An event/subscription abstraction, if supplied, is an explicit
library collection/dispatch contract, not a language-level multicast event
feature. It defines ordering, refusal, exceptions, reentrancy, subscription
mutation/cleanup, and handling of every result rather than silently returning
only the last. Contextual lambdas preserve the expected callable or functor
representation, and a capturing lambda is not implicitly a `fnptr`. Generic
functors use declared symbolic constraints and specialization; ordinary
scalar callbacks do not imply SIMD implementations or automatic vectorization.

`params Collection<T>` and common `params Span<T>` forms use ordinary checked
collection construction and element copy/move rules. Expanded values occupy
new slots rather than aliasing caller locals; `params` adds no by-reference
parameter mode, though explicit reference types may be elements where normal
storage and lifetime rules permit them. `params` modifies the collection
parameter's call form; it is not a different element or parameter type. An
argument matching the declared collection binds directly in normal form;
expanded form gathers individual arguments into a new collection. Resolve
overloads by ordinary match quality and specificity first; a fixed-arity,
non-expanded candidate wins an otherwise equal match over expanded `params`,
not regardless of conversion quality. Accepted ranking compares candidates
by per-argument dominance: identity is better than permitted
access/qualification weakening, and crossed better/worse arguments are
ambiguous rather than summed into a score. Apply declared specificity and the
by-value/`implicit ref<T>`/`params` priorities. Return type, declaration
order, literal numeric fit and conversion-chain search do not resolve a tie.
Exact grammar, diagnostics and generic collection-expression inference remain
specification work. Binding is fixed at source compilation; a backend does not
choose among overloads. For
example, given `Log<T>(T item)` and `Log<T>(params List<T> items)`, a
`List<int32>` argument selects the `List<T>` overload: both infer one `T` and
have identity matches after inference, but the declared `List<T>` parameter
shape is more specific than bare `T`. This same specificity applies without
the `params` modifier; it is not a count of inferred parameters or solver work.

These are target contracts. Callable and tuple syntax, checked witnesses and
contextual lambda lowering remain open; user-defined type-pack expansion,
pack indexing/folds, polymorphic callable erasure and implicit callback
vectorization are outside this direction. Current compiler support is
documented in [implementation](implementation.md).

### Construction, errors and effects

Ordinary explicit constructors are baseline. Independent opt-in `Generate` operations can supply construction, equality/hash, deconstruction, formatting and combined validated withers from declared storage/mappings; no field-name magic, automatic primary-constructor capture, implicit successive setters or parameter-name-only overload distinctions. Omitted update members are `Unchanged`, not a null/default value. Affine and class withers need explicit failure, cleanup, allocation and identity behavior.

The target inference order also applies to constructors: known argument types
establish generic positions, expected-result context may fill unresolved ones,
and contextual literals follow. `new DifferentRandom(new CounterRandom(0),
0xA5A5_A5A5)` illustrates first-argument inference and declared `uint32`
literal contexts; it does not infer field capture, change ownership, choose a
type from constraints or invert optional/box normalization. General constructor
inference remains separate target work; current inference support is documented
in [implementation](implementation.md).

The target parser contract is `IParsable<TSelf>.TryParse(TextSpan, init ref<TSelf>)`, including user types. `init ref<T>` is a vacant initialization loan: its destination cannot be read before complete initialization and publication. The accepted call-site form is `TryParse(text, init result)`. Conditional initialization facts are normally authored as declaration postconditions using `ensure <expr>`, an ordinary Boolean expression that may compose patterns and short-circuit predicates. A legal value predicate such as `result > 0` implies initialization; source-body/path checking must prove reads are legal and may not inspect vacant storage to decide a predicate's result. A successful value predicate establishes initialization. These clauses correlate the output's live/uninitialized state with the declared status/result; they are not a separate conditional-assertion block language. An initialized nullable value (including null) is a live `T` value; an uninitialized outcome has no `T` lifetime, access or cleanup, even if physical storage remains. Callers cannot receive an undiscriminated maybe-live owner or access/clean up a vacant arm. Exact clause placement, grammar and metadata remain to be specified; equivalent routine output attributes are not a parallel authoring mechanism. Failure cleans partial initialization according to the declared contract and never fabricates `default(T)`; asynchronous publication occurs at operation completion. This contract is distinct from a general writable reference. Expected invalid input normally branches on `Try...`/status. Domain outcomes may use response types; there is no mandatory language-wide `Option`/`Result` wrapper or typed-throws failure-set machinery. Current output-mode support is documented in [implementation](implementation.md).

The language-versioned boolean-success default initializes the output on
`true` and leaves it uninitialized on `false`. For other result types, an
`init ref<T>` output must be initialized on every normal return unless an
explicit declaration postcondition states a conditional relationship. These
defaults do not infer success from method names.
Status, initialization and nullability are independent: an initialized null
is a live optional value, while vacant storage cannot be tested even for null.
The caller retains a checked correlation between one call's saved
discriminator and its outputs; reassignment of that status, replacement or
movement of the destination, and relevant aliasing effects invalidate
dependent facts. Domain statuses use declared predicates, not the zero
enumerator or a name guessed from the API. The implementation must completely
initialize every promised live output; a declaration cannot create a value
merely by asserting its existence.

`require predicate;` is a safe user-facing checked boolean requirement: evaluate
the predicate once, continue on true, and raise the selected requirement failure
on false **in every build**. It accepts general predicates (`require
format.IsEmpty;`); for example, `require<FormatException>
int32.TryParse(text)` selects an exception only for a false predicate, not for
exceptions thrown while evaluating it. A declaration may provide a
default failure type for brevity, while an explicit call-site selection wins;
the annotation spelling and exception-construction contract remain open.
Common null/range conditions may receive recognized diagnostics, but checks do
not infer meaning from arbitrary method names or reevaluate arguments,
properties or user-defined comparisons. The implicit-destination expression
form below follows the same declared output contract; current support for
statement and expression forms is documented in [implementation](implementation.md).

The expression form can lift one declared success-conditioned output:
`int32 value = require int32.TryParse(text);`. The operation runs once; success
publishes the complete value, while false cleans partial initialization and
raises rather than exposing a vacant destination or manufacturing `default(T)`.
This is based on declaration metadata, not a `Try` naming convention. Multiple
outputs stay explicit; status APIs use an explicit declared success predicate
and preserve other statuses for explicit matching/diagnostics rather than
guessing an enum-zero member. Pattern and `init ref` invocation grammar remain
scheduled.

Declaration placement determines contract visibility. A declaration
precondition (`require`) and postcondition (`ensure`) are published promises;
they are executable unless proven redundant, and strengthening preconditions
or weakening postconditions breaks compatibility. Published clauses use a
checked, side-effect-free contract language; body `require` retains general
executable predicates and is an ordinary point check, not an inferred public
precondition. Body-only `static
require` proves a condition at that point and `static ensure` proves the outer
function's postcondition on every normal return; neither is automatically
published. Static forms are compile-time-only, side-effect-free proof checks
in every build. False or unproven required proofs are errors, never runtime
fallbacks or optimizer assumptions. They may use runtime values symbolically,
unlike constant evaluation, which must produce a value. `return` in a result
postcondition denotes the actual result contextually; it is not a new binder
or lambda and does not reevaluate the return expression. A `void` function has
no result-valued return. Static postconditions initially apply only to the
outer function body. Proofs respect mutation/alias invalidation.
The exact supported proof forms and resource limits are documented in
[implementation](implementation.md). Static proofs do not execute or publish
a contract; symbolic runtime-dependent proofs, declaration clauses and result
references remain separate design work.

Required static proofs have a versioned portable floor: every conforming
compiler must support its minimum rules/complexity and guaranteed resource
budget independently of optimization level. A compiler may soundly establish
more complex proofs or offer larger budgets, accepting additional programs;
acceptance beyond the floor need not be identical. If a required proof is
disproved or not established, compilation fails with the distinction reported
(including a complexity/analysis-limit reason where applicable), never a
runtime fallback. Ordinary pre/postconditions remain runtime-checked unless
sound analysis proves a check redundant, and elimination must preserve required
evaluation, effects and failures. Required constant evaluation is separate:
successful results retain exact target semantics and never fall back to
runtime; its supported operations, accounting and limits remain specification
work.

Where runtime values or checks are permitted, an author may explicitly choose
an ordinary expression or executable `require` instead of requiring constant
evaluation or static proof. This changes the source requirement; the compiler
never silently demotes it. Ordinary expressions may still be folded as sound
optimization, and an ordinary `require` remains runtime-enforced unless
proven redundant. This choice is unavailable where a compile-time value is
inherently required, such as layout, type or metadata formation, and does not
weaken structural lifetime or initialization safety. Illustrative syntax does
not select the exact spelling.

The contract phase boundary is `prologue -> preconditions -> method body ->
postconditions -> epilogue`. Preconditions and postconditions are public
declaration/signature contracts outside the authored body. The body includes
initialization of local and output values, authored execution and `finally`,
local cleanup, and cleanup of untransferred owned parameters; all complete before
postconditions. On successful return, no user-observable ownership cleanup is
deferred to the mechanical epilogue. Postconditions can inspect surviving
values and retained results, but not moved or destroyed values through their
former sources. Exceptional departure has no normal-return postcondition.
By-value owned arguments already belong to the callee when its preconditions
execute. If one fails, defined unwinding cleans those arguments without
restoring moved caller variables; an unentered body `finally` does not run.
A return expression is evaluated once and its result retained through body
cleanup. A copied handle or returned reference may observe a backing change
made by a `finally` before its postcondition is checked. A postcondition that
fails or throws starts ordinary failure handling at that phase; completed
body cleanup and `finally` do not run twice, and no body effects are rolled
back. Async postconditions apply before successful completion is published,
not merely when a task handle is returned.
Entry snapshots and arbitrary state-history contracts are deliberately absent
from the initial model, with no promise to add them later. Postconditions may
state observable boundary facts about initialization, nullability, ranges and
relationships to unchanged value inputs; these are behavioral guarantees, not
merely optimizer hints. Historical relations such as a count increasing by
one require state-history support and remain outside the initial model.
Proving or folding predicates may optimize enforcement only when legally
redundant and without losing observable evaluation or effects. Clauses may
refer only to declarations visible at the
method's effective accessibility and expressions permitted by the ordinary
contract language: an accessible public `Count` abstraction may be used when
ordinary evaluation permits it, but a private representation invariant cannot
become a hidden caller precondition. Caller facts derive from published
guarantees checked or proved for the callee, never private-body inference, and
are invalidated by relevant mutation, alias effects or lifetime changes. This
adds no getter-purity syntax; proving or folding a predicate does not replace
the public guarantee. Initialization safety is static and never checks vacant
storage by reading it. Postconditions are ordinary code at their declared
phase: failure follows ordinary unwinding, with no transactional rollback or
rerun of completed `finally` blocks. This does not add a separate
output-publication protocol.

The guaranteed static-proof floor is available independently of optimization
level; beyond-floor proof support may vary, but must remain sound. A
proven-to-fail ordinary guard produces a warning by default,
promotable to an error; an intentionally failing `require` remains a valid
path when that diagnostic is not promoted. Failed required static/constant
proofs diagnose. Removing runtime guards preserves required evaluation,
effects, exceptions, declared output-initialization outcomes and cleanup.
Neither `NoThrow` nor another public promise
is inferred from a conditional body check.

`require` does not need an unsafe context. Catching/recovery does; `finally`
for cleanup does not. Unsafe recovery does not prove an object's invariant
survived. Fail-fast termination promises no cleanup, so durable-state
protocols must tolerate abrupt termination/restart; `finally` and destructors
are not crash-consistency mechanisms. On defined unwinding paths, ownership,
conditional publication and promised `NoThrow` cleanup still apply.

Ordinary boolean requirements and conditional outputs preserve their declared
cleanup behavior. Fail-fast and checked-exception policies govern how a failure
propagates; neither permits reading or publishing an uninitialized output.
Current support for declaration clauses, static proofs, typed failure
selection, exception construction and output forms is documented in
[implementation](implementation.md).

Formatting uses one `IFormattable` → `ITextWriter` protocol with typed fragments/values, no obligatory allocating formatter per type. Target-typed interpolation and collection expressions select registered handler types, such as `[TextBuilder(typeof(TextBuilder))]` or `[CollectionBuilder(typeof(ListBuilder<>))]`, not arbitrary similarly named methods. Holes evaluate once left-to-right into typed captures before segments drain; ownership, borrowing and refusal cleanup remain explicit. Named unique transfers require `move`. Nested formatting forwards the same writer so a Point and its scalar fields can reach a custom handler without temporary strings or boxing. Refusal stops future writes but cannot undo prior output or hole evaluation; no replay, rollback or transactional stream guarantee. Expression handlers may consume an explicitly supported typed operation graph, as in `Tensor.Apply(destination, i => x[i] * y[i] + z[i])`; this is not body-divined autovectorization of an ordinary callback. Unsupported expressions diagnose or require a separately declared fallback. Owning text publishes only after full validation/freeze and cleans partial storage. Collection spreads enumerate once in order; builders own accepted elements, clean rejected consumed elements and abandon partial state exactly once. Detailed standard-library APIs and resumable async formatting are specified separately. Synchronous formatter refusal is not a resumable continuation.

Allocation, GC, rooted reads/writes, blocking, suspension, I/O, atomics, throwing/no-throw, normal/no-return, cleanup and retirement are independently expressible effect bounds, finer-grained than Core/Foundation/Application dependency layers. `void` is the single zero-data result type, with value `()`, usable as a zero-argument `func<void>` and `Task<void>`; `Option<void>` still represents two states. A checked `[DoesNotReturn]` on a `void` call removes its normal continuation but retains exception/cleanup edges; `[NoThrow]` is separate. A verified implementation cannot normally return, including by completing a task that terminates *later*. There is no public `unit` or `never` primitive; the compiler may use internal bottom flow. Removing an exported no-return guarantee breaks callers that rely on it.

Ownership transfer and cleanup are explicit: a moved owner becomes vacant,
fresh results may transfer directly, and partial construction cleans acquired
fields in reverse acquisition order. Receiver loans do not transfer ownership;
mutation preserves the owner's cleanup obligation. Actual native-profile
coverage and failure policies are documented in [implementation](implementation.md)
and [native](native.md).

### Documentation comments

Declaration-attached `///` before attributes/modifiers and `/** ... */`
comments use GFM-compatible prose. Leading prose before the first recognized
metadata heading is the implicit summary, with optional `# summary`.
Top-level `# remarks`, `# param name`, `# typeparam name`, `# returns`,
`# value`, `# throws Type`, `# safety` and `# example` headings introduce
structured sections; lower-level headings remain prose. Blank lines remain
within the current section, including the implicit summary; each structured
section continues to the next recognized metadata heading. Recognize headings
from parsed Markdown after removing comment decoration and common indentation,
never from raw-line prefixes inside code, quotes or lists.

Declaration-scoped `@value`, `@T` and `@Type.Member` mentions resolve to
checked references, not inside code, URLs or email addresses; `\@` escapes a
literal at-sign. Ordinary inline and reference-style Markdown links retain
their navigation meaning, not implicit symbol-reference meaning; explicit
`xref:` links address qualified/custom references. Backticks denote code
rather than implicit links. Explicit XML `<see cref="..."/>` and
`<paramref name="..."/>` remain available as structured references, with
ordinary XML attribute escaping such as `&lt;` and `&gt;` for generic
arguments.
Documentation compiles to a separate structured XML artifact; changing
prose alone does not alter executable ABI. It cannot create access, effect,
output, lifetime or failure contracts: those derive from checked
declarations, which generated API documentation should expose. A `# safety`
section on an `unsafe` callable is informative and cannot replace its checked
contract or caller obligations. Inherited prose cannot strengthen an
implementation's checked guarantees. Extraction is deterministic and does not
execute examples, fetch remote content or expose private declarations just to
resolve a link; a separately configured documentation build may opt into
example checks. Exact tokenization and escaping, unknown-heading behavior,
attachment and inheritance details, member identities, schema, diagnostics
and packaging remain specification work in the
[grammar proposal](proposals/grammar-spec.md).

### Storage, concurrency, entry and boundaries

Tracing GC handles aliased/cyclic application graphs; ownership handles deterministic native storage and resources. A unique owner may still refer to GC storage; a GC root does not keep a logically disposed native allocation valid. Distinguish storage acquisition, initialization, close, value destruction, storage release and eventual reclamation. Vacant/partially initialized/live/retired states, reverse-order partial cleanup, no-throw/non-suspending scope cleanup, direct destination construction and stable-address requirements must be checked. Replacing a live value builds the replacement before dropping the old one; explicit reconstruct-in-place may leave vacancy on failure. Pool leases revoke access on return; deferred dead-storage retirement differs from deferred live-value cleanup, with one retirement owner after commitment and explicit completion endpoints.

Values are thread-confined by default, not bound to a fixed OS thread or
executor. `[ThreadTransferable]` describes a complete form that may cross a
thread boundary under its checked transfer contract; it does not imply
concurrent sharing. `[ThreadShareable]` describes a complete form whose
declared access protocol permits concurrent use; it does not itself create
concurrent aliases, grant mutation authority, or perform synchronization. Neither
capability follows from GC, `[Copy]` or readonly. Reader/writer cardinality
belongs to the checked access protocol, not a separate trait family. Ordinary
access remains ordinary access: it does not turn `x++` into an atomic
read-modify-write. Confinement or an exclusive guard can permit an ordinary
increment; a potentially competing path needs an explicitly permitted
operation or guard. A checked `ListWriteAccess` guard arbitrates *all*
aliases/reentrancy before mutable span access, without silently registering
failed static loans; it is not by itself a cross-thread mutex.
A managed `LinkedList<T>` retains node identity through
remove/reinsert; `Clear` detaches even externally held nodes. Wrong-list/
preflight failures leave the structure unchanged; enumeration fails after
structural mutations, not mere payload replacement. Common collection
semantics and examples are in [samples](samples.md) and [libraries](libraries.md).

Asynchronous operations return task-like completion, including `Task<void>` for
no result; synchronous `void` remains valid, but `async void` is not permitted.
An async function call starts eagerly and runs inline on the calling flow until
it actually suspends. Awaiting an already-completed operation continues inline;
awaiting an incomplete operation suspends and resumes according to the
operation/runtime scheduling policy, without implicitly capturing the caller's
executor or requiring a worker-pool hop.
Async declaration postconditions are checked before successful task completion
is published, not merely when a task handle is returned.
The accepted terminology keeps three properties separate: asynchronous work
can happen independently of the main program flow, and may or may not be
concurrent; concurrency means a system can execute multiple flows of code, and
may or may not be parallel; parallelism is the ability to execute multiple
flows simultaneously. These definitions do not imply that async work is queued,
uses a thread/thread pool, always suspends, captures a scheduler, or is
concurrent or parallel.
Default confinement can keep state on one serialized flow when checked
transfer rules permit it; it does not erase backing lifetimes, outstanding
aliases, nontransferability, affinity or cleanup obligations. A function or
callback with declared executor affinity retains its own placement across
suspension, including cleanup and unwinding. An otherwise-unbound helper does
not inherit UI affinity merely because it was called from UI code.
Every started operation retains a responsible owner: a structured
scope that joins it, an explicit transfer to another responsible owner, or a
supervisor with error, lifetime and shutdown policy. Returning a task alone is
not sufficient when no caller/owner remains responsible, and discarding a task
does not cancel work or discharge completion/error obligations. Cancellation
is a request, not completion; it does not release borrowed backing before the
operation actually stops and completes required cleanup. A task cannot
legalize a loan escaping its backing's permitted lifetime. Entry conventions
include `Main` returning `void`/`int32` or `MainAsync` returning `Task`/
`Task<int32>`, with no arguments or `Span<TextSpan> args` excluding the program
name. Host-selected arguments/rooting last through async completion and
structured cleanup. `async` describes implementation, `Async` describes API
behavior, and no implicit blocking bridge is inserted. An explicit `Run`
schedules an initial invocation and represents its full async completion, but
does not bind all helper continuations to that executor or select the caller's
continuation, nor does it guarantee that the caller suspends. An explicit
awaitable adapter such as `ResumeOn(executor)` selects the awaiting
continuation, including when completion or failure has already occurred. It
dispatches when the awaiting flow is not already on that executor and may
continue inline when it is; fault/unwinding follows the selected continuation
policy. It does not reschedule the producer. An explicit `Yield` can request a
scheduling boundary, but does not promise fairness. Executor choice never
grants unsafe access or bypasses checked transfer, sharing, lifetime or
affinity rules. These are semantic directions, not exact library signatures
or an implemented runtime; API spelling and affinity annotations remain
specification work.
These contracts do not mandate actors. `Terminal.Write`/`WriteLine` default to
standard output; `output: TerminalOutput.Error` selects stderr, with writer
access for composition. Redirected streams work without an interactive
terminal; terminal controls require endpoint capabilities.

The execution-policy model does not imply a thread pool: synchronous
continuation is possible, logical serialized authority need not mean one OS
thread, and nontransferable/affine state must never migrate unsafely. Explicit
affinity obligations remain attached to the declared function or callback,
not imposed on every internal continuation. Exact scheduler/awaiter syntax,
propagation, boundary validation and runtime support remain to be specified
and implemented.

Reflection respects visibility, access, lifetime and trimming. Symbol-resolved tokens and compiler-checked `[ConstantExpected]` names (including through wrappers) support safe bounded lookup; open-ended runtime discovery or private access needs a *different* privileged unsafe API, explicit authorization and retained targets. Retention of layout, executable bodies, descriptors and debugging data is independent. Author-authorized deterministic generation can inspect its own defining module's private members but cannot grant consumers friendship or bypass normal checks. Foreign interop uses source or generated tooling for marshalling; the compiler/runtime provide checked native symbols, calling conventions, representations, ownership, callback-lifetime and exception-containment prerequisites for safe imported-C APIs, not built-in value marshalling. COM requires exact profiles, not behavioral-interface resemblance.

The target portable semantic representation carries typed control flow, values
and memory places, cleanup regions, roots, checked operations, construction and
suspension. It is not CLI IL. Source compilers check authored bodies against
contracts; binary consumers validate structure, references, binding and caller
use, then rely on published dependency contracts rather than re-proving bodies.
Contract-bound and implementation-pinned replacement are distinct from static
and dynamic linkage; replacement must preserve capture, output, access, layout
and effect guarantees. The `TRRM` identifier names the evolving semantic-IL and
module format; its wire schema and revision encoding remain open. Actual module
and backend support is documented in [implementation](implementation.md).

## Provisional source notation and open decisions

The design examples use canonical `ref<T>`, `ptr<T>`, `box<T>`, `val<T>`,
`own<T>`, `init ref<T>`, `mut`, and `write` forms, plus `move`, bracketed
checked predicates, `[ViewOf]`, `[Captures]`, `[Pure]`,
`[MatchedAccess]`, `[Layout(Transparent)]`, `require`, `[TextTarget]`,
`[TextBuilder(typeof(...))]`, `[CollectionBuilder(typeof(...))]`,
`[Literal(...)]`, `[ConstEval]`, `[Specialize(...)]`, `[NativeExport]`,
`utf8z("...")`/`utf16z("...")` calls and generic trailing defaults as
**illustrations**, not implementation promises. Attached width suffixes diagnose
as `TRM0006`; prefix casts are not parsed. In particular, legacy `out` remains
supported while a coherent general migration to initialization loans is
planned. Current support for numeric conversions and initialization loans is
documented in [implementation](implementation.md).
Exact generic constraint,
associated-type and extension-disambiguation syntax, inference-hole spelling,
closed-case marker/match grammar and compatibility-forwarder attributes/source
visibility remain open. So do transparent-wrapper eligibility/ABI profiles,
conditional copy capabilities, first-class
stored-loan ABI, erased-interface dispatch ABI, span-anchor layout/calling
convention, inline `val<C>`, native ownership transitions, structural unions,
decimal encoding, text storage, wither omission/overloads,
handling/registration, task lifecycle/cancellation APIs, generic dispatch
selectors and exception ABI. Eager async invocation and ordinary-await
continuation semantics are accepted; the exact awaiter, executor and affinity
syntax remain open.
Syntax follows checked contracts; actual implementation boundaries are listed
in [implementation](implementation.md) and staged in [roadmap](roadmap.md).

## Implementation status

Syntax-only acceptance does not establish semantic validity. Native builds
must diagnose unsupported constructs rather than silently weaken their
contracts. See [implementation](implementation.md) and [native](native.md) for
actual support, [roadmap](roadmap.md) for staging, and [samples](samples.md)
for runnable examples versus broader design sketches.

## Sources and prior art

[TerraFX conventions](https://github.com/terrafx/terrafx/blob/9def09f6c24a0465035ce004ab240ecce0a97a04/.editorconfig) and [.NET Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/) inform naming, not copied restrictions. [C# scoped/ref escape rules](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/low-level-struct-improvements.md), [C# 13 ref-struct interfaces](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md), [collection builders](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions), [interpolated handlers](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/interpolated-string-handler), [ECMA-335](https://ecma-international.org/publications-and-standards/standards/ecma-335/), [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) and [Arm sizeless types](https://arm-software.github.io/acle/main/acle.html#sizeless-types) are comparison points, not automatic adoption. The current C#/.NET previews are backend/prior-art candidates subject to semantic and ABI verification.
