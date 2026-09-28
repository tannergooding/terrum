# Terrum coded instruction proposal

> [!IMPORTANT]
> Candidate `TRRM` interchange design, not the current revision-35 writer or
> an approved execution profile. The unencoded contracts at the end are
> stabilization blockers, not permissions to erase their semantics.

The [metadata proposal](terrum-il-metadata-spec.md) defines the Body table,
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
encoded loop/region hints are trustworthy without validation.

Numeric policy (checked, wrapping, saturating, bit reinterpretation) is
explicit; converting one into another is not a space-saving normalization.
A discarded result does not discard an effectful call. Typed source binding
selects one MethodDef before encoding; no reader reruns overload resolution.
Operations absent from the assigned tables reject rather than being guessed
from CIL or CLR behavior.

## Integer widths and instruction framing

Every multi-byte integer is little-endian. The common instruction uses
one-byte references, counts and data without a descriptor. An instruction
needing wider operands begins `F0 layout opcode`; otherwise it begins
`opcode` directly. The `layout` byte selects widths independently by operand
class; bits 6–7 must be zero:

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
instruction with `layout=0`, a nonminimal class width, or a reserved bit
rejects. This makes the default a direct opcode dispatch and bounds all
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
For small IDs and literals the common form matches the earlier LEB128
operand sizes, but a two-byte literal needs a two-byte prefix and layout
overhead; conversely fixed widths avoid continuation decoding for every
reference. No decode-throughput or total-size win is asserted without
representative workloads, including wider body IDs, switch tables and
large immediate values.

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
| `blockOffsets` | `u32[blockCount + 1]` little-endian | Relative to the following block-data start |
| `blockData` | `blockOffsets[last]` bytes | One block per consecutive offset extent |

Body-layout width codes 0, 1 and 2 mean 1, 2 and 4 bytes; code 3 rejects.
The local width applies to the four counts, structural block/region/value/
place IDs, block parameter and operation counts, region fields and place
region references. Metadata width applies to structural TypeSpec rows
(place and block-parameter types). Each width is the smallest capable of
representing every field in its class throughout the **whole body**;
unused classes select code 0. The `ownerMethod` and each `blockOffsets`
entry remain fixed `u32`, independent of `bodyLayout`. An instruction's
operand width is independent of the enclosing body width and is selected by
its own prefix, not by `bodyLayout`. Readers validate all fields and minimal
widths before accepting a body.

Offsets begin at 0, increase strictly (no empty blocks), and the last is
exactly the remaining body bytes. The body must therefore keep block data
under 4 GiB; reader budgets can be lower. Blocks are directly addressable
by ID without scanning operations. Offsets do not replace validation:
each decoded block consumes its exact extent. An invalid block index,
arithmetic overflow, overlap, trailing byte or duplicate body owner rejects.
Body rows follow the owning MethodDef `bodyRow` association, not a native
code-address order. A linker may relocate metadata rows only by rewriting
all references and revalidating the result.

Places are numbered `1..placeCount`. Flag `0x01` permits rebinding the place,
`0x02` indicates unique storage and `0x04` indicates an initially vacant
output; other bits reject. The complete TypeSpec determines referent access
and representation; these flags cannot grant `write` through a readonly
handle. Non-value parameters map to their declared places in parameter
ordinal order, followed by locally created places. By-value parameters are
entry-block values instead. Place descriptors name storage, not an exposed
address or a native stack slot.

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

A block extent starts with `local(regionId), local(parameterCount)`, then
`local(valueId), metadata(typeSpec)` for each parameter,
`local(operationCount)`, exactly
that many operations, and exactly one terminator. Block ID is its one-based
index in `blockOffsets`; region 0 is the function root. Entry block is ID 1.
Its by-value parameters correspond in order and complete type to the
MethodSig's by-value parameters; a receiver and non-value parameters use
their declared place/receiver contract, not a fabricated value parameter.
Other blocks receive precisely their incoming-edge types and arity.

