# Terrum IL and metadata working specification

> [!IMPORTANT]
> Proposal-only representation draft, not an accepted wire format or current
> revision list. Consult [current implementation](../implementation.md) and
> [roadmap](../roadmap.md) for actual support and migration boundaries.

**Status:** pre-v1 working specification. `TRRM` is the selected identifier
for the evolving Terrum semantic-IL/module format. There is no separate
`LMOD` family. The main text gives one concrete replacement-layout candidate
for review; Appendix A records the current positional revision-32 bytes.
Where coherent implementation work needs a concrete choice, this candidate
may serve as a provisional implementation basis, with retrospective correction
allowed before v1. That does not finalize every field or optional choice,
claim implementation support, or promise format stability; record actual
support separately and leave explicitly open alternatives unresolved.

Semantic revision 32 was the highest revision implemented when this draft was
written; the current repository has since introduced higher, feature-selected
revisions. Calling that earlier repository state `0.32` is descriptive only:
it does not select
on-wire major/minor fields, assign a release, or imply `1.0`. The format may
change incompatibly before v1. Support for historical revisions, including 10–32, and feature-dependent
writer selection are temporary repository implementation
facts scheduled for consolidation before v1, not a production compatibility
obligation. No general future compatibility promise is made here.

## 1. Purpose, authority, and trust boundary

The semantic IL is a compact binary representation of the language and
ecosystem: types, declarations, contracts, and operations. It is intended as
input to metadata binding, reflection/tooling, linking, and AOT/JIT code
generation without requiring source recompilation. It is not merely a
serialization of a compiler's private object graph or a verifier certificate.
Its role is comparable to CIL's ecosystem role, without adopting CIL, CLI, or
CLR formats or semantics.

The language specification remains authoritative for semantic rules:
[language](../language.md), [lifetimes](../lifetimes.md),
[conversions](../conversions.md), [type families](../type-families.md),
[data layout and ABI](../data-layout-and-abi.md), and the
[memory model](../memory-model.md).
This draft specifies how a future artifact can carry and validate those
contracts; it does not reopen or replace them.

**Accepted binary trust boundary:** source compilation checks authored bodies
against their declared contracts. A binary consumer checks safe decoding,
structure, references, binding, contract compatibility, and caller use, then
trusts declared contracts for external dependencies rather than requiring
dependency-body re-proof. Callers remain responsible for imported preconditions,
access, ownership, effects, and other declared obligations. Deliberately
dishonest dependency metadata is not ruled out by mandatory whole-program
proof; malformed bytes, unresolved references, incompatible contracts, and
unsupported semantic forms are not trusted. Optional deeper body audit is not
a v1 promise. Signing or provenance does not establish semantic correctness.

Source-language revision, semantic artifact revision, and physical native
profile/container revision are distinct concepts. The format must not conflate
them accidentally. The present native containers and PE images remain separate
artifacts; none is a second name for `TRRM`.

## 2. Format identity and revision scope

| Topic | Direction | Status |
|---|---|---|
| Identifier | Use `TRRM` for the evolving Terrum semantic-IL/module format; do not introduce a distinct `LMOD` family. | Accepted |
| Pre-v1 evolution | Allow intentional breaking changes while the design solidifies; no current historical reader range becomes a release promise. | Accepted |
| `0.32` | Optional prose label for the repository state whose highest implemented semantic revision is 32. It is not a wire revision encoding or a `1.0` assignment. | Clarification |
| Wire revision field | Choose whether and how the future format identifies its encoding/schema and its semantic requirements. | Open |
| Native target profile | Keep target ABI/profile requirements separate from source and artifact revisions; decide their exact metadata representation with the schema. | Principle accepted; representation open |

The signature is the four ASCII bytes `TRRM` (`54 52 52 4D`). Header layout
and revision numbering are separate choices; Appendix A describes the current
encoding after that signature.

## 3. TRRM replacement layout candidate

