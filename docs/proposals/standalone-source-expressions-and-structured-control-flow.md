# Standalone source expressions and structured control flow

**Status: UNAPPROVED; provisional direction under the user-authorized
missing-contract workflow, pending later review.** The admission and internal
representation choices below are candidates, not accepted language changes,
compiler support, schema stabilization or qualification results.

## Decision and scope

Extend the standalone owned call pipeline with **typed places, scalar
expression occurrences, lexical statement scopes and an explicit normalized
CFG**. Use Homes for scalar values that must survive later evaluation or an
edge; retain checked initialization descriptors under their existing non-Copy
staging contract. Write and read ordinary register Bodies, not a structured
statement wire format or a v88 overlay.

No accepted expression, initialization or loop rule is missing. The decision
needed here is the standalone operation's admission boundary and complete
owned producer/normalizer/verifier/rewriter/carrier agreement. Existing Body
records already represent its control flow. **No schema successor or new
wire record, opcode, Feature, ABI word or CLI transport is required.**

Preserve `terrum.experimental.standalone-method-imports/1`, explicit source
using-Module ownership, raw owned artifacts, nominated Module/key roots,
retained expectations and the independent Terrum compiler boundary from
[standalone method imports](standalone-scalar-frontend-method-imports.md).
Preserve target requirement **1**, runtime policy **0** and the enclosing
request's explicit source arithmetic selection. This is ordinary execution,
not a new Pure or constant-evaluation operation.

Preserve the predecessor's source lookup and candidate rules: namespace and
lexical-using scopes remain distinct; caller parameters shadow namespace
callables; local labels map arguments before applicability; supported typed
arguments use their complete input types; contextual integer literals do not
rank by range/value; result types do not rank; and colliding imported
MethodRoot views remain separate, ambiguous declaration spaces. Imported
roots remain label-free and positional. Expanding the admitted source types
must extend these same rules, not derive labels from provider metadata or add
numeric conversions.

Select exactly
**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-witness-projections-1`**
on every supplied artifact, including unused/dependency-only inputs, and on
output and source-free consumption. The [WP1 selection](witness-projection-predicate-dependency-codec.md#exact-selection-and-admission)
and [B+I+G composition](standard-trrm-initialization-predicate-composition.md#exact-selected-contents-and-ownership)
remain their unapproved owners. A source producer accepting more expressions
does not change that schema's operand meanings.

This recommendation supersedes only the first scalar-call/return source-body
ceiling in [source production and normalization](standalone-scalar-frontend-method-imports.md#source-producer-and-normalization).
It completes the internal expression/statement/CFG requirement left open
there. It does not supersede accepted specs, import exposure, graph/projection
semantics, ordinary parameter-label/default gates or retained binding rules.

## Inspected boundary

These are static source observations, not test or execution results:

| Inspected owner | Current limitation |
|---|---|
| [`StandaloneOwnedSourceProgram.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceProgram.terrum), `BindStandaloneSourceBody` | Flat return/call statements, a return required at the end for non-unit bodies, and at most an unsafe wrapper encompassing the body. No local/structured-statement arena. Bare non-unit calls are currently admitted, contrary to accepted explicit-result consumption. |
| Same file, `ParseStandaloneSourceExpression`, `ParseStandaloneInitializationArgumentExpression` | Parameter reads, contextual decimal integer/Boolean leaves, DirectCall and a parameter-only initialization argument. No operator precedence, grouping, lazy branches or local places. Headers also recognize float32/float64; that does not establish floating expression support. The initialization parser's bare parameter lookup cannot supply the accepted distinction between a descriptor cell and `*parameter`. |
| [`StandaloneOwnedSourceNormalization.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceNormalization.terrum), `VisitStandaloneSourceExpression`, `NormalizeStandaloneSourceStatement`, `VerifyStandaloneNormalizedProgram` | Traversal descends through call arguments; normalization has five operation kinds and one body-level terminal flag, not Blocks/edges. Statement traversal cannot reach new structured children. |
| [`StandaloneOwnedSourceRemapping.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceRemapping.terrum), `RewriteStandaloneOwnedCallNodes` | Separate callable/type/Body maps exist, but there are no scope/local/place/Block/edge identities to remap. |
| [`StandaloneOwnedSourceWriter.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceWriter.terrum), `BuildStandaloneWriterBodyPlans`, `WriteStandaloneWriterBody` | Writes blockCount=1, one empty BlockEntryState and CallSite records; its code planner/writer admits only the five normalized operation kinds. |
| [`StandaloneOwnedSourceDecoder.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceDecoder.terrum), `DecodeStandaloneBodyRecords` | Refuses blockCount other than one and nonempty entry-register states. Assigned branch records/opcodes alone do not establish standalone consumption. |

