# Provider binding for direct native-output imports

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** Every recommendation below, including routing,
thunks, records, version selection and exclusions, is a provisional candidate,
not approval or evidence of implementation support.

The revision-92 inner selection below is this proposal's standalone
composition. When the scalar frozen-value persistence candidate is selected,
its proposed revision 94 intentionally supersedes only that inner selection;
the outer version-3 provider-binding table and leaf-thunk requirements remain
unchanged.

## Decision and scope

Keep a native module import bound to its **selected provider DLL/export**.
When that provider owns a published bodyless `DirectNativeImport`, emit a real
provider export consisting of an ABI-transparent leaf jump to its declared
foreign import. Do not redirect the consumer around the selected provider.
Retain the consumer's expected foreign descriptor in a small native-container
table so replacement cannot silently change it.

This applies to synchronous direct calls with the existing admitted private
Windows x64 direct-import signature, with or without NativeOutput cell
contracts.
Preserve the exact revision-92 inner schema selected by
[private NoThrow retirement](no-throw-private-persistence.md) and
[aggregate-output persistence](aggregate-native-output-private-persistence.md).
In particular, retain its declaration-owned output collection, roles,
selector/state partition, dependencies, reservation tag 41 and remapping
rules unchanged. Those proposals remain unapproved. No source production,
attribute/API schema, standard TRRM record, native profile or calling
convention is approved or allocated here.

The new expectation table requires **candidate outer version 3** for `TNPK`,
`TNSP` and `TNGP`. This intentionally supersedes only the prerequisite's
unchanged-version-2 native framing recommendation for this integration;
it does not amend revision 92 or reinterpret any version-2 bytes.

## Evidence and contract owners

These are source observations, not newly run tests or native qualification.

| Source owner | Relevant fact |
|---|---|
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `CreateReference`, `Declaration`, `CreateConsumer` | Reference projection retains the direct-import declaration and marks `IsModuleReference` in memory. Consumer creation distinguishes it from a source-authored direct import; the latter still rejects a provider-name collision. Imported `ModuleFunction` records subsequently have no `Function` payload. |
| `Contract`, `PutFunction`, `GetFunction` in the same file | The ordinary signature string does **not** retain the direct-import library/export or its `NativeParameter` vector. Those are in a stored function's direct-import payload. The normalized output collection alone cannot preserve the missing native descriptor on an imported expectation. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | `IsModuleReference` is internal in-memory state, not a persisted provider identity. `BoundModuleImport` separately names producer/export/target/policy. Neither flag nor matching function names can substitute for retained binding data. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), `CreateConsumer`, `BindModuleImportBindings`, `ProducerImageExports` | Module imports are bound to `terrum-<identity>.dll` and their selected export names. Producer image validation expects the semantic export names to be actual PE exports. |
| [`NativeEmitter.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs), `EmitLibraryCore`, `Exportable`, `QualifyDirectNativeImports`; [`PeImage.cs`](../../sources/Terrum.CodeAnalysis/Native/PeImage.cs), `BuildCore` | An external declaration is not an accessible library export; PE exports require emitted source offsets. Direct foreign imports instead receive their declared DLL/export, with the current private SystemDirectory path qualification. Merely relaxing `Exportable` would not emit an export implementation. |
| [`NativeModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.Graph.cs), `NativeBindings`, `DirectNativeBindings`; [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs), [`GraphJit`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs) | Provider declarations/machine imports and direct foreign bindings are separate routes. Graph JIT re-lowers only the root and uses precompiled provider DLLs. |
| [`NativeModuleImageValidator.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleImageValidator.cs), `CheckImports`, `CheckExports`, `CheckUnwind` | Images are checked against expected DLL/symbol sets. Exports currently must name saved-frame source entries; the validator has no admitted leaf export-thunk case. |
| [`NativeOutputModulePersistenceTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs) | Active test source covers imported direct-output binding, changed output contracts and source-authored collisions. It does not establish the missing native export path. |

The accepted owners remain
[native roles and image admission](../specs/native.md),
[binding and replacement](../specs/representation/binding-and-replacement.md),
[consumer verification](../specs/representation/reader-trust.md),
[private persistence](../specs/representation/persistence.md),
[module scopes](../specs/modules.md), [sets](../specs/module-sets.md),
[graphs](../specs/native-graph.md), and
[Windows x64 deployment](../specs/targets/windows-x64-deployment.md).
Preserve their identity, unsafe, ABI, output, lifetime and before-use
obligations. This proposal owns only the additional private package mapping
and expectation representation. No normative text is changed.

