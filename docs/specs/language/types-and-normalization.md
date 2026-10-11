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
| Interface | Complete behavior instance; bare interface notation supplies no storable value representation |
| Formal | Selected formal identity and declared constraints; no guessed implementation type |
| Optional | One presence layer around the complete normalized inner type |
| `ref<T>` | Checked descriptor for an eligible `T` place, with distinct outer access/pin and exact stored `T` |
| `ptr<T>` | Pointer representation retaining exact `T`, access and target contract; `ptr<void>` may be an explicitly opaque pointer |
| `own<T>` | Unique native indirection to storage containing exactly `T`, not ownership of every target reachable through `T` |
| `box<T>` | Requested managed representation with its outer access; compatible existing managed boxing normalizes without another box |
| `val<T>` | Eligible inline payload representation; type selection alone neither copies nor extracts a value |
| Tuple | Ordered complete element types, arity at least two; labels are source-view information |
| Callable | Callable family, closed ordered logical signature, representation/convention, and registered type qualifications |
| Other registered family | Selected constructor and complete arguments under that family's explicit contracts, not name-based erasure or covariance |

Every constructed form must satisfy its declaration/registration's
formation obligations. Storage also needs a supported representation,
validity, initialization, tracing/root, ownership and lifetime contract.
An abstract interface, sizeless value, or unsupported target form cannot
become storable by supplying a nominal name or arbitrary size number.
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

[Exception eligibility](local-failures.md) likewise retains a resolved
nominal declaration or exact category-witness route without changing complete
arguments, representation or access. Aliases preserve the normalized fact;
an unannotated wrapper does not inherit it from equal layout or an eligible
field. Bare category interfaces are test subjects, not storable payloads.
The marker adds no Copy, default value or valid escaping dependency.
[Runtime requirement construction](requirement-failures.md) likewise needs
the selected complete form's actual ordinary zero-input constructor evidence.
Exception constraints do not establish `new T()`, and a callable's
RequireException recipe is not an additional type capability or inference
source. Alias normalization preserves the selected payload and constructor
context rather than choosing another representation.

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
it is not an interchangeable spelling of access. `init` is a parameter
destination mode, not a representation family or qualifier inside `T`.

`write` controls the immediate access contract of a reference/handle/view.
It is admitted on a class-handle form, `ref`, `ptr`, compatible managed box,
native owning indirection, or an explicitly registered writable view.
It grants no member mutation forbidden by the nominal/family contract.
An inline scalar/ordinary struct has writable places, not a writable-value
type created by `write int32` or `write S`.
An inline payload selected by `val<C>` likewise uses place authority;
it does not inherit a class handle's writable receiver authority.

`pin` qualifies only a `ref<T>` form. It expresses a required pin dependency,
not acquisition by type normalization or a change to `ptr<T>`.
Aliases are expanded before qualification admissibility is checked.
Substitution may make repeated identical access/pin qualifications redundant;
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
requirements, as in `where T : write, [Copy], IReset<T>`.
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
are not admitted. Requirements filter applicability rather than create
constraint-only overloads or improve a candidate's conversion ranking.
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
already-optional substitution retains one presence layer.
`alias Option<T> = T?;` therefore gives `Option<Option<int32>>` the same
type as `int32?`. Explicit `int32??` and `T??`, including intervening
trivia, remain source syntax errors.
Different nested positions, such as `List<int32?>?`, remain distinct.

Normalization never invents a live payload or default. Present null in a
nominal union case remains distinct from another nominal absence case.
`Option<void>` distinguishes absent from present `()`; there is still only
one unit value.
No optional rule commutes `ref`, `ptr`, `own`, or an arbitrary nominal
container with presence: `ref<T?>` borrows an optional slot, whereas
`ref<T>?` is an optional reference.

### Managed boxing and requested access

For a compatible class-handle type `C`, readonly `box<C>` normalizes to
readonly `C`. Nested compatible boxing retains one managed representation.
Its **requested outer access**, not the already-boxed input's access,
determines that resulting managed form.

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
The type expression itself allocates nothing.

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
