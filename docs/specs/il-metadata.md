# Terrum semantic IL and metadata

> [!IMPORTANT]
> Approved current design for the specified `TRRM` metadata and framing
> contracts, not a released byte schema or a claim of compiler support.
> See [implementation](../implementation.md) for the current executable
> boundary. Terms such as "must" apply to the specified `(1, 0)` design;
> explicitly open contracts require further specification before use.

This document defines metadata tables and physical framing; the
[coded instruction set](il-instruction-set.md) defines bounded Body encoding
and operations.
The [unified registry](representation/registry.md) owns numeric allocations,
operand shapes and admission status; the tables here project that registry.
The [requirement and admission contract](representation/requirements-and-admission.md)
owns artifact-schema versus Module-semantic scope and complete retained
requirements. The [Module manifest](representation/semantic-manifests.md)
supplies the physical edition/Feature protocol. Feature IDs 9-12 remain
assigned at revision 1 with incomplete registrations; Feature 13
`LayoutQueries` is registered at revision 1, without implying implementation
support or closing its conditional target/generic/witness/constant gates.
The [binding and replacement contract](representation/binding-and-replacement.md)
owns selected-definition resolution and checked directional satisfaction of
complete retained expectations; it does not enable missing physical features.
The [verification contract](representation/reader-trust.md) owns producer
truth obligations, mandatory structural safe-consumption checks, and
schema-owned reference namespaces.
The [canonicality contract](representation/encoding-canonicality.md)
separates ordinary acceptance of equivalent noncanonical encodings from
canonical writers and explicit scoped audits.
The [resource-accounting contract](representation/resource-accounting.md)
defines scoped capacity, deterministic work/state and explicit exhaustion.
Portable minimum envelopes are intended; their numerical values are unassigned.
The artifact can be embedded in PE or ELF but does not require either
container.

## Definitions and conventions

| Term | Meaning in this document |
|---|---|
| `TRRM` artifact | One complete, bounded byte slice carrying semantic IL and metadata. It may be a standalone file or embedded in another container. Its extent excludes the enclosing PE/ELF file and separately referenced resource payloads. |
| Module | A project's semantic unit with its own identity. Ordinary compilation produces one `TRRM` artifact with one Module row. A trimmed or merged artifact can contain several independently identified modules sharing physical sections, without a separate assembly identity or container. |
| Artifact-relative offset | A zero-based byte position from the first byte of the `TRRM` signature. A valid artifact keeps every extent `[offset, offset + length)` within the supplied `TRRM` slice; this document does not prescribe reader behavior for invalid extents. |
| Section directory (`directory`) | The fixed-width index following the header, containing one entry per present section kind, not the sections' bytes. Each entry records its kind, flags, record count, artifact-relative offset and byte length; the entry is not part of the section it indexes. |
| Section | One contiguous artifact-relative byte extent identified by a directory entry and containing a metadata table or body stream. An absent table has no directory entry or section. |
| Row | A fixed-stride payload in a fixed table, or an extent in a variable table's offset-indexed data. Rows have one-based indexes; absent table means zero rows. |
| Typed row reference | A little-endian `u32` row index with reserved-zero high byte. The field or registered enclosing form fixes its table; there is no table-kind selector. Zero is allowed only where explicitly nullable. |
| Local definition ID (`DefId`) | A polymorphic `u32` reference: high byte is a permitted registered definition-table kind, low 24 bits are its one-based row. It is not a typed row reference or a stable cross-artifact identity. |
| Producer identity and export key | The selected dependency and published declaration identity used together to resolve an import, whether its producer is packed here or in another artifact. Equality of decoded text is specified below; source-side key assignment and authentication are separate. |
| Declared contract | Published type, callable, ownership/lifetime, access, effect and requirement facts against which consumers bind and check use. A contract is not inferred from a dependency body or source spelling. |
| Body | Typed semantic operations and control flow for an executable declaration, distinct from declaration metadata. |
| Source revision, artifact revision, native profile | Independent axes for source syntax/semantics, `TRRM` encoding and requirements, and target/runtime ABI assumptions. The enclosing container format is separate. One revision number does not imply another. |
| Required feature and optional data | A required semantic feature must be understood before binding or execution. An optional section can be skipped only if its registered kind or designated extension namespace is nonsemantic and safe to ignore. Its directory policy separately governs rewrites. |

Field tables use `u8`, `u16`, `u32` and `u64` for fixed-width unsigned
little-endian integers. Byte offsets are artifact-relative; lengths are byte
counts unless stated otherwise. Numeric assignments below describe this
approved current design. Numerical portable resource minima remain unassigned;
the selected [format-version policy](representation/format-versioning.md)
governs draft changes and released schema identities.

## Scope and conformance boundary

The semantic IL is a compact binary representation of the language and
ecosystem: types, declarations, contracts, and operations. It is intended as
input to metadata binding, reflection/tooling, linking, and AOT/JIT code
generation without requiring source recompilation. It is not merely a
serialization of a compiler's private object graph or a verifier certificate.

Source compilation checks authored bodies against their declared contracts,
and producers are responsible for emitting valid metadata and truthful
semantic facts. Before binding, code generation or execution, binary
consumers perform the structural, range, reference, schema-support and
binding checks needed to safely consume selected input. They do not
re-prove represented dominance, liveness, effect/ownership flow or other
producer-published semantic facts. The verifier's internal organization and
diagnostic wording are not prescribed. These checks do not prove arbitrary
external provider behavior or authored unsafe promises, which remain explicit
trust contracts. A merger or rewriter is responsible for correctness of the
data it changes or emits.

The accepted language contracts remain authoritative for their semantics:
[language](language.md), [lifetimes](lifetimes.md),
[conversions](conversions.md), [type families](type-families.md),
[data layout and ABI](data-layout-and-abi.md), and the
[memory model](memory-model.md). This document defines the approved current
binary design where specified; it does not redefine their rules. The
[register instruction set](il-instruction-set.md) and its Body layouts are
the approved current draft design; they do not claim compiler/runtime support
or complete capability implementation.

| Role | Responsibility |
|---|---|
| Source compiler and writer | Check authored source against declared language contracts; emit a valid artifact and truthful semantic facts. |
| Binary reader and verifier | Safely decode and verify the selected artifact and retained requirements before binding, code generation or execution; reject affected use on invalid or unsupported input. |
| Linker or executable consumer | Apply selected binding and execution semantics after required verification; external provider behavior remains a trust contract, while broader audits are implementation-selected. |

## Logical source and runtime requirements

[Logical record requirements](representation/logical-records.md) retain
the selected declaration/access/witness, failure, construction, iteration,
ownership, completion, dependency and evaluation facts independently of
these physical tables. Their missing encodings remain explicit admission
gates, not inferred uses of reserved bits or optional records.

## Format identity and revision scope

`TRRM` is the format identifier, with ASCII signature bytes `54 52 52 4D`.
The [persistence taxonomy](representation/persistence.md) identifies `.trrm`
as the normative metadata/Body interchange. A development `.trm` file can
share this signature without sharing this schema; native private containers
likewise do not select their embedded semantic schema by extension or version.
The [format-version contract](representation/format-versioning.md) uses the
existing major/minor pair as the exact stable encoding-schema identity for
released versions, without a separate schema-ID field. No released schema is
assigned yet. This beta design retains `(1, 0)`; drafts and preview builds do
not require pair updates or compatibility with one another. They exchange only
under exact out-of-band schema agreement, including the governing semantics.
Matching preview version bytes alone does not establish that agreement.
The current schema is `(1, 0)`; historical same-pair drafts are not aliases.
Released assignments are stable, and another released schema uses another
pair. Numeric ordering and equal major versions imply no decoding
compatibility or roll-forward. Each implementation chooses how it handles
input that does not select a supported schema.
The [Module semantic manifest](representation/semantic-manifests.md) separately
encodes exact governing editions and required Feature revisions; it registers no
compatibility mapping or richer capability. Source revisions and target ABI/profile
requirements remain independent of the format pair.
The current registry edition is `trrm/1-draft-layout-queries-1`. It selects one
allocation catalog, not an alternate floating/generic decoder. Draft agreement
also covers the governing semantic catalog and chapters; a registry digest is not
a complete semantic-schema identity.

The [logical admission rules](representation/requirements-and-admission.md)
select one encoding schema for the entire artifact. Each Module retains its
exact governing semantic edition and complete required feature revisions;
packing does not choose one Module's rules for the others or take a maximum
revision. Consumers establish supported semantics and complete retained
dependencies before affected binding/evaluation/execution. Exact semantics
or an explicitly specified compatibility mapping is required, not numeric
roll-forward. Module carries its governing edition and Feature range under the
manifest protocol; exact current-draft schema agreement remains out of band.

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
| 12 | Directory entry count | `u32` | Number of present sections; 26 kinds are assigned below, plus nonsemantic extension kinds |
| 16 | Total artifact length | `u32` | Exact byte length of the supplied `TRRM` slice; zero is reserved and invalid in this schema |
| 20 | Directory offset | `u32` | `32` in this design |
| 24 | Reserved | 8 bytes | All zero in a valid artifact |

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
| `0x0000` | Required semantics: the section carries required meaning. An implementation can claim support only for semantics it supports; verification rejects unknown or unsupported required content before affected use. |
| `0x0001` | Optional, self-contained: an implementation may ignore it; a valid rewrite preserves it only when the registered contract guarantees that changes elsewhere cannot invalidate it. |
| `0x0002` | Optional, reference-slotted: an implementation may ignore its payload. A valid rewrite that retains it remaps every declared reference slot without changing target meaning; optional data may instead be omitted. |
| `0x0003` | Optional, discard-on-rewrite: an implementation may ignore it; a valid changed artifact may preserve or omit it under the selected schema's rewrite contract. |

Nonzero upper bits are invalid; these policies are mutually exclusive. The section-kind
registry defines the exact policy for assigned kinds. Unassigned
kinds are skippable only in the reserved nonsemantic extension namespace
defined below; their flags cannot override that namespace policy.
Every present semantic table uses `0x0000`; SourceMap uses `0x0002`.
Empty tables are omitted rather than encoded as empty entries. The entry does
not add a minimum-reader-version field, and a matching major version does
not imply acceptance of a different minor version. A separate minimum-reader
field remains an alternative for future revision policy.

Valid-artifact framing:

- `headerLength` is 32 and `directoryOffset` is 32; the directory has exactly
  `entryCount * 16` bytes.
- `totalLength` equals the supplied `TRRM` slice length and is in
  `32..0xFFFF_FFFF`. Header, directory, and every section lie wholly within
  `[0, totalLength)`. A section begins at or after the directory end.
- Directory entries are strictly ordered by section kind, so duplicate kinds
  are invalid. Section extents do not overlap the header, directory, or one
  another. Any alignment requirement is defined by the section schema.
