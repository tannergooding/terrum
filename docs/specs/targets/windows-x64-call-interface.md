# Windows x64 call interface

## 1. Foreign primitive admission

A direct primitive foreign signature requires a formal matching platform
primitive, not merely storage with the same number of bits. The selected
foreign declaration must have the exact size, representation, parameter/
result classification and compatible validity/access contract.
Complete Terrum identities remain distinct; this table is not source
conversion or C typedef/name-based binding.

| Terrum form | Admitted equivalent Windows primitive |
|---|---|
| int8/uint8, int16/uint16, int32/uint32, int64/uint64 | Explicit matching signed/unsigned integer width; signed char, short, int/long and 64-bit integer forms as applicable |
| nint/nuint | Matching signed/unsigned 64-bit native integer, such as intptr_t/uintptr_t; not Windows long |
| bool | One-byte two-valued Boolean with representation 0/1; not Win32 BOOL |
| char8/char16/char32 | Explicit matching unsigned code-unit/integer carrier, including matching char16_t/wchar_t where applicable |
| rune | Matching unsigned 32-bit carrier with the additional Unicode-scalar validity obligation |
| float32/float64 | Windows float/double binary32/binary64, including Windows binary64 long double |
| Admitted ptr<T>/plain fnptr | Matching eight-byte native address, retaining pointee/access/code-lifetime contracts |
| void result | No returned payload; ordinary normal completion still publishes logical unit |
| int128/uint128, float16/bfloat16/float128, decimal32/decimal64/decimal128 | No formal primitive equivalent in this profile; decline direct foreign signature |

A named Terrum void input is not a C void parameter-list marker. A foreign
signature containing it needs an explicit logical adaptation and is not a
direct primitive match. The nonforeign carrier sequence can omit its payload
without erasing its logical ordinal.

A foreign record is an explicitly matched fixed, rootless representation,
not a way to infer primitive equivalence from any equal-sized declaration.
Its extent, alignment, field/opaque-bit-region correspondence, padding,
validity and ownership/access obligations must match. Unsupported primitive
bits can be carried in an explicitly declared ordinary record or buffer.
For example, a two-uint64 record can carry 128 integer bits using ordinary
aggregate rules, but it is not a direct int128 primitive or SIMD return.
IEEE decimal bits may use explicitly declared same-width integer regions;
no implicit decimal/integer conversion or retagging is introduced.

An integer-backed frozen enum and an admitted Transparent wrapper retain
nominal validity while using the final carrier's classification. Transparent
around an unsupported foreign primitive remains unsupported.
Ordinary one-field records instead use aggregate classification.
The native counterpart must meet the imported trivial/POD record convention;
native C++ constructors/destructors, references, bases, virtual dispatch or
member-function rules are not inferred from a Terrum type.
A Terrum constructor or Cleanup does not automatically make its explicit
C-compatible carrier a nontrivial C++ record.

## 2. Carrier classification and nonforeign defaults

The following classification applies after complete type, layout and
boundary-mode admission. For ordinary admitted aggregates, sizes 1, 2, 4
and 8 bytes use one same-width integer bit carrier. Every other nonzero size
uses an indirect argument copy and a hidden result buffer.
A one-float record therefore returns through RAX; a Transparent float32
wrapper returns through XMM0.

| Form | Argument carrier | Result carrier |
|---|---|---|
| Integer/code-unit/rune/bool up to eight bytes; native pointer/code address | Low payload bits of one integer carrier | Low payload bits of RAX |
| float32/float64, including eligible Transparent carriers | Low 32/64 bits of a floating carrier | Low 32/64 bits of XMM0 |
| Ordinary fixed aggregate of size 1/2/4/8 | Same-width integer bit carrier | Low same-width bits of RAX |
| Other nonzero ordinary fixed aggregate | Pointer to caller-owned by-value copy | Hidden first result pointer, returned in RAX |
| Logical unit or zero-extent Transparent carrier | No payload carrier | No payload carrier |

For profile-selected **nonforeign** scalar lowering, native-style defaults
also cover the otherwise unsupported foreign primitives:

| Scalar | Default argument | Default result |
|---|---|---|
| int128/uint128 | Indirect 16-byte value | Hidden result buffer; its address in RAX |
| float16/bfloat16 | Low 16 bits of a floating carrier | Low 16 bits of XMM0 |
| float128 | Indirect 16-byte value | All 128 bits of XMM0 |
| decimal32/decimal64 BID | Same-width integer bit carrier | Low 32/64 bits of RAX |
| decimal128 BID | Indirect 16-byte value | Hidden result buffer; its address in RAX |

These are bit-preserving defaults, not implicit binary32 widening,
numeric conversion, direct foreign admission or a newly standardized
Terrum-to-Terrum ABI. They require the actual joint-lowering/helper support
and exact rule agreement; another private convention is possible only where
both sides are regenerated together. A published independent crossing cannot
guess or silently change them. An explicit bit-record foreign boundary uses
that record's classification, not this scalar-default table.

