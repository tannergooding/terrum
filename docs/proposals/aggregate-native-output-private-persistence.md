# Private persistence of aggregate native outputs

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** The authorization permits implementation using
this recommendation as provisional direction; it does not approve or stabilize
its private assignments, amend specifications, or establish implementation
support. This document supplies only the aggregate-only revision-90
private-persistence candidate for
[NativeOutput cells](aggregate-native-outputs.md). The later scalar/pointer
cell widening and revision-92/93/94 compositions are separate exact draft
candidates, not aliases or compatibility readers for revision 90.

## Decision and boundary

Recommend **candidate private semantic-module revision 90**, with one
declaration-owned normalized output-contract collection and new expression
tag **41 (`0x29`)** for `BoundNativeOutputReservation`. Use that exact inner
schema for standalone structured `.trm` modules and their embeddings in
`.trmn`, `.tnset` and `.trng`. Keep the three native container versions at
**2**; their framing does not change.

Preserve the prerequisite proposal's whole contract: one wholly vacant,
caller-owned, positive-extent `Blittable Copy` aggregate per annotated formal;
exact `write ptr<T>` authority; direct `&init` reservation at the argument's
source position; call-extent address use; complete publication only on the
declared normal-result selector; vacancy on its complement; cancellation
before invocation and indeterminate exceptional state after invocation.
Several independent annotated formals may use one captured result.

For revision 90, this adds no source alias, general pointer output,
pointer/COM ownership acquisition, retained address, partial output or
ordinary live-value replacement. It does not reopen NoThrow retirement. The
selected standard direction remains shared OutputState/Dependency, with the
predicate and reservation amendments in the cell proposal. Private records
below are a bounded projection of those meanings, not another standard clause,
Feature, Body codec or registry allocation.

**Composition with NoThrow retirement.** This document remains the standalone
candidate revision 90 on the revision-89 layout; it does not remove the
NoThrow fields or promises. The separate
[private NoThrow persistence proposal](no-throw-private-persistence.md)
recommends revision 91 for that retirement on 89, followed by revision 92 for
the exact aggregate-output delta from this proposal composed with retirement.
When both candidates are implemented together, use that proposed revision 92;
this paragraph does not amend or approve revision 90.

The accepted [persistence taxonomy](../specs/representation/persistence.md),
[exact-schema policy](../specs/representation/format-versioning.md),
[consumer obligations](../specs/representation/reader-trust.md) and
[binding relation](../specs/representation/binding-and-replacement.md) remain
unchanged. Their private/native boundary obligations apply independently of
this proposal's approval.

## Historical source baseline

These observations describe the source snapshot used when this proposal was
drafted; they are not current support claims or qualification results. Current
private-module support is tracked in the
[implementation inventory](../implementation/modules.md).

