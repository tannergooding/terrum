# Terrum coded instruction proposal

> [!IMPORTANT]
> Candidate coded-instruction design, not the current semantic-module writer or
> an approved execution profile. The unencoded contracts at the end are
> stabilization blockers, not permissions to erase their semantics.

The [approved current metadata design](terrum-il-metadata-spec.md) defines the Body table,
TypeSpec, MethodDef, contracts and artifact framing. This document specifies
the Body payload and operation stream separately, following only the
*organizational* distinction between ECMA-335 Partitions II and III. It does
not adopt CIL opcodes, its evaluation stack, branch displacement formats,
metadata tokens or runtime ABI.

## Model and canonical form

A body implements one MethodDef. A **block** has typed parameters, ordered
operations and one terminator; an edge supplies its successor's parameters.
A **value ID** denotes one immutable, typed definition. A **place** denotes
addressable storage with separate initialization, mutability and ownership
state; loading a value is not the same as borrowing or moving its place. A
**region** groups blocks for loop structure or lexical lifetime. Only the
function region (ID 0) is implicit. Source spans are optional SourceMap rows;
they never determine execution.

The interchange form is already normalized enough to avoid recovering basic
blocks from branch byte offsets, loop headers from arbitrary backedges, or
local lifetimes from source names. It is **not** an optimizer's canonical SSA:
block parameters replace implicit stack merges, but place writes, calls and
failures remain ordered effects. An interpreter may execute verified blocks;
a JIT or AOT compiler may build its own lowered IR. Neither may assume the
encoded loop/region facts are true for untrusted input without validation.
Trusted producers may supply these facts directly to a fast consumer;
semantic audit remains an explicit optional mode.

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

The `layout` byte selects widths independently by operand class; bits
6–7 must be zero:

| Bits | Operand class | Codes `0`, `1`, `2`, `3` |
|---|---|---|
| 0–1 | Reference (`r`, `p`, `t`, `m`, block, region, field row) | 1, 2, 4 bytes; `3` invalid (IDs are at most `u32`) |
| 2–3 | Count (argument, case, exit count) | 1, 2, 4 bytes; `3` invalid |
| 4–5 | Data (signed literal or unsigned bit pattern) | 1, 2, 4, 8 bytes |

All operands of a class in **one instruction** use its selected width;
other instructions choose independently. A class unused by an instruction
has code 0. Fixed enumerations (`kind`, `policy`, `reason`) are always `u8`,
not references, counts or data; only their assigned values are valid.
References and counts are unsigned; data is unsigned for `bits`/`Switch`
patterns and two's-complement signed for `ConstSigned`. Each class uses the
smallest listed width that represents *all* its operands in the instruction.
Unsigned data zero-extends, signed data sign-extends to 64 bits. A prefixed
instruction with `layout=0` or a reserved bit rejects; a nonminimal
class width is noncanonical and fails a strict audit. This makes the
default a direct opcode dispatch and bounds all
wider reads without a continuation loop per operand. It does not imply that
reading or validating a variable-length stream is free.

Opcodes `0x01..0x7F` are operations and `0x80..0xEF` are terminators;
`0xF0` is the width prefix, not a standalone operation. `0x00` and
`0xF1..0xFF` reject in this revision. A prefix must be followed by its
layout and a registered opcode *within the same block extent*. Operands
follow in the opcode's specified order, without a per-instruction length
header. Unknown opcodes reject semantic loading; an apparent length or
prefix cannot authorize skipping unknown executable semantics. Counts are
bounded against the remaining block extent before allocation or traversal;
the known opcode layout and operation count determine the terminator start.
This is a regular `prefix, layout, opcode, operands` escape for uncommon
widths rather than four opcode assignments for every semantic operation.
It preserves one-byte dispatch for the common form without multiplying
the opcode catalog by reference/count/immediate width combinations.
An interpreter may dispatch directly on `(opcode, layout)` or decode to
native-width arrays; those alternatives require measured cost evidence.
IDs `1..255` use the common form; `256..65535` need a two-byte reference,
and only larger bodies need four-byte references. A prefixed instruction
costs two extra bytes, but fixed widths avoid continuation decoding per
operand. This choice prioritizes predictable dispatch and direct indexes
for ordinary small methods over minimizing every large literal or method
body. Explicit block transfers, place-state joins and loop headers also
save trusted consumers from rediscovering some dataflow. No
decode-throughput or total-size win is asserted.

