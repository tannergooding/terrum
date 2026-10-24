# Unified TRRM registry conformance

These cases apply the [registry](../representation/registry.md), its
[machine-readable catalog](../representation/trrm-1.registry.json),
[metadata](../il-metadata.md) and [Body contracts](../il-instruction-set.md).
The selected schema is `trrm/1-draft-register-il-1`; `(1, 0)` alone is not
schema agreement.

[Fixed operand, typed-record and complete artifact vectors](il-registry-vectors.json)
include literal octets, expected fields and logical programs. RequirementFailure
vectors exercise Boolean and uint8-mask conditions, true/false outcomes and
distinct register selectors; BlockEntryState vectors distinguish value and
place-handle inputs. Complete
artifacts contain their header, directory, Module, String, TypeSpec,
MethodDef, MethodSig, Param where needed, and Body sections. The integer
artifact exercises tiny and long register instructions in one stream. The
instruction vectors cover representative tiny/short/long layouts; a separate
SourceMap payload vector covers the zero-based Block and step-role layout.
Separate token vectors cover well-known and pseudo-type payloads.

Finite structural/type/value assessments of these supplied instances are
not a general binary consumer, body-proof system, native implementation or
performance result. In particular, they do not close the separate
complete capability codecs, import/replacement relation, canonicality/resource
envelope, rich clause applicability, collector or physical owner/root codecs.
The [manifest companion](il-semantic-manifests.md) covers the governing-edition
field and exact Feature protocol; current artifact vectors carry edition `1`.
Cases that describe malformed, unsafe-to-consume bytes or unsupported forms
require structural checks and support admission to reject affected use.
Semantically false but structurally consumable producer facts need not be
re-proved by consumers. The cases do not prescribe diagnostic wording or
internal check organization.

## 1. Registry agreement

| Case | Situation | Required result |
|---|---|---|
| RREG-01 | Enumerate the instruction-byte namespace | `00..0F` are one-byte tiny tuples, `10..BF` two-byte short forms and `C0..FF` four-byte long forms; no width prefix exists |
| RREG-02 | Compare the registry with displayed metadata/instruction tables | Exact tiny/short/long assignments, controls, record layouts and section strides agree; the registry remains the numeric authority |
| RREG-03 | Enumerate all assigned instruction and record values | T0..T15, assigned short forms, `C0..CC`, `D0..DD`, `E0..E8`, `EA..EB`, and record kinds `01..0B` match the machine registry |
| RREG-04 | Decode an unlisted primary byte or family control | Reject it; reserved capacity does not create an escape, operation, profile or compatibility alias |
| RREG-05 | A producer emits `F0` as a prefix, recursively decodes an operand byte, or uses an unassigned primary | Reject the required Body; the stream has fixed 1/2/4-byte instructions and no recursive decoding |
| RREG-06 | A long family uses R14 as a writable result, a non-R15 register in an unused register field, an invalid selector or a nonzero reserved field | Reject; unused register fields require R15, while reserved non-register bits require zero |
| RREG-07 | Equal numeric values occur in different index/reference namespaces | Resolve by the field definition: Body-local `u16` indexes are zero-based; metadata tokens remain fixed-width artifact references |
| RREG-08 | An assigned operation is used with a type outside its operation matrix | Reject affected use; opcode or type assignment alone does not admit the pair |
| RREG-09 | Enumerate atomic, TypeSpec-family, pseudo-family and TypeSpecRef token codes | Preserve 26 atomic identities, assigned TypeSpec families, F0/F1 Body-local pseudo types, F2 well-known types and F3 inline built-in families; `simd*mask<T>` has no separate type code |
| RREG-10 | Enumerate fixed rows, clause extents and flag domains | Producers use exact strides and Effects/OutputState/Transfer/Dependency/Cleanup row extents `12/20/16/20/12`; unknown/reserved bits gain no meaning |
| RREG-11 | Read Param modes/flags and Module/Feature manifests | `ref<init T>` uses TypeSpec form `0x0E` with ordinary Param mode `00`; mode `01` is reserved. Edition `1` has no reduced GA floor; required capabilities need their complete registered closure |
| RREG-12 | A consumer uses a producer-published semantic claim | Validate framing, references and support before affected use; the producer remains responsible for truth, and an integrity check or registry flag does not prove it |
| RREG-13 | A Body requires lexical handlers, filters, `finally`, exception-object transport or region cleanup | Declining affected use is required until the ExceptionRegion/handler carrier and runtime capability are assigned; `Throw`/`RegionExit` opcode shapes do not supply that directory |
| RREG-14 | A required operation/call uses assigned rows but one required capability carrier is absent | No support claim for the affected semantics; row presence alone is not support, and no permanent category-wide profile cap is implied |
| RREG-15 | A failure is raised by an operation/call with an exact failure contract or source `throw e;` carries an exception object | Preserve that failure/event contract; the operation opcode does not replace exception-object transport or handler-region metadata |
| RREG-16 | B3/E7 references a `RequirementFailure` record | Resolve its explicit Boolean or uint8-mask kind, register and predicate; exercise true/false conditions and distinct registers without reevaluating the predicate |
| RREG-17 | A `BlockEntryState` row carries a live register | `carrierKind=0` requires a value type and `accessKind=0`; `carrierKind=1` requires a referent type and assigned place access; preserve dynamic backing/loan/provenance facts through verified edges |
| RREG-18 | A Body TypeMap entry refers to a TypeSpec row | Use the Body token kind `11`; general TypeSpecRef row form `00` is not an alias in this field |
| RREG-19 | A tiny/short Home, reference or place store is selected | Accept only `sourceUse=1` with a Copy-eligible source and valid destination state/authority; full Home `D6` admits its explicit Transfer/BorrowRead modes when dependencies are derivable; other modes or per-store formal/dependency facts require SemanticStore |
| RREG-20 | `C0 01 F0 10` uses R15 in the unused result slot | Accept R15 as the role-specific unused field; reject a different register there, while reserved non-register bits remain zero |

