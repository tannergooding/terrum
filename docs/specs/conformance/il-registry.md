# Unified TRRM registry conformance

These cases apply the [registry](../representation/registry.md), its
[machine-readable catalog](../representation/trrm-1.registry.json),
[metadata](../il-metadata.md) and [Body contracts](../il-instruction-set.md).
The selected schema is `trrm/1-draft-unified-3`; `(1, 0)` alone is not
schema agreement.

[Fixed operand and complete artifact vectors](il-registry-vectors.json)
include literal octets, expected fields and logical programs. Complete
artifacts contain their header, directory, Module, String, TypeSpec,
MethodDef, MethodSig, Param where needed, and Body sections. The integer
example uses all three Body forms; floating examples retain their declared
types and result relationships. A fixture does not require a compiler to
produce one chosen Body form.

Finite structural/type/value assessments of these supplied instances are
not a general binary consumer, body-proof system, native implementation or
performance result. In particular, they do not close the separate
complete capability codecs, import/replacement relation, canonicality/resource
envelope, rich clause applicability, collector or physical owner/root codecs.
Five document-only cases below identify arbitrary-program/native obligations.
The [manifest companion](il-semantic-manifests.md) covers the governing-edition
field and exact Feature protocol; current artifact vectors carry edition `1`.

## 1. Registry agreement

| Case | Situation | Required result |
|---|---|---|
| RREG-01 | Enumerate every primary instruction allocation | One name, role, operand sequence and status for each assigned byte; 34 operations and six terminators |
| RREG-02 | Compare the registry with displayed metadata/instruction tables | Exact allocation, row-stride, operand and enum agreement; prose is not a competing registry |
| RREG-03 | Combine the formerly overlapping generic/floating shapes | Indexing uses `27..29`, calls `30`, floats `40..46`; no retired-number aliases |
| RREG-04 | Enumerate family bands and unused capacity | Disjoint operation/terminator roles; vector/memory/future bands allocate nothing merely by being reserved |
| RREG-05 | Encounter `00`, `7F`, `EF`, standalone/nested `F0`, or `F1..FF` | Reject invalid/reserved starts and undeclared extended envelopes |
| RREG-06 | Equal numeric values occur in several enum domains | Check the selected domain; zero/unknown semantic variants reject, unlike zero literal data |
| RREG-07 | Compare section kinds with definition-reference kinds | Preserve `u16` sections versus `u8` definition kinds; no truncation of `0113` into TypeDef `13` |
| RREG-08 | Inspect an assigned place, aggregate, exit or provisional family shape | Bounded recognition only; missing semantic/profile support still rejects execution |
| RREG-09 | Enumerate atomic/family codes | Preserve all 26 atomic identities and 16 family slots; unknown codes and `FF` reject, metadata assignment is not operation support |
| RREG-10 | Enumerate fixed rows, clause extents and flag domains | Exact strides and Effects/OutputState/Transfer/Dependency/Cleanup row extents `12/16/16/20/12`; unknown/reserved bits gain no meaning |
| RREG-11 | Read Param modes/flags and Module/Feature manifests | Only ordinary `00` and init-destination `01`, with the existing implicit flag restrictions; governing edition `1` selects the bounded baseline, and no extra capability IDs are registered |
| RREG-12 | Producer trust or an integrity check is offered instead of semantic truth | Required decoding/admission still applies; arbitrary body assertions need the selected trust/audit policy, not a registry flag |

## 2. Encoding and admission

| Case | Situation | Required result |
|---|---|---|
| RENC-01 | The complete checked-add artifact uses general, compact linear or compact indexed Body | Same ordered typed operations and logical result; each form retains its required fields and indexes. Equivalent wider used widths ordinarily load; explicit canonicality audits remain strict |
| RENC-02 | A floating comparison produces bool in a compact Body | Recover bool from its own mapped TypeSpec; reject a missing bool map or inconsistent indexed descriptor |
| RENC-03 | Call uses nonzero `m`, or zero plus a typed MethodDef row index | Decode the correct conditional field without a table selector; packed/nonlocal use remains outside the bounded executable profile |
| RENC-04 | PlaceField or Extract uses a FieldDef word under narrow/wide local widths | Consume exactly four little-endian bytes for that typed word, not a local ID |
| RENC-05 | Arguments, switch cases, edges and exit lists have different counts | Decode each nested count/namespace and consume the exact extent |
| RENC-06 | Truncate every fixed instruction and complete artifact fixture at each byte boundary | Reject before reading outside the supplied extent; no successful partial decode |
| RENC-07 | An operation appears at a terminator position, or vice versa | Reject by role, not operand plausibility |
| RENC-08 | Width layout has reserved bits, ref/count code 3 or layout zero after F0 | Reject; data code 3 instead means eight-byte data |
| RENC-09 | A nonzero width code selects an unused operand class | Reject; empty counted data lists do not create encoded data operands |
| RENC-10 | A required local ID is zero, versus unit-result nullable definition/return | Reject null required IDs; nullable unit forms retain their exact signature/definition rules |
| RENC-11 | Unknown/reserved opcode occurs in an uncalled Body or unselected block | Producer is nonconforming; reject when decoded/used or during a scoped full structural audit. Trusted selective access need not search unvisited streams |
| RENC-12 | A fixed table, directory or typed-reference field has an invalid stride, policy, count or reserved value | Reject at the affected framing/reference/admission phase |
| RENC-13 | The retired overlapping fragment `1B 03 01 02 83 03` is supplied as current-schema input | Reject unassigned `1B`; do not infer an old integer-to-float or range schema |
| RENC-14 | Required unknown section, unowned Feature row or unsupported retained manifest is present | Reject at the applicable directory/manifest admission; a known Feature codec is not extensible by producer choice |
| RENC-15 | Unknown `8000..FFFF` section uses self-contained, slotted or discard policy | Validate directory framing/policy; inspect slots when used or preserved, not merely to skip nonsemantic payloads |
| RENC-16 | References/counts cross 255/256 or 65535/65536; compact-derived value count crosses its local bound | Widen the affected class or reject before growing storage; never truncate or borrow another namespace's width |
| RENC-17 | A known but unsupported atomic/optional/nominal format is substituted for an admitted scalar | Preserve metadata identity and reject affected execution; no automatic wider-float, decimal, vector or optional-float support |

