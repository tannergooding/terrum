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
operations and one terminator; each edge supplies its successor's parameters.
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

Numeric policy (checked, wrapping, saturating, bit reinterpretation) is
explicit; converting one into another is not a space-saving normalization.
A discarded result does not discard an effectful call. Typed source binding
selects one MethodDef before encoding; no reader reruns overload resolution.
Operations absent from the assigned tables reject rather than being guessed
from CIL or CLR behavior.

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
parameters or operations define its value. `B1`–`B3` are one-based
block IDs. A block's parameter list declares values received on entry,
not all values that its operations will define.

```text
Source:   int32 AddThenThree(int32 x, int32 y) { return x + y + 3; }

TypeSpec[1] = Builtin(int32)                // row payload 01 00 08
MethodSig[1] = (parameter[0]: TypeSpec[1], parameter[1]: TypeSpec[1])
               -> TypeSpec[1]              // x, y in source order
MethodDef[1] = (signature=1, bodyRow=1)
Body: ownerMethod=1, bodyLayout=0
      blockCount=3, valueCount=5, placeCount=0, regionCount=0, typeCount=1
      typeMap=[t1: TypeSpec[1]]             // fixed u32 typed reference
      valueDescriptors=[r1:(t1,value), r2:(t1,value), r3:(t1,value),
                        r4:(t1,value), r5:(t1,value)]
      blockOffsets=[0,16,35,46], terminatorOffsets=[10,13,9]
      B1 operationCount=0, B2 operationCount=1, B3 operationCount=0
      all blocks: region=0, liveIns=[], liveOuts=[], place lists=[]

B1(r1 <- parameter[0], r2 <- parameter[1]):  // x, y defined on entry
    CheckedBinary add r1, r2 -> B2()
B2(r3 <- first checked add):                 // implicit success value
    r4 = ConstSigned t1, 3
    CheckedBinary add r3, r4 -> B3()
B3(r5 <- second checked add):                // implicit success value
    Return r5
```

`ConstSigned` defines `r4` in B2 using the typed literal `3`; it is not
a block input. The first checked terminator supplies `r3` as B2's
first parameter; the second supplies `r5` as B3's first parameter.
Neither terminator defines a value in its own block, and neither
success edge carries an explicit argument. On first-add overflow B2
is not entered; on second-add overflow B3 is not entered. Both
failures follow the module policy. Every value has one descriptor
and one definition; the descriptor alone does not create a value.
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

The Body **section** uses one mapped type, five values, three blocks and
no places or regions. Its 106-byte structure is:

| Region | Size and contents |
|---|---|
| Body-section offset index | 8 bytes: `00 00 00 00 62 00 00 00` = `[0,98]`; not an instruction |
| Body row header | 24 bytes: `ownerMethod=1`, `bodyLayout=0`, counts `(block,value,place,region,type)=(3,5,0,0,1)`, `typeMap=[TypeSpec row 1]` and five `(t1,value)` descriptors |
| Body row offset arrays | 28 fixed-`u32` bytes: `blockOffsets=[0,16,35,46]`, `terminatorOffsets=[10,13,9]` |
| Block data | 46 bytes: B1 is 16 bytes; B2 is 19 bytes; B3 is 11 bytes |

