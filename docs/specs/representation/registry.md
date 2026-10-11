# TRRM allocation and shape registry

## 1. Authority and schema identity

The [machine-readable registry](trrm-1.registry.json) is normative for numeric
allocations, namespace roles, operand sequences, widths, enum domains and the
stated structural/executable status. The [metadata chapter](../il-metadata.md)
owns framing, row semantics and binding; the [instruction chapter](../il-instruction-set.md)
owns Body and operation semantics. Their displayed tables are projections of
this registry, not independent allocation authorities. A disagreement is a
specification defect, not permission to choose a meaning.
The [clause chapter](contract-clauses.md) owns assigned clause applicability,
occurrence keys, absence and complete cross-row obligations.
The [reader-trust chapter](reader-trust.md) owns access-scoped checks,
trusted indexes and table-implied reference interpretation.
The [canonicality chapter](encoding-canonicality.md) owns minimal writer
widths/maps, equivalent ordinary noncanonical admission and explicit audits.
The [resource-accounting chapter](resource-accounting.md) owns scoped capacity,
deterministic structural/audit recipes and explicit exhaustion; numerical
portable minima remain unassigned.
The [format-version chapter](format-versioning.md) owns stable released
schema assignments and exact out-of-band draft agreement.
The [semantic-manifest chapter](semantic-manifests.md) owns Module editions,
exact coherent capability requirements, complete closure and physical ranges.

`trrm/1-draft-unified-3` identifies this registry edition for the development
format pair `(1, 0)`. It is not an on-wire Feature ID or profile selector.
Independently exchanged drafts require exact agreement on this registry,
its semantic catalog **and** governing chapters. Matching `(1, 0)`, an edition
label or the registry's digest alone does not prove agreement on all semantics.
Draft/preview schemas can change while retaining `(1, 0)` without a
compatibility obligation. Once a schema is released, its format pair has one
stable meaning; this registry assigns no released schema or new header field.

There is one catalog. The earlier generic and floating draft catalogs are
not alternate reader modes or aliases in this schema. A producer emits the
current assignments; a consumer must not infer a retired catalog from operand
types, plausible instruction lengths, implementation enums or chronology.
An explicitly selected old development schema may be inspected by a separate
tool; those bytes are not current-schema input.

## 2. Allocation policy and growth

Primary opcode allocation uses the following family capacity. A band reserves
space for related **semantics**, not one instruction per API, scalar width,
vector lane or nominal operator. An operation's type matrix and subcode select
its exact behavior; a source-selected nominal operator remains a call.

| Primary bytes | Role and capacity |
|---|---|
| `01..0F` | Constants, Copy values, selection, presence and aggregate values |
| `10..1F` | Integer/Boolean operations, bit operations and checked arithmetic/conversions |
| `20..2F` | Places, storage transitions, indexing and views |
| `30..3F` | Selected calls and future invocation/static-target forms |
| `40..4F` | Floating/decimal operation families and conversions |
| `50..5F` | Future fixed/scalable vector and mask operation families |
| `60..6F` | Future explicit memory, atomic, fence and logical-root operations |
| `70..7E` | Future operation families |
| `7F` | Reserved for a possible extended operation envelope; no assigned decoding |
| `80..EE` | Terminators; the registry assigns the present control/failure/exit shapes |
| `EF` | Reserved for a possible extended terminator envelope; no assigned decoding |
| `F0` | Width prefix only |
| `00`, `F1..FF` | Invalid instruction starts |

Unassigned and reserved primary values reject. `7F` and `EF` are not active
escapes, and `F0` never selects an operation family or nested prefix. Future
extended keys need a complete bounded envelope, role, operand sequence,
canonical writer and admission rules before allocation. No experimental
opcode or private interpretation is admitted.

The current catalog has 34 operation shapes and six terminator shapes.
Ten place/indexing shapes, two aggregate shapes and `Leave` are inspectable
only; their numbers do not supply missing ownership, backing or unwind
contracts. The seven floating shapes use `float32`/`float64` where explicitly
admitted, not every floating identity in metadata. Vector, atomic, root and
decimal-operation bands allocate no instructions yet.

## 3. Machine-readable shape conventions

Each instruction's `operands` is an ordered sequence of `[name, form]` pairs.
An enum form names a fixed `u8` domain, not a width-controlled operand.
Instruction `ref`, `count` and `data` widths are independent; the common
envelope uses one byte in each used class. Fixed metadata references always
use little-endian `u32`, regardless of that envelope.