| Owner | Current representation and actual gap |
|---|---|
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `ReadModuleHeader`, `Write` | At this snapshot, magic `TRRM` was followed by a little-endian **i32** private revision, not the standard header's u16 pair. The reader explicitly listed revisions **76–89**. The writer finished revision selection with `ArithmeticOperationVersion=89`, including programs without arithmetic. Revision 90 was not assigned there. |
| [`FixedLayout`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.FixedLayout.cs), [`TokenSpans`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.TokenSpans.cs) | Additional revisions are 77 (span prototype), 78 (fixed layout) and 81 (their combination). Main-code revisions are 79 direct imports/outputs, 80 native parameters, 82 function pointers, 83 pointers, 84 vtables, 85 vptr offsets, 86 expected vtable bindings, 87 expected vtable outputs, 88 callable entry names and 89 resolved arithmetic. Explicit revision lists coexist with threshold tests; adding a constant alone is insufficient. |
| `PutFunction`/`GetFunction`, `PutImportedNativeVTableMethod`/`GetImportedNativeVTableMethod` | Old output vectors occur inside direct imports, defined vtable metadata, and imported expected vtable metadata. Each is an i32 count followed by **16-byte** tuples: `i32 ParameterIndex, i32 PointeeType, u32 ResultCondition, u32 FailureDisposition`. They carry no explicit formation, complementary state relation or dependency set. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`DirectNativeImportContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs), [`NativeVTableCallContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeVTableCallContract.cs) | `DirectNativeOutput` still has that prototype tuple. Prototype conditions are 1 signed-nonzero, 2 unsigned-nonzero and 3 signed-nonnegative; failure disposition is 1. The in-progress validators now admit aggregate reservations, but the imported expected-vtable reader still uses the scalar/pointer-only `IsOutputPointee(type)` overload. Neither those numbers nor these differing gates select a candidate persistence contract. |
| `PutExpression`/`GetExpression` | Expression tags **1–40** are assigned. The common envelope is `u8 kind, i32 type, i32 spanStart, i32 spanLength`. Tag 35 stores ordinary `BoundNativeStorageAddress`: local, carrier/element types, geometry, readonly Boolean and index expression. The void-typed `BoundNativeOutputReservation(local, pointee, span)` has **no writer/reader tag**. Tag 35 cannot stand in for it. |
| `Contract`, `CompatibleContract`, `SameNativeVTableMethod` | Vtable output expectations are duplicated in textual `NativeVTableOutput:` suffixes and, on selected imports, binary expected-vtable metadata. Direct import output facts live in their retained function metadata. Existing comparisons of prototype condition/failure integers are not a complete aggregate-output comparison. |
| [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Sets/graphs use the same structured codec, retain imported expectations separately from provider bodies, merge pointer/function-pointer tables, remap structures and restore symbolic calls before verification. Graph libraries have a separate `ReadGraphLibrary` entry path sharing header/function readers. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs) | `TNPK`, `TNSP`, `TNGP` each use outer u8 version **2**, length-delimited structured `Contract` bytes, an emitted image and SHA-256 integrity checking. Readers independently parse the inner module and check policy, declarations/imports and images. A checksum is not a semantic proof. |
| [`GraphJit`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs), `ExecuteGraphJitWorker` | Restores and re-lowers only the root through `RestoreConsumerAgainstReference`. Provider DLLs remain precompiled. Ordinary package/set/graph AOT execution uses the stored images; successful root restoration does not qualify their behavior. |

## Why a distinct schema

A minimal alternative is to keep revision 89, add tag 41, and enlarge or
reinterpret the old output tuples. Unpublished private drafts need not retain
compatibility, so a precisely selected new revision-89 draft is possible.
It nevertheless needs a separate exact-schema selector: existing readers can
encounter declaration-only output facts without encountering a new body tag.
Neither a native-profile string nor outer version 2 identifies that amendment.
Reusing the prototype tuples also leaves expected declarations dependent on
two representations and omits dependencies.

Select revision **90** instead, and move output facts to one declaration
carrier. The existing revision allowlist then rejects the new schema before
record interpretation. This is a deliberate fail-closed private choice, not
a requirement that every pre-release amendment increment a revision.

All new structured writes use 90, including empty output collections; feature
selection must not downgrade them to 89 or older. Preserve revision 89's
resolved arithmetic mode bytes and all unrelated contracts. Add 90 to the
explicit header, span, structure-role and producer-validation gates and
review fixed-layout/pointer/vtable threshold gates together.

Do not add a historical decoder or conversion. Existing exact older readers
may remain for their supported inputs, but **no revision 76–89 output tuple
authorizes this aggregate reservation contract**. Reject such attempted use,
including apparently plausible aggregate tuples, rather than upgrading it
from its field values. Unknown revisions reject; failed 90 decoding never
retries an older layout. Header substitution is not conversion. Exact private
schema agreement remains necessary even when both parties say “90.”

## Candidate revision-90 records

Everything in this section, including numbers and field positions, is an
**unapproved private assignment**. Multi-byte integers are little-endian.
Retain the existing bounded i32-length/strict-UTF-8 strings, source spans,
type tables, node/depth limits and strict 0/1 Boolean representation.
Private type identifiers are existing i32 implementation identities, **not**
TypeSpecRefs, DefIds or standard table rows.

For `pointeeType`, use the existing private `PrimitiveType` identity. The
eligible aggregate `T` is encoded by the declaration-local structure identity
already used by `ModuleCompiler` (`100 + BoundStruct index`); validate it
against the exact writable pointer formal and remap it with the owning
declaration's structure mapping. No independent structure ordinal or new
type-code namespace is introduced.

