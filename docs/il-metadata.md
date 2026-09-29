# Terrum semantic IL and metadata

> [!IMPORTANT]
> Approved current design for the specified `TRRM` metadata and framing
> contracts, not a released byte schema or a claim of compiler support.
> See [implementation](implementation.md) for the current executable
> boundary. Terms such as "must" apply to the specified `(1, 0)` design;
> explicitly open contracts require further specification before use.

This document defines metadata tables and physical framing; the
[coded instruction set](il-instruction-set.md) defines bounded Body encoding
and operations.
The artifact can be embedded in PE or ELF but does not require either
container.

## Definitions and conventions

| Term | Meaning in this document |
|---|---|
| `TRRM` artifact | One complete, bounded byte slice carrying semantic IL and metadata. It may be a standalone file or embedded in another container. Its extent excludes the enclosing PE/ELF file and separately referenced resource payloads. |
| Module | A project's semantic unit with its own identity. Ordinary compilation produces one `TRRM` artifact with one Module row. A trimmed or merged artifact can contain several independently identified modules sharing physical sections, without a separate assembly identity or container. |
| Artifact-relative offset | A zero-based byte position from the first byte of the `TRRM` signature. An extent `[offset, offset + length)` is end-exclusive and must be contained in the supplied artifact slice before an embedded reader adds any container base. |
| Section directory (`directory`) | The fixed-width index following the header, containing one entry per present section kind, not the sections' bytes. Each entry records its kind, flags, record count, artifact-relative offset and byte length; the entry is not part of the section it indexes. |
| Section | One contiguous artifact-relative byte extent identified by a directory entry and containing a metadata table or body stream. An absent table has no directory entry or section. |
| Row | A fixed-stride payload in a fixed table, or an extent in a variable table's offset-indexed data. Rows have one-based indexes; absent table means zero rows. |
| Local definition ID (`DefId`) | A `u32` reference word: high byte is a registered definition-table kind, low 24 bits are its one-based row. A kind byte of zero selects the table fixed by a typed field's schema; polymorphic definition fields require an explicit registered kind. Zero is the only null word. Its value is not a stable cross-artifact identity. |
| Producer identity and export key | The selected dependency and published declaration identity used together to resolve an import, whether its producer is packed here or in another artifact. Equality of decoded text is specified below; source-side key assignment and authentication are separate. |
| Declared contract | Published type, callable, ownership/lifetime, access, effect and requirement facts against which consumers bind and check use. A contract is not inferred from a dependency body or source spelling. |
| Body | Typed semantic operations and control flow for an executable declaration, distinct from declaration metadata. |
| Source revision, artifact revision, native profile | Independent axes for source syntax/semantics, `TRRM` encoding and requirements, and target/runtime ABI assumptions. The enclosing container format is separate. One revision number does not imply another. |
| Required feature and optional data | A required semantic feature must be understood before binding or execution. An optional section can be skipped only if its registered kind or designated extension namespace is nonsemantic and safe to ignore. Its directory policy separately governs rewrites. |

Field tables use `u8`, `u16`, `u32` and `u64` for fixed-width unsigned
little-endian integers. Byte offsets are artifact-relative; lengths are byte
counts unless stated otherwise. Numeric assignments below describe this
approved current design. Numerical resource ceilings and future format-revision
policy remain unspecified.

## Scope and conformance boundary

The semantic IL is a compact binary representation of the language and
ecosystem: types, declarations, contracts, and operations. It is intended as
input to metadata binding, reflection/tooling, linking, and AOT/JIT code
generation without requiring source recompilation. It is not merely a
serialization of a compiler's private object graph or a verifier certificate.

Source compilation checks authored bodies against their declared contracts.
A binary consumer checks safe decoding, structure, supported semantics,
references, and binding/contract compatibility for retained imports. A
trusted merger performs the corresponding checks when it resolves imports
to direct packed definitions and produces a transformed artifact; loading
that trusted artifact need not repeat the merger's semantic proof. A
consumer may trust a compiler's or merger's body-level facts and promises
for artifacts admitted by its trust policy,
without re-proving dominance, liveness, effect/ownership flow, or every
call site. This is an explicit unsafe-like trust boundary, not a property
conferred by a byte flag in the artifact. An optional semantic audit checks
those facts for untrusted input or diagnostics; otherwise the compiler or
merger is responsible for their truth. Callers remain responsible for imported
preconditions, access, ownership and effects when they are compiled.
Malformed framing, unresolved retained imports, incompatible retained
contracts and unknown required semantic forms still reject; trusting a
producer does
not justify reading outside its byte extents or calling with a mismatched ABI.
An environment that cannot trust an artifact's author must audit its
semantic facts before safe execution or decline to execute it.

The accepted language contracts remain authoritative for their semantics:
[language](language.md), [lifetimes](lifetimes.md),
[conversions](conversions.md), [type families](type-families.md),
[data layout and ABI](data-layout-and-abi.md), and the
[memory model](memory-model.md). This document defines the approved current
binary design where specified; it does not redefine their rules. The separately
specified [coded instruction set](il-instruction-set.md) is also approved for
its bounded executable subset.

| Role | Required checks |
|---|---|
| Source compiler and writer | Check authored bodies against declared language contracts; encode complete, well-formed declarations and supported bodies. |
| Binary reader | Bound and decode the supplied artifact; validate framing, required features and referenced contract shapes before accepting it. |
| Linker or executable consumer | Resolve selected dependencies, check compatibility and reject unsupported semantics before lowering or execution. Trusted bodies need no repeated semantic/caller-use proof; an opt-in audit can check them. |

## Format identity and revision scope

`TRRM` is the format identifier, with ASCII signature bytes `54 52 52 4D`.
This design targets format 1.0, encoded as major/minor `(1, 0)`; it is not
a declaration of general availability or a compatibility promise for draft
artifacts. Before release, the schema can change without incrementing this
pair. Pre-release tools may exchange artifacts only when they share the exact
schema; matching version bytes alone do not establish that. A reader of the
specified schema accepts exactly `(1, 0)` and rejects other pairs. Post-release
revision and feature-negotiation rules remain unspecified. Source revisions
and target ABI/profile requirements are independent of the format pair.

## Physical framing and embedding

The following framing, table IDs and row layouts describe the approved current
encoding. Initial opcodes and bodies are defined in the
[coded instruction set](il-instruction-set.md).

### Header and directory

Byte order is little-endian. The header is fixed at 32 bytes in this
layout; the directory follows the header, and present sections follow the
directory in ascending artifact-relative offset order. There is one entry
per **present** table kind. An absent known table is empty; Module cannot be
absent. Multiple modules share these artifact-wide sections and row indexes;
there is no directory per module. No compression or whole-section transform
is used, so a consumer can bound and index rows without inflating the complete
artifact.

The artifact is the supplied `TRRM` byte slice, whether stored alone or
embedded in a PE/ELF container. Its total length and offsets describe that
slice, not the containing file. Large resources may be held separately and
referenced by metadata; their payload bytes are not part of `totalLength`.
Resource identity, resolution, integrity and limits remain schema decisions.