## 3. Floating families

| Case | Situation | Required result |
|---|---|---|
| RFLO-01 | FloatCompare uses each of six kinds with finite values, both zeros, infinities and NaNs in both formats | Numeric bool result; NaNs unordered, inequality true, signs of zero equal |
| RFLO-02 | Floating operands/result mix formats or use the integer Binary family | Reject type/domain mismatch without implicit conversion |
| RFLO-03 | ConstBits supplies IEEE zeros, infinities and NaN representations at admitted data widths | Preserve exact declared-width bits; reject high bits beyond the format, never round or quiet by loading |
| RFLO-04 | FloatNegate is applied to either sign, infinity or signaling/quiet NaN bits | Toggle exactly the sign bit; all other bits remain identical |
| RFLO-05 | FloatConvert uses either distinct-format pair or a same-format/unsupported destination | Distinct pairs use destination rounding; identity is Copy, unsupported pairs reject |
| RFLO-06 | IntToFloat uses the four integer sources and two destinations | Exactly eight typed pairs; nearest/ties-to-even direct conversion, no character or floating alias |
| RFLO-07 | Char16ToFloat uses each destination and boundary/surrogate code units | Interpret `0..65535` numerically; no scalar decoding or surrogate rejection |
| RFLO-08 | FloatToInteger uses eight reverse pairs, including a negative fraction to unsigned | Truncate toward zero before range checking; an in-range zero is a legitimate result |
| RFLO-09 | FloatToInteger receives NaN, infinity or an overflowing truncated value | Ordered overflow failure and no result/publication; no ISA sentinel or success-shaped default |
| RFLO-10 | Copy or eager Select carries a signed zero or NaN | Preserve the selected exact representation and existing eager effects |
| RFLO-11 | FloatBinary uses each kind and both exact formats | Four typed total families; finite fixture results have destination rounding, unknown kinds reject |
| RFLO-12 | Arbitrary floating arithmetic/conversion is lowered or transformed | Preserve source rounding, nonfinite classes and permitted NaN variation; no implicit FMA, reassociation, new format or remainder |
| RFLO-13 | Native execution uses ambient FP modes or conversion instructions | Preserve the caller's environment and specified results/failures; registry/finite fixtures are not target proof |

## 4. Rewriting and evidence

| Case | Situation | Required result |
|---|---|---|
| RREW-01 | Re-encode the earlier generic Body examples under the unified catalog | Decode under the explicit old schema, rewrite only instruction starts, and recover the same named operations/operands |
| RREW-02 | The same octet occurs as opcode, data, count, local ID, presence mask or offset | Only the opcode occurrence changes during primary reallocation |
| RREW-03 | Primary reallocation leaves every instruction length unchanged | Definition/block/terminator offsets and instruction ordinals remain unchanged; verify rather than assuming them |
| RREW-04 | TypeSpec row 255 becomes row 256 while local t1 stays t1 | Change the fixed map reference only; instruction bytes and local widths remain unchanged |
| RREW-05 | Body-local type-map order changes | Remap explicit local type operands and descriptors, including indexed/compact-derived types; preserve complete nominal meaning. Ordinary non-first-use order is accepted, not canonical |
| RREW-06 | A FieldDef row is remapped | Change its fixed reference word, not result/value IDs or the instruction width class |
| RREW-07 | A packed direct MethodDef row is remapped | Change only the typed row index after m=0 and preserve its selected meaning; no implicit local-call conversion |
| RREW-08 | Body rows reorder with SourceMap or unknown slotted data retained | Remap every affected method/slot target; preserve instruction meaning or drop the optional section |
| RREW-09 | An audited fixture's definition/terminator index points into an operand or the wrong instruction | Reject the disagreement before using it for selective import |
| RREW-10 | Rewriting retains an unsupported instruction or strips a required semantic section | Reject affected execution; dropping optional debug data cannot repair missing required semantics |
| RREW-11 | Packing/replacement changes nominal origins, imported expectations or shared structural context | Apply the complete identity/compatibility relation; equal strings, rows or layouts are not proof |
| RREW-12 | An arbitrary optimizer changes CFG, effects, cleanup, lifetimes or instruction lengths | Preserve all selected observations and rebuild affected metadata/indexes; finite byte reallocation is not a general transformation proof |
