# Static initialization metadata and attribute schema

> **Status: unapproved proposal.** This document records a recommended
> encoding and source-attribute schema for the already accepted
> [static initialization contract](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage).
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
types with ordinary eager scalar static fields. Permit an authored static
constructor only with one of the two explicit trigger choices above. Admit
execution only in a runtime profile that establishes nonconcurrent access and
uses fail-fast termination; neither condition follows from a source shape or
from `static`. Exclude lazy fields, static-property backing storage, foreign
storage, generic substitutions, partial type/constructor composition and
managed or cleanup-bearing static values from this slice; the accepted
language contracts for those forms do not change. A module may contain
unrelated imports, but an imported or packed definition cannot participate in
a static initializer, initialization helper, triggerable member or cleanup
dependency. Reject rather than infer a foreign initialization dependency.

Within this profile, use these proposed access obligations:

| Context or operation | Proposed obligation |
|---|---|
| Runtime access to a grouped static field | Complete the owning group before the field access when no static constructor is authored or its explicit policy is `grouped-static-field-access`. |
| Runtime access to a static field or static member | Complete the owning group before the access when its explicit policy is `runtime-static-member-access`. |
| Static constructor, initializer, or checked helper accessing the owning group under validated initialization-phase authority | Use that authority, not an ordinary reentrant demand. A read is valid only after the field has been initialized; no unfinished group is published to ordinary callers. A helper does not receive this authority merely because an ordinary caller invokes it. |
| Type-name resolution, member binding or metadata inspection without runtime access | Create no initialization demand. |
| Imported or packed access participating in initialization | Reject in this slice; do not bind by name or omit the initialization effect. |

The runtime profile must prevent concurrent entry to every operation that can
touch the loaded module, including ordinary reads after `Complete`. The
candidate is a single-threaded, non-reentrant load profile with no concurrent
host entry or callbacks into that module; it does not infer safety from source
shape, visibility or absence of an observed race. A runtime that cannot enforce
this admission rule rejects the profile.

The triggering operation carries the accepted initialization effect and
failure behavior in its published contract. Successful group completion
publishes all required initialized and completion facts. Under this fail-fast
profile, failure terminates execution and never publishes the group; no
recoverable failure replay or retry API is exposed. The profile does not
weaken cycle detection, memory publication, ownership, cleanup or load-scoped
unload requirements. These are required runtime obligations, not facts that
the proposed metadata can prove.

### Candidate access-obligation artifact contract

**Recommendation, still unapproved:** assign the following closed logical
records and Body-operation forms to the first slice, without assigning wire
IDs, opcode numbers or native handles. Use a new required schema/profile
instead of overloading the current metadata flags or `(1, 0)` Body profile.
The candidate static-field identity is distinct from the existing instance
`FieldDef`; a static field must not be inserted into an instance-layout range.

| Logical form | Required identity and invariant |
|---|---|
| `StaticInitProfile` | Required module profile identifying the single-module, non-generic, non-partial, eager-scalar, fail-fast and proved-nonconcurrent subset. Unknown or unsupported requirements reject. |
| `StaticField` | New module-local typed static-field definition with owner type, scalar type, mutability and owning group. Every eligible static field belongs to exactly one group; no implicit default makes vacant storage live. |
| `EagerGroup` | Owning type, normalized trigger, ordered initializer range and optional authored static constructor. Exactly one group exists per eligible type, including constructor-only types. With no authored constructor, only the synthesized grouped-field trigger is valid. |
| `EagerInitializer` | Group, unique contiguous ordinal, destination `StaticField` and compiler-owned initializer body. The ordered range follows compiler-input order and source order; steps are not ordinary callable methods or implicit default writes. |
| `InitializationRole` | Required method clause distinguishing ordinary methods, group initializer steps, the group's constructor and checked helpers. Constructor and helper roles are not ordinary entry points; every helper's phase authority and initialized-field facts are checked for its owning group. |
| `InitializationDemand` | Required published method clause naming each group whose ordinary completion may be demanded. It is separate from ordinary effects and must be preserved through same-module direct calls and linking; imported methods that participate in the initialization dependency closure reject. |
| `Access` | Typed static-field/member identity, owning group, access kind and either ordinary `None`/`Complete` demand or group-scoped initialization-phase authority. Names and row order never establish identity. |

