# Terrum coded instruction set

> [!IMPORTANT]
> Approved current design for the specified Body encoding and bounded
> executable subset, not the current semantic-module writer or a released
> interchange format. Unsupported operations and unencoded contracts do not
> become executable by appearing in the opcode registry.
> `(1, 0)` below identifies the bounded subset of the approved metadata
> design's format revision `(1, 0)`, not a separate on-wire profile selector.
> Profile negotiation for future revisions remains open.

The [approved current metadata design](il-metadata.md) defines the Body table,
TypeSpec, MethodDef, contracts and artifact framing. This document specifies
the Body payload and operation stream separately, following only the
*organizational* distinction between ECMA-335 Partitions II and III. It does
not adopt CIL opcodes, its evaluation stack, branch displacement formats,
metadata tokens or runtime ABI.

## Model and canonical form

A Body encodes an implementation owned by exactly one MethodDef in its
module. A **block** has typed parameters, ordered operations and one
terminator; each normal edge supplies its successor's
parameters. Checked operations and potentially failing calls do not end a
block merely because they can fail. They define their result at their
operation site on success; failure terminates or unwinds under the selected
runtime policy, without a normal edge or a normal successor. Normal block
boundaries are the method entry and explicit normal branch or transfer
targets, including merge and backedge targets; branch, return, failure and
region-exit terminators end their current block. A value definition or
liveness change does not create a block. An optional unsafe catch requires
separately specified exceptional state and handler rules.
A **value ID** denotes an immutable typed definition. A **place** denotes
addressable storage with separate initialization, mutability and ownership
state; loading a value is distinct from borrowing or moving its place. A
**region** groups blocks for loop structure or lexical lifetime. Only the
function region (ID 0) is implicit. Optional SourceMap spans do not affect
execution.

Explicit blocks and regions avoid recovering control flow from branch
displacements or backedges, and local lifetimes from source names. This is
not an optimizer's canonical SSA: block parameters replace implicit stack
merges, but place writes, calls and failures remain ordered effects.
Interpreters, JITs and AOT compilers may lower verified blocks independently.
Untrusted loop/region facts require validation; trusted producers may supply
them to a fast consumer, with semantic audit as an explicit option.

Value IDs and block parameters form an SSA-like value graph, while mutable
places and ordered effects remain explicit rather than pretending all storage
is in SSA. The Body records value types and cross-block liveness for ordinary
values; consumers need not infer those solely from instruction uses. It
also indexes each value's defining operation and its final operand use in
each using block, but not every intermediate use. It does not yet encode
per-safepoint logical roots or the native locations required by a GC.
Those are separate concerns: a future producer-recorded root inventory
can identify logical roots, ownership and interior-reference dependencies,
but only the backend knows which registers or stack slots hold them after
lowering. Do not treat source-local counts or ordinary SSA last-use as a
complete GC root map.

Numeric policy (checked, wrapping, saturating, bit reinterpretation) is
explicit; converting one into another is not a space-saving normalization.
A discarded result does not discard an effectful call. Typed source binding
selects one MethodDef before encoding; no reader reruns overload resolution.
Operations absent from the assigned tables reject rather than being guessed
from CIL or CLR behavior.

### Architecture-critical indexes and contracts

The worked examples are part of the design explanation, not
replaceable opcode trivia. The intended fast path crosses two documents:

| Input | What a consumer can learn without rediscovering it from instructions |
|---|---|
| [Approved metadata tables](il-metadata.md#section-kinds-and-row-layouts) | Selected declaration and complete type/signature; retained imports bind against published contracts without re-proving dependency bodies |
| [Contract clauses](il-metadata.md#semantic-contracts-and-stable-binding) | Assigned effects such as `NoThrow`/`DoesNotReturn` and reserved output, transfer, dependency and cleanup shapes; only Effects is executable in this narrow Body subset, so readers must not assume the other contracts are already usable |
| [Body index](#body-record-direct-block-index-and-regions) | Type/value/place inventories, exact definition locations, direct block/terminator addresses, normal boundary liveness and per-block final uses |
| [Compact Body forms](#compact-body-forms) | One-block linear encoding derives indexes on a scan; compact indexed retains direct seeks for small CFGs; neither omits required semantics or enables unassigned roots |
| [Conditional add](#adding-a-conditional-to-the-sum), [merge and loop](#conditional-merge-and-loop-informative-shapes) | How a single checked block grows normal branches and a backedge; which values merely transit a block, which ones are actually used there, and how pruning a dead arm updates surviving use-block counts |
| [Checked-add bytes](#checked-add-end-to-end-informative) and [place example](#value-and-place-state-through-checked-arithmetic) | Ordered failures without artificial CFG edges; separate immutable values, addressable storage and their last-use/state transitions |

A specializing inliner can seek a predicate's definition, inspect the
relevant dependency instructions, follow indexed terminators and discard
unreachable blocks using their use inventories. It still decodes retained
operations and respects effects, ownership and selected contracts;
indexes are producer facts under the specified trust/audit policy, not
an independent proof of safety. Method-level effect/call summaries
and per-safepoint logical roots remain
[future work](#tool-roles-and-future-executable-contracts), not capabilities this
bounded subset has already encoded. If these informative examples move to a
companion document, retain this contract map and links from the
normative rules; splitting the examples must not orphan the fast-import
story or silently promote unsupported Body shapes to executable status.
The [example fixture checks](../../tests/Terrum.CodeAnalysis.Tests/TrRmBodyExampleTests.cs)
independently decode the compact bytes below and general-form bytes kept
in the tests; they are test-only checks of the approved bytes, not an
executable TRRM Body reader.

The examples progress from a checked scalar sum in one block to a
conditional sum and repeated additions around a loop header. A separate
merge example supplies exact bytes for edge arguments, and an addressable
local illustrates why place state differs from values. A later
[exception sketch](#why-a-catch-is-not-another-normal-edge) describes a
missing contract, not a usable handler encoding. The source snippets are
illustrative; the recorded Body facts, not the spelling of a source local,
define the bounded Body design.

## Instruction encoding structure

The stream is byte-addressed, not a bitfield ISA. Every multi-byte integer
is little-endian. The opcode selects **one** operand schema; no operand
encodes an addressing mode, a hidden evaluation stack or a type-specific
opcode variant. There are two instruction envelopes:

| Envelope | Bytes in decoding order | Use |
|---|---|---|
| Common | `opcode operands` | Reference, count and data operands each occupy one byte |
| Wide | `F0 layout opcode operands` | `layout` selects the widths for this instruction alone; its `opcode` has exactly the common form's meaning |

As in the approved metadata design, every cross-table reference in Body
is a fixed little-endian `u32` word with the column's typed or explicit
kind rule. The Body header's type map holds TypeSpec references once;
instructions and descriptors use compact one-based **Body-local type IDs**
into that map.
FieldDef row operands use fixed `u32` references directly. Neither a
local type ID nor an instruction's `ref` width changes a metadata row
number, bypasses its TypeSpec or changes its typed-reference rules.

The [approved reference word](il-metadata.md#metadata-logical-tables-and-row-semantics)
is a fixed `u32` split into an 8-bit kind and 24-bit row data; no separate
`uint24` storage type is required. The present `typeMap` holds indexed
TypeSpec references. The separately described
[inline built-in extension](../proposals/il-extensions.md#inline-built-in-type-reference)
would assign kind `0xFE` to complete built-in type words and requires a
future metadata revision and reader/writer support; the approved `u32`
split alone does not adopt that additional kind. Body-local `t` IDs
remain one/two/four bytes and are distinct from artifact-wide references.

The `layout` byte selects widths independently by operand class; bits
6–7 must be zero:

| Bits | Operand class | Codes `0`, `1`, `2`, `3` |
|---|---|---|
| 0–1 | Body-local reference (`r`, `p`, `t`, `m`, block, region) | 1, 2, 4 bytes; `3` invalid |
| 2–3 | Count (argument, case, exit count) | 1, 2, 4 bytes; `3` invalid |
| 4–5 | Data (signed literal or unsigned bit pattern) | 1, 2, 4, 8 bytes |

All operands of one class in an instruction share its selected width;
instructions select widths independently. An unused class must use code 0;
a nonzero code for it rejects.
Enumerations (`kind`, `policy`, `reason`) are fixed `u8`, not width classes.
The opcode defines which operands occur and their order; `layout` changes
widths only. References and counts are unsigned; `bits`/`Switch` data is
unsigned and `ConstSigned` data is two's-complement signed. Writers must
choose the smallest width that represents all operands of each class.
Decoding zero-extends unsigned data and sign-extends signed data to 64 bits.
An `F0` prefix with `layout=0` or reserved bits rejects. Nonminimal widths
are noncanonical; a strict canonicality audit rejects them, but load-time
acceptance without that audit is not specified.

The prefix keeps common instructions to direct opcode dispatch without
multiplying the opcode catalog by width combinations. It adds two bytes to a
wide instruction and avoids continuation decoding per operand; no read or
validation cost is implied.

For eligible trivial methods, compact linear is the recommended
space-saving representation: one entry block, the root region, by-value
parameters and empty place/liveness inventories need no stored records
when their state follows from the signature and bounded instruction
stream. A writer may still choose a valid general form; compact is not
required and consumers must accept either. `bodyLayout=0` selects
one-byte local fields in the general form, whereas compact form tags
select different **storage for the same logical Body**.
The general form is not a suitable space-first default for trivial methods:
the one-block checked-add section is 105 bytes for 16 instruction bytes,
and `return 42` is 57 bytes for 6 instruction bytes. The section
index accounts for 8 bytes in each; the rest is mostly repeated type,
definition and use metadata. Those are measured *encoded sizes* of
these single-row examples, not evidence that removing indexes always
makes JIT import faster. In a multi-row Body section the offset index
uses `4 * (rowCount + 1)` bytes, not a separate eight-byte index for
each row; compare row payloads and shared index overhead separately.

### Compact Body forms

The Body row begins with `bodyLayout` (`u8`), not a MethodDef
owner. Exactly one MethodDef selects each Body row, as required by the
approved metadata ownership rule.
Bits 0–1 choose the Body-local width as in the general form;
bits 2–3 choose the form, with `0` general, `1` compact linear
(`bodyLayout=0x04` at one-byte width), `2` compact indexed
(`bodyLayout=0x08` at one-byte width), and `3` invalid. Bits 4–7
must be zero. Codes `3` in bits 0–1 still reject. The Body-section
row-offset index and instruction envelopes are unchanged. There is
no second implicit width for instruction operands: each instruction
keeps its own common or `F0` wide layout. Every form must reconstruct
the same Body-local IDs, types, ordered effects, boundaries and uses;
decoding a different form never licenses weaker safety checks.

Every Body row must be referenced by exactly one locally authored
MethodDef. Its MethodSig supplies entry parameters and result types;
its module supplies the context for local `m` call operands and
contextual TypeSpec/import references. A reader builds the Body-to-owner
mapping while validating MethodDef references, without scanning for
an owner on every Body lookup. Each declaration retains its identity,
contracts and checks. Any future Body-sharing feature must define
signature, contract, SourceMap and module-relative-reference rules
before changing this ownership invariant.

**Compact linear** is for exactly one root-region block with only
by-value parameters and results, no places and no normal successor.
Its row after `bodyLayout` is `local typeCount`,
`u32 typeMap[typeCount]`, `local operationCount`, exactly that many
operations and one `Return` or `Fail` terminator, consuming the row
exactly. No other index or block header is stored. Block ID 1 and its
region 0 are implicit; absent places, other regions, normal edges and
boundary liveness are not empty arrays to serialize. Entry parameter IDs,
types and modes come from MethodSig in order; every value type, including a parameter or returned
value not named by an instruction, must occur exactly once in `typeMap`.
The mapped complete type must match each MethodSig type. Results such as
comparison `bool` need their own mapped type even when no instruction
has an explicit `t` operand. Result IDs follow in operation order without
gaps; multi-result operations use their declared result-operand order.
Only preceding definitions may be read. Derive `valueCount` from the
by-value parameter count plus instruction result counts as they are
decoded; reject a result that exceeds the selected Body-local width
**before** growing descriptor storage or continuing the scan. Counted
fields and all operands remain bounded by the row extent. Operation
result types and kinds come from mapped type IDs, validated opcode schemas and
selected metadata contracts. Derive descriptors, definition locations,
use-block counts (0 or 1), and last-use ordinals by a bounded
instruction scan. An independent consumer or a repeated uncached
import scans again; this form does not claim direct definition seeks.
There are no stored empty place/region/live arrays, sentinel offsets
or per-value pairs. Only opcodes whose complete result types and effects
can be recovered from their schema and selected declarations are eligible.
The executable compact-linear subset is restricted to the existing
root-free, Copy scalar/optional forms and policy 0; by-value alone does
not prove an operand is root-free or has no cleanup obligation.
Place-producing and place-using operations, non-value parameter modes
and region exits use the general form for structural inspection.
Operations requiring an unassigned root or exceptional contract are
not executable in **any** form.

**Compact indexed** supports multiple normal blocks, including valid
loop regions, when all parameters and results are root-free Copy values
and no place inventory is required. Here, root-free is a property of the
complete value type and its contract; by-value and `Copy` alone do not
prove it. This shape constraint does not supply method-level effects or
per-safepoint roots for operations that can call or safepoint.
It retains the direct definition, boundary and final-use information needed
to prune an inlinable arm.
Its row after `bodyLayout` has, in order:

| Field | Compact indexed encoding |
|---|---|
| Counts and types | `local blockCount`, `local valueCount`, `local regionCount`, `local typeCount`, then `u32 typeMap[typeCount]`; positive blockCount |
| Regions | `regionCount` entries with the general form's `u8 kind, local parent/entry/exit/cleanup` schema |
| Value descriptors | For each ID, `local typeId, local useBlockCount`; kind 0 (by-value) is implicit |
| Definitions | `local valueDefStarts[blockCount+1]` and `local valueDefinitionOffsets[valueCount]`; all-ones at the local width marks a block parameter, otherwise the offset seeks the defining operation within its block |
| Direct addresses | `local blockOffsets[blockCount+1]` from block-data start and `local terminatorOffsets[blockCount]` relative to each block start |
| Block data | Exactly `blockOffsets[last]` bytes of compact block extents in ID order |

A compact block begins with `u8 presence`: bit 0 means a nonzero
`local regionId` follows; bits 1, 2, 3 and 4 mean respectively a
nonempty parameter list, live-in list, live-out list and value-last-use
inventory follows; bits 5–7 reject. Each present list has a `local`
count and the same ordered `local` ID or `(valueId,lastUseOrdinal)`
entries as the general block; an absent list is empty. Then come
`local operationCount`, its operations and one indexed terminator.
There are no place fields. B1's parameter list still matches
MethodSig; other block parameters match incoming edges. The
general-form definition and liveness audits apply to these decoded
logical records. The compact indexed offsets are one/two/four bytes
according to `bodyLayout`, instead of always four. The all-ones
definition sentinel cannot be an operation offset; choose a wider
Body-local width or the general form when any count, ordinal or offset
cannot fit. All block offsets, terminator offsets and the final
block-data length must fit the selected width. Validate each bounded
offset and list before use, even when semantic indexes are trusted.

#### Compact-form information accounting

> [!IMPORTANT]
> **Compact encodings omit storage only where the form defines how to
> reconstruct it.** They never infer control flow from natural
> fallthrough: operation counts and instructions remain encoded, and
> every block has an explicit terminator.
>
> - **Compact linear (form bits 1; `bodyLayout=0x04` at one-byte
>   width):** one block (B1), region 0,
>   no places, no normal edges, and the corresponding block/region/place
>   counts and boundary/use lists are implicit. Entry parameter types
>   and modes and the declared result come from the selected
>   MethodSig/Param rows; only by-value parameters are eligible, and
>   Body-local entry IDs follow parameter order. Result IDs follow
>   operation result order. `valueCount` (by-value parameter count plus
>   scanned result counts), value kinds/types/descriptors, definition
>   locations, use-block counts and last-use ordinals are reconstructed
>   by scanning encoded operations in order, using their schemas and
>   selected contracts. Still encoded are `typeCount`, the fixed-`u32`
>   TypeSpec `typeMap`, `operationCount`, every operation and the
>   terminator. The terminator is not inferred from the row end.
> - **Compact indexed (form bits 2; `bodyLayout=0x08` at one-byte
>   width):** `placeCount=0` and place
>   fields are absent; descriptor kind is implicitly by-value (kind 0).
>   A missing `presence` bit means its list is empty; a missing region
>   bit means `regionId=0`. Nonzero region IDs and nonempty lists are
>   encoded. Block IDs follow `blockOffsets` order; each descriptor slot
>   `i` describes value ID `i+1`. Counts, type map, region entries,
>   descriptors, definition indexes, block/terminator offsets,
>   operation counts, operations and explicit terminators remain
>   encoded. Edges and liveness are not inferred from fallthrough.
> - **Cross-section and section data (both forms):** `MethodDef.bodyRow`
>   selects the Body row; `MethodDef.signature` selects MethodSig, whose
>   parameter range selects Param rows. The Body `typeMap` contains
>   encoded fixed-`u32` references to artifact-wide TypeSpec rows; local
>   `t` IDs index that map, not metadata rows. The owning module supplies
>   contextual resolution for method/import operands. Directory entries
>   and the Body-section row-offset index are also encoded outside the
>   Body row; they are not omitted or reconstructed from the row.

The general form remains the structural fallback for places, non-value
parameters and other rich bodies; **neither form yet encodes** complete
ownership/loan/cleanup, safepoint roots or exceptional regions. A
compact form may structurally contain assigned opcode
shapes only when its derived records are complete; it does not make
calls outside the executable subset, fields or exceptional operations executable.
Future root and method/block effect inventories must be represented
in *every* eligible form before adding features requiring them to its
executable subset. `NoThrow` is neither purity nor a no-safepoint
promise. In particular, a scalar-only compact example is not evidence
that an arbitrary managed forwarder or auto-property getter is already
representable. The common one-block target includes such methods when
their selected calls, access paths, roots and effects have a complete
compact contract, not by dropping any of those facts.

These two approved encodings are alternatives; no exact canonical writer
policy is selected. **Accepted direction:** compact representations should
match the information a method needs and common method shapes, with persisted
byte reduction as a design goal. Omit information only when it is derivable
from the body or fixed by a defined default; required semantics such as
effects, lifetimes, ownership, cleanup, roots and exceptions must remain
represented. The representation set may grow, including forms that explicitly
encode section presence or defaults instead of deriving them by scanning.
This direction does not choose an encoding order or claim that decoding is
cheaper. Compact-linear's scan-derived facts are one encoding mechanism, not
the reason to select it.

An unassigned **boundary-only** candidate for small CFGs would encode direct
block/terminator extents and typed edge parameters, while omitting definition,
use and liveness indexes only if consumers can reconstruct or otherwise obtain
the required information. Other encodings, such as a presence bitvector for
omitted/defaulted sections, are also possible. Neither the boundary-only form
nor a bitvector layout is assigned. Selection should follow the method's
required information rather than a block-count cutoff. Widening
compact-indexed offsets past 255 bytes currently widens **all** its local
fields; independently sized offsets remain a future format option. All forms
use minimal field widths where selected, and a reader can accept a valid
general form for a compact-eligible body. The size-oriented direction does
not require a decoder-cost benchmark; specific performance claims remain
separate. Declaration pre/postconditions can eliminate authored guard blocks
from a method Body, but their checks and failure/effect facts still belong to
the declaration's contract execution and import cost; this does not claim
pre/postcondition encoding is already executable or free.

| Method shape | Representation characteristics | Present status |
|---|---|---|
| Root-free one block, no normal edges | Compact linear omits flowgraph state absent from this shape; value facts are derivable from its operations | Assigned bytes: checked add 31, `return 42` 21, `return x` 17 section bytes |
| Small root-free normal CFG | Compact indexed retains direct definition/use and terminator indexes; alternate omission/default encoding is unassigned | Compact indexed is assigned; lighter alternative remains open |
| More blocks, loops or many uses | Required direct indexes depend on the method information consumers need; local-width offsets and index inventories affect encoded size | Four-block indexed merge 100 versus general 165 section bytes; no general performance claim |
| Places, owners, handlers, roots | Required state, effects, cleanup and safepoint facts dominate; choose sparse sidecars or compact headers by actual density without changing meaning | General structural form for current decode shapes; executable root/exception contract unassigned |

Accelerator sidecars (for example, precomputed definitions or last uses)
can be optional only if readers have a specified, bounded reconstruction
path and rewrites discard or rebuild stale facts. Logical roots,
owner/interior dependencies and exceptional cleanup are **required**
semantics, not optional hints: a future sidecar containing them must
be required and feature-negotiated for executable users, with validated
Body ownership and reference remapping. The approved optional-section
skip policy does not make required Body semantics dispensable. Native
post-allocation GC maps remain JIT/AOT products, not reusable Body
metadata. Shape interning across bodies likewise requires a separate
validated template reference: storing separate Body rows with *identical*
bytes for compatible MethodDefs does not make `return 42` and `return x` the
same implementation. Compare template binding, remapping and lookup
cost against storing short rows plus the amortized section index before
introducing a shared-shape table.

Opcodes `0x01..0x7F` are operations; `0x80..0xEF` are terminators.
`0xF0` introduces a width layout and is not standalone; `0x00` and
`0xF1..0xFF` reject. A wide prefix, layout and registered opcode must fit
within one block. Operands follow the opcode schema; there is no
per-instruction length. Unknown opcodes reject semantic loading and cannot
be skipped using an apparent length or prefix. Bound every operand and
counted field to the remaining block extent before use. Before allocating or
traversing repeated items, prove their minimum encoded bytes fit that extent.
After exactly `operationCount` operations, the terminator must be
one registered instruction that ends the block. General and compact
indexed forms also verify their stored terminator offset.

Only opcode values with an assigned operand schema in the tables below are
registered; explicitly reserved entries also reject. This stream has
no opaque-instruction extension form. The metadata design's optional-section
policy does not make Body opcodes optional: a present Body section carries
required semantic content.
Future opcode allocation and feature/revision negotiation remain unspecified.
An executable reader of this revision validates opcode membership in every
block before selectively importing its operations, including blocks it
will not compile. A trusted body's optional semantic audit does not waive
this mandatory structural/feature check.

An interpreter may dispatch on `(opcode, layout)` or decode native-width
operands; either choice needs measured cost evidence. Body-local IDs `1..255` use the common form, `256..65535` use two bytes,
and larger IDs use four. Fixed metadata `u32` operands do not widen with
the instruction layout.
This favors predictable dispatch and direct indexes for small methods over
minimizing every large literal or body. Explicit edges, place-state joins and
loop headers expose dataflow to consumers; no throughput or total-size win
is asserted.

The general and compact-indexed forms' upfront type/value inventories,
block/terminator offsets, direct value-definition locations, and
per-block use/last-use inventories are intended as **shared importer
inputs**. The general form also carries place state. For these forms,
each consumer can obtain typed ID bounds, normal CFG targets and boundary
liveness without rediscovering them by scanning the operation stream;
compact linear deliberately derives its omitted one-block facts by
decoding the stream instead.
Inlining profitability, register allocation and whether a particular
result-producing instruction is dead remain consumer decisions. A direct
terminator index does not by itself reveal calls or failures inside a block;
tools still need to inspect its operations for those facts. A compact,
mandatory method-level operation/effect summary is needed to avoid repeated
full-body scans for common inlining and policy decisions, but its exact
counts, effect taxonomy, consistency checks and location in the Body header
remain to be specified before claiming that fast path. Unvalidated summaries
must not authorize omitting safety checks or dropping operations.

### Direct-index requirements for pruning and inlining

For general or compact-indexed multi-block bodies, block/terminator
offsets, typed value descriptors and boundary liveness alone are
**not sufficient** for a fast inliner. The definition index and
per-block use inventories below additionally identify where a branch
predicate was defined, which block owns each result, and which blocks
use a value or place. An inliner specializing
an argument should be able to find the predicate definition from its
value ID, inspect only the predicate's needed dependencies, select a
known branch, and discard its unreachable block subtree without decoding
unvisited operation streams. That does not imply that arbitrary predicates
are constant: effects, calls and unsupported expressions require the
normal importer path.

Each `valueDefStarts` range gives the defining block, and
`valueDefinitionOffsets` seeks directly to its defining operation (or marks
a block parameter). Per-value/place use-block counts and sorted per-block
last-use inventories let an inliner decrement surviving use-block counts
when pruning blocks without decoding their operations. A use includes
instruction operands and terminator operands, including edge arguments;
mere transit through `liveIns`/`liveOuts` does not count. Place activation,
inherited state and end transitions count as place uses, even without an
explicit `PlaceLocal`. A definition with zero remaining uses is not itself
proof that its defining instruction can be discarded.
Effectful instructions must not disappear just because their result is
unused. Block and value identity stays stable during import; native register
allocation and target-dependent profitability are not wire facts.

The direct indexes do **not** yet summarize a block's precise effect classes,
call count, or normal successors without inspecting its terminator, nor
does a sorted final-use position encode every intermediate read. A method
effect synopsis and any finer per-block instruction inventory still need
defined bounds, cost and consistency rules; retained blocks are decoded
when imported. In particular, an inliner can prune a trivially dead arm
quickly, but cannot claim arbitrary conditional predicates are constant
or drop operations in a retained arm without examining their semantics.

Ordinary value liveness, place/ownership state and logical GC-root
continuity are distinct but related summaries. The producer should record
root identity and required owner/interior-reference dependencies at
indexed safepoints, including roots needed by cleanup or exceptional
continuations. An inliner can then remove a dead block's root obligations
without scanning its instructions, but must preserve roots at retained
safepoints and let its backend build the final physical GC maps. The
binary placement, compression, worst-case cost and audit rules for the
logical-root/safepoint inventory remain a stabilization decision; the
currently encoded Body examples **do not yet contain** GC roots.

### Checked add end to end (informative)

Consider `int32 AddThenThree(int32 x, int32 y) { return x + y + 3; }`
under the fail-fast policy. The example uses
the compact-linear encoding appropriate to this root-free, one-block
method. The [compact-form accounting](#compact-form-information-accounting)
summarizes what this representation stores versus reconstructs. The
field listing is a readable projection of the stored rows, not an
additional wire syntax. The byte sequence below encodes the Body section
only.
Throughout the examples, artifact-wide metadata context is separate from
Body-local type IDs and CFG/index facts; shape listings are projections,
and explicitly labeled byte listings contain complete Body sections.
Inline byte examples are instruction or operand fragments, not full
artifacts.

```text
Artifact-wide table rows (separate sections; not Body bytes):
    TypeSpec (0x0011):
        row[1]: Builtin(int32)
    MethodSig (0x0017):
        row[1]:
            resultTypeSpec: TypeSpec[1]
            receiverTypeSpec: 0
            paramFirst: 1
            paramCount: 2
            genericFirst: 0
            genericCount: 0
    Param (0x0018):
        row[1]:
            ownerMethodSig: MethodSig[1]
            ordinal: 0
            typeSpec: TypeSpec[1]
            mode: 0
        row[2]:
            ownerMethodSig: MethodSig[1]
            ordinal: 1
            typeSpec: TypeSpec[1]
            mode: 0
    MethodDef (0x0016):
        row[1]:
            signature: MethodSig[1]
            bodyRow: Body[1]
```

```text
Body (0x0020): // Body section; directory recordCount=1
    // Candidate compact linear; 31 encoded bytes
    rowOffsets: [0, 23] // 8-byte section index; bounds Body row 1
    row[1]: // 23-byte Body row
        bodyLayout: 0x04
        typeCount: 1
        typeMap:
            t1: TypeSpec[1] // fixed-u32 reference to the TypeSpec row
        operationCount: 3
        operations:
            r3 = CheckedBinary add r1, r2
            r4 = ConstSigned t1, 3
            r5 = CheckedBinary add r3, r4
        terminator: Return r5
```

```text
Body shape (inferred projection; not additional compact-linear bytes):
    counts:
        blockCount: 1
        valueCount: 5
        placeCount: 0
        regionCount: 0
    valueDescriptors:
        r1: t1 // x; by-value parameter
        r2: t1 // y; by-value parameter
        r3: t1 // first checked add
        r4: t1 // constant 3
        r5: t1 // second checked add, returned
    blocks:
        B1(r1 <- x, r2 <- y):
            r3 = CheckedBinary add r1, r2
            r4 = ConstSigned t1, 3
            r5 = CheckedBinary add r3, r4
            Return r5
```

```text
Body index (inferred projection; not stored in compact linear):
    valueDefStarts: [0, 5]
    definitions:
        r1, r2: entry parameter sentinels
        r3..r5: B1 operations 0..2
    useBlockCounts: [1, 1, 1, 1, 1] // r1..r5
    last uses:
        B1: [(r1,0),(r2,0),(r3,2),(r4,2),(r5,3)]
```

The header's `directoryOffset` locates the artifact directory. Each
directory entry stores its section kind, policy flags, record count,
artifact-relative section offset and section byte length; the directory
is not included in the Body section bytes shown below. Table row numbers
such as `TypeSpec[1]` and `MethodSig[1]` are one-based and artifact-wide;
they are not byte offsets, Body-local IDs or per-method subtables.
The displayed fields contain references to those separate rows, not
copies of their contents.
`MethodDef[1].signature` selects the separate MethodSig row, whose
`paramFirst` and `paramCount` select the two Param rows; its `bodyRow`
selects Body row 1. The Body section's offset index is separate from its
row: offsets `0,23` bound that one 23-byte row. TypeSpec and MethodSig
(with its Param rows) are structural data available to other
declarations; contracts, names and visibility belong to each MethodDef,
not to the Body. A distinct MethodDef may reuse the MethodSig, but
must own a distinct Body row even if its instruction bytes are identical.

The encoded Body section is 31 bytes: an 8-byte shared row index, a
7-byte row header and 16 instruction bytes. The sequence below encodes
the section index followed by Body row 1:

```text
00 00 00 00  17 00 00 00
04 01 01 00 00 00 03
15 03 01 01 02  02 04 01 03  15 05 01 03 04  83 05
```

Here MethodSig supplies
by-value `r1` and `r2` (source `x` and `y`), and the three ordered
operations define `r3`–`r5`. The descriptor and block shape above are
inferred from that signature and instruction stream, not encoded as
separate rows in the compact-linear Body. The expected `int32` context selects
checked primitive addition, not a numeric fallback or user witness.
A bounded scan derives their types,
definitions and last-use ordinals `(0,0,2,2,3)`, as well as the
checked failure order and return. The `typeMap` still supplies complete
`int32` identity. Those omitted records are either inapplicable here
or derivable, not permissions to discard an effect or weaken validation.
The tradeoff is no direct definition seek: a later uncached consumer
must scan again. This is a size recommendation, not a mandate for every
writer or a measured cold-import win.

On first-add overflow the later operations do not run; on second-add
overflow Return does not run. Each failure follows the module policy
at its operation site without a normal CFG edge or an artificial block.
`ConstSigned` types the literal `3`; its bytes are not an add immediate.
The `typeMap` entry is the fixed `u32` reference to TypeSpec row 1;
the `t1` operand is a Body-local ID, not a shortened metadata reference.
By-value parameters need no place; an addressable mutable local needs
`PlaceLocal` then `Load`, with
`Assign` through that place on success. See the
[local-place shape](#value-and-place-state-through-checked-arithmetic),
which is decode-only in this revision.

For example, if a checked add instead defines `r256`, it uses
`F0 01 15 00 01 01 01 00 02 00`: two-byte references widen
the destination and both operands. That instruction prefix does not set
the Body's structural widths; a real Body with 256 values also needs
a two-byte local width for its `valueCount`.
For a separate instruction fragment (not a complete Body section), let
Body-local `t1` map to an `int32` TypeSpec. `ConstSigned r3, t1, 200` uses
`F0 10 02 03 01 C8 00` (one-byte references, two-byte signed data); if
its result ID were 256, it would use
`F0 11 02 00 01 01 00 C8 00`. The prefix changes encoded widths, not
the opcode's semantic identity. Conformance should independently verify
the bytes, decoded records and audited CFG/types/states; a same-writer
round trip alone is insufficient.

### Adding a conditional to the sum

Now make the first add conditional:
`int32 MaybeAdd(bool flag, int32 x, int32 y) {
if (flag) { return x + y; } return x; }`.
The following separates artifact-wide metadata context from the Body's
local type IDs and logical block shape; it is not a byte listing:

```text
Artifact-wide table rows (separate sections; not Body bytes):
    TypeSpec (0x0011):
        row[1]: Builtin(bool)
        row[2]: Builtin(int32)
    MethodSig (0x0017):
        row[1]:
            resultTypeSpec: TypeSpec[2]
            receiverTypeSpec: 0
            paramFirst: 1
            paramCount: 3
            genericFirst: 0
            genericCount: 0
    Param (0x0018):
        row[1]:
            ownerMethodSig: MethodSig[1]
            ordinal: 0
            typeSpec: TypeSpec[1]
            mode: 0
        row[2]:
            ownerMethodSig: MethodSig[1]
            ordinal: 1
            typeSpec: TypeSpec[2]
            mode: 0
        row[3]:
            ownerMethodSig: MethodSig[1]
            ordinal: 2
            typeSpec: TypeSpec[2]
            mode: 0
    MethodDef (0x0016):
        row[1]:
            signature: MethodSig[1]
            bodyRow: Body[1]
```

```text
Body shape (logical projection; not wire syntax):
    counts:
        blockCount: 3
        valueCount: 4
        placeCount: 0
        regionCount: 0
        typeCount: 2
    typeMap:
        t1: TypeSpec[1]
        t2: TypeSpec[2]
    valueDescriptors:
        r1: t1 // flag
        r2: t2 // x
        r3: t2 // y
        r4: t2 // checked sum
    blocks:
        B1(r1 <- flag, r2 <- x, r3 <- y): // liveOut=[r2,r3]
            BranchIf r1, B2(), B3()
        B2(): // liveIn=[r2,r3]
            r4 = CheckedBinary add r2, r3
            Return r4
        B3(): // liveIn=[r2]
            Return r2
```

The Body has four by-value descriptors and three normal blocks; the add
remains an operation within its block. Its Body index projection (logical
fields encoded by compact indexed, not a byte listing) is:

```text
Body index (logical projection; not wire syntax):
valueDefStarts: [0, 3, 4, 4]
definition offsets:
    r1..r3: parameter sentinels in B1
    r4: B2 checked operation
useBlockCounts: [1, 2, 1, 1] // r1..r4
last uses:
    B1: [(r1,0)]
    B2: [(r2,0),(r3,0),(r4,1)]
    B3: [(r2,0)]
```

B1 uses `r1` at its terminator, B2 uses `r2` and `r3` at its add
and `r4` at Return, and B3 uses `r2` at Return. `r2` and `r3`
cross B1 without an operand use there. The use-block counts count B2
and B3 as distinct use blocks for `r2`, even though only one arm executes.

If the flag is known false after specialization, B2 and its checked add
are unreachable and may be pruned; if known true, B3 is unreachable.
An unknown flag requires both branches and the checked failure in B2
remains observable. An overflow in B2 fails at the add; it neither
returns from B3 nor requires a fourth normal block. This shape is
executable in the approved fail-fast scalar subset, though this
sketch does not require the general form. It introduces normal branching;
the next example introduces a merge argument, and then a loop backedge.

#### Compact indexed conditional sum

See [compact-form information accounting](#compact-form-information-accounting)
for the implicit-versus-encoded fields in this form.

This fixture uses the same artifact-wide metadata context and Body-local
type map shown above. Its exact bytes encode only the bounded
compact-indexed Body (0x0020) section: 90 bytes (8-byte offset index plus an
82-byte row). The three-block `MaybeAdd` row has
`valueDefStarts=[0,3,4,4]`: the first three definition offsets are
parameter sentinels and the fourth seeks B2's checked add at offset 12.
Its value descriptors' use-block counts are `(1,2,1,1)`.
Compact block offsets are `[0,18,37,46]`; terminator offsets are
`[12,17,7]`:

```text
00 00 00 00  52 00 00 00
08 03 04 00 02  01 00 00 00  02 00 00 00
01 01  02 02  02 01  02 01
00 03 04 04  FF FF FF 0C
00 12 25 2E  0C 11 07
1A 03 01 02 03 02 02 03 01 01 00 00  81 01 02 00 03 00
14 02 02 03 03 02 00 03 00 04 01 01  15 04 01 02 03  83 04
14 01 02 01 02 00 00  83 02
```

B2's one checked instruction remains ordered before its Return. B3
contains no operations; its `r2` Return is independent of B2's failure.
The direct B2 operation offset and all three branch/return offsets
make indexed import possible without scanning other blocks, subject
to the producer-trust and unknown-opcode policies noted above.

### Conditional merge and loop (informative shapes)

The following sketches use the same type-map, value-descriptor,
block-header and edge **rules**, with their own MethodSigs. They show
composition rather than a required source lowering or complete byte
listing; a writer must still encode exact block and terminator offsets.
Artifact-wide TypeSpec and MethodSig/Param context is shown separately
from Body-local IDs. Only listings explicitly labeled as encoded bytes
give a wire listing.

For `int32 TwiceSum(int32 x, int32 y) { int32 tmp = x + y;
return tmp + tmp; }`, its artifact-wide metadata context is:

```text
Artifact-wide table rows (separate sections; not Body bytes):
    TypeSpec (0x0011):
        row[1]: Builtin(int32)
    MethodSig (0x0017):
        row[1]:
            resultTypeSpec: TypeSpec[1]
            receiverTypeSpec: 0
            paramFirst: 1
            paramCount: 2
            genericFirst: 0
            genericCount: 0
    Param (0x0018):
        row[1]:
            ownerMethodSig: MethodSig[1]
            ordinal: 0
            typeSpec: TypeSpec[1]
            mode: 0
        row[2]:
            ownerMethodSig: MethodSig[1]
            ordinal: 1
            typeSpec: TypeSpec[1]
            mode: 0
    MethodDef (0x0016):
        row[1]:
            signature: MethodSig[1]
            bodyRow: Body[1]
```

Its Body shape is:

```text
Body shape (logical projection; no bytes assigned):
    counts:
        blockCount: 1
        valueCount: 4
        placeCount: 0
        regionCount: 0
        typeCount: 1
    typeMap:
        t1: TypeSpec[1] // int32
    valueDescriptors:
        r1..r4:
            typeId: t1
            kind: value
            useBlockCount: 1
    blocks:
        B1(r1 <- x, r2 <- y):
            r3 = CheckedBinary add r1, r2
            r4 = CheckedBinary add r3, r3
            Return r4
```

```text
Body index (logical projection; not wire syntax):
    valueDefStarts: [0, 4]
    definitions:
        r1, r2: parameter sentinels
        r3: B1 operation 0
        r4: B1 operation 1
    last uses:
        B1: [(r1,0),(r2,0),(r3,1),(r4,2)]
```

Reuse needs no duplicate descriptor or `Copy` operation for this Copy
scalar; a unique value could not be duplicated the same way. Ordinal 2
denotes Return. `r3` is read *twice* by operation 1 but appears once
in the per-block inventory, so its `useBlockCount` is 1, not 2.

For `int32 Choose(bool flag, int32 x, int32 y) { mut int32 chosen = x;
if (flag) { chosen = y; } return chosen; }`, the artifact-wide metadata
context is separate from the Body:

```text
Artifact-wide table rows (separate sections; not Body bytes):
    TypeSpec (0x0011):
        row[1]: Builtin(bool)
        row[2]: Builtin(int32)
    MethodSig (0x0017):
        row[1]:
            resultTypeSpec: TypeSpec[2]
            receiverTypeSpec: 0
            paramFirst: 1
            paramCount: 3
            genericFirst: 0
            genericCount: 0
    Param (0x0018):
        row[1]:
            ownerMethodSig: MethodSig[1]
            ordinal: 0
            typeSpec: TypeSpec[1]
            mode: 0
        row[2]:
            ownerMethodSig: MethodSig[1]
            ordinal: 1
            typeSpec: TypeSpec[2]
            mode: 0
        row[3]:
            ownerMethodSig: MethodSig[1]
            ordinal: 2
            typeSpec: TypeSpec[2]
            mode: 0
    MethodDef (0x0016):
        row[1]:
            signature: MethodSig[1]
            bodyRow: Body[1]
```

The Body-local type map and logical CFG are:

```text
Body shape (logical projection; not wire syntax):
    counts:
        blockCount: 4
        valueCount: 4
        placeCount: 0
        regionCount: 0
        typeCount: 2
    typeMap:
        t1: TypeSpec[1] // bool
        t2: TypeSpec[2] // int32
    valueDescriptors:
        r1: t1 // flag
        r2: t2 // x
        r3: t2 // y
        r4: t2 // selected value
    blocks: // all in region 0; no places or operations
        B1(r1 <- flag, r2 <- x, r3 <- y): // liveOut=[r2,r3]
            BranchIf r1, B2(), B3()
        B2(): // liveIn=[r3], liveOut=[r3]
            Branch B4(r3)
        B3(): // liveIn=[r2], liveOut=[r2]
            Branch B4(r2)
        B4(r4 <- selected value): // no liveIn
            Return r4
```

The two explicit incoming arguments to B4 have the same complete type,
but `r4` is defined once by B4's parameter, not by either predecessor.
The branch copies only Copy values; `liveOut` is the union of the
successors' live-ins and explicit edge arguments. Source variable names
and the apparent assignment are not stored in the Body. An SSA lowering
needs no place for the unaddressed source local.

The method-wide Body index projection (not encoded bytes) is:

```text
Body index (logical projection; not wire syntax):
valueDefStarts: [0, 3, 3, 3, 4]
definitions:
    r1..r3: parameter sentinels in B1
    r4: parameter sentinel in B4
valueDescriptors:
    r1:
        typeId: t1
        kind: value
        useBlockCount: 1
    r2..r4:
        typeId: t2
        kind: value
        useBlockCount: 1
```

The intervening empty definition intervals mean B2 and B3 define no
values. All four blocks have `operationCount=0`, so ordinal 0 means
their terminator:

| Block | `valueLastUses` | Boundary facts |
|---|---|---|
| B1 | `(r1,0)` | `r2` and `r3` are live-out transit values, not uses in B1 |
| B2 | `(r3,0)` | `r3` is live-in and supplied as B4's edge argument |
| B3 | `(r2,0)` | `r2` is live-in and supplied as B4's edge argument |
| B4 | `(r4,0)` | `r4` is defined at entry and used by Return |

#### Compact indexed conditional merge

See [compact-form information accounting](#compact-form-information-accounting)
for the implicit-versus-encoded fields in this form.

The four-block `Choose` example qualifies for compact indexed despite
having more than three blocks: it has only Copy values and normal
edges, and its direct definitions/uses and branch addresses remain
available. The local-width (one-byte) definition-offset sentinels,
block offsets `[0,18,31,44,53]` and terminator offsets
`[12,9,9,7]` directly index the definitions and terminators. Each block's
presence byte omits zero counts and lists; the four value descriptor
kinds are implicitly by-value. Its Body (0x0020) section is 100 bytes
(8-byte index, 92-byte row). The following
exact byte listing contains only that Body (0x0020) section; the artifact-wide
TypeSpec and signature context is separate:

```text
00 00 00 00  5C 00 00 00
08 04 04 00 02  01 00 00 00  02 00 00 00
01 01  02 01  02 01  02 01
00 03 03 03 04  FF FF FF FF
00 12 1F 2C 35  0C 09 09 07
1A 03 01 02 03 02 02 03 01 01 00 00  81 01 02 00 03 00
1C 01 03 01 03 01 03 00 00  80 04 01 03
1C 01 02 01 02 01 02 00 00  80 04 01 02
12 01 04 01 04 00 00  83 04
```

The indexed compact header still says that B1 uses `r1`, B2 uses
`r3`, B3 uses `r2`, and B4 uses `r4` at their terminators. B1's
`r2`/`r3` live-outs remain transit-only. An inliner may follow the
four terminator offsets and decrement B3's `r2` use-block count when
discarding that arm. The section's eight-byte row index is not
compressed by this Body layout.

B1's terminator selects B2 or B3, each with zero edge arguments.
B2 and B3 each send one typed argument to B4; the sent `r3` or `r2`
is an operand *use* at the terminator, while B4's `r4` is a new
parameter definition. The row's use-block counts do not count B1's
transit-only live-outs as reads.

If an inliner knows `flag` is true, B1's indexed terminator identifies
B2 as the only normal successor. B3 can be removed; its use inventory
lists `r2`, so removing B3 decrements `r2`'s remaining use-block count
to zero without decoding B3's operations. B1's resulting live-out no
longer includes `r2`. B2 still supplies `r3` to B4, whose `r4`
parameter remains a valid merge definition unless the inliner chooses
to substitute it. This is a Copy-scalar example; dropping a unique
argument or cleanup obligation requires its separate ownership/exit
contract, not just a zero ordinary-use count.

To repeat the preceding addition, consider
`int32 RepeatAdd(int32 x, int32 y, int32 n) {
mut int32 sum = x; mut int32 i = 0;
while (i < n) { sum = sum + y; i = i + 1; } return sum; }`.
The artifact-wide metadata context is:

```text
Artifact-wide table rows (separate sections; not Body bytes):
    TypeSpec (0x0011):
        row[1]: Builtin(int32)
        row[2]: Builtin(bool)
    MethodSig (0x0017):
        row[1]:
            resultTypeSpec: TypeSpec[1]
            receiverTypeSpec: 0
            paramFirst: 1
            paramCount: 3
            genericFirst: 0
            genericCount: 0
    Param (0x0018):
        row[1]:
            ownerMethodSig: MethodSig[1]
            ordinal: 0
            typeSpec: TypeSpec[1]
            mode: 0
        row[2]:
            ownerMethodSig: MethodSig[1]
            ordinal: 1
            typeSpec: TypeSpec[1]
            mode: 0
        row[3]:
            ownerMethodSig: MethodSig[1]
            ordinal: 2
            typeSpec: TypeSpec[1]
            mode: 0
    MethodDef (0x0016):
        row[1]:
            signature: MethodSig[1]
            bodyRow: Body[1]
```

The Body shape is a logical projection, not an assigned byte listing:

```text
Body shape (logical projection; byte offsets are not assigned):
    counts:
        blockCount: 4
        valueCount: 11
        placeCount: 0
        regionCount: 1
        typeCount: 2
    typeMap:
        t1: TypeSpec[1] // int32
        t2: TypeSpec[2] // bool
    valueDescriptors: // all kind: value
        r1..r6: t1
        r7: t2
        r8..r11: t1
    region 1:
        kind: loop
        parent: 0
        entry: B2
        exit: B4
        cleanup: 0
    region membership:
        B1, B4: region 0
        B2, B3: region 1
    blocks:
        B1(r1 <- x, r2 <- y, r3 <- n): // liveOut=[r1,r2,r3,r4]
            r4 = ConstSigned t1, 0
            Branch B2(r1,r4)
        B2(r5 <- sum, r6 <- i): // liveIn=[r2,r3], liveOut=[r2,r3,r5,r6]
            r7 = Binary Less r6, r3 // t2 bool
            BranchIf r7, B3(), B4(r5)
        B3(): // liveIn=[r2,r3,r5,r6], liveOut=[r2,r3,r8,r10]
            r8 = CheckedBinary add r5, r2
            r9 = ConstSigned t1, 1
            r10 = CheckedBinary add r6, r9
            Branch B2(r8,r10)
        B4(r11 <- final sum): // no liveIn; outside loop
            Return r11
```

The loop's changing `sum` and `i` are block parameters, not addressable
places; their source names are not stored in the Body.
The initial edge supplies `(x,0)` and the backedge supplies the next
`(sum,i)` to the same two header parameters. The false edge supplies
the current sum to B4. `r2` and `r3` are dominating values, not
additional header parameters: `r2` is read in B3 and `r3` in B2,
and both transit other blocks so they remain available on the backedge.
In particular, B3's `liveIn` and `liveOut` include `r3` although no
B3 instruction reads it. Both additions may fail at their operation
sites under the fail-fast policy. The region and its backedge do not
create implicit exceptional successors.

The Body index projection (logical fields of the general form, not
assigned bytes) is:

```text
Body index (logical projection; not wire syntax):
    useBlockCounts:
        r1..r4: 1
        r5..r6: 2
        r7..r11: 1
    valueDefStarts: [0, 4, 7, 10, 11]
    definitions:
        r1..r3, r5..r6, r11: parameter sentinels
        r4: B1 operation 0
        r7: B2 operation 0
        r8..r10: B3 operations 0..2
```

These descriptor counts are `useBlockCount`, **not** the number of
traversals of a loop edge. The actual row stores byte offsets, not the
operation labels above; this sketch does not assign bytes.

| Block | `operationCount` | `valueLastUses` | Important distinction |
|---|---:|---|---|
| B1 | 1 | `(r1,1),(r4,1)` | `r2` and `r3` merely transit B1 |
| B2 | 1 | `(r3,0),(r5,1),(r6,0),(r7,1)` | `r5` is supplied on the false edge; `r6` is read by the comparison |
| B3 | 3 | `(r2,0),(r5,0),(r6,2),(r8,3),(r9,2),(r10,3)` | `r3` transits the backedge without a B3 operand use |
| B4 | 0 | `(r11,0)` | The exit parameter is a new definition read by Return |

`r5` and `r6` each have two use blocks; their backedge replacements
`r8` and `r10` each have one. Pruning B3 after proving the condition
false removes its uses, but a consumer still must respect the checked
operations whenever B3 is reachable. There are no places, so every
block has `placeUseCount=0`. Ordinary last uses alone do not prove
that a managed root or cleanup obligation may expire.

The following are operand *kinds*, not additional on-wire tags. `ref`
means the selected reference width, `count` the selected count width,
`data` the selected data width, and `u8` exactly one byte:

| Operand notation | Decode | Namespace and zero rule |
|---|---|---|
| `r`, `rNew`, `rPlace` | `ref` | Body-local value ID `1..valueCount`; `rNew` defines the next ID, `r` uses one, and `rPlace` must denote a place-valued ID |
| `r?` | `ref` | Value ID or zero only when the declared result is void |
| `rNew?` | `ref` | New value ID, or zero iff the selected callable returns void; zero defines nothing |
| `p` | `ref` | Body-local place descriptor ID `1..placeCount` |
| `t` | `ref` | Body-local type ID `1..typeCount`; `typeMap[t - 1]` is currently a fixed `u32` typed reference to a TypeSpec row, interpreted in this body's module context; an inline built-in word needs the matching metadata revision |
| `m` | `ref`, then `u32 DefId` only when `m=0` | Nonzero: one-based MethodDef row within the body module's MethodDef range, resolved by checked `methodDefStart + m - 1`; retained imports use its bound expected MethodDef. Zero: resolved direct call to an authored MethodDef in another packed module, with an exact little-endian MethodDef `DefId` following the selected-width zero. Invalid in a single-module artifact |
| `fieldDefRow` | fixed `u32` | Typed FieldDef reference, high byte zero, row `1..FieldDef.recordCount` owned by the selected type; a trusted merger may bind it directly to a field of a packed producer's resolved TypeDef |
| `targetBlock`, `regionId` | `ref` | Body-local block or region ID; only explicitly specified region roots permit zero |
| `args` | `count`, then `ref[count]` | Ordered value IDs; zero is not an argument |
| `edge` | `ref targetBlock`, then `args` | Target's typed parameter list consumes the arguments |
| `data(bits)`, `signedData(value)` | `data` | Unsigned bit pattern or signed two's-complement literal, respectively |
| `kind`, `policy`, `reason` | `u8` | Numeric discriminant from the assigned tables below; zero and unassigned values reject |

At one-byte reference width, a resolved direct call to packed MethodDef
row 1 encodes its `m` operand as `00 01 00 00 16`: one zero reference
followed by the little-endian `DefId=0x1600_0001`. At two- or four-byte
reference width, the selected-width zero precedes the same four-byte
token. This conditional operand has a known length; it does not make
unknown opcodes skippable.

`rNew` and an edge target are the same-width *class*, but different ID
namespaces. Prefixes affect width, not namespace. A decoder selects a
registered opcode, reads its listed operands **left to right**, and uses
each decoded count to read the following repeated items in the specified
order. For `Switch`, this is selector, case count, that many `(pattern,
edge)` pairs, then default edge. For `Call`, it is result ID, method
row, argument count, then argument IDs. Validate each read against its
block extent before advancing; a count is not an allocation size until
its minimum encoded byte requirement fits that extent. After exactly
`operationCount` operations, the stored terminator offset must point to
the beginning of exactly one registered terminator that ends the block.
The last condition is a writer obligation and an optional semantic audit;
even a trusted decoder bounds its direct terminator access. The reader
never scans for marker bytes or guesses an unregistered opcode's length.

The approved metadata design first bounds the artifact and Body-section row.
Body decoding then uses checked arithmetic and bounds every field, index,
operand and repeated item before lookup, allocation or traversal; a trusted
input does not waive these checks. A consumer may omit body semantic audit
only under its trust policy; this does not waive metadata reference or
contract checks. Malformed forms reject decoding. Assigned but
non-executable forms may be structurally inspected, but an executable
consumer rejects them before execution. Diagnostic codes, error
ordering/recovery and numeric parser or audit budgets are not assigned here.

The [introductory checked-add example](#checked-add-end-to-end-informative)
traces compact-linear Body storage and its unprefixed instruction
operands from source through structure to bytes. The adjacent wider
operand variants show that an instruction prefix and the enclosing
`bodyLayout` select widths independently.

## Body record, direct block index and regions

A Body table row is the bytes between adjacent Body-section offsets, with
no further row-length prefix. The following table gives the **general
form** (`bodyLayout` form bits zero); compact forms have the common
layout prefix but the distinct field sequences specified
[above](#compact-body-forms). Its fields, in order, are:

| Field | Encoding | Constraint |
|---|---|---|
| `bodyLayout` | `u8` | Bits 0–1 Body-local width; bits 2–3 zero for this form, bits 4–7 zero for every form |
| `blockCount`, `valueCount`, `placeCount`, `regionCount`, `typeCount` | five local-width unsigned integers | Body-local counts; blockCount is positive; typeCount is zero iff no local type ID is used |
| `typeMap` | `u32[typeCount]` little-endian | Distinct nonzero typed TypeSpec references, indexed by one-based Body-local type ID |
| `places` | `placeCount` entries | local-width `typeId`, local-width `regionId`, `u8 flags`, local-width `useBlockCount` each |
| `regions` | `regionCount` entries | Described below, IDs `1..regionCount` |
| `valueDescriptors` | `valueCount` entries | local-width `typeId`, `u8 kind`, local-width `useBlockCount` each, indexed by `valueId - 1` |
| `valueDefStarts` | local-width `[blockCount + 1]` | Zero-based descriptor indexes: starts at 0, nondecreasing by the number of definitions in each block, ends at `valueCount`; blocks without definitions may have empty intervals |
| `valueDefinitionOffsets` | fixed `u32[valueCount]` little-endian | Relative to the defining block's start; `0xFFFF_FFFF` for a block parameter, otherwise points directly to its defining operation opcode or width prefix |
| `blockOffsets` | `u32[blockCount + 1]` little-endian | Relative to the following block-data start |
| `terminatorOffsets` | `u32[blockCount]` little-endian | Each relative to its own block start; points directly to its terminator opcode or width prefix |
| `blockData` | `blockOffsets[last]` bytes | One block per consecutive offset extent |

The upfront value/place counts and descriptors are a pre-scan inventory of
logical value IDs and addressable places, with their Body types and place
metadata; they do not allocate native stack slots or assign JIT locations.
A backend chooses physical locations during lowering.

Body-layout width codes 0, 1 and 2 mean local widths of 1, 2 and 4 bytes;
code 3 rejects. In the general form this width sizes all five counts,
structural block/region/value/
place/type IDs, block parameters, place-state and operation counts, region
fields, per-descriptor use-block counts, definition starts, last-use
ordinals and place-region references. Choose the smallest width that
represents every field in this class throughout the **whole body**.
Type-map entries, definition offsets and both block
offset arrays remain fixed `u32` typed references or offsets. Each
instruction independently selects its
Body-local operand width via its prefix; FieldDef and direct-call DefId
operands remain fixed `u32`. Under the approved indexed-only rule,
bound `typeCount` by the TypeSpec table count and the remaining row
bytes before reading its four-byte entries. If inline built-ins are
adopted, use the Body-local ID range and remaining bytes as the bound
instead; a Body can have built-in types with no TypeSpec rows. Every
entry must be distinct and resolve to a complete type valid in the
referencing module's context; duplicates reject. Writers order entries by first use
across descriptors, then instruction streams, without unused entries;
a strict canonicality audit may check ordering and usage. Readers bound
every field and validate map entries
before dereferencing local type IDs; out-of-range IDs reject. Writers
must use minimal widths. A strict canonicality audit may verify them,
but load-time treatment of nonminimal `bodyLayout` values is not
specified. Unsupported codes, reserved bits and truncated fields reject.

For value ID `r`, locate its descriptor at `r - 1` and the unique
`valueDefStarts` interval containing that index; the interval identifies
its defining block without decoding preceding blocks. The first
`parameterCount` IDs of a block's interval are its listed block parameters
and have sentinel definition offsets. Each later ID has an in-range offset
to the result-producing operation in that block. Multiple results from
one operation may share its offset but have distinct IDs; the verifier
matches operand/result order and the descriptor. Every definition is
accounted for once, and no offset may point into a header, operand or
terminator. A direct reader bounds the target before decoding it;
the writer guarantees the index agrees with the instruction stream.
`useBlockCount` counts distinct blocks with a corresponding use entry
below, not occurrences or transit-only liveness. It is at most
`blockCount`. A semantic audit checks all definition and use indexes,
including zero-use values, against the decoded operations and edges.
An untrusted executable reader must perform that audit before relying
on the indexes to discard code; only an explicit producer trust policy
can permit their use without the semantic audit. Bounded offsets alone
do not make asserted deadness true.

Block offsets start at 0, increase strictly, and end at the exact remaining
body length; empty blocks are invalid. Block data is under 4 GiB, though
read budgets may be lower. Direct indexes do not replace bounds checks:
each block must consume its full extent. Reject invalid indexes, arithmetic
overflow, overlap, trailing bytes and Body rows without compatible local
MethodDef references. A terminator
offset follows exactly the counted operations and points to one complete
terminator ending the block. Trusted readers may seek there after bounded
decoding; semantic audits compare it with the operation stream. MethodDefs
select Body rows through `bodyRow`; relocating metadata requires rewriting
references and revalidating every affected use.

Places are numbered `1..placeCount`. Their `typeId` resolves through the
type map to the referent's complete TypeSpec. Flags `0x01`, `0x02` and `0x04` mean
rebindable, unique storage and initially vacant (including `out`),
respectively; other bits reject. The complete TypeSpec determines referent
access and representation; flags cannot grant `write` through a readonly
handle. Non-value parameters map to places in parameter order, followed by
local places; by-value parameters are entry-block values. Place descriptors
name storage, not an exposed address or native stack slot.

Descriptor kind `0x00` is a by-value result of its mapped complete TypeSpec;
`0x01` is a place handle whose mapped TypeSpec is its **referent**, not a loaded
value or native pointer. All other descriptor kinds reject. A handle's
access, owner and region come from its defining
place operation; the descriptor grants no authority. The array indexes
types directly by value ID minus one. Writers must match descriptors to
parameters and definitions; semantic audits may verify them. `void` has no
descriptor. Ordinary `r` operands require kind 0; `rPlace`/`rBasePlace`
require kind 1. `PlaceLocal`, `PlaceField` and reserved `CheckedIndex`
produce kind 1; `Load` defines a distinct kind-0 value. Copying a Copy
referent does not duplicate place authority or extend its loan. Future
parameter modes need explicit reference/loan operands, not a reinterpretation
of kind 0.

A region entry is `u8 kind, local(parentRegion), local(entryBlock),
local(exitBlock), local(cleanupBlock)`. Parent is 0 (function) or a
preceding region ID, so parent chains are acyclic. Kind `0x01` is a loop
with a nonzero entry header, one designated exit outside its subtree and
zero cleanupBlock. Kind `0x02`
lexical scope is reserved until lifetime and exit rules are assigned.
Kinds `0x03` handler, `0x04` finally, `0x05` loan,
and `0x07` root reserve their identities but are **not executable** until
exit/cleanup/exception rules and feature requirements are assigned.

Kind `0x06` is an indexed-Body lexical unsafe region in the bounded fail-fast
profile. Its Body uses root-free Copy-scalar shapes. Caller-unsafe
declarations are limited to top-level functions, and calls are limited to
local definitions. The region requires a preceding parent region ID, an entry
block within its subtree, a designated exit outside its subtree but within its
parent, and a zero cleanup block.
Every incoming control-flow edge from outside the region targets its entry;
every normal edge leaving the region targets its designated exit. Nested
regions are permitted, and loop backedges remain within their loop region.
Returns and fail-fast terminators may end a path inside the region; other
transfers leaving it use the designated exit. A call's caller-unsafe
authorization is reconstructed only from validated enclosing-region
membership. Writers and readers audit every Body, including uncalled
definitions, and reject malformed, bypassed or leaking scope structures.
Compact-linear Bodies, imported calls, `unsafe(expr)`, unsafe members, cleanup,
exception unwind and lifetime authority are not supported by this region.
Kind `0x06` does not change `bodyLayout` or prove borrow safety or release an
owner.

A block extent contains these fields in order:

| Field | Encoding | Meaning |
|---|---|---|
| `regionId` | `local` | Innermost containing region; zero for the function root |
| `parameterCount`, `parameters` | `local`, then `local valueId[parameterCount]` | Values defined at this block's entry, in incoming-edge order; types come from `valueDescriptors` |
| `liveInCount`, `liveIns` | `local`, then `local valueId[liveInCount]` | Prior definitions live at entry, excluding block parameters |
| `liveOutCount`, `liveOuts` | `local`, then `local valueId[liveOutCount]` | Definitions available at block exit and needed on at least one outgoing normal edge, including values defined in this block |
| `inheritedPlaceCount`, `inheritedPlaces` | `local`, then `(local placeId, u8 state)[inheritedPlaceCount]` | Places continuing from predecessors; state 0 vacant, 1 initialized |
| `startPlaceCount`, `startPlaces` | `local`, then `local placeId[startPlaceCount]` | Places becoming active at entry with the descriptor's initial state |
| `endPlaceCount`, `endPlaces` | `local`, then `local placeId[endPlaceCount]` | Places ending after this block's operations, before normal successor transfer or return |
| `valueUseCount`, `valueLastUses` | `local`, then `(local valueId, local lastUseOrdinal)[valueUseCount]` | Distinct used values in strictly increasing ID order; last occurrence in this block |
| `placeUseCount`, `placeLastUses` | `local`, then `(local placeId, local lastUseOrdinal)[placeUseCount]` | Distinct places mentioned in the block's state lists or operations, in increasing ID order |
| `operationCount`, `operations` | `local`, then `operationCount` coded instructions | Definitions and state transitions in source order |
| `terminator` | One coded terminator | Explicit normal successor/return/failure; no fall-through |

Block ID is its one-based index in `blockOffsets`; region 0 is the function
root, and entry block ID is 1. Its by-value parameters match MethodSig
order and types. Receivers and non-value parameters use their declared
place/receiver contracts, not fabricated values. Other blocks match all
incoming-edge arities and types. Entry starts each non-value parameter
place in the state selected by its mode and descriptor; it has no
inherited places.

`liveIns` and `liveOuts` are strictly increasing IDs; `liveIns` exclude
block parameters. Each ID must identify a value with a type from its
definition.
`liveIns` refer to prior definitions available on every incoming normal
edge; `liveOuts` are the union of successor `liveIns` and explicit edge
arguments. Returns and failures have no live-outs. Account for every
cross-block use and transit in these lists; definitions in the block need
no `liveIn`.
Optional audit checks definitions, transfers, dominance and exact lists.
This permits ordinary dominating values while typing merge values as
block parameters; checked operations define results within their block.
These block-boundary lists are not a per-instruction live-range table and
cannot determine when a reference ceases to be a logical GC root. The
per-block last-use inventories give ordinary operand last uses without
making an SSA last use into a GC root end. The first operation has ordinal
0, the terminator has ordinal `operationCount`, and an end-place transition
also has ordinal `operationCount`. Every listed last-use ordinal must
refer to an actual use of that value or place at that position; absence
means no use in this block. The maximum ordinal must fit the Body-local
width, and the total number of distinct blocks listing an ID equals
its descriptor's `useBlockCount`. Bound `valueUseCount` and
`placeUseCount` by the respective descriptor counts before allocating
or traversing entries. Root
lifetime can extend beyond the last ordinary use, especially for owning
storage, interior references, cleanup and exceptional continuations.

Place lists are strictly increasing, duplicate-free; inherited and starting
places are disjoint. Each place has one declaration and lexical start block,
which may execute repeatedly in a loop. Only active places may be addressed.
For each normal edge, apply operations and end-place transitions to the
inherited and started sets; the result must equal the successor's inherited
set. Successor starts are not inherited, and all predecessors must agree.
An initialized place may end only if its type is Copy and needs no cleanup;
otherwise explicitly `Move` or `Drop` it. `Return` requires an empty set.
These records expose local state at block boundaries. Failure still needs
separate cleanup/root rules, including the exact failure site within a block;
place metadata cannot create an unwind edge or enable unsupported operations.

Value IDs are gapless unique definitions in `1..valueCount`, assigned in
block/operation order: block parameters first, then result-producing
operations. Checked results are ordinary operation definitions. Direct
cross-block uses require dominance and liveness on every crossed normal edge.
Distinct incoming definitions become typed parameters with explicit edge
arguments; edge arguments are live at the terminator. These facts do not
certify Copy, cleanup or aliasing; trusted consumers may use them, while an
opt-in verifier may recompute them. Conditional edges are mutually exclusive,
but duplicating a live unique value on one edge, using it after move or
implicitly copying a non-Copy value rejects.

Executable regions are loops only. A block belongs to its innermost region
and its ancestors; each loop header is assigned to that loop and dominates
its members. Outside edges enter only at the header, internal backedges
target it, and leaving edges target its designated exit in the parent or an
ancestor. Every block is entry-reachable, every loop has a reachable
backedge, and every CFG cycle lies in a valid loop; nested loops admit entry
only at their header. Multi-exit loops and multi-level breaks have no
encoding. An audit checks dominance, reachability and region nesting within
its work budget; trusted consumers may use recorded regions, not as a
cryptographic certificate. Irreducible cycles are unsupported.

### Logical decoder records

An implementation can materialize these records directly from the
preceding fields. Names are descriptive, not a mandated host-language ABI:

| Record | Required data | Index/identity invariant |
|---|---|---|
| `BodyIndex` | Body layout, five counts, fixed-`u32` TypeSpec map, place/value descriptors, value-definition starts/offsets, block/terminator offsets | Local IDs and offsets remain relative to this one body; MethodDefs select its artifact-wide row via `bodyRow` |
| `ValueDescriptor` | Body-local type ID and value/place kind | Entry `i` describes `r(i+1)`; resolve its type through the validated map without decoding the defining operation |
| `BlockLocation` | Block-data start + `blockOffsets[i-1]`, next block offset, relative terminator offset | Entry `i-1` addresses block ID `i`; terminator lies inside this block's extent |
| `BlockHeader` | Region, parameter IDs, live-in/out IDs, inherited/starting/ending place states, sorted last uses, operation count | Parameters define values; live sets refer to cross-block transfers, not native registers |
| `Instruction` | Registered opcode, three resolved widths and the opcode-specific typed operands | The wide prefix changes the byte widths only; do not store raw bytes as if they were an addressing mode |
| `Terminator` | Registered opcode and its typed edge/return/failure payload | Directly indexed; each normal edge targets a block ID and transmits the listed arguments |

Bound the Body row, descriptors and indexes before lookup. Materialize
the same logical records for all three forms, deriving the omitted
one-block facts on a compact-linear scan and expanding compact-indexed
presence lists and local offsets with checked arithmetic. Decode each
operation by its registered schema when importing its block; semantic audit
compares the stream with the terminator index and checks type/flow. A
consumer explicitly trusting the producer's semantic indexes may start at
an indexed terminator and follow edges without decoding unrelated blocks.
The format does not prescribe an object graph, register assignment,
optimization tier or cache.

## Type and definition model for operands

`t` indexes a Body-local type-map entry holding a fixed `u32` reference
to a complete row in the metadata design's
[TypeSpec table](il-metadata.md#metadata-logical-tables-and-row-semantics),
not to a byte-sized primitive code embedded in an instruction. Entry
types and instruction type operands use the same map; their IDs have
Body-local scope, while the referenced TypeSpec retains its approved
artifact-wide identity and qualification.
The narrow executable profile uses the metadata registry codes `0x00 void`,
`0x01 bool`, `0x03 char16`, `0x08 int32`, `0x0D uint32`, `0x09 int64`,
`0x0E uint64`. Their logical bit widths are respectively none, 1, 16,
32, 32, 64 and 64; `bool` has only values 0 and 1. Other assigned atomic
types can be decoded as metadata but are not executable in this profile.
`void` cannot define an `r` or be a block parameter. The four integer codes alone
participate in integer arithmetic; `char16` is not implicitly an integer.

| TypeSpec form | Body-facing interpretation |
|---|---|
| `0x01` built-in | Assigned atomic type has its exact width; high-bit family codes with an inline atomic argument are structurally decoded but are not executable in this subset |
| `0x02` local nominal, `0x03` imported nominal | Resolve the referenced TypeDef or expected import and its complete layout/contracts; equal representation does not make two nominal types interchangeable |
| `0x04` formal, `0x05` constructed | Preserve the formal or its ordered nominal type substitutions; a body cannot assume numeric operations on an unconstrained formal |
| `0x06` optional | Separate presence on the fully qualified inner type; `None` cannot fabricate its payload |
| `0x07..0x0B` ref/ptr/box/val/own, `0x0C` indexed built-in | Indexed family argument when inline atomic encoding does not apply; preserve distinct representation/capability and reject unsupported executable use |

Qualifiers remain part of type equality across edges. Each `r` has one
indexed descriptor; a block parameter's type comes from its ID, and every
operation result must match its descriptor. Place descriptors supply
referent TypeSpecs; field results resolve FieldDefs against the owner's
nominal type. Entry values derive from MethodSig, with declared and inferred
forms agreeing. Declared types and call contracts determine Copy, ownership
and effects. Nominal operators remain selected MethodDefs, not primitive
`Binary`/`CheckedBinary` kinds. Transparent layout changes ABI
classification, not numeric identity.

| Definition form | Type/state of the new value |
|---|---|
| `ConstBits`, `ConstSigned`, `None`, `Aggregate` | Exact referenced `t`; the operation's restrictions still apply |
| `Copy`, `Load`, `Move` | Complete source or place-referent type; a place handle itself is not copied by `Load` |
| `Unary`, integer/bitwise/wrapping `Binary` | Operand's complete type, subject to the kind's type rule |
| Comparison `Binary`, `IsPresent` | A TypeSpec of primitive `bool`, not an untyped machine flag |
| `Convert` | Exact `tDest`, independent of source representation width |
| `Select` | Identical complete type of its two eager alternatives |
| `MakeOptional` | Exact `tOptional`, whose inner type equals the payload's complete type |
| `PlaceLocal`, `PlaceField` | Place-valued ID with referent, access, owner and region from the place/field declaration |
| `Call` | Selected MethodSig result type, or no definition when `rNew?=0` |
| `Extract` | Selected field's complete type, requiring a Copy-by-value field |
| `CheckedBinary`, `CheckedNegate`, `CheckedDivRem` | Result ID(s) defined in the same block on success; their failure is an ordered effect, not a normal edge |
| `CheckedConvert` | Exact `tDest` at its result ID on success |

The scalar executable profile accepts supported primitives and optional
forms, but rejects place-valued results. Other rows define decode
relationships only; they do not make future profiles executable.

## Operation encoding and normalized effects

Operands follow the notation above. These tables assign decode shapes;
the executable subset is narrower, as specified below. Assigned but
unsupported shapes may be inspected, not emitted as executable `(1, 0)`
bodies.

| Opcode | Operation; operands in byte order | Semantics and writer/audit requirement |
|---:|---|---|
| `01` | `ConstBits rNew, t, data(bits)` | Canonical zero-extended bit pattern for supported scalar; `bool` 0/1, `char16` 16 bits, signed integers canonical after zero-extension to their declared bit width. |
| `02` | `ConstSigned rNew, t, signedData(value)` | Sign-extended signed integer literal; range checked in `t`, no default numeric type. |
| `03` | `Copy rNew, r` | Source's complete type has checked `Copy`; does not duplicate a unique obligation. |
| `04` | `Unary rNew, u8(kind), r` | Kind 1 bool NOT, 2 integer bitwise NOT, 3 integer unary plus. Checked negation has its own operation. |
| `05` | `Binary rNew, u8(kind), rLeft, rRight` | Kinds 1/2/3 bitwise AND/OR/XOR, 4/5 equality/inequality, 6/7/8/9 ordered `<`/`<=`/`>`/`>=`, 10/11/12 explicit wrapping add/subtract/multiply. Only built-in scalar operations; no invented user witness. |
| `06` | `Convert rNew, tDest, u8(policy), rSource` | Policy 1 integer wrapping, 2 integer saturation, 3 checked bit-preserving reinterpretation of eligible equal-size representations. Checked range conversion has its own operation. |
| `07` | `Select rNew, rCondition, rTrue, rFalse` | Both source values already exist; eager selection of equal complete Copy types, never conditional evaluation or owner transfer. |
| `08` | `MakeOptional rNew, tOptional, rValue` | Wrap one fully initialized value; payload type and qualification match. |
| `09` | `None rNew, tOptional` | Absent optional; no fabricated payload or extra optional layer. |
| `0A` | `IsPresent rNew, rOptional` | Boolean presence test, not a value load. |
| `0B` | `PlaceLocal rNew, p` | Defines a place-valued result scoped to the place's region. |
| `0C` | `PlaceField rNew, rBasePlace, fieldDefRow` | Checked field identity, access, layout and retained owner lifetime; produces a place, not a field copy. |
| `0D` | `Load rNew, rPlace` | Reads initialized Copy value; a unique value instead requires Move. |
| `0E` | `Initialize rPlace, rValue` | Writes only vacant authorized storage, consumes a unique source; fully publishes or fails before publication. |
| `0F` | `Assign rPlace, rValue` | Requires writable initialized storage; old owning value needs its explicit cleanup path before replacement. |
| `10` | `Move rNew, rPlace` | Consumes an initialized unique place into a unique value and leaves it vacant. |
| `11` | `Drop rPlace` | Drops one initialized unique place using its declared NoThrow cleanup; leaves it vacant. |
| `12` | `Call rNew?, m, args` | Exact selected callable; accepts matching signature, modes and contracts, regardless of whether it declares `NoThrow`. `rNew=0` iff result is void. A failure follows the selected runtime policy or an authored handler when its exceptional contract is supported; it is not a normal CFG edge. |
| `13` | `Aggregate rNew, t, args` | Fully constructs ordered fields in declaration order, with checked Copy/unique transfer and validity; no implicit zero-fill or padding observation. |
| `14` | `Extract rNew, rAggregate, fieldDefRow` | Copy-only by-value field; unique fields require an authorized place and Move. |
| `15` | `CheckedBinary rNew, u8(kind), rLeft, rRight` | Kinds 1..5 checked add/subtract/multiply/divide/remainder; 6/7 checked-count left/right shift. Failure at this operation follows the selected policy; no result is defined on failure. |
| `16` | `CheckedConvert rNew, tDest, rSource` | Checked numeric range conversion; `rNew` has `tDest` on success. |
| `17` | `CheckedNegate rNew, rOperand` | Checked signed negation; fails at the minimum. |
| `18` | `CheckedDivRem rQuotient, rRemainder, rLeft, rRight` | Single checked failure decision; two distinct same-typed results on success. |
| `19` | Reserved (reject) | No separate throwing-call opcode or effect inferred from the call site. |
| `1A` | `CheckedIndex rNew, rBase, rIndex` | Reserved: checked indexed place result; backing and authority rules are not executable yet. |
| `1B` | `CheckedRange rNew, rBase, rStart, rCount` | Reserved: checked bounded view; backing, access and loan rules are not executable yet. |
| `1C` | `CheckedReadIndex rNew, rBase, rIndex` | Reserved: checked Copy element read without an intermediate place. |

The `u8(kind)` following `Unary`, `Binary` or `CheckedBinary` is one
of these **exact** assignments. Every other value, including zero,
rejects for that opcode. Both `Binary` and `CheckedBinary` produce an
`rNew` in the same block:

| Opcode | Kind byte | Name | Valid sources | Result and failure |
|---:|---:|---|---|---|
| `04` | `01` | BoolNot | `bool` | `bool`, logical complement |
| `04` | `02` | BitNot | One integer primitive | Same type, width-limited bitwise complement |
| `04` | `03` | Plus | One integer primitive | Same value and type |
| `05` | `01` | BitAnd | Equal integer types | Same type, width-limited AND |
| `05` | `02` | BitOr | Equal integer types | Same type, width-limited OR |
| `05` | `03` | BitXor | Equal integer types | Same type, width-limited XOR |
| `05` | `04` | Equal | Equal primitive scalar types | `bool`, exact value equality |
| `05` | `05` | NotEqual | Equal primitive scalar types | `bool`, negated exact value equality |
| `05` | `06` | LessThan | Equal integer types | `bool`, order in declared signedness |
| `05` | `07` | LessThanOrEqual | Equal integer types | `bool`, order in declared signedness |
| `05` | `08` | GreaterThan | Equal integer types | `bool`, order in declared signedness |
| `05` | `09` | GreaterThanOrEqual | Equal integer types | `bool`, order in declared signedness |
| `05` | `0A` | WrappingAdd | Equal integer types | Same type, sum modulo its bit width |
| `05` | `0B` | WrappingSubtract | Equal integer types | Same type, difference modulo its bit width |
| `05` | `0C` | WrappingMultiply | Equal integer types | Same type, product modulo its bit width |
| `15` | `01` | CheckedAdd | Equal integer types | Same type on success; failure if mathematical sum is out of range |
| `15` | `02` | CheckedSubtract | Equal integer types | Same type on success; failure if mathematical difference is out of range |
| `15` | `03` | CheckedMultiply | Equal integer types | Same type on success; failure if mathematical product is out of range |
| `15` | `04` | CheckedDivide | Equal integer types | Same type on success; zero divisor or signed minimum / `-1` fails |
| `15` | `05` | CheckedRemainder | Equal integer types | Same type on success; zero divisor fails, signed minimum % `-1` is zero |
| `15` | `06` | CheckedShiftLeft | Integer left, `uint32` count right | Same left type; count outside `[0,width)` fails, bits shifted out are discarded |
| `15` | `07` | CheckedShiftRight | Integer left, `uint32` count right | Same left type; count outside `[0,width)` fails, sign-fill for signed, zero-fill for unsigned |

The opcode/kind pair selects behavior; operand types supply width and
signedness, and the result descriptor must match the result type.
`CheckedBinary` is a provisional **integer primitive** mnemonic, not a
polymorphic call to an operator named `+` or a promise that every type
with a numeric witness accepts the opcode. There are three distinct
cases: this exact built-in primitive operation; an arbitrary nominal
`operator +` selected as a MethodDef call; and a type explicitly opting
into a **semantic numeric contract** whose selected witness commits
to specified numeric rules. The third case need not be opaque to an
optimizer merely because the implementation is user-defined. It still
must retain the exact selected witness/MethodDef for fallback and separate
compilation, rather than treating the interface name or a matching
machine representation as a primitive operation.

A generic operator witness currently establishes only its declared
signature/effects, not algebraic laws. A stronger `INumber`-style
contract could specify value domain, exact arithmetic and overflow
policy, failure order, observable effects, result validity/ownership
and which identities or transformations are observationally sound.
Different contracts may promise different subsets: mathematical
associativity alone cannot erase an intermediate checked failure,
allocation failure or cleanup. A contract that explicitly permits
folding still needs deterministic constant evaluation and a validated
serialization of the resulting value. The producer's checked witness
and published contract would be trusted under the metadata trust policy,
or audited where required; neither a type name nor a convenient
implementation body creates these promises.

Before admitting the third case to executable Body, choose a wire form
such as a semantic numeric operation carrying the selected witness and
fallback MethodDef, or a selected call with a registered semantic
profile. Define its binding, version/compatibility and exact optimizer
permissions together; the present `CheckedBinary` operand schema
cannot carry those identities. Until then, encode the selected call
and do not silently substitute a primitive opcode. This is an open
encoding choice, **not** a claim that opt-in numeric types must remain
opaque or can never participate in constant folding.

The symbolic spellings `Binary`, `CheckedBinary` and `ConstSigned` name
these **opcode schemas**, not source-language operations or
mandated display mnemonics. `CheckedIntegerAdd` and
`ConstSignedInteger` would describe this narrow subset more precisely;
the naming and family split should be resolved together with later
floating, aggregate and registered numeric forms rather than silently
extending the current integer kinds to them. Renaming these schemas
need not change their assigned opcode bytes.

The other discriminants are `Convert.policy`: `01` integer wrap,
`02` integer saturate, `03` equal-width integer bit reinterpretation;
and `Fail.reason`: `01` requirement, `02` overflow, `03` division by
zero, `04` invalid shift count. These are separate enum domains even
when the same byte value occurs. `CheckedConvert` has **no policy
byte**: it performs a checked range conversion with `tDest` before its
source `r` and defines `rNew` on success. Wrapping converts
the mathematical integer modulo the destination bit width; saturation
clamps to its range. Neither is an alternate decoding of `CheckedConvert`.

`Binary` kinds `01..05`, `0A`, `0C` and `CheckedBinary` kinds `01`, `03`
are commutative over already computed values; swapping operands does not
reorder their producing instructions. `NoThrow` is not purity:
unused results do not permit dropping or commoning calls, writes, failure
or cleanup. Constant folding must preserve a checked operation's success
value or ordered failure. No generic numeric opcode changes meaning when
a primitive or SIMD type is added.

`ConstBits` encodes the smallest unsigned width holding the declared-width
bit pattern, not a sign-extended 64-bit container: `int32(-1)` is
`FF FF FF FF` and `int64(-1)` is eight `FF` bytes. The reader first
zero-extends the encoded bits to the declared width and then interprets
signedness. Its `t` is `bool`, `char16` or an integer primitive.
`ConstSigned` instead uses the smallest signed width holding the
mathematical value (for example `-1` is `FF`, `200` is `C8 00`).
Its `t` is one of the four integer primitives, signed or unsigned, and
the signed mathematical value must fit that destination. Use `ConstBits`
for unsigned values above `int64.MaxValue`. The complete TypeSpec
selects the width; unrepresentable or noncanonical patterns reject.
For example, `02 04 01 03` defines `r4` as value 3 of Body-local `t1`,
which resolves to `int32`: `03` is the **one-byte encoded signedData**
chosen because 3 fits in one byte, not a one-byte result or an untyped
source literal. The result has the `int32` width and value range from
its type descriptor. `ConstBits` instead supplies a declared-width
bit pattern, so an `int32` based literal `0xFFFF_FFFF` has four
`FF` data bytes and means `-1`; its interpretation is not inferred
from the number of encoded bytes.

Neither operation accepts floating, aggregate or arbitrary nominal
TypeSpecs. A future floating constant must state its exact format,
rounding/bit encoding and NaN/signed-zero treatment rather than reuse
signed-integer data; a constructed aggregate must preserve its typed
field/initialization rules rather than reinterpret one scalar immediate.
A registered user literal is parsed at compile time against its selected
target; its eventual Body encoding must represent the validated value
under that type's constant and ABI contracts. This design does not
assign such a constant encoding. The literal does not make raw source
spelling an IL numeric operand or grant a generic primitive opcode to
the user type.

The executable scalar set is `bool`, `char16`, `int32`, `uint32`, `int64`
and `uint64`; integer arithmetic uses the declared width. `Binary`
comparisons require equal complete operand types and return `bool`; their
kind does not imply a user equality witness. `ConstBits` and `ConstSigned`
carry explicit Body-local type IDs resolving to TypeSpec; arithmetic reads types from value
descriptors and must match the result descriptor. No instruction assumes
a default integer type or implicitly narrows, promotes, loads, updates or
swaps operands. `Load` produces a typed Copy value; `Assign` writes through
a place only after a preceding checked operation succeeds. Unsupported floating, vector,
atomic, raw-pointer and text operations have no numeric opcode.

`Convert` policy 1 wraps the mathematical value modulo the destination
width and interprets those bits in the destination's signedness; policy 2
clamps to its range. Policy 3 preserves bits only between equal-width
integer primitives; it cannot fabricate `bool`, `char16`, optional or
nominal values. `Unary` NOT and `Binary` bitwise/wrapping kinds apply only
to integers; Boolean NOT, equality and comparisons have separate rules.
`Switch` patterns must be canonical `ConstBits` patterns of the selector
type, without truncated high bits or duplicates. Other effectful or
potentially failing operations require a specified operation or future
feature, not an invented call target.

`Call` does not encode a no-throw assertion. The selected declaration's
optional `NoThrow` guarantee is a separate published effect, not part of
ordinary call identity; it still requires proof and compatible binding
when declared. A call without that guarantee may fail, including through
native interop. A later exceptional-region contract can direct such a
failure to an explicitly authored unsafe `try`/`catch`; this initial
scalar executable subset does not yet encode handlers. Failure cannot
be erased or silently converted into a normal return.

`Initialize`, `Assign`, `Move`, `Drop` and `Aggregate` require complete
flow-sensitive vacancy, ownership and cleanup verification over every
normal edge. Explicit Drop records the point of cleanup; a region marker
alone does not execute it. In this revision any implicit failure departure
with a live owner, loan, or required root rejects unless its cleanup and
unwind representation is specified. No additional private representation
implicitly extends this revision's accepted Body forms.

## Terminators and CFG

An **edge** is reference-width `targetBlock` followed by `args`. Every
target exists, and supplied values match its parameters in order. There
are no fall-through or byte-relative branches. Here `r?` is zero only for
void; failure uses the module's fail-fast or checked-exception policy.

| Opcode | Terminator; operands in byte order | Contract |
|---:|---|---|
| `80` | `Branch edge` | Unconditional transfer. |
| `81` | `BranchIf rBool, edgeTrue, edgeFalse` | Evaluates an already computed Boolean once. |
| `82` | `Switch rScalar, count(caseCount), (data(bits), edge)[caseCount], edgeDefault` | Exact type-width patterns sorted strictly by unsigned bits; one selected edge, no repeated selector evaluation. |
| `83` | `Return r?` | Matches declared result; moved owner transfers, remaining obligations must be discharged. |
| `87` | `Fail u8(reason)` | Declared requirement failure (`1`), overflow (`2`), division by zero (`3`), invalid shift count (`4`); no successor. |
| `8A` | `Leave count(exitCount), regionId[exitCount], edge` | Reserved for explicit scope/cleanup exit; not executable until its unwind contract is assigned. |

Failure never becomes an unchecked result or `None`. A potentially failing
operation or `Fail` terminator is forbidden in a declared `NoThrow` method
unless a specified static proof rules out failure. In `(1, 0)`, a terminal
call to a separately audited `DoesNotReturn` method supplies that proof only
for the otherwise unreachable `Fail` fallback after the call; it does not
excuse any other failure. `Call` checks the selected signature, declared effects,
argument modes and return ownership. Without a declared `NoThrow`
guarantee, failure policy does not create a normal CFG edge or automatic
cleanup, so a caller needing cleanup on failure is unsupported by this
initial executable subset.
`CheckedIndex` and `Leave` are reserved and rejected by `(1, 0)`.

`CheckedBinary` kinds 1..5 use equal integer types; kinds 6/7 take a
`uint32` right operand as the shift count. Signed negation fails at the
minimum. Checked conversion preserves the exact value or fails, with no
numeric fallback. Explicit edges and loop
regions define the CFG; block and terminator offsets provide direct access.
Trusted consumers may use those indexes and live sets; an optional audit
decodes operations and recomputes predecessors, dominance and liveness.
Neither may assign new meaning to an unknown opcode.

`CheckedDivRem` takes equal complete integer types. It checks a zero
divisor or signed minimum divided by `-1` once, then defines distinct
same-typed quotient and remainder results. Quotient truncates
toward zero; remainder follows the dividend. `CheckedBinary` remainder does not
fail for signed minimum `% -1` (the result is zero), so it cannot be
rewritten as combined division/remainder. A consumer may fuse distinct
operations only when failure order, uses and effects permit it.

`CheckedRange` would check
`start <= length && count <= length - start`, allowing the empty range at
the end; later accesses still need a proof or check. `CheckedReadIndex`
would combine a bounds check and Copy read without exposing an invalid
intermediate place. Both remain non-executable pending backing,
provenance, access and failure-order rules. Future intrinsics need explicit
identities and contracts; callable names alone do not establish them.

### Why a catch is not another normal edge

A source extension of `MaybeAdd` could wrap its checked sum in
`try { return x + y; } catch { return x; }` under a checked-exception
policy. Its successful path still has the same B2 add and Return as
above; overflow departs **at the add**, before the Return, and the
handler returns `x`. It does not redirect B1's earlier choice to B3
or imply a normal successor after every checked operation.
The source `catch` is special/unsafe, not a fallback expression that
turns an overflowing primitive operation into a wrapping sum.

This is **not an encodable or executable Body example** in the present
subset: only fail-fast policy 0 executes, and handler regions are
reserved. A future representation must identify protected operation
sites and their handler/cleanup order, the failure payload and policy,
place/owner state and logical roots at exceptional departure, and how
control enters the handler. Normal CFG indexes remain useful for
branches, loop backedges and returns, but cannot stand in for those
exceptional facts. Merely changing the runtime-policy byte on the
previous exact-byte fixture would not supply them.

## Value and place state through checked arithmetic

The introductory [checked-add example](#checked-add-end-to-end-informative)
shows the executable by-value case in the narrow fail-fast `(1, 0)`
profile: `r1` and `r2` are its parameters and `r3` is defined by the first checked
operation in B1. The alternative below adds an addressable local place
and is decode-only in this revision.

For `int32 IncrementLocal() { mut int32 local = 10;
local = local + 5; return local; }`, instead declare a mutable, initially vacant
`int32` place `p1` in the Body place table. Its artifact-wide type context
is separate from the Body:

```text
Artifact-wide table rows (separate sections; not Body bytes):
    TypeSpec (0x0011):
        row[1]: Builtin(int32)
    MethodSig (0x0017):
        row[1]:
            resultTypeSpec: TypeSpec[1]
            receiverTypeSpec: 0
            paramFirst: 0
            paramCount: 0
            genericFirst: 0
            genericCount: 0
    Param (0x0018): no rows selected
    MethodDef (0x0016):
        row[1]:
            signature: MethodSig[1]
            bodyRow: Body[1]
```

The following Body projection is decode-only and not a wire listing:

```text
Body shape (logical projection; not wire syntax):
    counts:
        blockCount: 1
        valueCount: 5
        placeCount: 1
        regionCount: 0
        typeCount: 1
    typeMap:
        t1: TypeSpec[1]
    placeDescriptors:
        p1:
            typeId: t1
            regionId: 0
            flags: 0x05
            useBlockCount: 1
    valueDescriptors:
        r1:
            typeId: t1
            kind: place-handle
            useBlockCount: 1
        r2..r5:
            typeId: t1
            kind: value
            useBlockCount: 1
    blocks:
        B1:
            parameters: []
            liveIn, liveOut: [], []
            inheritedPlaces: []
            startPlaces: [p1 vacant]
            operations:
                r1 = PlaceLocal p1
                r2 = ConstSigned t1, 10
                Initialize r1, r2
                r3 = Load r1
                r4 = ConstSigned t1, 5
                r5 = CheckedBinary add r3, r4
                Assign r1, r5
            endPlaces: [p1]
            terminator: Return r5
```

`r5` is assigned and returned only after the check succeeds. `PlaceLocal`,
`Initialize`, `Load` and
`Assign` lack complete place and failure-path rules, so this shape is not
executable. `PlaceField` produces a place; `Extract` reads a Copy field
from a by-value aggregate.

`r1` has the place's *referent* type, not a raw pointer type. No value
is a block parameter; the Body index projection (not encoded bytes) is:

```text
Body index (logical projection; not wire syntax):
valueDefStarts: [0, 5]
valueDefinitionOffsets: [24, 27, 34, 37, 41] // B1-relative bytes
placeLastUses: [(p1, 7)] // end-place transition
valueLastUses:
    r1: Assign (6)
    r2: Initialize (2)
    r3, r4: CheckedBinary (5)
    r5: Return (7)
```

All five definition offsets point to B1 operations. `p1` becomes active
vacant at entry and ends after the operations; its last-use ordinal 7
includes the end-place transition. `r1` first becomes available at
`PlaceLocal` (operation 0) and is last read by `Assign` (6). Ordinary
`r2` is last read by `Initialize` (2), `r3` and `r4` by the checked add
(5), and `r5` by Return (7). `r5` is also read by `Assign` (6), but the
last-use inventory stores only its *final* use. Dropping this block would also
remove its place-state transitions; dropping individual effects from
a retained block requires their full ownership and failure contract.

The following exact bytes encode only the 122-byte Body (0x0020) section
(8-byte offset index and 114-byte row). Its `05` place flags mean mutable
and initially vacant; block offsets are `[0,51]`, and the terminator
offset is `[49]`. `p1` occurs in one block; B1's last-use inventory records
its end transition at ordinal 7. The five value definitions occur at
B1 byte offsets `[24,27,34,37,41]`:

```text
00 00 00 00  72 00 00 00
00  01 05 01 00 01  01 00 00 00  01 00 05 01
01 01 01  01 00 01  01 00 01  01 00 01  01 00 01
00 05
18 00 00 00  1B 00 00 00  22 00 00 00  25 00 00 00  29 00 00 00
00 00 00 00  33 00 00 00  31 00 00 00
00 00 00 00 00 01 01 01 01
05 01 06 02 02 03 05 04 05 05 07  01 01 07  07
0B 01 01  02 02 01 0A  0E 01 02  0D 03 01  02 04 01 05
15 05 01 03 04  0F 01 05  83 05
```

B1 starts vacant `p1`, ends `p1` after seven operations, and has no
cross-block live values or inherited places. Its use entries give
`r1` last use at Assign (ordinal 6), `r2` at Initialize (2), `r3`
and `r4` at the checked add (5), and `r5` at Return (7).
Its header and the terminator
index are directly readable; they do not authorize executing
the unsupported place operations. An interpreter may use value slots and
verified place storage; JIT/AOT may choose registers or memory. The format
does not prescribe native stack slots or separate add opcodes by operand
source.

## Worked bytes and illustrative size

For [metadata example](il-metadata.md#row-assignment-example),
the artifact-wide metadata context, separate from the Body section, is:

```text
Artifact-wide table rows (separate sections; not Body bytes):
    TypeSpec (0x0011):
        row[1]: Builtin(int32)
    MethodSig (0x0017):
        row[1]:
            resultTypeSpec: TypeSpec[1]
            receiverTypeSpec: 0
            paramFirst: 0
            paramCount: 0
            genericFirst: 0
            genericCount: 0
    Param (0x0018): no rows selected
    MethodDef (0x0016):
        row[1]:
            signature: MethodSig[1]
            bodyRow: Body[1]
```

The `return 42` Body has one value, one implicit entry block, no places
and no explicit regions. Compact linear is the natural space-saving
illustration; the general form remains valid but is not repeated here.

### Compact linear constant return

See [compact-form information accounting](#compact-form-information-accounting)
for the implicit-versus-encoded fields in this form.

The `return 42` method uses a compact-linear Body section of 21 bytes
(8-byte row index and 13-byte row). `ConstBits` and Return are unchanged;
the separate MethodSig and mapped TypeSpec supply the complete result
type. Its Body row projection and exact bytes are:

```text
Body row (compact linear; 13 encoded bytes):
    bodyLayout: 0x04
    typeCount: 1
    typeMap:
        t1: TypeSpec[1] // int32
    operationCount: 1
    operations:
        r1 = ConstBits t1, 42
    terminator: Return r1
```

```text
Body shape (inferred projection; not additional compact-linear bytes):
    counts:
        blockCount: 1
        valueCount: 1
        placeCount: 0
        regionCount: 0
    valueDescriptors:
        r1: t1 // constant 42, defined by operation 0
    blocks:
        B1():
            r1 = ConstBits t1, 42
            Return r1
```

```text
Body index (inferred projection; not stored in compact linear):
    valueDefStarts: [0, 1]
    definitions:
        r1: B1 operation 0
    useBlockCounts: [1]
    last uses:
        B1: [(r1,1)]
```

The absent block header, place/region records, descriptor and
definition/last-use indexes need no empty placeholders. The following
sequence contains only the Body (0x0020) section bytes:

```text
00 00 00 00  0D 00 00 00
04 01 01 00 00 00 01
01 01 01 2A 83 01
```

#### Compact linear parameter return

See [compact-form information accounting](#compact-form-information-accounting)
for the implicit-versus-encoded fields in this form.

For a separate `Identity` fixture,
`int32 Identity(int32 x) { return x; }`, the artifact-wide context is:

```text
Artifact-wide table rows (separate sections; not Body bytes):
    TypeSpec (0x0011):
        row[1]: Builtin(int32)
    MethodSig (0x0017):
        row[1]:
            resultTypeSpec: TypeSpec[1]
            receiverTypeSpec: 0
            paramFirst: 1
            paramCount: 1
            genericFirst: 0
            genericCount: 0
    Param (0x0018):
        row[1]:
            ownerMethodSig: MethodSig[1]
            ordinal: 0
            typeSpec: TypeSpec[1]
            mode: 0
    MethodDef (0x0016):
        row[1]:
            signature: MethodSig[1]
            bodyRow: Body[1]
```

The entry parameter is Body-local `r1`; its row shape and exact Body
(0x0020) section bytes are:

```text
Body row (compact linear; 9 encoded bytes):
    bodyLayout: 0x04
    typeCount: 1
    typeMap:
        t1: TypeSpec[1] // int32
    operationCount: 0
    terminator: Return r1
```

```text
Body shape (inferred projection; not additional compact-linear bytes):
    counts:
        blockCount: 1
        valueCount: 1
        placeCount: 0
        regionCount: 0
    valueDescriptors:
        r1: t1 // x; by-value parameter
    blocks:
        B1(r1 <- x):
            Return r1
```

```text
Body index (inferred projection; not stored in compact linear):
    valueDefStarts: [0, 1]
    definitions:
        r1: entry parameter sentinel
    useBlockCounts: [1]
    last uses:
        B1: [(r1,0)]
```

This is a 17-byte Body section (8-byte row index and 9-byte row):

```text
00 00 00 00  09 00 00 00
04 01 01 00 00 00 00 83 01
```

`Identity` has the same one-block *shape* as `return 42` but a different
definition/use graph. There is no second type-independent opcode or
license to share an executable Body across MethodDefs under the current
unique-owner rule, regardless of signature.
Interning a reusable shape across methods would require a new
bounded template/reference contract with type, operand, effect and
binding substitutions; cross-body template lookup could cost more
than these short rows and is not assigned in this design. Exact Body
sharing among compatible MethodDefs also requires a future metadata
ownership revision; it is not an implicit optimization.

## Tool roles and future executable contracts

A structural reader bounds tables and Body rows, indexes strings and blocks,
preserves unknown **registered nonsemantic** sections when rewriting, and
reports unsupported opcodes without executing them. A semantic audit checks
operations and flow; interpreters, JITs and AOT implement only supported
features. Linkers rewrite row references, body IDs and affected indexes/
liveness facts, then revalidate. Trimmers retain transitive type, signature,
contract, selected-call, witness and cleanup dependencies. Removing
SourceMap cannot change execution.

| Consumer question | Direct format input | Remaining consumer freedom |
|---|---|---|
| Where is a block and its branch? | Block and terminator offsets, explicit target IDs | Choose hot/cold layout and machine branch forms without bytecode displacement fixups. |
| What crosses a block boundary? | Typed merge parameters, sorted live-in/out value IDs, active place/state joins; indexed definitions and last uses identify in-block lifetimes | Fast tier can avoid rebuilding ordinary boundary and last-use facts; optimizing tier may rebuild SSA/liveness or choose a different register allocation. Native stack slots are not prescribed. |
| What must be rooted at a safepoint? | Typed values, place descriptors and boundary liveness identify candidates, but no per-safepoint root/lifetime index is assigned | A backend must currently establish logical root continuity and emit its own register/stack GC maps; boundary liveness alone cannot supply them. |
| Which loops and failures are present? | Region headers/exits; checked operations carry ordered failure effects; explicit `Fail` terminators | Lower failure/cleanup and optimize loops without treating hints as a prescribed machine CFG. Locating failures inside blocks still needs an operation scan until an effect summary is assigned. |
| Which calls are intrinsic candidates? | Exact selected method identities and declared contracts; future registered intrinsics require explicit semantics | Inline or lower only compatible recognized identities, never guess from names or rewrite effectful calls as pure arithmetic. |
| Which numeric/vector operations exist? | Stable opcode semantics plus TypeSpec and required feature identity | Extend primitives and SIMD via new registered types/operations; do not silently reinterpret old scalar opcodes or assume every lane has the scalar's failure behavior. |

These indexes support importer/JIT work without prescribing an optimization
schedule or preventing deeper alias, memory-SSA or range analysis. They may
reduce repeated traversal; no throughput ratio is asserted.

### Executable subset versus assigned decode shapes

For `(1, 0)`, an executable body uses only scalar TypeSpecs (`bool`,
`char16`, `int32`, `uint32`, `int64`, `uint64`) and optional forms of those
scalars, by-value parameters, no receiver, no places, and no generic or
external call. It can contain loop regions as constrained above. Operations
`01..0A`, module-local scalar `12`, `15..18` and terminators `80..83`, `87` are executable with their stated
type, state and failure checks. `CheckedBinary` kinds 4 and 5 require
an integer divisor and use the selected module fail-fast policy on failure;
`CheckedConvert` accepts the assigned integer types and `char16` as a
non-arithmetic 16-bit code unit with range `0..65535`; it preserves
the mathematical value when in range. `Fail` terminates
under that policy. Only `runtimePolicy=0` is executable in this subset:
checked-exception dispatch requires a specified exceptional edge, payload
and cleanup protocol. All live values in this subset are Copy values, so a
failure cannot strand a unique cleanup obligation.

Executable `Call` (`12`) selects a locally authored, module-level MethodDef
using nonzero `m`, with no receiver or generic parameters. Its parameters are
by-value primitive scalars and its result is a primitive scalar or `void`;
every argument and result must match the selected signature exactly. A void
call has result ID zero but still consumes an operation ordinal and retains
its ordered effects. It creates no value definition. Every declared Body,
including uncalled targets, must pass the executable audit. A `NoThrow`
caller requires the selected callee's checked `NoThrow` declaration; other
calls may fail under the module's fail-fast policy. Forward and recursive
references do not require declaration order or recursive inlining.
Imported, packed cross-module (`m=0`), nominal/instance, generic/witness,
non-value-mode and owning calls remain outside this executable subset.

`0B..11`, `13..14`, `1A..1C`, `8A`, and nonzero place counts are **assigned
decode shapes, not executable forms**; `19` is reserved and rejects.
The assigned shapes require place authority,
ownership, cleanup, richer call failure, backing or region rules that this revision
does not encode completely. The `02` lexical and `03..05`, `07` region kinds
are likewise reserved for execution. A structural inspector can identify each
known opcode's operands and report unsupported semantics; a semantic editor
cannot safely relocate or rewrite it without those rules. An executable
reader rejects such forms rather than treating them as pure scalar
operations. This classification overrides any broader description in the
decode table; it is not a promise that an unsupported program will execute.

This is not yet enough for a Cecil-like semantic editor over all Terrum
programs. Required design work before claiming a complete interchange
contract:

| Surface | Missing binary and verification contract |
|---|---|
| Exception and cleanup | Authored catch/finally, cleanup on normal/exceptional exit, fault replacement, operation-site failure state (including calls) and required roots through cleanup. Root-free fail-fast calls do not establish these broader contracts. |
| Fast method facts | The Body now assigns direct value definitions, per-block last-use inventories and per-value/place use-block counts. Still define the mandatory method-level operation/effect summary, finer per-block effects and safepoint/root inventory, with bounds and consistency audits. Specialized branch reachability and per-consumer profitability are computed at the call site, not serialized as authoritative facts. |
| Loans and views | Place provenance, region escape/alias checks, retained backing and owner dependencies, interior references, `mut` versus `write`, suspension/capture and explicit begin/end loan forms. |
| Managed values | Root maps at safepoints, stable descriptors, write barriers, allocation/collection, optional handles and per-image heap boundaries. Root lifetime cannot be inferred solely from SSA uses. |
| Logical root inventory | Specify producer-recorded, directly indexed safepoint locations and live logical root/owner/interior-place dependencies (including exceptional continuation and cleanup), with precise lifetime and verification rules. The format should avoid making each interpreter/JIT reconstruct logical GC liveness from every use; it must not prescribe post-allocation native register/stack maps or assume that ordinary value liveness covers roots. |
| Published contracts | Full effects, ownership/transfer, correlated output states, pre/postconditions, capability and witness evidence, generic specialization/export metadata and layout/ABI compatibility. Metadata clauses must be specified together with verifier inputs. |
| Operations | Checked indexing/backing, raw/unaligned and atomic memory order, floating/SIMD/tensor semantics, indirect calls, traits/witness dispatch, intrinsics, async state and typed failure values. Assign only operations whose exact type, failure, ordering and lifetime rules are known. |
| Revision and reader policy | Future on-wire feature/revision negotiation, nonminimal-width load policy, diagnostic/recovery behavior and numeric parser/audit budgets remain open. Readers of this subset validate every block's registered opcodes before selective import. |
| Type-map conformance | A complete reader must validate each fixed-`u32` indexed TypeSpec reference, distinct map entries, local type IDs and type agreement with MethodSig and selected definitions. The separate inline built-in extension requires a metadata format revision and explicit identity checks across indexed and inline forms; no Body-local ID may be mistaken for an artifact-wide metadata reference. |

These are requirements for future broader executable profiles and complete
interchange, not permissions to invent semantics or serialize the current
private bound tree. The bounded subset specified above is approved; forms
missing the stated contracts remain non-executable.
See [implementation](../implementation.md) for what the repository actually
executes. The source-language [portable proof floor](language-contracts.md#4-a-reproducible-proof-and-evaluation-foundation)
is distinct from decoder and verifier work budgets.

The compact tags and their bytes are approved for the bounded subset;
writer-selection policy and other unapproved performance extensions remain
in the [IL extension proposal](../proposals/il-extensions.md#compact-profile-selection-and-follow-up).
