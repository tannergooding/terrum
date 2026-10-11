# Windows x64 target cases

These cases apply to [windows-x64-native/1](../targets/windows-x64.md) with
the stated complete declarations and admitted support. They distinguish
physical storage/calls, supplied contract decisions and actual target/runtime
proof. A finite model or C fixture does not establish Terrum compiler
conformance, native body truth, collector support or exception interchange.

## 1. Scalar storage and validity

| Case | Fixture | Required outcome |
|---|---|---|
| WXSC-01 | All 26 core scalars | Exactly the specified target widths/alignments, including void 0/1 and 128-bit formats 16/16 |
| WXSC-02 | nint/nuint and equal-width int64/uint64 | Width 64 without identity coalescing or mapping Windows long to nint |
| WXSC-03 | uint64 bits 0x0123456789ABCDEF | Bytes EF CD AB 89 67 45 23 01 |
| WXSC-04 | Boolean bytes 0, 1, 2, 255 | First two valid; latter two cannot publish bool or silently mean true |
| WXSC-05 | Rune boundaries D7FF, D800, DFFF, E000, 10FFFF, 110000 | D7FF/E000/10FFFF valid; surrogates and 110000 invalid |
| WXSC-06 | Raw char16 D800 and char32 FFFFFFFF | Valid raw code units, not validated runes or implicit text decoding |
| WXSC-07 | Zero/subnormal/normal/infinity/NaN classes in all five binary formats | Exact sign/exponent/fraction decoding and format-specific bias |
| WXSC-08 | Native pointer/plain code address | Eight-byte aligned carrier; no inferred descriptor/root/provenance support |
| WXSC-09 | Copy negative zero and signaling/quiet NaN bit containers | Preserve every bit without arithmetic, quieting or a trap |
| WXSC-10 | Actual naturally aligned primitive/wrapper access and packed/wide/aggregate alternatives | Prove required non-tearing accesses; no size-only aggregate, sixteen-byte or unaligned atomic promise |
| WXSC-11 | bool versus Win32 BOOL; float64 versus Windows long double | Boolean contracts differ; binary64 matches without admitting float128 |
| WXSC-12 | Stored ref/own/class/view/task/optional representation supplied only as a pointer size | Decline absent complete representation; do not derive it from this table |

## 2. BID decimal

| Case | Fixture | Required outcome |
|---|---|---|
| WXBID-01 | decimal32 zero at q=0 and coefficient 550 at q=-2 | Containers 32800000 and 31800226 |
| WXBID-02 | decimal64/decimal128 zero at q=0 | Containers 31C0000000000000 and 30400000000000000000000000000000 |
| WXBID-03 | Each finite format at minimum/maximum quantum exponent | Decode exactly -101/90, -398/369 and -6176/6111 |
| WXBID-04 | Coefficient 10^p-1 at maximum q | Valid largest finite coefficient/exponent pair; no binary64 intermediary |
| WXBID-05 | decimal32 steering bits 60000000 | Canonical coefficient 8388608, q=-101 |
| WXBID-06 | decimal64 steering bits 6000000000000000 | Canonical coefficient 9007199254740992, q=-398 |
| WXBID-07 | decimal32/64 steering coefficients above 10^p-1 | Numeric zero at original sign/q; unchanged original bit container on copy |
| WXBID-08 | Finite decimal128 steering encoding | Always noncanonical numeric zero with decoded sign/q |
| WXBID-09 | Non-steering decimal128 coefficient 10^34 | Noncanonical zero, not invalid typed scalar |
| WXBID-10 | Infinity/NaN patterns also beginning with steering 11 | Special-class recognition precedes finite decoding |
| WXBID-11 | Signed infinity with nonzero trailing bits | Same signed infinity; copy retains bits and canonical production clears trailing bits |
| WXBID-12 | Decimal NaN signaling bit versus binary quiet bit | Decimal one is signaling; binary one is quiet |
| WXBID-13 | NaN payload 10^(p-1)-1 and then 10^(p-1) | First canonical; second numeric payload zero with raw copy unchanged |
| WXBID-14 | Decimal NaN reserved production bits set | Noncanonical NaN, not scalar rejection; bit-preserving observation retains them |
| WXBID-15 | Equal-valued decimal cohorts, signed zeros and copied noncanonical values | Numeric equality does not erase promised coefficient/q/sign/container observations |
| WXBID-16 | BID storage claim offered as a new ConstBits/Feature codec | Decline missing physical codec; no schema or allocation change |

## 3. Aggregate layout and access