## 2. Encoding and admission

| Case | Situation | Required result |
|---|---|---|
| RENC-01 | A Body uses a tiny, short or long instruction form | Decode fixed length from the primary byte; do not read a prefix, shared width class or block mode |
| RENC-02 | An exact operation has both a short and long encoding | Ordinary reading accepts both; canonical writer/audit selects the shortest exact form |
| RENC-03 | Body implementation kind is `01..05` | Read the exact descriptor or 33-byte instruction-stream prefix; never infer the kind from row contents |
| RENC-04 | Body-local index is `0`, `0xFFFF` with count 65,536, or just outside the count | Accept the in-range endpoints; reject out-of-range values without implicit widening |
| RENC-05 | HomeMap, TypeMap, MethodMap, ConstantMap, Block or record count crosses 65,536 | Reject 65,537-entry inventories; counts use `u32`, indexes remain `u16` |
| RENC-06 | Block table or instruction bytes are truncated, overlapped, gapped or do not end in one terminator per block | Reject before affected use |
| RENC-07 | `recordOffsets` has an invalid count, range, order or final extent | Reject; each record's exact header/payload extent must be consumed |
| RENC-08 | Unknown record kind, reserved flags or invalid record-control combination occurs | Reject before affected use |
| RENC-09 | SourceMap selector 1 uses a nonzero section kind, one-based Block index or nonzero reserved field | Reject the malformed selected mapping |
| RENC-10 | SourceMap stepRole is 1..5 for an assigned arithmetic recipe | Resolve A-negation, B-negation, inner, C-negation or outer; reject these roles on other instructions |
| RENC-11 | TypeMap includes TypeSpec, TypeDef, type Import, F2, F3 or Body pseudo token | Resolve each token to one complete type; F3 uses an assigned family over an atomic argument; pseudo bases are zero-based TypeMap indexes and cycles reject |
| RENC-12 | TypeSpecRef uses `0xF4..0xFE`, `0xFF`, or an invalid F3 payload | Reject unassigned token kinds, reserved indirection and type-inapplicable combinations |
| RENC-13 | A complete-type field uses TypeSpec row, F2 or eligible F3 | Resolve to the same complete type; nested type-valued fields follow TypeSpecRef rules; `genericTypeSpec` remains row-only |
| RENC-14 | TypeMap/MethodMap/ConstantMap order changes consistently, or contains duplicates/unused entries | Ordinary reading preserves meaning; a complete canonicality audit checks first-use order, reuse and omission |
| RENC-15 | Required unknown section, unowned Feature row or unsupported retained manifest is present | Reject affected use; these do not establish a support claim |
| RENC-16 | Unknown `8000..FFFF` section uses self-contained, slotted or discard policy | Verify framing and the registered nonsemantic policy; ignore or preserve only as that policy permits |
| RENC-17 | A type is used with an operation outside its assigned type matrix | Reject unsupported required use; type identity alone does not assign an operation |
| RENC-18 | All 256 wrapper/modifier byte values are enumerated for both pseudo-type nesting kinds | Test each assigned wrapper with fixed per-position modifier validity; wrapper codes `6..7` reject |
| RENC-19 | A pseudo token represents init/write/gc composition | Preserve distinct qualifier positions and reject invalid type-family/qualifier combinations |
| RENC-20 | F3 uses an unassigned family/argument, reserved qualifier or invalid Span init placement | Reject; `init` is allowed only in the immediate Span-argument position |
| RENC-21 | `genericTypeSpec` or a constant-formal argument uses the wrong reference kind | Keep generic definitions row-only and constant arguments as ConstantSource references |
| RENC-22 | TypeSpecRef row movement changes a row-backed token | Rebuild the fixed token and all section/index extents; inline F2/F3 payloads remain unchanged |
| RENC-23 | Typed-record order changes, or duplicate/unused records are retained | Ordinary reading preserves all referenced meanings; canonical writers use first logical use, reuse equivalent records and omit unused entries |
| RENC-24 | Short `B0 2A` or `90 2A` carries a Block/CallSite index above 15 | Consume the full operand byte as index 42, not a register-nibble pair |
| RENC-25 | Short `B1 43` carries a Boolean register and ConditionalEdge index | Split the operand byte into low `x=R3` and high `y=record index 4` nibbles |