Here `bodyLayout=0` selects one-byte Body-local widths. The owner,
type-map entries and offset arrays remain fixed `u32`; instruction
widths are independent, and none here has an `F0` prefix.
Body fields and validation rules are specified in
[Body record, direct block index and regions](#body-record-direct-block-index-and-regions).

The exact section bytes are the eight-byte offset index followed by the
98-byte Body row:

```text
00 00 00 00  62 00 00 00
01 00 00 00  00  03 05 00 00 01  01 00 00 00
01 00  01 00  01 00  01 00  01 00
00 00 00 00  10 00 00 00  23 00 00 00  2E 00 00 00
0A 00 00 00  0D 00 00 00  09 00 00 00
00 02 01 02 00 00 00 00 00 00  84 01 01 02 02 00
00 01 03 00 00 00 00 00 01  02 04 01 03  84 01 03 04 03 00
00 01 05 00 00 00 00 00 00  83 05
```

| Location | Opcode-specific schema, in byte order | Widths / layout | Encoded fields | Reading |
|---|---|---|---|---|
| B1 terminator | `84 CheckedBinary u8(kind) ref(rLeft) ref(rRight) edgeSuccess` | Common; ref=1, count=1; kind=`u8` | `84` opcode · `01` kind · `01` rLeft · `02` rRight · `02` targetBlock · `00` argumentCount | `kind=1` add; inputs `r1` (`x`) and `r2` (`y`); implicit result is B2's `r3` |
| B2 operation | `02 ConstSigned ref(rNew) ref(t) signedData(value)` | Common; ref=1, data=1 | `02` opcode · `04` rNew · `01` t · `03` signedData | Define `r4` as mapped `t1` value `3` |
| B2 terminator | `84 CheckedBinary u8(kind) ref(rLeft) ref(rRight) edgeSuccess` | Common; ref=1, count=1; kind=`u8` | `84` opcode · `01` kind · `03` rLeft · `04` rRight · `03` targetBlock · `00` argumentCount | Add `r3` and `r4`; implicit result is B3's `r5` |
| B3 terminator | `83 Return ref(result)` | Common; ref=1 | `83` opcode · `05` result | Return `r5`, the second checked result |

In the terminator schema, `edgeSuccess` expands to
`ref(targetBlock), count(argumentCount), ref[argumentCount]`; a zero
count means no trailing argument references. `CheckedBinary` encodes no
destination: each success value is its target's first parameter (`r3`,
then `r5`). `ConstSigned` carries signed data, not an add immediate.
By-value parameters need no place; an addressable mutable local needs
`PlaceLocal` then `Load`, with
`Assign` through that place on success. See the
[local-place shape](#value-and-place-state-through-checked-arithmetic),
which is decode-only in this revision.

For example, if B1's checked add instead targets block 256, it uses
`F0 01 84 01 01 00 02 00 00 01 00`: two-byte references widen both
operands and the target, while the count stays one byte. That instruction
prefix does not set the Body's structural widths; a real Body with 256
blocks also needs a two-byte local width for its `blockCount`.
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
return tmp + tmp; }`, the first checked add passes its implicit
`int32` result into B2 as `r3`. B2's second checked terminator uses
`r3` for *both* input operands and passes its new implicit result to
B3 for `Return`. Reuse needs no duplicate descriptor or `Copy`
operation for this Copy scalar; a unique value could not be duplicated
the same way.

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

For `int32 CountTo(int32 n) { mut int32 i = 0; while (i < n) {
i = i + 1; } return i; }`, MethodSig has one by-value `int32` parameter
and an `int32` result. The Body has `(5,7,0,1,2)` for the same five
counts, `ownerMethod` pointing to its MethodDef,
`typeMap=[t1:TypeSpec[1] (int32),t2:TypeSpec[2] (bool)]`,
and descriptors `r1,r2,r3,r5,r6,r7`
of `t1` with `r4` of `t2`, all by-value. Region 1 is
`(kind=loop,parent=0,entry=B2,exit=B5,cleanup=0)`; B2–B4 belong to
it, while B1 and B5 are at region 0:

```text
B1(r1 <- n):                       // liveOut=[r1,r2]
    r2 = ConstSigned t1, 0
    Branch B2(r2)
B2(r3 <- current i):               // liveIn=[r1], liveOut=[r1,r3]
    r4 = Binary Less r3, r1         // t2 bool
    BranchIf r4, B3(), B5(r3)
B3():                              // liveIn=[r1,r3], liveOut=[r1]
    r5 = ConstSigned t1, 1
    CheckedBinary add r3, r5 -> B4()
B4(r6 <- checked add):             // liveIn=[r1], liveOut=[r1,r6]
    Branch B2(r6)
B5(r7 <- final i):                 // no liveIn; outside loop
    Return r7
