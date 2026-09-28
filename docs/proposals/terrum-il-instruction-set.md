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

There is one encoding for each semantic operation family, not separate
one-byte/long-branch or short/long-local opcode variants. Numeric policy
(checked, wrapping, saturating, bit reinterpretation) is explicit; converting
one into another is not a space-saving normalization. A discarded result does
not discard an effectful call. Typed source binding selects one MethodDef
before encoding; no reader reruns overload resolution. Operations absent
from the assigned tables reject rather than being guessed from CIL or CLR
behavior.

## Integer and row encodings

`u(n)` is a canonical unsigned base-128 LEB128 integer; `u32` accepts at
most five bytes with the fifth payload at most `0x0F`, and `u64` at most ten
bytes with the tenth payload at most `0x01`. A final zero group on a
multi-byte encoding, excess continuation, truncation or overflow rejects.
`s(n)` is a `u64` encoding of ZigZag signed 64-bit integer
(`n >= 0 ? 2*n : -2*n-1`, computed without signed overflow). Every decoded
count is checked against the containing byte extent **before** allocation.
Numbers in body operands are unsigned `u32` unless stated as `u64`/`s`.
Bare `u32` in a body row or index is fixed little-endian; `u64(bits)`
in an operand means a canonical `u` limited to 64 bits, not eight fixed
bytes. `Switch` patterns use this same `u64(bits)` encoding.
These bytes are for interchange; after a bounded decode pass a tool can keep
native-width indexed arrays. The format does not require varint decoding in
an interpreter's hot loop.

An opcode is one byte: `0x01..0x7F` are operations, `0x80..0xFE` are
terminators, and `0x00`/`0xFF` are invalid. Operands follow their opcode in
the specified order, without an eight-byte per-instruction header. Known
opcode arity and the block-offset index delimit instructions; unknown opcodes
reject semantic loading, not skip by an untrusted length. All byte and
integer forms have one canonical encoding in this format revision.

## Body record, direct block index and regions

A Body table row is the bytes between adjacent Body-section offsets, with
no further row-length prefix. Its fields, in order, are:

| Field | Encoding | Constraint |
|---|---|---|
| `ownerMethod` | `u32` little-endian | Local MethodDef row whose `bodyRow` points here |
| `bodyFlags` | `u8` | Zero; unknown bits reject |
| `blockCount`, `valueCount`, `placeCount`, `regionCount` | four `u` | Body-local counts; blockCount is positive |
| `places` | `placeCount` entries | `u(typeSpec), u(regionId), u8 flags` each |
| `regions` | `regionCount` entries | Described below, IDs `1..regionCount` |
| `blockOffsets` | `u32[blockCount + 1]` little-endian | Relative to the following block-data start |
| `blockData` | `blockOffsets[last]` bytes | One block per consecutive offset extent |

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

A region entry is `u8 kind, u(parentRegion), u(entryBlock), u(exitBlock),
u(cleanupBlock)`. Parent is 0 (function) or a preceding region ID, so
parent chains are acyclic. Kind `0x01` is a loop with a nonzero entry header,
one designated exit outside its subtree and zero cleanupBlock. Kind `0x02`
lexical scope is reserved until lifetime and exit rules are assigned.
Kinds `0x03` handler, `0x04` finally, `0x05` loan,
`0x06` unsafe and `0x07` root reserve their identities but are **not
executable** until exit/cleanup/exception rules and feature requirements are
assigned. No region kind by itself proves borrow safety or releases an owner.

A block extent starts with `u(regionId), u(parameterCount)`, then
`u(valueId), u(typeSpec)` for each parameter, `u(operationCount)`, exactly
that many operations, and exactly one terminator. Block ID is its one-based
index in `blockOffsets`; region 0 is the function root. Entry block is ID 1.
Its by-value parameters correspond in order and complete type to the
MethodSig's by-value parameters; a receiver and non-value parameters use
their declared place/receiver contract, not a fabricated value parameter.
Other blocks receive precisely their incoming-edge types and arity.

Value IDs are unique definitions in `1..valueCount`, with no holes: block
parameters and result-producing operations each define one. A definition
must dominate every use, including uses on edges. Block arguments to distinct
conditional successors represent mutually exclusive transfers. Passing the
same live unique value twice on a single edge, using it after move, or
implicitly copying a non-Copy value rejects. In the executable subset, region entries are loops only. A block's `regionId`
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

