# Register IL encoding-canonicality conformance

These cases apply [encoding canonicality](../representation/encoding-canonicality.md),
[consumer verification](../representation/reader-trust.md), [Body encoding](../il-instruction-set.md)
and exact schema `trrm/1-draft-layout-queries-1`. Ordinary acceptance, writer
conformance and an explicit canonicality audit are separate claims. The
fixtures establish only the stated byte/metadata facts, not arbitrary Body
truth, compiler support or native behavior.

## 1. Instruction forms and boundaries

| Case | Input or observation | Required outcome |
|---|---|---|
| ICF-01 | Tiny `09 00` loads ConstantMap entry 0 to R0 and returns R0 | Accept as two one-byte instructions; each first byte starts one instruction |
| ICF-02 | An exact tiny operation is encoded using its assigned short or long equivalent | Ordinary reading accepts; an instruction-canonicality audit rejects the longer equivalent |
| ICF-03 | A short instruction uses a register, HomeMap/TypeMap/MethodMap/ConstantMap or record index within its encoded nibble/byte limit | Decode the exact assigned operand namespace and meaning |
| ICF-04 | A long family has an assigned primary but an unassigned control or invalid unused field | Reject as malformed required input, not noncanonical |
| ICF-05 | An operand byte equals an assigned primary opcode | Treat it only as the operand; never recursively decode it |
| ICF-06 | A producer uses `F0` as a prefix, or supplies an unassigned first byte | Reject; this codec has no width prefix or nested decoder |
| ICF-07 | Long instruction payload is truncated or has trailing bytes | Reject before affected use |
| ICF-08 | `R15` appears as an unused field, discard result or source value | Apply only the exact operation-role rule; do not treat an unused field as a read or discard as cleanup |
| ICF-09 | A source/destination register overlaps in a multi-result operation | Read all sources before publishing results; distinct published results require distinct writable general registers |

The first-byte partitions are `00..0F` (one byte), `10..BF` (two bytes)
and `C0..FF` (four bytes). Unassigned values inside a partition do not
inherit the range's length as a semantic default.

## 2. Body framing, maps and records

| Case | Input or observation | Required outcome |
|---|---|---|
| ICB-01 | Body implementation kind is `01` and codec selector is `01` | Read the exact `17 + 4*popcount(mapMask)` prefix, present maps, fixed Block table, record index/data and exact code extent |
| ICB-02 | Implementation kind is `02..05` | Decode only its exact field/forwarder descriptor and validate target kind, signature and contract; do not infer a stream |
| ICB-03 | A reserved flag, nonzero reserved field, unknown implementation kind or unknown codec selector appears | Reject before affected use |
| ICB-04 | HomeMap, TypeMap, MethodMap, FieldMap, ConstantMap, Block or record index is `0xFFFF` with count 65,536 | Accept as the final valid zero-based index |
| ICB-05 | Any indexed inventory has 65,537 entries or an index is outside its count | Reject; no implicit wider local-index encoding exists |
| ICB-06 | `blockCount` is zero, or `recordCount` is zero | Reject; a stream has at least one Block and each Block needs a BlockEntryState record |
| ICB-07 | A Block has an empty/overlapping range, a gap in code coverage, or lacks exactly one terminal instruction | Reject |
| ICB-08 | Branch target is a Block index versus a code byte offset | Resolve only as a zero-based Block index |
| ICB-09 | `recordOffsets` is truncated, nonmonotonic, out of range, or does not end at the exact record-data extent | Reject |
| ICB-10 | A record has unknown kind, nonzero flags/reserved fields, malformed list count or trailing payload bytes | Reject before affected use |
| ICB-11 | A record index or list count is zero or `0xFFFF` | Interpret using its explicit field rule; zero is not absence unless the field says so, and `0xFFFF` is not a sentinel |
| ICB-12 | Reordered/duplicate/unused TypeMap, MethodMap, FieldMap or ConstantMap entries preserve every resolved reference | Ordinary reading accepts; a complete canonicality audit may reject noncanonical order, duplicates or unused entries |
| ICB-13 | Two HomeMap rows have equal types/flags, or a rewriter merges their indexes | Equal row fields remain distinct homes; remap only when the complete storage identity/state contract is preserved |
| ICB-14 | A stream Body uses an empty-present map | Ordinary reading accepts; canonical writers omit its count/payload and an explicit prefix audit rejects the bit |
| ICB-15 | FieldPath row repeats an identical complete root/ordered-step signature | A canonical metadata writer interns it; ordinary duplicate rows remain valid when references preserve their meanings and Import expectations |
| ICB-16 | A TypeIdentity `BlockEntryState` has nonzero unused type/access fields | Reject; the zero fields are reserved and are not remapped as TypeMap[0] |
| ICB-17 | A one-field path has an exact non-generic declaring root | Canonical FieldMap uses the direct FieldDef/Import token; an equivalent one-step FieldPath row may be accepted ordinarily but is noncanonical |
| ICB-18 | A constructed, qualified, formal or nested root is encoded as a FieldPath | Preserve the complete root TypeSpecRef and ordered FieldDef/Import identities; do not compress a multi-step path into an unassigned inline token |

