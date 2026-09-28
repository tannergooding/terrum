# Terrum semantic IL and metadata

> [!IMPORTANT]
> Working proposal, not an accepted byte schema or a claim of compiler support.
> See [implementation](../implementation.md) for the current executable
> boundary. Terms such as "must" below describe the proposed contract where
> its representation has been specified; open choices are identified explicitly.

The organization takes only a structural cue from ECMA-335 (6th edition):
metadata tables and physical/container layout are treated separately here,
as in Partition II §§22–25, while coded operations have their own
[instruction proposal](terrum-il-instruction-set.md), analogous in scope to
Partition III. Terrum does not adopt CIL instructions, CLI metadata tokens,
CLI profiles, or PE as its required container. This is an engineering draft,
not an attempt to reproduce a standards-body specification.

## Definitions and conventions

| Term | Meaning in this document |
|---|---|
| `TRRM` artifact | One complete, bounded byte slice carrying semantic IL and metadata. It may be a standalone file or embedded in another container. Its extent excludes the enclosing PE/ELF file and separately referenced resource payloads. |
| Artifact-relative offset | A zero-based byte position from the first byte of the `TRRM` signature. An extent `[offset, offset + length)` is end-exclusive and must be contained in the supplied artifact slice before an embedded reader adds any container base. |
| Directory and section | The candidate directory indexes one metadata table or body stream per section kind. Each section occupies one contiguous artifact-relative byte extent. |
| Record | One `u32` byte length followed by exactly that many payload bytes. A table's section count gives its number of records; record lengths do not include their own four-byte prefix. |
| Local definition ID (`DefId`) | A `(table kind, one-based row)` pair naming a declaration only within one artifact. Zero is reserved for a nullable row reference, not a definition. Its numeric value is not a stable cross-artifact identity. |
| Producer identity and export key | The selected dependency and published declaration identity used together to resolve an external reference. Exact identity/key encodings and authentication policy are open. |
| Declared contract | Published type, callable, ownership/lifetime, access, effect and requirement facts against which consumers bind and check use. A contract is not inferred from a dependency body or source spelling. |
| Body | Typed semantic operations and control flow for an executable declaration, distinct from declaration metadata. |
| Source revision, artifact revision, native profile | Independent axes for source syntax/semantics, `TRRM` encoding and requirements, and target/runtime ABI assumptions. The enclosing container format is separate. One revision number does not imply another. |
| Required feature and optional data | A required semantic feature must be understood before binding or execution. An optional section can be skipped only if its registered kind is nonsemantic and explicitly safe to ignore. |

Candidate field tables use `u8`, `u16`, `u32` and `u64` for fixed-width unsigned
little-endian integers. Byte offsets are artifact-relative; lengths are byte
counts unless stated otherwise. All numeric assignments in this proposal are
**candidate assignments**, not values in the current repository artifact.
Numerical resource ceilings and format-revision policy are not assigned here.

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
are assigned **provisionally** below as format `(0, 1)` for the initial
candidate only. This is not semantic artifact revision 1, language version
0.1, or a release label. Its initial reader accepts that exact pair; future
revision and feature-negotiation policy remain open. Target ABI/profile
requirements remain separate from source and artifact revisions. Pre-v1
format changes may be incompatible; this proposal does not promise a
historical reader range or a frozen wire version.

## Physical framing and embedding (proposed)

The following framing, table IDs, row layouts and initial opcodes form one
proposed byte schema, **not the current encoding**. Future feature negotiation
and numerical resource ceilings remain open. Implementers
cannot infer that the current revision-35 reader accepts these bytes.

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
| 4 | Format major | `u16` | `0` in this candidate |
| 6 | Format minor | `u16` | `1` in this candidate |
| 8 | Header length | `u32` | `32` for this candidate layout |
| 12 | Directory entry count | `u32` | `19`, or `20` with optional SourceMap |
| 16 | Total artifact length | `u64` | Exact byte length of the supplied `TRRM` slice |
| 24 | Directory offset | `u64` | `32` in this candidate |

Each directory entry is 24 bytes:

| Entry offset | Field | Width | Candidate meaning |
|---:|---|---:|---|
| 0 | Section kind | `u16` | One of the table assignments below |
| 2 | Flags | `u16` | `0x0001` required or `0x0000` optional; all other bits reject |
| 4 | Record count | `u32` | Number of records encoded in the section |
| 8 | Section offset | `u64` | Artifact-relative section start |
| 16 | Section byte length | `u64` | Exact section extent |

Every assigned metadata table and Body section uses the required flag;
SourceMap alone uses the optional flag. An unknown section's optional bit is
not permission to ignore it without a nonsemantic registry entry. The entry
does not add a minimum-reader-version field. This candidate rejects any
header format pair other than `(0, 1)`; a different reader compatibility
policy requires a format revision, not a silent interpretation of minor
versions. A separate minimum-reader field remains an alternative for review.

Candidate framing invariants:

- `headerLength` is 32 and `directoryOffset` is 32; the directory has exactly
  `entryCount * 24` bytes, with checked
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

## Metadata logical tables and row semantics (proposed)

### Table stream and token encoding

Each registered metadata section is a table. It contains exactly the directory
entry's `recordCount` successive records, each encoded as `u32 payloadLength`
followed by its payload. An empty table has a zero count and zero length.
Fixed-layout rows reject a payload of any other size; variable rows validate
their embedded lengths and consume the entire payload. Row numbers are assigned
in byte order, starting at 1. A same-table reference is a `u32` row; a `DefId`
is `u16 tableKind, u32 row` (six bytes). Unless explicitly nullable, row zero
rejects. A contiguous range is `u32 firstRow, u32 count`; its empty encoding
is `(0, 0)`, otherwise `firstRow` is nonzero and the checked last row is in
that table. Ranges for one owner must not overlap or include another owner's
rows. There are no pointer-sized fields or implicit string terminators.

| Kind (`u16`) | Table | Row payload and role |
|---:|---|---|
| `0x0001` | Module | Singleton identity, runtime requirement and entry point |
| `0x0002` | String | Variable UTF-8 bytes for names and keys |
| `0x0003` | Blob | Variable opaque bytes, interpreted only by their referring field |
| `0x0004` | Feature | Required semantic feature identifiers |
| `0x0010` | ModuleRef | Selected external producer identity |
| `0x0011` | TypeSpec | Complete type reference or constructed type |
| `0x0012` | TypeArg | Ordered arguments for one constructed TypeSpec |
| `0x0013` | TypeDef | Nominal type declaration |
| `0x0014` | FieldDef | Field declaration and optional fixed offset |
| `0x0015` | Layout | Per-target layout commitment |
| `0x0016` | MethodDef | Callable declaration and optional body |
| `0x0017` | MethodSig | Return, receiver and ordered parameter range |
| `0x0018` | Param | Complete parameter type and passing mode |
| `0x0019` | GenericParam | Declared formal, owner and ordered constraints |
| `0x001A` | Constraint | One definition-site formal requirement |
| `0x001B` | Contract | One published semantic clause on a declaration |
| `0x001C` | Import | Producer/key binding to a local expected declaration |
| `0x001D` | Export | Stable key assigned to a local definition |
| `0x0020` | Body | One length-delimited coded body for a MethodDef |
| `0x7F00` | SourceMap | Optional nonsemantic body-to-source mapping |

All sections except `SourceMap` are required **kinds** (possibly empty), not
necessarily required to contain a row. `Module` has exactly one row. In format `(0, 1)`, the Feature table is empty;
later features require a new explicitly understood format. Unknown
section kinds are not self-declared safe to skip. The numeric gaps are
reserved, not available for vendor-specific execution semantics. A required
feature is identified by `u32` in `Feature`; its registry, minimum revision
rule and negotiation policy still require a decision. Until then, a producer
cannot emit an unrecognized semantic form by merely writing a feature number.
`SourceMap` rows are uninterpreted byte records that a reader may discard;
they cannot change any semantic table, instruction or binding.

The next tables show **row payloads**, excluding the common `u32` record-length
prefix. `str` and `blob` mean one-based rows in their respective tables;
zero is allowed only where a column says `?`. String rows contain their exact
UTF-8 bytes (no BOM, NUL terminator or normalization); readers reject invalid
UTF-8 and compare names/keys by decoded ordinal scalar sequence. Blob rows
are bytes only: each use must name its interpretation and validate its
contents. Duplicate strings or blobs are not a second declaration identity.

