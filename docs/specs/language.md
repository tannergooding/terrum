# Terrum language design

Terrum (TAIR-um), from TerraFX, is a C#-shaped language for applications through systems code. This document specifies accepted semantic direction; the [source grammar](grammar-spec.md) defines approved source forms and explicitly open boundaries. Design examples remain informative, not blanket approval of their library APIs. Detailed call, output and checking contracts are in [language contracts](language-contracts.md). Actual compiler and runtime support is documented in [implementation](../implementation.md), its staging in [roadmap](../roadmap.md), runnable examples in [samples](../samples.md), accepted library direction in [libraries](libraries.md), and candidate API declarations in the [core-library proposal](../proposals/core-library-reference.md).

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

- The lowest `language` profile is the selected compilation/conformance floor, with a closed reserved-primitive binding catalog and explicit feature dependencies. Core is a higher, scoped generally useful library surface, not the language floor; Foundation/Application and ecosystem packages add their own facilities. Exact inventories remain separately selected, and embedded/kernel/application environments need not provide every optional feature. See [dependency layers](libraries.md#dependency-layers-and-package-boundaries); these names do not introduce compiler CLI profiles or grant primitive-definition authority.
- C#/.NET application and API ergonomics are the baseline; ownership, access, representation, lifetime, effects and placement are distinct contracts. An application can use managed cyclic graphs without propagating lifetime parameters through every edge; unique native resources and deterministic cleanup remain nonduplicable. There is no implicit copy, boxing, pinning, retain, synchronization or materialization to repair an invalid program.
- AOT and JIT must implement the same semantics for supported features. Independently serviced modules, DLLs and plugins require published, versioned contracts and binding-time compatibility; closed-world optimization or implementation pinning is explicit. Neither a compiler host nor a particular backend defines Terrum's type system, semantic IL or permanent ABI; current target support is documented in [implementation](../implementation.md).
- Concrete types are sealed by default; `sealed` prohibits inheritance, not interface dispatch or API evolution. Types are evolvable by default: closed case sets, fixed layouts and frozen APIs are separate scoped contracts. Interface inheritance and composition replace general concrete inheritance. Public namespaces express concepts, not library tiers. Source visibility includes `public`, module `internal`, declaring-type `private`, strict source-unit `file`, and member-scoped `friend<F, G>`. Namespace/file-level types, functions and aliases permit public/internal/file, not private/friend. Another module's namespace spelling, unsafe authorization or linking creates no protected-family or friend-module access. Author-approved generation does not bypass the selected source-unit/type access domain. A public Terrum surface is not automatically a native export.
- File access does not include other partial parts in different compiler source units. Same-named file-local definitions in distinct units retain distinct identities through an explicit module-owned semantic scope discriminator, not paths or name mangling. Same-scope collisions diagnose. `friend<F>` means private access plus the listed type's own code only, without transitivity, implementer/extension privileges or access to other private members. Exact generic friends substitute owner arguments; explicit `_` grants one-slot wildcard matching, not inference or `var` filtering. Grants preserve containing-type access, mutation, safety and lifetime checks.
- Exceptions propagate exceptional failures; expected alternatives use conditional outputs or explicit domain statuses. `require` is a safe checked failure; catching/recovering requires unsafe recovery authorization, while unsafe operations and caller-unsafe invocations require explicit `unsafe { ... }` blocks or `unsafe(expr)` expressions. Cleanup and `try/finally` do not require unsafe authorization. Unsafe recovery does not prove an object's invariant has survived. Platform callbacks must contain faults or terminate rather than unwind across a foreign ABI.
- Type, declaration, interface and metadata contracts are authoritative. A compiler may infer private implementation facts but cannot silently publish stronger or weaker promises from a replaceable body. Consumers bind against the producer's resolved contracts and rule version, not their own reinterpretation of its source defaults. [Lifetimes](lifetimes.md) specifies this distinction for borrowed results.
- Visibility-bearing primary declarations require explicit visibility and fixed role-specific modifier order. Types place `partial` immediately before their kind modifiers and kind; functions/methods order `static`, `extern`, receiver `mut`, `unsafe`, `async`, then `partial` before the return type/signature. A static constructor instead has no visibility and identifies its primary through its mandatory initialization-policy annotation. A partial identity has exactly one primary contract author; secondaries cannot repeat or alter visibility, prefix modifiers, conformances, constraints, defaults or conditions. Secondary attributes are permitted unless their resolved schema disallows placement, with contract-changing semantic schemas enforcing primary-only authorship. New members contributed by a secondary type part have their own declarations and contracts. See [declaration forms](grammar-spec.md#31-source-file-structure-and-declarations).
- A public interface may have internal, file or friend conformance requirements to restrict new implementers to its defining module, source unit or authorized type code while remaining accessible for public operations and constraints. Inherited requirements retain those boundaries. Control over implementers is not a blanket ABI-compatibility promise. Private interface members are complete helpers owned by their declaring interface, not conformance requirements or replaceable defaults.
- Instance constructors synchronously initialize an unpublished receiver, with declaration field/property initializers before constructor preconditions, body and postconditions. Caller initializer entries follow, then independent completion validation and publication. Their ordered prefix is attributes, visibility, optional `unsafe`, then optional `partial`; initialization grants receiver write authority without `mut`. Partial constructors retain sole-primary ownership of defaults, conditions and completion guarantees. Constructors cannot be `async` or `extern`. External callable bindings apply only to top-level functions and static methods; constructors may wrap those bindings under ordinary checked contracts.
- Fields order attributes, visibility, optional `static`, `extern`, storage `mut`, then caller-`unsafe` before their type/name. Ordinary bare storage is fixed after initialization. For reference type `C`, `write C` permits referent mutation, `mut C` permits slot replacement, and `mut write C` permits both; directly held structs or `val<C>` need `mut` place authority, retaining nominal/member restrictions. An unsafe field requires an explicit unsafe context for every direct read/write/move or reference/pointer formation. Fields reject `async` and member-level `partial`.
- Static extern fields bind foreign data symbols under explicit representation, access, validity, synchronization and lifetime contracts, without Terrum initializers. A non-mut import limits Terrum access but does not promise immutable foreign storage or stable loads.
- Ordinary static storage or an authored static constructor forms one initialization group per loaded closed type, with separate generic-instantiation storage. Non-lazy initializers execute in compiler-input/source order before any authored `static T()` preconditions/body/postconditions and group publication. Static constructors have attributes, `static`, optional `partial`, no parameters/visibility/caller-unsafe modifier, and a mandatory explicit `[StaticInitializer(...)]` trigger policy. Without an authored body, the synthesized group is implicitly field-triggered. `[LazyInitialization]` selects a separate demand-initialized field whose external first access completes the group first. See [static initialization](grammar-spec.md#static-initialization-and-load-scoped-storage).
- `static` is not a concurrency opt-in: runtime static fields require the normal explicit checked sharing/access protocol, including shared reads, or a proven nonconcurrent restriction. Initialization blocks competing permitted callers by default, but does not grant later sharing or mutation authority. Cyclic demands for unfinished completion fail. Existing exception policy applies; unsafe recovery can explicitly retry failed unpublished state under cleanup/lifetime constraints, and unsafe suppression alone grants no initialization facts. Static state has a checked load-scoped lifetime and unload obligations. AOT/JIT optimizations preserve effects, failures, identity, ownership, cleanup and the memory model, not just computed values.
- [Standard failure categories](runtime/standard-failures.md) select InitializationCycleException at cyclic demand. An initializing flow receives the actual escaping event; waiting/later access to its failed unit creates a fresh InitializationFailedException, without sharing the original payload/event or retrying.
- Named properties may be instance/static; `this[...]` indexers are instance-only. Accessors inherit visibility or explicitly narrow it, and independently declare receiver/caller-safety contracts. Static `init` accessors are available only within their owning group's unpublished initialization phase, not for ordinary post-completion assignments. Accessors are synchronous; awaitables may be returned as values, while async execution uses methods. A partial primary owns accessor shape and contracts without allocating backing storage; one matching secondary implements them. Extern properties/accessors require a separate concrete interop design.
- Operators/conversions are static, with explicit operands and ordinary member accessibility. They may be caller-unsafe or partial under sole-primary ownership, but have no receiver `mut`, `async` or adopted extern form. Operator operands and conversion inputs may use `implicit ref<T>` under existing access, lifetime and overload rules, not size-based ABI adaptation. Conversions retain their logical source type; syntax never adds an implicit await or user-defined conversion chain.
- Associated type/constant declarations and bindings use attributes and visibility before `type`/`const`, without callable/storage modifiers or member-level `partial`. A partial enclosing declaration may contribute a complete associated declaration/binding under existing access and conformance rules.
- Source compilation checks authored bodies against declared contracts. Ordinary binary consumers safely decode and structurally validate artifacts, resolve references, check binding/contract compatibility and validate caller use, then trust declared metadata contracts for external dependencies instead of re-proving dependency implementations. Callers still satisfy imported preconditions, access, ownership and effects. This is not permission to trust malformed bytes; optional deeper audit tooling is not a v1 promise. Signing/provenance is separate from semantic correctness; no mandatory signature or trust store is selected, and signer identity does not prove a contract true. Current reader and verifier support is documented in [implementation](../implementation.md).
- Keep minimum language requirements separate from the standard application-library/runtime surface and feature-specific dependencies. Foundational types such as `ref<T>`, `ptr<T>` and `int32` are illustrative, not a complete mandatory API list. Language conformance alone does not require heap/GC, a scheduler or OS services; richer features retain their dependencies and checked contracts when used, with no silent semantic weakening or fallback. This can enable embedded/kernel environments in principle, but does not promise a backend there or universal support for every feature.

### Safe and unsafe boundaries

The [memory model](memory-model.md) defines the accepted access, optimization
and concurrency contracts. `unsafe` transfers proof obligations to the
developer; it does not change specified operations, imply `unchecked`, or
authorize arbitrary behavior when analysis discovers a violation. Existing
explicit checks remain. Optional diagnostics may identify misuse without
inserting repairs or changing the contract after inlining or specialization.

Mark caller obligations with `unsafe` on the callable declaration:
`public unsafe void M()` propagates obligations to callers, while ordinary
`public void M()` does not. This modifier does not make the callable body an unsafe
context: unsafe operations inside either form still require explicit
`unsafe { ... }` blocks or `unsafe(expr)` expressions, and calls to
caller-unsafe declarations require an explicit unsafe context at the call site.
Encourage a `# safety` documentation
section to explain caller responsibilities; prose is informative, not a
compiler proof or a substitute for checked contract metadata. Safe declarations
may contain internally scoped unsafe work without an extra
trusted-implementation marker.
These are target directions. Current support for `unsafe` recovery is
documented in [implementation](../implementation.md).

### Types and operations

The [complete-type chapter](language/types-and-normalization.md) defines
normalized identity, qualification positions, substitution, optional and
requested-access managed-box normalization. This section gives their
storage/operation context.

Declarations are immutable by default. A bare local is fixed after initialization;
`mut T value` permits rebinding that local. `public mut class C` and `public mut struct S`
declare mutable nominal storage; an instance method's receiver is read-only by
default, and `public mut void Update()` explicitly requires a writable receiver. Keep
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
[References and explicit indirection](language/references.md) defines the
descriptor model: `mut ref<T>` permits local descriptor rebinding,
`write ref<T>` permits immediate referent writes, safe `*r` dereferences a
reference, and `r->Member` abbreviates `(*r).Member`. Ordinary class handles
retain `c.Member` access. These rules do not change under generic substitution.
`write box<T>` can grant access
through a reference only within the payload's declared mutable-member
contract. `write val<T>` is not a default permission; inline fields need
writable containing storage or a writable loan/view. `own<val<C>>` is an
explicit native owner of an inline payload, not the unqualified/legacy `own C`
spelling. `init ref<T>` is an initialization loan to vacant storage: its
referent cannot be read before complete initialization, and publication follows
the declared outcome contract. The accepted call-side
form passes `init name` for vacant storage or declares a vacant local with
`init var name`. Conditional publication is distinct from ordinary writable
references and preserves the destination's complete type and cleanup
contract. Parameter-only `ref T`, `mut ref T`, `out T`, `T*` and `T&` are not alternate
representations. `mut` is not valid as a generic type-argument qualifier;
use `write` when the referent capability itself is part of the type. See
[implementation](../implementation.md) for the supported output modes.

The accepted call-side readonly-borrow form is the parameter-only modifier
`implicit ref<T>`, not `implicit T`, `in T`, or a qualifier inside `T` or
`ref<T>`. Callers pass ordinary expressions, evaluated once: an eligible
existing place is borrowed, otherwise a hidden mutable local is assigned at that
argument's original evaluation point in the current enclosing lexical scope.
Its initialization is not hoisted to scope entry, and it is not wrapped in a
shorter lifetime-only scope. It follows ordinary local initialization, ownership/root
cleanup and unwind rules; conditional or short-circuit non-evaluation creates
no new value and leaves any previously initialized value unchanged. Each
source materialization site reuses one initially vacant mutable binding per
supporting-scope activation, rather than retaining a new owner every iteration.
Ordinary replacement and loan checks apply. The
introduced slot loan has the same semantics as an explicitly authored readonly
`ref<T>`, including declared result and backing dependencies. A valid returned
reference to a materialized local may be used by later statements in that same
scope. `implicit` adds no special noescape or suspension restriction, does not
widen an existing place's or backing value's lifetime, and grants no new
capture, suspension or escape permission: ordinary declared dependencies still
govern each use. The [source-equivalent loop expansion](lifetimes.md#repeated-evaluations-and-reusable-locals)
makes the scopes, initialization, replacement, and final cleanup explicit.

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
Current support for this contract is documented in [implementation](../implementation.md).

Constructor initialization does not imply later mutation permission. Loans are
place- and flow-sensitive, ending after last use; ordinary readonly and writable
references do not by themselves declare exclusion. Overlapping accesses reject
when they conflict with a checked owner, guard, initialization, or exclusion
contract. Qualified generic `T` preserves its
complete caller-selected qualification: a concrete readonly default does not
strip `write` from a substitution. A generic writable operation must state the
required access contract; `write T` is not a universal make-mutable transform.
A readonly-only type constraint is not the same as projecting a readonly view
from an unknown, caller-qualified `T`. The exact generic constraint syntax,
representation-aware mutation rules, and required protocol for writable
generic operations remain open.

An ordinary `struct` is inline and promises checked `[Copy]`; every field must support a code-free, allocation-free copy without duplicating a unique obligation. Adding a destructor or unique field must fail the declaration instead of silently removing that public capability. A `unique struct` or `unique struct union` is noncopyable even if its bytes look trivial. An ordinary `class` uses aliasable managed handles; copying a handle shares its object, not its payload. A `unique class` or `unique class union` has a unique owning handle, not necessarily a unique reachable graph or a prohibition on GC. `unique` restricts duplication; `move` explicitly transfers a named value (including a copyable value when invalidating its source is desired). By-value copy/consume is based on the **complete qualified form**, not merely the payload declaration. A moved handle need not relocate the payload; large `[Copy]` values are not assumed cheap.

[Owning fields and cleanup](language/ownership-and-cleanup.md) permit
structurally tracked partial field moves only under existing write/init
authority, retaining every live child and prohibiting whole-value use until
checked repair. Authored Cleanup requires a complete receiver: its body runs
before automatic reverse-declaration field cleanup. Failed construction instead
unwinds reverse acquisition without incomplete-parent Cleanup.

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
value while preserving the requested access, retained inner qualifications,
and absence. The outer box access controls a compatible collapsed layer:
for `U = write box<S>`, `box<U>` is readonly `box<S>`, not another box
or a writable result. `U` itself remains complete and writable; this is
representation-preserving attenuation, not equality with `write box<S>`.
`val<box<S>>` names a payload
type without extracting it. `val<C>` remains conditional on explicit
inline-payload, identity, copying and address-stability rules; class-handle
aliasing cannot be silently inherited by inline storage. Scoped loans,
arbitrary cleanup obligations and raw pointers cannot be boxed to escape
restrictions. `val<T>` has no implicit writable-default interpretation.

There is no universal `default(T)`: live fields and active union payloads must be initialized. Explicitly repeated optional modifiers (`int32??`, `T??`, including intervening trivia) are syntax errors. Optional construction remains semantically idempotent: substituting `T = int32?` into `T?` yields `int32?`, not a second presence layer. `Option<T>` likewise normalizes an optional payload rather than adding a presence layer. Modifiers on distinct nested types, such as `List<int32?>?`, are valid. Target `is var item` yields the initialized non-null payload, `is var? item` permits initialized null, `is null` tests absence in a live optional, and `is init` tests initialization independently. Nominal unions preserve distinct cases such as `Missing` versus `Found(null)`; `with` updates need separate `Unchanged`/`Set(null)` states. `T?` and `box<T>` are non-injective projections, so inverse generic inference cannot guess a unique original type. Normalization precedes signature identity/overload checks, without flattening nominal wrappers. Niche encodings must be declared validity/ABI contracts (e.g. reserved `-1` for nonnegative `int32`, zero for nonzero `int32`, null for a non-null reference); invalid encodings and padding are not free option states. Imports and mutations preserve validity.

The accepted inference direction uses `var` for a non-null binding and `var?`
for a normalized nullable binding. An ordinary declaration from a possibly
null expression requires `var?` or established non-nullness; there is no
implicit unwrap, throw or fabricated default. Pattern binding first establishes
initialization, then the requested presence contract, preserving access and
ownership. Structural output patterns such as `(var x, _)` can consume a
contextual multi-output matching view of one call; the view retains status
correlations and never reads vacant outputs to construct an ordinary tuple.
Ignored live owners still require cleanup. Ordinary value bindings copy only
when the type permits it; shared handles preserve access and rooting, while
value-type bindings do not acquire hidden aliases. Matching a reference or
pointer preserves its existing form and dependencies; creating a reference or
transferring ownership requires an explicit operation. Pattern observations are
once per member path within an attempted arm, fresh in later attempts, with
ordinary effects and failures; optimization must preserve the memory/side-effect
contract. Matching does not implicitly consume a unique source before arm
selection or roll back explicitly written guard effects. Union-case patterns
mirror qualified construction, matching declared payload positions rather
than invoking constructors or name-discovered `Deconstruct`. Omitted eligible
output arguments activate lifting in matching/`require` contexts; explicit
destinations are not lifted, and partial omission is permitted. The channel
is fixed by the call, not arm types, so Boolean payloads and generic
specializations do not introduce status/payload ambiguity. Matching follows
declared per-output availability, including initialized failure outputs.
Ordinary `when` expressions provide member/relational conditions; dedicated
pattern syntax and pattern `where` are deferred. Explicit `ref<var>` and
`write ref<var>` patterns match existing compatible references, not hidden
reference formation, extra indirection, value-type copies or storage narrowing.
`x is T t` follows the same rules for concrete and generic `T`, including
reference/pointer forms permitted by constraints. Generic type-pattern slots distinguish
wildcard `_` from inferred `var`/`var?`: the latter filter non-nullable versus
either argument type, not contained values. Named forms such as
`List<var? T> list` capture the actual complete type as `T` and bind `list`
separately as a value. Reserved `typeof(T)` permits type inspection; `@typeof`
uses ordinary identifier binding instead. A recognized
exact identity test refines `T` on the proven path without a conversion.
Each slot denotes one argument of the selected constructor, not a pack or
higher-kinded application. Ordinary generic `_` inference remains distinct
from pattern wildcard matching. Runtime-only captured types use checked
evidence and ordinary generic constraints/witnesses with supported execution;
capture does not silently box or generate code. General application/constraint
binders, witness representation and richer-status/object registration remain
separate work.
This replaces target `some` pattern spelling, not the compiler's current
`is some` support or its existing whole-type `var` inference.

Integer-backed `enum` values include unnamed underlying values; `[Flags]`
defines named bit-set operations without closing the set. Nominal unions are
declared as `struct union` for inline values or `class union` for managed
objects; neither storage form is an implicit default. Struct-union copying
requires every possible payload to support the promised Copy contract.
Class-union copying aliases the managed object through its handle, not a copy
of the active payload. Both forms have explicit unique variants and no
nominal `mut` form. Case-set promises remain separate from storage, layout
and ownership contracts. Unions are
evolvable by default; an explicitly closed case-set promise makes adding a case
a breaking change, ordinarily rejected before consumers bind to it. Union
representations still require a valid active payload;
an overlapping native layout does **not** establish a safe tagged union.
Cases declare `Missing;` or `Found(T value);` and construct through qualified
`.Missing` or `.Found(value)` forms. The parameterless checked `[ClosedCases]`
attribute selects the case-set promise, not fixed layout or record synthesis.
Payload parameter names do not generate public getters or other members;
pattern extraction remains separate. See the accepted
[declaration grammar](grammar-spec.md#31-source-file-structure-and-declarations);
current union implementation and exact metadata encoding remain absent.
The [registered layout schemas](targets/windows-x64-data-layout.md#4-registered-source-layout-schemas)
use `[Layout(LayoutKind.Sequential)]` and the separate `[Blittable]` marker.
They are source contracts, not compiler support claims. `Auto`
is the default: instance fields are private and external access uses properties or
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
compatible fields. Its registered `[Blittable]` marker is no-argument and
nonrepeatable; the logical commitment does not allocate its physical codec.
Ordinary `ref<T>` is excluded; `ptr<T>` may qualify subject to ordinary pointer
safety. [Windows x64](targets/windows-x64.md) specifies its target limits and
call/validity rules; missing metadata remains independently gated.
Unfrozen instance fields remain private; a frozen representation
may expose them without changing ordinary readonly or `mut` access rules.
`Blittable` does not imply `Copy`, atomicity, sharing, arbitrary-bit validity,
or a universal cross-platform wire format. Public instance-field access requires the
fixed `Blittable` commitment, regardless of layout kind. Static storage is
not part of instance layout; foreign data bindings have their own explicit
compatible representation contract. Published
language-level signatures and their layout, access, ownership and effect
contracts are binary compatibility commitments even without foreign
interoperability; a public method does not need a foreign-layout annotation
merely because it is public. Layout-dependent values without a fixed
commitment cannot cross an independently replaceable dynamic boundary; they
can be used in layout-coupled compile/rebuild/invalidation scopes. JIT/AOT
labels alone do not decide whether such a boundary is coupled.

Both a power-of-two member-packing limit and a separate power-of-two
aggregate-alignment control are accepted and may be combined. Packing caps
natural member and derived aggregate alignment, without weakening transitive
explicit nested alignment; the other control over-aligns the aggregate,
with tail padding preserving array stride. The combined Layout schema has
named uint32 Packing/Alignment controls under the selected target limits.
Transparent inherits its carrier and
rejects either control. Initializing fields does not initialize padding; raw
object-byte blits and unmediated raw foreign transfer are unsafe. A safe
serializer must account for the initialization and representation of every
emitted byte, but may use verified or explicitly trusted raw copy. Details are
in the [layout contract](data-layout-and-abi.md#fixed-directly-interoperable-representations).

These are target design contracts, not blanket compiler support claims.
The provisional compact declaration spelling and private layout limits are
[implementation observations](../implementation/layout-profiles.md#provisional-compact-source-layout),
not restrictions on the complete [Windows target contract](targets/windows-x64.md).
Draft source/schema changes need not preserve unpublished declarations or
artifacts. Retained independently replaceable contracts still meet their
[binding and replacement obligations](representation/binding-and-replacement.md).
Language/module compatibility is distinct from foreign ABI and serialization:
external wire or callback encodings are specified for concrete protocols, not
as one global boolean, reference or SIMD format.
Declared overlapping field storage is available only through an explicit
`[Layout(LayoutKind.Union)]` declaration with `[Blittable]`; it
is unsafe to access overlapping alternatives, with no active-member tracking
or special safe subset. This does not prohibit unsafe pointer-based
aliasing/reinterpretation or compiler-private overlapping representations. A
safe non-Blittable discriminated-union/sum type is a separate higher language
feature: case/tag checks must preserve ordinary validity, ownership, roots and
cleanup, and do not grant raw reinterpretation. The source grammar selects
nominal union declarations and qualified case patterns; physical safe-sum
representation and metadata remain open.

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
property subset is documented in [implementation](../implementation.md).

`where T : IFoo` requires a checked behavioral witness; `where T : [Sized]`, `[Copy]`, `[ManagedStorable]`, `[ThreadTransferable]`, `[ThreadShareable]`, `[TextCodeUnit]` or `[NativeComInterface]` asks for a compiler-verified property of the complete qualified form, **not** implementation of an `I...` interface or the mere presence of an attribute. Thread capabilities describe which checked transfer or concurrent-use contracts a complete form can satisfy; they do not imply that a value is currently shared, authorize mutation, or perform synchronization. Thread confinement is the default but does not bind a value to one OS thread or executor. `[Sized]` does not promise a size constant in shared generic code. `[TextCodeUnit]` admits `char8`, `char16`, `char32`, not arbitrary valid encoded text. Native COM additionally requires checked IID, inherited slots, signatures, layout and lifetime. User shape generation can provide witnesses or adapters only after checking costs, ownership and ABI.

The NativeComInterface item is an overview-level direction, not a completed
source/property schema or implementation claim. The
[native cleanup amendment](../proposals/unsafe-ffi-vtable-cleanup.md#source-overview-amendment)
recommends replacing that compiler-COM property with generic native/vtable
facts and authored library IID/acquisition/ownership contracts. Do not infer
a selected intrinsic from the illustrative attribute name or implement
additional COM policy merely to preserve this draft example.

Thread-capability proofs can be derived from complete definitions and
dependencies, but consumer-visible guarantees come from declaration contracts
and producer-published rules, not incidental private layout. Generic bodies use
declared constraints and published conditional rules; machine-code
specialization cannot invent a missing source capability. A promised capability
cannot silently disappear when an implementation changes. Capability stability,
fixed layout and trivial-wrapper atomicity are separate contracts.

[Core value-layout queries](language/registered-queries.md#value-layout-queries)
use `RuntimeHelpers.SizeOf<T>()`/`AlignmentOf<T>()` with a by-value `nuint`
result. They observe the complete normalized value/handle representation,
not reachable storage or object payload. A declared `[Sized]` guarantee
supports admitted runtime layout evidence, not an automatic numeric constant.
Retained observations preserve their exact target and published fixed
commitments or explicit layout-coupled rebuild/invalidation dependencies.
No new keyword, mutable static query state or general reflection object is
introduced.

`where T : write` is a [direct access requirement](language/types-and-normalization.md#generic-write-requirements)
on the complete normalized type argument, not an attribute or interface.
It requires already-writable immediate access and preserves `T`; it cannot
upgrade readonly arguments, substitute for member/witness contracts, or
derive write access from `mut` bindings or writable inner handles.
The declaration uses `<T>` and ordinary `:` requirements; `<write T>`
formal shorthand and `where T is write` are not selected forms.

An eligible one-field integer/Boolean wrapper derives
[constant-formal participation](language/constant-generics.md)
from its checked Transparent contract and existing constant-construction,
complete-initialization, frozen Copy/Blittable and typed value-codec obligations.
Canonical identity is the exact complete nominal type plus carrier value,
not user equality or hashing. Its nominal identity and validity
remain meaningful, including a nonnegative invariant. Equal representation,
an adapter relationship or `Transparent`/`Blittable` alone does not supply
that capability. Removing or incompatibly changing its published guarantees
is breaking under the existing replacement contract; unpromised private
implementation details remain replaceable. No second attribute/protocol is
added; physical encoding and any separate attribute-encoded constructor recipe
remain specification work.

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
match. Forwarding is explicit checked membership, authored or opt-in generated,
not a live wildcard; trivial forwards should avoid duplicated metadata while
preserving identity and contracts. Concrete adapter/projection schemas,
conflicts and dispatch encoding remain specification work; `Blittable` is a
separate fixed-representation promise.

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
that can bind older witnesses to a newly supplied default. Qualified member
bindings, optional first `<this T>`, `.via<I>` witness selection and restricted
current-receiver `default<I>.Member()` calls follow the
[source grammar](grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding).
The [witness chapter](language/interface-witnesses.md) defines inherited
identity/default resolution, associated checking, and source-equivalent
qualified-self projection. `T = write C` remains a complete qualified type:
an existing self witness may adapt only when every requested contract holds,
not by erasing access or inventing a writable result. Witness metadata and
dispatch ABI remain to specify; current bounded static operator support is documented in
[implementation](../implementation.md).

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

[Calls and inference](language/calls-and-inference.md) owns ordinary source
argument mapping, finite inference, applicability and ranking.
Source overload resolution records the selected declaration and closed generic
arguments. A linker or JIT validates and implements that binding rather than
rerunning source resolution; runtime virtual/interface dispatch is separate.
Exact matches precede only previously permitted, declared projections or
erasures. Overload binding does not search implicit numeric or arbitrary type
conversions; permitted access/qualification weakening can affect match
quality (for example, `write Span<T>` to a readonly `Span<T>` parameter), but
does not infer a new or reversed family projection. Binding first determines
applicability, then compares each argument's match quality independently:
identity, then permitted immediate-access attenuation, then other admitted
single-step bindings.
One explicit additional built-in edge is `pin ref<T>` to matching `ptr<T>`:
it retains access and lifetime/pin/backing dependencies, and an identity match
ranks ahead of it, as does access attenuation over the same representation.
This does not enable conversion-operator lookup, conversion
chains, implicit numeric conversion or inferred future exceptions. One
candidate is better only when it is no worse for every argument and better
for at least one; crossed better/worse arguments leave the pair incomparable rather than
combined into a score. After quality ties, binding priorities retain
by-value over an otherwise equal implicit formation of `ref<T>` and the
accepted `params` normal/expanded rules. Declared structural specificity
then resolves remaining ties. A fixed-arity non-expanded candidate
wins over an otherwise equal expanded `params` candidate. Constraints filter
applicability but do not create overloads differing only by constraints or
require arbitrary logical subsumption; return-only overloads are not
introduced. Declaration order, return type, and literal numeric fit do not
break ties. Applicable members beat extensions. Thus a sole
`Set(int32)` accepts `Set(1)`, but adding `Set(uint32)` makes the source literal
ambiguous while an already compiled caller retains its `int32` binding.
Removing the bound declaration remains a compatibility break.

Generic inference is finite and staged: explicit arguments pin positions;
typed values and constituent evidence in known tuple/collection shapes
establish types; known callable inputs permit delayed typed result evidence;
expected-result context fills only unresolved positions; declared defaults
fill omitted unresolved positions; then full contextual expressions are
checked. Context never retargets established inputs or generic types.
New input types from expected-result/default fallback reenable typed
evidence before further defaults; defaults follow declaration order.
For example, `int64 x = 50000 * 50000`
constructs `int64` operands, while `int32 a = 50000; int64 y = a * a` needs
explicit conversion. Partial `_` holes use the selected spelling:
`Convert<_, int32>(typedValue)` may infer only the first argument,
and `int32 result = Convert(typedValue)` may infer a result-only type from its
target. `func<_, int32> f = ToInt32` can fill a hole from one unique method
signature. All holes resolve at local binding; exported fields, aliases, and
parameters cannot retain them. There is no wildcard/existential/higher-kinded
meaning, and non-injective projections are not inverted. Ambiguity diagnoses.
These are specification contracts, not a claim of current compiler support.
Typed by-value evidence over the same normalized stem chooses the strongest
common permitted immediate access: all writable inputs retain write;
mixed writable/readonly inputs infer readonly. Exact invariant storage
evidence pins the complete type instead. Expected results and constraints
cannot weaken it to repair applicability. Delayed lambda results fill
unresolved positions without guessing inputs or representations.
Typed collection elements/spreads can supply evidence only through an already
selected family's published forward element contract; bare untyped literals
still supply no default width.
Literal-kind eligibility precedes unique signature selection, but value/range
acceptance and registered parser execution follow it without overload fallback.
Definition-site constraints/effects remain authoritative; a caller cannot
lend stronger constraints to a template.
Generic signature lookup remains distinct from type-argument inference.
Physical generic ABI, unsupported families and registration schemas remain
separate work; current support is documented in
[implementation](../implementation.md#bounded-generic-functions).

Operator protocols are explicitly declared and constrained; homogeneous
operands require the applicable conformance. Operator spelling does not imply
`[NoThrow]`, derive complements or ordering laws, or establish universal
equality, hashing or managed identity policy. Unary result types are declared,
not inferred from operator spelling; `!` is not inherently bool-result, and
result type alone does not distinguish overloads. Primitive and heterogeneous
witnesses, nullable operands and public generic ABI are separate contracts.
See [implementation](../implementation.md) for the bounded operator subset.
Static `++`/`--` have operand-shaped results rather than an arbitrary result
type; this does not import C# inheritance. `true` and
`false` are the explicit exception: both return `bool` and are declared
together. C# 14's instance-mutating `++`/`--` and `void` compound-assignment
operators are not adopted here. `++`/`--`, `true`/`false` and shift
protocols remain unsupported.

`_` is reserved and never names an identifier. `_ = expression;` is the
accepted standalone discard statement: evaluate the expression once, preserve
its effects and failures, and apply ordinary ownership/cleanup. Every normally
produced non-`void` expression result must be used or explicitly discarded,
including scalar calls, constructions, increments and awaits. Only genuine
unit-valued expressions can stand alone without result discard.
It is not an
assignment and has no name-dependent fallback. Positional wildcard patterns
and omitted lambda parameter names are separate nonbinding contexts; they do
not drop the matched/argument value or alter parameter mode. Separately
specified type holes are not identifiers and are not covered by this discard
rule. Their bounded source forms are defined in the [source grammar](grammar-spec.md).
`name = _;` separately ends a named local's current value lifetime, performing
its scope-exit obligations and leaving it vacant. It neither removes the
declaration nor grants write authority: same-scope redeclaration remains invalid,
an ordinary immutable `var` cannot be assigned again, and an `init var` local
can be initialized again when vacant without changing its established type.
`init mut var` and `init mut T` separately allow ordinary binding mutation
and replacement after initialization; vacancy does not grant that permission.
Ending a borrow releases that loan, not its referent; retiring a GC handle
does not destroy a shared object. Dependent loans/pointers constrain early end.
See [lifetime ending](lifetimes.md#explicit-local-lifetime-end). Both forms
have bounded source-compilation support; local lifetime ending is not accepted
in source-free executable TRRM. Wildcard patterns, lambda placeholders and
generic type holes are separate features; see the [implementation boundary](../implementation.md).

The [local declaration-space rules](language/local-declarations.md) prohibit
shadowing an enclosing local or parameter but permit independent sibling
scopes to reuse names. A local participates in its whole block's name lookup;
using it before its declaration or before initialization still diagnoses,
without falling back to an outer declaration.
Generic formal names are also protected against nested formal, parameter,
and local reuse under [names and binding](language/names-and-binding.md).

Pattern bindings also occupy their supporting declaration space when a match
fails. Ordinary value-binding patterns introduce initialization-capable fixed
locals; explicit `mut` patterns permit ordinary live replacement.
Failed/skipped matches preserve a previous successful value, including after
loop exit. [Structural flow](language/initialization-and-flow.md) defines
negative paths, state tests, joins, and captured-type availability.
Declaration preconditions share one isolated scope and postconditions another;
neither introduces implementation-body locals.

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
Nominal union-case and structural tuple patterns are selected by the source
grammar; arbitrary object extractors and closed-case metadata
encoding remain open. The declaration marker is `[ClosedCases]`. Unknown payloads
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
authorizes a [pure evaluation form](language/published-predicates-and-effects.md)
with no observable effects, including external mutation and escaping exception
effects on its declared valid-input domain. State read through `this` or
explicit inputs is part of the logical input; private mutation and immutable
published data are permitted. Mutable ambient state, I/O, identity-producing
allocation, volatile/atomic observation, synchronization and suspension are
not. No `Stable` or read-scope modifier is required. Repeated-call reuse needs
unchanged relevant input state and surviving code/form/backing dependencies;
readonly and reference-address equality do not prove that.
The optional [NoAlias parameter contract](language/published-predicates-and-effects.md#noalias-parameters)
requires checked separation of accessed mutable regions from the receiver,
other inputs and initialization destinations throughout the invocation.
Proven immutable sharing is exempt. It is not an exclusive loan, a purity
claim, an overload discriminator or additional write/lifetime authority.

The working `IContiguousSequence<T>.AsSpan()` returns a read projection. `Span<T>` implements it **explicitly**, permitting constrained generic calls without a redundant concrete `span.AsSpan()` member. Known-code-unit `TextSpan<TCodeUnit>.AsSpan()` and `Text.AsSpan()` stay public because those change the view; erased `TextSpan<void>` has no `AsSpan()` or `IContiguousSequence<void>`. A stored or returned view must preserve its opaque backing dependency, even for empty/end slices. Detailed receiver and backing rules are in [lifetimes](lifetimes.md). [Value iteration](language/iteration.md) selects `ISequence<T, TCursor>` factories and direct `ICursor<T>` sources; [lending iteration](language/lending-iteration.md) selects distinct reference-output roles, not an associated lifetime-generic item or automatic view witness. Working `Task<TResult = void>` makes `Task` precisely `Task<void>` after type-argument expansion, not an independent task protocol; type-default syntax follows the [source grammar](grammar-spec.md#32-types-parameters-and-arguments).

Declared [type families](type-families.md) can expose checked common operations and conditional members/conformance. A static parameter meaning "any fixed vector of float32" preserves its actual type, width and associated mask, including same-instantiation relationships between operands/results; it does not require users to introduce a nominal `ISimdVector` solely to name the family. Runtime interchange that hides an encoding is different from preserving an unspecified static instantiation. Erased forms are explicit family contracts, never an automatic `Type<T>` to `Type<void>` rule; copy/move, tracing, cleanup, lifetime and ABI obligations survive. `Task<void>` has a zero-data unit completion result, not permission to discard an arbitrary owning result.

### Primitives, strict arithmetic and text

The mandatory scalar catalog is `void`, `bool`, raw `char8`/`char16`/`char32`,
validated scalar `rune`, signed `int8`/`int16`/`int32`/`int64`/`int128`,
unsigned `uint8`/`uint16`/`uint32`/`uint64`/`uint128`, distinct target-size
`nint`/`nuint`, IEEE binary `float16`/`float32`/`float64`/`float128`,
`bfloat16`, and IEEE `decimal32`/`decimal64`/`decimal128`.
These are the 26 reserved scalar identities of the
[language conformance floor](overview/conformance.md#mandatory-scalar-catalog).
An implementation may use a semantics-preserving software implementation
when the target has no direct instruction for a format. The catalog does not
require a managed heap, scheduler, or operating-system service.
`simd64/128/256/512<T>` are separately admitted fixed ABI shapes; `simd<T>`
is genuinely hardware-scalable and initially sizeless, not an unspecified
preferred width. The [Windows x64 profile](targets/windows-x64-data-layout.md#3-ieee-decimal-bid)
selects BID decimal storage; other target encodings and precise vector-family
syntax need their own selection. Decimal value semantics are not CLR `System.Decimal`.

Numeric literals have **no fallback type**: an expected type or explicit
target must disambiguate; `var x = 1` and unconstrained `F(1)` diagnose.
Contextual construction rounds/validates directly in the selected format, not
through a hidden `int32`/`float64`. `int32(1234)`, `uint64(0xFFFF_FFFF)` and
`float32(0.1)` illustrate direct target parsing. Width suffixes do not exist.
`T(untypedLiteral)` uses the registered parser directly; `T(typedExpression)`
is a checked explicit conversion. `new T(arguments)` remains nominal
construction. Already-typed numbers never implicitly widen or narrow.
Contextual decimal, hexadecimal and binary integer literals have no fallback
type, and numeric type-call conversions are checked.
[Type/value-call binding](language/names-and-binding.md#calls-conversions-and-recorded-binding)
resolves the name before argument applicability; an invalid selected callable
does not expose a hidden type conversion.
Current executable coverage, including floating literals, is documented in
[implementation](../implementation.md).

An attached leading ASCII `+`/`-` is part of a numeric token;
`-(128)` is instead an ordinary operator expression. The
[literal chapter](language/literal-construction.md) owns target selection,
registration and exact input/results. Decimal integers have mathematical
range semantics, including directly signed minima. Signless hexadecimal
and binary integers construct integer-target bits: `int32(0x8000_0000)` is
`int32.MinValue`, `int32(0xFFFF_FFFF)` is `-1`, and `int64(0xFFFF_FFFF)` is
`4294967295`. Nonzero bits above the target width are rejected; leading zeroes
and separators do not add significant bits. Once constructed, the value is
typed normally and subsequent conversions remain checked:
`int32(uint32(0xFFFF_FFFF))` fails, while
`int64(int32(0xFFFF_FFFF))` yields `-1`. Explicitly signed based tokens use
numeric range instead: `int8(-0x80)` is -128, `int8(+0x80)` rejects +128,
and `int8(-0xFF)` rejects -255. Typed/grouped-operand negation remains
ordinary checked arithmetic: `-int8(0xFF)` produces 1, while
`-int8(0x80)` fails. Floating targets interpret every based integer
numerically, not as floating bits. Decimal spelling retains its preferred
cohort within format bounds; all floating literals round directly to their
target, including successful infinity/subnormal/signed-zero results.
Binary literals use `0b`/`0B`; `_` runs may
separate digits or follow a based prefix, but cannot trail a digit run or
attach to a decimal point, exponent marker or sign.

`[ConstEval]` opts declarations into bounded deterministic
compile-time execution and implies checked pure-form support.
`[Pure]` alone may authorize backend folding, not required Terrum interpreter
admission. `RuntimeHelpers.IsPureEval` and `IsConstEval` report the selected
form: ordinary false/false, pure true/false, constant true/true.
These are explicit author-supported forms, not detection that optimization
occurred. `const T a = F()`
requires a constant declaration value, while `T b = const F()` requires only
the expression. `const F() + x` requires
only `F()`, while `const (F() + x)` requires the whole expression. Literal
parsing always uses the registered compile-time parser, even in ordinary local
initialization; there is no runtime fallback. Current evaluator support is
documented in [implementation](../implementation.md).
The [canonical proof/evaluation floor](language/proof-and-constant-evaluation.md)
defines local scalar facts, target-semantic actions, selected-path eligibility
and guaranteed resource accounting. Private mutation and eligible inline values
are permitted; emitted constants require Blittable/frozen Copy validity and an
admitted target value recipe. The attribute does not promise ordinary-form
`NoThrow`, totality or every-input constant success.
There is no ambient I/O or arbitrary native/source-reading JIT execution.
Admitted constant NaN-processing arithmetic, nonidentity numeric conversion
and numeric selection use the selected positive quiet zero-payload recipe,
without weakening copy/sign-only bit contracts or fixing runtime NaN payloads. Required requests
preserve producer/rule/value-ABI dependencies and never fall back to runtime.
Incompatible documented constant-result changes are breaking; purity alone
does not preserve an old computed answer. Required evaluation does not change
type checking, overload binding or waive transformation safety.
A declaration with `operator +` needs a separate contract for
`operator +=`; user compound assignment mutates its place once through
`write ref<T>` without an implicit read/copy/write sequence. Wrapping and
saturating operations remain explicit.

The [primitive operator chapter](language/primitive-operators.md) owns
the complete mandatory scalar signatures, operand/count contexts,
observations, and checked place/accessor updates. Small integers retain
their own type; code units and `rune` supply equality/order, not implicit
numeric arithmetic. Integer arithmetic and extent arithmetic are checked in every build. Division
truncates toward zero; signed minimum divided by `-1` fails while its remainder
is zero. Unsigned unary negation is same-type checked `0 - x`, so only zero
succeeds. Typed shift counts can use any mandatory fixed/native integer;
otherwise the built-in count slot supplies `int32` context. Counts must
be nonnegative and below the data width, without masking or source numeric
conversion. Left shift is width-truncating bit
manipulation, not checked multiplication. Floating operations use target-format
round-to-nearest ties-even without implicit FMA, reassociation, ambient rounding
or flush dependence. Two-operand arithmetic that consumes or produces NaN returns
a quiet NaN with unspecified runtime sign/payload. Admitted constant
NaN-processing arithmetic/conversion/selection uses positive quiet zero-payload
results. Unary negation toggles only sign; Abs clears only sign, preserving
NaN payload/quiet-signaling state and decimal quantum even in constant execution.
The [floating-environment contract](language/floating-environment.md) supplies
no ambient language status flags and retains actual native control duties.
Bitcasts and copies
preserve bits under their representation contracts. Representation-level
`FromBits`/`ToBits` operations have their own bit contracts.
Floating-to-floating conversions follow IEEE-754:
inexactness, finite overflow to infinity, underflow, NaN and infinity do not
raise language failures, and no floating-point exception traps are enabled.
Nonidentity numeric conversion returning NaN produces a quiet destination
NaN, while same-format identity preserves its representation.
Other rounding modes require explicit APIs/adapters; there is no global
floating-point control-word API. Checked floating-to-integer conversions
truncate toward zero then throw if out of range; unchecked conversions truncate
then saturate rather than wrap, with NaN converting to zero.
[Conversion policies](conversions.md) distinguish range, fractional rounding,
integer wrapping, nonfinite handling and bit reinterpretation; floating
conversion does not promise cross-format NaN-payload preservation or exactness.
Numeric-key comparer semantics (all NaNs equal, signed zeros equal, decimal
cohorts normalized) are separate from IEEE operator equality.
Floating `%` uses the exact truncating quotient, not nearest-integer IEEE
remainder or a rounded floating division/product. Zero divisor or infinite
dividend gives NaN without a language failure; zero remainder retains the
dividend sign. Finite decimal remainder uses the smaller operand exponent
for its cohort. Built-in scalar updates may use eligible value getters/
setters without fabricating borrowable backing, and perform no final store
when the primitive check fails.

Source file text follows the [strict UTF-8 input contract](language/source-input.md):
one leading signature is consumed, invalid bytes reject before lexing,
and scalar/line-ending spelling is preserved in the immutable UTF-16 snapshot.
Runtime text has **no default encoding**. The single `TextSpan<TCodeUnit = void>` family has immutable validated `char8`/`char16`/`char32` members and a family-declared encoding-erased `void` specialization: bare `TextSpan` is exactly `TextSpan<void>`. Here `void` does not mean zero-size units or a default encoding. Erasure needs no text copy/transcoding and retains actual encoding, backing, access and lifetime/lease dependencies. Checked typed projection rejects a mismatched representation rather than transcoding; only known-code-unit forms expose typed `AsSpan` and contiguous-sequence conformance. Common `IsEmpty`, ordinal and formatting operations work on either form. A raw `AsSpan()` can forget text validity but not immutable backing, access, lifetime or rooting. Raw slices can split scalars; text slices must preserve scalar boundaries. Editing copies into mutable raw storage or a builder, then validates and freezes before publication; transferring an exclusively held initial buffer may avoid a copy only by revoking all writes. Readonly aliasing or validation alone cannot establish immutable text. Registered string targets use ordinary calls such as `utf8z("Terrum")`, not lexer prefixes or suffix tokens; the target validates encoding, termination and embedded-zero rules without implying pointer lifetime. Final storage, string facade, adaptive/block/RLE representation and projection ABI require experiments.

Character literals carry one scalar, target-typed to a representable code-unit/scalar type without a numeric or UTF-16 fallback. Registered text targets provide `rune` context and forward scalar fragments directly through the writer protocol. A raw `char8` is not automatically a valid standalone UTF-8 scalar. Precise representability rules are in [conversions](conversions.md#character-intent); compiler coverage remains in [implementation](../implementation.md).

Defaults are **ordinal plus invariant together**: exact Unicode scalar sequence equality and scalar ordering across encodings, matching hash, no implicit normalization/casing/linguistic comparison, and ASCII digits/signs/punctuation for numeric parsing and formatting. U+0661/U+FF11 digits and U+2212 minus do not act like ASCII tokens. Arbitrary valid Unicode remains valid text data. Termination, embedded-zero exclusion, pinning and lifetime are distinct native-string requirements. `Text` is a common payload/facade. The target sketch uses `own<val<Text>>` when it means a native owner of inline `Text` payload; construction, conversion and cleanup syntax remain open. Text implementation support is documented in [implementation](../implementation.md).

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
through pointer nesting, aliases, parameters, and results. The
[native interop proposal](../proposals/trrm-native-interop-metadata.md)
recommends concrete source-to-metadata mappings; their exact names/grammar
and import/export ergonomics remain unapproved. No implicit
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
by per-argument dominance: identity outranks access attenuation, which
outranks other admitted direct bindings, and crossed better/worse arguments
leave the pair incomparable rather than being summed into a score. After
quality ties, apply the by-value/`implicit ref<T>`/`params` priorities, then
declared structural specificity. Return type, declaration
order, literal numeric fit and conversion-chain search do not resolve a tie.
The [canonical call algorithm](language/calls-and-inference.md) defines
applicability, including typed collection-element evidence where a published
forward contract is available. Binding is fixed at source compilation; a backend does not
choose among overloads. For
example, given `Log<T>(T item)` and `Log<T>(params List<T> items)`, a
`List<int32>` argument selects the `List<T>` overload: both infer one `T` and
have identity matches after inference, but the declared `List<T>` parameter
shape is more specific than bare `T`. This same specificity applies without
the `params` modifier; it is not a count of inferred parameters or solver work.
An applicable nongeneric exact declaration wins a remaining tie with a generic
declaration after argument quality and the existing binding priorities, not
before them. Explicit generic arguments select eligible generic candidates.
Constructors use the same explicit/typed-evidence/expected-result/default
inference stages; field capture, private bodies and arbitrary witness search
do not infer otherwise unresolved formals. Constructor applicability uses
the call algorithm after type-name selection; constructor-versus-conversion
classification follows [name binding](language/names-and-binding.md#calls-conversions-and-recorded-binding).

The [source grammar](grammar-spec.md#32-types-parameters-and-arguments)
specifies final `params` placement, eligible Copy parameter defaults,
position-preserving named/positional argument mixtures and tuple forms.
Tuple indexing uses a constant `[index]`; labels do not change tuple
identity. End-relative `^offset` indexing and half-open slice bounds retain
their checked source rules. Target-directed lambdas and collection
expressions do not infer a canonical callable representation or permit
stronger hidden obligations on the caller. The [checked witness](language/interface-witnesses.md)
and finite [target-directed callable](language/calls-and-inference.md#delayed-callable-evidence)
source rules are specified. General closure representations, erased callable
dispatch and physical witness codecs remain separate work; user-defined type-pack expansion,
pack indexing/folds, polymorphic callable erasure and implicit callback
vectorization are outside this direction. Current compiler support is
documented in [implementation](../implementation.md).

### Construction, errors and effects

Ordinary explicit constructors are baseline. Independent opt-in `Generate` operations can supply construction, equality/hash, deconstruction, formatting and combined validated withers from declared storage/mappings; no field-name magic, automatic primary-constructor capture, implicit successive setters or parameter-name-only overload distinctions. Omitted update members are `Unchanged`, not a null/default value. Affine and class withers need explicit failure, cleanup, allocation and identity behavior.

The target inference order also applies to constructors: known argument types
establish generic positions, expected-result context may fill unresolved ones,
and contextual literals follow. `new DifferentRandom(new CounterRandom(0),
0xA5A5_A5A5)` illustrates first-argument inference and declared `uint32`
literal contexts; it does not infer field capture, change ownership, choose a
type from constraints or invert optional/box normalization. General constructor
inference follows [calls and inference](language/calls-and-inference.md);
current inference support is documented
in [implementation](../implementation.md).

The target parser contract is `IParsable<TSelf>.TryParse(TextSpan, init ref<TSelf>)`, including user types. `init ref<T>` is a vacant initialization loan: its destination cannot be read before complete initialization and publication. The accepted call-site form is `TryParse(text, init result)`. Conditional initialization facts are normally authored as declaration postconditions using `ensure <expr>`, an ordinary Boolean expression that may compose patterns and short-circuit predicates. A legal value predicate over the projected destination, such as `*result > 0`, implies its initialization; `result is init` tests the descriptor instead. Source-body/path checking must prove reads are legal and may not inspect vacant storage to decide a predicate's result. A successful value predicate establishes initialization. These clauses correlate the output's live/uninitialized state with the declared status/result; they are not a separate conditional-assertion block language. An initialized nullable value (including null) is a live `T` value; an uninitialized outcome has no `T` lifetime, access or cleanup, even if physical storage remains. Callers cannot receive an undiscriminated maybe-live owner or access/clean up a vacant arm. Clause placement follows the [source grammar](grammar-spec.md#31-source-file-structure-and-declarations); correlated-output metadata remains to be specified, and equivalent routine output attributes are not a parallel authoring mechanism. Failure cleans partial initialization according to the declared contract and never fabricates `default(T)`. A selected [completion-output contract](language/async-iteration.md) reserves its destination until checked completion observation; Task shape alone does not change ordinary output defaults. This contract is distinct from a general writable reference. Expected invalid input normally branches on `Try...`/status. Domain outcomes may use response types; there is no mandatory language-wide `Option`/`Result` wrapper or typed-throws failure-set machinery. Current output-mode support is documented in [implementation](../implementation.md).

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
primary-callable `[RequireException(typeof(E))]` default; an explicit
`require<E>` wins, otherwise the exact Core RequirementException is used.
[Requirement failures](language/requirement-failures.md) specify ordinary
checked `new E()` with zero supplied arguments only on failure. E must satisfy
Exception eligibility and actual construction/access/ownership support;
the marker supplies no constructor. Constructor failure propagates its own
failure without an invented E event or retry of another default. Defaults
belong to the check's semantic source owner, not caller/type/module context.
Common null/range conditions may receive recognized diagnostics, but checks do
not infer meaning from arbitrary method names or reevaluate arguments,
properties or user-defined comparisons. The implicit-destination expression
form below follows the same declared output contract; current support for
statement and expression forms is documented in [implementation](../implementation.md).

The expression form lifts eligible output destinations omitted from a call:
`int32 value = require int32.TryParse(text);`. The operation runs once; success
publishes the complete value under that Boolean contract. A failed check
constructs its selected failure without reading missing outputs or erasing
already initialized ones; partial and scope cleanup follow their actual exits,
not fabricated `default(T)` or a new rollback. More generally, bare lifted
`require` demands initialization of
every lifted output, with its full nullable/ownership type; supplied destinations
remain ordinary arguments. An explicit pattern requirement can request
selected outputs or non-nullness instead. This follows declared per-output
availability, including richer statuses, not a `Try` naming convention,
enum-zero guess or universal success group. Patterns and `init` argument forms are
specified in the [source grammar](grammar-spec.md); lifting metadata remains open.

Without omitted outputs, the expression form performs the ordinary Boolean
check and yields `void`/`()` on true; false raises the selected requirement
failure. Explicit destinations retain their actual published values and states,
not an extracted result or rollback. A non-Boolean ordinary status needs an
explicit predicate. An omitted initialized `bool` output, by contrast, remains
its payload and can yield false. See
[Boolean requirements and unit](language/calls-and-inference.md#boolean-requirements-and-logical-unit).

Declaration placement determines contract visibility. A declaration
precondition (`require`) and postcondition (`ensure`) are published promises;
they are executable unless proven redundant, and strengthening preconditions
or weakening postconditions breaks compatibility. Published clauses use a
checked, side-effect-free contract language; body `require` retains general
executable predicates and is an ordinary point check, not an inferred public
precondition. A normally false runtime ensure selects fixed Core
PostconditionException under the [standard category contract](runtime/standard-failures.md#2-runtime-postconditions);
RequireException does not override it, and actual predicate failure retains
its own event. Body-only `static
require` proves a condition at that point and `static ensure` proves the outer
function's postcondition on every normal return; neither is automatically
published. Static forms are compile-time-only, side-effect-free proof checks
in every build. False or unproven required proofs are errors, never runtime
fallbacks or optimizer assumptions. They may use runtime values symbolically,
unlike constant evaluation, which must produce a value. `return` in a result
postcondition denotes the actual result contextually; it is not a new binder
or lambda and does not reevaluate the return expression. A `void` function's
normal result is `()`, with no exposable payload address.
Static postconditions initially apply only to the
outer function body. Proofs respect mutation/alias invalidation.
The [proof/evaluation chapter](language/proof-and-constant-evaluation.md)
owns the selected scalar facts, transfers, predicate catalog and guaranteed
resources; [implementation](../implementation.md) records the narrower current
support. Static proofs do not execute or publish a runtime contract.
The admitted symbolic facts, declaration clauses and contextual result
references are selected logical/source rules, not evidence of a physical codec.

Required static proofs have a versioned portable floor: every conforming
compiler must support its minimum rules/complexity and guaranteed resource
budget independently of optimization level. A compiler may soundly establish
more complex proofs or offer larger budgets, accepting additional programs;
acceptance beyond the floor need not be identical. If a required proof is
disproved or not established, compilation fails with the distinction reported
(including a complexity/analysis-limit reason where applicable), never a
runtime fallback. The accepted v1 scope covers local scalar, boolean, and
initialization facts plus refinement for supported built-in branch predicates
under target arithmetic, with the exact predicate catalog, fact domain,
transfer rules, conformance cases and guaranteed resources defined by that
chapter. Numeric loop induction, private-body call summaries and
alias-sensitive numeric facts are outside this
initial floor. Ordinary pre/postconditions remain runtime-checked unless
sound analysis proves a check redundant, and elimination must preserve required
evaluation, effects and failures. Required constant evaluation is separate:
successful results retain exact target semantics and never fall back to
runtime; its admitted domain, path eligibility, accounting and limits follow
the same canonical owner. Additional domains and physical program/value
records remain separate work.

Where runtime values or checks are permitted, an author may explicitly choose
an ordinary expression or executable `require` instead of requiring constant
evaluation or static proof. This changes the source requirement; the compiler
never silently demotes it. Ordinary expressions may still be folded as sound
optimization, and an ordinary `require` remains runtime-enforced unless
proven redundant. This choice is unavailable where a compile-time value is
inherently required, such as layout, type or metadata formation, and does not
weaken structural lifetime or initialization safety. Illustrations do not
add source forms beyond the selected grammar.

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

Checked handler selection unwinds to each candidate's still-live scope before
testing its catches and filters. Exited inner scopes have completed cleanup;
their effects and failures can therefore affect selection. A false filter
continues to later catches at that scope; if none matches, unwinding proceeds
outward. The selected body is entered only after type/filter success. Pending
failure state remains valid for filters, without retaining invalid inner-frame
references or deferring cleanup effects until a match. This is not a
transaction or a pre-unwind first-pass filter search. The
[local failure chapter](language/local-failures.md) owns the selected logical
source contract; physical transport and native runtime integration remain
separate work.
The separately selected [contained foreign bridge](runtime/foreign-failure-bridge.md)
returns an owned domain packet before resuming local propagation; it does
not substitute foreign filter timing or supply other failure catalogs,
the Core captured-event source contract or a physical handler codec.

On checked unwinding, an authored `finally` runs before the pending failure
continues to its next handler. If that `finally` faults, the new failure
propagates with the pending failure preserved as its inner cause; repeated
failures form a causal chain. An authored lexical `defer` follows these same
rules: each action is registered only when reached and executes in reverse
registration order on exit from its block, including checked unwinding.
An action's expression/body is evaluated at exit, with its referenced
places kept valid through execution. Faulting cleanup is not implicitly
`NoThrow` or automatically unsafe; catching a failure still requires unsafe
recovery. A `defer` action that faults on an otherwise normal exit starts an
ordinary checked failure. [Exception eligibility and catch inputs](language/local-failures.md)
are specified separately. Runtime require construction/defaults have their
[own selected contract](language/requirement-failures.md).
[Core scalar failures](language/scalar-failures.md) select exact zero-data
OverflowException, DivideByZeroException and ArgumentOutOfRangeException
for the specified checked numeric/count domains. A requirement default cannot
replace a failure evaluating its predicate, and floating IEEE results remain
distinct from integer failures.
[Numeric selection members](language/numeric-selection.md) separately define
same-type Min/Max/Clamp signatures and reusable Core InvalidBoundsException.
Clamp's explicit min <= max precondition rejects reversed or NaN bounds after
ordinary argument staging; a value outside valid bounds is normal clamping,
not ArgumentOutOfRangeException. Its failure retains the semantic precondition
owner and ordinary construction/event contract.
[Core CapturedException](language/captured-exceptions.md) separately selects
one opaque affine owner and direct no-argument Capture in an active source
handler body, not filters or separately called helpers. It transfers the
existing event while preserving catch-input extents; named captured throw
requires move and preserves origin/causes/dependencies. A transferred handler
cannot reuse or retire that event. Other failure catalogs, shared/typed
capture families and physical constructor/payload/handler/cause/unwind
representations remain open; bounded compiler
support is described in [implementation](../implementation.md).

Ordinary boolean requirements and conditional outputs preserve their declared
cleanup behavior. Fail-fast and checked-exception policies govern how a failure
propagates; neither permits reading or publishing an uninitialized output.
Current support for declaration clauses, static proofs, typed failure
selection, exception construction and output forms is documented in
[implementation](../implementation.md).

Formatting uses one `IFormattable` → `ITextWriter` protocol with typed fragments/values, no obligatory allocating formatter per type. Target-typed interpolation and collection expressions select registered handler types, such as `[TextBuilder(typeof(TextBuilder))]` or `[CollectionBuilder(typeof(ListBuilder<T>))]` on `List<T>`, not arbitrary similarly named methods. The [constructed type recipe](language/construction-handlers.md) uses the target's declared formals, not an open-generic type value or use-site arity guessing. Construction is eager: holes and collection element/spread-source expressions evaluate once left-to-right into typed captures before that site's builder acquisition and drain; ownership, borrowing and refusal cleanup remain explicit. Named unique transfers require `move`. Nested formatting forwards the same writer so a Point and its scalar fields can reach a custom handler without temporary strings or boxing. Refusal stops future writes but cannot suppress prior capture effects or undo prior output; no replay, rollback or transactional stream guarantee. Expression handlers may consume an explicitly supported typed operation graph, as in `Tensor.Apply(destination, i => x[i] * y[i] + z[i])`; this is not body-divined autovectorization of an ordinary callback. Unsupported expressions diagnose or require a separately declared fallback. Owning text publishes only after full validation/freeze and cleans partial storage. Captured collection spreads enumerate once in drain order; eager source capture does not force an unreached spread or formatter. Builders own accepted elements, clean rejected consumed elements and abandon partial state exactly once. Concrete standard-library APIs require their own adoption; resumable async formatting remains unselected. Synchronous formatter refusal is not a resumable continuation, and no incremental construction-policy or deferred-content interface is adopted.

[TextTarget](language/construction-handlers.md#texttarget-parameter-schema)
is a no-argument parameter contract. An eligible generic content input
receives a checked formatter holding eager typed captures, not a deferred
ITextContent program. Equal complete content shapes in one module share a
generated type, not formatter values or backing. Ordinary values, repeated
formatting and captured ownership/backing retain their own contracts;
annotation adds no implicit boxing, lifetime extension or overload preference.

[Plain value construction](language/construction-handlers.md#refusal-in-plain-value-construction)
maps Boolean refusal to ordinary `require` failure without publishing a
prefix/default target. Operation exceptions propagate normally; explicit
Try/writer consumers keep their own declared status and partial-output
contracts.

[Source builder completion](language/construction-handlers.md#consuming-builder-completion)
transfers the private builder into the selected Core IBuilder static
requirement. Success initializes the complete result; refusal leaves it
vacant, and checked failure cleans remaining owned state without restoring
the builder. Explicit reusable APIs remain separate checked contracts.

[Source acquisition](language/construction-handlers.md#parameterless-builder-acquisition)
invokes the parameterless Core IBuilderFactory requirement after eager
captures. It initializes valid builder output on true and leaves it vacant
on refusal/exception. Runtime context is passed through ordinary explicit
APIs; the language supplies no allocator, capacity hint or hidden caller capture.

[Standard drain operations](language/construction-handlers.md#standard-text-operations)
use exact Core IFormattable/ITextWriter identities and int32 alignment measured
in Unicode scalars, with once-only formatting and checked staging.
[ICollectionBuilder](language/construction-handlers.md#collection-acceptance-and-spreads)
composes factory/completion with by-value item acceptance and the exact
contextual result type. [Standard synchronous spreads](language/iteration.md#eager-collection-spreads)
retain exact factory/direct-cursor, item and close relationships rather than
implicitly moving from borrowed sequences. An already-captured direct cursor
closes even when an earlier refusal prevents its drain; an unreached factory
is not acquired. Neither path defers source capture or adds a count/replay pass.

Allocation, GC, rooted reads/writes, blocking, suspension, I/O, atomics, throwing/no-throw, normal/no-return, cleanup and retirement are independently expressible effect bounds, finer-grained than Core/Foundation/Application dependency layers. `void` is the first-class zero-data Copy type, with value `()`, usable in ordinary value positions and as a zero-argument `func<void>` or `Task<void>` result; `Option<void>` still represents two states. It retains logical arity and initialization without payload bytes and is not an arbitrary result-discard conversion. [Values and statement execution](language/expressions-and-statements.md) distinguishes unit values from `_`, defines ordinary control flow, and requires explicit unused non-unit result discard.

A checked [no-return contract](language/expressions-and-statements.md#no-return-contracts)
applies to any declared result type and removes the actual call's normal
continuation while retaining failure/cleanup edges. `[NoThrow]` is separate.
Returning a task or cursor whose work terminates later is still a normal call
completion. The result retains its ordinary type; no public `unit` or `never`
primitive or implicit discard conversion is introduced. Removing a published
no-return guarantee breaks callers that relied on it.

Ownership transfer and cleanup are explicit: a moved owner becomes vacant,
fresh results may transfer directly, and partial construction cleans acquired
fields in reverse acquisition order. Receiver loans do not transfer ownership;
mutation preserves the owner's cleanup obligation. Actual native-profile
coverage and failure policies are documented in [implementation](../implementation.md)
and the [development native profile](../implementation/native.md).

### Documentation comments

Declaration-attached `///` before attributes/modifiers and `//* ... *//`
comments use GFM-compatible prose. They attach to the following declaration,
including its attribute prefix, only without an intervening blank source
line; an empty line inside the comment's prose is different from a detached
source line. Partial/generated parts do not inherit attachment across files.
Leading prose before the first recognized
metadata heading is the implicit summary, with optional `# summary`.
Documentation adds one slash to the ordinary line/block comment marker.
Recognize documentation-block openers before ordinary `//` line comments.
The whole opening star run selects the fence length; its whole matching
closer has two slashes for documentation, not the ordinary block's one.
Thus `//* ... *//` closes, while `//* ... */` does not. Contents remain
opaque and nonnesting; longer closing-looking star runs reject, as for raw
quote fences. The [lexical contract](lexical-grammar.md#trivia-and-diagnostics)
defines nonoverlapping fences and their exact boundary behavior.
Top-level `# remarks`, `# param name`, `# typeparam name`, `# returns`,
`# value`, `# throws Type`, `# safety` and `# example` headings introduce
structured sections; lower-level headings remain prose. Blank lines remain
within the current section, including the implicit summary; each structured
section continues to the next recognized metadata heading. Recognize headings
from parsed Markdown after removing comment decoration and common indentation,
never from raw-line prefixes inside code, quotes or lists. The finite heading
vocabulary is case-sensitive; unknown metadata-looking headings diagnose.

Declaration-scoped `@value`, `@T` and `@Type.Member` mentions resolve to
checked references, not inside code, URLs or email addresses; `\@` escapes a
literal at-sign. Ordinary inline and reference-style Markdown links retain
their navigation meaning, not implicit symbol-reference meaning; explicit
`xref:` links address qualified/custom references. Backticks denote code
rather than implicit links. Complex references reuse source name/type syntax
where unambiguous and require an explicit signature to select an overload.
Inherited documentation explicitly selects a resolved member; name matching
does not select an interface member, alias or forwarder.
Explicit XML `<see cref="..."/>` and
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
example checks. Tokenization, escaping and metadata-heading classification
follow the lexical contract and
[documentation grammar](grammar-spec.md#514-documentation-comment-grammar-and-tooling).
Partial-document merge precedence, complete member identities, the structured
artifact schema, diagnostic catalog and packaging remain separate work.

### Storage, concurrency, entry and boundaries

Tracing GC handles aliased/cyclic application graphs; ownership handles deterministic native storage and resources. A unique owner may still refer to GC storage; a GC root does not keep a logically disposed native allocation valid. Distinguish storage acquisition, initialization, close, value destruction, storage release and eventual reclamation. Vacant/partially initialized/live/retired states, reverse-order partial cleanup, no-throw/non-suspending generated owner cleanup, direct destination construction and stable-address requirements must be checked. Authored `finally`/`defer` actions may fault under their ordinary checked contracts; they do not relax generated owner cleanup guarantees. Replacing a live value builds the replacement before dropping the old one; explicit reconstruct-in-place may leave vacancy on failure. Pool leases revoke access on return; deferred dead-storage retirement differs from deferred live-value cleanup, with one retirement owner after commitment and explicit completion endpoints.

[Thread-capability declarations](runtime/sharing-and-handoff.md#explicit-source-opt-in-and-conditional-contracts)
require explicit user-type opt-in. Ordinary where clauses following a capability
attribute condition that capability only; clauses on the type header still
constrain every instantiation. Missing eligibility never invalidates an
otherwise valid confined type or turns its ordinary operations concurrent.
The same chapter owns checked handoff, publication, guard and scalar-access
composition under existing unsafe/producer-trust boundaries.

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
semantics and examples are in [samples](../samples.md) and [libraries](libraries.md).

Asynchronous operations return task-like completion, including `Task<void>` for
no result; synchronous `void` remains valid, but `async void` is not permitted.
An async function call starts eagerly and runs inline on the calling flow until
it actually suspends. Awaiting an already-completed operation continues inline;
awaiting an incomplete operation suspends and resumes according to the
operation/runtime scheduling policy, without implicitly capturing the caller's
executor or requiring a worker-pool hop.
Async declaration postconditions are checked before successful task completion
is published, not merely when a task handle is returned.
[Async execution](language/async-execution.md) makes caller argument and
failed-establishment propagation synchronous; after logical operation
establishment, escaping precondition/body/cleanup/completion-check failures
are failed completion even if no await suspends. Non-async task factories
keep their ordinary invocation boundary.
[Async cursor advancement](language/async-iteration.md) separately binds its
exact init destination and mutable cursor access to the returned operation.
While reserved, caller output cannot be read, state-tested, reused or retired;
checked completion observation supplies true/live or false/failure/vacant
after operation cleanup. An already-completed task follows the same rule.
Task status or cancellation request alone does not release the reservation,
and arbitrary Task-returning output defaults remain unchanged.
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
Every started operation retains a responsible owner.
[Core Task<T>](language/task-responsibility.md) is affine: ordinary await
consumes it and produces exact T; a named task requires `await move task`.
Responsibility can instead transfer explicitly to a checked structured owner
or supervisor with error, lifetime and shutdown policy. Every normal and
exceptional exit must settle or preserve/transfer it.
Returning a task transfers its responsibility to the actual caller; a task
cannot be discarded when that would abandon completion/error obligations.
Ordinary destruction never blocks, suspends, cancels or invents supervision. Cancellation
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
Same-named file-local definitions remain distinct through their semantic
module/file-scope identities and definition references. A name-only lookup
must retain the matches or report ambiguity, not pick an arbitrary definition.
Private-state authorization does not make an ambiguous identity precise;
complete reflection API/catalog design remains separate.

The target portable semantic representation carries typed control flow, values
and memory places, cleanup regions, roots, checked operations, construction and
suspension. It is not CLI IL. Source compilers check authored bodies against
contracts; binary consumers validate structure, references, binding and caller
use, then rely on published dependency contracts rather than re-proving bodies.
Contract-bound and implementation-pinned replacement are distinct from static
and dynamic linkage; replacement must preserve capture, output, access, layout
and effect guarantees. The `TRRM` identifier names the evolving semantic-IL and
module format. Its [selected representation](representation/README.md) defines
the current registry, framing and revision rules; additional physical
contracts and proposed draft amendments are not implied by logical support.
Actual module and backend support is documented in [implementation](../implementation.md).

<a id="provisional-source-notation-and-open-decisions"></a>

## Source notation and open decisions

The [source grammar](grammar-spec.md) is normative within its stated scope.
It defines contextual literal typing without fallback, optional/coalescing
and conditional expressions, guarded switch coverage and explicit
constant/wildcard transfers. Open pattern and protocol details remain open,
not implicit adoption of every illustrative API.

Mixed automatic/authored property accessors share one backing field when an
auto accessor or property-local `field` requires it; fully authored accessors
without `field` have no implicit storage. Receiver mutation uses `mut get`
or `mut set`, while `init` alone provides unpublished initialization access.
Constructor `ensure P is init` clauses publish minimum guarantees, and
init-property writes may repeat before publication under ordinary cleanup
rules. These do not authorize later mutation through `init`.

`async` marks an implementation and `await` is a prefix expression. Ordinary
async invocation starts eagerly; there is no `async void` form. Producers
instead use a resolved `[Iterator]` attribute, `yield expression;` and
`return;`, with lazy body start on first advance.
[Generated producers](language/generated-producers.md) associate that callable
with an owned `[IteratorCursor]` named mut unique struct and exact direct
cursor role. Creation is synchronous with creator clauses/effects; cursor
operations govern later items, completion and close. Factory/reusable sequences
are explicit wrappers. `[Iterator] async` requires a named async cursor,
not Task<Cursor> or a sequence factory. `await for` and
ordinary iteration perform structured checked close, including awaited
async close. [Standard value iteration](language/iteration.md) binds exact
Core ISequence<T, TCursor>/ICursor<T> identities, TryMoveNext(init ref<T>)
publication and mandatory synchronous Close, without universal Copy items or
a Current getter. The loop owns/closes its cursor, not its source collection;
named affine direct sources require move.
[Async value iteration](language/async-iteration.md) selects synchronous
GetAsyncEnumerator factory/direct-cursor acquisition, completion-published
TryMoveNextAsync(init ref<T>) and awaited CloseAsync without overlapping
pending operations. [Lending iteration](language/lending-iteration.md) separately
selects readonly/writable sync/async roles, element-place aliases and an
init-reference output with cursor-bound or available checked refined backing.
Writable factories require mut receivers without new backing authority;
retiring the alias/descriptor does not destroy the element or end surviving
reborrows. Async lending composes the same pending completion reservation.
The [general dependency schemas](language/dependencies-and-views.md) select
whole receiver/formal sources, result/init-output and retained destinations,
and per-source ForwardsBacking refinements. Exact member-contract constraints
can require published witness facts without narrowing an unconstrained
generic contract through body inspection. Copies, storage, erasure and joins
retain source versions and complete backing/owner/guard/root/load bounds.
These annotations supply no owner acquisition, sharing authority or view ABI.
Nonstandard producer registration,
scheduling/cancellation APIs and physical frame/phase representation remain
separate work.
Typed/captured failure transport and
catch-filter source forms do not silently swallow filter failures; general
exception transport and ABI remain separate work.

The source contracts select the admitted `ref<T>`, `ptr<T>`, `box<T>`,
`val<T>`, `own<T>`, `init ref<T>`, `mut`, `write` and `move` forms,
checked predicates, the three dependency schemas, Pure/NoAlias,
Layout/Blittable, `require`, TextTarget, the registered builder/literal
schemas, ConstEval and trailing generic defaults within their stated scopes.
This is not an implementation promise. Ordinary
`utf8z("...")`/`utf16z("...")` call syntax alone does not establish a
registered target or provided API. MatchedAccess, ViewOf, Specialize and
NativeExport remain additional annotation sketches, not selected schemas.
Implementation-specific
suffix diagnostics, historical output forms and current conversion/loan
support are documented in the
[development native profile](../implementation/native.md), not additional
source productions or a required migration from legacy parameter modes.
The three dependency schemas above use exact checked source/target rules,
not arbitrary runtime attributes. The remaining sketches do not establish a
general lifetime, exclusivity, specialization or native-export contract.
Physical generic/witness serialization, unsupported family registration,
closed-case metadata, generalized pattern forms and compatibility-forwarder
source schemas remain open. Ordinary source visibility follows the
[declaration rules](grammar-spec.md#31-source-file-structure-and-declarations). Beyond the derived constant-generic subset,
transparent-wrapper eligibility/ABI profiles remain open, as do conditional
copy capabilities, first-class
stored-loan ABI, erased-interface dispatch ABI, span-anchor layout/calling
convention, inline `val<C>`, additional native ownership/managed-boundary profiles, structural unions,
decimal interchange value codecs, text storage, wither omission/overloads,
additional handler/registration profiles and serialization, broader task observation/supervision/cancellation APIs, generic dispatch
selectors and additional exception/handler representations and ABI. Eager async invocation and ordinary-await
continuation semantics, affine Core task ownership, consuming await and
explicit every-exit settlement are accepted. General awaiter registration,
executor and affinity contracts remain open.
Syntax follows checked contracts; actual implementation boundaries are listed
in [implementation](../implementation.md) and staged in [roadmap](../roadmap.md).

## Implementation status

Syntax-only acceptance does not establish semantic validity. Native builds
must diagnose unsupported constructs rather than silently weaken their
contracts. See [implementation](../implementation.md) and the
[development native profile](../implementation/native.md) for
actual support, [roadmap](../roadmap.md) for staging, and [samples](../samples.md)
for runnable examples versus broader design sketches.

## Sources and prior art

[TerraFX conventions](https://github.com/terrafx/terrafx/blob/9def09f6c24a0465035ce004ab240ecce0a97a04/.editorconfig) and [.NET Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/) inform naming, not copied restrictions. [C# scoped/ref escape rules](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/low-level-struct-improvements.md), [C# 13 ref-struct interfaces](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md), [collection builders](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions), [interpolated handlers](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/interpolated-string-handler), [ECMA-335](https://ecma-international.org/publications-and-standards/standards/ecma-335/), [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) and [Arm sizeless types](https://arm-software.github.io/acle/main/acle.html#sizeless-types) are comparison points, not automatic adoption. The current C#/.NET previews are backend/prior-art candidates subject to semantic and ABI verification.
