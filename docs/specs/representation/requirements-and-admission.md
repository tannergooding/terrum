# Interchange requirements and admission

## 1. Identity and ownership

These rules define logical `interchange-requirements/1`. The
[Module manifest](semantic-manifests.md) supplies its current physical
edition/Feature protocol under the [registry](registry.md) and
[metadata](../il-metadata.md). The draft registry still lacks registrations
and physical carriers for some approved capabilities; those gaps do not define
a reduced TRRM 1.0 conformance floor.

The [persistence taxonomy](persistence.md) distinguishes normative `.trrm`
interchange from implementation-private saved IR and native containers.
Private serialization does not replace the applicable public semantic,
binding or target obligations, and a private wrapper does not select an
embedded normative payload's schema or governing rules.

An artifact has **one encoding schema**. The schema fixes every encoded
field's namespace, valid shape and reference interpretation.
A Module cannot select another decoder for its rows or Bodies, even when
physical tables contain several modules. An enclosing PE/ELF format, source
edition, registry label, target ABI or runtime policy is not the encoding
schema's identity.

The [format-version contract](format-versioning.md) selects the existing
major/minor pair as the stable exact released-schema identity. Current beta
and preview drafts retain `(1, 0)` without pair-update or compatibility duties;
their exact schema agreement remains out of band. No separate schema-ID field
or released assignment is introduced.

Separately supplied dependency artifacts each select their own schema.
A consumer must understand and admit each one; it does not require equal
encoding identities merely because their modules can bind. Packing into one
artifact instead requires one destination schema capable of representing
all retained modules.

Each logical Module independently owns these semantic requirements:

| Requirement | Meaning |
|---|---|
| Governing rule edition | The exact selected edition of the language/runtime rules, including its defined baseline, defaults and feature interpretation |
| Required features | Resolved feature identities and exact required semantic revisions, with their complete prerequisites |
| Target and runtime | Applicable representations, ABI, failure policy, helpers and execution substrates under their own contracts |
| Dependencies | Selected module/definition identities and complete retained expected contracts, including their required semantics |

These are semantic identities, not mutable package labels, source spelling,
physical row numbers, producer-assertion flags or an integer "at least" test.
A feature revision identifies rules, not a producer's implementation version.
Published logical contracts such as `task-responsibility/1` do not thereby
receive physical Feature IDs.

The governing edition identifies the rules under which the Module's bodies
and published facts were checked. The consumer's current default edition
cannot replace it. A private body need not repeat every rule identity on
each operation, but its Module and retained contracts must determine them
unambiguously. A record with its own required rule identity retains that
requirement; it cannot silently override an incompatible governing rule.

An implementation declares the semantic editions/revisions it supports.
It can satisfy a requirement by supporting that exact semantics or an
explicitly specified compatibility mapping that preserves the required
behavior and contracts in the applicable context. A greater revision number,
same layout, subset of known names or support for a different edition is
not such a mapping. Compatibility is directed; symmetry, transitivity and
roll-forward are not inferred. The required identity remains unchanged.
The [binding contracts](binding-and-replacement.md)
separately determine whether two declarations can be linked or replaced;
feature support is not proof of declaration compatibility.

Modules in one artifact can retain distinct rule/feature revisions.
This does not promise that arbitrary revisions can coexist in one execution:
the consumer must support every retained requirement and the actual
cross-module contracts, target and runtime must be compatible. Selecting the
newest edition or one maximum feature revision for the whole artifact is not
a permitted way to resolve a mismatch.

## 2. Complete requirement closure

A governing edition defines a closed baseline. Each registered feature
revision defines its semantic scope and exact prerequisites, including
other rules/features, required contracts, representations and substrates.
A name, numeric allocation, annotation or known row shape alone does not
define that scope.

A Module's effective requirements are the least complete set containing its
edition baseline, explicitly required features and all their prerequisites.
This is a logical obligation set, not a requirement to duplicate identical
physical rows. An encoding may share or derive records only under its
specified rules; the complete exact requirements must remain recoverable.
Unknown required identities, missing prerequisite definitions and conflicting
revisions of one feature violate the producer's requirements obligation or
fall outside the selected schema. An implementation cannot claim support by
picking the larger revision.
One revision can support another only through the explicit compatibility
rule; it does not rewrite the Module's required identity.

