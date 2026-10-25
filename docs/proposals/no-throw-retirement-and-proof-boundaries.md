# NoThrow retirement and no-failure proof boundaries

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This recommendation may guide implementation
provisionally, but does not amend accepted owners, approve a source or wire
contract, allocate an ID, or establish compiler support.

## Decision and scope

Recommend retiring the **general compiler-recognized source `NoThrow`
declaration and independently published NoThrow promise**, not relocating
the retired Effects bit. Keep sound, scoped internal no-failure analysis and
every independently required role-specific failure/retirement contract.

This is an explicit source-contract decision, not a consequence already
implied by removing one metadata bit. It intentionally removes a modular
ordinary-call guarantee supported by the current implementation. It does not
remove runtime checks, permit new failures in another retained contract, or
make cleanup universally nonthrowing.

The recommendation needs no replacement carrier. A separately reviewed
ordinary-form failure-bound carrier could restore that capability later;
this document neither selects that extension nor leaves implementations free
to invent one. Source recompilation and regeneration of affected private
artifacts/native consumers are the migration boundary.

## Settled baseline and genuine missing choice

| Accepted owner | Established rule and limit |
|---|---|
| [Effects](../specs/representation/contract-clauses.md#3-effects) and the [machine registry](../specs/representation/trrm-1.registry.json) | “The only assigned bits are `02` DoesNotReturn and `04` caller-unsafe”; the retired `01` NoThrow bit rejects. This already settles standard byte handling. It does not expressly retire a source annotation or define its replacement. |
| [Logical effect records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | The bounded Effects clause “carries no no-failure promise.” Ordinary and selected pure forms have distinct logical activity bounds, but no general physical NoThrow carrier is supplied. |
| [Attribute grammar and schemas](../specs/grammar-spec.md#31-source-file-structure-and-declarations) | Bracketed attribute syntax does not itself register a semantic schema. Resolved identities, applicable targets, multiplicity and primary contract authorship are required. There is no corresponding complete general NoThrow schema here. |
| [Primitive arithmetic](../specs/language/primitive-operators.md#integer-arithmetic-and-bit-operations), [invocation phases](../specs/language-contracts.md#31-the-normal-sequence), and [IL failure semantics](../specs/il-instruction-set.md#explicit-failfast-and-unreachable) | Still refer to NoThrow checking, declared no-throw bounds or “declared NoThrow guarantees.” These references prevent treating source retirement as an already explicit, complete accepted decision. They do not specify a new carrier or authorize dropping a published promise. |
| [Complete-value Cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement), [lifetime cleanup](../specs/lifetimes.md#owner-transfer-and-cleanup-contracts), and [native retirement entries](../specs/targets/windows-x64-call-interface.md#6-complete-owners-and-retirement-entries) | Compiler-invoked Cleanup runs on normal retirement, is nonsuspending, and may throw. If it throws, remaining generated owner/field cleanup does not resume during that unwind. This does not require a general NoThrow annotation. |
| [Cleanup clause](../specs/representation/contract-clauses.md#7-cleanup) | Separately selects an exact MethodDef through a TypeDef-owned clause. Its shape is not proof of Unique, callable role, ownership or complete execution support, and supplies no general no-failure promise. |
| [Pure forms](../specs/language/published-predicates-and-effects.md#pure-means-complete-input-state) | Exclude escaping language failures on the valid-input selected pure form. Ordinary execution may differ. Pure is not an alias for an ordinary impure-but-nonthrowing callable contract. |

The unresolved choice is whether to retain and represent a general source/API
NoThrow guarantee, restrict it to some explicitly defined non-interchange
facility, or retire it. The implementation's Boolean, current tests and private
encodings cannot choose among these.

Some cleanup summaries are additionally stale: the
[IL store discussion](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement)
says authored Cleanup is checked NoThrow, and
[OREG-06](../specs/conformance/source-ownership-and-cleanup.md#registered-cleanup-and-complete-types)
rejects throwing Cleanup. Those contradict the canonical complete-value owner
above. Correcting those summaries to that owner is a conformance correction,
not permission to choose a different cleanup policy in this proposal.

## Proposed source and proof rules

1. **No general declaration promise.** Remove NoThrow from the
   compiler-recognized callable/accessor/constructor and interface/operator
   requirement schemas. A use requesting that retired semantic contract
   diagnoses; do not silently accept it as a no-op or translate it to Pure,
   DoesNotReturn, Cleanup, caller-unsafety or a native attribute. Ordinary
   user-defined attributes remain governed by their own resolved identities
   and acquire no compiler privilege from this spelling.
2. **Operation semantics remain exact.** Checked arithmetic, conversions,
   indexing, requirements, postconditions, initialization triggers, allocation,
   calls and cleanup retain their actual failure behavior and order. A
   fail-fast runtime policy does not prove that an operation has no
   language-failure effect. Executed `unreachable` remains a language failure.
   DoesNotReturn removes only normal completion, not failure edges.
3. **Internal facts are evidence, not declarations.** An implementation may
   prove that an exact operation or closed region cannot produce or escape a
   modeled language failure. Keep the distinction between an operation that
   cannot fail and a region that contains its failures. Neither proves
   termination, purity, nonsuspension, absence of native faults or any unrelated
   authority. Include the selected arithmetic policy, input/path facts,
   contracts, initialization, cleanup, target/runtime and binding dependencies
   actually needed by the proof.
4. **Caller validity is not body inference.** A local proof cannot authorize an
   independently compiled caller, satisfy an unrepresented general interface
   promise, or remove a caller obligation. A closed optimization may use body
   evidence only while preserving observable behavior and retaining or
   invalidating every dependency needed to keep that evidence true.
5. **No optimistic absence default.** An ordinary call with no applicable
   published failure bound is potentially failing for analysis. Neither a
   missing Effects row, an explicit zero mask, a unit result, a constant current
   body nor a native-callable role grants a general NoThrow guarantee. A
   supported role-specific contract or sound closed proof can establish only
   its actual narrower conclusion.

An implementation can reuse its proof machinery rather than delete useful
analysis. It must separate declaration promises from analysis contexts; a
publicly constructible Boolean or a decoded private field is not a universal
certificate. Removing the old property must not accidentally remove the
failure-edge, initialization, ownership or unsafe checks currently coupled to
it.

### Cleanup is a separate contract

Preserve the selected Cleanup operation, complete readonly receiver,
body-then-reverse-declaration field retirement, partial-state distinctions,
nonsuspension and exact dependencies. Normal Cleanup may throw under its
accepted propagation rule; it need not carry NoThrow. Failed unpublished
construction keeps its distinct reverse-acquisition rule without invoking the
incomplete parent's Cleanup. Exceptional cleanup is authored in crossed
`finally` blocks, with its actual failure/replacement/chaining semantics.
Do not force every authored `finally` or `defer` through a blanket NoThrow
context.

This does not weaken specifically required nonthrowing/nonsuspending programs:
for example [CapturedException retirement](../specs/language/captured-exceptions.md#6-handler-completion-retirement-phases-and-effects),
[Task residual destruction](../specs/language/task-responsibility.md#every-exit-settlement-and-ordinary-destruction),
or [failure-domain release](../specs/runtime/foreign-failure-bridge.md#3-one-responsibility-and-valid-retained-state).
Establish the complete transitive obligation for that exact role, not from a
Cleanup name or the retired bit. If its required program/effect/ownership
records or target support are unavailable, reject affected use rather than
erase the obligation. This proposal assigns none of those missing codecs.

A backend may support only a locally proved nonthrowing cleanup subset.
Report that as an implementation limitation, not a source eligibility rule.
Do not admit arbitrary throwing cleanup merely by removing the old guard
unless its actual propagation and remaining-ownership behavior is supported.

## Separate compilation, replacement and persistence

New standard producers emit only the assigned Effects meanings, with canonical
zero omission; readers admit the equivalent explicit zero and reject every
mask containing the retired bit. Cleanup does not require that bit on its
target. An unfinished producer receiving a bound declaration that still
requires a general published NoThrow promise must reject that standard
emission, not mask the bit away and claim to preserve the declaration.

Under this recommendation, newly compiled ordinary declarations/import
expectations do not publish that retired promise. Apply
[complete directed satisfaction](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions)
to all remaining contracts. Preserve independent failure observations,
selected pure-form support, cleanup, output states, phases, caller-unsafety
and no-return guarantees; removing the general promise does not remove those.
No change to a bit mask may bypass comparison of the complete retained graph.

An old consumer that retained NoThrow, or whose machine code relied on it,
cannot be serviced by silently weakening that expectation. Recompile/reprove
and regenerate that consumer against the new contract boundary, including
affected transitive consumers. An exact old implementation-specific agreement
can instead retain its old obligations; it is not current standard admission.
For a new closed optimization relying on a provider's body, either preserve
the exact dependency/proof during replacement or invalidate and regenerate
the affected code. Equal signatures or a currently nonthrowing replacement
body are not an independently retained published guarantee.

[Private persistence](../specs/representation/persistence.md) may encode local
analysis state under its explicitly selected implementation schema. Development
`.trm`, native `.trmn`, sets and graphs are not standard carriers for a missing
`.trrm` promise. A private executable path retaining old NoThrow contracts
remains a distinct old/private semantic facility until migrated; a
`developmentProfile` switch does not make those meanings standard.
An independently interpreted embedded `.trrm` payload still follows its own
exact schema.

Follow [draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts):
unchanged `(1, 0)` bytes do not select an old schema or authorize a fallback
decoder. No alias, historical-bit reinterpretation, optional Attr/SourceMap
carrier or guessed conversion is proposed. A meaning-preserving transformation
cannot simply clear the old bit: reconstruct the selected declarations,
expectations and dependent proofs, remap actual references/maps and regenerate
affected code as required. There is no obligation to preserve unpublished
private formats or draft readers.

## Native, execution and trust boundaries

NativeCallable, NativeExport and native function-pointer types retain their
own ABI, caller-unsafe, failure-containment and code-lifetime contracts. They
are not general NoThrow declarations. A backend that currently requires an
annotated nonthrowing body must replace that dependency with a sound proof of
its supported boundary subset or a supported explicitly authored containment
path, otherwise reject affected use. Do not silently add catch/translation
thunks, suppress failures, or alter argument/result carriers.

Preserve the [native exception boundary](../specs/targets/windows-x64-deployment.md#2-direct-calls-and-exception-boundaries):
supported native exceptions may unwind inward under the separately admitted
capability; Terrum failures do not unwind outward through native frames.
Contained bridge raw entries and release programs retain their exact special
obligations. Unsafe authorization proves neither absence of failures nor
containment. A no-failure body proof also does not waive platform stack-walk
and unwind metadata required by the actual emitted frame.

[Producer truth and consumer checking](../specs/representation/reader-trust.md)
remain separate. Source compilation/public bound-IR producers establish every
fact they publish or use to justify generated code. Consumers validate exact
schema, selected shapes/references, support and binding before use; they need
not semantically re-prove producer body facts. Optional audits can examine
failure analysis within their stated scope. External provider behavior and
authored unsafe promises remain trust contracts, not facts proved by structural
checking. Neither audit availability nor retirement of the NoThrow bit weakens
mandatory structural checks or permits execution of an unsupported required
cleanup/failure contract.

## Integration owners and implementation surfaces

These are proposed follow-up edits, not changes made by this document.

| Owner | Required integration |
|---|---|
| [Grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations) and [source effects](../specs/language/published-predicates-and-effects.md) | State explicit retirement of the general NoThrow schema; keep bracketed syntax, resolved identity rules and selected pure/constant forms. Do not register a replacement spelling. |
| [Primitive operators](../specs/language/primitive-operators.md#integer-arithmetic-and-bit-operations), [invocation phases](../specs/language-contracts.md#31-the-normal-sequence), and [IL failure/store text](../specs/il-instruction-set.md) | Replace general declared-NoThrow references with operation/region failure analysis or the exact role-specific bound. Correct the blanket authored-Cleanup NoThrow sentence to the canonical normal-retirement rule. Preserve checks, policies and observable failures. |
| [Cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement), [lifetimes](../specs/lifetimes.md#owner-transfer-and-cleanup-contracts), and their [conformance cases](../specs/conformance/source-ownership-and-cleanup.md) | Preserve the existing canonical contract. Reconcile OREG-06 and generic cleanup summaries: nonsuspension is required; throwing normal Cleanup follows ordinary propagation, whereas specifically nonthrowing residual/release roles retain their separate requirement. |
| [Contract clauses](../specs/representation/contract-clauses.md), [registry](../specs/representation/registry.md), and [logical records](../specs/representation/logical-records.md) | Keep the retired-bit rejection, assigned Cleanup shape and absence semantics. Clarify that no general NoThrow carrier is being substituted. Required role/effect programs remain independently gated. No assignment changes. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [persistence](../specs/representation/persistence.md), and [target deployment](../specs/targets/windows-x64-deployment.md) | Apply retirement only to newly compiled contracts; retain all other expected facts and reject silent weakening of old consumers. Require proof/dependency invalidation and regeneration where applicable. |
| [Support inventory](../implementation/native.md#checked-no-throw-declarations), [module inventory](../implementation/modules.md), samples and proposal cross-references | Describe actual migrated support and limitations when implementation changes; do not retain documentation claims that the retired source promise is accepted or supported through standard Effects. |

| Implementation surface examined | Consequence under this candidate |
|---|---|
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`Binder.StaticProtocols.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs), and [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | Retire attribute recognition, generic/operator requirement promises and declared `IsNoThrow`; preserve ordinary binding and separate internal proof contexts. Remove NoThrow as source eligibility for Cleanup/native-callable declarations without bypassing their actual support gates. |
| [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) and [`TrRmScalarGraph.cs`](../../sources/Terrum.CodeAnalysis/Semantics/TrRmScalarGraph.cs) | Separate declaration checks from operation/region proof and role-specific restrictions. Reconsider call failure edges, generated and authored cleanup, constructor adapters and graph reconstruction together; do not default missing facts to proven. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) and [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Migrate private Boolean storage, `NoThrow`/`MayThrow` contract suffixes, directed comparison and recorded import promises coherently. Native packages/sets/graphs retaining those contracts require the same explicit migration boundary. |
| [`TrRmContractTableReader.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs), [`TrRmContractTableWriter.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableWriter.cs), and [`TrRmExecutableCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) | Keep standard mask/cleanup rules distinct from explicitly private development interpretation. Migrate executable effect construction/reconstruction and Body/indexed failure checks; fixing the table reader alone does not migrate the executable producer or establish source retirement. |
| [`NativeFunctionPointerContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs), [`NativeEmitter.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs), and [`ScalarFrontendHost.cs`](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.cs) | Replace declaration-Boolean dependencies with the actual supported boundary/proof requirements. Carry the distinction through source projections, verification entry points, lowering and persisted graph inputs. |
| [`NoThrowTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NoThrowTests.cs), [`NoThrowModuleTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NoThrowModuleTests.cs), [`TrRmContractTableReaderTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmContractTableReaderTests.cs), and [`ContractTableValidation.terrum`](../../tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/ContractTableValidation.terrum) | Current tests/projections exercise declaration acceptance, proof, replacement and old bit/cleanup coupling. Replace the affected expectations with retirement, exact-schema and preserved-proof cases; retain unrelated arithmetic, ownership and native regressions. These are implementation evidence, not authority or reports of a test run. |

## Alternatives and acceptance cases

**Minimal coherent retirement, recommended.** Remove the general source/export
promise and its private contract machinery while preserving useful analysis
and independently owned guarantees. This loses author-published ordinary
impure-but-nonthrowing APIs and modular proofs based solely on that promise,
but fits the currently assigned standard schema without new allocations.
Merely clearing a wire bit while keeping those promises would not.

**Replacement carrier.** Retain a checked source NoThrow contract and normalize
it to an ordinary-form language-failure bound in the shared effect-record
design. This would preserve modular ordinary-call guarantees independently of
Pure, but needs explicit applicability, invocation/phase and failure-domain
semantics, complete mandatory physical records, references, admission and
directed replacement rules before independent use. The current Effects mask,
Cleanup row, optional attributes and private Boolean cannot serve as that
carrier. No new clause/Feature number or fallback encoding is selected here.

Keeping the source annotation only as a compilation-local assertion is a
third possible contract, not an automatic compromise: its scope and rejection
at every export/import/optimization boundary would need explicit rules and
would change the current declaration promise. It is not recommended here.

The following are proposed acceptance requirements, not executed evidence:

| Case | Required outcome |
|---|---|
| Retired source annotation on a function, constructor, getter or generic/operator requirement | Diagnose the retired semantic contract, including primary/secondary and duplicate placements; do not silently translate or ignore it. |
| Standard Effects absent or explicitly zero; masks `02`, `04`, `06` | Preserve assigned meaning and canonical omission. Every combination containing retired bit `01`, including with no-return/unsafe, rejects before affected use. |
| Structurally admitted Cleanup without a NoThrow Effects bit | No missing-bit error. Complete semantic/executable use still requires the separate supported role, ownership and retirement facts. |
| Normal Cleanup throws after some work | Propagate its actual failure; no generated remaining-field cleanup resumes during that unwind, no fabricated retirement success or replay. Suspension remains ineligible. |
| Captured owner, Task residual destruction or failure-domain release may throw/suspend | Reject the violated role-specific contract, even though no general NoThrow annotation exists. |
| Authored `finally` throws during existing failure propagation | Preserve its ordinary replacement/chaining and ownership semantics; do not reject solely because every cleanup was forced through a NoThrow declaration context. |
| Checked versus wrapping arithmetic, failed requirement, static trigger or executed `unreachable` under either runtime policy | Preserve the exact selected failure semantics. No profile-wide no-failure inference or failure-edge deletion from bit absence. |
| Pure/ConstEval selection versus ordinary invocation | Preserve the selected form/domain and its actual bounds; never treat the attribute as an unconditional ordinary-form NoThrow replacement. |
| New import without a supported failure bound; provider currently has a constant body | Treat the call conservatively unless a sound closed proof applies. Do not create a cross-module published guarantee by body inference. |
| Old semantic/native consumer retains NoThrow; replacement drops it | No silent contract normalization or unchanged-image servicing. Retain the selected old obligations or recompile/reprove and regenerate affected consumers. |
| Closed optimization removes a failure edge, then provider/binding/target/proof inputs change | Preserve all relied-on facts or invalidate and regenerate affected code before use; signature equality alone is insufficient. |
| Native-callable entry can let a Terrum failure escape outward | Prove supported containment/no-escape behavior, use an explicitly supported authored adapter, or reject. Unsafe, a raw address or a retired Boolean cannot authorize the crossing. |
| Private old payload or equal-version earlier TRRM draft is supplied as current standard input | Reject the unsupported/invalid selected interpretation; do not probe layouts, restore the bit, relabel the artifact or fall back to a private decoder. |
| Producer/public IR contains a stale general NoThrow promise but the selected standard schema has no carrier | Refuse publication until the source/contract migration is explicit; do not silently emit a weaker artifact. |

No required choice remains open within this recommended current-schema
retirement. Adopting a future general failure-bound carrier would be a new
reviewed decision. Approval and implementation/qualification remain separate:
this proposal changes only this document and its index entry.
