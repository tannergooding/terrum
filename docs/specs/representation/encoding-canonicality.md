# Encoding canonicality

## 1. Ordinary reading, writing and auditing

Ordinary readers accept structurally valid, semantically equivalent
noncanonical encodings. In particular, a used operand class or Body-local
field class may use a wider assigned width, and a Body type map may have a
different consistently referenced order or distinct unused entries.
These differences do not change the selected types, values, operations,
effects or declaration identities.

Writers still emit the canonical widths and type-map order specified below.
An explicit canonicality audit rejects departures within its stated scope.
Ordinary acceptance is not a claim that the writer conformed, that the bytes
are canonical, or that the whole artifact was examined. The
[reader-trust contract](reader-trust.md) still permits selective access
without a preliminary instruction-stream or global type-map scan.

Other assigned writer requirements remain owned by their forms, including
[omission of zero-mask Effects](contract-clauses.md#3-effects).
This chapter does not select one String encoding, require global
deduplication or define a unique canonical artifact byte image.

Noncanonical is not another name for malformed or unsupported. Reserved
bits, unassigned codes, invalid references, duplicate map entries, missing
required types, contradictory indexes, invalid declared-type values and
unsupported affected semantics retain their existing rejection rules.
Ordinary readers reject invalidity exposed by their accesses. No alternative
encoding relaxes binding, effects, ownership, unsafe authorization or the
selected execution profile.

## 2. Instruction widths

For each instruction independently, a writer chooses the smallest assigned
width that represents every actually encoded operand in that class:

| Class | Canonical width |
|---|---|
| Body-local references | Smallest of 1, 2 or 4 bytes holding all unsigned local IDs in that instruction |
| Counts | Smallest of 1, 2 or 4 bytes holding all encoded argument, case or exit-list counts |
| Unsigned data | Smallest of 1, 2, 4 or 8 bytes holding every encoded bit pattern |
| Signed data | Smallest of 1, 2, 4 or 8 bytes holding every encoded mathematical value in two's complement |

The common envelope is required when all class codes are zero. Otherwise
the writer uses `F0 layout opcode`, with each class's canonical code. A wider
assigned code for a **used** class is noncanonical but ordinarily accepted.
Decoding zero-extends unsigned data and sign-extends signed data before
checking the selected operation's declared-type rules.

An unused class must still have code zero. A list count is an encoded count
even when its value is zero; its class is used. An empty Switch case list
does not encode any case data and therefore does not use the data class.
Reserved layout bits, reference/count code 3 and `F0` followed by layout
zero remain invalid encodings, not permissive width variants.

Fixed metadata words, enums and other fixed-width fields never participate
in these classes. A packed Call's local `m=0` uses a reference operand, but
its following typed MethodDef row remains four bytes; row 256 or 65,536
does not widen that zero local operand. The same distinction applies to
FieldDef rows and Body type-map words.

For example, these operation fragments both define int32 `r1` as 42 when
local `t1` selects int32:

```text
02 01 01 2A
F0 10 02 01 01 2A 00
```

The first is canonical. The second uses two-byte signed data and is accepted
by an ordinary reader but rejected by a canonicality audit. Neither changes
the result's int32 representation.

## 3. Body-local widths and representation choice

Canonical Body-local width is the smallest assigned width that represents
the selected form's complete local inventory. Include its encoded counts,
IDs, list counts, ordinals and structural fields, together with the
signature/operation-derived value inventory that a compact-linear Body must
represent. Derivation cannot evade a local-ID limit merely because that
form omits the corresponding stored count or descriptor.

General-form TypeSpec references, definition offsets and block/terminator
offset arrays remain fixed `u32`; their numeric magnitude does not widen
the general form's local field class. Compact-indexed offsets do belong to
its local class, including the final block-data length. Widening that class
widens all its local fields, not just its offsets.

For compact-indexed width selection, recompute block-header/list lengths and
relative offsets at each candidate local width. Choose the smallest candidate
whose re-encoded local fields and offsets fit. The larger headers/lists of a
wider representation do not justify that width when a narrower re-encoding
fits. Instruction bytes are held fixed for this Body-local comparison;
their class minima are checked independently by the full canonicality audit.

An all-ones definition offset marking a block parameter is a sentinel, not
a numeric offset whose bit pattern forces the widest layout. A writer
regenerates the sentinel at the selected form's offset width. Real offsets
must remain in their block and cannot be mistaken for that marker.

An ordinary reader accepts an assigned wider Body-local width while
retaining the form's exact field sequence, presence rules, bounds and
signature-derived inventory checks. A too-narrow layout, reserved bits,
unassigned form/width code or truncated field remains invalid.

Canonicality does not choose a universal representation for a logical Body.
A valid general form is still accepted for a compact-eligible Body;
compact-linear and compact-indexed retain their own eligibility rules.
A writer's widths are minimal **within its chosen form**. No preference
among those forms, new boundary-only encoding or offset-width class is
introduced.

## 4. Type maps

Every map entry remains a distinct nonzero typed TypeSpec reference valid
in the Body's selected module context. Local type IDs select that map,
not artifact rows. Required signature, descriptor and operation types
must be present and agree exactly under the existing formation rules.

Writers order map entries by first logical use across place descriptors in
place-ID order, then value descriptors in value-ID order, matching the general
form's inventory sequence. Explicit type operands in instruction streams
follow in block/instruction order. An omitted-storage form
uses its reconstructed logical descriptors for this ordering, not the
physical order in which its bytes happen to be decoded. Any signature-only
types that the form requires follow those descriptors, with parameter types
in declaration order and then the result type, before stream-only type uses.
Writers omit entries not needed by these logical uses.

Ordinary readers accept a different order only with all descriptors,
explicit type operands and other local type selections consistently mapped.
They also accept distinct, otherwise valid unused entries. This is not
permission to omit required types, duplicate entries, change a complete
nominal identity, ignore an accessed invalid TypeSpec or hide undeclared
required semantics. Complete declared retained requirements remain governed
by [admission](requirements-and-admission.md).

Map-order and unused-entry canonicality needs the relevant complete logical
use inventory. A trusted selected lookup need not construct it. A full
canonicality audit does; a partial audit reports its narrower scope instead
of certifying the whole map.

## 5. Constant values and exact bits

Width canonicality is separate from whether the selected type admits the
decoded value:

| Operation/type | Required interpretation |
|---|---|
| Integer `ConstBits` | Unsigned encoded pattern must fit the declared integer's bit width; then interpret that pattern with its declared signedness |
| `ConstSigned` | Sign-extended mathematical value must fit the selected signed or unsigned destination's value domain |
| Boolean `ConstBits` | Only decoded patterns 0 and 1 |
| char16 `ConstBits` | Any code unit in `0..65535`, including surrogates; not a Unicode-scalar conversion |
| void `ConstBits` | Only decoded zero, defining the singleton unit value |
| binary32/binary64 `ConstBits` | Exact declared-width IEEE representation; nonzero bits beyond that width reject |

Thus int32 `ConstBits` with pattern `FFFFFFFF` may use eight data bytes with
four additional zero bytes during ordinary reading; it still means -1.
Sign-extended `ConstSigned(-1)` may likewise use a wider signed field.
Neither admits uint32 `ConstSigned(-1)` or an int32 mathematical value
outside its range. A wider field does not create a wider scalar type.

Boolean and unit zero may use wider zero fields, but other invalid patterns
do not become legal. Widening floating data preserves signed zero and every
NaN sign, signaling/quiet bit and payload bit. It does not execute arithmetic,
quiet a NaN, normalize its payload or depend on host floating-point state.
Unadmitted formats remain unsupported even if their storage size matches.

## 6. Re-encoding and evidence

Shrinking or widening an instruction changes its byte extent. A rewriter
repairs every affected definition, block, terminator and containing-row
offset while retaining logical operation ordinals and references.
Changing Body-local width also repairs its structural fields and sentinels.
Changing map order remaps every local type selection in its actual context.
Opaque bytes are not fixup lists; optional slotted data is preserved only
under its [existing remap/drop contract](reader-trust.md#6-validation-claims-transformations-and-reuse).

These operations preserve selected identities, values and exact bits, not
merely storage size or a same-shaped signature. No effect may be dropped
because its result is unused, and no private-body optimization or nominal
substitution is justified by canonicalization.

An audit states whether it checked selected instructions, a Body's complete
width/map rules or every applicable object in a declared artifact scope.
Unvisited payloads are not certified. Unsupported required interpretation
or incomplete/exhausted work declines the requested claim; a trust decision
does not supply missing canonicality evidence.

The rules define `encoding-canonicality/1` under the exact
`trrm/1-draft-unified-3` schema and out-of-band governing-chapter agreement.
They change ordinary admission policy, not byte layouts, allocation,
feature identity or executable membership. Numerical structural/semantic
resource minima remain unassigned under the separate
[resource-accounting contract](resource-accounting.md).

The [Module manifest](semantic-manifests.md#3-rows-ranges-and-ordinary-encodings)
additionally distinguishes equivalent explicit Feature-list order/duplicates
from exact-revision conflicts. Its canonical writer/audit rule is local to each
list, not a globally unique interning or whole-artifact image requirement.