## Candidate image binding

For a consumer-local import `F` selecting provider identity `P` and export
key `K`, use exactly this chain:

```text
consumer call F
    -> consumer IAT: terrum-P.dll / K
    -> P's real .text export entry for K
    -> P's IAT: SystemDirectory(declared foreign DLL) / declared foreign export
```

`P/K` is the semantic provider selection; the foreign DLL/export is its
retained native target. They are distinct identities. Equal foreign targets
do not merge providers or discharge an import of one through another.
Retain the existing private restriction that the local name and export key
coincide; this notation and the record below introduce no alias syntax.

Only a **provider-owned**, public, otherwise exportable top-level or
static-member direct-import declaration is eligible. It remains bodyless and
caller-unsafe in semantic metadata. A module-import proxy is not
provider-owned and cannot itself be exported as a forwarder. A graph
intermediate may publish its own direct import, or call another provider's
declaration from an owned implementation; it cannot re-export the imported
expectation.

The generated entry is exactly a six-byte Windows x64 leaf:
`FF 25 disp32`, an indirect RIP-relative jump to the corresponding eight-byte
IAT cell. Check displacement arithmetic and image bounds. It leaves argument
and nonvolatile registers, stack locations, return address and control modes
unchanged. It neither creates a semantic `BoundFunction` body nor invents a
saved-frame unwind record. The platform leaf-unwind rule applies. Distinct
exported declarations have their own export-entry association even if their
IAT target is shared.

The jump passes the original arguments, including the actual reserved output
storage address, and returns the foreign result directly to the caller.
Publication/cancellation/exceptional indeterminacy remains at the original
invocation under revision 92. There is no second reservation or publication,
temporary aggregate, copy-back, initialization, marshalling, result conversion,
cleanup adapter, exception catch or revived NoThrow promise.

An exported thunk roots its foreign IAT entry even when no provider body calls
that declaration. Compute producer foreign imports from both ordinary
retained call/address uses and these export roots; source-call enumeration
alone is insufficient. Consumer imports of `F` remain provider bindings and
must not also enter `DirectNativeBindings`. Consumer-owned source imports
continue to use their separate direct route and collision checks.

The selected bridge still describes the prototype's private SystemDirectory
IAT route. That physical behavior is not a source resolution value and is not
equivalent to Link, Dynamic/Default or Dynamic/CompatibilitySearch. Candidate
97's normalized resolution and convention are retained in the importing
declaration under [NativeImport resolution private persistence](native-import-resolution-private-persistence.md);
compare both from the original expectation rather than inferring them from an
outer row, IAT or replacement. This bridge does not implement those source
mappings by relabeling SystemDirectory lookup. Unsupported target,
convention or resolution requirements reject.

## Candidate outer expectation table

The table is necessary even with a provider thunk: after imported declarations
lose their function payload, revision 92 cannot compare the original foreign
descriptor with a replacement. Inferring that expectation from the
replacement would make the comparison vacuous.

Use the current version-2 layout with its version byte changed to **3** and
exactly this mandatory insertion, including an empty count:

| Container owner | Insertion immediately before embedded `Contract` bytes |
|---|---|
| `NativeModuleCompiler.cs`, `TNPK` `Read`/`Write` | After the existing machine-import table. A standalone producer has count zero because it has no selected module dependencies. |
| `NativeModuleCompiler.ModuleSets.cs`, `TNSP` `ReadSet`/`WriteSet` | After the complete dependency/declaration/machine-import lists. |
| `NativeModuleCompiler.GraphPackages.cs`, `TNGP` reader/writer | After the complete type-origin table, for both root consumers and intermediate producers. |

All integers are little-endian. `String` is the existing outer
`u16 byteLength` followed by strict UTF-8, not the inner i32-length string.
The exact new layout is:

```text
u16 directExpectationCount
DirectExpectation[directExpectationCount]

DirectExpectation:
    String localFunctionName
    String providerIdentity
    String providerExportKey
    String foreignLibraryName
    String foreignExportName
    u16 nativeParameterCount
    NativeParameter[nativeParameterCount]

NativeParameter:                   // 14 bytes
    u32 formalOrdinal
    i32 pointeeType
    u8 access
    u8 retention
    i32 extent
```