| Offset | Field | Width | Meaning |
|---:|---|---:|---|
| 0 | Magic | 4 bytes | ASCII `TRRM` |
| 4 | Format major | `u16` | `1` in this design |
| 6 | Format minor | `u16` | `0` in this design |
| 8 | Header length | `u32` | `32` for this layout |
| 12 | Directory entry count | `u32` | Number of present sections; 22 kinds are assigned below, plus nonsemantic extension kinds |
| 16 | Total artifact length | `u32` | Exact byte length of the supplied `TRRM` slice; zero is reserved and invalid in this schema |
| 20 | Directory offset | `u32` | `32` in this design |
| 24 | Reserved | 8 bytes | All zero; readers reject nonzero bytes |

Each directory entry is 16 bytes:

| Entry offset | Field | Width | Meaning |
|---:|---|---:|---|
| 0 | Section kind | `u16` | One of the table assignments below |
| 2 | Flags | `u16` | Two-bit section-policy selector; upper 14 bits reserved and zero |
| 4 | Record count | `u32` | Number of records encoded in the section, `1..0xFF_FFFF` when present |
| 8 | Section offset | `u32` | Artifact-relative section start |
| 12 | Section byte length | `u32` | Exact section extent |

The low two directory-flag bits form one policy selector, not independent
permissions or producer authority to declare a section safe to skip:

| Flags | Reader and rewriter policy |
|---:|---|
| `0x0000` | Required semantics: a consumer must understand this section before binding or execution. A rewriter must understand and update it or reject the rewrite. |
| `0x0001` | Optional, self-contained: a consumer may ignore it; a rewriter may preserve its bytes if its registered contract guarantees that changes elsewhere cannot invalidate them. |
| `0x0002` | Optional, reference-slotted: a consumer may ignore its payload. A rewriter may preserve the unchanged payload if it remaps every declared reference slot and proves each target still has the same meaning; otherwise it drops the section. |
| `0x0003` | Optional, discard-on-rewrite: a consumer may ignore it, but a rewriter that does not understand and regenerate it drops it whenever the artifact changes. |

Nonzero upper bits reject; these policies are mutually exclusive. The section-kind
registry must authorize the exact policy for assigned kinds. Unassigned
kinds are skippable only in the reserved nonsemantic extension namespace
defined below; their flags cannot override that namespace policy.
Every present semantic table uses `0x0000`; SourceMap uses `0x0002`.
Empty tables are omitted rather than encoded as empty entries. The entry does
not add a minimum-reader-version field, and a matching major version does
not imply acceptance of a different minor version. A separate minimum-reader
field remains an alternative for future revision policy.

Framing invariants:

- `headerLength` is 32 and `directoryOffset` is 32; the directory has exactly
  `entryCount * 16` bytes, with checked
  multiplication and checked end-offset arithmetic.
- `totalLength` equals the supplied `TRRM` slice length and is in
  `32..0xFFFF_FFFF`. Header, directory, and every section lie wholly within
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
  variable and reference-slotted sections validate their index or prelude
  and data extents as specified below.

The directory remains fixed-width for direct section lookup. An artifact
has 32 bytes of header plus 16 bytes **per present table** before section
payloads. The eight reserved header bytes have no assigned flags or other
semantics; writers set them to zero. Large payloads needing a wider address
space require a separately specified resource reference, not wider offsets
in ordinary metadata. Individual variable-table data is limited to `u32`
bytes by its offset index.

Zero `totalLength` is left available for a future extended-framing escape,
but has no interpretation in this schema. An extended artifact would need
wider section offsets and lengths as well as a wider total length; sections
larger than `u32` bytes would also need wider row-data indexes. No reader may
infer that format merely from a zero length. Encoding the ordinary length in
512-byte units would impose padding and lose exact byte framing without
addressing those other widths.

## Metadata logical tables and row semantics

### Table stream and token encoding

Each present table has the directory's `recordCount` rows, at most
`0xFF_FFFF` per table. The same limit applies to any section's payload
record count; slot counts and byte lengths are separate bounds. **Fixed** tables
concatenate their fixed-size row payloads, without per-row lengths. **Variable**
tables start with `u32 offsets[recordCount + 1]`, followed by their data bytes.
Offsets are relative to the start of the data (after the index), start at zero,
are nondecreasing and end at the exact data length (`<= 0xFFFF_FFFF`). Row `i`
occupies `[offsets[i-1], offsets[i])`, for one-based `i`. Empty Blob
rows are permitted; a String row always contains its header and
terminator even when its content is empty, so String offsets strictly
increase. Other semantic variable rows must be nonempty. The checked
index size plus the final offset equals the section length. This indexes
strings, type forms, contracts and bodies directly without scanning prior
records; a one-row heap without per-row headers pays four additional bytes
versus length-prefixing, while larger heaps pay the same four bytes per row
plus one sentinel. String also retains this index alongside its tagged
header, which costs eight index bytes for one row but preserves direct lookup.
This deliberately favors repeated reader lookup after validating the index:
writers and mergers may first compute row lengths, buffer rows, or backpatch
offsets. A reader still validates every offset and extent before use; the
index avoids rescanning preceding rows for subsequent AOT, JIT, interpreter
and tooling queries.

A table reference or `DefId` uses the same four-byte `u32` word:
`(tableKind << 24) | row`, with row in `1..0xFF_FFFF`. This is an 8-bit
kind and 24-bit data partition of one `u32`, not a
separate `uint24` type or a three-byte row encoding. In a schema-typed
column such as `MethodDef.signature`, `tableKind=0` means the table named
by that column (MethodSig here); it does **not** mean the containing
table. This also applies to references across tables, such as
`MethodDef.bodyRow`. A polymorphic `DefId` field such as
`Export.localDefId` instead requires the explicit low byte of its target
definition-table kind: TypeDef `0x13`, FieldDef `0x14`, MethodDef `0x16`,
GenericParam `0x19` or PropertyDef `0x1E`, further restricted by the
field's permitted target kinds. Thus typed MethodDef row 1 is
`0x0000_0001`, whereas explicit MethodDef row 1 is `0x1600_0001`;
neither loses a row bit. Nonzero explicit kinds in typed columns and
kind zero in polymorphic columns reject. Only the all-zero word is null,
and only in columns marked `?`. Other kinds reject, including `0xFF`,
which is reserved for a future required indirection encoding, not
defined or accepted here.

### Candidate inline built-in type reference (future revision)