The [implementation summary](../implementation.md) and
[component boundary](../../sources/Terrum.CodeAnalysis.Terrum/README.md)
still distinguish bounded hosted components from an independent compiler.
[Standalone integration tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/StandaloneMethodImportInputTests.cs)
are the existing test area; their current bare-call fixtures are not authority
to retain implicit non-unit discard.

## Settled requirements and proposed choices

| Controlling section | Settled requirement preserved |
|---|---|
| [Grammar §3.3](../specs/grammar-spec.md#33-statements-and-local-declarations), [§3.4](../specs/grammar-spec.md#34-expressions-and-precedence) | Existing productions, precedence, associativity, operator spacing, attached call openers and braced control bodies. Assignment is a statement, not an operand. No `unchecked` syntax. |
| [Categories and results](../specs/language/expressions-and-statements.md#expression-categories), [explicit discard](../specs/language/expressions-and-statements.md#using-results-and-explicit-discard) | Places differ from values; non-unit normal results require use/discard. Unit is inhabited, but vacancy is not unit. |
| [Evaluation boundaries](../specs/language/expressions-and-statements.md#evaluation-and-expression-boundaries), [ordinary loops](../specs/language/expressions-and-statements.md#ordinary-loops) | Left-to-right eager evaluation, reached-only lazy operands, once-selected assignment places, exact continue/break destinations and scope retirement. |
| [Local spaces](../specs/language/local-declarations.md#declaration-spaces), [points of declaration](../specs/language/local-declarations.md#scope-and-points-of-declaration) | Whole-block lookup/collision checking, no nested local/parameter shadowing, use-before-declaration errors and independent sibling scopes. |
| [Structural joins](../specs/language/initialization-and-flow.md#transfers-joins-and-cleanup), [invalidation](../specs/language/initialization-and-flow.md#outcome-correlations-and-invalidation) | Reachable-edge joins and loop fixed points; actual place/version state and dependencies survive separately from saved-result correlations. |
| [Primitive operators](../specs/language/primitive-operators.md#signatures-and-availability), [literal selection](../specs/language/literal-construction.md#target-selection), [best candidate](../specs/language/calls-and-inference.md#applicability-and-best-candidate) | Exact types, no numeric promotion/default width, selection before literal acceptance, no overload retry after failure and no result-type/literal-fit ranking. |
| [Initialization references](../specs/language/references.md#initialization-references), [destination projection](../specs/language/references.md#initialization-destination-projection) | Descriptor versus referent, exact vacancy, scoped child reborrow, consumed init authority and independently preserved outer write authority. |
| [Caller-unsafety](../specs/grammar-spec.md#53-caller-unsafe-declarations-and-explicit-unsafe-scopes), [no-return typing](../specs/language/expressions-and-statements.md#no-return-contracts) | Declaration unsafety does not authorize its body. DoesNotReturn removes normal continuation, not the declared result type, failures or source checks. |
| [Body and entry states](../specs/il-instruction-set.md#body-row-layout-and-compact-implementations), [reader trust](../specs/representation/reader-trust.md) | Explicit terminal Blocks and typed namespaces; mandatory safe-consumption/support/binding checks remain distinct from producer semantic truth. |

**Proposed choices**, not new language rules: the finite source closure below,
the internal records, Home-first normalization, complete multi-block standalone
admission and reuse of the already selected initialization-state protocol.
No accepted production is added, removed or reinterpreted.

### Exact source closure

Let **I** be int32/uint32/int64/uint64, **F** float32/float64, and
**S** I plus F, bool, char16 and void. Preserve receiver-free, nongeneric
callable headers and namespace/import lookup from the predecessor. Source
results and by-value formals use S; initialization formals use the admitted
complete `ref<init T>` or `write ref<init T>` for non-unit T in S.
No qualification is erased to obtain membership.

The candidate admits the following forms closed under grouping, nesting,
argument use and structured block composition:

| Area | Included forms |
|---|---|
| Leaves/construction | Initialized scalar parameter/local reads; `true`, `false`, `()`; accepted integer tokens for I/char16/F, floating tokens for F and character tokens for char16. `T(literal)` and `T(e)` where contextual completion makes e exactly T use existing primitive construction/identity rules. Cross-type conversions are outside this slice. |
| Places | A scalar local or by-value formal; `*p` for an admitted initialization-reference formal p. Parentheses preserve the category. Ordinary by-value formals remain fixed; no new `mut` parameter surface is selected. |
| Eager operators | Every applicable primitive unary `+ - ! ~`, binary arithmetic/bitwise/shift/equality/ordering instance over these sets, with exactly the owning matrix's restrictions. No Boolean ordering, char16 arithmetic, heterogeneous arithmetic or unit ordering. |
| Lazy operators | Primitive-bool `&&`, `||`, and `condition ? a : b`. Both conditional arms must satisfy the ordinary complete result typing even when an arm has no normal completion. |
| Calls/initialization | Existing fully supplied local/imported DirectCalls; matching live init-formal descriptor input by scoped child reborrow, `ref init local`, and canonical `ref init *p` forwarding. Formation is only an argument in this slice, not stored/captured init-capability source authoring. Local labels retain accepted mapping; imported labels/defaults remain separately gated. |
| Local state | `[mut] T name = e;`, `[mut] var name = e;`, `init [mut] T name;`, with T in S. Vacant inferred `init var`, nullable inference and constant-local authoring are outside this slice. `place is init` tests a local/formal binding or the admitted `*p` destination. |
| Updates/results | Assignment to an eligible included place; applicable primitive compound assignments other than `??=`; prefix/postfix `++/--`; `_ = e;`; `name = _;` for an eligible named local; ordinary expression statements only for exact unit or checked no-normal completion. |
| Structured statements | Nested/empty Blocks and `;`; `if`/`else`/`else if`; unlabelled `while`, `do` and ordinary `for`; bare `break`/`continue`; `return;`/`return e;`; nested `unsafe {}` and `unsafe(e)`. For headers use the corresponding admitted LocalBinding/StepExpression forms without internal semicolons. |

Source forms outside this closure are **unsupported by this operation**, not
thereby invalid Terrum. In particular, labels, switch/pattern bindings,
iteration protocols, nullable/coalescing/optional access, aggregate/member/
index places, general reference values, raw pointers, callable-value/native
calls, owning/task/GC values, policy variants, require/throw/try/defer,
source contract clauses and Pure/ConstEval authoring keep their existing
owners/gates. Decline the whole unsupported operation; do not compile a
partial Body or dispatch individual nodes to C#.

All authored forms are parsed, named and typed, including unreachable and
unselected arms. An untyped `var x = 1 + 2` or `1 < 2` still lacks numeric
data evidence. A numeric result context cannot retarget an already typed
operand. Signed-token construction is not ordinary negation of a grouped
operand. An out-of-range literal diagnoses after unique selection, without
trying a wider overload.

### Minimal extension versus register-first SSA

| Approach | Consequence for this decision |
|---|---|
| **Owned expressions/scopes, then Home-first CFG — recommended** | Makes scalar snapshots, mutable storage, branch results and availability witnesses explicit using existing Body operations. Retains one representation for binding, verification, rewriting and source-free consumption; later promotion remains possible. No runtime-performance advantage is claimed. |
| Bind directly into register-first SSA with explicit edge/phi values | Can reduce materialized scalar Homes, but requires complete value/edge/liveness and spill rewriting from the outset, including non-Copy loans and loop generations. It still needs storage identities and the same producer/consumer checks. Existing BlockEntryState can encode phi-equivalent merges, so this alternative also needs no new schema; serialized SSA IDs or a structured-source Body codec are unnecessary. |

Select the first as the initial producer strategy, not a prohibition on proven
promotion or equivalent register allocation. Neither approach may postpone
effectful expression evaluation, omit source scopes or exchange a partial IR.

## Exact owned representation

These are **internal logical records/variants**, not a file format, public
object ABI, new source syntax or numeric tags in the registry. Preserve the
predecessor's immutable Module/type/contract catalogs, LocalCallable and
ImportedCallable identities and exact DirectCall/Invoke payloads.

A source Body owns its root Block, scopes, locals, places, expression and
statement occurrences, child ranges, calls and argument ranges. IDs below
are Body-local except the predecessor's catalog/callable IDs. Locations use
`-1` for absence and remain nonsemantic. Names needed after binding are owned
decoded text; source/token spans do not become semantic backing.

| Record/variant | Required fields or payload |
|---|---|
| `StandaloneSourceScope` | `parentScopeId` (explicit absent root), ordered `localIds`, `unsafeContext`. The latter is a source-verification context, not serialized authorization. |
| `StandaloneSourceLocal` | `scopeId, typeId, bindingKind, ownedName, declarationLocationId`; bindingKind is Fixed, Mutable, Init or InitMutable. |
| `StandaloneSourcePlace` | `typeId, locationId`, plus Local(localId), Formal(parameterOrdinal), or InitReferent(parameterOrdinal). InitReferent retains the formal's complete descriptor and reservation as its backing source, not the descriptor cell. |
| Expression occurrence | `typeId, category, locationId`, plus Literal(exact typed value), Place(placeId), Read(placeId), Initialized(placeId), InitializationView(viewId), Unary(selected operation, operand), Binary(selected operation, left, right), LogicalAnd/LogicalOr(left,right), Conditional(condition,trueArm,falseArm), PrimitiveTarget(typeId,operand), Update(placeId,selected increment/decrement,prefix/postfix), Unsafe(operand), or DirectCall(existing callId). |
| Initialization-view occurrence | `placeId, referenceTypeId, parentReservationId` (explicit absent for a fresh local), `locationId`; usable only by its owning source-use-5 argument. |
| Statement occurrence | `scopeId, locationId`, plus Block(ordered child range), Declare(localId,optional initializer), Assign(placeId,value), CompoundAssign(placeId,selected operation,value), Discard(value), EndLocal(localId), Expression(value), If(condition,trueBlock,optional falseStatement), Loop(loopId,kind,optional initializer/condition/step,bodyBlock), Transfer(break/continue,loopId), Return(optional value), UnsafeBlock(block), or Empty. |

Optional fields have explicit presence, not a stolen valid ID. Ordered
child/argument ranges use checked nonnegative first/count pairs, `(0,0)` when
empty, exact owner bounds and exclusive range ownership. Each occurrence
has one structural parent. Literal **values** may be interned; expression
occurrences are not shared to erase/duplicate reads, calls or failures.
Source children are acyclic. Loop-transfer and callable-target references
are control/declaration edges, not recursively visited expression children.

Place occurrences use the accepted Place category; reads and initialization
descriptors use Value, with their respective copy/loan restrictions. An
InitializationView expression selects the exclusively owned view row through
the existing CallArgument expressionId and sourceUse=5; it does not introduce
another argument-ID namespace. Other value uses of an uncompleted view reject.
Matching init-formal arguments create that same view of the formal's retained
referent/reservation, not a Copy Read of the descriptor binding.
Loop IDs identify exactly one owning Loop statement. While/Do require a
condition and have no initializer/step; For alone admits those optional fields
and an omitted condition. Declare requires an initializer for Fixed/Mutable
and absence for the selected vacant Init/InitMutable declarations.
For has a header scope containing init/condition/step and enclosing its body
scope; While/Do conditions remain in their enclosing block. No extra
while/do header scope is invented.

Inventory parameters and locals across each whole declaration space before
binding occurrences; preserve the accepted source point of declaration.
Resolve each transfer to its enclosing loop identity during binding.
Local/parameter/place identity, not spelling, determines subsequent flow.
All arenas and contracts publish atomically only after the complete selected
operation succeeds.

### Normalized records

Add these records to the predecessor's `StandaloneBodyProgram`; keep its
immutable binding catalog and tagged targets:

| Record | Required content |
|---|---|
| `StandaloneBody` | Real callable/Module owner, `entryBlockId=0`, owned Homes/constants/Invoke inputs/Invokes, Blocks and operations. No bodyless expectation is a Body. |
| `StandaloneBlock` | Ordered operation range, typed incoming general-register bindings, exactly one terminator. |
| `StandaloneTerminator` | Jump(blockId), BooleanBranch(completed selector,trueBlock,falseBlock), Return(completed selector), or NoReturnInvoke(invokeId). A no-return invocation is emitted once as the terminal call, not also as an ordinary operation. |
| Completed selector | Existing None/Register/Home kind and index, complete type and compiler-held value/loan provenance. None requires index zero and represents only genuine unit or an explicitly absent normal result. |
| Place/state plan | Exact source place, local activation/destination generation, supporting formal/loan, applicable authority/state and optional compiler-generated uint32 state-witness Home, 0=vacant and 1=complete under the selected publication protocol. Preserve saved discriminator versions separately. These are compiler facts, not another serialized state table. |

Operations are complete typed constant/read/store/retirement, selected scalar
operation and ordinary Invoke instances, using the existing record semantics
where needed. They are ordered computations, not delayed argument recipes.
No byte substitution of the existing five operation tags constitutes this
extension.

## Binding, flow and normalization obligations

1. **Stages and snapshots.** Bind the uniquely selected callable against its
   expected view. Evaluate eager children left-to-right; a failed or
   no-normal child prevents later children and the enclosing operation.
   Snapshot a scalar read before a later child can change its source; passing
   the original mutable local Home as an allegedly completed value is not
   staging. Reference arguments capture descriptors/destinations, not pointee
   snapshots. Use ordinary typed argument Homes or register/entry bindings
   under the non-Copy descriptor protocol; no CopyRead store may duplicate
   an initialization reservation.
2. **Lazy CFG.** Evaluate the condition/LHS once. Enter only the reached RHS
   or conditional arm. Every normally completing arm writes the agreed
   result to a compiler-generated Home, or supplies an equivalent typed entry
   merge. A terminal arm supplies neither an input nor a fabricated value.
   Do not use eager select/movcc to execute effectful arm expressions.
3. **Storage and authority.** Scalar locals start vacant; initialized
   declarations publish after successful initializer evaluation. Fixed,
   mutable, init and init-mut permissions remain distinct. Select assignment
   places once before the RHS. Compound/update forms capture the old value
   at the prescribed point and commit only after successful computation;
   postfix retains that old result, prefix the new result. An unrelated RHS
   mutation is not rolled back. Home rebindability never grants source
   mutation permission.
4. **Unit storage.** Unit locals retain their type, declaration, availability
   and lifetime identity. Erase only their payload: when state is observable,
   use the same availability witness/CFG operations as below. Unit-producing
   effects execute before publication/use/discard. An unavailable unit read
   still diagnoses; None at a completed call input is not proof that a source
   unit local was live. No `ref<void>`, payload address or Boolean-to-unit
   conversion is introduced.
5. **Joins and repetition.** Run finite structural state analysis to a fixed
   point at loop headers, including initial entry and every reachable
   backedge. Intersect available facts at normal joins while retaining all
   possible dependency/reservation origins. Body-scope reentry creates a
   fresh activation; header locals survive ordinary for iterations. Known
   structural nontermination can eliminate normal fallthrough, but unknown
   conditions do not prove execution, termination or an invariant.
6. **Exact transfer order.** While continues to its condition; do continues
   to its trailing condition; for continues through its step then condition.
   Break executes neither step nor another condition. Enter for initialization
   once; omitted condition is true and omitted init/step does nothing.
   Exit crossed body/header scopes before the selected transfer. Every normal
   Return exits scopes after retaining its result; unit fallthrough becomes
   unit Return. Non-unit normal fallthrough diagnoses.
7. **Initialization.** Normalize/check each local callable's accepted output
   defaults before body flow: Boolean true/complete and false/vacant, otherwise
   unconditional complete on normal return. Imported relations remain their
   original published contracts. `ref init *p` lends the same T destination;
   `ref init p` targets the descriptor cell and is not a forwarding alias.
   Formation requires exact vacancy/backing and no conflicting staged loan.
   `*p` initialization uses only init authority; later replacement needs
   independently retained outer write. Child completion updates the same
   destination before its parent resumes. Stage/Check/Invoke/phase failure
   and nonreturn perform no normal OutputState publication; they cannot
   supply an invented false/vacant outcome.
8. **Actual state versus correlation.** Reuse the already selected
   [state-publication protocol](initialization-state-runtime-publication.md#exact-candidate-protocol).
   Lower observable `is init` and guarded local retirement from the actual
   place witness, initialized/updated by explicit Boolean/Home/CFG code.
   Test its complete uint32 value against 1 to obtain bool; a state-witness
   Home is not a T payload or a source-visible flag/address. Equivalent
   static elimination retains all effects and state obligations.
   Keep local stores, lifetime end, fresh activation and normal call
   publication consistent with that witness. Saved/copied results retain
   only supported versioned correlations. Overwriting a discriminator cannot
   clear a live place; an old result cannot revive an ended/new-generation
   output. Never replay ensures/getters/helpers to classify an outcome.
9. **Effects and failure.** Resolve integer add/subtract/multiply/negate and
   updates to checked Development/Debug or wrapping Release operations before
   publication. Preserve the [independent division/count rules](../specs/language/primitive-operators.md#integer-arithmetic-and-bit-operations)
   and strict F semantics, including signed zero and sign-only NaN handling.
   Validate shift counts in their original I type before successful exact
   uint32 control conversion. Do not fuse a check past a later call.
   Runtime policy 0 admits terminal value-check failure, not checked exception
   object creation/handling; [scalar failure §3](../specs/language/scalar-failures.md#3-creation-and-event-ownership)
   preserves that distinction. Encoded checked operations still publish no
   failed result/write. Do not replace an operand's actual failure with zero,
   false, rollback or a purported mapped exception.
10. **Unsafe and no-return.** Check caller-unsafety at each source occurrence
    in its lexical unsafe context, including cold arms. Retain complete
    expected Effects; no new NoThrow bit or implicit Pure claim is inferred.
    DoesNotReturn retains the declaration's full type and uses terminal
    Invoke with no result or normal successor. Source typing remains required
    even if an earlier operation prevents that call from executing.

A full-tree traversal visits every expression/statement child and call once
as a **static occurrence**, including cold syntax. Normalization emits a
reachable occurrence at its correct static CFG position; loops repeat that
position dynamically. A checked unreachable call may have no emitted Invoke.
Do not require dynamic invocation counts to equal arena counts, or omit
retained import/dependency obligations when code is unreachable.

General G/WP1 dependencies remain complete retained metadata. This slice
does not add execution/observation carriers for richer phase-3 relationships:
state reconstruction admits unconditional completion and the existing total
six bounded result selectors. A call needing unavailable surviving
observations declines affected execution, without converting a general/sparse
map into a default, evaluating its graph as a new predicate pass or removing
uncalled graph closure. That is the existing independent G execution gate,
not a new output policy.

## Standard carrier and source-free consumption

Reuse the [registry](../specs/representation/registry.md#2-instruction-allocation)
and its [machine allocations](../specs/representation/trrm-1.registry.json):

| Normalized requirement | Existing carrier |
|---|---|
| Real multi-block Body | `implementationKind=01`, `instructionEncoding=01`; actual mapMask/counts; one 12-byte Block entry per Block, with disjoint ordered nonempty ranges covering code exactly. |
| Incoming values | `01=BlockEntryState`, complete TypeMap identities and up to 14 sorted unique general bindings. Home-first scalar paths can use an empty state; other supported input uses cannot be assumed empty. |
| Constants, scalar reads/writes | Existing typed ConstantMap/ConstantSource and Home load/store forms; CopyRead only for genuinely Copy-eligible values. Homes are distinct identities, never signature/value-interned. |
| Arithmetic/comparison | C0 checked/wrapping integer and strict floating controls; C1 typed bit/count operations; C2 comparison and C3 Boolean materialization. Use exact registry controls and unused-register fields. Primitive target identity needs no new conversion form. |
| Lazy/structured edges | `06=ConditionalEdge`, payload `u16 trueBlock,u16 falseBlock`; B1/E4 Boolean branch and B0/E3 absolute Block jump. Both successors are explicit. |
| Initialization/retirement | Existing `04=PlaceSelection`, `05=SemanticStore` and the I-owned DD formation flags. Semantic init stores do not require fabricated ordinary write authority. State bookkeeping lowers to ordinary Boolean/CFG/Home operations, not a new Initialized record. |
| Calls and return | Existing `02=CallSite`, 90/E0, MethodMap MethodDef/Import tokens, complete formal-order inputs and legal result selector; signature-aware returns, B8/EB for non-unit Home results. |

Use Home role 3/rebindable only for legal live replacement, including
compiler-generated reusable snapshots/witnesses; receiver/formal flags remain
zero. Source binding permissions remain checked independently. Checked
descriptor Homes retain their complete type and loan rather than T payload.
Unit payload erasure cannot erase state/evaluation code.

Allocate the complete Body's maps, entry/call/place/store/edge records and
Block indexes before final code offsets. Recompute instruction widths and
exact Block/record/Body/section extents with nonwrapping arithmetic. Shared
identical empty entry records are permitted; record capacity is for the
**combined** inventory, not separate maxima for calls and branches.
Canonical writers use first logical use in their deterministic Block/code
order, reuse identical eligible maps/records and choose the shortest
equivalent assigned form. Ordinary noncanonical reads remain allowed under
[canonicality](../specs/representation/encoding-canonicality.md).
Control-flow Bodies use stream kind 01; existing compact descriptors retain
their exact eligibility. Where a compact descriptor is eligible, canonical
writing still selects the smallest complete Body row, with the lower kind
breaking a tie. The consumer must support the actually selected form or
decline the operation, never guess an equivalent decoder.

Map/Home/Block/record indexes remain zero-based u16, including **65535**;
counts use u32 through **65536**, with mandatory positive Block/record counts.
Global rows retain their one-based low-24-bit namespaces. Formal/count,
code-offset, instruction-ordinal and source-coordinate domains are distinct.
The full plan must fit, including witness Homes and edge/entry records.
Incidental current Source-length/depth scratch limits are resource boundaries,
not new grammar or portable-capacity rules.

The Terrum source-free decoder must reconstruct every supported Block,
terminal, record, register/Home value and call in its owning metadata Module,
without source/tokens or C# projection. Before affected use, check:

* exact schema, complete per-Module requirements/bindings and retained closure;
* framing, reserved fields, typed indexes, instruction boundaries and full
  Block coverage, including no target into a byte offset or another Body;
* exact record kind/extent, explicit successors, one terminal per Block and
  DoesNotReturn CallSite terminal/result restrictions;
* supported entry types, register definitions/joins, Home state and the
  place/loan relationships needed for safe reconstruction;
* selected target/expected-contract identity, complete input/result types and
  source-use-5 carrier/publication correspondence.

These are safe-consumption checks, not mandatory re-proof of arbitrary
provider Body facts, source unsafe authorization or general predicate truth.
A same-shaped target substitution can encode a different valid program;
preservation is the producer/rewriter's obligation, not debug-name recovery.
Joint lowering consumes this same owned CFG and retained target plan for
local and imported Bodies. Scalar failure policy and arithmetic controls come
from the admitted request/artifact, never the consuming build configuration.

Keep G's catalog/closure, including 10/1 for bounded selectors, 17/1 with its
10 prerequisite for general/projection uses, and 11/1 where retained scalar
constants require it. Compute each owning/using Module's closure, including
compiler-generated constants and retained cold content. This source extension
does not itself require a new Feature or inherit another Module's list.

## Traversal, remapping and publication

Extend traversal and rewrite dispatch across **all** variants, not just calls.
Visit children in stored source order; visit declarations/targets separately
under the dependency worklist. Remapping requires independent Module,
local/imported callable, expected contract, type, Body, scope, local, place,
expression, statement, loop, Home, Block, record and map correspondences.
Rebuild every owned range and remap transfers, conditional targets, entry
records, selectors and source-occurrence/Invoke maps together.

Preserve activation/destination origins, parent-child loan lineage, saved
result versions and each retained import/equality group. Ordinary rewriting
cannot retag an imported target as local. Packing retains unkeyed graph/helper
closure in original Modules under the [standalone closure rules](standalone-published-predicate-graph-closure.md#cross-artifact-ownership-and-packingremapping-boundary)
and [WP1 remapping](witness-projection-predicate-dependency-codec.md#writers-source-free-copying-and-packing);
it neither invents keys nor turns provider dependencies into source locals.

A source-free rewrite remaps decoded Body references under their distinct
namespaces, not matching octets. Rebuild widths, Block ranges, record indexes,
variable offsets, Module partitions and manifests. Update or deliberately
omit affected optional SourceMap/debug data under its owner; never leave stale
instruction/recipe/source coordinates or use debug data as semantic backing.
No arithmetic policy, source-use tag, literal bits or numeric ordinal is
silently treated as a row reference.

Verify complete source/normalized ownership and changed producer facts before
freezing a replacement. Parse/type/flow failure, unsupported contract or
capacity exhaustion publishes no partial program/artifact and preserves any
previously published output. No fake normal return, dropped loan, default
selector or alternate decoder is a successful fallback.

## Candidate acceptance

The following source examples use **accepted syntax/semantics** and illustrate
the proposed standalone coverage, not current standalone execution support.
No builds, tests or external-process inspection were performed for this
document; the active no-overlap hold remains in force.

```terrum
int32 Accumulate(int32 count, bool stop)
{
    mut int32 total = 0;
    for (mut int32 i = 0; i < count; i += 1)
    {
        if (stop && i == 2)
        {
            break;
        }
        if ((i & 1) != 0)
        {
            continue;
        }
        total += i;
    }
    return stop ? total : total + 1;
}
```

For the finite inputs `(7,false)`, `(7,true)` and `(0,false)`, the required
results are 13, 0 and 1. Continue must execute the for step; break must not.
These outcomes require no assumption about arbitrary-input overflow.

```terrum
bool FillIf(bool choose, ref<init int32> target)
{
    if (choose)
    {
        *target = 17;
        return true;
    }
    return false;
}

int32 Select(bool choose)
{
    init int32 item;
    bool completed = FillIf(choose, ref init item);
    if (!completed)
    {
        item = 23;
    }
    return item;
}
```

The accepted Boolean output default supplies true/live and false/vacant;
the proposal adds no implicit unknown-output mode or authored ensure.
Select must produce 17/23, with the same results when the fallback condition
uses `!(item is init)` under the selected witness protocol.

Required gates, **not executed results**:

| Area | Positive | Negative/boundary |
|---|---|---|
| Parsing/typing | Nested precedence/grouping; signed minima and based literals; char16 equality/order; strict float32/64 operations; unit arguments and equality evaluate all producers. | No default width, numeric promotion or literal-fit overload choice; wrong call attachment/operator spacing; char arithmetic/bool ordering; out-of-range literal after unique selection. Preserve floating signed zero/NaN and same-type intermediate precision. |
| Declarations/state | Both if arms initialize a fixed `init T`; sibling locals reuse names; mutable and maybe-live init-mut replacements with guarded prior retirement; fresh body locals each iteration; `init void token; token = ();` and state/early-end behavior with no payload. | Before-point/self-initializer read, nested/parameter collision, bare `T name;`, fixed-live overwrite, maybe-live read or fixed-binding initialization, unit read while vacant, reassigning an ended ordinary fixed binding. |
| Evaluation | Unrelated synthetic local/imported calls nested in unary/binary/conditional arguments; written label order differs from formal order. Record reads/calls/checks once at their reached positions. | `false && RHS`/`true || RHS` skip a failing/effectful RHS; eager `&` does not. A checked product before a later call fails before that call. A staged earlier scalar is not reread after a later update. |
| Loops/joins | Zero and several iterations; nested while/do/for with bare transfers; do body once; omitted for fields; conditional break/return; fixed-point vacancy and fresh body activation. | Continue reaches the correct condition/step, never an inner or skipped step; no for-header local after exit; no assumption a while body ran; no normal-return value fabricated for a reachable non-unit fallthrough. |
| Results/terminal flow | Unit return/fallthrough and explicit discard; both if arms return; structural infinite loop with no normal exit; terminal imported DoesNotReturn with its exact result type. | Bare non-unit call/update/for-step diagnoses; non-unit expression cannot return from void; mismatched no-return arm still type-checks and rejects; no successor/result/extra return after terminal call. Cold unsafe/type errors still diagnose. |
| Init identity/publication | Fresh local and `ref init *p` forwarding through unrelated helpers; explicit outer-write replacement; saved/copied bounded discriminator; result discard still publishes actual state. | Descriptor-cell forwarding, nonvacant/overlapping reservations, later-argument invalidation, second initialization, lost parent loan, false/live under the default, incomplete unconditional output. An old status cannot revive a new generation; sparse/general maps cannot become default pairs. |
| Failure/effects | Development/Debug checked versus Release wrapping; signed min division by -1; min remainder -1 is zero; invalid original-width shift counts; branch-local failure prevents later publication. | No early setter/write, false status, rollback or fabricated exception. uint64 oversized counts must not wrap to valid uint32; negative counts must not first fail a numeric conversion. Pure/NoThrow/caller safety cannot follow from scalar shape or skipped execution. |
| Source-free ownership | Write/read/rewrite a multi-block local/imported program without source/debug, with two independent keyed views, an original shared expectation group and origin-Module G/WP1 helpers. | Wrong Module/Body/target namespace, merging views by current actual target, lost graph closure or C# partial-semantic handoff is not acceptance. Independently permute artifact/Module/type/Home/Block/record rows and rebuild all links/extents. |
| Encoding/capacity | Zero/65535 indexes and 65536 counts where the complete inventory permits; branch targets 255/256, edge indexes 15/16, call records 255/256 and short/long Home forms. Accept otherwise-valid noncanonical widths/maps. | One-past counts, combined entry+edge+call-record overflow, count/extent overflow, wrong terminal/record kind, overlapping/gapped/truncated code, R14 writable or R15 misused as an incoming value. Capacity includes generated witness/snapshot storage; failure preserves old output. |

Use the existing public standalone API/integration test area and unrelated
synthetic providers, not sample names or C# bound-object injection. Acceptance
requires source binding/verification, shared traversal/remapping, complete
writer/source-free consumer, and actual supported headless lowering/execution
against the final relevant inputs. Differential inspection of a complete
standard artifact is ordinary interchange, not a production intermediate
handoff. Emission, parsing, fixtures or a document alone establish none of
those later gates.

## Remaining decisions and independent gates

The source closure, internal ownership model and wire reuse above leave
**no additional grammar, allocation or compatibility policy undecided within
this bounded recommendation**. Adoption remains pending for this document
and its I/G/WP1/publication companions; the authorized workflow permits their
provisional use, not an acceptance claim.

Checked-exception class/factory/event/phase/cause/transport support and
ExceptionRegion/handler/finally inventories remain independently incomplete.
The [scalar failure physical boundary](../specs/language/scalar-failures.md#5-publication-and-physical-completion)
and [region allocation boundary](../specs/representation/registry.md#2-instruction-allocation)
therefore prevent extending this recommendation to checked-exception runtime
policy or require/throw/try/defer handling without further exact decisions.
Recognized RegionExit shapes are not usable codecs. Richer G observation/
enforcement execution, source declaration surfaces and excluded source forms
likewise keep their owning prerequisites; none is repaired by CFG support.

Implementing this coherent slice is continuation of the conformance work,
not completion of a milestone or the whole compiler. Its remaining support
and acceptance gates must be reconciled in their owning inventories when
implementation actually changes them; this proposal changes no inventory,
plan or work queue and does not lift the hold.
