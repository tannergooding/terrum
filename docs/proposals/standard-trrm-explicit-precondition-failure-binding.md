# Standard TRRM explicit precondition failure binding

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This selects an accompanying binding contract,
not normative adoption, compiler support or an execution policy.

## Decision and gap

For declaration `require<E>` under exactly
**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-1`**
([B + I + G](standard-trrm-initialization-predicate-composition.md)), require
one producer-returned, nonserialized **precondition failure-binding map**.
It classifies every retained phase-1 occurrence as explicit E or the
originating Module's Core default, and maps each occurrence to its already
selected assigned Body **`0B=ConstructorBinding`** record. That record's
exception TypeMap entry supplies the complete failure class and constructor
plan. The Core-default case must match its selected RequirementException role;
the explicit case retains E without requiring that role. No second class
selector or constructor lookup is needed.

Contract `000B` retains phase, phase-local ordinal and PredicateGraph reference,
not explicit E or its construction binding. The
[Core role selection](core-contract-failure-identity-selection.md) intentionally
does not select E. The
[postcondition construction map](standard-trrm-initialization-predicate-postcondition-construction.md#exact-accompanying-map)
applies only to phase 2; reusing its plan-location technique does **not** extend
that map to phase 1 or select its terminal execution route.

### Alternatives

| Route | Assessment |
|---|---|
| **Total phase-1 accompanying map — recommended** | One nullable plan location distinguishes ordinary from explicit selection, supplies E through the existing plan, and makes missing occurrence entries structurally detectable. Adds no wire field or independent type/constructor selector. |
| Wire-owned failure binding | A required companion clause keyed by phase-1 ordinal could carry `TypeSpecRef E` plus typed construction-plan/origin references; alternatively enlarge/replace `000B`. Self-describing bodyless exchange also needs metadata-level plan/default/origin schemas, with complete ownership, reference, canonicality, remapping and exact-schema decisions. Viable, not selected; unpublished IDs/layouts may be reworked without aliases or compatibility readers. |

Keep `000B`'s **12-byte payload / 20-byte row**, zero flags/reserved fields,
G's catalog and the exact B + I + G wire selection unchanged. This is a
required accompanying-context amendment, not an alternate decoder. No new
ID, Feature, section, opcode, schema token or production provider/key is
assigned. Exchange must agree on this handoff; equal version bytes or
Features cannot recover missing context.

**Non-goals:** statement/value requirement Bodies, ordinary default-recipe
codecs, phase-2/3 carrier redesign, E1 composition, ABI/runtime policy and
CLI/sidecar transport.

## Owners and intended integration

| Owner | Preserved contract or proposed amendment |
|---|---|
| [Requirement failures](../specs/language/requirement-failures.md), [local failures](../specs/language/local-failures.md) | Preserve explicit E before callable default before Core fallback, concrete class/self-witness eligibility, ordinary zero-supplied construction and original semantic ownership. No source or library amendment. |
| [Contract clauses](../specs/representation/contract-clauses.md), [G failure semantics](general-predicate-graph-schema.md#phase-execution-and-failure), [logical requirement records](../specs/representation/logical-records.md#runtime-requirement-construction-and-defaults-encoding-pending) | Add the phase-1 accompanying association/coverage below to the B + I + G handoff. Preserve graph ownership, phase-local order and independent missing codecs. |
| [Failure-role handoff](standard-trrm-initialization-predicate-failure-identity.md), [phase-2 construction](standard-trrm-initialization-predicate-postcondition-construction.md) | Keep Core roles and the phase-2 map separate. Neither supplies explicit E. Their required contexts remain inputs where independently used. |
| [Typed Body records](../specs/il-instruction-set.md#typed-operand-records) | Count the new map reference as a required logical use of `0B`, including canonical first-use/record retention. Reuse its exact payload; introduce no condition register or `RequirementFailure` record for a metadata predicate. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md), [format agreement](../specs/representation/format-versioning.md) | Preserve every original expectation and actual origin. Add mandatory structural/support/binding checks for the map, not source reconstruction or arbitrary provider-body proof. |

## Exact candidate carrier

Propose public host records **`TrRmPreconditionFailureBinding`** and
**`TrRmConstructorBindingLocation`**. These names and API shapes are
**unapproved candidates**, not serialized records.

| `TrRmPreconditionFailureBinding` field | Exact shape |
|---|---|
| `ClauseArtifact`, `MethodDefRow`, `PhaseLocalOrdinal` | Captured `TrRmStandardArtifactInput`, `uint`, `uint`; identifies one retained phase-1 `000B`. Phase 1 is implicit, not another tag. |
| `OriginArtifact`, `OriginMethodDefRow`, `OriginPhaseLocalOrdinal` | Captured `TrRmStandardArtifactInput`, `uint`, `uint`; identifies the original declaration occurrence and its owning logical Module. |
| `FailureSource` | Required closed value `ExplicitType` or `CoreDefault`. It preserves whether source selected E explicitly or used the originating Module's default. |
| `ConstructorPlan` | Required `TrRmConstructorBindingLocation` for both sources. The plan's exception TypeMap entry supplies the exact class; explicit E is retained as that typed binding, while CoreDefault must match the selected RequirementException role. |

`TrRmConstructorBindingLocation` contains exactly
`TrRmStandardArtifactInput Artifact`, `uint MethodDefRow`, and
`ushort ConstructorBindingRecordIndex`. Derive the nonzero Body from that
MethodDef; the index selects record kind `0B` in **that Body**. Its TypeMap,
MethodMap and ConstantMap are interpreted in the Plan MethodDef's context,
not the Clause or Origin context.

MethodDef rows are one-based `1..00FF_FFFF`, reserved high byte zero.
Ordinals are full-width zero-based `uint` phase-local integers, not rows.
Record indexes are zero-based `ushort`: both zero and FFFF are valid when
present; neither is an absence sentinel. Check counts/extents without
wrapping. Artifact fields identify captured input occurrences, not filenames,
mutable buffers or identity inferred from content equality.

Require exactly one entry per retained phase-1 attachment in the operation's
retained root/provider closure, including ordinary, bodyless, inherited,
uncalled and proved/eliminated occurrences. Reject null entries, duplicate
Clause keys even when equal, missing/extraneous entries, and phase-2/3 or
static-proof entries. Require an initialized array, empty only with no
retained phase 1. Total coverage prevents omitted transport from silently
classifying an occurrence as ordinary. Array order has no execution meaning.
Several entries may share an immutable plan only when resolved class,
constructor and default facts agree; their occurrences, origins and
executions remain distinct.

An original occurrence names itself as Origin. Imported expectations follow
their exact ordinary declaration binding. Inherited, cloned or relocated
occurrences retain explicit supported declaration/formal/ordinal
correspondence to that original; physical placement and matching names do
not establish it. Origin must identify a retained original occurrence, not
an arbitrary chain/cycle of aliases. Its classification and resolved binding
must agree under that correspondence. Unsupported correspondence declines.

The original Module supplies `UsingModuleIdentity` even for explicit-only
checks with **no** Core failure-role record. Reconstruct explicit clauses with
`FailureSource=ExplicitType` as the plan's complete normalized E in
`ExplicitFailureType` and `SelectedFailureClass=null`. For
`FailureSource=CoreDefault`, reconstruct `ExplicitFailureType=null` and retain
the exact selected RequirementException role. Do not put explicit E in a Core
role slot or rekey either source to the importing caller. This map does not
complete RequireException recipe publication or ordinary construction.

## Binding and consumer obligations

1. Capture input bytes, collections and selections once. Select the exact
   B + I + G schema for root and every supplied provider before dependent
   decoding; require unambiguous actual Module snapshots and compatible
   edition/catalog/target/runtime requirements. Check map coverage against
   MethodDef Contract ranges, phase-1 contiguous ordinals, graph ownership,
   and the explicit Plan Body/index/kind/extent. Never scan for a plausible
   record or use graph/DefId bits as a Body index.
2. Resolve `0B.exceptionTypeMapIndex` through its complete typed binding.
Preserve the class's nominal identity, qualifications, substitutions,
every original Import expectation, actual provider revision and exact
export key, using ordinary ordinal text comparison without normalization.
For CoreDefault, require exact equality with the originating Module's
selected RequirementException role. For ExplicitType, the resolved class
is the retained explicit E and no Core default role is required. Apply
supported Plan/Origin/Clause substitutions; never capture unrelated Plan
formals implicitly. Local declarations use direct identity; this proposal
imposes no Core spelling or blanket public/nongeneric requirement on local
E. External references need their actual ordinary public provider-local
exports, not proxy re-exports or invented keys.
   Require a concrete managed exception class and its exact self-typed Core
   IException witness; layout, class flags or a same-named interface do not
   supply eligibility. Independent checked IException context is required;
   this selects neither E1 nor an Unreachable anchor.
3. Validate the exact constructor MethodMap target, selected owner/member and
   original callable expectations. Require the admitted ordinary constructor
   projection whose constructed result is exactly E, not an arbitrary
   class-returning factory or raw unit-returning initializer. Preserve the
   original access environment and all modes, effects, safety,
   initialization/final-validation and cleanup obligations. Cross-Module
   callable binding needs its own real keys; E's class key supplies none.
4. Apply assigned `0B` rules: `suppliedCount=0`,
   `formalCount=bindingCount` matching the signature, ordered complete formal
   coverage, valid binding kinds/source uses and exact ConstantMap defaults
   or admitted empty-params values. Zero supplied does not mean zero formal
   parameters. Bind once at the original check; consumers check the recorded
   binding, not overload applicability by renewed lookup. Missing codecs,
   inaccessible/ambiguous source binding or incompatible retained facts
   decline; do not retry another constructor, class, provider or key.

The producer establishes truthful selection/access/eligibility/construction.
Consumers check structure, declared evidence, support and binding, not authored
E recovered from source or provider-body truth. Hosts cannot replace the map
to evade retained expectations; false published facts remain producer
violations, not a new mandatory truth-audit requirement.

## API, transformations and enforcement boundary

Add candidate `LoadStandardWithPreconditionFailureBindings` and
`CreateStandardReferenceWithPreconditionFailureBindings` in
`TrRmExecutableCompiler`. Each takes, in order, existing
`input`, `moduleIdentity`, `providers`,
`ImmutableArray<CoreContractFailureIdentitySelection>`,
`ImmutableArray<TrRmPostconditionConstructionBinding>`, and required
`ImmutableArray<TrRmPreconditionFailureBinding>`.
They return the usual checked program/reference, retaining all three arrays
and decoded original bindings. Core/phase-2 entries are required only by
their own retained uses; explicit E alone requires neither default class role.

Candidate `EmitStandardWithPreconditionFailureBindings` takes the checked
program and existing Module/schema/target arguments, returns artifact bytes,
and returns those same three immutable arrays as `out` results. It derives
entries from checked clause/constructor facts and emitted references, not
host-supplied replacement selections. Maps name a captured emitted artifact
and explicitly retained provider/origin inputs. Existing byte-only or
role-only routes decline retained phase 1 without this required context.
No CLI flag, sidecar grammar or byte-only publication route is selected.

Shared traversal, substitution, rewriting, verification and source-reference
binding retain E, its complete plan, origin and original expectations as
bound facts, not only physical indexes. Re-emission rebuilds the map alongside
Module/MethodDef/TypeDef/Import/Export correspondence, Contract ranges,
phase ordinals, graphs, complete types/witnesses, Body maps and `0B` indexes,
bindings, offsets/extents and per-Module requirements. Canonical record use
includes clause associations in logical phase order; ordinary valid
permutations remain consumable with updated maps.

Keep a mapped plan as a required use when its failed branch is eliminated.
Retain or explicitly remap selected snapshots/origins; deleting the only
plan-bearing Body or required origin without a supported replacement carrier
declines. A bodyless expectation can name a retained bodyful provider plan;
do not manufacture a dummy Body. Added overloads never change the selected
binding. Replacement rechecks every original type/member/default/witness,
alias equality and stronger relied-on fact; destination Module identity
does not restamp the original using Module.

Existing requirement semantics remain: evaluate reached phase-1 predicates
once in ordinal order; true constructs nothing, normal false executes that
plan once and raises the completed E object at the retained clause origin.
Predicate/construction/cleanup failure remains itself; no later precondition
or body entry follows failure. Preserve actual output/ownership state.
This association supplies **no execution support**: it selects no ABI,
allocator, runtime policy, terminal route, handler/region carrier, E1
composition or phase-2/3 behavior.

## Focused conformance and remaining gates

These are required future cases, not results from this documentation change.

| Case | Required outcome |
|---|---|
| Explicit/default identity | Local E and imported E at an unrelated key/non-first provider survive emission, load and reference creation. Alias views retain every expectation. Same-spelled non-Core E stays that class; equality with a Core class does not erase explicit selection. Explicit-only use needs no fallback role; CoreDefault entries require the exact originating RequirementException selection and matching plan class. |
| Phase/default separation | Mix ordinary and explicit phase-1 entries, phase-2 ensures and phase-3 selectors with independently zero-based ordinals. Null entries preserve ordinary default gates; Core/caller changes cannot override E. The phase-2 map cannot satisfy a phase-1 entry, and selector falsehood constructs nothing. |
| Structural negatives/bounds | Reject absent/incomplete/duplicate/extraneous tables, wrong origins/phases/owners, stale captures, missing Body, wrong record kind/index, malformed counts/default bindings and mixed schemas. Exercise row high-byte checks, full-width ordinals, Body indexes zero/FFFF and checked extent limits. |
| Binding negatives | Reject wrong/private/missing external exports, proxy re-exports, ineligible/optional/open E, wrong self witness/owner/result, changed aliases or incompatible constructor/default contracts. Added overloads and missing optional/params codecs cause no re-resolution or fallback. |
| Source-free/cross-route | Remove source/debug data; load, create a bodyless reference with a retained provider plan, permute rows/records, clone/relocate supported occurrences, re-emit and reconsume with all arrays. Preserve actual/original provider keys, phase/ordinal/origin and exact E/plan. Unsupported origin/plan retention declines before output replacement or execution. |
| Independently admitted execution | True/false and predicate/constructor-failure cases establish zero/one construction, phase order, exact completed E/event origin and no substituted failure. Check both JIT and AOT only under an independently selected supported runtime agreement; emission or nonzero exit alone proves none of these. |

Outstanding prerequisites are **checked constructor facts on bound clauses;
Standard class/self-witness/common-member and constructor-projection/value
publication; independent IException context; complete phase-1 graph/origin
reconstruction and remapping; and API/reference integration**. Ordinary
default/recipe codecs, exchange without retained plan-bearing inputs, CLI
transport and executable class/backing/event/cleanup qualification remain
separate gates. The accompanying-map choice is provisionally settled here;
none of those prerequisites is deemed implemented or normatively approved.