This extension is distinct from the approved 8-bit-kind/24-bit-row
reference word. It is not part of the `(1, 0)` encoding.
Reserve kind `0xFE` for typed type-reference fields, with the word
`0xFE_qq_ff_pp`: `pp` is an assigned atomic built-in code, `ff=0` means
the atomic type itself and an assigned one-argument family code in `ff`
means that family over `pp`. In `qq`, bit 0 qualifies the outer type as
`write`, bit 1 qualifies its argument as `write`; all other bits reject.
For an atom, `ff=0` and argument `write` must be zero. Both qualifier
bits require a type/family for which the respective authority is legal;
unassigned codes, illegal family/argument pairs and `write` on a
non-writable type reject. Thus `uint8` (code `0x0B`) would be
`0xFE_00_00_0B` (`0B 00 00 FE` little-endian), `ref<uint8>` (family
`0x8B`) would be `0xFE_00_8B_0B` (`0B 8B 00 FE`), and
`write Span<uint8>` would be `0xFE_01_8A_0B`
(`0B 8A 01 FE`). This reserves one of the 256 reference-kind values,
not TypeSpec rows: TypeSpec retains its `0xFF_FFFF` row ceiling, and
kind `0xFF` remains available for future required indirection.

The inline form applies only where the schema expects a complete type
reference, such as MethodSig results/receivers, Param, FieldDef,
PropertyDef, Constraint and TypeArg arguments, including a candidate
Body type-map entry. It is not a DefId, table row/range, TypeArg owner
or a shortcut for an arbitrary generic formal. TypeSpec's own
`innerTypeSpec`, `genericTypeSpec` and indexed-family argument remain
earlier **row** references; a built-in row can still be needed to
construct another TypeSpec. An inline TypeArg argument has no earlier
row to check but must satisfy that argument's qualifier and constraint
rules. `Span<write C>` needs a TypeSpec when `C` is nominal or formal;
only an assigned, write-eligible atomic `pp` can use the argument-`write`
bit without a TypeSpec lookup. Qualified and other non-inline types
continue to use TypeSpec rows.

A revised-format writer would emit the inline form for every eligible
complete typed reference; a row naming that same built-in would be
noncanonical in such a field. Readers would validate code membership,
both qualifier positions and field-specific restrictions, then compare
**resolved types**, not raw words. A merger can copy an inline word
unchanged and remap only indexed rows; it must still validate the
revised artifact's type contracts. A Body-local map keeps its compact
IDs and must reject duplicate resolved types, including an inline/row
alias. This avoids the TypeSpec dereference, not the map lookup.
Adoption needs a format revision and updated readers/writers; current
readers must reject these candidate words.

A contiguous range is `u32 firstRow, u32 count`; its empty encoding is
`(0, 0)`, otherwise `firstRow` is nonzero, has a zero high byte, and the
checked last row is at most `0xFF_FFFF` and belongs to that table.
Ranges for one owner must not overlap or include another owner's rows.
There are no pointer-sized fields or implicit string terminators; the
String row's explicit terminator is specified below.

### Optional section reference slots

A `0x0002` section begins with `u32 slotCount`, followed by `slotCount`
16-byte slots. The remaining bytes are its kind-specific payload. The
directory's `recordCount` counts payload records, not slots. Slot numbers
are one-based `u32` values local to this section; zero is invalid where a
payload requires a reference. The generic slot layout is:

| Slot offset | Field | Width | Meaning |
|---:|---|---:|---|
| 0 | Target section kind | `u16` | Assigned core table containing the target |
| 2 | Selector | `u16` | `0` row, `1` Body instruction |
| 4 | Target row | `u32` | One-based row in the target table, high byte zero |
| 8 | Block ID | `u32` | Zero for selector 0; nonzero Body block for selector 1 |
| 12 | Instruction ordinal | `u32` | Zero for selector 0; operation ordinal or terminator ordinal for selector 1 |

The prelude must fit entirely within its section: validate
`4 + slotCount * 16` with checked arithmetic before reading or allocating.
Each slot has a supported selector and a present target row of the specified
core kind; selector 1 requires Body. A section's registry or extension
namespace contract specifies which selectors it permits and requires
**all cross-section references in its payload** to use slots, not raw row
indexes. Core metadata and executable bodies retain direct indexes without
this extra lookup. Only sections selecting this policy pay the four-byte
count and 16 bytes per slot.

A generic rewriter keeps the opaque payload and its slot numbers unchanged.
It may rewrite a slot's target coordinates only when its own transformation
provides an unambiguous old-to-new mapping **and** preserves the referent's
meaning. Reordering rows can be remapped; deleting a target, changing a
referenced Body instruction, or renumbering blocks/ordinals without a
meaning-preserving mapping cannot be repaired by indirection alone. The
rewriter drops the optional section rather than emitting dangling or stale
slots. A section-aware tool may instead regenerate its payload and prelude.
Dropping a section also removes its directory entry and does not leave
unclaimed bytes. The prelude is not a promise that arbitrary opaque payload
bytes remain valid after semantic changes.

### Section kinds and row layouts

| Kind (`u16`) | Table | Row payload and role |
|---:|---|---|
| `0x0001` | Module | One row per logical module: identity, runtime requirement and entry point |
| `0x0002` | String | Offset-indexed, tagged ASCII/UTF-8/UTF-16LE/UTF-32LE rows |
| `0x0003` | Blob | Variable opaque non-reference bytes, interpreted only by their referring field |
| `0x0004` | Feature | Required semantic feature identifiers |
| `0x0010` | ModuleRef | A consumer module's selected producer identity, in this artifact or external |
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

`Module` is always present with at least one row; ordinary one-project output
has one row. A merger may retain several module rows and deduplicate shared
physical data without conflating their nominal identities or export keys.
Other known tables are absent when empty, not encoded as empty directory
entries. In format `(1, 0)`, the Feature table is absent, and every Module
row's feature range is `(0, 0)`. A new semantic feature requires an explicit
schema update, not merely a feature
number in an otherwise unsupported artifact. Kinds `0x8000..0xFFFF` are
reserved for **nonsemantic optional extensions**. A reader can skip an
unrecognized kind in this range only with flags `0x0001`, `0x0002` or `0x0003`,
after checking its directory framing and any `0x0002` prelude. `0x0002`
extensions use only selector 0 to refer to assigned core tables or selector
1 to refer to Body instructions; their payload uses slots for all such
references. They cannot define executable behavior, contract obligations,
or new interpretations of core rows, and core sections cannot refer to them.
Required-policy entries or unknown kinds outside this range reject. Numeric gaps
below this range are reserved, not available for execution semantics. A
required feature is identified by `u32` in `Feature`; its registry, minimum
revision rule and negotiation policy still require a decision. Until then,
a producer cannot emit an unrecognized semantic form by merely writing a
feature number.
`SourceMap` is skippable for execution, not uninterpretable for tools. Its
reference-slot prelude uses selector 1 for Body instruction locations and
selector 0 for SourceFile rows. After the prelude, its fixed 16-byte rows
are `u32 instructionSlot, u32 sourceFileSlot, u32 startUtf16,
u32 lengthUtf16`. Each slot must have the stated selector and target kind;
the section length is exactly `4 + slotCount * 16 + recordCount * 16`.
Rows sort by `instructionSlot` and do not duplicate an instruction slot.
Ordinal zero means the first operation, `operationCount` means its
terminator. SourceMap-aware tools validate that each block and ordinal exists;
opaque-preserving rewriters must discard the section when they cannot
establish that changed Body locations retain the same meaning. A remapping
must not merge two referenced instruction locations into one.
SourceFile paths are identifiers, not permissions to read a local file;
`digestBlob` is an optional exact-content digest whose algorithm requires
an assigned feature before nonzero use. Spans validate checked end arithmetic
and need not imply that source bytes are embedded. Dropping SourceMap does not
change execution or contracts; tools preserving debug data must also retain
or remap its referenced SourceFile rows.

