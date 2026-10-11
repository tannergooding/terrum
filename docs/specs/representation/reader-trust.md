# Reader trust and schema-owned references

## 1. Valid artifacts and admitted producers

A producer must satisfy the complete selected schema, including every row,
Body, index, declared requirement and published contract it emits. This
obligation is distinct from how much an individual consumer examines.
Source checking establishes authored facts; a merger is responsible for
preserving them in its output.

An executable consumer admits body facts through its explicit producer-trust
policy or an adequate semantic audit. Trust is an environment decision, not
a Module identity, signature, checksum, metadata bit or claim in the artifact.
Signing can establish provenance under a distribution policy; it does not
prove semantic truth. Without adequate trust or checking, the consumer must
decline safe execution. An incomplete or exhausted audit is not approval.

A trusted consumer may use recorded ownership, CFG, definition, liveness,
effect and requirement facts without rediscovering them. It need not scan
every Body, block, instruction stream, row or offset array before selective
access, import, specialization or pruning. There is no separate all-Body
scan or semantic-audit requirement for the bounded scalar profile.

Trust does not authorize an out-of-bounds access, a different schema,
unsupported required behavior or an incompatible retained binding. The
checks naturally encountered while decoding, looking up, binding or
lowering an object remain required. A contradiction already exposed by
those accesses rejects; trust is not permission to ignore known invalidity.
Unexamined bytes and facts are not thereby certified as valid.

## 2. Required checks at the access boundary

The following partition applies to structural inspectors, executable
consumers and rewriters according to the objects they actually use:

| Access or operation | Required check | Not required solely to perform this access |
|---|---|---|
| Establish an artifact's section map | Exact schema, header, supplied slice length, directory extent, kinds/policies, counts, strides and nonoverlapping section extents | Decode every section payload or scan padding bytes |
| Read a fixed row | Table-implied row bound and checked stride/offset arithmetic; decode that row's fields and reserved values | Visit preceding or unrelated rows |
| Read a variable row | Bound the index storage and the selected adjacent offset pair against the section's data; consume the selected row's exact extent | Scan all other offsets or payloads for global agreement |
| Follow a typed reference, range or Body-local ID | Its schema-owned namespace, zero rule, count and checked extent; decode the selected target as needed | Search other tables for a matching row or reconstruct the entire graph |
| Interpret text as a name, path or key | Bound its actual String row and validate the text and use-specific rules | Decode unrelated String rows or optional literal data |
| Read a Contract range | Decode every selected clause, check its owner/subject, exact extent, key, masks and supported meaning | Search unrelated declaration ranges for orphaned or shared rows |
| Admit a selected Module and retained dependencies | Exact declared rules, complete requirements, supported representation/runtime and selected identities | Scan bodies to infer undeclared features or machine-call dependencies |
| Bind or replace a retained declaration | Complete directed satisfaction of the actual retained expectation, including types, modes, state, phases and target/runtime | Re-prove the provider's body or accept a same-shaped substitute |
| Ignore an optional nonsemantic section | Its directory policy and bounded section extent | Decode its payload or reference-slot prelude |
| Preserve or rewrite reference-slotted data | Decode and remap every used slot; preserve target meaning or drop the optional section | Treat opaque payload bytes as hidden references |

Checking a range is not permission for wrapping arithmetic or allocating from
an unchecked count. A selected variable row can be bounded by its two offsets
without establishing monotonicity of the entire offset array. If both
neighboring records are read and their exposed facts conflict, reject the
conflict. Full offset, ownership, uniqueness, unused-row and padding
validation belongs to a producer or a tool claiming that broader validation
scope; an ordinary trusted lookup need not perform it.

The entire artifact uses one decoder. The consumer establishes complete
semantic support for selected modules and their retained dependency closure
before affected binding, evaluation, lowering or execution. Those declared
requirements include uncalled retained declarations and non-call
dependencies. Trust allows relying on their completeness, not choosing a
maximum revision, the current host default or anticipated execution path.
Executing initialization, getters or cleanup to discover requirements is
not admission.

