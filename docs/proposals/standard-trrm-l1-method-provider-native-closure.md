# Standard TRRM L1 executable MethodDef-provider closure

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract direction.** Formal review, approval, normative adoption,
implementation and qualification remain separate. This document changes no
accepted specification or existing approval.

## Problem and recommendation

[L1](standard-trrm-local-aggregate-layout-execution.md#dependencies-exclusions-and-remaining-approval)
leaves executable MethodDef-provider closure to the provider-native owner's
checked plan. The
[provider-native owner](standard-trrm-provider-native-lowering.md#decision-and-scope)
selects an F NativeImport handoff, not a native-ready representation of
ordinary provider Bodies. A successfully reconstructed root alone does not
authorize dropping its Imports or selecting code by name.

Recommend an **identity-preserving semantic Module closure**, realized by a
distinct **L1 semantic-Methods branch of the existing checked native-operation
plan**, sharing its immutable artifact/Module preparation. Bind ordinary
body-bearing provider Methods, retain separate expected/actual identities and
re-emit their Modules without flattening ownership. Carry every original
expectation through joint JIT/PE lowering. The branch has no foreign-library/
export binding payload and requires neither NativeImport nor NativeAddress.

Initially admit module-level, nongeneric Methods without receivers, with
ordinary by-value parameters, complete value types already admitted by the L1
local-body subset, register Bodies and no MethodDef contract rows. This adds
provider resolution for that callable subset, not more type forms, declaration
roles, effects or call ABIs.

Use the existing public `TrRmExecutableCompiler.LoadStandard` and
`NativeModuleCompiler.EmitStandardExecutable`, `EmitStandardLibrary` and
`ExecuteStandard` argument/result contracts; add no overload, public native-ready
handle, resolver or CLI option. Native operations prepare their checked plan
from the submitted selections themselves, rather than loading a root and
discarding its binding context.

This is **semantic relowering into one fresh execution image**, not independent
precompiled-provider deployment, source recompilation or live replacement.
The F branch, its actual-NativeImport rule and its bounded admission remain
unchanged. Adding L1 to an F schema predicate and continuing through F's
NativeImport realization is not this recommendation.

Preserve exactly **`trrm/1-draft-local-aggregate-layout-execution-1`** and
`TrRmArtifactSchema.LocalAggregateLayoutDraft`, including L1's complete catalog,
`(1,0)` bytes, record shapes and use-dependent requirements. No wire field,
Feature, instruction, section, version, private revision, alias, dual decoder
or new schema identity is needed. This decision changes provider-operation
applicability and its in-memory handoff, not L1's encoding or local callable
subset.

## Evidence and gap classification

These are source/document observations, not newly executed test results.

| Inspected owner or implementation | Finding |
|---|---|
| [L1 provider deferral](standard-trrm-local-aggregate-layout-execution.md#dependencies-exclusions-and-remaining-approval) | Selects ordinary aggregate-local Bodies and requires preserving original/provider correspondences in re-emission, but leaves executable MethodDef-provider closure to a checked provider-native plan. It expressly excludes broadening F-only acceptance. |
| [Selected-provider consumption](standard-trrm-selected-provider-consumption.md#bounded-f-module-closure), [native handoff](standard-trrm-provider-native-lowering.md#checked-native-plan-handoff-and-origin) | Supply exact input transport, every-edge comparison and a checked-operation pattern. Their F actual is a public owned NativeImport, expressly not an equal-shaped authored Method. They do not select this L1 Body realization. |
| [`StandardConsumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), [`L1 reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.LocalAggregateLayoutReader.cs) | Explicitly reject L1 MethodDef Imports rather than routing them through NativeImport. The local reader reconstructs one Module's Bodies and resolves calls through its local MethodDef map. |
| [`TrRmStandardNativeBindingPlan`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs) | Native binding records require actual library/export strings. `CreateLoweringInputs` requires imported functions to carry DirectNativeImport and rejects a lowering with no direct NativeImport. Its existing type-provider support does not supply semantic method Bodies. |
| [`BoundProgram`/`BoundFunction`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`L1 writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.LocalAggregateLayoutBodyWriter.cs) | Type origins have retained provider snapshots/expectations, but there is no corresponding body-bearing semantic method closure carrier. The writer emits one Module and rejects MethodDef Imports. These are implementation prerequisites for the selected direction. |
| [`FixedArrayCopyWriter.CreateA2SourceTypeImportPlan`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.FixedArrayCopyWriter.cs) | Requires A2 schema identity on retained source-type origins. An L1 TypeDef-origin failure here is an implementation integration issue under L1's existing type-origin/re-emission obligations and the [Copy identity owner](standard-trrm-source-copy-type-identity.md), not evidence of a missing MethodDef contract. Do not remove the check without implementing exact L1 origin preservation. |

Existing metadata carriers suffice. The missing decision is the bounded
**semantic closure and checked execution-operation boundary** for body-bearing
Methods; missing compiler classes or a stale type-only schema guard do not
independently justify another wire design.

## Owners and exact proposed amendments

On approval, integrate the following applicability changes; none are made by
this proposal task:

| Owner | Proposed change or preserved responsibility |
|---|---|
| [L1](standard-trrm-local-aggregate-layout-execution.md) | Preserve its exact composition, constructor normalization, type/layout and re-emission obligations. Replace its executable MethodDef-provider deferral with a reference to this separate closure/handoff owner; do not restate it as F native binding. |
| [Provider-native lowering](standard-trrm-provider-native-lowering.md) | Add this separately selected L1 plan branch to the existing three operations. Its F scope, actual-NativeImport checks and foreign-code realization remain unchanged. The “at least one direct NativeImport” precondition belongs only to the existing native-import branch, never L1 semantic Methods. |
| [Selected-provider consumption](standard-trrm-selected-provider-consumption.md) | Reuse exact artifact/Module selection, snapshot, directory and every-original-edge obligations. Add an explicit reference to L1's separate operation applicability, without changing the bounded F Module shapes or admitting authored providers under F. No new source-reference projection is selected here. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [binding](../specs/representation/binding-and-replacement.md), [consumer checking](../specs/representation/reader-trust.md) | No amendment: existing ModuleRef/Import/Export, MethodDef/MethodSig/Param, Body maps and exact original/actual comparison carry this closure. |
| [Modules](../specs/modules.md), [JIT](../specs/jit.md), [deployment](../specs/targets/windows-x64-deployment.md) | No amendment: retain semantic-versus-precompiled consumption, actual origins, ABI, publication, replacement and lifetime obligations. |
| Shared bound program and native plan | Add the host-only immutable closure data and explicit L1 plan payload below. Preserve it through shared traversal/rewriting; do not serialize plan acceptance or create a second Body representation. |

No accepted text, numeric assignment or existing proposal's distinct schema
is superseded.

## Checked L1 preparation and binding

1. Preflight **every** supplied artifact as exact L1 before schema-dependent
   indexing, including unused providers. Select the root Module only in
   `input`. Use one unambiguous directory for all supplied Modules, including
   co-located Modules; duplicate identities reject even for equal bytes.
   The provider list is the complete additional input set, not a search
   catalog. An empty list is valid for a closed co-located closure.
2. Admit each supplied artifact and Module, its governing edition and complete
   retained requirements under L1's edition 1, Windows x64 target/profile and
   fail-fast selection (`targetRequirement=1`, `runtimePolicy=0`). Bind every
   retained Import in that supplied closure, including disconnected/uncalled
   expectations. Consume every ModuleRef through its retained supported edges
   or reject; choosing one called method cannot hide another obligation.
3. Resolve each method edge by its using Module's exact ModuleRef identity,
   requirement/policy and explicit Export key. The expected definition is a
   bodyless MethodDef. The actual must be a public, provider-owned,
   non-imported MethodDef with one supported register Body. A NativeImport,
   anchor, bodyless declaration, imported proxy or wrong-kind Export is not
   an implementation. No forwarding search or native symbol binding follows.
4. Apply the bounded callable subset above to both declarations and selected
   actual Bodies. Compare complete signatures in their owning contexts after exact
   TypeDef-provider binding: receiver absence, generic absence, ordered logical
   inputs, parameter modes/flags and complete result type. Retain the
   zero-MethodDef-contract bound; do not silently erase contracts or invent
   variance for unsupported forms. Call operands keep their original formal
   mapping and source-order captures; do not rebind labels/defaults to actual
   provider parameter names or reevaluate inputs. Unit inputs still occupy
   logical positions. Structural-array call signatures and every other
   independently unselected signature/ABI remain unselected.
5. A MethodMap `0x1C` token selects its exact Import row and expected MethodDef
   `0x16`. A direct `0x16` token naming an expected stub cannot bypass that
   stub's binding. Resolve locally implemented targets in their own Module;
   preserve any already-admitted direct-bound cross-Module correspondence
   after a checked rewrite. Never use a method name, Body row, dense function
   index or F's `RootTargetName` as cross-Module identity.
6. Allocate the complete declaration/origin directory before reconstructing
   calls and Bodies. Local recursion, chains, diamonds and finite cross-Module
   call cycles require no recursive artifact loading or forwarding. A
   self-edge may resolve only to a distinct public owned implementation, not
   its expected stub. Check every original comparison separately; an
   in-progress/visited Module is not successful evidence for an incoming edge.
7. Preserve each selected TypeDef/field/array origin and its own original
   expectations through signatures and provider-local Bodies. The existing
   Copy, validity, access, Layout/Packing/Alignment and geometry obligations
   apply independently. Layout equality or a root's local type number cannot
   substitute for a provider's nominal identity.

Use the existing complete binding relation, including retained equality
relationships and stronger provider facts actually used. Explicit distinct
Export keys already permitted by metadata do not create new method identities;
check each original Import and reject conflicting actual selections for one
expected declaration. This proposal assigns no key-generation convention.

Reconstruct and run the compiler's existing mandatory verification in each
Module's context and after meaning-changing rewrites. Extend shared verification
and traversal rather than disabling them to accept a provider. The
[consumer-trust owner](../specs/representation/reader-trust.md) still does not
require arbitrary semantic re-proof of provider Bodies or source authorization.
Structural/support/binding rejection and optional producer-truth audits are
different claims.

## Shared carrier and native-plan payload

Recommend one immutable semantic closure attached internally to the returned
root `BoundProgram`, shared with the checked L1 native-plan payload. The
following defines its required **logical records**, not serialized records or
a public type layout. Reuse existing checked snapshot/type-origin machinery.

| Record | Required data and identity |
|---|---|
| Artifact selection | Owned immutable bytes, exact L1 schema/catalog selection and artifact slot. Retained results do not depend on later caller-buffer mutation. |
| Module selection | Artifact slot, Module row, decoded identity, governing edition, retained capabilities/profiles, target/runtime, entry/no-entry state and owned declaration ranges/Exports. |
| Declaration correspondence | Exact artifact/Module/MethodDef identity to its reconstructed declaration and Body, with the original complete signature/contracts and type context. Expected stubs and actual implementations remain separate records. |
| Every method edge | Using artifact/Module, Import row, ModuleRef row, expected MethodDef, original key/requirements, selected Export row/key, actual artifact/Module/MethodDef and the checked type-provider correspondences. Key by the original edge/context, never a function name. |
| Retained semantic programs | Per-Module Bodies and their typed calls, Homes, maps, fields, arrays and original/actual type bindings, including uncalled dependencies. Keep private helpers in their actual owning Module. |
| Native correspondence | Each emitted implementation's semantic origin to its backend function/range and call fixups; each resolved imported call retains both its original edge and selected actual target. Actual machine code belongs to the regenerated image, not a fabricated provider DLL. |

The internal native plan has an explicit, mutually exclusive L1 semantic
payload rather than populating F native-binding records with placeholder
library/export strings. Share common capture/admission machinery, but dispatch
to the L1 closure checker before constructing this payload. A Boolean
“verified”, copied descriptor or bare import-free root is not the payload.

Native operations consume that same owned closure; they must not reread caller
bytes/files or reconstruct a different provider selection after checking.
Validate operation/root/entry/export correspondence and any changed semantic
inputs before generation. Meaning-changing transformations produce a newly
checked closure/plan; retaining an old plan beside changed functions is invalid.

The backend may use dense joint function/type indexes. This is physical
indexing, **not** copying provider declarations into root ownership. Calls
target the selected actual implementation while the carrier retains their
expected declarations and Imports. Reuse typed remappers for all supporting
facts. Do not clear `ModuleImport` or erase expected stubs as the binding
operation. Existing direct `NativeEmitter`/`NativeJit` APIs gain no provider
resolver or authority to consume an unresolved/context-losing loaded root.

Executable emission/execution retains the selected root's existing supported
local entry shape; this amendment does not add imported entries. Library
emission requires an explicitly no-entry root and only existing eligible
root-owned exports. An imported expectation or provider implementation does
not become a root DLL export; provider keys are not native export selectors.
Provider entries are retained metadata, not automatic activation. Required
unsupported initialization, cleanup scheduling or runtime facilities decline.

## Re-encoding, replacement and native lifetime

For this provider route, re-emit the retained closure as co-located Modules,
extending L1's retained-origin/re-emission obligations. Explicitly select the L1
destination and rebuild original Imports/Exports, declarations and Bodies,
keeping Module identities distinct. The requested root identity must select
the retained root; an output argument cannot restamp it or its providers.
Remap Module partitions/cursors, entries, ModuleRefs, expected/actual DefIds,
signatures/types, Layout/Field references, per-Module requirements, Body
maps/records/calls/Blocks and code extents consistently. Preserve optional
source/native origins or omit optional debug data legitimately. Clone shared
structural rows when different using-Module meanings require it.

Artifact slots and physical rows may change; nominal ownership and each
expected/actual edge may not. Rebind the re-emitted bytes under their explicit
selection before native use. No old plan is serialized as authorization, and
no F conversion, byte relabeling or private persistence fallback is supplied.

A newly selected compatible provider may change private implementation Bodies
or physical rows. Recheck every original expectation, type/layout dependency
and stronger fact actually relied on, then generate new code. This supplies
no replacement inside a prepared plan, unchanged-PE servicing, hot patching,
escaped-address retargeting or early unload.

Joint calls use L1's existing Windows x64 value/aggregate classification,
independent argument copies, aligned result storage and fail-fast behavior.
No independent provider ABI, implicit marshalling or exception bridge is added.
Publish all required code, fixups, instruction-cache state and registered unwind
backing before entry exposure. Retain the whole actually needed image/helper/
unwind and load backing through its last call, retained value/address or other
admitted dependency, even if an optimization removes a machine call.

Retire only after all such uses end; failed unwind deregistration retains its
backing. AOT DLL consumers retain their actual image while its admitted entries
remain usable. There is no new escaped-pointer lease or loader policy. L1's
terminal failure policy does not supply E1 events, checked unwinding or normal
Cleanup after termination.

## Failure and resource boundary

Capture, decoding, comparison and reconstruction execute no provider Body,
initializer, foreign-symbol lookup or native load. Complete admission/binding,
verification and operation support before machine generation or execution.
Emit methods activate nothing and return only complete PE bytes.

Reuse the existing API/CLI categories: argument/selector failures at preflight,
invalid data for malformed references or incompatible binding, and unsupported
operation/content for valid unimplemented forms. Preserve existing layout and
capacity failure distinctions; import no A3-only failure policy or new diagnostic
ID. Failures/cancellation publish no partial program, artifact, image or entry;
CLI output collisions include every provider, and sentinel outputs survive
preparation/emission failure. Do not retry another provider/schema/runtime or
return an empty successful closure.

Charge retained snapshots, comparison pairs, declarations, Bodies, mappings and
output buffers to the same root request under the
[resource owner](../specs/representation/resource-accounting.md).
Use checked format/count/extent arithmetic and existing declared capacities.
No per-provider budget reset, new graph-depth/provider-count cap, array-count
expansion or “visited means satisfied” shortcut is selected. Resource exhaustion
declines explicitly, without publishing partially checked cyclic closure.
Native preparation/publication failure follows existing owned-resource cleanup;
execution and platform-loader effects are not rolled back or sandboxed.

## Required acceptance cases

These are future gates, not support or execution claims. Extend the existing
[L1 consumer tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmLocalAggregateLayoutConsumerTests.cs)
and public Standard/native-operation tests with independently assembled inputs
and unrelated synthetic providers.

| Area | Positive, negative and boundary requirements |
|---|---|
| Real body-bearing provider | Load a root importing an ordinary provider Method by a key different from either method name, with no NativeImport, NativeAddress or signature anchor. Call it through JIT and a supervised emitted EXE. Exercise a separately authored no-entry root and actually invoke its eligible emitted DLL wrapper. Emission alone is not execution evidence. |
| Closure and ownership | Separate versus co-located providers; root/provider not first in their artifacts; chains, diamonds, local recursion, cross-Module call cycles and valid distinct-stub self-edges. Preserve same-spelled methods in different Modules and multiple explicit keys/original expectations without name-based coalescing. Provider entries never run automatically. |
| Every original obligation | Include an uncalled second Import, an invalid incoming edge in a diamond/cycle and an invalid disconnected supplied Module. Missing/duplicate Modules, wrong-case identities/keys, requirement mismatch and conflicting actual selections reject before generation. Checking one edge cannot satisfy another. |
| Actual role and supported signature | Reject proxy/forwarded, native, anchor, bodyless, private or wrong-kind actuals. Reject incompatible formal order/modes/result/nominal origin; valid unsupported receiver/generic/contract/native-call forms decline without erasure. Preserve once-only source-order captures and original formal mapping when expected/actual parameter names differ. Equal ABI/layout and matching names do not satisfy identity. Unit formals keep their logical positions. |
| Type-provider composition | Return/pass already-admitted Copy nominal aggregates above sixteen bytes with packed/over-aligned fields, provider-private helpers and local field/array paths. Use separately bound TypeDef providers and independently relocated rows; preserve original and actual origins. Reject incompatible Copy, validity, field, packing/alignment or retained geometry facts. Do not label the FixedArrayCopyWriter TypeDef support gap a new MethodDef decision. |
| Method maps and re-emission | Exercise direct local MethodDef and exact Import tokens, plus a direct expected-stub reference that must still bind. Rewrite/re-emit/reload a source-free multi-Module closure as L1, retaining original stubs/Exports and consistently remapping methods, types, fields, maps, calls and entries. Cross short/long and Body-local index boundaries; never patch matching payload bytes. |
| Exact selection and isolation | A non-L1 provider, even unused, fails L1-operation preflight before indexing. Unknown/altered tokens, F/A2/E1 selection, malformed ownership/maps and unsupported retained sections do not select another decoder. F still rejects an authored-method actual and retains its own NativeImport/NativeAddress admission unchanged. |
| Replacement and snapshot mutation | Compatible fresh private-body/row changes succeed after fresh binding; incompatible or stale closure/plan pairs reject. Later caller-buffer changes cannot change a completed result. Reordering/provider argument order cannot choose a different revision or target. |
| Capacity and failure | Exercise zero/unit inputs, no-extra-provider closure, repeated actual targets, supported large finite graphs, u16 map index/count edges and checked 24-bit global-reference limits through bounded fixtures/preflight. Capacity refusal is not format invalidity or successful truncation. Preserve output sentinels and observe no entry marker on every preparation failure. |
| JIT/AOT lifetime | Observe real aggregate results and mutations from the final source-free closure, including aligned nested calls. Verify fixups/unwind publication and owned failure cleanup/retirement with existing probes; no provider code or registered backing retires early. Keep decoding, binding, re-emission, PE inspection, runtime and platform-unwind evidence separate. |

Use owned headless workers, deadlines, actual result/trace capture, owned-process
termination and wait-for-exit/`TestFileCleanup` where execution needs supervision.
Keep existing visible/hardware opt-ins untouched. Record final patch/compiler/
configuration and exact root/provider selections; distinguish selected, passed,
failed, skipped and incomplete cases. This document runs no compiler tests.

## Prerequisites and remaining decisions

Implementation prerequisites are the shared semantic closure carrier across
binding/traversal/verification, module-context Body reconstruction and method
resolution, complete retained TypeDef-provider integration, multi-Module typed
Standard re-emission, and the explicit L1 native-plan/backend correspondence.
The current local-only reader/writer and F-native plan do not meet those gates.
Fix the TypeDef writer integration under its existing owner rather than creating
a new type-provider contract here.

The remaining approval is this bounded L1 closure/handoff together with its
still-unapproved L1 prerequisites. No additional in-scope byte encoding,
identity relation, provider-selection or native-lifetime policy is left for an
implementation to invent. Public constructor exports, richer Method contracts,
generics/receivers, structural-array call ABI, native invocation, E1 composition,
source method-key authoring/reference APIs and independently precompiled or
live-provider deployment remain outside this decision and retain their owners.
Completing this slice does not establish broader L1 or roadmap acceptance.
