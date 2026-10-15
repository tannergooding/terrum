# Bounded layout and replacement directions

This informative inventory preserves existing bounded implementation
directions and support observations. It does not narrow the selected
[Windows x64 layout](../specs/targets/windows-x64-data-layout.md),
[call interface](../specs/targets/windows-x64-call-interface.md) or
[binding and replacement](../specs/representation/binding-and-replacement.md)
contracts. A private target-requirement number is not a standard Feature ID.

## Implemented public fixed-layout source profile

Source-authored `[Blittable]` commitments support natural Windows x64
`Sequential` layouts and one-field scalar `Transparent` layouts. Sequential
fields may use the fixed scalar/pointer set or transitively committed
structures; unique fields remain restricted to unique owners. The compiler
recomputes the layout under mandatory verification and uses the existing
Windows x64 target requirement `1`, not a separate layout-profile identity.
Externally visible public fields require this commitment; a public field on
an internal struct does not independently expose its layout for replacement.
Private semantic modules persist and revalidate these facts. Packing and
alignment controls, unions, opaque handle fields, and standard TRRM layout
persistence are not implemented.

Direct native `NativeParameter` inputs may use a committed aggregate pointee.
The pointer contract remains one element, `NoEscape`, and backed by an
initialized local; verification checks that all aggregate fields are
initialized. Private semantic metadata revision 80 preserves and remaps the
pointee type. Standard TRRM `NativeParameter` persistence remains rejected
pending an approved encoding, and direct native outputs remain limited to
scalar and raw-pointer pointees.
When fixed-layout commitments coexist with private span roles, semantic-module
revision 81 carries both fact sets without claiming standard TRRM support.


## Compact and resilient layout support

The currently implemented compact value/module layouts remain exact contracts,
not an already implemented resilient-layout mechanism.


## Semantic versus precompiled replacement

The current module paths illustrate the distinction:

- A persisted semantic consumer selects a compatible producer and both are
  lowered together. Its artifact retains checked source-level contracts;
  `run-module` generates native code for that launch, and `link-module` emits
  a fresh PE. This does not promise replacement of an already emitted static PE.
- A persisted native consumer calls an independently compiled DLL. Compatible
  replacement must preserve the physical crossing contract without rebuilding
  the consumer image.


## Managed exports

The current implementation does
not support public managed-handle exports; no new export surface or stable
managed ABI is implied here.


## Provisional compact source layout

The implementation's provisional `[Layout(Sequential)]` spelling admits nonpublic compact
Copy structs with private fields and eligible nested Copy fields, using the
existing natural declaration-order layout (1-16 bytes). It does not publish
fields, freeze representation, or permit independently replaceable layout
boundaries; no semantic wire field or ABI migration is implied. This bounded
compact form is distinct from the public `[Blittable]` fixed-storage contract.
`ValueLayout` remains limited to naturally aligned 1-16-byte aggregates.

These are private compiler/development contracts, not restrictions on the
selected source or target ABI. Their unpublished forms require no legacy
mode, retained alpha artifact or compatibility migration. An actually
retained independently replaceable boundary still satisfies the selected
[binding and replacement contract](../specs/representation/binding-and-replacement.md).
