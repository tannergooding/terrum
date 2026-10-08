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
Adoption needs a format revision and updated readers/writers; current
readers must reject these candidate words.

**Decision recorded:** defer allocating kind `0xFE` to this extension until
the format revision, negotiation and downgrade rules are specified and
compatibility and performance evidence is available. The candidate remains
rejected by the approved `(1, 0)` schema; this decision does not change its
reference-word contract.

### Decisions before adoption

| Open decision | Evidence required |
|---|---|
| Revision and negotiation | Specify the schema revision and whether a required feature additionally gates kind `0xFE` in every eligible typed-reference field; a feature alone cannot change the `(1, 0)` schema. An `(1, 0)` reader must continue to reject it; a new reader must not interpret an unknown inline code as an indexed TypeSpec or silently drop its qualifiers. Determine when a writer can rewrite an artifact to an earlier revision by expanding every inline reference into valid TypeSpec rows. |
| Canonical identity | Specify resolved-type equality across inline words and indexed TypeSpec rows, including outer versus argument `write` and field-specific restrictions. Test that a Body map rejects duplicate resolved types even when one spelling is inline and the other indexed; raw-word equality is insufficient. |
| Measured benefit | Compare emitted bytes, TypeSpec row count, cold decode and selective-import cost on representative scalar and generic-heavy artifacts. Count rows still needed as operands of nested TypeSpec forms; an inline word is not evidence that all corresponding TypeSpec rows disappear. |

## Compact-profile selection and follow-up

The two compact tags and their bytes in the approved instruction design
are settled for the bounded subset. The accepted size-oriented direction is
recorded in the [instruction-set specification](../specs/il-instruction-set.md#compact-body-forms);
exact writer selection and additional representations remain open. The
following are future design and implementation decisions:

| Area | Accepted boundary and remaining decision |
|---|---|
| Shape partition | Accepted direction: choose or add compact forms by the information common method shapes require, omitting only derivable/default data. A section-presence bitvector is one candidate; exact forms, eligibility and writer order remain open. General form remains necessary when compact forms cannot represent required semantics. |
| Offset width | The current compact-indexed form couples local-field widths. A separate compact-index width and its 255/65535-byte boundary behavior remain unapproved future-format work. |
| Logical roots and effects | Any method/block effects, roots, owners, interior dependencies, cleanup or exceptional continuations required by a feature must remain represented. Exact contracts and encoding remain open; no managed/exceptional compact execution until readers validate them. |
| Cross-method sharing | Sharing Body rows needs a future ownership revision that validates every referencing MethodDef's signature and contracts. Defer until that ownership model and a concrete need are established. |
| Trusted selective import | Accepted boundary: executable readers validate structural bounds, metadata binding and opcode membership in every block, including unvisited blocks. Feature negotiation for future extensions remains unspecified. |
| Declaration contracts | Accepted invariant: removing authored guard blocks does not remove separately required contract checks, their effects or failure order. The exact Body/contract model for inlining remains open. |
