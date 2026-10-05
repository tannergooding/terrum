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

### Decisions still open

| Question | Summary | Recommendation |
|---|---|---|
| Which required metadata kinds and executable access forms carry these records? | Current TRRM assigns no static-storage records or static access operations; definition references and required-unknown-data handling already exist. | Assign closed required records and access forms together, with typed ownership and checked references. Do not emit under the current profile or overload existing flags. |
| How are semantic clauses validated? | The accepted contract requires phase authority, completion, sharing, trigger effects, failure and lifecycle obligations that structural metadata alone cannot prove. | Encode typed required clause references and define consumer/verifier checks for each; reject unsupported clauses rather than accepting producer assertions as proof. |
| What runtime identity and lifecycle ABI implements the accepted behavior? | Runtime state must distinguish loaded closed types and preserve waiting, publication, failure, cleanup and unload guarantees; these are not structural-reader facts. | Specify canonicalization and runtime state transitions against the accepted contract before runtime adoption; do not inherit host static-constructor behavior. |
| How do imported field references participate? | Current module imports do not bind `FieldDef`, so cross-module static-field access cannot yet carry a validated owner or initialization obligation. | Keep such imports unsupported until field identity and access obligations are explicit; never fall back to name-only binding. |
| What artifact revision boundary applies? | Terrum remains pre-1.0 and has no external artifact consumers. | Keep one current writer and reader during development; make any shipped-preview boundary an explicit release decision. |

## Implementation boundary

This proposal does not authorize changes to the accepted trigger, initialization,
sharing, failure/recovery, cleanup or unload semantics. It does not make the
current `(1, 0)` metadata flags carry static-initialization policy, and it does
not establish hosted or native compiler support. Emission and execution are
not authorized under the current profiles. Implementation remains blocked on
assigning required metadata kinds and access forms, specifying the semantic
contract clauses and verifier rules, defining loaded closed-type
canonicalization and runtime initialization/publication/failure/cleanup
behavior, and resolving the current lack of imported field binding. These
blockers do not justify changing the accepted contract or borrowing host
static-constructor behavior.
