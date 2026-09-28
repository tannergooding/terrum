# Terrum semantic IL and metadata

> [!IMPORTANT]
> Working proposal, not an accepted byte schema or a claim of compiler support.
> See [implementation](../implementation.md) for the current executable
> boundary. Terms such as "must" below describe the proposed contract where
> its representation has been specified; open choices are identified explicitly.

This document defines metadata tables and physical framing; the
[instruction proposal](terrum-il-instruction-set.md) defines coded operations.
The artifact can be embedded in PE or ELF but does not require either
container.

## Definitions and conventions

| Term | Meaning in this document |
|---|---|
| `TRRM` artifact | One complete, bounded byte slice carrying semantic IL and metadata. It may be a standalone file or embedded in another container. Its extent excludes the enclosing PE/ELF file and separately referenced resource payloads. |
| Artifact-relative offset | A zero-based byte position from the first byte of the `TRRM` signature. An extent `[offset, offset + length)` is end-exclusive and must be contained in the supplied artifact slice before an embedded reader adds any container base. |
| Section directory (`directory`) | The fixed-width index following the header, containing one entry per present section kind, not the sections' bytes. Each entry records its kind, flags, record count, artifact-relative offset and byte length; the entry is not part of the section it indexes. |
| Section | One contiguous artifact-relative byte extent identified by a directory entry and containing a metadata table or body stream. An absent table has no directory entry or section. |
| Row | A fixed-stride payload in a fixed table, or an extent in a variable table's offset-indexed data. Rows have one-based indexes; absent table means zero rows. |
| Local definition ID (`DefId`) | A `u32` token: high byte is a registered definition-table kind, low 24 bits are its one-based row. Zero is the only null token. Its value is not a stable cross-artifact identity. |
| Producer identity and export key | The selected dependency and published declaration identity used together to resolve an external reference. Bytes and ordinal keys are specified below; source-side key assignment and authentication are separate. |
| Declared contract | Published type, callable, ownership/lifetime, access, effect and requirement facts against which consumers bind and check use. A contract is not inferred from a dependency body or source spelling. |
| Body | Typed semantic operations and control flow for an executable declaration, distinct from declaration metadata. |
| Source revision, artifact revision, native profile | Independent axes for source syntax/semantics, `TRRM` encoding and requirements, and target/runtime ABI assumptions. The enclosing container format is separate. One revision number does not imply another. |
| Required feature and optional data | A required semantic feature must be understood before binding or execution. An optional section can be skipped only if its registered kind is nonsemantic and explicitly safe to ignore. |

Field tables use `u8`, `u16`, `u32` and `u64` for fixed-width unsigned
little-endian integers. Byte offsets are artifact-relative; lengths are byte
counts unless stated otherwise. Numeric assignments below describe this
proposed format. Numerical resource ceilings and future format-revision
policy remain unspecified.

## Scope and conformance boundary

The semantic IL is a compact binary representation of the language and
ecosystem: types, declarations, contracts, and operations. It is intended as
input to metadata binding, reflection/tooling, linking, and AOT/JIT code
generation without requiring source recompilation. It is not merely a
serialization of a compiler's private object graph or a verifier certificate.

Source compilation checks authored bodies against their declared contracts.
A binary consumer checks safe decoding, structure, supported semantics,
references, binding and contract compatibility. It may trust a compiler's
body-level facts and promises for artifacts admitted by its trust policy,
without re-proving dominance, liveness, effect/ownership flow, or every
call site. This is an explicit unsafe-like trust boundary, not a property
conferred by a byte flag in the artifact. An optional semantic audit checks
those facts for untrusted input or diagnostics; otherwise the producer is
responsible for their truth. Callers remain responsible for imported
preconditions, access, ownership and effects when they are compiled.
Malformed framing, unresolved references, incompatible contracts and
unknown required semantic forms still reject; trusting a producer does
not justify reading outside its byte extents or calling with a mismatched ABI.
An environment that cannot trust an artifact's author must audit its
semantic facts before safe execution or decline to execute it.