The following are operand *kinds*, not additional on-wire tags. `ref`
means the selected reference width, `count` the selected count width,
`data` the selected data width, and `u8` exactly one byte:

| Operand notation | Decode | Namespace and zero rule |
|---|---|---|
| `r`, `rNew`, `rPlace` | `ref` | Body-local value ID `1..valueCount`; `rNew` defines the next ID, `r` uses one, and `rPlace` must denote a place-valued ID |
| `r?` | `ref` | Value ID or zero only when the declared result is void |
| `rNew?` | `ref` | New value ID, or zero iff the selected callable returns void; zero defines nothing |
| `p` | `ref` | Body-local place descriptor ID `1..placeCount` |
| `t` | `ref` | Artifact-wide TypeSpec row ID interpreted in the body module's context, never a primitive code in place of a row ID |
| `m` | `ref`, then `u32 DefId` only when `m=0` | Nonzero: one-based MethodDef row within the body module's MethodDef range, resolved by checked `methodDefStart + m - 1`; retained imports use its bound expected MethodDef. Zero: resolved direct call to an authored MethodDef in another packed module, with an exact little-endian MethodDef `DefId` following the selected-width zero. Invalid in a single-module artifact |
| `fieldDefRow` | `ref` | Artifact-wide FieldDef row ID for the selected owner type; a trusted merger may bind it directly to a field of a packed producer's resolved TypeDef |
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

## Body record, direct block index and regions

A Body table row is the bytes between adjacent Body-section offsets, with
no further row-length prefix. Its fields, in order, are:

| Field | Encoding | Constraint |
|---|---|---|
| `ownerMethod` | `u32` little-endian | Local MethodDef row whose `bodyRow` points here |
| `bodyLayout` | `u8` | Bits 0–1 local width; bits 2–3 metadata width; bits 4–7 zero |
| `blockCount`, `valueCount`, `placeCount`, `regionCount` | four local-width unsigned integers | Body-local counts; blockCount is positive |
| `places` | `placeCount` entries | metadata-width `typeSpec`, local-width `regionId`, `u8 flags` each |
| `regions` | `regionCount` entries | Described below, IDs `1..regionCount` |
| `valueDescriptors` | `valueCount` entries | metadata-width `typeSpec`, `u8 kind` each, indexed by `valueId - 1` |
| `blockOffsets` | `u32[blockCount + 1]` little-endian | Relative to the following block-data start |
| `terminatorOffsets` | `u32[blockCount]` little-endian | Each relative to its own block start; points directly to its terminator opcode or width prefix |
| `blockData` | `blockOffsets[last]` bytes | One block per consecutive offset extent |

Body-layout width codes 0, 1 and 2 mean 1, 2 and 4 bytes; code 3 rejects.
The local width applies to the four counts, structural block/region/value/
place IDs, block parameter, place-state and operation counts, region fields
and place region references. Metadata width applies to structural TypeSpec
rows (places and value descriptors). Each width is the smallest capable of
representing every field in its class throughout the **whole body**;
unused classes select code 0. The `ownerMethod`, `blockOffsets` and
`terminatorOffsets` remain fixed `u32`, independent of `bodyLayout`. An instruction's
operand width is independent of the enclosing body width and is selected by
its own prefix, not by `bodyLayout`. Readers bound all fields before use.
Canonical minimal widths are a writer obligation; strict readers may
additionally audit them. Unsupported
width codes, reserved bits and truncated fields always reject.

Offsets begin at 0, increase strictly (no empty blocks), and the last is
exactly the remaining body bytes. The body must therefore keep block data
under 4 GiB; reader budgets can be lower. Blocks are directly addressable
by ID without scanning operations. Offsets do not replace validation:
each decoded block consumes its exact extent. An invalid block index,
arithmetic overflow, overlap, trailing byte or duplicate body owner rejects.
Each terminator offset must follow exactly the counted operations and leave
exactly one complete terminator before its block end. A CFG reader can
address terminators without first decoding every operation; a trusted
consumer can use the producer's direct offset after bounded decoding,
whereas a semantic audit compares it against the operation stream.
Body rows follow the owning MethodDef `bodyRow` association, not a native
code-address order. A linker may relocate metadata rows only by rewriting
all references and revalidating the result.

