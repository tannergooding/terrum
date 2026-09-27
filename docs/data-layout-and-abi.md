# Data layout and ABI contracts

> **Design direction and proposal.** This document separates agreed primitive
> memory guarantees from layout, interchange and procedure-call decisions that
> still need target profiles. It does not freeze a universal Terrum managed ABI
> or claim these facilities are implemented. See [implementation](implementation.md)
> for current support and [memory model](memory-model.md) for concurrency rules.

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

A trivial wrapper carries one primitive value without changing its relevant
storage and access requirements. Its nominal type and validation requirements
remain meaningful. Exact eligibility and declaration syntax need specification;
neither a one-field declaration nor equal size alone establishes every ABI
equivalence.

For contrast:

```text
struct S { int8 x; int8 y; }
```

This can occupy two bytes while exposing two separate fields. A whole-value
copy may use separate field accesses; it is not promised to be an atomic
two-byte snapshot. A backend selecting one wider instruction for a particular
copy does not strengthen the portable contract.

Atomic wrapper access and a transparent calling convention are separate
questions. The accepted Transparent direction additionally requires carrier-
equivalent argument/result classification, size/alignment, validity, ownership,
cleanup and any niche representation. It does not imply an ordinary source
conversion or permit bypassing a wrapper's invariant.

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
[accepted layout direction](language-library-review.md#21-opaque-layout-and-explicit-abi-contracts).
The currently implemented compact value/module layouts remain exact contracts,
not an already implemented resilient-layout mechanism.

## Representation access is not numeric conversion

An explicit value-level helper can expose the backing `int32` bits of a
`float32` without creating overlapping typed references. Its contract specifies
bit preservation, including signed zero and NaN encodings, rather than
performing a floating-to-integer numeric conversion. Such helpers must not
become unrestricted construction of references or invalid nominal values.

Explicit unaligned operations are distinct from reinterpretation and from
atomicity. A helper may assemble an unaligned value through smaller accesses
without promising an atomic snapshot. Overlapping typed views require unsafe
code or a checked abstraction that accounts for aliasing, initialization,
lifetime, validity and concurrency. Any safe subset of overlapping union
access remains to be defined; matching fields are not sufficient by themselves.

The backend may implement a valid source operation with register bit moves or
overlapping machine accesses when all observable source guarantees survive.
It may not use that freedom to overwrite neighboring storage or introduce
concurrency conflicts absent from the source.

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
No public managed-handle ABI is introduced by this proposal.

## Remaining profile work

Complete the primitive and wrapper eligibility tables, select target procedure-
call profiles, and specify cross-module compatibility rules independently of
internal lowering. Coordinate any checked union subset with the aliasing and
lifetime model. API names and attributes remain proposals until approved;
memory guarantees must not depend on an accidental current backend sequence.
