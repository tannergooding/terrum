# Terrum specifications

These documents define accepted target contracts. They are normative design
references, not inventories of compiler support; see the
[implementation summary](../implementation.md) for implemented boundaries.
Unapproved alternatives belong in [proposals](../proposals/README.md).
Acceptance is not pre-v1 immutability: proposals may recommend replacing these
contracts, structures, and draft assignments under the
[scope and authority rules](overview/conformance.md#scope).

## Scope and conformance

[Scope, authority, and conformance](overview/conformance.md) defines the
normative hierarchy, conformance objects, mandatory scalar catalog, and
feature dependencies. Partitions use per-chapter documents and their own
indexes; summaries do not independently redefine another chapter's rules.

## Source language

Start with [definitions and notation](language.md#definitions-and-notation):
values, copying, aliases, references and lifetime, followed by the precise
borrow/loan/lease and scope meanings and common abbreviations. The focused
chapters below remain the owners of their individual contracts.

| Contract | Specification |
|---|---|
| UTF-8 compilation input, UTF-16 snapshots, and distinct origin coordinates | [Source input](language/source-input.md) |
| Reserved/contextual words, keyword escapes, and type-query/selector spelling | [Source vocabulary](grammar-spec.md#source-vocabulary) |
| Literal parser authority, signed-token semantics, direct primitive construction and validated text input | [Literal construction](language/literal-construction.md) |
| Target-owned builders, TextTarget parameters, constructed recipes and eager capture/acquisition order | [Construction handlers](language/construction-handlers.md) |
| Nominal consuming builder completion, result states and residual cleanup | [Construction handlers](language/construction-handlers.md#consuming-builder-completion) |
| Parameterless source factory, after-capture acquisition and explicit runtime context | [Construction handlers](language/construction-handlers.md#parameterless-builder-acquisition) |
| Standard typed text/collection drain, scalar alignment and spread obligations | [Construction handlers](language/construction-handlers.md#standard-text-operations) |
| Synchronous factory/direct-cursor sources, by-value publication and required close | [Value iteration](language/iteration.md) |
| Async cursor completion-output reservations, synchronous acquisition and awaited close | [Async value iteration](language/async-iteration.md) |
| Readonly/writable element aliases, reference outputs and checked backing/invalidation refinements | [Lending iteration](language/lending-iteration.md) |
| Eager async execution, operation establishment, failure channels and completion ownership | [Async execution](language/async-execution.md) |
| Affine Task<T>, consuming await, payload/output observation and explicit every-exit settlement | [Task responsibility](language/task-responsibility.md) |
| Structural partial field moves, complete Cleanup receivers, replacement and exact retirement order | [Ownership and cleanup](language/ownership-and-cleanup.md) |
| Self-typed exception classes, complete catch subjects, object-handle bindings and active propagation ownership | [Local failures](language/local-failures.md) |
| Runtime require selectors, ordinary zero-supplied construction, callable-owned defaults and Core RequirementException | [Requirement failures](language/requirement-failures.md) |
| Opaque affine Core CapturedException, lexical Capture, original-event transfer and consuming rethrow | [Captured exceptions](language/captured-exceptions.md) |
| Owned named affine producer cursors, capture, lazy yield and creation/operation/close contracts | [Generated producers](language/generated-producers.md) |
| Whole receiver/formal sources, retained destinations, backing-only forwarding and checked member constraints | [Dependencies and views](language/dependencies-and-views.md) |
| Reference descriptors, explicit indirection, and parameter identity | [References](language/references.md) |
| Local declaration spaces and points of declaration | [Local declarations](language/local-declarations.md) |
| Namespace/member declaration spaces, lookup, and type-call binding | [Names and binding](language/names-and-binding.md) |
| Initialization, pattern bindings, joins, and state facts | [Structural flow](language/initialization-and-flow.md) |
| Unit values, expression/result categories, and statement execution | [Values and execution](language/expressions-and-statements.md) |
| Nominal interfaces, associated bindings, and qualified-self adaptation | [Interface witnesses](language/interface-witnesses.md) |
| Complete type identity, substitution, and representation normalization | [Complete types](language/types-and-normalization.md) |
| Constant-formal domains, derived Transparent wrapper eligibility and canonical nominal/carrier values | [Constant generics](language/constant-generics.md) |
| Exact compiler-known queries, complete value-layout subjects and fixed/coupled target observations | [Registered queries](language/registered-queries.md) |
| Argument mapping, finite generic inference, overload ranking, and contextual outputs | [Calls and inference](language/calls-and-inference.md) |
| Mandatory scalar signatures, arithmetic/count domains, comparisons, and checked updates | [Primitive operators](language/primitive-operators.md) |
| Same-type scalar Abs members, checked signed-minimum failure and explicit-variant boundaries | [Absolute value](language/absolute-value.md) |
| Same-type Min/Max/Clamp members, floating selection rules and exact invalid-bound precondition failure | [Numeric selection](language/numeric-selection.md) |
| Sign-only floating operations, quiet processing-result NaNs, constant recipes and no ambient status | [Floating environment](language/floating-environment.md) |
| Coherent Core numeric groups, combined Min/Max/Clamp and exact primitive witness matrix | [Numeric capabilities](language/numeric-capabilities.md) |
| Exact Core exception classes for scalar overflow, integer zero division and invalid shift counts | [Scalar checked failures](language/scalar-failures.md) |
| Portable scalar proofs, deterministic constant execution, and exact resource accounting | [Proof and constant execution](language/proof-and-constant-evaluation.md) |
| Published predicates, input-state purity, evaluation modes, NoAlias parameters and dependency-safe results | [Published predicates and pure evaluation](language/published-predicates-and-effects.md) |
| Language design and accepted direction | [Language](language.md) |
| Source productions and semantic constraints | [Grammar](grammar-spec.md) |
| Tokens and lexical rules | [Lexical grammar](lexical-grammar.md) |
| Cross-cutting source contracts | [Language contracts](language-contracts.md) |
| Type families and nominal operations | [Type families](type-families.md) |
| Numeric and character conversions | [Conversions](conversions.md) |
| Layout and calling convention | [Data layout and ABI](data-layout-and-abi.md) |

## Abstract runtime

[Runtime partition](runtime/README.md) distinguishes source authority and
logical execution obligations from physical interchange and target support.

| Contract | Specification |
|---|---|
| Explicit and conditional thread capability opt-in, actual handoff/publication/guard authority | [Sharing and handoff](runtime/sharing-and-handoff.md) |
| Reference lifetimes, backing, roots, loans and retirement | [Lifetimes](lifetimes.md) |
| Exact concurrent observations, mixed SC/fences, causal justification and storage generations | [Concurrent executions](runtime/concurrent-executions.md) |
| Shared CPU policy, access/library direction and target/platform boundaries | [Memory model](memory-model.md) |
| Paired contained adapters, affine failure domains, mapping/causes and callback responsibility | [Foreign failure bridge](runtime/foreign-failure-bridge.md) |
| Fixed postcondition/index/recoverable-allocation/cycle/failed-unit categories and affine delivery | [Standard runtime failures](runtime/standard-failures.md) |

## Libraries and protocols

| Contract | Specification |
|---|---|
| Adopted library behavior, API boundaries, and dependencies | [Libraries](libraries.md) |
| Selected API/schema/protocol roles, incomplete families and unselected candidate surfaces | [Adopted library inventory](libraries/adopted-inventory.md) |
| Provider/consumer paths, FDG comparison and deliberate responsibility/output departures | [Library design scenarios](libraries/design-scenarios.md) |
| Exact scalar Abs subjects/signature, integer magnitude and admitted floating result classes | [Absolute value](language/absolute-value.md) |
| Exact scalar Min/Max/Clamp signatures, selection observations and reusable InvalidBoundsException | [Numeric selection](language/numeric-selection.md) |
| Nominal scalar exception-class catalog, exact mappings and event/phase/admission boundaries | [Scalar checked failures](language/scalar-failures.md) |
| Independent numeric capability groups and optional same-type scalar composition | [Numeric capabilities](language/numeric-capabilities.md) |
| Standard runtime exception classes, creation, state and recoverable-publication boundaries | [Standard runtime failures](runtime/standard-failures.md) |

## Semantic interchange

[Semantic representation partition](representation/README.md) separates
allocation, metadata/Body semantics and independent conformance evidence.

| Contract | Specification |
|---|---|
| Normative `.trrm` interchange, private development/native persistence and independent target/tooling contracts | [Interchange and private persistence](representation/persistence.md) |
| Stable released-schema pairs, unchanged beta/preview versions and exact draft agreement | [Format versioning](representation/format-versioning.md) |
| Exact-schema allocations, operand shapes, family capacity and admission status | [Unified TRRM registry](representation/registry.md) |
| Artifact-wide schema, module-owned rule/features and retained admission closure | [Requirements and admission](representation/requirements-and-admission.md) |
| Required governing-edition field, exact capability lists, derived prerequisites and physical manifest admission | [Module semantic manifests](representation/semantic-manifests.md) |
| Selected import identities, complete directed contract satisfaction and retained replacement obligations | [Binding and replacement](representation/binding-and-replacement.md) |
| Assigned clause ownership, uniqueness, defaults and result-type-independent no-return | [Contract clauses](representation/contract-clauses.md) |
| Producer truth obligations, mandatory structural safe-consumption checks and schema-owned references | [Consumer verification](representation/reader-trust.md) |
| Canonical writers, equivalent noncanonical ordinary reads and explicit scoped audits | [Encoding canonicality](representation/encoding-canonicality.md) |
| Scoped capacity, deterministic structural/audit recipes and deferred numerical portable minima | [Resource accounting](representation/resource-accounting.md) |
| Required source/runtime declaration, witness, phase, ownership and evaluation records whose physical codecs remain gated | [Logical record requirements](representation/logical-records.md) |
| TRRM metadata tables and semantic contracts | [IL metadata](il-metadata.md) |
| Body encoding and executable subset | [IL instruction set](il-instruction-set.md) |

## Modules and native execution

| Contract | Specification |
|---|---|
| Admitted native binding, bounded calls and code lifetime | [Native execution boundaries](native.md) |
| JIT execution, replacement and retained code obligations independent of worker/backend support | [JIT](jit.md) |
| Logical Module binding and semantic/precompiled consumption scopes | [Module linking](modules.md) |
| Multi-provider contracts distinct from private set serialization | [Native deployment sets](module-sets.md) |
| Dependency, origin and cleanup obligations distinct from private graph serialization | [Native graphs](native-graph.md) |

Private native/development wire revisions, support inventories and CLI
examples are in the [implementation references](../implementation.md), not
independent serialized-interchange specifications.

## Targets

[Target profiles](targets/README.md) own physical representation and
interoperability, without equating storage with formal foreign mappings.

| Contract | Specification |
|---|---|
| Windows x64 fixed rootless boundary and imported platform rules | [Windows x64 native](targets/windows-x64.md) |
| All scalar storage, BID decimal, source schemas, packing/alignment and safe access | [Windows x64 data layout](targets/windows-x64-data-layout.md) |
| Standard-only primitive admission, positional carriers, stable reference inputs, outputs and owners | [Windows x64 call interface](targets/windows-x64-call-interface.md) |
| Cheap non-unwinding default, preservation, PE/unwind, closure/origin and code lifetime | [Windows x64 deployment](targets/windows-x64-deployment.md) |
| Explicit Boolean/result/owned-packet wire, domain release and synchronous callback containment | [Windows x64 failure bridge](targets/windows-x64-failure-bridge.md) |

## Tooling protocol

[Tooling partition](tooling/README.md) separates the selected machine protocol
from source-coordinate authority and informative worker responses.

| Contract | Specification |
|---|---|
| Unified versioned envelope, exact coordinates/results, distinct program status/streams and explicit supervision | [CLI machine protocol](cli-protocol.md) |

## Supporting references

[Specification conformance cases](conformance/README.md) give required
source, concurrent observation, lifetime, and representation outcomes,
independently of current implementation support.

[Design references and provenance](references.md) records external material
used when developing these contracts; it does not independently define
Terrum behavior.
