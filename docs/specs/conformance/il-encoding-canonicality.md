# Encoding-canonicality conformance

These cases apply [encoding canonicality](../representation/encoding-canonicality.md),
[reader trust](../representation/reader-trust.md), [Body encoding](../il-instruction-set.md)
and the exact `trrm/1-draft-unified-3` schema. Ordinary acceptance,
writer conformance and explicit canonicality auditing are separate.
Assessed fragments include their stated type/signature context; they do not
claim arbitrary Body truth or a production reader.

These compact-linear rows both return int32 42 using TypeSpec row 1.
Only the Body-local fields change width; the instruction and fixed map
word do not:

```text
04 01 01 00 00 00 01 02 01 01 2A 83 01
05 01 00 01 00 00 00 01 00 02 01 01 2A 83 01
```

Both ordinarily load. The first has canonical local width; an explicit
canonicality audit rejects the second. A valid general-form encoding of
the same Body is not rejected merely because compact-linear is eligible.

## 1. Consumer, writer and audit boundaries

| Case | Input or observation | Required outcome |
|---|---|---|
| ICLD-01 | Canonical supported fixture is ordinarily read and explicitly audited | Both accept their stated scope |
| ICLD-02 | Equivalent supported fixture has nonminimal used widths | Ordinary reading accepts; canonicality audit rejects |
| ICLD-03 | Wider code belongs to an unused operand class | Reject as invalid encoding, not noncanonical |
| ICLD-04 | A noncanonical producer's bytes pass ordinary access | Reader acceptance does not certify writer conformance |
| ICLD-05 | Trusted indexed reader visits one definition/terminator only | No preliminary canonicality scan of other operations or maps |
| ICLD-06 | Actual access exposes invalid bits, references, type or index agreement | Reject despite permissive width policy or producer trust |
| ICLD-07 | A known shape needs an unsupported retained feature/profile | Decline affected use; canonicality does not expand support |
| ICLD-08 | Required access/audit exhausts its supplied fixture budget | Explicitly decline; no skipped-work certificate |
| ICLD-09 | Tool checked selected records and offers a whole-artifact canonicality claim | Reject the overbroad claim; report the actual scope |
| ICLD-10 | Ordinary loading or canonicality is offered as arbitrary source/runtime certification | Requires independent evidence; no such claim |

## 2. Instruction classes and boundaries

| Case | Input or observation | Required outcome |
|---|---|---|
| ICIW-01 | Local reference crosses 255/256 | Canonical reference width changes from one to two bytes; assigned wider ordinary forms remain equivalent |
| ICIW-02 | Local reference crosses 65,535/65,536 | Canonical reference width changes from two to four bytes |
| ICIW-03 | References and counts are encoded wider than their maxima require | Decode the same unsigned operands; explicit audit rejects the wider used classes |
| ICIW-04 | A used list count is zero and has wider assigned count width | Ordinary reading accepts; zero is still an encoded count |
| ICIW-05 | Switch has no cases but selects wider data width | Reject the unused data class |
| ICIW-06 | Signed data straddles -129/-128, 127/128, -32,769/-32,768 and 32,767/32,768 | Minimal signed width follows mathematical range, not unsigned magnitude |
| ICIW-07 | Unsigned patterns straddle 255/256, 65,535/65,536 and 32-bit maximum/next value | Minimal unsigned data width follows the exact pattern |
| ICIW-08 | Adjacent operations need different reference/count/data widths | Each instruction selects its own class widths; no Body-wide instruction width |
| ICIW-09 | Fixed FieldDef row is 256 or 65,536 while local IDs remain small | Four-byte row changes independently; it does not widen the local class |
| ICIW-10 | Packed Call uses local zero and a large typed MethodDef row | Escape remains fixed four-byte typed row; local zero still needs only one reference byte canonically |
| ICIW-11 | Prefix has layout zero, reserved bits or reference/count code 3 | Reject; no equivalent-encoding exception |
| ICIW-12 | Used data class selects code 3 | Eight bytes are assigned; audit compares them to the decoded pattern/value's minimum |

## 3. Body-local fields and omitted inventories

| Case | Input or observation | Required outcome |
|---|---|---|
| ICBW-01 | Same supported Body uses general, compact-linear or compact-indexed form | Preserve logical records; no universal smallest-form requirement |
| ICBW-02 | General or compact Body uses wider assigned local fields, including offsets enlarged by that width | Ordinary reading accepts; audit tests narrower structural re-encoding so self-enlarged offsets cannot justify a nonminimal width |
| ICBW-03 | General-form fixed offset exceeds 255 while its local fields fit one byte | Fixed u32 offsets do not force wider local fields |
| ICBW-04 | The one-byte compact-indexed candidate's block-data length crosses 255/256 | Exact 255 fits one-byte offsets; the 256-byte candidate requires a wider local re-encoding |
| ICBW-05 | The two-byte compact-indexed candidate's block-data length crosses 65,535/65,536 | Exact 65,535 fits; the 65,536-byte candidate requires four-byte local re-encoding |
| ICBW-06 | Definition offset is an all-ones block-parameter sentinel | Treat it as a marker and regenerate it at the chosen offset width; not a numeric widening trigger |
| ICBW-07 | Compact-linear signature-derived parameter inventory crosses 255/256 | Include the omitted value inventory; reject too-narrow Body-local width before storage growth |
| ICBW-08 | Signature-derived inventory crosses 65,535/65,536 | Same rule at the two/four-byte boundary |
| ICBW-09 | Multi-result operations push compact-derived value count past its selected bound | Include every result in declaration order; do not count only operations |
| ICBW-10 | Body form/width code, reserved bits or a required field is invalid/truncated | Reject structural invalidity |
| ICBW-11 | A whole-Body local-width certificate considers only one selected block | Insufficient scope; inspect the complete relevant local inventory |
| ICBW-12 | General form is eligible where compact would save bytes | Valid form is accepted; width canonicality is within the chosen form |