The accepted language contracts remain authoritative for their semantics:
[language](../language.md), [lifetimes](../lifetimes.md),
[conversions](../conversions.md), [type families](../type-families.md),
[data layout and ABI](../data-layout-and-abi.md), and the
[memory model](../memory-model.md). This document describes their proposed
binary representation and validation; it does not redefine their rules.

| Role | Required checks |
|---|---|
| Source compiler and writer | Check authored bodies against declared language contracts; encode complete, well-formed declarations and supported bodies. |
| Binary reader | Bound and decode the supplied artifact; validate framing, required features and referenced contract shapes before accepting it. |
| Linker or executable consumer | Resolve selected dependencies, check compatibility and reject unsupported semantics before lowering or execution. Trusted bodies need no repeated semantic/caller-use proof; an opt-in audit can check them. |

## Format identity and revision scope

`TRRM` is the format identifier, with ASCII signature bytes `54 52 52 4D`.
This proposal targets format 1.0, encoded as major/minor `(1, 0)`; it is not
a declaration of general availability or a compatibility promise for draft
artifacts. Before release, the schema can change without incrementing this
pair. Pre-release tools may exchange artifacts only when they share the exact
schema; matching version bytes alone do not establish that. A reader of the
proposed schema accepts exactly `(1, 0)` and rejects other pairs. Post-release
revision and feature-negotiation rules remain unspecified. Source revisions
and target ABI/profile requirements are independent of the format pair.

## Physical framing and embedding (proposed)

The following framing, table IDs and row layouts describe the proposed
encoding. Initial opcodes and bodies are defined in the
[instruction proposal](terrum-il-instruction-set.md).

### Header and directory

Candidate byte order is little-endian. The header is fixed at 32 bytes in this
layout; the directory follows the header, and present sections follow the
directory in ascending artifact-relative offset order. There is one entry
per **present** table kind. An absent known table is empty; Module cannot be
absent. No compression or whole-section transform is used, so a consumer can
bound and index rows without inflating the complete artifact.

The artifact is the supplied `TRRM` byte slice, whether stored alone or
embedded in a PE/ELF container. Its total length and offsets describe that
slice, not the containing file. Large resources may be held separately and
referenced by metadata; their payload bytes are not part of `totalLength`.
Resource identity, resolution, integrity and limits remain schema decisions.

| Offset | Field | Width | Candidate meaning |
|---:|---|---:|---|
| 0 | Magic | 4 bytes | ASCII `TRRM` |
| 4 | Format major | `u16` | `1` in this proposal |
| 6 | Format minor | `u16` | `0` in this proposal |
| 8 | Header length | `u32` | `32` for this candidate layout |
| 12 | Directory entry count | `u32` | Number of present sections, at most 22 for the assigned registry |
| 16 | Total artifact length | `u32` | Exact byte length of the supplied `TRRM` slice |
| 20 | Directory offset | `u32` | `32` in this candidate |
| 24 | Reserved | 8 bytes | All zero; readers reject nonzero bytes |

Each directory entry is 16 bytes:

| Entry offset | Field | Width | Candidate meaning |
|---:|---|---:|---|
| 0 | Section kind | `u16` | One of the table assignments below |
| 2 | Flags | `u16` | `0x0001` required or `0x0000` optional; all other bits reject |
| 4 | Record count | `u32` | Number of records encoded in the section |
| 8 | Section offset | `u32` | Artifact-relative section start |
| 12 | Section byte length | `u32` | Exact section extent |

Every present semantic table uses the required flag; SourceMap alone uses the
optional flag. Empty tables are omitted rather than encoded as empty entries.
An unknown section's optional bit is not permission to ignore it without a
nonsemantic registry entry. The entry does not add a minimum-reader-version
field, and a matching major version does not imply acceptance of a different
minor version. A separate minimum-reader field remains an alternative for
future revision policy.

Candidate framing invariants:

