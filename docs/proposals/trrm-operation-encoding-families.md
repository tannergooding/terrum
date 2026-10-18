# Executable TRRM operation-encoding families

**Status: Unified allocation and bounded family direction adopted;
additional operations/encodings remain unapproved**

## Scope and rationale

The [unified TRRM registry](../specs/representation/registry.md) owns current
allocations and coherent semantic-family capacity. It replaces overlapping
generic/floating draft bytes, preserving their operation semantics rather
than unpublished numbers. There are no legacy aliases. The
[floating chapter](../specs/il-instruction-set.md#floating-operations) owns
the selected binary32/binary64 contracts.

The alternatives below address additional operations and encodings, not a
second current registry. Their candidate shapes and size comparisons remain
illustrative. This proposal assigns no feature IDs, extended keys, short
forms, new numeric witnesses or additional executable type support.

The metadata atomic registry already names signed/unsigned 8/16/32/64/128-bit
integers, native integers, binary floating formats, decimal32/64/128, `bool`,
`char8/16/32` and `rune`. Metadata assignment is not executable eligibility:
new formats still need representation, validity, arithmetic, conversion,
effects and lowering contracts. In particular, a character code unit is not
implicitly an integer, and equal-width nominal types are not primitives.

## Alternatives

Here `t` means a Body-local ID for a complete TypeSpec, not an embedded
primitive byte; `rNew` is a value definition, not a mutable register.

| Candidate | Illustrative shape | Tradeoff |
|---|---|---|
| Per-operation, explicit type | `Add t, rNew, rLeft, rRight` | Removes a kind byte and gives direct operation dispatch; spends opcodes per semantic operation and repeats a type often known from operands. One opcode can still serve several admitted types. |
| Generic arity, explicit type | `BinOp kind, t, rNew, rLeft, rRight` | Conserves opcode slots, but adds kind dispatch and redundant type checks; arity alone does not describe type relationships or effects. |
| Bounded semantic families, inferred types | `NumericBinary rNew, kind, rLeft, rRight` | Keeps common same-type operations small; requires an explicit operation/type/effect matrix rather than universal numeric dispatch. |
| Selected short forms | `Add rNew, rLeft, rRight`, expanding to one family kind | Can save a kind byte on hot operations; introduces duplicate encodings and a canonical-choice rule. Allocate only with workload evidence. |

The operation range is `0x01..0x7F`, not all 256 byte values; `7F` is held
for a possible future extended envelope and currently rejects.
Terminators and the `F0` width prefix have separate ranges. Multiplying
operations by types exhausts that budget quickly. Grouping hot operations
into opcode ranges could simplify dispatch or provide short forms, but no
bitfield partition or escape opcode is justified yet. A future escape would
need its own bounded schema and negotiation, not reuse `F0` or reserved bytes.

For a binary result with reference width `w`, excluding any wide prefix,
the existing kind-based shape costs `2 + 3w` bytes. Explicit-type
per-operation costs `1 + 4w`; explicit-type generic costs `2 + 4w`;
inferred-type per-operation costs `1 + 3w`. At one-byte width these are
5, 5, 6 and 4 bytes. An explicit `t` above 255 can also widen *all*
reference operands and require the two-byte prefix even when value IDs fit
one byte. These are schema accounting examples, not measured artifact wins.

## Recommended direction

Prefer bounded semantic families with operand-derived types as the future
baseline; retain explicit destination types where inference is insufficient,
especially conversions and constants. Do not introduce universal
`UnOp`/`BinOp`/`TernOp` dispatch merely to share arity. A family kind must
select a complete rule: legal operand types, result count/type, rounding,
overflow, failure ordering and effects. Checked, wrapping, saturating and
floating arithmetic are distinct semantics, not an implicit policy inferred
from width or an extensible user witness. Nominal operators remain calls.

If an explicit type is retained, define whether it denotes an operand or
result type and require exact agreement with independently resolved facts.
Comparisons return `bool`; shifts can take a `uint32` count independent of
the left type; conversions require a destination distinct from the source.
One undifferentiated `type` field cannot express all these relationships.

An illustrative ternary schema is
`TernOp kind, rNew, rA, rB, rC` (three inputs). FMA would require a
separate single-rounding contract, including NaNs, signed zero and floating
environment behavior; it is not a compact spelling of separately rounded
multiply/add. Conditional select must preserve existing eager `Select`
semantics: all inputs already exist. It cannot replace conditional
evaluation, CFG branches or suppression of effects in a select chain.

An illustrative `TernLog rNew, u8(table), rA, rB, rC` could apply an
arbitrary three-input Boolean truth table independently to each integer bit.
One candidate convention selects bit `(a << 2) | (b << 1) | c` of `table`
for input bits `a,b,c`. Specify equal admitted integer types, declared-width
masking and no arithmetic overflow; whether to admit `bool` separately is
open. All 256 table values would be truth tables, not semantic kind IDs.
ISA availability must not determine interchange semantics.

## Validation, canonicality and Body forms

Keep one deterministic operand schema per opcode, little-endian operands,
independent instruction widths and bounded decoding. Unknown opcodes,
unknown family kinds and unsupported operation/type combinations reject at
the applicable access/admission boundary. An accessed Body does not excuse
malformed inactive blocks; a trusted selective consumer need not pre-scan
every unrelated Body. An explicit type never bypasses descriptor/signature
checks or the selected reader-trust rules.

Compact linear must reconstruct every result's complete type, result count,
uses and ordered effects from the schema and selected declarations; inferred
types still occur exactly once in its type map. Indexed/general descriptors
must agree with the same rules. Arity-based dispatch does not make places,
roots, owners, safepoints or exceptional behavior compact-representable.
Every eligible Body form must preserve those facts.

Keep minimal operand widths separate from instruction canonicalization and
Body-form selection. If short and family forms coexist, require one
deterministic canonical writer spelling per supported schema revision and
specify whether readers reject or normalize alternatives. Do not infer
canonical bytes from algebraic equivalence: floating reassociation, FMA
contraction and effectful select rewrites can change behavior. Re-encoding
must rebuild definition/block/terminator offsets and instruction references,
including SourceMap slots. Small byte savings can avoid an indexed
offset-width cliff; added type operands can cause one.

## Decisions and evidence needed

Before allocation, settle the semantic family/kind registry, eligibility
matrix, explicit-type exceptions, ternary contracts, short-form canonical
rules and opcode-space/escape budget. Measure representative scalar and CFG
artifacts across reference/offset width boundaries: total bytes including
maps/indexes, cold audited import, allocation, repeated/selective import and
dispatch/lowering cost. Compare identical semantics, not FMA against a
different two-rounding operation. Existing size fixtures establish Body
tradeoffs, not a performance result for these candidates.

A minimal new subcode is appropriate when its full semantics fit an existing
family. Otherwise revise the family or its reservation before adding another
escape/short form. Reassign unpublished primary bytes when that makes the
combined integer, floating, native-memory and invocation layout clearer.
Allocation review must include the native and generic metadata proposals,
not only a collision test against today's registry. No preservation of an
experimental byte is required.

Any additional adoption needs an explicit schema decision. Pre-release
`(1, 0)` can evolve only with exact-schema agreement; matching version bytes
are insufficient. Preserve semantic contracts, not overlapping unpublished
byte assignments. Before any post-release evolution, choose version and
required-feature negotiation, reader rejection and migration rules.
Optional sections cannot carry executable semantics, and writing a Feature
number alone does not activate an unsupported schema. This proposal promises
neither post-release compatibility nor a current artifact migration.

Repository evidence:

- [Instruction envelopes and widths](../specs/il-instruction-set.md#instruction-encoding-structure),
  [compact Bodies](../specs/il-instruction-set.md#compact-body-forms) and
  [type/result rules](../specs/il-instruction-set.md#type-and-definition-model-for-operands)
  establish the accounting, reconstruction and exact-type constraints.
- [Operation schemas](../specs/il-instruction-set.md#operation-encoding-and-normalized-effects)
  separate integer kinds, eager selection and checked multi-result operations;
  the same unified registry now owns the selected floating allocations.
- [Metadata TypeSpecs](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics),
  [revision scope](../specs/il-metadata.md#format-identity-and-revision-scope) and
  [section rules](../specs/il-metadata.md#section-kinds-and-row-layouts)
  distinguish type assignment, schema agreement and required semantics.
- [Canonical floating contracts](../specs/il-instruction-set.md#floating-operations)
  and the [unified registry](../specs/representation/registry.md) own selected
  floating semantics and current allocations. The
  [encoding history](../implementation/trrm-encoding-history.md) distinguishes
  retired development numbers from those assignments.
- [Linear reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmCompactLinearBodyReader.cs)
  historically derives `0x1D` results from equal floating operands and rejects other kinds;
  [compiler tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmExecutableCompilerTests.cs)
  cover floating arithmetic and malformed kinds/integer operands. Existing
  implementation and tests are evidence, not approval of future families.
