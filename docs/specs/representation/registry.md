# TRRM allocation and shape registry

## 1. Authority and schema identity

The [machine-readable registry](trrm-1.registry.json) is normative for numeric
allocations, namespace roles, operand sequences, widths, enum domains and
recorded admission constraints. The [metadata chapter](../il-metadata.md)
owns framing, row semantics and binding; the [instruction chapter](../il-instruction-set.md)
owns Body and operation semantics. Their displayed tables are projections of
this registry, not independent allocation authorities. A disagreement is a
specification defect, not permission to choose a meaning.
The [clause chapter](contract-clauses.md) owns assigned clause applicability,
occurrence keys, absence and complete cross-row obligations.
The [verification chapter](reader-trust.md) owns producer truth obligations,
mandatory structural safe-consumption checks, optional semantic audits and
schema-owned reference interpretation.
The [canonicality chapter](encoding-canonicality.md) owns minimal writer
widths/maps, equivalent ordinary noncanonical admission and explicit audits.
The [resource-accounting chapter](resource-accounting.md) owns scoped capacity,
deterministic structural/audit recipes and explicit exhaustion; numerical
portable minima remain unassigned.
The [format-version chapter](format-versioning.md) owns stable released
schema assignments and exact out-of-band draft agreement.
The [semantic-manifest chapter](semantic-manifests.md) owns Module editions,
exact coherent capability requirements, complete closure and physical ranges.

`trrm/1-draft-layout-queries-1` identifies this registry edition for the development
format pair `(1, 0)`. It is not an on-wire Feature ID or profile selector.
Independently exchanged drafts require exact agreement on this registry,
its semantic catalog **and** governing chapters. Matching `(1, 0)`, an edition
label or the registry's digest alone does not prove agreement on all semantics.
Draft/preview schemas can change while retaining `(1, 0)` without a
compatibility obligation. Once a schema is released, its format pair has one
stable meaning; this registry assigns no released schema or new header field.

There is one catalog. The earlier generic and floating draft catalogs are
not alternate reader modes or aliases in this schema. A valid artifact uses
the selected catalog's assignments; operand types, plausible instruction
lengths, implementation enums or chronology do not select a retired catalog.
An explicitly selected old development schema may be inspected by a separate
tool; those bytes are not current-schema input.

## 2. Instruction allocation

`instructionEncoding=01` selects the current mixed register codec. The first
byte alone fixes instruction length; no prefix, recursive width selector or
block-wide mode exists:

| First byte | Length | Form |
|---|---:|---|
| `00..0F` | 1 byte | Sixteen assigned tiny tuples |
| `10..BF` | 2 bytes | Assigned short operations and restricted forms |
| `C0..FF` | 4 bytes | Assigned long families |