Value IDs are unique definitions in `1..valueCount`, with no holes: block
parameters and result-producing operations each define one. IDs are allocated
in block-ID/operation order, including block parameters before operations.
An implicit checked-operation success result is its target block's first
parameter, not an extra definition at the terminator. A definition
must dominate every use, including uses on edges. Block arguments to distinct
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
until an explicit exit encoding is assigned. The verifier builds the CFG and
checks dominance, reachability and region nesting within its work budget;
loop regions accelerate consumers but are checked, not proof certificates.
Irreducible cycles have no encoding in this profile.

## Operation encoding and normalized effects

`r` means a reference-width value ID, `p` a reference-width place ID,
`t` a reference-width TypeSpec row, and `m` a reference-width MethodDef row;
zero is invalid except `r?` for a void result. `args` is a count-width
integer followed by that many reference-width `r` operands. `rNew` is the
next unique value definition in block order, not a use; its inferred or
explicit type must be consistent at every use. An instruction's result type is
inferred from its operands and declared metadata unless an explicit `t`
appears. The table assigns decode shapes. The executable subset is narrower, as
specified after the opcode tables; assigned but unsupported shapes can be
inspected but must not be emitted as executable `(0, 3)` bodies.

| Opcode | Operation; operands in byte order | Semantics and verifier requirement |
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

`ConstBits` encodes the smallest unsigned width holding the declared-width
bit pattern, not a sign-extended 64-bit container: `int32(-1)` is
`FF FF FF FF` and `int64(-1)` is eight `FF` bytes. The reader first
zero-extends the encoded bits to the declared width and then interprets
signedness. `ConstSigned` instead uses the smallest signed width holding
the mathematical value (for example `-1` is `FF`, `200` is `C8 00`).
The complete TypeSpec selects the width; unrepresentable or noncanonical
patterns reject.

The accepted scalar set is `bool`, `char16`, `int32`, `uint32`, `int64`,
`uint64`; integer arithmetic works in its exact declared width. `Binary`
comparison returns `bool`, and its operands have the same complete type;
operator kind does not imply a user-supplied equality witness. No
floating, vector, atomic, raw-pointer or text operation is mapped onto a
numeric opcode. Wrapping and saturation are explicit, not consequences of
unchecked machine code. A `Convert` bit reinterpretation requires identical
validated representation and cannot create an invalid value or handle.
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
| `84` | `CheckedBinary u8(kind), rLeft, rRight, edgeSuccess` | Kinds 1..5 checked add/subtract/multiply/divide/remainder. Computed result is an *implicit first* success-block parameter, before the edge's explicit arguments; overflow/divide-by-zero dispatch failure. |
| `85` | `CheckedConvert tDest, rSource, edgeSuccess` | Checked numeric range conversion; implicit first success parameter is `tDest`. |
| `86` | `Invoke m, args, edgeSuccess` | Potentially failing exact selected call. A non-void result is the implicit first success parameter; failure departs under runtime policy. Authored handlers require a future exception-region encoding. |
| `87` | `Fail u8(reason)` | Declared requirement failure (`1`), overflow (`2`), division by zero (`3`); no successor. |
| `88` | `CheckedNegate rOperand, edgeSuccess` | Checked signed negation with implicit first success parameter. |
| `89` | `CheckedIndex rBase, rIndex, edgeSuccess` | Bounds/validity check and implicit first result place; requires a specified indexed-backing feature before emission. |
| `8A` | `Leave count(exitCount), regionId[exitCount], edge` | Reserved for explicit scope/cleanup exit; not executable until its unwind contract is assigned. |