Places are numbered `1..placeCount`. Flag `0x01` permits rebinding the place,
`0x02` indicates unique storage and `0x04` indicates storage that starts
vacant (including `out`); other bits reject. The complete TypeSpec determines referent access
and representation; these flags cannot grant `write` through a readonly
handle. Non-value parameters map to their declared places in parameter
ordinal order, followed by locally created places. By-value parameters are
entry-block values instead. Place descriptors name storage, not an exposed
address or a native stack slot.

Value descriptor kind `0x00` is a by-value result of its complete
`typeSpec`; `0x01` is a place-valued handle whose `typeSpec` is its
**referent**, not a loaded value or an encoded native pointer. Other
kinds reject. A place-valued handle's access, owner and region still
derive from its defining place operation; the descriptor does not grant
access. The typed array is indexed directly for imports, interpreter
slots, block live sets and a JIT's fast path without parsing preceding
operations to infer types. A writer must make each descriptor agree
with its parameter or defining instruction; an optional semantic audit
checks the agreement. `void` has no descriptor. Ordinary by-value `r`
operands (including numeric, `Copy`, `Select` and scalar edge arguments)
require kind 0; operands named `rPlace`/`rBasePlace` require kind 1.
`PlaceLocal`, `PlaceField` and the reserved `CheckedIndex` produce kind 1;
`Load` obtains a distinct kind-0 value. Copying a Copy *referent* does
not duplicate its place authority or extend its loan. Future callable
parameter modes require separately assigned reference/loan operands,
not reinterpretation of a kind-0 argument.

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

Block ID is its one-based index in `blockOffsets`; region 0 is the
function root. Entry block is ID 1. Its by-value parameters correspond
in order and complete type to the MethodSig's by-value parameters; a
receiver and non-value parameters use their declared place/receiver
contract, not a fabricated value parameter. Other blocks receive precisely
their incoming-edge types and arity. The entry block starts each
non-value parameter place with the state selected by its mode and
descriptor; it has no inherited places.

The `liveIns` and `liveOuts` are strictly increasing value IDs, not a
duplicated encoding of block-parameter definitions. Each listed ID refers
to an existing value with a type determined by its definition. `liveIns`
must be available on **every** incoming normal edge. `liveOuts` are the
union of successor `liveIns` and explicit edge arguments; no return or
failure has live-outs. Every cross-block direct value use and transit
through a block must be accounted for by these lists; values defined
here need no `liveIn` entry. An optional audit checks use/definition,
successor transfer, dominance and exact lists. This avoids mandatory
parameter renaming for ordinary dominating values while keeping merge values
and checked-operation results as block parameters typed by descriptor.

All three place lists are strictly increasing by ID with no duplicates;
`inheritedPlaces` and `startPlaces` are disjoint. Every place has one
declaration and one lexical start block, possibly revisited at runtime in
a loop. Only active places may be addressed. On each normal outgoing edge,
apply the block's operations and end list to its inherited and started
places: the resulting sorted `(placeId, state)` set **equals** the
successor's inherited list. Starts in the successor are not inherited.
Every predecessor must agree with that successor's list. A place ending
while initialized is allowed only if its complete type is Copy and needs
no cleanup; a unique owner requires an explicit `Move` or `Drop` first.
On `Return` the resulting set is empty. This records active local storage
and its vacancy at boundaries, rather than forcing each consumer to
discover lifetime joins from arbitrary place operations. An implicit
failure departure still needs its own cleanup/root rules; this place
metadata cannot manufacture an unwind edge or make unsupported place
operations executable.

