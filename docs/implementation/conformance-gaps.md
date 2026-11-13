# Compiler conformance overview

This is a navigation and status overview, not a second specification or a
test log. The [accepted specifications](../specs/README.md) define contracts;
the [implementation summary](../implementation.md) describes supported
profiles. The session SQL todos carry detailed work, dependencies and evidence.

**Evidence boundary:** per-run results and compiler/configuration/input identity
belong in `artifacts\log`, `artifacts\tst`, or the owning qualification
artifact. A scoped pass does not establish global conformance; `acceptance-reconcile`
remains open.

**Status:** *Ready* means accepted work has no known design blocker;
*In progress* means implementation remains; *Bounded* means only a selected
profile is qualified; *Blocked* means a named contract or prerequisite is
missing; *Provisional* means the design remains unapproved.

| ID | Area / contract | Status | Prerequisites | Acceptance gate |
|---|---|---|---|---|
| CG-001 | [TRRM producers and consumers](../specs/representation/README.md) | In progress | Exact schema and feature assignments | Complete assigned producer/consumer routes, structural and binding validation, replacement checks and selected-target evidence. |
| CG-002 | [References, dependencies and output state](../specs/representation/binding-and-replacement.md) | In progress | CG-001 | Preserve exact origin and consumer expectation through binding, remapping, replacement, pruning and re-encoding, including pointer-cell initialization without a pointee-state transition. |
| CG-003 | [Failure, cleanup and publication](../specs/language/ownership-and-cleanup.md) | Blocked / in progress | CG-002; selected exception Body representation; [Core failure selection](../proposals/core-contract-failure-identity-selection.md), [CLI transport](../proposals/core-contract-failure-cli-selection.md), and [Windows x64 object transport](../proposals/windows-x64-local-exception-object-transport.md) | Carry exact failure identities, outcomes, cleanup and conditional publication through each selected source, Body and runtime route. |
| CG-004 | [Native declarations and Bodies](../specs/native.md) | In progress | CG-001–003 | Preserve effects, ABI, ownership and output facts from source declaration through target execution. |
| CG-005 | [Layout and Windows x64 ABI](../specs/data-layout-and-abi.md) | Ready | Selected target; CG-001/004 | Verify selected layouts and calls across metadata, source binding, emission and supervised native execution. |
| CG-006 | [Generic identity and witnesses](../specs/language/types-and-normalization.md) | In progress | CG-001–003; coordinated identity allocations | Preserve exact nominal arguments, including imported and source-generic constant domains, nested typed constant actuals, open interface constraints, source `StructDecl` arguments, closed owner constants, constraints, witnesses and member selection through proof, persistence and lowering. G1 version-3 source publication now covers only its bounded interface declaration subset; role-2 evidence, source-free binding and witness persistence/lowering remain open. |
| CG-007 | [Construction and constant programs](../specs/language/construction-handlers.md) | In progress | CG-002/003/006/010; reviewed codecs | Preserve construction state, ownership, failure cleanup and typed constant values across admitted routes. |
| CG-008 | [Static initialization](../specs/language/initialization-and-flow.md) | Provisional / in progress | Reviewed storage and group records | Carry initializer identity, ordering, trigger and publication through binding, providers and native lifecycle. |
| CG-009 | [Owning fields and retirement](../specs/language/ownership-and-cleanup.md) | Bounded | CG-002/003 | Complete field-place authority, partial moves and exact retirement on normal and failure exits. |
| CG-010 | [Portable proof and ConstEval](../specs/language/proof-and-constant-evaluation.md) | In progress | Accepted source rules and portable codecs | Verify exact proof inputs, deterministic values and resource limits in independent consumers. |
| CG-011 | [Numeric and library contracts](../specs/language/numeric-capabilities.md) | Ready; candidates separate | Registered identities and witnesses | Implement accepted operations and exact failure/rounding behavior; keep candidate APIs unapproved. |
| CG-012 | [Quiet arithmetic NaN](../specs/language/floating-environment.md) | Bounded | Existing floating paths | Qualify result-class and status observations without selecting fixed runtime payload bits. |
| CG-013 | [Non-unit DoesNotReturn](../specs/language/published-predicates-and-effects.md) | In progress | Accepted clause applicability | Carry source, bound, verification and persistence facts without imposing unit-only behavior. |
| CG-014 | [Signed-minimum remainder](../specs/language/primitive-operators.md) | Ready | Accepted arithmetic contract | Align the bootstrap/source result and rejection cases with the signed-minimum rule. |
| CG-015 | [Scalar frontend records](../specs/grammar-spec.md) | In progress | Int32/nint records; standalone fallback | Complete source-sized record handling and remove projected-record dependence. |
| CG-016 | [Standalone compiler transition](../implementation/terrum-code-analysis.md) | In progress | Selected envelope and invocation | Keep the Terrum compiler independent, consume original source, and qualify the selected fallback boundary. |
| CG-017 | [Token spans and returned views](../specs/language/dependencies-and-views.md) | Bounded / consumer-dependent | CG-002; an identified consumer for persistence | Keep transient spans compilation-local; add persistence only for a concrete admitted consumer. |
| CG-018 | [Source grants and native promotion](../specs/representation/persistence.md) | Prototype only | Private JIT source profiles | Do not infer a native source ABI or promote the prototype without an explicit design. |
| CG-020 | [CAS and concurrent execution](../specs/runtime/concurrent-executions.md) | Blocked: representation | Selected APIs and physical operations | Assign exact source and physical operations before adding execution support. |
| CG-021 | [Tasks, iteration and producers](../specs/language/iteration.md) | Logical rules ready; physical routes incomplete | CG-002/003/006/009 | Preserve creation, completion, phase, ownership and cleanup across selected codecs and runtimes. |
| CG-022 | [Roots, handoff and failure boundaries](../specs/lifetimes.md) | Bounded | Complete selected runtime/target profiles | Qualify lifetime, code origin, publication and failure containment on each selected target. |
| CG-023 | [CLI and source input](../specs/cli-protocol.md) | Ready | Accepted protocol and input identity | Verify exact source/program identity, coordinates, output preservation and supervised status. |
| CG-024 | [Representation and resource accounting](../specs/representation/registry.md) | In progress | Exact assignments and calibrated recipes | Validate assigned shapes, bounds, resource charges and failure atomicity at producer and consumer. |
| CG-025 | [Generic native qualification](../specs/native.md) | Bounded | Final compiler inputs and active cases | Re-run the selected generic native matrix against final inputs and runtime policies. |
| CG-026 | [Source grammar and binding](../specs/grammar-spec.md) | Ready | CG-023 source identity | Close accepted declaration, lookup, import, projection and generic member-call postfix routes through public compilation APIs. |
| CG-027 | [Normal/GC backing, roots and pinning](../specs/lifetimes.md) | In progress | CG-001/002/003/004/006/009/022/026; selected GC contract | Preserve backing, roots, pin reborrows and cleanup through one selected GC/runtime boundary. |
| CG-028 | [Nominal enums and persistence](../specs/type-families.md) | Provisional / in progress | CG-001/004/026; exact carrier approval | Keep candidate identity/transport separate from accepted schemas; complete only explicitly authorized routes. |
| CG-029 | [Published predicates and pure evaluation](../specs/language/published-predicates-and-effects.md) | Provisional / in progress | CG-002/006/010; [schema identity](../proposals/core-pure-schema-identity-selection.md), [CLI selection](../proposals/core-evaluation-forms-cli-selection.md), [callable contract composition](../proposals/pure-callable-contract-composition.md), and [mode-query identity](../proposals/core-evaluation-mode-query-identity-selection.md) | Retain exact per-Module identities, read/effect bounds, outputs and dependencies; decline routes without supported declarations and context codecs. |