For one mapping from Body row 1, block 1, operation 0 to SourceFile row 1,
UTF-16 span `[0, 2)`, the SourceMap section has two slots and one row
(`recordCount=1`), for 52 bytes:

```text
02 00 00 00
20 00 01 00 01 00 00 00 01 00 00 00 00 00 00 00
1F 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00
01 00 00 00 02 00 00 00 00 00 00 00 02 00 00 00
```

Moving Body row 1 to row 3 changes only slot 1's `targetRow`; the row's
`instructionSlot=1` and the rest of the payload remain unchanged, provided
the referenced instruction still has the same meaning.

The next tables show **row payloads**, excluding the variable section's offset
index. `str` and `blob` mean one-based rows in their respective tables;
zero is allowed only where a column says `?`. A String row has the physical
encoding below; fields for names, paths and keys require valid decoded scalar
text and compare by ordinal scalar sequence without normalization, even when
the rows use different encodings. Blob rows
are bytes only: each use must name its interpretation and validate its
contents. A Blob payload must not encode a semantically meaningful table
index, `DefId`, token, Body instruction location or other reference that
trimming, rewriting or merging would need to remap. Such references belong
in typed table fields or, for eligible optional sections, validated reference
slots; a Blob is not a hidden fixup list. Coincidental byte patterns have no
reference meaning. A merger may preserve or deduplicate Blob bytes but never
silently treat their contents as relocatable references. Duplicate strings or
blobs are not a second declaration identity.

The String section begins at a 4-byte-aligned artifact offset. Each indexed
row starts at a 4-byte-aligned offset relative to its data region and contains
`u8 flags, u24 unitCount` (the count stored in three little-endian
bytes), followed immediately by `unitCount` code units, an all-zero
terminator of the encoding's code-unit width, and the minimum zero padding
needed to align the next row to four bytes. The count excludes the header,
terminator and padding; it is in `0..0xFF_FFFF`. ASCII and UTF-8 units are
one byte, UTF-16LE units two and UTF-32LE units four. A reader checks the
product `unitCount * unitWidth`, adds the header and terminator, then checks
the aligned row extent against the adjacent offsets before accessing content.
The offset index addresses whole rows, not content bytes; its difference
includes the header, terminator and padding. The reader also validates that
the padding and terminator are zero. Content
begins four bytes after the row start and is therefore 4-byte aligned in the
artifact for every encoding. The terminator makes eligible NUL-free content
convenient for interop and bounds accidental scans at the row end, but does
not replace the explicit count or checked access.

| `flags & 0x03` | Content | Unit width | Terminator |
|---:|---|---:|---|
| `0` | ASCII code units (`0..0x7F`) | 1 byte | One zero byte |
| `1` | UTF-8 code units, no BOM | 1 byte | One zero byte |
| `2` | UTF-16 little-endian code units, no BOM | 2 bytes | Two zero bytes |
| `3` | UTF-32 little-endian code units, no BOM | 4 bytes | Four zero bytes |

`unitCount` counts encoded units, not Unicode scalar values: one scalar can
occupy several UTF-8 bytes or a UTF-16 surrogate pair. The terminator is
one additional code unit and is not included in `unitCount`.
The common four-byte content-start alignment satisfies the per-encoding
start-offset requirement. A whole number of code units follows from
`unitCount * unitWidth`; no separate content-length divisibility check is
needed. The counted extent must still end before the required terminator
and agree exactly with the next indexed row offset after padding.

Bits 0–1 of `flags` select the encoding. Bits 2–7 are reserved and must
be zero in `(1, 0)`; readers reject any row with these bits set. All four
encoding values are assigned, so a further encoding needs an explicitly
defined future extension rather than interpreting reserved bits as more
encoding-kind bits. There is no validity or normalization bit. A name,
path or key reference must decode to well-formed scalar text:
UTF-8 uses shortest sequences; UTF-16 rejects unmatched surrogates; UTF-32
rejects surrogates and values above `0x10FFFF`. ASCII rejects high-bit bytes.
Name/path/key uses also reject embedded zero scalars. The explicit length
remains authoritative: raw literal data could contain zero code units, so
the trailing terminator alone never makes arbitrary content safe for a
NUL-terminated interop call. Identifier ASCII and UTF-8 rows with equal
ASCII content compare equally; raw bytes/code units are not silently
transcoded. The 24-bit bound is on the complete unsegmented *unit count*, not
each piece of a larger composite: at its maximum it permits almost 16 MiB of
ASCII/UTF-8, 32 MiB of UTF-16LE or 64 MiB of UTF-32LE content. The total
section and artifact retain their separate `u32` byte bounds and checked
extent arithmetic. Multi-gigabyte payloads belong in separately managed
resources, not mainstream String rows.

These physical rows can carry names and keys now. Literal references,
ill-formed literal payload permissions and their use-site validation remain
unassigned in `(1, 0)`; a producer cannot acquire literal semantics by
tagging a row or claiming that its bytes are valid. An **additional**
segmented ASCII/UTF-16LE representation for mostly ASCII data remains open,
not the default. It must bound the *combined* content, preserve direct row
lookup and account for per-segment cost, decoding and alternating-run cases.
The four initial kinds above do not allocate a segmented wire kind.

The mapped content can back a validated, immutable typed `TextSpan` only
while its artifact backing remains live. A String row is not directly a
`Text` object with `nint _length` followed by inline data: its header has
a kind and a 24-bit code-unit count, not a target-width native length, and
the row belongs to the artifact rather than a runtime text object. A runtime
may copy/transcode into owned `Text`
or explicitly retain the artifact lease in a compatible text representation;
zero-copy ownership, GC rooting and direct aliasing are not implied.

