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

Current implementation support, test evidence and remaining gates belong in
the [module](../implementation/modules.md),
[native](../implementation/native.md),
[graph](../implementation/native-graph.md),
[CLI](../implementation/cli-protocol.md) and
[conformance](../implementation/conformance-gaps.md) inventories. This
proposal defines a candidate admission/binding contract; it does not treat an
implementation slice or a passing test report as approval or as full F
support. The
[provider-native-lowering proposal](standard-trrm-provider-native-lowering.md)
owns native realization, actual code/load origin and execution lifetime.

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
bounded admission is defined below; the
[native-lowering candidate](standard-trrm-provider-native-lowering.md#checked-native-plan-handoff-and-origin)
owns native realization of the checked origins.

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

## Bounded F Module closure

The explicitly supplied artifacts form the entire semantic dependency input;
there is no discovery, search path, forwarding protocol or provider package
selection. Validate all supplied artifacts and all their Modules under each
artifact's exact F schema, including uncalled/declaration-only edges and
disconnected populated Modules. The root is selected only from artifact
`input`, by its explicit Module identity. A provider Module is not executed,
merged into the root or re-exported by being present in this closure.

For the bounded callable subset, each participating populated Module has one
noncallable, nonexported pointer anchor, one supported caller Body, and the
following bodyless NativeImport MethodDefs:

| Module role | Owned NativeImports (`O`) | Imported expectations (`I`) | Relevant MethodDef count |
|---|---:|---:|---:|
| Owned-direct | 1 | 0 | 3 |
| Imported consumer | 0 | at least 1 | `I + 2` |
| Mixed provider/consumer | 1 | at least 1 | `I + 3` |

Permitted empty Modules remain subject to ordinary structural/requirement
checks. A selected F Import must resolve to a public **Module-local owned**
NativeImport in the exact producer Module; an imported proxy is never an
Export target. A mixed Module may own one such declaration and retain
expectations to other Modules. This allows finite provider dependency graphs
without forwarding a call or re-exporting another Module's declaration.
No TypeDef, FieldDef/data, PropertyDef, authored-method provider, generic
callable, extra Body/anchor, root initialization schedule or general linker
is admitted by this shape.

Each caller has exactly one existing E9 NativeAddress formation, selecting
one owned NativeImport or imported expectation through its checked
MethodMap/Body record. Other retained Imports need not occur in its
MethodMap/CallSites and still require complete admission. Preserve the
existing Effects masks (`04` for NativeImport, `00` for caller and anchor),
the F `000E` contract and each Module's governing-edition/capability closure.
This adds no nonempty raw-output support or new Feature/catalog.

Each expected MethodDef has exactly one original Import. An Import's
ModuleRef owner must be its using Module. `I` counts retained expected
MethodDefs, not ModuleRefs or distinct provider identities; distinct Imports
may reuse a ModuleRef/provider, but each original expectation is checked.
Multiple views of one expected definition, duplicate Import triples and
unowned/unused ModuleRefs reject. Let `M` be the ModuleRef count: require
`M=0` when `I=0`, otherwise `1<=M<=I`, and every ModuleRef is used by at least
one Import. This selects no two-provider, 16-provider or arbitrary depth cap;
the existing checked row/count/backend resources still apply, and capacity
exhaustion fails instead of truncating closure.

**Current lowering-name bound:** in any participating Module with more than
one callable declaration (`O+I>1`), require ordinally distinct method names
among those declarations and the caller; the anchor is not a callable name.
This is an implementation admission limit caused by the present
`RootTargetName`/`function.Name` locator, not metadata identity or a general
source-language uniqueness rule. Do not rename/coalesce declarations to
make a collision pass. Names may repeat across different Modules, where exact
Module identities/Export keys keep them distinct. The single-declaration
routes gain no new name bound.

The finite Module graph may contain chains, diamonds and cycles. Check every
edge, including uncalled edges, after the complete input directory is known.
Reaching a previously visited Module is not success for another incoming
expectation; memoization may reuse only the same full original/actual
comparison pair and context. A self-edge is admissible only when a distinct
expected Import resolves to a public owned NativeImport in that same Module;
an imported-only Module cannot satisfy its own expectation. These are
metadata dependency cycles, not call forwarding, recursive type identities
or authorization for initialization/cleanup cycles.

Do not introduce a provider-count, graph-depth or two-provider cap in this
candidate. Existing checked artifact-wide row/count, Body-local map and
backend/resource limits remain in force. A finite closure that exceeds those
resources fails explicitly; a traversal cutoff cannot mark unvisited edges
valid. Reuse an actual Module snapshot for repeated ModuleRefs, but retain and
compare each original expectation separately.

This closure and the distinct-name bound are still unapproved candidate
admission choices. The two shorter
[multiple-root](standard-trrm-multiple-root-method-imports.md) and
[provider-closure](standard-trrm-provider-dependency-closure.md) decision
records preserve their distinct scope/approval history; the rules above are
the single complete implementation-facing admission owner.

## Provider identity and F binding

Build one unambiguous directory from the decoded Module identities of
`input` and every explicitly supplied provider artifact. Each identity may
occur once across that set; duplicate selections reject even for equal
bytes. A diamond reuses the one supplied Module snapshot; it does not supply
two revisions and let traversal order decide. All supplied artifacts are
selected inputs and must pass admission, not merely the first matching one.
Provider authentication remains the host/environment's responsibility.

For every retained ModuleRef/Import, including co-located and declaration-only
uses in every Module of the supplied closure, not only the selected root:

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
   Never redirect an edge to a terminal leaf or treat a provider's own
   ModuleRef as forwarding/re-export.
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
| Bounded Module shapes | Exercise owned-direct (`O=1,I=0`), imported (`O=0,I>=1`) and mixed (`O=1,I>=1`) Modules with the exact anchor/caller/bodyless-import MethodDef counts. Empty Modules still undergo structural/requirements checks. No extra Bodies, anchors or non-MethodDef Imports are admitted. |
| Multiple root expectations | Root retains at least two distinct MethodDef Imports while one E9/NativeAddress selects one exact expected target; an uncalled second Import is still resolved and checked. Reuse one ModuleRef or use separate rows for the same provider, retaining separate originals. Expanded multi-callable Module names obey the explicit implementation bound; equal names across Modules remain distinct. |
| Provider-owned dependency closure | Assemble `Root -> P -> Q` with P's selected Export targeting an owned local NativeImport and P retaining an uncalled Import to Q. Check every original edge and exact provider-owned target. No re-export, forwarding or dropped edge. |
| Diamonds, cycles and self-edge | Reuse one snapshot in a diamond; permit a finite cycle/self-edge only when every expectation resolves to a distinct public owned NativeImport as specified. Check every incoming comparison, including in-progress/visited nodes; imported-only self-binding and proxy Export reject. |
| Missing/wrong identity | Missing P, wrong Module/key/case, two artifacts defining P, imported-proxy Export, anchor target, data import and equal-ABI non-NativeImport reject before code/image loading or execution. Equal DLL/export strings in distinct P/Q do not coalesce origins. |
| Complete original expectations | Independently vary convention, library/entry, resolution/policy, pointer access/extent/retention, output type/state/selector/phase/dependencies and caller obligations. Reject each incompatible provider without overwriting expectations. A stronger ordinary guarantee may satisfy its directed relation but cannot erase the exact foreign protocol. |
| Replacement and snapshot isolation | A/B retain different valid expectations for P/K and reuse one supplied P snapshot; check both. Reject a replacement satisfying only one, address-only/uncalled mismatches and stale cached acceptance after bytes/schema/binding change. |
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
