# Standard TRRM provider-owned dependency closure

**Status: unapproved candidate; provisionally actionable only under the user's
instruction to prepare the smallest missing-contract proposal and continue with
it as provisional direction.** This document is not formal approval, normative
adoption, implementation support or compiler/native qualification.

## Decision and supersession

Admit a complete finite **ModuleRef/MethodDef Import closure** in the bounded
Standard NativeAddress consumer, including dependencies owned by providers.
Each edge still selects an actual **provider-local callable NativeImport**.
That declaration is a binding leaf; its Module need not be a dependency leaf.
For example, `Root -> P -> Q` means two separately checked expectations.
Root's `P/key` target remains P's owned declaration, not Q's declaration or a
re-exported Import. No transitive target forwarding is introduced.

The expanded support promise applies only when every supplied artifact
independently selects exact `trrm/1-draft-foreign-invocation-contracts-1` (F).
Other selected-schema operations keep their existing boundaries; they are not
F aliases or newly supported participants in this closure. Preserve Windows
x64, fail-fast, one NativeAddress target per bounded caller, and the existing
foreign-call, output, entry, export and lifetime policies.

This amends the leaf-only topology and preparation/rejection cases in
[provider-native lowering](standard-trrm-provider-native-lowering.md#bounded-module-closure-and-origin)
and [multiple root Imports](standard-trrm-multiple-root-method-imports.md).
It replaces their root-only edge ownership, three-MethodDef provider bound,
provider-chain/cycle exclusion and, **only for the mixed shape below**, the
mixed-owned/imported and self-binding exclusions. It does not replace their
native realization or the selection/comparison rules in
[selected-provider consumption](standard-trrm-selected-provider-consumption.md).
No accepted specification text changes.

The genuine missing decision is this bounded consumer-admission promise,
particularly the mixed provider shape and cycle handling. Complete retained
binding is already required; implementing a worklist is not a new metadata
contract. Existing ModuleRef, Import, Export, NativeImport, Contract and Body
records represent the recommendation without a new schema identity, record,
ID, alias, public resolver or native package.

| Alternative | Assessment |
|---|---|
| **Bounded declaration-leaf closure — selected** | Add one owned NativeImport alongside retained expectations where needed, retain every edge and validate each Module independently. Generalize topology without merging provider Bodies or changing invocation. |
| Arbitrary semantic/native linking | Authored-method providers, re-export/forwarder APIs, generic/type/data dependencies, Body merging, initialization schedules and independently deployed images need additional admission and realization contracts. None is necessary to check this retained NativeImport closure. |

## Owners and inspected gap

The [specification index](../specs/README.md) identifies these preserved owners:

| Owner | Controlling obligation |
|---|---|
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [binding and replacement](../specs/representation/binding-and-replacement.md) | Artifact-wide rows and owning contexts; exact decoded producer/key resolution; public local Exports, every original expectation and complete directed satisfaction; context-aware graph comparison and remapping. |
| [Exact schemas](../specs/representation/format-versioning.md), [admission](../specs/representation/requirements-and-admission.md), [manifests](../specs/representation/semantic-manifests.md), [consumer checking](../specs/representation/reader-trust.md) | Independently selected immutable inputs, each Module's exact edition/features/requirements and complete retained closure before use; mandatory safe consumption, not mandatory re-proof of producer Bodies. |
| [Modules](../specs/modules.md), [Windows x64 deployment](../specs/targets/windows-x64-deployment.md), [resource accounting](../specs/representation/resource-accounting.md) | Semantic relowering is distinct from precompiled composition; finite dependency cycles do not authorize execution/initialization cycles, unload or omitted obligations; capacity is separate from validity. |
| [F composition](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities), [NativeImport pointer contracts](standard-native-import-function-pointer-contracts.md) | Unapproved prerequisites, unchanged exact foreign projection and target-to-view relation, `000E`/`16/1`, seven-byte pointer TypeSpec, E9/record-0D and independent raw-output gates. |

Direct source inspection, not new execution evidence, establishes the gap:

* [`StandardConsumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs):
  `LoadStandardProgramCore` binds artifact-wide MethodDef Imports as root-owned;
  `ValidateStandardInputModules` rejects non-root callable-provider dependencies.
  The separate A2 type-only exception does not admit F MethodDef chains.
* `ValidateNativeImportProvider` in that file requires three provider methods,
  reconstructs the provider with no selected Import map, and uses reverse
  definition-to-key lookup. Closure preparation must instead validate the actual
  owned declaration and all provider-side expectations in their own contexts,
  using the existing exact key-to-definition lookup.
* [`NativeAddressReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs)
  admits several exact-F declarations only when all are imported expectations.
  [`Native binding plan`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs)
  retains root-only Import origins and root-name lowering locators.

The active `standard-native-fnptr-provider-chain-closure` item requires these
gaps to close together. Active
[F tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs)
already check multiple root Imports, uncalled expectations, ModuleRef reuse,
original unsigned extents, non-first/co-located Modules and selected-provider
JIT/PE operations. The
[CLI tests](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmExecution.cs)
cover flat separate/co-located selections and sentinel-output rejection.
Neither establishes the new mixed provider shape or recursive closure.

## Exact bounded Module shape

Every populated Module has exactly one noncallable pointer anchor, one existing
supported F caller Body, and the following bodyless NativeImport declarations:

| Shape | Owned declarations `O` | Expected declarations `I` | MethodDef count |
|---|---:|---:|---:|
| Existing owned-direct | 1 | 0 | 3 |
| Existing imported consumer | 0 | At least 1 | `I + 2` |
| New dependency provider | 1 | At least 1 | `I + 3` |

The new mixed shape is necessary because an Export cannot name an imported
proxy. It has **one**, not arbitrarily many, owned NativeImports. The same
shape applies when that Module is selected as the root of loading, reference
creation or a native operation; artifact position does not change its meaning.
Any selected Import target must be the owned declaration in an owned-direct
or mixed Module. Permitted empty Modules remain empty and undergo existing
structural/requirement checks.

All declarations keep the existing closed, static, receiver-free,
module-level signature/native-role limits. Every expected MethodDef has
exactly one Import and its own original NativeImport/contract rows, no Body,
and a distinct expected DefId. No TypeDef, FieldDef/data, PropertyDef or other
Import kind is added. Anchors remain noncallable, nonaddressable and unexported.
The caller contains exactly one E9 formation selecting one owned declaration
or imported expectation through its valid MethodMap/NativeAddress record; only
an imported target needs that declaration's binding-map entry. Other
expectations need not share its signature or occur in the Body's maps/calls.

Retain the current admitted Effects masks: `04` on NativeImport declarations,
`00` on the caller and anchor, with no nonempty raw-output collection or new
clause admission. Each Module independently admits governing edition `1` and
its exact capability closure, including `16/1` for retained `000E`; another
Module's manifest cannot supply a missing requirement.

Let `M` be the Module's retained ModuleRef count. Require `M = 0` when `I = 0`,
otherwise `1 <= M <= I`; every ModuleRef is used by at least one Import.
Imports may share one ModuleRef or use several rows naming the same producer.
Each repeated row independently retains its target/runtime expectation.
The existing one-Import-per-expected-DefId and duplicate-triple rejection remain;
this adds no multiple views of one expected declaration.

When `O + I > 1`, require ordinally distinct names among those callable
declarations and the caller; the anchor is not a function locator. This extends
the existing multi-Import root's **implementation admission bound** to mixed
Modules, not metadata identity or a general overload prohibition. Existing
single-declaration routes acquire no new name restriction. Different Modules
may use the same method/caller names or export-key spellings.

No new Module/edge/depth cap, private 16-provider limit or Body-map-derived
Import cap is selected. Existing checked artifact-wide 24-bit row/count
limits, Body-local 65,536-entry limits and operation/backend capacity apply.
A finite graph can exceed offered resources and explicitly fail; no depth
cutoff may mark unvisited obligations successful. General signature, output,
effect, implication, target and resolution forms remain independently gated.

## Complete traversal and every-edge validation

Use one checked preparation for `LoadStandard`, `CreateStandardReference`
and the existing three
[Standard native operations](standard-trrm-provider-native-lowering.md#checked-preparation-and-retained-plan):

1. Preflight every exact artifact selection before indexing; capture the input
   list, selections and owned byte snapshots once. Index all Modules and perform
   existing structural, manifest and operation-support admission on all supplied
   inputs, not just root-reachable machine uses. Select the requested root only
   in input artifact `0`, by exact decoded identity.
2. Build one directory with **one snapshot per decoded Module identity** across
   all inputs. Duplicate identities reject within/across artifacts, including
   equal bytes, unused copies and resubmission of the root artifact. A diamond
   reuses one supplied Module; argument order never chooses a revision.
   Identities/keys compare ordinal decoded scalar text without normalization,
   String-encoding dependence or filesystem case folding. Each snapshot retains
   its own schema/catalog, edition, features, target and runtime.
3. Enumerate Imports by the original expected definition's owning Module,
   checking that its ModuleRef has that same owner. Build each Module's complete
   outgoing edge list, including declaration-only/uncalled Imports and all
   retained ModuleRefs. Seed a finite worklist with **all supplied Modules**,
   including disconnected selected Modules; enumerate each node once.
4. For **each original edge**, resolve its exact ModuleRef producer identity,
   target/runtime and Import key to that Module's public local Export of the
   required MethodDef kind. Use key-to-definition lookup, not method-name or
   reverse single-key lookup. Existing legal distinct Export keys for one local
   definition remain exact selections, not inferred aliases. Reject a proxy,
   anchor, data import, authored method or NativeCallable-only declaration as
   the actual target. Its owned NativeImport role must be real, not copied from
   the expectation.
5. Compare the using Module's original expected declaration to that actual
   declaration in their respective signature/Param/TypeSpec/Contract contexts.
   Preserve complete types, modes, caller-unsafety, library, original resolved
   entry name, convention, resolution mode/policy and every normalized `000E`
   fact. Resolve an omitted entry separately from each original declaration
   name, never from an Export key or replacement name. F's
   [20-byte `000E`](standard-native-import-function-pointer-contracts.md#candidate-nativeparameter-row)
   retains formal ordinal, access, NoEscape, extent kind and the **full unsigned
   `u32` element count**, including `FFFF_FFFF`; derive pointee/stride/alignment
   from the complete formal. No signed narrowing, larger private-field codec,
   absent-row default or weakening of the exact foreign protocol is permitted.
   Preserve other supported directed obligations and reject unsupported ones;
   nonempty raw-output collections remain unsupported, never erased.
6. Check each bounded caller/anchor's existing structural and target-to-view
   obligations against its own selected original declaration. An imported
   target uses its **own edge**, not the first binding, current provider extras
   or an outer root expectation. Safely reconstruct provider Modules using
   their complete selected Import maps, not recursive public loads with an
   empty provider list. Keep existing verifier organization; this does not
   require re-proving provider Bodies or foreign behavior.

Reaching an already scheduled/visited Module does not skip another incoming
expectation. Memoization may reuse only the same complete comparison pair,
including both original/actual declarations, owning contexts and selected
rules. Accept cycles, diamonds and repeated references only after **every**
node's local checks and every retained edge succeed; an in-progress node/pair
is not a successful binding.

Cycles may include several providers or a self-edge. A self-edge requires a
distinct expected stub selecting a public **owned** NativeImport in that same
Module; it cannot export/resolve the stub itself. A selected mixed root can
participate under precisely that rule. Artifact index `0` and the selected root
Module row are therefore valid actual origins, not absent-binding sentinels.
Self-selection by an imported-only Module remains unsupported.
These are finite metadata-dependency cycles, not synthesized call forwarders,
recursive structural types or authorization for initialization/cleanup cycles.

## Retained handoff, remapping and failure

Extend the internal immutable binding context/plan, not the public API or wire:

| Retained part | Required correspondence |
|---|---|
| Inputs and Modules | Owned artifact snapshots and independent exact selections; every Module's identity, manifest/requirements and ranges; selected root entry/no-entry and operation context. |
| Every original edge | Using artifact/Module, original Import row, expected MethodDef, original ModuleRef row/identity/key/requirements, complete original native/pointer facts and their signature/type/contract contexts. |
| Selected actual | Provider artifact/Module/MethodDef and exact Export key/definition correspondence, complete admitted NativeImport descriptor/contracts and checked satisfaction under selected rules. |
| Uses and realization | Original declaration-to-bound-function/map correspondence, address/call view, checked actual declaration and eventual foreign import/thunk/code-load origin. Retain edges even when their Modules/Bodies are not lowered. |

Keep originals separate from actual facts. Preserve the closure context in
loaded/reference results needed for subsequent binding and in native preparation;
reference projection still exposes only eligible selected-Module exports.
Exact key-to-definition binding does not add alias/multiple-view source-reference
projection; its existing separate admission limits remain.
The public loaded root retains its ModuleImport markers. Only the checked
internal lowering view realizes bindings; a bare root, copied descriptor or
prior success flag remains insufficient for direct native APIs.

Root lowering consumes root-owned correspondence, not a graph-wide method-name
dictionary. Provider-side names/keys neither rename root functions nor confer
root DLL export eligibility. Determine export eligibility from the root-owned
declaration, not a matching graph key: an eligible owned direct import retains
its existing explicit export thunk even when a self-edge selects it, while the
expected stub remains ineligible. Each edge is keyed by using artifact/Module and
original Import/expected row; shared actual declarations and equal foreign
symbols never coalesce the original obligations or provider origins.
JIT, EXE and DLL emission derive native entries from checked actual declarations
using the existing IAT/usable-entry realization and lifetime rules. Provider
Bodies/entries/initializers are not automatically lowered, invoked or exported.
The graph admits dependencies, not a new activation or load schedule.

Packing/remapping rebuilds Module partitions, ModuleRef/Import/Export and
NativeImport owner references, signatures/Params/Contracts, pointer anchors,
Body/MethodMap/TypeMap/CallSite references, indexes/extents and applicable source
maps/reference slots in their exact contexts. Clone shared structural rows when
one using context changes. Preserve each original edge plus its checked
new-row correspondence; byte substitution, flattened descriptors or replacing
all expectations with one provider contract are not transformations.
Missing/stale/unconsumed correspondence fails before lowering.

A new provider selection requires fresh whole-closure checking and new code.
A compatible private Body change can pass; a deep provider replacement that
breaks P's expectation fails even when Root's expectation of P still matches.
Cache reuse requires unchanged complete snapshots, selections, expectations,
origins and rule/target/runtime context. No replacement of a prepared plan,
unchanged emitted image or escaped address, hot swap or early unload is added.

Preserve the existing
[API/CLI failure contract](standard-trrm-provider-native-lowering.md#failure-and-side-effect-contract):
malformed references/ownership, duplicate Module selections/Import bindings,
missing exact producer/key, wrong actual kind/role and incompatible originals
fail as `InvalidDataException`; schema-valid unsupported shapes/requirements/
relations decline as `NotSupportedException`. Existing bounded unconsumed
ModuleRef rejection, argument/schema preflight, cancellation, I/O and resource
failure categories remain. No new diagnostic ID or inter-error priority is
selected.

All retained edges complete checking **before publishing a program/reference/
plan or lowering**, code generation, native loading, output replacement or
program start. Failure publishes no partial result, preserves CLI sentinel
outputs and tries no alternate provider/key/schema, source or private format.
Admission executes no provider code or foreign lookup. Foreign-symbol
resolution/publication failures remain at the existing native boundary;
emitting a PE establishes neither symbol resolution nor execution.
Output formats, runtime/foreign policies, supervision, Core authority, source
production/re-emission and other schema/native-package routes remain unchanged.

## Required evidence

These are future acceptance cases, not results. Use public APIs and
schema-aware assembled/repacked F fixtures with complete valid contexts,
ranges, references, maps and indexes; broad source production/re-emission is
not a prerequisite. Keep ordinary cases headless and existing native opt-ins.

| Area | Concrete cases and required outcome |
|---|---|
| Chains and selected-input closure | `Root -> P -> Q`, with P owning Root's actual target and an uncalled Import of Q. Retain both originals/origins. Extend beyond two edges; repeat with an unreferenced supplied Module retaining a dependency. Missing/incompatible deep Q rejects before lowering even though Root/P still match. |
| Diamonds and independent views | Root selects P and Q; both select one supplied L. Reuse ModuleRefs, then repeat producer identities in separate ModuleRef rows, same provider/key and legal distinct keys of one owned declaration. Every original expectation survives; one incompatible view/requirement rejects the whole operation. |
| Cycles | P/Q mutual dependencies and a valid self-edge to a distinct owned declaration terminate and pass. A mismatch on the back-edge, unresolved cycle member or proxy Export rejects, never hidden by visited/in-progress state. A mixed selected root can participate; an imported-only root cannot satisfy an incoming actual-target requirement. |
| Exact identity/key/role | Different String encodings of equal decoded identities bind; changed case/normalization does not. Keys differ from expected/provider names; repeated key spelling in distinct Modules stays separate. Duplicate snapshots, including equal/unused copies, wrong owners/kinds, a re-exported stub, anchor or authored/native-callable-only target reject. |
| Complete contracts and boundaries | Data-driven deep-edge changes to library/resolved entry, convention/resolution, complete pointee/access, `000E` presence, NoEscape and extent kind/count reject. Preserve counts `0`/kind-0, `1`, `7FFF_FFFF`, `8000_0000`, `FFFF_FFFF` with valid kind-1 cases; reject invalid zero/minimum and overflowed target calculations. Unsupported raw outputs, features, signatures or extra address targets decline rather than disappear. |
| Shape/name/capacity | Existing direct/single/multi-Import roots and leaf providers retain behavior. Mixed `I=1` and `I>1` pass, with one owned declaration. Multiple owned declarations, extra Bodies/anchors and same-name expanded local declarations decline; equal names across Modules pass. Exercise a chain and fan-in beyond private 16-provider assumptions, uncalled Imports absent from MethodMap, and existing checked row/map/resource boundaries without partial success. |
| Placement and remapping | Run separate, all-co-located and hybrid closures; include non-first root/provider Modules and self/other actual origins in artifact `0`. Reorder artifacts/Modules/declarations/Imports/contracts/Body maps with valid typed remapping and shared-row cloning. Preserve exact originals, selected target and actual origin; reject stale/out-of-owner correspondence. |
| Replacement and snapshots | Fresh compatible deep-provider/private-Body replacement passes with originals intact; incompatible deep replacement fails despite an unchanged outer Export. Caller-buffer mutation cannot change retained results. Reordering/equal foreign symbols cannot revive a stale cached selection. |
| Public/native boundaries and atomicity | Loaded imports remain references; direct native APIs still reject bare imported roots. References/DLL export lists expose no imported proxy or automatic provider entry. A self-edge does not disqualify its independently eligible root-owned export thunk or make the expected stub exportable. All native APIs and CLI build routes reject a deep bad edge before replacing sentinel EXE/DLL outputs or executing an entry. |
| JIT and AOT | For chain, diamond and cycle fixtures, exercise Windows x64 `ExecuteStandard`, supervised emitted EXE execution and invoked no-entry DLL root wrappers, including hybrid/co-located placement and non-first address targets. Exercise existing CLI run/build/library selections without new options. Separately inspect PE imports/exports/fixups and observe the selected native call's result; do not claim provider-Body execution or pointer backing/output behavior not actually exercised. |

Retain final-patch/configuration and exact root/provider input identities with
qualification, and confirm the intended selectors ran without omitted/skipped
coverage. Documentation/link checks are not compiler conformance. No choice
within this bounded recommendation remains unresolved; formal approval and
implementation/final-input qualification remain required. General linking,
broader schema composition and source production/re-emission remain separate.