The proposed Body forms are direct, by-value operations: `StaticRead`,
`StaticInitialize`, `StaticAssign` and `StaticCall`. Every form carries a
validated `Access`; `StaticInitialize` is legal only for vacant storage under
the owning group's phase authority, while ordinary assignment follows the
field's mutability contract. `StaticCall` uses `None` demand for an ordinary
static-member call under the grouped-field trigger and `Complete` under the
runtime-static-member trigger. Field reads and writes demand completion under
the grouped-field trigger. A separate `InitHelperCall` may forward phase
authority only to a helper checked for that same group; ordinary source
operands cannot manufacture authority. No operation returns a reference,
creates a view or exposes unfinished storage.

Each obligation identifies the selected local member, its owning group recipe,
the access kind and one of two authorities:

| Authority | Required meaning |
|---|---|
| Ordinary demand | A runtime operation selected by the trigger policy requires its owning group to complete before the operation proceeds. The operation's published contract exposes this initialization effect; ordinary method effects cannot hide it. |
| Initialization-phase authority | A source-compiler-validated initializer, static constructor or checked helper participates in its owning group's unpublished initialization flow. The authority is scoped to that group and flow, is not an ordinary reentrant demand, and permits reads only after the particular field has been initialized. |

For an ordinary demand, the referenced group and access kind must agree with
the normalized constructor trigger, or with the synthesized grouped-field
trigger when no constructor is authored. Normal completion guarantees the
required group completion before the operation continues. An unfinished group
is never exposed to ordinary callers; failure behavior is the selected runtime
policy, not an access-specific fallback. Compile-time binding and inspection
carry no demand.

Initialization dependencies in this slice resolve only to definitions in the
same module. The selected operation, member and group recipe use typed
definition references and must agree on their owning type; names, row order
and linker remapping do not establish identity. Imported or packed
initialization dependencies reject. This logical contract is not an encoding
or runtime protocol, does not assign table kinds, opcode numbers, byte layouts
or the runtime handle ABI, and does not authorize implementation. The
nonconcurrent/fail-fast admission choice remains subject to approval.

Proposed validation ownership for this slice:

| Layer | Required validation | Required rejection |
|---|---|---|
| Source compiler | Resolve registered attribute identities and closed schemas; assign each field to one group; preserve initializer source order; attach the selected local member/group obligation to each triggering operation; prove authored bodies satisfy initialization-phase authority, definite-initialization, effect and constructor-policy clauses. | Reject same-named unregistered attributes, ambiguous or mismatched group ownership, reads before field initialization, unvalidated helper access and trigger effects omitted from the published contract. |
| Metadata reader | Require the profile and every required record; validate row extents, typed owner references, selected-member/group agreement, access kind, unique field/group assignments, signatures, policy cardinality, initializer ordering, and the identity, payload shape, placement and consistency of every recognized clause. Under the selected artifact trust policy, structural validation does not re-prove accepted producer body facts. | Reject absent or unknown required schema, dangling or wrong-kind references, missing/conflicting obligations, unknown semantic clauses and unsupported trigger or authority forms. |
| Linker or consumer | Preserve the selected definitions, obligations and trigger effects without deriving them from names or remapped row order. | Reject imported/packed initialization dependencies and incompatible group or trigger contracts before lowering. |
| Runtime | For the first profile, enforce nonconcurrent access for the load instance, detect same-flow cycles, publish completion once, fail fast without publishing on failure, and quiesce before unload. Later checked-sharing profiles additionally require waiters, retained failure and cleanup transport. | Reject admission unless the runtime can enforce the selected sharing, failure, publication and lifecycle obligations; never expose unfinished state. |

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

The general entry has four externally meaningful states: `Unstarted`,
`Initializing(owner)`, `Complete`, and `Failed(outcome)`. Exactly one
permitted execution flow claims `Unstarted` and runs the ordered initializer
sequence followed by the authored constructor. Under later checked-sharing
profiles, competing permitted callers wait for that execution's outcome. The
first nonconcurrent profile admits no competing access and needs no waiter
mechanism. In either profile, an ordinary demand that cycles back to the
unfinished group fails rather than observing partial state or waiting
indefinitely. Validated initialization-phase authority is distinct from an
ordinary demand: it permits the initializer or checked helper to participate
in the unpublished group, but does not permit reading a field before its
required initialization.

On normal completion, required field and constructor guarantees are
established before the runtime publishes `Complete` to ordinary callers and,
for checked-sharing profiles, waiters. Under the first profile, fail-fast
termination promises no cleanup and no caller survives to observe or retry a
failed entry; `Failed(outcome)` replay belongs to a later defined-unwinding
profile. Defined-unwinding profiles clean acquired partial state without
publishing a live group or rolling back external effects; the runtime records
`Failed` and retains the failure outcome. Later ordinary demands surface that
outcome and do not retry. The first slice excludes unsafe recovery and
suppression APIs.