Requirements include retained public declarations, expected imports,
constant programs/values, witnesses, layout observations, generated bodies,
cleanup and load/code dependencies, not only emitted machine calls.
For example, a retained view's backing/cleanup contract is still a dependency
when no direct call instruction names its provider. A compiler-only parser
that leaves no retained runtime or value/contract dependency need not become
a runtime import merely because it was used during compilation.

Every selected dependency Module retains its own edition and complete
requirements. The consumer admits the complete retained dependency closure,
including co-located producers and direct packed references. Requirements
are not limited to the selected entry method, branch or anticipated execution
path. Lazy compilation does not permit discovering an unsupported required
feature only after affected source behavior has started.

Finite recursive dependency graphs have a finite closure: an already
identified Module/requirement does not acquire another identity when reached
again. A graph cycle is not itself permission for recursive storage, static
initialization, cleanup or execution that its canonical contract forbids.
Unresolved or conflicting selected identities do not denote empty
dependencies or establish a support claim.

## 3. Support claims and producer validity

This chapter defines valid artifact requirements and the scope of an
implementation's support claim. Source compilation checks authored source.
Consumers of reconstructed metadata, JIT and AOT inputs must perform the
structural, range, reference, schema-support and binding checks needed for
safe affected use; they are not required to semantically re-prove
producer-published facts. The implementation chooses the checks' internal
organization and diagnostic wording.

| Scope | Required boundary |
|---|---|
| Entire artifact | A valid artifact selects one exact schema and satisfies its framing and section rules; safely decode the selected input before binding or use |
| Records, indexes and references | Validate schema-owned namespaces, assigned shapes, ranges and consistent references before affected use |
| Selected modules and retained dependencies | A support claim includes their exact governing editions, complete semantic requirements, representations and substrates |
| Retained binding/replacement edges | Check the complete directed expected/provider relation, target and runtime contracts needed for the selected binding; this does not prove either implementation honors its contract |
| Authored body facts | Producers establish truth by source checking; consumers safely decode supported reconstructed bodies and may rely on their published facts without semantic re-proof |

A structural inspector may report recognized unsupported semantics without
executing them. Decoding a known feature is distinct from implementing its
required behavior. Execution support for known but unretained semantics need
not be claimed. Structural safe-consumption checks and support admission
cover the selected artifact and retained dependency closure before code
generation or execution; semantic audits remain optional and separately
identified.

A producer must not hide undeclared required semantics in an uncalled Body
or unselected Module. Consumers determine the selected artifact's complete
requirements and safely decode relevant required bodies rather than treating
the manifest as proof that no additional required forms are present. A known
but unsupported semantic requirement does not
establish a support claim. Optional nonsemantic sections, debug paths, Attr
rows, unused bits and producer-chosen labels cannot carry or activate
required behavior.

In `trrm/1-draft-layout-queries-1`, Module's required `governingRuleEdition` identifies
its exact baseline and its Feature range supplies explicit additional
requirements. Edition `1` is the accepted TRRM 1.0 semantic baseline; it does
not select a reduced execution profile. The registry assigns Feature IDs 9-13. Feature 13 `LayoutQueries` covers
retained `DE` queries, TypeIdentity flow/comparison, FieldMap/FieldPath,
FieldDef Import/Export binding and retained layout field paths; it has no
unconditional Feature prerequisite. Features 9-12 still lack complete
registrations and prerequisite closure. A module that needs no additional
registered feature uses `(0, 0)`; that empty range does not assert that the
consumer implements every accepted TRRM 1.0 contract. Exact draft
schema/catalog agreement remains out of band. Reserved header bytes or a
same-spelled attribute cannot substitute for the required Module manifest.