```

The header B2 receives its initial `i` from B1 and later iterations
from B4. `r1` is a dominating value carried across the backedge through
live-in/out sets, not an extra header parameter. The checked result is
B4's implicit first parameter; B4 then explicitly supplies `r6` to
the header. The false edge supplies `r3` to the loop exit. A loop
region and valid backedge do not make a failing add an implicit cleanup
or catch edge.

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
| `edge` | `ref targetBlock`, then `args` | Target's typed parameter list consumes the arguments, subject to implicit checked results |
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
| `places` | `placeCount` entries | local-width `typeId`, local-width `regionId`, `u8 flags` each |
| `regions` | `regionCount` entries | Described below, IDs `1..regionCount` |
| `valueDescriptors` | `valueCount` entries | local-width `typeId`, `u8 kind` each, indexed by `valueId - 1` |
| `blockOffsets` | `u32[blockCount + 1]` little-endian | Relative to the following block-data start |
| `terminatorOffsets` | `u32[blockCount]` little-endian | Each relative to its own block start; points directly to its terminator opcode or width prefix |
| `blockData` | `blockOffsets[last]` bytes | One block per consecutive offset extent |

Body-layout codes 0, 1 and 2 mean local widths of 1, 2 and 4 bytes; code 3
rejects. This width sizes all five counts, structural block/region/value/
place/type IDs, block parameters, place-state and operation counts, region
fields and place-region references. Choose the smallest width that
represents every field in this class throughout the **whole body**.
`ownerMethod`, type-map entries and both offset arrays remain fixed `u32`
typed references or offsets. Each instruction independently selects its
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
| `operationCount`, `operations` | `local`, then `operationCount` coded instructions | Definitions and state transitions in source order |
| `terminator` | One coded terminator | Explicit successor/return/failure; no fall-through |

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
This permits ordinary dominating values while typing merge values and
checked results as block parameters.

Place lists are strictly increasing, duplicate-free; inherited and starting
places are disjoint. Each place has one declaration and lexical start block,
which may execute repeatedly in a loop. Only active places may be addressed.
For each normal edge, apply operations and end-place transitions to the
inherited and started sets; the result must equal the successor's inherited
set. Successor starts are not inherited, and all predecessors must agree.
An initialized place may end only if its type is Copy and needs no cleanup;
otherwise explicitly `Move` or `Drop` it. `Return` requires an empty set.
These records expose local state at block boundaries. Failure still needs
separate cleanup/root rules; place metadata cannot create an unwind edge or
enable unsupported operations.

Value IDs are gapless unique definitions in `1..valueCount`, assigned in
block/operation order: block parameters first, then result-producing
operations. An implicit checked result is the target's first parameter,
not a terminator definition. Direct
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
| `BodyIndex` | `ownerMethod`, body layout, five counts, fixed-`u32` TypeSpec map, place descriptors, region records, value descriptors, block/terminator offsets | Local IDs and offsets remain relative to this one body; `ownerMethod` is a metadata row |
| `ValueDescriptor` | Body-local type ID and value/place kind | Entry `i` describes `r(i+1)`; resolve its type through the validated map without decoding the defining operation |
| `BlockLocation` | Block-data start + `blockOffsets[i-1]`, next block offset, relative terminator offset | Entry `i-1` addresses block ID `i`; terminator lies inside this block's extent |
| `BlockHeader` | Region, parameter IDs, live-in/out IDs, inherited/starting/ending place states, operation count | Parameters define values; live sets refer to prior definitions, not machine registers |
| `Instruction` | Registered opcode, three resolved widths and the opcode-specific typed operands | The wide prefix changes the byte widths only; do not store raw bytes as if they were an addressing mode |
| `Terminator` | Registered opcode and its typed edge/result/failure payload | Directly indexed; each normal edge targets a block ID and transmits the listed arguments |

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
| Checked terminator | First target parameter(s) receive typed computed results; the terminator defines no additional ID in its own block |

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
| `04` | `Unary rNew, u8(kind), r` | Kind 1 bool NOT, 2 integer bitwise NOT, 3 integer unary plus. Checked negation uses a failure terminator. |
| `05` | `Binary rNew, u8(kind), rLeft, rRight` | Kinds 1/2/3 bitwise AND/OR/XOR, 4/5 equality/inequality, 6/7/8/9 ordered `<`/`<=`/`>`/`>=`, 10/11/12 explicit wrapping add/subtract/multiply. Only built-in scalar operations; no invented user witness. |
| `06` | `Convert rNew, tDest, u8(policy), rSource` | Policy 1 integer wrapping, 2 integer saturation, 3 checked bit-preserving reinterpretation of eligible equal-size representations. Checked range conversion uses a failure terminator. |
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

The `u8(kind)` following `Unary`, `Binary` or `CheckedBinary` is one
of these **exact** assignments. Every other value, including zero,
rejects for that opcode. `Binary` produces an `rNew` in the same block;
`CheckedBinary` instead produces its result as its success block's
first parameter:

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
| `84` | `01` | CheckedAdd | Equal integer types | Same type on success; failure if mathematical sum is out of range |
| `84` | `02` | CheckedSubtract | Equal integer types | Same type on success; failure if mathematical difference is out of range |
| `84` | `03` | CheckedMultiply | Equal integer types | Same type on success; failure if mathematical product is out of range |
| `84` | `04` | CheckedDivide | Equal integer types | Same type on success; zero divisor or signed minimum / `-1` fails |
| `84` | `05` | CheckedRemainder | Equal integer types | Same type on success; zero divisor fails, signed minimum % `-1` is zero |
| `84` | `06` | CheckedShiftLeft | Integer left, `uint32` count right | Same left type; count outside `[0,width)` fails, bits shifted out are discarded |
| `84` | `07` | CheckedShiftRight | Integer left, `uint32` count right | Same left type; count outside `[0,width)` fails, sign-fill for signed, zero-fill for unsigned |

`Binary` defines its result with `rNew`; `CheckedBinary` has no result
operand and supplies its result as the success target's first parameter.
The opcode/kind pair selects behavior; operand types supply width and
signedness, and the target parameter must match the result type.

The other discriminants are `Convert.policy`: `01` integer wrap,
`02` integer saturate, `03` equal-width integer bit reinterpretation;
and `Fail.reason`: `01` requirement, `02` overflow, `03` division by
zero, `04` invalid shift count. These are separate enum domains even
when the same byte value occurs. `CheckedConvert` has **no policy
byte**: it performs a checked range conversion with `tDest` before its
source `r` and sends the result to its success edge. Wrapping converts
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

The executable scalar set is `bool`, `char16`, `int32`, `uint32`, `int64`
and `uint64`; integer arithmetic uses the declared width. `Binary`
comparisons require equal complete operand types and return `bool`; their
kind does not imply a user equality witness. `ConstBits` and `ConstSigned`
carry explicit Body-local type IDs resolving to TypeSpec; arithmetic reads types from value
descriptors and must match the result descriptor. No instruction assumes
a default integer type or implicitly narrows, promotes, loads, updates or
swaps operands. `Load` produces a typed Copy value; `Assign` writes through
a place only after a successful result edge. Unsupported floating, vector,
atomic, raw-pointer and text operations have no numeric opcode.

`Convert` policy 1 wraps the mathematical value modulo the destination
width and interprets those bits in the destination's signedness; policy 2
clamps to its range. Policy 3 preserves bits only between equal-width
integer primitives; it cannot fabricate `bool`, `char16`, optional or
nominal values. `Unary` NOT and `Binary` bitwise/wrapping kinds apply only
to integers; Boolean NOT, equality and comparisons have separate rules.
`Switch` patterns must be canonical `ConstBits` patterns of the selector
type, without truncated high bits or duplicates. Other effectful or
potentially failing operations require a specified terminator or future
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
| `84` | `CheckedBinary u8(kind), rLeft, rRight, edgeSuccess` | Kinds 1..5 checked add/subtract/multiply/divide/remainder; 6/7 checked-count left/right shift. Computed result is an *implicit first* success-block parameter, before the edge's explicit arguments; numeric/count failures dispatch the selected policy. |
| `85` | `CheckedConvert tDest, rSource, edgeSuccess` | Checked numeric range conversion; implicit first success parameter is `tDest`. |
| `86` | `Invoke m, args, edgeSuccess` | Potentially failing exact selected call. A non-void result is the implicit first success parameter; failure departs under runtime policy. Authored handlers require a future exception-region encoding. |
| `87` | `Fail u8(reason)` | Declared requirement failure (`1`), overflow (`2`), division by zero (`3`), invalid shift count (`4`); no successor. |
| `88` | `CheckedNegate rOperand, edgeSuccess` | Checked signed negation with implicit first success parameter. |
| `89` | `CheckedIndex rBase, rIndex, edgeSuccess` | Bounds/validity check and implicit first result place; requires a specified indexed-backing feature before emission. |
| `8A` | `Leave count(exitCount), regionId[exitCount], edge` | Reserved for explicit scope/cleanup exit; not executable until its unwind contract is assigned. |
| `8B` | `CheckedDivRem rLeft, rRight, edgeSuccess` | One evaluation and failure decision produces quotient and remainder as the first **two** success-block parameters, in that order, before explicit edge arguments. |
| `8C` | `CheckedRange rBase, rStart, rCount, edgeSuccess` | Reserved: validates a contiguous range once and produces a bounded view on success; backing, access and loan rules require a future feature. |
| `8D` | `CheckedReadIndex rBase, rIndex, edgeSuccess` | Reserved: bounds check and Copy element read form one ordered failure boundary, returning a value without publishing an intermediate place. |

Failure never becomes an unchecked result or `None`. A potentially failing
terminator is forbidden in a `NoThrow` method unless a specified static
proof rules out failure; `(1, 0)` defines no such proof. `Invoke` checks the
selected signature, effects, argument modes and return ownership. An
implicit result adds one first parameter to the success target, before
explicit edge arguments. Failure policy does not create a catch or cleanup
edge, so a caller needing cleanup on failure is unsupported. `CheckedIndex`
and `Leave` are reserved and rejected by `(1, 0)`.

`CheckedBinary` kinds 1..5 use equal integer types; kinds 6/7 take a
`uint32` right operand as the shift count. Signed negation fails at the
minimum. Checked conversion preserves the exact value or fails, with no
numeric fallback. Explicit edges and loop
regions define the CFG; block and terminator offsets provide direct access.
Trusted consumers may use those indexes and live sets; an optional audit
decodes operations and recomputes predecessors, dominance and liveness.
Neither may assign new meaning to an unknown opcode.

`CheckedDivRem` takes equal complete integer types. It checks a zero
divisor or signed minimum divided by `-1` once, then returns same-typed
quotient and remainder. Quotient truncates
toward zero; remainder follows the dividend, and the target has
`edge.argumentCount + 2` parameters. `CheckedBinary` remainder does not
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
profile: `r1` is its parameter and `r3` is the implicit checked result
in B2. The alternative below adds an addressable local place and is
decode-only in this revision.

For `local = local + 5`, instead declare a mutable, initially vacant
`int32` place `p1` in the body place table. Its header and operations have
this decode-only shape:

| Block | Value parameters | Live-in / live-out values | Inherited places | Starts / ends | Operations and terminator |
|---|---|---|---|---|---|
| B1 | None | None / `r1` | None | Start `p1` vacant / end none | `r1 = PlaceLocal p1`; `r2 = ConstSigned t1, 10`; `Initialize r1, r2`; `r3 = Load r1`; `r4 = ConstSigned t1, 5`; `CheckedBinary add r3, r4 -> B2()` |
| B2 | `r5` (descriptor `t1`, value), implicit checked result | `r1` / none | `(p1,initialized)` | Start none / end `p1` | `Assign r1, r5`; `Return r5` |

The successful edge carries initialized state for `p1` and keeps its
handle `r1` live, but not the local's old contents. `r4` dies in B1; `r5`
is both assigned and returned. `PlaceLocal`, `Initialize`, `Load` and
`Assign` lack complete place and failure-path rules, so this shape is not
executable. `PlaceField` produces a place; `Extract` reads a Copy field
from a by-value aggregate.

The encoded Body section is 106 bytes. Its `05` place flags mean mutable
and initially vacant; block offsets are `[0,33,51]`, and terminator
offsets are `[27,16]`:

```text
00 00 00 00  62 00 00 00
01 00 00 00  00  02 05 01 00 01  01 00 00 00  01 00 05
01 01  01 00  01 00  01 00  01 00
00 00 00 00  21 00 00 00  33 00 00 00
1B 00 00 00  10 00 00 00
00 00 00 01 01 00 01 01 00 05
0B 01 01  02 02 01 0A  0E 01 02  0D 03 01  02 04 01 05
84 01 03 04 02 00
00 01 05 01 01 00 01 01 01 00 01 01 01
0F 01 05  83 05
```

B1 has live-out `r1`, starts vacant `p1`, and has five operations. B2 has
parameter `r5` (descriptor 5), live-in `r1`, inherited initialized place
`(p1,1)`, ends `p1`, and has one operation. These headers and the
terminator index are directly readable; they do not authorize executing
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
00 00 00 00  2A 00 00 00
01 00 00 00  00  01 01 00 00 01
01 00 00 00
01 00
00 00 00 00  0E 00 00 00  0C 00 00 00
00 00 00 00 00 00 00 01  01 01 01 2A  83 01
```