Failure departures never become unchecked numeric results or `None`.
Checked operations with a potentially failing edge cannot occur in a
`NoThrow` method unless a separately specified static-proof rule establishes
that failure cannot occur; format `(0, 3)` makes no such exception. `Invoke`
still checks selected method signature, effects, argument modes and return
ownership. For an operation with an implicit success result, the target
parameter count is `edge.argumentCount + 1` and its first type is the result
type. The failure policy does not synthesize a catch region; if the caller
owns something requiring cleanup on failure, this profile cannot encode
the call. The `CheckedIndex` and `Leave` opcode assignments reserve their
shapes but are rejected by a `(0, 3)` executable reader.

For `CheckedBinary`, operands share one of `int32`, `uint32`, `int64`,
`uint64`; signed minimum divided by `-1` overflows and zero divisors
fail. Signed checked negation fails at its minimum. Checked integer
conversion either preserves the exact value or fails; no implicit numeric
fallback type is involved. Explicit `Branch`/`Switch` edges and loop-region records allow a tool to
build a CFG without discovering block starts from branch displacements.
The block-offset index provides direct block access, **not** direct
terminator access: a tool must parse the counted operations in each block
to locate its terminator, including their width prefixes. It may cache
terminator offsets, predecessors, dominance and liveness after bounded
validation, but cannot treat hints or unused code as permission to ignore
malformed instructions.

## Value and place state through checked arithmetic

A function returning `int32` with one by-value `int32` parameter starts with
its parameter as block value `r1`. The following is **executable** in the
narrow fail-fast `(0, 3)` profile; `t1` is the `int32` TypeSpec:

```text
B1(r1: t1): r2 = ConstSigned t1, 5
            CheckedBinary add r1, r2 -> B2()
B2(r3: t1): Return r3
```

The checked result is `r3`, **not** a hidden evaluation-stack value or an
in-place mutation of `r1`. On overflow there is no B2 edge: failure uses
the module policy. For illustration, a mutable local instead needs a
place descriptor `p1` (mutable and initially vacant), followed by
`r1 = PlaceLocal p1`, `r2 = ConstSigned t1, 10`,
`Initialize r1, r2`, `r3 = Load r1`, `r4 = ConstSigned t1, 5`,
`CheckedBinary add r3, r4 -> B2(r5: t1)`, `Assign r1, r5`,
and `Return r5`. This second sequence has assigned operand shapes
but **cannot execute in `(0, 3)`**: `PlaceLocal`/`Initialize`/`Load`/`Assign`
still lack complete place and failure-path rules. `PlaceField` would derive
an addressable field place from an owning place, whereas `Extract` returns
a Copy field value from an aggregate; neither is a silent register/address
mode of `Binary`. Folding a literal into a future add-immediate opcode
would have to preserve the same overflow and evaluation semantics.

| Source operand shape | Proposed state transition before checked add |
|---|---|
| By-value parameter or computed temporary | Already has a typed immutable `r`; use it directly. |
| Integer immediate | `ConstSigned` or `ConstBits` defines a typed `r`; it is not inferred from a machine immediate or a default integer type. |
| Mutable local | `PlaceLocal` obtains its `rPlace`; `Load` defines a Copy `r`. On the successful edge, `Assign` writes the result through the place after verifying authority and state. |
| Field of an addressable owner | `PlaceField` derives a field place and `Load` obtains its value; for a Copy aggregate held by value, `Extract` returns a field value instead. Provenance and owner lifetime are not lost. |

Only the first two rows are executable in `(0, 3)`. An interpreter can
materialize values in body-local slots and places in verified storage;
JIT/AOT may choose registers or memory independently. The format does not
require a distinct add opcode for every source operand combination, nor
does it assume that a stored place is a native stack slot.

## Worked byte example and measured format costs