No value is split across multiple argument registers. Upper unused register
and stack-slot bits are unspecified; they are not a sign/zero-extension
promise or padding initialization. Consumers interpret exactly the payload
width and its signedness/validity. Copy/identity returns preserve scalar
bits, including signaling NaNs and signed zero.

## 3. Physical argument positions and stack

Form the physical sequence after the logical call is mapped and checked:
the hidden result pointer, when required, comes first; an explicit instance
receiver follows; then ordinary parameters in declaration order.
Omit only positions with no payload, retaining their logical signature.
A borrowed receiver contributes one pointer, not an owned value.
No hidden generic dictionary, managed context, exception bit or other
convention-changing argument is inferred.

Use one shared physical ordinal for integer and floating carriers:

| Zero-based physical position | Integer/pointer | Floating |
|---|---|---|
| 0 | RCX | XMM0 |
| 1 | RDX | XMM1 |
| 2 | R8 | XMM2 |
| 3 | R9 | XMM3 |
| 4 and later | Eight-byte stack slot | Eight-byte stack slot |

Unused members of a register pair do not acquire another argument.
The fifth physical argument starts at `[entryRSP + 40]`, immediately after
the return address and 32-byte home area; later slots increase by eight.
At the caller before CALL it starts at `[RSP + 32]`.
Low-width payloads occupy the low-address bytes of a slot.

The caller always reserves at least 32 bytes of shadow space, including
zero-argument calls, and enough checked stack extent for later arguments.
The callee owns the home area during the call; a caller cannot keep a staged
value there across a nested call. RSP is 16-byte aligned immediately before
CALL; pushing the return address makes entryRSP congruent to eight modulo
16. Frames obey the imported prolog/epilog rules. There is no Windows red
zone below unallocated RSP.

Source expressions still evaluate once in source order, not register order
or the order used to fill stack slots. Labels can change the binding, not
evaluation order. Preserve earlier value snapshots, responsible owners and
captured reference identities while later arguments or nested calls execute.
Pre-invocation failure prevents the call and retires still-caller-owned
staged values under the ordinary scope order.

This physical sequence describes the ordinary C-style call mapping. A
`NativeCallable` member or interface slot follows the effective convention's
target ABI for receiver placement, hidden result storage, explicit arguments,
and any required inheritance/vptr adjustment. Do not assume the C-style
explicit-receiver order above or impose a universal `this`/result/arguments
sequence. A consumer that cannot implement the target ABI's exact mapping
rejects the affected call.

For Windows x64 `NativeCallable` instance methods using `Unmanaged`, `Cdecl`
or `Stdcall`, the member ABI places the implicit `this` receiver first, then
hidden result storage when required, then explicit parameters. Thus `this`
uses RCX and an indirect result pointer uses RDX; subsequent arguments use the
ordinary shared physical-ordinal classification. This differs from the
C-style explicit-receiver sequence above. `Vectorcall` uses its distinct
MSVC mapping; its receiver, result and argument placement must follow that
mapping or the affected call is rejected. Interface-subobject and multiple-
inheritance adjustments likewise follow the selected target ABI.

For example, `(uint8, float64, int32, float32, uint64)` uses
RCX/XMM1/R8/XMM3/stack. An indirect aggregate result for
`(int32, float64, int32, float32)` uses hidden RCX, then
RDX/XMM2/R9/stack. Counting floating registers separately is wrong.
the receiver in this example is an explicit C-style parameter, not a hidden
member receiver.

## 4. By-value copies and result storage

An indirect by-value input points to an independent exact-extent copy.
It is not a loan of the caller's original place, source implicit-reference
adaptation or permission for the callee to retain that temporary.
The standard incoming copy has at least 16-byte ABI alignment.
Do not infer object alignment above 16 from the incoming pointer.
When the callee needs an addressable over-aligned T, it materializes correctly
aligned local value storage before using that stronger requirement.
A producer may provide stronger alignment, but a standard caller is not
required to do so merely because T has explicit Alignment=32.

This distinction preserves both Microsoft parameter rules and Terrum's
object-alignment promise. ABI backing is not automatically an ordinary
borrowable T place. Materialization/copy elision is possible only when its
actual alignment, value/address observations, nonaliasing and obligations
survive. It does not authorize hidden heap allocation, rooting or ownership
duplication.

An indirect result receives separate caller-prepared storage of the exact
result extent, with its complete object alignment. For an ordinary C-style
call, the hidden pointer is physical position zero, and the same pointer is
returned in RAX. A `NativeCallable` instance method using `Unmanaged`,
`Cdecl` or `Stdcall` places `this` first and the hidden result pointer second,
as specified above; other conventions follow their target mapping. The hidden
pointer is not an ordinary logical parameter or a result reference.
The caller cannot read T until its complete normal result is published.
Result and argument supporting storage remain independent unless a checked
lowering proves the selected ABI, alias, identity and publication contracts
are unchanged.

