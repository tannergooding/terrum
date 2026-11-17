# Bounded layout and replacement directions

This informative inventory preserves existing bounded implementation
directions and support observations. It does not narrow the selected
[Windows x64 layout](../specs/targets/windows-x64-data-layout.md),
[call interface](../specs/targets/windows-x64-call-interface.md) or
[binding and replacement](../specs/representation/binding-and-replacement.md)
contracts. A private target-requirement number is not a standard Feature ID.

## Registered source layout and public fixed-layout profile

Source `[Layout]`, `[Blittable]` and `LayoutKind` roles are recognized only
through an explicit host-selected `LayoutCoreRegistrationSet` for the using
Module. The carrier is implemented provisionally under the unapproved
[source-layout registration proposal](../proposals/source-layout-core-registration.md);
same-named source declarations or native Core registration do not supply a
default selection. The compiler-owned BID-decimal helper source supplies its
own exact in-process Layout registration; that implementation detail does not
select a default for user compilation.

The selected target implements `Auto`, `Sequential`, raw `Union` and one-field
scalar-or-raw-pointer `Transparent` layouts. Sequential/Union Packing accepts
1/2/4/8/16 and defaults to 16; optional Alignment accepts powers of two from
1 through 8192 and cannot weaken derived field alignment. Binding and mandatory
verification derive field offsets, extent, natural/completed alignment and
transitive required alignment. Nested explicit alignment survives Packing and
fixed-array stride/extent calculations use the completed element layout.
Raw `Union` alternatives require rootless Copy storage; typed alternative
access requires an explicit unsafe scope, and no active-member tracker is
provided. Raw pointer fields preserve address bits only; they do not describe
or extend the referent's lifetime. Unique fields remain restricted to unique
owners. The existing Windows x64 target requirement `1` remains the
Blittable commitment, not a separate layout-profile identity. Externally
visible public fields require this commitment; a public field on an internal
struct does not independently expose its layout for replacement.

Private semantic revision **104** serializes Packing, optional declared
Alignment, and the recomputed Alignment/RequiredAlignment facts alongside its
fixed-array data. Its unapproved wide-layout candidate is implemented
provisionally: `BoundStruct.Size` and struct-field offsets/sizes use u64 in the
private record, while managed-class geometry remains i32. The reader
recomputes and compares full-width facts. Standard `Layout` rows preserve
normalized Packing and declared Alignment, then rederive geometry on load;
these metadata paths do not establish executable L1 Body support.
The unapproved
[L1 aggregate-layout proposal](../proposals/standard-trrm-local-aggregate-layout-execution.md)
is provisionally implemented through local constructors, field/array places,
calls/returns, source-free reconstruction, bounded provider closure and native
lowering. The bounded slice includes nested field/array places and conditional
writes. Full remapping/re-emission, generic/receiver-bearing providers and
overall L1 qualification remain open. Bodyless MethodDef provider binding
preserves the assigned implicit-readonly-reference Param marker through
provider comparison and the selected CallSite route. A2/L1 metadata retains
wide positive-stride extents and UInt64 indices; this does not allocate or
execute large arrays.

Native x64 frames account for object alignment, dynamic stack alignment,
probing and recoverable unwind state. Standard L1 runtime support is bounded
to Alignment=32. The unapproved
[Layout CLI manifest proposal](../proposals/source-layout-cli-registration.md)
defines the direct/Standard source commands and the separately unapproved
[private-module extension](../proposals/private-module-source-layout-registration.md).
The latter admits only `compile-module producer`/`consumer` source binding;
the separate scanner extension admits `scan-component`/`build-component`
only for scanner-source compilation, not subject-source compilation.
`compile-native`, native set/graph, source-free and syntax-only routes still
reject the option. The manifest configures only the explicit Layout/Blittable
providers; it does not select native Core schemas or persist host
authorization/context in `.trm`.

