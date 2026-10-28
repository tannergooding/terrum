# General published-predicate graph schema

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This is a concrete integration recommendation,
not normative adoption, a registered capability, compiler support or
qualification evidence. The accepted specifications remain unchanged.

## Decision and boundary

Complete the occurrence-tree representation of declaration preconditions and
normal postconditions, rather than interpreting their ordinary Body bytes or
extending the native-result selector language. Recommend:

* Candidate **`17/1=GeneralPredicateGraphs`**, governing edition 1, with
  immediate prerequisites **`[(10,1)]`**. Preserve bounded `10/1` with no
  prerequisites, its six phase-3 selectors and `8/1 -> {1/1,2/1,10/1}`.
* The exact node, operand, application, capture and read-bound schemas below.
  Keep the assigned `000A/000B` clauses and `0037..003E` record extents.
  Add candidate required sections **`004D..0052`**, a scoped captured-type
  TypeSpec form **`10`**, and read-root kind **`8`**.
* Select the [scalar frozen-value recommendation](scalar-frozen-value-registration.md)
  for `11/1`: its closed fixed-scalar domain and no unconditional prerequisites.
  Require it when a graph actually retains such a literal, not for every
  graph. Do not silently restore either `10 -> 11` or `11 -> 9`.
* Amend the still-incomplete `12/1` candidate's immediate prerequisites to
  **`[(11,1),(17,1)]`**. Constant execution includes pure-form semantics and
  must not get those semantics from the six-selector capability. This does
  **not** complete Feature 12's program, intrinsic, value or resource codecs.

These are **unapproved assignments and owner amendments**, including changes
to the scope of already assigned draft numbers. No private revision, Body
opcode, instruction prefix, escape, trust bit or compatibility reader is
selected. Proposed display names below are specification labels, not source
intrinsics, callable names or executable opcode assignments.