The [instruction chapter](../il-instruction-set.md#active-mixed-instruction-encoding)
lists exact tiny tuples, short operands, long payloads, operation semantics
and type constraints. The machine-readable registry repeats every primary,
family selector, record kind and control domain. The assigned numeric ranges
are:

| Range | Assignment |
|---|---|
| `00..0F` | Tiny tuples T0..T15 |
| `10..1A`, `20..2B`, `30..36`, `40..41`, `50..52`, `60..67` | Short register, arithmetic, logic, comparison, conversion and place operations |
| `70..7F` | Field extraction ordinals 0..15 |
| `80..83`, `90..92`, `A0..AF`, `B0..B5`, `B7..B8` | Call windows/records, predicate branches, control records, failfast, unreachable and Home return |
| `B6` | Recognized RegionExit shape; no usable `exitKind` or region index domain |
| `C0..CC` | Scalar/bit/comparison/selection/ternary/recipe families |
| `D0..DD` | Conversion, constants, Home and typed place/store/reference operations |
| `DE` | TypeOf/TypeIdentity and SizeOf/AlignOf/OffsetOf query family |
| `E0..E8`, `EB` | Calls, branches, switch, requirement, throw and Home return |
| `EA` | Recognized RegionExit shape; no usable `exitKind` or region index domain |

Every other first-byte or family-control value is invalid. The `B6` and `EA`
shapes reject affected use until their region carrier is assigned. Reserved capacity
does not create an escape, extension, operation, profile or private
interpretation. Source-selected nominal operators remain calls under their
declared bindings; an operation name does not authorize compiler
recognition.

The `B6`/`EA` RegionExit and `Throw` active-handler record shapes do not
complete their required ExceptionRegion, handler/filter/finally or runtime
event carriers. No `RegionExit.exitKind` value or Body region inventory is
assigned; those records reject affected use until the dependent schema is
selected. See the [conformance gaps](../../implementation/conformance-gaps.md#cg-003).

## 3. Machine-readable shape conventions

Tiny instructions are one byte. A short instruction is its primary byte
followed by one operand byte. Forms listing `x`/`y` split it into low/high
nibbles; `90`, `92`, `B0`, `B2..B8` use the full byte where specified. The
predicate nibble in `A0..AF` and extraction ordinal in `70..7F` are carried
by the primary byte. A long instruction is
`primary:u8, control:u8, payload:u16`; the payload is little-endian. The
register-family payload nibbles are least-significant first:
result0/result1/source0/source1.

The stream codec selector is a separate `u8`, fixed to `01` for this
encoding. Its `mapMask`-selected prefix is `17 + 4*popcount(mapMask)` bytes
(17..37), with optional Home/Type/Method/Field/Constant map counts in bit
order and mandatory Block/record/code counts. FieldMap has four-byte
FieldDef/Import/FieldPath tokens; global FieldPath rows are in section
`0x004B`, and offset-observation field ranges use `0x004A`. The five-byte
BlockEntryState row adds carrier 2=TypeIdentity with unused `typeMapIndex=0`
and `accessKind=0`. Exact framing and index rules are specified in
[IL metadata](../il-metadata.md#body-table-and-coded-instruction-boundary)
and the machine registry. Body-local map, Home, Block and record indexes are
zero-based `u16`; all 65,536 values are available and corresponding counts
are `u32`. Global table tokens and code byte offsets/lengths/instruction
ordinals retain their own widths and namespaces.

Records begin with `u8 kind, u8 flags=0, u16 reserved=0`; the kind fixes the
payload schema. Variable record lists carry their assigned `u32` counts.
Register roles, selector domains, exact record payloads, reserved values and
typed operation constraints are normative in the two owning chapters and
machine-readable assignments. No definition/value/region index or width
class from the retired SSA codec carries forward; current Body references use
the distinct register, Home, Block and record namespaces.

## 4. Admission status and namespace boundaries

An assigned operation has the semantics specified by the language and
instruction chapters, subject to its complete type, call, region, effect,
runtime and capability prerequisites. Assignment is not an implementation
support claim. Unlisted or reserved values have no assigned meaning and do
not default to a known form. A support claim requires each required
capability's complete transitive prerequisite closure.
The full accepted TRRM 1.0 requirements define GA conformance; implementations
may advertise partial capabilities but cannot claim full GA conformance while
omitting a required contract.

Metadata assignment is structural recognition, not a claim of executable
support for every type or declaration. In particular:

- The 26 atomic identities and currently assigned TypeSpec/pseudo-family
  codes do not admit every family membership, representation, constant codec
  or operation. SIMD comparisons return the same vector type with
  all-zero/all-one lane bits; there is no separate mask type family.
- A complete `TypeSpecRef` remains four bytes and may be a TypeSpec row, a
  well-known `0xF2` token, or an inline built-in-family `0xF3` token. `0xF3`
  reuses TypeSpec form `0x01` family/atomic-argument and qualifier meanings;
  `0xF0`/`0xF1` remain Body-local pseudo-types because their base is a Body
  TypeMap index. These assignments do not admit a type family or operation
  outside its own semantic contract. `0xF4..0xFE` remain unassigned and
  `0xFF` remains reserved.
- Module's required `governingRuleEdition` is a full-width semantic identity,
  not a row reference. Edition `1` defines the accepted TRRM 1.0 semantic
  baseline, not a reduced scalar execution profile. Feature has an exact
  two-word manifest codec. Feature IDs 9-12 are assigned at revision 1, but a
  number does not complete its registration or prerequisite closure;
  incomplete required capabilities cannot be admitted or silently omitted.
  Feature 13 `LayoutQueries` is registered at revision 1 for retained `DE`
  queries, TypeIdentity flow/comparison, FieldMap/FieldPath, FieldDef
  Import/Export binding and target-layout field-path dependencies. Common
  map-mask framing alone does not require Feature 13.
- Effects has assigned MethodDef applicability, and OutputState/Dependency
  have approved shared row contracts in the
  [clause chapter](contract-clauses.md). OutputState's predicate target still
  requires its published-predicate carrier; Dependency uses only the assigned
  shared destinations and has no native-output destination. Transfer remains
  semantically inactive.
- Numeric gaps below section kind `8000` are not extensions. Unknown kinds
  `8000..FFFF` may be skipped only under the registered nonsemantic policy;
  they cannot supply required semantics or change core rows.
- A section kind is `u16`; a definition-reference kind is `u8`. A future
  referenceable table above `00FF` needs an explicit mapping/indirection
  decision, not truncation into an existing kind.

No reserved type escape, definition kind, header bit, extra Body
implementation kind, wider constant payload, static identity or semantic
optional section becomes usable by appearing in a proposal.

Status constrains producer conformance and what implementation support may be
claimed. Consumers must validate the selected artifact's framing, references,
requirements and structural safe-consumption properties before binding, code
generation or execution; producer indexes and manifests are not substitutes
for those checks. Producers remain responsible for semantic body facts, which
consumers may rely on without re-proof. The verifier's organization and
diagnostic wording are implementation choices. Optional audits remain
separately scoped claims.

## 5. Re-encoding and conformance

Canonical writers use the assigned tiny, short or long form; there are no
variable instruction-width classes. TypeMap, MethodMap, FieldMap and
ConstantMap entries follow deterministic first logical use, reuse identical
entries and omit unused entries. FieldPath rows are structurally interned
without conflating distinct Import expectations. The map mask omits the count
and payload of each empty map. Typed records follow first logical use and omit unused
records. HomeMap entries remain distinct storage identities. Ordinary readers
accept structurally valid reordered or duplicate TypeMap/MethodMap/FieldMap/
ConstantMap/typed-record entries where every reference preserves its meaning;
HomeMap ordering is not canonicalized, and audits do not claim a unique
HomeMap order. Canonicality is an explicitly scoped audit.

Re-encoding reconstructs typed instructions, records, maps and CFG, then
rebuilds code offsets, record offsets, references and any affected SourceMap
slots/step roles. It never searches for matching opcode octets in literals,
counts, enum values, indexes or offsets. A changed instruction length or
block order requires corresponding Body index and source-map remapping.

[Required cases and complete artifact vectors](../conformance/il-registry.md)
cover the tiny/short/long partitions, each assigned selector, typed records,
Body indexes, SourceMap step roles, type/result restrictions and rewrites.
They identify the current schema explicitly and distinguish independent
structural/finite assessments from arbitrary flow, producer truth, native
behavior, performance and unassigned feature conformance. The compiler has
not been migrated by changing the specification.
