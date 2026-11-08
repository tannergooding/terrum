# Windows x64 general optional value layout

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This recommendation is provisional direction,
not an accepted specification update, implemented support, native qualification
or a stable independently replaceable ABI.

## Decision and owners

Select a **one-byte presence tag followed by an aligned inline payload** for
general optional values whose complete rootless Copy payload contract is
already admitted. Retain the accepted null-as-absence semantics for optional
class handles, checked references and raw pointers. This does not select a
stored checked-reference layout or its exact null bit pattern; those still
require a separately selected complete descriptor profile. No additional
niche encoding is selected.

The missing decision is physical representation, not optional source syntax.
[Complete types](../specs/language/types-and-normalization.md#optional-normalization)
already equates `option<T>` with `T?`, makes optional construction idempotent,
and admits optional scalars and `option<void>`.
[Data layout and ABI](../specs/data-layout-and-abi.md#scope) defines null absence
for those three families without assigning their stored descriptor layouts. The
[Windows x64 layout](../specs/targets/windows-x64-data-layout.md) leaves general
optional/tag storage unassigned.

On adoption, add the rules below as **General optional values** in the Windows
x64 data-layout chapter. Narrow §1's unassigned-optional statement and §4's
optional-field exclusion only for this admitted tagged domain; give these
options a derived per-target Blittable commitment when their payload has it.
Retain §5's placement/required-alignment rules and §6's padding/access rules.
Do not change the grammar, complete-type normalization or accepted null-absence
contract. Other optional families and general tagged nominal unions remain
outside this amendment.

The [private persistence proposal](checked-reference-private-persistence.md)
owns constructor-3 optional type rows and explicitly leaves scalar optional
layout open. The [stored-reference](reference-stored-descriptor-layout.md),
[normal-backing](reference-normal-backing-support.md) and
[integrated GC ABI](integrated-gc-runtime-abi.md) proposals concern their own
descriptor representations, not this tagged value layout. This proposal
complements them without superseding their schemas or runtime agreements.

| Material alternative | Assessment |
|---|---|
| **Byte tag and aligned payload — selected** | One validity rule independent of payload bit patterns; costs padding and doubles a naturally aligned 16-byte payload's extent. |
| Machine-word tag | Simpler word accesses do not justify making unit and narrow options word-sized/aligned; wide payloads gain no extent reduction. |
| Additional payload niches | Can reduce some sizes, but requires exact reserved-pattern, nominal-validity and copy rules for each family. Full-domain integers and binary floating patterns supply no spare representation; no bool, rune, NaN or wrapper niche is inferred. |

## Applicability and normalization

Normalize the complete type before choosing representation: expand aliases,
substitute complete arguments and apply the accepted constructor/qualification
rules. `option<option<int32>>` has one tag and the layout of `int32?`;
written `int32??` remains invalid syntax. Distinct nested positions do not
collapse: `ptr<option<T>>` points to optional storage, whereas
`option<ptr<T>>` is an optional pointer. Nominal wrappers retain their identity,
validity and fields; equal layout, including Transparent layout, does not
grant a pointer/class/reference niche.

| Normalized family | Rule |
|---|---|
| Optional class handle or checked reference | Retain null-as-absence semantics. Storage requires a separately selected complete handle/descriptor profile; the accepted Windows x64 chapter and this proposal assign no stored checked-reference size, alignment or null bit pattern, rooting, retention or initialization-capability storage. |
| `option<ptr<T>>` | Preserve the selected raw-pointer niche: eight-byte, eight-aligned storage; all eight zero bytes mean absence. A present value is a valid non-null pointer with unchanged access, provenance and lifetime obligations. `ptr<void>` remains opaque, not a unit payload address. |
| Other complete rootless Copy value with already admitted finite layout, initialization, validity and value operations | Use the tagged rule below. This includes unit, scalars, fixed integer-backed enums and eligible inline aggregates, and composes with already admitted fixed arrays. An admitted plain code-address `fnptr` is not `ptr<T>` and receives no new null niche. |
| Missing or unsupported payload/profile | Refuse affected lowering; neither a known extent nor an optional type row establishes admission. No fallback to pointer-sized or zero-sized storage. |

The new tagged domain contains no owning/unique payload, managed/rooted handle,
stored-reference field, initialization capability, box/own/view/task descriptor
or unsupported tuple/union/callable representation, including transitively.
Their language types and any separately selected descriptor/runtime profiles are
not rejected or redesigned; their additional execution support is not selected
here. Pointer pointees are not recursively expanded into inline storage.
An open formal acquires no guessed layout; apply this rule only when its exact
payload and target evidence are available. Copy is an established contract,
not an inference from size, rootlessness or the absence of Cleanup.

## Exact tagged layout

Retain the payload's extent `s`, natural object-alignment component `a` and
separate required explicit alignment `r`. Do not assume `r <= a` or discard
`r` when composing Windows x64 §5's layout facts. Positive alignment facts are
powers of two; `roundUp(x, k)` is the least multiple of `k` not below `x`.

* Optional natural object-alignment component: `A = max(1, a)`.
* Required explicit alignment: `R = max(1, r)`.
* Combined placement/stride alignment: `L = max(A, R)`; retain `A` and `R`
  independently rather than replacing the natural alignment with `R`.
* Tag: exactly one eight-bit byte at offset **0**; **0 = absent**, **1 = present**.
* Payload offset: `P = roundUp(1, L)`.
* Complete extent and repeated-element stride: `S = roundUp(P + s, L)`.
* Bytes `[1, P)` and `[P + s, S)` are padding. Bytes `[P, P + s)` hold
  exactly the existing `T` representation when present, without changing its
  internal field offsets, byte order, validity or padding.

Calculate every addition/rounding and count-times-stride in the checked
Windows x64 `nuint` domain. Overflow is an unrepresentable layout, not a
sentinel; narrower codecs and representable resource exhaustion remain
separate gates. There is no new aggregate-size limit.

These are **candidate layout examples**, not compiler execution evidence:

| Payload facts `s / a / r` | Payload offset `P` | Optional size / stride `S` | Natural alignment `A` | Required alignment `R` |
|---|---:|---:|---:|---:|
| `void`: 0 / 0 / 1 | 1; no payload bytes | 1 | 1 | 1 |
| `bool` or `uint8`: 1 / 1 / 1 | 1 | 2 | 1 | 1 |
| `int32` or `float32`: 4 / 4 / 1 | 4 | 8 | 4 | 1 |
| `int64` or `float64`: 8 / 8 / 1 | 8 | 16 | 8 | 1 |
| `int128` or `float128`: 16 / 16 / 1 | 16 | 32 | 16 | 1 |
| Admitted inline aggregate: 24 / 8 / 1 | 8 | 32 | 8 | 1 |
| Admitted explicitly aligned aggregate: 32 / 32 / 32 | 32 | 64 | 32 | 32 |
| Mixed alignment-fact case: 32 / 8 / 32 | 32 | 64 | 8 | 32 |

For `s=32, a=8, r=32`, retain `A=8` and `R=32`, but use `L=32`,
`P=32` and `S=64`. From a 32-aligned array base, three optional elements
begin at offsets 0, 64 and 128; their payloads begin at 32, 96 and 160.
Every payload address and the stride satisfy `R`, without conflating it
with the natural alignment.

`option<void>` therefore has two values distinguished solely by its tag;
present unit has no payload load/store. A separately admitted zero-extent
nominal payload also uses the formula, but still needs its own complete
construction/validity and does not become `void`. The envelope's nonzero
extent does not expose a unit/zero-extent payload address or admit new payload
borrows.

There are no constructor-level Packing/Alignment arguments. A containing
aggregate uses `max(min(A, Packing), R)` for field placement, without repacking
the envelope internally. Standalone objects and ordinary arrays require base
alignment `L`; `P` and `S` preserve both `A` and `R`. Permitted packing can
reduce only the natural alignment component of actual accesses, never `R`;
lowering retains the actual base/access alignment evidence.

## Validity, padding and publication

For a **live initialized optional**, tag 0 is valid absence and requires no
live/valid `T` payload. Tag 1 is valid only with a completely initialized,
valid `T`. Tags **2 through 255 are invalid**, not truthy presence, vacancy or
another state. The tag is neither a source-visible bool field nor the holder's
initialization bookkeeping.

Padding and inactive payload bytes are unspecified and may remain
uninitialized. They are not value observations or a canonical zero-filled
encoding. Typed absence construction/copy needs only tag 0 and never reads or
default-constructs `T`. A checked raw-byte adapter validates the tag and, only
when present, the payload before publishing a typed optional; it accounts for
every transmitted byte when its protocol exposes padding. Unsafe providers
still owe validity. Invalid input has no promised implicit repair or trap.

Present construction publishes tag 1 only after the payload is complete.
Storage allocation, zero-filled bytes and tag 0 alone do not perform source
initialization: a vacant holder cannot be presence-tested as a live optional.
Failure before publication preserves the ordinary destination-state contract.
Presence tests/coalescing do not read an absent payload; payload extraction
requires established presence and retains the complete type and dependencies.
Lifting/copy/extraction preserve promised payload bits, including signed zero,
signaling NaNs and decimal cohorts/noncanonical encodings, without arithmetic.
No whole-optional atomicity follows from an atomic tag or small total extent.

## Integration and excluded boundaries

Binding, shared bound-tree traversal/rewriting, mandatory source verification,
layout queries and lowering must carry one normalized optional identity and
its discriminator/payload facts. Use the same layout in fields, temporaries
and admitted repeated storage, preserving actual alignment, vacancy,
publication, dependency and retained layout/provider obligations. This is a
generic constructor rule, not a scalar-name or motivating-sample recognizer.

The tag is a **native object byte**, not a TRRM opcode, type-row discriminator
or constant codec. This selects no wire allocation, private revision, Feature,
numeric TargetProfile or serialized host authorization. Existing complete
optional type transport remains separate from typed value/Body codecs and
source-independent executable admission. Under
[reader trust](../specs/representation/reader-trust.md), producers publish
truthful facts; consumers check safe structure, supported forms and binding,
not mandatory semantic re-proof. Preserve
[exact schema agreement](../specs/representation/format-versioning.md), including
retained target rules, through remapping and replacement.

Tagged options are not Transparent to `T` or direct foreign primitives.
This document admits no new argument/result/export/callback mapping,
independently replaceable Terrum ABI, native marshalling or failure bridge.
A crossing still needs its exact optional carrier classification and admission;
`option<void>` cannot be treated as a payload-free `void` signature position.
No tracing, root/pin/retain, ownership-transfer/drop, task settlement, view
backing or general nominal-sum representation is supplied. Provisional support
must identify this extension rather than claim the unchanged accepted profile.

## Acceptance gates and remaining decisions

Before claiming an implementation slice, establish its corresponding gates:

* **Types/layout:** aliases and nullable substitutions normalize once; qualify
  the correct node. Verify the table, nested options in fields, packed and
  explicitly aligned parents, empty/zero-extent payloads, arrays where already
  admitted, overflow and unsupported capacity. Check `P % R = 0` and
  `S % R = 0`, including the `a=8/r=32` case above: retain `A=8/R=32`,
  require a 32-aligned ordinary array base and test all three element/payload
  addresses plus packed-parent placement without weakening `R`.
  Equal-sized nominal carriers
  and pointer-to-option versus optional-pointer cases remain distinct.
* **Validity/flow:** exercise absent/present unit, zero-valued present scalars,
  bool/rune/nominal invalid payloads, all invalid tag bytes, arbitrary padding
  and inactive bytes. Reject vacant reads; preserve branch snapshots,
  short-circuit fallback effects and failed-output vacancy without reading `T`.
* **Lowering:** qualify bit-preserving copies, stores, extraction and layout
  observations through both JIT and emitted-image execution for the supported
  slice, using unrelated aggregate cases and actual alignment/extent checks.
  Emission alone is not runtime evidence.
* **Independent consumption/crossings, if claimed:** separately select and
  qualify complete typed value/Body codecs, provider binding/remapping and
  carrier agreements. Do not derive these from constructor-3 rows or this
  storage layout.

Normative adoption, the excluded runtime/ABI profiles and additional
interchange value codecs remain decisions outside this proposal. The
rootless Copy/unit tagged layout itself needs no further niche or tag choice.
Integration of [source admission](../../sources/Terrum.CodeAnalysis/Semantics/CheckedReferenceTypes.cs)
and [`ValueLayout`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs)
must be qualified independently of type-row transport. The
[native inventory](../implementation/native.md) records scalar options/general
generic optional normalization outside its profile. No new execution result,
implementation or inventory change is claimed here.