Direct native `NativeParameter` inputs may use a committed aggregate pointee.
The address-based storage limit is 128 bytes, independent of the 16-byte
executable value profile. The pointer contract remains one element,
`NoEscape`, and backed by an initialized local; verification checks that all
aggregate fields are initialized. Private semantic metadata revision 80
preserves and remaps the pointee type. The canonical Standard `NativeParameter`
encoding remains unapproved; bounded Development and F/F+S experiments do not
establish general register-Body support. Direct `NativeOutput` has a separate
result-correlated vacant-cell profile for scalar, raw-pointer and eligible
Blittable Copy values, tracked in the
[native integration inventory](conformance-gaps.md#cg-004).
The separate unapproved F+S Standard route currently addresses initialized
scalar local Homes. Its unapproved A2 composition now carries TypeSpec
fixed-array identity, Module-owned layout rows, Feature `20/1`, TypeDef
ordinary-Copy evidence, and structural scalar/local
Sequential/Union/Transparent layout validation, including packed and
over-aligned calculations. A2 Body
maps validate local Home/Register selections and counted EC RegisterPlace
shapes, including nested array paths. A direct selected-provider path now
binds imported Copy TypeDefs by ModuleRef identity and ExportKey, compares
their fields, Layout variants and derived geometry, and retains the binding
through the Standard native plan. The exact A2 `EmitStandard` route now emits
declaration-only local Blittable Sequential/Union/Transparent TypeDefs with
positive Copy evidence for checked ordinary types and Feature `2/1` closure. Array
TypeSpec `0x11`/`0x0012` and Feature `20/1` rows are emitted for scalar, unit
and nested array fields in these declarations; array Body production remains
open. A2 TypeDef Exports are emitted only for explicit source declaration/key
mappings; no export key is derived from a type name. The source-free
NativeAddress reader now
reconstructs local A2 array type definitions for scalar, unit, direct-pointer,
function-pointer and nested-array element cases, validating target-derived
geometry before exposing bound types. A separate type-only branch reconstructs
local non-generic DeclaredCopy TypeDefs and scalar/nested-array field types, including acyclic arrays of
local nominal Copy elements and raw `Union` layouts after their dependencies
are reconstructed. The selected union metadata/source-free and private
round-trip cases are covered; whole-Union initialization and executable
union-array values remain under the unapproved, provisionally actionable
[union initialization proposal](../proposals/standard-trrm-raw-union-initialization.md).
The source-reference path selects explicit nonempty TypeDef ExportKeys from
one local type-only A2 Module, preserves the provider snapshot and each
selected/closure TypeDef origin, and exposes only selected roots to ordinary
name lookup. Selected type-only Standard writers preserve these origins;
private writers reject unsupported projections rather than discarding them.
Separately supplied A2 providers bind through their checked
Copy/layout contracts, and imported closure types retain actual provider
origins plus original consumer Import/ModuleRef expectations. Direct-field and
array imports, compatible TypeDef relocation, incompatible packing and
snapshot immutability are covered. Provider-owned TypeDef Import chains also
project through separately supplied or co-located A2 type-only Modules while
retaining both provider and consumer expectations. Selected alias keys and
distinct expected views preserve their original expectations while binding
to the same actual TypeDef. Type-only re-encoding covers relocated provider
rows and imported origin graphs; general replacement/remapping and executable
Body consumption remain open. [CG-004](conformance-gaps.md#cg-004) retains the
exact reference/re-emission gates.
The executable NativeAddress path still rejects nonempty TypeDef tables.
The type-only reconstruction/reference paths do not consume executable
PlaceSelection/EC Bodies. General provider-owned array replacement/remapping
and native-address execution of imported nominal-array elements remain open;
the selected local-array executable slices are tracked in
[CG-004](conformance-gaps.md#cg-004).
Private semantic-module revision 81 combined fixed-layout commitments with
span roles and is rejected before decoding. Current private writers retain
fixed-layout facts but reject compilation-local span roles.


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


## Bounded compact source layout

The implementation also retains a bounded nonpublic compact Copy path using
the selected `[Layout(LayoutKind.Sequential)]` schema, private fields and
eligible nested Copy fields. It does not publish fields, freeze representation
or permit independently replaceable layout boundaries. `ValueLayout` remains
limited to naturally aligned 1-16-byte aggregates; that compact value limit
does not cap the separate public Blittable storage/frame layout.

These are private compiler/development contracts, not restrictions on the
selected source or target ABI. Their unpublished forms require no legacy
mode, retained alpha artifact or compatibility migration. An actually
retained independently replaceable boundary still satisfies the selected
[binding and replacement contract](../specs/representation/binding-and-replacement.md).