Value IDs are unique definitions in `1..valueCount`, with no holes: block
parameters and result-producing operations each define one. IDs are allocated
in block-ID/operation order, including block parameters before operations.
An implicit checked-operation success result is its target block's first
parameter, not an extra definition at the terminator. A definition can be
used directly in its own block or across blocks when it dominates every
use and is declared live through all crossed normal
edges. Distinct incoming definitions must instead become typed block
parameters with explicit edge arguments; a value sent as an edge argument
is live at that terminator. These boundaries avoid forcing a consumer to
discover cross-block temporaries by a backward whole-body use scan; they
do not certify Copy, cleanup or aliasing. A trusted consumer can use the
declared facts; an opt-in verifier can recompute them independently.
Block arguments to distinct
conditional successors represent mutually exclusive transfers. Passing the
same live unique value twice on a single edge, using it after move, or
implicitly copying a non-Copy value rejects. In the executable subset,
region entries are loops only. A block's `regionId`
is its innermost region; it belongs to all ancestors. A loop's header is
assigned directly to that loop. Its header dominates every member; outside
edges enter only at its header, every backedge within it targets that header,
and every edge leaving it targets its designated exit. Its exit is outside
the loop, in the parent region or an ancestor. Each loop has a reachable
backedge, each block is reachable from entry, and every CFG cycle is enclosed
by a valid loop region; an edge must not enter a nested loop except at its
header. These constraints exclude multi-exit loops and multi-level breaks
until an explicit exit encoding is assigned. An auditing verifier builds
the CFG and checks dominance, reachability and region nesting within its
work budget; trusted consumers may use the
recorded loop structure directly. It is not a cryptographic certificate.
Irreducible cycles have no encoding in this profile.

### Logical decoder records

An implementation can materialize these records directly from the
preceding fields. Names are descriptive, not a mandated host-language ABI:

| Record | Required data | Index/identity invariant |
|---|---|---|
| `BodyIndex` | `ownerMethod`, body layout, four counts, place descriptors, region records, value descriptors, block/terminator offsets | IDs and offsets remain relative to this one body; `ownerMethod` is a metadata row |
| `ValueDescriptor` | TypeSpec row and value/place kind | Entry `i` describes `r(i+1)`; type and kind are accessible without decoding its defining operation |
| `BlockLocation` | Block-data start + `blockOffsets[i-1]`, next block offset, relative terminator offset | Entry `i-1` addresses block ID `i`; terminator lies inside this block's extent |
| `BlockHeader` | Region, parameter IDs, live-in/out IDs, inherited/starting/ending place states, operation count | Parameters define values; live sets refer to prior definitions, not machine registers |
| `Instruction` | Registered opcode, three resolved widths and the opcode-specific typed operands | The wide prefix changes the byte widths only; do not store raw bytes as if they were an addressing mode |
| `Terminator` | Registered opcode and its typed edge/result/failure payload | Directly indexed; each normal edge targets a block ID and transmits the listed arguments |

The reader first bounds the Body row, its fixed fields, descriptor
arrays and offset indexes, then may look up any block or value by ID.
It decodes an operation using that opcode's schema and only then
advances to the next operation; a strict audit compares this traversal
to the terminator index and checks type/flow correspondence. A trusted
consumer can start at an indexed terminator, follow its edges and look
up value types without decoding unrelated block operations first.
The byte format prescribes these facts, **not** a particular importer
object graph, machine register assignment, optimization tier or cache.

## Type and definition model for operands