The containing image/module owns the table. Each row references one imported
declaration in that image's embedded Contract, **not** a definition or a
provider-local function index. Require one row per module-selected direct
import, including unused declarations and imports without outputs, and no
rows for ordinary provider functions, vtable methods or consumer-owned
source imports. An output-bearing row requires the unchanged declaration
collection's direct-import role. No output facts are duplicated here.

Require unique rows sorted ordinally by `localFunctionName`,
`directExpectationCount <= 1024` and no more rows than retained imports.
Names/keys and foreign library/export names are nonempty and at most 256
bytes; provider identity uses the existing 1–64-byte ASCII identity grammar.
Apply existing private symbol and direct-library validation, including no
path in `foreignLibraryName`. Cross-check provider identity/key against the
enclosing dependency owner; for TNPK the identity must equal its sole binding.
Current local-name/key equality remains required. Conflicting, duplicate,
or orphan rows reject rather than selecting one by order.

`nativeParameterCount` is bounded by the explicit formal count and existing
256-slot limit. Rows use zero-based logical explicit formals, with no implicit
receiver, in strictly increasing ordinal order. Reuse the current private
`NativeParameterAccess` and `NativeParameterRetention` encodings and validity
rules: Read/Write/ReadWrite, NoEscape, nonnegative extent, exact pointee/type
and access eligibility, and no overlap with annotated output formals.
Zero rows means no such input-parameter annotations, not unknown annotations.
Unknown or unsupported values reject. Existing independent call-argument
limits still apply.

`pointeeType` belongs to the **importing embedded Contract's** private type
universe, including its structure, native-pointer and function-pointer tables.
It is not a provider row or a standard TypeSpecRef. The outer row carries no
source resolution identity. Candidate 97 obtains that fact from the original
importing declaration; the bounded Windows x64/SystemDirectory IAT route
above is a physical prototype behavior, not an implementation of any
normalized source mode. Affected native use rejects unless the selected
target mapping satisfies the retained resolution contract.

Keep existing header fields, dependency/import/origin records, image-payload
framing and SHA-256 coverage rules otherwise unchanged. ContractLength still
counts only the exact revision-92 Contract; ImageLength still counts only the PE.
The insertion contributes to outer payload bounds and the checksum, not
either length field. Preserve the existing total/semantic size limits.
There is no optional trailer, guessed alternate layout or native-profile
label selecting the new table.

Current candidate writers/readers select exact outer **3** and inner **92**;
old version-2 artifacts must be regenerated, not header-patched. No historical
decoder or automatic converter is required. This is an implementation-private
schema choice under [exact draft agreement](../specs/representation/format-versioning.md),
not a standard version or Feature assignment.

## Candidate binding and image checks

Perform the following before emission or affected image loading:

1. Decode/admit the exact outer and inner schemas and all retained requirements.
   Resolve each provider identity/key uniquely within the selected closure.
   Use ordinal case-sensitive package identity; separately reject actual
   target-path collisions. No same-named alternative or provider-order fallback.
2. Check the expectation against that selected declaration: existing complete
   signature/layout/ownership/unsafe/target/runtime obligations, **exact**
   normalized output-role/collection equality under revision 92, and the
   new row's direct-import role, normalized resolution/convention facts and
   native descriptor. Compare library names ordinal-ignore-case under the
   existing Windows DLL mapping, export names ordinally, and native
   parameters by corresponding logical formals and resolved complete
   pointee identities, access, retention and extent.
   A role, foreign target or input-contract change rejects even when machine
   carriers and output selectors happen to match.
3. Retain and use the original expectations after resolution. Reconstruct
   provider-bound in-memory state from checked import ownership plus the outer
   table, not a serialized authorization Boolean. Do not replace expectations
   with stronger facts copied from a replacement before comparing them.
4. Validate actual stored images, not only emitted binding lists. Consumer
   machine imports must still be owned by retained declarations and exactly
   match the recorded provider-DLL IAT sets, including semantic calls and
   implicit required calls. A declaration-only dependency may have no IAT
   entry but still requires every semantic/native expectation check.
