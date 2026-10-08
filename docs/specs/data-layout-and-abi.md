# Data layout and ABI contracts

> **Accepted core contracts; target profiles remain to be specified.** The
> memory guarantees, opaque-layout direction and boundary-preservation rules
> below are accepted. This is not a completed target ABI specification or a
> universal Terrum managed ABI. Current support remains defined by
> [implementation](../implementation.md) and the [native profile](native.md);
> concurrency semantics are defined by the [memory model](memory-model.md).

## Definitions

| Term | Meaning |
|---|---|
| `Blittable` | A per-target-profile commitment that fixes a type's size, alignment and field offsets, and requires every field to be transitively `Blittable`. It is a fixed, directly-interoperable representation promise, not selected compiler syntax or a universal byte encoding. |
| `Transparent` | A layout commitment that a type has exactly one field, with that field's representation and carrier-equivalent argument/result classification. It inherits the carrier ABI and does not accept custom packing or aggregate alignment. |
| `Auto` | The default instance layout: fields are private, external access goes through properties or methods, and the declaration carries no `Blittable`, packing or aggregate-alignment commitment. |
| `Sequential` | A layout that places fields at offsets following declaration order, subject to the target's natural alignment, packing and aggregate-alignment rules. |
| `Union` | A layout that places every field at offset zero, requiring the `Blittable` commitment. It permits overlapping storage; it does not by itself provide safe typed access to inactive alternatives. |
| Independently replaceable boundary | A crossing point between separately compiled or separately versioned artifacts, where the physical contract must be explicit and versioned rather than an internal implementation detail. |
| Private implementation detail | A representation or calling-convention choice visible only where the compiler controls, and can regenerate, both sides of a crossing. |

## Scope

Per-target size/alignment/representation tables, procedure-call classification
and compiler conformance remain target-profile work; current compiler support
for any part of this contract is tracked in
[implementation](../implementation.md) and the [native profile](native.md), not
restated here.

Unimplemented does not mean unapproved. Completing a table from an already
selected target contract does not reopen the language policy; choosing a new
observable representation or independently replaceable public boundary does.
The [proposal index](../proposals/README.md) and
[roadmap](../roadmap.md) track pending designs and implementation without making
the accepted core provisional.

A `Blittable` or `Transparent` commitment is a compatibility promise: changes
that violate it are breaking, but neither promise a universal byte encoding
or freeze layout kinds that were not committed. Any future checked source
enforcement of these commitments needs an explicit compatibility migration;
it does not retroactively reject ordinary declarations or revoke existing
source/artifact contracts.

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
nominal type and validation requirements remain meaningful. The accepted
eligibility policies below do not settle exact source spelling, target-profile
checks or metadata. Neither a one-field declaration nor equal size alone
establishes any unstated ABI equivalence.

An ordinary declaration such as `struct CounterValue { int32 value; }` makes
no foreign-interoperability promise merely because it currently has one field
or happens to match a primitive's size. A wrapper must explicitly opt into a
checked contract before external code may rely on its representation.
For example, promising that `CounterValue` is a single `int32` carrier for one
profile is a distinct contract from merely declaring its current field; its
consumers rely on the promise, not an observation of today's source shape.

For contrast:

```text
struct S { int8 x; int8 y; }
```

This can occupy two bytes while containing two separate fields. A whole-value
copy may use separate field accesses; it is not promised to be an atomic
two-byte snapshot. A backend selecting one wider instruction for a particular
copy does not strengthen the portable contract.

The accepted Transparent contract is exactly one field with the carrier's
representation and carrier-equivalent argument/result classification under the
selected target profile. It inherits the carrier ABI and does not accept custom
packing or aggregate alignment. A layout kind alone does not freeze the type.
Atomic access, `Copy`, sharing, arbitrary bit-pattern validity and ordinary
source conversion remain separate; this contract does not establish a
universal cross-target encoding or bypass a wrapper invariant.

### Fixed directly interoperable representations

**Blittable** denotes one bundled, fixed,
directly-interoperable representation promise per target profile. A conceptual
`Blittable: true` commitment fixes size, alignment and field offsets and
requires transitively compatible fields; it is not selected compiler syntax
or a universal byte encoding. An ordinary struct does not acquire this promise
from its current shape. Ordinary `ref<T>` is excluded; `ptr<T>` may be
eligible when its target representation is covered and its ordinary pointer
validity, lifetime and access-safety rules are met.

