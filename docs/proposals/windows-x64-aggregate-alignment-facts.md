# Windows x64 aggregate alignment components

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This is a proposed contract amendment, not
normative adoption, compiler support, native qualification or a stable ABI.
It supplies direction only; this document-only task authorizes no integration.

## Decision and evidence

Recommend a **shared derivation of natural/default alignment `A`, required
explicit alignment `R`, and completed object alignment `C`**. For positive
stored representations, `C = max(A, R)`. Keep existing completed-alignment
observations and physical layouts; do not recover `A` by assuming `R <= A`,
subtracting `R`, or changing only optional payloads.

The [optional layout proposal](windows-x64-optional-value-layout.md#exact-tagged-layout)
requires payload facts `s=32, A=8, R=32`, giving payload offset `P=32`
and optional extent/stride `S=64`. Its synthetic
[`OptionalValueLayoutPreservesRequiredAlignmentBeyondNaturalAlignment`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/CheckedReferenceTypeTests.cs)
constructs a fieldless `BoundStruct` with `Alignment=8, RequiredAlignment=32`
and checks only the optional layout calculation.

That is not source-backed layout evidence.
[`BoundStruct.Alignment`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
currently means **completed** object alignment.
[`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs),
[`FixedLayoutStorage.Compute`](../../sources/Terrum.CodeAnalysis/Semantics/FixedLayoutStorage.cs)
and [`NativeFixedArrayContract.TryGetStructureLayout`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFixedArrayContract.cs)
accumulate required field-placement alignment into that completed fact.
Their `32/32` result for a packed wrapper around an explicitly 32-aligned
child follows [Windows x64 §5](../specs/targets/windows-x64-data-layout.md#5-aggregate-placement-empty-storage-and-stride).
This is **an intentional amendment to retain an additional compositional
fact**, not evidence that those producers violate the accepted rule or that
one more native test alone would resolve the gap.

The source-backed
[`RequiredAlignmentAboveNaturalAlignmentSurvivesOptionalArrays`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/OptionalValueOperationTests.cs)
fixture now uses a 32-aligned payload inside a Packing=8 wrapper and an
optional fixed array. It observes completed wrapper alignment 32 and optional
`A=8, R=32, P=32, S=64`, then executes the operations under both runtime
policies. A supervised emitted image now executes the same mixed-alignment
fixed-array copy/repeat case. This establishes source-derived natural
alignment through layout, JIT and image execution for this composition; it
does not qualify every packed/array combination or source-free replacement.

[`NestedAlignmentSurvivesPackingAndFixedArrayStride`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/LayoutCoreRegistrationTests.cs)
and [`StandardSourceFreeRoundTripsPackingAlignmentAndArrayStride`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFixedArrayLayoutTableTests.cs)
check completed alignment and required alignment both equal to 32.
The [layout inventory](../implementation/layout-profiles.md#registered-source-layout-and-public-fixed-layout-profile)
and [native inventory](../implementation/native.md) describe bounded
packed/aligned and optional slices, not qualification of source-derived
`A=8/R=32`. These are inspected sources, not newly executed test results.

## Proposed derivation

`A` means the natural/default, pack-reducible component **after this type's
own declared Alignment, but before inherited required-alignment floors**.
`R` is the maximum of one, the type's supplied Alignment, and transitive
explicit subobject requirements. `C`, not `A` alone, is the standalone
placement and ordinary object-alignment observation. Positive alignment
components are powers of two; zero-data exceptions retain their selected rules.

For Sequential/Union with packing `K`, derive from complete field facts:

* Each nonzero field contributes `n_i = min(A_i, K)` to natural alignment
  and has placement alignment `q_i = max(n_i, R_i)`.
* `A0 = max(1, all n_i)`; independently `R0 = max(1, all field R_i)`.
  Void contributes no natural placement requirement or bytes.
* Without a supplied Alignment, retain `A=A0`, `R=R0`, `C=max(A0,R0)`.
* A supplied `D` must satisfy the existing limits and
  `D >= max(A0,R0)`. Retain the current own-control behavior:
  `A=D`, `R=max(R0,D)`, `C=max(A,R)`. A parent's Packing may cap this
  child's `A` contribution but can never remove its `R`.

Sequential rounds each nonzero field start to `q_i`; Union keeps every
alternative at zero. Use complete child extents without repacking them,
then round the aggregate extent to **`C`**, not merely `A`.
Preserve ordinary empty-aggregate minimum padding, logical void offsets
and Transparent's exact carrier size/alignment/required-alignment behavior,
including its zero-data exceptions. No zero-extent address/anchor support
or new Union initialization/cleanup rule is supplied.

This deliberately preserves the optional proposal's directly explicitly
aligned `32/32/32` case; it does not redefine `A` as alignment with *all*
declared controls removed. Only inherited required floors stop being
folded back into a parent's natural/default component.

The physical placement rule is preserved because
`max(min(max(A,R),K),R) = max(min(A,K),R)`.
Thus separating these facts does not change admitted field offsets,
completed alignment, tail padding or aggregate carrier classification.
This is a correctness/scope argument, not an assumption that pre-v1
contracts cannot be changed.

For admitted arrays, derive `A_array=max(1,A_element)` and inherit
`R_element`; require base alignment `C_array=max(A_array,R_element)`
and stride `roundUp(s_element,C_array)`. Validate the complete element even
at count zero. Preserve existing unit/zero-extent container floors and
address restrictions; every addressable element, not just the first,
must satisfy its required alignment.

For tagged options, use the existing equations unchanged:
`A_option=max(1,A_payload)`, `R_option=max(1,R_payload)`,
`L=max(A_option,R_option)`, `P=roundUp(1,L)`,
`S=roundUp(P+s_payload,L)`. No tag, niche, validity, padding or publication
rule changes. Standalone storage requires `L`; containing Packing applies
only to the envelope's natural contribution, never its internal payload
offset, extent or required floor.

## Owners and precise intended amendments

On adoption, integrate the following together; no linked file is edited here.

| Owner | Intended amendment or preservation |
|---|---|
| [General layout: Definitions](../specs/data-layout-and-abi.md#definitions) and [Packing and aggregate alignment](../specs/data-layout-and-abi.md#packing-and-aggregate-alignment) | Define `A/R/C` with the derivation above. Replace ambiguous uses of aggregate alignment with the appropriate component. Blittable fixes completed storage geometry and retains the compositional facts needed by enclosing layouts; no additional marker. |
| [Windows x64 §4](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas) | Clarify that supplied Alignment cannot weaken `max(A0,R0)`. Preserve registration, grammar, Packing values/defaults, Alignment range 1..8192 and field eligibility. |
| [Windows x64 §5](../specs/targets/windows-x64-data-layout.md#5-aggregate-placement-empty-storage-and-stride) | Replace the rule accumulating placement alignments into the sole aggregate fact with separate `A/R/C` derivation. Preserve placement, own Alignment, extent, empty/Transparent rules and checked target-nuint arithmetic; state base/stride requirements using `C`. |
| [Windows x64 §6](../specs/targets/windows-x64-data-layout.md#6-access-padding-and-observations), [general observations](../specs/data-layout-and-abi.md#value-layout-observations) and [registered queries](../specs/language/registered-queries.md#layout-observation-and-authorization) | Explicitly retain `alignof` as **`C`**, with the existing void exception and nint result range. `A=8/R=32` never authorizes an eight-aligned standalone object. Packed access uses actual path alignment; no new aligned-reference, pointer or atomicity permission. |
| [Metadata Layout semantics](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Clarify that declared kind/Packing/Alignment and complete ordered fields derive `A/R/C` under the selected target. Add no serialized geometry, row, operand, Feature or TargetProfile. Preserve complete variant replacement and binding requirements. |
| [Optional proposal: Exact tagged layout](windows-x64-optional-value-layout.md#exact-tagged-layout) | Make payload natural alignment refer to shared `A`, not a query returning completed `C`. Retain every representation equation and existing domain/exclusion; add the source-derived mixed fixture below when integrating. |
| [Fixed-array proposal: Complete identity and exact layout row](standard-trrm-fixed-array-identity-shape.md#complete-identity-and-exact-layout-row) | Distinguish its existing completed/container object-alignment operand from the new natural component. Preserve the operand's existing `max(1, completed element alignment)` derivation and byte shape; derive natural `A_array` separately. Do not silently write 8 into an existing mixed-element row whose object alignment must remain 32. |

The amendment selects no optional foreign carrier, independently replaceable
ABI, descriptor representation, pointer qualification, runtime policy or
new executable interchange composition.

## Producer, consumer and native integration

* **Source/shared facts:** derive `A/R/C` over normalized complete fields
  through one general layout mechanism. Binding, mandatory source verification,
  arrays/options, bound-tree replacement and retained query dependencies
  must agree. Preserve nominal identities, Copy/validity and actual provider
  origins. Existing `BoundStruct.Alignment` and other completed-alignment
  slots may remain `C`; derive/cache `A` separately rather than repurposing
  them or admitting the synthetic fixture as a verified declaration.
* **Persistence/binding:** Standard nominal Layout stores declared inputs,
  so source-free reconstruction can derive the new component without an
  added wire field. Retain existing private completed Alignment/RequiredAlignment
  meanings and array object-alignment operands; recompute `A` from complete
  declarations/element identities. Changing a persisted operand's meaning
  instead requires a separate explicit exact-schema decision, not header
  patching or a compatibility guess. Rewriters preserve/remap Module,
  TypeSpec, fields, layout variants, original expectations and provider
  obligations; equal `C` or size does not excuse losing relied-on `R`.
* **Consumers:** perform required framing, bounds, reference, selected-schema,
  support and binding checks; derive geometry where the selected layout schema
  requires it. Reject contradictory recorded geometry under its assigned
  meaning, but not a valid derived `R>A`.
  [Producer truth and consumer checks](../specs/representation/reader-trust.md)
  remain distinct: this does not mandate re-proving arbitrary Body facts.
  Preserve [exact draft agreement](../specs/representation/format-versioning.md).
* **Native:** allocate locals, spills, result/scratch buffers and array bases
  at `C/L`; retain actual alignment for every packed projection and copy.
  [`ScratchAlignment`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
  already takes the maximum of both components; inspect all other uses
  rather than assuming that one helper establishes support. Preserve probing,
  unwind recovery, exact authorized access extents and optional publication.
  Emission, JIT execution and PE execution remain separate evidence gates.

## Conformance fixtures and acceptance

This **accepted target-syntax declaration fragment** requires explicitly
selected Layout/Blittable Core registration. Its split-fact expectations
below are proposed; it is not a runnable program or native evidence.

```terrum
[Blittable]
[Layout(LayoutKind.Sequential, Alignment: 32)]
public struct AlignedLeaf { public uint64 Value; }

[Blittable]
[Layout(LayoutKind.Sequential, Packing: 8)]
public struct Mixed { public AlignedLeaf Value; }
```

| Case | Required proposed outcome |
|---|---|
| Source admission and facts | `AlignedLeaf`: `s=32,A=32,R=32,C=32`. `Mixed`: field offset 0, `s=32,A=8,R=32,C=32`. `alignof(Mixed)=32`. Derive through public compiler APIs and required verification, not injected bound facts. |
| Mixed tagged optional | `P=32,S=64,A=8,R=32,L=32`; standalone base is 32-aligned. Present payload keeps the leaf's representation/offsets; absence never reads it. |
| Packed containing record | With one-byte prefix, optional Mixed, one-byte suffix and Packing=1: offsets `0,32,96`, optional payload at 64, record extent/stride 128, `A=1,R=C=32`. Check neighbors and tail padding without assuming padding initialized. |
| Ordinary arrays and every address | Three Mixed elements have stride 32 and starts `0,32,64`; three optional Mixed elements have stride 64 and starts `0,64,128`, payloads `32,96,160`, from a 32-aligned base. Check **all** element/payload addresses and nested arrays, preserving required alignment through packed parents. Existing object-alignment wire operands remain 32, not 8. |
| Packing/control variants | Sequential and admitted raw Union wrappers with Packing 1/4/8/16 give natural components 1/4/8/16, always `R=C=32`. Transparent inherits its carrier's facts and rejects controls. Preserve validity/access contracts; a supplied parent Alignment below inherited 32 rejects. |
| Natural-only underalignment | Packing=1 may underalign an optional uint64 whose `R=1`, without repacking its envelope. Access must use actual alignment; no aligned member borrow or stronger atomic guarantee follows. This contrasts with the nonpackable `R=32` case. |
| Overflow and capacity | Check field-end addition, aggregate/optional round-up and count-times-stride in Windows x64 nuint. A completed payload extent `2^64-32` plus `P=32` overflows; `2^58` elements at stride 64 overflow. Check adjacent representable boundaries type-only, query nint range separately, and distinguish backend/codec/resource limits without huge allocations or sentinels. |
| Source-free/replacement | Reconstruct the same declared layouts without producer source, preserve facts through selected private/Standard type-only round-trip and provider relocation/re-emission. Equal-sized replacements that weaken relied-on alignment contracts fail binding. Optional type transport alone does not admit an executable optional Body. |
| Malformed/unsupported input | Reject bad Packing/Alignment controls, zero/non-power-of-two positive-storage alignments, missing/wrong-owner field/type references, inline cycles even at array count zero, wrong profiles/schemas, and inconsistent persisted completed alignment, stride or extent. Do not reject `R>A` itself, infer Core registration from names, or invent facts from unknown/unsupported payloads. |
| Native qualification | Supervised headless JIT and PE runs exercise this source-derived payload, standalone/packed/repeated storage, present/absent copies and every address above using admitted observation mechanisms. Qualify staging/probing/unwind paths where affected; retain final compiler/configuration identity. A layout-query assertion or emitted image alone is insufficient. |

## Alternatives and remaining work

| Alternative | Trade-off |
|---|---|
| Optional-only natural derivation | Smallest local patch: recursively recover payload `A` for options while keeping all general APIs on `C`. Physical optional layout can be correct, but arrays, metadata reconstruction and other enclosing layouts still need the same derivation. Duplicated optional-only authority is not the smallest coherent integration. |
| **Shared component derivation — recommended** | Retains current physical geometry and own Alignment behavior, makes the mixed fact source-derivable, and supplies one reusable producer/consumer rule without a wire extension. |
| Pure natural alignment excluding every supplied Alignment from `A` | A coherent broader redesign, not forbidden by draft compatibility. It changes the directly aligned optional fact convention and requires an explicit amendment of those examples/consumers; it is unnecessary for this gap. |

Formal adoption of the component convention remains a decision. Source-derived
optional layout, JIT execution and emitted-image execution cover the mixed
`A=8/R=32` composition. Source-free reconstruction/replacement and the
remaining packed and array fixtures remain open.
Internal caching versus on-demand derivation is engineering, not a new source
contract. Any changed byte semantics, richer optional Body/value codec or ABI
crossing still requires its own explicitly selected proposal/schema; none is
provisionally selected by this amendment. No broader optional or layout
milestone is declared complete.