| Table | Ordered row payload |
|---|---|
| Module | `str identity, u32 targetRequirement, u32 runtimePolicy, u32 entryMethod?, u32 moduleFlags, u32 featureFirst, u32 featureCount, u32 moduleRefStart, u32 typeDefStart, u32 methodDefStart, u32 sourceFileStart` |
| Feature | `u32 featureId, u32 minimumRuleRevision` |
| ModuleRef | `str producerIdentity, u32 targetRequirement, u32 runtimePolicy` |
| TypeSpec (variable) | `u8 form, u8 qualifiers`, followed by only the operands specified for that form below |
| TypeArg | `u32 ownerTypeSpec, u32 ordinal, u32 argumentTypeSpec` |
| TypeDef | `str namespace, str name, u32 flags, u32 fieldFirst, u32 fieldCount, u32 genericFirst, u32 genericCount, u32 layoutRow?, u32 contractFirst, u32 contractCount` |
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
are: Module 44, Feature 8, ModuleRef 12, TypeArg 12, TypeDef 40,
FieldDef 20, Layout 24, MethodDef 28, MethodSig 24, Param 16,
GenericParam 20, Constraint 16, Import 12, Export 8, PropertyDef 20,
and SourceFile 8. String, Blob, TypeSpec, Contract and Body
use offset indexes; SourceMap has the reference-slot prelude and 16-byte
rows defined above. A consumer rejects an incorrect stride or an unconsumed
field, rather than guessing a trailing extension. Empty namespace strings
are allowed; declaration names, export keys and SourceFile paths are not
empty. SourceFile requires `digestBlob=0` in `(1, 0)` because no digest
algorithm is assigned.

The following ownership and range rules apply in addition to the module
partition below. A TypeDef's `fieldFirst/fieldCount` names exactly its
contiguous FieldDef rows; each row's `ownerTypeDef` names that TypeDef.
Its `genericFirst/genericCount` similarly names its GenericParam rows.
A MethodSig's `paramFirst/paramCount` names exactly its Param rows, whose
`ownerMethodSig` names that MethodSig and whose zero-based `ordinal` equals
its position in the range. Its `genericFirst/genericCount` names the
MethodDef's formals, not formals owned by the structural MethodSig: each
GenericParam's `ownerDefId` names the using MethodDef. Consequently a
MethodSig with a nonempty generic range cannot be shared by distinct
MethodDefs. Each formal's `constraintFirst/constraintCount` names exactly
its Constraint rows, whose `ownerGenericParam` names that formal. Nonempty
ranges must satisfy the common checked range rules; empty ranges are
`(0, 0)`. Field and formal rows cannot belong to two owners.

`MethodDef.signature` and `PropertyDef.typeSpec` are required references.
A nonzero `MethodSig.receiverTypeSpec` requires a MethodDef with a nonzero
`ownerTypeDef`; a module-level MethodDef has no receiver. A method owned by
a type can have no receiver (a type-scoped static method). The getter named
by a PropertyDef must have the same owner TypeDef and a MethodSig with the
same result TypeSpec; its parameter and receiver contracts remain those of
that method, not inferred from the property name. A MethodDef's nonzero
`bodyRow` is unique to that MethodDef, and every Body row is referenced
exactly once. A TypeDef's nonzero `layoutRow` names a Layout row whose
`ownerTypeDef` names it; no Layout row may be orphaned or shared between
different TypeDefs. TypeSpec form `0x05` ranges similarly contain only
TypeArg rows whose `ownerTypeSpec` names that constructed TypeSpec and whose
zero-based `ordinal` equals its position in the range. Their
`argumentTypeSpec` rows must precede the constructed TypeSpec, so structural
TypeSpec references remain acyclic.

The remaining fixed-row columns have these `(1, 0)` validation rules:

| Row | References, values and ownership |
|---|---|
| Module | `identity` is required; `entryMethod?` is a MethodDef in this module. `featureFirst/featureCount` is `(0, 0)` because Feature is absent. `moduleFlags=0`; `targetRequirement` and `runtimePolicy` use the assigned values below. The four `*Start` fields are cursors into their respective module-partitioned tables, not nullable row references. |
| ModuleRef | `producerIdentity` is required, and requirement/policy must match the selected producer; ownership follows `Module.moduleRefStart`. An in-artifact identity selects exactly one Module. |
| TypeDef | `namespace` may name an empty String row; `name` must be nonempty. `flags` contains the specified visibility, kind and layout. `layoutRow?` names this type's Layout when present; field, generic and contract ranges have exactly the owners described here. |
| FieldDef | `ownerTypeDef`, `name` and `typeSpec` are required. `byteOffset=0xFFFF_FFFF` when no fixed offset is published; otherwise it is an offset within the owning published Layout. Its flags permit visibility and the mutable bit only. |
| Layout | `ownerTypeDef` is required, `flags=0`; `targetRequirement` uses the assigned target registry. `size`, `alignment` and `pack` are checked against the target's layout rules; a row with unsupported layout rules cannot publish a valid fixed representation merely by supplying numbers. |
| MethodDef | `name` and `signature` are required; `ownerTypeDef?` is zero for a module-level callable. Contract rows have this MethodDef as owner. `bodyRow?` names its unique Body; retained imported expectations have no Body. Only visibility bits are assigned. |
| MethodSig | `resultTypeSpec` is required (the built-in `void` TypeSpec denotes no result); `receiverTypeSpec?` is zero when absent. Param and generic ranges follow the ownership rules above. |
| Param | `ownerMethodSig` and `typeSpec` are required. `mode` uses the assigned registry below, `flags=0`, and `reserved=0`; ordinal is zero-based within the owning range. |
| GenericParam, Constraint | `ownerDefId` names a TypeDef or MethodDef and `ownerGenericParam` names a formal, respectively. Names are nonempty, ordinals are zero-based within the owning range, and constraints belong to their formal. `Constraint.flags=0`; unassigned kinds and witnesses cannot be bound as executable generic contracts. |
| Import | `moduleRef` names a ModuleRef owned by the expected definition's module; `exportKey` is nonempty and `expectedLocalDefId` names that module's TypeDef, MethodDef or PropertyDef with no locally authored implementation. Resolve that selected producer/key against the complete expected contract before execution. |
| Export | `localDefId` names this module's public TypeDef, MethodDef or PropertyDef, not an imported expectation; `exportKey` is nonempty and unique within this module's producer identity. An exported member's owner TypeDef is public. |
| PropertyDef | `ownerTypeDef`, `name`, `typeSpec` and `getterMethodDef` are required; only visibility bits are assigned. The getter and result must match as specified above. |
| SourceFile | `path` is required nonempty decoded text; `digestBlob?=0` until a digest algorithm is assigned. The Module partition owns the row; paths convey no filesystem authority. |

`Module.identity` and `ModuleRef.producerIdentity` are required references
to nonempty, well-formed String text without embedded zero scalars, not
hashes or proof of authorship. Identity equality uses
ordinal decoded scalar text without normalization, even across different
String row encodings; Module identities must be distinct within one artifact.
Changing an identity selects another producer. Export keys are likewise
nonempty decoded strings, compared ordinally without normalization.
Metadata table indexes and `DefId` rows are artifact-wide. Rows in ModuleRef,
TypeDef, MethodDef and SourceFile are grouped by owning Module row. For
each such table, the first Module row's `*Start` is `1`; starts are
nondecreasing, and Module row `i` owns rows in
`[start[i], start[i+1])`, with the last end equal to the table's
`recordCount + 1`. This is a one-based cursor range, not a table
reference: an empty range is permitted, all starts are `1` when the
table is absent, and the last end may be `0x100_0000` when the table
has `0xFF_FFFF` rows. Every present row belongs to exactly one
module without storing an owner word on each declaration. A merger
reorders rows by module and remaps references when packing them.
The four start cursors cost 16 bytes per module instead of four owner
bytes per row in these four tables; that size trade breaks even at four
scoped rows per module. A reader can index the module ranges once for
row-owner checks. It does not make every reference cheaper: shared
TypeSpec rows still use artifact-wide indexes.