Result buffer alignment is the result's object requirement, not a universal
extra 16-byte promise for every three-byte record. Conversely, a 32-byte
aligned result needs suitable result storage; ordinary shadow-space alignment
does not discharge that requirement.
Required supporting extent/alignment and resource checks precede access.

Scalar/wrapper invariants and field bits survive typed by-value transfer.
Padding is governed by the [data-layout contract](windows-x64-data-layout.md#6-access-padding-and-observations);
an aggregate carrier does not initialize it or make a raw foreign call safe.
The ABI imposes no proposal-specific maximum aggregate storage size.

## 5. Stable input borrows and initialization

“Borrow” here is the non-owning reference relationship in the shared
[definitions](../language.md#definitions-and-notation), projected only for
the admitted stable call extent below. It is not an ABI-indirect by-value
copy, owner transfer or automatic lifetime extension.

This profile admits call-extent ref<T>/write ref<T> inputs and initialization
destinations only when their complete T has admitted fixed rootless storage,
positive extent, actual required alignment and stable backing for the call.
The crossing projection is one native pointer.
The caller retains the exact storage/value generation, backing, authority
and retirement exclusion through the entire invocation and its cleanup.
Readonly restricts this path, not other aliases; NoAlias is checked only
where separately declared.

The callee can neither retain the projected borrow nor return/capture it
through an undeclared relationship. This bounded projection assigns no
stored checked-reference descriptor or general result-reference ABI.
Managed pin/root/barrier support remains a separate runtime requirement;
ordinary ref formation cannot acquire a pin or turn movable storage into
a stable pointer. Explicit ptr<T> inputs/results retain their separately
declared lifetime/provenance obligations without becoming checked loans.

A writable borrow permits only the declared writes through that path.
The original caller place, not a hidden by-value copy, is affected.
An underaligned member cannot satisfy an aligned typed-pointer contract;
passing its explicitly packed enclosing record is a different operation.
Overlapping arguments remain possible under ordinary reference rules unless
their actual owner/guard/NoAlias protocol prohibits them.

An `ref<init T>` parameter points to reserved vacant caller storage.
It is not a writable alias to a live owner or readable default.
The complete declaration publishes initialized/vacant states for each
normal result, including an explicitly live failure output when admitted.
The familiar true-only form publishes on true and keeps false vacant.
Its status has its own exact primitive/nominal contract; Win32 BOOL is not
silently rewritten to the profile's one-byte bool.

Constructed-but-unpublished values and partial acquisitions remain privately
responsible. A normal outcome requiring vacancy retires them before return;
one requiring a complete value publishes only after its declared checks and
completion obligations. Bytes written and a returned buffer pointer do not
by themselves establish value initialization or ownership.
Under `NativeExceptionUnwind`, an admitted native exception can unwind inward
through the target ABI and is normalized before source catch matching. It is
not an ordinary output state or a status result; an unhandled event fails
fast, and no unwinding exception fabricates a normal result or output
publication.

## 6. Complete owners and retirement entries

A fixed rootless unique value can cross only with its complete transfer,
validity, cleanup/allocator-origin and retained dependency contract.
Physical classification follows its admitted aggregate/Transparent carrier,
not a Copy inference. A named by-value input uses explicit move; a fresh
result transfers its existing responsibility. Partial owning-field state,
vacant values, task responsibility and unknown root protocols cannot cross
as complete T.

Staging transfers the responsible holder, not two independent owners.
At actual invocation the callee becomes responsible for consumed inputs;
every normal outcome must transfer or retire them under the declared contract.
If the invocation never occurs, the caller retires its still-staged owners.
Normal failure does not restore a moved caller slot or invent a fresh owner.
A published owning result/init output transfers its one responsibility only
at the declared publication point.

An admitted readonly unique instance receiver is one pointer to the live
original owner, including owners whose payload is only four or eight bytes.
It is not an owned by-value call. The physical retirement entry instead
consumes a value using the aggregate ABI. This is the generated/runtime
retirement entry, not an ordinary user-callable alias to the authored
readonly Cleanup method.

The retirement driver preserves the complete source contract during normal
retirement: authored Cleanup body once, then live owning fields in reverse
declaration order. Cleanup is nonsuspending but may raise an ordinary
exception; if it does, propagation begins and the remaining generated
owner/field cleanup does not resume during that unwind.
An indirect parent retirement argument uses distinct supporting storage;
writes to that consumed argument cannot corrupt the original child payloads
required for later child retirement. No physical duplicate bits create a
second logical owner or authorize reading a vacant field.
Failed unpublished construction instead uses reverse acquisition order and
does not call the incomplete parent's Cleanup.

Native allocation transfer retains the exact allocating/releasing domain,
selected type and Cleanup provider, outstanding loans and affinity.
A raw pointer or a middle image that forwards an owner does not acquire
allocator ownership. No hidden retain, code unload or cross-CRT free repairs
a missing contract. The foreign party must supply the corresponding
explicit take/release protocol; its ordinary C record declaration or C++
destructor name is not evidence of that protocol.