The block-offset array is eight bytes and the terminator-offset array
four; the Body row is 42 bytes. It contains owner row 1, `bodyLayout=0`,
five one-byte counts, a fixed `u32` map entry for TypeSpec row 1,
one `(t1,value)` descriptor, block offsets `[0,14]`,
terminator offset `12`, and a 14-byte block. That block has region 0, no
parameters, live values or places, one `ConstBits` operation and one
`Return`. Including the eight-byte section row index, the Body section is
50 bytes. With eight populated metadata sections, the artifact is
**354 bytes** before alignment: a 32-byte header, eight 16-byte directory
entries and 194 bytes of sections. This is encoded size, not throughput;
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
| What crosses a block boundary? | Typed merge parameters, sorted live-in/out value IDs, active place/state joins | Fast tier can allocate slots directly; optimizing tier can rebuild SSA, liveness or register allocation if profitable. |
| Which loops and failures are present? | Region headers/exits, typed success edges, explicit ordered failure terminators | Lower failure/cleanup and optimize loops without treating hints as a prescribed machine CFG. |
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
`01..0A` and terminators `80..85`, `87..88`, `8B` are executable with their stated
type, state and failure checks. `CheckedBinary` kinds 4 and 5 require
an integer divisor and use the selected module fail-fast policy on failure;
`CheckedConvert` accepts the assigned integer types and `char16` as a
non-arithmetic 16-bit code unit with range `0..65535`; it preserves
the mathematical value when in range. `Fail` terminates
under that policy. Only `runtimePolicy=0` is executable in this subset:
checked-exception dispatch requires a specified exceptional edge, payload
and cleanup protocol. All live values in this subset are Copy values, so a
failure cannot strand a unique cleanup obligation.