A MethodDef with `ownerTypeDef` must have the same owner as that TypeDef.
FieldDef, Layout and PropertyDef inherit ownership from their ownerTypeDef;
GenericParam, Constraint, Contract and Body inherit it through their owning
declaration. TypeSpec, TypeArg, MethodSig and Param are structural rows
without a module owner; their references must be valid in every declaration's
module context that uses them. An Import belongs to its expected local
definition's module; its ModuleRef must belong to that same module. An
Export belongs to its local definition's module. Module entry methods must
be owned by their Module row. A SourceMap row must associate a Body
instruction with a SourceFile owned by that same module.

Strings and blobs can be shared by equal bytes when the referring fields
give them the same interpretation. Structural TypeSpec/TypeArg and
MethodSig/Param rows may be shared only when their resolved nominal, formal
and imported references and passing modes remain identical in every using
module. Distinct module identities and nominal TypeDefs are not coalesced
merely because names or layouts match. A source compiler binds a
cross-module declaration through its own Import and expected contract.
A trusted merger may resolve that edge against the producer's export,
preserve the selected semantic contract, and rewrite uses to the
producer's definition in the same artifact. It may then omit an
unneeded Import and expected definition; module identities remain
distinct. Several consumer modules can directly share one physical
definition when they resolve to the same nominal identity and compatible
declared contract. Equal spelling, layout or body bytes alone never
establish that identity or compatibility. A retained Import remains
available when independently replaceable binding is needed. A shared
structural row cannot be rewritten in place if another module still
uses its original imported meaning; the merger writes a distinct row
and remaps only the affected uses.

A ModuleRef selects the matching Module identity within this artifact when
present, otherwise an external producer; its requirement and policy must
match the selected module. Retained internal and external imports use the
same export key and complete expected-contract compatibility checks.
Each module's runtime policy and target requirement must be compatible
with the dependencies the merger resolves; retained imports still require
binding checks. Packing modules does not waive a mismatch.
Export keys are unique per Module identity, not across the whole artifact.
An entry point is selected by Module identity; several modules may each
declare one, but no loader guesses from their row order. A merger must
remap all row references, reconcile imports and exports, and update or
drop optional reference-slotted sections. A trusted merger checks semantic
compatibility when rewriting an edge; the resolved artifact does not
require repeating that proof on each trusted load. Later replacement of
a direct-bound module requires revalidation or relinking, not a new
module identity. Trimming cannot silently discard a published contract
while claiming full compatibility under the same identity.
The artifact still has one physical directory and no assembly manifest.
Producers may assign keys as they choose, but must preserve a published key
to offer a compatible replacement; an implementation name is not a binding
algorithm. `runtimePolicy` candidate values
are `0` fail-fast and `1` checked exceptions; `targetRequirement` values are
`0` portable semantic (no native assumption) and `1` Windows x64 native.
Other targets and nonzero module flags still need assignments and reject
in `(1, 0)`. The entry method, if present, is a MethodDef row owned by
that module. Imports identify **one** selected module and key; their expected
local TypeDef, MethodDef or PropertyDef (including its referenced signature,
layout and contracts) is the complete consumer-side contract. The expected
definition has no body. Imports are unique by
`(moduleRef, exportKey, expectedLocalDefinition)`, and duplicate conflicting
bindings reject. MethodDef `bodyRow` is nonzero only for locally authored
implementations. A public exported declaration may have a body containing
private implementation facts; the export is its declared contract, not
permission to trust those private facts for caller binding.

TypeSpec form assignments and **entire** payload following `form, qualifiers`:

| Form | Remaining bytes | Meaning |
|---:|---|---|
| `0x01` built-in | `u8 typeCode`, then `u8 argumentCode` iff the type code is `0x80..0xFE` | Atomic type in 3 bytes, or one-argument built-in family over an atomic type in 4 bytes |
| `0x02` in-artifact | `u32 typeDefRow` | Nominal TypeDef in this artifact, including a direct resolved definition from another packed module |
| `0x03` imported | `u32 importRow` | Import owned by the using declaration's module whose expected definition is TypeDef |
| `0x04` formal | `u32 genericParamRow` | Declared type parameter owned by the using declaration's module |
| `0x05` constructed | `u32 genericTypeSpec, u32 typeArgFirst, u32 typeArgCount` | Ordered substitution for a nominal generic definition |
| `0x06` optional | `u32 innerTypeSpec` | Presence on complete qualified type |
| `0x07` ref / `0x08` ptr / `0x09` box / `0x0A` val / `0x0B` own | `u32 innerTypeSpec` | Indexed representation-family argument when the inline built-in form does not apply |
| `0x0C` indexed built-in | `u8 familyCode, u32 argumentTypeSpec` | Other one-argument built-in family over a non-inline TypeSpec argument |

Nesting references in TypeSpec must point to an
earlier TypeSpec row; nominal declarations may refer to each other through
their TypeSpec rows, subject to storage-cycle validation. A TypeSpec reached
from several modules must retain the same resolved nominal identity in
each context; identical spelling alone cannot make unrelated types shared.
In a merged artifact, the in-artifact form may name another module's TypeDef
after a trusted merger resolves that import. The imported form preserves
the consumer's selected expected TypeDef when the edge remains an Import.
TypeArg rows for
one constructed nominal form are contiguous with ordinals `0..count-1`; arity,
qualification and constraints are checked against the selected nominal
declaration. Qualifier bit `0x01` denotes `write` where that type form permits
it; other bits reject. The proposed atomic built-in code registry is:

| Codes | Atomic types in increasing code order |
|---|---|
| `0x00` | `void` |
| `0x01` | `bool` |
| `0x02..0x05` | `char8`, `char16`, `char32`, `rune` |
| `0x06..0x0A` | `int8`, `int16`, `int32`, `int64`, `int128` |
| `0x0B..0x0F` | `uint8`, `uint16`, `uint32`, `uint64`, `uint128` |
| `0x10..0x11` | `nint`, `nuint` |
| `0x12..0x16` | `float16`, `bfloat16`, `float32`, `float64`, `float128` |
| `0x17..0x19` | `decimal32`, `decimal64`, `decimal128` |

Codes `0x1A..0x7F` are reserved for further atomic types. A `typeCode`
with its high bit clear names one assigned atomic type and consumes no
argument byte. A high-bit code in `0x80..0xFE` names a one-argument
built-in family and **must** consume the following byte as the atomic
type code for `T`. The common built-in instance therefore needs no
additional TypeSpec lookup for its argument; an instruction's `t` operand
still names the containing TypeSpec row. Assigned family codes are:

