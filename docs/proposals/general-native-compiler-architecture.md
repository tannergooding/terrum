# General-purpose native compiler architecture

**Status: UNAPPROVED recommendation.** This proposal addresses where native
interop behavior belongs and how to remove application-specific behavior from
the compiler. It does not accept or revise the canonical Windows x64 ABI,
layout, lifetime, native-FFI, callback, COM, or ownership contracts. Existing
DX11 support remains provisional until those contracts are separately
reviewed and accepted.

## Problem

The Windows x64 DX11 sample was made to build and run by adding application
knowledge to `Terrum.CodeAnalysis`, rather than by implementing reusable
language and target mechanisms. That knowledge currently includes:

- source-hash dispatch and source projections for exact DX11 stages;
- `PrimitiveType`, `BoundIntrinsic`, and bound-expression cases for
  sample-specific fixed-array carriers, SDK descriptors, DX11 resources,
  rendering operations, and sample evidence;
- DX11-specific COM interface, owner, method, and output contracts enforced
  by the binder, verifier, and emitter;
- emitter code that constructs an application render/resize loop instead of
  lowering the source program's ordinary calls and control flow.

The result couples a general compiler to one sample's API, types, layouts, and
application protocol. Renaming these cases or marking them provisional does
not remove that coupling. Conversely, placing the implementation in a C
application bridge would avoid compiler changes but would no longer exercise
the intended Terrum-authored integration.

## Recommendation

Keep the compiler responsible for source semantics, generic native
representations, target ABI lowering, and validation of selected native
metadata. Keep API declarations, SDK layout data, COM interface descriptions,
and application policy in a separate interop library/profile and in Terrum
source. The compiler should consume those descriptions through generic
records and lowering rules; it should not branch on DX11/Vertex names, exact
sample source, or a rendering protocol.

The recommended boundary is:

| Concern | Owner |
|---|---|
| Source binding, checked places, aggregate initialization, bounds, loans, and cleanup | Terrum language/compiler |
| Windows x64 scalar and aggregate classification, stack/register placement, indirect calls, and PE imports | Windows x64 target backend |
| Selected import identities, native layouts, callback signatures, COM slots, effects, and ownership mappings | Versioned native interop profile data |
| Win32/DXGI/D3D declarations, typed wrappers, resource helpers, and shutdown ordering | Interop library and Terrum sample source |
| Visual behavior and sample policy (message handling, resize, rendering, shader paths) | Terrum sample source |
| Independent ABI/platform instrumentation | Tests and native probes |

Interop profile data remains closed and compiler-selected in the first
implementation; this proposal does not recommend arbitrary source-selected
DLLs, paths, plugins, or caller-created catalogs. A profile record selects
authority and ABI facts, but is not proof that a foreign provider conforms.
The existing library/source declarations and implementation evidence must
remain distinct from that profile and from accepted contracts.

## Migration

1. **Extract reusable catalog and ABI rules.** Model COM interfaces,
   inheritance, method slots, signatures, effects, parameter adaptations,
   owner cells, and acquisitions as validated generic metadata. Implement
   catalog validation and target call lowering over that metadata. Add
   independent synthetic catalog tests; the DX11 catalog must be one data
   instance, not the definition of the compiler's COM rules. Before generic
   catalog data can authorize lowering, validate signature arity against ABI
   locations, bind slots to the declaring interface's own methods, require
   each owner acquisition to match an in-range pointer-sized output producer
   and interface, and restrict pointer adaptations to explicitly permitted
   direct base edges. Validate ABI locations against explicit signature value
   classifications, and require COM receivers to resolve to the owner interface
   or one of its declared ancestors, including explicitly recorded aliases.
   Add negative tests for forged producer, slot, signature, ABI, and adaptation
   metadata; nonempty descriptive strings are not sufficient evidence.
2. **Generalize native storage and loans.** Use the selected target layout
   rules and ordinary nominal Blittable/Sequential declarations for fixed
   representations. Represent inline arrays, descriptor storage, opaque
   handles, and bounded readonly/writable address loans through generic
   bound forms and catalog-declared parameter contracts. Preserve
   initialization, lifetime, extent, and non-escape checks. Do not infer
   validity, ownership, zero-initializability, or a pointer extent from field
   shape alone.
