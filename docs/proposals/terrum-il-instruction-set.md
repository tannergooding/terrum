# Terrum coded instruction proposal

> [!IMPORTANT]
> Candidate coded-instruction design, not the current semantic-module writer or
> an approved execution profile. The unencoded contracts at the end are
> stabilization blockers, not permissions to erase their semantics.
> `(1, 0)` below labels this proposal's candidate executable subset; it is
> neither an on-wire profile selector nor the approved metadata design's
> format revision `(1, 0)`. Profile negotiation remains unresolved.

The [approved current metadata design](../il-metadata.md) defines the Body table,
TypeSpec, MethodDef, contracts and artifact framing. This document specifies
the Body payload and operation stream separately, following only the
*organizational* distinction between ECMA-335 Partitions II and III. It does
not adopt CIL opcodes, its evaluation stack, branch displacement formats,
metadata tokens or runtime ABI.

## Model and canonical form

A body implements one MethodDef. A **block** has typed parameters, ordered
operations and one terminator; each normal edge supplies its successor's
parameters. Checked operations and potentially failing calls do not end a
block merely because they can fail. They define their result at their
operation site on success; failure leaves normal control flow under the
selected runtime policy. Explicit branches, returns, failures and region
exits determine normal CFG boundaries.
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
values; consumers need not infer those solely from instruction uses. It does
not yet encode the *intra-block* last uses, per-safepoint logical roots or
the native locations required by a GC. Those are separate concerns: a
producer can publish logical root identity, lexical lifetime, ownership and
interior-reference dependencies, but only the backend knows which registers
or stack slots hold them after lowering. Do not treat source-local counts or
ordinary SSA last-use as a complete GC root map.

Numeric policy (checked, wrapping, saturating, bit reinterpretation) is
explicit; converting one into another is not a space-saving normalization.
A discarded result does not discard an effectful call. Typed source binding
selects one MethodDef before encoding; no reader reruns overload resolution.
Operations absent from the assigned tables reject rather than being guessed
from CIL or CLR behavior.

### Architecture-critical indexes and contracts

This proposal's worked examples are part of the design explanation, not
replaceable opcode trivia. The intended fast path crosses two documents:

| Input | What a consumer can learn without rediscovering it from instructions |
|---|---|
| [Approved metadata tables](../il-metadata.md#section-kinds-and-row-layouts) | Selected declaration and complete type/signature; retained imports bind against published contracts without re-proving dependency bodies |
| [Contract clauses](../il-metadata.md#semantic-contracts-and-stable-binding) | Assigned effects such as `NoThrow`/`DoesNotReturn` and reserved output, transfer, dependency and cleanup shapes; only Effects is executable in this narrow Body subset, so readers must not assume the other contracts are already usable |
| [Body index](#body-record-direct-block-index-and-regions) | Type/value/place inventories, exact definition locations, direct block/terminator addresses, normal boundary liveness and per-block final uses |
| [Conditional and loop examples](#conditional-merge-and-loop-informative-shapes) | Which values merely transit a block, which ones are actually used there, and how pruning a dead arm updates surviving use-block counts |
| [Checked-add bytes](#checked-add-end-to-end-informative) and [place example](#value-and-place-state-through-checked-arithmetic) | Ordered failures without artificial CFG edges; separate immutable values, addressable storage and their last-use/state transitions |

A specializing inliner can seek a predicate's definition, inspect the
relevant dependency instructions, follow indexed terminators and discard
unreachable blocks using their use inventories. It still decodes retained
operations and respects effects, ownership and selected contracts;
indexes are producer facts under the specified trust/audit policy, not
an independent proof of safety. Method-level effect/call summaries
and per-safepoint logical roots remain [stabilization
blockers](#tool-roles-and-stabilization-blockers), not capabilities this
draft has already encoded. If these informative examples move to a
companion document, retain this contract map and links from the
normative rules; splitting the examples must not orphan the fast-import
story or silently promote a pending Body schema to approved metadata.

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

An [inline built-in type-word candidate](../il-metadata.md#table-stream-and-token-encoding)
is under review, **not approved or implemented**: typed type-reference
fields could use fixed `0xFE_qq_ff_pp` words for assigned built-in
atoms/families and independently qualified outer and argument types.
If adopted in both metadata and Body, `typeMap` would hold complete
typed type-reference words: either the inline form or an indexed
TypeSpec row for other types. A map lookup of an inline word avoids the
TypeSpec table lookup, but the Body-local `t` ID remains useful for
repeated uses and exposes the body's type inventory without scanning its
blocks. The fixed four-byte word and the Body-local 1/2/4-byte
IDs address different costs; replacing all local IDs with four-byte
words would also widen values, blocks and places. The byte listings
below use the **currently approved**, indexed TypeSpec form until a
metadata format revision adopts the candidate.

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

Opcodes `0x01..0x7F` are operations; `0x80..0xEF` are terminators.
`0xF0` introduces a width layout and is not standalone; `0x00` and
`0xF1..0xFF` reject. A wide prefix, layout and registered opcode must fit
within one block. Operands follow the opcode schema; there is no
per-instruction length. Unknown opcodes reject semantic loading and cannot
be skipped using an apparent length or prefix. Bound every operand and
counted field to the remaining block extent before use. Before allocating or
traversing repeated items, prove their minimum encoded bytes fit that extent.
After exactly `operationCount` operations, the indexed terminator must be
one registered instruction that ends the block.

Only opcode values assigned in the tables below are registered; other
values in the operation and terminator bands are reserved. This stream has
no opaque-instruction extension form. The metadata design's optional-section
policy does not make Body opcodes optional: a present Body section carries
required semantic content.
Future opcode allocation and feature/revision negotiation remain unspecified.
The policy for a trusted consumer that skips an entire unvisited block, which
might contain an unknown opcode, also remains unresolved.

An interpreter may dispatch on `(opcode, layout)` or decode native-width
operands; either choice needs measured cost evidence. Body-local IDs `1..255` use the common form, `256..65535` use two bytes,
and larger IDs use four. Fixed metadata `u32` operands do not widen with
the instruction layout.
This favors predictable dispatch and direct indexes for small methods over
minimizing every large literal or body. Explicit edges, place-state joins and
loop headers expose dataflow to consumers; no throughput or total-size win
is asserted.

The Body's upfront type, place and value inventories, block/terminator
offsets, direct value-definition locations, and per-block use/last-use
inventories are intended as **shared importer inputs**:
each consumer can obtain typed ID bounds, normal CFG targets and boundary
liveness without rediscovering them by scanning the operation stream.
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

Block/terminator offsets, typed value descriptors and boundary liveness
alone are **not sufficient** for a fast inliner. The definition index and
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

Consider `AddThenThree` under the fail-fast policy. Assume MethodDef row
1 owns the body, its MethodSig declares two by-value `int32` parameters
and an `int32` result, and TypeSpec row 1 describes `int32`. The
expected `int32` context types the literal `3`; each `+` selects
checked primitive addition, not a numeric fallback or user witness.

The notation below exposes the metadata and Body facts needed to read the
example; it is not wire syntax or a required source rewrite. `t1` is
Body-local type ID 1, mapped by a fixed `u32` header entry to TypeSpec
row 1. That TypeSpec row's built-in form uses primitive code `0x08`
for `int32`. `r1`–`r5` are one-based Body value IDs, not machine registers.
The value-descriptor array declares each ID's type and kind; the block
parameters or operations define its value. `B1` is a one-based
block ID. A block's parameter list declares values received on entry,
not all values that its operations will define. The definition index gives
`r1` and `r2` the B1 parameter sentinel, then points `r3`, `r4` and
`r5` to B1 operations at byte offsets 22, 27 and 31, respectively.

```text
Source:   int32 AddThenThree(int32 x, int32 y) { return x + y + 3; }

TypeSpec[1] = Builtin(int32)                // row payload 01 00 08
MethodSig[1] = (parameter[0]: TypeSpec[1], parameter[1]: TypeSpec[1])
               -> TypeSpec[1]              // x, y in source order
MethodDef[1] = (signature=1, bodyRow=1)
Body: ownerMethod=1, bodyLayout=0
      blockCount=1, valueCount=5, placeCount=0, regionCount=0, typeCount=1
      typeMap=[t1: TypeSpec[1]]             // fixed u32 typed reference
      valueDescriptors=[r1:(t1,value,useBlocks=1), r2:(t1,value,useBlocks=1),
                        r3:(t1,value,useBlocks=1), r4:(t1,value,useBlocks=1),
                        r5:(t1,value,useBlocks=1)]
      valueDefStarts=[0,5]
      valueDefinitionOffsets=[parameter,parameter,22,27,31]
      blockOffsets=[0,38], terminatorOffsets=[36]
      B1 operationCount=3, region=0, liveIns=[], liveOuts=[], place lists=[]
      B1 valueLastUses=[(r1,op0),(r2,op0),(r3,op2),(r4,op2),
                        (r5,terminator3)]

B1(r1 <- parameter[0], r2 <- parameter[1]):  // x, y defined on entry
    r3 = CheckedBinary add r1, r2
    r4 = ConstSigned t1, 3
    r5 = CheckedBinary add r3, r4
    Return r5
```

The operations define `r3`, `r4` and `r5` in source order within B1;
`ConstSigned` types the literal `3`. On first-add overflow the later
operations do not run; on second-add overflow the return does not run.
Both failures follow the module policy without manufacturing normal CFG
edges or block parameters. Every value has one descriptor
and one definition; the descriptor alone does not create a value.
`r1` and `r2` become available at block entry and are last read by
operation 0. Their parameter sentinel is not a machine slot or an
instruction that needs executing.
`r3` becomes available after operation 0 and is last read by operation 2;
`r4` becomes available after operation 1 and is also last read by
operation 2. `r5` becomes available after operation 2 and is last read
by the return terminator (ordinal 3). Neither value lives across a block
boundary; the five `useBlocks=1` counts are corroborated by B1's five
last-use entries. This describes ordinary operand uses, not a GC-root
lifetime or an instruction's permission to be deleted.
The Body stores numeric IDs, not the source names or these comments.
The TypeSpec row reference occupies four bytes in `typeMap`, as it does
in MethodSig; repeated `t1` references are one-byte local IDs because
this Body has one type. The equal numbers in `t1` and `TypeSpec[1]` are
coincidental: the map may assign a different local ID to that row.
Under the inline-word candidate, the `typeMap` entry for an unqualified
`int32` could instead be `0xFE_00_00_08` (`08 00 00 FE` on the wire).
That keeps `t1` one byte per use without the TypeSpec lookup; it does
not license replacing the current `01 00 00 00` entry in this fixture
until the metadata reference rule changes.

The Body **section** uses one mapped type, five values, one block and
no places or regions. Its 109-byte structure is:

| Region | Size and contents |
|---|---|
| Body-section offset index | 8 bytes: `00 00 00 00 65 00 00 00` = `[0,101]`; not an instruction |
| Body row header and definition index | 51 bytes: `ownerMethod=1`, `bodyLayout=0`, counts `(block,value,place,region,type)=(1,5,0,0,1)`, `typeMap=[TypeSpec row 1]`, five `(t1,value,useBlockCount=1)` descriptors, `valueDefStarts=[0,5]`, and five fixed-`u32` definition offsets |
| Body row offset arrays | 12 fixed-`u32` bytes: `blockOffsets=[0,38]`, `terminatorOffsets=[36]` |
| Block data | 38 bytes: B1 has five last-use entries, three operations and one return |

Here `bodyLayout=0` selects one-byte Body-local widths. The owner,
type-map entries and offset arrays remain fixed `u32`; instruction
widths are independent, and none here has an `F0` prefix.
Body fields and validation rules are specified in
[Body record, direct block index and regions](#body-record-direct-block-index-and-regions).

The exact section bytes are the eight-byte offset index followed by the
101-byte Body row:

```text
00 00 00 00  65 00 00 00
01 00 00 00  00  01 05 00 00 01  01 00 00 00
01 00 01  01 00 01  01 00 01  01 00 01  01 00 01
00 05
FF FF FF FF  FF FF FF FF  16 00 00 00  1B 00 00 00  1F 00 00 00
00 00 00 00  26 00 00 00  24 00 00 00
00 02 01 02 00 00 00 00 00  05 01 00 02 00 03 02 04 02 05 03  00 03
15 03 01 01 02  02 04 01 03  15 05 01 03 04  83 05
```

| Location | Opcode-specific schema, in byte order | Widths / layout | Encoded fields | Reading |
|---|---|---|---|---|
| B1 operation 1 | `15 CheckedBinary ref(rNew) u8(kind) ref(rLeft) ref(rRight)` | Common; ref=1; kind=`u8` | `15` opcode · `03` rNew · `01` kind · `01` rLeft · `02` rRight | `kind=1` checked add; define `r3` from `r1` and `r2` |
| B1 operation 2 | `02 ConstSigned ref(rNew) ref(t) signedData(value)` | Common; ref=1, data=1 | `02` opcode · `04` rNew · `01` t · `03` signedData | Define `r4` as mapped `t1` value `3` |
| B1 operation 3 | `15 CheckedBinary ref(rNew) u8(kind) ref(rLeft) ref(rRight)` | Common; ref=1; kind=`u8` | `15` opcode · `05` rNew · `01` kind · `03` rLeft · `04` rRight | Define `r5` from `r3` and `r4` |
| B1 terminator | `83 Return ref(result)` | Common; ref=1 | `83` opcode · `05` result | Return `r5`, the second checked result |

Each checked operation encodes its own destination and has no success
edge. `ConstSigned` carries signed data, not an add immediate.
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
In a separate body, `ConstSigned r3, t1, 200` instead uses
`F0 10 02 03 01 C8 00` (one-byte references, two-byte signed data); if
its result ID were 256, it would use
`F0 11 02 00 01 01 00 C8 00`. The prefix changes encoded widths, not
the opcode's semantic identity. Conformance should independently verify
the bytes, decoded records and audited CFG/types/states; a same-writer
round trip alone is insufficient.

### Conditional merge and loop (informative shapes)

The following sketches use the same MethodSig, type-map, value-
descriptor, block-header and edge **rules**. They show composition rather
than another required source lowering or complete byte listing; a writer
must still encode exact block and terminator offsets.

For `int32 TwiceSum(int32 x, int32 y) { int32 tmp = x + y;
return tmp + tmp; }`, one block defines `r3 = CheckedBinary add r1, r2`,
then `r4 = CheckedBinary add r3, r3`, and returns `r4`. Reuse needs no
duplicate descriptor or `Copy`
operation for this Copy scalar; a unique value could not be duplicated
the same way. With all four value descriptors `(t1,value,useBlockCount=1)`,
its definition starts are `[0,4]`: `r1` and `r2` have the parameter
sentinel, `r3` points to operation 0 and `r4` to operation 1. B1's
last-use entries are `(r1,0),(r2,0),(r3,1),(r4,2)`, with ordinal 2
denoting Return. `r3` is read *twice* by operation 1 but appears once
in the per-block inventory and its `useBlockCount` is 1, not 2.

For `int32 Choose(bool flag, int32 x, int32 y) { mut int32 chosen = x;
if (flag) { chosen = y; } return chosen; }`, MethodSig has the three
by-value parameters in that order and an `int32` result. The Body has
`(blockCount,valueCount,placeCount,regionCount,typeCount)=(4,4,0,0,2)`,
`ownerMethod` pointing to its MethodDef,
`typeMap=[t1:TypeSpec[1] (bool),t2:TypeSpec[2] (int32)]`, and descriptors
`[r1:t1,r2:t2,r3:t2,r4:t2]`, all by-value. All blocks have region 0,
no places and no operations. An SSA lowering needs no place for the
unaddressed source local:

```text
B1(r1 <- flag, r2 <- x, r3 <- y): // liveOut=[r2,r3]
    BranchIf r1, B2(), B3()
B2():                              // liveIn=[r3], liveOut=[r3]
    Branch B4(r3)
B3():                              // liveIn=[r2], liveOut=[r2]
    Branch B4(r2)
B4(r4 <- selected value):          // no liveIn
    Return r4
```

The two explicit incoming arguments to B4 have the same complete type,
but `r4` is defined once by B4's parameter, not by either predecessor.
The branch copies only Copy values; `liveOut` is the union of the
successors' live-ins and explicit edge arguments. Source variable names
and the apparent assignment are not stored in the Body.

The method-wide definition starts are `[0,3,3,3,4]`. The first
three value-definition offsets are parameter sentinels in B1, and
`r4` is a parameter sentinel in B4; the intervening empty intervals
mean B2 and B3 define no values. The mapped value descriptors are
`[r1:(t1,value,1),r2:(t2,value,1),r3:(t2,value,1),r4:(t2,value,1)]`,
where the final number is `useBlockCount`. All four blocks have
`operationCount=0`, so ordinal 0 means their terminator:

| Block | `valueLastUses` | Boundary facts |
|---|---|---|
| B1 | `(r1,0)` | `r2` and `r3` are live-out transit values, not uses in B1 |
| B2 | `(r3,0)` | `r3` is live-in and supplied as B4's edge argument |
| B3 | `(r2,0)` | `r2` is live-in and supplied as B4's edge argument |
| B4 | `(r4,0)` | `r4` is defined at entry and used by Return |

If an inliner knows `flag` is true, B1's indexed terminator identifies
B2 as the only normal successor. B3 can be removed; its use inventory
lists `r2`, so removing B3 decrements `r2`'s remaining use-block count
to zero without decoding B3's operations. B1's resulting live-out no
longer includes `r2`. B2 still supplies `r3` to B4, whose `r4`
parameter remains a valid merge definition unless the inliner chooses
to substitute it. This is a Copy-scalar example; dropping a unique
argument or cleanup obligation requires its separate ownership/exit
contract, not just a zero ordinary-use count.

For `int32 CountTo(int32 n) { mut int32 i = 0; while (i < n) {
i = i + 1; } return i; }`, MethodSig has one by-value `int32` parameter
and an `int32` result. The Body has `(4,7,0,1,2)` for the same five
counts, `ownerMethod` pointing to its MethodDef,
`typeMap=[t1:TypeSpec[1] (int32),t2:TypeSpec[2] (bool)]`,
and descriptors `r1,r2,r3,r5,r6,r7`
of `t1` with `r4` of `t2`, all by-value. Region 1 is
`(kind=loop,parent=0,entry=B2,exit=B4,cleanup=0)`; B2–B3 belong to
it, while B1 and B4 are at region 0:

```text
B1(r1 <- n):                       // liveOut=[r1,r2]
    r2 = ConstSigned t1, 0
    Branch B2(r2)
B2(r3 <- current i):               // liveIn=[r1], liveOut=[r1,r3]
    r4 = Binary Less r3, r1         // t2 bool
    BranchIf r4, B3(), B4(r3)
B3():                              // liveIn=[r1,r3], liveOut=[r1,r6]
    r5 = ConstSigned t1, 1
    r6 = CheckedBinary add r3, r5
    Branch B2(r6)
B4(r7 <- final i):                 // no liveIn; outside loop
    Return r7
```

The header B2 receives its initial `i` from B1 and later iterations
from B3. `r1` is a dominating value carried across the backedge through
live-in/out sets, not an extra header parameter. B3 defines `r6` before
explicitly supplying it to the header. The false edge supplies `r3` to
the loop exit. A loop
region and valid backedge do not make a failing add an implicit cleanup
or catch edge.

The seven value descriptors resolve to the type map in ID order:
`[r1:t1/1,r2:t1/1,r3:t1/2,r4:t2/1,r5:t1/1,r6:t1/1,r7:t1/1]`.
All have value kind; the suffix is `useBlockCount`, **not** the number
of reads. `valueDefStarts=[0,2,4,6,7]`; `r1`, `r3` and `r7` have
parameter-sentinel definition offsets, while the others point directly
to `(B1,op0)`, `(B2,op0)`, `(B3,op0)` and `(B3,op1)`, respectively.
The actual row stores byte offsets to those operations; this sketch
does not assign their block-header or instruction bytes.

| Block | `operationCount` | `valueLastUses` | Important distinction |
|---|---:|---|---|
| B1 | 1 | `(r2,1)` | `r1` is a live-out parameter but is not read here |
| B2 | 1 | `(r1,0),(r3,1),(r4,1)` | `r3` is read by the comparison and again as the false-edge argument |
| B3 | 2 | `(r3,1),(r5,1),(r6,2)` | `r1` transits the backedge through live-in/out but is not read here |
| B4 | 0 | `(r7,0)` | `r7` is an entry definition read by Return |

The loop-header `r3` is used in both B2 and B3, so its
`useBlockCount=2`; its carried backedge replacements `r2` and `r6`
each have one use block. There are no places in this SSA-shaped
example, so `placeUseCount=0` in every block. These inventories are
about normal operand uses, not a license to drop a checked add or
proof that any managed root expires at an SSA last use.

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
edge)` pairs, then default edge. For `CallNoThrow`, it is result ID, method
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
traces the common form from source through Body structure to operand bytes.
It also shows why a wide instruction prefix and the enclosing
`bodyLayout` select widths independently.

## Body record, direct block index and regions

A Body table row is the bytes between adjacent Body-section offsets, with
no further row-length prefix. Its fields, in order, are:

| Field | Encoding | Constraint |
|---|---|---|
| `ownerMethod` | typed `u32` little-endian | Nonzero local MethodDef row with zero high byte, whose `bodyRow` points here |
| `bodyLayout` | `u8` | Bits 0–1 Body-local width; bits 2–7 zero |
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

Body-layout codes 0, 1 and 2 mean local widths of 1, 2 and 4 bytes; code 3
rejects. This width sizes all five counts, structural block/region/value/
place/type IDs, block parameters, place-state and operation counts, region
fields, per-descriptor use-block counts, definition starts, last-use
ordinals and place-region references. Choose the smallest width that
represents every field in this class throughout the **whole body**.
`ownerMethod`, type-map entries, definition offsets and both block
offset arrays remain fixed `u32` typed references or offsets. Each
instruction independently selects its
Body-local operand width via its prefix; FieldDef and direct-call DefId
operands remain fixed `u32`. Under the approved indexed-only rule,
bound `typeCount` by the TypeSpec table count and the remaining row
bytes before reading its four-byte entries. If inline built-ins are
adopted, use the Body-local ID range and remaining bytes as the bound
instead; a Body can have built-in types with no TypeSpec rows. Every
entry must be distinct and resolve to a complete type valid in the
owning module's context; duplicates reject. Writers order entries by first use
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
overflow, overlap, trailing bytes and duplicate body owners. A terminator
offset follows exactly the counted operations and points to one complete
terminator ending the block. Trusted readers may seek there after bounded
decoding; semantic audits compare it with the operation stream. Body rows
follow the owning MethodDef's `bodyRow`; relocating metadata requires
rewriting references and revalidating the body.

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
`0x06` unsafe and `0x07` root reserve their identities but are **not
executable** until exit/cleanup/exception rules and feature requirements are
assigned. No region kind by itself proves borrow safety or releases an owner.

A block extent contains these fields in order:

| Field | Encoding | Meaning |
|---|---|---|
| `regionId` | `local` | Innermost containing region; zero for the function root |
| `parameterCount`, `parameters` | `local`, then `local valueId[parameterCount]` | Values defined at this block's entry, in incoming-edge order; types come from `valueDescriptors` |
| `liveInCount`, `liveIns` | `local`, then `local valueId[liveInCount]` | Prior definitions live at entry, excluding block parameters |
| `liveOutCount`, `liveOuts` | `local`, then `local valueId[liveOutCount]` | Prior definitions needed on at least one outgoing normal edge |
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
| `BodyIndex` | `ownerMethod`, body layout, five counts, fixed-`u32` TypeSpec map, place/value descriptors, value-definition starts/offsets, block/terminator offsets | Local IDs and offsets remain relative to this one body; `ownerMethod` is a metadata row |
| `ValueDescriptor` | Body-local type ID and value/place kind | Entry `i` describes `r(i+1)`; resolve its type through the validated map without decoding the defining operation |
| `BlockLocation` | Block-data start + `blockOffsets[i-1]`, next block offset, relative terminator offset | Entry `i-1` addresses block ID `i`; terminator lies inside this block's extent |
| `BlockHeader` | Region, parameter IDs, live-in/out IDs, inherited/starting/ending place states, sorted last uses, operation count | Parameters define values; live sets refer to cross-block transfers, not native registers |
| `Instruction` | Registered opcode, three resolved widths and the opcode-specific typed operands | The wide prefix changes the byte widths only; do not store raw bytes as if they were an addressing mode |
| `Terminator` | Registered opcode and its typed edge/return/failure payload | Directly indexed; each normal edge targets a block ID and transmits the listed arguments |

Bound the Body row, descriptors and indexes before lookup. Decode each
operation by its registered schema; strict audit compares the stream with
the terminator index and checks type/flow. A trusted consumer may start at
an indexed terminator and follow edges without decoding unrelated blocks.
The format does not prescribe an object graph, register assignment,
optimization tier or cache.

## Type and definition model for operands

`t` indexes a Body-local type-map entry holding a fixed `u32` reference
to a complete row in the metadata design's
[TypeSpec table](../il-metadata.md#metadata-logical-tables-and-row-semantics),
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
| `CallNoThrow` | Selected MethodSig result type, or no definition when `rNew?=0` |
| `Extract` | Selected field's complete type, requiring a Copy-by-value field |
| `CheckedBinary`, `CheckedNegate`, `CheckedDivRem` | Result ID(s) defined in the same block on success; their failure is an ordered effect, not a normal edge |
| `CheckedConvert` | Exact `tDest` at its result ID on success |
| `CallMayFail` | Selected MethodSig result type, or no definition when `rNew?=0` |

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
| `12` | `CallNoThrow rNew?, m, args` | Exact selected callable; accepts only matching modes/contracts and a declared NoThrow effect. `rNew=0` iff result is void. Does not drop observable effects. |
| `13` | `Aggregate rNew, t, args` | Fully constructs ordered fields in declaration order, with checked Copy/unique transfer and validity; no implicit zero-fill or padding observation. |
| `14` | `Extract rNew, rAggregate, fieldDefRow` | Copy-only by-value field; unique fields require an authorized place and Move. |
| `15` | `CheckedBinary rNew, u8(kind), rLeft, rRight` | Kinds 1..5 checked add/subtract/multiply/divide/remainder; 6/7 checked-count left/right shift. Failure at this operation follows the selected policy; no result is defined on failure. |
| `16` | `CheckedConvert rNew, tDest, rSource` | Checked numeric range conversion; `rNew` has `tDest` on success. |
| `17` | `CheckedNegate rNew, rOperand` | Checked signed negation; fails at the minimum. |
| `18` | `CheckedDivRem rQuotient, rRemainder, rLeft, rRight` | Single checked failure decision; two distinct same-typed results on success. |
| `19` | `CallMayFail rNew?, m, args` | Potentially failing exact selected call; `rNew=0` iff void. Failure follows runtime policy, and authored handlers need a future exception-region contract. |
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
this proposal's **opcode schemas**, not source-language operations or
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
under that type's constant and ABI contracts. This proposal does not
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
feature, not `CallNoThrow`.

`Initialize`, `Assign`, `Move`, `Drop` and `Aggregate` require complete
flow-sensitive vacancy, ownership and cleanup verification over every
normal edge. Explicit Drop records the point of cleanup; a region marker
alone does not execute it. In this revision any implicit failure departure
with a live owner, loan, or required root rejects unless its cleanup and
unwind representation is specified. This is deliberately narrower than the
current compiler's private IR and does not weaken its executed guarantees.

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
operation or `Fail` terminator is forbidden in a `NoThrow` method unless a
specified static proof rules out failure; `(1, 0)` defines no such proof.
`CallMayFail` checks the selected signature, effects, argument modes and
return ownership. Failure policy does not create a normal CFG edge or
automatic cleanup, so a caller needing cleanup on failure is unsupported.
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

## Value and place state through checked arithmetic

The introductory [checked-add example](#checked-add-end-to-end-informative)
shows the executable by-value case in the narrow fail-fast `(1, 0)`
profile: `r1` is its parameter and `r3` is defined by the first checked
operation in B1. The alternative below adds an addressable local place
and is decode-only in this revision.

For `local = local + 5`, instead declare a mutable, initially vacant
`int32` place `p1` in the body place table. Its header and operations have
this decode-only shape:

| Block | Value parameters | Live-in / live-out values | Inherited places | Starts / ends | Operations and terminator |
|---|---|---|---|---|---|
| B1 | None | None / none | None | Start `p1` vacant / end `p1` | `r1 = PlaceLocal p1`; `r2 = ConstSigned t1, 10`; `Initialize r1, r2`; `r3 = Load r1`; `r4 = ConstSigned t1, 5`; `r5 = CheckedBinary add r3, r4`; `Assign r1, r5`; `Return r5` |

`r5` is assigned and returned only after the check succeeds. `PlaceLocal`,
`Initialize`, `Load` and
`Assign` lack complete place and failure-path rules, so this shape is not
executable. `PlaceField` produces a place; `Extract` reads a Copy field
from a by-value aggregate.

Its `typeMap=[t1:TypeSpec[1] (int32)]` and the place descriptor is
`p1:(t1,region0,flags=05,useBlockCount=1)`. The value descriptors are
`r1:(t1,place-handle,1)` and `r2`–`r5:(t1,value,1)`: `r1` has the
place's *referent* type, not a raw pointer type. No value is a block
parameter; `valueDefStarts=[0,5]` and all five definition offsets
point to B1 operations. `p1` becomes active vacant at entry and ends
after the operations; its last-use ordinal 7 includes the end-place
transition. `r1` first becomes available at `PlaceLocal` (operation 0)
and is last read by `Assign` (6). Ordinary `r2` is last read by
`Initialize` (2), `r3` and `r4` by the checked add (5), and `r5` by
Return (7). `r5` is also read by `Assign` (6), but the last-use
inventory stores only its *final* use. Dropping this block would also
remove its place-state transitions; dropping individual effects from
a retained block requires their full ownership and failure contract.

The encoded Body section is 126 bytes. Its `05` place flags mean mutable
and initially vacant; block offsets are `[0,51]`, and the terminator
offset is `[49]`. `p1` occurs in one block; B1's last-use inventory records
its end transition at ordinal 7. The five value definitions occur at
B1 byte offsets `[24,27,34,37,41]`:

```text
00 00 00 00  76 00 00 00
01 00 00 00  00  01 05 01 00 01  01 00 00 00  01 00 05 01
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

For [metadata example](../il-metadata.md#row-assignment-example),
MethodDef row 1 returns `int32` with no arguments; TypeSpec row 1 is
`int32`. Its `return 42` body has one value, one block, zero places and
zero explicit regions. The Body **section** for its single row is:

```text
00 00 00 00  35 00 00 00
01 00 00 00  00  01 01 00 00 01
01 00 00 00
01 00 01  00 01  0C 00 00 00
00 00 00 00  12 00 00 00  10 00 00 00
00 00 00 00 00 00 00  01 01 01  00 01  01 01 01 2A  83 01
```

The block-offset array is eight bytes and the terminator-offset array
four; the Body row is 53 bytes. It contains owner row 1, `bodyLayout=0`,
five one-byte counts, a fixed `u32` map entry for TypeSpec row 1,
one `(t1,value,useBlockCount=1)` descriptor, definition starts `[0,1]`,
the `ConstBits` definition offset 12, block offsets `[0,18]`,
terminator offset `16`, and an 18-byte block. That block has region 0, no
parameters, live values or places, a last use of `r1` at its return,
one `ConstBits` operation and one `Return`. Including the eight-byte
section row index, the Body section is 61 bytes. With eight populated
metadata sections, the artifact is **365 bytes** before alignment: a
32-byte header, eight 16-byte directory entries and 205 bytes of sections.
The direct definition/use indexes increase this tiny Body section from
the former 50 bytes to 61 bytes; their cost on representative bodies
still needs measurement before approval. This is encoded size, not throughput;
IDs, prefixes, blocks, decoding, branches, debug data and alignment also
affect cost.

## Tool roles and stabilization blockers

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
`01..0A`, `15..18` and terminators `80..83`, `87` are executable with their stated
type, state and failure checks. `CheckedBinary` kinds 4 and 5 require
an integer divisor and use the selected module fail-fast policy on failure;
`CheckedConvert` accepts the assigned integer types and `char16` as a
non-arithmetic 16-bit code unit with range `0..65535`; it preserves
the mathematical value when in range. `Fail` terminates
under that policy. Only `runtimePolicy=0` is executable in this subset:
checked-exception dispatch requires a specified exceptional edge, payload
and cleanup protocol. All live values in this subset are Copy values, so a
failure cannot strand a unique cleanup obligation.

`0B..14`, `19..1C`, `8A`, and nonzero place counts are **assigned
decode shapes, not executable forms**. They require place authority,
ownership, cleanup, call failure, backing or region rules that this revision
does not encode completely. The `02` lexical and `03..07` region kinds are
likewise reserved for execution. A structural inspector can identify each
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
| Exception and cleanup | Authored catch/finally, cleanup on normal/exceptional exit, fault replacement, operation-site failure state (including calls) and required roots through cleanup. A potentially failing call without a local handler is insufficient. |
| Fast method facts | The Body now assigns direct value definitions, per-block last-use inventories and per-value/place use-block counts. Still define the mandatory method-level operation/effect summary, finer per-block effects and safepoint/root inventory, with bounds and consistency audits. Specialized branch reachability and per-consumer profitability are computed at the call site, not serialized as authoritative facts. |
| Loans and views | Place provenance, region escape/alias checks, retained backing and owner dependencies, interior references, `mut` versus `write`, suspension/capture and explicit begin/end loan forms. |
| Managed values | Root maps at safepoints, stable descriptors, write barriers, allocation/collection, optional handles and per-image heap boundaries. Root lifetime cannot be inferred solely from SSA uses. |
| Logical root inventory | Specify producer-recorded, directly indexed safepoint locations and live logical root/owner/interior-place dependencies (including exceptional continuation and cleanup), with precise lifetime and verification rules. The format should avoid making each interpreter/JIT reconstruct logical GC liveness from every use; it must not prescribe post-allocation native register/stack maps or assume that ordinary value liveness covers roots. |
| Published contracts | Full effects, ownership/transfer, correlated output states, pre/postconditions, capability and witness evidence, generic specialization/export metadata and layout/ABI compatibility. Metadata clauses must be specified together with verifier inputs. |
| Operations | Checked indexing/backing, raw/unaligned and atomic memory order, floating/SIMD/tensor semantics, indirect calls, traits/witness dispatch, intrinsics, async state and typed failure values. Assign only operations whose exact type, failure, ordering and lifetime rules are known. |
| Revision and reader policy | No on-wire selector or feature/revision negotiation for the candidate executable subset; nonminimal-width load policy, unknown opcodes in unvisited trusted blocks, diagnostic/recovery behavior and numeric parser/audit budgets remain open. |
| Type-map conformance | A complete reader must validate each fixed-`u32` typed type reference, distinct map entries, local type IDs and type agreement with MethodSig and selected definitions. The candidate inline built-in words require a metadata format revision and explicit identity checks across indexed and inline forms; no Body-local ID may be mistaken for an artifact-wide metadata reference. |

These are explicit blockers to declaring the proposals final, not an
invitation to invent semantics or serialize the current private bound tree.
See [implementation](../implementation.md) for what the repository actually
executes. The source-language [portable proof floor](../language-contract-review.md#4-a-reproducible-proof-and-evaluation-foundation)
is distinct from decoder and verifier work budgets.
