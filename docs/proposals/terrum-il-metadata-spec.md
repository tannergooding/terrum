# Terrum semantic IL and metadata

> [!IMPORTANT]
> Working proposal, not an accepted byte schema or a claim of compiler support.
> See [implementation](../implementation.md) for the current executable
> boundary. Terms such as "must" below describe the proposed contract where
> its representation has been specified; open choices are identified explicitly.

## Definitions and conventions

| Term | Meaning in this document |
|---|---|
| `TRRM` artifact | One complete, bounded byte slice carrying semantic IL and metadata. It may be a standalone file or embedded in another container. Its extent excludes the enclosing PE/ELF file and separately referenced resource payloads. |
| Artifact-relative offset | A zero-based byte position from the first byte of the `TRRM` signature. An extent `[offset, offset + length)` is end-exclusive and must be contained in the supplied artifact slice before an embedded reader adds any container base. |
| Directory and section | The candidate directory indexes sections by kind. Each section occupies one contiguous artifact-relative byte extent and contains records of its declared kind. |
| Record | A schema-identified unit within a section. Any declared byte extent and nested counts must be consumed exactly; record kinds and encodings remain to be assigned. |
| Local definition ID (`DefId`) | An index naming a declaration only within one artifact. Its numeric value is not a stable cross-artifact identity. |
| Producer identity and export key | The selected dependency and published declaration identity used together to resolve an external reference. Exact identity/key encodings and authentication policy are open. |
| Declared contract | Published type, callable, ownership/lifetime, access, effect and requirement facts against which consumers bind and check use. A contract is not inferred from a dependency body or source spelling. |
| Body | Typed semantic operations and control flow for an executable declaration, distinct from declaration metadata. |
| Source revision, artifact revision, native profile | Independent axes for source syntax/semantics, `TRRM` encoding and requirements, and target/runtime ABI assumptions. The enclosing container format is separate. One revision number does not imply another. |
| Required feature and optional data | A required semantic feature must be understood before binding or execution. An optional section can be skipped only if its registered kind is nonsemantic and explicitly safe to ignore. |

Candidate field tables use `u16`, `u32` and `u64` for fixed-width unsigned
little-endian integers. Byte offsets are artifact-relative; lengths are byte
counts unless stated otherwise. Field widths, numeric limits, encodings not
shown in a table, and the format-revision policy remain proposed, not assigned
wire contracts.

## Scope and conformance boundary

The semantic IL is a compact binary representation of the language and
ecosystem: types, declarations, contracts, and operations. It is intended as
input to metadata binding, reflection/tooling, linking, and AOT/JIT code
generation without requiring source recompilation. It is not merely a
serialization of a compiler's private object graph or a verifier certificate.

Source compilation checks authored bodies against their declared contracts.
A binary consumer checks safe decoding, structure, references, binding,
contract compatibility, and caller use, then
trusts declared contracts for external dependencies rather than requiring
dependency-body re-proof. Callers remain responsible for imported preconditions,
access, ownership, effects, and other declared obligations. Deliberately
dishonest dependency metadata is not ruled out by mandatory whole-program
proof; malformed bytes, unresolved references, incompatible contracts, and
unsupported semantic forms are not trusted. Optional deeper body audit is not
a required dependency-consumption step.

The accepted language contracts remain authoritative for their semantics:
[language](../language.md), [lifetimes](../lifetimes.md),
[conversions](../conversions.md), [type families](../type-families.md),
[data layout and ABI](../data-layout-and-abi.md), and the
[memory model](../memory-model.md). This document describes their proposed
binary representation and validation; it does not redefine their rules.

| Role | Required checks |
|---|---|
| Source compiler and writer | Check authored bodies against declared language contracts; encode complete, well-formed declarations and supported bodies. |
| Binary reader | Bound and decode the supplied artifact; validate its structure, required features, references and declared contracts before accepting it. |
| Linker or executable consumer | Resolve selected dependencies, check compatibility and caller use, and reject unsupported semantics before lowering or execution. It need not re-prove dependency bodies. |

## Format identity and revision scope