The exact schema is the candidate revision-89 layout with only the new
collection, removal of the three legacy output vectors/text suffix, and
expression tag 41 specified below. Other field positions relative to their
remaining neighbors, checked-output modes and operation meanings are
unchanged; there is no optional extension trailer.

### One output collection per declaration

Immediately after each `ModuleFunction`'s existing `HasOwnershipTransfer`
Boolean, before the expected-vtable presence Boolean, insert:

```text
OutputContracts:
    i32 outputCount
    if outputCount != 0:
        u8 nativeRole
        u8 flags = 0
        u16 reserved = 0
        OutputContract[outputCount]

OutputContract:
    u32 formalOrdinal
    i32 pointeeType
    u8 formation = 1
    u8 publicationPhase = 1
    u8 successSelector
    u8 successState = 1
    u8 complementSelector
    u8 complementState = 0
    u16 reserved = 0
    i32 dependencyCount
    Dependency[dependencyCount]

Dependency:                         // 12 bytes; shared logical field meanings
    u8 destinationKind = 2
    u8 sourceKind
    u8 relationship
    u8 phase = 1
    u32 destinationOrdinal
    u32 sourceOrdinal
```

An output's fixed prefix is **20 bytes**, including `dependencyCount`; each
dependency adds 12 bytes. Count zero omits the role/header and means no
aggregate-native-output contract, not unconditional success. Require
`0 <= outputCount <= explicitFormalCount`; output ordinals are unique and
strictly increasing. The existing maximum of 256 function slots and 64 call
arguments remains a private support limit, not a new language/ABI limit.

The containing declaration owns the entire collection. Its identity is the
selected logical module/declaration plus formal ordinal, not a vector index
or same-spelled declaration in another module. Preserve this ownership when
projecting exported references and imported expectations; no collection is
shared across independent expected declarations.

`nativeRole=1` requires the exact direct foreign-import declaration;
`2` requires the independently admitted foreign native-method declaration.
Zero/other values reject. Preserve the actual library/symbol/convention or
declaring method/interface/receiver/vtable contract and provider identity in
their owning metadata. The role byte is a required role to validate, not
serialized authorization or evidence that an arbitrary pointer/slot is that
interface. An imported expectation retains its own required role and output
collection; resolving it must not manufacture either from the replacement.
An unsupported independent role/identity representation still rejects its
affected use.

For revision 90, **remove** the old output count/tuple vectors from all three
locations: direct-import metadata after library/export names; function vtable
metadata after its parameter vector; and imported expected-vtable metadata
after its parameter vector. Preserve the remaining fields in their existing
order. Also remove the old `NativeVTableOutput:` suffix from revision-90
contract strings. The new collection is the sole serialized output authority;
readers do not choose between a string, legacy tuple and new collection.
Existing textual signature/promise comparison remains necessary, but is no
longer sufficient without comparing the collection.

This common carrier is needed even on an imported declaration without a
retained numeric vtable binding. It preserves output expectations without
turning ordinary private-module replacement into exact slot matching.
Where the native path already retains an exact slot/offset binding, retain
and compare it independently. Complete definition/declaration projections
must agree with their owner collection; conflicting in-memory copies reject.
The current `DirectNativeOutput` tuple cannot be the normalized model for
this carrier: it lacks required facts.

### Formation, selectors and dependencies

`formation=1` has exactly the prerequisite proposal's vacant-whole-object,
no-incoming-value-read, no-address-retention, call-extent semantics.
`publicationPhase=1` means normal publication after the invocation's required
postconditions and phase cleanup. Extent/alignment come from the exact
complete pointee layout and selected target, not a new integer size claim.
The formal's type must independently resolve to **writable** `ptr<T>`, with
exactly the recorded `T`, ordinary parameter mode and the required native
role/caller-unsafety. Require positive extent and complete Blittable/Copy
eligibility; pointer spelling or a struct flag alone is insufficient.

The private selector byte uses this closed assignment:

| Selector | Meaning and exact result type |
|---|---|
| `3` | Nonzero, int32 or uint32 |
| `4` | Zero, int32 or uint32 |
| `5` | Nonnegative, int32 |
| `6` | Negative, int32 |