**Proposal under review, not the current byte layout or an adopted schema.**
This is one cohesive candidate for evolving the existing `TRRM` format from
its current positional revision-32 layout. It makes concrete framing and
record-shape choices so they can be evaluated together. The exact field
selection, revision values, record-kind values, opcode values, feature policy,
and numeric resource ceilings remain open. In particular, the candidate's
revision fields do not encode repository label `0.32`; their values remain
unassigned until the wire design is adopted.

### 3.1 Header and directory

Candidate byte order is little-endian. The header is fixed at 32 bytes in this
layout; the directory follows the header, and sections follow the directory
in ascending artifact-relative offset order. There is one directory entry per
section kind. No compression is used in the initial candidate, simplifying bounds
checking and random access. This candidate's 32-byte header length is
unrelated to the historical semantic revision 32; the actual
revision-32 prefix is documented in Appendix A.

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

The candidate uses fixed-width section-directory entries and 64-bit
artifact-relative offsets with 32-bit local table indexes. This favors bounded
parsing over compact varints. The exact widths and organization remain review
choices; no size ceiling follows from the `u64` offset width.

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

### 3.2 Metadata records and relationships

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
accepted source semantics are linked in §1 rather than restated here.

### 3.3 Identity and contract invariants

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

## 4. Body, instruction, and region structure

The body format must represent typed values and addressable places distinctly,
and make control flow, exceptional flow, cleanup, initialization, ownership,
and managed-root obligations verifiable. Source evaluation order, effects, and
failure behavior come from the language contract; a wire representation must
preserve them.

The candidate body organization uses typed basic blocks with explicit
arguments/edges and a region structure for lexical loans/views, cleanup,
unsafe scopes, exception handling, and managed-root obligations. Source spans
may be carried as optional diagnostic data; they do not determine execution.
It replaces the current nested-tree body organization in the candidate.

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
the false case. Current executable modules retain bounded bool/`out` behavior
until the coordinated `init ref<T>` migration.

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

## 5. Limits, counts, ranges, and accounting

The specification must define each limit in terms of what it bounds, when it
is checked, and whether it is per-record, per-body, or per-artifact. At
minimum, review limits for:

| Resource | Required accounting definition | Numeric target |
|---|---|---|
| Total bytes and framed payload | Count the complete `TRRM` slice, including headers, optional data, padding and nested encoded payload; separately referenced resource bytes and enclosing PE/ELF bytes are outside it. Check before allocation. | Open |
| Record/section and table counts | Per-kind and aggregate counts, including duplicate detection and references. | Open |
| String/byte sequences | Encoded byte length, encoding validation, and any decoded-memory accounting. | Open |
| Lists, paths, and ranges | Element count, index bounds, arithmetic overflow, range containment, and valid empty forms. | Open |
| Nesting and graph depth | Which recursive structures count and whether the bound is per body or artifact. | Open |
| Decode and verification work | Deterministic structural/work units, including repeated analysis; no wall-clock deadlines. | Open |
| Temporary memory | Whether expansion, decompression, and intermediate tables are budgeted. | Open |

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
  checked `start + length`; negative signed offsets, lengths or counts, wraparound, and extents
  beyond the containing artifact/section reject.
- Decoding and verification budgets count deterministic structural/work
  units, not wall-clock time. Required checks that exceed their configured
  budget fail explicitly rather than skipping validation.

The `u32` count field and `u64` offsets describe representable encodings, not
practical acceptance ceilings. Numerical ceilings and exact accounting remain
open. Current `.trm` measurements are listed separately in Appendix A and do
not supply the candidate's limits.

## 6. Validation, unknown data, and references

Define validation in stages so a reader cannot execute a partially accepted
artifact:

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
preserving optional sections when rewriting remain open. The current positional
revision-32 layout has no such directory/skip mechanism and rejects unknown
revisions or body tags.

The format is not an authorization system. Checksums can detect accidental
changes only if their exact scope is defined; signatures, provenance, and
distribution policy are separate concerns. None can bypass structural,
reference, contract, or caller-use validation.

## 7. Worked semantic relationship (not wire bytes)

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