An unknown required directory kind rejects while establishing the section
map. An unknown opcode, qualifier, type, clause or other form rejects when
its record is decoded or used; a consumer must not execute or guess it.
A trusted selective consumer need not search unvisited payloads for such
forms. A producer hiding an undeclared required form is nonconforming;
absence of detection is not a reader claim that the hidden form is valid.
A whole-artifact structural validator does examine every schema-required
payload within its stated scope and rejects such a form even when uncalled.

## 3. Body indexes and selective import

Recorded block, terminator and definition locations may drive direct seeks.
Before each seek, bound the containing Body, required index storage, selected
block/definition interval and target offset with checked arithmetic. Decode
the selected record under its registered role and operand schema. An indexed
terminator must decode completely to that block's end. A definition lookup
checks the result ID, kind and type that its selected instruction actually
exposes against the requested descriptor. These are ordinary local
access checks, not a prerequisite instruction-stream scan.

The producer guarantees that the terminator follows the counted operations,
that definition offsets point to actual operation starts, and that all
definition/use/liveness indexes agree with the complete stream. A trusted
consumer need not re-establish those facts. In particular, an in-range
offset can point into an earlier operand without that being discoverable
from the selected bytes alone. Such a producer assertion is false, but a
trusted reader is not required to scan preceding instructions to detect it.

When a consumer already decodes a complete block, it consumes exactly its
counted operations and one terminator, checks the full block extent, and
compares the definition/terminator locations exposed by that traversal with
the indexes it uses. A known mismatch rejects. Decoding one selected
instruction does not require reconstructing dominance, edge reachability,
global liveness, zero-use definitions or unrelated operation boundaries.

Compact linear Bodies omit indexes and derive their one-block facts by
decoding the stream. That access inherently requires the linear traversal;
it does not impose a preliminary scan on indexed forms. Compact indexed
presence lists and general-form indexes retain their own bounded layouts.
No form enables unassigned operations, roots, ownership or exception codecs.

An untrusted consumer must establish the facts on which execution or pruning
depends, audit an adequate larger scope, or decline. Merely bounding an
asserted deadness index does not prove code unreachable. An audit must
include dependencies needed for its claims, not assume a false summary in
order to justify excluding the code that would contradict it.

## 4. Calls, effects and unsafe authorization

Importing a Call naturally accesses its selected method and signature.
Check its declared argument/result types, modes, logical arity and required
caller effects in that context. Existing unit-result and no-return rules
remain exact. A trusted checked NoThrow or DoesNotReturn contract may be
used without auditing the callee's implementation.

A caller-unsafe target requires the current call site to have the specified
unsafe authorization. In the bounded profile, inspect the current block's
recorded enclosing-region chain and the selected callee contract; bound
region IDs, validate accessed entries and enforce the permitted region
roles. A safe root context does not acquire authority from producer trust,
a callee's name or its own unsafe-caller requirement.

The producer establishes the truth of region membership and the complete
entry/exit/edge discipline. A trusted consumer need not rebuild the CFG or
audit every unrelated Body to check one call. If the consumer traverses an
edge whose exposed source/target records violate the required discipline,
it rejects that known violation. An adequate untrusted audit establishes
the enclosing-scope and control-flow facts on which authorization relies.

Trust shifts responsibility for those facts; it does not change source
validity, permit reference/ownership misuse or make a callable's
caller-unsafe requirement authorize unsafe operations inside its own body.
NoThrow, purity, no-return, termination and ownership remain distinct.

## 5. Schema-owned reference namespaces