Only `(successSelector, complementSelector)=(3,4)` or `(5,6)` is valid, with
states `(1,0)` respectively. There is no omitted complement, unconditional
raw output, live failure value, partial state, Boolean foreign result or
default predicate. Signed negative results satisfy selector 3. These are
explicit private projections of the proposed shared result tests; equal
numbers do not make private bytes a standard PredicateGraph. Map source
conditions and shared selectors explicitly, never by prototype enum casts.

Ordinals in **new** output/dependency records are zero-based logical explicit
formals, excluding an implicit receiver. Direct-import ordinals coincide
with current parameter positions. The private vtable carrier has its receiver
at parameter position 0, so its existing `ParameterIndex`/call mapping uses
position `formalOrdinal + 1`. Perform that conversion explicitly; do not
reinterpret the older NativeParameter fields or count the receiver as an
output. Receiver dependency ordinals remain zero.

Require a complete explicit dependency source set per output:

* `dependencyCount=1` with `sourceKind=relationship=sourceOrdinal=0`
  expresses the empty set. Missing/zero count is invalid.
* Otherwise use `sourceKind=1` for a logical formal, `2` for the receiver;
  `relationship=1` borrows source storage, `2` forwards existing backing,
  `3` explicitly declares their union. Each source occurs once, ordered by
  `(sourceKind, sourceOrdinal)`; a receiver source requires a receiver.
* Every row has `destinationKind=2`, `destinationOrdinal=formalOrdinal`
  of its containing output, and `phase=1`. Bound a nonempty set by the
  number of explicit formals plus the receiver, retaining all rows.
  Empty and nonempty rows cannot coexist.

These are the shared Dependency relationships applied directly to the output
formal, not references to an output-row number. NativeOutput alone normalizes
to the explicit empty set; it must not overwrite independently required
dependencies. Successful publication creates the declared relations;
normal failure creates no output holder. Empty sources do not erase type,
provider/code lifetime, external backing or target obligations. Raw pointer
pointees are not thereby checked dependency sources. Unknown/unsupported
relations reject before use; an initial implementation supporting only the
empty set must explicitly reject nonempty required sets, not discard them.

### Reservation expression and call context

Candidate tag **41** uses the unchanged expression envelope and this payload:

```text
u8 kind = 41
i32 type = 0                       // existing private Void identity
i32 spanStart
i32 spanLength
i32 targetLocal
i32 pointeeType
```

Its exact extent is **21 bytes**; it has no child expression, pointer result,
geometry, runtime generation, call-ID field or unsafe-authorization bit.
Reconstruct `BoundNativeOutputReservation`, never tag-35 storage or a
`BoundNativePointerAddress`. The local must be a declared whole
initialization-capable local, not a formal/receiver/field, with exactly this
pointee type. The existing vacant-local statement and reconstruction of
`IsInitializationLocal` remain authoritative declaration evidence.

Tag 41 is valid only as the output argument of its containing direct
`BoundCall` occurrence, allowing the existing transparent unsafe-expression
wrapper without creating an ordinary value. It is invalid in a return,
initializer, cast, field store, capture, indirect-call operand or unannotated
argument. Reconstruct unsafe permission from actual enclosing unsafe scopes
and preserve caller-unsafety; a void node or serialized flag grants none.

Keep tag **5** unchanged:

```text
ExpressionEnvelope
String symbolicTarget
i32 argumentCount
Boolean hasParameterMapping
if hasParameterMapping: i32 parameterPosition[argumentCount]
Expression sourceOrderedArguments[argumentCount]
```

The containing call's **tree occurrence**, not its target name, text span or
structural equality, supplies static call identity. Its argument index and
validated positional/default map supply the exact formal position; convert
the receiver position as above. Require a total, unambiguous mapping and
exactly one matching reservation for every annotated formal, with no
overlapping destinations. A new redundant serialized call ID or formal field
is unnecessary because reservations cannot leave this context. Validate that
context before lowering, including for declaration-only imports once bound.

Never reorder `sourceOrderedArguments` into formal/ABI order in persistence
or rewriting. A dynamic invocation, including each loop/recursive execution,
gets fresh reservation instances tied to the local's current vacancy
generation. Serialized node offsets or host object identities are not
runtime generations. Reconstruct correlation from the retained structured
operations and declaration contract; do not serialize transient verifier
state as trusted authority.

