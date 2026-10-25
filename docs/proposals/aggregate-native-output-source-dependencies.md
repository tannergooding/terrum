# Aggregate native-output source dependencies

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract workflow.** This is a narrow applicability
recommendation for [aggregate native outputs](aggregate-native-outputs.md),
not approval of that proposal, an amendment to normative owners, or evidence
of compiler support. Implementation may use this direction provisionally
under that authorization. Approval, implementation and qualification remain
separate.

## Decision and existing authority

Reuse the resolved Core **DependsOn** and **ForwardsBacking** source schemas
to describe the **published inner aggregate** of an exactly selected
`NativeOutput` formal. Extend their output-target applicability; add no new
attribute, source recipe, relationship code or wire record. NativeOutput
continues to own initialization, reservation and result-correlated
publication. A dependency annotation grants none of those permissions.

The accepted source vocabulary already expresses the needed relationships:

| Owner | Already accepted; boundary that remains |
|---|---|
| [Dependencies and views — Source schemas and targets](../specs/language/dependencies-and-views.md#source-schemas-and-targets), [Destination and phase](../specs/language/dependencies-and-views.md#destination-and-phase) | Primary-only, nonrepeatable DependsOn/ForwardsBacking; exact receiver/formal recipes; initialized-inner-output destinations, explicit empty sets and backing-only refinement. Raw NativeOutput applicability is not selected there. |
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations) and [§5.5 dependency declarations](../specs/grammar-spec.md#result-dependency-declarations) | Inline attributes and `param(name)` selectors already exist. The dependency explanation targets initialized outputs, not arbitrary `write ptr<T>` pointees. No new attribute production is needed. |
| [Contract clauses §6](../specs/representation/contract-clauses.md#6-dependency) and [general dependency logical records](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending) | Shared Dependency source/relationship/phase codes and explicit empty rows are accepted. The current destination domain is checked initialization output; the logical owner explicitly excludes native-output destinations. |
| [Aggregate-output candidate — OutputState and Dependency application](aggregate-native-outputs.md#outputstate-and-dependency-application) | Proposes that domain extension, success-only dependencies and an explicit empty-set default while preserving independently required sources. It does not specify which source attribute placements select this raw formal's inner value. |
| [Private-persistence candidate — Formation, selectors and dependencies](aggregate-native-output-private-persistence.md#formation-selectors-and-dependencies) | Proposes an explicit dependency collection with those shared meanings. It is not source-schema approval or proof that the binder can author nonempty sets. |

Thus the existing forms are sufficient **syntax and relationship vocabulary**,
but their application to the new raw output needs an explicit semantic
amendment. This document supplies that decision, not a new lifetime model.
It intentionally supplements the two aggregate-output candidates; it does
not replace their formation, state, ABI, standard or private schemas.

## Candidate source applicability

All new applicability choices below are **unapproved**. Preserve
`dependencies-and-views/1` and `dependency-source-schemas/1` meanings outside
this expressly proposed target extension.

### Destination and placement

An eligible destination is an explicit formal of an otherwise valid
aggregate NativeOutput declaration: exactly `write ptr<T>`, with the
prerequisite proposal's closed, positive-extent, directly interoperable
`Blittable Copy` aggregate T and complete NativeOutput condition.

Admit these equivalent primary-contract placements:

* On the callable, `[param(output): DependsOn(...)]` and
  `[param(output): ForwardsBacking(...)]`, selecting that exact formal.
* Directly on that formal, `[DependsOn(...)]` and
  `[ForwardsBacking(...)]`, alongside its NativeOutput annotation.

Both select the **complete T published by this output**, never the pointer
descriptor, callee parameter slot or caller destination slot. Inline and
named placements share one nonrepeatable occurrence per schema/destination;
combining them for the same effective annotation diagnoses. Different output
formals have independent relationships. Parenthesized source lists are
required, including `DependsOn()`.

An arbitrary pointer formal does not become an output through DependsOn.
Reject this new applicability without the complete eligible NativeOutput
contract. `return:` continues to select the ordinary status result; it does
not select an output. Untargeted callable DependsOn/ForwardsBacking retain
their ordinary result interpretation. A `this:` destination does not select
an output, and fields, callbacks, function-pointer type uses and secondary
partial declarations acquire no new attachment permission.

### Source recipes and selection

Use the accepted flat recipes only: direct `this`, `nameof(parameter)` or
a direct literal naming that exact formal. Resolve against the annotated
callable even before its parameter list. Reject computed strings, field
paths, ordinals as source arguments, duplicate effective sources and unknown
names. `nameof(@this)` selects an escaped formal, not the receiver.

Bind source identities to **already staged actual arguments**, through the
checked named/positional map, in ordinary source evaluation order. Do not
re-evaluate arguments or treat caller variable names as persisted sources.
Keep actual source instances, storage generations and complete carried
obligations; normal writes do not automatically create new storage lifetimes.
Recheck required validity at invocation under the existing call contract.

Source interpretation remains the accepted whole-source interpretation:

| Selected source | Meaning for this output |
|---|---|
| An admitted initialized input with eligible own storage | Broad permission can borrow that selected storage or forward its existing backing. No reference to a destroyed callee parameter copy is permitted. |
| By-value descriptor or aggregate carrying descriptors | Forward its actual carried dependencies, not the caller's descriptor variable or the physical callee/indirect-ABI copy. |
| Raw pointer input | A by-value raw descriptor, **not** a checked reference to its pointee. Preserve any independently established, supported backing/provenance; do not infer pointee initialization, extent, ownership or lifetime from the pointer or this annotation. Missing required backing facts are not an empty set. |
| Receiver | Only the actual receiver's independently admitted storage/backing/capability relationship. No fabricated receiver slot, owner, strong hold or pin. |
| Independent scalar/unit | Supplying it from a local creates no external borrowed storage. Naming it cannot justify a pointer into the callee's scalar parameter slot. |

Each input/receiver still needs a complete admitted native signature and
representation. The generic source chapter's `ref<T>` reasoning does not
make checked references foreign Blittable parameters. A raw pointer derived
through an independently supported stable-reference conversion keeps its
actual supporting dependencies; this proposal introduces no such conversion.

**Target-only extension:** a NativeOutput formal is not newly admitted as a
dependency source. `nameof(output)` in a source list does not project its
future inner T, and its `&init` reservation is not a first-class escaping
pointer source. Reject self-dependencies and raw-output-to-output/result
source claims, including backing-only claims, in this bounded amendment.
Do not reinterpret them as ordinary initialized inputs or silently omit
them. The accepted checked-init inter-output rule remains unchanged.
Admitting a raw output's inner value as a source would need a separately
explicit projection/publication decision; borrowing its destination address
would also violate this NativeOutput protocol's call-extent no-escape rule.
Neither is required to express dependencies on existing inputs/receiver.

### Broad, borrowing and forwarding

DependsOn names the complete **permitted set**, not an assertion that each
source is actually used, a data-computation dependency, or permission to
discover sources from the provider's body. For every listed source, broad
permission includes its **eligible own-storage borrowing and existing-backing
forwarding alternatives**. Callers conservatively retain all possible
sources, not their intersection or only the provider's current branch.

ForwardsBacking must name at least one source already in the effective
output relationship. It may refine a strict subset. It excludes that
source's own-storage alternative and preserves every carried data/shape,
owner/lease/guard, root/pin, publication, load/code and affinity obligation.
It cannot add a source, manufacture backing, authorize retention elsewhere,
or release an owner merely because descriptor bytes are no longer needed.
Forwarding the original checked reference still borrows its selected
referent; copying its bits does not make that borrow backing-only.

For these source-authored output relationships, normalize each broad source
to shared `relationship=3` (declared union); a ForwardsBacking refinement
normalizes that source to `2`. These are alternatives permitted for one
source, not two duplicate rows or a claim that both are used. Existing
`relationship=1` means storage borrowing only; this amendment adds no
borrow-only spelling and does not widen an independently restricted contract
to a union. Ineligible storage alternatives never become eligible merely
because a row says union.

### Explicit empty set and default

NativeOutput's proposed **empty receiver/formal source default** applies
only when no other effective declared/inherited/registered relationship
supplies this destination's sources. Do not apply the ordinary single-input
borrowed-result default to this raw output. Both an otherwise unannotated
NativeOutput and explicit `DependsOn()` normalize to the explicit empty row,
but only the latter is an authored empty-source assertion.

An explicit nonempty DependsOn replaces that raw-output default, not
independent obligations. Resolve inherited/registered contracts under their
existing compatibility rules; do not overwrite them with an empty set.
`DependsOn()` cannot erase required type, definition, external backing,
provider/code, target or inherited obligations. If a required relationship
cannot be represented or established, diagnose rather than encode empty.
A separately published compatible narrower implementation fact does not
rewrite the broad contract retained by its callers.

ForwardsBacking alone cannot populate the empty default. It requires an
already effective source set and is invalid when that set is empty.
Neither the default nor explicit emptiness validates arbitrary pointer
fields: a live aggregate's raw pointer bits still grant no checked
dereference, pointee state, rooting or ownership.

## Native roles and outcome semantics

Admit only the prerequisite proposal's **bodyless direct callable
NativeImport**, or its **exact bodyless foreign native-interface method**
with independently admitted interface layout, effective NativeCallable
convention, receiver and dispatch contract. Follow
[native declaration roles](../specs/native.md#native-declaration-roles) and
[Windows x64 foreign admission](../specs/targets/windows-x64-call-interface.md#1-foreign-primitive-admission).
`extern`, NativeCallable, an address, equal slot number or same-named
attribute alone does not establish the role.

A direct import is static and has no `this` source; an explicit
receiver-like argument is selected with `nameof(thatFormal)`. An interface
method's implicit receiver is a distinct source kind, **not formal zero**.
Inherited slots retain the declaring method's contract and checked source
mapping. An interface convention default supplies no output/dependency
facts. This does not admit the prototype NativeVTableMethod's explicit
first-pointer convention as an implicit receiver or enable Terrum-authored
callback/slot bodies without separate verification support.

| Phase/outcome | Required dependency behavior |
|---|---|
| Argument staging and before Invoke | Establish live eligible input sources and backing adequate for the output's full possible use. Preserve source-order reservations and detect later argument effects that invalidate them. Source annotations neither read the vacant output nor grant competing access to its reservation. |
| Normal success | After invocation postconditions and phase cleanup, publish complete T with its effective dependencies, correlated with this saved result and destination generation. Every dependent copy/nested field retains them for its actual lifetime. |
| Normal false/complement result | Publish no T and no output dependency holder, even after partial/full byte writes. No complete-T cleanup, rollback or hidden owner acquisition follows. Independently surviving argument effects or receiver captures are not undone. |
| Status not yet discriminated | Keep conservative maybe-live state and possible output dependencies. Lack of a result test does not permit reading T or invalidating backing needed by a possible live output. Existing saved-discriminator/invalidation rules apply. |
| Cancellation before Invoke | Release only the abandoned unentered reservations and their prospective output use; destination remains vacant. Preserve earlier argument effects and their independent dependencies/responsibilities. |
| Admitted failure after Invoke begins, including postconditions/cleanup before Publish | No normal success/false relation is fabricated; retain the governing indeterminate exceptional state. Neither empty sources nor ForwardsBacking proves vacancy, reusable storage, completed cleanup or release of supporting obligations. Recovery requires independent exceptional/state evidence. |

Multiple outputs keep independent source sets and conditions over the one
captured result. There is no inferred all-outputs-success relation. A
DoesNotReturn declaration still requires valid supported contracts, but has
no normal publication. NoThrow retirement and independent failure-containment
obligations are unchanged.

The provider may use the **output destination address only during this
invocation**. No hidden capture, returned alias to that storage, asynchronous
use or retained reservation is authorized. A published T may depend on
listed **input/receiver backing** beyond the call; that is the declared
output relationship, not foreign retention of its destination address or
permission for unrelated receiver/global storage. Such other retention
requires its own supported contract. No new Captures applicability is added.

The destination remains caller-owned storage for a `Blittable Copy` payload;
no foreign-resource or owning-child acquisition is added.
DependsOn/ForwardsBacking perform no ownership transfer, COM acquisition/
AddRef, rooting, pin acquisition, marshalling or lifetime extension.
Provider truth, source backing, aggregate validity, target layout/ABI,
caller-unsafety and provider/code lifetime remain distinct obligations.
Loading a symbol or satisfying a dependency source set proves none of the
others. A foreign declaration is an authored assertion about that provider;
ordinary consumer checking does not re-prove its implementation.

## Source fragments

These are **proposal-only declaration fragments**, not runnable or currently
supported examples. Short names mean the resolved Core schemas selected by
the prerequisite proposals, not the existing same-spelled interop prototype.
Assume exact foreign layouts, valid provider behavior, and independently
admitted backing/access for the input pointer. No input range/access schema
is selected by this example.

```terrum
[Blittable]
[Layout(LayoutKind.Sequential)]
public struct ByteSlice
{
    public ptr<uint8> Data;
    public uint32 Count;
}

[NativeImport("sensor.dll", "TrySlice")]
[param(slice): DependsOn(nameof(source))]
[param(slice): ForwardsBacking(nameof(source))]
extern unsafe int32 TrySlice(
    ptr<uint8> source,
    uint32 count,
    [NativeOutput(When: NativeOutputCondition.NonZero)]
    write ptr<ByteSlice> slice);
```

The **alternative inline placement**, replacing the two callable-level
dependency annotations rather than duplicating them, is:

```terrum
[NativeOutput(When: NativeOutputCondition.NonZero)]
[DependsOn(nameof(source))]
[ForwardsBacking(nameof(source))]
write ptr<ByteSlice> slice
```

On success, the inner ByteSlice forwards whatever supported backing the
staged source actually carries. The caller's source-pointer variable can
end/rebind when no other relationship needs its bytes; its required backing
cannot end while a dependent slice copy survives. Neither annotation proves
that `Data` may be dereferenced or `Count` is an authorized extent.
Without the nonempty source contract, NativeOutput defaults to no borrowed
receiver/formal source; it does not guess `source` because this provider
happens to return an interior pointer.

## Metadata, binding and verification

Use the prerequisite aggregate-output amendment to shared Dependency
`destinationKind=2` with the **direct explicit formal ordinal** and
`phase=1` normal publication. No NativeOutput-row indirection is added.
Sources use `sourceKind=1` with the exact explicit formal ordinal, or
`2` with ordinal zero for the receiver. The explicit empty source row has
source kind/relationship/ordinal all zero, cannot coexist with nonempty
rows, and must not be represented by missing required data.

Use the [private candidate's existing collection](aggregate-native-output-private-persistence.md#one-output-collection-per-declaration)
with the same meanings. This adds source/applicability validation, not a
revision, field, tag or second codec. Its explicit private receiver-position
conversion remains necessary; never serialize an implicit receiver as
logical formal zero. No new standard Feature is allocated: retain the
prerequisite proposed Feature 8/10 closure and all independently required
source/capability/target support. Equal version bytes still require
[exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts).

Producers must bind the destination/source recipes, check source and native
role eligibility, validate effective sets/refinements, and preserve complete
facts through bound traversal/rewriting, mandatory verification, copying,
joins, erasure, persistence and lowering. The normalized declaration must
represent destination, sources, relationships and phase, not just a
return-forwarding Boolean. Source compilation checks its actual caller
states; false foreign promises remain the provider/declarer's responsibility.

Consumers validate selected schema, required support, owner/native role,
complete types, ordinal namespaces, source eligibility, phases, uniqueness,
empty/nonempty conflicts and relationship codes before affected use.
Reject a required raw-output source or unsupported nonempty relationship;
do not clear it, treat optional Attr data as authority or fall back to empty.
No semantic re-proof of arbitrary producer/foreign bodies is required; see
[reader trust](../specs/representation/reader-trust.md).

Retain every expected source permission through
[binding/replacement](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions).
Adding a source, weakening backing-only to storage borrowing, changing phase,
or dropping relied-on backing is not compatible servicing. More precise
provider facts can satisfy a broader expectation under the accepted directed
rules, but exploiting them creates retained obligations; the private
candidate's selected exact comparison remains its stricter checking rule.
Remap logical formals, receiver context, complete types and declaration/code
origins together, never by name, equal address, row number or layout.

## Implementation evidence and remaining gates

These are inspected worktree boundaries, **not passing qualification**:

| Exact code/support owner | Observed gap relevant to this amendment |
|---|---|
| [`Semantics/Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), `Declare` and `BindResultDependencyParameters` | The DependsOn path is return-targeted, nonempty `nameof` lists for internal compilation-local `Span<Token>` results. The inline parameter-attribute loop admits NativeOutput, not general source schemas. It does not implement this output target, DependsOn(), literal/receiver recipes or ForwardsBacking. |
| [`Semantics/BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `BoundFunction` and `DirectNativeOutput` | `ReturnsForwardedBacking` and `ResultDependencyParameters` describe Span results and are marked never persisted. The inspected output tuple has parameter/pointee/condition/failure fields, not output source sets, phases or borrowing/refinement modes. Neither carrier is a general output dependency model. |
| [`Semantics/Binder.DirectNativeImport.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.DirectNativeImport.cs), `BindDirectNativeOutputReservation`; [`SemanticVerifier.DirectNativeImport.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.DirectNativeImport.cs), `NativeOutputDestinationFact` / `CreateNativeOutputCallFact` | The reservation identifies a local/pointee; the flow fact records local, result condition and destination version. Successful state correlation alone does not attach whole-source dependency/capability sets to the published aggregate or its copies. |
| [`ScalarFrontendInvocationProfileTests.PrivateSpans.cs`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Frontend/ScalarFrontendInvocationProfileTests.PrivateSpans.cs), `PrivateSpanModuleKeepsReturnedViewsCapturesAndSourceFreeArtifactsClosedAsync` | Existing cases use return DependsOn for a private Span helper and expect producer rejection. They are not native-output dependency persistence or general source-schema evidence. |
| [Hosted support](../implementation.md#hosted-compiler), [view/owner profiles](../implementation/view-and-owner-profiles.md) and the two prerequisite proposals | Aggregate source experimentation, compilation-local view loans, private persistence and standard representation have separate gates. None establishes this amendment's support. |

An implementation limited to empty native-output sets must reject required
nonempty sets, including declaration-only imports, before affected use. It
cannot claim complete support for this source amendment or the proposed
standard path by passing empty-set examples.

Adoption needs focused changes to the dependency chapter's **Destination and
phase** and **Source schemas and targets**, the grammar's **§5.5** explanation,
and native/output conformance cases. The aggregate proposals' shared
clause/logical-record applicability and private collection must incorporate
these exact source checks. Normative owners and existing proposal contracts
are unchanged here. There is no undecided source syntax or wire choice
inside the recommended target-only amendment; approval, complete backing/
capability representations, implementation and qualification remain gates.
Raw-output-as-source projection and wider ownership/region protocols are
excluded, not implicitly approved prerequisites.

## Alternatives and required cases

**Recommend the narrow applicability amendment.** A substantive redesign
could put source sets/refinements inside NativeOutput or introduce a separate
native dependency/region schema and codec. That could address broader
pointee, inter-output, retention and ownership protocols, but would duplicate
the accepted source recipes, relationship algebra, phase/binding rules and
shared Dependency carrier for this use. It needs more decisions without
solving a missing expressive requirement here. Merely treating every
`write ptr<T>` as an initialized-output target is smaller textually but
unsound: write access is not initialization or publication authority.

Future implementation must cover these **design acceptance cases**, using
unrelated synthetic providers and public compiler APIs; none ran as part of
this documentation change:

| Case | Required outcome |
|---|---|
| Named versus inline placement; reordered named call arguments | Same inner-output contract and staged actual sources; duplicate effective placement rejects. |
| Omitted relationship, DependsOn(), one ordinary eligible input, or ForwardsBacking alone | Preserve the raw empty default; no single-input inference. Explicit empty retains independent obligations; refinement cannot add a source. |
| Two declared sources with ForwardsBacking on only one | Retain both conservative sources; refine only the selected own-storage alternative, not the other's or any carried guard/owner. |
| Direct import with `this`; interface receiver versus explicit pointer formal zero | Reject missing receiver; preserve distinct logical receiver/formal identities and inherited declaration mapping. |
| Aggregate/descriptor input, pointer with known backing, or independent scalar | Preserve actual carried backing without inventing caller/callee descriptor-slot loans, checked pointee facts or scalar storage. Reject an escaping borrow of the callee copy. |
| Source field path, computed string, duplicate/unknown source, empty refinement, unsupported required relationship | Diagnose before affected use; no guessed recipes or empty-set downgrade. |
| NativeOutput missing, ordinary ptr/ref destination, callback/fnptr use, secondary contract change | No new output-target applicability or privilege. Existing checked-init source contracts remain unchanged. |
| Raw output names itself or a sibling as a source, even when both would succeed | Reject under the chosen target-only scope; no implicit inner-pointee source projection or output-address escape. |
| Success followed by copying/storing the aggregate; descriptor ends versus backing owner/guard ends | Copies retain full dependencies. Ending unneeded descriptor bytes is possible; invalidating surviving backing is not. No retain/pin/ownership synthesis. |
| False result after no, partial or complete writes; saved result not yet tested | False publishes no holder. Unknown outcome retains conservative state/dependencies; stale discriminators cannot authorize a new generation. |
| Later argument invalidates source or fails before Invoke; nested argument call | Preserve once-only source order and outer reservations. Reject invalid backing; cancellation preserves already-executed effects without publishing output. |
| Native unwind/postcondition failure after Invoke; DoesNotReturn | Keep exceptional indeterminacy or terminal no-publication behavior, respectively; no false-result, vacancy, cleanup or no-failure inference. |
| Nonempty metadata, renamed/reordered declarations, replacement or private/standard round trip | Preserve exact destination, source kind/ordinal, phase and refinement. Reject changed/unsupported expectations, malformed empty/source combinations and illegal raw-output sources before execution. |
| Lying provider, unsupported root/anchor/layout, pointer bits alone, or attempted ownership acquisition | No validity from annotations/loading/byte coverage; preserve independent provider truth and target/backing gates. |
