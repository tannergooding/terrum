# Standard TRRM declaration-predicate failure profile

**Status: unapproved; provisionally actionable under the user-authorized
missing-contract workflow.** This resolves profile and accompanying-context
scope only. It does not amend accepted specifications, establish implementation
support or stabilize a draft.

## Decision

Use the existing **Construction-2** exact agreement for the complete phase-1/2
declaration-failure handoff: graphs, selected classes and self witnesses,
constructor plans and their real construction closure. Its candidate identity is:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2`**

This is the already defined [Construction-2 successor](standard-trrm-c1-construction-runtime-boundary.md#exact-semantic-successor-and-candidate-assignments),
not a new profile allocation: C1M1 plus Construction-1's explicit initializer/
projection roles and Construction-2's backing lifecycle. Retain the required
nonserialized role, interface-anchor and **separate phase-1 and phase-2 maps**.
Do not broaden or relabel
`trrm/1-draft-init-reference-bodies-general-predicate-graphs-1` (B+I+G).

Select Construction-2 ordinally and case-sensitively for the root, mapped
plan-bearing inputs and **every supplied provider, including unused inputs**,
before schema-dependent decoding. Retain `(1,0)`; version bytes, Features,
record shapes and shared decoder helpers select nothing. No compatibility
alias, component parsing, subset retry, implicit conversion or API default
change is proposed.

**Parameter-label overlay:** the unapproved
[G1/C1 parameter-label proposal](standard-trrm-g1-interface-parameter-labels.md)
selects the distinct Construction-2L1 identity for complete failure
construction when the retained source views require MethodDef labels.
Construction-2 bytes do not decode label section `0066`, and Construction-2L1
is not an alias or fallback. This proposal's Construction-2 decision remains
the exact no-label agreement; the active label-preserving route uses
Construction-2L1 for the root, phase maps, plan-bearing inputs and every
supplied provider.

## Why the existing scope is insufficient

Body `0B=ConstructorBinding` is already
[assigned](../specs/il-instruction-set.md#typed-operand-records). Its presence
alone does **not** require Construction-2. The mismatch is the complete
declaration/construction closure that the accompanying maps promise:

- A graph and `000B` attachment identify neither the failure role nor the
  selected constructor. The [C1 owner](standard-trrm-exception-class-witness-binding.md#decision-and-alternatives)
  already finds B+I+G insufficient for the complete Core contract: retained
  formatter method formals require G1, even when uncalled.
- [C1M1](standard-trrm-c1-class-declaration-mutability.md#exact-schema-and-proposed-assignment)
  adds the class declaration's exact mutability fact. Class flags or a
  same-shaped interface cannot replace complete class/self-witness binding.
- [Construction-1](standard-trrm-c1-constructor-declarations.md#allocation-and-exact-agreement)
  explicitly associates complete-result projection P with initializer I
  through successor-only Contract `0003`. `0B` selects P; it does not classify
  a helper as P or a raw unit-returning initializer as a constructed result.
  Construction-2 supplies the real backing/Publish/Abort program, including
  `ED/00`, record `11` and Feature `25/1`.

| Alternative | Disposition |
|---|---|
| Extend or deliberately replace B+I+G | Permissible for unpublished drafts, but requires a complete exact agreement for generic common-member closure, class facts, I/P roles and backing operations. Keeping the old name or adding accompanying strings supplies none of these. A narrower declaration-only redesign also needs its own admission contract. No smaller wire encoding is demonstrated. Not selected. |
| C1M1 alone | Smallest existing complete-class declaration/binding stage, consistent with `core-contract-failure-provider-binding`. It is not Construction-1/2 and cannot transport their required constructor roles/lifecycle as C1M1 bytes. Retain this intermediate scope without claiming complete failure construction. |
| **Existing Construction-2 whole agreement** | Recommended smallest already specified composition for the full mapped construction closure. Reuses all required owners without another wire assignment or mixed-profile provider bridge. Its selection remains necessary, not sufficient, for admitted enforcement. |

**Work-order context:** `core-contract-failure-provider-binding` and
`contract-clause-postcondition-construction-binding` call for exact C1M1
complete class/witness binding, separate phase maps and a phase-2 map to
assigned `0B`; they exclude E1, CheckedExceptions and CLI maps. Preserve that
C1M1 binding slice. These queue entries approve no new exact identity and
establish no constructor-role/lifecycle support. When the retained closure
needs P→I or real backing Bodies, use the explicitly selected constructor
successor; this proposal recommends existing Construction-2 for its no-label
closure, not promotion of C1M1 artifacts. The label-bearing counterpart
selects C1M1L1 for the declaration-binding slice and Construction-2L1 for
complete construction under the linked proposal.

### Current admission mismatch

[`TrRmExecutableCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
keeps distinct identities/branches. Its
`IsSelectedGeneralPredicateGraphsSchemaIdentity` includes the C1 family,
including C1M1 Construction-2, whereas
`IsSelectedInitializationReferenceGeneralPredicateGraphsSchemaIdentity`
accepts only base B+I+G and its pointer-load/NativeParameter variants,
**excluding C1/C1M1 and both construction successors**.