| Table | Ordered row payload |
|---|---|
| Module | `u32 identityBlob, u32 targetRequirement, u32 runtimePolicy, u32 entryMethod?, u32 moduleFlags, u32 featureFirst, u32 featureCount` |
| Feature | `u32 featureId, u32 minimumRuleRevision` |
| ModuleRef | `u32 producerIdentityBlob, u32 targetRequirement, u32 runtimePolicy` |
| TypeSpec | `u8 form, u8 qualifiers, u16 reserved=0, u32 a, u32 b, u32 c` |
| TypeArg | `u32 ownerTypeSpec, u32 ordinal, u32 argumentTypeSpec` |
| TypeDef | `str namespace, str name, u8 kind, u8 visibility, u8 layoutKind, u8 flags, u32 fieldFirst, u32 fieldCount, u32 genericFirst, u32 genericCount, u32 layoutRow?, u32 contractFirst, u32 contractCount` |
| FieldDef | `u32 ownerTypeDef, str name, u32 typeSpec, u32 flags, u32 byteOffset` |
| Layout | `u32 ownerTypeDef, u32 targetRequirement, u32 size, u32 alignment, u32 pack, u32 flags` |
| MethodDef | `u32 ownerTypeDef?, str name, u32 signature, u32 flags, u32 contractFirst, u32 contractCount, u32 bodyRow?` |
| MethodSig | `u32 resultTypeSpec, u32 receiverTypeSpec?, u32 paramFirst, u32 paramCount, u32 genericFirst, u32 genericCount` |
| Param | `u32 ownerMethodSig, u32 ordinal, u32 typeSpec, u8 mode, u8 flags, u16 reserved=0` |
| GenericParam | `DefId owner, u32 ordinal, str name, u32 constraintFirst, u32 constraintCount` |
| Constraint | `u32 ownerGenericParam, u16 kind, u16 flags, u32 targetTypeSpec, u32 witnessRef?` |
| Import | `u32 moduleRef, str exportKey, DefId expectedLocalDefinition` |
| Export | `DefId localDefinition, str exportKey` |

`Module.identityBlob` and `ModuleRef.producerIdentityBlob` are opaque byte
identities, not hashes or proof of authorship. `runtimePolicy` candidate values
are `0` fail-fast and `1` checked exceptions; `targetRequirement` values are
`0` portable semantic (no native assumption) and `1` Windows x64 native.
Other targets and nonzero module flags still need assignments. The entry
method, if present, is a local
MethodDef row. Imports identify **one** selected module and key; their expected
local TypeDef or MethodDef (including its referenced signature, layout and
contracts) is the complete consumer-side contract. The expected definition has
no body. Export keys are unique within a producer; imports are unique by
`(moduleRef, exportKey, expectedLocalDefinition)`, and duplicate conflicting
bindings reject. MethodDef `bodyRow` is nonzero only for locally authored
implementations. An exported body may be private; the export is the contract,
not permission to inspect or trust private implementation facts.

For the initial TypeSpec encoding, `form` assignments are `0x01` primitive
(`a=primitiveCode`), `0x02` local TypeDef (`a=typeDefRow`), `0x03` imported
TypeDef (`a=importRow`), `0x04` formal (`a=genericParamRow`), `0x05` constructed
(`a=genericTypeSpec, b=typeArgFirst, c=typeArgCount`), `0x06` optional,
`0x07` ref, `0x08` ptr, `0x09` box, `0x0A` val, `0x0B` own (each `a=innerTypeSpec`).
Unused `a/b/c` are zero. Nesting references in TypeSpec must point to an
earlier TypeSpec row; nominal declarations may refer to each other through
their TypeSpec rows, subject to storage-cycle validation. TypeArg rows for
one constructed form are contiguous with ordinals `0..count-1`; arity,
qualification and constraints are checked against the selected nominal
declaration. Qualifier bit `0x01` denotes `write` where that type form permits
it; other bits reject. Candidate primitive codes: `0x00 void`, `0x01 bool`,
`0x02 char16`, `0x03 int32`, `0x04 uint32`, `0x05 int64`, `0x06 uint64`.
Other primitives require explicit assignments and feature support, not a
reader's private enum ordinals.

