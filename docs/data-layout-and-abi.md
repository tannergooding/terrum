# Data layout and ABI contracts

> **Accepted core contracts; remaining decisions are listed separately.** The
> memory guarantees, opaque-layout direction and boundary-preservation rules
> below are accepted. This is not a completed target ABI specification or a
> universal Terrum managed ABI. Current support remains defined by
> [implementation](implementation.md) and the [native profile](native.md);
> concurrency semantics are defined by the [memory model](memory-model.md).

## Status and review boundary

| Status | Covered here |
|---|---|
| Accepted design | Primitive and eligible-wrapper access guarantees; no aggregate atomicity inferred from size; opaque layout by default; no foreign-interop promise inferred from an ordinary struct's current shape; explicit checked, target-profile representation/call contracts; separation of representation, calling convention, atomic-access eligibility, validity, ownership and sharing; preservation of published boundaries |
| Implemented bounded profile | The existing Windows x64 compact-value and module ABIs. Their exact current contracts remain authoritative; they are not superseded by an unspecified future profile |
| Needs policy or declaration review | Wrapper eligibility/declaration details, any checked overlapping-union subset, externally promised encodings not determined by a selected profile, and a future public managed boundary; see [remaining decisions](#remaining-decisions-requiring-review) |
| Specification and implementation work | Recording chosen target rules, checking layouts and metadata, validating compatible replacement, and testing lowering against accepted guarantees; see [profile work](#profile-specification-and-implementation-work) |

Unimplemented does not mean unapproved. Completing a table from an already
selected target contract does not reopen the language policy; choosing a new
observable representation or independently replaceable public boundary does.
The cross-document [pending-decision index](language-library-review.md)
tracks those review items without making the accepted core provisional.

Explicit layout or carrier opt-ins constrain compatible replacement under the
same contract: adding a field to a frozen layout or changing a promised
carrier is breaking unless a separately versioned contract permits it. Such
promises are target/profile-specific, not universal byte encodings. Any future
checked source enforcement needs an explicit compatibility migration; it does
not claim that the current compiler rejects ordinary structs or retroactively
revoke existing source/artifact contracts.

## Contract layers

| Layer | Defines | Does not fix |
|---|---|---|
| Language values and memory accesses | Primitive value semantics, required atomicity, initialization, access authority and lifetime | Register allocation or every private representation |
| Storage and data interchange | Size, alignment, representation, field layout, padding, byte order and explicitly selected external formats | Which registers pass a value or whether it may be shared concurrently |
| Target procedure-call profile | Argument/result classification, register and stack rules, preservation, variadic rules where supported, and unwind interoperability | Source conversions, ownership or permission to reinterpret equal-sized types |
| Runtime/module boundary | Published identities, effects, roots, cleanup, allocation ownership and compatible replacement | All private implementation choices inside a jointly compiled region |

Procedure-call standards are a useful organizational model: each target
profile should have an explicit data-layout chapter and a call-interface
chapter. Memory-access guarantees are nevertheless language obligations, not
merely calling-convention conventions.

## Primitive storage and atomicity

For ordinary coherent memory, naturally aligned primitive loads and stores
up to the machine pointer size are atomic. Eligible trivial wrappers retain
their carrier's guarantee; managed-reference loads/stores are also non-tearing.
Terrum-provided storage and lowering must meet the requirements. An ordinary
user need not request a special atomic wrapper just to prevent tearing.

| Value or operation | Required baseline |
|---|---|
| `int32` / `uint32` on the intended 32-/64-bit profiles | Four-byte storage with natural alignment; ordinary whole-value loads/stores are atomic |
| Other primitive storage no wider than the machine pointer | Naturally aligned whole-value loads/stores are atomic; the profile records representation and alignment |
| Managed reference | Runtime-provided storage and access preserve non-tearing reference values; rooting and reclamation remain separate obligations |
| Trivial primitive wrapper | Preserves the carrier's atomic access contract under checked eligibility; nominal identity and value validity remain intact |
| Multi-field aggregate, even if no wider than a pointer | No whole-aggregate atomicity follows from size alone |
| Primitive wider than a pointer | No automatic whole-value atomicity from this baseline; a stronger operation or target guarantee must say so |
| Increment, decrement or other compound update | Individual atomic loads/stores do not make the complete update an atomic RMW |

The pointer-width rule does not itself commit Terrum to every historical
machine. A target profile must meet the required semantics or explicitly fall
outside the supported profile; it cannot silently weaken `int32` atomicity.
This does not promise atomicity for arbitrary unaligned foreign storage,
device memory, or concurrent overlapping accesses under incompatible protocols.

These are observable access guarantees, not an obligation to emit a memory
instruction for every source operation. Private nonconcurrent values may stay
in registers or be optimized away. Shared accesses must preserve the selected
memory contract, including any permitted elimination or combination of accesses.

### Trivial wrappers are not arbitrary small aggregates

A type explicitly promised as a trivial wrapper carries one primitive value
without changing the storage/access properties named by that promise. Its
nominal type and validation requirements remain meaningful. Exact eligibility
and declaration syntax need specification; neither a one-field declaration
nor equal size alone establishes any unstated ABI equivalence.

An ordinary declaration such as `struct CounterValue { int32 value; }` makes
no foreign-interoperability promise merely because it currently has one field
or happens to match a primitive's size. A wrapper must explicitly opt into a
checked contract before external code may rely on its representation. A frozen
or Transparent promise constrains future compatible replacement under that
same contract; changing the field set or carrier is not silently compatible.
Each promise is scoped to its target profile.
For example, promising that `CounterValue` is a single `int32` carrier for one
profile is a distinct contract from merely declaring its current field; its
consumers rely on the promise, not an observation of today's source shape.

For contrast:

```text
struct S { int8 x; int8 y; }
```

This can occupy two bytes while exposing two separate fields. A whole-value
copy may use separate field accesses; it is not promised to be an atomic
two-byte snapshot. A backend selecting one wider instruction for a particular
copy does not strengthen the portable contract.

The accepted Transparent contract deliberately combines a frozen carrier
representation with carrier-equivalent argument/result classification for the
selected target profile. Its checked eligibility must preserve size/alignment,
validity, ownership, cleanup and any niche rules. This named combined promise
does not imply that every layout opt-in has the same effect: Sequential or
Union layout alone does not promise procedure-call equivalence. Atomic access,
sharing, arbitrary-bit validity and ordinary source conversion remain separate
questions; no promise permits bypassing a wrapper's invariant or establishes a
universal cross-target encoding.

## What a target data-layout profile must record

- Primitive storage size, natural alignment, valid representations and byte
  order; native-sized values and reference representation are identified
  separately from fixed-width integers.
- Scalar access guarantees, including eligible wrapper accesses, separately
  from wider atomic operations, RMWs and ordering instructions.
- Aggregate layout and alignment, field order, padding, arrays and element
  stride, and the obligations of explicitly chosen interchange layouts.
- Supported unaligned operations and any stronger, target-specific atomic
  guarantees, with exact cache-line/page and memory-type conditions.
- Representation and coordination needed when code generated by different
  compilers or modules accesses the same storage.

The size/alignment/representation table is still to be completed; this document
does not choose a default serialization format, boolean encoding, reference
encoding, or SIMD ABI. Register classification is not inferred from storage
size alone.

Ordinary layout remains opaque in the accepted language direction. Explicit
Sequential, Union and Transparent contracts constrain what may change.
Sequential layout computes offsets and padding from declared order and
alignment; it is not arbitrary user-selected offsets. Union support does not
automatically make inactive-member access safe. See the
[accepted layout direction](language.md#types-and-operations).
The currently implemented compact value/module layouts remain exact contracts,
not an already implemented resilient-layout mechanism.

## Representation access is not numeric conversion

An explicit value-level helper can expose the backing `int32` bits of a
`float32` without creating overlapping typed references. Its contract specifies
bit preservation, including signed zero and NaN encodings, rather than
performing a floating-to-integer numeric conversion. Such helpers must not
become unrestricted construction of references or invalid nominal values.

Explicit unaligned operations are distinct from reinterpretation and from
atomicity. `MemoryPrimitives.UnalignedRead`/`UnalignedWrite` may assemble or
store an unaligned value through smaller accesses
without promising an atomic snapshot. Overlapping typed views require unsafe
code or a checked abstraction that accounts for aliasing, initialization,
lifetime, validity and concurrency. Any safe subset of overlapping union
access remains to be defined; matching fields are not sufficient by themselves.

The backend may implement a valid source operation with register bit moves or
overlapping machine accesses when all observable source guarantees survive.
It may not use that freedom to overwrite neighboring storage or introduce
concurrency conflicts absent from the source.

CAS support requires a defined bitwise-comparison representation. This is
neither ordinary numeric equality nor automatic eligibility for any equally
sized aggregate; unspecified padding cannot become an accidental comparison
input. Representation helpers must still preserve nominal validity and access
obligations.

## Private implementation versus independently replaceable boundaries

Where the compiler controls both sides and can regenerate them together,
private calling conventions and representations may remain implementation
details. It may specialize calls, pass values differently or eliminate a
representation while preserving source semantics and every externally
observable boundary.

Where independently generated machine code must continue to interoperate
unchanged, the crossing contract must be explicit and versioned, or mediated
by an adapter. This distinction is not simply static versus dynamic linking:
native object files, retained callbacks and runtime entry points can require
stable coordination too. Public source visibility alone does not freeze all
private layouts.

Consumer-visible transfer/share capabilities likewise come from published
declaration contracts, not inspection of the current private representation.
An opaque layout may evolve while preserving a promised capability; a frozen
layout alone does not prove sharing safety.

The current module paths illustrate the distinction:

- A persisted semantic consumer selects a compatible producer and both are
  lowered together. Its artifact retains checked source-level contracts;
  `run-module` generates native code for that launch, and `link-module` emits
  a fresh PE. This does not promise replacement of an already emitted static PE.
- A persisted native consumer calls an independently compiled DLL. Compatible
  replacement must preserve the physical crossing contract without rebuilding
  the consumer image.

A future managed boundary also needs explicit root/barrier conventions,
allocation and cleanup ownership, failure/unwind behavior and lifetime rules.
A matching register convention alone is insufficient. Whether the boundary
uses one stable managed ABI, versioned profiles or adapters remains open.
No public managed-handle ABI is introduced by these contracts.

## Remaining decisions requiring review

| Decision | Accepted boundary that the decision must preserve |
|---|---|
| Exact wrapper eligibility and declarations | Define which forms may promise primitive-carrier atomicity or Transparent ABI and how each promise is exposed. These are independent, checked contracts; equal size, a single field or nominal identity alone does not establish every equivalence. Ordinary structs receive no inferred foreign-interoperability promise |
| Any safe overlapping-union subset | Decide whether to admit a narrowly checked subset and its active-member, initialization, aliasing and lifetime conditions. Until then, matching fields do not establish safe access |
| Additional externally promised representations | Choose an encoding or interchange rule only where the selected target/ABI does not already determine it. Storage layout is not a default serialization format, and no general reference encoding or SIMD ABI is selected here |
| Future public managed boundary | Choose stable versus versioned profiles/adapters together with roots, barriers, allocation/cleanup ownership, failure and lifetime contracts. Private managed implementations do not imply a public managed-handle ABI |

These decisions need not block unrelated compiler work. Keep unsupported forms
unsupported until their contracts are selected; do not infer approval from a
current backend representation.

## Profile specification and implementation work

For an already selected target contract, record primitive size, alignment,
representation, access guarantees and procedure-call classification separately.
Complete wrapper tables after the eligibility decision, and implement the
corresponding layout, validity and metadata checks. Describe cross-module
compatibility independently of private lowering, preserving existing artifact
contracts and feature-version checks. Introduce any future checked opt-in
enforcement through an explicit compatibility migration; do not retroactively
reject ordinary declarations or reinterpret retained compact-value/module
contracts.

Validate argument/result classification, padding and boundaries, non-tearing
accesses, malformed metadata and compatible replacement across the supported
AOT, JIT and module paths. Accepted memory guarantees must not depend on an
accidental current backend sequence. This work does not authorize another
architecture backend or silently settle the review items above.