`0B..14`, `86`, `89..8A`, `8C..8D`, and nonzero place counts are **assigned
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
| Exception and cleanup | Authored catch/finally, cleanup on normal/exceptional exit, fault replacement, call-site unwind successors and required roots through cleanup. `Invoke` without a local handler is insufficient. |
| Loans and views | Place provenance, region escape/alias checks, retained backing and owner dependencies, interior references, `mut` versus `write`, suspension/capture and explicit begin/end loan forms. |
| Managed values | Root maps at safepoints, stable descriptors, write barriers, allocation/collection, optional handles and per-image heap boundaries. Root lifetime cannot be inferred solely from SSA uses. |
| Published contracts | Full effects, ownership/transfer, correlated output states, pre/postconditions, capability and witness evidence, generic specialization/export metadata and layout/ABI compatibility. Metadata clauses must be specified together with verifier inputs. |
| Operations | Checked indexing/backing, raw/unaligned and atomic memory order, floating/SIMD/tensor semantics, indirect calls, traits/witness dispatch, intrinsics, async state and typed failure values. Assign only operations whose exact type, failure, ordering and lifetime rules are known. |
| Revision and reader policy | No on-wire selector or feature/revision negotiation for the candidate executable subset; nonminimal-width load policy, unknown opcodes in unvisited trusted blocks, diagnostic/recovery behavior and numeric parser/audit budgets remain open. |
| Type-map conformance | A complete reader must validate each fixed-`u32` typed type reference, distinct map entries, local type IDs and type agreement with MethodSig and selected definitions. The candidate inline built-in words require a metadata format revision and explicit identity checks across indexed and inline forms; no Body-local ID may be mistaken for an artifact-wide metadata reference. |

These are explicit blockers to declaring the proposals final, not an
invitation to invent semantics or serialize the current private bound tree.
See [implementation](../implementation.md) for what the repository actually
executes. The source-language [portable proof floor](../language-contract-review.md#4-a-reproducible-proof-and-evaluation-foundation)
is distinct from decoder and verifier work budgets.