[`StandardConsumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs)
uses the **narrower helper** to admit supplied Core contract-failure selection
tables. A nonempty table therefore rejects with C1M1/Construction-2 even
though `SnapshotStandardInput` and schema/index dispatch recognize those
profiles. The role-table-returning `EmitStandard` overload has the same narrow
gate. Recognition is not context admission; this is a current integration
mismatch, not permission to equate profiles or silently widen the helper.
Future context-bearing admission must explicitly select its supported
C1M1 binding or Construction-2 closure route and validate every required
context and same-profile input; this document changes neither helper.

[`TrRmStandardNativeBindingPlan`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs)
also admits exact B+I+G-family sets, not Construction-2. Neither shared graph
helpers nor bounded C1M1 type-reference support establishes complete
class/witness/construction or native enforcement support.

## Required accompanying transport

Producer, source-free load/reference and re-emission exchange captured
Construction-2 inputs and these immutable contexts together:

| Context | Required meaning |
|---|---|
| `CoreContractFailureIdentitySelection` | Preserve authorized roles per **originating logical Module**. Require RequirementException only for retained fallback uses, PostconditionException for retained ensures, and RequireException schema context where independently required. No ambient Core designation or production key is introduced. |
| C1's candidate `TrRmExceptionInterfaceBinding` | Independently select that originating Module's exact Core `IException<TSelf>` interface and retain its original expectation/provider snapshot. This interface-only anchor selects neither E1 nor UnreachableException. |
| [Phase-1 `TrRmPreconditionFailureBinding`](standard-trrm-explicit-precondition-failure-binding.md#exact-candidate-carrier) | Total occurrence coverage, explicit original declaration correspondence and the already selected `0B` location. Explicit E remains that complete type; it is not put in a Core role slot. CoreDefault must match the originating RequirementException role. |
| [Phase-2 `TrRmPostconditionConstructionBinding`](standard-trrm-initialization-predicate-postcondition-construction.md#exact-accompanying-map) | Total retained ensure coverage, original declaration correspondence and explicit `0B` location; its class must equal the originating authorized PostconditionException. Phase-1 entries cannot satisfy this map or vice versa. |

Reuse the maps' existing exact shapes and Plan-MethodDef Type/Method/ConstantMap
contexts; add no failure operand to `000B` or the graph. Preserve explicit E
→ callable RequireException recipe → Core fallback priority. The current
phase-1 map's ExplicitType/CoreDefault domain does not encode callable
recipes: those require their separately admitted schema/default/provenance
carrier, or affected exchange declines. Never label a recipe CoreDefault.

Coverage includes retained bodyless expectations, inherited/relocated clauses,
uncalled declarations and proved/eliminated failed paths. Static proof requests
and phase-3 selectors receive neither map; selector falsehood is not failure.
A bodyless expectation needs an explicitly retained real plan-bearing Body.
No dummy Body, Body scan, debug-source recovery or byte-only exchange replaces
that input. Writers derive and return contexts from checked facts, not host
replacement selections. Existing byte-/role-only routes decline missing
required context; no new CLI flag or sidecar grammar is selected here.

## Scope corrected or superseded

These are intended integration deltas; existing files remain unchanged.

| Owner | Required correction |
|---|---|
| [Failure-identity handoff](standard-trrm-initialization-predicate-failure-identity.md) | Supersede its exact-B+I+G root/provider and flag/API applicability claims **for complete declaration-failure binding** with the Construction-2 context-bearing route. Preserve role authority, original-owner rules and ordinary exact binding. A role-only handoff remains insufficient. |
| [Postcondition construction](standard-trrm-initialization-predicate-postcondition-construction.md), [explicit precondition binding](standard-trrm-explicit-precondition-failure-binding.md) | Supersede their same-B+I+G artifact requirement for mapped complete construction. Preserve separate maps, plan/origin semantics, zero-supplied binding and existing independent execution restrictions. |
| [B+I+G composition](standard-trrm-initialization-predicate-composition.md), [G schema](general-predicate-graph-schema.md#phase-execution-and-failure) | Preserve their exact identities, graphs/catalog and phase separation. Complete failure-class/witness/construction retention uses the selected successor, not a silently enlarged B+I+G route. Unsupported required clauses cannot be dropped or replaced by phase-3 projection. |
| C1/C1M1 and construction owners | Retain ownership of class/witness, mutability, I/P and lifecycle semantics. Their extension of the earlier handoffs means explicit successor selection, not acceptance of earlier-profile bytes. C1M1-only work retains its declaration-only boundary. |
| [Contract clauses](../specs/representation/contract-clauses.md), [registry](../specs/representation/registry.md), [format agreement](../specs/representation/format-versioning.md) | No new allocation or normative edit here. Preserve `000B`'s 12-byte payload/20-byte row; G already identifies the clause chapter's stale pending-assignment prose. Construction `0003` remains successor-only, not baseline Transfer, and Body `0B` remains a different namespace. |

## Acceptance and rejection gates

These are future gates, not build, test or runtime results.

| Gate | Required outcome |
|---|---|
| Producer | Truthfully bind source predicates, failure source, original Module and ordinary zero-supplied construction once; retain complete class/common-member/self-witness, exact P→I, defaults, access/effects/safety, initialization and cleanup facts through shared traversal and verification. Preserve the initial zero-formal execution limit without reselecting a different constructor when the bound plan needs unsupported defaults/params. |
| Consumer | Before affected use check exact schemas/support, framing/extents/reserved fields, typed bounds/owners, graph/phase/ordinal correspondence, total map coverage and captured Plan Body/index/kind. Validate `0B`, complete type/substitution/default consistency and P→I roles. Keep global 24-bit rows, full-width ordinals and Body-u16 indexes distinct, including index zero/FFFF. Mandatory structural/support/binding checks are not arbitrary provider-Body truth audits. |
| Provider/replacement | Resolve authorized exact Module/revision/key/kind before comparison; require public primary external role declarations, the complete class/self-interface witness and all common-member/private implementation closure. Local explicit E retains ordinary access rules. Recheck every original expectation, alias equality, selected member/default, origin, target/runtime and stronger relied-on fact. Added overloads, compatible shapes or changed caller defaults never reselect a role, class, interface or constructor. |
| Source-free remap | Remove source/debug data, permute rows and Body records, clone supported occurrences and reconsume both maps plus role/anchor contexts. Rebuild class/member/witness/Import/Export correspondence, graph/Contract ranges, Body maps/indexes/extents and emitted map locations together; preserve separate original expectations and all originating Modules. Mapped plans remain logical uses for retention/canonicality. Lost origin or the only plan-bearing Body causes refusal, not snapshot passthrough. |
| Closure/execution | Derive each Module's complete inherited closure: 17→10, 23 for retained generic method contracts, 25 for retained construction Bodies/operations, and 11/13 where used. For execution independently admit the construction owner's single-threaded, jointly relowered normal-backing runtime agreement, policy zero and terminal fresh-class route. Qualify once-only phase order, real completed object/Message/self witness, roots, cleanup and code lifetime in JIT/AOT. Normal false constructs/raises once; actual predicate/construction failure remains itself. No fake object or failfast substitute, rollback or normal Publish after failure. |

Reject missing/duplicate/conflicting contexts, stale or ambiguous snapshots,
wrong kinds/self/owners/roles/keys, proxy exports, malformed maps/records,
incompatible replacement and any missing required codec before affected use.
Include recognized schemas with unsupported Core-map admission, mixed
profiles and unused invalid providers in negatives; preserve
output destinations and start no worker on admission failure. Unsupported
RequireException schema/default, member/value/witness/origin, construction or
event carriers remain refusal gates. No E1/CheckedExceptions, handler/region,
CLI maps, foreign unwind, independent managed ABI or ambient runtime fallback
is composed.

## Remaining boundary

No further profile or accompanying-host-transport choice is needed for this
bounded recommendation. Complete codecs, binding/remapping, lowering and
qualification remain implementation/admission gates; this document discharges
none of them. CLI map delivery and self-contained bodyless exchange without
plan-bearing inputs remain separately unselected decisions, outside this scope.
Normative adoption and released-schema stability remain separate.