For [metadata example](terrum-il-metadata-spec.md#row-assignment-example),
MethodDef row 1 returns `int32` with no arguments; TypeSpec row 1 is
`int32`. Its `return 42` body has one value, one block, zero places and
zero explicit regions. The Body **section** for its single row is:

```text
00 00 00 00  1A 00 00 00
01 00 00 00  00  01 01 00 00
00 00 00 00  09 00 00 00
00 00 01  01 01 01 2A  83 01
```

The eight-byte section offset index delimits a 26-byte body. The body
contains a four-byte owner row, a zero body layout, four one-byte counts,
an eight-byte block-offset index `[0,9]`, and a nine-byte block (region 0,
zero parameters, one `ConstBits` operation, one `Return` terminator).
Old `(0,1)` required
96 bytes for that Body record; `(0,3)` uses 34 bytes including its section
offsets. With the exact eight populated metadata sections in the example,
the earlier 19-entry directory and length-prefixed rows occupied **729
bytes** before alignment: 32-byte header + 19 * 24-byte directory + 241
bytes of sections. Omitting empty tables and length prefixes, indexing
variable rows and using this coded body reduces that illustrative artifact
to **386 bytes**: 32-byte header + 8 * 24-byte directory + 162 bytes
of metadata/Body sections. These are encoded-size calculations, **not**
measured throughput improvements; wider IDs, prefixes, block counts,
decode work, branch count, debug information and alignment still matter.

For the preceding by-value checked-add function (assuming the stated MethodSig
and `t1`), the Body **section** uses three values, two blocks and no places or
regions. Block 1 is 15 bytes; block 2 is seven. Its exact 51 bytes are:

```text
00 00 00 00  2B 00 00 00
01 00 00 00  00  02 03 00 00
00 00 00 00  0F 00 00 00  16 00 00 00
00 01 01 01 01  02 02 01 05  84 01 01 02 02 00
00 01 03 01 00  83 03
```

The counted operation is `02 02 01 05` (`ConstSigned r2, t1, 5`);
`84 01 01 02 02 00` is checked add of `r1` and `r2` with a
zero-explicit-argument edge to B2. B2's first parameter `r3` receives the
implicit result. For width selection, `ConstSigned r3, t1, 200` instead
uses `F0 10 02 03 01 C8 00` (two-byte signed data). If its result
ID were 256, the same instruction would use
`F0 11 02 00 01 01 00 C8 00` (two-byte references and data).
Neither prefix changes the operation's semantic identity.

Readers reject nonminimal widths, a truncated prefix, a block-offset
sentinel of 8 instead of 9, a missing terminator, duplicate values,
an invalid successor or an unknown opcode before execution. An independent
decoder should round-trip
the exact rows and produce the same CFG/type/state outcomes as a verifier;
same-writer/same-reader round trips alone do not establish conformance.

## Tool roles and stabilization blockers

The structural reader can enumerate every defined table and body extent,
index strings and blocks in constant time after bounds checking, preserve
unknown **registered nonsemantic** sections as opaque bytes when rewriting,
and report unsupported semantic opcodes without executing them. A semantic
reader additionally validates references, binding, operations and flow;
an interpreter/JIT/AOT implements only the supported feature set. A linker
rewrites row references and body IDs with corresponding table and CFG
validation; a trimmer must retain transitive type/signature/contract,
source-selected call, witness and cleanup dependencies, not just reachable
body opcodes. Removing optional SourceMap cannot change execution.

### Executable subset versus assigned decode shapes

For `(0, 3)`, an executable body uses only scalar TypeSpecs (`bool`,
`char16`, `int32`, `uint32`, `int64`, `uint64`) and optional forms of those
scalars, by-value parameters, no receiver, no places, and no generic or
external call. It can contain loop regions as constrained above. Operations
`01..0A` and terminators `80..85`, `87..88` are executable with their stated
type, state and failure checks. `CheckedBinary` kinds 4 and 5 require
an integer divisor and use the selected module fail-fast policy on failure;
`CheckedConvert` accepts only the assigned integer types. `Fail` terminates
under that policy. Only `runtimePolicy=0` is executable in this subset:
checked-exception dispatch requires a specified exceptional edge, payload
and cleanup protocol. All live values in this subset are Copy values, so a
failure cannot strand a unique cleanup obligation.

`0B..14`, `86`, `89..8A`, and nonzero place counts are **assigned
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
