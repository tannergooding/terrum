# Stable-backing Span value profile

> [!IMPORTANT]
> **Status: unapproved candidate; provisionally actionable under the user's
> implementation instruction.** This does not adopt a public API, stabilize an
> ABI, or claim implementation support.

## Problem and boundary

The selected `Span<T>` family already distinguishes descriptor rebinding
(`mut Span<T>`), element-slot access (`write Span<T>`) and inner element
qualification (`Span<write T>`). WRITE-06/07 are accepted type rules and need
no new registration syntax or generic constraint. The implementation has only
readonly `Span<Token>` support; `MutableInt32BufferView` is a distinct lexical
intrinsic that cannot be copied, passed, stored or returned.

The missing decision for executable, stable-backed Span values is a concrete
Core binding and target value/call profile. Generic WRITE-06 type checking is
an independent implementation obligation; it must not treat the buffer
intrinsic as a general Span or require this value profile for type-only checks.

## Recommended candidate

Select one synchronous, nonforeign Windows x64 profile for normal-backed
Span values:

| Area | Candidate |
|---|---|
| Identity | Reuse the built-in Span family (`TypeSpec` family `0x8A`) and its existing qualifier placement. Bind exact Core declaration/provider identities; do not resolve by name or layout. Retain the accepted explicit `IContiguousSequence<T>.AsSpan()` projection and its witness. |
| Formation | Select only `Span<T>.FromReference(ref<T>)` and `Span<T>.FromWritableReference(write ref<T>)` as checked one-element formations. The first is readonly; the second preserves immediate write authority. These are unapproved Core members, not accepted source APIs. Both are synchronous and nonallocating; their result depends on the referent, not the source parameter slot, and neither forwards backing. |
| Descriptor | 24 bytes, 8-byte alignment: element address, `nuint` count, and the existing normal-backing support handle (zero only for fully externally bounded stable storage). |
| Backing | Reuse the selected normal-backing hold/retirement contract. One support word represents one actual element-storage allocation: formation/copy retains it, move transfers it, and retirement releases it once. Reject unsupported multiple-allocation/GC/guard obligations. No owner acquisition, pin, implicit lifetime extension, per-view allocation, or exclusivity guarantee. |
| Calls | Within the exact profile, pass a 24-byte input as an indirect by-value copy and return it through an exact caller result buffer. No foreign ABI or independent precompiled crossing. |
| Selection | Use the exact candidate agreement key `terrum.experimental.windows-x64.normal-backing-span`. Do not change the existing normal-backing or TokenSpan profiles, and do not fall back when this profile is absent. |

This reuses TypeSpec family `0x8A` and the existing generic intrinsic-Write
constraint encoding (`Constraint` kind `2`, property code `2`). It adds no type
code, constraint code, Feature, opcode, Body record, private revision or
compatibility alias.
The agreement is bounded to initialized normal or externally bounded stable
elements. GC relocation, `Span<init T>` execution, optional/existential layouts,
arbitrary range constructors, indexers/slicing, and independent module
crossings remain unsupported by this candidate and require their own gates.

## Alternatives

| Alternative | Disposition |
|---|---|
| 16-byte address/count only | Sufficient only for externally bounded storage; it cannot preserve normal-backing support after the source handle ends. |
| Universal normal/GC/pin/guard/erased view ABI | A broader redesign requiring provider dispatch, relocation, acquisition/failure/retirement and deployment contracts. Not required for this bounded profile. |
| Reuse `MutableInt32BufferView` by name or layout | Reject. It is a lexical exclusive buffer loan, not Span identity or a general view value. |

## Required invariants and conformance

The descriptor's support identifies the allocation containing element slots,
not the source descriptor variable or a stored owner handle. Obtain it only
from checked source provenance; never infer it from an address or subtract a
header. A normal support hold does not extend an explicit owner, guard or
resource endpoint. Empty/end views preserve their actual dependencies and
support; count zero permits no element access or nonempty address arithmetic.
A single support word does not collapse multiple source dependencies or support
obligations; reject any view whose complete relationship is not represented by
this profile. Zero support is valid only for a separately verified,
externally-bounded stable source.

Outer write access applies to element places only. It does not change `T`, grant
writes through readonly class handles, replace the descriptor slot, imply
Copy of `T`, or imply exclusivity. Readonly projections preserve the complete
element type and dependencies. Ordinary ownership, initialization, lifetime,
backing, caller-access and cleanup checks remain independent.

| Case | Required result |
|---|---|
| `write Span<C>` versus `Span<write C>` | The first has writable element slots; the second remains readonly at the slots. |
| `mut Span<C>` | Permits descriptor rebinding only; does not satisfy `where T : write`. |
| Aliases and generic defaults | Preserve the complete outer/inner qualifications; never upgrade readonly types. |
| Type-only generic actual | Check the accepted Write property and ordinary formal domains; no runtime Span value is implied. |
| Writable type, readonly value | Type satisfaction does not grant caller authority or permit an inferred upgrade. |
| Backing/owner/guard retirement | Reject use after the required support or source lifetime ends, including in `unsafe`. |
| Empty/end and bounds | Preserve dependencies; reject malformed support, overflow, invalid alignment, or access at `index == count`. |
| Unsupported GC/init/foreign/persistence route | Reject before affected use; no fallback to TokenSpan or a lexical buffer view. |

## Proposed spec owners

If adopted, add only the following profile-specific clauses; keep the general
Span family and WRITE-06/07 contracts unchanged:

- **Libraries, Views:** exact Core binding for the built-in Span identity and
  the two formation members; no general API catalog.
- **Windows layout and call interface:** the named agreement's 24/8 descriptor,
  indirect input and exact result-buffer rules; no foreign or independent
  precompiled ABI.
- **Dependencies and lifetimes:** support acquisition, copy/move/retirement,
  empty-view dependencies and the explicit owner/guard limits above.
- **Implementation inventory:** track this as a bounded profile, not full Span
  API, GC, init, persistence or runtime-family completion.

The [Core API inventory](core-library-reference.md#general-span-and-inline-storage)
and [normal-backing proposal](reference-normal-backing-support.md) remain
unapproved owners; this proposal composes with their cited contracts but does
not approve their broader surfaces.
