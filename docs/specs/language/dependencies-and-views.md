# Dependency sources, captures and views

This chapter owns logical `dependencies-and-views/1` and
`dependency-source-schemas/1`: whole-source relationships, retained
reference/view dependencies, checked backing-only refinements and their
declaration/witness rules.
[References](references.md) owns access and place formation;
[flow](initialization-and-flow.md) owns availability;
[lifetimes](../lifetimes.md) owns ordinary ownership and cleanup.
No source annotation supplies a view representation or acquires an owner.
The shared [definitions](../language.md#definitions-and-notation) explain
copying, aliasing, lifetime and the technical borrow/loan/lease terms.

## Dependency identities and obligations

A dependency relates a particular live value, non-owning access (loan) or
operation to storage and capabilities that must remain valid for its declared extent. It is not
just an address or a type argument. Retain the exact source instance and
storage version, any data and shape-metadata backing, owner/lease/guard,
root and relocation support, publication, load/code lifetime and affinity.
An empty/end view retains those obligations.

The initial declaration vocabulary names whole sources, not private field
paths, byte ranges, arbitrary predicates or lifetime type parameters.
A complete source can conservatively describe several backing objects.
Every declared possible source constrains a caller, regardless of the branch
an implementation chooses. A provider must establish a complete supported
relationship; missing facts are not an empty dependency set.

Source identities are the implicit receiver or a declared formal of the exact
callable/member. The receiver is not parameter ordinal zero. Map interface
and implementation formals through the checked witness, and call formals to
already staged arguments through ordinary named/positional binding.
Copies preserve source provenance; rebinding introduces the actual new source.
Storage-instance versions distinguish retired/reused lifetimes, not every
ordinary write to a still-live place. An aliasable reference can observe later
permitted Copy assignments to that place. Such writes change input/read state
without automatically ending its storage lifetime or freezing an old value.

| Source form | Permitted storage/backing |
|---|---|
| Struct receiver or `ref<T>` input | Its selected `T` place and the complete backing/capabilities carried through that place; the source is not the callee's reference-descriptor slot |
| By-value view or aggregate carrying descriptors | Existing dependencies carried by the staged value; not the callee parameter copy's storage or an invented reference to the caller variable |
| Class/box handle or receiver | Its declared object storage/backing/root relationships; not the staged or caller handle slot |
| Transferred owning value | Its actual live value and declared backing after transfer; surviving ownership needs an explicit transfer destination |
| Independent by-value scalar/unit | No external reference dependency merely because a caller supplied a local |
| Initialization destination | Not a default input source; an explicit inter-output relationship requires its live publication in the same declared outcome |

For a `ref<T>` source, backing-only excludes borrowing that selected `T`
place or its inline substorage. Returning the original reference still
requires the source-storage permission, even if its descriptor bits are copied.
Forwarding a view stored inside `T` can instead use its existing backing.
For a by-value view source, the caller's descriptor variable is never
eligible own-storage in the first place. Generic sources retain the complete
declared possibilities rather than guessing from one concrete instantiation.

An annotation cannot return a reference to destroyed parameter storage,
restore a moved caller binding or move an owner into a result implicitly.
Referencing an inline field and forwarding an allocation owned by that value
remain different. A by-value result's physical indirect ABI is not a source loan.

Direct and nested references use the same lifetime/access reasoning. When
they designate the same actual place/generation with identical supporting
obligations, nesting alone does not make that resolved dependency different.
Keep each declared source permission and provenance, every dependent's live
extent, and all authority/invalidation constraints. A reference to a wrapper's
descriptor field slot selects different storage from the reference carried
by that field. Equal numeric addresses do not identify generations, and
distinct owner or guard tokens retain their own responsibilities.

## Source schemas and targets

Core supplies three resolved, primary-only, nonrepeatable schemas:

| Schema | Meaning |
|---|---|
| DependsOn | Select the complete permitted source set of an ordinary result or initialized output |
| Captures | Declare the sources a destination may retain beyond its ordinary input-use extent |
| ForwardsBacking | Refine listed sources to existing backing only, excluding a borrow of those sources' own descriptor/referent storage |

They use ordinary attribute placement and explicit flat parenthesized argument
lists. Each argument
is the direct `this` receiver recipe or `nameof(parameter)` for an exact
declared formal. A direct text literal naming that formal is also admitted;
`nameof` is preferred. Names resolve against the target callable, even when
the annotation precedes its parameter list. In a member-contract constraint,
they resolve against the selected interface requirement, not the enclosing
caller's parameters.
Here nameof is a schema-specific direct call-form recipe, not a new global
keyword or a call to a same-spelled user helper. Other value computations
cannot substitute for it.

These are compiler-bound source recipes, not runtime source objects or general
constant-string expressions. Do not execute the receiver or a parameter to
bind them. Unknown names, duplicate effective sources, non-source expressions,
incompatible targets and duplicate effective annotation placement diagnose.
An escaped parameter named `@this` is selected by `nameof(@this)` or its
literal name, not by the direct receiver recipe. Same-named user attributes
have no registered meaning.
Accessor annotations first select the exact callable using ordinary
getter/setter/init target rules. An ambiguous property-wide source/formal
target cannot guess an accessor or synthesize its parameters.

An explicit `DependsOn()` declares no borrowed receiver/formal source.
It requires independently valid result state, including any self-owned,
immutable-data, root and load obligations; it does not allocate that state
or excuse an unsupported reference. `Captures()` likewise declares no retained
receiver/formal source. Neither empty set drops independent definition-owned
dependencies or weakens an inherited relationship. `ForwardsBacking` needs at least
one source already present in the effective dependency/capture relationship.
It cannot add a source or independently authorize retention.
Its sources may be a strict subset of that relationship.

### Destination and phase

| Effective target | Dependency/capture destination |
|---|---|
| `return: DependsOn` | Normal result; ordinary async uses its admitted completed payload, while generated producer creation uses its cursor |
| `param(output): DependsOn` or inline DependsOn on an init formal | Initialized inner output value, never the formal descriptor or caller output slot |
| Captures on a constructor | Constructed receiver and its full retained-value extent |
| `this: Captures` on an instance member | Existing receiver/value, including retention that may survive an exceptional exit |
| `param(holder): Captures` | The specified retained destination under its actual storage/access/transfer contract, not a fabricated callee-local escape |
| `return: Captures` | Returned holder/value; unpublished failed construction still requires cleanup |
| Captures on ordinary async implementation or callable with an explicit completion-operation relationship | Responsible completion operation, not automatically its completed payload or a permanently retaining receiver; task shape alone supplies no new relationship |
| Captures on registered generated producer | Creation cursor, under its inherited complete capture relationship |

An untargeted DependsOn on a callable/getter selects its ordinary result;
inline placement on an init formal selects that output's initialized value.
An untargeted Captures on an ordinary synchronous instance member without an
admitted operation relationship denotes
its receiver; a static/top-level callable instead needs an explicit result
or formal destination. `this` is a singleton attribute target only where a
receiver exists, including construction. Schemas such as NoAlias still reject
receiver placement when their own target contract does not admit it.
Untargeted ForwardsBacking on a constructor refines its constructed receiver's
capture relationship; on a callable/getter it refines its result relationship;
inline init-formal placement refines that output. Other capture destinations
require the corresponding explicit target; `method:` selects the responsible
operation only for a callable with that admitted relationship.
Where DependsOn and Captures both
describe the same destination, the refinement applies to that source's
own-storage alternative in both; neither source authorization is invented.

A retained formal destination must designate surviving state: a reference
formal selects its referent, a handle selects its object, and an init formal
selects its published inner value under that output's exact outcome.
An ordinary by-value parameter copy is not a persistent caller holder.
Storing into it can escape only by its separately authorized result/transfer
route, which must retain the nested dependencies. Captures grants no write
or initialization authority and cannot make a vacant destination readable.

Ordinary argument preparation precedes source binding. Before invocation,
the caller must establish the permitted access, live input sources and storage
extents that can support the destination's full possible retained use.
An explicitly selected output source is instead established at its declared
publication phase; its vacancy at entry is not an input read or a live-value
proof. Captures is a
conservative retention contract, not a normal-success-only postcondition:
writing borrowed state into an existing receiver/holder before failure is
not rolled back. A declared exceptional release guarantee may discharge a
bound only under its separately checked contract.

Initialization-output relationships follow their existing outcome and
publication phase. A live view cannot depend on a vacant sibling output.
Establish that source's actual live value and complete dependencies before
publishing a dependent result/output in the same declared outcome.
Returning or storing a view on an exceptional path cannot rely on an output
whose failure cleanup leaves it vacant.
In an async completion relationship, pending output remains reserved until
the checked observation; the annotation supplies no new publication phase.
Task shape alone cannot defer an ordinary init-output contract.
An admitted Task capture relationship can apply to a non-async factory
without changing its synchronous invocation/failure phase. Return-targeted
annotations on an ordinary async implementation describe its completed
payload; callable-operation capture remains the separately selected extent.
[Core Task<T>](task-responsibility.md) is an affine owner of that completion
relationship. Moving it into a wait/holder preserves actual operation,
capture and output-version obligations; neither payload observation nor
task retirement silently shortens independently surviving backing.

Source recipes do not capture caller locals, capture an init destination for
later unsupported work, grant writes or duplicate cleanup responsibility.
An owned input stored by explicit transfer still brings its nested borrowed
dependencies. Ordinary synchronous borrowed-input defaults prohibit other
retention unless the destination relationship is declared.

## Broad sources and backing-only forwarding

The effective ordinary result relationship comes from its explicit DependsOn,
inherited/registered contract or language-versioned default. A dependency-bearing
result with one eligible receiver/input uses the broad source relationship.
With multiple eligible sources, declare the permitted set; do not guess from
body branches. With none, establish independent valid result state.
Independent scalar results need no fabricated borrowed relationship.

Broad permits referencing the source's own eligible storage or forwarding
its existing backing. It does not make a by-value parameter copy eligible
escaping storage. Caller bounds conservatively include every eligible
permitted source-storage alternative and all its carried obligations.
An independent scalar/unit input alone is not an eligible view source.

ForwardsBacking rules out the own-storage alternative for the named sources.
It preserves every carried data/shape, owner/guard/lease, root, publication,
load and affinity obligation. A source descriptor can consequently end or move
when no remaining dependency needs its bytes. Its owner cannot be disposed
merely because the descriptor bytes are no longer borrowed.

```terrum
[return: DependsOn(nameof(source))]
[return: ForwardsBacking(nameof(source))]
public static Span<int32> Forward(Span<int32> source)
{
    return move source;
}
```

This source example presupposes an admitted Span<int32> contract. It transfers
the supplied view and forwards its existing dependencies; it selects neither
an allocation nor a Span representation.
The equivalent annotation may refine only one member of a larger DependsOn
set. The other sources retain their broad relationship.

Backing-only is not immutable contents, stable reads, exclusive access,
NoAlias, permission to structurally mutate storage or a promise that a view
survives close. In particular, [lending](lending-iteration.md) separately
requires published invalidation refinements for advance/close; eliminating
cursor bytes does not eliminate a guard or source released by those operations.
Type/access attenuation still obeys the exact stored-type rules.

## Stored values and flow

Construction, assignment and capture propagate complete dependencies into
the actual destination instance. A wrapper, tuple, field, erasure or projection
does not erase them. A stored view is not permission to store its owner or an
implicit retain. Rebinding a descriptor cannot shed obligations from surviving
copies or from the holder's conservative capture contract.

Dependency-bearing values retain ordinary Copy/transfer eligibility. A Copy
operation duplicates that value's dependency obligations, not its referent,
lease, guard or close owner. Where a protocol prohibits duplication, a Copy
promise cannot bypass it. References and writable views do not acquire
exclusivity merely from write access; independently required guards still apply.

Normal joins retain the union of all possible source instances and versions.
An established existing discriminator can guard availability under ordinary
flow rules, but equal addresses or a private branch cannot replace the
published whole-source bound. Loops use existing scope/site identities;
there is no retained-owner arena or per-iteration capture allocation.

End or transfer a value under its actual contract. Ending a loan/descriptor
does not destroy a referent; ending one copy does not end the others.
Moving an owner can preserve allocation lifetime under its checked transfer
contract without preserving references into its old inline storage.
The retained state's own extent matters even after its last visible handle
ends; dropping a handle alone does not establish release of that state.
Replacement, retirement, guard release, reclamation or unload must not
invalidate a surviving dependent value. Unsafe does not repair that dependency.
Circular source claims do not establish storage liveness or acquire an owner.

Relevant mutation changes input state for [pure evaluation](published-predicates-and-effects.md).
Lifetime validity is not a memory-version guarantee. A permitted write may
invalidate a cached fact without ending a view; structural invalidation has
its actual separate contract. Capturing into pre-existing external state is
not made pure by a fresh descriptor or a proved NoAlias requirement.

The public nominal view remains one Span<T>, not Span<T, TOwner>.
A source instance's dependency set is not a new type identity or an
owner-dependent instance size. No automatic boxing, view allocation,
per-view retain, pointer-to-owner registry or implicit scope extension is
introduced. Every use still needs a supported complete view/value profile.
An owning aggregate retaining a task also retains its
[every-exit settlement obligation](task-responsibility.md#every-exit-settlement-and-ordinary-destruction);
ending the aggregate cannot abandon pending work or unobserved failure.

[Structural field moves and cleanup](ownership-and-cleanup.md) preserve
dependencies of every remaining live field and transferred child.
Field-slot lifetime and a moved handle's allocation lifetime remain distinct.
Partial state is not whole vacancy; neither replacement nor reverse-order
retirement can invalidate a surviving sibling/holder or later cleanup.
A broad whole-source declaration is not narrowed by private field knowledge.

## Published witnesses and generic refinements

[Shared and handed-off extents](../runtime/sharing-and-handoff.md) retain their
actual sources, token/return duties, publication and code/cleanup affinity
through this same flow. ForwardsBacking cannot restore sender authority,
release a guard or make a shallow readonly view immutable. Remaining result
dependencies can outlive an operation's separate return/publication endpoint.

Preserve the broad interface requirement and the separately checked published
implementation relationship. An implementation can use fewer permitted
sources or backing-only forwarding while satisfying a broader requirement.
It cannot add a source, capture destination, access requirement or invalidation,
nor narrow the inherited valid caller domain.

A generic caller uses the broad requirement unless its declaration exposes
and establishes the relevant member/witness fact. The initial conditional
form is a member-contract constraint:

```terrum
[return: DependsOn(nameof(source))]
[return: ForwardsBacking(nameof(source))]
public static Span<int32> ReadUnits<TSequence>(ref<TSequence> source)
    where TSequence : IContiguousSequence<int32>
    where TSequence.via<IContiguousSequence<int32>>.AsSpan : [ForwardsBacking(this)]
{
    return (*source).via<IContiguousSequence<int32>>.AsSpan();
}
```

The fragment presupposes that exact declared interface/view profile.
Resolve the existing complete witness first. The projection must identify
one required callable or property's getter, including its selected result/output
relationship. Missing or overloaded/ambiguous targets diagnose; do not select
by private body or inference from an expected result. The bracketed contract
must resolve to this Core ForwardsBacking schema and identify its exact
requirement receiver/formals.

The default constraint target is that member's ordinary result. An explicit
`param(output):` target can select its exact initialized output value;
other capture destinations use the same explicit target rules as declarations.
Only this ForwardsBacking requirement is admitted in the initial member-contract
constraint; arbitrary attributes or contract predicates are not.
The constraint selects sources in the broad requirement and requires that
the checked published implementation cannot borrow their own storage.
An implementation that excludes a selected source satisfies that source's
refinement without a redundant annotation; one that retains it must publish
backing-only permission. This is the same contract implication used in witness
compatibility, not permission to add a source or infer a private body.
This ForwardsBacking constraint does not itself create conformance,
replace a body proof, improve conversion ranking or establish disjoint
overload domains. Definition checking uses the declared condition, and application
checks it after normal complete-type selection/substitution.
Unsupported or unestablished required facts diagnose.
Conditions on arbitrary runtime state or source paths are not this initial form.
Conjoined member requirements must all hold for the same selected witness
and exact target relationships; they are not alternative implementation choices.

A concrete published witness can establish that fact. Devirtualization,
specialization, code pinning or known current bodies cannot silently narrow
an unconstrained generic function's exported source relationship.
Access-qualified witness projection maps source and destination identities
along with its exact types, phases, retained bounds and other contracts.

## Independent admission and representations

The published logical contract retains the exact rule versions, declaration/
code owner, complete types, receiver/formal identities, source modes, target
kind/output phase, retained extent, captures, backing/capability bounds and
member/witness conditions. Invocation and storage bind that contract to actual
source/destination instances and versions with their complete dependencies;
those dynamic identities are not serialized as fixed declaration instances.
Map the published identities independently of names/rows.
The producer is responsible for the truth of published body contracts;
consumers validate selected record structure, references and support before
affected use, then may rely on those contracts without semantic re-proof. A
structural record or semantic attribute alone supplies no missing contract.
Optional audits do not prove arbitrary external provider behavior or authored
unsafe promises.

Compatible replacement cannot add dependencies/capture, drop a required bound,
replace backing-only with storage borrowing, change a phase/owner or weaken
relied-on validity/invalidation. More precise implementation facts are not
silently adopted by retained callers. Complete target layout and code/load
requirements remain independently compared.
The [binding relation](../representation/binding-and-replacement.md) compares
every retained expectation in its complete using context; exploiting an extra
provider refinement creates a corresponding retained replacement obligation.

Each target profile must define the admitted descriptor/anchor, stored/erased
root and relocation representation, capability validity, call ABI and
cleanup/transfer support. A fixed-size opaque representation may fulfill those
requirements when explicitly admitted; this chapter selects no such layout.
Unsupported stored/erased/escaping uses diagnose before affected use.
The historical TokenCursor/compact-owner support inventory is
[implementation documentation](../../implementation/view-and-owner-profiles.md),
not a smaller normative lifetime model.

Physical source/capture/refinement records, root/loan programs, target view
layouts and codecs remain separately gated by
[metadata](../representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending).
[Conformance cases](../conformance/source-dependencies-and-views.md) are logical
requirements, not compiler or physical ABI conformance.