| Case | Fixture | Required outcome |
|---|---|---|
| WXAG-01 | Sequential uint8 then uint32, defaults | Offsets 0/4, size 8, alignment 4 |
| WXAG-02 | uint8, WXAG-01 struct, uint16, defaults | Offsets 0/4/12, size 16, alignment 4 |
| WXAG-03 | WXAG-01 with Packing=1 | Offsets 0/1, size 5, alignment 1 |
| WXAG-04 | WXAG-01 with Alignment=32 | Offsets 0/4, size 32, alignment 32 |
| WXAG-05 | WXAG-01 with Packing=1, Alignment=32 | Offsets 0/1, size 32, alignment 32 |
| WXAG-06 | uint8, WXAG-04 struct, uint16 under Packing=1 | Explicit nested alignment survives: offsets 0/32/64, size 96, alignment 32 |
| WXAG-07 | uint8, WXAG-01 struct, uint16 under Packing=1 | Nested internal layout stays unchanged: offsets 0/1/9, size 11, alignment 1 |
| WXAG-08 | Raw Union uint8/uint64 | Offsets 0/0, size 8, alignment 8; alternative access remains unsafe |
| WXAG-09 | Transparent float32 versus ordinary one-field float32 record | Same 4/4 storage, distinct floating versus aggregate integer carrier |
| WXAG-10 | Empty/void-only ordinary struct, void and Transparent void | Ordinary extent 1; void and Transparent carrier extent 0; identities and source reference rules remain distinct |
| WXAG-11 | Packing 1/2/4/8/16; Alignment through 8192 and weaker-than-derived control | Exact allowed limits; underalignment and values outside the domain reject |
| WXAG-12 | Array of WXAG-01, three elements | Stride 8, extent 24 and preserved element alignment |
| WXAG-13 | Count-times-stride and round-up at nuint limits | Checked representability before allocation; no wrapped extent or sentinel |
| WXAG-14 | Forged field offset/size/alignment/stride record | Recompute and reject contradiction, not accept numeric plausibility |
| WXAG-15 | Recursive by-value field versus pointer recursion | By-value cycle rejects; pointer does not recursively expand payload |
| WXAG-16 | Actual packed uint32 read/write at offset 1 with neighboring fields/pages | Safe actual-alignment lowering over exactly authorized bytes, no neighbor overwrite, overread or atomic snapshot |

## 4. Source schemas

| Case | Fixture | Required outcome |
|---|---|---|
| WXAT-01 | Primary Blittable plus Layout(Sequential, Packing: 1, Alignment: 32) | Bind exact registered identities and ordinary named arguments |
| WXAT-02 | Same-spelled unregistered attribute/enum | No layout semantics or Blittable promise |
| WXAT-03 | Duplicate placement or contract attribute on a secondary type part | Reject under nonrepeatable, primary-only authorship |
| WXAT-04 | Arguments to Blittable; extra positional controls or unknown/repeated labels | Reject against the exact schema |
| WXAT-05 | Missing Layout Kind or unnamed enum value 4 | Reject; no guessed kind |
| WXAT-06 | Packing with wrong typed constant, zero, negative, 3 or 32 | Reject; no numeric conversion, sentinel or ambient default |
| WXAT-07 | Alignment 1 through 8192, plus zero/3/16384 | Accept only powers of two that preserve derived alignment |
| WXAT-08 | Auto/Transparent with supplied controls, even default-equivalent ones | Reject; Auto also cannot combine with Blittable/public fields |
| WXAT-09 | Explicitly frozen integer-backed enum | Underlying integer layout; no Layout annotation, no closing unnamed values |
| WXAT-10 | Sequential/public fields without Blittable; primitive core commitment | Public unfrozen field rejects; primitive commitment needs no user marker |
| WXAT-11 | Fixed outer type containing unsupported rooted/ref/own/optional/view/task field | Reject transitive representation; marker/size cannot repair it |
| WXAT-12 | Raw Union with unique owning child or claimed automatic alternative cleanup | Reject missing active-state/cleanup representation |

## 5. Foreign admission