## 3. Integer, Boolean and conversion families

| Case | Situation | Required result |
|---|---|---|
| RINT-01 | `C1` selects Boolean NOT or integer bitwise NOT | Boolean NOT accepts only `bool`; bitwise NOT accepts the assigned integer identities and preserves the declared width |
| RINT-02 | `C1` selects AND/OR/XOR | Accept equal integer types or two `bool` values; Boolean forms are eager and two-valued |
| RINT-03 | `C2` selects relation, equality or inequality; `40`/`41` consume or test the relation | Equality/inequality accept the assigned scalar identities; ordering admits only its specified domains, and relation output is one-hot uint8 |
| RINT-04 | `C0` checked/wrapping controls or `C7..C9` recipes use each integer primitive | Preserve exact type and width; checked forms use ordered overflow failures, wrapping forms use modulo `2^w`, and C9 clamps each mathematical node, including signed-minimum/unsigned saturating negation |
| RINT-05 | `C0` controls divide, remainder or DivRem exercise zero and signed minimum / `-1` | Divide fails for zero and signed quotient overflow; remainder for signed minimum `% -1` is zero; combined DivRem fails once and publishes neither result |
| RINT-06 | `C1` shift controls use signed/unsigned data types and legal count boundaries | Validate the source count in `[0,width)` before conversion; signed arithmetic right-shifts sign-fill and logical shifts zero-fill |
| RINT-07 | `C0` checked/wrapping negation uses signed minimum and unsigned zero/nonzero | Checked signed negation fails at minimum; unsigned negation follows checked/wrapping `0-x` semantics without promotion |
| RINT-08 | `D0..D3` convert integer source/destination pairs | D0 follows the selected ordinary pair contract, D1 wraps integers, D2 saturates admitted pairs and D3 requires an exact representation-valid pair |
| RINT-09 | A source conversion is outside an assigned instruction pair | Do not infer conversion from a type code; require an exact selected callable or other assigned contract |
| RINT-10 | ConstantMap carries an integer value, including a signed bit pattern or value beyond 64 bits | Preserve the exact typed ConstantSource representation; do not synthesize an inline bit-width form or truncate a wide value |

## 4. Floating and decimal families

| Case | Situation | Required result |
|---|---|---|
| RFLO-01 | `C2` relation or equality controls compare finite values, both zeros, infinities and NaNs | Numeric relation/Boolean result follows the selected type contract; NaNs are unordered where supported, signed zeros compare equal |
| RFLO-02 | Floating operands/results mix formats or use an integer-only family | Reject type/domain mismatch without implicit conversion |
| RFLO-03 | ConstantMap supplies IEEE zeros, infinities or NaN representations | Preserve exact declared-format bits; loading never rounds, quiets or canonicalizes |
| RFLO-04 | Short or long strict floating negation sees either sign, infinity or signaling/quiet NaN bits | Toggle exactly the representation's sign component; preserve every other bit |
| RFLO-05 | `D0` converts between distinct floating formats or targets the same format | Distinct pairs use destination rounding; identity uses value aliasing; unsupported pairs reject |
| RFLO-06 | `D0` converts each assigned integer source to a floating destination | Use the exact admitted pairs and rounding rule; do not infer character or floating aliases |
| RFLO-07 | `D0` converts char16 to each assigned floating destination | Interpret code units `0..65535` numerically; do not decode Unicode scalars or reject surrogates |
| RFLO-08 | `D0` converts floating values to integer destinations, including negative fractions to unsigned | Truncate toward zero before range checking; an in-range zero is a legitimate result |
| RFLO-09 | Floating-to-integer conversion receives NaN, infinity or an overflowing truncated value | Propagate the assigned conversion failure with no result/publication; no sentinel or success-shaped default |
| RFLO-10 | `mov`, conditional selection or `selectcc` carries signed zero or NaN | Preserve the selected exact representation and existing eager effects |
| RFLO-11 | `C0` strict floating arithmetic uses add/subtract/multiply/divide/remainder | Preserve destination rounding, truncating-quotient remainder and exact source failure rules; unknown family controls reject |
| RFLO-12 | Arbitrary floating arithmetic/conversion is lowered or transformed | Preserve source rounding, nonfinite classes and permitted NaN variation; no implicit FMA, reassociation, new format or remainder |
| RFLO-13 | Native execution uses ambient FP modes or conversion instructions | Preserve the caller's environment and specified results/failures; registry/finite fixtures are not target proof |
| RFLO-14 | Strict negation operates on binary or decimal formats | Toggle only the selected representation's sign component; preserve all other bits/cohort data |
| RFLO-15 | `C2` compares decimal cohorts and special values | Compare numeric values, preserve cohort-equality semantics and apply the selected unordered NaN rules |
| RFLO-16 | `C0` strict decimal arithmetic reaches precision/exponent boundaries | Preserve preferred exponents, rounding, exact truncating remainder and quiet-NaN results; no binary64 intermediate |
| RFLO-17 | ConstantMap targets an unassigned constant representation | Reject unsupported constant representation; operation support alone does not assign its constant codec |