5. Validate each provider's complete declared PE export set. Ordinary exports
   retain existing saved-frame checks. A direct-import export must point into
   executable `.text` at the declared six-byte leaf, whose checked displacement
   identifies the exact IAT slot for that declaration's qualified foreign
   target. Reject `.edata` forwarder strings, jumps to another symbol,
   runtime/helper/funclet entries and out-of-range or overlapping entry extents.
   Keep ordinary unwind checks intact; explicitly permit a direct-import-only
   provider with leaf exports and the existing runtime prelude but no
   saved-frame source function. Do not fabricate unwind entries to satisfy
   the old source-function-count gate.
6. Validate provider foreign IAT sets against ordinary uses plus thunk export
   roots, alongside independently required runtime imports. Recompute all
   outer lengths/checksums when writing. Compatible replacement does not pin
   a provider image hash, but must pass both retained contract checks and
   validation of its own current image.

These checks establish structural consumption and selected binding, not
semantic proof of the foreign implementation or arbitrary native bodies.
Foreign output truth remains an authored unsafe promise. A valid checksum,
matching export name or successful OS load proves none of that truth.
Foreign-library availability and exact symbol resolution must still succeed
under the selected loader contract before invocation; the package table is
not evidence that the foreign symbol exists or implements the claimed ABI.

## Consumption modes, remapping and exclusions

* **Private semantic modules:** a `.trm` producer can retain a bodyless direct
  import as a semantic export. Linking/restoration checks the selected
  declaration and exact output collection, then semantic lowering can invoke
  the selected foreign import directly. It does not acquire a PE export.
  Revision 92 remains unchanged and alone does not promise retention of the
  imported foreign descriptor. Creating a native expectation table requires
  the original complete expected declaration before that information is
  discarded. Reject promotion of an already-erased imported expectation;
  do not manufacture it from the replacement or a matching DLL name.
* **TNPK/set AOT:** capture rows from the checked reference-bearing program
  before `CreateConsumer`/`CreateConsumerAgainstReference` drops function
  payloads. Single-provider and set consumers use the same provider-thunk
  route. Keep each retained provider even if it has no consumer IAT entry.
* **Graphs:** apply this to every direct edge, including intermediates and
  TNPK leaves. Graph import classification must distinguish owned direct
  declarations from module-selected ones; `DirectNativeImport != null` alone
  cannot decide ownership. Preserve exact type/cleanup origins and separate
  using expectations in diamonds. Existing unsupported duplicate-name
  selections remain rejected; this proposal adds no ambiguity resolver.
* **Graph root-only JIT:** validate all rows/edges and provider images first,
  restore the root's expected facts, and bind its emitted fixups to the same
  provider DLL/export sets as AOT. Load the exact selected precompiled
  providers; do not regenerate them or redirect the root to foreign symbols.
  An unbound module reference remains invalid for standalone direct-native
  JIT. Ordinary semantic linking/JIT is the distinct consumption scope above.
* **Lifetime/failure:** retain selected provider code/unwind/support for all
  uses; its foreign DLL dependency remains valid through the thunk/IAT uses.
  Keep existing worker supervision, runtime-policy and native-unwind gates.
  The leaf adds no frame, failure bridge, unload lease or hot-replacement API.
* **Remapping:** include outer native-parameter pointees in required-type
  discovery and pruning. Encode them only after remapping into the embedded
  Contract's final tables. Rebuild declaration-name/key correspondences,
  pointer/function-pointer and structure mappings, output/dependency formal
  correspondences, reservation pointees, graph origins, lengths and checksums
  together. Never compare raw numeric type IDs or rewrite bytes in place.
* **Explicit exclusions:** no imported-data export, cross-module re-export,
  arbitrary external stub, nondefault unsupported convention/policy, or
  module-selected function-address/indirect-call use of these bodyless
  exports. A retained native-callable address/entry-name requirement on such
  a declaration rejects this bridge rather than inventing pointer identity
  or exporting an unvalidated alias. Existing separately admitted address
  paths are unchanged; plain fnptr cannot erase required output facts.

Implementation touches the existing semantic reference/import classification,
native container readers/writers and binding checks, emitter export/fixup
mapping, PE construction and image validation. It requires no new semantic
expression, output codec or transient verifier-state serialization.
Public support inventories should describe only the subsequently implemented
and qualified paths; this proposal itself establishes none.

## Alternatives and recommendation

* **Consumer bypass to the foreign DLL:** avoids one leaf jump, but still
  needs the retained descriptor table. It also changes persisted machine
  routing, splits native semantic exports from PE exports, and needs an
  admitted no-export/metadata-only provider representation plus corresponding
  set/graph/JIT lifetime rules. Not selected for this bounded integration.
