# Native generalization support boundary

This informative inventory separates the completed removal of API-specific
compiler policy from remaining generic native/interchange work. Current
support is recorded in the [implementation summary](../implementation.md)
and [native profile](native.md); pending standard records are in the
[native metadata proposal](../proposals/trrm-native-interop-metadata.md).

## Current boundary

The compiler sources contain no DX11 API catalog, sample/window-owner
recognizer, COM ownership/acquisition rule or synthesized renderer.
Maintained [sample entry points](../../samples/Win32Dx11Triangle/README.md)
author imports, SDK layouts, numeric-slot calls, callbacks, message processing
and resource cleanup. The compiler mechanisms are generic imports, pointers,
function pointers, fixed layouts, vtable ABI lowering and ordinary
unique/Cleanup behavior.

Raw boundaries remain caller-unsafe, including scalar-only signatures.
Annotations express authored provider promises; they do not prove provider
behavior, establish a fixed IID from arbitrary void**, or confer an owner.
Known backing, access, output correlation, loan and cleanup obligations remain
checked. Retiring compiler policy does not retire those obligations.

## Remaining gates and evidence

Standard TRRM native records, source spelling and several executable
compositions remain unapproved or bounded experiments. Private revisions and
successful source-authored samples do not establish standard interchange.
Use the [compiler conformance inventory](conformance-gaps.md) for those gaps,
their approval dependencies and acceptance cases.

The active suite and explicit test-project exclusions define coverage.
Historical DX11/COM prototype tests depend on retired contracts and are not
active acceptance evidence. Maintained-entry-point emission, generic ABI
probes, WARP runtime, hardware runtime, resize/paint behavior and pixel
observations are separate claims; none implies all of the others. Retain
final compiler/input/configuration identity with actual qualification evidence.