The stream prefix uses bits 0..4 of `mapMask` for HomeMap, TypeMap,
MethodMap, FieldMap and ConstantMap, with one `u32` count per set bit.
Block/record/code counts remain mandatory `u32` values; zero-based local map
references are `u16`.
Global row tokens, code offsets/lengths and instruction ordinals retain
independent widths.

## 3. SourceMap locations

| Case | Input or observation | Required outcome |
|---|---|---|
| ICM-01 | SourceMap slot selector 1 targets a Body instruction | Require section kind zero, one-based Body row, zero-based `u16` Block index and zero-based `u32` instruction ordinal |
| ICM-02 | SourceMap slot selector 0 targets a SourceFile row | Require a valid SourceFile row and zero Block/ordinal fields |
| ICM-03 | SourceMap slot reserved field or selector/target-kind pairing is invalid | Reject when the mapping is interpreted or preserved |
| ICM-04 | A normal instruction mapping uses stepRole 0 | Map the whole instruction |
| ICM-05 | A recipe mapping uses stepRole 1..5 | Resolve A-negation, B-negation, inner, C-negation or outer at the exact assigned recipe step |
| ICM-06 | A non-recipe instruction uses stepRole 1..5, or stepRole is unknown | Reject the mapping |
| ICM-07 | A rewritten recipe changes source/failure steps | Regenerate SourceMap step roles or drop the optional section; do not retain stale locations |
| ICM-08 | SourceMap rows are not sorted by instruction slot, step role, source slot and span, or exact rows are duplicated | Ordinary readers may accept if structurally valid; a canonicality audit rejects |

Reference slots are 16 bytes. SourceMap rows are 20 bytes:
`instructionSlot, sourceFileSlot, startUtf16, lengthUtf16, stepRole,
flags=0, reserved=0`. The section length is
`4 + slotCount*16 + recordCount*20`.

## 4. Complete types and constants

| Case | Input or observation | Required outcome |
|---|---|---|
| ICTM-01 | TypeMap entry is a TypeSpec row, `0xF2`, `0xF3`, or eligible `0xF0`/`0xF1` pseudo token | Resolve exactly to a complete type; reject invalid token context or cyclic pseudo bases |
| ICTM-02 | Complete-type field uses inline F2/F3 or an equivalent row reference | Ordinary reading accepts both; canonical writers follow assigned token spelling and whole-artifact size/tie rules |
| ICTM-03 | `genericTypeSpec` uses an inline token, or a TypeSpecRef appears outside a complete-type field | Reject the wrong field form |
| ICTM-04 | TypeMap has a repeated token or two spellings resolve to the same complete type | Preserve exact type identity; canonical writers reuse the eligible entry |
| ICC-01 | ConstantMap entries have equal numeric values but different types, signed zeros, NaN payload/state or decimal cohorts | Preserve distinct exact representations; do not merge by numeric comparison |
| ICC-02 | Boolean, unit or char16 constant has an invalid value representation | Reject; a valid storage width does not make an invalid value legal |
| ICC-03 | R14 or R15 is loaded as a special constant | Preserve contextual zero or the exact all-one pattern; do not quiet, canonicalize or reinterpret it as a numeric maximum |
| ICC-04 | A ConstantMap entry is duplicated or unused but valid | Ordinary reading accepts; canonical writer reuses identical typed values and omits unused entries |

## 5. Rewriting and audit scope

| Case | Input or observation | Required outcome |
|---|---|---|
| ICR-01 | Instruction form or length changes | Rebuild affected Block code offsets/lengths, codeByteLength, record offsets and containing Body extent |
| ICR-02 | TypeMap/MethodMap/ConstantMap is reordered | Remap every instruction and typed-record reference in its actual namespace |
| ICR-03 | A metadata token's target row or Import binding changes | Rebuild its fixed-width token and revalidate exact identity/contract; do not substitute matching bytes |
| ICR-04 | Blocks or instruction ordinals change with SourceMap present | Remap all source slots and step roles only when meaning is preserved; otherwise regenerate or drop SourceMap |
| ICR-05 | Partial inspection is offered as a complete canonicality claim | Decline the complete claim and report the inspected scope |
| ICR-06 | Canonical bytes are offered as proof of producer truth, dominance, ownership, effects or native correctness | Reject the claim; canonicality is not semantic proof or implementation support |
| ICR-07 | Required interpretation is unsupported or audit work is exhausted | Decline the requested claim explicitly; do not guess, skip work or return success-shaped evidence |