The target source direction uses `Create(init value)` at an `init ref<T>`
call site. Current module artifacts still carry their bounded bool/`out`
behavior until coordinated implementation migration. A future metadata
representation must preserve declared initialization facts without introducing
a separate output-publication transaction or reading vacant storage.

## 8. Remaining format review

The following are concrete specification questions, not reopening accepted
language policy:

1. Define the byte header, framing, encoding revision and any separately
   represented semantic/runtime requirements.
2. Select metadata record relationships, type forms, declaration identities,
   import/export keys, and exact callable-contract representation.
3. Select body control-flow and region representation; specify instruction
   semantics, exception/cleanup edges, roots, and the verifier boundary.
4. Define counts, length/range checks, nesting and work accounting, allocation
   limits, and deterministic/canonical rules where required.
5. Define unknown and reserved values, forward-compatibility behavior, and
   which unsupported semantic additions must reject before lowering.
6. Exercise independent readers/writers, malformed artifacts, binding and
   contract mismatches, resource boundaries, and semantically equivalent
   execution across conforming backends before adopting bytes.

No future record IDs, opcodes, numerical limits, version fields, optional-data
rules, module identity scheme, or feature-negotiation protocol are assigned by
this draft.

## Appendix A. Historical repository byte layout (semantic revision 32)

This appendix is implementation evidence only. It is not the normative
pre-v1 layout, and it does not preserve historical reader support as a future
requirement. The relevant implementation is
`sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs`, especially
`Magic`, `Write`, `Read`, `ReadModuleHeader`, `PutFunction`,
`PutStatement`, `PutExpression`, and their readers.

### A.1 Header and top-level order

Revision 32 writes fields in this order; integers are little-endian. There is
no directory, total-length field, section table, integrity checksum,
cryptographic signature, or compression layer in the semantic artifact itself.

| Field | Current encoding |
|---|---|
| Magic | `i32` value `0x4D525254`, bytes `54 52 52 4D` (`TRRM`) |
| Semantic revision | `i32`, value `32` for this layout |
| Native profile | `PutString`: `i32` byte count followed by strict UTF-8 bytes |
| Consumer kind | Canonical boolean byte (`0` or `1`) |
| Runtime policy | One byte (`FailFast` or `CheckedExceptions`) |
| Entry point | `i32`; consumer entry indexes its function table, producer uses `-1` |
| Value-structure count | `i32`, followed by structure records |
| Class count | `i32`, followed by class records (present from revision 19 onward) |
| Imported-structure count | `i32`, followed by imported type-name strings |
| Function count | `i32`, followed by function records |

The top-level order is positional. `PutString` uses a signed 32-bit byte
length, not a character count or null terminator. A span is two `i32` values:
start and length. A body statement is a tag byte and span, followed by
tag-specific operands. An expression is a tag byte, primitive-type `i32`,
span, and tag-specific operands.

### A.2 Revision-32 record shapes

| Record | Current fields and relationships |
|---|---|
| Value structure | Name string; size `i32`; private, internal, unique booleans; cleanup-present boolean and optional cleanup-method name; field count; ordered fields. |
| Structure field | Name string; type code `i32`; offset and size `i32`; private, internal, mutable booleans. |
| Class | Name string; size `i32`; internal and mutable booleans; field count; ordered fields of the same field shape. |
| Imported structure | Type name string; resolved against local declarations/layouts during module binding. |
| Function declaration | Name string; declaration-derived contract string; import/export boolean; body-present boolean; ownership-transfer boolean. Producer exports and consumer-owned definitions carry bodies; consumer imports carry declarations without bodies. |
| Function body | Name string; return type code; constructor, visibility, static, `NoThrow`, `DoesNotReturn`, cleanup flags; intrinsic byte; class-constructor flag and factory index; optional declaring-structure name; parameter count; local/slot count; source span; ordered variable records; nested statement body. |
| Variable/slot | Name string; type code; mutable and `out` booleans; source span. Slot indexes in the body are local to the function. |