Checked unload first prevents new demands and waits for quiescence: active
calls, loans, roots, pins and ownership dependencies must be discharged.
Applicable cleanup retires acquired values while their cleanup code remains
available; only then may the runtime invalidate the group storage and code.
The state entry is load-scoped and must not itself keep an unloaded image
alive.

This protocol is an implementation recommendation derived from the accepted
contract, not a new language rule or assigned runtime ABI. The recommended
first profile uses an explicitly validated proof of nonconcurrent access and
fail-fast termination; it does not infer either from `static`, member
visibility or the absence of an observed race. Later profiles may add a
checked initialization gate and defined-unwinding failure transport only
after their sharing and cleanup contracts are assigned and implemented.
Reject runtime profiles for which neither the required sharing proof nor a
checked gate is available. The exact state-entry handle, wait/notification
mechanism, recursive-owner identity, failure transport and cleanup
registration still require a typed runtime/metadata contract before emission
or execution is authorized.

### Decisions still open

| Question | Summary | Recommendation |
|---|---|---|
| What is the logical access obligation before assigning its encoding? | The accepted source contract distinguishes ordinary demand from initialization authority, but does not assign artifact access records or operations. | Review the candidate forms above: bind each runtime access to its selected local member and owning group, distinguish `None`/`Complete` demand from validated group-scoped authority, publish the trigger effect, and reject missing or mismatched obligations. |
| Which required metadata kinds and executable access forms carry these records? | Current TRRM and semantic-module revision 76 assign no static-storage records or access operations; existing instance `FieldDef` rows do not identify static cells. | Use the candidate `StaticInitProfile`, `StaticField`, `EagerGroup`, `EagerInitializer`, method-role/demand clauses and typed Body access forms above. Keep wire IDs, opcode numbers and native handles unassigned until this proposal is reviewed; do not emit in current profiles or overload existing flags. |
| How are semantic clauses validated? | The slice assigns reader validation of clause shape/placement separately from source-body proof, but exact clause identities, payloads and proof obligations remain open. | Define each typed required clause and its compiler proof, reader shape/consistency checks, linker compatibility checks and runtime enforcement. Keep body re-proof governed by the artifact trust policy; reject unknown required or unsupported clauses. |
| What runtime identity and lifecycle ABI implements the accepted behavior? | The candidate names load-instance identity and state transitions, but no runtime handle or unload protocol is assigned. | Assign a load-scoped group key and phase-authority transport. The first profile needs no waiter or retained-failure ABI; later checked-sharing or defined-unwinding profiles must specify their own waiting, failure, cleanup and retirement machinery. Do not inherit host static-constructor behavior. |
| Which sharing and runtime-policy combinations are admissible in the first slice? | The accepted contract permits checked sharing or proved nonconcurrency, and distinguishes fail-fast termination from defined unwinding; current compiler profiles enforce neither. | Start with single-module, non-generic, non-partial eager scalar storage under a single-threaded, non-reentrant load profile and fail-fast termination. Test both trigger choices, initializer order, one-time completion, cycle failure in a subprocess, load separation and quiescent unload. Keep checked sharing, defined-unwinding failure transport and non-scalar cleanup for later profiles; reject unsupported admission rather than infer permission. |
| How do imported field references participate? | The current module schema does not bind static-field imports; the candidate slice avoids cross-module initialization dependencies. | Keep imported/packed initialization edges rejected in the first slice; define validated field identity, trigger effects and dependency edges before a later cross-module slice. |
| What artifact revision boundary applies? | Terrum remains pre-1.0 and has no external artifact consumers. | Keep one current writer and reader during development; make any shipped-preview boundary an explicit release decision. |

## Implementation boundary

This proposal does not authorize changes to the accepted trigger, initialization,
sharing, failure/recovery, cleanup or unload semantics. The candidate logical
records and operations above remain unapproved and do not make the current
`(1, 0)` metadata flags or semantic-module revision 76 carry
static-initialization policy. They do not establish hosted or native compiler
support. Emission and execution are not authorized under current profiles.
Implementation remains blocked on proposal approval, assigned wire schemas
and Body opcodes, exact semantic-clause and verifier rules, and a runtime
protocol implementing load-scoped identity, nonconcurrent admission,
publication, failure, cleanup and unload. Cross-module initialization remains
excluded until field identity and dependency obligations are defined. These
blockers do not justify changing the accepted contract or borrowing host
static-constructor behavior.
