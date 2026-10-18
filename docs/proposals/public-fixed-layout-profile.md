# Public fixed-layout integration

**Status:** The Windows x64 layout semantics, registered source schemas,
explicit transitive Blittable commitment and use of the existing target
identity are accepted. Natural Sequential and scalar/raw-pointer Transparent
source layouts have bounded implementation support. Standard interchange and
the wider implementation work below remain incomplete; this document does
not propose another layout profile.

## Contract owners and current support

The [data-layout contract](../specs/data-layout-and-abi.md), registered
[source schemas](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas)
and [call interface](../specs/targets/windows-x64-call-interface.md) own the
accepted semantics. Core primitives are Blittable by their type contract.
An aggregate requires its explicit, per-target commitment and transitive
field eligibility; Sequential or equal machine size alone is insufficient.
Blittable and Copy are independent facts.

The [layout inventory](../implementation/layout-profiles.md) owns current
source, storage and private-module support. In particular, `[Blittable]` is
recognized; source support is no longer merely a test-only evaluator or
bound-IR marker. Private modules persist the commitment, including its
combination with private span metadata. Their revision numbers and retained
readers are not standard TRRM assignments or compatibility requirements.
Address-based native input storage and by-value executable carriers have
different limits; neither limit defines the target ABI.

## Remaining integration

| Surface | Required work |
|---|---|
| Standard TRRM | Carry the explicit target commitment, complete field types, layout kind, packing and actual explicit alignment; consumers recompute offsets, size, alignment and stride before use |
| Wider source layout | Adopt accepted Packing/Alignment controls and Union only with their validity, access and lowering rules; do not infer support from attribute parsing |
| Native boundary | Classify complete values under Windows x64, including indirect aggregate shadow copies and result storage; do not impose a proposal-specific 16-byte ABI ceiling |
| Replacement | Retain promised layout, nominal identity, field access/mutation, caller-unsafety and every stronger physical fact actually used by a consumer |
| Source/tooling selection | Keep the actual implemented language version and diagnostic behavior in the support inventory; any further adoption must select its concrete compiler boundary |

The [native metadata proposal](trrm-native-interop-metadata.md#explicit-fixed-representation)
recommends a physical commitment carrier. Its rootless Copy slice does not
replace the general accepted Blittable contract or admit owners by relabeling
them. Wider value categories need their own complete validity, ownership and
representation facts.

## Minimal versus shared integration

A minimal scalar/pointer Sequential codec is useful for a first declaration
library, but should not become a permanent layout system with a second target
identity. Recommend the existing target/layout owner plus an explicit
commitment, extending its admitted value matrix in coherent slices.
Packing, Union and over-alignment are accepted design requiring implementation,
not permission to invent a competing schema or weaken target rules.
Unpublished private revisions need not be preserved.

## Conformance gates

Compare independently authored producer/consumer layouts with an independent
Windows x64 C fixture: every storable scalar, mixed fields, interior/tail
padding, nesting, opaque pointers, larger and explicitly aligned aggregates.
Check transitive missing commitments, forged offsets/widths/sizes, storage
cycles, unsupported targets, invalid attribute arguments and incompatible
replacement. Test procedure-call classification separately from storage
calculation, including register/stack positions and indirect shadow copies.

Existing `PublicFixedLayoutStorageTests`, `SequentialLayoutTests`,
`TransparentLayoutTests` and the test-only candidate evaluator cover different
surfaces; their existence is not evidence that all gates ran or that standard
TRRM layout persistence exists. Remaining compiler work is tracked in the
[conformance inventory](../implementation/conformance-gaps.md).
