# Terrum coded instruction proposal

> [!IMPORTANT]
> Proposal only: these are candidate `TRRM` body bytes and verifier rules,
> not the repository's current structured-IR serialization or an adopted
> executable profile.

The [metadata and file-format proposal](terrum-il-metadata-spec.md) defines
the header, directory, tables, declaration identities, and Body row. This
document defines that row's initial coded content: block control flow,
typed values, and a small executable operation set. It is organized separately
from metadata, following the *structural* distinction between metadata and
instructions in ECMA-335 Partitions II and III. No CIL opcode, evaluation
stack, CLI token or managed-machine ABI is imported.

## Definitions and encoding conventions

A **body** is the implementation of exactly one local MethodDef. A **block**
has parameters, ordered operations and one terminator. A **value ID** names
one typed definition within a body; it is not an address, storage location or
metadata token. A **region** describes the scope governing blocks; the
initial profile permits only the function region. A **normal edge** transfers
typed values to block parameters. A **failure departure** dispatches to the
declared runtime policy without an authored catch or cleanup region in this
initial profile. Source spans are optional nonsemantic SourceMap data.

All fields are little-endian fixed-width integers (`u8/u16/u32/u64`). Lengths
count bytes and exclude their own `u32` prefix. All IDs are body-local `u32`
integers; zero is invalid except where explicitly specified. A TypeSpec or
MethodDef reference uses a one-based row in the metadata table of that name;
the only cross-table declaration operand is the six-byte metadata `DefId`.
No operand implicitly uses a compiler enum value, pointer or text name.

## Coded body layout

The Body section uses the same `u32 payloadLength, payload[payloadLength]`
record framing as every metadata table. Body rows occur in MethodDef `bodyRow`
order, with no unowned, multiply owned or imported body. The payload is:

| Order | Field | Width | Rule |
|---:|---|---:|---|
| 1 | `ownerMethod` | `u32` | Local MethodDef row whose `bodyRow` points back here |
| 2 | `bodyFlags` | `u32` | Zero in this candidate profile; unknown bits reject |
| 3 | `valueCount` | `u32` | Definitions are exactly IDs `1..valueCount` |
| 4 | `regionCount` | `u32` | Exactly 1 in the initial profile |
| 5 | `blockCount` | `u32` | Positive; entry block is ID 1 |
| 6 | `regions` | `regionCount` records | Fixed 16-byte records below |
| 7 | `blocks` | `blockCount` records | Variable records below, in ID order |

A region is `u32 id, u32 parentId, u8 kind, u8 flags, u16 reserved,
u32 entryBlock`. Initial bodies encode one region with ID 1, parent 0, kind
`0x01` (function), flags/reserved zero and entryBlock 1. Nested lexical,
handler, cleanup, unsafe, loan/view and root regions have **no assigned
encoding** here. A reader must reject them rather than flattening their
semantics into the function region.

A block is `u32 blockByteLength` followed by this exact payload:

| Order | Field | Width | Rule |
|---:|---|---:|---|
| 1 | `blockId` | `u32` | Exactly its one-based ordinal in this body |
| 2 | `regionId` | `u32` | 1 for this profile |
| 3 | `parameterCount` | `u32` | Entry parameters correspond to the MethodSig parameters |
| 4 | `parameters` | `parameterCount * 8` bytes | Each is `u32 valueId, u32 typeSpec` |
| 5 | `operationCount` | `u32` | Number of nonterminating operations |
| 6 | `operations` | `operationCount` records | Encoded in evaluation order |
| 7 | `terminator` | 1 record | Exactly one; no trailing bytes |

The entry block parameters have the same order and complete types as the
method's by-value parameters. The initial body profile admits only `bool`,
`char16`, `int32`, `uint32`, `int64` and `uint64` parameters/results (plus
`void` result); it rejects receivers, generic methods, non-value parameter
modes and ownership-bearing values.
Other blocks receive precisely the values carried by each incoming edge.
Block parameters and operation results define unique value IDs; their IDs
must fill `1..valueCount` without holes or duplicates. Every value use must
be dominated by its definition, including a use on an edge. Values are
immutable; a `Copy` instruction creates a fresh SSA name, not an owning
duplicate.

## Operation records and assigned candidate opcodes

An instruction record is `u16 opcode, u16 flags, u32 payloadLength,
byte[payloadLength]`. Flags must be zero in this profile. The high bit of
`opcode` distinguishes terminators from operations; no unknown opcode or
unknown operand can be ignored. Payloads are exact: trailing bytes, short
fields and nonzero reserved fields reject.

| Opcode | Operation payload, in byte order | Static and execution contract |
|---:|---|---|
| `0x0001` `ConstBits` | `u32 result, u32 typeSpec, u64 bits` | Defines one scalar value; upper bits must be zero for narrower unsigned, `bool` is 0 or 1, and signed narrower values are canonical sign extensions. No host-sized conversion. |
| `0x0002` `Copy` | `u32 result, u32 typeSpec, u32 source` | Source has the same complete TypeSpec and a checked `Copy` property; result is a separate value. |
| `0x0003` `PureBinary` | `u32 result, u32 resultTypeSpec, u8 kind, u8 reserved=0, u16 reserved=0, u32 left, u32 right` | Ordered operands; kinds `1/2/3` are bitwise AND/OR/XOR on the four integers, `4/5` are scalar equality/inequality returning `bool`. Operand types must match and have the specified operation; no user witness is inferred. |
| `0x0004` `CallNoThrow` | `u32 result?, u32 resultTypeSpec, DefId target, u32 argumentCount, u32 arguments[argumentCount]` | Calls the already selected local or imported MethodDef with exactly its declared scalar by-value arguments/result. The callee must promise `NoThrow` and have no unsupported unsafe or ownership obligations; `result=0` exactly for `void`. The call still preserves observable effects. |