Blittability/frozen representation does not initialize padding bytes, validate
arbitrary bit patterns, imply `Copy` or sharing, grant atomic access, or
authorize every raw operation.
These restrictions describe instance payloads. Static storage is not part
of instance layout; a foreign static data binding requires its own compatible
representation, access, validity, synchronization and lifetime contract under
the [source declaration rules](grammar-spec.md#31-source-file-structure-and-declarations).
`Auto` is the default: instance fields are private, external access goes through
properties or methods, and the declaration has no `Blittable`, packing or
aggregate-alignment commitment. Unfrozen instance fields remain private; a frozen
representation may expose fields without changing ordinary readonly or `mut`
access rules. Public instance-field access requires the fixed `Blittable` commitment,
regardless of layout kind; neither `Sequential` nor `Transparent` alone makes
an exposed field a `Blittable` field. `Sequential` lays fields out in
declaration order; `Union` places every field at offset zero and requires the
fixed `Blittable` commitment. Neither permits
arbitrary user-selected field offsets. Those kinds do not themselves freeze
the layout or carrier across versions. Layout-dependent values without a fixed commitment
cannot cross an independently replaceable dynamic boundary; they may be used
where producer and consumer remain layout-coupled through compilation,
rebuild, or invalidation. JIT versus AOT labels alone do not determine that
coupling.

A `Sequential` annotation on a nonpublic compact struct is a checked subset of
this contract, not a `Blittable` or stable public ABI promise: it does not by
itself cover public fields, unique owners, or packing/aggregate-alignment
controls. Current compiler support for this annotation is tracked in
[implementation](../implementation.md).

### Packing and aggregate alignment

Both a power-of-two member-packing limit and a separate power-of-two
aggregate-alignment control are accepted; they may be combined, though most
declarations need only one. Packing caps member alignment and the derived
natural aggregate alignment; aggregate alignment may over-align the result,
with tail padding preserving aggregate alignment and array stride. The resolved
library attribute schema defines argument names, types and defaults; its
compiler-recognized registration must match the semantic metadata shape.
No special layout argument grammar or universal positional overload is
selected. Concrete schemas, target limits and encoding remain specification
work. For a struct
with `int8` followed by `int32`, assuming the profile naturally aligns `int32`
to four bytes:

| Controls | Field offsets (`int8`, `int32`) | Size / array stride | Aggregate alignment |
|---|---|---|---|
| Packing limit 1 | `0, 1` | `5` | `1` |
| Aggregate alignment 32 only | `0, 4` | `32` | `32` |
| Packing limit 1 plus aggregate alignment 32 | `0, 1` | `32` | `32` |

Transparent inherits its carrier ABI and does not permit custom packing or
aggregate-alignment overrides. Safe access lowering for packed, under-aligned
members remains to specify.

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
size alone. Explicit raw `Union` layout permits overlapping storage, not safe
typed access to inactive alternatives, and requires the fixed `Blittable`
commitment. Safe non-Blittable discriminated-union/sum types are a separate
higher language feature. See the
[accepted layout direction](language.md#types-and-operations).
The currently implemented compact value/module layouts remain exact contracts,
not an already implemented resilient-layout mechanism.

## Representation access is not numeric conversion

An explicit value-level helper can expose the backing `int32` bits of a
`float32` without creating overlapping typed references. Its contract specifies
bit preservation, including signed zero and NaN encodings, rather than
performing a floating-to-integer numeric conversion. Such helpers must not
become unrestricted construction of references or invalid nominal values.

Initializing every field does not initialize the aggregate's padding bytes.
Raw object-representation blits and direct raw foreign transfer are unsafe
operations; an explicit Blittable contract supplies a fixed target-profile
representation, not blanket permission for unsafe access. A safe binary
serializer must account for padding initialization and representation validity
under its wire format rather than assume that every byte of a live value is
initialized or has a portable representation. Field-wise encoding is one
approach, not a requirement to exclude a correctly validated raw-copy implementation.

Explicit unaligned operations are distinct from reinterpretation and from
atomicity. `MemoryPrimitives.UnalignedRead`/`UnalignedWrite` may assemble or
store an unaligned value through smaller accesses
without promising an atomic snapshot. Reinterpreting overlapping storage through
incompatible typed views requires unsafe code. A raw `Union` layout is the only
accepted way to request overlapping field storage, and it does not provide
active-member tracking or a safe subset of typed reinterpretation. A future
tagged/sum type is a separate feature whose case and tag checks must preserve
ordinary validity, ownership, roots and cleanup, and need not be Blittable.
Raw `Union` requires the explicit fixed `Blittable` commitment; matching
fields are not sufficient by themselves.

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

Published language-level signatures and their layout, access, ownership and
effect contracts are compatibility commitments for independently replaceable
consumers, whether or not they promise foreign interoperability. A public
field exposes its representation and therefore requires `Blittable`; a public
method does not need a foreign-layout annotation merely because it is public.
Changing a published guarantee incompatibly breaks replacement, while
contract-preserving implementation changes and compatible extensions or
stronger guarantees are not automatically breaking. This language/module
binary contract is distinct from both a foreign procedure-call ABI and a
serialization or wire format.

The current module paths illustrate the distinction:

- A persisted semantic consumer selects a compatible producer and both are
  lowered together. Its artifact retains checked source-level contracts;
  `run-module` generates native code for that launch, and `link-module` emits
  a fresh PE. This does not promise replacement of an already emitted static PE.
- A persisted native consumer calls an independently compiled DLL. Compatible
  replacement must preserve the physical crossing contract without rebuilding
  the consumer image.

Terrum does not promise a published, frozen managed-machine ABI. Managed
precompiled code is version-sensitive implementation material, not a durable
foreign interface. Before dynamically combining it with dependencies, the
runtime must have enough metadata about dependency, compiler and runtime
assumptions to validate that code or adapt, recompile, discard, or reject it
before execution. The mechanism is engineering work: adapters, recompilation
from an available form, and load/compile failure are possibilities, not
selected requirements. There is no mandatory JIT fallback or requirement to
retain IR; strict AOT may require rebuilding or fail. This strategy cannot
weaken published language contracts or required static and safety guarantees.

Foreign exports are distinct: an explicitly declared FFI boundary is fixed by
its target profile, and implementation changes behind it must preserve the
caller's contract rather than reinterpret it. The current implementation does
not support public managed-handle exports; no new export surface or stable
managed ABI is implied here.

## Unsupported extensions

No unresolved policy choice remains in the accepted layout model. Keep
unsupported forms unsupported until their specifications and implementations
are complete; do not infer compiler support from a current backend
representation. Define serialization, callback and other
external protocol encodings for concrete boundaries as needed, reusing the
selected profile where it determines representation; no global boolean,
reference or SIMD wire format is selected.

## Profile specification and implementation work

For an already selected target contract, record primitive size, alignment,
representation, access guarantees and procedure-call classification separately.
Native interop must match the selected system ABI's argument and result
classification. On Windows x64, a by-value struct argument classified for
indirect passing uses a caller-created shadow copy; it does not pass the
address of the caller's actual storage or acquire source-level
`implicit ref<T>` semantics. The target ABI determines direct versus indirect
passing; this classification does not impose a maximum struct storage size.
Specify both packing and aggregate-alignment controls, their profile limits and
the safe-access rules for any under-aligned members. Specify source spellings,
target limits, metadata, transitive `Blittable` field eligibility, and boundary
coupling/invalidation rules; then implement the corresponding layout, validity
and metadata checks and conformance cases. Describe cross-module compatibility
independently of private lowering, preserving existing artifact contracts and
feature-version checks. Introduce any future checked opt-in enforcement through
an explicit compatibility migration; do not retroactively reject ordinary
declarations or reinterpret retained compact-value/module contracts. Specify
external protocol encodings only for concrete boundaries that need them; do
not turn language-level binary compatibility into a universal wire format.
For managed precompiled-code composition, specify dependency/runtime metadata
validation and before-execution handling; preserve the accepted boundary
without mandating JIT/IR retention, an adapter, or fallback.

Validate argument/result classification, padding and boundaries, non-tearing
accesses, malformed metadata and compatible replacement across the supported
AOT, JIT and module paths. Accepted memory guarantees must not depend on an
accidental current backend sequence. This work does not authorize another
architecture backend or silently settle the review items above.