TypeDef kinds are `0x01` struct, `0x02` class, `0x03` interface; layout kinds
are `0x00` Auto, `0x01` Sequential, `0x02` Union and `0x03` Transparent.
Visibility is `0x00` private, `0x01` internal, `0x02` public. For the initial
profile, `MethodDef.flags` uses bit `0x01` for public and `0x02` for internal
(neither means private; both together reject). FieldDef flags use the same
visibility bits plus `0x04` for mutable. All other definition flags and
call-classification masks need assignments before use; unknown bits reject.
Only a public MethodDef can be exported in the initial profile. `Layout` is
required only for a published fixed representation;
private `Auto` has no layout row. `FieldDef.byteOffset` is `0xFFFFFFFF` when
no fixed offset is published. A fixed layout validates size, alignment,
overlap, field offsets and ABI classification against its target requirement;
`Transparent` retains nominal identity while using its one carrier's direct
call classification. Equal layout is not implicit conversion.

Param modes are `0x00` by-value, `0x01` `out`, `0x02` `init ref`,
`0x03` readonly `ref`, `0x04` writable `ref`. Only by-value mode `0x00`
is executable in the initial [coded-body profile](terrum-il-instruction-set.md);
other modes have no instruction encoding yet. `MethodSig` records
declaration order; argument labels and receiver access need registered
contract fields rather than name-based inference. GenericParam, Constraint
and constructed TypeSpec rows are reserved for a separately specified generic
feature; their row shapes alone do not make generic bodies executable or
exportable.
Parameter and generic-formal ordinals are zero-based and contiguous within
their owning MethodSig or declaration. Unused range fields are `(0, 0)`.

### Semantic contracts and stable binding

Each Contract row has `DefId owner, u16 clauseKind, u16 flags=0, u32 dataLength,
byte[dataLength]`. The outer row length must equal `14 + dataLength`.
Contracts for a TypeDef or MethodDef occupy its contiguous `contractFirst/count`
range; duplicates of a singleton clause reject. The initial candidate clause
assignments are:

| Clause | `data` payload | Meaning |
|---:|---|---|
| `0x0001` Effects | `u32 mask` | Bit `0x01` promises `NoThrow`, bit `0x02` promises `DoesNotReturn`, bit `0x04` requires an unsafe caller; other bits reject. |
| `0x0002` OutputState | `u32 parameterOrdinal, u8 onTrue, u8 onFalse, u8 onExceptional, u8 reserved=0` | A declared result-correlated output state (`0` vacant, `1` fully initialized). Legal only for a Boolean result and `out`/`init ref` parameter. |
| `0x0003` Transfer | `u32 parameterOrdinal, u8 transferMode, u8 cleanupMode, u16 reserved=0` | A selected owned-parameter transfer/cleanup obligation; mode IDs beyond the initial profile are not yet assigned. |
| `0x0004` Dependency | `u8 resultKind, u8 sourceKind, u16 flags, u32 resultOrdinal, u32 sourceOrdinal` | A declared result/view backing, root, or lifetime dependency; kind/flag registry remains to be assigned. |

The bounded profile can encode effects and Boolean output-state correlation.
It does **not** encode unspecified Transfer/Dependency variants, pre/postcondition
expression trees, arbitrary witnesses or public managed-handle ABI. Those
need assigned values, required feature IDs and verifier rules before emission;
their reserved rows must not be ignored. Effects do not follow from an opcode
name, a method name, or an implementation body. A source-selected declaration
and closed witness bindings remain the call target; linking validates that
binding rather than rerunning overload resolution.

### Identity and contract invariants

Local indexes are compact references within one artifact, not stable identities
across rebuilds. External references resolve through a selected producer
identity and exported declaration key. A digest is not automatically a
nominal identity. The identity algorithms and stable-key allocation remain
open.

An adopted schema must preserve the complete published contract rather than
infer it from names, layout, or private bodies. The candidate's bounded clause
set is not yet sufficient to publish every accepted language contract:

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

### Row-assignment example