3. **Generalize callbacks and calls.** Lower admitted plain `fnptr` values and
   indirect Windows x64 calls from checked signatures and call metadata.
   Keep target lifetime, `NoThrow`, synchronous/no-retention, and any
   registration lifetime as explicit independent obligations. Do not infer a
   callback ABI from a delegate or arbitrary address.
4. **Move API-specific declarations out of the compiler.** Place SDK
   identities, signatures, layouts, COM inheritance/slots, ownership
   relationships, and callable declarations in the selected native interop
   profile/library. Express wrappers and ownership protocol in Terrum source.
   Keep the compiler's catalog reader, validator, verifier, and lowerer
   independent of those names.
5. **Remove sample authorization and generated application behavior.**
   Delete exact-source hash dispatch, sample-specific proofs and intrinsic
   cases, and the custom renderer/message-loop emitter. The DX11 entry point
   must bind and lower from its source through ordinary declarations, calls,
   loops, and cleanup. It must not require an authored C application bridge.
6. **Preserve evidence without promoting it to a contract.** Keep independent
   C probes for ABI/layout and real-device/WARP integration tests for the
   sample. Add unrelated synthetic APIs to generic compiler tests. Record
   target/environment prerequisites and visually observed behavior
   separately; neither evidence nor provisional implementation accepts a
   source or target contract.

## Provisional implementation evidence

The first implementation slice now separates generic COM metadata records and
validation from the DX11 catalog data. It validates declared versus inherited
slots, output producers and interface/effect identities, pointer-sized output
addresses, signature-bounded output/adaptation indexes, ABI locations against
classified signature widths including Windows x64 floating-point register
classes, COM receiver ancestry and explicit SDK aliases, and direct permitted
pointer adaptations. Synthetic unrelated-interface tests
cover positive cases and malformed producer, slot, signature, ABI, and
adaptation metadata. Bounded tree walks and call rewriting use
expression-declared child traversal, so the shared helper does not switch on
DX11/sample-specific node names. The compiler also emits signature-verified
generic COM vtable-call stubs, including explicitly
catalogued owner loans and Windows x64 integer, floating-point, and stack
argument placement; an unrelated synthetic catalog exercises that lowering.
Generic stubs also stage catalogued owned-interface outputs, convert signed
HRESULT success into conditional output initialization, and release a failed
call's output only when the catalog marks it as potentially written on failure;
synthetic coverage validates the output contract and emitted call shape.
Catalogued COM owners also carry a validated release-method identity, and the
shared `NativeComOwnerClose` verifies and emits release calls for the WARP,
buffer, shader, and presentation owners. Their generated cleanup no longer
depends on DX11-specific close intrinsics.
Generic vtable calls require receiver and borrowed-owner arguments to retain
their loan representation and acquisition provenance; acquisition outputs
and specialized wrapper intrinsics keep their distinct contracts. Catalog
entries sharing a declaring-interface method identity must agree on return
and parameter ABI classifications, effects, output identity, and borrowed
owner indexes while allowing receiver specialization and signature aliases.
COM owner-cell alignment is read from the selected catalog rather than from
API-specific type names. Bounded tree traversal includes nested calls in
composite expressions through their shared child hooks. Bounded local-storage
addresses now share one bound form carrying carrier, element, count, stride,
byte extent, and read-only facts; common lowering performs the checked
index/offset calculation, while descriptor dependencies use a generic
storage-loan record. The former Vertex3-specific storage path and admitted
inline-array initializers/reads now share a metadata-bearing fixed-array
layout and bound representation; stack allocation and element lowering
consume its count, stride, and extent; indexed binding uses the shared layout,
and verification rechecks element type, count, stride, extent, and alignment
against the selected catalog. DX11 fixture shapes, shader path buffers, and
descriptor authorization/import restrictions remain closed; readonly
descriptor backing uses catalogued fixed-array layout metadata and the generic
`NativeStorageAddress` representation. The metadata records element identity,
count, stride, alignment, and byte extent; verification also checks the
aggregate's sequential field layout and complete initialization. Direct
native-import storage borrowing continues to use per-parameter metadata for
address type, carrier, extent, readonly access, and required start index;
shared verification consumes that declaration rather than a carrier-specific
parameter-contract branch.
Callback signatures and lifetimes are attached to individual import
parameters, and shared verification matches callback targets against the
selected parameter's signature. The generic `Terrum.Interop.NativeCallback`
name and profile-specific callback type names are injected from selected
catalog metadata; the Windows catalog supplies `WNDPROC` with its exact
signature. Synthetic tests exercise distinct signatures through the same
generic source type and reject cross-signature use. The test-only
retained-callback lifetime gate remains closed and separate. Fixed-array
storage imports remain separately closed to their exact reviewed carriers.
The direct pipeline's `GetBuffer`, render-target creation, and `Present`
declarations pass generic catalogued-call checks, while the sample verifier
retains their closed shapes without rejecting those names alone. Specialized
COM wrappers are validated against catalogued methods and live owner
acquisitions without annotating sealed source calls; shader-owner cleanup uses
the generic close contract without DX11-specific release metadata. COM owner
cloning now has a generic intrinsic that requires an explicitly catalogued
reference-acquisition method, records a distinct owner acquisition, and emits
the selected vtable dispatch; a synthetic `IFixture` catalog tests it without
the DX11 profile. Generic COM calls now also lower catalogued hidden scalar and
null-pointer arguments, single-owner pointer arrays, and addresses of readonly
scalar values. Synthetic `IFixture` coverage exercises pointer-array/value-
address lowering independently of DX11. The direct pipeline now represents
render-target binding, vertex-buffer binding, shader binding, input-layout and
topology binding, drawing, and context clearing as generic catalogued vtable
calls; their former DX11-specific intrinsics and direct argument marshalling
have been removed. Focused COM-catalog, provenance, and direct-pipeline tests
passed (66 passed, 1 skipped).