| Codes | Built-in family in increasing code order |
|---|---|
| `0x80..0x84` | `simd64<T>`, `simd128<T>`, `simd256<T>`, `simd512<T>`, scalable `simd<T>` |
| `0x85..0x88` | `simd64mask<T>`, `simd128mask<T>`, `simd256mask<T>`, `simd512mask<T>` |
| `0x89` | Provisional scalable `simdmask<T>`; name, representation and membership remain open |
| `0x8A` | `Span<T>` |
| `0x8B..0x8F` | `ref<T>`, `ptr<T>`, `box<T>`, `val<T>`, `own<T>` |

Codes `0x90..0xFE` are reserved for future built-in families; `0xFF`
is reserved as a possible extended-code escape and rejects in `(1, 0)`.
The inline `argumentCode` must be an assigned atomic code below `0x80`,
not another family code or `0xFF`; family-specific membership and legal
element types still require validation. An indexed family argument uses
an earlier complete TypeSpec: forms `0x07..0x0B` for their corresponding
representations, or form `0x0C` with the assigned `familyCode` for SIMD,
masks and `Span<T>`. These indexed forms are required when `T` is nominal,
imported, formal, qualified or itself constructed; they reject a plain
unqualified atomic `T` for which the inline form is canonical. The
indexed `familyCode` is the same `0x80..0xFE` registry value, not an
independent enum. `out` and `init ref` remain Param passing modes, not
types, and have no family codes; `ref<T>` may be their referent type
without collapsing their distinct call modes. A built-in family form
does not add a second `N` parameter: fixed SIMD width is in its family
code, while scalable `simd<T>` has its own code.

Assigning an atomic or family code identifies a type for structural
reading; it does not make unsupported widths, floating/decimal
operations, SIMD, spans or general references executable in the narrow
`(1, 0)` body profile. Reserved codes reject rather than becoming private
reader-enum ordinals.

These codes are semantic type identities, not C ABI aliases or promises of
one machine-register representation. C `long`, `wchar_t` and `long double`
depend on the target ABI; an x87 extended `float80` or built-in complex
number would require separately specified value, storage, target-support
and call-classification rules before receiving a code. `Task<T>` is a
declared completion family with ownership and async contracts, not a scalar
or a fixed task-object layout implied by an inline type code. `mut` belongs
to a place or receiver, `write` is a type qualifier, and `out`/`init ref`
are passing modes. Multi-argument callable signatures such as `func` and
`fnptr`, tuples, nominal types and arbitrary type arguments require their
own structural TypeSpec/contract references; a one-byte `T` cannot stand
in for them. SIMD mask codes above reserve candidate identities only:
mask naming, layout and lane correspondence remain under review.

The low two bits (`flags & 0x03`) of every definition's `u32 flags` encode
visibility: `0x00` private, `0x01` internal, `0x02` public; `0x03`
rejects. This applies identically to TypeDef, FieldDef, MethodDef and
PropertyDef. Their remaining bits have row-specific assignments:

| Row | Assigned bits beyond visibility | Reserved bits in `(1, 0)` |
|---|---|---|
| TypeDef | Bits 2–3 (`(flags >> 2) & 0x03`): kind `1` struct, `2` class, `3` interface; zero rejects. Bits 4–5 (`(flags >> 4) & 0x03`): layout `0` Auto, `1` Sequential, `2` Union, `3` Transparent. | Bits 6–31 |
| FieldDef | Bit 2 (`0x04`): mutable field. | Bits 3–31 |
| MethodDef, PropertyDef | None. | Bits 2–31 |

Reserved bits reject rather than silently changing a declaration's meaning.
Packing TypeDef kind, layout and visibility into one `u32` retains its
40-byte row stride. Other flag fields do not encode declaration visibility:
`Module.moduleFlags`, `Layout.flags`, `Param.flags` and `Constraint.flags`
must be zero in `(1, 0)` until their own meanings are assigned. Directory
section-policy flags, String encoding flags and Body place flags have
separate, explicitly defined domains. The PropertyDef getter MethodDef
belongs to the same declaring type, has the same result TypeSpec, and is
bound by its PropertyDef identity, never a `get_Name` spelling. Exported
TypeDefs, MethodDefs and PropertyDefs require public visibility; an exported
member also requires a public owner TypeDef. `Layout` is required only for a
published fixed representation; private `Auto` has no layout row.
`FieldDef.byteOffset` is `0xFFFF_FFFF` when
no fixed offset is published. A fixed layout validates size, alignment,
overlap, field offsets and ABI classification against its target requirement;
`Transparent` retains nominal identity while using its one carrier's direct
call classification. Equal layout is not implicit conversion.

Param modes are `0x00` by-value, `0x01` `out`, `0x02` `init ref`,
`0x03` readonly `ref`, `0x04` writable `ref`. The
[coded instruction set](il-instruction-set.md) has local place
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
range. Each Contract's `ownerDefId` must name that owner; no Contract row is
orphaned or shared between owners. Duplicates of a singleton clause reject.
The initial clause
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
across rebuilds. Internal and external imports resolve through a selected producer
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