## Producer, restoration and execution obligations

1. **Establish and retain facts.** Source/public-bound-program producers
   establish the complete declaration, local eligibility, source ordering,
   vacancy, disjointness, authority and supported dependencies. Validate a
   normalized declaration model before writing; the existence of the old
   `DirectNativeOutput` tuple is insufficient. Foreign provider truth remains
   an authored unsafe assertion, not a body proof or checksum property.
2. **Reserve at evaluation, invoke once.** Evaluation of tag 41 reserves that
   local generation before later arguments. Pending outer reservations
   survive nested argument calls; nested call completion must not consume
   them. Preserve earlier argument snapshots while placing native arguments.
   Only the owning invocation projects its reservation to the actual local
   address, with checked frame extent/alignment and retained backing.
3. **Distinguish cancellation and failure.** If target/argument evaluation
   prevents invocation, cancel only the unentered reservations being
   abandoned and restore their vacant state without touching bytes or
   undoing earlier argument effects. Once Invoke begins, an escaping admitted
   failure leaves the destination indeterminate; neither argument-failure
   cancellation nor the complement selector may claim vacancy. Failure in
   postconditions/cleanup before normal Publish produces no normal output.
4. **Publish from one captured status.** On normal completion evaluate the
   normalized selector pair against that invocation's saved int32/uint32
   result, even if source discards it. Success publishes one valid complete
   T and its dependencies; the complement publishes vacancy irrespective of
   writes. No constructor, zero fill, readback validation, rollback, old-value
   preservation or complete-T cleanup is synthesized. DoesNotReturn keeps
   its complete signature but publishes neither a default result nor output.
5. **Preserve correlation through flow.** Saved discriminators retain their
   exact call/local/generation association. Retirement, replacement, relevant
   alias writes or discriminator reassignment invalidate affected relations.
   Joins/loops preserve actual state or remain conservative; old status
   values cannot authorize a later generation. No ordinary pointer escape,
   reentrant conflicting access or address retention is acquired.
6. **Check before affected use.** Consumers validate exact schema, lengths,
   counts, references, field domains, owner/role, type/layout/write authority,
   selector/result compatibility, complements, dependency sets and
   reservation/call/argument agreement. Resolve all selected identities and
   binding/support requirements before emission or image loading. Existing
   `Resolve`, `ValidateProducer`, `RestoreConsumerAgainstReference` and
   graph validation/verifier paths must preserve the facts; no path may
   bypass checking because it already has an image. This requires safe
   consumption and binding checks, not semantic re-proof of arbitrary
   producer bodies or the foreign implementation. An optional deeper flow
   audit remains distinct.

## Binding, remapping and native containers

Compare each retained expectation against the **selected** provider, not a
same-named alternative. In addition to current signature/layout/ownership/
unsafe/target/runtime checks, require equality of the bounded normalized
output role, complete T and writable formal, formation, extent/alignment,
publication phase, exact selector partition/states and dependency source
permissions. Exact equality is the selected private checking rule here; this
proposal adds no predicate implication engine. Rows, private type codes,
parameter names and physical ordering are not cross-module identities.
Changing NonZero to NonNegative, adding an input read/retention/dependency,
or changing vacant failure to live is not compatible servicing.

Retain expected collections even after resolution. Provider-only guarantees
must not silently replace weaker recorded assumptions in restored bodies.
`RecordedImportPromises`, reference/declaration projection, ordinary `Link`,
set restoration and graph-import checking need the output contract alongside
their existing promise handling. Preserve existing selected provider/export,
type-origin and cleanup/code-lifetime mappings and exact native bindings;
output equality alone cannot satisfy them.

Type pruning, structure/class remapping, native pointer/function-pointer
table merging and generic/bound-tree rewriting must visit **pointeeType
inside the void reservation**, not only `expression.Type`. Remap the
declaration collection, complete formal types and all dependency
formal/receiver correspondences together. The current structural
`RemapExpression` has a reservation case, but `RemapNativeTypeIdentities`
still handles its ordinary expression type only. Audit both families and
`Calls`/required-type discovery; one remapper is not end-to-end preservation.