`r` means `u(valueId)`, `p` means `u(placeId)`, `t` means `u(typeSpecRow)`,
and `m` means `u(methodDefRow)`; zero is invalid except `r?` for a void
result. `args` is `u(count), r[count]`. `rNew` is the next unique value
definition, not a use; its inferred or explicit type must be consistent
at every use. An instruction's result type is
inferred from its operands and declared metadata unless an explicit `t`
appears. The table assigns decode shapes. The executable subset is narrower, as
specified after the opcode tables; assigned but unsupported shapes can be
inspected but must not be emitted as executable `(0, 2)` bodies.

| Opcode | Operation; operands in byte order | Semantics and verifier requirement |
|---:|---|---|
| `01` | `ConstBits rNew, t, u64(bits)` | Canonical bit pattern for supported scalar; `bool` 0/1, `char16` 16 bits, smaller signed integers sign-extended. |
| `02` | `ConstSigned rNew, t, s(value)` | Direct signed integer literal; range checked in `t`, no default numeric type. |
| `03` | `Copy rNew, r` | Source's complete type has checked `Copy`; does not duplicate a unique obligation. |
| `04` | `Unary rNew, u(kind), r` | Kind 1 bool NOT, 2 integer bitwise NOT, 3 integer unary plus. Checked negation uses a failure terminator. |
| `05` | `Binary rNew, u(kind), rLeft, rRight` | Kinds 1/2/3 bitwise AND/OR/XOR, 4/5 equality/inequality, 6/7/8/9 ordered `<`/`<=`/`>`/`>=`, 10/11/12 explicit wrapping add/subtract/multiply. Only built-in scalar operations; no invented user witness. |
| `06` | `Convert rNew, tDest, u(policy), rSource` | Policy 1 integer wrapping, 2 integer saturation, 3 checked bit-preserving reinterpretation of eligible equal-size representations. Checked range conversion uses a failure terminator. |
| `07` | `Select rNew, rCondition, rTrue, rFalse` | Both source values already exist; eager selection of equal complete Copy types, never conditional evaluation or owner transfer. |
| `08` | `MakeOptional rNew, tOptional, rValue` | Wrap one fully initialized value; payload type and qualification match. |
| `09` | `None rNew, tOptional` | Absent optional; no fabricated payload or extra optional layer. |
| `0A` | `IsPresent rNew, rOptional` | Boolean presence test, not a value load. |
| `0B` | `PlaceLocal rNew, p` | Defines a place-valued result scoped to the place's region. |
| `0C` | `PlaceField rNew, rBasePlace, u(fieldDefRow)` | Checked field identity, access, layout and retained owner lifetime; produces a place, not a field copy. |
| `0D` | `Load rNew, rPlace` | Reads initialized Copy value; a unique value instead requires Move. |
| `0E` | `Initialize rPlace, rValue` | Writes only vacant authorized storage, consumes a unique source; fully publishes or fails before publication. |
| `0F` | `Assign rPlace, rValue` | Requires writable initialized storage; old owning value needs its explicit cleanup path before replacement. |
| `10` | `Move rNew, rPlace` | Consumes an initialized unique place into a unique value and leaves it vacant. |
| `11` | `Drop rPlace` | Drops one initialized unique place using its declared NoThrow cleanup; leaves it vacant. |
| `12` | `CallNoThrow rNew?, m, args` | Exact selected callable; accepts only matching modes/contracts and a declared NoThrow effect. `rNew=0` iff result is void. Does not drop observable effects. |
| `13` | `Aggregate rNew, t, args` | Fully constructs ordered fields in declaration order, with checked Copy/unique transfer and validity; no implicit zero-fill or padding observation. |
| `14` | `Extract rNew, rAggregate, u(fieldDefRow)` | Copy-only by-value field; unique fields require an authorized place and Move. |

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

An **edge** is `u(targetBlock), args`. Every target exists; supplied values
match its block parameters, in order. No fall-through or byte-relative
branches exist. In the following table `edge` has that shape, `r?` can be
zero only for void, and failure uses the module's declared fail-fast or
checked-exception policy:

| Opcode | Terminator; operands in byte order | Contract |
|---:|---|---|
| `80` | `Branch edge` | Unconditional transfer. |
| `81` | `BranchIf rBool, edgeTrue, edgeFalse` | Evaluates an already computed Boolean once. |
| `82` | `Switch rScalar, u(caseCount), (u64(bits), edge)[caseCount], edgeDefault` | Exact type-width patterns sorted strictly by unsigned bits; one selected edge, no repeated selector evaluation. |
| `83` | `Return r?` | Matches declared result; moved owner transfers, remaining obligations must be discharged. |
| `84` | `CheckedBinary u(kind), rLeft, rRight, edgeSuccess` | Kinds 1..5 checked add/subtract/multiply/divide/remainder. Computed result is an *implicit first* success-block parameter, before the edge's explicit arguments; overflow/divide-by-zero dispatch failure. |
| `85` | `CheckedConvert tDest, rSource, edgeSuccess` | Checked numeric range conversion; implicit first success parameter is `tDest`. |
| `86` | `Invoke m, args, edgeSuccess` | Potentially failing exact selected call. A non-void result is the implicit first success parameter; failure departs under runtime policy. Authored handlers require a future exception-region encoding. |
| `87` | `Fail u(reason)` | Declared requirement failure (`1`), overflow (`2`), division by zero (`3`); no successor. |
| `88` | `CheckedNegate rOperand, edgeSuccess` | Checked signed negation with implicit first success parameter. |
| `89` | `CheckedIndex rBase, rIndex, edgeSuccess` | Bounds/validity check and implicit first result place; requires a specified indexed-backing feature before emission. |
| `8A` | `Leave u(exitCount), u(regionId)[exitCount], edge` | Reserved for explicit scope/cleanup exit; not executable until its unwind contract is assigned. |

Failure departures never become unchecked numeric results or `None`.
Checked operations with a potentially failing edge cannot occur in a
`NoThrow` method unless a separately specified static-proof rule establishes
that failure cannot occur; format `(0, 2)` makes no such exception. `Invoke`
still checks selected method signature, effects, argument modes and return
ownership. For an operation with an implicit success result, the target
parameter count is `edge.argumentCount + 1` and its first type is the result
type. The failure policy does not synthesize a catch region; if the caller
owns something requiring cleanup on failure, this profile cannot encode
the call. The `CheckedIndex` and `Leave` opcode assignments reserve their
shapes but are rejected by a `(0, 2)` executable reader.

For `CheckedBinary`, operands share one of `int32`, `uint32`, `int64`,
`uint64`; signed minimum divided by `-1` overflows and zero divisors
fail. Signed checked negation fails at its minimum. Checked integer
conversion either preserves the exact value or fails; no implicit numeric
fallback type is involved. Explicit `Branch`/`Switch` edges and loop-region
records allow a tool to build a CFG by scanning **terminators only**, with
direct block offsets already validated. It may cache predecessors,
dominance and liveness, but cannot treat unverifiable hints or unused
code as permission to ignore malformed instructions.

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
contains a four-byte owner row, one flag, four one-byte counts, an eight-byte
block-offset index `[0,9]`, and a nine-byte block (region 0, zero parameters,
one `ConstBits` operation, one `Return` terminator). Old `(0,1)` required
96 bytes for that Body record; `(0,2)` uses 34 bytes including its section
offsets. With the exact eight populated metadata sections in the example,
the earlier 19-entry directory and length-prefixed rows occupied 731 bytes
total, before alignment. Omitting empty tables and length-prefixes, indexing
variable rows, and using this coded body reduces that illustrative artifact
to **386 bytes**: 32 header + 8 directory entries * 24 + 162 bytes of
metadata/Body sections. This is an encoded-size comparison, **not** a
measured runtime or throughput improvement; decode cost, branch count,
debug information and index alignment still matter.

Readers reject noncanonical varints, a block-offset sentinel of 8 instead
of 9, a missing terminator, duplicate values, an invalid successor or an
unknown opcode before execution. An independent decoder should round-trip
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

For `(0, 2)`, an executable body uses only scalar TypeSpecs (`bool`,
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
