# Windows x64 data layout

## 1. Scalar storage and validity

Storage uses eight-bit bytes and little-endian order. Bit zero is the least
significant bit of a scalar's complete unsigned bit container. Tables give
payload extent and natural object alignment in bytes, not call slots,
serialized constants, allocator headers or descriptor bookkeeping.

| Type | Size | Alignment | Representation and value domain |
|---|---:|---:|---|
| void | 0 | 1 | One inhabited unit value; no payload data address |
| bool | 1 | 1 | Exactly byte 0 or 1 |
| char8 | 1 | 1 | Unsigned eight-bit raw code unit |
| char16 | 2 | 2 | Unsigned sixteen-bit raw code unit, including surrogates |
| char32 | 4 | 4 | Unsigned thirty-two-bit raw code unit |
| rune | 4 | 4 | Unsigned Unicode scalar, 0 through 0x10FFFF excluding 0xD800 through 0xDFFF |
| int8 | 1 | 1 | Eight-bit two's complement |
| int16 | 2 | 2 | Sixteen-bit two's complement |
| int32 | 4 | 4 | Thirty-two-bit two's complement |
| int64 | 8 | 8 | Sixty-four-bit two's complement |
| int128 | 16 | 16 | One hundred twenty-eight-bit two's complement |
| uint8 | 1 | 1 | Unsigned eight-bit integer |
| uint16 | 2 | 2 | Unsigned sixteen-bit integer |
| uint32 | 4 | 4 | Unsigned thirty-two-bit integer |
| uint64 | 8 | 8 | Unsigned sixty-four-bit integer |
| uint128 | 16 | 16 | Unsigned one hundred twenty-eight-bit integer |
| nint | 8 | 8 | Signed sixty-four-bit native integer, retaining nint identity |
| nuint | 8 | 8 | Unsigned sixty-four-bit native integer, retaining nuint identity |
| float16 | 2 | 2 | IEEE binary16 |
| bfloat16 | 2 | 2 | One sign, eight exponent and seven trailing fraction bits |
| float32 | 4 | 4 | IEEE binary32 |
| float64 | 8 | 8 | IEEE binary64 |
| float128 | 16 | 16 | IEEE binary128 |
| decimal32 | 4 | 4 | IEEE decimal32 BID below |
| decimal64 | 8 | 8 | IEEE decimal64 BID below |
| decimal128 | 16 | 16 | IEEE decimal128 BID below |