`TRRM` is the format identifier; there is no separate `LMOD` family. Its four
ASCII signature bytes are `54 52 52 4D`. The wire fields for encoding revision
and semantic requirements are not settled. Target ABI/profile requirements
remain separate from source and artifact revisions, with their exact
representation open. Pre-v1 format changes may be incompatible; this proposal
does not promise a historical reader range or a frozen wire version.

## Candidate artifact layout

The following framing and record shapes form one proposed byte schema, not the
current encoding. Exact field selection, revision values, record-kind values,
opcodes, feature policy and numeric resource ceilings remain open.

### Header and directory

Candidate byte order is little-endian. The header is fixed at 32 bytes in this
layout; the directory follows the header, and sections follow the directory
in ascending artifact-relative offset order. There is one directory entry per
section kind. No compression is used in the initial candidate, simplifying bounds
checking and random access.

The artifact is the supplied `TRRM` byte slice, whether stored alone or
embedded in a PE/ELF container. Its total length and offsets describe that
slice, not the containing file. Large resources may be held separately and
referenced by metadata; their payload bytes are not part of `totalLength`.
Resource identity, resolution, integrity and limits remain schema decisions.

| Offset | Field | Width | Candidate meaning |
|---:|---|---:|---|
| 0 | Magic | 4 bytes | ASCII `TRRM` |
| 4 | Format major | `u16` | Byte-schema compatibility axis; value unassigned |
| 6 | Format minor | `u16` | Byte-schema evolution axis; value unassigned |
| 8 | Header length | `u32` | `32` for this candidate layout |
| 12 | Directory entry count | `u32` | Number of fixed-size directory entries |
| 16 | Total artifact length | `u64` | Exact byte length of the supplied `TRRM` slice |
| 24 | Directory offset | `u64` | Artifact-relative start of the directory |

Each directory entry is 24 bytes:

| Entry offset | Field | Width | Candidate meaning |
|---:|---|---:|---|
| 0 | Section kind | `u16` | Section registry key; numeric assignments open |
| 2 | Flags | `u16` | Required/optional classification; reserved bits must be zero |
| 4 | Record count | `u32` | Number of records encoded in the section |
| 8 | Section offset | `u64` | Artifact-relative section start |
| 16 | Section byte length | `u64` | Exact section extent |

The entry may mark a section required or optional, but does not add a
minimum-reader-version field. A separate minimum-reader field and alternative
versioning schemes remain alternatives for review, not mandatory compatibility
policy. The major/minor labels above are candidate field names; they do not by
themselves define reader support, roll-forward, or compatibility rules. A
required feature manifest may be a required section; its record shape and
relation to format revision remain open.

Candidate framing invariants:

- `headerLength` equals the selected header size; the directory begins at or
  after the header and has exactly `entryCount * 24` bytes, with checked
  multiplication and checked end-offset arithmetic.
- `totalLength` equals the supplied `TRRM` slice length. Header, directory, and
  every section lie wholly within `[0, totalLength)`. A section begins at or
  after the directory end. Embedded readers translate to container positions
  only after validating the slice boundary and the relative offset.
- Directory entries are strictly ordered by section kind, so duplicate kinds
  are invalid. Section extents do not overlap the header, directory, or one
  another. Any alignment requirement is defined by the section schema and
  checked on its offset.
- Every non-padding byte belongs to the header, directory, or a section.
  Permitted alignment gaps contain zero bytes; unclaimed trailing payload is
  rejected.
- Section parsers consume exactly their declared extents and record counts.
  All offsets, lengths, additions, multiplications, counts, and index
  conversions use checked arithmetic before slicing, allocation, or iteration.
- A zero-record section has zero records and may have zero byte length only
  where its section schema permits an empty section; nonempty sections must
  contain at least one complete record.

The candidate uses fixed-width directory entries, 64-bit artifact-relative
offsets and 32-bit local table indexes. Exact widths and organization remain
open; representable `u64` offsets do not set a reader's acceptance ceiling.

Candidate directory contents are organized by these section roles; their
kind values and exact record encodings are unassigned:

| Section role | Candidate cardinality/status |
|---|---|
| Requirements/manifest | One required section describing format requirements and target/runtime assumptions |
| Types | One required section, possibly empty, for type declarations and references |
| Members | One required section for declarations and contracts |
| Dependencies | One required section, possibly empty, for imports and exports |
| Bodies | One required section, possibly empty, for executable definitions |
| Source map/debug data | Optional section; cannot alter identity, contracts, binding, or execution |

Whether the first five roles are separate sections or records in fewer
sections remains open; this table makes the candidate graph concrete without
assigning section IDs.

### Metadata records and relationships

The directory separates record classes; the following names and shapes are
conceptual schema pseudocode for the candidate. They are not numeric record
assignments. Local `DefId`/indexes identify declarations only within one
artifact; external references use a producer identity plus an exported
declaration key.

```text
Module {
  module_identity;
  target_requirements;
  required_features[];
  optional_features[];
  types[];
  members[];
  imports[];
  exports[];
  bodies[];
  source_map?;
}

TypeDef {
  local_def_id;
  export_key?;
  qualified_name;
  visibility;
  kind;
  generic_formals[];
  representation_contract;
  field_contracts[];
}

TypeRef =
    Primitive(primitive_code)
  | Local(local_def_id)
  | External(producer_identity, export_key)
  | Qualified(base_type_ref, explicit_qualifiers)
  | Formal(owner_def_id, ordinal)
  | Constructed(generic_type_ref, ordered_type_arguments[]);

MemberDef {
  local_def_id;
  export_key?;
  owner_type_ref?;
  name;
  visibility;
  callable_kind;
  generic_formals[];
  ordered_parameters[]; // mode and complete type
  result_type;
  receiver_authority?;
  effects;
  ownership_lifetime_and_initialization_contracts;
  selected_witness_requirements[];
  body_ref?;
}

Import {
  producer_identity;
  exported_declaration_key;
  expected_callable_or_type_contract;
  required_type_origins[];
}

Body {
  member_local_def_id;
  local_types[];
  regions[];
  blocks[];
  selected_call_and_witness_bindings[];
  source_map_ref?;
}
```

These record relationships preserve nominal identity independently of source
spelling, layout, local indexes, and content digest. Imported declarations
resolve to one selected producer declaration and are checked against their
required contract. Calls and witness uses record source binding; a linker
does not rerun overload resolution. The candidate leaves exact module/export
identity, record separation, feature-manifest contents, generic-template and
witness exportability, and public layout disclosure for review.

Type and callable records carry semantic facts, not inferred body summaries:
full supported qualifiers and optionality; parameter/reference modes;
receiver authority; representation and call classification separately;
ownership, move/transfer, cleanup, root and lifetime/backing requirements;
initialization facts and correlations; effects, failures, and unsafe caller
obligations. Public language-level signatures and declared layout, access,
ownership, and effect contracts remain compatibility obligations. Their
accepted source semantics are linked above rather than restated here.

### Identity and contract invariants

Local indexes are compact references within one artifact, not stable identities
across rebuilds. External references resolve through a selected producer
identity and exported declaration key. A digest is not automatically a
nominal identity. The identity algorithms and stable-key allocation remain
open.

The records preserve the complete published contract rather than infer it
from names, layout, or private bodies:

- Nominal identity remains distinct from equal spelling, bits, or layout;
  representation and call classification are separate.
- Type references preserve complete supported qualification and optionality.
  A readonly reference to a handle slot does not attenuate the handle's own
  authority.
- Callable records preserve parameter/reference modes, receiver authority,
  ownership and transfer, cleanup, roots, lifetime/backing, initialization
  facts/correlations, effects, failure, and caller-unsafe obligations.
- A source-selected declaration and its witness bindings remain the call
  target. Linking validates that target against the dependency; it does not
  redo overload resolution.
- Published language signatures and declared layout/access/ownership/effect
  contracts remain compatibility obligations, distinct from FFI ABI and
  serialization formats.

Exact stable export keys, generic-template and witness exportability, the
compatibility relation, and the representation commitments exposed by
visibility remain under review.