For the `return 42` [coded-body example](terrum-il-instruction-set.md#encoded-example-and-invalid-forms),
these are the relevant **candidate** row references, not current repository
bytes or stable identifiers:

| Table row | Payload values in column order |
|---|---|
| Blob 1 | UTF-8 bytes `Example` (opaque module identity) |
| String 1 | UTF-8 bytes `Answer` (method name and export key) |
| Module 1 | `identityBlob=1, targetRequirement=1, runtimePolicy=0, entryMethod=1, moduleFlags=0, featureFirst=0, featureCount=0` |
| TypeSpec 1 | `form=0x01, qualifiers=0, reserved=0, a=0x03` (`int32`), `b=0, c=0` |
| MethodSig 1 | `resultTypeSpec=1, receiverTypeSpec=0, paramFirst=0, paramCount=0, genericFirst=0, genericCount=0` |
| MethodDef 1 | `ownerTypeDef=0, name=1, signature=1, flags=1` (public), `contractFirst=0, contractCount=0, bodyRow=1` |
| Export 1 | `DefId=(0x0016, 1), exportKey=1` |
| Body 1 | `ownerMethod=1`, followed by the body bytes specified in the companion proposal |

For example, TypeSpec 1 is exactly `10 00 00 00 01 00 00 00 03 00 00 00
00 00 00 00 00 00 00 00`: four bytes of payload length followed by its
16-byte payload. Neither the Module row nor an export authorizes a caller to
skip bounds, contract or body validation.

## Body table and coded-instruction boundary

Each Body row uses the table's common `u32 payloadLength` prefix and carries
exactly the coded bytes defined in the
[instruction-set proposal](terrum-il-instruction-set.md#coded-body-layout).
Its one-based row matches the MethodDef `bodyRow` that owns it. Every Body row
must be owned by exactly one local MethodDef, and an imported declaration has
no body. Body-to-method identity and type references use the metadata tables
above; block/value/region IDs are **body-local**, not metadata `DefId`s.
Bodies cannot redefine a published signature, effect or result contract.

The instruction proposal assigns an initial executable subset with exact
record layout, opcodes and edge payloads. Exception regions, general loans,
owner cleanup, rooted managed values, witness calls and correlated outputs
must receive additional feature-gated encodings before they can be emitted.
They are not silently modeled as ordinary branch instructions. An ordinary
binary consumer need not re-prove dependency bodies, but it must reject
malformed or unsupported coded bodies it accepts for execution.

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

## Example: conditional output contract (beyond the initial coded profile)

Suppose MethodDef row 1 returns `bool` and its fourth parameter (ordinal 3)
has `Param.mode=0x02` (`init ref`). If it promises initialization exactly
when the Boolean result is true, its Contract row is:

```text
16 00 00 00  16 00 01 00 00 00  02 00 00 00
08 00 00 00  03 00 00 00  01 00 00 00
```

The first four bytes say the row has a 22-byte payload; the next six name
MethodDef row 1 (`DefId=(0x0016, 1)`), then clause `0x0002`, flags zero,
eight data bytes, parameter ordinal 3, and states `(1, 0, 0)` for true,
false, and exceptional departure. A consumer can compare this declared
promise with a producer without reading the output to discover whether it
was initialized. The [coded instruction proposal](terrum-il-instruction-set.md)
does **not** yet encode the output place and publication operations, so this
metadata row alone does not make such a body executable.

## Unspecified schema details

The candidate defines relationships and validation principles, but not the
following byte-level contracts:

| Surface | Not yet specified |
|---|---|
| Encoding and revisions | Adoption of candidate widths, assigned revision values, feature/minimum-rule registry and compatibility rules. |
| Declarations | Stable identity and export-key allocation, resource references, generic/witness exportability, nonzero flags, richer qualifier/contract clauses and their validation. Initial table kinds, rows and bounded clause tags are assigned above. |
| Executable bodies | Beyond the [assigned initial opcodes and block encoding](terrum-il-instruction-set.md): output places, exceptional handlers/cleanup, loans, roots, witness bindings and their precise verifier rules. |
| Limits | Per-record and per-artifact sizes, nesting and work budgets, allocation limits and canonical encoding requirements. |
| Unknown data | Registry, reserved values, feature negotiation and safe handling or preservation of optional nonsemantic sections. |