`t` refers to a complete row in the metadata design's
[TypeSpec table](terrum-il-metadata-spec.md#metadata-logical-tables-and-row-semantics),
not to a byte-sized `PrimitiveType` embedded in an instruction.
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

Qualifiers are part of type equality and do not disappear when moving
through an edge. An `r` has exactly one indexed value descriptor (and,
for a place-valued ID, referent type, access and provenance); a block
parameter's type is looked up by ID, while an operation's result must
agree with the descriptor according to the table below. The body place
descriptor supplies a local
place's referent TypeSpec; a place-valued `r` is **not** the loaded
referent value. A field result resolves its FieldDef against the
owner's nominal type. The by-value initial parameters derive their
types from MethodSig, and both declared and inferred forms must agree.
`Copy`, ownership and effects come from the declared type/call contracts,
not from the opcode or byte size. Nominal arithmetic/operator witnesses
remain selected MethodDefs invoked through registered call semantics,
not primitive `Binary` or `CheckedBinary` kinds. Transparent layout
changes ABI classification, not numeric identity.

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

The scalar executable profile requires each resulting type to be a
supported primitive or supported optional form and rejects place-valued
results. The remaining rows assign decoding and intended type relationships
for future profiles; they do not make those profiles executable today.

## Operation encoding and normalized effects

All operand notation follows the encoding table above. Each listed
opcode number is a byte, not a source-language operator spelling.
The table assigns decode shapes; the executable subset is narrower,
as specified after the opcode tables. Assigned but unsupported shapes
can be inspected but must not be emitted as executable `(1, 0)` bodies.

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

For the compact `int32` values `r1` and `r2`, `05 03 0A 01 02`
defines `r3 = WrappingAdd(r1,r2)` (no failure edge), whereas
`84 01 01 02 02 00` means `CheckedAdd(r1,r2)` with a success edge to
B2, no explicit arguments, and its result as B2's first parameter.
The opcode/kind pair, not an inferred signedness bit or a nearby store,
decides whether overflow fails. Signedness and width come from the
input value types, and the target parameter must match them.

The other discriminants are `Convert.policy`: `01` integer wrap,
`02` integer saturate, `03` equal-width integer bit reinterpretation;
and `Fail.reason`: `01` requirement, `02` overflow, `03` division by
zero, `04` invalid shift count. These are separate enum domains even
when the same byte value occurs. `CheckedConvert` has **no policy
byte**: it performs a checked range conversion with `tDest` before its
source `r` and sends the result to its success edge. Wrapping converts
the mathematical integer modulo the destination bit width; saturation
clamps to its range. Neither is an alternate decoding of `CheckedConvert`.

The paired sources of `Binary` kinds `01..05`, `0A` and `0C` and
`CheckedBinary` kinds `01` and `03` are commutative **as already computed
values**. Swapping their source IDs does not authorize changing the
evaluation order of instructions that created those values. `NoThrow`
is not purity: calls, place writes, failure terminators and cleanup
cannot be dropped or commoned merely because their result is unused.
A constant-folded checked operation must preserve the same success
value or ordered failure, not convert it to wrapping arithmetic.
There is no generic "any numeric add" opcode whose meaning changes
silently when a new primitive or SIMD type is registered.

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

The accepted scalar set is `bool`, `char16`, `int32`, `uint32`, `int64`,
`uint64`; integer arithmetic works in its exact declared width. `Binary`
comparison returns `bool`, and its operands have the same complete type;
operator kind does not imply a user-supplied equality witness. No
floating, vector, atomic, raw-pointer or text operation is mapped onto a
numeric opcode. Wrapping and saturation are explicit, not consequences of
unchecked machine code. A `Convert` bit reinterpretation requires identical
validated representation and cannot create an invalid value or handle.

`ConstSigned` and `ConstBits` each carry an explicit TypeSpec reference; an
arithmetic opcode instead reads its operand types from value descriptors
and must produce the matching result descriptor. It does not
encode another type token or assume a default integer type. A destination
place is not an arithmetic operand: `Load` obtains a typed Copy value and
`Assign` writes only after a successful result edge. No instruction
implicitly narrows, promotes, loads, updates or swaps its operands.
Integer wrapping converts the mathematical source value modulo the
destination's bit width and interprets the resulting bits in that signedness;
saturation clamps the mathematical value to the destination range. The
bit-preserving policy is restricted to equal-width integer primitive types;
Boolean, char and optional/nominal types cannot be fabricated by bitcast.
`Unary` bitwise NOT and `Binary` bitwise/wrapping kinds apply only to the four
integer primitives; Boolean NOT, equality and comparisons are typed
separately. Each `Switch` pattern is the canonical `ConstBits` bit pattern
for the selector type, with no truncated high bits or duplicate cases.
Effectful or potentially failing operations not named above must use a
specified failure terminator or a future feature, not `CallNoThrow`.

`Initialize`, `Assign`, `Move`, `Drop` and `Aggregate` require complete
flow-sensitive vacancy, ownership and cleanup verification over every
normal edge. Explicit Drop records the point of cleanup; a region marker
alone does not execute it. In this revision any implicit failure departure
with a live owner, loan, or required root rejects unless its cleanup and
unwind representation is specified. This is deliberately narrower than the
current compiler's private IR and does not weaken its executed guarantees.

## Terminators and CFG

An **edge** is reference-width `targetBlock` followed by `args`. Every target exists; supplied values
match its block parameters, in order. No fall-through or byte-relative
branches exist. In the following table `edge` has that shape, `r?` can be
zero only for void, and failure uses the module's declared fail-fast or
checked-exception policy:

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

Failure departures never become unchecked numeric results or `None`.
Checked operations with a potentially failing edge cannot occur in a
`NoThrow` method unless a separately specified static-proof rule establishes
that failure cannot occur; format `(1, 0)` makes no such exception. `Invoke`
still checks selected method signature, effects, argument modes and return
ownership. For an operation with an implicit success result, the target
parameter count is `edge.argumentCount + 1` and its first type is the result
type. The failure policy does not synthesize a catch region; if the caller
owns something requiring cleanup on failure, this profile cannot encode
the call. The `CheckedIndex` and `Leave` opcode assignments reserve their
shapes but are rejected by a `(1, 0)` executable reader.

For `CheckedBinary` kinds 1..5, operands share one of `int32`, `uint32`,
`int64`, `uint64`; zero divisors fail. Signed minimum divided by `-1`
overflows for division but has remainder zero. Shift kinds 6/7 take a
`uint32` count regardless of the left integer's width. Signed checked
negation fails at its minimum. Checked integer
conversion either preserves the exact value or fails; no implicit numeric
fallback type is involved. Explicit `Branch`/`Switch` edges and loop-region
records allow a tool to build a CFG without discovering block starts from
branch displacements. Block and terminator offsets give direct access
without first scanning operation streams. A trusted consumer can use
these offsets and the recorded live sets; an optional audit independently
decodes counted operations and recomputes predecessors, dominance and
liveness. Neither mode may interpret an unknown opcode as different
known semantics.

`CheckedDivRem` accepts equal complete integer primitive types and checks
zero divisor or signed minimum / `-1` once; it then supplies quotient
and remainder of that type. Quotient truncates toward zero, and remainder
follows the dividend. Its success target has `edge.argumentCount + 2`
parameters.
The remainder-only `CheckedBinary` form does **not** fail for signed
minimum % `-1`: that remainder is zero, so it cannot be silently rewritten
as a combined division/remainder. A producer may choose `CheckedDivRem`
when both results are needed; a consumer may fuse distinct operations
only when their failure order, uses and effects permit it. `CheckedRange`
would check `start <= length && count <= length - start`, with the empty
range at the end valid; later indexed accesses still need either a range-
relative proof or their own check. `CheckedReadIndex` groups the often
adjacent bounds check and Copy read so no invalid intermediate place
escapes. Neither range nor indexed-read form is executable in this
profile, pending backing/provenance, access and failure-order contracts.
A future registered intrinsic would need an exact semantic identity and
contract; a consumer must not infer these grouped semantics from a
callable's spelling or assume an arbitrary `CallNoThrow` is an intrinsic.

## Value and place state through checked arithmetic

A function returning `int32` with one by-value `int32` parameter starts with
its parameter as block value `r1`. The following is **executable** in the
narrow fail-fast `(1, 0)` profile; `t1` is the `int32` TypeSpec:

```text
B1(r1: t1): r2 = ConstSigned t1, 5
            CheckedBinary add r1, r2 -> B2()
B2(r3: t1): Return r3
```

The checked result is `r3`, **not** a hidden evaluation-stack value or an
in-place mutation of `r1`. On overflow there is no B2 edge: failure uses
the module policy. In the exact block header above, B1 has one parameter
ID `r1` (descriptor `(t1,value)`), zero inherited/start/end places and
one operation; B2 has parameter ID `r3` (descriptor `(t1,value)`),
zero inherited/start/end places and zero operations.
`r1` and the literal temporary `r2` have their last use at B1's
terminator; neither is live in B2. Both live-in and live-out lists are
empty in this example. The result becomes live **as B2's
parameter**. A reader finds its type in value descriptor 3, the checked
arithmetic kind in B1's terminator and its source types at the two B1
definitions. It does not infer an evaluation stack from machine code.

For `local = local + 5`, instead declare a mutable, initially vacant
`int32` place `p1` in the body place table. Its header and operations would
have the following shape (shown to expose the tables, **not** as an
executable `(1, 0)` body):

| Block | Value parameters | Live-in / live-out values | Inherited places | Starts / ends | Operations and terminator |
|---|---|---|---|---|---|
| B1 | None | None / `r1` | None | Start `p1` vacant / end none | `r1 = PlaceLocal p1`; `r2 = ConstSigned t1, 10`; `Initialize r1, r2`; `r3 = Load r1`; `r4 = ConstSigned t1, 5`; `CheckedBinary add r3, r4 -> B2()` |
| B2 | `r5` (descriptor `t1`, value), implicit checked result | `r1` / none | `(p1,initialized)` | Start none / end `p1` | `Assign r1, r5`; `Return r5` |

The successful B1 edge carries the initialized **place state** of `p1`
and keeps its place handle `r1` live through the edge, but does not carry
the local's old contents as a value. `r4` has its last use in B1; it is
not live in B2. The checked result `r5` is usable as both the assigned
Copy value and the return. If a dominating computed temporary also had
to survive, its ID would appear in B1's live-out and B2's live-in lists;
if distinct incoming definitions had to merge, B2 would instead define
a corresponding typed parameter with explicit edge arguments.
`PlaceLocal`/`Initialize`/
`Load`/`Assign` still lack complete place and failure-path rules, so the
shape is **not executable**. `PlaceField` would derive
an addressable field place from an owning place, whereas `Extract` returns
a Copy field value from an aggregate; neither is a silent register/address
mode of `Binary`. Folding a literal into a future add-immediate opcode
would have to preserve the same overflow and evaluation semantics.

The **decode-only** Body section for that local case, with `t1=int32`,
is 101 bytes. Its `05` place flags mean mutable and initially vacant;
the block offsets are `[0,33,51]`, and terminator offsets `[27,16]`:

```text
00 00 00 00  5D 00 00 00
01 00 00 00  00  02 05 01 00  01 00 05
01 01  01 00  01 00  01 00  01 00
00 00 00 00  21 00 00 00  33 00 00 00
1B 00 00 00  10 00 00 00
00 00 00 01 01 00 01 01 00 05
0B 01 01  02 02 01 0A  0E 01 02  0D 03 01  02 04 01 05
84 01 03 04 02 00
00 01 05 01 01 00 01 01 01 00 01 01 01
0F 01 05  83 05
```

B1's header has no incoming values or places, one outgoing live value
`r1`, one starting place `p1`, and five operations. B2's header has
the implicit result parameter `r5`, typed by descriptor 5, incoming live value `r1`,
inherited initialized place `(p1,1)`, ending place `p1`, and one
operation. A tool can read those facts before decoding either operation
stream; the terminator index locates the checked add and return directly.
The bytes establish a decode shape, **not** authority to execute the
unsupported place operations.

| Source operand shape | Proposed state transition before checked add |
|---|---|
| By-value parameter or computed temporary | Already has a typed immutable `r`; use it directly. |
| Integer immediate | `ConstSigned` or `ConstBits` defines a typed `r`; it is not inferred from a machine immediate or a default integer type. |
| Mutable local | `PlaceLocal` obtains its `rPlace`; `Load` defines a Copy `r`. On the successful edge, `Assign` writes the result through the place after verifying authority and state. |
| Field of an addressable owner | `PlaceField` derives a field place and `Load` obtains its value; for a Copy aggregate held by value, `Extract` returns a field value instead. Provenance and owner lifetime are not lost. |

Only the first two rows are executable in `(1, 0)`. An interpreter can
materialize values in body-local slots and places in verified storage;
JIT/AOT may choose registers or memory independently. The format does not
require a distinct add opcode for every source operand combination, nor
does it assume that a stored place is a native stack slot.

## Worked bytes and illustrative size

For [metadata example](terrum-il-metadata-spec.md#row-assignment-example),
MethodDef row 1 returns `int32` with no arguments; TypeSpec row 1 is
`int32`. Its `return 42` body has one value, one block, zero places and
zero explicit regions. The Body **section** for its single row is:

```text
00 00 00 00  25 00 00 00
01 00 00 00  00  01 01 00 00
01 00
00 00 00 00  0E 00 00 00  0C 00 00 00
00 00 00 00 00 00 00 01  01 01 01 2A  83 01
```

The index holds eight bytes of block offsets and four of terminator
offsets; it delimits a 37-byte body. The body
contains a four-byte owner row, a zero body layout, four one-byte counts,
one `(t1,value)` descriptor, the block-offset index `[0,14]`,
terminator offset `12`, and a 14-byte
block (region 0, zero parameters, empty live-in/out and place lists,
one `ConstBits` operation, one `Return` terminator). The Body section
occupies 45 bytes including its row offsets. With the eight populated
metadata sections in this example, the artifact occupies **349 bytes**
before alignment: 32-byte header + 8 * 16-byte directory + 189 bytes of
metadata/Body sections. This is an encoded-size calculation, **not** a
throughput measurement; wider IDs, prefixes, block counts, decode work,
branch count, debug information and alignment still matter.

For the preceding by-value checked-add function (assuming the stated MethodSig
and `t1`), the Body **section** uses three values, two blocks and no places or
regions. Block 1 is 19 bytes; block 2 is eleven. Its exact 73 bytes are:

```text
00 00 00 00  41 00 00 00
01 00 00 00  00  02 03 00 00
01 00  01 00  01 00
00 00 00 00  13 00 00 00  1E 00 00 00
0D 00 00 00  09 00 00 00
00 01 01 00 00 00 00 00 01  02 02 01 05  84 01 01 02 02 00
00 01 03 00 00 00 00 00 00  83 03
```

The counted operation is `02 02 01 05` (`ConstSigned r2, t1, 5`);
`84 01 01 02 02 00` is checked add of `r1` and `r2` with a
zero-explicit-argument edge to B2. B2's first parameter `r3` receives the
implicit result. For width selection, `ConstSigned r3, t1, 200` instead
uses `F0 10 02 03 01 C8 00` (two-byte signed data). If its result
ID were 256, the same instruction would use
`F0 11 02 00 01 01 00 C8 00` (two-byte references and data).
Neither prefix changes the operation's semantic identity.

An audit rejects nonminimal widths, a block-offset sentinel of 13 instead
of 14, an inaccurate terminator offset, duplicate values or an invalid
successor. All readers reject a truncated prefix, out-of-bounds index,
missing terminator or unknown opcode before executing it. An independent
decoder should round-trip the exact rows; an independent optional audit
should agree on their CFG, types and states. Same-writer/same-reader
round trips alone do not establish conformance.

## Tool roles and stabilization blockers

The structural reader can enumerate every defined table and body extent,
index strings and blocks in constant time after bounds checking, preserve
unknown **registered nonsemantic** sections as opaque bytes when rewriting,
and report unsupported semantic opcodes without executing them. An auditing
semantic reader additionally checks operations and flow; an
interpreter/JIT/AOT implements only the supported feature set. A linker
rewrites row references, body IDs and affected indexes/liveness facts,
preserving their meaning or selecting an audit before publishing the
rewritten body. A trimmer must retain transitive type/signature/contract,
source-selected call, witness and cleanup dependencies, not just reachable
body opcodes. Removing optional SourceMap cannot change execution.

| Consumer question | Direct format input | Remaining consumer freedom |
|---|---|---|
| Where is a block and its branch? | Block and terminator offsets, explicit target IDs | Choose hot/cold layout and machine branch forms without bytecode displacement fixups. |
| What crosses a block boundary? | Typed merge parameters, sorted live-in/out value IDs, active place/state joins | Fast tier can allocate slots directly; optimizing tier can rebuild SSA, liveness or register allocation if profitable. |
| Which loops and failures are present? | Region headers/exits, typed success edges, explicit ordered failure terminators | Lower failure/cleanup and optimize loops without treating hints as a prescribed machine CFG. |
| Which calls are intrinsic candidates? | Exact selected method identities and declared contracts; future registered intrinsics require explicit semantics | Inline or lower only compatible recognized identities, never guess from names or rewrite effectful calls as pure arithmetic. |
| Which numeric/vector operations exist? | Stable opcode semantics plus TypeSpec and required feature identity | Extend primitives and SIMD via new registered types/operations; do not silently reinterpret old scalar opcodes or assume every lane has the scalar's failure behavior. |

These indexed facts target importer and time-gated JIT work such as
discovering edges, joining local state and locating lifetimes. They
do not encode a chosen optimization schedule or prohibit a deeper
compiler from computing alias, memory-SSA or range facts of its own.
Spending a few bytes per block on direct access can save repeated
whole-body traversal; no throughput ratio is implied.

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

These are explicit blockers to declaring the proposals final, not an
invitation to invent semantics or serialize the current private bound tree.
See [implementation](../implementation.md) for what the repository actually
executes. The source-language [portable proof floor](../language-contract-review.md#4-a-reproducible-proof-and-evaluation-foundation)
is distinct from decoder and verifier work budgets.