Malformed framing, missing required representations, unsupported
requirements, incompatible bindings and target/runtime mismatches are
distinct validity or support conditions. This specification defines the
conditions for a valid artifact and a correct support claim. Consumers reject
structurally unsafe, malformed or unsupported input, incompatible bindings
and target/runtime mismatches before affected use; a semantically false but
structurally consumable producer assertion remains a producer violation and
need not be detected by the consumer. Diagnostic wording and rejection order
are not prescribed. Resource exhaustion remains governed by
[resource accounting](resource-accounting.md) for supported operations,
separately from artifact validity and semantic support. Capacity declarations
cannot replace complete retained requirements or expand an optional audit's
claimed scope.

Admission discovers declared requirements through supported records, not by
running a dependency's initialization, parser, equality, getter or cleanup
to infer its contracts. Required constant interpretation first admits the
actual selected program/intrinsic and its dependencies under the
[constant-execution rules](../language/proof-and-constant-evaluation.md).
Ordinary runtime preconditions remain invocation obligations; support
admission does not prove that all future callers satisfy them.

## 4. Source obligations and representation completion

The following inventory is a completion gate, **not** a Feature-ID allocation
or a claim of complete source-free support. Each coherent retained feature
needs its declarations/contracts, typed operations/value codecs, binding and
rewrite rules, execution/target dependencies, rule interpretation and
independent cases. Supplying only some of those layers does not admit the
feature.