A table-specific field encodes a row index, not a table selector. Its table
is fixed by the field or the registered enclosing form. Current metadata
row indexes occupy a little-endian `u32`, with row `1..0xFF_FFFF` and zero
only where explicitly nullable; the high byte is reserved zero. It is not
an optional DefId kind. An attempted explicit table kind rejects rather
than redirecting the lookup, even if it names the intended table.

| Field or form | Schema-owned target |
|---|---|
| `MethodDef.signature`, `bodyRow`, `ownerTypeDef` | MethodSig, Body and TypeDef respectively |
| TypeSpec nominal/import/formal operands | TypeDef, Import and GenericParam respectively |
| Body type-map word, `fieldDefRow` | TypeSpec and FieldDef respectively |
| Packed `Call` escape after `m=0` | Artifact-wide MethodDef row; no DefId kind byte |
| Effects/OutputState `ownerRow` | MethodDef, selected by clause kind |
| Cleanup `ownerRow`, `cleanupMethodDef` | TypeDef and MethodDef respectively |
| Reference-slot selector 1 | Body; its section-kind field is reserved zero |

The eight-byte Contract header is `u32 ownerRow, u16 clauseKind,
u16 flags=0`. A semantically assigned clause fixes its owner table;
Effects/OutputState cannot select a TypeDef owner, and Cleanup cannot
select MethodDef. Reserved Transfer/Dependency shapes have no admitted
owner application or semantic reference interpretation until their
variants and owner rules are assigned. Their shape does not activate
an owner selector.

A genuinely polymorphic declaration field, such as Export, Import's expected
definition or GenericParam's TypeDef/MethodDef owner, retains a DefId:
`(definitionKind << 24) | row`. It admits only that field's specified kinds,
not every table. Similarly a generic selector-0 reference slot needs its
registered target-section kind because it can actually refer to different
core tables. A selector or field with one fixed target does not.

Names, layouts, same numeric row IDs and coincident payload bytes cannot
change any reference namespace. Remapping operates on decoded reference
fields using their schema-owned table/context, not arbitrary matching
octets. Typed references and polymorphic DefIds remain fixed `u32`;
Body-local instruction widths and row-255/256 transitions are independent.

## 6. Validation claims, transformations and reuse

A tool states whether it performed selected access checks, whole-artifact
structural validation, a supported semantic audit or canonicality checking.
None of these names implies the others or arbitrary-body certification.
Canonical writer rules remain obligations even when a trusted consumer
does not scan for unused maps, nonminimal widths or padding.
[Encoding canonicality](encoding-canonicality.md) permits equivalent
nonminimal used widths and consistently referenced non-first-use/unused
map entries during ordinary reading; only an explicit scoped canonicality
audit rejects those departures. Invalid reserved fields, duplicates, values
or exposed contradictions remain invalid.

A rewriter establishes meaning-preserving changes and repairs affected
references, indexes, requirements and ownership facts. It may rely on
admitted producer facts for unchanged input; it is responsible for the
output facts it changes or emits. Removing code does not discharge remaining
type, layout, constant, witness, cleanup or load/code dependencies.

Reuse depends on the actual unchanged bytes/context, selected schema,
required rules, targets, bindings and admitted trust/audit basis. A reused
decision covers only the checked or trusted scope; it cannot become
whole-artifact certification. Resource exhaustion explicitly declines the
requested check or operation, not success through skipped required work.
[Resource accounting](resource-accounting.md) supplies selected operation/root
scopes, deterministic work and live-state recipes without a preliminary
trusted scan. Portable minimum supported envelopes are intended; their
numerical values remain unassigned.

These rules define logical `reader-trust/1` and
`schema-owned-references/1`. The current exact registry edition is
`trrm/1-draft-unified-3`, still using format pair `(1, 0)` and out-of-band
schema agreement. Encoded DefIds at the now-table-specific packed-call or
Contract-owner sites, and a nonzero section kind for selector 1, are not
aliases in this schema. No Feature ID, physical trust flag, new opcode,
unassigned variant or additional execution profile is activated.