Catalogued fixed scalar-array inputs now also have a generic address
adaptation: source scalar parameters are staged contiguously before a native
call. The direct presentation `ClearRenderTargetView` operation uses this
generic COM path instead of a DX11-specific intrinsic and emitter, and an
unrelated synthetic interface exercises the same adaptation.

The Win32 `GetClientRect` import declaration and its parameter-contract
validation now belong to the Windows client-rectangle contract rather than the
DX11 presentation contract. The compiler admits catalogued address loans of
plain scalar-only aggregate locals under both runtime policies, checking exact
size/alignment, full initialization, mutability, and no escape; synthetic
unrelated-import tests cover writable and readonly loans, mismatch rejection,
non-escape, and checked-policy emission. The WARP, hardware, and resize sample
sources declare `ClientRectangle`; the direct WARP and hardware pipeline
sources call `GetClientRect` through this generic address type and calculate
zero-origin dimensions in Terrum. The direct pipeline and the source-authored
resize/render loop use catalogued COM array adaptations and catalog-driven
direct method wrappers; the loop's `ResizeBuffers` invocation is now an
ordinary bound COM call. The WARP and hardware entry points bind and emit their
authored message coalescing, extent queries, resize, redraw, and render logic
through ordinary source lowering, without resize-proof nodes or a specialized
loop emitter.
An opt-in hidden WARP runtime test exercises initial rendering, resized
extents, and minimize/restore through that ordinary source path.

The generic native-call lowering also now classifies
`NativeStorageAddress` as a 64-bit value. Omitting it from the wide-value set
had truncated the aggregate address passed to `GetClientRect` on x64. Hidden
WARP runtime coverage reproduced and now verifies the full-width address.
The hidden texture/viewport proof and custom resize-render lowering were
subsequently retired. Current resize, extent, viewport and mapped-pixel
behavior is authored in the WARP/hardware samples. This implementation
evidence does not approve this proposal or any underlying FFI, COM, or
ownership contract.

The focused resize-render, resize-queue, resize-evidence, and direct-pipeline
regression slice passed (116 passed, 2 environment-dependent skips).

The standalone resize-queue fixture no longer uses an exact-source proof or a
custom machine-code message pump. Its `Run` method now authors the `ShowWindow`,
`GetMessageW`, `TranslateMessage`, and `DispatchMessageW` loop directly, and
ordinary binding and emission consume the reviewed Windows import declarations;
the former proof node, source projection, binder route, and queue-specific
emission dispatch are removed. Focused source-control-flow and import-emission
tests passed (4 passed, 0 failed). The active WARP/hardware renderer has since
moved its coalescing, extent-query, `ResizeBuffers`, and render behavior into
source; the retired hidden evidence path no longer requires custom lowering.
The combined source-loop/import and resize-render
regression filter passed (7 passed, 0 failed).