## Approval and closure

Pre-v1 specifications remain amendable, but proposals do not change accepted
contracts by themselves. Provisional implementation must be explicitly
authorized and remain labeled provisional.

| Direction | Approval | Boundary |
|---|---|---|
| Core Count witness and declaration origins ([witness](../proposals/core-countable-witness-selection.md), [origins](../proposals/core-countable-declaration-origin.md)) | Unapproved; provisional work authorized | Exact per-Module witness selection; no Count-specific codec or fallback. |
| Core contract-failure CLI transport ([proposal](../proposals/core-contract-failure-cli-selection.md)) | Unapproved; provisional work authorized | Transport explicit per-Module failure identities; no default providers or `terrum.cli/1` extension. |
| Core Pure/ConstEval schema identity ([selection](../proposals/core-pure-schema-identity-selection.md), [CLI manifest](../proposals/core-evaluation-forms-cli-selection.md)) | Unapproved; provisional work authorized | Explicit host selection and retained local/external context; no Core default, spelling fallback or new syntax. |
| Pure callable contract composition ([proposal](../proposals/pure-callable-contract-composition.md)) | Unapproved; provisional work authorized | MethodDef-owned composition through existing typed records; candidate `0x0051` and Feature 17 remain unapproved, with no new allocation or implied private codec. |
| Evaluation-mode query identity ([proposal](../proposals/core-evaluation-mode-query-identity-selection.md)) | Unapproved; provisional work authorized | Exact RuntimeHelpers owner/member selection and immutable recipes; no arbitrary property recognition or Core default. |
| [Standard LayoutQueries reference composition](../proposals/standard-trrm-layout-queries-reference-composition.md) | Unapproved; implementation remains provisional | Compose exact selected Modules/field keys; no schema or value-access expansion. |
| Standard enum source identity and CLI ([identity](../proposals/standard-trrm-source-enum-identity-inputs.md), [transport](../proposals/standard-trrm-source-enum-cli-transport.md)) | Unapproved; provisional work authorized | Explicit keys and exact schema only; no inferred keys or compatibility alias. |
| [Private nominal identity CLI transport](../proposals/private-module-cli-identity-inputs.md) | Unapproved; provisional work authorized | Strict file-only manifest and public selector API for all four explicit inputs; producer Core imports require a selected checked registration, consumer imports the positional producer; no `.trm` wire change or inferred keys. |
| Standard GenericCall ([proposal](../proposals/standard-trrm-generic-method-call-body-encoding.md)) and private G107 ([proposal](../proposals/private-generic-method-persistence.md)) | Unapproved; provisional work authorized | Keep the formats distinct; neither changes accepted generic syntax. |
| G1 interface declaration export inputs ([proposal](../proposals/standard-trrm-g1-interface-declaration-exports.md)) | Unapproved; provisional work authorized | G1-only version-3 source mapping now publishes the bounded InterfaceDef and requirement MethodDef subset; no new wire identity, Feature or interface ABI. Role-2 producer/consumer support and closed-witness persistence remain separate work. |
| Standard register-Body initialization-destination inputs ([proposal](../proposals/standard-trrm-initialization-destination-callsite.md)) | Unapproved; provisional work authorized | Exact out-of-band profile `trrm/1-draft-init-reference-bodies-1`; bounded scalar `ref<init T>` store/call and three-block Boolean true/live, false/vacant provider/caller paths. Feature `10/1` default Boolean OutputState rows are source-produced, provider-bound, and preserved by source-free re-emission; same-module FailFast native-library execution passes. The separate `GeneralPredicateGraphsDraft` consumer structurally admits Feature `17/1` phase-3 selectors only for checked init destinations. Source ensure projection/emission/verification, Span, hidden preparation, general CFG joins, dependency graphs, provider-linked execution, CheckedExceptions and failure cleanup remain open. |
| Windows x64 local exception-object transport ([proposal](../proposals/windows-x64-local-exception-object-transport.md)) | Unapproved; provisional work authorized | Private Terrum object-event path, distinct from scalar reason records and foreign `NativeExceptionUnwind`; no Standard TRRM encoding. |

Other proposals remain unapproved unless their owner and approval status say
otherwise; see the [proposal index](../proposals/README.md).

Close a gap only with evidence for its final relevant inputs and route. A
parser, binder, private round trip or successful emission is not evidence for
an independent Standard consumer, native ABI or runtime behavior. Check active
test selectors and skips; do not infer whole-project acceptance from the
scoped result above.
