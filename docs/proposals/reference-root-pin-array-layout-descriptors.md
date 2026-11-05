# RootPin FixedArray descriptor amendment

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract direction.** This remains a separate FixedArray
descriptor decision for
[RootPin loaded-type admission](reference-root-pin-loaded-type-descriptors.md).
The loaded-type proposal is the sole complete prior loaded-types/2 candidate
owner; the integrated GC proposal may supersede its provider packaging, not
silently approve the descriptor contract.

## Decision and supersession

Use one exact supplementary loaded-types successor,
`windows-x64-root-pin-loaded-types/2`, with the complete records, validation,
traversal and acceptance cases in the loaded-type owner. It replaces the
prospective `/1` candidate; do not retain a second full V1 ABI, accept both
revisions, or infer a decoder from equal operation-table bytes.

The FixedArray Value shape 9 carries one exact element Value descriptor E,
u64 element count N and checked byte stride S. Preserve the owner’s complete
geometry and identity obligations: derive alignment `A=max(1,a(E))`, stride
`S=checkedRoundUp(s(E),A)` and size `N*S`; validate E even for N=0; permit
S=0 only for unit, with alignment 1 and no element residence/address/scan.
Preserve complete nominal element identity, nested dimensions and actual packed
containing alignment. Admission remains atomic, and descriptor/provider
origins and required code/lifetime obligations remain retained as specified by
the loaded-type owner.

`TypeNodeV2` is 96 bytes, with u64 elementCount and elementStride at offsets
80 and 88. This is distinct from the unchanged RootPin helper table
(104 bytes, revision 1, twelve entries) and `RootPinRuntimeInfoV1`
(112 bytes); neither extent is a FixedArray descriptor record.

## Preserved boundaries

This amendment adds no source spelling, Core registration, TypeSpec form,
TRRM section, Feature, Body kind, private revision or Standard profile
composition. Shape 9 supplies no Copy, allocation, initialization, cleanup,
runtime length or backing. Dynamic arrays, capacity-bearing collections and
external managed storage remain excluded.

Loaded-method admission, allocation/construction and deployment remain
separate decisions. A RootPin descriptor does not select F/G, NativeAddress,
A2, a private format or a source array API. The typed V2 acceptance cases in
the [loaded-type owner](reference-root-pin-loaded-type-descriptors.md#focused-acceptance-cases)
are the complete conformance owner. Approval, implementation and runtime
qualification remain outstanding; this amendment claims no tests, ABI probes
or execution evidence.