- `headerLength` is 32 and `directoryOffset` is 32; the directory has exactly
  `entryCount * 16` bytes, with checked
  multiplication and checked end-offset arithmetic.
- `totalLength` equals the supplied `TRRM` slice length and is at most
  `0xFFFFFFFF` bytes. Header, directory, and every section lie wholly within
  `[0, totalLength)`. Add section offsets and lengths using checked arithmetic
  wide enough for their sum before checking containment. A section begins at
  or after the directory end. Embedded readers translate to container
  positions only after validating the slice boundary and the relative offset.
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
- A present section has a positive record count. An absent registered table
  has zero rows. Fixed sections have exactly `recordCount * rowStride` bytes;
  variable sections validate their index and data extents as specified below.

The directory remains fixed-width for direct section lookup. An artifact
has 32 bytes of header plus 16 bytes **per present table** before section
payloads. The eight reserved header bytes have no assigned flags or other
semantics; writers set them to zero. Large payloads needing a wider address
space require a separately specified resource reference, not wider offsets
in ordinary metadata. Individual variable-table data is limited to `u32`
bytes by its offset index.

## Metadata logical tables and row semantics (proposed)

### Table stream and token encoding

Each present table has the directory's `recordCount` rows. **Fixed** tables
concatenate their fixed-size row payloads, without per-row lengths. **Variable**
tables start with `u32 offsets[recordCount + 1]`, followed by their data bytes.
Offsets are relative to the start of the data (after the index), start at zero,
are nondecreasing and end at the exact data length (`<= 0xFFFFFFFF`). Row `i`
occupies `[offsets[i-1], offsets[i])`, for one-based `i`. Empty String/Blob
rows are permitted; semantic variable rows must be nonempty. The checked
index size plus the final offset equals the section length. This indexes
strings, type forms, contracts and bodies directly without scanning prior
records; a one-row heap pays four additional bytes versus length-prefixing,
while larger heaps pay the same four bytes per row plus one sentinel.

A same-table reference is a one-based `u32` row. `DefId` is a four-byte `u32`
token: `(tableKind << 24) | row`, with `tableKind` the low byte of a registered
definition table (TypeDef `0x13`, FieldDef `0x14`, MethodDef `0x16`,
GenericParam `0x19`, PropertyDef `0x1E`). Its row must be in `1..0xFFFFFF`;
only token zero is
null where allowed. Other table kinds in a DefId reject. A contiguous range
is `u32 firstRow, u32 count`; its empty encoding is `(0, 0)`, otherwise
`firstRow` is nonzero and the checked last row belongs to that table.
Ranges for one owner must not overlap or include another owner's rows.
There are no pointer-sized fields or implicit string terminators.

| Kind (`u16`) | Table | Row payload and role |
|---:|---|---|
| `0x0001` | Module | Singleton identity, runtime requirement and entry point |
| `0x0002` | String | Variable UTF-8 bytes for names and keys |
| `0x0003` | Blob | Variable opaque bytes, interpreted only by their referring field |
| `0x0004` | Feature | Required semantic feature identifiers |
| `0x0010` | ModuleRef | Selected external producer identity |
| `0x0011` | TypeSpec (variable) | Complete type reference or constructed type |
| `0x0012` | TypeArg | Ordered arguments for one constructed TypeSpec |
| `0x0013` | TypeDef | Nominal type declaration |
| `0x0014` | FieldDef | Field declaration and optional fixed offset |
| `0x0015` | Layout | Per-target layout commitment |
| `0x0016` | MethodDef | Callable declaration and optional body |
| `0x0017` | MethodSig | Return, receiver and ordered parameter range |
| `0x0018` | Param | Complete parameter type and passing mode |
| `0x0019` | GenericParam | Declared formal, owner and ordered constraints |
| `0x001A` | Constraint | One definition-site formal requirement |
| `0x001B` | Contract (variable) | One published semantic clause on a declaration |
| `0x001C` | Import | Producer/key binding to a local expected declaration |
| `0x001D` | Export | Stable key assigned to a local definition |
| `0x001E` | PropertyDef | Named getter identity, distinct from its callable implementation |
| `0x001F` | SourceFile | Optional source identity for diagnostic locations |
| `0x0020` | Body (variable) | One offset-indexed coded body for a MethodDef |
| `0x7F00` | SourceMap | Optional nonsemantic body-to-source mapping |