Resolve symbolic call targets after the complete destination function table
is known. Preserve distinct local slots, argument order and mapped formal
positions; rebuild occurrence-local reservation associations when cloning
bodies. Module-set and diamond-graph merges must retain provider/type origin
and each using declaration's expectation, not intern by equal aggregate
layout or local numeric type code. Re-encoding rebuilds all changed lengths,
spans/maps where affected, outer offsets and checksums, not just a header.

`TNPK`/`TNSP`/`TNGP` version **2** continues to frame the revision-90 Contract
and emitted image. Its writer must derive both from the same checked facts;
its reader must admit the inner schema independently, then perform existing
declaration/import, provider, runtime-policy, image and lifetime checks.
Recompute contract lengths and SHA-256 after re-encoding. Add no native-profile
label: this changes retained semantic facts, not pointer/status ABI carriers.
An unsupported inner revision/record/required relation rejects **before
affected image execution**, even when the image and checksum are valid.
No image-only, source-rebinding, empty-program or alternate-decoder fallback
is authorized.

Qualify both consumption modes: semantic module linking/JIT and graph
root-only JIT restore the reservation and re-lower; native package/set/graph
AOT runs the original stored image after contract validation. Compatible
provider replacement leaves consumer/root/intermediate artifacts unchanged.
Graph JIT must not silently regenerate provider DLLs to make a mismatch pass.

## Explicitly uncovered encodings