## 4. Type-map ordering and validity

| Case | Input or observation | Required outcome |
|---|---|---|
| ICTM-01 | Map follows first logical descriptor use | Canonical descriptor-first order, not metadata row-number order |
| ICTM-02 | A type occurs only in explicit instruction operands | Its first stream use follows descriptor and required signature-only uses |
| ICTM-03 | Omitted-storage form requires signature-only types | Include them in declared order; physical scan order does not replace logical ordering |
| ICTM-04 | Map order and every local type selection are consistently permuted | Ordinary reading preserves types; audit rejects a non-first-use order |
| ICTM-05 | Map adds a distinct valid unused entry | Ordinary reading accepts; canonical writer omits it and audit rejects it |
| ICTM-06 | Required signature/descriptor/operation type is missing | Reject; permissiveness does not fabricate a map entry |
| ICTM-07 | An entry repeats the same typed TypeSpec reference | Reject duplicate-map invalidity, not merely a canonicality deviation |
| ICTM-08 | Local type ID or fixed map word is null/out of its actual namespace | Reject on access |
| ICTM-09 | TypeSpec row 255 becomes 256 while local t1 remains t1 | Keep fixed four-byte map words and independent local widths |
| ICTM-10 | Equal layout/row numbers are offered as complete nominal/generic identity | Need independent formation/binding evidence; no identity inference |

## 5. Constant domains and exact representations

| Case | Input or observation | Required outcome |
|---|---|---|
| ICCV-01 | int32 ConstBits pattern FFFFFFFF uses four bytes or eight with zero high bytes | Both mean -1; canonical data width is four |
| ICCV-02 | Boolean patterns 0/1 versus 2 use each assigned width | Ordinary 0/1 forms preserve bool; 2 is invalid at every width |
| ICCV-03 | void zero versus nonzero uses each assigned width | Zero preserves unit; every nonzero pattern rejects |
| ICCV-04 | char16 boundaries and surrogate code units use wider fields | Preserve code units through 65,535; reject larger values without Unicode-scalar reinterpretation |
| ICCV-05 | Signed int32 -1 uses sign-extended one/two/four/eight-byte data | Same mathematical -1; only one byte is canonical |
| ICCV-06 | ConstSigned negative value targets an unsigned type | Reject even with wider/sign-extended data |
| ICCV-07 | Decoded signed mathematical value exceeds its declared destination | Reject; storage width is not a conversion |
| ICCV-08 | binary32 positive zero uses each assigned data width | Exact zero bits; only one byte is canonical |
| ICCV-09 | binary32 negative zero uses four/eight-byte data | Preserve sign bit; four bytes are canonical |
| ICCV-10 | Signaling/quiet NaN signs and payloads are carried in wider fields | Preserve every exact declared-format bit; no arithmetic or quieting |
| ICCV-11 | Nonzero bits above a declared 32-bit pattern are encoded in eight bytes | Reject, not truncate |
| ICCV-12 | Wider data is offered as a new floating/decimal/vector/nominal codec | Decline unsupported use; no feature allocation from width |

## 6. Re-encoding and honest claims

| Case | Input or observation | Required outcome |
|---|---|---|
| ICRA-01 | An instruction is shrunk/widened | Repair its definition, terminator, block and containing-row offsets; retain logical ordinals |
| ICRA-02 | Body-local width changes | Repair structural strides, offsets and sentinel width while preserving decoded records |
| ICRA-03 | Type map is reordered | Remap every descriptor and explicit local type operand in context |
| ICRA-04 | Partial inspection is used as a full canonicality certificate | Decline the full claim; report only established scope |
| ICRA-05 | Required operation/type interpretation for an audit is unknown or unsupported | Reject/decline the requested claim instead of guessing |
| ICRA-06 | Canonical bytes are offered as proof of dominance, effects, ownership or producer truth | No such proof; retain separate audits/trust |
| ICRA-07 | A canonical byte digest is offered as selected nominal identity or logical equivalence | No such proof; binding/context still apply |
| ICRA-08 | Smaller bytes are offered as a measured decoding/throughput improvement | Requires independent measurement |
| ICRA-09 | Canonicalization is offered as permission to remove observable operations | Requires separate semantic-preservation evidence |
| ICRA-10 | Fixture accounting is offered as a universal numerical resource envelope | Resource policy remains a separate selected contract |
