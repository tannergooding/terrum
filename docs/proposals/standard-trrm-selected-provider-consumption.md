# Standard TRRM selected-provider consumption

**Status: unapproved candidate; provisionally actionable only under the
user's authorized missing-contract workflow.** This supplies an API/CLI
direction, not formal approval, normative adoption, implementation support
or execution evidence.

## Decision and scope

Add an explicitly selected **Standard semantic consumer** with an explicit
set of provider artifacts. Expose it through `LoadStandard`,
`CreateStandardReference`, and the existing artifact-execution commands.
The first selected schema is exactly
`trrm/1-draft-foreign-invocation-contracts-1` (F), as already defined by
[draft-schema selection](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities).
Every input independently supplies its whole-artifact schema before indexing;
the root also explicitly selects its logical Module. There is no default,
inference, negotiation or fallback in this new surface.

This closes only the consumer-surface deferral in
[source schema selection](standard-trrm-source-schema-selection.md#prospective-cli-transport)
and [draft-schema selection](trrm-draft-schema-profile-selection.md#explicit-caller-selection-and-operation-boundaries).
Preserve the complete existing
[NativeImport pointer-contract candidate](standard-native-import-function-pointer-contracts.md),
including NativeAddress, NativeParameter, raw outputs, original expectations,
actual-target checks and retained code origins. This proposal assigns no
metadata ID, schema identity, record, opcode or compatibility mapping.

The consumption scope is semantic loading and joint lowering of explicitly
supplied `.trrm` semantics, not unchanged precompiled-provider deployment.
Native packages/sets/graphs retain their existing selection, entry and
lifetime contracts; this surface cannot bypass a selected precompiled
provider or silently switch that route to regeneration.

### Proposal placement

| Alternative | Assessment |
|---|---|
| Amend the source-selection proposal | The smallest textual change would replace its compile-only option boundary with consumer APIs and provider arguments. It would share the token rules, but mix output selection, source/Core authority and dependency admission in a document deliberately limited to production. It still needs every consumer decision below. |
| **Separate consumer proposal — recommended** | Keep the producer API, `compile-trrm` behavior and schema owners unchanged. This document owns only the missing consumer transport and applies their existing rules. On adoption, cross-link the source proposal's separate-surface deferral and replace the three commands' fixed-route-only restriction for explicitly selected invocations. |

Neither alternative requires redesigning F or a general resolver, package
manifest, profile algebra, conversion API, ABI or runtime policy.

## Authority and inspected boundaries

The [specification index](../specs/README.md) leads to these controlling owners:

| Owner | Preserved obligation |
|---|---|
| [Format versioning](../specs/representation/format-versioning.md), [persistence](../specs/representation/persistence.md) and [admission](../specs/representation/requirements-and-admission.md) | One exact schema/catalog per artifact; separate input selections; complete retained support before affected use. Shared magic, `(1,0)`, filenames and native containers are not selectors. |
| [Module manifests](../specs/representation/semantic-manifests.md), [ModuleRef/Import/Export](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) and [binding](../specs/representation/binding-and-replacement.md) | Module-owned requirements, exact decoded-text identities, identity before full directed comparison, and preservation of each original expectation. |
| [Consumer verification](../specs/representation/reader-trust.md) | Mandatory structural, support and binding checks, without mandatory semantic re-proof of producer Bodies or foreign behavior. |
| [Modules](../specs/modules.md), [native boundaries](../specs/native.md) and [Windows x64](../specs/targets/windows-x64.md) | Semantic relowering is distinct from precompiled composition; actual native roles, target mappings, unsafe obligations and code lifetime remain required. |
| [CLI protocol](../specs/cli-protocol.md) | Artifact selection is independent of machine-response and execution-supervision selection. The existing experimental `--json` route is not `terrum.cli/1`. |

These are current source observations, not approval or qualification:

| Inspected source | Relevant boundary |
|---|---|
| [`TrRmExecutableCompiler`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`Standard consumer API`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs) | `EmitStandard`, `LoadStandard` and `CreateStandardReference` now use exact F identity input. Public `Load` and `CreateReference(byte[])` still select Development; they remain distinct routes. |
| [`F source-free loader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs) | The bounded loader reconstructs one direct NativeImport, one anchor and one caller Body, and binds one retained MethodDef Import to one selected F provider export. It validates multiple populated Modules of that shape, selects exact non-first root/provider identities and admits one co-located leaf provider in the root artifact. It compares the selected provider key, signature, NativeImport descriptor and complete `000E` rows before verification. Provider chains, extra dependency edges and general remapping remain unsupported. |
| [`TrRmArtifactIndex`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) | The public default selects Standard; explicit internal F and CopyOrigin factories select distinct schemas. The enum is not a public selection protocol. |
| [`NativeEmitter`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs) | Direct-native JIT and library emission still reject `ModuleImport`/module references without explicit module binding. The bounded Standard high-level route now consumes an internal checked plan and lowers one selected provider NativeImport into the admitted foreign import. DLL/export strings supplied to `EmitWithImports` remain no proof of provider compatibility. |
| [`Standard native plan`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs), [`NativeModuleCompiler`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.StandardTrRm.cs) and [`NativeJit`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.StandardTrRm.cs) | A new unapproved, provisionally actionable lowering proposal now has a bounded implementation: the plan retains artifact snapshots, exact schema and Module selections, the original root expectation, and the selected provider MethodDef origin. The public operations route to JIT or complete PE emission without private package conversion. |
| [`TRRM commands`](../../sources/terrumc/Program.TrRmExecution.cs) and [`usage`](../../sources/terrumc/Program.cs) | `compile-trrm` retains `--trrm-schema` as a destination selector. Artifact commands accept exact F `--trrm-schema`, `--trrm-module`, and repeatable `--trrm-provider` selections; both direct-import and one-provider run/build/library routes are wired. Source compilation supplies no explicit `NativeCoreRegistration`. |

Existing implementation and focused tests do not approve this surface or
establish support beyond the recorded F subset.

The current bounded implementation routes the exact selected F root/provider
pair through the internal native binding plan, including a leaf provider
co-located in the root artifact. The
[native-lowering candidate](standard-trrm-provider-native-lowering.md#owners-and-inspected-boundary)
records the latest 27-test Debug F/CLI report and distinguishes co-located
provider JIT/AOT executable coverage from the still-bounded separate-provider
library and broader closure evidence. Input hashes are retained at
`artifacts\\log\\fnptr-fprofile-multimodule-colocated-final-v2-inputs.txt`.
Provider chains, raw-output contracts, co-located library invocation and broad
remapping remain unqualified; the report does not approve the candidate or
complete the larger implementation plan.

## Exact proposed public API

The following are **unapproved C# API candidates** in
`Terrum.CodeAnalysis.Modules`. All arguments are required:

```csharp
public sealed record TrRmStandardArtifactInput(
    string SchemaIdentity,
    ReadOnlyMemory<byte> Artifact);
```

Add these methods to `TrRmExecutableCompiler`:

```csharp
public static BoundProgram LoadStandard(
    TrRmStandardArtifactInput input,
    string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers);

public static CompilationReference CreateStandardReference(
    TrRmStandardArtifactInput input,
    string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers);
```

`input` identifies one complete artifact, not a Module byte slice.
`moduleIdentity` selects exactly one Module **in that artifact**: the root
program/library for `LoadStandard`, or the published declaration projection
for `CreateStandardReference`. It never selects a decoder. Multiple Module
rows do not create multiple schemas; row order selects neither entry nor
provider.

`providers` is the complete, explicitly selected additional artifact set,
not a search catalog. Use an empty list when no external provider is needed.
Each provider carries its own exact schema; neither the root's token nor a
ModuleRef supplies it. No provider path, resolver callback, installed-package
search, native DLL or private package is accepted by these APIs.
A distinct provider Module co-located with the selected root is already part
of `input`; do not resubmit that artifact in `providers`. The
[native-lowering candidate](standard-trrm-provider-native-lowering.md#bounded-module-closure-and-origin)
owns the bounded admission and checked origin handoff for that case.

Schema tokens compare ordinally, case-sensitively and without trimming,
normalization or parsing components. Initially these two operations support
only F. Other defined schemas/composites are known-but-unsupported selections
until their complete operation support is deliberately added. Even common
subset bytes do not permit reducing C+F or F+G to F. Internal enum names,
numbers, `Standard`, `Development`, `F` and `latest` are not aliases.

### Admission and result

1. Validate arguments and preflight **every input's exact selection and
   operation-level structural schema/catalog support before constructing any
   artifact index or table/Body reader**. An unsupported provider selector
   fails here even if that provider would later prove unused. Do not probe a
   default decoder, inspect Features or catch a failed parse to select F.
2. Capture the input list and owned byte snapshots, then index each artifact
   only with its selected schema. Inputs must not be concurrently mutated
   during the call; retained results cannot depend on later caller mutation.
   Keep the same immutable selection through all readers, validators,
   reconstruction, binding, remapping and lowering handoff.
3. Perform mandatory safe-consumption checks and admit the complete retained
   requirements of the supplied artifacts and their Modules. Required
   unsupported semantic uses, target/runtime mappings or missing carriers
   fail before code generation or execution, even when uncalled. Schema
   support preflight is distinct from these content-dependent checks.
4. Bind the complete retained dependency closure under the rules below.
   Publish a program/reference only after the whole operation succeeds.
   Missing dependencies never become empty declarations or functions.

`LoadStandard` reconstructs the selected Module's entry, if any, and the
admitted, explicitly bound semantic closure needed for joint lowering. It
retains selected origins and every original expected contract, including
address-only dependencies. Binding/remapping may resolve an Import to an
actual definition only while preserving those obligations; merely clearing
`ModuleImport` or copying a native descriptor is not a binding proof.

`CreateStandardReference` exposes the selected Module's admitted exports
with complete original declaration contracts and provider origins. It does
not execute Bodies, select a native entry, grant source access or designate
Core authority. Reference projection cannot replace a required constant
program, layout or other published promise with a stub. A missing required
codec/support declines the reference operation rather than dropping facts.

Neither method generates/loads code, runs module initialization or resolves
foreign symbols as part of decoding/comparison. A required initialization
or execution facility without admitted support remains a failure, not a
new activation policy. Cached decisions require the same owned input,
exact schema/catalog, selected binding closure and rule/target/runtime
context; Module text or byte equality alone is not the cache key.

## Provider identity and F binding

Build one unambiguous directory from the decoded Module identities of
`input` and every explicitly supplied provider artifact. Each identity may
occur once across that set; duplicate selections reject even for equal
bytes. A diamond reuses the one supplied Module snapshot; it does not supply
two revisions and let traversal order decide. All supplied artifacts are
selected inputs and must pass admission, not merely the first matching one.
Provider authentication remains the host/environment's responsibility.

For every retained ModuleRef/Import, including co-located and declaration-only
uses:

1. Resolve the exact `producerIdentity`, then its exact `exportKey` and
   required definition kind. Use metadata's ordinal decoded-scalar equality
   without normalization, independent of String encoding and filesystem
   casing. ModuleRef target/runtime fields must match the selected producer.
   Missing Module/key, conflicting selections and wrong kinds fail; do not
   search equal signatures, method names or other providers.
2. Require the Export to name a public provider-local definition, with the
   required owner. An imported expectation/proxy cannot be re-exported as
   that definition. Honor legal explicit export keys, including keys
   different from the MethodDef name; infer no alias or native symbol.
3. Resolve complete types and contract subjects in their original using,
   owner and substitution contexts. Check **every** original expectation
   against the actual selected provider, not a union, weakest expectation
   or replacement-authored expectation. Preserve stronger provider facts
   actually relied upon without substituting them into the original view.

For F NativeAddress and NativeImport uses, additionally apply the
[existing target-to-view relation](standard-native-import-function-pointer-contracts.md#exact-target-to-view-relation):

* The selected actual provider must own an eligible closed, static,
  receiver-free callable NativeImport and its valid native descriptor.
  An equal-shaped authored method, data import, anchor, unbound expectation
  or bodyless declaration without that role is not the actual target.
* Compare the original library/entry, resolution mode and applicable policy,
  effective convention, complete types/layouts, and exact normalized
  NativeParameter/raw-output protocol under the existing owner rules.
  Preserve access, NoEscape, extent kind/count, output state/status partition,
  publication phase and complete dependency sets. Resolve omitted entry
  names in the original declaration context, not from a replacement or key.
  Matching foreign symbols or ABI carriers do not merge provider identities.
* Apply complete directed satisfaction to the other applicable obligations,
  including published caller-unsafety. Check the pointer view against the
  retained original expectation, not extra current-provider facts. Keep
  anchors noncallable and separate actual target identity from structural
  pointer identity.
* Before affected lowering, retain the checked binding and actual code/load
  origin through remapping and address-only reachability. Rebuild contextual
  references/maps rather than patching matching bytes. Native lowering must
  consume that handoff, not fall back to an unbound direct import or a
  caller-supplied DLL/export mapping.

These are structural/support and finite declared-contract checks, **not
mandatory re-proof of provider Bodies**, call-site semantic truth or foreign
behavior. Consumers may rely on producer-published facts after the required
checks; optional deeper audits remain separate. Existing unsafe, backing,
reservation, publication and native resolution/lifetime obligations are not
waived. Actual foreign resolution must succeed at its existing boundary
before NativeAddress publishes a usable pointer, including discard.

## Exact proposed CLI transport

Extend `run-trrm`, `build-trrm` and `build-trrm-library` with:

```text
--trrm-schema <exact-schema-identity>
--trrm-module <exact-root-module-identity>
[--trrm-provider <exact-schema-identity> <provider-path>] ...
```

`--trrm-schema` and `--trrm-module` occur exactly once together on the new
Standard route. Each repeatable `--trrm-provider` consumes exactly two values
and supplies one whole artifact; it has no inherited schema. For example,
this is a proposed invocation, not an implemented/executed command:

```powershell
terrumc run-trrm .\consumer.trrm --runtime fail-fast `
    --trrm-schema trrm/1-draft-foreign-invocation-contracts-1 `
    --trrm-module consumer `
    --trrm-provider trrm/1-draft-foreign-invocation-contracts-1 .\provider.trrm
```

No selection means the existing fixed Development route, unchanged.
Provider/module options without the root schema are usage errors; supplying
providers never implicitly selects Standard or F. With an explicit schema,
construct the corresponding API inputs and call `LoadStandard`, never the
nonselected `Load` or `CreateReference(byte[])`. Validate all option shapes
and schema selections before reading/indexing artifacts. A valid F input
without external dependencies needs no provider option.

Keep required `--runtime fail-fast`, root entry/no-entry checks, library
export rules, output extensions, diagnostic behavior and `--json` placement.
Only the selected root Module supplies the entry/exports; provider entries
are not automatically invoked/exported. Artifact input `-` remains invalid,
including provider inputs. Reject output paths colliding with any input
under the existing Windows path comparison, not just the root path.

`compile-trrm --trrm-schema` retains its existing **destination** meaning and
does not gain these provider/module options. Validators, private-module and
native-package commands keep fixed routes and reject the new options.
Default indexes/writers, Development APIs and historical CopyOrigin routes
are unchanged. Producing F bytes does not make an omitted-selector invocation
consume them as F.

This source-free route does not require Core re-registration. The absent
standalone source `NativeCoreRegistration` input remains a separate source
host/configuration gate under its [owner](native-core-provider-configuration.md);
provider artifacts here cannot authorize Core or fabricate source schemas.

### Failures and side effects

| Condition | Proposed API result; CLI mapping |
|---|---|
| Null input/list/element or schema/module argument | `ArgumentNullException`, identifying the argument/input; CLI missing values are usage errors. |
| Empty/invalid root identity, unknown/altered schema token, or a schema identity not selected by this operation | `ArgumentException`; schema/argument preflight before indexing. CLI option selection/shape errors: exit `2`, `TRM2000`. |
| Empty artifact contents | `InvalidDataException` during selected-schema decoding; no output replacement or program start. |
| A selected schema operation without complete support for its retained content | `NotSupportedException` before affected code generation/loading/execution, including common-subset bytes selected under another profile. CLI exit `1`, `TRM5001`. |
| Requested root Module absent from `input` | `ArgumentException` naming `moduleIdentity`, after safe metadata decoding; no selection from a provider instead. CLI exit `1`, `TRM5001`. |
| Malformed framing/records/references, duplicate Module identities, unresolved dependency/key, incompatible original expectation or invalid native role/protocol | `InvalidDataException`, identifying the offending input/binding; CLI exit `1`, `TRM5001`. |
| Valid retained but unsupported semantics, checking relation, target/runtime use or lowering | `NotSupportedException`; reject before affected code generation/loading/execution. CLI exit `1`, `TRM5001`. Unassigned/malformed encoded values remain invalid-data errors. |
| Input/output I/O, access or decoding failure | Existing CLI exit `2`, `TRM2001`; do not classify missing files as provider compatibility success. |

Reject missing/empty option values, repeated singleton selectors and ineligible
options as usage errors. Repeating provider inputs is not first/last-wins:
decoded identity duplication follows the admission-error row above.
Error text identifies root/provider input and, when established, the
Module/key/obligation; no new diagnostic ID or response field is needed.
Resource/internal failures remain failures, not empty successful programs.

Complete admission/binding and required lowering checks before output
publication or program start. Do not create/replace a destination on those
failures; publish only a completely emitted image. Decoder/binding work
executes no provider code or native loader action. No failure retries another
schema, provider, source compilation, private format or runtime policy.
Actual platform resolution failures retain their native execution boundary;
they never publish a successful null/fabricated pointer.

These mappings apply to the existing experimental worker surface, not adoption
of `terrum.cli/1`. `run-trrm` retains raw program status and diagnostic-only
`--json`; its caller must supervise execution. A machine-envelope/supervisor
migration is outside this decision.

## Required conformance cases

These are future acceptance tests, not results of this documentation change.
Use public APIs, independent byte fixtures and unrelated synthetic headless
providers; test decoding, binding, emission and supervised execution separately.

| Case | Required outcome |
|---|---|
| Explicit F and empty dependency set | A supported local-import F program loads with an explicit empty provider list. Exercise F selection on a supported common-subset program too; no F fact is required merely to select the schema. |
| Routing before indexing | Unknown root/provider tokens, changed case/whitespace, enum names/numbers and known unsupported composites fail preflight even with malformed bytes in another input. No default decoder is constructed or retried. |
| Preserved routes | Existing Standard default index, Development `Load`/`CreateReference`, omitted CLI schema and explicit historical CopyOrigin fixtures keep their original interpretation. F-only records fail those selected non-F schemas; equal valid subset bytes alone cannot prove originating provenance. |
| Actual selected provider | Root imports `P/K`; P owns an eligible NativeImport with NativeParameter or raw-output facts. Exact input selections bind and source-free execution observes the expected result. A legal key different from the method name resolves by key, not name. |
| Missing/wrong identity | Missing P, wrong Module/key/case, two artifacts defining P, imported-proxy Export, anchor target, data import and equal-ABI non-NativeImport reject before code/image loading or execution. Equal DLL/export strings in distinct P/Q do not coalesce origins. |
| Complete original expectations | Independently vary convention, library/entry, resolution/policy, pointer access/extent/retention, output type/state/selector/phase/dependencies and caller obligations. Reject each incompatible provider without overwriting expectations. A stronger ordinary guarantee may satisfy its directed relation but cannot erase the exact foreign protocol. |
| Diamonds and replacement | A/B retain different valid expectations for P/K and reuse one supplied P snapshot; check both. Reject a replacement satisfying only one, address-only/uncalled mismatches and stale cached acceptance after bytes/schema/binding change. |
| Whole artifact and Module selection | Permute Module/table rows; explicitly select the same root identity and preserve behavior. Co-located imports use the same checks as external ones. Missing root, duplicate cross-input identities, malformed uncalled records and unsupported retained Modules cannot be hidden by choosing the first row or trimming. |
| Context and cross-schema boundaries | Independently preflight every provider schema; never inherit F. Known unsupported baseline/C/composite provider selections decline before indexing even for common bytes. Nested/imported pointer types preserve their owning contexts and per-Module requirements; mixed-schema binding is admitted only after deliberate support for every exact schema and the full relation. |
| No semantic re-proof requirement | A structurally valid but semantically false producer promise is not a guaranteed rejection case. Mandatory tests assert structure/support/binding checks; any deeper audit is separately selected/reported and does not prove foreign code. |
| API isolation | Interleave selections and reuse equal bytes under distinct contexts without ambient selection leakage. Mutating caller buffers after success cannot change a returned program/reference or cached binding. Reference creation supplies no Core authorization or executable/native-provider selection. |
| CLI/error atomicity | Exercise all three commands, missing/repeated selectors, provider-without-schema, input I/O, unsupported retained use, root entry/no-entry rules and output/input collision. Sentinel outputs survive rejection, no execution marker appears, and experimental JSON/status behavior is preserved. |
| Final execution gate | Reload source-free F artifacts with real selected providers, transport an address and invoke it under existing supported Windows x64 rules. Cover direct and indirect/address-only edges, multiple call sites and required output outcomes; separately qualify JIT and AOT. Emission or a bounded local round trip is not runtime/provider evidence. |

Formal approval, complete F consumer/reconstruction and binding support,
provider-aware lowering, CLI wiring and final-input qualification remain
separate gates. The new API/CLI recommendation leaves no selector, provider
lookup or fallback policy for implementation to invent. Broader source/Core
configuration, missing independent codecs and precompiled-provider runtime
work remain with their existing owners; this document does not declare the
overall milestone complete.