| Retained surface | Required published/operational content | Current physical boundary |
|---|---|---|
| [Scalar and unit values](../language/primitive-operators.md) | Exact type/result/parameter identity, values, rounding/failure and operation domains | The current draft registry does not yet encode every accepted type/operation capability; implementation and artifact admission require complete type, operation and prerequisite closure, not a fixed scalar execution profile |
| [Absolute-value members](../language/absolute-value.md) | Exact owning scalar/member, same-type value formal/result, target width, checked magnitude domain, failure/result state, actual effects and required helper/program dependencies | Primitive codes and equal ABI do not register a member, variant, constant program or overflow payload; complete member/call/failure support remains gated without new intrinsic or opcode allocation |
| [Numeric selection members](../language/numeric-selection.md) | Exact Min/Max/Clamp owner/formals/results, NaN/zero/cohort observations, target width, explicit ordered-bound predicate/Core exception class and factory, source precondition state/phase and full actual dependencies | [Selection records](logical-records.md#numeric-selection-and-bounds-failures-encoding-pending) remain unassigned; machine compare/selects and equal exception-class ABI do not register a member or admit its precondition/event program |
| [Floating environment and representation](../language/floating-environment.md) | Sign-only Abs/negation, quiet processing-result NaNs, deterministic admitted constant recipe, no ambient status facility and independent target control duties | Bounded floating families do not supply missing formats/member/constant programs; no new ambient-state codec or opcode follows |
| [Grouped numeric capabilities](../language/numeric-capabilities.md) | Exact Core self/formal/member identities, default Copy-capable domains, grouped selection, inherited requirements and complete primitive witness matrix | [Witness records](logical-records.md#floating-representation-and-numeric-witnesses-encoding-pending) remain incomplete; primitive codes/member names do not encode generic dispatch or constant authorization |
| [References and outputs](../language/references.md) | Complete access/backing/root/pin/representation, input versus initialization mode, actual output states, loans and failure publication | Ordinary Param mode `00` and the distinct `ref<init T>` TypeSpec are assigned logically; GC TypeSpec identity, predicate/dependency registrations and other physical carriers remain gates, not category-wide exclusions |
| [File access and friends](logical-records.md#source-unit-access-and-definition-identity-encoding-pending) | Semantic source-unit principals, definition identities and exact access grants preserved through relocation | SourceFile debug paths and existing visibility bits are not these records |
| [Nominal witnesses](../language/interface-witnesses.md) | Complete interface/self/member/associated identities, substitutions, constraints and checked adaptations | Existing TypeDef/Constraint shapes do not encode complete witness/projection dispatch |
| [Generics and constants](../language/constant-generics.md) | Formal/domain/default identities, canonical nominal/carrier values, validity and target value-codec commitments | Inspectable generic rows and scalar bits alone do not admit generic export, constant arguments or wrappers |
| [Static initialization](logical-records.md#static-initialization-and-load-scope-encoding-pending) | Loaded closed-type groups, initializer order, phase authority, trigger/failure/recovery and cleanup/load dependencies | Instance FieldDef/MethodDef shapes, packed row order and reserved flags are not static records |
| [Local exception eligibility](../language/local-failures.md) | Exact self-typed exception-class and common-view witnesses, complete catch subjects, object-handle bindings, active event ownership/provenance and input retirement | Class/factory/handler/operation-state codecs remain unassigned; a source name or raw runtimePolicy cannot supply them |
| [Core scalar failures](../language/scalar-failures.md) | Exact exception-class/factory identities, numeric/count domain, original operation site, no-result state, policy/effects/phase and complete event/transport dependencies | [Scalar catalog records](logical-records.md#scalar-failure-catalog-encoding-pending) remain unassigned; scalar opcodes, class identity alone and raw runtimePolicy do not admit exception creation/handler execution |
| [Standard runtime categories](../runtime/standard-failures.md) | Fixed postcondition/index/recoverable-allocation/cycle/failed-unit exception classes, original versus fresh events, actual output/cleanup state and runtime/load dependencies | [Category records](logical-records.md#standard-runtime-categories-encoding-pending) remain incomplete; a failed-state bit cannot share the original event or prove recoverable OOM transport |
| [Runtime requirement failures](../language/requirement-failures.md) | Explicit/default complete E, primary RequireException/Core identities, source owner/phase, exact ordinary zero-supplied constructor binding/effects, actual failure and output/cleanup states | Full default/constructor/operation/value/phase codecs remain unassigned; source names, scalar defaults and runtimePolicy cannot activate them |
| [Captured exception ownership](../language/captured-exceptions.md) | Exact Core owner/member, lexical handler source and transfer state, preserved event/payload/causes, input extents, creation/throw/retirement programs, phase/effects and complete dependencies | Registration/context/owner/handler/cause/phase codecs remain unassigned; a Capture name, opaque words or reserved failure band cannot supply them |
| [Checked failure and patterns](logical-records.md#pattern-evidence-and-handler-selection-encoding-pending) | Typed failure/captured-type evidence, source-order post-unwind selection, live filters, causal replacement, roots and cleanup | The lexical exception-region and handler contracts are approved; exact runtime/registry carriers and complete prerequisite closure remain gates, not a fail-fast-only execution floor |
| [Effects and predicates](../language/published-predicates-and-effects.md) | Exact form/domain, finite reads/effects, selected predicates, NoAlias extents, initialization results and rule dependencies | Bounded Effects is not a codec for Pure/ConstEval, predicate programs, NoAlias or completion effects |
| [Literal construction](../language/literal-construction.md) | Target-owned kind/signature/input recipes, required parser/program/intrinsic and valid frozen value | Existing Attr or ConstBits data does not encode registrations, all target values or interpreter eligibility |
| [Eager construction](../language/construction-handlers.md) | Builder recipes, nominal acquisition/drain/completion, typed capture graphs, TextTarget and exact spread/formatter obligations | No handler/graph/capture codec follows from a builder TypeSpec or annotation |
| [Value and lending iteration](../language/iteration.md) | Exact factories/cursors/members, output value versus loan, advance/close phases, invalidation and retained sources | Synchronous/asynchronous/lending protocols need their own complete call/phase/output representations |
| [Async and producers](../language/generated-producers.md) | Establishment/failure timing, named cursor identity, captures, resume/yield/completion/close and generated code ownership | Task/frame/continuation/producer representations remain gated |
| [Task responsibility](../language/task-responsibility.md) | Exact affine owner/wait/payload/output identities and every-exit settlement/transfer | A Task-shaped nominal type, status bit or root cannot encode consuming await or discharge responsibility |
| [Ownership and cleanup](../language/ownership-and-cleanup.md) | Complete/partial/vacant field states, transfer/repair, actual acquisitions and retirement order | Inspectable Cleanup and Drop shapes do not provide the general owner/cleanup protocol |
| [Dependencies and views](../language/dependencies-and-views.md) | Whole source/result/capture identities, backing refinements, root/load/guard/lease and invalidation relationships | Held Dependency tags, a Span type or optional attributes cannot supply these relationships |
| [Sharing and concurrency](../runtime/concurrent-executions.md) | Explicit capabilities/premises, actual protocols/locations, order, failure effects, lifetime and target support | Reserved atomic/fence/root bands allocate no operations or authority/progress protocols |
| [Layout and queries](../language/registered-queries.md) | Complete type/target layout, explicit Blittable and fixed/coupled observations with rebuild/rebind obligations | `DE` kinds 1..5, FieldMap/FieldPath and target-layout path ranges are assigned under Feature 13; exact open-generic identity evidence, dynamic target models, complete layout/coupling support and related constant/witness prerequisites remain separate gates. Scalar query results do not freeze a replaceable layout |
| [Managed and foreign boundaries](../data-layout-and-abi.md) | Selected allocation/root/barrier/code lifetime, representation/ABI, callbacks and unwind containment | [Windows x64 native](../targets/windows-x64.md) specifies its fixed rootless boundary; structural pointer/call shapes alone do not admit it, a collector, callbacks or exception interchange |
| [Contained foreign failure](logical-records.md#foreign-failure-bridge-encoding-pending) | Selected paired definitions/signatures/domains, exact status/output states, eligible local mapping, affine packet/cause retirement and runtime/allocator/code dependencies | [Windows x64 failure bridge](../targets/windows-x64-failure-bridge.md) fixes its native wire; packet/local failure/callback/mapping codecs remain gated, without new Feature IDs or exception regions |

An unavailable required physical layer is an unsupported requirement before
independent use, even when a source compiler privately implements the source
feature. Excluding it from a bounded physical profile does not remove it
from the complete source language floor.

## 5. Packing, transformation and retained decisions

Packing preserves each Module's exact edition, effective requirements,
selected dependencies and complete contracts while remapping physical rows.
It does not union modules into a new semantic identity, take a maximum
revision, choose the entry Module's rules for other bodies or turn an import
into an obligation-free direct call. Shared rows must retain every using
Module's interpretation and contract context; context-sensitive cloning
remains subject to complete binding rules.

Encoding conversion uses an explicit source and destination schema and
preserves decoded meaning; the destination must represent every retained
requirement. A byte reallocation is not a semantic-edition upgrade.
Changing governing semantics requires a separately justified transformation
and renewed compatibility/admission, not relabeling the artifact.

Trimming may remove genuinely unretained definitions/modules and their
requirements only under complete dependency and rewrite rules. It cannot
use a false index, unknown required form or omitted contract to justify that
removal. If a dependency is completely discharged by a semantics-preserving
transformation, its former runtime import need not survive; all remaining
value, contract, helper, layout, code/load and cleanup requirements still do.

Admission decisions apply to the actual admitted artifact/module revisions,
selected dependency definitions, semantic support/mappings, target/runtime
and trust/audit environment. Reuse is valid only while the facts on which
the decision relied remain unchanged or are revalidated under the applicable
replacement contract. Same Module identity or entry name alone is not a
cache-validity guarantee. A producer-authored "already accepted" flag or
integrity digest is not a consumer admission decision.

The [replacement relation](binding-and-replacement.md#5-packing-replacement-and-relied-on-facts)
also retains stronger provider facts actually used by an optimizer. Removing
an Import or observing a broader old expectation does not discharge those
additional obligations or patch an already derived native artifact.

## 6. Conformance and physical protocol boundary

[Required cases](../conformance/il-requirements-and-admission.md) distinguish
exact schema/requirement identity, retained closure, bounded access/validation scope,
support versus binding/truth, and transformations. Finite supplied-fact models
do not prove a general decoder, body audit, linker, native runtime or arbitrary
source-obligation inference.

This contract selects artifact versus Module ownership and exact semantic
interpretation. Released schema identity uses the format pair, with no
separate physical schema-ID record; current drafts still require out-of-band
agreement. The [manifest protocol](semantic-manifests.md) supplies Module edition
and exact Feature records, coherent capability-registration gates and current
edition `1`; its [cases](../conformance/il-semantic-manifests.md) cover their
physical admission and finite transformations. No extra Feature ID or compatibility
mapping is registered. `Feature.ruleRevision` is exact, not numeric roll-forward.
Header reserved bytes and Module flags remain zero. Missing representations in
the completion inventory remain gated.