This decision completes the graph language and its admission rules. It does
not complete every type, witness, failure, constant or target representation
that a graph can require. A node with an unavailable independent prerequisite
remains unsupported before affected use; its result is not false, unit or an
empty dependency set. The [remaining gates](#independent-gates-and-implementation-evidence)
identify those boundaries explicitly.

### Composition and supersession

| Existing owner or alternative | Candidate disposition |
|---|---|
| [Predicate Feature composition](predicate-feature-composition.md) | Preserve its selected narrow `10/1` and all selector/output/anchor closures. Supersede its *unselected* option (b), `16/1=GeneralPredicateGraphs`, with the complete `17/1` design here. General graphs are no longer indefinitely gated merely for missing node/operand semantics once this whole candidate is selected and implemented. |
| [NativeImport pointer contracts](standard-native-import-function-pointer-contracts.md#capabilities-namespaces-and-allocation-boundary) | Preserve its separate **unapproved** `16/1=ForeignInvocationContracts` and `000E` candidates. Do not assign 16 twice or interpret it according to a Module's contents. |
| [Aggregate outputs](aggregate-native-outputs.md#exact-shared-predicate-codec) | Preserve node kinds `1..6`, operand kind 1, phase 3, their exact result-only footprint and publication semantics. A general assertion is not a selector, even if it computes the same Boolean. |
| [Function-pointer anchors](standard-function-pointer-anchor-contracts.md) | Preserve their phase-3-only predicate applicability. Feature 17 does **not** admit phase-1/2 contracts, EvaluationForm or callable read bounds on a noncallable anchor. Broadening pointer effect/contract types needs its own applicability decision. |
| [Scalar frozen values](scalar-frozen-value-registration.md) | Select its narrow `11/1`, but supersede its instruction to preserve `10 -> 11` and its old `12 -> 10,11` projection with this combined catalog. Do not activate richer FrozenValue/ImmutableData forms through that scalar registration. |
| [Foreign-interface identity](native-output-foreign-interface-method-identity.md#proposed-standard-metadata-delta) | Leave its unapproved section `004C=ForeignNativeInterface` intact. The new general-graph tables start at 004D, not at a competing interpretation of 004C. |

Features 14/15 remain the separate root/pin and copy-origin candidates.
Neither occupied candidate numbers nor current draft registry numbers are
immutable; retaining these boundaries avoids changing unrelated complete
proposals, not an additive-only compatibility rule.

### Alternatives considered

| Alternative | Assessment |
|---|---|
| Broaden `10/1` to all graphs | Coherent if adopted as a whole, but would make even result-only native output/anchor users require the general evaluator. Moving selectors elsewhere also changes Feature 8 and every selector consumer. No encoding-size advantage follows from retaining the lower number. |
| General `10/2`, retaining selector `10/1` | Conflicts with Feature 8's exact prerequisite in one using Module. A maximum-revision rule or presumed revision compatibility is not a solution. Revising the entire output bundle is larger than a distinct coherent capability. |
| **Complete the assigned tree with typed application facts — recommended** | Preserves occurrence and phase ownership while supplying selected operations, source/formal mapping, captures and finite read bounds. Small immutable identity/control records avoid putting non-reference integers or ad hoc payloads into `operandRef`. |
| Replace the tree with register Body/SSA or one universal contract program | Could consolidate execution machinery, but requires new control, local, effect, ownership, failure and source-map contracts. Body availability also does not identify a published expression occurrence. This substantive redesign is not needed to close this gap. |
| Inline all application data into variable node rows | Removes helper references but replaces the assigned 16-byte node and 8-byte operand contracts, and duplicates generic/member selection at every occurrence. The recommended helpers carry only missing typed facts; they are not another instruction stream. |

## Baseline and intended owner integration

The [specification index](../specs/README.md) leads to these controlling owners.
The source contracts below are preserved, not re-approved by this proposal.

| Owner | Preserved contract; exact proposed integration |
|---|---|
| [Published predicates and pure evaluation](../specs/language/published-predicates-and-effects.md) | Preserve complete-input purity, true/false versus true/true modes, finite roots, visible selected identities, fresh results, invalidation and no entry snapshots. Add the physical graph/read-bound mapping below; do not broaden the source subset. |
| [Contract phases](../specs/language-contracts.md#3-contracts-bracket-semantic-body-completion), [structural flow](../specs/language/initialization-and-flow.md) | Preserve staged arguments, isolated ordered phase scopes, saved results, binding assignment, cleanup and failure. Add the exact same-phase capture-input representation. |
| [Proof/constant execution](../specs/language/proof-and-constant-evaluation.md), [primitive operators](../specs/language/primitive-operators.md), [conversions](../specs/conversions.md) | Consume the same selected operations, guards, facts, outcomes and accounting. No imported private-body numeric summary, mode switch, host arithmetic or new portable budget follows. |
| [Logical predicate records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Replace open node/operand values, roles/arities and application-reference prose with this schema. Add callable-owned read-bound footprints, root 8, type-pattern recipes and the scoped type binder. Permit mutable checked-descriptor bindings as explained below. Preserve graph ownership and separate dependency families. |
| [Clause owner](../specs/representation/contract-clauses.md#2-clause-matrix-and-uniqueness), [ICOW-16/18](../specs/conformance/il-contract-clauses.md#owner-ranges-and-occurrence-keys) | Reconcile the stale statement that clause/section assignment is open with assigned `000B/0037`; preserve the 20-byte attachment and phase-1/2 meanings. Register `000A`'s per-form occurrence key below. Phase 3 remains the companion proposal's amendment. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [registry](../specs/representation/trrm-1.registry.json) | Add only the candidate sections, TypeSpec form, root kind and exact schemas below; mirror Feature registration/edges in both `semantic_manifest.features` and `metadata.feature_ids`. Node/capture complete types use TypeSpecRef, as metadata already requires, despite older logical prose calling them row-only references. |
| [Admission](../specs/representation/requirements-and-admission.md), [manifests](../specs/representation/semantic-manifests.md) | Register 17 and the selected 10/11 scopes, change the pending 12 prerequisite list, and add complete per-Module detection. Preserve exact revisions, derived closure and ordinary duplicate-list acceptance. |
| [Binding](../specs/representation/binding-and-replacement.md), [consumer verification](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md) | Add context-sensitive comparison/remapping for every new typed reference and distinguish ordinary valid order from canonical traversal order. Preserve producer truth versus mandatory safe-consumption checks. |
| [IL calls](../specs/il-instruction-set.md#calls), [resource accounting](../specs/representation/resource-accounting.md) | Lower the decoded semantic tree through existing operations/call phases. Graph records are global metadata, never Body-local CallSite indexes or a second executable encoding. Charge their selected semantic actions under the existing owners. |

The combined candidate must be selected out of band under
[draft format rules](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts).
Candidate designation `trrm/1-draft-general-predicate-graphs-1` describes this
combined design; it is not an on-wire selector. Equal `(1,0)` bytes, Features
or plausible extents do not select it. No released-schema promise or draft
migration obligation is introduced.

## Records, references and ownership

All multi-byte fields are little-endian. Section numbers, node kinds, operand
kinds, operation codes, TypeSpec forms, DefIds and Body opcodes are **different
namespaces**, even when their numeric values coincide.

* A table-selected reference is a nonzero `u32` row in `1..00FF_FFFF`, high
  byte zero. Null is permitted only where stated. It is not a DefId,
  Import/Body token, address or zero-based index.
* Complete type fields use the accepted four-byte **TypeSpecRef**, including
  equivalent F2/F3 forms. `TypeSpec.genericTypeSpec` and declaration references
  retain their row-only schemas. New helper `PredicateTypeUse` permits a
  complete TypeSpecRef without changing the operand row's table-reference
  meaning.
* Ranges use `(0,0)` iff empty, otherwise a nonzero first row and checked
  inclusive last row within the selected table and 24-bit capacity. All
  multiplication, addition, variable-row extents and allocation checks use
  nonwrapping mathematics. Counts/ordinals remain `u32`, not hidden `u16`
  indexes; actual containing-table capacity additionally constrains them.
* Variable tables use the existing `u32 offsets[count+1]` framing and exact
  row extents. Fixed tables concatenate rows without per-row headers.
  Candidate new sections all have required policy `0000`.

### Retained records

| Record | Exact retained shape and ownership |
|---|---|
| Contract `000B`, 20 bytes | Header `u32 ownerRow, u16 clauseKind=000B, u16 flags=0`; payload `u8 phaseTag, u8 flags=0, u16 reserved=0, u32 phaseLocalOrdinal, u32 predicateGraphRef`. One MethodDef, phase and contiguous ordinal own one graph. |
| `0037` PredicateGraph, 8 bytes | `u32 rootNodeRef, u32 readFootprintRef`; both required. One owning attachment; one root of built-in bool value category for phases 1/2. Additional OutputState references remain limited to the admitted phase-3 selector use. |
| `0038` PredicateReadFootprint, 16 bytes | `u32 rootFirst, u32 rootCount, u32 dependencyFirst, u32 dependencyCount`; two exclusive ranges into 0039/003A. |
| `0039` PredicateReadRoot, 8 bytes | `u8 rootKind, u8 flags=0, u16 reserved=0, u32 rootIdentity`; retain kinds 1..7 and add kind 8 below. |
| `003A` PredicateReadDependency, 8 bytes | `u8 dependencyKind, u8 flags=0, u16 reserved=0, u32 dependencyRef`; retain 1=MethodDef, 2=Constraint, 3=InterfaceWitness, 4=WitnessProjection. Kind 4 still needs its independently missing target-table codec; it cannot be decoded as kind 3. |
| `003B` PredicateNode, 16 bytes | `u32 nodeKind, TypeSpecRef resultTypeSpec, u32 operandFirst, u32 operandCount`; exactly one evaluated occurrence, never an interned expression. |
| `003C` PredicateNodeOperand, 8 bytes | `u8 operandKind, u8 flags=0, u16 reserved=0, u32 operandRef`; one table-selected typed reference. Operand position, kind and exact count are selected below. |
| `003D` PredicateChildEdge, 12 bytes | `u32 parentNodeRef, u32 childOrdinal, u32 childNodeRef`; required node rows, contiguous zero-based child ordinals. |
| `003E` PredicateCapture, 16 bytes | `u32 graphRef, u32 ordinal, u8 role, u8 flags, u16 reserved=0, TypeSpecRef capturedTypeSpec`; preserve roles 1=Value, 2=CheckedReference, 3=GenericTypeArgument. Candidate flag `01` permits binding replacement for roles 1/2; other bits and role-3 flags are zero. |

Each node is reachable from exactly one graph root. The root has no parent;
every other node has exactly one incoming child edge. Cycles, shared children,
cross-graph edges, duplicate/gapped child ordinals, orphan nodes and shared
operand rows reject. Operand ranges are exclusive, including equal repeated
semantic operands. Immutable targets may be shared under their own rules.

Each footprint now has **one** owner: a graph's actual observation, or the
callable read-bound record below. This intentionally extends the older
graph-only footprint ownership rule, not graph/allowed-bound identity.
No footprint or root/dependency row is shared between those roles or owners.
Duplicate semantic roots/dependencies within a footprint reject. Equal
entries in different footprints are separate rows.

### New candidate sections

| Section / label | Exact candidate row |
|---|---|
| `004D` PredicateOperation, stride 4 | `u16 operationCode, u8 policy, u8 reserved=0`. Closed codes/policies appear below; no Body opcode or type is hidden here. |
| `004E` PredicateCall, variable | 24-byte prefix `u8 targetRole, u8 callKind, u16 flags=0, u32 dependencyRef, u32 requirementMethodDefRef, TypeSpecRef ownerTypeSpec, u32 genericArgumentCount, u32 bindingCount`; then `genericArgumentCount` eight-byte generic arguments and `bindingCount` twelve-byte bindings below. Exact extent `24 + 8*g + 12*b`. |
| `004F` PredicateTypeUse, stride 4 | One required `TypeSpecRef typeSpec`. This is a typed complete-type identity, not another type language. |
| `0050` PredicateControl, stride 8 | `u16 controlKind, u16 flags=0, u32 value`; kinds `1=TupleOrdinal`, `2=IntrinsicProperty`, `3=OptionalObservationDepth`. Values are full-width integers, **not references**. |
| `0051` EvaluationReadBound, stride 12 | `u32 ownerMethodDefRef, u32 readFootprintRef, u32 flags=0`; both references required. Exactly one for every actual callable with either `000A` form, none on signature anchors. Its footprint is the pure-form allowed bound, not a clause's actual footprint. |
| `0052` PredicateTypePattern, variable | `u8 kind, u8 typeFlags, u16 reserved=0, u32 targetRef, u32 argumentCount`, followed by that many required typed `u32 PredicateTypePattern` rows. Exact extent `12 + 4*n`; closed kinds below. |

Operation, TypeUse, Control and capture-free TypePattern identities may be
interned when their complete resolved meanings agree. A PredicateCall may
be reused within one graph only when its dependency, substitution and binding
recipes agree; its execution/hidden-slot identity is still the **node
occurrence**, not that helper row. Same-footprint references prevent sharing
it across graph/allowed-bound owners. TypePatterns containing a capture
declarator belong to that one graph/declarator occurrence and are not shared
between binding sites.

### Operand kinds

All counts below count ordered operand rows, not children. `operandRef`
always selects the table in this list; an F2 word is valid inside a TypeUse,
not in the operand's row-reference field.

| Candidate kind | Target and contextual restriction |
|---:|---|
| `01` ReadRoot | 0039 row in this graph's **own actual footprint**. Preserve phase-3 interpretation. |
| `02` Literal | 003F FrozenValueNode **root**, with its exact complete type/value codec; never Blob, ConstantSource, ConstantExecution or an unevaluated constructor. |
| `03` Capture | 003E row owned by this graph; identity is graph plus capture ordinal, not row/name. |
| `04` Operation | 004D PredicateOperation. |
| `05` Call | 004E PredicateCall; its direct dependency belongs to this graph's footprint. |
| `06` Type | 004F PredicateTypeUse. |
| `07` Field | 004B FieldPath, including a one-step path; complete owner substitution and every FieldReference are retained. FieldDef/Import bytes are not silently accepted as a FieldPath row. |
| `08` Property | 001E PropertyDef, local definition or consumer-local expected declaration under its own Import; independently match its selected getter to the Call operand. |
| `09` UnionCase | 0034 UnionCaseDef typed row, not kind-21 DefId bits; separately retain complete owner through a Type operand. |
| `0A` Control | 0050 PredicateControl, with the control kind required by the node. |
| `0B` TypePattern | 0052 PredicateTypePattern. |

Zero and other operand kinds are invalid in this candidate. This does not
reassign operation names, witnesses or missing definition kinds to arbitrary
integers.

## Node schema and evaluation

Kinds `01..06` remain **phase-3-only** selectors. General kinds below use
hexadecimal codes and occur only in phases 1/2. Zero, `07..0F` and unlisted
values have no candidate meaning. A phase-1/2 graph cannot reuse a selector
leaf to evade the general gate, and a phase-3 graph cannot contain a general
leaf or helper record.

`V(T)` is an ordinary complete value, `P(T)` a logical place without a T read,
and `M` a pattern test receiving its subject observation from its parent.
These are specification categories, not new TypeSpecs. A place node's
`resultTypeSpec` is its referent T; an M node's is built-in bool. No fake
unit or pointer type carries a place, type witness or contextual projection.
Children are listed by `childOrdinal`; operands are listed in exact order.

### Subjects, access and primitive expressions

| Kind / label | Operands | Children | Category and exact action |
|---|---|---|---|
| `10` SubjectPlace | ReadRoot | 0 | `P(T)` for the captured receiver/formal holder, logical output, saved result, earlier phase capture or published immutable region. Root-kind restrictions below apply. No payload read. |
| `11` SubjectValue | ReadRoot | 0 | `V(T)` by the ordinary live Copy read of that subject or resolved constant. No unrecorded caller-variable reread or entry snapshot. |
| `12` CapturePlace | Capture | 0 | `P(T)` for this graph's role-1/2 phase local. Role 3 is not a value/place. Availability still follows the declaring pattern, not row presence. |
| `13` Load | none | 1 place | `V(T)` from live `P(T)` with ordinary Copy/read permission. Non-Copy inspection instead uses an eligible checked borrow/call. |
| `14` Literal | Literal | 0 | `V(T)` from the validated frozen root; complete type equals the node's T, including nominal identity and exact bits. |
| `15` FieldPlace | Field | 1 base place | `P(T)` at the validated ordered path; preserve base generation, access/backing and derived Layout, without reading the final field. |
| `16` FieldValue | Field | 1 base value | `V(T)` extracted under ordinary field Copy rules; no address is formed. A partial/non-Copy whole value is not fabricated to supply the base. |
| `17` TuplePlace | Control(kind 1) | 1 tuple place | `P(T)` at that zero-based slot; exact tuple arity/type and access apply. |
| `18` TupleValue | Control(kind 1) | 1 tuple value | `V(T)` copied from that slot under its own contract. |
| `19` Dereference | none | 1 checked descriptor value | `P(T)` through an admitted initialized reference, including its distinct checked initialization-destination projection. Preserve validity, exact referent type, backing and authority. Not a raw-ptr conversion or pin. |
| `1A` ReadReference | none | 1 place | `V(ref<T>)` through explicit permitted readonly reference formation. |
| `1B` WriteReference | none | 1 place | `V(write ref<T>)` only with actual writable authority. Neither reference node exposes an address for zero-data unit, roots storage or extends a lifetime. |
| `1C` ElementPlace | none | 2: base, index | `P(T)` through the selected built-in indexed-place contract. Validate index/place at this point, before later siblings. No indexer getter is inferred; a declared indexer uses Call. |
| `1D` IsPresent | none | 1 optional observation | `V(bool)` from a live optional discriminator; no absent payload read or consumption. |
| `1E` OptionalPayload | none | 1 optional observation | `V(T)` under already established presence and ordinary Copy permission; not an unchecked absent/default extraction. |
| `1F` OptionalSubject | Control(kind 3) | 0 | Readonly `P(T)` for the retained non-null payload observation of the indicated enclosing OptionalMap, depth 0 nearest. The node's T must match that observation; no later source reread or implicit owner copy. |
| `20` Unary | Operation | 1 value | Apply one admitted unary recipe after its operand completes. |
| `21` Binary | Operation | 2 values | Evaluate left then right once, then apply one admitted binary recipe. |
| `22` Conversion | Operation | 1 value | Apply the exact selected primitive numeric/representation conversion to the node's result type. A nominal conversion is Call, not this node. |
| `23` BooleanNot | none | 1 bool | Negate truth and exchange edge facts, without undoing completed bindings. |
| `24/25/26` BooleanAnd/Or/Xor | none | 2 bools | Eager left-to-right evaluation, then the ordinary Boolean truth table. A determining LHS does not skip RHS. |
| `27` AndThen | none | 2 bools | Evaluate RHS only on LHS true, under that edge's facts; false joins both possible false exits. |
| `28` OrElse | none | 2 bools | Evaluate RHS only on LHS false; true joins both possible true exits. |
| `29` Conditional | none | 3: bool, true, false | Evaluate exactly one result arm. Both arms have the same complete value type T; no common-carrier conversion or join-time ownership duplication. |
| `2A` Coalesce | none | 2: optional, fallback | Capture LHS once; copy its present payload or evaluate RHS only on absence under the ordinary normalized result contract. |
| `2B` OptionalMap | none | 2: optional, selected expression | On absence construct the declared absent result without evaluating child 1; on presence child 1 may use OptionalSubject and is wrapped/lifted under the already bound optional-access contract. No re-inference or double optional layer is added. |

This candidate's ElementPlace primitive is restricted to an admitted
`Span<T>` view with an exact `nint` index and complete element T. Capture the
view then index once, validate the descriptor/backing, and require
`0 <= index < length` before selecting the element. A selected checked-index
failure follows exact Core IndexOutOfRangeException; no truncation precedes
the check. Access cannot exceed the view's authority. `Span<init T>` retains
its whole-range state contract and is not an element-state/bitmap escape.
Other sequence/index domains require their actual independently selected
Call/access contract, not this node's result field or a guessed array
registration. A builtin or library slice likewise uses its selected pure
Call; no slice primitive or ambiguous range opcode is allocated.

SubjectPlace/SubjectValue can select roots 1, 2, 4, 5 and 8 where that subject
exists. Root 6 permits a constant value or a separately admitted immutable
region place according to its declared kind. Root 3 is **coverage of input
reachable state**, not an implicit dereference; first obtain the descriptor
from its input value and explicitly project it. Root 7 is not a place or a
free Boolean: mode queries remain their exact selected Core getter/intrinsic.
An output root denotes its logical inner destination, not its descriptor cell;
the explicit formal descriptor remains input root 2. For `ref<init T>` its
place type is T. For `Span<init T>` root 4 is a whole-range state observation
with that complete formal type, not a single T place or a readable fabricated
Span value; it can supply Initialized, not Load/field/element access. Ordinary
range reads require the separately established complete-range contract and
explicit input descriptor. Phase 1 has no saved ordinary result root 5.
Reserved destinations cannot be inspected.

### Primitive operation identities

`operationCode` is a new candidate **semantic recipe code**, not a Body
primary. Its complete identity also includes the node's operand/result types
and selected policy. The scalar sets I/F/C are exactly those in
[primitive operators](../specs/language/primitive-operators.md#signatures-and-availability).

| Code | Recipe / arity | Valid types and policy |
|---:|---|---|
| `0001` | Unary plus / 1 | Same I or F result; policy 0. Preserve representation. |
| `0002` | Negate / 1 | Same I with policy 1=checked or 2=wrapping; same F with policy 0 and sign-only rules. |
| `0003` | Bitwise complement / 1 | Same I; policy 0. |
| `0004/0005/0006` | Add/subtract/multiply / 2 | Same I with policy 1 or 2; same F with policy 0 and exact format arithmetic. |
| `0007/0008` | Divide/remainder / 2 | Same I or F; policy 0. Preserve division versus remainder failure distinctions. |
| `0009/000A/000B` | Integer and/or/xor / 2 | Same I; policy 0. Boolean eager composition uses its structural nodes. |
| `000C/000D/000E` | Left/right/logical-right shift / 2 | Data I and independently typed count I, result same data I; policy 0. Validate the original mathematical count against data width before the IL uint32 bridge. |
| `000F/0010` | Equal/not equal / 2 | Same one of all 26 scalar identities, result bool; policy 0. |
| `0011/0012/0013/0014` | Less/less-equal/greater/greater-equal / 2 | Same I, F or C, result bool; policy 0. |
| `0015` | Numeric conversion / 1 | Exactly a selected source/destination pair from the conversion owner; policy 0=total, 1=checked, 2=wrapping or 3=saturating only where that pair/policy is admitted. No general convertibility follows from equal widths. |
| `0016` | Bit reinterpretation / 1 | Policy 0; only an independently admitted explicit bit-preserving pair. This code grants no pointer, provenance, nominal or layout cast. |

No other code/policy combination is valid. In particular there is no
consumer-selected arithmetic default, ambient uncheckedness, universal
saturating arithmetic or bool/code-unit promotion. Source-profile resolution
occurs before encoding. Scalar APIs such as Abs/Clamp, custom operators and
conversions retain their exact selected Call unless a separately registered
intrinsic supplies their full contract.

Preserve integer overflow, zero division, signed-minimum division versus
remainder, exact shift-count failure, floating unordered comparisons, signed
zero and decimal cohort observations. Unit equality evaluates both live
operands even though its truth is fixed. Ordinary pure arithmetic retains
its runtime NaN latitude; true/true constant arithmetic uses the constant
owner's deterministic NaN recipe at the operation, never at literal loading.

### Calls, construction and observations

| Kind / label | Operands | Children and result |
|---|---|---|
| `2C` Call | Call | Completed receiver/argument expressions in **evaluation order**; `V(T)` is the selected ordinary result. |
| `2D` PropertyGet | Property, Call | Same staging; the selected MethodDef must be that property's actual getter after owner substitution. `V(T)` is a value/descriptor, not a fabricated property place. |
| `2E` CallProjection | Call | Same staging, with at least one hidden checked init output in its recipe. Produces a contextual observation, not an ordinary tuple; `resultTypeSpec` remains the call's exact ordinary result type. It may only be Match's subject. |
| `2F` Construct | Call | Fresh inline result T; callKind 2 selects its actual initialization-only constructor and fresh receiver. No managed/resource identity allocation or bitwise field-default construction. |
| `30` TupleConstruct | none | One value child per declared tuple slot in order; result is that complete tuple, arity at least two. |
| `31` OptionalNone | none | Zero children; result is the declared complete optional, no payload/default read. |
| `32` OptionalSome | none | One complete payload child; result is its declared normalized optional. |
| `33` UnionConstruct | UnionCase, Type | Payload children in declared case-field order; Type is the complete constructed union owner, identical to the node result type. No constructor is invoked merely for testing a case. |
| `34` Match | none | Child 0 is one retained subject place/value/CallProjection; child 1 is an M pattern. Result bool; supply that exact observation to the pattern tree without replay. |
| `35` TypeIdentityEqual | Type, Type | Zero expression children, result bool. Compare exact normalized types using available declared/captured evidence, not names, assignability or equal representation. |
| `36` TypeProperty | Type, Control(kind 2) | Zero expression children, result bool; control values are exactly 1=Unique, 2=Write, 3=Blittable under the accepted closed-property/refinement rules. |

No other node may consume a contextual projection as an ordinary value.
For one omitted output Match observes that destination; several use the
ordered projection pattern below. With no omissions it observes the ordinary
result. Matching a hidden output checks its actual live/vacant relation
before any read; ignoring an output does not cancel its cleanup.

The PredicateCall prefix has `targetRole=1=Direct` or
`2=InterfaceRequirement`, and `callKind=1=Ordinary` or
`2=FreshInlineConstructor`. Other values reject:

* `dependencyRef` selects a same-footprint 003A entry. Direct requires
  dependency kind 1 and `requirementMethodDefRef=0`; its MethodDef is the
  actual selected declaration or consumer-local expected declaration.
  An expected MethodDef follows its retained Import binding, not a guessed
  provider Body or a NativeImport section index.
* InterfaceRequirement requires kind 2/3 (or independently completed kind 4),
  a nonzero typed `requirementMethodDefRef`, and its complete declaring
  interface `ownerTypeSpec`. Constraint evidence must be the exact
  interface-instance requirement, not an intrinsic-property constraint.
  Closed witness selection follows the unique applicable WitnessMember.
  Compare the requirement's published contract before following its selected
  implementation; stronger implementation facts do not rewrite it.
* For a direct instance or constructed-owner call, `ownerTypeSpec` is the
  required complete declaring owner. It is zero only for a module-level
  declaration with no owner. An unconstructed nominal owner is still a
  complete TypeSpecRef, not a TypeDef row in this field.
* A call to an anchor/noncallable, unavailable accessor/constructor role,
  unresolved witness, unknown intrinsic or unsupported callback shape
  declines use. Constructor callKind requires direct selection and its
  actual admitted constructor/receiver contract; it cannot relabel a helper.
  Nodes 2C/2D/2E require callKind 1 and the exact declared result TypeSpec;
  node 2F requires callKind 2, the selected constructor's ordinary unit
  completion and the exact fresh receiver/result T. DoesNotReturn preserves
  the declared type and all required checks but produces no normal value.

Each eight-byte generic argument is
`u8 kind, u8 flags=0, u16 reserved=0, u32 argumentRef`:
`1=TypeSpecRef` or `2=ConstantSource` typed row. Match exact **method-formal**
order/kinds/count, after declaring-owner substitution. Owner arguments are
already in `ownerTypeSpec`, not repeated as method arguments. A captured
type uses the scoped TypeSpec form below. Constant arguments retain exact
domain/value requirements; nothing executes to rediscover an inference or
overload. Zero arguments is correct only when no method formals remain.

Each twelve-byte input binding is
`u8 bindingKind, u8 sourceUse, u16 reserved=0,
u32 formalOrdinal, u32 childOrdinal`:

| Binding kind | Required fields and action |
|---:|---|
| `1` ReceiverChild | `formalOrdinal=0`; select the indicated evaluated receiver child and its actual declared receiver adaptation. Exactly one iff the ordinary target has a receiver. |
| `2` FormalChild | Select the exact logical explicit-formal ordinal and evaluated child. Include unit parameters; do not count receiver/ABI-hidden results. |
| `3` HiddenInitOutput | Select one omitted eligible checked init formal; `sourceUse=5`, `childOrdinal=0` unused. Identity is `(call node occurrence, formalOrdinal, phase activation)`. Not a source read or raw native-output reservation. |
| `4` FreshReceiver | Constructor only; `sourceUse=5`, both ordinals zero unused. Its fresh unpublished T destination is the Construct result, not a caller-owned live receiver. |

`sourceUse=1..5` has the ordinary copy/read, transfer, borrow-read,
borrow-write and initialization meanings. For a borrowed child place, form
the exact checked descriptor under the complete formal's authority; a
value descriptor can instead be passed under its ordinary value contract.
Transfer is legal only for an actually transferable fresh/private value:
no input/captured owner is implicitly moved by this expression subset.
Write/init access must remain confined to eligible clause-private fresh
state/results, not pre-existing caller/input backing.

Every formal and required receiver has exactly one binding; duplicates and
missing entries reject. Canonical writers order bindings receiver first then
formal order; ordinary readers may accept a consistent permutation because
the explicit role/ordinal identifies each target. Generic arguments, which
have no explicit formal ordinal, always retain method-formal order.
Distinct evaluated children are numbered contiguously in receiver/
written-argument source order. Each such child supplies exactly one child
binding; copying one source into two arguments requires two source expression
occurrences. Published defaults become explicit typed Literal children after
written arguments in formal order. Implicit readonly materialization is
derived from the exact selected formal/child category at that child's staging
point, with the source supporting scope. This adds no default for ref/init
loans or guessed empty `params` construction. Other source argument recipes
must first be normalized through their independently admitted complete
construction/expansion contract, or remain unsupported.

Run Capture, Stage, Check, Prepare, Invoke and Publish in their ordinary order.
The metadata binding list does not reorder source children. Preserve nested
call snapshots, invocation-only checks, hidden observation/output preparation,
dependency-valid retirement, outcome correlations and actual generations.
The call's ordinary result and outputs are saved once. Hidden destinations
and implicit materializations retain their **phase supporting scope**, not a
new last-use lifetime. A skipped site has no current observation to expose.
CallProjection's hidden results are ordinary checked outputs, not aliasing
forms of Feature 8's DF/record-0C raw reservation protocol.

Fresh aggregate/tuple/optional/union construction follows ordinary child
acquisition, validity, transfer and partial cleanup. A graph neither clones
non-Copy fields nor loses a fresh owner's settlement obligation. A complete
value's Cleanup and failed construction's reverse-acquisition retirement
remain distinct. An independently missing construction/layout/cleanup codec
cannot be replaced with current field bytes.

### Patterns and typed captures

Each pattern node below returns bool and receives **one implicit observation**,
not another expression-child edge to the original subject. Subpatterns receive
the corresponding retained observation/slot without repeating getters,
constructors or source expressions.

| Kind / label | Operands | Children and behavior |
|---|---|---|
| `40` Wildcard | none | 0; ignore the position without reading/binding its payload. Does not retire or suppress a live ignored owner. |
| `41` Initialized | none | 0; test place/binding completeness without reading T. False for partial state does not establish whole vacancy; reserved referents cannot be tested. |
| `42` Null | none | 0; an eligible vacant place is a nonmatch without a value read; otherwise require legal observation and test actual absence. |
| `43` Constant | Literal | 0; establish subject availability and compare its exact same-typed scalar value under primitive equality. A nominal enum additionally needs its admitted nominal equality/value contract. No arbitrary aggregate equality or user-name intrinsic is implied. |
| `44` TypeTest | Type | 0; ordinary supported non-consuming type/presence matching, retaining qualification and checked type evidence. |
| `45` TypedValueBind | Type, Capture(role 1) | 0; ordinary typed match, then Copy-initialize the declared binding on its own positive match. |
| `46` InferredNonNullBind | Capture(role 1) | 0; ordinary `var` value binding, including its current-presence test and non-null payload snapshot. |
| `47` InferredNullableBind | Capture(role 1) | 0; ordinary `var?` value binding, preserving normalized nullable/non-null type. |
| `48` ReferencePattern | TypePattern, optional Capture(role 2) | 0; recipe kind 9/10 matches an existing compatible checked descriptor; never form ref-to-subject or narrow referent storage. Named captures retain that exact ref type/backing. |
| `49` GenericTypePattern | TypePattern, optional Capture(role 1) | 0; recipe kind 7; match exact constructor/argument kinds and bind checked type arguments, then the optional value binding. Missing runtime type/witness support rejects, not a false match. |
| `4A` TuplePattern | none | One M child per ordinary tuple slot, arity at least two; match in slot order, stop at first nonmatch, retain completed earlier bindings. |
| `4B` UnionCasePattern | UnionCase, Type | One M child per declared payload field; first test availability, complete owner and exact active case, then payload patterns in order. No constructor/Deconstruct lookup. |
| `4C` PatternNot | none | 1 M child on the same observation; negate its truth, **not** its completed assignments. |
| `4D` PatternAnd | none | 2 M children on the same observation; RHS only after LHS match. |
| `4E` PatternOr | none | 2 M children on the same observation; RHS only after LHS nonmatch. Distinct binding sites/names do not merge. |
| `4F` ProjectionPattern | none | One M child per omitted output in formal order, at least two; only a CallProjection subject with that arity. Retain each destination's availability; this is not an ordinary tuple read. |

A value/type/null/payload test of eligible vacancy is a nonmatch only under
the accepted structural matching rules; invalid descriptor formation,
unavailable required backing/evidence and a reserved place are not converted
to false. Partial aggregates cannot supply a whole-value receiver. A pattern
cannot silently copy a unique owner, box, dereference, convert, move or create
a reference. Borrow explicitly before non-consuming inspection when required.

PredicateTypePattern is a **type-argument matching recipe**, not a TypeSpec,
ordinary value, reflection object or executable expression:

| Kind | `targetRef` | `argumentCount` / `typeFlags` / semantics |
|---:|---|---|
| `1` ExactType | TypeUse | 0 / 0; exact complete type identity. |
| `2` AnyArgument | 0 | 0 / 0; `_` in one declared type or constant slot; no capture. |
| `3` NonNullType | 0 | 0 / 0; inferred non-nullable **argument type**, not an element-value test. |
| `4` AnyType | 0 | 0 / 0; inferred nullable-allowed type argument, retaining actual qualification. |
| `5` BindNonNullType | Capture(role 3) | 0 / 0; kind 3 plus that type binding. |
| `6` BindAnyType | Capture(role 3) | 0 / 0; kind 4 plus that type binding. |
| `7` NominalApplication | TypeUse naming exact nominal generic declaration | One recipe per declared formal in order. Flags `01` request the written outer write qualification, `02` the written nullable generic-pattern form; all other bits zero. Apply ordinary generic/type/presence matching, not structural duck typing or conversions. |
| `8` ConstantArgument | ConstantSource | 0 / 0; exact admitted typed constant argument under its formal's canonical value identity. |
| `9/10` Read/WriteReference | 0 | Exactly one type-argument recipe / flags 0; ordinary explicit reference-pattern nullability filters and actual access. |

All row references in recipes are typed and nonzero except the explicitly
zero target variants. Recipe argument graphs are acyclic; argument counts
match the selected constructor, and type-only recipes cannot occupy constant
slots. This schema does not permit constant-argument capture, argument packs
or higher-kinded patterns. Kind-2 `_` is not an existential TypeSpec or an
ordinary call-site inference hole.

**Candidate TypeSpec form `0x10=PredicateCapturedType`:**
exactly six bytes `u8 form=10, u8 qualifiers=0, u32 captureRef`.
The required capture is role 3. Its `capturedTypeSpec` names this binder type;
the binder's identity is `(owning graph identity, capture ordinal)`. This is
a scoped type variable analogous to a declared formal, **not** a structural
cycle to unfold or a persistent concrete type to guess. A successful matching
recipe supplies its complete actual type, qualification and checked evidence.
Value/reference captures whose type depends on it retain the corresponding
complete constructed TypeSpec.

This narrowly adds the missing identity for a named type capture; it does not
put an implicit formal in MethodSig or change `0F=Existential`. Without a
scoped binder, substituting a witness/name/row or storing zero would lose that
identity. A broader general local-type-variable representation is an
alternative redesign, not an additional codec selected here.

The binder TypeSpec and TypeUses containing it are valid only in the owning
graph or later graphs of the **same MethodDef and phase**, with the required
positive evidence available at every use. They cannot escape in a published
MethodSig/field, anchor, frozen literal, another phase or Body local type.
Earlier-TypeSpec rules for other structural children remain; the
TypeSpec-to-capture binder edge is a declared identity edge, not permission
for any other structural type cycle. Complete comparison resolves actual
evidence before claiming equality or execution support.

Capture ordinals are contiguous per graph in declaration order. Each capture
has one defining positive binding site, including a type-recipe binder; reads
cannot use a forward declaration or invent availability. Its mutability is
binding replacement permission, not new referent access. Named binding
assignments follow ordinary build-before-drop rules; failure, negation, later
nonmatch and short-circuiting do not roll them back or clear an earlier value.
Positive writes and joins retain actual phase-local live/vacant/partial state
and cleanup responsibility.

The role-2 mutable flag is an intentional small amendment to the logical
record's current role-1-only flag domain: the accepted
[reference-pattern production and binding rules](../specs/grammar-spec.md#33-statements-and-local-declarations)
admit a mutable named descriptor binding. Preserve that permission in the
existing flag byte rather than silently making it fixed or reclassifying an
explicit checked-reference capture as an untyped value. Matching may perform
only the ordinary admitted access attenuation; the capture's complete
descriptor type records that result, never an upgrade or lost backing.

### Earlier clauses in one phase

Retain graph-local capture identity without copying binding payloads into
later graphs. Add candidate **read-root `8=PhaseCapture`**, with
`rootIdentity` a required typed 003E reference to an **earlier phase-local
clause** of the same MethodDef and phase. Compare it by that graph's
phase/ordinal and capture ordinal. No forward, other-phase or other-owner
reference is valid.

Later value/reference reads use their own ReadRoot operand, not a cross-graph
Capture operand. A type use of an earlier binder likewise records the
PhaseCapture evidence root. Follow its original value/state/backing
dependencies; it is not a new value copy, entry snapshot or lifetime extension.
Same-graph Capture operands still refer only to their own graph.

Every phase-local capture and supporting hidden slot starts vacant at phase
activation, even when its site is skipped. Phase completion retires actual
live values under ordinary reverse declaration/acquisition rules. Names and
type evidence do not escape into the body, opposite phase or caller facts.
Published facts must be expressed over eligible receiver/formal/result/output
identities and survive phase cleanup; retaining a private capture does not
publish its name or extend its backing.

## Forms, read footprints, failure and dependencies

### EvaluationForm and callable allowed reads

Retain `000A`'s exact 12-byte shape and tags `1=Pure`, `2=ConstEval`.
Recommend occurrence key **`(owner,000A,formTag)`**, permitting each once.
This intentionally reconciles the registry's current owner/kind-only key
with the source owner permitting both attributes. ConstEval implies pure
support; an additional Pure clause is redundant capability, not a duplicate
of a different tag. A canonical writer orders the retained tags numerically.
Duplicate identical tags reject; neither form is an Effects bit.

Each such actual callable owns one EvaluationReadBound and a distinct
allowed-read footprint. It applies to both true/false and true/true pure
forms conservatively; a more precise constant-only footprint is not a new
schema variant. The complete finite allowed set covers:

* Receiver/input value and reachable-state roots actually permitted by that
  form, including transitive input backing and immutable data;
* Mode root 7 where mode is observed, plus direct selected call/witness
  dependencies, including accessors, construction, contracts and cleanup;
* Its own logical result/output observations where used in completion
  contracts, preserving their separately checked input/backing relationships.

The allowed bound may conservatively include all eligible whole input roots,
but not unknown external effects. It contains no PhaseCapture root 8.
Empty is an explicit checked empty bound, not absence. Missing bounds are
unsupported required pure-form metadata, not pure-with-no-reads.
Ordinary-form effects remain conservative unknown unless another admitted
complete record supplies them; this table does not purify the ordinary form.

The Pure contract itself fixes no pre-existing external writes, no forbidden
activity and no escaping language failure on its valid domain. Fresh results
remain allowed under the accepted rules. Signature, preconditions, Dependency,
output and type contracts retain domain/transfer/publication obligations.
This does not create an ordinary NoThrow, no-safepoint or NoAlias carrier.
An unmarked helper needs independently admitted closed purity/equivalence
proof to be selected without author authorization; body availability is not
that proof, and an unavailable proof codec cannot be replaced with `000A`.

### Actual observation footprints

Retain roots 1..7 and their existing identity payloads: receiver/result/mode
require zero, input value/state/output require a valid logical ordinal,
immutable data requires its typed 0041 identity. Root 8 uses the capture rule
above. Resolve immutable declarations/owner substitutions, publication and
load lifetime through their own codecs; Feature 11's scalar scope does not
activate 0041.

The separate [ImmutableData Feature-admission proposal](immutable-data-feature-admission.md)
supplies an unapproved `19/1` ownership/closure candidate for that dependency,
amending the catalog/detection below without completing its declaration or
publication codecs. Neither this graph capability nor Feature 13 admits it.

Each graph's footprint is a complete **conservative union of possible
observations and direct dependencies**, including guarded branches. This
clarifies “actual” as the expression's dependency bound, not a producer's
trace of one successful run. An unreached branch consumes no runtime read,
call or failure, but its retained records still validate and require support.
ConstantExecution's executed-dependency list remains an executed-path record
under its own owner; do not replace either with the other.

Structural/Feature admission is not eager constant execution. A known,
structurally valid Pure-only helper in a constant-unselected branch does not
need ConstEval authorization or its constant Body merely for that request.
A reached call does. The enclosing callable's valid-domain pure guarantee
and all source-required literal/constant formation still apply to every
relevant source path under the accepted executed-path rules.

For each selected call, map its declared allowed roots through the actual
staged receiver/formals, snapshots, reference/view paths and fresh outputs.
Include all resulting external roots and retained backing. A copied scalar
depends on its staged value, while a copied handle's observation also depends
on reachable state. Do not copy a transitive call graph into the direct
dependency list: recursively validate each selected callable's own bound.
Fields use validated paths/layout, not serialized offsets or private imported
representation. Cleanup and immutable/static entry demands retain their own
effects and code/load dependencies.

The producer checks completeness and truth against authored source/public
bound inputs. Mandatory consumers check ownership, references, mappings,
types, selected contracts, supported forms and binding needed for safe use;
they do not re-prove arbitrary callee bodies or foreign promises.
Optional deeper effect/purity audits remain separately scoped.

No analysis revisions, storage addresses, cached proof Booleans or transient
loan IDs are serialized. Consumers reconstruct observation dependencies in
their own analysis. Invalidate reuse on relevant mutation, alias effects,
replacement, move, retirement, publication/guard or code/value/target changes.
Readonly, pointer inequality, NoAlias at a different extent or byte stability
after retirement is insufficient. Every requested output still gets its own
initialization/transfer, even when computation reuse is legal.

### Phase execution and failure

1. Establish ordinary prologue/owned arguments; evaluate phase-1 clauses in
   declaration order. Each clause is its own selected pure-expression
   activation: runtime true/false, required constant evaluation true/true.
   Nested eligible calls inherit the mode. An ordinary body resumes ordinary
   mode after precondition phase completion.
2. Save the ordinary return once. Complete actual body `finally`, local and
   untransferred owned-parameter cleanup before phase 2. Observe only legally
   surviving inputs/results/outputs, not changed formals masquerading as
   caller snapshots.
3. Check phase-2 clauses in declaration order, then complete phase cleanup
   before normal publication. Later clauses/cleanup must not invalidate facts
   the body/caller is entitled to rely on. Prove survival/reestablish soundly
   or diagnose; do not replay predicates or retain expired locals.
4. A normally false precondition takes its **originating callable's exact
   selected requirement-failure binding**. A normally false postcondition
   selects exact Core PostconditionException. Predicate/helper/partial-
   operation/cleanup failure propagates through its actual channel, not a
   fabricated false result or substituted postcondition event.
5. Escaping body/cleanup failure, precondition failure, no-normal-return,
   divergence and fail-fast do not manufacture a normal postcondition edge.
   Already completed cleanup is not rerun and initialized explicit outputs
   are not rolled back. Reserved/private outputs retain their own protocol.

The graph computes a Boolean and identities; it is **not the failed-path
exception constructor binding**. Actual enforcement must retain the
ordinary checked Body/program's selected failure path, including the
[RequirementFailure/ConstructorBinding records](../specs/il-instruction-set.md#runtime-require-instruction)
and exact source owner/default/constructor or the independently admitted
runtime creation contract. A caller/adapter that relocates enforcement must
carry that same binding, not select its own RequireException or infer one
from the graph. Required source-independent failure/default/class/static-init
records that remain unavailable still gate that execution. A structurally
valid graph does not fill them, and enforcement cannot simply be omitted.

Primitive proof consumption uses the accepted structural/scalar floor and
separates Proven, Disproved, Not established and Exhausted. An opaque pure
predicate can remain a runtime boundary assertion without a numeric summary.
Truth from one constant run proves no universal obligation. Required constant
work additionally needs ConstEval opt-in, its actual checked Body/intrinsic,
value admission and envelope; no runtime/host fallback.

Use the existing unoptimized semantic-action/resource recipes for reached
nodes, place selection, binding, calls, cleanup and encoding. Type-pattern
slot tests/bindings are their ordinary matching/type-evidence actions, not a
free bulk test. Retained branch validation uses structural work even when
execution skips it. No new graph fuel field, portable verifier minimum or
discount for shared metadata/cached/repeated calls is introduced.

## Complete Feature closure and per-Module detection

The **single combined candidate catalog** is:

| Pair | Immediate prerequisites | Scope/status selected here |
|---|---|---|
| `1..7/1` | Existing native catalog unchanged | Actual native uses retain their own gates. |
| `8/1` | `[(1,1),(2,1),(10,1)]` | Preserve output-cell candidate, including the separate scalar/pointer widening. |
| `9/1` | `[]` | NominalUnionMetadata remains registration-pending; 17 does not complete it. |
| `10/1` | `[]` | Complete only the companion's six phase-3 selectors. |
| `11/1` | `[]` | Select the separate closed fixed-scalar frozen-value candidate. Richer values/immutable data remain gated. |
| `12/1` | **`[(11,1),(17,1)]`** | Proposed replacement for its pending prerequisite list; registration remains incomplete. Closure `{10/1,11/1,12/1,17/1}` does not establish execution support. |
| `13/1` | `[]` | Preserve LayoutQueries registration and its conditional type/target prerequisites. |
| `14/1`, `15/1` | `[]` in their respective candidates | RootPinRuntime and CopyOriginContracts remain separate unapproved designs. |
| `16/1` | `[(1,1)]` in its candidate | ForeignInvocationContracts, not general graphs. |
| **`17/1`** | **`[(10,1)]`** | Phases 1/2, general nodes/operands/application/type-capture recipes, Pure read bounds, evaluation, comparison, transformation and before-use support defined here. |

For every using Module, inspect its **retained** declarations, expected
imports, nested complete types, Bodies/programs, contract/graph helpers,
values, bound selections and dependencies, not just reachable machine calls:

* Require 17 for every phase-1/2 attachment, either EvaluationForm, callable
  EvaluationReadBound, or retained use of a new general-graph/type-binder
  record. This includes bodyless expectations, uncalled/no-return
  declarations and constant-unselected branches. Auxiliary graph references
  inherit their owning MethodDef's Module, not a global “predicate Module.”
* Require 10 for retained phase-3 selectors; derive it for 17 and 8. Feature
  10 alone never admits general nodes, Pure, or phase-1/2 assertion graphs.
* Require 11 for every retained admitted scalar literal/frozen argument,
  including literals that no executed path reads. A unit literal still has
  its real frozen-value registration. General graphs without literals do
  not acquire 11 or 9. Missing richer value/immutable-data registration is a
  refusal, not permission to widen 11.
* Require 12 for retained ConstEval form/required ConstantExecution or its
  other assigned uses. Pure runtime predicates do not automatically require
  12 or a body for a bodyless checked pure helper's published contract.
  Actual evaluation still needs its executable implementation.
* Require 9 for retained nominal-union metadata/case uses, and the complete
  separate union registration before their use. Require 13 for FieldPath/
  FieldDef Import binding, retained TypeIdentity/layout observations and
  associated target-layout dependencies under its owner. A type-property
  Blittable observation retains its actual layout/target dependencies.
* Add independent native/root/pin/copy-origin/foreign requirements only for
  their retained uses. No form, pointer bits or generic graph invocation
  substitutes for those protocols. Features do not supply a missing runtime
  type-witness, handler, output, ownership or target codec.

Unreferenced immutable operation/type/control helper rows have no invented
using Module; table presence alone does not activate a capability. They
still satisfy the selected schema. An unowned graph, footprint or capture
is invalid, not such an unused immutable helper. Retained unsupported uses
cannot be hidden as optional attributes/debug data or trimmed only from the
manifest.

Compute each Module's least exact closure. Physical prerequisite rows may be
omitted; reordered lists/identical pairs are ordinary equivalent input.
Different revisions of one ID conflict; no max-revision or compatibility
mapping is selected. Canonical writers order explicit lists by unsigned ID
without repeats, not by prerequisite/topological order.

| Retained content | Sufficient explicit pairs | Effective closure |
|---|---|---|
| Phase-1 bool-input assertion, no literal/call | `{17/1}` | `{10/1,17/1}` |
| Scalar comparison with a frozen int32 literal | `{11/1,17/1}` | `{10/1,11/1,17/1}` |
| Pure callable/read bound, no literals/native uses | `{17/1}` | `{10/1,17/1}` |
| Required constant program with general predicates | `{12/1}` | `{10/1,11/1,12/1,17/1}`; still refuse missing Feature-12 prerequisites/codecs |
| General field predicate and scalar literal | `{11/1,13/1,17/1}` | `{10/1,11/1,13/1,17/1}` |
| General assertions beside direct output-cell calls | `{3/1,8/1,17/1}` plus 11 if literals | `{1/1,2/1,3/1,8/1,10/1,17/1}` plus 11 |
| General assertions beside foreign-output pointer formation/use | `{4/1,8/1,16/1,17/1}` plus actual value/query uses | `{1/1,2/1,3/1,4/1,8/1,10/1,16/1,17/1}` plus those uses |
| Union pattern without a frozen literal | `{9/1,17/1}` | `{9/1,10/1,17/1}`; union registration remains a real gate |

Packing retains these closures per Module. A provider's 17 or another packed
Module's 11 cannot repair the importing Module's missing effective
requirement. Ownerless TypeUse/TypeSpec/helper rows must have the same
resolved meaning in every using context or be cloned.

## Binding, verification, rewriting and lowering

### Binding and normalized comparison

Retain every original expected declaration, actual selected provider/key,
Module/rule/target context and complete substitution. Compare trees by
structural kind/category, complete types, ordered child/operand roles,
exact operation/policy, typed literals, selected member/call/witness
identities, formal mapping and capture/type-binder identities. Alpha-rebase
owner-local subjects/captures by logical phase/ordinal/declaration position;
external declarations and retained expectations are not alpha-renamed.
No raw graph bytes, rows, names, digest or one observed truth is equivalence.

The binding owner retains directed precondition/postcondition implication,
valid old-domain failures, modes, outputs, read bounds, backing and phase
cleanup. Exact normalized identity can establish equality; it does not solve
arbitrary implication or make additional failing clauses strengthening.
An expected pure form requires provider pure support and a satisfied bound;
read roots may narrow only under the published noninterference relation.
Changed code, constant data, target semantics or stronger facts actually
used by an optimized consumer remain retained dependencies.

Complete finite comparison work before success. A visiting type binder is
an identity anchor, not proof that its evidence is satisfied. Unsupported or
exhausted comparison declines; do not count unvisited obligations as true.
Permitted nominal recursion does not excuse structural graph/recipe cycles
or infinitely expanding substitutions.

### Producer and consumer responsibilities

Producers/source and public-bound-program verification establish legal
selection/accessibility, staged subjects, availability, purity, guard safety,
truth, alias/backing/ownership and every-exit cleanup. General graph facts
must survive shared traversal/rewriting and later verification, not only
source syntax binding. A forged unsafe/purity/body fact is not authorization.

Mandatory consumers validate before affected binding/code generation/use:

* Selected exact schema, required section policies, table/offset extents,
  arithmetic bounds, reserved fields and known tags;
* Every clause owner/range/key, phase ordinal, graph/footprint/node/operand/
  edge/capture ownership and tree/recipe topology;
* Every schema-selected reference namespace, TypeSpecRef, type/category,
  operation arity/policy, call substitution/mapping, getter/constructor/
  witness role, binder scope and same-footprint dependency/root reference;
* Required form/read-bound/manifest closure, independent representations,
  failure/enforcement program availability, target/runtime and complete
  retained expected/provider binding needed for the selected use.

They may rely on structurally consumable producer semantic facts without
re-proving body truth, all alias paths or arbitrary external providers.
Consumer checking and optional deeper audits are not an excuse to turn
missing mandatory graph semantics into trusted metadata.

### Canonicality and transformations

Canonical graph/node order remains owning resolved MethodDef identity, phase,
phase-local ordinal, then root-first DFS by child ordinal. Captures follow
graph identity then declaration ordinal. Edges sort unsigned parent row then
child ordinal; operand ranges follow their owning nodes and operand roles.
Root/dependency sets sort by kind and complete semantic identity, not row
bits; PhaseCapture uses the prior clause/capture identity. Helper interning
is optional and does not create a unique whole-artifact image.

Ordinary readers accept otherwise valid consistent physical permutations.
Neither `childRow > parentRow` nor root-first node placement is an ordinary
validity condition. Validate topology by actual references. Canonicality
audits may separately require the writer order. Duplicate clause/child/
capture keys and overlapping exclusive ranges remain invalid even if equal.

Rewriters must decode and rebuild, not substitute matching octets:

1. Remap MethodDef owners/expected declarations, contracts/ranges/phases and
   every graph, footprint, root, direct dependency, node, operand, edge and
   capture reference. Preserve occurrence identity and declaration/evaluation
   order; never intern repeated expression occurrences or distinct holders.
2. Remap complete TypeSpecRefs, capture-binder TypeSpec references, TypeUses,
   FieldPaths/FieldReferences/Imports, PropertyDef/getter and UnionCase/owner
   identities. Rebuild context-sensitive rows and captured-type scopes when
   graph/Module cloning requires them.
3. Remap call dependency/requirement/owner references and each generic
   argument according to its tag; preserve formal/child ordinals or rebuild
   the correspondence explicitly. Numeric operation/policy/control fields
   are not row references. Rebuild type-pattern references/capture binders,
   callable read bounds and exclusively owned ranges.
4. Remap frozen roots/children/Blobs, immutable data and selected code/value/
   witness/target dependencies through their own schemas. Keep actual
   observation, allowed bound, executed-constant, layout and ordinary backing
   dependencies distinct.
5. Re-sort affected canonical tables after remapping, rebuild variable
   offsets, directory counts/lengths and each using Module's Feature ranges.
   If lowering/re-encoding Bodies, separately rebuild Body-local maps,
   records, code/Block extents and affected optional SourceMap slots. There
   is no graph-row-to-Body-index shortcut or debug-data semantic carrier.

Lower the same typed tree to ordinary operations and staged calls, preserving
each reached occurrence once, guarded failure, source order, selected modes,
captured observations, hidden destinations and actual cleanup/publication.
Do not speculatively hoist dereferences/division/calls out of guards or
convert pattern nonmatch to exception suppression. Optimizer proofs may
remove work only under existing equivalence/effect/ownership rules.
JIT/AOT must retain actual code/load/layout/failure dependencies; emitting a
Boolean instruction sequence alone does not establish graph conformance.

## Required cases

These are **candidate conformance requirements, not tests run here**. Use
public compiler APIs plus independently authored full-artifact fixtures;
separate binding/decoding/proof/emission from supervised runtime evidence.
Assume all separately required contracts are supported unless being tested.

| Case | Required outcome |
|---|---|
| GPG-01 Positive scalar graph | Phase-1 bool input and phase-2 saved scalar-result comparison use exact typed subjects, general nodes, declaration order and independent allowed/actual footprints. No private callee-body inference. |
| GPG-02 Literal/reference namespaces | Frozen int32 zero uses operand 02 to a FrozenValue root, not Blob or ConstantSource; equivalent F2/row TypeSpecs in nodes/TypeUses compare equally. A token-shaped row reference, wrong-table operand or mismatched literal type rejects. |
| GPG-03 Guarded partial operation | AndThen guards zero division, a dereference or output payload read; RHS is never evaluated on the determining edge. Eager BooleanAnd evaluates it and retains its failure. Constant-unselected nodes still validate structurally. |
| GPG-04 Once-only order | Two equal getter occurrences, nested calls, reordered named source arguments and a returned-reference backing change exercise distinct occurrences and saved observations. No node sharing, formal-order evaluation or return replay. |
| GPG-05 Primitive boundaries | All scalar widths; signed/unsigned negation checked versus wrapping; minimum divided/remainder -1; zero divisors; typed negative/overwide shifts; NaN unordered equality/order; signed zero and decimal cohorts. Preserve exact operation policy/failure/bit contracts. |
| GPG-06 Unit and presence | Live unit reads/equality retain both evaluations and availability; vacant unit is not (). Optional absence does not read a payload. Coalesce/OptionalMap preserve one observation and skip only the unselected branch. |
| GPG-07 Initialization distinction | Descriptor-cell state versus its checked init referent, partial whole aggregate versus vacant, and reserved output state remain distinct. Invalid backing/evidence never becomes a false/null/default result. |
| GPG-08 Pattern assignment | Positive binding under PatternNot, later And failure, distinct Or bindings, mutable build-before-drop and skipped sites retain completed assignments/ordinary joins. Duplicate declarations/capture keys and implicit owner consumption reject. |
| GPG-09 Cross-clause phase capture | An earlier successful binding/type capture is read through root 8 in a later clause; preserve origin/backing and phase cleanup. Reject forward, cross-owner, cross-phase and out-of-scope type-binder references. |
| GPG-10 Generic recipes | Exact nominal constructor, `_`, non-null versus nullable-allowed type filters, named captures, constant slots and reference patterns preserve kind/arity/access and exact actual types. No existential/inference-hole alias, constant capture, pack or higher-kinded wildcard. |
| GPG-11 Type evidence | Scoped form-10 binder, exact type equality and closed property branches retain positive/closed-negative facts. Missing runtime witness/shared execution rejects rather than returns false; equal layout or display name is not type identity. |
| GPG-12 Calls and properties | Direct expected MethodDef, constrained requirement and closed witness calls retain complete owner/method arguments, getter identity, Pure and mapped read bounds. Wrong constraint kind, unavailable projected-witness codec, anchor or invented getter rejects. |
| GPG-13 Fresh results/outputs | Inline construction and omitted checked outputs retain private destinations, saved discriminator, whole completeness, dependencies and supporting phase scope. Ignored owners still retire; live/null versus vacant and overlapping output obligations are not merged. |
| GPG-14 Failure ownership | False require uses its originating selected E/default constructor binding, false ensure uses Core PostconditionException, helper failure keeps its own event. Failed argument/check/cleanup and postcondition failure neither replay cleanup nor publish normal success. |
| GPG-15 Purity and invalidation | Input-alias reads, mutable reachable backing, immutable publication/load lifetime, unrelated writes and invalidating callbacks preserve/invalidate the correct observations. Missing ordinary effects are unknown; readonly/RVA/NoAlias alone do not prove reuse. |
| GPG-16 Modes/proof/constant | Runtime clause true/false, enclosing ordinary-body resumption and required request true/true remain distinct. Pure-only helper is not ConstEval; one true execution is not static proof; no resource/runtime/host fallback. |
| GPG-17 Exact structures | Truncate each old/new row/prefix/array, add trailing bytes, nonzero reserved fields, unknown node/operand/recipe/policy, missing owners/roots, duplicate/gapped ordinals or overlapping ranges: reject selected invalid input. |
| GPG-18 Topology/ordinary order | Permuted valid nodes/edges/captures/helpers remain ordinary input; canonical rewrite restores order. Reject shared non-root nodes, cycles, orphan occurrences, cross-graph operands and ambiguous capture binders regardless of physical ordering. |
| GPG-19 Capacity | Exercise first/last 24-bit typed rows, full-width controls/formals, exact variable extent and one-over/wrapping ranges. Reject count 01000000 for a global table, high-byte typed references and overflow before indexing/allocation. No Body-u16 limit is imposed on graph identities. |
| GPG-20 Catalog composition | `{17/1}` without literals, `{11/1,17/1}`, and output/foreign-pointer combinations derive the exact table above. Missing effective 17/11, conflicting revisions and use of 16 for graphs fail; duplicate/reordered identical Feature lists remain equivalent. |
| GPG-21 Per-Module/support boundary | Packed A has general graphs, B only selectors. B's manifest cannot admit A, and 17 cannot widen B's anchors. Known union/richer-value/constructor/handler shapes without their complete registrations remain unsupported. |
| GPG-22 Binding/replacement | Rename/remap rows and alpha-rebase local captures without changing contracts; reject weakened read/domain/output/failure/mode/backing/target facts and preserve every original expectation, including uncalled/address-only dependencies. |
| GPG-23 Re-encoding | Permute all typed tables, clone owner-sensitive graphs/types, remap constants/fields/witnesses/calls and relower Bodies. Retain one-parent occurrences, phase scopes, exact operands and code/source extents; raw byte replacement/header relabeling is not conversion. |
| GPG-24 Trust/resources | A structurally consumable false body/effect promise remains a producer violation, not mandatory semantic re-proof. Separately test malformed structure, unsupported prerequisites, optional audits, exact/one-over selected work/frame/live-state/result envelopes and refusal without partial success. |

### Small candidate graph fixture

For one int32 input at ordinal 0 and a phase-1 comparison “input >= frozen
int32 zero,” a minimal semantic fixture is:

```text
Contract: MethodDef M, kind 000B, phase 1, ordinal 0, graph 1
Graph 1: rootNode=1, footprint=1
Footprint 1: roots=(1,1), dependencies=(0,0)
ReadRoot 1: kind=2, flags/reserved=0, identity=0
Operation 1: code=0014 (greater-or-equal), policy=0, reserved=0
Node 1: kind=21, result=bool,  operands=(1,1)
Node 2: kind=11, result=int32, operands=(2,1)
Node 3: kind=14, result=int32, operands=(3,1)
Operand 1: kind=04, ref=Operation 1
Operand 2: kind=01, ref=ReadRoot 1
Operand 3: kind=02, ref=FrozenValueNode Z
Edges: (parent=1, ordinal=0, child=2), (parent=1, ordinal=1, child=3)
Required effective Features: 10/1, 11/1, 17/1
```

Z uses the separate scalar proposal's exact int32/FrozenValue/Blob codec;
no literal bytes or source expression occur in an operand. This is a
**candidate fragment**, not a full executable artifact: the MethodDef,
manifest, complete types/value rows, enclosing implementation and exact
failed-assertion binding still have to be supplied and validated.

## Independent gates and implementation evidence

The following are source-inspection observations in this workspace, not
passing test/build/runtime claims. Some older proposal evidence is stale
relative to these sources; it is not another permissible schema.

| Current source | Evidence boundary / required integration |
|---|---|
| [Predicate graph tables](../../sources/Terrum.CodeAnalysis/Modules/TrRmPredicateGraphTables.cs), [candidate tables](../../sources/Terrum.CodeAnalysis/Modules/TrRmGeneralPredicateTables.cs) | Graph/footprint/capture records retain their assigned shapes. Child-edge validation now follows node references and accepts noncanonical node/edge row order; writers retain canonical edge order. The internal `GeneralPredicateGraphsDraft` profile also has candidate 004D-0052 codecs, TypeSpec form 10 and ReadRoot kind 8. This remains provisional structural support, not a general executable predicate evaluator. |
| [Selector and general-graph validators](../../sources/Terrum.CodeAnalysis/Modules/TrRmPublishedPredicateSelectorValidator.cs), [structural composition](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs) | Standard still invokes the closed phase-3 selector validator. The internal candidate profile additionally validates attached phase-1 `TypeIdentityEqual` graphs and rejects selector nodes in phase 1; complete per-node type/effect/witness/failure verification, source binding and lowering remain open. |
| [Contract reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs) | Candidate profile validates exact 000A Pure/ConstEval rows, distinct `(owner,formTag)` keys, 000B attachments and exact Feature 17 closure. EvaluationReadBound presence is checked structurally; the actual allowed-read semantics and consumer execution remain open. |
| [Feature reader/validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs) | Standard keeps its current catalog. The internal candidate profile registers 17/1, derives 17->10, removes 10->11 and 11->9 only within that profile, and uses 12->11+17. This implements the unapproved proposal provisionally; no normative registry or Standard profile change is made. |
| [Frozen-value reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenValueNodeTable.cs) | Supplies the separate bounded fixed-scalar payload path and unit rule; nominal/composite/immutable/native-width completion is not inferred from recognizing kinds 1..6. |
| [Binder.ConstEval](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstEval.cs) | Contains hosted scalar constant/proof machinery, saved predicates and private depth-64/work-4096 limits. Those are not the accepted portable resource floor or a serialized general predicate evaluator. Reuse typed scalar operations/facts, not a second predicate-only arithmetic implementation. |
| [Graph-table tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmPredicateGraphTableTests.cs), [candidate codec tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGeneralPredicateGraphTableTests.cs), [constant tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ConstEvalTests.cs) | The focused candidate/graph/contract selection passes 65/65 with zero skips, including a source-free phase-1 TypeIdentityEqual artifact and ordinary node-row permutation. It does not establish source admission, proof/ConstEval execution, dependency/replacement remapping, or JIT/AOT execution. |

Independent gates remain explicit:

* Nominal unions/Feature 9, tuple TypeSpec assignment, richer frozen values,
  native-width target-bound constants and ImmutableData declaration/publication
  codecs are not completed here. Candidate graph/type-pattern nodes specify
  their uses exactly but cannot activate those missing representations.
* General runtime captured-type/witness execution and WitnessProjection's
  physical table/identity remain separate. Form 10 supplies capture **identity
  and scope**, not proof, code generation, boxing or a runtime ABI.
* Actual constructor/accessor/ownership/partial cleanup, NoAlias, static
  initialization, failed-path exception/default binding and handler/runtime
  carriers remain required where used. A graph cannot synthesize these facts
  from source names, complete type shape or the Pure flag.
* Feature 12's full checked program/intrinsic registration, exact resource
  recipes, substitution/input/view transport, target profiles and executed/
  layout dependencies still need their own completion. This proposal supplies
  general predicate and pure-read dependencies, not an alternate constant
  instruction language.
* Function-pointer anchors remain selector-only. Native, root/pin and foreign
  invocation candidates retain their separate applicability/ABI/allocation
  gates, including the already documented NativeAddress/root-pin Body-kind
  conflict; no general graph number resolves that conflict.

Implementation must carry this model through binding, shared bound-tree
traversal/rewriting, mandatory producer verification, Standard production/
loading, retained dependency binding/replacement and JIT/AOT lowering.
Qualification must include unrelated synthetic APIs, negative/cross-feature
fixtures and independent schema readers, not only native-output samples.
[CG-001](../implementation/conformance-gaps.md#cg-001),
[CG-007](../implementation/conformance-gaps.md#cg-007) and
[CG-010](../implementation/conformance-gaps.md#cg-010) remain the maintained
integration/acceptance owners; this document does not change their status.

No further graph-tag, operand-namespace, application-mapping or Feature-17
policy is delegated to an implementer of this recommendation. Approval,
normative reconciliation, independent prerequisite completion, implementation
and final-input qualification remain distinct. This proposal and its index
entry supply no compiler migration, executed validation or milestone closure.
