# Semantic IL and metadata extensions

> [!IMPORTANT]
> The high-level size-oriented compact direction is accepted, but exact writer
> policy and new compact layouts remain pending. The inline type word is not
> part of approved `(1, 0)`; existing readers reject it and unsupported
> executable forms.

## Inline built-in type reference

This extension is distinct from the approved 8-bit-kind/24-bit-row `u32`
reference word. Reserve kind `0xFE` for typed type-reference fields, with
the word `0xFE_qq_ff_pp`: `pp` is an assigned atomic built-in code, `ff=0`
means the atomic type itself and an assigned one-argument family code in `ff`
means that family over `pp`. In `qq`, bit 0 qualifies the outer type as
`write`, bit 1 qualifies its argument as `write`; all other bits reject.
For an atom, `ff=0` and argument `write` must be zero. Both qualifier bits
require a type/family for which the respective authority is legal; unassigned
codes, illegal family/argument pairs and `write` on a non-writable type
reject. Thus `uint8` (code `0x0B`) would be `0xFE_00_00_0B`
(`0B 00 00 FE` little-endian), `ref<uint8>` (family `0x8B`) would be
`0xFE_00_8B_0B` (`0B 8B 00 FE`), and `write Span<uint8>` would be
`0xFE_01_8A_0B` (`0B 8A 01 FE`). This reserves one of 256 reference-kind
values, not TypeSpec rows: TypeSpec retains its `0xFF_FFFF` row ceiling,
and kind `0xFF` remains available for future required indirection.

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
Adoption needs an explicitly selected schema and updated readers/writers;
current-schema readers reject these candidate words. Under the
[draft-version policy](../specs/representation/format-versioning.md), that
schema may retain `(1, 0)`. A new version pair, old-reader compatibility or
automatic downgrade is not a prerequisite for a pre-release design change.

**Pending allocation:** keep `0xFE` unassigned until the field matrix,
resolved identity, canonical writer and measured benefit are reviewed.
Recommend changing the complete typed-reference fields in one selected draft,
not adding a legacy decoder or a second meaning to current-schema words.
The candidate remains rejected by the currently accepted schema.

### Decisions before adoption

| Open decision | Evidence required |
|---|---|
| Selected schema and admission | Specify the exact draft and every eligible typed-reference field, including whether a coherent required capability is additionally needed. A Feature cannot teach an old decoder a new word, but the draft itself may change while retaining `(1, 0)`. Reject unknown codes and qualifiers; do not guess indexed TypeSpecs or promise older-schema conversion. |
| Canonical identity | Specify resolved-type equality across inline words and indexed TypeSpec rows, including outer versus argument `write` and field-specific restrictions. Test that a Body map rejects duplicate resolved types even when one spelling is inline and the other indexed; raw-word equality is insufficient. |
| Measured benefit | Compare emitted bytes, TypeSpec row count, cold decode and selective-import cost on representative scalar and generic-heavy artifacts. Count rows still needed as operands of nested TypeSpec forms; an inline word is not evidence that all corresponding TypeSpec rows disappear. |

### Minimal versus substantive integration

A Body-map-only experiment changes fewer fields but leaves duplicate type-word
rules throughout metadata. Recommend evaluating the shared complete-type-field
design above against that baseline, including generic/construction and native
signature carriers. Preserve typed row/range references as rows rather than
turning every `u32` into a polymorphic reference. Revisit `0xFE` and the held
`0xFF` indirection together if a better namespace structure emerges; their
unpublished values are not compatibility constraints.

On approval, update the registry, metadata field matrix, Body type maps,
canonicality, packing/remapping and independent conformance vectors together.
Do not register the word from a size experiment alone.

## Compact-profile selection and follow-up

The assigned compact layouts and accepted size-oriented direction are recorded
in the [instruction-set specification](../specs/il-instruction-set.md#compact-body-forms).
Exact writer selection and any additional representations remain open. The
remaining decisions are:

| Area | Pending decision |
|---|---|
| Writer selection and additional forms | Exact eligibility, canonical writer order and layouts remain unassigned; a section-presence bitvector is one candidate. |
| Offset width | The compact-indexed form couples local-field widths. Independent compact-index widths and their 255/65535-byte boundary behavior remain unapproved future-format work. |
| Logical roots and effects | Define the method/block effects, roots, owners, interior dependencies, cleanup and exceptional continuations needed by executable features, and encode them so readers can validate them. |
| Cross-method sharing | A future ownership revision must validate every referencing MethodDef's signature and contracts. Defer until that model and a concrete need are established. |
| Inlining contracts | The exact Body/contract model remains open; see the [instruction-set specification](../specs/il-instruction-set.md#compact-body-forms) for the accepted declaration-contract boundary. |