`Module` is always present with exactly one row. Other known tables are absent
when empty, not encoded as empty directory entries. In format `(1, 0)`, the
Feature table is absent, and Module's feature range is `(0, 0)`. A new
semantic feature requires an explicit schema update, not merely a feature
number in an otherwise unsupported artifact. Unknown section kinds are not
self-declared safe to skip. The numeric gaps are
reserved, not available for vendor-specific execution semantics. A required
feature is identified by `u32` in `Feature`; its registry, minimum revision
rule and negotiation policy still require a decision. Until then, a producer
cannot emit an unrecognized semantic form by merely writing a feature number.
`SourceMap` is skippable for execution, not uninterpretable for tools. Its
fixed 24-byte rows are `u32 bodyRow, u32 blockId, u32 instructionOrdinal,
u32 sourceFileRow, u32 startUtf16, u32 lengthUtf16`.
Rows sort by body, block and ordinal and do not duplicate a location.
Ordinal zero means the first operation, `operationCount` means its terminator.
SourceFile paths are identifiers, not permissions to read a local file;
`digestBlob` is an optional exact-content digest whose algorithm requires
an assigned feature before nonzero use. Spans validate checked end arithmetic
and need not imply that source bytes are embedded. Rewriting may omit
SourceMap without changing execution or contracts; tools preserving debug
data must retain its referenced SourceFile rows.

The next tables show **row payloads**, excluding the variable section's offset
index. `str` and `blob` mean one-based rows in their respective tables;
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
| TypeSpec (variable) | `u8 form, u8 qualifiers`, followed by only the operands specified for that form below |
| TypeArg | `u32 ownerTypeSpec, u32 ordinal, u32 argumentTypeSpec` |
| TypeDef | `str namespace, str name, u8 kind, u8 visibility, u8 layoutKind, u8 flags, u32 fieldFirst, u32 fieldCount, u32 genericFirst, u32 genericCount, u32 layoutRow?, u32 contractFirst, u32 contractCount` |
| FieldDef | `u32 ownerTypeDef, str name, u32 typeSpec, u32 flags, u32 byteOffset` |
| Layout | `u32 ownerTypeDef, u32 targetRequirement, u32 size, u32 alignment, u32 pack, u32 flags` |
| MethodDef | `u32 ownerTypeDef?, str name, u32 signature, u32 flags, u32 contractFirst, u32 contractCount, u32 bodyRow?` |
| MethodSig | `u32 resultTypeSpec, u32 receiverTypeSpec?, u32 paramFirst, u32 paramCount, u32 genericFirst, u32 genericCount` |
| Param | `u32 ownerMethodSig, u32 ordinal, u32 typeSpec, u8 mode, u8 flags, u16 reserved=0` |
| GenericParam | `u32 ownerDefId, u32 ordinal, str name, u32 constraintFirst, u32 constraintCount` |
| Constraint | `u32 ownerGenericParam, u16 kind, u16 flags, u32 targetTypeSpec, u32 witnessRef?` |
| Import | `u32 moduleRef, str exportKey, u32 expectedLocalDefId` |
| Export | `u32 localDefId, str exportKey` |
| PropertyDef | `u32 ownerTypeDef, str name, u32 typeSpec, u32 getterMethodDef, u32 flags` |
| SourceFile | `str path, u32 digestBlob?` |

The fixed row strides in bytes, in registry order excluding variable tables,
are: Module 28, Feature 8, ModuleRef 12, TypeArg 12, TypeDef 40,
FieldDef 20, Layout 24, MethodDef 28, MethodSig 24, Param 16,
GenericParam 20, Constraint 16, Import 12, Export 8, PropertyDef 20,
SourceFile 8, and SourceMap 24. String, Blob, TypeSpec, Contract and Body
use offset indexes. A consumer rejects an incorrect stride or an unconsumed
field, rather than guessing a trailing extension. Empty namespace strings
are allowed; declaration names, export keys and SourceFile paths are not
empty. SourceFile requires `digestBlob=0` in `(1, 0)` because no digest
algorithm is assigned.