- Every non-padding byte belongs to the header, directory, or a section.
  Permitted alignment gaps contain zero bytes; unclaimed trailing payload is
  not part of a valid artifact.
- Sections encode exactly their declared extents and record counts.
- A present section has a positive record count. An absent registered table
  has zero rows. Fixed sections have exactly `recordCount * rowStride` bytes;
  variable and reference-slotted sections follow the index or prelude and
  data layout specified below.

These are producer validity requirements. Consumers perform the applicable
structural safe-consumption checks before binding or affected use; see
[consumer verification](representation/reader-trust.md). Diagnostic wording
and internal check order are not prescribed.

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
larger than `u32` bytes would also need wider row-data indexes. A producer
cannot use zero length to publish that format. Encoding the ordinary length in
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
This deliberately favors direct lookup:
writers and mergers may first compute row lengths, buffer rows, or backpatch
offsets. Valid artifacts keep index storage and adjacent offsets within the
section. An implementation may check selected offsets while reading; no
preflight scan of preceding or unrelated entries is required. A selected
offset and its extent are verified before affected use, and invalid selected
ranges are rejected; verification timing and diagnostics are not prescribed. The
index avoids rescanning preceding rows for subsequent AOT, JIT, interpreter
and tooling queries.

A table-specific row reference is a four-byte little-endian `u32` row index
in `1..0xFF_FFFF`, with its high byte reserved zero. The field or registered
enclosing form fixes the table: `MethodDef.signature` names MethodSig,
and `MethodDef.bodyRow` names Body. Neither has a table-kind selector.
This is not a three-byte row encoding or a request to search the containing
table. A valid table-specific row reference has a zero high byte; a producer
must not emit a nonzero high byte even when it resembles the intended table's
kind.

A complete type-valued field uses a four-byte `TypeSpecRef`, not a bare
TypeSpec row reference. Its accepted forms are:

| Form | Word | Meaning |
|---|---|---|
| TypeSpec row | `0x00_000001..0x00_FFFFFF` | One-based TypeSpec row reference; zero is null only where that field permits absence |
| Well-known type | `0xF2_0000_cc` | Assigned well-known code `cc` in the low payload byte; upper 16 payload bits are zero |
| Inline built-in family | `0xF3_qq_ff_pp` | `qq` is the TypeSpec qualifier byte, `ff` an assigned built-in-family code, and `pp` an assigned atomic argument code |

The `0xF3` form reuses TypeSpec form `0x01` family/argument and qualifier
semantics, but is valid only for an assigned one-argument family over an
assigned atomic type. Its payload bytes are `pp, ff, qq` in little-endian
order. `qq` uses the existing TypeSpec qualifier meanings and field-position
validity; reserved bits, ineligible families/arguments and invalid
qualifier combinations reject. `0xF2` and `0xF3` resolve to the same complete
type identities as their equivalent TypeSpec forms.

Use `TypeSpecRef` in every field whose schema denotes a complete type,
including nested TypeSpec values and `FieldPath.rootTypeSpecRef`.
`TypeSpec.form 0x05`'s `genericTypeSpec`
remains a row reference to the nominal generic definition; constant formal
arguments remain ConstantSource references. Other table rows, DefIds, ranges,
MethodDef/Import identities and TypeArg owners do not become TypeSpecRefs.
The all-zero word remains null only where the particular field permits it.
Body TypeMaps accept these same `0xF2`/`0xF3` complete-type forms in addition
to their assigned row and Body-local pseudo-type tokens.

A genuinely polymorphic `DefId`, such as `Export.localDefId`, instead uses
`(definitionKind << 24) | row`. Its high byte selects a permitted registered
definition kind: TypeDef `0x13`, FieldDef `0x14`, MethodDef `0x16`,
GenericParam `0x19`, PropertyDef `0x1E` or UnionCaseDef `0x21`, restricted further by the
field's actual target set. Typed MethodDef row 1 is `0x0000_0001`;
an Export naming that method uses `0x1600_0001`. A valid polymorphic field uses a permitted nonzero kind. The
all-zero word is null only where explicitly permitted. Other kinds, including
`0xFF` (reserved for a future required indirection encoding), are invalid
producer output and are not defined or accepted here.

The Body `TypeMap` and `MethodMap` use a fixed-width token schema. Token words are
`(kind << 24) | value`, with globally fixed eight-bit kinds and a 24-bit
row/payload. The assigned Body-map token kinds are TypeSpec row `0x11`,
TypeDef row `0x13`, MethodDef row `0x16`, Import row `0x1C`, pseudo type
`wrapper<generic<T>>` `0xF0`, pseudo type `generic<wrapper<T>>` `0xF1`, and
well-known type `0xF2` and inline built-in family `0xF3`. Map context restricts TypeDef/Import/TypeSpec tokens
to complete types and MethodDef/Import tokens to methods; an Import's expected
definition and contract are retained. `0xF0`/`0xF1` are Body-map-only because
their base payload is a zero-based TypeMap index; `0xF2`/`0xF3` also encode
complete types in TypeSpecRef fields. Ordinary row-token values are
one-based. The `0xF0`/`0xF1` payloads use a zero-based TypeMap index and fixed
eight-bit family/modifier fields; `0xF2` uses an eight-bit well-known code in
the low payload byte, with the upper 16 bits zero. `0xF3` uses the TypeSpecRef
payload described above. `0xF4..0xFE` are unassigned, and `0xFF` remains
reserved for a future required indirection.

The Body-local FieldMap uses a separate four-byte token union:

```text
FieldMapToken:u32 =
    DefId(kind=0x14, row)
  | ImportToken(kind=0x1C, row)          // expected definition is FieldDef
  | FieldPathToken(kind=0x1D, row)        // section 0x004B
```

Rows are one-based `1..0xFF_FFFF` in the selected FieldDef, Import or
FieldPath table. `0x14` is a FieldDef DefId definition-kind, not a global
token kind. FieldPath token kind `0x1D` is distinct from metadata section
kind `0x001D` Export and is valid only in FieldMap. The direct
FieldDef/Import forms are the compact one-field path only when their exact
root is the declaring non-generic nominal type; constructed, qualified,
generic or nested paths use a FieldPath row.
No compressed inline multi-step token or dedicated FieldRef table is assigned;
the direct one-field form and shared FieldPath table are the compact encodings
for this schema. Cross-module field binding uses the existing generic Import
row with its FieldDef target contract; a field-specific lookup/reference table
would require a separately reviewed schema amendment.

The global FieldPath steps and `TargetLayoutDependencyField` rows use the
four-byte `FieldReference` subset of this union:

```text
FieldReference:u32 =
    DefId(kind=0x14, row)
  | ImportToken(kind=0x1C, row)          // expected definition is FieldDef
```

Its one-based row is bounded by the selected FieldDef or Import table.
FieldReference excludes the FieldPath-token branch; nested Path signatures
are already flat ordered step lists.

For a `0xF2` well-known type token, codes `0x00..0x19` use the same
identities and assignments as the atomic built-in code table below. The
remaining assigned codes are:

| Well-known code | Type identity |
|---:|---|
| `0x1A` | Existential |

Codes `0x1B..0xFF` are unassigned in this well-known-type namespace; nominal
exception types use ordinary TypeMap TypeDef/Import/TypeSpec tokens. These
codes are not the TypeSpec form, opcode, or token-kind namespaces; for
example, code `0x1A` here does not assign atomic TypeSpec code `0x1A`.

For a `0xF3` inline built-in-family token, the 24-bit payload is
`qualifiers:u8, familyCode:u8, atomicArgumentCode:u8` from most to least
significant, so little-endian bytes are `argumentCode, familyCode,
qualifiers, 0xF3`. `familyCode` reuses an assigned one-argument family from
TypeSpec form `0x01`; the current families are `0x80..0x84`, `0x8A`, and
`0x8B..0x8F`. `atomicArgumentCode` must be an assigned atomic code below
`0x80` and must satisfy that family's element-type rules. Atomic types use
`0xF2` rather than a duplicate `0xF3` spelling. The qualifier byte has the
same meanings and reserved bits as TypeSpec; `init` is valid only when the
TypeSpecRef is the immediate argument of indexed Span family `0x8A`.
`0xF3` identifies the same complete type as TypeSpec form `0x01` with those
qualifiers, family and atomic argument.

