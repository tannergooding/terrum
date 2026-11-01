# Standard TRRM multiple root MethodDef Imports

**Status: unapproved candidate; provisionally actionable only under the user's
instruction to prepare a missing-contract proposal and continue with it as
provisional direction.** This is not formal approval, normative adoption,
implementation support or native qualification.

## Decision and supersession

For the bounded Standard NativeAddress consumer, admit multiple retained
**root-owned MethodDef Imports**, each independently bound to an explicitly
selected leaf provider. Retain one caller Body and one pointer anchor, with
one NativeAddress formation selecting one of those imported expectations.
The additional Imports need not be called or addressed.

This replaces only the one-root-MethodDef-Import admission boundary recorded
in [selected-provider consumption](standard-trrm-selected-provider-consumption.md)
and the corresponding single-Import topology, additional-root-import exclusion
and rejection cases in
[provider-native lowering](standard-trrm-provider-native-lowering.md#bounded-module-closure-and-origin).
Their selection, comparison, checked handoff and native-realization rules
remain the owners; this document amends cardinality, not their binding relation.

The expanded admission promise is for exact
`trrm/1-draft-foreign-invocation-contracts-1` (F), Windows x64 and fail-fast.
F and its [schema selection](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities),
[pointer contracts](standard-native-import-function-pointer-contracts.md)
and the two consumer candidates remain unapproved prerequisites.
Existing owned-direct and single-import routes remain unchanged. Already
selected storage/array/exception compositions are not F aliases and acquire
no additional admission promise from this amendment.

No accepted specification text changes: metadata and binding already allow
several expectations to select one or several providers. The smallest coherent
decision is to lift the consumer's cardinality bound, not introduce a resolver,
provider graph, universal foreign descriptor or new public API.

## Authority and implementation observations

The [specification index](../specs/README.md) identifies the preserved owners:

| Owner | Obligation |
|---|---|
| [ModuleRef/Import/Export metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Artifact-wide row identities, owning Module ranges, exact decoded-text producer/key identity, public local Exports, Import uniqueness and original bodyless expectations. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md) | Identity before comparison; every original expectation must succeed, including uncalled/declaration-only dependencies. Explicit distinct Export keys may select one public local definition without creating new nominal identities. |
| [Admission](../specs/representation/requirements-and-admission.md), [consumer checking](../specs/representation/reader-trust.md) and [exact schemas](../specs/representation/format-versioning.md) | Independent whole-artifact selections, safe structural consumption, complete retained requirements and supported bindings before use; no mandatory semantic re-proof of producer Bodies or foreign behavior. |
| [Modules](../specs/modules.md), [native boundaries](../specs/native.md) and [Windows x64 deployment](../specs/targets/windows-x64-deployment.md) | Semantic relowering is not precompiled-provider composition; preserve native roles, caller-unsafety, mappings and actual code/load lifetime. |

Directly inspected source establishes these limitations, not contract decisions
or execution evidence:

| Source | Current boundary relevant to this amendment |
|---|---|
| [`StandardConsumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), `LoadStandardProgramCore` / `ValidateNativeImportProvider` | Now enumerates every root MethodDef Import, validates each exact original ModuleRef/Import expectation against its selected provider, and returns all actual provider bindings. Multiple retained MethodDef Imports are admitted only for exact F; F+S and array-schema operations retain their existing single-MethodDef-Import bound. Each provider remains a three-MethodDef leaf. Separate A2 TypeDef binding paths do not establish general MethodDef provider chains. |
| [`NativeAddressReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs) | Now receives a MethodDef-row-to-`BoundModuleImport` map, validates every selected Import and reconstructs every imported stub with its own retained marker. Exact F admits multiple imports only when every direct NativeImport MethodDef is selected; the single NativeAddress target must resolve through its matching map entry. StorageAddress's existing multiple direct NativeImport reconstruction remains distinct from multi-Import provider binding. |
| [`Native binding plan`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs) | Now retains an immutable array of all provider bindings, including each root Import/ModuleRef/MethodDef row and selected provider artifact/Module/MethodDef origin. Lowering checks root/provider ranges and consumes every retained origin; an uncalled imported stub uses the same checked native binding path. |
| [`CreateLoweringInputs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs) | Still indexes bindings by `RootTargetName` and maps them to `function.Name`. The provisional multi-Import amendment therefore bounds the expanded case to distinct root imported names and a caller name that differs; this is an implementation locator limit, not metadata identity or general overload support. |
| [`Import reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs), [`ModuleRef reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmModuleRefTableReader.cs) and [`Export reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExportTableReader.cs) | F binds one Import per expected DefId; ModuleRefs have no producer-identity uniqueness rule. Exact key-to-definition lookup exists, while definition-to-key lookup declines multiply-keyed definitions. Do not turn that reverse-lookup limitation into a new identity rule. |
| [`NativeAddress producer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs) | Rejects `ModuleImport` on a non-address-target function. Consumer expansion does not establish source production or successful re-emission of the expanded shape. |

The active [F tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs)
include `StandardFReferencePreservesImportedNativeCallContracts`,
`BindsProviderModuleFromMultiplePopulatedModules` and
`LowersProviderModuleCoLocatedWithTheSelectedRoot`.
[CLI tests](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmExecution.cs)
include separate/co-located single-provider routes and sentinel-output rejection.
They are compiled by the current
[test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj);
native cases have Windows x64 assumptions. The provisional multi-Import
implementation's final Debug fast build passed. The combined full
`TrRmForeignInvocationAddressEmitterTests` class and selected separate/
co-located/multi-provider CLI tests passed **131/131** with zero skips at
`artifacts\\tst\\Debug\\standard-multi-import-final2\\standard-multi-import-final2.trx`.
Coverage includes an uncalled second provider, missing/incompatible-provider
rejection, both ModuleRef reuse shapes, same-name multi-import rejection,
three provider imports, CLI run/build and the Windows x64 in-process JIT test
under the existing gate.
These results are implementation evidence only, not formal approval,
same-name lowering, provider-chain or source-production/re-emission support.

## Exact bounded multiplicity and producer identity

1. In the expanded provider-bound root, `N >= 1` retained Import rows name
   `N` distinct root-owned expected MethodDefs, exactly one Import per expected
   declaration. Each expectation has its own callable NativeImport descriptor,
   no Body, no receiver or generics, and the existing admitted signature,
   caller-unsafe and NativeParameter contracts. The root's MethodDef range
   contains exactly these `N` declarations, one noncallable anchor and one
   supported caller: `N + 2` rows. This does not newly admit mixed owned-direct
   and imported declarations in F, extra Bodies or extra anchors.
2. Let `M` be the number of root-owned ModuleRef rows. Require `1 <= M <= N`;
   every Import uses one of them and every retained root ModuleRef is used by
   at least one Import. Multiple Imports **may reuse one ModuleRef**. Do not
   equate Import count, ModuleRef count or distinct-provider count.
   No new two-provider or private-package numeric cap applies; existing checked
   table/range limits, operation support and backend/resource limits still apply.
3. Repeated producer identities in ModuleRef rows are allowed, including in
   the same root, but each row's original target/runtime expectation must match
   the **same one selected producer Module snapshot**. Reuse is not a choice
   between revisions or permission to supply duplicate Modules.
4. Build the existing directory from every Module of the root artifact and
   all explicitly supplied artifacts. A decoded Module identity occurs once
   across that set, including unused Modules; repeated artifacts/equal bytes
   do not waive duplication. Identity/key comparison remains ordinal decoded
   scalar text without normalization or filesystem case folding.
5. Distinct expected MethodDefs may select the same producer/key, or explicit
   different keys of the same public local definition. Check each expectation
   separately; retain each recorded key and the common actual definition origin.
   Infer no alias, alternate key or name match. Identical Import triples and
   conflicting bindings are invalid; multiple Import views of one expected
   MethodDef are not added by this slice.
6. Each selected provider is a distinct logical Module from the root, either
   co-located or supplied separately. It remains the supported three-MethodDef
   leaf shape, with no retained ModuleRef/Import dependencies. Other populated
   F Modules remain supported leaves; permitted empty Modules do not waive
   admission. Neither the new root count nor reuse of a provider admits chains,
   cycles, self-binding or dependencies owned by another Module.

**Provisional name bound:** when `N > 1`, require ordinally distinct decoded
MethodDef names among the imported declarations and caller. The noncallable
anchor is not a function-binding key. The existing `N = 1` route does not gain
this new restriction. This is a deliberate admission bound for the expanded
case, motivated by the current `RootTargetName`/`function.Name` lowering
locator; it is not an accepted metadata uniqueness rule or a change to
declaration identity. Schema-valid same-name multi-import root mapping declines
as unsupported during preparation. Do not silently rename or coalesce
definitions to make it fit.

Retain the checked root MethodDef-row-to-bound-function correspondence and each
actual provider row even within this name bound. Distinct provider Modules may
share method names with each other or the root; their identities/origins still
resolve separately. General row-keyed lowering for same-name root MethodDefs
is neither required nor provisionally approved by this slice.

## Every-Import checking and retained handoff

Use one checked preparation shared by semantic loading/reference creation and
the [three Standard native operations](standard-trrm-provider-native-lowering.md#checked-preparation-and-retained-plan),
without new selectors or a public trust handle:

1. Preflight all input selections before indexing, capture owned snapshots once,
   and perform the existing structural, manifest, Module-directory and support
   checks for all supplied artifacts/Modules. Root selection remains in artifact
   `0`, by its explicitly requested identity, not a matching provider or row order.
2. Enumerate every root-owned Import by its expected definition's owning range,
   not the caller's MethodMap or machine reachability. Check the root name bound
   during preparation, before lowering. Validate its original row, ModuleRef
   owner/requirements, expected MethodDef and native role.
   Resolve that exact producer identity and Export key to the public provider-local
   callable NativeImport of the required owner/kind. An imported proxy, anchor,
   data import or equal-shaped authored method is not that actual definition.
3. Compare each complete original expectation to that selected actual declaration
   under the existing supported relation. Preserve original type/signature/Param
   contexts, caller-unsafety, library and resolved entry, convention, resolution
   mode/policy and exact normalized NativeParameter access, retention, extent
   kind/count and full unsigned extents. Resolve omitted entry names in each
   original declaration context, never from the selected key or replacement name.
   Raw-output collections and other unsupported contracts remain rejection gates,
   not empty collections or weaker guarantees. Do not merge expectations or
   overwrite one with provider facts before comparing it.
4. Reconstruct **all** bodyless expectations with their own original
   `ModuleImport`, module-reference marker, callable/native contracts and root
   MethodDef correspondence, including uncalled ones. Keep the public root intact.
   Publish a loaded program/reference or native-ready plan only after all retained
   bindings succeed; reference projection does not export imported expectations
   or automatically expose another Module's functions.
5. Resolve the caller's single NativeAddress record through its exact MethodMap
   MethodDef/Import token to the corresponding root expected MethodDef, then
   **that declaration's binding**, not the first/last Import, an equal signature
   or a matching foreign symbol. Check the anchor/view against that original
   expectation under the
   [target-to-view relation](standard-native-import-function-pointer-contracts.md#exact-target-to-view-relation).
   Other Imports need not share the anchor's signature. Keep existing supported
   caller/CallSite shapes and root entry/no-entry rules; add no second formation
   target. Saved indirect calls use the retained structural contract, not another
   provider search.
6. Before lowering, require a complete one-to-one correspondence between retained
   root expectations and binding entries. Check every original root origin and
   selected actual origin against captured contexts/ranges, including uncalled
   Imports. Missing, conflicting, stale or unconsumed correspondence fails.
   Within the admitted distinct-name root, name/index locators implement only
   the retained row correspondence, not cross-Module identity. Rewrites rebuild
   affected references/maps and preserve all expectations; a copied descriptor
   or prior success flag is not authority.

Each binding retains at least:

| Side | Retained information |
|---|---|
| Original | Root artifact/schema/Module context, Import row, expected MethodDef, original ModuleRef row/producer/key/requirements and complete supported expected contracts. |
| Selected actual | Owned provider artifact/schema, Module row/identity/requirements, exact Export key and provider-local MethodDef, admitted native descriptor/contracts and checked satisfaction correspondence. |
| Lowering | Mapping of that root declaration and any address/call use to the checked actual and eventual foreign import/thunk/code-load origin. Preserve entries even without a machine call. |

Artifact index `0` is a real co-located provider origin only when its Module
differs from the root; `providers[i]` has artifact index `i + 1`. MethodDef
rows remain artifact-wide and must lie in their own Module's range.
Retain all `N` binding entries even when several share one actual definition.
Equal foreign symbols in distinct provider Modules do not merge semantic origins;
any physical machinery sharing preserves every original obligation.
Only the checked internal lowering view may realize bindings, deriving foreign
entries from the checked actual declarations. Bare loaded roots remain ineligible
for public direct-import native APIs. JIT, EXE and DLL emission use the same
correspondence and existing lifetime/retirement rules.

## Failure semantics and exclusions

Preserve the existing [API/CLI failure contract](standard-trrm-provider-native-lowering.md#failure-and-side-effect-contract).
Malformed references/ownership, duplicate Module identities or Import triples,
missing exact producer/key, wrong actual kind/role and an incompatible original
expectation fail as `InvalidDataException`; valid but unsupported retained
content, comparison or topology declines as `NotSupportedException`.
Argument/schema preflight, cancellation, I/O and resource failures retain their
existing categories. No new diagnostic ID, response field or rejection ordering
between independently invalid inputs is selected.

All retained Imports, including uncalled ones absent from the caller's MethodMap
and CallSites, complete admission/comparison in checked preparation **before
lowering**, code generation, native loading, output replacement or program start.
Failure returns no partial program/plan/image, preserves sentinel outputs and
does not retry another provider/key/schema, source compilation or private format.
Preparation executes no provider Body/initializer or foreign resolution.
Actual native-symbol failures remain at their existing execution boundary;
emission is not proof of symbol resolution or runtime behavior.

Excluded from the expanded shape: TypeDef, FieldDef/data, PropertyDef and any
other non-MethodDef Import; ordinary authored-method or NativeCallable-only
providers; provider-owned dependency edges, chains/cycles/self-binding; additional
anchors/NativeAddress targets/caller Bodies; raw outputs or unsupported signature,
effect, implication, resolution/convention or target forms; general Body merging,
same-name root MethodDef lowering, reference alias projection and automatic
source production/re-emission.
Separate existing A2 type-provider work is neither extended nor revoked.
No schema/record/opcode/allocation, implicit marshalling, registration/Core
authority, runtime policy, startup, supervision, initialization, precompiled
provider selection, replacement or hot-unload behavior changes.

## Contract-to-test matrix

These are required future checks, not results. Use public APIs and independent,
schema-aware assembled/repacked F fixtures with valid ranges, contexts, references,
Body maps and indexes. Do not patch coincident bytes or require the currently
bounded source producer to manufacture the additional Import. Keep native checks
headless and supervised under the existing test patterns.

| Contract | Concrete required case and outcome |
|---|---|
| Multiplicity and uncalled retention | Root has two distinctly named expectations selecting separate `P` and `Q` Modules, with different foreign descriptors; only `P` is addressed/called, and `Q` has no caller MethodMap/CallSite use. Loading retains both original ModuleImports/contracts, and native preparation accepts both. Repeat with three Imports to detect a new accidental two-Import cap. |
| Every original expectation before lowering | With the same unreferenced `Q`, omit its provider, then supply same-identity `Q` with an incompatible library/entry, convention or NativeParameter fact. Require binding rejection identifying `Q` during preparation through loading and all native APIs, not later foreign-symbol or machine-use failure. A compatible private provider-Body change does not alter the retained expectation. |
| Matching call target | Reorder Import/ModuleRef/MethodDef/provider rows and valid maps; select the non-first expectation in the single NativeAddress record with its matching anchor. Observe the selected provider's behavior, never the first binding's; reject wrong-kind/unbound tokens. |
| Name bound versus row identity | Distinct root callable names pass. A schema-valid same-name root overload-shaped fixture declines before lowering, without renaming or merging; same-spelled methods in distinct provider Modules still bind to their own retained rows and identities. |
| ModuleRef reuse and repeated identity | Two distinct expectations reuse one root ModuleRef and one supplied `P` snapshot; repeat with two coherent root ModuleRefs naming `P`. Check both expectations independently. An incompatible requirement or one unsatisfied expectation rejects, not weakest/first-wins acceptance. |
| Exact keys and origins | Use a key different from expected/provider MethodDef names, repeated key spelling in `P`/`Q`, explicit distinct keys for one local provider definition and equal foreign symbols in distinct Modules. Resolve exact recorded keys and retain each original view/actual origin; infer no aliases. |
| Duplicate and ownership rejection | Duplicate `P` within/across artifacts, including equal bytes or an unused copy, duplicate Import triples, conflicting expected bindings, foreign-owned ModuleRefs/expected rows and out-of-owner Export targets reject. A retained unused root ModuleRef remains rejected. |
| Co-location and row independence | Exercise separate, co-located and mixed `P`/`Q` placement, non-first root/provider Modules and a provider MethodDef outside the root's range. Preserve origin `0` versus additional-artifact origins, root entry/no-entry and behavior after schema-aware reordering. |
| Whole selected input admission | Malformed or unsupported uncalled Import/provider/distractor content still rejects. Unsupported exact schema selection fails preflight; no inherited F selector or decoder retry. |
| Remaining boundary | Reject non-MethodDef Imports, a selected proxy/anchor/data/authored/non-NativeImport target, provider chains/cycles/self-binding, a second NativeAddress formation and unsupported raw-output/contracts without dropping them. |
| Public root and checked lowering | Loaded expectations remain module references; direct native APIs still reject them, and library export requests cannot turn either imported expectation into a root-owned export. No provider caller/entry is automatically invoked or exported. |
| Native routes and atomicity | Exercise JIT, emitted EXE execution and an explicitly no-entry DLL's invoked root wrapper with both retained bindings; inspect PE structure separately. Missing/incompatible uncalled `Q` preserves CLI sentinel outputs and produces no execution marker. Emission-only cases are reported as such. |
| Preserved routes and snapshots | Existing zero-edge owned-direct F, single-import separate/co-located F, default/Development and private precompiled-provider routes retain their behavior. Caller-buffer mutation after an operation cannot alter its retained result; new selections are freshly checked, not cached by producer text alone. |

No identity, multiplicity, root-name or call-target decision remains open within
this recommendation. Formal approval and final-input qualification remain
required. Source production/re-emission of non-target ModuleImports, multi-view
reference projection, same-name root lowering, richer schema compositions and
broader provider closure remain separate work, not support inferred from this
admission amendment.