* **PE export forwarder:** not the declared SystemDirectory IAT route, and
  currently not an admitted export/image form. A loader forwarder must not
  silently change foreign lookup or bypass exact target validation.
* **Reject direct-import providers or require authored wrappers:** preserves
  the current emitter limitation but does not complete selected-provider
  native-output adoption. A different authored callable is not automatically
  the direct-import declaration/role against which the consumer was checked.
* **Drop the foreign expectation or infer it at replacement time:** smaller
  bytes, but loses the retained native contract. Not a valid comparison.

Select real provider leaf exports plus the bounded outer expectation table.
It preserves existing provider-DLL routing and uniform native export
validation without changing revision 92. The cost is one indirect leaf jump
and explicit native expectation persistence; no additional policy choice is
left open for this slice.

## Required regression matrix

These are implementation acceptance cases, not passing evidence from this
documentation task. Extend the existing module, native module/set/graph,
graph-JIT and direct-import test homes through public APIs.

| Class | Required observation |
|---|---|
| Positive native provider | A provider exporting only an owned direct-output declaration emits a real leaf export, the correct foreign IAT root and no fake semantic body/unwind range. A mixed ordinary/direct provider preserves both export forms. |
| Positive execution | Headless supervised AOT single-provider, set and graph execution calls through the actual provider entry and writes the caller's reserved storage. Separately exercise root-only JIT with unchanged provider DLLs. Use foreign fixtures admitted by the existing loader contract; do not install DLLs into SystemDirectory or weaken loader gates for tests. |
| Output outcomes | NonZero int32/uint32, negative nonzero success, and NonNegative zero success/negative failure; multiple independent outputs; false after absent/partial/full writes; discarded/saved status and required dependencies. Preserve vacancy/indeterminacy/publication rules exactly. |
| Call/ABI boundaries | Output at register and stack argument positions, mixed scalar/pointer inputs, named/source-order mapping, nested later-argument calls and failure before invocation. Prove no copy-back, repeated argument evaluation or second publication. |
| Retained but uncalled | No consumer IAT entry, but a complete expectation row and selected provider remain required; changed role/output/foreign target still rejects before loading. The provider's published leaf still roots its own foreign IAT entry. |
| Compatible servicing | Change unrelated provider implementation/image bytes and reorder private tables while retaining identity, descriptors and normalized outputs. Consumer/root/intermediate artifacts stay unchanged in AOT and root-only JIT. |
| Incompatible servicing | Reject changed provider identity/key, direct versus ordinary/vtable role, DLL/export, native parameter access/retention/extent/pointee, output selectors/states/formal/type/layout/dependencies, runtime or target. Do not accept a same-named compatible alternative. |
| Ownership and routing | Source-authored direct-import/provider-name collision still rejects. Provider-owned direct imports work at leaves and intermediates; exporting an imported proxy rejects. Distinct providers sharing a foreign target remain distinct; ambiguity and filename collisions are not merged. |
| Remapping | Reorder structures, pointer/function-pointer tables and functions; use unrelated synthetic declarations, nested aggregates and diamond origins. Outer pointees and inner expected/output/reservation types all resolve to the correct original owners. |
| Container boundaries | Empty/full tables; zero/max supported parameter counts; maximum string/count bounds; bad UTF-8, truncation, duplicate/orphan/unsorted rows, invalid formal/type/enum/extent and wrong dependency owner. Recompute checksums on mutations so rejection reaches the intended check. Version 2/unknown outer and unsupported inner schemas reject without retries. |
| Actual PE mismatch | Valid checksums with missing/extra provider/foreign imports, absent/wrong export, wrong IAT displacement, truncated/overlapping thunk, `.edata` forwarder or helper/funclet target reject before affected loading. Ordinary frame/unwind validation is not relaxed by the leaf exception. |
| Excluded paths | Missing original native expectation, unbound module reference, imported-data/re-export/address/entry-alias use or unsupported convention/policy rejects explicitly. Development/standard TRRM do not gain this private representation or a fallback. |

Schema, structural and binding rejection must precede affected image loading
or invocation. OS loading failures remain load failures, not evidence of
semantic admission. Emission, structural validation and execution remain
distinct evidence. Implementation can proceed using this bundle as provisional
direction; approval, final-patch qualification and the broader adoption-plan
acceptance remain separate.
