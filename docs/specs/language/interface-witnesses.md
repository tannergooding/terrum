# Interface witnesses and qualified self types

This chapter owns nominal conformance, associated-member resolution, and
checked access-qualified self-witness projection. [Names and binding](names-and-binding.md)
select declarations; [source productions](../grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding)
provide their spelling. [Complete normalized types](types-and-normalization.md),
not member names or representation similarity, identify interface instances.

## Nominal conformance and member identity

A witness is an explicit, checked conformance of one implementing type to
one complete interface instance. A type's declaration, including its closed
generic arguments, supplies that origin. Imports, extensions, matching
members, and adapters of an unrelated type cannot create its conformance.
An explicitly declared adapter has its own nominal identity and witnesses.

A witness records the implementing type, complete interface arguments,
requirement identities, selected implementations/defaults, associated
bindings, and published contracts. A member identity includes its declaring
interface instance and declaration, not just a name/signature or dispatch
slot. Two unrelated `Read` requirements remain distinct even when one
ordinary implementation fulfills both.

An interface's first `<this T>` formal identifies its implementing self
type. An authored conformance binds that formal to the complete implementing
declaration form. Other type/constant formals keep their supplied values;
being equal to the self argument in one instantiation does not make them
self formals. Interfaces without a self dependency need no such formal.

Every requirement must have one selected binding. The producer checks its
logical parameter/result types, modes, generic constraints, receiver access,
caller safety, effects, initialization/output states, ownership, backing,
capture, roots, cleanup, and any declared conditional guarantees.
The [published evaluation contracts](published-predicates-and-effects.md)
include supported forms, complete read/effect bounds and mapped NoAlias
parameter requirements. A selected implementation cannot restrict the
retained caller domain by adding separation requirements.
Unit positions remain logical positions. Equal payload size, a callable
name, or a compatible machine calling convention cannot replace this check.
Private interface helpers are not implementation requirements.

