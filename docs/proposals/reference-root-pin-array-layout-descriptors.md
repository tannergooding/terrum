# RootPin FixedArray descriptor amendment

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract direction.** This remains a separate fixed-array
descriptor decision for
[RootPin loaded-type admission](reference-root-pin-loaded-type-descriptors.md).
The loaded-type proposal is the sole complete prior loaded-types/2 candidate
owner; this file records the array amendment and its independent approval
boundary.

The [integrated GC runtime proposal](integrated-gc-runtime-boundary.md)
supersedes the separate-provider and frozen runtime-info/table dependencies
below, not the typed FixedArray validation/traversal analysis. Exact V2 records
remain the prior candidate; they are not automatically the common runtime ABI.

## Decision and exact replacement

Select one closed **FixedArray Value shape 9** in the supplementary
RootPin loaded-type descriptor interface. It carries one exact element
Value descriptor, fixed u64 count N and checked byte stride S; its complete
size/alignment are derived from E and the selected Windows x64 layout.
Elements are indexed subplaces, not FieldDefs.

The amendment replaces prospective
`windows-x64-root-pin-loaded-types/1` with the exact successor
**`windows-x64-root-pin-loaded-types/2`**. The base loaded-type proposal now
contains the complete V2 batch/node records, shape rules, fixed storage
formula, zero/alignment behavior, validation, traversal and acceptance cases.
Use that one V2 definition; do not retain a second full V1 ABI, accept both
revisions, or infer a decoder from equal operation-table bytes.

The V2 descriptor is `TypeNodeV2` **96 bytes**, with u64 elementCount and
elementStride at offsets 80 and 88; all nodes use that stride. The existing
RootPin operation table remains **104 bytes/revision 1/twelve entries**,
`RootPinRuntimeInfoV1` remains **112 bytes**, and
`TerrumRootPinGetRuntimeV1` is unchanged. Those extents are not FixedArray
descriptor records.

## Preserved boundaries

The shape changes no RootPin operation/status, cell envelope,
PlaceRequest/SupportSlot, allocation/construction boundary, root/pin
ownership, cleanup, origin lease, code lifetime or deployment policy. It
adds no source spelling, Core registration, TypeSpec form, TRRM section,
Feature, Body kind, private revision or Standard profile composition.

FixedArray descriptors preserve complete nominal element identity, checked
Windows x64 `nuint` geometry, actual packed/aligned containing placement,
nested dimensions and atomic descriptor admission. N=0 still validates E;
S=0 is only the existing zero-data unit case, with alignment 1 and no
element residence/address. Shape 9 does not manufacture Copy, allocation,
initialization, cleanup, a runtime length or backing. Dynamic arrays,
capacity-bearing collections and external managed storage remain excluded.

Loaded-method admission, allocation/construction and deployment remain
separate transactions and policies. In particular, the 104-byte helper table
and 112-byte RuntimeInfo are unchanged, but their existence does not imply
FixedArray descriptor support or allow a V1 fallback. A RootPin descriptor
does not select F/G, NativeAddress, A2, a private format or a source array
API.

Approval, implementation and runtime qualification remain outstanding. The
base proposal's [V2 acceptance cases](reference-root-pin-loaded-type-descriptors.md#focused-acceptance-cases)
are the complete conformance owner; this amendment does not duplicate its
schema or claim tests, ABI probes or execution evidence.