## Body, instruction, and region structure

The body format must represent typed values and addressable places distinctly,
and make control flow, exceptional flow, cleanup, initialization, ownership,
and managed-root obligations verifiable. Source evaluation order, effects, and
failure behavior come from the language contract; a wire representation must
preserve them.

The proposed body organization uses typed basic blocks with explicit
arguments/edges and a region structure for lexical loans/views, cleanup,
unsafe scopes, exception handling, and managed-root obligations. Source spans
may be carried as optional diagnostic data; they do not determine execution.

```text
Region {
  region_id;
  parent_region?;
  kind;                 // function, lexical scope, view/loan, unsafe, handler...
  entry_block;
  normal_exit?;
  exceptional_exit?;
  cleanup_blocks_in_order[];
}

Block {
  block_id;
  parameters[];         // typed block arguments
  operations[];         // typed values and places
  terminator;           // branch, conditional branch, return, exceptional exit...
}

```

Region/block names are schema pseudocode; they assign no byte IDs. Candidate
branch edges identify a target block and supply arguments matching that
block's parameter count and types. Definitions must dominate uses in the
control-flow graph; exceptional edges and exits must satisfy region and
cleanup ordering. Exact region kinds, operation families, opcode numbers,
block/region ID widths, and whether every body uses this organization remain
review decisions.

Illustrative control flow for a conditional initialization contract:

```text
function_region:
  entry(%useLeft: bool, %left: int32, %right: int32,
        %chosen: vacant init-place<int32>):
    branch_if %useLeft, success(%left), failure()

  success(%selected: int32):
    initialize %chosen, copy %selected
    return true

  failure():
    return false // %chosen remains vacant on this edge.
```

`branch_if`, `initialize`, and `return` are semantic pseudocode, not assigned
opcodes. The artifact must preserve the declared relation between the Boolean
result and output initialization; it must not read vacant storage to discover
the false case.

When a source compiler checks an authored body, or a separately selected
audit checks an available body, verification must cover the applicable
language obligations, including:

1. Type and declaration references, generic substitutions, selected witness
   bindings, owners, signatures, and effect promises resolve unambiguously.
2. Definitions and uses, block entry/edge arity and type, terminators,
   exceptional successors, region nesting, and cleanup ordering are valid.
3. Definite initialization, vacancy, moves, loans, exclusivity, roots, and
   ownership are tracked across normal, failing, and exceptional paths.
4. Access qualification, capabilities, backing/lifetime, and valid return or
   capture dependencies are preserved; a concrete caller cannot grant a
   generic definition undeclared authority.
5. Potential failures/effects satisfy the caller contract; optimizations
   preserve observable required effects and checks.
6. Unsupported semantic forms, invalid references, malformed bodies, and
   analysis/resource exhaustion fail explicitly rather than being treated as
   no-ops or silently downgraded.

