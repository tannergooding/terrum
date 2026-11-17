# Standard TRRM phase-2 postcondition construction

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This selects a bounded construction-binding
handoff and enforcement route, not normative adoption, implemented support
or execution evidence.

## Decision

For exactly **`trrm/1-draft-init-reference-bodies-general-predicate-graphs-1`**
(B + I + G), retain the existing per-originating-Module
[`CoreContractFailureIdentitySelection`](standard-trrm-initialization-predicate-failure-identity.md)
and add **one required, nonserialized, producer-returned postcondition
construction-binding map**. Each retained phase-2 clause maps to an explicit
assigned Body **`0B=ConstructorBinding`** record in a captured, selected
Standard artifact. Resolve its exact class and constructor through ordinary
TypeDef/MethodDef Import/Export binding; never redo constructor lookup.

Lower the checked plan using ordinary construction/calls and a fresh class
throw at the failed clause. Initially admit only a **jointly relowered,
same-runtime terminal route**, with Standard `runtimePolicy=0` and no admitted
handlers. Construct the exact completed PostconditionException object before
raising it. Do not use the terminal-policy allowance to skip this selected
construction path.

This is the smallest accompanying-context route because the identity
handoff already requires source-free inputs beyond artifact bytes, and the
assigned constructor record already pins the target, class and bound
defaults. It adds no wire layout, numeric assignment, failure factory or
managed-machine ABI. It deliberately requires the selected plan-bearing
artifact; bytes plus a role table alone remain insufficient.

The [semantic selection owner](core-contract-failure-identity-selection.md)
already provisionally replaces false declaration ensure's common creation
path with **ordinary zero-supplied-argument construction**. Preserve that
choice: do not replace it with `Create`, an allocator callback, injected
diagnostic arguments or a constructor key in the Core role record.

## Genuine gap and alternatives

The [composition owner](standard-trrm-initialization-predicate-composition.md)
requires source-free phase-2 retention and exact normally-false failure.
Contract `000B` retains only phase, ordinal and graph attachment, **12-byte
payload / 20-byte total row**. Neither it nor the Core role table identifies
the producer's selected ordinary constructor, defaults or execution plan.
Existing class/member references can bind identities but cannot supply that
missing association.

| Route | Assessment |
|---|---|
| TypeDef/Import/Export and class members alone | Necessary, not sufficient. TypeDef flags distinguish class kind; MethodDef flags do not identify constructors. Matching names, an applicable zero-argument overload or a class-returning signature cannot recover the previously selected construction binding. Replacement must not trigger new overload resolution. |
| **Accompanying map to assigned `ConstructorBinding` records** | Recommended bounded route. Reuses exact existing typed operands and the originating selection handoff. Carries the association explicitly, without scanning a Body for a plausible plan. Requires the mapped Body and its retained binding/runtime closure. |
| Required wire-owned phase-2 binding clause and metadata-level construction plan | Viable self-describing redesign when exchange must dispense with the accompanying map or plan-bearing Bodies. It needs explicit clause/plan/reference assignments, defaults, ownership, canonicality, remapping and an exact schema agreement. A Body-local record index cannot serve as a plan on a bodyless declaration. No assignments or successor identity for this unselected route are proposed here; unpublished numbers need not be preserved. |
| Runtime-owned nonreturning “raise postcondition” callable | Would need an explicitly unapproved API, exact selected class/constructor binding and origin/holder call carrier. `DoesNotReturn` promises neither this failure category nor construction, and a no-argument helper cannot silently acquire clause origin. Not selected. |

The map is a **resolved binding**, not another role selection or permission
for the receiving host to choose a constructor. A source-free consumer may
read the directly referenced typed record; it need not infer enforcement
from private Body behavior or semantically re-prove that behavior.

Current evidence establishes separate implementation gaps:

- [`BoundDeclarationContractClause`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  retains phase/ordinal, predicate, selected failure pair and using Module,
  but no complete constructor plan.
- [`TrRmExecutableCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
  rejects declaration clauses and executable `program.Classes`.
- [`InitializationReferenceBodies`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.InitializationReferenceBodies.cs)
  reconstructs only a bounded Boolean-input phase-3 tree subset. This is
  neither phase-2 execution nor exception construction.
- [`PeCompiler`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
  rejects declaration clauses before lowering.
- [`Private managed-class consumers`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeManagedClassModuleTests.SemanticConsumers.cs)
  use private semantic/native routes. Their support is not a Standard
  class/witness/constructor codec or exact exception-object qualification.

## Contract owners and intended amendments

| Owner | Candidate delta or preserved responsibility |
|---|---|
| [B + I + G composition](standard-trrm-initialization-predicate-composition.md) and [failure identity handoff](standard-trrm-initialization-predicate-failure-identity.md) | Add the explicit accompanying plan association below for retained phase 2. Keep exact selection, role authority, originating Modules, phase-3 separation and independent admission gates. |
| [Core selection](core-contract-failure-identity-selection.md#source-binding-and-ordinary-construction) and [requirements §2](../specs/language/requirement-failures.md#2-ordinary-zero-supplied-argument-construction) | Preserve ordinary binding, original access environment, zero supplied versus zero formal arguments, defaults, actual construction failure and no fallback. |
| [Runtime postconditions](../specs/runtime/standard-failures.md#2-runtime-postconditions) and [call phases](../specs/language-contracts.md#3-contracts-bracket-semantic-body-completion) | Preserve ordering, saved results, exact class, actual failure channels and no successful publication. The bounded route selects actual construction even when termination makes the event unobservable. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [binding/replacement](../specs/representation/binding-and-replacement.md) and [logical failures](../specs/representation/logical-records.md#standard-runtime-categories-encoding-pending) | Retain exact class/member/witness expectations, origin correspondence and every selected provider. This proposal completes no independent class, initializer or witness codec. |
| [Typed records](../specs/il-instruction-set.md#typed-operand-records), [calls](../specs/il-instruction-set.md#calls), [throw](../specs/il-instruction-set.md#one-throw-family-explicit-payload-kinds) and [registry](../specs/representation/trrm-1.registry.json) | Reuse `0B`'s selected construction-plan semantics for a mapped phase-2 check. Count a map reference as a required logical use of that record for ownership/canonicality, including an eliminated failed path. Add no record field, opcode or clause operand. |
| [Local object/event semantics](../specs/language/local-failures.md#2-object-identity-storage-and-lifetime) and [Windows deployment](../specs/targets/windows-x64-deployment.md) | The completed class object is the event. Admit only the bounded terminal path below; establish real backing, roots, origin and provider/code lifetime. No public managed ABI or cross-image adaptation strategy is selected. |
| [Reader trust](../specs/representation/reader-trust.md), [admission](../specs/representation/requirements-and-admission.md) and [format agreement](../specs/representation/format-versioning.md) | Consumers check structure, support and binding before use; producers publish truthful binding/origin facts. The unchanged wire identity does not make the accompanying context optional or enable an unconfigured reader. |

This completes an accompanying-context prerequisite of the existing
unapproved composition; it does not silently reinterpret serialized bytes.
Exchange must agree on this whole candidate handoff. Existing API defaults
and exact selections remain unchanged and decline required missing context.
Do not put the map in `000B`, graph operands, Blob, optional debug data,
an E1 anchor, or a private module.

## Exact accompanying map

Propose the public host DTO name **`TrRmPostconditionConstructionBinding`**;
its shape and all API names below are **unimplemented, unapproved candidates**.
The map is an immutable array of these entries:

| Field group | Required shape |
|---|---|
| Clause | Captured `TrRmStandardArtifactInput`, `u32 MethodDefRow`, `u32 PhaseLocalOrdinal` selecting one retained phase-2 `000B` attachment. |
| Origin | Captured `TrRmStandardArtifactInput`, `u32 MethodDefRow`, `u32 PhaseLocalOrdinal` selecting that clause's original declaration occurrence. Its checked original Module selects the Core role record. |
| Plan | Captured `TrRmStandardArtifactInput`, `u32 MethodDefRow`, `u16 ConstructorBindingRecordIndex`. That MethodDef has a nonzero Body; the index selects its assigned record `0B`. |

Artifact fields identify exact captured inputs, not paths, package names,
mutable file handles or new authority tokens. Capture bytes and collections
once. All artifacts must be explicitly supplied root/provider inputs under
the **same exact B + I + G selection**, with unambiguous Module revisions and
compatible edition/target/runtime requirements. An input occurrence is not
identified by content equality alone.

MethodDef rows are one-based `1..00FF_FFFF` with reserved-zero high byte;
phase ordinals are full-width `u32` integers, not references. Plan indexes
are zero-based `u16`, including zero and FFFF when present; there is no
absent-index sentinel. Derive the Body from the explicitly selected MethodDef,
never search another method or confuse a graph row with a Body record index.

Require exactly one entry per retained phase-2 occurrence, including bodyless
expectations, inherited clauses, uncalled declarations and proved/eliminated
failed paths. Duplicate Clause keys reject even when identical. Reject entries
for phase 1, phase 3 or static proof requests. Several entries may name one
immutable plan only when all resolved binding facts agree; they remain
distinct assertion executions and origins.

An ordinary declaration uses itself as Origin. An imported expectation
follows its exact ordinary binding to the originating declaration.
Inherited, cloned or relocated clauses require supported explicit
declaration/formal/phase-ordinal correspondence; physical MethodDef ownership
does not replace that evidence. Validate the associated typed graphs and
original expectations under the supported substitution/normalized comparison.
If origin correspondence cannot be established, decline that route.

A bodyless expectation can name a plan in an explicitly retained bodyful
provider or enforcement implementation. That reference supplies the plan,
not a new exception factory or source lookup. If no selected plan-bearing
artifact exists, this route declines. Do not manufacture a dummy Body merely
to store a record, require debug source, or claim byte-only reference exchange.

### Bind the selected plan

1. Validate the mapped `0B` record's exact extent/header, counts and ordered
   bindings using its assigned schema. Decode TypeMap, MethodMap and ConstantMap
   in the **Plan MethodDef's** Module context, separately from semantic Origin.
2. Resolve its exception TypeMap entry to the complete concrete managed class.
   Require equality with Origin's authorized `PostconditionExceptionClass`
   after ordinary binding, retaining all alias keys and original expectations.
   Require the real public primary nongeneric class and its complete exact
   Core self-typed exception witness, not merely class flags or an interface
   with the same name. The independently checked Core IException anchor
   remains required under the semantic selection owner; neither this map
   nor the postcondition role selects that interface. Supplying its evidence
   selects neither E1 nor an Unreachable dependency.
3. Resolve the exact constructor MethodMap target and original callable
   expectation. Record `0B` already requires a construction target returning
   the selected exception type, with zero supplied arguments. A raw
   unit-returning initializer is not that constructed-result target.
   Require the actual admitted constructor projection and its correspondence
   to the selected ordinary constructor. A class-returning helper cannot be
   relabeled as it. Missing projection/publication support is a gate.
4. Preserve the selected member/owner, substitutions, full modes, optional
   defaults/empty-params bindings, access, effects, safety, initialization,
   final validation and cleanup. Callable Imports need their actual public
   provider-local exports and keys; the class selector invents none.
   Check every binding, even on an unexecuted failed path.

For the first executable slice, support the already selected **zero-formal**
plan (`formalCount=bindingCount=0`), with no receiver or own generic formals
on its constructed-result target. This is an admission limit, not a new
constructor-resolution rule: perform ordinary zero-supplied binding first.
If it selects an optional/default/params plan, refuse unsupported execution
instead of searching for a different zero-formal constructor. Nonzero-formal
plans still need their exact assigned bindings and independently admitted
value/expansion codecs; scalar frozen values do not encode arbitrary Text or
empty arrays.

The common exception contract remains complete, including durable Message,
type-owned DefaultMessage, causes and formatting witness. String metadata
does not substitute for a Text object. Neither `0B` nor the map supplies
allocation, rooting, field-initializer, constructor-projection, self-witness
or partial-construction support that the actual inputs lack.

## API preservation and transformations

Add candidate entry points **`LoadStandardWithPostconditionConstructionBindings`**
and **`CreateStandardReferenceWithPostconditionConstructionBindings`**. Each
takes the existing root input, logical Module identity, provider inputs and
`ImmutableArray<CoreContractFailureIdentitySelection>`, plus an explicit
`ImmutableArray<TrRmPostconditionConstructionBinding>`. No ambient constructor
or provider is selected. Empty/default arrays supply no entries.

Add candidate **`EmitStandardWithPostconditionConstructionBindings`**, taking
the checked program and existing Module/schema/target inputs and returning
bytes plus both immutable accompanying arrays. The writer computes the map
from checked original bindings and emitted typed references; it does not
accept a replacement constructor selection. The returned map refers to the
captured emitted artifact and explicitly retained provider snapshots.

Load/reference results retain the complete decoded plan, origin and original
expectations as bound facts, not only numeric indexes. Traversal, substitution,
rewriting, verification and JIT/AOT consume those facts. Re-emission rebuilds
the map against the output artifacts; it never reruns overload resolution.
Snapshot-only byte passthrough is insufficient transformation evidence.

Remap Module/MethodDef/Import/Export/TypeSpec/witness relationships, Contract
ranges and phase occurrences, Body ownership, Type/Method/Constant maps,
`0B` record indexes and bindings, code/record offsets and enclosing extents.
Keep a mapped record as a required logical use even if its failed path is
eliminated. Canonical first-use order includes these clause associations in
logical phase order; ordinary valid record permutations remain consumable
with the map updated. Retain or explicitly remap each selected snapshot;
deleting a plan-bearing Body requires preserving the complete plan elsewhere
through a separately admitted carrier or refusing the transformation.

Replacement rechecks every original class, witness, callable, default,
origin and stronger relied-on fact. An added overload cannot change a bound
plan. Retargeting an alias, changing the selected member or losing a provider
does not trigger another lookup. Live objects retain actual provider/runtime/
code dependencies; recompiling future code does not migrate an existing event.

No new CLI flag or sidecar grammar is selected here.
`--trrm-postcondition-anchor` supplies only the existing role table;
the source manifest still supplies source selection, not artifact plans.
Artifact commands without a supported map transport continue to decline
required missing context before output replacement or worker launch.
Source CLI publication likewise needs a supported delivery/preservation
handoff for the returned map and retained inputs; a selection manifest
does not authorize byte-only publication of these obligations.

## Bounded execution and failure ordering

This is an explicit candidate **terminal fresh-class enforcement** contract,
not E1 in miniature. Reuse Standard policy zero, fail-fast for *unhandled*
failures. Since this route admits no handlers, its reached fresh throw is
unhandled. Decline retained handler/active-event/captured-owner or required
exceptional-unwind programs before use. Do not encode `CheckedExceptions`,
Feature 21, an ExceptionDirectory, RegionExit, native unwind or a new policy.
The [E1 proposal](standard-trrm-register-body-exception-regions.md) and
[Windows local-object transport proposal](windows-x64-local-exception-object-transport.md)
are not composed.

1. Save the normal result once. Finish actual body/finally/local and
   untransferred owned-parameter cleanup before phase 2. Escaping failure,
   divergence or no-normal-return runs no ensure.
2. Evaluate each reached phase-2 predicate once, in declaration order, with
   its legally surviving state and selected evaluation form. True advances
   without construction. Predicate/helper/cleanup failure keeps its own
   admitted failure channel; it is not Boolean false.
3. On normal false, execute that occurrence's already checked ordinary
   construction plan once. Predicate pure/constant authorization grants no
   constructor permission. Respect all declaration initializers, constructor
   clauses, actual partial state and the selected construction's own origins.
   A verified DoesNotReturn construction call remains terminal with no result
   selector; do not append `failfast`/`unreachable` or read an unconstructed
   result as a fallback.
4. Only successful complete construction supplies the exact live class object
   to the fresh-throw operation (`09`, payload kind 1; `B4`/`E8` if serialized).
   The object itself is the event, not a scalar reason, null handle, Copy
   payload or second event wrapper. The final raise site's instruction address
   and retained semantic clause origin survive outlining/inlining; neither a
   shared-helper address nor optional SourceMap defines the clause identity.
5. Throw has no normal continuation: skip later ensures and normal Publish.
   Do not replay finished cleanup, reevaluate the return, undo transfers or
   vacate already initialized ordinary outputs. Reserved/private outputs
   still require their own admitted protocol. Complete normal phase cleanup
   precedes Publish only when all clauses succeed.

Known missing construction, event, cleanup or lifetime support refuses the
affected operation before execution. Failure before normal construction
completion produces no completed outer PostconditionException. Later rooting,
event-preparation or transport failure follows its own admitted contract,
preserving responsibility for the already constructed object and resources;
it fabricates no postcondition event or normal Publish. Termination promises
neither recovery nor unwind cleanup, but cannot stand in for an unsupported
normally-false enforcement path. No scalar failure stub or unconditional
`failfast` substitutes for the selected construction and fresh throw.

Joint semantic relowering permits compilation-private managed representation
and calls only when one actual runtime agreement covers the entire retained
closure. Establish complete class backing, initialization, Message storage,
roots through construction/calls/raise and actual code/allocator lifetime.
Existing private managed-class support is not this evidence. Independently
generated managed binaries need their own explicit validated runtime/code
agreement; this proposal selects neither a stable ABI nor automatic adaptation,
recompilation fallback or outward native exception propagation.

Retained phase-2 use continues to require G's `17/1 -> [(10,1)]`; actual fixed
scalar defaults/literals additionally require `11/1` where applicable.
Compute complete least closure in each using Module, including expectations
and unreached plans. No Feature 7, 8 or 21 follows from postcondition selection,
and no Feature number supplies missing managed/runtime representations.

## Evidence and unresolved prerequisites

These are future gates, not results from this documentation change:

| Gate | Required producer, consumer and runtime evidence |
|---|---|
| Real provider/ordinary construction | Authorized Core class at non-name-derived keys and a non-first provider; exact complete self witness/common members and genuine constructor projection. Source binding selects once; independent metadata binding checks the same class/member/expectations without source. |
| Map/source-free retention | Emit, remove source/debug data, load, create a bodyless expectation with an explicit plan-bearing provider, permute/remap rows and records, re-emit and reconsume with both arrays. Preserve several originating Modules, inherited/relocated occurrences and alias equality, or explicitly decline unsupported correspondence. |
| Structure/context negatives | Reject missing/duplicate/wrong-phase entries, stale snapshots, wrong Module/ordinal/Body/index/record kind, wrong namespaces, malformed `0B`, duplicate or gapped bindings and incompatible schema/target/runtime. Exercise global 24-bit rows and Body index zero/FFFF with checked extents. Preserve sentinel destinations and start no worker on refusal. |
| Ordinary binding negatives | Reject unauthorized/private/proxy/wrong-kind/generic class, wrong self witness, incompatible class/member/default expectation, raw initializer or arbitrary factory substituted for the construction target. Reject unavailable defaults/params codecs and changed aliases/overloads without retry. |
| Phase/failure behavior | Headless supervised true/false cases: zero/one construction, once-only saved return and predicates, ordered body/phase cleanup, no later clause or Publish after false, no phase-3 falsehood construction. Actual predicate, initializer, constructor-clause and preparation failures remain themselves. Observe completed effects/output states without inventing rollback. |
| Exact runtime event | Qualify the actual constructed class object, complete state/Message, self-witness/type identity, responsible root and clause raise origin. A nonzero process exit, decoded graph, class flag or emitted PE alone proves none of these. Use runtime inspection/instrumented qualification, not a new source catch or public diagnostic ABI. |
| Lowering/lifetime/isolation | Final-patch JIT and PE/AOT inputs with explicit compiler/configuration/schema/provider/runtime identities. Retain code/backing through final use; refuse incompatible replacement/unload and all unsupported E1/CheckedExceptions/foreign or cross-image routes. |

The map choice and bounded terminal route are provisionally settled here.
Outstanding prerequisites are **actual Standard class/witness/common-member
publication and independent Core IException context, constructor-projection
and initializer/value codecs, complete phase-2 source-free reconstruction
and origin comparison, normal/partial
ownership support, managed backing/root/event lowering, API integration and
runtime qualification**. CLI map transport and bodyless exchange without a
plan-bearing input remain separately unselected decisions. This proposal
does not declare B + I + G execution or the surrounding milestone complete.