`Module.identityBlob` and `ModuleRef.producerIdentityBlob` are opaque nonempty
byte identities, not hashes or proof of authorship. Identity equality is exact
byte equality; replacing the identity describes another producer. Export keys
are nonempty decoded strings, compared ordinally without normalization.
Producers may assign keys as they choose, but must preserve a published key
to offer a compatible replacement; an implementation name is not a binding
algorithm. `runtimePolicy` candidate values
are `0` fail-fast and `1` checked exceptions; `targetRequirement` values are
`0` portable semantic (no native assumption) and `1` Windows x64 native.
Other targets and nonzero module flags still need assignments and reject
in `(1, 0)`. The entry method, if present, is a local
MethodDef row. Imports identify **one** selected module and key; their expected
local TypeDef, MethodDef or PropertyDef (including its referenced signature,
layout and contracts) is the complete consumer-side contract. The expected definition has
no body. Export keys are unique within a producer; imports are unique by
`(moduleRef, exportKey, expectedLocalDefinition)`, and duplicate conflicting
bindings reject. MethodDef `bodyRow` is nonzero only for locally authored
implementations. A public exported declaration may have a body containing
private implementation facts; the export is its declared contract, not
permission to trust those private facts for caller binding.

TypeSpec form assignments and **entire** payload following `form, qualifiers`:

| Form | Remaining bytes | Meaning |
|---:|---|---|
| `0x01` primitive | `u8 primitiveCode` | Built-in type; TypeSpec row is 3 bytes |
| `0x02` local | `u32 typeDefRow` | Nominal type in this artifact |
| `0x03` imported | `u32 importRow` | Import whose expected definition is TypeDef |
| `0x04` formal | `u32 genericParamRow` | Declared type parameter |
| `0x05` constructed | `u32 genericTypeSpec, u32 typeArgFirst, u32 typeArgCount` | Ordered substitution |
| `0x06` optional | `u32 innerTypeSpec` | Presence on complete qualified type |
| `0x07` ref / `0x08` ptr / `0x09` box / `0x0A` val / `0x0B` own | `u32 innerTypeSpec` | Distinct representation families |

Nesting references in TypeSpec must point to an
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
PropertyDef visibility uses the same public/internal bits; its getter
MethodDef belongs to the same declaring type, has the same result TypeSpec,
and is bound by its PropertyDef identity, never a `get_Name` spelling.
Exported properties and methods require public visibility. `Layout` is
required only for a published fixed representation;
private `Auto` has no layout row. `FieldDef.byteOffset` is `0xFFFFFFFF` when
no fixed offset is published. A fixed layout validates size, alignment,
overlap, field offsets and ABI classification against its target requirement;
`Transparent` retains nominal identity while using its one carrier's direct
call classification. Equal layout is not implicit conversion.

Param modes are `0x00` by-value, `0x01` `out`, `0x02` `init ref`,
`0x03` readonly `ref`, `0x04` writable `ref`. The
[coded-body proposal](terrum-il-instruction-set.md) has local place
operations; publishing `out`/`init ref` across calls and loaning general
references still require their specified call/exception/loan rules.
Unimplemented modes cannot be silently treated as by-value. `MethodSig` records
declaration order; argument labels and receiver access need registered
contract fields rather than name-based inference. GenericParam, Constraint
and constructed TypeSpec rows are reserved for a separately specified generic
feature; their row shapes alone do not make generic bodies executable or
exportable.
Parameter and generic-formal ordinals are zero-based and contiguous within
their owning MethodSig or declaration. Unused range fields are `(0, 0)`.

### Semantic contracts and stable binding