Ordinary binary dependency consumption does not require re-proving external
dependency bodies. This separation is intentional: safe structural decoding,
binding, contract compatibility, and caller-use validation are mandatory;
whole-dependency implementation proof is not. The source-language
[portable proof floor](../language-contract-review.md#4-a-reproducible-proof-and-evaluation-foundation)
is a separate compile-time contract and must not be conflated with binary
reader work budgets.

## Limits, counts, ranges, and accounting

Each limit must state what it bounds, when it is checked, and whether it is
per-record, per-body, or per-artifact. The bounded reader accounts for:

| Resource | Required accounting definition |
|---|---|
| Total bytes and framed payload | Count the complete `TRRM` slice, including headers, optional data, padding and nested encoded payload; separately referenced resource bytes and enclosing PE/ELF bytes are outside it. Check before allocation. |
| Record/section and table counts | Per-kind and aggregate counts, including duplicate detection and references. |
| String/byte sequences | Encoded byte length, encoding validation, and any decoded-memory accounting. |
| Lists, paths, and ranges | Element count, index bounds, arithmetic overflow, range containment, and valid empty forms. |
| Nesting and graph depth | Which recursive structures count and whether the bound is per body or artifact. |
| Decode and verification work | Deterministic structural/work units, including repeated analysis; no wall-clock deadlines. |
| Temporary memory | Whether expansion, decompression, and intermediate tables are budgeted. |

Limits must make malformed or adversarial input bounded before expensive
allocation or recursive processing. A structural parse budget and a semantic
verifier budget are distinct unless a future design explicitly proves
otherwise. Candidate structural invariants are:

- Before allocating a directory or record array, validate its declared count
  against the containing byte extent, its encoded element width where fixed,
  the remaining artifact budget, and checked arithmetic.
- Every length-delimited record and list is consumed wholly within its parent
  section/record; the parser consumes exactly its declared length and count.
- Each local index is in range for the specific table and refers to the
  expected record kind. Each external reference resolves to one declared
  dependency/key pair. Duplicate identities and duplicate singleton records
  reject.
- Every offset/length pair uses checked `offset + length`; every range uses
  checked `start + length`. Negative signed offsets, lengths or counts,
  wraparound, and extents beyond the containing artifact/section reject.
- Decoding and verification budgets count deterministic structural/work
  units, not wall-clock time. Required checks that exceed their configured
  budget fail explicitly rather than skipping validation.

The `u32` count field and `u64` offsets describe representable encodings, not
practical acceptance ceilings. Numerical ceilings and exact accounting remain
open.

## Validation, unknown data, and references

Readers validate in stages before lowering or executing an artifact:

1. Check signature, revision/requirements identity, framing, lengths, ranges,
   canonical encodings where required, and resource limits.
2. Validate record shapes, uniqueness rules, reserved values, counts, local
   indexes, and all references.
3. Resolve external declarations and verify the imported contract against the
   selected producer before lowering or execution.
4. Validate caller use against the imported contract. Check authored bodies
   during source compilation; check dependency bodies only when source
   compilation or an explicitly selected audit has access and authority to do
   so.
5. Reject unsupported required semantics before native code generation.

Candidate unknown-data rules are:

- Reject an unsupported format revision before interpreting sections.
- Reject a section whose kind is unknown and whose required flag is set.
- Reject unknown flag bits, nonzero reserved fields, duplicate section kinds,
  and malformed directory ordering.
- Skip an unknown optional section only if the section registry explicitly
  classifies it as nonsemantic and safe to ignore; otherwise reject it. A
  producer cannot make a safety-relevant section ignorable by setting a flag.
- Reject unknown required features and all unknown instructions, types,
  constraints, qualifiers, or callable-contract forms before binding/lowering.
  A feature declaration cannot authorize behavior the reader does not
  implement.

The exact registry, reserved values, feature declaration, and policy for
preserving optional sections when rewriting remain open.

Structural, reference, contract, and caller-use validation are required
independently of any separately defined integrity check.

## Example: conditional output binding

The following names are symbolic and illustrative; they assign no numeric
IDs, record tags, opcodes, or source grammar:

```text
module Selection
  type bool
  type int32
  member choose:
    parameters: useLeft: bool, left: int32, right: int32,
                chosen: init ref<int32>
    result: bool
    contract: chosen is initialized exactly on successful return
    body: selected implementation body
  export: choose

module Consumer
  import Selection.choose with its declared callable contract
  body: call the selected import; validate caller use against that contract
```

The metadata must preserve declared initialization facts without introducing
a separate output-publication transaction or reading vacant storage.

## Unspecified schema details

The candidate defines relationships and validation principles, but not the
following byte-level contracts:

| Surface | Not yet specified |
|---|---|
| Encoding and revisions | Final header, wire widths, assigned revision values, semantic/runtime requirement representation and compatibility rules. |
| Declarations | Record tags and encodings, type forms, stable identity and export keys, resource references, generic/witness exportability and exact callable-contract representation. |
| Executable bodies | Operation and region tags, block encoding, exception/cleanup edges, root records and precise verifier input rules. |
| Limits | Per-record and per-artifact sizes, nesting and work budgets, allocation limits and canonical encoding requirements. |
| Unknown data | Registry, reserved values, feature negotiation and safe handling or preservation of optional nonsemantic sections. |