`ConstBits` initially admits `bool`, `char16`, `int32`, `uint32`, `int64`
and `uint64` with TypeSpec primitive codes from the metadata proposal.
For signed 32-bit values the 64-bit bit pattern is sign-extended; `char16`
uses only its low 16 bits. `PureBinary` does not silently substitute a
numeric operator witness, invent an equality policy for managed identity, or
permit overflow-prone arithmetic under an unqualified "pure" name. A
`CallNoThrow` target is a MethodDef `DefId`; a method name or dependency
candidate set is never searched while interpreting the instruction.

## Terminators and control-flow operands

Each block ends with exactly one of the following instruction records. Edge
payloads store value IDs in source order and match the destination block's
parameter types and count. The current profile has no fall-through.

| Opcode | Terminator payload, in byte order | Effect |
|---:|---|---|
| `0x8001` `Branch` | `u32 targetBlock, u32 argumentCount, u32 arguments[argumentCount]` | Transfer all arguments to target block parameters. |
| `0x8002` `BranchIf` | `u32 condition, u32 trueBlock, u32 trueCount, u32 trueArgs[trueCount], u32 falseBlock, u32 falseCount, u32 falseArgs[falseCount]` | Condition must be `bool`; exactly one edge is taken. |
| `0x8003` `Return` | `u32 result?` | Zero exactly for `void`, otherwise a value with the declared result type. |
| `0x8004` `CheckedBinary` | `u8 kind, u8 reserved=0, u16 reserved=0, u32 left, u32 right, u32 successBlock, u32 otherCount, u32 otherArgs[otherCount]` | Kinds `1/2/3/4/5` mean checked add/subtract/multiply/divide/remainder. On success, the computed result becomes the **first** success-block parameter, followed by `otherArgs`; on failure dispatch to the runtime policy. |
| `0x8005` `Fail` | `u16 reason, u16 reserved=0` | Raise a declared failure (`1` requirement failed). No successor. |

For `CheckedBinary`, both operands have the same type from `int32`, `uint32`,
`int64` or `uint64`, and the result has that type. The success block has exactly
`otherCount + 1` parameters with the result type first. Overflow, zero
divisor and signed-minimum divided by `-1` follow the accepted checked
numeric rules; the resulting failure uses reason `2` (overflow) or `3`
(division by zero). This terminator cannot appear in a `NoThrow` method
under the initial verifier, even if its producer thinks it will not fail.
`Fail` likewise contradicts `NoThrow`. Fail-fast and checked-exception
behavior are selected by the artifact's declared runtime policy; the
instructions do not encode host-language exceptions.

Instructions are ordered operations, not a source-level expression tree:
`BranchIf` evaluates an already computed Boolean once, `CallNoThrow`
evaluates no argument expression a second time, and a checked operation
cannot silently become an unchecked one. A binary reader can validate
framing, types, references, branch arity and supported features independently
of a writer. A source compiler additionally checks its authored body
against declarations. An ordinary consumer is not required to re-prove an
external dependency's body, but cannot execute an unsupported or malformed
body it chooses to load.

## Encoded example and invalid forms

Suppose MethodDef row 1 is a no-argument method returning `int32` and
TypeSpec row 1 denotes `int32`. Its `return 42` body has `valueCount=1`,
one function region and one block. The complete Body **record**, including
its four-byte record-length prefix, is:

```text
5C 00 00 00  01 00 00 00  00 00 00 00  01 00 00 00
01 00 00 00  01 00 00 00
01 00 00 00  00 00 00 00  01 00 00 00  01 00 00 00
34 00 00 00  01 00 00 00  01 00 00 00  00 00 00 00  01 00 00 00
01 00 00 00  10 00 00 00  01 00 00 00  01 00 00 00
2A 00 00 00 00 00 00 00
03 80  00 00  04 00 00 00  01 00 00 00
```

The payload is 92 bytes: 20-byte body header, 16-byte region, four-byte
block-length prefix and 52-byte block. `ConstBits` defines value 1 and
`Return` consumes it. The following each reject before execution: a Body
row owned by MethodDef 2, a block length of 51, a duplicate value ID,
an out-of-range TypeSpec row, a nonzero reserved instruction flag, or an
unknown opcode—even if an unknown instruction declares a plausible length.

## Feature boundary and next encoding work

This candidate gives concrete bytes for the initial by-value scalar and
`NoThrow` call graph, ordinary blocks, and checked numeric failure without
authored recovery. It does not represent the repository's current full
bounded execution profile. Before encoding loans, places, field writes,
initialization outputs, owner moves and cleanup, handler/`finally` regions,
managed roots, generic witness calls, or pre/postcondition checks, specify
their operands, normal and exceptional edges, region transitions,
metadata clauses, resource accounting and required feature IDs together.
Never encode those obligations as ordinary `Copy`, `Branch` or `Return`.
The source-language [portable proof floor](../language-contract-review.md#4-a-reproducible-proof-and-evaluation-foundation)
is separate from body decoder and verifier work budgets.
