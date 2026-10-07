# Semantic IL and metadata extensions

> [!IMPORTANT]
> Pending revisions and performance decisions only. The approved `(1, 0)`
> [metadata](../specs/il-metadata.md) and [coded instructions](../specs/il-instruction-set.md)
> do not use these extensions. Existing readers reject unassigned words and
> unsupported executable forms.

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
Adoption needs a format revision and updated readers/writers; current
readers must reject these candidate words.

### Decisions before adoption

| Open decision | Evidence required |
|---|---|
| Revision and negotiation | Specify the schema revision and whether a required feature additionally gates kind `0xFE` in every eligible typed-reference field; a feature alone cannot change the `(1, 0)` schema. An `(1, 0)` reader must continue to reject it; a new reader must not interpret an unknown inline code as an indexed TypeSpec or silently drop its qualifiers. Determine when a writer can rewrite an artifact to an earlier revision by expanding every inline reference into valid TypeSpec rows. |
| Canonical identity | Specify resolved-type equality across inline words and indexed TypeSpec rows, including outer versus argument `write` and field-specific restrictions. Test that a Body map rejects duplicate resolved types even when one spelling is inline and the other indexed; raw-word equality is insufficient. |
| Measured benefit | Compare emitted bytes, TypeSpec row count, cold decode and selective-import cost on representative scalar and generic-heavy artifacts. Count rows still needed as operands of nested TypeSpec forms; an inline word is not evidence that all corresponding TypeSpec rows disappear. |

## Compact-profile selection and follow-up

The two compact tags and their bytes in the approved instruction design
are settled for the bounded subset. No canonical writer policy or
measured performance claim is established. The following are future
design and implementation decisions:

| Decision | Candidate and evidence still needed |
|---|---|
| Shape partition | Compare one-block linear, 2–3-block boundary-only, indexed small CFG and general forms on representative body distributions; choose by total bytes, cold import, allocations and selective-inlining time under trusted and audited loading, not a hard block-count cutoff. |
| Offset width | Compare a separate compact-index width against the current coupled local width at 255/65535-byte boundaries, including wider IDs with short blocks. Validate exact fallback to the general form. |
| Logical roots and effects | Define mandatory method/block effects and indexed safepoint root/owner/interior dependencies including cleanup/exceptional continuations. Decide inline versus required sparse sidecar by actual density; no executable managed compact form until a reader verifies these facts. |
| Cross-method sharing | A future ownership revision would need to validate exact Body-row sharing within one module against every referencing MethodDef's signature and contracts. Compare remaining independent short rows with a typed, bounded shared-shape table and substitutions; preserve source identity and measure binding and rewrite/remap costs as well as byte savings. |
| Trusted selective import | Resolve unvisited-block unknown opcodes and feature negotiation before a reader relies on terminator/definition indexes without decoding that block; structural bounds and metadata binding never become optional. |
| Declaration contracts | Model authored Body bytes and the separately executed pre/postcondition checks together for inlining/effect decisions. Their ability to remove explicit guard branches does not remove runtime work or failure order. |
