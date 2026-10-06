# Static initialization metadata and attribute schema

> **Status: unapproved proposal.** This document records a recommended
> encoding and source-attribute schema for the already accepted
> [static initialization contract](../grammar-spec.md#static-initialization-and-load-scoped-storage).
> It does not change that contract or claim compiler support.

## Summary

The accepted source contract defines type-wide initialization groups,
explicit static-constructor triggers, individually lazy fields, ordered
initializers, completion publication, failure behavior and load-scoped
cleanup. The approved `(1, 0)` metadata design does not encode static storage,
initialization groups, trigger policy or lazy state. The source grammar also
leaves the concrete semantic-attribute schemas open. Consequently, compiler
binding, persistence and runtime adoption cannot proceed safely by treating
these attributes as ordinary names or by inheriting host static-constructor
behavior.

## Decision

Which resolved semantic-attribute schemas and required metadata records should
carry the accepted static-initialization contract?

**Recommendation:** define `StaticInitializer` and `LazyInitialization` as
compiler-recognized attributes with closed schemas, then persist their
normalized meaning in required static-initialization records. Keep the
accepted execution, failure, publication, sharing and unload rules unchanged.

### Attribute schemas

- `StaticInitializer` requires exactly one positional string constant
  argument whose value is one of `"grouped-static-field-access"` or
  `"runtime-static-member-access"`. It has no default and applies only to the
  primary authored static constructor. The resolved attribute identity, not
  its spelling alone, selects this schema; the resolved constructor and
  argument name, type, and position are fixed by registration.
- `LazyInitialization` takes no arguments and applies only to an ordinary,
  non-`extern` static field. It does not apply to constants or property
  backing storage; its resolved attribute identity and zero-argument schema
  are fixed by registration.
- Neither attribute permits repetition. Partial declarations retain the
  accepted primary-only policy authorship; a secondary static-constructor
  implementation cannot restate or change the trigger.
- Both schemas use the ordinary checked constant evaluator and exact resolved
  identity rules. An arbitrary same-named user attribute cannot select these
  compiler behaviors.

The string values provide an explicit, readable closed choice without
requiring enum support or attribute-specific expression syntax. The compiler
normalizes them to a closed trigger tag; raw source strings are not runtime
policy.

### Required logical records

These are proposed record meanings and invariants, **not assigned TRRM table
kinds, tags, flags or byte layouts**. Use existing typed definition references
where available; every reference must be validated against its owning module,
definition kind and resolved generic substitution.

| Required record | Proposed contents and invariants |
|---|---|
| Schema requirement | A closed schema identity/revision and required runtime profile, present whenever the artifact has static storage semantics. Readers reject unknown requirements; absence cannot make static storage appear to be instance storage. |
| Storage classification | Exhaustively classify storage as instance, grouped static, lazy static, constant or foreign. A grouped or lazy field has exactly one owning unit; constants and foreign storage have neither. Property backing storage is explicitly associated with its property. |
| Group recipe | Owning nominal type definition, normalized trigger, constructor-policy reference, ordered-initializer range, completion contract and lifecycle contract. Exactly one recipe exists for each type requiring grouped initialization, including a type with an authored constructor but no field initializer. Lazy-only types retain the required group prerequisite rather than bypassing group completion. |
| Ordered initializer | Group, unique contiguous ordinal, destination field, initializer method/body reference and checked initialization contract. The sequence preserves compiler-input order followed by source order; property backing initializers retain their declaration positions. These are authored initializer-expression steps, not implicit default writes. |
| Constructor policy | Group, authored constructor method, normalized trigger, primary policy contract and implementation method. The constructor is initialization-only with no receiver, parameters or result; it is not an ordinary callable static method. One policy and one implementation are required when authored. |
| Lazy unit | Designated field, prerequisite group, initializer method/body, completion contract and lifecycle contract. There is one unit per eligible field, separate from the group initializer range; external demand completes the prerequisite group first. |
| Access obligation | Selected member, access kind, owning closed-type substitution, applicable group or lazy unit, and trigger/effect contract. The obligation must be visible to binding and executable consumers rather than hidden behind the member's ordinary effects. Binding and inspection alone do not create runtime demand. |
| Contract clauses | Typed, required references for initialization-phase authority, completion guarantees, sharing/access, trigger effects, failure policy, ownership, roots, cleanup targets and unload dependencies. References name supported clauses; opaque blobs, optional attributes, or assertions that grant initialized-state facts are insufficient. |

The nominal definition identifies the persisted recipe; runtime state is keyed
by the **loaded nominal definition plus its complete resolved generic
substitution**. The loader supplies load-instance identity, which is not an
artifact row number. Distinct substitutions and independent loads have
distinct groups, while aliases resolving to the same loaded closed type do
not create duplicates. Generic substitution applies to initializer bodies,
storage types and contracts as well as the group key.

The logical schema must let readers reject missing or conflicting storage
assignments, wrong owners or substitutions, duplicate constructor policies,
invalid signatures or ordinary constructor calls, malformed initializer
order, lazy fields included in grouped order, and unsupported access kinds,
contracts or runtime requirements. Runtime waiting state, cycle detection,
publication synchronization, retained failure outcomes and unload execution
are runtime machinery, not facts that structural metadata alone proves.

No artifact-wide partial-declaration ordering is introduced: partial
composition is within one module. Linking and packing preserve each selected
type's ordered sequence and identity; they do not concatenate unrelated
groups, sort filenames, prioritize a primary part, or derive execution order
from remapped row numbers. Cross-module initialization follows actual
dependencies and accesses, not startup order.

The exact row layouts, definition kinds, access-operation forms, contract
clauses and revision policy remain for metadata design to assign before
implementation. Terrum is pre-1.0 with no external artifact consumers, so
the implementation should maintain one current writer and reader rather than
add historical-revision support; a version boundary for a shipped preview
remains a release decision.

### Candidate first-slice profile

**Recommendation, still unapproved:** make the first implementation slice a
self-contained single-module profile for non-generic, non-partial nominal
types with ordinary eager static fields. Permit an authored static constructor
only with one of the two explicit trigger choices above. Exclude lazy fields,
static-property backing storage, foreign storage, generic substitutions and
partial type/constructor composition from this slice; the accepted language
contracts for those forms do not change. A module may contain unrelated
imports, but an imported or packed definition cannot participate in a static
initializer, initialization helper, triggerable member or cleanup dependency.
Reject rather than infer a foreign initialization dependency.

Within this profile, use these proposed access obligations:

| Context or operation | Proposed obligation |
|---|---|
| Runtime access to a grouped static field | Complete the owning group before the field access when no static constructor is authored or its explicit policy is `grouped-static-field-access`. |
| Runtime access to a static field or static member | Complete the owning group before the access when its explicit policy is `runtime-static-member-access`. |
| Static constructor, initializer, or checked helper accessing the owning group under validated initialization-phase authority | Use that authority, not an ordinary reentrant demand. A read is valid only after the field has been initialized; no unfinished group is published to ordinary callers. A helper does not receive this authority merely because an ordinary caller invokes it. |
| Type-name resolution, member binding or metadata inspection without runtime access | Create no initialization demand. |
| Imported or packed access participating in initialization | Reject in this slice; do not bind by name or omit the initialization effect. |

The triggering operation carries the accepted initialization effect and
failure behavior in its published contract. Successful group completion
publishes all required initialized and completion facts; a failed group remains
unpublished and retains the accepted failure outcome. The profile does not
weaken waiting, cycle-detection, memory-publication, ownership, cleanup or
load-scoped unload requirements. These are required runtime obligations, not
facts that the proposed metadata can prove.

Proposed validation ownership for this slice:

| Layer | Required responsibility |
|---|---|
| Source compiler | Resolve registered attribute identities and closed schemas; assign each field to one group; preserve initializer source order; prove authored bodies satisfy initialization-phase authority, definite-initialization, effect and constructor-policy clauses. |
| Metadata reader | Require the profile and every required record; validate row extents, typed owner references, unique field/group assignments, signatures, policy cardinality, initializer ordering, and the identity, payload shape, placement and consistency of every recognized clause; reject unknown required forms. Under the selected artifact trust policy, this structural contract validation does not re-prove accepted producer body facts. |
| Linker or consumer | Accept only self-contained initialization dependencies for this slice; reject imported/packed initialization edges and incompatible trigger contracts before lowering. |
| Runtime | Key state by loaded type identity within its load instance; implement the accepted waiting, cycle, completion publication, retained failure, cleanup and unload behavior. Metadata is not a substitute for these state transitions. |

This proposed slice is a scope boundary for staged implementation, not a new
language restriction. Later slices must add generic closed-type identity,
partial composition, lazy units, property backing storage and validated
cross-module dependencies without changing the accepted initialization
contract.

### Candidate eager-group runtime protocol refinement

**Recommendation, still unapproved:** represent each eager group in one module
load by a runtime-owned state entry keyed by the loaded nominal definition and
its resolved closed substitution. The candidate first slice excludes generic
types, so its key is the load-instance identity plus the loaded definition;
artifact row numbers, type names and process-global type identities are not
substitutes for that key.

The entry has four externally meaningful states: `Unstarted`,
`Initializing(owner)`, `Complete`, and `Failed(outcome)`. Exactly one
permitted execution flow claims `Unstarted` and runs the ordered initializer
sequence followed by the authored constructor. Competing permitted callers
wait for that execution's outcome. An ordinary demand that cycles back to the
unfinished group fails rather than observing partial state or waiting
indefinitely. Validated initialization-phase authority is distinct from an
ordinary demand: it permits the initializer or checked helper to participate
in the unpublished group, but does not permit reading a field before its
required initialization.

On normal completion, required field and constructor guarantees are
established before the runtime publishes `Complete` to waiters and ordinary
callers. Under fail-fast termination, failure does not promise cleanup. Under
defined unwinding, acquired partial state is cleaned without publishing a
live group or rolling back external effects; the runtime records `Failed` and
retains the failure outcome. Later ordinary demands surface that outcome and
do not retry. The first slice excludes unsafe recovery and suppression APIs.

Checked unload first prevents new demands and waits for quiescence: active
calls, loans, roots, pins and ownership dependencies must be discharged.
Applicable cleanup retires acquired values while their cleanup code remains
available; only then may the runtime invalidate the group storage and code.
The state entry is load-scoped and must not itself keep an unloaded image
alive.

This protocol is an implementation recommendation derived from the accepted
contract, not a new language rule or assigned runtime ABI. Admission remains
conditional on explicit, validated sharing and runtime-policy contracts:
use a checked initialization gate or a proof of nonconcurrency, and reject
profiles for which neither is available. Do not infer sharing permission or
failure behavior from `static`, member visibility, or host-runtime defaults.
The exact state-entry handle, wait/notification mechanism, recursive-owner
identity, failure transport and cleanup registration still require a typed
runtime/metadata contract before emission or execution is authorized.

### Decisions still open

| Question | Summary | Recommendation |
|---|---|---|
| Which required metadata kinds and executable access forms carry these records? | The slice now names the logical cases, but current TRRM assigns no static-storage records or access operations. | Assign closed required records and access forms together, with typed ownership and checked references. Do not emit under the current profile or overload existing flags. |
| How are semantic clauses validated? | The slice assigns reader validation of clause shape/placement separately from source-body proof, but exact clause identities, payloads and proof obligations remain open. | Define each typed required clause and its compiler proof, reader shape/consistency checks, linker compatibility checks and runtime enforcement. Keep body re-proof governed by the artifact trust policy; reject unknown required or unsupported clauses. |
| What runtime identity and lifecycle ABI implements the accepted behavior? | The candidate eager-group protocol now names load-scoped identity and required state transitions, but no handle, wait mechanism, recursive-owner identity, failure transport or cleanup ABI exists. | Assign typed runtime inputs and outcomes to the required metadata/access records; preserve the protocol above and reject any runtime profile unable to publish, retain failure, clean up or retire safely. Do not inherit host static-constructor behavior. |
| Which sharing and runtime-policy combinations are admissible in the first slice? | The accepted contract permits checked sharing or proved nonconcurrency, and distinguishes fail-fast termination from defined unwinding; current compiler profiles do not implement initialization gates or failure transport. | Admit only combinations whose sharing proof/gate and failure/cleanup path are explicit and implemented. Reject every unrepresented combination rather than infer permission or return a success-shaped fallback. |
| How do imported field references participate? | The current module schema does not bind `FieldDef` imports; the candidate slice avoids that dependency but not the eventual cross-module contract. | Keep imported/packed initialization edges rejected in the first slice; define validated field identity, trigger effects and dependency edges before a later cross-module slice. |
| What artifact revision boundary applies? | Terrum remains pre-1.0 and has no external artifact consumers. | Keep one current writer and reader during development; make any shipped-preview boundary an explicit release decision. |

## Implementation boundary

This proposal does not authorize changes to the accepted trigger, initialization,
sharing, failure/recovery, cleanup or unload semantics. It does not make the
current `(1, 0)` metadata flags carry static-initialization policy, and it does
not establish hosted or native compiler support. Emission and execution are
not authorized under the current profiles. The candidate first-slice profile
does not itself assign metadata kinds or access encodings. Implementation
remains blocked on those assignments, exact semantic clause and verifier
rules, and a runtime protocol implementing initialization identity,
publication, failure, cleanup and unload. Cross-module initialization remains
excluded until field identity and dependency obligations are defined. These
blockers do not justify changing the accepted contract or borrowing host
static-constructor behavior.
