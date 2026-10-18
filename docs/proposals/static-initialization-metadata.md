# Static initialization metadata and attribute schema

> **Status: partially accepted.** Source semantics are in the
> [grammar specification](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage);
> the distinct static-field identity and explicit typed access-obligation
> requirements are in the [metadata specification](../specs/il-metadata.md).
> The accepted first implementation gate is tracked in the
> [implementation reference](../implementation.md). This proposal retains
> unresolved attribute registration, metadata/Body encoding, runtime ABI and
> implementation work; no implementation support is established.

## Remaining schema work

The `(1, 0)` metadata design has no static-storage records or initialization
operations. The accepted source forms and access obligations are specified
canonically; do not repeat or weaken them here. Resolve their compiler
registration through the selected core library rather than attribute names.

The enum and case identities for `StaticInitializer`, the resolved attribute
identities, argument binding, and per-attribute multiplicity remain open.
The recommendation is to make both attributes non-repeatable and reject
ambiguous registrations or unknown arguments. The accepted primary-only
authorship and argument-shape rules remain in the
[grammar specification](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage).

### Required logical records

The following logical record inventory is **unapproved** and assigns no TRRM
table kinds, tags, flags or byte layouts. The accepted source and access
semantics are in the [grammar](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage)
and [metadata specifications](../specs/il-metadata.md); the remaining design
work is how to persist, validate and execute those contracts.

| Candidate record | Unresolved representation decision |
|---|---|
| Schema requirement | Choose schema/profile identity and revision, plus how readers discover and reject unsupported requirements. |
| Static-field record | Choose a physical record for the accepted logical identity distinct from instance `FieldDef`; define owner, type, mutability and group references. |
| Group recipe | Encode the owner, normalized trigger, initializer range and constructor association; specify generic-substitution binding and cardinality. |
| Ordered initializer | Map source/compiler order to stable initializer references and represent destination initialization contracts. |
| Constructor policy | Encode the resolved trigger and primary policy without making the initializer an ordinary callable method; specify identity and cardinality. |
| Lazy unit | Link an eligible field to its prerequisite group and initializer without merging it into the eager initializer sequence. |
| Access obligation | Persist selected member/group identity, access kind and authority so binding and executable consumers can validate the accepted obligation. |
| Contract clauses | Define typed references, payloads and validation for effects, initialization authority, failure, ownership, cleanup and unload dependencies. |

References must be validated against their owning module, definition kind and
resolved substitution. The writer and linker must preserve source order and
identity across partial composition and packing; exact reference remapping and
rejection rules remain open. Runtime state, cycle detection, synchronization,
failure transport and unload execution are not facts that metadata alone can
prove. Follow the pre-release format evolution policy in the
[metadata specification](../specs/il-metadata.md); no historical-reader
support is required for the current unreleased schema.

### Candidate access-obligation mapping (unapproved)

The accepted implementation gate is documented in the
[implementation reference](../implementation.md); it is not a source-language
restriction or complete TRRM 1.0 GA profile. The following mapping is a
metadata/runtime recommendation, not part of that accepted gate:

| Context or operation | Proposed obligation |
|---|---|
| Runtime access to a grouped static field | Complete the owning group before the field access when no static constructor is authored or its explicit policy is `grouped-static-field-access`. |
| Runtime access to a static field or static member | Complete the owning group before the access when its explicit policy is `runtime-static-member-access`. |
| Static constructor, initializer, or checked helper accessing the owning group under validated initialization-phase authority | Use that authority, not an ordinary reentrant demand. A read is valid only after the field has been initialized; no unfinished group is published to ordinary callers. A helper does not receive this authority merely because an ordinary caller invokes it. |
| Type-name resolution, member binding or metadata inspection without runtime access | Create no initialization demand. |
| Imported or packed access participating in initialization | Reject in this slice; do not bind by name or omit the initialization effect. |

The trigger/effect and failure semantics are canonical in the
[source specification](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage);
the remaining decision is how the proposed records expose them to consumers.

### Candidate access-obligation artifact contract

**Recommendation, still unapproved:** assign the following closed logical
records and Body-operation forms to the first slice, without assigning wire
IDs, opcode numbers or native handles. Extend the selected draft with required
records and exact capability requirements rather than hiding semantics in
current flags. This may retain `(1, 0)` under the draft versioning rules;
it does not require a new on-wire selector or old-reader support.
The accepted static-field identity is in the
[metadata specification](../specs/il-metadata.md); `StaticField` below is only
a candidate physical representation.

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
`StaticInitialize`, `StaticAssign` and `StaticCall`, with an optional
`InitHelperCall`. Their exact operand and `Access` encodings are unapproved.
The accepted demand/authority semantics and trigger effects are in the
[grammar](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage)
and [metadata specifications](../specs/il-metadata.md). This candidate slice
uses typed same-module definition references and rejects imported or packed
initialization dependencies; cross-module linking remains open. It assigns no
table kinds, opcode numbers, byte layouts or runtime handles.

Proposed validation ownership for this slice:

| Layer | Required validation | Required rejection |
|---|---|---|
| Source compiler | Resolve registered attribute identities and closed schemas; assign each field to one group; preserve initializer source order; attach the selected local member/group obligation to each triggering operation; prove authored bodies satisfy initialization-phase authority, definite-initialization, effect and constructor-policy clauses. | Reject same-named unregistered attributes, ambiguous or mismatched group ownership, reads before field initialization, unvalidated helper access and trigger effects omitted from the published contract. |
| Metadata reader | Require the profile and every required record; validate row extents, typed owner references, selected-member/group agreement, access kind, unique field/group assignments, signatures, policy cardinality, initializer ordering, and the identity, payload shape, placement and consistency of every recognized clause. Under the selected artifact trust policy, structural validation does not re-prove accepted producer body facts. | Reject absent or unknown required schema, dangling or wrong-kind references, missing/conflicting obligations, unknown semantic clauses and unsupported trigger or authority forms. |
| Linker or consumer | Preserve the selected definitions, obligations and trigger effects without deriving them from names or remapped row order. | Reject imported/packed initialization dependencies and incompatible group or trigger contracts before lowering. |
| Runtime | For the first profile, enforce nonconcurrent access for the load instance, detect same-flow cycles, publish completion once, fail fast without publishing on failure, and quiesce before unload. Later checked-sharing profiles additionally require waiters, retained failure and cleanup transport. | Reject admission unless the runtime can enforce the selected sharing, failure, publication and lifecycle obligations; never expose unfinished state. |

This proposed slice is an implementation scope, not a new language
restriction. Later slices may add generic closed-type identity, partial
composition, lazy units, property backing storage and validated cross-module
dependencies without changing the accepted source contract.

### Candidate eager-group runtime protocol refinement

**Recommendation, still unapproved:** give each eager group a load-scoped
runtime state entry keyed by loaded nominal definition and resolved generic
substitution, not by artifact row number, type name or process-global type
identity. A candidate state machine is `Unstarted`, `Initializing(owner)`,
`Complete`, and `Failed(outcome)`.

The accepted first-profile admission and failure boundary is in the
[implementation reference](../implementation.md). Proposed extensions beyond
that gate are checked-sharing waiters and retained failure outcomes for
defined-unwinding profiles. The runtime state must remain load-scoped and
unload only after required quiescence; exact owner identity, handles,
wait/notification, failure transport and cleanup registration remain open.
The accepted source lifecycle rules remain in the
[grammar specification](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage);
this candidate protocol assigns no runtime ABI.

### Decisions still open

| Question | Summary | Recommendation |
|---|---|---|
| Which required metadata kinds and executable access forms carry these records? | Current TRRM and semantic-module revision 76 assign no static-storage records or access operations; existing instance `FieldDef` rows do not identify static cells. | Use the candidate `StaticInitProfile`, `StaticField`, `EagerGroup`, `EagerInitializer`, method-role/demand clauses and typed Body access forms above. Keep wire IDs, opcode numbers and native handles unassigned until this proposal is reviewed; do not emit in current profiles or overload existing flags. |
| How are semantic clauses validated? | The slice assigns reader validation of clause shape/placement separately from source-body proof, but exact clause identities, payloads and proof obligations remain open. | Define each typed required clause and its compiler proof, reader shape/consistency checks, linker compatibility checks and runtime enforcement. Keep body re-proof governed by the artifact trust policy; reject unknown required or unsupported clauses. |
| What runtime identity and lifecycle ABI implements the accepted behavior? | The metadata proposal names a load-scoped state entry, but no owner identity, runtime handle, waiter or unload protocol is assigned. | Specify the state-entry key and lifecycle ABI; keep waiters and retained-failure transport outside the accepted first gate unless a later profile defines them. |
| Which sharing and runtime-policy combinations follow the first gate? | The accepted nonconcurrent/fail-fast admission boundary is documented in the [implementation reference](../implementation.md); the runtime does not implement it yet. | Implement and validate that gate first. Define checked-sharing, defined-unwinding and non-scalar cleanup as later profiles rather than silently admitting them. |
| How do imported field references participate? | The current module schema does not bind static-field imports; the candidate slice avoids cross-module initialization dependencies. | Keep imported/packed initialization edges rejected in the first slice; define validated field identity, trigger effects and dependency edges before a later cross-module slice. |

## Implementation boundary

This proposal does not authorize changes to the accepted trigger, initialization,
sharing, failure/recovery, cleanup or unload semantics. The accepted logical
access-obligation requirement assigns no record layout, opcode or runtime ABI;
candidate records and operations above remain unapproved and do not make the
current `(1, 0)` metadata flags or semantic-module revision 76 carry
static-initialization policy. They do not establish hosted or native compiler
support. Emission and execution are not authorized under current profiles.
Implementation remains blocked on assigned wire schemas and Body opcodes,
exact semantic-clause and verifier rules, and a runtime
protocol implementing load-scoped identity, nonconcurrent admission,
publication, failure, cleanup and unload. Cross-module initialization remains
excluded until field identity and dependency obligations are defined. These
blockers do not justify borrowing host static-constructor behavior or silently
weakening the accepted contract. A deliberate redesign remains possible
through an explicit amendment; current assignments are not immutable.

## Integration and review boundary

Prefer the distinct typed static-field/group model above over an instance
FieldDef flag: group ownership, initialization-phase authority and load
identity affect binding and execution, not just storage placement. Review the
complete record/clause/operation allocation alongside native and generic
metadata rather than reserving unrelated numbers independently.

Amend the metadata static-field/access sections, logical-record inventory,
registry, instruction operations and conformance cases together after approval.
The source trigger rules need no amendment unless that review explicitly
recommends one. Exact attribute identities, physical records, clause codecs
and runtime lifecycle ABI remain blockers; see the
[compiler conformance inventory](../implementation/conformance-gaps.md).