| Operand form | Encoded field |
|---|---|
| `definition`, `value`, `place-value`, `type`, `place`, `block`, `region` | Unsigned Body-local reference width; the form fixes its namespace |
| `definition?`, `value?` | Same width, with zero permitted only by the unit-result rule |
| `method` | Nonzero local reference; zero instead consumes the following fixed-`u32` artifact-wide MethodDef row index, high byte zero, for the inspectable packed-call shape; no DefId selector |
| `field` | Fixed-`u32` typed FieldDef reference |
| `bits`, `signed` | Data width; unsigned bit pattern or two's-complement signed integer respectively |
| `enum:<domain>` | Fixed `u8`; exact membership in the named domain |
| `arguments` | Count-width argument count followed by that many value-reference-width IDs |
| `edge` | Block-reference-width target followed by `arguments` |
| `cases` | Count-width case count followed by that many `(bits, edge)` pairs |
| `regions` | Count-width exit count followed by that many region-reference-width IDs |

Lists and conditional fields are not opaque payloads: their minimum sizes,
counts and full extents must be checked before traversal or allocation.
Used width classes follow the fields actually present after list/conditional
expansion; an empty case list has no data fields and requires data code zero.
The registry fixes operand boundaries even for an inspectable-only shape.
It does not waive type, state, contract, target or flow requirements.

Metadata rows similarly list their ordered fields and widths. Table-specific
`u32` fields, including `str`/`blob`, are row indexes with reserved-zero high
byte, not table selectors, inline text or native pointers. Only genuinely
polymorphic definition fields use a DefId discriminator. Clause kind fixes
the Contract owner's table; slot selector 1 fixes Body and has a zero
section-kind field.
`local` widths in Body structures are separate from instruction widths.
All reference kinds, section kinds, atomic/family codes and clause IDs retain
their distinct namespaces even when their numbers coincide.

## 4. Admission status and namespace boundaries

`executable` means that the bounded `scalar-copy-failfast` profile admits
the shape **subject to** its complete type, call, region, effect and runtime
restrictions. Governing edition `1` selects this baseline; the profile name
is descriptive, not a separate profile selector or an assigned Feature row.
`shape-only` means bounded inspection is possible but execution rejects.
`provisional` retains an allocation without an admitted semantic use; it
also rejects before affected binding/execution. Other unlisted or reserved
values reject rather than defaulting to a known form.

Metadata assignment is structural recognition, not a claim of executable
support for every type or declaration. In particular:

- The 26 atomic and 16 family slots do not admit every family membership,
  mask, representation, constant codec or operation.
- Module's required `governingRuleEdition` is a full-width semantic identity,
  not a row reference. Edition `1` fixes the bounded baseline. Feature has an
  exact two-word manifest codec, but no capability IDs are registered; current
  producers omit its table and use empty ranges.
- Effects has a bounded executable MethodDef use and
  [closed assigned-clause applicability](contract-clauses.md). Output publication,
  unassigned transfer/dependency variants, cleanup and generic witnesses
  retain their separate completion gates.
- Numeric gaps below section kind `8000` are not extensions. Unknown kinds
  `8000..FFFF` may be skipped only under the registered nonsemantic policy;
  they cannot supply required semantics or change core rows.
- A section kind is `u16`; a definition-reference kind is `u8`. A future
  referenceable table above `00FF` needs an explicit mapping/indirection
  decision, not truncation into an existing kind.

No reserved type escape, definition kind, header bit, fourth Body form,
wider constant payload, static identity or semantic optional section becomes
usable by appearing in a proposal.

Status constrains producer validity and affected use, not the amount a trusted
consumer must scan. Unknown/unsupported accessed forms reject; a producer
must declare complete required semantics. Trust permits relying on indexes
without a preliminary all-Body form scan. Whole-artifact structural validation
and semantic/canonicality audits are separately scoped claims.

## 5. Re-encoding and conformance

Canonical instruction widths follow each used class's decoded maximum;
Body-local widths follow the selected form's complete local inventory.
Type maps follow first logical use without unused entries. Ordinary
nonminimal widths, consistently referenced reordered maps and distinct valid
unused entries remain accepted; canonicality is an explicitly scoped audit,
not a preliminary trusted-import scan or a universal Body-form choice.

Re-encoding uses decoded instruction roles and operand schemas, never
search-and-replace of byte values. Opcode values can occur in literals,
counts, enum values, local IDs, metadata references, presence masks and
offset arrays. Those occurrences remain in their own namespaces.

The present primary reallocation changes opcode octets, not operand widths
or instruction lengths. In unchanged example Bodies, definition, block and
terminator offsets therefore stay valid. A transformation that changes
length, maps, IDs, CFG, contracts or module context must rebuild every
affected index/reference and remap or drop optional SourceMap data according
to its meaning-preservation policy.

[Required cases and complete artifact vectors](../conformance/il-registry.md)
cover allocation uniqueness, family capacity, conditional operands, all
three Body forms, type/result restrictions and rewrites. They identify the
current schema explicitly and distinguish independent structural/finite
assessments from arbitrary flow, producer truth, native behavior, performance
and unassigned feature/codec conformance. The WIP compiler has not been
silently migrated by changing the specification.