| Case | Fixture | Required outcome |
|---|---|---|
| WXAD-01 | All formally mapped integer/code-unit/Boolean/binary32/64 primitives | Admit only with the explicit matching declaration/validity contract |
| WXAD-02 | Direct int128/uint128, float16/bfloat16/float128 and all decimals | Decline every unsupported foreign primitive |
| WXAD-03 | Transparent around one WXAD-02 primitive | Still decline; no wrapper laundering |
| WXAD-04 | Explicit two-uint64 C-compatible bit record for 128 bits | Ordinary aggregate indirect argument/result, not int128 or SIMD primitive |
| WXAD-05 | Explicit uint32/uint64 BID bit regions or decimal128 bit buffer | Permit only fully matched bit-record/buffer protocol, no implicit numeric conversion |
| WXAD-06 | Foreign Windows long offered for nint/int64 | Reject width mismatch; long can match int32 |
| WXAD-07 | Windows long double offered for float128/float64 | Reject float128 mapping; admit matching binary64 contract |
| WXAD-08 | One-byte bool offered for Win32 BOOL or rune producer without scalar validity | Reject the actual contract mismatch |
| WXAD-09 | Named Terrum void input versus void result | Named input is not C empty parameter-list equivalence; compatible void result has no payload |
| WXAD-10 | Plain native function pointer versus captured callable/member pointer | Code-address mapping only; no inferred closure/context/member ABI |
| WXAD-11 | Native C++ nontrivial record/member function versus explicit trivial C-style carrier | Decline unselected C++ convention; Terrum Cleanup does not redefine carrier classification |
| WXAD-12 | Known target number/optional Attr/matching Layout numbers without complete required codec/rules | Decline before independent use; no schema/Feature activation |

## 6. Call positions and supporting storage

| Case | Fixture | Required outcome |
|---|---|---|
| WXCA-01 | uint8, float64, int32, float32, uint64 | RCX/XMM1/R8/XMM3/entry stack +40 |
| WXCA-02 | Four floating arguments followed by a fifth | XMM0/1/2/3 then entry stack +40, no independent integer count |
| WXCA-03 | Hidden aggregate result and explicit receiver, then mixed parameters | Hidden RCX, receiver RDX; all later shared ordinals shift |
| WXCA-04 | Logical unit input among nonforeign parameters | No payload position, but retained logical arity/type for binding |
| WXCA-05 | Zero physical arguments and nested calls | At least 32-byte shadow space and proper CALL alignment |
| WXCA-06 | Narrow signed/unsigned input/result with arbitrary high register bits | Interpret only low payload bits, without invented extension guarantees |
| WXCA-07 | Ordinary aggregate sizes 1/2/4/8 versus 3/5/12/16/24 | First group integer carriers; second group indirect copies/hidden results |
| WXCA-08 | Scalar/Transparent float32 versus ordinary float32 record | XMM result versus RAX bit-carrier result |
| WXCA-09 | Nonforeign extended-scalar defaults | Exact table carriers; int128 is hidden-result, not XMM0 or RDX:RAX |
| WXCA-10 | Callee changes indirect by-value argument | Caller original is unchanged; no implicit ref adaptation |
| WXCA-11 | Incoming 16-byte ABI copy of Alignment=32 object; over-aligned result | Do not assume input object alignment; materialize when needed, and provide correct result storage |
| WXCA-12 | Hidden result returns a different pointer or incomplete value | Reject violated contract; no default publication |
| WXCA-13 | Fifth/sixth/seventh physical arguments | Entry stack offsets 40/48/56, each eight-byte slot |
| WXCA-14 | Later argument mutates a previously staged value versus captured borrow | Preserve earlier value snapshot; borrow retains original selected place identity |
| WXCA-15 | Earlier staged owner followed by argument failure | No call; retire the still-staged owner once, do not restore moved source |
| WXCA-16 | Extra hidden dictionary/context/error argument at an independent ordinary boundary | Decline a mismatched physical signature; no implicit convention extension |

## 7. Stable borrows and initialization

| Case | Fixture | Required outcome |
|---|---|---|
| WXBR-01 | Fixed initialized stack T with proper extent/alignment and call lifetime | One-pointer call-extent projection with caller backing retained |
| WXBR-02 | Readonly borrow versus writable borrow | Only writable path can change permitted original fields; no hidden by-value copy |
| WXBR-03 | Aliasing references with/without NoAlias | Ordinary aliasing allowed subject to actual protocol; declared separation checked |
| WXBR-04 | Underaligned uint32 field versus explicitly packed enclosing record | Field cannot satisfy aligned typed borrow; packed record keeps its own alignment contract |
| WXBR-05 | Callee retains/returns a call-only projected ref | Decline undeclared lifetime/representation, no hidden pin or retained descriptor |
| WXBR-06 | Movable/managed backing, including pin claim without admitted collector/root support | Decline missing runtime projection support |
| WXBR-07 | init ref<T> before publication | Reserved vacant destination is not readable T or a writable live alias |
| WXBR-08 | True-only normal output with partial construction on false | True publishes complete T; false retires private acquisitions and leaves caller vacant |
| WXBR-09 | Explicitly live failure output | Preserve that declared normal-failure state instead of imposing true-only vacancy |
| WXBR-10 | Owner/init aliases or code/backing lifetime ended during invocation | Decline invalid authority/lifetime; descriptor bits do not discharge obligations |
| WXBR-11 | Attempt to use a raw foreign unwind as ordinary output completion | Decline missing selected exception transport; no status/default result |
| WXBR-12 | Actual native borrow/output lowering and runtime publication | Prove original-place access, proper reservations, every-exit cleanup and no unsupported hidden roots/pins |

