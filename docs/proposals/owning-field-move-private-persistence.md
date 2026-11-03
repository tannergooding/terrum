# Owning-field moves in private persistence

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** Review and approval remain outstanding. This
recommendation selects private encoding direction, not normative adoption,
implementation support or runtime qualification.

## Decision and scope

Recommend **private expression discriminator `42` (`0x2A`) for
`BoundFieldMove` in the revision-92 draft**, with exactly one existing
field-place expression as its operand. Select the whole amended draft out
of band as **`terrum/private-semantic-92-field-move-1`**. Keep the header
`TRRM` followed by little-endian **i32 `92`**, not Standard TRRM's u16 pair.
Every standalone, set, graph and embedded semantic reader/writer must agree
on this same draft.

This is the smallest sound change: it adds a previously unsupported
expression, without changing any existing record's layout or meaning.
A new private revision is possible but unnecessary under
[draft schema agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts);
it would also require updating the codec's separate revision gates.
Neither option supplies compatibility. There is one selected decoder, not
an earlier-92 attempt followed by a newer-92 attempt. Matching revision
bytes, profiles or outer checksums do not establish draft agreement.
Regenerate artifacts whose schema is not the selected one; do not guess
their layout, patch headers or introduce aliases.

The base is the selected private-92 composition in
[bounded semantic modules](../implementation/modules.md#definitions-and-scope):
resolved arithmetic, NoThrow retirement and the complete declaration-owned
NativeOutput delta, with the current nested-assignment framing below.
All other bytes, meanings, resource limits and support/admission gates stay
unchanged. General dependencies, wide scalars, nominal enums and checked
references do not become persistable through this amendment.
The [checked-reference candidate](checked-reference-private-persistence.md#new-operation-schemas)
assigns expression 42 a different meaning in its **distinct, unimplemented
revision-97 draft**. This assignment is version-local, not an amendment to
that draft or an automatic composition with it.

The accepted [structural move and repair rules](../specs/language/ownership-and-cleanup.md#structural-field-moves-and-repair),
[complete cleanup rules](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement)
and [producer/consumer boundary](../specs/representation/reader-trust.md)
remain the semantic owners. Only the private codec boundary is amended.
Under the [persistence taxonomy](../specs/representation/persistence.md),
**this proposal creates no Standard `.trrm` support**: no Standard opcode,
Body record, FieldPath mapping, Feature, type codec or admission change.
It also changes no source syntax, native ABI, runtime policy or cross-image
ownership permission.

## Current implementation state

* [`BoundFieldMove`](../../sources/Terrum.CodeAnalysis/Semantics/BoundFieldMove.cs)
  holds a typed `BoundFieldRead Place` and exposes it to shared traversal.
  Binding and verification now handle nested unique field paths.
* [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
  selects only private revision 92. It now writes and reads discriminator 42
  with the typed field-place operand, resolves that place in its enclosing
  safety scope, and visits it during call/type closure. `RemapExpression`
  remaps the move's result type and recursively remaps its place.
* Statement discriminator 9 already persists `BoundFieldAssignment` as
  `i32 variable, i32 firstField, i32 nestedCount`, then exactly
  `nestedCount` i32 field ordinals and one value expression, after its
  unchanged statement envelope. The count is mandatory even when zero.
  This proposal retains that layout; an earlier same-numbered draft
  without the count is not an alternate decoder.
* [Implementation support](../implementation.md#hosted-compiler) and
  [CG-009](../implementation/conformance-gaps.md#cg-009-owning-fields-and-partial-retirement)
  still record broader ownership/persistence gaps. In-progress nested
  source/native support does not close private exchange or that milestone.
* The private-module test independently checks the tag-42 envelope and two
  field-place nodes, then rejects an unknown tag, non-field operand,
  out-of-range field/root and field-type mismatch. Direct module replacement
  plus native graph and set execution exercise the same private payload with
  a consumer-owned type preceding producer types. The Debug fast build and
  combined checked-reference, partial-owner, nested-owner, cleanup and
  private-module selection passed 63/63 with zero skips at
  `artifacts\\tst\\Debug\\nested-owner-complete-final-v5\\nested-owner-complete-final-v5.trx`.
  This is focused private-codec evidence, not Standard TRRM or milestone
  acceptance.

## Exact record and place identity

All integers below are little-endian. `Envelope` is the existing 13-byte
expression prefix; the parentheses name values, not additional fields.

```text
Envelope(kind, type, span):
    u8 kind
    i32 type
    i32 spanStart
    i32 spanLength

FieldMove:
    Envelope(42, movedType, moveSpan)
    FieldPlace

FieldPlace:
    Envelope(7, fieldType, fieldSpan)
    i32 fieldOrdinal
    (FieldPlace | RootPlace)

RootPlace:
    Envelope(2, rootType, rootSpan)
    i32 variableIndex
```

The next expression discriminator selects each receiver alternative; there
is no additional union tag. A path has at least one FieldPlace and ends in
exactly one RootPlace. Preserve the existing bound tree and each node's
type/span. For `k >= 1` fields, FieldMove occupies exactly **`30 + 17*k`
bytes** and **`k + 2` expression nodes**: 47 bytes for one field, 64 for two.
There is no path count, separate owner ID, byte offset, live mask, cleanup
flag, address, authorization bit or optional trailer.

`variableIndex` is zero-based in the containing Function's declared variable
collection. `rootType` must equal that variable's type and select an admitted
unique inline structure. Resolve the chain **from the root outward**.
Each ordinal is zero-based in its immediate containing `BoundStruct.Fields`,
not a module-wide field table or a native offset. Each receiver type must
equal the preceding selected type; each field type must equal the selected
declaration's type. Every containing step is an admitted unique inline
structure, and the final type is an admitted unique value.
`movedType == outermost FieldPlace.type` is mandatory.

Reconstruct exactly `BoundFieldMove(place, movedType, moveSpan)`. The operand
is a **place selection**, not a sequence of ordinary whole-value copies:
an independently live child may be selectable while an ancestor is partial.
Calls, casts, receiver loans, whole-owner moves, reference dereferences,
class/box fields, array elements and arbitrary expressions cannot be roots
or intervening steps. Unsupported types remain unsupported; no type is
inferred from a same-sized carrier or coerced to make a path fit.
Expression 9 remains solely `BoundMove(variable)`; it is not a field-move
alias. Existing loan expressions 11/20 keep their own effects.

## Validation and ownership

Producers must verify the authored or transformed program before writing:
the selected child is complete and live, every path step has the required
write or unpublished construction authority, and transfer does not leave
an authored complete-value Cleanup receiver partial. Preserve the existing
constructor exception and enclosing-ancestor restrictions, not a new
serialization-specific move permission.

The reconstructed operation transfers the child's actual ownership,
backing/provenance and cleanup obligations once, vacates only its subtree,
and advances the affected place generation. Ancestors become appropriately
partial; unrelated live siblings and their generations survive. Repair and
partial retirement retain their existing field paths and orders. Do not
replace a move with a read, whole-root move, zero store or eager cleanup.
Rebuild flow/generation facts from operations; never serialize a verifier
instance's masks, provenance keys or native cleanup-state flags.

Before affected consumption, validate framing, exact supported kind/path
shape, all variable/type/field references and type equalities above, selected
layout/support and binding compatibility. Enforce existing 4 MiB,
100,000-node and depth-128 codec budgets, counting every wrapper, field and
root through ordinary expression accounting; enclosing statement depth
also counts. Check negative/one-past indexes, truncation, trailing bytes
and spans with nonnegative start/length and nonoverflowing end. Existing
stricter type/field/slot limits still apply.

`ResolveExpression` must descend into the place under the actual enclosing
unsafe context, just as for other field-place operands. Do not persist or
trust `IsUnsafeAuthorized`, hide a required scope, or widen the current
caller-unsafe field admission. Preserve existing producer and loaded-program
verification calls. Producer truth includes liveness, complete-value,
authority and exactly-once obligations; mandatory consumer structural
checks do **not** create a requirement to semantically re-prove every
producer body. A structurally well-formed but false ownership claim is a
producer violation, not a promised new consumer diagnostic.

## Remapping and provider replacement

Keeping field ordinals is sound **only under the existing ordered-layout
invariant**. `SelectStructures` retains whole field collections;
`RemapStructure` changes their types, not their order.
`SameStructure` compares fields at the same ordinal, including name,
resolved type, size/offset, mutability, visibility and caller-unsafety,
and compares the structure's size, uniqueness, cleanup and layout
commitments. Imported layouts are checked through ordinary linking,
[sets](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs)
and [graphs](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs).
The required-structure closure retains nested field types. These checks,
not coincident ordinals, names or offsets across arbitrary providers,
justify the unchanged `Field` and `NestedFields` integers.

Retain those checks and every original expected layout. Reordered structure
tables may remap all result, root, intermediate receiver and field types;
reordered imported fields must still reject. Visit the place in `Calls`,
resolution, shared traversal/type rewriting and dependency projection,
including native-pointer/function-pointer remappers. Local variable
indexes remain function-local; any transformation that reindexes variables
must map the root explicitly. Validate the rebuilt path against the
destination layouts before lowering. This proposal supplies no general
field-reordering adapter or resilient layout contract.

Private native containers keep their current outer-3 layouts and independent
admission. Any retained inner semantic payload must select this exact draft;
rebuild affected lengths/digests and regenerate affected images, rather
than treating an intact old image as evidence of a correct new codec.

## Required acceptance cases and remaining decision

Use public compiler/module APIs and independent encoded fixtures. These
are implementation gates, not results of this documentation pass.

| Gate | Required cases |
|---|---|
| Exact round trip | Producer-owned and consumer-owned bodies with direct and multi-level field moves; inspect the linked `BoundFieldMove`, every place node/type/span and root/ordinal. Combine tag 9 whole moves, nonzero `NestedFields` repair and partial cleanup. Independently assert tag 42 and the 47/64-byte extents; no source rebinding. |
| Semantic preservation | Headless loaded-program execution under both runtime policies: moved-child cleanup once, live siblings retained, nested repair, failed repair/unwind and whole-partial retirement. Producer negatives include repeated/vacant moves, whole use of a partial ancestor, missing mutation authority and an enclosing authored Cleanup receiver. Include moved/sibling reference-generation behavior only where the existing private type codec admits it; checked-reference persistence remains gated. |
| Structural rejection | Non-field operand, missing field step, wrong root kind, negative/one-past variable/type/field references, receiver/field/result type mismatches, nonunique or unsupported containing/leaf types, truncated envelopes/operands, invalid spans, trailing data and exact depth/work/size boundaries plus one-past. Earlier omitted nested-assignment counts are not inferred. Well-typed false body facts remain separate producer/audit tests. |
| Binding and closure | Reorder structure/function/type tables while preserving fields, in ordinary producer replacement and applicable set/graph/nested-contract paths. Preserve local/private owner separation and required nested types. Reject changed imported field order, type/access, uniqueness, cleanup or layout; do not relax existing boundary admission to make a fixture pass. |
| Schema isolation | Keep non-92 revisions rejected and unknown revision-92 expression tags rejected. Exercise existing arithmetic/output records beside tag 42. Do not interpret revision-97 ReferenceForm 42, old tag-9 bytes or a native-profile string as this node. Embedded payloads use the same schema; valid outer integrity does not bypass inner checks. |

Existing test owners are
[`ModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs)
(layout/order/replacement and malformed input),
[`NativeOutputModulePersistenceTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs)
(exact revision 92 and type-table remapping), and the source/native
[`partial-owner`](../../tests/Terrum.CodeAnalysis.Tests/Native/Jit/PartialOwnerFieldLifecycleTests.cs)
and [`nested-owner`](../../tests/Terrum.CodeAnalysis.Tests/Native/Jit/NestedOwnerFieldOperationTests.cs)
tests. Their source is not, by itself, evidence that every private consumer or
malformed-input case is covered. The formal proposal remains unapproved.

**Decision requested:** approve the version-local private-92 discriminator
42 and single typed field-place operand above. No unresolved choice blocks
this bounded codec under provisional direction. Formal approval, composition
with the separate complete-97/checked-reference draft, and any Standard
TRRM representation remain outstanding and are not selected here.