* **Development `.trrm`** remains its explicitly selected
  `TrRmArtifactSchema.Development`, separate from private structured modules.
  [`TrRmExecutableCompiler`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs),
  its [output-body codec](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Outputs.cs)
  and [`TrRmContractTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs)
  use the old native-output clause `0x12`, conditions 2/4 and storage-address
  operations. This proposal does **not** assign them aggregate-reservation
  meanings or a new development Feature/revision. Selected development
  producers/consumers must reject the new declaration/reservation, including
  declaration-only requirements, until a separately selected complete codec
  is implemented. Do not lower tag 41 to their old address form.
* **Standard `.trrm`** must implement the prerequisite proposal's complete
  shared OutputState/Dependency, selector and call-reservation direction under
  exact draft agreement. Private revision 90, tag 41 or a native wrapper
  cannot activate it. Until then reject unsupported required output data;
  private success does not close the whole-plan standard acceptance gate.
* **Loaded indexed executable bodies, native fixed-array expressions,
  compilation-local span/source-grant prototypes and other currently
  unsupported structured persistence** retain their own existing gates.
  This proposal does not make them serializable or discard their facts.
* No legacy-output migration, arbitrary function-pointer output contract,
  general predicate program or independent interface/ownership codec is
  selected. Those are either exclusions of the prerequisite direction or
  independent representation prerequisites, not alternative escape routes.

## Implementation owners after provisional direction

No file in this table is amended by this documentation change.

| Owner | Required integration |
|---|---|
| `Semantics\BoundProgram.cs`, `DirectNativeImportContract.cs`, `NativeVTableCallContract.cs`, binder/verifier partials, `BoundTreeTraversal.cs` | Carry and validate normalized output/selector/dependency facts; preserve call-only reservations and generation/cancellation/publication behavior. Do not promote the prototype tuple into the contract by changing its allowed pointee types alone. |
| `Modules\ModuleCompiler.cs`, `.FixedLayout.cs`, `.TokenSpans.cs` | Select/admit exact 90, add the common collection and tag 41, remove duplicate old carriers in 90, validate context, project declarations, compare expectations and remap every nested type/identity. |
| `Modules\ModuleCompiler.ModuleSets.cs`, `.Graph.cs` | Preserve collection ownership through reference merging, graph-library reads, imported expectations, type origins, replacement and restoration. |
| `Modules\NativeModuleCompiler.cs`, `.ModuleSets.cs`, `.Graph.cs`, `.GraphPackages.cs`, `.GraphJit.cs`; `Native\NativeEmitter.cs`, `.NativeStorage.cs` | Keep independent inner admission and complete pre-load binding, pair truthful metadata/images, preserve exact caller-storage projection and root-only versus precompiled execution. |
| Development/standard TRRM compiler, contract, Body readers/writers | Add explicit unsupported gates for this candidate where not implemented. Subsequent standard integration follows its own complete proposed schema, never these private assignments. |
| [Private module inventory](../implementation/modules.md), [native](../implementation/native.md), [sets](../implementation/module-sets.md), [graphs](../implementation/native-graph.md), [conformance handoff](../implementation/conformance-gaps.md) | Record actual implemented schema/paths and final-patch evidence at the corresponding integration checkpoint; keep CG-002/004/025 and the whole-plan standard gate distinct. Link the prerequisite proposal to this private decision without changing its source or standard direction. |

## Acceptance gates and malformed cases

These are required future cases, **not passing test evidence**. Use public
compiler APIs, independently authored byte fixtures and ordinary headless,
supervised native execution. Source round trips alone cannot detect two
matching incorrect codecs.

| Gate / existing test home | Required evidence |
|---|---|
| Exact codec — [`ModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs) | Independent 90 fixtures for empty/nonempty declaration collections, both result types/selector pairs, direct and independently admitted native-method roles, explicit empty and nonempty dependencies, and exact 21-byte tag 41. Include output-bearing imported declarations with no body and unrelated checked/wrapping arithmetic to detect field shifting or lost revision-89 facts. |
| Malformed records and admission | Reject negative/oversized/truncated counts; bad UTF-8/bounds; unknown revision, role, formation, phase or reserved value; invalid type/write authority/layout; duplicate/out-of-range formals; missing/reversed/noncomplementary selectors, wrong states/result types; absent/mixed/duplicate/invalid dependency sources or unsupported required relations. Reject old-layout bytes under 90 and new data under 89 without retry or guessed conversion. |
| Malformed reservations/calls | Reject non-void tag 41, missing/mistyped/formal/undeclared local, tag 35 substituted for tag 41, standalone/stored/returned/indirect reservations, missing or duplicate mapped formals, receiver-as-output, mismatched pointee, overlapping destinations, absent output contract and unsafe-scope stripping. Include reordered named arguments and identical spans/callee names at distinct call occurrences. |
| Flow and lowering — semantic/native aggregate-output tests | Preserve source order and once-only evaluation; later-argument failure cancels before invoke; nested argument calls retain outer reservations; invoked failure/postcondition failure is indeterminate. Exercise success, false after no/partial/full writes, discarded status, saved copies, invalidation, joins, loop generations, reentry conflicts, live/partial/maybe-live rejection and actual frame alignment. No constructor/default initialization or ordinary pointer escape. |
| Dependencies/replacement — module/set/graph tests | Reorder types, pointer/function-pointer tables and functions; include nested aggregates and diamond origins. Preserve formal/receiver source permissions and independent providers. Accept equal normalized contracts; reject changed selector, failure state, output type/write access/layout/role, dependency, phase or retained native binding before code load. Compare declaration-only imports too. |
| Embedded native execution — [`NativeModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeModuleTests.cs), [`NativeModuleSetsTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeModuleSetsTests.cs), [`NativeGraphTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Graphs/NativeGraphTests.cs), [`NativeGraphJitTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Graphs/NativeGraphJitTests.cs) | Exercise 90 inside version-2 producer/consumer, set, graph root/intermediate and TNPK leaf packages. Test actual stored-image execution separately from semantic/root-JIT restoration, both supported runtime policies where admitted, and compatible replacement with unchanged consumer bytes. Recompute outer checksums for malformed-inner tests so rejection reaches semantic admission; verify no invocation/image-load side effect on rejection. |
| Unsupported paths and standard milestone | Development/standard TRRM and indexed reserialization reject new unsupported requirements without clearing them or choosing private fallback. Preserve unrelated existing pointer/address/input behavior and all independent native support gates. Private tests/emission do not replace standard independent vectors or actual runtime evidence. |

Acceptance requires final relevant patch/configuration evidence for the
implemented paths, not this document, a prototype-only test, a structural
parse or successful image emission. No additional private wire choice is
deliberately deferred within this recommendation: **revision 90, the common
collection, closed selector/dependency shapes and tag 41 are one candidate
bundle**. Adopting that bundle remains unapproved; implementation,
qualification and independently missing standard/interface capabilities
remain separate gates.