A contiguous range is `u32 firstRow, u32 count`; its empty encoding is
`(0, 0)`, otherwise `firstRow` is nonzero, has a zero high byte, and the
checked last row is at most `0xFF_FFFF` and belongs to that table.
Ranges of uniquely owned rows must not overlap or include another owner's rows.
Feature manifest lists instead permit immutable sharing under their
[table-specific rules](representation/semantic-manifests.md#3-rows-ranges-and-ordinary-encodings).
These are producer-validity rules. Consumers perform applicable structural
safe-consumption checks before binding or affected use; see the
[consumer-verification contract](representation/reader-trust.md). Diagnostic
wording and internal check order are implementation choices.
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
| 0 | Target section kind | `u16` | Selector 0: assigned core table containing the target. Selector 1: zero; its target is inherently Body |
| 2 | Selector | `u16` | `0` row, `1` Body instruction |
| 4 | Target row | `u32` | One-based row in the target table, high byte zero |
| 8 | Block index | `u16` | Zero for selector 0; zero-based Body block index for selector 1 |
| 10 | Reserved | `u16` | Zero |
| 12 | Instruction ordinal | `u32` | Zero for selector 0; zero-based operation/terminator ordinal within the block for selector 1 |

The prelude in a valid artifact fits entirely within its section, with
`4 + slotCount * 16` representable using checked arithmetic. Each accessed
slot has a supported selector and a present target row of its schema-owned
table. Selector 1 has no table choice and a valid slot has zero
section-kind, including not using `0x0020`. These are producer-validity rules;
consumers structurally validate the selected prelude, selectors, targets and
cross-section references before affected use. A
section's registry or extension
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
| `0x0001` | Module | One row per logical module: identity, governing edition, required Features, target/runtime and entry point |
| `0x0002` | String | Offset-indexed, tagged ASCII/UTF-8/UTF-16LE/UTF-32LE rows |
| `0x0003` | Blob | Variable opaque non-reference bytes, interpreted only by their referring field |
| `0x0004` | Feature | Required semantic feature identifiers |
| `0x0010` | ModuleRef | A consumer module's selected producer identity, in this artifact or external |
| `0x0011` | TypeSpec (variable) | Complete type reference or constructed type |
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
| `0x0021` | NativeImport | Native callable/data import binding |
| `0x0022` | NativeExport | Explicit native method/data export |
| `0x0023` | LayoutField | Ordered field membership in one complete layout variant |
| `0x0024` | InterfaceMethod | Ordered directly-declared interface method |
| `0x0025` | InterfaceDef | Interface declaration and separate typed declaration ranges |
| `0x0026` | InterfaceBase | Ordered complete base-interface instance |
| `0x0027` | AssociatedTypeDef | Associated-type declaration and optional default |
| `0x0028` | InterfaceWitness | Complete implementing/interface instance and binding ranges |
| `0x0029` | WitnessMember | Exact selected target for one interface requirement |
| `0x002A` | WitnessAssociatedType | Typed associated-type binding for one exact interface instance |
| `0x002B` | BuilderRegistration | Target-owned construction recipe identity |
| `0x002C` | TextTarget | Exact eligible target method parameter |
| `0x002D` | TextContentShape | Generated content shape and ordered ranges |
| `0x002E` | TextCaptureShape | Typed capture and acquisition mode |
| `0x002F` | TextFragmentShape | Literal or formatted fragment structure |
| `0x0030` | ConstantSource | Shared typed frozen-value/evaluation source |
| `0x0031` | InterfaceConstDef | Initialized interface-owned constant |
| `0x0032` | AssociatedConstDef | Uninitialized associated-constant requirement |
| `0x0033` | WitnessAssociatedConst | Exact witness binding for an associated constant |
| `0x0034` | UnionCaseDef | First-class nominal union-case identity |
| `0x0035` | UnionCaseField | Ordered typed payload slot for a union case |
| `0x0036` | AssociatedRelation | Exact interface-instance associated-type relation |
| `0x0037` | PredicateGraph | One published predicate expression graph |
| `0x0038` | PredicateReadFootprint | Graph-owned actual roots and direct dependencies |
| `0x0039` | PredicateReadRoot | One typed logical read root |
| `0x003A` | PredicateReadDependency | One direct call/witness dependency |
| `0x003B` | PredicateNode | One evaluated expression occurrence |
| `0x003C` | PredicateNodeOperand | One typed non-expression operand |
| `0x003D` | PredicateChildEdge | Ordered expression-child relationship |
| `0x003E` | PredicateCapture | Graph-local pattern capture and complete type |
| `0x003F` | FrozenValueNode | Recursive typed frozen-value node |
| `0x0040` | FrozenValueChild | Typed aggregate/tuple child |
| `0x0041` | ImmutableData | Resolved constant or published immutable region identity |
| `0x0042` | ConstantExecution | One required constant-evaluation request |
| `0x0043` | ConstantExecutionArgument | One transformed request input |
| `0x0044` | ConstantExecutionDependency | One direct executed dependency |
| `0x0045` | TargetLayoutDependency | One target-layout query occurrence |
| `0x0046` | LogicalInputView | Typed backing and referent view |
| `0x0047` | LogicalViewPathEntry | One ordered logical path selector |
| `0x0048` | TargetProfile | Exact registered target/layout/ABI identity |
| `0x0049` | TargetValueBinding | Completed target-dependent frozen result |
| `0x004A` | TargetLayoutDependencyField | One FieldDef/Import reference in an owned offset-path range |
| `0x004B` | FieldPath (variable) | One complete root type and ordered hierarchy of FieldDef/Import references |
| `0x7F00` | SourceMap | Optional nonsemantic body-to-source mapping |

Section kind `0x0012` remains unassigned; the earlier `TypeArg` child-table
candidate is retired. Native boundary kinds `0x0021..0x0024` are assigned by
this schema. Features 9-12 remain assigned with registration pending;
Feature 13 is registered for its listed query/identity/path/binding scope;
assignment alone does not authorize independent use.

`Module` is always present with at least one row; ordinary one-project output
has one row. A merger may retain several module rows and deduplicate shared
physical data without conflating their nominal identities or export keys.
Other known tables are absent when empty, not encoded as empty directory
entries. The current governing edition is `1`; native Features 1-7 are
registered at revision 1, while Features 9-12 remain assigned with
registration pending and Feature 13 is registered at revision 1. A Module with no additional registered feature uses
`(0, 0)`, but the empty range does not claim that its producer or consumer
implements every accepted TRRM 1.0 contract. The
[manifest protocol](representation/semantic-manifests.md) defines exact
requirements and complete capability-registration gates; a number alone supplies
no missing codec or semantics. Kinds `0x8000..0xFFFF` are
reserved for **nonsemantic optional extensions**. In a valid artifact, an
unrecognized kind in this range has flags `0x0001`, `0x0002` or `0x0003`.
An implementation may ignore such a section; a consumer interpreting or
preserving it is responsible for the correctness of its output.
`0x0002` extensions use only selector 0 to refer to assigned core tables or selector
1 to refer to Body instructions; their payload uses slots for all such
references. They cannot define executable behavior, contract obligations,
or new interpretations of core rows, and core sections cannot refer to them.
Required-policy entries or unknown kinds outside this range are not assigned
by this schema. Numeric gaps below this range are reserved, not available for
execution semantics. A
required feature uses full-width `u32 featureId, u32 ruleRevision` fields in
Feature. Both identities are nonzero; `ruleRevision` is exact, not an "at least"
compatibility rule.
The [logical requirement contract](representation/requirements-and-admission.md)
requires exact semantic support or an explicit applicable mapping. Until a
complete capability and all of its required physical layers are registered,
a producer cannot emit an unrecognized semantic form by merely writing a
feature number.
`SourceMap` is skippable for execution, not uninterpretable for tools. Its
reference-slot prelude uses selector 1 for Body instruction locations and
selector 0 for SourceFile rows. After the prelude, its fixed 20-byte rows
are `u32 instructionSlot, u32 sourceFileSlot, u32 startUtf16,
u32 lengthUtf16, u8 stepRole, u8 flags=0, u16 reserved=0`. `stepRole` is
`0=whole instruction`, `1=A-negation`, `2=B-negation`, `3=inner operation`,
`4=C-negation`, or `5=outer operation`; roles 1..5 are valid only for the
assigned arithmetic-recipe instructions. Each slot must have the stated
selector and target kind; the section length is exactly
`4 + slotCount * 16 + recordCount * 20`.
Canonical writers sort rows lexicographically by `instructionSlot`,
`stepRole`, `sourceFileSlot`, `startUtf16` and `lengthUtf16`, omitting exact
duplicate rows. Ordinary readers may accept a different order or duplicate
rows when they are otherwise valid. Ordinal zero means the first operation,
`operationCount` means its terminator. SourceMap-aware tools may audit that each block and ordinal exists;
opaque-preserving rewriters must discard the section when they cannot
establish that changed Body locations retain the same meaning. A remapping
must not merge two referenced instruction locations into one.
These extents address the [decoded source snapshot](language/source-input.md#coordinates-and-origin-mapping),
not original UTF-8 bytes or a consumed file signature. A byte-input failure
has no fabricated snapshot SourceMap. A SourceFile content digest must keep
its assigned algorithm/input contract; equal decoded text does not make
original bytes or their origin maps identical.

SourceFile paths are identifiers, not permissions to read a local file;
`digestBlob` is an optional exact-content digest whose algorithm requires
an assigned feature before nonzero use. Valid producer spans have checked end arithmetic and need not imply that
source bytes are embedded. A consumer using optional source mappings verifies
the selected spans, references and assigned digest contract before exposing
them; an unused nonsemantic mapping need not be scanned.
Dropping SourceMap does not
change execution or contracts; tools preserving debug data must also retain
or remap its referenced SourceFile rows.

For one mapping from Body row 1, block index 0, operation ordinal 0 to SourceFile row 1,
UTF-16 span `[0, 2)`, the SourceMap section has two slots and one row
(`recordCount=1`), for 56 bytes:

```text
02 00 00 00
00 00 01 00 01 00 00 00 00 00 00 00 00 00 00 00
1F 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00
01 00 00 00 02 00 00 00 00 00 00 00 02 00 00 00 00 00 00 00
```

Moving Body row 1 to row 3 changes only slot 1's `targetRow`; the row's
`instructionSlot=1` and the rest of the payload remain unchanged, provided
the referenced instruction still has the same meaning.

The next tables show **row payloads**, excluding the variable section's offset
index. `str` and `blob` mean one-based rows in their respective tables;
zero is allowed only where a column says `?`. A String row has the physical
encoding below; valid name, path and key rows contain well-formed scalar text
and compare by ordinal scalar sequence without normalization, even when the
rows use different encodings. Producers are responsible for this validity.
Blob rows are bytes only: each use names its interpretation, and a valid
producer supplies content conforming to it. A Blob payload must not encode a
semantically meaningful table
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
one byte, UTF-16LE units two and UTF-32LE units four. A valid row's extent
contains its header, counted content, terminator and required padding, and
agrees with the adjacent row offset.
The offset index addresses whole rows, not content bytes; its difference
includes the header, terminator and padding. The padding and terminator are
zero. Content
begins four bytes after the row start and is therefore 4-byte aligned in the
artifact for every encoding. The terminator makes eligible NUL-free content
convenient for interop, but does not replace the explicit count.

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
needed. The counted extent in valid producer output ends before the required
terminator and agrees exactly with the next indexed row offset after padding.

Bits 0–1 of `flags` select the encoding. Bits 2–7 are reserved and are zero
in valid `(1, 0)` producer output. All four
encoding values are assigned, so a further encoding needs an explicitly
defined future extension rather than interpreting reserved bits as more
encoding-kind bits. There is no validity or normalization bit. A name,
path or key in a valid artifact decodes to well-formed scalar text: UTF-8
uses shortest sequences; UTF-16 has no unmatched surrogates; UTF-32 has no
surrogates or values above `0x10FFFF`; ASCII has no high-bit bytes. Producers
of name/path/key rows omit embedded zero scalars. Consumers reject invalid
scalar text in selected names, paths or keys before binding or use. The
explicit length
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

The mapped content can back an immutable typed `TextSpan` only
while its artifact backing remains live. A String row is not directly a
`Text` object with `nint _length` followed by inline data: its header has
a kind and a 24-bit code-unit count, not a target-width native length, and
the row belongs to the artifact rather than a runtime text object. A runtime
may copy/transcode into owned `Text`
or explicitly retain the artifact lease in a compatible text representation;
zero-copy ownership, GC rooting and direct aliasing are not implied.

| Table | Ordered row payload |
|---|---|
| Module | `str identity, u32 governingRuleEdition, u32 targetRequirement, u32 runtimePolicy, u32 entryMethod?, u32 moduleFlags, u32 featureFirst, u32 featureCount, u32 moduleRefStart, u32 typeDefStart, u32 methodDefStart, u32 sourceFileStart` |
| Feature | `u32 featureId, u32 ruleRevision` |
| ModuleRef | `str producerIdentity, u32 targetRequirement, u32 runtimePolicy` |
| TypeSpec (variable) | `u8 form, u8 qualifiers`, followed by only the operands specified for that form below |
| TypeDef | `str namespace, str name, u32 flags, u32 fieldFirst, u32 fieldCount, u32 genericFirst, u32 genericCount, u32 layoutFirst, u32 layoutCount, u32 contractFirst, u32 contractCount` |
| FieldDef | `u32 ownerTypeDef, str name, u32 typeSpec, u32 flags` |
| Layout | `u32 ownerTypeDef, u32 targetProfileRef?, u8 layoutKind, u8 flags, u16 reserved=0, u32 alignment, u32 pack, u32 layoutFieldFirst, u32 layoutFieldCount` |
| LayoutField | `u32 ownerLayout, u32 ordinal, u32 fieldDef` |
| MethodDef | `u32 ownerTypeDef?, str name, u32 signature, u32 flags, u32 contractFirst, u32 contractCount, u32 bodyRow?` |
| MethodSig | `u32 resultTypeSpec, u32 receiverTypeSpec?, u32 paramFirst, u32 paramCount, u32 genericFirst, u32 genericCount` |
| Param | `u32 ownerMethodSig, u32 ordinal, u32 typeSpec, u8 mode, u8 flags, u16 reserved=0` |
| GenericParam | `u32 ownerDefId, u32 ordinal, str name, u32 constraintFirst, u32 constraintCount, u8 formalKind, u8 ownershipDomain, u8 nullabilityDomain, u8 reserved=0, u32 constantTypeSpec?, u32 defaultArgumentRef?` |
| Constraint | `u32 ownerGenericParam, u16 kind, u16 flags=0, u32 payload` |
| Import | `u32 moduleRef, str exportKey, u32 expectedLocalDefId` |
| Export | `u32 localDefId, str exportKey` |
| PropertyDef | `u32 ownerTypeDef, str name, u32 typeSpec, u32 getterMethodDef, u32 flags` |
| SourceFile | `str path, u32 digestBlob?` |
| NativeImport | `u8 ownerKind, u8 resolutionMode, u8 resolutionPolicy, u8 callConv, u32 ownerRef, u32 libraryName, u32 entryName?` |
| NativeExport | `u8 ownerKind, u8 flags=0, u16 reserved=0, u32 ownerRef, u32 entryName?, u32 nativeCallableContract?` |
| InterfaceMethod | `u32 ownerInterfaceDef, u32 ordinal, u32 methodDef` |
| InterfaceDef | `u32 ownerTypeDef, u32 selfGenericParam?, u32 baseFirst, u32 baseCount, u32 methodFirst, u32 methodCount, u32 associatedTypeFirst, u32 associatedTypeCount, u32 associatedConstFirst, u32 associatedConstCount, u32 interfaceConstFirst, u32 interfaceConstCount` |
| InterfaceBase | `u32 ownerInterfaceDef, u32 ordinal, u32 baseTypeSpec` |
| AssociatedTypeDef | `u32 ownerInterfaceDef, str name, u32 defaultTypeSpec?` |
| InterfaceWitness | `u32 implementingTypeSpec, u32 interfaceTypeSpec, u32 memberFirst, u32 memberCount, u32 associatedTypeFirst, u32 associatedTypeCount, u32 associatedConstFirst, u32 associatedConstCount` |
| WitnessMember | `u32 witnessRow, u32 declaringInterfaceTypeSpec, u32 requirementMethodDef, u32 selectedOwnerTypeSpec?, u32 selectedMethodDef` |
| WitnessAssociatedType | `u32 witnessRow, u32 declaringInterfaceTypeSpec, u32 associatedTypeDefId, u32 valueTypeSpec, u32 bindingOrigin` |
| BuilderRegistration | `u32 targetTypeDef, u32 schemaTypeSpec, u32 recipeTypeSpec` |
| TextTarget | `u32 ownerMethodDef, u32 schemaTypeSpec, u32 parameterOrdinal` |
| TextContentShape | `u32 generatedTypeDef, u32 captureFirst, u32 captureCount, u32 fragmentFirst, u32 fragmentCount` |
| TextCaptureShape | `u32 shapeRow, u32 ordinal, u32 typeSpec, u8 mode, u8 flags=0, u16 reserved=0` |
| TextFragmentShape | `u32 shapeRow, u32 ordinal, u8 kind, u8 flags=0, u16 reserved=0, u32 valueCapture?, u32 alignmentCapture?, u32 formatCapture?, u32 formatterWitness?, u32 formatterConstraint?, u32 formatterMethodDef?, u32 writerRequirementMethodDef` |
| ConstantSource | `u32 kind, u32 payloadRef` |
| InterfaceConstDef | `u32 ownerInterfaceDef, str name, u32 constantTypeSpec, u32 constantSourceRef` |
| AssociatedConstDef | `u32 ownerInterfaceDef, str name, u32 constantTypeSpec` |
| WitnessAssociatedConst | `u32 witnessRow, u32 declaringInterfaceTypeSpec, u32 associatedConstDefId, u32 constantSourceRef` |
| UnionCaseDef | `u32 ownerTypeDef, u32 ordinal, str name, u32 payloadFirst, u32 payloadCount` |
| UnionCaseField | `u32 ownerUnionCaseDef, u32 ordinal, str name, u32 typeSpec` |
| AssociatedRelation | `u32 declaringInterfaceTypeSpec, u32 associatedTypeDefId, u32 requiredTypeSpec` |
| PredicateGraph | `u32 rootNodeRef, u32 readFootprintRef` |
| PredicateReadFootprint | `u32 rootFirst, u32 rootCount, u32 dependencyFirst, u32 dependencyCount` |
| PredicateReadRoot | `u8 rootKind, u8 flags=0, u16 reserved=0, u32 rootIdentity` |
| PredicateReadDependency | `u8 dependencyKind, u8 flags=0, u16 reserved=0, u32 dependencyRef` |
| PredicateNode | `u32 nodeKind, u32 resultTypeSpec, u32 operandFirst, u32 operandCount` |
| PredicateNodeOperand | `u8 operandKind, u8 flags=0, u16 reserved=0, u32 operandRef` |
| PredicateChildEdge | `u32 parentNodeRef, u32 childOrdinal, u32 childNodeRef` |
| PredicateCapture | `u32 graphRef, u32 ordinal, u8 role, u8 flags, u16 reserved=0, u32 capturedTypeSpec` |
| FrozenValueNode | `u32 typeSpec, u8 kind, u8 flags=0, u16 reserved=0, u32 scalarBlobRef?, u32 childFirst, u32 childCount, u32 activeUnionCaseDef?` |
| FrozenValueChild | `u32 slotKind, u32 slotRef, u32 childNodeRef` |
| ImmutableData | `u8 kind, u8 flags=0, u16 reserved=0, u32 declarationDefId, u32 ownerTypeSpec?` |
| ConstantExecution | `u8 sourceKind, u8 flags=0, u16 reserved=0, u32 selectedSourceRef, u32 resultTypeSpec, u32 targetProfileRef, u32 substitutionFirst, u32 substitutionCount, u32 argumentFirst, u32 argumentCount, u32 executedDependencyFirst, u32 executedDependencyCount, u32 targetLayoutDependencyFirst, u32 targetLayoutDependencyCount, u64 semanticSteps, u64 activeFrames, u64 liveStorageBytes, u64 encodedResultBytes` |
| ConstantExecutionArgument | `u8 kind, u8 flags=0, u16 reserved=0, u32 sourceRef` |
| ConstantExecutionDependency | `u8 kind, u8 flags=0, u16 reserved=0, u32 targetRef` |
| TargetLayoutDependency | `u8 queryKind, u8 flags=0, u16 reserved=0, u32 subjectTypeSpec, u32 selectedTargetProfileRef, u32 fieldFirst, u32 fieldCount` |
| TargetLayoutDependencyField | `u32 fieldReference` (`DefId(kind=0x14,row)` or `ImportToken(kind=0x1C,row)` expecting FieldDef) |
| FieldPath (variable) | `u32 rootTypeSpecRef, u32 fieldCount, fieldCount ordered u32 FieldReferences` |
| LogicalInputView | `u8 backingKind, u8 flags=0, u16 reserved=0, u32 backingRef, u32 referentTypeSpec, u32 pathFirst, u32 pathCount` |
| LogicalViewPathEntry | `u8 selectorKind, u8 flags=0, u16 reserved=0, u32 selectorRef, u64 indexOrRangeStart, u64 rangeCount` |
| TargetProfile | `u32 profileId, u32 ruleRevision` |
| TargetValueBinding | `u32 frozenValueRootRef, u32 targetProfileRef, u32 layoutDependencyFirst, u32 layoutDependencyCount` |

In these physical row layouts, each four-byte field named `typeSpec`,
`*TypeSpec` or `*typeSpec` is a `TypeSpecRef`, including nested complete-type
references. `TypeSpec.genericTypeSpec` is the row-specific nominal generic
definition reference. `GenericParam.defaultArgumentRef` is a TypeSpecRef for a
type formal and a ConstantSource reference for a constant formal.
`Constraint.payload` is a TypeSpecRef only for variants whose payload is a
complete type; other variants retain their kind-specific reference.

Each `FieldPath` variable row is a complete structural path signature. Its
`rootTypeSpecRef` is a required complete TypeSpecRef, `fieldCount` is
nonzero, and the row extent is exactly `8 + 4*fieldCount` bytes. The ordered
FieldReferences are FieldDef DefIds (`0x14`) or Imports (`0x1C`) whose
expected definition is a FieldDef; FieldPath rows cannot recursively contain
FieldPath tokens. A row has no Module cursor, like TypeSpec, so each
FieldReference and Import must resolve in every using Module context.
Imported expectations remain distinct even if they bind to the same provider
field. FieldPath rows may be shared only when their complete root and ordered
field identities/contracts agree. FieldMap's `0x1D` FieldPath token names
the one-based row; it is not a FieldReference accepted in `0x004A`.

`TargetLayoutDependency` (`0x0045`) retains its 20-byte row and query kinds
`1=SizeOf`, `2=AlignOf`, `3=OffsetOfTypePath`, `4=OffsetOfPlace`.
`subjectTypeSpec` is a complete TypeSpecRef and
`selectedTargetProfileRef` is a concrete nonzero TargetProfile row.
Size/alignment rows require `(fieldFirst,fieldCount)=(0,0)`. Offset rows
require a nonempty ordered range in `TargetLayoutDependencyField` (`0x004A`);
each range is one-based, checked within that table, exclusively owned by
that observation, and not shared. A `0x004A` row is one FieldReference:
FieldDef DefId kind `0x14` or Import token kind `0x1C` whose expected
definition is FieldDef. Expand FieldPath tokens to these ordered references
when retaining an observation; do not store Body-map or FieldPath-table
indexes in `0x004A`. The existing owning request/value relationship supplies
the Module context for imported references.

Fixed row strides and fields for assigned tables are owned by the
[machine-readable registry](representation/trrm-1.registry.json) and
projected in the table above. String, Blob, TypeSpec, Contract and Body use
offset indexes; SourceMap has the reference-slot prelude and 20-byte rows
defined above. An incorrect stride or unconsumed field is outside this
schema. Empty namespace strings
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
exactly once. A TypeDef's nonempty `layoutFirst/layoutCount` names its
complete Layout range. Each type has exactly one default row
(`targetProfileRef=0`) and at most one row for each exact registered
TargetProfile; rows are owned by that TypeDef and are not shared. Each
Layout's `layoutFieldFirst/layoutFieldCount` names its complete ordered
LayoutField list. Every LayoutField identifies a FieldDef of the same
TypeDef, uses its contiguous zero-based ordinal, and occurs at most once in
that variant. No variant is a sparse patch over another. Constructed
TypeSpec form `0x05` carries one ordered `u32` argument reference per formal
in the referenced generic definition; the formal range supplies the count,
with no separate count or `TypeArg` child table. A type formal's argument
references a TypeSpec row; a constant formal's argument references a
ConstantSource row. TypeSpec form `0x0F` identifies an existential type
argument; its hidden concrete type and required witness evidence remain
per-value facts rather than another inline argument tag. All referenced
TypeSpecs and typed constant sources must resolve in the same module context,
and structural references remain acyclic.

The remaining fixed-row columns have these `(1, 0)` valid-artifact rules:

| Row | References, values and ownership |
|---|---|
| Module | `identity` and exact nonzero `governingRuleEdition` are required; `entryMethod?` is a MethodDef in this module. `featureFirst/featureCount` names its explicit manifest list under the common checked row-range rules; edition `1` has no baseline Feature requirement, while optional native capabilities are declared explicitly. `moduleFlags=0`; `targetRequirement` and `runtimePolicy` use the assigned values below. The four `*Start` fields are cursors into their respective module-partitioned tables, not nullable row references. |
| Feature | Nonzero full-width `featureId` and exact `ruleRevision` name a coherent registered capability/revision, not rows or numeric minimums. Each row belongs to at least one Module manifest; shared rows/lists retain every using Module's independent interpretation and requirements. |
| ModuleRef | `producerIdentity` is required, and requirement/policy must match the selected producer; ownership follows `Module.moduleRefStart`. An in-artifact identity selects exactly one Module. |
| TypeDef | `namespace` may name an empty String row; `name` must be nonempty. `flags` contains visibility and nominal kind only. `layoutFirst/layoutCount` names exactly one default Layout and its exact-profile variants; field, generic and contract ranges have exactly the owners described here. |
| FieldDef | `ownerTypeDef`, `name` and `typeSpec` are required. Flags permit visibility, mutable (`0x04`) and static (`0x08`) only. Static fields do not appear in instance LayoutField lists. |
| Layout | `ownerTypeDef` is required; `targetProfileRef=0` denotes the one default variant, otherwise it names one exact TargetProfile. `layoutKind` is `0=Auto`, `1=Sequential`, `2=Union`, `3=Transparent` or `4=BlittableInterface`; flags permit only the Blittable commitment bit `0x01`; `reserved=0`. Alignment and packing are declared inputs; zero means omitted for alignment and not applicable for packing. Physical size, final alignment and field offsets are derived by the selected target ABI and are not serialized. Sequential/Union packing is normalized to `1/2/4/8/16`, with source omission meaning 16. |
| LayoutField | `ownerLayout`, `fieldDef` and zero-based `ordinal` are required. The field must be an instance FieldDef of the Layout's TypeDef; a field occurs at most once in one variant. |
| NativeImport | `ownerKind` is `1=MethodDef` or `2=FieldDef`, and `ownerRef` names that exact static declaration. `libraryName` is a nonempty ASCII String; `entryName=0` means the exact declaration name, otherwise it is an exact ASCII symbol name. `resolutionMode` is `1=Link` or `2=Dynamic`; policy `0` is required for Link and Dynamic uses `1=Default` or `2=CompatibilitySearch`. Callable methods use calling convention `1=Unmanaged`, `2=Cdecl`, `3=Stdcall` or `4=Vectorcall`; data fields use zero. There is at most one row per owner. |
| NativeExport | `ownerKind` is `1=MethodDef` or `2=FieldDef`, with exact static `ownerRef`; `entryName=0` means the exact declaration name. Method rows require `nativeCallableContract` to name the owner's unique `0x000C` clause; field rows require zero. `flags` and `reserved` are zero. Export names are unique within one output image. |
| InterfaceDef | `ownerTypeDef` and `selfGenericParam?` identify the exact interface owner/self formal; all declaration ranges belong to this InterfaceDef. `methodFirst/methodCount` names directly-declared InterfaceMethod rows in source declaration order. |
| InterfaceMethod | `ownerInterfaceDef` names the owning interface, `ordinal` is contiguous from zero, and `methodDef` names a directly declared instance MethodDef owned by the same TypeDef. Inherited order is derived from ordered InterfaceBase rows and the selected target ABI; this is declaration order, not a serialized vtable slot. |
| MethodDef | `name` and `signature` are required; `ownerTypeDef?` is zero for a module-level callable. Contract rows have this MethodDef as owner. `bodyRow?` names its unique Body; retained imported expectations have no Body. Only visibility bits are assigned. |
| MethodSig | `resultTypeSpec` is required (the built-in `void` TypeSpec denotes the zero-data unit result); `receiverTypeSpec?` is zero when absent. Param and generic ranges follow the ownership rules above; ordinary unit parameters remain logical parameters. |
| Param | `ownerMethodSig` and `typeSpec` are required. `mode` and `flags` use their assigned registries below, and `reserved=0`; ordinal is zero-based within the owning range. |
| GenericParam, Constraint | `ownerDefId` names a TypeDef or MethodDef and `ownerGenericParam` names a formal, respectively. Names are nonempty and ordinals are zero-based within the owning range. `formalKind` is `1=type` or `2=constant`; type formals carry valid nonzero ownership/nullability domains and zero `constantTypeSpec`, while constant formals carry zero domain tags, a nonzero value-domain TypeSpec and no type constraints. `defaultArgumentRef=0` means no default; otherwise it targets a TypeSpec for a type formal or a ConstantSource for a constant formal. `Constraint.flags=0`; kinds `1=interface instance`, `2=intrinsic property` and `3=associated relation` select a nonzero kind-specific payload. |
| Import | `moduleRef` names a ModuleRef owned by the expected definition's module; `exportKey` is nonempty and `expectedLocalDefId` names that module's TypeDef, MethodDef, PropertyDef or FieldDef with no locally authored implementation. A FieldDef expectation is a stub in its expected owner's checked contiguous field range; bind its owner and field to the exact selected provider declarations before use. |
| Export | `localDefId` names this module's public TypeDef, MethodDef, PropertyDef or FieldDef, not an imported expectation; `exportKey` is nonempty and unique within this module's producer identity. An exported member's owner TypeDef is public. |
| PropertyDef | `ownerTypeDef`, `name`, `typeSpec` and `getterMethodDef` are required; only visibility bits are assigned. The getter and result must match as specified above. |
| SourceFile | `path` is required nonempty decoded text; `digestBlob?=0` until a digest algorithm is assigned. The Module partition owns the row; paths convey no filesystem authority. |

FieldDef Import/Export is metadata declaration binding, not native data
binding. The expected FieldDef stub belongs to the consumer Module and its
expected owner TypeDef; its owner binds independently through the selected
ModuleRef/provider. The selected nonempty export key must resolve to one
public local provider FieldDef with a public owner, and the expected owner
must bind to that exact provider owner. Compare the complete field type,
static/mutable role and access, applied owner substitutions, and every
retained complete Layout/TargetProfile membership and ordered
LayoutField correspondence. A FieldDef Import or FieldPath token grants no
field access, storage, initialization, address formation or marshalling.
`NativeImport`/`NativeExport` remain their separate static native-binding
contracts; matching library symbols or names do not satisfy metadata imports.
The complete satisfaction and replacement obligations are defined in
[binding and replacement](representation/binding-and-replacement.md).

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
FieldDef, Layout, PropertyDef, UnionCaseDef and UnionCaseField inherit
ownership from their owner declaration; GenericParam, Constraint, Contract
and Body inherit it through their owner. TypeSpec, MethodSig and Param are
structural rows
without a module owner; their references must be valid in every declaration's
module context that uses them. An Import belongs to its expected local
definition's module; its ModuleRef must belong to that same module. An
Export belongs to its local definition's module. Module entry methods must
be owned by their Module row. A SourceMap row must associate a Body
instruction with a SourceFile owned by that same module.

Strings and blobs can be shared by equal bytes when the referring fields
give them the same interpretation. Structural TypeSpec and MethodSig/Param
rows may be shared only when their resolved nominal, formal
and imported references and passing modes remain identical in every using
module. Distinct module identities and nominal TypeDefs are not coalesced
merely because names or layouts match. A source compiler binds a
cross-module declaration through its own Import and expected contract.
A merger may resolve that edge against the producer's export,
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
declare one, but row order does not establish identity. A merger that emits
transformed data is responsible for preserving row references, import/export
relationships and optional reference-slotted sections. A successful binding
claim uses the complete published contract; a fixed in-artifact direct
binding does not require repeating the import-resolution proof on every load.
A compatible replacement under the same module identity preserves retained
contracts. After structural validation and the required binding check,
consumers need not repeat the import-resolution proof on every load. Trimming
cannot silently discard a published contract
while claiming full compatibility under the same identity.
The artifact still has one physical directory and no assembly manifest.
Producers may assign keys as they choose, but must preserve a published key
to offer a compatible replacement; an implementation name is not a binding
algorithm. The only assigned `runtimePolicy` value is `0` fail-fast for
unhandled failures; explicit unsafe source catches do not require a runtime
policy opt-in. `targetRequirement` values are
`0` portable semantic (no native assumption) and `1` Windows x64 native.
Other targets and nonzero module flags have no assigned `(1, 0)` meaning and
are invalid producer output. The entry method, if present, is a MethodDef row owned by
that module. Imports identify **one** selected module and key; their expected
local TypeDef, MethodDef or PropertyDef (including its referenced signature,
layout and contracts) is the complete consumer-side contract. The expected
definition has no body. Valid artifacts keep imports unique by
`(moduleRef, exportKey, expectedLocalDefinition)`; conflicting duplicate
bindings violate producer obligations. Every retained expectation uses the
[complete binding relation](representation/binding-and-replacement.md):
several expectations can select one provider, but all must be satisfied and
one expected declaration cannot resolve to conflicting identities.
Distinct keys may export the same public local definition without creating
new nominal identities; they cannot re-export an imported expectation.
MethodDef `bodyRow` is nonzero only for locally authored
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
| `0x05` constructed | `u32 genericTypeSpec` row, then one fixed `TypeSpecRef` or ConstantSource reference per formal | Ordered substitution for a nominal generic definition; type-formal arguments are complete TypeSpecRefs, and constant-formal arguments are ConstantSource rows, in formal order |
| `0x06` optional | `TypeSpecRef innerType` | Presence on complete qualified type |
| `0x07` ref / `0x08` ptr / `0x09` box / `0x0A` val / `0x0B` own | `TypeSpecRef innerType` | Indexed representation-family argument when the inline built-in form does not apply |
| `0x0C` indexed built-in | `u8 familyCode, TypeSpecRef argumentType` | Other one-argument built-in family over a complete type reference |
| `0x0D` native function pointer | `u8 callingConvention, u32 signatureContractMethodDef` | Native function-pointer contract using a bodyless MethodDef anchor; convention tags are NativeCallingConvention values `1..4` |
| `0x0E` initialization reference | `TypeSpecRef innerType` | Distinct `ref<init T>` type; the row's `write` qualifier may preserve outer write authority |
| `0x0F` existential | no payload | Existential generic argument/view identity; hidden type and witness evidence remain per value |

Qualifier bits have fixed per-node meanings:

| Bit | Meaning and validity |
|---:|---|
| `0x01` | `write` on this complete type node, where its immediate access contract permits it |
| `0x02` | `gc` backing on this node, only for admitted class/box/initialized-reference forms |
| `0x04` | `init` on a TypeSpec row used as the immediate argument of indexed Span family `0x8A`; never on the outer Span node or as a standalone complete type |
| `0xF8` | Reserved zero |

Form `0x0E` permits only the `write` bit on the reference node; `gc` is
invalid there because an uninitialized destination cannot itself be rooted,
and `init` is carried by the distinct form. Form `0x0F` and native function
pointer form `0x0D` require zero qualifiers.

Form `0x0D` has zero TypeSpec qualifier bits. Its convention byte is
nonzero and uses the registered closed NativeCallingConvention domain; its
typed MethodDef reference names a bodyless, non-exported contract anchor.
The anchor carries the complete MethodSig and applicable ordinary contracts,
but is not callable and cannot be used by `Call` or `NativeAddress`.
Function-pointer identity is the resolved complete signature, convention and
contracts, not the anchor row or name; equivalent anchors normalize to the
same type identity.

`TypeSpecRef` fields inside a TypeSpec row may use a valid row reference or
an eligible inline F2/F3 token. Row references, including `genericTypeSpec`,
must point to an earlier TypeSpec row where required by the form; inline
tokens create no row-graph edge. Nominal declarations may refer to each other
through their TypeSpec rows, subject to producer-side storage-cycle validity.
A TypeSpec reached
from several modules must retain the same resolved nominal identity in
each context; identical spelling alone cannot make unrelated types shared.
In a merged artifact, the in-artifact form may name another module's TypeDef
after a merger resolves that import. The imported form preserves
the consumer's selected expected TypeDef when the edge remains an Import.
The referenced generic definition supplies the formal count; there is no
separate argument-count field or `TypeArg` child table. Each entry follows the
formal order and is validated against that formal's domain, qualification and
constraints. Resolved identity follows the
[complete-type normalization rules](language/types-and-normalization.md),
not physical row equality. A compatible nested box does not encode another
handle slot; its requested outer access controls the normalized managed form.
The normalized type does not preserve an inner box's immediate write access
when the outer box requests readonly access, or drop qualifications inside
an unremoved payload. TypeSpec qualifier bits are `0x01=write`, `0x02=gc`
and `0x04=init`; bits `0xF8` are reserved zero. Each bit qualifies only the
type node carrying it, so nested rows distinguish outer and inner positions.
`write` must be admitted by that node's immediate access contract. `gc` is
valid only for class handles, boxes and initialized references; it is invalid
for inline values, pointers, owners and arbitrary types. The distinct
`0x0E` form encodes `ref<init T>`; its `write` qualifier preserves outer
write authority, while its `gc` and `init` qualifier bits are invalid. The
`init` qualifier bit is valid only on the immediate argument TypeSpec of
indexed built-in `Span` (`0x0C` with family code `0x8A`); it is not a
standalone complete type or declaration-storage fact. Thus `write ref<init T>`
preserves repeated whole-place writes after initialization, while
`ref<init write T>` preserves inner mutation after one-time initialization.
`gc ref<init T>`, `gc ptr<T>` and `ptr<init T>` are invalid.
`ref<init ptr<T>>` initializes pointer storage, not its pointee.

TypeSpec form `0x01` carries no qualifier for its atomic argument. Use an
indexed family TypeSpec when that argument itself has `write`, `gc` or `init`;
TypeSpecRef fields may use inline F2/F3 tokens for eligible complete types.
Constructed arguments use one TypeSpecRef or ConstantSource reference per
formal. Thus `Span<init T>` is represented by the indexed Span family over an
init-qualified TypeSpecRef; the qualifier is valid only in that immediate
argument position. Pinning remains an explicit backing dependency, not a
TypeSpec qualifier. All other reserved bits are invalid producer output.
The assigned atomic built-in code registry is:

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
additional TypeSpec lookup for its argument. An instruction's `t` operand
names the containing TypeSpec through the Body-local type map, not directly
by artifact row number. Assigned family codes are:

| Codes | Built-in family in increasing code order |
|---|---|
| `0x80..0x84` | `simd64<T>`, `simd128<T>`, `simd256<T>`, `simd512<T>`, scalable `simd<T>` |
| `0x8A` | `Span<T>` |
| `0x8B..0x8F` | `ref<T>`, `ptr<T>`, `box<T>`, `val<T>`, `own<T>` |

Codes `0x85..0x89` and `0x90..0xFE` are reserved for future built-in
families; `0xFF`
is reserved as a possible extended-code escape and is invalid producer
output in `(1, 0)`.
The inline `argumentCode` must be an assigned atomic code below `0x80`,
not another family code or `0xFF`; family-specific membership and legal
element types are producer-validity requirements. An indexed family argument uses
an earlier complete TypeSpec: forms `0x07..0x0B` for their corresponding
representations, or form `0x0C` with the assigned `familyCode` for SIMD and
`Span<T>`. These indexed forms are required when `T` is nominal,
imported, formal, qualified or itself constructed; valid producer output
does not use them for a plain unqualified atomic `T` for which the inline
form is canonical. The
indexed `familyCode` is the same `0x80..0xFE` registry value, not an
independent enum. `ref<init T>` is the distinct TypeSpec form `0x0E`, and
direct parameters use ordinary Param mode `00`; `init` is not a parameter
mode. `Span<init T>` uses the `init` qualifier on the argument TypeSpec of
indexed Span family form `0x0C`. Ordinary readonly and writable references
are descriptor-value parameters whose access is in TypeSpec, not separate
passing enums. A built-in family form
does not add a second `N` parameter: fixed SIMD width is in its family
code, while scalable `simd<T>` has its own code.

Assigning an atomic or family code identifies a type for structural reading;
it does not by itself admit an operation or establish compiler support.
Operation admission follows each assigned instruction's type matrix and
required capability/target prerequisites. The selected scalar arithmetic
matrix covers its five binary and three decimal floating identities; it does
not assign `float80`, optional floating values or arbitrary numeric
witnesses. A producer must not assign private meanings to reserved codes, and
a consumer declines unsupported required forms before affected use.

These codes are semantic type identities, not C ABI aliases or promises of
one machine-register representation. C `long`, `wchar_t` and `long double`
depend on the target ABI; an x87 extended `float80` or built-in complex
number would require separately specified value, storage, target-support
and call-classification rules before receiving a code. `Task<T>` is a
declared completion family with ownership and async contracts, not a scalar
or a fixed task-object layout implied by an inline type code. `mut` belongs
to a place or receiver, `write` is a type qualifier, and `init` is represented
by the `ref-init` TypeSpec form or the Span element qualifier; declaration
storage initialization remains a separate place fact. Pinning is an explicit
backing dependency established by `ref *root`, not a TypeSpec qualification.
Multi-argument callable signatures such as `func` and
`fnptr`, tuples, nominal types and arbitrary type arguments require their
own structural TypeSpec/contract references; a one-byte `T` cannot stand
in for them. SIMD comparisons use the same vector family and element type,
with all-zero/all-one lane bits; there is no separate mask TypeSpec family.

The low two bits (`flags & 0x03`) of every definition's `u32 flags` encode
visibility: `0x00` private, `0x01` internal, `0x02` public; `0x03` is invalid
producer output. This applies identically to TypeDef, FieldDef, MethodDef and
PropertyDef. Their remaining bits have row-specific assignments:

| Row | Assigned bits beyond visibility | Reserved bits in `(1, 0)` |
|---|---|---|
| TypeDef | Bits 2–3 (`(flags >> 2) & 0x03`): kind `1` struct, `2` class, `3` interface; zero is invalid producer output. | Bits 4–31 |
| FieldDef | Bit 2 (`0x04`): mutable field. Bit 3 (`0x08`): static field. | Bits 4–31 |
| MethodDef, PropertyDef | None. | Bits 2–31 |

Reserved bits are zero in valid producer output and do not change a
declaration's meaning. Layout kind moves from TypeDef flags into each
profile-tagged Layout row; TypeDef flags therefore retain only visibility
and nominal kind. `Layout.flags` assigns bit `0x01` to the Blittable
commitment; other bits are reserved. `Module.moduleFlags` and
`Constraint.flags` remain zero. Directory
section-policy flags, String encoding flags and Body place flags have
separate, explicitly defined domains. The PropertyDef getter MethodDef
belongs to the same declaring type, has the same result TypeSpec, and is
bound by its PropertyDef identity, never a `get_Name` spelling. Exported
TypeDefs, MethodDefs and PropertyDefs require public visibility; an exported
member also requires a public owner TypeDef. Every TypeDef has a default Layout row, including Auto-layout types. The
LayoutField list states each variant's complete ordered instance fields.
Physical offsets, aggregate size and final alignment are derived from the
declared fields and selected target ABI; they are not producer-published
FieldDef or Layout values. `Transparent` retains nominal identity while
using its one carrier's direct call classification. Equal layout is not
implicit conversion.

Public fixed-layout declarations require an explicit `Blittable`
commitment; it is not inferred from visibility, `Sequential`, or matching
layout values. Each Layout row carries its layout kind, optional declared
alignment, normalized packing and complete ordered field membership. The
exact TargetProfile row selects a complete alternative and replaces the
default layout/field payload; if no exact row exists, the default applies.
There are no profile sets, sparse merges or fallback chains. The producer is
responsible for source and contract truth. A consumer derives physical layout
using the selected target ABI, verifies the admitted layout and representation
facts before affected use, and rejects unsupported mappings; it does not
trust serialized offsets or sizes because none are present.

The [Windows x64 target rules](targets/windows-x64-data-layout.md) now specify
scalar representation, registered Layout/Blittable source schemas, member
packing, explicit nested alignment, object extent and access obligations.
The TypeDef/Layout/LayoutField rows carry the declared inputs; the target
profile derives offsets, size, final alignment and call classification.

Param mode `0x00` is ordinary passing, including complete `ref<T>`,
`write ref<T>`, `ref<init T>`, `write ref<init T>` and `Span<init T>` types.
It also includes `void`: a unit parameter has an ordinary ordinal/type and
contributes to logical signature identity despite zero payload data. Mode
`0x01` and codes
`0x02..0xFF` are reserved and invalid in producer output; historical
`out`/readonly-ref/writable-ref passing codes are not aliases.
`ref<init T>` is the distinct TypeSpec form `0x0E`, not mode `0x01` combined
with `ref<T>`.

Param flag `0x01` publishes `implicit` source-call adaptation. It is legal
only with mode `0x00` and an ordinary readonly outer `ref<T>`; it cannot
form or forward `gc ref<T>` or `ref<init T>`. Any existing backing/pin
dependency of a passed `ref<T>` remains part of its logical contract.
Complete inner qualification remains unchanged. Bits `0x02..0x80` are
reserved.
Local parameter-binding `mut` is not a signature flag and adds no caller
authority. Signature identity compares normalized complete types and mode,
not the adaptation flag or local descriptor rebinding permission. The
[coded instruction set](il-instruction-set.md) has local place
operations; executing initialization destinations or general loans still
requires their complete publication, dependency, exception and capability
carriers.
Unimplemented modes cannot be silently treated as by-value. `MethodSig` records
declaration order; argument labels and receiver access need registered
contract fields rather than name-based inference. GenericParam, Constraint
and constructed TypeSpec rows are reserved for a separately specified generic
feature; their row shapes alone do not make generic bodies executable or
exportable.
Parameter and generic-formal ordinals are zero-based and contiguous within
their owning MethodSig or declaration. Unused range fields are `(0, 0)`.

The source [write requirement](language/types-and-normalization.md#generic-write-requirements)
belongs to its type formal's published generic contract. It applies to the
resolved normalized argument's immediate access; it does not set
`TypeSpec.qualifiers`, reinterpret inner writable payloads, or derive
authority from private code. The producer publishes the requirement with the
other declared constraints and is responsible for its truth. Consumers
validate its assigned shape, reference namespace and support before relying
on that contract; they do not re-prove its semantic validity. Its
physical Constraint kind and generic feature admission remain unassigned; an
implementation that does not support required generic semantics cannot claim
support for that input.

For [ordinary output lifting](language-contracts.md#23-checking-and-consuming-the-contract),
the complete `ref<init T>` parameter type and its checked output/dependency
contract establish source eligibility without another attribute or
output-group flag. The Param uses ordinary mode `00`; its TypeSpec form is
assigned through the coordinated registry update. Producers must encode each required initialization relation, ownership and
dependency truthfully. Later-completion support requires its exact phase and capability
carriers rather than a separate initialization parameter mode.

The [source-call contract](language/calls-and-inference.md) logically records
the selected declaration, normalized closed type/constant arguments,
formal constraints, witness origins, parameter modes, chosen normal/expanded
form and admitted adaptations. Callable labels/defaults retain their source
view; pattern types do not choose a return/output channel.
An output observation must retain per-invocation initialization/presence,
status correlations, complete ownership and dependencies, including any
saved return obligations. Lowering reusable hidden locals must preserve
conditional vacancy preparation, argument/check order and scoped cleanup;
storage from a skipped prior call is not a new publication.
These are publication/verifier obligations, not new Param flags, Contract
kinds or generic feature IDs. A producer emits the required representation
before claiming independent semantic support.

### Semantic contracts and stable binding

Each Contract row is `u32 ownerRow, u16 clauseKind, u16 flags=0, data`.
The offset index gives its exact extent; each clause defines the length of
`data`. Valid artifacts use clauses defined by their selected schema; a
producer cannot reclassify an unknown required clause as debug data. An
implementation cannot claim support for semantics it does not implement;
verification rejects unknown or malformed required clauses before affected
use.
Contracts for a TypeDef or MethodDef occupy its contiguous `contractFirst/count`
range. The assigned clause kind fixes the owner table, and `ownerRow`
must name that owner without a DefId discriminator; valid producer output
does not orphan or share Contract rows between owners or duplicate singleton
clauses.
The [clause matrix](representation/contract-clauses.md) defines the closed
owner/subject, extent, occurrence-key, absence and cross-row rules. In
particular, Effects and NativeCallable are MethodDef-owned only,
OutputState is keyed by its initialization formal and exact published
predicate, Dependency is MethodDef-owned, and Cleanup is TypeDef-owned. The
current clause shapes are:

| Clause | `data` payload | Meaning |
|---:|---|---|
| `0x0001` Effects | `u32 mask` | MethodDef-only singleton. Bit `0x02` promises `DoesNotReturn` for any declared result type, bit `0x04` requires an unsafe caller; the retired `0x01` NoThrow bit and all other bits are invalid. Absence and one explicit zero mask have the same assigned meaning; canonical writers omit zero. |
| `0x0002` OutputState | `u32 parameterOrdinal, u32 predicateRef, u8 state, u8 reserved[3]=0` | A MethodDef-owned normal-outcome relation for a `ref<init T>` destination or `Span<init T>` range formal using Param mode `00`; the exact published predicate selects the outcome and state is `0` vacant or `1` complete. |
| `0x0003` Transfer | `u32 parameterOrdinal, u8 transferMode, u8 cleanupMode, u16 reserved=0` | Reserved and semantically inactive; owner handoff is derived from complete types, explicit Body/call transfer, responsible holder and Cleanup identity. |
| `0x0004` Dependency | `u8 destinationKind, u8 sourceKind, u8 relationship, u8 phase, u32 destinationOrdinal, u32 sourceOrdinal` | A shared MethodDef-owned relation for borrowing source storage, forwarding existing backing or their declared union across publication, retention and deferred-completion phases. Destinations are result `1`, checked init-output `2`, retained formal holder `3` and retained receiver `4`. |
| `0x0005` Cleanup | `u32 cleanupMethodDef` | TypeDef-only singleton on an admitted unique type. Names its selected parameterless readonly instance void, synchronous/nonsuspending cleanup operation; compiler-invoked Cleanup runs on normal retirement only. Complete uniqueness/role/ownership records remain required. |
| `0x000C` NativeCallable | `u8 callingConvention, u8 flags=0, u16 reserved=0` | MethodDef-only singleton. The unique clause selects the native-callable role and its effective NativeCallingConvention. Method-form NativeExport references this exact clause. |

OutputState and Dependency preserve their assigned semantic relations, but
row presence does not supply the published-predicate codec, complete place/
publication obligations, runtime obligations or
capability closure. An implementation may claim support only when it
implements every required layer. Transfer remains inactive and does not
encode owner handoff. This schema does not
encode pre/postcondition expression trees, arbitrary witnesses or a public
managed-handle ABI. Effects do not follow from an opcode name, a method name,
or an implementation body. A source-selected declaration and closed witness
bindings remain the call target; linking validates that binding rather than
rerunning overload resolution.
In particular, DoesNotReturn does not stand in for the
[selected-form purity records](representation/logical-records.md#published-predicates-and-evaluation-forms)
or authorize removal of ordinary-form effects.

### Identity and contract invariants

Local indexes are compact references within one artifact, not stable identities
across rebuilds. Internal and external imports resolve through a selected producer
identity and exported declaration key with the equality rules above. A
digest is not automatically a nominal identity. Stable-key generation and
producer authentication remain separate, open contracts.
[Binding compatibility](representation/binding-and-replacement.md) defines
the retained expected/provider comparison, not a physical row-identity rule.

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

The logical relation preserves exact complete signatures, state, ownership,
phase and representation obligations while allowing established caller-domain
widening and promise strengthening. Effects masks are not numerically ordered,
and extra initialization cannot replace required vacancy.
Generic-template/witness exportability and missing physical representation
commitments remain gated; approving the relation does not encode them.

### Row-assignment example

For a zero-argument `Answer` method returning `int32` value 42, these are
the relevant row references; row indexes are artifact-wide:

| Table row | Payload values in column order |
|---|---|
| String 1 | `flags=0, unitCount=6, ASCII content=Answer, terminator=00, padding=00` (method name and export key) |
| String 2 | `flags=0, unitCount=7, ASCII content=Example, terminator=00` (module identity) |
| Module 1 | `identity=2, governingRuleEdition=1, targetRequirement=1, runtimePolicy=0, entryMethod=1, moduleFlags=0, featureFirst=0, featureCount=0, moduleRefStart=1, typeDefStart=1, methodDefStart=1, sourceFileStart=1` |
| TypeSpec 1 | `form=0x01, qualifiers=0, primitiveCode=0x08` (`int32`) |
| MethodSig 1 | `resultTypeSpec=1, receiverTypeSpec=0, paramFirst=0, paramCount=0, genericFirst=0, genericCount=0` |
| MethodDef 1 | `ownerTypeDef=0, name=1, signature=1, flags=2` (public), `contractFirst=0, contractCount=0, bodyRow=1` |
| Export 1 | `localDefId=0x1600_0001, exportKey=1` |
| Body 1 | Instruction-stream kind `01`, codec `01`, `mapMask=0x12`; present counts `typeMapCount=1`, `constantMapCount=1`, `blockCount=1`, `recordCount=1`, `codeByteLength=2` |

For example, the TypeSpec table with its single `int32` row is exactly
`00 00 00 00 03 00 00 00 01 00 08`: two four-byte offsets followed by the
three-byte row. Module and Export identity do not change the schema-owned
meaning of references or the producer's obligation to publish compatible
contracts. The producer remains responsible for body-level correctness;
consumers safely decode the Body and may rely on its published semantics
without re-proof. A deeper semantic audit remains optional.

The Body row is 67 bytes: its 25-byte prefix is followed by TypeMap token
`0x11000001` (TypeSpec row 1), ConstantMap entry
`(TypeSpecRef(1), ConstantSource(1))`,
one 12-byte Block entry `(codeOffset=0, codeLength=2,
entryStateRecord=0, flags=0)`, record offsets `[0, 8]`, an empty
`BlockEntryState` record (kind `01`, registerCount `0`), and code bytes
`09 00` (`R0 = load.constant 0`; return `R0`). The Body-local map, Block and
record indexes are zero-based; Body and referenced metadata rows remain
one-based. The referenced ConstantSource/FrozenValueNode rows are omitted
from this row-assignment excerpt; they resolve to the exact `int32` value 42.

The two String rows have offsets `00 00 00 00 0C 00 00 00 18 00 00 00`.
String 1 is the 12-byte row `00 06 00 00 41 6E 73 77 65 72 00 00`:
the first `00` selects ASCII with no other flags, `06 00 00` is the
three-byte code-unit count, and the last `00` is alignment padding after
the one-byte terminator. String 2 is
`00 07 00 00 45 78 61 6D 70 6C 65 00`, also 12 bytes.

A merger could add String 3 (`Consumer`), Module 2
(`identity=3, governingRuleEdition=1, targetRequirement=1, runtimePolicy=0, entryMethod=0,
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
caller subsequently added to Module 2 could use Body-local MethodMap index
`0` to select its retained Import 1, whose expected declaration is MethodDef 2
(`DefId=0x1600_0002`), then resolve Module 1's exported MethodDef 1. A merger can instead resolve that call
directly to MethodDef 1 and omit the unused expected row and Import.

## Body table and coded-instruction boundary

Each Body row is the variable table's offset-indexed extent. Its one-based
row is referenced by the `MethodDef.bodyRow` that owns it; every Body row is
owned by exactly one local MethodDef, and an imported or bodyless declaration
has no Body. The `implementationKind:u8` tag selects one of the five assigned
forms in [Body row layout and compact implementations](il-instruction-set.md#body-row-layout-and-compact-implementations).
Kinds `02..05` carry their exact field or method token descriptors; kind `01`
contains the register instruction stream and its explicit
`instructionEncoding:u8`. No decoder infers a form from row length or
payload bytes.

For kind `01`, byte 2 is a `mapMask:u8`: bits 0..4 indicate HomeMap, TypeMap,
MethodMap, FieldMap and ConstantMap, respectively; bits 5..7 are zero.
The initial bytes are `u8 implementationKind=01, u8 instructionEncoding=01,
u8 mapMask, u16 reserved=0`.
Serialize the `u32` counts only for maps whose bits are set, in that order,
between the initial five bytes and the three mandatory `u32` counts
`blockCount`, `recordCount` and `codeByteLength`. With
`K=popcount(mapMask)`, the prefix is exactly `17 + 4*K` bytes, 17..37.
An absent map has no count or payload. A set bit with a zero count is valid
noncanonical input; canonical writers clear bits for empty maps.

The row then contains the present maps in Home/Type/Method/Field/Constant
order, omitting absent maps: eight-byte HomeMap entries, four-byte TypeMap
tokens, four-byte MethodMap tokens, four-byte FieldMap tokens and eight-byte
ConstantMap references. Next are `blockCount` fixed 12-byte Block entries,
the record-offset index and typed-record data, and exactly
`codeByteLength` instruction bytes. There is no padding or unclaimed trailing
data. For effective map counts `H,T,M,F,C`, mandatory counts `B,R`,
record-data extent `D` and code extent `L`, the exact Body extent is
`17 + 4*K + 8*H + 4*T + 4*M + 4*F + 8*C + 12*B + 4*(R+1) + D + L`,
using checked arithmetic.

Home, TypeMap, MethodMap, FieldMap, ConstantMap, Block and typed-record indexes are
zero-based `u16` values; `0xFFFF` is valid. Their `u32` counts are at most
65,536, with at least one block and one record. TypeMap entries use the
Body-specific type-token grammar: TypeSpec row kind `11`, TypeDef row kind
`13`, Import kind `1C`, assigned pseudo/well-known/inline-family kinds, and
no general TypeSpecRef row-form alias. ConstantMap type fields use the
separate complete `TypeSpecRef` grammar. MethodMap entries use exact
MethodDef or Import tokens. The maps are independent namespaces. MethodMap lookup does
not use a zero/direct-row escape or derive a MethodDef row from Module ranges.
Cross-module bindings retain their Import identity and expected declaration.
Global table tokens keep their registered four-byte form and are not
truncated to Body-local widths.

Each Block entry stores `u32 codeOffset, u32 codeLength,
u16 entryStateRecord, u16 flags=0`. Its code range is nonempty, block ranges
are disjoint and ordered, and together they cover the complete code byte
array. Every block ends with exactly one terminal instruction; adjacency
does not imply fallthrough. `entryStateRecord` selects a typed
`BlockEntryState`, which publishes the available general-register inputs.
Each row distinguishes an ordinary value, an internal place handle or an
internal TypeIdentity. A TypeIdentity row uses `carrierKind=2`,
`typeMapIndex=0` and `accessKind=0`; the type field is unused and does not
refer to TypeMap[0]. Place handles retain their access class and referent
type. Dynamic backing, loan, identity evidence and provenance facts remain
mandatory verifier state across incoming edges. Register, home, block and
record identities remain Body-local, not metadata `DefId`s.

`recordOffsets` is `u32[recordCount+1]`, relative to record data. It starts
at zero, strictly increases, and ends at the exact record-data extent. Each
record has a four-byte kind/flags/reserved header and the exact typed payload
assigned by the instruction chapter. `codeByteLength` and instruction
ordinals are separate `u32` domains; they are not Block or record indexes.
Each instruction begins in exactly one of the tiny (`00..0F`), short
(`10..BF`) or long (`C0..FF`) byte partitions selected by
`instructionEncoding=01`; instructions have lengths one, two or four bytes
and no width prefix.

Consumers validate the Body framing, indexes, instruction boundaries,
record kinds and operation/control combinations before affected use, and
admit required semantic support. Assigned encodings do not establish
compiler/runtime support or waive declared capability prerequisites.
This Body layout assigns no ExceptionRegion directory or handler/filter/
finally rows; instruction/record shapes that need that carrier reject
affected use until its schema is assigned.
Producer-published semantic facts remain producer obligations; external
provider behavior remains a trust contract. Out-of-bounds framing, unknown
required values and unsupported required forms reject before affected use.

## Limits, counts, ranges, and accounting

The [resource-accounting chapter](representation/resource-accounting.md)
owns artifact/object/request scopes, checked preflight, retained input and
constructed payload/state, deterministic structural versus semantic work,
depth, recipes and exhaustion. Nested access does not reset a root budget.
Trusted selective reading still need not scan unrelated payloads.

The `u32` total length and 24-bit row/unit counts are representational bounds,
not a portable capacity guarantee or decoded-memory bound. Implementations
declare their supported envelopes and recipes. Portable minimum supported
envelopes will permit declared higher limits; numerical minima are deferred
until calibrated, not filled with normative placeholders.

Exceeding an envelope explicitly declines the requested operation or claim;
it does not make otherwise valid bytes malformed or excuse skipped checks.
Host cancellation, I/O failure and unavailable physical memory are distinct
from deterministic resource exhaustion. Source proof/ConstEval budgets remain
separate.

## Producer validity, support and references

The sections and registries define valid encodings and their meanings.
Producers emit assigned forms, valid references, complete declared
requirements and truthful semantic facts. Consumers validate the structure,
bounds, references and support needed to safely consume selected input before
binding, code generation or execution. They reject malformed, unknown,
unsupported or incompatible required input, but are not required to re-prove
the semantic truth of structurally consumable producer facts. Check
organization, diagnostic wording and internal order are implementation
choices.

A consumer's support claim is separate from artifact validity. An
implementation may claim support only for schemas and required semantics it
actually implements. Directory policy defines the meaning of optional
nonsemantic sections in a valid artifact; the producer cannot use an optional
policy to hide required semantics. A rewriter that emits a transformed
artifact is responsible for preserving meaning and publishing correct
references, indexes, requirements and contracts.

The exact schema and its table-implied reference namespaces define valid
meaning; row numbers, names or coincident bytes do not change a reference's
target. The consumer-verification contract requires rejecting an out-of-range
reference, unknown required form, invalid reserved value or incompatible
binding before affected use. Ordinary acceptance and optional canonicality
audits remain separate claims under
[encoding canonicality](representation/encoding-canonicality.md).

## Example: predicate-correlated output contract

Suppose MethodDef row 1 has a fourth parameter (ordinal 3) whose complete
type is `ref<init T>` and whose Param mode is ordinary `00`. If the method
promises that the destination is complete when the canonical true predicate
holds and vacant when the canonical false predicate holds, it has two
OutputState rows:

```text
ownerRow=1, clauseKind=0x0002, flags=0,
parameterOrdinal=3, predicateRef=<canonical true predicate>, state=1,
reserved=0

ownerRow=1, clauseKind=0x0002, flags=0,
parameterOrdinal=3, predicateRef=<canonical false predicate>, state=0,
reserved=0
```

Each row has a 12-byte payload and a 20-byte total extent including the
Contract header. `predicateRef` resolves through the shared published-
predicate codec; its physical encoding is coordinated with that codec. The row
describes a declared normal-outcome relation, not an exceptional state or a
per-call success flag. A consumer does not read the output to discover its
state. For a particular call, correlate the selected predicate with its
evaluated value and exact destination generation using the callable contract
and decoded Body call/value/place references; do not persist an opaque success
flag. The producer is responsible for semantic correctness; consumers
validate the selected records, references and support needed for safe use,
then rely on represented contract and Body facts without semantic re-proof.
Executable support also depends on the complete
Body place, state, publication and capability obligations; the clause alone
does not make a body executable.

## Unspecified schema details

The approved current design defines relationships and validation principles, but not the
following byte-level contracts:

| Surface | Not yet specified |
|---|---|
| Encoding and revisions | Released schema assignments, additional capability identities/definitions and explicit compatibility mappings remain unassigned. The format pair/exact draft agreement and physical Module governing-edition/Feature codec are selected; no additional schema selector or manifest is implied. |
| Allocation | The selected registry's current opcode allocations and family capacity are assigned. Extended operation/terminator envelopes, new feature IDs, wider constants and additional Body forms remain unassigned. |
| Declarations | Encoding the accepted explicit `Blittable` commitment, resource references, generic/witness exportability, further type/field/layout flags, richer qualifier/contract clauses and their validation. Module identity and export-key equality are specified; assigning a stable key to a source declaration is a writer policy. |
| Constant generics | Exact nominal/carrier values, derived Transparent eligibility, formal/default relationships, validity/value-codec obligations and compatibility. Logical rules do not activate an existing scalar/Attr encoding. |
| Static storage | Physical schemas/Body encoding for the approved `StaticField`, `EagerGroup`, `EagerInitializer`, `LazyUnit`, `StaticAccess`, `InitializationRole` and `InitializationDemand` families, plus `StaticPlace`; preserve ordered initialization, constructor policy, sharing/phase authority, failure/recovery and cleanup/unload. No current `(1, 0)` row or flag supplies this encoding. |
| Patterns and handlers | Runtime captured-type evidence/constraints/code availability, existential witness retention and qualified reference matching; the approved lexical exception-region and ordered-filter semantics still need physical directory/handler rows, exception-object transport and ParentException storage. |
| Executable bodies | Beyond the [assigned decode shapes](il-instruction-set.md#active-mixed-instruction-encoding): output places, physical exceptional-handler/cleanup carriers, loans, roots, witness bindings and their precise verifier rules. |
| Limits | [Resource accounting](representation/resource-accounting.md) assigns scopes, capacity/work/state recipes and explicit exhaustion; portable numerical minima remain unassigned. External resource/expanded codec recipes need separate adoption. Canonical widths/maps and ordinary noncanonical admission follow [encoding canonicality](representation/encoding-canonicality.md). |
| Unknown data | Kind-specific extension schemas and new reserved-value meanings; generic skip/remap/discard policies and the exact Feature row codec are already assigned. |