## 8. Owners, cleanup and origin

| Case | Fixture | Required outcome |
|---|---|---|
| WXOW-01 | Named unique by-value input without move; then with move | Reject implicit Copy; move vacates the source and stages one responsibility |
| WXOW-02 | Staged owner followed by pre-invocation failure | Caller retires staged responsibility exactly once; no callee or source restoration |
| WXOW-03 | Callee consumes owner and returns normal failure | Retire or transfer under its declared outcome; no leak/default owner |
| WXOW-04 | Partial unique aggregate offered as whole argument/result | Decline incomplete value, retaining only actual live-field responsibilities |
| WXOW-05 | Readonly receiver of four-/eight-byte owner | Pointer to original live owner, not an owned integer-carrier copy |
| WXOW-06 | Actual indirect parent retirement with later child cleanup | Distinct parent supporting storage preserves original child payloads and exact body-then-fields order |
| WXOW-07 | Privately constructed owner on a vacant normal output | Cleanup before return and no caller-readable owner |
| WXOW-08 | Complete owning return/init output through Middle from Leaf | Publish one responsibility, preserving Leaf type/cleanup/allocator origin |
| WXOW-09 | Release offered by wrong allocator domain or same-named middle cleanup | Decline incompatible pairing/origin, not cross-free by pointer equality |
| WXOW-10 | Owner retirement while a dependent call/loan/source remains | Decline invalid retirement until the actual dependency ends |
| WXOW-11 | Actual cross-image resource release and code lifetime | Prove responsible allocator/cleanup provider stays usable through every real release |
| WXOW-12 | Cleanup marker/trust flag without checked NoThrow/nonsuspending transitive truth | Decline missing complete contract; name/layout is not body proof |

## 9. Deployment and support

| Case | Fixture | Required outcome |
|---|---|---|
| WXDP-01 | Direct default foreign call with normal status/output failure | Non-unwinding exact ABI, no hidden exception adapter/state |
| WXDP-02 | Escaping C++/SEH/Terrum failure offered under runtimePolicy=1 alone | Decline missing special exception protocol |
| WXDP-03 | Actual calls modify nonvolatile registers/control fields or DF | Prove required restoration/standard call state; volatile status is not a source API |
| WXDP-04 | Optional ISA/register/unwind extension without CPU/OS/encoding support | Decline; baseline target and parent-page citation do not activate it |
| WXDP-05 | Wrong machine/PE magic, overflowing RVA/section/relocation or invalid used record | Reject actual platform contradiction before use |
| WXDP-06 | Selected provider closure, including finite cycle with missing valid load/init schedule | Complete requirements/schedule required; topology alone is neither proof nor default prohibition |
| WXDP-07 | Actual nonleaf/prolog/epilog, guarded stack and OS unwind lookup | Prove accurate full-point records/probing, not only successful return |
| WXDP-08 | Actual dynamic registration/retirement and failed deregistration | Keep still-used/registered code and record backing valid; no release after failure |
| WXDP-09 | Retained type/cleanup/helper edge absent from emitted IAT | Still belongs to required closure and code lifetime |
| WXDP-10 | Replacement changes layout/carrier/validity/outputs/cleanup/allocator/relied-on facts | Reject incompatible edge even with identical names/hashes/export keys |
| WXDP-11 | Private native package/version or renamed extension | No standardized bytes, body trust, managed/owner-mode union or fallback inferred |
| WXDP-12 | Host-selected Module entry versus Main/PE startup with absent body/import/launch adapter | Require exact admitted invocation and separately complete launch contract; no guessed signature/status/default |
| WXDP-13 | DWORD-aligned RUNTIME_FUNCTION row with byte-aligned code start/end, plus a misaligned row or UNWIND_INFO | Code RVAs need not be multiples of four; require twelve-byte rows and separately DWORD-aligned row/UNWIND_INFO storage |

## Evidence boundary

Required machine access, actual borrow/output lowering, generated retirement,
cross-image allocator/code lifetime, control restoration, full-point OS
unwind/probing and registration retirement require execution/target proofs.
Finite layout, bit-container, schema, carrier and supplied contract models can
assess their stated decisions without claiming those proofs.
The source schemas and profile add no wire IDs, missing feature codec,
implementation migration or automatic publication.