Declaration contracts are currently serialized as delimiter-separated
strings derived from declaration name, return and parameter types/modes,
function/constructor kind, visibility, static/instance owner, mutable receiver,
cleanup and effect/return promises. Ownership transfer is also recorded
separately. A replacement must preserve the signature and transfer contract;
it may strengthen `NoThrow` and `DoesNotReturn`, not weaken them. Exact
contract-string syntax is an implementation encoding, not the future
record schema.

Function calls and cleanup targets in bodies are converted to symbolic names
before serialization and resolved against the module's declaration table
while reading/linking. Type codes referring to value structures and class
records are local to the artifact and remapped when combining artifacts.
Neither local type positions nor body slot indexes are global identities.
Generic specializations in the present format are closed compilation-local
functions; there is no public generic-template or witness ABI.

### A.3 Existing body tags

These byte values are present revision-32 persistence tags, not proposed
normative IL opcodes or source tokens.

| Statement tag | Current node |
|---:|---|
| 1–9 | block; local; assignment; expression statement; `if`; `while`; `require`; return; field assignment |
| 10–17 | `try`/`finally`; unsafe block; `try`/`catch`; drop; owner declaration; constructed-field cleanup; lexical view scope; view write |
| 18–20 | `break`; `continue`; `while` with iterator |

| Expression tag | Current node |
|---:|---|
| 1–9 | constant; read; unary; binary; direct call; cast; field read; `out` argument; move |
| 10–16 | receiver loan; readonly field loan; mutable receiver loan; view length; view read; null; optional lift |
| 17–20 | null test; `Some` test; readonly projection; mutable field loan |

The current body is a nested typed tree, not the candidate future typed
basic-block/region representation. Revision gates control which later tags and
fields a historical reader accepts. Calls serialize the selected symbolic
callee name and arguments; the reader resolves the declaration and the
semantic verifier checks the reconstructed body before execution.

### A.4 Current reader limits and rejection behavior

| Implementation bound | Current value |
|---|---:|
| Entire semantic artifact | 4 MiB |
| Function records | 1,024 |
| Value-structure records | 1,024 |
| Class records | 256 |
| Function slots and value-structure fields | 256 each |
| Class fields | 16 |
| Body graph-step counter | 100,000 |
| Recursive body/path depth | 128 |
| Individual strict UTF-8 string | 1,024 bytes |

The body graph-step counter is shared across function bodies in one artifact;
it charges serialized nodes and selected nested lists/paths. The semantic
verifier has a separate 100,000-visit work bound. These are implementation
limits, not selected `TRRM` limits or the source-language proof floor.

The current reader rejects unsupported revisions and node tags, inputs below
its 17-byte minimum or above the artifact limit, malformed or truncated fields,
noncanonical boolean bytes, invalid strict UTF-8, out-of-range counts, invalid
entry points, incompatible profile/policy combinations,
unsupported revision-feature combinations, unresolved or contradictory
declarations, and trailing bytes. It then checks reference/binding and
contract rules; linked bodies pass the semantic verifier. The `.trm` artifact
does not authenticate producer identity or bytes.

## Appendix B. Repository history and related artifacts

The current reader accepts historical semantic revisions 10–32. The writer
selects a revision based on the retained program features rather than always
writing the highest revision. This is useful repository migration/test
behavior, not a future format rule. The historical variants are scheduled for
removal/consolidation before v1; until that cleanup lands, documentation may
describe them as present implementation facts.

The native profile label (`native-10` through `native-20`) is independent of
semantic revision and source syntax. Native containers are also separate:
`TNPK` for a single provider/consumer package, `TNSP` for native module sets,
and `TNGP` for native dependency graphs. Their image, import, digest, and
unwind behavior belongs to
[native modules](../native.md), [module sets](../module-sets.md),
and [native graphs](../native-graph.md);
these containers are not the `TRRM` semantic-IL encoding.

**Canonical direction:** [language](../language.md),
[roadmap](../roadmap.md#language-semantic-il-and-metadata-specification),
and [implementation boundary](../implementation.md).
The [grammar](terrum-grammar-spec.md) and
[core-library](terrum-core-library-reference.md) companions are separate
technical drafts; links do not adopt their detailed proposals.