Every core scalar has its core Blittable commitment. This table neither
admits every scalar at a [foreign primitive boundary](windows-x64-call-interface.md#1-foreign-primitive-admission)
nor changes the mandatory source operator catalog. Windows long is int32;
Windows long double is binary64, not float128. Win32 BOOL is a thirty-two-bit
integer contract, not this bool representation.

Native data pointers and admitted plain code-address function pointers occupy
eight bytes with alignment eight. Their bits do not prove provenance,
non-null/liveness, pointee extent, access, ownership or code lifetime.
ptr<void> can be an explicitly opaque address, not an exposable void payload.
Stored ref/own/box/class/view/task representations and optional/tag layouts are
not assigned by this pointer rule. A call-only reference projection likewise
does not assign a stored ref<T> layout.

Boolean bytes outside 0/1 and invalid rune values cannot be used as those
typed values. Code units do not undergo Unicode validation as runes.
Nominal enums/wrappers retain their own complete validity contracts.
An unsafe raw input still owes validity; it is not silently clamped,
normalized or interpreted as true. A checked byte adapter validates before
publishing a typed value.

## 2. Binary floating representation

The fields, from high to low, are sign, biased exponent and trailing fraction:

| Format | Exponent bits | Fraction bits | Bias |
|---|---:|---:|---:|
| float16 | 5 | 10 | 15 |
| bfloat16 | 8 | 7 | 127 |
| float32 | 8 | 23 | 127 |
| float64 | 11 | 52 | 1023 |
| float128 | 15 | 112 | 16383 |

For exponent zero, fraction zero is signed zero; other fractions are
subnormal with no implicit leading one and exponent `1 - bias`.
For a nonzero, non-all-ones exponent, the significand has an implicit
leading one and exponent `encodedExponent - bias`. An all-ones exponent
with zero fraction is signed infinity; with nonzero fraction it is NaN.
The highest fraction bit distinguishes quiet (one) from signaling (zero)
NaNs. Every complete binary bit pattern denotes one of these classes.

Typed copies, unary plus and explicitly bit-preserving operations preserve
all field bits, including signed zero, signaling state and NaN payload.
Arithmetic instead follows the [source floating rules](../language/primitive-operators.md#floating-arithmetic-and-comparisons),
including strict target-format rounding and their permitted arithmetic NaN
variation. Loading/copying a signaling pattern is not an implicit arithmetic
operation or permission to trap.

## 3. IEEE decimal BID

The selected encoding is binary integer decimal, not DPD or CLR decimal.
For a finite value, the decoded sign, coefficient `C` and quantum exponent
`q` denote `(-1)^sign * C * 10^q`. Equal-valued coefficients/exponents remain
distinct observable cohorts. The following constants determine the encoding:

| Format | Bits N | Precision p | Exponent bits E | Short coefficient bits K | Bias | q minimum | q maximum | NaN payload bits |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| decimal32 | 32 | 7 | 8 | 23 | 101 | -101 | 90 | 20 |
| decimal64 | 64 | 16 | 10 | 53 | 398 | -398 | 369 | 50 |
| decimal128 | 128 | 34 | 14 | 113 | 6176 | -6176 | 6111 | 110 |

Decode the complete N-bit container in this order:

1. Bit `N - 1` is the sign. Bits `N - 2` through `N - 6` equal to
   binary `11110` denote infinity; `11111` denote NaN. These classes take
   precedence over finite steering.
2. Otherwise, if bits `N - 2` and `N - 3` are not both one, extract the
   E-bit biased exponent immediately above the low K coefficient bits.
   Those low K bits are the raw coefficient.
3. Otherwise, extract the E-bit biased exponent immediately above the
   low `K - 2` bits, and form the raw coefficient as
   `2^K + low(K - 2 bits)`. These are the finite `11` steering encodings.
4. Subtract the bias to obtain q. If the raw coefficient exceeds
   `10^p - 1`, its numeric coefficient is zero with the decoded sign and
   q. It is a noncanonical finite encoding, not a new invalid scalar.

Canonical finite production encodes a coefficient at most `10^p - 1`
in the short form when it is below `2^K`, otherwise in the steering form,
using `q + bias`. The finite exponent range is exactly the table range.
All finite decimal128 steering encodings are noncanonical because
`2^113 > 10^34 - 1`.

For infinity, trailing bits do not change the signed infinity value;
canonical production clears them. For NaN, bit `N - 7` is the signaling
bit: one means signaling, unlike the binary format's quiet bit.
The low payload-bit count in the table carries a binary integer payload.
Bits between that payload and the signaling bit are reserved for canonical
production and are zero in a canonical NaN. A payload above
`10^(p - 1) - 1` has numeric payload zero; reserved/trailing noncanonical
bits do not turn a NaN into an invalid scalar.

Ordinary copies and explicit bit-preserving observations retain the entire
original container, including noncanonical encodings. Numeric decoding of a
noncanonical coefficient/payload does not rewrite that container.
Arithmetic produces its specified value/cohort and may canonicalize under
IEEE rules; its NaN payload/sign policy remains the source policy.
Typed constant emission retains promised scalar observations, not arbitrary
uninitialized padding or a different interchange codec.

For example, positive decimal32 zero at q=0 is `0x32800000`;
decimal32 `(C=550, q=-2)` is `0x31800226`. Merely choosing an equal-valued
q=-1 cohort is not a bit-preserving copy. decimal32 `0x60000000` decodes
coefficient 8388608 at q=-101, while `0x601FFFFF` is a noncanonical zero at
that same q. Classification is independent of host floating arithmetic.

IEEE 754-2019 sections 3.5.2 and 6.2 supply the decimal interchange/class
terminology. The field extraction and canonicality rules above are explicit
Terrum target requirements. The pinned
[BID reference implementations](../references.md#windows-x64-platform-and-decimal-evidence)
are independent design evidence, not normative .NET API contracts.

## 4. Registered source layout schemas

The selected Core registration supplies the exact LayoutKind enum and
Layout/Blittable identities. A same-named user attribute/enum cannot select
these semantics. Library definitions must match this schema; registration
does not assign a Core binary identity or physical metadata codec.

| Schema | Arguments | Placement and multiplicity |
|---|---|---|
| Blittable | None | Nonrepeatable on the primary ordinary inline struct/unique struct or integer-backed enum contract |
| Layout | Required `Kind: LayoutKind`; optional named `Packing: uint32`, `Alignment: uint32` | Nonrepeatable on the primary ordinary inline struct/unique struct contract |

LayoutKind has int32 underlying values Auto=0, Sequential=1, Union=2 and
Transparent=3. Unnamed values do not select another layout rule.
Kind can be the first positional argument or its ordinary named argument.
Packing/Alignment are named controls, not extra positional overloads.
Missing/repeated/unknown labels, wrong constant types, unknown enum values,
arguments to Blittable and secondary-part contract authorship reject.
Labels use the [ordinary attribute argument grammar](../grammar-spec.md#31-source-file-structure-and-declarations),
with the same `name:` spelling as calls, not assignment expressions.

```terrum
[Blittable]
[Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
public struct Packet
{
    public uint8 Tag;
    public uint32 Value;
}
```

Omission of Layout selects Auto. Explicit Auto is permitted without controls,
but cannot combine with Blittable or externally visible public instance
fields. A public field on an internal struct remains internal and does not
create an independently replaceable layout boundary.
Sequential does not itself grant Blittable. Union requires Blittable.
Transparent has exactly one instance field and accepts neither custom
control, even an otherwise equivalent value. An enum uses its explicit
underlying integer storage and has no Layout annotation.
Static fields are excluded from instance placement.
Classes, interfaces, tagged source unions, aliases, fields, parameters and
callables do not acquire these type-layout contracts from these schemas.

Omitted Packing is 16. Supplied Packing is exactly 1, 2, 4, 8 or 16.
Omitted Alignment uses derived alignment; a supplied value is a power of
two from 1 through 8192 and must not weaken the required derived alignment.
Zero is not a sentinel for omission. Auto/Transparent reject either control.
The general compiler-time constant rules apply; there is no layout-specific
evaluator or ambient module-wide packing mode.

Fixed-layout eligibility is transitive over every instance field's complete
representation. Core scalars, covered native pointers, explicitly fixed
integer-backed enums and eligible fixed inline aggregates can participate.
Movable/rooted handles, stored checked references, own<T> descriptors,
optional/tag/tuple layouts, views/tasks and unknown representations are not
made Blittable by a marker. Recursive by-value storage cycles reject;
pointer recursion does not expand pointee storage.
Copy, authority, nominal validity and retirement remain independent.

This profile's raw Union alternatives must be rootless Copy fields with no
automatic owning-child retirement. It has no active-member tracker; typed
alternative access remains unsafe. A complete unique outer wrapper can have
its own checked Cleanup over non-owning bits, but overlapping unique children
need a separately specified active-state/cleanup representation.

## 5. Aggregate placement, empty storage and stride

For each field's completed layout retain size, object alignment and
**required explicit alignment**. The latter is one for a primitive, and
the maximum of an aggregate's supplied Alignment and all transitive explicit
subobject requirements. It survives nesting and packing.

For Sequential/Union with packing P, each nonzero field's placement
alignment is `max(min(fieldAlignment, P), fieldRequiredExplicitAlignment)`.
The derived aggregate alignment is the maximum of these alignments and one.
A supplied aggregate Alignment must be at least that maximum; it becomes
the aggregate alignment. Explicit nested alignment cannot be packed away.
Packing does not recompute a nested field's internal layout or extent.

Sequential visits instance fields in declaration order. Start at offset zero;
round the current end up to each field's placement alignment, assign that
offset, and add its complete extent. Union assigns offset zero to every
alternative and uses the largest field extent. Round the resulting end up
to the aggregate alignment to obtain size and array stride.

An ordinary empty Sequential/Union aggregate, including one with only logical
void fields, has minimum storage extent one before final alignment rounding.
That storage is layout padding, not an initialized default or hidden unit
payload. A void field consumes no bytes and has its logical offset at the
current end without introducing padding. Transparent instead inherits its
one field's exact size/alignment/classification, even when zero.
Zero-sized nominal identity is not void and does not inherit void's source
reference restrictions; a zero-extent native borrow requires separate
address/anchor support and is not admitted by this profile.

Transparent retains the carrier's explicit alignment requirements and field
offset zero. It neither inserts a minimum byte nor converts an ordinary
aggregate into a scalar. Its carrier must meet the actual fixed/validity
requirements for a frozen boundary.

All offsets, additions, alignment rounding, nested extents and array
count-times-stride calculations are checked in the nuint domain before
allocation or access. Unrepresentable layout is not truncated to metadata
u32, assigned a sentinel or repaired by changing field order.
Representable but unsupported capacity reports exhaustion separately.
Current physical Layout/FieldDef ranges can impose a narrower codec gate.

| Fields and controls | Offsets | Size / stride | Alignment |
|---|---|---:|---:|
| uint8, uint32; defaults | 0, 4 | 8 | 4 |
| uint8, uint32; Packing=1 | 0, 1 | 5 | 1 |
| uint8, uint32; Alignment=32 | 0, 4 | 32 | 32 |
| uint8, uint32; Packing=1, Alignment=32 | 0, 1 | 32 | 32 |
| uint8, default first-row struct, uint16; Packing=1 | 0, 1, 9 | 11 | 1 |
| uint8, Alignment=32 first-row struct, uint16; Packing=1 | 0, 32, 64 | 96 | 32 |

Array element alignment is the element's completed object alignment.
Its base and stride preserve each element's required alignment; no managed
array, variable-length field or new fixed-array source syntax is assigned.
Object storage, ABI temporary alignment and the alignment of the address
actually used are distinct.

## 6. Access, padding and observations

In ordinary coherent memory, naturally aligned whole primitive accesses up
to eight bytes satisfy the core non-tearing guarantee. A checked trivial
primitive wrapper can retain that carrier guarantee. Small multi-field
records, wide primitives and compound updates do not inherit it by size.
This profile adds no sixteen-byte atomic, unaligned atomic, device-memory
or stronger cross-language synchronization guarantee.

Packed field reads/writes are safe under ordinary authority and validity
when the lowering uses their actual alignment and exact authorized extent.
It may use supported unaligned instructions or smaller accesses, without
touching adjacent fields, manufacturing an aligned reference or promising
an atomic snapshot. Across pages every byte must be valid accessible memory;
no extra faulting read, cache-line assumption or page-overread is permitted.
This is ordinary coherent storage, not an implicit MMIO protocol.

A reference representation or consumer that requires stronger alignment
cannot receive an underaligned field merely because its type is int32.
Retain the actual alignment evidence or decline that use. A projected native
typed borrow requires its promised object alignment; an explicit packed
record pointer is distinct from an aligned pointer to one of its members.

Field initialization does not initialize padding. Typed copies preserve all
promised field/representation observations, not unspecified padding.
Raw foreign transfer and object-representation access remain unsafe and owe
their complete byte, validity and lifetime contracts. A safe adapter must
account for every transmitted byte, including padding when its protocol
exposes it. It cannot publish invalid Boolean/rune/nominal values.

SizeOf observes complete object extent including layout padding;
AlignmentOf observes completed object alignment. Neither reports call-slot
size, heap/header cost, foreign equivalence or a stored descriptor that this
profile did not assign. Retained observations keep their
[fixed or coupled dependencies](../language/registered-queries.md#value-layout-queries).