The [async cursor requirement](async-iteration.md#nominal-protocol-identities)
also retains its explicit completion phase, exact init formal/result predicate,
pending reservation and operation/capture/cleanup relationship. A same-shaped
Task<bool> result or an implementation's `async` modifier cannot replace that
check. Forwarding maps the phase/output obligations without early publication
or duplicate ownership; ordinary authored clauses retain their own phase.

The [lending requirements](lending-iteration.md#output-dependencies-and-checked-refinements)
retain the initialized inner descriptor of their exact init formal, permitted
element origins, complete access/backing and advance/close invalidation.
Checked published refinements map their exact conditions and remaining source
obligations. A writable output cannot satisfy readonly init storage through
covariance, and ordinary reference attenuation does not create another nominal
role. Generic callers retain the broad contract unless an available checked
refinement is exposed and proven.

The [whole-source schemas](dependencies-and-views.md) map receiver and exact
formal identities, result/init-output targets, capture destinations and phases.
ForwardsBacking can narrow permitted source storage without dropping carried
backing or changing operation invalidation. Preserve the broad requirement
and separately published implementation fact. An exact
`where T.via<I>.Member : [ForwardsBacking(this)]` condition can require that
fact in a generic declaration; private body inspection, source names or
devirtualization cannot supply it.

[Generated cursor shells](generated-producers.md#owned-named-cursor-registration)
authorize compiler implementations only for their exact selected Core role.
Written slot contracts must preserve the inherited caller domain and
output/access/phase promises; extra guarantees must be established for every
admitted generated instance. Omitted slots inherit those same requirements.
The original producer body retains its semantic code owner, not automatic
private access for authored shell clauses or unrelated code.

Caller-visible names/defaults come from the selected interface contract.
An implementation's differently named parameter does not change an
interface call's labels or rewrite a compiled default argument.
[Requirement-failure defaults](requirement-failures.md) are separately
owned by the source check: an inherited interface clause/default body keeps
its originating member's recipe, while an implementation's authored body
keeps its own default. Dispatch or signature adaptation cannot transplant
one onto the other, replace the bound constructor or alter the retained
failure/channel contract.
Its [TextTarget content capability](construction-handlers.md#texttarget-parameter-schema)
likewise belongs to the selected requirement's exact parameter contract.
Forwarding preserves already formed content and its complete input mode,
not source-hole replay or an implementation-specific annotation choice.
The witness grants only the declared operation. It does not expose an
explicit implementation through ordinary concrete-member lookup or give
the caller independent access to its private target.

### Inherited requirements and defaults

Interface inheritance must have a finite, acyclic declaration graph.
Changing type arguments around a cycle does not make it valid. Close each
base instance with ordinary substitution before resolving its requirements.
Repeated paths to the same closed declaring-member identity share one
requirement; unrelated identities or distinct closed instances do not merge.

An explicit implementation selects its named identity and must satisfy its
contract. Otherwise an eligible ordinary implementation is selected by the
ordinary binding rules. Ambiguous eligible implementations diagnose rather
than falling through to a default. With no selected authored implementation,
use that requirement's unambiguous inherited default.

An explicit replacement of an inherited binding supersedes the binding it
replaces. Deduplicate unchanged bindings reached through several paths.
Distinct remaining defaults for the same identity require explicit
resolution; inheritance-list order, import order, or a matching method name
cannot choose one. A more-derived explicit replacement is not an
import-order preference. A selected type implementation outranks defaults.

Different interface identities may each retain their own valid defaults.
An unqualified use still needs determinate member lookup; `.via<I>` selects
one existing witness. It cannot repair conflicting claims for the same
type/interface pair.
Conformance restriction/access requirements retain their originating
module, source-unit, or type principals through inheritance and projection.

## Associated types and constants

Resolve the selected witness before projecting an associated member.
`T.via<I>.Item` selects the `Item` binding belonging to that witness;
unqualified `T.Item` needs one applicable supplying identity.
An explicit binding wins over its replaceable default. Inherited conflicts
use the same identity/default rules as callable requirements.

Associated bindings preserve complete type identity, constant domain, and
published validity. An alias may normalize to its target; a nominal wrapper,
different qualification, or equal-width value domain cannot substitute for it.
The identity clause `where T.via<I>.Item is U` first resolves `T`, `I`, the
witness, and `U`, then checks exact normalized equality. It is not a new
inference source, an assignability test, or a search for an implementing type.

Expand transparent associated bindings finitely. Recursive dependencies
among binding/default declarations reject, including a cycle that changes
generic arguments on each expansion. A binding to a finite nominal type
does not expand that type's fields; recursive data may therefore use an
ordinary nominal anchor, not an infinitely expanded synonym.

No interface name implies numeric laws, purity, stable reads, covariance,
constant eligibility, or a storage representation. Those are separate
registered/published contracts.
The [selected numeric groups](numeric-capabilities.md) explicitly publish
their member sets, Copy constraints, inherited selection/comparison
requirements and primitive witness matrix. Min/Max/Clamp remain one
capability. The optional scalar composite adds no parsing, formatting,
conversion or evaluation capability.

The resolved [Exception category schema](local-failures.md#1-registered-exception-capability)
can designate an interface as a catch category. An explicit checked witness
to its exact closed instance is nominal exception opt-in; structural lookup
or another type's adapter is not. Inherited category paths follow the same
finite identity/deduplication rules. Category membership supplies no
storable interface value or Copy, and a category underscore catch tests
without manufacturing either.

## Qualified self-witness projection

[Thread-capability and sharing contracts](../runtime/sharing-and-handoff.md)
preserve conditional eligibility, actual token/authority requirements,
publication/return, reentrancy, captures and cleanup affinity through a witness.
Qualified-self adaptation cannot add crossing permission or remove a required
protocol; an interface name or matching lock member is not a capability proof.

An access-qualified receiver does not erase its complete type. In particular,
inference from solely writable by-value evidence retains `T = write C`;
it does not choose `C` merely to make a constraint succeed.
[Mixed-access or invariant evidence](calls-and-inference.md#explicit-selection-and-typed-evidence)
can independently establish the readonly form. Witness selection checks
that established complete argument; it does not retarget inference.
An existing `C : I<C>` self witness may nevertheless supply `I<write C>`
through a checked projection. This is a contract adaptation of that declared
witness, not type equality, an independently inferred conformance, boxing,
or a new nominal adapter type.

### Eligibility

Start with an existing declared witness, including an inherited witness.
Its interface must identify its self formal with `<this T>`, and its bound
self must identify this closed implementing declaration form.
The requested self has the same normalized declaration, closed arguments,
representation, ownership, pin status, and other capabilities; only the
permitted outer readonly/write access differs.

Retain every ordinary interface type/constant argument. Substitute the new
self into expressions actually dependent on the declared self formal,
including inherited base instances and member signatures. Do not rewrite
other occurrences of the concrete type merely because they happened to
equal the original self. No inner qualification or container element changes
by an outer access change.
An interface without a self formal does not gain this projection merely
because its first argument is an implementing type.

Check the requested interface's own constraints and every requirement in
its inherited closure. Projection is all-or-nothing for that interface:
unused members, associated relations, and restricted requirements count.
A safe `Equals` member cannot rescue an incompatible `GetSelf` requirement.
A separately selected smaller base interface is checked on its own closure.

### Source-equivalent contract adaptation

For each requirement, retain its selected authored target or default origin.
Construct an ordinary forwarding operation with the requested contract.
Argument adaptation is from a requested input to the selected target's
input; result adaptation is from that target's result to the requested
result. Each uses only identity or already-permitted access attenuation.

| Obligation | Required adaptation |
|---|---|
| Receiver | Evaluate/capture once; satisfy the selected target's access and storage contract without new authority |
| Ordinary value inputs | Preserve their full types, source order, copy/transfer behavior, and dependencies; permitted outer access attenuation may satisfy a readonly input |
| Reference descriptors | Apply only their existing access attenuation; never change their stored inner type or manufacture a place |
| Initialization destinations | Preserve mode and complete destination type exactly; no covariant output storage or fabricated initialized value |
| Lending output relationships | Map exact init-output/source identities, access, broad/refined backing and invalidation conditions without a new loan extent |
| Whole-source/capture relationships | Map exact receiver/formal and destination identities, broad/backing-only permissions, retained extents and member-contract conditions |
| Results | Preserve the declared result or attenuate permitted access; no authority upgrade, implicit discard, boxing, or new ownership |
| Associated bindings | Retain the selected concrete binding; check all requested relations with the newly substituted self |
| NoAlias parameters | Map exact formal ordinals and actual receiver/input/output regions; preserve required separation without assuming fresh backing from qualification or forwarding |
| Safety/effects/dependencies | Satisfy the requested published bounds without added caller obligations, capture, failure, cleanup, or invalidated sources |

The forwarding operation must be source-expressible under the ordinary
ownership, call staging, and cleanup rules. It cannot copy a unique value,
invent a two-phase loan, inspect an implementation body for a stronger
promise, change cleanup timing, or silently materialize a new owner.
No additional callback invocation or operand evaluation is permitted.
Physical lowering may eliminate the adapter, but cannot weaken its checks.

For example, these illustrative interface/generic declarations retain full
type inference:

```terrum
public interface IEqual<this T>
{
    public bool Equals(T other);
}

public bool Same<T>(T left, T right) where T : [Copy], IEqual<T>
{
    return left.via<IEqual<T>>.Equals(right);
}
```

Given `C : IEqual<C>` with an eligible readonly `Equals(C)` target, a call
with two `write C` values keeps `T = write C`. Its projected call has the
ordinary forwarding behavior:

```terrum
public bool ProjectedEquals(write C receiver, write C other)
{
    return receiver.via<IEqual<C>>.Equals(other);
}
```

Both arguments may attenuate to the existing readonly contract; the Boolean
result needs no adaptation. The recorded target is the existing witness,
not another overload search performed inside the adapter.

Conversely, for `IGetSelf<this T>` requiring `T GetSelf()`, a selected
authored target declared to return `C` cannot provide `write C`.
Its body returning a freshly created object or the original receiver does
not strengthen the published result. The projection rejects, even when
that member is unused by the current generic body.
An explicit `Use<C>(writableValue)` may still use ordinary argument
attenuation and the original `IGetSelf<C>` contract.

A selected generic default may be instantiated normally at the requested
self when its published generic contract supports that instantiation.
Check its declared constraints and retain the same default definition.
This is not inspection or rewriting of a concrete implementation body.
A selected incompatible authored override cannot be bypassed by falling
back to that default.

Preserve associated bindings rather than choosing new defaults. For example,
an existing `Item = C` remains `C` in a projected witness unless a separately
published qualification-polymorphic binding explicitly provides otherwise.
If a required relation instead demands `Item is T`, substituting
`T = write C` makes that retained readonly binding incompatible.
No such binding declaration mechanism is introduced by this chapter.

### Resolution and termination

At a complete receiver type, resolve an exact existing witness first.
Otherwise consider only checked self projections rooted in that type's
declared/inherited nominal witnesses, together with their inherited views.
All type arguments are already determined; constraints do not infer an
unknown implementer, choose a type argument, or search arbitrary adapters.
Incompatible projections contribute no conformance; they do not retry
inference with weakened `T`.

Deduplicate the same origin/target projection reached along several paths.
If distinct available origins claim the same complete target interface,
their requirement mappings and associated contracts must agree or the
claim diagnoses. Import order, default order, or a cheaper physical thunk
cannot resolve the conflict.
An exact witness is not replaced by a projected alternative.

Memoize goals by complete implementing type, requested interface, original
witness, and projection rule. Recursive self constraints may refer to a
finite goal graph anchored in those explicit nominal declarations.
A recurring exact goal can close that graph only when every nonrecursive
member/constraint obligation is discharged; recursion alone is not
conformance. Unrooted cycles, unresolved obligations, and growing
binding/alias/inheritance expansions reject. No numeric proof or speculative
body execution is needed to discover a witness.

Projection availability is constraint satisfaction, not an argument
conversion or an overload-quality bonus. Normal name lookup, inference,
applicability, and ranking remain unchanged. `.via<I>` selects the resulting
existing/checked witness; it cannot create a missing one.

## Publication and independent use

A projected witness has a logical identity derived from its original
witness, complete requested interface/self, retained mappings/bindings,
adaptations, and rule version. Publish those requirements when an
independent consumer must bind, dispatch, or rely on them.
The broad requested interface contract remains the generic caller's
contract; private specialization does not expose a stronger refinement.

Replacement compatibility must preserve the projected obligation as well
as the original witness. A replacement that still satisfies `I<C>` can be
incompatible with a retained `I<write C>` adaptation. The consumer checks
the published projection and selected targets, not current private bodies.
The [metadata requirements](../representation/logical-records.md#nominal-and-qualified-self-witnesses-encoding-pending)
do not assign new table kinds, clause bits, opcodes, or a dispatch ABI.

Projection neither introduces stored interface values nor opens an erased
hidden self. Two erased receivers do not acquire a shared self identity
from equal interface names. Existing representation, opening, backing,
root, affinity, and dynamic-callability obligations remain distinct.

[Conformance cases](../conformance/source-interface-witnesses.md) cover
member identity, associated resolution, checked adaptation, and publication.