## 5. Semantic display and layer ownership

| Case | Situation | Required result |
|---|---|---|
| RDISP-01 | Checked and wrapping integer addition use the same operand type | Display `add.checked` versus `add.wrap`; retain distinct overflow behavior and result obligations |
| RDISP-02 | Integer and floating addition share the semantic root `add` | Display their complete types and retain distinct arithmetic, rounding and failure contracts; do not infer one common operation policy |
| RDISP-03 | A selected source query is shown in semantic IL | Reuse its existing source identity and contract; do not create a Core member, opcode or alternate query, and retain the selected `ICountable.Count : nint` witness for `countof(Place)` |
| RDISP-04 | `const` displays a value produced by required evaluation or optional folding | Required execution retains its `ConstantExecution` request and dependencies; optional folding needs no optimizer-history record. The display encodes neither provenance, and required work cannot fall back to runtime |
| RDISP-05 | A semantic operation is compiled by JIT/AOT or lowered to target IR/assembly | Preserve its typed source contract and actual dependencies; the display does not prescribe a machine instruction, helper, or runtime support API |
| RDISP-06 | A reader/writer exposes a wire operation family or enum key beside a semantic display | Keep registry identifiers and operand schemas authoritative for encoding; display spelling is neither a binary alias nor a new allocation |

## 6. Rewriting and evidence

| Case | Situation | Required result |
|---|---|---|
| RREW-01 | A historical unpublished Body is explicitly selected for migration | Decode only under its exact historical schema, reconstruct logical operations and emit the register schema; current readers do not guess old meanings from `(1,0)` |
| RREW-02 | Opcode-valued octets also occur in operands, constants, record payloads or offsets | Rewrite only decoded operation starts; other namespaces retain their own values |
| RREW-03 | An operation changes from one-byte to four-byte form or vice versa | Rebuild affected Block code offsets/lengths, `codeByteLength` and the containing Body section offsets |
| RREW-04 | A TypeSpec row token moves while its TypeMap entry remains | Rebuild the fixed TypeMap token; zero-based `u16` local references remain unchanged |
| RREW-05 | TypeMap, MethodMap or ConstantMap order changes | Remap every instruction and typed-record index in its namespace; preserve resolved types, exact Import expectations and typed constant identity |
| RREW-06 | A FieldDef, MethodDef or Import token is remapped | Rebuild the exact fixed-width token; do not substitute equal names, signatures or row bytes |
| RREW-07 | Record order or record payload length changes | Rebuild all record indexes/offsets and every instruction/record reference |
| RREW-08 | Block order or code layout changes | Remap every branch target, Block entry reference, SourceMap slot and instruction ordinal; preserve the same CFG and operation order |
| RREW-09 | A byte offset points into an operand, record or wrong instruction ordinal | Reject before affected use; decode boundaries from assigned primary bytes and Block code extents |
| RREW-10 | A TypeSpec row moves or becomes unused while TypeSpecRef fields contain inline F2/F3 forms | Rebuild affected row references, nested TypeSpecRef fields, ranges and offsets; preserve valid F2/F3 tokens, and retain `genericTypeSpec` as a row reference |
| RREW-11 | A rewrite retains an unsupported instruction or strips required semantic data | Decline support for affected semantics; dropping optional debug data does not supply missing requirements |
| RREW-12 | Packing/replacement changes nominal origins, imported expectations or shared structural context | Apply the complete identity/compatibility relation; equal strings, rows or layouts are not proof |
| RREW-13 | A SourceMap instruction slot has selector 1 and section kind `0020`, an invalid Block index or stale step role | Reject selected mapping use or regenerate/drop the optional section under its registered policy |
| RREW-14 | An optimizer changes CFG, effects, cleanup, lifetimes or operation order while re-encoding | Preserve all selected observations and rebuild affected Body/SourceMap metadata; byte reallocation alone is not a general transformation proof |