Each Contract row is `u32 ownerDefId, u16 clauseKind, u16 flags=0, data`.
The offset index gives its exact extent; each clause defines the length of
`data`. Unknown clauses reject semantic consumption, even if the row is
well framed; no producer can reclassify one as debug data.
Contracts for a TypeDef or MethodDef occupy its contiguous `contractFirst/count`
range; duplicates of a singleton clause reject. The initial candidate clause
assignments are:

| Clause | `data` payload | Meaning |
|---:|---|---|
| `0x0001` Effects | `u32 mask` | Bit `0x01` promises `NoThrow`, bit `0x02` promises `DoesNotReturn`, bit `0x04` requires an unsafe caller; other bits reject. |
| `0x0002` OutputState | `u32 parameterOrdinal, u8 onTrue, u8 onFalse, u8 onExceptional, u8 reserved=0` | A declared result-correlated output state (`0` vacant, `1` fully initialized). Legal only for a Boolean result and `out`/`init ref` parameter. |
| `0x0003` Transfer | `u32 parameterOrdinal, u8 transferMode, u8 cleanupMode, u16 reserved=0` | A selected owned-parameter transfer/cleanup obligation; mode IDs beyond the initial profile are not yet assigned. |
| `0x0004` Dependency | `u8 resultKind, u8 sourceKind, u16 flags, u32 resultOrdinal, u32 sourceOrdinal` | A declared result/view backing, root, or lifetime dependency; kind/flag registry remains to be assigned. |
| `0x0005` Cleanup | `u32 cleanupMethodDef` | On a unique TypeDef, names its checked NoThrow cleanup method. This declares an obligation; an IR Drop or verified exceptional cleanup must discharge it exactly once. |

The bounded profile gives inspectable row shapes for effects, Boolean
output-state correlation and a nominal cleanup target. Only Effects is
usable by the narrow executable subset; OutputState and Cleanup require
publication, exceptional-path and ownership verification rules before a
consumer can accept them as executable contracts. It does **not** encode unspecified
Transfer/Dependency variants, pre/postcondition expression trees, arbitrary
witnesses or public managed-handle ABI. Those
need assigned values, required feature IDs and verifier rules before emission;
their reserved rows must not be ignored. Effects do not follow from an opcode
name, a method name, or an implementation body. A source-selected declaration
and closed witness bindings remain the call target; linking validates that
binding rather than rerunning overload resolution.

### Identity and contract invariants

Local indexes are compact references within one artifact, not stable identities
across rebuilds. External references resolve through a selected producer
identity and exported declaration key with the equality rules above. A
digest is not automatically a nominal identity. Stable-key assignment,
authentication and compatibility across published contract changes remain
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