The reviewed Windows `MSG` carrier now has on-demand source accessors for its
message identifier and `wParam`. They accept only the local opaque message
address, are emitted as ordinary compiler functions only when referenced, and
are verified against the selected message-loop catalog. The same no-escape
carrier can now be passed to its catalogued message APIs from ordinary source,
without requiring the DX11 owner proof. Synthetic ordinary source tests bind
and emit both accesses and `PeekMessageW`. Catalogued scalar COM method wrappers
are also now requested from source-invoked member names, avoiding unused
function-index changes; an unrelated `IFixture::Resize` catalog exercises the
wrapper factory. The active WARP and hardware renderers now author message
coalescing, extent queries, `ResizeBuffers`, redraw, and rendering in Terrum and
use generic COM lowering. The hidden texture/viewport proof projection and
resize-render emitter have been removed.

Generic COM calls also adapt checked non-unique scalar-only aggregate values
to catalogued const-pointer parameters when the recorded storage size and
alignment match the bound layout. An unrelated synthetic interface exercises
the pointer staging and rejects mismatched or unique aggregate layouts; this
uses only the aggregate sizes admitted by the existing value-layout limit.

The focused fixed-array storage slice now also covers synthetic native imports
whose catalogs declare readonly `NativeStorageAddress` parameters for
initialized inline-array locals. Binding and verification accept only the
catalogued element type and layout geometry with a constant zero start, and
reject metadata mismatches, nonzero starts, and dynamic indexes. The former
per-shape carrier identities and both sample-array readonly-address identities
have been removed. DX11 descriptor loans now use the generic
`NativeStorageAddress` representation while retaining their selected catalog
and layout checks. The combined
fixed-array, sample-array, DX11 buffer and CreateBuffer regression filter
passed (317 passed, 0 failed, 0 skipped), including an unrelated sequential
aggregate authorized by root layout metadata and rejection of recursive
aggregate layouts before alignment traversal. The broader DX11 filter passed
(362 passed, 0 failed, 16 WARP/hardware runtime cases skipped). The fixed-array
storage import remains a closed test fixture and does not establish arbitrary
inline-array storage support.

This is implementation evidence, not contract acceptance. Generic COM
metadata and mapped-buffer byte reads are in use, and the WARP/hardware
samples author their resize loop, extent and viewport observations, and pixel
validation. The hidden texture/viewport projections and custom resize-render
loop/emitter have been retired. Shared DX11/sample-specific binding,
intrinsics, verification and policy gates remain under cleanup, and unrelated
synthetic coverage plus the final acceptance audit are still required. The
architecture acceptance criteria are not yet met; this proposal and canonical
contract status remain unchanged and **UNAPPROVED**.

The direct-buffer wrapper no longer carries duplicate DX11 slot, signature, and
ABI metadata; its catalogued COM method is authoritative. Its compiler
intrinsic remains because generic COM lowering cannot yet adapt the
pointer-containing `D3D11_SUBRESOURCE_DATA` descriptor as a catalogued
readonly, no-escape loan. The existing aggregate-storage adaptation is limited
to plain-value aggregates, so retiring this intrinsic requires a separately
validated generic descriptor-address representation.

Catalogued fixed-array storage can optionally pin ordered element-field names,
types, offsets, and sizes. Binding and verification compare that metadata to
the selected source layout, while the synthetic `Vertex3` ABI is now an
ordinary test catalog entry rather than a compiler-reserved import contract.
The sample-array-only source gate has also been replaced by generic
catalogued layouts, including optional field access and mutability constraints.
The direct-pipeline vertex layout uses the same metadata; its duplicate
sample-specific shape predicate is retired. The latest fast build passed, the
sample-array/DX11 test filter passed 534 tests with 16 hardware/WARP skips, and
the independent generic mechanism filter passed 152 tests. DX11-specific
descriptor, acquisition, shader-path, and source-profile gates remain, so the
acceptance criteria are still unmet and this proposal remains **UNAPPROVED**.

## Acceptance criteria

- Shared compiler binding, verification, bound IR, and emission contain no
  DX11-, Vertex3-, sample-protocol-, or exact-source-specific behavior.
- DX11 and sample names appear only in their interop profile/library, sample,
  tests, and proposal/evidence documentation.
- No compiler-emitted replacement for source-authored application control
  flow remains.
- An unrelated synthetic native API exercises each generalized mechanism
  independently of the DX11 profile.
- The DX11 WARP and hardware sources build through the same generic compiler
  path and retain their current intended runtime behavior without a custom C
  application bridge.
- Canonical contract status is unchanged until an explicit contract decision
  is recorded in its governing proposal/specification.
