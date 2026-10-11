# Bounded layout and replacement directions

This informative inventory preserves existing bounded implementation
directions and support observations. It does not narrow the selected
[Windows x64 layout](../specs/targets/windows-x64-data-layout.md),
[call interface](../specs/targets/windows-x64-call-interface.md) or
[binding and replacement](../specs/representation/binding-and-replacement.md)
contracts. A private target-requirement number is not a standard Feature ID.

## Initial public fixed-layout direction

For the first public Windows x64 fixed-layout slice, field eligibility uses
the existing target-specific `Blittable` requirements and ordinary `Copy` and
value-validity rules, not a slice-specific type whitelist. A nested struct
field must also satisfy that slice's `Sequential` requirement. This accepts
the eligibility and bounded implementation directions. The
[registered source schemas](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas)
are specified; physical metadata encoding and exact migration version/tooling
remain open, and no implementation support is implied.
The implementation direction reuses Windows x64 target requirement `1` and
introduces no separate layout-profile identity.


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
boundaries; no semantic wire field or ABI migration is implied.
`ValueLayout` remains limited to naturally aligned 1-16-byte aggregates.

These are private compiler/development contracts, not restrictions on the
selected source or target ABI. Their unpublished forms require no legacy
mode, retained alpha artifact or compatibility migration. An actually
retained independently replaceable boundary still satisfies the selected
[binding and replacement contract](../specs/representation/binding-and-replacement.md).
