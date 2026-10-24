# Complete types and normalization

This chapter owns complete type identity, forward substitution, and
representation normalization. [Names and binding](names-and-binding.md)
select declarations before expansion; [source productions](../grammar-spec.md#32-types-parameters-and-arguments)
determine legal written forms. [Interface witnesses](interface-witnesses.md)
resolve nominal conformance and associated members.
Representation eligibility, value conversion, and target layout are
different obligations; equal bits do not establish type equality.

## Complete type terms

A complete type retains its selected declaration/constructor, ordered
normalized type and constant arguments, representation, and access/other
type qualifications. A generic formal is identified by its owning
declaration and ordinal, not its spelling. A closed nominal type retains
its semantic definition identity, including module/source-unit provenance.
Row numbers, source paths, aliases, and ABI carriers are not that identity.

| Form | Identity and formation requirement |
|---|---|
| Atomic value | One of the mandatory scalar identities, including inhabited `void`; widths with different identities do not merge |
| Nominal type | Selected definition and complete ordered arguments; retain nominal validity, ownership and representation contracts |
| Existential generic view | Selected generic constructor with explicit hidden argument slot(s); each value retains its complete hidden arguments and required evidence, distinct from `void` and inference holes |
| Interface | Complete behavior instance; bare interface notation supplies no storable value representation |
| Formal | Selected formal identity and declared constraints; no guessed implementation type |
| Optional | One presence layer around the complete normalized inner type |
| `ref<T>` | Checked descriptor for an eligible live `T` place, with distinct outer access/backing policy and exact stored `T` |
| `ref<init T>` | Checked non-owning initialization capability for an eligible `T` place; a distinct complete type that preserves the exact `T` |
| `gc ref<T>` | Independently rooted locator for one initialized place in GC-backed storage; not a stable address or a general owner |
| `ptr<T>` | Pointer representation retaining exact `T`, access and target contract; `ptr<void>` may be an explicitly opaque pointer |
| `own<T>` | Unique native indirection to storage containing exactly `T`, not ownership of every target reachable through `T` |
| `box<T>` | Normal shared, stable managed representation with its outer access; compatible existing boxing normalizes without another box |
| `gc box<T>` | Distinct GC-backed box representation; its collector-managed backing and root contract are not erased by normalization |
| `gc C` | Distinct GC-backed class-handle representation; its root does not promise stable address or own independent resources reachable from the object |
| `val<T>` | Eligible inline payload representation; type selection alone neither copies nor extracts a value |
| Tuple | Ordered complete element types, arity at least two; labels are source-view information |
| Callable | Callable family, closed ordered logical signature, representation/convention, and registered type qualifications |
| Other registered family | Selected constructor and complete arguments under that family's explicit contracts, not name-based erasure or covariance |

Every constructed form must satisfy its declaration/registration's
formation obligations. Storage also needs a supported representation,
validity, initialization, tracing/root, ownership and lifetime contract.
An abstract interface or a value unsupported for the requested storage
context cannot become storable by supplying a nominal name or size alone.
A complete type has one declared copy/transfer contract; an instance's
backing address or live/vacant state is not a new type identity.

[Whole-source dependencies](dependencies-and-views.md) belong to particular
live values and published member/capture relationships. One Span<T> does not
become Span<T, TOwner>, change its complete type or acquire a variable-sized
instance when its backing source changes. Forward substitution preserves
source/destination/formal/witness identities and their member-contract
conditions; normalization cannot erase a root, guard, loan or retained extent.
[Core Task<T>](task-responsibility.md) has one affine completion-owner contract
and exact complete T, including its access and dependencies. Default T = void
does not erase another result or permit covariance/Copy. A task-bearing
aggregate carries the task's settlement obligation without gaining a new
type identity from its current operation or terminal status.

[Structural owning-field state](ownership-and-cleanup.md) likewise belongs
to actual storage, not a new type argument or identity. Partial values are
not complete T values crossing ordinary calls or conversions. Copy eligibility,
selected authored/derived Cleanup, field order and complete-receiver bounds
are retained type contracts; equal layout does not erase them.

[Exception eligibility](local-failures.md) requires a class's exact
self-typed `IException<TSelf>` implementation without changing its complete
arguments, representation or access. Aliases preserve the normalized class
and witness; an unannotated wrapper does not inherit them from equal layout or
an eligible field. `IException<_>` is a per-value existential view of an
already-constructed object, not a category payload or concrete construction
target. Its common instance contract is selected; exception eligibility adds
no `Copy`, default value or valid escaping dependency. In an admitted type
position, any nominal generic constructor may form an existential view by
hiding any subset of its type-argument slots, including all of them. Constant
generic argument slots retain their separate semantics. Visible type slots
retain their exact complete arguments in the static type identity. Each value
retains its own complete hidden arguments and required witness/evidence; only
operations whose signatures and contracts are valid for every hidden argument
admitted by the generic constructor's declared constraints are exposed. A
successful generic type pattern may recover one or more concrete hidden
arguments and their required evidence within that branch; each hidden slot can
be captured with its own `var` binder. This does not imply covariance, erasure
to `void`, or an arbitrary dynamic interface-object representation. This
checked pattern is the only general recovery mechanism;
`typeof(Place)` remains static and no runtime type/witness-inspection API is
selected. In particular, `Task<_>` is not `Task<void>` and does not erase its
owner's payload, failure or cleanup obligations; see
[task responsibility](task-responsibility.md#existential-task-view).
[Runtime requirement creation](requirement-failures.md) uses the selected
class's common factory witness. An exception constraint establishes that
factory only through the exact self-typed interface contract, not ordinary
constructor evidence, and a callable's RequireException recipe is not an
additional type capability or inference source. Alias normalization
preserves the selected exception class and witness rather than choosing
another representation.

[Core CapturedException](captured-exceptions.md) is one exact nongeneric
opaque affine identity, not a payload-specific type or Exception constraint.
Aliases preserve its consuming propagation role; Copy, equal layout or an
annotated wrapper cannot acquire it. Its hidden live event and cause/dependency
closure are actual value state, not additional type arguments. Vacancy is
not a default captured value, and an outer ref borrows its holder without
supplying another owner or the captured-throw role.

The zero-data unit type is not a zero-arity tuple or a universal conversion
target. `ref<void>` has no ordinary exposable unit payload place;
unit value positions and opaque foreign pointers remain distinct.
Zero-sized nominal aggregates do not thereby become `void` or acquire
the unit's reference restrictions.

## Qualification and source positions

`mut` belongs to a binding/place or receiver, not a generic type argument.
`unique` belongs to the selected nominal/representation ownership contract;
it is not an interchangeable spelling of access. Declaration `init` selects
the declaration's own storage. The inner `init` in `ref<init T>` instead
qualifies the place designated by that checked reference; it is part of the
complete reference type, not a general `init T` value type or parameter mode.
`Span<init T>` is the separately admitted whole-range element-view form;
`init` is not a free generic type-argument qualifier. The call-site reference
argument remains explicit, as in `Create(ref init destination)`.

`write` controls the immediate access contract of a reference/handle/view.
It is admitted on a class-handle form, `ref`, `ptr`, compatible managed box,
native owning indirection, or an explicitly registered writable view.
It grants no member mutation forbidden by the nominal/family contract.
An inline scalar/ordinary struct has writable places, not a writable-value
type created by `write int32` or `write S`.
An inline payload selected by `val<C>` likewise uses place authority;
it does not inherit a class handle's writable receiver authority.

`gc` is part of complete type identity and selects GC-backed storage only for
class handles, `box<T>` and initialized `ref<T>` locators. It is not admitted
for inline payloads, `ptr<T>`, `own<T>` or arbitrary types. It cannot qualify
the outer uninitialized reference (`gc ref<init T>`); GC backing on an inner
stored `T` is a distinct qualification and must independently satisfy its
backing contract. Thus `ref<init gc C>` may initialize a vacant place
containing a GC-backed class handle, without rooting the destination itself.
The canonical combined access form is `write gc T`;
the GC backing contract is not normalized into normal shared backing.
`gc ref<T>` denotes an independent root for an exact initialized place. It
does not establish address stability or grant access to sibling places.
`ref *root` explicitly reborrows that place as a bounded stable `ref<T>` and
pins the required backing for the result's lifetime. Type normalization
acquires no root or pin.

Aliases are expanded before qualification admissibility is checked.
Substitution may make repeated identical access qualifications redundant;
normalization retains one copy of each admitted qualifier. Source grammar
still rejects a repeated prefix not admitted by its productions.

The optional suffix wraps the preceding qualified type:
`write C?` is optional writable `C`, not write authority over an absent
value or permission to mutate an optional's containing slot.
Applying `write` after alias expansion or substitution to an optional form
qualifies its eligible present type and retains the presence layer.
When managed-box normalization exposes a presence layer, apply the
requested box access to the present managed form, preserving absence.

Qualification never rewrites stored payloads or constructor arguments.
`write ref<C>` and
`ref<write C>` remain different; so do `write Span<C>` and `Span<write C>`.
A readonly containing path may carry an independently writable stored
handle/view without allowing replacement of its slot.
An unknown generic `T` retains its caller-selected complete qualification.
Generic `write T` requires declared formation/operation guarantees for
its admissible representations; it is not a universal make-mutable
transform, inferred authority, or a specialization-time repair.

`ref<init T>` applies to the exact complete `T` after alias expansion and
substitution. It may designate eligible inline values, class handles,
pointers, views or nested references; it does not infer `Copy`, default
construction, pointee initialization or `T : write`. The capability is
non-Copy and preserves one fixed destination identity. After that
destination is completely initialized, a state-dependent attenuation to
ordinary `ref<T>` may strip the completed initialization authority; it does
not permit reinitialization or live replacement. The resulting ordinary
reference follows ordinary backing, dependency and lifetime rules.

`write ref<init T>` is admitted as a fixed destination that must be
initialized before reads and then permits repeated whole-place replacement.
After its first successful initialization, its flow type attenuates to
`write ref<T>`; the one-time init authority is consumed while the outer write
capability remains. This is distinct from `ref<init write T>`, which permits
one-time whole-place initialization followed by mutation through the inner
`T`, without replacing that place. `write ref<init write T>` combines both
only where `T` admits inner mutation. For inline `S`, `write ref<S>` permits
replacement of the whole referenced place, while `ref<write S>` requires
an explicitly mutation-capable `S`; these forms are not interchangeable.

`Span<init T>` is an init-aware element view for a whole range, not a general
`init T` type or a per-element state vector. The range is unavailable for reads
until it is completely initialized. `write Span<init T>` permits element
replacement after that transition; `Span<init write T>` retains inner
mutation only where `T` admits it. Whole-range initialization and failure
settlement are defined by the Span and initialization-flow contracts.

## Generic formal domains and specialization

An unmarked generic type formal admits the common non-unique, Copy-capable,
non-null domain. Copy is the default domain, not an explicit `[Copy]`
constraint. `where T : unique` narrows the domain to unique forms;
`where T : unique?` admits both the default domain and unique forms.
`where T : null?` independently admits nullable complete type arguments
without replacing an optional type with its payload or making a value
present. These modifiers compose independently, so both are required to
admit a nullable unique actual such as `UniqueC?`.

Copy-capable ordinary structs/by-value forms satisfy the default domain.
Unique forms require explicit unique opt-in and may be move-only. A successful
presence test unwraps only the tested value/place and preserves any unique
obligation; it does not alter the formal's exact substitution or other values.

Same-signature generic overloads may differ by constraints only when their
applicability domains are proven pairwise disjoint using closed, exhaustive
properties. Unknown overlap is treated as possible overlap and rejects the
overload set. Inside a generic body, overload selection must be valid across
the formal's declared domain; it is not deferred until a future actual is
chosen. A broad `unique?` formal therefore needs an explicit property branch
to select a default-domain or unique-only overload.

`if (T is unique)` tests a type property, not a value's runtime type or
general reflection. Both arms are checked against the generic declaration's
constraints and their respective refinements. The true arm carries proof of
uniqueness; the false arm proves Copy only where the formal's domain is closed
and exhaustive, as it is for `unique?`. Other false arms establish only that
the tested property is absent. The same rule applies to other published
constraint/property identities for which the generic definition has checked
evidence.

The property may be resolved during Terrum compilation, specialized for a
closed instance during JIT/AOT compilation, or evaluated in shared or
canonicalized generic code using compact implementation-provided type
metadata. Shared execution is not general reflection. In value context the
test produces an ordinary Boolean; flow may retain the relationship between
that Boolean and the tested property through saved values and supported joins.
No proof is inferred from one successful instantiation of an otherwise
uninstantiated generic body.

This separates two supported mechanisms: constraint-selected method bodies
over disjoint domains and an explicit property branch within one generic
body. The branch is the explicit selector when the generic body needs to call
one of those domain-specific overloads. Resource/termination bounds,
open-generic witness persistence and consumers without closed-instance
specialization remain metadata and execution-admission requirements.

## Generic write requirements

[Thread capabilities](../runtime/sharing-and-handoff.md#explicit-source-opt-in-and-conditional-contracts)
also use normalized complete forms, preserving each qualification and carried
obligation. User nominal types opt in explicitly; aliases preserve their
targets, while wrappers/erasure need their own admitted contracts.
Capability-owned conditional where clauses are separate from type-wide
formation constraints and do not supply a new type identity or implicit sharing.

`where T : write` requires the normalized complete type argument for `T`
to carry immediate write access. It is a type-formal requirement, not an
interface, attribute, local-binding modifier, or request to qualify a
readonly argument. It composes with ordinary property and behavioral
requirements, as in `where T : write, IReset<T>`.
The constrained name must identify a type formal of that declaration;
a constant formal cannot carry this requirement.

Check the requirement after ordinary argument selection and normalization.
Aliases retain their complete target; explicit arguments and defaults must
already satisfy it. Constraints do not manufacture inference evidence,
choose a box/view representation, or rewrite an inferred or explicit `T`.

| Complete argument | Requirement outcome |
|---|---|
| `write C`, `write ref<C>`, `write ptr<S>`, `write own<S>` | Satisfies, subject to each form's ordinary eligibility |
| `write Span<C>` for a registered writable view | Satisfies its immediate view access |
| `C`, `ref<write C>`, `Span<write C>` | Does not satisfy; writable inner data is not immediate access |
| `write C?` | Satisfies the present type's access; proves neither presence nor initialization |
| Readonly box/cell containing a writable handle/view | Does not satisfy; do not search inside its payload |

The requirement preserves all other qualifications, representation,
ownership and lifetime dependencies. A `mut` binding or nominal declaration
does not satisfy it on behalf of a readonly type. It implies neither Copy,
pinning, unsafe-call permission, thread transfer/shareability, nor permission
to replace the caller's containing slot. Current loans, guards and required
member authority remain independently checked.

For example, this signature requires an already-writable argument:

```terrum
public T Forward<T>(T value) where T : write
{
    return move value;
}
```

`Forward<write C>(value)` accepts an otherwise valid writable value;
`Forward<C>(value)` fails the requirement even if the supplied value is
writable. Ordinary inference from a writable argument can retain
`T = write C`. The forwarder only illustrates the requirement; forwarding
alone would also be valid without the constraint.

A generic body may rely on its declared write requirement or other checked,
published type facts that entail it. Specific mutation still needs the
appropriate declared member/interface/family operation; write access supplies
no universal `Reset`, indexer, field, or writable storage representation.
Under this requirement, redundant `write T` is admitted and retains the same
complete `T`. Without a formation guarantee at its use, generic `write T`
rejects; concrete instantiations or JIT specialization cannot repair the
declaration. Checked exact-type refinement can establish an eligible form
within its ordinary proof scope, not change a published signature.

`Forward<write T>` at a call site is an ordinary qualified type argument;
it does not impose a requirement on an unconstrained generic declaration.
Qualified formal-declaration shorthand `<write T>` and `where T is write`
are not admitted. This write requirement filters applicability and does not
improve a candidate's conversion ranking. It does not by itself establish
disjoint overload domains; same-signature generic overloads are valid only
when their complete applicability domains are proven pairwise disjoint under
closed, exhaustive properties.
A published generic contract retains the requirement and its formal identity;
an independently compiled body or witness cannot replace it with incidental
private representation facts.

## Forward substitution and normalization

Check written syntax and declaration selection before semantic rewrites.
Then normalize the supplied arguments, substitute each formal's complete
term/value, expand transparent declarations, and normalize the resulting
constructor tree. Validate formal kinds, constraints, qualification,
formation and use eligibility. A normal form does not waive those checks.
Signature comparison uses the result, not an unexpanded source spelling.

Nominal definitions are anchors: normalize their arguments without expanding
their fields or replacing them with carriers. Transparent aliases expand
to their complete target. Defaults follow the selected formal's ordinary
earlier-formal and inference rules; normalization supplies neither a missing
argument nor a new default.
Associated projections resolve through their selected witness, not backward
from a desired result type.

### Transparent expansion and termination

Aliases and associated bindings must have finite declaration dependencies.
Reject direct/indirect cycles, including cycles that grow or change type
arguments and therefore never repeat one closed instantiation.
Normalize an argument before substituting it: finite source nesting such as
`Identity<Identity<int32>>` for `alias Identity<T> = T;` is not a declaration
cycle. Recursive data uses a nominal anchor rather than an infinite synonym.

Normalize each node from its already-normalized children. The optional and
compatible box/payload rules below eliminate redundant constructors; they
do not recreate eliminated layers or search inverse types. Qualification
application is idempotent where admitted. Retain unresolved formal-dependent
constructors symbolically; do not execute bodies or enumerate possible
implementers to normalize them.

This procedure produces one structural normal form for the selected
declarations and published constructor contracts. It is independent of
allocation decisions, JIT specialization, import order, or current field
layout. A malformed/cyclic term diagnoses rather than becoming an unknown
type or a success-shaped fallback.

### Optional normalization

Optional construction is semantically idempotent: applying optional to an
already-optional substitution retains one presence layer. The language
constructor `option<T>` and suffix `T?` denote the same optional type;
`option<option<int32>>` therefore has the same type as `int32?`. Explicit
`int32??` and `T??`, including intervening trivia, remain source syntax
errors. Different nested positions, such as `List<int32?>?`, remain distinct.

Normalization never invents a live payload or default. Present null in a
nominal union case remains distinct from another nominal absence case.
`option<void>` distinguishes absent from present `()`; there is still only
one unit value. Unqualified class handles, checked `ref<T>` descriptors and
raw `ptr<T>` are non-null. Their optional forms use an outer optional layer:
`C?`, `ref<T>?` and `ptr<T>?`. A caller that needs an empty class value uses
an explicit sentinel object. The present checked reference still retains its
complete descriptor, backing, provenance and lifetime obligations;
optionality does not weaken them.

Optional construction does not commute with indirection: `ref<T?>` borrows
an optional slot, whereas `ref<T>?` is an optional reference. `ptr<T?>`
points to an optional slot, whereas `ptr<T>?` is an optional pointer
descriptor. The same distinction composes with both layers present.

### Postfix type shorthands

Postfix `*` and `?` shorthands compose as a left-folded suffix chain before
the outer `write` access prefix:

| Shorthand | Explicit complete type |
|---|---|
| `T**` | `ptr<ptr<T>>` |
| `T*?` | `option<ptr<T>>` |
| `T?*` | `ptr<option<T>>` |
| `T?*?` | `option<ptr<option<T>>>` |
| `write C*` | `write ptr<C>` |
| `write T**` | `write ptr<ptr<T>>` |

The last form grants write access to the outer pointer's immediate referent
(the inner pointer cell). It is distinct from `ptr<write T*>`, which retains
a readonly outer pointer with a writable inner pointer's referent. `write`
remains prefix-only; `T write*` is not a shorthand. Group nested or repeated
prefix constructors explicitly, for example `ref<ref<T>>`, not `ref ref T`.
`option<T>`, `ptr<T>` and `ref<T>` remain the clearer explicit forms for
nested compositions. Shorthand is pure syntax and creates no conversion,
allocation, root or additional authority. `gc` is a separate prefix
qualification, not a postfix shorthand.

### Managed boxing and requested access

For a compatible normal class-handle type `C`, readonly `box<C>` normalizes
to readonly `C`. Nested compatible normal boxing retains one shared,
non-moving managed representation. Its **requested outer access**, not the
already-boxed input's access, determines that resulting managed form.
GC-backed forms are not compatible normal-box normalization targets:
`gc box<C>` and `gc C` retain distinct backing identity. In particular,
`box<gc C>` (a normal box whose payload is a GC handle) is not
`gc box<C>` (a GC-backed box of `C`).

| Written type or substitution | Normal form |
|---|---|
| `box<box<S>>` | `box<S>` |
| `box<write box<S>>` | `box<S>` |
| `write box<box<S>>` | `write box<S>` |
| `write box<write box<S>>` | `write box<S>` |
| `box<write C>` for compatible managed class `C` | `C` |
| `write box<C>` | `write C` |
| `box<T?>` | `box<T>?`, with readonly outer box access |
| `write box<T?>` | `write box<T>?`, with writable present box access |

Here `S` is an eligible inline payload and `C` an eligible compatible
managed class form. No rule assumes that every ownership, lifetime or
representation variant is compatible merely because it is spelled `C`.

For a generic formal `U = write box<S>`, substituting into `box<U>` gives
readonly `box<S>`. The extra constructor adds neither another allocation
nor another handle slot. `U` itself remains `write box<S>`; the explicitly
requested box result attenuates its immediate write access.
Readonly and writable complete types are still unequal. Viewing an existing
writable box through the readonly normalized form is representation-preserving
access attenuation, not exact qualified-type identity.

Type normalization grants no value conversion from readonly to writable.
`write box<U>` requests that capability but the supplying value/operation
must already have the required authority. No cast, generic substitution,
or redundant box repairs an unavailable capability.
Obtaining a boxed value remains an explicit checked operation: it may reuse
a compatible existing box, or allocate/copy/transfer an eligible inline
payload under its construction, failure, rooting and cleanup contracts.
The type expression itself allocates nothing. An explicit `box<T>(value)`
may select GC backing when its expected complete type is `gc box<T>`;
without an expected type, `gc box<T>(value)` selects that backing explicitly.
Neither form copies a unique obligation or introduces implicit boxing.

This rule removes only access attached to the compatible managed layer.
It does not erase qualifications inside a retained inline payload:
`box<write Span<S>>` contains a writable span value in a readonly managed
box, while `write box<Span<S>>` permits the box payload's applicable writes.
A nominal cell containing a handle similarly keeps that field's declared
type. Use such an explicit cell when a separate replaceable handle slot is
needed; another compatible `box` is not that slot.

### Inline payload selection

`val<S>` for an already-inline eligible `S` retains that complete inline
type. `val<box<S>>` selects the box's eligible payload type, not its
managed handle or immediate handle-access capability.
It does not dereference, unbox, copy, move, or allocate a runtime value.
Any value extraction is a separately checked operation.

`val<C>` requires a published inline-payload contract for that class:
finite storage, validity, copy/unique transfer, member receiver rules,
cleanup, rooting and address stability. It is not automatically the
ordinary class-handle type or a Copy clone of its current private fields.
Removing a managed layer removes that layer's access permission; retained
payload fields, views, and inner qualifications remain unchanged.
The resulting inline form uses ordinary place/loan authority.
No universal `val` projection, payload boxing, or interface payload layout
is inferred where these contracts are absent.

## Identity versus checked adaptation

Two normal forms are equal exactly when their constructors/definitions,
representations, ordered complete arguments/constant values, and admitted
type qualifications agree. Aliases and the specified normalizations do not
add nominal identities. Different nominal definitions remain different
even with equal fields, `Transparent` carriers, or zero payload bytes.

[Constant-generic arguments](constant-generics.md#canonical-value-identity)
retain their exact eligible complete domain and canonical carrier value.
Equal values reached through different checked constant expressions compare
equal within that domain, without user equality/hash execution. Transparent
wrappers and enums do not compare equal to another nominal domain or their
carrier merely because their bits or layouts match.

Access attenuation, reference formation, boxing/unboxing, family erasure,
checked refinement and witness projection are operations/relations, not
extra equality rules. Their availability cannot rewrite inferred `T`.
Writable storage remains invariant; there is no recursively inferred
container covariance.
Optional and managed-box construction are non-injective, so their normal
form does not uniquely recover an original generic argument.

Tuples compare ordered element types and arity; labels do not affect equality.
`(void, int32)` is not `int32`. Callables compare family, ordered complete
logical parameter/result signature, parameter modes, and calling convention.
`func<void>` has zero parameters and unit result; `func<void, void>` has
one unit parameter. The bare `func<Args..., R>`/`fnptr<Args..., R>` type
argument forms specify ordinary by-value arguments, including reference
descriptor values; they do not spell initialization-destination modes.
Method/lambda parameter modes must match the selected complete callable
contract; an unsupported mode cannot be disguised as a by-value argument.

Callable labels/defaults and declaration effects/capture relationships are
checked source-view/contracts, not return-only or contract-only overload
discriminators. A registered type-use calling convention or other
identity-bearing qualification remains part of the complete type.
Target formation must preserve the required effects, capture, ownership and
lifetime contract even when the invocation shape matches. A signature match
does not authorize allocated closure adaptation, a copied owner, or a
foreign-convention cast.

[Conformance cases](../conformance/source-types-and-normalization.md) give
required identity, qualification, boxing, substitution and signature outcomes.