For the `return 42` [coded-body example](il-instruction-set.md#worked-bytes-and-illustrative-size),
these are the relevant row references; row indexes are artifact-wide:

| Table row | Payload values in column order |
|---|---|
| String 1 | `flags=0, unitCount=6, ASCII content=Answer, terminator=00, padding=00` (method name and export key) |
| String 2 | `flags=0, unitCount=7, ASCII content=Example, terminator=00` (module identity) |
| Module 1 | `identity=2, targetRequirement=1, runtimePolicy=0, entryMethod=1, moduleFlags=0, featureFirst=0, featureCount=0, moduleRefStart=1, typeDefStart=1, methodDefStart=1, sourceFileStart=1` |
| TypeSpec 1 | `form=0x01, qualifiers=0, primitiveCode=0x08` (`int32`) |
| MethodSig 1 | `resultTypeSpec=1, receiverTypeSpec=0, paramFirst=0, paramCount=0, genericFirst=0, genericCount=0` |
| MethodDef 1 | `ownerTypeDef=0, name=1, signature=1, flags=2` (public), `contractFirst=0, contractCount=0, bodyRow=1` |
| Export 1 | `localDefId=0x1600_0001, exportKey=1` |
| Body 1 | Body bytes specified in the companion instruction set; ownership comes from MethodDef 1's `bodyRow`, not a Body-row owner field |

For example, the TypeSpec table with its single `int32` row is exactly
`00 00 00 00 03 00 00 00 01 00 08`: two four-byte offsets followed by the
three-byte row. Neither the Module row nor an export authorizes a caller to
skip bounds and contract compatibility checks; semantic body auditing is
governed by the consumer's trust policy.

The two String rows have offsets `00 00 00 00 0C 00 00 00 18 00 00 00`.
String 1 is the 12-byte row `00 06 00 00 41 6E 73 77 65 72 00 00`:
the first `00` selects ASCII with no other flags, `06 00 00` is the
three-byte code-unit count, and the last `00` is alignment padding after
the one-byte terminator. String 2 is
`00 07 00 00 45 78 61 6D 70 6C 65 00`, also 12 bytes.

A merger could add String 3 (`Consumer`), Module 2
(`identity=3, targetRequirement=1, runtimePolicy=0, entryMethod=0,
moduleFlags=0, featureFirst=0, featureCount=0, moduleRefStart=1,
typeDefStart=1, methodDefStart=2, sourceFileStart=1`), ModuleRef 1
(`producerIdentity=2, targetRequirement=1, runtimePolicy=0`), and
MethodDef 2 (`ownerTypeDef=0, name=1, signature=1, flags=2,
contractFirst=0, contractCount=0, bodyRow=0`).
Import 1 binds ModuleRef 1, String 1 as export key, and expected local
`DefId=0x1600_0002`; Export 1 still identifies MethodDef 1 of Module 1.
String 1, TypeSpec 1 and MethodSig 1 are shared, but the two MethodDefs
retain different owners and the consumer's expected contract is not
silently replaced by the producer's body. This adds rows to the same
artifact-wide sections, not a second directory or nested artifact. A
caller subsequently added to Module 2 could use operand `m=1` to select its
own expected MethodDef 2 (`DefId=0x1600_0002`), then Import 1 to resolve
Module 1's exported MethodDef 1. A trusted merger can instead resolve that call
directly to MethodDef 1 and omit the unused expected row and Import.

## Body table and coded-instruction boundary

Each Body row is the variable table's offset-indexed extent and carries
exactly the coded bytes defined in the
[coded instruction set](il-instruction-set.md#body-record-direct-block-index-and-regions).
Its one-based row is referenced by the MethodDef `bodyRow` that owns it. Every Body row
must be owned by exactly one local MethodDef, and an imported declaration has
no body. Body-to-method identity and type references use the metadata tables
above; block/value/region IDs are **body-local**, not metadata `DefId`s.
The coded-body `m` operand is the exception to artifact-wide metadata row
numbering. A nonzero `m` is a one-based index within the owning module's
MethodDef range; its physical row is `methodDefStart + m - 1` after checked
range arithmetic. An importing module's retained expected MethodDef is
in that same range. `m=0` is a resolved packed-call escape followed by an
exact little-endian `u32 DefId` naming an authored MethodDef of another
Module row in this artifact. Its kind must be MethodDef and its row must
exist; the escape is invalid in a single-module artifact or when targeting
an unbound external producer. It carries no extra per-call certificate:
the trusted merger checked the selected binding and semantic obligations
before replacing the import. A reader still checks the escape and target
for safe decoding, while its trust policy determines whether to audit
body-level call correctness. In an ordinary single-module artifact,
nonzero local and physical row numbers coincide; after merging, common
calls need not widen just because earlier modules added rows. A merger
may retain an Import for repeated calls instead of paying the escape on
every use. At one-byte reference width, the escape takes five bytes
instead of one; a retained Import and expected MethodDef occupy their
fixed table rows once for any number of calls. A merger can choose per
binding based on actual references and shared rows, not a universal
encoding preference. TypeSpec operands remain artifact-wide and may
widen after deduplication; a separate body-local dictionary would have its own
prelude and lookup cost and is not specified without representative
size and decode measurements.
Bodies cannot redefine a published signature, effect or result contract.

The coded instruction set assigns a narrow scalar executable subset with exact
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
  expected record kind. Each imported reference resolves to one declared
  dependency/key pair, including when the producer is packed in this artifact.
  Duplicate Module identities and owner/key export pairs reject.
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
3. Select the requested Module identity and resolve its dependency closure,
   including producers co-located in this artifact; verify each imported
   contract against the selected producer before lowering or execution.
4. Optionally audit authored bodies' use of imported contracts, CFG,
   liveness, place state, effects and ownership. Source compilers must check
   the bodies they produce; trusted binary consumers need not repeat this
   work or audit dependency bodies.
5. Reject unsupported required semantics before native code generation.

Candidate unknown-data rules are:

- Reject an unsupported format revision before interpreting sections.
- Reject unknown required kinds and unknown kinds outside the nonsemantic
  extension range; an unknown optional extension cannot affect binding or
  execution, and no core record may reference it.
- Reject undefined or registry-incompatible section policies, nonzero reserved
  fields, duplicate section kinds, and malformed directory ordering.
- Skip an unknown optional section only when its kind lies in the
  nonsemantic extension range, its flags select a permitted policy and its
  generic framing is valid. A producer cannot make a safety-relevant section
  ignorable by setting a flag.
- Before preserving a reference-slotted section, validate the prelude and
  mapped targets. Preserve its opaque payload only when every slot can be
  updated without changing target meaning; otherwise drop it. Optional
  discard-on-rewrite sections are regenerated by an aware tool or dropped.
- Reject unknown required features and all unknown instructions, types,
  constraints, qualifiers, or callable-contract forms before binding/lowering.
  A feature declaration cannot authorize behavior the reader does not
  implement.

Kind-specific schemas for future optional extensions, reserved values, and
feature declaration remain open. Optional extensions are limited to their
namespace's generic rewrite policy and, if reference-slotted, the permitted
target selectors above; a consumer that interprets an extension must also
validate its kind-specific payload.

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
row 1 (`DefId=0x1600_0001`), then clause `0x0002`, flags zero, parameter
ordinal 3 and states `(1, 0, 0)` for true,
false, and exceptional departure. A consumer can compare this declared
promise with a producer without reading the output to discover whether it
was initialized. The [coded instruction set](il-instruction-set.md)
does **not** yet encode the output place and publication operations, so this
metadata row alone does not make such a body executable.

## Unspecified schema details

The approved current design defines relationships and validation principles, but not the
following byte-level contracts:

| Surface | Not yet specified |
|---|---|
| Encoding and revisions | Future version/feature policy and reader compatibility rules. The approved current `(1, 0)` design accepts only its registered semantic forms but is not a released schema. |
| Declarations | Resource references, generic/witness exportability, further type/field/layout flags, richer qualifier/contract clauses and their validation. Module identity and export-key equality are specified; assigning a stable key to a source declaration is a writer policy. |
| Executable bodies | Beyond the [narrow executable subset and other assigned decode shapes](il-instruction-set.md#executable-subset-versus-assigned-decode-shapes): output places, exceptional handlers/cleanup, loans, roots, witness bindings and their precise verifier rules. |
| Limits | Per-record and per-artifact sizes, nesting and work budgets, allocation limits and canonical encoding requirements. |
| Unknown data | Kind-specific extension schemas, reserved values and feature negotiation; generic skip, remap and discard policies are assigned above. |