For the `return 42` [coded-body example](terrum-il-instruction-set.md#worked-bytes-and-illustrative-size),
these are the relevant row references; row indexes are local to this artifact:

| Table row | Payload values in column order |
|---|---|
| Blob 1 | UTF-8 bytes `Example` (opaque module identity) |
| String 1 | UTF-8 bytes `Answer` (method name and export key) |
| Module 1 | `identityBlob=1, targetRequirement=1, runtimePolicy=0, entryMethod=1, moduleFlags=0, featureFirst=0, featureCount=0` |
| TypeSpec 1 | `form=0x01, qualifiers=0, primitiveCode=0x03` (`int32`) |
| MethodSig 1 | `resultTypeSpec=1, receiverTypeSpec=0, paramFirst=0, paramCount=0, genericFirst=0, genericCount=0` |
| MethodDef 1 | `ownerTypeDef=0, name=1, signature=1, flags=1` (public), `contractFirst=0, contractCount=0, bodyRow=1` |
| Export 1 | `localDefId=0x16000001, exportKey=1` |
| Body 1 | `ownerMethod=1`, followed by the body bytes specified in the companion proposal |

For example, the TypeSpec table with its single `int32` row is exactly
`00 00 00 00 03 00 00 00 01 00 03`: two four-byte offsets followed by the
three-byte row. Neither the Module row nor an export authorizes a caller to
skip bounds and contract compatibility checks; semantic body auditing is
governed by the consumer's trust policy.

## Body table and coded-instruction boundary

Each Body row is the variable table's offset-indexed extent and carries
exactly the coded bytes defined in the
[instruction-set proposal](terrum-il-instruction-set.md#body-record-direct-block-index-and-regions).
Its one-based row is referenced by the MethodDef `bodyRow` that owns it. Every Body row
must be owned by exactly one local MethodDef, and an imported declaration has
no body. Body-to-method identity and type references use the metadata tables
above; block/value/region IDs are **body-local**, not metadata `DefId`s.
Bodies cannot redefine a published signature, effect or result contract.

The instruction proposal assigns a narrow scalar executable subset with exact
record layout, opcodes and edge payloads. The other assigned opcode shapes
are structurally inspectable but not executable. Exception regions, general
loans, owner cleanup, rooted managed values, witness calls and correlated
outputs must receive additional feature-gated encodings before they can be
emitted as executable bodies.
They are not silently modeled as ordinary branch instructions. An ordinary binary consumer need not re-prove dependency bodies, but it
must reject out-of-bounds framing and unsupported coded forms it accepts
for execution. Trusting producer-supplied body facts is distinct from
silently interpreting an unknown instruction as a supported one.

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
- Every variable row and list is consumed wholly within its indexed extent or
  parent row; the parser consumes exactly its declared length and count.
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

The `u32` total length bounds the encoded artifact; count and section
length fields do not set practical reader acceptance ceilings. Numerical
ceilings below the format maximum and exact accounting remain open.

## Validation, unknown data, and references

Readers perform required loading checks before lowering or executing an
artifact; a trust policy chooses whether to perform the optional semantic
audit:

1. Check signature, revision/requirements identity, framing, lengths, ranges,
   canonical encodings where required, and resource limits.
2. Validate record shapes, reserved values, counts, local indexes, and
   referenced contract shapes needed for safe decoding/binding.
3. Resolve external declarations and verify the imported contract against the
   selected producer before lowering or execution.
4. Optionally audit authored bodies' use of imported contracts, CFG,
   liveness, place state, effects and ownership. Source compilers must check
   the bodies they produce; trusted binary consumers need not repeat this
   work or audit dependency bodies.
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

Framing, reference resolution and contract compatibility are required
independently of any separately defined integrity check. Semantic caller-use
and flow audits are optional for trusted binaries, not silently replaced by
integrity checks.

## Example: conditional output contract (beyond the initial coded profile)

Suppose MethodDef row 1 returns `bool` and its fourth parameter (ordinal 3)
has `Param.mode=0x02` (`init ref`). If it promises initialization exactly
when the Boolean result is true, its Contract row is:

```text
00 00 00 00  10 00 00 00
01 00 00 16  02 00 00 00  03 00 00 00  01 00 00 00
```

The first eight bytes index one 16-byte row; the next four name MethodDef
row 1 (`DefId=0x16000001`), then clause `0x0002`, flags zero, parameter
ordinal 3 and states `(1, 0, 0)` for true,
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
| Encoding and revisions | Adoption of candidate widths, future version/feature policy and reader compatibility rules. The proposed `(1, 0)` schema accepts only its registered semantic forms. |
| Declarations | Resource references, generic/witness exportability, type/field/layout flags, richer qualifier/contract clauses and their validation. Module identity and export-key equality are specified; assigning a stable key to a source declaration is a writer policy. |
| Executable bodies | Beyond the [narrow executable subset and other assigned decode shapes](terrum-il-instruction-set.md#executable-subset-versus-assigned-decode-shapes): output places, exceptional handlers/cleanup, loans, roots, witness bindings and their precise verifier rules. |
| Limits | Per-record and per-artifact sizes, nesting and work budgets, allocation limits and canonical encoding requirements. |
| Unknown data | Registry, reserved values, feature negotiation and safe handling or preservation of optional nonsemantic sections. |
